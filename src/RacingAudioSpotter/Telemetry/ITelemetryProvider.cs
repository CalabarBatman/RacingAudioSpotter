namespace RacingAudioSpotter.Telemetry;

/// <summary>
/// A source of telemetry from one racing simulator. Implementations hide all
/// simulator-specific SDK types and handle connecting, reconnecting and recovery.
/// </summary>
public interface ITelemetryProvider : IDisposable
{
    /// <summary>Human-readable simulator name, e.g. "iRacing".</summary>
    string SimulatorName { get; }

    /// <summary>
    /// Raised with a short diagnostic message when something noteworthy happens
    /// (an SDK error, a missing variable, ...). May be raised on a background thread.
    /// </summary>
    event Action<string>? DiagnosticMessage;

    /// <summary>Begins waiting for and connecting to the simulator. Returns immediately.</summary>
    void Start();

    /// <summary>Returns the latest telemetry. Cheap enough to call many times per second.</summary>
    TelemetrySnapshot GetSnapshot();
}
