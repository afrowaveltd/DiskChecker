using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DiskChecker.Core.Interfaces;
using DiskChecker.Core.Models;
using DiskChecker.UI.Avalonia.Services.Interfaces;

namespace DiskChecker.UI.Avalonia.ViewModels;

/// <summary>
/// Prohlížeč všech uložených certifikátů s rekonstrukcí grafů a detailů.
/// </summary>
public partial class CertificateBrowserViewModel : ViewModelBase, INavigableViewModel
{
    private const int GraphTargetPoints = 32;

    private readonly IDiskCardRepository _diskCardRepository;
    private readonly INavigationService _navigationService;
    private readonly IDialogService _dialogService;
    private readonly ICertificateGenerator _certificateGenerator;

    private ObservableCollection<CertificateListItem> _allCertificates = new();
    private ObservableCollection<CertificateListItem> _filteredCertificates = new();
    private CertificateListItem? _selectedListItem;
    private DiskCertificate? _selectedCertificate;
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

    public CertificateBrowserViewModel(
        IDiskCardRepository diskCardRepository,
        INavigationService navigationService,
        IDialogService dialogService,
        ICertificateGenerator certificateGenerator)
    {
        _diskCardRepository = diskCardRepository;
        _navigationService = navigationService;
        _dialogService = dialogService;
        _certificateGenerator = certificateGenerator;

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
            if (SetProperty(ref _selectedListItem, value) && value != null)
            {
                _ = LoadCertificateDetailAsync(value.Id);
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
    public string WriteProfilePoints
    {
        get => _writeProfilePoints;
        private set => SetProperty(ref _writeProfilePoints, value);
    }

    public string ReadProfilePoints
    {
        get => _readProfilePoints;
        private set => SetProperty(ref _readProfilePoints, value);
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

    #endregion

    #region Navigation

    public void OnNavigatedTo()
    {
        _ = LoadAllCertificatesAsync();
    }

    #endregion

    #region Commands

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

    [RelayCommand]
    private async Task ViewDiskCardAsync()
    {
        if (SelectedCertificate?.DiskCardId > 0)
        {
            var card = await _diskCardRepository.GetByIdAsync(SelectedCertificate.DiskCardId);
            if (card != null)
            {
                // Navigate to disk card detail - we'd need ISelectedDiskService for this
                // For now, navigate back to disk cards
                _navigationService.NavigateTo<DiskCardsViewModel>();
                StatusMessage = string.Format(L.Get("CertificateBrowser.Status.RedirectedToCards"), SelectedCertificate.DiskModel);
            }
        }
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

    private async Task LoadCertificateDetailAsync(int certificateId)
    {
        try
        {
            IsLoading = true;
            StatusMessage = L.Get("CertificateBrowser.Status.LoadingDetail");

            var cert = await _diskCardRepository.GetCertificateAsync(certificateId);
            if (cert == null)
            {
                StatusMessage = L.Get("CertificateBrowser.Status.CertNotFound");
                return;
            }

            // Try to load test session for graph data
            if (cert.TestSessionId > 0)
            {
                _selectedSession = await _diskCardRepository.GetTestSessionWithoutSamplesAsync(cert.TestSessionId);
            }

            SelectedCertificate = cert;
            await ReconstructGraphAsync(cert);

            StatusMessage = string.Format(L.Get("CertificateBrowser.Status.CertLoaded"), cert.CertificateNumber, cert.DiskModel);
        }
        catch (Exception ex)
        {
            StatusMessage = string.Format(L.Get("Common.Error"), ex.Message);
        }
        finally
        {
            IsLoading = false;
        }
    }

    private async Task ReconstructGraphAsync(DiskCertificate certificate)
    {
        ArgumentNullException.ThrowIfNull(certificate);

        // Use stored profile points if available
        var hasWriteProfile = certificate.WriteProfilePoints is { Count: > 0 };
        var hasReadProfile = certificate.ReadProfilePoints is { Count: > 0 };

        if (hasWriteProfile && hasReadProfile)
        {
            // Reconstruct graph from stored downsampled points
            var writeValues = certificate.WriteProfilePoints
                .Select(p => new SpeedSample { ProgressPercent = 0, SpeedMBps = p })
                .ToList();
            var readValues = certificate.ReadProfilePoints
                .Select(p => new SpeedSample { ProgressPercent = 0, SpeedMBps = p })
                .ToList();

            await Task.Run(() =>
            {
                var graphData = BuildGraphDataFromValues(
                    writeValues.Select(s => s.SpeedMBps).ToList(),
                    readValues.Select(s => s.SpeedMBps).ToList(),
                    certificate.AvgWriteSpeed,
                    certificate.MaxWriteSpeed,
                    certificate.AvgReadSpeed,
                    certificate.MaxReadSpeed);

                WriteProfilePoints = graphData.WriteProfilePoints;
                ReadProfilePoints = graphData.ReadProfilePoints;
                TemperatureProfilePoints = graphData.TemperatureProfilePoints;
                HasTemperatureProfile = graphData.HasTemperatureProfile;
                ChartMaxSpeedLabel = graphData.ChartMaxSpeedLabel;
                ChartMidSpeedLabel = graphData.ChartMidSpeedLabel;
                ChartMinSpeedLabel = graphData.ChartMinSpeedLabel;
                ChartXAxisStartLabel = graphData.ChartXAxisStartLabel;
                ChartXAxisMidLabel = graphData.ChartXAxisMidLabel;
                ChartXAxisEndLabel = graphData.ChartXAxisEndLabel;
            });
        }
        else if (_selectedSession != null && certificate.TestSessionId > 0)
        {
            // Try to load speed samples from test session
            try
            {
                var (writeSamples, readSamples) = await _diskCardRepository.GetSpeedSampleSeriesAsync(certificate.TestSessionId);

                if (writeSamples.Count > 0 || readSamples.Count > 0)
                {
                    var tempSamples = _selectedSession.TemperatureSamples ?? new List<TemperatureSample>();

                    await Task.Run(() =>
                    {
                        var graphData = BuildGraphData(
                            writeSamples.Where(s => s.SpeedMBps > 0).OrderBy(s => s.ProgressPercent).ToList(),
                            readSamples.Where(s => s.SpeedMBps > 0).OrderBy(s => s.ProgressPercent).ToList(),
                            tempSamples,
                            certificate.AvgWriteSpeed,
                            certificate.MaxWriteSpeed,
                            certificate.AvgReadSpeed,
                            certificate.MaxReadSpeed);

                        WriteProfilePoints = graphData.WriteProfilePoints;
                        ReadProfilePoints = graphData.ReadProfilePoints;
                        TemperatureProfilePoints = graphData.TemperatureProfilePoints;
                        HasTemperatureProfile = graphData.HasTemperatureProfile;
                        ChartMaxSpeedLabel = graphData.ChartMaxSpeedLabel;
                        ChartMidSpeedLabel = graphData.ChartMidSpeedLabel;
                        ChartMinSpeedLabel = graphData.ChartMinSpeedLabel;
                        ChartXAxisStartLabel = graphData.ChartXAxisStartLabel;
                        ChartXAxisMidLabel = graphData.ChartXAxisMidLabel;
                        ChartXAxisEndLabel = graphData.ChartXAxisEndLabel;
                    });
                }
                else
                {
                    ResetGraphToDefaults();
                }
            }
            catch
            {
                ResetGraphToDefaults();
            }
        }
        else
        {
            ResetGraphToDefaults();
        }
    }

    private void ResetGraphToDefaults()
    {
        var defaults = CertificateGraphData.Default;
        WriteProfilePoints = defaults.WriteProfilePoints;
        ReadProfilePoints = defaults.ReadProfilePoints;
        TemperatureProfilePoints = defaults.TemperatureProfilePoints;
        HasTemperatureProfile = defaults.HasTemperatureProfile;
        ChartMaxSpeedLabel = defaults.ChartMaxSpeedLabel;
        ChartMidSpeedLabel = defaults.ChartMidSpeedLabel;
        ChartMinSpeedLabel = defaults.ChartMinSpeedLabel;
        ChartXAxisStartLabel = defaults.ChartXAxisStartLabel;
        ChartXAxisMidLabel = defaults.ChartXAxisMidLabel;
        ChartXAxisEndLabel = defaults.ChartXAxisEndLabel;
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

        var (writeSamples, readSamples) = await _diskCardRepository.GetSpeedSampleSeriesAsync(certificate.TestSessionId);
        var writeValues = writeSamples.Select(s => s.SpeedMBps).Where(v => v > 0).ToList();
        var readValues = readSamples.Select(s => s.SpeedMBps).Where(v => v > 0).ToList();

        if (certificate.AvgWriteSpeed <= 0 && writeValues.Count > 0) certificate.AvgWriteSpeed = writeValues.Average();
        if (certificate.MaxWriteSpeed <= 0 && writeValues.Count > 0) certificate.MaxWriteSpeed = writeValues.Max();
        if (certificate.AvgReadSpeed <= 0 && readValues.Count > 0) certificate.AvgReadSpeed = readValues.Average();
        if (certificate.MaxReadSpeed <= 0 && readValues.Count > 0) certificate.MaxReadSpeed = readValues.Max();

        certificate.WriteProfilePoints = DownsampleSpeedValues(writeValues, GraphTargetPoints);
        certificate.ReadProfilePoints = DownsampleSpeedValues(readValues, GraphTargetPoints);
    }

    private static List<double> DownsampleSpeedValues(List<double> values, int targetPoints)
    {
        values = values.Where(v => v > 0).ToList();
        if (values.Count <= targetPoints)
        {
            return values;
        }

        var result = new List<double>(targetPoints);
        var bucketSize = values.Count / (double)targetPoints;
        for (var i = 0; i < targetPoints; i++)
        {
            var start = (int)Math.Floor(i * bucketSize);
            var end = (int)Math.Floor((i + 1) * bucketSize);
            end = Math.Clamp(end, start + 1, values.Count);
            result.Add(values.Skip(start).Take(end - start).Average());
        }
        return result;
    }

    private static CertificateGraphData BuildGraphDataFromValues(
        List<double> writeValues,
        List<double> readValues,
        double avgWriteSpeed,
        double maxWriteSpeed,
        double avgReadSpeed,
        double maxReadSpeed)
    {
        var allValues = writeValues.Concat(readValues).Where(v => v > 0).ToList();
        if (allValues.Count == 0)
        {
            return CertificateGraphData.Default;
        }

        var minSpeed = allValues.Min();
        var maxSpeed = allValues.Max();
        var spread = maxSpeed - minSpeed;
        if (spread < Math.Max(1d, maxSpeed * 0.02))
        {
            var center = (maxSpeed + minSpeed) / 2.0;
            var pad = Math.Max(1d, center * 0.05);
            minSpeed = Math.Max(0d, center - pad);
            maxSpeed = center + pad;
        }

        // Create synthetic samples for polyline building
        var writeSamples = writeValues.Select((v, i) => new SpeedSample
        {
            ProgressPercent = writeValues.Count > 1 ? (double)i / (writeValues.Count - 1) * 100d : 50d,
            SpeedMBps = v
        }).ToList();

        var readSamples = readValues.Select((v, i) => new SpeedSample
        {
            ProgressPercent = readValues.Count > 1 ? (double)i / (readValues.Count - 1) * 100d : 50d,
            SpeedMBps = v
        }).ToList();

        return new CertificateGraphData(
            BuildPolylinePoints(writeSamples, minSpeed, maxSpeed),
            BuildPolylinePoints(readSamples, minSpeed, maxSpeed),
            "10,110 490,110",
            false,
            $"{maxSpeed:F1} MB/s",
            $"{((maxSpeed + minSpeed) / 2):F1} MB/s",
            $"{minSpeed:F1} MB/s",
            "0 %",
            "50 %",
            "100 %");
    }

    private static CertificateGraphData BuildGraphData(
        List<SpeedSample> writeSamples,
        List<SpeedSample> readSamples,
        List<TemperatureSample> temperatureSamples,
        double avgWriteSpeed,
        double maxWriteSpeed,
        double avgReadSpeed,
        double maxReadSpeed)
    {
        var writePoints = GetGraphSamples(writeSamples, avgWriteSpeed, maxWriteSpeed);
        var readPoints = GetGraphSamples(readSamples, avgReadSpeed, maxReadSpeed);
        var allValues = writePoints.Select(p => p.SpeedMBps).Concat(readPoints.Select(p => p.SpeedMBps)).ToList();

        if (allValues.Count == 0)
        {
            return CertificateGraphData.Default;
        }

        var minSpeed = allValues.Min();
        var maxSpeed = allValues.Max();
        var spread = maxSpeed - minSpeed;
        if (spread < Math.Max(1d, maxSpeed * 0.02))
        {
            var center = (maxSpeed + minSpeed) / 2.0;
            var pad = Math.Max(1d, center * 0.05);
            minSpeed = Math.Max(0d, center - pad);
            maxSpeed = center + pad;
        }

        var tempPoints = temperatureSamples.OrderBy(t => t.ProgressPercent).ToList();
        var hasTemp = tempPoints.Count > 1;
        var temperaturePolyline = hasTemp ? BuildTemperaturePolylinePoints(tempPoints) : "10,110 490,110";

        return new CertificateGraphData(
            BuildPolylinePoints(writePoints, minSpeed, maxSpeed),
            BuildPolylinePoints(readPoints, minSpeed, maxSpeed),
            temperaturePolyline,
            hasTemp,
            $"{maxSpeed:F1} MB/s",
            $"{((maxSpeed + minSpeed) / 2):F1} MB/s",
            $"{minSpeed:F1} MB/s",
            "0 %",
            "50 %",
            "100 %");
    }

    private static List<SpeedSample> GetGraphSamples(List<SpeedSample> samples, double averageSpeed, double maxSpeed)
    {
        var values = samples.Where(s => s.SpeedMBps > 0).OrderBy(s => s.ProgressPercent).ToList();
        if (values.Count > 0)
        {
            return DownsampleGraphSamples(values, GraphTargetPoints);
        }

        if (averageSpeed <= 0 && maxSpeed <= 0)
        {
            return new List<SpeedSample>();
        }

        var peakSpeed = maxSpeed > 0 ? maxSpeed : averageSpeed;
        var baseSpeed = averageSpeed > 0 ? averageSpeed : peakSpeed;
        return new List<SpeedSample>
        {
            new() { ProgressPercent = 0, SpeedMBps = baseSpeed },
            new() { ProgressPercent = 50, SpeedMBps = peakSpeed },
            new() { ProgressPercent = 100, SpeedMBps = baseSpeed }
        };
    }

    private static string BuildPolylinePoints(List<SpeedSample> samples, double minSpeed, double maxSpeed)
    {
        if (samples.Count == 0)
        {
            return "10,102 70,102 130,102 190,102 250,102 310,102 370,102 430,102 490,102";
        }

        var startX = 10d;
        var endX = 490d;
        var minY = 18d;
        var maxY = 102d;
        var range = Math.Max(0.0001d, maxSpeed - minSpeed);
        var points = new List<string>(samples.Count);

        foreach (var sample in samples)
        {
            var x = startX + (Math.Clamp(sample.ProgressPercent, 0d, 100d) / 100d) * (endX - startX);
            var ratio = Math.Clamp((sample.SpeedMBps - minSpeed) / range, 0d, 1d);
            var y = maxY - ((maxY - minY) * ratio);
            points.Add(FormattableString.Invariant($"{x:0},{y:0}"));
        }

        return string.Join(" ", points);
    }

    private static string BuildTemperaturePolylinePoints(List<TemperatureSample> samples)
    {
        if (samples.Count == 0)
        {
            return "10,110 490,110";
        }

        var minTemp = samples.Min(s => s.TemperatureCelsius);
        var maxTemp = samples.Max(s => s.TemperatureCelsius);
        var range = Math.Max(1d, maxTemp - minTemp);
        var startX = 10d;
        var endX = 490d;
        var minY = 18d;
        var maxY = 102d;
        var points = new List<string>(samples.Count);

        foreach (var sample in samples)
        {
            var x = startX + (Math.Clamp(sample.ProgressPercent, 0d, 100d) / 100d) * (endX - startX);
            var ratio = Math.Clamp((sample.TemperatureCelsius - minTemp) / range, 0d, 1d);
            var y = maxY - ((maxY - minY) * ratio);
            points.Add(FormattableString.Invariant($"{x:0},{y:0}"));
        }

        return string.Join(" ", points);
    }

    private static List<SpeedSample> DownsampleGraphSamples(List<SpeedSample> values, int targetPoints)
    {
        if (values.Count <= targetPoints)
        {
            return values;
        }

        var result = new List<SpeedSample>(targetPoints);
        var bucketSize = values.Count / (double)targetPoints;

        for (var i = 0; i < targetPoints; i++)
        {
            var start = (int)Math.Floor(i * bucketSize);
            var end = (int)Math.Floor((i + 1) * bucketSize);
            end = Math.Clamp(end, start + 1, values.Count);

            var sumSpeed = 0d;
            var sumProgress = 0d;
            var count = 0;
            for (var j = start; j < end; j++)
            {
                sumSpeed += values[j].SpeedMBps;
                sumProgress += values[j].ProgressPercent;
                count++;
            }

            result.Add(new SpeedSample
            {
                ProgressPercent = count > 0 ? sumProgress / count : i * bucketSize,
                SpeedMBps = count > 0 ? sumSpeed / count : 0
            });
        }

        return result;
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
            if (!string.IsNullOrEmpty(SearchText))
            {
                var searchLower = SearchText.ToLowerInvariant();
                if (!cert.DiskModel.ToLowerInvariant().Contains(searchLower) &&
                    !cert.SerialNumber.ToLowerInvariant().Contains(searchLower) &&
                    !cert.CertificateNumber.ToLowerInvariant().Contains(searchLower))
                {
                    continue;
                }
            }

            if (!string.IsNullOrEmpty(SelectedGradeFilter) && SelectedGradeFilter != L.Get("CertificateBrowser.Status.All") && cert.Grade != SelectedGradeFilter)
            {
                continue;
            }

            FilteredCertificates.Add(cert);
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
    public string RecommendedText => Recommended ? "✅ Ano" : "—";
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
