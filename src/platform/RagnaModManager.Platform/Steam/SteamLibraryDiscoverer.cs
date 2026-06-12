namespace RagnaModManager.Platform.Steam;

public sealed class SteamLibraryDiscoverer
{
    public const string RagnarockSteamAppId = "1345820";

    public IReadOnlyList<string> FindSteamRoots()
    {
        var roots = new List<string>();
        if (OperatingSystem.IsWindows())
        {
            roots.Add(@"C:\Program Files (x86)\Steam");
            roots.Add(@"C:\Program Files\Steam");
        }
        else
        {
            var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            roots.Add(Path.Combine(home, ".steam", "steam"));
            roots.Add(Path.Combine(home, ".steam", "debian-installation"));
            roots.Add(Path.Combine(home, ".local", "share", "Steam"));
            roots.Add(Path.Combine(home, ".var", "app", "com.valvesoftware.Steam", ".local", "share", "Steam"));
        }

        return roots.Where(Directory.Exists).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
    }

    public IReadOnlyList<string> FindLibraryFolders()
    {
        var libraries = new List<string>();
        foreach (var root in FindSteamRoots())
        {
            var steamApps = Path.Combine(root, "steamapps");
            if (Directory.Exists(steamApps))
            {
                libraries.Add(steamApps);
            }

            var vdf = Path.Combine(steamApps, "libraryfolders.vdf");
            if (!File.Exists(vdf))
            {
                continue;
            }

            libraries.AddRange(ParseLibraryFoldersVdf(File.ReadAllText(vdf))
                .Select(path => Path.Combine(path, "steamapps")));
        }

        return libraries.Where(Directory.Exists).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
    }

    public IReadOnlyList<string> FindGameInstallCandidates(string folderName = "Ragnarock")
    {
        var candidates = FindLibraryFolders()
            .Select(library => Path.Combine(library, "common", folderName))
            .ToList();

        if (!OperatingSystem.IsWindows() && Directory.Exists("/mnt"))
        {
            candidates.AddRange(Directory.EnumerateDirectories("/mnt")
                .Select(d => Path.Combine(d, "SteamLibrary", "steamapps", "common", folderName)));
        }

        return candidates.Where(Directory.Exists).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
    }

    public bool IsLikelySteamInstall(string gameRoot)
    {
        var full = Path.GetFullPath(gameRoot);
        return FindGameInstallCandidates().Any(candidate => string.Equals(Path.GetFullPath(candidate), full, StringComparison.OrdinalIgnoreCase));
    }

    public static IReadOnlyList<string> ParseLibraryFoldersVdf(string text)
    {
        var paths = new List<string>();
        foreach (var rawLine in text.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries))
        {
            var line = rawLine.Trim();
            if (!line.StartsWith("\"path\"", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var parts = line.Split('"', StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length >= 2)
            {
                paths.Add(parts[^1].Replace(@"\\", @"\"));
            }
        }

        return paths;
    }
}
