using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DiskChecker.Core.Models;
using DiskChecker.UI.Avalonia.Services.Interfaces;
using DiskChecker.Application.Services;
using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;

namespace DiskChecker.UI.Avalonia.ViewModels
{
    public partial class ReportViewModel : ViewModelBase
    {
        private readonly HistoryService _historyService;
        private readonly IDialogService _dialogService;
        private ObservableCollection<TestReport> _reports = new();
        private TestReport? _selectedReport;
        private bool _isGenerating;
        private string _statusMessage = string.Empty;

        public ReportViewModel(HistoryService historyService, IDialogService dialogService)
        {
            _historyService = historyService ?? throw new ArgumentNullException(nameof(historyService));
            _dialogService = dialogService ?? throw new ArgumentNullException(nameof(dialogService));
            
            GenerateReportCommand = new AsyncRelayCommand(GenerateReportAsync, () => !IsGenerating);
            DeleteReportCommand = new AsyncRelayCommand(DeleteReportAsync, () => SelectedReport != null);
            ExportReportCommand = new AsyncRelayCommand(ExportReportAsync, () => SelectedReport != null);
            
            LoadReportsCommand = new AsyncRelayCommand(LoadReportsAsync);
            _ = LoadReportsAsync();
        }

        public ObservableCollection<TestReport> Reports
        {
            get => _reports;
            set => SetProperty(ref _reports, value);
        }

        public TestReport? SelectedReport
        {
            get => _selectedReport;
            set
            {
                if (SetProperty(ref _selectedReport, value))
                {
                    DeleteReportCommand.NotifyCanExecuteChanged();
                    ExportReportCommand.NotifyCanExecuteChanged();
                }
            }
        }

        public bool IsGenerating
        {
            get => _isGenerating;
            set
            {
                if (SetProperty(ref _isGenerating, value))
                {
                    GenerateReportCommand.NotifyCanExecuteChanged();
                }
            }
        }

        public string StatusMessage
        {
            get => _statusMessage;
            set => SetProperty(ref _statusMessage, value);
        }

        public IAsyncRelayCommand GenerateReportCommand { get; }
        public IAsyncRelayCommand DeleteReportCommand { get; }
        public IAsyncRelayCommand ExportReportCommand { get; }
        public IAsyncRelayCommand LoadReportsCommand { get; }

        private async Task GenerateReportAsync()
        {
            try
            {
                IsGenerating = true;
                StatusMessage = "Generuji report...";
                
                var report = await _historyService.GenerateReportAsync();
                
                Reports.Add(report);
                SelectedReport = report;
                
                StatusMessage = "Report úspěšně vygenerován";
            }
            catch (Exception ex)
            {
                StatusMessage = $"Chyba při generování reportu: {ex.Message}";
                await _dialogService.ShowErrorAsync("Chyba", $"Nepodařilo se vygenerovat report: {ex.Message}");
            }
            finally
            {
                IsGenerating = false;
            }
        }

        private async Task DeleteReportAsync()
        {
            if (SelectedReport == null) return;

            try
            {
                var confirmation = await _dialogService.ShowConfirmationAsync(
                    "Potvrzení", 
                    $"Opravdu chcete smazat report z {SelectedReport.TestDate:dd.MM.yyyy}?");
                
                if (confirmation)
                {
                    await _historyService.DeleteReportAsync(SelectedReport.ReportId);
                    Reports.Remove(SelectedReport);
                    SelectedReport = null;
                    StatusMessage = "Report úspěšně smazán";
                }
            }
            catch (Exception ex)
            {
                StatusMessage = $"Chyba při mazání reportu: {ex.Message}";
                await _dialogService.ShowErrorAsync("Chyba", $"Nepodařilo se smazat report: {ex.Message}");
            }
        }

        private async Task ExportReportAsync()
        {
            if (SelectedReport == null) return;

            try
            {
                StatusMessage = "Exportuji report...";
                await _historyService.ExportReportAsync(SelectedReport, "pdf");
                StatusMessage = "Report úspěšně exportován";
            }
            catch (Exception ex)
            {
                StatusMessage = $"Chyba při exportu reportu: {ex.Message}";
                await _dialogService.ShowErrorAsync("Chyba", $"Nepodařilo se exportovat report: {ex.Message}");
            }
        }

        private async Task LoadReportsAsync()
        {
            try
            {
                StatusMessage = "Načítám reporty...";
                var reports = await _historyService.GetReportsAsync();
                Reports = new ObservableCollection<TestReport>(reports);
                StatusMessage = $"Načteno {reports.Count()} reportů";
            }
            catch (Exception ex)
            {
                StatusMessage = $"Chyba při načítání reportů: {ex.Message}";
                await _dialogService.ShowErrorAsync("Chyba", $"Nepodařilo se načíst reporty: {ex.Message}");
            }
        }
    }
}