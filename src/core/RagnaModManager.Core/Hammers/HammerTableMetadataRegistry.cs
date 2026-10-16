using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json;
using RagnaModManager.Core.Common;
using RagnaModManager.Core.Logging;
using RagnaModManager.Core.Platform;

namespace RagnaModManager.Core.Hammers;

public sealed class HammerTableMetadataManifest
{
    public int FormatVersion { get; set; }
    public string GamePakSha256 { get; set; } = "";
    public string EngineVersion { get; set; } = "";
    public int PakVersion { get; set; }
    public string TablePath { get; set; } = "Ragnarock/Content/Data/Hammers/DT_Hammers.uasset";
    public string TableSha256 { get; set; } = "";
    public string ExportSha256 { get; set; } = "";
    public string DataAssetTemplatePath { get; set; } = "";
    public string DataAssetTemplateSha256 { get; set; } = "";
    public string DataAssetTemplateExportSha256 { get; set; } = "";
    public List<string> ExistingAssetPackagePaths { get; set; } = [];
}

public sealed record HammerMetadataSelection(
    string DirectoryPath,
    string InstalledGamePakSha256,
    string MetadataGamePakSha256,
    DateTimeOffset PublishedAt,
    bool IsExactBuild);

public sealed class HammerTableMetadataRegistry
{
    private const string IndexUrl = "https://raw.githubusercontent.com/Brollyy/RagnaModManager-AssetMeta/master/index.json";
    private const long MaxArchiveBytes = 256L * 1024 * 1024;
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };
    private readonly AppPaths _paths;
    private readonly AppLogger _logger;
    private readonly HttpClient _httpClient = new() { Timeout = TimeSpan.FromSeconds(30) };

    public HammerTableMetadataRegistry(AppPaths paths, AppLogger logger)
    {
        _paths = paths;
        _logger = logger;
    }

    public string GetInstalledBuildHash(string gamePakPath)
    {
        using var stream = File.OpenRead(gamePakPath);
        return Convert.ToHexString(SHA256.HashData(stream));
    }

    public string GetCachedMetadataPath(string gamePakSha256) => Path.Combine(_paths.HammerMetadata, gamePakSha256.ToLowerInvariant());

    public async Task<Result<HammerMetadataSelection>> EnsureAvailableAsync(string gamePakPath, CancellationToken cancellationToken = default)
    {
        try
        {
            var installedPakSha256 = GetInstalledBuildHash(gamePakPath);
            var exactCacheDirectory = GetCachedMetadataPath(installedPakSha256);
            if (TryValidateCache(exactCacheDirectory, installedPakSha256, out _))
                return Result<HammerMetadataSelection>.Ok(new(
                    exactCacheDirectory, installedPakSha256, installedPakSha256, DateTimeOffset.MinValue, true));

            var indexCachePath = Path.Combine(_paths.HammerMetadata, "index.json");
            List<HammerMetadataIndexEntry>? orderedBuilds = null;
            try
            {
                using var indexResponse = await _httpClient.GetAsync(IndexUrl, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
                if (indexResponse.IsSuccessStatusCode)
                {
                    var downloadedIndex = await ReadBoundedAsync(indexResponse.Content, 2 * 1024 * 1024, cancellationToken);
                    if (downloadedIndex.Length <= 2 * 1024 * 1024 && TryReadIndex(downloadedIndex, out orderedBuilds))
                        CacheIndex(indexCachePath, downloadedIndex);
                    else
                        _logger.Warning("The downloaded hammer compatibility list is invalid; trying the cached list.");
                }
                else
                {
                    _logger.Warning($"Could not refresh the hammer compatibility list (HTTP {(int)indexResponse.StatusCode}); trying the cached list.");
                }
            }
            catch (HttpRequestException ex)
            {
                _logger.Warning($"Could not refresh the hammer compatibility list: {ex.Message}; trying the cached list.");
            }
            catch (InvalidDataException ex)
            {
                _logger.Warning($"The downloaded hammer compatibility list could not be read: {ex.Message}; trying the cached list.");
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                _logger.Warning("The hammer compatibility list request timed out; trying the cached list.");
            }

            if (orderedBuilds is null && TryReadCachedIndex(indexCachePath, out var cachedBuilds))
            {
                orderedBuilds = cachedBuilds;
                _logger.Info("Using the cached hammer compatibility list.");
            }
            if (orderedBuilds is null)
                return Result<HammerMetadataSelection>.Fail("RMM couldn't check hammer compatibility right now. Try again later.");

            var exactBuild = orderedBuilds.FirstOrDefault(build =>
                string.Equals(build.GamePakSha256, installedPakSha256, StringComparison.OrdinalIgnoreCase));
            var entry = exactBuild ?? orderedBuilds[0];
            var isExactBuild = exactBuild is not null;
            var metadataPakSha256 = entry.GamePakSha256.ToUpperInvariant();
            var cacheDirectory = GetCachedMetadataPath(metadataPakSha256);
            if (TryValidateCache(cacheDirectory, metadataPakSha256, out _))
                return Result<HammerMetadataSelection>.Ok(new(
                    cacheDirectory, installedPakSha256, metadataPakSha256, entry.PublishedAt, isExactBuild));
            if (!Uri.TryCreate(entry.Url, UriKind.Absolute, out var archiveUri) || archiveUri.Scheme != Uri.UriSchemeHttps)
                return Result<HammerMetadataSelection>.Fail("The hammer compatibility list contains an invalid download link.");

            using var archiveResponse = await _httpClient.GetAsync(archiveUri, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
            if (!archiveResponse.IsSuccessStatusCode || archiveResponse.Content.Headers.ContentLength > MaxArchiveBytes)
                return Result<HammerMetadataSelection>.Fail("RMM couldn't download the latest hammer compatibility data.");
            var archiveBytes = await ReadBoundedAsync(archiveResponse.Content, MaxArchiveBytes, cancellationToken);
            if (archiveBytes.LongLength > MaxArchiveBytes || !FixedTimeHashEquals(entry.Sha256, SHA256.HashData(archiveBytes)))
                return Result<HammerMetadataSelection>.Fail("The downloaded hammer compatibility data failed its integrity check.");

            var staging = Path.Combine(_paths.HammerMetadata, ".metadata-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(staging);
            try
            {
                using var memory = new MemoryStream(archiveBytes, writable: false);
                using var archive = new ZipArchive(memory, ZipArchiveMode.Read);
                if (archive.Entries.Count > 8 || archive.Entries.Sum(item => item.Length) > MaxArchiveBytes)
                    return Result<HammerMetadataSelection>.Fail("The downloaded hammer compatibility data is too large.");
                foreach (var item in archive.Entries)
                {
                    var path = item.FullName.Replace('\\', '/');
                    if (Path.IsPathRooted(path) || path.Contains(':') || path.Split('/').Any(part => part is ".." or "."))
                        return Result<HammerMetadataSelection>.Fail("The downloaded hammer compatibility data is invalid.");
                    var target = Path.GetFullPath(Path.Combine(staging, path.Replace('/', Path.DirectorySeparatorChar)));
                    if (!target.StartsWith(Path.GetFullPath(staging) + Path.DirectorySeparatorChar, StringComparison.Ordinal))
                        return Result<HammerMetadataSelection>.Fail("The downloaded hammer compatibility data is invalid.");
                    Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                    item.ExtractToFile(target);
                }
                if (!TryValidateCache(staging, metadataPakSha256, out _))
                    return Result<HammerMetadataSelection>.Fail("The downloaded hammer compatibility data doesn't match its published game build.");
                if (Directory.Exists(cacheDirectory)) Directory.Delete(cacheDirectory, recursive: true);
                Directory.Move(staging, cacheDirectory);
            }
            finally
            {
                if (Directory.Exists(staging)) Directory.Delete(staging, recursive: true);
            }

            if (!TryValidateCache(cacheDirectory, metadataPakSha256, out _))
                return Result<HammerMetadataSelection>.Fail("The latest hammer compatibility data couldn't be installed.");
            return Result<HammerMetadataSelection>.Ok(new(
                cacheDirectory, installedPakSha256, metadataPakSha256, entry.PublishedAt, isExactBuild));
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or IOException or UnauthorizedAccessException or InvalidDataException or JsonException or ArgumentException or InvalidOperationException)
        {
            _logger.Error($"Could not retrieve hammer compatibility metadata: {ex}");
            return Result<HammerMetadataSelection>.Fail("RMM couldn't retrieve hammer compatibility data. Check your connection and try again.");
        }
    }

    private static bool TryValidateCache(string directory, string expectedPakSha256, out string tablePath)
    {
        tablePath = "";
        var manifestPath = Path.Combine(directory, "metadata.json");
        if (!File.Exists(manifestPath)) return false;
        try
        {
            var metadata = JsonSerializer.Deserialize<HammerTableMetadataManifest>(File.ReadAllText(manifestPath), JsonOptions);
            if (metadata is null || metadata.FormatVersion != 1 || metadata.PakVersion != 11 ||
                !string.Equals(metadata.EngineVersion, "UE4.27", StringComparison.OrdinalIgnoreCase) ||
                !string.Equals(metadata.GamePakSha256, expectedPakSha256, StringComparison.OrdinalIgnoreCase) ||
                metadata.ExistingAssetPackagePaths is null || metadata.ExistingAssetPackagePaths.Count == 0 ||
                !string.Equals(metadata.TablePath, "Ragnarock/Content/Data/Hammers/DT_Hammers.uasset", StringComparison.Ordinal) ||
                string.IsNullOrWhiteSpace(metadata.DataAssetTemplatePath) ||
                Path.IsPathRooted(metadata.DataAssetTemplatePath) ||
                !metadata.DataAssetTemplatePath.StartsWith("Ragnarock/Content/Data/Hammers/", StringComparison.Ordinal) ||
                metadata.DataAssetTemplatePath.Contains("..", StringComparison.Ordinal) ||
                !metadata.DataAssetTemplatePath.EndsWith(".uasset", StringComparison.OrdinalIgnoreCase)) return false;
            tablePath = Path.Combine(directory, metadata.TablePath.Replace('/', Path.DirectorySeparatorChar));
            var exportPath = Path.ChangeExtension(tablePath, ".uexp");
            var templatePath = Path.Combine(directory, metadata.DataAssetTemplatePath.Replace('/', Path.DirectorySeparatorChar));
            var templateExportPath = Path.ChangeExtension(templatePath, ".uexp");
            return File.Exists(tablePath) && File.Exists(exportPath) && File.Exists(templatePath) && File.Exists(templateExportPath) &&
                   FileHashEquals(tablePath, metadata.TableSha256) && FileHashEquals(exportPath, metadata.ExportSha256) &&
                   FileHashEquals(templatePath, metadata.DataAssetTemplateSha256) && FileHashEquals(templateExportPath, metadata.DataAssetTemplateExportSha256);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException or ArgumentException or NotSupportedException or NullReferenceException)
        {
            return false;
        }
    }

    private static bool TryReadCachedIndex(string path, out List<HammerMetadataIndexEntry> orderedBuilds)
    {
        orderedBuilds = [];
        try
        {
            var info = new FileInfo(path);
            if (!info.Exists || info.Length > 2 * 1024 * 1024) return false;
            return TryReadIndex(File.ReadAllBytes(path), out orderedBuilds);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException or ArgumentException)
        {
            return false;
        }
    }

    private static bool TryReadIndex(byte[] bytes, out List<HammerMetadataIndexEntry> orderedBuilds)
    {
        orderedBuilds = [];
        try
        {
            var index = JsonSerializer.Deserialize<HammerMetadataIndex>(bytes, JsonOptions);
            if (index?.SchemaVersion != 1 || index.Assets is null ||
                !index.Assets.TryGetValue("hammers", out var builds) || builds is null || builds.Count == 0)
                return false;

            var entries = builds.OrderByDescending(build => build.PublishedAt).ToList();
            if (entries.Any(build => !IsSha256(build.GamePakSha256) || !IsSha256(build.Sha256) ||
                    build.PublishedAt == DateTimeOffset.MinValue ||
                    !Uri.TryCreate(build.Url, UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttps))
                return false;
            orderedBuilds = entries;
            return true;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private void CacheIndex(string path, byte[] bytes)
    {
        var temporaryPath = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllBytes(temporaryPath, bytes);
            File.Move(temporaryPath, path, overwrite: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _logger.Warning($"Could not save the hammer compatibility list for offline use: {ex.Message}");
        }
        finally
        {
            try
            {
                if (File.Exists(temporaryPath)) File.Delete(temporaryPath);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                _logger.Warning($"Could not remove a temporary hammer compatibility list: {ex.Message}");
            }
        }
    }

    private static async Task<byte[]> ReadBoundedAsync(HttpContent content, long maxBytes, CancellationToken cancellationToken)
    {
        await using var source = await content.ReadAsStreamAsync(cancellationToken);
        using var buffer = new MemoryStream();
        var chunk = new byte[64 * 1024];
        while (true)
        {
            var count = await source.ReadAsync(chunk, cancellationToken);
            if (count == 0) return buffer.ToArray();
            if (buffer.Length + count > maxBytes) throw new InvalidDataException("Metadata download exceeds its size limit.");
            buffer.Write(chunk, 0, count);
        }
    }

    private static bool FileHashEquals(string path, string expected)
    {
        using var stream = File.OpenRead(path);
        return FixedTimeHashEquals(expected, SHA256.HashData(stream));
    }

    private static bool FixedTimeHashEquals(string expected, byte[] actual)
    {
        try { return CryptographicOperations.FixedTimeEquals(Convert.FromHexString(expected), actual); }
        catch (FormatException) { return false; }
    }

    private static bool IsSha256(string? value) => value is { Length: 64 } && value.All(Uri.IsHexDigit);

    private sealed class HammerMetadataIndex
    {
        public int SchemaVersion { get; set; }
        public Dictionary<string, List<HammerMetadataIndexEntry>> Assets { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    }

    private sealed class HammerMetadataIndexEntry
    {
        public string GamePakSha256 { get; set; } = "";
        public DateTimeOffset PublishedAt { get; set; }
        public string Url { get; set; } = "";
        public string Sha256 { get; set; } = "";
    }
}
