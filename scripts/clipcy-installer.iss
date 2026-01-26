#define AppName "Clipcy"
#define AppVersion "0.1.0"
#define AppPublisher "Dao Anh Thanh (Đào Anh Thành)"
#define AppExeName "Clipcy.exe"
#define PublishDir "..\dist\windows\win-x64"
#define OutputDir "..\dist\installer"
#define IconPath "..\apps\clipcy-windows-ui\Assets\icon.ico"

[Setup]
AppId={{5C3623B7-30B0-4C61-8B08-7C0D9DE3B7D9}}
AppName={#AppName}
AppVersion={#AppVersion}
AppPublisher={#AppPublisher}
AppSupportURL=mailto:daoanhthanh.work@gmail.com
DefaultDirName={autopf}\{#AppName}
DefaultGroupName={#AppName}
UninstallDisplayIcon={#IconPath}
OutputDir={#OutputDir}
OutputBaseFilename={#AppName}-Setup-{#AppVersion}
SetupIconFile={#IconPath}
Compression=lzma
SolidCompression=yes
WizardStyle=modern
ArchitecturesAllowed=x64
ArchitecturesInstallIn64BitMode=x64

[Files]
Source: "{#PublishDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{group}\{#AppName}"; Filename: "{app}\{#AppExeName}"
Name: "{group}\Uninstall {#AppName}"; Filename: "{uninstallexe}"
Name: "{autodesktop}\{#AppName}"; Filename: "{app}\{#AppExeName}"; Tasks: desktopicon

[Tasks]
Name: "desktopicon"; Description: "Create a &desktop icon"; GroupDescription: "Additional icons:"; Flags: unchecked

[Run]
Filename: "{app}\{#AppExeName}"; Description: "Launch {#AppName}"; Flags: nowait postinstall skipifsilent
