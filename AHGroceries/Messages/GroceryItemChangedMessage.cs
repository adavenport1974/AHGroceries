using AHGroceries.Models;
using CommunityToolkit.Mvvm.Messaging.Messages;

namespace AHGroceries.Messages;

public sealed class GroceryItemChangedMessage : ValueChangedMessage<GroceryItem>
{
    public GroceryItemChangedMessage(GroceryItem item)
        : base(item)
    {
    }
}
