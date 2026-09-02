using RagnaModManager.Core.Deployment;
using RagnaModManager.Core.Compatibility;
using RagnaModManager.Core.Manifests;
using RagnaModManager.Ragnarock.Ue4ss;

namespace RagnaModManager.Ragnarock.DeploymentRules;

public sealed class RagnarockDeploymentRules : IGameDeploymentRules
{
    private readonly Ue4ssService _ue4ss = new();

    public string GameId => "ragnarock";

    public string GetTargetPath(string gameRoot, ModManifest manifest, ManifestFile file, string sourcePath)
    {
        var exeFolder = GetExecutableFolder(gameRoot);
        var ue4ss = _ue4ss.Detect(gameRoot);
        var ue4ssRoot = ue4ss.RootPath;
        var modsRoot = ue4ss.ModsPath;
        var relativeName = GetRelativeSourceName(manifest, file, sourcePath);

        return file.Type.ToLowerInvariant() switch
        {
            "ue4ss-lua" => Path.Combine(modsRoot, file.EffectiveModFolder, "scripts", relativeName),
            "ue4ss-dll" => Path.Combine(modsRoot, file.EffectiveModFolder, "dlls", Path.GetFileName(sourcePath)),
            "pak" => Path.Combine(GetPakModFolder(gameRoot), BuildPakName(manifest, file, sourcePath)),
            "config" => Path.Combine(ue4ssRoot, NormalizeTarget(file.Target ?? Path.Combine("Mods", manifest.Id, Path.GetFileName(sourcePath)))),
            "loose-file" => Path.Combine(gameRoot, NormalizeTarget(file.Target ?? Path.GetFileName(sourcePath))),
            _ => throw new InvalidOperationException($"Unsupported file type: {file.Type}")
        };
    }

    public bool IsApprovedTarget(string gameRoot, string targetPath)
    {
        var fullGame = Path.GetFullPath(gameRoot);
        var fullTarget = Path.GetFullPath(targetPath);
        return IsUnder(fullTarget, Path.Combine(fullGame, "Ragnarock", "Binaries", "Win64", "ue4ss")) ||
               IsUnder(fullTarget, GetPakModFolder(fullGame)) ||
               IsUnder(fullTarget, fullGame);
    }

    public IEnumerable<string> GetRelatedPackageFiles(string sourcePath)
    {
        var directory = Path.GetDirectoryName(sourcePath)!;
        var name = Path.GetFileNameWithoutExtension(sourcePath);
        var baseName = name.EndsWith("_P", StringComparison.OrdinalIgnoreCase) ? name[..^2] : name;
        var candidates = new[]
        {
            sourcePath,
            Path.Combine(directory, baseName + "_P.utoc"),
            Path.Combine(directory, baseName + "_P.ucas"),
            Path.Combine(directory, baseName + ".utoc"),
            Path.Combine(directory, baseName + ".ucas")
        };

        return candidates.Where(File.Exists).Distinct();
    }

    public string GetUe4ssModsFile(string gameRoot) =>
        Path.Combine(_ue4ss.Detect(gameRoot).ModsPath, "mods.txt");

    public IEnumerable<DeploymentConflict> GetRequirementConflicts(string gameRoot, ModManifest manifest, IReadOnlyList<DeploymentItem> manifestItems)
    {
        var usesUe4ss = manifestItems.Any(i => i.FileType.Equals("ue4ss-lua", StringComparison.OrdinalIgnoreCase) ||
                                               i.FileType.Equals("ue4ss-dll", StringComparison.OrdinalIgnoreCase));
        if (!usesUe4ss && manifest.Requires?.ContainsKey("ue4ss") != true)
        {
            yield break;
        }

        var status = _ue4ss.Detect(gameRoot);
        if (!status.Installed)
        {
            yield return new DeploymentConflict(
                "ue4ss-requirement",
                $"{manifest.Id} needs UE4SS, but UE4SS is not installed.",
                manifestItems,
                BlocksDeployment: usesUe4ss);
            yield break;
        }

        if (manifest.Requires?.TryGetValue("ue4ss", out var ue4ssRequirement) == true &&
            (string.IsNullOrWhiteSpace(status.Version) || !VersionRequirement.IsSatisfied(ue4ssRequirement, status.Version)))
        {
            yield return new DeploymentConflict(
                "ue4ss-version",
                $"{manifest.Id} requires UE4SS {ue4ssRequirement}, detected version is {status.Version ?? "unknown"}.",
                manifestItems,
                BlocksDeployment: true);
        }
    }

    public static string GetExecutableFolder(string gameRoot) =>
        Path.Combine(gameRoot, "Ragnarock", "Binaries", "Win64");

    public static string GetPakModFolder(string gameRoot) =>
        Path.Combine(gameRoot, "Ragnarock", "Content", "Paks", "~mods");

    private static string BuildPakName(ModManifest manifest, ManifestFile file, string sourcePath)
    {
        var loadOrder = file.LoadOrder ?? 500;
        var fileName = Path.GetFileName(sourcePath);
        return $"{loadOrder:0000}_{manifest.Id}_{fileName}";
    }

    private static string GetRelativeSourceName(ModManifest manifest, ManifestFile file, string sourcePath)
    {
        var installRoot = Directory.GetParent(Path.GetDirectoryName(sourcePath) ?? "")?.FullName ?? "";
        var sourceRoot = Path.GetDirectoryName(sourcePath) ?? "";

        if (file.Source.EndsWith('/') || file.Source.EndsWith('\\') || Directory.Exists(Path.Combine(installRoot, file.Source)))
        {
            var marker = file.Source.TrimEnd('/', '\\').Replace('\\', Path.DirectorySeparatorChar).Replace('/', Path.DirectorySeparatorChar);
            var index = sourcePath.IndexOf(marker + Path.DirectorySeparatorChar, StringComparison.Ordinal);
            if (index >= 0)
            {
                return sourcePath[(index + marker.Length + 1)..];
            }
        }

        return Path.GetFileName(sourcePath);
    }

    private static string NormalizeTarget(string target) => target.Replace('\\', Path.DirectorySeparatorChar).Replace('/', Path.DirectorySeparatorChar);

    private static bool IsUnder(string path, string root)
    {
        var fullRoot = Path.GetFullPath(root);
        var rootWithSeparator = fullRoot.EndsWith(Path.DirectorySeparatorChar)
            ? fullRoot
            : fullRoot + Path.DirectorySeparatorChar;
        return path.StartsWith(rootWithSeparator, StringComparison.Ordinal) || string.Equals(path, fullRoot, StringComparison.Ordinal);
    }
}
