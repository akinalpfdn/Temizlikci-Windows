; Inno Setup script for Temizlikci. Built by scripts\publish.ps1, which passes the version and the published folder:
;   ISCC /DAppVersion=1.1.0 /DSourceDir=<published folder> /DOutputDir=<artifacts> installer\Temizlikci.iss
; Installs per user (no administrator rights, like most developer tools): %LOCALAPPDATA%\Programs\Temizlikci.

#ifndef AppVersion
  #error Pass /DAppVersion=x.y.z
#endif
#ifndef SourceDir
  #error Pass /DSourceDir=<published folder>
#endif
#ifndef OutputDir
  #define OutputDir "..\artifacts"
#endif

[Setup]
; Never change AppId: Windows uses it to recognize upgrades and the uninstaller.
AppId={{8A5DCE39-5726-4B7B-A869-3102D183366F}
AppName=Temizlikci
AppVersion={#AppVersion}
AppVerName=Temizlikci {#AppVersion}
AppPublisher=Akinalp Fidan
AppPublisherURL=https://github.com/akinalpfdn/Temizlikci-Windows
AppSupportURL=https://github.com/akinalpfdn/Temizlikci-Windows/issues
AppUpdatesURL=https://github.com/akinalpfdn/Temizlikci-Windows/releases
VersionInfoVersion={#AppVersion}
DefaultDirName={localappdata}\Programs\Temizlikci
DisableDirPage=yes
DisableProgramGroupPage=yes
DefaultGroupName=Temizlikci
PrivilegesRequired=lowest
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
MinVersion=10.0.22621
OutputDir={#OutputDir}
; A fixed name, so "releases/latest/download/Temizlikci-Setup.exe" always points at the newest version.
OutputBaseFilename=Temizlikci-Setup
SetupIconFile=..\src\Temizlikci.App\Assets\Temizlikci.ico
UninstallDisplayIcon={app}\Temizlikci.exe
UninstallDisplayName=Temizlikci
Compression=lzma2/ultra64
SolidCompression=yes
WizardStyle=modern
; An open Temizlikci is closed for the upgrade and started again afterwards.
CloseApplications=yes
RestartApplications=yes
LicenseFile=..\LICENSE

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"; Flags: unchecked

[Files]
Source: "{#SourceDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[InstallDelete]
; An upgrade replaces the whole folder, so files a newer version no longer ships don't linger.
Type: filesandordirs; Name: "{app}\*"

[Icons]
Name: "{autoprograms}\Temizlikci"; Filename: "{app}\Temizlikci.exe"
Name: "{autodesktop}\Temizlikci"; Filename: "{app}\Temizlikci.exe"; Tasks: desktopicon

[Run]
Filename: "{app}\Temizlikci.exe"; Description: "{cm:LaunchProgram,Temizlikci}"; Flags: nowait postinstall skipifsilent

; The app's own data (%LOCALAPPDATA%\Temizlikci: saved scans, history, settings) is kept on uninstall, like any
; document; the app never leaves anything else behind.
