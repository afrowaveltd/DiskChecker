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

    [RelayCommand]
    private void NavigateToDiskSelection()
    {
        _navigationService.NavigateTo<DiskSelectionViewModel>();
        StatusMessage = "Naviguji na výběr disků...";
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
    private void NavigateToReport()
    {
        _navigationService.NavigateTo<ReportViewModel>();
        StatusMessage = "Naviguji na reporty...";
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

    [RelayCommand]
    private void NavigateToDiskCards()
    {
        _navigationService.NavigateTo<DiskCardsViewModel>();
        StatusMessage = "Naviguji na karty disků...";
    }

    [RelayCommand]
    private void NavigateToDiskCardDetail()
    {
        _navigationService.NavigateTo<DiskCardDetailViewModel>();
        StatusMessage = "Naviguji na detail disku...";
    }

    [RelayCommand]
    private void NavigateToCertificate()
    {
        _navigationService.NavigateTo<CertificateViewModel>();
        StatusMessage = "Naviguji na certifikát...";
    }

    [RelayCommand]
    private void NavigateToDiskComparison()
    {
        _navigationService.NavigateTo<DiskComparisonViewModel>();
        StatusMessage = "Naviguji na porovnání disků...";
    }

    private void OnNavigated(object? sender, NavigationEventArgs e)
    {
        CurrentContent = e.ViewModel;
    }
}