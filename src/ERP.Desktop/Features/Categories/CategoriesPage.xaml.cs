using ERP.Presentation.Features.Categories;
using Microsoft.UI.Xaml.Controls;

namespace ERP.Desktop.Features.Categories;

public sealed partial class CategoriesPage : Page
{
    public CategoriesPage()
    {
        ViewModel = new CategoriesViewModel(
            App.Services.RetailSetup,
            App.Services.CatalogLookup);
        InitializeComponent();
        Loaded += OnLoaded;
    }

    public CategoriesViewModel ViewModel { get; }

    private async void OnLoaded(object sender, Microsoft.UI.Xaml.RoutedEventArgs e)
    {
        await ViewModel.LoadAsync(CancellationToken.None);
        RebuildCategoryTree();
    }

    private void RebuildCategoryTree()
    {
        CategoryTree.RootNodes.Clear();
        var nodes = ViewModel.Categories.ToDictionary(
            category => category.Id,
            category => new TreeViewNode
            {
                Content = category.Name,
                IsExpanded = true,
            });

        foreach (var category in ViewModel.Categories)
        {
            var node = nodes[category.Id];
            if (category.ParentId is { } parentId && nodes.TryGetValue(parentId, out var parent))
            {
                parent.Children.Add(node);
            }
            else
            {
                CategoryTree.RootNodes.Add(node);
            }
        }
    }
}
