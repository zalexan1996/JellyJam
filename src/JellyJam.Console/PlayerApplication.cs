using System.Net;
using System.Text;
using JellyJam.Cli.Presentation;
using JellyJam.Core;
using JellyJam.Core.Configuration;
using JellyJam.Core.Jellyfin;
using JellyJam.Core.Playback;
using Spectre.Console;

namespace JellyJam.Cli;

public sealed class PlayerApplication(IAnsiConsole console, IConfigurationStore configurations, ICredentialStore credentials, IAudioPlayer audio, PlayerRenderer renderer)
{
    public static HttpClient CreateHttpClient() => new(new SocketsHttpHandler
    {
        AllowAutoRedirect = false,
        AutomaticDecompression = DecompressionMethods.All,
        ConnectTimeout = TimeSpan.FromSeconds(10),
        RequestHeaderEncodingSelector = (_, _) => Encoding.UTF8
    }) { Timeout = TimeSpan.FromSeconds(15) };

    public Task<int> ConfigureAsync(CancellationToken cancellationToken)
        => GuardAsync(async () => { await ConfigureCoreAsync(cancellationToken); return 0; }, cancellationToken);

    public Task<int> PlayAsync(RandomMode? random, RepeatMode? repeat, CancellationToken cancellationToken)
        => GuardAsync(async () =>
        {
            var configuration = await configurations.LoadAsync(cancellationToken) ?? await ConfigureCoreAsync(cancellationToken);
            var token = await credentials.LoadAsync(configuration, cancellationToken);
            if (string.IsNullOrEmpty(token))
            {
                configuration = await ConfigureCoreAsync(cancellationToken);
                token = await credentials.LoadAsync(configuration, cancellationToken);
            }
            if (string.IsNullOrEmpty(token)) throw new JellyJamException("No saved login. Run 'configure'.");
            configuration = configuration with { Random = random ?? configuration.Random, Repeat = repeat ?? configuration.Repeat };
            using var http = CreateHttpClient();
            using var client = new JellyfinClient(configuration, token, http);
            WriteLine("JELLYJAM // Connecting to your music library", CyberpunkTheme.Signal);
            var tracks = await client.GetTracksAsync(cancellationToken);
            using var session = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            var coordinator = new PlaybackCoordinator(client, audio, tracks, configuration.Random, configuration.Repeat);
            var run = coordinator.RunAsync(session.Token);
            coordinator.Send(PlayerAction.Play);
            var interactive = !System.Console.IsInputRedirected;
            var exitCode = 0;
            try
            {
                if (console.Profile.Capabilities.Ansi && !System.Console.IsOutputRedirected)
                {
                    await console.Live(renderer.Build(coordinator.Snapshot, console.Profile.Width)).AutoClear(false).StartAsync(async display =>
                    {
                        await LoopAsync(snapshot => display.UpdateTarget(renderer.Build(snapshot, console.Profile.Width)));
                    });
                }
                else
                {
                    string? lastLine = null;
                    await LoopAsync(snapshot =>
                    {
                        var line = $"{snapshot.Status} | {snapshot.Track?.Artist} | {snapshot.Track?.Title} | Random: {snapshot.Random} | Repeat: {snapshot.Repeat} | {snapshot.Message}";
                        if (line != lastLine) WriteLine(line, CyberpunkTheme.Signal);
                        lastLine = line;
                    });
                }
            }
            finally
            {
                await session.CancelAsync();
                await run;
            }
            return exitCode;

            async Task LoopAsync(Action<PlaybackSnapshot> render)
            {
                using var timer = new PeriodicTimer(TimeSpan.FromMilliseconds(250));
                while (!session.IsCancellationRequested)
                {
                    if (interactive)
                    {
                        while (System.Console.KeyAvailable)
                        {
                            var key = System.Console.ReadKey(true).Key;
                            if (key is ConsoleKey.Q or ConsoleKey.Escape) { await session.CancelAsync(); break; }
                            var action = key switch
                            {
                                ConsoleKey.N or ConsoleKey.RightArrow => PlayerAction.Next,
                                ConsoleKey.P or ConsoleKey.LeftArrow => PlayerAction.Previous,
                                ConsoleKey.R => PlayerAction.CycleRandom,
                                ConsoleKey.T => PlayerAction.CycleRepeat,
                                ConsoleKey.Enter => PlayerAction.Play,
                                _ => (PlayerAction?)null
                            };
                            if (action is { } command) coordinator.Send(command);
                        }
                    }
                    var snapshot = coordinator.Snapshot;
                    render(snapshot);
                    if (snapshot.Random != configuration.Random || snapshot.Repeat != configuration.Repeat)
                    {
                        configuration = configuration with { Random = snapshot.Random, Repeat = snapshot.Repeat };
                        await configurations.SaveAsync(configuration, cancellationToken);
                    }
                    if (!interactive && snapshot.Status is PlayerStatus.Stopped or PlayerStatus.Error)
                    {
                        exitCode = snapshot.Status == PlayerStatus.Error ? 1 : 0;
                        break;
                    }
                    if (session.IsCancellationRequested) break;
                    try { if (!await timer.WaitForNextTickAsync(session.Token)) break; }
                    catch (OperationCanceledException) when (session.IsCancellationRequested) { break; }
                }
            }
        }, cancellationToken);

    public Task<int> LogoutAsync(CancellationToken cancellationToken)
        => GuardAsync(async () =>
        {
            try
            {
                var configuration = await configurations.LoadAsync(cancellationToken);
                if (configuration is not null && await credentials.LoadAsync(configuration, cancellationToken) is { } token)
                {
                    using var http = CreateHttpClient();
                    using var client = new JellyfinClient(configuration, token, http);
                    await client.LogoutAsync(cancellationToken);
                }
            }
            catch (Exception) { WriteLine("Server revocation could not be confirmed. Removing the local login.", CyberpunkTheme.Warning); }
            finally { await credentials.DeleteAsync(CancellationToken.None); }
            WriteLine("Local login removed.", CyberpunkTheme.Signal);
            return 0;
        }, cancellationToken);

    private async Task<PlayerConfiguration> ConfigureCoreAsync(CancellationToken cancellationToken)
    {
        if (System.Console.IsInputRedirected)
            throw new JellyJamException("Setup requires an interactive terminal. Run 'configure' locally first.");
        PlayerConfiguration? previous = null;
        try { previous = await configurations.LoadAsync(cancellationToken); }
        catch (JellyJamException) { }
        WriteLine("JELLYJAM // UPLINK", CyberpunkTheme.Signal);
        var address = console.Prompt(new TextPrompt<string>("[cyan]Server URL[/]")
            .DefaultValue(previous?.ServerUrl ?? "http://localhost:8096")
            .Validate(value =>
            {
                try { ServerAddress.Normalize(value); return ValidationResult.Success(); }
                catch (ArgumentException) { return ValidationResult.Error("Enter an HTTP(S) URL without credentials, query, or fragment."); }
            }));
        address = ServerAddress.Normalize(address);
        if (new Uri(address).Scheme == "http")
        {
            WriteLine("HTTP sends login and music traffic without encryption. Use only on a trusted network.", CyberpunkTheme.Warning);
            if (!console.Confirm("Continue with HTTP?", false)) throw new JellyJamException("Setup cancelled. Use an HTTPS server URL.");
        }
        var usernamePrompt = new TextPrompt<string>("[cyan]Username[/]");
        if (!string.IsNullOrEmpty(previous?.UserName)) usernamePrompt.DefaultValue(previous.UserName);
        var username = console.Prompt(usernamePrompt);
        var password = console.Prompt(new TextPrompt<string>("[cyan]Password[/]").Secret().AllowEmpty());
        cancellationToken.ThrowIfCancellationRequested();
        var configuration = new PlayerConfiguration
        {
            ServerUrl = address, UserName = username, DeviceId = previous?.DeviceId ?? Guid.NewGuid().ToString("N"),
            Random = previous?.Random ?? RandomMode.All, Repeat = previous?.Repeat ?? RepeatMode.None
        };
        using var http = CreateHttpClient();
        using var client = new JellyfinClient(configuration, "", http);
        var authenticated = await client.AuthenticateAsync(username, password, cancellationToken);
        await credentials.SaveAsync(authenticated.Configuration, authenticated.Token, cancellationToken);
        await configurations.SaveAsync(authenticated.Configuration, cancellationToken);
        WriteLine($"Connected as {authenticated.Configuration.UserName}.", CyberpunkTheme.Signal);
        return authenticated.Configuration;
    }

    private async Task<int> GuardAsync(Func<Task<int>> operation, CancellationToken cancellationToken)
    {
        try { return await operation(); }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { return 0; }
        catch (JellyJamException error) { WriteLine(error.Message, CyberpunkTheme.Error); return 1; }
        catch (Exception) { WriteLine("Operation failed. Check local settings access, the server connection, and your audio device.", CyberpunkTheme.Error); return 1; }
    }

    private void WriteLine(string text, Style style) => console.Write(new Text(PlayerRenderer.Clean(text) + Environment.NewLine, style));
}