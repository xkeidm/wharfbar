@echo off
REM WharfBar launcher - builds, then starts a copy OUTSIDE the workspace.
REM
REM A process started from a sandboxed workspace folder gets a low-integrity token even
REM when Explorer launches it: Windows then silently drops the taskbar hide and the AppBar
REM reservation, and settings cannot be written (startup warns and the run is not valid
REM manual QA). The same binary copied into %LOCALAPPDATA% starts at normal (medium)
REM integrity. See docs/04-gotchas.md.
REM
REM The app is detached from this console, so closing the window does NOT kill it and
REM leave the taskbar hidden. Quit via the tray menu or the top-bar menu.
setlocal
cd /d "%~dp0"

dotnet build "%~dp0WharfBar.sln" -v q
if errorlevel 1 (
  echo Build failed.
  pause
  exit /b 1
)

set "SRC=%~dp0src\WharfBar\bin\Debug\net8.0-windows10.0.19041.0"
set "DST=%LOCALAPPDATA%\WharfBar\run"
if not exist "%SRC%\WharfBar.exe" (
  echo Executable not found:
  echo   %SRC%\WharfBar.exe
  pause
  exit /b 1
)

robocopy "%SRC%" "%DST%" /MIR /NFL /NDL /NJH /NJS /NP >nul
if errorlevel 8 (
  echo Copy failed:
  echo   %SRC% --^> %DST%
  pause
  exit /b 1
)

start "" "%DST%\WharfBar.exe"
