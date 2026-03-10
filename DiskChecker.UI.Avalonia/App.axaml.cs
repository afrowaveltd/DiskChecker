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
using DiskChecker.Infrastructure.Persistence;
using DiskChecker.Application.Services;
using DiskChecker.Core;

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
        services.AddSingleton<DiskCheckerDbContext>();

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
        
        // Disk detection service
        services.AddSingleton<IDiskDetectionService, DiskDetectionService>();
        
        // View models
        services.AddSingleton<MainWindowViewModel>();
        services.AddTransient<DiskSelectionViewModel>();
        services.AddTransient<SurfaceTestViewModel>();
        services.AddTransient<SmartCheckViewModel>();
        services.AddTransient<AnalysisViewModel>();
        services.AddTransient<ReportViewModel>();
        services.AddTransient<HistoryViewModel>();
        services.AddTransient<SettingsViewModel>();
        
        // Platform-specific SMART provider
        services.AddTransient<ISmartaProvider, WindowsSmartaProvider>();
    }
}