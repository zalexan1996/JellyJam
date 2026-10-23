using JellyJam.Core;
using JellyJam.Core.Playback;

namespace JellyJam.Tests;

public class RadioSessionTests
{
    [Fact]
    public async Task RunningSessionAppliesBothModesAtomically()
    {
        var store = new Store { Configuration = Configuration(), Token = "cached-token" };
        await using var session = new RadioSession(store, store, new SilentAudio(), (_, _) => new FakeClient());
        await session.RestoreAsync();
        await session.PlayAsync();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        await session.SetModesAsync(RandomMode.Artist, RepeatMode.Album, timeout.Token);
        Assert.Equal(RandomMode.Artist, session.Snapshot.Random);
        Assert.Equal(RepeatMode.Album, session.Snapshot.Repeat);
        await session.SetModesAsync(RandomMode.None, RepeatMode.Song, timeout.Token);
        Assert.Equal(RandomMode.None, session.Snapshot.Random);
        Assert.Equal(RepeatMode.Song, session.Snapshot.Repeat);
    }

    [Fact]
    public async Task ModesCanBeSavedBeforePlaybackStarts()
    {
        var store = new Store { Configuration = Configuration(), Token = "cached-token" };
        await using var session = new RadioSession(store, store, new SilentAudio(), (_, _) => throw new Exception("No network needed."));
        await session.RestoreAsync();
        await session.SetModesAsync(RandomMode.Artist, RepeatMode.Album);
        Assert.Equal(RandomMode.Artist, session.Snapshot.Random);
        Assert.Equal(RepeatMode.Album, store.Configuration!.Repeat);
    }

    [Fact]
    public async Task CachedTokenRestoresWithoutSendingPasswordOrStartingAudio()
    {
        var store = new Store { Configuration = Configuration(), Token = "cached-token" };
        await using var session = new RadioSession(store, store, new SilentAudio(), (_, _) => throw new Exception("Restore must not need the network."));
        await session.RestoreAsync();
        Assert.True(session.IsSignedIn);
        Assert.Equal("listener", session.UserName);
        Assert.Equal(PlayerStatus.Idle, session.Snapshot.Status);
    }

    [Fact]
    public async Task SignInCachesTokenAndSignOutClearsItEvenWhenServerIsOffline()
    {
        var store = new Store();
        var client = new FakeClient { OfflineLogout = true };
        await using var session = new RadioSession(store, store, new SilentAudio(), (_, _) => client);
        await session.SignInAsync("https://music.example/jellyfin", "listener", "password-in-memory-only");
        Assert.True(session.IsSignedIn);
        Assert.Equal("new-token", store.Token);
        Assert.Equal("https://music.example/jellyfin/", store.Configuration!.ServerUrl);
        await session.SignOutAsync();
        Assert.False(session.IsSignedIn);
        Assert.Null(store.Token);
    }

    [Fact]
    public async Task FailedLoginDoesNotPersistCredentials()
    {
        var store = new Store();
        await using var session = new RadioSession(store, store, new SilentAudio(), (_, _) => new FakeClient { RejectLogin = true });
        await Assert.ThrowsAsync<JellyJamException>(() => session.SignInAsync("https://music.example", "listener", "wrong"));
        Assert.False(session.IsSignedIn);
        Assert.Null(store.Configuration);
        Assert.Null(store.Token);
    }

    private static PlayerConfiguration Configuration() => new() { ServerUrl = "https://music.example/jellyfin/", UserId = Guid.NewGuid(), UserName = "listener" };

    private sealed class Store : IConfigurationStore, ICredentialStore
    {
        public PlayerConfiguration? Configuration { get; set; }
        public string? Token { get; set; }
        public Task<PlayerConfiguration?> LoadAsync(CancellationToken cancellationToken) => Task.FromResult(Configuration);
        public Task<string?> LoadAsync(PlayerConfiguration configuration, CancellationToken cancellationToken) => Task.FromResult(Token);
        public Task SaveAsync(PlayerConfiguration configuration, CancellationToken cancellationToken) { Configuration = configuration; return Task.CompletedTask; }
        public Task SaveAsync(PlayerConfiguration configuration, string token, CancellationToken cancellationToken) { Token = token; return Task.CompletedTask; }
        public Task DeleteAsync(CancellationToken cancellationToken) { Token = null; return Task.CompletedTask; }
    }

    private sealed class SilentAudio : IAudioPlayer
    {
        public TimeSpan Position => TimeSpan.Zero;
        public Task PlayAsync(PlaybackSource source, Action onStarted, CancellationToken cancellationToken) => Task.CompletedTask;
    }

    private sealed class FakeClient : IJellyfinSessionClient
    {
        public bool RejectLogin { get; init; }
        public bool OfflineLogout { get; init; }
        public Task<(PlayerConfiguration Configuration, string Token)> AuthenticateAsync(string username, string password, CancellationToken cancellationToken)
            => RejectLogin ? throw new JellyJamException("Sign-in failed.") : Task.FromResult((Configuration(), "new-token"));
        public Task LogoutAsync(CancellationToken cancellationToken) => OfflineLogout ? throw new HttpRequestException() : Task.CompletedTask;
        public Task<IReadOnlyList<Track>> GetTracksAsync(CancellationToken cancellationToken) => Task.FromResult<IReadOnlyList<Track>>([]);
        public Task<PlaybackSource> ResolveAsync(Track track, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task ReleaseAsync(PlaybackSource source, CancellationToken cancellationToken) => Task.CompletedTask;
        public void Dispose() { }
    }
}