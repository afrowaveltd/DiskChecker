using DiskChecker.TUI.Services;
using Xunit;

namespace DiskChecker.Tui.Tests;

public sealed class TuiOptionsTests
{
    [Fact]
    public void ParsesDatabaseLanguageAndScreen()
    {
        var options = TuiOptions.Parse(new[] { "--db", "/tmp/x.db", "--lang", "en", "--screen", "smart" });
        Assert.Equal("/tmp/x.db", options.DatabasePath);
        Assert.Equal("en", options.Language);
        Assert.Equal("smart", options.StartScreen);
        Assert.False(options.ShowHelp);
    }

    [Fact]
    public void PositionalArgumentSelectsScreen()
    {
        var options = TuiOptions.Parse(new[] { "analysis" });
        Assert.Equal("analysis", options.StartScreen);
    }

    [Fact]
    public void HelpAndVersionAreRecognised()
    {
        Assert.True(TuiOptions.Parse(new[] { "--help" }).ShowHelp);
        Assert.True(TuiOptions.Parse(new[] { "-v" }).ShowVersion);
        Assert.Contains("--db", TuiOptions.HelpText, StringComparison.Ordinal);
        Assert.Contains("disks", TuiOptions.HelpText, StringComparison.Ordinal);
    }

    [Fact]
    public void UnknownFlagsAreIgnored()
    {
        var options = TuiOptions.Parse(new[] { "--nonsense" });
        Assert.Null(options.StartScreen);
        Assert.False(options.ShowHelp);
    }
}
