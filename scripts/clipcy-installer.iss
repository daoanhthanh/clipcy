#ifndef AppName
#define AppName "Clipcy"
#endif

#ifndef AppVersion
#define AppVersion "0.1.0"
#endif

#ifndef AppPublisher
#define AppPublisher "Dao Anh Thanh (Đào Anh Thành)"
#endif

#ifndef AppExeName
#define AppExeName "Clipcy.exe"
#endif

#ifndef PublishDir
#define PublishDir "..\dist\windows\win-x64"
#endif

#ifndef OutputDir
#define OutputDir "..\dist\installer"
#endif

#ifndef IconPath
#define IconPath "..\apps\clipcy-windows\Assets\icon.ico"
#endif

#ifndef AppArch
#define AppArch "x64compatible"
#endif

#ifndef ArchAllowed
#define ArchAllowed "x64compatible"
#endif

#ifndef ArchInstallIn64BitMode
#define ArchInstallIn64BitMode "x64compatible"
#endif

[Setup]
AppId={{5C3623B7-30B0-4C61-8B08-7C0D9DE3B7D9}}
AppName={#AppName}
AppVersion={#AppVersion}
AppPublisher={#AppPublisher}
AppSupportURL=mailto:daoanhthanh.work@gmail.com
DefaultDirName={autopf}\{#AppName}
DefaultGroupName={#AppName}
UninstallDisplayIcon={#IconPath}
PrivilegesRequired=lowest
OutputDir={#OutputDir}
OutputBaseFilename={#AppName}-Setup-{#AppVersion}-{#AppArch}
SetupIconFile={#IconPath}
Compression=lzma
SolidCompression=yes
WizardStyle=modern
ArchitecturesAllowed={#ArchAllowed}
ArchitecturesInstallIn64BitMode={#ArchInstallIn64BitMode}

[Files]
Source: "{#PublishDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{group}\{#AppName}"; Filename: "{app}\{#AppExeName}"
Name: "{group}\Uninstall {#AppName}"; Filename: "{uninstallexe}"
Name: "{autodesktop}\{#AppName}"; Filename: "{app}\{#AppExeName}"; Tasks: desktopicon

[Tasks]
Name: "desktopicon"; Description: "Create a &desktop icon"; GroupDescription: "Additional icons:"; Flags: unchecked

[UninstallRun]
Filename: "cmd.exe"; Parameters: "/c taskkill /im {#AppExeName} /f"; Flags: runhidden; RunOnceId: "DelClipcyProcess"

[UninstallDelete]
Type: filesandordirs; Name: "{localappdata}\Clipcy"

[Run]
Filename: "{app}\{#AppExeName}"; Description: "Launch {#AppName}"; Flags: nowait postinstall skipifsilent
