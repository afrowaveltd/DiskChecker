using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Data.Core;
using Avalonia.Data.Core.Plugins;
using System.Linq;
using Avalonia.Markup.Xaml;
using DiskChecker.UI.Avalonia.ViewModels;
using DiskChecker.UI.Avalonia.Views;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using DiskChecker.UI.Avalonia.Services;
using DiskChecker.UI.Avalonia.Services.Interfaces;
using DiskChecker.Core.Interfaces;
using DiskChecker.Infrastructure.Hardware;
using DiskChecker.Infrastructure.Configuration;
using DiskChecker.Infrastructure.Hardware.Sanitization;
using DiskChecker.Infrastructure.Persistence;
using DiskChecker.Infrastructure.Services;
using DiskChecker.Application.Services;
using DiskChecker.Core;
using System.Runtime.InteropServices;
using Microsoft.EntityFrameworkCore;

namespace DiskChecker.UI.Avalonia;

public partial class App : global::Avalonia.Application
{
    private ServiceProvider? _serviceProvider;

    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
    }

    public static T? GetService<T>()
        where T : class
    {
        if (Current is App app && app._serviceProvider is IServiceProvider serviceProvider)
        {
            return serviceProvider.GetService<T>();
        }
        return null;
    }

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            // Avoid duplicate validations
            DisableAvaloniaDataAnnotationValidation();
            
            // Setup dependency injection
            var services = new ServiceCollection();
            ConfigureServices(services);
            _serviceProvider = services.BuildServiceProvider();
            
            // Initialize database
            InitializeDatabase();
            
            // Create main window with DI
            var mainWindow = new MainWindow();
            mainWindow.DataContext = _serviceProvider.GetRequiredService<MainWindowViewModel>();
            
            desktop.MainWindow = mainWindow;
        }

        base.OnFrameworkInitializationCompleted();
    }

    private void DisableAvaloniaDataAnnotationValidation()
    {
        var dataValidationPluginsToRemove =
            BindingPlugins.DataValidators.OfType<DataAnnotationsValidationPlugin>().ToArray();

        foreach (var plugin in dataValidationPluginsToRemove)
        {
            BindingPlugins.DataValidators.Remove(plugin);
        }
    }
    
    private void InitializeDatabase()
    {
        using var scope = _serviceProvider!.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<DiskCheckerDbContext>();
        dbContext.Database.EnsureCreated();
    }
    
    private void ConfigureServices(IServiceCollection services)
    {
        // Logging
        services.AddLogging(config =>
        {
            config.SetMinimumLevel(LogLevel.Information);
        });

        // Core services
        services.AddCoreServices();

        // Database context
        services.AddDbContext<DiskCheckerDbContext>(options =>
            options.UseSqlite("Data Source=DiskChecker.db"));

        // Application services
        // HistoryService depends on DbContext (scoped). Register the concrete service as scoped
        // and expose it via the UI-facing IHistoryService interface using a factory to avoid
        // requiring the application service to implement the UI interface directly.
        services.AddScoped<DiskChecker.Application.Services.HistoryService>();
        services.AddScoped<IHistoryService, HistoryServiceAdapter>();
        services.AddSingleton<SettingsService>();
        services.AddSingleton<SmartCheckService>();
        services.AddScoped<DiskCardTestService>();
        // Infrastructure and application surface test components
        services.AddSingleton<DiskChecker.Infrastructure.Hardware.SurfaceTestExecutorFactory>();
        services.AddScoped<SurfaceTestPersistenceService>();
        services.AddScoped<ISurfaceTestService, SurfaceTestService>();
        // Register analysis/reporting components
        services.AddSingleton<TestReportAnalysisService>();
        // Register UI analysis service implementation so AnalysisViewModel can resolve IAnalysisService.
        services.AddSingleton<IAnalysisService, AnalysisService>();
        
        // Navigation service
        services.AddSingleton<INavigationService, NavigationService>();
        
        // Dialog service
        services.AddSingleton<IDialogService, DialogService>();
        
        // Backup service
        services.AddSingleton<IBackupService, BackupService>();
        
        // Settings service interface
        services.AddSingleton<ISettingsService, SettingsService>();
        
        // Selected disk service for sharing between views
        services.AddSingleton<ISelectedDiskService, SelectedDiskService>();
        
        // Platform-specific
        // Load configuration (optional appsettings.json) and bind SMART cache options
        // Load simple appsettings.json (optional) to configure SMART cache TTL without
        // pulling in the full Microsoft.Configuration extensions at runtime.
        var ttl = 10;
        try
        {
            var settingsPath = System.IO.Path.Combine(AppContext.BaseDirectory, "appsettings.json");
            if (System.IO.File.Exists(settingsPath))
            {
                using var fs = System.IO.File.OpenRead(settingsPath);
                using var doc = System.Text.Json.JsonDocument.Parse(fs);
                if (doc.RootElement.TryGetProperty("SmartaCacheOptions", out var section) &&
                    section.TryGetProperty("TtlMinutes", out var ttlProp) && ttlProp.ValueKind == System.Text.Json.JsonValueKind.Number)
                {
                    var v = ttlProp.GetInt32();
                    if (v > 0) ttl = v;
                }
            }
        }
        catch { /* ignore config parse errors, use default */ }

        services.Configure<SmartaCacheOptions>(opt => opt.TtlMinutes = ttl);
        // Fallback default if not configured
        services.PostConfigure<SmartaCacheOptions>(opt => { if (opt.TtlMinutes <= 0) opt.TtlMinutes = 10; });
        
        // Platform-specific SMART provider and disk detection
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
        {
            services.AddSingleton<IDiskDetectionService, LinuxDiskDetectionService>();
            services.AddTransient<ISmartaProvider, LinuxSmartaProvider>();
        }
        else
        {
            services.AddSingleton<IDiskDetectionService, DiskDetectionService>();
            services.AddTransient<ISmartaProvider, WindowsSmartaProvider>();
        }
        
        // Disk sanitization service
        services.AddSingleton<DiskSanitizationService>();
        
        // Infrastructure services
        services.AddScoped<DiskCardRepository>();
        services.AddScoped<CertificateGenerator>();
        services.AddScoped<MetricsCollector>();
        services.AddScoped<DiskComparisonService>();
        
        // Interfaces
        services.AddScoped<IDiskCardRepository, DiskCardRepository>();
        services.AddScoped<IDiskComparisonService, DiskComparisonService>();
        services.AddScoped<ICertificateGenerator, CertificateGenerator>();
        services.AddScoped<IMetricsCollector>(provider => provider.GetRequiredService<MetricsCollector>());
        
        // View models - existing
        services.AddSingleton<MainWindowViewModel>();
        services.AddTransient<DiskSelectionViewModel>();
        services.AddTransient<SurfaceTestViewModel>();
        services.AddTransient<SmartCheckViewModel>();
        services.AddTransient<AnalysisViewModel>();
        services.AddTransient<ReportViewModel>();
        services.AddTransient<HistoryViewModel>();
        services.AddTransient<SettingsViewModel>();
        
        // View models - new
        services.AddTransient<DiskCardsViewModel>();
        services.AddTransient<DiskCardDetailViewModel>();
        services.AddTransient<CertificateViewModel>();
        services.AddTransient<DiskComparisonViewModel>();
    }
}