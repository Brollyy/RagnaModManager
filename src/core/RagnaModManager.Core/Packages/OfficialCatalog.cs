using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text.Json;
using RagnaModManager.Core.Common;
using RagnaModManager.Core.Database;
using RagnaModManager.Core.Logging;
using RagnaModManager.Core.Manifests;
using RagnaModManager.Core.Platform;

namespace RagnaModManager.Core.Packages;

public sealed record OfficialCatalog(string SchemaVersion, string Repository, IReadOnlyList<CatalogMod> Mods);
public sealed record CatalogMod(string Id, string Name, string? Author, string? Description, IReadOnlyList<CatalogRelease> Releases)
{
    public CatalogRelease? Latest => Releases.OrderByDescending(r => Version.TryParse(r.Version.TrimStart('v', 'V'), out var version) ? version : new Version(0, 0)).FirstOrDefault();
}
public sealed record CatalogRelease(string Version, string PackageUrl, string Sha256, string? PublishedAt = null);

public sealed class OfficialCatalogService
{
    public const string DefaultIndexUrl = "https://raw.githubusercontent.com/Brollyy/RagnaModManager-ModRegistry/main/index.json";
    private const long MaxPackageBytes = 250L * 1024 * 1024;
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };
    private readonly AppPaths _paths;
    private readonly PackageImporter _importer;
    private readonly HttpClient _http;
    private readonly AppLogger _logger;

    public OfficialCatalogService(AppPaths paths, ManagerDatabase database, AppLogger logger, HttpClient? http = null)
    {
        _paths = paths;
        _importer = new PackageImporter(paths, database, logger);
        _http = http ?? new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
        _http.DefaultRequestHeaders.UserAgent.ParseAdd("RagnaModManager/0.1");
        _logger = logger;
    }

    public string IndexUrl => Environment.GetEnvironmentVariable("RMM_OFFICIAL_REGISTRY_URL") ?? DefaultIndexUrl;

    public async Task<Result<OfficialCatalog>> LoadAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            var catalog = await _http.GetFromJsonAsync<OfficialCatalog>(IndexUrl, JsonOptions, cancellationToken);
            if (catalog is null || catalog.Mods is null)
                return Result<OfficialCatalog>.Fail("The official mod registry is empty or malformed.");
            if (!string.Equals(catalog.SchemaVersion, "1", StringComparison.Ordinal))
                return Result<OfficialCatalog>.Fail($"Unsupported official registry schema '{catalog.SchemaVersion}'.");
            if (!string.Equals(catalog.Repository, "official", StringComparison.OrdinalIgnoreCase))
                return Result<OfficialCatalog>.Fail("The downloaded registry is not the official registry.");
            return Result<OfficialCatalog>.Ok(catalog with { Mods = catalog.Mods.Where(IsValidMod).ToList() });
        }
        catch (Exception ex) when (ex is HttpRequestException or JsonException or TaskCanceledException)
        {
            return Result<OfficialCatalog>.Fail($"Could not load the official mod registry: {ex.Message}");
        }
    }

    public async Task<Result<ModManifest>> DownloadAndImportAsync(CatalogMod catalogMod, CatalogRelease release, CancellationToken cancellationToken = default)
    {
        if (!Uri.TryCreate(release.PackageUrl, UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttps)
            return Result<ModManifest>.Fail("Official packages must use HTTPS URLs.");
        if (string.IsNullOrWhiteSpace(release.Sha256) || release.Sha256.Length != 64)
            return Result<ModManifest>.Fail("The registry release is missing a valid SHA-256 checksum.");

        var cache = Path.Combine(_paths.Downloads, "official", release.Sha256.ToLowerInvariant() + ".rmod");
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(cache)!);
            if (!File.Exists(cache) || !ChecksumMatches(cache, release.Sha256))
            {
                using var response = await _http.GetAsync(uri, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
                response.EnsureSuccessStatusCode();
                if (response.Content.Headers.ContentLength > MaxPackageBytes)
                    return Result<ModManifest>.Fail("The package exceeds the 250 MB safety limit.");
                await using (var input = await response.Content.ReadAsStreamAsync(cancellationToken))
                await using (var output = File.Create(cache))
                {
                    var buffer = new byte[81920];
                    long total = 0;
                    int read;
                    while ((read = await input.ReadAsync(buffer, cancellationToken)) > 0)
                    {
                        total += read;
                        if (total > MaxPackageBytes)
                        {
                            output.Close();
                            File.Delete(cache);
                            return Result<ModManifest>.Fail("The package exceeds the 250 MB safety limit.");
                        }
                        await output.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
                    }
                }
                if (!ChecksumMatches(cache, release.Sha256))
                {
                    File.Delete(cache);
                    return Result<ModManifest>.Fail("Downloaded package checksum does not match the registry.");
                }
            }

            var inspection = new PackageInspector().Inspect(cache);
            if (!inspection.Success || inspection.Value is null)
                return Result<ModManifest>.Fail(inspection.Error ?? "Downloaded package inspection failed.");
            if (!string.Equals(inspection.Value.Manifest.Id, catalogMod.Id, StringComparison.OrdinalIgnoreCase) ||
                !string.Equals(inspection.Value.Manifest.Version, release.Version, StringComparison.OrdinalIgnoreCase))
                return Result<ModManifest>.Fail("Downloaded package identity does not match the official registry release.");

            var result = _importer.Import(cache);
            if (result.Success) _logger.Info($"Installed official mod {result.Value!.Id} {result.Value.Version}.");
            return result;
        }
        catch (Exception ex) when (ex is HttpRequestException or IOException or UnauthorizedAccessException or TaskCanceledException)
        {
            return Result<ModManifest>.Fail($"Could not download official package: {ex.Message}");
        }
    }

    private static bool IsValidMod(CatalogMod mod) => !string.IsNullOrWhiteSpace(mod.Id) && mod.Releases.Count > 0;

    private static Version ParseVersion(string value) => Version.TryParse(value.TrimStart('v', 'V'), out var version) ? version : new Version(0, 0);

    private static bool ChecksumMatches(string path, string expected)
    {
        using var stream = File.OpenRead(path);
        var actual = Convert.ToHexString(SHA256.HashData(stream));
        return string.Equals(actual, expected, StringComparison.OrdinalIgnoreCase);
    }
}
