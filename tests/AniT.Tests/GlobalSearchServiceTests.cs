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
        var episodeOneId = Guid.NewGuid();
        var libraryRootId = Guid.NewGuid();

        try
        {
            using (var context = AniTDatabase.Create(databasePath))
            {
                context.LibraryRoots.Add(new LibraryRoot { Id = libraryRootId, Path = directory });
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
                                new Episode
                                {
                                    Id = episodeOneId,
                                    Number = 1,
                                    MediaFiles =
                                    {
                                        new MediaFile
                                        {
                                            LibraryRootId = libraryRootId,
                                            RelativePath = "Magical Destroyers 01.mkv",
                                            FileName = "Magical Destroyers 01.mkv",
                                            Extension = ".mkv"
                                        }
                                    }
                                },
                                new Episode
                                {
                                    Number = 10,
                                    Title = "Novo mundo",
                                    MediaFiles =
                                    {
                                        new MediaFile
                                        {
                                            LibraryRootId = libraryRootId,
                                            RelativePath = "Magical Destroyers 10.mkv",
                                            FileName = "Magical Destroyers 10.mkv",
                                            Extension = ".mkv"
                                        }
                                    }
                                }
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
            Assert.Contains(await service.SearchAsync("01"), item => item.Kind == GlobalSearchResultKind.Episode && item.EpisodeId == episodeOneId);
            Assert.Contains(await service.SearchAsync("episódio 01"), item => item.Kind == GlobalSearchResultKind.Episode && item.EpisodeId == episodeOneId);
            Assert.Contains(await service.SearchAsync("S01E01"), item => item.Kind == GlobalSearchResultKind.Episode && item.EpisodeId == episodeOneId);
            Assert.Contains(await service.SearchAsync("Destroyers 01"), item => item.Kind == GlobalSearchResultKind.Episode && item.EpisodeId == episodeOneId);
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

    [Fact]
    public async Task Search_DoesNotReturnAnimeWhoseOnlyFileIsMissing()
    {
        var directory = Path.Combine(Path.GetTempPath(), "AniT.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var databasePath = Path.Combine(directory, "search.db");

        try
        {
            using (var context = AniTDatabase.Create(databasePath))
            {
                var root = new LibraryRoot { Path = directory };
                context.LibraryRoots.Add(root);
                context.Anime.Add(new Anime
                {
                    Title = "Anime removido",
                    Seasons =
                    {
                        new Season
                        {
                            Number = 1,
                            Episodes =
                            {
                                new Episode
                                {
                                    Number = 1,
                                    MediaFiles =
                                    {
                                        new MediaFile
                                        {
                                            LibraryRootId = root.Id,
                                            RelativePath = "Anime removido - 01.mkv",
                                            FileName = "Anime removido - 01.mkv",
                                            Availability = MediaFileAvailability.Missing
                                        }
                                    }
                                }
                            }
                        }
                    }
                });
                context.SaveChanges();
            }

            var service = new GlobalSearchService(() => AniTDatabase.Create(databasePath));

            Assert.Empty(await service.SearchAsync("anime removido"));
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            if (Directory.Exists(directory)) Directory.Delete(directory, true);
        }
    }
}
