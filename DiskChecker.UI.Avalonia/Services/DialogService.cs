using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using DiskChecker.UI.Avalonia.Views;
using DiskChecker.UI.Avalonia.Services.Interfaces;

// Alias to avoid namespace conflicts
using AvaloniaApp = Avalonia.Application;

namespace DiskChecker.UI.Avalonia.Services
{
    public class DialogService : IDialogService
    {
        public async Task ShowMessageAsync(string title, string message)
        {
            var messageBox = new MessageBoxWindow(title, message, MessageBoxIcon.Information);
            await ShowDialogAsync(messageBox);
        }

        public async Task ShowErrorAsync(string title, string message)
        {
            var messageBox = new MessageBoxWindow(title, message, MessageBoxIcon.Error);
            await ShowDialogAsync(messageBox);
        }

        public async Task<bool> ShowConfirmationAsync(string title, string message)
        {
            var messageBox = new MessageBoxWindow(title, message, MessageBoxIcon.Question);
            var result = await ShowDialogAsync(messageBox);
            return result == MessageBoxResult.Yes;
        }

        private async Task<MessageBoxResult> ShowDialogAsync(MessageBoxWindow messageBox)
        {
            // Get the main window
            var mainWindow = AvaloniaApp.Current?.ApplicationLifetime is 
                IClassicDesktopStyleApplicationLifetime desktop ?
                desktop.MainWindow : null;

            if (mainWindow != null)
            {
                return await messageBox.ShowDialog<MessageBoxResult>(mainWindow);
            }
            
            messageBox.Show();
            return MessageBoxResult.OK;
        }
    }

    public enum MessageBoxIcon
    {
        Information,
        Error,
        Question
    }

    public enum MessageBoxResult
    {
        OK,
        Yes,
        No,
        Cancel
    }
}
