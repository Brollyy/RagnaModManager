using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Markup.Xaml;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using System.Diagnostics;
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

public partial class MainWindow : Window
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

    private ContentControl _body = null!;
    private readonly TabControl _tabs = new();
    private TextBlock _status = null!;
    private Border _pendingChangesBar = null!;
    private Button? _launchButton;
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
    private readonly HashSet<string> _expandedAccordions = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, IReadOnlyList<ProfileModRecord>> _appliedProfileSnapshots = new(StringComparer.OrdinalIgnoreCase);
    private readonly MainWindowViewModel _viewModel = new();

    public MainWindow()
    {
        InitializeComponent();
        DataContext = _viewModel;

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

    private void InitializeComponent() => AvaloniaXamlLoader.Load(this);

    private void BuildShell()
    {
        _body = this.FindControl<ContentControl>("Body") ?? throw new InvalidOperationException("Body host was not loaded.");
        _pendingChangesBar = this.FindControl<Border>("PendingChangesBar") ?? throw new InvalidOperationException("Pending changes bar was not loaded.");
        _status = this.FindControl<TextBlock>("StatusText") ?? throw new InvalidOperationException("Status host was not loaded.");
        _launchButton = this.FindControl<Button>("LaunchButton") ?? throw new InvalidOperationException("Launch button was not loaded.");
        _launchButton.Click += (_, _) => LaunchGame();

        _tabs.SelectionChanged += (_, _) =>
        {
            if (!_rebuildingTabs)
            {
                _selectedTab = Math.Max(0, _tabs.SelectedIndex);
                _status.Text = "";
            }
        };

        _pendingChangesBar.IsVisible = false;
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
        EnsureAppliedProfileSnapshot(active.Id);
        _viewModel.CurrentSetup = active.Name;
        var planResult = game is null ? null : CreateDeploymentService().Preview(game.InstallPath);
        if (planResult is { Success: true, Value: not null })
        {
            // A setup edit marks the session dirty before the page is rebuilt. Keep that
            // state until Apply succeeds; a preview alone cannot reliably detect changes
            // such as load-order edits.
            _changesPending = _changesPending || !IsDeploymentSynchronized(planResult.Value);
        }
        var canLaunch = game is not null && !_changesPending && planResult is { Success: true, Value: not null } &&
                        !planResult.Value.Conflicts.Any(conflict => conflict.BlocksDeployment);
        _viewModel.CanLaunch = canLaunch;
        _viewModel.HasPendingChanges = _changesPending;
        _launchButton?.SetCurrentValue(Avalonia.Controls.Button.IsEnabledProperty, canLaunch);

        _rebuildingTabs = true;
        try
        {
            _tabs.Items.Clear();
            _tabs.TabStripPlacement = Dock.Left;
            _tabs.Items.Add(Tab("HOME", BuildDashboardPage(game, active, mods, planResult)));
            _tabs.Items.Add(Tab("DISCOVER", new ScrollViewer { Content = BuildOfficialCatalog(mods) }));
            _tabs.Items.Add(Tab("MODS", BuildModsPage(active, mods, planResult)));
            _tabs.Items.Add(Tab("SETUPS", new ScrollViewer { Content = BuildProfiles(active) }));
            _tabs.Items.Add(Tab("SETTINGS", BuildSettingsPage(game, planResult)));
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
                BuildDashboardSummary(game, active, mods, planResult),
                BuildQuickActions(game),
                BuildDashboardDeploymentStatus(game, planResult)
            }}
        };
    }

    private Control BuildDashboardDeploymentStatus(GameRecord? game, Core.Common.Result<DeploymentPlan>? planResult)
    {
        if (game is null || planResult is not { Success: true, Value: not null }) return new Border { IsVisible = false };
        var plan = planResult.Value;
        var synchronized = !_changesPending && IsDeploymentSynchronized(plan);
        if (synchronized && plan.Warnings.Count == 0) return new Border { IsVisible = false };
        var content = new List<Control>
        {
            Text(synchronized ? "Your setup is active" : "Your setup is not active")
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
            content.Add(Text(count is not null ? $"{count} extra mod file{(count == "1" ? "" : "s")} found." : "Extra mod files were found."));
            content.Add(MutedText("These files are outside your current setup. Open Settings if you want to clean them up."));
        }
        content.AddRange(plan.Warnings.Where(w => !w.StartsWith("Found ", StringComparison.Ordinal)).Select(w => MutedText("Notice: " + w)));
        if (!synchronized)
        {
            content.Add(MutedText("Your latest choices are waiting. Use Apply changes below to make this setup active."));
        }
        return Section("Before you play", content.ToArray());
    }

    private Control BuildModsPage(ProfileRecord active, IReadOnlyList<ModRecord> mods, Core.Common.Result<DeploymentPlan>? planResult)
    {
        var import = PrimaryButton("Add mod file");
        import.Click += async (_, _) => await ImportModPackage();
        var search = new TextBox { Watermark = "Search installed mods…", Text = _modSearch, MinWidth = 300 };
        search.KeyDown += (_, e) =>
        {
            if (e.Key == Key.Enter)
            {
                _modSearch = search.Text?.Trim() ?? "";
                ShowDashboard(2);
            }
        };
        var searchAction = Button("Search");
        searchAction.Click += (_, _) => { _modSearch = search.Text?.Trim() ?? ""; ShowDashboard(2); };
        var clearSearch = Button("Clear");
        clearSearch.IsEnabled = !string.IsNullOrWhiteSpace(_modSearch);
        clearSearch.Click += (_, _) => { _modSearch = ""; ShowDashboard(2); };
        var searchBox = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto"), Children = { Cell(search, 0), Cell(clearSearch, 1) } };
        var visibleMods = mods.Where(m => string.IsNullOrWhiteSpace(_modSearch) ||
            m.Name.Contains(_modSearch, StringComparison.OrdinalIgnoreCase) ||
            m.Id.Contains(_modSearch, StringComparison.OrdinalIgnoreCase)).ToList();
        var enableAll = Button("Enable all visible");
        enableAll.IsEnabled = visibleMods.Count > 0;
        enableAll.Click += (_, _) => SetAllVisibleMods(active, mods, true);
        var disableAll = Button("Disable all visible");
        disableAll.IsEnabled = visibleMods.Count > 0;
        disableAll.Click += (_, _) => SetAllVisibleMods(active, mods, false);
        var enableSelected = Button("Enable selected");
        enableSelected.IsEnabled = _selectedMods.Any(id => visibleMods.Any(m => m.Id.Equals(id, StringComparison.OrdinalIgnoreCase)));
        enableSelected.Click += (_, _) => SetSelectedMods(active, true);
        var disableSelected = Button("Disable selected");
        disableSelected.IsEnabled = enableSelected.IsEnabled;
        disableSelected.Click += (_, _) => SetSelectedMods(active, false);
        var removeSelected = Button("Remove selected");
        removeSelected.IsEnabled = _selectedMods.Any(id => visibleMods.Any(m => m.Id.Equals(id, StringComparison.OrdinalIgnoreCase)));
        removeSelected.Click += async (_, _) => await RemoveSelectedMods();
        var selectAll = Button("Select all");
        selectAll.IsEnabled = visibleMods.Count > 0;
        selectAll.Click += (_, _) => { foreach (var mod in visibleMods) _selectedMods.Add(mod.Id); ShowDashboard(2); };
        var selectNone = Button("Clear selection");
        selectNone.Click += (_, _) => { _selectedMods.Clear(); ShowDashboard(2); };
        var page = new StackPanel { Spacing = 14, Children =
        {
            new TextBlock { Text = "Your mods", FontSize = 24, FontWeight = FontWeight.SemiBold },
            Text("These are the mods you have installed. Enable one to use it in your current setup."),
            Wrap(searchBox, searchAction, import),
            CompactExpander("Bulk actions", Wrap(selectAll, selectNone, enableAll, disableAll, enableSelected, disableSelected, removeSelected), 150),
            MutedText("You can also drag and drop a mod file anywhere in this page."),
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
        if (conflicts.Count == 0) return new Border { IsVisible = false };

        var details = new StackPanel { Spacing = 4 };
        details.Children.Add(new TextBlock
        {
            Text = "Dependencies need attention",
            FontWeight = FontWeight.SemiBold,
            Foreground = new SolidColorBrush(Color.Parse("#FF8B8B"))
        });
        details.Children.Add(Text("These enabled mods cannot be applied until their dependencies are installed, enabled, or updated."));
        foreach (var conflict in conflicts)
        {
            var action = conflict.RelatedModId is null ? null : BuildDependencyAction(conflict);
            details.Children.Add(action is null
                ? new TextBlock { Text = "• " + conflict.Message, Foreground = new SolidColorBrush(Color.Parse("#FF8B8B")), TextWrapping = TextWrapping.Wrap }
                : Wrap(new TextBlock { Text = "• " + conflict.Message, Foreground = new SolidColorBrush(Color.Parse("#FF8B8B")), TextWrapping = TextWrapping.Wrap }, action));
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
                var profile = _database.GetActiveProfile();
                var existing = _database.GetProfileMods(profile.Id)
                    .FirstOrDefault(mod => mod.ModId.Equals(dependency.Id, StringComparison.OrdinalIgnoreCase));
                _database.SetProfileMod(profile.Id, dependency.Id, true, existing?.Priority ?? 0, existing?.Version ?? dependency.Version);
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
        var open = Button("Open Discover");
        open.Click += (_, _) => _tabs.SelectedIndex = 1;
        return open;
    }

    private void SetAllVisibleMods(ProfileRecord profile, IReadOnlyList<ModRecord> mods, bool enabled)
    {
        var visible = mods.Where(m => string.IsNullOrWhiteSpace(_modSearch) ||
            m.Name.Contains(_modSearch, StringComparison.OrdinalIgnoreCase) ||
            m.Id.Contains(_modSearch, StringComparison.OrdinalIgnoreCase)).ToList();
        var current = _database.GetProfileMods(profile.Id).ToDictionary(m => m.ModId, StringComparer.OrdinalIgnoreCase);
        var changed = false;
        var changedCount = 0;
        foreach (var mod in visible)
        {
            if (enabled)
            {
                current.TryGetValue(mod.Id, out var existing);
                if (existing?.Enabled == true) continue;
                _database.SetProfileMod(profile.Id, mod.Id, true, existing?.Priority ?? 0, existing?.Version ?? mod.Version);
                changed = true;
                changedCount++;
            }
            else
            {
                current.TryGetValue(mod.Id, out var existing);
                if (existing?.Enabled != true) continue;
                _database.SetProfileMod(profile.Id, mod.Id, false, existing?.Priority ?? 0, existing?.Version ?? mod.Version);
                changed = true;
                changedCount++;
            }
        }
        _changesPending |= changed;
        SetStatus(changed
            ? $"{(enabled ? "Enabled" : "Disabled")} {CountPhrase(changedCount, "mod")} in {profile.Name}."
            : "No mod changes were needed.");
        ShowDashboard(2);
    }

    private void SetSelectedMods(ProfileRecord profile, bool enabled)
    {
        var selected = _database.GetMods().Where(m => _selectedMods.Contains(m.Id)).ToList();
        var current = _database.GetProfileMods(profile.Id).ToDictionary(m => m.ModId, StringComparer.OrdinalIgnoreCase);
        var changed = false;
        var changedCount = 0;
        foreach (var mod in selected)
        {
            if (enabled)
            {
                current.TryGetValue(mod.Id, out var existing);
                if (existing?.Enabled == true) continue;
                _database.SetProfileMod(profile.Id, mod.Id, true, existing?.Priority ?? 0, existing?.Version ?? mod.Version);
                changed = true;
                changedCount++;
            }
            else
            {
                current.TryGetValue(mod.Id, out var existing);
                if (existing?.Enabled != true) continue;
                _database.SetProfileMod(profile.Id, mod.Id, false, existing?.Priority ?? 0, existing?.Version ?? mod.Version);
                changed = true;
                changedCount++;
            }
        }
        SetStatus(changed
            ? $"{(enabled ? "Enabled" : "Disabled")} {CountPhrase(changedCount, "selected mod")} in {profile.Name}."
            : "No mod changes were needed.");
        _changesPending |= changed;
        ShowDashboard(2);
    }

    private async Task RemoveSelectedMods()
    {
        var selected = _database.GetMods().Where(m => _selectedMods.Contains(m.Id)).ToList();
        if (selected.Count == 0) { SetStatus("Select at least one mod first.", error: true); return; }
        if (!await Confirm("Remove selected mods", $"Remove {CountPhrase(selected.Count, "selected mod")} from the manager and all setups?", "Remove Mods")) return;
        var game = _database.GetGame();
        var pendingBeforeRemoval = _changesPending;
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
        _changesPending = pendingBeforeRemoval;
        SetStatus($"Removed {CountPhrase(selected.Count, "mod")}.");
        ShowDashboard(2);
    }

    private Control BuildSettingsPage(GameRecord? game, Core.Common.Result<DeploymentPlan>? planResult)
    {
        var openLogs = Button("Open app logs");
        openLogs.Click += (_, _) => OpenFolder(_paths.Logs);
        var openIssues = Button("Report a problem on GitHub");
        openIssues.Click += (_, _) => OpenExternalLink("https://github.com/Brollyy/RagnaModManager/issues");
        return new ScrollViewer
        {
            Content = new StackPanel { Spacing = 14, Children =
            {
                new TextBlock { Text = "Get Ragnarock ready", FontSize = 24, FontWeight = FontWeight.SemiBold },
                Text(game is null
                    ? "Choose your game folder, apply your mod setup, and add support only when a mod needs it."
                    : "Your game is connected. Apply your mod setup, and add support only when a mod needs it."),
                BuildGameSetup(game),
                BuildProfileFileStatus(game, planResult),
                BuildScriptSupportSettings(game, planResult),
                CompactExpander("If something goes wrong", BuildDeploymentRecovery(game, planResult)),
                CompactExpander("Advanced launch options", BuildLaunchOptions()),
                CompactExpander("Troubleshooting", Section("Get help", Text("Open the logs when support asks for them, or report a problem on GitHub."), Wrap(openLogs, openIssues)))
            }}
        };
    }

    private Control BuildProfileFileStatus(GameRecord? game, Core.Common.Result<DeploymentPlan>? planResult)
    {
        var lines = new List<string>();
        if (game is null)
        {
            lines.Add("Choose your Ragnarock folder above first.");
        }
        else if (planResult is { Success: true, Value: not null } plan)
        {
            var applied = !_changesPending && IsDeploymentSynchronized(plan.Value);
            lines.Add(applied ? "Your current mod setup is active in Ragnarock." : "Your current mod setup has changes waiting to be applied.");
            lines.AddRange(plan.Value.Conflicts.Where(c => c.BlocksDeployment).Select(FriendlyDeploymentConflict));
            var unmanaged = CreateDeploymentService().GetUnmanagedFiles(game.InstallPath);
            if (unmanaged.Count == 1) lines.Add("There is 1 extra mod file in the game folder.");
            else if (unmanaged.Count > 1) lines.Add($"There are {unmanaged.Count} extra mod files in the game folder.");
        }
        else
        {
            lines.Add("We can’t check your mod setup yet. Resolve the issue shown here first.");
        }

        var apply = PrimaryButton("Apply setup");
        apply.IsEnabled = game is not null;
        apply.Click += (_, _) => DeployActiveProfile();
        var remove = Button("Clean up extra files");
        remove.IsEnabled = game is not null && CreateDeploymentService().GetUnmanagedFiles(game.InstallPath).Count > 0;
        remove.Click += async (_, _) =>
        {
            if (game is null) return;
            var unmanaged = CreateDeploymentService().GetUnmanagedFiles(game.InstallPath);
            if (unmanaged.Count == 0) return;
            if (!await Confirm("Clean up extra mod files", $"Remove {CountPhrase(unmanaged.Count, "file")} that are outside this app’s setup? This cannot be undone.", "Clean up files")) return;
            var result = CreateDeploymentService().RemoveUnmanagedFiles(game.InstallPath);
            SetStatus(result.Success ? $"Removed {CountPhrase(result.Value, "extra mod file")}." : result.Error ?? "Could not remove extra mod files.", !result.Success);
            ShowDashboard(4);
        };
        return Section("Apply your mod setup", Text(string.Join(Environment.NewLine, lines)), MutedText("Applying updates the files this app manages. Other game files are left alone."), Wrap(apply, remove));
    }

    private Control BuildScriptSupportSettings(GameRecord? game, Core.Common.Result<DeploymentPlan>? planResult)
    {
        var scriptMods = planResult?.Success == true && planResult.Value is not null
            ? planResult.Value.Items.Where(i => i.FileType.Equals("ue4ss-lua", StringComparison.OrdinalIgnoreCase) || i.FileType.Equals("ue4ss-dll", StringComparison.OrdinalIgnoreCase)).Select(i => i.ModId).Distinct(StringComparer.OrdinalIgnoreCase).ToList()
            : [];
        var status = game is null
            ? "Choose your game folder first."
            : _ue4ss.Detect(game.InstallPath).Installed ? "Script support is installed." : "Script support is not installed yet.";
        var scriptModNames = scriptMods.Select(id => _database.GetMod(id)?.Name ?? id).ToList();
        var note = scriptModNames.Count == 0
            ? "Your current mods don’t need anything extra."
            : $"{CountPhrase(scriptModNames.Count, "active mod")} { (scriptModNames.Count == 1 ? "needs" : "need") } script support: {string.Join(", ", scriptModNames)}.";
        var check = PrimaryButton("Check for support updates");
        check.IsEnabled = game is not null;
        check.Click += async (_, _) => await CheckUe4ssUpdates();
        var install = Button("Install support from file");
        install.IsEnabled = game is not null;
        install.Click += async (_, _) => await InstallUe4ssSupport();
        var controls = new List<Control>
        {
            Text($"Status: {status}"),
            MutedText(note),
            MutedText("Only needed by mods that use scripts."),
            Wrap(check, install)
        };
        var cached = _ue4ssReleases.GetCachedReleases();
        if (cached.Count > 0)
        {
            var picker = new ComboBox { ItemsSource = cached.Select(r => $"{r.Version} ({r.AssetName})").ToList(), SelectedIndex = 0, MinWidth = 280 };
            var use = Button("Use saved support version");
            use.IsEnabled = game is not null;
            use.Click += (_, _) =>
            {
                var current = _database.GetGame();
                if (current is null || picker.SelectedIndex < 0 || picker.SelectedIndex >= cached.Count) return;
                var result = _ue4ssReleases.InstallCachedRelease(current.InstallPath, cached[picker.SelectedIndex]);
                SetStatus(result.Success ? $"Installed saved script support {cached[picker.SelectedIndex].Version}." : result.Error ?? "Script support installation failed.", !result.Success);
                ShowDashboard(4);
            };
            controls.Add(CompactExpander("Use an older support release", Wrap(MutedText("Use this only when a mod requires an older support release."), picker, use), 120));
        }
        return Section("Support for script-based mods", controls.ToArray());
    }

    private Control BuildDeploymentRecovery(GameRecord? game, Core.Common.Result<DeploymentPlan>? planResult)
    {
        var summary = planResult is { Success: true, Value: not null }
            ? $"This session has {CountPhrase(planResult.Value.Conflicts.Count(c => c.BlocksDeployment), "issue")} that stop changes from being applied."
            : "Choose your Ragnarock folder before using recovery tools.";
        var rollback = Button("Undo last change");
        rollback.IsEnabled = game is not null;
        rollback.Click += (_, _) =>
        {
            var result = CreateDeploymentService().RollbackLatest();
            SetStatus(result.Success ? "The last change was undone." : result.Error ?? "Could not undo the last change.", !result.Success);
            ShowDashboard(4);
        };
        var reset = Button("Remove applied setup");
        reset.IsEnabled = game is not null;
        reset.Click += async (_, _) =>
        {
            if (!await Confirm("Remove applied setup", "Remove the setup currently applied by this app from the game folder? Backups are retained when possible.", "Remove setup")) return;
            var result = CreateDeploymentService().ResetDeployment();
            SetStatus(result.Success ? "The applied setup was removed." : result.Error ?? "Could not remove the applied setup.", !result.Success);
            ShowDashboard(4);
        };
        return Section("Undo or repair changes", Text(summary), MutedText("These actions affect files applied by this app. They do not touch unrelated game files."), Row(rollback, reset));
    }

    private Control BuildLaunchOptions()
    {
        var arguments = new TextBox { Text = _launchArguments, Watermark = "Optional launch arguments", MinWidth = 420 };
        var save = Button("Save launch options");
        save.Click += (_, _) =>
        {
            _launchArguments = arguments.Text ?? "";
            File.WriteAllText(_paths.LaunchArgumentsPath, _launchArguments);
            SetStatus(string.IsNullOrWhiteSpace(_launchArguments) ? "Launch arguments cleared." : "Launch arguments saved.");
        };
        return Section("Launch options", Text("Most players can leave this empty. These options are only used when launching Ragnarock directly; Steam launches ignore them."), Wrap(arguments, save));
    }

    private Control BuildDashboardSummary(GameRecord? game, ProfileRecord active, IReadOnlyList<ModRecord> mods, Core.Common.Result<DeploymentPlan>? planResult)
    {
        var enabled = GetEnabledMods().Count;
        var ready = game is not null && new RagnarockCompatibilityChecker().Check(game.InstallPath).CanManage;
        var synchronized = game is not null && !_changesPending && planResult is { Success: true, Value: not null } && IsDeploymentSynchronized(planResult.Value);
        var headline = game is null
            ? "Welcome! Let’s get Ragnarock ready for mods."
            : ready
                ? synchronized
                    ? enabled == 0
                        ? "Ragnarock is ready. Add a mod or launch without mods."
                        : "Your setup is ready. Launch Ragnarock or change your mods."
                    : "Ragnarock is ready, but your setup is not applied."
                : "One quick setup step remains before you can use mods.";
        var state = new TextBlock
        {
            Text = headline,
            FontSize = 18,
            FontWeight = FontWeight.SemiBold,
            Foreground = game is null || !ready ? new SolidColorBrush(Color.Parse("#FFB15C")) : new SolidColorBrush(Color.Parse("#4DE1C1")),
            TextWrapping = TextWrapping.Wrap
        };
        var metrics = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("*,*,*"),
            ColumnSpacing = 10,
            Children =
            {
                Cell(MetricCard("SETUP", active.Name, "Your current setup"), 0),
                Cell(MetricCard("ACTIVE MODS", enabled == 0 ? "None yet" : $"{enabled} active", $"{mods.Count} installed"), 1),
                Cell(MetricCard("PLAY STATUS", game is null ? "Set up first" : !ready ? "Needs setup" : synchronized ? "Ready to play" : "Apply changes", game is null ? "Choose your game folder" : "Ragnarock"), 2)
            }
        };
        var nextSteps = game is null
            ? "Set up your game folder, then choose your first mod. We’ll keep the rest of the setup out of your way."
            : ready
                ? synchronized
                    ? "Browse for something new, or launch Ragnarock with this setup."
                    : "Review your active mods, then apply the setup before launching Ragnarock."
                : "Choose a valid Ragnarock folder to continue.";
        return Section("Your session", state, metrics,
            Section("What to do next", Text(nextSteps)));
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
        "same-target" => "Two enabled mods try to replace the same game file. Turn one off before applying the setup.",
        "declared-conflict" => "Two enabled mods are incompatible. Turn one off before applying the setup.",
        "missing-dependency" => "An enabled mod needs another mod that is not installed. Install it before applying the setup.",
        "disabled-dependency" => "An enabled mod needs another mod that is turned off. Turn that mod on before applying the setup.",
        "dependency-version" => "An enabled mod needs a different version of another mod. Choose a compatible version before applying the setup.",
        "profile-version" => "A selected mod version is not installed. Install it or choose another version in Mods.",
        "manager-requirement" => "An enabled mod requires a newer version of RagnaModManager.",
        "ue4ss-requirement" => "An enabled mod needs extra support. Install it from Settings before applying the setup.",
        _ => "Your setup has an issue that must be resolved before it can be applied."
    };

    private Control BuildOfficialCatalog(IReadOnlyList<ModRecord> installed)
    {
        var refresh = PrimaryButton("Refresh mod list");
        refresh.Click += async (_, _) => await RefreshOfficialCatalog();
        var updateAll = Button("Update all");
        updateAll.Click += async (_, _) => await UpdateAllOfficial(installed);
        updateAll.IsEnabled = _officialCatalogResult?.Success == true;
        var installSelected = Button("Install selected");
        installSelected.Click += async (_, _) => await InstallSelectedOfficial();
        installSelected.IsEnabled = _selectedCatalogMods.Count > 0;
        var search = new TextBox { Watermark = "Search community mods…", Text = _librarySearch, MinWidth = 300 };
        search.KeyDown += (_, e) =>
        {
            if (e.Key == Key.Enter)
            {
                _librarySearch = search.Text?.Trim() ?? "";
                ShowDashboard(1);
            }
        };
        var searchAction = Button("Search");
        searchAction.Click += (_, _) => { _librarySearch = search.Text?.Trim() ?? ""; ShowDashboard(1); };
        var clearSearch = Button("Clear");
        clearSearch.Click += (_, _) => { _librarySearch = ""; ShowDashboard(1); };
        var searchBox = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto"), Children = { Cell(search, 0), Cell(clearSearch, 1) } };
        var content = new StackPanel { Spacing = 10 };
        content.Children.Add(Text("Find community mods for Ragnarock and install them with one click."));
        content.Children.Add(MutedText("Community content is maintained by mod authors and players."));
        content.Children.Add(Wrap(searchBox, searchAction, refresh, installSelected, updateAll));
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
            if (visibleMods.Count == 0)
            {
                var clearSearchButton = Button("Clear search");
                clearSearchButton.IsEnabled = !string.IsNullOrWhiteSpace(_librarySearch);
                clearSearchButton.Click += (_, _) => { _librarySearch = ""; ShowDashboard(1); };
                content.Children.Add(EmptyState(
                    string.IsNullOrWhiteSpace(_librarySearch) ? "No community mods yet." : "No mods match that search.",
                    string.IsNullOrWhiteSpace(_librarySearch) ? "Check back when the community catalog has something new." : "Try a different mod name.",
                    clearSearchButton));
            }
            else
            {
                content.Children.Add(BuildCatalogHeader(visibleMods));
                foreach (var mod in SortCatalog(visibleMods))
                    content.Children.Add(OfficialModRow(mod, installed));
            }
        }

        return new StackPanel
        {
            Spacing = 14,
            Children =
            {
                new TextBlock { Text = "Discover", FontSize = 24, FontWeight = FontWeight.SemiBold },
                Section("Discover mods", content)
            }
        };
    }

    private Control BuildCatalogHeader(IReadOnlyList<CatalogMod> visibleMods)
    {
        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("28,Auto,*,*,120,120,220"), ColumnSpacing = 8, Margin = new Thickness(8, 4) };
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
        grid.Children.Add(Cell(MutedText("Description"), 3));
        grid.Children.Add(Cell(SortButton("Latest", "Latest"), 4));
        grid.Children.Add(Cell(SortButton("Installed", "Installed"), 5));
        grid.Children.Add(Cell(MutedText("Actions"), 6));
        return new Border
        {
            HorizontalAlignment = HorizontalAlignment.Stretch,
            Background = new SolidColorBrush(Color.Parse("#17243B")),
            BorderBrush = new SolidColorBrush(Color.Parse("#2A3A55")),
            BorderThickness = new Thickness(0, 0, 0, 1),
            Child = grid
        };
    }

    private Button SortButton(string label, string column)
    {
        var button = Button($"{label}{(_catalogSortColumn == column ? (_catalogSortDescending ? " (descending)" : " (ascending)") : "")}");
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
        SetStatus($"Installed {CountPhrase(selected.Count, "selected community mod")} and their dependencies.");
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
        SetStatus($"Updated {CountPhrase(available.Count, "community mod")}.");
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
        var check = new CheckBox { IsChecked = _selectedCatalogMods.Contains(catalogMod.Id), VerticalAlignment = VerticalAlignment.Center };
        check.Click += (_, _) => ToggleCatalogSelection(catalogMod, check.IsChecked == true);
        var expand = new ToggleButton { Content = "›", Width = 24, Height = 24, Padding = new Thickness(0), HorizontalContentAlignment = HorizontalAlignment.Center };
        var compact = new Grid { ColumnDefinitions = new ColumnDefinitions("28,Auto,*,*,120,120,220"), ColumnSpacing = 8, Margin = new Thickness(8, 5), HorizontalAlignment = HorizontalAlignment.Stretch };
        compact.Children.Add(expand);
        compact.Children.Add(Cell(check, 1));
        compact.Children.Add(Cell(new TextBlock { Text = catalogMod.Name, FontWeight = FontWeight.SemiBold }, 2));
        compact.Children.Add(Cell(new TextBlock { Text = catalogMod.Description ?? "No description provided.", Foreground = Brushes.DimGray, TextWrapping = TextWrapping.Wrap, MaxHeight = 38 }, 3));
        compact.Children.Add(Cell(new TextBlock { Text = latest?.Version ?? "—" }, 4));
        compact.Children.Add(Cell(new TextBlock { Text = installedVersions.Count switch { 0 => "Not installed", 1 => installedVersions[0].Version, _ => $"{installedVersions.Count} versions" }, Foreground = updateAvailable ? Brushes.DarkGoldenrod : Brushes.DimGray }, 5));
        compact.Children.Add(Cell(new TextBlock { Text = "Select a release below", Foreground = Brushes.DimGray }, 6));
        var details = Button("Details");
        details.Click += async (_, _) => await ShowDetails(catalogMod.Name, $"{catalogMod.Description ?? "No description."}{Environment.NewLine}Author: {catalogMod.Author ?? "Unknown"}{Environment.NewLine}License: {catalogMod.License ?? "Not specified"}{Environment.NewLine}Source: {catalogMod.SourceUrl ?? "Not specified"}{Environment.NewLine}{Environment.NewLine}Dependencies:{Environment.NewLine}{FormatDependencies(catalogMod.Dependencies)}{Environment.NewLine}{Environment.NewLine}Available releases: {string.Join(", ", releases.Select(r => $"{r.Version}{(r.SizeBytes is null ? "" : $" ({r.SizeBytes / 1024} KB)")}"))}{Environment.NewLine}{Environment.NewLine}{releases[0].Changelog ?? "No release notes provided."}", catalogMod.SourceUrl);
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
        SetLibraryStatus(_officialCatalogResult.Success ? $"Community catalog loaded: {CountPhrase(_officialCatalogResult.Value!.Mods.Count, "mod")}. Last checked {_catalogLastChecked}." : $"Community catalog unavailable (last checked {_catalogLastChecked ?? "never"}): {_officialCatalogResult.Error}", !_officialCatalogResult.Success);
    }

    private void SetLibraryStatus(string message, bool error = false)
    {
        if (_libraryStatus is not null)
        {
            _libraryStatus.Text = message;
            _libraryStatus.Foreground = error ? new SolidColorBrush(Color.Parse("#FF8B8B")) : new SolidColorBrush(Color.Parse("#4DE1C1"));
        }
    }

    private Control BuildQuickActions(GameRecord? game)
    {
        var actions = new List<Control>();
        if (game is null)
        {
            var setup = PrimaryButton("Set up automatically");
            setup.Click += (_, _) => SetupAutomatically();
            actions.Add(setup);
        }

        var import = PrimaryButton("Add a mod");
        import.Click += async (_, _) => await ImportModPackage();

        var openGame = Button("Open game folder");
        var openMods = Button("Open mod library");
        openGame.IsEnabled = game is not null;
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
                        new TextBlock { Text = game is null ? "Start here" : "Manage your mods", FontSize = 20, FontWeight = FontWeight.SemiBold },
                        MutedText(game is null ? "One setup step, then you can browse and play." : "Choose a mod, turn it on, or launch Ragnarock.")
                    }
                }, 0),
                Cell(Row(actions.Concat([import, openGame, openMods]).ToArray()), 1)
            }
        });
    }

    private void UpdatePendingChangesBar()
    {
        var content = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 12, VerticalAlignment = VerticalAlignment.Center };
        content.Children.Add(new TextBlock { Text = "This setup has unapplied changes.", FontWeight = FontWeight.SemiBold, VerticalAlignment = VerticalAlignment.Center });
        var apply = PrimaryButton("Apply changes");
        apply.IsEnabled = _database.GetGame() is not null;
        apply.Click += (_, _) => DeployActiveProfile();
        content.Children.Add(apply);
        var revert = Button("Revert changes");
        revert.Click += (_, _) => RevertPendingChanges();
        content.Children.Add(revert);
        _pendingChangesBar.Child = content;
        _pendingChangesBar.IsVisible = _changesPending;
        _pendingChangesBar.Background = new SolidColorBrush(Color.Parse("#3B2D1B"));
        _pendingChangesBar.BorderBrush = new SolidColorBrush(Color.Parse("#FFB15C"));
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
        SetStatus("Ragnarock is ready. Browse Discover or import a mod.");
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

        return Section("Connect Ragnarock",
            status,
            Wrap(pathBox, browse, detect, save));
    }

    private Control BuildModList(ProfileRecord active, IReadOnlyList<ModRecord> mods)
    {
        var state = _database.GetProfileMods(active.Id).ToDictionary(m => m.ModId, StringComparer.OrdinalIgnoreCase);
        var list = new StackPanel { Spacing = 10 };

        if (mods.Count == 0)
        {
            var addFirst = PrimaryButton("Add your first mod");
            addFirst.Click += async (_, _) => await ImportModPackage();
            list.Children.Add(EmptyState("No mods installed yet.", "Add a mod to start building this setup.", addFirst));
        }
        else
        {
            var visibleCount = 0;
            foreach (var missing in state.Values.Where(p => _database.GetMod(p.ModId) is null))
            {
                var removeMissing = Button("Remove from setup");
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
                visibleCount++;
            }
            if (visibleCount == 0 && !string.IsNullOrWhiteSpace(_modSearch))
            {
                var clearSearch = Button("Clear search");
                clearSearch.Click += (_, _) => { _modSearch = ""; ShowDashboard(2); };
                list.Children.Add(EmptyState("No installed mods match that search.", "Try a different mod name.", clearSearch));
            }
        }

        return Section($"Mods in {active.Name}", list);
    }

    private Control ModRow(ProfileRecord active, ModRecord mod, ProfileModRecord? profileMod)
    {
        var versions = _database.GetModVersions(mod.Id);
        var selectedVersion = profileMod?.Version ?? mod.Version;
        var versionPicker = new ComboBox
        {
            ItemsSource = versions.Select(v => v.Version).ToList(),
            SelectedIndex = Math.Max(0, versions.Select(v => v.Version).ToList().FindIndex(v => string.Equals(v, selectedVersion, StringComparison.OrdinalIgnoreCase))),
            MinWidth = 110
        };
        versionPicker.SelectionChanged += (_, _) =>
        {
            if (versionPicker.SelectedIndex < 0 || versionPicker.SelectedIndex >= versions.Count) return;
            var selectedVersion = versions[versionPicker.SelectedIndex].Version;
            _database.SetProfileMod(active.Id, mod.Id, profileMod?.Enabled == true, profileMod?.Priority ?? 0, selectedVersion);
            _changesPending = true;
            SetStatus($"Using {mod.Name} version {selectedVersion} in {active.Name}.");
            ShowDashboard(2);
        };
        var selected = new CheckBox { IsChecked = _selectedMods.Contains(mod.Id), Content = "Select", VerticalAlignment = VerticalAlignment.Center };
        selected.Click += (_, _) => { if (selected.IsChecked == true) _selectedMods.Add(mod.Id); else _selectedMods.Remove(mod.Id); };
        var enabled = new CheckBox
        {
            IsChecked = profileMod?.Enabled == true,
            Content = profileMod?.Enabled == true ? "Enabled" : "Disabled",
            VerticalAlignment = VerticalAlignment.Center
        };
        enabled.Click += (_, _) =>
        {
            var isEnabled = enabled.IsChecked == true;
            if (isEnabled)
                _database.SetProfileMod(active.Id, mod.Id, true, profileMod?.Priority ?? 0, profileMod?.Version ?? mod.Version);
            else
                _database.SetProfileMod(active.Id, mod.Id, false, profileMod?.Priority ?? 0, profileMod?.Version ?? mod.Version);
            _changesPending = true;
            SetStatus($"{mod.Name} is now {(isEnabled ? "enabled" : "disabled")} in {active.Name}.");
            ShowDashboard();
        };

        var moveEarlier = Button("Move up");
        var enabledOrder = _database.GetProfileMods(active.Id)
            .Where(item => item.Enabled)
            .OrderBy(item => item.Priority)
            .ThenBy(item => item.ModId, StringComparer.OrdinalIgnoreCase)
            .ToList();
        var orderIndex = enabledOrder.FindIndex(item => item.ModId.Equals(mod.Id, StringComparison.OrdinalIgnoreCase));
        moveEarlier.IsEnabled = profileMod?.Enabled == true && orderIndex > 0;
        moveEarlier.Click += (_, _) => ChangePriority(active.Id, mod, profileMod?.Priority ?? 0, -1);

        var moveLater = Button("Move down");
        moveLater.IsEnabled = profileMod?.Enabled == true && orderIndex >= 0 && orderIndex < enabledOrder.Count - 1;
        moveLater.Click += (_, _) => ChangePriority(active.Id, mod, profileMod?.Priority ?? 0, 1);

        var remove = Button("Remove");
        remove.Click += async (_, _) => await RemoveMod(mod);
        var details = Button("Details");
        details.Click += async (_, _) => await ShowInstalledModDetails(mod);

        var modActions = new StackPanel
        {
            Spacing = 6,
            Children = { Wrap(selected, enabled, moveEarlier, moveLater, details, remove) }
        };
        if (versions.Count > 1)
        {
            modActions.Children.Add(CompactExpander(
                "Choose a different version",
                Wrap(MutedText("Most players should leave this on the latest version."), versionPicker),
                120,
                $"mod-version:{mod.Id}"));
        }

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
                            Text = $"Version {selectedVersion}" + (string.IsNullOrWhiteSpace(mod.Author) ? "" : $" by {mod.Author}"),
                            Foreground = new SolidColorBrush(Color.Parse("#9AAAC2")),
                            TextWrapping = TextWrapping.Wrap
                        },
                        new TextBlock
                        {
                            Text = profileMod?.Enabled == true ? "Enabled in this setup" : "Disabled in this setup",
                            Foreground = profileMod?.Enabled == true ? new SolidColorBrush(Color.Parse("#4DE1C1")) : new SolidColorBrush(Color.Parse("#9AAAC2")),
                            TextWrapping = TextWrapping.Wrap
                        }
                    }
                }, 0),
                Cell(modActions, 1)
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
                ? "No mods turned on"
                : CountPhrase(enabled, "active mod") + (names.Count == 0 ? "" : $"{Environment.NewLine}{string.Join(", ", names)}");

            var profileNameBox = new TextBox { Text = profile.Name, MinWidth = 220 };
            var rename = Button("Rename");
            rename.Click += (_, _) =>
            {
                try
                {
                    _database.RenameProfile(profile.Id, profileNameBox.Text ?? "");
                    SetStatus($"Renamed setup to {profileNameBox.Text?.Trim()}.");
                    ShowDashboard(3);
                }
                catch (InvalidOperationException ex)
                {
                    SetStatus(ex.Message, error: true);
                }
            };
            var use = Button(profile.Id == active.Id ? "Active" : "Use setup");
            use.IsEnabled = profile.Id != active.Id;
            use.Click += (_, _) =>
            {
                _database.SetActiveProfile(profile.Id);
                TryRedeployAfterProfileChange(profile);
                ShowDashboard(3);
            };
            var clone = Button("Duplicate");
            clone.Click += (_, _) => CreateProfile(profile.Name + " Copy", profile);
            var delete = Button("Delete");
            delete.IsEnabled = profile.Id != active.Id;
            delete.Click += async (_, _) =>
            {
                if (!await Confirm("Delete setup", $"Delete setup ‘{profile.Name}’ and its saved mod selections? This cannot be undone.", "Delete setup")) return;
                try
                {
                    _database.DeleteProfile(profile.Id);
            SetStatus($"Deleted setup {profile.Name}.");
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
                    Cell(Wrap(use, clone, delete), 1)
                }
            }));
        }

        var name = new TextBox { Watermark = "Name this setup", MinWidth = 240 };
        var create = PrimaryButton("Create setup");
        create.Click += (_, _) => CreateProfile(name.Text?.Trim() ?? "", null);
        list.Children.Add(Section("Create another setup", Text("Save a different group of mods for another kind of play session."), Wrap(name, create)));
        var export = Button("Export setup");
        export.Click += async (_, _) => await ExportCurrentProfile(active);
        var import = Button("Import setup");
        import.Click += async (_, _) => await ImportProfile();
        list.Children.Add(Section("Share or back up setups", Text("Save this setup to a file, or bring one in from another installation."), Row(export, import)));
        return new StackPanel
        {
            Spacing = 14,
            Children =
            {
                new TextBlock { Text = "Setups", FontSize = 24, FontWeight = FontWeight.SemiBold },
                Text("Save different groups of mods for different ways to play."),
                list
            }
        };
    }

    private async Task ExportCurrentProfile(ProfileRecord profile)
    {
        var file = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = "Export setup",
            SuggestedFileName = profile.Id + ".json",
            FileTypeChoices = [new FilePickerFileType("JSON setup") { Patterns = ["*.json"] }]
        });
        if (file is null) return;
        var result = _database.ExportProfile(profile.Id, file.Path.LocalPath);
        SetStatus(result.Success ? $"Exported {profile.Name}." : result.Error ?? "Setup export failed.", !result.Success);
    }

    private async Task ImportProfile()
    {
        var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Import setup",
            AllowMultiple = false,
            FileTypeFilter = [new FilePickerFileType("JSON setups") { Patterns = ["*.json"] }]
        });
        if (files.Count == 0) return;
        var stem = Path.GetFileNameWithoutExtension(files[0].Name);
        var id = MakeProfileId(stem);
        var suffix = 2;
        var candidate = id;
        while (_database.GetProfile(candidate) is not null) candidate = $"{id}-{suffix++}";
        var result = _database.ImportProfile(files[0].Path.LocalPath, candidate, stem);
        SetStatus(result.Success ? $"Imported setup {stem}." : result.Error ?? "Setup import failed.", !result.Success);
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
        var sourceUrl = _officialCatalogResult?.Value?.Mods.FirstOrDefault(catalogMod => catalogMod.Id.Equals(mod.Id, StringComparison.OrdinalIgnoreCase))?.SourceUrl;
        await ShowDetails($"{m.Name} {m.Version}", $"Author: {m.Author ?? "Unknown"}{Environment.NewLine}Package size: {size}{Environment.NewLine}{Environment.NewLine}Description: {m.Description ?? "None"}{Environment.NewLine}{Environment.NewLine}Dependencies:{Environment.NewLine}{dependencies}{Environment.NewLine}{Environment.NewLine}Conflicts: {conflicts}{Environment.NewLine}Files: {m.Files.Count}", sourceUrl);
    }

    private async Task ShowDetails(string title, string message, string? sourceUrl = null)
    {
        var dialog = new Window { Title = title, Width = 560, Height = 420, WindowStartupLocation = WindowStartupLocation.CenterOwner };
        var close = PrimaryButton("Close");
        close.Click += (_, _) => dialog.Close();
        var actions = new List<Control>();
        if (Uri.TryCreate(sourceUrl, UriKind.Absolute, out var sourceUri) && (sourceUri.Scheme == Uri.UriSchemeHttp || sourceUri.Scheme == Uri.UriSchemeHttps))
        {
            var openSource = Button("Open source link");
            openSource.Click += (_, _) => OpenExternalLink(sourceUri.ToString());
            actions.Add(openSource);
        }
        actions.Add(close);
        dialog.Content = new StackPanel { Margin = new Thickness(18), Spacing = 14, Children = { new TextBlock { Text = title, FontSize = 20, FontWeight = FontWeight.SemiBold }, new ScrollViewer { Content = Text(message) }, Wrap(actions.ToArray()) } };
        await dialog.ShowDialog(this);
    }

    private void OpenExternalLink(string url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
        {
            SetStatus("This link is not valid.", error: true);
            return;
        }

        try
        {
            Process.Start(new ProcessStartInfo(uri.ToString()) { UseShellExecute = true });
        }
        catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            SetStatus($"Could not open the link: {ex.Message}", error: true);
        }
    }

    private void CreateProfile(string cleanName, ProfileRecord? source)
    {
        if (string.IsNullOrWhiteSpace(cleanName))
        {
            SetStatus("Enter a setup name first.", error: true);
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
                    _database.SetProfileMod(candidate, mod.ModId, mod.Enabled, mod.Priority, mod.Version);
            }
            _database.SetActiveProfile(candidate);
            SetStatus($"Created setup {cleanName}. Choose its mods below.");
            ShowDashboard(2);
        }
        catch (Exception ex) when (ex is InvalidOperationException)
        {
            SetStatus(ex.Message, error: true);
        }
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
                    $"{CountPhrase(unmanaged.Count, "existing file")} {(unmanaged.Count == 1 ? "is" : "are")} outside this app’s setup. Applying this setup will overwrite only those listed target files; unrelated files in the game folder will be left untouched. Continue?\n\n{string.Join(Environment.NewLine, unmanaged.Take(5))}{(unmanaged.Count > 5 ? Environment.NewLine + "…" : "")}",
                    "Reconcile and Apply"))
            {
                result = CreateDeploymentService().Deploy(game.InstallPath, allowUnmanagedFiles: true);
            }
        }
        if (result.Success)
        {
            _changesPending = false;
            CaptureAppliedProfileSnapshot(_database.GetActiveProfile().Id);
        }
        SetStatus(result.Success ? "Changes applied to Ragnarock." : result.Error ?? "Apply failed.", !result.Success);
        ShowDashboard();
    }

    private void RevertPendingChanges()
    {
        var active = _database.GetActiveProfile();
        if (!_appliedProfileSnapshots.TryGetValue(active.Id, out var snapshot))
        {
            SetStatus("There is no saved applied setup to restore.", error: true);
            return;
        }

        var originalById = snapshot.ToDictionary(mod => mod.ModId, StringComparer.OrdinalIgnoreCase);
        foreach (var current in _database.GetProfileMods(active.Id))
        {
            if (!originalById.ContainsKey(current.ModId))
                _database.RemoveProfileMod(active.Id, current.ModId);
        }

        foreach (var original in snapshot)
            _database.SetProfileMod(active.Id, original.ModId, original.Enabled, original.Priority, original.Version);

        _changesPending = false;
        SetStatus($"Reverted unapplied changes in {active.Name}.");
        ShowDashboard();
    }

    private void EnsureAppliedProfileSnapshot(string profileId)
    {
        if (!_appliedProfileSnapshots.ContainsKey(profileId))
            CaptureAppliedProfileSnapshot(profileId);
    }

    private void CaptureAppliedProfileSnapshot(string profileId)
    {
        _appliedProfileSnapshots[profileId] = _database.GetProfileMods(profileId).ToList();
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

        if (_changesPending)
        {
            SetStatus("Apply your pending changes before launching Ragnarock.", error: true);
            return;
        }

        var preview = CreateDeploymentService().Preview(game.InstallPath);
        if (!preview.Success || preview.Value is null || preview.Value.Conflicts.Any(conflict => conflict.BlocksDeployment))
        {
            SetStatus("Resolve the setup issues before launching Ragnarock.", error: true);
            return;
        }

        var result = new RagnarockLauncher().Launch(game.InstallPath, _launchArguments);
        SetStatus(result.Success ? "Launch requested." : result.Error ?? "Launch failed.", !result.Success);
    }

    private void ChangePriority(string profileId, ModRecord mod, int currentPriority, int delta)
    {
        var ordered = _database.GetProfileMods(profileId)
            .Where(profileMod => profileMod.Enabled)
            .OrderBy(profileMod => profileMod.Priority)
            .ThenBy(profileMod => profileMod.ModId, StringComparer.OrdinalIgnoreCase)
            .ToList();
        var currentIndex = ordered.FindIndex(profileMod => profileMod.ModId.Equals(mod.Id, StringComparison.OrdinalIgnoreCase));
        var targetIndex = currentIndex + delta;
        if (currentIndex < 0 || targetIndex < 0 || targetIndex >= ordered.Count) return;

        (ordered[currentIndex], ordered[targetIndex]) = (ordered[targetIndex], ordered[currentIndex]);
        for (var index = 0; index < ordered.Count; index++)
        {
            var item = ordered[index];
            var source = _database.GetMod(item.ModId);
            _database.SetProfileMod(profileId, item.ModId, true, index * 10, item.Version ?? source?.Version);
        }
        _changesPending = true;
        SetStatus($"{mod.Name} load order updated. Apply changes when ready.");
        ShowDashboard();
    }

    private async Task RemoveMod(ModRecord mod)
    {
        var confirmed = await Confirm(
            "Remove mod",
            $"Remove {mod.Name} {mod.Version} from the manager and all setups? This also removes files managed by this app.",
            "Remove Mod");
        if (!confirmed) return;

        var game = _database.GetGame();
        var pendingBeforeRemoval = _changesPending;
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
        _changesPending = pendingBeforeRemoval;
        SetStatus($"Removed {mod.Name}.");
        ShowDashboard(2);
    }

    private void TryRedeployAfterProfileChange(ProfileRecord profile)
    {
        var game = _database.GetGame();
        if (game is null)
        {
            SetStatus($"Using setup {profile.Name}. Choose your Ragnarock folder before applying it.");
            return;
        }

        var result = CreateDeploymentService().Deploy(game.InstallPath);
        _changesPending = !result.Success;
        if (result.Success) CaptureAppliedProfileSnapshot(profile.Id);
        SetStatus(result.Success ? $"Using setup {profile.Name}; changes applied." : $"Using setup {profile.Name}, but apply failed: {result.Error}", !result.Success);
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
        _status.Foreground = error ? new SolidColorBrush(Color.Parse("#FF8B8B")) : new SolidColorBrush(Color.Parse("#4DE1C1"));
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

    private static string CountPhrase(int count, string singular) =>
        $"{count} {singular}{(count == 1 ? "" : "s")}";

    private static TextBlock MutedText(string text) => new()
    {
        Text = text,
        Foreground = new SolidColorBrush(Color.Parse("#9AAAC2")),
        TextWrapping = TextWrapping.Wrap
    };

    private Control CompactExpander(string header, Control content, double maxHeight = 220, string? stateKey = null)
    {
        var key = string.IsNullOrWhiteSpace(stateKey) ? header : stateKey;
        var expanded = _expandedAccordions.Contains(key);
        var disclosureState = new TextBlock
        {
            Text = expanded ? "Hide" : "Show",
            Foreground = new SolidColorBrush(Color.Parse("#9AAAC2")),
            VerticalAlignment = VerticalAlignment.Center
        };
        var toggle = new ToggleButton
        {
            Tag = header,
            IsChecked = expanded,
            Classes = { "disclosure" },
            Content = new Grid
            {
                ColumnDefinitions = new ColumnDefinitions("*,Auto"),
                ColumnSpacing = 10,
                Children =
                    {
                        Cell(new TextBlock { Text = header, FontWeight = FontWeight.SemiBold, VerticalAlignment = VerticalAlignment.Center }, 0),
                    Cell(disclosureState, 1)
                }
            }
        };
        var body = new ScrollViewer
        {
            MaxHeight = maxHeight,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            IsVisible = expanded,
            Content = content
        };
        var panel = new StackPanel { Spacing = expanded ? 10 : 0, Children = { toggle, body } };
        void SetExpanded(bool isExpanded)
        {
            expanded = isExpanded;
            body.IsVisible = expanded;
            panel.Spacing = expanded ? 10 : 0;
            disclosureState.Text = expanded ? "Hide" : "Show";
            if (expanded) _expandedAccordions.Add(key);
            else _expandedAccordions.Remove(key);
        }
        toggle.PropertyChanged += (_, change) =>
        {
            if (change.Property == ToggleButton.IsCheckedProperty)
                SetExpanded(change.GetNewValue<bool?>() == true);
        };
        toggle.Click += (_, _) => SetExpanded(toggle.IsChecked == true);

        return new Border
        {
            Background = new SolidColorBrush(Color.Parse("#17243B")),
            BorderBrush = new SolidColorBrush(Color.Parse("#2A3A55")),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(10),
            Padding = new Thickness(14, 11),
            Child = panel
        };
    }

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
        button.Classes.Add("accent");
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
            new TextBlock { Text = message, Foreground = new SolidColorBrush(Color.Parse("#9AAAC2")), TextWrapping = TextWrapping.Wrap, HorizontalAlignment = HorizontalAlignment.Center },
            action
        }
    });

    private static Border Card(Control content) => new()
    {
        BorderBrush = new SolidColorBrush(Color.Parse("#2A3A55")),
        BorderThickness = new Thickness(1),
        CornerRadius = new CornerRadius(12),
        BoxShadow = BoxShadows.Parse("0 4 12 0 #15000000"),
        Background = new SolidColorBrush(Color.Parse("#111B2E")),
        Padding = new Thickness(16),
        Child = content
    };

    private static Border MetricCard(string label, string value, string detail) => new()
    {
        Background = new SolidColorBrush(Color.Parse("#17243B")),
        BorderBrush = new SolidColorBrush(Color.Parse("#2A3A55")),
        BorderThickness = new Thickness(1),
        CornerRadius = new CornerRadius(10),
        BoxShadow = BoxShadows.Parse("0 3 10 0 #12000000"),
        Padding = new Thickness(14),
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
