[Setup]
AppName=James Optimizer
AppVersion=1.0.0
DefaultDirName={autopf}\James Optimizer
DefaultGroupName=James Optimizer
OutputBaseFilename=JamesOptimizer_Setup
OutputDir=C:\Users\adity\source\repos\optimizer\OptimizerUI\Output
Compression=lzma
SolidCompression=yes
PrivilegesRequired=admin

[Files]
Source: "C:\Users\adity\source\repos\optimizer\OptimizerUI\bin\Release\net10.0-windows10.0.17763.0\publish\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{group}\James Optimizer"; Filename: "{app}\James Optimizer.exe"
Name: "{autodesktop}\James Optimizer"; Filename: "{app}\James Optimizer.exe"; Tasks: desktopicon

[Tasks]
Name: "desktopicon"; Description: "Create a &desktop icon"; GroupDescription: "Additional icons:"

[Run]
Filename: "{app}\James Optimizer.exe"; Description: "Launch James Optimizer"; Flags: nowait postinstall skipifsilent
