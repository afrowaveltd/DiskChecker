using System.Windows.Threading;
using DiskChecker.Core.Models;
using OxyPlot;

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
         
         BytesProcessed = progress.BytesProcessed;
         ProgressPercent = progress.PercentComplete;
         CurrentThroughputMbps = progress.CurrentThroughputMbps;

         // Aktualizuj elapsed time
         ElapsedTime = DateTime.UtcNow - _testStartTime;

         // Vypočítej ETA
         if(progress.PercentComplete > 0 && progress.PercentComplete < 100)
         {
            double timePerPercent = ElapsedTime.TotalSeconds / progress.PercentComplete;
            double remainingSeconds = timePerPercent * (100 - progress.PercentComplete);
            EstimatedTimeRemaining = TimeSpan.FromSeconds(remainingSeconds);
         }

         // Aktualizuj fázi textu
         if(progress.PercentComplete < 50)
         {
            CurrentPhase = $"🔵 Zápis dat... {progress.PercentComplete:F1}%";
         }
         else if(progress.PercentComplete < 100)
         {
            CurrentPhase = $"🟢 Ověření čtení... {progress.PercentComplete:F1}%";
         }
         else
         {
            CurrentPhase = "✅ Test dokončen!";
         }

         // Aktualizuj block vizualizaci - throttlovaně
         UpdateBlockVisualizationThrottled(progress);

         // Přidej speed sample - ale ne každý update, jen každých 2 sekundy
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
      int percentRounded = (int)(progress.PercentComplete / 5) * 5; // Zaokrouhli na 5%
      int targetBlockIndex = (int)(percentRounded / 100.0 * Blocks.Count);

      for(int i = 0; i < Blocks.Count && i <= targetBlockIndex; i++)
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
            continue; // Nech netestované bloky
         }

         // Update jen pokud se změnil
         if(Blocks[i].Status != newStatus)
         {
            Blocks[i].Status = newStatus;
         }
      }
   }
}
