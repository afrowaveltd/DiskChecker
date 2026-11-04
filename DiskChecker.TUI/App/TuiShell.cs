using DiskChecker.TUI.Screens;
using DiskChecker.TUI.Services;
using Microsoft.Extensions.DependencyInjection;
using Spectre.Console;

namespace DiskChecker.TUI.App;

/// <summary>
/// Application shell: renders the header/navigation/status frame and runs the
/// screen stack. Mirrors the desktop window chrome (header, navigation, system
/// info bar and status bar).
/// </summary>
public sealed class TuiShell
{
    private readonly IServiceProvider _provider;
    private readonly TuiContext _context;
    private readonly TuiCancellation _cancellation;
    private readonly Stack<ScreenEntry> _stack = new();
    private bool _bannerShown;

    public TuiShell(
        IServiceProvider provider,
        TuiContext context,
        TuiCancellation cancellation,
        IReadOnlyDictionary<string, Type>? screenTypes = null)
    {
        _provider = provider ?? throw new ArgumentNullException(nameof(provider));
        _context = context ?? throw new ArgumentNullException(nameof(context));
        _cancellation = cancellation ?? throw new ArgumentNullException(nameof(cancellation));
        _screenTypes = screenTypes ?? DefaultScreenTypesCatalog;
    }

    private readonly IReadOnlyDictionary<string, Type> _screenTypes;

    private sealed record ScreenEntry(string Id, ITuiScreen Screen);

    private static readonly (string Id, string Key, string Icon)[] Navigation =
    {
        (ScreenIds.DiskSelection, "Nav.DiskSelection", "💾"),
        (ScreenIds.DiskCards, "Nav.DiskCards", "🗂"),
        (ScreenIds.SurfaceTest, "Nav.SurfaceTest", "📈"),
        (ScreenIds.SmartCheck, "Nav.SmartCheck", "🔬"),
        (ScreenIds.SeekTest, "Nav.SeekTest", "🎯"),
        (ScreenIds.AbsoluteDestructive, "Nav.DestructiveTest", "⚠"),
        (ScreenIds.SafeDestructive, "Nav.SafeDestructiveTest", "🛡"),
        (ScreenIds.Analysis, "Nav.Analysis", "📊"),
        (ScreenIds.Comparison, "Nav.Comparison", "⚖"),
        (ScreenIds.Report, "Nav.Report", "📄"),
        (ScreenIds.History, "Nav.History", "📜"),
        (ScreenIds.Certificates, "Nav.Certificates", "🏅"),
        (ScreenIds.Backup, "Nav.Backup", "💾"),
        (ScreenIds.Restore, "Nav.Restore", "♻"),
        (ScreenIds.Settings, "Nav.Settings", "⚙"),
        (ScreenIds.About, "Nav.About", "ℹ"),
    };

    private static readonly Dictionary<string, Type> DefaultScreenTypesCatalog = new(StringComparer.OrdinalIgnoreCase)
    {
        [ScreenIds.DiskSelection] = typeof(DiskSelectionScreen),
        [ScreenIds.DiskCards] = typeof(DiskCardsScreen),
        [ScreenIds.DiskCardDetail] = typeof(DiskCardDetailScreen),
        [ScreenIds.SurfaceTest] = typeof(SurfaceTestScreen),
        [ScreenIds.SmartCheck] = typeof(SmartCheckScreen),
        [ScreenIds.SeekTest] = typeof(SeekTestScreen),
        [ScreenIds.AbsoluteDestructive] = typeof(AbsoluteDestructiveScreen),
        [ScreenIds.SafeDestructive] = typeof(SafeDestructiveScreen),
        [ScreenIds.Analysis] = typeof(AnalysisScreen),
        [ScreenIds.Comparison] = typeof(ComparisonScreen),
        [ScreenIds.Report] = typeof(ReportScreen),
        [ScreenIds.History] = typeof(HistoryScreen),
        [ScreenIds.Certificates] = typeof(CertificateBrowserScreen),
        [ScreenIds.Certificate] = typeof(CertificateScreen),
        [ScreenIds.Backup] = typeof(BackupScreen),
        [ScreenIds.Restore] = typeof(RestoreScreen),
        [ScreenIds.Settings] = typeof(SettingsScreen),
        [ScreenIds.About] = typeof(AboutScreen),
    };

    public async Task<int> RunAsync(string startScreen, CancellationToken cancellationToken = default)
    {
        if (!_screenTypes.ContainsKey(startScreen))
        {
            _context.Io.Line($"[red]Neznámá obrazovka: {Markup.Escape(startScreen)}[/]");
            startScreen = ScreenIds.DiskSelection;
        }

        ShowBanner();
        _stack.Clear();
        Push(startScreen);

        while (_stack.Count > 0)
        {
            if (cancellationToken.IsCancellationRequested || _cancellation.ExitRequested)
            {
                return 0;
            }

            var entry = _stack.Peek();
            RenderFrame(entry);

            ScreenResult result;
            try
            {
                result = await entry.Screen.ShowAsync(_context, cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                return 0;
            }
            catch (Exception ex)
            {
                _context.Io.Line($"[red]Chyba obrazovky: {Markup.Escape(ex.Message)}[/]");
                _context.Io.Pause();
                result = ScreenResult.Stay();
            }

            if (_context.Io.IsInputClosed)
            {
                _context.Io.Line("[grey]Vstup byl uzavřen – ukončuji.[/]");
                return 0;
            }

            switch (result.Action)
            {
                case ScreenAction.Exit:
                    _context.Io.Line("[green]Na shledanou.[/]");
                    return 0;
                case ScreenAction.Back:
                    if (CanNavigate())
                    {
                        _stack.Pop();
                        if (_stack.Count == 0)
                        {
                            Push(ScreenIds.DiskSelection);
                        }
                    }

                    break;
                case ScreenAction.Menu:
                    if (CanNavigate())
                    {
                        await ShowNavigationMenuAsync().ConfigureAwait(false);
                    }

                    break;
                case ScreenAction.Navigate:
                    if (CanNavigate() && !string.IsNullOrEmpty(result.Target))
                    {
                        Push(result.Target);
                    }

                    break;
                case ScreenAction.Stay:
                default:
                    break;
            }
        }

        return 0;
    }

    private bool CanNavigate()
    {
        if (!_context.NavigationLocked)
        {
            return true;
        }

        _context.Io.Line("[red]Probíhá operace s diskem – nejprve ji dokončete nebo přerušte.[/]");
        _context.Io.Pause();
        return false;
    }

    private void Push(string id)
    {
        var screen = CreateScreen(id);
        if (screen is null)
        {
            return;
        }

        _stack.Push(new ScreenEntry(id, screen));
    }

    private ITuiScreen? CreateScreen(string id)
    {
        if (!_screenTypes.TryGetValue(id, out var type))
        {
            _context.Io.Line($"[red]Obrazovka \"{Markup.Escape(id)}\" není dostupná.[/]");
            return null;
        }

        return (ITuiScreen)ActivatorUtilities.CreateInstance(_provider, type);
    }

    private async Task ShowNavigationMenuAsync()
    {
        // The localized navigation labels already carry their icon.
        var choices = Navigation
            .Select(item => new TuiChoice(item.Id, _context.L(item.Key)))
            .ToList();
        choices.Add(new TuiChoice(TuiKeys.Exit, "🚪 Ukončit aplikaci"));

        var selected = _context.Io.Choose("Navigace", choices, pageSize: 20, allowCancel: true);
        if (selected is null || selected == TuiKeys.Back)
        {
            return;
        }

        if (selected == TuiKeys.Exit)
        {
            _stack.Clear();
            return;
        }

        if (_screenTypes.ContainsKey(selected))
        {
            Push(selected);
        }

        await Task.CompletedTask.ConfigureAwait(false);
    }

    private void ShowBanner()
    {
        if (_bannerShown)
        {
            return;
        }

        _bannerShown = true;
        var io = _context.Io;
        io.Clear();
        if (io.Width >= 80)
        {
            io.Write(new FigletText("DiskChecker").Color(TuiTheme.Primary));
        }
        else
        {
            io.Line("[bold]DiskChecker[/]");
        }

        io.Line($"[grey]{Markup.Escape(_context.L("App.Subtitle"))}[/]");
        io.Line($"[grey]{Markup.Escape(_context.SystemInfo.AppVersion)} · {Markup.Escape(_context.SystemInfo.RuntimeInfo)} · " +
                $"{Markup.Escape(_context.SystemInfo.OsName)}[/]");
        io.Line();
    }

    private void RenderFrame(ScreenEntry entry)
    {
        var io = _context.Io;
        io.Clear();

        // Breadcrumb and host context; every screen renders its own heading below.
        string title = _context.L(entry.Screen.TitleKey);
        io.Line($"[grey]{Markup.Escape(_context.L("App.Name"))} ›[/] [bold]{Markup.Escape(title)}[/]   " +
                $"[grey]{Markup.Escape(_context.SystemInfo.OsName)} · " +
                $"{Markup.Escape(TuiFormats.Ellipsis(_context.SystemInfo.CpuInfo, 42))} · " +
                $"{Markup.Escape(_context.SystemInfo.RamInfo)} · {Markup.Escape(_context.SystemInfo.RuntimeInfo)}[/]");
    }

    /// <summary>Exposed for tests: available screen identifiers.</summary>
    public IReadOnlyCollection<string> ScreenIdsAvailable => _screenTypes.Keys.ToArray();

    /// <summary>The default screen catalog used by the production shell.</summary>
    public static IReadOnlyDictionary<string, Type> DefaultScreenTypes => DefaultScreenTypesCatalog;
}