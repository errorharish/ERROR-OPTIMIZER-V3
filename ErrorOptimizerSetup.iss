; =====================================================================
; Error Optimizer V3 - Production Inno Setup Script
; =====================================================================

#define MyAppName "Error Optimizer V3"
#define MyAppVersion "3.0.0"
#define MyAppPublisher "Error Optimizer Team"
#define MyAppURL "https://erroroptimizer.com"
#define MyAppExeName "ErrorOptimizer.exe"
#define SourceDistDir "dist\win-x64"
#define AssetsDir "assets"

[Setup]
; App Identity
AppId={{D41A28A1-889B-4923-9D22-EC3619572B20}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppVerName={#MyAppName}
AppPublisher={#MyAppPublisher}
AppPublisherURL={#MyAppURL}
AppSupportURL={#MyAppURL}
AppUpdatesURL={#MyAppURL}

; Installation Target
DefaultDirName={autopf}\Error Optimizer V3
DefaultGroupName={#MyAppName}
AllowNoIcons=yes
PrivilegesRequired=admin
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible

; Visuals & Branding
SetupIconFile={#AssetsDir}\Error Optimizer V3.ico
UninstallDisplayIcon={app}\{#MyAppExeName}
WizardStyle=modern
DisableWelcomePage=no
DisableProgramGroupPage=yes

; Packaging & Compression
Compression=lzma2/ultra64
SolidCompression=yes
OutputDir=dist\installer
OutputBaseFilename=ErrorOptimizer_V3_Setup
CloseApplications=force
RestartApplications=no
AppMutex=ErrorOptimizer_SingleInstance_Mutex,BiosOptimizer_Service_Mutex

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"; Flags: unchecked
Name: "startwithwindows"; Description: "Automatically launch Error Optimizer with Windows (Recommended)"; GroupDescription: "Startup Options:"

[Files]
; Dist runtime files (Self-contained binaries & dependencies)
Source: "{#SourceDistDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs
; Official High-Resolution Branding Icon
Source: "{#AssetsDir}\Error Optimizer V3.ico"; DestDir: "{app}\assets"; Flags: ignoreversion

[Icons]
; Start Menu
Name: "{group}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; IconFilename: "{app}\{#MyAppExeName}"
Name: "{group}\{cm:UninstallProgram,{#MyAppName}}"; Filename: "{uninstallexe}"
; Desktop (Optional)
Name: "{autodesktop}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; Tasks: desktopicon; IconFilename: "{app}\{#MyAppExeName}"

[Registry]
; Configure clean per-machine installation metadata
Root: HKLM; Subkey: "Software\Error Optimizer V3"; ValueType: string; ValueName: "InstallLocation"; ValueData: "{app}"; Flags: uninsdeletekey
Root: HKLM; Subkey: "Software\Error Optimizer V3"; ValueType: string; ValueName: "Version"; ValueData: "{#MyAppVersion}"; Flags: uninsdeletekey
Root: HKLM; Subkey: "Software\Error Optimizer V3"; ValueType: string; ValueName: "ServicePath"; ValueData: "{app}\core\BiosOptimizer.Service.exe"; Flags: uninsdeletekey

[Run]
; Configure clean startup if task was selected
Filename: "{app}\{#MyAppExeName}"; Parameters: "--register-startup"; Flags: runhidden runasoriginaluser waituntilterminated; Tasks: startwithwindows
; Launch Application after Setup Completion
Filename: "{app}\{#MyAppExeName}"; Description: "{cm:LaunchProgram,{#StringChange(MyAppName, '&', '&&')}}"; Flags: nowait postinstall skipifsilent runasoriginaluser

[Code]
procedure CleanLegacyStartupRegistrations();
var
  ResultCode: Integer;
begin
  // Remove any legacy HKLM Run entries
  RegDeleteValue(HKLM64, 'Software\Microsoft\Windows\CurrentVersion\Run', 'Error Optimizer');
  RegDeleteValue(HKLM64, 'Software\Microsoft\Windows\CurrentVersion\Run', 'ErrorOptimizer');
  RegDeleteValue(HKLM64, 'Software\Microsoft\Windows\CurrentVersion\Run', 'Error Optimizer V3');
  RegDeleteValue(HKLM64, 'Software\Microsoft\Windows\CurrentVersion\Run', 'BiosOptimizer');
  RegDeleteValue(HKLM32, 'SOFTWARE\Microsoft\Windows\CurrentVersion\Run', 'Error Optimizer');
  RegDeleteValue(HKLM32, 'SOFTWARE\Microsoft\Windows\CurrentVersion\Run', 'ErrorOptimizer');
  RegDeleteValue(HKLM32, 'SOFTWARE\Microsoft\Windows\CurrentVersion\Run', 'Error Optimizer V3');
  RegDeleteValue(HKLM32, 'SOFTWARE\Microsoft\Windows\CurrentVersion\Run', 'BiosOptimizer');

  // Remove legacy HKCU Run entries
  RegDeleteValue(HKCU, 'Software\Microsoft\Windows\CurrentVersion\Run', 'ErrorOptimizer');
  RegDeleteValue(HKCU, 'Software\Microsoft\Windows\CurrentVersion\Run', 'Error Optimizer V3');
  RegDeleteValue(HKCU, 'Software\Microsoft\Windows\CurrentVersion\Run', 'ErrorOptimizerV3');
  RegDeleteValue(HKCU, 'Software\Microsoft\Windows\CurrentVersion\Run', 'BiosOptimizer');
  RegDeleteValue(HKCU, 'Software\Microsoft\Windows\CurrentVersion\Run', 'Bios Optimizer');

  // Clean Startup folders
  DeleteFile(ExpandConstant('{userstartup}\Error Optimizer.lnk'));
  DeleteFile(ExpandConstant('{userstartup}\ErrorOptimizer.lnk'));
  DeleteFile(ExpandConstant('{userstartup}\Error Optimizer V3.lnk'));
  DeleteFile(ExpandConstant('{commonstartup}\Error Optimizer.lnk'));
  DeleteFile(ExpandConstant('{commonstartup}\ErrorOptimizer.lnk'));
  DeleteFile(ExpandConstant('{commonstartup}\Error Optimizer V3.lnk'));

  // Clean stale logon tasks
  Exec('schtasks.exe', '/delete /tn "\ErrorOptimizer\StartupTask" /f', '', SW_HIDE, ewWaitUntilTerminated, ResultCode);
  Exec('schtasks.exe', '/delete /tn "ErrorOptimizerStartup" /f', '', SW_HIDE, ewWaitUntilTerminated, ResultCode);
  Exec('schtasks.exe', '/delete /tn "BiosOptimizerStartup" /f', '', SW_HIDE, ewWaitUntilTerminated, ResultCode);
end;

// Terminate running instances before install / upgrade
function PrepareToInstall(var NeedsRestart: Boolean): String;
var
  ResultCode: Integer;
begin
  Result := '';
  Exec('taskkill.exe', '/F /IM ErrorOptimizer.exe', '', SW_HIDE, ewWaitUntilTerminated, ResultCode);
  Exec('taskkill.exe', '/F /IM BiosOptimizer.Service.exe', '', SW_HIDE, ewWaitUntilTerminated, ResultCode);
  Exec('taskkill.exe', '/F /IM BiosOptimizer.CLI.exe', '', SW_HIDE, ewWaitUntilTerminated, ResultCode);
  CleanLegacyStartupRegistrations();
  Sleep(500);
end;

procedure CurStepChanged(CurStep: TSetupStep);
begin
  if CurStep = ssPostInstall then
  begin
    CleanLegacyStartupRegistrations();
  end;
end;

// Terminate running instances before uninstall
procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
var
  ResultCode: Integer;
begin
  if CurUninstallStep = usUninstall then
  begin
    Exec('taskkill.exe', '/F /IM ErrorOptimizer.exe', '', SW_HIDE, ewWaitUntilTerminated, ResultCode);
    Exec('taskkill.exe', '/F /IM BiosOptimizer.Service.exe', '', SW_HIDE, ewWaitUntilTerminated, ResultCode);
    Exec('taskkill.exe', '/F /IM BiosOptimizer.CLI.exe', '', SW_HIDE, ewWaitUntilTerminated, ResultCode);
    CleanLegacyStartupRegistrations();
    RegDeleteValue(HKCU, 'Software\Microsoft\Windows\CurrentVersion\Run', 'Error Optimizer');
    RegDeleteValue(HKCU, 'Software\Microsoft\Windows\CurrentVersion\Run', 'ErrorOptimizer');
    RegDeleteValue(HKCU, 'Software\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\Run', 'Error Optimizer');
    Sleep(500);
  end;
end;
