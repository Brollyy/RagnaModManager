using RagnaModManager.Core.Common;
using RagnaModManager.Ragnarock.Detection;
using RagnaModManager.Platform.Proton;

namespace RagnaModManager.Ragnarock.Launch;

public sealed class RagnarockLauncher
{
    private readonly RagnarockDetector _detector;
    private readonly ProtonLaunch _protonLaunch;
    private readonly SteamProtonLauncher _steamProtonLauncher;

    public RagnarockLauncher() : this(new RagnarockDetector(), new ProtonLaunch(), new SteamProtonLauncher())
    {
    }

    public RagnarockLauncher(RagnarockDetector detector, ProtonLaunch protonLaunch, SteamProtonLauncher? steamProtonLauncher = null)
    {
        _detector = detector;
        _protonLaunch = protonLaunch;
        _steamProtonLauncher = steamProtonLauncher ?? new SteamProtonLauncher();
    }

    public LaunchPlan BuildLaunchPlan(string gameRoot, string? userOptionalArguments = null, string? launchOptionArguments = null)
    {
        var executable = RagnarockDetector.FindExecutable(gameRoot);
        var steamInstall = _detector.IsLikelySteamInstall(gameRoot);
        if (!OperatingSystem.IsWindows() && steamInstall)
        {
            var gameArguments = launchOptionArguments?.Trim() ?? "";
            var display = string.IsNullOrWhiteSpace(gameArguments) ? executable ?? "Proton run Ragnarock" : $"{executable} {gameArguments}";
            return new LaunchPlan(display, false, executable, gameArguments,
                _steamProtonLauncher.BuildTemplate(userOptionalArguments, launchOptionArguments));
        }

        var arguments = Join(launchOptionArguments, userOptionalArguments);
        return _protonLaunch.BuildPlan(executable, false, arguments);
    }

    public Result Launch(string gameRoot, string? userOptionalArguments = null, string? launchOptionArguments = null)
    {
        if (!OperatingSystem.IsWindows() && _detector.IsLikelySteamInstall(gameRoot))
        {
            var executable = RagnarockDetector.FindExecutable(gameRoot);
            return string.IsNullOrWhiteSpace(executable)
                ? Result.Fail("Could not find Ragnarock executable. Set or validate the game path first.")
                : _steamProtonLauncher.Launch(gameRoot, executable, userOptionalArguments, launchOptionArguments);
        }

        var plan = BuildLaunchPlan(gameRoot, userOptionalArguments, launchOptionArguments);
        if (!plan.UsesSteamProtocol && string.IsNullOrWhiteSpace(plan.ExecutablePath))
        {
            return Result.Fail("Could not find Ragnarock executable. Set or validate the game path first.");
        }

        return _protonLaunch.Launch(plan);
    }

    private static string Join(params string?[] values) =>
        string.Join(' ', values.Where(value => !string.IsNullOrWhiteSpace(value)).Select(value => value!.Trim()));
}
