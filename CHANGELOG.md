# Changelog

All notable changes to AnalogtoKey are documented here.

---

## [0.45] — 2026-06-09

### Added
- **Per-axis Key Hold** — `KeyHoldMs` is now configurable per axis (default 50 ms) instead of globally. Each axis shows its own "Hold: X ms" field in the editor. Old profiles default to 50 ms per axis.
- **Button modifier keys** — each button mapping can now have a modifier key (Ctrl / Shift / Alt) via a dropdown in the button row. The modifier is held down before the key and released after — e.g. Button 1 → Ctrl+A. Modifier is stored separately so existing profiles are fully backward-compatible.

---

## [0.4] — 2026-06-08

### Added
- **Stacked mode** (Steps Mode) — optional per-axis queue that ensures keypresses are never dropped when the stick moves faster than `KeyHoldMs` allows. Instead of firing concurrent fire-and-forget tasks, keypresses are placed in a FIFO queue and sent sequentially — preventing step desync between stick position and simulator. Configurable inter-keypress pause (`Pause ms`, 0 = same as hold duration). Enabled per axis via the "Stacked mode" checkbox in the axis editor.

---

## [0.3.5] — 2026-06-04

### Fixed
- **Flight controller detection** — devices that register as `DeviceType.Flight` in DirectInput (e.g. Honeycomb Bravo Throttle Quadrant, Saitek/Logitech X-55 Throttle) were not detected by the device scan. The scan now uses `DeviceClass.GameControl` which covers all game input devices — joysticks, gamepads, flight controllers, racing wheels, and more. These devices were visible in Windows `joy.cpl` but missing from AnalogtoKey's device dropdown.
- **Floating windows always-on-top** — Axis Monitor and Debug Window now call `SetWindowPos(HWND_TOPMOST)` via P/Invoke on `SourceInitialized` and `Deactivated`, ensuring they stay on top of borderless fullscreen games that override WPF's `Topmost` property.

---

## [0.3.41] — 2026-06-03

### Added
- **Floating Axis Monitor window** — detachable axis monitor (≡ menu → Detach Axis Monitor). Always-on-top, custom dark titlebar, draggable, remembers last position and size. When open, bars disappear from main window and show in the monitor instead.
- **Axis bars in main window** — axis visualisation bars moved permanently to bottom-right panel below buttons, fixed 180px height, scales with window width via Viewbox.
- **Axis visual feedback** — axis key buttons now blink green on every step boundary crossed (Steps mode) or stay solid green while held in zone (CP mode). Blinks even when muted.
- **Transmit ON/OFF toggle** — `● LIVE` / `● MUTED` button in toolbar. When muted, no keys are sent to the OS; keys that *would* have fired still appear in the Debug Window in red (`[M]`). Toggle is instant and releases all held keys on mute.
- **Device capability scan** — on connect, AnalogtoKey queries DirectInput for which axes are physically present (X/Y/Z/Rx/Ry/Rz/Slider0/Slider1) and exact button count. UI shows only what the device actually has — no empty rows.
- **Slider axes (Slider0 / Slider1)** — polled and displayed alongside standard axes. Verified working with Turtle Beach VelocityOne Yoke and Throttle Quadrant.
- **32-button support with paging** — button mapping now covers buttons 1–32. Devices with more than 16 buttons show page tabs (1–16 / 17–32). Only buttons the device actually has are shown.
- **End-of-travel keypresses (MaxKey / MinKey)** — optional keys that fire once as a pulse when the axis first reaches its maximum or minimum step. Center key (`Center ○`) lives in the "End of travel" section. Order: Max key ▲ → Center ○ → Min key ▼.
- **Version label** — build version shown in bottom-right corner of app window, reads directly from assembly.
- **Open Debug Window** in ≡ app menu — debug window is always-on-top and floats over borderless windowed games.
- **User guide as Markdown** — `AnalogtoKey_UserGuide.md` added to repo for easy text editing.

### Fixed
- **ComboBox keyboard block** — controller keystrokes (e.g. arrow keys) no longer affect Profile or Controller dropdowns when they have focus.
- **Axis blink only fired once** — blink logic now correctly triggers on every step change in both directions, including when moving upward from below center.
- **Window position not remembered** — added `WindowStartupLocation="Manual"` to XAML; Windows no longer overrides saved Left/Top values.
- **CP highlight orange when unassigned** — CP zone buttons (Hold Up / Hold Down) now only light green when a key is actually assigned AND the zone is active.
- **Axis step keys light green** — Throttle/Brake buttons now highlight green based on current axis zone position, not only on key-sent events.
- **Axis UI cleanup** — mode checkboxes (Steps Mode / Center / Const. Pressure) always visible. Dead zone, CP keys, and End of travel section appear conditionally when relevant.

---

## [0.3.4] — 2026-06-01

### Fixed
- **CP priority over steps** — when Constant Pressure and Steps Mode are both active, CP now suppresses steps for any direction where a CP key is assigned. If the CP key for a direction is empty, steps fire as normal (fallback). This enables mixed setups, e.g. throttle up = CP hold, brake down = 3 steps.

---

## [0.3.3] — 2026-06-01

### Added
- **Toolbar redesigned** — 6 individual buttons replaced with two dropdown menus:
  - **Edit ▾** (next to profile selector): New profile / Copy profile / Rename profile / Delete profile
  - **≡** (top-right): Scan for new devices / Minimize to tray on close / Read Documentation / Exit
- **Scan for new devices** — re-detects all connected controllers without restarting the app; useful when a throttle quadrant or stick is unplugged and replugged
- **Minimize to tray on close** — configurable toggle in the ≡ menu, persisted in settings.json; enabled by default (existing behaviour). When disabled, the X button closes the app directly
- **Unsaved changes warning** — Save button shows `●` when the current profile has unsaved edits; switching profiles, creating, copying, renaming or deleting a profile prompts before discarding unsaved changes
- **Live raw axis value** — the axis editor now shows the raw axis reading alongside step info (e.g. `Step:2/5   raw:30942`), making it easier to locate the centre point and set dead zones

---

## [0.3.1] — 2026-06-01

### Fixed
- Dead zone max raised from 49% to 95% (values above 49% were silently rejected)
- InputDialog OK/Cancel buttons clipped due to window height being too small (150→170px)
- Center mode steps column too narrow — arrow buttons hidden behind key button (76→100px)

### Changed
- Dead zone default lowered from 10% to 5%
- "Standard" axis mode renamed to "Steps Mode (Standard)"

### Added
- Spinner fields (▲▼ RepeatButtons) on all numeric inputs: dead zone %, Steps, Throttle steps, Brake steps — unified control with shared border

---

## [0.3] — 2026-05-31

### Added
- **Three axis modes** — selectable via checkboxes, freely combinable:
  - **Steps Mode (Standard)** — pulses Up/Down key per step change, up to 99 steps
  - **Center Mode** — splits axis at midpoint with configurable dead zone (1–95%); independent step counts for throttle (up) and brake (down)
  - **Constant Pressure** — holds a key while axis exceeds dead zone, releases on return to neutral
- Bi-directional axis bar visualisation for Center mode (green = throttle, grey = neutral, red = brake)
- Mode-aware step display: ▲2/9, ▼3/5, neutral, HOLD▲, HOLD▼
- Backwards-compatible JSON loading (old "Steps" field maps to StepsUp)

---

## [0.2.1] — 2026-05-30

### Fixed
- HidHide exclusive-access error (ACCESS_DENIED / Win32 error 5) when HidHide Configuration Client is open — now shows Retry/Cancel dialog instead of silently failing

### Added
- Rename profile button in toolbar
- DebugKeyWindow (hidden debug tool for monitoring key events system-wide)

---

## [0.2 Beta] — 2026-05

### Added
- App renamed from JoyMap to AnalogtoKey
- Full English UI
- System tray — X button minimises, double-click restores, right-click menu
- App icon (.ico) on executable and window title
- Inno Setup installer bundling HidHide driver
- PDF user guide (generated via QuestPDF)
- "Read Manual" button opens PDF from install folder
- Copy profile button
- Variable axes (1–8 per controller, +/− buttons, rename)
- Window position/size remembered between sessions
- HidHide in-app install button + warning banner when not installed

---

## [0.1 Beta] — 2026-04

### Added
- Initial release
- Joystick/arcade stick reading via SharpDX.DirectInput
- HidHide integration — hides controllers from all other apps
- Keyboard output via Windows SendInput API
- Stepped axis mapping with MIN/MAX calibration
- D-pad (8 directions) and button mapping
- Multiple named profiles (JSON, per-device-GUID)
- Live input highlighter in UI
- Status bar with controller status and last sent key
