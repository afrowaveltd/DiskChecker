using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Configuration;
using DiskChecker.Core.Interfaces;
using DiskChecker.Core.Models;
using DiskChecker.Core.Services;
using DiskChecker.Infrastructure.Persistence;
using DiskChecker.Infrastructure.Hardware;
using DiskChecker.Application.Services;
using DiskChecker.Core;
using DiskChecker.UI.Console;

var configuration = new ConfigurationBuilder()
    .AddJsonFile("appsettings.json", optional: true, reloadOnChange: true)
    .Build();

var services = new ServiceCollection();

services.AddSingleton<IConfiguration>(configuration);
services.Configure<EmailSettings>(configuration.GetSection("EmailSettings"));
services.AddCoreServices();
services.AddLogging(logging => 
{
    logging.ClearProviders();
    // logging.AddConsole(); // Disabled to keep console UI clean
});
services.AddPersistence("Data Source=DiskChecker.db");
services.AddScoped<DiskCheckerService>();
services.AddScoped<SmartCheckService>();
services.AddScoped<SurfaceTestPersistenceService>();
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
services.AddScoped<DiskCheckerApp>();

var serviceProvider = services.BuildServiceProvider();

using (var scope = serviceProvider.CreateScope())
{
    var context = scope.ServiceProvider.GetRequiredService<DiskCheckerDbContext>();
    context.Database.EnsureCreated();
}

var app = serviceProvider.GetRequiredService<DiskCheckerApp>();
await app.RunAsync(args);
