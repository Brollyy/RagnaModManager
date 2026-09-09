using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Input;

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
    public string Headline { get; set; } = "Welcome! Let’s get Ragnarock ready for mods.";
    public string HeadlineBrush { get; set; } = "#FFB15C";
    public string NextSteps { get; set; } = "Set up your game folder, then choose your first mod.";
    public string SetupName { get; set; } = "Default";
    public string ActiveMods { get; set; } = "None yet";
    public string InstalledMods { get; set; } = "0 installed";
    public string PlayStatus { get; set; } = "Set up first";
    public string PlayDetail { get; set; } = "Choose your game folder";
    public bool ShowSetupAction { get; set; }
    public bool CanOpenGame { get; set; }
    public bool ShowDeploymentNotice { get; set; }
    public string DeploymentMessage { get; set; } = "Your setup is not active.";
    public string DeploymentDetails { get; set; } = "";
    public ICommand? SetupAutomatically { get; set; }
    public ICommand? AddMod { get; set; }
    public ICommand? OpenGameFolder { get; set; }
    public ICommand? OpenModLibrary { get; set; }
}

public sealed class ModRowViewModel : ObservableObject
{
    private bool _selected;
    private bool _enabled;
    private string _selectedVersion = "";

    public string Id { get; init; } = "";
    public string Name { get; init; } = "";
    public string Version { get; init; } = "";
    public string Author { get; init; } = "";
    public string SourceUrl { get; init; } = "";
    public bool HasSource => !string.IsNullOrWhiteSpace(SourceUrl);
    public bool IsMissing { get; init; }
    public bool IsInstalled => !IsMissing;
    public string Status => IsMissing ? "Missing from the mod library" : Enabled ? "Enabled in this setup" : "Disabled in this setup";
    public string StatusBrush => Enabled ? "#4DE1C1" : "#9AAAC2";
    public bool IsDisabled => !Enabled;
    public bool HasMultipleVersions => Versions.Count > 1;
    public bool Selected { get => _selected; set => SetField(ref _selected, value); }
    public bool Enabled
    {
        get => _enabled;
        set { if (!SetField(ref _enabled, value)) return; OnPropertyChanged(nameof(Status)); OnPropertyChanged(nameof(StatusBrush)); OnPropertyChanged(nameof(IsDisabled)); }
    }
    public ObservableCollection<string> Versions { get; } = [];
    public string SelectedVersion { get => _selectedVersion; set => SetField(ref _selectedVersion, value); }
    public bool CanMoveUp { get; init; }
    public bool CanMoveDown { get; init; }
    public ICommand? ToggleEnabled { get; set; }
    public ICommand? ChangeVersion { get; set; }
    public ICommand? MoveUp { get; set; }
    public ICommand? MoveDown { get; set; }
    public ICommand? Details { get; set; }
    public ICommand? OpenSource { get; set; }
    public ICommand? Remove { get; set; }
    public ICommand? RemoveFromSetup { get; set; }
}

public sealed class ModsPageViewModel : ObservableObject
{
    private string _search = "";
    public string Search { get => _search; set => SetField(ref _search, value); }
    public string Subtitle { get; set; } = "These are the mods you have installed. Enable one to use it in your current setup.";
    public ObservableCollection<ModRowViewModel> Items { get; } = [];
    public ObservableCollection<DependencyIssueViewModel> DependencyIssues { get; } = [];
    public bool HasItems => Items.Count > 0;
    public bool IsEmpty => !HasItems;
    public bool HasSearch => !string.IsNullOrWhiteSpace(Search);
    public string EmptyTitle => HasSearch ? "No installed mods match that search." : "No mods installed yet.";
    public string EmptyMessage => HasSearch ? "Try a different mod name." : "Add a mod to start building this setup.";
    public bool ShowAddFirstMod => !HasSearch;
    public bool HasVisibleItems => Items.Any(item => item.IsInstalled);
    public bool HasSelectedItems => Items.Any(item => item.Selected && item.IsInstalled);
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
    public ICommand? SearchAction { get; set; }
    public Func<string, Task>? ImportDropped { get; set; }
    public string DependencyNotice { get; set; } = "";
    public bool HasDependencyNotice => !string.IsNullOrWhiteSpace(DependencyNotice);
    public void RefreshState() { OnPropertyChanged(nameof(HasItems)); OnPropertyChanged(nameof(IsEmpty)); OnPropertyChanged(nameof(HasSearch)); OnPropertyChanged(nameof(EmptyTitle)); OnPropertyChanged(nameof(EmptyMessage)); OnPropertyChanged(nameof(ShowAddFirstMod)); OnPropertyChanged(nameof(HasVisibleItems)); OnPropertyChanged(nameof(HasSelectedItems)); OnPropertyChanged(nameof(HasDependencyNotice)); }
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
    public string Id { get; init; } = "";
    public string Name { get; init; } = "";
    public string Description { get; init; } = "No description provided.";
    public string Latest { get; init; } = "—";
    public string Installed { get; init; } = "Not installed";
    public string InstalledBrush { get; init; } = "#9AAAC2";
    public bool Selected { get => _selected; set => SetField(ref _selected, value); }
    public ObservableCollection<string> Releases { get; } = [];
    public string SelectedRelease { get; set; } = "";
    public ICommand? ToggleSelected { get; set; }
    public ICommand? Install { get; set; }
    public ICommand? Details { get; set; }
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
    public ObservableCollection<DiscoverModViewModel> Mods { get; } = [];
    public bool CanInstallSelected => Mods.Any(mod => mod.Selected);
    public bool HasUpdates { get; set; }
    public ICommand? SearchCommand { get; set; }
    public ICommand? ClearSearch { get; set; }
    public ICommand? Refresh { get; set; }
    public ICommand? InstallSelected { get; set; }
    public ICommand? UpdateAll { get; set; }
    public ICommand? SelectAll { get; set; }
    public ICommand? SortByName { get; set; }
    public ICommand? SortByLatest { get; set; }
    public ICommand? SortByInstalled { get; set; }
    public void RefreshState() { OnPropertyChanged(nameof(IsEmpty)); OnPropertyChanged(nameof(HasSearch)); OnPropertyChanged(nameof(CanInstallSelected)); OnPropertyChanged(nameof(HasUpdates)); }
}

public sealed class ProfileRowViewModel : ObservableObject
{
    public string Id { get; init; } = "";
    public string Name { get; set; } = "";
    public string Summary { get; init; } = "No mods turned on";
    public bool IsActive { get; init; }
    public bool IsInactive => !IsActive;
    public ICommand? Rename { get; set; }
    public ICommand? Use { get; set; }
    public ICommand? Duplicate { get; set; }
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
    private string _launchArguments = "";
    private string _selectedCachedSupport = "";
    public string Intro { get; set; } = "Choose your game folder, apply your mod setup, and add support only when a mod needs it.";
    public string GamePath { get => _gamePath; set => SetField(ref _gamePath, value); }
    public string GameStatus { get; set; } = "Choose your Ragnarock folder to get started.";
    public string ApplyStatus { get; set; } = "Choose your Ragnarock folder above first.";
    public string SupportStatus { get; set; } = "Choose your game folder first.";
    public string SupportNote { get; set; } = "Your current mods don’t need anything extra.";
    public string RecoverySummary { get; set; } = "Choose your Ragnarock folder before using recovery tools.";
    public string LaunchArguments { get => _launchArguments; set => SetField(ref _launchArguments, value); }
    public bool HasGame { get; set; }
    public bool CanApplySetup => HasGame;
    public bool CanRecover => HasGame;
    public bool CanUseSupport => HasGame;
    public bool CanCleanUp { get; set; }
    public bool HasCachedSupport { get; set; }
    public ObservableCollection<string> CachedSupportVersions { get; } = [];
    public string SelectedCachedSupport { get => _selectedCachedSupport; set => SetField(ref _selectedCachedSupport, value); }
    public ICommand? DetectGame { get; set; }
    public ICommand? BrowseGame { get; set; }
    public ICommand? SaveGame { get; set; }
    public ICommand? ApplySetup { get; set; }
    public ICommand? CleanUp { get; set; }
    public ICommand? CheckSupport { get; set; }
    public ICommand? InstallSupport { get; set; }
    public ICommand? UseCachedSupport { get; set; }
    public ICommand? Rollback { get; set; }
    public ICommand? ResetDeployment { get; set; }
    public ICommand? SaveLaunchOptions { get; set; }
    public ICommand? OpenLogs { get; set; }
    public ICommand? OpenIssues { get; set; }
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
    public ICommand? Confirm { get; set; }
    public ICommand? Cancel { get; set; }
}
