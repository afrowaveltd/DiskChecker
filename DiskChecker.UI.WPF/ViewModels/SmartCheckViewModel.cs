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
