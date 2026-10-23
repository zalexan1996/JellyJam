using Cocona;
using JellyJam.Cli;
using JellyJam.Cli.Presentation;
using JellyJam.Core;
using JellyJam.Core.Configuration;
using JellyJam.Infrastructure.Windows.Audio;
using JellyJam.Infrastructure.Windows.Security;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Spectre.Console;

var builder = CoconaApp.CreateBuilder(args);
builder.Logging.ClearProviders();
var directory = Environment.GetEnvironmentVariable("JELLYJAM_HOME")
    ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "JellyJam");
var noColor = Environment.GetEnvironmentVariable("NO_COLOR") is not null;
builder.Services.AddSingleton(AnsiConsole.Create(new AnsiConsoleSettings
{
    Ansi = System.Console.IsOutputRedirected || noColor ? AnsiSupport.No : AnsiSupport.Detect,
    ColorSystem = noColor ? ColorSystemSupport.NoColors : ColorSystemSupport.Detect
}));
builder.Services.AddSingleton<IConfigurationStore>(new JsonConfigurationStore(directory));
builder.Services.AddSingleton<ICredentialStore>(new WindowsCredentialStore(directory));
builder.Services.AddSingleton(_ => PlayerApplication.CreateHttpClient());
builder.Services.AddSingleton<IAudioPlayer, NAudioPlayer>();
builder.Services.AddSingleton<PlayerRenderer>();
builder.Services.AddSingleton<PlayerApplication>();
var app = builder.Build();
app.AddCommand((PlayerApplication player, CoconaAppContext context, RandomMode? random, RepeatMode? repeat)
    => player.PlayAsync(random, repeat, context.CancellationToken)).WithDescription("Start your Jellyfin radio session.");
app.AddCommand("play", (PlayerApplication player, CoconaAppContext context, RandomMode? random, RepeatMode? repeat)
    => player.PlayAsync(random, repeat, context.CancellationToken)).WithDescription("Stream music with optional random and repeat modes.");
app.AddCommand("configure", (PlayerApplication player, CoconaAppContext context)
    => player.ConfigureAsync(context.CancellationToken)).WithDescription("Connect a Jellyfin account using secure local prompts.");
app.AddCommand("logout", (PlayerApplication player, CoconaAppContext context)
    => player.LogoutAsync(context.CancellationToken)).WithDescription("Remove the saved login and revoke it when the server is available.");
await app.RunAsync();