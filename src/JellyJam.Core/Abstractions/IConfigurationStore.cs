namespace JellyJam.Core;

/// <summary>Persists non-secret player settings.</summary>
public interface IConfigurationStore
{
    Task<PlayerConfiguration?> LoadAsync(CancellationToken cancellationToken = default);

    Task SaveAsync(PlayerConfiguration configuration, CancellationToken cancellationToken = default);
}
