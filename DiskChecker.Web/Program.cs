using DiskChecker.Core;
using DiskChecker.Core.Models;
using DiskChecker.Application.Services;
using DiskChecker.Infrastructure.Persistence;
using DiskChecker.Infrastructure.Hardware;
using DiskChecker.Core.Interfaces;
using DiskChecker.Core.Services;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddRazorPages();
builder.Services.AddServerSideBlazor();

builder.Services.AddCoreServices();
builder.Services.AddPersistence("Data Source=DiskChecker.db");
builder.Services.Configure<EmailSettings>(builder.Configuration.GetSection("EmailSettings"));
builder.Services.AddSingleton<ISmartaProvider>(_ => SmartaProviderFactory.CreateProvider());
builder.Services.AddSingleton<IQualityCalculator, QualityCalculator>();
builder.Services.AddScoped<DiskCheckerService>();
builder.Services.AddScoped<SmartCheckService>();
builder.Services.AddScoped<SurfaceTestPersistenceService>();
builder.Services.AddScoped<ISurfaceTestExecutor, SurfaceTestExecutor>();
builder.Services.AddScoped<ISurfaceTestService, SurfaceTestService>();
builder.Services.AddScoped<SurfaceTestService>();
builder.Services.AddScoped<ITestReportExporter, TestReportExportService>();
builder.Services.AddScoped<TestReportExportService>();
builder.Services.AddScoped<IPdfReportExporter, PdfReportExportService>();
builder.Services.AddScoped<PdfReportExportService>();
builder.Services.AddScoped<IEmailSettingsService, EmailSettingsService>();
builder.Services.AddScoped<IEmailSender, SmtpEmailSender>();
builder.Services.AddScoped<IReportEmailService, ReportEmailService>();
builder.Services.AddScoped<ReportEmailService>();

var app = builder.Build();

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error");
    app.UseHsts();
}

app.UseHttpsRedirection();
app.UseStaticFiles();
app.UseRouting();

app.MapBlazorHub();
app.MapFallbackToPage("/_Host");

app.Run();
