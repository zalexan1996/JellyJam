namespace JellyJam.Core;

public sealed class PlaybackSource(Uri uri, string accessToken, string playSessionId, Guid? itemId = null)
{
    public Uri Uri { get; } = uri;

    public string AccessToken { get; } = accessToken;

    public string PlaySessionId { get; } = playSessionId;

    public Guid? ItemId { get; } = itemId;

    // Keeps the token and stream URL out of logs and exception messages.
    public override string ToString() => "Authenticated audio stream";
}
