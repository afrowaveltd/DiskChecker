using System.Windows.Controls;
using System.Windows.Input;
using DiskChecker.UI.WPF.ViewModels;

namespace DiskChecker.UI.WPF.Views;

/// <summary>
/// Interaction logic for DiskSelectionView.xaml
/// </summary>
public partial class DiskSelectionView : UserControl
{
    /// <summary>
    /// Initializes a new instance of the <see cref="DiskSelectionView"/> class.
    /// </summary>
    public DiskSelectionView()
    {
        InitializeComponent();
    }

    /// <summary>
    /// Obslouží event kliknutí na diskovou kartu pro jej výběr.
    /// </summary>
    private void SelectDisk_Click(object sender, MouseButtonEventArgs e)
    {
        if(sender is Grid grid && grid.DataContext is DiskStatusCardItem card)
        {
            if(DataContext is DiskSelectionViewModel viewModel)
            {
                viewModel.SelectedDiskCard = card;
            }
        }
    }
}
