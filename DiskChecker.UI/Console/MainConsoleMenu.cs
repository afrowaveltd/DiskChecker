using Spectre.Console;
using DiskChecker.Application.Services;
using DiskChecker.Core.Interfaces;
using DiskChecker.Core.Models;
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

    /// <summary>
    /// Initializes a new instance of the <see cref="MainConsoleMenu"/> class.
    /// </summary>
    /// <param name="diskCheckerService">Service for drive discovery.</param>
    /// <param name="smartCheckService">Service for SMART checks.</param>
    /// <param name="surfaceTestService">Service for surface tests.</param>
    /// <param name="reportExporter">Service for test report exporting.</param>
    /// <param name="pdfExporter">Service for exporting reports as PDF.</param>
    /// <param name="reportEmailService">Service for sending reports via email.</param>
    public MainConsoleMenu(
        DiskCheckerService diskCheckerService,
        SmartCheckService smartCheckService,
        ISurfaceTestService surfaceTestService,
        ITestReportExporter reportExporter,
        IPdfReportExporter pdfExporter,
        IReportEmailService reportEmailService)
    {
        _diskCheckerService = diskCheckerService;
        _smartCheckService = smartCheckService;
        _surfaceTestService = surfaceTestService;
        _reportExporter = reportExporter;
        _pdfExporter = pdfExporter;
        _reportEmailService = reportEmailService;
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

            var choice = AnsiConsole.Prompt(
                new SelectionPrompt<string>()
                    .Title("DiskChecker - hlavní menu [v0.1]")
                    .PageSize(10)
                    .MoreChoicesText("[i](použijte šipky pro výběr)[/]")
                    .AddChoices(MenuChoices));

            switch (choice)
            {
                case var _ when choice.Contains("Kontrola disku"):
                    await CheckDiskMenuAsync();
                    break;
                case var _ when choice.Contains("Úplný test"):
                    await FullTestMenuAsync();
                    break;
                case var _ when choice.Contains("Historie"):
                    await HistoryMenuAsync();
                    break;
                case var _ when choice.Contains("Porovnání"):
                    await CompareMenuAsync();
                    break;
                case var _ when choice.Contains("Nastavení"):
                    await SettingsMenuAsync();
                    break;
                case var _ when choice.Contains("Konec"):
                    return;
            }
        }
    }

    private async Task CheckDiskMenuAsync()
    {
        AnsiConsole.MarkupLine("[yellow]Získávám seznam disků...[/]");
        var drives = await _diskCheckerService.ListDrivesAsync();

        if (drives.Count == 0)
        {
            AnsiConsole.MarkupLine("[red]Nebyl nalezen žádný disk.[/]");
            WaitForReturn();
            return;
        }

        var drive = AnsiConsole.Prompt(
            new SelectionPrompt<CoreDriveInfo>()
                .Title("Vyberte disk pro SMART kontrolu")
                .PageSize(10)
                .UseConverter(info => $"{info.Name} ({info.Path})")
                .AddChoices(drives));

        AnsiConsole.MarkupLine("[yellow]Načítám SMART data...[/]");
        var result = await _smartCheckService.RunAsync(drive);

        if (result == null)
        {
            AnsiConsole.MarkupLine("[red]SMART data nelze načíst.[/]");
            WaitForReturn();
            return;
        }

        var table = new Table()
            .AddColumn("Parametr")
            .AddColumn("Hodnota");

        table.AddRow("Model", result.SmartaData.DeviceModel ?? "Neznámý");
        table.AddRow("Sériové číslo", result.SmartaData.SerialNumber ?? "Neznámé");
        table.AddRow("Firmware", result.SmartaData.FirmwareVersion ?? "Neznámý");
        table.AddRow("Naběhané hodiny", result.SmartaData.PowerOnHours.ToString());
        table.AddRow("Reallocated", result.SmartaData.ReallocatedSectorCount.ToString());
        table.AddRow("Pending", result.SmartaData.PendingSectorCount.ToString());
        table.AddRow("Uncorrectable", result.SmartaData.UncorrectableErrorCount.ToString());
        table.AddRow("Teplota", $"{result.SmartaData.Temperature:F1} °C");
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
                AnsiConsole.MarkupLine($" - {warning}");
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
            AnsiConsole.MarkupLine("[red]Nebyl nalezen žádný disk.[/]");
            WaitForReturn();
            return;
        }

        var drive = AnsiConsole.Prompt(
            new SelectionPrompt<CoreDriveInfo>()
                .Title("Vyberte disk pro test")
                .PageSize(10)
                .UseConverter(info => $"{info.Name} ({info.Path})")
                .AddChoices(drives));

        var profileChoice = AnsiConsole.Prompt(
            new SelectionPrompt<string>()
                .Title("Zvolte profil testu")
                .AddChoices("HDD - úplný test (zápis)", "SSD - rychlý test (čtení)"));

        var profile = profileChoice.StartsWith("HDD", StringComparison.Ordinal)
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
            AnsiConsole.MarkupLine($"[yellow]{result.Notes}[/]");
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

        var format = AnsiConsole.Prompt(
            new SelectionPrompt<string>()
                .Title("Vyberte formát exportu")
                .AddChoices("Text", "HTML", "CSV", "Certifikát (HTML)", "Certifikát (PDF)"));

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
        AnsiConsole.MarkupLine("[yellow]Načítám historii...[/]");
        await Task.Delay(500);
        AnsiConsole.MarkupLine("[red]TODO: Zobrazit historii testů[/]");
        WaitForReturn();
    }

    private async Task CompareMenuAsync()
    {
        AnsiConsole.MarkupLine("[yellow]Načítám data pro porovnání...[/]");
        await Task.Delay(500);
        AnsiConsole.MarkupLine("[red]TODO: Zobrazit porovnání disků[/]");
        WaitForReturn();
    }

    private async Task SettingsMenuAsync()
    {
        var language = AnsiConsole.Prompt(
            new SelectionPrompt<string>()
                .Title("Vyberte jazyk / Select Language")
                .PageSize(5)
                .AddChoices(new[] { "Čeština", "English" }));

        AnsiConsole.MarkupLine($"[green]Jazyk nastaven na: {language}[/]");
        await Task.Delay(1000);
    }

    private static void WaitForReturn()
    {
        AnsiConsole.MarkupLine("[dim]Stiskněte libovolnou klávesu pro návrat...[/]");
        ReadKey(true);
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
