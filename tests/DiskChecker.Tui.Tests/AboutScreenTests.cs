using System.Text.Json;
using DiskChecker.Application.Services;
using DiskChecker.Core.Interfaces;
using DiskChecker.TUI.Screens;
using DiskChecker.TUI.Services;
using DiskChecker.Tui.Tests.Support;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using NSubstitute;
using Xunit;

namespace DiskChecker.Tui.Tests;

public sealed class AboutScreenTests : IDisposable
{
    private static readonly JsonSerializerOptions SettingsJsonOptions = new() { WriteIndented = true };

    private readonly string _settingsDirectory =
        Path.Combine(Path.GetTempPath(), "dcheck-about", Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_settingsDirectory))
            {
                Directory.Delete(_settingsDirectory, recursive: true);
            }
        }
        catch (IOException)
        {
            // Temporary directories are best effort cleanup.
        }
    }

    [Fact]
    public async Task RendersIdentityCapabilitiesAndDestructiveWarning()
    {
        var settings = CreateSettingsService();
        var (languageSettings, written) = CreateLanguageSettings();
        using var harness = TestHarness.Create(configure: services => Register(services, settings, languageSettings));
        harness.Io.EnqueueChoice("back");

        var result = await new AboutScreen().ShowAsync(harness.Context, TestContext.Current.CancellationToken);

        Assert.Equal(ScreenAction.Back, result.Action);
        Assert.Equal(ScreenIds.About, new AboutScreen().Id);
        Assert.Equal("Nav.About", new AboutScreen().TitleKey);
        Assert.True(harness.Io.Contains("DiskChecker"));
        Assert.True(harness.Io.Contains("© 2024-2026 DiskChecker Project"));
        Assert.True(harness.Io.Contains(harness.Context.SystemInfo.AppVersion));
        Assert.True(harness.Io.Contains("Detekce disků a načtení parametrů"));
        Assert.True(harness.Io.Contains("SMART monitoring a historie atributů"));
        Assert.True(harness.Io.Contains("Destruktivní testy a sanitizace"));
        Assert.True(harness.Io.Contains("Certifikáty a export do PDF"));
        Assert.True(harness.Io.Contains("Záloha a obnova disku"));
        Assert.True(harness.Io.Contains("destruktivní testy"));
        Assert.True(harness.Io.Contains("Podporované platformy"));
        Assert.True(harness.Io.Contains(harness.Context.SystemInfo.OsName));
        Assert.True(harness.Io.Contains(harness.Context.SystemInfo.RuntimeInfo));
        Assert.Empty(written);
    }

    [Fact]
    public async Task ShowsDatabaseProviderAndLocationWithoutCredentials()
    {
        var settings = CreateSettingsService(
            "Host=db.internal;Database=diskcards;Username=diskadmin;Password=SuperSecret123");
        var (languageSettings, _) = CreateLanguageSettings();
        using var harness = TestHarness.Create(configure: services => Register(services, settings, languageSettings));
        harness.Io.EnqueueChoice("back");

        await new AboutScreen().ShowAsync(harness.Context, TestContext.Current.CancellationToken);

        Assert.True(harness.Io.Contains("PostgreSQL"));
        Assert.True(harness.Io.Contains("Host=db.internal"));
        Assert.True(harness.Io.Contains("Password=***"));
        Assert.True(harness.Io.Contains("Username=***"));
        Assert.True(harness.Io.Contains("settings.json"));
        Assert.False(harness.Io.Contains("SuperSecret123"));
        Assert.False(harness.Io.Contains("diskadmin"));
    }

    [Fact]
    public async Task SqliteDatabaseShowsTheDatabaseFileLocation()
    {
        var settings = CreateSettingsService();
        var (languageSettings, _) = CreateLanguageSettings();
        using var harness = TestHarness.Create(configure: services => Register(services, settings, languageSettings));
        harness.Io.EnqueueChoice("back");

        await new AboutScreen().ShowAsync(harness.Context, TestContext.Current.CancellationToken);

        Assert.True(harness.Io.Contains("SQLite"));
        Assert.True(harness.Io.Contains("DiskChecker.db"));
        Assert.False(harness.Io.Contains("Password=***"));
    }

    [Fact]
    public async Task SwitchingLanguageWritesThroughSettingsAndAppliesTheLocale()
    {
        var settings = CreateSettingsService();
        var (languageSettings, written) = CreateLanguageSettings();
        using var harness = TestHarness.Create(configure: services => Register(services, settings, languageSettings));
        harness.Io.EnqueueChoice("language");
        harness.Io.EnqueueChoice("language-en");
        harness.Io.EnqueueChoice("back");

        await new AboutScreen().ShowAsync(harness.Context, TestContext.Current.CancellationToken);

        Assert.Equal(new[] { "en" }, written);
        Assert.Equal("en", harness.Locale.CurrentLocale);
        Assert.True(harness.Io.Contains("Language changed to English."));
        Assert.True(harness.Io.Contains("Application version"));
    }

    [Fact]
    public async Task RefreshActionReportsTheRefreshedInformation()
    {
        var settings = CreateSettingsService();
        var (languageSettings, _) = CreateLanguageSettings();
        using var harness = TestHarness.Create(configure: services => Register(services, settings, languageSettings));
        harness.Io.EnqueueChoice("refresh");
        harness.Io.EnqueueChoice("back");

        await new AboutScreen().ShowAsync(harness.Context, TestContext.Current.CancellationToken);

        Assert.True(harness.Io.Contains("Informace obnoveny."));
    }

    [Fact]
    public async Task MenuReturnsTheNavigationIntent()
    {
        var settings = CreateSettingsService();
        var (languageSettings, _) = CreateLanguageSettings();
        using var harness = TestHarness.Create(configure: services => Register(services, settings, languageSettings));
        harness.Io.EnqueueChoice(TuiKeys.MainMenu);

        var result = await new AboutScreen().ShowAsync(harness.Context, TestContext.Current.CancellationToken);

        Assert.Equal(ScreenAction.Menu, result.Action);
    }

    private static void Register(IServiceCollection services, SettingsService settings, ISettingsService languageSettings)
    {
        services.RemoveAll<SettingsService>();
        services.AddSingleton(settings);
        services.RemoveAll<ISettingsService>();
        services.AddSingleton(languageSettings);
    }

    private static (ISettingsService Service, List<string> Written) CreateLanguageSettings()
    {
        var written = new List<string>();
        var substitute = Substitute.For<ISettingsService>();
        substitute.SetLanguageAsync(Arg.Any<string>()).Returns(Task.CompletedTask);
        substitute.When(service => service.SetLanguageAsync(Arg.Any<string>()))
            .Do(call => written.Add(call.Arg<string>()));
        return (substitute, written);
    }

    private SettingsService CreateSettingsService(string? connectionString = null)
    {
        Directory.CreateDirectory(_settingsDirectory);
        string provider = connectionString is null ? "Sqlite" : "PostgreSql";
        string connection = connectionString ?? "Data Source=" + Path.Combine(_settingsDirectory, "DiskChecker.db");
        var payload = new Dictionary<string, string>
        {
            ["DatabaseProvider"] = provider,
            ["DatabaseConnectionString"] = connection,
        };

        File.WriteAllText(
            Path.Combine(_settingsDirectory, "settings.json"),
            JsonSerializer.Serialize(payload, SettingsJsonOptions));

        return new SettingsService(_settingsDirectory);
    }
}
