using System;
using System.Linq;
using Avalonia.Controls;
using DiskChecker.Core.Models;
using DiskChecker.UI.Avalonia.ViewModels;

namespace DiskChecker.UI.Avalonia.Views;

public partial class DiskCardDetailView : UserControl
{
    public DiskCardDetailView()
    {
        InitializeComponent();
    }

    /// <summary>
    /// DataGrid.SelectedItem only holds ONE row even when several rows are
    /// blue-highlighted. Forward the entire selected set to the view model,
    /// without altering its SelectedSession diagnostic-detail binding.
    /// </summary>
    private void HistoryGrid_SelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (sender is DataGrid grid && DataContext is DiskCardDetailViewModel viewModel)
            viewModel.SetSelectedHistorySessions(grid.SelectedItems.OfType<TestSession>());
    }
}
