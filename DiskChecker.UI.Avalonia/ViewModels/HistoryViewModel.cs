using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DiskChecker.Core.Interfaces;
using DiskChecker.Core.Models;
using DiskChecker.UI.Avalonia.Services.Interfaces;
using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;

namespace DiskChecker.UI.Avalonia.ViewModels
{
    public partial class HistoryViewModel : ViewModelBase, INavigableViewModel
    {
        private readonly IHistoryService _historyService;
        private readonly IDialogService _dialogService;
        private readonly INavigationService _navigationService;
        private readonly ISelectedDiskService _selectedDiskService;
        private readonly IDiskCardRepository _diskCardRepository;
        private ObservableCollection<HistoricalTest> _tests = new();
        private ObservableCollection<HistoricalTest> _filteredTests = new();
        private HistoricalTest? _selectedTest;
        private bool _isLoading;
        private string _statusMessage = string.Empty;
        private int _selectedFilterIndex;
        private string[] _filterLabels = Array.Empty<string>();

        public HistoryViewModel(
            IHistoryService historyService,
            IDialogService dialogService,
            INavigationService navigationService,
            ISelectedDiskService selectedDiskService,
            IDiskCardRepository diskCardRepository)
        {
            _historyService = historyService ?? throw new ArgumentNullException(nameof(historyService));
            _dialogService = dialogService ?? throw new ArgumentNullException(nameof(dialogService));
            _navigationService = navigationService ?? throw new ArgumentNullException(nameof(navigationService));
            _selectedDiskService = selectedDiskService ?? throw new ArgumentNullException(nameof(selectedDiskService));
            _diskCardRepository = diskCardRepository ?? throw new ArgumentNullException(nameof(diskCardRepository));

            _filterLabels = new[] { L.Get("Common.All"), L.Get("History.Filter.Surface"), L.Get("History.Filter.Smart"), L.Get("History.Filter.Sanitize") };
            
            LoadTestsCommand = new AsyncRelayCommand(LoadTestsAsync);
            RefreshCommand = new AsyncRelayCommand(LoadTestsAsync);
            ClearHistoryCommand = new AsyncRelayCommand(ClearHistoryAsync);
            DeleteTestCommand = new AsyncRelayCommand(DeleteTestAsync, () => SelectedTest != null);
            ViewDetailsCommand = new AsyncRelayCommand(ViewDetailsAsync, () => SelectedTest != null);
            GoBackCommand = new RelayCommand(GoBack);
        }

        public ObservableCollection<HistoricalTest> Tests
        {
            get => _tests;
            set
            {
                if (SetProperty(ref _tests, value))
                {
                    ApplyFilter();
                }
            }
        }

        public ObservableCollection<HistoricalTest> FilteredTests
        {
            get => _filteredTests;
            set => SetProperty(ref _filteredTests, value);
        }

        public int SelectedFilterIndex
        {
            get => _selectedFilterIndex;
            set
            {
                if (SetProperty(ref _selectedFilterIndex, value))
                {
                    ApplyFilter();
                }
            }
        }

        public string[] FilterOptions => _filterLabels;

        public HistoricalTest? SelectedTest
        {
            get => _selectedTest;
            set
            {
                if (SetProperty(ref _selectedTest, value))
                {
                    DeleteTestCommand.NotifyCanExecuteChanged();
                    ViewDetailsCommand.NotifyCanExecuteChanged();
                }
            }
        }

        public bool IsLoading
        {
            get => _isLoading;
            set
            {
                if (SetProperty(ref _isLoading, value))
                {
                    LoadTestsCommand.NotifyCanExecuteChanged();
                    RefreshCommand.NotifyCanExecuteChanged();
                    ClearHistoryCommand.NotifyCanExecuteChanged();
                }
            }
        }

        public string StatusMessage
        {
            get => _statusMessage;
            set => SetProperty(ref _statusMessage, value);
        }

        public double AverageScore => Tests.Count == 0 ? 0 : Tests.Average(t => t.Score);

        public IAsyncRelayCommand LoadTestsCommand { get; }
        public IAsyncRelayCommand RefreshCommand { get; }
        public IAsyncRelayCommand ClearHistoryCommand { get; }
        public IAsyncRelayCommand DeleteTestCommand { get; }
        public IAsyncRelayCommand ViewDetailsCommand { get; }
        public IRelayCommand GoBackCommand { get; }

        public void OnNavigatedTo()
        {
            _ = LoadTestsAsync();
        }

        private async Task LoadTestsAsync()
        {
            try
            {
                IsLoading = true;
                StatusMessage = L.Get("History.Status.Loading");
                
                var tests = (await _historyService.GetHistoryAsync()).OrderByDescending(t => t.TestDate).ToList();
                Tests = new ObservableCollection<HistoricalTest>(tests);
                ApplyFilter();
                OnPropertyChanged(nameof(AverageScore));
                
                StatusMessage = L.Get("History.Status.Loaded", tests.Count.ToString());
            }
            catch (InvalidOperationException ex)
            {
                StatusMessage = L.Get("History.Status.LoadError", ex.Message);
                await _dialogService.ShowErrorAsync(L.Get("Common.Error"), L.Get("History.LoadTestsError", ex.Message));
            }
            finally
            {
                IsLoading = false;
            }
        }

        private void ApplyFilter()
        {
            var filtered = _selectedFilterIndex switch
            {
                1 => _tests.Where(t => IsSurfaceTestType(t.TestType)),
                2 => _tests.Where(t => IsSmartTestType(t.TestType)),
                3 => _tests.Where(t => IsSanitizationTestType(t.TestType)),
                _ => _tests.AsEnumerable()
            };

            FilteredTests = new ObservableCollection<HistoricalTest>(filtered.ToList());
            StatusMessage = _selectedFilterIndex == 0
                ? L.Get("History.Status.Loaded", _tests.Count.ToString())
                : L.Get("History.Status.Filtered", FilteredTests.Count.ToString(), _tests.Count.ToString());
        }

        private static bool IsSurfaceTestType(string? testType)
        {
            if (string.IsNullOrWhiteSpace(testType)) return false;
            return testType.Equals("QuickRead", StringComparison.OrdinalIgnoreCase) ||
                   testType.Equals("QuickWrite", StringComparison.OrdinalIgnoreCase) ||
                   testType.Equals("FullRead", StringComparison.OrdinalIgnoreCase) ||
                   testType.Equals("FullWrite", StringComparison.OrdinalIgnoreCase) ||
                   testType.Equals("FullReadWrite", StringComparison.OrdinalIgnoreCase) ||
                   testType.Equals("SurfaceScan", StringComparison.OrdinalIgnoreCase);
        }

        private static bool IsSmartTestType(string? testType)
        {
            if (string.IsNullOrWhiteSpace(testType)) return false;
            return testType.Equals("SmartShort", StringComparison.OrdinalIgnoreCase) ||
                   testType.Equals("SmartExtended", StringComparison.OrdinalIgnoreCase) ||
                   testType.Equals("SmartConveyance", StringComparison.OrdinalIgnoreCase);
        }

        private static bool IsSanitizationTestType(string? testType)
        {
            if (string.IsNullOrWhiteSpace(testType)) return false;
            return testType.Equals("Sanitization", StringComparison.OrdinalIgnoreCase);
        }

        private async Task ClearHistoryAsync()
        {
            var confirmed = await _dialogService.ShowConfirmationAsync(
                "Vymazat historii",
                "Opravdu chcete vymazat celou historii testů?");

            if (!confirmed)
            {
                return;
            }

            await _historyService.ClearHistoryAsync();
            await LoadTestsAsync();
        }

        private async Task DeleteTestAsync()
        {
            if (SelectedTest == null) return;

            try
            {
                var confirmation = await _dialogService.ShowConfirmationAsync(
                    "Potvrzení", 
                    $"Opravdu chcete smazat test '{SelectedTest.TestType}' z {SelectedTest.TestDate:dd.MM.yyyy HH:mm}?");
                
                if (confirmation)
                {
                    await _historyService.DeleteHistoryAsync(SelectedTest.Id);
                    Tests.Remove(SelectedTest);
                    SelectedTest = null;
                    OnPropertyChanged(nameof(AverageScore));
                    StatusMessage = L.Get("History.Status.Deleted");
                }
            }
            catch (InvalidOperationException ex)
            {
                StatusMessage = L.Get("History.Status.DeleteError", ex.Message);
                await _dialogService.ShowErrorAsync(L.Get("Common.Error"), L.Get("History.DeleteFailed", ex.Message));
            }
        }

        private async Task ViewDetailsAsync()
        {
            await ViewDetailsAsync(SelectedTest);
        }

        private async Task ViewDetailsAsync(HistoricalTest? test)
        {
            if (test == null)
            {
                return;
            }

            SelectedTest = test;

            try
            {
                StatusMessage = L.Get("History.Status.LoadingDetail");

                if (!string.IsNullOrWhiteSpace(test.SerialNumber))
                {
                    var card = await _diskCardRepository.GetBySerialNumberAsync(test.SerialNumber);
                    if (card != null)
                    {
                        _selectedDiskService.SelectedDisk = new CoreDriveInfo
                        {
                            Path = card.DevicePath,
                            Name = card.ModelName,
                            TotalSize = card.Capacity,
                            SerialNumber = card.SerialNumber,
                            FirmwareVersion = card.FirmwareVersion
                        };
                        _selectedDiskService.SelectedDiskDisplayName = card.ModelName;
                        _selectedDiskService.IsSelectedDiskLocked = card.IsLocked;

                        var sessions = await _diskCardRepository.GetTestSessionsAsync(card.Id);
                        var targetSession = sessions
                            .OrderBy(s => Math.Abs((s.StartedAt - test.TestDate).Ticks))
                            .FirstOrDefault();

                        _selectedDiskService.SelectedTestSessionId = targetSession?.Id;
                        _selectedDiskService.SelectedCertificateId = null;

                        _navigationService.NavigateTo<CertificateViewModel>();
                        return;
                    }
                }

                await _dialogService.ShowMessageAsync("Detaily testu", 
                    $"Disk: {test.DiskName}\n" +
                    $"Typ testu: {test.TestType}\n" +
                    $"Datum: {test.TestDate:dd.MM.yyyy HH:mm}\n" +
                    $"Hodnocení: {test.Grade} ({test.Score}%)\n" +
                    $"Stav: {test.HealthAssessment}\n" +
                    $"Trvání: {test.Duration}\n" +
                    $"Chyby: {test.ErrorCount}\n\n" +
                    $"Poznámky: {test.Notes ?? "Žádné"}");
                
                StatusMessage = L.Get("History.Status.DetailShown");
            }
            catch (InvalidOperationException ex)
            {
                StatusMessage = L.Get("History.Status.DetailError", ex.Message);
                await _dialogService.ShowErrorAsync(L.Get("Common.Error"), L.Get("History.DetailError", ex.Message));
            }
        }

        private void GoBack()
        {
            _navigationService.NavigateTo<DiskSelectionViewModel>();
        }
    }
}