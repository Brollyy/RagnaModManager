using RagnaModManager.Platform.Steam;

namespace RagnaModManager.Ragnarock.Detection;

public sealed class RagnarockDetector
{
    private readonly SteamLibraryDiscoverer _steam;

    public RagnarockDetector() : this(new SteamLibraryDiscoverer())
    {
    }

    public RagnarockDetector(SteamLibraryDiscoverer steam)
    {
        _steam = steam;
    }

    public IReadOnlyList<string> FindCandidates()
    {
        var candidates = new List<string>();
        candidates.AddRange(_steam.FindGameInstallCandidates());
        if (OperatingSystem.IsWindows())
        {
            candidates.Add(@"C:\Program Files (x86)\Steam\steamapps\common\Ragnarock");
            candidates.Add(@"C:\Program Files\Steam\steamapps\common\Ragnarock");
        }
        else
        {
            var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            candidates.Add(Path.Combine(home, ".steam", "steam", "steamapps", "common", "Ragnarock"));
            candidates.Add(Path.Combine(home, ".steam", "debian-installation", "steamapps", "common", "Ragnarock"));
            candidates.Add(Path.Combine(home, ".local", "share", "Steam", "steamapps", "common", "Ragnarock"));
            candidates.AddRange(Directory.Exists("/mnt")
                ? Directory.EnumerateDirectories("/mnt")
                    .Select(d => Path.Combine(d, "SteamLibrary", "steamapps", "common", "Ragnarock"))
                : []);
        }

        return candidates.Where(Directory.Exists).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
    }

    public RagnarockInstall? DetectFirstValid()
    {
        return FindCandidates()
            .Select(Validate)
            .OrderByDescending(i => i.IsValid)
            .FirstOrDefault();
    }

    public RagnarockInstall Validate(string root)
    {
        var diagnostics = new List<string>();
        if (!Directory.Exists(root))
        {
            return new RagnarockInstall(root, null, false, [$"Folder does not exist: {root}"]);
        }

        var exe = FindExecutable(root);
        if (exe is null)
        {
            diagnostics.Add("Could not find Ragnarock.exe or Ragnarock/Binaries/Win64/Ragnarock-Win64-Shipping.exe.");
        }

        var binaries = Path.Combine(root, "Ragnarock", "Binaries", "Win64");
        if (!Directory.Exists(binaries))
        {
            diagnostics.Add("Missing expected folder: Ragnarock/Binaries/Win64");
        }

        var paks = Path.Combine(root, "Ragnarock", "Content", "Paks");
        if (!Directory.Exists(paks))
        {
            diagnostics.Add("Missing expected folder: Ragnarock/Content/Paks");
        }

        return new RagnarockInstall(root, exe, diagnostics.Count == 0, diagnostics);
    }

    public bool IsLikelySteamInstall(string root) => _steam.IsLikelySteamInstall(root);

    public static string? FindExecutable(string root)
    {
        var candidates = new[]
        {
            Path.Combine(root, "Ragnarock.exe"),
            Path.Combine(root, "Ragnarock", "Binaries", "Win64", "Ragnarock-Win64-Shipping.exe")
        };
        return candidates.FirstOrDefault(File.Exists);
    }
}
