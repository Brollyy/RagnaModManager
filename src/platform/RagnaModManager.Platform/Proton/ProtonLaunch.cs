using System.Diagnostics;
using System.Text.RegularExpressions;
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

    private static readonly Regex EnvironmentAssignment = new(
        @"\G\s*(?<name>[A-Za-z_][A-Za-z0-9_]*)=(?:""(?<double>[^""]*)""|'(?<single>[^']*)'|(?<plain>[^\s]+))",
        RegexOptions.Compiled);
    private static readonly Regex ArgumentToken = new(@"[^\s""']+|""[^""]*""|'[^']*'", RegexOptions.Compiled);
    private readonly SteamLibraryDiscoverer _steam;

    public ProtonLaunch() : this(new SteamLibraryDiscoverer()) { }
    public ProtonLaunch(SteamLibraryDiscoverer steam) => _steam = steam;

    public static string BuildSteamLaunchOptions(string? extraArguments) =>
        OperatingSystem.IsWindows()
            ? Join(extraArguments)
            : Join(RequiredSteamLaunchOptions, extraArguments);

    public static string BuildSteamLaunchOptions(string? userOptionalArguments, string? launchOptionArguments)
    {
        if (OperatingSystem.IsWindows()) return Join(userOptionalArguments, launchOptionArguments);
        var commandPosition = RequiredSteamLaunchOptions.IndexOf("%command%", StringComparison.Ordinal);
        var prefix = RequiredSteamLaunchOptions[..commandPosition].Trim();
        return Join(prefix, userOptionalArguments, "%command%", launchOptionArguments);
    }

    public LaunchPlan BuildPlan(string? executablePath, bool preferSteamProtocol, string? extraArguments = null)
    {
        var gameArguments = Join(extraArguments);
        if (preferSteamProtocol || string.IsNullOrWhiteSpace(executablePath))
        {
            return new LaunchPlan($"steam://rungameid/{SteamLibraryDiscoverer.RagnarockSteamAppId}", true,
                executablePath, gameArguments, BuildSteamLaunchOptions(extraArguments));
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
                Process.Start(new ProcessStartInfo { FileName = plan.DisplayCommand, UseShellExecute = true });
                return Result.Ok();
            }
            if (string.IsNullOrWhiteSpace(plan.ExecutablePath)) return Result.Fail("No executable path is available for direct launch.");
            Process.Start(new ProcessStartInfo
            {
                FileName = plan.ExecutablePath,
                Arguments = Join(plan.GameArguments, arguments),
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

    public Result Launch(string gameRoot, string executablePath, string? userOptionalArguments, string? launchOptionArguments)
    {
        if (!TryParseEnvironment(userOptionalArguments, out var environment, out var error)) return Result.Fail(error!);
        var settings = Resolve(gameRoot);
        if (!settings.Success || settings.Value is null) return Result.Fail(settings.Error ?? "Could not find Steam's Proton runtime.");

        try
        {
            var launch = settings.Value;
            var start = new ProcessStartInfo { FileName = launch.Wrapper, WorkingDirectory = gameRoot, UseShellExecute = false };
            start.ArgumentList.Add("--");
            start.ArgumentList.Add(launch.Reaper);
            start.ArgumentList.Add("SteamLaunch");
            start.ArgumentList.Add($"AppId={SteamLibraryDiscoverer.RagnarockSteamAppId}");
            start.ArgumentList.Add("--");
            start.ArgumentList.Add(launch.RuntimeEntryPoint);
            start.ArgumentList.Add("--verb=waitforexitandrun");
            start.ArgumentList.Add("--");
            start.ArgumentList.Add(launch.Proton);
            start.ArgumentList.Add("waitforexitandrun");
            start.ArgumentList.Add(executablePath);
            foreach (var argument in SplitArguments(launchOptionArguments)) start.ArgumentList.Add(argument);

            start.Environment["STEAM_COMPAT_CLIENT_INSTALL_PATH"] = launch.SteamRoot;
            start.Environment["STEAM_COMPAT_DATA_PATH"] = launch.CompatData;
            start.Environment["STEAM_COMPAT_APP_ID"] = SteamLibraryDiscoverer.RagnarockSteamAppId;
            start.Environment["SteamAppId"] = SteamLibraryDiscoverer.RagnarockSteamAppId;
            start.Environment["SteamGameId"] = SteamLibraryDiscoverer.RagnarockSteamAppId;
            start.Environment["WINEDLLOVERRIDES"] = "dwmapi=n,b";
            foreach (var (name, value) in environment) start.Environment[name] = value;
            Process.Start(start);
            return Result.Ok();
        }
        catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception or IOException)
        {
            return Result.Fail($"Could not launch Ragnarock through Proton: {ex.Message}");
        }
    }

    private Result<SteamLaunchSettings> Resolve(string gameRoot)
    {
        var steamApps = Path.GetFullPath(Path.Combine(gameRoot, "..", ".."));
        if (!Directory.Exists(steamApps)) return Result<SteamLaunchSettings>.Fail("Could not locate Ragnarock's Steam library.");
        var steamRoot = _steam.FindSteamRoots().FirstOrDefault(root => Directory.Exists(Path.Combine(root, "steamapps", "common")));
        if (steamRoot is null) return Result<SteamLaunchSettings>.Fail("Could not locate the Steam installation.");

        var common = Path.Combine(steamRoot, "steamapps", "common");
        var runtime = new[] { "SteamLinuxRuntime_4", "SteamLinuxRuntime_sniper", "SteamLinuxRuntime_soldier" }
            .Select(name => Path.Combine(common, name)).FirstOrDefault(path => File.Exists(Path.Combine(path, "_v2-entry-point")));
        var proton = Directory.EnumerateDirectories(common, "Proton*")
            .Where(path => File.Exists(Path.Combine(path, "proton")))
            .OrderByDescending(Directory.GetLastWriteTimeUtc).FirstOrDefault();
        if (runtime is null || proton is null) return Result<SteamLaunchSettings>.Fail("Could not locate an installed Steam Linux Runtime and Proton build.");

        var wrapper = Path.Combine(steamRoot, "ubuntu12_32", "steam-launch-wrapper");
        var reaper = Path.Combine(steamRoot, "ubuntu12_32", "reaper");
        var runtimeEntryPoint = Path.Combine(runtime, "_v2-entry-point");
        if (!File.Exists(wrapper) || !File.Exists(reaper)) return Result<SteamLaunchSettings>.Fail("Steam's launch wrapper could not be found.");
        return Result<SteamLaunchSettings>.Ok(new SteamLaunchSettings(steamRoot,
            Path.Combine(steamApps, "compatdata", SteamLibraryDiscoverer.RagnarockSteamAppId), wrapper, reaper,
            runtimeEntryPoint, Path.Combine(proton, "proton")));
    }

    private static bool TryParseEnvironment(string? text, out Dictionary<string, string> values, out string? error)
    {
        values = new Dictionary<string, string>(StringComparer.Ordinal);
        error = null;
        var input = text ?? "";
        var position = 0;
        while (position < input.Length)
        {
            var match = EnvironmentAssignment.Match(input, position);
            if (!match.Success)
            {
                if (string.IsNullOrWhiteSpace(input[position..])) break;
                error = "Enter optional Proton settings as NAME=value pairs, before %command%.";
                return false;
            }
            values[match.Groups["name"].Value] = match.Groups["double"].Success ? match.Groups["double"].Value
                : match.Groups["single"].Success ? match.Groups["single"].Value : match.Groups["plain"].Value;
            position = match.Index + match.Length;
        }
        return true;
    }

    private static IReadOnlyList<string> SplitArguments(string? text) => string.IsNullOrWhiteSpace(text)
        ? []
        : ArgumentToken.Matches(text).Select(match => match.Value.Trim('"', '\'')).ToList();

    private static string Join(params string?[] values) =>
        string.Join(' ', values.Where(value => !string.IsNullOrWhiteSpace(value)).Select(value => value!.Trim()));

    private sealed record SteamLaunchSettings(string SteamRoot, string CompatData, string Wrapper, string Reaper, string RuntimeEntryPoint, string Proton);
}
