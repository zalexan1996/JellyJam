using JellyJam.Core.Audio;

namespace JellyJam.Tests;

public class SpectrumBandsTests
{
    [Theory]
    [InlineData(44100)]
    [InlineData(48000)]
    [InlineData(22050)]
    public void ToneFallsInExpectedLogarithmicBand(int sampleRate)
    {
        var spectrum = new SpectrumBands();
        var fft = new byte[1024];
        var bin = (int)Math.Round(1000d * fft.Length / sampleRate);
        fft[bin * 2] = 100;
        spectrum.Update(fft, sampleRate * 1000, TimeSpan.FromMilliseconds(50));
        var expected = (int)(Math.Log(bin * (double)sampleRate / fft.Length / 60) / Math.Log(16000d / 60) * SpectrumBands.Count);
        var values = spectrum.Levels.ToArray();
        Assert.True(values[expected] > 0.5);
        Assert.Single(values, value => value > 0);
    }

    [Fact]
    public void SignedRealAndImaginaryComponentsProduceEqualMagnitudes()
    {
        var positive = new SpectrumBands();
        var negative = new SpectrumBands();
        var real = new byte[1024];
        var imaginary = new byte[1024];
        real[20] = 100;
        imaginary[21] = unchecked((byte)(sbyte)-100);
        positive.Update(real, 44100000, TimeSpan.FromMilliseconds(50));
        negative.Update(imaginary, 44100000, TimeSpan.FromMilliseconds(50));
        Assert.Equal(positive.Levels.ToArray(), negative.Levels.ToArray());
    }

    [Fact]
    public void SilenceAndDcDoNotInventActivity()
    {
        var spectrum = new SpectrumBands();
        var fft = new byte[1024];
        fft[0] = 127;
        fft[1] = 127;
        Assert.False(spectrum.Update(fft, 44100000, TimeSpan.FromMilliseconds(50)));
        Assert.All(spectrum.Levels.ToArray(), value => Assert.Equal(0, value));
    }

    [Fact]
    public void PeaksHoldThenDecayAndResetClearsEverything()
    {
        var spectrum = new SpectrumBands();
        var fft = new byte[1024];
        fft[20] = 127;
        spectrum.Update(fft, 44100000, TimeSpan.FromMilliseconds(50));
        var peak = spectrum.Peaks.ToArray().Max();
        spectrum.Update([], 0, TimeSpan.FromMilliseconds(50));
        Assert.Equal(peak, spectrum.Peaks.ToArray().Max());
        Assert.True(spectrum.Levels.ToArray().Max() < peak);
        for (var frame = 0; frame < 100; frame++) spectrum.Update([], 0, TimeSpan.FromMilliseconds(50));
        Assert.All(spectrum.Peaks.ToArray(), value => Assert.Equal(0, value));
        spectrum.Update(fft, 44100000, TimeSpan.FromMilliseconds(50));
        spectrum.Reset();
        Assert.All(spectrum.Levels.ToArray(), value => Assert.Equal(0, value));
        Assert.All(spectrum.Peaks.ToArray(), value => Assert.Equal(0, value));
    }

    [Theory]
    [InlineData(0, 44100000)]
    [InlineData(3, 44100000)]
    [InlineData(1024, 0)]
    [InlineData(1024, -1)]
    public void InvalidFramesStayFiniteAndSilent(int length, int rate)
    {
        var spectrum = new SpectrumBands();
        spectrum.Update(new byte[length], rate, TimeSpan.FromMilliseconds(50));
        Assert.All(spectrum.Levels.ToArray(), value => Assert.Equal(0, value));
    }
}