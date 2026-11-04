using DiskChecker.Core.Models;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Spectre.Console;

namespace DiskChecker.TUI.Services;

/// <summary>
/// Everything a screen needs: the console, the localized vocabulary, shared state
/// and access to the dependency injection container.
/// </summary>
public sealed class TuiContext
{
    private int _navigationLocks;

    public TuiContext(
        IServiceProvider services,
        ITuiIo io,
        TuiLocaleService locale,
        TuiDialogs dialogs,
        ITuiSelection selection,
        ITuiDiskCache diskCache,
        TuiOptions options,
        TuiSystemInfo systemInfo)
    {
        Services = services ?? throw new ArgumentNullException(nameof(services));
        Io = io ?? throw new ArgumentNullException(nameof(io));
        Locale = locale ?? throw new ArgumentNullException(nameof(locale));
        Dialogs = dialogs ?? throw new ArgumentNullException(nameof(dialogs));
        Selection = selection ?? throw new ArgumentNullException(nameof(selection));
        DiskCache = diskCache ?? throw new ArgumentNullException(nameof(diskCache));
        Options = options ?? throw new ArgumentNullException(nameof(options));
        SystemInfo = systemInfo ?? throw new ArgumentNullException(nameof(systemInfo));
    }

    public IServiceProvider Services { get; }

    public ITuiIo Io { get; }

    public TuiLocaleService Locale { get; }

    public TuiDialogs Dialogs { get; }

    public ITuiSelection Selection { get; }

    public ITuiDiskCache DiskCache { get; }

    public TuiOptions Options { get; }

    public TuiSystemInfo SystemInfo { get; }

    /// <summary>Status line shown by every screen.</summary>
    public string StatusMessage { get; set; } = string.Empty;

    public bool NavigationLocked => Volatile.Read(ref _navigationLocks) > 0;

    public T GetRequiredService<T>() where T : notnull => Services.GetRequiredService<T>();

    public T? GetService<T>() where T : class => Services.GetService<T>();

    public ILogger<T> Logger<T>() => Services.GetRequiredService<ILogger<T>>();

    /// <summary>
    /// Prevents leaving the current screen while a disk owning operation runs.
    /// The desktop application blocks navigation the same way.
    /// </summary>
    public IDisposable LockNavigation(string reason)
    {
        Interlocked.Increment(ref _navigationLocks);
        StatusMessage = reason;
        return new NavigationLock(this);
    }

    public IServiceScope CreateScope() => Services.CreateScope();

    public async Task<T> UseScopedAsync<T>(Func<IServiceProvider, Task<T>> body)
    {
        ArgumentNullException.ThrowIfNull(body);
        using var scope = Services.CreateScope();
        return await body(scope.ServiceProvider).ConfigureAwait(false);
    }

    public async Task UseScopedAsync(Func<IServiceProvider, Task> body)
    {
        ArgumentNullException.ThrowIfNull(body);
        using var scope = Services.CreateScope();
        await body(scope.ServiceProvider).ConfigureAwait(false);
    }

    public string L(string key) => Locale.GetString(key);

    public string L(string key, params object[] args) => Locale.Get(key, args);

    public void RenderStatusBar()
    {
        string status = string.IsNullOrWhiteSpace(StatusMessage)
            ? "[grey]" + Markup.Escape(L("Status.Indicator") + L("Status.Ready")) + "[/]"
            : "[green]●[/] " + Markup.Escape(StatusMessage);
        Io.Line($"[grey]{Markup.Escape(SystemInfo.AppVersion)}[/]  {status}");
    }

    private sealed class NavigationLock : IDisposable
    {
        private readonly TuiContext _context;
        private int _disposed;

        public NavigationLock(TuiContext context) => _context = context;

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) == 0)
            {
                Interlocked.Decrement(ref _context._navigationLocks);
            }
        }
    }
}
