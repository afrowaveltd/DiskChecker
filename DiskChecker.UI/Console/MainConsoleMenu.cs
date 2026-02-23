using Spectre.Console;
using DiskChecker.Application.Services;
using DiskChecker.Core.Interfaces;
using DiskChecker.Core.Models;
using static System.Console;
using System.Text;

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
        while (true)
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

            var choice = ReadLine();

            switch (choice.Trim())
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
        AnsiConsole.MarkupLine("[yellow]Získávám seznam disků...[/]");
        var drives = await _diskCheckerService.ListDrivesAsync();

        if (drives.Count == 0)
        {
            AnsiConsole.MarkupLine("[red]Nebyl nalezen žádný disk. Zkuste spustit program jako Správce (Administrator).[/]");
            WaitForReturn();
            return;
        }

        AnsiConsole.MarkupLine("[bold white]Vyberte disk pro SMART kontrolu:[/]");
        for (int i = 0; i < drives.Count; i++)
        {
            var d = drives[i];
            AnsiConsole.MarkupLine($" [blue]{i + 1}.[/] {Markup.Escape(d.Name)} ({Markup.Escape(d.Path)}) - {FormatBytes(d.TotalSize)}");
        }
        AnsiConsole.MarkupLine($" [blue]{drives.Count + 1}.[/] Zpět");
        AnsiConsole.WriteLine();
        AnsiConsole.Markup("Zadejte volbu: ");
        
        var choiceStr = ReadLine();
        if (!int.TryParse(choiceStr, out var choice) || choice < 1 || choice > drives.Count)
        {
            return;
        }

        var drive = drives[choice - 1];

        AnsiConsole.MarkupLine("[yellow]Načítám SMART data...[/]");
        var result = await _smartCheckService.RunAsync(drive);

        // Check if we got "meaningful" data (at least temperature or hours)
        bool hasRichData = result != null && (result.SmartaData.Temperature > 0 || result.SmartaData.PowerOnHours > 0);

        if (result == null || !hasRichData)
        {
            if (result == null)
            {
                AnsiConsole.MarkupLine("[red]SMART data nelze načíst vůbec.[/]");
            }
            else
            {
                AnsiConsole.MarkupLine("[yellow]Byla načtena pouze základní data, detailní parametry SMART chybí.[/]");
            }
            
            // Check for missing dependencies
            var instructions = await _smartCheckService.GetDependencyInstructionsAsync();
            if (!string.IsNullOrEmpty(instructions))
            {
                AnsiConsole.WriteLine();
                AnsiConsole.MarkupLine("[bold white]Doporučení:[/]");
                AnsiConsole.MarkupLine(instructions);
                AnsiConsole.WriteLine();

                if (AnsiConsole.Confirm("Chcete se pokusit o automatickou instalaci 'smartmontools'?"))
                {
                    AnsiConsole.MarkupLine("[yellow]Instaluji součásti, prosím čekejte...[/]");
                    var success = await _smartCheckService.TryInstallDependenciesAsync();
                    
                    if (success)
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

            if (result == null)
            {
                WaitForReturn();
                return;
            }
        }

        var table = new Table()
            .AddColumn("Parametr")
            .AddColumn("Hodnota");

        table.AddRow("Model", result.SmartaData.DeviceModel ?? "---");
        table.AddRow("Sériové číslo", result.SmartaData.SerialNumber ?? "---");
        table.AddRow("Firmware", result.SmartaData.FirmwareVersion ?? "---");
        table.AddRow("Naběhané hodiny", result.SmartaData.PowerOnHours > 0 ? result.SmartaData.PowerOnHours.ToString() : "---");
        table.AddRow("Reallocated", result.SmartaData.ReallocatedSectorCount > 0 ? result.SmartaData.ReallocatedSectorCount.ToString() : "---");
        table.AddRow("Pending", result.SmartaData.PendingSectorCount > 0 ? result.SmartaData.PendingSectorCount.ToString() : "---");
        table.AddRow("Uncorrectable", result.SmartaData.UncorrectableErrorCount > 0 ? result.SmartaData.UncorrectableErrorCount.ToString() : "---");
        table.AddRow("Teplota", result.SmartaData.Temperature > 0 ? $"{result.SmartaData.Temperature:F1} °C" : "---");
        if (result.SmartaData.WearLevelingCount.HasValue)
        {
            table.AddRow("Wear leveling", $"{result.SmartaData.WearLevelingCount.Value}%");
        }

        AnsiConsole.Write(table);
        AnsiConsole.MarkupLine($"[green]Známka: {result.Rating.Grade} | Skóre: {result.Rating.Score:F1}[/]");

        if (result.Rating.Warnings.Count > 0)
        {
            AnsiConsole.MarkupLine("[yellow]Upozornění:[/]");
            foreach (var warning in result.Rating.Warnings)
            {
                AnsiConsole.MarkupLine($" - {Markup.Escape(warning)}");
            }
        }

        WaitForReturn();
    }

    private async Task FullTestMenuAsync()
    {
        AnsiConsole.MarkupLine("[yellow]Získávám seznam disků...[/]");
        var drives = await _diskCheckerService.ListDrivesAsync();

        if (drives.Count == 0)
        {
            AnsiConsole.MarkupLine("[red]Nebyl nalezen žádný disk. Zkuste spustit program jako Správce (Administrator).[/]");
            WaitForReturn();
            return;
        }

        AnsiConsole.MarkupLine("[bold white]Vyberte disk pro test:[/]");
        for (int i = 0; i < drives.Count; i++)
        {
            var d = drives[i];
            AnsiConsole.MarkupLine($" [blue]{i + 1}.[/] {Markup.Escape(d.Name)} ({Markup.Escape(d.Path)}) - {FormatBytes(d.TotalSize)}");
        }
        AnsiConsole.MarkupLine($" [blue]{drives.Count + 1}.[/] Zpět");
        AnsiConsole.WriteLine();
        AnsiConsole.Markup("Zadejte volbu: ");

        var choiceStr = ReadLine();
        if (!int.TryParse(choiceStr, out var choice) || choice < 1 || choice > drives.Count)
        {
            return;
        }

        var drive = drives[choice - 1];

        AnsiConsole.MarkupLine("Zvolte profil testu:");
        AnsiConsole.MarkupLine(" [blue]1.[/] HDD - úplný test (zápis)");
        AnsiConsole.MarkupLine(" [blue]2.[/] SSD - rychlý test (čtení)");
        AnsiConsole.Markup("Volba: ");
        var profileChoice = ReadLine();

        var profile = profileChoice.Trim() == "1"
            ? SurfaceTestProfile.HddFull
            : SurfaceTestProfile.SsdQuick;

        var technology = profile == SurfaceTestProfile.HddFull
            ? DriveTechnology.Hdd
            : DriveTechnology.Ssd;

        var operation = profile == SurfaceTestProfile.HddFull
            ? SurfaceTestOperation.WriteZeroFill
            : SurfaceTestOperation.ReadOnly;

        var confirm = false;
        if (operation != SurfaceTestOperation.ReadOnly)
        {
            confirm = AnsiConsole.Confirm("[red]Test smaže data na disku. Zařízení budou testována jen v omezeném rozsahu. Pokračovat?[/]");
            if (!confirm)
            {
                return;
            }
        }

        var request = new SurfaceTestRequest
        {
            Drive = drive,
            Technology = technology,
            Profile = profile,
            Operation = operation,
            AllowDeviceWrite = operation != SurfaceTestOperation.ReadOnly && confirm,
            MaxBytesToTest = operation != SurfaceTestOperation.ReadOnly && drive.TotalSize > 0 ? drive.TotalSize : null
        };

        SurfaceTestResult? result = null;
        await AnsiConsole.Progress()
            .AutoClear(true)
            .HideCompleted(false)
            .StartAsync(async ctx =>
            {
                var task = ctx.AddTask("Povrchový test", new ProgressTaskSettings { MaxValue = 100 });
                var progress = new Progress<SurfaceTestProgress>(update =>
                {
                    task.Value = Math.Min(100, update.PercentComplete);
                    task.Description = $"Povrchový test {update.PercentComplete:F1}% | {update.CurrentThroughputMbps:F1} MB/s";
                });

                result = await _surfaceTestService.RunAsync(request, progress);
                task.Value = 100;
            });

        if (result == null)
        {
            AnsiConsole.MarkupLine("[red]Test se nepodařilo dokončit.[/]");
            WaitForReturn();
            return;
        }

        var table = new Table()
            .AddColumn("Parametr")
            .AddColumn("Hodnota");

        table.AddRow("Profil", result.Profile.ToString());
        table.AddRow("Operace", result.Operation.ToString());
        table.AddRow("Testováno", FormatBytes(result.TotalBytesTested));
        table.AddRow("Průměr", $"{result.AverageSpeedMbps:F1} MB/s");
        table.AddRow("Maximum", $"{result.PeakSpeedMbps:F1} MB/s");
        table.AddRow("Minimum", $"{result.MinSpeedMbps:F1} MB/s");
        table.AddRow("Chyby", result.ErrorCount.ToString());
        table.AddRow("Vzorky", result.Samples.Count.ToString());

        AnsiConsole.Write(table);

        if (result.Samples.Count > 1)
        {
            var chart = new BarChart()
                .Width(60)
                .Label("Rychlost (MB/s)")
                .AddItem("Min", result.MinSpeedMbps, Color.Red)
                .AddItem("Avg", result.AverageSpeedMbps, Color.Yellow)
                .AddItem("Max", result.PeakSpeedMbps, Color.Green);

            AnsiConsole.Write(chart);
        }

        if (result.Samples.Count > 2)
        {
            var midIndex = result.Samples.Count / 2;
            var trendTable = new Table()
                .AddColumn("Vzorek")
                .AddColumn("MB/s");

            var first = result.Samples[0];
            var middle = result.Samples[midIndex];
            var last = result.Samples[^1];

            trendTable.AddRow("Start", first.ThroughputMbps.ToString("F1"));
            trendTable.AddRow("Střed", middle.ThroughputMbps.ToString("F1"));
            trendTable.AddRow("Konec", last.ThroughputMbps.ToString("F1"));

            AnsiConsole.Write(trendTable);
        }

        if (!string.IsNullOrWhiteSpace(result.Notes))
        {
            AnsiConsole.MarkupLine($"[yellow]{Markup.Escape(result.Notes)}[/]");
        }

        await OfferSurfaceExportAsync(drive, result);

        WaitForReturn();
    }

    private async Task OfferSurfaceExportAsync(CoreDriveInfo drive, SurfaceTestResult surfaceResult)
    {
        var export = AnsiConsole.Confirm("Chcete exportovat výsledek testu?");
        if (!export)
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
        var formatChoice = ReadLine();

        var format = formatChoice.Trim() switch
        {
            "2" => "HTML",
            "3" => "CSV",
            "4" => "Certifikát (HTML)",
            "5" => "Certifikát (PDF)",
            _ => "Text"
        };

        var content = format switch
        {
            "HTML" => _reportExporter.GenerateHtml(report),
            "CSV" => _reportExporter.GenerateCsv(report),
            "Certifikát (HTML)" => _reportExporter.GenerateCertificateHtml(report),
            _ => _reportExporter.GenerateText(report)
        };

        var extension = format switch
        {
            "HTML" => "html",
            "CSV" => "csv",
            "Certifikát (HTML)" => "html",
            "Certifikát (PDF)" => "pdf",
            _ => "txt"
        };

        var filePath = AnsiConsole.Ask<string>("Zadejte cestu k souboru", $"export.{extension}");
        if (format == "Certifikát (PDF)")
        {
            var pdfBytes = _pdfExporter.GenerateCertificatePdf(report);
            await File.WriteAllBytesAsync(filePath, pdfBytes);
        }
        else
        {
            await File.WriteAllTextAsync(filePath, content);
        }

        AnsiConsole.MarkupLine($"[green]Export uložen: {filePath}[/]");

        var sendEmail = AnsiConsole.Confirm("Odeslat report emailem?");
        if (!sendEmail)
        {
            return;
        }

        var recipient = AnsiConsole.Ask<string>("Zadejte email příjemce");
        var includeCertificate = format == "Certifikát (HTML)" || format == "Certifikát (PDF)" ||
            AnsiConsole.Confirm("Použít A4 certifikát v emailu?");

        await _reportEmailService.SendReportAsync(report, recipient, includeCertificate);
        AnsiConsole.MarkupLine("[green]Email odeslán.[/]");
    }

    private async Task HistoryMenuAsync()
    {
        var pageSize = 10;
        var pageIndex = 0;

        while (true)
        {
            var history = await _historyService.GetHistoryAsync(pageSize: pageSize, pageIndex: pageIndex);

            AnsiConsole.MarkupLine($"[green]Stránka {history.PageIndex + 1} z {history.TotalPages} ({history.TotalItems} testů)[/]");
            
            var table = new Table()
                .AddColumn("Datum")
                .AddColumn("Disk")
                .AddColumn("Typ")
                .AddColumn("Známka")
                .AddColumn("Skóre")
                .AddColumn("Rychlost");

            foreach (var item in history.Items)
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

            if (history.TotalPages <= 1)
            {
                break;
            }

            AnsiConsole.MarkupLine(" [blue]N[/] Další strana | [blue]P[/] Předchozí strana | [blue]X[/] Zpět");
            AnsiConsole.Markup("Volba: ");
            var nav = ReadLine().ToUpperInvariant();

            if (nav == "P" && pageIndex > 0)
            {
                pageIndex--;
            }
            else if (nav == "N" && pageIndex < history.TotalPages - 1)
            {
                pageIndex++;
            }
            else if (nav == "X")
            {
                break;
            }
        }

        WaitForReturn();
    }

    private async Task CompareMenuAsync()
    {
        AnsiConsole.MarkupLine("[yellow]Získávám seznam disků...[/]");
        var drives = await _historyService.GetDrivesWithTestsAsync();

        if (drives.Count == 0)
        {
            AnsiConsole.MarkupLine("[red]Nebyly nalezeny žádné testy pro porovnání.[/]");
            WaitForReturn();
            return;
        }

        var selectedDrives = new List<DriveCompareItem>();

        while (selectedDrives.Count < 2)
        {
            var available = drives.Where(d => !selectedDrives.Contains(d)).ToList();

            if (available.Count == 0)
            {
                break;
            }

            AnsiConsole.MarkupLine($"[bold white]Vyberte disk pro porovnání ({selectedDrives.Count + 1}/2):[/]");
            for (int i = 0; i < available.Count; i++)
            {
                var d = available[i];
                AnsiConsole.MarkupLine($" [blue]{i + 1}.[/] {Markup.Escape(d.DriveName)} - {Markup.Escape(d.Model)} ({d.TotalTests} testů)");
            }
            AnsiConsole.MarkupLine($" [blue]{available.Count + 1}.[/] Hotovo/Zpět");
            AnsiConsole.WriteLine();
            AnsiConsole.Markup("Zadejte volbu: ");

            var choiceStr = ReadLine();
            if (!int.TryParse(choiceStr, out var choice) || choice < 1 || choice > available.Count)
            {
                break;
            }

            selectedDrives.Add(available[choice - 1]);
        }

        if (selectedDrives.Count < 2)
        {
            AnsiConsole.MarkupLine("[red]Je potřeba vybrat alespoň 2 disky pro porovnání.[/]");
            WaitForReturn();
            return;
        }

        AnsiConsole.MarkupLine("[yellow]Načítám detaily testů...[/]");
        var comparisons = new List<CompareItem>();

        for (int i = 0; i < selectedDrives.Count - 1; i++)
        {
            var drive1 = selectedDrives[i];
            var drive2 = selectedDrives[i + 1];

            var test1 = drive1.LastTestDate.HasValue 
                ? (await _historyService.GetForCompareAsync(1)).FirstOrDefault()
                : null;
            var test2 = drive2.LastTestDate.HasValue
                ? (await _historyService.GetForCompareAsync(1)).FirstOrDefault()
                : null;

            if (test1 != null && test2 != null)
            {
                var comp = await _historyService.CompareTestsAsync(test1.TestId, test2.TestId);
                comparisons.AddRange(comp);
            }
        }

        var compTable = new Table()
            .AddColumn("Parametr")
            .AddColumn(Markup.Escape(selectedDrives[0].DriveName))
            .AddColumn(Markup.Escape(selectedDrives[1].DriveName));

        foreach (var comp in comparisons)
        {
            compTable.AddRow(Markup.Escape(comp.Label), Markup.Escape(comp.Value1), Markup.Escape(comp.Value2));
        }

        AnsiConsole.Write(compTable);

        if (comparisons.Count > 0)
        {
            var export = AnsiConsole.Confirm("Chcete exportovat výsledek porovnání?");
            if (export)
            {
                var content = GenerateCompareText(comparisons, selectedDrives);
                var filePath = AnsiConsole.Ask<string>("Zadejte cestu k souboru", "compare.txt");
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

        foreach (var comp in comparisons)
        {
            sb.AppendLine($"{comp.Label, -30} | {comp.Value1, -20} | {comp.Value2, -20}");
        }

        return sb.ToString();
    }

    private async Task SettingsMenuAsync()
    {
        while (true)
        {
            try { Clear(); } catch { }
            AnsiConsole.Write(new FigletText("Nastaveni").Color(Color.Yellow));
            AnsiConsole.MarkupLine("[bold white]Nastavení aplikace[/]");
            AnsiConsole.MarkupLine(" [blue]1.[/] Jazyk / Language");
            AnsiConsole.MarkupLine(" [blue]2.[/] Email (SMTP) nastavení");
            AnsiConsole.MarkupLine(" [blue]3.[/] Zpět");
            AnsiConsole.WriteLine();
            AnsiConsole.Markup("Zadejte volbu (1-3): ");

            var choice = ReadLine();
            switch (choice.Trim())
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
                    if (string.IsNullOrEmpty(choice)) return; // Exit on EOF
                    break;
            }
        }
    }

    private async Task ChangeLanguageMenuAsync()
    {
        AnsiConsole.MarkupLine(" [blue]1.[/] Čeština");
        AnsiConsole.MarkupLine(" [blue]2.[/] English");
        AnsiConsole.Markup("Vyberte jazyk: ");
        var lang = ReadLine();
        var language = lang.Trim() == "2" ? "English" : "Čeština";
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

        if (!AnsiConsole.Confirm("Chcete změnit nastavení?"))
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
            if (System.Console.IsInputRedirected)
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

    private static string FormatBytes(long bytes)
    {
        string[] sizes = { "B", "KB", "MB", "GB", "TB" };
        int i = 0;
        double b = bytes;
        while (b >= 1024 && i < sizes.Length - 1)
        {
            b /= 1024;
            i++;
        }
        return $"{b:F1} {sizes[i]}";
    }
}
