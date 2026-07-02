# SCUF → DualSense Bridge

Makes a **SCUF controller running in its PlayStation/HID mode** show up to Windows
and games as a genuine Sony **DualShock 4** — so you get PlayStation button
prompts, a working touchpad **click**, and the PS button, with no double input.

Built because tools like DS4Windows/reWASD didn't recognise this specific SCUF
(Corsair VID `0x1B1C`, PID `0x3A27`) as a PlayStation device. Windows and
XInput-native games saw a "generic controller" and ignored it or showed Xbox
prompts. This reads the pad's raw HID report directly and re-presents it under
Sony's real VID (`0x054C` / DS4 `0x05C4`) via ViGEm.

> ⚠️ **This is calibrated for one specific SCUF model.** The byte/bit map in
> `ScufReport.cs` was reverse-engineered for VID `1B1C` / PID `3A27`. A
> different SCUF (or firmware) may use a different PID and/or report layout. See
> "Porting to another pad" below — the included calibration wizard makes it
> straightforward.

## What works

Sticks, triggers, all face/shoulder/menu buttons, D-pad, L3/R3, **PS button**,
and **touchpad click**. Everything the game needs for PlayStation prompts.

**Not** implemented: touchpad *surface* dragging (the 12-bit X/Y coordinates),
gyro/motion, adaptive triggers, and haptics. The touch coordinates exist in the
report (bytes 33–35) but pushing them through requires ViGEm raw extended
reports; PRs welcome. The Omega has no adaptive triggers/haptics anyway.

## Requirements

- Windows 10/11, .NET 8 SDK
- **[ViGEmBus](https://github.com/nefarius/ViGEmBus/releases)** driver (virtual DS4)
- **[HidHide](https://github.com/nefarius/HidHide/releases)** driver (hides the physical pad; optional — see `EnableHidHide`)
- Admin rights (the app auto-elevates via its manifest)

## Build & run

```powershell
dotnet run -c Release
```

If NuGet restore fails on the Nefarius packages, let it pick current versions:

```powershell
dotnet add package Nefarius.Drivers.HidHide --prerelease
dotnet add package Nefarius.Utilities.DeviceManagement
```

The app lives in the system tray. Right-click → **Exit** to quit cleanly (this
restores HidHide and drops the virtual pad). Logs go to
`%LOCALAPPDATA%\ScufDualSense\scuf.log`.

## Usage in games

1. SCUF in **PS mode**.
2. For a game with native PlayStation support (e.g. Arc Raiders): **disable
   Steam Input for that game** (per-game Controller override). Steam Input would
   otherwise re-wrap the virtual DS4 as an Xbox pad and undo the PlayStation
   prompts.
3. Launch. You should see PlayStation prompts and a single, clean controller.

## Auto-start at logon (hidden, elevated)

Because the app needs admin, use **Task Scheduler** (Startup-folder shortcuts
can't elevate):

1. Publish a standalone exe:
   ```powershell
   dotnet publish -c Release -r win-x64 --self-contained -p:PublishSingleFile=true
   ```
   Result: `bin\Release\net8.0-windows\win-x64\publish\ScufDualSense.exe`
2. Task Scheduler → **Create Task** (not Basic):
   - **General**: check *Run with highest privileges*; *Run only when user is
     logged on* (so the tray icon shows).
   - **Triggers**: *At log on*.
   - **Actions**: start the published `ScufDualSense.exe`.
   - **Conditions**: uncheck *Start only on AC power* if on a laptop.
3. Done — it now starts hidden at logon and waits for the pad.

## Porting to another SCUF / pad

The report layout is per-model. To map a different pad:

1. Use the **calibration wizard** (in `/tools/scuf-calibrate`): it walks you
   through each control and prints an exact byte/bit layout map.
2. Update the VID/PID in `ScufBridge.cs` + `ScufReport.cs` and the mask/offset
   constants in `ScufReport.cs` from the wizard output.
3. The `HidDumper` tool is there too if you need raw report inspection.

## Honest caveats

- **Anti-cheat**: this uses ViGEm + HidHide (same as DS4Windows). Widely
  tolerated, but injecting virtual input + hiding devices near kernel
  anti-cheat is never zero-risk. Use on your own account at your own judgement.
- **One-model calibration**: see the big warning above.
- **Not affiliated** with SCUF/Corsair, Sony, or Nefarius.

## License

MIT — see `LICENSE`.
