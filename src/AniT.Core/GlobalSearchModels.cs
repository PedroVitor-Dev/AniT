namespace AniT.Core;

public enum GlobalSearchResultKind
{
    Anime,
    Episode,
    Genre
}

public sealed record GlobalSearchResult(
    GlobalSearchResultKind Kind,
    Guid? AnimeId,
    Guid? EpisodeId,
    string? GenreName,
    string Title,
    string Subtitle,
    string TypeLabel,
    string Glyph,
    string Accent);
