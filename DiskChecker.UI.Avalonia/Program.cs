using System;
using System.Runtime.InteropServices;
using Avalonia;

namespace DiskChecker.UI.Avalonia;

class Program
{
    [DllImport("libc", EntryPoint = "geteuid")]
    private static extern uint GetEffectiveUserId();

    [STAThread]
    public static void Main(string[] args)
    {
        // Windows uses app.manifest requireAdministrator; Linux must also reject
        // direct non-root launches (desktop entry elevates through polkit).
        if (OperatingSystem.IsLinux() && GetEffectiveUserId() != 0)
        {
            Console.Error.WriteLine("DiskChecker requires root for raw disk access. Run it with sudo or the privileged desktop launcher.");
            Environment.ExitCode = 1;
            return;
        }

        BuildAvaloniaApp()
            .StartWithClassicDesktopLifetime(args);
    }

    public static AppBuilder BuildAvaloniaApp()
        => AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .WithInterFont()
            .LogToTrace();
}