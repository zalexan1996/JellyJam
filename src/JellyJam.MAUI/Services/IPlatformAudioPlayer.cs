using JellyJam.Core;

namespace JellyJam.Mobile.Services;

/// <summary>An audio player that can be interrupted by the operating system (focus loss, session changes).</summary>
public interface IPlatformAudioPlayer : IAudioPlayer
{
    event Action? Interrupted;
}
