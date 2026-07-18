using CommunityToolkit.Mvvm.ComponentModel;
using DiskChecker.Core.Models;

namespace DiskChecker.UI.Avalonia.ViewModels;

/// <summary>
/// Wrapper pro DiskCard s IsSelected pro výběr v UI.
/// </summary>
public partial class SelectableDiskCard : ObservableObject
{
    private readonly DiskCard _card;

    public SelectableDiskCard(DiskCard card)
    {
        _card = card;
    }

    public int Id => _card.Id;
    public string ModelName => _card.ModelName;
    public string SerialNumber => _card.SerialNumber;
    public string CapacityText => _card.CapacityText;
    public string Manufacturer => _card.Manufacturer;

    [ObservableProperty] private bool _isSelected;
}
