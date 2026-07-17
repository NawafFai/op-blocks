; ONE PROCESS Blocks — end-user installer (Inno Setup 6)
; Built by scripts\build-installer.ps1 (stages files into build\stage\app first).

#define AppName "ONE PROCESS Blocks"
#define AppVersion "1.0.0"
#define Publisher "ONE PROCESS Simulation"
#define ExeName "OPBlocksManager.exe"

[Setup]
AppId={{8B1C2A90-7C11-4E9A-9E7E-0A1E4F3C2B01}
AppName={#AppName}
AppVersion={#AppVersion}
AppPublisher={#Publisher}
AppPublisherURL=https://oneprocess.sim
DefaultDirName={autopf}\ONE PROCESS Blocks
DefaultGroupName=ONE PROCESS Blocks
DisableProgramGroupPage=yes
OutputDir=Output
OutputBaseFilename=OPBlocks_Setup
Compression=lzma2
SolidCompression=yes
WizardStyle=modern
PrivilegesRequired=admin
ArchitecturesInstallIn64BitMode=x64
UninstallDisplayIcon={app}\{#ExeName}

[Languages]
Name: "en"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "Create a desktop shortcut"; GroupDescription: "Additional shortcuts:"

[Files]
; Everything staged under build\stage\app (Manager + blocks + templates + scripts + docs).
Source: "..\build\stage\app\*"; DestDir: "{app}"; Flags: recursesubdirs createallsubdirs ignoreversion

[Icons]
Name: "{group}\ONE PROCESS Blocks Manager"; Filename: "{app}\{#ExeName}"
Name: "{group}\Uninstall ONE PROCESS Blocks"; Filename: "{uninstallexe}"
Name: "{autodesktop}\ONE PROCESS Blocks Manager"; Filename: "{app}\{#ExeName}"; Tasks: desktopicon

[Run]
; Register all blocks (x64 + x86) after files are copied. OPBLOCKS_NOWAIT stops
; the script's "Press Enter" prompt from hanging the hidden installer window.
Filename: "cmd.exe"; Parameters: "/c set OPBLOCKS_NOWAIT=1 && powershell.exe -NoProfile -ExecutionPolicy Bypass -File ""{app}\scripts\register-all-blocks.ps1"""; \
    StatusMsg: "Registering ONE PROCESS blocks (x64 + x86)..."; Flags: runhidden waituntilterminated
; Offer to launch the Manager.
Filename: "{app}\{#ExeName}"; Description: "Launch ONE PROCESS Blocks Manager"; Flags: nowait postinstall skipifsilent

[UninstallRun]
; Unregister the blocks on uninstall.
Filename: "cmd.exe"; Parameters: "/c set OPBLOCKS_NOWAIT=1 && powershell.exe -NoProfile -ExecutionPolicy Bypass -File ""{app}\scripts\register-all-blocks.ps1"" -Unregister"; \
    Flags: runhidden waituntilterminated; RunOnceId: "UnregOPBlocks"
