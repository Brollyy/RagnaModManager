using RagnaModManager.Core.Manifests;
using RagnaModManager.Core.Common;
using RagnaModManager.Core.Compatibility;
using RagnaModManager.Core.Platform;
using RagnaModManager.Core.Profiles;
using System.Text.Json;

namespace RagnaModManager.Core.Database;

public sealed class ManagerDatabase
{
    private readonly string _path;
    private readonly AppPaths _paths;
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true, PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

    public ManagerDatabase(AppPaths paths)
    {
        _paths = paths;
        _path = paths.DatabasePath;
    }

    public void Initialize()
    {
        using var db = Open();
        db.Execute("""
            CREATE TABLE IF NOT EXISTS games (
              id TEXT PRIMARY KEY,
              name TEXT NOT NULL,
              install_path TEXT NOT NULL,
              executable_path TEXT,
              detected_version TEXT,
              platform TEXT,
              created_at TEXT NOT NULL,
              updated_at TEXT NOT NULL
            );
            CREATE TABLE IF NOT EXISTS mods (
              id TEXT PRIMARY KEY,
              name TEXT NOT NULL,
              version TEXT NOT NULL,
              author TEXT,
              installed_path TEXT NOT NULL,
              manifest_path TEXT NOT NULL,
              source_archive TEXT,
              checksum TEXT,
              installed_at TEXT NOT NULL,
              updated_at TEXT NOT NULL
            );
            CREATE TABLE IF NOT EXISTS mod_versions (
              id TEXT NOT NULL,
              version TEXT NOT NULL,
              name TEXT NOT NULL,
              author TEXT,
              installed_path TEXT NOT NULL,
              manifest_path TEXT NOT NULL,
              source_archive TEXT,
              installed_at TEXT NOT NULL,
              updated_at TEXT NOT NULL,
              PRIMARY KEY (id, version)
            );
            CREATE TABLE IF NOT EXISTS profiles (
              id TEXT PRIMARY KEY,
              name TEXT NOT NULL,
              game_id TEXT NOT NULL,
              is_active INTEGER NOT NULL DEFAULT 0,
              created_at TEXT NOT NULL,
              updated_at TEXT NOT NULL
            );
            CREATE TABLE IF NOT EXISTS profile_mods (
              profile_id TEXT NOT NULL,
              mod_id TEXT NOT NULL,
              enabled INTEGER NOT NULL DEFAULT 1,
              priority INTEGER NOT NULL DEFAULT 0,
              version TEXT,
              PRIMARY KEY (profile_id, mod_id)
            );
            CREATE TABLE IF NOT EXISTS deployed_files (
              id INTEGER PRIMARY KEY AUTOINCREMENT,
              profile_id TEXT NOT NULL,
              mod_id TEXT NOT NULL,
              source_path TEXT NOT NULL,
              target_path TEXT NOT NULL,
              deployment_method TEXT NOT NULL,
              checksum TEXT,
              deployed_at TEXT NOT NULL
            );
            """);
        try { db.Execute("ALTER TABLE profile_mods ADD COLUMN version TEXT;"); } catch (Exception) { }
        db.Execute("INSERT OR IGNORE INTO mod_versions (id, version, name, author, installed_path, manifest_path, source_archive, installed_at, updated_at) SELECT id, version, name, author, installed_path, manifest_path, source_archive, installed_at, updated_at FROM mods;");
        EnsureDefaultProfile();
    }

    public void UpsertGame(GameRecord game)
    {
        using var db = Open();
        var now = Now();
        db.Execute($"""
            INSERT INTO games (id, name, install_path, executable_path, detected_version, platform, created_at, updated_at)
            VALUES ({Q(game.Id)}, {Q(game.Name)}, {Q(game.InstallPath)}, {Q(game.ExecutablePath)}, {Q(game.DetectedVersion)}, {Q(game.Platform)}, {Q(now)}, {Q(now)})
            ON CONFLICT(id) DO UPDATE SET
              name=excluded.name,
              install_path=excluded.install_path,
              executable_path=excluded.executable_path,
              detected_version=excluded.detected_version,
              platform=excluded.platform,
              updated_at=excluded.updated_at;
            """);
        EnsureDefaultProfile(game.Id);
    }

    public GameRecord? GetGame(string id = "ragnarock")
    {
        using var db = Open();
        return db.Query($"SELECT * FROM games WHERE id={Q(id)} LIMIT 1").Select(ToGame).FirstOrDefault();
    }

    public void UpsertMod(ModManifest manifest, string installedPath, string manifestPath, string sourceArchive)
    {
        using var db = Open();
        var now = Now();
        db.Execute($"""
            INSERT INTO mod_versions (id, version, name, author, installed_path, manifest_path, source_archive, installed_at, updated_at)
            VALUES ({Q(manifest.Id)}, {Q(manifest.Version)}, {Q(manifest.Name)}, {Q(manifest.Author)}, {Q(installedPath)}, {Q(manifestPath)}, {Q(sourceArchive)}, {Q(now)}, {Q(now)})
            ON CONFLICT(id, version) DO UPDATE SET name=excluded.name, author=excluded.author, installed_path=excluded.installed_path, manifest_path=excluded.manifest_path, source_archive=excluded.source_archive, updated_at=excluded.updated_at;
            """);
        db.Execute($"""
            INSERT INTO mods (id, name, version, author, installed_path, manifest_path, source_archive, checksum, installed_at, updated_at)
            VALUES ({Q(manifest.Id)}, {Q(manifest.Name)}, {Q(manifest.Version)}, {Q(manifest.Author)}, {Q(installedPath)}, {Q(manifestPath)}, {Q(sourceArchive)}, NULL, {Q(now)}, {Q(now)})
            ON CONFLICT(id) DO UPDATE SET
              name=excluded.name,
              version=excluded.version,
              author=excluded.author,
              installed_path=excluded.installed_path,
              manifest_path=excluded.manifest_path,
              source_archive=excluded.source_archive,
              updated_at=excluded.updated_at;
            """);
    }

    public IReadOnlyList<ModRecord> GetMods()
    {
        using var db = Open();
        return db.Query("SELECT * FROM mods ORDER BY name").Select(ToMod).ToList();
    }

    public IReadOnlyList<ModRecord> GetModVersions(string id)
    {
        using var db = Open();
        return db.Query($"SELECT id, name, version, author, installed_path, manifest_path, source_archive FROM mod_versions WHERE id={Q(id)}")
            .Select(ToMod).OrderByDescending(m => m.Version, Comparer<string>.Create(SemanticVersion.Compare)).ToList();
    }

    public ModRecord? GetMod(string id)
    {
        return GetModVersions(id).FirstOrDefault() ?? GetCurrentMod(id);
    }

    public ModRecord? GetMod(string id, string version)
    {
        return GetModVersions(id).FirstOrDefault(m => string.Equals(m.Version, version, StringComparison.OrdinalIgnoreCase));
    }

    public void EnsureDefaultProfile(string gameId = "ragnarock")
    {
        using var db = Open();
        var now = Now();
        db.Execute($"""
            INSERT OR IGNORE INTO profiles (id, name, game_id, is_active, created_at, updated_at)
            VALUES ('default', 'Default', {Q(gameId)}, 1, {Q(now)}, {Q(now)});
            UPDATE profiles SET is_active = CASE WHEN id='default' THEN 1 ELSE 0 END;
            """);
        WriteProfileJson("default");
    }

    public ProfileRecord GetActiveProfile()
    {
        using var db = Open();
        var row = db.Query("SELECT * FROM profiles WHERE is_active=1 LIMIT 1").FirstOrDefault()
                  ?? throw new InvalidOperationException("No active profile exists.");
        return ToProfile(row);
    }

    public IReadOnlyList<ProfileRecord> GetProfiles()
    {
        using var db = Open();
        return db.Query("SELECT * FROM profiles ORDER BY name").Select(ToProfile).ToList();
    }

    public ProfileRecord? GetProfile(string profileId)
    {
        using var db = Open();
        return db.Query($"SELECT * FROM profiles WHERE id={Q(profileId)} LIMIT 1").Select(ToProfile).FirstOrDefault();
    }

    public void CreateProfile(string id, string name, string gameId = "ragnarock")
    {
        if (!IsProfileId(id))
        {
            throw new InvalidOperationException("Profile id must be lowercase letters, numbers, dots, underscores, or hyphens.");
        }

        if (string.IsNullOrWhiteSpace(name))
        {
            throw new InvalidOperationException("Profile name is required.");
        }

        if (GetProfile(id) is not null)
        {
            throw new InvalidOperationException($"Profile already exists: {id}");
        }

        using var db = Open();
        var now = Now();
        db.Execute($"""
            INSERT INTO profiles (id, name, game_id, is_active, created_at, updated_at)
            VALUES ({Q(id)}, {Q(name)}, {Q(gameId)}, 0, {Q(now)}, {Q(now)});
            """);
        WriteProfileJson(id);
    }

    public void SetActiveProfile(string profileId)
    {
        if (GetProfile(profileId) is null)
        {
            throw new InvalidOperationException($"Unknown profile id: {profileId}");
        }

        using var db = Open();
        var now = Now();
        db.Execute($"""
            UPDATE profiles SET is_active = CASE WHEN id={Q(profileId)} THEN 1 ELSE 0 END, updated_at={Q(now)};
            """);
        foreach (var profile in GetProfiles())
        {
            WriteProfileJson(profile.Id);
        }
    }

    public void RenameProfile(string profileId, string name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new InvalidOperationException("Profile name is required.");
        }

        if (GetProfile(profileId) is null)
        {
            throw new InvalidOperationException($"Profile does not exist: {profileId}");
        }

        using var db = Open();
        db.Execute($"UPDATE profiles SET name={Q(name.Trim())}, updated_at={Q(Now())} WHERE id={Q(profileId)};");
        WriteProfileJson(profileId);
    }

    public void DeleteProfile(string profileId)
    {
        var profile = GetProfile(profileId) ?? throw new InvalidOperationException($"Profile does not exist: {profileId}");
        if (profile.IsActive)
        {
            throw new InvalidOperationException("Switch to another profile before deleting the active profile.");
        }

        if (GetProfiles().Count <= 1)
        {
            throw new InvalidOperationException("At least one profile must remain.");
        }

        using var db = Open();
        db.Execute($"DELETE FROM profile_mods WHERE profile_id={Q(profileId)}; DELETE FROM deployed_files WHERE profile_id={Q(profileId)}; DELETE FROM profiles WHERE id={Q(profileId)};");
        var profilePath = Path.Combine(_paths.Profiles, profileId + ".json");
        if (File.Exists(profilePath)) File.Delete(profilePath);
    }

    public void AddModToDefaultProfile(string modId, bool enabled, int priority)
    {
        foreach (var profile in GetProfiles())
        {
            SetProfileMod(profile.Id, modId, enabled, priority, insertOnly: true);
        }
    }

    public void SetProfileMod(string profileId, string modId, bool enabled, int priority, string? version = null)
    {
        SetProfileMod(profileId, modId, enabled, priority, version, insertOnly: false);
    }

    public void RemoveProfileMod(string profileId, string modId)
    {
        using var db = Open();
        db.Execute($"DELETE FROM profile_mods WHERE profile_id={Q(profileId)} AND mod_id={Q(modId)};");
        WriteProfileJson(profileId);
    }

    public void ClearProfileModVersion(string profileId, string modId)
    {
        using var db = Open();
        db.Execute($"UPDATE profile_mods SET version=NULL WHERE profile_id={Q(profileId)} AND mod_id={Q(modId)};");
        WriteProfileJson(profileId);
    }

    public void RemoveMod(string modId)
    {
        using var db = Open();
        db.Execute($"DELETE FROM profile_mods WHERE mod_id={Q(modId)}; DELETE FROM deployed_files WHERE mod_id={Q(modId)}; DELETE FROM mod_versions WHERE id={Q(modId)}; DELETE FROM mods WHERE id={Q(modId)};");
        foreach (var profile in GetProfiles())
        {
            WriteProfileJson(profile.Id);
        }
    }

    public IReadOnlyList<ProfileModRecord> GetProfileMods(string profileId)
    {
        using var db = Open();
        return db.Query($"SELECT * FROM profile_mods WHERE profile_id={Q(profileId)} ORDER BY priority, mod_id")
            .Select(ToProfileMod)
            .ToList();
    }

    public IReadOnlyList<DeployedFileRecord> GetDeployedFiles(string profileId)
    {
        using var db = Open();
        return db.Query($"SELECT * FROM deployed_files WHERE profile_id={Q(profileId)} ORDER BY target_path")
            .Select(ToDeployedFile)
            .ToList();
    }

    public void ReplaceDeployedFiles(string profileId, IEnumerable<DeployedFileRecord> files)
    {
        using var db = Open();
        db.Execute("BEGIN TRANSACTION;");
        try
        {
            db.Execute($"DELETE FROM deployed_files WHERE profile_id={Q(profileId)};");
            var now = Now();
            foreach (var file in files)
            {
                db.Execute($"""
                    INSERT INTO deployed_files (profile_id, mod_id, source_path, target_path, deployment_method, checksum, deployed_at)
                    VALUES ({Q(file.ProfileId)}, {Q(file.ModId)}, {Q(file.SourcePath)}, {Q(file.TargetPath)}, {Q(file.DeploymentMethod)}, {Q(file.Checksum)}, {Q(now)});
                    """);
            }

            db.Execute("COMMIT;");
        }
        catch
        {
            db.Execute("ROLLBACK;");
            throw;
        }
    }

    private void SetProfileMod(string profileId, string modId, bool enabled, int priority, string? version = null, bool insertOnly = false)
    {
        using (var db = Open())
        {
            if (insertOnly)
            {
                db.Execute($"""
                    INSERT OR IGNORE INTO profile_mods (profile_id, mod_id, enabled, priority, version)
                    VALUES ({Q(profileId)}, {Q(modId)}, {(enabled ? 1 : 0)}, {priority}, {Q(version)});
                    """);
            }
            else
            {
                db.Execute($"""
                    INSERT INTO profile_mods (profile_id, mod_id, enabled, priority, version)
                    VALUES ({Q(profileId)}, {Q(modId)}, {(enabled ? 1 : 0)}, {priority}, {Q(version)})
                    ON CONFLICT(profile_id, mod_id) DO UPDATE SET
                      enabled=excluded.enabled,
                      priority=excluded.priority,
                      version=COALESCE(excluded.version, profile_mods.version);
                    """);
            }
        }

        WriteProfileJson(profileId);
    }

    public void WriteProfileJson(string profileId)
    {
        using var db = Open();
        var profile = db.Query($"SELECT * FROM profiles WHERE id={Q(profileId)} LIMIT 1").Select(ToProfile).FirstOrDefault();
        if (profile is null)
        {
            return;
        }

        var mods = db.Query($"SELECT * FROM profile_mods WHERE profile_id={Q(profileId)} ORDER BY priority, mod_id")
            .Select(ToProfileMod)
            .Select(m => new ProfileModDocument(m.ModId, m.Enabled, m.Priority, m.Version))
            .ToList();
        var document = new ProfileDocument(profile.Id, profile.Name, mods);
        var path = Path.Combine(_paths.Profiles, profile.Id + ".json");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, JsonSerializer.Serialize(document, JsonOptions));
    }

    public Result ExportProfile(string profileId, string destinationPath)
    {
        var profile = GetProfile(profileId);
        if (profile is null) return Result.Fail($"Profile does not exist: {profileId}");
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(destinationPath))!);
            var mods = GetProfileMods(profileId)
                .Select(m => new ProfileModDocument(m.ModId, m.Enabled, m.Priority, m.Version))
                .ToList();
            var document = new ProfileDocument(profile.Id, profile.Name, mods);
            File.WriteAllText(destinationPath, JsonSerializer.Serialize(document, JsonOptions));
            return Result.Ok();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return Result.Fail($"Could not export profile: {ex.Message}");
        }
    }

    public Result<ProfileRecord> ImportProfile(string sourcePath, string profileId, string name)
    {
        try
        {
            var document = JsonSerializer.Deserialize<ProfileDocument>(File.ReadAllText(sourcePath), JsonOptions);
            if (document is null || document.Mods is null)
                return Result<ProfileRecord>.Fail("The profile file is empty or malformed.");
            CreateProfile(profileId, name);
            foreach (var mod in document.Mods)
            {
                if (string.IsNullOrWhiteSpace(mod.Id)) continue;
                SetProfileMod(profileId, mod.Id, mod.Enabled, mod.Priority, mod.Version);
            }
            return Result<ProfileRecord>.Ok(GetProfile(profileId)!);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException or InvalidOperationException)
        {
            return Result<ProfileRecord>.Fail($"Could not import profile: {ex.Message}");
        }
    }

    private SqliteConnection Open() => new(_path);

    private ModRecord? GetCurrentMod(string id)
    {
        using var db = Open();
        return db.Query($"SELECT * FROM mods WHERE id={Q(id)} LIMIT 1").Select(ToMod).FirstOrDefault();
    }

    private static string Q(string? value) => SqliteConnection.Quote(value);

    private static string Now() => DateTimeOffset.UtcNow.ToString("O");

    private static bool IsProfileId(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        return value.All(c => char.IsAsciiLetterLower(c) || char.IsDigit(c) || c is '-' or '_' or '.');
    }

    private static GameRecord ToGame(Dictionary<string, string?> row) =>
        new(row["id"]!, row["name"]!, row["install_path"]!, row["executable_path"], row["detected_version"], row["platform"]);

    private static ModRecord ToMod(Dictionary<string, string?> row) =>
        new(row["id"]!, row["name"]!, row["version"]!, row["author"], row["installed_path"]!, row["manifest_path"]!, row["source_archive"]);

    private static ProfileRecord ToProfile(Dictionary<string, string?> row) =>
        new(row["id"]!, row["name"]!, row["game_id"]!, row["is_active"] == "1");

    private static ProfileModRecord ToProfileMod(Dictionary<string, string?> row) =>
        new(row["profile_id"]!, row["mod_id"]!, row["enabled"] == "1", int.Parse(row["priority"] ?? "0"), row.GetValueOrDefault("version"));

    private static DeployedFileRecord ToDeployedFile(Dictionary<string, string?> row) =>
        new(row["profile_id"]!, row["mod_id"]!, row["source_path"]!, row["target_path"]!, row["deployment_method"]!, row["checksum"] ?? "");
}
