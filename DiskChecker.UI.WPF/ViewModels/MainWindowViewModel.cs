using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DiskChecker.UI.WPF.Services;

namespace DiskChecker.UI.WPF.ViewModels;

/// <summary>
/// ViewModel pro hlavní okno aplikace.
/// Spravuje navigaci a globální stav.
/// </summary>
public partial class MainWindowViewModel : ViewModelBase
{
    private readonly INavigationService _navigationService;
    
    [ObservableProperty]
    private object? currentContent;

    [ObservableProperty]
    private ViewModelBase? currentViewModel;

    [ObservableProperty]
    private string title = "DiskChecker - Diagnóza Disků 🖴";

    /// <summary>
    /// Initializes a new instance of the <see cref="MainWindowViewModel"/> class.
    /// </summary>
    public MainWindowViewModel(INavigationService navigationService)
    {
        _navigationService = navigationService;
        _navigationService.ViewChanged += OnViewChanged;

        // Iniciální navigace
        NavigateToDiskSelection();
    }

    /// <summary>
    /// Navigates to disk selection view.
    /// </summary>
    [RelayCommand]
    public void NavigateToDiskSelection()
    {
        _navigationService.NavigateTo<DiskSelectionViewModel>();
    }

    /// <summary>
    /// Navigates to SMART check view.
    /// </summary>
    [RelayCommand]
    public void NavigateToSmartCheck()
    {
        _navigationService.NavigateTo<SmartCheckViewModel>();
    }

    /// <summary>
    /// Navigates to surface test view.
    /// </summary>
    [RelayCommand]
    public void NavigateToSurfaceTest()
    {
        _navigationService.NavigateTo<SurfaceTestViewModel>();
    }

    /// <summary>
    /// Navigates to report view.
    /// </summary>
    [RelayCommand]
    public void NavigateToReport()
    {
        _navigationService.NavigateTo<ReportViewModel>();
    }

    /// <summary>
    /// Navigates to history view.
    /// </summary>
    [RelayCommand]
    public void NavigateToHistory()
    {
        _navigationService.NavigateTo<HistoryViewModel>();
    }

    /// <summary>
    /// Navigates to settings view.
    /// </summary>
    [RelayCommand]
    public void NavigateToSettings()
    {
        _navigationService.NavigateTo<SettingsViewModel>();
    }

    /// <summary>
    /// Goes back to previous view.
    /// </summary>
    [RelayCommand]
    public void GoBack()
    {
        _navigationService.GoBack();
    }

    /// <summary>
    /// Handles view changed event.
    /// </summary>
    private void OnViewChanged(object? sender, ViewChangedEventArgs e)
    {
        CurrentContent = e.View;
        CurrentViewModel = e.ViewModel as ViewModelBase;
    }

    /// <summary>
    /// Cleans up resources.
    /// </summary>
    public override void Cleanup()
    {
        _navigationService.ViewChanged -= OnViewChanged;
        base.Cleanup();
    }
}
