using IRSDKSharper;

namespace RacingAudioSpotter.Telemetry;

/// <summary>
/// Maps iRacing's <c>CarLeftRight</c> telemetry value (irsdk_CarLeftRight) to <see cref="CarProximityState"/>.
/// </summary>
public static class IRacingCarLeftRightMapper
{
    public static CarProximityState Map(int rawValue) => (IRacingSdkEnum.CarLeftRight)rawValue switch
    {
        // iRacing reports Off when its spotter is inactive (not driving, in the garage, replays...).
        IRacingSdkEnum.CarLeftRight.Off => CarProximityState.Unavailable,
        IRacingSdkEnum.CarLeftRight.Clear => CarProximityState.Clear,
        IRacingSdkEnum.CarLeftRight.CarLeft => CarProximityState.Left,
        IRacingSdkEnum.CarLeftRight.CarRight => CarProximityState.Right,
        IRacingSdkEnum.CarLeftRight.CarLeftRight => CarProximityState.BothSides,
        IRacingSdkEnum.CarLeftRight.TwoCarsLeft => CarProximityState.MultipleLeft,
        IRacingSdkEnum.CarLeftRight.TwoCarsRight => CarProximityState.MultipleRight,
        _ => CarProximityState.Unknown,
    };
}
