@echo off
setlocal EnableDelayedExpansion

title James Optimizer - Installer
echo ===================================================
echo     James Optimizer - Installation Setup
echo ===================================================
echo.

:: Check for Administrator privileges
net session >nul 2>&1
if %errorLevel% == 0 (
    echo [OK] Running with Administrator privileges.
) else (
    echo [ERROR] This installer requires Administrator privileges.
    echo Please right-click "Install.bat" and select "Run as administrator".
    pause
    exit /b 1
)

set "INSTALL_DIR=C:\Program Files\James Optimizer"
set "SERVICE_DIR=%INSTALL_DIR%\Service"
set "UI_SOURCE=%~dp0OptimizerUI\bin\Release\net10.0-windows10.0.17763.0\win-x64\publish"
set "SERVICE_SOURCE=%~dp0JamesOptimizer.Service\bin\Release\net10.0\win-x64\publish"

echo.
echo Step 1: Creating Installation Directories...
if not exist "%INSTALL_DIR%" mkdir "%INSTALL_DIR%"
if not exist "%SERVICE_DIR%" mkdir "%SERVICE_DIR%"

echo.
echo Step 2: Copying UI Files...
if not exist "%UI_SOURCE%\James Optimizer 2.exe" (
    echo [ERROR] Could not find published UI files. Make sure the build succeeded.
    pause
    exit /b 1
)
xcopy /Y /S /I "%UI_SOURCE%\*" "%INSTALL_DIR%"

echo.
echo Step 3: Copying Background Service Files...
if not exist "%SERVICE_SOURCE%\JamesOptimizer.Service.exe" (
    echo [ERROR] Could not find published Service files. Make sure the build succeeded.
    pause
    exit /b 1
)
xcopy /Y /S /I "%SERVICE_SOURCE%\*" "%SERVICE_DIR%"

echo.
echo Step 4: Creating Shortcuts (Desktop ^& Start Menu)...
set "SHORTCUT_SCRIPT=%temp%\CreateShortcut.ps1"
echo $WshShell = New-Object -comObject WScript.Shell > "%SHORTCUT_SCRIPT%"
echo $DesktopPath = [Environment]::GetFolderPath('Desktop') >> "%SHORTCUT_SCRIPT%"
echo $StartMenuPath = [Environment]::GetFolderPath('CommonStartMenu') >> "%SHORTCUT_SCRIPT%"
echo $DesktopShortcut = $WshShell.CreateShortcut("$DesktopPath\James Optimizer.lnk") >> "%SHORTCUT_SCRIPT%"
echo $DesktopShortcut.TargetPath = "%INSTALL_DIR%\James Optimizer 2.exe" >> "%SHORTCUT_SCRIPT%"
echo $DesktopShortcut.WorkingDirectory = "%INSTALL_DIR%" >> "%SHORTCUT_SCRIPT%"
echo $DesktopShortcut.Save() >> "%SHORTCUT_SCRIPT%"
echo $StartMenuShortcut = $WshShell.CreateShortcut("$StartMenuPath\Programs\James Optimizer.lnk") >> "%SHORTCUT_SCRIPT%"
echo $StartMenuShortcut.TargetPath = "%INSTALL_DIR%\James Optimizer 2.exe" >> "%SHORTCUT_SCRIPT%"
echo $StartMenuShortcut.WorkingDirectory = "%INSTALL_DIR%" >> "%SHORTCUT_SCRIPT%"
echo $StartMenuShortcut.Save() >> "%SHORTCUT_SCRIPT%"
powershell.exe -ExecutionPolicy Bypass -NoProfile -File "%SHORTCUT_SCRIPT%"
del "%SHORTCUT_SCRIPT%"

echo.
echo Step 5: Registering Background Service...
sc stop "James Optimizer Background Service" >nul 2>&1
sc delete "James Optimizer Background Service" >nul 2>&1
sc create "James Optimizer Background Service" binPath= "\"%SERVICE_DIR%\JamesOptimizer.Service.exe\"" start= auto DisplayName= "James Optimizer Background Service"
sc start "James Optimizer Background Service"

echo.
echo ===================================================
echo     Installation Complete!
echo ===================================================
echo You can now launch "James Optimizer" from your Desktop or Start Menu.
echo.
pause
