using System.Security.Cryptography;
using System.Text;
using JellyJam.Core;

namespace JellyJam.Mobile.Services;

public sealed class SecureCredentialStore(ISecureStorage storage) : ICredentialStore
{
    private const string Key = "jellyjam.session.v1";

    public async Task<string?> LoadAsync(PlayerConfiguration configuration, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        try
        {
            var saved = await storage.GetAsync(Key);
            var prefix = Fingerprint(configuration) + ":";
            return saved?.StartsWith(prefix, StringComparison.Ordinal) == true ? saved[prefix.Length..] : null;
        }
        catch (Exception)
        {
            storage.Remove(Key);
            return null;
        }
    }

    public Task SaveAsync(PlayerConfiguration configuration, string token, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return storage.SetAsync(Key, Fingerprint(configuration) + ":" + token);
    }

    public Task DeleteAsync(CancellationToken cancellationToken = default)
    {
        storage.Remove(Key);
        return Task.CompletedTask;
    }

    private static string Fingerprint(PlayerConfiguration configuration)
        => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(configuration.ServerUrl + "|" + configuration.UserId)));
}