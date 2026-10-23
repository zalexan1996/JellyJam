using System.Net;
using System.Text;
using System.Text.Json;
using JellyJam.Core;
using JellyJam.Core.Jellyfin;

namespace JellyJam.Tests;

public class JellyfinClientTests
{
    [Fact]
    public async Task AndroidSessionUsesItsOwnDeviceLabel()
    {
        using var http = new HttpClient(new Handler(request =>
        {
            Assert.Contains("JellyJam Android", Uri.UnescapeDataString(request.Headers.ToString()));
            return Json(new { AccessToken = "test-token", User = new { Id = Guid.NewGuid(), Name = "listener" } });
        }));
        using var client = new JellyfinClient(Configuration(), "", http, "JellyJam Android");
        var result = await client.AuthenticateAsync("listener", "fake-password", CancellationToken.None);
        Assert.Equal("test-token", result.Token);
    }

    [Fact]
    public async Task DeletedTrackIsRecoverable()
    {
        using var http = new HttpClient(new Handler(_ => new HttpResponseMessage(HttpStatusCode.NotFound)
        {
            Content = new StringContent("{}", Encoding.UTF8, "application/json")
        }));
        using var client = new JellyfinClient(Configuration(), "fake-token", http);
        var failure = await Assert.ThrowsAsync<JellyJamException>(() => client.ResolveAsync(TrackSelectorTests.Library()[0], CancellationToken.None));
        Assert.False(failure.Fatal);
    }

    private static PlayerConfiguration Configuration() => new() { ServerUrl = "https://music.example/jellyfin/", UserId = Guid.NewGuid() };

    [Fact]
    public async Task LibraryUsesAuthenticatedUserAndAllPages()
    {
        var configuration = Configuration();
        var requests = 0;
        using var http = new HttpClient(new Handler(request =>
        {
            Assert.Equal("/jellyfin/Items", request.RequestUri!.AbsolutePath);
            Assert.Contains(configuration.UserId.ToString(), request.RequestUri.Query);
            Assert.Contains("fake-token", request.Headers.ToString());
            requests++;
            if (requests == 2) Assert.Contains("startIndex=2", request.RequestUri.Query);
            return Json(new
            {
                TotalRecordCount = 4,
                Items = new[]
                {
                    new { Id = Guid.NewGuid(), Name = "Track one", Album = "Album", IndexNumber = 1, RunTimeTicks = 10000000L },
                    new { Id = Guid.NewGuid(), Name = "Track two", Album = "Album", IndexNumber = 2, RunTimeTicks = 20000000L }
                }
            });
        }));
        using var client = new JellyfinClient(configuration, "fake-token", http);
        var tracks = await client.GetTracksAsync(CancellationToken.None);
        Assert.Equal(4, tracks.Count);
        Assert.Equal(2, requests);
        Assert.Equal(TimeSpan.FromSeconds(1), tracks[0].Duration);
    }

    [Fact]
    public async Task AuthenticationReturnsIdentityAndTokenWithoutStoringPassword()
    {
        var userId = Guid.NewGuid();
        using var http = new HttpClient(new Handler(request =>
        {
            Assert.EndsWith("/Users/AuthenticateByName", request.RequestUri!.AbsolutePath);
            return Json(new { AccessToken = "test-token", User = new { Id = userId, Name = "listener" } });
        }));
        using var client = new JellyfinClient(Configuration(), "", http);
        var result = await client.AuthenticateAsync("listener", "fake-password", CancellationToken.None);
        Assert.Equal(userId, result.Configuration.UserId);
        Assert.Equal("test-token", result.Token);
        Assert.DoesNotContain("fake-password", JsonSerializer.Serialize(result.Configuration));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task ResolvesMp3DirectOrTranscodedAndReportsStop(bool direct)
    {
        var track = TrackSelectorTests.Library()[0];
        var stopped = false;
        using var http = new HttpClient(new Handler(request =>
        {
            if (request.RequestUri!.AbsolutePath.EndsWith("/Sessions/Playing/Stopped"))
            {
                stopped = true;
                return new HttpResponseMessage(HttpStatusCode.NoContent);
            }
            Assert.EndsWith("/PlaybackInfo", request.RequestUri.AbsolutePath);
            Assert.Equal(HttpMethod.Post, request.Method);
            return Json(new
            {
                PlaySessionId = "session",
                MediaSources = new[] { new
                {
                    Id = "source", Container = direct ? "mp3" : "flac", SupportsDirectPlay = direct,
                    SupportsTranscoding = !direct, TranscodingUrl = direct ? null : "/Audio/track/stream.mp3?playSessionId=session"
                } }
            });
        }));
        using var client = new JellyfinClient(Configuration(), "fake-token", http);
        var source = await client.ResolveAsync(track, CancellationToken.None);
        Assert.StartsWith("https://music.example/jellyfin/Audio/", source.Uri.AbsoluteUri);
        Assert.Equal(direct, source.Uri.Query.Contains("static=true"));
        Assert.Equal("fake-token", source.AccessToken);
        Assert.DoesNotContain("fake-token", source.ToString());
        await client.ReleaseAsync(source, CancellationToken.None);
        Assert.True(stopped);
    }

    [Theory]
    [InlineData(HttpStatusCode.Unauthorized, 1)]
    [InlineData(HttpStatusCode.ServiceUnavailable, 3)]
    public async Task FailuresAreSanitizedAndRetriesAreBounded(HttpStatusCode status, int expectedRequests)
    {
        var requests = 0;
        using var http = new HttpClient(new Handler(_ =>
        {
            requests++;
            return new HttpResponseMessage(status);
        }));
        using var client = new JellyfinClient(Configuration(), "fake-token", http);
        var failure = await Assert.ThrowsAsync<JellyJamException>(() => client.GetTracksAsync(CancellationToken.None));
        Assert.DoesNotContain("fake-token", failure.ToString());
        Assert.Equal(expectedRequests, requests);
    }

    private static HttpResponseMessage Json(object value) => new(HttpStatusCode.OK)
    {
        Content = new StringContent(JsonSerializer.Serialize(value), Encoding.UTF8, "application/json")
    };

    private sealed class Handler(Func<HttpRequestMessage, HttpResponseMessage> response) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            => Task.FromResult(response(request));
    }
}