using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace RagnaModManager.Desktop;

public sealed class MainWindowViewModel : INotifyPropertyChanged
{
    private string _currentSetup = "Default";
    private bool _canLaunch;
    private bool _hasPendingChanges;

    public string WindowTitle => "Ragna Mod Manager";
    public string CurrentSetup
    {
        get => _currentSetup;
        set => SetField(ref _currentSetup, value);
    }
    public string CurrentSetupLabel => $"Current setup: {CurrentSetup}";
    public bool CanLaunch
    {
        get => _canLaunch;
        set => SetField(ref _canLaunch, value);
    }
    public bool HasPendingChanges
    {
        get => _hasPendingChanges;
        set => SetField(ref _hasPendingChanges, value);
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    private void SetField<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return;
        field = value;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        if (propertyName == nameof(CurrentSetup))
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(CurrentSetupLabel)));
    }
}
