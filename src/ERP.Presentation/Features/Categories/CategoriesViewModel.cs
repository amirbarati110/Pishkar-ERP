using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ERP.Application.Catalog;

namespace ERP.Presentation.Features.Categories;

public partial class CategoriesViewModel : ObservableObject
{
    private readonly ICreateCategoryHandler _handler;
    private readonly ICatalogLookupReader _catalogLookup;

    public CategoriesViewModel(
        ICreateCategoryHandler handler,
        ICatalogLookupReader catalogLookup)
    {
        _handler = handler;
        _catalogLookup = catalogLookup;
    }

    public ObservableCollection<CategoryLookupItem> Categories { get; } = [];

    [ObservableProperty]
    public partial string Name { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string? NameError { get; set; }

    [ObservableProperty]
    public partial string? StatusMessage { get; set; }

    [ObservableProperty]
    public partial bool IsBusy { get; set; }

    public async Task LoadAsync(CancellationToken cancellationToken)
    {
        var snapshot = await _catalogLookup.LoadAsync(cancellationToken);
        ReplaceWith(Categories, snapshot.Categories);
    }

    [RelayCommand]
    private async Task SaveAsync()
    {
        NameError = null;
        StatusMessage = null;

        if (string.IsNullOrWhiteSpace(Name))
        {
            NameError = "نام دسته‌بندی را وارد کنید.";
            return;
        }

        IsBusy = true;

        try
        {
            var result = await _handler.ExecuteAsync(
                new CreateCategoryCommand(Name, null, 0),
                CancellationToken.None);

            if (result.IsSuccess)
            {
                Name = string.Empty;
                StatusMessage = "دسته‌بندی با موفقیت ساخته شد.";
                await LoadAsync(CancellationToken.None);
            }
            else
            {
                NameError = result.Error?.Message;
            }
        }
        finally
        {
            IsBusy = false;
        }
    }

    private static void ReplaceWith<T>(
        ObservableCollection<T> target,
        IEnumerable<T> source)
    {
        target.Clear();
        foreach (var item in source)
        {
            target.Add(item);
        }
    }
}
