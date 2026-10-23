namespace JellyJam.Mobile.Services;

/// <summary>Supplies live frequency-spectrum data for the visualizer.</summary>
public interface ISpectrumSource
{
    int SamplingRateMillihertz { get; }

    /// <summary>Increments whenever the underlying capture is rebuilt, so consumers can discard stale history.</summary>
    int Revision { get; }

    string Status { get; }

    void SetEnabled(bool value);

    void SetVisible(bool value);

    ReadOnlySpan<byte> ReadFft();
}
