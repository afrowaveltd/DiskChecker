using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Layout;
using Avalonia.Media;
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

        public async Task<string?> ShowInputDialogAsync(string title, string message, string defaultValue = "")
        {
            // Create input dialog window
            var inputDialog = new Window
            {
                Title = title,
                Width = 400,
                Height = 200,
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
                CanResize = false
            };

            var textBox = new TextBox
            {
                Text = defaultValue,
                Margin = new Thickness(10),
                Watermark = message
            };

            var okButton = new Button
            {
                Content = "OK",
                Width = 80,
                Margin = new Thickness(5),
                IsDefault = true
            };

            var cancelButton = new Button
            {
                Content = "Zrušit",
                Width = 80,
                Margin = new Thickness(5),
                IsCancel = true
            };

            okButton.Click += (s, e) =>
            {
                inputDialog.Close(textBox.Text);
            };

            cancelButton.Click += (s, e) =>
            {
                inputDialog.Close(null);
            };

            var buttonsPanel = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Center,
                Margin = new Thickness(10),
                Children = { okButton, cancelButton }
            };

            var panel = new StackPanel
            {
                Children =
                {
                    new TextBlock { Text = message, Margin = new Thickness(10), TextWrapping = TextWrapping.Wrap },
                    textBox,
                    buttonsPanel
                }
            };

            inputDialog.Content = panel;

            // Get the main window
            var mainWindow = AvaloniaApp.Current?.ApplicationLifetime is 
                IClassicDesktopStyleApplicationLifetime desktop ?
                desktop.MainWindow : null;

            if (mainWindow != null)
            {
                return await inputDialog.ShowDialog<string?>(mainWindow);
            }
            
            inputDialog.Show();
            return null;
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