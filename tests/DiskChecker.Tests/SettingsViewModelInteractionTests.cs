using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using DiskChecker.Application.Services;
using DiskChecker.Core.Interfaces;
using DiskChecker.Core.Models;
using System.Threading;
using DiskChecker.UI.Avalonia.Services;
using DiskChecker.UI.Avalonia.Services.Interfaces;
using DiskChecker.UI.Avalonia.ViewModels;
using NSubstitute;
using Xunit;

namespace DiskChecker.Tests;

public sealed class SettingsViewModelInteractionTests
{
    [Fact]
    public async Task FolderPickerCancellationKeepsOriginalPathAndCacheClearIsNonDestructive()
    {
        string directory = Directory.CreateTempSubdirectory("dc-settings-ui-").FullName;
        try
        {
            var settings = new SettingsService(directory);
            var dialog = Substitute.For<IDialogService>();
            dialog.PickFoldersAsync(Arg.Any<string>(), false)
                .Returns(Task.FromResult<IReadOnlyList<string>>(Array.Empty<string>()));
            var backup = Substitute.For<IBackupService>();
            backup.GetAvailableBackupsAsync()
                .Returns(Task.FromResult<IEnumerable<IBackupService.BackupInfo>>(Array.Empty<IBackupService.BackupInfo>()));
            var cache = Substitute.For<IDiskCacheService>();
            var smtp = Substitute.For<IEmailSettingsService>();
            smtp.GetAsync(Arg.Any<CancellationToken>())
                .Returns(Task.FromResult(new EmailSettings { IncludeCertificateAttachment = true }));
            var vm = new SettingsViewModel(settings, dialog, backup,
                new LocaleService(), cache, Substitute.For<INavigationService>(), smtp, Substitute.For<IEmailSender>());

            vm.DefaultExportPath = "original";
            await vm.BrowseExportPathCommand.ExecuteAsync(null);
            Assert.Equal("original", vm.DefaultExportPath);

            dialog.PickFoldersAsync(Arg.Any<string>(), false)
                .Returns(Task.FromResult<IReadOnlyList<string>>(new[] { directory }));
            await vm.BrowseExportPathCommand.ExecuteAsync(null);
            Assert.Equal(directory, vm.DefaultExportPath);

            vm.ClearCacheCommand.Execute(null);
            cache.Received(1).ClearCache();
            vm.EmailIncludeCertificateAttachment = false;
            await vm.SaveSettingsCommand.ExecuteAsync(null);
            await smtp.Received(1).SaveAsync(
                Arg.Is<EmailSettings>(s => !s.IncludeCertificateAttachment),
                Arg.Any<CancellationToken>());
            Assert.True(File.Exists(settings.SettingsFilePath));

            vm.IsSaving = true;
            Assert.True(vm.IsNavigationBlocked);
            vm.IsSaving = false;
            Assert.False(vm.IsNavigationBlocked);
        }
        finally
        {
            if (Directory.Exists(directory))
                Directory.Delete(directory, recursive: true);
        }
    }
    [Fact]
    public async Task UnavailableSmtpStoreDisablesAttachmentChoiceButAllowsOtherSettings()
    {
        string directory = Directory.CreateTempSubdirectory("dc-settings-smtp-").FullName;
        try
        {
            var settings = new SettingsService(directory);
            var dialog = Substitute.For<IDialogService>();
            var backup = Substitute.For<IBackupService>();
            backup.GetAvailableBackupsAsync().Returns(Task.FromResult<IEnumerable<IBackupService.BackupInfo>>(
                Array.Empty<IBackupService.BackupInfo>()));
            var smtp = Substitute.For<IEmailSettingsService>();
            smtp.GetAsync(Arg.Any<CancellationToken>()).Returns(
                Task.FromException<EmailSettings>(new InvalidOperationException("SMTP database unavailable")));
            var vm = new SettingsViewModel(settings, dialog, backup, new LocaleService(),
                Substitute.For<IDiskCacheService>(), Substitute.For<INavigationService>(), smtp, Substitute.For<IEmailSender>());
            Assert.False(vm.IsEmailAttachmentAvailable);

            vm.Language = "en";
            await vm.SaveSettingsCommand.ExecuteAsync(null);
            Assert.Equal("en", await settings.GetLanguageAsync());
            await smtp.DidNotReceiveWithAnyArgs().SaveAsync(default!, TestContext.Current.CancellationToken);
        }
        finally
        {
            if (Directory.Exists(directory)) Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task SmtpConfigurationStoresChangesWithoutExposingStoredPassword()
    {
        string directory = Directory.CreateTempSubdirectory("dc-smtp-ui-").FullName;
        try
        {
            var settings = new SettingsService(directory);
            var dialog = Substitute.For<IDialogService>();
            var backup = Substitute.For<IBackupService>();
            backup.GetAvailableBackupsAsync().Returns(
                Task.FromResult<IEnumerable<IBackupService.BackupInfo>>(Array.Empty<IBackupService.BackupInfo>()));
            var email = new EmailSettings
            {
                Host = "old.example.net", Port = 465, UserName = "old",
                Password = "previous-password", FromAddress = "old@example.net"
            };
            var smtp = Substitute.For<IEmailSettingsService>();
            smtp.GetAsync(Arg.Any<CancellationToken>()).Returns(Task.FromResult(email));
            var sender = Substitute.For<IEmailSender>();
            var vm = new SettingsViewModel(settings, dialog, backup, new LocaleService(),
                Substitute.For<IDiskCacheService>(), Substitute.For<INavigationService>(), smtp, sender);

            Assert.Equal(string.Empty, vm.SmtpPassword);
            Assert.True(vm.HasStoredSmtpPassword);
            vm.SmtpHost = "smtp.example.net";
            vm.SmtpPort = 587;
            vm.SmtpUseTls = true;
            vm.SmtpUserName = "operator";
            vm.SmtpFromName = "DiskChecker";
            vm.SmtpFromAddress = "sender@example.net";
            vm.SmtpPassword = "new-app-password";
            vm.ReportRecipientEmail = "tech@example.net";
            await vm.TestSmtpCommand.ExecuteAsync(null);

            Assert.Equal("smtp.example.net", email.Host);
            Assert.Equal(587, email.Port);
            Assert.Equal("new-app-password", email.Password);
            Assert.Equal(string.Empty, vm.SmtpPassword);
            await smtp.Received().SaveAsync(Arg.Any<EmailSettings>(), Arg.Any<CancellationToken>());
            await sender.Received(1).SendAsync(
                Arg.Is<EmailMessage>(message => message.ToAddress == "tech@example.net"),
                Arg.Any<CancellationToken>());

            vm.ClearStoredSmtpPassword = true;
            await vm.SaveSettingsCommand.ExecuteAsync(null);
            Assert.Equal(string.Empty, email.Password);
        }
        finally
        {
            if (Directory.Exists(directory)) Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task RestoreRequiresExplicitBackupSelection()
    {
        string directory = Directory.CreateTempSubdirectory("dc-backup-select-").FullName;
        try
        {
            var settings = new SettingsService(directory);
            var backup = Substitute.For<IBackupService>();
            backup.GetAvailableBackupsAsync().Returns(Task.FromResult<IEnumerable<IBackupService.BackupInfo>>(
                new[] { new IBackupService.BackupInfo { FileName = "backup.zip", FilePath = "/tmp/backup.zip" } }));
            var smtp = Substitute.For<IEmailSettingsService>();
            smtp.GetAsync(Arg.Any<CancellationToken>()).Returns(Task.FromResult(new EmailSettings()));
            var vm = new SettingsViewModel(settings, Substitute.For<IDialogService>(), backup, new LocaleService(),
                Substitute.For<IDiskCacheService>(), Substitute.For<INavigationService>(), smtp,
                Substitute.For<IEmailSender>());
            Assert.False(vm.RestoreBackupCommand.CanExecute(null));
            vm.SelectedBackup = new IBackupService.BackupInfo { FileName = "backup.zip", FilePath = "/tmp/backup.zip" };
            Assert.True(vm.RestoreBackupCommand.CanExecute(null));
            vm.SelectedBackup = null;
            Assert.False(vm.RestoreBackupCommand.CanExecute(null));
        }
        finally
        {
            if (Directory.Exists(directory)) Directory.Delete(directory, recursive: true);
        }
    }

}
