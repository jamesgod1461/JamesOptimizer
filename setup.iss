[Setup]
AppName=James Optimizer
AppVersion=2.0
DefaultDirName={autopf}\James Optimizer
DefaultGroupName=James Optimizer
OutputDir=.\Installer
OutputBaseFilename=JamesOptimizer_Setup
Compression=lzma2
SolidCompression=yes
ArchitecturesInstallIn64BitMode=x64
PrivilegesRequired=admin

[Files]
Source: "PublishOutput\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{group}\James Optimizer"; Filename: "{app}\James Optimizer 2.exe"
Name: "{autodesktop}\James Optimizer"; Filename: "{app}\James Optimizer 2.exe"

[Run]
Filename: "{app}\James Optimizer 2.exe"; Description: "Launch James Optimizer"; Flags: nowait postinstall skipifsilent

[UninstallRun]
Filename: "sc.exe"; Parameters: "stop ""JamesOptimizerService"""; Flags: runhidden; RunOnceId: "StopService"
Filename: "sc.exe"; Parameters: "delete ""JamesOptimizerService"""; Flags: runhidden; RunOnceId: "DelService"
Filename: "schtasks.exe"; Parameters: "/delete /tn ""James Optimizer 2"" /f"; Flags: runhidden; RunOnceId: "DelTask"
