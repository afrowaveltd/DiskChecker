using Spectre.Console;
using System.Text;

namespace DiskChecker.UI.Console;

/// <summary>
/// Custom Spectre.Console components for enhanced test visualization.
/// </summary>
public static class TestVisualizationComponents
{
    /// <summary>
    /// Creates a temperature gauge bar with color gradient (green→yellow→red) and temperature cursor.
    /// </summary>
    public static Panel CreateTemperatureGauge(int currentTemp, int minTemp, int maxTemp, int panelWidth)
    {
        const int GAUGE_WIDTH = 40;
        const int MIN_SAFE = 20;    // Green
        const int YELLOW_START = 40; // Yellow warning
        const int RED_START = 55;    // Red critical
        const int MAX_DISPLAY = 80;  // Max scale
        
        // Clamp for display
        var displayCurrent = Math.Min(Math.Max(currentTemp, MIN_SAFE), MAX_DISPLAY);
        var displayMax = Math.Min(Math.Max(maxTemp, MIN_SAFE), MAX_DISPLAY);
        
        // Build gradient bar
        var bar = new StringBuilder();
        for (int i = 0; i < GAUGE_WIDTH; i++)
        {
            float position = (float)i / GAUGE_WIDTH;
            int temp = MIN_SAFE + (int)(position * (MAX_DISPLAY - MIN_SAFE));
            
            // Determine color
            string color = temp < YELLOW_START
                ? "[green]"
                : temp < RED_START
                ? "[yellow]"
                : "[red]";
            
            // Check if this is cursor position
            float cursorPos = (displayCurrent - MIN_SAFE) / (float)(MAX_DISPLAY - MIN_SAFE);
            bool isCursor = Math.Abs(position - cursorPos) < (1f / GAUGE_WIDTH);
            
            if (isCursor)
            {
                bar.Append("[white]▼[/]");
            }
            else
            {
                bar.Append($"{color}═[/]");
            }
        }
        
        // Temperature stats
        var grid = new Grid();
        grid.AddColumn(new GridColumn().Width(12));
        grid.AddColumn(new GridColumn().Width(28));
        grid.AddColumn(new GridColumn().Width(12));
        grid.AddColumn(new GridColumn().Width(12));
        grid.AddColumn(new GridColumn().Width(12));
        
        grid.AddRow(
            new Markup("[bold cyan]Teplota:[/]"),
            new Markup(bar.ToString()),
            new Markup($"[bold green]Min: {minTemp}°C[/]"),
            new Markup($"[bold white]Aktuální: {currentTemp}°C[/]"),
            new Markup($"[bold red]Max: {maxTemp}°C[/]")
        );
        
        // Health indicator color based on max temp
        var healthColor = maxTemp < 40 ? "green" : maxTemp < 55 ? "yellow" : "red";
        var healthText = maxTemp < 40 ? "✓ OK" : maxTemp < 55 ? "⚠ VAROVÁNÍ" : "❌ KRITICKÉ";
        
        var panel = new Panel(grid)
        {
            Width = panelWidth
        };

        panel.Border(BoxBorder.Rounded);
        panel.BorderColor(healthColor == "green" ? Color.Green : healthColor == "yellow" ? Color.Yellow : Color.Red);
        panel.Header($"[bold {healthColor}] STAV TEPLOTY - {healthText} [/]");
        panel.Padding(1, 0);

        return panel;
    }
    
    /// <summary>
    /// Creates an overall progress gauge showing both write and verify phases.
    /// </summary>
    public static Panel CreateOverallProgressGauge(
        double writeProgress,
        double verifyProgress,
        string writeEta,
        string verifyEta,
        long totalBytes,
        long testedBytes,
        TimeSpan elapsed,
        string overallEta,
        int panelWidth)
    {
        const int GAUGE_WIDTH = 60;
        
        // Write phase (0-50% of bar)
        var writeBarLength = (int)(writeProgress / 100 * GAUGE_WIDTH / 2);
        var writeBar = new string('█', writeBarLength) + new string('░', GAUGE_WIDTH / 2 - writeBarLength);
        
        // Verify phase (50-100% of bar)
        var verifyBarLength = (int)(verifyProgress / 100 * GAUGE_WIDTH / 2);
        var verifyBar = new string('█', verifyBarLength) + new string('░', GAUGE_WIDTH / 2 - verifyBarLength);
        
        // Combined gauge
        var combinedGauge = $"[cyan]{writeBar}[/][yellow]{verifyBar}[/]";
        
        // Overall percentage
        var overallPercent = (writeProgress * 0.5 + verifyProgress * 0.5);
        
        var grid = new Grid();
        grid.AddColumn(new GridColumn().Width(14));
        grid.AddColumn(new GridColumn().Width(66));
        
        grid.AddRow(
            new Markup("[bold cyan]Celkem:[/]"),
            new Markup($"{combinedGauge} [bold]{overallPercent:F1}%[/]")
        );
        
        grid.AddRow(
            new Markup("[bold cyan]Zápis:[/]"),
            new Markup($"[cyan]{writeProgress:F1}%[/] ETA: {writeEta}")
        );
        
        grid.AddRow(
            new Markup("[bold cyan]Ověření:[/]"),
            new Markup($"[yellow]{verifyProgress:F1}%[/] ETA: {verifyEta}")
        );

        grid.AddRow(
            new Markup("[bold cyan]Čas:[/]"),
            new Markup($"[white]{FormatDuration(elapsed)}[/] [dim]Zbývá: {overallEta}[/]")
        );
        
        grid.AddRow(
            new Markup("[bold cyan]Stav:[/]"),
            new Markup($"[green]{DiskChecker.UI.Console.MainConsoleMenu.FormatBytes(testedBytes)}[/] / {DiskChecker.UI.Console.MainConsoleMenu.FormatBytes(totalBytes)}")
        );
        
        var panel = new Panel(grid)
        {
            Width = panelWidth
        };

        panel.Border(BoxBorder.Rounded);
        panel.BorderColor(Color.Cyan);
        panel.Header("[bold cyan] CELKOVÝ PRŮBĚH TESTU [/]");
        panel.Padding(1, 0);

        return panel;
    }
    
    /// <summary>
    /// Creates a health status indicator bar (green → yellow → red).
    /// </summary>
    public static Panel CreateHealthIndicator(int errorCount, long reallocatedSectors, int temperatureC)
    {
        // Calculate health score (0-100, 100 = perfect)
        var healthScore = 100;
        
        // Penalties
        if (errorCount > 0)
            healthScore -= Math.Min(40, errorCount * 5);
        if (reallocatedSectors > 0)
            healthScore -= Math.Min(30, (int)(reallocatedSectors / 100));
        if (temperatureC > 55)
            healthScore -= Math.Min(20, (temperatureC - 55) * 2);
        
        healthScore = Math.Max(0, healthScore);
        
        // Color and status
        string status, color;
        if (healthScore >= 80)
        {
            status = "✓ VÝBORNÝ STAV";
            color = "green";
        }
        else if (healthScore >= 60)
        {
            status = "⚠ PŘIJATELNÝ STAV";
            color = "yellow";
        }
        else if (healthScore >= 40)
        {
            status = "❌ DEGRADACE";
            color = "red3";
        }
        else
        {
            status = "❌❌ KRITICKÝ STAV";
            color = "darkred";
        }
        
        // Health bar
        const int BAR_WIDTH = 50;
        var filledBars = (int)(healthScore / 100 * BAR_WIDTH);
        var barColor = healthScore >= 80 ? "green" : healthScore >= 60 ? "yellow" : "red";
        var healthBar = $"[{barColor}]{new string('█', filledBars)}[/][dim]{new string('░', BAR_WIDTH - filledBars)}[/]";
        
        var grid = new Grid();
        grid.AddColumn(new GridColumn().Width(30));
        grid.AddColumn(new GridColumn().Width(56));
        
        grid.AddRow(
            new Markup($"[bold {color}]{status}[/]"),
            new Markup($"{healthBar} {healthScore}%")
        );
        
        grid.AddRow(
            new Markup("[dim]Chyby:[/]"),
            new Markup($"[{(errorCount > 0 ? "red" : "green")}]{errorCount}[/]")
        );
        
        grid.AddRow(
            new Markup("[dim]Realokované:[/]"),
            new Markup($"[{(reallocatedSectors > 0 ? "yellow" : "green")}]{reallocatedSectors}[/]")
        );
        
        var panel = new Panel(grid)
            .Border(BoxBorder.Rounded)
            .BorderColor(color == "green" ? Color.Green : color == "yellow" ? Color.Yellow : Color.Red)
            .Header($"[bold {color}] ZDRAVÍ DISKU - {healthScore}% [/]")
            .Padding(1, 0);
        
        return panel;
    }

    private static string FormatDuration(TimeSpan elapsed)
    {
        var totalHours = (int)elapsed.TotalHours;
        return $"{totalHours:00}:{elapsed.Minutes:00}:{elapsed.Seconds:00}";
    }
}
