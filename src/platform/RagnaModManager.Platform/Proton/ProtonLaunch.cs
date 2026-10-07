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
    public const string RequiredSteamLaunchOptions = "WINEDLLOVERRIDES=\"dwmapi=n,b\" %command%";

    public static string BuildSteamLaunchOptions(string? extraArguments) =>
        BuildSteamLaunchOptions(extraArguments, null);

    public static string BuildSteamLaunchOptions(string? userOptionalArguments, string? launchOptionArguments)
    {
        if (OperatingSystem.IsWindows()) return CombineArguments(userOptionalArguments, launchOptionArguments);

        const string command = "%command%";
        var commandIndex = RequiredSteamLaunchOptions.IndexOf(command, StringComparison.Ordinal);
        var prefix = RequiredSteamLaunchOptions[..commandIndex].Trim();
        var userProvidedCommand = userOptionalArguments?.Contains(command, StringComparison.Ordinal) == true;
        return userProvidedCommand
            ? CombineArguments(prefix, userOptionalArguments, launchOptionArguments)
            : CombineArguments(prefix, userOptionalArguments, command, launchOptionArguments);
    }

    public LaunchPlan BuildPlan(string? executablePath, bool preferSteamProtocol, string? extraArguments = null, string? launchOptionArguments = null)
    {
        var gameArguments = CombineArguments(extraArguments, launchOptionArguments);
        if (preferSteamProtocol)
        {
            return new LaunchPlan($"steam://rungameid/{SteamLibraryDiscoverer.RagnarockSteamAppId}", true, executablePath, gameArguments,
                BuildSteamLaunchOptions(extraArguments, launchOptionArguments));
        }

        if (string.IsNullOrWhiteSpace(executablePath))
        {
            return new LaunchPlan($"steam://rungameid/{SteamLibraryDiscoverer.RagnarockSteamAppId}", true, null, gameArguments, BuildSteamLaunchOptions(extraArguments, launchOptionArguments));
        }

        var displayCommand = string.IsNullOrWhiteSpace(gameArguments) ? executablePath : $"{executablePath} {gameArguments}";
        return new LaunchPlan(displayCommand, false, executablePath, gameArguments, BuildSteamLaunchOptions(extraArguments, launchOptionArguments));
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
