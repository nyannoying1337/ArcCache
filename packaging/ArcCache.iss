#ifndef AppVersion
  #error AppVersion must be passed as /DAppVersion=...
#endif
#ifndef AppPayloadDir
  #error AppPayloadDir must be passed as /DAppPayloadDir=...
#endif

[Setup]
AppId={{2ED6A4DC-7582-4F0B-87B6-B5938B2E86B1}
AppName=Arc Cache
AppVersion={#AppVersion}
AppPublisher=nyannoying1337
AppPublisherURL=https://github.com/nyannoying1337/ArcCache
AppSupportURL=https://github.com/nyannoying1337/ArcCache/issues
AppUpdatesURL=https://github.com/nyannoying1337/ArcCache/releases
DefaultDirName={localappdata}\Programs\Arc Cache
DefaultGroupName=Arc Cache
DisableProgramGroupPage=yes
UninstallDisplayName=Arc Cache
UninstallDisplayIcon={app}\ArcCache.exe
PrivilegesRequired=lowest
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
WizardStyle=modern
Compression=lzma2/max
SolidCompression=yes
SetupLogging=yes
OutputBaseFilename=ArcCache-Setup-{#AppVersion}-Windows-x64
CloseApplications=no
RestartApplications=no
Uninstallable=yes

[Tasks]
Name: "desktopicon"; Description: "Create a desktop shortcut"; GroupDescription: "Shortcuts:"; Flags: unchecked

[Files]
Source: "{#AppPayloadDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs; Excludes: "*.pdb"

[Icons]
Name: "{autoprograms}\Arc Cache"; Filename: "{app}\ArcCache.exe"; WorkingDir: "{app}"
Name: "{autodesktop}\Arc Cache"; Filename: "{app}\ArcCache.exe"; WorkingDir: "{app}"; Tasks: desktopicon

[Run]
Filename: "{app}\ArcCache.exe"; Description: "Start Arc Cache"; Flags: postinstall nowait skipifsilent

[Code]
// Uninstalling removes only the program folder. Game setups keep their driver copy in the game folder (and the game
// keeps loading it), so warn while any setup record is not "removed". Silent uninstalls are not interrupted.
function HasActiveGameSetups(): Boolean;
var
  FindRec: TFindRec;
  Dir: String;
  Text: AnsiString;
begin
  Result := False;
  Dir := ExpandConstant('{localappdata}\IntelArcCacheCap\games\');
  if FindFirst(Dir + '*.json', FindRec) then
  begin
    try
      repeat
        if (not Result) and LoadStringFromFile(Dir + FindRec.Name, Text) then
          if Pos('"Status":"removed"', String(Text)) = 0 then
            Result := True;
      until Result or not FindNext(FindRec);
    finally
      FindClose(FindRec);
    end;
  end;
end;

function InitializeUninstall(): Boolean;
begin
  Result := True;
  if (not UninstallSilent()) and HasActiveGameSetups() then
    Result := MsgBox('Arc Cache still has game setups. Their driver copies stay in the game folders after uninstalling, and those games keep loading them.' + #13#10#13#10 +
      'To remove them first, choose No, open Arc Cache and use "Use default settings" for each game, or Settings > Reset Arc Cache completely.' + #13#10#13#10 +
      'Uninstall anyway?', mbConfirmation, MB_YESNO or MB_DEFBUTTON2) = IDYES;
end;
