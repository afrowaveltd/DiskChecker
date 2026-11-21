using DiskChecker.UI.Avalonia.Services;
using DiskChecker.UI.Avalonia.Views;
using Xunit;

namespace DiskChecker.Tests;

/// <summary>Regression: input dialog has no ViewModel.L indexer.</summary>
public sealed class InputDialogButtonLabelsTests
{
    [Fact]
    public void PacingDialogUsesLocalizedActionLabels()
    {
        var locale = new LocaleService();
        var dialog = new InputDialogViewModel(null!, "Write pacing", "Seconds", "0", locale);

        Assert.Equal(locale.GetString("Common.OK", "OK"), dialog.OkButtonText);
        Assert.Equal(locale.GetString("Common.Cancel", "Cancel"), dialog.CancelButtonText);
        Assert.False(string.IsNullOrWhiteSpace(dialog.OkButtonText));
        Assert.False(string.IsNullOrWhiteSpace(dialog.CancelButtonText));
        Assert.Equal("0", dialog.InputText);
    }

    [Fact]
    public void EnglishLabelsAreResolvedWhenLocaleChanges()
    {
        var locale = new LocaleService();
        locale.SetLocale("en");
        var dialog = new InputDialogViewModel(null!, "Write pacing", "Seconds", "5", locale);

        Assert.Equal(locale.GetString("Common.OK", "OK"), dialog.OkButtonText);
        Assert.Equal(locale.GetString("Common.Cancel", "Cancel"), dialog.CancelButtonText);
        Assert.Equal("5", dialog.InputText);
    }
}
