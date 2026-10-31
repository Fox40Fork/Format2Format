; Inno Setup script for Format2Format (Inno Setup 6.3 or newer).
;
; Build the app first, then compile this script:
;   dotnet publish src\Format2Format -p:PublishProfile=win-x64
;   "%ProgramFiles(x86)%\Inno Setup 6\ISCC.exe" installer\Format2Format.iss
; The setup program is written to dist\.
;
; Installs per user (no admin rights) into %LOCALAPPDATA%\Programs\Format2Format. The right-click menu
; entry lives in HKCU too, so everything belongs to the user who installed it.

#define AppName "Format2Format"
#define AppExe "Format2Format.exe"
#ifndef SourceDir
  #define SourceDir "..\publish\win-x64"
#endif
#ifndef AppVersion
  #define AppVersion GetStringFileInfo(AddBackslash(SourceDir) + AppExe, "ProductVersion")
#endif

[Setup]
AppId={{8F3C2B1E-5A7D-4C6E-9B2F-1D4E6A8C0F35}
AppName={#AppName}
AppVersion={#AppVersion}
AppVerName={#AppName} {#AppVersion}
AppPublisher={#AppName}
VersionInfoVersion={#AppVersion}
DefaultDirName={autopf}\{#AppName}
DefaultGroupName={#AppName}
DisableProgramGroupPage=yes
PrivilegesRequired=lowest
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
MinVersion=10.0
OutputDir=..\dist
OutputBaseFilename=Format2Format-{#AppVersion}-setup
SetupIconFile=..\src\Format2Format\app.ico
UninstallDisplayIcon={app}\{#AppExe}
UninstallDisplayName={#AppName}
Compression=lzma2/max
SolidCompression=yes
WizardStyle=modern
; Close a running Format2Format before upgrading or uninstalling.
CloseApplications=yes
RestartApplications=no

[Tasks]
Name: "contextmenu"; Description: "Add ""Convert with Format2Format"" to the right-click menu for image files"; GroupDescription: "Explorer integration:"
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"; Flags: unchecked

[Files]
Source: "{#SourceDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{autoprograms}\{#AppName}"; Filename: "{app}\{#AppExe}"
Name: "{autodesktop}\{#AppName}"; Filename: "{app}\{#AppExe}"; Tasks: desktopicon

[Run]
; The app writes the registry keys itself, so the installer and Settings > "Add/Remove right-click menu entry"
; always produce exactly the same entries.
Filename: "{app}\{#AppExe}"; Parameters: "--register-shell"; Tasks: contextmenu; Flags: runhidden waituntilterminated; StatusMsg: "Adding the right-click menu entry..."
Filename: "{app}\{#AppExe}"; Parameters: "--unregister-shell"; Tasks: not contextmenu; Flags: runhidden waituntilterminated
Filename: "{app}\{#AppExe}"; Description: "{cm:LaunchProgram,{#AppName}}"; Flags: nowait postinstall skipifsilent

[UninstallRun]
Filename: "{app}\{#AppExe}"; Parameters: "--unregister-shell"; Flags: runhidden waituntilterminated; RunOnceId: "UnregisterShell"

[Registry]
; Safety net for uninstall: delete the right-click entries even if the app couldn't run above.
; "dontcreatekey" means setup never creates them; the app does (see [Run]).
Root: HKCU; Subkey: "Software\Classes\SystemFileAssociations\.jpg\shell\Format2Format"; Flags: dontcreatekey uninsdeletekey
Root: HKCU; Subkey: "Software\Classes\SystemFileAssociations\.jpeg\shell\Format2Format"; Flags: dontcreatekey uninsdeletekey
Root: HKCU; Subkey: "Software\Classes\SystemFileAssociations\.jfif\shell\Format2Format"; Flags: dontcreatekey uninsdeletekey
Root: HKCU; Subkey: "Software\Classes\SystemFileAssociations\.png\shell\Format2Format"; Flags: dontcreatekey uninsdeletekey
Root: HKCU; Subkey: "Software\Classes\SystemFileAssociations\.webp\shell\Format2Format"; Flags: dontcreatekey uninsdeletekey
Root: HKCU; Subkey: "Software\Classes\SystemFileAssociations\.bmp\shell\Format2Format"; Flags: dontcreatekey uninsdeletekey
Root: HKCU; Subkey: "Software\Classes\SystemFileAssociations\.gif\shell\Format2Format"; Flags: dontcreatekey uninsdeletekey
Root: HKCU; Subkey: "Software\Classes\SystemFileAssociations\.tif\shell\Format2Format"; Flags: dontcreatekey uninsdeletekey
Root: HKCU; Subkey: "Software\Classes\SystemFileAssociations\.tiff\shell\Format2Format"; Flags: dontcreatekey uninsdeletekey
Root: HKCU; Subkey: "Software\Classes\SystemFileAssociations\.ico\shell\Format2Format"; Flags: dontcreatekey uninsdeletekey
Root: HKCU; Subkey: "Software\Classes\SystemFileAssociations\.heic\shell\Format2Format"; Flags: dontcreatekey uninsdeletekey
Root: HKCU; Subkey: "Software\Classes\SystemFileAssociations\.heif\shell\Format2Format"; Flags: dontcreatekey uninsdeletekey
Root: HKCU; Subkey: "Software\Classes\SystemFileAssociations\.avif\shell\Format2Format"; Flags: dontcreatekey uninsdeletekey

[UninstallDelete]
; Log and remembered settings.
Type: filesandordirs; Name: "{localappdata}\Format2Format"
