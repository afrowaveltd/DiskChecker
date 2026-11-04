using DiskChecker.TUI.Services;
using Xunit;

namespace DiskChecker.Tui.Tests;

public sealed class TuiProgressTests
{
    [Fact]
    public void SnapshotReflectsUpdates()
    {
        var progress = new TuiProgress("Test")
        {
            Phase = "Zápis",
            Percent = 42.5,
            SpeedText = "120.0 MB/s",
            Errors = 2,
        };
        progress.SetMetric("Blok", "10 / 20");
        progress.AddLog("start");

        var snapshot = progress.Snapshot();

        Assert.Equal("Test", snapshot.Title);
        Assert.Equal("Zápis", snapshot.Phase);
        Assert.Equal(42.5, snapshot.Percent, 3);
        Assert.Equal(2, snapshot.Errors);
        Assert.Single(snapshot.Log);
        Assert.Contains(snapshot.Metrics, metric => metric.Label == "Blok" && metric.Value == "10 / 20");
    }

    [Fact]
    public void PercentIsClamped()
    {
        var progress = new TuiProgress { Percent = 140 };
        Assert.Equal(100, progress.Percent);
        progress.Percent = -5;
        Assert.Equal(0, progress.Percent);
    }

    [Fact]
    public void PhaseStatesAreTracked()
    {
        var progress = new TuiProgress();
        progress.AddPhase("Příprava");
        progress.AddPhase("Sanitizace 1");

        progress.SetPhaseState(0, TuiPhaseState.Completed, "hotovo");
        progress.SetPhaseState(1, TuiPhaseState.Running, "běží", 25);

        var snapshot = progress.Snapshot();
        Assert.Equal(2, snapshot.Phases.Count);
        Assert.Equal(TuiPhaseState.Completed, snapshot.Phases[0].State);
        Assert.Equal(100, snapshot.Phases[0].Percent);
        Assert.Equal(25, snapshot.Phases[1].Percent);
        Assert.Equal("běží", snapshot.Phases[1].Detail);
    }

    [Fact]
    public void SetPhaseStateIgnoresUnknownIndex()
    {
        var progress = new TuiProgress();
        progress.SetPhaseState(4, TuiPhaseState.Failed, "x");
        Assert.Empty(progress.Snapshot().Phases);
    }

    [Fact]
    public void EtaIsDerivedFromProgress()
    {
        var progress = new TuiProgress { Percent = 50 };
        progress.UpdateFromElapsed(DateTime.UtcNow.AddSeconds(-10));
        Assert.Equal("00:10", progress.EtaText);
        Assert.StartsWith("00:00", progress.ElapsedText);
    }

    [Fact]
    public void EtaIsZeroWhenFinished()
    {
        var progress = new TuiProgress { Percent = 100 };
        progress.UpdateFromElapsed(DateTime.UtcNow.AddSeconds(-10));
        Assert.Equal("00:00", progress.EtaText);
    }

    [Fact]
    public void LogIsBounded()
    {
        var progress = new TuiProgress();
        for (int i = 0; i < 300; i++)
        {
            progress.AddLog("line " + i);
        }

        var snapshot = progress.Snapshot();
        Assert.Equal(8, snapshot.Log.Count);
        Assert.Equal("line 299", snapshot.Log[^1]);
    }

    [Fact]
    public void RendererShowsPercentPhaseAndErrors()
    {
        var progress = new TuiProgress("Absolutní test")
        {
            Phase = "Sanitizace 1",
            Percent = 12.5,
            Errors = 3,
            SpeedText = "100.0 MB/s",
        };
        progress.AddPhase("Sanitizace 1");
        progress.SetPhaseState(0, TuiPhaseState.Running, "Zápis", 12.5);

        string output = string.Join("\n", TuiProgressRenderer.BuildLines(progress.Snapshot(), 100));

        Assert.Contains("Absolutní test", output, StringComparison.Ordinal);
        Assert.Contains("12.5%", output, StringComparison.Ordinal);
        Assert.Contains("Sanitizace 1", output, StringComparison.Ordinal);
        Assert.Contains("100.0 MB/s", output, StringComparison.Ordinal);
        Assert.Contains("⚠ 3", output, StringComparison.Ordinal);
    }

    [Fact]
    public void RendererRendersChartWhenDataPresent()
    {
        var progress = new TuiProgress("Test");
        var chart = new TuiChart { MinX = 0, MaxX = 100 };
        chart.AddSeries("write", TuiTheme.WriteSeries).Add(50, 100);
        progress.Chart = chart;

        string output = string.Join("\n", TuiProgressRenderer.BuildLines(progress.Snapshot(), 100));
        Assert.Contains("█", output, StringComparison.Ordinal);
    }
}
