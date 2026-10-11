#define AppName "Stillframe"
#define AppVersion "0.1.5"
#ifndef PublishDirectory
#define PublishDirectory "..\artifacts\publish"
#endif

[Setup]
AppId={{22FC1CA1-D078-4500-BC71-B03583EC91D3}
AppName={#AppName}
AppVersion={#AppVersion}
AppPublisher=Stillframe
DefaultDirName={localappdata}\Programs\Stillframe
DefaultGroupName=Stillframe
DisableProgramGroupPage=yes
PrivilegesRequired=lowest
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
MinVersion=10.0.22000
OutputDir=..\..\
OutputBaseFilename=Stillframe-Setup-0.1.5
SetupIconFile=..\src\Stillframe\Assets\Stillframe.ico
UninstallDisplayIcon={app}\Stillframe.exe
Compression=lzma2
SolidCompression=yes
WizardStyle=modern
CloseApplications=yes
RestartApplications=no
Uninstallable=yes
LicenseFile=..\NOTICE.md

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "Create a desktop shortcut"; GroupDescription: "Shortcuts:"; Flags: unchecked

[Files]
Source: "{#PublishDirectory}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs
Source: "..\README.md"; DestDir: "{app}"; Flags: ignoreversion
Source: "..\architecture.md"; DestDir: "{app}"; Flags: ignoreversion
Source: "..\RELEASING.md"; DestDir: "{app}"; Flags: ignoreversion
Source: "..\docs\screenshots\*"; DestDir: "{app}\docs\screenshots"; Flags: ignoreversion

[Icons]
Name: "{autoprograms}\Stillframe"; Filename: "{app}\Stillframe.exe"
Name: "{autodesktop}\Stillframe"; Filename: "{app}\Stillframe.exe"; Tasks: desktopicon

[Run]
Filename: "{app}\Stillframe.exe"; Description: "Open Stillframe"; Flags: nowait postinstall skipifsilent

[Registry]
Root: HKCU; Subkey: "Software\Microsoft\Windows\CurrentVersion\Run"; ValueType: none; ValueName: "Stillframe"; Flags: uninsdeletevalue

; User preferences and original photos are deliberately preserved by uninstall.
