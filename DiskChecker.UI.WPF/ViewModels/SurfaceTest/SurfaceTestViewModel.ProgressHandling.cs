using System.Windows.Threading;
using DiskChecker.Core.Models;
using DiskChecker.UI.WPF.Models;

namespace DiskChecker.UI.WPF.ViewModels;

/// <summary>
/// Partial class pro optimalizované progress handling surface testu.
/// </summary>
public partial class SurfaceTestViewModel
{
   private SurfaceTestProgress? _latestProgress;
   private DispatcherTimer? _uiUpdateTimer;
   private bool _isUpdatingUi;

   /// <summary>
   /// Inicializace throttlovaného progress handlingu.
   /// Volat z hlavního konstruktoru.
   /// </summary>
   private void InitializeProgressHandling()
   {
      // Timer pro UI update - 200ms throttling (5x za sekundu max)
      _uiUpdateTimer = new DispatcherTimer
      {
         Interval = TimeSpan.FromMilliseconds(200)
      };
      _uiUpdateTimer.Tick += UiUpdateTimer_Tick;
   }

   /// <summary>
   /// Timer callback - aktualizuje UI throttlovaně.
   /// </summary>
   private void UiUpdateTimer_Tick(object? sender, EventArgs e)
   {
      if(_isUpdatingUi || _latestProgress == null)
      {
         return;
      }

      System.Diagnostics.Debug.WriteLine($"[Timer Tick] Updating UI - Progress: {_latestProgress.PercentComplete:F1}%");

      _isUpdatingUi = true;

      try
      {
         var progress = _latestProgress;
         ProgressPercent = progress.PercentComplete;
         CurrentThroughputMbps = progress.CurrentThroughputMbps;

         bool isWritePhase = progress.PercentComplete < 50;
         if(isWritePhase)
         {
            WriteBytesProcessed = Math.Min(TotalBytes, Math.Max(0, progress.BytesProcessed));
            ReadBytesProcessed = 0;
            _maxWriteSpeedMeasured = Math.Max(_maxWriteSpeedMeasured, progress.CurrentThroughputMbps);
         }
         else
         {
            WriteBytesProcessed = TotalBytes;
            ReadBytesProcessed = Math.Min(TotalBytes, Math.Max(0, progress.BytesProcessed));
            _maxReadSpeedMeasured = Math.Max(_maxReadSpeedMeasured, progress.CurrentThroughputMbps);
         }

         BytesProcessed = Math.Min(WriteBytesProcessed + ReadBytesProcessed, TotalBytes * 2);

         CurrentWriteThroughputMbps = isWritePhase ? progress.CurrentThroughputMbps : 0;
         CurrentReadThroughputMbps = isWritePhase ? 0 : progress.CurrentThroughputMbps;

         double observedMax = Math.Max(_maxWriteSpeedMeasured, _maxReadSpeedMeasured);
         GaugeMaxMbps = observedMax <= 0 ? 100 : Math.Ceiling(observedMax * 1.15);

         UpdateBandState(true, CurrentWriteThroughputMbps);
         UpdateBandState(false, CurrentReadThroughputMbps);

         WriteNeedleAngle = CalculateNeedleAngle(CurrentWriteThroughputMbps, GaugeMaxMbps);
         ReadNeedleAngle = CalculateNeedleAngle(CurrentReadThroughputMbps, GaugeMaxMbps);

         ElapsedTime = DateTime.UtcNow - _testStartTime;

         if(progress.PercentComplete > 0 && progress.PercentComplete < 100)
         {
            double timePerPercent = ElapsedTime.TotalSeconds / progress.PercentComplete;
            double remainingSeconds = timePerPercent * (100 - progress.PercentComplete);
            EstimatedTimeRemaining = TimeSpan.FromSeconds(remainingSeconds);
         }

         if(progress.PercentComplete < 50)
         {
            CurrentPhase = $"🔵 FÁZE 1/2: Zápis dat ({progress.PercentComplete:F1} %) • Zapsáno {FormatBytes(WriteBytesProcessed)} / {FormatBytes(TotalBytes)}";
         }
         else if(progress.PercentComplete < 100)
         {
            CurrentPhase = $"🟢 FÁZE 2/2: Ověření čtením ({progress.PercentComplete:F1} %) • Ověřeno {FormatBytes(ReadBytesProcessed)} / {FormatBytes(TotalBytes)}";
         }
         else
         {
            CurrentPhase = $"✅ Test dokončen! Zapsáno i ověřeno {FormatBytes(TotalBytes)}";
         }

         UpdateBlockVisualizationThrottled(progress);

         if(SpeedSamples.Count == 0 || (ElapsedTime.TotalSeconds - SpeedSamples[^1].TimeSeconds) >= 2)
         {
            SpeedSample sample = new()
            {
               TimeSeconds = ElapsedTime.TotalSeconds,
               ThroughputMbps = progress.CurrentThroughputMbps,
               Phase = progress.PercentComplete < 50 ? 0 : 1
            };
            SpeedSamples.Add(sample);
            UpdateSpeedPlot();
         }
      }
      finally
      {
         _isUpdatingUi = false;
      }
   }

   /// <summary>
   /// Throttlovaná vizualizace bloků - update každých 5%.
   /// </summary>
   private void UpdateBlockVisualizationThrottled(SurfaceTestProgress progress)
   {
      int visualizableCount = Math.Max(_activeBlockCount, 1);
      int percentRounded = (int)(progress.PercentComplete / 5) * 5;
      int targetBlockIndex = (int)(percentRounded / 100.0 * visualizableCount);

      for(int i = 0; i < _activeBlockCount && i <= targetBlockIndex; i++)
      {
         int newStatus;
         if(i < targetBlockIndex * 0.5)
         {
            newStatus = 2; // Write OK (blue)
         }
         else if(i < targetBlockIndex)
         {
            newStatus = 3; // Read OK (green)
         }
         else if(i == targetBlockIndex)
         {
            newStatus = 1; // Currently processing
         }
         else
         {
            continue;
         }

         if(Blocks[i].Status != newStatus)
         {
            Blocks[i].Status = newStatus;
         }
      }
   }
}
