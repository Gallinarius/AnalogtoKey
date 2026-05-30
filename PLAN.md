# AnalogtoKey — Development Plan

## Phases

---

## Phase 0 — Environment and Dependencies ✅ DONE
*Goal: Get everything installed and an empty project running*

1. Install Visual Studio Code + C# Dev Kit extension
2. Install HidHide driver (once, requires admin restart)
3. Create .NET 10 WPF project
4. Add NuGet packages: SharpDX.DirectInput, Hardcodet.NotifyIcon.Wpf, Nefarius.Drivers.HidHide
5. Verify project builds and starts

---

## Phase 1 — Joystick Reading ✅ DONE
*Goal: Detect and read connected joysticks*

1. Implement `InputService` — scans connected joysticks via DirectInput
2. Poll loop reading all axes, buttons and hat switch in real time
3. Support for multiple simultaneous controllers

---

## Phase 2 — HidHide Integration ✅ DONE
*Goal: Hide controllers from all other apps*

1. Implement `HidHideService` — hide/whitelist/restore via HidHide API
2. Auto-hide on app start, auto-restore on app close
3. HidHide status indicator in status bar
4. In-app install button + warning banner if HidHide not installed

---

## Phase 3 — Keyboard Output ✅ DONE
*Goal: Send real Windows keyboard input from joystick events*

1. Implement `KeySender` via Windows `SendInput` API (P/Invoke)
2. Key down (held while input is active) + Key up (released when input stops)
3. Correct struct sizing for 64-bit Windows

---

## Phase 4 — Mapping Engine ✅ DONE
*Goal: Connect joystick input with keyboard output*

1. `MappingProfile` data model (C# class + JSON serialisation)
2. `InputMapper` — translates joystick events to key events
3. Hat switch: 8 directions with sector-based normalisation
4. Buttons: direct 1-to-1 mapping
5. Axes: stepped (1–N steps) with MIN/MAX calibration

---

## Phase 5 — Profile System ✅ DONE
*Goal: Save and load mappings as JSON files*

1. `ProfileManager` — Save / Load / List / Delete profiles
2. Profiles stored in `%AppData%\AnalogtoKey\profiles\`
3. Auto-load last used profile on startup
4. Window position/size persisted in `settings.json`

---

## Phase 6 — GUI ✅ DONE
*Goal: User-friendly visual editor for mappings*

1. Single-controller view with dropdown selector
2. D-pad section, Axes section, Buttons section
3. Key capture: click a button, press a key
4. Profile toolbar: New / Copy / Delete / Save
5. Live input highlighter (active inputs light up in real time)
6. Status bar: controller status, last sent key, HidHide status
7. Axis sliders: 6 vertical bars (X/Y/Z/Rx/Ry/Rz), segmented when Steps > 1

---

## Phase 6c — Pre-release Fixes ✅ DONE

- One-controller-at-a-time UI with dropdown
- Variable axes (1–8 per controller, +/− buttons)
- Copy profile button
- Window position memory
- Cross-thread crash fixes (volatile _selectedGuid)
- English UI translation
- HidHide in-app fallback + warning banner
- Inno Setup installer script

---

## Phase 7 — System Tray ✅ DONE
*Goal: App runs in background with tray icon*

1. X button minimises to tray instead of closing
2. Double-click tray icon to restore window
3. Right-click context menu: Open / Exit
4. App icon on .exe and window

---

## Phase 8 — Polish and Testing ⏳ PENDING
*Goal: Stable, production-ready application*

1. Full test with Running Train
2. Hot-plug test (stick disconnected/reconnected while running)
3. Error message if no devices found
4. Clean up any remaining debug code

---

## Phase 9 — Distribution ⏳ IN PROGRESS
*Goal: Distributable installer and GitHub release*

1. ✅ Release build (self-contained, no .NET required)
2. ✅ Inno Setup installer (bundles HidHide as optional component)
3. GitHub repository + README
4. GitHub Release with installer as asset

---

## Phase 10 — Documentation ⏳ PENDING
*Goal: User guide with screenshots*

1. PDF manual covering installation, interface, profile setup, axes
