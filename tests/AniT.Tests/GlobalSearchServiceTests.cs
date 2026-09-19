using AniT.Core;
using AniT.Infrastructure;
using Microsoft.Data.Sqlite;

namespace AniT.Tests;

public sealed class GlobalSearchServiceTests
{
    [Fact]
    public async Task Search_FindsAnimeAliasesEpisodesAndLocalizedGenres()
    {
        var directory = Path.Combine(Path.GetTempPath(), "AniT.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var databasePath = Path.Combine(directory, "search.db");

        try
        {
            using (var context = AniTDatabase.Create(databasePath))
            {
                var anime = new Anime
                {
                    Title = "Mahou Shoujo Magical Destroyers",
                    EnglishTitle = "Magical Girl Destroyers",
                    Genres = "Action|Fantasy",
                    Aliases =
                    {
                        new AnimeAlias
                        {
                            Alias = "Magical Destroyers",
                            NormalizedAlias = AnimeTitleNormalizer.Normalize("Magical Destroyers"),
                            Source = AnimeAliasSource.Manual
                        }
                    },
                    Seasons =
                    {
                        new Season
                        {
                            Number = 1,
                            Episodes =
                            {
                                new Episode { Number = 10, Title = "Novo mundo" }
                            }
                        }
                    }
                };
                context.Anime.Add(anime);
                context.SaveChanges();
            }

            var service = new GlobalSearchService(() => AniTDatabase.Create(databasePath));

            Assert.Contains(await service.SearchAsync("magical destroyers"), item => item.Kind == GlobalSearchResultKind.Anime);
            Assert.Contains(await service.SearchAsync("episódio 10"), item => item.Kind == GlobalSearchResultKind.Episode);
            Assert.Contains(await service.SearchAsync("fantasia"), item => item.Kind == GlobalSearchResultKind.Anime);
            Assert.Contains(await service.SearchAsync("fantasia"), item => item.Kind == GlobalSearchResultKind.Genre && item.GenreName == "Fantasia");
            Assert.Contains(await service.SearchAsync("acao"), item => item.Kind == GlobalSearchResultKind.Genre && item.GenreName == "Ação");
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            if (Directory.Exists(directory)) Directory.Delete(directory, true);
        }
    }
}
