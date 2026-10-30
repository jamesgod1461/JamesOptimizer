; ══════════════════════════════════════════════════════════════════════════
;  JAMES OPTIMIZER PRO - INNO SETUP INSTALLER SCRIPT
;  Deploys: JamesOptimizer.WinUI & JamesOptimizer.Service
;  Cleans: Old WPF OptimizerUI / James Optimizer 2 legacy files
; ══════════════════════════════════════════════════════════════════════════

#define MyAppName "James Optimizer PRO"
#define MyAppVersion "3.0.0"
#define MyAppPublisher "James God"
#define MyAppExeName "JamesOptimizer.WinUI.exe"
#define MyServiceExeName "JamesOptimizer.Service.exe"
#define MyServiceName "James Optimizer Background Service"

[Setup]
AppId={{078082C2-FA1A-48DC-96E3-1EE39A2B74E1}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppPublisher={#MyAppPublisher}
DefaultDirName={autopf}\James Optimizer
DefaultGroupName={#MyAppName}
AllowNoIcons=yes
OutputDir=Output
OutputBaseFilename=JamesOptimizerSetup_v3.0.0
SetupIconFile=..\JamesOptimizer.WinUI\Assets\AppIcon.ico
UninstallDisplayIcon={app}\{#MyAppExeName}
Compression=lzma2/ultra64
SolidCompression=yes
WizardStyle=modern
PrivilegesRequired=admin
ArchitecturesInstallIn64BitMode=x64

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"; Flags: unchecked

[Files]
; 1. WinUI 3 Main Application Files
Source: "..\JamesOptimizer.WinUI\bin\Release\net8.0-windows10.0.26100.0\win-x64\publish\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

; 2. Background Service Files
Source: "..\JamesOptimizer.Service\bin\Release\net10.0\win-x64\publish\*"; DestDir: "{app}\Service"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{group}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; IconFilename: "{app}\Assets\AppIcon.ico"
Name: "{group}\{cm:UninstallProgram,{#MyAppName}}"; Filename: "{uninstallexe}"
Name: "{autodesktop}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; IconFilename: "{app}\Assets\AppIcon.ico"; Tasks: desktopicon

[Run]
; Install & Start the Background Windows Service
Filename: "{sys}\sc.exe"; Parameters: "create ""{#MyServiceName}"" binPath= ""{app}\Service\{#MyServiceExeName}"" start= auto DisplayName= ""{#MyServiceName}"""; Flags: runhidden
Filename: "{sys}\sc.exe"; Parameters: "start ""{#MyServiceName}"""; Flags: runhidden

; Launch WinUI App upon finish
Filename: "{app}\{#MyAppExeName}"; Description: "{cm:LaunchProgram,{#StringChange(MyAppName, '&', '&&')}}"; Flags: nowait postinstall skipifsilent

[UninstallRun]
; Stop and Delete Background Service
Filename: "{sys}\sc.exe"; Parameters: "stop ""{#MyServiceName}"""; Flags: runhidden
Filename: "{sys}\sc.exe"; Parameters: "delete ""{#MyServiceName}"""; Flags: runhidden

[Code]
// Pre-install check: stop running processes and purge legacy OptimizerUI
procedure CurStepChanged(CurStep: TSetupStep);
var
  ResultCode: Integer;
begin
  if CurStep = ssInstall then
  begin
    // Terminate any running optimizer instances
    Exec('taskkill.exe', '/F /IM JamesOptimizer.WinUI.exe', '', SW_HIDE, ewWaitUntilTerminated, ResultCode);
    Exec('taskkill.exe', '/F /IM "James Optimizer 2.exe"', '', SW_HIDE, ewWaitUntilTerminated, ResultCode);
    Exec('taskkill.exe', '/F /IM OptimizerUI.exe', '', SW_HIDE, ewWaitUntilTerminated, ResultCode);
    Exec('taskkill.exe', '/F /IM JamesOptimizer.Service.exe', '', SW_HIDE, ewWaitUntilTerminated, ResultCode);

    // Stop service if already running
    Exec('sc.exe', 'stop "{#MyServiceName}"', '', SW_HIDE, ewWaitUntilTerminated, ResultCode);

    // Clean up legacy WPF executable if present in target folder
    DeleteFile(ExpandConstant('{app}\James Optimizer 2.exe'));
    DeleteFile(ExpandConstant('{app}\OptimizerUI.exe'));
    DeleteFile(ExpandConstant('{app}\OptimizationEngine.cs'));
  end;
end;
