using Spectre.Console;
using Spectre.Console.Rendering;

namespace DiskChecker.TUI.Services;

/// <summary>Keys produced by the navigation prompts.</summary>
public static class TuiKeys
{
    public const string Back = "back";
    public const string Exit = "exit";
    public const string MainMenu = "menu";
}

/// <summary>One selectable item offered by a screen.</summary>
public sealed record TuiChoice(string Key, string Label, string? Description = null, bool Enabled = true);

/// <summary>
/// Abstraction over the terminal. Screens never touch Spectre directly so the
/// complete user experience can be exercised by tests with a scripted implementation.
/// </summary>
public interface ITuiIo
{
    /// <summary>Usable terminal width in columns.</summary>
    int Width { get; }

    /// <summary>True when the process owns an interactive terminal.</summary>
    bool IsInteractive { get; }

    /// <summary>
    /// True once standard input reached end of file (scripted runs). The shell stops
    /// instead of looping on cancelled prompts.
    /// </summary>
    bool IsInputClosed { get; }

    void Clear();

    void Write(IRenderable renderable);

    void Line(string markup = "");

    void Rule(string? titleMarkup = null);

    /// <summary>
    /// Shows a list of actions and returns the key of the chosen one.
    /// Returns <see cref="TuiKeys.Back"/> when the user cancels.
    /// </summary>
    string? Choose(
        string title,
        IReadOnlyList<TuiChoice> choices,
        int pageSize = 18,
        bool allowCancel = true,
        string cancelKey = TuiKeys.Back);

    bool Confirm(string question, bool defaultValue = false);

    string? Ask(string question, string? defaultValue = null);

    /// <summary>Waits for the user (interactive terminals only).</summary>
    void Pause(string? messageMarkup = null);

    /// <summary>
    /// Runs <paramref name="body"/> while an updating progress panel is displayed.
    /// The body updates <paramref name="progress"/>; the panel repaints on a timer.
    /// </summary>
    Task RunLiveAsync(string title, TuiProgress progress, Func<TuiProgress, Task> body, CancellationToken cancellationToken = default);
}
