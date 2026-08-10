[Setup]
AppName=James Optimizer 2
AppVersion=2.0.0
DefaultDirName={autopf}\James Optimizer
DefaultGroupName=James Optimizer
OutputBaseFilename=JamesOptimizer_Setup
OutputDir=C:\Users\adity\OneDrive\Desktop
Compression=lzma
SolidCompression=yes
PrivilegesRequired=admin
ArchitecturesInstallIn64BitMode=x64

[Files]
Source: "C:\Users\adity\source\repos\optimizer\OptimizerUI\bin\Release\net10.0-windows10.0.17763.0\win-x64\publish\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs
Source: "C:\Users\adity\source\repos\optimizer\JamesOptimizer.Service\bin\Release\net10.0\win-x64\publish\*"; DestDir: "{app}\Service"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{group}\James Optimizer"; Filename: "{app}\James Optimizer 2.exe"
Name: "{autodesktop}\James Optimizer"; Filename: "{app}\James Optimizer 2.exe"; Tasks: desktopicon

[Tasks]
Name: "desktopicon"; Description: "Create a &desktop icon"; GroupDescription: "Additional icons:"

[Registry]
Root: HKCU; Subkey: "Software\JamesOptimizer"; ValueType: dword; ValueName: "WalkthroughComplete"; ValueData: 0; Flags: uninsdeletevalue

[Run]
Filename: "{sys}\sc.exe"; Parameters: "stop ""James Optimizer Background Service"""; Flags: runhidden
Filename: "{sys}\sc.exe"; Parameters: "delete ""James Optimizer Background Service"""; Flags: runhidden
Filename: "{sys}\sc.exe"; Parameters: "create ""James Optimizer Background Service"" binPath= ""\""{app}\Service\JamesOptimizer.Service.exe\"""" start= auto DisplayName= ""James Optimizer Background Service"""; Flags: runhidden
Filename: "{sys}\sc.exe"; Parameters: "start ""James Optimizer Background Service"""; Flags: runhidden
Filename: "{app}\James Optimizer 2.exe"; Description: "Launch James Optimizer"; Flags: nowait postinstall skipifsilent

[UninstallRun]
Filename: "{sys}\sc.exe"; Parameters: "stop ""James Optimizer Background Service"""; Flags: runhidden skipifdoesntexist
Filename: "{sys}\sc.exe"; Parameters: "delete ""James Optimizer Background Service"""; Flags: runhidden skipifdoesntexist
