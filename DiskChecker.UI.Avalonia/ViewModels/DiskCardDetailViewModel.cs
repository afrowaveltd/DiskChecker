using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DiskChecker.Application.Services;
using DiskChecker.Core.Interfaces;
using DiskChecker.Core.Models;
using DiskChecker.UI.Avalonia.Services;
using DiskChecker.UI.Avalonia.Services.Interfaces;
using OxyPlot;
using OxyPlot.Axes;
using OxyPlot.Series;

namespace DiskChecker.UI.Avalonia.ViewModels;

/// <summary>
/// View model for displaying detailed disk card information.
/// </summary>
public partial class DiskCardDetailViewModel : ViewModelBase, INavigableViewModel
{
    private static readonly JsonSerializerOptions SmartJsonFormattingOptions = new()
    {
        WriteIndented = true
    };

    private readonly IDiskCardRepository _diskCardRepository;
    private readonly INavigationService _navigationService;
    private readonly IDialogService _dialogService;
    private readonly IDiskComparisonService _comparisonService;
    private readonly ICertificateGenerator _certificateGenerator;
    private readonly ISelectedDiskService _selectedDiskService;
    private readonly CertificateExportService _certificateExportService;
    
    private DiskCard? _currentCard;
    private TestSession? _selectedSession;
    private DiskCertificate? _latestCertificate;
    private bool _isLoading;
    private string _statusMessage = "Detail karty disku";

    private string _notes = string.Empty;
    private string _selectedSessionSmartSummary = "Vyberte test pro zobrazení SMART snapshotu.";
    private string _selectedSessionSmartJson = string.Empty;
    private string _selectedSessionDegradationSummary = "Porovnání degradace bude dostupné po výběru testu.";
    private string _selectedSessionErrorSummary = "Vyberte test pro zobrazení detailu chyb.";
    private string _selectedSessionDiagnosticSummary = "Vyberte test pro diagnostický rozbor průběhu výkonu.";
    private string _selectedSessionDiagnosticHighlights = string.Empty;

    public DiskCardDetailViewModel(
        IDiskCardRepository diskCardRepository,
        INavigationService navigationService,
        IDialogService dialogService,
        IDiskComparisonService comparisonService,
        ICertificateGenerator certificateGenerator,
        ISelectedDiskService selectedDiskService,
        CertificateExportService certificateExportService)
    {
        _diskCardRepository = diskCardRepository;
        _navigationService = navigationService;
        _dialogService = dialogService;
        _comparisonService = comparisonService;
        _certificateGenerator = certificateGenerator;
        _selectedDiskService = selectedDiskService;
        _certificateExportService = certificateExportService;
        
        TestSessions = new ObservableCollection<TestSession>();
        SmartHistory = new ObservableCollection<SmartHistoryItem>();
        SpeedChartModel = new PlotModel { Title = "Rychlost testu" };
        TemperatureChartModel = new PlotModel { Title = "Teplota během testu" };
    }

    #region Properties

    public DiskCard? CurrentCard
    {
        get => _currentCard;
        private set
        {
            if (SetProperty(ref _currentCard, value))
            {
                OnPropertyChanged(nameof(HasCard));
                OnPropertyChanged(nameof(DiskModel));
                OnPropertyChanged(nameof(DiskSerial));
                OnPropertyChanged(nameof(DiskCapacity));
                OnPropertyChanged(nameof(DiskType));
                OnPropertyChanged(nameof(DiskInterface));
                OnPropertyChanged(nameof(DiskFirmware));
                OnPropertyChanged(nameof(OverallGrade));
                OnPropertyChanged(nameof(OverallScore));
                OnPropertyChanged(nameof(TestCount));
                OnPropertyChanged(nameof(LastTested));
                OnPropertyChanged(nameof(IsLocked));
                OnPropertyChanged(nameof(IsArchived));
            }
        }
    }

    public TestSession? SelectedSession
    {
        get => _selectedSession;
        set
        {
            if (SetProperty(ref _selectedSession, value))
            {
                UpdateSelectedSessionDetails();
                OnPropertyChanged(nameof(HasSelectedSession));
                OnPropertyChanged(nameof(SelectedSessionHasCriticalDiagnostics));
                OnPropertyChanged(nameof(SelectedSessionHasWarningDiagnostics));
                OnPropertyChanged(nameof(SelectedSessionDiagnosticBadgeText));
                OnPropertyChanged(nameof(SelectedSessionDiagnosticBadgeBackground));
                OnPropertyChanged(nameof(SelectedSessionDiagnosticBadgeForeground));
                OnPropertyChanged(nameof(SelectedSessionHistoryFlags));
            }
        }
    }

    public DiskCertificate? LatestCertificate
    {
        get => _latestCertificate;
        private set => SetProperty(ref _latestCertificate, value);
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

    public ObservableCollection<TestSession> TestSessions { get; }

    public ObservableCollection<SmartHistoryItem> SmartHistory { get; }

    public PlotModel SpeedChartModel { get; }
    public PlotModel TemperatureChartModel { get; }

    public string Notes
    {
        get => _notes;
        set => SetProperty(ref _notes, value);
    }

    public string SelectedSessionSmartSummary
    {
        get => _selectedSessionSmartSummary;
        private set => SetProperty(ref _selectedSessionSmartSummary, value);
    }

    public string SelectedSessionSmartJson
    {
        get => _selectedSessionSmartJson;
        private set
        {
            if (SetProperty(ref _selectedSessionSmartJson, value))
            {
                OnPropertyChanged(nameof(HasSelectedSessionSmartJson));
            }
        }
    }

    public bool HasSelectedSessionSmartJson => !string.IsNullOrWhiteSpace(SelectedSessionSmartJson);

    public string SelectedSessionDegradationSummary
    {
        get => _selectedSessionDegradationSummary;
        private set => SetProperty(ref _selectedSessionDegradationSummary, value);
    }

    public string SelectedSessionErrorSummary
    {
        get => _selectedSessionErrorSummary;
        private set => SetProperty(ref _selectedSessionErrorSummary, value);
    }

    /// <summary>
    /// Gets high-level diagnostics summary for the selected session.
    /// </summary>
    public string SelectedSessionDiagnosticSummary
    {
        get => _selectedSessionDiagnosticSummary;
        private set => SetProperty(ref _selectedSessionDiagnosticSummary, value);
    }

    /// <summary>
    /// Gets diagnostic highlights extracted from session notes.
    /// </summary>
    public string SelectedSessionDiagnosticHighlights
    {
        get => _selectedSessionDiagnosticHighlights;
        private set
        {
            if (SetProperty(ref _selectedSessionDiagnosticHighlights, value))
            {
                OnPropertyChanged(nameof(HasSelectedSessionDiagnosticHighlights));
            }
        }
    }

    public bool HasSelectedSessionDiagnosticHighlights => !string.IsNullOrWhiteSpace(SelectedSessionDiagnosticHighlights);

    /// <summary>
    /// Gets whether the selected session contains critical diagnostic signals.
    /// </summary>
    public bool SelectedSessionHasCriticalDiagnostics => ContainsDiagnosticMarker(SelectedSession?.Notes, "kritické") || ContainsDiagnosticMarker(SelectedSession?.Notes, "kritický");

    /// <summary>
    /// Gets whether the selected session contains warning-level diagnostic signals.
    /// </summary>
    public bool SelectedSessionHasWarningDiagnostics =>
        ContainsDiagnosticMarker(SelectedSession?.Notes, "propad") ||
        ContainsDiagnosticMarker(SelectedSession?.Notes, "nestabil") ||
        ContainsDiagnosticMarker(SelectedSession?.Notes, "histor");

    public string SelectedSessionDiagnosticBadgeText => SelectedSessionHasCriticalDiagnostics
        ? "KRITICKÉ SIGNÁLY"
        : SelectedSessionHasWarningDiagnostics
            ? "VAROVNÉ SIGNÁLY"
            : "STABILNÍ PRŮBĚH";

    public string SelectedSessionDiagnosticBadgeBackground => SelectedSessionHasCriticalDiagnostics
        ? "#FDECEC"
        : SelectedSessionHasWarningDiagnostics
            ? "#FFF4DB"
            : "#EAF7EE";

    public string SelectedSessionDiagnosticBadgeForeground => SelectedSessionHasCriticalDiagnostics
        ? "#B42318"
        : SelectedSessionHasWarningDiagnostics
            ? "#B54708"
            : "#027A48";

    public string SelectedSessionHistoryFlags => BuildHistoryFlags(SelectedSession?.Notes);

    // Compatibility aliases for XAML bindings
    public DiskCard? Card => CurrentCard;
    public bool HasTestSessions => TestSessions.Count > 0;
    public IRelayCommand NavigateBackCommand => GoBackCommand;
    public IAsyncRelayCommand SaveNotesCommand => AddNoteCommand;

    public string GradeColor => (CurrentCard?.OverallGrade ?? "?") switch
    {
        "A" => "#27AE60",
        "B" => "#2ECC71",
        "C" => "#F39C12",
        "D" => "#E67E22",
        "E" => "#E74C3C",
        "F" => "#C0392B",
        _ => "#95A5A6"
    };

    public double AverageScore => TestSessions.Count == 0 ? 0 : TestSessions.Average(s => s.Score);

    public string BestGrade => TestSessions.Count == 0
        ? "-"
        : TestSessions.OrderByDescending(s => s.Score).First().Grade;

    public string WorstGrade => TestSessions.Count == 0
        ? "-"
        : TestSessions.OrderBy(s => s.Score).First().Grade;

    public long TotalSectorsTested => TestSessions.Sum(s => s.SectorsTested);

    public int TotalErrors => TestSessions.Sum(s => s.ErrorCount);

    // Computed properties
    public bool HasCard => CurrentCard != null;
    public bool HasSelectedSession => SelectedSession != null;

    public string DiskModel => CurrentCard?.ModelName ?? "Neznámý";
    public string DiskSerial => CurrentCard?.SerialNumber ?? "-";
    public string DiskCapacity => FormatCapacity(CurrentCard?.Capacity ?? 0);
    public string DiskType => CurrentCard?.DiskType ?? "Neznámý";
    public string DiskInterface => CurrentCard?.InterfaceType ?? "-";
    public string DiskFirmware => CurrentCard?.FirmwareVersion ?? "-";
    public string OverallGrade => CurrentCard?.OverallGrade ?? "?";
    public string OverallScore => CurrentCard != null ? $"{CurrentCard.OverallScore:F1}" : "-";
    public string TestCount => CurrentCard?.TestCount.ToString() ?? "0";
    public string LastTested => CurrentCard?.LastTestedAt.ToString("dd.MM.yyyy HH:mm") ?? "-";
    public bool IsLocked => CurrentCard?.IsLocked ?? false;
    public bool IsArchived => CurrentCard?.IsArchived ?? false;

    #endregion

    #region Navigation

    public void OnNavigatedTo()
    {
        // Load card based on selected disk service
        if (_selectedDiskService.SelectedDisk != null)
        {
            // Try to find existing card or create new one for the disk
            _ = LoadCardForDiskAsync(_selectedDiskService.SelectedDisk);
        }
    }

    #endregion

    #region Commands

    [RelayCommand]
    private async Task RefreshAsync()
    {
        if (CurrentCard != null)
        {
            await LoadCardAsync(CurrentCard.Id);
        }
    }

    [RelayCommand]
    private void ViewSession(TestSession session)
    {
        if (session == null) return;
        SelectedSession = session;
        StatusMessage = $"Test: {session.TestType} - {session.StartedAt:dd.MM.yyyy HH:mm}";
    }

    [RelayCommand]
    private async Task GenerateCertificateAsync()
    {
        if (CurrentCard == null) return;

        try
        {
            IsLoading = true;
            StatusMessage = "Generuji certifikát...";

            // Get latest test session ID
            var sessions = await _diskCardRepository.GetTestSessionsAsync(CurrentCard.Id);
            var latestSession = sessions.FirstOrDefault();

            if (latestSession == null)
            {
                await _dialogService.ShowErrorAsync("Chyba", "Neexistuje žádný test pro tento disk.");
                return;
            }

            // Use centralized export service with automatic downsampling and progress reporting
            var progress = new Progress<CertificateExportProgress>(p =>
            {
                StatusMessage = p.Message;
            });

            var result = await _certificateExportService.ExportCertificateAsync(
                latestSession.Id,
                progress);

            if (!result.IsSuccess)
            {
                await _dialogService.ShowErrorAsync("Chyba", $"Nepodařilo se vygenerovat certifikát: {result.ErrorMessage}");
                return;
            }

            // Update local state
            LatestCertificate = result.Certificate;
            StatusMessage = $"Certifikát vygenerován: {result.Certificate!.CertificateNumber}";
            
            var openPdf = await _dialogService.ShowConfirmationAsync(
                "Certifikát",
                $"Certifikát vytvořen:\n{result.Certificate.CertificateNumber}\n\nZnámka: {result.Certificate.Grade}\nSkóre: {result.Certificate.Score:F0}/100\n\n" +
                $"PDF uloženo:\n{result.PdfPath}\n\nOtevřít PDF?");

            if (openPdf)
            {
                DocumentLauncher.OpenFile(result.PdfPath!);
            }
        }
        catch (Exception ex)
        {
            StatusMessage = $"Chyba: {ex.Message}";
            await _dialogService.ShowErrorAsync("Chyba", $"Nepodařilo se vygenerovat certifikát: {ex.Message}");
        }
        finally
        {
            IsLoading = false;
        }
    }

    [RelayCommand]
    private void OpenCertificateViewer()
    {
        if (CurrentCard == null)
        {
            StatusMessage = "Karta disku není načtena.";
            return;
        }

        _selectedDiskService.SelectedDisk = new CoreDriveInfo
        {
            Path = CurrentCard.DevicePath,
            Name = CurrentCard.ModelName,
            TotalSize = CurrentCard.Capacity,
            SerialNumber = CurrentCard.SerialNumber,
            FirmwareVersion = CurrentCard.FirmwareVersion
        };
        _selectedDiskService.SelectedDiskDisplayName = CurrentCard.ModelName;
        _selectedDiskService.IsSelectedDiskLocked = CurrentCard.IsLocked;
        _selectedDiskService.SelectedTestSessionId = SelectedSession?.Id ?? TestSessions.FirstOrDefault()?.Id;
        _selectedDiskService.SelectedCertificateId = LatestCertificate?.Id;

        _navigationService.NavigateTo<CertificateViewModel>();
    }

    [RelayCommand]
    private async Task CompareWithOtherAsync()
    {
        if (CurrentCard == null) return;
        _navigationService.NavigateTo<DiskComparisonViewModel>();
    }

    [RelayCommand]
    private async Task AddNoteAsync()
    {
        if (CurrentCard == null) return;

        var note = await _dialogService.ShowInputDialogAsync("Poznámka", "Zadejte poznámku:", CurrentCard.Notes ?? "");
        if (note == null)
        {
            return;
        }

        CurrentCard.Notes = note;
        Notes = note;
        await _diskCardRepository.UpdateAsync(CurrentCard);
        StatusMessage = "Poznámka uložena";
    }

    [RelayCommand]
    private void GoBack()
    {
        _navigationService.NavigateTo<DiskCardsViewModel>();
    }

    #endregion

    #region Private Methods

    private async Task LoadCardForDiskAsync(CoreDriveInfo disk)
    {
        try
        {
            IsLoading = true;
            StatusMessage = "Hledám kartu disku...";

            var identityKey = DriveIdentityResolver.BuildIdentityKey(
                disk.Path,
                disk.SerialNumber,
                disk.Name ?? "Unknown",
                disk.FirmwareVersion);
            var legacyKey = BuildLegacyIdentityKey(
                disk.Path,
                disk.SerialNumber,
                disk.Name ?? "Unknown",
                disk.FirmwareVersion);

            DiskCard? card = await _diskCardRepository.GetBySerialNumberAsync(identityKey)
                           ?? await _diskCardRepository.GetBySerialNumberAsync(legacyKey)
                           ?? await _diskCardRepository.GetByDevicePathAsync(disk.Path);

            if (card == null)
            {
                // Create new card
                card = new DiskCard
                {
                    ModelName = disk.Name ?? "Unknown",
                    SerialNumber = identityKey,
                    DevicePath = disk.Path,
                    Capacity = disk.TotalSize,
                    FirmwareVersion = disk.FirmwareVersion ?? string.Empty,
                    DiskType = "Unknown",
                    InterfaceType = disk.BusType.ToString(),
                    IsLocked = _selectedDiskService.IsSelectedDiskLocked
                };

                card = await _diskCardRepository.CreateAsync(card);
                StatusMessage = "Vytvořena nová karta disku";
            }
            else
            {
                StatusMessage = "Karta disku načtena";
            }

            await LoadCardAsync(card.Id);
        }
        catch (Exception ex)
        {
            StatusMessage = $"Chyba: {ex.Message}";
            await _dialogService.ShowErrorAsync("Chyba", $"Nepodařilo se načíst kartu: {ex.Message}");
        }
        finally
        {
            IsLoading = false;
        }
    }

    private async Task LoadCardAsync(int cardId)
    {
        try
        {
            IsLoading = true;
            StatusMessage = "Načítám detail...";

            CurrentCard = await _diskCardRepository.GetByIdAsync(cardId);

            if (CurrentCard != null)
            {
                // Load test sessions
                var sessions = await _diskCardRepository.GetTestSessionsAsync(cardId);
                TestSessions.Clear();
                SmartHistory.Clear();

                foreach (var session in sessions.OrderByDescending(s => s.StartedAt))
                {
                    TestSessions.Add(session);

                    var smartSnapshot = session.SmartBefore;
                    SmartHistory.Add(new SmartHistoryItem
                    {
                        TestedAt = session.StartedAt,
                        Temperature = smartSnapshot?.Temperature ?? session.Temperature,
                        PowerOnHours = smartSnapshot?.PowerOnHours ?? 0,
                        ReallocatedSectors = smartSnapshot?.ReallocatedSectorCount ?? 0,
                        PendingSectors = smartSnapshot?.PendingSectorCount ?? 0,
                        ReadErrors = smartSnapshot?.UncorrectableErrorCount ?? session.ReadErrors,
                        ReallocEvents = smartSnapshot?.MediaErrors ?? session.VerificationErrors,
                        Grade = session.Grade,
                        Score = session.Score,
                        Notes = session.Notes ?? string.Empty
                    });
                }

                Notes = CurrentCard.Notes ?? string.Empty;

                // Load latest certificate
                LatestCertificate = await _diskCardRepository.GetLatestCertificateAsync(cardId);

                OnPropertyChanged(nameof(Card));
                OnPropertyChanged(nameof(HasTestSessions));
                OnPropertyChanged(nameof(GradeColor));
                OnPropertyChanged(nameof(AverageScore));
                OnPropertyChanged(nameof(BestGrade));
                OnPropertyChanged(nameof(WorstGrade));
                OnPropertyChanged(nameof(TotalSectorsTested));
                OnPropertyChanged(nameof(TotalErrors));

                if (SelectedSession == null)
                {
                    SelectedSession = TestSessions.FirstOrDefault();
                }

                StatusMessage = $"Karta načtena: {CurrentCard.ModelName}";
            }
            else
            {
                StatusMessage = "Karta nenalezena";
            }
        }
        finally
        {
            IsLoading = false;
        }
    }

    private static string BuildLegacyIdentityKey(string drivePath, string? serialNumber, string deviceModel, string? firmwareVersion)
    {
        if (!string.IsNullOrWhiteSpace(serialNumber))
        {
            return $"{serialNumber}_{deviceModel}_{firmwareVersion ?? "unknown"}";
        }

        if (!string.IsNullOrWhiteSpace(deviceModel))
        {
            return $"{deviceModel}_{drivePath.Replace("\\", "_").Replace("/", "_")}_{firmwareVersion ?? "unknown"}";
        }

        return drivePath;
    }

    private static string FormatCapacity(long bytes)
    {
        if (bytes <= 0) return "Neznámý";
        var gb = bytes / (1024.0 * 1024.0 * 1024.0);
        if (gb >= 1000) return $"{gb / 1024.0:F2} TB";
        return $"{gb:F0} GB";
    }

    private void UpdateSelectedSessionDetails()
    {
        if (SelectedSession == null)
        {
            SelectedSessionSmartSummary = "Vyberte test pro zobrazení SMART snapshotu.";
            SelectedSessionSmartJson = string.Empty;
            SelectedSessionDegradationSummary = "Porovnání degradace bude dostupné po výběru testu.";
            SelectedSessionErrorSummary = "Vyberte test pro zobrazení detailu chyb.";
            SelectedSessionDiagnosticSummary = "Vyberte test pro diagnostický rozbor průběhu výkonu.";
            SelectedSessionDiagnosticHighlights = string.Empty;
            return;
        }

        SelectedSessionDiagnosticSummary = BuildDiagnosticSummary(SelectedSession);
        SelectedSessionDiagnosticHighlights = BuildDiagnosticHighlights(SelectedSession.Notes);

        var smartSnapshot = SelectedSession.SmartBefore;
        if (smartSnapshot == null)
        {
            SelectedSessionSmartSummary = "K vybranému testu není uložen SMART snapshot. Test ale může být jinak platný a hodnocení tím není zhoršeno.";
            SelectedSessionSmartJson = string.Empty;
            SelectedSessionDegradationSummary = "Nelze porovnat degradaci bez SMART snapshotu.";
            SelectedSessionErrorSummary = BuildErrorSummary(SelectedSession);
            return;
        }

        SelectedSessionSmartSummary = BuildSmartSummary(smartSnapshot);
        SelectedSessionSmartJson = !string.IsNullOrWhiteSpace(SelectedSession.SmartBeforeJson)
            ? FormatSmartJson(SelectedSession.SmartBeforeJson)
            : string.Empty;
        SelectedSessionDegradationSummary = BuildDegradationSummary(SelectedSession, smartSnapshot);
        SelectedSessionErrorSummary = BuildErrorSummary(SelectedSession);
        SelectedSessionDiagnosticSummary = BuildDiagnosticSummary(SelectedSession);
        SelectedSessionDiagnosticHighlights = BuildDiagnosticHighlights(SelectedSession.Notes);
    }
    
    private static string BuildErrorSummary(TestSession session)
    {
        if (session.Errors.Count == 0)
        {
            return "Pro vybraný test nejsou evidované detailní chyby.";
        }

        var builder = new StringBuilder();
        foreach (var error in session.Errors.OrderBy(e => e.Timestamp))
        {
            builder.AppendLine($"[{error.Phase}] {error.ErrorCode}: {error.Message}");
            if (!string.IsNullOrWhiteSpace(error.Details))
            {
                builder.AppendLine($"  {error.Details}");
            }
        }

        return builder.ToString().TrimEnd();
    }

    private string BuildDegradationSummary(TestSession selectedSession, SmartaData selectedSmart)
    {
        var previousSession = TestSessions
            .Where(s => s.Id != selectedSession.Id && s.StartedAt < selectedSession.StartedAt)
            .OrderByDescending(s => s.StartedAt)
            .FirstOrDefault();

        var previousSmart = previousSession?.SmartBefore;
        if (previousSession == null || previousSmart == null)
        {
            return "K vybranému testu není starší SMART snapshot pro porovnání degradace.";
        }

        var lines = new List<string>
        {
            $"Porovnání proti testu {previousSession.StartedAt:dd.MM.yyyy HH:mm}:"
        };

        AppendDelta(lines, "Power-On Hours", selectedSmart.PowerOnHours, previousSmart.PowerOnHours);
        AppendDelta(lines, "Reallocated Sector Count", selectedSmart.ReallocatedSectorCount, previousSmart.ReallocatedSectorCount);
        AppendDelta(lines, "Pending Sector Count", selectedSmart.PendingSectorCount, previousSmart.PendingSectorCount);
        AppendDelta(lines, "Uncorrectable Error Count", selectedSmart.UncorrectableErrorCount, previousSmart.UncorrectableErrorCount);
        AppendDelta(lines, "Media Errors", selectedSmart.MediaErrors, previousSmart.MediaErrors);
        AppendDelta(lines, "Percentage Used", selectedSmart.PercentageUsed, previousSmart.PercentageUsed, suffix: "%");

        return string.Join(Environment.NewLine, lines);
    }

    private static void AppendDelta(List<string> lines, string name, int? current, int? previous, string suffix = "")
    {
        if (!current.HasValue || !previous.HasValue)
        {
            lines.Add($"• {name}: N/A");
            return;
        }

        var delta = current.Value - previous.Value;
        var suffixWithSpace = string.IsNullOrEmpty(suffix) ? string.Empty : suffix;
        lines.Add($"• {name}: {current.Value}{suffixWithSpace} (Δ {delta:+#;-#;0}{suffixWithSpace})");
    }

    private static string BuildSmartSummary(SmartaData snapshot)
    {
        var builder = new StringBuilder();
        builder.AppendLine($"Model: {snapshot.DeviceModel}");
        builder.AppendLine($"Sériové číslo: {snapshot.SerialNumber}");
        builder.AppendLine($"Firmware: {snapshot.FirmwareVersion}");
        builder.AppendLine($"Teplota: {(snapshot.Temperature.HasValue ? $"{snapshot.Temperature}°C" : "N/A")}");
        builder.AppendLine($"Power-On Hours: {snapshot.PowerOnHours?.ToString() ?? "N/A"}");
        builder.AppendLine($"Reallocated: {snapshot.ReallocatedSectorCount?.ToString() ?? "N/A"}");
        builder.AppendLine($"Pending: {snapshot.PendingSectorCount?.ToString() ?? "N/A"}");
        builder.AppendLine($"Uncorrectable: {snapshot.UncorrectableErrorCount?.ToString() ?? "N/A"}");
        builder.Append($"Media Errors: {snapshot.MediaErrors?.ToString() ?? "N/A"}");
        return builder.ToString();
    }

    private static string BuildDiagnosticSummary(TestSession session)
    {
        var notes = session.Notes ?? string.Empty;
        var flags = new List<string>();

        if (notes.Contains("kritické", StringComparison.OrdinalIgnoreCase) || notes.Contains("kritický", StringComparison.OrdinalIgnoreCase))
        {
            flags.Add("kritické signály výkonu");
        }

        if (notes.Contains("propad", StringComparison.OrdinalIgnoreCase))
        {
            flags.Add("výrazné propady rychlosti");
        }

        if (notes.Contains("nestabil", StringComparison.OrdinalIgnoreCase))
        {
            flags.Add("nestabilní průběh");
        }

        if (notes.Contains("histor", StringComparison.OrdinalIgnoreCase))
        {
            flags.Add("odchylka od historie");
        }

        if (notes.Contains("SMART", StringComparison.OrdinalIgnoreCase))
        {
            flags.Add("SMART kontext");
        }

        if (flags.Count == 0)
        {
            return "Diagnostika neodhalila výrazné signály mimo standardní výsledek testu.";
        }

        return "Detekované diagnostické signály: " + string.Join(", ", flags) + ".";
    }

    private static string BuildDiagnosticHighlights(string? notes)
    {
        if (string.IsNullOrWhiteSpace(notes))
        {
            return string.Empty;
        }

        var parts = notes
            .Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(p =>
                p.Contains("propad", StringComparison.OrdinalIgnoreCase) ||
                p.Contains("kritické", StringComparison.OrdinalIgnoreCase) ||
                p.Contains("nestabil", StringComparison.OrdinalIgnoreCase) ||
                p.Contains("histor", StringComparison.OrdinalIgnoreCase) ||
                p.Contains("SMART", StringComparison.OrdinalIgnoreCase) ||
                p.Contains("thermal", StringComparison.OrdinalIgnoreCase) ||
                p.Contains("teplot", StringComparison.OrdinalIgnoreCase))
            .Distinct(StringComparer.Ordinal)
            .ToList();

        if (parts.Count == 0)
        {
            parts = notes
                .Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Take(4)
                .ToList();
        }

        return string.Join(Environment.NewLine, parts.Select(p => $"• {p}"));
    }

    private static bool ContainsDiagnosticMarker(string? notes, string marker)
    {
        return !string.IsNullOrWhiteSpace(notes) && notes.Contains(marker, StringComparison.OrdinalIgnoreCase);
    }

    private static string BuildHistoryFlags(string? notes)
    {
        if (string.IsNullOrWhiteSpace(notes))
        {
            return "OK";
        }

        var flags = new List<string>();
        if (notes.Contains("kritické", StringComparison.OrdinalIgnoreCase) || notes.Contains("kritický", StringComparison.OrdinalIgnoreCase))
        {
            flags.Add("KRIT");
        }

        if (notes.Contains("propad", StringComparison.OrdinalIgnoreCase))
        {
            flags.Add("DROP");
        }

        if (notes.Contains("nestabil", StringComparison.OrdinalIgnoreCase))
        {
            flags.Add("CV");
        }

        if (notes.Contains("histor", StringComparison.OrdinalIgnoreCase))
        {
            flags.Add("HIST");
        }

        if (notes.Contains("SMART", StringComparison.OrdinalIgnoreCase))
        {
            flags.Add("SMART");
        }

        return flags.Count == 0 ? "OK" : string.Join(" | ", flags);
    }

    private static string FormatSmartJson(string json)
    {
        try
        {
            using var document = JsonDocument.Parse(json);
            return JsonSerializer.Serialize(document.RootElement, SmartJsonFormattingOptions);
        }
        catch (JsonException)
        {
            return json;
        }
    }

    #endregion
}