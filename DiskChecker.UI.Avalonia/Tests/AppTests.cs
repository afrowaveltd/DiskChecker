using System;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using DiskChecker.UI.Avalonia;
using Xunit;

namespace DiskChecker.UI.Avalonia.Tests
{
    public class AppTests
    {
        [AvaloniaTheory]
        [InlineData("DiskChecker.UI.Avalonia")]
        public void AppBuilder_StartsQuickly(string assemblyName)
        {
            var builder = AppBuilder.Configure<App>()
                .UseHeadlessDesignerPlatform()
                .UseSkia();

            Assert.NotNull(builder);
        }

        [AvaloniaFact]
        public async Task App_ShowsMainWindow()
        {
            using var app = UnitTestApplication.Start(
                AppBuilder.Configure<App>()
                    .UseHeadlessDesignerPlatform()
                    .UseSkia());

            var window = await Dispatcher.UIThread.InvokeAsync(() =>
            {
                var lifetime = (IClassicDesktopStyleApplicationLifetime)Application.Current!.ApplicationLifetime!;
                return lifetime.MainWindow;
            });

            Assert.NotNull(window);
            Assert.Equal("DiskChecker - Diagnóza Disků 🖴", window.Title);
        }
    }
}