namespace AniT.Core;

public static class LibraryFreshness
{
    public static readonly TimeSpan NewItemDuration = TimeSpan.FromDays(2);

    public static DateTimeOffset GetLatestImportAt(Anime anime)
    {
        var latestMediaImport = anime.Seasons
            .SelectMany(season => season.Episodes)
            .SelectMany(episode => episode.MediaFiles)
            .Where(file => file.Availability == MediaFileAvailability.Available)
            .Select(file => (DateTimeOffset?)file.ImportedAt)
            .Max();

        return latestMediaImport ?? anime.CreatedAt;
    }

    public static bool IsNew(Anime anime, DateTimeOffset now)
    {
        var age = now.ToUniversalTime() - GetLatestImportAt(anime).ToUniversalTime();
        return age >= TimeSpan.Zero && age < NewItemDuration;
    }
}
