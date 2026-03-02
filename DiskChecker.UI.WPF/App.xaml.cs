using DiskChecker.Application.Services;
using DiskChecker.Core;
using DiskChecker.Core.Interfaces;
using DiskChecker.Infrastructure.Hardware;
using DiskChecker.Infrastructure.Persistence;
using DiskChecker.UI.WPF.Services;
using DiskChecker.UI.WPF.ViewModels;
using DiskChecker.UI.WPF.Views;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using System.Windows;

namespace DiskChecker.UI.WPF;

/// <summary>
/// Interaction logic for App.xaml
/// </summary>
public partial class App : System.Windows.Application
{
   private const string DefaultConnectionString = "Data Source=DiskChecker.db";
   private readonly ServiceProvider _serviceProvider;

   /// <summary>
   /// Initializes a new instance of the <see cref="App"/> class.
   /// </summary>
   public App()
   {
      ServiceCollection services = new ServiceCollection();

      // Logging
      services.AddLogging(config =>
      {
         config.AddConsole();
         config.SetMinimumLevel(LogLevel.Information);
      });

      services.AddCoreServices();
      services.AddPersistence(DefaultConnectionString);

      // Core Services
      services.AddScoped<DiskCheckerService>();
      services.AddScoped<SmartCheckService>();
      services.AddScoped<HistoryService>();
      services.AddScoped<DatabaseMaintenanceService>();
      services.AddScoped<DiskHistoryArchiveService>();
      services.AddScoped<TestReportAnalysisService>();
      services.AddScoped<ISurfaceTestService, SurfaceTestService>();
      services.AddScoped<SurfaceTestService>();
      services.AddScoped<SurfaceTestExecutorFactory>();
      services.AddScoped<SurfaceTestPersistenceService>();

      // WPF Services
      services.AddSingleton<INavigationService, NavigationService>();

      // ViewModels
      services.AddTransient<MainWindowViewModel>();
      services.AddTransient<DiskSelectionViewModel>();
      services.AddTransient<SmartCheckViewModel>();
      services.AddTransient<SurfaceTestViewModel>();
      services.AddTransient<AnalysisViewModel>();
      services.AddTransient<ReportViewModel>();
      services.AddTransient<HistoryViewModel>();
      services.AddTransient<SettingsViewModel>();

      _serviceProvider = services.BuildServiceProvider();

      // Registruj View-ViewModel mappingy
      RegisterViewMappings();
   }

   /// <summary>
   /// Registruje mapping mezi ViewModely a Views.
   /// </summary>
   private void RegisterViewMappings()
   {
      var navigationService = _serviceProvider.GetRequiredService<INavigationService>();

      navigationService.RegisterViewForViewModel<DiskSelectionViewModel, DiskSelectionView>();
      navigationService.RegisterViewForViewModel<SurfaceTestViewModel, SurfaceTestView>();
      navigationService.RegisterViewForViewModel<SmartCheckViewModel, SmartCheckView>();
      navigationService.RegisterViewForViewModel<AnalysisViewModel, AnalysisView>();
      navigationService.RegisterViewForViewModel<ReportViewModel, ReportView>();
      navigationService.RegisterViewForViewModel<HistoryViewModel, HistoryView>();
      navigationService.RegisterViewForViewModel<SettingsViewModel, SettingsView>();
   }

   /// <summary>
   /// Handles application startup.
   /// </summary>
   private void Application_Startup(object sender, StartupEventArgs e)
   {
      using(var scope = _serviceProvider.CreateScope())
      {
         var dbContext = scope.ServiceProvider.GetRequiredService<DiskCheckerDbContext>();
         dbContext.Database.EnsureCreated();
         SchemaCompatibilityPatcher.Apply(dbContext);
      }

      // Vytvořit a zobrazit MainWindow
      var mainVM = _serviceProvider.GetRequiredService<MainWindowViewModel>();
      MainWindow mainWindow = new MainWindow
      {
         DataContext = mainVM
      };
      mainWindow.Show();
   }

   /// <summary>
   /// Handles application exit.
   /// </summary>
   private void Application_Exit(object sender, ExitEventArgs e)
   {
      _serviceProvider?.Dispose();
   }
}

