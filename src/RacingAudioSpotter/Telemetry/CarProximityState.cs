namespace RacingAudioSpotter.Telemetry;

/// <summary>
/// Simulator-independent description of cars alongside the player's car.
/// Each simulator adapter maps its own data onto these values.
/// </summary>
public enum CarProximityState
{
    /// <summary>
    /// The simulator is connected but is not currently reporting proximity,
    /// for example because the player is in the garage, spectating or watching a replay.
    /// </summary>
    Unavailable,

    /// <summary>The simulator does not provide proximity information at all.</summary>
    NotSupported,

    /// <summary>No cars alongside.</summary>
    Clear,

    /// <summary>One car on the left.</summary>
    Left,

    /// <summary>One car on the right.</summary>
    Right,

    /// <summary>Cars on both sides.</summary>
    BothSides,

    /// <summary>More than one car on the left.</summary>
    MultipleLeft,

    /// <summary>More than one car on the right.</summary>
    MultipleRight,

    /// <summary>The simulator reported a value this application does not recognise.</summary>
    Unknown,
}
