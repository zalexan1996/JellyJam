namespace JellyJam.Core;

/// <summary>Reads the music library and resolves streams from a Jellyfin server.</summary>
public interface IJellyfinClient
{
    Task<IReadOnlyList<Track>> GetTracksAsync(CancellationToken cancellationToken);

    Task<PlaybackSource> ResolveAsync(Track track, CancellationToken cancellationToken);

    Task ReleaseAsync(PlaybackSource source, CancellationToken cancellationToken);
}
