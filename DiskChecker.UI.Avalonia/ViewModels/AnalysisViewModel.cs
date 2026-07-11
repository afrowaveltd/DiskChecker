using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DiskChecker.Core.Interfaces;
using DiskChecker.Core.Models;
using DiskChecker.Core.Services;
using DiskChecker.UI.Avalonia.Services.Interfaces;
using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;

namespace DiskChecker.UI.Avalonia.ViewModels;

public partial class AnalysisViewModel : ViewModelBase
{
    private readonly IDialogService _dialogService;
    private readonly ITestAnalysisDataService _analysisDataService;
    private readonly IDiskCardRepository _diskCardRepository;
    private readonly ISettingsService _settingsService;
    private readonly SmartTrendService _smartTrendService;

    private ObservableCollection<TestAnalysisSummary> _analysisSummaries = new();
    private TestAnalysisSummary? _selectedSummary;
    private TestAnalysisData? _analysisData;
    private ObservableCollection<string> _workspaceModes = new(Enum.GetNames<AnalysisWorkspaceMode>());
    private string _selectedWorkspaceMode = AnalysisWorkspaceMode.Auto.ToString();
    private AnalysisWorkspaceMode _effectiveWorkspaceMode = AnalysisWorkspaceMode.Compact;
    private double _availableWidth;
    private bool _isLoadingAnalysis;
    private string _statusMessage = "Analytické pracoviště připraveno";
    private string _throughputProgressWritePoints = string.Empty;
    private string _throughputProgressReadPoints = string.Empty;
    private string _throughputTimeWritePoints = string.Empty;
    private string _throughputTimeReadPoints = string.Empty;
    private string _seekLatencyPoints = string.Empty;
    private string _temperaturePoints = string.Empty;
    private ObservableCollection<AnalysisChartOverlayRegion> _anomalyOverlays = new();
    private ObservableCollection<AnalysisChartOverlayRegion> _stallOverlays = new();
    private ObservableCollection<AnalysisAnomalyListItem> _topAnomalies = new();
    private ObservableCollection<AnalysisStallListItem> _topStalls = new();
    private AnalysisAnomalyListItem? _selectedAnomaly;
    private AnalysisStallListItem? _selectedStall;
    private double? _zoomStartPercent;
    private double? _zoomEndPercent;

    // SMART trend properties
    private SmartTrendReport? _smartTrendReport;
    private string _smartTrendSummary = string.Empty;
    private string _smartWearAssessment = string.Empty;
    private string _smartTrendTemperaturePoints = string.Empty;
    private string _smartTrendReallocatedPoints = string.Empty;
    private string _smartTrendWearPoints = string.Empty;
    private string _smartTrendPercentageUsedPoints = string.Empty;
    private string _smartTrendPendingSectorsPoints = string.Empty;

    public AnalysisViewModel(
        IDialogService dialogService,
        ITestAnalysisDataService analysisDataService,
        IDiskCardRepository diskCardRepository,
        ISettingsService settingsService,
        SmartTrendService smartTrendService)
    {
        _dialogService = dialogService ?? throw new ArgumentNullException(nameof(dialogService));
        _analysisDataService = analysisDataService ?? throw new ArgumentNullException(nameof(analysisDataService));
        _diskCardRepository = diskCardRepository ?? throw new ArgumentNullException(nameof(diskCardRepository));
        _settingsService = settingsService ?? throw new ArgumentNullException(nameof(settingsService));
        _smartTrendService = smartTrendService ?? throw new ArgumentNullException(nameof(smartTrendService));

        RefreshWorkspaceCommand = new AsyncRelayCommand(LoadWorkspaceAsync, () => !IsLoadingAnalysis);
        LoadSelectedAnalysisCommand = new AsyncRelayCommand(LoadSelectedAnalysisAsync, () => SelectedSummary != null && !IsLoadingAnalysis);
        SelectAnomalyCommand = new RelayCommand<AnalysisAnomalyListItem>(SelectAnomaly);
        SelectStallCommand = new RelayCommand<AnalysisStallListItem>(SelectStall);
        ResetZoomCommand = new RelayCommand(ResetZoom);

        _ = InitializeWorkspaceAsync();
    }

    public ObservableCollection<TestAnalysisSummary> AnalysisSummaries { get => _analysisSummaries; set => SetProperty(ref _analysisSummaries, value); }

    public TestAnalysisSummary? SelectedSummary
    {
        get => _selectedSummary;
        set
        {
            if (SetProperty(ref _selectedSummary, value))
            {
                LoadSelectedAnalysisCommand.NotifyCanExecuteChanged();
                _ = LoadSelectedAnalysisAsync();
            }
        }
    }

    public TestAnalysisData? AnalysisData
    {
        get => _analysisData;
        set
        {
            if (SetProperty(ref _analysisData, value))
            {
                RebuildChartData();
                OnPropertyChanged(nameof(HasAnalysisData));
                OnPropertyChanged(nameof(CompactPrimaryChartTitle));
                OnPropertyChanged(nameof(SummaryLine));
                OnPropertyChanged(nameof(FullTelemetryLine));
                OnPropertyChanged(nameof(FullEventsLine));
                OnPropertyChanged(nameof(SmartAnalysisLine));
                OnPropertyChanged(nameof(SmartAnalysisDetails));
            }
        }
    }

    public bool HasAnalysisData => AnalysisData != null;
    public ObservableCollection<string> WorkspaceModes => _workspaceModes;

    public string SelectedWorkspaceMode
    {
        get => _selectedWorkspaceMode;
        set
        {
            if (SetProperty(ref _selectedWorkspaceMode, value))
            {
                _ = SaveWorkspaceModeAsync();
                UpdateEffectiveMode();
            }
        }
    }

    public AnalysisWorkspaceMode EffectiveWorkspaceMode
    {
        get => _effectiveWorkspaceMode;
        private set
        {
            if (SetProperty(ref _effectiveWorkspaceMode, value))
            {
                OnPropertyChanged(nameof(IsCompactLayout));
                OnPropertyChanged(nameof(IsFullLayout));
                OnPropertyChanged(nameof(EffectiveWorkspaceModeText));
            }
        }
    }

    public double AvailableWidth
    {
        get => _availableWidth;
        set
        {
            if (SetProperty(ref _availableWidth, value))
                UpdateEffectiveMode();
        }
    }

    public bool IsCompactLayout => EffectiveWorkspaceMode == AnalysisWorkspaceMode.Compact;
    public bool IsFullLayout => EffectiveWorkspaceMode == AnalysisWorkspaceMode.Full;
    public string EffectiveWorkspaceModeText => $"Režim: {EffectiveWorkspaceMode}";

    public bool IsLoadingAnalysis
    {
        get => _isLoadingAnalysis;
        set { if (SetProperty(ref _isLoadingAnalysis, value)) { RefreshWorkspaceCommand.NotifyCanExecuteChanged(); LoadSelectedAnalysisCommand.NotifyCanExecuteChanged(); } }
    }

    public string StatusMessage { get => _statusMessage; set => SetProperty(ref _statusMessage, value); }

    public string ThroughputProgressWritePoints { get => _throughputProgressWritePoints; set => SetProperty(ref _throughputProgressWritePoints, value); }
    public string ThroughputProgressReadPoints { get => _throughputProgressReadPoints; set => SetProperty(ref _throughputProgressReadPoints, value); }
    public string ThroughputTimeWritePoints { get => _throughputTimeWritePoints; set => SetProperty(ref _throughputTimeWritePoints, value); }
    public string ThroughputTimeReadPoints { get => _throughputTimeReadPoints; set => SetProperty(ref _throughputTimeReadPoints, value); }
    public string SeekLatencyPoints { get => _seekLatencyPoints; set => SetProperty(ref _seekLatencyPoints, value); }
    public string TemperaturePoints { get => _temperaturePoints; set => SetProperty(ref _temperaturePoints, value); }

    public bool HasThroughputGraph => !string.IsNullOrWhiteSpace(ThroughputProgressWritePoints) || !string.IsNullOrWhiteSpace(ThroughputProgressReadPoints);
    public bool HasSeekGraph => !string.IsNullOrWhiteSpace(SeekLatencyPoints);
    public bool HasTemperatureGraph => !string.IsNullOrWhiteSpace(TemperaturePoints);
    public ObservableCollection<AnalysisChartOverlayRegion> AnomalyOverlays { get => _anomalyOverlays; set => SetProperty(ref _anomalyOverlays, value); }
    public ObservableCollection<AnalysisChartOverlayRegion> StallOverlays { get => _stallOverlays; set => SetProperty(ref _stallOverlays, value); }
    public ObservableCollection<AnalysisAnomalyListItem> TopAnomalies { get => _topAnomalies; set => SetProperty(ref _topAnomalies, value); }
    public ObservableCollection<AnalysisStallListItem> TopStalls { get => _topStalls; set => SetProperty(ref _topStalls, value); }

    public AnalysisAnomalyListItem? SelectedAnomaly
    {
        get => _selectedAnomaly;
        set
        {
            if (SetProperty(ref _selectedAnomaly, value))
            {
                OnPropertyChanged(nameof(SelectedAnomalyDetail));
            }
        }
    }

    public AnalysisStallListItem? SelectedStall
    {
        get => _selectedStall;
        set
        {
            if (SetProperty(ref _selectedStall, value))
            {
                OnPropertyChanged(nameof(SelectedStallDetail));
            }
        }
    }

    public string SelectedAnomalyDetail => SelectedAnomaly?.DetailText ?? "Vyber anomálii pro detail.";
    public string SelectedStallDetail => SelectedStall?.DetailText ?? "Vyber stall pro detail.";
    public bool IsZoomActive => _zoomStartPercent.HasValue && _zoomEndPercent.HasValue;
    public string ZoomText => IsZoomActive ? $"Zoom: {_zoomStartPercent:F2}-{_zoomEndPercent:F2}%" : "Zoom: celý rozsah";

    public string SummaryLine => SelectedSummary == null
        ? "Vyber měření pro detailní analýzu."
        : $"{SelectedSummary.DiskModel} | {SelectedSummary.TestType} | {SelectedSummary.StartedAt:g} | známka {SelectedSummary.Grade}, skóre {SelectedSummary.Score:F0}";

    public string CompactPrimaryChartTitle => AnalysisData?.HasSeekSamples == true
        ? "Hlavní graf: seek latency podle indexu"
        : "Hlavní graf: throughput podle průběhu disku";

    public string FullTelemetryLine => AnalysisData == null
        ? "Telemetrie není načtena."
        : $"Telemetry: {AnalysisData.TelemetrySamples.Count} bodů, teploty: {AnalysisData.TemperatureSamples.Count}, seek: {AnalysisData.SeekSamples.Count}";

    public string FullEventsLine => AnalysisData == null
        ? "Události nejsou načteny."
        : $"Anomálie: {AnalysisData.AnomalyEvents.Count}, stally: {AnalysisData.StallEvents.Count}";

    public string SmartAnalysisLine => AnalysisData?.SmartReport?.Summary ?? "SMART: data nejsou načtena.";

    // ========== SMART Trend Properties ==========

    public SmartTrendReport? SmartTrendReport
    {
        get => _smartTrendReport;
        set
        {
            if (SetProperty(ref _smartTrendReport, value))
            {
                OnPropertyChanged(nameof(HasSmartTrendData));
                OnPropertyChanged(nameof(SmartTrendSummary));
                OnPropertyChanged(nameof(SmartWearAssessment));
                OnPropertyChanged(nameof(HasSmartWearAssessment));
                OnPropertyChanged(nameof(SmartTrendSnapshotCount));
                OnPropertyChanged(nameof(SmartTrendDateRange));
            }
        }
    }

    public bool HasSmartTrendData => SmartTrendReport != null && SmartTrendReport.SnapshotCount >= 2;

    public string SmartTrendSummary
    {
        get => SmartTrendReport?.Summary ?? "SMART trendy nejsou k dispozici.";
        set => SetProperty(ref _smartTrendSummary, value);
    }

    public string SmartWearAssessment
    {
        get => SmartTrendReport?.WearAssessment?.Description ?? "N/A";
        set => SetProperty(ref _smartWearAssessment, value);
    }

    public bool HasSmartWearAssessment => SmartTrendReport?.WearAssessment != null;

    public string SmartTrendSnapshotCount => SmartTrendReport != null
        ? $"{SmartTrendReport.SnapshotCount} snapshotů"
        : "0";

    public string SmartTrendDateRange => SmartTrendReport?.FirstSnapshot != null && SmartTrendReport?.LastSnapshot != null
        ? $"{SmartTrendReport.FirstSnapshot:dd.MM.yyyy} – {SmartTrendReport.LastSnapshot:dd.MM.yyyy}"
        : "N/A";

    public string SmartTrendTemperaturePoints
    {
        get => _smartTrendTemperaturePoints;
        set => SetProperty(ref _smartTrendTemperaturePoints, value);
    }

    public string SmartTrendReallocatedPoints
    {
        get => _smartTrendReallocatedPoints;
        set => SetProperty(ref _smartTrendReallocatedPoints, value);
    }

    public string SmartTrendWearPoints
    {
        get => _smartTrendWearPoints;
        set => SetProperty(ref _smartTrendWearPoints, value);
    }

    public string SmartTrendPercentageUsedPoints
    {
        get => _smartTrendPercentageUsedPoints;
        set => SetProperty(ref _smartTrendPercentageUsedPoints, value);
    }

    public string SmartTrendPendingSectorsPoints
    {
        get => _smartTrendPendingSectorsPoints;
        set => SetProperty(ref _smartTrendPendingSectorsPoints, value);
    }

    public bool HasSmartTrendCharts =>
        !string.IsNullOrWhiteSpace(SmartTrendTemperaturePoints) ||
        !string.IsNullOrWhiteSpace(SmartTrendReallocatedPoints) ||
        !string.IsNullOrWhiteSpace(SmartTrendWearPoints) ||
        !string.IsNullOrWhiteSpace(SmartTrendPercentageUsedPoints) ||
        !string.IsNullOrWhiteSpace(SmartTrendPendingSectorsPoints);

    public string SmartAnalysisDetails => AnalysisData?.SmartReport == null
        ? string.Empty
        : string.Join(Environment.NewLine, AnalysisData.SmartReport.WearIndicators
            .Concat(AnalysisData.SmartReport.Deltas)
            .OrderByDescending(d => d.Severity)
            .Take(8)
            .Select(d => $"{d.Severity}: {d.Name} {FormatNullable(d.Before)} -> {FormatNullable(d.After)} (Δ {FormatNullable(d.Delta)}) - {d.Note}"));

    private static string FormatNullable(long? value) => value.HasValue ? value.Value.ToString("N0") : "n/a";

    public IAsyncRelayCommand RefreshWorkspaceCommand { get; }
    public IAsyncRelayCommand LoadSelectedAnalysisCommand { get; }
    public IRelayCommand<AnalysisAnomalyListItem> SelectAnomalyCommand { get; }
    public IRelayCommand<AnalysisStallListItem> SelectStallCommand { get; }
    public IRelayCommand ResetZoomCommand { get; }

    private void RebuildChartData()
    {
        const double width = 520;
        const double height = 180;
        if (AnalysisData == null)
        {
            ThroughputProgressWritePoints = ThroughputProgressReadPoints = string.Empty;
            ThroughputTimeWritePoints = ThroughputTimeReadPoints = string.Empty;
            SeekLatencyPoints = TemperaturePoints = string.Empty;
            AnomalyOverlays = new ObservableCollection<AnalysisChartOverlayRegion>();
            StallOverlays = new ObservableCollection<AnalysisChartOverlayRegion>();
            TopAnomalies = new ObservableCollection<AnalysisAnomalyListItem>();
            TopStalls = new ObservableCollection<AnalysisStallListItem>();
            SelectedAnomaly = null;
            SelectedStall = null;
            NotifyChartFlags();
            return;
        }

        var zoomStart = _zoomStartPercent ?? 0;
        var zoomEnd = _zoomEndPercent ?? 100;
        var write = AnalysisData.TelemetrySamples
            .Where(s => s.Phase is TelemetrySamplePhase.Write or TelemetrySamplePhase.Sanitize1Write or TelemetrySamplePhase.Sanitize2Write)
            .Where(s => !IsZoomActive || (s.ProgressPercent >= zoomStart && s.ProgressPercent <= zoomEnd))
            .OrderBy(s => s.SequenceIndex)
            .ToList();
        var read = AnalysisData.TelemetrySamples
            .Where(s => s.Phase is TelemetrySamplePhase.Read or TelemetrySamplePhase.Sanitize1Read or TelemetrySamplePhase.Sanitize2Read)
            .Where(s => !IsZoomActive || (s.ProgressPercent >= zoomStart && s.ProgressPercent <= zoomEnd))
            .OrderBy(s => s.SequenceIndex)
            .ToList();
        var maxSpeed = write.Concat(read).Where(s => s.SpeedMBps > 0).Select(s => s.SpeedMBps).DefaultIfEmpty(1).Max();
        ThroughputProgressWritePoints = BuildTelemetryPolyline(write, width, height, maxSpeed, useTimeAxis: false, zoomStart, zoomEnd, IsZoomActive);
        ThroughputProgressReadPoints = BuildTelemetryPolyline(read, width, height, maxSpeed, useTimeAxis: false, zoomStart, zoomEnd, IsZoomActive);
        ThroughputTimeWritePoints = BuildTelemetryPolyline(write, width, height, maxSpeed, useTimeAxis: true);
        ThroughputTimeReadPoints = BuildTelemetryPolyline(read, width, height, maxSpeed, useTimeAxis: true);

        var seekMax = AnalysisData.SeekSamples.Where(s => s.LatencyMs > 0).Select(s => s.LatencyMs).DefaultIfEmpty(1).Max();
        SeekLatencyPoints = BuildSeekPolyline(AnalysisData.SeekSamples, width, height, seekMax);

        var tempValues = AnalysisData.TemperatureSamples.OrderBy(t => t.Timestamp).ToList();
        var minTemp = tempValues.Select(t => t.TemperatureCelsius).DefaultIfEmpty(0).Min();
        var maxTemp = tempValues.Select(t => t.TemperatureCelsius).DefaultIfEmpty(1).Max();
        TemperaturePoints = BuildTemperaturePolyline(tempValues, width, height, minTemp, maxTemp);

        AnomalyOverlays = new ObservableCollection<AnalysisChartOverlayRegion>(
            AnalysisData.AnomalyEvents
                .OrderByDescending(a => a.SeverityScore)
                .Take(30)
                .Where(a => !IsZoomActive || (a.EndProgressPercent >= zoomStart && a.StartProgressPercent <= zoomEnd))
                .Select(a => BuildProgressOverlay(a.StartProgressPercent, a.EndProgressPercent, width, $"A {a.SeverityScore:F0}", zoomStart, zoomEnd, IsZoomActive)));
        StallOverlays = new ObservableCollection<AnalysisChartOverlayRegion>(
            AnalysisData.StallEvents
                .OrderByDescending(s => s.DurationMs)
                .Take(30)
                .Where(s => !IsZoomActive || (s.EndProgressPercent >= zoomStart && s.StartProgressPercent <= zoomEnd))
                .Select(s => BuildProgressOverlay(s.StartProgressPercent, s.EndProgressPercent, width, $"S {s.DurationMs / 1000:F1}s", zoomStart, zoomEnd, IsZoomActive)));
        TopAnomalies = new ObservableCollection<AnalysisAnomalyListItem>(AnalysisData.AnomalyEvents
            .OrderByDescending(a => a.SeverityScore)
            .Take(8)
            .Select(a => new AnalysisAnomalyListItem(a)));
        TopStalls = new ObservableCollection<AnalysisStallListItem>(AnalysisData.StallEvents
            .OrderByDescending(s => s.DurationMs)
            .Take(8)
            .Select(s => new AnalysisStallListItem(s)));
        SelectedAnomaly = TopAnomalies.FirstOrDefault();
        SelectedStall = TopStalls.FirstOrDefault();
        NotifyChartFlags();
    }

    private static AnalysisChartOverlayRegion BuildProgressOverlay(double startPercent, double endPercent, double chartWidth, string label, double zoomStart = 0, double zoomEnd = 100, bool zoom = false)
    {
        var start = Math.Clamp(Math.Min(startPercent, endPercent), zoom ? zoomStart : 0, zoom ? zoomEnd : 100);
        var end = Math.Clamp(Math.Max(startPercent, endPercent), zoom ? zoomStart : 0, zoom ? zoomEnd : 100);
        var range = Math.Max(0.001, (zoom ? zoomEnd - zoomStart : 100));
        var x = (start - (zoom ? zoomStart : 0)) / range * chartWidth;
        var width = Math.Max(2, (end - start) / range * chartWidth);
        return new AnalysisChartOverlayRegion(x, width, label);
    }

    private void NotifyChartFlags()
    {
        OnPropertyChanged(nameof(HasThroughputGraph));
        OnPropertyChanged(nameof(HasSeekGraph));
        OnPropertyChanged(nameof(HasTemperatureGraph));
    }

    private static string BuildTelemetryPolyline(IReadOnlyList<TestTelemetrySample> samples, double width, double height, double maxSpeed, bool useTimeAxis, double zoomStart = 0, double zoomEnd = 100, bool zoom = false)
    {
        var valid = samples.Where(s => s.SpeedMBps > 0 || s.IsStalled).ToList();
        if (valid.Count == 0) return string.Empty;
        var maxElapsed = valid.Select(s => s.ElapsedMs ?? 0).DefaultIfEmpty(0).Max();
        if (useTimeAxis && maxElapsed <= 0)
            maxElapsed = Math.Max(1, valid.Count - 1);
        return string.Join(' ', valid.Select((s, i) =>
        {
            var xRatio = useTimeAxis
                ? ((s.ElapsedMs ?? i) / Math.Max(1, maxElapsed))
                : zoom
                    ? (Math.Clamp(s.ProgressPercent, zoomStart, zoomEnd) - zoomStart) / Math.Max(0.001, zoomEnd - zoomStart)
                    : Math.Clamp(s.ProgressPercent, 0, 100) / 100.0;
            var speed = s.IsStalled ? 0 : Math.Max(0, s.SpeedMBps);
            var yRatio = speed / Math.Max(1, maxSpeed);
            return $"{xRatio * width:F1},{height - yRatio * height:F1}";
        }));
    }

    private static string BuildSeekPolyline(IReadOnlyList<SeekSampleRecord> samples, double width, double height, double maxLatency)
    {
        var valid = samples.Where(s => s.LatencyMs > 0 && !s.HasError).OrderBy(s => s.Index).ToList();
        if (valid.Count == 0) return string.Empty;
        var maxIndex = Math.Max(1, valid.Max(s => s.Index));
        return string.Join(' ', valid.Select(s =>
        {
            var x = (s.Index - 1) / (double)Math.Max(1, maxIndex - 1) * width;
            var y = height - (s.LatencyMs / Math.Max(1, maxLatency) * height);
            return $"{x:F1},{y:F1}";
        }));
    }

    private static string BuildTemperaturePolyline(IReadOnlyList<TemperatureSample> samples, double width, double height, int minTemp, int maxTemp)
    {
        if (samples.Count == 0) return string.Empty;
        var range = Math.Max(1, maxTemp - minTemp);
        return string.Join(' ', samples.Select((s, i) =>
        {
            var x = samples.Count == 1 ? 0 : i / (double)(samples.Count - 1) * width;
            var y = height - ((s.TemperatureCelsius - minTemp) / (double)range * height);
            return $"{x:F1},{y:F1}";
        }));
    }

    private void SelectAnomaly(AnalysisAnomalyListItem? item)
    {
        if (item == null) return;
        SelectedAnomaly = item;
        SetZoom(item.Anomaly.StartProgressPercent, item.Anomaly.EndProgressPercent);
        StatusMessage = $"Vybrána anomálie: {item.DisplayText}";
    }

    private void SelectStall(AnalysisStallListItem? item)
    {
        if (item == null) return;
        SelectedStall = item;
        SetZoom(item.Stall.StartProgressPercent, item.Stall.EndProgressPercent);
        StatusMessage = $"Vybrán stall: {item.DisplayText}";
    }

    private void SetZoom(double startPercent, double endPercent)
    {
        var start = Math.Clamp(Math.Min(startPercent, endPercent) - 0.5, 0, 100);
        var end = Math.Clamp(Math.Max(startPercent, endPercent) + 0.5, 0, 100);
        if (end - start < 1)
            end = Math.Min(100, start + 1);
        _zoomStartPercent = start;
        _zoomEndPercent = end;
        OnPropertyChanged(nameof(IsZoomActive));
        OnPropertyChanged(nameof(ZoomText));
        RebuildChartData();
    }

    private void ResetZoom()
    {
        _zoomStartPercent = null;
        _zoomEndPercent = null;
        OnPropertyChanged(nameof(IsZoomActive));
        OnPropertyChanged(nameof(ZoomText));
        RebuildChartData();
    }

    private async Task InitializeWorkspaceAsync()
    {
        var mode = await _settingsService.GetAnalysisWorkspaceModeAsync();
        _selectedWorkspaceMode = mode.ToString();
        OnPropertyChanged(nameof(SelectedWorkspaceMode));
        UpdateEffectiveMode();
        await LoadWorkspaceAsync();
    }

    private async Task SaveWorkspaceModeAsync()
    {
        if (Enum.TryParse<AnalysisWorkspaceMode>(SelectedWorkspaceMode, out var mode))
            await _settingsService.SetAnalysisWorkspaceModeAsync(mode);
    }

    private void UpdateEffectiveMode()
    {
        var selected = Enum.TryParse<AnalysisWorkspaceMode>(SelectedWorkspaceMode, out var parsed) ? parsed : AnalysisWorkspaceMode.Auto;
        EffectiveWorkspaceMode = selected switch
        {
            AnalysisWorkspaceMode.Compact => AnalysisWorkspaceMode.Compact,
            AnalysisWorkspaceMode.Full => AnalysisWorkspaceMode.Full,
            _ => AvailableWidth >= 1400 ? AnalysisWorkspaceMode.Full : AnalysisWorkspaceMode.Compact
        };
    }

    private async Task LoadWorkspaceAsync()
    {
        try
        {
            IsLoadingAnalysis = true;
            StatusMessage = "Načítám analytická data...";
            var cards = await _diskCardRepository.GetAllAsync();
            var all = new ObservableCollection<TestAnalysisSummary>();
            foreach (var card in cards.OrderBy(c => c.ModelName))
            {
                var summaries = await _analysisDataService.GetDiskAnalysisSummariesAsync(card.Id);
                foreach (var summary in summaries)
                    all.Add(summary);
            }
            AnalysisSummaries = all;
            SelectedSummary ??= AnalysisSummaries.FirstOrDefault();
            StatusMessage = $"Načteno {AnalysisSummaries.Count} měření pro analýzu.";
        }
        catch (Exception ex)
        {
            StatusMessage = $"Chyba načítání analýzy: {ex.Message}";
            await _dialogService.ShowErrorAsync(L.Get("Common.Error"), StatusMessage);
        }
        finally
        {
            IsLoadingAnalysis = false;
        }
    }

    private async Task LoadSelectedAnalysisAsync()
    {
        if (SelectedSummary == null) return;
        try
        {
            IsLoadingAnalysis = true;
            AnalysisData = await _analysisDataService.GetAnalysisDataAsync(SelectedSummary.TestSessionId);
            StatusMessage = AnalysisData == null ? "Měření nebylo nalezeno." : $"Načten detail testu {SelectedSummary.TestSessionId}.";

            // Load SMART trends for this disk
            await LoadSmartTrendsAsync(SelectedSummary.DiskCardId);
        }
        catch (Exception ex)
        {
            StatusMessage = $"Chyba detailu analýzy: {ex.Message}";
            await _dialogService.ShowErrorAsync(L.Get("Common.Error"), StatusMessage);
        }
        finally
        {
            IsLoadingAnalysis = false;
        }
    }

    private async Task LoadSmartTrendsAsync(int diskCardId)
    {
        try
        {
            var report = await _smartTrendService.BuildTrendReportAsync(diskCardId);
            SmartTrendReport = report;

            if (report.SnapshotCount >= 2)
            {
                // Build chart data for key metrics
                const double chartWidth = 520;
                const double chartHeight = 160;

                var tempTrend = report.Trends.FirstOrDefault(t => t.MetricName == "Teplota");
                if (tempTrend != null)
                    SmartTrendTemperaturePoints = _smartTrendService.BuildChartData(tempTrend, chartWidth, chartHeight).PolylinePoints;

                var reallocTrend = report.Trends.FirstOrDefault(t => t.MetricName == "Reallocated Sectors");
                if (reallocTrend != null)
                    SmartTrendReallocatedPoints = _smartTrendService.BuildChartData(reallocTrend, chartWidth, chartHeight).PolylinePoints;

                var wearTrend = report.Trends.FirstOrDefault(t => t.MetricName == "Wear Leveling");
                if (wearTrend != null)
                    SmartTrendWearPoints = _smartTrendService.BuildChartData(wearTrend, chartWidth, chartHeight).PolylinePoints;

                var pctUsedTrend = report.Trends.FirstOrDefault(t => t.MetricName == "Percentage Used");
                if (pctUsedTrend != null)
                    SmartTrendPercentageUsedPoints = _smartTrendService.BuildChartData(pctUsedTrend, chartWidth, chartHeight).PolylinePoints;

                var pendingTrend = report.Trends.FirstOrDefault(t => t.MetricName == "Pending Sectors");
                if (pendingTrend != null)
                    SmartTrendPendingSectorsPoints = _smartTrendService.BuildChartData(pendingTrend, chartWidth, chartHeight).PolylinePoints;

                OnPropertyChanged(nameof(HasSmartTrendCharts));
            }
        }
        catch
        {
            // Non-critical - trends are supplementary
            SmartTrendReport = null;
        }
    }

}

public sealed record AnalysisChartOverlayRegion(double X, double Width, string Label);

public sealed class AnalysisAnomalyListItem
{
    public AnalysisAnomalyListItem(TestAnomalyEvent anomaly) => Anomaly = anomaly;
    public TestAnomalyEvent Anomaly { get; }
    public string DisplayText => $"Severity {Anomaly.SeverityScore:F0} | @{Anomaly.StartProgressPercent:F1}-{Anomaly.EndProgressPercent:F1}% | {Anomaly.MinSpeedMBps:F1}-{Anomaly.MaxSpeedMBps:F1} MB/s";
    public string DetailText => $"Anomálie ({Anomaly.Phase})\nProgress: {Anomaly.StartProgressPercent:F2}-{Anomaly.EndProgressPercent:F2}%\nTrvání: {Anomaly.DurationMs / 1000:F2} s\nRychlost min/avg/max: {Anomaly.MinSpeedMBps:F1}/{Anomaly.AvgSpeedMBps:F1}/{Anomaly.MaxSpeedMBps:F1} MB/s\nOdchylka: {Anomaly.MaxDeviationPercent:F1}% | Severity: {Anomaly.SeverityScore:F0}\nLBA512: {Anomaly.StartLba512:N0}-{Anomaly.EndLba512:N0}\nBytes: {Anomaly.StartBytesProcessed:N0}-{Anomaly.EndBytesProcessed:N0}\nTyp defektu: {Anomaly.DefectType ?? "n/a"}";
}

public sealed class AnalysisStallListItem
{
    public AnalysisStallListItem(TestStallEvent stall) => Stall = stall;
    public TestStallEvent Stall { get; }
    public string DisplayText => $"{Stall.DurationMs / 1000:F2}s | @{Stall.StartProgressPercent:F1}-{Stall.EndProgressPercent:F1}% | {FormatSpeed(Stall.LastSpeedBeforeStallMBps)} -> {FormatSpeed(Stall.FirstSpeedAfterStallMBps)}";
    public string DetailText => $"Stall ({Stall.Phase})\nČas: {Stall.StartedAtUtc:HH:mm:ss.fff} - {Stall.EndedAtUtc:HH:mm:ss.fff} UTC\nTrvání: {Stall.DurationMs / 1000:F3} s\nProgress: {Stall.StartProgressPercent:F2}-{Stall.EndProgressPercent:F2}%\nBytes: {Stall.BytesProcessed:N0}\nRychlost před/po: {FormatSpeed(Stall.LastSpeedBeforeStallMBps)} -> {FormatSpeed(Stall.FirstSpeedAfterStallMBps)}";
    private static string FormatSpeed(double? value) => value.HasValue ? $"{value.Value:F1} MB/s" : "n/a";
}
