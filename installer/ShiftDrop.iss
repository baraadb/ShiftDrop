; ShiftDrop Setup (Inno Setup 6). Packs build\publish (app + .NET runtime + FFmpeg + PDFium) into one Setup.exe.
; Build it with Build-Setup.cmd, or: ISCC /DAppVersion=0.1.0 installer\ShiftDrop.iss

#ifndef AppVersion
  #define AppVersion "0.1.0"
#endif
#define AppName "ShiftDrop"
#define AppExe "ShiftDrop.exe"

[Setup]
AppId={{6F1C2B7E-3A9D-4E51-9B0C-5D2A8E7F4C13}
AppName={#AppName}
AppVersion={#AppVersion}
AppVerName={#AppName} {#AppVersion}
AppPublisher=ShiftDrop (open source)
AppPublisherURL=https://github.com/
AppComments=Drag a file, press Shift, drop it on a format.
VersionInfoVersion={#AppVersion}
; per-user install: no admin prompt
PrivilegesRequired=lowest
DefaultDirName={localappdata}\Programs\{#AppName}
DisableDirPage=auto
DisableProgramGroupPage=yes
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
MinVersion=10.0.17763
OutputDir=..\build
OutputBaseFilename=ShiftDrop-Setup-{#AppVersion}
SetupIconFile=..\assets\shiftdrop.ico
UninstallDisplayIcon={app}\{#AppExe}
UninstallDisplayName={#AppName}
LicenseFile=..\LICENSE
WizardStyle=modern
Compression=lzma2/ultra64
SolidCompression=yes
LZMANumBlockThreads=4
CloseApplications=no

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "autostart"; Description: "Start ShiftDrop with Windows (recommended)"; GroupDescription: "Options:"
Name: "sendto"; Description: "Add ShiftDrop to the right-click ""Send to"" menu"; GroupDescription: "Options:"

[Files]
Source: "..\build\publish\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{userprograms}\{#AppName}"; Filename: "{app}\{#AppExe}"; Comment: "Drag a file, press Shift"
Name: "{usersendto}\{#AppName}"; Filename: "{app}\{#AppExe}"; Tasks: sendto

[Registry]
Root: HKCU; Subkey: "Software\Microsoft\Windows\CurrentVersion\Run"; ValueType: string; ValueName: "{#AppName}"; ValueData: """{app}\{#AppExe}"""; Flags: uninsdeletevalue; Tasks: autostart

[Run]
Filename: "{app}\{#AppExe}"; Description: "Start ShiftDrop now"; Flags: postinstall nowait skipifsilent

[UninstallRun]
Filename: "{app}\{#AppExe}"; Parameters: "--quit"; RunOnceId: "QuitShiftDrop"; Flags: runhidden waituntilterminated skipifdoesntexist

[UninstallDelete]
Type: filesandordirs; Name: "{app}\tools"

[Code]
// Close a running ShiftDrop before files are replaced (updates and reinstalls).
function PrepareToInstall(var NeedsRestart: Boolean): String;
var
  Code: Integer;
  Exe: String;
begin
  Result := '';
  Exe := ExpandConstant('{app}\{#AppExe}');
  if FileExists(Exe) then
  begin
    Exec(Exe, '--quit', '', SW_HIDE, ewWaitUntilTerminated, Code);
    Sleep(800);
  end;
  // the older script-based install used the same folder; its uninstall entry would be a duplicate
  RegDeleteKeyIncludingSubkeys(HKCU, 'Software\Microsoft\Windows\CurrentVersion\Uninstall\ShiftDrop');
end;
