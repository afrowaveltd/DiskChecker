using DiskChecker.Core;
using DiskChecker.Application.Services;
using DiskChecker.Infrastructure.Persistence;
using DiskChecker.Core.Interfaces;
using DiskChecker.Core.Services;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddRazorPages();
builder.Services.AddServerSideBlazor();

builder.Services.AddCoreServices();
builder.Services.AddPersistence("Data Source=DiskChecker.db");
builder.Services.AddSingleton<ISmartaProvider, TestDiskProvider>();
builder.Services.AddSingleton<IQualityCalculator, QualityCalculator>();
builder.Services.AddSingleton<DiskCheckerService>();

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
