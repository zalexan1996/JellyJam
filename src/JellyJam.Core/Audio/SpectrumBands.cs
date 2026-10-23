namespace JellyJam.Core.Audio;

public sealed class SpectrumBands
{
    public const int Count = 32;
    public const float MinimumFrequency = 60;
    public const float MaximumFrequency = 16000;
    private readonly float[] levels = new float[Count];
    private readonly float[] peaks = new float[Count];
    private readonly float[] targets = new float[Count];
    private readonly double[] hold = new double[Count];

    public ReadOnlySpan<float> Levels => levels;
    public ReadOnlySpan<float> Peaks => peaks;

    public bool Update(ReadOnlySpan<byte> fft, int samplingRateMillihertz, TimeSpan elapsed)
    {
        Array.Clear(targets);
        var sampleRate = samplingRateMillihertz / 1000d;
        var upperFrequency = Math.Min(MaximumFrequency, sampleRate / 2);
        if (fft.Length >= 4 && (fft.Length & (fft.Length - 1)) == 0 && upperFrequency > MinimumFrequency)
        {
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
                targets[band] = Math.Max(targets[band], (float)Math.Clamp((decibels + 60) / 60, 0, 1));
            }
        }

        var seconds = Math.Clamp(elapsed.TotalSeconds, 0, 0.2);
        var attack = 1 - Math.Exp(-seconds / 0.035);
        var release = 1 - Math.Exp(-seconds / 0.18);
        var changed = false;
        for (var band = 0; band < Count; band++)
        {
            var previous = levels[band];
            var previousPeak = peaks[band];
            levels[band] += (float)((targets[band] - levels[band]) * (targets[band] > levels[band] ? attack : release));
            if (levels[band] < 0.0001f) levels[band] = 0;
            if (levels[band] >= peaks[band])
            {
                peaks[band] = levels[band];
                hold[band] = 0.35;
            }
            else
            {
                hold[band] -= seconds;
                if (hold[band] <= 0) peaks[band] = Math.Max(levels[band], peaks[band] - (float)seconds * 0.8f);
            }
            changed |= previous != levels[band] || previousPeak != peaks[band];
        }
        return changed;
    }

    public void Reset()
    {
        Array.Clear(levels);
        Array.Clear(peaks);
        Array.Clear(targets);
        Array.Clear(hold);
    }
}