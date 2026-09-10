using Avalonia.Controls;
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
        AddHandler(DragDrop.DragOverEvent, OnDragOver, RoutingStrategies.Bubble);
        AddHandler(DragDrop.DragLeaveEvent, OnDragLeave, RoutingStrategies.Bubble);
        AddHandler(DragDrop.DropEvent, OnDrop, RoutingStrategies.Bubble);
    }

    private void OnDragOver(object? sender, DragEventArgs e)
    {
        if (DataContext is ModsPageViewModel model) model.IsDragOver = true;
        e.DragEffects = DragDropEffects.Copy;
        e.Handled = true;
    }

    private void OnDragLeave(object? sender, DragEventArgs e)
    {
        if (DataContext is ModsPageViewModel model) model.IsDragOver = false;
    }

    private void OnSearchKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter && DataContext is ModsPageViewModel model && model.SearchCommand?.CanExecute(null) == true)
        {
            model.SearchCommand.Execute(null);
        }
    }

    private async void OnDrop(object? sender, DragEventArgs e)
    {
        if (DataContext is ModsPageViewModel model) model.IsDragOver = false;
        var file = e.DataTransfer.TryGetFiles()?.FirstOrDefault(item => IsModPackage(item.Name));
        if (file is null || DataContext is not ModsPageViewModel page || page.ImportDropped is null) return;
        e.Handled = true;
        await page.ImportDropped(file.Path.LocalPath);
    }

    private static bool IsModPackage(string name) =>
        name.EndsWith(".rmod", StringComparison.OrdinalIgnoreCase) ||
        name.EndsWith(".zip", StringComparison.OrdinalIgnoreCase);

}
