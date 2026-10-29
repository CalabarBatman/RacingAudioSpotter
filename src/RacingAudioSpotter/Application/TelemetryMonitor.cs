using RacingAudioSpotter.Telemetry;

namespace RacingAudioSpotter.Application;

/// <summary>
/// Polls a telemetry provider and writes connection and proximity changes to an output.
/// Only changes are written, so a steady state produces no further output.
/// </summary>
public sealed class TelemetryMonitor
{
    /// <summary>
    /// How often the provider is polled. Fast enough for a spotter (iRacing updates at 60 Hz),
    /// slow enough to avoid busy-waiting.
    /// </summary>
    public static readonly TimeSpan DefaultPollInterval = TimeSpan.FromMilliseconds(50);

    private readonly ITelemetryProvider _provider;
    private readonly TextWriter _output;

    private TelemetryConnectionState? _lastConnection;
    private CarProximityState? _lastProximity;
    private int? _lastRawProximityValue;

    public TelemetryMonitor(ITelemetryProvider provider, TextWriter output)
    {
        _provider = provider;

        // Diagnostic messages may arrive on the provider's background threads.
        _output = TextWriter.Synchronized(output);
    }

    /// <summary>Starts the provider and reports changes until <paramref name="cancellationToken"/> is cancelled.</summary>
    public async Task RunAsync(TimeSpan pollInterval, CancellationToken cancellationToken)
    {
        _provider.DiagnosticMessage += WriteDiagnostic;
        try
        {
            _provider.Start();

            using var timer = new PeriodicTimer(pollInterval);
            do
            {
                ProcessSnapshot(_provider.GetSnapshot());
            }
            while (await timer.WaitForNextTickAsync(cancellationToken));
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // Normal shutdown.
        }
        finally
        {
            _provider.DiagnosticMessage -= WriteDiagnostic;
        }
    }

    /// <summary>Compares a snapshot with the previous one and writes anything that changed.</summary>
    public void ProcessSnapshot(TelemetrySnapshot snapshot)
    {
        if (snapshot.Connection != _lastConnection)
        {
            if (IsReceiving(_lastConnection) && !IsReceiving(snapshot.Connection))
            {
                _output.WriteLine("Telemetry: Disconnected");
            }

            _output.WriteLine(DescribeConnection(snapshot.Connection));
            _lastConnection = snapshot.Connection;

            // Forget the last proximity so the current one is printed again once data is live.
            _lastProximity = null;
            _lastRawProximityValue = null;
        }

        if (snapshot.Connection != TelemetryConnectionState.Connected)
        {
            return;
        }

        bool proximityChanged = snapshot.Proximity != _lastProximity
            || (snapshot.Proximity == CarProximityState.Unknown && snapshot.RawProximityValue != _lastRawProximityValue);

        if (proximityChanged)
        {
            _output.WriteLine($"Car proximity: {DescribeProximity(snapshot)}");
            _lastProximity = snapshot.Proximity;
            _lastRawProximityValue = snapshot.RawProximityValue;
        }
    }

    private static bool IsReceiving(TelemetryConnectionState? state) =>
        state is TelemetryConnectionState.Connected or TelemetryConnectionState.Stale;

    private string DescribeConnection(TelemetryConnectionState state) => state switch
    {
        TelemetryConnectionState.SimulatorNotRunning => $"Telemetry: Waiting for {_provider.SimulatorName}...",
        TelemetryConnectionState.WaitingForSession => $"Telemetry: {_provider.SimulatorName} is running, waiting for live telemetry...",
        TelemetryConnectionState.Connected => "Telemetry: Connected",
        TelemetryConnectionState.Stale => "Telemetry: Data stopped updating (proximity unknown)",
        _ => $"Telemetry: Unrecognised connection state {state}",
    };

    private static string DescribeProximity(TelemetrySnapshot snapshot) => snapshot.Proximity switch
    {
        CarProximityState.Clear => "CLEAR",
        CarProximityState.Left => "LEFT",
        CarProximityState.Right => "RIGHT",
        CarProximityState.BothSides => "BOTH SIDES",
        CarProximityState.MultipleLeft => "MULTIPLE CARS LEFT",
        CarProximityState.MultipleRight => "MULTIPLE CARS RIGHT",
        CarProximityState.Unavailable => "UNAVAILABLE (simulator spotter inactive, e.g. not on track)",
        CarProximityState.NotSupported => "NOT SUPPORTED (simulator does not provide this data)",
        _ => $"UNKNOWN (raw value {snapshot.RawProximityValue?.ToString() ?? "none"})",
    };

    private void WriteDiagnostic(string message) => _output.WriteLine($"[diagnostic] {message}");
}
