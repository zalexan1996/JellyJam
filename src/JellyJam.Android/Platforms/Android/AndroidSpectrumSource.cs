using Android.Media.Audiofx;

namespace JellyJam.Mobile;

public sealed class AndroidSpectrumSource : IDisposable
{
    private Visualizer? capture;
    private byte[] fft = [];
    private int? sessionId;
    private bool enabled;
    private bool visible;
    private bool unavailable;

    public int SamplingRateMillihertz { get; private set; }
    public int Revision { get; private set; }
    public string Status => !enabled ? "OFF" : sessionId is null ? "IDLE" : unavailable ? "UNAVAILABLE" : capture is null ? "IDLE" : "LIVE";

    public void SetSession(int? audioSessionId)
    {
        var next = audioSessionId is > 0 ? audioSessionId : null;
        if (sessionId == next) return;
        sessionId = next;
        Reconnect();
    }

    public void SetEnabled(bool value)
    {
        if (enabled == value) return;
        enabled = value;
        Reconnect();
    }

    public void SetVisible(bool value)
    {
        if (visible == value) return;
        visible = value;
        Reconnect();
    }

    public ReadOnlySpan<byte> ReadFft()
    {
        if (capture is null) return [];
        try
        {
            if ((int)capture.GetFft(fft) == 0)
            {
                SamplingRateMillihertz = capture.SamplingRate;
                return fft;
            }
        }
        catch (Exception) { }
        unavailable = true;
        ReleaseCapture();
        return [];
    }

    private void Reconnect()
    {
        ReleaseCapture();
        unavailable = false;
        if (!enabled || !visible || sessionId is not > 0) return;
        try
        {
            var range = Visualizer.GetCaptureSizeRange();
            if (range is null || range.Length < 2) throw new NotSupportedException();
            capture = new Visualizer(sessionId.Value);
            var size = Math.Clamp(1024, range[0], range[1]);
            if ((int)capture.SetCaptureSize(size) != 0 || (int)capture.SetEnabled(true) != 0)
                throw new NotSupportedException();
            fft = new byte[size];
            SamplingRateMillihertz = capture.SamplingRate;
        }
        catch (Exception)
        {
            unavailable = true;
            ReleaseCapture();
        }
    }

    private void ReleaseCapture()
    {
        Revision++;
        SamplingRateMillihertz = 0;
        Array.Clear(fft);
        if (capture is null) return;
        try { capture.SetEnabled(false); capture.Release(); }
        catch (Exception) { }
        try { capture.Dispose(); }
        catch (Exception) { }
        capture = null;
    }

    public void Dispose() => ReleaseCapture();
}