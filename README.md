# AnalogtoKey

**Map joystick and arcade stick buttons to keyboard keys on Windows.**

AnalogtoKey reads input from USB joysticks and arcade sticks and converts every button press, D-pad direction, and axis movement into real keyboard keystrokes — exactly as if you had pressed a key on your keyboard.

## Download

➡ **[Download AnalogtoKey_Setup_v0.3.3.exe](https://github.com/Gallinarius/AnalogtoKey/releases/latest)**

No .NET installation required. Self-contained, runs on Windows 10 and 11 (64-bit).

---

## Screenshot

![AnalogtoKey main window](docs/screenshot.png)

---

## Why does this exist?

Many simulators are best controlled via keyboard shortcuts but have poor or forced gamepad support — causing wrong input, doubled keystrokes, or axes behaving incorrectly when a joystick or arcade stick is connected.

AnalogtoKey bridges the gap:
1. Hides the controllers from all other applications (via the HidHide driver)
2. Reads all input directly itself
3. Sends the correct keystrokes to the active game window

Unlike TSW-specific tools, AnalogtoKey works at the Windows level — it sends real keystrokes, so it works with any simulator or game that supports keyboard input, regardless of game engine.

**Known to be a good fit for:**
- Train Sim World 2 / 3 / 4 / 5 / 6 (TSW)
- Train Simulator Classic
- Running Train
- SimRail — The Railway Simulator
- Open Rails
- Derail Valley
- Zusi
- Any other sim with keyboard support but no native analog input

---

## Features

- Map any **button** (1–16) to any keyboard key
- Map all **8 D-pad directions** independently
- Map **analog axes** (X/Y/Z/Rx/Ry/Rz) with three modes:
  - **Steps Mode** — axis sends a keypress per step (up to 99 steps, MIN/MAX calibration)
  - **Center Mode** — splits axis at centre with a configurable dead zone; throttle and brake get independent step counts (e.g. 9 throttle + 9 brake = 19 positions)
  - **Constant Pressure** — holds a key down while the axis is past the dead zone, releases when back to neutral
  - Modes can be combined freely
- **Live raw axis value** — shows the raw axis reading in the editor (`raw:30942`) to help locate centre points and dial in dead zones
- **Multiple named profiles** — New / Copy / Rename / Delete via the Edit dropdown
- **Unsaved changes protection** — save indicator (`●`) and prompt before discarding edits
- **Scan for new devices** — re-detect controllers without restarting the app
- **System tray** — X button minimises to tray by default; configurable toggle to close directly instead
- **HidHide integration** — automatically hides controllers on start, restores on exit
- PDF user guide included

---

## Installation

1. Download `AnalogtoKey_Setup_v0.3.3.exe` from [Releases](https://github.com/Gallinarius/AnalogtoKey/releases/latest)
2. Run it (administrator rights may be required)
3. The installer offers to install the **HidHide** driver — recommended for Running Train and similar games
4. Restart Windows if HidHide was installed
5. Launch AnalogtoKey from the Start Menu or desktop shortcut

### Do I need HidHide?

| Situation | HidHide needed? |
|---|---|
| Game forces gamepad mode (e.g. Running Train) | **Yes** — without it you get double input |
| Game lets you disable controller input in its own settings | No |

HidHide is a free, open-source kernel driver. It is bundled in the installer and only needs to be installed once. AnalogtoKey manages the whitelist automatically — your controllers are restored when you exit the app.

---

## Compatible devices

AnalogtoKey works with any controller that exposes itself as a standard **DirectInput joystick** on Windows.

| Device | Status |
|---|---|
| HORI Fighting Stick Mini | ✅ Confirmed working |
| Simworkshop TSC-X | ✅ Confirmed working |
| Turtle Beach VelocityOne Flight (throttle quadrant) | 🔄 Expected to work — not yet verified |
| Most USB joysticks and HOTAS systems | 🔄 Expected to work |
| RailDriver Desktop Train Cab Controller | ❌ Not compatible — uses proprietary SDK, not DirectInput |

**Don't see your device?** Try it and let us know if it works via the [Issues page](https://github.com/Gallinarius/AnalogtoKey/issues). Verified devices get added to this list.

---

## Requirements

- Windows 10 or 11 (64-bit)
- USB joystick or arcade stick
- HidHide driver (bundled in installer, optional but recommended)

---

## Hardware tested

- HORI Fighting Stick Mini (×2, simultaneous)

---

## Tech stack

| Component | Technology |
|---|---|
| Language | C# 13 / .NET 10 |
| GUI | WPF |
| Joystick input | SharpDX.DirectInput |
| Keyboard output | Windows SendInput API |
| Device hiding | HidHide (kernel driver) |
| Installer | Inno Setup 6 |

---

## Bug reports & feature requests

Use the [Issues page](https://github.com/Gallinarius/AnalogtoKey/issues) for both bugs and ideas.

**Reporting a bug** — please include:
- What you did
- What you expected to happen
- What actually happened
- Your Windows version and controller model

**Suggesting a feature** — describe what you want and why it would be useful. All suggestions are welcome.

---

## License

This project is released for personal use. HidHide is developed by [Nefarius Software Solutions](https://github.com/nefarius/HidHide) and is subject to its own license.
