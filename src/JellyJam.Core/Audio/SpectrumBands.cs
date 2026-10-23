namespace JellyJam.Core.Audio;

public sealed class SpectrumBands
{
    public const int Count = 32;
    public const float MinimumFrequency = 60;
    public const float MaximumFrequency = 16000;
    private const double MaxFrameSeconds = 0.2;
    private const double AttackSeconds = 0.035;
    private const double ReleaseSeconds = 0.18;
    private const double PeakHoldSeconds = 0.35;
    private const float PeakFallPerSecond = 0.8f;
    private const double DynamicRangeDecibels = 60;
    private readonly float[] levels = new float[Count];
    private readonly float[] peaks = new float[Count];
    private readonly float[] targets = new float[Count];
    private readonly double[] hold = new double[Count];

    public ReadOnlySpan<float> Levels => levels;
    public ReadOnlySpan<float> Peaks => peaks;

    public bool Update(ReadOnlySpan<byte> fft, int samplingRateMillihertz, TimeSpan elapsed)
    {
        ComputeTargets(fft, samplingRateMillihertz / 1000d);
        return Smooth(Math.Clamp(elapsed.TotalSeconds, 0, MaxFrameSeconds));
    }

    private void ComputeTargets(ReadOnlySpan<byte> fft, double sampleRate)
    {
        Array.Clear(targets);
        var upperFrequency = Math.Min(MaximumFrequency, sampleRate / 2);
        var isPowerOfTwo = (fft.Length & (fft.Length - 1)) == 0;
        if (fft.Length < 4 || !isPowerOfTwo || upperFrequency <= MinimumFrequency) return;
        var logarithmicRange = Math.Log(MaximumFrequency / MinimumFrequency);
        for (var bin = 1; bin < fft.Length / 2; bin++)
        {
            var frequency = bin * sampleRate / fft.Length;
            if (frequency < MinimumFrequency || frequency >= upperFrequency) continue;
            var band = Math.Clamp((int)(Math.Log(frequency / MinimumFrequency) / logarithmicRange * Count), 0, Count - 1);
            var real = unchecked((sbyte)fft[bin * 2]);
            var imaginary = unchecked((sbyte)fft[bin * 2 + 1]);
            var magnitude = Math.Sqrt(real * real + imaginary * imaginary) / 128;
            var decibels = 20 * Math.Log10(Math.Max(magnitude, 0.001));
            targets[band] = Math.Max(targets[band], (float)Math.Clamp((decibels + DynamicRangeDecibels) / DynamicRangeDecibels, 0, 1));
        }
    }

    private bool Smooth(double seconds)
    {
        var attack = 1 - Math.Exp(-seconds / AttackSeconds);
        var release = 1 - Math.Exp(-seconds / ReleaseSeconds);
        var changed = false;
        for (var band = 0; band < Count; band++)
        {
            var previous = levels[band];
            var previousPeak = peaks[band];
            levels[band] += (float)((targets[band] - levels[band]) * (targets[band] > levels[band] ? attack : release));
            if (levels[band] < 0.0001f) levels[band] = 0;
            UpdatePeak(band, seconds);
            changed |= previous != levels[band] || previousPeak != peaks[band];
        }
        return changed;
    }

    private void UpdatePeak(int band, double seconds)
    {
        if (levels[band] >= peaks[band])
        {
            peaks[band] = levels[band];
            hold[band] = PeakHoldSeconds;
            return;
        }
        hold[band] -= seconds;
        if (hold[band] <= 0) peaks[band] = Math.Max(levels[band], peaks[band] - (float)seconds * PeakFallPerSecond);
    }

    public void Reset()
    {
        Array.Clear(levels);
        Array.Clear(peaks);
        Array.Clear(targets);
        Array.Clear(hold);
    }
}