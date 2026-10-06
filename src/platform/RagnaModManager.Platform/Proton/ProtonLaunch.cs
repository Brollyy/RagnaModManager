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
        OperatingSystem.IsWindows()
            ? CombineArguments(extraArguments)
            : CombineArguments(RequiredSteamLaunchOptions, extraArguments);

    public LaunchPlan BuildPlan(string? executablePath, bool preferSteamProtocol, string? extraArguments = null, int? steamLaunchOption = null)
    {
        var gameArguments = CombineArguments(extraArguments);
        if (preferSteamProtocol || steamLaunchOption.HasValue)
        {
            var launchUri = steamLaunchOption is int option
                ? $"steam://launch/{SteamLibraryDiscoverer.RagnarockSteamAppId}/option{option}"
                : $"steam://rungameid/{SteamLibraryDiscoverer.RagnarockSteamAppId}";
            return new LaunchPlan(launchUri, true, executablePath, gameArguments, BuildSteamLaunchOptions(extraArguments));
        }

        if (string.IsNullOrWhiteSpace(executablePath))
        {
            return new LaunchPlan($"steam://rungameid/{SteamLibraryDiscoverer.RagnarockSteamAppId}", true, null, gameArguments, BuildSteamLaunchOptions(extraArguments));
        }

        var displayCommand = string.IsNullOrWhiteSpace(gameArguments) ? executablePath : $"{executablePath} {gameArguments}";
        return new LaunchPlan(displayCommand, false, executablePath, gameArguments, BuildSteamLaunchOptions(extraArguments));
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
