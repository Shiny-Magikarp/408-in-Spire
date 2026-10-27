@echo off
powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0import_essays.ps1" %*
pause
