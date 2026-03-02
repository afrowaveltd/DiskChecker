using DiskChecker.Application.Services;
using DiskChecker.Core.Interfaces;
using DiskChecker.Core.Models;
using DiskChecker.Core.Services;
using Spectre.Console;
using System.Diagnostics;
using System.Text;
using static System.Console;

namespace DiskChecker.UI.Console;

/// <summary>
/// Provides the main console menu and SMART check flow.
/// </summary>
public class MainConsoleMenu
{
   private const int PanelWidth = 110;
   private static readonly TimeSpan SmartRefreshInterval = TimeSpan.FromSeconds(15);

   private readonly DiskCheckerService _diskCheckerService;
   private readonly SmartCheckService _smartCheckService;
   private readonly ISurfaceTestService _surfaceTestService;
   private readonly ITestReportExporter _reportExporter;
   private readonly IPdfReportExporter _pdfExporter;
   private readonly IReportEmailService _reportEmailService;
   private readonly HistoryService _historyService;
   private readonly IEmailSettingsService _emailSettingsService;

   /// <summary>
   /// Initializes a new instance of the <see cref="MainConsoleMenu"/> class.
   /// </summary>
   /// <param name="diskCheckerService">Service for drive discovery.</param>
   /// <param name="smartCheckService">Service for SMART checks.</param>
   /// <param name="surfaceTestService">Service for surface tests.</param>
   /// <param name="reportExporter">Service for test report exporting.</param>
   /// <param name="pdfExporter">Service for exporting reports as PDF.</param>
   /// <param name="reportEmailService">Service for sending reports via email.</param>
   /// <param name="historyService">Service for test history and comparison.</param>
   /// <param name="emailSettingsService">Service for email settings.</param>
   public MainConsoleMenu(
       DiskCheckerService diskCheckerService,
       SmartCheckService smartCheckService,
       ISurfaceTestService surfaceTestService,
       ITestReportExporter reportExporter,
       IPdfReportExporter pdfExporter,
       IReportEmailService reportEmailService,
       HistoryService historyService,
       IEmailSettingsService emailSettingsService)
   {
      _diskCheckerService = diskCheckerService;
      _smartCheckService = smartCheckService;
      _surfaceTestService = surfaceTestService;
      _reportExporter = reportExporter;
      _pdfExporter = pdfExporter;
      _reportEmailService = reportEmailService;
      _historyService = historyService;
      _emailSettingsService = emailSettingsService;
   }

   private static readonly string[] MenuChoices = new[]
   {
        "[green]1. Kontrola disku (SMART)[/]",
        "[green]2. Úplný test (zápis/nula + kontrola)[/]",
        "[yellow]3. Historie testů[/]",
        "[yellow]4. Porovnání disků[/]",
        "[red]5. Nastavení (jazyk)[/]",
        "[dim]6. Konec[/]"
    };

   public async Task ShowAsync()
   {
      while(true)
      {
         try
         {
            Clear();
         }
         catch
         {
            // No console available, skip clear
         }

         AnsiConsole.Write(new FigletText("DiskChecker").Color(Color.Red));
         AnsiConsole.MarkupLine("[bold white]Hlavní menu[/]");
         AnsiConsole.MarkupLine(" [blue]1.[/] Kontrola disku (SMART)");
         AnsiConsole.MarkupLine(" [blue]2.[/] Úplný test (zápis/nula + kontrola)");
         AnsiConsole.MarkupLine(" [blue]3.[/] Historie testů");
         AnsiConsole.MarkupLine(" [blue]4.[/] Porovnání disků");
         AnsiConsole.MarkupLine(" [blue]5.[/] Nastavení (jazyk)");
         AnsiConsole.MarkupLine(" [blue]6.[/] Konec");
         AnsiConsole.WriteLine();
         AnsiConsole.Markup("Zadejte volbu (1-6): ");

         string choice = ReadLine();

         switch(choice.Trim())
         {
            case "1":
               await CheckDiskMenuAsync();
               break;
            case "2":
               await FullTestMenuAsync();
               break;
            case "3":
               await HistoryMenuAsync();
               break;
            case "4":
               await CompareMenuAsync();
               break;
            case "5":
               await SettingsMenuAsync();
               break;
            case "6":
               return;
            default:
               AnsiConsole.MarkupLine("[red]Neplatná volba, zkuste to znovu.[/]");
               WaitForReturn();
               break;
         }
      }
   }

   private static string ReadLine()
   {
      try
      {
         return System.Console.ReadLine() ?? string.Empty;
      }
      catch
      {
         return string.Empty;
      }
   }

   private async Task CheckDiskMenuAsync()
   {
      AnsiConsole.Clear();
      AnsiConsole.MarkupLine("[yellow]Získávám seznam disků...[/]");
      IReadOnlyList<CoreDriveInfo> drives = await _diskCheckerService.ListDrivesAsync();

      if(drives.Count == 0)
      {
         AnsiConsole.MarkupLine("[red]Nebyl nalezen žádný disk. Zkuste spustit program jako Správce (Administrator).[/]");
         WaitForReturn();
         return;
      }

      AnsiConsole.MarkupLine("[bold white]Vyberte disk pro SMART kontrolu:[/]");
      for(int i = 0; i < drives.Count; i++)
      {
         CoreDriveInfo d = drives[i];
         var displayPath = FormatDrivePathForDisplay(d.Path);
         AnsiConsole.MarkupLine($" [blue]{i + 1}.[/] {Markup.Escape(d.Name)} [dim]({Markup.Escape(displayPath)})[/] - {FormatBytes(d.TotalSize)}");
      }
      AnsiConsole.MarkupLine($" [blue]{drives.Count + 1}.[/] Zpět");
      AnsiConsole.WriteLine();
      AnsiConsole.Markup("Zadejte volbu: ");

      string choiceStr = ReadLine();
      if(!int.TryParse(choiceStr, out int choice) || choice < 1 || choice > drives.Count)
      {
         return;
      }

      CoreDriveInfo drive = drives[choice - 1];

      AnsiConsole.Clear();

      // Use Spectre's Status spinner for loading with retry logic
      SmartCheckResult? result = await AnsiConsole.Status()
          .Spinner(Spinner.Known.Dots)
          .SpinnerStyle(Style.Parse("yellow"))
          .StartAsync("[yellow]Načítám SMART data (s retry)...[/]", async ctx =>
          {
             // Use RunAsync which already has error handling
             return await _smartCheckService.RunAsync(drive);
          });

      // Check if we got "meaningful" data (at least temperature or hours)
      bool hasRichData = result != null && (result.SmartaData.Temperature > 0 || result.SmartaData.PowerOnHours > 0);

      if(result == null || !hasRichData)
      {
         if(result == null)
         {
            AnsiConsole.MarkupLine("[red]SMART data nelze načíst vůbec.[/]");
         }
         else
         {
            AnsiConsole.MarkupLine("[yellow]Byla načtena pouze základní data, detailní parametry SMART chybí.[/]");
         }

         // Check for missing dependencies
         string? instructions = await _smartCheckService.GetDependencyInstructionsAsync();
         if(!string.IsNullOrEmpty(instructions))
         {
            AnsiConsole.WriteLine();
            AnsiConsole.MarkupLine("[bold white]Doporučení:[/]");
            AnsiConsole.MarkupLine(instructions);
            AnsiConsole.WriteLine();

            if(AnsiConsole.Confirm("Chcete se pokusit o automatickou instalaci 'smartmontools'?"))
            {
               AnsiConsole.MarkupLine("[yellow]Instaluji součásti, prosím čekejte...[/]");
               bool success = await _smartCheckService.TryInstallDependenciesAsync();

               if(success)
               {
                  AnsiConsole.MarkupLine("[green]Instalace dokončena úspěšně![/]");
                  AnsiConsole.MarkupLine("[yellow]DŮLEŽITÉ: Pro aktivaci nástroje je nutné aplikaci (a terminál) RESTARTOVAT.[/]");
                  WaitForReturn();
                  return;
               }
               else
               {
                  AnsiConsole.MarkupLine("[red]Instalace selhala. Zkuste prosím 'winget install smartmontools' ručně v novém okně PowerShellu.[/]");
               }
            }
         }

         if(result == null)
         {
            WaitForReturn();
            return;
         }
      }

      // === Display SMART data using rounded tables with better formatting ===
      AnsiConsole.MarkupLine("[bold cyan]===== INFORMACE O DISKU =====[/]");
      Table driveTable = new Table()
          .Border(TableBorder.Rounded)
          .BorderColor(Color.Cyan)
          .AddColumn("Parametr")
          .AddColumn("Hodnota");

      if(!string.IsNullOrEmpty(result.SmartaData.DeviceModel))
      {
         driveTable.AddRow("[yellow]Model[/]", $"[bold white]{Markup.Escape(result.SmartaData.DeviceModel)}[/]");
      }
      if(!string.IsNullOrEmpty(result.SmartaData.ModelFamily))
      {
         driveTable.AddRow("[yellow]Rodina[/]", Markup.Escape(result.SmartaData.ModelFamily));
      }
      driveTable.AddRow("[yellow]Kapacita[/]", FormatBytes(result.Drive.TotalSize));

      AnsiConsole.Write(driveTable);
      AnsiConsole.WriteLine();

      // === Technical parameters ===
      AnsiConsole.MarkupLine("[bold yellow]===== TECHNICKÉ PARAMETRY =====[/]");
      Table techTable = new Table()
          .Border(TableBorder.Rounded)
          .BorderColor(Color.Yellow)
          .AddColumn("Parametr")
          .AddColumn("Hodnota");

      if(!string.IsNullOrEmpty(result.SmartaData.SerialNumber))
      {
         techTable.AddRow("[cyan]Sériové číslo[/]", Markup.Escape(result.SmartaData.SerialNumber));
      }
      if(!string.IsNullOrEmpty(result.SmartaData.FirmwareVersion))
      {
         techTable.AddRow("[cyan]Firmware[/]", Markup.Escape(result.SmartaData.FirmwareVersion));
      }

      if(result.SmartaData.PowerOnHours > 0)
      {
         int hours = result.SmartaData.PowerOnHours;
         int days = hours / 24;
         int years = days / 365;
         int months = days % 365 / 30;
         string timeStr = years > 0 ? $"{years} let {months} měsíců" : $"{months} měsíců";
         techTable.AddRow("[cyan]Provozní hodiny[/]", $"[white]{hours:N0} h  ({timeStr})[/]");
      }

      if(result.SmartaData.Temperature > 0)
      {
         string tempColor = result.SmartaData.Temperature > 60 ? "red" :
                           result.SmartaData.Temperature > 50 ? "yellow" :
                           result.SmartaData.Temperature > 40 ? "orange1" : "green";
         tempColor = result.SmartaData.Temperature > 60 ? "red" :
                    result.SmartaData.Temperature > 50 ? "yellow" : "green";
         techTable.AddRow("[cyan]Teplota[/]", $"[{tempColor}]{result.SmartaData.Temperature:F1}°C[/]");
      }

      if(result.SmartaData.WearLevelingCount.HasValue)
      {
         string wearColor = result.SmartaData.WearLevelingCount.Value > 70 ? "yellow" : "green";
         techTable.AddRow("[cyan]Opotřebení SSD[/]", $"[{wearColor}]{result.SmartaData.WearLevelingCount.Value} %[/]");
      }

      AnsiConsole.Write(techTable);
      AnsiConsole.WriteLine();

      // === Health indicators ===
      AnsiConsole.MarkupLine("[bold green]===== STAV DISKU =====[/]");
      Table healthTable = new Table()
          .Border(TableBorder.Rounded)
          .BorderColor(Color.Green)
          .AddColumn("Parametr")
          .AddColumn("Hodnota");

      string reallocColor = result.SmartaData.ReallocatedSectorCount > 10 ? "red" :
                        result.SmartaData.ReallocatedSectorCount > 0 ? "yellow" : "green";
      healthTable.AddRow(
          "[green]Přemístěné sektory[/]",
          result.SmartaData.ReallocatedSectorCount > 0
              ? $"[{reallocColor}]{result.SmartaData.ReallocatedSectorCount:N0}[/]"
              : $"[green]0[/]");

      string pendingColor = result.SmartaData.PendingSectorCount > 10 ? "red" :
                        result.SmartaData.PendingSectorCount > 0 ? "yellow" : "green";
      healthTable.AddRow(
          "[green]Čekající sektory[/]",
          result.SmartaData.PendingSectorCount > 0
              ? $"[{pendingColor}]{result.SmartaData.PendingSectorCount:N0}[/]"
              : $"[green]0[/]");

      string uncorrColor = result.SmartaData.UncorrectableErrorCount > 10 ? "red" :
                       result.SmartaData.UncorrectableErrorCount > 0 ? "yellow" : "green";
      healthTable.AddRow(
          "[green]Neopravitelné chyby[/]",
          result.SmartaData.UncorrectableErrorCount > 0
              ? $"[{uncorrColor}]{result.SmartaData.UncorrectableErrorCount:N0}[/]"
              : $"[green]0[/]");

      AnsiConsole.Write(healthTable);
      AnsiConsole.WriteLine();

      // === Quality rating ===
      string gradeColor = result.Rating.Grade switch
      {
         QualityGrade.A => "green",
         QualityGrade.B => "green",
         QualityGrade.C => "yellow",
         QualityGrade.D => "yellow",
         QualityGrade.E => "red",
         QualityGrade.F => "red",
         _ => "white"
      };

      AnsiConsole.MarkupLine("[bold white]===== HODNOCENÍ KVALITY =====[/]");
      AnsiConsole.MarkupLine($"  Známka: [{gradeColor}]{result.Rating.Grade}[/]");
      AnsiConsole.MarkupLine($"  Skóre:  {result.Rating.Score:F1} / 100");
      AnsiConsole.WriteLine();

      if(result.Rating.Warnings.Count > 0)
      {
         AnsiConsole.MarkupLine("[bold yellow]VAROVÁNÍ:[/]");
         foreach(string warning in result.Rating.Warnings)
         {
            AnsiConsole.MarkupLine($"  [yellow]• {Markup.Escape(warning)}[/]");
         }
         AnsiConsole.WriteLine();
      }

      // Overall verdict based on grade
      if(result.Rating.Grade == QualityGrade.A || result.Rating.Grade == QualityGrade.B)
      {
         AnsiConsole.MarkupLine("[green]-----------------------------------[/]");
         AnsiConsole.MarkupLine("[green]  DISK JE V DOBRÉM STAVU[/]");
         AnsiConsole.MarkupLine("[green]-----------------------------------[/]");
      }
      else if(result.Rating.Grade == QualityGrade.F)
      {
         AnsiConsole.MarkupLine("[red]-----------------------------------[/]");
         AnsiConsole.MarkupLine("[bold red]  DISK JE VADNÝ - VYŘADIT Z PROVOZU![/]");
         AnsiConsole.MarkupLine("[red]-----------------------------------[/]");
      }
      else
      {
         AnsiConsole.MarkupLine("[yellow]-----------------------------------[/]");
         AnsiConsole.MarkupLine("[yellow]  DISK VYŽADUJE POZORNOST[/]");
         AnsiConsole.MarkupLine("[yellow]-----------------------------------[/]");
      }

      WaitForReturn();
   }

   private async Task FullTestMenuAsync()
   {
      AnsiConsole.Clear();
      AnsiConsole.MarkupLine("[yellow]Získávám seznam disků...[/]");
      IReadOnlyList<CoreDriveInfo> drives = await _diskCheckerService.ListDrivesAsync();

      if(drives.Count == 0)
      {
         AnsiConsole.MarkupLine("[red]Nebyl nalezen žádný disk.[/]");
         WaitForReturn();
         return;
      }

      AnsiConsole.MarkupLine("[bold white]Vyberte disk pro plný test povrchu:[/]");
      for(int i = 0; i < drives.Count; i++)
      {
         CoreDriveInfo d = drives[i];
         var displayPath = FormatDrivePathForDisplay(d.Path);
         AnsiConsole.MarkupLine($" [blue]{i + 1}.[/] {Markup.Escape(d.Name)} [dim]({Markup.Escape(displayPath)})[/] - {FormatBytes(d.TotalSize)}");
      }
      AnsiConsole.MarkupLine($" [blue]{drives.Count + 1}.[/] 🔍 Automatické rozpoznání disku");
      AnsiConsole.MarkupLine($" [blue]{drives.Count + 2}.[/] Zpět");
      AnsiConsole.WriteLine();
      AnsiConsole.Markup("Zadejte volbu: ");

      string choiceStr = ReadLine();
      if(!int.TryParse(choiceStr, out int choice) || choice < 1 || choice > drives.Count + 2)
      {
         return;
      }

      CoreDriveInfo? drive = null;

      if(choice == drives.Count + 1)
      {
         // Auto-detect workflow
         drive = await AutoDetectDiskAsync(drives.ToList());
         if(drive == null)
         {
            return;
         }
      }
      else if(choice == drives.Count + 2)
      {
         // Go back
         return;
      }
      else
      {
         drive = drives[choice - 1];
      }

      // Ask for test profile
      AnsiConsole.Clear();
      AnsiConsole.MarkupLine("[bold white]Vyberte profil testu:[/]");
      AnsiConsole.MarkupLine(" [blue]1.[/] HDD Plný test");
      AnsiConsole.MarkupLine(" [blue]2.[/] SSD Rychlý test");
      AnsiConsole.MarkupLine(" [blue]3.[/] Kompletní vymazání disku");
      AnsiConsole.Markup("Zadejte volbu: ");

      string profileChoice = ReadLine();
      SurfaceTestProfile profile = profileChoice.Trim() switch
      {
         "1" => SurfaceTestProfile.HddFull,
         "2" => SurfaceTestProfile.SsdQuick,
         "3" => SurfaceTestProfile.FullDiskSanitization,
         _ => SurfaceTestProfile.HddFull
      };

      var request = new SurfaceTestRequest
      {
         Drive = drive,
         Profile = profile,
         Operation = SurfaceTestOperation.WriteZeroFill,
         Technology = DriveTechnology.Unknown
      };

      AnsiConsole.Clear();
      AnsiConsole.MarkupLine($"[yellow]Spouštím test disku {Markup.Escape(drive.Name)}...[/]");
      AnsiConsole.MarkupLine("[dim]Načítání SMART dat (s retry)...[/]");
      AnsiConsole.WriteLine();

      // Initialize live SMART display with error handling - IMPROVED WITH RETRY
      var smartDisplay = new LiveSmartDisplay(_smartCheckService);
      try
      {
         // Use retry logic for problematic drives like WD
         await smartDisplay.StartMonitoringWithRetryAsync(drive);
         if(smartDisplay.CurrentSmartData != null)
         {
            AnsiConsole.MarkupLine("[green]✓ SMART data načtena[/]");
         }
         else
         {
            AnsiConsole.MarkupLine("[yellow]⚠ SMART data nejsou dostupná (test pokračuje bez nich)[/]");
         }
      }
      catch(Exception ex)
      {
         AnsiConsole.MarkupLine($"[yellow]⚠ Chyba při načítání SMART dat: {ex.Message}[/]");
      }
      AnsiConsole.WriteLine();

      SmartaData? smartData = smartDisplay.CurrentSmartData;

      // Calculate max bytes for ETA
      long maxBytesToTest = request.MaxBytesToTest ?? drive.TotalSize;

      // Use Spectre.Console Live Table for better alignment
      SurfaceTestResult? result = null;

      // Track current phase and metrics
      string currentPhase = "write";
      DateTime startTime = DateTime.UtcNow;
      DateTime lastSmartUpdate = DateTime.UtcNow;
      double writeProgress = 0.0;
      double writeSpeed = 0.0;
      long writeBytes = 0L;
      string writeEta = "--:--:--";
      int writeErrors = 0;
      double verifyProgress = 0.0;
      double verifySpeed = 0.0;
      long verifyBytes = 0L;
      string verifyEta = "--:--:--";
      int verifyErrors = 0;

      // === TEMPERATURE TRACKING ===
      int currentTemp = (int)Math.Round(smartData?.Temperature ?? 0, MidpointRounding.AwayFromZero);
      int minTemp = currentTemp;
      int maxTemp = currentTemp;
      int? currentPowerOnHours = smartData?.PowerOnHours > 0 ? smartData.PowerOnHours : null;
      var smartRefreshLock = new SemaphoreSlim(1, 1);

      long overallTestedBytes = 0L;

      // === SPEED SMOOTHING: Moving average of last 3 samples ===
      var writeSpeedSamples = new Queue<double>(3);
      var verifySpeedSamples = new Queue<double>(3);

      DateTime phaseStartTime = DateTime.UtcNow;  // Track phase start for ETA calculation
      DateTime? lastTempUpdate = null;  // Track last successful temperature update

      // Start test with progress display
      // Create a layout with disk info panel above progress table
      Layout layout = new Layout("Root")
          .SplitRows(
              new Layout("DiskInfo").Size(6),
              new Layout("Spacer1").Size(1),
              new Layout("Temperature").Size(6),  // Zvětšeno z 5 na 6 pro 2 řádky
              new Layout("Spacer2").Size(1),
              new Layout("Overall").Size(7),
              new Layout("Spacer3").Size(1),
              new Layout("Progress")
          );

      layout["DiskInfo"].Update(Align.Center(CreateDiskInfoPanel(drive, smartData, maxBytesToTest, writeBytes, verifyBytes, currentTemp, currentPowerOnHours, PanelWidth)));
      layout["Spacer1"].Update(new Text(string.Empty));
      layout["Temperature"].Update(Align.Center(CreateTemperaturePanel(smartData, currentTemp, minTemp, maxTemp, PanelWidth, lastTempUpdate)));
      layout["Spacer2"].Update(new Text(string.Empty));
      layout["Overall"].Update(Align.Center(TestVisualizationComponents.CreateOverallProgressGauge(
          writeProgress, verifyProgress, writeEta, verifyEta,
          maxBytesToTest, overallTestedBytes, TimeSpan.Zero, FormatEta(0), PanelWidth)));
      layout["Spacer3"].Update(new Text(string.Empty));
      layout["Progress"].Update(Align.Center(CreateProgressTable(
              writeProgress, writeSpeed, writeBytes, writeEta, writeErrors,
              verifyProgress, verifySpeed, verifyBytes, verifyEta, verifyErrors)));

      await AnsiConsole.Live(layout)
          .StartAsync(async ctx =>
          {
             var progress = new Progress<SurfaceTestProgress>(async p =>
             {
                try
                {
                   // ALL tests have 2 phases: WRITE (0-50%) + VERIFY (50-100%)
                   bool isVerifyPhase = p.PercentComplete >= 50;

                   // Phase transition: write → verify
                   if(isVerifyPhase && currentPhase == "write")
                   {
                      currentPhase = "verify";
                      
                      // Calculate write phase duration for verify ETA estimation
                      TimeSpan writeDuration = DateTime.UtcNow - phaseStartTime;
                      
                      // Reset for verify phase
                      phaseStartTime = DateTime.UtcNow;
                      writeBytes = maxBytesToTest;
                      writeProgress = 100;
                      writeSpeed = 0;
                      writeEta = "✓";
                      
                      // Initialize verify: 0% progress, ETA = write duration (initial estimate)
                      verifyBytes = 0;
                      verifyProgress = 0;
                      verifyEta = FormatEta(writeDuration.TotalSeconds);
                   }

                   if(currentPhase == "write")
                   {
                      // === WRITE PHASE (0-50%) ===
                      writeBytes = Math.Min(maxBytesToTest, p.BytesProcessed);
                      writeProgress = maxBytesToTest == 0 ? 0 : Math.Min(100, writeBytes * 100d / maxBytesToTest);

                      // === SMOOTH SPEED: Moving average of last 3 samples ===
                      writeSpeedSamples.Enqueue(p.CurrentThroughputMbps);
                      if(writeSpeedSamples.Count > 3)
                         writeSpeedSamples.Dequeue();
                      writeSpeed = writeSpeedSamples.Average();

                      writeErrors = 0;

                      // Calculate ETA for write phase
                      double elapsedWrite = (DateTime.UtcNow - phaseStartTime).TotalSeconds;
                      double bytesPerSecondWrite = elapsedWrite > 0 ? writeBytes / elapsedWrite : 0;
                      long remainingWriteBytes = maxBytesToTest - writeBytes;
                      double etaWriteSeconds = bytesPerSecondWrite > 0 ? remainingWriteBytes / bytesPerSecondWrite : 0;
                      writeEta = FormatEta(etaWriteSeconds);

                      // === ESTIMATE VERIFY ETA during write ===
                      // Logika: během write upřesňujeme odhad verify času podle aktuální rychlosti
                      if(elapsedWrite > 10 && writeBytes > 0)
                      {
                         // Estimate total write time
                         double estimatedWriteTotal = elapsedWrite / (writeBytes / (double)maxBytesToTest);
                         // Verify will likely take similar time
                         verifyEta = $"~{FormatEta(estimatedWriteTotal)}";
                      }
                      else
                      {
                         verifyEta = "vypočítává se...";
                      }
                   }
                   else
                   {
                      // === VERIFY PHASE (50-100%) ===
                      writeProgress = 100;
                      writeBytes = maxBytesToTest;
                      
                      // p.BytesProcessed = totalWrite + currentVerify
                      // Odečíst write bytes pro získání verify bytes
                      verifyBytes = Math.Max(0, Math.Min(maxBytesToTest, p.BytesProcessed - maxBytesToTest));
                      verifyProgress = maxBytesToTest == 0 ? 0 : Math.Min(100, verifyBytes * 100d / maxBytesToTest);

                      // === SMOOTH SPEED: Moving average of last 3 samples ===
                      verifySpeedSamples.Enqueue(p.CurrentThroughputMbps);
                      if(verifySpeedSamples.Count > 3)
                         verifySpeedSamples.Dequeue();
                      verifySpeed = verifySpeedSamples.Average();

                      // Calculate ETA for verify phase - klasicky: zbývající bytes / aktuální rychlost
                      // Use current throughput (MB/s) directly from progress instead of calculating from elapsed
                      // This gives more accurate ETA based on real-time speed
                      double currentSpeedMbps = p.CurrentThroughputMbps > 0 ? p.CurrentThroughputMbps : verifySpeed;
                      long remainingBytes = maxBytesToTest - verifyBytes;
                      double remainingMb = remainingBytes / (1024.0 * 1024.0);
                      double etaVerifySeconds = currentSpeedMbps > 0 ? remainingMb / currentSpeedMbps : 0;  // Fixed: removed * 60
                      verifyEta = FormatEta(etaVerifySeconds);
                   }

                   // === SMART DATA REFRESH - EVERY 15 SECONDS ===
                   // Use FAST temperature-only update during test (works even when disk is locked)
                   if(DateTime.UtcNow - lastSmartUpdate >= SmartRefreshInterval)
                   {
                      await smartRefreshLock.WaitAsync();
                      try
                      {
                         // Fast temperature update (doesn't require full SMART read)
                         await smartDisplay.RefreshTemperatureOnlyAsync(drive);
                         var refreshedSmartData = smartDisplay.CurrentSmartData;
                         if(refreshedSmartData != null)
                         {
                            smartData = refreshedSmartData;
                            lastTempUpdate = DateTime.UtcNow;  // Update last successful temp update time
                         }
                      }
                      finally
                      {
                         smartRefreshLock.Release();
                         lastSmartUpdate = DateTime.UtcNow;
                      }
                   }

                   if(smartData?.Temperature > 0)
                   {
                      currentTemp = (int)Math.Round(smartData.Temperature, MidpointRounding.AwayFromZero);
                      minTemp = Math.Min(minTemp, currentTemp);
                      maxTemp = Math.Max(maxTemp, currentTemp);
                   }

                   if(smartData?.PowerOnHours > 0)
                   {
                      currentPowerOnHours = smartData.PowerOnHours;
                   }

                   // Stav má při ověřování začínat od nuly, ne sčítat write + verify
                   overallTestedBytes = currentPhase == "verify" ? verifyBytes : writeBytes;

                   TimeSpan totalElapsed = DateTime.UtcNow - startTime;
                   double overallEtaSeconds;
                   if(currentPhase == "write" && writeBytes > 0)
                   {
                      double elapsed = (DateTime.UtcNow - phaseStartTime).TotalSeconds;
                      double bytesPerSecond = elapsed > 0 ? writeBytes / elapsed : 0;
                      // Remaining write bytes + estimated verify time (same speed)
                      long remainingWriteBytes = maxBytesToTest - writeBytes;
                      overallEtaSeconds = bytesPerSecond > 0 
                         ? (remainingWriteBytes / bytesPerSecond) + (maxBytesToTest / bytesPerSecond)
                         : 0;
                   }
                   else if(verifyBytes > 0)
                   {
                      // During VERIFY: Simple calculation - remaining bytes / current speed
                      double currentSpeedMbps = p.CurrentThroughputMbps > 0 ? p.CurrentThroughputMbps : verifySpeed;
                      long remainingBytes = maxBytesToTest - verifyBytes;
                      double remainingMb = remainingBytes / (1024.0 * 1024.0);
                      overallEtaSeconds = currentSpeedMbps > 0 ? (remainingMb / currentSpeedMbps) * 60 : 0; // Convert MB/s to seconds
                   }
                   else
                   {
                      overallEtaSeconds = 0;
                   }
                   string overallEta = FormatEta(overallEtaSeconds);

                   // Update progress display (disk info, temperature, overall gauge, progress table)
                   layout["DiskInfo"].Update(Align.Center(CreateDiskInfoPanel(drive, smartData, maxBytesToTest, writeBytes, verifyBytes, currentTemp, currentPowerOnHours, PanelWidth)));
                   layout["Temperature"].Update(Align.Center(CreateTemperaturePanel(smartData, currentTemp, minTemp, maxTemp, PanelWidth, lastTempUpdate)));
                   layout["Overall"].Update(Align.Center(TestVisualizationComponents.CreateOverallProgressGauge(
                      writeProgress, verifyProgress, writeEta, verifyEta,
                      maxBytesToTest, overallTestedBytes, totalElapsed, overallEta, PanelWidth)));
                   layout["Progress"].Update(Align.Center(CreateProgressTable(
                      writeProgress, writeSpeed, writeBytes, writeEta, writeErrors,
                      verifyProgress, verifySpeed, verifyBytes, verifyEta, verifyErrors)));
                   ctx.UpdateTarget(layout);
                }
                catch
                {
                   // Log error but don't crash the progress display
                }
             });

             result = await _surfaceTestService.RunAsync(request, progress, CancellationToken.None);

             // Final update with actual error counts and final SMART data
             if(result != null)
             {
                verifyErrors = result.ErrorCount;
                try
                {
                   await smartDisplay.RefreshDataWithRetryAsync(drive);
                   var refreshedSmartData = smartDisplay.CurrentSmartData;
                   if(refreshedSmartData != null)
                   {
                      smartData = refreshedSmartData;
                   }
                }
                catch { }

                if(smartData?.Temperature > 0)
                {
                   currentTemp = (int)Math.Round(smartData.Temperature, MidpointRounding.AwayFromZero);
                   minTemp = Math.Min(minTemp, currentTemp);
                   maxTemp = Math.Max(maxTemp, currentTemp);
                }

                if(smartData?.PowerOnHours > 0)
                {
                   currentPowerOnHours = smartData.PowerOnHours;
                }

                writeBytes = maxBytesToTest;
                verifyBytes = maxBytesToTest;
                writeProgress = 100;
                verifyProgress = 100;
                overallTestedBytes = maxBytesToTest;

                // Use final speeds from result instead of local variables
                double finalWriteSpeed = result.AverageSpeedMbps;
                double finalVerifySpeed = result.AverageSpeedMbps;

                TimeSpan totalElapsed = DateTime.UtcNow - startTime;
                double finalOverallEtaSeconds = 0;
                string overallEta = FormatEta(finalOverallEtaSeconds);

                layout["DiskInfo"].Update(Align.Center(CreateDiskInfoPanel(drive, smartData, maxBytesToTest, writeBytes, verifyBytes, currentTemp, currentPowerOnHours, PanelWidth)));
                layout["Temperature"].Update(Align.Center(CreateTemperaturePanel(smartData, currentTemp, minTemp, maxTemp, PanelWidth, lastTempUpdate)));
                layout["Overall"].Update(Align.Center(TestVisualizationComponents.CreateOverallProgressGauge(
                   writeProgress, verifyProgress, writeEta, verifyEta,
                   maxBytesToTest, overallTestedBytes, totalElapsed, overallEta, PanelWidth)));
                layout["Progress"].Update(Align.Center(CreateProgressTable(
                    100, finalWriteSpeed, writeBytes, writeEta, writeErrors,
                    100, finalVerifySpeed, verifyBytes, verifyEta, verifyErrors)));
                ctx.UpdateTarget(layout);
             }
          });

      AnsiConsole.WriteLine();
      AnsiConsole.WriteLine();

      // Show SMART data at the end if available
      try
      {
         if(smartDisplay.CurrentSmartData != null)
         {
            AnsiConsole.MarkupLine("[bold cyan]===== STAV DISKU NA KONCI TESTU =====[/]");
            AnsiConsole.Write(smartDisplay.CreateSmartDataTable());
            AnsiConsole.WriteLine();
         }
      }
      catch { }

      if(result != null)
      {
         await DisplaySurfaceTestResults(result, drive, profile);
      }
   }

   /// <summary>
   /// Creates a disk information panel for surface test display.
   /// </summary>
   private static Panel CreateDiskInfoPanel(CoreDriveInfo drive, SmartaData? smartData, long totalBytesToTest, long writeBytes, long verifyBytes, int currentTemp, int? currentPowerOnHours, int panelWidth)
   {
      var grid = new Grid();
      // Adjust column widths to better fit content
      grid.AddColumn(new GridColumn().Width(18));
      grid.AddColumn(new GridColumn().Width(35));
      grid.AddColumn(new GridColumn().Width(18));
      grid.AddColumn(new GridColumn().Width(29));

      var writePercent = totalBytesToTest == 0 ? 0 : writeBytes * 100.0 / totalBytesToTest;
      var verifyPercent = totalBytesToTest == 0 ? 0 : verifyBytes * 100.0 / totalBytesToTest;

      // Row 1: Model and Serial
      grid.AddRow(
          new Markup("[bold cyan]Model:[/]"),
          new Markup($"[white]{Markup.Escape((smartData?.DeviceModel ?? drive.Name).Replace(" USB Device", ""))}[/]"),
          new Markup("[bold cyan]Sériové číslo:[/]"),
          new Markup($"[white]{Markup.Escape(smartData?.SerialNumber ?? "N/A")}[/]")
      );

      // Row 2: Capacity and Test Size
      grid.AddRow(
          new Markup("[bold cyan]Celková kapacita:[/]"),
          new Markup($"[green]{FormatBytes(drive.TotalSize)}[/]"),
          new Markup("[bold cyan]Testováno:[/]"),
          new Markup($"[yellow]Zápis {FormatBytes(writeBytes)}[/] [dim]({writePercent:F1}%)[/] | [yellow]Ověření {FormatBytes(verifyBytes)}[/] [dim]({verifyPercent:F1}%)[/]")
      );

      // Row 3: Temperature and Hours
      var hasTemp = currentTemp > 0;
      var hoursText = currentPowerOnHours.HasValue ? FormatHours(currentPowerOnHours.Value) : "N/A";
      if(smartData != null || hasTemp || currentPowerOnHours.HasValue)
      {
         grid.AddRow(
             new Markup("[bold cyan]Teplota:[/]"),
             new Markup(hasTemp ? $"[white]{currentTemp}°C[/]" : "[dim]N/A[/]"),
             new Markup("[bold cyan]Odpracováno:[/]"),
             new Markup($"[white]{hoursText}[/]")
         );
      }
      else
      {
         grid.AddRow(
             new Markup("[dim]Teplota:[/]"),
             new Markup("[dim]N/A[/]"),
             new Markup("[dim]Odpracováno:[/]"),
             new Markup("[dim]N/A[/]")
         );
      }

      var panel = new Panel(grid)
      {
         Width = panelWidth
      };

      panel.Border(BoxBorder.Rounded);
      panel.BorderColor(Color.Cyan);
      panel.Header("INFORMACE O TESTOVANÉM DISKU");
      panel.HeaderAlignment(Justify.Center);
      panel.Padding(1, 0);
      return panel;
   }

   /// <summary>
   /// Creates a formatted progress table for surface test display.
   /// </summary>
   private static Table CreateProgressTable(
        double writeProgress, double writeSpeed, long writeBytes, string writeEta, int writeErrors,
        double verifyProgress, double verifySpeed, long verifyBytes, string verifyEta, int verifyErrors)
   {
      Table table = new Table()
          .Border(TableBorder.Rounded)
          .Centered()
          .Width(PanelWidth)
          .AddColumn(new TableColumn("Fáze").Width(20))
          .AddColumn(new TableColumn("Rychlost").Width(14))
          .AddColumn(new TableColumn("Data").Width(12))
          .AddColumn(new TableColumn("Průběh").Width(30))
          .AddColumn(new TableColumn("Chyby").Width(10))
          .AddColumn(new TableColumn("Zbývá").Width(10));

      // Write row
      table.AddRow(
          "[cyan]Fáze 1: Zápis[/]",
          $"[cyan]{writeSpeed:F1} MB/s[/]",
          $"[cyan]{FormatBytes(writeBytes)}[/]",
          $"[cyan]{GenerateProgressBar(writeProgress)}[/] {writeProgress:F0}%",
          writeErrors == 0 ? "[green]✓ 0[/]" : $"[red]{writeErrors}[/]",
          $"[cyan]{writeEta}[/]");

      // Verify row
      string verifyErrorMarkup = verifyErrors == 0
          ? "[green]✓ 0[/]"
          : verifyErrors <= 5
              ? $"[yellow]⚠ {verifyErrors}[/]"
              : $"[red]⚠ {verifyErrors}[/]";

      string verifyProgressMarkup = verifyProgress > 0
          ? $"[yellow]{GenerateProgressBar(verifyProgress)}[/] {verifyProgress:F0}%"
          : $"[dim]{GenerateProgressBar(0)} 0%[/]";

      table.AddRow(
          verifyProgress > 0 ? "[yellow]Fáze 2: Ověřování[/]" : "[dim]Fáze 2: Ověřování[/]",
          verifyProgress > 0 ? $"[yellow]{verifySpeed:F1} MB/s[/]" : "[dim]0,0 MB/s[/]",
          verifyProgress > 0 ? $"[yellow]{FormatBytes(verifyBytes)}[/]" : "[dim]0 B[/]",
          verifyProgressMarkup,
          verifyErrorMarkup,
          verifyProgress > 0 ? $"[yellow]{verifyEta}[/]" : "[dim]--:--:--[/]");

      return table;
   }

   private static Panel CreateTemperaturePanel(SmartaData? smartData, int currentTemp, int minTemp, int maxTemp, int panelWidth, DateTime? lastUpdate)
   {
      if(currentTemp <= 0)
      {
         var panel = new Panel(new Markup("[dim]SMART teplota není dostupná[/]"))
         {
            Width = panelWidth
         };

         panel.Border(BoxBorder.Rounded);
         panel.BorderColor(Color.Cyan);
         panel.Header("[bold cyan] TEPLOTA DISKU [/]");
         panel.Padding(1, 0);
         return panel;
      }

      return TestVisualizationComponents.CreateTemperatureGauge(currentTemp, minTemp, maxTemp, panelWidth, lastUpdate);
   }

   /// <summary>
   /// Creates a combined display with SMART data and test progress.
   /// </summary>

   private async Task DisplaySurfaceTestResults(SurfaceTestResult result, CoreDriveInfo drive, SurfaceTestProfile profile)
   {
      // Use new comprehensive report page
      await TestResultReportPage.DisplayResultAsync(result);

      // For full disk sanitization, offer formatting
      if(profile == SurfaceTestProfile.FullDiskSanitization && result.ErrorCount == 0)
      {
         AnsiConsole.WriteLine();
         AnsiConsole.MarkupLine("[bold white]Disk je nyní kompletně očištěn a připraven.[/]");

         if(AnsiConsole.Confirm("Chcete nyní disk naformátovat na NTFS?"))
         {
            await FormatDiskAsync(drive);
         }
      }

      WaitForReturn();
   }

   private async Task FormatDiskAsync(CoreDriveInfo drive)
   {
      try
      {
         AnsiConsole.MarkupLine($"\n[yellow]Formátování disku {Markup.Escape(drive.Name)}...[/]");
         AnsiConsole.MarkupLine("[red]VAROVÁNÍ: Všechna data budou smazána! Vytvoří se GPT tabulka oddílů.[/]");

         if(!AnsiConsole.Confirm("Opravdu chcete pokračovat?"))
         {
            return;
         }

         // Disk path is like \\.\PhysicalDrive0
         string driveNumber = new string(drive.Path.Where(char.IsDigit).ToArray());
         if(string.IsNullOrEmpty(driveNumber))
         {
            AnsiConsole.MarkupLine("[red]Chyba: Nelze určit číslo disku[/]");
            return;
         }

         int diskNumber = int.Parse(driveNumber);
         
         // Defaultní název oddílu "Servisni"
         string partitionLabel = "Servisni";

         // Use diskpart for Windows disk partitioning with GPT
         // GPT je povinný pro moderní systémy a větší disky
         string diskpartScript = $@"list disk
select disk {diskNumber}
clean
convert gpt
create partition primary size=max
select partition 1
format fs=ntfs label={partitionLabel} quick
assign
exit";

         string tempScriptPath = Path.Combine(Path.GetTempPath(), $"format_disk_{Guid.NewGuid():N}.txt");

         try
         {
            await File.WriteAllTextAsync(tempScriptPath, diskpartScript);

            AnsiConsole.MarkupLine("[yellow]Spouštím diskpart...[/]");
            AnsiConsole.MarkupLine("[dim]Prosím čekejte, formátování může trvat několik minut...[/]");

            var psi = new ProcessStartInfo
            {
               FileName = "diskpart",
               Arguments = $"/s \"{tempScriptPath}\"",
               UseShellExecute = false,
               CreateNoWindow = false,
               RedirectStandardOutput = true,
               RedirectStandardError = true
            };

            using var process = System.Diagnostics.Process.Start(psi);
            if(process == null)
            {
               AnsiConsole.MarkupLine("[red]Chyba: Nelze spustit diskpart[/]");
               return;
            }

            // Čekáme na dokončení procesu
            await process.WaitForExitAsync();

            if(process.ExitCode == 0)
            {
               AnsiConsole.MarkupLine($"[green]✅ Disk {Markup.Escape(drive.Name)} byl úspěšně naformátován na NTFS[/]");
               AnsiConsole.MarkupLine($"[green]✓ Tabulka oddílů: GPT[/]");
               AnsiConsole.MarkupLine($"[green]✓ Souborový systém: NTFS[/]");
               AnsiConsole.MarkupLine($"[green]✓ Název oddílu: {partitionLabel}[/]");
            }
            else
            {
               AnsiConsole.MarkupLine($"[red]❌ Chyba: Diskpart vrátil kód {process.ExitCode}[/]");
               AnsiConsole.MarkupLine("[red]Možné příčiny:[/]");
               AnsiConsole.MarkupLine("[red]  • Disk není odpojený v Průzkumníku Windows[/]");
               AnsiConsole.MarkupLine("[red]  • Chybějící oprávnění administrátora[/]");
               AnsiConsole.MarkupLine("[red]  • Disk je používán jiným procesem[/]");
               
               // Pokusíme se přečíst výstup
               string output = await process.StandardOutput.ReadToEndAsync();
               if(!string.IsNullOrEmpty(output))
               {
                  AnsiConsole.MarkupLine("[dim]Detaily:[/]");
                  AnsiConsole.MarkupLine($"[dim]{output}[/]");
               }
            }
         }
         finally
         {
            try { if(File.Exists(tempScriptPath)) File.Delete(tempScriptPath); } catch { }
         }
      }
      catch(Exception ex)
      {
         AnsiConsole.MarkupLine($"[red]❌ Chyba při formátování: {ex.Message}[/]");
         AnsiConsole.MarkupLine($"[dim]Stack: {ex.StackTrace}[/]");
      }

   }

   private async Task OfferSurfaceExportAsync(CoreDriveInfo drive, SurfaceTestResult surfaceResult)
   {
      bool export = AnsiConsole.Confirm("Chcete exportovat výsledek testu?");
      if(!export)
      {
         return;
      }

      SmartCheckResult smartCheck = await _smartCheckService.RunAsync(drive) ?? new SmartCheckResult
      {
         Drive = drive,
         SmartaData = new SmartaData(),
         Rating = new QualityRating(),
         TestDate = DateTime.UtcNow
      };

      var report = new TestReportData
      {
         SmartCheck = smartCheck,
         SurfaceTest = surfaceResult
      };

      AnsiConsole.MarkupLine("Vyberte formát exportu:");
      AnsiConsole.MarkupLine(" [blue]1.[/] Text");
      AnsiConsole.MarkupLine(" [blue]2.[/] HTML");
      AnsiConsole.MarkupLine(" [blue]3.[/] CSV");
      AnsiConsole.MarkupLine(" [blue]4.[/] Certifikát (HTML)");
      AnsiConsole.MarkupLine(" [blue]5.[/] Certifikát (PDF)");
      AnsiConsole.Markup("Volba: ");
      string formatChoice = ReadLine();

      string format = formatChoice.Trim() switch
      {
         "2" => "HTML",
         "3" => "CSV",
         "4" => "Certifikát (HTML)",
         "5" => "Certifikát (PDF)",
         _ => "Text"
      };

      string content = format switch
      {
         "HTML" => _reportExporter.GenerateHtml(report),
         "CSV" => _reportExporter.GenerateCsv(report),
         "Certifikát (HTML)" => _reportExporter.GenerateCertificateHtml(report),
         _ => _reportExporter.GenerateText(report)
      };

      string extension = format switch
      {
         "HTML" => "html",
         "CSV" => "csv",
         "Certifikát (HTML)" => "html",
         "Certifikát (PDF)" => "pdf",
         _ => "txt"
      };

      string filePath = AnsiConsole.Ask<string>("Zadejte cestu k souboru", $"export.{extension}");
      if(format == "Certifikát (PDF)")
      {
         byte[] pdfBytes = _pdfExporter.GenerateCertificatePdf(report);
         await File.WriteAllBytesAsync(filePath, pdfBytes);
      }
      else
      {
         await File.WriteAllTextAsync(filePath, content);
      }

      AnsiConsole.MarkupLine($"[green]Export uložen: {filePath}[/]");

      bool sendEmail = AnsiConsole.Confirm("Odeslat report emailem?");
      if(!sendEmail)
      {
         return;
      }

      string recipient = AnsiConsole.Ask<string>("Zadejte email příjemce");
      bool includeCertificate = format == "Certifikát (HTML)" || format == "Certifikát (PDF)" ||
            AnsiConsole.Confirm("Použít A4 certifikát v emailu?");

      await _reportEmailService.SendReportAsync(report, recipient, includeCertificate);
      AnsiConsole.MarkupLine("[green]Email odeslán.[/]");
   }

   private async Task HistoryMenuAsync()
   {
      int pageSize = 10;
      int pageIndex = 0;

      while(true)
      {
         AnsiConsole.Clear();
         AnsiConsole.MarkupLine("[bold cyan]===== HISTORIE TESTŮ =====[/]");
         PagedResult<TestHistoryItem> history = await _historyService.GetHistoryAsync(pageSize: pageSize, pageIndex: pageIndex);

         AnsiConsole.MarkupLine($"[green]Stránka {history.PageIndex + 1} z {history.TotalPages} ({history.TotalItems} testů)[/]");
         AnsiConsole.WriteLine();

         Table table = new Table()
             .Border(TableBorder.Rounded)
             .BorderColor(Color.Cyan)
             .AddColumn(new TableColumn("[bold cyan]Datum[/]").Width(25))
             .AddColumn(new TableColumn("[bold cyan]Disk[/]").Width(20))
             .AddColumn(new TableColumn("[bold cyan]Typ[/]").Width(15))
             .AddColumn(new TableColumn("[bold cyan]Známka[/]").Width(10))
             .AddColumn(new TableColumn("[bold cyan]Skóre[/]").Width(10))
             .AddColumn(new TableColumn("[bold cyan]Rychlost[/]").Width(12));

         foreach(TestHistoryItem item in history.Items)
         {
            var grade = item.Grade.ToString();
            var gradeColor = grade switch
            {
               "A" => "[green]A[/]",
               "B" => "[green]B[/]",
               "C" => "[yellow]C[/]",
               "D" => "[yellow]D[/]",
               "E" => "[red]E[/]",
               "F" => "[red]F[/]",
               _ => "[white]N/A[/]"
            };

            table.AddRow(
                $"[dim]{item.TestDate:G}[/]",
                $"[white]{Markup.Escape(item.DriveName)}[/]",
                $"[yellow]{Markup.Escape(item.TestType)}[/]",
                gradeColor,
                $"[white]{item.Score:F1}[/]",
                $"[cyan]{item.AverageSpeed:F1} MB/s[/]"
            );
         }

         AnsiConsole.Write(table);
         AnsiConsole.WriteLine();

         if(history.TotalPages <= 1)
         {
            break;
         }

         AnsiConsole.MarkupLine(" [blue]N[/] Další strana | [blue]P[/] Předchozí strana | [blue]X[/] Zpět");
         AnsiConsole.Markup("Volba: ");
         string nav = ReadLine().ToUpperInvariant();

         if(nav == "P" && pageIndex > 0)
         {
            pageIndex--;
         }
         else if(nav == "N" && pageIndex < history.TotalPages - 1)
         {
            pageIndex++;
         }
         else if(nav == "X")
         {
            break;
         }
      }

      WaitForReturn();
   }

   private async Task CompareMenuAsync()
   {
      AnsiConsole.Clear();
      AnsiConsole.MarkupLine("[bold cyan]===== POROVNÁNÍ DISKŮ =====[/]");
      AnsiConsole.MarkupLine("[yellow]Získávám seznam disků...[/]");
      List<DriveCompareItem> drives = await _historyService.GetDrivesWithTestsAsync();

      if(drives.Count == 0)
      {
         AnsiConsole.MarkupLine("[red]Nebyly nalezeny žádné testy pro porovnání.[/]");
         WaitForReturn();
         return;
      }

      var selectedDrives = new List<DriveCompareItem>();

      while(selectedDrives.Count < 2)
      {
         var available = drives.Where(d => !selectedDrives.Contains(d)).ToList();

         if(available.Count == 0)
         {
            break;
         }

         AnsiConsole.MarkupLine($"[bold white]Vyberte disk pro porovnání ({selectedDrives.Count + 1}/2):[/]");
         for(int i = 0; i < available.Count; i++)
         {
            DriveCompareItem d = available[i];
            AnsiConsole.MarkupLine($" [blue]{i + 1}.[/] {Markup.Escape(d.DriveName)} [dim]({Markup.Escape(d.Model)})[/] - {d.TotalTests} testů");
         }
         AnsiConsole.MarkupLine($" [blue]{available.Count + 1}.[/] Hotovo/Zpět");
         AnsiConsole.WriteLine();
         AnsiConsole.Markup("Zadejte volbu: ");

         string choiceStr = ReadLine();
         if(!int.TryParse(choiceStr, out int choice) || choice < 1 || choice > available.Count)
         {
            break;
         }

         selectedDrives.Add(available[choice - 1]);
      }

      if(selectedDrives.Count < 2)
      {
         AnsiConsole.MarkupLine("[red]Je potřeba vybrat alespoň 2 disky pro porovnání.[/]");
         WaitForReturn();
         return;
      }

      AnsiConsole.MarkupLine("[yellow]Načítám detaily testů...[/]");
      var comparisons = new List<CompareItem>();

      for(int i = 0; i < selectedDrives.Count - 1; i++)
      {
         DriveCompareItem drive1 = selectedDrives[i];
         DriveCompareItem drive2 = selectedDrives[i + 1];

         TestHistoryItem? test1 = drive1.LastTestDate.HasValue
             ? (await _historyService.GetForCompareAsync(1)).FirstOrDefault()
             : null;
         TestHistoryItem? test2 = drive2.LastTestDate.HasValue
             ? (await _historyService.GetForCompareAsync(1)).FirstOrDefault()
             : null;

         if(test1 != null && test2 != null)
         {
            List<CompareItem> comp = await _historyService.CompareTestsAsync(test1.TestId, test2.TestId);
            comparisons.AddRange(comp);
         }
      }

      AnsiConsole.Clear();
      AnsiConsole.MarkupLine("[bold cyan]===== VÝSLEDEK POROVNÁNÍ =====[/]");
      AnsiConsole.MarkupLine($"[yellow]Disk 1:[/] [white]{Markup.Escape(selectedDrives[0].DriveName)}[/] [dim]({Markup.Escape(selectedDrives[0].Model)})[/]");
      AnsiConsole.MarkupLine($"[yellow]Disk 2:[/] [white]{Markup.Escape(selectedDrives[1].DriveName)}[/] [dim]({Markup.Escape(selectedDrives[1].Model)})[/]");
      AnsiConsole.WriteLine();

      Table compTable = new Table()
          .Border(TableBorder.Rounded)
          .BorderColor(Color.Cyan)
          .AddColumn(new TableColumn("[bold cyan]Parametr[/]").Width(30))
          .AddColumn(new TableColumn($"[bold yellow]{Markup.Escape(selectedDrives[0].DriveName)}[/]").Width(25))
          .AddColumn(new TableColumn($"[bold yellow]{Markup.Escape(selectedDrives[1].DriveName)}[/]").Width(25));

      foreach(CompareItem comp in comparisons)
      {
         compTable.AddRow(
             $"[white]{Markup.Escape(comp.Label)}[/]",
             $"[cyan]{Markup.Escape(comp.Value1)}[/]",
             $"[cyan]{Markup.Escape(comp.Value2)}[/]"
         );
      }

      AnsiConsole.Write(compTable);
      AnsiConsole.WriteLine();

      if(comparisons.Count > 0)
      {
         bool export = AnsiConsole.Confirm("Chcete exportovat výsledek porovnání?");
         if(export)
         {
            string content = GenerateCompareText(comparisons, selectedDrives);
            string filePath = AnsiConsole.Ask<string>("Zadejte cestu k souboru", "compare.txt");
            await File.WriteAllTextAsync(filePath, content);
            AnsiConsole.MarkupLine($"[green]✓ Export uložen: {Markup.Escape(filePath)}[/]");
         }
      }

      WaitForReturn();
   }

   private string GenerateCompareText(List<CompareItem> comparisons, List<DriveCompareItem> drives)
   {
      var sb = new StringBuilder();
      sb.AppendLine("=== DiskChecker - Porovnání disků ===");
      sb.AppendLine();
      sb.AppendLine($"Disk 1: {drives[0].DriveName} ({drives[0].Model})");
      sb.AppendLine($"Disk 2: {drives[1].DriveName} ({drives[1].Model})");
      sb.AppendLine();
      sb.AppendLine(new string('-', 50));

      foreach(CompareItem comp in comparisons)
      {
         sb.AppendLine($"{comp.Label,-30} | {comp.Value1,-20} | {comp.Value2,-20}");
      }

      return sb.ToString();
   }

   private async Task SettingsMenuAsync()
   {
      while(true)
      {
         try { Clear(); } catch { }
         AnsiConsole.Write(new FigletText("Nastaveni").Color(Color.Yellow));
         AnsiConsole.MarkupLine("[bold white]Nastavení aplikace[/]");
         AnsiConsole.MarkupLine(" [blue]1.[/] Jazyk / Language");
         AnsiConsole.MarkupLine(" [blue]2.[/] Email (SMTP) nastavení");
         AnsiConsole.MarkupLine(" [blue]3.[/] Zpět");
         AnsiConsole.WriteLine();
         AnsiConsole.Markup("Zadejte volbu (1-3): ");

         string choice = ReadLine();
         switch(choice.Trim())
         {
            case "1":
               await ChangeLanguageMenuAsync();
               break;
            case "2":
               await EmailSettingsMenuAsync();
               break;
            case "3":
               return;
            default:
               if(string.IsNullOrEmpty(choice)) return; // Exit on EOF
               break;
         }
      }
   }

   private async Task ChangeLanguageMenuAsync()
   {
      AnsiConsole.MarkupLine(" [blue]1.[/] Čeština");
      AnsiConsole.MarkupLine(" [blue]2.[/] English");
      AnsiConsole.Markup("Vyberte jazyk: ");
      string lang = ReadLine();
      string language = lang.Trim() == "2" ? "English" : "Čeština";
      AnsiConsole.MarkupLine($"[green]Jazyk nastaven na: {language}[/]");
      await Task.Delay(1000);
   }

   private async Task EmailSettingsMenuAsync()
   {
      EmailSettings settings = await _emailSettingsService.GetAsync();

      AnsiConsole.MarkupLine("[bold]Aktuální SMTP nastavení:[/]");
      AnsiConsole.MarkupLine($"Host: [yellow]{Markup.Escape(settings.Host)}[/]");
      AnsiConsole.MarkupLine($"Port: [yellow]{settings.Port}[/]");
      AnsiConsole.MarkupLine($"SSL: [yellow]{settings.UseSsl}[/]");
      AnsiConsole.MarkupLine($"Uživatel: [yellow]{Markup.Escape(settings.UserName)}[/]");
      AnsiConsole.MarkupLine($"Odesílatel: [yellow]{Markup.Escape(settings.FromName)} <{Markup.Escape(settings.FromAddress)}>[/]");
      AnsiConsole.WriteLine();

      if(!AnsiConsole.Confirm("Chcete změnit nastavení?"))
      {
         return;
      }

      settings.Host = AnsiConsole.Ask<string>("SMTP Host:", settings.Host);
      settings.Port = AnsiConsole.Ask<int>("SMTP Port:", settings.Port);
      settings.UseSsl = AnsiConsole.Confirm("Použít SSL?", settings.UseSsl);
      settings.UserName = AnsiConsole.Ask<string>("Uživatelské jméno:", settings.UserName);
      settings.Password = AnsiConsole.Prompt(new TextPrompt<string>("Heslo:").Secret());
      settings.FromName = AnsiConsole.Ask<string>("Jméno odesílatele:", settings.FromName);
      settings.FromAddress = AnsiConsole.Ask<string>("Email odesílatele:", settings.FromAddress);

      await _emailSettingsService.SaveAsync(settings);
      AnsiConsole.MarkupLine("[green]Nastavení uloženo.[/]");
      WaitForReturn();
   }

   private static void WaitForReturn()
   {
      AnsiConsole.MarkupLine("[dim]Stiskněte libovolnou klávesu pro návrat...[/]");
      try
      {
         if(System.Console.IsInputRedirected)
         {
            System.Console.In.Read();
         }
         else
         {
            System.Console.ReadKey(true);
         }
      }
      catch
      {
         // Ignore
      }
   }

   /// <summary>
   /// Checks if the application is running with administrator rights.
   /// </summary>
   [System.Runtime.Versioning.SupportedOSPlatform("windows")]
   private static bool IsRunningAsAdmin()
   {
      try
      {
         using var identity = System.Security.Principal.WindowsIdentity.GetCurrent();
         var principal = new System.Security.Principal.WindowsPrincipal(identity);
         return principal.IsInRole(System.Security.Principal.WindowsBuiltInRole.Administrator);
      }
      catch
      {
         return false;
      }
   }

   internal static string FormatBytes(long bytes)
   {
      string[] sizes = { "B", "KB", "MB", "GB", "TB" };
      int i = 0;
      double b = bytes;
      while(b >= 1024 && i < sizes.Length - 1)
      {
         b /= 1024;
         i++;
      }
      return $"{b:F1} {sizes[i]}";
   }

   private static string FormatHours(int hours)
   {
      if(hours < 24)
         return $"{hours} h";

      var days = hours / 24;
      if(days < 30)
         return $"{days} dní";

      var months = days / 30;
      if(months < 12)
         return $"{months} měsíců";

      var years = months / 12;
      var remainingMonths = months % 12;
      return remainingMonths > 0 ? $"{years} let {remainingMonths} měsíců" : $"{years} let";
   }

   /// <summary>
   /// Formats ETA seconds into human-readable string (e.g., "5m 23s", "1h 15m", "< 1m").
   /// </summary>
   private static string FormatEta(double seconds)
   {
      if(seconds <= 0 || double.IsInfinity(seconds) || double.IsNaN(seconds))
         return "vypočítává se...";

      if(seconds < 60)
         return "< 1m";

      var ts = TimeSpan.FromSeconds(seconds);

      if(ts.TotalHours >= 1)
         return $"{(int)ts.TotalHours}h {ts.Minutes}m";

      if(ts.TotalMinutes >= 1)
         return $"{(int)ts.TotalMinutes}m {ts.Seconds}s";

      return $"{ts.Seconds}s";
   }

   /// <summary>
   /// Generates a text-based progress bar.
   /// </summary>
   private static string GenerateProgressBar(double percent, int width = 20)
   {
      int filled = (int)(percent / 100.0 * width);
      int empty = width - filled;
      return new string('█', filled) + new string('░', empty);
   }

   /// <summary>
   /// Auto-detection workflow for disk identification using USB port/framework.
   /// </summary>
   private async Task<CoreDriveInfo?> AutoDetectDiskAsync(List<CoreDriveInfo> initialDrives)
   {
      AnsiConsole.Clear();
      AnsiConsole.MarkupLine("[bold yellow]🔍 Automatické rozpoznání disku[/]");
      AnsiConsole.WriteLine();

      var service = new DiskDetectionService();

      // Step 1: Capture before state
      AnsiConsole.MarkupLine("[bold]Krok 1: Odpojte testovaný disk[/]");
      AnsiConsole.MarkupLine("Prosím odpojte disk z USB rámečku nebo portu, který chcete testovat.");
      AnsiConsole.WriteLine();
      AnsiConsole.MarkupLine("[dim]Aktuálně připojené disky:[/]");
      foreach(CoreDriveInfo drive in initialDrives)
      {
        AnsiConsole.MarkupLine($"  • {Markup.Escape(drive.Name)} - {FormatBytes(drive.TotalSize)}");
      }
      AnsiConsole.WriteLine();
      AnsiConsole.MarkupLine("[yellow]Stiskněte Enter když je disk odpojen...[/]");
      ReadLine();

      DiskDetectionService.DiskSnapshot snapshotBefore = service.CreateSnapshot(initialDrives);

      // Step 2: Wait for reconnect
      AnsiConsole.Clear();
      AnsiConsole.MarkupLine("[bold]Krok 2: Zapojte testovaný disk[/]");
      AnsiConsole.MarkupLine("Nyní zapojte disk do stejného USB portu zpět.");
      AnsiConsole.MarkupLine("[dim]Systém bude detekovat nový disk během 10 sekund...[/]");
      AnsiConsole.WriteLine();

      DiskDetectionService.DiskSnapshot? snapshotAfter = null;
      bool diskDetected = false;

      for(int i = 0; i < 10; i++)
      {
         await Task.Delay(1000);
         IReadOnlyList<CoreDriveInfo> currentDrives = await _diskCheckerService.ListDrivesAsync();
         snapshotAfter = service.CreateSnapshot(currentDrives.ToList());

         DiskDetectionService.ComparisonResult comparison = service.Compare(snapshotBefore, snapshotAfter);

         if(comparison.Added.Count == 1)
         {
            diskDetected = true;
            break;
         }
         else if(comparison.Added.Count > 1)
         {
            AnsiConsole.MarkupLine("[red]❌ Příliš mnoho disků připojeno! Prosím odpojte všechny disky mimo testovaného a zkuste znovu.[/]");
            WaitForReturn();
            return null;
         }

         AnsiConsole.Write($"\r  {new string('█', (i + 1) * 3)} {(i + 1) * 10}%  ");
      }

      AnsiConsole.WriteLine();
      AnsiConsole.WriteLine();

      if(!diskDetected || snapshotAfter is null)
      {
         AnsiConsole.MarkupLine("[red]❌ Čas vypršel. Disk nebyl detekován. Prosím zkontrolujte, že je disk správně zapojený, a zkuste znovu.[/]");
         WaitForReturn();
         return null;
      }

      // Step 3: Show result
      DiskDetectionService.ComparisonResult comparison2 = service.Compare(snapshotBefore, snapshotAfter);
      if(comparison2.Added.Count != 1)
      {
         AnsiConsole.MarkupLine("[red]❌ Chyba detekce. Zkuste znovu.[/]");
         WaitForReturn();
         return null;
      }

      DiskDetectionService.DiskIdentifier detectedDisk = comparison2.Added.First();
      AnsiConsole.Clear();
      AnsiConsole.MarkupLine("[bold green]✅ Disk detekován![/]");
      AnsiConsole.WriteLine();
      AnsiConsole.MarkupLine($"[green]Model:[/] {detectedDisk.Model}");
      AnsiConsole.MarkupLine($"[green]Cesta:[/] {detectedDisk.Path}");
      AnsiConsole.MarkupLine($"[green]Kapacita:[/] {FormatBytes(detectedDisk.TotalBytes)}");
      if(!string.IsNullOrEmpty(detectedDisk.DriveLetter))
      {
         AnsiConsole.MarkupLine($"[green]Písmenko:[/] {detectedDisk.DriveLetter}");
      }
      AnsiConsole.WriteLine();

      // Get the actual CoreDriveInfo from the current list
      IReadOnlyList<CoreDriveInfo> allDrives = await _diskCheckerService.ListDrivesAsync();
      CoreDriveInfo? selectedDrive = allDrives.FirstOrDefault(d => d.Path == detectedDisk.Path);

      if(selectedDrive != null)
      {
         AnsiConsole.MarkupLine("[green]✓ Disk je připraven pro test[/]");
         return selectedDrive;
      }

      AnsiConsole.MarkupLine("[red]⚠️  Disk nebyl nalezen v aktuálním seznamu. Zkuste znovu.[/]", detectedDisk.Path);
      WaitForReturn();
      return null;
   }

   private static string FormatDrivePathForDisplay(string path)
   {
      if(string.IsNullOrWhiteSpace(path))
         return string.Empty;

      var physicalIndex = path.IndexOf("PhysicalDrive", StringComparison.OrdinalIgnoreCase);
      if(physicalIndex >= 0)
         return path[physicalIndex..];

      return path.Replace("\\\\.\\", string.Empty).Replace("\\\\", string.Empty);
   }

}









