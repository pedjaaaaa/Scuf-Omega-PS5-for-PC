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
> "Porting to another SCUF / pad" below for how to remap it.

## What works

Sticks, triggers, all face/shoulder/menu buttons, D-pad, L3/R3, **PS button**,
and **touchpad click**. Everything the game needs for PlayStation prompts.

**Not** implemented: touchpad *surface* dragging (the 12-bit X/Y coordinates),
gyro/motion, adaptive triggers, and haptics. The touch coordinates exist in the
report (bytes 33–35) but pushing them through requires ViGEm raw extended
reports; PRs welcome. The Omega has no adaptive triggers/haptics anyway.

## Requirements

- Windows 10/11
- **[.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0)** — needed to build
  from source. (If you use a self-contained published exe as described in
  *Auto-start* below, end users don't need .NET installed.)
- **[ViGEmBus](https://github.com/nefarius/ViGEmBus/releases)** driver (creates the virtual DS4)
- **[HidHide](https://github.com/nefarius/HidHide/releases)** driver (hides the physical pad; optional — see `EnableHidHide` in `ScufBridge.cs`)
- Admin rights — the app auto-elevates via its manifest (HidHide and ViGEm both require it)

> Install **both drivers first and reboot** before running the app. On first run
> you'll get a UAC prompt because the app elevates itself.

### Which SCUF does this support?

Out of the box, only the specific model it was calibrated for: **Corsair VID
`0x1B1C`, PID `0x3A27`** (a SCUF running in PS/HID mode). Other SCUF models or
firmware revisions may enumerate with a different PID and/or report layout — see
[Porting to another SCUF / pad](#porting-to-another-scuf--pad).

## Build & run

Clone the repo and run from its root:

```powershell
git clone https://github.com/pedjaaaaa/Scuf-Omega-PS5-for-PC.git
cd Scuf-Omega-PS5-for-PC
dotnet run -c Release
```

If NuGet restore fails on the Nefarius packages, let it pick current versions:

```powershell
dotnet add package Nefarius.Drivers.HidHide --prerelease
dotnet add package Nefarius.Utilities.DeviceManagement
```

The app lives in the system tray (look for the PlayStation icon). Right-click →
**Exit** to quit cleanly (this restores HidHide and drops the virtual pad). Logs
go to `%LOCALAPPDATA%\ScufDualSense\scuf.log`.

> **Note on the build output path.** The project pins `<Platforms>x64</Platforms>`,
> so a plain `dotnet build` writes to `bin\Release\net8.0-windows\`, while a
> build that specifies the platform (e.g. `-p:Platform=x64`) writes to
> `bin\x64\Release\net8.0-windows\`. If a change doesn't seem to take effect,
> make sure you're running the exe from the folder your last build actually
> wrote to.

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

The HID report layout is per-model, so a different SCUF (or firmware) will likely
need remapping. The process:

1. **Find your pad's VID/PID.** With the SCUF in PS mode, open Device Manager →
   your controller → *Details* → *Hardware Ids*, and note the `VID_xxxx` /
   `PID_xxxx` values.
2. **Update the identifiers.** Set `Vid`, `Pid`, and `DeviceFragment` in
   `ScufBridge.cs`, and confirm the VID/PID references in `ScufReport.cs`.
3. **Map the report bytes.** Determine which report byte/bit each control uses,
   then update the offset and mask constants at the top of `ScufReport.cs`
   (`LX`, `L2_ANALOG`, `M_CROSS`, etc.). You can inspect the raw report bytes by
   temporarily logging the buffer in `RunOneCycle` in `ScufBridge.cs` and
   watching how bytes change as you press each control.

> A **calibration wizard** is included in [`tools/`](tools/) to speed this up: run
> it (`dotnet run` from `tools/`), press each control when prompted, and it prints
> a byte/bit layout map you can copy straight into `ScufReport.cs`. Requires the
> SCUF in PS mode with no remapper app or HidHide hiding it.

## Troubleshooting

- **`GenerateBundle` / "the process cannot access the file ... ScufDualSense.exe
  because it is being used by another process"** when publishing or building.
  A copy of the app is still running and holding the exe. Exit it from the tray
  (right-click → **Exit**), and if it was launched via Task Scheduler, stop it
  there or end `ScufDualSense.exe` in Task Manager, then rebuild.
- **The tray/exe still shows the generic icon.** Two causes: (1) you ran a stale
  build from a different output folder — see the build-output-path note above;
  or (2) Windows cached the old icon. Fully exit the app, then refresh the cache
  with `ie4uinit.exe -show`.
- **`[fatal] ViGEmBus unavailable`** in the log. The ViGEmBus driver isn't
  installed (or you didn't reboot after installing). Install it and reboot.
- **Game shows Xbox prompts / double input.** Steam Input is re-wrapping the
  virtual DS4. Disable Steam Input for that game (see *Usage in games*).
- **A control maps wrong (or not at all).** The report layout differs for your
  pad — see *Porting to another SCUF / pad*.
- **Where are the logs?** `%LOCALAPPDATA%\ScufDualSense\scuf.log` (also reachable
  via tray → **Open log folder**). A `[rate]` line reports the pad's polling rate
  on connect (~250 Hz is normal — the genuine DualShock 4 USB rate).

## Honest caveats

- **Anti-cheat**: this uses ViGEm + HidHide (same as DS4Windows). Widely
  tolerated, but injecting virtual input + hiding devices near kernel
  anti-cheat is never zero-risk. Use on your own account at your own judgement.
- **One-model calibration**: see the big warning above.
- **Not affiliated** with SCUF/Corsair, Sony, or Nefarius.

## License

MIT — see `LICENSE`.
