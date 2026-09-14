using AniT.Core;
using Microsoft.EntityFrameworkCore;

namespace AniT.Infrastructure;

public sealed class AniTDbContext(DbContextOptions<AniTDbContext> options) : DbContext(options)
{
    public DbSet<Anime> Anime => Set<Anime>();
    public DbSet<Season> Seasons => Set<Season>();
    public DbSet<Episode> Episodes => Set<Episode>();
    public DbSet<MediaFile> MediaFiles => Set<MediaFile>();
    public DbSet<PlaybackProgress> PlaybackProgresses => Set<PlaybackProgress>();
    public DbSet<LibraryRoot> LibraryRoots => Set<LibraryRoot>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Anime>(entity =>
        {
            entity.Property(item => item.Title).HasMaxLength(300).IsRequired();
            entity.HasIndex(item => item.Title);
        });

        modelBuilder.Entity<Season>()
            .HasIndex(item => new { item.AnimeId, item.Number })
            .IsUnique();

        modelBuilder.Entity<Episode>()
            .HasIndex(item => new { item.SeasonId, item.Number })
            .IsUnique();

        modelBuilder.Entity<Episode>()
            .HasOne(item => item.MediaFile)
            .WithOne(item => item.Episode)
            .HasForeignKey<MediaFile>(item => item.EpisodeId)
            .OnDelete(DeleteBehavior.Cascade);

        modelBuilder.Entity<Episode>()
            .HasOne(item => item.PlaybackProgress)
            .WithOne(item => item.Episode)
            .HasForeignKey<PlaybackProgress>(item => item.EpisodeId)
            .OnDelete(DeleteBehavior.Cascade);

        modelBuilder.Entity<LibraryRoot>()
            .HasIndex(item => item.Path)
            .IsUnique();
    }
}
