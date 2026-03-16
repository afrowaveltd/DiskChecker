using CommunityToolkit.Mvvm.ComponentModel;
using DiskChecker.Core.Models;

namespace DiskChecker.UI.Avalonia.ViewModels;

public class DiskComparisonItem : ObservableObject
{
    private bool _isSelected;

    public DiskCard Disk { get; set; } = null!;

    public bool IsSelected
    {
        get => _isSelected;
        set => SetProperty(ref _isSelected, value);
    }
}