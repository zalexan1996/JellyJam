namespace JellyJam.Core;

/// <summary>Plays one stream at a time through the platform's audio output.</summary>
public interface IAudioPlayer
{
    TimeSpan Position { get; }

    /// <summary>Plays until the stream ends, invoking <paramref name="onStarted"/> once audio begins.</summary>
    Task PlayAsync(PlaybackSource source, Action onStarted, CancellationToken cancellationToken);
}
