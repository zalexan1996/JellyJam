namespace JellyJam.Core;

public enum RandomMode { All, Artist, Album, None }
public enum RepeatMode { Song, Album, None }
public enum PlayerStatus { Idle, Loading, Playing, Stopped, Error }
public enum PlayerAction { Play, Next, Previous, CycleRandom, CycleRepeat, Stop }

public sealed record Track(
    Guid Id, string Title, string Artist, string Album,
    Guid? ArtistId, Guid? AlbumId, int Disc, int Number, TimeSpan? Duration);

public sealed record PlayerConfiguration
{
    public int Version { get; init; } = 1;
    public string ServerUrl { get; init; } = "";
    public Guid UserId { get; init; }
    public string UserName { get; init; } = "";
    public string DeviceId { get; init; } = Guid.NewGuid().ToString("N");
    public RandomMode Random { get; init; } = RandomMode.All;
    public RepeatMode Repeat { get; init; } = RepeatMode.None;
}

public sealed record PlaybackSnapshot(
    PlayerStatus Status, Track? Track, RandomMode Random, RepeatMode Repeat,
    TimeSpan Position, string Message);

public sealed class JellyJamException(string message, bool fatal = true) : Exception(message)
{
    public bool Fatal { get; } = fatal;
}

public sealed class PlaybackSource(Uri uri, string accessToken, string playSessionId, Guid? itemId = null)
{
    public Uri Uri { get; } = uri;
    public string AccessToken { get; } = accessToken;
    public string PlaySessionId { get; } = playSessionId;
    public Guid? ItemId { get; } = itemId;
    public override string ToString() => "Authenticated audio stream";
}