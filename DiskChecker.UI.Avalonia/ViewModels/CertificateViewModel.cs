using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DiskChecker.Core.Interfaces;
using DiskChecker.Core.Models;
using DiskChecker.UI.Avalonia.Services.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace DiskChecker.UI.Avalonia.ViewModels;

/// <summary>
/// View model for displaying and generating disk certificates.
/// </summary>
public partial class CertificateViewModel : ViewModelBase, INavigableViewModel
{
    private readonly ICertificateGenerator _certificateGenerator;
    private readonly IDiskCardRepository _diskCardRepository;
    private readonly INavigationService _navigationService;
    private readonly IDialogService _dialogService;
    private readonly ISelectedDiskService _selectedDiskService;
    
    private DiskCertificate? _certificate;
    private bool _isLoading;
    private string _statusMessage = "Certifikát disku";
    private bool _isBlackAndWhiteMode;
    private TestSession? _selectedSession;
    private List<SpeedSample> _writeGraphSamples = [];
    private List<SpeedSample> _readGraphSamples = [];
    private string _writeProfilePoints = "10,62 70,58 130,56 190,54 250,50 310,48 370,45 430,42 490,40";
    private string _readProfilePoints = "10,66 70,61 130,58 190,55 250,53 310,50 370,47 430,45 490,43";
    private string _chartMaxSpeedLabel = "0 MB/s";
    private string _chartMidSpeedLabel = "0 MB/s";
    private string _chartMinSpeedLabel = "0 MB/s";
    private string _chartXAxisStartLabel = "0 %";
    private string _chartXAxisMidLabel = "50 %";
    private string _chartXAxisEndLabel = "100 %";

    public CertificateViewModel(
        ICertificateGenerator certificateGenerator,
        IDiskCardRepository diskCardRepository,
        INavigationService navigationService,
        IDialogService dialogService,
        ISelectedDiskService selectedDiskService)
    {
        _certificateGenerator = certificateGenerator;
        _diskCardRepository = diskCardRepository;
        _navigationService = navigationService;
        _dialogService = dialogService;
        _selectedDiskService = selectedDiskService;
    }

    #region Properties

    public DiskCertificate? Certificate
    {
        get => _certificate;
        private set
        {
            if (SetProperty(ref _certificate, value))
            {
                OnPropertyChanged(nameof(HasCertificate));
                OnPropertyChanged(nameof(CurrentDateTime));
                OnPropertyChanged(nameof(CertificateNumber));
                OnPropertyChanged(nameof(DiskModel));
                OnPropertyChanged(nameof(SerialNumber));
                OnPropertyChanged(nameof(Capacity));
                OnPropertyChanged(nameof(DiskType));
                OnPropertyChanged(nameof(Grade));
                OnPropertyChanged(nameof(Score));
                OnPropertyChanged(nameof(HealthStatus));
                OnPropertyChanged(nameof(TestType));
                OnPropertyChanged(nameof(TestDuration));
                OnPropertyChanged(nameof(AvgWriteSpeed));
                OnPropertyChanged(nameof(AvgReadSpeed));
                OnPropertyChanged(nameof(TemperatureRange));
                OnPropertyChanged(nameof(Errors));
                OnPropertyChanged(nameof(SmartPassed));
                OnPropertyChanged(nameof(Recommended));
                OnPropertyChanged(nameof(RecommendationText));
                OnPropertyChanged(nameof(ScoringReasonsText));
                OnPropertyChanged(nameof(HasScoringReasons));
                OnPropertyChanged(nameof(SmartPowerOnHoursText));
                OnPropertyChanged(nameof(SmartPowerCyclesText));
                OnPropertyChanged(nameof(SmartReallocatedSectorsText));
                OnPropertyChanged(nameof(SmartPendingSectorsText));
            }
        }
    }

    public bool IsLoading
    {
        get => _isLoading;
        set => SetProperty(ref _isLoading, value);
    }

    /// <summary>
    /// Gets or sets whether certificate preview uses black-and-white print-safe mode.
    /// </summary>
    public bool IsBlackAndWhiteMode
    {
        get => _isBlackAndWhiteMode;
        set
        {
            if (SetProperty(ref _isBlackAndWhiteMode, value))
            {
                OnPropertyChanged(nameof(SealBackground));
                OnPropertyChanged(nameof(PanelBackground));
                OnPropertyChanged(nameof(GradeColor));
                OnPropertyChanged(nameof(ChartStrokeWrite));
                OnPropertyChanged(nameof(ChartStrokeRead));
            }
        }
    }

    public string StatusMessage
    {
        get => _statusMessage;
        set => SetProperty(ref _statusMessage, value);
    }

    public bool HasCertificate => Certificate != null;

    public DateTime CurrentDateTime => DateTime.Now;

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


    // Certificate display properties
    public string CertificateNumber => Certificate?.CertificateNumber ?? "-";
    public string DiskModel => Certificate?.DiskModel ?? "Neznámý";
    public string SerialNumber => Certificate?.SerialNumber ?? "-";
    public string Capacity => Certificate?.Capacity ?? "-";
    public string DiskType => Certificate?.DiskType ?? "-";
    public string Grade => Certificate?.Grade ?? "?";
    public string Score => Certificate != null ? $"{Certificate.Score:F0}/100" : "-";
    public string HealthStatus => Certificate?.HealthStatus ?? "-";
    public string TestType => Certificate?.TestType ?? "-";
    public string TestDuration => Certificate != null ? Certificate.TestDuration.ToString(@"hh\:mm\:ss") : "-";
    public string AvgWriteSpeed => Certificate != null ? $"{Certificate.AvgWriteSpeed:F1} MB/s" : "-";
    public string AvgReadSpeed => Certificate != null ? $"{Certificate.AvgReadSpeed:F1} MB/s" : "-";
    public string TemperatureRange => Certificate?.TemperatureRange ?? "-";
    public string Errors => Certificate?.ErrorCount.ToString() ?? "0";
    public bool SmartPassed => Certificate?.SmartPassed ?? false;
    public string SmartPowerOnHoursText => Certificate?.PowerOnHours > 0 ? $"{Certificate.PowerOnHours:#,0}" : "N/A";
    public string SmartPowerCyclesText => Certificate?.PowerCycles > 0 ? $"{Certificate.PowerCycles:#,0}" : "N/A";
    public string SmartReallocatedSectorsText => (Certificate?.ReallocatedSectors ?? 0).ToString(CultureInfo.InvariantCulture);
    public string SmartPendingSectorsText => (Certificate?.PendingSectors ?? 0).ToString(CultureInfo.InvariantCulture);
    public bool Recommended => Certificate?.Recommended ?? false;
    public string RecommendationText => Certificate?.RecommendationNotes ?? "Není k dispozici";
    public string ScoringReasonsText => string.IsNullOrWhiteSpace(Certificate?.Notes)
        ? "Bez významných varování."
        : Certificate.Notes;
    public bool HasScoringReasons => !string.IsNullOrWhiteSpace(Certificate?.Notes);

    // Grade color
    public string GradeColor => (Certificate?.Grade ?? "?") switch
    {
        "A" => IsBlackAndWhiteMode ? "#111111" : "#27AE60",
        "B" => IsBlackAndWhiteMode ? "#222222" : "#2ECC71",
        "C" => IsBlackAndWhiteMode ? "#333333" : "#F1C40F",
        "D" => IsBlackAndWhiteMode ? "#444444" : "#E67E22",
        "E" => IsBlackAndWhiteMode ? "#555555" : "#E74C3C",
        "F" => IsBlackAndWhiteMode ? "#666666" : "#C0392B",
        _ => "#95A5A6"
    };

    public string SealBackground => IsBlackAndWhiteMode ? "#F5F5F5" : "#F8FAFC";
    public string PanelBackground => IsBlackAndWhiteMode ? "#FAFAFA" : "#F6F8FB";
    public string ChartStrokeWrite => IsBlackAndWhiteMode ? "#1F2937" : "#DC2626";
    public string ChartStrokeRead => IsBlackAndWhiteMode ? "#4B5563" : "#059669";

    #endregion

    #region Navigation

    public void OnNavigatedTo()
    {
        _ = LoadCertificateAsync();
    }

    #endregion

    #region Commands

    [RelayCommand]
    private async Task GenerateNewCertificateAsync()
    {
        if (_selectedDiskService.SelectedDisk == null)
        {
            StatusMessage = "Není vybrán žádný disk";
            return;
        }

        try
        {
            IsLoading = true;
            StatusMessage = "Generuji certifikát...";

            var card = await _diskCardRepository.GetByDevicePathAsync(_selectedDiskService.SelectedDisk.Path);

            if (card == null)
            {
                StatusMessage = "Karta disku nenalezena. Proveďte nejprve test.";
                await _dialogService.ShowErrorAsync("Chyba", "Karta disku nenalezena. Proveďte nejprve test disku.");
                return;
            }

            var sessions = await _diskCardRepository.GetTestSessionsAsync(card.Id);
            var latestSession = sessions.FirstOrDefault();

            if (latestSession == null)
            {
                StatusMessage = "Žádný test nenalezen. Proveďte nejprve test.";
                await _dialogService.ShowErrorAsync("Chyba", "Pro tento disk nebyl proveden žádný test. Proveďte nejprve test.");
                return;
            }

            _selectedSession = await _diskCardRepository.GetTestSessionAsync(latestSession.Id);
            if (_selectedSession == null)
            {
                StatusMessage = "Detail testu se nepodařilo načíst.";
                await _dialogService.ShowErrorAsync("Chyba", "Nepodařilo se načíst detail posledního testu.");
                return;
            }

            Certificate = await _certificateGenerator.GenerateCertificateAsync(_selectedSession, card);
            await PrepareCertificateGraphAsync(Certificate, updateView: true);
            await _diskCardRepository.CreateCertificateAsync(Certificate);

            StatusMessage = $"Certifikát vygenerován: {Certificate.CertificateNumber}";
        }
        catch (InvalidOperationException ex)
        {
            StatusMessage = $"Chyba: {ex.Message}";
            await _dialogService.ShowErrorAsync("Chyba", $"Nepodařilo se vygenerovat certifikát: {ex.Message}");
        }
        catch (DbUpdateException ex)
        {
            var message = ex.InnerException?.Message ?? ex.Message;
            StatusMessage = $"Chyba: {message}";
            await _dialogService.ShowErrorAsync("Chyba", $"Nepodařilo se uložit certifikát: {message}");
        }
        finally
        {
            IsLoading = false;
        }
    }

    [RelayCommand]
    private async Task ExportPdfAsync()
    {
        if (Certificate == null)
        {
            StatusMessage = "Nejprve vygenerujte certifikát";
            return;
        }

        try
        {
            IsLoading = true;
            StatusMessage = "Exportuji PDF...";

            await PrepareCertificateGraphAsync(Certificate, updateView: false);
            var pdfPath = await _certificateGenerator.GeneratePdfAsync(Certificate);

            StatusMessage = $"PDF uloženo: {pdfPath}";

            var openPdf = await _dialogService.ShowConfirmationAsync(
                "PDF Export",
                $"PDF certifikát uložen:\n{pdfPath}\n\nOtevřít soubor?");

            if (openPdf)
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = pdfPath,
                    UseShellExecute = true
                });
            }
        }
        catch (InvalidOperationException ex)
        {
            StatusMessage = $"Chyba: {ex.Message}";
            await _dialogService.ShowErrorAsync("Chyba", $"Nepodařilo se exportovat PDF: {ex.Message}");
        }
        finally
        {
            IsLoading = false;
        }
    }

    [RelayCommand]
    private async Task PrintAsync()
    {
        if (Certificate == null)
        {
            StatusMessage = "Nejprve vygenerujte certifikát";
            return;
        }

        try
        {
            await PrepareCertificateGraphAsync(Certificate, updateView: false);
            var pdfPath = await _certificateGenerator.GeneratePdfAsync(Certificate);

            Process.Start(new ProcessStartInfo
            {
                FileName = pdfPath,
                Verb = "print",
                UseShellExecute = true
            });

            StatusMessage = "Tisk spuštěn";
        }
        catch (InvalidOperationException ex)
        {
            StatusMessage = $"Chyba tisku: {ex.Message}";
            await _dialogService.ShowErrorAsync("Chyba", $"Nepodařilo se vytisknout: {ex.Message}");
        }
    }

    [RelayCommand]
    private async Task PrintLabelAsync()
    {
        if (Certificate == null)
        {
            StatusMessage = "Nejprve vygenerujte certifikát";
            return;
        }

        try
        {
            IsLoading = true;
            StatusMessage = "Generuji štítek...";

            await PrepareCertificateGraphAsync(Certificate, updateView: false);
            var labelPath = await _certificateGenerator.GenerateLabelAsync(Certificate);

            Process.Start(new ProcessStartInfo
            {
                FileName = labelPath,
                Verb = "print",
                UseShellExecute = true
            });

            StatusMessage = "Tisk štítku spuštěn";
        }
        catch (InvalidOperationException ex)
        {
            StatusMessage = $"Chyba tisku štítku: {ex.Message}";
            await _dialogService.ShowErrorAsync("Chyba", $"Nepodařilo se vytisknout štítek: {ex.Message}");
        }
        catch (IOException ex)
        {
            StatusMessage = $"Chyba tisku štítku: {ex.Message}";
            await _dialogService.ShowErrorAsync("Chyba", $"Nepodařilo se vytisknout štítek: {ex.Message}");
        }
        catch (Win32Exception ex)
        {
            StatusMessage = $"Chyba tisku štítku: {ex.Message}";
            await _dialogService.ShowErrorAsync("Chyba", $"Nepodařilo se vytisknout štítek: {ex.Message}");
        }
        finally
        {
            IsLoading = false;
        }
    }

    [RelayCommand]
    private void GoBack()
    {
        _navigationService.NavigateTo<DiskCardDetailViewModel>();
    }

    #endregion

    #region Private Methods

    private async Task LoadCertificateAsync()
    {
        try
        {
            IsLoading = true;
            StatusMessage = "Načítám certifikát...";
            _writeGraphSamples = [];
            _readGraphSamples = [];

            DiskCard? card = null;

            if (_selectedDiskService.SelectedDisk != null)
            {
                card = await _diskCardRepository.GetByDevicePathAsync(_selectedDiskService.SelectedDisk.Path);
            }

            if (card == null)
            {
                var allCards = await _diskCardRepository.GetAllAsync();
                card = allCards.OrderByDescending(c => c.LastTestedAt).FirstOrDefault();

                if (card != null)
                {
                    _selectedDiskService.SelectedDisk = new CoreDriveInfo
                    {
                        Path = card.DevicePath,
                        Name = card.ModelName,
                        TotalSize = card.Capacity,
                        SerialNumber = card.SerialNumber,
                        FirmwareVersion = card.FirmwareVersion
                    };
                    _selectedDiskService.SelectedDiskDisplayName = card.ModelName;
                    _selectedDiskService.IsSelectedDiskLocked = card.IsLocked;
                }
            }

            if (card == null)
            {
                StatusMessage = "Karta disku nenalezena.";
                return;
            }

            DiskCertificate? selectedCert = null;
            _selectedSession = null;

            if (_selectedDiskService.SelectedCertificateId.HasValue)
            {
                selectedCert = await _diskCardRepository.GetCertificateAsync(_selectedDiskService.SelectedCertificateId.Value);
            }

            if (selectedCert == null && _selectedDiskService.SelectedTestSessionId.HasValue)
            {
                _selectedSession = await _diskCardRepository.GetTestSessionAsync(_selectedDiskService.SelectedTestSessionId.Value);
                if (_selectedSession != null)
                {
                    selectedCert = await _certificateGenerator.GenerateCertificateAsync(_selectedSession, card);
                }
            }

            var latestCert = selectedCert ?? await _diskCardRepository.GetLatestCertificateAsync(card.Id);

            if (latestCert == null)
            {
                var sessions = await _diskCardRepository.GetTestSessionsAsync(card.Id);
                var latestSessionId = sessions.FirstOrDefault()?.Id;
                if (latestSessionId.HasValue)
                {
                    _selectedSession = await _diskCardRepository.GetTestSessionAsync(latestSessionId.Value);
                    if (_selectedSession != null)
                    {
                        latestCert = await _certificateGenerator.GenerateCertificateAsync(_selectedSession, card);
                    }
                }
            }

            if (latestCert != null)
            {
                await ApplySmartSummaryAsync(latestCert, card);
                Certificate = latestCert;
                await PrepareCertificateGraphAsync(latestCert, updateView: true);
                StatusMessage = $"Certifikát načten: {latestCert.CertificateNumber}";
            }
            else
            {
                ResetGraphToDefaults();
                StatusMessage = "Žádný certifikát nenalezen. Vygenerujte nový.";
            }
        }
        catch (InvalidOperationException ex)
        {
            StatusMessage = $"Chyba při načítání certifikátu: {ex.Message}";
            await _dialogService.ShowErrorAsync("Chyba", $"Nepodařilo se načíst certifikát: {ex.Message}");
        }
        catch (IOException ex)
        {
            StatusMessage = $"Chyba při načítání certifikátu: {ex.Message}";
            await _dialogService.ShowErrorAsync("Chyba", $"Nepodařilo se načíst certifikát: {ex.Message}");
        }
        catch (ExternalException ex)
        {
            StatusMessage = $"Chyba při načítání certifikátu: {ex.Message}";
            await _dialogService.ShowErrorAsync("Chyba", $"Nepodařilo se vykreslit certifikát: {ex.Message}");
        }
        finally
        {
            IsLoading = false;
        }
    }

    private async Task ApplySmartSummaryAsync(DiskCertificate certificate, DiskCard card)
    {
        ArgumentNullException.ThrowIfNull(certificate);
        ArgumentNullException.ThrowIfNull(card);

        if (certificate.PowerOnHours <= 0 && card.PowerOnHours.HasValue)
        {
            certificate.PowerOnHours = card.PowerOnHours.Value;
        }

        if (certificate.PowerCycles <= 0 && card.PowerCycleCount.HasValue)
        {
            certificate.PowerCycles = card.PowerCycleCount.Value;
        }

        if (certificate.ReallocatedSectors > 0 || certificate.PendingSectors > 0)
        {
            return;
        }

        var sessions = await _diskCardRepository.GetTestSessionsAsync(card.Id);
        var latestSmartSessionId = sessions
            .Where(s => s.TestType == DiskChecker.Core.Models.TestType.SmartShort || s.TestType == DiskChecker.Core.Models.TestType.SmartExtended || s.TestType == DiskChecker.Core.Models.TestType.SmartConveyance)
            .OrderByDescending(s => s.StartedAt)
            .Select(s => (int?)s.Id)
            .FirstOrDefault();

        if (!latestSmartSessionId.HasValue)
        {
            return;
        }

        var smartSession = await _diskCardRepository.GetTestSessionAsync(latestSmartSessionId.Value);
        if (smartSession == null)
        {
            return;
        }

        var reallocated = smartSession.SmartChanges.FirstOrDefault(c => c.AttributeId == 5);
        var pending = smartSession.SmartChanges.FirstOrDefault(c => c.AttributeId == 197);

        if (certificate.ReallocatedSectors <= 0 && reallocated != null)
        {
            certificate.ReallocatedSectors = reallocated.ValueAfter > int.MaxValue ? int.MaxValue : (int)reallocated.ValueAfter;
        }

        if (certificate.PendingSectors <= 0 && pending != null)
        {
            certificate.PendingSectors = pending.ValueAfter > int.MaxValue ? int.MaxValue : (int)pending.ValueAfter;
        }

        certificate.SmartPassed = certificate.ReallocatedSectors == 0 && certificate.PendingSectors == 0;
    }

    private async Task PrepareCertificateGraphAsync(DiskCertificate certificate, bool updateView)
    {
        ArgumentNullException.ThrowIfNull(certificate);

        await EnsureCertificateGraphDataAsync(certificate);

        if (updateView)
        {
            await RebuildPerformanceGraphAsync(certificate).ConfigureAwait(true);
        }
    }

    private async Task EnsureCertificateGraphDataAsync(DiskCertificate certificate)
    {
        ArgumentNullException.ThrowIfNull(certificate);

        var hasWriteProfile = certificate.WriteProfilePoints is { Count: > 0 };
        var hasReadProfile = certificate.ReadProfilePoints is { Count: > 0 };

        if (_selectedSession != null)
        {
            _writeGraphSamples = _selectedSession.WriteSamples
                .Where(s => s.SpeedMBps > 0)
                .OrderBy(s => s.ProgressPercent)
                .ToList();
            _readGraphSamples = _selectedSession.ReadSamples
                .Where(s => s.SpeedMBps > 0)
                .OrderBy(s => s.ProgressPercent)
                .ToList();

            if (!hasWriteProfile)
            {
                certificate.WriteProfilePoints = DownsampleSpeedSamples(_writeGraphSamples.Select(s => s.SpeedMBps), 32);
            }

            if (!hasReadProfile)
            {
                certificate.ReadProfilePoints = DownsampleSpeedSamples(_readGraphSamples.Select(s => s.SpeedMBps), 32);
            }

            return;
        }

        var sessionId = certificate.TestSessionId;
        if (sessionId <= 0 && _selectedDiskService.SelectedTestSessionId.HasValue)
        {
            sessionId = _selectedDiskService.SelectedTestSessionId.Value;
        }

        if (sessionId <= 0)
        {
            return;
        }

        var speedSeries = await _diskCardRepository.GetSpeedSampleSeriesAsync(sessionId);
        _writeGraphSamples = speedSeries.WriteSamples;
        _readGraphSamples = speedSeries.ReadSamples;

        if (_writeGraphSamples.Count == 0 && _readGraphSamples.Count == 0)
        {
            return;
        }

        if (!hasWriteProfile)
        {
            certificate.WriteProfilePoints = DownsampleSpeedSamples(_writeGraphSamples.Select(s => s.SpeedMBps), 32);
        }

        if (!hasReadProfile)
        {
            certificate.ReadProfilePoints = DownsampleSpeedSamples(_readGraphSamples.Select(s => s.SpeedMBps), 32);
        }
    }

    private async Task RebuildPerformanceGraphAsync(DiskCertificate certificate)
    {
        ArgumentNullException.ThrowIfNull(certificate);

        var writeGraphSamples = _writeGraphSamples;
        var readGraphSamples = _readGraphSamples;
        var graphData = await Task.Run(() => BuildGraphData(certificate, writeGraphSamples, readGraphSamples)).ConfigureAwait(true);

        WriteProfilePoints = graphData.WriteProfilePoints;
        ReadProfilePoints = graphData.ReadProfilePoints;
        ChartMaxSpeedLabel = graphData.ChartMaxSpeedLabel;
        ChartMidSpeedLabel = graphData.ChartMidSpeedLabel;
        ChartMinSpeedLabel = graphData.ChartMinSpeedLabel;
        ChartXAxisStartLabel = graphData.ChartXAxisStartLabel;
        ChartXAxisMidLabel = graphData.ChartXAxisMidLabel;
        ChartXAxisEndLabel = graphData.ChartXAxisEndLabel;
    }

    private void RebuildPerformanceGraph()
    {
        if (Certificate == null)
        {
            ResetGraphToDefaults();
            return;
        }

        var graphData = BuildGraphData(Certificate, _writeGraphSamples, _readGraphSamples);
        WriteProfilePoints = graphData.WriteProfilePoints;
        ReadProfilePoints = graphData.ReadProfilePoints;
        ChartMaxSpeedLabel = graphData.ChartMaxSpeedLabel;
        ChartMidSpeedLabel = graphData.ChartMidSpeedLabel;
        ChartMinSpeedLabel = graphData.ChartMinSpeedLabel;
        ChartXAxisStartLabel = graphData.ChartXAxisStartLabel;
        ChartXAxisMidLabel = graphData.ChartXAxisMidLabel;
        ChartXAxisEndLabel = graphData.ChartXAxisEndLabel;
    }

    private static CertificateGraphData BuildGraphData(
        DiskCertificate certificate,
        IReadOnlyList<SpeedSample> writeGraphSamples,
        IReadOnlyList<SpeedSample> readGraphSamples)
    {
        ArgumentNullException.ThrowIfNull(certificate);

        var writePoints = GetGraphSamples(writeGraphSamples, certificate.AvgWriteSpeed, certificate.MaxWriteSpeed);
        var readPoints = GetGraphSamples(readGraphSamples, certificate.AvgReadSpeed, certificate.MaxReadSpeed);
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

        return new CertificateGraphData(
            BuildPolylinePoints(writePoints, minSpeed, maxSpeed),
            BuildPolylinePoints(readPoints, minSpeed, maxSpeed),
            $"{maxSpeed:F1} MB/s",
            $"{((maxSpeed + minSpeed) / 2):F1} MB/s",
            $"{minSpeed:F1} MB/s",
            "0 %",
            "50 %",
            "100 %");
    }

    private static List<SpeedSample> GetGraphSamples(IReadOnlyList<SpeedSample> samples, double averageSpeed, double maxSpeed)
    {
        var values = samples
            .Where(s => s.SpeedMBps > 0)
            .OrderBy(s => s.ProgressPercent)
            .ToList();
        if (values.Count > 0)
        {
            return DownsampleGraphSamples(values, 32);
        }

        if (averageSpeed <= 0 && maxSpeed <= 0)
        {
            return [];
        }

        var peakSpeed = maxSpeed > 0 ? maxSpeed : averageSpeed;
        var baseSpeed = averageSpeed > 0 ? averageSpeed : peakSpeed;

        return
        [
            new SpeedSample { ProgressPercent = 0, SpeedMBps = baseSpeed },
            new SpeedSample { ProgressPercent = 50, SpeedMBps = peakSpeed },
            new SpeedSample { ProgressPercent = 100, SpeedMBps = baseSpeed }
        ];
    }

    private static string BuildPolylinePoints(IEnumerable<SpeedSample> samples, double minSpeed, double maxSpeed)
    {
        var values = samples.Where(v => v.SpeedMBps > 0).OrderBy(v => v.ProgressPercent).ToList();
        if (values.Count == 0)
        {
            return "10,102 70,102 130,102 190,102 250,102 310,102 370,102 430,102 490,102";
        }

        var startX = 10d;
        var endX = 490d;
        var minY = 18d;
        var maxY = 102d;
        var range = Math.Max(0.0001d, maxSpeed - minSpeed);
        var points = new List<string>(values.Count);

        foreach (var sample in values)
        {
            var x = startX + (Math.Clamp(sample.ProgressPercent, 0d, 100d) / 100d) * (endX - startX);
            var ratio = Math.Clamp((sample.SpeedMBps - minSpeed) / range, 0d, 1d);
            var y = maxY - ((maxY - minY) * ratio);
            points.Add(FormattableString.Invariant($"{x:0},{y:0}"));
        }

        return string.Join(" ", points);
    }

    private static List<SpeedSample> DownsampleGraphSamples(IReadOnlyList<SpeedSample> values, int targetPoints)
    {
        if (values.Count <= targetPoints)
        {
            return values.ToList();
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

            if (count > 0)
            {
                result.Add(new SpeedSample
                {
                    ProgressPercent = sumProgress / count,
                    SpeedMBps = sumSpeed / count
                });
            }
        }

        return result;
    }

    private static List<double> Downsample(IReadOnlyList<double> values, int targetPoints)
    {
        if (values.Count <= targetPoints)
        {
            return values.ToList();
        }

        var result = new List<double>(targetPoints);
        var bucketSize = values.Count / (double)targetPoints;

        for (var i = 0; i < targetPoints; i++)
        {
            var start = (int)Math.Floor(i * bucketSize);
            var end = (int)Math.Floor((i + 1) * bucketSize);
            end = Math.Clamp(end, start + 1, values.Count);

            var avg = 0d;
            var count = 0;
            for (var j = start; j < end; j++)
            {
                avg += values[j];
                count++;
            }

            result.Add(count > 0 ? avg / count : values[Math.Min(start, values.Count - 1)]);
        }

        return result;
    }

    private static List<double> DownsampleSpeedSamples(IEnumerable<double> speeds, int targetPoints)
    {
        ArgumentNullException.ThrowIfNull(speeds);

        var values = speeds.Where(v => v > 0).ToList();
        if (values.Count == 0)
        {
            return [];
        }

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

            var sum = 0d;
            var count = 0;
            for (var j = start; j < end; j++)
            {
                sum += values[j];
                count++;
            }

            result.Add(count > 0 ? sum / count : values[Math.Min(start, values.Count - 1)]);
        }

        return result;
    }

    private void ResetGraphToDefaults()
    {
        _writeGraphSamples = [];
        _readGraphSamples = [];
        var graphData = CertificateGraphData.Default;
        WriteProfilePoints = graphData.WriteProfilePoints;
        ReadProfilePoints = graphData.ReadProfilePoints;
        ChartMaxSpeedLabel = graphData.ChartMaxSpeedLabel;
        ChartMidSpeedLabel = graphData.ChartMidSpeedLabel;
        ChartMinSpeedLabel = graphData.ChartMinSpeedLabel;
        ChartXAxisStartLabel = graphData.ChartXAxisStartLabel;
        ChartXAxisMidLabel = graphData.ChartXAxisMidLabel;
        ChartXAxisEndLabel = graphData.ChartXAxisEndLabel;
    }

    private sealed record CertificateGraphData(
        string WriteProfilePoints,
        string ReadProfilePoints,
        string ChartMaxSpeedLabel,
        string ChartMidSpeedLabel,
        string ChartMinSpeedLabel,
        string ChartXAxisStartLabel,
        string ChartXAxisMidLabel,
        string ChartXAxisEndLabel)
    {
        public static CertificateGraphData Default { get; } = new(
            "10,90 70,85 130,80 190,75 250,70 310,65 370,60 430,58 490,55",
            "10,95 70,90 130,86 190,82 250,79 310,76 370,73 430,70 490,68",
            "1 MB/s",
            "0.5 MB/s",
            "0 MB/s",
            "0 %",
            "50 %",
            "100 %");
    }

    #endregion
}