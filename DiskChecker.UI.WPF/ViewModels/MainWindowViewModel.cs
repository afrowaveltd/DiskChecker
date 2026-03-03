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

    [ObservableProperty]
    private bool isDiskSelectionActive;

    [ObservableProperty]
    private bool isSurfaceTestActive;

    [ObservableProperty]
    private bool isSmartCheckActive;

    [ObservableProperty]
    private bool isAnalysisActive;

    [ObservableProperty]
    private bool isReportActive;

    [ObservableProperty]
    private bool isHistoryActive;

    [ObservableProperty]
    private bool isSettingsActive;

    [ObservableProperty]
    private string statusMessage = string.Empty;

    /// <summary>
    /// Initializes a new instance of the <see cref="MainWindowViewModel"/> class.
    /// </summary>
    public MainWindowViewModel(INavigationService navigationService)
    {
        _navigationService = navigationService;
        _navigationService.ViewChanged += OnViewChanged;

        // Iniciální navigace na UI threadu s malým zpožděním
        _ = System.Windows.Application.Current.Dispatcher.InvokeAsync(async () =>
        {
            await Task.Delay(100); // Počkej aby se MainWindow zobrazila
            NavigateToDiskSelection();
        });
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
    /// Navigates to analysis view.
    /// </summary>
    [RelayCommand]
    public void NavigateToAnalysis()
    {
        _navigationService.NavigateTo<AnalysisViewModel>();
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
        // TODO: Implementovat ReportViewModel
        StatusMessage = "⚠️ Report view zatím není implementován";
        // _navigationService.NavigateTo<ReportViewModel>();
    }

    /// <summary>
    /// Navigates to history view.
    /// </summary>
    [RelayCommand]
    public void NavigateToHistory()
    {
        // TODO: Implementovat HistoryViewModel
        StatusMessage = "⚠️ Historie view zatím není implementována";
        // _navigationService.NavigateTo<HistoryViewModel>();
    }

    /// <summary>
    /// Navigates to settings view.
    /// </summary>
    [RelayCommand]
    public void NavigateToSettings()
    {
        // TODO: Implementovat SettingsViewModel
        StatusMessage = "⚠️ Nastavení view zatím není implementováno";
        // _navigationService.NavigateTo<SettingsViewModel>();
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

        IsDiskSelectionActive = e.ViewModel is DiskSelectionViewModel;
        IsSurfaceTestActive = e.ViewModel is SurfaceTestViewModel;
        IsSmartCheckActive = e.ViewModel is SmartCheckViewModel;
        IsAnalysisActive = e.ViewModel is AnalysisViewModel;
        // TODO: Až budou implementované, odkomentovat
        IsReportActive = false; // e.ViewModel is ReportViewModel;
        IsHistoryActive = false; // e.ViewModel is HistoryViewModel;
        IsSettingsActive = false; // e.ViewModel is SettingsViewModel;
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
