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
        Certificates = new ObservableCollection<DiskCertificate>();
        SmartHistory = new ObservableCollection<SmartHistoryItem>();
        SpeedChartModel = new PlotModel { Title = L.Get("DiskCardDetail.Status.SpeedTest") };
        TemperatureChartModel = new PlotModel { Title = L.Get("DiskCardDetail.Status.TemperatureDuringTest") };
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

    public ObservableCollection<DiskCertificate> Certificates { get; }

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
        ? L.Get("CertificateView.Badge.Critical")
        : SelectedSessionHasWarningDiagnostics
            ? L.Get("CertificateView.Badge.Warning")
            : L.Get("CertificateView.Badge.Stable");

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

    public string DiskModel => CurrentCard?.ModelName ?? L.Get("Grade.Unknown");
    public string DiskSerial => CurrentCard?.SerialNumber ?? "-";
    public string DiskCapacity => FormatCapacity(CurrentCard?.Capacity ?? 0);
    public string DiskType => CurrentCard?.DiskType ?? L.Get("Grade.Unknown");
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
        StatusMessage = string.Format(L.Get("DiskCardDetail.Status.TestSelected"), session.TestType, session.StartedAt.ToString("dd.MM.yyyy HH:mm"));
    }

    [RelayCommand]
    private async Task GenerateCertificateAsync()
    {
        if (CurrentCard == null) return;

        try
        {
            IsLoading = true;
            StatusMessage = L.Get("DiskCardDetail.Status.GeneratingCert");

            // Get latest test session ID
            var sessions = await _diskCardRepository.GetTestSessionsAsync(CurrentCard.Id);
            var latestSession = sessions.FirstOrDefault();

            if (latestSession == null)
            {
                await _dialogService.ShowErrorAsync(L.Get("Common.Error"), L.Get("DiskCardDetail.Status.NoTestForCert"));
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
                await _dialogService.ShowErrorAsync(L.Get("Common.Error"), string.Format(L.Get("DiskCardDetail.Error.GenerateCert"), result.ErrorMessage));
                return;
            }

            // Update local state
            LatestCertificate = result.Certificate;
            StatusMessage = string.Format(L.Get("DiskCardDetail.Status.CertGenerated"), result.Certificate!.CertificateNumber);
            
            var openPdf = await _dialogService.ShowConfirmationAsync(
                L.Get("DiskCardDetail.Dialog.Certificate"),
                string.Format(L.Get("DiskCardDetail.Dialog.CertCreated"), result.Certificate.CertificateNumber, result.Certificate.Grade, result.Certificate.Score) +
                string.Format(L.Get("DiskCardDetail.Dialog.PdfSaved"), result.PdfPath));

            if (openPdf)
            {
                DocumentLauncher.OpenFile(result.PdfPath!);
            }
        }
        catch (Exception ex)
        {
            StatusMessage = string.Format(L.Get("Common.Error"), ex.Message);
            await _dialogService.ShowErrorAsync(L.Get("Common.Error"), string.Format(L.Get("DiskCardDetail.Error.GenerateCert"), ex.Message));
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
            StatusMessage = L.Get("DiskCardDetail.Status.CardNotLoaded");
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

        var note = await _dialogService.ShowInputDialogAsync(L.Get("DiskCardDetail.Dialog.NoteTitle"), L.Get("DiskCardDetail.Dialog.NotePrompt"), CurrentCard.Notes ?? "");
        if (note == null)
        {
            return;
        }

        CurrentCard.Notes = note;
        Notes = note;
        await _diskCardRepository.UpdateAsync(CurrentCard);
        StatusMessage = L.Get("DiskCardDetail.Dialog.NoteSaved");
    }

    [RelayCommand]
    private void BrowseAllCertificates()
    {
        _navigationService.NavigateTo<CertificateBrowserViewModel>();
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
            StatusMessage = L.Get("DiskCardDetail.Status.SearchingCard");

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
                StatusMessage = L.Get("DiskCardDetail.Status.NewCardCreated");
            }
            else
            {
                StatusMessage = L.Get("DiskCardDetail.Status.CardLoaded");
            }

            await LoadCardAsync(card.Id);
        }
        catch (Exception ex)
        {
            StatusMessage = string.Format(L.Get("Common.Error"), ex.Message);
            await _dialogService.ShowErrorAsync(L.Get("Common.Error"), string.Format(L.Get("DiskCardDetail.Error.LoadCard"), ex.Message));
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
            StatusMessage = L.Get("DiskCardDetail.Status.LoadingDetail");

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

                // Load all certificates for this disk
                var allCerts = await _diskCardRepository.GetCertificatesAsync(cardId);
                Certificates.Clear();
                foreach (var cert in allCerts.OrderByDescending(c => c.GeneratedAt))
                {
                    Certificates.Add(cert);
                }

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

                StatusMessage = string.Format(L.Get("DiskCardDetail.Status.CardLoadedName"), CurrentCard.ModelName);
            }
            else
            {
                StatusMessage = L.Get("DiskCardDetail.Status.CardNotFound");
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

    private string FormatCapacity(long bytes)
    {
        if (bytes <= 0) return L.Get("Grade.Unknown");
        var gb = bytes / (1024.0 * 1024.0 * 1024.0);
        if (gb >= 1000) return $"{gb / 1024.0:F2} TB";
        return $"{gb:F0} GB";
    }

    private void UpdateSelectedSessionDetails()
    {
        if (SelectedSession == null)
        {
            SelectedSessionSmartSummary = L.Get("DiskCardDetail.Status.SelectTestForSmart");
            SelectedSessionSmartJson = string.Empty;
            SelectedSessionDegradationSummary = L.Get("DiskCardDetail.Status.DegradationCompare");
            SelectedSessionErrorSummary = L.Get("DiskCardDetail.Status.SelectTestForErrors");
            SelectedSessionDiagnosticSummary = L.Get("DiskCardDetail.Status.SelectTestForDiagnostics");
            SelectedSessionDiagnosticHighlights = string.Empty;
            return;
        }

        SelectedSessionDiagnosticSummary = BuildDiagnosticSummary(SelectedSession);
        SelectedSessionDiagnosticHighlights = BuildDiagnosticHighlights(SelectedSession.Notes);

        var smartSnapshot = SelectedSession.SmartBefore;
        if (smartSnapshot == null)
        {
            SelectedSessionSmartSummary = L.Get("DiskCardDetail.Status.NoSmartSnapshot");
            SelectedSessionSmartJson = string.Empty;
            SelectedSessionDegradationSummary = L.Get("DiskCardDetail.Status.NoDegradationCompare");
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
    
    private string BuildErrorSummary(TestSession session)
    {
        if (session.Errors.Count == 0)
        {
            return L.Get("DiskCardDetail.Status.NoErrorsForTest");
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
            return L.Get("DiskCardDetail.Status.NoPreviousSmart");
        }

        var lines = new List<string>
        {
            string.Format(L.Get("DiskCardDetail.Status.ComparisonAgainst"), previousSession.StartedAt.ToString("dd.MM.yyyy HH:mm"))
        };

        AppendDelta(lines, "Power-On Hours", selectedSmart.PowerOnHours, previousSmart.PowerOnHours);
        AppendDelta(lines, "Reallocated Sector Count", selectedSmart.ReallocatedSectorCount, previousSmart.ReallocatedSectorCount);
        AppendDelta(lines, "Pending Sector Count", selectedSmart.PendingSectorCount, previousSmart.PendingSectorCount);
        AppendDelta(lines, "Uncorrectable Error Count", selectedSmart.UncorrectableErrorCount, previousSmart.UncorrectableErrorCount);
        AppendDelta(lines, "Media Errors", selectedSmart.MediaErrors, previousSmart.MediaErrors);
        AppendDelta(lines, "Percentage Used", selectedSmart.PercentageUsed, previousSmart.PercentageUsed, suffix: "%");

        return string.Join(Environment.NewLine, lines);
    }

    private void AppendDelta(List<string> lines, string name, int? current, int? previous, string suffix = "")
    {
        if (!current.HasValue || !previous.HasValue)
        {
            lines.Add($"\u2022 {name}: {L.Get("CertificatePdf.NA")}");
            return;
        }

        var delta = current.Value - previous.Value;
        var suffixWithSpace = string.IsNullOrEmpty(suffix) ? string.Empty : suffix;
        lines.Add($"• {name}: {current.Value}{suffixWithSpace} (Δ {delta:+#;-#;0}{suffixWithSpace})");
    }

    private string BuildSmartSummary(SmartaData snapshot)
    {
        var builder = new StringBuilder();
        builder.AppendLine($"Model: {snapshot.DeviceModel}");
        builder.AppendLine(string.Format(L.Get("DiskCardDetail.Status.SerialNumber"), snapshot.SerialNumber));
        builder.AppendLine($"Firmware: {snapshot.FirmwareVersion}");
        builder.AppendLine($"Teplota: {(snapshot.Temperature.HasValue ? $"{snapshot.Temperature}°C" : "N/A")}");
        builder.AppendLine($"Power-On Hours: {snapshot.PowerOnHours?.ToString() ?? "N/A"}");
        builder.AppendLine($"Reallocated: {snapshot.ReallocatedSectorCount?.ToString() ?? "N/A"}");
        builder.AppendLine($"Pending: {snapshot.PendingSectorCount?.ToString() ?? "N/A"}");
        builder.AppendLine($"Uncorrectable: {snapshot.UncorrectableErrorCount?.ToString() ?? "N/A"}");
        builder.Append($"Media Errors: {snapshot.MediaErrors?.ToString() ?? "N/A"}");
        return builder.ToString();
    }

    private string BuildDiagnosticSummary(TestSession session)
    {
        var notes = session.Notes ?? string.Empty;
        var flags = new List<string>();

        if (notes.Contains("kritické", StringComparison.OrdinalIgnoreCase) || notes.Contains("kritický", StringComparison.OrdinalIgnoreCase))
        {
            flags.Add(L.Get("DiskCardDetail.Status.CriticalSignals"));
        }

        if (notes.Contains("propad", StringComparison.OrdinalIgnoreCase))
        {
            flags.Add(L.Get("DiskCardDetail.Status.SpeedDrops"));
        }

        if (notes.Contains("nestabil", StringComparison.OrdinalIgnoreCase))
        {
            flags.Add(L.Get("DiskCardDetail.Status.UnstableProgress"));
        }

        if (notes.Contains("histor", StringComparison.OrdinalIgnoreCase))
        {
            flags.Add(L.Get("DiskCardDetail.Status.HistoryDeviation"));
        }

        if (notes.Contains("SMART", StringComparison.OrdinalIgnoreCase))
        {
            flags.Add(L.Get("DiskCardDetail.Status.SmartContext"));
        }

        if (flags.Count == 0)
        {
            return L.Get("DiskCardDetail.Status.NoDiagnosticSignals");
        }

        return L.Get("DiskCardDetail.Status.DetectedSignals") + string.Join(", ", flags) + ".";
    }

    private string BuildDiagnosticHighlights(string? notes)
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

    private bool ContainsDiagnosticMarker(string? notes, string marker)
    {
        return !string.IsNullOrWhiteSpace(notes) && notes.Contains(marker, StringComparison.OrdinalIgnoreCase);
    }

    private string BuildHistoryFlags(string? notes)
    {
        if (string.IsNullOrWhiteSpace(notes))
        {
            return L.Get("DiskCardDetail.Status.Ok");
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

        return flags.Count == 0 ? L.Get("DiskCardDetail.Status.Ok") : string.Join(" | ", flags);
    }

    private string FormatSmartJson(string json)
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