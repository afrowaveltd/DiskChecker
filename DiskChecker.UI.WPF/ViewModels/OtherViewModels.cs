using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DiskChecker.Application.Services;
using DiskChecker.Core.Interfaces;
using DiskChecker.Core.Models;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Win32;
using OxyPlot;
using OxyPlot.Axes;
using OxyPlot.Series;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;

namespace DiskChecker.UI.WPF.ViewModels;

/// <summary>
/// Represents a single speed sample point in the real-time graph.
/// </summary>
public class SpeedSample
{
   /// <summary>
   /// Time of the sample (relative seconds).
   /// </summary>
   public double TimeSeconds { get; set; }

   /// <summary>
   /// Throughput in MB/s.
   /// </summary>
   public double ThroughputMbps { get; set; }

   /// <summary>
   /// Is this a write or read sample (0=write, 1=read).
   /// </summary>
   public int Phase { get; set; }
}

/// <summary>
/// ViewModel pro Surface test (zápis + ověření) s vizualizací.
/// </summary>
public partial class SurfaceTestViewModel : ViewModelBase, IDisposable
{
   public enum SurfaceRunMode
   {
      FullWriteRead,
      ReadOnlyFullScan,
      FullDiskErase,
      NonDestructiveFileWriteRead
   }

   public class SurfaceRunModeOption
   {
      public SurfaceRunMode Value { get; init; }
      public string Label { get; init; } = string.Empty;
   }

   public enum GraphTimeRange
   {
      Full,
      Last1Minute,
      Last5Minutes,
      Last15Minutes,
      Last30Minutes
   }

   public class GraphTimeRangeOption
   {
      public GraphTimeRange Value { get; init; }
      public string Label { get; init; } = string.Empty;
   }

   public enum GraphOverlayMode
   {
      None,
      SameSizeReference
   }

   public class GraphOverlayModeOption
   {
      public GraphOverlayMode Value { get; init; }
      public string Label { get; init; } = string.Empty;
   }

   private readonly ISurfaceTestService _surfaceTestService;
   private readonly DiskCheckerService _diskCheckerService;
   private readonly SmartCheckService _smartCheckService;
   private readonly HistoryService _historyService;
   private readonly LineSeries _currentSpeedSeries;
   private readonly LineSeries _referenceSpeedSeries;
   private readonly List<SpeedSample> _referenceSpeedSamples = [];
   private CancellationTokenSource? _testCancellationTokenSource;
   private DateTime _testStartTime;
   private SurfaceTestResult? _lastSurfaceTestResult;
   private SmartCheckResult? _lastSmartCheckResult;
   private readonly int _totalBlocks = 2048; // Default počet bloků k vizualizaci

   [ObservableProperty]
   private bool isTestRunning;

   [ObservableProperty]
   private double progressPercent;

   [ObservableProperty]
   private long bytesProcessed;

   [ObservableProperty]
   private long totalBytes;

   [ObservableProperty]
   private double currentThroughputMbps;

   [ObservableProperty]
   private double averageThroughputMbps;

   [ObservableProperty]
   private int errorCount;

   [ObservableProperty]
   private TimeSpan elapsedTime;

   [ObservableProperty]
   private TimeSpan estimatedTimeRemaining;

   [ObservableProperty]
   private string currentPhase = "Připravuji test..."; // "Writing", "Reading", "Completed"

   [ObservableProperty]
   private ObservableCollection<BlockStatus> blocks = [];

   [ObservableProperty]
   private ObservableCollection<SpeedSample> speedSamples = [];

   [ObservableProperty]
   private string selectedDriveInfo = "Vyber disk pro test";

   [ObservableProperty]
   private CoreDriveInfo? selectedDrive;

   [ObservableProperty]
   private ObservableCollection<CoreDriveInfo> availableDrives = [];

   private bool _canPrintCertificate;
   private string _certificateStatus = "Certifikát zatím není připraven.";
   private SurfaceRunMode _selectedRunMode = SurfaceRunMode.FullWriteRead;
   private readonly IReadOnlyList<SurfaceRunModeOption> _availableRunModes =
   [
      new() { Value = SurfaceRunMode.FullWriteRead, Label = "Kompletní test (zápis + čtení)" },
      new() { Value = SurfaceRunMode.ReadOnlyFullScan, Label = "Nedestruktivní test čtení celého povrchu" },
      new() { Value = SurfaceRunMode.FullDiskErase, Label = "Kompletní výmaz disku (sanitizace)" },
      new() { Value = SurfaceRunMode.NonDestructiveFileWriteRead, Label = "Nedestruktivní test souboru na partition (zápis + čtení)" }
   ];
   private string _systemDiskProtectionMessage = string.Empty;
   private PlotModel _speedPlotModel;
   private GraphTimeRange _selectedGraphTimeRange = GraphTimeRange.Full;
   private readonly IReadOnlyList<GraphTimeRangeOption> _availableGraphTimeRanges =
   [
      new() { Value = GraphTimeRange.Full, Label = "Celý test" },
      new() { Value = GraphTimeRange.Last1Minute, Label = "Poslední 1 minuta" },
      new() { Value = GraphTimeRange.Last5Minutes, Label = "Posledních 5 minut" },
      new() { Value = GraphTimeRange.Last15Minutes, Label = "Posledních 15 minut" },
      new() { Value = GraphTimeRange.Last30Minutes, Label = "Posledních 30 minut" }
   ];
   private GraphOverlayMode _selectedGraphOverlayMode = GraphOverlayMode.None;
   private readonly IReadOnlyList<GraphOverlayModeOption> _availableGraphOverlayModes =
   [
      new() { Value = GraphOverlayMode.None, Label = "Bez overlay" },
      new() { Value = GraphOverlayMode.SameSizeReference, Label = "Referenční disk stejné velikosti" }
   ];
   private string _referenceOverlayStatus = "Overlay není načten.";

   public bool CanPrintCertificate
   {
      get => _canPrintCertificate;
      private set => SetProperty(ref _canPrintCertificate, value);
   }

   public string CertificateStatus
   {
      get => _certificateStatus;
      private set => SetProperty(ref _certificateStatus, value);
   }

   public SurfaceRunMode SelectedRunMode
   {
      get => _selectedRunMode;
      set
      {
         if(SetProperty(ref _selectedRunMode, value))
         {
            UpdateRunModeMessage();
         }
      }
   }

   public IReadOnlyList<SurfaceRunModeOption> AvailableRunModes => _availableRunModes;

   public string SystemDiskProtectionMessage
   {
      get => _systemDiskProtectionMessage;
      private set => SetProperty(ref _systemDiskProtectionMessage, value);
   }

   public PlotModel SpeedPlotModel
   {
      get => _speedPlotModel;
      private set => SetProperty(ref _speedPlotModel, value);
   }

   public GraphTimeRange SelectedGraphTimeRange
   {
      get => _selectedGraphTimeRange;
      set
      {
         if(SetProperty(ref _selectedGraphTimeRange, value))
         {
            UpdateSpeedPlot();
         }
      }
   }

   public IReadOnlyList<GraphTimeRangeOption> AvailableGraphTimeRanges => _availableGraphTimeRanges;

   public GraphOverlayMode SelectedGraphOverlayMode
   {
      get => _selectedGraphOverlayMode;
      set => SetProperty(ref _selectedGraphOverlayMode, value);
   }

   public IReadOnlyList<GraphOverlayModeOption> AvailableGraphOverlayModes => _availableGraphOverlayModes;

   public string ReferenceOverlayStatus
   {
      get => _referenceOverlayStatus;
      private set => SetProperty(ref _referenceOverlayStatus, value);
   }

   /// <summary>
   /// Initializes a new instance of the <see cref="SurfaceTestViewModel"/> class.
   /// </summary>
   public SurfaceTestViewModel(
      ISurfaceTestService surfaceTestService,
      DiskCheckerService diskCheckerService,
      SmartCheckService smartCheckService,
      HistoryService historyService)
   {
      _surfaceTestService = surfaceTestService;
      _diskCheckerService = diskCheckerService;
      _smartCheckService = smartCheckService;
      _historyService = historyService;
      _currentSpeedSeries = new LineSeries
      {
         Title = "Aktuální test",
         Color = OxyColors.SteelBlue,
         StrokeThickness = 2
      };
      _referenceSpeedSeries = new LineSeries
      {
         Title = "Referenční test",
         Color = OxyColors.Gray,
         StrokeThickness = 1.5,
         LineStyle = LineStyle.Dash
      };
      _speedPlotModel = CreateSpeedPlotModel();
      InitializeBlocks();
      InitializeProgressHandling();
   }

   /// <summary>
   /// Inicializuje kolekci bloků pro vizualizaci.
   /// </summary>
   private void InitializeBlocks()
   {
      Blocks = new ObservableCollection<BlockStatus>(
          Enumerable.Range(0, _totalBlocks)
              .Select(i => new BlockStatus { Index = i, Status = 0 })
              .ToList()
      );
   }

   /// <summary>
   /// Spustí test povrchu disku.
   /// </summary>
   [RelayCommand]
   public async Task StartTest()
   {
      if(SelectedDrive == null)
      {
         StatusMessage = "❌ Prosím vyber disk pro testování!";
         return;
      }

      bool isDestructiveMode = SelectedRunMode is SurfaceRunMode.FullWriteRead or SurfaceRunMode.FullDiskErase;
      if(isDestructiveMode && await IsSystemDiskAsync(SelectedDrive))
      {
         SystemDiskProtectionMessage = "⛔ Destruktivní test je z bezpečnostních důvodů zamčen pro systémový disk host OS.";
         StatusMessage = SystemDiskProtectionMessage;
         return;
      }

      SystemDiskProtectionMessage = string.Empty;

      IsTestRunning = true;
      _testCancellationTokenSource = new CancellationTokenSource();
      _testStartTime = DateTime.UtcNow;
      ProgressPercent = 0;
      ErrorCount = 0;
      CanPrintCertificate = false;
      CertificateStatus = "Certifikát zatím není připraven.";
      _lastSurfaceTestResult = null;
      _lastSmartCheckResult = null;
      SpeedSamples.Clear();
      _currentSpeedSeries.Points.Clear();
      if(SelectedGraphOverlayMode == GraphOverlayMode.None)
      {
         _referenceSpeedSamples.Clear();
      }
      UpdateSpeedPlot();
      InitializeBlocks();
      CurrentPhase = "🔵 Příprava zápisu...";
      StatusMessage = $"Zahájena test povrchu: {SelectedDrive.Name}";

      System.Diagnostics.Debug.WriteLine($"[StartTest] Začínám test: {SelectedDrive.Name}, režim: {SelectedRunMode}");

      try
      {
         var request = BuildSurfaceRequest(SelectedDrive, SelectedRunMode);

         System.Diagnostics.Debug.WriteLine($"[StartTest] Request vytvořen: Profile={request.Profile}, Operation={request.Operation}");

         // DŮLEŽITÉ: Progress musí být vytvořen NA UI THREAD aby callback běžel na UI!
         Progress<SurfaceTestProgress> progress = new Progress<SurfaceTestProgress>(OnTestProgress);

         System.Diagnostics.Debug.WriteLine($"[StartTest] Volám _surfaceTestService.RunAsync...");

         // NEPOUŽÍVAT Task.Run! Progress<T> potřebuje UI context!
         var result = await _surfaceTestService.RunAsync(
             request,
             progress,
             _testCancellationTokenSource.Token
         ).ConfigureAwait(true); // true = pokračuj na UI thread pro result handling

         System.Diagnostics.Debug.WriteLine($"[StartTest] Test dokončen! Result: {result.TestId}");

         _lastSurfaceTestResult = result;
         await LoadSmartDataForCertificateAsync();

         CurrentPhase = "✅ Test dokončen!";
         StatusMessage = $"Test povrchu dokončen. Chyb: {ErrorCount}";
      }
      catch(OperationCanceledException)
      {
         CurrentPhase = "⏹️ Test zrušen";
         StatusMessage = "Test byl zrušen uživatelem.";
      }
      catch(Exception ex)
      {
         CurrentPhase = "❌ Chyba";
         StatusMessage = $"Chyba během testu: {ex.Message}";
      }
      finally
      {
         IsTestRunning = false;
         _uiUpdateTimer?.Stop();
         _testCancellationTokenSource?.Dispose();
      }
   }

   private void UpdateRunModeMessage()
   {
      if(SelectedRunMode == SurfaceRunMode.ReadOnlyFullScan)
      {
         SystemDiskProtectionMessage = "✅ Nedestruktivní režim: provádí se pouze čtení povrchu bez změny dat.";
      }
      else if(SelectedRunMode == SurfaceRunMode.NonDestructiveFileWriteRead)
      {
         SystemDiskProtectionMessage = "✅ Nedestruktivní souborový režim: zapisuje a ověřuje pouze testovací soubor na partition.";
      }
      else if(SelectedRunMode == SurfaceRunMode.FullDiskErase)
      {
         SystemDiskProtectionMessage = "⚠️ Sanitizace: kompletní výmaz testovaného disku.";
      }
      else
      {
         SystemDiskProtectionMessage = string.Empty;
      }
   }

   private static SurfaceTestRequest BuildSurfaceRequest(CoreDriveInfo selectedDrive, SurfaceRunMode mode)
   {
      var request = new SurfaceTestRequest
      {
         Drive = selectedDrive,
         Profile = SurfaceTestProfile.HddFull,
         Operation = SurfaceTestOperation.WriteZeroFill,
         BlockSizeBytes = 1024 * 1024,
         SampleIntervalBlocks = 128,
         AllowDeviceWrite = true
      };

      switch(mode)
      {
         case SurfaceRunMode.ReadOnlyFullScan:
            request.Operation = SurfaceTestOperation.ReadOnly;
            request.AllowDeviceWrite = false;
            break;

         case SurfaceRunMode.FullDiskErase:
            request.Profile = SurfaceTestProfile.FullDiskSanitization;
            request.Operation = SurfaceTestOperation.WriteZeroFill;
            request.SecureErase = true;
            request.AllowDeviceWrite = true;
            break;

         case SurfaceRunMode.NonDestructiveFileWriteRead:
            request.Profile = SurfaceTestProfile.Custom;
            request.Operation = SurfaceTestOperation.WritePattern;
            request.AllowDeviceWrite = true;
            request.MaxBytesToTest = 2L * 1024 * 1024 * 1024;
            request.Drive = new CoreDriveInfo
            {
               Name = selectedDrive.Name,
               FileSystem = selectedDrive.FileSystem,
               FreeSpace = selectedDrive.FreeSpace,
               TotalSize = selectedDrive.TotalSize,
               Path = ResolveSafeTestFilePath(selectedDrive)
            };
            break;
      }

      return request;
   }

   private static string ResolveSafeTestFilePath(CoreDriveInfo drive)
   {
      if(!string.IsNullOrWhiteSpace(drive.Path) && !drive.Path.StartsWith("\\\\.\\", StringComparison.OrdinalIgnoreCase))
      {
         if(Directory.Exists(drive.Path))
         {
            return Path.Combine(drive.Path, "diskchecker_surface_test.bin");
         }

         string? root = Path.GetPathRoot(drive.Path);
         if(!string.IsNullOrWhiteSpace(root) && Directory.Exists(root))
         {
            return Path.Combine(root, "diskchecker_surface_test.bin");
         }
      }

      string tempFolder = Path.Combine(Path.GetTempPath(), "DiskChecker");
      Directory.CreateDirectory(tempFolder);
      string safeName = new string(drive.Name.Where(ch => char.IsLetterOrDigit(ch) || ch == '_').ToArray());
      if(string.IsNullOrWhiteSpace(safeName))
      {
         safeName = "drive";
      }

      return Path.Combine(tempFolder, $"surface_{safeName}.bin");
   }

   /// <summary>
   /// Zastaví probíhající test.
   /// </summary>
   [RelayCommand]
   public void StopTest()
   {
      _testCancellationTokenSource?.Cancel();
   }

   /// <summary>
   /// Vytiskne A4 certifikát posledního kompletního surface testu.
   /// </summary>
   [RelayCommand]
   public void PrintCertificate()
   {
      if(SelectedDrive == null || _lastSurfaceTestResult == null || _lastSmartCheckResult == null)
      {
         StatusMessage = "❌ Certifikát nelze vytisknout. Nejprve dokončete kompletní test.";
         return;
      }

      var document = Services.SurfaceTestCertificateDocumentBuilder.CreateDocument(
          SelectedDrive,
          _lastSurfaceTestResult,
          _lastSmartCheckResult);

      var printDialog = new System.Windows.Controls.PrintDialog();
      if(printDialog.ShowDialog() == true)
      {
         printDialog.PrintDocument(document.DocumentPaginator, "DiskChecker A4 certifikát");
         StatusMessage = "🖨️ Certifikát byl odeslán do tisku.";
      }
   }

   /// <summary>
   /// Zpracuje progress report z testu.
   /// </summary>
   private void OnTestProgress(SurfaceTestProgress progress)
   {
      // DEBUG: Loguj že progress přišel
      System.Diagnostics.Debug.WriteLine($"[Progress] {progress.PercentComplete:F1}% - {progress.BytesProcessed} bytes - {progress.CurrentThroughputMbps:F1} MB/s");
      
      // Lehký handler - jen uloží latest progress do field
      _latestProgress = progress;
      
      // Pokud timer není spuštěn, spusť ho
      if(_uiUpdateTimer != null && !_uiUpdateTimer.IsEnabled)
      {
         System.Diagnostics.Debug.WriteLine("[Timer] Spouštím UI update timer");
         _uiUpdateTimer.Start();
      }
   }

   /// <summary>
   /// Načte referenční průběh z historie testu disku stejné velikosti.
   /// </summary>
   [RelayCommand]
   public async Task LoadReferenceOverlayAsync()
   {
      if(SelectedDrive == null)
      {
         ReferenceOverlayStatus = "Vyberte disk pro načtení referenčního overlay.";
         return;
      }

      if(SelectedGraphOverlayMode == GraphOverlayMode.None)
      {
         _referenceSpeedSamples.Clear();
         ReferenceOverlayStatus = "Overlay vypnut.";
         UpdateSpeedPlot();
         return;
      }

      var candidates = await _historyService.GetForCompareAsync(100);
      var candidate = candidates
         .Where(t => t.TestType.Contains("Surface", StringComparison.OrdinalIgnoreCase) && t.TotalBytesTested > 0)
         .OrderBy(t => Math.Abs(t.TotalBytesTested - SelectedDrive.TotalSize))
         .FirstOrDefault();

      if(candidate == null)
      {
         ReferenceOverlayStatus = "Nenalezen vhodný referenční test v historii.";
         _referenceSpeedSamples.Clear();
         UpdateSpeedPlot();
         return;
      }

      var detailed = await _historyService.GetTestByIdAsync(candidate.TestId);
      var historySamples = detailed?.SurfaceSamples?.OrderBy(s => s.TimestampUtc).ToList();
      if(historySamples == null || historySamples.Count < 2)
      {
         ReferenceOverlayStatus = "Referenční test neobsahuje dostatek vzorků grafu.";
         _referenceSpeedSamples.Clear();
         UpdateSpeedPlot();
         return;
      }

      var start = historySamples[0].TimestampUtc;
      _referenceSpeedSamples.Clear();
      _referenceSpeedSamples.AddRange(historySamples.Select(s => new SpeedSample
      {
         TimeSeconds = (s.TimestampUtc - start).TotalSeconds,
         ThroughputMbps = s.ThroughputMbps,
         Phase = 0
      }));

      ReferenceOverlayStatus = $"✅ Načten referenční test: {candidate.DriveName} ({candidate.TestDate:dd.MM.yyyy HH:mm})";
      UpdateSpeedPlot();
   }

   /// <summary>
   /// Aktualizuje vizualizaci bloků.
   /// </summary>
   private void UpdateBlockVisualization(SurfaceTestProgress progress)
   {
      // Vypočítej kolik bloků by mělo být v jaké fázi
      double blockProgress = Math.Min(progress.PercentComplete / 100.0 * Blocks.Count, Blocks.Count);

      for(int i = 0; i < Blocks.Count; i++)
      {
         if(i < blockProgress * 0.5) // First 50% = writing phase
         {
            Blocks[i].Status = 2; // Write OK (blue)
         }
         else if(i < blockProgress) // Next 50% = reading phase
         {
            Blocks[i].Status = 3; // Read OK (green)
         }
         else if(i == (int)blockProgress)
         {
            Blocks[i].Status = 1; // Currently processing
         }
      }
   }

   /// <summary>
   /// Nastaví vybraný disk pro test.
   /// </summary>
   public void SetSelectedDrive(CoreDriveInfo? drive)
   {
      SelectedDrive = drive;
      if(drive != null)
      {
         SelectedDriveInfo = $"💾 {drive.Name} ({FormatBytes(drive.TotalSize)})";
         TotalBytes = drive.TotalSize;
      }
      else
      {
         SelectedDriveInfo = "Vyber disk pro test";
         TotalBytes = 0;
      }
   }

   /// <summary>
   /// Formátuje počet bajtů na čitelný formát.
   /// </summary>
   private static string FormatBytes(long bytes)
   {
      const long gb = 1024 * 1024 * 1024;
      const long mb = 1024 * 1024;
      const long kb = 1024;

      if(bytes >= gb)
         return $"{bytes / (double)gb:F2} GB";
      if(bytes >= mb)
         return $"{bytes / (double)mb:F2} MB";
      if(bytes >= kb)
         return $"{bytes / (double)kb:F2} KB";
      return $"{bytes} B";
   }

   private PlotModel CreateSpeedPlotModel()
   {
      var model = new PlotModel
      {
         Title = "Rychlost testu (MB/s)"
      };

      model.Axes.Add(new LinearAxis
      {
         Position = AxisPosition.Bottom,
         Title = "Čas (s)",
         Minimum = 0
      });

      model.Axes.Add(new LinearAxis
      {
         Position = AxisPosition.Left,
         Title = "MB/s",
         Minimum = 0
      });

      model.Series.Add(_currentSpeedSeries);
      model.Series.Add(_referenceSpeedSeries);
      return model;
   }

   private IEnumerable<SpeedSample> FilterByTimeRange(IEnumerable<SpeedSample> samples)
   {
      var sampleList = samples.ToList();
      if(sampleList.Count == 0 || SelectedGraphTimeRange == GraphTimeRange.Full)
      {
         return sampleList;
      }

      double maxSeconds = sampleList.Max(s => s.TimeSeconds);
      double windowSeconds = SelectedGraphTimeRange switch
      {
         GraphTimeRange.Last1Minute => 60,
         GraphTimeRange.Last5Minutes => 300,
         GraphTimeRange.Last15Minutes => 900,
         GraphTimeRange.Last30Minutes => 1800,
         _ => double.MaxValue
      };

      return sampleList.Where(s => s.TimeSeconds >= maxSeconds - windowSeconds);
   }

   private void UpdateSpeedPlot()
   {
      _currentSpeedSeries.Points.Clear();
      _referenceSpeedSeries.Points.Clear();

      foreach(var sample in FilterByTimeRange(SpeedSamples))
      {
         _currentSpeedSeries.Points.Add(new DataPoint(sample.TimeSeconds, sample.ThroughputMbps));
      }

      if(SelectedGraphOverlayMode == GraphOverlayMode.SameSizeReference)
      {
         foreach(var sample in FilterByTimeRange(_referenceSpeedSamples))
         {
            _referenceSpeedSeries.Points.Add(new DataPoint(sample.TimeSeconds, sample.ThroughputMbps));
         }
      }

      SpeedPlotModel.InvalidatePlot(true);
   }

   private async Task LoadSmartDataForCertificateAsync()
   {
      if(SelectedDrive == null)
      {
         CanPrintCertificate = false;
         CertificateStatus = "SMART data nejsou dostupná.";
         return;
      }

      _lastSmartCheckResult = await _smartCheckService.RunAsync(SelectedDrive);
      CanPrintCertificate = _lastSmartCheckResult != null && _lastSurfaceTestResult != null;
      CertificateStatus = CanPrintCertificate
          ? "✅ Certifikát připraven k tisku."
          : "⚠️ SMART data nejsou dostupná, certifikát nelze dokončit.";
   }

   private static async Task<bool> IsSystemDiskAsync(CoreDriveInfo drive)
   {
      ArgumentNullException.ThrowIfNull(drive);
      if(!OperatingSystem.IsWindows())
      {
         return false;
      }

      string? systemRoot = Path.GetPathRoot(Environment.SystemDirectory);
      if(string.IsNullOrWhiteSpace(systemRoot) || !systemRoot.EndsWith(":\\", StringComparison.OrdinalIgnoreCase))
      {
         return false;
      }

      if(!TryExtractPhysicalDiskNumber(drive.Path, out int selectedDiskNumber))
      {
         return false;
      }

      char driveLetter = systemRoot[0];
      string command = $"(Get-Partition -DriveLetter '{driveLetter}' | Get-Disk).Number";
      var psi = new ProcessStartInfo
      {
         FileName = "powershell.exe",
         Arguments = $"-NoProfile -Command \"{command}\"",
         RedirectStandardOutput = true,
         RedirectStandardError = true,
         UseShellExecute = false,
         CreateNoWindow = true
      };

      try
      {
         using var process = Process.Start(psi);
         if(process == null)
         {
            return selectedDiskNumber == 0;
         }

         string output = await process.StandardOutput.ReadToEndAsync();
         await process.WaitForExitAsync();

         return int.TryParse(output.Trim(), out int systemDiskNumber)
            ? systemDiskNumber == selectedDiskNumber
            : selectedDiskNumber == 0;
      }
      catch(InvalidOperationException)
      {
         return selectedDiskNumber == 0;
      }
      catch(Win32Exception)
      {
         return selectedDiskNumber == 0;
      }
   }

   private static bool TryExtractPhysicalDiskNumber(string path, out int diskNumber)
   {
      diskNumber = -1;
      if(string.IsNullOrWhiteSpace(path))
      {
         return false;
      }

      string digits = new string(path.Where(char.IsDigit).ToArray());
      return int.TryParse(digits, out diskNumber);
   }

   /// <summary>
   /// Disposes the view model.
   /// </summary>
   public void Dispose()
   {
      _uiUpdateTimer?.Stop();
      _uiUpdateTimer = null;
      _testCancellationTokenSource?.Dispose();
      GC.SuppressFinalize(this);
   }
}

/// <summary>
/// ViewModel pro SMART kontrolu disku.
/// </summary>
public partial class SmartCheckViewModel : ViewModelBase, IDisposable
{
   public class SmartAttributeSortOptionItem
   {
      public SmartAttributeSortOption Value { get; init; }
      public string Label { get; init; } = string.Empty;
   }

   public class SmartAttributeUiItem
   {
      public required SmartaAttributeItem Attribute { get; init; }
      public required string Description { get; init; }
      public bool IsCritical => Attribute.IsCritical;
   }

   public class SelfTestTypeOptionItem
   {
      public SmartaSelfTestType Value { get; init; }
      public string Label { get; init; } = string.Empty;
   }

   public class MaintenanceActionOptionItem
   {
      public SmartaMaintenanceAction Value { get; init; }
      public string Label { get; init; } = string.Empty;
   }

   public enum SmartAttributeSortOption
   {
      ById,
      ByName,
      CriticalFirst,
      RawDescending
   }

   private readonly SmartCheckService _smartCheckService;
   private readonly DiskCheckerService _diskCheckerService;
   private readonly LineSeries _temperatureSeries;
   private Task? _temperatureMonitoringTask;
   private CancellationTokenSource? _temperatureMonitorCts;
   private CancellationTokenSource? _selfTestMonitorCts;

   [ObservableProperty]
   private CoreDriveInfo? selectedDrive;

   [ObservableProperty]
   private ObservableCollection<CoreDriveInfo> availableDrives = [];

   [ObservableProperty]
   private string selectedDriveInfo = "Vyber disk pro SMART kontrolu";

   [ObservableProperty]
   private string smartDataSourceText = "Zdroj dat: SMART";

   [ObservableProperty]
   private string smartDataSourceBadgeBackground = "#005A2B";

   [ObservableProperty]
   private string qualityGrade = "-";

   [ObservableProperty]
   private double qualityScore;

   [ObservableProperty]
   private double temperatureCelsius;

   [ObservableProperty]
   private long reallocatedSectorCount;

   [ObservableProperty]
   private long pendingSectorCount;

   [ObservableProperty]
   private long uncorrectableErrorCount;

   [ObservableProperty]
   private int powerOnHours;

   [ObservableProperty]
   private string warningsSummary = "Žádná varování";

   [ObservableProperty]
   private DateTime? lastCheckDate;

   [ObservableProperty]
   private DateTime? lastTemperatureUpdate;

   [ObservableProperty]
   private PlotModel temperaturePlotModel;

   [ObservableProperty]
   private ObservableCollection<SmartaAttributeItem> smartAttributes = [];

   [ObservableProperty]
   private ObservableCollection<SmartAttributeUiItem> filteredSmartAttributes = [];

   [ObservableProperty]
   private string smartAttributeFilterText = string.Empty;

   [ObservableProperty]
   private SmartAttributeSortOption selectedSmartAttributeSort = SmartAttributeSortOption.ById;

   [ObservableProperty]
   private IReadOnlyList<SmartAttributeSortOptionItem> availableSmartAttributeSortOptions =
   [
      new() { Value = SmartAttributeSortOption.ById, Label = "Řazení: podle ID" },
      new() { Value = SmartAttributeSortOption.ByName, Label = "Řazení: podle názvu" },
      new() { Value = SmartAttributeSortOption.CriticalFirst, Label = "Řazení: kritické nahoře" },
      new() { Value = SmartAttributeSortOption.RawDescending, Label = "Řazení: RAW sestupně" }
   ];

   [ObservableProperty]
   private string smartHealthBadgeText = "Stav SMART: neznámý";

   [ObservableProperty]
   private string smartHealthBadgeBackground = "#FF6C757D";

   [ObservableProperty]
   private ObservableCollection<SmartaSelfTestEntry> selfTestLogEntries = [];

   [ObservableProperty]
   private string selfTestStatusText = "Stav self-testu není načten.";

   [ObservableProperty]
   private string selfTestStatusForeground = "#333333";

   [ObservableProperty]
   private bool isSelfTestRunning;

   [ObservableProperty]
   private double selfTestProgressPercent;

   [ObservableProperty]
   private string selfTestReportText = "Report self-testu zatím není k dispozici.";

   [ObservableProperty]
   private SmartaSelfTestType selectedSelfTestType = SmartaSelfTestType.Quick;

   [ObservableProperty]
   private IReadOnlyList<SelfTestTypeOptionItem> availableSelfTestTypes =
   [
      new() { Value = SmartaSelfTestType.Quick, Label = "Krátký (Quick)" },
      new() { Value = SmartaSelfTestType.Extended, Label = "Rozšířený (Extended)" },
      new() { Value = SmartaSelfTestType.Conveyance, Label = "Přepravní (Conveyance)" },
      new() { Value = SmartaSelfTestType.Selective, Label = "Selektivní" },
      new() { Value = SmartaSelfTestType.Offline, Label = "Offline" },
      new() { Value = SmartaSelfTestType.Abort, Label = "Přerušit běžící test" }
   ];

   [ObservableProperty]
   private bool isSmartScanRunning;

   [ObservableProperty]
   private double smartScanProgressPercent;

   [ObservableProperty]
   private string smartScanProgressText = "Rychlý SMART test není spuštěn.";

   [ObservableProperty]
   private SmartaMaintenanceAction selectedMaintenanceAction = SmartaMaintenanceAction.EnableSmart;

   [ObservableProperty]
   private IReadOnlyList<MaintenanceActionOptionItem> availableMaintenanceActions =
   [
      new() { Value = SmartaMaintenanceAction.EnableSmart, Label = "Zapnout SMART" },
      new() { Value = SmartaMaintenanceAction.DisableSmart, Label = "Vypnout SMART" },
      new() { Value = SmartaMaintenanceAction.EnableAutoSave, Label = "Zapnout SMART AutoSave" },
      new() { Value = SmartaMaintenanceAction.DisableAutoSave, Label = "Vypnout SMART AutoSave" },
      new() { Value = SmartaMaintenanceAction.RunOfflineDataCollection, Label = "Spustit offline data collection" },
      new() { Value = SmartaMaintenanceAction.AbortSelfTest, Label = "Přerušit běžící self-test" }
   ];

   [ObservableProperty]
   private string maintenanceActionStatusText = "Tovární reset disku není přes SMART standardně podporován.";

   /// <summary>
   /// Initializes a new instance of the <see cref="SmartCheckViewModel"/> class.
   /// </summary>
   public SmartCheckViewModel(SmartCheckService smartCheckService, DiskCheckerService diskCheckerService)
   {
      _smartCheckService = smartCheckService;
      _diskCheckerService = diskCheckerService;
      _temperatureSeries = new LineSeries
      {
         Title = "Teplota",
         Color = OxyColors.OrangeRed,
         StrokeThickness = 2
      };
      TemperaturePlotModel = CreateTemperaturePlotModel();
      StatusMessage = "SMART kontrola připravena.";
   }

   /// <summary>
   /// Spustí SMART kontrolu vybraného disku.
   /// </summary>
   [RelayCommand]
   public async Task RunSmartCheckAsync()
   {
      if(SelectedDrive == null)
      {
         StatusMessage = "❌ Prosím vyber disk před spuštěním SMART kontroly.";
         return;
      }

      IsBusy = true;
      IsSmartScanRunning = true;
      SmartScanProgressPercent = 10;
      SmartScanProgressText = "Inicializace rychlého SMART testu...";
      StatusMessage = $"🔍 Načítám SMART data pro {SelectedDrive.Name}...";

      try
      {
         SmartScanProgressPercent = 40;
         SmartScanProgressText = "Načítání SMART atributů...";
         using var quickSmartCts = new CancellationTokenSource(TimeSpan.FromSeconds(20));
         var result = await _smartCheckService.RunAsync(SelectedDrive, quickSmartCts.Token);
         if(result == null)
         {
            string? instructions = await _smartCheckService.GetDependencyInstructionsAsync();
            SmartDataSourceText = "Zdroj dat: OS fallback (SMART nedostupné)";
            SmartDataSourceBadgeBackground = "#B35A00";
            StatusMessage = string.IsNullOrWhiteSpace(instructions)
                ? "SMART data nejsou dostupná pro vybraný disk."
                : instructions;
            return;
         }

         SmartScanProgressPercent = 70;
         SmartScanProgressText = "Vyhodnocuji výsledky testu...";
         MapResult(result);
         using var advancedCts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
         await LoadAdvancedSmartDataAsync(advancedCts.Token);
         await RefreshMaintenanceActionsAsync(advancedCts.Token);
         if(_temperatureMonitoringTask == null || _temperatureMonitoringTask.IsCompleted)
         {
            _temperatureMonitoringTask = StartTemperatureMonitoringAsync();
         }
         SmartScanProgressPercent = 100;
         SmartScanProgressText = "✅ Rychlý SMART test dokončen.";
         StatusMessage = $"✅ SMART kontrola dokončena: známka {result.Rating.Grade}, skóre {result.Rating.Score:F1}.";
      }
      catch(InvalidOperationException ex)
      {
         StatusMessage = $"❌ SMART kontrola selhala: {ex.Message}";
         SmartScanProgressText = "❌ SMART test selhal.";
      }
      catch(DbUpdateException ex)
      {
         StatusMessage = $"❌ SMART kontrola selhala při zápisu do DB: {ex.Message}";
         SmartScanProgressText = "❌ SMART test selhal (DB).";
      }
      catch(SqliteException ex)
      {
         StatusMessage = $"❌ SMART kontrola selhala kvůli schématu databáze: {ex.Message}";
         SmartScanProgressText = "❌ SMART test selhal (schema DB).";
      }
      catch(OperationCanceledException)
      {
         StatusMessage = "⚠️ SMART kontrola vypršela časově (timeout). Část detailů může chybět.";
         SmartScanProgressText = "⚠️ SMART test dokončen s timeoutem.";
      }
      finally
      {
         IsSmartScanRunning = false;
         IsBusy = false;
      }

      SmartDataSourceText = "Zdroj dat: SMART";
      SmartDataSourceBadgeBackground = "#005A2B";
   }

   /// <summary>
   /// Spustí vybranou SMART servisní akci (pokud je podporovaná providerem).
   /// </summary>
   [RelayCommand]
   public async Task ExecuteMaintenanceActionAsync()
   {
      if(SelectedDrive == null)
      {
         MaintenanceActionStatusText = "Vyberte disk pro SMART servisní akci.";
         return;
      }

      bool success = await _smartCheckService.ExecuteMaintenanceActionAsync(SelectedDrive, SelectedMaintenanceAction);
      MaintenanceActionStatusText = success
         ? $"✅ SMART akce provedena: {SelectedMaintenanceAction}."
         : $"⚠️ SMART akci nelze provést: {SelectedMaintenanceAction}.";
   }

   /// <summary>
   /// Nastaví vybraný disk pro SMART kontrolu.
   /// </summary>
   /// <param name="drive">Vybraný disk.</param>
   public void SetSelectedDrive(CoreDriveInfo? drive)
   {
      SelectedDrive = drive;
   }

   partial void OnSelectedDriveChanged(CoreDriveInfo? value)
   {
      SelectedDriveInfo = value == null
          ? "Vyber disk pro SMART kontrolu"
          : $"💾 {value.Name} ({NormalizeDrivePath(value.Path)})";

      // Automaticky načti základní SMART data při změně disku
      if(value != null)
      {
         _ = LoadInitialSmartDataAsync();
      }
   }

   /// <summary>
   /// Initializes the view model asynchronously.
   /// </summary>
   public override async Task InitializeAsync()
   {
      await ReloadAvailableDrivesAsync();
      StatusMessage = SelectedDrive == null
          ? "Vyber disk v přehledu disků a spusť SMART kontrolu."
          : $"Připraveno pro SMART kontrolu: {SelectedDrive.Name}";

      await RefreshMaintenanceActionsAsync();

      return;
   }

   [RelayCommand]
   public async Task ReloadAvailableDrivesAsync()
   {
      var drives = await _diskCheckerService.ListDrivesAsync();
      AvailableDrives = new ObservableCollection<CoreDriveInfo>(drives);

      if(SelectedDrive == null || !AvailableDrives.Any(d => d.Path == SelectedDrive.Path))
      {
         SelectedDrive = AvailableDrives.FirstOrDefault();
      }
   }

   /// <summary>
   /// Spustí periodické načítání teploty pro graf historie.
   /// </summary>
   [RelayCommand]
   public async Task StartTemperatureMonitoringAsync()
   {
      if(SelectedDrive == null)
      {
         StatusMessage = "❌ Pro sledování teploty je potřeba vybraný disk.";
         return;
      }

      _temperatureMonitorCts?.Cancel();
      _temperatureMonitorCts = new CancellationTokenSource();
      var token = _temperatureMonitorCts.Token;

      try
      {
         while(!token.IsCancellationRequested)
         {
            int? temperature = await _smartCheckService.GetTemperatureOnlyAsync(SelectedDrive, token);
            if(temperature.HasValue)
            {
               AddTemperatureSample(temperature.Value);
            }

            await RefreshSelfTestStatusAsync(token);
            await Task.Delay(TimeSpan.FromSeconds(5), token);
         }
      }
      catch(OperationCanceledException)
      {
      }
   }

   /// <summary>
   /// Zastaví periodické načítání teploty.
   /// </summary>
   [RelayCommand]
   public void StopTemperatureMonitoring()
   {
      _temperatureMonitorCts?.Cancel();
   }

   /// <summary>
   /// Spustí SMART self-test na vybraném disku.
   /// </summary>
   [RelayCommand]
   public async Task StartSelfTestAsync()
   {
      if(SelectedDrive == null)
      {
         StatusMessage = "❌ Pro spuštění self-testu je potřeba vybraný disk.";
         return;
      }

      IsBusy = true;
      var startedAt = DateTime.UtcNow;
      bool started = await _smartCheckService.StartSelfTestAsync(SelectedDrive, SelectedSelfTestType);
      if(started)
      {
         StatusMessage = $"✅ SMART self-test ({SelectedSelfTestType}) byl spuštěn.";
         await MonitorSelfTestProgressAsync(startedAt);
      }
      else
      {
         StatusMessage = "⚠️ SMART self-test se nepodařilo spustit. Zkontroluj podporu disku/ovladače.";
      }
      IsBusy = false;
   }

   /// <summary>
   /// Načte aktuální stav SMART self-testu.
   /// </summary>
   [RelayCommand]
   public async Task RefreshSelfTestStatusAsync(CancellationToken cancellationToken = default)
   {
      if(SelectedDrive == null)
      {
         return;
      }

      var status = await _smartCheckService.GetSelfTestStatusAsync(SelectedDrive, cancellationToken);
      if(status == null)
      {
         SelfTestStatusText = "Stav self-testu není dostupný.";
         IsSelfTestRunning = false;
         return;
      }

      IsSelfTestRunning = status.IsRunning;
      var translatedStatus = TranslateSelfTestStatus(status.StatusText);
      SelfTestStatusText = status.RemainingPercent.HasValue
          ? $"{translatedStatus} (zbývá {status.RemainingPercent.Value} %)"
          : translatedStatus;
   }

   /// <summary>
   /// Načte log SMART self-testů.
   /// </summary>
   [RelayCommand]
   public async Task RefreshSelfTestLogAsync(CancellationToken cancellationToken = default)
   {
      if(SelectedDrive == null)
      {
         return;
      }

      var logEntries = await _smartCheckService.GetSelfTestLogAsync(SelectedDrive, cancellationToken);
      SelfTestLogEntries = new ObservableCollection<SmartaSelfTestEntry>(logEntries.OrderByDescending(e => e.Number));
   }

   /// <summary>
   /// Cleans up resources.
   /// </summary>
   public override void Cleanup()
   {
      _temperatureMonitorCts?.Cancel();
      _temperatureMonitorCts?.Dispose();
      _temperatureMonitorCts = null;
      _selfTestMonitorCts?.Cancel();
      _selfTestMonitorCts?.Dispose();
      _selfTestMonitorCts = null;
      base.Cleanup();
   }

   /// <summary>
   /// Disposes the view model resources.
   /// </summary>
   public void Dispose()
   {
      _temperatureMonitorCts?.Dispose();
      _temperatureMonitorCts = null;
      _selfTestMonitorCts?.Dispose();
      _selfTestMonitorCts = null;
      GC.SuppressFinalize(this);
   }

   private async Task MonitorSelfTestProgressAsync(DateTime startedAtUtc)
   {
      if(SelectedDrive == null)
      {
         return;
      }

      _selfTestMonitorCts?.Cancel();
      _selfTestMonitorCts = new CancellationTokenSource();
      var token = _selfTestMonitorCts.Token;

      try
      {
         // Měření teploty PŘED testem
         var tempBefore = await _smartCheckService.GetTemperatureOnlyAsync(SelectedDrive, token);
         if(tempBefore.HasValue)
         {
            AddTemperatureSample(tempBefore.Value);
            StatusMessage = $"🌡️ Teplota před testem: {tempBefore.Value:F1} °C";
         }

         while(!token.IsCancellationRequested)
         {
            var status = await _smartCheckService.GetSelfTestStatusAsync(SelectedDrive, token);
            if(status == null)
            {
               SelfTestStatusText = "Stav self-testu není dostupný.";
               break;
            }

            IsSelfTestRunning = status.IsRunning;
            SelfTestProgressPercent = status.RemainingPercent.HasValue
               ? Math.Max(0, 100 - status.RemainingPercent.Value)
               : (status.IsRunning ? SelfTestProgressPercent : 100);

            var translatedStatus = TranslateSelfTestStatus(status.StatusText);
            SelfTestStatusText = status.RemainingPercent.HasValue
               ? $"{translatedStatus} (zbývá {status.RemainingPercent.Value} %)"
               : translatedStatus;

            // Měření teploty BĚHEM testu (každých 10s)
            var tempDuring = await _smartCheckService.GetTemperatureOnlyAsync(SelectedDrive, token);
            if(tempDuring.HasValue)
            {
               AddTemperatureSample(tempDuring.Value);
            }

            await RefreshSelfTestLogAsync(token);

            if(!status.IsRunning)
            {
               break;
            }

            await Task.Delay(TimeSpan.FromSeconds(10), token);
         }

         // Test dokončen - načti aktualizovaná data a zobraz report
         StatusMessage = "✅ SMART self-test dokončen, načítám výsledky...";
         
         // Měření teploty PO testu
         var tempAfter = await _smartCheckService.GetTemperatureOnlyAsync(SelectedDrive, token);
         if(tempAfter.HasValue)
         {
            AddTemperatureSample(tempAfter.Value);
         }

         // Načti kompletní SMART data po testu
         var smartResult = await _smartCheckService.RunAsync(SelectedDrive, token);
         if(smartResult != null)
         {
            MapResult(smartResult);
         }

         // Načti finální self-test log
         await RefreshSelfTestLogAsync(token);

         // Vytvoř a zobraz report
         var report = await _smartCheckService.BuildSelfTestReportAsync(SelectedDrive, SelectedSelfTestType, startedAtUtc, token);
         var translatedTestType = TranslateTestTypeName(SelectedSelfTestType);
         var translatedSummary = TranslateSelfTestStatus(report.Summary);
         var duration = report.FinishedAtUtc - report.StartedAtUtc;
         
         SelfTestReportText = $"✅ {translatedTestType} dokončen!\n" +
            $"Trvání: {duration.TotalMinutes:F1} min\n" +
            $"Výsledek: {translatedSummary}\n" +
            $"Stav: {(report.Passed ? "✅ Test prošel" : "⚠️ Dokončeno s upozorněním")}";
         
         SelfTestProgressPercent = 100;
         StatusMessage = report.Passed 
            ? $"✅ {translatedTestType} úspěšně dokončen! ({duration.TotalMinutes:F1} min)" 
            : $"⚠️ {translatedTestType} dokončen s upozorněním ({duration.TotalMinutes:F1} min)";
      }
      catch(OperationCanceledException)
      {
         StatusMessage = "⚠️ Monitorování self-testu bylo přerušeno.";
         SelfTestStatusText = "Test byl přerušen.";
      }
      catch(Exception ex)
      {
         StatusMessage = $"❌ Chyba během self-testu: {ex.Message}";
         SelfTestStatusText = "Chyba během testu.";
         System.Diagnostics.Debug.WriteLine($"MonitorSelfTestProgressAsync error: {ex.Message}");
      }
      finally
      {
         IsSelfTestRunning = false;
      }
   }


   private async Task LoadSmartDataAfterSelfTestAsync(DateTime startedAtUtc, CancellationToken cancellationToken)
   {
      if(SelectedDrive == null)
      {
         return;
      }

      try
      {
         using var smartCts = new CancellationTokenSource(TimeSpan.FromSeconds(15));
         var result = await _smartCheckService.RunAsync(SelectedDrive, smartCts.Token);

         if(result != null)
         {
            MapResult(result);
            StatusMessage = $"✅ SMART kontrola dokončena: známka {result.Rating.Grade}, skóre {result.Rating.Score:F1}";
         }

         var report = await _smartCheckService.BuildSelfTestReportAsync(
            SelectedDrive,
            SelectedSelfTestType,
            startedAtUtc,
            cancellationToken);

         var duration = report.FinishedAtUtc - report.StartedAtUtc;
         string resultIcon = report.Passed ? "✅" : "❌";
         string resultText = report.Passed ? "ÚSPĚŠNÝ" : "NEÚSPĚŠNÝ";

         SelfTestReportText = $"{resultIcon} Self-Test: {resultText}\n\n" +
            $"Typ: {SelectedSelfTestType}\n" +
            $"Trvání: {duration.TotalMinutes:F1} min\n" +
            $"Výsledek: {TranslateSelfTestStatus(report.Summary)}";
      }
      catch(Exception ex)
      {
         System.Diagnostics.Debug.WriteLine($"LoadSmartDataAfterSelfTestAsync error: {ex.Message}");
         StatusMessage = $"⚠️ Chyba při načítání dat po testu: {ex.Message}";
      }
   }

   private static string TranslateSelfTestStatus(string englishStatus)
   {
      if(string.IsNullOrWhiteSpace(englishStatus))
      {
         return "Neznámý stav";
      }

      return englishStatus.ToLowerInvariant() switch
      {
         var s when s.Contains("completed without error") => "✅ Dokončeno bez chyb",
         var s when s.Contains("aborted by host") => "⚠️ Přerušeno systémem",
         var s when s.Contains("interrupted") => "⚠️ Přerušeno",
         var s when s.Contains("fatal error") => "❌ Kritická chyba",
         var s when s.Contains("unknown error") => "❌ Neznámá chyba",
         var s when s.Contains("electrical error") => "❌ Elektrická chyba",
         var s when s.Contains("servo error") => "❌ Servo chyba",
         var s when s.Contains("read error") => "❌ Chyba čtení",
         var s when s.Contains("handling damage") => "❌ Mechanické poškození",
         var s when s.Contains("in progress") => "⏳ Probíhá test...",
         var s when s.Contains("reserved") => "🔒 Vyhrazeno",
         _ => englishStatus
      };
   }

   private static string TranslateTestTypeName(SmartaSelfTestType testType)
   {
      return testType switch
      {
         SmartaSelfTestType.Quick => "Krátký test",
         SmartaSelfTestType.Extended => "Rozšířený test",
         SmartaSelfTestType.Conveyance => "Přepravní test",
         SmartaSelfTestType.Selective => "Selektivní test",
         SmartaSelfTestType.Offline => "Offline test",
         SmartaSelfTestType.Abort => "Přerušení testu",
         _ => testType.ToString()
      };
   }

   private void MapResult(SmartCheckResult result)
   {
      QualityGrade = result.Rating.Grade.ToString();
      QualityScore = result.Rating.Score;
      TemperatureCelsius = result.SmartaData.Temperature;
      ReallocatedSectorCount = result.SmartaData.ReallocatedSectorCount;
      PendingSectorCount = result.SmartaData.PendingSectorCount;
      UncorrectableErrorCount = result.SmartaData.UncorrectableErrorCount;
      PowerOnHours = result.SmartaData.PowerOnHours;
      LastCheckDate = result.TestDate;
      WarningsSummary = result.Rating.Warnings.Count == 0
          ? "Žádná varování"
          : string.Join(Environment.NewLine, result.Rating.Warnings);

      SmartAttributes = new ObservableCollection<SmartaAttributeItem>(result.Attributes.OrderBy(a => a.Id));
      SelfTestLogEntries = new ObservableCollection<SmartaSelfTestEntry>(result.SelfTestLog.OrderByDescending(e => e.Number));
      if(result.SelfTestStatus != null)
      {
         IsSelfTestRunning = result.SelfTestStatus.IsRunning;
         var translatedStatus = TranslateSelfTestStatus(result.SelfTestStatus.StatusText);
         SelfTestStatusText = result.SelfTestStatus.RemainingPercent.HasValue
             ? $"{translatedStatus} (zbývá {result.SelfTestStatus.RemainingPercent.Value} %)"
             : translatedStatus;
      }
   }

   private async Task LoadAdvancedSmartDataAsync(CancellationToken cancellationToken = default)
   {
      if(SelectedDrive == null)
      {
         return;
      }

      SmartAttributes = new ObservableCollection<SmartaAttributeItem>(
          (await _smartCheckService.GetSmartAttributesAsync(SelectedDrive, cancellationToken)).OrderBy(a => a.Id));
      await RefreshSelfTestStatusAsync(cancellationToken);
      await RefreshSelfTestLogAsync(cancellationToken);
   }

   private async Task RefreshMaintenanceActionsAsync(CancellationToken cancellationToken = default)
   {
      if(SelectedDrive == null)
      {
         return;
      }

      var supported = await _smartCheckService.GetSupportedMaintenanceActionsAsync(SelectedDrive, cancellationToken);
      if(supported.Count == 0)
      {
         return;
      }

      var labels = AvailableMaintenanceActions.ToDictionary(a => a.Value, a => a.Label);
      AvailableMaintenanceActions = supported.Select(a => new MaintenanceActionOptionItem
      {
         Value = a,
         Label = labels.TryGetValue(a, out string? label) ? label : a.ToString()
      }).ToList();
      SelectedMaintenanceAction = AvailableMaintenanceActions[0].Value;
   }

   partial void OnSmartAttributesChanged(ObservableCollection<SmartaAttributeItem> value)
   {
      RefreshSmartAttributesView();
   }

   partial void OnSmartAttributeFilterTextChanged(string value)
   {
      RefreshSmartAttributesView();
   }

   partial void OnSelectedSmartAttributeSortChanged(SmartAttributeSortOption value)
   {
      RefreshSmartAttributesView();
   }

   partial void OnQualityGradeChanged(string value)
   {
      UpdateSmartHealthBadge();
   }

   private void RefreshSmartAttributesView()
   {
      IEnumerable<SmartaAttributeItem> query = SmartAttributes;

      if(!string.IsNullOrWhiteSpace(SmartAttributeFilterText))
      {
         string filter = SmartAttributeFilterText.Trim();
         query = query.Where(a => a.Name.Contains(filter, StringComparison.OrdinalIgnoreCase)
             || a.Id.ToString().Contains(filter, StringComparison.OrdinalIgnoreCase));
      }

      query = SelectedSmartAttributeSort switch
      {
         SmartAttributeSortOption.ByName => query.OrderBy(a => a.Name),
         SmartAttributeSortOption.CriticalFirst => query.OrderByDescending(a => a.IsCritical).ThenBy(a => a.Id),
         SmartAttributeSortOption.RawDescending => query.OrderByDescending(a => a.RawValue).ThenBy(a => a.Id),
         _ => query.OrderBy(a => a.Id)
      };

      FilteredSmartAttributes = new ObservableCollection<SmartAttributeUiItem>(query.Select(a => new SmartAttributeUiItem
      {
         Attribute = a,
         Description = GetSmartAttributeDescription(a)
      }));
   }

   private static string GetSmartAttributeDescription(SmartaAttributeItem attribute)
   {
      return attribute.Id switch
      {
         5 => "Přemapované sektory. Nenulová hodnota znamená fyzické poškození povrchu.",
         9 => "Počet provozních hodin disku.",
         190 or 194 => "Aktuální teplota disku.",
         197 => "Nestabilní sektory čekající na přemapování.",
         198 => "Neopravitelné chyby čtení/zápisu.",
         199 => "Chyby přenosu po rozhraní (kabel, konektor, řadič).",
         9001 => "NVMe teplota.",
         9002 => "NVMe provozní hodiny.",
         9003 => "NVMe media errors.",
         9004 => "NVMe opotřebení v procentech.",
         9005 => "NVMe jednotky přečtených dat.",
         9006 => "NVMe jednotky zapsaných dat.",
         _ => "SMART atribut bez specifického popisu v aplikaci."
      };
   }

   private void UpdateSmartHealthBadge()
   {
      switch(QualityGrade)
      {
         case "A":
         case "B":
            SmartHealthBadgeText = $"Stav SMART: výborný ({QualityGrade})";
            SmartHealthBadgeBackground = "#FF28A745";
            break;
         case "C":
            SmartHealthBadgeText = $"Stav SMART: pozor ({QualityGrade})";
            SmartHealthBadgeBackground = "#FFFFC107";
            break;
         default:
            SmartHealthBadgeText = $"Stav SMART: rizikový ({QualityGrade})";
            SmartHealthBadgeBackground = "#FFDC3545";
            break;
      }
   }

   private PlotModel CreateTemperaturePlotModel()
   {
      var model = new PlotModel
      {
         Title = "Historie teploty"
      };

      model.Axes.Add(new DateTimeAxis
      {
         Position = AxisPosition.Bottom,
         StringFormat = "HH:mm:ss",
         Title = "Čas"
      });

      model.Axes.Add(new LinearAxis
      {
         Position = AxisPosition.Left,
         Title = "Teplota (°C)",
         Minimum = 0
      });

      model.Series.Add(_temperatureSeries);
      return model;
   }

   private void AddTemperatureSample(double temperature)
   {
      LastTemperatureUpdate = DateTime.Now;
      _temperatureSeries.Points.Add(new DataPoint(DateTimeAxis.ToDouble(DateTime.Now), temperature));

      while(_temperatureSeries.Points.Count > 120)
      {
         _temperatureSeries.Points.RemoveAt(0);
      }

      TemperaturePlotModel.InvalidatePlot(true);
   }

   private static string NormalizeDrivePath(string path)
   {
      if(string.IsNullOrWhiteSpace(path))
      {
         return "N/A";
      }

      if(path.StartsWith("\\\\.\\PhysicalDrive", StringComparison.OrdinalIgnoreCase))
      {
         string driveNumber = path.Replace("\\\\.\\PhysicalDrive", string.Empty, StringComparison.OrdinalIgnoreCase);
         return $"Fyzický disk {driveNumber}";
      }

      return path.Replace("\\\\.\\", string.Empty, StringComparison.OrdinalIgnoreCase);
   }
}

/// <summary>
/// ViewModel pro zobrazení reportu.
/// </summary>
public class ReportSummaryItem
{
   public DateTime TestDate { get; set; }
   public string DriveName { get; set; } = string.Empty;
   public string TestType { get; set; } = string.Empty;
   public string Grade { get; set; } = string.Empty;
   public double Score { get; set; }
   public int ErrorCount { get; set; }
}

/// <summary>
/// ViewModel pro zobrazení reportu.
/// </summary>
public partial class ReportViewModel : ViewModelBase
{
   private readonly HistoryService _historyService;

   [ObservableProperty]
   private int totalTests;

   [ObservableProperty]
   private int smartCheckTests;

   [ObservableProperty]
   private int surfaceTests;

   [ObservableProperty]
   private double averageScore;

   [ObservableProperty]
   private string bestGrade = "-";

   [ObservableProperty]
   private string? lastDriveName;

   [ObservableProperty]
   private DateTime? lastTestDate;

   [ObservableProperty]
   private ObservableCollection<ReportSummaryItem> recentTests = [];

   /// <summary>
   /// Initializes a new instance of the <see cref="ReportViewModel"/> class.
   /// </summary>
   public ReportViewModel(HistoryService historyService)
   {
      _historyService = historyService;
      StatusMessage = "Přehled reportů připraven.";
   }

   /// <summary>
   /// Načte souhrn reportů z historie testů.
   /// </summary>
   [RelayCommand]
   public async Task RefreshReportAsync()
   {
      IsBusy = true;
      StatusMessage = "📄 Načítám souhrn reportů...";

      var history = await _historyService.GetForCompareAsync(20);

      TotalTests = history.Count;
      SmartCheckTests = history.Count(i => i.TestType.Contains("Smart", StringComparison.OrdinalIgnoreCase));
      SurfaceTests = history.Count(i => i.TestType.Contains("Surface", StringComparison.OrdinalIgnoreCase));
      AverageScore = history.Count == 0 ? 0 : history.Average(i => i.Score);

      if(history.Count > 0)
      {
         var last = history.OrderByDescending(i => i.TestDate).First();
         LastDriveName = last.DriveName;
         LastTestDate = last.TestDate;
         BestGrade = history.Min(i => i.Grade).ToString();
      }
      else
      {
         LastDriveName = null;
         LastTestDate = null;
         BestGrade = "-";
      }

      RecentTests = new ObservableCollection<ReportSummaryItem>(history
          .OrderByDescending(i => i.TestDate)
          .Take(10)
          .Select(i => new ReportSummaryItem
          {
             TestDate = i.TestDate,
             DriveName = i.DriveName,
             TestType = i.TestType,
             Grade = i.Grade.ToString(),
             Score = i.Score,
             ErrorCount = i.ErrorCount
          }));

      StatusMessage = history.Count == 0
          ? "Zatím nejsou k dispozici žádné reporty."
          : $"✅ Načteno {history.Count} testů do reportu.";
      IsBusy = false;
   }

   /// <summary>
   /// Initializes the view model asynchronously.
   /// </summary>
   public override async Task InitializeAsync()
   {
      await RefreshReportAsync();
   }
}

/// <summary>
/// ViewModel pro historii testů.
/// </summary>
public class HistoryListItem
{
   public Guid TestId { get; set; }
   public DateTime TestDate { get; set; }
   public string DriveName { get; set; } = string.Empty;
   public string TestType { get; set; } = string.Empty;
   public string Grade { get; set; } = string.Empty;
   public double Score { get; set; }
   public int ErrorCount { get; set; }
}

/// <summary>
/// ViewModel pro historii testů.
/// </summary>
public partial class HistoryViewModel : ViewModelBase
{
   private readonly HistoryService _historyService;

   [ObservableProperty]
   private ObservableCollection<HistoryListItem> historyItems = [];

   [ObservableProperty]
   private HistoryListItem? selectedItem;

   [ObservableProperty]
   private int totalItems;

   /// <summary>
   /// Initializes a new instance of the <see cref="HistoryViewModel"/> class.
   /// </summary>
   public HistoryViewModel(HistoryService historyService)
   {
      _historyService = historyService;
      StatusMessage = "Historie testů připravena.";
   }

   /// <summary>
   /// Načte historii testů.
   /// </summary>
   [RelayCommand]
   public async Task RefreshHistoryAsync()
   {
      IsBusy = true;
      StatusMessage = "📚 Načítám historii testů...";

      var page = await _historyService.GetHistoryAsync(pageSize: 100, pageIndex: 0);

      HistoryItems = new ObservableCollection<HistoryListItem>(page.Items.Select(i => new HistoryListItem
      {
         TestId = i.TestId,
         TestDate = i.TestDate,
         DriveName = i.DriveName,
         TestType = i.TestType,
         Grade = i.Grade.ToString(),
         Score = i.Score,
         ErrorCount = i.ErrorCount
      }));

      TotalItems = page.TotalItems;
      StatusMessage = page.TotalItems == 0
          ? "Historie je zatím prázdná."
          : $"✅ Načteno {HistoryItems.Count} položek historie.";
      IsBusy = false;
   }

   /// <summary>
   /// Initializes the view model asynchronously.
   /// </summary>
   public override async Task InitializeAsync()
   {
      await RefreshHistoryAsync();
   }
}

/// <summary>
/// ViewModel pro nastavení aplikace.
/// </summary>
public partial class SettingsViewModel : ViewModelBase
{
   private readonly HistoryService _historyService;
   private readonly DatabaseMaintenanceService _maintenanceService;
   private readonly DiskHistoryArchiveService _archiveService;

   private ObservableCollection<DriveCompareItem> _drives = [];
   private DriveCompareItem? _selectedDrive;
   private int _totalHistoryItems;
   private string _archivePath = string.Empty;
   private bool _isDryRun = true;

   /// <summary>
   /// Initializes a new instance of the <see cref="SettingsViewModel"/> class.
   /// </summary>
   public SettingsViewModel(
      HistoryService historyService,
      DatabaseMaintenanceService maintenanceService,
      DiskHistoryArchiveService archiveService)
   {
      _historyService = historyService;
      _maintenanceService = maintenanceService;
      _archiveService = archiveService;
      StatusMessage = "Správa databáze připravena.";
   }

   public ObservableCollection<DriveCompareItem> Drives
   {
      get => _drives;
      private set => SetProperty(ref _drives, value);
   }

   public DriveCompareItem? SelectedDrive
   {
      get => _selectedDrive;
      set => SetProperty(ref _selectedDrive, value);
   }

   public int TotalHistoryItems
   {
      get => _totalHistoryItems;
      private set => SetProperty(ref _totalHistoryItems, value);
   }

   public string ArchivePath
   {
      get => _archivePath;
      set => SetProperty(ref _archivePath, value);
   }

   public bool IsDryRun
   {
      get => _isDryRun;
      set => SetProperty(ref _isDryRun, value);
   }

   public override async Task InitializeAsync()
   {
      await RefreshDatabaseOverviewAsync();
   }

   /// <summary>
   /// Refreshes DB overview metrics and drive list.
   /// </summary>
   [RelayCommand]
   public async Task RefreshDatabaseOverviewAsync()
   {
      IsBusy = true;
      var paged = await _historyService.GetHistoryAsync(pageSize: 1, pageIndex: 0);
      TotalHistoryItems = paged.TotalItems;

      var drives = await _historyService.GetDrivesWithTestsAsync();
      Drives = new ObservableCollection<DriveCompareItem>(drives.OrderByDescending(d => d.LastTestDate));
      StatusMessage = $"Načteno {Drives.Count} disků s historií a {TotalHistoryItems} aktivních testů.";
      IsBusy = false;
   }

   /// <summary>
   /// Deletes invalid and duplicate tests from DB.
   /// </summary>
   [RelayCommand]
   public async Task CleanupDatabaseAsync()
   {
      IsBusy = true;

      if(IsDryRun)
      {
         var preview = await _maintenanceService.PreviewCleanupAsync();
         StatusMessage = $"Dry-run: neplatné {preview.InvalidTests}, duplicity {preview.DuplicateTests}, celkem ke smazání {preview.TotalToRemove}.";
         IsBusy = false;
         return;
      }

      int invalid = await _maintenanceService.DeleteInvalidAndIncompleteTestsAsync();
      int duplicates = await _maintenanceService.RemoveDuplicateTestsAsync();
      await RefreshDatabaseOverviewAsync();
      StatusMessage = $"Údržba DB dokončena. Smazáno neplatných: {invalid}, duplicit: {duplicates}.";
      IsBusy = false;
   }

   /// <summary>
   /// Archives selected drive history to ZIP while preserving latest measurement.
   /// </summary>
   [RelayCommand]
   public async Task ArchiveSelectedDriveAsync()
   {
      if(SelectedDrive == null)
      {
         StatusMessage = "Vyberte disk k archivaci.";
         return;
      }

      IsBusy = true;
      string folder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "DiskCheckerArchives");
      Directory.CreateDirectory(folder);
      string safeName = string.IsNullOrWhiteSpace(SelectedDrive.DriveName)
          ? SelectedDrive.DriveId.ToString()
          : SelectedDrive.DriveName.Replace(' ', '_');
      string targetZip = Path.Combine(folder, $"{safeName}_{DateTime.Now:yyyyMMdd_HHmmss}.zip");

      int archivedCount = await _archiveService.ArchiveDriveHistoryAsync(SelectedDrive.DriveId, targetZip);
      ArchivePath = targetZip;
      await RefreshDatabaseOverviewAsync();
      StatusMessage = archivedCount == 0
          ? "Archivace neproběhla (disk má pouze poslední měření)."
          : $"Archivováno {archivedCount} testů do {targetZip}";
      IsBusy = false;
   }

   /// <summary>
   /// Imports archive from ZIP into active DB.
   /// </summary>
   [RelayCommand]
   public async Task ImportArchiveAsync()
   {
      string path = ArchivePath;
      if(string.IsNullOrWhiteSpace(path))
      {
         var dialog = new OpenFileDialog
         {
            Filter = "DiskChecker archiv (*.zip)|*.zip",
            Title = "Vyberte archiv historie disku"
         };

         if(dialog.ShowDialog() != true)
         {
            return;
         }

         path = dialog.FileName;
         ArchivePath = path;
      }

      IsBusy = true;
      int imported = await _archiveService.ImportDriveHistoryArchiveAsync(path);
      await RefreshDatabaseOverviewAsync();
      StatusMessage = $"Import dokončen. Načteno {imported} testů ze souboru {Path.GetFileName(path)}.";
      IsBusy = false;
   }
}

