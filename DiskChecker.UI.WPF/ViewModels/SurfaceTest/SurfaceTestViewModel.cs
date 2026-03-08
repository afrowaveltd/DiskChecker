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
   private readonly LinearAxis _speedAxis;
   private readonly List<SpeedSample> _referenceSpeedSamples = [];
   private CancellationTokenSource? _testCancellationTokenSource;
   private DateTime _testStartTime;
   private SurfaceTestResult? _lastSurfaceTestResult;
   private SmartCheckResult? _lastSmartCheckResult;
   private int _activeBlockCount;

   private const int VisualGridRowCount = 10;
   private const int VisualGridColumnCount = 100;
   private const int TotalVisualBlocks = VisualGridRowCount * VisualGridColumnCount;
   private long _bytesPerVisualBlock = 1024L * 1024L * 1024L; // Default 1GB

   public long BytesPerVisualBlock
   {
      get => _bytesPerVisualBlock;
      set
      {
         if (_bytesPerVisualBlock != value)
         {
            _bytesPerVisualBlock = value;
            OnPropertyChanged(nameof(BytesPerVisualBlock));
         }
      }
   }

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

   [ObservableProperty]
   private long writeBytesProcessed;

   [ObservableProperty]
   private long readBytesProcessed;

   [ObservableProperty]
   private double currentWriteThroughputMbps;

   [ObservableProperty]
   private double currentReadThroughputMbps;

   [ObservableProperty]
   private double gaugeMaxMbps = 500;

   [ObservableProperty]
   private double writeNeedleAngle = -95;

   [ObservableProperty]
   private double readNeedleAngle = -95;

   [ObservableProperty]
   private int activeBlockCount;

   [ObservableProperty]
   private long bytesPerVisualizedBlock = 1024L * 1024L; // Will be updated based on disk size

   [ObservableProperty]
   private string writeSpeedStats = "Zápis: min/max/průměr -";

   [ObservableProperty]
   private string readSpeedStats = "Čtení: min/max/průměr -";

   [ObservableProperty]
   private string writeBandBrush = "#DC3545";

   [ObservableProperty]
   private string readBandBrush = "#DC3545";

   [ObservableProperty]
   private string writeBandText = "Nízké pásmo";

   [ObservableProperty]
   private string readBandText = "Nízké pásmo";

    private bool _canPrintCertificate;
    private bool _canPrintLabel;
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

   private double _maxWriteSpeedMeasured;
   private double _maxReadSpeedMeasured;

    public bool CanPrintCertificate
    {
       get => _canPrintCertificate;
       private set => SetProperty(ref _canPrintCertificate, value);
    }

    public bool CanPrintLabel
    {
       get => _canPrintLabel;
       private set => SetProperty(ref _canPrintLabel, value);
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
   /// Gets the fixed number of rows for the surface visualization grid.
   /// </summary>
   public int VisualGridRows => VisualGridRowCount;

   /// <summary>
   /// Gets the fixed number of columns for the surface visualization grid.
   /// </summary>
   public int VisualGridColumns => VisualGridColumnCount;

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
      _speedAxis = new LinearAxis
      {
         Position = AxisPosition.Left,
         Title = "MB/s",
         Minimum = 0,
         Maximum = 100
      };
      _speedPlotModel = CreateSpeedPlotModel();
      InitializeBlocks(0);
      InitializeProgressHandling();
   }

   /// <summary>
   /// Inicializuje kolekci bloků pro vizualizaci synchronně.
   /// </summary>
   private void InitializeBlocks(long totalBytes)
   {
      int calculatedActiveBlocks = totalBytes <= 0
         ? 0
         : (int)Math.Ceiling(totalBytes / (double)BytesPerVisualBlock);

      _activeBlockCount = Math.Clamp(calculatedActiveBlocks, 0, TotalVisualBlocks);
      ActiveBlockCount = _activeBlockCount;

      // Создаем пустую коллекцию с 1000 блоков
      Blocks = new ObservableCollection<BlockStatus>();
      for (int i = 0; i < TotalVisualBlocks; i++)
      {
         Blocks.Add(new BlockStatus
         {
            Index = i,
            Status = 0,
            IsAllocated = i < _activeBlockCount
         });
      }
   }

   /// <summary>
   /// Asynchronně inicializuje kolekci bloků pro vizualizaci (po dohodě UI threadu).
   /// </summary>
   private async Task InitializeBlocksAsyncAsync(long totalBytes)
   {
      int calculatedActiveBlocks = totalBytes <= 0
         ? 0
         : (int)Math.Ceiling(totalBytes / (double)BytesPerVisualBlock);

      _activeBlockCount = Math.Clamp(calculatedActiveBlocks, 0, TotalVisualBlocks);
      ActiveBlockCount = _activeBlockCount;

      // Vytvářet bloky v batches aby se neblokoval UI thread
      var newBlocks = new List<BlockStatus>(TotalVisualBlocks);
      
      // Vytvořit bloky asynchronně v pozadí
      await Task.Run(() =>
      {
         for (int i = 0; i < TotalVisualBlocks; i++)
         {
            newBlocks.Add(new BlockStatus
            {
               Index = i,
               Status = 0,
               IsAllocated = i < _activeBlockCount
            });
         }
      });

      // Přepsat kolekci na UI threadu
      Blocks = new ObservableCollection<BlockStatus>(newBlocks);
    }

   /// <summary>
   /// Aktualizuje diskové informace při změně výběru disku.
   /// </summary>
   /// <param name="value">Nově vybraný disk.</param>
   partial void OnSelectedDriveChanged(CoreDriveInfo? value)
   {
        if(value != null)
        {
            SelectedDriveInfo = $"💾 {value.Name} ({FormatBytes(value.TotalSize)})";
            TotalBytes = value.TotalSize;
            
            // Dynamicky spočítat BytesPerVisualBlock aby se vešlo do 100x10 gridu
            // Cíl: max 100 sloupců, takže celkem max 1000 bloků
            // 1TB disk: 1TB / 1000 = 1GB per block
            // 500GB disk: 500GB / 1000 = 512MB per block
            long bytesPerBlock = Math.Max(1024L * 1024L, value.TotalSize / TotalVisualBlocks);
            BytesPerVisualBlock = bytesPerBlock;
            
            // Inicializovat bloky asynchronně, aby se neblokoval UI thread
            _ = InitializeBlocksAsyncAsync(value.TotalSize);
        }
        else
        {
            SelectedDriveInfo = "Vyber disk pro test";
            TotalBytes = 0;
            BytesPerVisualBlock = 1024L * 1024L * 1024L; // Reset na default 1GB
            InitializeBlocks(0);
        }
    }

   /// <summary>
   /// Vypočítá úhel ručičky budíku z aktuální propustnosti.
   /// </summary>
   /// <param name="throughputMbps">Aktuální rychlost v MB/s.</param>
   /// <param name="maxMbps">Maximum budíku v MB/s.</param>
   /// <returns>Úhel ručičky v rozsahu -120 až 120 stupňů.</returns>
   private static double CalculateNeedleAngle(double throughputMbps, double maxMbps)
   {
      if(maxMbps <= 0)
      {
         return -95;
      }

      double normalized = Math.Clamp(throughputMbps / maxMbps, 0, 1);
      // Range from -95° (zero, left) to +85° (max, right) = 180° total sweep
      return -95 + (normalized * 180);
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
      TotalBytes = SelectedDrive.TotalSize;
      ProgressPercent = 0;
      ErrorCount = 0;
      WriteBytesProcessed = 0;
      ReadBytesProcessed = 0;
      CurrentWriteThroughputMbps = 0;
      CurrentReadThroughputMbps = 0;
      GaugeMaxMbps = 100; // Start with reasonable minimum
      WriteNeedleAngle = -95; // Zero position (left)
      ReadNeedleAngle = -95; // Zero position (left)
      _maxWriteSpeedMeasured = 0;
      _maxReadSpeedMeasured = 0;
      WriteSpeedStats = "Zápis: min/max/průměr -";
      ReadSpeedStats = "Čtení: min/max/průměr -";
      WriteBandBrush = "#DC3545";
      ReadBandBrush = "#DC3545";
      WriteBandText = "Nízké pásmo";
      ReadBandText = "Nízké pásmo";
       CanPrintCertificate = false;
       CanPrintLabel = false;
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
      InitializeBlocks(SelectedDrive.TotalSize);
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

    [RelayCommand]
    public void PrintLabel()
    {
       if(SelectedDrive == null || _lastSurfaceTestResult == null || _lastSmartCheckResult == null)
       {
          StatusMessage = "❌ Štítek nelze vytisknout. Nejprve dokončete kompletní test.";
          return;
       }

       var dialog = new Views.LabelPrinterDialog();
       if(dialog.ShowDialog() == true)
       {
          var labelSize = dialog.GetSelectedSize();
          var testDate = DateTime.UtcNow;
          
          var document = Services.DiskLabelPrinterBuilder.CreateLabel(
              SelectedDrive,
              _lastSmartCheckResult,
              _lastSurfaceTestResult,
              labelSize,
              testDate);

          var printDialog = new System.Windows.Controls.PrintDialog();
          if(printDialog.ShowDialog() == true)
          {
             printDialog.PrintDocument(document.DocumentPaginator, $"DiskChecker štítek - {labelSize}");
             StatusMessage = $"🖨️ Štítek ({labelSize}) odeslán do tisku.";
          }
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
      int visualizableCount = Math.Max(_activeBlockCount, 1);
      double blockProgress = Math.Min(progress.PercentComplete / 100.0 * visualizableCount, visualizableCount);

      for(int i = 0; i < _activeBlockCount; i++)
      {
         if(i < blockProgress * 0.5)
         {
            Blocks[i].Status = 2;
         }
         else if(i < blockProgress)
         {
            Blocks[i].Status = 3;
         }
         else if(i == (int)blockProgress)
         {
            Blocks[i].Status = 1;
         }
      }
   }

   /// <summary>
   /// Nastaví vybraný disk pro test.
   /// </summary>
   public void SetSelectedDrive(CoreDriveInfo? drive)
   {
      SelectedDrive = drive;
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

      model.Axes.Add(_speedAxis);

      model.Series.Add(_currentSpeedSeries);
      model.Series.Add(_referenceSpeedSeries);
      return model;
   }

   private void UpdateSpeedStatistics()
   {
      var writeSamples = SpeedSamples.Where(s => s.Phase == 0).Select(s => s.ThroughputMbps).ToList();
      var readSamples = SpeedSamples.Where(s => s.Phase == 1).Select(s => s.ThroughputMbps).ToList();

      if(writeSamples.Count > 0)
      {
         double min = writeSamples.Min();
         double max = writeSamples.Max();
         double avg = writeSamples.Average();
         _maxWriteSpeedMeasured = Math.Max(_maxWriteSpeedMeasured, max);
         WriteSpeedStats = $"Zápis: min {min:F1} | max {max:F1} | průměr {avg:F1} MB/s";
      }

      if(readSamples.Count > 0)
      {
         double min = readSamples.Min();
         double max = readSamples.Max();
         double avg = readSamples.Average();
         _maxReadSpeedMeasured = Math.Max(_maxReadSpeedMeasured, max);
         ReadSpeedStats = $"Čtení: min {min:F1} | max {max:F1} | průměr {avg:F1} MB/s";
      }
   }

   private void UpdateDynamicScale()
   {
      double observedMax = Math.Max(_maxWriteSpeedMeasured, _maxReadSpeedMeasured);
      if(observedMax <= 0)
      {
         return;
      }

      _speedAxis.Maximum = Math.Ceiling(observedMax * 1.1);
   }

   private void UpdateBandState(bool isWriteBand, double currentSpeed)
   {
      double phaseMax = isWriteBand ? _maxWriteSpeedMeasured : _maxReadSpeedMeasured;
      if(phaseMax <= 0)
      {
         if(isWriteBand)
         {
            WriteBandBrush = "#DC3545";
            WriteBandText = "Nízké pásmo";
         }
         else
         {
            ReadBandBrush = "#DC3545";
            ReadBandText = "Nízké pásmo";
         }

         return;
      }

      double greenThreshold = phaseMax * 0.8;
      double orangeThreshold = phaseMax * 0.5;

      string brush;
      string text;
      if(currentSpeed >= greenThreshold)
      {
         brush = "#28A745";
         text = "Zelené pásmo";
      }
      else if(currentSpeed >= orangeThreshold)
      {
         brush = "#FF8C00";
         text = "Oranžové pásmo";
      }
      else
      {
         brush = "#DC3545";
         text = "Červené pásmo";
      }

      if(isWriteBand)
      {
         WriteBandBrush = brush;
         WriteBandText = text;
      }
      else
      {
         ReadBandBrush = brush;
         ReadBandText = text;
      }
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

      UpdateSpeedStatistics();
      UpdateDynamicScale();
      SpeedPlotModel.InvalidatePlot(true);
   }

   private async Task LoadSmartDataForCertificateAsync()
   {
       if(SelectedDrive == null)
       {
          CanPrintCertificate = false;
          CanPrintLabel = false;
          CertificateStatus = "SMART data nejsou dostupná.";
          return;
       }

       _lastSmartCheckResult = await _smartCheckService.RunAsync(SelectedDrive);
       CanPrintCertificate = _lastSmartCheckResult != null && _lastSurfaceTestResult != null;
       CanPrintLabel = CanPrintCertificate;
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
