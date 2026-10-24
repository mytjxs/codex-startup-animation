@echo off
setlocal
powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%~dp0Install.ps1" -BindOfficialDesktopShortcut
set "installResult=%ERRORLEVEL%"
echo.
echo Installation finished. Read the status above before closing this window.
pause
exit /b %installResult%
