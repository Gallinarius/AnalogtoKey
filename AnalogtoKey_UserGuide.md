# AnalogtoKey — User Guide

> Map joystick & arcade stick buttons to keyboard keys
> Version 0.3.3 — Windows 10 / 11

---

## Contents

1. [What is AnalogtoKey?](#1-what-is-analogtokey)
2. [Installation](#2-installation)
3. [The Interface](#3-the-interface)
4. [Setting up a Profile](#4-setting-up-a-profile)
5. [Setting up Axes](#5-setting-up-axes)
6. [Tips & Tricks](#6-tips--tricks)

---

## 1 What is AnalogtoKey?

AnalogtoKey is a small Windows utility that reads input from joysticks and arcade sticks and converts every button press, D-pad direction, and axis movement into real keyboard keystrokes — exactly as if you had pressed a key on your keyboard.

### The problem it solves

Some games — such as Running Train — detect any connected USB controller and try to interpret it as an Xbox gamepad, causing incorrect or doubled input. These games do support keyboard input correctly. AnalogtoKey bridges the gap:

- Hides the controllers from all other applications using the HidHide kernel driver
- Reads all input directly itself
- Sends the correct keystrokes to the active game window

### When do you need it?

| Scenario | Why |
|---|---|
| Running Train / similar sims | Game forces gamepad mode on connected sticks — use AnalogtoKey |
| HORI Fighting Stick Mini | Detected as Xbox controller — causes wrong axis behaviour |
| Two sticks at once | Full individual mapping for each controller simultaneously |
| TDW6 and similar | Not needed — controller input can be disabled in-game |

### Key features

- Map any button (1–16) to any keyboard key
- Map all 8 D-pad directions independently
- Map analog axes (X/Y/Z/Rx/Ry/Rz) with three modes: Steps, Center, and Constant Pressure
- Multiple named profiles — switch instantly between games
- Unsaved changes protection with visual indicator on the Save Profile button
- Scan for new devices without restarting the app
- Runs in the system tray — always available, never in the way (configurable)
- Automatic HidHide management — hides on start, restores on exit

---

## 2 Installation

### Requirements

| | |
|---|---|
| OS | Windows 10 or Windows 11 (64-bit) |
| .NET runtime | None — the installer is self-contained |
| HidHide driver | Required for device hiding (bundled in installer) |
| USB ports | One per controller |

### Installer walkthrough

1. Run `AnalogtoKey_Setup.exe` — right-click and **Run as administrator** if prompted.
2. **Choose components** — the installer offers to install HidHide (the device-hiding driver) as an optional component. Strongly recommended for use with Running Train.
3. **Complete the installation** — files are copied to `C:\Program Files\AnalogtoKey\`.
4. **Restart Windows** if you installed HidHide — the kernel driver requires a reboot.
5. **Launch AnalogtoKey** from the Start Menu or desktop shortcut.

### About HidHide

HidHide is a free, open-source Windows kernel driver that hides USB HID devices from selected applications. AnalogtoKey uses it to make your controllers invisible to every other program while AnalogtoKey itself continues to read them.

When AnalogtoKey starts, it automatically adds itself to the HidHide whitelist and hides all detected controllers. When AnalogtoKey exits, it removes itself — controllers become visible again immediately.

> **HidHide not installed?**
> If HidHide is missing, AnalogtoKey shows a yellow warning banner with an Install button. Click it and follow the prompts. A restart is required after installation.

### Uninstall

Use **Windows Settings > Apps > AnalogtoKey > Uninstall**. Profiles are stored separately in `%AppData%\AnalogtoKey\profiles\` and are not removed automatically — delete that folder manually if desired.

---

## 3 The Interface

The main window is divided into six areas. The screenshot below shows a fully configured profile with one controller connected.

*Figure 1 — AnalogtoKey main window with a profile loaded*

### Area overview

| Area | Description |
|---|---|
| **(1) Toolbar** | Left side: Profile drop-down showing the active profile name. The **Edit ▾** button opens a menu: New profile / Copy profile / Rename profile / Delete profile. Right side: **Save Profile** button — shows a dot (●) when you have unsaved changes. The **≡** application menu contains: Scan for new devices, Minimize to tray on close toggle, Read Documentation, and Exit. |
| **(2) Controller selector** | Drop-down listing all connected controllers. The green dot confirms the device is active. Only the selected controller's mappings are shown below. Use **≡ → Scan for new devices** if a controller does not appear after being plugged in. |
| **(3) Status bar** | Three live indicators: number of active devices, the last key sent, and HidHide status (Active / Inactive). |
| **(4) D-PAD section** | Eight direction slots — Up, Up-Right, Right, Down-Right, Down, Down-Left, Left, Up-Left. Click any slot then press a key to assign. Click **x** to clear. |
| **(5) Axes section** | Six vertical bars visualise live axis position (X/Y/Z/Rx/Ry/Rz). The panel below the bars shows the selected axis detail (see Chapter 5). |
| **(6) Buttons section** | 16 button slots. Blue = assigned, dark = unassigned. Click any slot then press a key to assign. |

---

## 4 Setting up a Profile

A profile stores a complete set of mappings — D-pad, buttons, and axes — for all your connected controllers. You can have as many profiles as you like and switch between them instantly.

### Create a new profile

1. Click the **Edit ▾** button next to the profile name, then choose **New profile**.
2. Type a name (e.g. *Running Train*) and press OK.
3. Select your controller in the Controller drop-down.
4. Assign keys to buttons and D-pad directions (see below).
5. Click **Save Profile** to write the profile to disk.

### Assigning a key to a button

1. Locate the button slot in the **KNAPPER** section (Button 1 to Button 16).
2. Click the slot — it turns into a blinking capture field.
3. Press the key you want on your keyboard (e.g. Space, W, F1).
4. The slot shows the captured key name and turns blue.
5. Repeat for each button you want to map.

### Assigning a key to a D-pad direction

The process is identical to buttons. In the **D-PAD** section, click any direction slot (e.g. D-pad Up), then press the desired key. All eight directions can be mapped independently — or left as *(none)* to be ignored.

### Clearing a mapping

Click the **x** button next to any slot to remove its assignment. The slot returns to *(none)* and that input will be ignored.

> **Unsaved changes indicator**
> The Save Profile button shows a dot (●) whenever the current profile has unsaved changes. Switching profiles, creating, copying, renaming or deleting a profile will prompt you before discarding your edits.

### Switching profiles

Open the profile drop-down and select any saved profile. The mappings load immediately and take effect for new input — no restart required.

---

## 5 Setting up Axes

Axes handle analog inputs — throttle levers, joystick tilt, rotary knobs, and pedals. Each physical axis (X, Y, Z, Rx, Ry, Rz) is divided into a number of steps. As the axis moves through each step boundary, AnalogtoKey sends the configured key down or up.

### Add an axis mapping

1. In the **AKSER** section, click **+ Axis**. A new entry appears in the selector drop-down.
2. Give it a descriptive name in the **Name** field (e.g. *Throttle*, *Brake*).
3. Choose the physical axis: AxisX, AxisY, AxisZ, AxisRx, AxisRy, or AxisRz.
4. Set the number of **Steps** (1–8). Steps = 1 means binary on/off. Steps = 5 means five evenly-spaced zones — useful for a throttle lever.
5. Click the **Up ▲** slot and press the key for the positive direction.
6. Click the **Down ▼** slot and press the key for the negative direction.
7. Calibrate the range — see *Calibrating MIN / MAX* below.

### Calibrating MIN / MAX

Raw axis values from DirectInput vary between controllers. Calibration tells AnalogtoKey what value corresponds to the physical minimum and maximum, so step thresholds are calculated correctly.

1. Move the axis to its **minimum** position (fully back, fully released). Click **Capture MIN**.
2. Move the axis to its **maximum** position (fully forward, fully pressed). Click **Capture MAX**.
3. The **Step: N/N** indicator shows which step the axis is in. Move the axis to verify.

> **Step indicator and raw value**
> The step indicator (e.g. Step: 2/5) shows the current step. Next to it, `raw:XXXXX` shows the exact unprocessed axis value from the controller. The raw value is especially useful for finding the exact centre point when configuring Center mode dead zones, or for verifying that the axis is moving as expected.

### How stepped axes work — example with Steps = 5

With Steps = 5, Up key = Z, Down key = A:

| Zone | Behaviour |
|---|---|
| Steps 4–5 (top zone) | Z held down continuously |
| Steps 2–3 (middle zone) | Neither key — neutral |
| Step 1 (bottom zone) | A held down continuously |

### Remove an axis mapping

Select the axis in the drop-down and click **− Remove**. The entry is deleted from the profile. Save the profile afterwards.

---

## 6 Tips & Tricks

> **Copy a profile for a new game**
> Open the profile you want to base the new one on, then click **Copy**. A duplicate is created with a new name. Rename it and adjust the mappings. The original is unchanged.

> **Use descriptive axis names**
> Rename axes to match what they control (Throttle, Brake, Rudder). The name appears in the drop-down, making it easy to select the right axis at a glance.

> **Two controllers simultaneously**
> Both controllers appear in the Controller drop-down. Switch between them to edit each one's mappings within the same profile. Both are active at the same time during use — you only need to switch the selector when editing.

> **Minimise to system tray — or close directly**
> By default, clicking X hides the window to the system tray. AnalogtoKey keeps running and forwarding keystrokes in the background. Double-click the tray icon to restore. To change this, open the **≡** menu and uncheck *Minimize to tray on close* — the X button will then close the app directly. The setting is remembered between sessions.

> **Scan for new devices**
> If you unplug and replug a controller while AnalogtoKey is running, open the **≡** menu and click *Scan for new devices*. AnalogtoKey re-detects all connected controllers and updates HidHide — no restart needed.

> **Profiles auto-load on start**
> AnalogtoKey remembers the last profile you used and loads it automatically at startup. You rarely need to touch the profile selector once it is set up.

> **HidHide is optional for some games**
> If your game lets you disable gamepad input in its own settings, HidHide is not needed. AnalogtoKey still works — it just cannot prevent the game from also seeing raw controller data.

> **Back up your profiles**
> Profiles are stored in `%AppData%\AnalogtoKey\profiles\` as JSON files. Copy that folder to a safe location to back up all your work.

> **Reverse an axis direction**
> AnalogtoKey always maps positive axis movement to the Up key and negative movement to the Down key. If your physical axis is wired the wrong way round (throttle forward fires the wrong key), simply swap the two key assignments: put the key you want for forward in the Down slot and the key for backward in the Up slot. The axis will now behave as expected.

> **Shorten the active travel zone**
> You do not have to calibrate the full physical range. To pack all steps into the first half of the lever travel, move the lever to the bottom, click **Capture MIN**, then move it only halfway and click **Capture MAX**. All steps now fit in that shorter range — pushing past the midpoint has no further effect (the axis stays clamped at the top step). Conversely, set Capture MIN slightly above the resting position to create a dead zone at the start so accidental bumps do not fire a key.

---

*AnalogtoKey v0.3.3 — Profiles: `%AppData%\AnalogtoKey\profiles\`*
