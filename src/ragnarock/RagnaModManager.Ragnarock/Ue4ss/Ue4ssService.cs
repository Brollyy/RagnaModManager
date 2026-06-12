using System.IO.Compression;
using RagnaModManager.Core.Common;

namespace RagnaModManager.Ragnarock.Ue4ss;

public sealed record Ue4ssStatus(bool Installed, string Layout, string? Version, IReadOnlyList<string> Diagnostics);

public sealed class Ue4ssService
{
    public Ue4ssStatus Detect(string gameRoot)
    {
        var exeFolder = DeploymentRules.RagnarockDeploymentRules.GetExecutableFolder(gameRoot);
        var diagnostics = new List<string>();
        var modernRoot = Path.Combine(exeFolder, "ue4ss");
        var modernDll = Path.Combine(modernRoot, "UE4SS.dll");
        var proxyDll = Directory.Exists(exeFolder)
            ? Directory.EnumerateFiles(exeFolder, "*.dll").FirstOrDefault(p => !Path.GetFileName(p).Equals("UE4SS.dll", StringComparison.OrdinalIgnoreCase))
            : null;

        if (File.Exists(modernDll))
        {
            if (!Directory.Exists(Path.Combine(modernRoot, "Mods")))
            {
                diagnostics.Add("UE4SS is present but ue4ss/Mods is missing.");
            }

            return new Ue4ssStatus(true, "modern-ue4ss-subfolder", DetectVersion(modernRoot), diagnostics);
        }

        if (File.Exists(Path.Combine(exeFolder, "UE4SS.dll")))
        {
            return new Ue4ssStatus(true, "legacy-exe-folder", DetectVersion(exeFolder), ["Legacy UE4SS layout detected. The manager deploys to the modern ue4ss/ subfolder layout."]);
        }

        diagnostics.Add("UE4SS.dll was not found under Ragnarock/Binaries/Win64/ue4ss.");
        if (proxyDll is null)
        {
            diagnostics.Add("No proxy DLL was found next to the game executable.");
        }

        return new Ue4ssStatus(false, "missing", null, diagnostics);
    }

    public Result InstallFromZip(string gameRoot, string zipPath, string? installedVersion = null)
    {
        if (!File.Exists(zipPath))
        {
            return Result.Fail($"UE4SS zip does not exist: {zipPath}");
        }

        var exeFolder = DeploymentRules.RagnarockDeploymentRules.GetExecutableFolder(gameRoot);
        Directory.CreateDirectory(exeFolder);

        try
        {
            using var archive = ZipFile.OpenRead(zipPath);
            if (!archive.Entries.Any(e => Path.GetFileName(e.FullName).Equals("UE4SS.dll", StringComparison.OrdinalIgnoreCase)))
            {
                return Result.Fail("UE4SS zip does not contain UE4SS.dll.");
            }

            foreach (var entry in archive.Entries)
            {
                var normalized = entry.FullName.Replace('\\', '/');
                if (string.IsNullOrWhiteSpace(normalized) || normalized.EndsWith('/'))
                {
                    continue;
                }

                if (Path.IsPathRooted(normalized) || normalized.Contains("..") || normalized.Contains(':'))
                {
                    return Result.Fail($"UE4SS zip contains unsafe path: {entry.FullName}");
                }

                var target = MapUe4ssZipEntry(exeFolder, normalized);
                Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                entry.ExtractToFile(target, overwrite: true);
            }

            Directory.CreateDirectory(Path.Combine(exeFolder, "ue4ss", "Mods"));
            if (!string.IsNullOrWhiteSpace(installedVersion))
            {
                File.WriteAllText(Path.Combine(exeFolder, "ue4ss", "UE4SS-version.txt"), installedVersion.Trim());
            }

            return Result.Ok();
        }
        catch (InvalidDataException ex)
        {
            return Result.Fail($"UE4SS zip is invalid: {ex.Message}");
        }
    }

    private static string MapUe4ssZipEntry(string exeFolder, string entry)
    {
        var fileName = Path.GetFileName(entry);
        if (fileName.Equals("UE4SS.dll", StringComparison.OrdinalIgnoreCase) ||
            fileName.Equals("UE4SS-settings.ini", StringComparison.OrdinalIgnoreCase))
        {
            return Path.Combine(exeFolder, "ue4ss", fileName);
        }

        if (entry.StartsWith("Mods/", StringComparison.OrdinalIgnoreCase) ||
            entry.StartsWith("ue4ss/Mods/", StringComparison.OrdinalIgnoreCase))
        {
            var relative = entry.StartsWith("ue4ss/", StringComparison.OrdinalIgnoreCase) ? entry["ue4ss/".Length..] : entry;
            return Path.Combine(exeFolder, "ue4ss", relative.Replace('/', Path.DirectorySeparatorChar));
        }

        return Path.Combine(exeFolder, fileName);
    }

    private static string? DetectVersion(string ue4ssRoot)
    {
        var versionFile = Path.Combine(ue4ssRoot, "UE4SS-version.txt");
        return File.Exists(versionFile) ? File.ReadAllText(versionFile).Trim() : null;
    }
}
