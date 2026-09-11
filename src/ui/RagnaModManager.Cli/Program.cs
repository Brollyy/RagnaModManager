using System.Text.Json;
using RagnaModManager.Core.Compatibility;
using RagnaModManager.Core.Database;
using RagnaModManager.Core.Deployment;
using RagnaModManager.Core.Diagnostics;
using RagnaModManager.Core.Logging;
using RagnaModManager.Core.Packages;
using RagnaModManager.Core.Platform;
using RagnaModManager.Platform.Folders;
using RagnaModManager.Ragnarock.DeploymentRules;
using RagnaModManager.Ragnarock.Compatibility;
using RagnaModManager.Ragnarock.Detection;
using RagnaModManager.Ragnarock.Launch;
using RagnaModManager.Ragnarock.Ue4ss;

var app = AppBootstrap.Create();
return app.Run(args);

internal sealed class AppBootstrap
{
    private readonly AppPaths _paths;
    private readonly ManagerDatabase _database;
    private readonly AppLogger _logger;
    private readonly OfficialCatalogService _officialCatalog;
    private readonly RagnarockDetector _detector = new();
    private readonly Ue4ssService _ue4ss = new();
    private readonly RagnarockDeploymentRules _rules = new();

    private AppBootstrap(AppPaths paths, ManagerDatabase database, AppLogger logger)
    {
        _paths = paths;
        _database = database;
        _logger = logger;
        _officialCatalog = new OfficialCatalogService(paths, database, logger);
    }

    public static AppBootstrap Create()
    {
        var root = Environment.GetEnvironmentVariable("RMM_DATA_DIR");
        var paths = string.IsNullOrWhiteSpace(root) ? AppPaths.CreateDefault() : AppPaths.Create(root);
        var logger = new AppLogger(paths.Logs);
        var database = new ManagerDatabase(paths);
        database.Initialize();
        var active = database.GetActiveProfile();
        if (!database.HasAppliedProfileSnapshot(active.Id))
            database.CaptureAppliedProfileSnapshot(active.Id);
        logger.Info("RagnaModManager CLI started.");
        return new AppBootstrap(paths, database, logger);
    }

    public int Run(string[] args)
    {
        if (args.Length == 0)
        {
            return Interactive();
        }

        try
        {
            return args[0] switch
            {
                "init" => Init(),
                "interactive" or "ui" => Interactive(),
                "detect" => Detect(),
                "set-game" => Require(args, 2, () => SetGame(args[1])),
                "inspect" => Require(args, 2, () => InspectPackage(args[1])),
                "import" => Require(args, 2, () => Import(args[1])),
                "mods" => Mods(),
                "enable" => Require(args, 2, () => SetEnabled(args[1], enabled: true, ParsePriority(args))),
                "disable" => Require(args, 2, () => SetEnabled(args[1], enabled: false, ParsePriority(args))),
                "profile" => Profile(args),
                "catalog" or "registry" => Catalog(args),
                "version" => Require(args, 3, () => SetVersion(args[1], args[2])),
                "revert" => Revert(),
                "preview" => WithGame(root => Preview(root)),
                "deploy" => WithGame(root => Deploy(root, args.Contains("--allow-warnings"))),
                "rollback" => Rollback(),
                "reset-deployment" => ResetDeployment(),
                "ue4ss" => Ue4ss(args),
                "compat" => WithGame(Compatibility),
                "launch-plan" => WithGame(LaunchPlan),
                "launch" => WithGame(root => Launch(root)),
                "open-data-folder" => OpenFolder(_paths.Root),
                "open-game-folder" => WithGame(OpenFolder),
                "diagnostics-json" => DiagnosticsJson(),
                "paths" => Paths(),
                "help" or "--help" or "-h" => Help(),
                _ => Unknown(args[0])
            };
        }
        catch (Exception ex)
        {
            _logger.Error(ex.ToString());
            Console.Error.WriteLine(ex.Message);
            return 1;
        }
    }

    private int Init()
    {
        Console.WriteLine($"Data: {_paths.Root}");
        Console.WriteLine($"Database: {_paths.DatabasePath}");
        Console.WriteLine($"Logs: {_paths.Logs}");
        return 0;
    }

    private int Interactive()
    {
        while (true)
        {
            Console.WriteLine();
            Console.WriteLine("RagnaModManager");
            Console.WriteLine("1. First-run setup");
            Console.WriteLine("2. Installed mods");
            Console.WriteLine("3. Import mod");
            Console.WriteLine("4. Deployment");
            Console.WriteLine("5. Settings");
            Console.WriteLine("6. Launch Ragnarock");
            Console.WriteLine("0. Exit");
            Console.Write("> ");

            switch (Console.ReadLine()?.Trim())
            {
                case "1":
                    InteractiveSetup();
                    break;
                case "2":
                    InteractiveInstalledMods();
                    break;
                case "3":
                    InteractiveImport();
                    break;
                case "4":
                    InteractiveDeployment();
                    break;
                case "5":
                    InteractiveSettings();
                    break;
                case "6":
                    WithGame(Launch);
                    break;
                case "0":
                case "":
                case null:
                    return 0;
                default:
                    Console.WriteLine("Unknown selection.");
                    break;
            }
        }
    }

    private void InteractiveSetup()
    {
        Console.WriteLine();
        Console.WriteLine("First-run setup");
        var detected = _detector.DetectFirstValid();
        if (detected is not null)
        {
            PrintInstall(detected);
            Console.Write("Use this path? [Y/n] ");
            var answer = Console.ReadLine();
            if (string.IsNullOrWhiteSpace(answer) || answer.StartsWith("y", StringComparison.OrdinalIgnoreCase))
            {
                SaveGame(detected);
                Console.WriteLine("Game path saved.");
                return;
            }
        }

        Console.Write("Enter Ragnarock folder path: ");
        var path = Console.ReadLine();
        if (!string.IsNullOrWhiteSpace(path))
        {
            SetGame(path);
        }
    }

    private void InteractiveInstalledMods()
    {
        Console.WriteLine();
        Console.WriteLine("Installed mods");
        Mods();
        Console.Write("Enter mod id to toggle, or blank to return: ");
        var modId = Console.ReadLine();
        if (string.IsNullOrWhiteSpace(modId))
        {
            return;
        }

        var profile = _database.GetActiveProfile();
        var state = _database.GetProfileMods(profile.Id).FirstOrDefault(m => string.Equals(m.ModId, modId, StringComparison.OrdinalIgnoreCase));
        SetEnabled(modId, !(state?.Enabled ?? false), state?.Priority ?? 0);
    }

    private void InteractiveImport()
    {
        Console.WriteLine();
        Console.WriteLine("Import mod");
        Console.Write("Path to .rmod package: ");
        var path = Console.ReadLine();
        if (!string.IsNullOrWhiteSpace(path))
        {
            Import(path);
        }
    }

    private void InteractiveDeployment()
    {
        Console.WriteLine();
        Console.WriteLine("Deployment");
        WithGame(Preview);
        Console.Write("Deploy now? [y/N] ");
        var answer = Console.ReadLine();
        if (answer?.StartsWith("y", StringComparison.OrdinalIgnoreCase) == true)
        {
            WithGame(root => Deploy(root, allowWarnings: false));
        }
    }

    private void InteractiveSettings()
    {
        Console.WriteLine();
        Console.WriteLine("Settings");
        Paths();
        var game = _database.GetGame();
        Console.WriteLine($"Game path: {game?.InstallPath ?? "not configured"}");
        if (game is not null)
        {
            Ue4ss(["ue4ss", "status"]);
        }

        Console.Write("Enter new game path, or blank to return: ");
        var path = Console.ReadLine();
        if (!string.IsNullOrWhiteSpace(path))
        {
            SetGame(path);
        }
    }

    private int Detect()
    {
        var installs = _detector.FindCandidates().Select(_detector.Validate).ToList();
        if (installs.Count == 0)
        {
            Console.WriteLine("No Ragnarock Steam install candidates found. Use set-game <path>.");
            return 2;
        }

        foreach (var install in installs)
        {
            PrintInstall(install);
        }

        var best = installs.FirstOrDefault(i => i.IsValid);
        if (best is not null)
        {
            SaveGame(best);
            Console.WriteLine($"Saved game path: {best.Root}");
        }

        return best is null ? 2 : 0;
    }

    private int SetGame(string path)
    {
        var install = _detector.Validate(path);
        PrintInstall(install);
        SaveGame(install);
        return install.IsValid ? 0 : 2;
    }

    private int Import(string archivePath)
    {
        var importer = new PackageImporter(_paths, _database, _logger);
        var result = importer.Import(archivePath);
        if (!result.Success)
        {
            Console.Error.WriteLine(result.Error);
            return 1;
        }

        Console.WriteLine($"Imported {result.Value!.Name} ({result.Value.Id}) {result.Value.Version}");
        Console.WriteLine("Imported mods start disabled. Run enable <mod-id>.");
        return 0;
    }

    private int InspectPackage(string archivePath)
    {
        var result = new PackageInspector().Inspect(archivePath);
        if (!result.Success)
        {
            Console.Error.WriteLine(result.Error);
            return 1;
        }

        PrintInspection(result.Value!);
        return 0;
    }

    private int Mods()
    {
        var profile = _database.GetActiveProfile();
        var profileMods = _database.GetProfileMods(profile.Id).ToDictionary(m => m.ModId, StringComparer.OrdinalIgnoreCase);
        foreach (var mod in _database.GetMods())
        {
            profileMods.TryGetValue(mod.Id, out var state);
            Console.WriteLine($"{mod.Id}\t{state?.Version ?? mod.Version}\t{(state?.Enabled == true ? "enabled" : "disabled")}\tpriority={state?.Priority ?? 0}\t{mod.Name}");
        }

        return 0;
    }

    private int SetEnabled(string modId, bool enabled, int priority)
    {
        var mod = _database.GetMod(modId);
        if (mod is null)
        {
            Console.Error.WriteLine($"Unknown mod id: {modId}");
            return 1;
        }

        var profile = _database.GetActiveProfile();
        var existing = _database.GetProfileMods(profile.Id).FirstOrDefault(m => m.ModId.Equals(modId, StringComparison.OrdinalIgnoreCase));
        _database.SetProfileMod(profile.Id, modId, enabled, priority, existing?.Version ?? mod.Version);
        Console.WriteLine($"{(enabled ? "Enabled" : "Disabled")} {modId} in profile {profile.Id} with priority {priority}");
        return 0;
    }

    private int SetVersion(string modId, string version)
    {
        var mod = _database.GetMod(modId, version);
        if (mod is null)
        {
            Console.Error.WriteLine($"Version {version} of mod {modId} is not installed.");
            return 1;
        }

        var profile = _database.GetActiveProfile();
        var existing = _database.GetProfileMods(profile.Id).FirstOrDefault(m => m.ModId.Equals(modId, StringComparison.OrdinalIgnoreCase));
        _database.SetProfileMod(profile.Id, modId, existing?.Enabled == true, existing?.Priority ?? 0, mod.Version);
        Console.WriteLine($"Pinned {modId} to version {mod.Version} in profile {profile.Id}.");
        return 0;
    }

    private int Catalog(string[] args)
    {
        var loaded = _officialCatalog.LoadAsync().GetAwaiter().GetResult();
        if (!loaded.Success)
        {
            Console.Error.WriteLine(loaded.Error);
            return 1;
        }

        var catalog = loaded.Value!;
        if (args.Length == 1 || args[1] is "list" or "refresh")
        {
            foreach (var mod in catalog.Mods.OrderBy(mod => mod.Name, StringComparer.OrdinalIgnoreCase))
                Console.WriteLine($"{mod.Id}\t{mod.Latest?.Version ?? "—"}\t{mod.Name}");
            return 0;
        }

        if (args[1] == "install" && args.Length >= 3)
        {
            var mod = catalog.Mods.FirstOrDefault(item => item.Id.Equals(args[2], StringComparison.OrdinalIgnoreCase));
            if (mod is null)
            {
                Console.Error.WriteLine($"Community catalog does not contain mod: {args[2]}");
                return 1;
            }

            var requestedVersion = GetOption(args, "--version");
            var release = string.IsNullOrWhiteSpace(requestedVersion)
                ? mod.Latest
                : mod.Releases.FirstOrDefault(item => item.Version.Equals(requestedVersion, StringComparison.OrdinalIgnoreCase));
            if (release is null)
            {
                Console.Error.WriteLine($"Version {requestedVersion} of {mod.Name} is not in the community catalog.");
                return 1;
            }

            var result = _officialCatalog.DownloadAndImportAsync(mod, release).GetAwaiter().GetResult();
            if (!result.Success)
            {
                Console.Error.WriteLine(result.Error);
                return 1;
            }

            Console.WriteLine($"Installed {result.Value!.Name} {result.Value.Version} and any required dependencies.");
            return 0;
        }

        if (args[1] is "update" or "upgrade")
        {
            var installed = _database.GetMods();
            var updates = catalog.Mods
                .Select(mod => (Mod: mod, Current: installed.FirstOrDefault(item => item.Id.Equals(mod.Id, StringComparison.OrdinalIgnoreCase))))
                .Where(item => item.Current is not null && item.Mod.Latest is not null && SemanticVersion.IsNewer(item.Mod.Latest!.Version, item.Current!.Version))
                .ToList();
            foreach (var update in updates)
            {
                var latest = update.Mod.Latest!;
                var result = _officialCatalog.DownloadAndImportAsync(update.Mod, latest).GetAwaiter().GetResult();
                if (!result.Success)
                {
                    Console.Error.WriteLine(result.Error);
                    return 1;
                }
                Console.WriteLine($"Updated {update.Mod.Name} to {latest.Version}.");
            }
            if (updates.Count == 0) Console.WriteLine("All community mods are up to date.");
            return 0;
        }

        Console.Error.WriteLine("Usage: catalog [list|refresh] | catalog install <mod-id> [--version <version>] | catalog update");
        return 1;
    }

    private int Revert()
    {
        var profile = _database.GetActiveProfile();
        var result = _database.RestoreAppliedProfileSnapshot(profile.Id);
        if (!result.Success)
        {
            Console.Error.WriteLine(result.Error);
            return 1;
        }

        Console.WriteLine($"Reverted unapplied changes in profile {profile.Id}. Run deploy to apply a different saved setup.");
        return 0;
    }

    private int Profile(string[] args)
    {
        if (args.Length >= 2 && args[1] == "export")
        {
            if (args.Length < 3)
            {
                Console.Error.WriteLine("Usage: profile export <path> [profile-id]");
                return 1;
            }

            var exportProfile = args.Length >= 4 ? _database.GetProfile(args[3]) : _database.GetActiveProfile();
            if (exportProfile is null)
            {
                Console.Error.WriteLine($"Unknown profile: {args[3]}");
                return 1;
            }

            var result = _database.ExportProfile(exportProfile.Id, args[2]);
            Console.WriteLine(result.Success ? $"Exported profile {exportProfile.Id} to {args[2]}." : result.Error);
            return result.Success ? 0 : 1;
        }

        if (args.Length >= 2 && args[1] == "import")
        {
            if (args.Length < 5)
            {
                Console.Error.WriteLine("Usage: profile import <path> <id> <name>");
                return 1;
            }

            var result = _database.ImportProfile(args[2], args[3], args[4]);
            if (!result.Success)
            {
                Console.Error.WriteLine(result.Error);
                return 1;
            }
            _database.CaptureAppliedProfileSnapshot(result.Value!.Id);
            Console.WriteLine($"Imported profile {result.Value.Id} from {args[2]}.");
            return 0;
        }

        if (args.Length >= 2 && args[1] == "create")
        {
            if (args.Length < 4)
            {
                Console.Error.WriteLine("Usage: profile create <id> <name>");
                return 1;
            }

            _database.CreateProfile(args[2], args[3]);
            _database.CaptureAppliedProfileSnapshot(args[2]);
            Console.WriteLine($"Created profile {args[2]}.");
            return 0;
        }

        if (args.Length >= 2 && args[1] == "switch")
        {
            if (args.Length < 3)
            {
                Console.Error.WriteLine("Usage: profile switch <id>");
                return 1;
            }

            return SwitchProfile(args[2]);
        }

        var profile = _database.GetActiveProfile();
        foreach (var existing in _database.GetProfiles())
        {
            Console.WriteLine($"{existing.Id}\t{existing.Name}\tactive={existing.IsActive}");
        }

        Console.WriteLine();
        Console.WriteLine($"Active profile mods: {profile.Id}");
        foreach (var mod in _database.GetProfileMods(profile.Id))
        {
            Console.WriteLine($"{mod.ModId}\tenabled={mod.Enabled}\tversion={mod.Version ?? "latest"}\tpriority={mod.Priority}");
        }

        return 0;
    }

    private int Preview(string gameRoot)
    {
        var service = CreateDeploymentService();
        var result = service.Preview(gameRoot);
        if (!result.Success)
        {
            Console.Error.WriteLine(result.Error);
            return 1;
        }

        PrintPlan(result.Value!);
        return result.Value!.CanDeploy ? 0 : 2;
    }

    private int Deploy(string gameRoot, bool allowWarnings)
    {
        var service = CreateDeploymentService();
        var preview = service.Preview(gameRoot);
        if (preview.Success)
        {
            PrintPlan(preview.Value!);
        }

        var result = service.Deploy(gameRoot, allowWarnings);
        if (!result.Success)
        {
            Console.Error.WriteLine(result.Error);
            return 1;
        }

        Console.WriteLine("Deployment complete.");
        return 0;
    }

    private int Rollback()
    {
        var result = CreateDeploymentService().RollbackLatest();
        if (!result.Success)
        {
            Console.Error.WriteLine(result.Error);
            return 1;
        }

        Console.WriteLine("Rollback complete.");
        return 0;
    }

    private int ResetDeployment()
    {
        var result = CreateDeploymentService().ResetDeployment();
        if (!result.Success)
        {
            Console.Error.WriteLine(result.Error);
            return 1;
        }

        Console.WriteLine("Deployment reset complete.");
        return 0;
    }

    private int SwitchProfile(string profileId)
    {
        _database.SetActiveProfile(profileId);
        var game = _database.GetGame();
        if (game is null)
        {
            Console.WriteLine($"Active profile: {profileId}");
            Console.WriteLine("No Ragnarock path is configured, so deployment was skipped.");
            return 0;
        }

        var result = CreateDeploymentService().Deploy(game.InstallPath);
        if (!result.Success)
        {
            Console.Error.WriteLine($"Active profile: {profileId}");
            Console.Error.WriteLine($"Redeploy failed: {result.Error}");
            return 1;
        }

        Console.WriteLine($"Active profile: {profileId}");
        Console.WriteLine("Redeployed game files.");
        return 0;
    }

    private int Ue4ss(string[] args)
    {
        if (args.Length < 2)
        {
            Console.WriteLine("Usage: ue4ss status | ue4ss install <zip>");
            return 1;
        }

        return WithGame(root =>
        {
            if (args[1] == "status")
            {
                var status = _ue4ss.Detect(root);
                Console.WriteLine($"Installed: {status.Installed}");
                Console.WriteLine($"Layout: {status.Layout}");
                Console.WriteLine($"Version: {status.Version ?? "unknown"}");
                foreach (var diagnostic in status.Diagnostics)
                {
                    Console.WriteLine($"- {diagnostic}");
                }

                return status.Installed ? 0 : 2;
            }

            if (args[1] == "install" && args.Length >= 3)
            {
                var result = _ue4ss.InstallFromZip(root, args[2]);
                if (!result.Success)
                {
                    Console.Error.WriteLine(result.Error);
                    return 1;
                }

                Console.WriteLine("UE4SS installed.");
                return 0;
            }

            Console.WriteLine("Usage: ue4ss status | ue4ss install <zip>");
            return 1;
        });
    }

    private int Launch(string gameRoot)
    {
        var result = new RagnarockLauncher().Launch(gameRoot);
        if (!result.Success)
        {
            Console.Error.WriteLine(result.Error);
            return 1;
        }

        Console.WriteLine("Launch requested.");
        return 0;
    }

    private int LaunchPlan(string gameRoot)
    {
        var plan = new RagnarockLauncher().BuildLaunchPlan(gameRoot);
        Console.WriteLine($"Command: {plan.DisplayCommand}");
        Console.WriteLine($"Steam protocol: {plan.UsesSteamProtocol}");
        Console.WriteLine($"Executable: {plan.ExecutablePath ?? "not found"}");
        Console.WriteLine($"Game arguments: {plan.GameArguments}");
        Console.WriteLine($"Steam launch options: {plan.SteamLaunchOptions}");
        return 0;
    }

    private int Compatibility(string gameRoot)
    {
        var report = new RagnarockCompatibilityChecker().Check(gameRoot);
        Console.WriteLine($"Can manage: {report.CanManage}");
        PrintGroup("Errors", report.Errors);
        PrintGroup("Warnings", report.Warnings);
        PrintGroup("Info", report.Info);
        return report.CanManage ? 0 : 2;
    }

    private int Paths()
    {
        Console.WriteLine($"Root: {_paths.Root}");
        Console.WriteLine($"Database: {_paths.DatabasePath}");
        Console.WriteLine($"Mod library: {_paths.ModLibrary}");
        Console.WriteLine($"Deployment: {_paths.Deployment}");
        Console.WriteLine($"Logs: {_paths.Logs}");
        return 0;
    }

    private int OpenFolder(string path)
    {
        var opener = new FolderOpener();
        var result = opener.Open(path);
        if (!result.Success)
        {
            Console.Error.WriteLine(result.Error);
            Console.Error.WriteLine($"Command would be: {opener.BuildCommand(path).DisplayCommand}");
            return 1;
        }

        Console.WriteLine($"Opened: {path}");
        return 0;
    }

    private int DiagnosticsJson()
    {
        var diagnostics = BuildDiagnostics();
        Console.WriteLine(JsonSerializer.Serialize(diagnostics, new JsonSerializerOptions
        {
            WriteIndented = true,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        }));
        return 0;
    }

    private int WithGame(Func<string, int> action)
    {
        var game = _database.GetGame();
        if (game is null)
        {
            Console.Error.WriteLine("No Ragnarock path is configured. Run detect or set-game <path>.");
            return 1;
        }

        return action(game.InstallPath);
    }

    private DeploymentService CreateDeploymentService()
    {
        var planner = new DeploymentPlanner(_database, _rules);
        return new DeploymentService(_paths, _database, planner, _rules, _logger);
    }

    private ManagerDiagnostics BuildDiagnostics()
    {
        var game = _database.GetGame();
        var profile = _database.GetActiveProfile();
        var compatibility = game is null ? null : new RagnarockCompatibilityChecker().Check(game.InstallPath);
        var ue4ss = game is null ? null : _ue4ss.Detect(game.InstallPath);
        var launchPlan = game is null ? null : new RagnarockLauncher().BuildLaunchPlan(game.InstallPath);
        return new ManagerDiagnostics(
            _paths.Root,
            _paths.DatabasePath,
            _paths.ModLibrary,
            _paths.CurrentDeploymentPath,
            game,
            profile,
            _database.GetMods(),
            _database.GetProfileMods(profile.Id),
            _database.GetDeployedFiles(profile.Id),
            compatibility,
            ue4ss,
            launchPlan);
    }

    private void SaveGame(RagnarockInstall install)
    {
        _database.UpsertGame(new GameRecord("ragnarock", "Ragnarock", install.Root, install.ExecutablePath, null, OperatingSystem.IsWindows() ? "windows" : "linux"));
    }

    private static void PrintInstall(RagnarockInstall install)
    {
        Console.WriteLine($"{install.Root}");
        Console.WriteLine($"  Valid: {install.IsValid}");
        Console.WriteLine($"  Executable: {install.ExecutablePath ?? "not found"}");
        foreach (var diagnostic in install.Diagnostics)
        {
            Console.WriteLine($"  - {diagnostic}");
        }
    }

    private static void PrintPlan(DeploymentPlan plan)
    {
        Console.WriteLine($"Profile: {plan.ProfileId}");
        Console.WriteLine($"Files: {plan.Items.Count}");
        foreach (var item in plan.Items)
        {
            Console.WriteLine($"{item.ModId}\t{item.FileType}\t{item.SourcePath} -> {item.TargetPath}");
        }

        foreach (var warning in plan.Warnings)
        {
            Console.WriteLine($"Warning: {warning}");
        }

        foreach (var conflict in plan.Conflicts)
        {
            Console.WriteLine($"{(conflict.BlocksDeployment ? "Blocking conflict" : "Warning conflict")}: {conflict.Message}");
        }
    }

    private static void PrintInspection(PackageInspection inspection)
    {
        var manifest = inspection.Manifest;
        Console.WriteLine($"Package: {inspection.ArchivePath}");
        Console.WriteLine($"Mod: {manifest.Name} ({manifest.Id}) {manifest.Version}");
        Console.WriteLine($"Author: {manifest.Author ?? "unknown"}");
        Console.WriteLine($"Game: {manifest.Game}");
        Console.WriteLine($"Files declared: {manifest.Files.Count}");
        Console.WriteLine($"Archive entries: {inspection.Entries.Count}");
        Console.WriteLine($"Uncompressed bytes: {inspection.TotalUncompressedBytes}");
        foreach (var file in manifest.Files)
        {
            Console.WriteLine($"- {file.Type}: {file.Source}");
        }
    }

    private static int ParsePriority(string[] args)
    {
        var index = Array.IndexOf(args, "--priority");
        return index >= 0 && args.Length > index + 1 && int.TryParse(args[index + 1], out var priority) ? priority : 0;
    }

    private static string? GetOption(string[] args, string option)
    {
        var index = Array.IndexOf(args, option);
        return index >= 0 && args.Length > index + 1 ? args[index + 1] : null;
    }

    private static int Require(string[] args, int count, Func<int> action)
    {
        if (args.Length < count)
        {
            PrintHelp();
            return 1;
        }

        return action();
    }

    private static int Unknown(string command)
    {
        Console.Error.WriteLine($"Unknown command: {command}");
        PrintHelp();
        return 1;
    }

    private static int Help()
    {
        PrintHelp();
        return 0;
    }

    private static void PrintHelp()
    {
        Console.WriteLine("""
            RagnaModManager CLI

            Commands:
              init                         Create data folders and manager.db
              interactive                  Open terminal UI screens
              detect                       Find and save a Ragnarock install
              set-game <path>              Save a manual Ragnarock folder
              inspect <file.rmod>          Validate and show package contents
              import <file.rmod>           Validate and install a package
              mods                         List installed mods
              enable <mod-id> [--priority n]
              disable <mod-id> [--priority n]
              version <mod-id> <version>   Pin a mod version in the active profile
              profile                      Show profiles and active profile state
              profile create <id> <name>   Create a profile
              profile switch <id>          Switch active profile
              profile export <path> [id]   Export a profile with version pins
              profile import <path> <id> <name>
                                           Import a profile with version pins
              catalog [list|refresh]       List community catalog mods
              catalog install <id> [--version <version>]
                                           Install a catalog mod and dependencies
              catalog update               Update installed catalog mods
              revert                       Revert unapplied active-profile changes
              preview                      Show deployment plan and conflicts
              deploy [--allow-warnings]    Copy enabled files and cleanup stale files
              rollback                      Restore the latest deployment backup
              reset-deployment              Remove manager-owned deployed files
              ue4ss status
              ue4ss install <zip>
              compat                       Show game-folder compatibility diagnostics
              launch-plan                  Show direct/Steam launch command
              launch                       Launch Ragnarock executable
              open-data-folder             Open manager data folder
              open-game-folder             Open configured Ragnarock folder
              diagnostics-json             Print manager diagnostics as JSON
              paths                        Show manager data paths

            Running without a command opens the terminal UI.
            Set RMM_DATA_DIR to override the platform app-data location.
            """);
    }

    private static void PrintGroup(string label, IReadOnlyList<string> values)
    {
        Console.WriteLine(label + ":");
        if (values.Count == 0)
        {
            Console.WriteLine("  none");
            return;
        }

        foreach (var value in values)
        {
            Console.WriteLine("  - " + value);
        }
    }
}
