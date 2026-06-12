using System.Net.Http.Headers;
using System.Text.Json;
using RagnaModManager.Core.Common;
using RagnaModManager.Core.Platform;

namespace RagnaModManager.Ragnarock.Ue4ss;

public sealed record Ue4ssRelease(
    string Version,
    string TagName,
    string Name,
    string AssetName,
    string DownloadUrl,
    DateTimeOffset PublishedAt,
    bool Prerelease,
    string? CachedArchivePath,
    DateTimeOffset? CachedAt);

public sealed record Ue4ssUpdateCheck(Ue4ssStatus Installed, Ue4ssRelease? Latest, bool UpdateAvailable);

public sealed class Ue4ssReleaseService
{
    private const string ReleasesUrl = "https://api.github.com/repos/UE4SS-RE/RE-UE4SS/releases";
    private readonly AppPaths _paths;
    private readonly HttpClient _client;
    private readonly Ue4ssService _ue4ss;
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    public Ue4ssReleaseService(AppPaths paths, HttpClient? client = null, Ue4ssService? ue4ss = null)
    {
        _paths = paths;
        _client = client ?? new HttpClient();
        _ue4ss = ue4ss ?? new Ue4ssService();
        if (!_client.DefaultRequestHeaders.UserAgent.Any())
        {
            _client.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("RagnaModManager", "1.0"));
        }
    }

    public async Task<Result<Ue4ssUpdateCheck>> CheckForUpdates(string gameRoot, CancellationToken cancellationToken = default)
    {
        var installed = _ue4ss.Detect(gameRoot);
        var releases = await FetchAvailableReleases(cancellationToken);
        if (!releases.Success)
        {
            return Result<Ue4ssUpdateCheck>.Fail(releases.Error ?? "Could not fetch RE-UE4SS releases.");
        }

        var latest = releases.Value!.FirstOrDefault();
        MergeReleaseMetadata(releases.Value!);
        var updateAvailable = latest is not null && (!installed.Installed || IsNewer(latest.Version, installed.Version));
        return Result<Ue4ssUpdateCheck>.Ok(new Ue4ssUpdateCheck(installed, latest, updateAvailable));
    }

    public IReadOnlyList<Ue4ssRelease> GetCachedReleases()
    {
        return ReadCatalog()
            .Where(r => !string.IsNullOrWhiteSpace(r.CachedArchivePath) && File.Exists(r.CachedArchivePath))
            .OrderByDescending(r => ParseVersion(r.Version))
            .ThenByDescending(r => r.PublishedAt)
            .ToList();
    }

    public async Task<Result<Ue4ssRelease>> DownloadRelease(Ue4ssRelease release, CancellationToken cancellationToken = default)
    {
        var existing = FindCachedRelease(release.Version);
        if (existing is not null)
        {
            return Result<Ue4ssRelease>.Ok(existing);
        }

        Directory.CreateDirectory(_paths.Ue4ssDownloads);
        var versionFolder = Path.Combine(_paths.Ue4ssDownloads, SafeFileName(release.Version));
        Directory.CreateDirectory(versionFolder);
        var target = Path.Combine(versionFolder, SafeFileName(release.AssetName));
        var temp = target + ".download";

        try
        {
            using var response = await _client.GetAsync(release.DownloadUrl, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
            response.EnsureSuccessStatusCode();
            await using (var source = await response.Content.ReadAsStreamAsync(cancellationToken))
            await using (var destination = File.Create(temp))
            {
                await source.CopyToAsync(destination, cancellationToken);
            }

            if (File.Exists(target))
            {
                File.Delete(target);
            }

            File.Move(temp, target);
            var cached = release with { CachedArchivePath = target, CachedAt = DateTimeOffset.UtcNow };
            UpsertCatalog(cached);
            return Result<Ue4ssRelease>.Ok(cached);
        }
        catch (Exception ex) when (ex is HttpRequestException or IOException or TaskCanceledException)
        {
            if (File.Exists(temp))
            {
                File.Delete(temp);
            }

            return Result<Ue4ssRelease>.Fail($"Could not download RE-UE4SS {release.Version}: {ex.Message}");
        }
    }

    public Result InstallCachedRelease(string gameRoot, Ue4ssRelease release)
    {
        if (string.IsNullOrWhiteSpace(release.CachedArchivePath) || !File.Exists(release.CachedArchivePath))
        {
            return Result.Fail($"RE-UE4SS {release.Version} is not cached yet.");
        }

        return _ue4ss.InstallFromZip(gameRoot, release.CachedArchivePath, release.Version);
    }

    public async Task<Result<IReadOnlyList<Ue4ssRelease>>> FetchAvailableReleases(CancellationToken cancellationToken = default)
    {
        try
        {
            var json = await _client.GetStringAsync(ReleasesUrl, cancellationToken);
            var releases = ParseGithubReleases(json);
            if (releases.Count == 0)
            {
                return Result<IReadOnlyList<Ue4ssRelease>>.Fail("No usable RE-UE4SS zip release assets were found.");
            }

            return Result<IReadOnlyList<Ue4ssRelease>>.Ok(releases);
        }
        catch (Exception ex) when (ex is HttpRequestException or JsonException or TaskCanceledException)
        {
            return Result<IReadOnlyList<Ue4ssRelease>>.Fail($"Could not fetch RE-UE4SS releases: {ex.Message}");
        }
    }

    internal static IReadOnlyList<Ue4ssRelease> ParseGithubReleases(string json)
    {
        using var document = JsonDocument.Parse(json);
        var releases = new List<Ue4ssRelease>();
        foreach (var release in document.RootElement.EnumerateArray())
        {
            if (release.TryGetProperty("draft", out var draft) && draft.GetBoolean())
            {
                continue;
            }

            var tagName = release.GetProperty("tag_name").GetString() ?? "";
            var version = NormalizeVersion(tagName);
            var name = release.TryGetProperty("name", out var releaseName) ? releaseName.GetString() ?? tagName : tagName;
            var published = release.TryGetProperty("published_at", out var publishedAt) && publishedAt.TryGetDateTimeOffset(out var date)
                ? date
                : DateTimeOffset.MinValue;
            var prerelease = release.TryGetProperty("prerelease", out var prereleaseValue) && prereleaseValue.GetBoolean();
            var asset = ChooseReleaseAsset(release.GetProperty("assets"));
            if (asset is null)
            {
                continue;
            }

            releases.Add(new Ue4ssRelease(
                version,
                tagName,
                name,
                asset.Value.Name,
                asset.Value.DownloadUrl,
                published,
                prerelease,
                null,
                null));
        }

        return releases
            .OrderByDescending(r => ParseVersion(r.Version))
            .ThenByDescending(r => r.PublishedAt)
            .ToList();
    }

    internal static bool IsNewer(string candidate, string? installed)
    {
        if (string.IsNullOrWhiteSpace(installed))
        {
            return true;
        }

        return ParseVersion(candidate).CompareTo(ParseVersion(installed)) > 0;
    }

    private void MergeReleaseMetadata(IReadOnlyList<Ue4ssRelease> releases)
    {
        var existing = ReadCatalog();
        foreach (var release in releases)
        {
            var cached = existing.FirstOrDefault(r => SameVersion(r.Version, release.Version) && !string.IsNullOrWhiteSpace(r.CachedArchivePath));
            UpsertCatalog(cached is null ? release : release with { CachedArchivePath = cached.CachedArchivePath, CachedAt = cached.CachedAt });
        }
    }

    private Ue4ssRelease? FindCachedRelease(string version)
    {
        return ReadCatalog().FirstOrDefault(r =>
            SameVersion(r.Version, version) &&
            !string.IsNullOrWhiteSpace(r.CachedArchivePath) &&
            File.Exists(r.CachedArchivePath));
    }

    private List<Ue4ssRelease> ReadCatalog()
    {
        var path = CatalogPath();
        if (!File.Exists(path))
        {
            return [];
        }

        try
        {
            return JsonSerializer.Deserialize<List<Ue4ssRelease>>(File.ReadAllText(path), JsonOptions) ?? [];
        }
        catch (JsonException)
        {
            return [];
        }
    }

    private void UpsertCatalog(Ue4ssRelease release)
    {
        var releases = ReadCatalog();
        releases.RemoveAll(r => SameVersion(r.Version, release.Version));
        releases.Add(release);
        releases = releases
            .OrderByDescending(r => ParseVersion(r.Version))
            .ThenByDescending(r => r.PublishedAt)
            .ToList();
        Directory.CreateDirectory(_paths.Ue4ssDownloads);
        File.WriteAllText(CatalogPath(), JsonSerializer.Serialize(releases, JsonOptions));
    }

    private string CatalogPath() => Path.Combine(_paths.Ue4ssDownloads, "releases.json");

    private static (string Name, string DownloadUrl)? ChooseReleaseAsset(JsonElement assets)
    {
        return assets.EnumerateArray()
            .Select(asset => new
            {
                Name = asset.GetProperty("name").GetString() ?? "",
                DownloadUrl = asset.GetProperty("browser_download_url").GetString() ?? ""
            })
            .Where(asset => asset.Name.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
            .Where(asset => asset.Name.Contains("UE4SS", StringComparison.OrdinalIgnoreCase))
            .Where(asset => !asset.Name.StartsWith("zDEV-", StringComparison.OrdinalIgnoreCase))
            .OrderBy(asset => asset.Name.StartsWith("UE4SS_", StringComparison.OrdinalIgnoreCase) ? 0 : 1)
            .Select(asset => ((string Name, string DownloadUrl)?)new(asset.Name, asset.DownloadUrl))
            .FirstOrDefault();
    }

    private static string NormalizeVersion(string value)
    {
        var version = value.Trim();
        return version.StartsWith('v') || version.StartsWith('V') ? version[1..] : version;
    }

    private static Version ParseVersion(string? value)
    {
        var normalized = NormalizeVersion(value ?? "");
        var chars = normalized.TakeWhile(c => char.IsDigit(c) || c == '.').ToArray();
        var core = new string(chars).Trim('.');
        if (string.IsNullOrWhiteSpace(core))
        {
            return new Version(0, 0);
        }

        var parts = core.Split('.', StringSplitOptions.RemoveEmptyEntries).Take(4).ToList();
        while (parts.Count < 2)
        {
            parts.Add("0");
        }

        return Version.TryParse(string.Join('.', parts), out var parsed) ? parsed : new Version(0, 0);
    }

    private static bool SameVersion(string left, string right) => ParseVersion(left).Equals(ParseVersion(right));

    private static string SafeFileName(string value)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var chars = value.Select(c => invalid.Contains(c) || c is '/' or '\\' ? '_' : c).ToArray();
        return new string(chars);
    }
}
