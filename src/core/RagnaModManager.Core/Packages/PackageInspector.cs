using System.IO.Compression;
using System.Text.Json;
using RagnaModManager.Core.Common;
using RagnaModManager.Core.Manifests;

namespace RagnaModManager.Core.Packages;

public sealed class PackageInspector
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Disallow,
        AllowTrailingCommas = false
    };

    public Result<PackageInspection> Inspect(string archivePath, bool developerMode = false)
    {
        if (!File.Exists(archivePath))
        {
            return Result<PackageInspection>.Fail($"Package does not exist: {archivePath}");
        }

        if (!archivePath.EndsWith(".rmod", StringComparison.OrdinalIgnoreCase) &&
            !archivePath.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
        {
            return Result<PackageInspection>.Fail("Package must be a .rmod zip archive.");
        }

        try
        {
            using var archive = ZipFile.OpenRead(archivePath);
            var entries = new List<PackageEntryInfo>();
            ZipArchiveEntry? manifestEntry = null;

            foreach (var entry in archive.Entries)
            {
                if (string.IsNullOrWhiteSpace(entry.FullName))
                {
                    continue;
                }

                var normalized = entry.FullName.Replace('\\', '/');
                if (!PathSafety.IsSafeRelativePath(normalized))
                {
                    return Result<PackageInspection>.Fail($"Package contains unsafe path: {entry.FullName}");
                }

                if (normalized.EndsWith('/'))
                {
                    continue;
                }

                entries.Add(new PackageEntryInfo(normalized, entry.CompressedLength, entry.Length));
                if (string.Equals(normalized, "manifest.json", StringComparison.Ordinal))
                {
                    manifestEntry = entry;
                }
            }

            if (manifestEntry is null)
            {
                return Result<PackageInspection>.Fail("Package is missing manifest.json.");
            }

            using var manifestStream = manifestEntry.Open();
            var manifest = JsonSerializer.Deserialize<ModManifest>(manifestStream, JsonOptions);
            if (manifest is null)
            {
                return Result<PackageInspection>.Fail("manifest.json is empty or malformed.");
            }

            var validation = ManifestValidator.Validate(manifest, developerMode);
            if (!validation.Success)
            {
                return Result<PackageInspection>.Fail(validation.Error!);
            }

            var entryPaths = entries.Select(e => e.Path).ToHashSet(StringComparer.OrdinalIgnoreCase);
            foreach (var file in manifest.Files)
            {
                var source = file.Source.TrimEnd('/', '\\').Replace('\\', '/');
                var hasSource = entryPaths.Contains(source) ||
                                entryPaths.Any(path => path.StartsWith(source + "/", StringComparison.OrdinalIgnoreCase));
                if (!hasSource)
                {
                    return Result<PackageInspection>.Fail($"Manifest source does not exist in package: {file.Source}");
                }
            }

            return Result<PackageInspection>.Ok(new PackageInspection(
                archivePath,
                manifest,
                entries.OrderBy(e => e.Path, StringComparer.OrdinalIgnoreCase).ToList(),
                entries.Sum(e => e.UncompressedBytes)));
        }
        catch (JsonException ex)
        {
            return Result<PackageInspection>.Fail($"manifest.json is not valid JSON: {ex.Message}");
        }
        catch (InvalidDataException ex)
        {
            return Result<PackageInspection>.Fail($"Package is not a readable zip archive: {ex.Message}");
        }
        catch (IOException ex)
        {
            return Result<PackageInspection>.Fail($"Could not inspect package: {ex.Message}");
        }
    }
}
