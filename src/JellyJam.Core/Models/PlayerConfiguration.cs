namespace JellyJam.Core;

public sealed record PlayerConfiguration
{
    public const int CurrentVersion = 1;

    public int Version { get; init; } = CurrentVersion;

    public string ServerUrl { get; init; } = "";

    public Guid UserId { get; init; }

    public string UserName { get; init; } = "";

    public string DeviceId { get; init; } = Guid.NewGuid().ToString("N");

    public RandomMode Random { get; init; } = RandomMode.All;

    public RepeatMode Repeat { get; init; } = RepeatMode.None;
}
