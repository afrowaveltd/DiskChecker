using System.Text;
using Spectre.Console;
using Spectre.Console.Rendering;

namespace DiskChecker.TUI.Services;

/// <summary>
/// Spectre.Console backed implementation of <see cref="ITuiIo"/>.
/// Works both on a real terminal and in redirected (scripted) mode, which keeps
/// the terminal build usable from automation and CI.
/// </summary>
public sealed class TuiIo : ITuiIo
{
    private readonly IAnsiConsole _console;
    private readonly TextReader _input;

    public TuiIo(IAnsiConsole? console = null, TextReader? input = null)
    {
        _console = console ?? AnsiConsole.Console;
        _input = input ?? Console.In;
        IsInteractive = !Console.IsInputRedirected && !Console.IsOutputRedirected;
    }

    public int Width => Math.Max(60, SafeWindowWidth());

    public bool IsInteractive { get; }

    /// <inheritdoc />
    public bool IsInputClosed { get; private set; }

    public void Clear()
    {
        if (IsInteractive)
        {
            _console.Clear();
        }
    }

    public void Write(IRenderable renderable) => _console.Write(renderable);

    public void Line(string markup = "")
    {
        if (string.IsNullOrEmpty(markup))
        {
            _console.WriteLine();
            return;
        }

        _console.MarkupLine(markup);
    }

    public void Rule(string? titleMarkup = null)
    {
        if (string.IsNullOrWhiteSpace(titleMarkup))
        {
            _console.Write(new Rule());
            return;
        }

        _console.Write(new Rule($"[bold yellow]{titleMarkup}[/]"));
    }

    public string? Choose(
        string title,
        IReadOnlyList<TuiChoice> choices,
        int pageSize = 18,
        bool allowCancel = true,
        string cancelKey = TuiKeys.Back)
    {
        ArgumentNullException.ThrowIfNull(choices);
        var enabled = choices.Where(choice => choice.Enabled).ToList();
        if (enabled.Count == 0)
        {
            return allowCancel ? cancelKey : null;
        }

        if (IsInteractive)
        {
            var prompt = new SelectionPrompt<TuiChoice>()
                .Title($"[bold]{Markup.Escape(title)}[/]")
                .PageSize(Math.Max(3, Math.Min(pageSize, enabled.Count + 1)))
                .UseConverter(choice => choice.Description is null
                    ? choice.Label
                    : $"{choice.Label} [grey]{Markup.Escape(choice.Description)}[/]")
                .HighlightStyle(new Style(foreground: TuiTheme.Primary, decoration: Decoration.Bold));

            foreach (var choice in enabled)
            {
                prompt.AddChoice(choice);
            }

            if (allowCancel)
            {
                prompt.AddChoice(new TuiChoice(cancelKey, "↩ Zpět"));
            }

            return _console.Prompt(prompt).Key;
        }

        // Redirected stdin: accept a key or a 1-based index on its own line.
        _console.MarkupLine($"[bold]{Markup.Escape(title)}[/]");
        for (int index = 0; index < enabled.Count; index++)
        {
            var choice = enabled[index];
            _console.MarkupLine($"  [grey]{index + 1,2})[/] {Markup.Escape(choice.Label)}" +
                                (choice.Description is null ? string.Empty : $" [grey]{Markup.Escape(choice.Description)}[/]"));
        }

        if (allowCancel)
        {
            _console.MarkupLine("  [grey] 0) Zpět[/]");
        }

        _console.Markup("[grey]> [/]");
        string? answer = _input.ReadLine();
        if (answer is null)
        {
            IsInputClosed = true;
            return cancelKey;
        }

        if (string.IsNullOrWhiteSpace(answer))
        {
            return allowCancel ? cancelKey : enabled[0].Key;
        }

        answer = answer.Trim();
        if (int.TryParse(answer, out int numeric))
        {
            if (numeric == 0)
            {
                return allowCancel ? cancelKey : enabled[0].Key;
            }

            if (numeric >= 1 && numeric <= enabled.Count)
            {
                return enabled[numeric - 1].Key;
            }
        }

        var byKey = enabled.FirstOrDefault(choice => choice.Key.Equals(answer, StringComparison.OrdinalIgnoreCase));
        if (byKey is not null)
        {
            return byKey.Key;
        }

        var byLabel = enabled.FirstOrDefault(choice => choice.Label.Equals(answer, StringComparison.OrdinalIgnoreCase));
        return byLabel?.Key ?? (allowCancel ? cancelKey : enabled[0].Key);
    }

    public bool Confirm(string question, bool defaultValue = false)
    {
        if (IsInteractive)
        {
            return _console.Confirm($"[bold]{Markup.Escape(question)}[/]", defaultValue);
        }

        _console.Markup($"[bold]{Markup.Escape(question)}[/] [grey]{(defaultValue ? "A/n" : "a/N")}[/] ");
        string? raw = _input.ReadLine();
        if (raw is null)
        {
            IsInputClosed = true;
            return defaultValue;
        }

        string answer = raw.Trim();
        if (answer.Length == 0)
        {
            return defaultValue;
        }

        return answer[0] is 'y' or 'Y' or 'a' or 'A' || answer.StartsWith("yes", StringComparison.OrdinalIgnoreCase);
    }

    public string? Ask(string question, string? defaultValue = null)
    {
        if (IsInteractive)
        {
            var prompt = new TextPrompt<string>($"[bold]{Markup.Escape(question)}[/]").AllowEmpty();
            if (!string.IsNullOrEmpty(defaultValue))
            {
                prompt.DefaultValue(defaultValue);
            }

            return _console.Prompt(prompt);
        }

        _console.Markup($"[bold]{Markup.Escape(question)}[/]" +
                        (string.IsNullOrEmpty(defaultValue) ? string.Empty : $" [grey]({Markup.Escape(defaultValue)})[/]") +
                        " ");
        string? answer = _input.ReadLine();
        if (answer is null)
        {
            IsInputClosed = true;
            return defaultValue;
        }

        return string.IsNullOrWhiteSpace(answer) ? defaultValue : answer.Trim();
    }

    public void Pause(string? messageMarkup = null)
    {
        if (!IsInteractive)
        {
            return;
        }

        _console.MarkupLine(messageMarkup ?? "[grey]Stiskněte Enter pro pokračování…[/]");
        _input.ReadLine();
    }

    public async Task RunLiveAsync(
        string title,
        TuiProgress progress,
        Func<TuiProgress, Task> body,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(progress);
        ArgumentNullException.ThrowIfNull(body);
        progress.Title = title;

        if (!IsInteractive)
        {
            await body(progress).ConfigureAwait(false);
            return;
        }

        var display = new LiveDisplay(_console, TuiProgressRenderer.Build(progress.Snapshot(), Width));
        await display.StartAsync(async context =>
        {
            var work = Task.Run(() => body(progress));

            while (!work.IsCompleted)
            {
                try
                {
                    await Task.Delay(120, cancellationToken).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    break;
                }

                context.UpdateTarget(TuiProgressRenderer.Build(progress.Snapshot(), Width));
                context.Refresh();
            }

            context.UpdateTarget(TuiProgressRenderer.Build(progress.Snapshot(), Width));
            context.Refresh();
            await work.ConfigureAwait(false);
        }).ConfigureAwait(false);
    }

    private static int SafeWindowWidth()
    {
        try
        {
            return Console.WindowWidth;
        }
        catch (IOException)
        {
            return 100;
        }
        catch (PlatformNotSupportedException)
        {
            return 100;
        }
    }

    /// <summary>Renders a renderable to plain text (used by diagnostics and tests).</summary>
    public static string ToPlainText(IRenderable renderable, int width = 100)
    {
        ArgumentNullException.ThrowIfNull(renderable);
        var writer = new StringWriter(new StringBuilder());
        var console = AnsiConsole.Create(new AnsiConsoleSettings
        {
            Ansi = AnsiSupport.No,
            ColorSystem = ColorSystemSupport.NoColors,
            Interactive = InteractionSupport.No,
            Out = new AnsiConsoleOutput(writer),
        });
        console.Profile.Width = width;
        console.Write(renderable);
        return writer.ToString();
    }
}