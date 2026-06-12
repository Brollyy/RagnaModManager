namespace RagnaModManager.Core.Platform;

public sealed record AppPaths(
    string Root,
    string DatabasePath,
    string Downloads,
    string ModLibrary,
    string Profiles,
    string Deployment,
    string Backups,
    string Logs)
{
    public static AppPaths CreateDefault()
    {
        var root = GetDefaultRoot();
        return Create(root);
    }

    public static AppPaths Create(string root)
    {
        var paths = new AppPaths(
            root,
            Path.Combine(root, "manager.db"),
            Path.Combine(root, "downloads"),
            Path.Combine(root, "mod-library"),
            Path.Combine(root, "profiles"),
            Path.Combine(root, "deployment"),
            Path.Combine(root, "deployment", "backups"),
            Path.Combine(root, "logs"));
        paths.EnsureCreated();
        return paths;
    }

    public string CurrentDeploymentPath => Path.Combine(Deployment, "current.json");

    public string Ue4ssDownloads => Path.Combine(Downloads, "ue4ss");

    public void EnsureCreated()
    {
        Directory.CreateDirectory(Root);
        Directory.CreateDirectory(Downloads);
        Directory.CreateDirectory(ModLibrary);
        Directory.CreateDirectory(Profiles);
        Directory.CreateDirectory(Deployment);
        Directory.CreateDirectory(Backups);
        Directory.CreateDirectory(Logs);
        Directory.CreateDirectory(Ue4ssDownloads);
    }

    private static string GetDefaultRoot()
    {
        if (OperatingSystem.IsWindows())
        {
            var local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            return Path.Combine(local, "RagnarockModManager");
        }

        var xdg = Environment.GetEnvironmentVariable("XDG_DATA_HOME");
        if (!string.IsNullOrWhiteSpace(xdg))
        {
            return Path.Combine(xdg, "ragnarock-mod-manager");
        }

        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        return Path.Combine(home, ".local", "share", "ragnarock-mod-manager");
    }
}
