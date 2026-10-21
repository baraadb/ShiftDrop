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
AppPublisherURL=https://github.com/baraadb/ShiftDrop
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
Compression=lzma2/max
SolidCompression=yes
; compress in a separate process with one thread: the compiler is 32-bit and runs out of memory otherwise
LZMAUseSeparateProcess=yes
LZMANumBlockThreads=1
LZMADictionarySize=32768
CloseApplications=no
ShowLanguageDialog=auto

[Languages]
; the wizard follows the Windows display language (Arabic.isl is Inno Setup's own translation, 6.5+)
Name: "english"; MessagesFile: "compiler:Default.isl"
Name: "arabic"; MessagesFile: "Arabic.isl"

[CustomMessages]
english.Options=Options:
arabic.Options=خيارات:
english.Autostart=Start ShiftDrop with Windows (recommended)
arabic.Autostart=تشغيل ShiftDrop مع Windows (يُنصح به)
english.SendTo=Add ShiftDrop to the right-click "Send to" menu
arabic.SendTo=إضافة ShiftDrop إلى قائمة «إرسال إلى»
english.StartNow=Start ShiftDrop now
arabic.StartNow=تشغيل ShiftDrop الآن
english.Comment=Drag a file, press Shift
arabic.Comment=اسحب ملفًا واضغط Shift

[Tasks]
Name: "autostart"; Description: "{cm:Autostart}"; GroupDescription: "{cm:Options}"
Name: "sendto"; Description: "{cm:SendTo}"; GroupDescription: "{cm:Options}"

[Files]
Source: "..\build\publish\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{userprograms}\{#AppName}"; Filename: "{app}\{#AppExe}"; Comment: "{cm:Comment}"
Name: "{usersendto}\{#AppName}"; Filename: "{app}\{#AppExe}"; Tasks: sendto

[Registry]
Root: HKCU; Subkey: "Software\Microsoft\Windows\CurrentVersion\Run"; ValueType: string; ValueName: "{#AppName}"; ValueData: """{app}\{#AppExe}"""; Flags: uninsdeletevalue; Tasks: autostart

[Run]
Filename: "{app}\{#AppExe}"; Description: "{cm:StartNow}"; Flags: postinstall nowait skipifsilent
; after a silent update started from inside ShiftDrop, bring it back
Filename: "{app}\{#AppExe}"; Flags: nowait; Check: WizardSilent

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
