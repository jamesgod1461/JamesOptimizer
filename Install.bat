@echo off
setlocal EnableDelayedExpansion

title JamesOptimizer V3 - Installer
echo ===================================================
echo     JamesOptimizer V3 - Installation Setup
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

set "INSTALL_DIR=C:\Program Files\JamesOptimizer"
set "SERVICE_DIR=%INSTALL_DIR%\Service"
set "UI_SOURCE=%~dp0JamesOptimizer.WinUI\bin\Release\net10.0-windows10.0.26100.0\win-x64\publish"
set "SERVICE_SOURCE=%~dp0JamesOptimizer.Service\bin\Release\net10.0\win-x64\publish"

echo.
echo Step 1: Stopping Running Processes ^& Cleaning Legacy Install Files...
taskkill /F /IM JamesOptimizer.WinUI.exe >nul 2>&1
taskkill /F /IM JamesOptimizer.exe >nul 2>&1
taskkill /F /IM OptimizerUI.exe >nul 2>&1
taskkill /F /IM JamesOptimizer.Service.exe >nul 2>&1
sc stop "JamesOptimizer Background Service" >nul 2>&1

if not exist "%INSTALL_DIR%" mkdir "%INSTALL_DIR%"
if exist "%SERVICE_DIR%" rmdir /S /Q "%SERVICE_DIR%" >nul 2>&1
if exist "%INSTALL_DIR%\WinUI" rmdir /S /Q "%INSTALL_DIR%\WinUI" >nul 2>&1
if exist "%INSTALL_DIR%\Tools" rmdir /S /Q "%INSTALL_DIR%\Tools" >nul 2>&1
for %%F in ("%INSTALL_DIR%\*.dll" "%INSTALL_DIR%\*.pdb" "%INSTALL_DIR%\*.json" "%INSTALL_DIR%\*.winmd" "%INSTALL_DIR%\*.pri" "%INSTALL_DIR%\createdump.exe" "%INSTALL_DIR%\JamesOptimizer.exe" "%INSTALL_DIR%\OptimizerUI.exe") do (
    if exist "%%~F" del /F /Q "%%~F" >nul 2>&1
)
for /D %%D in ("%INSTALL_DIR%\*") do (
    if /I not "%%~nxD"=="Service" (
        rmdir /S /Q "%%~D" >nul 2>&1
    )
)
mkdir "%SERVICE_DIR%"

echo.
echo Step 2: Copying WinUI Modern Client Files...
if not exist "%UI_SOURCE%\JamesOptimizer.WinUI.exe" (
    echo [ERROR] Could not find published UI files at "%UI_SOURCE%\JamesOptimizer.WinUI.exe".
    echo Please run 'dotnet publish JamesOptimizer.WinUI -c Release' first.
    pause
    exit /b 1
)
xcopy /Y /S /I "%UI_SOURCE%\*" "%INSTALL_DIR%"

echo.
echo Step 3: Copying Background Service Files...
if not exist "%SERVICE_SOURCE%\JamesOptimizer.Service.exe" (
    echo [ERROR] Could not find published Service files at "%SERVICE_SOURCE%\JamesOptimizer.Service.exe".
    echo Please run 'dotnet publish JamesOptimizer.Service -c Release' first.
    pause
    exit /b 1
)
xcopy /Y /S /I "%SERVICE_SOURCE%\*" "%SERVICE_DIR%"

echo.
echo Step 4: Creating Shortcuts (Desktop ^& Start Menu)...
:: Remove any legacy shortcuts
del /f /q "C:\Users\Public\Desktop\James Optimizer.lnk" >nul 2>&1
del /f /q "C:\Users\Public\Desktop\James Optimizer 2.lnk" >nul 2>&1
del /f /q "%USERPROFILE%\Desktop\James Optimizer.lnk" >nul 2>&1
del /f /q "%USERPROFILE%\Desktop\James Optimizer 2.lnk" >nul 2>&1

set "SHORTCUT_SCRIPT=%temp%\CreateShortcut.ps1"
echo $WshShell = New-Object -comObject WScript.Shell > "%SHORTCUT_SCRIPT%"
echo $DesktopPath = [Environment]::GetFolderPath('Desktop') >> "%SHORTCUT_SCRIPT%"
echo $PublicDesktop = [Environment]::GetFolderPath('CommonDesktopDirectory') >> "%SHORTCUT_SCRIPT%"
echo $StartMenuPath = [Environment]::GetFolderPath('CommonStartMenu') >> "%SHORTCUT_SCRIPT%"
echo $DesktopShortcut = $WshShell.CreateShortcut("$PublicDesktop\JamesOptimizer V3.lnk") >> "%SHORTCUT_SCRIPT%"
echo $DesktopShortcut.TargetPath = "%INSTALL_DIR%\JamesOptimizer.WinUI.exe" >> "%SHORTCUT_SCRIPT%"
echo $DesktopShortcut.WorkingDirectory = "%INSTALL_DIR%" >> "%SHORTCUT_SCRIPT%"
echo $DesktopShortcut.Save() >> "%SHORTCUT_SCRIPT%"
echo $UserShortcut = $WshShell.CreateShortcut("$DesktopPath\JamesOptimizer V3.lnk") >> "%SHORTCUT_SCRIPT%"
echo $UserShortcut.TargetPath = "%INSTALL_DIR%\JamesOptimizer.WinUI.exe" >> "%SHORTCUT_SCRIPT%"
echo $UserShortcut.WorkingDirectory = "%INSTALL_DIR%" >> "%SHORTCUT_SCRIPT%"
echo $UserShortcut.Save() >> "%SHORTCUT_SCRIPT%"
echo $StartMenuShortcut = $WshShell.CreateShortcut("$StartMenuPath\Programs\JamesOptimizer V3.lnk") >> "%SHORTCUT_SCRIPT%"
echo $StartMenuShortcut.TargetPath = "%INSTALL_DIR%\JamesOptimizer.WinUI.exe" >> "%SHORTCUT_SCRIPT%"
echo $StartMenuShortcut.WorkingDirectory = "%INSTALL_DIR%" >> "%SHORTCUT_SCRIPT%"
echo $StartMenuShortcut.Save() >> "%SHORTCUT_SCRIPT%"
powershell.exe -ExecutionPolicy Bypass -NoProfile -File "%SHORTCUT_SCRIPT%"
del "%SHORTCUT_SCRIPT%"

echo.
echo Step 5: Registering Background Service...
sc stop "JamesOptimizer Background Service" >nul 2>&1
sc delete "JamesOptimizer Background Service" >nul 2>&1
sc create "JamesOptimizer Background Service" binPath= "\"%SERVICE_DIR%\JamesOptimizer.Service.exe\"" start= auto DisplayName= "JamesOptimizer Background Service"
sc start "JamesOptimizer Background Service"

echo.
echo ===================================================
echo     Installation Complete!
echo ===================================================
echo You can now launch "JamesOptimizer V3" from your Desktop or Start Menu.
echo.
pause
