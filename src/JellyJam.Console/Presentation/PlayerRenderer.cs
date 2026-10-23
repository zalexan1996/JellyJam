using JellyJam.Core;
using Spectre.Console;
using Spectre.Console.Rendering;

namespace JellyJam.Cli.Presentation;

public static class CyberpunkTheme
{
    public static readonly Style Signal = new(Color.Cyan1);
    public static readonly Style Accent = new(Color.Magenta1);
    public static readonly Style Muted = new(Color.Grey);
    public static readonly Style Warning = new(Color.Yellow);
    public static readonly Style Error = new(Color.Red);
    public static readonly Style Title = new(Color.White, decoration: Decoration.Bold);
}

public sealed class PlayerRenderer
{
    public IRenderable Build(PlaybackSnapshot snapshot, int width)
    {
        var track = snapshot.Track;
        var barWidth = Math.Clamp(width - 6, 4, 56);
        var duration = track?.Duration;
        var position = duration is { } total ? TimeSpan.FromTicks(Math.Clamp(snapshot.Position.Ticks, 0, total.Ticks)) : snapshot.Position;
        var fraction = duration is { TotalSeconds: > 0 } length ? Math.Clamp(position.TotalSeconds / length.TotalSeconds, 0, 1) : 0;
        var filled = (int)(barWidth * fraction);
        var stateStyle = snapshot.Status switch
        {
            PlayerStatus.Error => CyberpunkTheme.Error,
            PlayerStatus.Loading => CyberpunkTheme.Warning,
            _ => CyberpunkTheme.Signal
        };
        return new Rows(
            new Text("JELLYJAM // RADIO", CyberpunkTheme.Signal),
            new Rule().RuleStyle(CyberpunkTheme.Muted),
            new Text(Clean(snapshot.Status.ToString().ToUpperInvariant()), stateStyle),
            new Text(Clean(track?.Title ?? "NO SIGNAL"), CyberpunkTheme.Title),
            new Text(Clean(track?.Artist ?? ""), CyberpunkTheme.Accent),
            new Text(Clean(track?.Album ?? ""), CyberpunkTheme.Muted),
            new Text(""),
            new Text("[" + new string('=', filled) + new string('-', barWidth - filled) + "]", CyberpunkTheme.Signal),
            new Text($"{FormatTime(position)} / {(duration is { } known ? FormatTime(known) : "--:--")}", CyberpunkTheme.Muted),
            new Text($"RANDOM {snapshot.Random.ToString().ToUpperInvariant()}  //  REPEAT {snapshot.Repeat.ToString().ToUpperInvariant()}", CyberpunkTheme.Accent),
            new Rule().RuleStyle(CyberpunkTheme.Muted),
            new Text(Clean(snapshot.Message), stateStyle));
    }

    public static string Clean(string value)
        => string.Concat(value.Where(character => !char.IsControl(character) && character is not ('\u202a' or '\u202b' or '\u202c' or '\u202d' or '\u202e' or '\u2066' or '\u2067' or '\u2068' or '\u2069')));

    private static string FormatTime(TimeSpan time)
        => $"{(int)Math.Max(0, time.TotalMinutes):00}:{Math.Max(0, time.Seconds):00}";
}