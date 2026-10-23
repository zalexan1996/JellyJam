using Android.Content;
using Android.Media;
using WakeLockFlags = Android.OS.WakeLockFlags;
using JellyJam.Core;
using JellyJam.Mobile.Services;

namespace JellyJam.Mobile;

public sealed class AndroidAudioPlayer(AndroidSpectrumSource spectrum) : Java.Lang.Object, IPlatformAudioPlayer, AudioManager.IOnAudioFocusChangeListener
{
    private long position;
    private MediaPlayer? active;
    private AudioFocusRequestClass? focus;
    private CancellationTokenSource? playing;
    private readonly AudioManager manager = (AudioManager)global::Android.App.Application.Context.GetSystemService(Context.AudioService)!;
    public event Action? Interrupted;
    public TimeSpan Position => TimeSpan.FromMilliseconds(Interlocked.Read(ref position));

    public async Task PlayAsync(PlaybackSource source, Action onStarted, CancellationToken cancellationToken)
    {
        using var lifetime = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var prepared = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var finished = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        IDispatcherTimer? timer = null;
        try
        {
            await MainThread.InvokeOnMainThreadAsync(() =>
            {
                lifetime.Token.ThrowIfCancellationRequested();
                var attributes = new AudioAttributes.Builder().SetUsage(AudioUsageKind.Media)!.SetContentType(AudioContentType.Music)!.Build()!;
                focus = new AudioFocusRequestClass.Builder(AudioFocus.Gain).SetAudioAttributes(attributes)!
                    .SetOnAudioFocusChangeListener(this)!.Build();
                if (manager.RequestAudioFocus(focus!) != AudioFocusRequest.Granted)
                    throw new JellyJamException("Another app is using audio. Try Play again.");
                active = new MediaPlayer();
                playing = lifetime;
                active.SetAudioAttributes(attributes);
                active.SetWakeMode(global::Android.App.Application.Context, WakeLockFlags.Partial);
                active.Prepared += (_, _) => prepared.TrySetResult();
                active.Completion += (_, _) => finished.TrySetResult();
                active.Error += (_, args) =>
                {
                    args.Handled = true;
                    var error = new JellyJamException("The audio stream could not be played. Check the server and transcoding permissions.", false);
                    prepared.TrySetException(error);
                    finished.TrySetException(error);
                };
                Interlocked.Exchange(ref position, 0);
                active.SetDataSource(global::Android.App.Application.Context, global::Android.Net.Uri.Parse(source.Uri.AbsoluteUri)!,
                    new Dictionary<string, string>
                    {
                        ["X-Emby-Token"] = source.AccessToken,
                        ["android-allow-cross-domain-redirect"] = "0"
                    });
                active.PrepareAsync();
            });
            await prepared.Task.WaitAsync(TimeSpan.FromSeconds(30), lifetime.Token);
            await MainThread.InvokeOnMainThreadAsync(() =>
            {
                lifetime.Token.ThrowIfCancellationRequested();
                active!.Start();
                spectrum.SetSession(active.AudioSessionId);
                timer = Dispatcher.GetForCurrentThread()!.CreateTimer();
                timer.Interval = TimeSpan.FromMilliseconds(250);
                timer.Tick += (_, _) =>
                {
                    if (active is not null) Interlocked.Exchange(ref position, active.CurrentPosition);
                };
                timer.Start();
                onStarted();
            });
            await finished.Task.WaitAsync(lifetime.Token);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch (OperationCanceledException) { throw new JellyJamException("Playback stopped because audio focus was lost."); }
        catch (TimeoutException) { throw new JellyJamException("The audio stream timed out.", false); }
        catch (JellyJamException) { throw; }
        catch (Exception) { throw new JellyJamException("Android could not open the audio stream.", false); }
        finally
        {
            await MainThread.InvokeOnMainThreadAsync(() =>
            {
                timer?.Stop();
                spectrum.SetSession(null);
                active?.Reset();
                active?.Release();
                active?.Dispose();
                active = null;
                playing = null;
                if (focus is not null) manager.AbandonAudioFocusRequest(focus);
                focus?.Dispose();
                focus = null;
            });
            _ = prepared.Task.Exception;
            _ = finished.Task.Exception;
        }
    }

    public void OnAudioFocusChange(AudioFocus focusChange)
    {
        if (focusChange is AudioFocus.Loss or AudioFocus.LossTransient or AudioFocus.LossTransientCanDuck)
        {
            Interrupted?.Invoke();
            playing?.Cancel();
        }
    }
}