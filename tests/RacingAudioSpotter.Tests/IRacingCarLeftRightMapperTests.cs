using IRSDKSharper;
using RacingAudioSpotter.Telemetry;

namespace RacingAudioSpotter.Tests;

public class IRacingCarLeftRightMapperTests
{
    [Theory]
    [InlineData(IRacingSdkEnum.CarLeftRight.Off, CarProximityState.Unavailable)]
    [InlineData(IRacingSdkEnum.CarLeftRight.Clear, CarProximityState.Clear)]
    [InlineData(IRacingSdkEnum.CarLeftRight.CarLeft, CarProximityState.Left)]
    [InlineData(IRacingSdkEnum.CarLeftRight.CarRight, CarProximityState.Right)]
    [InlineData(IRacingSdkEnum.CarLeftRight.CarLeftRight, CarProximityState.BothSides)]
    [InlineData(IRacingSdkEnum.CarLeftRight.TwoCarsLeft, CarProximityState.MultipleLeft)]
    [InlineData(IRacingSdkEnum.CarLeftRight.TwoCarsRight, CarProximityState.MultipleRight)]
    public void Maps_documented_values(IRacingSdkEnum.CarLeftRight iRacingValue, CarProximityState expected)
    {
        Assert.Equal(expected, IRacingCarLeftRightMapper.Map((int)iRacingValue));
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(7)]
    [InlineData(int.MaxValue)]
    public void Maps_unrecognised_values_to_unknown(int rawValue)
    {
        Assert.Equal(CarProximityState.Unknown, IRacingCarLeftRightMapper.Map(rawValue));
    }

    [Fact]
    public void Every_sdk_value_is_mapped_to_a_known_state()
    {
        // Catches a future SDK version adding a CarLeftRight value this mapper doesn't handle.
        foreach (var value in Enum.GetValues<IRacingSdkEnum.CarLeftRight>())
        {
            Assert.NotEqual(CarProximityState.Unknown, IRacingCarLeftRightMapper.Map((int)value));
        }
    }

    [Fact]
    public void Matches_documented_numeric_values()
    {
        // irsdk_CarLeftRight in the iRacing SDK header declares these in order, starting at 0.
        Assert.Equal(CarProximityState.Unavailable, IRacingCarLeftRightMapper.Map(0));
        Assert.Equal(CarProximityState.Clear, IRacingCarLeftRightMapper.Map(1));
        Assert.Equal(CarProximityState.MultipleRight, IRacingCarLeftRightMapper.Map(6));
    }
}
