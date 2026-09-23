using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ERP.Application.Inventory;
using ERP.Domain.Common;
using ERP.Domain.Inventory;

namespace ERP.Presentation.Features.Inventory;

/// <summary>One row of «لیست انبارها» as the screen writes it.</summary>
public sealed class WarehouseListItem
{
    public WarehouseListItem(WarehouseListRow row)
    {
        ArgumentNullException.ThrowIfNull(row);

        Row = row;
        Name = row.Name;
        AddressText = row.Address ?? string.Empty;
    }

    public WarehouseListRow Row { get; }

    public string Name { get; }

    public string AddressText { get; }

    /// <summary>What a screen reader says for the row (the row is not a control with its own text).</summary>
    public string AccessibleName => AddressText.Length == 0 ? Name : $"{Name}، {AddressText}";
}

/// <summary>
/// «لیست انبارها» (checklist «س»): every active warehouse with search, and a form for new/edit
/// (name + address — everything else in chapter 8's Warehouse/WMS design is Phase 2 and out of
/// scope here). «حذف» asks first and then archives — refused for the last active warehouse, one
/// that still has stock, or one with an open cash shift; the handler's own message explains which.
/// </summary>
public sealed partial class WarehouseListViewModel : ObservableObject
{
    private readonly IListWarehousesHandler _list;
    private readonly ICreateWarehouseHandler _create;
    private readonly IUpdateWarehouseHandler _update;
    private readonly IArchiveWarehouseHandler _archive;
    private CancellationTokenSource? _searchCancellation;
    private WarehouseListRow? _editing;

    public WarehouseListViewModel(
        IListWarehousesHandler list,
        ICreateWarehouseHandler create,
        IUpdateWarehouseHandler update,
        IArchiveWarehouseHandler archive)
    {
        _list = list;
        _create = create;
        _update = update;
        _archive = archive;
    }

    /// <summary>How long typing must pause before the list is re-read. Tests set it to zero.</summary>
    public TimeSpan SearchDelay { get; set; } = TimeSpan.FromMilliseconds(250);

    /// <summary>Where an unexpected failure is written (the app's log). Null only in tests, where it should surface.</summary>
    public Action<Exception>? ReportUnexpectedError { get; set; }

    public ObservableCollection<WarehouseListItem> Items { get; } = [];

    [ObservableProperty]
    public partial string SearchText { get; set; } = string.Empty;

    [ObservableProperty]
    public partial WarehouseListItem? SelectedItem { get; set; }

    [ObservableProperty]
    public partial bool IsLoading { get; set; }

    [ObservableProperty]
    public partial bool IsEmpty { get; set; }

    [ObservableProperty]
    public partial string CountText { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string? ErrorMessage { get; set; }

    [ObservableProperty]
    public partial string? Notice { get; set; }

    // ───── the form over the list ─────

    [ObservableProperty]
    public partial bool IsEditorOpen { get; set; }

    [ObservableProperty]
    public partial string EditorTitle { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string Name { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string Address { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string? NameError { get; set; }

    [ObservableProperty]
    public partial string? EditorError { get; set; }

    [ObservableProperty]
    public partial bool IsSaving { get; set; }

    // ───── «حذف» asks first ─────

    [ObservableProperty]
    public partial bool IsArchiveConfirmOpen { get; set; }

    [ObservableProperty]
    public partial string ArchiveConfirmText { get; set; } = string.Empty;

    public bool HasSelection => SelectedItem is not null;

    partial void OnSelectedItemChanged(WarehouseListItem? value)
    {
        OnPropertyChanged(nameof(HasSelection));
        EditCommand.NotifyCanExecuteChanged();
        ArchiveCommand.NotifyCanExecuteChanged();
    }

    partial void OnSearchTextChanged(string value) => _ = SearchAfterDelayAsync();

    /// <summary>First open.</summary>
    public async Task LoadAsync(CancellationToken cancellationToken)
    {
        try
        {
            await RefreshAsync(cancellationToken);
        }
        catch (Exception exception) when (ReportUnexpectedError is not null)
        {
            ReportUnexpectedError(exception);
            ErrorMessage = "لیست انبارها خوانده نشد و خطا برای پشتیبانی ثبت شد. دوباره امتحان کنید.";
        }
    }

    /// <summary>Re-reads the list for the current search text. Keeps the selection when that warehouse is still listed.</summary>
    public async Task RefreshAsync(CancellationToken cancellationToken)
    {
        var keep = SelectedItem?.Row.Id;
        IsLoading = true;
        ErrorMessage = null;
        try
        {
            var rows = await _list.ExecuteAsync(new WarehouseListQuery(SearchText), cancellationToken);

            Items.Clear();
            foreach (var row in rows)
            {
                Items.Add(new WarehouseListItem(row));
            }

            SelectedItem = Items.FirstOrDefault(item => item.Row.Id == keep);
            IsEmpty = Items.Count == 0;
            CountText = PersianNumber.FormatGrouped(Items.Count);
        }
        finally
        {
            IsLoading = false;
        }
    }

    private async Task SearchAfterDelayAsync()
    {
        _searchCancellation?.Cancel();
        var source = new CancellationTokenSource();
        _searchCancellation = source;

        try
        {
            if (SearchDelay > TimeSpan.Zero)
            {
                await Task.Delay(SearchDelay, source.Token);
            }

            await RefreshAsync(source.Token);
        }
        catch (OperationCanceledException)
        {
            // a newer keystroke replaced this search — nothing to do
        }
        catch (Exception exception) when (ReportUnexpectedError is not null)
        {
            ReportUnexpectedError(exception);
            ErrorMessage = "لیست خوانده نشد و خطا برای پشتیبانی ثبت شد.";
        }
    }

    // ───── new / edit ─────

    /// <summary>«جدید (F2)».</summary>
    [RelayCommand]
    private void New()
    {
        _editing = null;
        OpenEditor("انبار جدید", string.Empty, string.Empty);
    }

    /// <summary>«ویرایش (F3)» on the selected warehouse.</summary>
    [RelayCommand(CanExecute = nameof(HasSelection))]
    private void Edit()
    {
        if (SelectedItem is not { } item)
        {
            return;
        }

        _editing = item.Row;
        OpenEditor("ویرایش انبار", item.Row.Name, item.Row.Address ?? string.Empty);
    }

    private void OpenEditor(string title, string name, string address)
    {
        EditorTitle = title;
        Name = name;
        Address = address;
        NameError = null;
        EditorError = null;
        Notice = null;
        IsEditorOpen = true;
    }

    [RelayCommand]
    private void CancelEditor() => IsEditorOpen = false;

    [RelayCommand]
    private async Task SaveAsync()
    {
        NameError = null;
        EditorError = null;

        if (string.IsNullOrWhiteSpace(Name))
        {
            NameError = "نام انبار را وارد کنید.";
            return;
        }

        IsSaving = true;
        try
        {
            var name = Name.Trim();
            var address = string.IsNullOrWhiteSpace(Address) ? null : Address.Trim();

            if (_editing is { } editing)
            {
                var updated = await _update.ExecuteAsync(new UpdateWarehouseCommand(editing.Id, name, address), CancellationToken.None);
                if (!updated.IsSuccess)
                {
                    ShowSaveError(updated.Error?.Code, updated.Error?.Message);
                    return;
                }

                Notice = $"«{name}» ذخیره شد.";
            }
            else
            {
                var created = await _create.ExecuteAsync(new CreateWarehouseCommand(name, address), CancellationToken.None);
                if (!created.IsSuccess)
                {
                    ShowSaveError(created.Error?.Code, created.Error?.Message);
                    return;
                }

                Notice = $"«{name}» ساخته شد.";
            }

            IsEditorOpen = false;
            await RefreshAsync(CancellationToken.None);
        }
        catch (Exception exception) when (ReportUnexpectedError is not null)
        {
            ReportUnexpectedError(exception);
            EditorError = "ذخیره انجام نشد و خطا برای پشتیبانی ثبت شد. دوباره امتحان کنید.";
        }
        finally
        {
            IsSaving = false;
        }
    }

    private void ShowSaveError(string? code, string? message)
    {
        if (code == "inventory.warehouse.duplicate-name")
        {
            NameError = message;
        }
        else
        {
            EditorError = message;
        }
    }

    // ───── delete = archive ─────

    /// <summary>«حذف (F4)»: asks first. Nothing is archived until <see cref="ConfirmArchiveCommand"/>.</summary>
    [RelayCommand(CanExecute = nameof(HasSelection))]
    private void Archive()
    {
        if (SelectedItem is not { } item)
        {
            return;
        }

        ArchiveConfirmText = $"«{item.Name}» از لیست انبارها برداشته شود؟ این کار را نمی‌شود برگرداند مگر با ساخت دوباره.";
        IsArchiveConfirmOpen = true;
    }

    [RelayCommand]
    private void CancelArchive() => IsArchiveConfirmOpen = false;

    [RelayCommand]
    private async Task ConfirmArchiveAsync()
    {
        IsArchiveConfirmOpen = false;
        if (SelectedItem is not { } item)
        {
            return;
        }

        try
        {
            var result = await _archive.ExecuteAsync(new ArchiveWarehouseCommand(item.Row.Id), CancellationToken.None);
            if (result.IsSuccess)
            {
                Notice = $"«{item.Name}» حذف شد.";
                SelectedItem = null;
                await RefreshAsync(CancellationToken.None);
            }
            else
            {
                ErrorMessage = result.Error?.Message;
            }
        }
        catch (Exception exception) when (ReportUnexpectedError is not null)
        {
            ReportUnexpectedError(exception);
            ErrorMessage = "حذف انجام نشد و خطا برای پشتیبانی ثبت شد. دوباره امتحان کنید.";
        }
    }
}
