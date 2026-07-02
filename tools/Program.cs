// ScufCalibrate — guided calibration wizard (v2, auto-detect).
//
// You just PRESS each control when prompted; the tool locks onto the change
// automatically (no Enter timing), then waits for release before the next one.
// It ignores the gyro (12-31) AND touch (32-35) regions when hunting for
// buttons/sticks/triggers, so touch drift can't pollute the result.
//
// Prereqs: SCUF in PS/generic mode, remapper app closed, HidHide not hiding it.

using HidSharp;

namespace ScufCalibrate;

internal static class Program
{
    private const int TargetVid = 0x1B1C;
    private static readonly int? TargetPid = 0x3A27; // null = match VID only

    // Regions to ignore when hunting for buttons/sticks/triggers.
    private const int GyroStart = 12, GyroEnd = 12 + 20;   // 12..31 timestamp+IMU
    private const int TouchStart = 32, TouchEnd = 36;      // 32..35 touch active+coords

    private static HidStream _stream = null!;
    private static int _len;
    private static byte[] _baseline = null!;
    private static bool[] _volatile = null!;

    private static int Main()
    {
        Console.OutputEncoding = System.Text.Encoding.UTF8;

        var matches = DeviceList.Local.GetHidDevices()
            .Where(d => d.VendorID == TargetVid && (TargetPid is null || d.ProductID == TargetPid))
            .ToList();

        var dev = matches.FirstOrDefault(HasGamepadUsage)
               ?? matches.FirstOrDefault(d => SafeMaxInput(d) == 64)
               ?? matches.Where(d => SafeMaxInput(d) is >= 32 and < 65)
                         .OrderByDescending(SafeMaxInput).FirstOrDefault();

        if (dev is null) { Console.WriteLine("Gamepad collection not found (PS mode? app closed? HidHide clear?)."); return 1; }
        if (!dev.TryOpen(out _stream)) { Console.WriteLine("Couldn't open device (close Steam Big Picture)."); return 2; }

        using (_stream)
        {
            _stream.ReadTimeout = 300;
            _len = Math.Max(1, SafeMaxInput(dev));
            Console.WriteLine($"Selected: inputLen={_len}  path={dev.DevicePath}");
            Console.WriteLine();
            Console.WriteLine("Learning baseline — DON'T TOUCH the controller for ~2 seconds...");
            LearnBaseline(200);
            Console.WriteLine("Baseline learned.");
            Console.WriteLine();
            Console.WriteLine("Now press each control when asked. It auto-detects — no Enter needed.");
            Console.WriteLine();

            var map = new List<string>();

            Console.WriteLine("=== BUTTONS (press & hold briefly) ===");
            foreach (var name in new[]
            {
                "CROSS (X)", "CIRCLE (O)", "SQUARE", "TRIANGLE",
                "L1", "R1", "L3 (left stick click)", "R3 (right stick click)",
                "OPTIONS", "CREATE / SHARE", "PS BUTTON", "TOUCHPAD CLICK",
            })
                map.Add(DetectButton(name, hat: false));

            Console.WriteLine();
            Console.WriteLine("=== D-PAD (hold each) ===");
            foreach (var dir in new[] { "UP", "RIGHT", "DOWN", "LEFT" })
                map.Add(DetectButton("DPAD " + dir, hat: true));

            Console.WriteLine();
            Console.WriteLine("=== TRIGGERS (squeeze fully) ===");
            map.Add(DetectAxis("L2 trigger"));
            map.Add(DetectAxis("R2 trigger"));

            Console.WriteLine();
            Console.WriteLine("=== STICKS (push to the named extreme & hold) ===");
            map.Add(DetectAxis("LEFT stick RIGHT"));
            map.Add(DetectAxis("LEFT stick DOWN"));
            map.Add(DetectAxis("RIGHT stick RIGHT"));
            map.Add(DetectAxis("RIGHT stick DOWN"));

            Console.WriteLine();
            Console.WriteLine("=== TOUCHPAD SURFACE (hold a finger at each spot) ===");
            foreach (var spot in new[] { "LEFT edge", "RIGHT edge", "TOP", "BOTTOM" })
                map.Add(DetectTouch(spot));

            Console.WriteLine();
            Console.WriteLine("==================== LAYOUT MAP ====================");
            foreach (var line in map) Console.WriteLine(line);
            Console.WriteLine("===================================================");
            Console.WriteLine("Copy everything between the ==== lines and paste it back.");
        }
        return 0;
    }

    private static bool IsCandidate(int i)
        => !_volatile[i] && !(i >= GyroStart && i < GyroEnd) && !(i >= TouchStart && i < TouchEnd);

    private static void LearnBaseline(int samples)
    {
        var min = new byte[_len]; var max = new byte[_len];
        for (int i = 0; i < _len; i++) { min[i] = 255; max[i] = 0; }
        var last = new byte[_len]; var buf = new byte[_len];
        int got = 0; var start = DateTime.UtcNow;
        while (got < samples)
        {
            if (got == 0 && (DateTime.UtcNow - start).TotalSeconds > 3)
            { Console.WriteLine("[!] No reports arriving — wrong collection or pad not streaming. Exiting."); Environment.Exit(3); }
            int n; try { n = _stream.Read(buf, 0, buf.Length); } catch (TimeoutException) { continue; }
            if (n <= 0) continue;
            got++;
            for (int i = 0; i < _len; i++) { if (buf[i] < min[i]) min[i] = buf[i]; if (buf[i] > max[i]) max[i] = buf[i]; last[i] = buf[i]; }
        }
        _baseline = last;
        _volatile = new bool[_len];
        for (int i = 0; i < _len; i++) _volatile[i] = (max[i] - min[i]) > 3;
    }

    // Auto-detect: wait for a candidate byte to change, collect ~15 frames,
    // then report the byte that changed most and its accumulated bit mask.
    private static string DetectButton(string name, bool hat)
    {
        Console.Write($"  {name}: press now... ");
        var counts = new int[_len]; var acc = new byte[_len]; var buf = new byte[_len]; var lastChanged = new byte[_len];
        var start = DateTime.UtcNow; bool saw = false; int after = 0;
        while ((DateTime.UtcNow - start).TotalSeconds < 6 && after < 15)
        {
            int n; try { n = _stream.Read(buf, 0, buf.Length); } catch (TimeoutException) { continue; }
            if (n <= 0) continue;
            bool anyThis = false;
            for (int i = 0; i < _len; i++)
            {
                if (!IsCandidate(i)) continue;
                byte d = (byte)(buf[i] ^ _baseline[i]);
                if (d != 0) { counts[i]++; acc[i] |= d; anyThis = true; }
            }
            if (anyThis) { saw = true; Array.Copy(buf, lastChanged, _len); }
            if (saw) after++;
        }
        if (!saw) { var s = $"  {name,-24} -> (no change — skipped)"; Console.WriteLine("(skipped)"); return s; }

        int bi = 0; for (int i = 1; i < _len; i++) if (counts[i] > counts[bi]) bi = i;
        string res = hat
            ? $"  {name,-24} -> byte {bi}, hat value 0x{lastChanged[bi]:X2} (low nibble {lastChanged[bi] & 0x0F})"
            : $"  {name,-24} -> byte {bi}, bit mask 0x{acc[bi]:X2}";
        Console.WriteLine($"got byte {bi}" + (hat ? $" val 0x{lastChanged[bi]:X2}" : $" mask 0x{acc[bi]:X2}"));
        WaitForRelease();
        return res;
    }

    private static string DetectAxis(string name)
    {
        Console.Write($"  {name}: move fully & hold... ");
        var buf = new byte[_len]; var bestVal = new byte[_len]; Array.Copy(_baseline, bestVal, _len);
        var maxDelta = new int[_len];
        var start = DateTime.UtcNow; bool saw = false; int after = 0;
        while ((DateTime.UtcNow - start).TotalSeconds < 6 && after < 20)
        {
            int n; try { n = _stream.Read(buf, 0, buf.Length); } catch (TimeoutException) { continue; }
            if (n <= 0) continue;
            bool anyThis = false;
            for (int i = 0; i < _len; i++)
            {
                if (!IsCandidate(i)) continue;
                int d = Math.Abs(buf[i] - _baseline[i]);
                if (d > 20) anyThis = true;
                if (d > maxDelta[i]) { maxDelta[i] = d; bestVal[i] = buf[i]; }
            }
            if (anyThis) saw = true;
            if (saw) after++;
        }
        if (!saw) { var s = $"  {name,-24} -> (no movement — skipped)"; Console.WriteLine("(skipped)"); return s; }
        int bi = 0; for (int i = 1; i < _len; i++) if (maxDelta[i] > maxDelta[bi]) bi = i;
        var res = $"  {name,-24} -> byte {bi}: baseline 0x{_baseline[bi]:X2} -> 0x{bestVal[bi]:X2}";
        Console.WriteLine($"got byte {bi}");
        WaitForRelease();
        return res;
    }

    private static string DetectTouch(string spot)
    {
        Console.Write($"  touch {spot}: hold finger there... ");
        var buf = new byte[_len]; var cur = new byte[_len]; Array.Copy(_baseline, cur, _len);
        var start = DateTime.UtcNow; int got = 0;
        // Capture frames where the touch-active byte 32 shows a finger down (high bit clear-ish / value moving).
        while ((DateTime.UtcNow - start).TotalSeconds < 6 && got < 25)
        {
            int n; try { n = _stream.Read(buf, 0, buf.Length); } catch (TimeoutException) { continue; }
            if (n <= 0) continue;
            // Prefer frames where touch coords differ from baseline.
            bool moving = false;
            for (int i = TouchStart; i < TouchEnd; i++) if (buf[i] != _baseline[i]) moving = true;
            if (moving) { Array.Copy(buf, cur, _len); got++; }
        }
        var sb = new System.Text.StringBuilder();
        sb.Append($"  touch {spot,-12} -> ");
        for (int i = TouchStart; i <= Math.Min(_len - 1, TouchEnd); i++) sb.Append($"[{i}]=0x{cur[i]:X2} ");
        var res = sb.ToString();
        Console.WriteLine("captured");
        return res;
    }

    private static void WaitForRelease()
    {
        var buf = new byte[_len]; var start = DateTime.UtcNow;
        while ((DateTime.UtcNow - start).TotalSeconds < 3)
        {
            int n; try { n = _stream.Read(buf, 0, buf.Length); } catch (TimeoutException) { continue; }
            if (n <= 0) continue;
            bool anyHeld = false;
            for (int i = 0; i < _len; i++) if (IsCandidate(i) && buf[i] != _baseline[i]) anyHeld = true;
            if (!anyHeld) return;
        }
    }

    private static int SafeMaxInput(HidDevice d) { try { return d.GetMaxInputReportLength(); } catch { return 0; } }
    private static bool HasGamepadUsage(HidDevice d)
    {
        try
        {
            var rd = d.GetReportDescriptor();
            foreach (var item in rd.DeviceItems)
                foreach (uint u in item.Usages.GetAllValues())
                { ushort page = (ushort)(u >> 16), usage = (ushort)(u & 0xFFFF); if (page == 0x01 && (usage == 0x04 || usage == 0x05)) return true; }
        }
        catch { }
        return false;
    }
}
