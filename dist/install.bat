@echo off
setlocal
title olcoop installer
rem Installs Mod-olcoop.dll + launchers into Overload and VERIFIES the installed copy.
if "%OLPATH%"=="" set "OLPATH=C:\Program Files (x86)\Steam\steamapps\common\Overload"
set "SRC=%~dp0Mod-olcoop.dll"
set "DST=%OLPATH%\Mod-olcoop.dll"

if not exist "%OLPATH%\olmod.exe" (
  echo [X] olmod.exe not found in "%OLPATH%". Set OLPATH to your Overload folder and re-run.
  goto :fail
)
for /f "usebackq delims=" %%v in (`powershell -NoProfile -Command "(Get-Item -LiteralPath '%SRC%').VersionInfo.ProductVersion"`) do set "NEWVER=%%v"
for /f "usebackq delims=" %%v in (`powershell -NoProfile -Command "if (Test-Path -LiteralPath '%DST%') { (Get-Item -LiteralPath '%DST%').VersionInfo.ProductVersion } else { 'not installed' }"`) do set "OLDVER=%%v"
echo.
echo   Installing olcoop %NEWVER%
echo   Currently installed: %OLDVER%
echo   Target folder: %OLPATH%
echo.

copy /Y "%SRC%" "%DST%" >nul
if errorlevel 1 (
  echo [X] Could not copy the DLL. Right-click install.bat and choose "Run as administrator".
  goto :fail
)
copy /Y "%~dp0olcoop*.bat" "%OLPATH%\" >nul
if not exist "%OLPATH%\olcoop_logs" mkdir "%OLPATH%\olcoop_logs"

rem ---- verify: byte-for-byte compare + read version back from the installed file
fc /b "%SRC%" "%DST%" >nul
if errorlevel 1 (
  echo [X] VERIFY FAILED: installed DLL differs from this build. Is the game still running? Close it and retry.
  goto :fail
)
for /f "usebackq delims=" %%v in (`powershell -NoProfile -Command "(Get-Item -LiteralPath '%DST%').VersionInfo.ProductVersion"`) do set "GOTVER=%%v"
echo   =====================================================
echo     OK - olcoop %GOTVER% is installed and verified.
echo   =====================================================
echo.
echo   Co-op test: run olcoop-host.bat first, then olcoop-join.bat.
echo   The in-game log line [INIT] will also show %GOTVER%.
echo.
pause
exit /b 0

:fail
echo.
echo   INSTALL DID NOT COMPLETE.
pause
exit /b 1
