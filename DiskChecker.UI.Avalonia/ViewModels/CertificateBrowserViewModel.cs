using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using System.Text.Json;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DiskChecker.Core.Interfaces;
using DiskChecker.Core.Models;
using DiskChecker.UI.Avalonia.Services.Interfaces;
using LiveChartsCore.Defaults;

namespace DiskChecker.UI.Avalonia.ViewModels;

/// <summary>
/// Prohlížeč všech uložených certifikátů s rekonstrukcí grafů a detailů.
/// </summary>
public partial class CertificateBrowserViewModel : ViewModelBase, INavigableViewModel
{
    private const int GraphTargetPoints = 1024;
    private const double ChartStartX = 56d;
    private const double ChartEndX = 886d;
    private const double ChartMinY = 18d;
    private const double ChartMaxY = 226d;

    private readonly IDiskCardRepository _diskCardRepository;
    private readonly INavigationService _navigationService;
    private readonly IDialogService _dialogService;
    private readonly ICertificateGenerator _certificateGenerator;
    private readonly ISelectedDiskService? _selectedDiskService;

    private ObservableCollection<CertificateListItem> _allCertificates = new();
    private ObservableCollection<CertificateListItem> _filteredCertificates = new();
    private CertificateListItem? _selectedListItem;
    private DiskCertificate? _selectedCertificate;
    private int _detailSelectionGeneration;
    private string _technicianAssessmentDraft = string.Empty;
    private TestSession? _selectedSession;
    private bool _isLoading;
    private string _statusMessage = "Loading certificates...";
    private string _searchText = string.Empty;
    private string _selectedGradeFilter = string.Empty;
    private int _totalCertificateCount;

    // Graph properties
    private string _writeProfilePoints = CertificateGraphData.Default.WriteProfilePoints;
    private string _readProfilePoints = CertificateGraphData.Default.ReadProfilePoints;
    private string _temperatureProfilePoints = CertificateGraphData.Default.TemperatureProfilePoints;
    private bool _hasTemperatureProfile;
    private string _chartMaxSpeedLabel = CertificateGraphData.Default.ChartMaxSpeedLabel;
    private string _chartMidSpeedLabel = CertificateGraphData.Default.ChartMidSpeedLabel;
    private string _chartMinSpeedLabel = CertificateGraphData.Default.ChartMinSpeedLabel;
    private string _chartXAxisStartLabel = CertificateGraphData.Default.ChartXAxisStartLabel;
    private string _chartXAxisMidLabel = CertificateGraphData.Default.ChartXAxisMidLabel;
    private string _chartXAxisEndLabel = CertificateGraphData.Default.ChartXAxisEndLabel;
    private ObservableCollection<ObservablePoint> _seekScatterPoints = new();
    private string _seekFullStrokePoints = string.Empty;
    private string _seekRandomPoints = string.Empty;
    private string _seekSkipPoints = string.Empty;
    private string _seekLegacyPoints = string.Empty;
    private string _temperatureMaxLabel = "N/A";
    private string _temperatureMidLabel = "N/A";
    private string _temperatureMinLabel = "N/A";
    private string _seekChartMaxLabel = "0 ms";
    private string _seekChartMidLabel = "0 ms";
    private string _seekChartMinLabel = "0 ms";
    private string _seekChartXAxisStartLabel = "1";
    private string _seekChartXAxisMidLabel = "-";
    private string _seekChartXAxisEndLabel = "-";

    public CertificateBrowserViewModel(
        IDiskCardRepository diskCardRepository,
        INavigationService navigationService,
        IDialogService dialogService,
        ICertificateGenerator certificateGenerator,
        ISelectedDiskService? selectedDiskService = null)
    {
        _diskCardRepository = diskCardRepository;
        _navigationService = navigationService;
        _dialogService = dialogService;
        _certificateGenerator = certificateGenerator;
        _selectedDiskService = selectedDiskService ?? App.GetService<ISelectedDiskService>();

        GradeFilters = new ObservableCollection<string> { L.Get("CertificateBrowser.Status.All"), "A", "B", "C", "D", "E", "F" };
    }

    #region Properties

    public ObservableCollection<CertificateListItem> AllCertificates
    {
        get => _allCertificates;
        set => SetProperty(ref _allCertificates, value);
    }

    public ObservableCollection<CertificateListItem> FilteredCertificates
    {
        get => _filteredCertificates;
        set => SetProperty(ref _filteredCertificates, value);
    }

    public CertificateListItem? SelectedListItem
    {
        get => _selectedListItem;
        set
        {
            if (SetProperty(ref _selectedListItem, value))
            {
                int generation = ++_detailSelectionGeneration;
                if (value != null)
                    _ = LoadCertificateDetailAsync(value.Id, generation);
                else
                {
                    SelectedCertificate = null;
                    ResetGraphToDefaults();
                }
            }
        }
    }

    public DiskCertificate? SelectedCertificate
    {
        get => _selectedCertificate;
        private set
        {
            if (SetProperty(ref _selectedCertificate, value))
            {
                OnPropertyChanged(nameof(HasSelectedCertificate));
                OnPropertyChanged(nameof(CertificateNumber));
                OnPropertyChanged(nameof(DiskModel));
                OnPropertyChanged(nameof(SerialNumber));
                OnPropertyChanged(nameof(Capacity));
                OnPropertyChanged(nameof(DiskType));
                OnPropertyChanged(nameof(Firmware));
                OnPropertyChanged(nameof(Interface));
                OnPropertyChanged(nameof(Grade));
                OnPropertyChanged(nameof(Score));
                OnPropertyChanged(nameof(HealthStatus));
                OnPropertyChanged(nameof(TestType));
                OnPropertyChanged(nameof(TestDuration));
                OnPropertyChanged(nameof(AvgWriteSpeed));
                OnPropertyChanged(nameof(AvgReadSpeed));
                OnPropertyChanged(nameof(MaxWriteSpeed));
                OnPropertyChanged(nameof(MaxReadSpeed));
                OnPropertyChanged(nameof(TemperatureRange));
                OnPropertyChanged(nameof(Errors));
                OnPropertyChanged(nameof(SmartPassed));
                OnPropertyChanged(nameof(SmartPowerOnHoursText));
                OnPropertyChanged(nameof(SmartPowerCyclesText));
                OnPropertyChanged(nameof(SmartReallocatedSectorsText));
                OnPropertyChanged(nameof(SmartPendingSectorsText));
                OnPropertyChanged(nameof(Recommended));
                OnPropertyChanged(nameof(RecommendationText));
                TechnicianAssessmentDraft = value?.TechnicianAssessment ?? string.Empty;
                OnPropertyChanged(nameof(GeneratedAtText));
                OnPropertyChanged(nameof(GeneratedBy));
                OnPropertyChanged(nameof(HasSeekMetrics));
                OnPropertyChanged(nameof(SeekAvgLatencyMsText));
                OnPropertyChanged(nameof(SeekMinLatencyMsText));
                OnPropertyChanged(nameof(SeekMaxLatencyMsText));
                OnPropertyChanged(nameof(SeekP95LatencyMsText));
                OnPropertyChanged(nameof(SeekTestSummaryText));
                OnPropertyChanged(nameof(HasBeforeAfterComparison));
                OnPropertyChanged(nameof(Sanitize1WriteText));
                OnPropertyChanged(nameof(Sanitize2WriteText));
                OnPropertyChanged(nameof(WriteSpeedChangeText));
                OnPropertyChanged(nameof(Sanitize1ReadText));
                OnPropertyChanged(nameof(Sanitize2ReadText));
                OnPropertyChanged(nameof(ReadSpeedChangeText));
                OnPropertyChanged(nameof(SmartDeltaSummaryText));
                OnPropertyChanged(nameof(SanitizationPerformed));
                OnPropertyChanged(nameof(SanitizationMethod));
                OnPropertyChanged(nameof(DataVerified));
                OnPropertyChanged(nameof(PartitionScheme));
                OnPropertyChanged(nameof(FileSystem));
                OnPropertyChanged(nameof(VolumeLabel));
                OnPropertyChanged(nameof(Notes));
                OnPropertyChanged(nameof(GradeColor));
                OnPropertyChanged(nameof(SealBackground));
                OnPropertyChanged(nameof(PanelBackground));
                OnPropertyChanged(nameof(ChartStrokeWrite));
                OnPropertyChanged(nameof(ChartStrokeRead));
                OnPropertyChanged(nameof(DiagnosticBadgeText));
                OnPropertyChanged(nameof(DiagnosticBadgeBackground));
                OnPropertyChanged(nameof(DiagnosticBadgeForeground));
                OnPropertyChanged(nameof(HasDiagnosticHighlights));
                OnPropertyChanged(nameof(DiagnosticHighlightsText));
                OnPropertyChanged(nameof(HasScoringReasons));
                OnPropertyChanged(nameof(ScoringReasonsText));
                OnPropertyChanged(nameof(IsSeekChart));
                OnPropertyChanged(nameof(SeekScatterPoints));
                OnPropertyChanged(nameof(SeekChartMaxLabel));
                OnPropertyChanged(nameof(SeekChartMidLabel));
                OnPropertyChanged(nameof(SeekChartMinLabel));
                OnPropertyChanged(nameof(SeekChartXAxisStartLabel));
                OnPropertyChanged(nameof(SeekChartXAxisMidLabel));
                OnPropertyChanged(nameof(SeekChartXAxisEndLabel));
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

    public string SearchText
    {
        get => _searchText;
        set
        {
            if (SetProperty(ref _searchText, value))
            {
                ApplyFilters();
            }
        }
    }

    public string SelectedGradeFilter
    {
        get => _selectedGradeFilter;
        set
        {
            if (SetProperty(ref _selectedGradeFilter, value))
            {
                ApplyFilters();
            }
        }
    }

    public int TotalCertificateCount
    {
        get => _totalCertificateCount;
        set => SetProperty(ref _totalCertificateCount, value);
    }

    public ObservableCollection<string> GradeFilters { get; }

    // Computed
    public bool HasSelectedCertificate => SelectedCertificate != null;
    public int FilteredCount => FilteredCertificates.Count;

    // Certificate display properties
    public string CertificateNumber => SelectedCertificate?.CertificateNumber ?? "-";
    public string DiskModel => SelectedCertificate?.DiskModel ?? "-";
    public string SerialNumber => SelectedCertificate?.SerialNumber ?? "-";
    public string Capacity => SelectedCertificate?.Capacity ?? "-";
    public string DiskType => SelectedCertificate?.DiskType ?? "-";
    public string Firmware => SelectedCertificate?.Firmware ?? "-";
    public string Interface => SelectedCertificate?.Interface ?? "-";
    public string Grade => SelectedCertificate?.Grade ?? "?";
    public string Score => SelectedCertificate != null ? $"{SelectedCertificate.Score:F0}/100" : "-";
    public string HealthStatus => SelectedCertificate?.HealthStatus ?? "-";
    public string TestType => SelectedCertificate?.TestType ?? "-";
    public string TestDuration => SelectedCertificate != null ? SelectedCertificate.TestDuration.ToString(@"hh\:mm\:ss") : "-";
    public string AvgWriteSpeed => SelectedCertificate != null ? $"{SelectedCertificate.AvgWriteSpeed:F1} MB/s" : "-";
    public string AvgReadSpeed => SelectedCertificate != null ? $"{SelectedCertificate.AvgReadSpeed:F1} MB/s" : "-";
    public string MaxWriteSpeed => SelectedCertificate != null ? $"{SelectedCertificate.MaxWriteSpeed:F1} MB/s" : "-";
    public string MaxReadSpeed => SelectedCertificate != null ? $"{SelectedCertificate.MaxReadSpeed:F1} MB/s" : "-";
    public string TemperatureRange => SelectedCertificate?.TemperatureRange ?? "-";
    public string Errors => SelectedCertificate?.ErrorCount.ToString() ?? "0";
    public bool SmartPassed => SelectedCertificate?.SmartPassed ?? false;
    public string SmartPowerOnHoursText => SelectedCertificate?.PowerOnHours > 0 ? $"{SelectedCertificate.PowerOnHours:#,0}" : "N/A";
    public string SmartPowerCyclesText => SelectedCertificate?.PowerCycles > 0 ? $"{SelectedCertificate.PowerCycles:#,0}" : "N/A";
    public string SmartReallocatedSectorsText => (SelectedCertificate?.ReallocatedSectors ?? 0).ToString(CultureInfo.InvariantCulture);
    public string SmartPendingSectorsText => (SelectedCertificate?.PendingSectors ?? 0).ToString(CultureInfo.InvariantCulture);
    public bool Recommended => SelectedCertificate?.Recommended ?? false;
    public string RecommendationText => SelectedCertificate?.RecommendationNotes ?? "-";
    public string GeneratedAtText => SelectedCertificate?.GeneratedAt.ToString("dd.MM.yyyy HH:mm") ?? "-";
    public string GeneratedBy => SelectedCertificate?.GeneratedBy ?? "-";

    // Seek metrics
    public bool HasSeekMetrics => SelectedCertificate?.SeekAvgLatencyMs.HasValue == true;
    public string SeekAvgLatencyMsText => SelectedCertificate?.SeekAvgLatencyMs.HasValue == true ? $"{SelectedCertificate.SeekAvgLatencyMs.Value:F2} ms" : "—";
    public string SeekMinLatencyMsText => SelectedCertificate?.SeekMinLatencyMs.HasValue == true ? $"{SelectedCertificate.SeekMinLatencyMs.Value:F2} ms" : "—";
    public string SeekMaxLatencyMsText => SelectedCertificate?.SeekMaxLatencyMs.HasValue == true ? $"{SelectedCertificate.SeekMaxLatencyMs.Value:F2} ms" : "—";
    public string SeekP95LatencyMsText => SelectedCertificate?.SeekP95LatencyMs.HasValue == true ? $"{SelectedCertificate.SeekP95LatencyMs.Value:F2} ms" : "—";
    public string SeekTestSummaryText => SelectedCertificate?.SeekTestSummary ?? "—";

    // Before/After sanitization
    public bool HasBeforeAfterComparison => SelectedCertificate?.Sanitize1AvgWriteMBps.HasValue == true;
    public string Sanitize1WriteText => SelectedCertificate?.Sanitize1AvgWriteMBps.HasValue == true ? $"{SelectedCertificate.Sanitize1AvgWriteMBps.Value:F1} MB/s" : "—";
    public string Sanitize2WriteText => SelectedCertificate?.Sanitize2AvgWriteMBps.HasValue == true ? $"{SelectedCertificate.Sanitize2AvgWriteMBps.Value:F1} MB/s" : "—";
    public string WriteSpeedChangeText => SelectedCertificate?.WriteSpeedChangePercent.HasValue == true ? $"{SelectedCertificate.WriteSpeedChangePercent.Value:+0.0;-0.0}%" : "—";
    public string Sanitize1ReadText => SelectedCertificate?.Sanitize1AvgReadMBps.HasValue == true ? $"{SelectedCertificate.Sanitize1AvgReadMBps.Value:F1} MB/s" : "—";
    public string Sanitize2ReadText => SelectedCertificate?.Sanitize2AvgReadMBps.HasValue == true ? $"{SelectedCertificate.Sanitize2AvgReadMBps.Value:F1} MB/s" : "—";
    public string ReadSpeedChangeText => SelectedCertificate?.ReadSpeedChangePercent.HasValue == true ? $"{SelectedCertificate.ReadSpeedChangePercent.Value:+0.0;-0.0}%" : "—";
    public string SmartDeltaSummaryText => SelectedCertificate?.SmartDeltaSummary ?? "—";

    // Sanitization details
    public bool SanitizationPerformed => SelectedCertificate?.SanitizationPerformed ?? false;
    public string SanitizationMethod => SelectedCertificate?.SanitizationMethod ?? "—";
    public bool DataVerified => SelectedCertificate?.DataVerified ?? false;
    public string PartitionScheme => SelectedCertificate?.PartitionScheme ?? "—";
    public string FileSystem => SelectedCertificate?.FileSystem ?? "—";
    public string VolumeLabel => SelectedCertificate?.VolumeLabel ?? "—";
    public string Notes => SelectedCertificate?.Notes ?? "—";

    // Diagnostic
    public string DiagnosticBadgeText => HasCriticalSignals ? L.Get("CertificateView.Badge.Critical") : HasWarningSignals ? L.Get("CertificateView.Badge.Warning") : L.Get("CertificateView.Badge.Stable");
    public string DiagnosticBadgeBackground => HasCriticalSignals ? "#FDECEC" : HasWarningSignals ? "#FFF4DB" : "#EAF7EE";
    public string DiagnosticBadgeForeground => HasCriticalSignals ? "#B42318" : HasWarningSignals ? "#B54708" : "#027A48";
    public bool HasDiagnosticHighlights => !string.IsNullOrWhiteSpace(DiagnosticHighlightsText);
    public string DiagnosticHighlightsText => BuildDiagnosticHighlights(SelectedCertificate?.Notes);
    public bool HasScoringReasons => !string.IsNullOrWhiteSpace(SelectedCertificate?.Notes);
    public string ScoringReasonsText => SelectedCertificate?.Notes ?? L.Get("CertificateView.NoWarnings");

    private bool HasCriticalSignals =>
        !string.IsNullOrWhiteSpace(SelectedCertificate?.Notes) &&
        (SelectedCertificate.Notes.Contains("kritické", StringComparison.OrdinalIgnoreCase) ||
         SelectedCertificate.Notes.Contains("kritický", StringComparison.OrdinalIgnoreCase));

    private bool HasWarningSignals =>
        !string.IsNullOrWhiteSpace(SelectedCertificate?.Notes) &&
        (SelectedCertificate.Notes.Contains("propad", StringComparison.OrdinalIgnoreCase) ||
         SelectedCertificate.Notes.Contains("nestabil", StringComparison.OrdinalIgnoreCase) ||
         SelectedCertificate.Notes.Contains("histor", StringComparison.OrdinalIgnoreCase));

    // Visual
    public string GradeColor => (SelectedCertificate?.Grade ?? "?") switch
    {
        "A" => "#27AE60",
        "B" => "#2ECC71",
        "C" => "#F1C40F",
        "D" => "#E67E22",
        "E" => "#E74C3C",
        "F" => "#C0392B",
        _ => "#95A5A6"
    };

    public string SealBackground => "#F8FAFC";
    public string PanelBackground => "#F6F8FB";
    public string ChartStrokeWrite => "#DC2626";
    public string ChartStrokeRead => "#059669";

    // Graph properties
    public bool HasSpeedGraph =>
        !string.IsNullOrWhiteSpace(WriteProfilePoints) ||
        !string.IsNullOrWhiteSpace(ReadProfilePoints);

    public string WriteProfilePoints
    {
        get => _writeProfilePoints;
        private set
        {
            if (SetProperty(ref _writeProfilePoints, value))
                OnPropertyChanged(nameof(HasSpeedGraph));
        }
    }

    public string ReadProfilePoints
    {
        get => _readProfilePoints;
        private set
        {
            if (SetProperty(ref _readProfilePoints, value))
                OnPropertyChanged(nameof(HasSpeedGraph));
        }
    }

    public string TemperatureProfilePoints
    {
        get => _temperatureProfilePoints;
        private set => SetProperty(ref _temperatureProfilePoints, value);
    }

    public bool HasTemperatureProfile
    {
        get => _hasTemperatureProfile;
        private set => SetProperty(ref _hasTemperatureProfile, value);
    }

    public string ChartMaxSpeedLabel
    {
        get => _chartMaxSpeedLabel;
        private set => SetProperty(ref _chartMaxSpeedLabel, value);
    }

    public string ChartMidSpeedLabel
    {
        get => _chartMidSpeedLabel;
        private set => SetProperty(ref _chartMidSpeedLabel, value);
    }

    public string ChartMinSpeedLabel
    {
        get => _chartMinSpeedLabel;
        private set => SetProperty(ref _chartMinSpeedLabel, value);
    }

    public string ChartXAxisStartLabel
    {
        get => _chartXAxisStartLabel;
        private set => SetProperty(ref _chartXAxisStartLabel, value);
    }

    public string ChartXAxisMidLabel
    {
        get => _chartXAxisMidLabel;
        private set => SetProperty(ref _chartXAxisMidLabel, value);
    }

    public string ChartXAxisEndLabel
    {
        get => _chartXAxisEndLabel;
        private set => SetProperty(ref _chartXAxisEndLabel, value);
    }

    public ObservableCollection<ObservablePoint> SeekScatterPoints
    {
        get => _seekScatterPoints;
        private set
        {
            if (SetProperty(ref _seekScatterPoints, value))
            {
                OnPropertyChanged(nameof(IsSeekChart));
            }
        }
    }

    public string SeekFullStrokePoints
    {
        get => _seekFullStrokePoints;
        private set => SetProperty(ref _seekFullStrokePoints, value);
    }
    public string SeekRandomPoints
    {
        get => _seekRandomPoints;
        private set => SetProperty(ref _seekRandomPoints, value);
    }
    public string SeekSkipPoints
    {
        get => _seekSkipPoints;
        private set => SetProperty(ref _seekSkipPoints, value);
    }
    public string SeekLegacyPoints
    {
        get => _seekLegacyPoints;
        private set => SetProperty(ref _seekLegacyPoints, value);
    }
    public bool IsSeekChart => SeekFullStrokePoints.Length > 0 ||
        SeekRandomPoints.Length > 0 || SeekSkipPoints.Length > 0 || SeekLegacyPoints.Length > 0;

    public string TemperatureMaxLabel
    {
        get => _temperatureMaxLabel;
        private set => SetProperty(ref _temperatureMaxLabel, value);
    }
    public string TemperatureMidLabel
    {
        get => _temperatureMidLabel;
        private set => SetProperty(ref _temperatureMidLabel, value);
    }
    public string TemperatureMinLabel
    {
        get => _temperatureMinLabel;
        private set => SetProperty(ref _temperatureMinLabel, value);
    }

    public string SeekChartMaxLabel
    {
        get => _seekChartMaxLabel;
        private set => SetProperty(ref _seekChartMaxLabel, value);
    }

    public string SeekChartMidLabel
    {
        get => _seekChartMidLabel;
        private set => SetProperty(ref _seekChartMidLabel, value);
    }

    public string SeekChartMinLabel
    {
        get => _seekChartMinLabel;
        private set => SetProperty(ref _seekChartMinLabel, value);
    }

    public string SeekChartXAxisStartLabel
    {
        get => _seekChartXAxisStartLabel;
        private set => SetProperty(ref _seekChartXAxisStartLabel, value);
    }

    public string SeekChartXAxisMidLabel
    {
        get => _seekChartXAxisMidLabel;
        private set => SetProperty(ref _seekChartXAxisMidLabel, value);
    }

    public string SeekChartXAxisEndLabel
    {
        get => _seekChartXAxisEndLabel;
        private set => SetProperty(ref _seekChartXAxisEndLabel, value);
    }

    #endregion

    #region Navigation

    public void OnNavigatedTo()
    {
        _ = LoadAllCertificatesAsync();
    }

    #endregion

    /// <summary>Editable explanation by the operator; not part of automatic grading.</summary>
    public string TechnicianAssessmentDraft
    {
       get => _technicianAssessmentDraft;
       set => SetProperty(ref _technicianAssessmentDraft, value);
    }

    #region Commands

    [RelayCommand]
    private async Task SaveTechnicianAssessmentAsync()
    {
       var cert = SelectedCertificate;
       if(cert == null || cert.Id <= 0)
       {
          await _dialogService.ShowWarningAsync(L.Get("Common.Warning"), L.Get("CertificateView.TechnicianAssessment.SaveFirst"));
          return;
       }

       var text = TechnicianAssessmentDraft.Trim();
       if(text.Length > 2000)
       {
          await _dialogService.ShowWarningAsync(L.Get("Common.Warning"), L.Get("CertificateView.TechnicianAssessment.TooLong"));
          return;
       }

       try
       {
          IsLoading = true;
          if(!await _diskCardRepository.UpdateCertificateTechnicianAssessmentAsync(cert.Id, text))
             throw new InvalidOperationException(L.Get("CertificateView.TechnicianAssessment.Missing"));
          cert.TechnicianAssessment = string.IsNullOrEmpty(text) ? null : text;
          cert.TechnicianAssessmentUpdatedAtUtc = DateTime.UtcNow;
          // The old exported PDF is now out of date. Next export/print renders the note.
          cert.PdfGenerated = false;
          StatusMessage = L.Get("CertificateView.TechnicianAssessment.Saved");
       }
       catch(Exception ex)
       {
          await _dialogService.ShowErrorAsync(L.Get("Common.Error"), ex.Message);
       }
       finally
       {
          IsLoading = false;
       }
    }

    [RelayCommand]
    private async Task RefreshAsync()
    {
        await LoadAllCertificatesAsync();
    }

    [RelayCommand]
    private void GoBack()
    {
        _navigationService.NavigateTo<DiskCardsViewModel>();
    }

    [RelayCommand]
    private async Task ExportPdfAsync()
    {
        if (SelectedCertificate == null)
        {
            StatusMessage = L.Get("CertificateBrowser.Status.SelectFirst");
            return;
        }

        try
        {
            IsLoading = true;
            await HydrateCertificateForPdfAsync(SelectedCertificate);
            var pdfPath = await _certificateGenerator.GeneratePdfAsync(SelectedCertificate);
            SelectedCertificate.PdfPath = pdfPath;
            SelectedCertificate.PdfGenerated = true;
            await _diskCardRepository.UpdateCertificateAsync(SelectedCertificate);

            StatusMessage = string.Format(L.Get("CertificateBrowser.Status.PdfExported"), pdfPath);
            await _dialogService.ShowInfoAsync(L.Get("CertificateView.Dialog.PdfExport"),
                string.Format(L.Get("CertificateBrowser.Status.PdfExported"), pdfPath) + "\n\n" +
                L.Get("CertificateBrowser.Status.UseExternalViewer"));
        }
        catch (Exception ex)
        {
            StatusMessage = $"{L.Get("Common.Error")}: {ex.Message}";
            await _dialogService.ShowErrorAsync(L.Get("Common.Error"), ex.Message);
        }
        finally
        {
            IsLoading = false;
        }
    }

    /// <summary>
    /// Open the selected saved certificate in the detailed session viewer.
    /// That viewer also supports selecting *any* session and pairing with seek.
    /// </summary>
    [RelayCommand]
    private async Task OpenCertificateDetailsAsync()
    {
        var cert = SelectedCertificate;
        if (cert == null)
            return;

        var card = await _diskCardRepository.GetByIdAsync(cert.DiskCardId);
        if (card == null || _selectedDiskService == null)
        {
            await _dialogService.ShowErrorAsync(L.Get("Common.Error"),
                "Vybraný certifikát nemá dostupnou kartu disku.");
            return;
        }

        SetSelectionContext(card, cert.TestSessionId, cert.Id);
        _navigationService.NavigateTo<CertificateViewModel>();
    }

    [RelayCommand]
    private async Task ViewDiskCardAsync()
    {
        var cert = SelectedCertificate;
        if (cert == null)
            return;
        var card = await _diskCardRepository.GetByIdAsync(cert.DiskCardId);
        if (card == null || _selectedDiskService == null)
        {
            await _dialogService.ShowErrorAsync(L.Get("Common.Error"),
                "Vybraný disk nelze otevřít.");
            return;
        }

        SetSelectionContext(card, cert.TestSessionId, cert.Id);
        _navigationService.NavigateTo<DiskCardDetailViewModel>();
    }

    private void SetSelectionContext(DiskCard card, int sessionId, int certificateId)
    {
        if (_selectedDiskService == null) return;
        _selectedDiskService.SelectedDisk = new CoreDriveInfo
        {
            Path = card.DevicePath,
            Name = card.ModelName,
            SerialNumber = card.SerialNumber,
            TotalSize = card.Capacity,
            FirmwareVersion = card.FirmwareVersion
        };
        _selectedDiskService.SelectedDiskCardId = card.Id;
        _selectedDiskService.SelectedDiskDisplayName = card.ModelName;
        _selectedDiskService.IsSelectedDiskLocked = card.IsLocked;
        _selectedDiskService.SelectedTestSessionId = sessionId > 0 ? sessionId : null;
        _selectedDiskService.SelectedCertificateId = certificateId > 0 ? certificateId : null;
    }

    #endregion

    #region Private Methods

    private async Task LoadAllCertificatesAsync()
    {
        try
        {
            IsLoading = true;
            StatusMessage = L.Get("CertificateBrowser.Status.LoadingAll");

            var allCards = await _diskCardRepository.GetAllAsync();
            var allCertificates = new List<CertificateListItem>();

            foreach (var card in allCards)
            {
                var certs = await _diskCardRepository.GetCertificatesAsync(card.Id);
                foreach (var cert in certs)
                {
                    allCertificates.Add(new CertificateListItem
                    {
                        Id = cert.Id,
                        CertificateNumber = cert.CertificateNumber,
                        DiskModel = cert.DiskModel,
                        SerialNumber = cert.SerialNumber,
                        Grade = cert.Grade,
                        Score = cert.Score,
                        TestType = cert.TestType,
                        GeneratedAt = cert.GeneratedAt,
                        Recommended = cert.Recommended,
                        DiskCardId = cert.DiskCardId,
                        Capacity = cert.Capacity,
                        DiskType = cert.DiskType
                    });
                }
            }

            AllCertificates.Clear();
            foreach (var item in allCertificates.OrderByDescending(c => c.GeneratedAt))
            {
                AllCertificates.Add(item);
            }

            TotalCertificateCount = AllCertificates.Count;
            ApplyFilters();

            StatusMessage = string.Format(L.Get("CertificateBrowser.Status.Loaded"), TotalCertificateCount, allCards.Count);
        }
        catch (Exception ex)
        {
            StatusMessage = $"{L.Get("Common.Error")}: {ex.Message}";
            await _dialogService.ShowErrorAsync(L.Get("Common.Error"), string.Format(L.Get("CertificateBrowser.Error.Load"), ex.Message));
        }
        finally
        {
            IsLoading = false;
        }
    }

    private async Task LoadCertificateDetailAsync(int certificateId, int generation)
    {
        try
        {
            IsLoading = true;
            StatusMessage = L.Get("CertificateBrowser.Status.LoadingDetail");

            var cert = await _diskCardRepository.GetCertificateAsync(certificateId);
            if (generation != _detailSelectionGeneration) return;
            if (cert == null)
            {
                StatusMessage = L.Get("CertificateBrowser.Status.CertNotFound");
                return;
            }

            // Try to load test session for graph data
            if (cert.TestSessionId > 0)
            {
                _selectedSession = await _diskCardRepository.GetTestSessionWithoutSamplesAsync(cert.TestSessionId);
                if (generation != _detailSelectionGeneration) return;
            }
            else
            {
                _selectedSession = null;
            }

            SelectedCertificate = cert;
            await ReconstructGraphAsync(cert, generation);
            if (generation != _detailSelectionGeneration) return;

            StatusMessage = string.Format(L.Get("CertificateBrowser.Status.CertLoaded"), cert.CertificateNumber, cert.DiskModel);
        }
        catch (Exception ex)
        {
            StatusMessage = string.Format(L.Get("Common.Error"), ex.Message);
        }
        finally
        {
            if (generation == _detailSelectionGeneration)
                IsLoading = false;
        }
    }

    /// <summary>
    /// Both certificate screens render from the same real measurements. Prefer
    /// SQL-bounded samples with their original progress axis, then persisted
    /// certificate profiles when historical data have been pruned.
    /// Read-only tests need only their read profile; no write data is invented.
    /// </summary>
    private async Task ReconstructGraphAsync(DiskCertificate certificate, int generation)
    {
        ArgumentNullException.ThrowIfNull(certificate);

        if (certificate.SeekAvgLatencyMs.HasValue ||
            certificate.SeekLatencyPoints.Count > 0 ||
            certificate.TestType.Contains("Seek", StringComparison.OrdinalIgnoreCase))
        {
            var seek = await CertificateSeekChartLoader.LoadAsync(_diskCardRepository,
                certificate.TestSessionId, _selectedSession?.SeekResultsJson,
                certificate.SeekLatencyPoints);
            if (generation != _detailSelectionGeneration) return;
            ApplySeekChart(seek);
        }
        else
        {
            ResetSeekGraph();
        }

        var write = new List<SpeedSample>();
        var read = new List<SpeedSample>();
        var temperatures = new List<TemperatureSample>();

        if (certificate.TestSessionId > 0)
        {
            (write, read) = await LoadSpeedSamplesChunkedAsync(certificate.TestSessionId);
            if (generation != _detailSelectionGeneration) return;
            try
            {
                temperatures = await _diskCardRepository.GetTemperatureSampleSeriesDownsampledAsync(
                    certificate.TestSessionId, 256);
            }
            catch (Exception ex) when (ex is System.IO.IOException or InvalidOperationException)
            {
                // Temperature telemetry is optional; speeds remain usable.
            }
        }

        if (generation != _detailSelectionGeneration) return;
        var graph = CertificateChartProjector.Build(certificate, write, read, temperatures);
        WriteProfilePoints = graph.WriteProfilePoints;
        ReadProfilePoints = graph.ReadProfilePoints;
        TemperatureProfilePoints = graph.TemperatureProfilePoints;
        HasTemperatureProfile = graph.HasTemperatureProfile;
        TemperatureMaxLabel = graph.TemperatureMaxLabel;
        TemperatureMidLabel = graph.TemperatureMidLabel;
        TemperatureMinLabel = graph.TemperatureMinLabel;
        ChartMaxSpeedLabel = graph.ChartMaxSpeedLabel;
        ChartMidSpeedLabel = graph.ChartMidSpeedLabel;
        ChartMinSpeedLabel = graph.ChartMinSpeedLabel;
        ChartXAxisStartLabel = graph.ChartXAxisStartLabel;
        ChartXAxisMidLabel = graph.ChartXAxisMidLabel;
        ChartXAxisEndLabel = graph.ChartXAxisEndLabel;
    }

    private void ResetGraphToDefaults()
    {
        ResetSeekGraph();
        var defaults = CertificateGraphData.Default;
        WriteProfilePoints = defaults.WriteProfilePoints;
        ReadProfilePoints = defaults.ReadProfilePoints;
        TemperatureProfilePoints = defaults.TemperatureProfilePoints;
        HasTemperatureProfile = defaults.HasTemperatureProfile;
        TemperatureMaxLabel = defaults.TemperatureMaxLabel;
        TemperatureMidLabel = defaults.TemperatureMidLabel;
        TemperatureMinLabel = defaults.TemperatureMinLabel;
        ChartMaxSpeedLabel = defaults.ChartMaxSpeedLabel;
        ChartMidSpeedLabel = defaults.ChartMidSpeedLabel;
        ChartMinSpeedLabel = defaults.ChartMinSpeedLabel;
        ChartXAxisStartLabel = defaults.ChartXAxisStartLabel;
        ChartXAxisMidLabel = defaults.ChartXAxisMidLabel;
        ChartXAxisEndLabel = defaults.ChartXAxisEndLabel;
    }

    private async Task<List<double>> LoadSeekLatencySamplesAsync(int sessionId)
    {
        try
        {
            // Keep all seek dots for analytical certificate browsing. Seek tests
            // are bounded by UI limits, unlike surface-test telemetry, and the
            // full-stroke shape is important for diagnosis.
            var records = await _diskCardRepository.GetSeekSamplesDownsampledAsync(sessionId, 1024);
            var successful = records.Where(s => !s.HasError && s.LatencyMs > 0).ToList();
            if (successful.Count > 0)
            {
                var fullStroke = successful
                    .Where(s => s.TestType == SeekTestType.FullStroke)
                    .OrderBy(s => s.Index)
                    .Select(s => s.LatencyMs)
                    .ToList();

                if (fullStroke.Count > 0)
                {
                    return fullStroke;
                }

                return successful
                    .OrderBy(s => s.TestType)
                    .ThenBy(s => s.Index)
                    .Select(s => s.LatencyMs)
                    .ToList();
            }
        }
        catch
        {
            // Fallback below for older DBs or partially migrated data.
        }

        return ExtractSeekLatenciesFromSession(_selectedSession);
    }

    private static List<double> ExtractSeekLatenciesFromSession(TestSession? session)
    {
        if (string.IsNullOrWhiteSpace(session?.SeekResultsJson))
        {
            return new List<double>();
        }

        try
        {
            var single = JsonSerializer.Deserialize<SeekTestResult>(session.SeekResultsJson);
            if (single?.Samples.Count > 0)
            {
                return single.Samples
                    .Where(s => !s.HasError && s.LatencyMs > 0)
                    .OrderBy(s => s.Index)
                    .Select(s => s.LatencyMs)
                    .ToList();
            }
        }
        catch (JsonException) { }

        try
        {
            var envelope = JsonSerializer.Deserialize<SeekResultsEnvelope>(session.SeekResultsJson);
            var fullStroke = envelope?.FullStroke?.Samples
                .Where(s => !s.HasError && s.LatencyMs > 0)
                .OrderBy(s => s.Index)
                .Select(s => s.LatencyMs)
                .ToList() ?? new List<double>();

            if (fullStroke.Count > 0)
            {
                return fullStroke;
            }

            return new[] { envelope?.Random, envelope?.Skip }
                .Where(r => r != null)
                .SelectMany(r => r!.Samples)
                .Where(s => !s.HasError && s.LatencyMs > 0)
                .OrderBy(s => s.Index)
                .Select(s => s.LatencyMs)
                .ToList();
        }
        catch (JsonException)
        {
            return new List<double>();
        }
    }

    private void ApplySeekChart(CertificateSeekGraphData graph)
    {
        SeekFullStrokePoints = graph.FullStrokePoints;
        SeekRandomPoints = graph.RandomPoints;
        SeekSkipPoints = graph.SkipPoints;
        SeekLegacyPoints = graph.LegacyPoints;
        SeekChartMaxLabel = graph.MaxLabel;
        SeekChartMidLabel = graph.MidLabel;
        SeekChartMinLabel = graph.MinLabel;
        SeekChartXAxisStartLabel = graph.StartLabel;
        SeekChartXAxisMidLabel = graph.MidIndexLabel;
        SeekChartXAxisEndLabel = graph.EndLabel;
        OnPropertyChanged(nameof(IsSeekChart));
    }

    private void ResetSeekGraph() => ApplySeekChart(CertificateSeekGraphData.Empty);

    private sealed class SeekResultsEnvelope
    {
        public SeekTestResult? FullStroke { get; set; }
        public SeekTestResult? Random { get; set; }
        public SeekTestResult? Skip { get; set; }
    }

    private async Task HydrateCertificateForPdfAsync(DiskCertificate certificate)
    {
        if (certificate.TestSessionId <= 0)
        {
            return;
        }

        var session = await _diskCardRepository.GetTestSessionWithoutSamplesAsync(certificate.TestSessionId);
        if (session != null)
        {
            _selectedSession = session;
            certificate.ChartImagePath = session.ChartImagePath;
            if (certificate.ErrorCount == 0)
            {
                certificate.ErrorCount = Math.Max(0, session.WriteErrors) + Math.Max(0, session.ReadErrors) + Math.Max(0, session.VerificationErrors);
            }

            if (string.IsNullOrWhiteSpace(certificate.TemperatureRange) || certificate.TemperatureRange == "N/A")
            {
                certificate.TemperatureRange = session.StartTemperature.HasValue && session.MaxTemperature.HasValue
                    ? $"{session.StartTemperature.Value}°C - {session.MaxTemperature.Value}°C"
                    : certificate.TemperatureRange;
            }
        }

        var (writeSamples, readSamples) = await LoadSpeedSamplesChunkedAsync(certificate.TestSessionId);
        var writeValues = writeSamples.Select(s => s.SpeedMBps).Where(v => v > 0).ToList();
        var readValues = readSamples.Select(s => s.SpeedMBps).Where(v => v > 0).ToList();

        if (certificate.AvgWriteSpeed <= 0 && writeValues.Count > 0) certificate.AvgWriteSpeed = writeValues.Average();
        if (certificate.MaxWriteSpeed <= 0 && writeValues.Count > 0) certificate.MaxWriteSpeed = writeValues.Max();
        if (certificate.AvgReadSpeed <= 0 && readValues.Count > 0) certificate.AvgReadSpeed = readValues.Average();
        if (certificate.MaxReadSpeed <= 0 && readValues.Count > 0) certificate.MaxReadSpeed = readValues.Max();

        // Never replace previously stored stall-preserving profiles with a
        // freshly averaged subset. Historical SQL downsampling may have fewer
        // points than the original persistent certificate data.
        if (!certificate.IsReadOnlyTest && certificate.WriteProfilePoints.Count == 0)
            certificate.WriteProfilePoints = DownsampleSpeedValues(writeValues, GraphTargetPoints);
        if (certificate.ReadProfilePoints.Count == 0)
            certificate.ReadProfilePoints = DownsampleSpeedValues(readValues, GraphTargetPoints);

        if (certificate.SeekLatencyPoints.Count == 0 &&
            (certificate.SeekAvgLatencyMs.HasValue ||
             certificate.TestType.Contains("Seek", StringComparison.OrdinalIgnoreCase)))
        {
            certificate.SeekLatencyPoints = await LoadSeekLatencySamplesAsync(certificate.TestSessionId);
        }
    }

    /// <summary>
    /// Načte rychlostní vzorky pomocí SQL-level downsamplingu pro prevenci OOM.
    /// Databáze vrátí maximálně ~512 vzorků na fázi bez ohledu na velikost datasetu.
    /// </summary>
    private async Task<(List<SpeedSample> WriteSamples, List<SpeedSample> ReadSamples)> LoadSpeedSamplesChunkedAsync(int sessionId)
    {
        const int maxPoints = GraphTargetPoints;
        try
        {
            return await _diskCardRepository.GetSpeedSampleSeriesDownsampledAsync(sessionId, maxPoints);
        }
        catch
        {
            return (new List<SpeedSample>(), new List<SpeedSample>());
        }
    }

    private static List<double> DownsampleSpeedValues(List<double> values, int targetPoints)
    {
        values = values.Where(v => double.IsFinite(v) && v >= 0).ToList();
        if (values.Count <= targetPoints) return values;

        // Preserve brief zero-speed stalls: one average per bucket previously
        // flattened a 0 MB/s pause into a healthy-looking speed.
        var result = new List<double>(targetPoints);
        int bucketCount = Math.Max(1, targetPoints / 4);
        for (int i = 0; i < bucketCount; i++)
        {
            int start = i * values.Count / bucketCount;
            int end = Math.Min(values.Count, (i + 1) * values.Count / bucketCount);
            if (end <= start) continue;
            var window = values.Skip(start).Take(end - start).ToArray();
            result.Add(window[0]);
            result.Add(window.Min());
            result.Add(window.Max());
            result.Add(window[^1]);
        }
        return result;
    }

    // Speed/temperature projection is implemented once in CertificateChartProjector.
    // The browser must not retain another independent graph reconstruction path.

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
            .Take(6)
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

    private void ApplyFilters()
    {
        FilteredCertificates.Clear();

        foreach (var cert in AllCertificates)
        {
            var query = SearchText?.Trim();
            if (!string.IsNullOrWhiteSpace(query) &&
                !new[]
                {
                    cert.DiskModel, cert.SerialNumber, cert.CertificateNumber,
                    cert.TestType, cert.Grade,
                    cert.GeneratedAt.ToString("dd.MM.yyyy"),
                    cert.GeneratedAt.ToString("yyyy-MM-dd")
                }.Any(value => value?.Contains(query, StringComparison.OrdinalIgnoreCase) == true))
            {
                continue;
            }

            if (!string.IsNullOrEmpty(SelectedGradeFilter) && SelectedGradeFilter != L.Get("CertificateBrowser.Status.All") && cert.Grade != SelectedGradeFilter)
            {
                continue;
            }

            FilteredCertificates.Add(cert);
        }

        // The detail pane must always correspond to a visible row.
        // A filter change must not leave an unrelated certificate displayed.
        if (SelectedListItem == null ||
            !FilteredCertificates.Any(c => c.Id == SelectedListItem.Id))
        {
            SelectedListItem = FilteredCertificates.FirstOrDefault();
        }

        OnPropertyChanged(nameof(FilteredCount));
        StatusMessage = FilteredCertificates.Count < AllCertificates.Count
            ? string.Format(L.Get("CertificateBrowser.Status.ShowingCount"), FilteredCertificates.Count, AllCertificates.Count)
            : string.Format(L.Get("CertificateBrowser.Status.TotalCount"), AllCertificates.Count);
    }

    #endregion
}

/// <summary>
/// Lightweight certificate list item for the browser DataGrid.
/// </summary>
public class CertificateListItem
{
    public int Id { get; set; }
    public string CertificateNumber { get; set; } = string.Empty;
    public string DiskModel { get; set; } = string.Empty;
    public string SerialNumber { get; set; } = string.Empty;
    public string Grade { get; set; } = "?";
    public double Score { get; set; }
    public string TestType { get; set; } = string.Empty;
    public DateTime GeneratedAt { get; set; }
    public bool Recommended { get; set; }
    public int DiskCardId { get; set; }
    public string Capacity { get; set; } = string.Empty;
    public string DiskType { get; set; } = string.Empty;

    public string ScoreText => $"{Score:F0}";
    public string GeneratedAtText => GeneratedAt.ToString("dd.MM.yyyy HH:mm");
    public string RecommendedText => Recommended ? App.GetService<DiskChecker.UI.Avalonia.Services.LocaleService>()?.Get("CertificateBrowser.RecommendedYes") ?? "✅ Yes" : "—";
    public string GradeColor => Grade switch
    {
        "A" => "#27AE60",
        "B" => "#2ECC71",
        "C" => "#F1C40F",
        "D" => "#E67E22",
        "E" => "#E74C3C",
        "F" => "#C0392B",
        _ => "#95A5A6"
    };
}
