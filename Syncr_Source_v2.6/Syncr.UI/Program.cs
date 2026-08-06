using Avalonia;
using Avalonia.LinuxFramebuffer;
using System;
using System.Linq;

namespace Syncr.UI;

class Program
{
    // Initialization code. Don't use any Avalonia, third-party APIs or any
    // SynchronizationContext-reliant code before AppMain is called: things aren't initialized
    // yet and stuff might break.
    [STAThread]
    public static void Main(string[] args)
    {
        AppDomain.CurrentDomain.UnhandledException += (sender, error) =>
        {
            try { System.IO.File.WriteAllText("crash_global.log", error.ExceptionObject.ToString()); } catch { }
        };

        // ─── Single Instance Guard ───────────────────────────────────────────────
        // Prevents double-launch (e.g. systemd service + desktop shortcut both firing)
        // This was causing ~720MB extra RAM usage from the second instance on Pi.
        const string MutexName = "Global\\SyncrEdge_v2";
        using var mutex = new System.Threading.Mutex(true, MutexName, out bool isNewInstance);
        if (!isNewInstance)
        {
            try { System.IO.File.WriteAllText("crash.log", $"[{DateTime.Now:HH:mm:ss}] Second instance launch blocked."); } catch { }
            return;
        }

        try
        {
            var builder = AppBuilder.Configure<App>()
                .WithInterFont()
                .LogToTrace();

            if (args.Contains("--drm"))
            {
                Console.CursorVisible = false;
                builder.StartLinuxDrm(args, null, 1.0);
            }
            else
            {
                if (OperatingSystem.IsLinux())
                {
                    builder.UsePlatformDetect()
                           .StartWithClassicDesktopLifetime(args);
                }
                else
                {
                    builder.UsePlatformDetect()
                           .StartWithClassicDesktopLifetime(args);
                }
            }
        }
        catch (Exception ex)
        {
            try { System.IO.File.WriteAllText("crash.log", ex.ToString()); } catch { }
        }
    }

    // Avalonia configuration, don't remove; also used by visual designer.
    public static AppBuilder BuildAvaloniaApp()
        => AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .WithInterFont()
            .LogToTrace();
}
