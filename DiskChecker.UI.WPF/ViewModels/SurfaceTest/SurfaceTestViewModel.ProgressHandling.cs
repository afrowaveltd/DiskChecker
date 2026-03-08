using System.Windows.Threading;
using DiskChecker.Core.Models;

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
             // Write phase (0-50%)
             WriteBytesProcessed = Math.Min(TotalBytes, Math.Max(0, progress.BytesProcessed));
             ReadBytesProcessed = 0;
             _maxWriteSpeedMeasured = Math.Max(_maxWriteSpeedMeasured, progress.CurrentThroughputMbps);
          }
          else
          {
             // Verify phase (50-100%)
             // In verify phase, we've already written all data, now reading it back
             WriteBytesProcessed = TotalBytes;
             // For display purposes, show how much we've read so far in the verify phase
             double verifyProgress = (progress.PercentComplete - 50.0) * 2.0; // Scale 50-100% to 0-100%
             ReadBytesProcessed = (long)(TotalBytes * verifyProgress / 100.0);
             ReadBytesProcessed = Math.Min(TotalBytes, Math.Max(0, ReadBytesProcessed));
             _maxReadSpeedMeasured = Math.Max(_maxReadSpeedMeasured, progress.CurrentThroughputMbps);
          }

          // For overall progress display, show bytes processed in current phase
          BytesProcessed = isWritePhase ? WriteBytesProcessed : ReadBytesProcessed;

         CurrentWriteThroughputMbps = isWritePhase ? progress.CurrentThroughputMbps : 0;
         CurrentReadThroughputMbps = isWritePhase ? 0 : progress.CurrentThroughputMbps;

          double observedMax = Math.Max(_maxWriteSpeedMeasured, _maxReadSpeedMeasured);
          GaugeMaxMbps = observedMax <= 0 ? 100 : Math.Ceiling(observedMax * 1.10); // Max + 10% reserve

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
    /// Throttlovaná vizualizace bloků - klasický scandisk styl.
    /// 1000 bloků (10x100), write fáze barví modře, verify fáze přepisuje na zelenou.
    /// </summary>
    private void UpdateBlockVisualizationThrottled(SurfaceTestProgress progress)
    {
       if(_activeBlockCount <= 0)
          return;

       // Vypočítat index aktálního bloku (0 až TotalVisualBlocks-1)
       // PercentComplete: 0-50% = write, 50-100% = verify
       // Každá fáze pokrývá všech 1000 bloků
       double phasePercent;
       bool isWritePhase = progress.PercentComplete < 50;
       
       if(isWritePhase)
       {
          // Write fáze: 0-50% → mapovat na 0-100% write průběh
          phasePercent = progress.PercentComplete * 2.0; // 0-50% → 0-100%
       }
       else
       {
          // Verify fáze: 50-100% → mapovat na 0-100% verify průběh
          phasePercent = (progress.PercentComplete - 50.0) * 2.0; // 50-100% → 0-100%
       }

       int currentBlockIndex = (int)(phasePercent / 100.0 * _activeBlockCount);
       currentBlockIndex = Math.Min(Math.Max(currentBlockIndex, 0), _activeBlockCount - 1);

       if(isWritePhase)
       {
          // WRITE FÁZE: barvím modře (Status = 2)
          for(int i = 0; i < _activeBlockCount; i++)
          {
             if(i < currentBlockIndex)
             {
                // Už zapsáno - modrá
                if(Blocks[i].Status != 4) // Nepřepisovat chyby
                   Blocks[i].Status = 2; // WriteOk (modrá)
             }
             else if(i == currentBlockIndex)
             {
                // Právě zapisuji - oranžová
                Blocks[i].Status = 1; // InProgress (oranžová)
             }
             // else: ještě nezapsáno - šedá (Status = 0)
          }
       }
       else
       {
          // VERIFY FÁZE: přepisuji modrou na zelenou (Status = 3)
          for(int i = 0; i < _activeBlockCount; i++)
          {
             if(i < currentBlockIndex)
             {
                // Už ověřeno - zelená
                if(Blocks[i].Status != 4) // Nepřepisovat chyby
                   Blocks[i].Status = 3; // ReadOk (zelená)
             }
             else if(i == currentBlockIndex)
             {
                // Právě ověřuji - oranžová
                Blocks[i].Status = 1; // InProgress (oranžová)
             }
             else
             {
                // Ještě neověřeno - zůstává modrá (Status = 2)
                // Pouze pokud ještě není zapsáno (pro jistotu)
                if(Blocks[i].Status == 0) // Netestováno
                   Blocks[i].Status = 0; // Zůstává netestované
             }
          }
       }
    }
}
