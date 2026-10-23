using JellyJam.Core.Playback;
using JellyJam.Mobile.Services;
using JellyJam.Mobile.ViewModels;
using JellyJam.Mobile.Views;

namespace JellyJam.Mobile;

public partial class App : Application
{
    private readonly RadioViewModel viewModel;
    private readonly RadioSession session;
    private readonly ISpectrumSource spectrum;
    private readonly ILogTail logTail;
    private readonly IPlaybackServiceController playbackService;

    public App(RadioViewModel viewModel, RadioSession session, ISpectrumSource spectrum, ILogTail logTail,
        IPlaybackServiceController playbackService)
    {
        InitializeComponent();
        Resources.MergedDictionaries.Add(new Styles().Theme("JellyJamCyber"));
        this.viewModel = viewModel;
        this.session = session;
        this.spectrum = spectrum;
        this.logTail = logTail;
        this.playbackService = playbackService;
        UserAppTheme = AppTheme.Dark;
    }

    protected override Window CreateWindow(IActivationState? activationState)
    {
        var page = new RadioPage(viewModel, spectrum, logTail);
        var window = new Window(page);
        window.Activated += (_, _) => page.SetForeground(true);
        window.Deactivated += (_, _) => page.SetForeground(false);
        window.Stopped += (_, _) => page.SetForeground(false);
        window.Destroying += async (_, _) =>
        {
            page.SetForeground(false);
            playbackService.Stop();
            await session.DisposeAsync();
        };
        return window;
    }
}