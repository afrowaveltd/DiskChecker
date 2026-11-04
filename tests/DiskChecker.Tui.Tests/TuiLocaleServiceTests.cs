using DiskChecker.TUI.Services;
using DiskChecker.Tui.Tests.Support;
using Xunit;

namespace DiskChecker.Tui.Tests;

public sealed class TuiLocaleServiceTests
{
    [Fact]
    public void LoadsCzechAndEnglishCatalogs()
    {
        var locale = new TuiLocaleService(TestPaths.LocalesDirectory);
        Assert.Contains("cs", locale.AvailableLocales);
        Assert.Contains("en", locale.AvailableLocales);
        Assert.True(locale.KeyCount > 100);
    }

    [Fact]
    public void SwitchingLocaleChangesVocabulary()
    {
        var locale = new TuiLocaleService(TestPaths.LocalesDirectory);
        string czech = locale.GetString("Nav.DiskSelection");
        locale.SetLocale("en");
        string english = locale.GetString("Nav.DiskSelection");

        Assert.False(string.IsNullOrWhiteSpace(czech));
        Assert.False(string.IsNullOrWhiteSpace(english));
        Assert.NotEqual(czech, english);
        Assert.Equal("en", locale.CurrentLocale);
    }

    [Fact]
    public void MissingKeysUseVisibleMarker()
    {
        var locale = new TuiLocaleService(TestPaths.LocalesDirectory);
        Assert.Equal("[[This.Key.Does.Not.Exist]]", locale.GetString("This.Key.Does.Not.Exist"));
    }

    [Fact]
    public void FormattingArgumentsAreApplied()
    {
        var locale = new TuiLocaleService(TestPaths.LocalesDirectory);
        Assert.Equal("fallback", locale.GetString("Test.Key.WithArgument", "fallback"));
        locale.SetLocale("cs");
        string formatted = locale.Get("Analysis.Status.AnalysisProgress", "42");
        Assert.Contains("42", formatted, StringComparison.Ordinal);
    }

    [Fact]
    public void UnknownLocaleFallsBackToCzech()
    {
        var locale = new TuiLocaleService(TestPaths.LocalesDirectory);
        locale.SetLocale("xx");
        Assert.Equal("cs", locale.CurrentLocale);
        Assert.False(string.IsNullOrWhiteSpace(locale.GetString("Nav.DiskSelection")));
    }

    [Fact]
    public void LocaleChangedEventFires()
    {
        var locale = new TuiLocaleService(TestPaths.LocalesDirectory);
        int raised = 0;
        locale.LocaleChanged += (_, _) => raised++;
        locale.SetLocale("en");
        Assert.Equal(1, raised);
    }

    [Fact]
    public void TerminalKeysUsedByTheShellExist()
    {
        var locale = new TuiLocaleService(TestPaths.LocalesDirectory);
        string[] keys =
        {
            "Nav.DiskSelection", "Nav.DiskCards", "Nav.SurfaceTest", "Nav.SmartCheck", "Nav.SeekTest",
            "Nav.DestructiveTest", "Nav.SafeDestructiveTest", "Nav.Analysis", "Nav.Comparison", "Nav.Report",
            "Nav.History", "Nav.Certificates", "Nav.Backup", "Nav.Restore", "Nav.Settings",
        };

        foreach (string key in keys)
        {
            string value = locale.GetString(key);
            Assert.DoesNotContain("[[", value, StringComparison.Ordinal);
        }
    }
}
