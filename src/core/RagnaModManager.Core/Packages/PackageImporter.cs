using System.IO.Compression;
using RagnaModManager.Core.Common;
using RagnaModManager.Core.Database;
using RagnaModManager.Core.Logging;
using RagnaModManager.Core.Manifests;
using RagnaModManager.Core.Platform;

namespace RagnaModManager.Core.Packages;

public sealed class PackageImporter
{
    private readonly AppPaths _paths;
    private readonly ManagerDatabase _database;
    private readonly AppLogger _logger;

    public PackageImporter(AppPaths paths, ManagerDatabase database, AppLogger logger)
    {
        _paths = paths;
        _database = database;
        _logger = logger;
    }

    public Result<ModManifest> Import(string archivePath, bool developerMode = false)
    {
        var inspection = new PackageInspector().Inspect(archivePath, developerMode);
        if (!inspection.Success)
        {
            return Result<ModManifest>.Fail(inspection.Error!);
        }

        var staging = Path.Combine(_paths.Root, "staging", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(staging);

        try
        {
            using (var archive = ZipFile.OpenRead(archivePath))
            {
                if (!archive.Entries.Any(e => string.Equals(e.FullName.Replace('\\', '/'), "manifest.json", StringComparison.Ordinal)))
                {
                    return Result<ModManifest>.Fail("Package is missing manifest.json.");
                }

                foreach (var entry in archive.Entries)
                {
                    if (string.IsNullOrWhiteSpace(entry.FullName))
                    {
                        continue;
                    }

                    var normalized = entry.FullName.Replace('\\', '/');
                    if (!PathSafety.IsSafeRelativePath(normalized))
                    {
                        return Result<ModManifest>.Fail($"Package contains unsafe path: {entry.FullName}");
                    }

                    if (normalized.EndsWith('/'))
                    {
                        continue;
                    }

                    var target = PathSafety.CombineUnderRoot(staging, normalized);
                    Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                    entry.ExtractToFile(target);
                }
            }

            var manifest = inspection.Value!.Manifest;
            foreach (var file in manifest.Files)
            {
                var source = PathSafety.CombineUnderRoot(staging, file.Source);
                if (!File.Exists(source) && !Directory.Exists(source))
                {
                    return Result<ModManifest>.Fail($"Manifest source does not exist in package: {file.Source}");
                }
            }

            var installRoot = Path.Combine(_paths.ModLibrary, manifest.Id);
            if (Directory.Exists(installRoot))
            {
                Directory.Delete(installRoot, recursive: true);
            }

            Directory.Move(staging, installRoot);
            var installedManifestPath = Path.Combine(installRoot, "manifest.json");
            _database.UpsertMod(manifest, installRoot, installedManifestPath, archivePath);
            _database.AddModToDefaultProfile(manifest.Id, enabled: false, priority: 0);
            _logger.Info($"Imported mod {manifest.Id} {manifest.Version} from {archivePath}");
            return Result<ModManifest>.Ok(manifest);
        }
        catch (InvalidDataException ex)
        {
            return Result<ModManifest>.Fail($"Package is not a readable zip archive: {ex.Message}");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            return Result<ModManifest>.Fail($"Could not import package: {ex.Message}");
        }
        finally
        {
            if (Directory.Exists(staging))
            {
                Directory.Delete(staging, recursive: true);
            }
        }
    }
}
