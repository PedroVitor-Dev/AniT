using System.Text.Json;

namespace AniT.Infrastructure;

public sealed record AutomaticBackupResult(string BackupPath, AniTBackupManifest Manifest, int RemovedOlderBackups);

public sealed class AutomaticBackupService
{
    private static readonly SemaphoreSlim Gate = new(1, 1);
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };
    private readonly AniTBackupService backupService;

    public AutomaticBackupService(AniTBackupService backupService) => this.backupService = backupService;

    public async Task<AutomaticBackupResult?> RunAsync(
        string dataRoot,
        string databasePath,
        string appVersion,
        AniTSystemSettings settings,
        bool force = false,
        CancellationToken cancellationToken = default)
    {
        if (!settings.AutomaticBackupEnabled && !force) return null;
        if (!await Gate.WaitAsync(0, cancellationToken)) return null;
        try
        {
            var statePath = Path.Combine(dataRoot, "Data", "automatic-backup-state.json");
            var state = LoadState(statePath);
            var now = DateTimeOffset.UtcNow;
            if (!force && !IsDue(settings.AutomaticBackupFrequency, state?.LastSuccessfulBackupUtc, now)) return null;

            var directory = Path.GetFullPath(settings.AutomaticBackupDirectory
                ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "AniT Backups"));
            Directory.CreateDirectory(directory);
            var destination = Path.Combine(directory, $"AniT-auto-{now:yyyyMMdd-HHmmss}{AniTBackupService.FileExtension}");
            var manifest = await backupService.ExportAsync(dataRoot, databasePath, destination, appVersion, cancellationToken);
            SaveState(statePath, new AutomaticBackupState(now, destination));
            var removed = Prune(directory, settings.AutomaticBackupRetention);
            return new AutomaticBackupResult(destination, manifest, removed);
        }
        finally
        {
            Gate.Release();
        }
    }

    public static bool IsDue(AutomaticBackupFrequency frequency, DateTimeOffset? lastSuccessUtc, DateTimeOffset nowUtc)
    {
        if (lastSuccessUtc is null) return true;
        var elapsed = nowUtc - lastSuccessUtc.Value;
        return frequency switch
        {
            AutomaticBackupFrequency.EveryStartup => true,
            AutomaticBackupFrequency.Daily => elapsed >= TimeSpan.FromDays(1),
            AutomaticBackupFrequency.Weekly => elapsed >= TimeSpan.FromDays(7),
            AutomaticBackupFrequency.Monthly => elapsed >= TimeSpan.FromDays(30),
            _ => true
        };
    }

    private static int Prune(string directory, int retention)
    {
        var removed = 0;
        foreach (var file in Directory.EnumerateFiles(directory, $"AniT-auto-*{AniTBackupService.FileExtension}")
                     .OrderByDescending(File.GetLastWriteTimeUtc)
                     .Skip(Math.Clamp(retention, 1, 30)))
        {
            try { File.Delete(file); removed++; } catch { }
        }
        return removed;
    }

    private static AutomaticBackupState? LoadState(string path)
    {
        try { return File.Exists(path) ? JsonSerializer.Deserialize<AutomaticBackupState>(File.ReadAllText(path), JsonOptions) : null; }
        catch { return null; }
    }

    private static void SaveState(string path, AutomaticBackupState state)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var temporary = path + ".tmp";
        File.WriteAllText(temporary, JsonSerializer.Serialize(state, JsonOptions));
        File.Move(temporary, path, true);
    }

    private sealed record AutomaticBackupState(DateTimeOffset LastSuccessfulBackupUtc, string BackupPath);
}
