using System.Text;
using System.Text.RegularExpressions;
using RagnaModManager.Core.Common;
using RagnaModManager.Platform.Proton;

namespace RagnaModManager.Platform.Steam;

public sealed record SteamLaunchOptionsStatus(
    bool Applicable,
    bool Configured,
    string? ConfigPath,
    string CurrentOptions,
    string RequiredOptions,
    string Message);

public sealed class SteamLaunchOptionsService
{
    private static readonly Regex AppBlock = new($"\\\"{SteamLibraryDiscoverer.RagnarockSteamAppId}\\\"\\s*\\{{", RegexOptions.Compiled);
    private static readonly Regex LaunchOptionsLine = new("(?m)^([ \\t]*)\\\"LaunchOptions\\\"[ \\t]+\\\"((?:\\\\.|[^\\\"])*)\\\"", RegexOptions.Compiled);

    private readonly SteamLibraryDiscoverer _steam;

    public SteamLaunchOptionsService() : this(new SteamLibraryDiscoverer())
    {
    }

    public SteamLaunchOptionsService(SteamLibraryDiscoverer steam)
    {
        _steam = steam;
    }

    public SteamLaunchOptionsStatus Inspect(string gameRoot)
    {
        if (OperatingSystem.IsWindows() || !_steam.IsLikelySteamInstall(gameRoot))
        {
            return new SteamLaunchOptionsStatus(false, true, null, "", ProtonLaunch.RequiredSteamLaunchOptions, "Steam launch options are not needed for this launch.");
        }

        var config = FindConfig();
        if (config is null)
        {
            return new SteamLaunchOptionsStatus(true, false, null, "", ProtonLaunch.RequiredSteamLaunchOptions, "Could not find your Steam launch-options file.");
        }

        var current = ReadOptions(config);
        return new SteamLaunchOptionsStatus(true, HasRequiredOptions(current), config, current, ProtonLaunch.RequiredSteamLaunchOptions,
            HasRequiredOptions(current) ? "Steam launch options are configured." : "Steam needs one launch option for UE4SS to load through Proton.");
    }

    public Result Configure(string gameRoot)
    {
        var status = Inspect(gameRoot);
        if (!status.Applicable || status.Configured) return Result.Ok();
        if (status.ConfigPath is null) return Result.Fail(status.Message);

        try
        {
            var text = File.ReadAllText(status.ConfigPath);
            var match = AppBlock.Match(text);
            if (!match.Success) return Result.Fail("The Ragnarock entry was not found in Steam's local configuration.");

            var blockStart = match.Index + match.Length;
            var blockEnd = FindBlockEnd(text, blockStart);
            if (blockEnd < 0) return Result.Fail("Steam's local configuration has an invalid Ragnarock entry.");

            var block = text[blockStart..blockEnd];
            var launchMatch = LaunchOptionsLine.Match(block);
            var current = launchMatch.Success ? Unescape(launchMatch.Groups[2].Value) : "";
            var updated = EnsureRequiredOptions(current);
            var updatedBlock = launchMatch.Success
                ? block[..launchMatch.Index] + launchMatch.Groups[1].Value + "\"LaunchOptions\"\t\"" + Escape(updated) + "\"" + block[(launchMatch.Index + launchMatch.Length)..]
                : block + Environment.NewLine + "\t\t\t\"LaunchOptions\"\t\"" + Escape(updated) + "\"";

            var result = text[..blockStart] + updatedBlock + text[blockEnd..];
            var temporary = status.ConfigPath + ".rmm.tmp";
            File.WriteAllText(temporary, result, new UTF8Encoding(false));
            File.Move(temporary, status.ConfigPath, overwrite: true);
            return Result.Ok();
        }
        catch (IOException ex)
        {
            return Result.Fail($"Could not update Steam launch options: {ex.Message}");
        }
    }

    private string? FindConfig()
    {
        foreach (var root in _steam.FindSteamRoots())
        {
            var userdata = Path.Combine(root, "userdata");
            if (!Directory.Exists(userdata)) continue;
            foreach (var account in Directory.EnumerateDirectories(userdata))
            {
                var config = Path.Combine(account, "config", "localconfig.vdf");
                if (File.Exists(config) && AppBlock.IsMatch(File.ReadAllText(config))) return config;
            }
        }

        return null;
    }

    private static string ReadOptions(string path)
    {
        var text = File.ReadAllText(path);
        var app = AppBlock.Match(text);
        if (!app.Success) return "";
        var end = FindBlockEnd(text, app.Index + app.Length);
        if (end < 0) return "";
        var match = LaunchOptionsLine.Match(text[(app.Index + app.Length)..end]);
        return match.Success ? Unescape(match.Groups[2].Value) : "";
    }

    private static bool HasRequiredOptions(string options) =>
        options.Contains("WINEDLLOVERRIDES=\"dwmapi=n,b\"", StringComparison.Ordinal) &&
        options.Contains("%command%", StringComparison.Ordinal) &&
        Regex.IsMatch(options, "(^|\\s)-nohmd(\\s|$)");

    private static string EnsureRequiredOptions(string current)
    {
        var withoutOverride = Regex.Replace(current, "WINEDLLOVERRIDES=\\\"[^\\\"]*\\\"\\s*", "", RegexOptions.IgnoreCase);
        var withoutNoHmd = Regex.Replace(withoutOverride, "(^|\\s)-nohmd(?=\\s|$)", " ", RegexOptions.IgnoreCase).Trim();
        var commandIndex = withoutNoHmd.IndexOf("%command%", StringComparison.Ordinal);
        var tail = commandIndex >= 0 ? withoutNoHmd[(commandIndex + "%command%".Length)..].Trim() : withoutNoHmd;
        return string.IsNullOrWhiteSpace(tail)
            ? ProtonLaunch.RequiredSteamLaunchOptions
            : $"WINEDLLOVERRIDES=\"dwmapi=n,b\" %command% -nohmd {tail}";
    }

    private static int FindBlockEnd(string text, int start)
    {
        var depth = 1;
        for (var index = start; index < text.Length; index++)
        {
            if (text[index] == '{') depth++;
            else if (text[index] == '}' && --depth == 0) return index;
        }

        return -1;
    }

    private static string Escape(string value) => value.Replace("\\", "\\\\").Replace("\"", "\\\"");
    private static string Unescape(string value) => value.Replace("\\\"", "\"").Replace("\\\\", "\\");
}
