# AnalogtoKey — TODO List

Status key: `[ ]` = not started · `[→]` = in progress · `[x]` = done · `[-]` = postponed

---

## PHASE 0 — Environment

- [x] Install Visual Studio Code + C# Dev Kit
- [x] Install HidHide driver
- [x] Create .NET 10 WPF project
- [x] Add NuGet: SharpDX.DirectInput, Hardcodet.NotifyIcon.Wpf, Nefarius.Drivers.HidHide
- [x] Verify project builds and starts

---

## PHASE 1 — Joystick Reading

- [x] Create `Services/InputService.cs`
- [x] Scan and list all connected DirectInput devices
- [x] Implement polling loop for axes and buttons
- [x] Implement polling for hat switch (D-pad)
- [x] Test with both sticks connected

---

## PHASE 2 — HidHide Integration

- [x] HidHide driver installed on system
- [x] Add NuGet: Nefarius.Drivers.HidHide 3.4.0
- [x] Create `Services/HidHideService.cs`
- [x] Implement `HideDevices()` — hides all connected controllers
- [x] Implement `WhitelistSelf()` — adds AnalogtoKey.exe to whitelist
- [x] Implement `RestoreDevices()` — removes hiding on close
- [x] HidHide status indicator in status bar
- [x] **HidHide test** — verified all controllers hidden correctly

---

## PHASE 3 — Keyboard Output

- [x] Create `Services/KeySender.cs`
- [x] Implement `SendKeyDown` / `SendKeyUp`
- [x] Test in Notepad: stick movement → letter typed

---

## PHASE 4 — Mapping Engine

- [x] Create `Models/MappingProfile.cs`
- [x] Create `Services/InputMapper.cs`
- [x] 8-direction hat switch mapping
- [x] Button mapping (1-to-1)
- [x] Stepped axis mapping with MIN/MAX calibration

---

## PHASE 5 — Profile System

- [x] Create `Services/ProfileManager.cs`
- [x] Save / Load / List / Delete profiles
- [x] Auto-create `%AppData%\AnalogtoKey\profiles\`
- [x] Auto-load last used profile on startup
- [x] Persist window position/size in settings.json

---

## PHASE 6 — GUI

- [x] Single-controller layout with dropdown selector
- [x] D-pad, Axes, Buttons sections
- [x] Key capture: click and press a key
- [x] Profile toolbar: New / Copy / Delete / Save
- [x] Live input highlighter
- [x] Status bar: controller status + last sent key + HidHide status
- [x] Axis sliders (6 bars, segmented when Steps > 1)
- [x] Variable axes (1–8, +/− buttons, rename via text field)

---

## PHASE 6c — Pre-release Fixes

- [x] New UI: one controller at a time with dropdown
- [-] Generic joystick support — postponed to v0.3
- [x] Copy profile button
- [x] Variable axes (1–8 per controller)
- [x] Window position memory
- [x] Bugfix: volatile _selectedGuid
- [x] Status bar live info
- [x] Axis sliders with click-to-assign
- [x] Translate UI to English
- [x] HidHide in-app install button + warning banner
- [x] Inno Setup installer script

---

## PHASE 7 — System Tray ✅ DONE

- [x] App icon (app.ico) on .exe and window
- [x] X button minimises to tray instead of closing
- [x] Double-click tray icon restores window
- [x] Right-click context menu: Open / Exit

---

## PHASE 8 — Polish and Testing

- [x] Full test with Running Train — verify correct input
- [x] Hot-plug test: stick disconnected and reconnected while app runs
- [x] Error message if no devices found at startup
- [x] Clean up any remaining debug code
- [x] Bug fix: Steps textbox does not rebuild axis slider on Enter/Tab — add LostFocus + PreviewKeyDown(Enter) handlers to call RebuildMappingUI()
- [x] Bug fix: Multiple instances can run simultaneously — add named Mutex in App.xaml.cs to enforce single instance (show message + Shutdown if already running)

---

## PHASE 9 — Distribution

- [x] Release build (self-contained, no .NET required)
- [x] Inno Setup installer (HidHide bundled as optional component)
- [x] App renamed to AnalogtoKey, version 0.2 Beta
- [x] GitHub repository (git init, .gitignore, initial commit, push)
- [x] Publish as GitHub Release with installer as asset
- [x] README.md: what is AnalogtoKey, requirements, download, screenshot
- [x] HidHide explanation in README (when needed vs. not needed)

---

## PHASE 10 — Documentation

- [x] PDF user manual with screenshots:
  - What is AnalogtoKey and when to use it
  - Installation (AnalogtoKey + HidHide)
  - The interface overview
  - Setting up a profile
  - Setting up axes (throttle/brake with steps)
  - Tips & tricks (incl. reverse axis, shorten travel zone)
- [x] "Read Manual" button in app — opens AnalogtoKey_UserGuide.pdf from install folder
- [x] Include PDF in Inno Setup installer (copy to install folder alongside .exe)

---

## Backlog (future versions)

- [ ] Auto-profile switch based on active window title
- [ ] Macro support (one press → sequence of keys)
- [ ] Turbo mode (rapid-fire)
- [ ] Mouse emulation
- [ ] Import/export profiles (zip file)
- [ ] Generic joystick support (read actual button count from device)
