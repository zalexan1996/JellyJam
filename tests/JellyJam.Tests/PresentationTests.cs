using JellyJam.Cli.Presentation;
using JellyJam.Core;
using Spectre.Console;
using Spectre.Console.Testing;

namespace JellyJam.Tests;

public class PresentationTests
{
    [Theory]
    [InlineData(120)]
    [InlineData(60)]
    [InlineData(30)]
    public void LayoutFitsAndMetadataIsLiteral(int width)
    {
        using var console = new TestConsole();
        console.Profile.Width = width;
        var track = TrackSelectorTests.Library()[0] with { Title = "[red]Untrusted[/] " + new string('X', 100), Artist = "Long artist name with multiple words" };
        console.Write(new PlayerRenderer().Build(new(PlayerStatus.Playing, track, RandomMode.All, RepeatMode.None, TimeSpan.FromSeconds(30), "Streaming"), width));
        var output = console.Output.Replace("\r", "");
        Assert.Contains("JELLYJAM", output);
        Assert.Contains("[red]Untrusted[/]", output);
        Assert.All(output.Split('\n'), line => Assert.True(line.Length <= width, $"Line exceeds {width}: {line}"));
    }

    [Fact]
    public void TerminalControlCharactersAreRemoved()
        => Assert.Equal("title[31mspoof", PlayerRenderer.Clean("title\u001b[31m\nspoof\u202e"));
}