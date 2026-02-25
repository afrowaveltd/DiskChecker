using DiskChecker.Core;
using DiskChecker.Core.Models;
using DiskChecker.Application.Services;
using DiskChecker.Infrastructure.Persistence;
using DiskChecker.Infrastructure.Hardware;
using DiskChecker.Core.Interfaces;
using DiskChecker.Core.Services;
using DiskChecker.Web.Hubs;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddRazorPages();
builder.Services.AddServerSideBlazor();
builder.Services.AddSignalR(); // NEW: SignalR support

builder.Services.AddCoreServices();
builder.Services.AddPersistence("Data Source=DiskChecker.db");
builder.Services.Configure<EmailSettings>(builder.Configuration.GetSection("EmailSettings"));
builder.Services.AddScoped<ISmartaProvider>(sp =>
{
    var factory = sp.GetRequiredService<SmartaProviderFactory>();
    return factory.Create();
});
builder.Services.AddSingleton<IQualityCalculator, QualityCalculator>();
builder.Services.AddScoped<DiskCheckerService>();
builder.Services.AddScoped<SmartCheckService>();
builder.Services.AddScoped<SurfaceTestPersistenceService>();
builder.Services.AddScoped<SurfaceTestExecutorFactory>();
builder.Services.AddScoped<ISurfaceTestExecutor, SurfaceTestExecutor>();
builder.Services.AddScoped<ISurfaceTestService, SurfaceTestService>();
builder.Services.AddScoped<SurfaceTestService>();
builder.Services.AddScoped<ITestReportExporter, TestReportExportService>();
builder.Services.AddScoped<PdfReportExportService>();
builder.Services.AddScoped<IPdfReportExporter, PdfReportExportService>();
builder.Services.AddScoped<IEmailSettingsService, EmailSettingsService>();
builder.Services.AddScoped<IEmailSender, SmtpEmailSender>();
builder.Services.AddScoped<IReportEmailService, ReportEmailService>();
builder.Services.AddScoped<ReportEmailService>();
builder.Services.AddScoped<HistoryService>();
builder.Services.AddSingleton<TestProgressBroadcaster>(); // NEW: Test progress broadcaster
builder.Services.AddScoped<TestCompletionNotificationService>(); // NEW: Email notifications

var app = builder.Build();

using (var scope = app.Services.CreateScope())
{
    var context = scope.ServiceProvider.GetRequiredService<DiskCheckerDbContext>();
    context.Database.EnsureCreated();
}

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error");
    app.UseHsts();
}

app.UseHttpsRedirection();
app.UseStaticFiles();
app.UseRouting();

app.MapBlazorHub();
app.MapHub<DiskTestHub>("/hubs/disk-test"); // NEW: SignalR hub endpoint
app.MapFallbackToPage("/_Host");

app.Run();
