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
        AnsiConsole.Write(new FigletText("VÝSLEDEK TESTU").Centered().Color(Color.Cyan));
        AnsiConsole.WriteLine();

        // Overall Grade Panel
        Color gradeColor = GetGradeColor(analytics.Grade);
        string gradeEmoji = GetGradeEmoji(analytics.Grade);
        
        var gradePanel = new Panel(new Markup($"[{GetColorName(gradeColor)}]{gradeEmoji} Celkové hodnocení: {analytics.Grade}  Skóre: {analytics.Score}/100[/]\n[{GetColorName(GetScoreColor(analytics.Score))}]{analytics.HealthAssessment}[/]"))
        {
            Border = BoxBorder.Rounded,
            BorderStyle = new Style(gradeColor),
            Padding = new Padding(2, 1),
            Header = new PanelHeader("[bold white]📊 HODNOCENÍ[/]")
        };
        AnsiConsole.Write(gradePanel);
        AnsiConsole.WriteLine();

        // Disk Information Section
        var diskInfo = new Table()
            .Border(TableBorder.Rounded)
            .BorderColor(Color.Blue)
            .AddColumn(new TableColumn("[bold blue]Vlastnost[/]").Width(20))
            .AddColumn(new TableColumn("[bold blue]Hodnota[/]").Width(50));

        diskInfo.AddRow("Model:", Markup.Escape(result.DriveModel ?? "N/A"));
        diskInfo.AddRow("Sériové číslo:", Markup.Escape(result.DriveSerialNumber ?? "N/A"));
        diskInfo.AddRow("Výrobce:", Markup.Escape(result.DriveManufacturer ?? "N/A"));
        diskInfo.AddRow("Rozhraní:", Markup.Escape(result.DriveInterface ?? "N/A"));
        diskInfo.AddRow("Kapacita:", FormatBytes(result.DriveTotalBytes));
        diskInfo.AddRow("Teplota:", result.CurrentTemperatureCelsius.HasValue ? $"{result.CurrentTemperatureCelsius}°C" : "N/A");
        diskInfo.AddRow("Provozní hodiny:", result.PowerOnHours.HasValue ? $"{result.PowerOnHours} h" : "N/A");

        var diskPanel = new Panel(diskInfo)
        {
            Border = BoxBorder.Rounded,
            BorderStyle = new Style(Color.Blue),
            Header = new PanelHeader("[bold blue]ℹ️ INFORMACE O DISKU[/]")
        };
        AnsiConsole.Write(diskPanel);
        AnsiConsole.WriteLine();

        // Performance Metrics Section
        var metrics = new Table()
            .Border(TableBorder.Rounded)
            .BorderColor(Color.Green)
            .AddColumn(new TableColumn("[bold green]Metrika[/]").Width(25))
            .AddColumn(new TableColumn("[bold green]Hodnota[/]").Width(45));

        metrics.AddRow("Doba testu:", analytics.Duration.ToString("hh\\:mm\\:ss"));
        metrics.AddRow("Testováno:", FormatBytes(result.TotalBytesTested));
        metrics.AddRow("Průměrná rychlost:", $"{result.AverageSpeedMbps:F2} MB/s");
        metrics.AddRow("Maximální rychlost:", $"{result.PeakSpeedMbps:F2} MB/s (s cache)");
        metrics.AddRow("Reálná max (bez cache):", $"{analytics.FilteredMaxSpeedMbps:F2} MB/s");
        metrics.AddRow("Reálná min:", $"{analytics.FilteredMinSpeedMbps:F2} MB/s");
        metrics.AddRow("Variabilita rychlosti:", $"±{analytics.SpeedStdDev:F2} MB/s");

        var metricsPanel = new Panel(metrics)
        {
            Border = BoxBorder.Rounded,
            BorderStyle = new Style(Color.Green),
            Header = new PanelHeader("[bold green]⚡ METRIKY VÝKONU[/]")
        };
        AnsiConsole.Write(metricsPanel);
        AnsiConsole.WriteLine();

        // Reliability Section
        Color reliabilityColor = result.ErrorCount == 0 ? Color.Green : Color.Red;
        var reliability = new Table()
            .Border(TableBorder.Rounded)
            .BorderColor(reliabilityColor)
            .AddColumn(new TableColumn("[bold]Kontrola[/]").Width(20))
            .AddColumn(new TableColumn("[bold]Výsledek[/]").Width(50));

        reliability.AddRow("Chyby:", result.ErrorCount == 0 ? "0 ✅ Žádné chyby" : $"{result.ErrorCount} ❌ Chyby nalezeny!");
        
        string anomalyQuality = analytics.AnomalyPercentage < 5 ? "✅ Vynikající" : analytics.AnomalyPercentage < 15 ? "⚠️ Dobrá" : "❌ Slabá";
        reliability.AddRow("Anomálie:", $"{analytics.AnomalyPercentage:F1}% vzorků ({anomalyQuality})");

        if (result.ReallocatedSectors.HasValue && result.ReallocatedSectors > 0)
        {
            reliability.AddRow("⚠️ Realokované sektory:", $"{result.ReallocatedSectors} (opotřebení zjištěno)");
        }

        var reliabilityPanel = new Panel(reliability)
        {
            Border = BoxBorder.Rounded,
            BorderStyle = new Style(reliabilityColor),
            Header = new PanelHeader("[bold]🔍 SPOLEHLIVOST[/]")
        };
        AnsiConsole.Write(reliabilityPanel);
        AnsiConsole.WriteLine();

        // Detected Issues Section
        if (analytics.DetectedAnomalies.Any())
        {
            var issuesPanel = new Panel(string.Join("\n", analytics.DetectedAnomalies.Select(a => $"[orange1]• {a}[/]")))
            {
                Border = BoxBorder.Rounded,
                BorderStyle = new Style(Color.Orange1),
                Header = new PanelHeader("[bold orange1]⚠️ POZNAMENANÁ ZJIŠTĚNÍ[/]")
            };
            AnsiConsole.Write(issuesPanel);
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

        var recommendationPanel = new Panel(new Markup($"[{GetColorName(recommendationColor)}]{recommendationEmoji} {recommendation}[/]"))
        {
            Border = BoxBorder.Rounded,
            BorderStyle = new Style(recommendationColor),
            Header = new PanelHeader("[bold blue]💡 DOPORUČENÍ[/]")
        };
        AnsiConsole.Write(recommendationPanel);
        AnsiConsole.WriteLine();

        // Graphs Section
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
        
        AnsiConsole.Write(new Panel(jsonReport)
        {
            Border = BoxBorder.Rounded,
            Header = new PanelHeader("[bold cyan]📄 JSON Report[/]"),
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

        var samples = result.Samples.ToList();
        double maxSpeed = samples.Max(s => s.ThroughputMbps);
        double minSpeed = samples.Min(s => s.ThroughputMbps);
        
        // Create chart using Spectre.Console BarChart
        var chart = new BarChart()
            .Width(80)
            .Label("[bold cyan]📈 Průběh rychlosti v čase[/]")
            .CenterLabel();

        // Sample 20 points from the data
        int step = Math.Max(1, samples.Count / 20);
        for (int i = 0; i < samples.Count; i += step)
        {
            var sample = samples[i];
            double percent = (double)i / samples.Count * 100;
            chart.AddItem($"{percent:F0}%", sample.ThroughputMbps, GetSpeedColor(sample.ThroughputMbps, maxSpeed));
        }

        AnsiConsole.Write(chart);
        AnsiConsole.WriteLine();
    }

    private static Color GetSpeedColor(double speed, double maxSpeed)
    {
        double ratio = speed / maxSpeed;
        if (ratio >= 0.8) return Color.Green;
        if (ratio >= 0.6) return Color.Yellow;
        if (ratio >= 0.4) return Color.Orange1;
        return Color.Red;
    }

    /// <summary>
    /// Displays a bar chart of speed distribution bucketed into ranges.
    /// </summary>
    private static void DisplaySpeedDistribution(SurfaceTestResult result, TestReportAnalysisService.TestAnalytics analytics)
    {
        if (!result.Samples.Any())
            return;

        var samples = result.Samples.ToList();
        double maxSpeed = samples.Max(s => s.ThroughputMbps);
        
        // Create bucket chart
        var chart = new BarChart()
            .Width(80)
            .Label("[bold cyan]📊 Distribuce rychlostí[/]")
            .CenterLabel();

        // Create 5 buckets
        const int bucketCount = 5;
        var buckets = new int[bucketCount];
        double bucketSize = maxSpeed / bucketCount;

        foreach (var sample in samples)
        {
            int bucketIndex = Math.Min((int)(sample.ThroughputMbps / bucketSize), bucketCount - 1);
            buckets[bucketIndex]++;
        }

        int maxCount = buckets.Max();
        for (int i = 0; i < bucketCount; i++)
        {
            double rangeStart = i * bucketSize;
            double rangeEnd = (i + 1) * bucketSize;
            int count = buckets[i];
            double percentage = (double)count / samples.Count * 100;
            
            string label = $"{rangeStart:F0}-{rangeEnd:F0} MB/s ({percentage:F0}%)";
            Color color = GetBucketColor(i, bucketCount);
            chart.AddItem(label, count, color);
        }

        AnsiConsole.Write(chart);
        AnsiConsole.WriteLine();
    }

    private static Color GetBucketColor(int index, int total)
    {
        double ratio = (double)index / (total - 1);
        if (ratio >= 0.8) return Color.Green;
        if (ratio >= 0.6) return Color.Green1;
        if (ratio >= 0.4) return Color.Yellow;
        if (ratio >= 0.2) return Color.Orange1;
        return Color.Red;
    }

    /// <summary>
    /// Displays statistics and error summary.
    /// </summary>
    private static void DisplayTestStatistics(SurfaceTestResult result, TestReportAnalysisService.TestAnalytics analytics)
    {
        var stats = new Table()
            .Border(TableBorder.Rounded)
            .BorderColor(Color.Magenta)
            .AddColumn(new TableColumn("[bold magenta]Statistika[/]").Width(25))
            .AddColumn(new TableColumn("[bold magenta]Hodnota[/]").Width(50));

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

        var statsPanel = new Panel(stats)
        {
            Border = BoxBorder.Rounded,
            BorderStyle = new Style(Color.Magenta),
            Header = new PanelHeader("[bold magenta]📈 STATISTIKY TESTŮ[/]")
        };
        
        AnsiConsole.Write(statsPanel);
        AnsiConsole.WriteLine();
    }

    private static void PrintDiskLabel(SurfaceTestResult result, TestReportAnalysisService.TestAnalytics analytics)
    {
        AnsiConsole.WriteLine();
        
        var labelTable = new Table()
            .Border(TableBorder.Rounded)
            .BorderColor(Color.Cyan)
            .AddColumn(new TableColumn("[bold]Vlastnost[/]").Width(20))
            .AddColumn(new TableColumn("[bold]Hodnota[/]").Width(40));

        labelTable.AddRow("Model:", Markup.Escape(result.DriveModel ?? "N/A"));
        labelTable.AddRow("Sériové číslo:", Markup.Escape(result.DriveSerialNumber ?? "N/A"));
        labelTable.AddRow("Kapacita:", FormatBytes(result.DriveTotalBytes));
        labelTable.AddRow("Testováno:", FormatBytes(result.TotalBytesTested));
        labelTable.AddRow("Datum testu:", DateTime.Now.ToString("dd.MM.yyyy HH:mm"));
        
        var gradeColor = GetGradeColor(analytics.Grade);
        var gradeEmoji = GetGradeEmoji(analytics.Grade);
        labelTable.AddRow("Známka:", $"[{GetColorName(gradeColor)}]{gradeEmoji} {analytics.Grade}[/]");
        labelTable.AddRow("Skóre:", $"{analytics.Score}/100");
        labelTable.AddRow("Rychlost:", $"{result.AverageSpeedMbps:F0} MB/s");
        labelTable.AddRow("Teplota:", result.CurrentTemperatureCelsius.HasValue ? $"{result.CurrentTemperatureCelsius}°C" : "N/A");
        
        string errorStatus = result.ErrorCount == 0 ? "[green]Žádné ✅[/]" : $"[red]{result.ErrorCount} ❌[/]";
        labelTable.AddRow("Chyby:", errorStatus);

        var labelPanel = new Panel(labelTable)
        {
            Border = BoxBorder.Double,
            BorderStyle = new Style(Color.Cyan),
            Header = new PanelHeader("[bold cyan]🏷️ ŠTÍTEK DISKU[/]")
        };
        
        AnsiConsole.Write(labelPanel);
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
        var modelName = result.DriveModel ?? "N/A";
        if(modelName.Length > 40) modelName = modelName[..37] + "...";
        
        var content = new Table()
            .Border(TableBorder.None)
            .AddColumn("Label");

        var gradeColor = GetGradeColor(analytics.Grade);
        content.AddRow($"[bold green]DiskChecker ─ {DateTime.Now:dd.MM.yy}[/]");
        content.AddRow($"[white]{Markup.Escape(modelName)}[/]");
        content.AddRow($"[yellow]SN: {Markup.Escape(result.DriveSerialNumber ?? "N/A")}[/]");
        content.AddRow($"[{GetColorName(gradeColor)}]ZNÁMKA: {analytics.Grade} ({analytics.Score}pts)[/]");
        content.AddRow($"[cyan]{result.AverageSpeedMbps:F0} MB/s | {FormatBytes(result.DriveTotalBytes)}[/]");

        var panel = new Panel(content)
        {
            Border = BoxBorder.Double,
            BorderStyle = new Style(Color.Green),
            Padding = new Padding(1, 0)
        };
        
        AnsiConsole.Write(panel);
        AnsiConsole.MarkupLine("[dim]Vyberte tento text a vytiskněte jako štítek 74x52mm[/]");
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
        
        var table = new Table()
            .Border(TableBorder.Rounded)
            .BorderColor(Color.Cyan)
            .AddColumn("Label");

        table.AddRow("[bold cyan]👕 DISKCHECKER CERTIFIKÁT[/]");
        table.AddRow("");
        table.AddRow($"Model: [white]{Markup.Escape(modelName)}[/]");
        table.AddRow($"Sériové číslo: [yellow]{Markup.Escape(result.DriveSerialNumber ?? "N/A")}[/]");
        table.AddRow($"Kapacita: [green]{FormatBytes(result.DriveTotalBytes)}[/]");
        table.AddRow("");
        table.AddRow($"[{GetColorName(gradeColor)}]ZNÁMKA: {analytics.Grade} {gradeEmoji}   SKÓRE: {analytics.Score}/100[/]");
        table.AddRow($"Rychlost: [cyan]{result.AverageSpeedMbps:F0} MB/s (průměr)[/]");
        table.AddRow($"Teplota: [yellow]{temp}°C[/]");
        table.AddRow($"Chyby: [{errorColor}]{errorText}[/]");
        table.AddRow("");
        table.AddRow($"Datum testu: [white]{DateTime.Now:dd.MM.yyyy HH:mm}[/]");
        table.AddRow($"Test ID: [dim]{result.TestId}[/]");

        var panel = new Panel(table)
        {
            Border = BoxBorder.Double,
            BorderStyle = new Style(Color.Cyan),
            Padding = new Padding(2, 1),
            Header = new PanelHeader("[bold cyan]DISKCHECKER[/]")
        };
        
        AnsiConsole.Write(panel);
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