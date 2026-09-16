using AniT.Core;
using Microsoft.EntityFrameworkCore;

namespace AniT.Infrastructure;

public sealed class AniTDbContext(DbContextOptions<AniTDbContext> options) : DbContext(options)
{
    public DbSet<Anime> Anime => Set<Anime>();
    public DbSet<Season> Seasons => Set<Season>();
    public DbSet<Episode> Episodes => Set<Episode>();
    public DbSet<MediaFile> MediaFiles => Set<MediaFile>();
    public DbSet<AnimeAlias> AnimeAliases => Set<AnimeAlias>();
    public DbSet<LibraryReviewItem> LibraryReviewItems => Set<LibraryReviewItem>();
    public DbSet<PlaybackProgress> PlaybackProgresses => Set<PlaybackProgress>();
    public DbSet<LibraryRoot> LibraryRoots => Set<LibraryRoot>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Anime>(entity =>
        {
            entity.Property(item => item.Title).HasMaxLength(300).IsRequired();
            entity.Property(item => item.EnglishTitle).HasMaxLength(300);
            entity.HasIndex(item => item.Title);
        });

        modelBuilder.Entity<AnimeAlias>(entity =>
        {
            entity.Property(item => item.Alias).HasMaxLength(300).IsRequired();
            entity.Property(item => item.NormalizedAlias).HasMaxLength(300).IsRequired();
            entity.HasIndex(item => item.NormalizedAlias);
            entity.HasIndex(item => new { item.AnimeId, item.NormalizedAlias }).IsUnique();
        });

        modelBuilder.Entity<Season>()
            .HasIndex(item => new { item.AnimeId, item.Number })
            .IsUnique();

        modelBuilder.Entity<Episode>()
            .HasIndex(item => new { item.SeasonId, item.Number })
            .IsUnique();

        modelBuilder.Entity<MediaFile>()
            .HasOne(item => item.Episode)
            .WithMany(item => item.MediaFiles)
            .HasForeignKey(item => item.EpisodeId)
            .OnDelete(DeleteBehavior.Cascade);

        modelBuilder.Entity<MediaFile>().HasIndex(item => item.EpisodeId);
        modelBuilder.Entity<MediaFile>().HasIndex(item => item.QuickHash);
        modelBuilder.Entity<MediaFile>().HasIndex(item => new { item.LibraryRootId, item.RelativePath }).IsUnique();

        modelBuilder.Entity<Episode>()
            .HasOne(item => item.PlaybackProgress)
            .WithOne(item => item.Episode)
            .HasForeignKey<PlaybackProgress>(item => item.EpisodeId)
            .OnDelete(DeleteBehavior.Cascade);

        modelBuilder.Entity<LibraryRoot>()
            .HasIndex(item => item.Path)
            .IsUnique();

        modelBuilder.Entity<LibraryReviewItem>(entity =>
        {
            entity.HasIndex(item => new { item.LibraryRootId, item.RelativePath }).IsUnique();
            entity.HasIndex(item => item.QuickHash);
            entity.HasOne(item => item.LibraryRoot)
                .WithMany(item => item.ReviewItems)
                .HasForeignKey(item => item.LibraryRootId)
                .OnDelete(DeleteBehavior.Cascade);
        });
    }
}
