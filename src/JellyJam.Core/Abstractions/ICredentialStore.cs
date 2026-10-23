namespace JellyJam.Core;

/// <summary>Persists the access token in an OS-protected store.</summary>
public interface ICredentialStore
{
    Task<string?> LoadAsync(PlayerConfiguration configuration, CancellationToken cancellationToken = default);

    Task SaveAsync(PlayerConfiguration configuration, string token, CancellationToken cancellationToken = default);

    Task DeleteAsync(CancellationToken cancellationToken = default);
}
