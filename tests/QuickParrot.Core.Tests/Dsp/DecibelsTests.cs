using QuickParrot.Core.Dsp;

namespace QuickParrot.Core.Tests.Dsp;

public class DecibelsTests
{
    [Theory]
    [InlineData(-60)]
    [InlineData(-6)]
    [InlineData(0)]
    [InlineData(12)]
    public void AmplitudeAndPower_RoundTrip(double db)
    {
        Assert.Equal(db, Decibels.FromAmplitude(Decibels.ToAmplitude(db)), 1e-9);
        Assert.Equal(db, Decibels.FromPower(Decibels.ToPower(db)), 1e-9);
    }

    [Fact]
    public void KnownValues()
    {
        Assert.Equal(0.5, Decibels.ToAmplitude(-6.0206), 1e-4);
        Assert.Equal(0.5, Decibels.ToPower(-3.0103), 1e-4);
        Assert.Equal(-20, Decibels.FromAmplitude(0.1), 1e-9);
        Assert.Equal(-10, Decibels.FromPower(0.1), 1e-9);
    }

    [Theory]
    [InlineData(0.0)]
    [InlineData(-1.0)]
    [InlineData(double.NaN)]
    public void Silence_IsNegativeInfinity_OrTheFloor(double value)
    {
        Assert.Equal(double.NegativeInfinity, Decibels.FromAmplitude(value));
        Assert.Equal(double.NegativeInfinity, Decibels.FromPower(value));
        Assert.Equal(-120, Decibels.FromAmplitude(value, -120));
        Assert.Equal(-120, Decibels.FromPower(value, -120));
    }

    [Fact]
    public void Floor_OnlyRaisesQuieterLevels()
    {
        Assert.Equal(-20, Decibels.FromAmplitude(0.1, -120), 1e-9);
        Assert.Equal(-120, Decibels.FromAmplitude(1e-9, -120));
        Assert.Equal(-10, Decibels.FromPower(0.1, -120), 1e-9);
        Assert.Equal(-120, Decibels.FromPower(1e-15, -120));
    }
}

public class RaisedCosineTests
{
    [Fact]
    public void At_RisesFromZeroToOneAndBack()
    {
        Assert.Equal(0, RaisedCosine.At(0), 1e-12);
        Assert.Equal(0.5, RaisedCosine.At(0.5), 1e-12);
        Assert.Equal(1, RaisedCosine.At(1), 1e-12);
        Assert.Equal(0, RaisedCosine.At(2), 1e-12);
    }

    [Fact]
    public void Hann_IsPeriodicAndSymmetric()
    {
        var window = RaisedCosine.Hann(16);

        Assert.Equal(0, window[0], 1e-12);
        Assert.Equal(1, window[8], 1e-12);
        for (var i = 1; i < window.Length; i++)
            Assert.Equal(window[i], window[window.Length - i], 1e-12);
    }
}
