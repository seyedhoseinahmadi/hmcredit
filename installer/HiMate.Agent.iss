#define MyAppName "HiMate Credit"
#define MyAppPublisher "HiMate"
#define MyAppExeName "HiMate.Credit.exe"
#ifndef MyAppVersion
  #define MyAppVersion "0.2.7"
#endif

[Setup]
AppId={{9E8C5BE7-ED51-49A0-B413-EE886DB1B634}
AppName={#MyAppName}
AppVerName={#MyAppName} {#MyAppVersion}
AppVersion={#MyAppVersion}
AppPublisher={#MyAppPublisher}
DefaultDirName={autopf64}\HiMate\Credit
DefaultGroupName=HiMate
DisableProgramGroupPage=yes
PrivilegesRequired=admin
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
OutputDir=..\artifacts\installer
OutputBaseFilename=HiMate-Credit-Setup
Compression=lzma2/max
SolidCompression=yes
WizardStyle=modern
SetupLogging=yes
SetupIconFile=..\src\HiMate.Agent\HiMate.ico
CloseApplications=yes
RestartApplications=no
UsePreviousAppDir=yes
UsePreviousGroup=yes
Uninstallable=yes
UninstallDisplayName={#MyAppName}
UninstallDisplayIcon={app}\{#MyAppExeName}
VersionInfoVersion={#MyAppVersion}
VersionInfoCompany={#MyAppPublisher}
VersionInfoDescription=HiMate Credit Windows Installer
VersionInfoProductName={#MyAppName}
VersionInfoProductVersion={#MyAppVersion}

[Files]
Source: "..\artifacts\win-x64\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs
Source: "..\artifacts\drivers\cp210x\*"; DestDir: "{tmp}\HiMate-Credit-CP210x"; Flags: deleteafterinstall recursesubdirs createallsubdirs

[Icons]
Name: "{autoprograms}\HiMate Credit"; Filename: "{app}\{#MyAppExeName}"; WorkingDir: "{app}"
Name: "{autodesktop}\HiMate Credit"; Filename: "{app}\{#MyAppExeName}"; WorkingDir: "{app}"; Tasks: desktopicon

[Tasks]
Name: "desktopicon"; Description: "Create a desktop shortcut"; GroupDescription: "Additional shortcuts:"; Flags: unchecked

[Run]
Filename: "{sys}\pnputil.exe"; Parameters: "/add-driver ""{tmp}\HiMate-Credit-CP210x\*.inf"" /subdirs /install"; Flags: runhidden waituntilterminated; StatusMsg: "Installing CP210x USB-to-Serial driver..."
Filename: "{app}\{#MyAppExeName}"; Description: "Launch HiMate Credit"; WorkingDir: "{app}"; Flags: nowait postinstall skipifsilent
