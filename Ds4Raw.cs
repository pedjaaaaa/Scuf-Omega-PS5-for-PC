// Ds4Raw.cs
//
// Builds the 63-byte DS4 "extended" input report that ViGEmBus accepts via
// IDualShock4Controller.SubmitRawReport(). The plain SetButtonState/SetAxisValue
// path only exposes the 9-byte DS4_REPORT, which has no room for motion data --
// gyro, accelerometer, sensor timestamp and touchpad coordinates only exist in
// the extended report.
//
// Layout references:
//   * output side  - DS4_REPORT_EX (63 bytes, report ID stripped; ViGEmBus
//                    prepends the 0x01 itself).
//   * input side   - the SCUF's PS-mode report is DualSense-format for sticks
//                    1-4, triggers 5-6, buttons 8-10, gyro 16-21, accel 22-27
//                    and sensor timestamp 28-31. The touch block is the one
//                    place it deviates: the two fingers occupy 32-39, a byte
//                    earlier than a real DualSense puts them. Established by
//                    dumping the report while dragging a finger, not assumed.
//
// The two devices use the same raw units, axis order and sign convention for
// the six motion axes (pitch, yaw, roll / X, Y, Z as little-endian int16), so
// the twelve motion bytes are a straight block copy -- no scaling, no flipping.

namespace ScufDualSense;

// How touchpad surface coordinates are forwarded to the virtual DS4.
public enum TouchMode
{
    // Click only; both fingers always report as lifted.
    Off,
    // Pass the 12-bit X/Y through untouched.
    Raw,
    // Pass X through, squeeze Y from the DualSense range into the DS4's.
    RescaleY,
}

public static class Ds4Raw
{
    // ---- SCUF (DualSense-format) input report offsets ----------------
    private const int IN_LX = 1;              // 1-4  sticks
    private const int IN_L2 = 5, IN_R2 = 6;   // analog triggers
    private const int IN_SEQ = 7;             // rolling sequence counter
    private const int IN_FACE_HAT = 8;        // hat (low nibble) + face buttons
    private const int IN_BTN2 = 9;            // L1/R1/L2/R2/Share/Options/L3/R3
    private const int IN_BTN3 = 10;           // PS / touchpad click / mute
    private const int IN_MOTION = 16;         // 16-21 gyro, 22-27 accel
    private const int IN_SENSOR_TS = 28;      // uint32, 0.33 us ticks

    // Offset of finger 1's tracking byte; finger 2 follows four bytes later, so
    // the pair occupies 32-39. A real DualSense puts this block at 33 — this pad
    // sits one byte earlier, the single place its report deviates from the
    // DualSense layout. Byte 32 carries the tracking id in bits 0-6 and the
    // lift flag in bit 7, and decoding from here yields a clean 0..1919 by
    // 0..1079 sweep. Porting to another pad? TouchProbe reports which bytes
    // actually move while a finger drags.
    public const int IN_TOUCH = 32;

    // Report must be at least this long to contain gyro + accel.
    public const int MotionMinLength = 28;
    // Report must be at least this long to contain both finger blocks. A DS4's
    // touch packet counter would sit at IN_TOUCH + 8, but this pad leaves that
    // byte permanently zero, so it is neither read nor required here.
    public const int TouchMinLength = IN_TOUCH + 8;

    // ---- DS4_REPORT_EX output offsets --------------------------------
    private const int EX_LX = 0;              // 0-3  sticks
    private const int EX_BTN1 = 4;            // hat + face
    private const int EX_BTN2 = 5;            // shoulders/triggers/menu/sticks
    private const int EX_SPECIAL = 6;         // PS | touchpad | counter << 2
    private const int EX_L2 = 7, EX_R2 = 8;
    private const int EX_TIMESTAMP = 9;       // uint16, 5.33 us ticks
    private const int EX_BATTERY = 11;
    private const int EX_MOTION = 12;         // 12-17 gyro, 18-23 accel
    private const int EX_BATTERY_SPECIAL = 29;
    private const int EX_TOUCH_PACKETS = 32;  // number of valid touch packets
    private const int EX_TOUCH_COUNTER = 33;
    private const int EX_TOUCH = 34;          // 34-41 two fingers, 4 bytes each

    /// <summary>Size ViGEm requires for SubmitRawReport.</summary>
    public const int ExLength = 63;

    private const byte M_L2 = 0x04, M_R2 = 0x08;
    private const byte FINGER_UP = 0x80;      // bit 7 set = finger not touching

    // Both pads report X across 0..1919, but the DS4's surface is shorter than
    // the DualSense's, so the Y ranges differ. A game that expects DS4 numbers
    // will see the bottom of a DualSense-range pad fall off the end.
    // Measured on this SCUF: X 0..1919, Y 0..1079 — DualSense-height, so
    // TouchMode.RescaleY is the correct setting rather than Raw.
    private const int Ds4TouchYMax = 942;
    private const int DualSenseTouchYMax = 1080;

    // Translate one SCUF report into a DS4 extended report, in place.
    // <paramref name="ex"/> must be <see cref="ExLength"/> bytes.
	
    // <param name="r">raw SCUF report (report ID at index 0)</param>
    // <param name="n">bytes actually read into <paramref name="r"/></param>
    // <param name="ex">destination buffer, reused between calls</param>
    // <param name="triggerThreshold">analog value above which the digital L2/R2 bit is asserted</param>
    // <param name="bias">optional gyro drift compensation, may be null</param>
    // <param name="touch">how to forward touchpad surface coordinates</param>
    public static void Build(ReadOnlySpan<byte> r, int n, byte[] ex,
                             byte triggerThreshold, GyroBias? bias, TouchMode touch)
    {
        Array.Clear(ex, 0, ExLength);

        // --- sticks, triggers -----------------------------------------
        ex[EX_LX + 0] = r[IN_LX + 0];
        ex[EX_LX + 1] = r[IN_LX + 1];
        ex[EX_LX + 2] = r[IN_LX + 2];
        ex[EX_LX + 3] = r[IN_LX + 3];
        ex[EX_L2] = r[IN_L2];
        ex[EX_R2] = r[IN_R2];

        // --- buttons ---------------------------------------------------
        // The SCUF's byte 8/9 bit layout is identical to DS4 byte 5/6, so
        // these copy straight across.
        ex[EX_BTN1] = r[IN_FACE_HAT];

        byte b2 = (byte)(r[IN_BTN2] & ~(M_L2 | M_R2));
        if (r[IN_L2] > triggerThreshold) b2 |= M_L2;
        if (r[IN_R2] > triggerThreshold) b2 |= M_R2;
        ex[EX_BTN2] = b2;

        // Low 2 bits are PS + touchpad click; the upper 6 are a frame counter
        // that some titles use to detect a stalled pad.
        ex[EX_SPECIAL] = (byte)((r[IN_BTN3] & 0x03) | ((r[IN_SEQ] & 0x3F) << 2));

        // Cosmetic: full battery, so the game's battery pip isn't stuck empty.
        ex[EX_BATTERY] = 0;
        ex[EX_BATTERY_SPECIAL] = 9;

        // --- motion ----------------------------------------------------
        if (n >= MotionMinLength)
        {
            r.Slice(IN_MOTION, 12).CopyTo(ex.AsSpan(EX_MOTION, 12));

            if (bias is not null)
            {
                short gx = ReadI16(ex, EX_MOTION + 0);
                short gy = ReadI16(ex, EX_MOTION + 2);
                short gz = ReadI16(ex, EX_MOTION + 4);
                bias.Feed(gx, gy, gz);
                if (bias.Ready)
                {
                    WriteI16(ex, EX_MOTION + 0, Sub(gx, bias.X));
                    WriteI16(ex, EX_MOTION + 2, Sub(gy, bias.Y));
                    WriteI16(ex, EX_MOTION + 4, Sub(gz, bias.Z));
                }
            }

            // The DualSense sensor clock ticks every 0.33 us; the DS4's every
            // 5.33 us. Dividing by 16 converts, so games that integrate gyro
            // against the timestamp delta get the right angular rate.
            uint ts = (uint)(r[IN_SENSOR_TS]
                          | (r[IN_SENSOR_TS + 1] << 8)
                          | (r[IN_SENSOR_TS + 2] << 16)
                          | (r[IN_SENSOR_TS + 3] << 24));
            WriteU16(ex, EX_TIMESTAMP, (ushort)(ts / 16));
        }

        // --- touchpad ---------------------------------------------------
        ex[EX_TOUCH_PACKETS] = 1;

        // An all-zero block is NOT an idle touchpad. Tracking byte 0x00 has the
        // up bit clear, so it decodes as "finger 0 is down at (0,0)" and pins
        // the pointer in a corner for as long as the bridge runs. Firmware that
        // doesn't implement the tracking surface leaves this region blank, so
        // blank has to be rejected rather than forwarded. A genuinely lifted
        // finger retains its last coordinates and sets bit 7, so real idle data
        // never looks blank and is never suppressed here.
        bool valid = false;
        if (touch != TouchMode.Off && n >= TouchMinLength)
        {
            for (int i = 0; i < 8; i++)
            {
                if (r[IN_TOUCH + i] == 0) continue;
                valid = true;
                break;
            }
        }

        if (valid)
        {
            // A DS4 advances its touch packet counter with each new packet, but
            // this pad leaves that byte permanently zero. A counter that never
            // moves makes some consumers treat every packet as a stale repeat
            // and ignore the position, so borrow the report's own sequence
            // counter, which always advances.
            ex[EX_TOUCH_COUNTER] = r[IN_SEQ];
            CopyFinger(r, IN_TOUCH + 0, ex, EX_TOUCH + 0, touch);
            CopyFinger(r, IN_TOUCH + 4, ex, EX_TOUCH + 4, touch);
        }
        else
        {
            ex[EX_TOUCH + 0] = FINGER_UP;
            ex[EX_TOUCH + 4] = FINGER_UP;
        }
    }

    // A finger is 4 bytes: [id | up-flag][X low 8][X high 4 | Y low 4][Y high 8].
    // Both pads pack it the same way, so Raw mode is a straight copy; Rescale
    // mode unpacks, squeezes Y into the DS4's shorter pad, and repacks.
    private static void CopyFinger(ReadOnlySpan<byte> r, int inOff, byte[] ex, int exOff, TouchMode mode)
    {
        ex[exOff] = r[inOff];   // tracking number + bit 7 = finger lifted

        if (mode == TouchMode.Raw)
        {
            ex[exOff + 1] = r[inOff + 1];
            ex[exOff + 2] = r[inOff + 2];
            ex[exOff + 3] = r[inOff + 3];
            return;
        }

        int x = ((r[inOff + 2] & 0x0F) << 8) | r[inOff + 1];
        int y = (r[inOff + 3] << 4) | ((r[inOff + 2] & 0xF0) >> 4);

        y = y * Ds4TouchYMax / DualSenseTouchYMax;
        if (y >= Ds4TouchYMax) y = Ds4TouchYMax - 1;

        ex[exOff + 1] = (byte)(x & 0xFF);
        ex[exOff + 2] = (byte)(((x >> 8) & 0x0F) | ((y << 4) & 0xF0));
        ex[exOff + 3] = (byte)(y >> 4);
    }

    // Neutral report, submitted once on connect so the pad reads as centred.
    public static void BuildNeutral(byte[] ex)
    {
        Array.Clear(ex, 0, ExLength);
        ex[EX_LX + 0] = ex[EX_LX + 1] = ex[EX_LX + 2] = ex[EX_LX + 3] = 0x80;
        ex[EX_BTN1] = 0x08;              // hat = neutral
        ex[EX_BATTERY_SPECIAL] = 9;
        ex[EX_TOUCH_PACKETS] = 1;
        ex[EX_TOUCH + 0] = FINGER_UP;
        ex[EX_TOUCH + 4] = FINGER_UP;
    }

    private static short ReadI16(byte[] b, int i) => (short)(b[i] | (b[i + 1] << 8));
    private static void WriteI16(byte[] b, int i, short v)
    { b[i] = (byte)v; b[i + 1] = (byte)(v >> 8); }
    private static void WriteU16(byte[] b, int i, ushort v)
    { b[i] = (byte)v; b[i + 1] = (byte)(v >> 8); }
    private static short Sub(short v, short bias)
        => (short)Math.Clamp(v - bias, short.MinValue, short.MaxValue);
}

// Continuously tracks and filters the gyro's zero-rate offset using an
// exponential moving average (EMA) during resting periods, preventing drift
// from thermal changes or prolonged use. The Scuf Omega does NOT expose
// factory calibration data via feature report 0x05.
public sealed class GyroBias
{
    // Low-pass filter weight for tracking slow thermal drift (higher = slower adaptation)
    private const float Alpha = 0.001f; 
    
    // Maximum deviation from zero to be considered "still" (tighter band prevents locking in hand motion)
    private const int NoiseThreshold = 15;

    private float _biasX, _biasY, _biasZ;
    private bool _initialized;

    public short X => (short)_biasX;
    public short Y => (short)_biasY;
    public short Z => (short)_biasZ;
    public bool Ready => _initialized;

    public void Feed(short gx, short gy, short gz)
    {
        if (!_initialized)
        {
            _biasX = gx;
            _biasY = gy;
            _biasZ = gz;
            _initialized = true;
            return;
        }

        // Check if the current raw values are stable enough to qualify as resting drift.
        // Check against the *current* running bias to measure pure noise.
        float diffX = gx - _biasX;
        float diffY = gy - _biasY;
        float diffZ = gz - _biasZ;

        if (Math.Abs(diffX) <= NoiseThreshold &&
            Math.Abs(diffY) <= NoiseThreshold &&
            Math.Abs(diffZ) <= NoiseThreshold)
        {
            // Slowly creep the bias toward the resting values to handle thermal warming
            _biasX += diffX * Alpha;
            _biasY += diffY * Alpha;
            _biasZ += diffZ * Alpha;
        }
        // If the controller is moving, do NOT update the bias, 
        // preventing physical movements from corrupting the center point.
    }
}

// One-shot diagnostic. Watches the six motion words for a few seconds after
// connect and logs the range each one covered. If every span is zero, the pad
// is not reporting motion at those offsets and no amount of plumbing on the
// output side will produce gyro.
public sealed class MotionProbe
{
    private const int WindowMs = 6000;
    private const int Base = 16;   // gyro starts here in the SCUF report

    private static readonly string[] Names =
        { "gyroPitch", "gyroYaw", "gyroRoll", "accelX", "accelY", "accelZ" };

    private readonly short[] _min = new short[6];
    private readonly short[] _max = new short[6];
    private long _start;
    private bool _seeded, _done;

    public void Feed(ReadOnlySpan<byte> r, int n, Action<string> log)
    {
        if (_done || n < Ds4Raw.MotionMinLength) return;

        long now = Environment.TickCount64;
        if (_start == 0)
        {
            _start = now;
            log("[motion] probing for 6 s — rotate and tilt the pad now.");
        }

        for (int i = 0; i < 6; i++)
        {
            short v = (short)(r[Base + i * 2] | (r[Base + i * 2 + 1] << 8));
            if (!_seeded) { _min[i] = _max[i] = v; }
            else
            {
                if (v < _min[i]) _min[i] = v;
                if (v > _max[i]) _max[i] = v;
            }
        }
        _seeded = true;

        if (now - _start < WindowMs) return;
        _done = true;

        int moving = 0;
        var parts = new List<string>(6);
        for (int i = 0; i < 6; i++)
        {
            int span = _max[i] - _min[i];
            if (span > 8) moving++;
            parts.Add($"{Names[i]} {_min[i]}..{_max[i]} (span {span})");
        }
        log("[motion] " + string.Join(", ", parts));
        log(moving == 0
            ? "[motion] every axis was flat — this pad appears to have no motion sensors."
            : $"[motion] {moving} of 6 axes moved — sensors present.");
    }
}

// One-shot diagnostic for the touch surface. Logs the extent each finger
// actually covered so you can see whether the pad reports coordinates at all,
// and what its real Y range is. Drag a finger into all four corners while it
// runs; the reported Y maximum decides whether you need
// <see cref="TouchMode.RescaleY"/>.
public sealed class TouchProbe
{
    private const int WindowMs = 8000;
    private const int Base = Ds4Raw.IN_TOUCH;

    // Bytes swept for activity. Deliberately starts past the sensor timestamp
    // (28-31), which changes every report and would drown out the signal.
    private const int ScanFrom = 32, ScanTo = 55;

    private readonly byte[] _lo = new byte[ScanTo - ScanFrom + 1];
    private readonly byte[] _hi = new byte[ScanTo - ScanFrom + 1];
    private bool _scanSeeded;

    private long _start;
    private bool _done, _seen;
    private int _minX = int.MaxValue, _maxX = int.MinValue;
    private int _minY = int.MaxValue, _maxY = int.MinValue;
    private int _samples, _twoFinger;

    public void Feed(ReadOnlySpan<byte> r, int n, Action<string> log)
    {
        if (_done || n < Ds4Raw.TouchMinLength) return;

        long now = Environment.TickCount64;
        if (_start == 0)
        {
            _start = now;
            log("[touch] probing for 8 s — drag a finger into all four corners of the pad.");
        }

        // Which bytes move at all while a finger is dragging? If the assumed
        // block is dead but something else here is alive, the tracking data is
        // simply at a different offset and IN_TOUCH needs changing.
        if (n > ScanTo)
        {
            for (int i = 0; i < _lo.Length; i++)
            {
                byte v = r[ScanFrom + i];
                if (!_scanSeeded) { _lo[i] = _hi[i] = v; }
                else
                {
                    if (v < _lo[i]) _lo[i] = v;
                    if (v > _hi[i]) _hi[i] = v;
                }
            }
            _scanSeeded = true;
        }

        if ((r[Base] & 0x80) == 0)
        {
            _seen = true;
            _samples++;
            int x = ((r[Base + 2] & 0x0F) << 8) | r[Base + 1];
            int y = (r[Base + 3] << 4) | ((r[Base + 2] & 0xF0) >> 4);
            if (x < _minX) _minX = x; if (x > _maxX) _maxX = x;
            if (y < _minY) _minY = y; if (y > _maxY) _maxY = y;
        }
        if ((r[Base + 4] & 0x80) == 0) _twoFinger++;

        if (now - _start < WindowMs) return;
        _done = true;

        var active = new List<string>();
        for (int i = 0; i < _lo.Length; i++)
            if (_hi[i] != _lo[i]) active.Add($"{ScanFrom + i}:{_lo[i]:X2}-{_hi[i]:X2}");
        log(active.Count == 0
            ? $"[touch] bytes {ScanFrom}-{ScanTo} never changed — no tracking data anywhere in this region."
            : $"[touch] bytes that moved: {string.Join(" ", active)}");

        if (!_seen)
        {
            log("[touch] no finger contact seen — either you didn't touch the pad, " +
                "or it reports click only and has no tracking surface.");
            return;
        }

        log($"[touch] {_samples} contact samples, X {_minX}..{_maxX}, Y {_minY}..{_maxY}, " +
            $"second finger seen {(_twoFinger > 0 ? "yes" : "no")}.");

        if (_maxX == _minX && _maxY == _minY)
            log($"[touch] coordinates never varied (stuck at {_minX},{_minY}) — the block at " +
                $"byte {Ds4Raw.IN_TOUCH} is not live. Compare against the moved-bytes list above.");
        else
            log(_maxY > 950
                ? "[touch] Y exceeds the DS4's 942 range — set TouchSurfaceMode = TouchMode.RescaleY."
                : "[touch] Y fits the DS4 range — TouchMode.Raw is correct.");
    }
}
