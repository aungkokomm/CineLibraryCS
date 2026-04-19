; CineLibrary — Portable Installer (Inno Setup)
; Not a traditional install to Program Files. The user picks any folder
; (external drive, USB stick, Documents, anywhere) and the app lives there
; self-contained, with its library data in CineLibrary-Data\ next to the exe.

#define MyAppName       "CineLibrary"
#define MyAppVersion    "1.2.0"
#define MyAppPublisher  "Aung Ko Ko Myint"
#define MyAppURL        "https://github.com/aungkokomm/CineLibraryCS"
#define MyAppExeName    "CineLibrary.exe"
#define SourceDir       "..\publish"
#define OutputDir       "..\dist"

[Setup]
AppId={{8B7F4E2A-CF9E-4A1C-B6D2-CINE1000LIBRARY}}
AppName={#MyAppName} (Portable)
AppVersion={#MyAppVersion}
AppVerName={#MyAppName} {#MyAppVersion} Portable
AppPublisher={#MyAppPublisher}
AppPublisherURL={#MyAppURL}
AppSupportURL={#MyAppURL}
AppUpdatesURL={#MyAppURL}/releases

; --- Portable layout ---
; No Program Files. Default to user's Desktop\CineLibrary so it's visible,
; but the user can redirect anywhere (F:\Apps\CineLibrary, USB stick, etc.)
DefaultDirName={userdesktop}\{#MyAppName}
DisableDirPage=no
AppendDefaultDirName=no
UsePreviousAppDir=yes

; No Start menu group by default — portable feel
DisableProgramGroupPage=yes
CreateUninstallRegKey=yes

; No admin rights needed
PrivilegesRequired=lowest
PrivilegesRequiredOverridesAllowed=dialog

; 64-bit only
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible

; Output
OutputDir={#OutputDir}
OutputBaseFilename=CineLibrary-v{#MyAppVersion}-Portable-Setup
Compression=lzma2/ultra64
SolidCompression=yes
LZMAUseSeparateProcess=yes

; UI
WizardStyle=modern
SetupIconFile=..\Assets\icon.ico
UninstallDisplayIcon={app}\{#MyAppExeName}
UninstallDisplayName={#MyAppName} (Portable) @ {app}
DisableReadyPage=no
DisableWelcomePage=no

; Target requirements
MinVersion=10.0.19041

; Custom messages (shown during install)
WizardImageAlphaFormat=defined

[Messages]
WelcomeLabel1=CineLibrary Portable Setup
WelcomeLabel2=This installer puts CineLibrary in any folder you choose — your Desktop, an external drive, a USB stick, Documents, anywhere.%n%nEverything CineLibrary needs (including your movie library database and cached posters) lives in that one folder. Move the folder to another PC and the app just keeps working.%n%nClick Next to choose where to put it.
SelectDirDesc=Where should CineLibrary be placed? It will be fully self-contained in that folder.
SelectDirLabel3=Pick any folder — the app runs right out of it. Recommended locations:%n   • An external drive (e.g. F:\Apps\CineLibrary)%n   • Your Desktop (good for quick access)%n   • Documents folder%nAvoid Program Files — it blocks the app from saving your library.
SelectDirBrowseLabel=Click Browse if you want a different folder, otherwise click Next.
ReadyLabel1=Ready to place CineLibrary
ReadyLabel2a=Click Install to copy CineLibrary to:
ReadyLabel2b=Click Install to copy CineLibrary to the folder below. The library data will live there too, keeping everything portable.
FinishedHeadingLabel=CineLibrary is ready
FinishedLabelNoIcons=CineLibrary is installed at [name].%n%nYou can move the whole folder to another drive or PC at any time — your library comes with it.
FinishedLabel=CineLibrary is installed at [name].%n%nYou can move the whole folder to another drive or PC at any time — your library comes with it.

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon";   Description: "Create a &Desktop shortcut";   GroupDescription: "Optional shortcuts:"; Flags: unchecked
Name: "startmenuicon"; Description: "Create a &Start Menu shortcut"; GroupDescription: "Optional shortcuts:"; Flags: unchecked

[Files]
Source: "{#SourceDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{autodesktop}\{#MyAppName}";       Filename: "{app}\{#MyAppExeName}"; WorkingDir: "{app}"; Tasks: desktopicon
Name: "{autoprograms}\{#MyAppName}";      Filename: "{app}\{#MyAppExeName}"; WorkingDir: "{app}"; Tasks: startmenuicon

[Run]
Filename: "{app}\{#MyAppExeName}"; Description: "Launch {#MyAppName} now"; Flags: nowait postinstall skipifsilent; WorkingDir: "{app}"

[UninstallDelete]
; On uninstall: leave CineLibrary-Data alone so user's library survives a reinstall.
; To wipe everything (library too), uncomment the next line:
; Type: filesandordirs; Name: "{app}\CineLibrary-Data"
Type: filesandordirs; Name: "{app}"

[Code]
// Block installation into Program Files (portable installs don't belong there;
// UAC would also prevent the app from writing CineLibrary-Data next to the exe).
function NextButtonClick(CurPageID: Integer): Boolean;
var
  LowerDir: String;
begin
  Result := True;
  if CurPageID = wpSelectDir then
  begin
    LowerDir := Lowercase(WizardDirValue);
    if (Pos('\program files', LowerDir) > 0) or
       (Pos('\programfiles', LowerDir) > 0) then
    begin
      MsgBox('Please choose a different folder.' + #13#10 + #13#10 +
             'CineLibrary is portable and needs to write its library data ' +
             '(CineLibrary-Data\) next to the exe. Program Files blocks that ' +
             'without admin rights.' + #13#10 + #13#10 +
             'Try your Desktop, Documents, or an external drive instead.',
             mbError, MB_OK);
      Result := False;
    end;
  end;
end;
