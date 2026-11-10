using AHGroceries.ViewModels;

namespace AHGroceries;

public partial class MainPage : ContentPage
{
    public MainPage()
    {
        InitializeComponent();
    }

    protected override void OnAppearing()
    {
        base.OnAppearing();
    }

    private void OnDragStarting(object? sender, DragStartingEventArgs e)
    {
        if (sender is BindableObject element && element.BindingContext is Models.GroceryItem item)
        {
            e.Data.Properties["GroceryItem"] = item;
        }
    }

    private async void OnDrop(object? sender, DropEventArgs e)
    {
        if (sender is not BindableObject element || element.BindingContext is not Models.GroceryItem target)
            return;

        if (e.Data.Properties.TryGetValue("GroceryItem", out var value) && value is Models.GroceryItem source && BindingContext is MainViewModel vm)
        {
            await vm.MoveItemAsync(source, target);
        }
    }
}