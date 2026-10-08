using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json;
using RagnaModManager.Core.Common;
using RagnaModManager.Core.Logging;
using RagnaModManager.Core.Platform;
using UAssetAPI;
using UAssetAPI.UnrealTypes;

namespace RagnaModManager.Core.Hammers;

public sealed class HammerManifest
{
    public int FormatVersion { get; set; } = 1;
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public string Author { get; set; } = "";
    public string Version { get; set; } = "1.0.0";
    public string Description { get; set; } = "";
    public string RowName { get; set; } = "";
    public string DisplayName { get; set; } = "";
    public string MeshAssetPath { get; set; } = "";
    public string? IconAssetPath { get; set; }
    public string DataAssetPath { get; set; } = "";
    public string? Thumbnail { get; set; }
    public List<HammerPackageFile> Assets { get; set; } = [];
}

public sealed class HammerPackageFile
{
    public string Source { get; set; } = "";
    public string PackagePath { get; set; } = "";
}

public sealed record HammerLibraryEntry(HammerManifest Manifest, string InstalledPath, bool Enabled);
public sealed record HammerLibraryStatus(IReadOnlyList<HammerLibraryEntry> Hammers, string GameBuild, string BuildStatus, string? GamePakPath);

/// <summary>Stores cooked, game-ready hammer packages imported as .rhammer ZIP archives.</summary>
public sealed class HammerLibraryService
{
    private const string ManifestFileName = "hammer.json";
    private const string IndexFileName = "library.json";
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        WriteIndented = true
    };

    private readonly AppPaths _paths;
    private readonly AppLogger _logger;
    private string? _cachedPakPath;
    private long _cachedPakLength = -1;
    private long _cachedPakWriteTicks = -1;
    private string? _cachedGameBuild;

    public HammerLibraryService(AppPaths paths, AppLogger logger)
    {
        _paths = paths;
        _logger = logger;
    }

    public IReadOnlyList<HammerLibraryEntry> GetEntries()
    {
        var enabled = ReadIndex();
        var entries = new List<HammerLibraryEntry>();
        foreach (var directory in Directory.EnumerateDirectories(_paths.HammerLibrary).OrderBy(Path.GetFileName, StringComparer.OrdinalIgnoreCase))
        {
            if (Path.GetFileName(directory).StartsWith(".", StringComparison.Ordinal)) continue;
            var manifestPath = Path.Combine(directory, ManifestFileName);
            if (!File.Exists(manifestPath)) continue;
            try
            {
                var manifest = JsonSerializer.Deserialize<HammerManifest>(File.ReadAllText(manifestPath), JsonOptions);
                if (manifest is not null)
                {
                    var validation = ValidateManifest(manifest, directory);
                    if (validation.Success)
                        entries.Add(new HammerLibraryEntry(manifest, directory, enabled.GetValueOrDefault(manifest.Id, true)));
                    else
                        _logger.Error($"Ignored invalid hammer package at '{directory}': {validation.Error}");
                }
            }
            catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException or InvalidDataException or InvalidOperationException or ArgumentException)
            {
                _logger.Error($"Could not read hammer package at '{directory}': {ex}");
            }
        }
        return entries;
    }

    public Result<HammerLibraryEntry> Import(string archivePath)
    {
        if (!File.Exists(archivePath)) return Result<HammerLibraryEntry>.Fail("The selected hammer package could not be found.");
        var staging = Path.Combine(_paths.HammerLibrary, ".import-" + Guid.NewGuid().ToString("N"));
        try
        {
            using (var archive = ZipFile.OpenRead(archivePath))
            {
                if (archive.Entries.Count > 512 || archive.Entries.Sum(entry => entry.Length) > 2L * 1024 * 1024 * 1024)
                    return Result<HammerLibraryEntry>.Fail("The hammer package is too large (maximum 512 files and 2 GiB uncompressed).");
                var paths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach (var entry in archive.Entries)
                {
                    var name = entry.FullName.Replace('\\', '/');
                    if (Path.IsPathRooted(name) || name.Contains(':') || name.Split('/').Any(part => part is ".." or ".") || !paths.Add(name))
                        return Result<HammerLibraryEntry>.Fail($"Unsafe path in hammer package: {entry.FullName}");
                }
                archive.ExtractToDirectory(staging);
            }

            var manifestPath = Path.Combine(staging, ManifestFileName);
            if (!File.Exists(manifestPath)) return Result<HammerLibraryEntry>.Fail("The package is missing hammer.json.");
            var manifest = JsonSerializer.Deserialize<HammerManifest>(File.ReadAllText(manifestPath), JsonOptions);
            if (manifest is null) return Result<HammerLibraryEntry>.Fail("hammer.json is empty or invalid.");
            var validation = ValidateManifest(manifest, staging);
            if (!validation.Success) return Result<HammerLibraryEntry>.Fail(validation.Error!);

            var entries = GetEntries();
            if (entries.Any(entry => entry.Manifest.Id.Equals(manifest.Id, StringComparison.OrdinalIgnoreCase)))
                return Result<HammerLibraryEntry>.Fail($"A hammer with package ID '{manifest.Id}' is already in the library.");
            var duplicateRow = entries.FirstOrDefault(entry => entry.Manifest.RowName.Equals(manifest.RowName, StringComparison.OrdinalIgnoreCase));
            if (duplicateRow is not null)
                return Result<HammerLibraryEntry>.Fail($"Row name '{manifest.RowName}' is already used by {duplicateRow.Manifest.Name}.");
            var duplicateAsset = FindPackagePathConflict(entries, manifest);
            if (duplicateAsset is not null)
                return Result<HammerLibraryEntry>.Fail($"Package path '{duplicateAsset}' is already used by another hammer.");

            var installedPath = Path.Combine(_paths.HammerLibrary, manifest.Id);
            Directory.Move(staging, installedPath);
            var index = ReadIndex();
            index[manifest.Id] = true;
            WriteIndex(index);
            return Result<HammerLibraryEntry>.Ok(new HammerLibraryEntry(manifest, installedPath, true));
        }
        catch (InvalidDataException ex)
        {
            _logger.Error($"Invalid hammer archive '{archivePath}': {ex}");
            return Result<HammerLibraryEntry>.Fail("This file isn't a valid hammer package.");
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException or InvalidOperationException or ArgumentException or NotSupportedException)
        {
            _logger.Error($"Could not import hammer archive '{archivePath}': {ex}");
            return Result<HammerLibraryEntry>.Fail("RMM couldn't add this hammer package. Check that the file is complete and try again.");
        }
        catch (Exception ex)
        {
            _logger.Error($"Unexpected hammer import failure for '{archivePath}': {ex}");
            return Result<HammerLibraryEntry>.Fail("RMM couldn't add this hammer package. Check the log for details.");
        }
        finally
        {
            try
            {
                if (Directory.Exists(staging)) Directory.Delete(staging, recursive: true);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                _logger.Error($"Could not clean up hammer import staging directory '{staging}': {ex}");
            }
        }
    }

    public Result SetEnabled(string id, bool enabled)
    {
        var entry = GetEntries().FirstOrDefault(item => item.Manifest.Id.Equals(id, StringComparison.OrdinalIgnoreCase));
        if (entry is null) return Result.Fail($"Hammer '{id}' is not installed.");
        var index = ReadIndex();
        index[id] = enabled;
        try
        {
            WriteIndex(index);
            return Result.Ok();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _logger.Error($"Could not update the enabled state for '{id}': {ex}");
            return Result.Fail("RMM couldn't update this hammer. Check the library folder permissions.");
        }
    }

    public Result Remove(string id)
    {
        var entry = GetEntries().FirstOrDefault(item => item.Manifest.Id.Equals(id, StringComparison.OrdinalIgnoreCase));
        if (entry is null) return Result.Fail($"Hammer '{id}' is not installed.");
        try
        {
            Directory.Delete(entry.InstalledPath, recursive: true);
            var index = ReadIndex();
            index.Remove(id);
            WriteIndex(index);
            return Result.Ok();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _logger.Error($"Could not remove hammer '{id}': {ex}");
            return Result.Fail("RMM couldn't remove this hammer. Check the library folder permissions.");
        }
    }

    public static string GetStableRowName(string id) => "RMM_" + id.Replace('-', '_').ToUpperInvariant();

    public static string GetDataAssetPath(string id) => "/Game/Data/Hammers/DA_" + GetStableRowName(id);

    public HammerLibraryStatus GetStatus(string? gameRoot)
    {
        if (string.IsNullOrWhiteSpace(gameRoot))
            return new HammerLibraryStatus(GetEntries(), "Game not configured", "Connect your Ragnarock folder to check hammer compatibility.", null);
        var pakPath = Path.Combine(gameRoot, "Ragnarock", "Content", "Paks", "Ragnarock-WindowsNoEditor.pak");
        if (!File.Exists(pakPath))
            return new HammerLibraryStatus(GetEntries(), "Unknown game build", "The installed game data PAK could not be found. Building is blocked to protect stock and DLC entries.", pakPath);
        var pakInfo = new FileInfo(pakPath);
        if (!string.Equals(_cachedPakPath, pakPath, StringComparison.OrdinalIgnoreCase) ||
            _cachedPakLength != pakInfo.Length || _cachedPakWriteTicks != pakInfo.LastWriteTimeUtc.Ticks)
        {
            using var stream = File.OpenRead(pakPath);
            _cachedGameBuild = Convert.ToHexString(SHA256.HashData(stream));
            _cachedPakPath = pakPath;
            _cachedPakLength = pakInfo.Length;
            _cachedPakWriteTicks = pakInfo.LastWriteTimeUtc.Ticks;
        }
        var entries = GetEntries();
        var enabledCount = entries.Count(entry => entry.Enabled);
        var metadataPath = Path.Combine(_paths.HammerMetadata, _cachedGameBuild!.ToLowerInvariant(), "metadata.json");
        var buildStatus = enabledCount == 0
            ? "Enable at least one hammer to add it to Ragnarock."
            : File.Exists(metadataPath)
                ? "Matching game data is ready. RMM will check it again before building."
                : "RMM will check for matching game data before building. New game updates may need a compatibility update first.";
        return new HammerLibraryStatus(entries, "Ragnarock detected", buildStatus, pakPath);
    }

    private Result ValidateManifest(HammerManifest manifest, string root)
    {
        if (manifest.FormatVersion != 1) return Result.Fail($"Unsupported hammer package format version {manifest.FormatVersion}.");
        if (!IsIdentifier(manifest.Id)) return Result.Fail("Package ID must contain only lowercase letters, digits, and hyphens, and be 60 characters or fewer.");
        if (string.IsNullOrWhiteSpace(manifest.Name) || string.IsNullOrWhiteSpace(manifest.Author)) return Result.Fail("Package name and author are required.");
        if (string.IsNullOrWhiteSpace(manifest.RowName) || manifest.RowName.Length > 64 || !manifest.RowName.All(c => char.IsAsciiLetterOrDigit(c) || c == '_'))
            return Result.Fail($"'{manifest.RowName}' is not a valid DataTable row name. Use 1–64 ASCII letters, digits, or underscores.");
        if (!manifest.RowName.Equals(GetStableRowName(manifest.Id), StringComparison.Ordinal))
            return Result.Fail($"{manifest.Name} must use stable row name '{GetStableRowName(manifest.Id)}'.");
        if (!manifest.DataAssetPath.Equals(GetDataAssetPath(manifest.Id), StringComparison.Ordinal))
            return Result.Fail($"{manifest.Name} must place its hammer data asset at '{GetDataAssetPath(manifest.Id)}'.");
        if (manifest.Assets is null || manifest.Assets.Count == 0) return Result.Fail($"{manifest.Name} contains no model files.");
        if (!IsGameAssetPath(manifest.MeshAssetPath) || !IsGameAssetPath(manifest.DataAssetPath))
            return Result.Fail($"{manifest.Name} must use valid /Game/... mesh and data asset paths.");
        if (manifest.IconAssetPath is not null && !IsGameAssetPath(manifest.IconAssetPath))
            return Result.Fail($"{manifest.Name} has an invalid /Game/... icon asset path.");

        var packageFiles = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var bySource = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var asset in manifest.Assets)
        {
            if (!IsSafeRelativePath(asset.Source) || !bySource.Add(asset.Source))
                return Result.Fail($"{manifest.Name} has an unsafe or duplicate source path '{asset.Source}'.");
            if (!IsGameAssetPath(asset.PackagePath))
                return Result.Fail($"{manifest.Name} has an invalid Unreal package path '{asset.PackagePath}'.");
            var extension = Path.GetExtension(asset.Source).ToLowerInvariant();
            if (extension is not ".uasset" and not ".uexp" and not ".ubulk" and not ".uptnl")
                return Result.Fail($"{manifest.Name} has an unsupported cooked asset file '{asset.Source}'.");
            if (!packageFiles.Add(asset.PackagePath + extension))
                return Result.Fail($"{manifest.Name} includes more than one '{extension}' file for '{asset.PackagePath}'.");
            var sourcePath = Path.GetFullPath(Path.Combine(root, asset.Source.Replace('/', Path.DirectorySeparatorChar)));
            var fullRoot = Path.GetFullPath(root) + Path.DirectorySeparatorChar;
            if (!sourcePath.StartsWith(fullRoot, StringComparison.Ordinal) || !File.Exists(sourcePath))
                return Result.Fail($"{manifest.Name} is missing cooked asset '{asset.Source}'.");
        }

        if (!packageFiles.Contains(manifest.MeshAssetPath + ".uasset")) return Result.Fail($"{manifest.Name} does not include its mesh asset '{manifest.MeshAssetPath}'.");
        if (manifest.IconAssetPath is not null && !packageFiles.Contains(manifest.IconAssetPath + ".uasset"))
            return Result.Fail($"{manifest.Name} does not include its icon asset '{manifest.IconAssetPath}'.");
        try
        {
            var meshSource = manifest.Assets.Single(asset => asset.PackagePath.Equals(manifest.MeshAssetPath, StringComparison.OrdinalIgnoreCase) && Path.GetExtension(asset.Source).Equals(".uasset", StringComparison.OrdinalIgnoreCase));
            var meshAsset = new UAsset(Path.Combine(root, meshSource.Source.Replace('/', Path.DirectorySeparatorChar)), EngineVersion.VER_UE4_27);
            if (!meshAsset.Exports.Any(export => export.ClassIndex.Index < 0 &&
                -export.ClassIndex.Index <= meshAsset.Imports.Count &&
                meshAsset.Imports[-export.ClassIndex.Index - 1].ObjectName.ToString().Equals("StaticMesh", StringComparison.OrdinalIgnoreCase)))
                return Result.Fail($"{manifest.Name}'s mesh package contains no cooked StaticMesh export.");
            if (manifest.IconAssetPath is not null)
            {
                var iconSource = manifest.Assets.Single(asset => asset.PackagePath.Equals(manifest.IconAssetPath, StringComparison.OrdinalIgnoreCase) &&
                    Path.GetExtension(asset.Source).Equals(".uasset", StringComparison.OrdinalIgnoreCase));
                var iconAsset = new UAsset(Path.Combine(root, iconSource.Source.Replace('/', Path.DirectorySeparatorChar)), EngineVersion.VER_UE4_27);
                if (!iconAsset.Exports.Any(export => export.ClassIndex.Index < 0 &&
                    -export.ClassIndex.Index <= iconAsset.Imports.Count &&
                    iconAsset.Imports[-export.ClassIndex.Index - 1].ObjectName.ToString().Equals("Texture2D", StringComparison.OrdinalIgnoreCase)))
                    return Result.Fail($"{manifest.Name}'s icon package contains no cooked Texture2D export.");
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException or ArgumentException or NotSupportedException or InvalidDataException or NullReferenceException)
        {
            _logger.Error($"Could not validate cooked model data for '{manifest.Name}': {ex}");
            return Result.Fail($"{manifest.Name}'s cooked assets could not be read as UE4.27 packages: {ex.Message}");
        }
        catch (Exception ex)
        {
            _logger.Error($"Unexpected model validation failure for '{manifest.Name}': {ex}");
            return Result.Fail($"{manifest.Name}'s model data could not be checked.");
        }
        if (string.IsNullOrWhiteSpace(manifest.DisplayName)) return Result.Fail($"{manifest.Name} is missing its in-game display name.");
        if (manifest.Thumbnail is not null)
        {
            if (!IsSafeRelativePath(manifest.Thumbnail) || Path.GetExtension(manifest.Thumbnail).ToLowerInvariant() is not ".png" and not ".jpg" and not ".jpeg" and not ".webp")
                return Result.Fail($"{manifest.Name} has an invalid thumbnail. Use a PNG, JPG, or WebP image inside the package.");
            var thumbnailPath = Path.GetFullPath(Path.Combine(root, manifest.Thumbnail.Replace('/', Path.DirectorySeparatorChar)));
            var fullRoot = Path.GetFullPath(root) + Path.DirectorySeparatorChar;
            if (!thumbnailPath.StartsWith(fullRoot, StringComparison.Ordinal) || !File.Exists(thumbnailPath))
                return Result.Fail($"{manifest.Name}'s preview image is missing.");
        }
        return Result.Ok();
    }

    private static string? FindPackagePathConflict(IEnumerable<HammerLibraryEntry> entries, HammerManifest manifest)
    {
        var paths = entries.SelectMany(entry => entry.Manifest.Assets.Select(asset => asset.PackagePath)).ToHashSet(StringComparer.OrdinalIgnoreCase);
        return manifest.Assets.Select(asset => asset.PackagePath).FirstOrDefault(paths.Contains);
    }

    private Dictionary<string, bool> ReadIndex()
    {
        var path = Path.Combine(_paths.HammerLibrary, IndexFileName);
        if (!File.Exists(path)) return new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);
        try { return JsonSerializer.Deserialize<Dictionary<string, bool>>(File.ReadAllText(path)) ?? new(StringComparer.OrdinalIgnoreCase); }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
        {
            _logger.Error($"Could not read hammer library settings: {ex}");
            return new(StringComparer.OrdinalIgnoreCase);
        }
    }

    private void WriteIndex(Dictionary<string, bool> index) => File.WriteAllText(
        Path.Combine(_paths.HammerLibrary, IndexFileName), JsonSerializer.Serialize(index, new JsonSerializerOptions { WriteIndented = true }));

    private static bool IsIdentifier(string? value) => value is { Length: > 0 and <= 60 } &&
        value.All(c => c is >= 'a' and <= 'z' or >= '0' and <= '9' or '-');

    private static bool IsSafeRelativePath(string value) => !string.IsNullOrWhiteSpace(value) && !Path.IsPathRooted(value) &&
        value.Replace('\\', '/').Split('/').All(part => part.Length > 0 && part is not "." and not "..") && !value.Contains(':');

    private static bool IsGameAssetPath(string? value) => value is not null && value.StartsWith("/Game/", StringComparison.Ordinal) &&
        !value.Contains("..", StringComparison.Ordinal) && !value.EndsWith('/');
}
