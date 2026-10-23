namespace JellyJam.Mobile.Services;

/// <summary>Exposes a rolling window of the application's own diagnostic log lines.</summary>
public interface ILogTail
{
    int Version { get; }

    void Start();

    string[] Snapshot(int count);
}
