using Avalonia.Controls;
using Avalonia.Input;
using DiskChecker.UI.Avalonia.ViewModels;

namespace DiskChecker.UI.Avalonia.Views;

public partial class RestoreView : UserControl
{
    public RestoreView()
    {
        InitializeComponent();
    }

    private void OnBackupPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (sender is Control control && control.DataContext is DiscoveredBackup backup)
        {
            if (DataContext is RestoreViewModel vm)
            {
                vm.SelectedBackup = backup;
                vm.HasSelectedBackup = true;
                vm.SelectBackupCommand.Execute(null);
            }
        }
    }

    private void OnTargetPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (sender is Control control && control.DataContext is RestoreTargetItem target)
        {
            // RadioButton handles IsSelected binding automatically
            // Just ensure only one is selected
            if (DataContext is RestoreViewModel vm)
            {
                foreach (var t in vm.TargetDisks)
                    t.IsSelected = t == target;
            }
        }
    }
}
