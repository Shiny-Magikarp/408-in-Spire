@echo off
powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%~dp0Preview.ps1"
if errorlevel 1 pause
