using RacingAudioSpotter.Application;
using RacingAudioSpotter.Telemetry;

namespace RacingAudioSpotter.Tests;

public class TelemetryMonitorTests
{
    private static readonly TelemetrySnapshot NotRunning = new(TelemetryConnectionState.SimulatorNotRunning, CarProximityState.Unavailable);
    private static readonly TelemetrySnapshot WaitingForSession = new(TelemetryConnectionState.WaitingForSession, CarProximityState.Unavailable);
    private static readonly TelemetrySnapshot Stale = new(TelemetryConnectionState.Stale, CarProximityState.Unavailable);

    private readonly FakeTelemetryProvider _provider = new();
    private readonly StringWriter _output = new();
    private readonly TelemetryMonitor _monitor;

    public TelemetryMonitorTests()
    {
        _monitor = new TelemetryMonitor(_provider, _output);
    }

    private static TelemetrySnapshot Connected(CarProximityState proximity, int? raw = null) =>
        new(TelemetryConnectionState.Connected, proximity, raw);

    private string[] Lines() =>
        _output.ToString().Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries);

    private void Process(params TelemetrySnapshot[] snapshots)
    {
        foreach (var snapshot in snapshots)
        {
            _monitor.ProcessSnapshot(snapshot);
        }
    }

    [Fact]
    public void Reports_waiting_when_simulator_is_not_running()
    {
        Process(NotRunning);

        Assert.Equal(["Telemetry: Waiting for FakeSim..."], Lines());
    }

    [Fact]
    public void Reports_when_simulator_runs_but_has_no_live_telemetry()
    {
        Process(NotRunning, WaitingForSession);

        Assert.Equal(
            ["Telemetry: Waiting for FakeSim...", "Telemetry: FakeSim is running, waiting for live telemetry..."],
            Lines());
    }

    [Fact]
    public void Does_not_repeat_unchanged_state()
    {
        var clear = Connected(CarProximityState.Clear, 1);
        Process(NotRunning, NotRunning, clear, clear, clear, clear);

        Assert.Equal(
            ["Telemetry: Waiting for FakeSim...", "Telemetry: Connected", "Car proximity: CLEAR"],
            Lines());
    }

    [Fact]
    public void Reports_each_proximity_change()
    {
        Process(
            Connected(CarProximityState.Clear),
            Connected(CarProximityState.Left),
            Connected(CarProximityState.Left),
            Connected(CarProximityState.BothSides),
            Connected(CarProximityState.Right),
            Connected(CarProximityState.MultipleLeft),
            Connected(CarProximityState.MultipleRight),
            Connected(CarProximityState.Clear));

        Assert.Equal(
            [
                "Telemetry: Connected",
                "Car proximity: CLEAR",
                "Car proximity: LEFT",
                "Car proximity: BOTH SIDES",
                "Car proximity: RIGHT",
                "Car proximity: MULTIPLE CARS LEFT",
                "Car proximity: MULTIPLE CARS RIGHT",
                "Car proximity: CLEAR",
            ],
            Lines());
    }

    [Fact]
    public void Reports_unknown_values_with_raw_value_once_per_change()
    {
        Process(
            Connected(CarProximityState.Unknown, 42),
            Connected(CarProximityState.Unknown, 42),
            Connected(CarProximityState.Unknown, 43));

        Assert.Equal(
            ["Telemetry: Connected", "Car proximity: UNKNOWN (raw value 42)", "Car proximity: UNKNOWN (raw value 43)"],
            Lines());
    }

    [Fact]
    public void Distinguishes_unavailable_proximity_from_clear()
    {
        Process(Connected(CarProximityState.Unavailable), Connected(CarProximityState.Clear));

        Assert.Equal(
            [
                "Telemetry: Connected",
                "Car proximity: UNAVAILABLE (simulator spotter inactive, e.g. not on track)",
                "Car proximity: CLEAR",
            ],
            Lines());
    }

    [Fact]
    public void Never_reports_proximity_without_a_live_connection()
    {
        // Even if a provider wrongly attached a proximity value, it must not be shown as current.
        Process(
            new TelemetrySnapshot(TelemetryConnectionState.SimulatorNotRunning, CarProximityState.Clear),
            new TelemetrySnapshot(TelemetryConnectionState.WaitingForSession, CarProximityState.Left));

        Assert.DoesNotContain(Lines(), line => line.StartsWith("Car proximity"));
    }

    [Fact]
    public void Reports_disconnection_and_returns_to_waiting()
    {
        Process(Connected(CarProximityState.Left), NotRunning);

        Assert.Equal(
            ["Telemetry: Connected", "Car proximity: LEFT", "Telemetry: Disconnected", "Telemetry: Waiting for FakeSim..."],
            Lines());
    }

    [Fact]
    public void Reprints_proximity_after_reconnecting_even_if_unchanged()
    {
        Process(Connected(CarProximityState.Clear), WaitingForSession, Connected(CarProximityState.Clear));

        Assert.Equal(
            [
                "Telemetry: Connected",
                "Car proximity: CLEAR",
                "Telemetry: Disconnected",
                "Telemetry: FakeSim is running, waiting for live telemetry...",
                "Telemetry: Connected",
                "Car proximity: CLEAR",
            ],
            Lines());
    }

    [Fact]
    public void Reports_stale_data_and_recovery()
    {
        Process(Connected(CarProximityState.Right), Stale, Stale, Connected(CarProximityState.Right));

        Assert.Equal(
            [
                "Telemetry: Connected",
                "Car proximity: RIGHT",
                "Telemetry: Data stopped updating (proximity unknown)",
                "Telemetry: Connected",
                "Car proximity: RIGHT",
            ],
            Lines());
    }

    [Fact]
    public void Reports_disconnection_after_stale_data()
    {
        Process(Connected(CarProximityState.Clear), Stale, NotRunning);

        Assert.Equal(
            [
                "Telemetry: Connected",
                "Car proximity: CLEAR",
                "Telemetry: Data stopped updating (proximity unknown)",
                "Telemetry: Disconnected",
                "Telemetry: Waiting for FakeSim...",
            ],
            Lines());
    }

    [Fact]
    public async Task RunAsync_starts_provider_writes_diagnostics_and_stops_on_cancellation()
    {
        using var cancellation = new CancellationTokenSource();
        _provider.Snapshot = Connected(CarProximityState.Clear);
        _provider.OnGetSnapshot = count =>
        {
            if (count == 1)
            {
                _provider.RaiseDiagnostic("something happened");
            }

            if (count == 3)
            {
                cancellation.Cancel();
            }
        };

        await _monitor.RunAsync(TimeSpan.FromMilliseconds(1), cancellation.Token);

        Assert.True(_provider.Started);
        Assert.Equal(3, _provider.SnapshotCount);
        Assert.Equal(
            ["[diagnostic] something happened", "Telemetry: Connected", "Car proximity: CLEAR"],
            Lines());

        // The monitor unsubscribes when it stops.
        _provider.RaiseDiagnostic("after shutdown");
        Assert.DoesNotContain("after shutdown", _output.ToString());
    }

    private sealed class FakeTelemetryProvider : ITelemetryProvider
    {
        public TelemetrySnapshot Snapshot { get; set; } = NotRunning;
        public Action<int>? OnGetSnapshot { get; set; }
        public bool Started { get; private set; }
        public int SnapshotCount { get; private set; }

        public string SimulatorName => "FakeSim";

        public event Action<string>? DiagnosticMessage;

        public void Start() => Started = true;

        public TelemetrySnapshot GetSnapshot()
        {
            SnapshotCount++;
            OnGetSnapshot?.Invoke(SnapshotCount);
            return Snapshot;
        }

        public void RaiseDiagnostic(string message) => DiagnosticMessage?.Invoke(message);

        public void Dispose()
        {
        }
    }
}
