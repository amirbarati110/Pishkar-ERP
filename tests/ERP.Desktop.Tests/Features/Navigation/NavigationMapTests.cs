using ERP.Domain.Identity;
using ERP.Presentation.Features.Navigation;

namespace ERP.Desktop.Tests.Features.Navigation;

/// <summary>The menu structure is data; these keep it honest (checklist «م»).</summary>
public sealed class NavigationMapTests
{
    [Fact]
    public void EveryCardKeyIsUniqueAcrossTheWholeMenu()
    {
        var keys = NavigationMap.Sections.SelectMany(section => section.Cards).Select(card => card.Key).ToList();

        Assert.Equal(keys.Count, keys.Distinct().Count());
    }

    [Fact]
    public void EverySectionHasAtLeastOneCardSoNoEmptyPageIsEverShown()
    {
        Assert.All(NavigationMap.Sections, section => Assert.NotEmpty(section.Cards));
    }

    [Fact]
    public void SectionKeysAreUniqueAndNeverCollideWithACardKey()
    {
        var sectionKeys = NavigationMap.Sections.Select(section => section.Key).ToList();
        var cardKeys = NavigationMap.Sections.SelectMany(section => section.Cards).Select(card => card.Key);

        Assert.Equal(sectionKeys.Count, sectionKeys.Distinct().Count());
        Assert.Empty(sectionKeys.Intersect(cardKeys));
        Assert.DoesNotContain(NavigationMap.Home, sectionKeys);
    }

    [Theory]
    [InlineData(NavigationMap.NewSale, NavigationMap.SalesSection)]
    [InlineData(NavigationMap.Returns, NavigationMap.SalesSection)]
    [InlineData(NavigationMap.CashShift, NavigationMap.SalesSection)]
    [InlineData(NavigationMap.ProductList, NavigationMap.CatalogSection)]
    [InlineData(NavigationMap.Categories, NavigationMap.CatalogSection)]
    [InlineData(NavigationMap.Import, NavigationMap.CatalogSection)]
    [InlineData(NavigationMap.WarehouseList, NavigationMap.InventorySection)]
    [InlineData(NavigationMap.OpeningStock, NavigationMap.InventorySection)]
    [InlineData(NavigationMap.CustomerList, NavigationMap.PeopleSection)]
    [InlineData(NavigationMap.Backup, NavigationMap.SystemSection)]
    public void ACardBelongsToItsSection(string cardKey, string sectionKey)
    {
        Assert.Equal(sectionKey, NavigationMap.SectionOf(cardKey)?.Key);
    }

    [Fact]
    public void EveryCardHasATitleADescriptionAnIconAndAnAutomationId()
    {
        Assert.All(NavigationMap.Sections.SelectMany(section => section.Cards), card =>
        {
            Assert.False(string.IsNullOrWhiteSpace(card.Title));
            Assert.False(string.IsNullOrWhiteSpace(card.Description));
            Assert.False(string.IsNullOrWhiteSpace(card.Glyph));
            Assert.StartsWith("HubCard_", card.AutomationId, StringComparison.Ordinal);
        });
    }

    [Fact]
    public void UnknownKeysFindNothing()
    {
        Assert.Null(NavigationMap.FindCard("no-such-card"));
        Assert.Null(NavigationMap.FindSection("no-such-section"));
        Assert.Null(NavigationMap.SectionOf("no-such-card"));
    }

    /// <summary>User decision 1405/07/02: a manager-only card is not shown to whoever lacks its right.</summary>
    [Fact]
    public void AManagerOnlyCardIsHiddenFromACashierAndShownToAManager()
    {
        var system = NavigationMap.FindSection(NavigationMap.SystemSection)!;

        var forCashier = NavigationMap.VisibleCards(system, _ => false);
        var forManager = NavigationMap.VisibleCards(system, _ => true);

        Assert.DoesNotContain(forCashier, card => card.Key == NavigationMap.Users);
        Assert.DoesNotContain(forCashier, card => card.Key == NavigationMap.Backup);
        Assert.Contains(forManager, card => card.Key == NavigationMap.Users);
        Assert.Contains(forManager, card => card.Key == NavigationMap.Backup);
    }

    /// <summary>A card with no <see cref="HubCard.RequiredRight"/> is the till's own work — always shown.</summary>
    [Fact]
    public void ACardWithNoRequiredRightIsAlwaysShown()
    {
        var sales = NavigationMap.FindSection(NavigationMap.SalesSection)!;

        var forNoOne = NavigationMap.VisibleCards(sales, _ => false);

        Assert.Contains(forNoOne, card => card.Key == NavigationMap.NewSale);
        Assert.Contains(forNoOne, card => card.Key == NavigationMap.CashShift);
    }

    [Fact]
    public void UsersBelongsToTheSystemSectionAndNeedsManageUsers()
    {
        Assert.Equal(NavigationMap.SystemSection, NavigationMap.SectionOf(NavigationMap.Users)?.Key);
        Assert.Equal(AccessRight.ManageUsers, NavigationMap.FindCard(NavigationMap.Users)?.RequiredRight);
    }
}
