using DiskChecker.Application.Services;
using DiskChecker.Core;
using DiskChecker.Core.Interfaces;
using DiskChecker.Core.Models;
using DiskChecker.Infrastructure.Configuration;
using DiskChecker.Infrastructure.Hardware;
using DiskChecker.Infrastructure.Hardware.Sanitization;
using DiskChecker.Infrastructure.Persistence;
using DiskChecker.Infrastructure.Services;
using DiskChecker.TUI.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace DiskChecker.TUI.App;

/// <summary>
/// Builds the same dependency graph the desktop application uses, so the terminal
/// front end runs exactly the same detection, SMART, test, certificate and
/// persistence code. Only the presentation services are terminal specific.
/// </summary>
public static class TuiHost
{
    public static ServiceCollection CreateServices(
        TuiOptions options,
        ITuiIo? io = null,
        TuiLocaleService? locale = null,
        string? settingsDirectory = null,
        Action<IServiceCollection>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(options);

        var services = new ServiceCollection();
        services.AddLogging(config =>
        {
            config.SetMinimumLevel(options.Verbose ? LogLevel.Debug : LogLevel.Warning);
        });

        services.AddCoreServices();

        // Settings and database are resolved once at startup; changing the
        // database backend requires a restart, exactly like the desktop app.
        var settingsService = new SettingsService(settingsDirectory);
        services.AddSingleton(settingsService);
        var databaseSettings = ResolveDatabaseSettings(options, settingsService);
        services.AddSingleton(databaseSettings);
        services.AddDbContext<DiskCheckerDbContext>(builder =>
        {
            DatabaseProviderConfiguration.Configure(builder, databaseSettings);
            if (databaseSettings.Provider == DatabaseProviderKind.Sqlite)
            {
                builder.EnableSensitiveDataLogging(false);
            }
        });

        // Application use cases
        services.AddScoped<HistoryService>();
        services.AddScoped<TestHistoryService>();
        services.AddScoped<SmartCheckService>();
        services.AddScoped<DiskCardTestService>();
        services.AddScoped<EmailSettingsService>();
        services.AddScoped<IEmailSettingsService>(provider => provider.GetRequiredService<EmailSettingsService>());
        services.AddScoped<IEmailSender, SmtpEmailSender>();
        services.AddScoped<TestCompletionNotificationService>();
        services.AddScoped<CertificateExportService>();
        services.AddScoped<SurfaceTestPersistenceService>();
        services.AddScoped<ISurfaceTestService, SurfaceTestService>();
        services.AddScoped<ITestAnalysisDataService, TestAnalysisDataService>();
        services.AddScoped<ReportGenerationService>();

        // Test executors
        services.AddSingleton<SurfaceTestExecutorFactory>();
        services.AddSingleton<ITuiSurfaceTestRunner, TuiSurfaceTestRunnerAdapter>();
        services.AddSingleton<ISeekTestExecutor, SeekTestExecutor>();
        services.AddSingleton<SeekTestService>();
        services.AddSingleton<TestReportAnalysisService>();

        // Containers wiring
        services.AddScoped<DiskCardRepository>();
        services.AddScoped<IDiskCardRepository>(provider => provider.GetRequiredService<DiskCardRepository>());
        services.AddScoped<CertificateGenerator>();
        services.AddScoped<ICertificateGenerator>(provider => provider.GetRequiredService<CertificateGenerator>());
        services.AddScoped<MetricsCollector>();
        services.AddScoped<IMetricsCollector>(provider => provider.GetRequiredService<MetricsCollector>());
        services.AddScoped<DiskComparisonService>();
        services.AddScoped<IDiskComparisonService>(provider => provider.GetRequiredService<DiskComparisonService>());
        services.AddTransient<IDiskTemperatureMonitor, DiskTemperatureMonitor>();

        // Settings interfaces
        services.AddSingleton<ISettingsService>(provider => provider.GetRequiredService<SettingsService>());
        services.AddSingleton<IPowerManagementService, PowerManagementService>();

        // SMART cache configuration from appsettings.json
        services.Configure<SmartaCacheOptions>(cache => cache.TtlMinutes = ReadSmartCacheTtl());
        services.PostConfigure<SmartaCacheOptions>(cache =>
        {
            if (cache.TtlMinutes <= 0)
            {
                cache.TtlMinutes = 10;
            }
        });

        RegisterPlatformServices(services);

        // Terminal presentation services
        services.AddSingleton(io ?? new TuiIo());
        services.AddSingleton(locale ?? new TuiLocaleService());
        services.AddSingleton<ILocaleProvider>(provider => provider.GetRequiredService<TuiLocaleService>());
        services.AddSingleton<ITuiSelection, TuiSelection>();
        services.AddSingleton<ITuiDiskCache, TuiDiskCache>();
        services.AddSingleton<TuiDialogs>();
        services.AddSingleton<TuiCancellation>();
        services.AddSingleton(TuiSystemInfo.Collect());
        services.AddSingleton(serviceProvider => new TuiContext(
            serviceProvider,
            serviceProvider.GetRequiredService<ITuiIo>(),
            serviceProvider.GetRequiredService<TuiLocaleService>(),
            serviceProvider.GetRequiredService<TuiDialogs>(),
            serviceProvider.GetRequiredService<ITuiSelection>(),
            serviceProvider.GetRequiredService<ITuiDiskCache>(),
            options,
            serviceProvider.GetRequiredService<TuiSystemInfo>()));

        configure?.Invoke(services);

        return services;
    }

    public static ServiceProvider BuildServices(
        TuiOptions options,
        ITuiIo? io = null,
        TuiLocaleService? locale = null,
        string? settingsDirectory = null,
        Action<IServiceCollection>? configure = null) =>
        CreateServices(options, io, locale, settingsDirectory, configure).BuildServiceProvider();

    /// <summary>Creates the database if needed and patches legacy schemas.</summary>
    public static void InitializeDatabase(IServiceProvider provider)
    {
        ArgumentNullException.ThrowIfNull(provider);
        using var scope = provider.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<DiskCheckerDbContext>();
        context.Database.EnsureCreated();
        SchemaCompatibilityPatcher.Apply(context);
    }

    private static DatabaseStorageSettings ResolveDatabaseSettings(TuiOptions options, SettingsService settingsService)
    {
        if (!string.IsNullOrWhiteSpace(options.DatabasePath))
        {
            return new DatabaseStorageSettings
            {
                Provider = DatabaseProviderKind.Sqlite,
                ConnectionString = $"Data Source={Path.GetFullPath(options.DatabasePath)}",
            };
        }

        try
        {
            return settingsService.GetDatabaseStorageSettingsAsync().GetAwaiter().GetResult();
        }
        catch (Exception ex) when (ex is InvalidOperationException or IOException or FormatException)
        {
            return DatabaseStorageSettings.Default;
        }
    }

    private static int ReadSmartCacheTtl()
    {
        try
        {
            string path = Path.Combine(AppContext.BaseDirectory, "appsettings.json");
            if (!File.Exists(path))
            {
                return 10;
            }

            using var stream = File.OpenRead(path);
            using var document = System.Text.Json.JsonDocument.Parse(stream);
            if (document.RootElement.TryGetProperty("SmartaCacheOptions", out var section)
                && section.TryGetProperty("TtlMinutes", out var ttl)
                && ttl.ValueKind == System.Text.Json.JsonValueKind.Number
                && ttl.GetInt32() > 0)
            {
                return ttl.GetInt32();
            }
        }
        catch (Exception ex) when (ex is IOException or System.Text.Json.JsonException or InvalidOperationException)
        {
            // Invalid configuration falls back to the default TTL.
        }

        return 10;
    }

    private static void RegisterPlatformServices(IServiceCollection services)
    {
        if (OperatingSystem.IsLinux())
        {
            services.AddTransient<ISmartaProvider, LinuxSmartaProvider>();
            services.AddSingleton<IDiskDetectionService>(provider =>
                new LinuxDiskDetectionService(provider.GetRequiredService<ISmartaProvider>()));
            services.AddSingleton<IDiskSanitizationService, LinuxDiskSanitizationService>();
            return;
        }

        services.AddTransient<ISmartaProvider, WindowsSmartaProvider>();
        services.AddSingleton<IDiskDetectionService>(provider =>
            new DiskDetectionService(provider.GetRequiredService<ISmartaProvider>()));
        services.AddSingleton<IDiskSanitizationService, WindowsDiskSanitizationService>();
    }
}
