using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using AniT.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace AniT.App;

internal sealed record OnlineImportResult(string Title, int Episodes, bool ArtworkPending);
public partial class App
{
    internal static async Task<OnlineImportResult> ImportOnlineAnimeAsync(string source, string destination, IProgress<OnlineImportProgress> progress, CancellationToken token)
    {
        Guid[] existing;
        await using (var db = OpenFreshDatabase()) existing = await db.Anime.Select(anime => anime.Id).ToArrayAsync(token);
        var service = Online;
        var job = await Task.Run(() => service.AddAsync(source, destination, "", true, token, progress));
        var artworkPending = false;
        await using var context = OpenFreshDatabase();
        var episodes = await context.MediaFiles.Where(file => file.OnlineTorrentId == job).Select(file => file.EpisodeId).Distinct().CountAsync();
        var animeIds = await context.MediaFiles.Where(file => file.OnlineTorrentId == job).Select(file => file.Episode!.Season!.AnimeId).Distinct().ToListAsync();
        var title = "Seu anime";
        foreach (var id in animeIds)
        {
            var anime = await context.Anime.FirstAsync(item => item.Id == id);
            title = anime.Title;
            progress.Report(new(72, "Identificando o anime", title + " · consultando nome, capa e informações."));
            try
            {
                using var metadata = CancellationTokenSource.CreateLinkedTokenSource(token); metadata.CancelAfter(TimeSpan.FromSeconds(35));
                var result = await Task.Run(() => EnsureAnimeMetadataAsync(id, anime.Title, anime.EnglishTitle, anime.CoverPath, metadata.Token,
                    savedSynopsis: anime.Synopsis, savedCriticScore: anime.CriticScore, savedGenres: anime.Genres,
                    savedReleaseYear: anime.ReleaseYear, savedStudio: anime.Studio, refreshCatalogDetails: true));
                if (!existing.Contains(id) && !string.IsNullOrWhiteSpace(result.EnglishTitle))
                {
                    var previousTitle = anime.Title;
                    var normalized = AniT.Core.AnimeTitleNormalizer.Normalize(previousTitle);
                    if (!await context.Set<AniT.Core.AnimeAlias>().AnyAsync(alias => alias.AnimeId == id && alias.NormalizedAlias == normalized))
                        context.Add(new AniT.Core.AnimeAlias { AnimeId = id, Alias = previousTitle, NormalizedAlias = normalized, Source = AniT.Core.AnimeAliasSource.Imported });
                    anime.Title = result.EnglishTitle; await context.SaveChangesAsync(); title = anime.Title;
                }
                artworkPending |= string.IsNullOrWhiteSpace(result.CoverPath);
            }
            catch (OperationCanceledException) { artworkPending = true; }
            catch (Exception ex) { artworkPending = true; WritePlaybackLog("[ONLINE] Metadados: " + ex); AniTDiagnostics.Write("ONLINE", "A lista foi importada; os metadados poderão ser completados depois.", ex); }
        }
        progress.Report(new(96, "Finalizando a biblioteca", $"{episodes} episódios organizados."));
        if (Current?.Dispatcher is { HasShutdownStarted: false } dispatcher)
            await dispatcher.InvokeAsync(() => LibraryArtworkUpdated?.Invoke(null, EventArgs.Empty));
        return new(title, episodes, artworkPending);
    }
}
