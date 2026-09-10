using System.Diagnostics;
using RagnaModManager.Core.Common;
using RagnaModManager.Platform.Steam;

namespace RagnaModManager.Platform.Proton;

public sealed record LaunchPlan(string DisplayCommand, bool UsesSteamProtocol, string? ExecutablePath);

public sealed class ProtonLaunch
{
    public LaunchPlan BuildPlan(string? executablePath, bool preferSteamProtocol)
    {
        if (preferSteamProtocol)
        {
            return new LaunchPlan($"steam://rungameid/{SteamLibraryDiscoverer.RagnarockSteamAppId}", true, executablePath);
        }

        if (string.IsNullOrWhiteSpace(executablePath))
        {
            return new LaunchPlan($"steam://rungameid/{SteamLibraryDiscoverer.RagnarockSteamAppId}", true, null);
        }

        return new LaunchPlan(executablePath, false, executablePath);
    }

    public Result Launch(LaunchPlan plan, string? arguments = null)
    {
        try
        {
            if (plan.UsesSteamProtocol)
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = plan.DisplayCommand,
                    UseShellExecute = true
                });
                return Result.Ok();
            }

            if (string.IsNullOrWhiteSpace(plan.ExecutablePath))
            {
                return Result.Fail("No executable path is available for direct launch.");
            }

            Process.Start(new ProcessStartInfo
            {
                FileName = plan.ExecutablePath,
                Arguments = arguments ?? "",
                WorkingDirectory = Path.GetDirectoryName(plan.ExecutablePath)!,
                UseShellExecute = true
            });
            return Result.Ok();
        }
        catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            return Result.Fail($"Could not launch Ragnarock: {ex.Message}");
        }
    }
}
