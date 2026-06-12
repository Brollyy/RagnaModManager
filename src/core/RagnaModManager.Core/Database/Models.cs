namespace RagnaModManager.Core.Database;

public sealed record GameRecord(string Id, string Name, string InstallPath, string? ExecutablePath, string? DetectedVersion, string? Platform);

public sealed record ModRecord(
    string Id,
    string Name,
    string Version,
    string? Author,
    string InstalledPath,
    string ManifestPath,
    string? SourceArchive);

public sealed record ProfileRecord(string Id, string Name, string GameId, bool IsActive);

public sealed record ProfileModRecord(string ProfileId, string ModId, bool Enabled, int Priority);

public sealed record DeployedFileRecord(string ProfileId, string ModId, string SourcePath, string TargetPath, string DeploymentMethod, string Checksum);
