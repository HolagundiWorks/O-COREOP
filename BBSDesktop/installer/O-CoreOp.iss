; O-CoreOp — Inno Setup script (per-user, no admin)
; Build via: powershell -File build-installer.ps1

#ifndef AppVersion
  #define AppVersion "1.0.0"
#endif

#define MyAppName "O-CoreOp"
#define MyAppPublisher "Human Centric Works"
#define MyAppPublisherUrl "https://humancentic.works"
#define MyAppExeName "O-CoreOp.exe"
#define MyAppId "{{A8F3C2E1-9B4D-4E6A-8C1F-2D7E5A9B0C3D}"
; The assistant model: Olmo-3-7B-Instruct Q4_K_M from unsloth/Olmo-3-7B-Instruct-GGUF (Apache-2.0), pinned revision, shared with O-Draft.
#define ModelFile "Olmo-3-7B-Instruct-Q4_K_M.gguf"
#define ModelSha256 "0de0bc63fae8a73a48e167be48f149c9544bd4af61d223cd3f331c6ea664c5c4"
#define ModelUrl "https://huggingface.co/unsloth/Olmo-3-7B-Instruct-GGUF/resolve/a2a9ae53f5a95087616b3f45ca96e9d2631344ee/" + ModelFile

[Setup]
AppId={#MyAppId}
AppName={#MyAppName}
AppVersion={#AppVersion}
AppVerName={#MyAppName} {#AppVersion}
AppPublisher={#MyAppPublisher}
AppPublisherURL={#MyAppPublisherUrl}
AppSupportURL={#MyAppPublisherUrl}
DefaultDirName={localappdata}\Programs\{#MyAppName}
DefaultGroupName={#MyAppName}
UsePreviousAppDir=no
DisableProgramGroupPage=yes
PrivilegesRequired=lowest
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
OutputDir=..\artifacts\installer
OutputBaseFilename=O-CoreOp-Setup-{#AppVersion}
SetupIconFile=..\BBSApp\Assets\app.ico
UninstallDisplayIcon={app}\{#MyAppExeName}
Compression=lzma2
SolidCompression=yes
WizardStyle=modern
VersionInfoVersion={#AppVersion}.0
VersionInfoCompany={#MyAppPublisher}
VersionInfoProductName={#MyAppName}
VersionInfoCopyright=Copyright © {#MyAppPublisher}, Hospet
LicenseFile=..\..\LICENSE

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"; Flags: unchecked
Name: "model"; Description: "Download the assistant model (Olmo 3 7B, 4.5 GB, runs on this computer only; O-Draft uses it too)"; GroupDescription: "Assistant:"

[Files]
Source: "..\artifacts\publish\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{group}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; WorkingDir: "{app}"
Name: "{group}\{cm:UninstallProgram,{#MyAppName}}"; Filename: "{uninstallexe}"
Name: "{autodesktop}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; WorkingDir: "{app}"; Tasks: desktopicon

[Run]
Filename: "{app}\{#MyAppExeName}"; Description: "{cm:LaunchProgram,{#MyAppName}}"; Flags: nowait postinstall skipifsilent

[InstallDelete]
; Earlier versions installed as AQC-Core / AQCCore.exe. Remove that program so an upgrade leaves one O-CoreOp behind.
; Settings in %LocalAppData%\AQCCore are not touched: the app copies them to O-CoreOp on first run.
Type: filesandordirs; Name: "{localappdata}\Programs\AQC-Core"
Type: files; Name: "{app}\AQCCore.*"

[Code]
var
  ModelPage: TDownloadWizardPage;

function ModelPath: String;
begin
  Result := ExpandConstant('{localappdata}\HumanCentricWorks\models\{#ModelFile}');
end;

procedure InitializeWizard;
begin
  ModelPage := CreateDownloadPage(SetupMessage(msgWizardPreparing), SetupMessage(msgPreparingDesc), nil);
end;

{ The model is fetched before files are copied, checked against its SHA-256, and moved into the folder both apps read. An existing file is kept. }
function NextButtonClick(CurPageID: Integer): Boolean;
begin
  Result := True;
  if (CurPageID = wpReady) and WizardIsTaskSelected('model') and (not FileExists(ModelPath)) then
  begin
    ModelPage.Clear;
    ModelPage.Add('{#ModelUrl}', '{#ModelFile}', '{#ModelSha256}');
    ModelPage.Show;
    try
      try
        ModelPage.Download;
        ForceDirectories(ExtractFileDir(ModelPath));
        if not RenameFile(ExpandConstant('{tmp}\{#ModelFile}'), ModelPath) then
        begin
          CopyFile(ExpandConstant('{tmp}\{#ModelFile}'), ModelPath, False);
          DeleteFile(ExpandConstant('{tmp}\{#ModelFile}'));
        end;
      except
        if ModelPage.AbortedByUser then
          Log('Model download aborted by the user.')
        else
          SuppressibleMsgBox(AddPeriod(GetExceptionMessage) + #13#10 + 'The assistant will not work until {#ModelFile} is in ' + ExtractFileDir(ModelPath) + '.', mbError, MB_OK, IDOK);
        { not fatal: the app installs, and the Ask bar says where the file goes }
      end;
    finally
      ModelPage.Hide;
    end;
  end;
end;
