using Avalonia.Controls;
using Avalonia;
using DiskChecker.UI.Avalonia.Services;
using DiskChecker.UI.Avalonia.ViewModels;
using Microsoft.Extensions.DependencyInjection;

// Alias to avoid namespace conflicts
using AvaloniaApp = Avalonia.Application;

namespace DiskChecker.UI.Avalonia.Views;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
        
        // Register views with the navigation service
        if (DataContext is MainWindowViewModel viewModel)
        {
            var serviceProvider = App.GetService<IServiceProvider>();
            var navigationService = serviceProvider?.GetService<INavigationService>();
            if (navigationService != null)
            {
                navigationService.RegisterViewForViewModel<DiskSelectionViewModel, DiskSelectionView>();
                navigationService.RegisterViewForViewModel<SurfaceTestViewModel, SurfaceTestView>();
                navigationService.RegisterViewForViewModel<SmartCheckViewModel, SmartCheckView>();
                navigationService.RegisterViewForViewModel<AnalysisViewModel, AnalysisView>();
                navigationService.RegisterViewForViewModel<ReportViewModel, ReportView>();
                navigationService.RegisterViewForViewModel<HistoryViewModel, HistoryView>();
                navigationService.RegisterViewForViewModel<SettingsViewModel, SettingsView>();
                
                // Navigate to initial view
                navigationService.NavigateTo<DiskSelectionViewModel>();
            }
        }
    }
}
