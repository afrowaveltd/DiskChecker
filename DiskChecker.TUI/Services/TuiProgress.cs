using System.Globalization;
using Spectre.Console;
using Spectre.Console.Rendering;

namespace DiskChecker.TUI.Services;

/// <summary>State of a single step in a long running operation.</summary>
public enum TuiPhaseState
{
    Pending,
    Running,
    Completed,
    Failed,
    Skipped,
}

/// <summary>One step of a multi phase operation.</summary>
public sealed class TuiPhaseLine
{
    public TuiPhaseLine(string name) => Name = name;

    public string Name { get; }

    public TuiPhaseState State { get; set; }

    public double Percent { get; set; }

    public string Detail { get; set; } = string.Empty;

    public string Glyph => State switch
    {
        TuiPhaseState.Completed => "[green]✔[/]",
        TuiPhaseState.Running => "[yellow]▶[/]",
        TuiPhaseState.Failed => "[red]✖[/]",
        TuiPhaseState.Skipped => "[grey]–[/]",
        _ => "[grey]·[/]",
    };
}

/// <summary>Immutable snapshot used while rendering the live panel.</summary>
public sealed record TuiProgressSnapshot(
    string Title,
    string Phase,
    string Detail,
    double Percent,
    string ElapsedText,
    string EtaText,
    string SpeedText,
    string TemperatureText,
    int Errors,
    IReadOnlyList<(string Label, string Value)> Metrics,
    IReadOnlyList<(string Name, TuiPhaseState State, double Percent, string Detail)> Phases,
    IReadOnlyList<string> Log,
    TuiChart? Chart,
    bool ShowChart);

/// <summary>
/// Thread safe progress state shared between a running test and the live renderer.
/// Every mutation is funnelled through a lock so background I/O can report safely.
/// </summary>
public sealed class TuiProgress
{
    private readonly object _gate = new();
    private readonly List<TuiPhaseLine> _phases = new();
    private readonly List<string> _log = new();
    private readonly List<(string Label, string Value)> _metrics = new();
    private string _phase = string.Empty;
    private string _detail = string.Empty;
    private double _percent;
    private string _elapsedText = "00:00:00";
    private string _etaText = "—";
    private string _speedText = "—";
    private string _temperatureText = "N/A";
    private int _errors;

    public TuiProgress(string? title = null) => Title = title ?? string.Empty;

    public string Title { get; set; }

    public TuiChart? Chart { get; set; }

    public bool ShowChart { get; set; } = true;

    public int MaxLogLines { get; set; } = 8;

    public double Percent
    {
        get { lock (_gate) { return _percent; } }
        set { lock (_gate) { _percent = Math.Clamp(value, 0, 100); } }
    }

    public string Phase
    {
        get { lock (_gate) { return _phase; } }
        set { lock (_gate) { _phase = value ?? string.Empty; } }
    }

    public string Detail
    {
        get { lock (_gate) { return _detail; } }
        set { lock (_gate) { _detail = value ?? string.Empty; } }
    }

    public string ElapsedText
    {
        get { lock (_gate) { return _elapsedText; } }
        set { lock (_gate) { _elapsedText = value ?? "—"; } }
    }

    public string EtaText
    {
        get { lock (_gate) { return _etaText; } }
        set { lock (_gate) { _etaText = value ?? "—"; } }
    }

    public string SpeedText
    {
        get { lock (_gate) { return _speedText; } }
        set { lock (_gate) { _speedText = value ?? "—"; } }
    }

    public string TemperatureText
    {
        get { lock (_gate) { return _temperatureText; } }
        set { lock (_gate) { _temperatureText = value ?? "N/A"; } }
    }

    public int Errors
    {
        get { lock (_gate) { return _errors; } }
        set { lock (_gate) { _errors = value; } }
    }

    public void AddPhase(string name) =>
        Update(phases => phases.Add(new TuiPhaseLine(name)));

    public void SetPhaseState(int index, TuiPhaseState state, string? detail = null, double? percent = null)
    {
        Update(phases =>
        {
            if (index < 0 || index >= phases.Count)
            {
                return;
            }

            var phase = phases[index];
            phase.State = state;
            if (detail is not null)
            {
                phase.Detail = detail;
            }

            if (percent is not null)
            {
                phase.Percent = percent.Value;
            }
            else if (state == TuiPhaseState.Completed)
            {
                phase.Percent = 100;
            }
        });
    }

    public void AddLog(string message)
    {
        lock (_gate)
        {
            _log.Add(message);
            while (_log.Count > 200)
            {
                _log.RemoveAt(0);
            }
        }
    }

    public void SetMetric(string label, string value)
    {
        lock (_gate)
        {
            _metrics.RemoveAll(metric => metric.Label == label);
            _metrics.Add((label, value));
        }
    }

    public void UpdateFromElapsed(DateTime startedUtc)
    {
        var elapsed = DateTime.UtcNow - startedUtc;
        ElapsedText = TuiFormats.Duration(elapsed);
        lock (_gate)
        {
            if (_percent > 0.5 && _percent < 99.9)
            {
                double total = elapsed.TotalSeconds / (_percent / 100d);
                var remaining = TimeSpan.FromSeconds(Math.Max(0, total - elapsed.TotalSeconds));
                _etaText = TuiFormats.Eta(remaining);
            }
            else if (_percent >= 99.9)
            {
                _etaText = "00:00";
            }
        }
    }

    public TuiProgressSnapshot Snapshot()
    {
        lock (_gate)
        {
            var phases = _phases
                .Select(phase => (phase.Name, phase.State, phase.Percent, phase.Detail))
                .ToArray();
            var log = _log.Skip(Math.Max(0, _log.Count - MaxLogLines)).ToArray();
            return new TuiProgressSnapshot(
                Title,
                _phase,
                _detail,
                _percent,
                _elapsedText,
                _etaText,
                _speedText,
                _temperatureText,
                _errors,
                _metrics.ToArray(),
                phases,
                log,
                Chart,
                ShowChart);
        }
    }

    private void Update(Action<List<TuiPhaseLine>> action)
    {
        lock (_gate)
        {
            action(_phases);
        }
    }
}

/// <summary>Renders <see cref="TuiProgress"/> state into a terminal panel.</summary>
public static class TuiProgressRenderer
{
    public static string[] BuildLines(TuiProgressSnapshot snapshot, int width)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        width = Math.Clamp(width, 40, 240);
        var lines = new List<string>
        {
            $"[bold]{Markup.Escape(snapshot.Title)}[/]",
        };

        if (!string.IsNullOrEmpty(snapshot.Phase))
        {
            lines.Add($"[{TuiTheme.Primary.ToMarkup()}]{Markup.Escape(snapshot.Phase)}[/]");
        }

        lines.Add($"[{TuiTheme.Primary.ToMarkup()}]{TuiFormats.Bar(snapshot.Percent, Math.Max(10, width - 12))}[/] " +
                  $"[bold]{snapshot.Percent.ToString("F1", CultureInfo.InvariantCulture)}%[/]");

        lines.Add(
            $"[grey]⏱ {snapshot.ElapsedText}[/]   [grey]⏳ {snapshot.EtaText}[/]   " +
            $"[grey]⚡ {Markup.Escape(snapshot.SpeedText)}[/]   " +
            $"[grey]🌡 {Markup.Escape(snapshot.TemperatureText)}[/]   " +
            $"[{(snapshot.Errors > 0 ? TuiTheme.Danger : TuiTheme.Muted).ToMarkup()}]⚠ {snapshot.Errors}[/]");

        if (!string.IsNullOrEmpty(snapshot.Detail))
        {
            lines.Add($"[grey]{Markup.Escape(TuiFormats.Ellipsis(snapshot.Detail, width - 2))}[/]");
        }

        if (snapshot.Metrics.Count > 0)
        {
            lines.Add(string.Join("   ", snapshot.Metrics.Select(metric =>
                $"[grey]{Markup.Escape(metric.Label)}:[/] [bold]{Markup.Escape(metric.Value)}[/]")));
        }

        if (snapshot.Phases.Count > 0)
        {
            lines.Add(string.Empty);
            foreach (var phase in snapshot.Phases)
            {
                string glyph = phase.State switch
                {
                    TuiPhaseState.Completed => "[green]✔[/]",
                    TuiPhaseState.Running => "[yellow]▶[/]",
                    TuiPhaseState.Failed => "[red]✖[/]",
                    TuiPhaseState.Skipped => "[grey]–[/]",
                    _ => "[grey]·[/]",
                };

                string name = Markup.Escape(TuiFormats.Ellipsis(phase.Name, 26));
                string detail = Markup.Escape(TuiFormats.Ellipsis(phase.Detail, Math.Max(8, width - 46)));
                string percent = phase.State == TuiPhaseState.Pending
                    ? string.Empty
                    : $" [grey]{phase.Percent.ToString("F0", CultureInfo.InvariantCulture)}%[/]";
                lines.Add($"{glyph} {name,-28}{percent,6}  [grey]{detail}[/]");
            }
        }

        if (snapshot.ShowChart && snapshot.Chart is { HasData: true } chart)
        {
            lines.Add(string.Empty);
            foreach (var row in chart.BuildLines(width - 4, 10))
            {
                lines.Add(Markup.Escape(row));
            }
        }

        if (snapshot.Log.Count > 0)
        {
            lines.Add(string.Empty);
            foreach (var entry in snapshot.Log)
            {
                lines.Add($"[grey]{Markup.Escape(TuiFormats.Ellipsis(entry, width - 2))}[/]");
            }
        }

        return lines.ToArray();
    }

    public static IRenderable Build(TuiProgressSnapshot snapshot, int width)
    {
        var lines = BuildLines(snapshot, width);
        return new Panel(new Markup(string.Join(Environment.NewLine, lines)))
        {
            Border = BoxBorder.Rounded,
            BorderStyle = new Style(foreground: TuiTheme.Primary),
            Padding = new Padding(1, 0, 1, 0),
        };
    }
}
