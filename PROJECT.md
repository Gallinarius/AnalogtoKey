# AnalogtoKey — Project Document

## Purpose

AnalogtoKey is a local Windows application that captures input from joysticks and arcade sticks and converts it to keyboard input. The app hides controllers completely from other programs via the HidHide driver, so games like **Running Train** do not detect them as Xbox controllers and misinterpret the input.

---

## The Problem It Solves

Running Train (and similar games) recognise the HORI Fighting Stick Mini as an Xbox game controller and process axes/buttons according to their own interpretation — causing incorrect behaviour. The game supports keyboard input correctly, so the solution is to:

1. Hide the sticks from all other apps (via HidHide kernel driver)
2. Capture all input directly in AnalogtoKey
3. Send the correct keystrokes to the active game

---

## Hardware

- **2x HORI Fighting Stick Mini** (USB, HID-compatible)
- Both active simultaneously
- Both with full, individual button mapping

---

## Tech Stack

| Component | Technology | Reason |
|---|---|---|
| Language | C# 13 | Native Windows, strong HID support |
| Runtime | .NET 10 (self-contained) | No installation required by end user |
| GUI framework | WPF (Windows Presentation Foundation) | Professional Windows GUI |
| Joystick input | SharpDX.DirectInput | Direct joystick reading |
| Keyboard output | Windows SendInput API (P/Invoke) | Reliable, low latency |
| Device hiding | HidHide (kernel-mode filter driver) | Industry standard |
| Config/profiles | JSON (System.Text.Json) | Simple, human-readable |
| System tray | Hardcodet.NotifyIcon.Wpf | Runs in background |
| Installer | Inno Setup 6.x | Professional Windows installer |

---

## Architecture — Components

```
┌─────────────────────────────────────────────────────┐
│                 AnalogtoKey.exe                      │
│                                                      │
│  ┌─────────────┐    ┌──────────────┐                │
│  │  GUI / WPF  │◄──►│ProfileManager│               │
│  │  (Editor)   │    │ (JSON files) │                │
│  └──────┬──────┘    └──────────────┘                │
│         │                                            │
│  ┌──────▼──────────────────────────┐                │
│  │         InputService            │                │
│  │  (polls all connected sticks)   │                │
│  └──────┬───────────────┬──────────┘                │
│         │               │                            │
│  ┌──────▼──────┐ ┌──────▼──────┐                   │
│  │  Stick #1   │ │  Stick #2   │                   │
│  │  Mapper     │ │  Mapper     │                   │
│  └──────┬──────┘ └──────┬──────┘                   │
│         └───────┬────────┘                           │
│          ┌──────▼──────┐                            │
│          │  KeySender  │                            │
│          │(SendInput)  │                            │
│          └─────────────┘                            │
│                                                      │
│  ┌─────────────────────┐                            │
│  │   HidHideService    │  (hides controllers)       │
│  └─────────────────────┘                            │
└─────────────────────────────────────────────────────┘
```

---

## Mapping Model

Each input on a stick can be mapped to:
- A single keystroke (e.g. `W`, `Space`, `F1`)
- No action (ignored)

### Input types mapped:
| Input | Description |
|---|---|
| Axes (X, Y, Z, Rx, Ry, Rz) | Analog → stepped digital (1–N steps) |
| D-pad (hat switch) 8 directions | Direct mapping per direction |
| Buttons 1–16 | Per-button mapping |

---

## Profile System

- Profiles stored as JSON files in `%AppData%\AnalogtoKey\profiles\`
- One profile = one set of mappings for all connected controllers
- Examples: `Running Train.json`, `Default.json`
- Profiles are per-controller (keyed by device GUID)

---

## HidHide — Device Hiding

AnalogtoKey uses **HidHide** (free, open source kernel driver) to hide controllers from all other applications. Only AnalogtoKey itself is whitelisted and can read them.

- HidHide is installed once as a driver (requires admin + restart)
- AnalogtoKey manages the whitelist automatically via the HidHide API
- When AnalogtoKey closes, controllers are removed from the whitelist (visible again)

**When HidHide is needed:** Games that always read all connected controllers and cannot disable it (e.g. Running Train) — without HidHide, double input occurs.

**When HidHide is NOT needed:** Games where controller input can be disabled directly (e.g. TDW6).

---

## Version History

| Version | Description |
|---|---|
| 0.1 Beta | Initial release — joystick reading, HidHide, keyboard output, profiles, GUI |
| 0.2 Beta | Renamed to AnalogtoKey, English UI, system tray, app icon, installer |
