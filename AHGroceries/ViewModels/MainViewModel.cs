using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using System.Collections.ObjectModel;
using AHGroceries.Models;
using AHGroceries.Messages;
using Supabase;

namespace AHGroceries.ViewModels;

public partial class MainViewModel : ObservableObject
{
    private const string SupabaseUrl = "https://luksliystpmrfijqjvfy.supabase.co";
    private const string SupabaseKey = "eyJhbGciOiJIUzI1NiIsInR5cCI6IkpXVCJ9.eyJpc3MiOiJzdXBhYmFzZSIsInJlZiI6Imx1a3NsaXlzdHBtcmZpanFqdmZ5Iiwicm9sZSI6ImFub24iLCJpYXQiOjE3OTM5MTgyOTIsImV4cCI6MjEwOTQ5NDI5Mn0.pxO6P0ce1IpFbrVpSAAYbwGeivVKMSWPgs9tj9q_xs4";

    private Supabase.Client _client = null!;
    private string _newItemName = string.Empty;
    private string _newItemQuantity = "1";

    public string NewItemName
    {
        get => _newItemName;
        set => SetProperty(ref _newItemName, value);
    }

    public string NewItemQuantity
    {
        get => _newItemQuantity;
        set => SetProperty(ref _newItemQuantity, value);
    }

    public ObservableCollection<GroceryItem> Items { get; } = new();

    private bool _isReordering;
    private readonly SemaphoreSlim _itemsMutationGate = new(1, 1);

    public MainViewModel()
    {
        WeakReferenceMessenger.Default.Register<GroceryItemChangedMessage>(
            this,
            static (recipient, message) =>
            {
                if (recipient is MainViewModel viewModel)
                {
                    _ = viewModel.ApplyIncomingItemAsync(message.Value);
                }
            });

        InitializeSupabase();
    }

    private async Task ApplyIncomingItemAsync(GroceryItem incoming)
    {
        await UpdateItemsAfterLayoutAsync(() =>
        {
            var existing = Items.FirstOrDefault(item => item.Id == incoming.Id);
            if (existing == null)
            {
                AttachItem(incoming);
                Items.Add(incoming);
                return;
            }

            existing.Name = incoming.Name;
            existing.Category = incoming.Category;
            existing.Quantity = incoming.Quantity;
            existing.IsPurchased = incoming.IsPurchased;
            existing.SortOrder = incoming.SortOrder;
        });
    }

    private async void InitializeSupabase()
    {
        try
        {
            var options = new SupabaseOptions { AutoConnectRealtime = false };
            _client = new Supabase.Client(SupabaseUrl, SupabaseKey, options);
            await _client.InitializeAsync();

            await LoadItemsAsync();
            await SubscribeToRealtimeAsync();
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Supabase connection error: {ex.Message}");
        }
    }

    public async Task LoadItemsAsync()
    {
        try
        {
            var response = await _client.From<GroceryItem>().Get();
            await UpdateItemsAfterLayoutAsync(() =>
            {
                Items.Clear();
                foreach (var item in response.Models.OrderBy(item => item.SortOrder).ThenBy(item => item.Name))
                {
                    AttachItem(item);
                    Items.Add(item);
                }
            });
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"LoadItems error: {ex.Message}");
        }
    }

    private void AttachItem(GroceryItem item)
    {
        item.PropertyChanged -= ItemPropertyChanged;
        item.PropertyChanged += ItemPropertyChanged;
    }

    private async void ItemPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (sender is GroceryItem item && e.PropertyName == nameof(GroceryItem.IsPurchased) && item.IsPurchased && !_isReordering)
        {
            await MoveItemToBottomAsync(item);
        }
    }

    private async Task MoveItemToBottomAsync(GroceryItem item)
    {
        if (_isReordering) return;

        _isReordering = true;
        try
        {
            var ordered = Items.Where(current => current != item && !current.IsPurchased)
                               .Concat(Items.Where(current => current != item && current.IsPurchased))
                               .Append(item)
                               .ToList();
            await ApplyOrderAsync(ordered);
        }
        finally
        {
            _isReordering = false;
        }
    }

    private async Task ApplyOrderAsync(IList<GroceryItem> ordered)
    {
        for (var index = 0; index < ordered.Count; index++)
        {
            ordered[index].SortOrder = index;
        }

        // CollectionView/RecyclerView cannot be modified while it is measuring,
        // laying out, or scrolling. BeginInvoke queues the reset until the
        // current Android layout pass has finished.
        await UpdateItemsAfterLayoutAsync(() =>
        {
            Items.Clear();
            foreach (var item in ordered)
            {
                Items.Add(item);
            }
        });

        foreach (var item in ordered)
        {
            await _client.From<GroceryItem>().Update(item);
        }
    }

    private async Task UpdateItemsAfterLayoutAsync(Action update)
    {
        await _itemsMutationGate.WaitAsync();
        try
        {
            var completion = new TaskCompletionSource<bool>(
                TaskCreationOptions.RunContinuationsAsynchronously);

            var dispatcher = Application.Current?.Dispatcher;
            if (dispatcher == null)
            {
                MainThread.BeginInvokeOnMainThread(() => ApplyUpdate(update, completion));
            }
            else if (!dispatcher.DispatchDelayed(
                         TimeSpan.FromMilliseconds(50),
                         () => ApplyUpdate(update, completion)))
            {
                completion.SetException(new InvalidOperationException(
                    "Unable to schedule the CollectionView update."));
            }

            await completion.Task;
        }
        finally
        {
            _itemsMutationGate.Release();
        }
    }

    private static void ApplyUpdate(
        Action update,
        TaskCompletionSource<bool> completion)
    {
        try
        {
            update();
            completion.SetResult(true);
        }
        catch (Exception ex)
        {
            completion.SetException(ex);
        }
    }

    public async Task MoveItemAsync(GroceryItem item, GroceryItem target)
    {
        if (item == null || target == null || item == target) return;

        var ordered = Items.ToList();
        var sourceIndex = ordered.IndexOf(item);
        var targetIndex = ordered.IndexOf(target);
        if (sourceIndex < 0 || targetIndex < 0) return;

        ordered.RemoveAt(sourceIndex);
        ordered.Insert(targetIndex, item);
        await ApplyOrderAsync(ordered);
    }

    private async Task SubscribeToRealtimeAsync()
    {
        try
        {
            await _client.Realtime.ConnectAsync();
            var channel = _client.Realtime.Channel("public:groceries");

            channel.AddPostgresChangeHandler(
                Supabase.Realtime.PostgresChanges.PostgresChangesOptions.ListenType.Inserts,
                (sender, change) =>
                {
                    var inserted = change.Model<GroceryItem>();
                    if (inserted == null) return;

                    MainThread.BeginInvokeOnMainThread(() =>
                    {
                        if (!Items.Any(i => i.Id == inserted.Id))
                        {
                            AttachItem(inserted);
                            _ = UpdateItemsAfterLayoutAsync(() => Items.Add(inserted));
                        }
                    });
                });

            channel.AddPostgresChangeHandler(
                Supabase.Realtime.PostgresChanges.PostgresChangesOptions.ListenType.Updates,
                (sender, change) =>
                {
                    var updated = change.Model<GroceryItem>();
                    if (updated == null) return;

                    MainThread.BeginInvokeOnMainThread(() =>
                    {
                        var existing = Items.FirstOrDefault(i => i.Id == updated.Id);
                        if (existing != null)
                        {
                            existing.IsPurchased = updated.IsPurchased;
                            existing.Quantity = updated.Quantity;
                            existing.SortOrder = updated.SortOrder;

                            var ordered = Items.OrderBy(item => item.SortOrder).ToList();
                            _ = UpdateItemsAfterLayoutAsync(() =>
                            {
                                Items.Clear();
                                foreach (var item in ordered)
                                {
                                    Items.Add(item);
                                }
                            });
                        }
                    });
                });

            channel.AddPostgresChangeHandler(
                Supabase.Realtime.PostgresChanges.PostgresChangesOptions.ListenType.Deletes,
                (sender, change) =>
                {
                    var deleted = change.Model<GroceryItem>();
                    if (deleted == null) return;

                    MainThread.BeginInvokeOnMainThread(() =>
                    {
                        var existing = Items.FirstOrDefault(i => i.Id == deleted.Id);
                        if (existing != null)
                        {
                             _ = UpdateItemsAfterLayoutAsync(() => Items.Remove(existing));
                        }
                    });
                });

            await channel.Subscribe();
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Realtime error: {ex.Message}");
        }
    }

    [RelayCommand]
    private async Task AddItem()
    {
        if (string.IsNullOrWhiteSpace(NewItemName)) return;

        string trimmedName = NewItemName.Trim();
        if (!int.TryParse(NewItemQuantity, out int qty) || qty < 1)
        {
            qty = 1;
        }

        // Reset input fields
        NewItemName = string.Empty;
        NewItemQuantity = "1";

        // Check if item already exists (case-insensitive)
        var existing = Items.FirstOrDefault(i => string.Equals(i.Name, trimmedName, StringComparison.OrdinalIgnoreCase));
        if (existing != null)
        {
            // Increment existing quantity instead of creating a duplicate
            existing.Quantity += qty;
            existing.IsPurchased = false; // Uncheck if re-adding
            await _client.From<GroceryItem>().Update(existing);
            return;
        }

        var newItem = new GroceryItem
        {
            Id = Guid.NewGuid().ToString(),
            Name = trimmedName,
            Quantity = qty,
            AddedBy = "Me",
            SortOrder = Items.Count
        };

        AttachItem(newItem);
        await UpdateItemsAfterLayoutAsync(() => Items.Add(newItem));

        try
        {
            await _client.From<GroceryItem>().Insert(newItem);
        }
        catch (Exception ex)
        {
            await UpdateItemsAfterLayoutAsync(() => Items.Remove(newItem));
            System.Diagnostics.Debug.WriteLine($"Insert error: {ex.Message}");
        }
    }

    [RelayCommand]
    private async Task MoveItemUp(GroceryItem item)
    {
        var index = Items.IndexOf(item);
        if (index <= 0) return;

        var ordered = Items.ToList();
        (ordered[index - 1], ordered[index]) = (ordered[index], ordered[index - 1]);
        await ApplyOrderAsync(ordered);
    }

    [RelayCommand]
    private async Task MoveItemDown(GroceryItem item)
    {
        var index = Items.IndexOf(item);
        if (index < 0 || index >= Items.Count - 1) return;

        var ordered = Items.ToList();
        (ordered[index], ordered[index + 1]) = (ordered[index + 1], ordered[index]);
        await ApplyOrderAsync(ordered);
    }

    [RelayCommand]
    private async Task RefreshList()
    {
        foreach (var item in Items.ToList())
        {
            await _client.From<GroceryItem>().Delete(item);
        }

        await UpdateItemsAfterLayoutAsync(Items.Clear);
    }

    [RelayCommand]
    private async Task IncreaseQuantity(GroceryItem item)
    {
        if (item == null) return;
        item.Quantity++;
        await _client.From<GroceryItem>().Update(item);
    }

    [RelayCommand]
    private async Task DecreaseQuantity(GroceryItem item)
    {
        if (item == null) return;
        if (item.Quantity > 1)
        {
            item.Quantity--;
            await _client.From<GroceryItem>().Update(item);
        }
        else
        {
            await DeleteItem(item);
        }
    }

    [RelayCommand]
    private async Task ToggleItem(GroceryItem item)
    {
        if (item == null) return;
        item.IsPurchased = !item.IsPurchased;
        await _client.From<GroceryItem>().Update(item);
    }

    [RelayCommand]
    private async Task DeleteItem(GroceryItem item)
    {
        if (item == null) return;
        await UpdateItemsAfterLayoutAsync(() => Items.Remove(item));
        await _client.From<GroceryItem>().Delete(item);
    }
}