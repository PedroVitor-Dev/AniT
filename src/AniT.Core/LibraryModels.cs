namespace AniT.Core;

public enum WatchStatus
{
    NotStarted,
    Watching,
    Completed
}

public sealed class Anime
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public required string Title { get; set; }
    public string? OriginalTitle { get; set; }
    public string? Synopsis { get; set; }
    public string? CoverPath { get; set; }
    public bool IsFavorite { get; set; }
    public double? Rating { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public ICollection<Season> Seasons { get; set; } = new List<Season>();
}

public sealed class Season
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid AnimeId { get; set; }
    public int Number { get; set; }
    public string? Name { get; set; }
    public Anime? Anime { get; set; }
    public ICollection<Episode> Episodes { get; set; } = new List<Episode>();
}

public sealed class Episode
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid SeasonId { get; set; }
    public int Number { get; set; }
    public string? Title { get; set; }
    public WatchStatus Status { get; set; } = WatchStatus.NotStarted;
    public DateTimeOffset? WatchedAt { get; set; }
    public double? Rating { get; set; }
    public Season? Season { get; set; }
    public MediaFile? MediaFile { get; set; }
    public PlaybackProgress? PlaybackProgress { get; set; }
}

public sealed class MediaFile
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid EpisodeId { get; set; }
    public Guid LibraryRootId { get; set; }
    public required string RelativePath { get; set; }
    public long SizeInBytes { get; set; }
    public DateTimeOffset LastModifiedAt { get; set; }
    public string? QuickHash { get; set; }
    public Episode? Episode { get; set; }
    public LibraryRoot? LibraryRoot { get; set; }
}

public sealed class PlaybackProgress
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid EpisodeId { get; set; }
    public double PositionSeconds { get; set; }
    public double DurationSeconds { get; set; }
    public DateTimeOffset LastPlayedAt { get; set; } = DateTimeOffset.UtcNow;
    public Episode? Episode { get; set; }
}

public sealed class LibraryRoot
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public required string Path { get; set; }
    public string DisplayName { get; set; } = "Minha biblioteca";
    public DateTimeOffset AddedAt { get; set; } = DateTimeOffset.UtcNow;
    public ICollection<MediaFile> MediaFiles { get; set; } = new List<MediaFile>();
}
