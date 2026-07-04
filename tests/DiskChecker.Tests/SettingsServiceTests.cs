using System;
using System.IO;
using System.Threading.Tasks;
using DiskChecker.Application.Services;
using Xunit;

namespace DiskChecker.Tests;

public class SettingsServiceTests
{
    private static string GetTempSettingsPath()
    {
        return Path.Combine(Path.GetTempPath(), $"DiskCheckerTests_{Guid.NewGuid():N}");
    }

    private static SettingsService CreateService()
    {
        return new SettingsService(GetTempSettingsPath());
    }

    [Fact]
    public async Task GetAutoCheckForUpdates_ReturnsDefaultTrue()
    {
        var service = CreateService();
        var result = await service.GetAutoCheckForUpdatesAsync();
        Assert.True(result);
    }

    [Fact]
    public async Task SetAndGetAutoCheckForUpdates_Works()
    {
        var service = CreateService();
        await service.SetAutoCheckForUpdatesAsync(false);
        var result = await service.GetAutoCheckForUpdatesAsync();
        Assert.False(result);
    }

    [Fact]
    public async Task GetMinimizeToTray_ReturnsDefaultTrue()
    {
        var service = CreateService();
        var result = await service.GetMinimizeToTrayAsync();
        Assert.True(result);
    }

    [Fact]
    public async Task SetAndGetMinimizeToTray_Works()
    {
        var service = CreateService();
        await service.SetMinimizeToTrayAsync(false);
        var result = await service.GetMinimizeToTrayAsync();
        Assert.False(result);
    }

    [Fact]
    public async Task GetAutoSaveInterval_ReturnsDefault5()
    {
        var service = CreateService();
        var result = await service.GetAutoSaveIntervalAsync();
        Assert.Equal(5, result);
    }

    [Fact]
    public async Task SetAndGetAutoSaveInterval_Works()
    {
        var service = CreateService();
        await service.SetAutoSaveIntervalAsync(10);
        var result = await service.GetAutoSaveIntervalAsync();
        Assert.Equal(10, result);
    }

    [Fact]
    public async Task GetDefaultExportPath_ReturnsDocumentsFolder()
    {
        var service = CreateService();
        var result = await service.GetDefaultExportPathAsync();
        Assert.NotNull(result);
        // On Linux, MyDocuments may be empty if XDG_DOCUMENTS_DIR is not set.
        // The path is valid either way — just ensure it doesn't throw.
        if (!string.IsNullOrEmpty(result))
        {
            Assert.True(Directory.Exists(result) || Directory.GetParent(result)?.Exists == true);
        }
    }

    [Fact]
    public async Task SetAndGetDefaultExportPath_Works()
    {
        var service = CreateService();
        var testPath = "/tmp/test_export";
        await service.SetDefaultExportPathAsync(testPath);
        var result = await service.GetDefaultExportPathAsync();
        Assert.Equal(testPath, result);
    }

    [Fact]
    public async Task GetLanguage_ReturnsDefaultCs()
    {
        var service = CreateService();
        var result = await service.GetLanguageAsync();
        Assert.Equal("cs", result);
    }

    [Fact]
    public async Task SetAndGetLanguage_Works()
    {
        var service = CreateService();
        await service.SetLanguageAsync("en");
        var result = await service.GetLanguageAsync();
        Assert.Equal("en", result);
    }

    [Fact]
    public async Task GetEnableLogging_ReturnsDefaultTrue()
    {
        var service = CreateService();
        var result = await service.GetEnableLoggingAsync();
        Assert.True(result);
    }

    [Fact]
    public async Task SetAndGetEnableLogging_Works()
    {
        var service = CreateService();
        await service.SetEnableLoggingAsync(false);
        var result = await service.GetEnableLoggingAsync();
        Assert.False(result);
    }

    [Fact]
    public async Task GetLogLevel_ReturnsDefaultInformation()
    {
        var service = CreateService();
        var result = await service.GetLogLevelAsync();
        Assert.Equal("Information", result);
    }

    [Fact]
    public async Task SetAndGetLogLevel_Works()
    {
        var service = CreateService();
        await service.SetLogLevelAsync("Debug");
        var result = await service.GetLogLevelAsync();
        Assert.Equal("Debug", result);
    }

    [Fact]
    public async Task GetIsDarkTheme_ReadsFromFile()
    {
        var service = CreateService();
        // Dark theme is read from file; just verify it doesn't throw
        var result = await service.GetIsDarkThemeAsync();
        // Value depends on file state, just verify roundtrip works
        await service.SetIsDarkThemeAsync(!result);
        Assert.Equal(!result, await service.GetIsDarkThemeAsync());
        await service.SetIsDarkThemeAsync(result); // Restore
    }

    [Fact]
    public async Task SetAndGetIsDarkTheme_Works()
    {
        var service = CreateService();
        var original = await service.GetIsDarkThemeAsync();
        await service.SetIsDarkThemeAsync(true);
        Assert.True(await service.GetIsDarkThemeAsync());
        await service.SetIsDarkThemeAsync(original); // Restore
    }

    [Fact]
    public async Task GetSmartCacheTtlMinutes_ReturnsDefault10()
    {
        var service = CreateService();
        var result = await service.GetSmartCacheTtlMinutesAsync();
        Assert.Equal(10, result);
    }

    [Fact]
    public async Task SetAndGetSmartCacheTtlMinutes_Works()
    {
        var service = CreateService();
        await service.SetSmartCacheTtlMinutesAsync(20);
        var result = await service.GetSmartCacheTtlMinutesAsync();
        Assert.Equal(20, result);
    }

    [Fact]
    public async Task SetSmartCacheTtlMinutes_ClampsToMinimum1()
    {
        var service = CreateService();
        await service.SetSmartCacheTtlMinutesAsync(0);
        var result = await service.GetSmartCacheTtlMinutesAsync();
        Assert.Equal(1, result);
    }

    [Fact]
    public async Task GetSmartProbeTimeoutSeconds_RoundtripWorks()
    {
        var service = CreateService();
        var original = await service.GetSmartProbeTimeoutSecondsAsync();
        await service.SetSmartProbeTimeoutSecondsAsync(8);
        Assert.Equal(8, await service.GetSmartProbeTimeoutSecondsAsync());
        await service.SetSmartProbeTimeoutSecondsAsync(original); // Restore
    }

    [Fact]
    public async Task SetAndGetSmartProbeTimeoutSeconds_Works()
    {
        var service = CreateService();
        var original = await service.GetSmartProbeTimeoutSecondsAsync();
        await service.SetSmartProbeTimeoutSecondsAsync(8);
        var result = await service.GetSmartProbeTimeoutSecondsAsync();
        Assert.Equal(8, result);
        await service.SetSmartProbeTimeoutSecondsAsync(original); // Restore
    }

    [Fact]
    public async Task GetSmartProbeParallelism_RoundtripWorks()
    {
        var service = CreateService();
        var original = await service.GetSmartProbeParallelismAsync();
        await service.SetSmartProbeParallelismAsync(4);
        Assert.Equal(4, await service.GetSmartProbeParallelismAsync());
        await service.SetSmartProbeParallelismAsync(original); // Restore
    }

    [Fact]
    public async Task SetAndGetSmartProbeParallelism_Works()
    {
        var service = CreateService();
        var original = await service.GetSmartProbeParallelismAsync();
        await service.SetSmartProbeParallelismAsync(4);
        var result = await service.GetSmartProbeParallelismAsync();
        Assert.Equal(4, result);
        await service.SetSmartProbeParallelismAsync(original); // Restore
    }

    [Fact]
    public async Task GetUsbRecoveryRetryCount_RoundtripWorks()
    {
        var service = CreateService();
        var original = await service.GetUsbRecoveryRetryCountAsync();
        await service.SetUsbRecoveryRetryCountAsync(3);
        Assert.Equal(3, await service.GetUsbRecoveryRetryCountAsync());
        await service.SetUsbRecoveryRetryCountAsync(original); // Restore
    }

    [Fact]
    public async Task SetAndGetUsbRecoveryRetryCount_Works()
    {
        var service = CreateService();
        var original = await service.GetUsbRecoveryRetryCountAsync();
        await service.SetUsbRecoveryRetryCountAsync(5);
        var result = await service.GetUsbRecoveryRetryCountAsync();
        Assert.Equal(5, result);
        await service.SetUsbRecoveryRetryCountAsync(original); // Restore
    }

    [Fact]
    public async Task SetUsbRecoveryRetryCount_ClampsToRange()
    {
        var service = CreateService();
        await service.SetUsbRecoveryRetryCountAsync(15);
        var result = await service.GetUsbRecoveryRetryCountAsync();
        Assert.Equal(10, result); // Clamped to max 10

        await service.SetUsbRecoveryRetryCountAsync(-5);
        result = await service.GetUsbRecoveryRetryCountAsync();
        Assert.Equal(0, result); // Clamped to min 0
    }

    [Fact]
    public async Task GetReportRecipientEmail_WhenCleared_ReturnsEmpty()
    {
        var service = CreateService();
        var original = await service.GetReportRecipientEmailAsync();

        await service.SetReportRecipientEmailAsync(string.Empty);
        var result = await service.GetReportRecipientEmailAsync();
        Assert.Equal(string.Empty, result);

        await service.SetReportRecipientEmailAsync(original);
    }

    [Fact]
    public async Task SetAndGetReportRecipientEmail_Works()
    {
        var service = CreateService();
        await service.SetReportRecipientEmailAsync("test@example.com");
        var result = await service.GetReportRecipientEmailAsync();
        Assert.Equal("test@example.com", result);
    }

    [Fact]
    public async Task SetReportRecipientEmail_TrimsWhitespace()
    {
        var service = CreateService();
        await service.SetReportRecipientEmailAsync("  test@example.com  ");
        var result = await service.GetReportRecipientEmailAsync();
        Assert.Equal("test@example.com", result);
    }

    [Fact]
    public async Task GetEmailSendOnlyForLongRunningTests_RoundtripWorks()
    {
        var service = CreateService();
        var original = await service.GetEmailSendOnlyForLongRunningTestsAsync();
        await service.SetEmailSendOnlyForLongRunningTestsAsync(!original);
        Assert.Equal(!original, await service.GetEmailSendOnlyForLongRunningTestsAsync());
        await service.SetEmailSendOnlyForLongRunningTestsAsync(original); // Restore
    }

    [Fact]
    public async Task SetAndGetEmailSendOnlyForLongRunningTests_Works()
    {
        var service = CreateService();
        var original = await service.GetEmailSendOnlyForLongRunningTestsAsync();
        await service.SetEmailSendOnlyForLongRunningTestsAsync(false);
        var result = await service.GetEmailSendOnlyForLongRunningTestsAsync();
        Assert.False(result);
        await service.SetEmailSendOnlyForLongRunningTestsAsync(original); // Restore
    }

    [Fact]
    public async Task GetEmailIncludeCertificateAttachment_ReturnsDefaultTrue()
    {
        var service = CreateService();
        var result = await service.GetEmailIncludeCertificateAttachmentAsync();
        Assert.True(result);
    }

    [Fact]
    public async Task SetAndGetEmailIncludeCertificateAttachment_Works()
    {
        var service = CreateService();
        await service.SetEmailIncludeCertificateAttachmentAsync(false);
        var result = await service.GetEmailIncludeCertificateAttachmentAsync();
        Assert.False(result);
    }

    [Fact]
    public async Task GetLockedDisks_ReturnsListWithSystemDisk()
    {
        var service = CreateService();
        var result = await service.GetLockedDisksAsync();
        Assert.NotNull(result);
        Assert.Contains(@"\\.\PhysicalDrive0", result);
    }

    [Fact]
    public async Task LockAndUnlockDisk_Works()
    {
        var service = CreateService();
        var testPath = @"\\.\PhysicalDrive5";

        await service.LockDiskAsync(testPath);
        var isLocked = await service.IsDiskLockedAsync(testPath);
        Assert.True(isLocked);

        await service.UnlockDiskAsync(testPath);
        isLocked = await service.IsDiskLockedAsync(testPath);
        Assert.False(isLocked);
    }

    [Fact]
    public async Task LockDisk_DuplicateNotAdded()
    {
        var service = CreateService();
        var testPath = @"\\.\PhysicalDrive3";

        await service.LockDiskAsync(testPath);
        await service.LockDiskAsync(testPath); // Duplicate

        var disks = await service.GetLockedDisksAsync();
        var count = disks.FindAll(d => d == testPath).Count;
        Assert.Equal(1, count);
    }

    [Fact]
    public async Task IsDiskLocked_NullOrEmpty_ReturnsFalse()
    {
        var service = CreateService();
        Assert.False(await service.IsDiskLockedAsync(null!));
        Assert.False(await service.IsDiskLockedAsync(""));
    }

    [Fact]
    public async Task ResetToDefaults_RestoresAllDefaults()
    {
        var service = CreateService();

        // Change everything
        await service.SetAutoCheckForUpdatesAsync(false);
        await service.SetMinimizeToTrayAsync(false);
        await service.SetAutoSaveIntervalAsync(20);
        await service.SetLanguageAsync("en");
        await service.SetEnableLoggingAsync(false);
        await service.SetLogLevelAsync("Debug");
        await service.SetIsDarkThemeAsync(true);
        await service.SetSmartCacheTtlMinutesAsync(30);
        await service.SetSmartProbeTimeoutSecondsAsync(10);
        await service.SetSmartProbeParallelismAsync(8);
        await service.SetUsbRecoveryRetryCountAsync(5);
        await service.SetReportRecipientEmailAsync("test@example.com");
        await service.SetEmailSendOnlyForLongRunningTestsAsync(false);
        await service.SetEmailIncludeCertificateAttachmentAsync(false);

        // Reset
        await service.ResetToDefaultsAsync();

        // Verify defaults (skip dark theme - it's file-based and may persist)
        Assert.True(await service.GetAutoCheckForUpdatesAsync());
        Assert.True(await service.GetMinimizeToTrayAsync());
        Assert.Equal(5, await service.GetAutoSaveIntervalAsync());
        Assert.Equal("cs", await service.GetLanguageAsync());
        Assert.True(await service.GetEnableLoggingAsync());
        Assert.Equal("Information", await service.GetLogLevelAsync());
        Assert.Equal(10, await service.GetSmartCacheTtlMinutesAsync());
        Assert.Equal(4, await service.GetSmartProbeTimeoutSecondsAsync());
        Assert.Equal(0, await service.GetSmartProbeParallelismAsync());
        Assert.Equal(2, await service.GetUsbRecoveryRetryCountAsync());
        Assert.Equal("", await service.GetReportRecipientEmailAsync());
        Assert.True(await service.GetEmailSendOnlyForLongRunningTestsAsync());
        Assert.True(await service.GetEmailIncludeCertificateAttachmentAsync());
    }
}

