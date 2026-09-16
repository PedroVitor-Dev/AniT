namespace AniT.Core;

public enum WatchStatus { NotStarted, Watching, Completed }
public enum AnimeAliasSource { Canonical, Imported, Manual, FilenameLearned }
public enum MediaFileAvailability { Available, Missing, RootUnavailable }
public enum LibraryReviewStatus { Pending, Resolved, Ignored }

public sealed class Anime
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public required string Title { get; set; }
    public string? EnglishTitle { get; set; }
    public string? OriginalTitle { get; set; }
    public string? Synopsis { get; set; }
    public string? CoverPath { get; set; }
    public double? CriticScore { get; set; }
    public bool IsFavorite { get; set; }
    public double? Rating { get; set; }
    public string? ReviewNotes { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public ICollection<Season> Seasons { get; set; } = new List<Season>();
    public ICollection<AnimeAlias> Aliases { get; set; } = new List<AnimeAlias>();
}

public sealed class AnimeAlias
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid AnimeId { get; set; }
    public required string Alias { get; set; }
    public required string NormalizedAlias { get; set; }
    public AnimeAliasSource Source { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public Anime? Anime { get; set; }
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
    public string? ReviewNotes { get; set; }
    public Season? Season { get; set; }
    public ICollection<MediaFile> MediaFiles { get; set; } = new List<MediaFile>();
    public PlaybackProgress? PlaybackProgress { get; set; }
}

/// <summary>
/// A physical representation of one logical episode. Playback state intentionally
/// remains on <see cref="Episode"/> so a rename, move or version change is harmless.
/// </summary>
public sealed class MediaFile
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid EpisodeId { get; set; }
    public Guid LibraryRootId { get; set; }
    public required string RelativePath { get; set; }
    public string FileName { get; set; } = string.Empty;
    public string Extension { get; set; } = string.Empty;
    public long SizeInBytes { get; set; }
    public DateTimeOffset LastModifiedAt { get; set; }
    public string? QuickHash { get; set; }
    public string? Resolution { get; set; }
    public string? ReleaseGroup { get; set; }
    public string? Language { get; set; }
    public DateTimeOffset ImportedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset LastSeenAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? MissingSince { get; set; }
    public MediaFileAvailability Availability { get; set; } = MediaFileAvailability.Available;
    public bool IsPreferred { get; set; }
    public Guid? DuplicateOfMediaFileId { get; set; }
    public Episode? Episode { get; set; }
    public LibraryRoot? LibraryRoot { get; set; }
}

public sealed class LibraryReviewItem
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid LibraryRootId { get; set; }
    public required string RelativePath { get; set; }
    public required string FileName { get; set; }
    public long SizeInBytes { get; set; }
    public DateTimeOffset LastModifiedAt { get; set; }
    public string? QuickHash { get; set; }
    public string? CandidateTitle { get; set; }
    public string? NormalizedTitle { get; set; }
    public int? SuggestedSeasonNumber { get; set; }
    public double? SuggestedEpisodeNumber { get; set; }
    public Guid? SuggestedAnimeId { get; set; }
    public double Confidence { get; set; }
    public string Reasons { get; set; } = string.Empty;
    public string? Resolution { get; set; }
    public string? ReleaseGroup { get; set; }
    public string? Language { get; set; }
    public LibraryReviewStatus Status { get; set; } = LibraryReviewStatus.Pending;
    public DateTimeOffset DiscoveredAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset LastSeenAt { get; set; } = DateTimeOffset.UtcNow;
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
    public bool IncludeSubfolders { get; set; } = true;
    public bool IsEnabled { get; set; } = true;
    public DateTimeOffset AddedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? LastScanAt { get; set; }
    public DateTimeOffset? LastUnavailableAt { get; set; }
    public ICollection<MediaFile> MediaFiles { get; set; } = new List<MediaFile>();
    public ICollection<LibraryReviewItem> ReviewItems { get; set; } = new List<LibraryReviewItem>();
}
