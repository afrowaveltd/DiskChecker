using DiskChecker.TUI.Models;
using DiskChecker.TUI.Services;
using Spectre.Console;

namespace DiskChecker.TUI;

/// <summary>
/// DiskChecker TUI – Terminal-based disk testing application.
/// Designed for offline testing stations running Windows 7.
/// Uses Spectre.Console for rich terminal UI.
/// </summary>
public static class Program
{
    private static readonly string DbPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "DiskChecker",
        "dcheck.db");

    public static async Task<int> Main(string[] args)
    {
        Console.OutputEncoding = System.Text.Encoding.UTF8;
        Console.Title = "DiskChecker TUI";

        // Ensure data directory exists
        Directory.CreateDirectory(Path.GetDirectoryName(DbPath)!);

        var repo = new ResultRepository(DbPath);
        await repo.InitializeAsync();

        // Show banner
        ShowBanner();

        // Main loop
        while (true)
        {
            var choice = ShowMainMenu();

            switch (choice)
            {
                case "test":
                    await RunTestAsync(repo);
                    break;
                case "history":
                    await ShowHistoryAsync(repo);
                    break;
                case "disks":
                    await ShowDisksAsync(repo);
                    break;
                case "about":
                    ShowAbout();
                    break;
                case "exit":
                    AnsiConsole.MarkupLine("[green]👋 Nashledanou![/]");
                    return 0;
            }
        }
    }

    #region UI Screens

    private static void ShowBanner()
    {
        AnsiConsole.Clear();
        AnsiConsole.Write(
            new FigletText("DiskChecker")
                .Centered()
                .Color(Color.Cyan1));

        AnsiConsole.Write(new Rule("[grey]Terminal Disk Testing Utility[/]"));
        AnsiConsole.MarkupLine("[grey]Verze 1.0 – Windows 7+ | .NET 8 | Offline testovací stanice[/]");
        AnsiConsole.WriteLine();
    }

    private static string ShowMainMenu()
    {
        AnsiConsole.Write(new Rule("[yellow]HLAVNÍ MENU[/]"));
        AnsiConsole.WriteLine();

        var choice = AnsiConsole.Prompt(
            new SelectionPrompt<string>()
                .Title("[bold]Co chceš dělat?[/]")
                .PageSize(10)
                .AddChoices(new[]
                {
                    "🧪 Spustit test disku",     // -> test
                    "📜 Historie testů",          // -> history
                    "💿 Přehled disků",           // -> disks
                    "ℹ️  O aplikaci",             // -> about
                    "👋 Ukončit"                  // -> exit
                }));

        return choice switch
        {
            "🧪 Spustit test disku" => "test",
            "📜 Historie testů" => "history",
            "💿 Přehled disků" => "disks",
            "ℹ️  O aplikaci" => "about",
            "👋 Ukončit" => "exit",
            _ => "exit"
        };
    }

    private static async Task RunTestAsync(ResultRepository repo)
    {
        AnsiConsole.Clear();
        AnsiConsole.Write(new Rule("[yellow]SPUSTIT TEST DISKU[/]"));
        AnsiConsole.WriteLine();

        // Detect disks
        var detector = new DiskDetector();
        List<PhysicalDiskInfo> disks;

        await AnsiConsole.Status()
            .Spinner(Spinner.Known.Dots)
            .StartAsync("Detekuji fyzické disky...", async _ =>
            {
                disks = await Task.Run(() => detector.DetectDisks());
            });

        // Wait for the closure to complete
        disks = detector.DetectDisks();

        if (disks.Count == 0)
        {
            AnsiConsole.MarkupLine("[red]❌ Nebyly nalezeny žádné fyzické disky.[/]");
            AnsiConsole.MarkupLine("[grey]Ujistěte se, že aplikace běží jako Administrátor.[/]");
            AnsiConsole.WriteLine();
            AnsiConsole.MarkupLine("[grey]Stiskni Enter pro návrat...[/]");
            Console.ReadLine();
            return;
        }

        // Show disk list
        var table = new Table()
            .Border(TableBorder.Rounded)
            .AddColumn(new TableColumn("[bold]#[/]").Centered())
            .AddColumn(new TableColumn("[bold]Model[/]"))
            .AddColumn(new TableColumn("[bold]Kapacita[/]").RightAligned())
            .AddColumn(new TableColumn("[bold]Rozhraní[/]"))
            .AddColumn(new TableColumn("[bold]Sériové číslo[/]"));

        foreach (var disk in disks)
        {
            string iface = disk.InterfaceType;
            if (disk.IsUsb) iface += " [yellow](USB)[/]";
            if (disk.IsRemovable) iface += " [red](Removable)[/]";

            table.AddRow(
                disk.Index.ToString(),
                disk.Model,
                disk.CapacityFormatted,
                iface,
                disk.SerialNumber);
        }

        AnsiConsole.Write(table);
        AnsiConsole.WriteLine();

        // Select disk
        var diskChoices = disks.Select(d => $"[{d.Index}] {d.Model} ({d.CapacityFormatted})").ToList();
        diskChoices.Add("↩️ Zpět");

        var selected = AnsiConsole.Prompt(
            new SelectionPrompt<string>()
                .Title("[bold]Vyber disk k otestování:[/]")
                .PageSize(15)
                .AddChoices(diskChoices));

        if (selected == "↩️ Zpět") return;

        var diskIndex = int.Parse(selected.Split(']')[0].TrimStart('['));
        var targetDisk = disks.First(d => d.Index == diskIndex);

        // Confirmation
        AnsiConsole.WriteLine();
        AnsiConsole.MarkupLine($"[bold yellow]⚠️ VAROVÁNÍ: Destruktivní test![/]");
        AnsiConsole.MarkupLine($"[yellow]Disk:[/] [white]{targetDisk.Model}[/] ({targetDisk.CapacityFormatted})");
        AnsiConsole.MarkupLine($"[yellow]Cesta:[/] [white]{targetDisk.DevicePath}[/]");
        AnsiConsole.MarkupLine("[red]VŠECHNA DATA NA DISKU BUDOU NENÁVRATNĚ ZNIČENA![/]");
        AnsiConsole.WriteLine();

        if (!AnsiConsole.Confirm("[bold red]Opravdu chceš pokračovat?[/]"))
        {
            AnsiConsole.MarkupLine("[grey]Test zrušen.[/]");
            AnsiConsole.WriteLine();
            AnsiConsole.MarkupLine("[grey]Stiskni Enter pro návrat...[/]");
            Console.ReadLine();
            return;
        }

        // Run test
        AnsiConsole.Clear();
        AnsiConsole.Write(new Rule($"[yellow]TEST: {targetDisk.Model}[/]"));
        AnsiConsole.WriteLine();

        var executor = new DiskTestExecutor(
            targetDisk.DevicePath,
            targetDisk.CapacityBytes,
            statusCallback: msg => AnsiConsole.MarkupLine($"[grey]{msg}[/]"),
            progressCallback: (pct, speed, seek) =>
            {
                // Progress is handled by the live display below
            });

        var cts = new CancellationTokenSource();

        // Live progress display
        var result = await AnsiConsole.Progress()
            .AutoClear(false)
            .Columns(new ProgressColumn[]
            {
                new TaskDescriptionColumn(),
                new ProgressBarColumn(),
                new PercentageColumn(),
                new SpinnerColumn(),
            })
            .StartAsync(async ctx =>
            {
                var task = ctx.AddTask("[cyan]Testování disku[/]", maxValue: 100);

                // Hook progress to update the Spectre task
                var progressWrapper = new Action<double, double, double?>((pct, speed, seek) =>
                {
                    task.Value = pct;
                    task.Description = seek.HasValue
                        ? $"[cyan]Seek test[/] ({pct:F0}%) – latence {seek.Value:F2} ms"
                        : $"[cyan]{pct:F0}%[/] – {speed:F1} MB/s";
                });

                var exec = new DiskTestExecutor(
                    targetDisk.DevicePath,
                    targetDisk.CapacityBytes,
                    statusCallback: msg => { /* status lines go above progress */ },
                    progressCallback: progressWrapper);

                var testResult = await exec.RunFullDestructiveAsync(targetDisk, cts.Token);
                task.Value = 100;
                task.Description = "[green]✅ Dokončeno[/]";
                task.StopTask();

                return testResult;
            });

        // Show results
        AnsiConsole.WriteLine();
        ShowTestResults(result);

        // Save to DB
        await AnsiConsole.Status()
            .Spinner(Spinner.Known.Dots)
            .StartAsync("Ukládám výsledky do databáze...", async _ =>
            {
                await repo.SaveResultAsync(result);
            });

        AnsiConsole.MarkupLine("[green]✅ Výsledky uloženy.[/]");
        AnsiConsole.MarkupLine($"[grey]Databáze: {DbPath}[/]");
        AnsiConsole.WriteLine();
        AnsiConsole.MarkupLine("[grey]Stiskni Enter pro návrat...[/]");
        Console.ReadLine();
    }

    private static void ShowTestResults(TestRunResult result)
    {
        AnsiConsole.Write(new Rule("[green]VÝSLEDKY TESTU[/]"));

        if (!result.Success)
        {
            AnsiConsole.MarkupLine($"[red]❌ Test selhal: {result.ErrorMessage}[/]");
            return;
        }

        var gradeColor = result.Grade switch
        {
            "A" => "green",
            "B" => "lime",
            "C" => "yellow",
            "D" => "orange1",
            "E" => "red",
            _ => "red"
        };

        // Grade panel
        var gradePanel = new Panel($"[bold {gradeColor}]Z N Á M K A: {result.Grade}[/]")
        {
            Border = BoxBorder.Double,
            Padding = new Padding(2, 1)
        };
        AnsiConsole.Write(gradePanel);
        AnsiConsole.WriteLine();

        // Performance table
        var perfTable = new Table()
            .Border(TableBorder.Rounded)
            .Title("[bold]Výkon disku[/]")
            .AddColumn("Metrika")
            .AddColumn(new TableColumn("Hodnota").RightAligned());

        perfTable.AddRow("Průměrná rychlost zápisu", $"{result.WriteSpeedAvgMBps:F1} MB/s");
        perfTable.AddRow("Minimální rychlost zápisu", $"{result.WriteSpeedMinMBps:F1} MB/s");
        perfTable.AddRow("Maximální rychlost zápisu", $"{result.WriteSpeedMaxMBps:F1} MB/s");
        perfTable.AddRow("Průměrná rychlost čtení", $"{result.ReadSpeedAvgMBps:F1} MB/s");
        perfTable.AddRow("Minimální rychlost čtení", $"{result.ReadSpeedMinMBps:F1} MB/s");
        perfTable.AddRow("Maximální rychlost čtení", $"{result.ReadSpeedMaxMBps:F1} MB/s");

        if (result.SeekAvgMs.HasValue)
        {
            perfTable.AddRow("Průměrný seek time", $"{result.SeekAvgMs:F2} ms");
            perfTable.AddRow("Minimální seek time", $"{result.SeekMinMs:F2} ms");
            perfTable.AddRow("Maximální seek time", $"{result.SeekMaxMs:F2} ms");
        }

        if (result.MaxTemperatureC.HasValue)
        {
            perfTable.AddRow("Maximální teplota", $"{result.MaxTemperatureC:F1} °C");
            perfTable.AddRow("Průměrná teplota", $"{result.AvgTemperatureC:F1} °C");
        }

        AnsiConsole.Write(perfTable);
        AnsiConsole.WriteLine();

        // Sanitization status
        if (result.SanitizationPassed)
            AnsiConsole.MarkupLine("[green]✅ Sanitizace: Úspěšná[/]");
        else
            AnsiConsole.MarkupLine($"[red]⚠️ Sanitizace: {result.SanitizationOutput ?? "Selhala"}[/]");

        // Duration
        AnsiConsole.MarkupLine($"[grey]Celkový čas testu: {result.Duration:hh\\:mm\\:ss}[/]");

        // ASCII speed graph
        AnsiConsole.WriteLine();
        ShowAsciiSpeedGraph(result);
    }

    private static void ShowAsciiSpeedGraph(TestRunResult result)
    {
        if (result.WriteSamples.Count == 0 && result.ReadSamples.Count == 0)
            return;

        AnsiConsole.Write(new Rule("[grey]Graf rychlosti (ASCII)[/]"));

        // Simple ASCII bar chart of speed across disk positions
        int barWidth = 60;
        double maxSpeed = Math.Max(
            result.WriteSamples.Count > 0 ? result.WriteSamples.Max(s => s.SpeedMBps) : 0,
            result.ReadSamples.Count > 0 ? result.ReadSamples.Max(s => s.SpeedMBps) : 0);
        maxSpeed = Math.Max(maxSpeed, 1);

        // Downsample to barWidth points
        var writeDownsampled = Downsample(result.WriteSamples, barWidth);
        var readDownsampled = Downsample(result.ReadSamples, barWidth);

        AnsiConsole.MarkupLine("[cyan]Zápis (W)[/]");
        DrawAsciiBars(writeDownsampled, maxSpeed, barWidth, Color.Cyan1);

        AnsiConsole.MarkupLine("[green]Čtení (R)[/]");
        DrawAsciiBars(readDownsampled, maxSpeed, barWidth, Color.Green);

        AnsiConsole.MarkupLine($"[grey]Max: {maxSpeed:F0} MB/s | 0% ──────────────── 100%[/]");
    }

    private static void DrawAsciiBars(List<double> values, double max, int width, Color color)
    {
        if (values.Count == 0)
        {
            AnsiConsole.MarkupLine("[grey]  (žádná data)[/]");
            return;
        }

        var sb = new System.Text.StringBuilder();
        sb.Append("  ");

        for (int i = 0; i < values.Count && i < width; i++)
        {
            double ratio = values[i] / max;
            int height = (int)(ratio * 8); // 0-8 blocks high

            char block = height switch
            {
                0 => ' ',
                1 => '▁',
                2 => '▂',
                3 => '▃',
                4 => '▄',
                5 => '▅',
                6 => '▆',
                7 => '▇',
                _ => '█'
            };

            sb.Append(block);
        }

        AnsiConsole.MarkupLine($"[{color.ToMarkup()}]{sb}[/]");
    }

    private static List<double> Downsample(List<SpeedSample> samples, int targetCount)
    {
        if (samples.Count <= targetCount)
            return samples.Select(s => s.SpeedMBps).ToList();

        var result = new List<double>(targetCount);
        double step = (double)samples.Count / targetCount;

        for (int i = 0; i < targetCount; i++)
        {
            int startIdx = (int)(i * step);
            int endIdx = (int)((i + 1) * step);
            if (endIdx > samples.Count) endIdx = samples.Count;
            if (startIdx >= samples.Count) startIdx = samples.Count - 1;

            double sum = 0;
            int count = 0;
            for (int j = startIdx; j < endIdx; j++)
            {
                sum += samples[j].SpeedMBps;
                count++;
            }
            result.Add(count > 0 ? sum / count : 0);
        }

        return result;
    }

    private static async Task ShowHistoryAsync(ResultRepository repo)
    {
        AnsiConsole.Clear();
        AnsiConsole.Write(new Rule("[yellow]HISTORIE TESTŮ[/]"));
        AnsiConsole.WriteLine();

        var sessions = await repo.GetRecentSessionsAsync(20);

        if (sessions.Count == 0)
        {
            AnsiConsole.MarkupLine("[grey]Zatím nebyly provedeny žádné testy.[/]");
            AnsiConsole.WriteLine();
            AnsiConsole.MarkupLine("[grey]Stiskni Enter pro návrat...[/]");
            Console.ReadLine();
            return;
        }

        var table = new Table()
            .Border(TableBorder.Rounded)
            .AddColumn(new TableColumn("[bold]Datum[/]"))
            .AddColumn(new TableColumn("[bold]Disk[/]"))
            .AddColumn(new TableColumn("[bold]Známka[/]").Centered())
            .AddColumn(new TableColumn("[bold]Zápis avg[/]").RightAligned())
            .AddColumn(new TableColumn("[bold]Čtení avg[/]").RightAligned())
            .AddColumn(new TableColumn("[bold]Seek[/]").RightAligned())
            .AddColumn(new TableColumn("[bold]Status[/]"));

        foreach (var session in sessions)
        {
            string gradeColor = session.Grade switch
            {
                "A" => "green",
                "B" => "lime",
                "C" => "yellow",
                "D" => "orange1",
                "E" => "red",
                _ => "grey"
            };

            string status = string.IsNullOrEmpty(session.ErrorMessage)
                ? "[green]✓[/]"
                : $"[red]✗ {session.ErrorMessage}[/]";

            string seek = session.SeekAvgMs.HasValue
                ? $"{session.SeekAvgMs:F1} ms"
                : "N/A";

            table.AddRow(
                session.CompletedAt.ToString("dd.MM.yyyy HH:mm"),
                $"{session.Id}", // Would need disk model – simplified
                $"[{gradeColor}]{session.Grade}[/]",
                $"{session.WriteSpeedAvgMBps:F0} MB/s",
                $"{session.ReadSpeedAvgMBps:F0} MB/s",
                seek,
                status);
        }

        AnsiConsole.Write(table);
        AnsiConsole.WriteLine();
        AnsiConsole.MarkupLine("[grey]Stiskni Enter pro návrat...[/]");
        Console.ReadLine();
    }

    private static async Task ShowDisksAsync(ResultRepository repo)
    {
        AnsiConsole.Clear();
        AnsiConsole.Write(new Rule("[yellow]PŘEHLED DISKŮ[/]"));
        AnsiConsole.WriteLine();

        var disks = await repo.GetDisksAsync();

        if (disks.Count == 0)
        {
            AnsiConsole.MarkupLine("[grey]Zatím nebyly otestovány žádné disky.[/]");
            AnsiConsole.WriteLine();
            AnsiConsole.MarkupLine("[grey]Stiskni Enter pro návrat...[/]");
            Console.ReadLine();
            return;
        }

        var table = new Table()
            .Border(TableBorder.Rounded)
            .AddColumn(new TableColumn("[bold]Model[/]"))
            .AddColumn(new TableColumn("[bold]Sériové č.[/]"))
            .AddColumn(new TableColumn("[bold]Kapacita[/]").RightAligned())
            .AddColumn(new TableColumn("[bold]Rozhraní[/]"))
            .AddColumn(new TableColumn("[bold]První test[/]"))
            .AddColumn(new TableColumn("[bold]Poslední test[/]"));

        foreach (var disk in disks)
        {
            table.AddRow(
                disk.Model,
                disk.SerialNumber,
                $"{disk.CapacityBytes / 1_000_000_000.0:F1} GB",
                disk.InterfaceType,
                disk.FirstSeen.ToString("dd.MM.yyyy"),
                disk.LastTested.ToString("dd.MM.yyyy HH:mm"));
        }

        AnsiConsole.Write(table);
        AnsiConsole.WriteLine();
        AnsiConsole.MarkupLine("[grey]Stiskni Enter pro návrat...[/]");
        Console.ReadLine();
    }

    private static void ShowAbout()
    {
        AnsiConsole.Clear();
        AnsiConsole.Write(new Rule("[yellow]O APLIKACI[/]"));
        AnsiConsole.WriteLine();

        var aboutPanel = new Panel(
            "[bold]DiskChecker TUI[/] – Terminálová verze\n\n" +
            "[grey]Určeno pro offline testovací stanice (Windows 7+).\n" +
            "Provádí destruktivní testy fyzických disků:\n" +
            "  • Zápis na celý disk\n" +
            "  • Čtení a verifikace\n" +
            "  • Seek time test (HDD)\n" +
            "  • Sanitizace (secure erase)\n\n" +
            "Výsledky se ukládají do SQLite databáze.\n" +
            "Certifikáty a grafy lze zobrazit v plné verzi DiskChecker.[/]")
        {
            Border = BoxBorder.Rounded,
            Padding = new Padding(2, 1)
        };

        AnsiConsole.Write(aboutPanel);
        AnsiConsole.WriteLine();

        AnsiConsole.MarkupLine("[bold]Technické informace:[/]");
        AnsiConsole.MarkupLine($"  • .NET 8.0 (self-contained)");
        AnsiConsole.MarkupLine($"  • Spectre.Console pro TUI");
        AnsiConsole.MarkupLine($"  • SQLite databáze: {DbPath}");
        AnsiConsole.MarkupLine($"  • Windows 7 SP1+ kompatibilní");
        AnsiConsole.WriteLine();
        AnsiConsole.MarkupLine("[grey]Stiskni Enter pro návrat...[/]");
        Console.ReadLine();
    }

    #endregion
}
