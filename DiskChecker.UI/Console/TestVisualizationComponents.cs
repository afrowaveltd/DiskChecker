using Spectre.Console;
using System.Text;

namespace DiskChecker.UI.Console.Pages;

/// <summary>
/// Custom Spectre.Console components for enhanced test visualization.
/// </summary>
public static class TestVisualizationComponents
{
    /// <summary>
    /// Creates a temperature gauge bar with color gradient (green→yellow→red) and temperature cursor.
    /// </summary>
    public static Panel CreateTemperatureGauge(int currentTemp, int minTemp, int maxTemp, int panelWidth, DateTime? lastUpdate = null)
    {
        const int GAUGE_WIDTH = 30;  // ZKRÁCENO z 40 na 30
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
        
        // Temperature stats with optional last update time
        var grid = new Grid();
        grid.AddColumn(new GridColumn().Width(10));
        grid.AddColumn(new GridColumn().Width(32));
        grid.AddColumn(new GridColumn().Width(12));
        grid.AddColumn(new GridColumn().Width(14));
        grid.AddColumn(new GridColumn().Width(12));
        
        // Build current temp display with optional update time
        string currentTempDisplay = $"[bold white]{currentTemp}°C[/]";
        if (lastUpdate.HasValue)
        {
            var secondsAgo = (int)(DateTime.UtcNow - lastUpdate.Value).TotalSeconds;
            string updateInfo = secondsAgo < 60 
                ? $"[dim]({secondsAgo}s)[/]" 
                : "[dim](>1m)[/]";
            currentTempDisplay = $"[bold white]{currentTemp}°C[/] {updateInfo}";
        }
        
        grid.AddRow(
            new Markup("[bold cyan]Teplota:[/]"),
            new Markup(bar.ToString()),
            new Markup($"[bold green]Min: {minTemp}°C[/]"),
            new Markup($"[bold white]Aktuální:[/]"),
            new Markup(currentTempDisplay)
        );
        
        // Add second row with max temp
        grid.AddRow(
            new Text(""),
            new Text(""),
            new Text(""),
            new Markup($"[bold red]Max: {maxTemp}°C[/]"),
            new Text("")
        );
        
        // Health indicator color based on max temp
        var healthColor = maxTemp < 40 ? "green" : maxTemp < 55 ? "yellow" : "red";
        var healthText = maxTemp < 40 ? "✓ OK" : maxTemp < 55 ? "⚠ VAROVÁNÍ" : "❌ KRITICKÉ";
        
        var panel = new Panel(grid)
        {
            Width = ResolvePanelWidth(panelWidth)
        };

        panel.Border(BoxBorder.Rounded);
        panel.BorderColor(healthColor == "green" ? Color.Green : healthColor == "yellow" ? Color.Yellow : Color.Red);
        // Use plain text header (no Spectre markup) to avoid header-width/markup rendering issues
        panel.Header($"Stav teploty - {healthText}");
        panel.HeaderAlignment(Justify.Center);
        panel.Padding(1, 1);

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
            new Markup($"[green]{DiskChecker.UI.Console.Pages.MainConsoleMenu.FormatBytes(testedBytes)}[/] / {DiskChecker.UI.Console.Pages.MainConsoleMenu.FormatBytes(totalBytes)}")
        );
        
        var panel = new Panel(grid)
        {
            Width = ResolvePanelWidth(panelWidth)
        };

        panel.Border(BoxBorder.Rounded);
        panel.BorderColor(Color.Cyan);
        panel.Header("Celkový průběh testu");
        panel.HeaderAlignment(Justify.Center);
        panel.Padding(1, 1);

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
            .Header($"ZDRAVÍ DISKU - {healthScore}% ")
            .Padding(1, 0);
        
        return panel;
    }

    /// <summary>
    /// Creates a live speed gauge panel showing current throughput with visual indicator.
    /// </summary>
    public static Panel CreateLiveSpeedGauge(double currentSpeedMbps, double maxExpectedSpeed = 600, int panelWidth = 78)
    {
        // Normalize speed to percentage (0-100%)
        double speedPercent = Math.Min(100, (currentSpeedMbps / maxExpectedSpeed) * 100);
        
        // Create speed bar
        const int BAR_WIDTH = 50;
        var filledBars = (int)(speedPercent / 100 * BAR_WIDTH);
        
        // Determine color based on speed
        string speedColor = currentSpeedMbps > maxExpectedSpeed * 0.8 ? "[green]" :
                           currentSpeedMbps > maxExpectedSpeed * 0.6 ? "[yellow]" :
                           currentSpeedMbps > maxExpectedSpeed * 0.4 ? "[orange1]" :
                           "[red]";
        
        // Determine status text
        string statusEmoji = currentSpeedMbps > maxExpectedSpeed * 0.8 ? "🟢" :
                            currentSpeedMbps > maxExpectedSpeed * 0.6 ? "🟡" :
                            currentSpeedMbps > maxExpectedSpeed * 0.4 ? "🟠" :
                            "🔴";
        
        string statusText = currentSpeedMbps > maxExpectedSpeed * 0.8 ? "VÝBORNÉ" :
                           currentSpeedMbps > maxExpectedSpeed * 0.6 ? "DOBRÉ" :
                           currentSpeedMbps > maxExpectedSpeed * 0.4 ? "PŘIJATELNÉ" :
                           "NÍZKÉ";
        
        // Create speed bar with colors
        var speedBar = new StringBuilder();
        for (int i = 0; i < BAR_WIDTH; i++)
        {
            if (i < filledBars)
            {
                speedBar.Append(speedColor + "█[/]");
            }
            else
            {
                speedBar.Append("[dim]░[/]");
            }
        }
        
        // Create grid with speed information
        var grid = new Grid();
        grid.AddColumn(new GridColumn().Width(20));
        grid.AddColumn(new GridColumn().Width(58));
        
        grid.AddRow(
            new Markup($"[bold white]{statusEmoji} RYCHLOST[/]"),
            new Markup($"{speedBar} [bold cyan]{currentSpeedMbps:F1} MB/s[/]")
        );
        
        grid.AddRow(
            new Markup($"[dim]Status:[/]"),
            new Markup($"[bold {(currentSpeedMbps > maxExpectedSpeed * 0.8 ? "green" : 
                                 currentSpeedMbps > maxExpectedSpeed * 0.6 ? "yellow" :
                                 currentSpeedMbps > maxExpectedSpeed * 0.4 ? "orange1" : "red")}]{statusText}[/]")
        );
        
        // Capacity indicator (percentage of max expected)
        grid.AddRow(
            new Markup("[dim]Kapacita:[/]"),
            new Markup($"[cyan]{speedPercent:F0}%[/] z {maxExpectedSpeed} MB/s")
        );
        
        var panel = new Panel(grid)
        {
            Width = panelWidth
        };

        panel.Border(BoxBorder.Rounded);
        string borderColor = currentSpeedMbps > maxExpectedSpeed * 0.8 ? "green" : 
                            currentSpeedMbps > maxExpectedSpeed * 0.6 ? "yellow" :
                            "red";
        panel.BorderColor(borderColor == "green" ? Color.Green : 
                         borderColor == "yellow" ? Color.Yellow : 
                         Color.Red);
        panel.Header(" Trend rychlosti ");
        panel.HeaderAlignment(Justify.Center);
        panel.Padding(1, 1);

        return panel;
    }

    private static string FormatDuration(TimeSpan elapsed)
    {
        var totalHours = (int)elapsed.TotalHours;
        return $"{totalHours:00}:{elapsed.Minutes:00}:{elapsed.Seconds:00}";
    }

    /// <summary>
    /// Ensure panel width does not exceed current console width to avoid clipped borders.
    /// </summary>
    private static int ResolvePanelWidth(int requestedWidth)
    {
        try
        {
            var consoleWidth = global::System.Console.WindowWidth;
            var max = Math.Max(20, consoleWidth - 4); // leave space for borders/margins
            return Math.Min(requestedWidth, max);
        }
        catch
        {
            return requestedWidth;
        }
    }

    public enum BlockStatus
    {
        NotTested = 0,
        InProgress = 1,
        WriteOk = 2,
        ReadOk = 3,
        Error = 4
    }

    public class SurfaceTestGridState
    {
        public const int GridColumns = 100;
        public const int GridRows = 10;
        public const int TotalBlocks = GridColumns * GridRows;

        public BlockStatus[] Blocks { get; } = new BlockStatus[TotalBlocks];
        public int BlocksCompleted { get; set; }
        public int BlocksWithErrors { get; set; }
        public int CurrentBlockIndex { get; set; } = -1;

        public SurfaceTestGridState()
        {
            for (int i = 0; i < TotalBlocks; i++)
            {
                Blocks[i] = BlockStatus.NotTested;
            }
        }

        public void UpdateProgress(long bytesProcessed, long totalBytes, bool isWritePhase, bool hasError)
        {
            if (totalBytes <= 0) return;

            var progress = (double)bytesProcessed / totalBytes;
            var blockIndex = (int)(progress * TotalBlocks);
            blockIndex = Math.Min(Math.Max(blockIndex, 0), TotalBlocks - 1);

            // Remember previous status for error counting
            var prevStatus = Blocks[blockIndex];

            if (isWritePhase)
            {
                // Normal write progression: update newly completed blocks from previous completed index up to current
                for (int i = BlocksCompleted; i <= blockIndex; i++)
                {
                    if (i == blockIndex)
                    {
                        // currently processing this block
                        Blocks[i] = hasError ? BlockStatus.Error : BlockStatus.InProgress;
                    }
                    else
                    {
                        // completed blocks
                        if (Blocks[i] == BlockStatus.InProgress)
                        {
                            Blocks[i] = hasError ? BlockStatus.Error : BlockStatus.WriteOk;
                        }
                        else if (Blocks[i] == BlockStatus.NotTested)
                        {
                            Blocks[i] = BlockStatus.WriteOk;
                        }
                    }
                }
                BlocksCompleted = Math.Max(BlocksCompleted, blockIndex);
            }
            else
            {
                // Verify phase: verification starts from the beginning. Ensure previously written blocks are converted
                // to ReadOk as they get verified. Iterate from 0..blockIndex so early blocks are converted even if
                // BlocksCompleted was advanced during write phase.
                for (int i = 0; i <= blockIndex; i++)
                {
                    if (i == blockIndex)
                    {
                        // currently processing this block during verify
                        Blocks[i] = hasError ? BlockStatus.Error : BlockStatus.InProgress;
                    }
                    else
                    {
                        // mark verified blocks as ReadOk unless they already have an Error
                        if (Blocks[i] == BlockStatus.Error)
                            continue;

                        Blocks[i] = BlockStatus.ReadOk;
                    }
                }

                // After converting verified blocks, ensure BlocksCompleted reflects at least the blockIndex
                BlocksCompleted = Math.Max(BlocksCompleted, blockIndex);
            }

            // Only increment error count when we transitioned a block to Error now
            if ((Blocks[blockIndex] == BlockStatus.Error) && prevStatus != BlockStatus.Error)
            {
                BlocksWithErrors++;
            }

            CurrentBlockIndex = blockIndex;
        }

        public void MarkComplete(bool hadWriteErrors)
        {
            for (int i = BlocksCompleted; i < TotalBlocks; i++)
            {
                if (Blocks[i] == BlockStatus.NotTested || Blocks[i] == BlockStatus.InProgress)
                {
                    Blocks[i] = hadWriteErrors ? BlockStatus.Error : BlockStatus.ReadOk;
                }
            }
            BlocksCompleted = TotalBlocks;
        }
    }

    public static Panel CreateSurfaceTestGrid(SurfaceTestGridState state, int panelWidth = 110)
    {
        var grid = new Grid();
        for (int col = 0; col < SurfaceTestGridState.GridColumns; col++)
        {
            grid.AddColumn(new GridColumn().Width(1));
        }

        for (int row = 0; row < SurfaceTestGridState.GridRows; row++)
        {
            var rowCells = new List<Markup>();
            for (int col = 0; col < SurfaceTestGridState.GridColumns; col++)
            {
                int index = row * SurfaceTestGridState.GridColumns + col;
                var status = state.Blocks[index];
                var cell = status switch
                {
                    BlockStatus.NotTested => "[dim]░[/]",
                    BlockStatus.InProgress => "[yellow]▓[/]",
                    BlockStatus.WriteOk => "[cyan]▓[/]",
                    BlockStatus.ReadOk => "[green]█[/]",
                    BlockStatus.Error => "[red]█[/]",
                    _ => "[dim]░[/]"
                };
                rowCells.Add(new Markup(cell));
            }
            grid.AddRow(rowCells.ToArray());
        }

        var legend = new Grid();
        legend.AddColumn(new GridColumn().Width(20));
        legend.AddColumn(new GridColumn().Width(15));
        legend.AddColumn(new GridColumn().Width(15));
        legend.AddColumn(new GridColumn().Width(15));
        legend.AddColumn(new GridColumn().Width(15));

        legend.AddRow(
            new Markup("[dim]░ Netestováno[/]"),
            new Markup("[yellow]▓ Probíhá[/]"),
            new Markup("[cyan]▓ Zápis OK[/]"),
            new Markup("[green]█ Čtení OK[/]"),
            new Markup("[red]█ Chyba[/]")
        );

        var statsGrid = new Grid();
        statsGrid.AddColumn(new GridColumn().Width(28));
        statsGrid.AddColumn(new GridColumn().Width(20));
        statsGrid.AddColumn(new GridColumn().Width(20));
        statsGrid.AddColumn(new GridColumn().Width(20));

        var testedPercent = SurfaceTestGridState.TotalBlocks > 0
            ? state.BlocksCompleted * 100.0 / SurfaceTestGridState.TotalBlocks
            : 0;

        statsGrid.AddRow(
            new Markup($"[bold cyan]Bloků:[/] {state.BlocksCompleted}/{SurfaceTestGridState.TotalBlocks}"),
            new Markup($"[bold green]Hotovo:[/] {testedPercent:F1}%"),
            new Markup($"[bold red]Chyby:[/] {state.BlocksWithErrors}"),
            new Markup($"[dim]Idx: {state.CurrentBlockIndex}[/]")
        );

        var contentGrid = new Grid();
        contentGrid.AddColumn(new GridColumn());
        contentGrid.AddRow(new Panel(grid).Border(BoxBorder.None));
        contentGrid.AddRow(new Panel(legend).Border(BoxBorder.None).Padding(0, 0));
        contentGrid.AddRow(new Panel(statsGrid).Border(BoxBorder.None).Padding(0, 0));

        var panel = new Panel(contentGrid)
        {
            Width = panelWidth
        };

        panel.Border(BoxBorder.Rounded);
        panel.BorderColor(Color.Blue);
        panel.Header("📊 Povrchový test disku - vizualizace");
        panel.HeaderAlignment(Justify.Center);
        panel.Padding(1, 1);

        return panel;
    }

    public static Panel CreateSurfaceTestGridCompact(SurfaceTestGridState state, int panelWidth = 110)
    {
        var sb = new StringBuilder();
        sb.AppendLine();

        for (int row = 0; row < SurfaceTestGridState.GridRows; row++)
        {
            sb.Append("  ");
            for (int col = 0; col < SurfaceTestGridState.GridColumns; col++)
            {
                int index = row * SurfaceTestGridState.GridColumns + col;
                var status = state.Blocks[index];
                var cell = status switch
                {
                    BlockStatus.NotTested => "[dim]░[/]",
                    BlockStatus.InProgress => "[yellow]▓[/]",
                    BlockStatus.WriteOk => "[cyan]▓[/]",
                    BlockStatus.ReadOk => "[green]█[/]",
                    BlockStatus.Error => "[red]█[/]",
                    _ => "[dim]░[/]"
                };
                sb.Append(cell);
            }
            sb.AppendLine();
        }

        var testedPercent = SurfaceTestGridState.TotalBlocks > 0
            ? state.BlocksCompleted * 100.0 / SurfaceTestGridState.TotalBlocks
            : 0;

        sb.AppendLine();
        sb.AppendLine($"[dim]░ Netestováno[/]  [yellow]▓ Probíhá/Zápis[/]  [green]█ Čtení OK[/]  [red]█ Chyba[/]  [bold]Progress: {testedPercent:F1}% ({state.BlocksCompleted}/{SurfaceTestGridState.TotalBlocks})[/]");

        var panel = new Panel(new Markup(sb.ToString()))
        {
            Width = ResolvePanelWidth(panelWidth)
        };

        panel.Border(BoxBorder.Rounded);
        var borderColor = state.BlocksWithErrors > 0 ? Color.Red : testedPercent >= 100 ? Color.Green : Color.Blue;
        panel.BorderColor(borderColor);
        panel.Header("[bold white]📊 Povrchový test[/]");
        panel.HeaderAlignment(Justify.Center);
        panel.Padding(1, 0);

        return panel;
    }
}
