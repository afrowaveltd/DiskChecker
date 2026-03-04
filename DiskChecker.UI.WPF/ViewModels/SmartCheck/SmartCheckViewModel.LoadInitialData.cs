using DiskChecker.Core.Models;
using DiskChecker.Core.Services;
using OxyPlot;
using OxyPlot.Axes;

namespace DiskChecker.UI.WPF.ViewModels;

/// <summary>
/// Partial class pro načítání iniciálních SMART dat.
/// </summary>
public partial class SmartCheckViewModel
{
   /// <summary>
   /// Načte základní SMART data ihned po výběru disku (rychlé zobrazení bez plného testu).
   /// </summary>
   private async Task LoadInitialSmartDataAsync()
   {
      if(SelectedDrive == null)
      {
         return;
      }

      StatusMessage = $"📊 Načítám základní SMART data pro {SelectedDrive.Name}...";

      try
      {
         // Rychlý snapshot bez full RunAsync (ušetříme čas)
         using var quickCts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
         var smartData = await _smartCheckService.GetSmartaDataSnapshotAsync(SelectedDrive, quickCts.Token);
         
         if(smartData != null)
         {
            // Zobraz základní údaje
            TemperatureCelsius = smartData.Temperature;
            PowerOnHours = smartData.PowerOnHours;
            ReallocatedSectorCount = smartData.ReallocatedSectorCount;
            PendingSectorCount = smartData.PendingSectorCount;
            UncorrectableErrorCount = smartData.UncorrectableErrorCount;
            
            // První vzorek teploty do grafu
            if(smartData.Temperature > 0)
            {
               AddTemperatureSample(smartData.Temperature);
            }
            
            // Základní hodnocení
            var qualityCalculator = new QualityCalculator();
            var quality = qualityCalculator.CalculateQuality(smartData);
            QualityGrade = quality.Grade.ToString();
            QualityScore = quality.Score;
            WarningsSummary = quality.Warnings.Count == 0
                ? "Žádná varování"
                : string.Join(Environment.NewLine, quality.Warnings);
            
            SmartDataSourceText = "Zdroj dat: SMART (iniciální snapshot)";
            SmartDataSourceBadgeBackground = "#005A2B";
            StatusMessage = $"✅ Načteno: {SelectedDrive.Name} - Známka {quality.Grade}, {smartData.Temperature:F1}°C";
         }
         else
         {
            SmartDataSourceText = "Zdroj dat: SMART nedostupné";
            SmartDataSourceBadgeBackground = "#B35A00";
            StatusMessage = "⚠️ SMART data nejsou dostupná.";
         }
         
         // Načti rozšířená data na pozadí (atributy, log)
         using var advancedCts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
         await LoadAdvancedSmartDataAsync(advancedCts.Token);
      }
      catch(OperationCanceledException)
      {
         StatusMessage = "⚠️ Načítání SMART dat vypršelo (timeout).";
      }
      catch(Exception ex)
      {
         System.Diagnostics.Debug.WriteLine($"LoadInitialSmartDataAsync error: {ex.Message}");
         StatusMessage = $"⚠️ Chyba při načítání SMART dat: {ex.Message}";
      }
   }
}
