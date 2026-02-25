using DiskChecker.Application.Services;
using DiskChecker.Core.Interfaces;
using DiskChecker.Core.Models;
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
      var drives = await _diskCheckerService.ListDrivesAsync();

      if(drives.Count == 0)
      {
         AnsiConsole.MarkupLine("[red]Nebyl nalezen žádný disk. Zkuste spustit program jako Správce (Administrator).[/]");
         WaitForReturn();
         return;
      }

      AnsiConsole.MarkupLine("[bold white]Vyberte disk pro SMART kontrolu:[/]");
      for(int i = 0; i < drives.Count; i++)
      {
         var d = drives[i];
         AnsiConsole.MarkupLine($" [blue]{i + 1}.[/] {Markup.Escape(d.Name)} ({Markup.Escape(d.Path)}) - {FormatBytes(d.TotalSize)}");
      }
      AnsiConsole.MarkupLine($" [blue]{drives.Count + 1}.[/] Zpět");
      AnsiConsole.WriteLine();
      AnsiConsole.Markup("Zadejte volbu: ");

      string choiceStr = ReadLine();
      if(!int.TryParse(choiceStr, out int choice) || choice < 1 || choice > drives.Count)
      {
         return;
      }

      var drive = drives[choice - 1];

      AnsiConsole.Clear();
      
      // Use Spectre's Status spinner for loading
      var result = await AnsiConsole.Status()
          .Spinner(Spinner.Known.Dots)
          .SpinnerStyle(Style.Parse("yellow"))
          .StartAsync("[yellow]Načítám SMART data...[/]", async ctx => 
          {
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

      // === Display SMART data using simple tables (no complex Layout) ===
      AnsiConsole.MarkupLine("[bold cyan]===== INFORMACE O DISKU =====[/]");
      var driveTable = new Table()
          .Border(TableBorder.Minimal)
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
      var techTable = new Table()
          .Border(TableBorder.Minimal)
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
         int months = (days % 365) / 30;
         var timeStr = years > 0 ? $"{years} let {months} měsíců" : $"{months} měsíců";
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
         var wearColor = result.SmartaData.WearLevelingCount.Value > 70 ? "yellow" : "green";
         techTable.AddRow("[cyan]Opotřebení SSD[/]", $"[{wearColor}]{result.SmartaData.WearLevelingCount.Value} %[/]");
      }

      AnsiConsole.Write(techTable);
      AnsiConsole.WriteLine();

      // === Health indicators ===
      AnsiConsole.MarkupLine("[bold green]===== STAV DISKU =====[/]");
      var healthTable = new Table()
          .Border(TableBorder.Minimal)
          .AddColumn("Parametr")
          .AddColumn("Hodnota");

      var reallocColor = result.SmartaData.ReallocatedSectorCount > 10 ? "red" :
                        result.SmartaData.ReallocatedSectorCount > 0 ? "yellow" : "green";
      healthTable.AddRow(
          "[green]Přemístěné sektory[/]",
          result.SmartaData.ReallocatedSectorCount > 0 
              ? $"[{reallocColor}]{result.SmartaData.ReallocatedSectorCount:N0}[/]" 
              : $"[green]0[/]");

      var pendingColor = result.SmartaData.PendingSectorCount > 10 ? "red" :
                        result.SmartaData.PendingSectorCount > 0 ? "yellow" : "green";
      healthTable.AddRow(
          "[green]Čekající sektory[/]",
          result.SmartaData.PendingSectorCount > 0 
              ? $"[{pendingColor}]{result.SmartaData.PendingSectorCount:N0}[/]" 
              : $"[green]0[/]");

      var uncorrColor = result.SmartaData.UncorrectableErrorCount > 10 ? "red" :
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
      var drives = await _diskCheckerService.ListDrivesAsync();

      if(drives.Count == 0)
      {
         AnsiConsole.MarkupLine("[red]Nebyl nalezen žádný disk.[/]");
         WaitForReturn();
         return;
      }

      AnsiConsole.MarkupLine("[bold white]Vyberte disk pro plný test povrchu:[/]");
      for(int i = 0; i < drives.Count; i++)
      {
         var d = drives[i];
         AnsiConsole.MarkupLine($" [blue]{i + 1}.[/] {Markup.Escape(d.Name)} ({Markup.Escape(d.Path)}) - {FormatBytes(d.TotalSize)}");
      }
      AnsiConsole.MarkupLine($" [blue]{drives.Count + 1}.[/] Zpět");
      AnsiConsole.WriteLine();
      AnsiConsole.Markup("Zadejte volbu: ");

      string choiceStr = ReadLine();
      if(!int.TryParse(choiceStr, out int choice) || choice < 1 || choice > drives.Count)
      {
         return;
      }

      var drive = drives[choice - 1];

      // Ask for test profile
      AnsiConsole.Clear();
      AnsiConsole.MarkupLine("[bold white]Vyberte profil testu:[/]");
      AnsiConsole.MarkupLine(" [blue]1.[/] HDD Plný test");
      AnsiConsole.MarkupLine(" [blue]2.[/] SSD Rychlý test");
      AnsiConsole.MarkupLine(" [blue]3.[/] Kompletní vymazání disku");
      AnsiConsole.Markup("Zadejte volbu: ");

      string profileChoice = ReadLine();
      var profile = profileChoice.Trim() switch
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
      AnsiConsole.MarkupLine("[dim]Načítání SMART dat...[/]");
      AnsiConsole.WriteLine();

      // Initialize live SMART display with error handling
      var smartDisplay = new LiveSmartDisplay(_smartCheckService);
      try
      {
         await smartDisplay.StartMonitoringAsync(drive);
         if (smartDisplay.CurrentSmartData != null)
         {
            AnsiConsole.MarkupLine("[green]✓ SMART data načtena[/]");
         }
         else
         {
            AnsiConsole.MarkupLine("[yellow]⚠ SMART data nejsou dostupná[/]");
         }
      }
      catch (Exception ex)
      {
         AnsiConsole.MarkupLine($"[yellow]⚠ Chyba při načítání SMART dat: {ex.Message}[/]");
      }
      AnsiConsole.WriteLine();

      // Calculate max bytes for ETA
      long maxBytesToTest = request.MaxBytesToTest ?? drive.TotalSize;

      // Use Spectre.Console Live Table for better alignment
      SurfaceTestResult? result = null;

      // Track current phase and metrics
      string currentPhase = "write";
      var startTime = DateTime.UtcNow;
      var lastSmartUpdate = DateTime.UtcNow;
      var writeProgress = 0.0;
      var writeSpeed = 0.0;
      var writeBytes = 0L;
      var writeEta = "--:--:--";
      var writeErrors = 0;
      var verifyProgress = 0.0;
      var verifySpeed = 0.0;
      var verifyBytes = 0L;
      var verifyEta = "--:--:--";
      var verifyErrors = 0;

      // Start test with progress display
      await AnsiConsole.Live(CreateProgressTable(
              writeProgress, writeSpeed, writeBytes, writeEta, writeErrors,
              verifyProgress, verifySpeed, verifyBytes, verifyEta, verifyErrors))
          .StartAsync(async ctx =>
          {
              var progress = new Progress<SurfaceTestProgress>(async p =>
              {
                 try
                 {
                    // Detect phase change
                    var isVerifyPhase = p.PercentComplete > 50;
                    
                    if (isVerifyPhase && currentPhase == "write")
                    {
                       currentPhase = "verify";
                       startTime = DateTime.UtcNow;
                    }

                    if (currentPhase == "write")
                    {
                       writeProgress = Math.Min(100, p.PercentComplete * 2);
                       writeSpeed = p.CurrentThroughputMbps;
                       writeBytes = p.BytesProcessed;
                       writeErrors = 0;
                       
                       // Calculate ETA
                       var elapsed = (DateTime.UtcNow - startTime).TotalSeconds;
                       var bytesPerSecond = elapsed > 0 ? p.BytesProcessed / elapsed : 0;
                       var remainingBytes = maxBytesToTest - p.BytesProcessed;
                       var etaSeconds = bytesPerSecond > 0 ? remainingBytes / bytesPerSecond : 0;
                       writeEta = FormatEta(etaSeconds);
                    }
                    else
                    {
                       writeProgress = 100;
                       verifyProgress = Math.Min(100, (p.PercentComplete - 50) * 2);
                       verifySpeed = p.CurrentThroughputMbps;
                       verifyBytes = p.BytesProcessed;
                       
                       // Calculate ETA
                       var elapsed = (DateTime.UtcNow - startTime).TotalSeconds;
                       var bytesPerSecond = elapsed > 0 ? p.BytesProcessed / elapsed : 0;
                       var remainingBytes = maxBytesToTest - p.BytesProcessed;
                       var etaSeconds = bytesPerSecond > 0 ? remainingBytes / bytesPerSecond : 0;
                       verifyEta = FormatEta(etaSeconds);
                    }

                    // NOTE: SMART data refresh is DISABLED during test to avoid DbContext threading issues
                    // SMART data will be refreshed AFTER the test completes

                    // Update progress display
                    ctx.UpdateTarget(CreateProgressTable(
                      writeProgress, writeSpeed, writeBytes, writeEta, writeErrors,
                      verifyProgress, verifySpeed, verifyBytes, verifyEta, verifyErrors));
                 }
                 catch
                 {
                    // Log error but don't crash the progress display
                 }
              });

             result = await _surfaceTestService.RunAsync(request, progress, CancellationToken.None);
             
             // Final update with actual error counts and final SMART data
             if (result != null)
             {
                 verifyErrors = result.ErrorCount;
                 try
                 {
                    await smartDisplay.RefreshDataAsync(drive);
                 }
                 catch { }
                 ctx.UpdateTarget(CreateProgressTable(
                     100, writeSpeed, writeBytes, writeEta, writeErrors,
                     100, verifySpeed, verifyBytes, verifyEta, verifyErrors));
             }
          });

      AnsiConsole.WriteLine();
      AnsiConsole.WriteLine();
      
      // Show SMART data at the end if available
      try
      {
         if (smartDisplay.CurrentSmartData != null)
         {
            AnsiConsole.MarkupLine("[bold cyan]===== STAV DISKU NA KONCI TESTU =====[/]");
            AnsiConsole.Write(smartDisplay.CreateSmartDataTable());
            AnsiConsole.WriteLine();
         }
      }
      catch { }
      
      if (result != null)
      {
         await DisplaySurfaceTestResults(result, drive, profile);
      }
   }

   /// <summary>
   /// Creates a formatted progress table for surface test display.
   /// </summary>
   private static Table CreateProgressTable(
       double writeProgress, double writeSpeed, long writeBytes, string writeEta, int writeErrors,
       double verifyProgress, double verifySpeed, long verifyBytes, string verifyEta, int verifyErrors)
   {
      var table = new Table()
          .Border(TableBorder.Rounded)
          .Centered()
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
      var errorColor = verifyErrors == 0 ? "green" : verifyErrors <= 5 ? "yellow" : "red";
      var errorMark = verifyErrors == 0 ? "✓" : "⚠";
      
      table.AddRow(
          verifyProgress > 0 ? "[yellow]Fáze 2: Ověřování[/]" : "[dim]Fáze 2: Ověřování[/]",
          verifyProgress > 0 ? $"[yellow]{verifySpeed:F1} MB/s[/]" : "[dim]0,0 MB/s[/]",
          verifyProgress > 0 ? $"[yellow]{FormatBytes(verifyBytes)}[/]" : "[dim]0 B[/]",
          verifyProgress > 0 ? $"[yellow]{GenerateProgressBar(verifyProgress)}[/] {verifyProgress:F0}%" : $"[dim]{GenerateProgressBar(0)}[/] [dim]0%[/]",
          verifyErrors == 0 ? "[green]✓ 0[/]" : $"[{errorColor}]{errorMark} {verifyErrors}[/{errorColor}]",
          verifyProgress > 0 ? $"[yellow]{verifyEta}[/]" : "[dim]--:--:--[/]");

      return table;
   }

   /// <summary>
   /// Creates a combined display with SMART data and test progress.
   /// </summary>
   
   private async Task DisplaySurfaceTestResults(SurfaceTestResult result, CoreDriveInfo drive, SurfaceTestProfile profile)
   {
      var table = new Table()
          .AddColumn("Parametr")
          .AddColumn("Hodnota");

      // === Drive Information ===
      AnsiConsole.MarkupLine("[bold cyan]=== INFORMACE O DISKU ===[/]");
      if(!string.IsNullOrEmpty(result.DriveModel))
      {
         table.AddRow("[yellow]Model[/]", Markup.Escape(result.DriveModel));
      }
      if(!string.IsNullOrEmpty(result.DriveManufacturer))
      {
         table.AddRow("[yellow]Výrobce[/]", result.DriveManufacturer);
      }
      if(!string.IsNullOrEmpty(result.DriveSerialNumber))
      {
         table.AddRow("[yellow]Sériové číslo[/]", Markup.Escape(result.DriveSerialNumber));
      }

      table.AddRow("[yellow]Kapacita[/]", FormatBytes(result.DriveTotalBytes));

      AnsiConsole.Write(table);
      AnsiConsole.WriteLine();

      // === Test Results ===
      var resultTable = new Table()
          .AddColumn("Parametr")
          .AddColumn("Hodnota");

      AnsiConsole.MarkupLine("[bold cyan]=== VÝSLEDKY TESTU ===[/]");
      resultTable.AddRow("Profil", result.Profile.ToString());
      resultTable.AddRow("Operace", result.Operation.ToString());
      resultTable.AddRow("Testováno", FormatBytes(result.TotalBytesTested));
      resultTable.AddRow("Průměr", $"{result.AverageSpeedMbps:F1} MB/s");
      resultTable.AddRow("Maximum", $"{result.PeakSpeedMbps:F1} MB/s");
      resultTable.AddRow("Minimum", $"{result.MinSpeedMbps:F1} MB/s");
      resultTable.AddRow("Chyby", result.ErrorCount.ToString());
      resultTable.AddRow("Vzorky", result.Samples.Count.ToString());

      AnsiConsole.Write(resultTable);

      if(result.ErrorCount > 0)
      {
         AnsiConsole.MarkupLine($"[red]⚠️  CHYBY: Disk hlásí {result.ErrorCount} chyb(y)[/]");
         if(result.ErrorCount > 5)
         {
            AnsiConsole.MarkupLine("[bold red]❌ DISK JE VADNÝ - NEPOUŽÍVEJTE HO![/]");
         }
      }

      if(!string.IsNullOrWhiteSpace(result.Notes))
      {
         AnsiConsole.MarkupLine($"[yellow]ℹ️  {Markup.Escape(result.Notes)}[/]");

         // Check if it's a disk detection failure and offer retry with manual selection
         if(result.Notes.Contains("DISKTEST_", StringComparison.OrdinalIgnoreCase) &&
             result.Notes.Contains("Nepodařilo se automaticky detekovat", StringComparison.OrdinalIgnoreCase))
         {
            AnsiConsole.WriteLine();
            AnsiConsole.MarkupLine("[bold yellow]💡 TIP: Disk byl naformátován, ale nepodařilo seautomaticky zjistit písmeno jednotky.[/]");
            AnsiConsole.MarkupLine("[yellow]   Zkuste:[/]");
            AnsiConsole.MarkupLine("[yellow]   1. Otevřít Průzkumník Windows (Win+E) a najít disk s labelem začínajícím 'DISKTEST_'[/]");
            AnsiConsole.MarkupLine("[yellow]   2. Restartovat aplikaci a zkusit test znovu[/]");
            AnsiConsole.MarkupLine("[yellow]   3. Restartovat počítač, pokud disk není viditelný[/]");
         }
      }

      // Positive result message
      if(result.ErrorCount == 0)
      {
         AnsiConsole.MarkupLine("[green]✓ Test úspěšný - disk vypadá v pořádku[/]");
      }

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
         AnsiConsole.MarkupLine("[red]VAROVÁNÍ: Všechna data budou smazána![/]");

         if(!AnsiConsole.Confirm("Opravdu chcete pokračit?"))
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
         string partitionLabel = AnsiConsole.Ask<string>("Zadejte název partice", "STORAGE");

         // Use diskpart for Windows disk partitioning
         string diskpartScript = $@"list disk
select disk {diskNumber}
clean
create partition primary
select partition 1
format fs=ntfs label={partitionLabel} quick
assign";

         string tempScriptPath = Path.Combine(Path.GetTempPath(), $"format_disk_{Guid.NewGuid():N}.txt");

         try
         {
            await File.WriteAllTextAsync(tempScriptPath, diskpartScript);

            var psi = new ProcessStartInfo
            {
               FileName = "diskpart",
               Arguments = $"/s \"{tempScriptPath}\"",
               UseShellExecute = false,
               CreateNoWindow = false
            };

            using var process = System.Diagnostics.Process.Start(psi);
            if(process == null)
            {
               AnsiConsole.MarkupLine("[red]Chyba: Nelze spustit diskpart[/]");
               return;
            }

            await process.WaitForExitAsync();

            if(process.ExitCode == 0)
            {
               AnsiConsole.MarkupLine($"[green]✓ Disk {Markup.Escape(drive.Name)} byl úspěšně naformátován na NTFS[/]");
               AnsiConsole.MarkupLine($"[yellow]Partice: {partitionLabel}[/]");
            }
            else
            {
               AnsiConsole.MarkupLine($"[red]Chyba: Diskpart vrátil kód {process.ExitCode}[/]");
            }
         }
         finally
         {
            try { if(File.Exists(tempScriptPath)) File.Delete(tempScriptPath); } catch { }
         }
      }
      catch(Exception ex)
      {
         AnsiConsole.MarkupLine($"[red]Chyba při formátování: {ex.Message}[/]");
      }

   }

   private async Task OfferSurfaceExportAsync(CoreDriveInfo drive, SurfaceTestResult surfaceResult)
   {
      bool export = AnsiConsole.Confirm("Chcete exportovat výsledek testu?");
      if(!export)
      {
         return;
      }

      var smartCheck = await _smartCheckService.RunAsync(drive) ?? new SmartCheckResult
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
         var history = await _historyService.GetHistoryAsync(pageSize: pageSize, pageIndex: pageIndex);

         AnsiConsole.MarkupLine($"[green]Stránka {history.PageIndex + 1} z {history.TotalPages} ({history.TotalItems} testů)[/]");

         var table = new Table()
             .AddColumn("Datum")
             .AddColumn("Disk")
             .AddColumn("Typ")
             .AddColumn("Známka")
             .AddColumn("Skóre")
             .AddColumn("Rychlost");

         foreach(var item in history.Items)
         {
            table.AddRow(
                item.TestDate.ToString("G"),
                Markup.Escape(item.DriveName),
                Markup.Escape(item.TestType),
                $"[{(item.Grade >= QualityGrade.C ? "red" : "green")}]{item.Grade}[/]",
                $"{item.Score:F1}",
                $"{item.AverageSpeed:F1} MB/s"
            );
         }

         AnsiConsole.Write(table);

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
      AnsiConsole.MarkupLine("[yellow]Získávám seznam disků...[/]");
      var drives = await _historyService.GetDrivesWithTestsAsync();

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
            var d = available[i];
            AnsiConsole.MarkupLine($" [blue]{i + 1}.[/] {Markup.Escape(d.DriveName)} - {Markup.Escape(d.Model)} ({d.TotalTests} testů)");
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
         var drive1 = selectedDrives[i];
         var drive2 = selectedDrives[i + 1];

         var test1 = drive1.LastTestDate.HasValue
             ? (await _historyService.GetForCompareAsync(1)).FirstOrDefault()
             : null;
         var test2 = drive2.LastTestDate.HasValue
             ? (await _historyService.GetForCompareAsync(1)).FirstOrDefault()
             : null;

         if(test1 != null && test2 != null)
         {
            var comp = await _historyService.CompareTestsAsync(test1.TestId, test2.TestId);
            comparisons.AddRange(comp);
         }
      }

      var compTable = new Table()
          .AddColumn("Parametr")
          .AddColumn(Markup.Escape(selectedDrives[0].DriveName))
          .AddColumn(Markup.Escape(selectedDrives[1].DriveName));

      foreach(var comp in comparisons)
      {
         compTable.AddRow(Markup.Escape(comp.Label), Markup.Escape(comp.Value1), Markup.Escape(comp.Value2));
      }

      AnsiConsole.Write(compTable);

      if(comparisons.Count > 0)
      {
         bool export = AnsiConsole.Confirm("Chcete exportovat výsledek porovnání?");
         if(export)
         {
            string content = GenerateCompareText(comparisons, selectedDrives);
            string filePath = AnsiConsole.Ask<string>("Zadejte cestu k souboru", "compare.txt");
            await File.WriteAllTextAsync(filePath, content);
            AnsiConsole.MarkupLine($"[green]Export uložen: {Markup.Escape(filePath)}[/]");
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

      foreach(var comp in comparisons)
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
      var settings = await _emailSettingsService.GetAsync();

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

   private static string FormatBytes(long bytes)
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

}

