using DiskChecker.Core.Models;
using DiskChecker.Application.Services;
using Spectre.Console;
using System.IO;

namespace DiskChecker.UI.Console.Pages;

/// <summary>
/// Displays comprehensive test result report with analytics and export options.
/// </summary>
public static class TestResultReportPage
{
    private static readonly System.Text.Json.JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true
    };

    /// <summary>
    /// Displays the complete test result analysis with options to export or return.
    /// </summary>
    public static async Task DisplayResultAsync(SurfaceTestResult result)
    {
        var analysisService = new TestReportAnalysisService();
        var analytics = analysisService.AnalyzeResult(result);

        // Clear screen and display beautiful Spectre report
        DisplaySpectreReport(result, analytics);

        AnsiConsole.WriteLine();

        // Export options - translate to Czech
        var option = AnsiConsole.Prompt(
            new SelectionPrompt<string>()
                .Title("[bold yellow]📊 Co chcete udělat?[/]")
                .AddChoices(new[]
                {
                    "📋 Zobrazit JSON Report",
                    "📊 Exportovat jako CSV (pro grafy)",
                    "💾 Uložit Certifikát (JSON)",
                    "🏷️ Vytisknout štítek disku",
                    "🔄 Zpět do Menu"
                }));

        switch (option)
        {
            case "📋 Zobrazit JSON Report":
                await ViewJsonReportAsync(result, analysisService, analytics);
                break;

            case "📊 Exportovat jako CSV (pro grafy)":
                await ExportCsvAsync(result, analysisService);
                break;

            case "💾 Uložit Certifikát (JSON)":
                await SaveCertificateAsync(result, analysisService, analytics);
                break;

            case "🏷️ Vytisknout štítek disku":
                PrintDiskLabel(result, analytics);
                break;

            case "🔄 Zpět do Menu":
                return;
        }

        // After action, return to this menu
        AnsiConsole.MarkupLine("\n[dim]Stiskněte libovolnou klávesu...[/]");
        System.Console.ReadKey(true);
        await DisplayResultAsync(result);
    }

    /// <summary>
    /// Displays a beautiful Spectre.Console report with colored formatting.
    /// </summary>
    private static void DisplaySpectreReport(SurfaceTestResult result, TestReportAnalysisService.TestAnalytics analytics)
    {
        AnsiConsole.Clear();
        
        // Header
        var titlePanel = new Panel(new Text("ZPRÁVA O TESTU DISKU", new Style(Color.Cyan, decoration: Decoration.Bold)))
        {
            Border = BoxBorder.Heavy,
            Padding = new Padding(2, 1),
            BorderStyle = new Style(Color.Cyan)
        };
        AnsiConsole.Write(titlePanel);
        AnsiConsole.WriteLine();

        // Overall Grade Section
        Color gradeColor = GetGradeColor(analytics.Grade);
        string gradeEmoji = GetGradeEmoji(analytics.Grade);
        
        AnsiConsole.MarkupLine($"[bold {GetColorName(gradeColor)}]{gradeEmoji} Celkové hodnocení: {analytics.Grade}  Skóre: {analytics.Score}/100[/]");
        AnsiConsole.MarkupLine($"[{GetColorName(GetScoreColor(analytics.Score))}]{analytics.HealthAssessment}[/]");
        AnsiConsole.WriteLine();

        // Disk Information Section
        var diskInfo = new Table()
            .Border(TableBorder.Rounded)
            .BorderColor(Color.Blue)
            .Title("[bold blue]ℹ️  INFORMACE O DISKU[/]", new Style(Color.Blue))
            .AddColumn(new TableColumn("[bold blue]Vlastnost[/]").Width(18))
            .AddColumn(new TableColumn("[bold blue]Hodnota[/]").Width(60));

        diskInfo.AddRow("Model:", result.DriveModel ?? "N/A");
        diskInfo.AddRow("Sériové číslo:", result.DriveSerialNumber ?? "N/A");
        diskInfo.AddRow("Výrobce:", result.DriveManufacturer ?? "N/A");
        diskInfo.AddRow("Rozhraní:", result.DriveInterface ?? "N/A");
        diskInfo.AddRow("Kapacita:", FormatBytes(result.DriveTotalBytes));
        diskInfo.AddRow("Teplota:", result.CurrentTemperatureCelsius.HasValue ? $"{result.CurrentTemperatureCelsius}°C" : "N/A");
        diskInfo.AddRow("Provozní hodiny:", result.PowerOnHours.HasValue ? $"{result.PowerOnHours} h" : "N/A");

        AnsiConsole.Write(diskInfo);
        AnsiConsole.WriteLine();

        // Performance Metrics Section
        var metrics = new Table()
            .Border(TableBorder.Rounded)
            .BorderColor(Color.Green)
            .Title("[bold green]⚡ METRIKY VÝKONU[/]", new Style(Color.Green))
            .AddColumn(new TableColumn("[bold green]Metrika[/]").Width(22))
            .AddColumn(new TableColumn("[bold green]Hodnota[/]").Width(40));

        metrics.AddRow("Doba testu:", analytics.Duration.ToString("hh\\:mm\\:ss"));
        metrics.AddRow("Testováno:", FormatBytes(result.TotalBytesTested));
        metrics.AddRow("Průměrná rychlost:", $"{result.AverageSpeedMbps:F2} MB/s");
        metrics.AddRow("Maximální rychlost:", $"{result.PeakSpeedMbps:F2} MB/s (s cache)");
        metrics.AddRow("Reálná max (bez cache):", $"{analytics.FilteredMaxSpeedMbps:F2} MB/s");
        metrics.AddRow("Reálná min:", $"{analytics.FilteredMinSpeedMbps:F2} MB/s");
        metrics.AddRow("Variabilita rychlosti:", $"±{analytics.SpeedStdDev:F2} MB/s");

        AnsiConsole.Write(metrics);
        AnsiConsole.WriteLine();

        // Reliability Section
        Color reliabilityColor = result.ErrorCount == 0 ? Color.Green : Color.Red;
        var reliability = new Table()
            .Border(TableBorder.Rounded)
            .BorderColor(reliabilityColor)
            .Title("[bold]🔍 SPOLEHLIVOST[/]", new Style(reliabilityColor))
            .AddColumn(new TableColumn("[bold]Kontrola[/]").Width(20))
            // Widen second column slightly to avoid right border being drawn inside content on narrow consoles
            .AddColumn(new TableColumn("[bold]Výsledek[/]").Width(44));

        reliability.AddRow("Chyby:", result.ErrorCount == 0 ? "0 ✅ Žádné chyby" : $"{result.ErrorCount} ❌ Chyby nalezeny!");
        
        string anomalyQuality = analytics.AnomalyPercentage < 5 ? "✅ Vynikající" : analytics.AnomalyPercentage < 15 ? "⚠️ Dobrá" : "❌ Slabá";
        reliability.AddRow("Anomálie:", $"{analytics.AnomalyPercentage:F1}% vzorků ({anomalyQuality})");

        if (result.ReallocatedSectors.HasValue && result.ReallocatedSectors > 0)
        {
            reliability.AddRow("⚠️ Realokované sektory:", $"{result.ReallocatedSectors} (opotřebení zjištěno)");
        }

        AnsiConsole.Write(reliability);
        AnsiConsole.WriteLine();

        // Detected Issues Section
        if (analytics.DetectedAnomalies.Any())
        {
            AnsiConsole.MarkupLine("[bold orange1]⚠️  POZNAMENANÁ ZJIŠTĚNÍ:[/]");
            foreach (var anomaly in analytics.DetectedAnomalies)
            {
                AnsiConsole.MarkupLine($"[orange1]  • {anomaly}[/]");
            }
            AnsiConsole.WriteLine();
        }

        // Recommendations Section
        Color recommendationColor = analytics.Score >= 90 ? Color.Green : analytics.Score >= 80 ? Color.Yellow : Color.Red;
        string recommendationEmoji = analytics.Score >= 90 ? "✅" : analytics.Score >= 80 ? "⚠️" : "❌";
        
        string recommendation = analytics.Score switch
        {
            >= 90 => "Disk je vhodný pro produkční použití a ukládání dat. Vše je v pořádku.",
            >= 80 => "Disk je použitelné, ale monitorujte problémy. Pravidelně zálohujte data.",
            >= 70 => "Disk má problémy. Zvažte výměnu. Nepoužívejte pro kritická data.",
            _ => "Disk je nespolehlivý. Doporučuje se okamžitá výměna."
        };

        var recommendationPanel = new Panel(new Text($"{recommendationEmoji} {recommendation}", new Style(recommendationColor, decoration: Decoration.Bold)))
        {
            Border = BoxBorder.Rounded,
            Padding = new Padding(2, 1),
            BorderStyle = new Style(recommendationColor)
        };
        AnsiConsole.Write(recommendationPanel);
        AnsiConsole.WriteLine();

        // Graphs Section
        AnsiConsole.MarkupLine("[bold cyan]═══════════════════════════════════════════════════════════════[/]");
        AnsiConsole.MarkupLine("[bold cyan]                    📊 VIZUÁLNÍ ANALÝZA                       [/]");
        AnsiConsole.MarkupLine("[bold cyan]═══════════════════════════════════════════════════════════════[/]");
        AnsiConsole.WriteLine();

        // Display speed graph
        DisplaySpeedGraph(result);

        // Display speed distribution
        DisplaySpeedDistribution(result, analytics);

        // Display test statistics
        DisplayTestStatistics(result, analytics);

        // Footer
        AnsiConsole.MarkupLine($"[dim]Vygenerováno: {DateTime.Now:yyyy-MM-dd HH:mm:ss} (ID testu: {result.TestId})[/]");
    }

    private static async Task ViewJsonReportAsync(SurfaceTestResult result, TestReportAnalysisService analysisService, TestReportAnalysisService.TestAnalytics analytics)
    {
        AnsiConsole.Clear();
        var jsonReport = analysisService.GenerateJsonReport(result, analytics);
        
        AnsiConsole.MarkupLine("[bold cyan]📄 JSON Report:[/]\n");
        AnsiConsole.Write(new Panel(jsonReport)
        {
            Border = BoxBorder.Rounded,
            Padding = new Padding(1, 0)
        });

        AnsiConsole.WriteLine();
        if (AnsiConsole.Confirm("💾 Uložit do souboru?"))
        {
            var filename = Path.Combine(
                AppContext.BaseDirectory,
                $"test_report_{result.TestId}_{DateTime.Now:yyyyMMdd_HHmmss}.json");
            
            await File.WriteAllTextAsync(filename, jsonReport);
            AnsiConsole.MarkupLine($"[green]✅ Uloženo: {filename}[/]");
        }
    }

    private static async Task ExportCsvAsync(SurfaceTestResult result, TestReportAnalysisService analysisService)
    {
        AnsiConsole.Clear();
        var csvReport = analysisService.GenerateCsvReport(result);

        AnsiConsole.MarkupLine("[bold cyan]📊 CSV Export (pro tvorbu grafů):[/]\n");
        AnsiConsole.MarkupLine("[yellow]Sloupce: TimeSeconds, OffsetBytes, ThroughputMbps, ErrorCount[/]\n");

        var filename = Path.Combine(
            AppContext.BaseDirectory,
            $"test_data_{result.TestId}_{DateTime.Now:yyyyMMdd_HHmmss}.csv");

        await File.WriteAllTextAsync(filename, csvReport);
        AnsiConsole.MarkupLine($"[green]✅ CSV uložen: {filename}[/]");
        AnsiConsole.MarkupLine("[dim]Lze importovat do Excelu, Pythonu, nebo online nástrojů na tvorbu grafů[/]");
    }

    private static async Task SaveCertificateAsync(SurfaceTestResult result, TestReportAnalysisService analysisService, TestReportAnalysisService.TestAnalytics analytics)
    {
        AnsiConsole.Clear();
        
        // Generate certificate content
        var certContent = GenerateCertificateContent(result, analytics);
        
        var filename = Path.Combine(
            AppContext.BaseDirectory,
            $"certificate_{result.TestId}_{DateTime.Now:yyyyMMdd}.json");

        await File.WriteAllTextAsync(filename, certContent);
        
        AnsiConsole.MarkupLine("[green]✅ Certifikát uložen[/]");
        AnsiConsole.MarkupLine($"[cyan]📁 {filename}[/]");
        AnsiConsole.MarkupLine("[dim]Tento certifikát lze použít pro porovnání disků a vytváření grafů[/]");
    }

    private static string GenerateCertificateContent(SurfaceTestResult result, TestReportAnalysisService.TestAnalytics analytics)
    {
        var certificate = new
        {
            certificateId = Guid.NewGuid().ToString(),
            issuedDate = DateTime.Now.ToString("yyyy-MM-dd"),
            testId = result.TestId,
            diskInfo = new
            {
                model = result.DriveModel,
                serialNumber = result.DriveSerialNumber,
                manufacturer = result.DriveManufacturer,
                interface_ = result.DriveInterface,
                capacityGb = result.DriveTotalBytes / (1024L * 1024L * 1024L)
            },
            testResults = new
            {
                grade = analytics.Grade,
                score = analytics.Score,
                verdict = analytics.HealthAssessment
            },
            performance = new
            {
                averageSpeedMbps = Math.Round(result.AverageSpeedMbps, 2),
                maxSpeedMbps = Math.Round(analytics.FilteredMaxSpeedMbps, 2),
                minSpeedMbps = Math.Round(analytics.FilteredMinSpeedMbps, 2),
                stabilityIndex = Math.Round(100 - analytics.AnomalyPercentage, 2)
            },
            reliability = new
            {
                errorCount = result.ErrorCount,
                status = result.ErrorCount == 0 ? "PASS" : "FAIL"
            },
            testDuration = analytics.Duration.ToString("hh\\:mm\\:ss"),
            dataProcessed = result.TotalBytesTested / (1024L * 1024L * 1024L) + " GB"
        };

        return System.Text.Json.JsonSerializer.Serialize(certificate, JsonOptions);
    }

    /// <summary>
    /// Helper to get color based on grade.
    /// </summary>
    private static Color GetGradeColor(string grade) => grade switch
    {
        "A" => Color.Green,
        "B" => Color.Yellow,
        "C" => Color.Orange1,
        "D" => Color.Red,
        _ => Color.DarkRed
    };

    /// <summary>
    /// Helper to get emoji based on grade.
    /// </summary>
    private static string GetGradeEmoji(string grade) => grade switch
    {
        "A" => "🟢",
        "B" => "🟡",
        "C" => "🟠",
        "D" => "🔴",
        _ => "⚫"
    };

    /// <summary>
    /// Helper to get color based on score.
    /// </summary>
    private static Color GetScoreColor(int score) => score switch
    {
        >= 90 => Color.Green,
        >= 80 => Color.Yellow,
        >= 70 => Color.Orange1,
        >= 60 => Color.Red,
        _ => Color.DarkRed
    };

    /// <summary>
    /// Helper to format bytes to readable format.
    /// </summary>
    private static string FormatBytes(long bytes)
    {
        string[] sizes = { "B", "KB", "MB", "GB", "TB" };
        double len = bytes;
        int order = 0;
        while (len >= 1024 && order < sizes.Length - 1)
        {
            order++;
            len = len / 1024;
        }
        return $"{len:0.##} {sizes[order]}";
    }

    /// <summary>
    /// Helper to convert Color to color name string for markup.
    /// </summary>
    private static string GetColorName(Color color) => color switch
    {
        _ when color == Color.Green => "green",
        _ when color == Color.Yellow => "yellow",
        _ when color == Color.Orange1 => "orange1",
        _ when color == Color.Red => "red",
        _ when color == Color.Cyan => "cyan",
        _ when color == Color.DarkRed => "darkred",
        _ when color == Color.Blue => "blue",
        _ => "white"
    };

    /// <summary>
    /// Displays a visual graph of speed throughout the test.
    /// </summary>
    private static void DisplaySpeedGraph(SurfaceTestResult result)
    {
        if (!result.Samples.Any())
        {
            AnsiConsole.MarkupLine("[yellow]Nejsou k dispozici data pro graf[/]");
            return;
        }

        AnsiConsole.MarkupLine("[bold cyan]📈 GRAF RYCHLOSTI V PRŮBĚHU TESTU[/]");
        AnsiConsole.WriteLine();

        // Normalizujeme data pro ASCII graf
        var samples = result.Samples.ToList();
        double maxSpeed = samples.Max(s => s.ThroughputMbps);
        double minSpeed = samples.Min(s => s.ThroughputMbps);
        
        const int graphHeight = 10;
        const int graphWidth = 60;
        
        // Vytvoříme matici pro graf
        var graph = new string[graphHeight];
        for (int i = 0; i < graphHeight; i++)
            graph[i] = new string(' ', graphWidth);

        // Normalizujeme vzorky na šířku grafu
        int step = Math.Max(1, samples.Count / graphWidth);
        
        for (int x = 0; x < graphWidth && x * step < samples.Count; x++)
        {
            var sample = samples[Math.Min(x * step, samples.Count - 1)];
            double normalized = (sample.ThroughputMbps - minSpeed) / (maxSpeed - minSpeed);
            int y = (int)((1.0 - normalized) * (graphHeight - 1));
            y = Math.Max(0, Math.Min(graphHeight - 1, y));
            
            var row = graph[y].ToCharArray();
            row[x] = '█';
            graph[y] = new string(row);
        }

        // Vykreslíme graf s osami
        AnsiConsole.MarkupLine($"[green]{maxSpeed:F0} MB/s[/] ┤");
        for (int i = 0; i < graphHeight; i++)
        {
            string yLabel = i == graphHeight - 1 ? $"[green]{minSpeed:F0} MB/s[/]" : "        ";
            AnsiConsole.MarkupLine($"{yLabel} │ [yellow]{graph[i]}[/]");
        }
        AnsiConsole.MarkupLine("          └" + new string('─', graphWidth));
        AnsiConsole.MarkupLine($"            [dim]0% času testu{new string(' ', graphWidth - 18)}100%[/]");
        AnsiConsole.WriteLine();
    }

    /// <summary>
    /// Displays a bar chart of speed distribution bucketed into ranges.
    /// </summary>
    private static void DisplaySpeedDistribution(SurfaceTestResult result, TestReportAnalysisService.TestAnalytics analytics)
    {
        if (!result.Samples.Any())
            return;

        AnsiConsole.MarkupLine("[bold cyan]📊 DISTRIBUCE RYCHLOSTÍ[/]");
        AnsiConsole.WriteLine();

        var samples = result.Samples.ToList();
        double maxSpeed = samples.Max(s => s.ThroughputMbps);
        
        // Vytvořit buckets (10 rozsahů)
        const int bucketCount = 10;
        var buckets = new int[bucketCount];
        double bucketSize = maxSpeed / bucketCount;

        foreach (var sample in samples)
        {
            int bucketIndex = Math.Min((int)(sample.ThroughputMbps / bucketSize), bucketCount - 1);
            buckets[bucketIndex]++;
        }

        // Najít max count pro normalizaci
        int maxCount = buckets.Max();
        
        for (int i = 0; i < bucketCount; i++)
        {
            double rangeStart = i * bucketSize;
            double rangeEnd = (i + 1) * bucketSize;
            int count = buckets[i];
            double percentage = (double)count / samples.Count * 100;
            
            // Vytvořit bar
            int barLength = (int)((double)count / maxCount * 30);
            string bar = new string('█', barLength);
            
            // Obarvit podle výkonu
            string color = (rangeStart / maxSpeed) switch
            {
                >= 0.8 => "green",
                >= 0.6 => "yellow",
                >= 0.4 => "orange1",
                _ => "red"
            };

            AnsiConsole.MarkupLine($"  [{color}]{rangeStart:F0}-{rangeEnd:F0} MB/s[/] │ {bar} {count} vzorků ({percentage:F1}%)");
        }
        AnsiConsole.WriteLine();
    }

    /// <summary>
    /// Displays statistics and error summary.
    /// </summary>
    private static void DisplayTestStatistics(SurfaceTestResult result, TestReportAnalysisService.TestAnalytics analytics)
    {
        AnsiConsole.MarkupLine("[bold cyan]📈 STATISTIKY TESTŮ[/]");
        AnsiConsole.WriteLine();

        var stats = new Table()
            .Border(TableBorder.Rounded)
            .BorderColor(Color.Magenta)
            .AddColumn(new TableColumn("[bold magenta]Statistica[/]").Width(25))
            .AddColumn(new TableColumn("[bold magenta]Hodnota[/]").Width(35));

        // Vzorky
        stats.AddRow("Počet vzorků:", result.Samples.Count.ToString());
        
        // Rychlost
        stats.AddRow("Minimální rychlost:", $"{analytics.FilteredMinSpeedMbps:F2} MB/s");
        stats.AddRow("Maximální rychlost:", $"{analytics.FilteredMaxSpeedMbps:F2} MB/s");
        stats.AddRow("Průměrná rychlost:", $"{result.AverageSpeedMbps:F2} MB/s");
        stats.AddRow("Standardní odchylka:", $"±{analytics.SpeedStdDev:F2} MB/s");
        
        // Chyby
        string errorStatus = result.ErrorCount == 0 ? "[green]0 ✅[/]" : $"[red]{result.ErrorCount} ❌[/]";
        stats.AddRow("Chyby:", errorStatus);
        
        // Anomálie
        string anomalyStatus = analytics.AnomalyPercentage < 5 ? 
            $"[green]{analytics.AnomalyPercentage:F1}% ✅[/]" : 
            $"[orange1]{analytics.AnomalyPercentage:F1}% ⚠️[/]";
        stats.AddRow("Anomálie:", anomalyStatus);
        
        // Čas
        stats.AddRow("Doba trvání:", analytics.Duration.ToString("hh\\:mm\\:ss"));

        AnsiConsole.Write(stats);
        AnsiConsole.WriteLine();
    }

    private static void PrintDiskLabel(SurfaceTestResult result, TestReportAnalysisService.TestAnalytics analytics)
    {
        AnsiConsole.WriteLine();
        AnsiConsole.MarkupLine("[bold cyan]═══════════════════════════════════════════════════════════════[/]");
        AnsiConsole.MarkupLine("[bold cyan]                    🏷️  ŠTÍTEK DISKU                               [/]");
        AnsiConsole.MarkupLine("[bold cyan]═══════════════════════════════════════════════════════════════[/]");
        AnsiConsole.WriteLine();

        var labelTable = new Table()
            .Border(TableBorder.Rounded)
            .BorderColor(Color.Cyan)
            .AddColumn(new TableColumn("[bold]Vlastnost[/]").Width(20))
            .AddColumn(new TableColumn("[bold]Hodnota[/]").Width(40));

        labelTable.AddRow("Model:", $"[white]{Markup.Escape(result.DriveModel ?? "N/A")}[/]");
        labelTable.AddRow("Sériové číslo:", $"[yellow]{Markup.Escape(result.DriveSerialNumber ?? "N/A")}[/]");
        labelTable.AddRow("Kapacita:", $"[green]{FormatBytes(result.DriveTotalBytes)}[/]");
        labelTable.AddRow("Testováno:", $"[green]{FormatBytes(result.TotalBytesTested)}[/]");
        labelTable.AddRow("Datum testu:", $"[cyan]{DateTime.Now:dd.MM.yyyy HH:mm}[/]");
        
        var gradeColor = GetGradeColor(analytics.Grade);
        var gradeEmoji = GetGradeEmoji(analytics.Grade);
        labelTable.AddRow("Známka:", $"[{GetColorName(gradeColor)}]{gradeEmoji} {analytics.Grade}[/]");
        labelTable.AddRow("Skóre:", $"[white]{analytics.Score}/100[/]");
        labelTable.AddRow("Rychlost:", $"[cyan]{result.AverageSpeedMbps:F0} MB/s[/]");
        labelTable.AddRow("Teplota:", result.CurrentTemperatureCelsius.HasValue ? $"[yellow]{result.CurrentTemperatureCelsius}°C[/]" : "[dim]N/A[/]");
        
        string errorStatus = result.ErrorCount == 0 ? "[green]Žádné ✅[/]" : $"[red]{result.ErrorCount} ❌[/]";
        labelTable.AddRow("Chyby:", errorStatus);

        AnsiConsole.Write(labelTable);
        AnsiConsole.WriteLine();

        AnsiConsole.MarkupLine("[bold yellow]Doporučení pro tisk:[/]");
        AnsiConsole.MarkupLine("  [dim]• Standardní štítek (50x25mm) - pro USB flash disky[/]");
        AnsiConsole.MarkupLine("  [dim]• Malý štítek (74x52mm) - pro externí disky[/]");
        AnsiConsole.MarkupLine("  [dim]• Velký štítek (A5) - pro archivaci[/]");
        AnsiConsole.WriteLine();

        var labelChoice = AnsiConsole.Prompt(
            new SelectionPrompt<string>()
                .Title("[bold yellow]Vyberte formát štítku:[/]")
                .AddChoices(new[]
                {
                    "📋 Malý štítek (74x52mm)",
                    "📄 Velký štítek (A5) - vhodný pro tiskárnu",
                    "💾 Uložit jako textový soubor",
                    "🔄 Zpět"
                }));

        switch(labelChoice)
        {
            case "📋 Malý štítek (74x52mm)":
                PrintSmallLabel(result, analytics);
                break;
            case "📄 Velký štítek (A5) - vhodný pro tiskárnu":
                PrintLargeLabel(result, analytics);
                break;
            case "💾 Uložit jako textový soubor":
                SaveLabelToFile(result, analytics);
                break;
            case "🔄 Zpět":
                return;
        }
    }

    private static void PrintSmallLabel(SurfaceTestResult result, TestReportAnalysisService.TestAnalytics analytics)
    {
        AnsiConsole.WriteLine();
        AnsiConsole.MarkupLine("[bold green]═══════════════════════════════════════════[/]");
        AnsiConsole.MarkupLine($"[bold green]  DiskChecker ─ {DateTime.Now:dd.MM.yy}[/]");
        var modelName = result.DriveModel ?? "N/A";
        if(modelName.Length > 40) modelName = modelName[..37] + "...";
        AnsiConsole.MarkupLine($"[white]  {Markup.Escape(modelName)}[/]");
        AnsiConsole.MarkupLine($"[yellow]  SN: {Markup.Escape(result.DriveSerialNumber ?? "N/A")}[/]");
        var gradeColor = GetGradeColor(analytics.Grade);
        AnsiConsole.MarkupLine($"[{GetColorName(gradeColor)}]  ZNÁMKA: {analytics.Grade} ({analytics.Score}pts)[/]");
        AnsiConsole.MarkupLine($"[cyan]  {result.AverageSpeedMbps:F0} MB/s | {FormatBytes(result.DriveTotalBytes)}[/]");
        AnsiConsole.MarkupLine("[bold green]═══════════════════════════════════════════[/]");
        AnsiConsole.WriteLine();
        AnsiConsole.MarkupLine("[dim]Vynejte tento text a vytiskněte jako štítek 74x52mm[/]");
    }

    private static void PrintLargeLabel(SurfaceTestResult result, TestReportAnalysisService.TestAnalytics analytics)
    {
        var modelName = result.DriveModel ?? "N/A";
        if(modelName.Length > 50) modelName = modelName[..47] + "...";
        var gradeColor = GetGradeColor(analytics.Grade);
        var gradeEmoji = GetGradeEmoji(analytics.Grade);
        var temp = result.CurrentTemperatureCelsius ?? 0;
        var errorText = result.ErrorCount == 0 ? "Žádné ✅" : $"{result.ErrorCount} ❌";
        var errorColor = result.ErrorCount == 0 ? "green" : "red";
        
        AnsiConsole.WriteLine();
        AnsiConsole.MarkupLine("[bold cyan]╔════════════════════════════════════════════════════════════════╗[/]");
        AnsiConsole.MarkupLine("[bold cyan]║                    🖴 DISKCHECKER CERTIFIKÁT                  ║[/]");
        AnsiConsole.MarkupLine("[bold cyan]╠════════════════════════════════════════════════════════════════╣[/]");
        AnsiConsole.MarkupLine($"[bold cyan]║[/] Model:       [white]{Markup.Escape(modelName),-50}[/][bold cyan]║[/]");
        AnsiConsole.MarkupLine($"[bold cyan]║[/] Sériové č.:  [yellow]{Markup.Escape(result.DriveSerialNumber ?? "N/A"),-50}[/][bold cyan]║[/]");
        AnsiConsole.MarkupLine($"[bold cyan]║[/] Kapacita:    [green]{Markup.Escape(FormatBytes(result.DriveTotalBytes)),-50}[/][bold cyan]║[/]");
        AnsiConsole.MarkupLine("[bold cyan]╠════════════════════════════════════════════════════════════════╣[/]");
        AnsiConsole.MarkupLine($"[bold cyan]║[/] [{GetColorName(gradeColor)}]ZNÁMKA: {analytics.Grade} {gradeEmoji}   SKÓRE: {analytics.Score}/100[/][bold cyan]║[/]");
        AnsiConsole.MarkupLine($"[bold cyan]║[/] Rychlost:    [cyan]{result.AverageSpeedMbps:F0} MB/s (průměr)[/][bold cyan]║[/]");
        AnsiConsole.MarkupLine($"[bold cyan]║[/] Teplota:      [yellow]{temp}°C[/][bold cyan]║[/]");
        AnsiConsole.MarkupLine($"[bold cyan]║[/] Chyby:       [{errorColor}]{errorText}[/][bold cyan]║[/]");
        AnsiConsole.MarkupLine("[bold cyan]╠════════════════════════════════════════════════════════════════╣[/]");
        AnsiConsole.MarkupLine($"[bold cyan]║[/] Datum testu: [white]{DateTime.Now:dd.MM.yyyy HH:mm}[/][bold cyan]║[/]");
        AnsiConsole.MarkupLine($"[bold cyan]║[/] Test ID:     [dim]{result.TestId}[/][bold cyan]║[/]");
        AnsiConsole.MarkupLine("[bold cyan]╚════════════════════════════════════════════════════════════════╝[/]");
        AnsiConsole.WriteLine();
        AnsiConsole.MarkupLine("[dim]Vytiskněte jako A5 nebo A4 certifikát[/]");
    }

    private static void SaveLabelToFile(SurfaceTestResult result, TestReportAnalysisService.TestAnalytics analytics)
    {
        var filename = Path.Combine(
            AppContext.BaseDirectory,
            $"disk_label_{result.DriveSerialNumber ?? "unknown"}_{DateTime.Now:yyyyMMdd_HHmm}.txt");

        var sb = new System.Text.StringBuilder();
        sb.AppendLine("═══════════════════════════════════════════════════════════════");
        sb.AppendLine("                    DISKCHECKER - ŠTÍTEK DISKU");
        sb.AppendLine("═══════════════════════════════════════════════════════════════");
        sb.AppendLine($"Model:          {result.DriveModel ?? "N/A"}");
        sb.AppendLine($"Sériové číslo: {result.DriveSerialNumber ?? "N/A"}");
        sb.AppendLine($"Kapacita:       {FormatBytes(result.DriveTotalBytes)}");
        sb.AppendLine("───────────────────────────────────────────────────────────────");
        sb.AppendLine($"ZNÁMKA:         {analytics.Grade} ({analytics.Score}/100)");
        sb.AppendLine($"Rychlost:       {result.AverageSpeedMbps:F0} MB/s (průměr)");
        sb.AppendLine($"Teplota:        {result.CurrentTemperatureCelsius ?? 0}°C");
        sb.AppendLine($"Chyby:          {result.ErrorCount}");
        sb.AppendLine("───────────────────────────────────────────────────────────────");
        sb.AppendLine($"Datum testu:   {DateTime.Now:dd.MM.yyyy HH:mm}");
        sb.AppendLine($"Test ID:       {result.TestId}");
        sb.AppendLine("═══════════════════════════════════════════════════════════════");

        File.WriteAllText(filename, sb.ToString());
        AnsiConsole.MarkupLine($"[green]✅ Štítek uložen: {filename}[/]");
    }
}
