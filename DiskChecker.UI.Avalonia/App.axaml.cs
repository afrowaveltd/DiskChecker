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
using DiskChecker.Infrastructure.Hardware.Sanitization;
using DiskChecker.Infrastructure.Persistence;
using DiskChecker.Infrastructure.Services;
using DiskChecker.Application.Services;
using DiskChecker.Core;
using System.Runtime.Versioning;
using Microsoft.EntityFrameworkCore;

namespace DiskChecker.UI.Avalonia;

[SupportedOSPlatform("windows")]
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
        services.AddSingleton<HistoryService>();
        services.AddSingleton<SettingsService>();
        services.AddSingleton<SmartCheckService>();
        services.AddSingleton<SurfaceTestService>();
        services.AddSingleton<DiskDetectionService>();
        
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
        
        // Disk detection service
        services.AddSingleton<IDiskDetectionService, DiskDetectionService>();
        
        // Platform-specific SMART provider
        services.AddTransient<ISmartaProvider, WindowsSmartaProvider>();
        
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