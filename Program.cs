// Program.cs
//
// Tray-app entry point. Runs the ScufBridge supervisor in the background and
// shows a system-tray icon whose menu lets you open the log folder or quit
// cleanly (important, since this runs with no console window).
//
// Logs to  %LOCALAPPDATA%\ScufDualSense\scuf.log  (rotated at ~1 MB).

using System.Diagnostics;
using System.Drawing;
using System.Windows.Forms;

namespace ScufDualSense;

internal static class Program
{
    private static readonly object _logLock = new();
    private static string _logDir = "";
    private static string _logPath = "";

    [STAThread]
    private static void Main()
    {
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);

        SetupLogging();
        Log("=== ScufDualSense starting ===");

        var bridge = new ScufBridge(Log);
        bridge.Start();

        using var tray = new NotifyIcon
        {
            Icon = LoadAppIcon(),
            Text = "SCUF -> DualSense bridge",
            Visible = true,
        };

        var menu = new ContextMenuStrip();
        menu.Items.Add("Open log folder", null, (_, _) =>
        {
            try { Process.Start("explorer.exe", _logDir); } catch { }
        });
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("Exit", null, (_, _) =>
        {
            bridge.Stop();
            tray.Visible = false;
            Application.Exit();
        });
        tray.ContextMenuStrip = menu;

        tray.BalloonTipTitle = "SCUF DualSense bridge";
        tray.BalloonTipText = "Running. Put your SCUF in PS mode.";
        tray.ShowBalloonTip(3000);

        // Live throughput on the tray tooltip (hover to see it). Ticks on the
        // UI thread, so updating tray.Text here is safe.
        using var rateTimer = new System.Windows.Forms.Timer { Interval = 1000 };
        rateTimer.Tick += (_, _) =>
        {
            int hz = bridge.CurrentRateHz;
            tray.Text = hz > 0 ? $"SCUF -> DS4  (~{hz} Hz)" : "SCUF -> DS4  (waiting for pad)";
        };
        rateTimer.Start();

        Application.Run();          // pumps the tray; blocks until Application.Exit()

        bridge.Dispose();
        Log("=== stopped ===");
    }

    // ------------------------------------------------------------------
    // Loads the app icon for the tray. Prefers the icon embedded in the
    // running exe (works with single-file publish), falls back to a loose
    // app.ico next to the exe, then to the generic system icon.
    private static Icon LoadAppIcon()
    {
        try
        {
            var exe = Environment.ProcessPath;
            if (exe is not null)
            {
                var embedded = Icon.ExtractAssociatedIcon(exe);
                if (embedded is not null) return embedded;
            }
        }
        catch { /* fall through */ }

        try
        {
            var dir = AppContext.BaseDirectory;
            var icoPath = Path.Combine(dir, "app.ico");
            if (File.Exists(icoPath)) return new Icon(icoPath);
        }
        catch { /* fall through */ }

        return SystemIcons.Application;
    }

    // ------------------------------------------------------------------
    private static void SetupLogging()
    {
        _logDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "ScufDualSense");
        Directory.CreateDirectory(_logDir);
        _logPath = Path.Combine(_logDir, "scuf.log");
    }

    private static void Log(string msg)
    {
        lock (_logLock)
        {
            try
            {
                // Rotate at ~1 MB so the log can't grow forever.
                var fi = new FileInfo(_logPath);
                if (fi.Exists && fi.Length > 1_000_000)
                {
                    var old = _logPath + ".old";
                    if (File.Exists(old)) File.Delete(old);
                    File.Move(_logPath, old);
                }
                File.AppendAllText(_logPath, $"{DateTime.Now:yyyy-MM-dd HH:mm:ss}  {msg}{Environment.NewLine}");
            }
            catch { /* logging must never crash the app */ }
        }
    }
}
