using CommunityToolkit.Mvvm.ComponentModel;
using Supabase.Postgrest.Attributes;
using Supabase.Postgrest.Models;
using System.ComponentModel;

namespace AHGroceries.Models;

[Table("groceries")]
public partial class GroceryItem : BaseModel, INotifyPropertyChanged
{
    [PrimaryKey("id", shouldInsert: true)]
    [Column("id")]
    public string Id { get; set; } = Guid.NewGuid().ToString();

    [Column("name")]
    public string Name { get; set; } = string.Empty;

    [Column("category")]
    public string Category { get; set; } = "General";

    private int _quantity = 1;
    [Column("quantity")]
    public int Quantity
    {
        get => _quantity;
        set
        {
            _quantity = value;
            OnPropertyChanged(nameof(Quantity));
        }
    }

    private bool _isPurchased;
    [Column("is_purchased")]
    public bool IsPurchased
    {
        get => _isPurchased;
        set
        {
            _isPurchased = value;
            OnPropertyChanged(nameof(IsPurchased));
        }
    }

    [Column("added_by")]
    public string AddedBy { get; set; } = "Me";

    private int _sortOrder;
    [Column("sort_order")]
    public int SortOrder
    {
        get => _sortOrder;
        set
        {
            if (_sortOrder == value) return;
            _sortOrder = value;
            OnPropertyChanged(nameof(SortOrder));
        }
    }

    public event System.ComponentModel.PropertyChangedEventHandler? PropertyChanged;
    protected void OnPropertyChanged(string propertyName) =>
        PropertyChanged?.Invoke(this, new System.ComponentModel.PropertyChangedEventArgs(propertyName));
}