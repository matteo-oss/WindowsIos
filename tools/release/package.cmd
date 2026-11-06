@echo off
setlocal
cd /d "%~dp0\..\.."

where pwsh.exe >nul 2>nul
if %errorlevel% equ 0 (
    pwsh.exe -NoLogo -NoProfile -ExecutionPolicy Bypass -File "tools\release\publish.ps1" %*
) else (
    powershell.exe -NoLogo -NoProfile -ExecutionPolicy Bypass -File "tools\release\publish.ps1" %*
)

if errorlevel 1 (
    echo.
    echo Packaging failed.
    pause
    exit /b 1
)

echo.
echo Package created in build\release.
pause
