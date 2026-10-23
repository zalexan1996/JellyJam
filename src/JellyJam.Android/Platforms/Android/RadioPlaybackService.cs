using Android.App;
using Android.Content;
using Android.Content.PM;
using Android.Graphics.Drawables;
using Android.Media;
using Android.Media.Session;
using Android.OS;
using JellyJam.Core;
using JellyJam.Core.Playback;
using AApplication = Android.App.Application;

namespace JellyJam.Mobile;

[Service(Exported = false, ForegroundServiceType = ForegroundService.TypeMediaPlayback)]
public sealed class RadioPlaybackService : Service
{
    private const string ChannelId = "jellyjam.playback";
    private const int NotificationId = 41;
    private static TaskCompletionSource? ready;
    private static RadioPlaybackService? instance;
    private RadioSession radio = null!;
    private MediaSession? media;
    private DateTime lastUpdate;
    private NoisyReceiver? noisy;

    public static async Task EnsureStartedAsync(CancellationToken cancellationToken)
    {
        await MainThread.InvokeOnMainThreadAsync(() =>
        {
            if (instance is not null) return;
            ready = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            AApplication.Context.StartForegroundService(new Intent(AApplication.Context, typeof(RadioPlaybackService)));
        });
        if (ready is not null) await ready.Task.WaitAsync(TimeSpan.FromSeconds(10), cancellationToken);
    }

    public static void StopPlaybackService()
        => AApplication.Context.StopService(new Intent(AApplication.Context, typeof(RadioPlaybackService)));

    public override void OnCreate()
    {
        base.OnCreate();
        instance = this;
        radio = MauiProgram.Services.GetRequiredService<RadioSession>();
        var notifications = (NotificationManager)GetSystemService(NotificationService)!;
        notifications.CreateNotificationChannel(new NotificationChannel(ChannelId, "Radio playback", NotificationImportance.Low));
        media = new MediaSession(this, "JellyJam");
        media.SetCallback(new Controls(radio));
        media.Active = true;
        noisy = new NoisyReceiver(radio);
        if (OperatingSystem.IsAndroidVersionAtLeast(33))
            RegisterReceiver(noisy, new IntentFilter(AudioManager.ActionAudioBecomingNoisy), ReceiverFlags.NotExported);
        else RegisterReceiver(noisy, new IntentFilter(AudioManager.ActionAudioBecomingNoisy));
        radio.Changed += Refresh;
    }

    public override StartCommandResult OnStartCommand(Intent? intent, StartCommandFlags flags, int startId)
    {
        var notification = CreateNotification(radio.Snapshot);
        if (OperatingSystem.IsAndroidVersionAtLeast(29)) StartForeground(NotificationId, notification, ForegroundService.TypeMediaPlayback);
        else StartForeground(NotificationId, notification);
        ready?.TrySetResult();
        switch (intent?.Action)
        {
            case "previous": radio.Send(PlayerAction.Previous); break;
            case "next": radio.Send(PlayerAction.Next); break;
            case "stop": radio.Send(PlayerAction.Stop); StopSelf(); break;
        }
        return StartCommandResult.NotSticky;
    }

    private void Refresh()
    {
        MainThread.BeginInvokeOnMainThread(() =>
        {
            if (instance != this) return;
            var snapshot = radio.Snapshot;
            if (snapshot.Status is PlayerStatus.Stopped or PlayerStatus.Error)
            {
                StopSelf();
                return;
            }
            if (DateTime.UtcNow - lastUpdate < TimeSpan.FromSeconds(1)) return;
            lastUpdate = DateTime.UtcNow;
            ((NotificationManager)GetSystemService(NotificationService)!).Notify(NotificationId, CreateNotification(snapshot));
        });
    }

    private Notification CreateNotification(PlaybackSnapshot snapshot)
    {
        var open = new Intent(this, typeof(MainActivity)).AddFlags(ActivityFlags.SingleTop | ActivityFlags.ClearTop);
        var launch = PendingIntent.GetActivity(this, 0, open, PendingIntentFlags.Immutable | PendingIntentFlags.UpdateCurrent);
        media?.SetMetadata(new MediaMetadata.Builder()
            .PutString(MediaMetadata.MetadataKeyTitle, snapshot.Track?.Title ?? "JellyJam")!
            .PutString(MediaMetadata.MetadataKeyArtist, snapshot.Track?.Artist ?? "Connecting")!
            .PutLong(MediaMetadata.MetadataKeyDuration, (long)(snapshot.Track?.Duration?.TotalMilliseconds ?? 0))!.Build());
        media?.SetPlaybackState(new PlaybackState.Builder()
            .SetActions(PlaybackState.ActionStop | PlaybackState.ActionSkipToNext | PlaybackState.ActionSkipToPrevious)!
            .SetState(snapshot.Status == PlayerStatus.Playing ? PlaybackStateCode.Playing : PlaybackStateCode.Buffering,
                (long)snapshot.Position.TotalMilliseconds, 1)!.Build());
        return new Notification.Builder(this, ChannelId)
            .SetSmallIcon(Resource.Drawable.radio_notification)!
            .SetContentTitle(snapshot.Track?.Title ?? "JellyJam")!
            .SetContentText(snapshot.Track?.Artist ?? "Connecting to your radio")!
            .SetContentIntent(launch)!
            .SetVisibility(NotificationVisibility.Public)!
            .SetOnlyAlertOnce(true)!
            .SetOngoing(true)!
            .AddAction(new Notification.Action.Builder(Icon.CreateWithResource(this, Android.Resource.Drawable.IcMediaPrevious), "Previous", Command("previous", 1)).Build())!
            .AddAction(new Notification.Action.Builder(Icon.CreateWithResource(this, Resource.Drawable.radio_notification), "Stop", Command("stop", 2)).Build())!
            .AddAction(new Notification.Action.Builder(Icon.CreateWithResource(this, Android.Resource.Drawable.IcMediaNext), "Next", Command("next", 3)).Build())!
            .SetStyle(new Notification.MediaStyle().SetMediaSession(media!.SessionToken)!.SetShowActionsInCompactView(0, 1, 2))!
            .Build();
    }

    private PendingIntent Command(string action, int requestCode)
        => PendingIntent.GetService(this, requestCode, new Intent(this, typeof(RadioPlaybackService)).SetAction(action),
            PendingIntentFlags.Immutable | PendingIntentFlags.UpdateCurrent)!;

    public override IBinder? OnBind(Intent? intent) => null;

    public override void OnDestroy()
    {
        radio.Changed -= Refresh;
        radio.Send(PlayerAction.Stop);
        if (noisy is not null) { UnregisterReceiver(noisy); noisy.Dispose(); }
        media?.Release();
        media?.Dispose();
        instance = null;
        ready = null;
        StopForeground(StopForegroundFlags.Remove);
        base.OnDestroy();
    }

    private sealed class Controls(RadioSession radio) : MediaSession.Callback
    {
        public override void OnSkipToNext() => radio.Send(PlayerAction.Next);
        public override void OnSkipToPrevious() => radio.Send(PlayerAction.Previous);
        public override void OnStop() => radio.Send(PlayerAction.Stop);
        public override void OnPause() => radio.Send(PlayerAction.Stop);
    }

    private sealed class NoisyReceiver(RadioSession radio) : BroadcastReceiver
    {
        public override void OnReceive(Context? context, Intent? intent) => radio.Send(PlayerAction.Stop);
    }
}