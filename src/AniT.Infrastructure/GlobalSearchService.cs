using AniT.Core;
using Microsoft.EntityFrameworkCore;

namespace AniT.Infrastructure;

public sealed class GlobalSearchService(Func<AniTDbContext> contextFactory)
{
    private static readonly TimeSpan CacheLifetime = TimeSpan.FromSeconds(30);
    private readonly SemaphoreSlim cacheGate = new(1, 1);
    private IReadOnlyList<SearchAnime> cachedAnime = [];
    private DateTimeOffset cacheExpiresAt;

    public async Task<IReadOnlyList<GlobalSearchResult>> SearchAsync(
        string? searchText,
        int maximumResults = 12,
        CancellationToken cancellationToken = default)
    {
        var query = searchText?.Trim() ?? string.Empty;
        if (AnimeTitleNormalizer.Normalize(query).Length < 2) return [];

        var anime = await GetIndexAsync(cancellationToken);
        var organizationSettings = AniTSystemSettingsStore.Load();
        var candidates = new List<RankedResult>();
        var normalizedQuery = AnimeTitleNormalizer.Normalize(query);
        var hasEpisodeIntent = HasEpisodeIntent(normalizedQuery);

        foreach (var item in anime)
        {
            var preferredTitle = organizationSettings.AnimeTitlePreference switch
            {
                AnimeTitlePreference.Portuguese => item.Aliases.FirstOrDefault() ?? item.Title,
                AnimeTitlePreference.English => item.EnglishTitle ?? item.Title,
                AnimeTitlePreference.Japanese => item.OriginalTitle ?? item.Title,
                _ => item.Title
            };
            var titles = new[] { item.Title, item.EnglishTitle, item.OriginalTitle }
                .Concat(item.Aliases)
                .ToArray();
            var genresText = AnimeGenreCatalog.ToSearchText(item.Genres);
            var titleMatch = AnimeSearch.Matches(query, titles);
            var genreMatch = AnimeSearch.Matches(query, genresText);

            if (titleMatch || genreMatch)
            {
                var displayGenres = AnimeGenreCatalog.Parse(item.Genres)
                    .Select(AnimeGenreCatalog.DisplayName)
                    .Take(3)
                    .ToArray();
                var subtitle = displayGenres.Length > 0
                    ? string.Join(" • ", displayGenres)
                    : !string.IsNullOrWhiteSpace(item.EnglishTitle)
                        ? item.EnglishTitle!
                        : $"{item.Episodes.Count} episódio{(item.Episodes.Count == 1 ? string.Empty : "s")}";
                candidates.Add(new RankedResult(
                    titleMatch ? RankTitle(normalizedQuery, titles) : 45,
                    new GlobalSearchResult(
                        GlobalSearchResultKind.Anime,
                        item.Id,
                        null,
                        null,
                        preferredTitle,
                        subtitle,
                        "ANIME",
                        "◉",
                        "#59D8FF")));
            }

            foreach (var episode in item.Episodes)
            {
                var episodeTitleMatch = !string.IsNullOrWhiteSpace(episode.Title)
                    && AnimeSearch.Matches(query, episode.Title);
                var episodeValues = titles
                    .Append(episode.Title)
                    .Append(EpisodeSearchText(episode))
                    .Concat(episode.FileNames)
                    .ToArray();
                var episodeMatch = hasEpisodeIntent && AnimeSearch.Matches(query, episodeValues);
                if (!episodeTitleMatch && !episodeMatch)
                {
                    continue;
                }

                candidates.Add(new RankedResult(
                    episodeTitleMatch ? RankTitle(normalizedQuery, [episode.Title]) : 12,
                    new GlobalSearchResult(
                        GlobalSearchResultKind.Episode,
                        item.Id,
                        episode.Id,
                        null,
                        EpisodeDisplayName.Format(preferredTitle, episode.SeasonNumber, episode.Number, organizationSettings.EpisodeNumberDisplayFormat.ToString()),
                        $"{preferredTitle}  •  Temporada {episode.SeasonNumber}  •  {OrganizationPreferences.EpisodeCode(episode.SeasonNumber, episode.Number, organizationSettings.EpisodeNumberDisplayFormat)}",
                        "EPISÓDIO",
                        "▶",
                        "#8CE8C5")));
            }
        }

        foreach (var genre in AnimeGenreCatalog.All.Where(item => AnimeSearch.Matches(query, item.SearchText)))
        {
            var count = anime.Count(item => AnimeGenreCatalog.Parse(item.Genres)
                .Contains(genre.CanonicalName, StringComparer.OrdinalIgnoreCase));
            candidates.Add(new RankedResult(
                RankTitle(normalizedQuery, [genre.DisplayName, genre.CanonicalName, .. genre.Aliases]) + 4,
                new GlobalSearchResult(
                    GlobalSearchResultKind.Genre,
                    null,
                    null,
                    genre.DisplayName,
                    genre.DisplayName,
                    count == 0
                        ? "Explorar este gênero"
                        : $"{count} anime{(count == 1 ? string.Empty : "s")} na sua biblioteca",
                    "GÊNERO",
                    "✦",
                    "#D59BFF")));
        }

        return candidates
            .OrderBy(item => item.Rank)
            .ThenBy(item => item.Result.Kind)
            .ThenBy(item => item.Result.Title, StringComparer.CurrentCultureIgnoreCase)
            .Select(item => item.Result)
            .DistinctBy(item => new { item.Kind, item.AnimeId, item.EpisodeId, item.GenreName })
            .Take(Math.Max(1, maximumResults))
            .ToArray();
    }

    public void Invalidate() => cacheExpiresAt = DateTimeOffset.MinValue;

    private async Task<IReadOnlyList<SearchAnime>> GetIndexAsync(CancellationToken cancellationToken)
    {
        if (DateTimeOffset.UtcNow < cacheExpiresAt) return cachedAnime;

        await cacheGate.WaitAsync(cancellationToken);
        try
        {
            if (DateTimeOffset.UtcNow < cacheExpiresAt) return cachedAnime;

            await using var context = contextFactory();
            var entities = await context.Anime
                .Include(item => item.Aliases)
                .Include(item => item.Seasons)
                .ThenInclude(item => item.Episodes)
                .ThenInclude(item => item.MediaFiles)
                .AsNoTracking()
                .AsSplitQuery()
                .OrderBy(item => item.Title)
                .ToListAsync(cancellationToken);

            cachedAnime = entities.Select(item => new SearchAnime(
                    item.Id,
                    item.Title,
                    item.EnglishTitle,
                    item.OriginalTitle,
                    item.Genres,
                    item.Aliases.Select(alias => alias.Alias).ToArray(),
                    item.Seasons.SelectMany(season => season.Episodes.Select(episode => new SearchEpisode(
                            episode.Id,
                            season.Number,
                            episode.Number,
                            episode.Title,
                            episode.MediaFiles
                                .Select(file => file.FileName)
                                .Where(fileName => !string.IsNullOrWhiteSpace(fileName))
                                .ToArray())))
                        .OrderBy(episode => episode.SeasonNumber)
                        .ThenBy(episode => episode.Number)
                        .ToArray()))
                .ToArray();
            cacheExpiresAt = DateTimeOffset.UtcNow.Add(CacheLifetime);
            return cachedAnime;
        }
        finally
        {
            cacheGate.Release();
        }
    }

    private static bool HasEpisodeIntent(string normalizedQuery) =>
        normalizedQuery.Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .Any(IsEpisodeIntentTerm);

    private static bool IsEpisodeIntentTerm(string term)
    {
        if (term is "ep" or "episodio" or "episode" or "capitulo" || int.TryParse(term, out _)) return true;
        if (term.StartsWith("ep", StringComparison.Ordinal) && int.TryParse(term.AsSpan(2), out _)) return true;

        var seasonEpisodeSeparator = term.IndexOf('e', StringComparison.Ordinal);
        if (term.StartsWith('s')
            && seasonEpisodeSeparator > 1
            && int.TryParse(term.AsSpan(1, seasonEpisodeSeparator - 1), out _)
            && int.TryParse(term.AsSpan(seasonEpisodeSeparator + 1), out _))
        {
            return true;
        }

        var compactSeparator = term.IndexOf('x', StringComparison.Ordinal);
        return compactSeparator > 0
            && int.TryParse(term.AsSpan(0, compactSeparator), out _)
            && int.TryParse(term.AsSpan(compactSeparator + 1), out _);
    }

    private static string EpisodeSearchText(SearchEpisode episode)
    {
        var episodeNumber = episode.Number.ToString(System.Globalization.CultureInfo.InvariantCulture);
        var paddedEpisodeNumber = episode.Number.ToString("00", System.Globalization.CultureInfo.InvariantCulture);
        var seasonNumber = episode.SeasonNumber.ToString(System.Globalization.CultureInfo.InvariantCulture);
        var paddedSeasonNumber = episode.SeasonNumber.ToString("00", System.Globalization.CultureInfo.InvariantCulture);
        return $"episódio {episodeNumber} episódio {paddedEpisodeNumber} " +
               $"episodio {episodeNumber} episodio {paddedEpisodeNumber} " +
               $"episode {episodeNumber} episode {paddedEpisodeNumber} " +
               $"ep {episodeNumber} ep {paddedEpisodeNumber} e{paddedEpisodeNumber} " +
               $"temporada {seasonNumber} season {seasonNumber} " +
               $"s{paddedSeasonNumber}e{paddedEpisodeNumber} {seasonNumber}x{paddedEpisodeNumber}";
    }

    private static int RankTitle(string normalizedQuery, IEnumerable<string?> values)
    {
        var normalized = values
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Select(AnimeTitleNormalizer.Normalize)
            .ToArray();
        if (normalized.Any(value => value == normalizedQuery)) return 0;
        if (normalized.Any(value => value.StartsWith(normalizedQuery, StringComparison.Ordinal))) return 2;
        return 6;
    }

    private sealed record RankedResult(int Rank, GlobalSearchResult Result);
    private sealed record SearchAnime(
        Guid Id,
        string Title,
        string? EnglishTitle,
        string? OriginalTitle,
        string? Genres,
        IReadOnlyList<string> Aliases,
        IReadOnlyList<SearchEpisode> Episodes);
    private sealed record SearchEpisode(
        Guid Id,
        int SeasonNumber,
        int Number,
        string? Title,
        IReadOnlyList<string> FileNames);
}
