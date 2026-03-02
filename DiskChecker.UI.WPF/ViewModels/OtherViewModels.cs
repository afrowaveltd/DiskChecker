using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DiskChecker.Application.Services;
using DiskChecker.Core.Models;
using System.Collections.ObjectModel;

namespace DiskChecker.UI.WPF.ViewModels;

/// <summary>
/// Represents the status of a single disk block during surface testing.
/// </summary>
public class BlockStatus
{
   /// <summary>
   /// Block index in the disk.
   /// </summary>
   public int Index { get; set; }

   /// <summary>
   /// Status: 0 = untested, 1 = writing, 2 = write ok, 3 = read ok, 4 = error
   /// </summary>
   public int Status { get; set; }

   /// <summary>
   /// Error message if status is error.
   /// </summary>
   public string? ErrorMessage { get; set; }
}

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
   private readonly SurfaceTestService _surfaceTestService;
   private CancellationTokenSource? _testCancellationTokenSource;
   private DateTime _testStartTime;
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

   /// <summary>
   /// Initializes a new instance of the <see cref="SurfaceTestViewModel"/> class.
   /// </summary>
   public SurfaceTestViewModel(SurfaceTestService surfaceTestService)
   {
      _surfaceTestService = surfaceTestService;
      InitializeBlocks();
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

      IsTestRunning = true;
      _testCancellationTokenSource = new CancellationTokenSource();
      _testStartTime = DateTime.UtcNow;
      ProgressPercent = 0;
      ErrorCount = 0;
      SpeedSamples.Clear();
      InitializeBlocks();
      CurrentPhase = "🔵 Příprava zápisu...";
      StatusMessage = $"Zahájena test povrchu: {SelectedDrive.Name}";

      try
      {
         SurfaceTestRequest request = new SurfaceTestRequest
         {
            Drive = SelectedDrive,
            Profile = SurfaceTestProfile.HddFull,
            Operation = SurfaceTestOperation.WriteZeroFill,
            BlockSizeBytes = 1024 * 1024, // 1 MB
            SampleIntervalBlocks = 128,
            AllowDeviceWrite = true
         };

         Progress<SurfaceTestProgress> progress = new Progress<SurfaceTestProgress>(OnTestProgress);

         var result = await _surfaceTestService.RunAsync(
             request,
             progress,
             _testCancellationTokenSource.Token
         );

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
         _testCancellationTokenSource?.Dispose();
      }
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
   /// Zpracuje progress report z testu.
   /// </summary>
   private void OnTestProgress(SurfaceTestProgress progress)
   {
      System.Windows.Application.Current?.Dispatcher.Invoke(() =>
      {
         BytesProcessed = progress.BytesProcessed;
         ProgressPercent = progress.PercentComplete;
         CurrentThroughputMbps = progress.CurrentThroughputMbps;

         // Aktualizuj elapsed time
         ElapsedTime = DateTime.UtcNow - _testStartTime;

         // Vypočítej ETA
         if(progress.PercentComplete > 0 && progress.PercentComplete < 100)
         {
            var timePerPercent = ElapsedTime.TotalSeconds / progress.PercentComplete;
            var remainingSeconds = timePerPercent * (100 - progress.PercentComplete);
            EstimatedTimeRemaining = TimeSpan.FromSeconds(remainingSeconds);
         }

         // Aktualizuj block vizualizaci
         UpdateBlockVisualization(progress);

         // Přidej speed sample
         SpeedSample sample = new SpeedSample
         {
            TimeSeconds = ElapsedTime.TotalSeconds,
            ThroughputMbps = progress.CurrentThroughputMbps,
            Phase = progress.PercentComplete < 50 ? 0 : 1 // 0=write, 1=read
         };
         SpeedSamples.Add(sample);
      });
   }

   /// <summary>
   /// Aktualizuje vizualizaci bloků.
   /// </summary>
   private void UpdateBlockVisualization(SurfaceTestProgress progress)
   {
      // Vypočítej kolik bloků by mělo být v jaké fázi
      var blockProgress = Math.Min(progress.PercentComplete / 100.0 * Blocks.Count, Blocks.Count);

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

   /// <summary>
   /// Initializes the view model asynchronously.
   /// </summary>
   public override Task InitializeAsync()
   {
      StatusMessage = "🖴 Surface Test View - Připraveno";
      return Task.CompletedTask;
   }

   /// <summary>
   /// Disposes the view model.
   /// </summary>
   public void Dispose()
   {
      _testCancellationTokenSource?.Dispose();
      GC.SuppressFinalize(this);
   }
}

/// <summary>
/// ViewModel pro SMART kontrolu disku.
/// </summary>
public class SmartCheckViewModel : ViewModelBase
{
   /// <summary>
   /// Initializes a new instance of the <see cref="SmartCheckViewModel"/> class.
   /// </summary>
   public SmartCheckViewModel()
   {
      StatusMessage = "SMART Check View (Coming Soon)";
   }
}

/// <summary>
/// ViewModel pro zobrazení reportu.
/// </summary>
public class ReportViewModel : ViewModelBase
{
   /// <summary>
   /// Initializes a new instance of the <see cref="ReportViewModel"/> class.
   /// </summary>
   public ReportViewModel()
   {
      StatusMessage = "Report View (Coming Soon)";
   }
}

/// <summary>
/// ViewModel pro historii testů.
/// </summary>
public class HistoryViewModel : ViewModelBase
{
   /// <summary>
   /// Initializes a new instance of the <see cref="HistoryViewModel"/> class.
   /// </summary>
   public HistoryViewModel()
   {
      StatusMessage = "History View (Coming Soon)";
   }
}

/// <summary>
/// ViewModel pro nastavení aplikace.
/// </summary>
public class SettingsViewModel : ViewModelBase
{
   /// <summary>
   /// Initializes a new instance of the <see cref="SettingsViewModel"/> class.
   /// </summary>
   public SettingsViewModel()
   {
      StatusMessage = "Settings View (Coming Soon)";
   }
}
