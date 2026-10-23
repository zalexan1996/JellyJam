using System.Diagnostics;

namespace JellyJam.Mobile;

public sealed class LogTail
{
    private const int Capacity = 200;
    private readonly object gate = new();
    private readonly Queue<string> lines = new();
    private int version;
    private int started;

    public int Version => Volatile.Read(ref version);

    public void Start()
    {
        if (Interlocked.Exchange(ref started, 1) == 1) return;
        _ = Task.Factory.StartNew(Read, TaskCreationOptions.LongRunning);
    }

    public string[] Snapshot(int count)
    {
        lock (gate) return lines.TakeLast(count).ToArray();
    }

    private void Read()
    {
        try
        {
            using var process = Java.Lang.Runtime.GetRuntime()!.Exec(["logcat", "-v", "brief", "--pid=" + Android.OS.Process.MyPid()])!;
            using var reader = new StreamReader(process.InputStream!);
            while (reader.ReadLine() is { } line)
            {
                lock (gate)
                {
                    lines.Enqueue(Sanitize(line));
                    while (lines.Count > Capacity) lines.Dequeue();
                }
                Interlocked.Increment(ref version);
            }
        }
        catch (Exception error)
        {
            Debug.WriteLine(error.Message);
        }
    }

    private static string Sanitize(string line) => string.Concat(line.Where(character => !char.IsControl(character)));
}
