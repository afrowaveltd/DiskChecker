using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DiskChecker.Application.Services;
using DiskChecker.Core.Models;
using DiskChecker.UI.Avalonia.Services.Interfaces;
using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using DiskChecker.Core.Interfaces;
using System.IO;
using System.Text.Json;
using System.Collections.Generic;
using System.Data.Common;
using DiskChecker.UI.Avalonia.Services;

namespace DiskChecker.UI.Avalonia.ViewModels
{
    public partial class ReportViewModel : ViewModelBase, INavigableViewModel
    {
        private static readonly JsonSerializerOptions ReportJsonOptions = new()
        {
            WriteIndented = true
        };

        private readonly IDiskCardRepository _diskCardRepository;
        private readonly IDialogService _dialogService;
        private readonly INavigationService _navigationService;
        private readonly ICertificateGenerator _certificateGenerator;
        private readonly ReportDocumentState _reportDocumentState;
        private readonly CertificateExportService _certificateExportService;
        private ObservableCollection<TestReportItem> _reports = new();
        private ObservableCollection<TestReportItem> _filteredReports = new();
        private TestReportItem? _selectedReport;
        private bool _isLoading;
        private string _statusMessage = string.Empty;
        private string _searchText = string.Empty;
        private int _loadingProgress;
        private bool _isProgressIndeterminate = true;

        public ReportViewModel(
            IDiskCardRepository diskCardRepository,
            IDialogService dialogService,
            INavigationService navigationService,
            ICertificateGenerator certificateGenerator,
            ReportDocumentState reportDocumentState,
            CertificateExportService certificateExportService)
        {
            _diskCardRepository = diskCardRepository ?? throw new ArgumentNullException(nameof(diskCardRepository));
            _dialogService = dialogService ?? throw new ArgumentNullException(nameof(dialogService));
            _navigationService = navigationService ?? throw new ArgumentNullException(nameof(navigationService));
            _certificateGenerator = certificateGenerator ?? throw new ArgumentNullException(nameof(certificateGenerator));
            _reportDocumentState = reportDocumentState ?? throw new ArgumentNullException(nameof(reportDocumentState));
            _certificateExportService = certificateExportService ?? throw new ArgumentNullException(nameof(certificateExportService));
            
            DeleteReportCommand = new AsyncRelayCommand(DeleteReportAsync, () => SelectedReport != null);
            ExportReportCommand = new AsyncRelayCommand(ExportReportAsync, () => SelectedReport != null);
            OpenFullReportCommand = new AsyncRelayCommand(OpenFullReportAsync, () => SelectedReport != null);
            ViewGeneratedReportCommand = new AsyncRelayCommand(ViewGeneratedReportAsync, () => _reportDocumentState.HasReport);
            NavigateBackCommand = new RelayCommand(NavigateBack);
            LoadReportsCommand = new AsyncRelayCommand(LoadReportsAsync);
            _ = LoadReportsAsync();
        }

        public ObservableCollection<TestReportItem> Reports
        {
            get => _reports;
            set
            {
                if (SetProperty(ref _reports, value))
                {
                    ApplyFilter();
                }
            }
        }

        /// <summary>
        /// Filtrovaná kolekce reportů podle vyhledávacího textu
        /// </summary>
        public ObservableCollection<TestReportItem> FilteredReports
        {
            get => _filteredReports;
            set => SetProperty(ref _filteredReports, value);
        }

        /// <summary>
        /// Vyhledávací text pro filtrování reportů
        /// </summary>
        public string SearchText
        {
            get => _searchText;
            set
            {
                if (SetProperty(ref _searchText, value))
                {
                    ApplyFilter();
                }
            }
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
                    OpenFullReportCommand.NotifyCanExecuteChanged();
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

        /// <summary>
        /// Aktuální průběh dlouhé operace v procentech (0-100).
        /// </summary>
        public int LoadingProgress
        {
            get => _loadingProgress;
            set => SetProperty(ref _loadingProgress, value);
        }

        /// <summary>
        /// Určuje, zda má indikátor běžet v neurčitém režimu.
        /// </summary>
        public bool IsProgressIndeterminate
        {
            get => _isProgressIndeterminate;
            set => SetProperty(ref _isProgressIndeterminate, value);
        }

        public IAsyncRelayCommand DeleteReportCommand { get; }
        public IAsyncRelayCommand ExportReportCommand { get; }
        public IAsyncRelayCommand LoadReportsCommand { get; }
        public IAsyncRelayCommand OpenFullReportCommand { get; }
        public IRelayCommand NavigateBackCommand { get; }
        public IAsyncRelayCommand ViewGeneratedReportCommand { get; }

        public void OnNavigatedTo()
        {
            ViewGeneratedReportCommand.NotifyCanExecuteChanged();
            _ = LoadReportsAsync();
        }

        /// <summary>
        /// Aplikuje vyhledávací filtr na kolekci reportů
        /// </summary>
        private void ApplyFilter()
        {
            if (string.IsNullOrWhiteSpace(SearchText))
            {
                FilteredReports = new ObservableCollection<TestReportItem>(Reports);
            }
            else
            {
                var searchLower = SearchText.ToLowerInvariant();
                var filtered = Reports.Where(r =>
                    r.Title.ToLowerInvariant().Contains(searchLower) ||
                    r.SerialNumber.ToLowerInvariant().Contains(searchLower) ||
                    r.DeviceName.ToLowerInvariant().Contains(searchLower) ||
                    r.Grade.ToLowerInvariant().Contains(searchLower) ||
                    r.TestDate.ToString("dd.MM.yyyy HH:mm").Contains(searchLower)
                ).ToList();

                FilteredReports = new ObservableCollection<TestReportItem>(filtered);
            }

            StatusMessage = string.IsNullOrWhiteSpace(SearchText)
                ? $"Načteno {Reports.Count} testů"
                : $"Zobrazeno {FilteredReports.Count} z {Reports.Count} testů";
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
                SetLoadingState(true, "Generuji certifikát...", null);

                // Use centralized export service with automatic downsampling and progress reporting
                var progress = new Progress<CertificateExportProgress>(p =>
                {
                    SetLoadingState(true, p.Message, p.ProgressPercent);
                });

                var result = await _certificateExportService.ExportCertificateAsync(
                    SelectedReport.Id,
                    progress);

                if (!result.IsSuccess)
                {
                    await _dialogService.ShowErrorAsync("Chyba", $"Nepodařilo se exportovat certifikát: {result.ErrorMessage}");
                    return;
                }

                StatusMessage = $"Certifikát uložen: {result.Certificate!.CertificateNumber}";

                var openPdf = await _dialogService.ShowConfirmationAsync(
                    "Certifikát vytvořen",
                    $"Certifikát byl uložen do PDF:\n{result.PdfPath}\n\nOtevřít soubor?");

                if (openPdf)
                {
                    DocumentLauncher.OpenFile(result.PdfPath!);
                }
            }
            catch (InvalidOperationException ex)
            {
                StatusMessage = $"Chyba při exportu certifikátu: {ex.Message}";
                await _dialogService.ShowErrorAsync("Chyba", $"Nepodařilo se exportovat certifikát: {ex.Message}");
            }
            finally
            {
                SetLoadingState(false, StatusMessage, null);
            }
        }

        /// <summary>
        /// Generuje plný JSON report testu s optimalizacemi proti přetížení SQLite:
        /// - omezuje SMART data na JSON texty (bez parsed objektů),
        /// - downsampluje vzorky,
        /// - omezuje chyby na prvních 100,
        /// - zapisuje JSON streamem do souboru.
        /// </summary>
        private async Task OpenFullReportAsync()
        {
            if (SelectedReport == null)
            {
                return;
            }

            try
            {
                SetLoadingState(true, "Připravuji generování plného reportu...", 0);

                UpdateProgress("Načítám kartu disku...", 5);
                var card = await _diskCardRepository.GetByIdAsync(SelectedReport.DiskCardId);
                if (card == null)
                {
                    await _dialogService.ShowErrorAsync("Chyba", "Karta disku pro vybraný report nebyla nalezena.");
                    return;
                }

                UpdateProgress("Načítám data testu...", 15);
                var session = await _diskCardRepository.GetTestSessionWithoutSamplesAsync(SelectedReport.Id);
                if (session == null)
                {
                    await _dialogService.ShowErrorAsync("Chyba", "Testová session pro vybraný report nebyla nalezena.");
                    return;
                }

                UpdateProgress("Načítám vzorky rychlosti...", 28);
                List<SpeedSample> writeSamples = new();
                List<SpeedSample> readSamples = new();
                var reducedMode = false;

                if (session.TestType == TestType.Sanitization || session.BytesWritten > 0 || session.BytesRead > 0)
                {
                    (writeSamples, readSamples, reducedMode) = await LoadSpeedSamplesProgressiveAsync(
                        SelectedReport.Id,
                        modulo: 100,
                        maxRemainders: 12,
                        progressStart: 28,
                        progressEnd: 64,
                        stageMessage: "Načítám vzorky rychlosti");
                }

                UpdateProgress("Načítám detaily chyb...", 66);
                List<TestError> testErrors;
                try
                {
                    testErrors = await _diskCardRepository.GetTestErrorsAsync(SelectedReport.Id, 100);
                }
                catch (DbException ex) when (IsDiskFullLike(ex))
                {
                    reducedMode = true;
                    testErrors = new List<TestError>();
                    StatusMessage = "Nedostatek místa v SQLite při čtení chyb. Pokračuji bez detailů chyb.";
                }

                UpdateProgress("Připravuji cílové soubory...", 72);
                var reportDirectory = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                    "DiskChecker",
                    "Reports",
                    "Full");
                Directory.CreateDirectory(reportDirectory);

                string? graphImagePath = null;
                if (session.TestType == TestType.Sanitization && (writeSamples.Count > 0 || readSamples.Count > 0))
                {
                    try
                    {
                        UpdateProgress("Optimalizuji data pro graf...", 78);
                        var writeGraphSamplesTask = Task.Run(() => DownsampleSamplesForGraph(writeSamples, 1200));
                        var readGraphSamplesTask = Task.Run(() => DownsampleSamplesForGraph(readSamples, 1200));
                        await Task.WhenAll(writeGraphSamplesTask, readGraphSamplesTask);

                        session.WriteSamples = writeGraphSamplesTask.Result;
                        session.ReadSamples = readGraphSamplesTask.Result;

                        UpdateProgress("Generuji graf sanitizačního testu...", 86);
                        var previewCertificate = await _certificateGenerator.GenerateCertificateAsync(session, card);
                        var previewBytes = await _certificateGenerator.GeneratePreviewAsync(previewCertificate);

                        graphImagePath = Path.Combine(
                            reportDirectory,
                            $"graph_{card.Id}_{session.Id}_{DateTime.UtcNow:yyyyMMddHHmmss}.png");

                        await File.WriteAllBytesAsync(graphImagePath, previewBytes);
                    }
                    catch (DbException ex) when (IsDiskFullLike(ex))
                    {
                        reducedMode = true;
                        graphImagePath = null;
                        StatusMessage = "SQLite nemá dost místa pro přípravu grafu. Pokračuji bez obrázku grafu.";
                    }
                    catch (IOException)
                    {
                        reducedMode = true;
                        graphImagePath = null;
                        StatusMessage = "Nedostatek místa při zápisu grafu. Pokračuji bez obrázku grafu.";
                    }
                }

                UpdateProgress("Připravuji JSON data reportu...", 90);
                var writeSamplesForJsonTask = Task.Run(() => DownsampleEveryNth(writeSamples, 2));
                var readSamplesForJsonTask = Task.Run(() => DownsampleEveryNth(readSamples, 2));
                await Task.WhenAll(writeSamplesForJsonTask, readSamplesForJsonTask);

                var fullReport = new
                {
                    generatedAtUtc = DateTime.UtcNow,
                    mode = reducedMode ? "reduced-samples" : "standard",
                    disk = new
                    {
                        id = card.Id,
                        model = card.ModelName,
                        serial = card.SerialNumber,
                        devicePath = card.DevicePath,
                        diskType = card.DiskType,
                        interfaceType = card.InterfaceType,
                        capacityBytes = card.Capacity,
                        firmware = card.FirmwareVersion,
                        connectionType = card.ConnectionType
                    },
                    testSession = new
                    {
                        id = session.Id,
                        sessionId = session.SessionId,
                        testType = session.TestType.ToString(),
                        startedAtUtc = session.StartedAt,
                        completedAtUtc = session.CompletedAt,
                        duration = session.Duration,
                        status = session.Status.ToString(),
                        result = session.Result.ToString(),
                        grade = session.Grade,
                        score = session.Score,
                        notes = session.Notes,
                        metrics = new
                        {
                            bytesWritten = session.BytesWritten,
                            bytesRead = session.BytesRead,
                            avgWriteSpeedMBps = session.AverageWriteSpeedMBps,
                            maxWriteSpeedMBps = session.MaxWriteSpeedMBps,
                            minWriteSpeedMBps = session.MinWriteSpeedMBps,
                            avgReadSpeedMBps = session.AverageReadSpeedMBps,
                            maxReadSpeedMBps = session.MaxReadSpeedMBps,
                            minReadSpeedMBps = session.MinReadSpeedMBps,
                            writeErrors = session.WriteErrors,
                            readErrors = session.ReadErrors,
                            verificationErrors = session.VerificationErrors
                        },
                        temperatures = new
                        {
                            start = session.StartTemperature,
                            max = session.MaxTemperature,
                            average = session.AverageTemperature
                        },
                        partitioning = new
                        {
                            created = session.PartitionCreated,
                            scheme = session.PartitionScheme,
                            formatted = session.WasFormatted,
                            fileSystem = session.FileSystem,
                            volumeLabel = session.VolumeLabel
                        }
                    },
                    smartDump = new
                    {
                        beforeRawJson = session.SmartBeforeJson,
                        afterRawJson = session.SmartAfterJson,
                        attributeChanges = session.SmartChanges
                    },
                    speedSeries = new
                    {
                        writeSamples = writeSamplesForJsonTask.Result,
                        readSamples = readSamplesForJsonTask.Result
                    },
                    temperatureSeries = session.TemperatureSamples,
                    errors = new
                    {
                        summary = new
                        {
                            totalErrors = session.WriteErrors + session.ReadErrors + session.VerificationErrors,
                            writeErrors = session.WriteErrors,
                            readErrors = session.ReadErrors,
                            verificationErrors = session.VerificationErrors
                        },
                        details = testErrors
                    },
                    assets = new
                    {
                        graphImage = graphImagePath
                    }
                };

                var filePath = Path.Combine(
                    reportDirectory,
                    $"FullReport_{card.Id}_{session.Id}_{DateTime.UtcNow:yyyyMMddHHmmss}.json");

                UpdateProgress("Zapisuji report do souboru...", 96);
                try
                {
                    await using var fileStream = new FileStream(filePath, FileMode.Create, FileAccess.Write, FileShare.None);
                    await JsonSerializer.SerializeAsync(fileStream, fullReport, ReportJsonOptions);
                }
                catch (IOException)
                {
                    reducedMode = true;
                    filePath = Path.Combine(Path.GetTempPath(), $"FullReport_{card.Id}_{session.Id}_{DateTime.UtcNow:yyyyMMddHHmmss}.json");
                    await using var fallbackStream = new FileStream(filePath, FileMode.Create, FileAccess.Write, FileShare.None);
                    await JsonSerializer.SerializeAsync(fallbackStream, fullReport, ReportJsonOptions);
                    StatusMessage = "Do výchozí složky nešlo zapisovat. Report byl uložen do dočasné složky systému.";
                }

                UpdateProgress("Hotovo", 100);
                StatusMessage = $"Plný report uložen: {filePath}";
                _reportDocumentState.LastReportPath = filePath;
                ViewGeneratedReportCommand.NotifyCanExecuteChanged();

                var openFile = await _dialogService.ShowConfirmationAsync(
                    "Plný report připraven",
                    (reducedMode ? "Report byl vytvořen v omezeném režimu kvůli nedostatku místa.\n\n" : string.Empty) +
                    $"Report byl uložen do:\n{filePath}\n\n" +
                    (graphImagePath != null ? $"Graf (PNG):\n{graphImagePath}\n\n" : string.Empty) +
                    "Otevřít report v aplikaci?");

                if (openFile)
                {
                    _navigationService.NavigateTo<FullReportViewerViewModel>();
                }
            }
            catch (Exception ex)
            {
                StatusMessage = $"Chyba při generování plného reportu: {ex.Message}";
                await _dialogService.ShowErrorAsync("Chyba", $"Nepodařilo se vygenerovat plný report: {ex.Message}");
            }
            finally
            {
                SetLoadingState(false, StatusMessage, null);
            }
        }

        private async Task ViewGeneratedReportAsync()
        {
            if (!_reportDocumentState.HasReport)
            {
                await _dialogService.ShowWarningAsync("Report", "Nejdříve vygenerujte plný report.");
                return;
            }

            _navigationService.NavigateTo<FullReportViewerViewModel>();
        }

        private void NavigateBack()
        {
            _navigationService.NavigateTo<DiskCardsViewModel>();
        }

        private async Task LoadReportsAsync()
        {
            try
            {
                SetLoadingState(true, "Načítám testy...", null);

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
                            ErrorCount = session.WriteErrors + session.ReadErrors + session.VerificationErrors,
                            DiskCardId = card.Id
                        });
                    }
                }

                Reports = reportItems;
            }
            catch (Exception ex)
            {
                StatusMessage = $"Chyba při načítání testů: {ex.Message}";
                await _dialogService.ShowErrorAsync("Chyba", $"Nepodařilo se načíst testy: {ex.Message}");
                Reports = new ObservableCollection<TestReportItem>();
            }
            finally
            {
                SetLoadingState(false, StatusMessage, null);
            }
        }

        /// <summary>
        /// Nastaví stav dlouhé operace a režim indikátoru průběhu.
        /// </summary>
        private void SetLoadingState(bool isLoading, string message, int? progressPercent)
        {
            IsLoading = isLoading;
            StatusMessage = message;

            if (progressPercent.HasValue)
            {
                IsProgressIndeterminate = false;
                LoadingProgress = Math.Clamp(progressPercent.Value, 0, 100);
                return;
            }

            IsProgressIndeterminate = true;
            LoadingProgress = 0;
        }

        /// <summary>
        /// Aktualizuje text stavu a procentní průběh.
        /// </summary>
        private void UpdateProgress(string message, int progressPercent)
        {
            StatusMessage = message;
            IsProgressIndeterminate = false;
            LoadingProgress = Math.Clamp(progressPercent, 0, 100);
        }

        /// <summary>
        /// Downsampluje sadu vzorků pro graf na rozumný počet bodů.
        /// </summary>
        private static List<SpeedSample> DownsampleSamplesForGraph(IReadOnlyList<SpeedSample> samples, int maxPoints)
        {
            if (samples.Count <= maxPoints)
            {
                return new List<SpeedSample>(samples);
            }

            var step = Math.Max(1, samples.Count / maxPoints);
            return DownsampleEveryNth(samples, step);
        }

        /// <summary>
        /// Vybere každý N-tý vzorek bez změny pořadí.
        /// </summary>
        private static List<SpeedSample> DownsampleEveryNth(IReadOnlyList<SpeedSample> samples, int step)
        {
            if (samples.Count == 0)
            {
                return new List<SpeedSample>();
            }

            if (step <= 1)
            {
                return new List<SpeedSample>(samples);
            }

            var result = new List<SpeedSample>(Math.Max(1, samples.Count / step + 1));
            for (var i = 0; i < samples.Count; i += step)
            {
                result.Add(samples[i]);
            }

            var last = samples[^1];
            if (!ReferenceEquals(result[^1], last))
            {
                result.Add(last);
            }

            return result;
        }

        /// <summary>
        /// Načte vzorky rychlosti progresivně po dávkách (modulo/remainder), aby se nečetla celá série najednou.
        /// Používá se pouze pro OpenFullReportAsync, exporty certifikátů používají CertificateExportService.
        /// </summary>
        private async Task<(List<SpeedSample> WriteSamples, List<SpeedSample> ReadSamples, bool ReducedMode)> LoadSpeedSamplesProgressiveAsync(
            int sessionId,
            int modulo,
            int maxRemainders,
            int progressStart,
            int progressEnd,
            string stageMessage)
        {
            var writeSamples = new List<SpeedSample>();
            var readSamples = new List<SpeedSample>();
            var reducedMode = false;

            var effectiveRemainders = Math.Clamp(maxRemainders, 1, modulo);
            for (var remainder = 0; remainder < effectiveRemainders; remainder++)
            {
                var progress = progressStart + ((progressEnd - progressStart) * (remainder + 1) / effectiveRemainders);
                UpdateProgress($"{stageMessage} ({remainder + 1}/{effectiveRemainders})...", progress);

                try
                {
                    var (writeChunk, readChunk) = await _diskCardRepository.GetSpeedSampleSeriesChunkAsync(sessionId, modulo, remainder);
                    writeSamples.AddRange(writeChunk);
                    readSamples.AddRange(readChunk);
                }
                catch (DbException ex) when (IsDiskFullLike(ex))
                {
                    reducedMode = true;
                    StatusMessage = "Detekován nízký prostor pro SQLite. Pokračuji s omezeným vzorkem dat.";
                    break;
                }
                catch (IOException)
                {
                    reducedMode = true;
                    StatusMessage = "Nedostatek místa na disku při čtení dat. Pokračuji s omezeným vzorkem.";
                    break;
                }

                await Task.Yield();
            }

            writeSamples = writeSamples.OrderBy(s => s.ProgressPercent).ToList();
            readSamples = readSamples.OrderBy(s => s.ProgressPercent).ToList();

            return (writeSamples, readSamples, reducedMode || effectiveRemainders < modulo);
        }

        /// <summary>
        /// Určí, zda výjimka signalizuje nedostatek místa v SQLite databázi nebo dočasném úložišti.
        /// </summary>
        private static bool IsDiskFullLike(Exception ex)
        {
            var message = ex.Message;
            return !string.IsNullOrWhiteSpace(message)
                && (message.Contains("database or disk is full", StringComparison.OrdinalIgnoreCase)
                    || message.Contains("disk is full", StringComparison.OrdinalIgnoreCase)
                    || message.Contains("SQLite Error 13", StringComparison.OrdinalIgnoreCase));
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
