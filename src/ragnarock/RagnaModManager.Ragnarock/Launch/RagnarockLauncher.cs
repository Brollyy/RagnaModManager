using RagnaModManager.Core.Common;
using RagnaModManager.Ragnarock.Detection;
using RagnaModManager.Platform.Proton;

namespace RagnaModManager.Ragnarock.Launch;

public sealed class RagnarockLauncher
{
    private readonly RagnarockDetector _detector;
    private readonly ProtonLaunch _protonLaunch;

    public RagnarockLauncher() : this(new RagnarockDetector(), new ProtonLaunch())
    {
    }

    public RagnarockLauncher(RagnarockDetector detector, ProtonLaunch protonLaunch)
    {
        _detector = detector;
        _protonLaunch = protonLaunch;
    }

    public LaunchPlan BuildLaunchPlan(string gameRoot)
    {
        var executable = RagnarockDetector.FindExecutable(gameRoot);
        var preferSteam = !OperatingSystem.IsWindows() && _detector.IsLikelySteamInstall(gameRoot);
        return _protonLaunch.BuildPlan(executable, preferSteam);
    }

    public Result Launch(string gameRoot, string? arguments = null)
    {
        var plan = BuildLaunchPlan(gameRoot);
        if (!plan.UsesSteamProtocol && string.IsNullOrWhiteSpace(plan.ExecutablePath))
        {
            return Result.Fail("Could not find Ragnarock executable. Set or validate the game path first.");
        }

        return _protonLaunch.Launch(plan, arguments);
    }
}
