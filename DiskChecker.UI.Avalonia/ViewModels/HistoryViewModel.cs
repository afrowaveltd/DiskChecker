using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DiskChecker.Core.Models;
using DiskChecker.UI.Avalonia.Services.Interfaces;
using System;
using System.Collections.ObjectModel;
using System.Threading.Tasks;

namespace DiskChecker.UI.Avalonia.ViewModels
{
    public partial class HistoryViewModel : ViewModelBase
    {
        private readonly IHistoryService _historyService;
        private readonly IDialogService _dialogService;
        private ObservableCollection<HistoricalTest> _tests = new();
        private HistoricalTest? _selectedTest;
        private bool _isLoading;
        private string _statusMessage = string.Empty;

        public HistoryViewModel(IHistoryService historyService, IDialogService dialogService)
        {
            _historyService = historyService ?? throw new ArgumentNullException(nameof(historyService));
            _dialogService = dialogService ?? throw new ArgumentNullException(nameof(dialogService));
            
            LoadTestsCommand = new AsyncRelayCommand(LoadTestsAsync);
            DeleteTestCommand = new AsyncRelayCommand(DeleteTestAsync, () => SelectedTest != null);
            ViewDetailsCommand = new AsyncRelayCommand(ViewDetailsAsync, () => SelectedTest != null);
            
            _ = LoadTestsAsync();
        }

        public ObservableCollection<HistoricalTest> Tests
        {
            get => _tests;
            set => SetProperty(ref _tests, value);
        }

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
                }
            }
        }

        public string StatusMessage
        {
            get => _statusMessage;
            set => SetProperty(ref _statusMessage, value);
        }

        public IAsyncRelayCommand LoadTestsCommand { get; }
        public IAsyncRelayCommand DeleteTestCommand { get; }
        public IAsyncRelayCommand ViewDetailsCommand { get; }

        private async Task LoadTestsAsync()
        {
            try
            {
                IsLoading = true;
                StatusMessage = "Načítám historii testů...";
                
                var tests = await _historyService.GetHistoricalTestsAsync();
                Tests = new ObservableCollection<HistoricalTest>(tests);
                
                StatusMessage = $"Načteno {tests.Count()} testů z historie";
            }
            catch (Exception ex)
            {
                StatusMessage = $"Chyba při načítání historie: {ex.Message}";
                await _dialogService.ShowErrorAsync("Chyba", $"Nepodařilo se načíst historii testů: {ex.Message}");
            }
            finally
            {
                IsLoading = false;
            }
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
                    await _historyService.DeleteHistoricalTestAsync(SelectedTest.TestId);
                    Tests.Remove(SelectedTest);
                    SelectedTest = null;
                    StatusMessage = "Test úspěšně smazán z historie";
                }
            }
            catch (Exception ex)
            {
                StatusMessage = $"Chyba při mazání testu: {ex.Message}";
                await _dialogService.ShowErrorAsync("Chyba", $"Nepodařilo se smazat test z historie: {ex.Message}");
            }
        }

        private async Task ViewDetailsAsync()
        {
            if (SelectedTest == null) return;

            try
            {
                StatusMessage = "Načítám detaily testu...";
                await _dialogService.ShowMessageAsync("Detaily testu", 
                    $"Typ testu: {SelectedTest.TestType}\n" +
                    $"Datum: {SelectedTest.TestDate:dd.MM.yyyy HH:mm}\n" +
                    $"Hodnocení: {SelectedTest.Grade} ({SelectedTest.Score}%)\n" +
                    $"Stav: {SelectedTest.HealthAssessment}\n" +
                    $"Trvání: {SelectedTest.Duration}\n" +
                    $"Chyby: {SelectedTest.ErrorCount}\n\n" +
                    $"Poznámky: {SelectedTest.Notes ?? "Žádné"}");
                
                StatusMessage = "Detaily testu zobrazeny";
            }
            catch (Exception ex)
            {
                StatusMessage = $"Chyba při zobrazení detailů: {ex.Message}";
                await _dialogService.ShowErrorAsync("Chyba", $"Nepodařilo se zobrazit detaily testu: {ex.Message}");
            }
        }
    }
}
