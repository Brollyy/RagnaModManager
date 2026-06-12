using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using RagnaModManager.Core.Database;
using RagnaModManager.Core.Deployment;
using RagnaModManager.Core.Logging;
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
    private const string Ue4ssZipHelp = "Script mods need RE-UE4SS. Choose the RE-UE4SS release zip for Unreal 4 games; it must contain UE4SS.dll, UE4SS-settings.ini, and a proxy DLL such as xinput1_3.dll. The manager will place those files in Ragnarock's UE4SS layout.";

    private readonly AppPaths _paths;
    private readonly ManagerDatabase _database;
    private readonly AppLogger _logger;
    private readonly RagnarockDetector _detector = new();
    private readonly RagnarockDeploymentRules _rules = new();
    private readonly Ue4ssService _ue4ss = new();
    private readonly Ue4ssReleaseService _ue4ssReleases;
    private readonly FolderOpener _folderOpener = new();

    private readonly ContentControl _body = new();
    private readonly TextBlock _status = new() { TextWrapping = TextWrapping.Wrap };

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

        try
        {
            _database.Initialize();
            _logger.Info("RagnaModManager desktop UI started.");
            BuildShell();
            ShowDashboard();
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
            Margin = new Thickness(0, 0, 0, 14)
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

        DockPanel.SetDock(header, Dock.Top);
        root.Children.Add(header);

        _status.Margin = new Thickness(0, 12, 0, 0);
        DockPanel.SetDock(_status, Dock.Bottom);
        root.Children.Add(_status);

        root.Children.Add(new ScrollViewer { Content = _body });
        Content = root;
    }

    private void ShowDashboard()
    {
        var game = _database.GetGame();
        var active = _database.GetActiveProfile();
        var mods = _database.GetMods();
        var planResult = game is null ? null : CreateDeploymentService().Preview(game.InstallPath);

        _body.Content = Page("My Mods",
            BuildQuickActions(game, mods, planResult),
            BuildGameSetup(game),
            BuildModList(active, mods),
            BuildPlaysets(active),
            BuildHealthSummary(game, planResult));
    }

    private Control BuildQuickActions(GameRecord? game, IReadOnlyList<ModRecord> mods, Core.Common.Result<DeploymentPlan>? planResult)
    {
        var import = Button("Add Mod");
        import.Click += async (_, _) => await ImportModPackage();

        var apply = PrimaryButton("Apply Changes");
        apply.IsEnabled = game is not null && (planResult?.Success != false);
        apply.Click += (_, _) => DeployActiveProfile();

        var openGame = Button("Open Game Folder");
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

        var enabledCount = GetEnabledMods().Count;
        var summary = game is null
            ? "Choose your Ragnarock folder to start managing mods."
            : $"{mods.Count} installed, {enabledCount} enabled.";

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
                        new TextBlock { Text = summary, FontSize = 20, FontWeight = FontWeight.SemiBold },
                        new TextBlock
                        {
                            Text = "Add a package, turn mods on or off, then apply changes before launching the game.",
                            Foreground = Brushes.DimGray,
                            TextWrapping = TextWrapping.Wrap
                        }
                    }
                }, 0),
                Cell(Row(import, apply, openGame), 1)
            }
        });
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
            ? "Ragnarock is not configured yet."
            : FriendlyInstallStatus(_detector.Validate(game.InstallPath)));

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
            list.Children.Add(EmptyState("No mods installed yet.", "Import a .rmod package to add it to this playset.", addFirst));
        }
        else
        {
            foreach (var mod in mods)
            {
                state.TryGetValue(mod.Id, out var profileMod);
                list.Children.Add(ModRow(active, mod, profileMod));
            }
        }

        return Section($"Mods in {active.Name}", list);
    }

    private Control ModRow(ProfileRecord active, ModRecord mod, ProfileModRecord? profileMod)
    {
        var enabled = new CheckBox
        {
            IsChecked = profileMod?.Enabled == true,
            Content = profileMod?.Enabled == true ? "On" : "Off",
            VerticalAlignment = VerticalAlignment.Center
        };
        enabled.Click += (_, _) =>
        {
            var isEnabled = enabled.IsChecked == true;
            _database.SetProfileMod(active.Id, mod.Id, isEnabled, profileMod?.Priority ?? 0);
            SetStatus($"{mod.Name} is now {(isEnabled ? "enabled" : "disabled")}. Apply changes when ready.");
            ShowDashboard();
        };

        var moveEarlier = Button("Move Up");
        moveEarlier.Click += (_, _) => ChangePriority(active.Id, mod, profileMod?.Priority ?? 0, -1);

        var moveLater = Button("Move Down");
        moveLater.Click += (_, _) => ChangePriority(active.Id, mod, profileMod?.Priority ?? 0, 1);

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
                            Text = $"Version {mod.Version}" + (string.IsNullOrWhiteSpace(mod.Author) ? "" : $" by {mod.Author}"),
                            Foreground = Brushes.DimGray,
                            TextWrapping = TextWrapping.Wrap
                        },
                        new TextBlock
                        {
                            Text = profileMod?.Enabled == true ? "Will be installed on next apply." : "Kept in your library, not installed in the game.",
                            Foreground = profileMod?.Enabled == true ? Brushes.DarkGreen : Brushes.DimGray,
                            TextWrapping = TextWrapping.Wrap
                        }
                    }
                }, 0),
                Cell(Row(enabled, moveEarlier, moveLater), 1)
            }
        });
    }

    private Control BuildPlaysets(ProfileRecord active)
    {
        var profiles = _database.GetProfiles();
        var selectedProfile = profiles.ToList().FindIndex(p => p.Id == active.Id);
        var picker = new ComboBox
        {
            ItemsSource = profiles.Select(p => p.Name).ToList(),
            SelectedIndex = Math.Max(0, selectedProfile),
            MinWidth = 220
        };

        var switchProfile = Button("Use Playset");
        switchProfile.Click += (_, _) =>
        {
            if (picker.SelectedIndex < 0 || picker.SelectedIndex >= profiles.Count)
            {
                SetStatus("Choose a playset first.", error: true);
                return;
            }

            var profile = profiles[picker.SelectedIndex];
            _database.SetActiveProfile(profile.Id);
            TryRedeployAfterProfileChange(profile);
            ShowDashboard();
        };

        var name = new TextBox { Watermark = "New playset name", MinWidth = 220 };
        var create = Button("Create Playset");
        create.Click += (_, _) =>
        {
            var cleanName = string.IsNullOrWhiteSpace(name.Text) ? "" : name.Text.Trim();
            if (string.IsNullOrWhiteSpace(cleanName))
            {
                SetStatus("Enter a playset name first.", error: true);
                return;
            }

            var id = MakeProfileId(cleanName);
            var suffix = 2;
            var candidate = id;
            while (_database.GetProfile(candidate) is not null)
            {
                candidate = $"{id}-{suffix++}";
            }

            try
            {
                _database.CreateProfile(candidate, cleanName);
                _database.SetActiveProfile(candidate);
                SetStatus($"Created playset {cleanName}.");
                ShowDashboard();
            }
            catch (Exception ex) when (ex is InvalidOperationException)
            {
                SetStatus(ex.Message, error: true);
            }
        };

        return Section("Playsets",
            Text("Use playsets when you want separate mod combinations for different sessions."),
            Wrap(picker, switchProfile, name, create));
    }

    private Control BuildHealthSummary(GameRecord? game, Core.Common.Result<DeploymentPlan>? planResult)
    {
        var lines = new List<string>();

        if (game is null)
        {
            lines.Add("Game folder: needed");
        }
        else
        {
            var report = new RagnarockCompatibilityChecker().Check(game.InstallPath);
            lines.Add(report.CanManage ? "Game folder: ready" : "Game folder: needs attention");
            lines.AddRange(report.Errors.Select(e => "Problem: " + e));
            lines.AddRange(report.Warnings.Select(w => "Note: " + w));

            var ue4ss = _ue4ss.Detect(game.InstallPath);
            lines.Add(ue4ss.Installed ? "Script mod support: RE-UE4SS installed" : "Script mod support: RE-UE4SS not installed");

            if (planResult is { Success: true, Value: not null })
            {
                lines.Add($"{planResult.Value.Items.Count} files will be applied.");
                lines.AddRange(planResult.Value.Warnings.Select(w => "Note: " + w));
                lines.AddRange(planResult.Value.Conflicts.Select(c => (c.BlocksDeployment ? "Problem: " : "Note: ") + c.Message));
            }
            else if (planResult is { Success: false })
            {
                lines.Add("Problem: " + planResult.Error);
            }
        }

        var checkUe4ss = PrimaryButton("Check for RE-UE4SS Updates");
        checkUe4ss.IsEnabled = game is not null;
        checkUe4ss.Click += async (_, _) => await CheckUe4ssUpdates();

        var installUe4ss = Button("Install RE-UE4SS from Local ZIP");
        installUe4ss.IsEnabled = game is not null;
        installUe4ss.Click += async (_, _) => await InstallUe4ssSupport();

        var openData = Button("Open Manager Folder");
        openData.Click += (_, _) => OpenFolder(_paths.Root);

        var controls = new List<Control>
        {
            Text(string.Join(Environment.NewLine, lines)),
            MutedText("The manager can download RE-UE4SS from GitHub releases, cache downloaded versions locally, and reinstall an older cached version if a newer one breaks your setup."),
            Row(checkUe4ss, installUe4ss, openData)
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

            var installCached = Button("Install Selected Cached Version");
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

        return Section("Status", controls.ToArray());
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
            SetStatus($"RE-UE4SS {check.Value.Installed.Version ?? latest.Version} is up to date.");
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

        var result = new PackageImporter(_paths, _database, _logger).Import(files[0].Path.LocalPath);
        if (result.Success)
        {
            SetStatus($"Imported {result.Value!.Name}. Turn it on, then apply changes.");
            ShowDashboard();
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

    private void DeployActiveProfile()
    {
        var game = _database.GetGame();
        if (game is null)
        {
            SetStatus("Choose your Ragnarock folder first.", error: true);
            return;
        }

        var result = CreateDeploymentService().Deploy(game.InstallPath);
        SetStatus(result.Success ? "Changes applied to Ragnarock." : result.Error ?? "Apply failed.", !result.Success);
        ShowDashboard();
    }

    private void LaunchGame()
    {
        var game = _database.GetGame();
        if (game is null)
        {
            SetStatus("Choose your Ragnarock folder before launching.", error: true);
            return;
        }

        var result = new RagnarockLauncher().Launch(game.InstallPath);
        SetStatus(result.Success ? "Launch requested." : result.Error ?? "Launch failed.", !result.Success);
    }

    private void ChangePriority(string profileId, ModRecord mod, int currentPriority, int delta)
    {
        var newPriority = currentPriority + delta;
        _database.SetProfileMod(profileId, mod.Id, true, newPriority);
        SetStatus($"{mod.Name} load order updated. Apply changes when ready.");
        ShowDashboard();
    }

    private void TryRedeployAfterProfileChange(ProfileRecord profile)
    {
        var game = _database.GetGame();
        if (game is null)
        {
            SetStatus($"Using playset {profile.Name}. Choose your Ragnarock folder before applying it.");
            return;
        }

        var result = CreateDeploymentService().Deploy(game.InstallPath);
        SetStatus(result.Success ? $"Using playset {profile.Name}; changes applied." : $"Using playset {profile.Name}, but apply failed: {result.Error}", !result.Success);
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

        return string.IsNullOrWhiteSpace(id) ? "playset" : id;
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
        Padding = new Thickness(12),
        Child = content
    };

    private static Control Cell(Control control, int column)
    {
        Grid.SetColumn(control, column);
        return control;
    }
}
