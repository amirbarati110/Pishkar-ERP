using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Windows.UI;

namespace ERP.Desktop;

/// <summary>
/// Minimal, code-only fallback window shown when startup fails before the normal
/// MainWindow/MainPage shell can initialize (source-of-truth Appendix A #4).
/// Built without XAML or the app's DesignSystem resources on purpose: those are
/// merged through the same Application.Resources pipeline that a startup failure
/// could itself be part of, so this window must not depend on it.
/// </summary>
internal static class StartupErrorWindow
{
    public static Window Create(Exception exception)
    {
        var window = new Window { Title = "پیشکار ERP — خطای راه‌اندازی" };

        var root = new Grid
        {
            Background = new SolidColorBrush(Color.FromArgb(0xFF, 0x0F, 0x17, 0x2A)),
            Padding = new Thickness(40),
            FlowDirection = FlowDirection.RightToLeft,
        };

        var panel = new StackPanel
        {
            Spacing = 16,
            MaxWidth = 520,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
        };

        panel.Children.Add(new TextBlock
        {
            Text = "پیشکار ERP نتوانست راه‌اندازی شود",
            FontSize = 22,
            FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
            Foreground = new SolidColorBrush(Color.FromArgb(0xFF, 0xFF, 0xFF, 0xFF)),
            TextWrapping = TextWrapping.Wrap,
        });

        panel.Children.Add(new TextBlock
        {
            Text = "دیتابیس یا فایل‌های موردنیاز پیدا نشدند. اگر این اولین اجرای برنامه است، "
                + "ابزار راه‌اندازی/نصب را اجرا کنید و دوباره تلاش کنید.",
            FontSize = 14,
            Foreground = new SolidColorBrush(Color.FromArgb(0xFF, 0xB8, 0xC9, 0xC2)),
            TextWrapping = TextWrapping.Wrap,
        });

        panel.Children.Add(new TextBox
        {
            Text = exception.Message,
            IsReadOnly = true,
            TextWrapping = TextWrapping.Wrap,
            AcceptsReturn = true,
            Height = 120,
        });

        var exitButton = new Button
        {
            Content = "بستن برنامه",
            HorizontalAlignment = HorizontalAlignment.Left,
        };
        exitButton.Click += (_, _) => Microsoft.UI.Xaml.Application.Current.Exit();
        panel.Children.Add(exitButton);

        root.Children.Add(panel);
        window.Content = root;
        return window;
    }
}
