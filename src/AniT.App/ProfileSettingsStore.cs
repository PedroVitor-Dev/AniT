using System.Text.Json;

namespace AniT.App;

public sealed record ProfileSettings(
    string DisplayName,
    string Bio,
    DateTimeOffset MemberSince,
    string? AvatarPath = null,
    double AvatarSize = 142,
    string? BannerPath = null,
    Guid ProfileId = default,
    string DisplayTitle = "Explorador de Mundos",
    bool IsPublic = false,
    bool HideHistory = false,
    bool HideRatings = false,
    bool HideFavorites = false,
    int AvatarZoomPercent = 100,
    int AvatarFocusXPercent = 50,
    int AvatarFocusYPercent = 50,
    string? PinHash = null,
    string? PinSalt = null,
    int PinIterations = 0)
{
    public override string ToString() => DisplayName;
}

public sealed record LocalProfileCollection(Guid ActiveProfileId, IReadOnlyList<ProfileSettings> Profiles);

internal static class ProfileSettingsStore
{
    private static readonly object Sync = new();
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };
    private static readonly string DataDirectory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "AniT", "Data");
    private static readonly string LegacyFilePath = Path.Combine(DataDirectory, "profile.json");
    private static readonly string ProfilesFilePath = Path.Combine(DataDirectory, "profiles.json");
    private static readonly string ProfilesBackupPath = ProfilesFilePath + ".bak";
    private static readonly string ProfilesDirectory = Path.Combine(DataDirectory, "Profiles");
    private static readonly string AvatarDirectory = Path.Combine(DataDirectory, "Profile");

    public static Guid ActiveProfileId => LoadCollection().ActiveProfileId;

    public static ProfileSettings Load()
    {
        var collection = LoadCollection();
        return collection.Profiles.First(profile => profile.ProfileId == collection.ActiveProfileId);
    }

    public static IReadOnlyList<ProfileSettings> GetProfiles() => LoadCollection().Profiles;

    public static bool HasPin(ProfileSettings profile) =>
        !string.IsNullOrWhiteSpace(profile.PinHash)
        && !string.IsNullOrWhiteSpace(profile.PinSalt)
        && profile.PinIterations > 0;

    public static bool VerifyPin(Guid profileId, string? pin)
    {
        var profile = GetProfiles().FirstOrDefault(item => item.ProfileId == profileId);
        if (profile is null) return false;
        if (!HasPin(profile)) return true;
        return global::AniT.Core.ProfilePinSecurity.Verify(pin, profile.PinHash, profile.PinSalt, profile.PinIterations);
    }

    public static bool SetPin(Guid profileId, string pin)
    {
        var credential = global::AniT.Core.ProfilePinSecurity.Create(pin);
        return UpdatePin(profileId, credential.Hash, credential.Salt, credential.Iterations);
    }

    public static bool ClearPin(Guid profileId) => UpdatePin(profileId, null, null, 0);

    public static bool IsValidPin(string? pin) => global::AniT.Core.ProfilePinSecurity.IsValidFormat(pin);

    public static ProfileSettings Create(string displayName)
    {
        lock (Sync)
        {
            var collection = LoadCollectionCore();
            var normalizedName = string.IsNullOrWhiteSpace(displayName) ? $"Perfil {collection.Profiles.Count + 1}" : displayName.Trim();
            var profile = Normalize(new ProfileSettings(
                normalizedName,
                "Uma nova jornada começa aqui. ♡",
                DateTimeOffset.Now,
                ProfileId: Guid.NewGuid()));
            SaveCollectionCore(collection with { Profiles = [.. collection.Profiles, profile] });
            EnsureBlankDatabase(profile.ProfileId);
            return profile;
        }
    }

    public static void Save(ProfileSettings settings)
    {
        lock (Sync)
        {
            var collection = LoadCollectionCore();
            var profileId = settings.ProfileId == Guid.Empty ? collection.ActiveProfileId : settings.ProfileId;
            var persisted = collection.Profiles.FirstOrDefault(profile => profile.ProfileId == profileId);
            var persistedPin = persisted is not null && HasPin(persisted)
                ? new global::AniT.Core.ProfilePinCredential(persisted.PinHash!, persisted.PinSalt!, persisted.PinIterations)
                : null;
            var draftPin = HasPin(settings)
                ? new global::AniT.Core.ProfilePinCredential(settings.PinHash!, settings.PinSalt!, settings.PinIterations)
                : null;
            var protectedPin = global::AniT.Core.ProfilePinSecurity.ResolveAfterProfileEdit(persistedPin, draftPin);
            var normalized = Normalize(settings with
            {
                ProfileId = profileId,
                // PIN credentials are security state, not editable profile data.
                // Preserve the latest persisted value so a stale settings draft
                // cannot restore a removed PIN or erase a newly created one.
                PinHash = protectedPin?.Hash,
                PinSalt = protectedPin?.Salt,
                PinIterations = protectedPin?.Iterations ?? 0
            });
            var profiles = collection.Profiles.Select(profile => profile.ProfileId == profileId ? normalized : profile).ToArray();
            if (!profiles.Any(profile => profile.ProfileId == profileId)) profiles = [.. profiles, normalized];
            SaveCollectionCore(collection with { Profiles = profiles });
            if (profileId == collection.ActiveProfileId) SaveLegacyMirror(normalized);
        }
    }

    public static bool Delete(Guid profileId)
    {
        lock (Sync)
        {
            var collection = LoadCollectionCore();
            if (profileId == collection.ActiveProfileId || collection.Profiles.Count <= 1) return false;
            var profiles = collection.Profiles.Where(profile => profile.ProfileId != profileId).ToArray();
            if (profiles.Length == collection.Profiles.Count) return false;
            SaveCollectionCore(collection with { Profiles = profiles });
            DeleteDatabaseFiles(GetDatabaseSnapshotPath(profileId));
            return true;
        }
    }

    public static bool SetActive(Guid profileId)
    {
        lock (Sync)
        {
            var collection = LoadCollectionCore();
            var profile = collection.Profiles.FirstOrDefault(item => item.ProfileId == profileId);
            if (profile is null) return false;
            SaveCollectionCore(collection with { ActiveProfileId = profileId });
            SaveLegacyMirror(profile);
            return true;
        }
    }

    public static string GetDatabaseSnapshotPath(Guid profileId) => Path.Combine(ProfilesDirectory, profileId.ToString("N"), "anit.db");

    public static string? PersistAvatar(string? sourcePath)
    {
        if (string.IsNullOrWhiteSpace(sourcePath)) return null;
        if (!File.Exists(sourcePath)) throw new FileNotFoundException("A imagem escolhida não está mais disponível.", sourcePath);

        Directory.CreateDirectory(AvatarDirectory);
        var sourceFullPath = Path.GetFullPath(sourcePath);
        var avatarDirectoryFullPath = Path.GetFullPath(AvatarDirectory).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        if (sourceFullPath.StartsWith(avatarDirectoryFullPath, StringComparison.OrdinalIgnoreCase)) return sourceFullPath;

        var extension = Path.GetExtension(sourceFullPath).ToLowerInvariant();
        if (extension is not (".png" or ".jpg" or ".jpeg" or ".bmp"))
            throw new InvalidDataException("Escolha uma imagem PNG, JPG, JPEG ou BMP.");
        var destination = Path.Combine(AvatarDirectory, $"avatar-{Guid.NewGuid():N}{extension}");
        File.Copy(sourceFullPath, destination, overwrite: false);
        return destination;
    }

    private static LocalProfileCollection LoadCollection()
    {
        lock (Sync) return LoadCollectionCore();
    }

    private static LocalProfileCollection LoadCollectionCore()
    {
        var primary = TryLoadCollection(ProfilesFilePath);
        var saved = primary ?? TryLoadCollection(ProfilesBackupPath);
        if (saved is not null && saved.Profiles.Count > 0)
        {
            var profiles = saved.Profiles.Select(Normalize).GroupBy(profile => profile.ProfileId).Select(group => group.First()).ToArray();
            var activeId = profiles.Any(profile => profile.ProfileId == saved.ActiveProfileId) ? saved.ActiveProfileId : profiles[0].ProfileId;
            var recovered = new LocalProfileCollection(activeId, profiles);
            if (primary is null)
            {
                RestorePrimaryCollection(recovered);
            }
            return recovered;
        }

        var legacy = LoadLegacy() with { ProfileId = Guid.NewGuid() };
        var migrated = new LocalProfileCollection(legacy.ProfileId, [Normalize(legacy)]);
        SaveCollectionCore(migrated);
        SaveLegacyMirror(migrated.Profiles[0]);
        return migrated;
    }

    private static ProfileSettings LoadLegacy()
    {
        return TryLoadLegacyProfile(LegacyFilePath)
            ?? TryLoadLegacyProfile(LegacyFilePath + ".bak")
            ?? Default();
    }

    private static ProfileSettings? TryLoadLegacyProfile(string path)
    {
        try
        {
            return File.Exists(path)
                ? JsonSerializer.Deserialize<ProfileSettings>(File.ReadAllText(path), JsonOptions)
                : null;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or JsonException)
        {
            global::AniT.Infrastructure.AniTDiagnostics.Write("PERFIL", $"Não foi possível ler {Path.GetFileName(path)}.", exception);
            return null;
        }
    }

    private static ProfileSettings Normalize(ProfileSettings profile) => profile with
    {
        ProfileId = profile.ProfileId == Guid.Empty ? Guid.NewGuid() : profile.ProfileId,
        DisplayName = string.IsNullOrWhiteSpace(profile.DisplayName) ? "Pet-S" : profile.DisplayName.Trim()[..Math.Min(profile.DisplayName.Trim().Length, 32)],
        Bio = (profile.Bio ?? string.Empty).Trim()[..Math.Min((profile.Bio ?? string.Empty).Trim().Length, 180)],
        DisplayTitle = string.IsNullOrWhiteSpace(profile.DisplayTitle) ? "Explorador de Mundos" : profile.DisplayTitle.Trim()[..Math.Min(profile.DisplayTitle.Trim().Length, 48)],
        AvatarSize = profile.AvatarSize is >= 104 and <= 190 ? profile.AvatarSize : 142,
        AvatarZoomPercent = Math.Clamp(profile.AvatarZoomPercent, 100, 200),
        AvatarFocusXPercent = Math.Clamp(profile.AvatarFocusXPercent, 0, 100),
        AvatarFocusYPercent = Math.Clamp(profile.AvatarFocusYPercent, 0, 100)
    };

    private static void SaveCollectionCore(LocalProfileCollection collection)
    {
        Directory.CreateDirectory(DataDirectory);
        WriteJsonAtomically(ProfilesFilePath, ProfilesBackupPath, JsonSerializer.Serialize(collection, JsonOptions));
    }

    private static bool UpdatePin(Guid profileId, string? hash, string? salt, int iterations)
    {
        lock (Sync)
        {
            var collection = LoadCollectionCore();
            if (!collection.Profiles.Any(profile => profile.ProfileId == profileId)) return false;
            var profiles = collection.Profiles
                .Select(profile => profile.ProfileId == profileId
                    ? profile with { PinHash = hash, PinSalt = salt, PinIterations = iterations }
                    : profile)
                .ToArray();
            SaveCollectionCore(collection with { Profiles = profiles });
            if (profileId == collection.ActiveProfileId)
                SaveLegacyMirror(profiles.First(profile => profile.ProfileId == profileId));
            return true;
        }
    }

    private static void SaveLegacyMirror(ProfileSettings settings)
    {
        Directory.CreateDirectory(DataDirectory);
        WriteJsonAtomically(LegacyFilePath, LegacyFilePath + ".bak", JsonSerializer.Serialize(settings, JsonOptions));
    }

    private static LocalProfileCollection? TryLoadCollection(string path)
    {
        try
        {
            return File.Exists(path)
                ? JsonSerializer.Deserialize<LocalProfileCollection>(File.ReadAllText(path), JsonOptions)
                : null;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or JsonException)
        {
            global::AniT.Infrastructure.AniTDiagnostics.Write("PERFIL", $"Não foi possível ler {Path.GetFileName(path)}.", exception);
            return null;
        }
    }

    private static void WriteJsonAtomically(string path, string backupPath, string json)
    {
        var temporaryPath = path + ".tmp";
        File.WriteAllText(temporaryPath, json);
        try
        {
            if (File.Exists(path))
                File.Replace(temporaryPath, path, backupPath, ignoreMetadataErrors: true);
            else
                File.Move(temporaryPath, path);
        }
        finally
        {
            if (File.Exists(temporaryPath)) File.Delete(temporaryPath);
        }
    }

    private static void RestorePrimaryCollection(LocalProfileCollection collection)
    {
        Directory.CreateDirectory(DataDirectory);
        var temporaryPath = ProfilesFilePath + ".recovered";
        File.WriteAllText(temporaryPath, JsonSerializer.Serialize(collection, JsonOptions));
        try { File.Move(temporaryPath, ProfilesFilePath, overwrite: true); }
        finally { if (File.Exists(temporaryPath)) File.Delete(temporaryPath); }
    }

    private static void EnsureBlankDatabase(Guid profileId)
    {
        var path = GetDatabaseSnapshotPath(profileId);
        if (File.Exists(path)) return;
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        using var database = global::AniT.Infrastructure.AniTDatabase.Create(path);
    }

    private static void DeleteDatabaseFiles(string path)
    {
        foreach (var candidate in new[] { path, path + "-wal", path + "-shm" })
        {
            try { if (File.Exists(candidate)) File.Delete(candidate); } catch { }
        }
        try
        {
            var directory = Path.GetDirectoryName(path);
            if (!string.IsNullOrWhiteSpace(directory) && Directory.Exists(directory) && !Directory.EnumerateFileSystemEntries(directory).Any()) Directory.Delete(directory);
        }
        catch { }
    }

    private static ProfileSettings Default() => new("Pet-S", "Animes tornam os dias comuns em momentos especiais. ♡", DateTimeOffset.Now, ProfileId: Guid.NewGuid());
}
