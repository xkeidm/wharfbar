@echo off
REM WharfBar launcher — starts the EXE detached so closing this console does NOT kill the app
REM (and leave the Windows taskbar hidden). Quit via tray "Quit WharfBar" or Alt+F4.
cd /d "%~dp0"

dotnet build "%~dp0WharfBar.sln" -v q
if errorlevel 1 (
  echo Build failed.
  pause
  exit /b 1
)

set "EXE=%~dp0src\WharfBar\bin\Debug\net8.0-windows10.0.19041.0\WharfBar.exe"
if not exist "%EXE%" (
  echo Executable not found:
  echo   %EXE%
  pause
  exit /b 1
)

start "" "%EXE%"
