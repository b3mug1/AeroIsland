#define AppName "AeroIsland"
#define AppVersion "0.1.3"
#define SourceDir "..\src\AeroIsland.App\bin\Release\net10.0-windows10.0.19041.0\win-x64\publish"
[Setup]
AppId={{B084B0A4-EF36-4D87-9BE6-71BA3E70B861}
AppName={#AppName}
AppVersion={#AppVersion}
DefaultDirName={localappdata}\Programs\AeroIsland
DefaultGroupName=AeroIsland
PrivilegesRequired=lowest
OutputDir=output
OutputBaseFilename=AeroIsland-Setup-{#AppVersion}-win-x64
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
UninstallDisplayIcon={app}\AeroIsland.App.exe
Compression=lzma2
SolidCompression=yes
[Tasks]
Name: "desktopicon"; Description: "Create a desktop shortcut"; Flags: unchecked
[Files]
Source: "{#SourceDir}\*"; DestDir: "{app}"; Flags: recursesubdirs createallsubdirs ignoreversion
[Icons]
Name: "{group}\AeroIsland"; Filename: "{app}\AeroIsland.App.exe"
Name: "{autodesktop}\AeroIsland"; Filename: "{app}\AeroIsland.App.exe"; Tasks: desktopicon
[Run]
Filename: "{app}\AeroIsland.App.exe"; Description: "Launch AeroIsland"; Flags: nowait postinstall skipifsilent
