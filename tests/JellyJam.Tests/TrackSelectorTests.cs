using JellyJam.Core;
using JellyJam.Core.Playback;

namespace JellyJam.Tests;

public class TrackSelectorTests
{
    [Fact]
    public void UnstartedSelectionsAreNotPlaybackHistory()
    {
        var tracks = Library();
        var selector = new TrackSelector(tracks, RandomMode.None, RepeatMode.None);
        Assert.Equal(tracks[0], selector.Next());
        selector.ConfirmCurrent();
        Assert.Equal(tracks[1], selector.Next());
        Assert.True(selector.DiscardPending());
        Assert.Equal(tracks[2], selector.Next());
        selector.ConfirmCurrent();
        Assert.Equal(tracks[0], selector.Previous());
    }

    internal static Track[] Library()
    {
        var artist = Guid.NewGuid();
        var album = Guid.NewGuid();
        return [
            new(Guid.NewGuid(), "First", "Artist A", "Album A", artist, album, 1, 1, TimeSpan.FromMinutes(3)),
            new(Guid.NewGuid(), "Second", "Artist A", "Album A", artist, album, 1, 2, TimeSpan.FromMinutes(3)),
            new(Guid.NewGuid(), "Third", "Artist A", "Album B", artist, Guid.NewGuid(), 1, 1, null),
            new(Guid.NewGuid(), "Fourth", "Artist B", "Album C", Guid.NewGuid(), Guid.NewGuid(), 1, 1, null)];
    }

    [Fact]
    public void OrderedTraversalExhaustsAndPreviousReplaysHistory()
    {
        var tracks = Library();
        var selector = new TrackSelector(tracks.Reverse(), RandomMode.None, RepeatMode.None);
        Assert.Null(selector.Previous());
        Assert.Equal(tracks[0], selector.Next());
        Assert.Equal(tracks[1], selector.Next());
        Assert.Equal(tracks[0], selector.Previous());
        Assert.Equal(tracks[1], selector.Next());
        Assert.Equal(tracks[2], selector.Next());
        Assert.Equal(tracks[3], selector.Next());
        Assert.Null(selector.Next());
    }

    [Fact]
    public void ShuffleDoesNotRepeatBeforeExhaustion()
    {
        var tracks = Library();
        var selector = new TrackSelector(tracks, RandomMode.All, RepeatMode.None, new Random(17));
        var actual = Enumerable.Range(0, tracks.Length).Select(_ => selector.Next()!.Id).ToArray();
        Assert.Equal(tracks.Length, actual.Distinct().Count());
        Assert.Null(selector.Next());
    }

    [Fact]
    public void SongRepeatOnlyAppliesToNaturalCompletion()
    {
        var tracks = Library();
        var selector = new TrackSelector(tracks, RandomMode.None, RepeatMode.Song);
        Assert.Equal(tracks[0], selector.Next());
        Assert.Equal(tracks[0], selector.Next(true));
        Assert.Equal(tracks[1], selector.Next());
    }

    [Theory]
    [InlineData(RandomMode.All)]
    [InlineData(RandomMode.Artist)]
    [InlineData(RandomMode.Album)]
    [InlineData(RandomMode.None)]
    public void AlbumRepeatOverridesRandomScope(RandomMode randomMode)
    {
        var selector = new TrackSelector(Library(), RandomMode.None, RepeatMode.None, new Random(17));
        var first = selector.Next()!;
        selector.SetModes(randomMode, RepeatMode.Album);
        for (var index = 0; index < 20; index++) Assert.Equal(first.AlbumId, selector.Next()!.AlbumId);
    }

    [Fact]
    public void ModeChangeKeepsPastAndReplacesFutureHistory()
    {
        var tracks = Library();
        var selector = new TrackSelector(tracks, RandomMode.None, RepeatMode.None);
        selector.Next();
        selector.Next();
        selector.Next();
        selector.Previous();
        selector.SetModes(RandomMode.Album, RepeatMode.None);
        Assert.Equal(tracks[0], selector.Previous());
        Assert.Equal(tracks[1], selector.Next());
        Assert.Equal(tracks[0], selector.Next());
        Assert.Null(selector.Next());
    }

    [Fact]
    public void UnknownAlbumsAreNotGroupedTogether()
    {
        var tracks = Library().Select(track => track with { AlbumId = null });
        var selector = new TrackSelector(tracks, RandomMode.Album, RepeatMode.None);
        Assert.NotNull(selector.Next());
        Assert.Null(selector.Next());
    }

    [Fact]
    public void EmptyAndFailedRepeatScopesTerminate()
    {
        Assert.Null(new TrackSelector([], RandomMode.All, RepeatMode.Album).Next());
        var track = Library()[0];
        var selector = new TrackSelector([track], RandomMode.All, RepeatMode.Album);
        Assert.Equal(track, selector.Next());
        Assert.Equal(track, selector.Next());
        selector.MarkFailed(track);
        Assert.Null(selector.Next());
    }
}