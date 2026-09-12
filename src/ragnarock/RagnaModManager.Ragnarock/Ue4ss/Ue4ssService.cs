using System.IO.Compression;
using RagnaModManager.Core.Common;

namespace RagnaModManager.Ragnarock.Ue4ss;

public sealed record Ue4ssStatus(
    bool Installed,
    string Layout,
    string? Version,
    IReadOnlyList<string> Diagnostics,
    string RootPath,
    string ModsPath);

public sealed class Ue4ssService
{
    // The current UE4SS layout is the preferred layout on Windows. Wine/Proton
    // loads the proxy DLL, but the proxy cannot reliably resolve UE4SS.dll from
    // the sibling ue4ss directory, so Linux installs must use the legacy
    // same-directory layout.
    private static bool UseProtonCompatibleLayout => OperatingSystem.IsLinux();

    public Ue4ssStatus Detect(string gameRoot)
    {
        var exeFolder = DeploymentRules.RagnarockDeploymentRules.GetExecutableFolder(gameRoot);
        var diagnostics = new List<string>();
        var modernRoot = Path.Combine(exeFolder, "ue4ss");
        var modernDll = Path.Combine(modernRoot, "UE4SS.dll");
        var legacyDll = Path.Combine(exeFolder, "UE4SS.dll");
        var proxyDll = Directory.Exists(exeFolder)
            ? Directory.EnumerateFiles(exeFolder, "*.dll").FirstOrDefault(p => !Path.GetFileName(p).Equals("UE4SS.dll", StringComparison.OrdinalIgnoreCase))
            : null;

        if (File.Exists(legacyDll))
        {
            if (File.Exists(modernDll))
            {
                diagnostics.Add("Both root and ue4ss-subfolder UE4SS layouts are present; using the proxy-loaded root layout.");
            }
            var modsPath = ResolveModsPath(exeFolder);
            if (!Directory.Exists(modsPath)) diagnostics.Add($"UE4SS Mods folder is missing: {modsPath}");
            return new Ue4ssStatus(true, "legacy-exe-folder", DetectVersion(exeFolder), diagnostics, exeFolder, modsPath);
        }

        if (File.Exists(modernDll))
        {
            var modsPath = ResolveModsPath(modernRoot);
            if (!Directory.Exists(modsPath))
            {
                diagnostics.Add($"UE4SS Mods folder is missing: {modsPath}");
            }

            return new Ue4ssStatus(true, "modern-ue4ss-subfolder", DetectVersion(modernRoot), diagnostics, modernRoot, modsPath);
        }

        diagnostics.Add("UE4SS.dll was not found under Ragnarock/Binaries/Win64/ue4ss.");
        if (proxyDll is null)
        {
            diagnostics.Add("No proxy DLL was found next to the game executable.");
        }

        return new Ue4ssStatus(false, "missing", null, diagnostics, modernRoot, Path.Combine(modernRoot, "Mods"));
    }

    public Result InstallFromZip(string gameRoot, string zipPath, string? installedVersion = null)
    {
        if (!File.Exists(zipPath))
        {
            return Result.Fail($"UE4SS zip does not exist: {zipPath}");
        }

        var exeFolder = DeploymentRules.RagnarockDeploymentRules.GetExecutableFolder(gameRoot);
        var installRoot = UseProtonCompatibleLayout ? exeFolder : Path.Combine(exeFolder, "ue4ss");
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

                var target = MapUe4ssZipEntry(exeFolder, installRoot, normalized);
                Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                entry.ExtractToFile(target, overwrite: true);
            }

            Directory.CreateDirectory(Path.Combine(installRoot, "Mods"));
            if (!string.IsNullOrWhiteSpace(installedVersion))
            {
                File.WriteAllText(Path.Combine(installRoot, "UE4SS-version.txt"), installedVersion.Trim());
            }

            return Result.Ok();
        }
        catch (InvalidDataException ex)
        {
            return Result.Fail($"UE4SS zip is invalid: {ex.Message}");
        }
    }

    private static string MapUe4ssZipEntry(string exeFolder, string installRoot, string entry)
    {
        var fileName = Path.GetFileName(entry);
        if (fileName.Equals("UE4SS.dll", StringComparison.OrdinalIgnoreCase) ||
            fileName.Equals("UE4SS-settings.ini", StringComparison.OrdinalIgnoreCase))
        {
            return Path.Combine(installRoot, fileName);
        }

        if (entry.StartsWith("Mods/", StringComparison.OrdinalIgnoreCase) ||
            entry.StartsWith("ue4ss/Mods/", StringComparison.OrdinalIgnoreCase))
        {
            var relative = entry.StartsWith("ue4ss/", StringComparison.OrdinalIgnoreCase) ? entry["ue4ss/".Length..] : entry;
            return Path.Combine(installRoot, relative.Replace('/', Path.DirectorySeparatorChar));
        }

        return Path.Combine(exeFolder, fileName);
    }

    private static string? DetectVersion(string ue4ssRoot)
    {
        var versionFile = Path.Combine(ue4ssRoot, "UE4SS-version.txt");
        return File.Exists(versionFile) ? File.ReadAllText(versionFile).Trim() : null;
    }

    private static string ResolveModsPath(string ue4ssRoot)
    {
        var settings = Path.Combine(ue4ssRoot, "UE4SS-settings.ini");
        if (File.Exists(settings))
        {
            foreach (var line in File.ReadLines(settings))
            {
                var trimmed = line.Trim();
                if (!trimmed.StartsWith("ModsFolderPath", StringComparison.OrdinalIgnoreCase)) continue;
                var separator = trimmed.IndexOf('=');
                if (separator < 0) continue;
                var configured = trimmed[(separator + 1)..].Trim().Trim('"');
                if (configured.Length == 0) break;
                return Path.GetFullPath(Path.IsPathRooted(configured) ? configured : Path.Combine(ue4ssRoot, configured));
            }
        }

        return Path.Combine(ue4ssRoot, "Mods");
    }
}
