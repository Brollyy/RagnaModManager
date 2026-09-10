using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Controls.Primitives;
using Avalonia.Markup.Xaml;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.Themes.Fluent;

namespace RagnaModManager.Desktop;

public sealed class App : Application
{
    public override void Initialize()
    {
        RequestedThemeVariant = ThemeVariant.Dark;
        Styles.Add(new FluentTheme());

        var ink = new SolidColorBrush(Color.Parse("#F4F7FB"));
        var muted = new SolidColorBrush(Color.Parse("#9AAAC2"));
        var panel = new SolidColorBrush(Color.Parse("#111B2E"));
        var surface = new SolidColorBrush(Color.Parse("#17243B"));
        var accent = new SolidColorBrush(Color.Parse("#4DE1C1"));
        var accentSoft = new SolidColorBrush(Color.Parse("#173D3D"));
        var line = new SolidColorBrush(Color.Parse("#2A3A55"));

        Resources["ControlForeground"] = ink;
        Resources["SystemControlForegroundBaseMediumLowBrush"] = muted;
        Resources["SystemControlBackgroundAltHighBrush"] = panel;
        Resources["SystemControlBackgroundBaseLowBrush"] = surface;
        Resources["TextControlBackgroundFocused"] = surface;
        Resources["TextControlBorderBrushFocused"] = line;
        Resources["TextControlBorderThemeThicknessFocused"] = new Thickness(1);
        Resources["TextControlForegroundFocused"] = ink;
        Resources["SystemAccentColor"] = accent.Color;
        Resources["SystemAccentColorLight1"] = accent.Color;

        Styles.Add(new Style(x => x.OfType<Window>()) { Setters = { new Setter(Window.BackgroundProperty, new SolidColorBrush(Color.Parse("#0B1220"))), new Setter(Window.ForegroundProperty, ink) } });
        Styles.Add(new Style(x => x.OfType<Button>()) { Setters = { new Setter(Button.BackgroundProperty, surface), new Setter(Button.BorderBrushProperty, line), new Setter(Button.BorderThicknessProperty, new Thickness(1)), new Setter(Button.ForegroundProperty, ink), new Setter(Button.CornerRadiusProperty, new CornerRadius(8)), new Setter(Button.PaddingProperty, new Thickness(13, 9)) } });
        Styles.Add(new Style(x => x.OfType<Button>().Class("accent")) { Setters = { new Setter(Button.BackgroundProperty, accent), new Setter(Button.ForegroundProperty, new SolidColorBrush(Color.Parse("#08151A"))), new Setter(Button.BorderBrushProperty, accent) } });
        Styles.Add(new Style(x => x.OfType<TextBox>()) { Setters = { new Setter(TextBox.BackgroundProperty, surface), new Setter(TextBox.BorderBrushProperty, line), new Setter(TextBox.ForegroundProperty, ink), new Setter(TextBox.CornerRadiusProperty, new CornerRadius(8)) } });
        Styles.Add(new Style(x => x.OfType<ComboBox>()) { Setters = { new Setter(ComboBox.BackgroundProperty, surface), new Setter(ComboBox.BorderBrushProperty, line), new Setter(ComboBox.ForegroundProperty, ink) } });
        Styles.Add(new Style(x => x.OfType<Border>().Class("card"))
        {
            Setters =
            {
                new Setter(Border.BackgroundProperty, panel),
                new Setter(Border.BorderBrushProperty, line),
                new Setter(Border.BorderThicknessProperty, new Thickness(1)),
                new Setter(Border.CornerRadiusProperty, new CornerRadius(12)),
                new Setter(Border.BoxShadowProperty, BoxShadows.Parse("0 4 12 0 #15000000"))
            }
        });
        Styles.Add(new Style(x => x.OfType<Border>().Class("metric"))
        {
            Setters =
            {
                new Setter(Border.BackgroundProperty, surface),
                new Setter(Border.BorderBrushProperty, line),
                new Setter(Border.BorderThicknessProperty, new Thickness(1)),
                new Setter(Border.CornerRadiusProperty, new CornerRadius(10)),
                new Setter(Border.BoxShadowProperty, BoxShadows.Parse("0 3 10 0 #12000000"))
            }
        });
        Styles.Add(new Style(x => x.OfType<Border>().Class("empty"))
        {
            Setters =
            {
                new Setter(Border.BackgroundProperty, panel),
                new Setter(Border.BorderBrushProperty, line),
                new Setter(Border.BorderThicknessProperty, new Thickness(1)),
                new Setter(Border.CornerRadiusProperty, new CornerRadius(10)),
                new Setter(Border.BoxShadowProperty, BoxShadows.Parse("0 4 12 0 #15000000"))
            }
        });
        Styles.Add(new Style(x => x.OfType<Border>().Class("warning"))
        {
            Setters =
            {
                new Setter(Border.BackgroundProperty, new SolidColorBrush(Color.Parse("#3B2D1B"))),
                new Setter(Border.BorderBrushProperty, new SolidColorBrush(Color.Parse("#FFB15C"))),
                new Setter(Border.BorderThicknessProperty, new Thickness(1)),
                new Setter(Border.CornerRadiusProperty, new CornerRadius(10))
            }
        });
        Styles.Add(new Style(x => x.OfType<ToggleButton>().Class("disclosure"))
        {
            Setters =
            {
                new Setter(ToggleButton.BackgroundProperty, Brushes.Transparent),
                new Setter(ToggleButton.BorderBrushProperty, Brushes.Transparent),
                new Setter(ToggleButton.ForegroundProperty, ink),
                new Setter(ToggleButton.BorderThicknessProperty, new Thickness(0)),
                new Setter(ToggleButton.PaddingProperty, new Thickness(0)),
                new Setter(ToggleButton.HorizontalContentAlignmentProperty, Avalonia.Layout.HorizontalAlignment.Stretch)
            }
        });
        Styles.Add(new Style(x => x.OfType<TabItem>()) { Setters = { new Setter(TabItem.ForegroundProperty, muted), new Setter(TabItem.PaddingProperty, new Thickness(16, 12)), new Setter(TabItem.MarginProperty, new Thickness(0, 2, 10, 2)) } });
        Styles.Add(new Style(x => x.OfType<TabItem>().Class(":selected")) { Setters = { new Setter(TabItem.ForegroundProperty, accent), new Setter(TabItem.BackgroundProperty, accentSoft), new Setter(TabItem.FontWeightProperty, FontWeight.SemiBold) } });
    }

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            desktop.MainWindow = new MainWindow();
        }

        base.OnFrameworkInitializationCompleted();
    }
}
