using System.Net;
using System.Text;
using JellyJam.Core;
using JellyJam.Core.Configuration;
using JellyJam.Core.Jellyfin;
using JellyJam.Core.Playback;
using JellyJam.Mobile.Services;
using JellyJam.Mobile.ViewModels;

namespace JellyJam.Mobile;

public static class MauiProgram
{
    public static IServiceProvider Services { get; private set; } = null!;

    public static MauiApp CreateMauiApp()
    {
        var builder = MauiApp.CreateBuilder();
        builder.UseMauiApp<App>().ConfigureFonts(fonts =>
        {
            fonts.AddFont("SpaceGrotesk.ttf", "SpaceGrotesk");
            fonts.AddFont("SpaceMono.ttf", "SpaceMono");
        });
        builder.Services.AddSingleton<ISecureStorage>(SecureStorage.Default);
        builder.Services.AddSingleton<ICredentialStore, SecureCredentialStore>();
        builder.Services.AddSingleton<IConfigurationStore>(_ => new JsonConfigurationStore(FileSystem.AppDataDirectory));
        builder.Services.AddSingleton<AndroidSpectrumSource>();
        builder.Services.AddSingleton<LogTail>();
        builder.Services.AddSingleton<AndroidAudioPlayer>();
        builder.Services.AddSingleton<IAudioPlayer>(services => services.GetRequiredService<AndroidAudioPlayer>());
        builder.Services.AddSingleton<RadioViewModel>();
        builder.Services.AddSingleton(services => new RadioSession(
            services.GetRequiredService<IConfigurationStore>(), services.GetRequiredService<ICredentialStore>(),
            services.GetRequiredService<IAudioPlayer>(), (configuration, token) =>
                new JellyfinClient(configuration, token, CreateHttpClient(), "JellyJam Android", ownsHttpClient: true)));
        var app = builder.Build();
        Services = app.Services;
        app.Services.GetRequiredService<AndroidAudioPlayer>().Interrupted += () => app.Services.GetRequiredService<RadioSession>().Send(PlayerAction.Stop);
        return app;
    }

    private static HttpClient CreateHttpClient() => new(new SocketsHttpHandler
    {
        AllowAutoRedirect = false, AutomaticDecompression = DecompressionMethods.All,
        ConnectTimeout = TimeSpan.FromSeconds(10), RequestHeaderEncodingSelector = (_, _) => Encoding.UTF8
    }) { Timeout = TimeSpan.FromSeconds(15) };
}