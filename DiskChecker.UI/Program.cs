using Microsoft.Extensions.DependencyInjection;
using DiskChecker.Core.Interfaces;
using DiskChecker.Core.Models;
using DiskChecker.Core.Services;
using DiskChecker.Infrastructure.Persistence;
using DiskChecker.Application.Services;
using DiskChecker.Core;

var services = new ServiceCollection();

// Add core services
services.AddCoreServices();

// Add persistence
services.AddPersistence("Data Source=DiskChecker.db");

// Register factories and services - use test provider for testing
services.AddSingleton<ISmartaProvider, TestDiskProvider>();
services.AddSingleton<IQualityCalculator, QualityCalculator>();
services.AddSingleton<DiskCheckerService>();

// Build provider
var serviceProvider = services.BuildServiceProvider();

// Get services
var diskCheckerService = serviceProvider.GetRequiredService<DiskCheckerService>();

// Example: List drives
var drives = await diskCheckerService.ListDrivesAsync();
Console.WriteLine($"Nalezeno disků: {drives.Count}");
foreach (var drive in drives)
{
    Console.WriteLine($"- {drive.Name} ({drive.Path})");
}
