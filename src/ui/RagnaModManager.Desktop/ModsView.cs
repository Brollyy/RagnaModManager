using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Markup.Xaml;

namespace RagnaModManager.Desktop;

public partial class ModsView : UserControl
{
    public ModsView() => AvaloniaXamlLoader.Load(this);
    private void OnSearchKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter && DataContext is ModsPageViewModel model && model.SearchCommand?.CanExecute(null) == true)
            model.SearchCommand.Execute(null);
    }
}
