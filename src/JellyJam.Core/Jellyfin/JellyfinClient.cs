using Jellyfin.Sdk;
using Jellyfin.Sdk.Generated.Models;
using JellyJam.Core.Configuration;
using Microsoft.Kiota.Abstractions;

namespace JellyJam.Core.Jellyfin;

public sealed class JellyfinClient : IJellyfinSessionClient
{
    private readonly JellyfinSdkSettings settings = new();
    private readonly JellyfinApiClient client;
    private readonly PlayerConfiguration configuration;
    private readonly HttpClient? ownedHttpClient;
    private string token;

    public JellyfinClient(PlayerConfiguration configuration, string token, HttpClient httpClient, string deviceName = "JellyJam Windows", bool ownsHttpClient = false)
    {
        ownedHttpClient = ownsHttpClient ? httpClient : null;
        this.configuration = configuration with { ServerUrl = ServerAddress.Normalize(configuration.ServerUrl) };
        this.token = token;
        settings.Initialize("JellyJam", "1.0.0", deviceName, configuration.DeviceId);
        settings.SetServerUrl(this.configuration.ServerUrl.TrimEnd('/'));
        settings.SetAccessToken(token);
        client = new JellyfinApiClient(new JellyfinRequestAdapter(new JellyfinAuthenticationProvider(settings), settings, httpClient));
    }

    public async Task<(PlayerConfiguration Configuration, string Token)> AuthenticateAsync(string username, string password, CancellationToken cancellationToken)
    {
        var result = await RequestAsync(() => client.Users.AuthenticateByName.PostAsync(
            new AuthenticateUserByName { Username = username, Pw = password }, cancellationToken: cancellationToken), cancellationToken, retry: false);
        if (result?.User?.Id is not { } userId || string.IsNullOrWhiteSpace(result.AccessToken))
            throw new JellyJamException("The server did not return a valid login. Check your account and server URL.");
        token = result.AccessToken;
        settings.SetAccessToken(token);
        return (configuration with { UserId = userId, UserName = result.User.Name ?? username }, token);
    }

    public async Task<IReadOnlyList<Track>> GetTracksAsync(CancellationToken cancellationToken)
    {
        var tracks = new Dictionary<Guid, Track>();
        var start = 0;
        while (true)
        {
            var result = await RequestAsync(() => client.Items.GetAsync(request =>
            {
                request.QueryParameters.UserId = configuration.UserId;
                request.QueryParameters.Recursive = true;
                request.QueryParameters.IncludeItemTypes = [BaseItemKind.Audio];
                request.QueryParameters.SortBy = [ItemSortBy.SortName];
                request.QueryParameters.StartIndex = start;
                request.QueryParameters.Limit = 500;
            }, cancellationToken), cancellationToken);
            var items = result?.Items ?? [];
            if (items.Count == 0) break;
            var previousCount = tracks.Count;
            foreach (var item in items)
            {
                if (item.Id is not { } id) continue;
                var artist = item.ArtistItems?.FirstOrDefault();
                tracks.TryAdd(id, new Track(id, item.Name ?? "Untitled", artist?.Name ?? item.Artists?.FirstOrDefault() ?? "Unknown artist",
                    item.Album ?? "Unknown album", artist?.Id, item.AlbumId, item.ParentIndexNumber ?? 0, item.IndexNumber ?? 0,
                    item.RunTimeTicks is > 0 ? TimeSpan.FromTicks(item.RunTimeTicks.Value) : null));
            }
            start += items.Count;
            if (result?.TotalRecordCount is { } total && start >= total) break;
            if (tracks.Count == previousCount) throw new JellyJamException("The server returned a repeated library page. Try again after the library scan finishes.");
        }
        return tracks.Values.ToArray();
    }

    public async Task<PlaybackSource> ResolveAsync(Track track, CancellationToken cancellationToken)
    {
        var result = await RequestAsync(() => client.Items[track.Id].PlaybackInfo.PostAsync(new PlaybackInfoDto
        {
            UserId = configuration.UserId,
            EnableDirectPlay = true,
            EnableDirectStream = true,
            EnableTranscoding = true,
            MaxStreamingBitrate = 320000,
            MaxAudioChannels = 2,
            DeviceProfile = new DeviceProfile
            {
                Name = "JellyJam MP3",
                MaxStreamingBitrate = 320000,
                DirectPlayProfiles = [new DirectPlayProfile { Container = "mp3", AudioCodec = "mp3", Type = DirectPlayProfile_Type.Audio }],
                TranscodingProfiles = [new TranscodingProfile
                {
                    Container = "mp3", AudioCodec = "mp3", Type = TranscodingProfile_Type.Audio,
                    Protocol = TranscodingProfile_Protocol.Http, Context = TranscodingProfile_Context.Streaming,
                    MaxAudioChannels = "2"
                }]
            }
        }, cancellationToken: cancellationToken), cancellationToken, trackRequest: true);
        var source = result?.MediaSources?.FirstOrDefault(media =>
            (media.SupportsDirectPlay == true && string.Equals(media.Container, "mp3", StringComparison.OrdinalIgnoreCase)) ||
            (media.SupportsTranscoding == true && !string.IsNullOrEmpty(media.TranscodingUrl)));
        if (source is null) throw new JellyJamException("No compatible MP3 stream. Check this user's Jellyfin transcoding permission.", false);
        var session = result?.PlaySessionId ?? "";
        string path;
        if (source.SupportsDirectPlay == true && string.Equals(source.Container, "mp3", StringComparison.OrdinalIgnoreCase))
            path = $"Audio/{track.Id:N}/stream.mp3?static=true&mediaSourceId={Uri.EscapeDataString(source.Id ?? "")}&playSessionId={Uri.EscapeDataString(session)}&deviceId={Uri.EscapeDataString(configuration.DeviceId)}";
        else
            path = source.TranscodingUrl!;
        return new PlaybackSource(ResolveStreamUri(configuration.ServerUrl, path), token, session, track.Id);
    }

    public static Uri ResolveStreamUri(string serverUrl, string path)
    {
        var server = new Uri(ServerAddress.Normalize(serverUrl));
        Uri resolved;
        if (Uri.TryCreate(path, UriKind.Absolute, out var absolute) && absolute.Scheme is "http" or "https")
            resolved = absolute;
        else if (path.StartsWith(server.AbsolutePath, StringComparison.OrdinalIgnoreCase))
            resolved = new Uri(server.GetLeftPart(UriPartial.Authority) + path);
        else
            resolved = new Uri(server, path.TrimStart('/'));
        if (resolved.Scheme != server.Scheme || resolved.Host != server.Host || resolved.Port != server.Port || !string.IsNullOrEmpty(resolved.UserInfo))
            throw new JellyJamException("The server returned a stream on a different origin. Refusing to forward credentials.");
        return resolved;
    }

    public async Task ReleaseAsync(PlaybackSource source, CancellationToken cancellationToken)
    {
        if (string.IsNullOrEmpty(source.PlaySessionId)) return;
        await client.Sessions.Playing.Stopped.PostAsync(new PlaybackStopInfo
        {
            ItemId = source.ItemId,
            PlaySessionId = source.PlaySessionId
        }, cancellationToken: cancellationToken);
    }

    public Task LogoutAsync(CancellationToken cancellationToken) => client.Sessions.Logout.PostAsync(cancellationToken: cancellationToken);

    private static async Task<T> RequestAsync<T>(Func<Task<T>> request, CancellationToken cancellationToken, bool retry = true, bool trackRequest = false)
    {
        for (var attempt = 0; ; attempt++)
        {
            try { return await request(); }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
            catch (ApiException error) when (error.ResponseStatusCode is 401 or 403)
            { throw new JellyJamException("Jellyfin rejected the login or access token. Run 'configure' and check account permissions."); }
            catch (ApiException error) when (trackRequest && error.ResponseStatusCode == 404)
            { throw new JellyJamException("This track is no longer available on the server.", false); }
            catch (Exception error) when (error is HttpRequestException or ApiException or OperationCanceledException)
            {
                var transient = error is not ApiException api || api.ResponseStatusCode is 408 or 429 or >= 500;
                if (retry && transient && attempt < 2)
                {
                    await Task.Delay(TimeSpan.FromMilliseconds(250 * (attempt + 1)), cancellationToken);
                    continue;
                }
                throw new JellyJamException("Jellyfin request failed. Check the server address, connection, and server permissions.");
            }
        }
    }

    public void Dispose()
    {
        client.Dispose();
        ownedHttpClient?.Dispose();
    }
}