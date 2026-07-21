using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Platform.Storage;
using DiskChecker.UI.Avalonia.Views;
using DiskChecker.UI.Avalonia.Services.Interfaces;

// Alias to avoid conflict with DiskChecker.Application namespace
using AvaloniaApp = Avalonia.Application;

namespace DiskChecker.UI.Avalonia.Services;

public class DialogService : IDialogService
{
    private readonly LocaleService _locale;

    public DialogService(LocaleService locale)
    {
        _locale = locale;
    }

    public async Task ShowInfoAsync(string title, string message)
    {
        await ShowDialogAsync(title, message, DialogType.Info);
    }

    public async Task ShowMessageAsync(string title, string message)
    {
        await ShowDialogAsync(title, message, DialogType.Info);
    }

    public async Task ShowSuccessAsync(string title, string message)
    {
        await ShowDialogAsync(title, message, DialogType.Success);
    }

    public async Task ShowWarningAsync(string title, string message)
    {
        await ShowDialogAsync(title, message, DialogType.Warning);
    }

    public async Task ShowErrorAsync(string title, string message)
    {
        await ShowDialogAsync(title, message, DialogType.Error);
    }

    public async Task ShowAlertAsync(string title, string message)
    {
        await ShowDialogAsync(title, message, DialogType.Warning);
    }

    public async Task<bool> ShowConfirmationAsync(string title, string message)
    {
        var result = await ShowDialogAsync(title, message, DialogType.Question);
        return result == MessageBoxResult.Yes || result == MessageBoxResult.OK;
    }

    public async Task<bool> ShowDangerConfirmationAsync(string title, string message)
    {
        var result = await ShowDialogAsync(title, message, DialogType.Danger);
        return result == MessageBoxResult.Yes || result == MessageBoxResult.OK;
    }

    public async Task<string?> ShowPromptAsync(string title, string message, string defaultValue = "")
    {
        return await ShowInputDialogAsync(title, message, defaultValue);
    }

    public async Task<string?> ShowInputDialogAsync(string title, string message, string defaultValue = "")
    {
        var dialog = new InputDialogWindow(title, message, defaultValue, _locale);
        var mainWindow = GetMainWindow();
        
        if (mainWindow != null)
        {
            return await dialog.ShowDialog<string?>(mainWindow);
        }
        
        dialog.Show();
        return null;
    }

    public async Task<IReadOnlyList<string>> PickFoldersAsync(string title, bool allowMultiple = true)
    {
        var mainWindow = GetMainWindow();
        if (mainWindow == null)
            return Array.Empty<string>();

        var result = await mainWindow.StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
        {
            Title = title,
            AllowMultiple = allowMultiple
        });

        return result
            .Select(item => item.Path.LocalPath)
            .Where(path => !string.IsNullOrWhiteSpace(path))
            .ToList();
    }

    public async Task<IReadOnlyList<string>> PickFilesAsync(string title, bool allowMultiple = true)
    {
        var mainWindow = GetMainWindow();
        if (mainWindow == null)
            return Array.Empty<string>();

        var result = await mainWindow.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = title,
            AllowMultiple = allowMultiple
        });

        return result
            .Select(item => item.Path.LocalPath)
            .Where(path => !string.IsNullOrWhiteSpace(path))
            .ToList();
    }


    public async Task<string?> PickSaveFileAsync(string title, string? suggestedFileName = null)
    {
        var mainWindow = GetMainWindow();
        if (mainWindow == null)
            return null;

        var result = await mainWindow.StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = title,
            SuggestedFileName = suggestedFileName
        });

        return result?.Path.LocalPath;
    }

    private async Task<MessageBoxResult> ShowDialogAsync(string title, string message, DialogType type)
    {
        var messageBox = new MessageBoxWindow();
        var viewModel = new MessageBoxViewModel(messageBox, title, message, type, _locale);
        messageBox.DataContext = viewModel;
        
        var mainWindow = GetMainWindow();
        
        if (mainWindow != null)
        {
            return await messageBox.ShowDialog<MessageBoxResult>(mainWindow);
        }
        
        messageBox.Show();
        return MessageBoxResult.OK;
    }

    private static Window? GetMainWindow()
    {
        return AvaloniaApp.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop
            ? desktop.MainWindow
            : null;
    }
}

public enum MessageBoxResult
{
    OK,
    Yes,
    No,
    Cancel
}