@echo off
powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%~dp0Build.ps1" -Install
if errorlevel 1 echo Build or installation failed. See the output above.
pause
