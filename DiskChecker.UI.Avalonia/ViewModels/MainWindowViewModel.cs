using System.ComponentModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DiskChecker.UI.Avalonia.Services.Interfaces;

namespace DiskChecker.UI.Avalonia.ViewModels;

public partial class MainWindowViewModel : ViewModelBase
{
    private readonly INavigationService _navigationService;
    private ViewModelBase? _currentContent;
    private string _statusMessage = "Připraven";
    private Type? _currentViewModelType;

    public MainWindowViewModel(INavigationService navigationService)
    {
        _navigationService = navigationService;
        _navigationService.Navigated += OnNavigated;
        
        // Navigate to initial view
        _navigationService.NavigateTo<DiskSelectionViewModel>();
    }

    public ViewModelBase? CurrentContent
    {
        get => _currentContent;
        set => SetProperty(ref _currentContent, value);
    }

    public string StatusMessage
    {
        get => _statusMessage;
        set => SetProperty(ref _statusMessage, value);
    }

    // Properties for active navigation button tracking
    public bool IsOnDiskSelection => _currentViewModelType == typeof(DiskSelectionViewModel);
    public bool IsOnDiskCards => _currentViewModelType == typeof(DiskCardsViewModel);
    public bool IsOnSurfaceTest => _currentViewModelType == typeof(SurfaceTestViewModel);
    public bool IsOnSmartCheck => _currentViewModelType == typeof(SmartCheckViewModel);
    public bool IsOnAnalysis => _currentViewModelType == typeof(AnalysisViewModel);
    public bool IsOnDiskComparison => _currentViewModelType == typeof(DiskComparisonViewModel);
    public bool IsOnReport => _currentViewModelType == typeof(ReportViewModel);
    public bool IsOnHistory => _currentViewModelType == typeof(HistoryViewModel);
    public bool IsOnSettings => _currentViewModelType == typeof(SettingsViewModel);

    [RelayCommand]
    private void NavigateToDiskSelection()
    {
        _navigationService.NavigateTo<DiskSelectionViewModel>();
        StatusMessage = "Naviguji na výběr disků...";
    }

    [RelayCommand]
    private void NavigateToDiskCards()
    {
        _navigationService.NavigateTo<DiskCardsViewModel>();
        StatusMessage = "Naviguji na karty disků...";
    }

    [RelayCommand]
    private void NavigateToSurfaceTest()
    {
        _navigationService.NavigateTo<SurfaceTestViewModel>();
        StatusMessage = "Naviguji na test povrchu...";
    }

    [RelayCommand]
    private void NavigateToSmartCheck()
    {
        _navigationService.NavigateTo<SmartCheckViewModel>();
        StatusMessage = "Naviguji na SMART kontrolu...";
    }

    [RelayCommand]
    private void NavigateToAnalysis()
    {
        _navigationService.NavigateTo<AnalysisViewModel>();
        StatusMessage = "Naviguji na analýzu...";
    }

    [RelayCommand]
    private void NavigateToDiskComparison()
    {
        _navigationService.NavigateTo<DiskComparisonViewModel>();
        StatusMessage = "Naviguji na porovnání disků...";
    }

    [RelayCommand]
    private void NavigateToReport()
    {
        _navigationService.NavigateTo<ReportViewModel>();
        StatusMessage = "Naviguji na report...";
    }

    [RelayCommand]
    private void NavigateToHistory()
    {
        _navigationService.NavigateTo<HistoryViewModel>();
        StatusMessage = "Naviguji na historii...";
    }

    [RelayCommand]
    private void NavigateToSettings()
    {
        _navigationService.NavigateTo<SettingsViewModel>();
        StatusMessage = "Naviguji na nastavení...";
    }

    private void OnNavigated(object? sender, NavigationEventArgs e)
    {
        CurrentContent = e.ViewModel;
        _currentViewModelType = e.ViewModel?.GetType();
        
        // Notify all IsOn* properties changed
        OnPropertyChanged(nameof(IsOnDiskSelection));
        OnPropertyChanged(nameof(IsOnDiskCards));
        OnPropertyChanged(nameof(IsOnSurfaceTest));
        OnPropertyChanged(nameof(IsOnSmartCheck));
        OnPropertyChanged(nameof(IsOnAnalysis));
        OnPropertyChanged(nameof(IsOnDiskComparison));
        OnPropertyChanged(nameof(IsOnReport));
        OnPropertyChanged(nameof(IsOnHistory));
        OnPropertyChanged(nameof(IsOnSettings));
    }
}