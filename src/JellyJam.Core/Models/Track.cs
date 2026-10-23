namespace JellyJam.Core;

public sealed record Track(
    Guid Id,
    string Title,
    string Artist,
    string Album,
    Guid? ArtistId,
    Guid? AlbumId,
    int Disc,
    int Number,
    TimeSpan? Duration);
