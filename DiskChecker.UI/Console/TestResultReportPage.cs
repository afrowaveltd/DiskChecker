using DiskChecker.Core.Models;
using DiskChecker.Application.Services;
using Spectre.Console;
using System.IO;

namespace DiskChecker.UI.Console;

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

        // Clear screen and display report
        AnsiConsole.Clear();
        var report = analysisService.GenerateTerminalReport(result, analytics);
        AnsiConsole.Write(new Panel(new Markup(EscapeMarkup(report)))
        {
            Border = BoxBorder.Rounded,
            Padding = new Padding(1, 1)
        }.BorderStyle(new Style(GetGradeColor(analytics.Grade))));

        AnsiConsole.WriteLine();

        // Export options
        var option = AnsiConsole.Prompt(
            new SelectionPrompt<string>()
                .Title("[bold yellow]📊 What would you like to do?[/]")
                .AddChoices(new[]
                {
                    "📋 View JSON Report",
                    "📊 Export as CSV (for graphing)",
                    "💾 Save Certificate (JSON)",
                    "🔄 Return to Menu"
                }));

        switch (option)
        {
            case "📋 View JSON Report":
                await ViewJsonReportAsync(result, analysisService, analytics);
                break;

            case "📊 Export as CSV (for graphing)":
                await ExportCsvAsync(result, analysisService);
                break;

            case "💾 Save Certificate (JSON)":
                await SaveCertificateAsync(result, analysisService, analytics);
                break;

            case "🔄 Return to Menu":
                return;
        }

        // After action, return to this menu
        AnsiConsole.MarkupLine("\n[dim]Stiskněte libovolnou klávesu...[/]");
        System.Console.ReadKey(true);
        await DisplayResultAsync(result);
    }

    private static async Task ViewJsonReportAsync(SurfaceTestResult result, TestReportAnalysisService analysisService, TestReportAnalysisService.TestAnalytics analytics)
    {
        AnsiConsole.Clear();
        var jsonReport = analysisService.GenerateJsonReport(result, analytics);
        
        AnsiConsole.MarkupLine("[bold cyan]📄 JSON Report:[/]\n");
        AnsiConsole.Write(new Panel(new Markup(EscapeMarkup(jsonReport)))
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

    private static Color GetGradeColor(string grade) => grade switch
    {
        "A+" => Color.Green,
        "A" => Color.Green,
        "B+" => Color.Blue,
        "B" => Color.Blue,
        "C" => Color.Yellow,
        "D" => Color.Orange1,
        _ => Color.Red
    };

    private static string EscapeMarkup(string text)
    {
        // Escape special Markup characters to display them literally
        return text
            .Replace("[", "\\[")
            .Replace("]", "\\]");
    }
}
