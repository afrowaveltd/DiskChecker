using Xunit;
using NSubstitute;
using DiskChecker.UI.WPF.ViewModels;
using DiskChecker.UI.WPF.Services;

namespace DiskChecker.Tests.WPF.ViewModels;

/// <summary>
/// Unit testy pro MainWindowViewModel.
/// </summary>
public class MainWindowViewModelTests
{
    private readonly INavigationService _navigationService;
    private readonly MainWindowViewModel _viewModel;

    public MainWindowViewModelTests()
    {
        // Arrange - vytvoření mock objektů
        _navigationService = Substitute.For<INavigationService>();
        
        _viewModel = new MainWindowViewModel(_navigationService);
    }

    /// <summary>
    /// Test inicializace - ověření načtení výchozího view.
    /// </summary>
    [Fact]
    public void Constructor_ShouldNavigateToDiskSelection()
    {
        // Arrange & Act se provádí v konstruktoru

        // Assert
        _navigationService.Received(1).NavigateTo<DiskSelectionViewModel>();
    }

    /// <summary>
    /// Test navigace na DiskSelection.
    /// </summary>
    [Fact]
    public void NavigateToDiskSelection_ShouldCallNavigationService()
    {
        // Act
        _viewModel.NavigateToDiskSelectionCommand.Execute(null);

        // Assert - should be called twice: once in constructor, once in command
        _navigationService.Received(2).NavigateTo<DiskSelectionViewModel>();
    }

    /// <summary>
    /// Test navigace na SmartCheck.
    /// </summary>
    [Fact]
    public void NavigateToSmartCheck_ShouldCallNavigationService()
    {
        // Act
        _viewModel.NavigateToSmartCheckCommand.Execute(null);

        // Assert
        _navigationService.Received(1).NavigateTo<SmartCheckViewModel>();
    }

    /// <summary>
    /// Test navigace na SurfaceTest.
    /// </summary>
    [Fact]
    public void NavigateToSurfaceTest_ShouldCallNavigationService()
    {
        // Act
        _viewModel.NavigateToSurfaceTestCommand.Execute(null);

        // Assert
        _navigationService.Received(1).NavigateTo<SurfaceTestViewModel>();
    }

    /// <summary>
    /// Test navigace na Report.
    /// </summary>
    [Fact]
    public void NavigateToReport_ShouldCallNavigationService()
    {
        // Act
        _viewModel.NavigateToReportCommand.Execute(null);

        // Assert
        _navigationService.Received(1).NavigateTo<ReportViewModel>();
    }

    /// <summary>
    /// Test navigace na History.
    /// </summary>
    [Fact]
    public void NavigateToHistory_ShouldCallNavigationService()
    {
        // Act
        _viewModel.NavigateToHistoryCommand.Execute(null);

        // Assert
        _navigationService.Received(1).NavigateTo<HistoryViewModel>();
    }

    /// <summary>
    /// Test navigace na Settings.
    /// </summary>
    [Fact]
    public void NavigateToSettings_ShouldCallNavigationService()
    {
        // Act
        _viewModel.NavigateToSettingsCommand.Execute(null);

        // Assert
        _navigationService.Received(1).NavigateTo<SettingsViewModel>();
    }

    /// <summary>
    /// Test GoBack.
    /// </summary>
    [Fact]
    public void GoBack_ShouldCallNavigationService()
    {
        // Act
        _viewModel.GoBackCommand.Execute(null);

        // Assert
        _navigationService.Received(1).GoBack();
    }

    /// <summary>
    /// Test ViewChanged event handling.
    /// </summary>
    [Fact]
    public void OnViewChanged_ShouldUpdateCurrentViewModel()
    {
        // Arrange
        var newViewModel = Substitute.For<ViewModelBase>();
        var newView = new object();
        var eventArgs = new ViewChangedEventArgs
        {
            ViewModelType = typeof(DiskSelectionViewModel),
            View = newView,
            ViewModel = newViewModel
        };

        // Act
        _navigationService.ViewChanged += Raise.EventWith(_navigationService, eventArgs);

        // Assert
        Assert.Equal(newViewModel, _viewModel.CurrentViewModel);
        Assert.Equal(newView, _viewModel.CurrentContent);
    }
}
