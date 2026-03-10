using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using DiskChecker.UI.Avalonia.ViewModels;

namespace DiskChecker.UI.Avalonia.Views;

public partial class DiskCardsView : UserControl
{
    public DiskCardsView()
    {
        InitializeComponent();
    }

    private void CardsDataGrid_DoubleTapped(object? sender, TappedEventArgs e)
    {
        if (DataContext is DiskCardsViewModel viewModel && 
            sender is DataGrid dataGrid &&
            dataGrid.SelectedItem is Core.Models.DiskCard selectedCard)
        {
            viewModel.ViewCardDetailsCommand.Execute(selectedCard);
        }
    }
}