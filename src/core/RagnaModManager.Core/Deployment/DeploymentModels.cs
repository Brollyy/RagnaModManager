using RagnaModManager.Core.Manifests;

namespace RagnaModManager.Core.Deployment;

public interface IGameDeploymentRules
{
    string GameId { get; }
    string GetTargetPath(string gameRoot, ModManifest manifest, ManifestFile file, string sourcePath);
    bool IsApprovedTarget(string gameRoot, string targetPath);
    IEnumerable<string> GetRelatedPackageFiles(string sourcePath);
    string GetUe4ssModsFile(string gameRoot);
    IEnumerable<DeploymentConflict> GetRequirementConflicts(string gameRoot, ModManifest manifest, IReadOnlyList<DeploymentItem> manifestItems);
}

public sealed record DeploymentItem(string ModId, string SourcePath, string TargetPath, string Method, string FileType, string? ModFolder = null);

public sealed record DeploymentConflict(string Kind, string Message, IReadOnlyList<DeploymentItem> Items, bool BlocksDeployment, string? RelatedModId = null);

public sealed record DeploymentPlan(
    string ProfileId,
    IReadOnlyList<DeploymentItem> Items,
    IReadOnlyList<DeploymentConflict> Conflicts,
    IReadOnlyList<string> Warnings,
    IReadOnlyList<string> Ue4ssLoadOrder)
{
    public bool CanDeploy => Conflicts.All(c => !c.BlocksDeployment);
}

public sealed record DeploymentFileManifest(string ModId, string Source, string Target, string Method, string Checksum);

public sealed record DeploymentManifest(string Profile, DateTimeOffset DeployedAt, IReadOnlyList<DeploymentFileManifest> Files);

public sealed record DeploymentBackupManifest(string Profile, DateTimeOffset CreatedAt, IReadOnlyList<DeploymentBackupFile> Files);

public sealed record DeploymentBackupFile(
    string ModId,
    string Source,
    string Target,
    string Method,
    string Checksum,
    string BackupPath);
