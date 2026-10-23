using System.Threading.Channels;

namespace JellyJam.Core.Playback;

public sealed class PlaybackCoordinator
{
    private sealed record Message(PlayerAction? Action = null, long Generation = 0, bool Started = false, JellyJamException? Failure = null,
        RandomMode? Random = null, RepeatMode? Repeat = null, TaskCompletionSource? Completion = null);
    private readonly Channel<Message> messages = Channel.CreateUnbounded<Message>(new UnboundedChannelOptions { SingleReader = true });
    private readonly IJellyfinClient client;
    private readonly IAudioPlayer audio;
    private readonly IReadOnlyList<Track> tracks;
    private TrackSelector selector;
    private PlaybackSnapshot snapshot;
    private Task worker = Task.CompletedTask;
    private CancellationTokenSource? load;
    private long generation;
    private int running;

    public PlaybackCoordinator(IJellyfinClient client, IAudioPlayer audio, IReadOnlyList<Track> tracks, RandomMode random, RepeatMode repeat)
    {
        this.client = client;
        this.audio = audio;
        this.tracks = tracks;
        selector = new TrackSelector(tracks, random, repeat);
        snapshot = new(PlayerStatus.Idle, null, random, repeat, TimeSpan.Zero, "Ready");
    }

    public PlaybackSnapshot Snapshot
    {
        get
        {
            var state = Volatile.Read(ref snapshot);
            return state.Status == PlayerStatus.Playing ? state with { Position = audio.Position } : state;
        }
    }

    public void Send(PlayerAction action) => messages.Writer.TryWrite(new Message(Action: action));

    public async Task SetModesAsync(RandomMode random, RepeatMode repeat, CancellationToken cancellationToken = default)
    {
        if (!Enum.IsDefined(random) || !Enum.IsDefined(repeat)) throw new ArgumentOutOfRangeException(nameof(random));
        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        await messages.Writer.WriteAsync(new Message(Random: random, Repeat: repeat, Completion: completion), cancellationToken);
        await completion.Task.WaitAsync(cancellationToken);
    }

    public async Task RunAsync(CancellationToken cancellationToken)
    {
        if (Interlocked.Exchange(ref running, 1) != 0) throw new InvalidOperationException("A session can only run once.");
        try
        {
            await foreach (var message in messages.Reader.ReadAllAsync(cancellationToken))
                Handle(message, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { }
        finally
        {
            CancelCurrent();
            await worker;
            messages.Writer.TryComplete();
            Publish(PlayerStatus.Stopped, "Session ended");
        }
    }

    private void Handle(Message message, CancellationToken cancellationToken)
    {
        if (message.Random is { } random && message.Repeat is { } repeat)
            ApplyModes(random, repeat, message.Completion);
        else if (message.Action is { } action)
            Handle(action, cancellationToken);
        else if (message.Generation == generation)
            HandleTrackEvent(message, cancellationToken);
    }

    private void ApplyModes(RandomMode random, RepeatMode repeat, TaskCompletionSource? completion)
    {
        selector.SetModes(random, repeat);
        Publish(snapshot.Status, "Playback modes changed");
        completion?.TrySetResult();
    }

    private void Handle(PlayerAction action, CancellationToken cancellationToken)
    {
        switch (action)
        {
            case PlayerAction.Play:
                if (snapshot.Status is PlayerStatus.Playing or PlayerStatus.Loading) break;
                selector = new TrackSelector(tracks, selector.RandomMode, selector.RepeatMode);
                Start(selector.Next(), cancellationToken);
                break;
            case PlayerAction.Next:
                selector.DiscardPending();
                Start(selector.Next(), cancellationToken);
                break;
            case PlayerAction.Previous:
                var discarded = selector.DiscardPending();
                var previous = discarded ? selector.Current : selector.Previous();
                if (discarded || previous is not null) Start(previous, cancellationToken);
                break;
            case PlayerAction.CycleRandom:
                selector.SetModes(NextValue(selector.RandomMode), selector.RepeatMode);
                Publish(snapshot.Status, "Random mode changed");
                break;
            case PlayerAction.CycleRepeat:
                selector.SetModes(selector.RandomMode, NextValue(selector.RepeatMode));
                Publish(snapshot.Status, "Repeat mode changed");
                break;
            case PlayerAction.Stop:
                selector.DiscardPending();
                CancelCurrent();
                Publish(PlayerStatus.Stopped, "Stopped");
                break;
        }
    }

    private void HandleTrackEvent(Message message, CancellationToken cancellationToken)
    {
        if (message.Started)
        {
            selector.ConfirmCurrent();
            Publish(PlayerStatus.Playing, "Streaming");
        }
        else if (message.Failure is { } failure)
            HandleFailure(failure, cancellationToken);
        else Start(selector.Next(naturalCompletion: true), cancellationToken);
    }

    private void HandleFailure(JellyJamException failure, CancellationToken cancellationToken)
    {
        if (failure.Fatal)
        {
            CancelCurrent();
            Publish(PlayerStatus.Error, failure.Message);
            return;
        }
        if (selector.Current is { } failed) selector.MarkFailed(failed);
        Start(selector.Next(), cancellationToken, failure.Message);
    }

    private static T NextValue<T>(T current) where T : struct, Enum
    {
        var values = Enum.GetValues<T>();
        return values[(Array.IndexOf(values, current) + 1) % values.Length];
    }

    private void Start(Track? track, CancellationToken cancellationToken, string? failure = null)
    {
        CancelCurrent();
        if (track is null)
        {
            Publish(failure is null ? PlayerStatus.Stopped : PlayerStatus.Error,
                failure is null ? tracks.Count == 0 ? "No music is available to this account" : "Selection complete" : $"No playable tracks remain. {failure}");
            return;
        }
        load = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        Publish(PlayerStatus.Loading, failure is null ? "Connecting to stream" : "Skipping an unavailable track");
        worker = RunTrackAsync(worker, track, generation, load.Token);
    }

    private void CancelCurrent()
    {
        generation++;
        load?.Cancel();
        load?.Dispose();
        load = null;
    }

    private async Task RunTrackAsync(Task previous, Track track, long currentGeneration, CancellationToken cancellationToken)
    {
        PlaybackSource? source = null;
        Message? result = null;
        try
        {
            await previous;
            cancellationToken.ThrowIfCancellationRequested();
            source = await client.ResolveAsync(track, cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            await audio.PlayAsync(source, () => messages.Writer.TryWrite(new Message(Generation: currentGeneration, Started: true)), cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            result = new Message(Generation: currentGeneration);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { }
        catch (JellyJamException error) { result = new Message(Generation: currentGeneration, Failure: error); }
        catch (Exception) { result = new Message(Generation: currentGeneration, Failure: new JellyJamException("Playback failed. Check the connection and audio device.")); }
        finally
        {
            if (source is not null)
            {
                using var cleanup = new CancellationTokenSource(TimeSpan.FromSeconds(2));
                try { await client.ReleaseAsync(source, cleanup.Token); }
                catch (Exception) { }
            }
        }
        if (result is not null && !cancellationToken.IsCancellationRequested) messages.Writer.TryWrite(result);
    }

    private void Publish(PlayerStatus status, string message)
        => Volatile.Write(ref snapshot, new PlaybackSnapshot(status, selector.Current, selector.RandomMode, selector.RepeatMode, TimeSpan.Zero, message));
}