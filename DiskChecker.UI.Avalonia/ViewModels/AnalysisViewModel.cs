using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DiskChecker.Core.Models;
using DiskChecker.UI.Avalonia.Services.Interfaces;
using System;
using System.Collections.ObjectModel;
using System.Threading.Tasks;

namespace DiskChecker.UI.Avalonia.ViewModels
{
    public partial class AnalysisViewModel : ViewModelBase
    {
        private readonly IAnalysisService _analysisService;
        private readonly IDialogService _dialogService;
        private ObservableCollection<SurfaceTestResult> _testResults = new();
        private SurfaceTestResult? _selectedResult;
        private bool _isAnalyzing;
        private string _statusMessage = string.Empty;
        private int _progressPercentage;

        public AnalysisViewModel(IAnalysisService analysisService, IDialogService dialogService)
        {
            _analysisService = analysisService ?? throw new ArgumentNullException(nameof(analysisService));
            _dialogService = dialogService ?? throw new ArgumentNullException(nameof(dialogService));
            
            StartAnalysisCommand = new AsyncRelayCommand(StartAnalysisAsync, () => !IsAnalyzing);
            CancelAnalysisCommand = new AsyncRelayCommand(CancelAnalysisAsync, () => IsAnalyzing);
        }

        public ObservableCollection<SurfaceTestResult> TestResults
        {
            get => _testResults;
            set => SetProperty(ref _testResults, value);
        }

        public SurfaceTestResult? SelectedResult
        {
            get => _selectedResult;
            set => SetProperty(ref _selectedResult, value);
        }

        public bool IsAnalyzing
        {
            get => _isAnalyzing;
            set
            {
                if (SetProperty(ref _isAnalyzing, value))
                {
                    StartAnalysisCommand.NotifyCanExecuteChanged();
                    CancelAnalysisCommand.NotifyCanExecuteChanged();
                }
            }
        }

        public string StatusMessage
        {
            get => _statusMessage;
            set => SetProperty(ref _statusMessage, value);
        }

        public int ProgressPercentage
        {
            get => _progressPercentage;
            set => SetProperty(ref _progressPercentage, value);
        }

        public IAsyncRelayCommand StartAnalysisCommand { get; }
        public IAsyncRelayCommand CancelAnalysisCommand { get; }

        private async Task StartAnalysisAsync()
        {
            try
            {
                IsAnalyzing = true;
                StatusMessage = "Zahajuji analýzu povrchu disku...";
                ProgressPercentage = 0;
                
                // Zde bychom normálně získali vybraný disk z nějakého společného kontextu
                // Pro demo účely použijeme placeholder
                var deviceId = "PHYSICALDRIVE0"; // Placeholder hodnota
                
                var progress = new Progress<int>(percent =>
                {
                    ProgressPercentage = percent;
                    StatusMessage = $"Probíhá analýza povrchu... {percent}%";
                });

                var results = await _analysisService.AnalyzeSurfaceAsync(deviceId, progress);
                TestResults = new ObservableCollection<SurfaceTestResult>(results);
                StatusMessage = "Analýza povrchu dokončena";
            }
            catch (Exception ex)
            {
                StatusMessage = $"Chyba při analýze: {ex.Message}";
                await _dialogService.ShowErrorAsync("Chyba", $"Nepodařilo se analyzovat povrch disku: {ex.Message}");
            }
            finally
            {
                IsAnalyzing = false;
            }
        }

        private async Task CancelAnalysisAsync()
        {
            try
            {
                await _analysisService.CancelAnalysisAsync();
                IsAnalyzing = false;
                StatusMessage = "Analýza zrušena uživatelem";
            }
            catch (Exception ex)
            {
                StatusMessage = $"Chyba při rušení analýzy: {ex.Message}";
                await _dialogService.ShowErrorAsync("Chyba", $"Nepodařilo se zrušit analýzu: {ex.Message}");
            }
        }
    }
}
