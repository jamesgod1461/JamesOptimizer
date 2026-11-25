; ══════════════════════════════════════════════════════════════════════════
;  JAMESOPTIMIZER V3 - CUSTOM INNO SETUP INSTALLER SCRIPT
;  Deploys: JamesOptimizer.WinUI & Mandatory JamesOptimizer.Service
;  Theme:   Custom Dark Obsidian + Cyan Hardware Suite
; ══════════════════════════════════════════════════════════════════════════

#define MyAppName "JamesOptimizer V3"
#define MyAppVersion "3.0.0"
#define MyAppPublisher "James God"
#define MyAppExeName "JamesOptimizer.WinUI.exe"
#define MyServiceExeName "JamesOptimizer.Service.exe"
#define MyServiceName "JamesOptimizer Background Service"

[Setup]
AppId={{078082C2-FA1A-48DC-96E3-1EE39A2B74E1}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppVerName={#MyAppName} ({#MyAppVersion} x64)
AppPublisher={#MyAppPublisher}
VersionInfoVersion=3.0.0.0
VersionInfoProductName={#MyAppName}
VersionInfoDescription={#MyAppName} Windows Performance Suite Setup
VersionInfoCompany={#MyAppPublisher}
VersionInfoCopyright=Copyright (C) 2026 {#MyAppPublisher}
MinVersion=10.0.19041
DefaultDirName={autopf}\JamesOptimizer
DefaultGroupName={#MyAppName}
AllowNoIcons=yes
DisableWelcomePage=no
DisableProgramGroupPage=yes
OutputDir=Output
OutputBaseFilename=JamesOptimizer-v{#MyAppVersion}-Setup
SetupIconFile=..\JamesOptimizer.WinUI\Assets\AppIcon.ico
#ifdef UseSignTool
SignedUninstaller=yes
SignTool=signtool_cmd
#endif
UninstallDisplayName={#MyAppName}
UninstallDisplayIcon={app}\{#MyAppExeName}
Compression=lzma2/ultra64
SolidCompression=yes
WizardStyle=modern dark windows11 hidebevels
WizardSizePercent=115,115
WizardImageFile=Assets\WizardBanner.bmp
WizardSmallImageFile=Assets\WizardSmall.bmp
WizardImageStretch=yes
WizardBackColor=#0E1117
WizardImageBackColor=#0B0E14
WizardSmallImageBackColor=#0E1117
PrivilegesRequired=admin
ArchitecturesInstallIn64BitMode=x64compatible

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[Messages]
SetupWindowTitle=%1 — Pro Suite Setup
WelcomeLabel1=Install [name]
WelcomeLabel2=This wizard installs [name/ver] (WinUI 3 Client + Background Telemetry Service) on your x64 Windows system.%n%nRunning instances and older builds are safely updated in place without disturbing system stability.%n%nClick Next to choose the installation folder.
ReadyLabel1=Ready to Install [name]
ReadyLabel2a=Click Install to deploy the WinUI 3 suite, bundled typography, and Background Service, or Back to change the installation path.
InstallingLabel=Deploying [name] and starting the Background Service...
FinishedHeadingLabel=[name] is Ready
FinishedLabelNoIcons=[name] has been installed and the Background Service is running.
FinishedLabel=[name] has been installed with its Background Telemetry Service running.%n%nClick Finish to launch the suite now.

[InstallDelete]
; Purge stale DLLs, symbols, legacy WPF client files, and old Service/WinUI/locale folders on upgrade
Type: files; Name: "{app}\*.dll"
Type: files; Name: "{app}\*.pdb"
Type: files; Name: "{app}\*.json"
Type: files; Name: "{app}\*.winmd"
Type: files; Name: "{app}\*.pri"
Type: files; Name: "{app}\createdump.exe"
Type: filesandordirs; Name: "{app}\Service"
Type: filesandordirs; Name: "{app}\WinUI"
Type: filesandordirs; Name: "{app}\Tools"
Type: filesandordirs; Name: "{app}\Microsoft.UI.Xaml"

[Files]
; 1. WinUI Modern Client Files (Main Application + Bundled Assets & Fonts)
Source: "..\JamesOptimizer.WinUI\bin\Release\net10.0-windows10.0.26100.0\win-x64\publish\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

; 2. Background Service Files (Mandatory)
Source: "..\JamesOptimizer.Service\bin\Release\net10.0\win-x64\publish\*"; DestDir: "{app}\Service"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{group}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; IconFilename: "{app}\{#MyAppExeName}"
Name: "{group}\{cm:UninstallProgram,{#MyAppName}}"; Filename: "{uninstallexe}"
Name: "{autodesktop}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; IconFilename: "{app}\{#MyAppExeName}"

[Run]
; Install & Start the Background Windows Service with failure recovery & Admin-only service control ACL
Filename: "{sys}\sc.exe"; Parameters: "create ""{#MyServiceName}"" binPath= ""\""{app}\Service\{#MyServiceExeName}\"""" start= auto DisplayName= ""{#MyServiceName}"""; Flags: runhidden
Filename: "{sys}\sc.exe"; Parameters: "failure ""{#MyServiceName}"" reset= 86400 actions= restart/5000/restart/10000/none/0"; Flags: runhidden
Filename: "{sys}\sc.exe"; Parameters: "sdset ""{#MyServiceName}"" D:(A;;CCLCSWRPWPDTLOCRRC;;;SY)(A;;CCDCLCSWRPWPDTLOCRSDRCWDWO;;;BA)(A;;CCLCSWLOCRRC;;;IU)"; Flags: runhidden
Filename: "{sys}\sc.exe"; Parameters: "start ""{#MyServiceName}"""; Flags: runhidden

; Launch App upon finish
Filename: "{app}\{#MyAppExeName}"; Description: "{cm:LaunchProgram,{#StringChange(MyAppName, '&', '&&')}}"; Flags: nowait postinstall skipifsilent

[UninstallRun]
; Offer clean revert of tweaks before stopping/removing service
Filename: "{app}\{#MyAppExeName}"; Parameters: "--revert-all"; RunOnceId: "RevertTweaks"; Flags: runhidden
; Stop and Delete Background Service cleanly (wait briefly so SCM releases handle)
Filename: "{cmd}"; Parameters: "/c sc.exe stop ""{#MyServiceName}"" & ping -n 3 127.0.0.1 >nul & sc.exe delete ""{#MyServiceName}"""; RunOnceId: "DeleteService"; Flags: runhidden
; Remove app-owned config/startup registry entries on uninstall
Filename: "{sys}\reg.exe"; Parameters: "delete ""HKLM\SOFTWARE\JamesOptimizer"" /f"; RunOnceId: "CleanHklmReg"; Flags: runhidden
Filename: "{sys}\reg.exe"; Parameters: "delete ""HKCU\Software\JamesOptimizer"" /f"; RunOnceId: "CleanHkcuReg"; Flags: runhidden
Filename: "{sys}\reg.exe"; Parameters: "delete ""HKCU\Software\Microsoft\Windows\CurrentVersion\Run"" /v ""JamesOptimizer"" /f"; RunOnceId: "CleanRunReg"; Flags: runhidden

[UninstallDelete]
Type: filesandordirs; Name: "{app}"
Type: filesandordirs; Name: "{commonappdata}\JamesOptimizer"
Type: filesandordirs; Name: "{localappdata}\JamesOptimizer"

[Code]
var
  BrandFooterLabel: TNewStaticText;

procedure InitializeWizard;
begin
  // Subtle custom engine footer along the bottom bar
  BrandFooterLabel := TNewStaticText.Create(WizardForm);
  BrandFooterLabel.Parent := WizardForm;
  BrandFooterLabel.Caption := 'JamesOptimizer V3  ·  Engine 3.0.0  ·  x64';
  BrandFooterLabel.Left := ScaleX(20);
  BrandFooterLabel.Top := WizardForm.NextButton.Top + ScaleY(6);
  BrandFooterLabel.Font.Color := $B8A394;
  BrandFooterLabel.Font.Size := 8;
end;

// Pre-install check: stop running processes and purge legacy files
procedure CurStepChanged(CurStep: TSetupStep);
var
  ResultCode: Integer;
  FindRec: TFindRec;
  AppDir: String;
begin
  if CurStep = ssInstall then
  begin
    AppDir := ExpandConstant('{app}');

    // Terminate any running optimizer instances
    Exec('taskkill.exe', '/F /IM JamesOptimizer.WinUI.exe', '', SW_HIDE, ewWaitUntilTerminated, ResultCode);
    Exec('taskkill.exe', '/F /IM JamesOptimizer.exe', '', SW_HIDE, ewWaitUntilTerminated, ResultCode);
    Exec('taskkill.exe', '/F /IM "James Optimizer 2.exe"', '', SW_HIDE, ewWaitUntilTerminated, ResultCode);
    Exec('taskkill.exe', '/F /IM "James Optimizer.exe"', '', SW_HIDE, ewWaitUntilTerminated, ResultCode);
    Exec('taskkill.exe', '/F /IM OptimizerUI.exe', '', SW_HIDE, ewWaitUntilTerminated, ResultCode);
    Exec('taskkill.exe', '/F /IM JamesOptimizer.Service.exe', '', SW_HIDE, ewWaitUntilTerminated, ResultCode);

    // Stop service if already running
    Exec('sc.exe', 'stop "{#MyServiceName}"', '', SW_HIDE, ewWaitUntilTerminated, ResultCode);

    // Clean up legacy executables if present in target folder
    DeleteFile(AppDir + '\James Optimizer 2.exe');
    DeleteFile(AppDir + '\James Optimizer.exe');
    DeleteFile(AppDir + '\JamesOptimizer.exe');
    DeleteFile(AppDir + '\OptimizerUI.exe');

    // Remove legacy subdirectories (e.g. old locale folders, Tools, WinUI, Service)
    if FindFirst(AppDir + '\*', FindRec) then
    begin
      try
        repeat
          if (FindRec.Attributes and FILE_ATTRIBUTE_DIRECTORY <> 0) and
             (FindRec.Name <> '.') and (FindRec.Name <> '..') then
          begin
            DelTree(AppDir + '\' + FindRec.Name, True, True, True);
          end;
        until not FindNext(FindRec);
      finally
        FindClose(FindRec);
      end;
    end;

    // Purge stale desktop shortcuts pointing to legacy paths
    DeleteFile(ExpandConstant('{userdesktop}\James Optimizer.lnk'));
    DeleteFile(ExpandConstant('{commondesktop}\James Optimizer.lnk'));
    DeleteFile(ExpandConstant('{userdesktop}\James Optimizer 2.lnk'));
    DeleteFile(ExpandConstant('{commondesktop}\James Optimizer 2.lnk'));
  end;
end;
