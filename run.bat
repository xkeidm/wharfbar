@echo off
REM WharfBar launcher. AppBar reserves the top edge; close via tray Quit or Alt+F4 — do not force-kill.
cd /d "%~dp0"
dotnet run --project src/WharfBar
if errorlevel 1 pause
