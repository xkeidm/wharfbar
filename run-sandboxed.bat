@echo off
REM ============================================================================
REM Sandboxed-workspace launcher.
REM
REM In a sandboxed dev environment (an agent terminal, or a workspace whose
REM executables are started with a low-integrity token), WharfBar runs below
REM medium integrity: Windows silently drops the taskbar hide and the AppBar
REM reservation, and no settings can be written. A binary started from this
REM folder is affected even when Explorer launches it - it is the workspace
REM path itself that is sandboxed (measured; see docs/04-gotchas.md).
REM
REM So: build here, copy the output outside the workspace, and start that copy.
REM Close WharfBar normally (tray menu -> Quit, or the top-bar menu) - a force
REM kill skips the AppBar removal.
REM ============================================================================
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
  echo Copy failed.
  pause
  exit /b 1
)

start "" "%DST%\WharfBar.exe"
