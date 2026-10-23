using System.Security.Cryptography;
using System.Text;
using JellyJam.Core;

namespace JellyJam.Infrastructure.Windows.Security;

public sealed class WindowsCredentialStore(string directory) : ICredentialStore
{
    private readonly string path = Path.Combine(directory, "credentials.dat");

    public async Task<string?> LoadAsync(PlayerConfiguration configuration, CancellationToken cancellationToken = default)
    {
        if (!File.Exists(path)) return null;
        try
        {
            var encrypted = await File.ReadAllBytesAsync(path, cancellationToken);
            var plaintext = ProtectedData.Unprotect(encrypted, Entropy(configuration), DataProtectionScope.CurrentUser);
            try { return Encoding.UTF8.GetString(plaintext); }
            finally { CryptographicOperations.ZeroMemory(plaintext); }
        }
        catch (Exception error) when (error is CryptographicException or IOException or UnauthorizedAccessException)
        {
            throw new JellyJamException("Saved credentials cannot be read for this account. Run 'configure' to reconnect.");
        }
    }

    public async Task SaveAsync(PlayerConfiguration configuration, string token, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(token);
        var plaintext = Encoding.UTF8.GetBytes(token);
        byte[] encrypted;
        try { encrypted = ProtectedData.Protect(plaintext, Entropy(configuration), DataProtectionScope.CurrentUser); }
        finally { CryptographicOperations.ZeroMemory(plaintext); }
        Directory.CreateDirectory(directory);
        var temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            await File.WriteAllBytesAsync(temporary, encrypted, cancellationToken);
            File.Move(temporary, path, true);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }

    public Task DeleteAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        File.Delete(path);
        return Task.CompletedTask;
    }

    private static byte[] Entropy(PlayerConfiguration configuration)
        => SHA256.HashData(Encoding.UTF8.GetBytes($"JellyJam|{configuration.ServerUrl}|{configuration.UserId:D}"));
}