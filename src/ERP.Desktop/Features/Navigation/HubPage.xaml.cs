using ERP.Presentation.Features.Navigation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;

namespace ERP.Desktop.Features.Navigation;

/// <summary>
/// The cards of one menu section. The page owns no rules: <see cref="NavigationMap"/>
/// says what the cards are and the shell says where each one goes.
/// </summary>
public sealed partial class HubPage : Page
{
    public HubPage()
    {
        InitializeComponent();
    }

    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);

        if (e.Parameter is string sectionKey && NavigationMap.FindSection(sectionKey) is { } section)
        {
            SectionTitle.Text = section.Title;
            SectionSubtitle.Text = section.Subtitle;
            Cards.ItemsSource = NavigationMap.VisibleCards(section, App.CurrentUser.Can);
        }
    }

    private void OnCardClick(object sender, ItemClickEventArgs e)
    {
        if (e.ClickedItem is HubCard card)
        {
            MainPage.ShellOf(this)?.OpenCard(card.Key);
        }
    }
}
