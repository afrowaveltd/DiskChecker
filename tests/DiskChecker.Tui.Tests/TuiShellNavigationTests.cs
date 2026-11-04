using DiskChecker.TUI.App;
using DiskChecker.TUI.Screens;
using DiskChecker.TUI.Services;
using DiskChecker.Tui.Tests.Support;
using Xunit;

namespace DiskChecker.Tui.Tests;

public sealed class TuiShellNavigationTests
{
    private static readonly Dictionary<string, Type> Catalog = new(StringComparer.OrdinalIgnoreCase)
    {
        [ScreenIds.DiskSelection] = typeof(ScriptedScreenA),
        [ScreenIds.SmartCheck] = typeof(ScriptedScreenB),
    };

    [Fact]
    public async Task StartScreenRunsAndExitEndsTheApplication()
    {
        using var harness = TestHarness.Create();
        ScriptedScreenA.Reset(_ => ScreenResult.Exit());
        var shell = new TuiShell(harness.Services, harness.Context, harness.Cancellation, Catalog);

        int exitCode = await shell.RunAsync(ScreenIds.DiskSelection, TestContext.Current.CancellationToken);

        Assert.Equal(0, exitCode);
        Assert.Empty(ScriptedScreenA.Behaviour);
    }

    [Fact]
    public async Task MainMenuNavigatesToAnotherScreen()
    {
        using var harness = TestHarness.Create();
        ScriptedScreenA.Reset(_ => ScreenResult.Menu(), _ => ScreenResult.Exit());
        ScriptedScreenB.Reset(_ => ScreenResult.Exit());
        harness.Io.EnqueueChoice(ScreenIds.SmartCheck);

        var shell = new TuiShell(harness.Services, harness.Context, harness.Cancellation, Catalog);
        await shell.RunAsync(ScreenIds.DiskSelection, TestContext.Current.CancellationToken);

        Assert.Equal(1, ScriptedScreenB.RenderCount);
        Assert.True(harness.Io.Contains("Navigace"));
    }

    [Fact]
    public async Task BackReturnsToThePreviousScreen()
    {
        using var harness = TestHarness.Create();
        int aRenders = 0;
        ScriptedScreenA.Reset(
            _ => { aRenders++; return ScreenResult.Navigate(ScreenIds.SmartCheck); },
            _ => { aRenders++; return ScreenResult.Exit(); });
        ScriptedScreenB.Reset(_ => ScreenResult.Back());

        var shell = new TuiShell(harness.Services, harness.Context, harness.Cancellation, Catalog);
        await shell.RunAsync(ScreenIds.DiskSelection, TestContext.Current.CancellationToken);

        Assert.Equal(2, aRenders);
        Assert.Equal(1, ScriptedScreenB.RenderCount);
    }

    [Fact]
    public async Task NavigationIsBlockedWhileADiskOperationRuns()
    {
        using var harness = TestHarness.Create();
        IDisposable? lockHandle = null;
        ScriptedScreenA.Reset(
            context =>
            {
                lockHandle = context.LockNavigation("Test běží");
                return ScreenResult.Navigate(ScreenIds.SmartCheck);
            },
            _ =>
            {
                lockHandle?.Dispose();
                return ScreenResult.Exit();
            });
        ScriptedScreenB.Reset(_ => ScreenResult.Exit());

        var shell = new TuiShell(harness.Services, harness.Context, harness.Cancellation, Catalog);
        await shell.RunAsync(ScreenIds.DiskSelection, TestContext.Current.CancellationToken);

        Assert.Equal(0, ScriptedScreenB.RenderCount);
        Assert.True(harness.Io.Contains("Probíhá operace s diskem"));
    }

    [Fact]
    public async Task UnknownStartScreenFallsBackToDiskSelection()
    {
        using var harness = TestHarness.Create();
        ScriptedScreenA.Reset(_ => ScreenResult.Exit());
        var shell = new TuiShell(harness.Services, harness.Context, harness.Cancellation, Catalog);

        await shell.RunAsync("does-not-exist", TestContext.Current.CancellationToken);

        Assert.True(harness.Io.Contains("Neznámá obrazovka"));
    }

    [Fact]
    public void ScreenCatalogContainsEveryNavigationTarget()
    {
        string[] expected =
        {
            ScreenIds.DiskSelection, ScreenIds.DiskCards, ScreenIds.SurfaceTest, ScreenIds.SmartCheck,
            ScreenIds.SeekTest, ScreenIds.AbsoluteDestructive, ScreenIds.SafeDestructive, ScreenIds.Analysis,
            ScreenIds.Comparison, ScreenIds.Report, ScreenIds.History, ScreenIds.Certificates, ScreenIds.Backup,
            ScreenIds.Restore, ScreenIds.Settings, ScreenIds.About,
        };

        foreach (string id in expected)
        {
            Assert.True(TuiShell.DefaultScreenTypes.ContainsKey(id), "Missing screen registration for " + id);
        }
    }
}
