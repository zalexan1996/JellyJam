using JellyJam.Core;
using JellyJam.Core.Configuration;
using JellyJam.Core.Jellyfin;

namespace JellyJam.Tests;

public class ConfigurationTests
{
    [Theory]
    [InlineData("https://music.example/jellyfin", "/Audio/123/stream.mp3", "https://music.example/jellyfin/Audio/123/stream.mp3")]
    [InlineData("https://music.example/jellyfin/", "/jellyfin/Audio/123/stream.mp3", "https://music.example/jellyfin/Audio/123/stream.mp3")]
    [InlineData("https://music.example", "Audio/123/stream.mp3", "https://music.example/Audio/123/stream.mp3")]
    public void StreamUrlsPreserveReverseProxyPrefix(string server, string stream, string expected)
        => Assert.Equal(expected, JellyfinClient.ResolveStreamUri(server, stream).AbsoluteUri);

    [Theory]
    [InlineData("file:///tmp/music")]
    [InlineData("https://user:password@example.com")]
    [InlineData("https://example.com?api_key=secret")]
    [InlineData("https://example.com#fragment")]
    public void UnsafeServerUrlsAreRejected(string address)
        => Assert.Throws<ArgumentException>(() => ServerAddress.Normalize(address));

    [Fact]
    public void CrossOriginStreamsAreRejected()
        => Assert.Throws<JellyJamException>(() => JellyfinClient.ResolveStreamUri("https://music.example", "https://other.example/stream"));

    [Fact]
    public async Task ConfigurationRoundTripAndCorruption()
    {
        var directory = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        try
        {
            var store = new JsonConfigurationStore(directory);
            Assert.Null(await store.LoadAsync());
            var configuration = new PlayerConfiguration { ServerUrl = "https://music.example/", UserId = Guid.NewGuid(), Random = RandomMode.Album };
            await store.SaveAsync(configuration);
            Assert.Equal(configuration, await store.LoadAsync());
            Assert.Single(Directory.GetFiles(directory));
            await File.WriteAllTextAsync(Path.Combine(directory, "settings.json"), "not json");
            await Assert.ThrowsAsync<JellyJamException>(() => store.LoadAsync());
        }
        finally { Directory.Delete(directory, true); }
    }
}