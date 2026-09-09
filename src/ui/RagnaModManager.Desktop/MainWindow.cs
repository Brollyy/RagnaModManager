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

    private TabControl _tabs = null!;
    private TextBlock _status = null!;
    private Button? _launchButton;
    private int _selectedTab;
    private bool _rebuildingTabs;
    private bool _changesPending;
    private string _modSearch = "";
    private string? _catalogLastChecked;
    private string _librarySearch = "";
    private readonly HashSet<string> _selectedCatalogMods = new(StringComparer.OrdinalIgnoreCase);
    private string _launchArguments = "";
    private readonly HashSet<string> _selectedMods = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, IReadOnlyList<ProfileModRecord>> _appliedProfileSnapshots = new(StringComparer.OrdinalIgnoreCase);
    private readonly MainWindowViewModel _viewModel = new();

    public MainWindow()
    {
        InitializeComponent();
        // Some Linux window managers restore a stale compact geometry before the
        // first layout pass. Keep the intended desktop canvas explicit.
        Width = 1240;
        Height = 820;
        MinWidth = 900;
        MinHeight = 640;
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
            Content = new TextBlock { Text = $"Startup failed\n\n{ex.Message}", TextWrapping = TextWrapping.Wrap, Margin = new Thickness(24) };
        }
    }

    private void InitializeComponent() => AvaloniaXamlLoader.Load(this);

    private void BuildShell()
    {
        _tabs = this.FindControl<TabControl>("NavigationTabs") ?? throw new InvalidOperationException("Navigation tabs were not loaded.");
        _status = this.FindControl<TextBlock>("StatusText") ?? throw new InvalidOperationException("Status host was not loaded.");
        _launchButton = this.FindControl<Button>("LaunchButton") ?? throw new InvalidOperationException("Launch button was not loaded.");
        _launchButton.Click += (_, _) => LaunchGame();
        _viewModel.ApplyChanges = new RelayCommand(DeployActiveProfile);
        _viewModel.RevertChanges = new RelayCommand(RevertPendingChanges);

        _tabs.SelectionChanged += (_, _) =>
        {
            if (!_rebuildingTabs)
            {
                _selectedTab = Math.Max(0, _tabs.SelectedIndex);
                _status.Text = "";
            }
        };

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
            RefreshDeclarativeViews(game, active, mods, planResult);
            _tabs.SelectedIndex = Math.Min(_selectedTab, _tabs.Items.Count - 1);
        }
        finally
        {
            _rebuildingTabs = false;
        }
        UpdatePendingChangesBar();
    }

    private void RefreshDeclarativeViews(GameRecord? game, ProfileRecord active, IReadOnlyList<ModRecord> mods, Core.Common.Result<DeploymentPlan>? planResult)
    {
        PopulateDashboardModel(game, active, mods, planResult);
        PopulateModsModel(active, mods, planResult);
        PopulateDiscoverModel(mods);
        PopulateProfilesModel(active);
        PopulateSettingsModel(game, planResult);

        _viewModel.RefreshPages();
    }

    private void PopulateDashboardModel(GameRecord? game, ProfileRecord active, IReadOnlyList<ModRecord> mods, Core.Common.Result<DeploymentPlan>? planResult)
    {
        var enabled = GetEnabledMods().Count;
        var ready = game is not null && new RagnarockCompatibilityChecker().Check(game.InstallPath).CanManage;
        var synchronized = game is not null && !_changesPending && planResult is { Success: true, Value: not null } && IsDeploymentSynchronized(planResult.Value);
        var dashboard = _viewModel.Dashboard;
        dashboard.SetupName = active.Name;
        dashboard.ActiveMods = enabled == 0 ? "None yet" : $"{enabled} active";
        dashboard.InstalledMods = $"{mods.Count} installed";
        dashboard.PlayStatus = game is null ? "Set up first" : !ready ? "Needs setup" : synchronized ? "Ready to play" : "Apply changes";
        dashboard.PlayDetail = game is null ? "Choose your game folder" : "Ragnarock";
        dashboard.ShowSetupAction = game is null;
        dashboard.Headline = game is null
            ? "Welcome! Let’s get Ragnarock ready for mods."
            : ready
                ? synchronized
                    ? enabled == 0 ? "Ragnarock is ready. Add a mod or launch without mods." : "Your setup is ready. Launch Ragnarock or change your mods."
                    : "Ragnarock is ready, but your setup is not applied."
                : "One quick setup step remains before you can use mods.";
        dashboard.HeadlineBrush = game is null || !ready ? "#FFB15C" : "#4DE1C1";
        dashboard.NextSteps = game is null
            ? "Set up your game folder, then choose your first mod. We’ll keep the rest of the setup out of your way."
            : ready
                ? synchronized ? "Browse for something new, or launch Ragnarock with this setup." : "Review your active mods, then apply the setup before launching Ragnarock."
                : "Choose a valid Ragnarock folder to continue.";
        dashboard.ShowDeploymentNotice = false;
        dashboard.DeploymentDetails = "";
        if (game is not null && planResult is { Success: true, Value: not null } plan)
        {
            var conflicts = plan.Value.Conflicts.Where(c => c.BlocksDeployment).Select(FriendlyDeploymentConflict).ToList();
            var warnings = plan.Value.Warnings.Where(w => !w.StartsWith("Found ", StringComparison.Ordinal)).ToList();
            dashboard.ShowDeploymentNotice = !synchronized || conflicts.Count > 0 || warnings.Count > 0;
            dashboard.DeploymentMessage = synchronized ? "Your setup is active." : "Your setup is not active.";
            dashboard.DeploymentDetails = string.Join(Environment.NewLine, conflicts.Concat(warnings.Select(w => "Notice: " + w)));
        }

        dashboard.SetupAutomatically = new RelayCommand(SetupAutomatically);
        dashboard.AddMod = new AsyncRelayCommand(ImportModPackage);
        dashboard.OpenGameFolder = new RelayCommand(() =>
        {
            var current = _database.GetGame();
            if (current is null) SetStatus("Choose your Ragnarock folder first.", error: true);
            else OpenFolder(current.InstallPath);
        });
        dashboard.OpenModLibrary = new RelayCommand(() => OpenFolder(_paths.ModLibrary));
    }

    private void PopulateModsModel(ProfileRecord active, IReadOnlyList<ModRecord> mods, Core.Common.Result<DeploymentPlan>? planResult)
    {
        var model = _viewModel.Mods;
        model.Search = _modSearch;
        model.Items.Clear();
        var visible = mods.Where(m => string.IsNullOrWhiteSpace(_modSearch) || m.Name.Contains(_modSearch, StringComparison.OrdinalIgnoreCase) || m.Id.Contains(_modSearch, StringComparison.OrdinalIgnoreCase)).ToList();
        var state = _database.GetProfileMods(active.Id).ToDictionary(m => m.ModId, StringComparer.OrdinalIgnoreCase);
        var enabledOrder = state.Values.Where(m => m.Enabled).OrderBy(m => m.Priority).ThenBy(m => m.ModId, StringComparer.OrdinalIgnoreCase).ToList();
        foreach (var mod in visible)
        {
            state.TryGetValue(mod.Id, out var profileMod);
            var versions = _database.GetModVersions(mod.Id).Select(v => v.Version).ToList();
            var selectedVersion = profileMod?.Version ?? mod.Version;
            var orderIndex = enabledOrder.FindIndex(item => item.ModId.Equals(mod.Id, StringComparison.OrdinalIgnoreCase));
            var row = new ModRowViewModel
            {
                Id = mod.Id,
                Name = mod.Name,
                Version = $"Version {selectedVersion}" + (string.IsNullOrWhiteSpace(mod.Author) ? "" : $" by {mod.Author}"),
                Author = mod.Author ?? "",
                SourceUrl = _officialCatalogResult?.Value?.Mods.FirstOrDefault(c => c.Id.Equals(mod.Id, StringComparison.OrdinalIgnoreCase))?.SourceUrl ?? "",
                Enabled = profileMod?.Enabled == true,
                Selected = _selectedMods.Contains(mod.Id),
                SelectedVersion = selectedVersion,
                CanMoveUp = profileMod?.Enabled == true && orderIndex > 0,
                CanMoveDown = profileMod?.Enabled == true && orderIndex >= 0 && orderIndex < enabledOrder.Count - 1
            };
            foreach (var version in versions) row.Versions.Add(version);
            row.ToggleEnabled = new RelayCommand(() =>
            {
                var enabled = !row.Enabled;
                _database.SetProfileMod(active.Id, mod.Id, enabled, profileMod?.Priority ?? 0, row.SelectedVersion);
                _changesPending = true;
                SetStatus($"{mod.Name} is now {(enabled ? "enabled" : "disabled")} in {active.Name}.");
                ShowDashboard(2);
            });
            row.ChangeVersion = new RelayCommand(() =>
            {
                if (string.IsNullOrWhiteSpace(row.SelectedVersion)) return;
                _database.SetProfileMod(active.Id, mod.Id, row.Enabled, profileMod?.Priority ?? 0, row.SelectedVersion);
                _changesPending = true;
                SetStatus($"Using {mod.Name} version {row.SelectedVersion} in {active.Name}.");
                ShowDashboard(2);
            });
            row.MoveUp = new RelayCommand(() => ChangePriority(active.Id, mod, profileMod?.Priority ?? 0, -1));
            row.MoveDown = new RelayCommand(() => ChangePriority(active.Id, mod, profileMod?.Priority ?? 0, 1));
            row.Details = new AsyncRelayCommand(() => ShowInstalledModDetails(mod));
            row.OpenSource = new RelayCommand(() => OpenExternalLink(row.SourceUrl));
            row.Remove = new AsyncRelayCommand(() => RemoveMod(mod));
            row.PropertyChanged += (_, e) =>
            {
                if (e.PropertyName == nameof(ModRowViewModel.Selected))
                {
                    if (row.Selected) _selectedMods.Add(row.Id); else _selectedMods.Remove(row.Id);
                }
            };
            model.Items.Add(row);
        }
        var conflicts = planResult?.Success == true && planResult.Value is not null
            ? planResult.Value.Conflicts.Where(c => c.Kind is "missing-dependency" or "disabled-dependency" or "dependency-version" or "profile-version").Select(c => "• " + c.Message).ToList()
            : [];
        model.DependencyNotice = conflicts.Count == 0 ? "" : "Dependencies need attention\n" + string.Join(Environment.NewLine, conflicts);
        model.SearchCommand = new RelayCommand(() => { _modSearch = model.Search.Trim(); ShowDashboard(2); });
        model.ClearSearch = new RelayCommand(() => { _modSearch = ""; ShowDashboard(2); });
        model.AddMod = new AsyncRelayCommand(ImportModPackage);
        model.SelectAll = new RelayCommand(() => { foreach (var row in model.Items) { row.Selected = true; _selectedMods.Add(row.Id); } });
        model.ClearSelection = new RelayCommand(() => { _selectedMods.Clear(); ShowDashboard(2); });
        model.EnableAll = new RelayCommand(() => SetAllVisibleMods(active, mods, true));
        model.DisableAll = new RelayCommand(() => SetAllVisibleMods(active, mods, false));
        model.EnableSelected = new RelayCommand(() => SetSelectedMods(active, true));
        model.DisableSelected = new RelayCommand(() => SetSelectedMods(active, false));
        model.RemoveSelected = new AsyncRelayCommand(RemoveSelectedMods);
        model.RefreshState();
    }

    private void PopulateDiscoverModel(IReadOnlyList<ModRecord> installed)
    {
        var model = _viewModel.Discover;
        model.Search = _librarySearch;
        model.Mods.Clear();
        model.IsLoading = _catalogLoading;
        model.HasCatalog = _officialCatalogResult?.Success == true;
        model.Status = _catalogLoading
            ? "Loading community catalog…"
            : _officialCatalogResult is null
                ? "Community catalog has not been loaded yet."
                : _officialCatalogResult.Success
                    ? $"Community catalog loaded: {CountPhrase(_officialCatalogResult.Value!.Mods.Count, "mod")}. Last checked {_catalogLastChecked ?? "not yet"}."
                    : _officialCatalogResult.Error ?? "Could not load the community catalog.";

        if (_officialCatalogResult?.Value is { } catalog)
        {
            var visible = catalog.Mods.Where(m => string.IsNullOrWhiteSpace(_librarySearch) || m.Name.Contains(_librarySearch, StringComparison.OrdinalIgnoreCase) || m.Id.Contains(_librarySearch, StringComparison.OrdinalIgnoreCase));
            foreach (var catalogMod in SortCatalog(visible))
            {
                var releases = catalogMod.Releases.OrderByDescending(r => r.Version, Comparer<string>.Create(SemanticVersion.Compare)).ToList();
                var latest = releases.FirstOrDefault();
                var installedVersions = _database.GetModVersions(catalogMod.Id);
                var current = installedVersions.FirstOrDefault() ?? installed.FirstOrDefault(m => m.Id.Equals(catalogMod.Id, StringComparison.OrdinalIgnoreCase));
                var row = new DiscoverModViewModel
                {
                    Id = catalogMod.Id,
                    Name = catalogMod.Name,
                    Description = catalogMod.Description ?? "No description provided.",
                    Latest = latest?.Version ?? "—",
                    Installed = installedVersions.Count switch { 0 => "Not installed", 1 => installedVersions[0].Version, _ => $"{installedVersions.Count} versions" },
                    InstalledBrush = current is not null && latest is not null && IsNewerVersion(latest.Version, current.Version) ? "#FFB15C" : "#9AAAC2",
                    Selected = _selectedCatalogMods.Contains(catalogMod.Id),
                    SelectedRelease = latest?.Version ?? ""
                };
                foreach (var release in releases) row.Releases.Add(release.Version);
                row.ToggleSelected = new RelayCommand(() =>
                {
                    if (row.Selected) AddCatalogSelectionWithDependencies(catalogMod); else _selectedCatalogMods.Remove(catalogMod.Id);
                });
                row.Install = new AsyncRelayCommand(async () =>
                {
                    var release = releases.FirstOrDefault(r => r.Version.Equals(row.SelectedRelease, StringComparison.OrdinalIgnoreCase)) ?? latest;
                    if (release is not null) await InstallOfficial(catalogMod, release, null);
                });
                row.Details = new AsyncRelayCommand(() => ShowDetails(catalogMod.Name,
                    $"{catalogMod.Description ?? "No description."}{Environment.NewLine}Author: {catalogMod.Author ?? "Unknown"}{Environment.NewLine}License: {catalogMod.License ?? "Not specified"}{Environment.NewLine}Source: {catalogMod.SourceUrl ?? "Not specified"}{Environment.NewLine}{Environment.NewLine}Dependencies:{Environment.NewLine}{FormatDependencies(catalogMod.Dependencies)}{Environment.NewLine}{Environment.NewLine}Releases: {string.Join(", ", releases.Select(r => r.Version))}{Environment.NewLine}{Environment.NewLine}{latest?.Changelog ?? "No release notes provided."}", catalogMod.SourceUrl));
                row.PropertyChanged += (_, e) =>
                {
                    if (e.PropertyName == nameof(DiscoverModViewModel.Selected))
                    {
                        if (row.Selected) AddCatalogSelectionWithDependencies(catalogMod); else _selectedCatalogMods.Remove(catalogMod.Id);
                    }
                };
                model.Mods.Add(row);
            }
        }
        model.SearchCommand = new RelayCommand(() => { _librarySearch = model.Search.Trim(); ShowDashboard(1); });
        model.ClearSearch = new RelayCommand(() => { _librarySearch = ""; ShowDashboard(1); });
        model.Refresh = new AsyncRelayCommand(RefreshOfficialCatalog);
        model.InstallSelected = new AsyncRelayCommand(InstallSelectedOfficial);
        model.UpdateAll = new AsyncRelayCommand(() => UpdateAllOfficial(installed));
        model.RefreshState();
    }

    private void PopulateProfilesModel(ProfileRecord active)
    {
        var model = _viewModel.Profiles;
        model.Items.Clear();
        foreach (var profile in _database.GetProfiles())
        {
            var enabled = _database.GetProfileMods(profile.Id).Where(m => m.Enabled).ToList();
            var names = enabled.Select(m => _database.GetMod(m.ModId)?.Name ?? m.ModId).Take(5).ToList();
            var row = new ProfileRowViewModel
            {
                Id = profile.Id,
                Name = profile.Name,
                IsActive = profile.Id.Equals(active.Id, StringComparison.OrdinalIgnoreCase),
                Summary = enabled.Count == 0 ? "No mods turned on" : CountPhrase(enabled.Count, "active mod") + (names.Count == 0 ? "" : $"{Environment.NewLine}{string.Join(", ", names)}")
            };
            row.Rename = new RelayCommand(() =>
            {
                try { _database.RenameProfile(profile.Id, row.Name); SetStatus($"Renamed setup to {row.Name.Trim()}."); ShowDashboard(3); }
                catch (InvalidOperationException ex) { SetStatus(ex.Message, error: true); }
            });
            row.Use = new RelayCommand(() => { _database.SetActiveProfile(profile.Id); TryRedeployAfterProfileChange(profile); ShowDashboard(3); });
            row.Duplicate = new RelayCommand(() => CreateProfile(profile.Name + " Copy", profile));
            row.Delete = new AsyncRelayCommand(async () =>
            {
                if (!await Confirm("Delete setup", $"Delete setup ‘{profile.Name}’ and its saved mod selections? This cannot be undone.", "Delete setup")) return;
                try { _database.DeleteProfile(profile.Id); SetStatus($"Deleted setup {profile.Name}."); ShowDashboard(3); }
                catch (InvalidOperationException ex) { SetStatus(ex.Message, error: true); }
            });
            model.Items.Add(row);
        }
        model.Create = new RelayCommand(() => CreateProfile(model.NewName.Trim(), null));
        model.Export = new AsyncRelayCommand(() => ExportCurrentProfile(active));
        model.Import = new AsyncRelayCommand(ImportProfile);
    }

    private void PopulateSettingsModel(GameRecord? game, Core.Common.Result<DeploymentPlan>? planResult)
    {
        var model = _viewModel.Settings;
        model.Intro = game is null ? "Choose your game folder, apply your mod setup, and add support only when a mod needs it." : "Your game is connected. Apply your mod setup, and add support only when a mod needs it.";
        model.GamePath = game?.InstallPath ?? "";
        model.HasGame = game is not null;
        model.GameStatus = game is null ? "Choose your Ragnarock folder to get started." : _detector.Validate(game.InstallPath).IsValid ? "Ragnarock is ready." : "This folder needs attention. Choose the correct Ragnarock folder.";
        model.ApplyStatus = game is null ? "Choose your Ragnarock folder above first." : planResult is { Success: true, Value: not null } plan
            ? (!_changesPending && IsDeploymentSynchronized(plan.Value) ? "Your current mod setup is active in Ragnarock." : "Your current mod setup has changes waiting to be applied.")
            : "We can’t check your mod setup yet. Resolve the issue shown here first.";
        model.CanCleanUp = game is not null && CreateDeploymentService().GetUnmanagedFiles(game.InstallPath).Count > 0;
        var scriptMods = planResult?.Success == true && planResult.Value is not null
            ? planResult.Value.Items.Where(i => i.FileType.Equals("ue4ss-lua", StringComparison.OrdinalIgnoreCase) || i.FileType.Equals("ue4ss-dll", StringComparison.OrdinalIgnoreCase)).Select(i => i.ModId).Distinct(StringComparer.OrdinalIgnoreCase).ToList()
            : [];
        model.SupportStatus = game is null ? "Choose your game folder first." : _ue4ss.Detect(game.InstallPath).Installed ? "Script support is installed." : "Script support is not installed yet.";
        var scriptNames = scriptMods.Select(id => _database.GetMod(id)?.Name ?? id).ToList();
        model.SupportNote = scriptNames.Count == 0 ? "Your current mods don’t need anything extra." : $"{CountPhrase(scriptNames.Count, "active mod")} {(scriptNames.Count == 1 ? "needs" : "need")} script support: {string.Join(", ", scriptNames)}.";
        model.RecoverySummary = planResult is { Success: true, Value: not null } p ? $"This session has {CountPhrase(p.Value.Conflicts.Count(c => c.BlocksDeployment), "issue")} that stop changes from being applied." : "Choose your Ragnarock folder before using recovery tools.";
        model.LaunchArguments = _launchArguments;
        model.CachedSupportVersions.Clear();
        foreach (var release in _ue4ssReleases.GetCachedReleases()) model.CachedSupportVersions.Add($"{release.Version} ({release.AssetName})");
        model.HasCachedSupport = model.CachedSupportVersions.Count > 0;
        model.DetectGame = new RelayCommand(() => { var install = _detector.DetectFirstValid(); if (install is null) SetStatus("No Ragnarock Steam install was found. Choose the folder manually.", error: true); else { SaveGame(install); SetStatus(install.IsValid ? "Ragnarock folder saved." : "Folder saved, but it may need attention.", !install.IsValid); ShowDashboard(); } });
        model.BrowseGame = new AsyncRelayCommand(async () =>
        {
            var folders = await StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions { Title = "Choose Ragnarock folder", AllowMultiple = false });
            if (folders.Count > 0) { model.GamePath = folders[0].Path.LocalPath; }
        });
        model.SaveGame = new RelayCommand(() => { if (string.IsNullOrWhiteSpace(model.GamePath)) { SetStatus("Choose your Ragnarock folder first.", error: true); return; } var install = _detector.Validate(model.GamePath); SaveGame(install); SetStatus(install.IsValid ? "Ragnarock folder saved." : "Folder saved, but it may need attention.", !install.IsValid); ShowDashboard(); });
        model.ApplySetup = new RelayCommand(DeployActiveProfile);
        model.CleanUp = new AsyncRelayCommand(async () =>
        {
            if (game is null) return;
            var unmanaged = CreateDeploymentService().GetUnmanagedFiles(game.InstallPath);
            if (unmanaged.Count == 0 || !await Confirm("Clean up extra mod files", $"Remove {CountPhrase(unmanaged.Count, "file")} that are outside this app’s setup? This cannot be undone.", "Clean up files")) return;
            var result = CreateDeploymentService().RemoveUnmanagedFiles(game.InstallPath); SetStatus(result.Success ? $"Removed {CountPhrase(result.Value, "extra mod file")}." : result.Error ?? "Could not remove extra mod files.", !result.Success); ShowDashboard(4);
        });
        model.CheckSupport = new AsyncRelayCommand(CheckUe4ssUpdates);
        model.InstallSupport = new AsyncRelayCommand(InstallUe4ssSupport);
        model.UseCachedSupport = new RelayCommand(() =>
        {
            var current = _database.GetGame(); var cached = _ue4ssReleases.GetCachedReleases();
            var index = model.CachedSupportVersions.IndexOf(model.SelectedCachedSupport);
            if (current is null || index < 0 || index >= cached.Count) return;
            var result = _ue4ssReleases.InstallCachedRelease(current.InstallPath, cached[index]); SetStatus(result.Success ? $"Installed saved script support {cached[index].Version}." : result.Error ?? "Script support installation failed.", !result.Success); ShowDashboard(4);
        });
        model.Rollback = new RelayCommand(() => { var result = CreateDeploymentService().RollbackLatest(); SetStatus(result.Success ? "The last change was undone." : result.Error ?? "Could not undo the last change.", !result.Success); ShowDashboard(4); });
        model.ResetDeployment = new AsyncRelayCommand(async () => { if (!await Confirm("Remove applied setup", "Remove the setup currently applied by this app from the game folder? Backups are retained when possible.", "Remove setup")) return; var result = CreateDeploymentService().ResetDeployment(); SetStatus(result.Success ? "The applied setup was removed." : result.Error ?? "Could not remove the applied setup.", !result.Success); ShowDashboard(4); });
        model.SaveLaunchOptions = new RelayCommand(() => { _launchArguments = model.LaunchArguments ?? ""; File.WriteAllText(_paths.LaunchArgumentsPath, _launchArguments); SetStatus(string.IsNullOrWhiteSpace(_launchArguments) ? "Launch arguments cleared." : "Launch arguments saved."); });
        model.OpenLogs = new RelayCommand(() => OpenFolder(_paths.Logs));
        model.OpenIssues = new RelayCommand(() => OpenExternalLink("https://github.com/Brollyy/RagnaModManager/issues"));
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

    private IEnumerable<CatalogMod> SortCatalog(IEnumerable<CatalogMod> mods)
    {
        return mods.OrderBy(m => m.Name, StringComparer.OrdinalIgnoreCase);
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

    private static string FormatDependencies(IReadOnlyDictionary<string, string>? dependencies) =>
        dependencies is null or { Count: 0 } ? "None" : string.Join(Environment.NewLine, dependencies.Select(d => $"- {d.Key} {d.Value}"));

    private async Task InstallOfficial(CatalogMod catalogMod, CatalogRelease release, Button? install)
    {
        if (install is not null) install.IsEnabled = false;
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
            if (install is not null) install.IsEnabled = true;
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
    }

    private void UpdatePendingChangesBar()
    {
        _viewModel.HasPendingChanges = _changesPending;
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
        var validSource = Uri.TryCreate(sourceUrl, UriKind.Absolute, out var sourceUri) &&
                          (sourceUri.Scheme == Uri.UriSchemeHttp || sourceUri.Scheme == Uri.UriSchemeHttps);
        var model = new DetailsDialogViewModel { Title = title, Message = message, HasSource = validSource };
        model.OpenSource = new RelayCommand(() => OpenExternalLink(sourceUri!.ToString()));
        model.Close = new RelayCommand(dialog.Close);
        dialog.Content = new DetailsDialog { DataContext = model };
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

        var model = new ConfirmDialogViewModel { Title = title, Message = message, ConfirmText = confirmText };
        model.Confirm = new RelayCommand(() => dialog.Close(true));
        model.Cancel = new RelayCommand(() => dialog.Close(false));
        dialog.Content = new ConfirmDialog { DataContext = model };

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

    private static string CountPhrase(int count, string singular) =>
        $"{count} {singular}{(count == 1 ? "" : "s")}";

}
