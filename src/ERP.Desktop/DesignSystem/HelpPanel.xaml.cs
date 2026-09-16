using ERP.Presentation.Help;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;

namespace ERP.Desktop.DesignSystem;

/// <summary>
/// The one place «راهنمای این صفحه» is drawn (§4.1/§3.14). A host page sets
/// <see cref="Workflow"/> when F1 is pressed and clears it (directly, or by
/// handling <see cref="CloseRequested"/>) to dismiss — the panel owns no
/// open/closed state of its own beyond what that property says.
/// </summary>
public sealed partial class HelpPanel : UserControl
{
    public static readonly DependencyProperty WorkflowProperty = DependencyProperty.Register(
        nameof(Workflow), typeof(PageWorkflow), typeof(HelpPanel), new PropertyMetadata(null, OnWorkflowChanged));

    public HelpPanel()
    {
        InitializeComponent();
    }

    public PageWorkflow? Workflow
    {
        get => (PageWorkflow?)GetValue(WorkflowProperty);
        set => SetValue(WorkflowProperty, value);
    }

    /// <summary>Raised when the cashier asks to close it — the ✕ button, clicking outside the card, or Esc/F1 bubbling up from the overlay.</summary>
    public event EventHandler? CloseRequested;

    /// <summary>x:Bind helper: Workflow is the DependencyProperty that can actually be tracked OneWay; a plain get-only bool property on this class cannot.</summary>
    public static Visibility WhenPresent(PageWorkflow? workflow) =>
        workflow is null ? Visibility.Collapsed : Visibility.Visible;

    private static void OnWorkflowChanged(DependencyObject sender, DependencyPropertyChangedEventArgs args) =>
        ((HelpPanel)sender).Apply();

    private void Apply()
    {
        if (Workflow is not { } workflow)
        {
            return;
        }

        TitleText.Text = workflow.TitleFa;
        StepsList.ItemsSource = workflow.Steps;
    }

    private void OnCloseClick(object sender, RoutedEventArgs e) => CloseRequested?.Invoke(this, EventArgs.Empty);

    private void OnOverlayTapped(object sender, TappedRoutedEventArgs e) => CloseRequested?.Invoke(this, EventArgs.Empty);

    /// <summary>Stops a tap on the card itself from bubbling to the overlay and closing the panel it was meant to keep open.</summary>
    private void OnCardTapped(object sender, TappedRoutedEventArgs e) => e.Handled = true;
}
