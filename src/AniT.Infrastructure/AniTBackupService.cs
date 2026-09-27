using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Data.Sqlite;

namespace AniT.Infrastructure;

public sealed record AniTBackupFile(string Path, long Size, string Sha256);

public sealed record AniTBackupSummary(
    int AnimeCount,
    int EpisodeCount,
    int WatchedEpisodeCount,
    int UnlockedAchievementCount);

public sealed record AniTBackupManifest(
    string Format,
    int FormatVersion,
    DateTimeOffset CreatedAtUtc,
    string AppVersion,
    string SourceDataRoot,
    AniTBackupSummary Summary,
    bool IncludesArtwork,
    bool IncludesMediaFiles,
    IReadOnlyList<AniTBackupFile> Files);

public sealed record AniTBackupInspection(AniTBackupManifest Manifest, long ArchiveSize);

public sealed record AniTBackupRestoreResult(AniTBackupManifest Manifest, string RecoveryBackupPath);

/// <summary>
/// Creates and restores AniT's portable, versioned backup archive. Video files are
/// intentionally referenced by the database instead of copied into the archive.
/// </summary>
public sealed class AniTBackupService
{
    public const string FileExtension = ".anitbackup";
    public const int CurrentFormatVersion = 1;
    private const string ManifestEntryName = "manifest.json";
    private const string DatabaseEntryName = "payload/Data/anit.db";
    private const long MaximumExpandedSize = 8L * 1024 * 1024 * 1024;
    private const int MaximumEntryCount = 100_000;
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web) { WriteIndented = true };

    public async Task<AniTBackupManifest> ExportAsync(
        string dataRoot,
        string databasePath,
        string destinationPath,
        string appVersion,
        CancellationToken cancellationToken = default)
    {
        dataRoot = Path.GetFullPath(dataRoot);
        databasePath = Path.GetFullPath(databasePath);
        destinationPath = Path.GetFullPath(destinationPath);
        if (!File.Exists(databasePath)) throw new FileNotFoundException("O banco de dados do AniT não foi encontrado.", databasePath);

        var workDirectory = CreateTemporaryDirectory("export");
        var payloadDirectory = Path.Combine(workDirectory, "payload");
        var snapshotPath = Path.Combine(payloadDirectory, "Data", "anit.db");
        var localArchive = Path.Combine(Path.GetTempPath(), $"AniT-{Guid.NewGuid():N}{FileExtension}");
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(snapshotPath)!);
            await CreateDatabaseSnapshotAsync(databasePath, snapshotPath, cancellationToken);
            await CopyPortableFilesAsync(dataRoot, payloadDirectory, cancellationToken);
            var summary = ReadSummary(snapshotPath);
            TryDeleteFile(snapshotPath + "-wal");
            TryDeleteFile(snapshotPath + "-shm");

            var files = new List<AniTBackupFile>();
            foreach (var file in Directory.EnumerateFiles(payloadDirectory, "*", SearchOption.AllDirectories).OrderBy(path => path, StringComparer.OrdinalIgnoreCase))
            {
                cancellationToken.ThrowIfCancellationRequested();
                var relativePath = NormalizeArchivePath(Path.GetRelativePath(workDirectory, file));
                files.Add(new AniTBackupFile(relativePath, new FileInfo(file).Length, await ComputeSha256Async(file, cancellationToken)));
            }

            var manifest = new AniTBackupManifest(
                "AniT Portable Backup",
                CurrentFormatVersion,
                DateTimeOffset.UtcNow,
                string.IsNullOrWhiteSpace(appVersion) ? "desconhecida" : appVersion,
                dataRoot,
                summary,
                files.Any(item => item.Path.StartsWith("payload/Covers/", StringComparison.OrdinalIgnoreCase)
                                  || item.Path.StartsWith("payload/Cache/ProfileBanners/", StringComparison.OrdinalIgnoreCase)),
                false,
                files);
            await File.WriteAllTextAsync(Path.Combine(workDirectory, ManifestEntryName), JsonSerializer.Serialize(manifest, JsonOptions), cancellationToken);

            if (File.Exists(localArchive)) File.Delete(localArchive);
            ZipFile.CreateFromDirectory(workDirectory, localArchive, CompressionLevel.Optimal, includeBaseDirectory: false);
            Directory.CreateDirectory(Path.GetDirectoryName(destinationPath)!);
            var partialPath = destinationPath + $".partial-{Guid.NewGuid():N}";
            try
            {
                File.Copy(localArchive, partialPath, overwrite: true);
                File.Move(partialPath, destinationPath, overwrite: true);
            }
            finally
            {
                TryDeleteFile(partialPath);
            }
            return manifest;
        }
        finally
        {
            TryDeleteDirectory(workDirectory);
            TryDeleteFile(localArchive);
        }
    }

    public async Task<AniTBackupInspection> InspectAsync(string backupPath, CancellationToken cancellationToken = default)
    {
        var prepared = await PrepareArchiveAsync(backupPath, cancellationToken);
        try { return new AniTBackupInspection(prepared.Manifest, new FileInfo(backupPath).Length); }
        finally { TryDeleteDirectory(prepared.Directory); }
    }

    public async Task<AniTBackupRestoreResult> RestoreAsync(
        string backupPath,
        string currentDataRoot,
        string currentDatabasePath,
        string appVersion,
        Func<Task> closeCurrentDataAsync,
        CancellationToken cancellationToken = default)
    {
        currentDataRoot = Path.GetFullPath(currentDataRoot);
        currentDatabasePath = Path.GetFullPath(currentDatabasePath);
        var prepared = await PrepareArchiveAsync(backupPath, cancellationToken);
        var recoveryDirectory = Path.Combine(currentDataRoot, "Recovery");
        Directory.CreateDirectory(recoveryDirectory);
        var recoveryPath = Path.Combine(recoveryDirectory, $"antes-da-importacao-{DateTime.Now:yyyy-MM-dd-HHmmss}{FileExtension}");
        var rollbackDirectory = CreateTemporaryDirectory("rollback");
        var installStarted = false;
        try
        {
            await ExportAsync(currentDataRoot, currentDatabasePath, recoveryPath, appVersion, cancellationToken);
            RebasePortablePaths(prepared.Directory, prepared.Manifest.SourceDataRoot, currentDataRoot);
            await closeCurrentDataAsync();
            SqliteConnection.ClearAllPools();
            installStarted = true;
            InstallPreparedBackup(prepared.Directory, currentDataRoot, rollbackDirectory);
            return new AniTBackupRestoreResult(prepared.Manifest, recoveryPath);
        }
        catch
        {
            if (installStarted)
            {
                RemoveManagedTargets(currentDataRoot);
                RestoreRollback(currentDataRoot, rollbackDirectory);
            }
            throw;
        }
        finally
        {
            TryDeleteDirectory(prepared.Directory);
            TryDeleteDirectory(rollbackDirectory);
        }
    }

    private static async Task CreateDatabaseSnapshotAsync(string sourcePath, string destinationPath, CancellationToken cancellationToken)
    {
        await using var source = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = sourcePath, Mode = SqliteOpenMode.ReadOnly, Pooling = false }.ToString());
        await using var destination = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = destinationPath, Mode = SqliteOpenMode.ReadWriteCreate, Pooling = false }.ToString());
        await source.OpenAsync(cancellationToken);
        await destination.OpenAsync(cancellationToken);
        source.BackupDatabase(destination);
    }

    private static async Task CopyPortableFilesAsync(string dataRoot, string payloadDirectory, CancellationToken cancellationToken)
    {
        var dataDirectory = Path.Combine(dataRoot, "Data");
        if (Directory.Exists(dataDirectory))
        {
            foreach (var file in Directory.EnumerateFiles(dataDirectory, "*.json", SearchOption.TopDirectoryOnly))
            {
                if (Path.GetFileName(file).Equals("automatic-backup-state.json", StringComparison.OrdinalIgnoreCase)) continue;
                await CopyFileAsync(file, Path.Combine(payloadDirectory, "Data", Path.GetFileName(file)), cancellationToken);
            }
        }

        await CopyDirectoryAsync(Path.Combine(dataRoot, "Data", "Profile"), Path.Combine(payloadDirectory, "Data", "Profile"), cancellationToken);
        await CopyDirectoryAsync(Path.Combine(dataRoot, "Data", "Profiles"), Path.Combine(payloadDirectory, "Data", "Profiles"), cancellationToken);
        await CopyDirectoryAsync(Path.Combine(dataRoot, "Covers"), Path.Combine(payloadDirectory, "Covers"), cancellationToken);
        await CopyDirectoryAsync(Path.Combine(dataRoot, "Cache", "ProfileBanners"), Path.Combine(payloadDirectory, "Cache", "ProfileBanners"), cancellationToken);
    }

    private static async Task CopyDirectoryAsync(string source, string destination, CancellationToken cancellationToken)
    {
        if (!Directory.Exists(source)) return;
        var options = new EnumerationOptions
        {
            RecurseSubdirectories = true,
            AttributesToSkip = FileAttributes.ReparsePoint,
            IgnoreInaccessible = true
        };
        foreach (var file in Directory.EnumerateFiles(source, "*", options))
        {
            cancellationToken.ThrowIfCancellationRequested();
            if ((File.GetAttributes(file) & FileAttributes.ReparsePoint) != 0) continue;
            await CopyFileAsync(file, Path.Combine(destination, Path.GetRelativePath(source, file)), cancellationToken);
        }
    }

    private static async Task CopyFileAsync(string source, string destination, CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
        await using var input = new FileStream(source, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete, 131072, FileOptions.Asynchronous | FileOptions.SequentialScan);
        await using var output = new FileStream(destination, FileMode.Create, FileAccess.Write, FileShare.None, 131072, FileOptions.Asynchronous | FileOptions.SequentialScan);
        await input.CopyToAsync(output, cancellationToken);
    }

    private static async Task<PreparedArchive> PrepareArchiveAsync(string backupPath, CancellationToken cancellationToken)
    {
        backupPath = Path.GetFullPath(backupPath);
        if (!File.Exists(backupPath)) throw new FileNotFoundException("O arquivo de backup não foi encontrado.", backupPath);
        var directory = CreateTemporaryDirectory("import");
        try
        {
            using var archive = ZipFile.OpenRead(backupPath);
            if (archive.Entries.Count is 0 or > MaximumEntryCount) throw new InvalidDataException("O arquivo não contém um backup válido do AniT.");
            var manifestEntries = archive.Entries
                .Where(entry => string.Equals(entry.FullName, ManifestEntryName, StringComparison.OrdinalIgnoreCase))
                .ToArray();
            if (manifestEntries.Length != 1) throw new InvalidDataException("O backup deve conter exatamente um manifesto.");
            var manifestEntry = manifestEntries[0];
            if (manifestEntry.Length > 2 * 1024 * 1024) throw new InvalidDataException("O manifesto do backup excede o tamanho permitido.");
            AniTBackupManifest manifest;
            await using (var stream = manifestEntry.Open())
                manifest = await JsonSerializer.DeserializeAsync<AniTBackupManifest>(stream, JsonOptions, cancellationToken)
                    ?? throw new InvalidDataException("O manifesto do backup está vazio.");
            if (!string.Equals(manifest.Format, "AniT Portable Backup", StringComparison.Ordinal) || manifest.FormatVersion != CurrentFormatVersion)
                throw new InvalidDataException($"Esta versão do AniT não suporta o formato de backup {manifest.FormatVersion}.");
            if (manifest.Files is null || manifest.Files.Count is 0 or > MaximumEntryCount
                || !manifest.Files.Any(item => string.Equals(item?.Path, DatabaseEntryName, StringComparison.OrdinalIgnoreCase)))
                throw new InvalidDataException("O backup não contém o banco de dados obrigatório.");

            var declared = new Dictionary<string, AniTBackupFile>(StringComparer.OrdinalIgnoreCase);
            foreach (var item in manifest.Files)
            {
                if (item is null || string.IsNullOrWhiteSpace(item.Path) || string.IsNullOrWhiteSpace(item.Sha256))
                    throw new InvalidDataException("O manifesto contém uma entrada de arquivo inválida.");
                var normalizedPath = NormalizeArchivePath(item.Path);
                if (item.Size < 0 || item.Sha256.Length != 64 || !item.Sha256.All(Uri.IsHexDigit))
                    throw new InvalidDataException($"O manifesto contém dados inválidos para {normalizedPath}.");
                if (!declared.TryAdd(normalizedPath, item))
                    throw new InvalidDataException($"O manifesto contém um caminho duplicado: {normalizedPath}.");
            }
            long totalSize = 0;
            foreach (var entry in archive.Entries.Where(item => !string.Equals(item.FullName, ManifestEntryName, StringComparison.OrdinalIgnoreCase)))
            {
                cancellationToken.ThrowIfCancellationRequested();
                var entryPath = NormalizeArchivePath(entry.FullName);
                if (entryPath.EndsWith('/')) continue;
                if (!declared.Remove(entryPath, out var expected)) throw new InvalidDataException($"O backup contém um arquivo não declarado: {entryPath}.");
                totalSize = checked(totalSize + entry.Length);
                if (totalSize > MaximumExpandedSize || entry.Length != expected.Size) throw new InvalidDataException("O conteúdo expandido do backup é inválido ou grande demais.");
                var destination = SafeDestinationPath(directory, entryPath);
                Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
                await using var input = entry.Open();
                await using var output = new FileStream(destination, FileMode.CreateNew, FileAccess.Write, FileShare.None, 131072, FileOptions.Asynchronous | FileOptions.SequentialScan);
                using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
                var buffer = new byte[131072];
                int read;
                while ((read = await input.ReadAsync(buffer, cancellationToken)) > 0)
                {
                    await output.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
                    hash.AppendData(buffer, 0, read);
                }
                var actualHash = Convert.ToHexString(hash.GetHashAndReset()).ToLowerInvariant();
                if (!string.Equals(actualHash, expected.Sha256, StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException($"O arquivo {entryPath} está corrompido.");
            }
            if (declared.Count != 0) throw new InvalidDataException("O backup está incompleto.");
            ValidateDatabase(Path.Combine(directory, DatabaseEntryName.Replace('/', Path.DirectorySeparatorChar)));
            return new PreparedArchive(directory, manifest);
        }
        catch
        {
            TryDeleteDirectory(directory);
            throw;
        }
    }

    private static void ValidateDatabase(string databasePath)
    {
        using var connection = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = databasePath, Mode = SqliteOpenMode.ReadOnly, Pooling = false }.ToString());
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = "PRAGMA integrity_check;";
        if (!string.Equals(Convert.ToString(command.ExecuteScalar()), "ok", StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("O banco de dados contido no backup está corrompido.");
        if (!TableExists(connection, "Anime") || !TableExists(connection, "Episodes"))
            throw new InvalidDataException("O backup não contém um banco de dados reconhecido pelo AniT.");
    }

    private static AniTBackupSummary ReadSummary(string databasePath)
    {
        using var connection = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = databasePath, Mode = SqliteOpenMode.ReadOnly, Pooling = false }.ToString());
        connection.Open();
        return new AniTBackupSummary(
            ReadCount(connection, "Anime"),
            ReadCount(connection, "Episodes"),
            ReadConditionalCount(connection, "Episodes", "Status <> 0"),
            ReadConditionalCount(connection, "UserAchievements", "IsUnlocked = 1"));
    }

    private static int ReadCount(SqliteConnection connection, string table) => TableExists(connection, table) ? ExecuteCount(connection, $"SELECT COUNT(*) FROM {table};") : 0;
    private static int ReadConditionalCount(SqliteConnection connection, string table, string condition) => TableExists(connection, table) ? ExecuteCount(connection, $"SELECT COUNT(*) FROM {table} WHERE {condition};") : 0;
    private static int ExecuteCount(SqliteConnection connection, string sql) { using var command = connection.CreateCommand(); command.CommandText = sql; return Convert.ToInt32(command.ExecuteScalar() ?? 0); }
    private static bool TableExists(SqliteConnection connection, string table) { using var command = connection.CreateCommand(); command.CommandText = "SELECT COUNT(*) FROM sqlite_master WHERE type='table' AND name=$name;"; command.Parameters.AddWithValue("$name", table); return Convert.ToInt32(command.ExecuteScalar()) > 0; }

    private static void RebasePortablePaths(string preparedDirectory, string sourceRoot, string destinationRoot)
    {
        if (string.IsNullOrWhiteSpace(sourceRoot)) return;
        sourceRoot = Path.GetFullPath(sourceRoot).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        destinationRoot = Path.GetFullPath(destinationRoot).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var databasePath = Path.Combine(preparedDirectory, DatabaseEntryName.Replace('/', Path.DirectorySeparatorChar));
        using (var connection = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = databasePath, Pooling = false }.ToString()))
        {
            connection.Open();
            if (TableExists(connection, "Anime"))
            {
                using var command = connection.CreateCommand();
                command.CommandText = "UPDATE Anime SET CoverPath = $destination || substr(CoverPath, length($source) + 1) WHERE CoverPath IS NOT NULL AND lower(substr(CoverPath, 1, length($source))) = lower($source) AND (length(CoverPath) = length($source) OR substr(CoverPath, length($source) + 1, 1) IN ('\\', '/'));";
                command.Parameters.AddWithValue("$destination", destinationRoot);
                command.Parameters.AddWithValue("$source", sourceRoot);
                command.ExecuteNonQuery();
            }
        }

        foreach (var profilePath in new[]
                 {
                     Path.Combine(preparedDirectory, "payload", "Data", "profile.json"),
                     Path.Combine(preparedDirectory, "payload", "Data", "profiles.json")
                 }.Where(File.Exists))
        {
            try
            {
                var root = JsonNode.Parse(File.ReadAllText(profilePath));
                if (root is null) continue;
                RebaseProfileImagePaths(root, sourceRoot, destinationRoot);
                File.WriteAllText(profilePath, root.ToJsonString(JsonOptions));
            }
            catch (JsonException) { throw new InvalidDataException("As configurações de perfil do backup estão corrompidas."); }
        }

        var systemSettingsPath = Path.Combine(preparedDirectory, "payload", "Data", "system-settings.json");
        if (File.Exists(systemSettingsPath))
        {
            try
            {
                var settings = JsonNode.Parse(File.ReadAllText(systemSettingsPath)) as JsonObject;
                if (settings is not null)
                {
                    RebaseSettingPath(settings, "ArtworkDirectory", sourceRoot, destinationRoot);
                    RebaseSettingPath(settings, "ImageCacheDirectory", sourceRoot, destinationRoot);
                    settings["AutomaticBackupDirectory"] = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "AniT Backups");
                    File.WriteAllText(systemSettingsPath, settings.ToJsonString(JsonOptions));
                }
            }
            catch (JsonException) { throw new InvalidDataException("As configurações do backup estão corrompidas."); }
        }
    }

    private static void RebaseSettingPath(JsonObject settings, string propertyName, string sourceRoot, string destinationRoot)
    {
        if (settings[propertyName] is JsonValue value
            && value.TryGetValue<string>(out var path)
            && !string.IsNullOrWhiteSpace(path)
            && IsPathWithinRoot(path, sourceRoot))
            settings[propertyName] = destinationRoot + path[sourceRoot.Length..];
    }

    private static void RebaseProfileImagePaths(JsonNode node, string sourceRoot, string destinationRoot)
    {
        if (node is JsonObject jsonObject)
        {
            foreach (var property in jsonObject.ToArray())
            {
                if (property.Key is "avatarPath" or "bannerPath" or "AvatarPath" or "BannerPath"
                    && property.Value is JsonValue value
                    && value.TryGetValue<string>(out var path)
                    && !string.IsNullOrWhiteSpace(path)
                    && IsPathWithinRoot(path, sourceRoot))
                    jsonObject[property.Key] = destinationRoot + path[sourceRoot.Length..];
                else if (property.Value is not null)
                    RebaseProfileImagePaths(property.Value, sourceRoot, destinationRoot);
            }
        }
        else if (node is JsonArray array)
        {
            foreach (var item in array.Where(item => item is not null)) RebaseProfileImagePaths(item!, sourceRoot, destinationRoot);
        }
    }

    private static void InstallPreparedBackup(string preparedDirectory, string targetRoot, string rollbackDirectory)
    {
        var managedTargets = new[]
        {
            "Data/anit.db", "Data/anit.db-wal", "Data/anit.db-shm", "Data/system-settings.json", "Data/notification-state.json", "Data/achievement-settings.json", "Data/profile.json", "Data/profiles.json",
            "Data/Profile", "Data/Profiles", "Covers", "Cache/ProfileBanners"
        };
        foreach (var relative in managedTargets)
        {
            var normalized = relative.Replace('/', Path.DirectorySeparatorChar);
            var target = SafeDestinationPath(targetRoot, normalized);
            var rollback = SafeDestinationPath(rollbackDirectory, normalized);
            var incoming = SafeDestinationPath(Path.Combine(preparedDirectory, "payload"), normalized);
            MoveExisting(target, rollback);
            if (File.Exists(incoming))
            {
                Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                File.Move(incoming, target, overwrite: true);
            }
            else if (Directory.Exists(incoming))
            {
                Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                Directory.Move(incoming, target);
            }
        }
    }

    private static void RestoreRollback(string targetRoot, string rollbackDirectory)
    {
        if (!Directory.Exists(rollbackDirectory)) return;
        foreach (var source in Directory.EnumerateFileSystemEntries(rollbackDirectory, "*", SearchOption.AllDirectories).OrderByDescending(path => path.Length))
        {
            var relative = Path.GetRelativePath(rollbackDirectory, source);
            var target = SafeDestinationPath(targetRoot, relative);
            if (File.Exists(source))
            {
                Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                File.Move(source, target, overwrite: true);
            }
        }
    }

    private static void RemoveManagedTargets(string targetRoot)
    {
        foreach (var relative in new[]
                 {
                     "Data/anit.db", "Data/anit.db-wal", "Data/anit.db-shm", "Data/system-settings.json", "Data/notification-state.json", "Data/achievement-settings.json", "Data/profile.json", "Data/profiles.json",
                     "Data/Profile", "Data/Profiles", "Covers", "Cache/ProfileBanners"
                 })
        {
            var target = SafeDestinationPath(targetRoot, relative.Replace('/', Path.DirectorySeparatorChar));
            if (File.Exists(target)) File.Delete(target);
            else if (Directory.Exists(target)) Directory.Delete(target, recursive: true);
        }
    }

    private static void MoveExisting(string source, string destination)
    {
        if (File.Exists(source))
        {
            Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
            File.Move(source, destination, overwrite: true);
        }
        else if (Directory.Exists(source))
        {
            Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
            Directory.Move(source, destination);
        }
    }

    private static async Task<string> ComputeSha256Async(string path, CancellationToken cancellationToken)
    {
        await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 131072, FileOptions.Asynchronous | FileOptions.SequentialScan);
        return Convert.ToHexString(await SHA256.HashDataAsync(stream, cancellationToken)).ToLowerInvariant();
    }

    private static string NormalizeArchivePath(string path)
    {
        path = path.Replace('\\', '/');
        if (string.IsNullOrWhiteSpace(path) || path.StartsWith('/') || path.Contains("../", StringComparison.Ordinal) || path.Contains(':'))
            throw new InvalidDataException("O backup contém um caminho inseguro.");
        return path;
    }

    private static string SafeDestinationPath(string root, string relativePath)
    {
        root = Path.GetFullPath(root);
        var destination = Path.GetFullPath(Path.Combine(root, relativePath));
        var prefix = root.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        if (!destination.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("O backup tentou acessar um caminho fora da área permitida.");
        return destination;
    }

    private static bool IsPathWithinRoot(string path, string root) =>
        path.Equals(root, StringComparison.OrdinalIgnoreCase)
        || (path.StartsWith(root, StringComparison.OrdinalIgnoreCase)
            && path.Length > root.Length
            && path[root.Length] is '\\' or '/');

    private static string CreateTemporaryDirectory(string purpose)
    {
        var path = Path.Combine(Path.GetTempPath(), "AniT", $"{purpose}-{Guid.NewGuid():N}");
        Directory.CreateDirectory(path);
        return path;
    }

    private static void TryDeleteDirectory(string path) { try { if (Directory.Exists(path)) Directory.Delete(path, recursive: true); } catch { } }
    private static void TryDeleteFile(string path) { try { if (File.Exists(path)) File.Delete(path); } catch { } }
    private sealed record PreparedArchive(string Directory, AniTBackupManifest Manifest);
}
