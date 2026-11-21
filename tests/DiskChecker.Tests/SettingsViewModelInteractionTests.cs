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
                new LocaleService(), cache, Substitute.For<INavigationService>(), smtp);

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
                Substitute.For<IDiskCacheService>(), Substitute.For<INavigationService>(), smtp);
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

}
