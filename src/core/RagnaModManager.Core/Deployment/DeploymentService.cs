using System.Text.Json;
using RagnaModManager.Core.Checksums;
using RagnaModManager.Core.Common;
using RagnaModManager.Core.Database;
using RagnaModManager.Core.Logging;
using RagnaModManager.Core.Platform;

namespace RagnaModManager.Core.Deployment;

public sealed class DeploymentService
{
    private readonly AppPaths _paths;
    private readonly ManagerDatabase _database;
    private readonly DeploymentPlanner _planner;
    private readonly IGameDeploymentRules _rules;
    private readonly AppLogger _logger;

    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true, PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

    public DeploymentService(AppPaths paths, ManagerDatabase database, DeploymentPlanner planner, IGameDeploymentRules rules, AppLogger logger)
    {
        _paths = paths;
        _database = database;
        _planner = planner;
        _rules = rules;
        _logger = logger;
    }

    public Result<DeploymentPlan> Preview(string gameRoot) => _planner.BuildPlan(gameRoot);

    public Result Deploy(string gameRoot, bool allowWarnings = false)
    {
        var planResult = _planner.BuildPlan(gameRoot);
        if (!planResult.Success)
        {
            return Result.Fail(planResult.Error!);
        }

        var plan = planResult.Value!;
        if (!plan.CanDeploy)
        {
            return Result.Fail("Deployment blocked by conflicts: " + string.Join("; ", plan.Conflicts.Where(c => c.BlocksDeployment).Select(c => c.Message)));
        }

        if (!allowWarnings && plan.Conflicts.Any(c => !c.BlocksDeployment))
        {
            return Result.Fail("Deployment has advisory conflicts. Re-run with --allow-warnings to accept: " + string.Join("; ", plan.Conflicts.Where(c => !c.BlocksDeployment).Select(c => c.Message)));
        }

        try
        {
            var retained = new List<DeployedFileRecord>();
            foreach (var profile in _database.GetProfiles())
            {
                BackupPreviousDeployment(profile.Id);
                var retainedForProfile = CleanupPrevious(profile.Id, gameRoot, retainDisabledUe4ss: true);
                if (!profile.Id.Equals(plan.ProfileId, StringComparison.OrdinalIgnoreCase))
                {
                    _database.ReplaceDeployedFiles(profile.Id, retainedForProfile);
                }
                else
                {
                    retained.AddRange(retainedForProfile);
                }
            }

            var deployed = new List<DeployedFileRecord>(retained);

            foreach (var item in plan.Items)
            {
                if (!File.Exists(item.SourcePath))
                {
                    return Result.Fail($"Source file missing during deployment: {item.SourcePath}");
                }

                if (File.Exists(item.TargetPath) && !CanOverwriteOwnedFile(plan.ProfileId, item.TargetPath))
                {
                    return Result.Fail($"Target file already exists and is not owned by this manager: {item.TargetPath}");
                }

                Directory.CreateDirectory(Path.GetDirectoryName(item.TargetPath)!);
                File.Copy(item.SourcePath, item.TargetPath, overwrite: true);
                var checksum = Sha256.FileChecksum(item.TargetPath);
                deployed.RemoveAll(file => string.Equals(file.TargetPath, item.TargetPath, StringComparison.OrdinalIgnoreCase));
                deployed.Add(new DeployedFileRecord(plan.ProfileId, item.ModId, item.SourcePath, item.TargetPath, item.Method, checksum));
                _logger.Deployment($"Copied {item.SourcePath} -> {item.TargetPath}");
            }

            WriteUe4ssEnabledState(gameRoot, plan, deployed);
            _database.ReplaceDeployedFiles(plan.ProfileId, deployed);
            WriteCurrentManifest(plan.ProfileId, deployed);
            return Result.Ok();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            return Result.Fail($"Deployment failed: {ex.Message}");
        }
    }

    private List<DeployedFileRecord> CleanupPrevious(string profileId, string? gameRoot = null, bool retainDisabledUe4ss = false)
    {
        var retained = new List<DeployedFileRecord>();
        foreach (var previous in _database.GetDeployedFiles(profileId))
        {
            if (!File.Exists(previous.TargetPath))
            {
                continue;
            }

            var currentChecksum = Sha256.FileChecksum(previous.TargetPath);
            if (!string.Equals(currentChecksum, previous.Checksum, StringComparison.OrdinalIgnoreCase))
            {
                _logger.Deployment($"Skipped cleanup because checksum changed: {previous.TargetPath}");
                retained.Add(previous);
                continue;
            }

            if (retainDisabledUe4ss && gameRoot is not null && TryGetUe4ssModFolder(gameRoot, previous.TargetPath, out _))
            {
                retained.Add(previous);
                _logger.Deployment($"Retained disabled UE4SS file {previous.TargetPath}");
                continue;
            }

            File.Delete(previous.TargetPath);
            _logger.Deployment($"Removed stale manager-owned file {previous.TargetPath}");
        }

        return retained;
    }

    public Result RollbackLatest(string? profileId = null)
    {
        profileId ??= _database.GetActiveProfile().Id;
        var latestBackup = FindLatestBackupForProfile(profileId);
        if (latestBackup is null)
        {
            return Result.Fail($"No deployment backup is available to roll back for profile {profileId}.");
        }

        var manifestPath = Path.Combine(latestBackup, "backup.json");
        try
        {
            var manifest = JsonSerializer.Deserialize<DeploymentBackupManifest>(File.ReadAllText(manifestPath), JsonOptions);
            if (manifest is null)
            {
                return Result.Fail($"Backup manifest is invalid: {manifestPath}");
            }

            if (!manifest.Profile.Equals(profileId, StringComparison.OrdinalIgnoreCase))
            {
                return Result.Fail($"Backup manifest profile mismatch: expected {profileId}, found {manifest.Profile}.");
            }

            CleanupPrevious(profileId);
            var restored = new List<DeployedFileRecord>();
            foreach (var file in manifest.Files)
            {
                if (!File.Exists(file.BackupPath))
                {
                    return Result.Fail($"Backup file is missing: {file.BackupPath}");
                }

                Directory.CreateDirectory(Path.GetDirectoryName(file.Target)!);
                File.Copy(file.BackupPath, file.Target, overwrite: true);
                var checksum = Sha256.FileChecksum(file.Target);
                if (!string.Equals(checksum, file.Checksum, StringComparison.OrdinalIgnoreCase))
                {
                    return Result.Fail($"Restored file checksum mismatch: {file.Target}");
                }

                restored.Add(new DeployedFileRecord(profileId, file.ModId, file.Source, file.Target, file.Method, file.Checksum));
                _logger.Deployment($"Rolled back {file.Target}");
            }

            _database.ReplaceDeployedFiles(profileId, restored);
            WriteCurrentManifest(profileId, restored);
            _logger.Deployment($"Rolled back deployment from {latestBackup}");
            return Result.Ok();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            return Result.Fail($"Rollback failed: {ex.Message}");
        }
    }

    private string? FindLatestBackupForProfile(string profileId)
    {
        if (!Directory.Exists(_paths.Backups))
        {
            return null;
        }

        foreach (var backup in Directory.EnumerateDirectories(_paths.Backups).OrderDescending())
        {
            var manifestPath = Path.Combine(backup, "backup.json");
            if (!File.Exists(manifestPath))
            {
                continue;
            }

            try
            {
                var manifest = JsonSerializer.Deserialize<DeploymentBackupManifest>(File.ReadAllText(manifestPath), JsonOptions);
                if (manifest?.Profile.Equals(profileId, StringComparison.OrdinalIgnoreCase) == true)
                {
                    return backup;
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
            {
                _logger.Deployment($"Skipped unreadable backup manifest {manifestPath}: {ex.Message}");
            }
        }

        return null;
    }

    public Result ResetDeployment(string? profileId = null)
    {
        profileId ??= _database.GetActiveProfile().Id;
        try
        {
            BackupPreviousDeployment(profileId);
            CleanupPrevious(profileId);
            _database.ReplaceDeployedFiles(profileId, []);
            WriteCurrentManifest(profileId, []);
            _logger.Deployment($"Reset deployment for profile {profileId}");
            return Result.Ok();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            return Result.Fail($"Reset deployment failed: {ex.Message}");
        }
    }

    private bool CanOverwriteOwnedFile(string profileId, string targetPath)
    {
        var previous = _database.GetDeployedFiles(profileId)
            .FirstOrDefault(f => string.Equals(f.TargetPath, targetPath, StringComparison.OrdinalIgnoreCase));
        if (previous is null)
        {
            return false;
        }

        return File.Exists(targetPath) &&
               string.Equals(Sha256.FileChecksum(targetPath), previous.Checksum, StringComparison.OrdinalIgnoreCase);
    }

    private void BackupPreviousDeployment(string profileId)
    {
        var previous = _database.GetDeployedFiles(profileId);
        var backupCandidates = new List<DeployedFileRecord>();
        foreach (var file in previous)
        {
            if (!File.Exists(file.TargetPath))
            {
                _logger.Deployment($"Skipped backup because deployed file is missing: {file.TargetPath}");
                continue;
            }

            var checksum = Sha256.FileChecksum(file.TargetPath);
            if (!string.Equals(checksum, file.Checksum, StringComparison.OrdinalIgnoreCase))
            {
                _logger.Deployment($"Skipped backup because deployed file checksum changed: {file.TargetPath}");
                continue;
            }

            backupCandidates.Add(file);
        }

        if (backupCandidates.Count == 0)
        {
            _logger.Deployment($"Skipped deployment backup for {profileId} because there are no restorable files.");
            return;
        }

        var backupRoot = Path.Combine(_paths.Backups, DateTimeOffset.UtcNow.ToString("yyyyMMddHHmmssfff"));
        var filesRoot = Path.Combine(backupRoot, "files");
        Directory.CreateDirectory(filesRoot);

        if (File.Exists(_paths.CurrentDeploymentPath))
        {
            File.Copy(_paths.CurrentDeploymentPath, Path.Combine(backupRoot, "current.json"), overwrite: true);
        }

        var backedUp = new List<DeploymentBackupFile>();
        var index = 0;
        foreach (var file in backupCandidates)
        {
            var backupPath = Path.Combine(filesRoot, $"{index++:000000}_{Path.GetFileName(file.TargetPath)}");
            File.Copy(file.TargetPath, backupPath, overwrite: false);
            backedUp.Add(new DeploymentBackupFile(file.ModId, file.SourcePath, file.TargetPath, file.DeploymentMethod, file.Checksum, backupPath));
        }

        var manifest = new DeploymentBackupManifest(profileId, DateTimeOffset.UtcNow, backedUp);
        File.WriteAllText(Path.Combine(backupRoot, "backup.json"), JsonSerializer.Serialize(manifest, JsonOptions));
        _logger.Deployment($"Wrote deployment backup {backupRoot}");
    }

    private void WriteUe4ssEnabledState(string gameRoot, DeploymentPlan plan, IReadOnlyList<DeployedFileRecord> deployed)
    {
        var activeUe4ssMods = plan.Items
            .Where(i => i.FileType.Equals("ue4ss-lua", StringComparison.OrdinalIgnoreCase) ||
                        i.FileType.Equals("ue4ss-dll", StringComparison.OrdinalIgnoreCase))
            .Select(i => Path.GetFileName(Path.GetDirectoryName(Path.GetDirectoryName(i.TargetPath)) ?? ""))
            .Where(v => !string.IsNullOrWhiteSpace(v))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var allKnownUe4ssMods = deployed
            .Select(file => TryGetUe4ssModFolder(gameRoot, file.TargetPath, out var modFolder) ? modFolder : null)
            .Where(modFolder => !string.IsNullOrWhiteSpace(modFolder))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Order(StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (allKnownUe4ssMods.Count == 0)
        {
            return;
        }

        var modsFile = _rules.GetUe4ssModsFile(gameRoot);
        Directory.CreateDirectory(Path.GetDirectoryName(modsFile)!);
        File.WriteAllLines(modsFile, allKnownUe4ssMods.Select(mod => $"{mod} : {(activeUe4ssMods.Contains(mod!) ? 1 : 0)}"));
        _logger.Deployment($"Wrote UE4SS enabled state to {modsFile}");
    }

    private static bool TryGetUe4ssModFolder(string gameRoot, string targetPath, out string? modFolder)
    {
        modFolder = null;
        var modsRoot = Path.Combine(gameRoot, "Ragnarock", "Binaries", "Win64", "ue4ss", "Mods");
        var fullModsRoot = Path.GetFullPath(modsRoot);
        var fullTarget = Path.GetFullPath(targetPath);
        var rootWithSeparator = fullModsRoot.EndsWith(Path.DirectorySeparatorChar)
            ? fullModsRoot
            : fullModsRoot + Path.DirectorySeparatorChar;
        if (!fullTarget.StartsWith(rootWithSeparator, StringComparison.Ordinal))
        {
            return false;
        }

        var relative = fullTarget[rootWithSeparator.Length..];
        var firstSeparator = relative.IndexOf(Path.DirectorySeparatorChar);
        if (firstSeparator <= 0)
        {
            return false;
        }

        modFolder = relative[..firstSeparator];
        return !modFolder.Equals("mods.txt", StringComparison.OrdinalIgnoreCase);
    }

    private void WriteCurrentManifest(string profileId, IReadOnlyList<DeployedFileRecord> deployed)
    {
        var manifest = new DeploymentManifest(
            profileId,
            DateTimeOffset.UtcNow,
            deployed.Select(d => new DeploymentFileManifest(d.ModId, d.SourcePath, d.TargetPath, d.DeploymentMethod, d.Checksum)).ToList());
        File.WriteAllText(_paths.CurrentDeploymentPath, JsonSerializer.Serialize(manifest, JsonOptions));
    }

}
