using DiskChecker.Core.Interfaces;
using DiskChecker.Core.Models;
using DiskChecker.Application.Services;
using System.Text;
using System.Text.Json;

namespace DiskChecker.Application.Services;

/// <summary>
/// Centralized service for generating reports for all types of tests and managing test records.
/// </summary>
public class ReportGenerationService
{
    private readonly IDiskCardRepository _diskCardRepository;
    private readonly TestReportAnalysisService _analysisService;
    private readonly ITestReportExporter _exporter;

    public ReportGenerationService(
        IDiskCardRepository diskCardRepository,
        TestReportAnalysisService analysisService,
        ITestReportExporter exporter)
    {
        _diskCardRepository = diskCardRepository;
        _analysisService = analysisService;
        _exporter = exporter;
    }

    /// <summary>
    /// Generates a comprehensive report for any type of test and saves it to the disk card.
    /// </summary>
    /// <param name="testSession">Test session data</param>
    /// <param name="driveInfo">Drive information</param>
    /// <returns>Generated test report</returns>
    public async Task<TestReport> GenerateAndSaveReportAsync(
        TestSession testSession, 
        CoreDriveInfo driveInfo)
    {
        // Create or update disk card
        var diskCard = await GetOrCreateDiskCardAsync(driveInfo, testSession.DiskCardId);
        
        // Update disk card with test results
        await UpdateDiskCardWithTestResults(diskCard, testSession);
        
        // Generate report data
        var reportData = CreateTestReportData(testSession, driveInfo);
        
        // Create TestReport model
        var testReport = new TestReport
        {
            ReportId = Guid.NewGuid(),
            TestDate = testSession.StartedAt,
            TestType = testSession.TestType.ToString(),
            Grade = testSession.Grade,
            Score = (int)testSession.Score,
            DriveModel = driveInfo.Model ?? diskCard.ModelName,
            SerialNumber = driveInfo.SerialNumber ?? diskCard.SerialNumber,
            AverageSpeed = testSession.AverageWriteSpeedMBps,
            PeakSpeed = testSession.MaxWriteSpeedMBps,
            Errors = testSession.Errors.Count + testSession.VerificationErrors,
            IsCompleted = testSession.Status == TestStatus.Completed
        };

        return testReport;
    }

    /// <summary>
    /// Gets or creates a disk card for the given drive.
    /// </summary>
    private async Task<DiskCard> GetOrCreateDiskCardAsync(CoreDriveInfo driveInfo, int diskCardId = 0)
    {
        DiskCard? card = null;
        
        // Try to find existing card
        if (diskCardId > 0)
        {
            card = await _diskCardRepository.GetByIdAsync(diskCardId);
        }
        
        if (card == null && !string.IsNullOrEmpty(driveInfo.SerialNumber))
        {
            card = await _diskCardRepository.GetBySerialNumberAsync(driveInfo.SerialNumber);
        }
        
        if (card == null)
        {
            // Create new card
            card = new DiskCard
            {
                ModelName = driveInfo.Model ?? "Unknown",
                SerialNumber = driveInfo.SerialNumber ?? "Unknown",
                DevicePath = driveInfo.Path,
                DiskType = DetermineDiskType(driveInfo),
                InterfaceType = driveInfo.Interface ?? "Unknown",
                Capacity = driveInfo.TotalSize,
                FirmwareVersion = driveInfo.FirmwareRevision ?? "Unknown",
                ConnectionType = driveInfo.BusType.ToString(),
                CreatedAt = DateTime.UtcNow,
                LastTestedAt = DateTime.UtcNow,
                OverallGrade = "?",
                OverallScore = 0,
                TestCount = 0,
                IsArchived = false
            };
            
            card = await _diskCardRepository.CreateAsync(card);
        }
        
        return card;
    }

    /// <summary>
    /// Updates disk card with test results.
    /// </summary>
    private async Task UpdateDiskCardWithTestResults(DiskCard diskCard, TestSession testSession)
    {
        // Update test count
        diskCard.TestCount++;
        diskCard.LastTestedAt = DateTime.UtcNow;
        
        // Update overall score and grade if this test is better
        if (testSession.Score > diskCard.OverallScore)
        {
            diskCard.OverallScore = testSession.Score;
            diskCard.OverallGrade = testSession.Grade;
        }
        
        // Save the updated card
        await _diskCardRepository.UpdateAsync(diskCard);
    }

    /// <summary>
    /// Creates test report data for export.
    /// </summary>
    private TestReportData CreateTestReportData(TestSession testSession, CoreDriveInfo driveInfo)
    {
        return new TestReportData
        {
            //SMART check would be added here if available
            Language = "cs-CZ"
        };
    }

    /// <summary>
    /// Determines disk type based on drive information.
    /// </summary>
    private string DetermineDiskType(CoreDriveInfo driveInfo)
    {
        if (driveInfo.MediaType == "Fixed hard disk media")
        {
            // Try to determine if it's SSD/NVMe based on other properties
            if (driveInfo.Model?.ToLowerInvariant().Contains("nvme") == true)
                return "NVMe";
            if (driveInfo.BusType == CoreBusType.Usb)
                return "HDD (USB)";
            return "HDD";
        }
        
        return driveInfo.MediaType ?? "Unknown";
    }

    /// <summary>
    /// Generates terminal-friendly report for any type of test.
    /// </summary>
    public string GenerateTerminalReport(TestSession testSession, CoreDriveInfo driveInfo)
    {
        var sb = new StringBuilder();
        
        sb.AppendLine("╔════════════════════════════════════════════════════════════════════════════════╗");
        sb.AppendLine("║                      ZPRÁVA O TESTU DISKU                                    ║");
        sb.AppendLine("╚════════════════════════════════════════════════════════════════════════════════╝");
        sb.AppendLine();
        
        // Overall Grade
        sb.AppendLine($"📊 CELKOVÉ HODNOCENÍ: [{testSession.Grade}]  Skóre: {(int)testSession.Score}/100");
        sb.AppendLine($"   {GetHealthAssessment(testSession)}");
        sb.AppendLine();
        
        // Disk Information
        sb.AppendLine("╔─ INFORMACE O DISKU ────────────────────────────────────────────────────────────╗");
        sb.AppendLine($"║ Model:              {SafeFormat(driveInfo.Model ?? "N/A", 65)}║");
        sb.AppendLine($"║ Sériové číslo:      {SafeFormat(driveInfo.SerialNumber ?? "N/A", 65)}║");
        sb.AppendLine($"║ Rozhraní:           {SafeFormat(driveInfo.Interface ?? "N/A", 65)}║");
        sb.AppendLine($"║ Typ:                {SafeFormat(DetermineDiskType(driveInfo), 65)}║");
        sb.AppendLine($"║ Kapacita:           {SafeFormat(FormatBytes(driveInfo.TotalSize), 65)}║");
        sb.AppendLine("╚──────────────────────────────────────────────────────────────────────────────────╝");
        sb.AppendLine();
        
        // Test Information
        sb.AppendLine("╔─ INFORMACE O TESTU ────────────────────────────────────────────────────────────╗");
        sb.AppendLine($"║ Typ testu:          {SafeFormat(testSession.TestType.ToString(), 65)}║");
        sb.AppendLine($"║ Stav:               {SafeFormat(testSession.Status.ToString(), 65)}║");
        sb.AppendLine($"║ Datum spuštění:     {SafeFormat(testSession.StartedAt.ToString("yyyy-MM-dd HH:mm:ss"), 65)}║");
        if (testSession.CompletedAt.HasValue)
            sb.AppendLine($"║ Datum dokončení:    {SafeFormat(testSession.CompletedAt.Value.ToString("yyyy-MM-dd HH:mm:ss"), 65)}║");
        sb.AppendLine($"║ Doba trvání:        {SafeFormat(testSession.Duration.ToString("hh\\:mm\\:ss"), 65)}║");
        sb.AppendLine("╚──────────────────────────────────────────────────────────────────────────────────╝");
        sb.AppendLine();
        
        // Performance Metrics (if applicable)
        if (testSession.BytesWritten > 0 || testSession.BytesRead > 0)
        {
            sb.AppendLine("╔─ METRIKY VÝKONU ──────────────────────────────────────────────────────────────╗");
            if (testSession.BytesWritten > 0)
            {
                sb.AppendLine($"║ Zapsáno:            {SafeFormat(FormatBytes(testSession.BytesWritten), 65)}║");
                sb.AppendLine($"║ Průměr zápisu:      {SafeFormat($"{testSession.AverageWriteSpeedMBps:F2} MB/s", 65)}║");
                sb.AppendLine($"║ Max zápisu:         {SafeFormat($"{testSession.MaxWriteSpeedMBps:F2} MB/s", 65)}║");
            }
            if (testSession.BytesRead > 0)
            {
                sb.AppendLine($"║ Přečteno:           {SafeFormat(FormatBytes(testSession.BytesRead), 65)}║");
                sb.AppendLine($"║ Průměr čtení:       {SafeFormat($"{testSession.AverageReadSpeedMBps:F2} MB/s", 65)}║");
                sb.AppendLine($"║ Max čtení:          {SafeFormat($"{testSession.MaxReadSpeedMBps:F2} MB/s", 65)}║");
            }
            sb.AppendLine("╚──────────────────────────────────────────────────────────────────────────────────╝");
            sb.AppendLine();
        }
        
        // Errors
        if (testSession.WriteErrors > 0 || testSession.ReadErrors > 0 || testSession.VerificationErrors > 0)
        {
            sb.AppendLine("╔─ CHYBY ────────────────────────────────────────────────────────────────────────╗");
            if (testSession.WriteErrors > 0)
                sb.AppendLine($"║ Chyby zápisu:       {SafeFormat(testSession.WriteErrors.ToString(), 65)}║");
            if (testSession.ReadErrors > 0)
                sb.AppendLine($"║ Chyby čtení:        {SafeFormat(testSession.ReadErrors.ToString(), 65)}║");
            if (testSession.VerificationErrors > 0)
                sb.AppendLine($"║ Chyby ověření:      {SafeFormat(testSession.VerificationErrors.ToString(), 65)}║");
            sb.AppendLine("╚──────────────────────────────────────────────────────────────────────────────────╝");
            sb.AppendLine();
        }
        
        // Temperature
        if (testSession.MaxTemperature.HasValue)
        {
            sb.AppendLine("╔─ TEPLOTA ──────────────────────────────────────────────────────────────────────╗");
            sb.AppendLine($"║ Maximální:          {SafeFormat($"{testSession.MaxTemperature}°C", 65)}║");
            if (testSession.AverageTemperature.HasValue)
                sb.AppendLine($"║ Průměrná:           {SafeFormat($"{testSession.AverageTemperature:F1}°C", 65)}║");
            sb.AppendLine("╚──────────────────────────────────────────────────────────────────────────────────╝");
            sb.AppendLine();
        }
        
        // Recommendations
        sb.AppendLine("╔─ DOPORUČENÍ ───────────────────────────────────────────────────────────────────╗");
        sb.AppendLine($"║ {SafeFormat(GetRecommendation(testSession), 76)}║");
        sb.AppendLine("╚──────────────────────────────────────────────────────────────────────────────────╝");
        sb.AppendLine();
        
        // Footer
        sb.AppendLine($"Vygenerováno: {DateTime.Now:yyyy-MM-dd HH:mm:ss} (ID testu: {testSession.SessionId})");
        
        return sb.ToString();
    }

    /// <summary>
    /// Gets health assessment based on test results.
    /// </summary>
    private string GetHealthAssessment(TestSession testSession) => testSession.Score switch
    {
        >= 95 => "✅ VYNIKAJÍCÍ: Disk funguje dokonale bez chyb.",
        >= 90 => "✅ VELMI DOBRÝ: Disk funguje dobře s minimálními problémy.",
        >= 80 => "✅ DOBRÝ: Disk je zdravý s malými variacemi výkonu.",
        >= 70 => "⚠️ PŘIJATELNÝ: Zjištěny některé problémy, monitorujte.",
        >= 60 => "❌ ŠPATNÝ: Zjištěny vážné problémy. Nedoporučuje se.",
        _ => "❌ KRITICKÝ: Disk má kritické problémy. Okamžitá výměna."
    };

    /// <summary>
    /// Gets recommendation based on test results.
    /// </summary>
    private string GetRecommendation(TestSession testSession)
    {
        if (testSession.Result == TestResult.Fail)
            return "❌ NEPROŠEL TESTEM - NEPOUŽÍVEJTE PRO KRITICKÁ DATA!";
        
        if (testSession.Errors.Count > 10 || testSession.VerificationErrors > 5)
            return "⚠️ Vysoce chybové - pouze pro nekritické použití.";
        
        if (testSession.Score >= 90)
            return "✅ Výborný stav - doporučeno pro všechny účely.";
        
        if (testSession.Score >= 70)
            return "✅ Dobrý stav - vhodný pro běžné použití.";
        
        if (testSession.Score >= 50)
            return "⚠️ Slabší výkon - vhodný pro méně náročné úlohy.";
        
        return "❌ Špatný stav - vyžaduje pozornost.";
    }

    /// <summary>
    /// Formats bytes to human-readable format.
    /// </summary>
    private static string FormatBytes(long bytes)
    {
        string[] sizes = { "B", "KB", "MB", "GB", "TB" };
        double len = bytes;
        int order = 0;
        while (len >= 1024 && order < sizes.Length - 1)
        {
            order++;
            len = len / 1024;
        }
        return $"{len:0.##} {sizes[order]}";
    }

    /// <summary>
    /// Helper to safely format strings for fixed-width report columns.
    /// </summary>
    private static string SafeFormat(string? text, int width)
    {
        if (string.IsNullOrEmpty(text))
            text = "N/A";
        
        if (text.Length > width)
            return text[..(width - 3)] + "...";
        
        return text.PadRight(width);
    }
}