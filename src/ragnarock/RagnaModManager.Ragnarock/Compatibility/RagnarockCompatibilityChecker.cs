using RagnaModManager.Ragnarock.DeploymentRules;
using RagnaModManager.Ragnarock.Detection;
using RagnaModManager.Ragnarock.Ue4ss;
using RagnaModManager.Platform.Steam;

namespace RagnaModManager.Ragnarock.Compatibility;

public sealed class RagnarockCompatibilityChecker
{
    private readonly RagnarockDetector _detector;
    private readonly Ue4ssService _ue4ss;

    public RagnarockCompatibilityChecker() : this(new RagnarockDetector(), new Ue4ssService())
    {
    }

    public RagnarockCompatibilityChecker(RagnarockDetector detector, Ue4ssService ue4ss)
    {
        _detector = detector;
        _ue4ss = ue4ss;
    }

    public CompatibilityReport Check(string gameRoot)
    {
        var errors = new List<string>();
        var warnings = new List<string>();
        var info = new List<string>();

        var install = _detector.Validate(gameRoot);
        if (!Directory.Exists(gameRoot))
        {
            errors.Add($"Game folder does not exist: {gameRoot}");
        }

        errors.AddRange(install.Diagnostics.Where(d => d.StartsWith("Missing expected folder", StringComparison.Ordinal)));
        warnings.AddRange(install.Diagnostics.Where(d => !d.StartsWith("Missing expected folder", StringComparison.Ordinal)));

        var pakFolder = RagnarockDeploymentRules.GetPakModFolder(gameRoot);
        if (Directory.Exists(Path.GetDirectoryName(pakFolder)))
        {
            info.Add($"Pak mod folder will be used: {pakFolder}");
        }

        if (_detector.IsLikelySteamInstall(gameRoot))
        {
            info.Add("Steam install detected.");
        }
        else
        {
            warnings.Add("The path is not in a discovered Steam library. Manual paths are supported, but launch may need direct executable access.");
        }

        var steamLaunch = _ue4ss.Detect(gameRoot).Installed
            ? new SteamLaunchOptionsService().Inspect(gameRoot)
            : null;
        if (steamLaunch?.Applicable == true && !steamLaunch.Configured)
        {
            warnings.Add($"{steamLaunch.Message} Required option: {steamLaunch.RequiredOptions}");
        }

        var ue4ss = _ue4ss.Detect(gameRoot);
        if (ue4ss.Installed)
        {
            info.Add($"UE4SS detected using layout: {ue4ss.Layout}");
        }
        else
        {
            warnings.Add("UE4SS is not installed. UE4SS Lua and DLL mods need UE4SS before launch.");
        }

        warnings.AddRange(ue4ss.Diagnostics);
        return new CompatibilityReport(errors.Count == 0, errors, warnings, info);
    }
}
