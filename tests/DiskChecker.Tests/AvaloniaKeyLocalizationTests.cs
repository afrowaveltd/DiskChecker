using DiskChecker.UI.Avalonia.Services;
using Xunit;

namespace DiskChecker.Tests;

public sealed class AvaloniaKeyLocalizationTests
{
    [Theory]
    [InlineData("cs", "Export / import dat", "Latence přístupů (Seek)")]
    [InlineData("en", "Export / import data", "Seek latency")]
    public void SettingsAndSeekTabHaveExplicitLabels(
        string language, string exportImport, string seekLatency)
    {
        var locale = new LocaleService();
        locale.SetLocale(language);
        Assert.Equal(exportImport, locale.Get("Settings.ExportImport"));
        Assert.Equal(seekLatency, locale.Get("DiskCardDetailView.SeekLatency"));
    }
}
