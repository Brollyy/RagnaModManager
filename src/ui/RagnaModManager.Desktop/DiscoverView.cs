using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Markup.Xaml;

namespace RagnaModManager.Desktop;

public partial class DiscoverView : UserControl
{
    public DiscoverView() => AvaloniaXamlLoader.Load(this);
    private void OnSearchKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter && DataContext is DiscoverPageViewModel model && model.SearchCommand?.CanExecute(null) == true)
            model.SearchCommand.Execute(null);
    }
}
