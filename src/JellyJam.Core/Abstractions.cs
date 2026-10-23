namespace JellyJam.Core;

public interface IJellyfinClient
{
    Task<IReadOnlyList<Track>> GetTracksAsync(CancellationToken cancellationToken);
    Task<PlaybackSource> ResolveAsync(Track track, CancellationToken cancellationToken);
    Task ReleaseAsync(PlaybackSource source, CancellationToken cancellationToken);
}

public interface IAudioPlayer
{
    TimeSpan Position { get; }
    Task PlayAsync(PlaybackSource source, Action onStarted, CancellationToken cancellationToken);
}

public interface IConfigurationStore
{
    Task<PlayerConfiguration?> LoadAsync(CancellationToken cancellationToken = default);
    Task SaveAsync(PlayerConfiguration configuration, CancellationToken cancellationToken = default);
}

public interface ICredentialStore
{
    Task<string?> LoadAsync(PlayerConfiguration configuration, CancellationToken cancellationToken = default);
    Task SaveAsync(PlayerConfiguration configuration, string token, CancellationToken cancellationToken = default);
    Task DeleteAsync(CancellationToken cancellationToken = default);
}

public interface IJellyfinSessionClient : IJellyfinClient, IDisposable
{
    Task<(PlayerConfiguration Configuration, string Token)> AuthenticateAsync(string username, string password, CancellationToken cancellationToken);
    Task LogoutAsync(CancellationToken cancellationToken);
}