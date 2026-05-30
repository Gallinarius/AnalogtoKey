# Build AnalogtoKey installer

## Requirements
1. [Inno Setup 6.x](https://jrsoftware.org/isinfo.php)
2. `HidHide_Installer.exe` placed in `installer\` folder
   — download from: https://github.com/nefarius/HidHide/releases/latest

## Steps

### 1. Build AnalogtoKey release
```
cd src
dotnet publish -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true
```
Output: `src\bin\Release\net10.0-windows\publish\AnalogtoKey.exe`

### 2. Compile installer
Open `JoyMap_Setup.iss` in Inno Setup → **Build → Compile** (Ctrl+F9)

Output: `installer\output\AnalogtoKey_Setup_v0.1_Beta.exe`

## What the installer does
- Installs `AnalogtoKey.exe` (self-contained, no .NET required)
- **HidHide** (pre-selected, optional): bundled in the installer, removed after install
  — required for games that cannot disable controller input (e.g. Running Train)
  — not needed if the game itself can disable controllers (e.g. TDW6)
- Start menu shortcut + optional desktop shortcut

## In-app fallback
If the user skips HidHide during installation:
AnalogtoKey shows a red warning banner with an "Install HidHide" button
that downloads and runs the installer directly from GitHub.
