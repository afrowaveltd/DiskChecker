using System.Text;
using DiskChecker.TUI.Services;
using Spectre.Console;
using Spectre.Console.Rendering;

namespace DiskChecker.Tui.Tests.Support;

/// <summary>
/// Deterministic terminal used by the tests: rendered output is captured as plain
/// text and every prompt is answered from a scripted queue.
/// </summary>
public sealed class ScriptedTuiIo : ITuiIo
{
    private readonly List<string> _lines = new();
    private readonly Queue<string?> _choices = new();
    private readonly Queue<bool> _confirms = new();
    private readonly Queue<string?> _asks = new();

    public int Width { get; set; } = 120;

    public bool IsInteractive => false;

    public bool IsInputClosed => false;

    public IReadOnlyList<string> Lines => _lines;

    public TuiProgress? LastProgress { get; private set; }

    public IReadOnlyList<TuiChoice> LastChoices { get; private set; } = Array.Empty<TuiChoice>();

    public string LastChoiceTitle { get; private set; } = string.Empty;

    public int PauseCount { get; private set; }

    public string Output => string.Join("\n", _lines);

    public void EnqueueChoice(string? key) => _choices.Enqueue(key);

    public void EnqueueConfirm(bool value) => _confirms.Enqueue(value);

    public void EnqueueAsk(string? value) => _asks.Enqueue(value);

    public void Clear()
    {
        // Real terminals clear; the scripted one keeps a continuous transcript.
    }

    public void Write(IRenderable renderable)
    {
        _lines.Add(TuiIo.ToPlainText(renderable, Width).TrimEnd());
    }

    public void Line(string markup = "")
    {
        _lines.Add(string.IsNullOrEmpty(markup) ? string.Empty : Markup.Remove(markup));
    }

    public void Rule(string? titleMarkup = null) => _lines.Add("--- " + (titleMarkup is null ? string.Empty : Markup.Remove(titleMarkup)));

    public string? Choose(string title, IReadOnlyList<TuiChoice> choices, int pageSize = 18, bool allowCancel = true, string cancelKey = TuiKeys.Back)
    {
        LastChoices = choices;
        LastChoiceTitle = title;
        _lines.Add(title);
        foreach (var choice in choices)
        {
            _lines.Add("  " + choice.Label);
        }

        string? answer = _choices.Count > 0 ? _choices.Dequeue() : cancelKey;
        if (answer is not null && answer != cancelKey && answer != TuiKeys.Exit && answer != TuiKeys.MainMenu)
        {
            bool known = choices.Any(choice => choice.Key.Equals(answer, StringComparison.OrdinalIgnoreCase) && choice.Enabled);
            if (!known)
            {
                throw new InvalidOperationException($"Choice '{answer}' is not offered by '{title}'. Offered: " +
                                                    string.Join(", ", choices.Select(choice => choice.Key)));
            }
        }

        return answer;
    }

    public bool Confirm(string question, bool defaultValue = false)
    {
        _lines.Add(question + (defaultValue ? " [A/n]" : " [a/N]"));
        return _confirms.Count > 0 ? _confirms.Dequeue() : defaultValue;
    }

    public string? Ask(string question, string? defaultValue = null)
    {
        _lines.Add(question);
        return _asks.Count > 0 ? _asks.Dequeue() : defaultValue;
    }

    public void Pause(string? messageMarkup = null)
    {
        PauseCount++;
        _lines.Add(messageMarkup is null ? "(pause)" : Markup.Remove(messageMarkup));
    }

    public async Task RunLiveAsync(string title, TuiProgress progress, Func<TuiProgress, Task> body, CancellationToken cancellationToken = default)
    {
        progress.Title = title;
        await body(progress).ConfigureAwait(false);
        LastProgress = progress;
        foreach (string line in TuiProgressRenderer.BuildLines(progress.Snapshot(), Width))
        {
            _lines.Add(Markup.Remove(line));
        }
    }

    public bool Contains(string text) => Output.Contains(text, StringComparison.OrdinalIgnoreCase);

    public bool ContainsLine(string text) =>
        _lines.Any(line => line.Contains(text, StringComparison.OrdinalIgnoreCase));

    public override string ToString()
    {
        var builder = new StringBuilder();
        foreach (string line in _lines)
        {
            builder.AppendLine(line);
        }

        return builder.ToString();
    }
}
