using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

namespace ERP.Desktop.DesignSystem;

/// <summary>The Pishkar lockup (symbol tile + «پیشکار ERP» + tagline) — the one place the logo is drawn.</summary>
public sealed partial class PishkarLogo : UserControl
{
    public static readonly DependencyProperty SymbolSizeProperty = DependencyProperty.Register(
        nameof(SymbolSize), typeof(double), typeof(PishkarLogo), new PropertyMetadata(36d, OnLookChanged));

    public static readonly DependencyProperty ShowTaglineProperty = DependencyProperty.Register(
        nameof(ShowTagline), typeof(bool), typeof(PishkarLogo), new PropertyMetadata(true, OnLookChanged));

    public static readonly DependencyProperty OnDarkProperty = DependencyProperty.Register(
        nameof(OnDark), typeof(bool), typeof(PishkarLogo), new PropertyMetadata(false, OnLookChanged));

    public static readonly DependencyProperty CaptionProperty = DependencyProperty.Register(
        nameof(Caption), typeof(string), typeof(PishkarLogo), new PropertyMetadata(DefaultCaption, OnLookChanged));

    private const string DefaultCaption = "گرداننده یکپارچه کسب‌وکار شما";

    public PishkarLogo()
    {
        InitializeComponent();
        ApplyLook();
    }

    public double SymbolSize
    {
        get => (double)GetValue(SymbolSizeProperty);
        set => SetValue(SymbolSizeProperty, value);
    }

    public bool ShowTagline
    {
        get => (bool)GetValue(ShowTaglineProperty);
        set => SetValue(ShowTaglineProperty, value);
    }

    /// <summary>
    /// On a dark surface the Figma «لوگو روی تیره» variant: white wordmark. The
    /// navy tile then gets a hairline so it does not dissolve into navy chrome.
    /// </summary>
    public bool OnDark
    {
        get => (bool)GetValue(OnDarkProperty);
        set => SetValue(OnDarkProperty, value);
    }

    /// <summary>
    /// The line under the wordmark. Defaults to the Figma tagline; each place
    /// that shows the lockup can say where the user is instead («فروش و صندوق»,
    /// «فروشگاه مرکزی · صندوق ۱»), which is more use to a cashier than a slogan.
    /// </summary>
    public string Caption
    {
        get => (string)GetValue(CaptionProperty);
        set => SetValue(CaptionProperty, value);
    }

    private static void OnLookChanged(DependencyObject sender, DependencyPropertyChangedEventArgs args) =>
        ((PishkarLogo)sender).ApplyLook();

    private void ApplyLook()
    {
        Tile.Width = Tile.Height = SymbolSize;
        Tile.CornerRadius = new CornerRadius(SymbolSize * 0.25); // Figma tile: 40 → 10
        TaglineText.Text = Caption;
        TaglineText.Visibility = ShowTagline && !string.IsNullOrWhiteSpace(Caption)
            ? Visibility.Visible
            : Visibility.Collapsed;
        NameText.FontSize = Math.Max(16, SymbolSize * 0.52);
        CategoryText.FontSize = Math.Max(10, SymbolSize * 0.30);
        TaglineText.FontSize = Math.Max(11, SymbolSize * 0.31);

        var text = OnDark
            ? new SolidColorBrush(Microsoft.UI.Colors.White)
            : (Brush)Microsoft.UI.Xaml.Application.Current.Resources["PishkarNavyBrush"];
        NameText.Foreground = text;
        TaglineText.Foreground = text;
        Tile.BorderBrush = OnDark ? new SolidColorBrush(Windows.UI.Color.FromArgb(0x33, 0xFF, 0xFF, 0xFF)) : null;
        Tile.BorderThickness = OnDark ? new Thickness(1) : new Thickness(0);
    }
}
