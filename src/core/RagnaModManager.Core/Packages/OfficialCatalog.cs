using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text.Json;
using RagnaModManager.Core.Common;
using RagnaModManager.Core.Compatibility;
using RagnaModManager.Core.Database;
using RagnaModManager.Core.Logging;
using RagnaModManager.Core.Manifests;
using RagnaModManager.Core.Platform;

namespace RagnaModManager.Core.Packages;

public sealed record OfficialCatalog(string SchemaVersion, string Repository, IReadOnlyList<CatalogMod> Mods);
public sealed record CatalogMod(string Id, string Name, string? Author, string? Description, IReadOnlyList<CatalogRelease> Releases, string? SourceUrl = null, string? License = null, IReadOnlyDictionary<string, string>? Dependencies = null, IReadOnlyList<string>? Conflicts = null)
{
    public CatalogRelease? Latest => Releases.OrderByDescending(r => r.Version, Comparer<string>.Create(SemanticVersion.Compare)).FirstOrDefault();
}
public sealed record CatalogRelease(string Version, string PackageUrl, string Sha256, string? PublishedAt = null, string? Changelog = null, long? SizeBytes = null);

public sealed class OfficialCatalogService
{
    public const string DefaultIndexUrl = "https://raw.githubusercontent.com/Brollyy/RagnaModManager-ModRegistry/main/index.json";
    private const long MaxPackageBytes = 250L * 1024 * 1024;
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };
    private readonly AppPaths _paths;
    private readonly PackageImporter _importer;
    private readonly HttpClient _http;
    private readonly AppLogger _logger;
    private IReadOnlyDictionary<string, CatalogMod> _catalogMods = new Dictionary<string, CatalogMod>(StringComparer.OrdinalIgnoreCase);

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
                return Result<OfficialCatalog>.Fail("The community mod catalog is empty or malformed.");
            if (!string.Equals(catalog.SchemaVersion, "1", StringComparison.Ordinal))
                return Result<OfficialCatalog>.Fail($"Unsupported community catalog schema '{catalog.SchemaVersion}'.");
            if (!string.Equals(catalog.Repository, "rmm-registry", StringComparison.OrdinalIgnoreCase))
                return Result<OfficialCatalog>.Fail("The downloaded registry is not the expected community catalog.");
            var mods = catalog.Mods.Where(IsValidMod).ToList();
            var byId = mods.ToDictionary(m => m.Id, StringComparer.OrdinalIgnoreCase);
            foreach (var mod in mods)
            {
                foreach (var dependency in mod.Dependencies ?? new Dictionary<string, string>())
                {
                    if (!IsValidId(dependency.Key) || !VersionRequirement.ValidateSyntax(dependency.Value).Success || !byId.ContainsKey(dependency.Key))
                        return Result<OfficialCatalog>.Fail($"Community catalog dependency '{dependency.Key}' for '{mod.Id}' is invalid or not cataloged.");
                }

                foreach (var conflict in mod.Conflicts ?? [])
                {
                    if (!IsValidId(conflict) || !byId.ContainsKey(conflict) || string.Equals(conflict, mod.Id, StringComparison.OrdinalIgnoreCase))
                        return Result<OfficialCatalog>.Fail($"Community catalog conflict '{conflict}' for '{mod.Id}' is invalid or not cataloged.");
                }
            }
            _catalogMods = byId;
            return Result<OfficialCatalog>.Ok(catalog with { Mods = mods });
        }
        catch (Exception ex) when (ex is HttpRequestException or JsonException or TaskCanceledException)
        {
            return Result<OfficialCatalog>.Fail($"Could not load the official mod registry: {ex.Message}");
        }
    }

    public async Task<Result<ModManifest>> DownloadAndImportAsync(CatalogMod catalogMod, CatalogRelease release, CancellationToken cancellationToken = default)
    {
        return await DownloadAndImportInternalAsync(catalogMod, release, new HashSet<string>(StringComparer.OrdinalIgnoreCase), cancellationToken);
    }

    private async Task<Result<ModManifest>> DownloadAndImportInternalAsync(CatalogMod catalogMod, CatalogRelease release, HashSet<string> visiting, CancellationToken cancellationToken)
    {
        if (!visiting.Add(catalogMod.Id)) return Result<ModManifest>.Fail($"Community catalog contains a dependency cycle involving '{catalogMod.Id}'.");
        try
        {
            foreach (var dependency in catalogMod.Dependencies ?? new Dictionary<string, string>())
            {
                var installed = _importer.Database.GetMod(dependency.Key);
                if (installed is not null && VersionRequirement.IsSatisfied(dependency.Value, installed.Version)) continue;
                if (!_catalogMods.TryGetValue(dependency.Key, out var dependencyMod))
                    return Result<ModManifest>.Fail($"{catalogMod.Name} requires {dependency.Key} {dependency.Value}, but it is not in the community catalog.");
                var dependencyRelease = dependencyMod.Releases
                    .Where(r => VersionRequirement.IsSatisfied(dependency.Value, r.Version))
                    .OrderByDescending(r => r.Version, Comparer<string>.Create(SemanticVersion.Compare))
                    .FirstOrDefault();
                if (dependencyRelease is null)
                    return Result<ModManifest>.Fail($"No community release of {dependencyMod.Name} satisfies {dependency.Value}.");
                var dependencyResult = await DownloadAndImportInternalAsync(dependencyMod, dependencyRelease, visiting, cancellationToken);
                if (!dependencyResult.Success) return dependencyResult;
            }

            return await DownloadPackageAsync(catalogMod, release, cancellationToken);
        }
        finally
        {
            visiting.Remove(catalogMod.Id);
        }
    }

    private async Task<Result<ModManifest>> DownloadPackageAsync(CatalogMod catalogMod, CatalogRelease release, CancellationToken cancellationToken)
    {
        if (!Uri.TryCreate(release.PackageUrl, UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttps)
            return Result<ModManifest>.Fail("Community catalog packages must use HTTPS URLs.");
        if (string.IsNullOrWhiteSpace(release.Sha256) || release.Sha256.Length != 64)
            return Result<ModManifest>.Fail("The catalog release is missing a valid SHA-256 checksum.");

            var cache = Path.Combine(_paths.Downloads, "community-catalog", release.Sha256.ToLowerInvariant() + ".rmod");
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
            var manifestConflicts = inspection.Value.Manifest.Conflicts.ToHashSet(StringComparer.OrdinalIgnoreCase);
            var missingCatalogConflicts = (catalogMod.Conflicts ?? [])
                .Where(conflict => !manifestConflicts.Contains(conflict))
                .ToList();
            if (missingCatalogConflicts.Count > 0)
                return Result<ModManifest>.Fail($"Downloaded package manifest is missing registry conflicts: {string.Join(", ", missingCatalogConflicts)}.");

            var result = _importer.Import(cache, enableInProfiles: true);
            if (result.Success) _logger.Info($"Installed community mod {result.Value!.Id} {result.Value.Version}.");
            return result;
        }
        catch (Exception ex) when (ex is HttpRequestException or IOException or UnauthorizedAccessException or TaskCanceledException)
        {
            return Result<ModManifest>.Fail($"Could not download official package: {ex.Message}");
        }
    }

    private static bool IsValidMod(CatalogMod mod) => !string.IsNullOrWhiteSpace(mod.Id) && mod.Releases.Count > 0;

    private static bool IsValidId(string value) => !string.IsNullOrWhiteSpace(value) && value.All(c => char.IsAsciiLetterLower(c) || char.IsDigit(c) || c is '-' or '_' or '.');

    private static bool ChecksumMatches(string path, string expected)
    {
        using var stream = File.OpenRead(path);
        var actual = Convert.ToHexString(SHA256.HashData(stream));
        return string.Equals(actual, expected, StringComparison.OrdinalIgnoreCase);
    }
}
