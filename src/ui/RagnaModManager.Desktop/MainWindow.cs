using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Markup.Xaml;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using System.Diagnostics;
using System.Text.Json;
using RagnaModManager.Core.Checksums;
using RagnaModManager.Core.Compatibility;
using RagnaModManager.Core.Database;
using RagnaModManager.Core.Deployment;
using RagnaModManager.Core.Hammers;
using RagnaModManager.Core.Logging;
using RagnaModManager.Core.Manifests;
using RagnaModManager.Core.Packages;
using RagnaModManager.Core.Platform;
using RagnaModManager.Platform.Folders;
using RagnaModManager.Platform.Proton;
using RagnaModManager.Platform.Steam;
using RagnaModManager.Ragnarock.Compatibility;
using RagnaModManager.Ragnarock.DeploymentRules;
using RagnaModManager.Ragnarock.Detection;
using RagnaModManager.Ragnarock.Launch;
using RagnaModManager.Ragnarock.Ue4ss;

namespace RagnaModManager.Desktop;

public partial class MainWindow : Window
{
    private const string Ue4ssZipHelp = "If a mod requires UE4SS, choose its UE4SS ZIP here and the manager will install it for you.";

    private readonly AppPaths _paths;
    private readonly ManagerDatabase _database;
    private readonly AppLogger _logger;
    private readonly RagnarockDetector _detector = new();
    private readonly RagnarockDeploymentRules _rules = new();
    private readonly Ue4ssService _ue4ss = new();
    private readonly Ue4ssReleaseService _ue4ssReleases;
    private readonly OfficialCatalogService _officialCatalog;
    private HammerLibraryService _hammerLibrary = null!;
    private HammerTableMetadataRegistry _hammerMetadataRegistry = null!;
    private readonly FolderOpener _folderOpener = new();
    private readonly SteamLaunchOptionsService _steamLaunchOptions = new();

    private TabControl _tabs = null!;
    private TextBlock _status = null!;
    private Button? _launchButton;
    private int _selectedTab;
    private bool _rebuildingTabs;
    private bool? _compactNavigation;
    private bool _changesPending;
    private string _modSearch = "";
    private string? _catalogLastChecked;
    private string _librarySearch = "";
    private readonly HashSet<string> _selectedCatalogMods = new(StringComparer.OrdinalIgnoreCase);
    private string _catalogSortColumn = "Name";
    private bool _catalogSortDescending;
    private string _launchArguments = "";
    private string _launchMode = "Default";
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
        _hammerLibrary = new HammerLibraryService(_paths, _logger);
        _hammerMetadataRegistry = new HammerTableMetadataRegistry(_paths, _logger);
        _ue4ssReleases = new Ue4ssReleaseService(_paths, ue4ss: _ue4ss);
        _officialCatalog = new OfficialCatalogService(_paths, _database, _logger);

        try
        {
            _database.Initialize();
            if (File.Exists(_paths.LaunchArgumentsPath)) _launchArguments = File.ReadAllText(_paths.LaunchArgumentsPath);
            if (File.Exists(_paths.LaunchModePath))
            {
                var savedLaunchMode = File.ReadAllText(_paths.LaunchModePath).Trim();
                if (savedLaunchMode is "Default" or "Flat" or "VR") _launchMode = savedLaunchMode;
            }
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
        var launchModeSelector = this.FindControl<ComboBox>("LaunchModeSelector") ?? throw new InvalidOperationException("Launch mode selector was not loaded.");
        launchModeSelector.ItemsSource = new[] { "Default", "Flat", "VR" };
        launchModeSelector.SelectedItem = _launchMode;
        launchModeSelector.SelectionChanged += (_, _) =>
        {
            if (launchModeSelector.SelectedItem is not string selectedMode) return;
            _launchMode = selectedMode;
            File.WriteAllText(_paths.LaunchModePath, _launchMode);
            ShowDashboard();
        };
        _viewModel.ApplyChanges = new RelayCommand(DeployActiveProfile);
        _viewModel.RevertChanges = new RelayCommand(RevertPendingChanges);
        SizeChanged += (_, _) => UpdateNavigationLayout();
        UpdateNavigationLayout();

        _tabs.SelectionChanged += (_, _) =>
        {
            if (!_rebuildingTabs)
            {
                _selectedTab = Math.Max(0, _tabs.SelectedIndex);
                Dispatcher.UIThread.Post(ApplyTabVisualState, DispatcherPriority.Render);
                _status.Text = "";
            }
        };

    }

    private void OnNavigationSizeChanged(object? sender, SizeChangedEventArgs e) => UpdateNavigationLayout();

    private void UpdateNavigationLayout()
    {
        if (_tabs is null) return;

        // The left rail is comfortable on a desktop canvas, but it steals the
        // space needed by names and descriptions on compact windows.
        var compact = Bounds.Width < 1080;
        if (_compactNavigation == compact) return;

        _compactNavigation = compact;
        var selectedIndex = _tabs.SelectedIndex >= 0 ? _tabs.SelectedIndex : _selectedTab;
        _rebuildingTabs = true;
        try
        {
            _tabs.TabStripPlacement = compact ? Dock.Top : Dock.Left;
            foreach (var item in _tabs.Items.OfType<TabItem>())
            {
                item.Margin = compact ? new Thickness(0, 0, 8, 6) : new Thickness(0, 2, 10, 2);
                item.Padding = compact ? new Thickness(12, 9) : new Thickness(16, 12);
            }
            _tabs.SelectedIndex = Math.Clamp(selectedIndex, 0, _tabs.Items.Count - 1);
        }
        finally
        {
            _rebuildingTabs = false;
        }
        ApplyTabVisualState();
    }

    private void ApplyTabVisualState()
    {
        var selectedIndex = _tabs.SelectedIndex;
        for (var index = 0; index < _tabs.Items.Count; index++)
        {
            if (_tabs.Items[index] is not TabItem tab) continue;
            tab.Classes.Remove("active-tab");
            tab.Classes.Remove("discover-tab");
            if (index == selectedIndex)
            {
                tab.Classes.Add("active-tab");
            }
        }
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
        // Profile edits can exist before a game folder is configured, but they
        // cannot be applied yet. Keep the footer focused on the next actionable
        // step instead of showing an Apply button that cannot work.
        _viewModel.HasPendingChanges = game is not null && _changesPending;
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
            ApplyTabVisualState();
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
        PopulateHammerLibraryModel(game);

        _viewModel.RefreshPages();
    }

    private void PopulateHammerLibraryModel(GameRecord? game)
    {
        var model = _viewModel.Hammers;
        var status = _hammerLibrary.GetStatus(game?.InstallPath);
        model.GameBuild = status.GameBuild;
        model.BuildStatus = status.BuildStatus;
        var hasEnabledHammers = status.Hammers.Any(entry => entry.Enabled);
        var generatedProfileMod = _database.GetProfileMods(_database.GetActiveProfile().Id)
            .FirstOrDefault(mod => mod.ModId.Equals("rmm-custom-hammers", StringComparison.OrdinalIgnoreCase));
        var canRemoveGeneratedPak = generatedProfileMod?.Enabled == true;
        model.BuildLabel = hasEnabledHammers ? "Build and add to Ragnarock" : "Remove from Ragnarock";
        model.CanBuild = hasEnabledHammers
            ? game is not null && status.GamePakPath is not null && File.Exists(status.GamePakPath)
            : canRemoveGeneratedPak;
        model.CountLabel = status.Hammers.Count == 1 ? "1 hammer in library" : $"{status.Hammers.Count} hammers in library";
        model.Items.Clear();
        foreach (var entry in status.Hammers)
        {
            var row = new HammerLibraryRowViewModel
            {
                Id = entry.Manifest.Id,
                Name = entry.Manifest.Name,
                Author = entry.Manifest.Author,
                Version = entry.Manifest.Version,
                Description = entry.Manifest.Description,
                RowName = entry.Manifest.RowName,
                MeshAssetPath = entry.Manifest.MeshAssetPath,
                Thumbnail = LoadHammerThumbnail(entry),
                Enabled = entry.Enabled
            };
            row.ToggleEnabled = new RelayCommand(() =>
            {
                var result = _hammerLibrary.SetEnabled(row.Id, row.Enabled);
                SetStatus(result.Success ? $"{row.Name} { (row.Enabled ? "enabled" : "disabled") } in the hammer library." : result.Error ?? "Could not update hammer.", !result.Success);
                ShowDashboard(5);
            });
            row.Remove = new AsyncRelayCommand(async () =>
            {
                if (!await Confirm("Remove custom hammer", $"Remove {row.Name} from the hammer library?", "Remove hammer", destructive: true)) return;
                var result = _hammerLibrary.Remove(row.Id);
                SetStatus(result.Success ? $"Removed {row.Name} from the hammer library." : result.Error ?? "Could not remove hammer.", !result.Success);
                ShowDashboard(5);
            });
            model.Items.Add(row);
        }
        model.Import = new AsyncRelayCommand(ImportHammerPackage);
        model.OpenFolder = new RelayCommand(() => OpenFolder(_paths.HammerLibrary));
        model.Build = new AsyncRelayCommand(BuildAndDeployHammers, () => model.CanBuild);
        model.RefreshState();
    }

    private Avalonia.Media.Imaging.Bitmap? LoadHammerThumbnail(HammerLibraryEntry entry)
    {
        if (string.IsNullOrWhiteSpace(entry.Manifest.Thumbnail)) return null;
        var path = Path.GetFullPath(Path.Combine(entry.InstalledPath, entry.Manifest.Thumbnail.Replace('/', Path.DirectorySeparatorChar)));
        var root = Path.GetFullPath(entry.InstalledPath) + Path.DirectorySeparatorChar;
        if (!path.StartsWith(root, StringComparison.Ordinal) || !File.Exists(path)) return null;
        try
        {
            using var stream = File.OpenRead(path);
            return new Avalonia.Media.Imaging.Bitmap(stream);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
        {
            _logger.Error($"Could not load preview image for {entry.Manifest.Name}: {ex}");
            return null;
        }
    }

    private async Task BuildAndDeployHammers()
    {
        var enabled = _hammerLibrary.GetEntries().Where(entry => entry.Enabled).ToList();
        if (enabled.Count == 0)
        {
            var removeGame = _database.GetGame();
            if (removeGame is null) { SetStatus("Choose your Ragnarock folder first.", error: true); return; }
            var removeProfile = _database.GetActiveProfile();
            var installedHammerMod = _database.GetProfileMods(removeProfile.Id).FirstOrDefault(mod => mod.ModId.Equals("rmm-custom-hammers", StringComparison.OrdinalIgnoreCase));
            if (installedHammerMod?.Enabled != true) { SetStatus("There are no enabled custom hammers or deployed hammer PAK to remove."); return; }
            _database.SetProfileMod(removeProfile.Id, installedHammerMod.ModId, false, installedHammerMod.Priority, installedHammerMod.Version);
            _changesPending = true;
            SetStatus("Removing the generated hammer PAK and restoring any files it replaced.");
            DeployActiveProfile();
            return;
        }
        var game = _database.GetGame();
        if (game is null) { SetStatus("Choose your Ragnarock folder first.", error: true); return; }
        var outputDirectory = Path.Combine(_paths.ManagedMods, "rmm-custom-hammers");
        Directory.CreateDirectory(outputDirectory);
        var pakPath = Path.Combine(outputDirectory, "RMM_CustomHammers_P.pak");
        var gamePakPath = Path.Combine(game.InstallPath, "Ragnarock", "Content", "Paks", "Ragnarock-WindowsNoEditor.pak");
        SetStatus("Checking compatibility with this Ragnarock update...");
        var metadata = await _hammerMetadataRegistry.EnsureAvailableAsync(gamePakPath);
        if (!metadata.Success || metadata.Value is null)
        {
            SetStatus(metadata.Error ?? "RMM could not check this game update.", error: true);
            return;
        }
        SetStatus("Preparing your enabled hammers...");
        var build = await Task.Run(() => new HammerPakBuildService(_logger).Build(game.InstallPath, enabled, metadata.Value, pakPath));
        if (!build.Success || build.Value is null)
        {
            SetStatus(build.Error ?? "Custom hammer PAK build failed.", error: true);
            return;
        }

        var packageVersion = "1.0.0-" + build.Value.Sha256[..12].ToLowerInvariant();
        var packageId = "rmm-custom-hammers";
        var packageDirectory = Path.Combine(outputDirectory, packageVersion);
        Directory.CreateDirectory(packageDirectory);
        var packagePath = Path.Combine(packageDirectory, packageId + "-" + packageVersion + ".rmod");
        ModManifest packageManifest;
        try
        {
            var templatePath = Path.Combine(AppContext.BaseDirectory, "ManagedMods", packageId, "manifest.json");
            packageManifest = JsonSerializer.Deserialize<ModManifest>(
                await File.ReadAllTextAsync(templatePath),
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true })
                ?? throw new InvalidDataException("Managed hammer package manifest is empty.");
            packageManifest.Version = packageVersion;
            packageManifest.Description = $"Contains {build.Value.AddedRowCount} custom hammers for game build {build.Value.GamePakSha256[..12]}.";
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException or InvalidDataException)
        {
            _logger.Error($"Could not load the custom hammer package manifest from ManagedMods: {ex}");
            SetStatus("RMM couldn't prepare the hammer package. Check the log for details.", error: true);
            return;
        }
        await using (var packageStream = File.Create(packagePath))
        using (var archive = new System.IO.Compression.ZipArchive(packageStream, System.IO.Compression.ZipArchiveMode.Create))
        {
            var manifestEntry = archive.CreateEntry("manifest.json");
            await using (var output = manifestEntry.Open())
                await System.Text.Json.JsonSerializer.SerializeAsync(output, packageManifest, new System.Text.Json.JsonSerializerOptions { WriteIndented = true });
            var pakEntry = archive.CreateEntry("Files/RMM_CustomHammers_P.pak", System.IO.Compression.CompressionLevel.NoCompression);
            await using var pakOutput = pakEntry.Open();
            await using var pakInput = File.OpenRead(pakPath);
            await pakInput.CopyToAsync(pakOutput);
        }

        var imported = new PackageImporter(_paths, _database, _logger).Import(packagePath);
        if (!imported.Success || imported.Value is null)
        {
            SetStatus(imported.Error ?? "Could not register the generated hammer PAK.", error: true);
            return;
        }
        var active = _database.GetActiveProfile();
        var existing = _database.GetProfileMods(active.Id).FirstOrDefault(mod => mod.ModId.Equals(packageId, StringComparison.OrdinalIgnoreCase));
        _database.SetProfileMod(active.Id, packageId, true, existing?.Priority ?? 500, packageVersion);
        _changesPending = true;
        ShowDashboard(5);
        SetStatus($"Built and registered {build.Value.AddedRowCount} custom hammers. Applying the profile to Ragnarock now.");
        DeployActiveProfile();
    }

    private async Task ImportHammerPackage()
    {
        var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Import custom hammer package",
            AllowMultiple = false,
            FileTypeFilter = [new FilePickerFileType("RMM hammer packages") { Patterns = ["*.rhammer"] }]
        });
        if (files.Count == 0) return;
        var result = _hammerLibrary.Import(files[0].Path.LocalPath);
        SetStatus(result.Success ? $"Added {result.Value!.Manifest.Name} to the hammer library." : result.Error ?? "Hammer import failed.", !result.Success);
        ShowDashboard(5);
    }

    private void PopulateDashboardModel(GameRecord? game, ProfileRecord active, IReadOnlyList<ModRecord> mods, Core.Common.Result<DeploymentPlan>? planResult)
    {
        var enabled = GetEnabledMods().Count;
        var ready = game is not null && new RagnarockCompatibilityChecker().Check(game.InstallPath).CanManage;
        var synchronized = game is not null && !_changesPending && planResult is { Success: true, Value: not null } && IsDeploymentSynchronized(planResult.Value);
        var dashboard = _viewModel.Dashboard;
        dashboard.SetupName = active.Name;
        dashboard.ActiveMods = $"{enabled} active";
        dashboard.InstalledMods = $"{mods.Count} installed";
        dashboard.PlayStatus = game is null ? "Set up first" : !ready ? "Needs setup" : synchronized ? "Ready to play" : "Apply changes";
        dashboard.PlayDetail = game is null ? "Choose your game folder" : "Ragnarock";
        dashboard.ShowSetupAction = game is null;
        dashboard.QuickActionsTitle = game is null ? "Get started" : "Your mods";
        dashboard.QuickActionsDescription = game is null
            ? "Connect the game, then pick the mods you want to use."
            : "Browse, install, and switch mods on or off.";
        dashboard.CanOpenGame = game is not null;
        dashboard.Headline = game is null
            ? "Let’s get some mods running."
            : ready
                ? synchronized
                    ? enabled == 0 ? "Ragnarock is ready. Find a mod or play vanilla." : "Your mods are ready to go."
                    : "Your mod changes are waiting to be applied."
                : "The game folder still needs a quick check.";
        dashboard.HeadlineBrush = game is null || !ready ? "#FFB15C" : "#4DE1C1";
        dashboard.NextSteps = game is null
            ? "Set up your game folder, then choose your first mod. We’ll keep the rest of the setup out of your way."
            : ready
                ? synchronized ? "Find something new, or launch Ragnarock with this setup." : "Apply your changes before launching Ragnarock."
                : "Choose the Ragnarock game folder to continue.";
        dashboard.ShowDeploymentNotice = false;
        dashboard.DeploymentDetails = "";
        if (game is not null && planResult is { Success: true, Value: not null } plan)
        {
            var conflicts = plan.Value.Conflicts.Where(c => c.BlocksDeployment).Select(FriendlyDeploymentConflict).ToList();
            var warnings = plan.Value.Warnings.Where(w => !w.StartsWith("Found ", StringComparison.Ordinal)).ToList();
            var unmanagedWarning = plan.Value.Warnings.FirstOrDefault(w => w.StartsWith("Found ", StringComparison.Ordinal));
            dashboard.ShowDeploymentNotice = !synchronized || conflicts.Count > 0 || warnings.Count > 0 || unmanagedWarning is not null;
            dashboard.DeploymentMessage = synchronized ? "Your setup is ready." : "Your setup still needs attention before you play.";
            IEnumerable<string> unmanagedDetail = unmanagedWarning is null
                ? []
                : new[] { "Ragnarock has other files in its mods folder. They will stay untouched unless one of your selected mods needs the same space." };
            dashboard.DeploymentDetails = string.Join(Environment.NewLine, conflicts.Concat(unmanagedDetail).Concat(warnings.Select(w => "Notice: " + w)));
        }

        dashboard.RefreshState();
        dashboard.SetupAutomatically = new AsyncRelayCommand(SetupAutomatically);
        dashboard.DiscoverMods = new RelayCommand(() => ShowDashboard(1));
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
        model.SetupName = active.Name;
        model.Search = _modSearch;
        model.Items.Clear();
        model.DependencyIssues.Clear();
        var visible = mods.Where(m => string.IsNullOrWhiteSpace(_modSearch) || m.Name.Contains(_modSearch, StringComparison.OrdinalIgnoreCase) || m.Id.Contains(_modSearch, StringComparison.OrdinalIgnoreCase)).ToList();
        var state = _database.GetProfileMods(active.Id).ToDictionary(m => m.ModId, StringComparer.OrdinalIgnoreCase);
        var enabledOrder = state.Values.Where(m => m.Enabled).OrderBy(m => m.Priority).ThenBy(m => m.ModId, StringComparer.OrdinalIgnoreCase).ToList();
        foreach (var missing in state.Values.Where(item => _database.GetMod(item.ModId) is null))
        {
            var missingRow = new ModRowViewModel
            {
                Id = missing.ModId,
                Name = missing.ModId,
                Version = "",
                IsMissing = true,
                SelectedVersion = missing.Version ?? ""
            };
            missingRow.RemoveFromSetup = new RelayCommand(() =>
            {
                _database.RemoveProfileMod(active.Id, missing.ModId);
                _changesPending = true;
                SetStatus($"Removed missing mod {missing.ModId} from {active.Name}.", error: true);
                ShowDashboard(2);
            });
            model.Items.Add(missingRow);
        }
        foreach (var mod in visible)
        {
            state.TryGetValue(mod.Id, out var profileMod);
            var versions = _database.GetModVersions(mod.Id).Select(v => v.Version).ToList();
            var selectedVersion = profileMod?.Version ?? mod.Version;
            var orderIndex = enabledOrder.FindIndex(item => item.ModId.Equals(mod.Id, StringComparison.OrdinalIgnoreCase));
            var manifest = ManifestValidator.LoadAndValidate(mod.ManifestPath).Value;
            var catalogMod = _officialCatalogResult?.Value?.Mods.FirstOrDefault(c => c.Id.Equals(mod.Id, StringComparison.OrdinalIgnoreCase));
            var row = new ModRowViewModel
            {
                Id = mod.Id,
                Name = mod.Name,
                Version = $"Version {selectedVersion}" + (string.IsNullOrWhiteSpace(mod.Author) ? "" : $" by {mod.Author}"),
                Author = manifest?.Author ?? mod.Author ?? catalogMod?.Author ?? "Unknown",
                Description = manifest?.Description ?? catalogMod?.Description ?? "No description provided.",
                DependenciesText = manifest is null ? "None" : FormatDependencies(manifest.Dependencies),
                SourceUrl = catalogMod?.SourceUrl ?? "",
                Enabled = profileMod?.Enabled == true,
                Selected = _selectedMods.Contains(mod.Id),
                SelectedVersion = selectedVersion,
                IsUsingOlderVersion = profileMod is not null && SemanticVersion.IsNewer(mod.Version, selectedVersion),
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
            row.MoveUp = new RelayCommand(() => ChangePriority(active.Id, mod, profileMod?.Priority ?? 0, -1));
            row.MoveDown = new RelayCommand(() => ChangePriority(active.Id, mod, profileMod?.Priority ?? 0, 1));
            row.ToggleDetails = new RelayCommand(() => row.DetailsExpanded = !row.DetailsExpanded);
            row.OpenSource = new RelayCommand(() => OpenExternalLink(row.SourceUrl));
            row.Remove = new AsyncRelayCommand(() => RemoveMod(mod));
            row.PropertyChanged += (_, e) =>
            {
                if (e.PropertyName == nameof(ModRowViewModel.Selected))
                {
                    if (row.Selected) _selectedMods.Add(row.Id); else _selectedMods.Remove(row.Id);
                    model.RefreshState();
                }
                else if (e.PropertyName == nameof(ModRowViewModel.SelectedVersion) && !string.Equals(row.SelectedVersion, selectedVersion, StringComparison.OrdinalIgnoreCase))
                {
                    _database.SetProfileMod(active.Id, mod.Id, row.Enabled, profileMod?.Priority ?? 0, row.SelectedVersion);
                    _changesPending = true;
                    SetStatus($"Using {mod.Name} version {row.SelectedVersion} in {active.Name}.");
                    ShowDashboard(2);
                }
            };
            model.Items.Add(row);
        }
        if (planResult?.Success == true && planResult.Value is not null)
        {
            foreach (var conflict in planResult.Value.Conflicts.Where(c => c.Kind is "missing-dependency" or "disabled-dependency" or "dependency-version" or "profile-version"))
                model.DependencyIssues.Add(CreateDependencyIssue(conflict));
        }
        model.DependencyNotice = model.DependencyIssues.Count == 0 ? "" : "Dependencies need attention";
        model.SearchCommand = new RelayCommand(() => { _modSearch = model.Search.Trim(); ShowDashboard(2); });
        model.SearchAction = model.SearchCommand;
        model.ClearSearch = new RelayCommand(() => { _modSearch = ""; ShowDashboard(2); });
        model.AddMod = new AsyncRelayCommand(ImportModPackage);
        model.ImportDropped = path => ImportModPackage(path);
        model.SelectAll = new RelayCommand(() => { foreach (var row in model.Items.Where(item => item.IsInstalled)) { row.Selected = true; _selectedMods.Add(row.Id); } model.RefreshState(); });
        model.ClearSelection = new RelayCommand(() => { _selectedMods.Clear(); ShowDashboard(2); });
        model.EnableAll = new RelayCommand(() => SetAllVisibleMods(active, mods, true));
        model.DisableAll = new RelayCommand(() => SetAllVisibleMods(active, mods, false));
        model.EnableSelected = new RelayCommand(() => SetSelectedMods(active, true));
        model.DisableSelected = new RelayCommand(() => SetSelectedMods(active, false));
        model.RemoveSelected = new AsyncRelayCommand(RemoveSelectedMods);
        model.ToggleBulkActions = new RelayCommand(() => model.BulkActionsExpanded = !model.BulkActionsExpanded);
        model.RefreshState();
    }

    private DependencyIssueViewModel CreateDependencyIssue(DeploymentConflict conflict)
    {
        var relatedId = conflict.RelatedModId;
        if (relatedId is null)
            return new DependencyIssueViewModel { Message = FriendlyDeploymentConflict(conflict) };

        var dependency = _database.GetMod(relatedId);
        if (dependency is not null && conflict.Kind == "disabled-dependency")
        {
            return new DependencyIssueViewModel
            {
                Message = FriendlyDeploymentConflict(conflict),
                ActionLabel = $"Enable {dependency.Name}",
                Action = new RelayCommand(() =>
                {
                    var profile = _database.GetActiveProfile();
                    var existing = _database.GetProfileMods(profile.Id).FirstOrDefault(mod => mod.ModId.Equals(dependency.Id, StringComparison.OrdinalIgnoreCase));
                    _database.SetProfileMod(profile.Id, dependency.Id, true, existing?.Priority ?? 0, existing?.Version ?? dependency.Version);
                    _changesPending = true;
                    SetStatus($"Enabled dependency {dependency.Name}.");
                    ShowDashboard(2);
                })
            };
        }

        var catalogMod = _officialCatalogResult?.Value?.Mods.FirstOrDefault(mod => mod.Id.Equals(relatedId, StringComparison.OrdinalIgnoreCase));
        if (catalogMod?.Latest is not null)
        {
            return new DependencyIssueViewModel
            {
                Message = FriendlyDeploymentConflict(conflict),
                ActionLabel = dependency is null ? $"Install {catalogMod.Name}" : $"Update {dependency.Name}",
                Action = new AsyncRelayCommand(() => InstallOfficial(catalogMod, catalogMod.Latest!, null))
            };
        }

        return new DependencyIssueViewModel
        {
            Message = FriendlyDeploymentConflict(conflict),
            ActionLabel = "Open Discover",
            Action = new RelayCommand(() => _tabs.SelectedIndex = 1)
        };
    }

    private void PopulateDiscoverModel(IReadOnlyList<ModRecord> installed)
    {
        var model = _viewModel.Discover;
        model.Search = _librarySearch;
        model.Mods.Clear();
        model.IsLoading = _catalogLoading;
        model.HasCatalog = _officialCatalogResult?.Success == true;
        model.Status = string.IsNullOrWhiteSpace(_librarySearch)
            ? _catalogLoading
                ? "Loading community catalog…"
                : _officialCatalogResult is null
                    ? "Community catalog has not been loaded yet."
                    : _officialCatalogResult.Success
                        ? $"Community catalog loaded: {CountPhrase(_officialCatalogResult.Value!.Mods.Count, "mod")}. Last checked {_catalogLastChecked ?? "not yet"}."
                        : _officialCatalogResult.Error ?? "Could not load the community catalog."
            : "";

        if (_officialCatalogResult?.Value is { } catalog)
        {
            var visible = catalog.Mods.Where(m => string.IsNullOrWhiteSpace(_librarySearch) || m.Name.Contains(_librarySearch, StringComparison.OrdinalIgnoreCase) || m.Id.Contains(_librarySearch, StringComparison.OrdinalIgnoreCase));
            foreach (var catalogMod in SortCatalog(visible))
            {
                var releases = catalogMod.Releases.OrderByDescending(r => r.Version, Comparer<string>.Create(SemanticVersion.Compare)).ToList();
                var latest = releases.FirstOrDefault();
                var installedVersions = _database.GetModVersions(catalogMod.Id);
                var current = installedVersions.FirstOrDefault() ?? installed.FirstOrDefault(m => m.Id.Equals(catalogMod.Id, StringComparison.OrdinalIgnoreCase));
                var latestInstalled = installedVersions.OrderByDescending(v => v.Version, Comparer<string>.Create(SemanticVersion.Compare)).FirstOrDefault();
                var row = new DiscoverModViewModel
                {
                    Id = catalogMod.Id,
                    Name = catalogMod.Name,
                    Description = catalogMod.Description ?? "No description provided.",
                    Author = catalogMod.Author ?? "Unknown",
                    License = catalogMod.License ?? "Not specified",
                    Source = catalogMod.SourceUrl ?? "Not specified",
                    DependenciesText = FormatDependencies(catalogMod.Dependencies),
                    ReleaseNotes = releases.ToDictionary(r => r.Version, r => r.Changelog ?? "No release notes provided.", StringComparer.OrdinalIgnoreCase),
                    InstallLabel = current is null ? "Install" : latest is not null && IsNewerVersion(latest.Version, current.Version) ? "Update" : "Installed",
                    CanInstall = current is null || latest is not null && IsNewerVersion(latest.Version, current.Version),
                    Latest = latest?.Version ?? "—",
                    Installed = latestInstalled?.Version ?? current?.Version ?? "Not installed",
                    InstalledVersion = current?.Version ?? "",
                    InstalledBrush = current is not null && latest is not null && IsNewerVersion(latest.Version, current.Version) ? "#B8860B" : "#696969",
                    Selected = _selectedCatalogMods.Contains(catalogMod.Id),
                    SelectedRelease = latest?.Version ?? ""
                };
                foreach (var release in releases) row.Releases.Add(release.Version);
                row.ToggleSelected = new RelayCommand(() =>
                {
                    if (row.Selected) AddCatalogSelectionWithDependencies(catalogMod); else _selectedCatalogMods.Remove(catalogMod.Id);
                });
                row.ToggleExpanded = new RelayCommand(() => row.IsExpanded = !row.IsExpanded);
                row.Install = new AsyncRelayCommand(async () =>
                {
                    var release = releases.FirstOrDefault(r => r.Version.Equals(row.SelectedRelease, StringComparison.OrdinalIgnoreCase)) ?? latest;
                    if (release is not null) await InstallOfficial(catalogMod, release, null);
                });
                row.OpenSource = new RelayCommand(() => OpenExternalLink(catalogMod.SourceUrl ?? ""));
                row.PropertyChanged += (_, e) =>
                {
                    if (e.PropertyName == nameof(DiscoverModViewModel.Selected))
                    {
                        if (row.Selected) AddCatalogSelectionWithDependencies(catalogMod); else _selectedCatalogMods.Remove(catalogMod.Id);
                        model.RefreshState();
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
        var catalogForSelection = _officialCatalogResult?.Value;
        model.SelectAll = new RelayCommand(() =>
        {
            var select = !model.AllSelected;
            foreach (var row in model.Mods)
            {
                row.Selected = select;
                if (select && catalogForSelection is not null)
                {
                    var catalogMod = catalogForSelection.Mods.FirstOrDefault(mod => mod.Id.Equals(row.Id, StringComparison.OrdinalIgnoreCase));
                    if (catalogMod is not null) AddCatalogSelectionWithDependencies(catalogMod);
                }
                else _selectedCatalogMods.Remove(row.Id);
            }
            model.RefreshState();
        });
        model.HasUpdates = _officialCatalogResult?.Value?.Mods.Any(catalogMod =>
            catalogMod.Latest is not null &&
            installed.FirstOrDefault(mod => mod.Id.Equals(catalogMod.Id, StringComparison.OrdinalIgnoreCase)) is { } current &&
            IsNewerVersion(catalogMod.Latest.Version, current.Version)) == true;
        model.SortByName = new RelayCommand(() => SortCatalogBy("Name"));
        model.RefreshState();
    }

    private void SortCatalogBy(string column)
    {
        if (_catalogSortColumn == column) _catalogSortDescending = !_catalogSortDescending;
        else { _catalogSortColumn = column; _catalogSortDescending = false; }
        ShowDashboard(1);
    }

    private void PopulateProfilesModel(ProfileRecord active)
    {
        var model = _viewModel.Profiles;
        model.Items.Clear();
        foreach (var profile in _database.GetProfiles())
        {
            var enabled = _database.GetProfileMods(profile.Id).Where(m => m.Enabled).ToList();
            var included = enabled
                .Select(m => (_database.GetMod(m.ModId)?.Name ?? m.ModId, m.Version ?? _database.GetMod(m.ModId)?.Version ?? ""))
                .ToList();
            var preview = included.Take(3).Select(item => item.Item1).ToList();
            var summary = enabled.Count == 0
                ? "No mods turned on"
                : CountPhrase(enabled.Count, "active mod") + (preview.Count == 0 ? "" : $"{Environment.NewLine}{string.Join(", ", preview)}{(included.Count > preview.Count ? $" … +{included.Count - preview.Count} more" : "")}");
            var row = new ProfileRowViewModel
            {
                Id = profile.Id,
                Name = profile.Name,
                IsActive = profile.Id.Equals(active.Id, StringComparison.OrdinalIgnoreCase),
                Summary = summary
            };
            foreach (var item in included) row.IncludedMods.Add(string.IsNullOrWhiteSpace(item.Item2) ? item.Item1 : $"{item.Item1}  ·  {item.Item2}");
            row.ToggleExpanded = new RelayCommand(() => row.IsExpanded = !row.IsExpanded);
            row.Rename = new RelayCommand(() => row.IsRenaming = true);
            row.SaveRename = new RelayCommand(() =>
            {
                try { _database.RenameProfile(profile.Id, row.Name); SetStatus($"Renamed setup to {row.Name.Trim()}."); row.IsRenaming = false; ShowDashboard(3); }
                catch (InvalidOperationException ex) { SetStatus(ex.Message, error: true); }
            });
            row.Use = new RelayCommand(() => { _database.SetActiveProfile(profile.Id); TryRedeployAfterProfileChange(profile); ShowDashboard(3); });
            row.Duplicate = new RelayCommand(() => CreateProfile(profile.Name + " Copy", profile));
            row.Export = new AsyncRelayCommand(() => ExportCurrentProfile(profile));
            row.Delete = new AsyncRelayCommand(async () =>
            {
                if (!await Confirm("Delete setup", $"Delete setup ‘{profile.Name}’ and its saved mod selections? This cannot be undone.", "Delete setup", destructive: true)) return;
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
        model.Intro = game is null ? "Choose your game folder, apply your mod setup, and install UE4SS only when a mod needs it." : "Your game is connected. Apply your mod setup, and install UE4SS only when a mod needs it.";
        model.GamePath = game?.InstallPath ?? "";
        model.SavedGamePath = game?.InstallPath ?? "";
        model.HasGame = game is not null;
        model.GameStatus = game is null ? "Choose your Ragnarock folder to get started." : _detector.Validate(game.InstallPath).IsValid ? "Ragnarock is ready." : "This folder needs attention. Choose the correct Ragnarock folder.";
        if (game is null)
        {
            model.ApplyStatus = "Choose your Ragnarock folder above first.";
        }
        else if (planResult is { Success: true, Value: not null } plan)
        {
            var applyLines = new List<string>
            {
                !_changesPending && IsDeploymentSynchronized(plan.Value) ? "Your current mod setup is active in Ragnarock." : "Your current mod setup has changes waiting to be applied."
            };
            applyLines.AddRange(plan.Value.Conflicts.Where(c => c.BlocksDeployment).Select(FriendlyDeploymentConflict));
            var unmanagedCount = CreateDeploymentService().GetUnmanagedFiles(game.InstallPath).Count;
            if (unmanagedCount > 0) applyLines.Add($"There {(unmanagedCount == 1 ? "is 1 extra mod file" : $"are {unmanagedCount} extra mod files")} in the game folder.");
            model.ApplyStatus = string.Join(Environment.NewLine, applyLines);
        }
        else
        {
            model.ApplyStatus = "We can’t check your mod setup yet. Resolve the issue shown here first.";
        }
        model.CanCleanUp = game is not null && CreateDeploymentService().GetUnmanagedFiles(game.InstallPath).Count > 0;
        model.CanOpenModsFolder = game is not null;
        model.CanOpenUe4ssLogFolder = game is not null && _ue4ss.Detect(game.InstallPath).Installed;
        var scriptMods = planResult?.Success == true && planResult.Value is not null
            ? planResult.Value.Items.Where(i => i.FileType.Equals("ue4ss-lua", StringComparison.OrdinalIgnoreCase) || i.FileType.Equals("ue4ss-dll", StringComparison.OrdinalIgnoreCase)).Select(i => i.ModId).Distinct(StringComparer.OrdinalIgnoreCase).ToList()
            : [];
        model.SupportStatus = game is null ? "Status: Choose your game folder first." : _ue4ss.Detect(game.InstallPath).Installed ? "Status: UE4SS is installed." : "Status: UE4SS is not installed yet.";
        model.SupportNote = scriptMods.Count == 0 ? "Your current setup does not require UE4SS." : "UE4SS is required by the current setup.";
        model.RecoverySummary = planResult is not { Success: true, Value: not null }
            ? "Choose your Ragnarock folder before using these actions."
            : planResult.Value.Conflicts.Count(c => c.BlocksDeployment) == 0
                ? "Nothing is blocking your setup."
                : $"There are {CountPhrase(planResult.Value.Conflicts.Count(c => c.BlocksDeployment), "thing")} to sort out before this setup can be applied.";
        var ue4ssInstalled = game is not null && _ue4ss.Detect(game.InstallPath).Installed;
        var steamOptions = ue4ssInstalled && game is not null
            ? _steamLaunchOptions.Inspect(game.InstallPath, _launchArguments)
            : null;
        var launchArguments = string.IsNullOrWhiteSpace(_launchArguments) && steamOptions?.Applicable == true
            ? steamOptions.CurrentArguments
            : _launchArguments;
        model.LaunchArguments = launchArguments;
        var launchModeArguments = GetLaunchModeArguments();
        var launchPlan = game is null ? null : new RagnarockLauncher().BuildLaunchPlan(game.InstallPath, launchArguments, launchModeArguments);
        model.SteamLaunchOptions = launchPlan?.SteamLaunchOptions ?? ProtonLaunch.BuildSteamLaunchOptions(launchArguments, launchModeArguments);
        model.ShowSteamLaunchOptions = steamOptions?.Applicable == true;
        model.CanConfigureSteamLaunch = steamOptions?.Applicable == true && steamOptions.Configured == false;
        model.LaunchSetupStatus = game is null
            ? "Choose your Ragnarock folder first."
            : !ue4ssInstalled
                ? "No Steam launch option is needed until UE4SS is installed."
            : OperatingSystem.IsWindows()
                ? "Direct launches use the optional arguments below."
                : steamOptions?.Message ?? "Launch setup could not be checked.";
        model.CachedSupportVersions.Clear();
        foreach (var release in _ue4ssReleases.GetCachedReleases()) model.CachedSupportVersions.Add($"{release.Version} ({release.AssetName})");
        model.HasCachedSupport = model.CachedSupportVersions.Count > 0;
        if (model.HasCachedSupport && !model.CachedSupportVersions.Contains(model.SelectedCachedSupport, StringComparer.Ordinal))
            model.SelectedCachedSupport = model.CachedSupportVersions[0];
        model.DetectGame = new RelayCommand(() => { var install = _detector.DetectFirstValid(); if (install is null) SetStatus("No Ragnarock Steam install was found. Choose the folder manually.", error: true); else { SaveGame(install); SetStatus(install.IsValid ? "Ragnarock folder saved." : "Folder saved, but it may need attention.", !install.IsValid); ShowDashboard(); } });
        model.BrowseGame = new AsyncRelayCommand(async () =>
        {
            var folders = await StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions { Title = "Choose Ragnarock folder", AllowMultiple = false });
            if (folders.Count > 0) { model.GamePath = folders[0].Path.LocalPath; }
        });
        model.SaveGame = new RelayCommand(() => { if (string.IsNullOrWhiteSpace(model.GamePath)) { SetStatus("Choose your Ragnarock folder first.", error: true); return; } var install = _detector.Validate(model.GamePath); SaveGame(install); SetStatus(install.IsValid ? "Ragnarock folder saved." : "Folder saved, but it may need attention.", !install.IsValid); ShowDashboard(); });
        model.ApplySetup = new RelayCommand(DeployActiveProfile);
        model.OpenModsFolder = new RelayCommand(() =>
        {
            if (game is null) return;
            var modsFolder = Path.GetDirectoryName(_rules.GetUe4ssModsFile(game.InstallPath));
            if (!string.IsNullOrWhiteSpace(modsFolder)) OpenFolder(modsFolder);
        });
        model.CleanUp = new AsyncRelayCommand(async () =>
        {
            if (game is null) return;
            var unmanaged = CreateDeploymentService().GetUnmanagedFiles(game.InstallPath);
            if (unmanaged.Count == 0 || !await Confirm("Clean up extra mod files", $"Remove {CountPhrase(unmanaged.Count, "file")} that are outside this app’s setup? This cannot be undone.", "Clean up files", destructive: true)) return;
            var result = CreateDeploymentService().RemoveUnmanagedFiles(game.InstallPath); SetStatus(result.Success ? $"Removed {CountPhrase(result.Value, "extra mod file")}." : result.Error ?? "Could not remove extra mod files.", !result.Success); ShowDashboard(4);
        });
        model.CheckSupport = new AsyncRelayCommand(CheckUe4ssUpdates);
        model.InstallSupport = new AsyncRelayCommand(InstallUe4ssSupport);
        model.UseCachedSupport = new RelayCommand(() =>
        {
            var current = _database.GetGame(); var cached = _ue4ssReleases.GetCachedReleases();
            var index = model.CachedSupportVersions.IndexOf(model.SelectedCachedSupport);
            if (current is null || index < 0 || index >= cached.Count) return;
            var result = _ue4ssReleases.InstallCachedRelease(current.InstallPath, cached[index]); SetStatus(result.Success ? $"Installed saved UE4SS {cached[index].Version}." : result.Error ?? "UE4SS installation failed.", !result.Success); ShowDashboard(4);
        });
        model.Rollback = new RelayCommand(() => { var result = CreateDeploymentService().RollbackLatest(); SetStatus(result.Success ? "The last change was undone." : result.Error ?? "Could not undo the last change.", !result.Success); ShowDashboard(4); });
        model.ResetDeployment = new AsyncRelayCommand(async () => { if (!await Confirm("Remove applied files", "Remove the files currently applied by Ragna Mod Manager from the game folder? Backups are retained when possible.", "Remove files", destructive: true)) return; var result = CreateDeploymentService().ResetDeployment(); SetStatus(result.Success ? "The applied files were removed." : result.Error ?? "Could not remove the applied files.", !result.Success); ShowDashboard(4); });
        model.SaveLaunchOptions = new RelayCommand(() => { _launchArguments = model.LaunchArguments ?? ""; File.WriteAllText(_paths.LaunchArgumentsPath, _launchArguments); SetStatus(string.IsNullOrWhiteSpace(_launchArguments) ? "Launch arguments cleared." : "Launch arguments saved."); });
        model.ConfigureSteamLaunch = new RelayCommand(() =>
        {
            var current = _database.GetGame();
            if (current is null) return;
            var result = _steamLaunchOptions.Configure(current.InstallPath, _launchArguments);
            SetStatus(result.Success ? "Steam launch options configured for UE4SS." : result.Error ?? "Could not configure Steam launch options.", !result.Success);
            ShowDashboard(4);
        });
        model.OpenLogs = new RelayCommand(() => OpenFolder(_paths.Logs));
        model.OpenUe4ssLogFolder = new RelayCommand(() =>
        {
            var current = _database.GetGame();
            if (current is null) return;
            var status = _ue4ss.Detect(current.InstallPath);
            if (status.Installed) OpenFolder(status.RootPath);
        });
        model.OpenIssues = new RelayCommand(() => OpenExternalLink("https://github.com/Brollyy/RagnaModManager/issues"));
        model.ToggleRecovery = new RelayCommand(() => model.RecoveryExpanded = !model.RecoveryExpanded);
        model.ToggleLaunchOptions = new RelayCommand(() => model.LaunchOptionsExpanded = !model.LaunchOptionsExpanded);
        model.ToggleTroubleshooting = new RelayCommand(() => model.TroubleshootingExpanded = !model.TroubleshootingExpanded);
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
        if (!await Confirm("Remove selected mods", $"Remove {CountPhrase(selected.Count, "selected mod")} from the manager and all setups?", "Remove Mods", destructive: true)) return;
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
        if (plan.Conflicts.Any(c => c.BlocksDeployment)) return false;

        // A freshly connected game with an empty setup has nothing to deploy yet.
        // Treat that as synchronized so choosing the game folder does not create
        // an "unapplied changes" state before the player has installed a mod.
        // Once a deployment manifest exists, it must still be checked so removing
        // previously managed mods continues to produce a pending deployment.
        if (!File.Exists(_paths.CurrentDeploymentPath)) return plan.Items.Count == 0;

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

    private string FriendlyDeploymentConflict(DeploymentConflict conflict)
    {
        var source = conflict.Items.FirstOrDefault()?.ModId;
        var sourceName = source is null ? "An enabled mod" : GetDisplayModName(source);
        var relatedName = conflict.RelatedModId is null ? "another mod" : GetDisplayModName(conflict.RelatedModId);
        return conflict.Kind switch
        {
            "unmanaged-file" => "A selected mod needs to replace an existing game file. Review and approve this in Settings.",
            "same-target" => $"{sourceName} and {relatedName} try to replace the same game file. The later one may override the earlier one.",
            "declared-conflict" => $"{sourceName} and {relatedName} may be incompatible.",
            "missing-dependency" => $"{sourceName} needs {relatedName}, which is not installed. Install it before applying the setup.",
            "disabled-dependency" => $"{sourceName} needs {relatedName}, which is turned off. Turn it on before applying the setup.",
            "dependency-version" => $"{sourceName} needs a different version of {relatedName}. Choose a compatible version before applying the setup.",
            "profile-version" => $"The selected version of {sourceName} is not installed. Install it or choose another version in Mods.",
            "manager-requirement" => $"{sourceName} requires a newer version of RagnaModManager.",
            "ue4ss-requirement" => $"{sourceName} needs UE4SS. Install it from Settings before applying the setup.",
            _ => "Your setup has an issue that must be resolved before it can be applied."
        };
    }

    private IEnumerable<CatalogMod> SortCatalog(IEnumerable<CatalogMod> mods)
    {
        var sorted = _catalogSortDescending ? mods.OrderByDescending(m => m.Name) : mods.OrderBy(m => m.Name);
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

        var activeProfile = _database.GetActiveProfile();
        var activeState = _database.GetProfileMods(activeProfile.Id).ToDictionary(item => item.ModId, StringComparer.OrdinalIgnoreCase);
        var pinned = available.Where(item => activeState.TryGetValue(item.Mod.Id, out var selected) &&
                                              selected.Version is not null &&
                                              SemanticVersion.IsNewer(item.Current!.Version, selected.Version)).ToList();
        var updatePinned = true;
        if (pinned.Count > 0)
        {
            var pinnedNames = string.Join(", ", pinned.Select(item => item.Mod.Name));
            updatePinned = await Confirm(
                "Update pinned mods?",
                $"You selected an older version for {pinnedNames}. Update it to the latest version now, or keep the pinned version?",
                "Update pinned versions");
        }

        var updates = updatePinned ? available : available.Where(item => !pinned.Contains(item)).ToList();
        if (updates.Count == 0)
        {
            SetStatus("Kept your pinned mod versions.");
            return;
        }

        var activeProfileUpdated = false;
        foreach (var item in updates)
        {
            SetStatus($"Updating {item.Mod.Name}…");
            var result = await _officialCatalog.DownloadAndImportAsync(item.Mod, item.Release!);
            if (!result.Success) { SetStatus($"Could not update {item.Mod.Name}: {result.Error}", error: true); return; }
            activeProfileUpdated |= SelectImportedVersionForActiveProfile(result.Value!.Id, result.Value.Version);
        }
        _changesPending |= activeProfileUpdated;
        SetStatus($"Updated {CountPhrase(updates.Count, "community mod")}.");
        ShowDashboard(1);
    }

    private bool SelectImportedVersionForActiveProfile(string modId, string version)
    {
        var profile = _database.GetActiveProfile();
        var existing = _database.GetProfileMods(profile.Id)
            .FirstOrDefault(item => item.ModId.Equals(modId, StringComparison.OrdinalIgnoreCase));
        if (existing is null || string.Equals(existing.Version, version, StringComparison.OrdinalIgnoreCase)) return false;

        _database.SetProfileMod(profile.Id, modId, existing.Enabled, existing.Priority, version);
        return true;
    }

    private Core.Common.Result<OfficialCatalog>? _officialCatalogResult;
    private bool _catalogLoading;

    private string FormatDependencies(IReadOnlyDictionary<string, string>? dependencies) =>
        dependencies is null or { Count: 0 } ? "None" : string.Join(Environment.NewLine, dependencies.Select(d => $"- {GetDisplayModName(d.Key)} {d.Value}"));

    private string GetDisplayModName(string id)
    {
        var installed = _database.GetMod(id);
        if (installed is not null) return installed.Name;
        return _officialCatalogResult?.Value?.Mods.FirstOrDefault(mod => mod.Id.Equals(id, StringComparison.OrdinalIgnoreCase))?.Name ?? id;
    }

    private async Task InstallOfficial(CatalogMod catalogMod, CatalogRelease release, Button? install)
    {
        if (install is not null) install.IsEnabled = false;
        try
        {
            var activeProfile = _database.GetActiveProfile();
            var selected = _database.GetProfileMods(activeProfile.Id)
                .FirstOrDefault(item => item.ModId.Equals(catalogMod.Id, StringComparison.OrdinalIgnoreCase));
            var installed = _database.GetMod(catalogMod.Id);
            if (selected?.Version is not null && installed is not null &&
                SemanticVersion.IsNewer(installed.Version, selected.Version) &&
                SemanticVersion.IsNewer(release.Version, selected.Version) &&
                !await Confirm(
                    "Update pinned mod?",
                    $"{catalogMod.Name} is pinned to older version {selected.Version}. Update it to {release.Version} now?",
                    "Update pinned version"))
            {
                SetStatus($"Kept {catalogMod.Name} pinned to version {selected.Version}.");
                return;
            }

            SetStatus($"Downloading {catalogMod.Name} {release.Version}…");
            var result = await _officialCatalog.DownloadAndImportAsync(catalogMod, release);
            if (!result.Success)
            {
                SetStatus(result.Error ?? "Community mod download failed.", error: true);
                return;
            }

            _changesPending |= SelectImportedVersionForActiveProfile(result.Value!.Id, result.Value.Version);
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
        _viewModel.HasPendingChanges = _database.GetGame() is not null && _changesPending;
    }

    private async Task SetupAutomatically()
    {
        SetStatus("Looking for your Ragnarock installation…");
        var install = _detector.DetectFirstValid();
        if (install is not { IsValid: true })
        {
            SetStatus("Ragnarock was not found automatically. Choose its folder in Settings.", error: true);
            _tabs.SelectedIndex = 4;
            return;
        }

        var launchReady = await ConfirmSteamLaunchOptionsIfNeeded(install.Root, "automatic setup");
        SaveGame(install);
        ShowDashboard(0);
        SetStatus(launchReady
            ? "Ragnarock is ready. Browse Discover or import a mod."
            : "Ragnarock is connected, but Steam launch options still need to be configured.", error: !launchReady);
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

    private async Task<bool> Confirm(string title, string message, string confirmText, bool destructive = false)
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

        var model = new ConfirmDialogViewModel { Title = title, Message = message, ConfirmText = confirmText, IsDestructive = destructive };
        model.Confirm = new RelayCommand(() => dialog.Close(true));
        model.Cancel = new RelayCommand(() => dialog.Close(false));
        dialog.Content = new ConfirmDialog { DataContext = model };

        return await dialog.ShowDialog<bool>(this);
    }

    private async Task ImportModPackage()
    {
        var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Choose a mod package",
            AllowMultiple = false,
            FileTypeFilter =
            [
                new FilePickerFileType("RagnaModManager packages") { Patterns = ["*.rmod", "*.zip"] }
            ]
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
                ? $"{enabledScriptMods[0]} needs UE4SS, but it is not installed. Install it now before applying your changes?"
                : "The selected mods need UE4SS, but it is not installed. Install it now before applying your changes?";
            if (await Confirm("UE4SS required", description, "Install UE4SS"))
            {
                await CheckUe4ssUpdates();
                if (_ue4ss.Detect(game.InstallPath).Installed)
                {
                    DeployActiveProfile();
                }
                else
                {
                    SetStatus("Changes were not applied. Install UE4SS before using these mods.", error: true);
                }
            }
            else
            {
                SetStatus("Changes were not applied. Install UE4SS before using these mods.", error: true);
            }

            return;
        }

        var deployment = CreateDeploymentService();
        var allowWarnings = false;
        var result = deployment.Deploy(game.InstallPath);
        if (!result.Success && preview.Success && preview.Value is not null)
        {
            var advisoryConflicts = preview.Value.Conflicts
                .Where(c => !c.BlocksDeployment && c.Kind != "unmanaged-file")
                .Select(FriendlyDeploymentConflict)
                .Distinct()
                .ToList();
            if (advisoryConflicts.Count > 0 && await Confirm(
                    "Review mod warnings",
                    $"Some enabled mods overlap or declare incompatibilities. They may override each other or behave unexpectedly.{Environment.NewLine}{Environment.NewLine}{string.Join(Environment.NewLine, advisoryConflicts)}{Environment.NewLine}{Environment.NewLine}Deploy anyway?",
                    "Deploy anyway"))
            {
                allowWarnings = true;
                result = deployment.Deploy(game.InstallPath, allowWarnings: true);
            }
        }
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
                result = deployment.Deploy(game.InstallPath, allowWarnings: allowWarnings, allowUnmanagedFiles: true);
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
            "UE4SS required",
            $"{modName} needs UE4SS, but it is not installed. Install it now?",
            "Install UE4SS");
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

    private async void LaunchGame()
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

        var ue4ssInstalled = _ue4ss.Detect(game.InstallPath).Installed;
        var steamOptions = ue4ssInstalled ? _steamLaunchOptions.Inspect(game.InstallPath, _launchArguments) : null;
        if (steamOptions?.Applicable == true && !steamOptions.Configured)
        {
            if (!await ConfirmSteamLaunchOptionsIfNeeded(game.InstallPath, "launching Ragnarock"))
            {
                SetStatus("Ragnarock was not launched. Configure the Steam launch option first.", error: true);
                return;
            }
        }

        var result = new RagnarockLauncher().Launch(game.InstallPath, _launchArguments, GetLaunchModeArguments());
        SetStatus(result.Success ? "Launch requested." : result.Error ?? "Launch failed.", !result.Success);
    }

    private string GetLaunchModeArguments() => _launchMode switch
    {
        "Flat" => "-nohmd",
        "VR" => "-vr",
        _ => ""
    };

    private async Task<bool> ConfirmSteamLaunchOptionsIfNeeded(string gameRoot, string context)
    {
        if (!_ue4ss.Detect(gameRoot).Installed) return true;
        var status = _steamLaunchOptions.Inspect(gameRoot, _launchArguments);
        if (!status.Applicable || status.Configured) return true;

        var confirmed = await Confirm(
            "Configure Steam for UE4SS?",
            $"This is a Steam installation. To load UE4SS through Proton, Ragna Mod Manager needs to add this Steam launch option:{Environment.NewLine}{Environment.NewLine}{status.RequiredOptions}{Environment.NewLine}{Environment.NewLine}Allow the manager to change Steam's launch options before {context}?",
            "Configure Steam launch");
        if (!confirmed) return false;

        var result = _steamLaunchOptions.Configure(gameRoot, _launchArguments);
        if (!result.Success)
        {
            SetStatus(result.Error ?? "Could not configure Steam launch options.", error: true);
            return false;
        }

        return true;
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
            "Remove Mod",
            destructive: true);
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
