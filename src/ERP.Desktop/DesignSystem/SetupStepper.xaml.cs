using Microsoft.UI.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

namespace ERP.Desktop.DesignSystem;

/// <summary>
/// «مرحله N از ۳». Each step before <see cref="CurrentStep"/> shows a check
/// mark on a soft green badge, the current step shows its number on a solid
/// green badge, and steps after it are dim — the same three states each of
/// the three setup pages drew by hand before.
/// </summary>
public sealed partial class SetupStepper : UserControl
{
    public static readonly DependencyProperty CurrentStepProperty = DependencyProperty.Register(
        nameof(CurrentStep), typeof(int), typeof(SetupStepper), new PropertyMetadata(1, OnCurrentStepChanged));

    public SetupStepper()
    {
        InitializeComponent();
        Apply();
    }

    /// <summary>1, 2 or 3 — «دسته‌بندی», «اطلاعات کالا», «موجودی».</summary>
    public int CurrentStep
    {
        get => (int)GetValue(CurrentStepProperty);
        set => SetValue(CurrentStepProperty, value);
    }

    private static void OnCurrentStepChanged(DependencyObject sender, DependencyPropertyChangedEventArgs args) =>
        ((SetupStepper)sender).Apply();

    private void Apply()
    {
        SetStep(1, Step1Badge, Step1Mark, Step1Label);
        SetStep(2, Step2Badge, Step2Mark, Step2Label);
        SetStep(3, Step3Badge, Step3Mark, Step3Label);
    }

    private void SetStep(int step, Border badge, TextBlock mark, TextBlock label)
    {
        var resources = Microsoft.UI.Xaml.Application.Current.Resources;
        var done = step < CurrentStep;
        var current = step == CurrentStep;

        badge.Background = (Brush)resources[done ? "AppPrimarySoftBrush" : current ? "AppPrimaryBrush" : "AppSurfaceMutedBrush"];
        mark.Text = done ? "✓" : PersianDigit(step);
        mark.Foreground = (Brush)resources[done ? "AppPrimaryBrush" : current ? "AppOnPrimaryBrush" : "AppTextMutedBrush"];
        label.Foreground = (Brush)resources[current ? "AppTextBrush" : "AppTextMutedBrush"];
        label.FontWeight = current ? FontWeights.SemiBold : FontWeights.Normal;

        static string PersianDigit(int value) => value switch
        {
            1 => "۱",
            2 => "۲",
            3 => "۳",
            _ => value.ToString(System.Globalization.CultureInfo.InvariantCulture),
        };
    }
}
