using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using System.Collections.ObjectModel;
using AHGroceries.Models;
using AHGroceries.Messages;
using Supabase;

namespace AHGroceries.ViewModels;

public partial class UsualViewModel : ObservableObject
{
    private const string SupabaseUrl = "https://luksliystpmrfijqjvfy.supabase.co";
    // Keep your same Supabase anon key here
    private const string SupabaseKey = "eyJhbGciOiJIUzI1NiIsInR5cCI6IkpXVCJ9.eyJpc3MiOiJzdXBhYmFzZSIsInJlZiI6Imx1a3NsaXlzdHBtcmZpanFqdmZ5Iiwicm9sZSI6ImFub24iLCJpYXQiOjE3OTM5MTgyOTIsImV4cCI6MjEwOTQ5NDI5Mn0.pxO6P0ce1IpFbrVpSAAYbwGeivVKMSWPgs9tj9q_xs4";

    private Supabase.Client _client = null!;
    private string _newFavoriteName = string.Empty;
    private readonly ObservableCollection<UsualItem> _hiddenItems = new();
    private readonly SemaphoreSlim _itemsMutationGate = new(1, 1);

    public string NewFavoriteName
    {
        get => _newFavoriteName;
        set => SetProperty(ref _newFavoriteName, value);
    }

    public ObservableCollection<UsualItem> UsualItems { get; } = new();

    public UsualViewModel()
    {
        InitializeAsync();
    }

    private async void InitializeAsync()
    {
        try
        {
            var options = new SupabaseOptions { AutoConnectRealtime = false };
            _client = new Supabase.Client(SupabaseUrl, SupabaseKey, options);
            await _client.InitializeAsync();
            await LoadFavoritesAsync();
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Error: {ex.Message}");
        }
    }

    private async Task LoadFavoritesAsync()
    {
        var response = await _client.From<UsualItem>().Get();
        await UpdateItemsAfterLayoutAsync(() =>
        {
            UsualItems.Clear();
            _hiddenItems.Clear();

            foreach (var item in response.Models.OrderBy(item => item.SortOrder).ThenBy(item => item.Name))
            {
                if (item.IsHidden)
                {
                    _hiddenItems.Add(item);
                }
                else
                {
                    UsualItems.Add(item);
                }
            }
        });
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
                    "Unable to schedule the Usual Items update."));
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

    // Adds a new frequent staple to this list
    // Adds a new frequent staple to this list
    [RelayCommand]
    private async Task AddFavorite()
    {
        if (string.IsNullOrWhiteSpace(NewFavoriteName)) return;

        string trimmedName = NewFavoriteName.Trim();

        // Prevent duplicate staples in Usual Items
        if (UsualItems.Concat(_hiddenItems)
                      .Any(u => string.Equals(u.Name, trimmedName, StringComparison.OrdinalIgnoreCase)))
        {
            MainThread.BeginInvokeOnMainThread(async () =>
            {
                if (Shell.Current != null)
                {
                    await Shell.Current.DisplayAlert("Already Exists", $"'{trimmedName}' is already in your usual items.", "OK");
                }
            });
            return;
        }

        var item = new UsualItem
        {
            Name = trimmedName,
            SortOrder = UsualItems.Count + _hiddenItems.Count == 0
                ? 0
                : UsualItems.Concat(_hiddenItems).Max(existing => existing.SortOrder) + 1
        };
        NewFavoriteName = string.Empty;
        await UpdateItemsAfterLayoutAsync(() => UsualItems.Add(item));

        try
        {
            await _client.From<UsualItem>().Insert(item);
        }

        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Error saving favorite: {ex.Message}");
        }
    }

    [RelayCommand]
    private async Task SkipFavorite(UsualItem item)
    {
        if (item == null || !UsualItems.Contains(item)) return;

        await UpdateItemsAfterLayoutAsync(() =>
        {
            UsualItems.Remove(item);
            UsualItems.Add(item);
        });
        await PersistVisibleOrderAsync();
    }

    [RelayCommand]
    private async Task HideFavorite(UsualItem item)
    {
        if (item == null || !UsualItems.Contains(item)) return;

        item.IsHidden = true;
        await UpdateItemsAfterLayoutAsync(() =>
        {
            UsualItems.Remove(item);
            _hiddenItems.Add(item);
        });

        await _client.From<UsualItem>().Update(item);
    }

    [RelayCommand]
    private async Task ResetHidden()
    {
        if (_hiddenItems.Count == 0) return;

        var hidden = _hiddenItems
            .OrderBy(item => item.SortOrder)
            .ToList();

        await UpdateItemsAfterLayoutAsync(() =>
        {
            foreach (var item in hidden)
            {
                item.IsHidden = false;
                UsualItems.Add(item);
            }

            _hiddenItems.Clear();
        });

        await PersistVisibleOrderAsync();
    }

    private async Task PersistVisibleOrderAsync()
    {
        for (var index = 0; index < UsualItems.Count; index++)
        {
            var item = UsualItems[index];
            item.SortOrder = index;
            await _client.From<UsualItem>().Update(item);
        }
    }

    [RelayCommand]
    private async Task PushToList(UsualItem favorite)
    {
        if (favorite == null) return;

        try
        {
            // Check if item is already in groceries
            var response = await _client.From<GroceryItem>()
                                        .Filter("name", Supabase.Postgrest.Constants.Operator.ILike, favorite.Name)
                                        .Get();

            var existing = response.Models.FirstOrDefault();

            if (existing != null)
            {
                // Increment existing item count
                existing.Quantity += 1;
                existing.IsPurchased = false;
                await _client.From<GroceryItem>().Update(existing);
                WeakReferenceMessenger.Default.Send(new GroceryItemChangedMessage(existing));

                MainThread.BeginInvokeOnMainThread(async () =>
                {
                    if (Shell.Current != null)
                    {
                        await Shell.Current.DisplayAlert(
                            "Updated!",
                            $"Added another '{favorite.Name}' (now {existing.Quantity}).",
                            "OK");
                    }
                });
            }
            else
            {
                // Insert brand new row
                var grocery = new GroceryItem
                {
                    Id = Guid.NewGuid().ToString(),
                    Name = favorite.Name,
                    Category = favorite.Category,
                    Quantity = 1,
                    IsPurchased = false,
                    AddedBy = "Favorites"
                };

                await _client.From<GroceryItem>().Insert(grocery);
                WeakReferenceMessenger.Default.Send(new GroceryItemChangedMessage(grocery));

                MainThread.BeginInvokeOnMainThread(async () =>
                {
                    if (Shell.Current != null)
                    {
                        await Shell.Current.DisplayAlert(
                            "Added!",
                            $"'{favorite.Name}' was added to your grocery list.",
                            "OK");
                    }
                });
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Error adding to list: {ex}");
            MainThread.BeginInvokeOnMainThread(async () =>
            {
                if (Shell.Current != null)
                {
                    await Shell.Current.DisplayAlert("Error", ex.Message, "OK");
                }
            });
        }
    }

    // Delete a staple
    [RelayCommand]
    private async Task DeleteFavorite(UsualItem item)
    {
        if (item == null) return;
        await UpdateItemsAfterLayoutAsync(() => UsualItems.Remove(item));
        await _client.From<UsualItem>().Delete(item);
    }
}