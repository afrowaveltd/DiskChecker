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
services.AddLogging(logging => logging.AddConsole());
services.AddPersistence("Data Source=DiskChecker.db");
services.AddSingleton<ISmartaProvider>(_ => SmartaProviderFactory.CreateProvider());
services.AddSingleton<IQualityCalculator, QualityCalculator>();
services.AddScoped<DiskCheckerService>();
services.AddScoped<SmartCheckService>();
services.AddScoped<SurfaceTestPersistenceService>();
services.AddScoped<ISurfaceTestExecutor, SurfaceTestExecutor>();
services.AddScoped<ISurfaceTestService, SurfaceTestService>();
services.AddScoped<SurfaceTestService>();
services.AddScoped<ITestReportExporter, TestReportExportService>();
services.AddScoped<TestReportExportService>();
services.AddScoped<IEmailSettingsService, EmailSettingsService>();
services.AddScoped<IEmailSender, SmtpEmailSender>();
services.AddScoped<IReportEmailService, ReportEmailService>();
services.AddScoped<ReportEmailService>();
services.AddScoped<MainConsoleMenu>();
services.AddScoped<DiskCheckerApp>();

var serviceProvider = services.BuildServiceProvider();

var app = serviceProvider.GetRequiredService<DiskCheckerApp>();
await app.RunAsync(args);
