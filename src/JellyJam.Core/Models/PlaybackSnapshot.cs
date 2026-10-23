namespace JellyJam.Core;

/// <summary>An immutable view of playback state that UIs render.</summary>
public sealed record PlaybackSnapshot(
    PlayerStatus Status,
    Track? Track,
    RandomMode Random,
    RepeatMode Repeat,
    TimeSpan Position,
    string Message);
