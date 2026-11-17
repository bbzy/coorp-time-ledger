using CoopTime.Core;
using Xunit;

namespace CoopTime.Tests;

public sealed class TimingTests
{
    [Theory]
    [InlineData("0", "00:00")]
    [InlineData("0.000001", "00:00")]
    [InlineData("4.999999", "00:00")]
    [InlineData("5", "00:10")]
    [InlineData("9.999999", "00:10")]
    [InlineData("10", "00:10")]
    [InlineData("19.999999", "00:20")]
    [InlineData("20", "00:20")]
    [InlineData("27.5", "00:30")]
    [InlineData("54.999999", "00:50")]
    [InlineData("55", "01:00")]
    [InlineData("1440", "24:00")]
    [InlineData("3999990", "66666:30")]
    [InlineData("6000000000000000000030", "100000000000000000000:30")]
    public void DisplayDoesNotChangeBalance(string exact, string expected)
    {
        GameMinutes balance = decimal.Parse(exact, System.Globalization.CultureInfo.InvariantCulture);
        Assert.Equal(expected, ClockMath.Display(balance));
        Assert.Equal((GameMinutes)decimal.Parse(exact, System.Globalization.CultureInfo.InvariantCulture), balance);
    }

    [Fact]
    public void FractionalMillisecondsAreCarriedInsteadOfDropped()
    {
        ClockMath math = new();
        Assert.Equal(0, math.ToMilliseconds(new GameMinutes(1, 1400), 7000));
        Assert.Equal(1, math.ToMilliseconds(new GameMinutes(1, 1400), 7000));
        Assert.Equal(16, math.ToMilliseconds(ClockMath.ToMinutes(16, 9000), 9000));
        math.Reset();
        Assert.Equal(0, math.ToMilliseconds(new GameMinutes(1, 1400), 7000));
    }

    [Fact]
    public void MillisecondRoundTripsAreExactAcrossCadences()
    {
        ClockMath math = new();
        foreach (int cadence in new[] { 7000, 9000, 11000, 7777 })
        {
            for (int dt = 0; dt < 1000; dt++)
            {
                Assert.Equal(dt, math.ToMilliseconds(ClockMath.ToMinutes(dt, cadence), cadence));
            }
        }
    }

    [Fact]
    public void NativePauseIsNeverOverridden()
    {
        Assert.True(SinglePlayerTiming.IsPaused(new(false, false, false, false, false, true, false)));
    }

    [Fact]
    public void BobberMenuAndMinigameOuterGatesAreIncluded()
    {
        Assert.True(SinglePlayerTiming.IsPaused(new(true, true, false, false, false, true, false)));
        Assert.True(SinglePlayerTiming.IsPaused(new(true, false, true, false, false, true, false)));
        Assert.False(SinglePlayerTiming.IsPaused(new(true, true, false, true, false, true, false)));
        Assert.True(SinglePlayerTiming.IsPaused(new(true, false, true, true, false, true, false, false)));
        Assert.False(SinglePlayerTiming.IsPaused(new(true, false, true, true, false, true, false, true)));
    }

    [Fact]
    public void FocusSettingAndSleepAreIncludedButOrdinaryActionsAreNot()
    {
        Assert.True(SinglePlayerTiming.IsPaused(new(true, false, false, false, true, false, false)));
        Assert.False(SinglePlayerTiming.IsPaused(new(true, false, false, false, false, false, false)));
        Assert.True(SinglePlayerTiming.IsPaused(new(true, false, false, false, false, true, true)));
        Assert.False(SinglePlayerTiming.IsPaused(new(true, false, false, false, true, true, false)));
    }

    [Theory]
    [InlineData(0, true, 200)]
    [InlineData(200, true, 200)]
    [InlineData(300, true, 300)]
    [InlineData(0, false, 0)]
    public void SkullCavernCadencePreservesCustomRates(int original, bool skull, int expected)
    {
        Assert.Equal(expected, SinglePlayerTiming.ExtraMilliseconds(original, skull));
    }
}
