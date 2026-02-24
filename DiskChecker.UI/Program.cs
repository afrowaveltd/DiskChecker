using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Configuration;
using DiskChecker.Core.Interfaces;
using DiskChecker.Core.Models;
using DiskChecker.Application.Services;
using DiskChecker.Infrastructure.Persistence;
using DiskChecker.Infrastructure.Hardware;
using DiskChecker.Core;
using DiskChecker.UI.Console;

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

var serviceProvider = services.BuildServiceProvider();

using (var scope = serviceProvider.CreateScope())
{
    var context = scope.ServiceProvider.GetRequiredService<DiskCheckerDbContext>();
    context.Database.EnsureCreated();
}

var app = serviceProvider.GetRequiredService<DiskCheckerApp>();
await app.RunAsync(args);
