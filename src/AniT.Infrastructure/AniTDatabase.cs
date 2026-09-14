using Microsoft.EntityFrameworkCore;

namespace AniT.Infrastructure;

public static class AniTDatabase
{
    public static AniTDbContext Create(string databasePath)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(databasePath)!);
        var options = new DbContextOptionsBuilder<AniTDbContext>()
            .UseSqlite($"Data Source={databasePath}")
            .Options;
        var context = new AniTDbContext(options);
        context.Database.EnsureCreated();
        return context;
    }
}
