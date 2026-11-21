using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.VisualTree;
using DiskChecker.Application.Services;
using DiskChecker.Core.Interfaces;
using DiskChecker.Core.Models;
using DiskChecker.UI.Avalonia.Services;
using DiskChecker.UI.Avalonia.Services.Interfaces;
using DiskChecker.UI.Avalonia.ViewModels;
using DiskChecker.UI.Avalonia.Views;
using NSubstitute;
using Xunit;

namespace DiskChecker.Tests;

[Collection("Seek UI")]
public sealed class SettingsViewBindingTests(SeekUiFixture fixture)
{
    [Fact]
    public Task SmtpAndBackupSelectionBindToRealViewModelProperties()
        => fixture.Session.Dispatch(async () =>
        {
            string folder = Directory.CreateTempSubdirectory("dc-ui-smoke-").FullName;
            try
            {
                var settings = new SettingsService(folder);
                var dialog = Substitute.For<IDialogService>();
                var backup = Substitute.For<IBackupService>();
                var item = new IBackupService.BackupInfo
                {
                    FileName = "validated.zip", FilePath = Path.Combine(folder, "validated.zip")
                };
                backup.GetAvailableBackupsAsync()
                    .Returns(Task.FromResult<IEnumerable<IBackupService.BackupInfo>>(new[] { item }));
                var smtp = Substitute.For<IEmailSettingsService>();
                smtp.GetAsync(Arg.Any<CancellationToken>()).Returns(Task.FromResult(new EmailSettings
                {
                    Host = "smtp.example.net", FromAddress = "sender@example.net"
                }));
                var vm = new SettingsViewModel(settings, dialog, backup, new LocaleService(),
                    Substitute.For<IDiskCacheService>(), Substitute.For<INavigationService>(),
                    smtp, Substitute.For<IEmailSender>());

                var view = new SettingsView { DataContext = vm };
                var window = new Window { Width = 1200, Height = 950, Content = view };
                try
                {
                    window.Show();
                    window.UpdateLayout();
                    await Task.Delay(30);

                    var inputs = window.GetVisualDescendants().OfType<TextBox>().ToList();
                    var host = Assert.Single(inputs, c => c.Name == "SmtpHostInput");
                    Assert.Equal("smtp.example.net", host.Text);
                    vm.SmtpHost = "mail.example.net";
                    Assert.Equal("mail.example.net", host.Text);
                    var secret = Assert.Single(inputs, c => c.Name == "SmtpPasswordInput");
                    Assert.Equal(string.Empty, secret.Text);
                    Assert.Equal('●', secret.PasswordChar);

                    var certificatePathInput = Assert.Single(inputs, c => c.Name == "CertificatePathInput");
                    vm.CertificatePath = folder;
                    Assert.Equal(folder, certificatePathInput.Text);
                    Assert.NotNull(vm.BrowseCertificatePathCommand);

                    var backupPicker = Assert.Single(window.GetVisualDescendants().OfType<ComboBox>(),
                        c => c.Name == "BackupSelection");
                    Assert.Single(vm.AvailableBackups);
                    backupPicker.SelectedItem = vm.AvailableBackups[0];
                    Assert.Same(vm.AvailableBackups[0], vm.SelectedBackup);
                    Assert.True(vm.RestoreBackupCommand.CanExecute(null));
                }
                finally { window.Close(); }
            }
            finally
            {
                if (Directory.Exists(folder)) Directory.Delete(folder, recursive: true);
            }
            return true;
        }, CancellationToken.None);
}
