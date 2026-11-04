using DiskChecker.TUI.App;
using DiskChecker.TUI.Services;
using Microsoft.Extensions.DependencyInjection;

namespace DiskChecker.Tui.Tests.Support;

/// <summary>
/// Builds a complete terminal application composition (same DI graph as production)
/// against a throwaway database and settings directory.
/// </summary>
public sealed class TestHarness : IDisposable
{
    private readonly string _root;
    private bool _disposed;

    private TestHarness(string root, ServiceProvider services, ScriptedTuiIo io, TuiLocaleService locale)
    {
        _root = root;
        Services = services;
        Io = io;
        Locale = locale;
        Context = services.GetRequiredService<TuiContext>();
        Cancellation = services.GetRequiredService<TuiCancellation>();
    }

    public ServiceProvider Services { get; }

    public ScriptedTuiIo Io { get; }

    public TuiLocaleService Locale { get; }

    public TuiContext Context { get; }

    public TuiCancellation Cancellation { get; }

    public string Root => _root;

    public static TestHarness Create(
        Action<IServiceCollection>? configure = null,
        string language = "cs",
        int terminalWidth = 120,
        bool initializeDatabase = true)
    {
        string root = Path.Combine(Path.GetTempPath(), "dcheck-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);

        var io = new ScriptedTuiIo { Width = terminalWidth };
        var locale = new TuiLocaleService(TestPaths.LocalesDirectory);
        var options = TuiOptions.Parse(new[] { "--db", Path.Combine(root, "DiskChecker.db") });
        var services = TuiHost.CreateServices(options, io, locale, Path.Combine(root, "settings"), configure);
        var provider = services.BuildServiceProvider();
        if (initializeDatabase)
        {
            TuiHost.InitializeDatabase(provider);
        }

        locale.SetLocale(language);
        return new TestHarness(root, provider, io, locale);
    }

    public T GetRequiredService<T>() where T : notnull => Services.GetRequiredService<T>();

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        Services.Dispose();
        try
        {
            if (Directory.Exists(_root))
            {
                Directory.Delete(_root, recursive: true);
            }
        }
        catch (IOException)
        {
            // Temporary directories are best effort cleanup.
        }
    }
}

/// <summary>Locates repository paths needed by tests.</summary>
public static class TestPaths
{
    public static string RepositoryRoot { get; } = FindRepositoryRoot();

    public static string LocalesDirectory => Path.Combine(RepositoryRoot, "DiskChecker.UI.Avalonia", "Locales");

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "DiskChecker.slnx")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new InvalidOperationException("Repository root (DiskChecker.slnx) not found from " + AppContext.BaseDirectory);
    }
}
