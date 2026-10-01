; Inno Setup script (https://jrsoftware.org/isinfo.php). Run build\publish.ps1 first, then compile this file.
#define AppName "Cycling Training Planner"
#define AppVersion "0.1.0"

[Setup]
AppId={{7B0C8D0E-6C55-4D3E-9C8A-3E4E2B1F6A21}
AppName={#AppName}
AppVersion={#AppVersion}
DefaultDirName={autopf}\Training Planner
DefaultGroupName={#AppName}
OutputDir=..\publish
OutputBaseFilename=TrainingPlannerSetup-{#AppVersion}
Compression=lzma2
SolidCompression=yes
ArchitecturesInstallIn64BitMode=x64compatible
PrivilegesRequired=lowest
; Data lives in %LOCALAPPDATA%\Trainer and is never touched by install or uninstall.

[Files]
Source: "..\publish\Trainer.exe"; DestDir: "{app}"; Flags: ignoreversion

[Icons]
Name: "{group}\{#AppName}"; Filename: "{app}\Trainer.exe"
Name: "{autodesktop}\{#AppName}"; Filename: "{app}\Trainer.exe"; Tasks: desktopicon

[Tasks]
Name: "desktopicon"; Description: "Create a desktop shortcut"; Flags: unchecked

[Run]
Filename: "{app}\Trainer.exe"; Description: "Start {#AppName}"; Flags: nowait postinstall skipifsilent
