using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using JellyJam.Core;
using JellyJam.Core.Configuration;
using JellyJam.Core.Playback;
using JellyJam.Mobile.Services;

namespace JellyJam.Mobile.ViewModels;

public partial class RadioViewModel : ObservableObject
{
    private readonly RadioSession session;
    private readonly ISpectrumSource spectrum;
    private readonly IPlaybackServiceController playbackService;
    private bool synchronizingSpectrum;
    private bool refreshing;
    private bool initialized;

    public RadioViewModel(RadioSession session, ISpectrumSource spectrum, IPlaybackServiceController playbackService)
    {
        this.session = session;
        this.spectrum = spectrum;
        this.playbackService = playbackService;
        session.Changed += () => MainThread.BeginInvokeOnMainThread(Refresh);
    }

    [ObservableProperty, NotifyPropertyChangedFor(nameof(ShowLogin)), NotifyPropertyChangedFor(nameof(ShowRadio))]
    private bool initializing = true;
    [ObservableProperty, NotifyPropertyChangedFor(nameof(ShowLogin)), NotifyPropertyChangedFor(nameof(ShowRadio))]
    private bool signedIn;
    [ObservableProperty, NotifyPropertyChangedFor(nameof(CanInteract)), NotifyPropertyChangedFor(nameof(CanNavigate)), NotifyPropertyChangedFor(nameof(CanToggleSpectrum))]
    private bool busy;
    [ObservableProperty, NotifyPropertyChangedFor(nameof(IsInsecure))]
    private string serverUrl = "";
    [ObservableProperty] private string userName = "";
    [ObservableProperty] private string password = "";
    [ObservableProperty] private bool allowHttp;
    [ObservableProperty] private string errorMessage = "";
    [ObservableProperty] private string trackTitle = "Ready to tune in";
    [ObservableProperty] private string artist = "JellyJam Radio";
    [ObservableProperty] private string album = "";
    [ObservableProperty] private string status = "IDLE";
    [ObservableProperty] private string positionText = "00:00 / --:--";
    [ObservableProperty] private double progress;
    [ObservableProperty] private string playbackIcon = "play.png";
    [ObservableProperty] private string playbackDescription = "Play";
    [ObservableProperty] private string account = "";
    [ObservableProperty] private string randomName = "All";
    [ObservableProperty] private string repeatName = "None";
    [ObservableProperty] private bool spectrumEnabled;
    [ObservableProperty, NotifyPropertyChangedFor(nameof(CanToggleSpectrum))]
    private bool spectrumPermissionBusy;
    public string[] RandomOptions { get; } = Enum.GetNames<RandomMode>();
    public string[] RepeatOptions { get; } = Enum.GetNames<RepeatMode>();
    public bool ShowLogin => !Initializing && !SignedIn;
    public bool ShowRadio => !Initializing && SignedIn;
    public bool CanInteract => !Busy;
    public bool CanNavigate => !Busy && session.Snapshot.Track is not null;
    public bool CanToggleSpectrum => !Busy && !SpectrumPermissionBusy;
    public bool IsInsecure => Uri.TryCreate(ServerUrl, UriKind.Absolute, out var uri) && uri.Scheme == "http";

    public async Task InitializeAsync()
    {
        if (initialized) return;
        initialized = true;
        await ExecuteAsync(async () =>
        {
            await session.RestoreAsync();
            ServerUrl = session.ServerUrl;
            UserName = session.UserName;
            var enabled = Preferences.Default.Get("spectrum.enabled", false)
                && await Permissions.CheckStatusAsync<Permissions.Microphone>() == PermissionStatus.Granted;
            ApplySpectrumSetting(enabled);
        });
        Initializing = false;
        Refresh();
    }

    [RelayCommand]
    private Task SignInAsync() => ExecuteAsync(async () =>
    {
        var url = ServerAddress.Normalize(ServerUrl);
        if (string.IsNullOrWhiteSpace(UserName)) throw new JellyJamException("Enter your username.");
        if (IsInsecure && !AllowHttp) throw new JellyJamException("Confirm unencrypted HTTP or use an HTTPS server URL.");
        try { await session.SignInAsync(url, UserName, Password); }
        finally { Password = ""; }
        Refresh();
    });

    [RelayCommand]
    private Task TogglePlaybackAsync() => ExecuteAsync(async () =>
    {
        if (session.Snapshot.Status is PlayerStatus.Playing or PlayerStatus.Loading)
        {
            session.Send(PlayerAction.Stop);
            playbackService.Stop();
            return;
        }
        if (OperatingSystem.IsAndroidVersionAtLeast(33)) await Permissions.RequestAsync<Permissions.PostNotifications>();
        await playbackService.EnsureStartedAsync(CancellationToken.None);
        try { await session.PlayAsync(); }
        catch { playbackService.Stop(); throw; }
    });

    [RelayCommand]
    private Task NextAsync() => NavigateAsync(PlayerAction.Next);

    [RelayCommand]
    private Task PreviousAsync() => NavigateAsync(PlayerAction.Previous);

    private Task NavigateAsync(PlayerAction action) => ExecuteAsync(async () =>
    {
        if (session.Snapshot.Track is null) return;
        await playbackService.EnsureStartedAsync(CancellationToken.None);
        session.Send(action);
    });

    [RelayCommand]
    private Task SignOutAsync() => ExecuteAsync(async () =>
    {
        playbackService.Stop();
        await session.SignOutAsync();
        Password = "";
        Refresh();
    });

    partial void OnRandomNameChanged(string value)
    {
        if (!refreshing && Enum.TryParse<RandomMode>(value, out var mode))
            _ = ExecuteAsync(() => session.SetModesAsync(mode, session.Snapshot.Repeat));
    }

    partial void OnRepeatNameChanged(string value)
    {
        if (!refreshing && Enum.TryParse<RepeatMode>(value, out var mode))
            _ = ExecuteAsync(() => session.SetModesAsync(session.Snapshot.Random, mode));
    }

    partial void OnSpectrumEnabledChanged(bool value)
    {
        if (!synchronizingSpectrum) _ = ChangeSpectrumAsync(value);
    }

    private async Task ChangeSpectrumAsync(bool enable)
    {
        SpectrumPermissionBusy = true;
        try
        {
            if (enable && await Permissions.CheckStatusAsync<Permissions.Microphone>() != PermissionStatus.Granted)
            {
                var page = Application.Current?.Windows.FirstOrDefault()?.Page;
                var consent = page is not null && await page.DisplayAlertAsync("Audio visualization",
                    "Android requires microphone permission for its playback visualizer. JellyJam analyzes only its music output; it does not record microphone audio.",
                    "Continue", "Not now");
                enable = consent && await Permissions.RequestAsync<Permissions.Microphone>() == PermissionStatus.Granted;
            }
            ApplySpectrumSetting(enable);
        }
        catch (Exception) { ApplySpectrumSetting(false); }
        finally { SpectrumPermissionBusy = false; }
    }

    private void ApplySpectrumSetting(bool enabled)
    {
        synchronizingSpectrum = true;
        try
        {
            spectrum.SetEnabled(enabled);
            SpectrumEnabled = enabled;
            Preferences.Default.Set("spectrum.enabled", enabled);
        }
        finally { synchronizingSpectrum = false; }
    }

    private void Refresh()
    {
        refreshing = true;
        try
        {
            SignedIn = session.IsSignedIn;
            OnPropertyChanged(nameof(CanNavigate));
            var snapshot = session.Snapshot;
            TrackTitle = snapshot.Track?.Title ?? "Ready to tune in";
            Artist = snapshot.Track?.Artist ?? "JellyJam Radio";
            Album = snapshot.Track?.Album ?? "";
            Status = snapshot.Status.ToString().ToUpperInvariant();
            var active = snapshot.Status is PlayerStatus.Playing or PlayerStatus.Loading;
            PlaybackIcon = active ? "square.png" : "play.png";
            PlaybackDescription = active ? "Stop" : "Play";
            var elapsed = snapshot.Position;
            var total = snapshot.Track?.Duration;
            Progress = total is { TotalSeconds: > 0 } duration ? Math.Clamp(elapsed.TotalSeconds / duration.TotalSeconds, 0, 1) : 0;
            PositionText = $"{Time(elapsed)} / {(total is { } known ? Time(known) : "--:--")}";
            Account = session.UserName;
            RandomName = snapshot.Random.ToString();
            RepeatName = snapshot.Repeat.ToString();
            if (snapshot.Status == PlayerStatus.Error) ErrorMessage = snapshot.Message.Replace("Run 'configure'", "Sign out and sign in again", StringComparison.Ordinal);
        }
        finally { refreshing = false; }
    }

    private async Task ExecuteAsync(Func<Task> operation)
    {
        if (Busy) return;
        Busy = true;
        ErrorMessage = "";
        try { await operation(); }
        catch (ArgumentException error) { ErrorMessage = error.Message; }
        catch (JellyJamException error) { ErrorMessage = error.Message.Replace("Run 'configure'", "Sign out and sign in again", StringComparison.Ordinal); }
        catch (Exception) { ErrorMessage = "Connection or secure storage failed. Check the server and try again."; }
        finally { Busy = false; }
    }

    private static string Time(TimeSpan time) => $"{(int)Math.Max(0, time.TotalMinutes):00}:{Math.Max(0, time.Seconds):00}";
}