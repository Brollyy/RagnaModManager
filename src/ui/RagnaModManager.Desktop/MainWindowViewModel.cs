using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Input;
using Avalonia;
using RagnaModManager.Core.Compatibility;

namespace RagnaModManager.Desktop;

public abstract class ObservableObject : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;

    protected bool SetField<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return false;
        field = value;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        return true;
    }

    protected void OnPropertyChanged([CallerMemberName] string? propertyName = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}

public sealed class RelayCommand : ICommand
{
    private readonly Action _execute;
    private readonly Func<bool>? _canExecute;

    public RelayCommand(Action execute, Func<bool>? canExecute = null)
    {
        _execute = execute;
        _canExecute = canExecute;
    }

    public bool CanExecute(object? parameter) => _canExecute?.Invoke() ?? true;
    public void Execute(object? parameter) => _execute();
    public event EventHandler? CanExecuteChanged;
    public void Refresh() => CanExecuteChanged?.Invoke(this, EventArgs.Empty);
}

public sealed class AsyncRelayCommand : ICommand
{
    private readonly Func<Task> _execute;
    private readonly Func<bool>? _canExecute;
    private bool _running;

    public AsyncRelayCommand(Func<Task> execute, Func<bool>? canExecute = null)
    {
        _execute = execute;
        _canExecute = canExecute;
    }

    public bool CanExecute(object? parameter) => !_running && (_canExecute?.Invoke() ?? true);
    public async void Execute(object? parameter)
    {
        if (!CanExecute(parameter)) return;
        _running = true;
        CanExecuteChanged?.Invoke(this, EventArgs.Empty);
        try { await _execute(); }
        finally
        {
            _running = false;
            CanExecuteChanged?.Invoke(this, EventArgs.Empty);
        }
    }
    public event EventHandler? CanExecuteChanged;
}

public sealed class MainWindowViewModel : ObservableObject
{
    private string _currentSetup = "Default";
    private bool _canLaunch;
    private bool _hasPendingChanges;
    private int _selectedTab;
    private ICommand? _applyChanges;
    private ICommand? _revertChanges;

    public string WindowTitle => "Ragna Mod Manager";
    public string VersionLabel => $"Version {ManagerCompatibility.Version}";
    public string CurrentSetup
    {
        get => _currentSetup;
        set
        {
            if (!SetField(ref _currentSetup, value)) return;
            OnPropertyChanged(nameof(CurrentSetupLabel));
        }
    }
    public string CurrentSetupLabel => $"Current setup: {CurrentSetup}";
    public bool CanLaunch { get => _canLaunch; set => SetField(ref _canLaunch, value); }
    public bool HasPendingChanges { get => _hasPendingChanges; set => SetField(ref _hasPendingChanges, value); }
    public int SelectedTab { get => _selectedTab; set => SetField(ref _selectedTab, value); }

    public DashboardViewModel Dashboard { get; } = new();
    public ModsPageViewModel Mods { get; } = new();
    public DiscoverPageViewModel Discover { get; } = new();
    public ProfilesPageViewModel Profiles { get; } = new();
    public SettingsPageViewModel Settings { get; } = new();
    public ICommand? ApplyChanges { get => _applyChanges; set => SetField(ref _applyChanges, value); }
    public ICommand? RevertChanges { get => _revertChanges; set => SetField(ref _revertChanges, value); }

    public void RefreshPages()
    {
        OnPropertyChanged(nameof(Dashboard));
        OnPropertyChanged(nameof(Mods));
        OnPropertyChanged(nameof(Discover));
        OnPropertyChanged(nameof(Profiles));
        OnPropertyChanged(nameof(Settings));
    }
}

public sealed class DashboardViewModel : ObservableObject
{
    public string Headline { get; set; } = "Let’s get some mods running.";
    public string HeadlineBrush { get; set; } = "#FFB15C";
    public string NextSteps { get; set; } = "Set up your game folder, then choose your first mod.";
    public string SetupName { get; set; } = "Default";
    public string ActiveMods { get; set; } = "None yet";
    public string InstalledMods { get; set; } = "0 installed";
    public string PlayStatus { get; set; } = "Set up first";
    public string PlayDetail { get; set; } = "Choose your game folder";
    public bool ShowSetupAction { get; set; }
    public string QuickActionsTitle { get; set; } = "Get started";
    public string QuickActionsDescription { get; set; } = "Connect the game, then pick the mods you want to use.";
    public bool CanOpenGame { get; set; }
    public bool ShowDeploymentNotice { get; set; }
    public string DeploymentMessage { get; set; } = "Your setup is not active.";
    public string DeploymentDetails { get; set; } = "";
    public ICommand? SetupAutomatically { get; set; }
    public ICommand? DiscoverMods { get; set; }
    public ICommand? AddMod { get; set; }
    public ICommand? OpenGameFolder { get; set; }
    public ICommand? OpenModLibrary { get; set; }

    public void RefreshState()
    {
        OnPropertyChanged(nameof(Headline));
        OnPropertyChanged(nameof(HeadlineBrush));
        OnPropertyChanged(nameof(NextSteps));
        OnPropertyChanged(nameof(SetupName));
        OnPropertyChanged(nameof(ActiveMods));
        OnPropertyChanged(nameof(InstalledMods));
        OnPropertyChanged(nameof(PlayStatus));
        OnPropertyChanged(nameof(PlayDetail));
        OnPropertyChanged(nameof(ShowSetupAction));
        OnPropertyChanged(nameof(QuickActionsTitle));
        OnPropertyChanged(nameof(QuickActionsDescription));
        OnPropertyChanged(nameof(CanOpenGame));
        OnPropertyChanged(nameof(ShowDeploymentNotice));
        OnPropertyChanged(nameof(DeploymentMessage));
        OnPropertyChanged(nameof(DeploymentDetails));
    }
}

public sealed class ModRowViewModel : ObservableObject
{
    private bool _selected;
    private bool _enabled;
    private bool _versionExpanded;
    private bool _detailsExpanded;
    private string _selectedVersion = "";

    public string Id { get; init; } = "";
    public string Name { get; init; } = "";
    public string Version { get; init; } = "";
    public string Author { get; init; } = "";
    public string Description { get; init; } = "No description provided.";
    public string License { get; init; } = "Not specified";
    public string DependenciesText { get; init; } = "None";
    public string SourceUrl { get; init; } = "";
    public bool HasSource => !string.IsNullOrWhiteSpace(SourceUrl);
    public bool IsMissing { get; init; }
    public bool IsInstalled => !IsMissing;
    public bool IsUsingOlderVersion { get; init; }
    public string Status => IsMissing ? "Missing from the mod library" : Enabled ? "Enabled in this setup" : "Disabled in this setup";
    public string StatusBrush => Enabled ? "#4DE1C1" : "#9AAAC2";
    public bool IsDisabled => !Enabled;
    public bool HasMultipleVersions => Versions.Count > 1;
    public bool DetailsExpanded
    {
        get => _detailsExpanded;
        set { if (!SetField(ref _detailsExpanded, value)) return; OnPropertyChanged(nameof(DetailsGlyph)); }
    }
    public string DetailsGlyph => DetailsExpanded ? "⌄" : "›";
    public bool VersionExpanded
    {
        get => _versionExpanded;
        set { if (!SetField(ref _versionExpanded, value)) return; OnPropertyChanged(nameof(VersionLabel)); }
    }
    public string VersionLabel => VersionExpanded ? "Hide" : "Show";
    public bool Selected { get => _selected; set => SetField(ref _selected, value); }
    public bool Enabled
    {
        get => _enabled;
        set { if (!SetField(ref _enabled, value)) return; OnPropertyChanged(nameof(Status)); OnPropertyChanged(nameof(StatusBrush)); OnPropertyChanged(nameof(IsDisabled)); }
    }
    public ObservableCollection<string> Versions { get; } = [];
    public string SelectedVersion
    {
        get => _selectedVersion;
        set
        {
            if (!SetField(ref _selectedVersion, value)) return;
            OnPropertyChanged(nameof(SelectedVersionIndex));
        }
    }
    public int SelectedVersionIndex
    {
        get => Versions.IndexOf(SelectedVersion);
        set
        {
            if (value < 0 || value >= Versions.Count) return;
            SelectedVersion = Versions[value];
        }
    }
    public bool CanMoveUp { get; init; }
    public bool CanMoveDown { get; init; }
    public ICommand? ToggleEnabled { get; set; }
    public ICommand? MoveUp { get; set; }
    public ICommand? MoveDown { get; set; }
    public ICommand? ToggleDetails { get; set; }
    public ICommand? OpenSource { get; set; }
    public ICommand? Remove { get; set; }
    public ICommand? RemoveFromSetup { get; set; }
}

public sealed class ModsPageViewModel : ObservableObject
{
    private string _search = "";
    private bool _bulkActionsExpanded;
    private bool _isDragOver;
    public string Search { get => _search; set => SetField(ref _search, value); }
    public bool BulkActionsExpanded { get => _bulkActionsExpanded; set { if (!SetField(ref _bulkActionsExpanded, value)) return; OnPropertyChanged(nameof(BulkActionsLabel)); } }
    public string BulkActionsLabel => BulkActionsExpanded ? "Hide" : "Show";
    public string SetupName { get; set; } = "Default";
    public string SectionTitle => $"Mods in {SetupName}";
    public string Subtitle { get; set; } = "These are the mods you have installed. Enable one to use it in your current setup.";
    public ObservableCollection<ModRowViewModel> Items { get; } = [];
    public ObservableCollection<DependencyIssueViewModel> DependencyIssues { get; } = [];
    public bool HasItems => Items.Count > 0;
    public bool IsWindows => OperatingSystem.IsWindows();
    public bool IsEmpty => !HasItems;
    public bool HasSearch => !string.IsNullOrWhiteSpace(Search);
    public string EmptyTitle => HasSearch ? "No installed mods match that search." : "No mods installed yet.";
    public string EmptyMessage => HasSearch ? "Try a different mod name." : "Add a mod to start building this setup.";
    public bool ShowAddFirstMod => !HasSearch;
    public bool HasVisibleItems => Items.Any(item => item.IsInstalled);
    public bool HasSelectedItems => Items.Any(item => item.Selected && item.IsInstalled);
    public string SelectedItemsLabel => $"{Items.Count(item => item.Selected && item.IsInstalled)} selected";
    public bool IsDragOver { get => _isDragOver; set { if (!SetField(ref _isDragOver, value)) return; OnPropertyChanged(nameof(DropHint)); } }
    public string DropHint => IsDragOver ? "Release to import this mod" : "Drop a .rmod file anywhere on this page to import it.";
    public ICommand? SearchCommand { get; set; }
    public ICommand? ClearSearch { get; set; }
    public ICommand? AddMod { get; set; }
    public ICommand? SelectAll { get; set; }
    public ICommand? ClearSelection { get; set; }
    public ICommand? EnableAll { get; set; }
    public ICommand? DisableAll { get; set; }
    public ICommand? EnableSelected { get; set; }
    public ICommand? DisableSelected { get; set; }
    public ICommand? RemoveSelected { get; set; }
    public ICommand? ToggleBulkActions { get; set; }
    public ICommand? SearchAction { get; set; }
    public Func<string, Task>? ImportDropped { get; set; }
    public string DependencyNotice { get; set; } = "";
    public bool HasDependencyNotice => !string.IsNullOrWhiteSpace(DependencyNotice);
    public void RefreshState() { OnPropertyChanged(nameof(HasItems)); OnPropertyChanged(nameof(IsEmpty)); OnPropertyChanged(nameof(HasSearch)); OnPropertyChanged(nameof(EmptyTitle)); OnPropertyChanged(nameof(EmptyMessage)); OnPropertyChanged(nameof(ShowAddFirstMod)); OnPropertyChanged(nameof(HasVisibleItems)); OnPropertyChanged(nameof(HasSelectedItems)); OnPropertyChanged(nameof(SelectedItemsLabel)); OnPropertyChanged(nameof(HasDependencyNotice)); OnPropertyChanged(nameof(DropHint)); OnPropertyChanged(nameof(IsWindows)); }
}

public sealed class DependencyIssueViewModel
{
    public string Message { get; init; } = "";
    public string ActionLabel { get; init; } = "";
    public bool HasAction => Action is not null;
    public ICommand? Action { get; init; }
}

public sealed class DiscoverModViewModel : ObservableObject
{
    private bool _selected;
    private bool _isExpanded;
    private string _selectedRelease = "";
    public string Id { get; init; } = "";
    public string Name { get; init; } = "";
    public string Description { get; init; } = "No description provided.";
    public string Author { get; init; } = "Unknown";
    public string License { get; init; } = "Not specified";
    public string Source { get; init; } = "Not specified";
    public string DependenciesText { get; init; } = "None";
    public IReadOnlyDictionary<string, string> ReleaseNotes { get; init; } = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
    public string SelectedReleaseNotes => ReleaseNotes.TryGetValue(SelectedRelease, out var notes) ? notes : "No release notes provided.";
    public bool HasSource => !string.IsNullOrWhiteSpace(Source) && !string.Equals(Source, "Not specified", StringComparison.OrdinalIgnoreCase);
    public string InstallLabel { get; init; } = "Install";
    public bool CanInstall { get; init; } = true;
    public string Latest { get; init; } = "—";
    public string Installed { get; init; } = "Not installed";
    public string InstalledVersion { get; init; } = "";
    public string InstalledBrush { get; init; } = "#696969";
    public bool IsInstalledState => !CanInstall;
    public bool Selected { get => _selected; set => SetField(ref _selected, value); }
    public bool IsExpanded { get => _isExpanded; set { if (!SetField(ref _isExpanded, value)) return; OnPropertyChanged(nameof(DisclosureGlyph)); } }
    public string DisclosureGlyph => IsExpanded ? "⌄" : "›";
    public ObservableCollection<string> Releases { get; } = [];
    public string SelectedRelease
    {
        get => _selectedRelease;
        set
        {
            if (!SetField(ref _selectedRelease, value)) return;
            OnPropertyChanged(nameof(CanInstallSelectedRelease));
            OnPropertyChanged(nameof(SelectedReleaseNotes));
        }
    }
    public bool CanInstallSelectedRelease => string.IsNullOrWhiteSpace(InstalledVersion) || !string.Equals(SelectedRelease, InstalledVersion, StringComparison.OrdinalIgnoreCase);
    public ICommand? ToggleSelected { get; set; }
    public ICommand? ToggleExpanded { get; set; }
    public ICommand? Install { get; set; }
    public ICommand? OpenSource { get; set; }
}

public sealed class DiscoverPageViewModel : ObservableObject
{
    private string _search = "";
    public string Search { get => _search; set => SetField(ref _search, value); }
    public bool HasSearch => !string.IsNullOrWhiteSpace(Search);
    public string Status { get; set; } = "Community catalog has not been loaded yet.";
    public bool IsLoading { get; set; }
    public bool HasCatalog { get; set; }
    public bool IsEmpty => Mods.Count == 0;
    public bool HasResults => Mods.Count > 0;
    public bool HasStatus => !string.IsNullOrWhiteSpace(Status);
    public Thickness EmptyMargin => HasSearch ? new Thickness(0, 35, 0, 0) : new Thickness(0);
    public ObservableCollection<DiscoverModViewModel> Mods { get; } = [];
    public bool CanInstallSelected => Mods.Any(mod => mod.Selected);
    public bool AllSelected => Mods.Count > 0 && Mods.All(mod => mod.Selected);
    public bool HasUpdates { get; set; }
    public ICommand? SearchCommand { get; set; }
    public ICommand? ClearSearch { get; set; }
    public ICommand? Refresh { get; set; }
    public ICommand? InstallSelected { get; set; }
    public ICommand? UpdateAll { get; set; }
    public ICommand? SelectAll { get; set; }
    public ICommand? SortByName { get; set; }
    public void RefreshState() { OnPropertyChanged(nameof(IsEmpty)); OnPropertyChanged(nameof(HasResults)); OnPropertyChanged(nameof(HasSearch)); OnPropertyChanged(nameof(EmptyMargin)); OnPropertyChanged(nameof(HasStatus)); OnPropertyChanged(nameof(CanInstallSelected)); OnPropertyChanged(nameof(AllSelected)); OnPropertyChanged(nameof(HasUpdates)); }
}

public sealed class ProfileRowViewModel : ObservableObject
{
    private bool _isExpanded;
    private bool _isRenaming;
    public string Id { get; init; } = "";
    public string Name { get; set; } = "";
    public string Summary { get; init; } = "No mods turned on";
    public bool IsActive { get; init; }
    public bool IsInactive => !IsActive;
    public ObservableCollection<string> IncludedMods { get; } = [];
    public bool HasIncludedMods => IncludedMods.Count > 0;
    public bool IsExpanded { get => _isExpanded; set { if (!SetField(ref _isExpanded, value)) return; OnPropertyChanged(nameof(DisclosureGlyph)); } }
    public string DisclosureGlyph => IsExpanded ? "⌄" : "›";
    public bool IsRenaming { get => _isRenaming; set { if (!SetField(ref _isRenaming, value)) return; OnPropertyChanged(nameof(IsNotRenaming)); } }
    public bool IsNotRenaming => !IsRenaming;
    public ICommand? Rename { get; set; }
    public ICommand? SaveRename { get; set; }
    public ICommand? Use { get; set; }
    public ICommand? Duplicate { get; set; }
    public ICommand? Export { get; set; }
    public ICommand? ToggleExpanded { get; set; }
    public ICommand? Delete { get; set; }
}

public sealed class ProfilesPageViewModel : ObservableObject
{
    public ObservableCollection<ProfileRowViewModel> Items { get; } = [];
    public string NewName { get; set; } = "";
    public ICommand? Create { get; set; }
    public ICommand? Export { get; set; }
    public ICommand? Import { get; set; }
}

public sealed class SettingsPageViewModel : ObservableObject
{
    private string _gamePath = "";
    private string _savedGamePath = "";
    private string _launchArguments = "";
    private string _selectedCachedSupport = "";
    private bool _recoveryExpanded;
    private bool _launchOptionsExpanded;
    private bool _troubleshootingExpanded;
    public string Intro { get; set; } = "Choose your game folder, apply your mod setup, and install UE4SS only when a mod needs it.";
    public string GamePath { get => _gamePath; set { if (!SetField(ref _gamePath, value)) return; OnPropertyChanged(nameof(CanSaveGame)); } }
    public string SavedGamePath { get => _savedGamePath; set { if (!SetField(ref _savedGamePath, value)) return; OnPropertyChanged(nameof(CanSaveGame)); } }
    public string GameStatus { get; set; } = "Choose your Ragnarock folder to get started.";
    public string ApplyStatus { get; set; } = "Choose your Ragnarock folder above first.";
    public string SupportStatus { get; set; } = "Choose your game folder first.";
    public string SupportNote { get; set; } = "Your current mods don’t need anything extra.";
    public string RecoverySummary { get; set; } = "Choose your Ragnarock folder before using recovery tools.";
    public string LaunchArguments { get => _launchArguments; set => SetField(ref _launchArguments, value); }
    public bool HasGame { get; set; }
    public bool CanSaveGame => !string.IsNullOrWhiteSpace(GamePath) && !string.Equals(GamePath.Trim(), SavedGamePath.Trim(), StringComparison.OrdinalIgnoreCase);
    public bool CanApplySetup => HasGame;
    public bool CanRecover => HasGame;
    public bool CanUseSupport => HasGame;
    public bool CanCleanUp { get; set; }
    public bool CanOpenModsFolder { get; set; }
    public bool HasCachedSupport { get; set; }
    public ObservableCollection<string> CachedSupportVersions { get; } = [];
    public string SelectedCachedSupport { get => _selectedCachedSupport; set => SetField(ref _selectedCachedSupport, value); }
    public bool RecoveryExpanded { get => _recoveryExpanded; set { if (!SetField(ref _recoveryExpanded, value)) return; OnPropertyChanged(nameof(RecoveryLabel)); } }
    public bool LaunchOptionsExpanded { get => _launchOptionsExpanded; set { if (!SetField(ref _launchOptionsExpanded, value)) return; OnPropertyChanged(nameof(LaunchOptionsLabel)); } }
    public bool TroubleshootingExpanded { get => _troubleshootingExpanded; set { if (!SetField(ref _troubleshootingExpanded, value)) return; OnPropertyChanged(nameof(TroubleshootingLabel)); } }
    public string RecoveryLabel => RecoveryExpanded ? "Hide" : "Show";
    public string LaunchOptionsLabel => LaunchOptionsExpanded ? "Hide" : "Show";
    public string TroubleshootingLabel => TroubleshootingExpanded ? "Hide" : "Show";
    public ICommand? DetectGame { get; set; }
    public ICommand? BrowseGame { get; set; }
    public ICommand? SaveGame { get; set; }
    public ICommand? ApplySetup { get; set; }
    public ICommand? OpenModsFolder { get; set; }
    public ICommand? CleanUp { get; set; }
    public ICommand? CheckSupport { get; set; }
    public ICommand? InstallSupport { get; set; }
    public ICommand? UseCachedSupport { get; set; }
    public ICommand? Rollback { get; set; }
    public ICommand? ResetDeployment { get; set; }
    public ICommand? SaveLaunchOptions { get; set; }
    public ICommand? OpenLogs { get; set; }
    public ICommand? OpenIssues { get; set; }
    public ICommand? ToggleRecovery { get; set; }
    public ICommand? ToggleLaunchOptions { get; set; }
    public ICommand? ToggleTroubleshooting { get; set; }
}

public sealed class DetailsDialogViewModel : ObservableObject
{
    public string Title { get; init; } = "Details";
    public string Message { get; init; } = "";
    public bool HasSource { get; init; }
    public ICommand? OpenSource { get; set; }
    public ICommand? Close { get; set; }
}

public sealed class ConfirmDialogViewModel : ObservableObject
{
    public string Title { get; init; } = "Confirm";
    public string Message { get; init; } = "";
    public string ConfirmText { get; init; } = "Confirm";
    public bool IsDestructive { get; init; }
    public bool IsSafeConfirm => !IsDestructive;
    public ICommand? Confirm { get; set; }
    public ICommand? Cancel { get; set; }
}
