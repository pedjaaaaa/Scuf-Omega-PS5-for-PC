# SCUF → DualSense Bridge — Use a SCUF Omega in PS5 Mode on PC (PlayStation Prompts, Gyro, Full Touchpad)

**Make a SCUF Omega (Corsair) controller work as a real PlayStation DualShock 4
on PC** — with PlayStation button prompts, **gyro/motion**, a **fully working
touchpad** (click *and* two-finger surface tracking), the PS button, and no
double input. For SCUF pads that DS4Windows, reWASD, and Steam don't recognise
as a PlayStation device.

### Does this describe your problem?

- Your **SCUF Omega in PC mode** shows up on PC as a **"generic controller"** or
  gives **Xbox button prompts** instead of PlayStation ones.
- **DS4Windows / reWASD don't detect** your SCUF as a DualShock 4.
- You get **double / duplicate controller input** in games.
- Your SCUF's **touchpad or PS button** don't work on PC.
- You want **gyro aiming** from a SCUF on PC and nothing exposes the motion data.

If so, this bridge fixes it. It reads the pad's raw HID report directly and
re-presents it under Sony's real VID (`0x054C` / DS4 `0x05C4`) via ViGEm, so
Windows and games see a genuine DualShock 4.

Built because tools like DS4Windows/reWASD didn't recognise this specific SCUF
(Corsair VID `0x1B1C`,Wired PID `0x3A27` / Wireless PID `0x3A29`) as a PlayStation device — Windows and
XInput-native games saw a "generic controller" and ignored it or showed Xbox
prompts.

> ⚠️ **This is calibrated for one specific SCUF model.** The byte/bit maps in
> `ScufReport.cs` (buttons and sticks) and `Ds4Raw.cs` (motion and touch) were
> reverse-engineered for VID `1B1C` / Wired PID `3A27`, Wireless PID `3A29`. A different SCUF (or firmware)
> may use a different PID and/or report layout. See
> "Porting to another SCUF / pad" below for how to remap it.

## Quick start

Follow these steps in order. Each links to the detailed section further down.

1. **Install the [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0).**
   Needed to build the app.
2. **Install the [ViGEmBus](https://github.com/nefarius/ViGEmBus/releases) driver**
   (creates the virtual DS4), then reboot. Use **1.17.333 or newer** — gyro and
   touchpad tracking need it.
3. **Install the [HidHide](https://github.com/nefarius/HidHide/releases) driver**
   (hides the physical pad), then reboot.
4. **Confirm your controller matches.** Put the SCUF in **PS5 mode**, open Device
   Manager → your controller → *Details* → *Hardware Ids*, and check it reads
   `VID_1B1C` / `PID_3A27` or `PID_3A29`. If it differs, see
   [Porting to another SCUF / pad](#porting-to-another-scuf--pad) before continuing.
5. **Clone and run the app:**
   ```powershell
   git clone https://github.com/pedjaaaaa/Scuf-Omega-PS5-for-PC.git
   cd Scuf-Omega-PS5-for-PC
   dotnet run -c Release
   ```
   A PlayStation icon appears in the system tray. See [Build & run](#build--run).
6. **(If you use Steam)** disable Steam Input for your game — see
   [Steam settings](#steam-settings-important).
7. **(Optional) Start it automatically at logon** via Task Scheduler — see
   [Auto-start at logon](#auto-start-at-logon-hidden-elevated).
8. **(Optional) Raise the polling rate** from ~250 Hz to ~800–900 Hz with hidusbf
   — see [Higher polling rate](#higher-polling-rate-optional-250--800900-hz).

## What works

Sticks, triggers, all face/shoulder/menu buttons, D-pad, L3/R3, **PS button**,
and **touchpad click** - everything the game needs for PlayStation prompts.

Also forwarded, via the DS4 **extended** report:

- **Gyro and accelerometer.** All six axes at full rate, with the sensor
  timestamp converted to DS4 units so games that integrate gyro against the
  timestamp delta get the correct angular rate. A resting-offset correction runs
  continuously so a pad left on the desk doesn't drift the camera.
- **Touchpad surface tracking.** Both fingers, full 1920 × 942 range, with the
  pad's taller native Y range rescaled to what a DS4 reports.

**Not** implemented: adaptive triggers and haptics - the Omega has neither, since
Sony's licensing terms exclude both for third-party controllers, and Scuf removed
the vibration motors entirely.

> **Requires ViGEmBus 1.17.333 or newer** for motion and touch coordinates. On an
> older bus the app logs a warning and falls back to buttons and axes only;
> everything else still works.

### How the motion and touch data gets through

ViGEm's ordinary `SetButtonState`/`SetAxisValue` path builds a 9-byte
`DS4_REPORT`, which has no fields for motion or touch coordinates at all. Those
only exist in the 63-byte `DS4_REPORT_EX`, so `Ds4Raw.cs` assembles that report
by hand and submits it with `SubmitRawReport`.

On the input side this pad's report is DualSense-format, and for the motion block
the two are byte-identical - same order, same units, same signs — so gyro and
accel are a straight 12-byte copy with no scaling or sign flips. The touch block
is the one place the pad deviates: its two fingers sit at bytes **32–39**, one
byte earlier than a real DualSense puts them.

## Requirements

- Windows 10/11
- **[.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0)** — needed to build
  from source. (If you use a self-contained published exe as described in
  *Auto-start* below, end users don't need .NET installed.)
- **[ViGEmBus](https://github.com/nefarius/ViGEmBus/releases)** driver (creates the virtual DS4).
  **1.17.333 or newer** — older builds reject the extended report, which costs you
  gyro and touchpad tracking but leaves buttons and axes working.
- **[HidHide](https://github.com/nefarius/HidHide/releases)** driver (hides the physical pad; optional — see `EnableHidHide` in `ScufBridge.cs`)
- Admin rights — the app auto-elevates via its manifest (HidHide and ViGEm both require it)

> Install **both drivers first and reboot** before running the app. On first run
> you'll get a UAC prompt because the app elevates itself.

### Which SCUF does this support?

Out of the box, only the specific model it was calibrated for: **Corsair VID
`0x1B1C`, Wired PID `0x3A27`/Wireless PID `0x3A29`** (a SCUF running in PS/HID Wired/Wireless mode). Other SCUF models or
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

1. SCUF in **PS5 mode**.
2. Start the bridge (tray icon appears).
3. Configure Steam (see below) so it doesn't re-wrap the virtual DS4.
4. Launch the game. You should see PlayStation prompts and a single, clean
   controller.

### Steam settings (important)

Steam Input will, by default, grab the virtual DS4 and re-present it as an Xbox
pad — which gives you Xbox prompts and can cause double input. The virtual
controller works fine outside Steam; these steps are only needed for games
launched through Steam.

**Per-game (recommended):**

1. In your Steam **Library**, right-click the game → **Properties**.
2. Open the **Controller** tab.
3. Set **Override for _[game]_** to **Disable Steam Input**.
4. Fully close and relaunch the game.

**Global setting to check** (Steam → **Settings** → **Controller**):

- If you want Steam to leave PlayStation controllers alone everywhere, turn
  **off** *PlayStation Controller Support*. Leaving it on is fine as long as you
  set the per-game override above — the per-game setting wins.
- Turning these toggles on/off takes effect after a game restart, sometimes after
  a Steam restart.

**Non-Steam games:** no Steam configuration needed — just run the bridge and
launch the game.

> Rule of thumb: if you're seeing **Xbox** button prompts or inputs firing twice,
> Steam Input is still active for that title. Re-check the per-game override.

## Auto-start at logon (hidden, elevated)

Because the app needs admin, use **Task Scheduler** (Startup-folder shortcuts
can't elevate):

1. Publish a standalone exe:
   ```powershell
   dotnet publish -c Release -r
   ```
   Result: `bin\Release\net8.0-windows\win-x64\publish\ScufDualSense.exe`
2. Task Scheduler → **Create Task** (not Basic):
   - **General**: check *Run with highest privileges*; *Run only when user is
     logged on* (so the tray icon shows).
   - **Triggers**: *At log on*.
   - **Actions**: start the published `ScufDualSense.exe`.
   - **Conditions**: uncheck *Start only on AC power* if on a laptop.
3. Done — it now starts hidden at logon and waits for the pad.

## Higher polling rate (optional, ~250 → 800–900 Hz)

In PS5 mode the SCUF reports at ~250 Hz (a real DualShock 4's USB rate). That's
fine for most people, but you can raise it with **hidusbf**, a kernel USB filter
originally by **SweetLow / [LordOfMice](https://github.com/LordOfMice/hidusbf)** and
redistributed by **Battle Beaver** in a signed build. hidusbf works at the Windows
USB interrupt-scheduling layer — it doesn't change the device itself — so your
bridge just inherits the higher rate and **no app changes are needed**. Thanks to
Front_Frame4653 for the driver-variant and stability findings.

**Realistic result:** ~800–900 Hz through the virtual DS4 (mine is stable at
**870 Hz** and survives reboots) — roughly 3.5× a genuine DualShock 4. A true
1000 Hz only exists in the pad's **PC/Xbox mode**, which loses the touchpad, PS
button, and PlayStation prompts, so this bridge tops out below 1000 by design.

### Download

Get the signed Battle Beaver build from their official guide page:

- **[Battle Beaver — Controller Overclocking guide + download](https://battlebeavercustoms.com/pages/overclocking)**

Battle Beaver EV-signed these kernel drivers and had them attestation-signed by
Microsoft, so on Windows 11 **you no longer need to disable Secure Boot**. Prefer
this official page over random mirrors — you're installing a kernel driver.

### Setup

1. **Turn off Memory Integrity** (Windows 11). Search Start for *Core isolation*
   and set **Memory Integrity** to **Off**, then reboot. The driver won't load
   otherwise. This is a real security tradeoff — see *Caveats*.
2. **Extract the zip to a permanent folder** (not a temp dir — the service
   references this path).
3. **Install the 4 kHz driver variant.** Open the extracted **`DRIVER`** folder,
   right-click **`2kHz-4kHz.cmd`** → **Run as administrator**. This swaps in the
   driver variant that exposes rates above 1000 Hz — **don't skip it**, the higher
   refresh rates won't be selectable otherwise. (The package also ships
   `1kHz.cmd` if you ever want to drop back to the standard 1 kHz driver.)
4. **Run `Setup.exe`** from that same `DRIVER` folder — right-click →
   **Run as administrator**. An "unknown publisher" prompt is normal.
5. **Find the SCUF.** With the pad plugged in and in PS5 mode, set the **Devices**
   dropdown (top-left) to **All**. The *Device Name* column is unhelpful with many
   USB devices — look in the **Child Name(s)** column for **"Wireless
   Controller"** instead. Click that row to select it.
6. **Apply the filter.** Tick **`Filter on Device`** (bottom-left), pick your rate
   in the **Selected Rate** dropdown, then click **Install Service**.
   > On my setup the value that worked best was **RefreshRate 62** (see below).
   > Battle Beaver's own guide uses the plain `1000` entry — try both, the
   > available options depend on which driver variant you installed in step 3.
7. **Click `Restart`** to re-enumerate the pad (sometimes you must physically
   unplug/replug instead).
8. **Verify.** The **Filter** column should read **Yes** and the **Rate** column
   should match what you selected. Unplug/replug and confirm it sticks. For a final
   check, set the **Devices** dropdown to **with HIDUSBF** — your pad should appear
   there.

> **If the device is highlighted red**, the flash failed. The common Windows 11 fix
> is to right-click **`HIDUSBF_AS`** → **Install**, then re-run the `.cmd` from
> step 3 as administrator. Otherwise see the
> [hidusbf troubleshooting wiki](https://github.com/LordOfMice/hidusbf/wiki).

### The two things that actually matter

- **Plug the SCUF into a rear motherboard USB 3.x port — never a hub or
  front-panel port.** This was the single biggest factor: a hub capped me at
  ~566 Hz; a rear 3.x port jumped it to ~750. If you're stuck below ~700, this is
  almost certainly why.
- **RefreshRate 62 beat 31 for me** (~750 → 870 Hz). Try both and keep whichever
  reads higher *and* stable — some setups do better on 31.

Measure with a polling tester —
**[Gamepadla](https://github.com/cakama3a/Gamepadla/releases/)** is what Battle
Beaver use — pointed at the virtual **"Wireless Controller"**, since that's what
the game actually sees. The tray tooltip also shows a rate, but it reads a little
low (it counts reports drained from the Windows HID buffer, which coalesces), so
trust the tester.

### Caveats

hidusbf is a **kernel driver** and requires USB 3.x with the Microsoft USB 3.x
stack (Windows 8/10/11). Two things to weigh:

- **Memory Integrity (Core Isolation) must be off** on Windows 11 for the driver to
  load. That's a genuine security tradeoff — it disables a hypervisor-backed
  protection against malicious drivers. Your call whether the input latency is
  worth it; you can turn it back on (and lose the overclock) at any time.
- **Anti-cheat**: this stacks another kernel driver alongside ViGEm and HidHide.
  Widely used in the competitive-controller scene, but never zero-risk.

On the plus side, the current Battle Beaver build is EV-signed and Microsoft
attestation-signed, so **disabling Secure Boot is no longer required** the way it
was with older unsigned hidusbf releases.

## Porting to another SCUF / pad

The HID report layout is per-model, so a different SCUF (or firmware) will likely
need remapping. The process:

1. **Find your pad's VID/PID.** With the SCUF in PS5 mode, open Device Manager →
   your controller → *Details* → *Hardware Ids*, and note the `VID_xxxx` /
   `PID_xxxx` values.
2. **Update the identifiers.** Set `Vid`, `Pid`, and `DeviceFragment` in
   `ScufBridge.cs`, and confirm the VID/PID references in `ScufReport.cs`.
3. **Map the buttons and sticks.** Determine which report byte/bit each control
   uses, then update the offset and mask constants at the top of `ScufReport.cs`
   (`LX`, `L2_ANALOG`, `M_CROSS`, etc.). You can inspect the raw report bytes by
   temporarily logging the buffer in `RunOneCycle` in `ScufBridge.cs` and
   watching how bytes change as you press each control.
4. **Map the motion and touch blocks.** These live in `Ds4Raw.cs`, not
   `ScufReport.cs`: `IN_MOTION` (gyro, then accel — twelve bytes total),
   `IN_SENSOR_TS`, and `IN_TOUCH`. Two built-in probes find them for you - set
   `LogMotionProbe` / `LogTouchProbe` to `true` in `ScufBridge.cs`, run, and read
   `%LOCALAPPDATA%\ScufDualSense\scuf.log`:
   - **MotionProbe** logs the range each of the six motion axes covered while you
     rotate the pad. Every axis flat means the pad has no sensors and no amount
     of plumbing will produce gyro.
   - **TouchProbe** logs which report bytes moved while you dragged a finger,
     plus the X/Y extent decoded from the current `IN_TOUCH`. If the decoded
     coordinates never vary but other bytes did move, point `IN_TOUCH` at the
     first byte that moved — that's the finger's tracking byte.

   If the probe reports a Y maximum above ~942, set `TouchSurfaceMode` to
   `TouchMode.RescaleY`; if it fits inside the DS4 range, `TouchMode.Raw` is
   correct. `TouchMode.Off` disables surface tracking and keeps click only.

> A **calibration wizard** is included in [`tools/`](tools/) to speed up step 3:
> run it (`dotnet run` from `tools/`), press each control when prompted, and it
> prints a byte/bit layout map you can copy straight into `ScufReport.cs`.
> Requires the SCUF in PS5 mode with no remapper app or HidHide hiding it.

> **A caution learned the hard way.** This pad's report is DualSense-format for
> sticks, buttons, triggers and the entire motion block - but *not* for touch,
> where the fingers sit one byte earlier than the DualSense spec says. Matching
> in one region is not evidence for another. Dump the bytes and confirm.

## Fixing phantom input at the login screen

If your screen flickers or fields tab through themselves on the Windows **login /
password screen** — stopping only when you unplug and replug the pad — the raw
SCUF is sending stray navigation input before anything is hiding it.

Why it happens: if the bridge auto-starts *only after you log on* (the Task
Scheduler setup above), then at the login screen the bridge isn't running yet, so
nothing is hiding the physical pad. Windows sees the bare controller and a
slightly off-center stick or held direction reads as repeated "navigate" input.

Fix it by having **HidHide's own service** hide the physical pad persistently —
it enforces this at boot, before login, independent of the bridge:

1. Open the **HidHide Configuration Client** (from the Start menu).
2. On the **Applications** tab, add the bridge exe so it can still read the pad
   while it's hidden from everything else:
   `bin\Release\net8.0-windows\win-x64\publish\ScufDualSense.exe`
3. On the **Devices** tab, **check the physical SCUF** (e.g. *"Corsair SCUF OMEGA
   WIRELESS CONTROLLER vendor"*, VID_1B1C). **Leave the virtual _"Sony ... Wireless
   Controller"_ unchecked** — that's the DS4 your games need to see; hiding it
   would break everything.
4. Make sure **Enable device hiding** (bottom left) is **checked**.
5. Unplug/replug the SCUF (the client reminds you: *re-connect for changes to take
   effect*), then reboot to confirm it persists across a cold boot.

After this, the physical pad is hidden from the moment Windows starts, so it can't
spam the login screen — while the whitelisted bridge still reads it and feeds the
visible virtual DS4 to games.

> **Rule of thumb:** hide the **physical** SCUF, keep the **virtual** Sony
> controller visible — never the other way around.

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
- **`[warn] extended DS4 reports rejected`** in the log, and no gyro or touchpad
  tracking. Your ViGEmBus predates the extended-report call. Update to
  **1.17.333 or newer** and reboot. Buttons and axes keep working meanwhile.
- **No gyro in the game, but the log says motion is on.** The bridge's job ends
  at the virtual pad; something has to read the motion from it. See
  *Gyro: which layer actually reads it* - most likely you need Steam Input
  **enabled** for that title rather than disabled.
- **The camera drifts slowly on its own.** The resting-offset correction only
  re-learns while the pad is genuinely still, so leave it untouched on a flat
  surface for a second. If it persists, set `EnableGyroBiasCorrection = false` in
  `ScufBridge.cs` and let the game do its own calibration instead.
- **Touchpad pointer stuck in a corner, or not following your finger.** The touch
  block is at the wrong offset for your pad. Set `LogTouchProbe = true` and
  follow *Porting to another SCUF / pad* — or set
  `TouchSurfaceMode = TouchMode.Off` to fall back to click-only, which is
  strictly better than a stuck contact, since a phantom touch can block menu
  input in some games.
- **Login screen flickers / tabs by itself until you replug the pad.** The raw
  SCUF is sending stray input before the bridge starts hiding it — see
  *Fixing phantom input at the login screen*.
- **The app crashed with no obvious cause.** Check
  `%LOCALAPPDATA%\ScufDualSense\scuf.log` for a `[FATAL]` line — it now records
  the full exception and stack trace instead of dying silently.
- **Where are the logs?** `%LOCALAPPDATA%\ScufDualSense\scuf.log` (also reachable
  via tray → **Open log folder**). The tray tooltip (hover) and the log's periodic
  throughput line show the delivered poll rate. ~250 Hz is normal — the genuine
  DualShock 4 USB rate.

## Honest caveats

- **Anti-cheat**: this uses ViGEm + HidHide (same as DS4Windows). Widely
  tolerated, but injecting virtual input + hiding devices near kernel
  anti-cheat is never zero-risk. Use on your own account at your own judgement.
- **One-model calibration**: see the big warning above.
- **Gyro calibration is approximated.** A DualSense reports uncalibrated sensor
  values and expects the host to apply the factory calibration from feature
  report `0x05`. This bridge doesn't read that; it learns the resting offset at
  runtime instead, which removes drift but doesn't correct per-axis scale. Good
  enough for aiming; not a substitute for the real calibration data if you need
  absolute angular accuracy.
- **Not affiliated** with SCUF/Corsair, Sony, or Nefarius.

## Keywords

SCUF Omega PC, SCUF Omega PS5 mode on PC, SCUF DualShock 4 emulator, SCUF
PlayStation prompts Windows, SCUF not detected DS4Windows, SCUF reWASD not
working, Corsair SCUF VID 1B1C PID 3A27, SCUF generic controller Windows, SCUF
Xbox prompts fix, SCUF touchpad PS button PC, virtual DualShock 4 ViGEm HidHide,
SCUF double input fix, SCUF Omega polling rate PC, SCUF Omega gyro PC, SCUF gyro
aiming Windows, SCUF motion controls DS4, SCUF Omega touchpad tracking, DS4
extended report gyro ViGEm, DS4_REPORT_EX SubmitRawReport motion.

## License

MIT — see `LICENSE`.
