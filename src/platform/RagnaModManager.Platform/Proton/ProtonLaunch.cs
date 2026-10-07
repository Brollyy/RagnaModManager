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

    private static readonly Regex LaunchRecord = new(
        @"--\s+'(?<runtime>[^']+)'/_v2-entry-point\s+--verb=\S+\s+--\s+'(?<proton>[^']+)/proton\s+waitforexitandrun\s+'(?<exe>[^']+)'(?<args>.*)$",
        RegexOptions.Compiled);
    private static readonly Regex EnvironmentAssignment = new(
        @"\G\s*(?<name>[A-Za-z_][A-Za-z0-9_]*)=(?:""(?<double>[^""]*)""|'(?<single>[^']*)'|(?<plain>[^\s]+))",
        RegexOptions.Compiled);

    private readonly SteamLibraryDiscoverer _steam;

    public ProtonLaunch() : this(new SteamLibraryDiscoverer())
    {
    }

    public ProtonLaunch(SteamLibraryDiscoverer steam)
    {
        _steam = steam;
    }

    public static string BuildSteamLaunchOptions(string? extraArguments) =>
        BuildSteamLaunchOptions(extraArguments, null);

    public static string BuildSteamLaunchOptions(string? userOptionalArguments, string? launchOptionArguments) =>
        BuildTemplateStatic(userOptionalArguments, launchOptionArguments);

    private static string BuildTemplateStatic(string? userOptionalArguments, string? launchOptionArguments)
    {
        if (OperatingSystem.IsWindows()) return Join(userOptionalArguments, launchOptionArguments);

        var required = RequiredSteamLaunchOptions;
        var commandIndex = required.IndexOf("%command%", StringComparison.Ordinal);
        var prefix = commandIndex >= 0 ? required[..commandIndex].Trim() : required;
        var command = commandIndex >= 0 ? required[commandIndex..] : "%command%";
        return string.Join(' ', new[] { prefix, userOptionalArguments, command, launchOptionArguments }
            .Where(value => !string.IsNullOrWhiteSpace(value)).Select(value => value!.Trim()));
    }

    public LaunchPlan BuildPlan(string? executablePath, bool preferSteamProtocol, string? extraArguments = null, int? steamLaunchOption = null)
    {
        var gameArguments = string.IsNullOrWhiteSpace(extraArguments) ? "" : extraArguments.Trim();
        var launchUri = steamLaunchOption is int option
            ? $"steam://launch/{SteamLibraryDiscoverer.RagnarockSteamAppId}/option{option}"
            : $"steam://rungameid/{SteamLibraryDiscoverer.RagnarockSteamAppId}";
        if (preferSteamProtocol || steamLaunchOption.HasValue || string.IsNullOrWhiteSpace(executablePath))
        {
            return new LaunchPlan(launchUri, true, executablePath, gameArguments, BuildSteamLaunchOptions(extraArguments));
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

            if (string.IsNullOrWhiteSpace(plan.ExecutablePath))
                return Result.Fail("No executable path is available for direct launch.");

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
        if (!TryParseEnvironment(userOptionalArguments, out var userEnvironment, out var parseError))
        {
            return Result.Fail(parseError!);
        }

        var launch = Resolve(gameRoot);
        if (!launch.Success || launch.Value is null)
        {
            return Result.Fail(launch.Error ?? "Could not find Steam's Proton runtime for Ragnarock.");
        }

        try
        {
            var settings = launch.Value;
            var start = new ProcessStartInfo
            {
                FileName = settings.WrapperPath,
                WorkingDirectory = gameRoot,
                UseShellExecute = false
            };

            if (settings.WrapperAvailable)
            {
                start.ArgumentList.Add("--");
                start.ArgumentList.Add(settings.ReaperPath);
                start.ArgumentList.Add("SteamLaunch");
                start.ArgumentList.Add($"AppId={SteamLibraryDiscoverer.RagnarockSteamAppId}");
                start.ArgumentList.Add("--");
            }

            start.ArgumentList.Add(settings.RuntimeEntryPoint);
            start.ArgumentList.Add("--verb=waitforexitandrun");
            start.ArgumentList.Add("--");
            start.ArgumentList.Add(settings.ProtonExecutable);
            start.ArgumentList.Add("waitforexitandrun");
            start.ArgumentList.Add(executablePath);
            foreach (var argument in SplitArguments(launchOptionArguments))
            {
                start.ArgumentList.Add(argument);
            }

            start.Environment["STEAM_COMPAT_CLIENT_INSTALL_PATH"] = settings.SteamRoot;
            start.Environment["STEAM_COMPAT_DATA_PATH"] = settings.CompatDataPath;
            start.Environment["STEAM_COMPAT_APP_ID"] = SteamLibraryDiscoverer.RagnarockSteamAppId;
            start.Environment["SteamAppId"] = SteamLibraryDiscoverer.RagnarockSteamAppId;
            start.Environment["SteamGameId"] = SteamLibraryDiscoverer.RagnarockSteamAppId;
            start.Environment["WINEDLLOVERRIDES"] = "dwmapi=n,b";
            foreach (var (name, value) in userEnvironment)
            {
                start.Environment[name] = value;
            }

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
        var steamAppsPath = Path.GetFullPath(Path.Combine(gameRoot, "..", ".."));
        var steamRoot = Directory.GetParent(steamAppsPath)?.FullName;
        if (steamRoot is null || !File.Exists(Path.Combine(steamRoot, "steamapps", "appmanifest_1345820.acf")))
        {
            var candidates = _steam.FindSteamRoots();
            steamRoot = candidates.FirstOrDefault(root => Directory.Exists(Path.Combine(root, "steamapps", "common")));
            steamAppsPath = steamRoot is null ? "" : Path.Combine(steamRoot, "steamapps");
        }

        if (string.IsNullOrWhiteSpace(steamRoot))
        {
            return Result<SteamLaunchSettings>.Fail("Could not locate the Steam installation for Ragnarock.");
        }

        var runtimeAndProton = FindLastSteamLaunch(steamRoot) ?? FindFallbackRuntimeAndProton(steamRoot);
        if (runtimeAndProton is null)
        {
            return Result<SteamLaunchSettings>.Fail("Could not locate an installed Steam Linux Runtime and Proton build.");
        }

        var (runtimePath, protonPath) = runtimeAndProton.Value;
        var compatDataPath = Path.Combine(steamAppsPath, "compatdata", SteamLibraryDiscoverer.RagnarockSteamAppId);
        if (!Directory.Exists(compatDataPath))
        {
            var library = _steam.FindLibraryFolders()
                .Select(path => Path.Combine(path, "compatdata", SteamLibraryDiscoverer.RagnarockSteamAppId))
                .FirstOrDefault(Directory.Exists);
            if (library is not null) compatDataPath = library;
        }

        var wrapper = Path.Combine(steamRoot, "ubuntu12_32", "steam-launch-wrapper");
        var reaper = Path.Combine(steamRoot, "ubuntu12_32", "reaper");
        var runtimeEntryPoint = Path.Combine(runtimePath, "_v2-entry-point");
        var protonExecutable = Path.Combine(protonPath, "proton");
        if (!File.Exists(runtimeEntryPoint) || !File.Exists(protonExecutable))
        {
            return Result<SteamLaunchSettings>.Fail("Steam's configured Proton or Linux Runtime files could not be found.");
        }

        return Result<SteamLaunchSettings>.Ok(new SteamLaunchSettings(
            steamRoot,
            compatDataPath,
            runtimeEntryPoint,
            protonExecutable,
            File.Exists(wrapper) && File.Exists(reaper) ? wrapper : runtimeEntryPoint,
            reaper,
            File.Exists(wrapper) && File.Exists(reaper)));
    }

    private static (string RuntimePath, string ProtonPath)? FindLastSteamLaunch(string steamRoot)
    {
        var logs = new[]
        {
            Path.Combine(steamRoot, "logs", "gameprocess_log.txt"),
            Path.Combine(steamRoot, "logs", "gameprocess_log.previous.txt")
        };

        foreach (var log in logs.Where(File.Exists).OrderByDescending(File.GetLastWriteTimeUtc))
        {
            foreach (var line in File.ReadLines(log).Reverse())
            {
                if (!line.Contains($"AppID {SteamLibraryDiscoverer.RagnarockSteamAppId} adding PID", StringComparison.Ordinal)) continue;
                var match = LaunchRecord.Match(line);
                if (!match.Success) continue;
                var runtime = match.Groups["runtime"].Value;
                var proton = match.Groups["proton"].Value;
                if (Directory.Exists(runtime) && Directory.Exists(proton)) return (runtime, proton);
            }
        }

        return null;
    }

    private static (string RuntimePath, string ProtonPath)? FindFallbackRuntimeAndProton(string steamRoot)
    {
        var common = Path.Combine(steamRoot, "steamapps", "common");
        var runtime = new[] { "SteamLinuxRuntime_4", "SteamLinuxRuntime_sniper", "SteamLinuxRuntime_soldier" }
            .Select(name => Path.Combine(common, name))
            .FirstOrDefault(path => File.Exists(Path.Combine(path, "_v2-entry-point")));
        if (runtime is null) return null;

        var proton = Directory.Exists(common)
            ? Directory.EnumerateDirectories(common, "Proton*")
                .Where(path => File.Exists(Path.Combine(path, "proton")))
                .OrderByDescending(GetProtonBuildTimestamp)
                .FirstOrDefault()
            : null;
        return proton is null ? null : (runtime, proton);
    }

    private static long GetProtonBuildTimestamp(string protonDirectory)
    {
        var versionFile = Path.Combine(protonDirectory, "version");
        if (!File.Exists(versionFile)) return 0;
        var firstPart = File.ReadLines(versionFile).FirstOrDefault()?.Split(' ', 2)[0];
        return long.TryParse(firstPart, out var timestamp) ? timestamp : File.GetLastWriteTimeUtc(versionFile).Ticks;
    }

    private static bool TryParseEnvironment(string? text, out Dictionary<string, string> values, out string? error)
    {
        values = new Dictionary<string, string>(StringComparer.Ordinal);
        error = null;
        var remaining = text ?? "";
        var position = 0;
        while (position < remaining.Length)
        {
            var match = EnvironmentAssignment.Match(remaining, position);
            if (!match.Success)
            {
                if (string.IsNullOrWhiteSpace(remaining[position..])) break;
                error = "Enter optional Proton settings as NAME=value pairs, before %command%.";
                return false;
            }

            var value = match.Groups["double"].Success ? match.Groups["double"].Value
                : match.Groups["single"].Success ? match.Groups["single"].Value
                : match.Groups["plain"].Value;
            values[match.Groups["name"].Value] = value;
            position = match.Index + match.Length;
        }

        return true;
    }

    private static IReadOnlyList<string> SplitArguments(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return [];
        return Regex.Matches(text, @"[^\s""']+|""[^""]*""|'[^']*'")
            .Select(match => match.Value.Trim('"', '\''))
            .ToList();
    }

    private static string Join(params string?[] values) =>
        string.Join(' ', values.Where(value => !string.IsNullOrWhiteSpace(value)).Select(value => value!.Trim()));

    private sealed record SteamLaunchSettings(
        string SteamRoot,
        string CompatDataPath,
        string RuntimeEntryPoint,
        string ProtonExecutable,
        string WrapperPath,
        string ReaperPath,
        bool WrapperAvailable);
}
