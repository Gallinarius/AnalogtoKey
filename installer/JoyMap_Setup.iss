; AnalogtoKey Installer — Inno Setup Script
; Kræver: Inno Setup 6.x (https://jrsoftware.org/isinfo.php)
;
; Inden build:
;   1. Placer HidHide_1.5.230_x64.exe i denne mappe (installer\)
;   2. Byg AnalogtoKey release:
;      cd src
;      dotnet publish -c Release -r win-x64 --self-contained true

#define MyAppName      "AnalogtoKey"
#define MyAppVersion   "0.4"
#define MyAppPublisher "AnalogtoKey"
#define MyAppURL       "https://github.com/YOUR_USERNAME/AnalogtoKey"
#define MyAppExeName   "AnalogtoKey.exe"
#define MyAppPublishDir "..\src\bin\Release\net10.0-windows\win-x64\publish"

[Setup]
AppId={{F4E7D8C9-3B2A-4F1E-8D7C-6B5A4E3D2C1B}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppPublisher={#MyAppPublisher}
AppPublisherURL={#MyAppURL}
AppSupportURL={#MyAppURL}
DefaultDirName={autopf64}\{#MyAppName}
DefaultGroupName={#MyAppName}
AllowNoIcons=yes
OutputDir=output
OutputBaseFilename=AnalogtoKey_Setup_v0.4
Compression=lzma
SolidCompression=yes
WizardStyle=modern
PrivilegesRequired=admin
RestartIfNeededByRun=yes
ArchitecturesInstallIn64BitMode=x64

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[Types]
Name: "full";   Description: "Full installation (recommended)"
Name: "custom"; Description: "Custom installation"; Flags: iscustom

[Components]
Name: "analogtokey"; Description: "AnalogtoKey";                                                                                          Types: full custom; Flags: fixed
Name: "hidhide";     Description: "HidHide driver (recommended) — hides your controller from games that cannot disable it (e.g. Running Train)"; Types: full

[Tasks]
Name: "desktopicon"; Description: "Create desktop shortcut"; GroupDescription: "Shortcuts:"

[Files]
; AnalogtoKey — all files from publish folder (self-contained, no .NET required)
Source: "{#MyAppPublishDir}\*"; DestDir: "{app}"; Components: analogtokey; Flags: ignoreversion recursesubdirs createallsubdirs

; PDF user guide
Source: "..\AnalogtoKey_UserGuide.pdf"; DestDir: "{app}"; Components: analogtokey; Flags: ignoreversion

; HidHide installer — bundled, removed after install
Source: "HidHide_1.5.230_x64.exe"; DestDir: "{tmp}"; Components: hidhide; Flags: ignoreversion deleteafterinstall

[Icons]
Name: "{group}\{#MyAppName}";          Filename: "{app}\{#MyAppExeName}"
Name: "{group}\Uninstall AnalogtoKey"; Filename: "{uninstallexe}"
Name: "{autodesktop}\{#MyAppName}";    Filename: "{app}\{#MyAppExeName}"; Tasks: desktopicon

[Run]
; Run HidHide installer (shows normal UI, may require restart)
Filename: "{tmp}\HidHide_1.5.230_x64.exe"; Components: hidhide; \
    Flags: waituntilterminated; \
    StatusMsg: "Installing HidHide driver..."; \
    Description: "Install HidHide driver"

; Launch AnalogtoKey after install
Filename: "{app}\{#MyAppExeName}"; \
    Description: "Launch AnalogtoKey"; \
    Flags: nowait postinstall skipifsilent
