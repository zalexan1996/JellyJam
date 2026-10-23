using JellyJam.Core.Configuration;

namespace JellyJam.Core.Playback;

public sealed class RadioSession(
    IConfigurationStore configurations,
    ICredentialStore credentials,
    IAudioPlayer audio,
    Func<PlayerConfiguration, string, IJellyfinSessionClient> createClient) : IAsyncDisposable
{
    private readonly SemaphoreSlim gate = new(1, 1);
    private PlayerConfiguration? configuration;
    private string? token;
    private IJellyfinSessionClient? client;
    private PlaybackCoordinator? coordinator;
    private CancellationTokenSource? lifetime;
    private Task runner = Task.CompletedTask;
    private Task monitor = Task.CompletedTask;

    public bool IsSignedIn => configuration is not null && !string.IsNullOrEmpty(token);
    public string UserName => configuration?.UserName ?? "";
    public string ServerUrl => configuration?.ServerUrl ?? "";
    public PlaybackSnapshot Snapshot => coordinator?.Snapshot ?? new(PlayerStatus.Idle, null,
        configuration?.Random ?? RandomMode.All, configuration?.Repeat ?? RepeatMode.None, TimeSpan.Zero, "Ready to play");
    public event Action? Changed;

    public async Task RestoreAsync(CancellationToken cancellationToken = default)
    {
        await gate.WaitAsync(cancellationToken);
        try
        {
            configuration = await configurations.LoadAsync(cancellationToken);
            token = configuration is null ? null : await credentials.LoadAsync(configuration, cancellationToken);
        }
        finally { gate.Release(); Changed?.Invoke(); }
    }

    public async Task SignInAsync(string serverUrl, string username, string password, CancellationToken cancellationToken = default)
    {
        await gate.WaitAsync(cancellationToken);
        try
        {
            var requested = new PlayerConfiguration
            {
                ServerUrl = ServerAddress.Normalize(serverUrl), UserName = username.Trim(),
                DeviceId = configuration?.DeviceId ?? Guid.NewGuid().ToString("N")
            };
            using var login = createClient(requested, "");
            var authenticated = await login.AuthenticateAsync(requested.UserName, password, cancellationToken);
            await StopSessionAsync();
            await credentials.SaveAsync(authenticated.Configuration, authenticated.Token, cancellationToken);
            await configurations.SaveAsync(authenticated.Configuration, cancellationToken);
            configuration = authenticated.Configuration;
            token = authenticated.Token;
        }
        finally { gate.Release(); Changed?.Invoke(); }
    }

    public async Task PlayAsync(CancellationToken cancellationToken = default)
    {
        await gate.WaitAsync(cancellationToken);
        try
        {
            if (!IsSignedIn) throw new JellyJamException("Sign in to your Jellyfin server first.");
            if (coordinator is null)
            {
                client = createClient(configuration!, token!);
                IReadOnlyList<Track> tracks;
                try { tracks = await client.GetTracksAsync(cancellationToken); }
                catch { client.Dispose(); client = null; throw; }
                lifetime = new CancellationTokenSource();
                coordinator = new PlaybackCoordinator(client, audio, tracks, configuration!.Random, configuration.Repeat);
                runner = coordinator.RunAsync(lifetime.Token);
                monitor = MonitorAsync(lifetime.Token);
            }
            coordinator.Send(PlayerAction.Play);
        }
        finally { gate.Release(); Changed?.Invoke(); }
    }

    public void Send(PlayerAction action) => coordinator?.Send(action);

    public async Task SetModesAsync(RandomMode random, RepeatMode repeat, CancellationToken cancellationToken = default)
    {
        if (!Enum.IsDefined(random) || !Enum.IsDefined(repeat)) throw new ArgumentOutOfRangeException(nameof(random));
        await gate.WaitAsync(cancellationToken);
        try
        {
            if (configuration is null) return;
            if (coordinator is not null) await coordinator.SetModesAsync(random, repeat, cancellationToken);
            configuration = configuration with { Random = random, Repeat = repeat };
            await configurations.SaveAsync(configuration, cancellationToken);
        }
        finally { gate.Release(); Changed?.Invoke(); }
    }

    public async Task SignOutAsync(CancellationToken cancellationToken = default)
    {
        await gate.WaitAsync(cancellationToken);
        try
        {
            await StopSessionAsync();
            if (configuration is not null && !string.IsNullOrEmpty(token))
            {
                using var logout = createClient(configuration, token);
                using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(3));
                try { await logout.LogoutAsync(timeout.Token); }
                catch (Exception) { }
            }
            await credentials.DeleteAsync(CancellationToken.None);
            token = null;
        }
        finally { gate.Release(); Changed?.Invoke(); }
    }

    private async Task MonitorAsync(CancellationToken cancellationToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromMilliseconds(250));
        try
        {
            while (await timer.WaitForNextTickAsync(cancellationToken))
            {
                var snapshot = Snapshot;
                if (configuration is { } saved && (snapshot.Random != saved.Random || snapshot.Repeat != saved.Repeat))
                {
                    configuration = saved with { Random = snapshot.Random, Repeat = snapshot.Repeat };
                    try { await configurations.SaveAsync(configuration, cancellationToken); }
                    catch (Exception) when (!cancellationToken.IsCancellationRequested) { }
                }
                Changed?.Invoke();
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { }
    }

    private async Task StopSessionAsync()
    {
        if (lifetime is not null) await lifetime.CancelAsync();
        await Task.WhenAll(runner, monitor);
        coordinator = null;
        client?.Dispose();
        client = null;
        lifetime?.Dispose();
        lifetime = null;
    }

    public async ValueTask DisposeAsync()
    {
        await gate.WaitAsync();
        try { await StopSessionAsync(); }
        finally { gate.Release(); }
    }
}