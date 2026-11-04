using DiskChecker.TUI.Screens;
using DiskChecker.TUI.Services;

namespace DiskChecker.Tui.Tests.Support;

/// <summary>Screen scripted by a test: each render pulls the next behaviour.</summary>
public sealed class ScriptedScreenA : ITuiScreen
{
    public static readonly Queue<Func<TuiContext, ScreenResult>> Behaviour = new();

    public string Id => ScreenIds.DiskSelection;

    public string TitleKey => "Nav.DiskSelection";

    public static void Reset(params Func<TuiContext, ScreenResult>[] actions)
    {
        Behaviour.Clear();
        foreach (var action in actions)
        {
            Behaviour.Enqueue(action);
        }
    }

    public Task<ScreenResult> ShowAsync(TuiContext context, CancellationToken cancellationToken) =>
        Task.FromResult(Behaviour.Count > 0 ? Behaviour.Dequeue()(context) : ScreenResult.Exit());
}

/// <inheritdoc cref="ScriptedScreenA" />
public sealed class ScriptedScreenB : ITuiScreen
{
    public static readonly Queue<Func<TuiContext, ScreenResult>> Behaviour = new();
    public static int RenderCount { get; private set; }

    public string Id => ScreenIds.SmartCheck;

    public string TitleKey => "Nav.SmartCheck";

    public static void Reset(params Func<TuiContext, ScreenResult>[] actions)
    {
        Behaviour.Clear();
        RenderCount = 0;
        foreach (var action in actions)
        {
            Behaviour.Enqueue(action);
        }
    }

    public Task<ScreenResult> ShowAsync(TuiContext context, CancellationToken cancellationToken)
    {
        RenderCount++;
        return Task.FromResult(Behaviour.Count > 0 ? Behaviour.Dequeue()(context) : ScreenResult.Exit());
    }
}
