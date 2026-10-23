namespace JellyJam.Core;

/// <summary>A Jellyfin client that can also authenticate and end the user's session.</summary>
public interface IJellyfinSessionClient : IJellyfinClient, IDisposable
{
    Task<(PlayerConfiguration Configuration, string Token)> AuthenticateAsync(
        string username, string password, CancellationToken cancellationToken);

    Task LogoutAsync(CancellationToken cancellationToken);
}
