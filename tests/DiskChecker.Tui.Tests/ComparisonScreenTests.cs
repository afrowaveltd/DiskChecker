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

public sealed class ComparisonScreenTests
{
    [Fact]
    public async Task LoadsOnlyTestedDisks()
    {
        var fake = ComparisonFake.Create();
        using var harness = TestHarness.Create(configure: fake.Register);
        harness.Io.EnqueueChoice("back");

        var result = await new ComparisonScreen().ShowAsync(harness.Context, TestContext.Current.CancellationToken);

        Assert.Equal(ScreenAction.Back, result.Action);
        Assert.Equal(ScreenIds.Comparison, new ComparisonScreen().Id);
        Assert.Equal("Nav.Comparison", new ComparisonScreen().TitleKey);
        Assert.True(harness.Io.Contains("Testované disky"));
        Assert.True(harness.Io.Contains("Nalezeno 6 testovaných disků."));
        Assert.True(harness.Io.Contains("Samsung SSD 870 EVO"));
        Assert.False(harness.Io.Contains("Untested NVMe"));
        Assert.True(harness.Io.Contains("Zatím žádný disk"));
    }

    [Fact]
    public async Task CompareRequiresTwoDisksBeforeItIsOffered()
    {
        var fake = ComparisonFake.Create();
        using var harness = TestHarness.Create(configure: fake.Register);
        harness.Io.EnqueueChoice("add");
        harness.Io.EnqueueChoice("add-1");
        harness.Io.EnqueueChoice("back");

        await new ComparisonScreen().ShowAsync(harness.Context, TestContext.Current.CancellationToken);

        Assert.Contains(harness.Io.LastChoices, choice => choice.Key == "compare" && !choice.Enabled);
        Assert.Contains(harness.Io.LastChoices, choice => choice.Key == "performance" && !choice.Enabled);
        Assert.True(harness.Io.Contains("Vybráno 1 disků k porovnání."));
    }

    [Fact]
    public async Task ComparesSelectedDisksAndRendersTheRankedTable()
    {
        var fake = ComparisonFake.Create();
        using var harness = TestHarness.Create(configure: fake.Register);
        harness.Io.EnqueueChoice("add");
        harness.Io.EnqueueChoice("add-1");
        harness.Io.EnqueueChoice("add");
        harness.Io.EnqueueChoice("add-2");
        harness.Io.EnqueueChoice("compare");
        harness.Io.EnqueueChoice("back");

        await new ComparisonScreen().ShowAsync(harness.Context, TestContext.Current.CancellationToken);

        Assert.Equal(new[] { 1, 2 }, fake.ComparedIds);
        Assert.True(harness.Io.Contains("Pořadí porovnání"));
        Assert.True(harness.Io.Contains("Výborný stav"));
        Assert.True(harness.Io.Contains("Skóre (0–100)"));
        Assert.True(harness.Io.Contains(TuiFormats.Bar(95, 28)));
        Assert.True(harness.Io.Contains("Porovnání dokončeno"));
        Assert.True(harness.Io.Contains("Nejlepší disk:"));
    }

    [Fact]
    public async Task MaximumOfFiveDisksCanBeSelected()
    {
        var fake = ComparisonFake.Create();
        using var harness = TestHarness.Create(configure: fake.Register);
        foreach (int id in new[] { 1, 2, 3, 4, 5 })
        {
            harness.Io.EnqueueChoice("add");
            harness.Io.EnqueueChoice("add-" + id.ToString(System.Globalization.CultureInfo.InvariantCulture));
        }

        harness.Io.EnqueueChoice("add");
        harness.Io.EnqueueChoice("add-6");
        harness.Io.EnqueueChoice("back");

        await new ComparisonScreen().ShowAsync(harness.Context, TestContext.Current.CancellationToken);

        Assert.True(harness.Io.Contains("Maximálně 5 disků pro porovnání."));
        Assert.True(harness.Io.Contains("Vybrané disky (5/5)"));
    }

    [Fact]
    public async Task RemovesADiskFromTheSelection()
    {
        var fake = ComparisonFake.Create();
        using var harness = TestHarness.Create(configure: fake.Register);
        harness.Io.EnqueueChoice("add");
        harness.Io.EnqueueChoice("add-1");
        harness.Io.EnqueueChoice("add");
        harness.Io.EnqueueChoice("add-2");
        harness.Io.EnqueueChoice("remove");
        harness.Io.EnqueueChoice("remove-1");
        harness.Io.EnqueueChoice("back");

        await new ComparisonScreen().ShowAsync(harness.Context, TestContext.Current.CancellationToken);

        Assert.True(harness.Io.Contains("Vybráno 1 disků k porovnání."));
    }

    [Fact]
    public async Task TwoDiskPerformanceComparisonRendersATextPanel()
    {
        var fake = ComparisonFake.Create();
        using var harness = TestHarness.Create(configure: fake.Register);
        harness.Io.EnqueueChoice("add");
        harness.Io.EnqueueChoice("add-1");
        harness.Io.EnqueueChoice("add");
        harness.Io.EnqueueChoice("add-2");
        harness.Io.EnqueueChoice("performance");
        harness.Io.EnqueueChoice("back");

        await new ComparisonScreen().ShowAsync(harness.Context, TestContext.Current.CancellationToken);

        Assert.Equal((1, 2), (fake.PerformanceFirst, fake.PerformanceSecond));
        Assert.True(harness.Io.Contains("Rychlejší disk:"));
        Assert.True(harness.Io.Contains("Rozdíl zápisu:"));
        Assert.True(harness.Io.Contains("Spolehlivější:"));
        Assert.True(harness.Io.Contains("Doporučení:"));
    }

    [Fact]
    public async Task BestDiskActionUsesTheRepositoryRanking()
    {
        var fake = ComparisonFake.Create();
        using var harness = TestHarness.Create(configure: fake.Register);
        harness.Io.EnqueueChoice("best");
        harness.Io.EnqueueChoice("back");

        await new ComparisonScreen().ShowAsync(harness.Context, TestContext.Current.CancellationToken);

        Assert.True(harness.Io.Contains("Nejlepší disk: Samsung SSD 870 EVO (skóre 95)"));
    }

    [Fact]
    public async Task MenuReturnsTheNavigationIntent()
    {
        var fake = ComparisonFake.Create();
        using var harness = TestHarness.Create(configure: fake.Register);
        harness.Io.EnqueueChoice(TuiKeys.MainMenu);

        var result = await new ComparisonScreen().ShowAsync(harness.Context, TestContext.Current.CancellationToken);

        Assert.Equal(ScreenAction.Menu, result.Action);
    }

    [Fact]
    public async Task ComparisonFailureIsReportedInsteadOfCrashing()
    {
        var fake = ComparisonFake.Create(failComparison: true);
        using var harness = TestHarness.Create(configure: fake.Register);
        harness.Io.EnqueueChoice("add");
        harness.Io.EnqueueChoice("add-1");
        harness.Io.EnqueueChoice("add");
        harness.Io.EnqueueChoice("add-2");
        harness.Io.EnqueueChoice("compare");
        harness.Io.EnqueueChoice("back");

        await new ComparisonScreen().ShowAsync(harness.Context, TestContext.Current.CancellationToken);

        Assert.True(harness.Io.Contains("Porovnání selhalo"));
    }

    private sealed class ComparisonFake
    {
        private ComparisonFake(IDiskCardRepository repository, IDiskComparisonService comparison)
        {
            Repository = repository;
            Comparison = comparison;
        }

        public IDiskCardRepository Repository { get; }

        public IDiskComparisonService Comparison { get; }

        public int[] ComparedIds { get; private set; } = Array.Empty<int>();

        public int PerformanceFirst { get; private set; }

        public int PerformanceSecond { get; private set; }

        public void Register(IServiceCollection services)
        {
            services.RemoveAll<IDiskCardRepository>();
            services.AddSingleton(Repository);
            services.RemoveAll<IDiskComparisonService>();
            services.AddSingleton(Comparison);
        }

        public static ComparisonFake Create(bool failComparison = false)
        {
            var cards = new List<DiskCard>
            {
                CreateCard(1, "Samsung SSD 870 EVO", 95, "A"),
                CreateCard(2, "Crucial MX500", 88, "B"),
                CreateCard(3, "WD Blue SA510", 84, "B"),
                CreateCard(4, "Kingston A400", 78, "C"),
                CreateCard(5, "Seagate BarraCuda", 74, "C"),
                CreateCard(6, "Toshiba MG08", 71, "C"),
                new DiskCard
                {
                    Id = 9,
                    ModelName = "Untested NVMe",
                    SerialNumber = "UNKNOWN",
                    OverallGrade = "?",
                    OverallScore = 0,
                    TestCount = 0,
                },
            };

            var repository = Substitute.For<IDiskCardRepository>();
            repository.GetActiveAsync().Returns(cards);
            repository.GetBestDisksAsync(1).Returns(new List<DiskCard> { cards[0] });

            var comparison = Substitute.For<IDiskComparisonService>();
            var fake = new ComparisonFake(repository, comparison);

            comparison.CompareAsync(Arg.Any<List<int>>()).Returns(call =>
            {
                var ids = call.Arg<List<int>>();
                fake.ComparedIds = ids.ToArray();
                if (failComparison)
                {
                    throw new InvalidOperationException("testovací chyba porovnání");
                }

                return BuildResults(cards, ids);
            });

            comparison.ComparePerformanceAsync(Arg.Any<int>(), Arg.Any<int>()).Returns(call =>
            {
                fake.PerformanceFirst = call.ArgAt<int>(0);
                fake.PerformanceSecond = call.ArgAt<int>(1);
                var first = cards.First(card => card.Id == fake.PerformanceFirst);
                var second = cards.First(card => card.Id == fake.PerformanceSecond);
                return new PerformanceComparison
                {
                    Disk1 = first,
                    Disk2 = second,
                    WriteSpeedDifference = 25.5,
                    ReadSpeedDifference = 18.2,
                    SpeedAdvantagePercent = 12,
                    FasterDisk = first.ModelName,
                    MoreReliableDisk = first.ModelName,
                    RecommendedDisk = first.ModelName,
                    Summary = "Oba disky mají srovnatelný výkon. Volba závisí na specifických potřebách.",
                };
            });

            return fake;
        }

        private static DiskCard CreateCard(int id, string model, double score, string grade) => new()
        {
            Id = id,
            ModelName = model,
            SerialNumber = "SN-" + id.ToString(System.Globalization.CultureInfo.InvariantCulture),
            OverallGrade = grade,
            OverallScore = score,
            TestCount = 2,
            Capacity = 500L * 1024 * 1024 * 1024,
            LastTestedAt = new DateTime(2026, 1, 5, 12, 0, 0),
        };

        private static List<DiskComparisonResult> BuildResults(List<DiskCard> cards, List<int> ids)
        {
            var results = ids
                .Select(id => cards.First(card => card.Id == id))
                .OrderByDescending(card => card.OverallScore)
                .Select((card, index) => new DiskComparisonResult
                {
                    Disk = card,
                    Rank = index + 1,
                    Score = card.OverallScore,
                    Grade = card.OverallGrade,
                    AvgWriteSpeed = 420 - (index * 10),
                    AvgReadSpeed = 510 - (index * 10),
                    ErrorCount = index,
                    Recommendation = card.OverallScore >= 90
                        ? "✅ Výborný stav - doporučeno pro všechny účely"
                        : "✅ Dobrý stav - vhodný pro běžné použití",
                })
                .ToList();

            return results;
        }
    }
}
