// ScufBridge.cs
//
// The resilient core. One background supervisor thread that:
//   * creates the ViGEm client + HidHide session ONCE for the process,
//   * waits for the SCUF (PS mode) to appear, connecting when it does,
//   * runs the HID->DS4 read loop until the pad is removed or errors,
//   * then loops back to waiting — surviving unplug/replug indefinitely.
//
// Nothing here writes to a console; all status goes through the injected
// logger so it works when the app runs hidden.

using HidSharp;

using Nefarius.Drivers.HidHide;
using Nefarius.Utilities.DeviceManagement.PnP;
using Nefarius.ViGEm.Client;
using Nefarius.ViGEm.Client.Targets;
using Nefarius.ViGEm.Client.Targets.DualShock4;

namespace ScufDualSense;

public sealed class ScufBridge : IDisposable
{
    // --- device identity (this specific SCUF model in PS mode) ---------
    private const int Vid = 0x1B1C;
    private const int Pid = 0x3A27;
    private const string DeviceFragment = "VID_1B1C&PID_3A27";

    // Hide the physical Corsair pad. The game ignores it anyway (wrong VID),
    // so this is defensive. Flip false if hiding ever disturbs the reader.
    private const bool EnableHidHide = true;

    // Analog value above which we also assert the digital L2/R2 button bit.
    private const byte TriggerDigitalThreshold = 12;

    private static readonly Guid HidInterface = new("4D1E55B2-F16F-11CF-88CB-001111000030");

    private readonly Action<string> _log;
    private volatile bool _running;
    private Thread? _thread;

    // Reports/second we forward to the virtual DS4 — i.e. the rate the GAME
    // sees at the end of the pipeline. 0 when no controller is connected.
    private volatile int _currentRateHz;
    public int CurrentRateHz => _currentRateHz;

    private ViGEmClient? _client;
    private HidHideControlService? _hidHide;
    private string? _exe;
    private readonly HashSet<string> _blocked = new();

    public ScufBridge(Action<string> log) => _log = log;

    public void Start()
    {
        if (_running) return;
        _running = true;
        _thread = new Thread(Supervise)
        {
            IsBackground = true,
            Name = "ScufBridge",
            Priority = ThreadPriority.AboveNormal,
        };
        _thread.Start();
    }

    public void Stop()
    {
        _running = false;
        _thread?.Join(3000);
    }

    public void Dispose() => Stop();

    // ------------------------------------------------------------------
    private void Supervise()
    {
        try { _client = new ViGEmClient(); }
        catch (Exception ex)
        {
            _log($"[fatal] ViGEmBus unavailable ({ex.Message}). Install the ViGEmBus driver and restart.");
            return;
        }

        if (EnableHidHide)
        {
            try
            {
                _hidHide = new HidHideControlService();
                if (!_hidHide.IsInstalled)
                {
                    _log("[warn] HidHide not installed — running without device hiding.");
                    _hidHide = null;
                }
                else
                {
                    _exe = Environment.ProcessPath;
                    if (_exe is not null) _hidHide.AddApplicationPath(_exe); // whitelist self
                    _hidHide.IsActive = true;
                }
            }
            catch (Exception ex) { _log($"[warn] HidHide setup failed ({ex.Message})."); _hidHide = null; }
        }

        _log("Ready. Waiting for SCUF in PS mode...");
        while (_running)
        {
            var dev = FindDevice();
            if (dev is null) { SleepInterruptible(1000); continue; }

            HideDevice();
            RunOneCycle(dev);
            if (_running) _log("SCUF gone. Waiting for it to return...");
        }

        FinalCleanup();
    }

    private HidDevice? FindDevice()
    {
        try
        {
            var list = DeviceList.Local.GetHidDevices()
                .Where(d => d.VendorID == Vid && d.ProductID == Pid).ToList();
            return list.FirstOrDefault(HasGamepadUsage) ?? list.FirstOrDefault(d => SafeLen(d) == 64);
        }
        catch { return null; }
    }

    private void HideDevice()
    {
        if (_hidHide is null) return;
        try
        {
            foreach (var id in FindAllInstanceIds(DeviceFragment))
                if (_blocked.Add(id)) { _hidHide.AddBlockedInstanceId(id); _log($"Hiding {id}"); }
        }
        catch (Exception ex) { _log($"[warn] hide failed ({ex.Message})."); }
    }

    private void RunOneCycle(HidDevice dev)
    {
        HidStream? stream = null;
        IDualShock4Controller? ds4 = null;
        try
        {
            if (!dev.TryOpen(out stream))
            {
                _log("[warn] couldn't open SCUF (held by another app?). Retrying...");
                SleepInterruptible(1500);
                return;
            }
            int len = Math.Max(ScufReport.MinLength, SafeLen(dev));
            stream.ReadTimeout = 200;

            ds4 = _client!.CreateDualShock4Controller();
            ds4.AutoSubmitReport = false;
            ds4.Connect();
            _log($"Connected. Virtual DS4 online ({len}-byte reports).");

            var buf = new byte[len];
            long windowStart = Environment.TickCount64;
            int windowCount = 0;
            int secondsSinceLog = 0;
            while (_running)
            {
                int n;
                try { n = stream.Read(buf, 0, buf.Length); }
                catch (TimeoutException) { continue; }
                catch { break; } // device removed
                if (n < ScufReport.MinLength) continue;

                ScufState s = ScufReport.Parse(buf);
                Apply(in s, ds4);

                // Throughput: reports forwarded per second. The SCUF streams
                // continuously (gyro never rests), so this reflects the actual
                // delivered poll rate even when you aren't pressing anything.
                windowCount++;
                long now = Environment.TickCount64;
                long elapsed = now - windowStart;
                if (elapsed >= 1000)
                {
                    _currentRateHz = (int)(windowCount * 1000L / elapsed);
                    windowCount = 0;
                    windowStart = now;
                    if (++secondsSinceLog >= 30)   // quiet heartbeat, once per 30s
                    {
                        _log($"Throughput ~{_currentRateHz} Hz to virtual DS4.");
                        secondsSinceLog = 0;
                    }
                }
            }
        }
        catch (Exception ex) { _log($"[warn] cycle error ({ex.Message})."); }
        finally
        {
            _currentRateHz = 0;
            try { ds4?.Disconnect(); } catch { }
            try { stream?.Dispose(); } catch { }
        }
    }

    private void FinalCleanup()
    {
        _log("Shutting down...");
        if (_hidHide is not null)
        {
            foreach (var id in _blocked) { try { _hidHide.RemoveBlockedInstanceId(id); } catch { } }
            if (_exe is not null) { try { _hidHide.RemoveApplicationPath(_exe); } catch { } }
            try { _hidHide.IsActive = false; } catch { }
        }
        try { _client?.Dispose(); } catch { }
        _log("Clean.");
    }

    // ------------------------------------------------------------------
    //  Mapping (SCUF DS4-format -> ViGEm DS4). Direct field copy.
    // ------------------------------------------------------------------
    private static void Apply(in ScufState s, IDualShock4Controller ds4)
    {
        ds4.SetAxisValue(DualShock4Axis.LeftThumbX, s.LX);
        ds4.SetAxisValue(DualShock4Axis.LeftThumbY, s.LY);
        ds4.SetAxisValue(DualShock4Axis.RightThumbX, s.RX);
        ds4.SetAxisValue(DualShock4Axis.RightThumbY, s.RY);

        ds4.SetSliderValue(DualShock4Slider.LeftTrigger, s.L2);
        ds4.SetSliderValue(DualShock4Slider.RightTrigger, s.R2);

        // Some games (e.g. Arc Raiders ADS on L2) read the DIGITAL trigger
        // BUTTON, not the analog axis. Derive those bits from the analog value
        // so aim/fire register no matter which the game binds to.
        ds4.SetButtonState(DualShock4Button.TriggerLeft, s.L2 > TriggerDigitalThreshold);
        ds4.SetButtonState(DualShock4Button.TriggerRight, s.R2 > TriggerDigitalThreshold);

        ds4.SetButtonState(DualShock4Button.Cross, s.Cross);
        ds4.SetButtonState(DualShock4Button.Circle, s.Circle);
        ds4.SetButtonState(DualShock4Button.Square, s.Square);
        ds4.SetButtonState(DualShock4Button.Triangle, s.Triangle);

        ds4.SetButtonState(DualShock4Button.ShoulderLeft, s.L1);
        ds4.SetButtonState(DualShock4Button.ShoulderRight, s.R1);
        ds4.SetButtonState(DualShock4Button.ThumbLeft, s.L3);
        ds4.SetButtonState(DualShock4Button.ThumbRight, s.R3);
        ds4.SetButtonState(DualShock4Button.Share, s.Share);
        ds4.SetButtonState(DualShock4Button.Options, s.Options);

        ds4.SetButtonState(DualShock4SpecialButton.Ps, s.Ps);
        ds4.SetButtonState(DualShock4SpecialButton.Touchpad, s.TouchpadClick);

        ds4.SetDPadDirection(HatToDpad(s.Hat));
        ds4.SubmitReport();
    }

    private static DualShock4DPadDirection HatToDpad(byte h) => h switch
    {
        0 => DualShock4DPadDirection.North,
        1 => DualShock4DPadDirection.Northeast,
        2 => DualShock4DPadDirection.East,
        3 => DualShock4DPadDirection.Southeast,
        4 => DualShock4DPadDirection.South,
        5 => DualShock4DPadDirection.Southwest,
        6 => DualShock4DPadDirection.West,
        7 => DualShock4DPadDirection.Northwest,
        _ => DualShock4DPadDirection.None,
    };

    // ------------------------------------------------------------------
    private static bool HasGamepadUsage(HidDevice d)
    {
        try
        {
            var rd = d.GetReportDescriptor();
            foreach (var item in rd.DeviceItems)
                foreach (uint u in item.Usages.GetAllValues())
                {
                    ushort page = (ushort)(u >> 16), usage = (ushort)(u & 0xFFFF);
                    if (page == 0x01 && (usage == 0x04 || usage == 0x05)) return true;
                }
        }
        catch { }
        return false;
    }

    private static int SafeLen(HidDevice d) { try { return d.GetMaxInputReportLength(); } catch { return 0; } }

    private static List<string> FindAllInstanceIds(string fragment)
    {
        var list = new List<string>();
        int i = 0;
        while (Devcon.FindByInterfaceGuid(HidInterface, out _, out string id, i, true))
        {
            if (id.Contains(fragment, StringComparison.OrdinalIgnoreCase)) list.Add(id);
            i++;
        }
        return list;
    }

    private void SleepInterruptible(int ms)
    {
        const int step = 100;
        for (int e = 0; e < ms && _running; e += step) Thread.Sleep(step);
    }
}
