using DiskChecker.Core.Interfaces;
using DiskChecker.Core.Models;
using DiskChecker.TUI.Services;
using Spectre.Console;

namespace DiskChecker.TUI.Screens;

/// <summary>
/// SMART check screen: disk identity, health metrics, attributes, self-tests with live
/// progress, self-test log, raw smartctl dump, cache maintenance and SMART-only
/// certificates. Mirrors the desktop SMART screen and shares its services.
/// </summary>
public sealed class SmartCheckScreen : ITuiScreen
{
    private CoreDriveInfo? _disk;
    private SmartaData? _smart;
    private SmartCheckResult? _result;
    private string _status = string.Empty;

    public string Id => ScreenIds.SmartCheck;

    public string TitleKey => "Nav.SmartCheck";

    public async Task<ScreenResult> ShowAsync(TuiContext context, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);

        _disk = context.Selection.SelectedDisk;
        if (_disk is null)
        {
            _disk = await PickDiskAsync(context, cancellationToken).ConfigureAwait(false);
            if (_disk is null)
            {
                return ScreenResult.Back();
            }
        }

        await LoadAsync(context, cancellationToken).ConfigureAwait(false);

        while (true)
        {
            Render(context);
            var choice = context.Io.Choose(
                context.L("SMART.Title"),
                BuildActions(context),
                pageSize: 14);

            switch (choice)
            {
                case "refresh":
                    await LoadAsync(context, cancellationToken).ConfigureAwait(false);
                    break;
                case "short":
                    await RunSelfTestAsync(context, SmartaSelfTestType.ShortTest, cancellationToken).ConfigureAwait(false);
                    break;
                case "extended":
                    await RunSelfTestAsync(context, SmartaSelfTestType.Extended, cancellationToken).ConfigureAwait(false);
                    break;
                case "abort":
                    await AbortSelfTestAsync(context, cancellationToken).ConfigureAwait(false);
                    break;
                case "log":
                    await ShowSelfTestLogAsync(context, cancellationToken).ConfigureAwait(false);
                    break;
                case "attributes":
                    ShowAttributes(context);
                    context.Io.Pause();
                    break;
                case "raw":
                    await ShowRawOutputAsync(context, cancellationToken).ConfigureAwait(false);
                    break;
                case "certificate":
                {
                    var navigation = await SaveSmartCertificateAsync(context, cancellationToken).ConfigureAwait(false);
                    if (navigation is not null)
                    {
                        return navigation.Value;
                    }

                    break;
                }
                case "cache":
                    await ClearCacheAsync(context).ConfigureAwait(false);
                    break;
                case "back":
                    return ScreenResult.Back();
                case TuiKeys.MainMenu:
                    return ScreenResult.Menu();
                default:
                    return ScreenResult.Back();
            }
        }
    }

    private static async Task<CoreDriveInfo?> PickDiskAsync(TuiContext context, CancellationToken cancellationToken)
    {
        IReadOnlyList<CoreDriveInfo> drives;
        try
        {
            drives = await context.DiskCache.GetDrivesAsync(forceRefresh: true).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            context.Dialogs.Error("Disky", "Detekce disků selhala: " + ex.Message);
            return null;
        }

        if (drives.Count == 0)
        {
            context.Dialogs.Warning("Disky", "Nebyly nalezeny žádné disky. Spusťte aplikaci s právy root/správce.");
            return null;
        }

        var choices = drives
            .Select(drive => new TuiChoice(
                drive.Path,
                drive.Name.Contains(drive.Path, StringComparison.OrdinalIgnoreCase)
                    ? drive.Name
                    : $"{drive.Name} ({drive.Path})",
                TuiFormats.Capacity(drive.TotalSize)))
            .ToList();

        string? selected = context.Io.Choose("Vyberte disk", choices, allowCancel: true);
        if (string.IsNullOrEmpty(selected) || selected == TuiKeys.Back)
        {
            return null;
        }

        var driveInfo = drives.FirstOrDefault(drive => string.Equals(drive.Path, selected, StringComparison.OrdinalIgnoreCase));
        if (driveInfo is not null)
        {
            context.Selection.SelectedDisk = driveInfo;
            context.Selection.SelectedDiskDisplayName = driveInfo.Name;
        }

        return driveInfo;
    }

    private async Task LoadAsync(TuiContext context, CancellationToken cancellationToken)
    {
        if (_disk is null)
        {
            return;
        }

        _status = context.L("SMART.Status.Loading", _disk.Name);
        context.StatusMessage = _status;

        try
        {
            using var scope = context.CreateScope();
            var smartCheck = scope.ServiceProvider.GetRequiredService<SmartCheckService>();
            _result = await smartCheck.RunAsync(_disk, cancellationToken).ConfigureAwait(false);
            _smart = _result?.SmartaData;

            if (_smart is null)
            {
                _status = context.GetRequiredService<ISmartaProvider>().LastOperationWasPermissionDenied
                    ? context.L("SMART.Status.PermissionDenied")
                    : context.L("SMART.Status.Unavailable");
            }
            else
            {
                _status = context.L("SMART.Status.Loaded", _smart.HealthStatus);
            }

            context.StatusMessage = _status;
        }
        catch (OperationCanceledException)
        {
            _status = "Načtení SMART bylo přerušeno.";
            context.StatusMessage = _status;
        }
        catch (Exception ex) when (ex is IOException or InvalidOperationException or UnauthorizedAccessException)
        {
            _smart = null;
            _result = null;
            _status = context.L("SMART.Status.Error", ex.Message);
            context.StatusMessage = _status;
        }
    }

    private IReadOnlyList<TuiChoice> BuildActions(TuiContext context)
    {
        var actions = new List<TuiChoice>
        {
            new("refresh", "🔄 " + context.L("Common.Refresh")),
            new("short", "▶ Krátký self-test"),
            new("extended", "▶ Rozšířený self-test"),
            new("log", "📜 Self-test log"),
            new("attributes", "📋 SMART atributy"),
            new("raw", "🧾 Raw smartctl výstup"),
            new("cache", "🧹 Vyčistit SMART cache"),
            new("back", "↩ Zpět"),
            new(TuiKeys.MainMenu, "☰ Navigace"),
        };

        if (_smart is not null && _result?.Rating is not null)
        {
            actions.Insert(7, new TuiChoice("certificate", "🏅 Vytvořit SMART certifikát"));
        }

        return actions;
    }

    private void Render(TuiContext context)
    {
        var io = context.Io;
        io.Clear();
        io.Rule("SMART kontrola");

        if (_disk is null)
        {
            io.Line("[red]Není vybrán žádný disk.[/]");
            return;
        }

        io.Write(ScreenWidgets.KeyValue("Disk", new[]
        {
            ("Model", FirstNonEmpty(_smart?.DeviceModel, _disk.Model, _disk.Name)),
            ("Sériové číslo", FirstNonEmpty(_smart?.SerialNumber, _disk.SerialNumber, "-")),
            ("Firmware", FirstNonEmpty(_smart?.FirmwareVersion, _disk.FirmwareVersion, "-")),
            ("Typ", FirstNonEmpty(_smart?.DeviceType, "-")),
            ("Cesta", _disk.Path),
            ("Rozhraní", TuiFormats.BusTypeDisplay(_disk) + " · " + TuiFormats.Capacity(_disk.TotalSize)),
        }));

        if (_smart is null)
        {
            io.Write(ScreenWidgets.Notice(
                "SMART data nejsou dostupná",
                _status,
                TuiTheme.Warning,
                "⚠"));
            io.Line();
            return;
        }

        string grade = _result?.Rating?.Grade.ToString() ?? "N/A";
        double? score = _result?.Rating?.Score;
        io.Line($"Známka: {TuiTheme.GradeMarkup(grade)}   Skóre: {TuiTheme.ScoreMarkup(score)}   " +
                $"Stav: {(_smart.IsHealthy ? "[green]✅ Zdravý[/]" : "[yellow]⚠ Pozor[/]")}   " +
                $"Teplota: {TuiTheme.TemperatureMarkup(_smart.Temperature)}");

        if (_smart.IsFailing)
        {
            io.Write(ScreenWidgets.Notice(
                "⚠ KRITICKÉ: disk hlásí selhání",
                string.IsNullOrWhiteSpace(_smart.FailurePrediction)
                    ? "SMART predikuje selhání disku."
                    : _smart.FailurePrediction,
                TuiTheme.Danger,
                "⛔"));
        }

        var metrics = new List<(string, string)>
        {
            ("Provozní hodiny", _smart.PowerOnHours is > 0 ? TuiFormats.Number(_smart.PowerOnHours.Value) + " h" : "-"),
            ("Zapnutí", _smart.PowerCycleCount > 0 ? TuiFormats.Number(_smart.PowerCycleCount) : "-"),
            ("Realokované", Counter(_smart.ReallocatedSectorCount)),
            ("Čekající", Counter(_smart.PendingSectorCount)),
            ("Neopravitelné", Counter(_smart.UncorrectableErrorCount)),
        };

        if (_smart.WearLevelingCount is not null)
        {
            metrics.Add(("Wear leveling", _smart.WearLevelingCount.Value.ToString()));
        }

        if (_smart.PercentageUsed is not null)
        {
            metrics.Add(("% použito", _smart.PercentageUsed.Value + " %"));
        }

        if (_smart.AvailableSpare is not null)
        {
            metrics.Add(("Rezerva", _smart.AvailableSpare.Value + " %"));
        }

        if (_smart.MediaErrors is not null)
        {
            metrics.Add(("Media chyby", Counter(_smart.MediaErrors)));
        }

        if (_smart.UnsafeShutdowns is not null)
        {
            metrics.Add(("Nešetrná vypnutí", Counter(_smart.UnsafeShutdowns)));
        }

        var metricsTable = new Table()
            .Border(TableBorder.Rounded)
            .BorderColor(TuiTheme.Muted)
            .Title("[bold]Zdravotní metriky[/]");
        metricsTable.AddColumn(new TableColumn("[grey]Metrika[/]").NoWrap());
        metricsTable.AddColumn(new TableColumn("[grey]Hodnota[/]").NoWrap());
        metricsTable.AddColumn(new TableColumn("[grey]Metrika[/]").NoWrap());
        metricsTable.AddColumn(new TableColumn("[grey]Hodnota[/]").NoWrap());

        for (int index = 0; index < metrics.Count; index += 2)
        {
            var first = metrics[index];
            var second = index + 1 < metrics.Count ? metrics[index + 1] : (string.Empty, string.Empty);
            metricsTable.AddRow(
                Markup.Escape(first.Item1),
                Markup.Escape(first.Item2),
                Markup.Escape(second.Item1),
                Markup.Escape(second.Item2));
        }

        io.Write(metricsTable);
        io.Line();

        if (_smart.Attributes.Count > 0)
        {
            var table = new Table()
                .Border(TableBorder.Rounded)
                .BorderColor(TuiTheme.Muted)
                .Title("[bold]Klíčové SMART atributy[/]")
                .AddColumn("ID")
                .AddColumn("Atribut")
                .AddColumn(new TableColumn("Hod.").RightAligned())
                .AddColumn(new TableColumn("Nej.").RightAligned())
                .AddColumn(new TableColumn("Prah").RightAligned())
                .AddColumn("Stav");

            foreach (var attribute in _smart.Attributes.OrderBy(item => item.Id).Take(12))
            {
                string status = attribute.IsOk ? "[green]OK[/]" : "[red]VAROVÁNÍ[/]";
                table.AddRow(
                    attribute.Id.ToString(),
                    Markup.Escape(TuiFormats.Ellipsis(attribute.Name, 30)),
                    attribute.Current.ToString(),
                    attribute.Worst.ToString(),
                    attribute.Threshold.ToString(),
                    status);
            }

            io.Write(table);
        }

        if (_smart.SelfTests.Count > 0)
        {
            var last = _smart.SelfTests.First();
            io.Line($"[grey]Poslední self-test:[/] {last.TestTypeName} · {last.StatusName} · {last.FormattedHours}");
        }

        if (!string.IsNullOrWhiteSpace(_status))
        {
            io.Line($"[grey]{Markup.Escape(_status)}[/]");
        }

        context.RenderStatusBar();
    }

    private static string Counter(int? value) =>
        value is null or <= 0 ? "0 ✓" : value.Value.ToString();

    private static string FirstNonEmpty(params string?[] candidates) =>
        candidates.FirstOrDefault(candidate => !string.IsNullOrWhiteSpace(candidate)) ?? "-";

    private async Task RunSelfTestAsync(TuiContext context, SmartaSelfTestType type, CancellationToken cancellationToken)
    {
        if (_disk is null || _smart is null)
        {
            context.Dialogs.Warning("SMART", "SMART data nejsou dostupná, self-test nelze spustit.");
            return;
        }

        if (_disk.IsSystemDisk)
        {
            context.Dialogs.Error("SMART", "Na systémovém disku nelze spustit self-test. Vyberte jiný disk.");
            return;
        }

        string typeName = type == SmartaSelfTestType.ShortTest ? "krátký" : "rozšířený";
        if (!context.Dialogs.Confirm(
                "Spustit SMART self-test?",
                $"Spustit {typeName} self-test na disku:\n\n{_disk.Name}\n{_disk.Path}\n\n" +
                "Během testu může být disk dočasně nedostupný. Pokračovat?"))
        {
            return;
        }

        using var scope = context.CreateScope();
        var smartCheck = scope.ServiceProvider.GetRequiredService<SmartCheckService>();

        bool started = await smartCheck.StartSelfTestAsync(_disk, type, cancellationToken).ConfigureAwait(false);
        if (!started)
        {
            context.Dialogs.Error("SMART", "Self-test se nepodařilo spustit. Ověřte smartmontools a práva.");
            return;
        }

        var progress = new TuiProgress();
        progress.AddPhase(type == SmartaSelfTestType.ShortTest ? "Krátký self-test" : "Rozšířený self-test");
        progress.SetPhaseState(0, TuiPhaseState.Running, "spuštěno");

        var chart = new TuiChart { MinX = 0, MaxX = 100, XSuffix = " %", YFormat = "F0" };
        chart.AddSeries("Zbývá", TuiTheme.Primary);
        progress.Chart = chart;
        progress.ShowChart = false;

        using var cancellation = context.GetRequiredService<TuiCancellation>().BeginOperation();
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellation.Token, cancellationToken);

        await context.Io.RunLiveAsync("SMART self-test", progress, async state =>
        {
            var startedAt = DateTime.UtcNow;
            try
            {
                while (!linked.IsCancellationRequested)
                {
                    var status = await smartCheck.GetSelfTestStatusAsync(_disk, linked.Token).ConfigureAwait(false);
                    if (status is null)
                    {
                        state.AddLog("Stav self-testu nelze získat.");
                        break;
                    }

                    if (status == SmartaSelfTestStatus.InProgress)
                    {
                        state.Percent = Math.Max(state.Percent, 1);
                        state.Detail = "Self-test probíhá…";
                    }
                    else
                    {
                        state.Percent = 100;
                        state.SetPhaseState(0, status == SmartaSelfTestStatus.CompletedWithoutError
                            ? TuiPhaseState.Completed
                            : TuiPhaseState.Failed, status.ToString());
                        state.AddLog("Výsledek: " + status);
                        break;
                    }

                    state.UpdateFromElapsed(startedAt);
                    await Task.Delay(3000, linked.Token).ConfigureAwait(false);
                }
            }
            catch (OperationCanceledException)
            {
                state.SetPhaseState(0, TuiPhaseState.Skipped, "přerušeno");
            }
        }, cancellationToken).ConfigureAwait(false);

        await LoadAsync(context, cancellationToken).ConfigureAwait(false);
    }

    private async Task AbortSelfTestAsync(TuiContext context, CancellationToken cancellationToken)
    {
        if (_disk is null)
        {
            return;
        }

        if (!context.Dialogs.Confirm("Přerušit self-test?", "Opravdu chcete přerušit běžící self-test?"))
        {
            return;
        }

        using var scope = context.CreateScope();
        var smartCheck = scope.ServiceProvider.GetRequiredService<SmartCheckService>();
        bool aborted = await smartCheck.StartSelfTestAsync(_disk, SmartaSelfTestType.Abort, cancellationToken).ConfigureAwait(false);
        context.Dialogs.Info("SMART", aborted ? "Self-test přerušen." : "Self-test se nepodařilo přerušit.");
    }

    private async Task ShowSelfTestLogAsync(TuiContext context, CancellationToken cancellationToken)
    {
        if (_disk is null)
        {
            return;
        }

        using var scope = context.CreateScope();
        var smartCheck = scope.ServiceProvider.GetRequiredService<SmartCheckService>();
        IReadOnlyList<SmartaSelfTestEntry> log;
        try
        {
            log = await smartCheck.GetSelfTestLogAsync(_disk, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is IOException or InvalidOperationException)
        {
            context.Dialogs.Error("SMART", "Self-test log se nepodařilo načíst: " + ex.Message);
            return;
        }

        context.Io.Clear();
        context.Io.Rule("Self-test log");
        if (log.Count == 0)
        {
            context.Io.Line("[grey]Self-test log je prázdný.[/]");
        }
        else
        {
            var table = new Table()
                .Border(TableBorder.Rounded)
                .AddColumn("#")
                .AddColumn("Typ")
                .AddColumn("Stav")
                .AddColumn(new TableColumn("Hodiny").RightAligned())
                .AddColumn(new TableColumn("Průběh").RightAligned());

            foreach (var entry in log)
            {
                table.AddRow(
                    entry.Number.ToString(),
                    Markup.Escape(entry.TestTypeName),
                    Markup.Escape(entry.StatusName),
                    entry.FormattedHours,
                    entry.FormattedProgress);
            }

            context.Io.Write(table);
        }

        context.Io.Pause();
    }

    private void ShowAttributes(TuiContext context)
    {
        context.Io.Clear();
        context.Io.Rule("SMART atributy");

        if (_smart is null || _smart.Attributes.Count == 0)
        {
            context.Io.Line("[grey]SMART atributy nejsou dostupné.[/]");
            return;
        }

        var table = new Table()
            .Border(TableBorder.Rounded)
            .AddColumn("ID")
            .AddColumn("Atribut")
            .AddColumn(new TableColumn("Hodnota").RightAligned())
            .AddColumn(new TableColumn("Nejhorší").RightAligned())
            .AddColumn(new TableColumn("Prah").RightAligned())
            .AddColumn(new TableColumn("Raw").RightAligned())
            .AddColumn("Interpretace");

        foreach (var attribute in _smart.Attributes.OrderBy(item => item.Id))
        {
            table.AddRow(
                attribute.Id.ToString(),
                Markup.Escape(attribute.Name),
                attribute.Current.ToString(),
                attribute.Worst.ToString(),
                attribute.Threshold.ToString(),
                attribute.RawValue.ToString(),
                Markup.Escape(TuiFormats.Ellipsis(attribute.Interpretation, 40)));
        }

        context.Io.Write(table);
    }

    private async Task ShowRawOutputAsync(TuiContext context, CancellationToken cancellationToken)
    {
        context.Io.Clear();
        context.Io.Rule("Raw smartctl");
        context.Io.Line("[grey]Načítám smartctl -a…[/]");
        string output = await SmartctlRunner.RunAsync(_disk?.Path, cancellationToken).ConfigureAwait(false);
        foreach (string line in output.Split('\n'))
        {
            context.Io.Line(Markup.Escape(line));
        }

        context.Io.Pause();
    }

    private async Task ClearCacheAsync(TuiContext context)
    {
        if (_disk is null)
        {
            return;
        }

        if (context.GetRequiredService<ISmartaProvider>() is not IAdvancedSmartaProvider advanced)
        {
            context.Dialogs.Warning("SMART cache", "Tento poskytovatel SMART cache nepodporuje.");
            return;
        }

        await advanced.RemoveSmartCacheForDeviceAsync(_disk.Path).ConfigureAwait(false);
        context.Dialogs.Success("SMART cache", "Cache byla vymazána pro " + _disk.Path);
    }

    private async Task<ScreenResult?> SaveSmartCertificateAsync(TuiContext context, CancellationToken cancellationToken)
    {
        if (_disk is null || _smart is null || _result?.Rating is null)
        {
            context.Dialogs.Warning("Certifikát", "Nejprve načtěte SMART data.");
            return null;
        }

        try
        {
            using var scope = context.CreateScope();
            var cardService = scope.ServiceProvider.GetRequiredService<DiskCardTestService>();
            var card = await cardService.GetOrCreateCardAsync(_disk, _smart, cancellationToken).ConfigureAwait(false);
            var session = await cardService.SaveSmartCheckAsync(
                card,
                _smart,
                _result.Rating,
                TestType.SmartShort,
                BuildSmartCertificateNotes(_smart),
                cancellationToken).ConfigureAwait(false);

            context.Selection.SelectedDiskCardId = card.Id;
            context.Selection.SelectedTestSessionId = session.Id;
            context.Selection.SelectedCertificateId = null;
            context.StatusMessage = "SMART kontrola uložena do karty disku.";
            return ScreenResult.Navigate(ScreenIds.Certificate);
        }
        catch (Exception ex) when (ex is IOException or InvalidOperationException or Microsoft.EntityFrameworkCore.DbUpdateException)
        {
            context.Dialogs.Error("Certifikát", "SMART kontrolu se nepodařilo uložit: " + ex.Message);
            return null;
        }
    }

    internal static string BuildSmartCertificateNotes(SmartaData smart)
    {
        var notes = new List<string>
        {
            "SMART-only certifikát: nebyl proveden povrchový ani sanitizační test; závěr vychází pouze z aktuální SMART diagnostiky.",
        };

        if (smart.IsFailing)
        {
            notes.Add("Disk hlásí SMART selhání: " + (string.IsNullOrWhiteSpace(smart.FailurePrediction) ? "predikce selhání" : smart.FailurePrediction));
        }

        if (smart.ReallocatedSectorCount is > 0)
        {
            notes.Add("Realokované sektory: " + smart.ReallocatedSectorCount.Value);
        }

        if (smart.PendingSectorCount is > 0)
        {
            notes.Add("Čekající sektory: " + smart.PendingSectorCount.Value);
        }

        if (smart.UncorrectableErrorCount is > 0)
        {
            notes.Add("Neopravitelné chyby: " + smart.UncorrectableErrorCount.Value);
        }

        if (smart.PercentageUsed is >= 90)
        {
            notes.Add("SSD je opotřebeno na " + smart.PercentageUsed.Value + " %.");
        }

        return string.Join("; ", notes);
    }
}
