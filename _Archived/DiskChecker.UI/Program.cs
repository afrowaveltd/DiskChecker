using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Configuration;
using DiskChecker.Core.Interfaces;
using DiskChecker.Core.Models;
using DiskChecker.Application.Services;
using DiskChecker.Infrastructure.Persistence;
using DiskChecker.Infrastructure.Hardware;
using System.Runtime.InteropServices;
using DiskChecker.Core;
using DiskChecker.UI.Console;
using DiskChecker.UI.Console.Pages;

var configuration = new ConfigurationBuilder()
    .AddJsonFile("appsettings.json", optional: true, reloadOnChange: true)
    .Build();

var services = new ServiceCollection();

services.AddSingleton<IConfiguration>(configuration);
services.Configure<EmailSettings>(configuration.GetSection("EmailSettings"));

// Configure logging FIRST before other services
services.AddLogging(logging => 
{
    logging.ClearProviders();
    logging.AddConsole();
    logging.SetMinimumLevel(LogLevel.Warning); // Changed to Warning to suppress Info logs
    logging.AddFilter("Microsoft.EntityFrameworkCore", LogLevel.Error); // Only show EF Core errors
});

services.AddCoreServices();
services.AddPersistence("Data Source=DiskChecker.db");
services.AddScoped<DiskCheckerService>();
services.AddScoped<SmartCheckService>();
services.AddScoped<SurfaceTestPersistenceService>();
services.AddScoped<SurfaceTestExecutorFactory>();
services.AddScoped<ISurfaceTestExecutor, SurfaceTestExecutor>();
services.AddScoped<ISurfaceTestService, SurfaceTestService>();
services.AddScoped<SurfaceTestService>();
services.AddScoped<ITestReportExporter, TestReportExportService>();
services.AddScoped<PdfReportExportService>();
services.AddScoped<IPdfReportExporter, PdfReportExportService>();
services.AddScoped<EmailSettingsService>();
services.AddScoped<IEmailSettingsService, EmailSettingsService>();
services.AddScoped<IEmailSender, SmtpEmailSender>();
services.AddScoped<ReportEmailService>();
services.AddScoped<IReportEmailService, ReportEmailService>();
services.AddScoped<HistoryService>();
services.AddScoped<MainConsoleMenu>();
services.AddScoped<DiagnosticsApp>();
services.AddScoped<DiskCheckerApp>();

// Register platform-specific SMART provider to avoid missing service resolution
if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
{
    services.AddScoped<ISmartaProvider, WindowsSmartaProvider>();
}
else if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux) || RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
{
    services.AddScoped<ISmartaProvider, LinuxSmartaProvider>();
}
else
{
    // Fallback for unknown platforms or during development
    services.AddScoped<ISmartaProvider, TestDiskProvider>();
}

var serviceProvider = services.BuildServiceProvider();

using (var scope = serviceProvider.CreateScope())
{
    var context = scope.ServiceProvider.GetRequiredService<DiskCheckerDbContext>();
    context.Database.EnsureCreated();
    // Ensure older SQLite schemas are patched for compatibility (adds missing columns)
    SchemaCompatibilityPatcher.Apply(context);
}

using (var runScope = serviceProvider.CreateScope())
{
    var app = runScope.ServiceProvider.GetRequiredService<DiskCheckerApp>();
    await app.RunAsync(args);
}
