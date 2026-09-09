using Avalonia.Controls;

namespace RagnaModManager.Desktop;

public partial class PageHost : UserControl
{
    public PageHost()
    {
        InitializeComponent();
    }

    public void SetContent(Control content) => PageContent.Content = content;
}
