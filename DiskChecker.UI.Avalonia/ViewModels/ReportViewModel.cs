using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DiskChecker.Core.Models;
using DiskChecker.UI.Avalonia.Services.Interfaces;
using DiskChecker.Application.Services;
using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using DiskChecker.Core.Interfaces;

namespace DiskChecker.UI.Avalonia.ViewModels
{
    public partial class ReportViewModel : ViewModelBase, INavigableViewModel
    {
        private readonly IDiskCardRepository _diskCardRepository;
        private readonly IDialogService _dialogService;
        private readonly INavigationService _navigationService;
        private readonly ISelectedDiskService _selectedDiskService;
        private readonly ICertificateGenerator _certificateGenerator;
        private ObservableCollection<TestReportItem> _reports = new();
        private TestReportItem? _selectedReport;
        private bool _isLoading;
        private string _statusMessage = string.Empty;

        public ReportViewModel(
            IDiskCardRepository diskCardRepository,
            IDialogService dialogService,
            INavigationService navigationService,
            ISelectedDiskService selectedDiskService,
            ICertificateGenerator certificateGenerator)
        {
            _diskCardRepository = diskCardRepository ?? throw new ArgumentNullException(nameof(diskCardRepository));
            _dialogService = dialogService ?? throw new ArgumentNullException(nameof(dialogService));
            _navigationService = navigationService ?? throw new ArgumentNullException(nameof(navigationService));
            _selectedDiskService = selectedDiskService ?? throw new ArgumentNullException(nameof(selectedDiskService));
            _certificateGenerator = certificateGenerator ?? throw new ArgumentNullException(nameof(certificateGenerator));
            
            DeleteReportCommand = new AsyncRelayCommand(DeleteReportAsync, () => SelectedReport != null);
            ExportReportCommand = new AsyncRelayCommand(ExportReportAsync, () => SelectedReport != null);
            OpenFullReportCommand = new AsyncRelayCommand(OpenFullReportAsync, () => SelectedReport != null);
            NavigateBackCommand = new RelayCommand(NavigateBack);
            LoadReportsCommand = new AsyncRelayCommand(LoadReportsAsync);
            _ = LoadReportsAsync();
        }

        public ObservableCollection<TestReportItem> Reports
        {
            get => _reports;
            set => SetProperty(ref _reports, value);
        }

        public TestReportItem? SelectedReport
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

        public bool IsLoading
        {
            get => _isLoading;
            set => SetProperty(ref _isLoading, value);
        }

        public string StatusMessage
        {
            get => _statusMessage;
            set => SetProperty(ref _statusMessage, value);
        }

        public IAsyncRelayCommand DeleteReportCommand { get; }
        public IAsyncRelayCommand ExportReportCommand { get; }
        public IAsyncRelayCommand LoadReportsCommand { get; }
        public IAsyncRelayCommand OpenFullReportCommand { get; }
        public IRelayCommand NavigateBackCommand { get; }

        public void OnNavigatedTo()
        {
            _ = LoadReportsAsync();
        }

        private async Task DeleteReportAsync()
        {
            if (SelectedReport == null) return;

            try
            {
                var confirmation = await _dialogService.ShowConfirmationAsync(
                    "Potvrzení", 
                    $"Opravdu chcete smazat test \"{SelectedReport.Title}\" z {SelectedReport.TestDate:dd.MM.yyyy HH:mm}?");
                
                if (confirmation)
                {
                    // Note: In a real implementation, we would delete from the repository
                    // For now, we'll just remove from the UI list
                    Reports.Remove(SelectedReport);
                    SelectedReport = null;
                    StatusMessage = "Test úspěšně odstraněn";
                }
            }
            catch (Exception ex)
            {
                StatusMessage = $"Chyba při mazání testu: {ex.Message}";
                await _dialogService.ShowErrorAsync("Chyba", $"Nepodařilo se smazat test: {ex.Message}");
            }
        }

        private async Task ExportReportAsync()
        {
            if (SelectedReport == null) return;

            try
            {
                IsLoading = true;
                StatusMessage = "Generuji certifikát...";

                var card = await _diskCardRepository.GetByIdAsync(SelectedReport.DiskCardId);
                if (card == null)
                {
                    await _dialogService.ShowErrorAsync("Chyba", "Karta disku pro vybraný report nebyla nalezena.");
                    return;
                }

                var session = await _diskCardRepository.GetTestSessionAsync(SelectedReport.Id);
                if (session == null)
                {
                    await _dialogService.ShowErrorAsync("Chyba", "Testová session pro vybraný report nebyla nalezena.");
                    return;
                }

                var certificate = await _certificateGenerator.GenerateCertificateAsync(session, card);
                var pdfPath = await _certificateGenerator.GeneratePdfAsync(certificate);
                await _diskCardRepository.CreateCertificateAsync(certificate);

                StatusMessage = $"Certifikát uložen: {certificate.CertificateNumber}";

                var openPdf = await _dialogService.ShowConfirmationAsync(
                    "Certifikát vytvořen",
                    $"Certifikát byl uložen do PDF:\n{pdfPath}\n\nOtevřít soubor?");

                if (openPdf)
                {
                    System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                    {
                        FileName = pdfPath,
                        UseShellExecute = true
                    });
                }
            }
            catch (InvalidOperationException ex)
            {
                StatusMessage = $"Chyba při exportu certifikátu: {ex.Message}";
                await _dialogService.ShowErrorAsync("Chyba", $"Nepodařilo se exportovat certifikát: {ex.Message}");
            }
            finally
            {
                IsLoading = false;
            }
        }

        private async Task OpenFullReportAsync()
        {
            if (SelectedReport == null)
            {
                return;
            }

            var card = await _diskCardRepository.GetByIdAsync(SelectedReport.DiskCardId);
            if (card == null)
            {
                await _dialogService.ShowErrorAsync("Chyba", "Karta disku pro vybraný report nebyla nalezena.");
                return;
            }

            _selectedDiskService.SelectedDisk = new CoreDriveInfo
            {
                Path = card.DevicePath,
                Name = card.ModelName,
                TotalSize = card.Capacity,
                SerialNumber = card.SerialNumber,
                FirmwareVersion = card.FirmwareVersion,
                IsRemovable = string.Equals(card.ConnectionType, "External", StringComparison.OrdinalIgnoreCase)
            };
            _selectedDiskService.SelectedDiskDisplayName = card.ModelName;
            _selectedDiskService.IsSelectedDiskLocked = card.IsLocked;

            _navigationService.NavigateTo<DiskCardDetailViewModel>();
        }

        private void NavigateBack()
        {
            _navigationService.NavigateTo<DiskCardsViewModel>();
        }

        private async Task LoadReportsAsync()
        {
            try
            {
                IsLoading = true;
                StatusMessage = "Načítám testy...";
                
                var cards = await _diskCardRepository.GetAllAsync();
                var reportItems = new ObservableCollection<TestReportItem>();
                
                foreach (var card in cards)
                {
                    var sessions = await _diskCardRepository.GetTestSessionsAsync(card.Id);
                    foreach (var session in sessions.OrderByDescending(s => s.StartedAt))
                    {
                        reportItems.Add(new TestReportItem
                        {
                            Id = session.Id,
                            Title = $"{session.TestType} - {card.ModelName}",
                            TestDate = session.StartedAt,
                            DeviceName = card.ModelName,
                            SerialNumber = card.SerialNumber,
                            Grade = session.Grade,
                            Score = session.Score,
                            AvgWriteSpeed = session.AverageWriteSpeedMBps,
                            AvgReadSpeed = session.AverageReadSpeedMBps,
                            ErrorCount = session.Errors.Count + session.WriteErrors + session.ReadErrors + session.VerificationErrors,
                            DiskCardId = card.Id
                        });
                    }
                }
                
                Reports = reportItems;
                StatusMessage = $"Načteno {reportItems.Count} testů";
            }
            catch (Exception ex)
            {
                StatusMessage = $"Chyba při načítání testů: {ex.Message}";
                await _dialogService.ShowErrorAsync("Chyba", $"Nepodařilo se načíst testy: {ex.Message}");
            }
            finally
            {
                IsLoading = false;
            }
        }
    }

    /// <summary>
    /// Item for displaying test reports in the UI.
    /// </summary>
    public class TestReportItem : ObservableObject
    {
        public int Id { get; set; }
        public string Title { get; set; } = string.Empty;
        public DateTime TestDate { get; set; }
        public string DeviceName { get; set; } = string.Empty;
        public string SerialNumber { get; set; } = string.Empty;
        public string Grade { get; set; } = string.Empty;
        public double Score { get; set; }
        public double AvgWriteSpeed { get; set; }
        public double AvgReadSpeed { get; set; }
        public int ErrorCount { get; set; }
        public int DiskCardId { get; set; }
    }
}