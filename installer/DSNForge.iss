#ifndef AppVersion
  #define AppVersion "1.1.0"
#endif
#ifndef SourceRoot
  #define SourceRoot ".."
#endif

[Setup]
AppId={{9E5CF2E8-2F38-48D9-AD25-6A487570419C}
AppName=DSNForge
AppVersion={#AppVersion}
AppVerName=DSNForge {#AppVersion}
AppPublisher=ParhamDRP1
AppPublisherURL=https://github.com/ParhamDRP1/DBForge
AppSupportURL=https://github.com/ParhamDRP1/DBForge/issues
AppUpdatesURL=https://github.com/ParhamDRP1/DBForge/releases
DefaultDirName={localappdata}\Programs\DSNForge
DefaultGroupName=DSNForge
PrivilegesRequired=lowest
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
MinVersion=10.0.17763
DisableProgramGroupPage=yes
LicenseFile={#SourceRoot}\LICENSE
SetupIconFile={#SourceRoot}\assets\app.ico
UninstallDisplayIcon={app}\DSNForge.Desktop.exe
OutputDir={#SourceRoot}\dist
OutputBaseFilename=DSNForge-{#AppVersion}-Setup-win-x64
Compression=lzma2
SolidCompression=yes
WizardStyle=modern
VersionInfoVersion={#AppVersion}
CloseApplications=yes
RestartApplications=no

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[Types]
Name: "full"; Description: "GUI, CLI and library"
Name: "custom"; Description: "Custom installation"; Flags: iscustom

[Components]
Name: "core"; Description: "Shared runtime and documentation"; Types: full custom; Flags: fixed
Name: "gui"; Description: "Windows desktop application"; Types: full
Name: "cli"; Description: "Command-line application"; Types: full
Name: "library"; Description: "Library integration example"; Types: full

[Tasks]
Name: "desktopicon"; Description: "Create a desktop shortcut"; GroupDescription: "Shortcuts:"; Flags: unchecked; Components: gui

[Files]
Source: "{#SourceRoot}\release\DSNForge.Core.dll"; DestDir: "{app}"; Components: core; Flags: ignoreversion
Source: "{#SourceRoot}\release\DSNForge.Desktop.exe*"; DestDir: "{app}"; Components: gui; Flags: ignoreversion
Source: "{#SourceRoot}\release\DSNForge.Cli.exe*"; DestDir: "{app}"; Components: cli; Flags: ignoreversion
Source: "{#SourceRoot}\LICENSE"; DestDir: "{app}"; Components: core; Flags: ignoreversion
Source: "{#SourceRoot}\README.txt"; DestDir: "{app}"; Components: core; Flags: ignoreversion
Source: "{#SourceRoot}\README.md"; DestDir: "{app}"; Components: core; Flags: ignoreversion
Source: "{#SourceRoot}\README.fa.md"; DestDir: "{app}"; Components: core; Flags: ignoreversion
Source: "{#SourceRoot}\CHANGELOG.md"; DestDir: "{app}"; Components: core; Flags: ignoreversion
Source: "{#SourceRoot}\docs\*"; DestDir: "{app}\docs"; Components: core; Flags: ignoreversion recursesubdirs createallsubdirs
Source: "{#SourceRoot}\assets\*"; DestDir: "{app}\assets"; Components: core; Flags: ignoreversion
Source: "{#SourceRoot}\examples\*"; DestDir: "{app}\examples"; Components: core; Flags: ignoreversion
Source: "{#SourceRoot}\LIBRARY-USAGE.cs.txt"; DestDir: "{app}"; Components: library; Flags: ignoreversion

[Icons]
Name: "{group}\DSNForge"; Filename: "{app}\DSNForge.Desktop.exe"; Components: gui
Name: "{autodesktop}\DSNForge"; Filename: "{app}\DSNForge.Desktop.exe"; Tasks: desktopicon; Components: gui
Name: "{group}\Documentation"; Filename: "{app}\README.txt"
Name: "{group}\Uninstall DSNForge"; Filename: "{uninstallexe}"

[Run]
Filename: "{app}\DSNForge.Desktop.exe"; Description: "Launch DSNForge"; Flags: nowait postinstall skipifsilent; Components: gui

[Code]
function InitializeSetup(): Boolean;
var
  Release: Cardinal;
begin
  Result := RegQueryDWordValue(HKLM, 'SOFTWARE\Microsoft\NET Framework Setup\NDP\v4\Full', 'Release', Release) and (Release >= 528040);
  if not Result then
    MsgBox('Install .NET Framework 4.8 or newer before installing DSNForge.', mbError, MB_OK);
end;
