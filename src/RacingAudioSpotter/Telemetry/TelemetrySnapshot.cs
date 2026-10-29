namespace RacingAudioSpotter.Telemetry;

/// <summary>The state of the telemetry connection to a simulator.</summary>
public enum TelemetryConnectionState
{
    /// <summary>The simulator is not running, so no telemetry can be read.</summary>
    SimulatorNotRunning,

    /// <summary>The simulator is running but is not yet providing live telemetry.</summary>
    WaitingForSession,

    /// <summary>Live telemetry is being received.</summary>
    Connected,

    /// <summary>The connection has not been closed yet, but data has stopped updating.</summary>
    Stale,
}

/// <summary>
/// A point-in-time view of the telemetry the rest of the application cares about.
/// </summary>
/// <param name="Connection">The connection state.</param>
/// <param name="Proximity">
/// The car proximity state. Only meaningful when <paramref name="Connection"/> is
/// <see cref="TelemetryConnectionState.Connected"/>; otherwise it is
/// <see cref="CarProximityState.Unavailable"/>.
/// </param>
/// <param name="RawProximityValue">
/// The simulator's raw proximity value, kept for diagnostics (for example when the value is
/// <see cref="CarProximityState.Unknown"/>). Null when there is no raw value.
/// </param>
public readonly record struct TelemetrySnapshot(
    TelemetryConnectionState Connection,
    CarProximityState Proximity,
    int? RawProximityValue = null);
