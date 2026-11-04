using DiskChecker.Core.Interfaces;
using DiskChecker.Core.Models;
using DiskChecker.TUI.Screens;
using DiskChecker.TUI.Services;
using DiskChecker.Tui.Tests.Support;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using NSubstitute;
using Xunit;

namespace DiskChecker.Tui.Tests;

public sealed class AnalysisScreenTests
{
    [Fact]
    public async Task RendersMeasurementsAndSummaryLine()
    {
        var fake = AnalysisFake.Create();
        using var harness = TestHarness.Create(configure: fake.Register);
        harness.Io.EnqueueChoice("back");

        var result = await new AnalysisScreen().ShowAsync(harness.Context, TestContext.Current.CancellationToken);

        Assert.Equal(ScreenAction.Back, result.Action);
        Assert.Equal(ScreenIds.Analysis, new AnalysisScreen().Id);
        Assert.Equal("Nav.Analysis", new AnalysisScreen().TitleKey);
        Assert.True(harness.Io.Contains("Samsung SSD 870 EVO 500GB"));
        Assert.True(harness.Io.Contains("FullReadWrite"));
        Assert.True(harness.Io.Contains("známka A"));
        Assert.True(harness.Io.Contains("Telemetry: 4 bodů"));
    }

    [Fact]
    public async Task RendersChartsCountsAndTopEventLists()
    {
        var fake = AnalysisFake.Create();
        using var harness = TestHarness.Create(configure: fake.Register);
        harness.Io.EnqueueChoice("back");

        await new AnalysisScreen().ShowAsync(harness.Context, TestContext.Current.CancellationToken);

        Assert.True(harness.Io.Contains("Propustnost podle průběhu disku"));
        Assert.True(harness.Io.Contains("Seek latence podle indexu vzorku"));
        Assert.True(harness.Io.Contains("Anomálie: 2, stally: 2"));
        Assert.True(harness.Io.Contains("Severity 9"));
        Assert.True(harness.Io.Contains("Anomálie"));
        Assert.True(harness.Io.Contains("Stally"));
        Assert.True(harness.Io.Contains("2.40s"));
        Assert.True(harness.Io.Contains("SMART: bez významných negativních změn."));
    }

    [Fact]
    public async Task CompactTerminalOmitsWideOnlyCharts()
    {
        var fake = AnalysisFake.Create();
        using var harness = TestHarness.Create(configure: fake.Register, terminalWidth: 120);
        harness.Io.EnqueueChoice("back");

        await new AnalysisScreen().ShowAsync(harness.Context, TestContext.Current.CancellationToken);

        Assert.True(harness.Io.Contains("Režim: kompaktní"));
        Assert.False(harness.Io.Contains("Teplotní trend"));
    }

    [Fact]
    public async Task WideTerminalRendersTemperatureAndSmartTrendCharts()
    {
        var fake = AnalysisFake.Create();
        using var harness = TestHarness.Create(configure: fake.Register, terminalWidth: 170);
        harness.Io.EnqueueChoice("back");

        await new AnalysisScreen().ShowAsync(harness.Context, TestContext.Current.CancellationToken);

        Assert.True(harness.Io.Contains("Režim: plný"));
        Assert.True(harness.Io.Contains("Teplotní trend"));
        Assert.True(harness.Io.Contains("SMART trendy"));
        Assert.True(harness.Io.Contains("snapshotů"));
        Assert.True(harness.Io.Contains("Reallocated sectors"));
        Assert.True(harness.Io.Contains("Wear Leveling"));
    }

    [Fact]
    public async Task ManualZoomReloadsTelemetryForTheProgressWindow()
    {
        var fake = AnalysisFake.Create();
        using var harness = TestHarness.Create(configure: fake.Register);
        harness.Io.EnqueueChoice("zoom");
        harness.Io.EnqueueAsk("20");
        harness.Io.EnqueueAsk("40");
        harness.Io.EnqueueChoice("back");

        await new AnalysisScreen().ShowAsync(harness.Context, TestContext.Current.CancellationToken);

        var window = Assert.Single(fake.ZoomRequests);
        Assert.Equal(20d, window.Start, 3);
        Assert.Equal(40d, window.End, 3);
        Assert.True(harness.Io.Contains("Výřez 20"));
    }

    [Fact]
    public async Task ResetZoomReloadsTheWholeRange()
    {
        var fake = AnalysisFake.Create();
        using var harness = TestHarness.Create(configure: fake.Register);
        harness.Io.EnqueueChoice("zoom-reset");
        harness.Io.EnqueueChoice("back");

        await new AnalysisScreen().ShowAsync(harness.Context, TestContext.Current.CancellationToken);

        var window = Assert.Single(fake.ZoomRequests);
        Assert.Equal(0d, window.Start, 3);
        Assert.Equal(100d, window.End, 3);
        Assert.True(harness.Io.Contains("Zoom zrušen"));
    }

    [Fact]
    public async Task InvalidZoomWindowKeepsTheScreenUsable()
    {
        var fake = AnalysisFake.Create();
        using var harness = TestHarness.Create(configure: fake.Register);
        harness.Io.EnqueueChoice("zoom");
        harness.Io.EnqueueAsk("abc");
        harness.Io.EnqueueAsk("40");
        harness.Io.EnqueueChoice("back");

        await new AnalysisScreen().ShowAsync(harness.Context, TestContext.Current.CancellationToken);

        Assert.Empty(fake.ZoomRequests);
        Assert.True(harness.Io.Contains("Neplatný rozsah výřezu"));
    }

    [Fact]
    public async Task AnomalyDetailUsesTheDesktopWording()
    {
        var fake = AnalysisFake.Create();
        using var harness = TestHarness.Create(configure: fake.Register);
        harness.Io.EnqueueChoice("anomaly");
        harness.Io.EnqueueChoice("anomaly-0");
        harness.Io.EnqueueChoice("back");

        await new AnalysisScreen().ShowAsync(harness.Context, TestContext.Current.CancellationToken);

        Assert.True(harness.Io.Contains("Anomálie (Write)"));
        Assert.True(harness.Io.Contains("Severity: 9"));
        Assert.True(harness.Io.Contains("LBA512"));
        Assert.True(harness.Io.Contains("Vybrána anomálie"));
        Assert.NotEmpty(fake.ZoomRequests);
    }

    [Fact]
    public async Task StallDetailUsesTheDesktopWording()
    {
        var fake = AnalysisFake.Create();
        using var harness = TestHarness.Create(configure: fake.Register);
        harness.Io.EnqueueChoice("stall");
        harness.Io.EnqueueChoice("stall-1");
        harness.Io.EnqueueChoice("back");

        await new AnalysisScreen().ShowAsync(harness.Context, TestContext.Current.CancellationToken);

        Assert.True(harness.Io.Contains("Stall (Read)"));
        Assert.True(harness.Io.Contains("Vybrán stall"));
        Assert.NotEmpty(fake.ZoomRequests);
    }

    [Fact]
    public async Task SelectingAnotherMeasurementLoadsItsAnalysisData()
    {
        var fake = AnalysisFake.Create();
        using var harness = TestHarness.Create(configure: fake.Register);
        harness.Io.EnqueueChoice("select");
        harness.Io.EnqueueChoice("measurement-1");
        harness.Io.EnqueueChoice("back");

        await new AnalysisScreen().ShowAsync(harness.Context, TestContext.Current.CancellationToken);

        Assert.True(harness.Io.Contains("Načten detail testu 43"));
    }

    [Fact]
    public async Task MenuReturnsTheNavigationIntent()
    {
        var fake = AnalysisFake.Create();
        using var harness = TestHarness.Create(configure: fake.Register);
        harness.Io.EnqueueChoice(TuiKeys.MainMenu);

        var result = await new AnalysisScreen().ShowAsync(harness.Context, TestContext.Current.CancellationToken);

        Assert.Equal(ScreenAction.Menu, result.Action);
    }

    [Fact]
    public async Task WithoutMeasurementsTheWorkspaceExplainsTheEmptyState()
    {
        var repository = Substitute.For<IDiskCardRepository>();
        repository.GetAllAsync().Returns(new List<DiskCard>());
        var analysis = Substitute.For<ITestAnalysisDataService>();
        using var harness = TestHarness.Create(configure: services =>
        {
            services.RemoveAll<IDiskCardRepository>();
            services.AddSingleton(repository);
            services.RemoveAll<ITestAnalysisDataService>();
            services.AddSingleton(analysis);
        });
        harness.Io.EnqueueChoice("back");

        await new AnalysisScreen().ShowAsync(harness.Context, TestContext.Current.CancellationToken);

        Assert.True(harness.Io.Contains("Nejsou dostupná žádná uložená měření"));
        Assert.False(harness.Io.Contains("Severity"));
    }

    private sealed class AnalysisFake
    {
        private readonly List<(double Start, double End)> _zoomRequests = new();

        private AnalysisFake(IDiskCardRepository repository, ITestAnalysisDataService analysis)
        {
            Repository = repository;
            Analysis = analysis;
        }

        public IDiskCardRepository Repository { get; }

        public ITestAnalysisDataService Analysis { get; }

        public IReadOnlyList<(double Start, double End)> ZoomRequests => _zoomRequests;

        public void Register(IServiceCollection services)
        {
            services.RemoveAll<IDiskCardRepository>();
            services.AddSingleton(Repository);
            services.RemoveAll<ITestAnalysisDataService>();
            services.AddSingleton(Analysis);
        }

        public static AnalysisFake Create()
        {
            var card = new DiskCard
            {
                Id = 7,
                ModelName = "Samsung SSD 870 EVO 500GB",
                SerialNumber = "S6P1NX0T",
                OverallGrade = "A",
                OverallScore = 94.5,
                TestCount = 2,
            };

            var repository = Substitute.For<IDiskCardRepository>();
            repository.GetAllAsync().Returns(new List<DiskCard> { card });
            repository.GetByIdAsync(7).Returns(card);
            repository.GetSmartSnapshotsAsync(7).Returns(new List<SmartSnapshotRecord>
            {
                new()
                {
                    DiskCardId = 7,
                    RetrievedAtUtc = new DateTime(2026, 1, 1, 8, 0, 0, DateTimeKind.Utc),
                    Temperature = 38,
                    ReallocatedSectorCount = 0,
                    WearLevelingCount = 98,
                    PercentageUsed = 4,
                },
                new()
                {
                    DiskCardId = 7,
                    RetrievedAtUtc = new DateTime(2026, 1, 8, 8, 0, 0, DateTimeKind.Utc),
                    Temperature = 45,
                    ReallocatedSectorCount = 3,
                    WearLevelingCount = 96,
                    PercentageUsed = 6,
                },
            });

            var summaries = new List<TestAnalysisSummary>
            {
                new()
                {
                    TestSessionId = 42,
                    DiskCardId = 7,
                    DiskModel = card.ModelName,
                    SerialNumber = card.SerialNumber,
                    TestType = TestType.FullReadWrite,
                    StartedAt = new DateTime(2026, 1, 2, 10, 30, 0),
                    CompletedAt = new DateTime(2026, 1, 2, 11, 30, 0),
                    Duration = TimeSpan.FromHours(1),
                    Grade = "A",
                    Score = 94.5,
                    TelemetrySampleCount = 4,
                    AnomalyCount = 2,
                    StallCount = 2,
                    SeekSampleCount = 3,
                },
                new()
                {
                    TestSessionId = 43,
                    DiskCardId = 7,
                    DiskModel = card.ModelName,
                    SerialNumber = card.SerialNumber,
                    TestType = TestType.QuickRead,
                    StartedAt = new DateTime(2026, 1, 3, 9, 0, 0),
                    Grade = "B",
                    Score = 81,
                    TelemetrySampleCount = 2,
                },
            };

            var analysis = Substitute.For<ITestAnalysisDataService>();
            analysis.GetDiskAnalysisSummariesAsync(7, Arg.Any<CancellationToken>()).Returns(summaries);
            analysis.GetAnalysisDataAsync(42, Arg.Any<CancellationToken>()).Returns(CreateAnalysisData(42));
            analysis.GetAnalysisDataAsync(43, Arg.Any<CancellationToken>()).Returns(CreateAnalysisData(43));

            var fake = new AnalysisFake(repository, analysis);
            analysis.GetTelemetryRangeAsync(42, Arg.Any<double>(), Arg.Any<double>(), Arg.Any<CancellationToken>())
                .Returns(call =>
                {
                    fake._zoomRequests.Add((call.ArgAt<double>(1), call.ArgAt<double>(2)));
                    return new List<TestTelemetrySample>
                    {
                        new()
                        {
                            TestSessionId = 42,
                            Phase = TelemetrySamplePhase.Write,
                            SequenceIndex = 0,
                            ProgressPercent = 25,
                            SpeedMBps = 380,
                        },
                    };
                });

            return fake;
        }

        private static TestAnalysisData CreateAnalysisData(int sessionId)
        {
            var session = new TestSession
            {
                Id = sessionId,
                DiskCardId = 7,
                TestType = sessionId == 42 ? TestType.FullReadWrite : TestType.QuickRead,
                StartedAt = new DateTime(2026, 1, 2, 10, 30, 0),
                Grade = sessionId == 42 ? "A" : "B",
                Score = sessionId == 42 ? 94.5 : 81,
                AverageWriteSpeedMBps = 420,
                AverageReadSpeedMBps = 510,
            };

            var data = new TestAnalysisData
            {
                Session = session,
                TelemetrySamples =
                {
                    new TestTelemetrySample
                    {
                        TestSessionId = sessionId,
                        Phase = TelemetrySamplePhase.Write,
                        SequenceIndex = 0,
                        ProgressPercent = 10,
                        SpeedMBps = 420,
                    },
                    new TestTelemetrySample
                    {
                        TestSessionId = sessionId,
                        Phase = TelemetrySamplePhase.Write,
                        SequenceIndex = 1,
                        ProgressPercent = 60,
                        SpeedMBps = 400,
                    },
                    new TestTelemetrySample
                    {
                        TestSessionId = sessionId,
                        Phase = TelemetrySamplePhase.Read,
                        SequenceIndex = 2,
                        ProgressPercent = 70,
                        SpeedMBps = 510,
                    },
                    new TestTelemetrySample
                    {
                        TestSessionId = sessionId,
                        Phase = TelemetrySamplePhase.Read,
                        SequenceIndex = 3,
                        ProgressPercent = 95,
                        SpeedMBps = 480,
                    },
                },
                AnomalyEvents =
                {
                    new TestAnomalyEvent
                    {
                        TestSessionId = sessionId,
                        Phase = TelemetrySamplePhase.Write,
                        StartProgressPercent = 20,
                        EndProgressPercent = 30,
                        StartLba512 = 1000,
                        EndLba512 = 2000,
                        StartBytesProcessed = 512000,
                        EndBytesProcessed = 1024000,
                        DurationMs = 1500,
                        MinSpeedMBps = 120,
                        AvgSpeedMBps = 260,
                        MaxSpeedMBps = 400,
                        MaxDeviationPercent = 41.5,
                        SeverityScore = 9,
                        DefectType = "SlowArea",
                    },
                    new TestAnomalyEvent
                    {
                        TestSessionId = sessionId,
                        Phase = TelemetrySamplePhase.Read,
                        StartProgressPercent = 70,
                        EndProgressPercent = 75,
                        DurationMs = 800,
                        MinSpeedMBps = 300,
                        AvgSpeedMBps = 420,
                        MaxSpeedMBps = 500,
                        MaxDeviationPercent = 12,
                        SeverityScore = 4,
                    },
                },
                StallEvents =
                {
                    new TestStallEvent
                    {
                        TestSessionId = sessionId,
                        Phase = TelemetrySamplePhase.Write,
                        StartedAtUtc = new DateTime(2026, 1, 2, 10, 40, 0, DateTimeKind.Utc),
                        EndedAtUtc = new DateTime(2026, 1, 2, 10, 40, 2, DateTimeKind.Utc),
                        DurationMs = 2400,
                        StartProgressPercent = 40,
                        EndProgressPercent = 45,
                        BytesProcessed = 512000,
                        LastSpeedBeforeStallMBps = 410,
                        FirstSpeedAfterStallMBps = 390,
                    },
                    new TestStallEvent
                    {
                        TestSessionId = sessionId,
                        Phase = TelemetrySamplePhase.Read,
                        StartedAtUtc = new DateTime(2026, 1, 2, 10, 50, 0, DateTimeKind.Utc),
                        EndedAtUtc = new DateTime(2026, 1, 2, 10, 50, 1, DateTimeKind.Utc),
                        DurationMs = 1100,
                        StartProgressPercent = 80,
                        EndProgressPercent = 82,
                        BytesProcessed = 256000,
                    },
                },
                SeekSamples =
                {
                    new SeekSampleRecord { TestSessionId = sessionId, Index = 1, LatencyMs = 3.4 },
                    new SeekSampleRecord { TestSessionId = sessionId, Index = 2, LatencyMs = 4.1 },
                    new SeekSampleRecord { TestSessionId = sessionId, Index = 3, LatencyMs = 3.9 },
                },
                TemperatureSamples =
                {
                    new TemperatureSample
                    {
                        Timestamp = new DateTime(2026, 1, 2, 10, 30, 0),
                        TemperatureCelsius = 38,
                        Phase = "Write",
                        ProgressPercent = 10,
                    },
                    new TemperatureSample
                    {
                        Timestamp = new DateTime(2026, 1, 2, 11, 10, 0),
                        TemperatureCelsius = 46,
                        Phase = "Read",
                        ProgressPercent = 90,
                    },
                },
                SmartReport = new SmartAnalysisReport
                {
                    Summary = "SMART: bez významných negativních změn.",
                },
            };

            return data;
        }
    }
}
