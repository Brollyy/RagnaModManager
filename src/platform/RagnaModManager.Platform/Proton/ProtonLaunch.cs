using System.Diagnostics;
using RagnaModManager.Core.Common;
using RagnaModManager.Platform.Steam;

namespace RagnaModManager.Platform.Proton;

public sealed record LaunchPlan(
    string DisplayCommand,
    bool UsesSteamProtocol,
    string? ExecutablePath,
    string GameArguments,
    string SteamLaunchOptions);

public sealed class ProtonLaunch
{
    public const string RequiredGameArguments = "-nohmd";
    public const string RequiredSteamLaunchOptions = "WINEDLLOVERRIDES=\"dwmapi=n,b\" %command% -nohmd";

    public LaunchPlan BuildPlan(string? executablePath, bool preferSteamProtocol, string? extraArguments = null)
    {
        var gameArguments = CombineArguments(RequiredGameArguments, extraArguments);
        if (preferSteamProtocol)
        {
            return new LaunchPlan($"steam://rungameid/{SteamLibraryDiscoverer.RagnarockSteamAppId}", true, executablePath, gameArguments, RequiredSteamLaunchOptions);
        }

        if (string.IsNullOrWhiteSpace(executablePath))
        {
            return new LaunchPlan($"steam://rungameid/{SteamLibraryDiscoverer.RagnarockSteamAppId}", true, null, gameArguments, RequiredSteamLaunchOptions);
        }

        return new LaunchPlan($"{executablePath} {gameArguments}", false, executablePath, gameArguments, RequiredSteamLaunchOptions);
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
                Arguments = CombineArguments(plan.GameArguments, arguments),
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

    private static string CombineArguments(params string?[] values) =>
        string.Join(' ', values.Where(value => !string.IsNullOrWhiteSpace(value)).Select(value => value!.Trim()));
}
