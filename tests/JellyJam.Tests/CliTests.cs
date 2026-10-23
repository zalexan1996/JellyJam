using System.Diagnostics;
using JellyJam.Cli;

namespace JellyJam.Tests;

public class CliTests
{
    [Theory]
    [InlineData("--help", 129, "Usage:")]
    [InlineData("--version", 0, "jellyjam 1.0.0")]
    [InlineData("play", 1, "Setup requires an interactive terminal")]
    public async Task CommandsHaveExpectedExitAndOutput(string command, int exitCode, string text)
    {
        var result = await RunAsync(command);
        Assert.Equal(exitCode, result.ExitCode);
        Assert.Contains(text, result.Output);
        Assert.DoesNotContain("Unhandled Exception", result.Output);
    }

    [Fact]
    public async Task InvalidModeFailsWithoutPrompting()
    {
        var result = await RunAsync("play", "--random", "invalid");
        Assert.NotEqual(0, result.ExitCode);
        Assert.Contains("random", result.Output, StringComparison.OrdinalIgnoreCase);
    }

    private static async Task<(int ExitCode, string Output)> RunAsync(params string[] arguments)
    {
        var home = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        var start = new ProcessStartInfo("dotnet")
        {
            RedirectStandardInput = true, RedirectStandardOutput = true, RedirectStandardError = true,
            UseShellExecute = false, CreateNoWindow = true
        };
        start.ArgumentList.Add(typeof(PlayerApplication).Assembly.Location);
        foreach (var argument in arguments) start.ArgumentList.Add(argument);
        start.Environment["JELLYJAM_HOME"] = home;
        start.Environment["NO_COLOR"] = "1";
        using var process = Process.Start(start)!;
        process.StandardInput.Close();
        var output = process.StandardOutput.ReadToEndAsync();
        var error = process.StandardError.ReadToEndAsync();
        try
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
            await process.WaitForExitAsync(timeout.Token);
            return (process.ExitCode, await output + await error);
        }
        finally
        {
            if (!process.HasExited) process.Kill(entireProcessTree: true);
            if (Directory.Exists(home)) Directory.Delete(home, true);
        }
    }
}