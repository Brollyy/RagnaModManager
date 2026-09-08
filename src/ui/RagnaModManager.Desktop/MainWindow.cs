using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using System.Text.Json;
using RagnaModManager.Core.Checksums;
using RagnaModManager.Core.Compatibility;
using RagnaModManager.Core.Database;
using RagnaModManager.Core.Deployment;
using RagnaModManager.Core.Logging;
using RagnaModManager.Core.Manifests;
using RagnaModManager.Core.Packages;
using RagnaModManager.Core.Platform;
using RagnaModManager.Platform.Folders;
using RagnaModManager.Ragnarock.Compatibility;
using RagnaModManager.Ragnarock.DeploymentRules;
using RagnaModManager.Ragnarock.Detection;
using RagnaModManager.Ragnarock.Launch;
using RagnaModManager.Ragnarock.Ue4ss;

namespace RagnaModManager.Desktop;

public sealed class MainWindow : Window
{
    private const string Ue4ssZipHelp = "If a mod asks for script support, choose its support ZIP here and the manager will install it for you.";

    private readonly AppPaths _paths;
    private readonly ManagerDatabase _database;
    private readonly AppLogger _logger;
    private readonly RagnarockDetector _detector = new();
    private readonly RagnarockDeploymentRules _rules = new();
    private readonly Ue4ssService _ue4ss = new();
    private readonly Ue4ssReleaseService _ue4ssReleases;
    private readonly OfficialCatalogService _officialCatalog;
    private readonly FolderOpener _folderOpener = new();

    private readonly ContentControl _body = new();
    private readonly TabControl _tabs = new();
    private readonly TextBlock _status = new() { TextWrapping = TextWrapping.Wrap };
    private readonly Border _pendingChangesBar = new();
    private TextBlock? _libraryStatus;
    private int _selectedTab;
    private bool _rebuildingTabs;
    private bool _changesPending;
    private string _modSearch = "";
    private string? _catalogLastChecked;
    private string _librarySearch = "";
    private readonly HashSet<string> _selectedCatalogMods = new(StringComparer.OrdinalIgnoreCase);
    private string _catalogSortColumn = "Name";
    private bool _catalogSortDescending;
    private string _launchArguments = "";
    private readonly HashSet<string> _selectedMods = new(StringComparer.OrdinalIgnoreCase);

    public MainWindow()
    {
        Title = "Ragna Mod Manager";
        Width = 1120;
        Height = 760;
        MinWidth = 900;
        MinHeight = 640;

        var root = Environment.GetEnvironmentVariable("RMM_DATA_DIR");
        _paths = string.IsNullOrWhiteSpace(root) ? AppPaths.CreateDefault() : AppPaths.Create(root);
        _logger = new AppLogger(_paths.Logs);
        _database = new ManagerDatabase(_paths);
        _ue4ssReleases = new Ue4ssReleaseService(_paths, ue4ss: _ue4ss);
        _officialCatalog = new OfficialCatalogService(_paths, _database, _logger);

        try
        {
            _database.Initialize();
            if (File.Exists(_paths.LaunchArgumentsPath)) _launchArguments = File.ReadAllText(_paths.LaunchArgumentsPath);
            if (File.Exists(_paths.CatalogLastCheckedPath)) _catalogLastChecked = File.ReadAllText(_paths.CatalogLastCheckedPath);
            _logger.Info("RagnaModManager desktop UI started.");
            BuildShell();
            ShowDashboard();
            _ = RefreshOfficialCatalog();
        }
        catch (Exception ex)
        {
            Content = Page("Startup failed", Text(ex.Message));
        }
    }

    private void BuildShell()
    {
        var root = new DockPanel { Margin = new Thickness(18) };

        var header = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("*,Auto"),
            Margin = new Thickness(0)
        };
        header.Children.Add(Cell(new StackPanel
        {
            Spacing = 2,
            Children =
            {
                new TextBlock { Text = "Ragna Mod Manager", FontSize = 28, FontWeight = FontWeight.SemiBold },
                new TextBlock
                {
                    Text = "Install, enable, and launch Ragnarock mods from one place.",
                    Foreground = Brushes.DimGray,
                    TextWrapping = TextWrapping.Wrap
                }
            }
        }, 0));

        var launch = PrimaryButton("Launch Ragnarock");
        launch.Click += (_, _) => LaunchGame();
        header.Children.Add(Cell(launch, 1));

        _tabs.SelectionChanged += (_, _) =>
        {
            if (!_rebuildingTabs)
            {
                _selectedTab = Math.Max(0, _tabs.SelectedIndex);
                _status.Text = "";
            }
        };

        var headerCard = new Border
        {
            Background = new SolidColorBrush(Color.Parse("#EEF6FF")),
            BorderBrush = new SolidColorBrush(Color.Parse("#C8DFF5")),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(8),
            Padding = new Thickness(16),
            Margin = new Thickness(0, 0, 0, 14),
            Child = header
        };
        DockPanel.SetDock(headerCard, Dock.Top);
        root.Children.Add(headerCard);

        _pendingChangesBar.IsVisible = false;
        _pendingChangesBar.Margin = new Thickness(0, 12, 0, 0);
        DockPanel.SetDock(_pendingChangesBar, Dock.Bottom);
        root.Children.Add(_pendingChangesBar);

        _status.Margin = new Thickness(0, 8, 0, 0);
        DockPanel.SetDock(_status, Dock.Bottom);
        root.Children.Add(_status);

        root.Children.Add(_body);
        Content = root;
    }

    private void ShowDashboard()
    {
        ShowDashboard(_selectedTab);
    }

    private void ShowDashboard(int selectedTab)
    {
        _selectedTab = Math.Max(0, selectedTab);
        var game = _database.GetGame();
        var active = _database.GetActiveProfile();
        var mods = _database.GetMods();
        var planResult = game is null ? null : CreateDeploymentService().Preview(game.InstallPath);
        if (planResult is { Success: true, Value: not null })
        {
            _changesPending = !IsDeploymentSynchronized(planResult.Value);
        }

        _rebuildingTabs = true;
        try
        {
            _tabs.Items.Clear();
            _tabs.Items.Add(Tab("Dashboard", BuildDashboardPage(game, active, mods, planResult)));
            _tabs.Items.Add(Tab("Browse", new ScrollViewer { Content = BuildOfficialCatalog(mods) }));
            _tabs.Items.Add(Tab("Mods", BuildModsPage(active, mods, planResult)));
            _tabs.Items.Add(Tab("Profiles", new ScrollViewer { Content = BuildProfiles(active) }));
            _tabs.Items.Add(Tab("Settings", BuildSettingsPage(game, planResult)));
            _tabs.SelectedIndex = Math.Min(_selectedTab, _tabs.Items.Count - 1);
            _body.Content = _tabs;
        }
        finally
        {
            _rebuildingTabs = false;
        }
        UpdatePendingChangesBar();
    }

    private Control BuildDashboardPage(GameRecord? game, ProfileRecord active, IReadOnlyList<ModRecord> mods, Core.Common.Result<DeploymentPlan>? planResult)
    {
        return new ScrollViewer
        {
            Content = new StackPanel { Spacing = 14, Children =
            {
                BuildQuickActions(game),
                BuildDashboardSummary(game, active, mods, planResult),
                BuildDashboardDeploymentStatus(game, planResult)
            }}
        };
    }

    private Control BuildDashboardDeploymentStatus(GameRecord? game, Core.Common.Result<DeploymentPlan>? planResult)
    {
        if (game is null || planResult is not { Success: true, Value: not null }) return new Border { IsVisible = false };
        var plan = planResult.Value;
        var synchronized = IsDeploymentSynchronized(plan);
        if (synchronized && plan.Warnings.Count == 0) return new Border { IsVisible = false };
        var content = new List<Control>
        {
            Text(synchronized ? "Profile applied" : "Profile not applied")
        };
        var conflicts = plan.Conflicts.Where(c => c.BlocksDeployment).Select(FriendlyDeploymentConflict).ToList();
        if (conflicts.Count > 0)
        {
            content.Add(Text(string.Join(Environment.NewLine, conflicts)));
        }
        var unmanagedWarning = plan.Warnings.FirstOrDefault(w => w.StartsWith("Found ", StringComparison.Ordinal));
        if (unmanagedWarning is not null)
        {
            var count = unmanagedWarning.Split(' ', StringSplitOptions.RemoveEmptyEntries)
                .FirstOrDefault(part => int.TryParse(part, out _));
            content.Add(Text($"Unmanaged game files: {count ?? "some"} found"));
            content.Add(MutedText("These files are outside the active profile. Open Settings to review or remove them."));
        }
        content.AddRange(plan.Warnings.Where(w => !w.StartsWith("Found ", StringComparison.Ordinal)).Select(w => MutedText("Notice: " + w)));
        if (!synchronized)
        {
            content.Add(MutedText("Select Apply Changes at the bottom of the window to make the game use this profile."));
        }
        return Section("Profile status", content.ToArray());
    }

    private Control BuildModsPage(ProfileRecord active, IReadOnlyList<ModRecord> mods, Core.Common.Result<DeploymentPlan>? planResult)
    {
        var import = PrimaryButton("Import Mod Package");
        import.Click += async (_, _) => await ImportModPackage();
        var search = new TextBox { Watermark = "Search by name or ID…", Text = _modSearch, MinWidth = 300 };
        search.KeyDown += (_, e) =>
        {
            if (e.Key == Key.Enter)
            {
                _modSearch = search.Text?.Trim() ?? "";
                ShowDashboard(2);
            }
        };
        var clearSearch = Button("×");
        clearSearch.MinWidth = 34;
        clearSearch.IsEnabled = !string.IsNullOrWhiteSpace(_modSearch);
        clearSearch.Click += (_, _) => { _modSearch = ""; ShowDashboard(2); };
        var searchBox = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto"), Children = { Cell(search, 0), Cell(clearSearch, 1) } };
        var enableAll = Button("Enable All");
        enableAll.Click += (_, _) => SetAllVisibleMods(active, mods, true);
        var disableAll = Button("Disable All");
        disableAll.Click += (_, _) => SetAllVisibleMods(active, mods, false);
        var enableSelected = Button("Enable Selected");
        enableSelected.Click += (_, _) => SetSelectedMods(active, true);
        var disableSelected = Button("Disable Selected");
        disableSelected.Click += (_, _) => SetSelectedMods(active, false);
        var removeSelected = Button("Remove Selected");
        removeSelected.Click += async (_, _) => await RemoveSelectedMods();
        var selectAll = Button("Select All");
        selectAll.Click += (_, _) => { foreach (var mod in mods) _selectedMods.Add(mod.Id); ShowDashboard(2); };
        var selectNone = Button("Select None");
        selectNone.Click += (_, _) => { _selectedMods.Clear(); ShowDashboard(2); };
        var page = new StackPanel { Spacing = 14, Children =
        {
            Text("These are the mods on your computer. Turn a mod on here to use it in the current profile."),
            Wrap(searchBox, selectAll, selectNone, enableAll, disableAll, enableSelected, disableSelected, removeSelected, import),
            MutedText("You can also drag and drop a .rmod package anywhere in this page."),
            BuildDependencyNotice(planResult),
            BuildModList(active, mods)
        }};
        DragDrop.SetAllowDrop(page, true);
        page.AddHandler(DragDrop.DropEvent, async (_, e) =>
        {
            var file = e.DataTransfer.TryGetFiles()?.FirstOrDefault(f => f.Name.EndsWith(".rmod", StringComparison.OrdinalIgnoreCase));
            if (file is not null) await ImportModPackage(file.Path.LocalPath);
        });
        return new ScrollViewer
        {
            Content = page
        };
    }

    private Control BuildDependencyNotice(Core.Common.Result<DeploymentPlan>? planResult)
    {
        var conflicts = planResult?.Success == true && planResult.Value is not null
            ? planResult.Value.Conflicts
                .Where(conflict => conflict.Kind is "missing-dependency" or "disabled-dependency" or "dependency-version" or "profile-version")
                .ToList()
            : [];
        if (conflicts.Count == 0)
        {
            return Card(new StackPanel
            {
                Spacing = 4,
                Children =
                {
                    new TextBlock { Text = "Dependencies", FontWeight = FontWeight.SemiBold },
                    MutedText("All dependencies for enabled mods are available.")
                }
            });
        }

        var details = new StackPanel { Spacing = 4 };
        details.Children.Add(new TextBlock
        {
            Text = "Dependencies need attention",
            FontWeight = FontWeight.SemiBold,
            Foreground = Brushes.Firebrick
        });
        details.Children.Add(Text("These enabled mods cannot be applied until their dependencies are installed, enabled, or updated."));
        foreach (var conflict in conflicts)
        {
            var action = conflict.RelatedModId is null ? null : BuildDependencyAction(conflict);
            details.Children.Add(action is null
                ? new TextBlock { Text = "• " + conflict.Message, Foreground = Brushes.Firebrick, TextWrapping = TextWrapping.Wrap }
                : Wrap(new TextBlock { Text = "• " + conflict.Message, Foreground = Brushes.Firebrick, TextWrapping = TextWrapping.Wrap }, action));
        }

        return Card(details);
    }

    private Control BuildDependencyAction(DeploymentConflict conflict)
    {
        var dependency = _database.GetMod(conflict.RelatedModId!);
        if (dependency is not null && conflict.Kind == "disabled-dependency")
        {
            var enable = Button($"Enable {dependency.Name}");
            enable.Click += (_, _) =>
            {
                _database.SetProfileMod(_database.GetActiveProfile().Id, dependency.Id, true, 0);
                _changesPending = true;
                SetStatus($"Enabled dependency {dependency.Name}.");
                ShowDashboard(2);
            };
            return enable;
        }

        var catalogMod = _officialCatalogResult?.Value?.Mods.FirstOrDefault(m =>
            string.Equals(m.Id, conflict.RelatedModId, StringComparison.OrdinalIgnoreCase));
        if (catalogMod?.Latest is not null)
        {
            var install = Button(dependency is null ? $"Install {catalogMod.Name}" : $"Update {dependency.Name}");
            install.Click += async (_, _) => await InstallOfficial(catalogMod, catalogMod.Latest, install);
            return install;
        }
        var open = Button("Open Browse");
        open.Click += (_, _) => _tabs.SelectedIndex = 1;
        return open;
    }

    private void SetAllVisibleMods(ProfileRecord profile, IReadOnlyList<ModRecord> mods, bool enabled)
    {
        var visible = mods.Where(m => string.IsNullOrWhiteSpace(_modSearch) ||
            m.Name.Contains(_modSearch, StringComparison.OrdinalIgnoreCase) ||
            m.Id.Contains(_modSearch, StringComparison.OrdinalIgnoreCase)).ToList();
        foreach (var mod in visible)
        {
            if (enabled) _database.SetProfileMod(profile.Id, mod.Id, true, 0);
            else _database.RemoveProfileMod(profile.Id, mod.Id);
        }
        _changesPending = true;
        SetStatus($"{(enabled ? "Enabled" : "Disabled")} {visible.Count} mod(s) in {profile.Name}.");
        ShowDashboard(2);
    }

    private void SetSelectedMods(ProfileRecord profile, bool enabled)
    {
        var selected = _database.GetMods().Where(m => _selectedMods.Contains(m.Id)).ToList();
        foreach (var mod in selected)
        {
            if (enabled) _database.SetProfileMod(profile.Id, mod.Id, true, 0);
            else _database.RemoveProfileMod(profile.Id, mod.Id);
        }
        SetStatus($"{(enabled ? "Enabled" : "Disabled")} {selected.Count} selected mod(s) in {profile.Name}.");
        _changesPending = selected.Count > 0;
        ShowDashboard(2);
    }

    private async Task RemoveSelectedMods()
    {
        var selected = _database.GetMods().Where(m => _selectedMods.Contains(m.Id)).ToList();
        if (selected.Count == 0) { SetStatus("Select at least one mod first.", error: true); return; }
        if (!await Confirm("Remove selected mods", $"Remove {selected.Count} selected mod(s) from the manager and all profiles?", "Remove Mods")) return;
        var game = _database.GetGame();
        foreach (var mod in selected)
        {
            var versions = _database.GetModVersions(mod.Id);
            var result = CreateDeploymentService().RemoveMod(mod.Id, game?.InstallPath);
            if (!result.Success) { SetStatus(result.Error ?? $"Could not remove {mod.Name}.", error: true); return; }
            foreach (var version in versions)
                if (Directory.Exists(version.InstalledPath)) Directory.Delete(version.InstalledPath, recursive: true);
            if (Directory.Exists(mod.InstalledPath)) Directory.Delete(mod.InstalledPath, recursive: true);
        }
        _selectedMods.Clear();
        SetStatus($"Removed {selected.Count} mod(s).");
        ShowDashboard(2);
    }

    private Control BuildSettingsPage(GameRecord? game, Core.Common.Result<DeploymentPlan>? planResult)
    {
        var openLogs = Button("Open Debug Logs");
        openLogs.Click += (_, _) => OpenFolder(_paths.Logs);
        return new ScrollViewer
        {
            Content = new StackPanel { Spacing = 14, Children =
            {
                new TextBlock { Text = "Settings", FontSize = 22, FontWeight = FontWeight.SemiBold },
                Text("Choose your game folder, manage script support, and keep the game in sync with your active profile."),
                MutedText($"Application version: {ManagerCompatibility.Version}"),
                BuildGameSetup(game),
                BuildProfileFileStatus(game, planResult),
                BuildScriptSupportSettings(game, planResult),
                new Expander { Header = "Advanced recovery", Content = BuildDeploymentRecovery(game, planResult), IsExpanded = false },
                new Expander { Header = "Advanced launch options", Content = BuildLaunchOptions(), IsExpanded = false },
                new Expander { Header = "Troubleshooting", Content = Section("Logs", Text("If something goes wrong, open the logs folder and share the relevant files."), openLogs), IsExpanded = false }
            }}
        };
    }

    private Control BuildProfileFileStatus(GameRecord? game, Core.Common.Result<DeploymentPlan>? planResult)
    {
        var lines = new List<string>();
        if (game is null)
        {
            lines.Add("Choose a Ragnarock folder to inspect the game.");
        }
        else if (planResult is { Success: true, Value: not null } plan)
        {
            var applied = IsDeploymentSynchronized(plan.Value);
            lines.Add(applied ? "Active profile: Applied" : "Active profile: Not applied");
            lines.AddRange(plan.Value.Conflicts.Where(c => c.BlocksDeployment).Select(FriendlyDeploymentConflict));
            var unmanaged = CreateDeploymentService().GetUnmanagedFiles(game.InstallPath);
            if (unmanaged.Count > 0) lines.Add($"Unmanaged game files: {unmanaged.Count} found");
        }
        else
        {
            lines.Add("The active profile cannot be checked until its issues are resolved.");
        }

        var apply = PrimaryButton("Apply Active Profile");
        apply.IsEnabled = game is not null;
        apply.Click += (_, _) => DeployActiveProfile();
        var remove = Button("Remove Unmanaged Mod Files");
        remove.IsEnabled = game is not null && CreateDeploymentService().GetUnmanagedFiles(game.InstallPath).Count > 0;
        remove.Click += async (_, _) =>
        {
            if (game is null) return;
            var unmanaged = CreateDeploymentService().GetUnmanagedFiles(game.InstallPath);
            if (unmanaged.Count == 0) return;
            if (!await Confirm("Remove unmanaged mod files", $"Remove {unmanaged.Count} file(s) from the game’s UE4SS Mods folder and their unmanaged mods.txt entries? This cannot be undone.", "Remove Files")) return;
            var result = CreateDeploymentService().RemoveUnmanagedFiles(game.InstallPath);
            SetStatus(result.Success ? $"Removed {result.Value} unmanaged mod file(s)." : result.Error ?? "Could not remove unmanaged files.", !result.Success);
            ShowDashboard(4);
        };
        return Section("Profile and game files", Text(string.Join(Environment.NewLine, lines)), MutedText("Applying the profile updates manager-owned files and leaves unrelated files alone. Unmanaged files stay in place until you remove them."), Wrap(apply, remove));
    }

    private Control BuildScriptSupportSettings(GameRecord? game, Core.Common.Result<DeploymentPlan>? planResult)
    {
        var scriptMods = planResult?.Success == true && planResult.Value is not null
            ? planResult.Value.Items.Where(i => i.FileType.Equals("ue4ss-lua", StringComparison.OrdinalIgnoreCase) || i.FileType.Equals("ue4ss-dll", StringComparison.OrdinalIgnoreCase)).Select(i => i.ModId).Distinct(StringComparer.OrdinalIgnoreCase).ToList()
            : [];
        var status = game is null ? "Choose a game folder first." : _ue4ss.Detect(game.InstallPath).Installed ? "Installed" : "Not installed";
        var note = scriptMods.Count == 0 ? "No enabled mods in the active profile require script support." : $"Required by {scriptMods.Count} enabled mod(s): {string.Join(", ", scriptMods)}.";
        var check = PrimaryButton("Check for Updates");
        check.IsEnabled = game is not null;
        check.Click += async (_, _) => await CheckUe4ssUpdates();
        var install = Button("Install from ZIP");
        install.IsEnabled = game is not null;
        install.Click += async (_, _) => await InstallUe4ssSupport();
        var controls = new List<Control> { Text($"Status: {status}"), MutedText(note), Wrap(check, install) };
        var cached = _ue4ssReleases.GetCachedReleases();
        if (cached.Count > 0)
        {
            var picker = new ComboBox { ItemsSource = cached.Select(r => $"{r.Version} ({r.AssetName})").ToList(), SelectedIndex = 0, MinWidth = 280 };
            var use = Button("Install Saved Version");
            use.IsEnabled = game is not null;
            use.Click += (_, _) =>
            {
                var current = _database.GetGame();
                if (current is null || picker.SelectedIndex < 0 || picker.SelectedIndex >= cached.Count) return;
                var result = _ue4ssReleases.InstallCachedRelease(current.InstallPath, cached[picker.SelectedIndex]);
                SetStatus(result.Success ? $"Installed saved script support {cached[picker.SelectedIndex].Version}." : result.Error ?? "Script support installation failed.", !result.Success);
                ShowDashboard(4);
            };
            controls.Add(Wrap(picker, use));
        }
        return Section("Script support", controls.ToArray());
    }

    private Control BuildDeploymentRecovery(GameRecord? game, Core.Common.Result<DeploymentPlan>? planResult)
    {
        var summary = planResult is { Success: true, Value: not null }
            ? $"Recovery information: {planResult.Value.Items.Count} file(s), {planResult.Value.Conflicts.Count(c => c.BlocksDeployment)} blocking issue(s), {planResult.Value.Warnings.Count} warning(s)."
            : "The deployment preview is unavailable until Ragnarock is configured.";
        var rollback = Button("Rollback Last Deployment");
        rollback.IsEnabled = game is not null;
        rollback.Click += (_, _) =>
        {
            var result = CreateDeploymentService().RollbackLatest();
            SetStatus(result.Success ? "Rolled back the last deployment." : result.Error ?? "Rollback failed.", !result.Success);
            ShowDashboard(4);
        };
        var reset = Button("Remove Managed Deployment");
        reset.IsEnabled = game is not null;
        reset.Click += async (_, _) =>
        {
            if (!await Confirm("Remove managed deployment", "Remove files currently managed by RagnaModManager from the game folder? Backups are retained when possible.", "Remove Deployment")) return;
            var result = CreateDeploymentService().ResetDeployment();
            SetStatus(result.Success ? "Managed deployment removed." : result.Error ?? "Could not remove deployment.", !result.Success);
            ShowDashboard(4);
        };
        return Section("Recovery tools", Text(summary), Text("Rollback restores the previous manager deployment. Remove Managed Deployment removes manager-owned files from the game folder. Unmanaged files are not affected."), Row(rollback, reset));
    }

    private Control BuildLaunchOptions()
    {
        var arguments = new TextBox { Text = _launchArguments, Watermark = "Optional launch arguments", MinWidth = 420 };
        var save = Button("Save Launch Options");
        save.Click += (_, _) =>
        {
            _launchArguments = arguments.Text ?? "";
            File.WriteAllText(_paths.LaunchArgumentsPath, _launchArguments);
            SetStatus(string.IsNullOrWhiteSpace(_launchArguments) ? "Launch arguments cleared." : "Launch arguments saved.");
        };
        return Section("Launch options", Text("These arguments are used when launching Ragnarock directly. Steam launches ignore them."), Wrap(arguments, save));
    }

    private Control BuildDashboardSummary(GameRecord? game, ProfileRecord active, IReadOnlyList<ModRecord> mods, Core.Common.Result<DeploymentPlan>? planResult)
    {
        var enabled = GetEnabledMods().Count;
        var ready = game is not null && new RagnarockCompatibilityChecker().Check(game.InstallPath).CanManage;
        var synchronized = game is not null && planResult is { Success: true, Value: not null } && IsDeploymentSynchronized(planResult.Value);
        var headline = game is null
            ? "Welcome! Let’s get Ragnarock ready for mods."
            : ready
                ? synchronized ? "Ragnarock is ready. Choose a mod to get started." : "Ragnarock is ready, but this profile is not applied."
                : "One quick setup step remains before you can use mods.";
        var state = new TextBlock
        {
            Text = headline,
            FontSize = 18,
            FontWeight = FontWeight.SemiBold,
            Foreground = game is null || !ready ? Brushes.DarkGoldenrod : Brushes.DarkGreen,
            TextWrapping = TextWrapping.Wrap
        };
        var ue4ssState = game is null ? "Not checked" : _ue4ss.Detect(game.InstallPath).Installed ? "Available" : "Optional";
        var updates = _officialCatalogResult?.Value?.Mods.Count(c =>
        {
            var current = mods.FirstOrDefault(m => m.Id.Equals(c.Id, StringComparison.OrdinalIgnoreCase));
            return current is not null && c.Latest is not null && SemanticVersion.IsNewer(c.Latest.Version, current.Version);
        }) ?? 0;
        var metrics = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("*,*,*,*"),
            ColumnSpacing = 10,
            Children =
            {
                Cell(MetricCard("PROFILE", active.Name, "Currently selected"), 0),
                Cell(MetricCard("MODS", $"{enabled} active / {mods.Count}", "Enabled / installed"), 1),
                Cell(MetricCard("GAME", game is null ? "Not configured" : !ready ? "Needs attention" : synchronized ? "Profile applied" : "Profile not applied", game is null ? "Choose a folder to begin" : $"Script support: {ue4ssState}"), 2)
                ,Cell(MetricCard("UPDATES", updates == 0 ? "Up to date" : $"{updates} available", _catalogLastChecked is null ? "Registry not checked" : $"Checked {_catalogLastChecked}"), 3)
            }
        };
        var nextSteps = game is null
            ? "1. Set up your Ragnarock folder.\n2. Browse the Library or import a mod.\n3. Turn it on in Mods."
            : ready
                ? synchronized
                    ? "1. Browse the Library or import a mod.\n2. Turn it on in Mods.\n3. Click Apply Changes when the banner appears."
                    : "1. Review the active profile.\n2. Resolve any reported file issues.\n3. Click Apply Changes to synchronize the game."
                : "Review setup and choose a valid Ragnarock folder.";
        return Section("Setup overview", state, metrics,
            Section("Next steps", Text(nextSteps)));
    }

    private bool IsDeploymentSynchronized(DeploymentPlan plan)
    {
        if (plan.Conflicts.Any(c => c.BlocksDeployment) || !File.Exists(_paths.CurrentDeploymentPath)) return false;

        try
        {
            var current = JsonSerializer.Deserialize<DeploymentManifest>(File.ReadAllText(_paths.CurrentDeploymentPath), new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            });
            var activeProfile = _database.GetActiveProfile();
            if (current is null || !current.Profile.Equals(activeProfile.Id, StringComparison.OrdinalIgnoreCase)) return false;

            var deployed = _database.GetDeployedFiles(activeProfile.Id);
            return plan.Items.All(item =>
            {
                var record = deployed.FirstOrDefault(file =>
                    file.ModId.Equals(item.ModId, StringComparison.OrdinalIgnoreCase) &&
                    file.SourcePath.Equals(item.SourcePath, StringComparison.OrdinalIgnoreCase) &&
                    file.TargetPath.Equals(item.TargetPath, StringComparison.OrdinalIgnoreCase));
                return record is not null && File.Exists(item.TargetPath) &&
                       record.Checksum.Equals(Sha256.FileChecksum(item.TargetPath), StringComparison.OrdinalIgnoreCase) &&
                       record.Checksum.Equals(Sha256.FileChecksum(item.SourcePath), StringComparison.OrdinalIgnoreCase);
            });
        }
        catch (Exception ex) when (ex is IOException or JsonException)
        {
            return false;
        }
    }

    private static TabItem Tab(string header, Control content) => new() { Header = header, Content = content };

    private static string FriendlyDeploymentConflict(DeploymentConflict conflict) => conflict.Kind switch
    {
        "unmanaged-file" => "A selected mod needs to replace an existing game file. Review and approve this in Settings.",
        "same-target" => "Two enabled mods try to replace the same game file. Disable one before applying the profile.",
        "declared-conflict" => "Two enabled mods are incompatible. Disable one before applying the profile.",
        "missing-dependency" => "An enabled mod is missing a required dependency. Install or enable it before applying the profile.",
        "disabled-dependency" => "An enabled mod depends on a mod that is turned off. Enable the dependency before applying the profile.",
        "dependency-version" => "An enabled mod needs a different dependency version. Install or select a compatible version before applying the profile.",
        "profile-version" => "A selected mod version is not installed. Install it or choose another version in Mods.",
        "manager-requirement" => "An enabled mod requires a newer version of RagnaModManager.",
        "ue4ss-requirement" => "An enabled mod requires script support. Install it from Settings before applying the profile.",
        _ => "The active profile has an issue that must be resolved before it can be applied."
    };

    private Control BuildOfficialCatalog(IReadOnlyList<ModRecord> installed)
    {
        var refresh = PrimaryButton("Refresh Community Catalog");
        refresh.Click += async (_, _) => await RefreshOfficialCatalog();
        var updateAll = Button("Update All Available");
        updateAll.Click += async (_, _) => await UpdateAllOfficial(installed);
        updateAll.IsEnabled = _officialCatalogResult?.Success == true;
        var installSelected = Button("Install Selected");
        installSelected.Click += async (_, _) => await InstallSelectedOfficial();
        installSelected.IsEnabled = _selectedCatalogMods.Count > 0;
        var search = new TextBox { Watermark = "Search by name or ID…", Text = _librarySearch, MinWidth = 300 };
        search.KeyDown += (_, e) =>
        {
            if (e.Key == Key.Enter)
            {
                _librarySearch = search.Text?.Trim() ?? "";
                ShowDashboard(1);
            }
        };
        var clearSearch = Button("×");
        clearSearch.MinWidth = 34;
        clearSearch.Click += (_, _) => { _librarySearch = ""; ShowDashboard(1); };
        var searchBox = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto"), Children = { Cell(search, 0), Cell(clearSearch, 1) } };
        var content = new StackPanel { Spacing = 10 };
        content.Children.Add(Text("Browse community-reviewed Ragnarock mods. Choose a version and install it with one click."));
        content.Children.Add(MutedText("This catalog is community-maintained and is not affiliated with Ragnarock, Wanadev, or RagnaCustoms."));
        content.Children.Add(Wrap(searchBox, refresh, installSelected, updateAll));
        _libraryStatus = new TextBlock { TextWrapping = TextWrapping.Wrap };
        content.Children.Add(_libraryStatus);

        if (_catalogLoading)
        {
            content.Children.Add(MutedText("Loading community catalog…"));
        }
        else if (_officialCatalogResult is null)
        {
            content.Children.Add(MutedText("Community catalog has not been loaded yet."));
        }
        else if (_officialCatalogResult is { Success: false })
        {
            content.Children.Add(Text(_officialCatalogResult.Error ?? "Could not load the community catalog."));
        }
        else if (_officialCatalogResult?.Value is { Mods.Count: 0 })
        {
            content.Children.Add(MutedText("No community mods are published yet."));
        }
        else if (_officialCatalogResult?.Value is { } catalog)
        {
            var visibleMods = catalog.Mods.Where(m => string.IsNullOrWhiteSpace(_librarySearch) || m.Name.Contains(_librarySearch, StringComparison.OrdinalIgnoreCase) || m.Id.Contains(_librarySearch, StringComparison.OrdinalIgnoreCase)).ToList();
            content.Children.Add(BuildCatalogHeader(visibleMods));
            foreach (var mod in SortCatalog(visibleMods))
                content.Children.Add(OfficialModRow(mod, installed));
        }

        return Section("Browse Community Mods", content);
    }

    private Control BuildCatalogHeader(IReadOnlyList<CatalogMod> visibleMods)
    {
        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("28,Auto,*,120,120,220"), ColumnSpacing = 8, Margin = new Thickness(8, 4) };
        var selectAll = new CheckBox { Content = "All", IsChecked = visibleMods.Count > 0 && visibleMods.All(m => _selectedCatalogMods.Contains(m.Id)) };
        selectAll.Click += (_, _) =>
        {
            foreach (var mod in visibleMods)
            {
                if (selectAll.IsChecked == true) AddCatalogSelectionWithDependencies(mod);
                else _selectedCatalogMods.Remove(mod.Id);
            }
            ShowDashboard(1);
        };
        grid.Children.Add(MutedText(""));
        grid.Children.Add(Cell(selectAll, 1));
        grid.Children.Add(Cell(SortButton("Mod", "Name"), 2));
        grid.Children.Add(Cell(SortButton("Latest", "Latest"), 3));
        grid.Children.Add(Cell(SortButton("Installed", "Installed"), 4));
        grid.Children.Add(Cell(MutedText("Actions"), 5));
        return new Border
        {
            HorizontalAlignment = HorizontalAlignment.Stretch,
            Background = new SolidColorBrush(Color.Parse("#EEF2F7")),
            BorderBrush = Brushes.LightGray,
            BorderThickness = new Thickness(0, 0, 0, 1),
            Child = grid
        };
    }

    private Button SortButton(string label, string column)
    {
        var button = Button($"{label}{(_catalogSortColumn == column ? (_catalogSortDescending ? " ↓" : " ↑") : "")}");
        button.Click += (_, _) =>
        {
            if (_catalogSortColumn == column) _catalogSortDescending = !_catalogSortDescending;
            else { _catalogSortColumn = column; _catalogSortDescending = false; }
            ShowDashboard(1);
        };
        return button;
    }

    private IEnumerable<CatalogMod> SortCatalog(IEnumerable<CatalogMod> mods)
    {
        var sorted = _catalogSortColumn switch
        {
            "Latest" => _catalogSortDescending
                ? mods.OrderByDescending(m => m.Latest?.Version, Comparer<string?>.Create((a, b) => SemanticVersion.Compare(a, b)))
                : mods.OrderBy(m => m.Latest?.Version, Comparer<string?>.Create((a, b) => SemanticVersion.Compare(a, b))),
            "Installed" => _catalogSortDescending
                ? mods.OrderByDescending(m => _database.GetMod(m.Id)?.Version, Comparer<string?>.Create((a, b) => SemanticVersion.Compare(a, b)))
                : mods.OrderBy(m => _database.GetMod(m.Id)?.Version, Comparer<string?>.Create((a, b) => SemanticVersion.Compare(a, b))),
            _ => _catalogSortDescending ? mods.OrderByDescending(m => m.Name) : mods.OrderBy(m => m.Name)
        };
        return sorted.ThenBy(m => m.Name, StringComparer.OrdinalIgnoreCase);
    }

    private void ToggleCatalogSelection(CatalogMod mod, bool selected)
    {
        if (selected) AddCatalogSelectionWithDependencies(mod);
        else _selectedCatalogMods.Remove(mod.Id);
        ShowDashboard(1);
    }

    private void AddCatalogSelectionWithDependencies(CatalogMod mod)
    {
        if (!_selectedCatalogMods.Add(mod.Id)) return;
        foreach (var id in mod.Dependencies?.Keys ?? [])
        {
            var dependency = _officialCatalogResult?.Value?.Mods.FirstOrDefault(m => m.Id.Equals(id, StringComparison.OrdinalIgnoreCase));
            if (dependency is not null) AddCatalogSelectionWithDependencies(dependency);
        }
    }

    private async Task InstallSelectedOfficial()
    {
        var catalog = _officialCatalogResult?.Value;
        if (catalog is null) return;
        var selected = catalog.Mods.Where(m => _selectedCatalogMods.Contains(m.Id)).ToList();
        foreach (var mod in selected)
        {
            var release = mod.Latest;
            if (release is null) continue;
            SetStatus($"Installing {mod.Name} {release.Version}…");
            var result = await _officialCatalog.DownloadAndImportAsync(mod, release);
            if (!result.Success) { SetStatus(result.Error ?? $"Could not install {mod.Name}.", error: true); return; }
        }
        _selectedCatalogMods.Clear();
        SetStatus($"Installed {selected.Count} selected community mod(s) and their dependencies.");
        ShowDashboard(1);
    }

    private async Task UpdateAllOfficial(IReadOnlyList<ModRecord> installed)
    {
        var catalog = _officialCatalogResult?.Value;
        if (catalog is null) return;
        var available = catalog.Mods
            .Select(m => (Mod: m, Release: m.Latest, Current: installed.FirstOrDefault(i => i.Id.Equals(m.Id, StringComparison.OrdinalIgnoreCase))))
            .Where(x => x.Release is not null && x.Current is not null && SemanticVersion.IsNewer(x.Release!.Version, x.Current!.Version))
            .ToList();
        if (available.Count == 0) { SetStatus("All installed community mods are up to date."); return; }
        foreach (var item in available)
        {
            SetStatus($"Updating {item.Mod.Name}…");
            var result = await _officialCatalog.DownloadAndImportAsync(item.Mod, item.Release!);
            if (!result.Success) { SetStatus($"Could not update {item.Mod.Name}: {result.Error}", error: true); return; }
        }
        SetStatus($"Updated {available.Count} community mod(s).");
        ShowDashboard(1);
    }

    private Core.Common.Result<OfficialCatalog>? _officialCatalogResult;
    private bool _catalogLoading;

    private Control OfficialModRow(CatalogMod catalogMod, IReadOnlyList<ModRecord> installed)
    {
        var installedVersions = _database.GetModVersions(catalogMod.Id);
        var current = installedVersions.FirstOrDefault() ?? installed.FirstOrDefault(m => string.Equals(m.Id, catalogMod.Id, StringComparison.OrdinalIgnoreCase));
        var releases = catalogMod.Releases
            .OrderByDescending(r => r.Version, Comparer<string>.Create(SemanticVersion.Compare))
            .ToList();
        var latest = releases.FirstOrDefault();
        var updateAvailable = current is not null && latest is not null && IsNewerVersion(latest.Version, current.Version);
        var picker = new ComboBox
        {
            ItemsSource = releases.Select(r => r.Version).ToList(),
            SelectedIndex = 0,
            MinWidth = 130
        };
        var install = Button(current is null ? "Install" : updateAvailable ? "Update" : "Reinstall");
        install.Click += async (_, _) =>
        {
            if (picker.SelectedIndex >= 0 && picker.SelectedIndex < releases.Count)
                await InstallOfficial(catalogMod, releases[picker.SelectedIndex], install);
        };
        var details = Button("Details");
        details.Click += async (_, _) => await ShowDetails(catalogMod.Name, $"{catalogMod.Description ?? "No description."}{Environment.NewLine}Author: {catalogMod.Author ?? "Unknown"}{Environment.NewLine}License: {catalogMod.License ?? "Not specified"}{Environment.NewLine}Source: {catalogMod.SourceUrl ?? "Not specified"}{Environment.NewLine}{Environment.NewLine}Dependencies:{Environment.NewLine}{FormatDependencies(catalogMod.Dependencies)}{Environment.NewLine}{Environment.NewLine}Available releases: {string.Join(", ", releases.Select(r => $"{r.Version}{(r.SizeBytes is null ? "" : $" ({r.SizeBytes / 1024} KB)")}"))}{Environment.NewLine}{Environment.NewLine}{releases[0].Changelog ?? "No release notes provided."}");

        var check = new CheckBox { IsChecked = _selectedCatalogMods.Contains(catalogMod.Id), VerticalAlignment = VerticalAlignment.Center };
        check.Click += (_, _) => ToggleCatalogSelection(catalogMod, check.IsChecked == true);
        var expand = new ToggleButton { Content = "›", Width = 24, Height = 24, Padding = new Thickness(0), HorizontalContentAlignment = HorizontalAlignment.Center };
        var compact = new Grid { ColumnDefinitions = new ColumnDefinitions("28,Auto,*,120,120,220"), ColumnSpacing = 8, Margin = new Thickness(8, 5), HorizontalAlignment = HorizontalAlignment.Stretch };
        compact.Children.Add(expand);
        compact.Children.Add(Cell(check, 1));
        compact.Children.Add(Cell(new TextBlock { Text = catalogMod.Name, FontWeight = FontWeight.SemiBold }, 2));
        compact.Children.Add(Cell(new TextBlock { Text = latest?.Version ?? "—" }, 3));
        compact.Children.Add(Cell(new TextBlock { Text = installedVersions.Count switch { 0 => "Not installed", 1 => installedVersions[0].Version, _ => $"{installedVersions.Count} versions" }, Foreground = updateAvailable ? Brushes.DarkGoldenrod : Brushes.DimGray }, 4));
        compact.Children.Add(Cell(new TextBlock { Text = "Select a release below", Foreground = Brushes.DimGray }, 5));
        var detailText = $"{catalogMod.Description ?? "No description."}{Environment.NewLine}Author: {catalogMod.Author ?? "Unknown"}{Environment.NewLine}License: {catalogMod.License ?? "Not specified"}{Environment.NewLine}Source: {catalogMod.SourceUrl ?? "Not specified"}{Environment.NewLine}{Environment.NewLine}Dependencies:{Environment.NewLine}{FormatDependencies(catalogMod.Dependencies)}{Environment.NewLine}{Environment.NewLine}Releases: {string.Join(", ", releases.Select(r => $"{r.Version}{(r.SizeBytes is null ? "" : $" ({r.SizeBytes / 1024} KB)")}"))}{Environment.NewLine}{Environment.NewLine}{releases[0].Changelog ?? "No release notes provided."}";
        var detailActions = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Children = { picker, install, details } };
        var detailPanel = new StackPanel { Margin = new Thickness(36, 0, 8, 8), Spacing = 8, IsVisible = false, Children = { Text(detailText), detailActions } };
        expand.Click += (_, _) =>
        {
            detailPanel.IsVisible = expand.IsChecked == true;
            expand.Content = expand.IsChecked == true ? "⌄" : "›";
        };
        return new Border
        {
            HorizontalAlignment = HorizontalAlignment.Stretch,
            BorderBrush = Brushes.LightGray,
            BorderThickness = new Thickness(0, 0, 0, 1),
            Child = new StackPanel { Children = { compact, detailPanel } }
        };
    }

    private static string FormatDependencies(IReadOnlyDictionary<string, string>? dependencies) =>
        dependencies is null or { Count: 0 } ? "None" : string.Join(Environment.NewLine, dependencies.Select(d => $"- {d.Key} {d.Value}"));

    private async Task InstallOfficial(CatalogMod catalogMod, CatalogRelease release, Button install)
    {
        install.IsEnabled = false;
        try
        {
            SetStatus($"Downloading {catalogMod.Name} {release.Version}…");
            var result = await _officialCatalog.DownloadAndImportAsync(catalogMod, release);
            if (!result.Success)
            {
                SetStatus(result.Error ?? "Community mod download failed.", error: true);
                return;
            }

            SetStatus($"Installed {result.Value!.Name} {result.Value.Version}. Open Mods to turn it on.");
            ShowDashboard();
            var game = _database.GetGame();
            if (game is not null && RequiresScriptSupport(result.Value) && !_ue4ss.Detect(game.InstallPath).Installed)
                await OfferScriptSupport(result.Value.Name);
        }
        finally
        {
            install.IsEnabled = true;
        }
    }

    private async Task RefreshOfficialCatalog()
    {
        var returnTab = _selectedTab;
        _catalogLoading = true;
        ShowDashboard(returnTab);
        _officialCatalogResult = await _officialCatalog.LoadAsync();
        _catalogLastChecked = DateTimeOffset.Now.ToString("g");
        File.WriteAllText(_paths.CatalogLastCheckedPath, _catalogLastChecked);
        _catalogLoading = false;
        ShowDashboard(returnTab);
        SetLibraryStatus(_officialCatalogResult.Success ? $"Community catalog loaded: {_officialCatalogResult.Value!.Mods.Count} mod(s). Last checked {_catalogLastChecked}." : $"Registry unavailable (last checked {_catalogLastChecked ?? "never"}): {_officialCatalogResult.Error}", !_officialCatalogResult.Success);
    }

    private void SetLibraryStatus(string message, bool error = false)
    {
        if (_libraryStatus is not null)
        {
            _libraryStatus.Text = message;
            _libraryStatus.Foreground = error ? Brushes.Firebrick : Brushes.DarkGreen;
        }
    }

    private Control BuildQuickActions(GameRecord? game)
    {
        var actions = new List<Control>();
        if (game is null)
        {
            var setup = PrimaryButton("Set Up Automatically");
            setup.Click += (_, _) => SetupAutomatically();
            actions.Add(setup);
        }

        var import = Button("Import Mod");
        import.Click += async (_, _) => await ImportModPackage();

        var openGame = Button("Open Game Folder");
        var openMods = Button("Open Mod Library");
        openGame.IsEnabled = game is not null;
        openMods.IsEnabled = true;
        openGame.Click += (_, _) =>
        {
            var currentGame = _database.GetGame();
            if (currentGame is null)
            {
                SetStatus("Choose your Ragnarock folder first.", error: true);
                return;
            }

            OpenFolder(currentGame.InstallPath);
        };
        openMods.Click += (_, _) => OpenFolder(_paths.ModLibrary);

        return Card(new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("*,Auto"),
            Children =
            {
                Cell(new StackPanel
                {
                    Spacing = 6,
                    Children =
                    {
                        new TextBlock { Text = game is null ? "Let’s get started" : "Manage your Ragnarock setup", FontSize = 20, FontWeight = FontWeight.SemiBold },
                    }
                }, 0),
                Cell(Row(actions.Concat([import, openGame, openMods]).ToArray()), 1)
            }
        });
    }

    private void UpdatePendingChangesBar()
    {
        var content = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 12, VerticalAlignment = VerticalAlignment.Center };
        content.Children.Add(new TextBlock { Text = "The active profile is not applied to the game.", FontWeight = FontWeight.SemiBold, VerticalAlignment = VerticalAlignment.Center });
        var apply = PrimaryButton("Apply Changes");
        apply.IsEnabled = _database.GetGame() is not null;
        apply.Click += (_, _) => DeployActiveProfile();
        content.Children.Add(apply);
        _pendingChangesBar.Child = content;
        _pendingChangesBar.IsVisible = _changesPending;
        _pendingChangesBar.Background = Brushes.LightYellow;
        _pendingChangesBar.BorderBrush = Brushes.Goldenrod;
        _pendingChangesBar.BorderThickness = new Thickness(1);
        _pendingChangesBar.CornerRadius = new CornerRadius(6);
        _pendingChangesBar.Padding = new Thickness(12, 8);
    }

    private void SetupAutomatically()
    {
        SetStatus("Looking for your Ragnarock installation…");
        var install = _detector.DetectFirstValid();
        if (install is not { IsValid: true })
        {
            SetStatus("Ragnarock was not found automatically. Choose its folder in Settings.", error: true);
            _tabs.SelectedIndex = 4;
            return;
        }

        SaveGame(install);
        ShowDashboard(0);
        SetStatus("Ragnarock is ready. Browse the Library or import a mod.");
    }

    private Control BuildGameSetup(GameRecord? game)
    {
        var pathBox = new TextBox
        {
            Text = game?.InstallPath ?? "",
            Watermark = "/path/to/steamapps/common/Ragnarock",
            MinWidth = 420
        };

        var status = Text(game is null
            ? "Choose your Ragnarock folder to get started."
            : _detector.Validate(game.InstallPath).IsValid
                ? "Ragnarock is ready."
                : "This folder needs attention. Choose the correct Ragnarock folder.");

        var detect = Button("Find Automatically");
        detect.Click += (_, _) =>
        {
            var install = _detector.DetectFirstValid();
            if (install is null)
            {
                SetStatus("No Ragnarock Steam install was found. Choose the folder manually.", error: true);
                return;
            }

            SaveGame(install);
            SetStatus(install.IsValid ? "Ragnarock folder saved." : "Folder saved, but it may need attention.", !install.IsValid);
            ShowDashboard();
        };

        var browse = Button("Choose Folder");
        browse.Click += async (_, _) =>
        {
            var folders = await StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
            {
                Title = "Choose Ragnarock folder",
                AllowMultiple = false
            });
            if (folders.Count > 0)
            {
                pathBox.Text = folders[0].Path.LocalPath;
            }
        };

        var save = PrimaryButton("Save Folder");
        save.Click += (_, _) =>
        {
            if (string.IsNullOrWhiteSpace(pathBox.Text))
            {
                SetStatus("Choose your Ragnarock folder first.", error: true);
                return;
            }

            var install = _detector.Validate(pathBox.Text);
            SaveGame(install);
            SetStatus(install.IsValid ? "Ragnarock folder saved." : "Folder saved, but it may need attention.", !install.IsValid);
            ShowDashboard();
        };

        return Section("Game Folder",
            status,
            Wrap(pathBox, browse, detect, save));
    }

    private Control BuildModList(ProfileRecord active, IReadOnlyList<ModRecord> mods)
    {
        var state = _database.GetProfileMods(active.Id).ToDictionary(m => m.ModId, StringComparer.OrdinalIgnoreCase);
        var list = new StackPanel { Spacing = 10 };

        if (mods.Count == 0)
        {
            var addFirst = PrimaryButton("Add Your First Mod");
            addFirst.Click += async (_, _) => await ImportModPackage();
            list.Children.Add(EmptyState("No mods installed yet.", "Import a .rmod package to add it to this profile.", addFirst));
        }
        else
        {
            foreach (var missing in state.Values.Where(p => _database.GetMod(p.ModId) is null))
            {
                var removeMissing = Button("Remove from profile");
                removeMissing.Click += (_, _) =>
                {
                    _database.RemoveProfileMod(active.Id, missing.ModId);
                    _changesPending = true;
                    SetStatus($"Removed missing mod {missing.ModId} from {active.Name}.", error: true);
                    ShowDashboard(2);
                };
                list.Children.Add(Card(new Grid
                {
                    ColumnDefinitions = new ColumnDefinitions("*,Auto"),
                    Children =
                    {
                        Cell(new StackPanel { Children = { new TextBlock { Text = missing.ModId, FontWeight = FontWeight.SemiBold }, MutedText("Missing from the mod library") } }, 0),
                        Cell(removeMissing, 1)
                    }
                }));
            }
            foreach (var mod in mods)
            {
                if (!string.IsNullOrWhiteSpace(_modSearch) &&
                    !mod.Name.Contains(_modSearch, StringComparison.OrdinalIgnoreCase) &&
                    !mod.Id.Contains(_modSearch, StringComparison.OrdinalIgnoreCase)) continue;
                state.TryGetValue(mod.Id, out var profileMod);
                list.Children.Add(ModRow(active, mod, profileMod));
            }
        }

        return Section($"Mods in {active.Name}", list);
    }

    private Control ModRow(ProfileRecord active, ModRecord mod, ProfileModRecord? profileMod)
    {
        var versions = _database.GetModVersions(mod.Id);
        var versionPicker = new ComboBox
        {
            ItemsSource = versions.Select(v => v.Version).ToList(),
            SelectedIndex = Math.Max(0, versions.Select(v => v.Version).ToList().FindIndex(v => string.Equals(v, profileMod?.Version ?? mod.Version, StringComparison.OrdinalIgnoreCase))),
            MinWidth = 110
        };
        versionPicker.SelectionChanged += (_, _) =>
        {
            if (versionPicker.SelectedIndex < 0 || versionPicker.SelectedIndex >= versions.Count) return;
            var selectedVersion = versions[versionPicker.SelectedIndex].Version;
            _database.SetProfileMod(active.Id, mod.Id, profileMod?.Enabled == true, profileMod?.Priority ?? 0, selectedVersion);
            _changesPending = true;
            SetStatus($"Pinned {mod.Name} to {selectedVersion} in {active.Name}.");
            ShowDashboard(2);
        };
        var selected = new CheckBox { IsChecked = _selectedMods.Contains(mod.Id), Content = "Select", VerticalAlignment = VerticalAlignment.Center };
        selected.Click += (_, _) => { if (selected.IsChecked == true) _selectedMods.Add(mod.Id); else _selectedMods.Remove(mod.Id); };
        var enabled = new CheckBox
        {
            IsChecked = profileMod?.Enabled == true,
            Content = profileMod?.Enabled == true ? "On" : "Off",
            VerticalAlignment = VerticalAlignment.Center
        };
        enabled.Click += (_, _) =>
        {
            var isEnabled = enabled.IsChecked == true;
            if (isEnabled)
                _database.SetProfileMod(active.Id, mod.Id, true, profileMod?.Priority ?? 0, profileMod?.Version ?? mod.Version);
            else
                _database.RemoveProfileMod(active.Id, mod.Id);
            _changesPending = true;
            SetStatus($"{mod.Name} is now {(isEnabled ? "on" : "off")} in {active.Name}.");
            ShowDashboard();
        };

        var moveEarlier = Button("↑");
        ToolTip.SetTip(moveEarlier, "Move Up");
        moveEarlier.IsEnabled = profileMod?.Enabled == true;
        moveEarlier.Click += (_, _) => ChangePriority(active.Id, mod, profileMod?.Priority ?? 0, -1);

        var moveLater = Button("↓");
        ToolTip.SetTip(moveLater, "Move Down");
        moveLater.IsEnabled = profileMod?.Enabled == true;
        moveLater.Click += (_, _) => ChangePriority(active.Id, mod, profileMod?.Priority ?? 0, 1);

        var remove = Button("Remove");
        remove.Click += async (_, _) => await RemoveMod(mod);
        var details = Button("Details");
        details.Click += async (_, _) => await ShowInstalledModDetails(mod);

        return Card(new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("*,Auto"),
            Children =
            {
                Cell(new StackPanel
                {
                    Spacing = 3,
                    Children =
                    {
                        new TextBlock { Text = mod.Name, FontWeight = FontWeight.SemiBold, FontSize = 16 },
                        new TextBlock
                        {
                            Text = $"Version {mod.Version}" + (string.IsNullOrWhiteSpace(mod.Author) ? "" : $" by {mod.Author}") + (profileMod?.Version is null ? "" : $" — profile pin: {profileMod.Version}"),
                            Foreground = Brushes.DimGray,
                            TextWrapping = TextWrapping.Wrap
                        },
                        new TextBlock
                        {
                            Text = profileMod?.Enabled == true ? "On in this profile" : "Off in this profile",
                            Foreground = profileMod?.Enabled == true ? Brushes.DarkGreen : Brushes.DimGray,
                            TextWrapping = TextWrapping.Wrap
                        }
                    }
                }, 0),
                Cell(Row(selected, enabled, versionPicker, moveEarlier, moveLater, details, remove), 1)
            }
        });
    }

    private Control BuildProfiles(ProfileRecord active)
    {
        var list = new StackPanel { Spacing = 10 };
        foreach (var profile in _database.GetProfiles())
        {
            var profileMods = _database.GetProfileMods(profile.Id).Where(m => m.Enabled).ToList();
            var enabled = profileMods.Count;
            var names = profileMods
                .Select(m => _database.GetMod(m.ModId)?.Name ?? m.ModId)
                .Take(5)
                .ToList();
            var summary = profileMods.Count == 0
                ? "No mods selected"
                : $"{enabled} mods" + (names.Count == 0 ? "" : $"{Environment.NewLine}{string.Join(", ", names)}");

            var profileNameBox = new TextBox { Text = profile.Name, MinWidth = 220 };
            var rename = Button("Rename");
            rename.Click += (_, _) =>
            {
                try
                {
                    _database.RenameProfile(profile.Id, profileNameBox.Text ?? "");
                    SetStatus($"Renamed profile to {profileNameBox.Text?.Trim()}.");
                    ShowDashboard(3);
                }
                catch (InvalidOperationException ex)
                {
                    SetStatus(ex.Message, error: true);
                }
            };
            var use = Button(profile.Id == active.Id ? "Current" : "Use Profile");
            use.IsEnabled = profile.Id != active.Id;
            use.Click += (_, _) =>
            {
                _database.SetActiveProfile(profile.Id);
                TryRedeployAfterProfileChange(profile);
                ShowDashboard(3);
            };
            var clone = Button("Clone");
            clone.Click += (_, _) => CreateProfile(profile.Name + " Copy", profile);
            var delete = Button("Delete");
            delete.IsEnabled = profile.Id != active.Id;
            delete.Click += async (_, _) =>
            {
                if (!await Confirm("Delete profile", $"Delete profile ‘{profile.Name}’ and its saved mod selections? This cannot be undone.", "Delete Profile")) return;
                try
                {
                    _database.DeleteProfile(profile.Id);
                    SetStatus($"Deleted profile {profile.Name}.");
                    ShowDashboard(3);
                }
                catch (InvalidOperationException ex)
                {
                    SetStatus(ex.Message, error: true);
                }
            };
            list.Children.Add(Card(new Grid
            {
                ColumnDefinitions = new ColumnDefinitions("*,Auto"),
                Children =
                {
                    Cell(new StackPanel
                    {
                        Spacing = 3,
                        Children =
                        {
                            Row(profileNameBox, rename),
                            MutedText(summary)
                        }
                    }, 0),
                    Cell(Row(use, clone, delete), 1)
                }
            }));
        }

        var name = new TextBox { Watermark = "New profile name", MinWidth = 240 };
        var create = PrimaryButton("Create New Profile");
        create.Click += (_, _) => CreateProfile(name.Text?.Trim() ?? "", null);
        list.Children.Add(Section("Create a profile", Text("Start empty, or clone an existing profile and then customize its mods."), Wrap(name, create)));
        var export = Button("Export Current Profile");
        export.Click += async (_, _) => await ExportCurrentProfile(active);
        var import = Button("Import Profile");
        import.Click += async (_, _) => await ImportProfile();
        list.Children.Add(Section("Share or back up profiles", Text("Profile files contain mod IDs and load order. Missing mods will be shown after import."), Row(export, import)));
        return new StackPanel { Spacing = 14, Children = { Text("Switch between different mod combinations for different sessions."), list } };
    }

    private async Task ExportCurrentProfile(ProfileRecord profile)
    {
        var file = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = "Export profile",
            SuggestedFileName = profile.Id + ".json",
            FileTypeChoices = [new FilePickerFileType("JSON profile") { Patterns = ["*.json"] }]
        });
        if (file is null) return;
        var result = _database.ExportProfile(profile.Id, file.Path.LocalPath);
        SetStatus(result.Success ? $"Exported {profile.Name}." : result.Error ?? "Profile export failed.", !result.Success);
    }

    private async Task ImportProfile()
    {
        var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Import profile",
            AllowMultiple = false,
            FileTypeFilter = [new FilePickerFileType("JSON profiles") { Patterns = ["*.json"] }]
        });
        if (files.Count == 0) return;
        var stem = Path.GetFileNameWithoutExtension(files[0].Name);
        var id = MakeProfileId(stem);
        var suffix = 2;
        var candidate = id;
        while (_database.GetProfile(candidate) is not null) candidate = $"{id}-{suffix++}";
        var result = _database.ImportProfile(files[0].Path.LocalPath, candidate, stem);
        SetStatus(result.Success ? $"Imported profile {stem}." : result.Error ?? "Profile import failed.", !result.Success);
        if (result.Success) ShowDashboard(3);
    }

    private async Task ShowInstalledModDetails(ModRecord mod)
    {
        var manifest = ManifestValidator.LoadAndValidate(mod.ManifestPath);
        if (!manifest.Success || manifest.Value is null)
        {
            await ShowDetails(mod.Name, manifest.Error ?? "Manifest unavailable.");
            return;
        }
        var m = manifest.Value;
        var dependencies = m.Dependencies.Count == 0 ? "None" : string.Join(Environment.NewLine, m.Dependencies.Select(d => $"- {d.Key} {d.Value}"));
        var conflicts = m.Conflicts.Count == 0 ? "None" : string.Join(", ", m.Conflicts);
        var size = mod.SourceArchive is { } archive && File.Exists(archive) ? $"{new FileInfo(archive).Length / 1024} KB" : "Unknown";
        await ShowDetails($"{m.Name} {m.Version}", $"Author: {m.Author ?? "Unknown"}{Environment.NewLine}Game: {m.Game}{Environment.NewLine}Package size: {size}{Environment.NewLine}{Environment.NewLine}Description: {m.Description ?? "None"}{Environment.NewLine}{Environment.NewLine}Dependencies:{Environment.NewLine}{dependencies}{Environment.NewLine}{Environment.NewLine}Conflicts: {conflicts}{Environment.NewLine}Files: {m.Files.Count}");
    }

    private async Task ShowDetails(string title, string message)
    {
        var dialog = new Window { Title = title, Width = 560, Height = 420, WindowStartupLocation = WindowStartupLocation.CenterOwner };
        var close = PrimaryButton("Close");
        close.Click += (_, _) => dialog.Close();
        dialog.Content = new StackPanel { Margin = new Thickness(18), Spacing = 14, Children = { new TextBlock { Text = title, FontSize = 20, FontWeight = FontWeight.SemiBold }, new ScrollViewer { Content = Text(message) }, close } };
        await dialog.ShowDialog(this);
    }

    private void CreateProfile(string cleanName, ProfileRecord? source)
    {
        if (string.IsNullOrWhiteSpace(cleanName))
        {
            SetStatus("Enter a profile name first.", error: true);
            return;
        }

        var id = MakeProfileId(cleanName);
        var suffix = 2;
        var candidate = id;
        while (_database.GetProfile(candidate) is not null) candidate = $"{id}-{suffix++}";
        try
        {
            _database.CreateProfile(candidate, cleanName);
            if (source is not null)
            {
                foreach (var mod in _database.GetProfileMods(source.Id))
                    _database.SetProfileMod(candidate, mod.ModId, mod.Enabled, mod.Priority);
            }
            _database.SetActiveProfile(candidate);
            SetStatus($"Created profile {cleanName}. Customize its mods below.");
            ShowDashboard(2);
        }
        catch (Exception ex) when (ex is InvalidOperationException)
        {
            SetStatus(ex.Message, error: true);
        }
    }

    private Control BuildHealthSummary(GameRecord? game, Core.Common.Result<DeploymentPlan>? planResult)
    {
        var lines = new List<string>();

        if (game is null)
        {
            lines.Add("Set up Ragnarock above to check compatibility.");
        }
        else
        {
            var ue4ss = _ue4ss.Detect(game.InstallPath);
            lines.Add(ue4ss.Installed ? "Script support is installed." : "Script support is not installed.");

            if (planResult is { Success: true, Value: not null })
            {
                var synchronized = IsDeploymentSynchronized(planResult.Value);
                lines.Add(!synchronized
                    ? "The active profile is not applied to the game."
                    : planResult.Value.Items.Count == 0
                        ? "The active profile is applied (no enabled files)."
                        : "The active profile is applied to the game.");
                lines.AddRange(planResult.Value.Conflicts.Where(c => c.BlocksDeployment).Select(FriendlyDeploymentConflict));
                var unmanagedWarning = planResult.Value.Warnings.FirstOrDefault(w => w.StartsWith("Found ", StringComparison.Ordinal));
                if (unmanagedWarning is not null)
                {
                    var count = unmanagedWarning.Split(' ', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault(part => int.TryParse(part, out _));
                    lines.Add($"Unmanaged game files: {count ?? "some"} found. Review them below.");
                }
                lines.AddRange(planResult.Value.Warnings.Where(w => !w.StartsWith("Found ", StringComparison.Ordinal)).Select(w => "Notice: " + w));
            }
            else if (planResult is { Success: false })
            {
                lines.Add("Selected mods: Cannot be applied");
            }
        }

        var checkUe4ss = PrimaryButton("Check for Script Support Updates");
        checkUe4ss.IsEnabled = game is not null;
        checkUe4ss.Click += async (_, _) => await CheckUe4ssUpdates();

        var installUe4ss = Button("Install Script Support from ZIP");
        installUe4ss.IsEnabled = game is not null;
        installUe4ss.Click += async (_, _) => await InstallUe4ssSupport();

        var openData = Button("Open Manager Folder");
        openData.Click += (_, _) => OpenFolder(_paths.Root);

        var removeUnmanaged = Button("Remove Unmanaged Mod Files");
        removeUnmanaged.IsEnabled = game is not null && CreateDeploymentService().GetUnmanagedFiles(game.InstallPath).Count > 0;
        removeUnmanaged.Click += async (_, _) =>
        {
            if (game is null) return;
            var unmanaged = CreateDeploymentService().GetUnmanagedFiles(game.InstallPath);
            if (unmanaged.Count == 0) return;
            if (!await Confirm("Remove unmanaged mod files", $"Remove {unmanaged.Count} file(s) from the game’s UE4SS Mods folder and remove their unmanaged mods.txt entries? This cannot be undone.\n\n{string.Join(Environment.NewLine, unmanaged.Take(8))}{(unmanaged.Count > 8 ? Environment.NewLine + "…" : "")}", "Remove Files")) return;
            var result = CreateDeploymentService().RemoveUnmanagedFiles(game.InstallPath);
            SetStatus(result.Success ? $"Removed {result.Value} unmanaged mod file(s)." : result.Error ?? "Could not remove unmanaged files.", !result.Success);
            ShowDashboard(4);
        };

        var scriptMods = planResult?.Success == true && planResult.Value is not null
            ? planResult.Value.Items.Where(i => i.FileType.Equals("ue4ss-lua", StringComparison.OrdinalIgnoreCase) || i.FileType.Equals("ue4ss-dll", StringComparison.OrdinalIgnoreCase)).Select(i => i.ModId).Distinct(StringComparer.OrdinalIgnoreCase).ToList()
            : [];
        var supportNote = scriptMods.Count == 0
            ? "The active profile does not currently use script support. Install it only when a selected mod requires it."
            : $"The active profile uses script support for {scriptMods.Count} mod(s): {string.Join(", ", scriptMods)}.";
        var controls = new List<Control>
        {
            Text(string.Join(Environment.NewLine, lines)),
            MutedText(supportNote + " Older downloaded versions can be kept here if you need to go back."),
            Wrap(checkUe4ss, installUe4ss, removeUnmanaged, openData)
        };

        var cached = _ue4ssReleases.GetCachedReleases();
        if (cached.Count > 0)
        {
            var picker = new ComboBox
            {
                ItemsSource = cached.Select(r => $"{r.Version} ({r.AssetName})").ToList(),
                SelectedIndex = 0,
                MinWidth = 280
            };

            var installCached = Button("Use Saved Version");
            installCached.IsEnabled = game is not null;
            installCached.Click += (_, _) =>
            {
                var currentGame = _database.GetGame();
                if (currentGame is null || picker.SelectedIndex < 0 || picker.SelectedIndex >= cached.Count)
                {
                    SetStatus("Choose your Ragnarock folder and a cached RE-UE4SS version first.", error: true);
                    return;
                }

                var selected = cached[picker.SelectedIndex];
                var result = _ue4ssReleases.InstallCachedRelease(currentGame.InstallPath, selected);
                SetStatus(result.Success ? $"Installed cached RE-UE4SS {selected.Version}." : result.Error ?? "Cached RE-UE4SS install failed.", !result.Success);
                ShowDashboard();
            };

            controls.Add(Wrap(picker, installCached));
        }
        else
        {
            controls.Add(MutedText(Ue4ssZipHelp));
        }

        return Section("Game and file status", controls.ToArray());
    }

    private async Task CheckUe4ssUpdates()
    {
        var game = _database.GetGame();
        if (game is null)
        {
            SetStatus("Choose your Ragnarock folder first.", error: true);
            return;
        }

        SetStatus("Checking GitHub releases for RE-UE4SS...");
        var check = await _ue4ssReleases.CheckForUpdates(game.InstallPath);
        if (!check.Success)
        {
            SetStatus(check.Error ?? "Could not check RE-UE4SS releases.", error: true);
            return;
        }

        var latest = check.Value!.Latest;
        if (latest is null)
        {
            SetStatus("No usable RE-UE4SS release zip was found.", error: true);
            return;
        }

        if (!check.Value.UpdateAvailable)
        {
            SetStatus(check.Value.Installed.Installed && string.IsNullOrWhiteSpace(check.Value.Installed.Version)
                ? "RE-UE4SS is installed, but its version could not be read. No reinstall is needed; install a newer release only if you choose to upgrade."
                : $"RE-UE4SS {check.Value.Installed.Version ?? latest.Version} is up to date.");
            ShowDashboard();
            return;
        }

        var installed = check.Value.Installed.Installed
            ? check.Value.Installed.Version ?? "unknown"
            : "not installed";
        var confirmed = await Confirm(
            "Update RE-UE4SS?",
            $"Installed: {installed}{Environment.NewLine}Latest: {latest.Version}{Environment.NewLine}{Environment.NewLine}Download and install this RE-UE4SS release? The archive will be cached so you can reinstall it later.",
            "Download and Install");
        if (!confirmed)
        {
            SetStatus("RE-UE4SS update skipped.");
            return;
        }

        await DownloadAndInstallUe4ss(game.InstallPath, latest);
    }

    private async Task DownloadAndInstallUe4ss(string gameRoot, Ue4ssRelease release)
    {
        SetStatus($"Downloading RE-UE4SS {release.Version}...");
        var download = await _ue4ssReleases.DownloadRelease(release);
        if (!download.Success)
        {
            SetStatus(download.Error ?? "RE-UE4SS download failed.", error: true);
            return;
        }

        var install = _ue4ssReleases.InstallCachedRelease(gameRoot, download.Value!);
        SetStatus(install.Success ? $"Downloaded and installed RE-UE4SS {download.Value!.Version}." : install.Error ?? "RE-UE4SS install failed.", !install.Success);
        ShowDashboard();
    }

    private async Task<bool> Confirm(string title, string message, string confirmText)
    {
        var dialog = new Window
        {
            Title = title,
            Width = 460,
            Height = 240,
            MinWidth = 420,
            MinHeight = 220,
            WindowStartupLocation = WindowStartupLocation.CenterOwner
        };

        var confirm = PrimaryButton(confirmText);
        var cancel = Button("Cancel");
        confirm.Click += (_, _) => dialog.Close(true);
        cancel.Click += (_, _) => dialog.Close(false);
        dialog.Content = new StackPanel
        {
            Margin = new Thickness(18),
            Spacing = 14,
            Children =
            {
                new TextBlock { Text = title, FontSize = 20, FontWeight = FontWeight.SemiBold },
                new TextBlock { Text = message, TextWrapping = TextWrapping.Wrap },
                Row(confirm, cancel)
            }
        };

        return await dialog.ShowDialog<bool>(this);
    }

    private async Task ImportModPackage()
    {
        var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Choose .rmod package",
            AllowMultiple = false
        });
        if (files.Count == 0)
        {
            return;
        }

        await ImportModPackage(files[0].Path.LocalPath);
    }

    private async Task ImportModPackage(string path)
    {
        var result = new PackageImporter(_paths, _database, _logger).Import(path);
        if (result.Success)
        {
            SetStatus($"Imported {result.Value!.Name}. Turn it on, then apply changes.");
            ShowDashboard();
            if (_database.GetGame() is not null && RequiresScriptSupport(result.Value) && !_ue4ss.Detect(_database.GetGame()!.InstallPath).Installed)
            {
                await OfferScriptSupport(result.Value.Name);
            }
        }
        else
        {
            SetStatus(result.Error ?? "Import failed.", error: true);
        }
    }

    private async Task InstallUe4ssSupport()
    {
        var game = _database.GetGame();
        if (game is null)
        {
            SetStatus("Choose your Ragnarock folder first.", error: true);
            return;
        }

        var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Choose RE-UE4SS release zip",
            AllowMultiple = false,
            FileTypeFilter =
            [
                new FilePickerFileType("Zip archives") { Patterns = ["*.zip"] },
                FilePickerFileTypes.All
            ]
        });
        if (files.Count == 0)
        {
            return;
        }

        var result = _ue4ss.InstallFromZip(game.InstallPath, files[0].Path.LocalPath);
        SetStatus(result.Success ? "RE-UE4SS installed for script mods." : result.Error ?? "RE-UE4SS install failed.", !result.Success);
        ShowDashboard();
    }

    private async void DeployActiveProfile()
    {
        var game = _database.GetGame();
        if (game is null)
        {
            SetStatus("Choose your Ragnarock folder first.", error: true);
            return;
        }

        var preview = CreateDeploymentService().Preview(game.InstallPath);
        if (preview.Success && preview.Value is not null &&
            preview.Value.Conflicts.Any(c => c.Kind == "ue4ss-requirement") &&
            !_ue4ss.Detect(game.InstallPath).Installed)
        {
            var enabledScriptMods = preview.Value.Conflicts
                .Where(c => c.Kind == "ue4ss-requirement")
                .SelectMany(c => c.Items)
                .Select(i => i.ModId)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Select(id => _database.GetMod(id)?.Name ?? id)
                .ToList();
            var description = enabledScriptMods.Count == 1
                ? $"{enabledScriptMods[0]} uses script support, but it is not installed. Install it now before applying your changes?"
                : "The selected mods use script support, but it is not installed. Install it now before applying your changes?";
            if (await Confirm("Script support needed", description, "Install Script Support"))
            {
                await CheckUe4ssUpdates();
                if (_ue4ss.Detect(game.InstallPath).Installed)
                {
                    DeployActiveProfile();
                }
                else
                {
                    SetStatus("Changes were not applied. Install script support before using these mods.", error: true);
                }
            }
            else
            {
                SetStatus("Changes were not applied. Install script support before using these mods.", error: true);
            }

            return;
        }

        var result = CreateDeploymentService().Deploy(game.InstallPath);
        if (!result.Success && preview.Success && preview.Value is not null)
        {
            var unmanaged = preview.Value.Conflicts
                .Where(c => c.Kind == "unmanaged-file")
                .SelectMany(c => c.Items)
                .Select(i => i.TargetPath)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
            if (unmanaged.Count > 0 && await Confirm(
                    "Reconcile existing game files",
                    $"{unmanaged.Count} existing file(s) are outside the manager’s tracked deployment. Applying this profile will overwrite only those listed target files; unrelated files in the game folder will be left untouched. Continue?\n\n{string.Join(Environment.NewLine, unmanaged.Take(5))}{(unmanaged.Count > 5 ? Environment.NewLine + "…" : "")}",
                    "Reconcile and Apply"))
            {
                result = CreateDeploymentService().Deploy(game.InstallPath, allowUnmanagedFiles: true);
            }
        }
        if (result.Success) _changesPending = false;
        SetStatus(result.Success ? "Changes applied to Ragnarock." : result.Error ?? "Apply failed.", !result.Success);
        ShowDashboard();
    }

    private async Task OfferScriptSupport(string modName)
    {
        var confirmed = await Confirm(
            "Script support needed",
            $"{modName} uses script support, but it is not installed. Install it now?",
            "Install Script Support");
        if (confirmed)
        {
            await CheckUe4ssUpdates();
        }
    }

    private static bool RequiresScriptSupport(ModManifest manifest) =>
        manifest.Files.Any(file => file.Type.Equals("ue4ss-lua", StringComparison.OrdinalIgnoreCase) ||
                                   file.Type.Equals("ue4ss-dll", StringComparison.OrdinalIgnoreCase)) ||
        manifest.Requires?.ContainsKey("ue4ss") == true;

    private static bool IsNewerVersion(string candidate, string installed)
    {
        return SemanticVersion.IsNewer(candidate, installed);
    }

    private void LaunchGame()
    {
        var game = _database.GetGame();
        if (game is null)
        {
            SetStatus("Choose your Ragnarock folder before launching.", error: true);
            return;
        }

        var result = new RagnarockLauncher().Launch(game.InstallPath, _launchArguments);
        SetStatus(result.Success ? "Launch requested." : result.Error ?? "Launch failed.", !result.Success);
    }

    private void ChangePriority(string profileId, ModRecord mod, int currentPriority, int delta)
    {
        var newPriority = currentPriority + delta;
        _database.SetProfileMod(profileId, mod.Id, true, newPriority);
        _changesPending = true;
        SetStatus($"{mod.Name} load order updated. Apply changes when ready.");
        ShowDashboard();
    }

    private async Task RemoveMod(ModRecord mod)
    {
        var confirmed = await Confirm(
            "Remove mod",
            $"Remove {mod.Name} {mod.Version} from the manager and all profiles? This also removes manager-owned deployed files.",
            "Remove Mod");
        if (!confirmed) return;

        var game = _database.GetGame();
        var versions = _database.GetModVersions(mod.Id);
        var result = CreateDeploymentService().RemoveMod(mod.Id, game?.InstallPath);
        if (!result.Success)
        {
            SetStatus(result.Error ?? "Mod removal failed.", error: true);
            return;
        }

        foreach (var version in versions)
        {
            if (Directory.Exists(version.InstalledPath)) Directory.Delete(version.InstalledPath, recursive: true);
        }
        if (Directory.Exists(mod.InstalledPath)) Directory.Delete(mod.InstalledPath, recursive: true);
        _changesPending = false;
        SetStatus($"Removed {mod.Name}.");
        ShowDashboard(2);
    }

    private void TryRedeployAfterProfileChange(ProfileRecord profile)
    {
        var game = _database.GetGame();
        if (game is null)
        {
            SetStatus($"Using profile {profile.Name}. Choose your Ragnarock folder before applying it.");
            return;
        }

        var result = CreateDeploymentService().Deploy(game.InstallPath);
        SetStatus(result.Success ? $"Using profile {profile.Name}; changes applied." : $"Using profile {profile.Name}, but apply failed: {result.Error}", !result.Success);
    }

    private IReadOnlyList<ProfileModRecord> GetEnabledMods()
    {
        var active = _database.GetActiveProfile();
        return _database.GetProfileMods(active.Id).Where(m => m.Enabled).ToList();
    }

    private DeploymentService CreateDeploymentService()
    {
        var planner = new DeploymentPlanner(_database, _rules);
        return new DeploymentService(_paths, _database, planner, _rules, _logger);
    }

    private void SaveGame(RagnarockInstall install)
    {
        _database.UpsertGame(new GameRecord("ragnarock", "Ragnarock", install.Root, install.ExecutablePath, null, OperatingSystem.IsWindows() ? "windows" : "linux"));
    }

    private void OpenFolder(string path)
    {
        var result = _folderOpener.Open(path);
        SetStatus(result.Success ? $"Opened {path}" : result.Error ?? $"Could not open {path}.", !result.Success);
    }

    private void SetStatus(string message, bool error = false)
    {
        _status.Text = message;
        _status.Foreground = error ? Brushes.Firebrick : Brushes.DarkGreen;
    }

    private static string FriendlyInstallStatus(RagnarockInstall install)
    {
        if (install.IsValid)
        {
            return $"Ready: {install.Root}";
        }

        var details = install.Diagnostics.Count == 0
            ? "The folder does not look like a complete Ragnarock install."
            : string.Join(Environment.NewLine, install.Diagnostics.Select(d => "- " + d));
        return $"Needs attention: {install.Root}{Environment.NewLine}{details}";
    }

    private static string MakeProfileId(string name)
    {
        var chars = name.ToLowerInvariant()
            .Select(c => char.IsAsciiLetterOrDigit(c) ? c : '-')
            .ToArray();
        var id = new string(chars).Trim('-');
        while (id.Contains("--", StringComparison.Ordinal))
        {
            id = id.Replace("--", "-", StringComparison.Ordinal);
        }

        return string.IsNullOrWhiteSpace(id) ? "profile" : id;
    }

    private static StackPanel Page(string title, params Control[] controls)
    {
        var panel = new StackPanel { Spacing = 14 };
        panel.Children.Add(new TextBlock
        {
            Text = title,
            FontSize = 24,
            FontWeight = FontWeight.SemiBold,
            Margin = new Thickness(0, 0, 0, 2)
        });
        foreach (var control in controls)
        {
            panel.Children.Add(control);
        }

        return panel;
    }

    private static Border Section(string title, params Control[] controls)
    {
        var panel = new StackPanel { Spacing = 10 };
        panel.Children.Add(new TextBlock { Text = title, FontSize = 18, FontWeight = FontWeight.SemiBold });
        foreach (var control in controls)
        {
            panel.Children.Add(control);
        }

        return Card(panel);
    }

    private static TextBlock Text(string text) => new()
    {
        Text = text,
        TextWrapping = TextWrapping.Wrap
    };

    private static TextBlock MutedText(string text) => new()
    {
        Text = text,
        Foreground = Brushes.DimGray,
        TextWrapping = TextWrapping.Wrap
    };

    private static Button Button(string label) => new()
    {
        Content = label,
        Padding = new Thickness(12, 8),
        HorizontalContentAlignment = HorizontalAlignment.Center
    };

    private static Button PrimaryButton(string label)
    {
        var button = Button(label);
        button.FontWeight = FontWeight.SemiBold;
        return button;
    }

    private static StackPanel Row(params Control[] controls)
    {
        var row = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 10,
            VerticalAlignment = VerticalAlignment.Center
        };
        foreach (var control in controls)
        {
            row.Children.Add(control);
        }

        return row;
    }

    private static WrapPanel Wrap(params Control[] controls)
    {
        var panel = new WrapPanel
        {
            Orientation = Orientation.Horizontal,
            VerticalAlignment = VerticalAlignment.Center,
            ItemHeight = double.NaN,
            ItemWidth = double.NaN
        };
        foreach (var control in controls)
        {
            control.Margin = new Thickness(0, 0, 10, 10);
            panel.Children.Add(control);
        }

        return panel;
    }

    private static Border EmptyState(string title, string message, Control action) => Card(new StackPanel
    {
        Spacing = 8,
        HorizontalAlignment = HorizontalAlignment.Center,
        Children =
        {
            new TextBlock { Text = title, FontSize = 18, FontWeight = FontWeight.SemiBold, HorizontalAlignment = HorizontalAlignment.Center },
            new TextBlock { Text = message, Foreground = Brushes.DimGray, TextWrapping = TextWrapping.Wrap, HorizontalAlignment = HorizontalAlignment.Center },
            action
        }
    });

    private static Border Card(Control content) => new()
    {
        BorderBrush = Brushes.LightGray,
        BorderThickness = new Thickness(1),
        CornerRadius = new CornerRadius(6),
        Background = new SolidColorBrush(Color.Parse("#F7F9FC")),
        Padding = new Thickness(12),
        Child = content
    };

    private static Border MetricCard(string label, string value, string detail) => new()
    {
        Background = Brushes.White,
        BorderBrush = Brushes.LightGray,
        BorderThickness = new Thickness(1),
        CornerRadius = new CornerRadius(6),
        Padding = new Thickness(12),
        Child = new StackPanel
        {
            Spacing = 4,
            Children =
            {
                MutedText(label),
                new TextBlock { Text = value, FontSize = 18, FontWeight = FontWeight.SemiBold, TextWrapping = TextWrapping.Wrap },
                MutedText(detail)
            }
        }
    };

    private static Control Cell(Control control, int column)
    {
        Grid.SetColumn(control, column);
        return control;
    }
}
