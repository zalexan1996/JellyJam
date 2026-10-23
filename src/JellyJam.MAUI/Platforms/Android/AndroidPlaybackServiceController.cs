using JellyJam.Mobile.Services;

namespace JellyJam.Mobile;

public sealed class AndroidPlaybackServiceController : IPlaybackServiceController
{
    public Task EnsureStartedAsync(CancellationToken cancellationToken) => RadioPlaybackService.EnsureStartedAsync(cancellationToken);

    public void Stop() => RadioPlaybackService.StopPlaybackService();
}
