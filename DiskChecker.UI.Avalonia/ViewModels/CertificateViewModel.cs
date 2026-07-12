using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
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
using DiskChecker.UI.Avalonia.Services;
using DiskChecker.UI.Avalonia.Services.Interfaces;
using LiveChartsCore.Defaults;
using Microsoft.EntityFrameworkCore;
using System.Data.Common;
using System.Text.Json;

namespace DiskChecker.UI.Avalonia.ViewModels;

/// <summary>
/// View model for displaying and generating disk certificates.
/// </summary>
public partial class CertificateViewModel : ViewModelBase, INavigableViewModel
{
   private const int CertificateGraphModulo = 96;
   private const int CertificateGraphRemainders = 4;

   private readonly ICertificateGenerator _certificateGenerator;
   private readonly IDiskCardRepository _diskCardRepository;
   private readonly INavigationService _navigationService;
   private readonly IDialogService _dialogService;
   private readonly ISelectedDiskService _selectedDiskService;

   private DiskCertificate? _certificate;
   private bool _isLoading;
   private bool _isPrinting;
   private string _statusMessage = string.Empty;
   private string _printProgressMessage = string.Empty;
   private bool _isBlackAndWhiteMode;
   private TestSession? _selectedSession;
   private List<SpeedSample> _writeGraphSamples = [];
   private List<SpeedSample> _readGraphSamples = [];
   private List<double> _seekLatencyGraphSamples = [];
   private ObservableCollection<ObservablePoint> _seekScatterPoints = new();
   private string _writeProfilePoints = "10,62 70,58 130,56 190,54 250,50 310,48 370,45 430,42 490,40";
   private string _readProfilePoints = "10,66 70,61 130,58 190,55 250,53 310,50 370,47 430,45 490,43";
   private string _chartMaxSpeedLabel = "0 MB/s";
   private string _chartMidSpeedLabel = "0 MB/s";
   private string _chartMinSpeedLabel = "0 MB/s";
   private string _chartXAxisStartLabel = "0 %";
   private string _chartXAxisMidLabel = "50 %";
   private string _chartXAxisEndLabel = "100 %";
   private string _temperatureProfilePoints = "10,110 490,110";
   private bool _hasTemperatureProfile;
   private string _selectedSessionSummary = string.Empty;
   private string _selectedSessionThermalSummary = string.Empty;
   private string _selectedSessionSmartSummary = string.Empty;
   private TestSession? _selectedSessionItem;

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
      StatusMessage = L.Get("CertificateView.Status.Title");
      SelectedSessionSummary = L.Get("CertificateView.Session.SelectForDetail");
      SelectedSessionThermalSummary = L.Get("CertificateView.Session.ThermalAvailableAfterSelection");
      SelectedSessionSmartSummary = L.Get("CertificateView.Session.SmartAvailableAfterSelection");
      AvailableSessions = new ObservableCollection<TestSession>();
   }

   #region Properties

   public DiskCertificate? Certificate
   {
      get => _certificate;
      private set
      {
         if(SetProperty(ref _certificate, value))
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
            OnPropertyChanged(nameof(Errors));
            OnPropertyChanged(nameof(SmartPassed));
            OnPropertyChanged(nameof(Recommended));
            OnPropertyChanged(nameof(RecommendationText));
            OnPropertyChanged(nameof(ScoringReasonsText));
            OnPropertyChanged(nameof(DiagnosticHighlightsText));
            OnPropertyChanged(nameof(HasDiagnosticHighlights));
            OnPropertyChanged(nameof(HasScoringReasons));
            OnPropertyChanged(nameof(DiagnosticHasCriticalSignals));
            OnPropertyChanged(nameof(DiagnosticHasWarningSignals));
            OnPropertyChanged(nameof(DiagnosticBadgeText));
            OnPropertyChanged(nameof(DiagnosticBadgeBackground));
            OnPropertyChanged(nameof(DiagnosticBadgeForeground));
            OnPropertyChanged(nameof(SmartPowerOnHoursText));
            OnPropertyChanged(nameof(SmartPowerCyclesText));
            OnPropertyChanged(nameof(SmartReallocatedSectorsText));
            OnPropertyChanged(nameof(SmartPendingSectorsText));
            OnPropertyChanged(nameof(SanitizationPerformed));
            OnPropertyChanged(nameof(SanitizationMethodText));
            OnPropertyChanged(nameof(DataVerifiedText));
            OnPropertyChanged(nameof(PartitionSchemeText));
            OnPropertyChanged(nameof(FileSystemText));
            OnPropertyChanged(nameof(VolumeLabelText));
            OnPropertyChanged(nameof(TestTypeLine));
            OnPropertyChanged(nameof(TestDurationLine));
            OnPropertyChanged(nameof(ErrorsLine));
            OnPropertyChanged(nameof(TemperatureRangeLine));
            OnPropertyChanged(nameof(AvgWriteSpeedLine));
            OnPropertyChanged(nameof(AvgReadSpeedLine));
            OnPropertyChanged(nameof(HealthStatusLine));
            OnPropertyChanged(nameof(RecommendedLine));
            OnPropertyChanged(nameof(SmartPowerOnHoursLine));
            OnPropertyChanged(nameof(SmartPowerCyclesLine));
            OnPropertyChanged(nameof(SmartReallocatedSectorsLine));
            OnPropertyChanged(nameof(SmartPendingSectorsLine));
            OnPropertyChanged(nameof(SeekSummaryLine));
            OnPropertyChanged(nameof(Sanitize1WriteLine));
            OnPropertyChanged(nameof(Sanitize2WriteLine));
            OnPropertyChanged(nameof(Sanitize1ReadLine));
            OnPropertyChanged(nameof(Sanitize2ReadLine));
            OnPropertyChanged(nameof(WriteSpeedChangeLine));
            OnPropertyChanged(nameof(ReadSpeedChangeLine));
            OnPropertyChanged(nameof(IsSeekChart));
            OnPropertyChanged(nameof(IsThroughputChart));
         }
      }
   }

   public bool IsLoading
   {
      get => _isLoading;
      set => SetProperty(ref _isLoading, value);
   }

   /// <summary>
   /// Gets whether a print operation is currently in progress.
   /// </summary>
   public bool IsPrinting
   {
      get => _isPrinting;
      private set => SetProperty(ref _isPrinting, value);
   }

   /// <summary>
   /// Gets the current step description during a print or export operation.
   /// </summary>
   public string PrintProgressMessage
   {
      get => _printProgressMessage;
      private set => SetProperty(ref _printProgressMessage, value);
   }

   /// <summary>
   /// Gets or sets whether certificate preview uses black-and-white print-safe mode.
   /// </summary>
   public bool IsBlackAndWhiteMode
   {
      get => _isBlackAndWhiteMode;
      set
      {
         if(SetProperty(ref _isBlackAndWhiteMode, value))
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

   public ObservableCollection<ObservablePoint> SeekScatterPoints
   {
      get => _seekScatterPoints;
      private set
      {
         if(SetProperty(ref _seekScatterPoints, value))
         {
            OnPropertyChanged(nameof(IsSeekChart));
            OnPropertyChanged(nameof(IsThroughputChart));
         }
      }
   }

   public bool IsSeekChart => HasSeekMetrics && SeekScatterPoints.Count > 0;

   public bool IsThroughputChart => !IsSeekChart;

   /// <summary>
   /// Gets temperature profile polyline points for the certificate chart.
   /// </summary>
   public string TemperatureProfilePoints
   {
      get => _temperatureProfilePoints;
      private set => SetProperty(ref _temperatureProfilePoints, value);
   }

   /// <summary>
   /// Gets whether temperature profile is available for the selected session.
   /// </summary>
   public bool HasTemperatureProfile
   {
      get => _hasTemperatureProfile;
      private set => SetProperty(ref _hasTemperatureProfile, value);
   }

   /// <summary>
   /// Gets available test sessions for in-app certificate analysis.
   /// </summary>
   public ObservableCollection<TestSession> AvailableSessions { get; }

   /// <summary>
   /// Gets or sets currently selected session for detailed in-app certificate analysis.
   /// </summary>
   public TestSession? SelectedSessionItem
   {
      get => _selectedSessionItem;
      set
      {
         if(SetProperty(ref _selectedSessionItem, value))
         {
            _ = SelectSessionAsync(value);
         }
      }
   }

   /// <summary>
   /// Gets selected session metrics summary.
   /// </summary>
   public string SelectedSessionSummary
   {
      get => _selectedSessionSummary;
      private set => SetProperty(ref _selectedSessionSummary, value);
   }

   /// <summary>
   /// Gets selected session thermal behavior summary.
   /// </summary>
   public string SelectedSessionThermalSummary
   {
      get => _selectedSessionThermalSummary;
      private set => SetProperty(ref _selectedSessionThermalSummary, value);
   }

   /// <summary>
   /// Gets selected session SMART summary.
   /// </summary>
   public string SelectedSessionSmartSummary
   {
      get => _selectedSessionSmartSummary;
      private set => SetProperty(ref _selectedSessionSmartSummary, value);
   }

   // Certificate display properties
   public string CertificateNumber => Certificate?.CertificateNumber ?? "-";

   public string DiskModel => Certificate?.DiskModel ?? L.Get("CertificateView.Unknown");
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
   public string RecommendationText => Certificate?.RecommendationNotes ?? L.Get("CertificateView.NotAvailable");

   public string TestTypeLine => string.Format(L.Get("CertificateView.TestTypeFormat"), TestType);
   public string TestDurationLine => string.Format(L.Get("CertificateView.TestDurationFormat"), TestDuration);
   public string ErrorsLine => string.Format(L.Get("CertificateView.ErrorsFormat"), Errors);
   public string TemperatureRangeLine => string.Format(L.Get("CertificateView.TemperatureFormat"), TemperatureRange);
   public string AvgWriteSpeedLine => string.Format(L.Get("CertificateView.AvgWriteSpeedFormat"), AvgWriteSpeed);
   public string AvgReadSpeedLine => string.Format(L.Get("CertificateView.AvgReadSpeedFormat"), AvgReadSpeed);
   public string HealthStatusLine => string.Format(L.Get("CertificateView.HealthStatusFormat"), HealthStatus);
   public string RecommendedLine => string.Format(L.Get("CertificateView.RecommendedFormat"), Recommended);
   public string SmartPowerOnHoursLine => string.Format(L.Get("CertificateView.PowerOnHoursFormat"), SmartPowerOnHoursText);
   public string SmartPowerCyclesLine => string.Format(L.Get("CertificateView.PowerCyclesFormat"), SmartPowerCyclesText);
   public string SmartReallocatedSectorsLine => string.Format(L.Get("CertificateView.ReallocatedSectorsFormat"), SmartReallocatedSectorsText);
   public string SmartPendingSectorsLine => string.Format(L.Get("CertificateView.PendingSectorsFormat"), SmartPendingSectorsText);
   public string SeekSummaryLine => string.Format(L.Get("CertificateView.SeekSummaryFormat"), SeekTestSummaryText);
   public string Sanitize1WriteLine => string.Format(L.Get("CertificateView.SanitizePass1Format"), Sanitize1WriteText);
   public string Sanitize2WriteLine => string.Format(L.Get("CertificateView.SanitizePass2Format"), Sanitize2WriteText);
   public string Sanitize1ReadLine => string.Format(L.Get("CertificateView.SanitizePass1Format"), Sanitize1ReadText);
   public string Sanitize2ReadLine => string.Format(L.Get("CertificateView.SanitizePass2Format"), Sanitize2ReadText);
   public string WriteSpeedChangeLine => string.Format(L.Get("CertificateView.ChangeFormat"), WriteSpeedChangeText);
   public string ReadSpeedChangeLine => string.Format(L.Get("CertificateView.ChangeFormat"), ReadSpeedChangeText);

   // ── Sanitization details ──

   public bool SanitizationPerformed => Certificate?.SanitizationPerformed == true;

   public string SanitizationMethodText => string.IsNullOrWhiteSpace(Certificate?.SanitizationMethod)
       ? "—"
       : Certificate.SanitizationMethod;

   public string DataVerifiedText => Certificate?.DataVerified == true
       ? L.Get("CertificateView.Yes")
       : L.Get("CertificateView.No");

   public string PartitionSchemeText => string.IsNullOrWhiteSpace(Certificate?.PartitionScheme)
       ? "—"
       : Certificate.PartitionScheme;

   public string FileSystemText => string.IsNullOrWhiteSpace(Certificate?.FileSystem)
       ? "—"
       : Certificate.FileSystem;

   public string VolumeLabelText => string.IsNullOrWhiteSpace(Certificate?.VolumeLabel)
       ? "—"
       : Certificate.VolumeLabel;

   // ── Seek test metrics (Absolute Destructive Test) ──

   public bool HasSeekMetrics => Certificate?.SeekAvgLatencyMs.HasValue == true;

   public string SeekAvgLatencyMsText => Certificate?.SeekAvgLatencyMs.HasValue == true
       ? $"{Certificate.SeekAvgLatencyMs.Value:F2} ms" : "—";

   public string SeekMinLatencyMsText => Certificate?.SeekMinLatencyMs.HasValue == true
       ? $"{Certificate.SeekMinLatencyMs.Value:F2} ms" : "—";

   public string SeekMaxLatencyMsText => Certificate?.SeekMaxLatencyMs.HasValue == true
       ? $"{Certificate.SeekMaxLatencyMs.Value:F2} ms" : "—";

   public string SeekP95LatencyMsText => Certificate?.SeekP95LatencyMs.HasValue == true
       ? $"{Certificate.SeekP95LatencyMs.Value:F2} ms" : "—";

   public string SeekTestSummaryText => Certificate?.SeekTestSummary ?? "—";

   // ── Before/After sanitization comparison ──

   public bool HasBeforeAfterComparison => Certificate?.Sanitize1AvgWriteMBps.HasValue == true;

   public string Sanitize1WriteText => Certificate?.Sanitize1AvgWriteMBps.HasValue == true
       ? $"{Certificate.Sanitize1AvgWriteMBps.Value:F1} MB/s" : "—";

   public string Sanitize2WriteText => Certificate?.Sanitize2AvgWriteMBps.HasValue == true
       ? $"{Certificate.Sanitize2AvgWriteMBps.Value:F1} MB/s" : "—";

   public string WriteSpeedChangeText => Certificate?.WriteSpeedChangePercent.HasValue == true
       ? $"{Certificate.WriteSpeedChangePercent.Value:+0.0;-0.0}%" : "—";

   public string Sanitize1ReadText => Certificate?.Sanitize1AvgReadMBps.HasValue == true
       ? $"{Certificate.Sanitize1AvgReadMBps.Value:F1} MB/s" : "—";

   public string Sanitize2ReadText => Certificate?.Sanitize2AvgReadMBps.HasValue == true
       ? $"{Certificate.Sanitize2AvgReadMBps.Value:F1} MB/s" : "—";

   public string ReadSpeedChangeText => Certificate?.ReadSpeedChangePercent.HasValue == true
       ? $"{Certificate.ReadSpeedChangePercent.Value:+0.0;-0.0}%" : "—";

   public string SmartDeltaSummaryText => Certificate?.SmartDeltaSummary ?? "—";

   public string ScoringReasonsText => string.IsNullOrWhiteSpace(Certificate?.Notes)
       ? L.Get("CertificateView.NoWarnings")
       : Certificate.Notes;

   /// <summary>
   /// Gets formatted diagnostic highlights derived from certificate notes.
   /// </summary>
   public string DiagnosticHighlightsText => BuildDiagnosticHighlightsText(Certificate?.Notes);

   public bool DiagnosticHasCriticalSignals => ContainsDiagnosticMarker(Certificate?.Notes, "kritické") || ContainsDiagnosticMarker(Certificate?.Notes, "kritický");
   public bool DiagnosticHasWarningSignals =>
       ContainsDiagnosticMarker(Certificate?.Notes, "propad") ||
       ContainsDiagnosticMarker(Certificate?.Notes, "nestabil") ||
       ContainsDiagnosticMarker(Certificate?.Notes, "histor");

   public string DiagnosticBadgeText => DiagnosticHasCriticalSignals
       ? L.Get("CertificateView.Badge.Critical")
       : DiagnosticHasWarningSignals
           ? L.Get("CertificateView.Badge.Warning")
           : L.Get("CertificateView.Badge.Stable");

   public string DiagnosticBadgeBackground => DiagnosticHasCriticalSignals
       ? "#FDECEC"
       : DiagnosticHasWarningSignals
           ? "#FFF4DB"
           : "#EAF7EE";

   public string DiagnosticBadgeForeground => DiagnosticHasCriticalSignals
       ? "#B42318"
       : DiagnosticHasWarningSignals
           ? "#B54708"
           : "#027A48";

   /// <summary>
   /// Gets whether diagnostic highlights are available.
   /// </summary>
   public bool HasDiagnosticHighlights => !string.IsNullOrWhiteSpace(DiagnosticHighlightsText);

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

   #endregion Properties

   #region Navigation

   public void OnNavigatedTo()
   {
      _ = LoadCertificateAsync();
   }

   #endregion Navigation

   #region Commands

   [RelayCommand]
   private async Task GenerateNewCertificateAsync()
   {
      if(_selectedDiskService.SelectedDisk == null)
      {
         StatusMessage = L.Get("CertificateView.Status.NoDisk");
         return;
      }

      try
      {
         IsLoading = true;
         StatusMessage = L.Get("CertificateView.Status.Generating");

         // Prefer finding the card by session ID (reliable) over device path (can be reused)
         DiskCard? card = null;
         TestSession? targetSession = null;

         if (_selectedDiskService.SelectedTestSessionId.HasValue)
         {
            targetSession = await _diskCardRepository.GetTestSessionWithoutSamplesAsync(
                _selectedDiskService.SelectedTestSessionId.Value);
            if (targetSession != null)
            {
               card = await _diskCardRepository.GetByIdAsync(targetSession.DiskCardId);
            }
         }

         // Fallback: use device path (less reliable but works for fresh tests)
         if (card == null)
         {
            card = await _diskCardRepository.GetByDevicePathAsync(_selectedDiskService.SelectedDisk.Path);
         }

         if(card == null)
         {
            StatusMessage = L.Get("CertificateView.Status.CardNotFound");
            await _dialogService.ShowErrorAsync(L.Get("Common.Error"), L.Get("CertificateView.Error.CardNotFound"));
            return;
         }

         // Use the session we already found, or get the latest
         if (targetSession == null)
         {
            var sessions = await _diskCardRepository.GetTestSessionsAsync(card.Id);
            targetSession = sessions.FirstOrDefault();
         }

         if(targetSession == null)
         {
            StatusMessage = L.Get("CertificateView.Status.NoTest");
            await _dialogService.ShowErrorAsync(L.Get("Common.Error"), L.Get("CertificateView.Error.NoTest"));
            return;
         }

         // Load session WITHOUT samples to avoid OOM on large datasets (e.g. sanitization tests with millions of samples)
         _selectedSession = await _diskCardRepository.GetTestSessionWithoutSamplesAsync(targetSession.Id);
         if(_selectedSession == null)
         {
            StatusMessage = L.Get("CertificateView.Status.TestDetailError");
            await _dialogService.ShowErrorAsync(L.Get("Common.Error"), L.Get("CertificateView.Error.TestDetail"));
            return;
         }

         // Load speed samples via chunked loading + downsampling to prevent memory exhaustion
         StatusMessage = L.Get("CertificateView.Status.LoadingGraphData");
         var speedSeries = await LoadCertificateGraphSamplesProgressiveAsync(targetSession.Id);
         _selectedSession.WriteSamples = speedSeries.WriteSamples;
         _selectedSession.ReadSamples = speedSeries.ReadSamples;

         // Load temperature samples via SQL downsampling (max 256 points in memory)
         try
         {
            _selectedSession.TemperatureSamples = await _diskCardRepository.GetTemperatureSampleSeriesDownsampledAsync(targetSession.Id, 256);
         }
         catch
         {
            _selectedSession.TemperatureSamples = new List<TemperatureSample>();
         }

         Certificate = await _certificateGenerator.GenerateCertificateAsync(_selectedSession, card);
         await PrepareCertificateGraphAsync(Certificate, updateView: true);
         await _diskCardRepository.CreateCertificateAsync(Certificate);

         StatusMessage = string.Format(L.Get("CertificateView.Status.Generated"), Certificate.CertificateNumber);
      }
      catch(InvalidOperationException ex)
      {
         StatusMessage = string.Format(L.Get("Common.Error"), ex.Message);
         await _dialogService.ShowErrorAsync(L.Get("Common.Error"), string.Format(L.Get("CertificateView.Error.GenerateFailed"), ex.Message));
      }
      catch(DbUpdateException ex)
      {
         var message = ex.InnerException?.Message ?? ex.Message;
         StatusMessage = string.Format(L.Get("CertificateView.Error.Generic"), message);
         await _dialogService.ShowErrorAsync(L.Get("Common.Error"), string.Format(L.Get("CertificateView.Error.SaveFailed"), message));
      }
      finally
      {
         IsLoading = false;
      }
   }

   [RelayCommand]
   private async Task ExportPdfAsync()
   {
      if(Certificate == null)
      {
         StatusMessage = L.Get("CertificateView.Status.GenerateFirst");
         return;
      }

      try
      {
         IsLoading = true;
         StatusMessage = L.Get("CertificateView.Status.ExportingPdf");

         await PrepareCertificateGraphAsync(Certificate, updateView: false);
         await HydrateCertificateForOutputAsync(Certificate);
         var pdfPath = await _certificateGenerator.GeneratePdfAsync(Certificate);

         StatusMessage = string.Format(L.Get("CertificateView.Status.PdfSaved"), pdfPath);

         var openPdf = await _dialogService.ShowConfirmationAsync(
             L.Get("CertificateView.Dialog.PdfExport"),
             string.Format(L.Get("CertificateView.Dialog.PdfSavedMessage"), pdfPath));

         if(openPdf)
         {
            DocumentLauncher.OpenFile(pdfPath);
         }
      }
      catch(InvalidOperationException ex)
      {
         StatusMessage = string.Format(L.Get("Common.Error"), ex.Message);
         await _dialogService.ShowErrorAsync(L.Get("Common.Error"), string.Format(L.Get("CertificateView.Error.ExportPdf"), ex.Message));
      }
      catch(ArgumentException ex)
      {
         StatusMessage = string.Format(L.Get("Common.Error"), ex.Message);
         await _dialogService.ShowErrorAsync(L.Get("Common.Error"), string.Format(L.Get("CertificateView.Error.ExportPdf"), ex.Message));
      }
      catch(FileNotFoundException ex)
      {
         StatusMessage = string.Format(L.Get("Common.Error"), ex.Message);
         await _dialogService.ShowErrorAsync(L.Get("Common.Error"), string.Format(L.Get("CertificateView.Error.ExportPdf"), ex.Message));
      }
      catch(IOException ex)
      {
         StatusMessage = string.Format(L.Get("Common.Error"), ex.Message);
         await _dialogService.ShowErrorAsync(L.Get("Common.Error"), string.Format(L.Get("CertificateView.Error.ExportPdf"), ex.Message));
      }
      finally
      {
         IsLoading = false;
      }
   }

   [RelayCommand]
   private async Task PrintAsync()
   {
      if(Certificate == null)
      {
         StatusMessage = L.Get("CertificateView.Status.GenerateFirst");
         return;
      }

      try
      {
         IsLoading = true;
         IsPrinting = true;
         PrintProgressMessage = L.Get("CertificateView.Print.PreparingGraph");
         StatusMessage = L.Get("CertificateView.Status.PreparingPrint");

         await PrepareCertificateGraphAsync(Certificate, updateView: false);

         await HydrateCertificateForOutputAsync(Certificate);

         PrintProgressMessage = L.Get("CertificateView.Print.GeneratingPdf");
         StatusMessage = L.Get("CertificateView.Status.GeneratingPdf");

         var pdfPath = await _certificateGenerator.GeneratePdfAsync(Certificate);

         PrintProgressMessage = L.Get("CertificateView.Print.OpeningPdf");
         StatusMessage = L.Get("CertificateView.Status.OpeningPdf");

         await Task.Run(() => DocumentLauncher.OpenFile(pdfPath));

         StatusMessage = L.Get("CertificateView.Status.PdfOpened");
         PrintProgressMessage = string.Empty;

         await _dialogService.ShowInfoAsync(
             L.Get("CertificateView.Dialog.PrintCertificate"),
             string.Format(L.Get("CertificateView.Dialog.PrintMessage"), pdfPath));
      }
      catch(InvalidOperationException ex)
      {
         StatusMessage = string.Format(L.Get("CertificateView.Error.Print"), ex.Message);
         PrintProgressMessage = string.Empty;
         await _dialogService.ShowErrorAsync(L.Get("Common.Error"), string.Format(L.Get("CertificateView.Error.PrintPrepare"), ex.Message));
      }
      catch(ArgumentException ex)
      {
         StatusMessage = string.Format(L.Get("CertificateView.Error.Print"), ex.Message);
         PrintProgressMessage = string.Empty;
         await _dialogService.ShowErrorAsync(L.Get("Common.Error"), string.Format(L.Get("CertificateView.Error.PrintPrepare"), ex.Message));
      }
      catch(FileNotFoundException ex)
      {
         StatusMessage = string.Format(L.Get("CertificateView.Error.Print"), ex.Message);
         PrintProgressMessage = string.Empty;
         await _dialogService.ShowErrorAsync(L.Get("Common.Error"), string.Format(L.Get("CertificateView.Error.OpenPdf"), ex.Message));
      }
      catch(IOException ex)
      {
         StatusMessage = string.Format(L.Get("CertificateView.Error.Print"), ex.Message);
         PrintProgressMessage = string.Empty;
         await _dialogService.ShowErrorAsync(L.Get("Common.Error"), string.Format(L.Get("CertificateView.Error.PrintPrepare"), ex.Message));
      }
      catch(Win32Exception ex)
      {
         StatusMessage = string.Format(L.Get("CertificateView.Error.Print"), ex.Message);
         PrintProgressMessage = string.Empty;
         await _dialogService.ShowErrorAsync(L.Get("Common.Error"), string.Format(L.Get("CertificateView.Error.OpenPdf"), ex.Message));
      }
      finally
      {
         IsLoading = false;
         IsPrinting = false;
         PrintProgressMessage = string.Empty;
      }
   }

   [RelayCommand]
   private async Task PrintLabelAsync()
   {
      if(Certificate == null)
      {
         StatusMessage = L.Get("CertificateView.Status.GenerateFirst");
         return;
      }

      try
      {
         IsLoading = true;
         StatusMessage = L.Get("CertificateView.Status.GeneratingLabel");

         await PrepareCertificateGraphAsync(Certificate, updateView: false);
         var labelPath = await _certificateGenerator.GenerateLabelAsync(Certificate);

         await Task.Run(() => DocumentLauncher.OpenFile(labelPath));

         StatusMessage = L.Get("CertificateView.Status.LabelOpened");
         await _dialogService.ShowInfoAsync(
             L.Get("CertificateView.Dialog.PrintLabel"),
             string.Format(L.Get("CertificateView.Dialog.LabelMessage"), labelPath));
      }
      catch(InvalidOperationException ex)
      {
         StatusMessage = string.Format(L.Get("CertificateView.Error.LabelPrint"), ex.Message);
         await _dialogService.ShowErrorAsync(L.Get("Common.Error"), string.Format(L.Get("CertificateView.Error.PrintLabelPrepare"), ex.Message));
      }
      catch(ArgumentException ex)
      {
         StatusMessage = string.Format(L.Get("CertificateView.Error.LabelPrint"), ex.Message);
         await _dialogService.ShowErrorAsync(L.Get("Common.Error"), string.Format(L.Get("CertificateView.Error.PrintLabelPrepare"), ex.Message));
      }
      catch(FileNotFoundException ex)
      {
         StatusMessage = string.Format(L.Get("CertificateView.Error.LabelPrint"), ex.Message);
         await _dialogService.ShowErrorAsync(L.Get("Common.Error"), string.Format(L.Get("CertificateView.Error.OpenLabel"), ex.Message));
      }
      catch(IOException ex)
      {
         StatusMessage = string.Format(L.Get("CertificateView.Error.LabelPrint"), ex.Message);
         await _dialogService.ShowErrorAsync(L.Get("Common.Error"), string.Format(L.Get("CertificateView.Error.PrintLabelPrepare"), ex.Message));
      }
      catch(Win32Exception ex)
      {
         StatusMessage = string.Format(L.Get("CertificateView.Error.LabelPrint"), ex.Message);
         await _dialogService.ShowErrorAsync(L.Get("Common.Error"), string.Format(L.Get("CertificateView.Error.OpenLabel"), ex.Message));
      }
      finally
      {
         IsLoading = false;
      }
   }

   /// <summary>
   /// Updates in-app certificate preview to selected test session.
   /// </summary>
   private async Task SelectSessionAsync(TestSession? session)
   {
      if(session == null)
      {
         return;
      }

      _selectedSession = await _diskCardRepository.GetTestSessionWithoutSamplesAsync(session.Id) ?? session;
      if(Certificate == null)
      {
         return;
      }

      var card = await _diskCardRepository.GetByIdAsync(_selectedSession.DiskCardId);
      if(card == null)
      {
         return;
      }

      Certificate = await _certificateGenerator.GenerateCertificateAsync(_selectedSession, card);
      await PrepareCertificateGraphAsync(Certificate, updateView: true);
      UpdateDetailedSummaries(_selectedSession);
   }

   [RelayCommand]
   private void GoBack()
   {
      _navigationService.NavigateTo<DiskCardDetailViewModel>();
   }

   #endregion Commands

   #region Private Methods

   private async Task LoadCertificateAsync()
   {
      try
      {
         IsLoading = true;
         StatusMessage = L.Get("CertificateView.Status.Loading");
         _writeGraphSamples = [];
         _readGraphSamples = [];
         _seekLatencyGraphSamples = [];
         SeekScatterPoints = new ObservableCollection<ObservablePoint>();

         DiskCard? card = null;
         DiskCertificate? selectedCert = null;
         _selectedSession = null;

         if(_selectedDiskService.SelectedCertificateId.HasValue)
         {
            selectedCert = await _diskCardRepository.GetCertificateAsync(_selectedDiskService.SelectedCertificateId.Value);

            if(selectedCert?.TestSessionId > 0)
            {
               _selectedSession = await _diskCardRepository.GetTestSessionWithoutSamplesAsync(selectedCert.TestSessionId);
               if(_selectedSession != null)
               {
                  card = await _diskCardRepository.GetByIdAsync(_selectedSession.DiskCardId);
               }
            }

            if(card == null && selectedCert?.DiskCardId > 0)
            {
               card = await _diskCardRepository.GetByIdAsync(selectedCert.DiskCardId);
            }
         }

         if(_selectedSession == null && _selectedDiskService.SelectedTestSessionId.HasValue)
         {
            StatusMessage = L.Get("CertificateView.Status.LoadingTestDetail");
            _selectedSession = await _diskCardRepository.GetTestSessionWithoutSamplesAsync(_selectedDiskService.SelectedTestSessionId.Value);
            if(_selectedSession != null)
            {
               card = await _diskCardRepository.GetByIdAsync(_selectedSession.DiskCardId) ?? card;
            }
         }

         if(card == null && _selectedDiskService.SelectedDisk != null)
         {
            card = await _diskCardRepository.GetByDevicePathAsync(_selectedDiskService.SelectedDisk.Path);
         }

         if(card == null)
         {
            var allCards = await _diskCardRepository.GetAllAsync();
            card = allCards.OrderByDescending(c => c.LastTestedAt).FirstOrDefault();
         }

         if(card == null)
         {
            StatusMessage = L.Get("CertificateView.Status.CardNotFound");
            return;
         }

         if(selectedCert == null && _selectedSession != null)
         {
            selectedCert = await _certificateGenerator.GenerateCertificateAsync(_selectedSession, card);
         }

         var latestCert = selectedCert ?? await _diskCardRepository.GetLatestCertificateAsync(card.Id);

         if(latestCert == null)
         {
            var sessions = await _diskCardRepository.GetTestSessionsAsync(card.Id);
            var latestSessionId = sessions.OrderByDescending(s => s.StartedAt).Select(s => (int?)s.Id).FirstOrDefault();
            if(latestSessionId.HasValue)
            {
               StatusMessage = L.Get("CertificateView.Status.LoadingLastTest");
               _selectedSession = await _diskCardRepository.GetTestSessionWithoutSamplesAsync(latestSessionId.Value);
               if(_selectedSession != null)
               {
                  var sessionCard = await _diskCardRepository.GetByIdAsync(_selectedSession.DiskCardId);
                  if(sessionCard != null)
                  {
                     card = sessionCard;
                     latestCert = await _certificateGenerator.GenerateCertificateAsync(_selectedSession, sessionCard);
                  }
               }
            }
         }

         if(latestCert != null)
         {
            if(_selectedSession == null && latestCert.TestSessionId > 0)
            {
               _selectedSession = await _diskCardRepository.GetTestSessionWithoutSamplesAsync(latestCert.TestSessionId);
            }

            if(_selectedSession != null)
            {
               var sessionCard = await _diskCardRepository.GetByIdAsync(_selectedSession.DiskCardId);
               if(sessionCard != null)
               {
                  card = sessionCard;
                  latestCert = await _certificateGenerator.GenerateCertificateAsync(_selectedSession, sessionCard);
               }
            }
            else if(latestCert.DiskCardId > 0)
            {
               card = await _diskCardRepository.GetByIdAsync(latestCert.DiskCardId) ?? card;
            }

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

            AvailableSessions.Clear();
            var sessionList = await _diskCardRepository.GetTestSessionsAsync(card.Id);
            foreach(var session in sessionList.OrderByDescending(s => s.StartedAt))
            {
               AvailableSessions.Add(session);
            }

            if(_selectedSession == null)
            {
               int? initialSessionId = _selectedDiskService.SelectedTestSessionId
                   ?? (latestCert.TestSessionId > 0 ? latestCert.TestSessionId : (int?)null)
                   ?? AvailableSessions.FirstOrDefault()?.Id;

               if(initialSessionId.HasValue)
               {
                  _selectedSession = await _diskCardRepository.GetTestSessionWithoutSamplesAsync(initialSessionId.Value);
               }
            }

            await ApplySmartSummaryAsync(latestCert, card);
            Certificate = latestCert;
            await PrepareCertificateGraphAsync(latestCert, updateView: true);
            if(_selectedSession != null)
            {
               UpdateDetailedSummaries(_selectedSession);
               SelectedSessionItem = AvailableSessions.FirstOrDefault(s => s.Id == _selectedSession.Id);
            }
            StatusMessage = string.Format(L.Get("CertificateView.Status.Loaded"), latestCert.CertificateNumber);
          }
          else
          {
             ResetGraphToDefaults();
             StatusMessage = L.Get("CertificateView.Status.NoCertificate");
          }
       }
       catch(DbException ex)
       {
          StatusMessage = string.Format(L.Get("CertificateView.Error.LoadStatus"), ex.Message);
          await _dialogService.ShowErrorAsync(L.Get("Common.Error"), string.Format(L.Get("CertificateView.Error.Load"), ex.Message));
       }
       catch(InvalidOperationException ex)
       {
          StatusMessage = string.Format(L.Get("CertificateView.Error.LoadStatus"), ex.Message);
          await _dialogService.ShowErrorAsync(L.Get("Common.Error"), string.Format(L.Get("CertificateView.Error.Load"), ex.Message));
       }
       catch(IOException ex)
       {
          StatusMessage = string.Format(L.Get("CertificateView.Error.LoadStatus"), ex.Message);
          await _dialogService.ShowErrorAsync(L.Get("Common.Error"), string.Format(L.Get("CertificateView.Error.Load"), ex.Message));
       }
       catch(ExternalException ex)
       {
          StatusMessage = string.Format(L.Get("CertificateView.Error.LoadStatus"), ex.Message);
          await _dialogService.ShowErrorAsync(L.Get("Common.Error"), string.Format(L.Get("CertificateView.Error.Render"), ex.Message));
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

      if(certificate.PowerOnHours <= 0 && card.PowerOnHours.HasValue)
      {
         certificate.PowerOnHours = card.PowerOnHours.Value;
      }

      if(certificate.PowerCycles <= 0 && card.PowerCycleCount.HasValue)
      {
         certificate.PowerCycles = card.PowerCycleCount.Value;
      }

      if(certificate.ReallocatedSectors > 0 || certificate.PendingSectors > 0)
      {
         return;
      }

      var sessions = await _diskCardRepository.GetTestSessionsAsync(card.Id);
      var latestSmartSessionId = sessions
          .Where(s => s.TestType == DiskChecker.Core.Models.TestType.SmartShort || s.TestType == DiskChecker.Core.Models.TestType.SmartExtended || s.TestType == DiskChecker.Core.Models.TestType.SmartConveyance)
          .OrderByDescending(s => s.StartedAt)
          .Select(s => (int?)s.Id)
          .FirstOrDefault();

      if(!latestSmartSessionId.HasValue)
      {
         return;
      }

      var smartSession = await _diskCardRepository.GetTestSessionWithoutSamplesAsync(latestSmartSessionId.Value);
      if(smartSession == null)
      {
         return;
      }

      var reallocated = smartSession.SmartBefore?.ReallocatedSectorCount ?? 0;
      var pending = smartSession.SmartBefore?.PendingSectorCount ?? 0;

      if(certificate.ReallocatedSectors <= 0 && reallocated > 0)
      {
         certificate.ReallocatedSectors = reallocated;
      }

      if(certificate.PendingSectors <= 0 && pending > 0)
      {
         certificate.PendingSectors = pending;
      }

      certificate.SmartPassed = certificate.ReallocatedSectors == 0 && certificate.PendingSectors == 0;
   }

   private async Task PrepareCertificateGraphAsync(DiskCertificate certificate, bool updateView)
   {
      ArgumentNullException.ThrowIfNull(certificate);

      await EnsureCertificateGraphDataAsync(certificate);
      RefreshCertificateResultProperties();

      if(updateView)
      {
         await RebuildPerformanceGraphAsync(certificate).ConfigureAwait(true);
      }
   }

   private async Task EnsureCertificateGraphDataAsync(DiskCertificate certificate)
   {
      ArgumentNullException.ThrowIfNull(certificate);

      var hasWriteProfile = certificate.WriteProfilePoints is { Count: > 0 };
      var hasReadProfile = certificate.ReadProfilePoints is { Count: > 0 };
      var testTypeStr = certificate.TestType ?? string.Empty;
      var isSeekCertificate = certificate.SeekAvgLatencyMs.HasValue ||
         string.Equals(testTypeStr, "Seek", StringComparison.OrdinalIgnoreCase) ||
         testTypeStr.Contains("Seek", StringComparison.OrdinalIgnoreCase);

      if(_selectedSession != null)
      {
         certificate.ChartImagePath = _selectedSession.ChartImagePath;
      }

      var sessionId = certificate.TestSessionId;
      if(sessionId <= 0 && _selectedDiskService.SelectedTestSessionId.HasValue)
      {
         sessionId = _selectedDiskService.SelectedTestSessionId.Value;
      }

      if(_selectedSession == null && sessionId > 0)
      {
         _selectedSession = await _diskCardRepository.GetTestSessionWithoutSamplesAsync(sessionId);
         if(_selectedSession != null)
         {
            certificate.ChartImagePath = _selectedSession.ChartImagePath;
         }
      }

      if(isSeekCertificate && _seekLatencyGraphSamples.Count == 0 && sessionId > 0)
      {
         _seekLatencyGraphSamples = await LoadSeekLatencySamplesAsync(sessionId);
         if(_seekLatencyGraphSamples.Count > 0)
         {
            certificate.SeekLatencyPoints = _seekLatencyGraphSamples.ToList();
         }
      }

      if(isSeekCertificate && _seekLatencyGraphSamples.Count == 0 && certificate.SeekLatencyPoints.Count > 0)
      {
         _seekLatencyGraphSamples = certificate.SeekLatencyPoints.Where(v => v > 0).ToList();
      }

      if(isSeekCertificate)
      {
         return;
      }

      if((_writeGraphSamples.Count == 0 && _readGraphSamples.Count == 0) && sessionId > 0)
      {
         StatusMessage = L.Get("CertificateView.Status.LoadingGraphData");
         var speedSeries = await LoadCertificateGraphSamplesProgressiveAsync(sessionId);
         _writeGraphSamples = DownsampleToLimit(speedSeries.WriteSamples, 512);
         _readGraphSamples = DownsampleToLimit(speedSeries.ReadSamples, 512);
      }

      if(_writeGraphSamples.Count == 0 && _readGraphSamples.Count == 0)
      {
         return;
      }

      if(!hasWriteProfile)
      {
         certificate.WriteProfilePoints = DownsampleSpeedSamples(_writeGraphSamples.Select(s => s.SpeedMBps), 32);
      }

      if(!hasReadProfile)
      {
         certificate.ReadProfilePoints = DownsampleSpeedSamples(_readGraphSamples.Select(s => s.SpeedMBps), 32);
      }

      BackfillCertificatePerformanceFromSamples(certificate, _writeGraphSamples, _readGraphSamples);
   }

   private async Task HydrateCertificateForOutputAsync(DiskCertificate certificate)
   {
      var sessionId = certificate.TestSessionId > 0 ? certificate.TestSessionId : _selectedSession?.Id ?? 0;
      if(sessionId <= 0)
      {
         return;
      }

      if(_seekLatencyGraphSamples.Count == 0 && sessionId > 0)
      {
         _seekLatencyGraphSamples = await LoadSeekLatencySamplesAsync(sessionId);
         if(_seekLatencyGraphSamples.Count > 0)
         {
            certificate.SeekLatencyPoints = _seekLatencyGraphSamples.ToList();
         }
      }

      if(_writeGraphSamples.Count == 0 && _readGraphSamples.Count == 0)
      {
         var speedSeries = await LoadCertificateGraphSamplesProgressiveAsync(sessionId);
         _writeGraphSamples = DownsampleToLimit(speedSeries.WriteSamples, 512);
         _readGraphSamples = DownsampleToLimit(speedSeries.ReadSamples, 512);
      }

      if(certificate.WriteProfilePoints.Count == 0 && _writeGraphSamples.Count > 0)
      {
         certificate.WriteProfilePoints = DownsampleSpeedSamples(_writeGraphSamples.Select(s => s.SpeedMBps), 32);
      }

      if(certificate.ReadProfilePoints.Count == 0 && _readGraphSamples.Count > 0)
      {
         certificate.ReadProfilePoints = DownsampleSpeedSamples(_readGraphSamples.Select(s => s.SpeedMBps), 32);
      }

      BackfillCertificatePerformanceFromSamples(certificate, _writeGraphSamples, _readGraphSamples);
      RefreshCertificateResultProperties();
   }
   private static void BackfillCertificatePerformanceFromSamples(
      DiskCertificate certificate,
      IReadOnlyList<SpeedSample> writeSamples,
      IReadOnlyList<SpeedSample> readSamples)
   {
      var writeValues = writeSamples.Where(s => s.SpeedMBps > 0).Select(s => s.SpeedMBps).ToList();
      var readValues = readSamples.Where(s => s.SpeedMBps > 0).Select(s => s.SpeedMBps).ToList();

      if(certificate.AvgWriteSpeed <= 0 && writeValues.Count > 0)
      {
         certificate.AvgWriteSpeed = writeValues.Average();
      }

      if(certificate.MaxWriteSpeed <= 0 && writeValues.Count > 0)
      {
         certificate.MaxWriteSpeed = writeValues.Max();
      }

      if(certificate.AvgReadSpeed <= 0 && readValues.Count > 0)
      {
         certificate.AvgReadSpeed = readValues.Average();
      }

      if(certificate.MaxReadSpeed <= 0 && readValues.Count > 0)
      {
         certificate.MaxReadSpeed = readValues.Max();
      }
   }

   private void RefreshCertificateResultProperties()
   {
      OnPropertyChanged(nameof(TestDuration));
      OnPropertyChanged(nameof(Errors));
      OnPropertyChanged(nameof(TemperatureRange));
      OnPropertyChanged(nameof(AvgWriteSpeed));
      OnPropertyChanged(nameof(AvgReadSpeed));
      OnPropertyChanged(nameof(HealthStatus));
      OnPropertyChanged(nameof(Recommended));
      OnPropertyChanged(nameof(SanitizationPerformed));
      OnPropertyChanged(nameof(SanitizationMethodText));
      OnPropertyChanged(nameof(DataVerifiedText));
      OnPropertyChanged(nameof(PartitionSchemeText));
      OnPropertyChanged(nameof(FileSystemText));
      OnPropertyChanged(nameof(VolumeLabelText));
      OnPropertyChanged(nameof(TestTypeLine));
      OnPropertyChanged(nameof(TestDurationLine));
      OnPropertyChanged(nameof(ErrorsLine));
      OnPropertyChanged(nameof(TemperatureRangeLine));
      OnPropertyChanged(nameof(AvgWriteSpeedLine));
      OnPropertyChanged(nameof(AvgReadSpeedLine));
      OnPropertyChanged(nameof(HealthStatusLine));
      OnPropertyChanged(nameof(RecommendedLine));
      OnPropertyChanged(nameof(SmartPowerOnHoursLine));
      OnPropertyChanged(nameof(SmartPowerCyclesLine));
      OnPropertyChanged(nameof(SmartReallocatedSectorsLine));
      OnPropertyChanged(nameof(SmartPendingSectorsLine));
      OnPropertyChanged(nameof(SeekSummaryLine));
      OnPropertyChanged(nameof(Sanitize1WriteLine));
      OnPropertyChanged(nameof(Sanitize2WriteLine));
      OnPropertyChanged(nameof(Sanitize1ReadLine));
      OnPropertyChanged(nameof(Sanitize2ReadLine));
      OnPropertyChanged(nameof(WriteSpeedChangeLine));
      OnPropertyChanged(nameof(ReadSpeedChangeLine));
   }

   private async Task<List<double>> LoadSeekLatencySamplesAsync(int sessionId)
   {
      try
      {
         var records = await _diskCardRepository.GetSeekSamplesAsync(sessionId);
         var values = records
            .Where(s => !s.HasError && s.LatencyMs > 0)
            .OrderBy(s => s.TestType)
            .ThenBy(s => s.Index)
            .Select(s => s.LatencyMs)
            .ToList();
         if(values.Count > 0)
         {
            return values;
         }
      }
      catch
      {
         // Fall back to SeekResultsJson below for older databases or partially migrated data.
      }

      return ExtractSeekLatenciesFromSession(_selectedSession);
   }

   private static List<double> ExtractSeekLatenciesFromSession(TestSession? session)
   {
      if(string.IsNullOrWhiteSpace(session?.SeekResultsJson))
      {
         return [];
      }

      try
      {
         var single = JsonSerializer.Deserialize<SeekTestResult>(session.SeekResultsJson);
         if(single?.Samples.Count > 0)
         {
            return single.Samples.Where(s => !s.HasError && s.LatencyMs > 0).OrderBy(s => s.Index).Select(s => s.LatencyMs).ToList();
         }
      }
      catch(JsonException) { }

      try
      {
         var envelope = JsonSerializer.Deserialize<SeekResultsEnvelope>(session.SeekResultsJson);
         return new[] { envelope?.FullStroke, envelope?.Random, envelope?.Skip }
            .Where(r => r != null)
            .SelectMany(r => r!.Samples)
            .Where(s => !s.HasError && s.LatencyMs > 0)
            .OrderBy(s => s.Index)
            .Select(s => s.LatencyMs)
            .ToList();
      }
      catch(JsonException)
      {
         return [];
      }
   }

   private void ApplySeekScatterGraph(IReadOnlyList<double> latencies)
   {
      var values = latencies.Where(v => v > 0).ToList();
      if(values.Count == 0)
      {
         SeekScatterPoints = new ObservableCollection<ObservablePoint>();
         return;
      }

      var max = values.Max();
      var yMax = Math.Max(max * 1.15, max + 1);
      const double startX = 10d;
      const double endX = 490d;
      const double minY = 18d;
      const double maxY = 102d;
      var points = new ObservableCollection<ObservablePoint>();

      for(var i = 0; i < values.Count; i++)
      {
         var x = values.Count == 1 ? startX : startX + (i / (double)(values.Count - 1)) * (endX - startX);
         var y = maxY - ((maxY - minY) * Math.Clamp(values[i] / yMax, 0d, 1d));
         points.Add(new ObservablePoint(x, y));
      }

      SeekScatterPoints = points;
      TemperatureProfilePoints = "10,110 490,110";
      HasTemperatureProfile = false;
      ChartMaxSpeedLabel = $"{yMax:F1} ms";
      ChartMidSpeedLabel = $"{(yMax / 2):F1} ms";
      ChartMinSpeedLabel = "0 ms";
      ChartXAxisStartLabel = "1";
      ChartXAxisMidLabel = Math.Max(1, values.Count / 2).ToString(CultureInfo.InvariantCulture);
      ChartXAxisEndLabel = values.Count.ToString(CultureInfo.InvariantCulture);
   }

   private sealed class SeekResultsEnvelope
   {
      public SeekTestResult? FullStroke { get; set; }
      public SeekTestResult? Random { get; set; }
      public SeekTestResult? Skip { get; set; }
   }
   private async Task<(List<SpeedSample> WriteSamples, List<SpeedSample> ReadSamples)> LoadCertificateGraphSamplesProgressiveAsync(int sessionId)
    {
       // SQL-level downsampling: the database returns only ~512 rows per phase
       // using ROW_NUMBER() window function. This keeps memory bounded
       // regardless of dataset size (e.g. sanitization tests with millions of
       // samples) and avoids the OOM that the previous chunked-loading approach
       // caused.
       const int graphMaxPoints = 512;

       try
       {
          return await _diskCardRepository.GetSpeedSampleSeriesDownsampledAsync(sessionId, graphMaxPoints);
       }
       catch(DbException)
       {
          return (new List<SpeedSample>(), new List<SpeedSample>());
       }
       catch(IOException)
       {
          return (new List<SpeedSample>(), new List<SpeedSample>());
       }
    }

    private async Task RebuildPerformanceGraphAsync(DiskCertificate certificate)
    {
       ArgumentNullException.ThrowIfNull(certificate);

       if(HasSeekMetrics)
       {
          var seekValues = _seekLatencyGraphSamples.Count > 0
             ? _seekLatencyGraphSamples
             : certificate.SeekLatencyPoints.Where(v => v > 0).ToList();
          ApplySeekScatterGraph(seekValues);
          return;
       }

       var writeGraphSamples = _writeGraphSamples;
       var readGraphSamples = _readGraphSamples;
       var temperatureSamples = _selectedSession?.TemperatureSamples ?? [];
       var graphData = await Task.Run(() => BuildGraphData(certificate, writeGraphSamples, readGraphSamples, temperatureSamples)).ConfigureAwait(true);

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
    }

    private void ResetGraphToDefaults()
    {
       _writeGraphSamples = [];
       _readGraphSamples = [];
       _seekLatencyGraphSamples = [];
       SeekScatterPoints = new ObservableCollection<ObservablePoint>();
       var graphData = CertificateGraphData.Default;
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
    }

    private void UpdateDetailedSummaries(TestSession session)
    {
       ArgumentNullException.ThrowIfNull(session);

       var totalErrors = session.WriteErrors + session.ReadErrors + session.VerificationErrors;
       SelectedSessionSummary =
           $"Test: {session.TestType} | Datum: {session.StartedAt:dd.MM.yyyy HH:mm}\n" +
           $"Skóre: {session.Score:F0}/100 | Známka: {session.Grade} | Výsledek: {session.Result}\n" +
           $"Zápis AVG/MIN/MAX: {session.AverageWriteSpeedMBps:F1}/{session.MinWriteSpeedMBps:F1}/{session.MaxWriteSpeedMBps:F1} MB/s\n" +
           $"Čtení AVG/MIN/MAX: {session.AverageReadSpeedMBps:F1}/{session.MinReadSpeedMBps:F1}/{session.MaxReadSpeedMBps:F1} MB/s\n" +
           $"Chyby: {totalErrors} | Doba: {session.Duration:hh\\:mm\\:ss}";

       SelectedSessionThermalSummary = BuildThermalSummary(session);
       SelectedSessionSmartSummary = BuildSmartSummary(session);
    }

    private static CertificateGraphData BuildGraphData(
        DiskCertificate certificate,
        IReadOnlyList<SpeedSample> writeGraphSamples,
        IReadOnlyList<SpeedSample> readGraphSamples,
        IReadOnlyList<TemperatureSample> temperatureSamples)
    {
       ArgumentNullException.ThrowIfNull(certificate);

       var writePoints = GetGraphSamples(writeGraphSamples, certificate.AvgWriteSpeed, certificate.MaxWriteSpeed);
       var readPoints = GetGraphSamples(readGraphSamples, certificate.AvgReadSpeed, certificate.MaxReadSpeed);
       var allValues = writePoints.Select(p => p.SpeedMBps).Concat(readPoints.Select(p => p.SpeedMBps)).ToList();

       if(allValues.Count == 0)
       {
          return CertificateGraphData.Default;
       }

       var minSpeed = allValues.Min();
       var maxSpeed = allValues.Max();
       var spread = maxSpeed - minSpeed;
       if(spread < Math.Max(1d, maxSpeed * 0.02))
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

    private static string BuildTemperaturePolylinePoints(IReadOnlyList<TemperatureSample> samples)
    {
       if(samples.Count == 0)
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

       foreach(var sample in samples)
       {
          var x = startX + (Math.Clamp(sample.ProgressPercent, 0d, 100d) / 100d) * (endX - startX);
          var ratio = Math.Clamp((sample.TemperatureCelsius - minTemp) / range, 0d, 1d);
          var y = maxY - ((maxY - minY) * ratio);
          points.Add(FormattableString.Invariant($"{x:0},{y:0}"));
       }

       return string.Join(" ", points);
    }

    private static List<SpeedSample> GetGraphSamples(IReadOnlyList<SpeedSample> samples, double averageSpeed, double maxSpeed)
    {
       var values = samples.Where(s => s.SpeedMBps > 0).OrderBy(s => s.ProgressPercent).ToList();
       if(values.Count > 0)
       {
          return DownsampleGraphSamples(values, 32);
       }

       if(averageSpeed <= 0 && maxSpeed <= 0)
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
       if(values.Count == 0)
       {
          return "10,102 70,102 130,102 190,102 250,102 310,102 370,102 430,102 490,102";
       }

       var startX = 10d;
       var endX = 490d;
       var minY = 18d;
       var maxY = 102d;
       var range = Math.Max(0.0001d, maxSpeed - minSpeed);
       var points = new List<string>(values.Count);

       foreach(var sample in values)
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
       if(values.Count <= targetPoints)
       {
          return values.ToList();
       }

       var result = new List<SpeedSample>(targetPoints);
       var bucketSize = values.Count / (double)targetPoints;

       for(var i = 0; i < targetPoints; i++)
       {
          var start = (int)Math.Floor(i * bucketSize);
          var end = (int)Math.Floor((i + 1) * bucketSize);
          end = Math.Clamp(end, start + 1, values.Count);

          var sumSpeed = 0d;
          var sumProgress = 0d;
          var count = 0;
          for(var j = start; j < end; j++)
          {
             sumSpeed += values[j].SpeedMBps;
             sumProgress += values[j].ProgressPercent;
             count++;
          }

          if(count > 0)
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

    private static List<double> DownsampleSpeedSamples(IEnumerable<double> speeds, int targetPoints)
    {
       ArgumentNullException.ThrowIfNull(speeds);

       var values = speeds.Where(v => v > 0).ToList();
       if(values.Count == 0)
       {
          return [];
       }

       if(values.Count <= targetPoints)
       {
          return values;
       }

       var result = new List<double>(targetPoints);
       var bucketSize = values.Count / (double)targetPoints;

       for(var i = 0; i < targetPoints; i++)
       {
          var start = (int)Math.Floor(i * bucketSize);
          var end = (int)Math.Floor((i + 1) * bucketSize);
          end = Math.Clamp(end, start + 1, values.Count);

          var sum = 0d;
          var count = 0;
          for(var j = start; j < end; j++)
          {
             sum += values[j];
             count++;
          }

          result.Add(count > 0 ? sum / count : values[Math.Min(start, values.Count - 1)]);
       }

       return result;
    }

    private string BuildThermalSummary(TestSession session)
    {
       var tempSamples = session.TemperatureSamples.OrderBy(t => t.ProgressPercent).ToList();
       if(tempSamples.Count == 0)
       {
          return L.Get("CertificateView.Thermal.Unavailable");
       }

       var minTemp = tempSamples.Min(t => t.TemperatureCelsius);
       var maxTemp = tempSamples.Max(t => t.TemperatureCelsius);
       var avgTemp = tempSamples.Average(t => t.TemperatureCelsius);
       return string.Format(L.Get("CertificateView.Session.ThermalSummaryFormat"), minTemp, avgTemp, maxTemp, tempSamples.Count);
    }

    private string BuildSmartSummary(TestSession session)
    {
       var smart = session.SmartBefore;
       if(smart == null)
       {
          return L.Get("CertificateView.Smart.Unavailable");
       }

       return
           $"Model: {smart.DeviceModel}\n" +
           $"FW: {smart.FirmwareVersion} | Teplota: {(smart.Temperature?.ToString() ?? "N/A")}°C\n" +
           $"Power-On Hours: {smart.PowerOnHours?.ToString() ?? "N/A"} | Power Cycles: {smart.PowerCycleCount}\n" +
           $"Reallocated: {smart.ReallocatedSectorCount?.ToString() ?? "N/A"} | Pending: {smart.PendingSectorCount?.ToString() ?? "N/A"} | Uncorrectable: {smart.UncorrectableErrorCount?.ToString() ?? "N/A"}";
    }

    private string BuildDiagnosticHighlightsText(string? notes)
    {
       if(string.IsNullOrWhiteSpace(notes))
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

       if(parts.Count == 0)
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

    /// <summary>
    /// Downsampleuje vzorky na zadaný limit pomocí rovnoměrného výběru.
    /// Používá se před generováním certifikátu pro prevenci OutOfMemoryException.
    /// </summary>
    private static List<SpeedSample> DownsampleToLimit(List<SpeedSample> samples, int maxPoints)
    {
        if (samples == null || samples.Count <= maxPoints)
        {
            return samples ?? new List<SpeedSample>();
        }

        var result = new List<SpeedSample>(maxPoints);
        var step = (double)samples.Count / maxPoints;

        for (int i = 0; i < maxPoints; i++)
        {
            var index = (int)(i * step);
            if (index < samples.Count)
            {
                result.Add(samples[index]);
            }
        }

        return result;
    }

    /// <summary>
    /// Downsampleuje teplotní vzorky na zadaný limit pomocí rovnoměrného výběru.
    /// Používá se před generováním certifikátu pro prevenci OutOfMemoryException.
    /// </summary>
    private static List<TemperatureSample> DownsampleTemperaturesToLimit(List<TemperatureSample> samples, int maxPoints)
    {
        if (samples == null || samples.Count <= maxPoints)
        {
            return samples ?? new List<TemperatureSample>();
        }

        var result = new List<TemperatureSample>(maxPoints);
        var step = (double)samples.Count / maxPoints;

        for (int i = 0; i < maxPoints; i++)
        {
            var index = (int)(i * step);
            if (index < samples.Count)
            {
                result.Add(samples[index]);
            }
        }

        return result;
    }

   #endregion
}

