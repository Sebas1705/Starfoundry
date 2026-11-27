; Inno Setup 6 script for the Starfoundry installer. Compile it through tools/release/build-installer.ps1 (which passes the
; defines below), or by hand:
;   ISCC.exe /DAppVersion=0.1.0 /DSourceDir=artifacts\windows\Starfoundry /DOutputDir=artifacts\installer /DIconFile=game\assets\icon\starfoundry.ico tools\release\installer.iss
;
; Behaviour (docs/release.md, ADR 0048):
;  - per-user install by default (no administrator rights, %LOCALAPPDATA%\Programs\Starfoundry); the wizard lets the user pick all users,
;  - Start Menu entry (always), Desktop shortcut (optional task, off by default), an Apps & features uninstall entry,
;  - upgrade in place: same AppId and folder; stale game files of the old version are removed first,
;  - saves and settings live in %APPDATA%\Godot\app_userdata\Starfoundry Belt Prototype and are never touched, not even by the uninstaller,
;  - the wizard follows the Windows display language: Spanish or English, like the game; the licence page shows LICENSE.txt,
;    THIRD-PARTY-NOTICES.txt sits next to the exe and is linked from the Start Menu.
; Silent flags: /VERYSILENT /SUPPRESSMSGBOXES /NORESTART /CURRENTUSER /DIR="C:\path" /TASKS="desktopicon" /LOG="file.log"; uninstall: unins000.exe /VERYSILENT.

#ifndef AppVersion
  #error Pass /DAppVersion=x.y.z
#endif
#ifndef SourceDir
  #error Pass /DSourceDir=<exported game folder>
#endif
#ifndef OutputDir
  #define OutputDir "."
#endif
#ifndef IconFile
  #error Pass /DIconFile=<.ico>
#endif
; Windows version resources need four numbers; the pre-release part of a semantic version is dropped there.
#define NumericVersion Copy(AppVersion, 1, Pos("-", AppVersion + "-") - 1) + ".0"

#define AppName "Starfoundry"
#define AppExe "Starfoundry.exe"

[Setup]
; Never change the AppId: it is how an upgrade finds the installed copy.
AppId={{B3F1D3C2-7A55-4E0B-9C61-5E2F7D0A4B18}
AppName={#AppName}
AppVersion={#AppVersion}
AppVerName={#AppName} {#AppVersion}
AppPublisher=Sebas1705
AppPublisherURL=https://github.com/Sebas1705/Starfoundry
AppSupportURL=https://github.com/Sebas1705/Starfoundry/issues
AppUpdatesURL=https://github.com/Sebas1705/Starfoundry/releases
AppCopyright=Copyright (c) 2026 Sebas1705
VersionInfoVersion={#NumericVersion}
VersionInfoProductVersion={#NumericVersion}
VersionInfoDescription={#AppName} setup
VersionInfoProductName={#AppName}
DefaultDirName={autopf}\{#AppName}
DefaultGroupName={#AppName}
DisableProgramGroupPage=yes
PrivilegesRequired=lowest
PrivilegesRequiredOverridesAllowed=dialog
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
LicenseFile={#SourceDir}\LICENSE.txt
SetupIconFile={#IconFile}
UninstallDisplayIcon={app}\{#AppExe}
UninstallDisplayName={#AppName}
OutputDir={#OutputDir}
OutputBaseFilename={#AppName}-{#AppVersion}-setup
Compression=lzma2/max
SolidCompression=yes
WizardStyle=modern
CloseApplications=yes
RestartApplications=no
LanguageDetectionMethod=uilanguage
ShowLanguageDialog=auto
UsePreviousLanguage=no
; Signing happens after compiling, in tools/release/sign.ps1 (docs/release.md); nothing about it lives in this script.

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"
Name: "spanish"; MessagesFile: "compiler:Languages\Spanish.isl"

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"; Flags: unchecked

[Files]
Source: "{#SourceDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[InstallDelete]
; An upgrade replaces the C# assemblies wholesale so no stale file of the old version is loaded.
Type: filesandordirs; Name: "{app}\data_Starfoundry.Game_windows_x86_64"

[Icons]
Name: "{autoprograms}\{#AppName}"; Filename: "{app}\{#AppExe}"; WorkingDir: "{app}"
Name: "{autoprograms}\{#AppName} (console)"; Filename: "{app}\Starfoundry.console.exe"; WorkingDir: "{app}"; Comment: "Starfoundry with a log window"
Name: "{autoprograms}\{#AppName} licences and credits"; Filename: "{app}\THIRD-PARTY-NOTICES.txt"
Name: "{autodesktop}\{#AppName}"; Filename: "{app}\{#AppExe}"; WorkingDir: "{app}"; Tasks: desktopicon

[Run]
Filename: "{app}\{#AppExe}"; Description: "{cm:LaunchProgram,{#AppName}}"; Flags: nowait postinstall skipifsilent

[UninstallDelete]
; Only what the installer put in {app}. The user data folder under %APPDATA% is deliberately not listed: saves outlive the game files.
Type: filesandordirs; Name: "{app}\data_Starfoundry.Game_windows_x86_64"
Type: dirifempty; Name: "{app}"
