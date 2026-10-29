using System.Diagnostics;
using IRSDKSharper;

namespace RacingAudioSpotter.Telemetry;

/// <summary>
/// Reads iRacing telemetry through the IRSDKSharper library.
/// </summary>
/// <remarks>
/// IRSDKSharper runs its own background threads: it retries opening iRacing's shared memory
/// every <see cref="IRacingSdk.ConnectionCheckIntervalInMS"/> ms, raises OnConnected when live
/// data arrives, OnTelemetryData once per frame (~60 Hz) and OnDisconnected after 3 seconds
/// without data. This class copies the values we need into fields under a lock so that
/// <see cref="GetSnapshot"/> can be called from any thread.
/// </remarks>
public sealed class IRacingTelemetryAdapter : ITelemetryProvider
{
    private const string CarLeftRightVariable = "CarLeftRight";

    // Name of the iRacing simulator process (not the iRacing UI). Only used to tell
    // "iRacing is not running" apart from "iRacing is running but no session is live yet".
    private const string SimulatorProcessName = "iRacingSim64DX11";

    private static readonly TimeSpan StaleAfter = TimeSpan.FromSeconds(1);
    private static readonly TimeSpan RestartDelay = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan ProcessCheckInterval = TimeSpan.FromSeconds(2);
    private static readonly TimeSpan StopTimeout = TimeSpan.FromSeconds(5);

    private readonly IRacingSdk _sdk = new();
    private readonly object _lock = new();
    private readonly ManualResetEventSlim _sdkStopped = new(initialState: true);

    // Shared between the SDK's telemetry thread and callers of GetSnapshot; guarded by _lock.
    private bool _receivingData;
    private bool _variableAvailable;
    private int _rawCarLeftRight;
    private long _lastFrameTimestamp;
    private bool _disposed;

    // Only touched on the SDK's telemetry thread.
    private IRacingSdkDatum? _carLeftRightDatum;
    private bool _variableProblemReported;

    // Only touched by callers of GetSnapshot.
    private bool _simulatorProcessRunning;
    private long _lastProcessCheckTimestamp;

    public IRacingTelemetryAdapter()
    {
        _sdk.OnConnected += HandleConnected;
        _sdk.OnTelemetryData += HandleTelemetryData;
        _sdk.OnDisconnected += HandleDisconnected;
        _sdk.OnException += HandleException;
        _sdk.OnStopped += HandleStopped;
    }

    public string SimulatorName => "iRacing";

    public event Action<string>? DiagnosticMessage;

    public void Start()
    {
        lock (_lock)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            _sdkStopped.Reset();
            _sdk.Start();
        }
    }

    public TelemetrySnapshot GetSnapshot()
    {
        bool receivingData;
        bool variableAvailable;
        int rawCarLeftRight;
        long lastFrameTimestamp;

        lock (_lock)
        {
            receivingData = _receivingData;
            variableAvailable = _variableAvailable;
            rawCarLeftRight = _rawCarLeftRight;
            lastFrameTimestamp = _lastFrameTimestamp;
        }

        if (!receivingData)
        {
            var connection = IsSimulatorProcessRunning()
                ? TelemetryConnectionState.WaitingForSession
                : TelemetryConnectionState.SimulatorNotRunning;
            return new TelemetrySnapshot(connection, CarProximityState.Unavailable);
        }

        // IRSDKSharper only reports a disconnect after 3 seconds without data;
        // stop presenting the last value as current well before that.
        if (Stopwatch.GetElapsedTime(lastFrameTimestamp) > StaleAfter)
        {
            return new TelemetrySnapshot(TelemetryConnectionState.Stale, CarProximityState.Unavailable);
        }

        if (!variableAvailable)
        {
            return new TelemetrySnapshot(TelemetryConnectionState.Connected, CarProximityState.NotSupported);
        }

        return new TelemetrySnapshot(
            TelemetryConnectionState.Connected,
            IRacingCarLeftRightMapper.Map(rawCarLeftRight),
            rawCarLeftRight);
    }

    public void Dispose()
    {
        lock (_lock)
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            _receivingData = false;
        }

        // Stop() returns immediately and cleans up on another thread; wait for it so the
        // shared memory handle is closed before the process exits.
        _sdk.Stop();
        if (!_sdkStopped.Wait(StopTimeout))
        {
            Report("Timed out waiting for the iRacing SDK to stop.");
        }

        _sdk.OnConnected -= HandleConnected;
        _sdk.OnTelemetryData -= HandleTelemetryData;
        _sdk.OnDisconnected -= HandleDisconnected;
        _sdk.OnException -= HandleException;
        _sdk.OnStopped -= HandleStopped;

        // _sdkStopped is deliberately not disposed: a pending restart task may still wait on it.
    }

    // SDK event handlers. These run on IRSDKSharper's threads and block frame processing
    // while they run, so they only copy values. Any exception thrown here is caught by the
    // SDK, reported through OnException and stops its telemetry thread (see HandleException).

    private void HandleConnected()
    {
        // Variable offsets can differ between sessions, so look the variable up again.
        _carLeftRightDatum = null;
        _variableProblemReported = false;
    }

    private void HandleTelemetryData()
    {
        _carLeftRightDatum ??= FindCarLeftRightVariable();

        int rawValue = _carLeftRightDatum is null ? 0 : _sdk.Data.GetInt(_carLeftRightDatum, 0);

        lock (_lock)
        {
            // Connected only once a frame has actually been read, so a snapshot never
            // reports "connected" without data behind it.
            _receivingData = true;
            _variableAvailable = _carLeftRightDatum is not null;
            _rawCarLeftRight = rawValue;
            _lastFrameTimestamp = Stopwatch.GetTimestamp();
        }
    }

    private void HandleDisconnected()
    {
        lock (_lock)
        {
            _receivingData = false;
            _variableAvailable = false;
        }

        _carLeftRightDatum = null;
    }

    private void HandleException(Exception exception)
    {
        lock (_lock)
        {
            _receivingData = false;
            _variableAvailable = false;
        }

        Report($"iRacing SDK error: {exception.GetType().Name}: {exception.Message}");

        // The SDK thread that raised this has stopped. The library's documented recovery is
        // Stop() then Start(). Stop() is safe to call from a handler, but Start() is not
        // (it waits for the stop to finish), so restart from a separate task.
        _sdk.Stop();
        Report($"Restarting iRacing telemetry in {RestartDelay.TotalSeconds:0} seconds...");
        _ = Task.Run(RestartAfterDelayAsync);
    }

    private void HandleStopped() => _sdkStopped.Set();

    private async Task RestartAfterDelayAsync()
    {
        await Task.Delay(RestartDelay).ConfigureAwait(false);

        if (!_sdkStopped.Wait(StopTimeout))
        {
            Report("The iRacing SDK did not stop; telemetry will not be restarted.");
            return;
        }

        lock (_lock)
        {
            if (_disposed)
            {
                return;
            }

            _sdkStopped.Reset();
            _sdk.Start();
        }
    }

    private IRacingSdkDatum? FindCarLeftRightVariable()
    {
        if (!_sdk.Data.TelemetryDataProperties.TryGetValue(CarLeftRightVariable, out var datum))
        {
            ReportVariableProblemOnce($"Telemetry variable '{CarLeftRightVariable}' was not found in this session.");
            return null;
        }

        if (datum.VarType != IRacingSdkEnum.VarType.Int)
        {
            ReportVariableProblemOnce(
                $"Telemetry variable '{CarLeftRightVariable}' has unexpected type {datum.VarType} (expected Int).");
            return null;
        }

        return datum;
    }

    private void ReportVariableProblemOnce(string message)
    {
        if (!_variableProblemReported)
        {
            _variableProblemReported = true;
            Report(message);
        }
    }

    private bool IsSimulatorProcessRunning()
    {
        // Process lookups are relatively expensive, so only refresh every few seconds.
        if (_lastProcessCheckTimestamp == 0 || Stopwatch.GetElapsedTime(_lastProcessCheckTimestamp) > ProcessCheckInterval)
        {
            var processes = Process.GetProcessesByName(SimulatorProcessName);
            _simulatorProcessRunning = processes.Length > 0;
            foreach (var process in processes)
            {
                process.Dispose();
            }

            _lastProcessCheckTimestamp = Stopwatch.GetTimestamp();
        }

        return _simulatorProcessRunning;
    }

    private void Report(string message) => DiagnosticMessage?.Invoke(message);
}
