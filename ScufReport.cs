// ScufReport.cs
//
// Parses one SCUF PS-mode HID input report into a neutral state struct, using
// the byte/bit map established by the calibration wizard. This SCUF's report is
// effectively a DualShock 4 report with the analog triggers relocated to bytes
// 5-6, so the offsets below are the ground-truth calibration results — edit a
// constant here if any control ever maps wrong.

namespace ScufDualSense;

/// <summary>Parsed state of one SCUF HID report (report ID is byte 0).</summary>
public struct ScufState
{
    public byte LX, LY, RX, RY;   // sticks: 0x80/0x7F center, Y is DOWN-positive (DS4-native)
    public byte L2, R2;           // analog triggers 0..255
    public bool L2Btn, R2Btn;     // digital trigger bits (report byte 9, bits 0x04/0x08)
    public byte Hat;              // 0..7 = direction, 8 = neutral
    public bool Cross, Circle, Square, Triangle;
    public bool L1, R1, L3, R3, Share, Options, Ps, TouchpadClick;
}

public static class ScufReport
{
    // ---- calibrated byte offsets (report ID at 0) --------------------
    private const int LX = 1, LY = 2, RX = 3, RY = 4;
    private const int L2_ANALOG = 5, R2_ANALOG = 6;
    private const int FACE_HAT = 8;   // low nibble = hat, high nibble = face buttons
    private const int BTN2 = 9;       // L1/R1/Share/Options/L3/R3
    private const int BTN3 = 10;      // PS / touchpad-click

    // ---- byte 8 (face) masks ----
    private const byte M_SQUARE = 0x10, M_CROSS = 0x20, M_CIRCLE = 0x40, M_TRIANGLE = 0x80;
    // ---- byte 9 masks ----
    private const byte M_L1 = 0x01, M_R1 = 0x02, M_L2 = 0x04, M_R2 = 0x08,
                       M_SHARE = 0x10, M_OPTIONS = 0x20, M_L3 = 0x40, M_R3 = 0x80;
    // ---- byte 10 masks ----
    private const byte M_PS = 0x01, M_TPAD = 0x02;

    /// <summary>Minimum report length we need to safely read every field.</summary>
    public const int MinLength = 11;

    public static ScufState Parse(ReadOnlySpan<byte> r)
    {
        byte face = r[FACE_HAT];
        byte b2 = r[BTN2];
        byte b3 = r[BTN3];

        return new ScufState
        {
            LX = r[LX], LY = r[LY], RX = r[RX], RY = r[RY],
            L2 = r[L2_ANALOG], R2 = r[R2_ANALOG],
            L2Btn = (b2 & M_L2) != 0,
            R2Btn = (b2 & M_R2) != 0,
            Hat = (byte)(face & 0x0F),

            Square   = (face & M_SQUARE)   != 0,
            Cross    = (face & M_CROSS)    != 0,
            Circle   = (face & M_CIRCLE)   != 0,
            Triangle = (face & M_TRIANGLE) != 0,

            L1      = (b2 & M_L1)      != 0,
            R1      = (b2 & M_R1)      != 0,
            Share   = (b2 & M_SHARE)   != 0,
            Options = (b2 & M_OPTIONS) != 0,
            L3      = (b2 & M_L3)      != 0,
            R3      = (b2 & M_R3)      != 0,

            Ps            = (b3 & M_PS)   != 0,
            TouchpadClick = (b3 & M_TPAD) != 0,
        };
    }
}
