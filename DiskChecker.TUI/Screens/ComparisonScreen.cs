using System.Globalization;
using DiskChecker.Core.Interfaces;
using DiskChecker.Core.Models;
using DiskChecker.TUI.Services;
using Microsoft.Extensions.DependencyInjection;
using Spectre.Console;

namespace DiskChecker.TUI.Screens;

/// <summary>
/// Disk comparison workspace: pick 2 to 5 tested disks, rank them through the shared
/// comparison service, compare two disks in detail and locate the best disk.
/// Terminal counterpart of the desktop <c>DiskComparisonViewModel</c>.
/// </summary>
public sealed class ComparisonScreen : ITuiScreen
{
    private const int MinimumSelection = 2;
    private const int MaximumSelection = 5;

    private readonly List<DiskCard> _available = new();
    private readonly List<DiskCard> _selected = new();

    private List<DiskComparisonResult> _results = new();
    private DiskCard? _bestDisk;
    private string _status = string.Empty;
    private bool _loaded;

    /// <inheritdoc />
    public string Id => ScreenIds.Comparison;

    /// <inheritdoc />
    public string TitleKey => "Nav.Comparison";

    /// <inheritdoc />
    public async Task<ScreenResult> ShowAsync(TuiContext context, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);

        if (!_loaded)
        {
            await LoadAvailableAsync(context, cancellationToken).ConfigureAwait(false);
        }

        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Render(context);

            string? action = context.Io.Choose(context.L("Tui.Comparison.Actions"), BuildActions(context), pageSize: 12);
            switch (action)
            {
                case "refresh":
                    await LoadAvailableAsync(context, cancellationToken).ConfigureAwait(false);
                    break;
                case "add":
                    await AddDiskAsync(context).ConfigureAwait(false);
                    break;
                case "remove":
                    await RemoveDiskAsync(context).ConfigureAwait(false);
                    break;
                case "compare":
                    await CompareAsync(context, cancellationToken).ConfigureAwait(false);
                    break;
                case "performance":
                    await ComparePerformanceAsync(context, cancellationToken).ConfigureAwait(false);
                    break;
                case "best":
                    await SelectBestDiskAsync(context, cancellationToken).ConfigureAwait(false);
                    break;
                case TuiKeys.Exit:
                    return ScreenResult.Exit();
                case TuiKeys.MainMenu:
                    return ScreenResult.Menu();
                default:
                    return ScreenResult.Back();
            }
        }
    }

    // ========== Data ==========

    private async Task LoadAvailableAsync(TuiContext context, CancellationToken cancellationToken)
    {
        _status = context.L("Tui.Comparison.Status.Loading");

        try
        {
            var disks = await context.UseScopedAsync(provider =>
                provider.GetRequiredService<IDiskCardRepository>().GetActiveAsync()).ConfigureAwait(false);

            _available.Clear();
            _available.AddRange(disks
                .Where(card => card.TestCount > 0)
                .OrderByDescending(card => card.OverallScore)
                .ThenBy(card => card.ModelName, StringComparer.OrdinalIgnoreCase));

            _selected.RemoveAll(selected => _available.All(card => card.Id != selected.Id));
            _loaded = true;
            _status = _available.Count == 0
                ? context.L("Tui.Comparison.Status.NoDisks")
                : context.L("Tui.Comparison.Status.Loaded", _available.Count);
        }
        catch (Exception ex) when (IsServiceFailure(ex))
        {
            _loaded = true;
            _status = context.L("Tui.Comparison.Status.LoadError", ex.Message);
        }

    }

    // ========== Actions ==========

    private IReadOnlyList<TuiChoice> BuildActions(TuiContext context)
    {
        return new List<TuiChoice>
        {
            new("refresh", "🔄 " + context.L("Common.Refresh")),
            new("add", "➕ " + context.L("Tui.Comparison.Action.Add"), Enabled: _available.Count > _selected.Count),
            new("remove", "➖ " + context.L("Tui.Comparison.Action.Remove"), Enabled: _selected.Count > 0),
            new("compare", "⚖ " + context.L("Tui.Comparison.Action.Compare"), Enabled: _selected.Count >= MinimumSelection),
            new("performance", "🏁 " + context.L("Tui.Comparison.Action.Performance"), Enabled: _selected.Count == MinimumSelection),
            new("best", "🏆 " + context.L("Tui.Comparison.Action.Best"), Enabled: _available.Count > 0),
            new(TuiKeys.Back, context.L("Common.Back")),
            new(TuiKeys.MainMenu, "☰ " + context.L("Tui.Common.Menu")),
        };
    }

    private Task AddDiskAsync(TuiContext context)
    {
        var candidates = _available.Where(card => _selected.All(selected => selected.Id != card.Id)).ToList();
        if (candidates.Count == 0)
        {
            _status = context.L("Tui.Comparison.Status.AllSelected");
            return Task.CompletedTask;
        }

        var choices = candidates
            .Select(card => new TuiChoice(
                "add-" + card.Id.ToString(CultureInfo.InvariantCulture),
                TuiFormats.Ellipsis(card.ModelName, 34),
                TuiFormats.Capacity(card.Capacity)))
            .ToList();

        string? choice = context.Io.Choose(context.L("Tui.Comparison.Action.Add"), choices, pageSize: 12);
        var card = ResolveChoice(choice, "add-", candidates);
        if (card is null)
        {
            return Task.CompletedTask;
        }

        if (_selected.Count >= MaximumSelection)
        {
            _status = context.L("Tui.Comparison.Status.Max");
            return Task.CompletedTask;
        }

        _selected.Add(card);
        _status = context.L("Tui.Comparison.Status.Selected", _selected.Count);
        return Task.CompletedTask;
    }

    private Task RemoveDiskAsync(TuiContext context)
    {
        if (_selected.Count == 0)
        {
            return Task.CompletedTask;
        }

        var choices = _selected
            .Select(card => new TuiChoice(
                "remove-" + card.Id.ToString(CultureInfo.InvariantCulture),
                TuiFormats.Ellipsis(card.ModelName, 34),
                TuiFormats.HealthStatus(card.OverallGrade)))
            .ToList();

        string? choice = context.Io.Choose(context.L("Tui.Comparison.Action.Remove"), choices, pageSize: 12);
        var card = ResolveChoice(choice, "remove-", _selected);
        if (card is null)
        {
            return Task.CompletedTask;
        }

        _selected.RemoveAll(selected => selected.Id == card.Id);
        _status = context.L("Tui.Comparison.Status.Selected", _selected.Count);
        return Task.CompletedTask;
    }

    private async Task CompareAsync(TuiContext context, CancellationToken cancellationToken)
    {
        if (_selected.Count < MinimumSelection)
        {
            _status = context.L("Tui.Comparison.Status.NeedTwo");
            return;
        }

        _status = context.L("Comparison.Running");
        var ids = _selected.Select(card => card.Id).ToList();

        try
        {
            var results = await context.UseScopedAsync(provider =>
                provider.GetRequiredService<IDiskComparisonService>().CompareAsync(ids)).ConfigureAwait(false);

            _results = results.OrderBy(result => result.Rank).ToList();
            _bestDisk = _results.FirstOrDefault()?.Disk;
            _status = context.L("Comparison.Completed");
        }
        catch (Exception ex) when (IsServiceFailure(ex))
        {
            _results = new List<DiskComparisonResult>();
            _status = context.L("Tui.Comparison.Status.CompareFailed", ex.Message);
        }
    }

    private async Task ComparePerformanceAsync(TuiContext context, CancellationToken cancellationToken)
    {
        if (_selected.Count != MinimumSelection)
        {
            _status = context.L("Tui.Comparison.Status.NeedExactlyTwo");
            return;
        }

        _status = context.L("Tui.Comparison.Status.ComparingPerformance");
        try
        {
            int firstId = _selected[0].Id;
            int secondId = _selected[1].Id;
            var comparison = await context.UseScopedAsync(provider =>
                provider.GetRequiredService<IDiskComparisonService>().ComparePerformanceAsync(firstId, secondId)).ConfigureAwait(false);

            context.Dialogs.Notice(context.L("Comparison.Performance"), BuildPerformanceText(context, comparison));
            _status = context.L("Tui.Comparison.Status.PerformanceDone");
        }
        catch (Exception ex) when (IsServiceFailure(ex))
        {
            _status = context.L("Tui.Comparison.Status.CompareFailed", ex.Message);
        }
    }

    private async Task SelectBestDiskAsync(TuiContext context, CancellationToken cancellationToken)
    {
        _status = context.L("Tui.Comparison.Status.SearchingBest");
        try
        {
            var best = await context.UseScopedAsync(provider =>
                provider.GetRequiredService<IDiskCardRepository>().GetBestDisksAsync(1)).ConfigureAwait(false);

            _bestDisk = best.FirstOrDefault();
            if (_bestDisk is null)
            {
                _status = context.L("Tui.Comparison.Status.NoDisks");
                return;
            }

            if (_selected.All(card => card.Id != _bestDisk.Id))
            {
                if (_selected.Count >= MaximumSelection)
                {
                    _selected.RemoveAt(_selected.Count - 1);
                }

                _selected.Insert(0, _bestDisk);
            }

            _status = context.L("Tui.Comparison.Status.Best", _bestDisk.ModelName, _bestDisk.OverallScore);
        }
        catch (Exception ex) when (IsServiceFailure(ex))
        {
            _status = context.L("Tui.Comparison.Status.BestFailed", ex.Message);
        }
    }

    // ========== Rendering ==========

    private void Render(TuiContext context)
    {
        context.Io.Clear();
        context.Io.Write(new Rule($"[bold]{Markup.Escape(context.L("Comparison.Title"))}[/] " +
                                 $"[grey]{Markup.Escape(context.L("Comparison.Description"))}[/]")
        {
            Justification = Justify.Left,
            Style = new Style(foreground: TuiTheme.Primary),
        });

        if (!string.IsNullOrWhiteSpace(_status))
        {
            context.Io.Line($"[grey]{Markup.Escape(_status)}[/]");
        }

        RenderAvailable(context);
        RenderSelection(context);
        RenderResults(context);
        RenderBestDisk(context);
    }

    private void RenderAvailable(TuiContext context)
    {
        var table = new Table()
            .Border(TableBorder.Rounded)
            .BorderColor(TuiTheme.Muted)
            .Title($"[bold]{Markup.Escape(context.L("Tui.Comparison.Available"))}[/]")
            .AddColumn(new TableColumn("#") { Alignment = Justify.Right })
            .AddColumn(new TableColumn(context.L("Model")))
            .AddColumn(new TableColumn(context.L("Grade")) { Alignment = Justify.Center })
            .AddColumn(new TableColumn(context.L("Score")) { Alignment = Justify.Right })
            .AddColumn(new TableColumn(context.L("Tui.Column.Tests")) { Alignment = Justify.Right })
            .AddColumn(new TableColumn(context.L("Tui.Column.LastTested")));

        if (_available.Count == 0)
        {
            table.AddRow("-", Markup.Escape(context.L("Tui.Comparison.Status.NoDisks")), "[grey]?[/]", "-", "-", "-");
        }
        else
        {
            for (int index = 0; index < _available.Count; index++)
            {
                var card = _available[index];
                bool selected = _selected.Any(item => item.Id == card.Id);
                table.AddRow(
                    (index + 1).ToString(CultureInfo.InvariantCulture),
                    Markup.Escape((selected ? "▶ " : string.Empty) + TuiFormats.Ellipsis(card.ModelName, 32)),
                    TuiTheme.GradeMarkup(card.OverallGrade),
                    card.OverallScore.ToString("F0", CultureInfo.InvariantCulture),
                    card.TestCount.ToString("N0", CultureInfo.InvariantCulture),
                    Markup.Escape(card.LastTestedAt == default
                        ? context.L("Common.NA")
                        : card.LastTestedAt.ToString("g", CultureInfo.CurrentCulture)));
            }
        }

        context.Io.Write(table);
    }

    private void RenderSelection(TuiContext context)
    {
        context.Io.Line($"[bold]{Markup.Escape(context.L("Tui.Comparison.Selection", _selected.Count, MaximumSelection))}[/]");

        if (_selected.Count == 0)
        {
            context.Io.Write(new Panel(new Markup(Markup.Escape(context.L("Tui.Comparison.Selection.Empty"))))
            {
                Border = BoxBorder.Rounded,
                BorderStyle = new Style(foreground: TuiTheme.Warning),
                Padding = new Padding(1, 0, 1, 0),
            });
            return;
        }

        var table = new Table().Border(TableBorder.Rounded).BorderColor(TuiTheme.Muted).HideHeaders();
        table.AddColumn(new TableColumn("#") { Alignment = Justify.Right });
        table.AddColumn(new TableColumn(context.L("Model")));
        table.AddColumn(new TableColumn(context.L("Grade")) { Alignment = Justify.Center });
        table.AddColumn(new TableColumn(context.L("Score")) { Alignment = Justify.Right });

        for (int index = 0; index < _selected.Count; index++)
        {
            var card = _selected[index];
            table.AddRow(
                (index + 1).ToString(CultureInfo.InvariantCulture),
                Markup.Escape(TuiFormats.Ellipsis(card.ModelName, 32)),
                TuiTheme.GradeMarkup(card.OverallGrade),
                card.OverallScore.ToString("F0", CultureInfo.InvariantCulture));
        }

        context.Io.Write(table);
    }

    private void RenderResults(TuiContext context)
    {
        if (_results.Count == 0)
        {
            return;
        }

        var table = new Table()
            .Border(TableBorder.Rounded)
            .BorderColor(TuiTheme.Muted)
            .Title($"[bold]{Markup.Escape(context.L("Tui.Comparison.Results"))}[/]")
            .AddColumn(new TableColumn(context.L("Rank")) { Alignment = Justify.Right })
            .AddColumn(new TableColumn(context.L("Model")))
            .AddColumn(new TableColumn(context.L("Grade")) { Alignment = Justify.Center })
            .AddColumn(new TableColumn(context.L("Score")) { Alignment = Justify.Right })
            .AddColumn(new TableColumn(context.L("Write")) { Alignment = Justify.Right })
            .AddColumn(new TableColumn(context.L("Read")) { Alignment = Justify.Right })
            .AddColumn(new TableColumn(context.L("Errors")) { Alignment = Justify.Right })
            .AddColumn(new TableColumn(context.L("Tui.Comparison.Column.Recommendation")));

        foreach (var result in _results)
        {
            table.AddRow(
                result.Rank.ToString(CultureInfo.InvariantCulture),
                Markup.Escape(TuiFormats.Ellipsis(result.Disk.ModelName, 28)),
                TuiTheme.GradeMarkup(result.Grade),
                TuiTheme.ScoreMarkup(result.Score),
                Markup.Escape(TuiFormats.Speed(result.AvgWriteSpeed)),
                Markup.Escape(TuiFormats.Speed(result.AvgReadSpeed)),
                result.ErrorCount.ToString("N0", CultureInfo.InvariantCulture),
                Markup.Escape(result.Recommendation));
        }

        context.Io.Write(table);

        context.Io.Line($"[grey]{Markup.Escape(context.L("Tui.Comparison.ScoreBars"))}[/]");
        foreach (var result in _results)
        {
            string bar = TuiFormats.Bar(result.Score, 28);
            context.Io.Line($"[{TuiTheme.HealthScoreColor(result.Score).ToMarkup()}]{bar}[/] " +
                            $"[bold]{result.Score.ToString("F0", CultureInfo.InvariantCulture)}[/] " +
                            $"[grey]{Markup.Escape(TuiFormats.Ellipsis(result.Disk.ModelName, 24))}[/]");
        }
    }

    private void RenderBestDisk(TuiContext context)
    {
        string text = _bestDisk is null
            ? context.L("Tui.Comparison.Best.None")
            : context.L(
                "Tui.Comparison.Best.Line",
                _bestDisk.ModelName,
                _bestDisk.OverallGrade,
                _bestDisk.OverallScore);

        context.Io.Write(new Panel(new Markup($"[bold]{Markup.Escape(text)}[/]"))
        {
            Border = BoxBorder.Rounded,
            BorderStyle = new Style(foreground: _bestDisk is null ? TuiTheme.Muted : TuiTheme.Success),
            Padding = new Padding(1, 0, 1, 0),
        });
    }

    // ========== Helpers ==========

    private static string BuildPerformanceText(TuiContext context, PerformanceComparison comparison)
    {
        var lines = new List<string>
        {
            context.L("Tui.Comparison.Performance.Faster", comparison.FasterDisk),
            context.L("Tui.Comparison.Performance.WriteDifference", comparison.WriteSpeedDifference, comparison.SpeedAdvantagePercent),
            context.L("Tui.Comparison.Performance.ReadDifference", comparison.ReadSpeedDifference),
            string.Empty,
            context.L("Tui.Comparison.Performance.Reliable", comparison.MoreReliableDisk),
            context.L("Tui.Comparison.Performance.Recommended", comparison.RecommendedDisk),
            string.Empty,
            comparison.Summary,
        };

        return string.Join(Environment.NewLine, lines);
    }

    private static DiskCard? ResolveChoice(string? choice, string prefix, IReadOnlyList<DiskCard> source)
    {
        if (choice is null || choice == TuiKeys.Back || !choice.StartsWith(prefix, StringComparison.Ordinal))
        {
            return null;
        }

        if (!int.TryParse(choice[prefix.Length..], NumberStyles.Integer, CultureInfo.InvariantCulture, out int id))
        {
            return null;
        }

        return source.FirstOrDefault(card => card.Id == id);
    }

    private static bool IsServiceFailure(Exception exception) =>
        exception is IOException or UnauthorizedAccessException or InvalidOperationException
            or NotSupportedException or ArgumentException or FormatException or TimeoutException;
}