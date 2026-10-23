namespace JellyJam.Core.Playback;

public sealed class TrackSelector
{
    private readonly Track[] library;
    private readonly Random random;
    private readonly List<Track> history = [];
    private readonly HashSet<Guid> failed = [];
    private Queue<Track> upcoming = new();
    private Track? anchor;
    private int cursor = -1;
    private bool pending;

    public TrackSelector(IEnumerable<Track> tracks, RandomMode randomMode, RepeatMode repeatMode, Random? random = null)
    {
        library = tracks.DistinctBy(track => track.Id)
            .OrderBy(track => track.Artist, StringComparer.OrdinalIgnoreCase)
            .ThenBy(track => track.Album, StringComparer.OrdinalIgnoreCase)
            .ThenBy(track => track.Disc).ThenBy(track => track.Number).ThenBy(track => track.Id).ToArray();
        this.random = random ?? Random.Shared;
        SetModes(randomMode, repeatMode);
    }

    public RandomMode RandomMode { get; private set; }
    public RepeatMode RepeatMode { get; private set; }
    public Track? Current => cursor >= 0 ? history[cursor] : null;

    public void SetModes(RandomMode randomMode, RepeatMode repeatMode)
    {
        if (!Enum.IsDefined(randomMode) || !Enum.IsDefined(repeatMode))
            throw new ArgumentOutOfRangeException(nameof(randomMode));
        RandomMode = randomMode;
        RepeatMode = repeatMode;
        anchor = Current;
        if (cursor + 1 < history.Count)
            history.RemoveRange(cursor + 1, history.Count - cursor - 1);
        Refill(Current?.Id);
    }

    public Track? Next(bool naturalCompletion = false)
    {
        if (naturalCompletion && RepeatMode == RepeatMode.Song && Current is { } current && !failed.Contains(current.Id))
            return current;
        while (cursor + 1 < history.Count)
        {
            cursor++;
            if (!failed.Contains(history[cursor].Id)) return history[cursor];
        }
        while (upcoming.TryPeek(out var candidate) && failed.Contains(candidate.Id)) upcoming.Dequeue();
        if (upcoming.Count == 0 && RepeatMode == RepeatMode.Album && anchor is not null) Refill();
        if (!upcoming.TryDequeue(out var next)) return null;
        history.Add(next);
        cursor = history.Count - 1;
        pending = true;
        if (anchor is null)
        {
            anchor = next;
            if (RandomMode is RandomMode.Artist or RandomMode.Album || RepeatMode == RepeatMode.Album)
                Refill(next.Id);
        }
        return next;
    }

    public Track? Previous()
    {
        for (var index = cursor - 1; index >= 0; index--)
        {
            if (failed.Contains(history[index].Id)) continue;
            cursor = index;
            return history[cursor];
        }
        return null;
    }

    public void MarkFailed(Track track) => failed.Add(track.Id);

    public void ConfirmCurrent() => pending = false;

    public bool DiscardPending()
    {
        if (!pending || cursor != history.Count - 1) return false;
        history.RemoveAt(cursor--);
        pending = false;
        return true;
    }

    private void Refill(Guid? exclude = null)
    {
        var candidates = library.Where(track => !failed.Contains(track.Id) && track.Id != exclude);
        if (anchor is not null)
        {
            if (RepeatMode == RepeatMode.Album || RandomMode == RandomMode.Album)
                candidates = candidates.Where(track => anchor.AlbumId is { } albumId ? track.AlbumId == albumId : track.Id == anchor.Id);
            else if (RandomMode == RandomMode.Artist)
                candidates = candidates.Where(track => anchor.ArtistId is { } artistId ? track.ArtistId == artistId : track.Id == anchor.Id);
        }
        var ordered = candidates.ToArray();
        if (RandomMode != RandomMode.None) random.Shuffle(ordered);
        upcoming = new Queue<Track>(ordered);
    }
}