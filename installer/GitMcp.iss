; GitMcp - per-user installer
;
; Installs into the user's roaming profile (no admin rights):
;   %APPDATA%\GitMcp\server\GitMcp.Server.exe    self-contained MCP server (no .NET needed on the target PC)
;   %APPDATA%\GitMcp\server\appsettings.json     settings (kept across upgrades)
; and registers it in Claude Desktop's claude_desktop_config.json as a stdio server named "git"
; (classic %APPDATA%\Claude and Microsoft Store builds). Uninstall removes that entry and nothing else.
;
; Build with ..\Build-Installer.ps1 (publishes the exe first).
; Silent: GitMcpSetup-<ver>.exe /VERYSILENT /TASKS="claude"

#ifndef AppVersion
  #define AppVersion "0.1.0"
#endif
#define AppName "GitMcp"
#define ServerExe "GitMcp.Server.exe"
#define McpName "git"

[Setup]
AppId={{6D0F3B52-2E7A-4C1B-9A0E-3F7C1D5B8E24}
AppName={#AppName}
AppVersion={#AppVersion}
AppVerName={#AppName} {#AppVersion}
AppPublisher=Pete
VersionInfoVersion={#AppVersion}
PrivilegesRequired=lowest
DefaultDirName={userappdata}\GitMcp
DisableDirPage=yes
DisableProgramGroupPage=yes
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
OutputDir=Output
OutputBaseFilename=GitMcpSetup-{#AppVersion}
Compression=lzma2/ultra64
SolidCompression=yes
WizardStyle=modern
UninstallDisplayName={#AppName}
UninstallDisplayIcon={app}\server\{#ServerExe}
CloseApplications=no
SetupLogging=yes

[Files]
Source: "..\publish\server\{#ServerExe}"; DestDir: "{app}\server"; Flags: ignoreversion
Source: "..\publish\server\appsettings.json"; DestDir: "{app}\server"; Flags: onlyifdoesntexist
Source: "..\README.md"; DestDir: "{app}"; Flags: ignoreversion

[Tasks]
Name: "claude"; Description: "Add the git MCP server to Claude Desktop (claude_desktop_config.json)"

[Run]
Filename: "{app}\server\{#ServerExe}"; Parameters: "--register --name {#McpName}"; \
  Flags: runhidden waituntilterminated; StatusMsg: "Registering with Claude Desktop..."; Tasks: claude

[UninstallRun]
Filename: "{app}\server\{#ServerExe}"; Parameters: "--unregister --name {#McpName}"; \
  Flags: runhidden waituntilterminated; RunOnceId: "UnregisterClaude"

[UninstallDelete]
Type: dirifempty; Name: "{app}\server"
Type: dirifempty; Name: "{app}"

[Code]
procedure KillServer;
var
  Code: Integer;
begin
  { Claude Desktop keeps the server exe open; stop it so it can be replaced. Claude restarts it. }
  Exec(ExpandConstant('{sys}\taskkill.exe'), '/F /IM {#ServerExe}', '', SW_HIDE, ewWaitUntilTerminated, Code);
end;

function PrepareToInstall(var NeedsRestart: Boolean): String;
begin
  KillServer;
  Result := '';
end;

function InitializeUninstall: Boolean;
begin
  KillServer;
  Result := True;
end;

procedure CurPageChanged(CurPageID: Integer);
begin
  if (CurPageID = wpFinished) and WizardIsTaskSelected('claude') then
    WizardForm.FinishedLabel.Caption := WizardForm.FinishedLabel.Caption + #13#10#13#10 +
      'Restart Claude Desktop to load the "{#McpName}" MCP server.';
end;
