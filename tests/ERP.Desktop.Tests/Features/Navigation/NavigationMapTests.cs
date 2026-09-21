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
    [InlineData(NavigationMap.OpeningStock, NavigationMap.InventorySection)]
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
}
