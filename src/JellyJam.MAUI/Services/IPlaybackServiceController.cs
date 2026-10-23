namespace JellyJam.Mobile.Services;

/// <summary>Keeps playback alive while the app is backgrounded (foreground service, background audio mode, etc.).</summary>
public interface IPlaybackServiceController
{
    Task EnsureStartedAsync(CancellationToken cancellationToken);

    void Stop();
}
