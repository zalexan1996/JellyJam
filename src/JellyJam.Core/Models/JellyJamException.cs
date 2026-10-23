namespace JellyJam.Core;

/// <summary>An error whose message is safe to show to the user.</summary>
/// <param name="fatal">When false, playback can recover by skipping to another track.</param>
public sealed class JellyJamException(string message, bool fatal = true) : Exception(message)
{
    public bool Fatal { get; } = fatal;
}
