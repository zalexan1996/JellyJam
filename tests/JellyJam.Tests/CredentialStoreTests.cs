using JellyJam.Core;
using JellyJam.Infrastructure.Windows.Security;

namespace JellyJam.Tests;

public class CredentialStoreTests
{
    [Fact]
    public async Task TokenIsEncryptedAndBoundToAccount()
    {
        var directory = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        try
        {
            var store = new WindowsCredentialStore(directory);
            var configuration = new PlayerConfiguration { ServerUrl = "https://music.example/", UserId = Guid.NewGuid() };
            Assert.Null(await store.LoadAsync(configuration));
            await store.SaveAsync(configuration, "test-token-not-a-real-secret");
            Assert.Equal("test-token-not-a-real-secret", await store.LoadAsync(configuration));
            Assert.DoesNotContain("test-token-not-a-real-secret", await File.ReadAllTextAsync(Path.Combine(directory, "credentials.dat")));
            await Assert.ThrowsAsync<JellyJamException>(() => store.LoadAsync(configuration with { UserId = Guid.NewGuid() }));
            await store.DeleteAsync();
            Assert.Null(await store.LoadAsync(configuration));
        }
        finally { Directory.Delete(directory, true); }
    }
}