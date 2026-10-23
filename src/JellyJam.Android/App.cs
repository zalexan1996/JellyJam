using JellyJam.Core.Playback;
using JellyJam.Mobile.ViewModels;
using JellyJam.Mobile.Views;

namespace JellyJam.Mobile;

public partial class App : Application
{
    private readonly RadioViewModel viewModel;
    private readonly RadioSession session;
    private readonly AndroidSpectrumSource spectrum;

    public App(RadioViewModel viewModel, RadioSession session, AndroidSpectrumSource spectrum)
    {
        InitializeComponent();
        this.viewModel = viewModel;
        this.session = session;
        this.spectrum = spectrum;
        UserAppTheme = AppTheme.Dark;
    }

    protected override Window CreateWindow(IActivationState? activationState)
    {
        var page = new RadioPage(viewModel, spectrum, MauiProgram.Services.GetRequiredService<LogTail>());
        var window = new Window(page);
        window.Activated += (_, _) => page.SetForeground(true);
        window.Deactivated += (_, _) => page.SetForeground(false);
        window.Stopped += (_, _) => page.SetForeground(false);
        window.Destroying += async (_, _) =>
        {
            page.SetForeground(false);
            RadioPlaybackService.StopPlaybackService();
            await session.DisposeAsync();
        };
        return window;
    }
}