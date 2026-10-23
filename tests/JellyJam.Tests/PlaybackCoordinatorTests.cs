using System.Threading.Channels;
using JellyJam.Core;
using JellyJam.Core.Playback;

namespace JellyJam.Tests;

public class PlaybackCoordinatorTests
{
    [Fact]
    public async Task RapidSkipCancelsOldAudioBeforeStartingNextAndIgnoresStaleCallbacks()
    {
        var tracks = TrackSelectorTests.Library();
        var client = new FakeClient(tracks);
        var audio = new FakeAudio();
        var coordinator = new PlaybackCoordinator(client, audio, tracks, RandomMode.None, RepeatMode.None);
        using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var run = coordinator.RunAsync(cancellation.Token);
        coordinator.Send(PlayerAction.Play);
        var first = await audio.Calls.Reader.ReadAsync(cancellation.Token);
        coordinator.Send(PlayerAction.Next);
        var second = await audio.Calls.Reader.ReadAsync(cancellation.Token);
        Assert.True(first.Token.IsCancellationRequested);
        Assert.Equal(tracks[1].Id, second.Source.ItemId);
        first.Started();
        first.Complete.TrySetResult();
        coordinator.Send(PlayerAction.CycleRepeat);
        await WaitUntilAsync(() => coordinator.Snapshot.Repeat == RepeatMode.Song, cancellation.Token);
        Assert.Equal(tracks[1].Id, coordinator.Snapshot.Track!.Id);
        await cancellation.CancelAsync();
        await run.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(1, audio.MaxActive);
        Assert.Equal(2, client.Released.Count);
    }

    [Fact]
    public async Task NaturalCompletionRepeatsSongButNextAdvances()
    {
        var tracks = TrackSelectorTests.Library();
        var audio = new FakeAudio();
        var coordinator = new PlaybackCoordinator(new FakeClient(tracks), audio, tracks, RandomMode.None, RepeatMode.Song);
        using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var run = coordinator.RunAsync(cancellation.Token);
        coordinator.Send(PlayerAction.Play);
        var first = await audio.Calls.Reader.ReadAsync(cancellation.Token);
        first.Complete.SetResult();
        var repeated = await audio.Calls.Reader.ReadAsync(cancellation.Token);
        Assert.Equal(first.Source.ItemId, repeated.Source.ItemId);
        coordinator.Send(PlayerAction.Next);
        var next = await audio.Calls.Reader.ReadAsync(cancellation.Token);
        Assert.Equal(tracks[1].Id, next.Source.ItemId);
        await cancellation.CancelAsync();
        await run;
    }

    [Fact]
    public async Task FailedRepeatingAlbumTerminatesInsteadOfRetryingForever()
    {
        var tracks = TrackSelectorTests.Library();
        var client = new FakeClient(tracks) { FailResolution = true };
        var coordinator = new PlaybackCoordinator(client, new FakeAudio(), tracks, RandomMode.None, RepeatMode.Album);
        using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var run = coordinator.RunAsync(cancellation.Token);
        coordinator.Send(PlayerAction.Play);
        await WaitUntilAsync(() => coordinator.Snapshot.Status == PlayerStatus.Error, cancellation.Token);
        Assert.Equal(2, client.ResolveCount);
        await cancellation.CancelAsync();
        await run;
    }

    [Fact]
    public async Task SkipCancelsPendingResolution()
    {
        var tracks = TrackSelectorTests.Library();
        var client = new FakeClient(tracks) { DelayFirstResolution = true };
        var audio = new FakeAudio();
        var coordinator = new PlaybackCoordinator(client, audio, tracks, RandomMode.None, RepeatMode.None);
        using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var run = coordinator.RunAsync(cancellation.Token);
        coordinator.Send(PlayerAction.Play);
        await client.Resolving.Task.WaitAsync(cancellation.Token);
        coordinator.Send(PlayerAction.Next);
        var played = await audio.Calls.Reader.ReadAsync(cancellation.Token);
        Assert.Equal(tracks[1].Id, played.Source.ItemId);
        coordinator.Send(PlayerAction.Previous);
        coordinator.Send(PlayerAction.CycleRepeat);
        await WaitUntilAsync(() => coordinator.Snapshot.Repeat == RepeatMode.Song, cancellation.Token);
        Assert.Equal(tracks[1].Id, coordinator.Snapshot.Track!.Id);
        await cancellation.CancelAsync();
        await run;
    }

    private static async Task WaitUntilAsync(Func<bool> condition, CancellationToken cancellationToken)
    {
        while (!condition()) await Task.Delay(10, cancellationToken);
    }

    private sealed class FakeClient(IReadOnlyList<Track> tracks) : IJellyfinClient
    {
        public bool FailResolution { get; init; }
        public bool DelayFirstResolution { get; init; }
        public int ResolveCount { get; private set; }
        public List<PlaybackSource> Released { get; } = [];
        public TaskCompletionSource Resolving { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public Task<IReadOnlyList<Track>> GetTracksAsync(CancellationToken cancellationToken) => Task.FromResult(tracks);
        public async Task<PlaybackSource> ResolveAsync(Track track, CancellationToken cancellationToken)
        {
            ResolveCount++;
            Resolving.TrySetResult();
            if (DelayFirstResolution && ResolveCount == 1) await Task.Delay(Timeout.Infinite, cancellationToken);
            if (FailResolution) throw new JellyJamException("Unavailable", false);
            return new PlaybackSource(new Uri("https://music.example/stream"), "fake", "session", track.Id);
        }
        public Task ReleaseAsync(PlaybackSource source, CancellationToken cancellationToken)
        {
            Released.Add(source);
            return Task.CompletedTask;
        }
    }

    private sealed class FakeAudio : IAudioPlayer
    {
        public sealed record Call(PlaybackSource Source, Action Started, CancellationToken Token, TaskCompletionSource Complete);
        public Channel<Call> Calls { get; } = Channel.CreateUnbounded<Call>();
        private int active;
        public int MaxActive { get; private set; }
        public TimeSpan Position => TimeSpan.Zero;
        public async Task PlayAsync(PlaybackSource source, Action onStarted, CancellationToken cancellationToken)
        {
            MaxActive = Math.Max(MaxActive, ++active);
            var complete = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            onStarted();
            Calls.Writer.TryWrite(new(source, onStarted, cancellationToken, complete));
            try { await complete.Task.WaitAsync(cancellationToken); }
            finally { active--; }
        }
    }
}