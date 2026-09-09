using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Markup.Xaml;
using Avalonia.Interactivity;

namespace RagnaModManager.Desktop;

public partial class ModsView : UserControl
{
    public ModsView()
    {
        AvaloniaXamlLoader.Load(this);
        DragDrop.SetAllowDrop(this, true);
        AddHandler(DragDrop.DropEvent, OnDrop, RoutingStrategies.Bubble);
    }
    private void OnSearchKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter && DataContext is ModsPageViewModel model && model.SearchCommand?.CanExecute(null) == true)
            model.SearchCommand.Execute(null);
    }

    private async void OnDrop(object? sender, DragEventArgs e)
    {
        var file = e.DataTransfer.TryGetFiles()?.FirstOrDefault(item => item.Name.EndsWith(".rmod", StringComparison.OrdinalIgnoreCase));
        if (file is not null && DataContext is ModsPageViewModel model && model.ImportDropped is not null)
            await model.ImportDropped(file.Path.LocalPath);
    }
}
