using RagnaModManager.Core.Database;

namespace RagnaModManager.Core.Diagnostics;

public sealed record ManagerDiagnostics(
    string DataRoot,
    string DatabasePath,
    string ModLibrary,
    string DeploymentManifest,
    GameRecord? Game,
    ProfileRecord ActiveProfile,
    IReadOnlyList<ModRecord> Mods,
    IReadOnlyList<ProfileModRecord> ProfileMods,
    IReadOnlyList<DeployedFileRecord> DeployedFiles,
    object? Compatibility,
    object? Ue4ss,
    object? LaunchPlan);
