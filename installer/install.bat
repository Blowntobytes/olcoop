@echo off
setlocal
title olcoop installer
rem Installs Mod-olcoop.dll + the olcoop launchers into your OLMOD folder (the folder with olmod.exe and GameMod.dll - olmod loads
rem mods from there; usually the Overload folder, but olmod can live anywhere) and VERIFIES the installed copy.
rem Needs olmod (https://olmod.overloadmaps.com) already installed.
rem Finds olmod: OLPATH if set, next to this folder, then find-overload.ps1 (Overload folders on every drive, shortcuts to
rem olmod.exe on the Desktop / Start Menu / taskbar, usual folders), then asks.

if not "%OLPATH%"=="" goto :check
if exist "%~dp0..\olmod.exe" (set "OLPATH=%~dp0.." & goto :check)

echo Looking for olmod on all drives...
for /f "usebackq delims=" %%p in (`powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0find-overload.ps1"`) do set "OLPATH=%%p"
if not "%OLPATH%"=="" echo Found olmod in: %OLPATH%

if "%OLPATH%"=="" (
  echo olmod was not found automatically.
  echo Type the full path of the folder that contains olmod.exe
  echo ^(right-click your olmod shortcut, Open file location, copy the address bar^).
  set /p "OLPATH=olmod folder: "
)

:check
set "OLPATH=%OLPATH:"=%"
if "%OLPATH:~-1%"=="\" set "OLPATH=%OLPATH:~0,-1%"
if not exist "%OLPATH%\olmod.exe" (
  echo [X] olmod.exe not found in "%OLPATH%".
  echo     Install olmod 0.5.14 first ^(https://olmod.overloadmaps.com^), then run this again.
  goto :fail
)
if not exist "%OLPATH%\GameMod.dll" (
  echo [X] Found olmod.exe in "%OLPATH%", but not GameMod.dll - olmod looks incomplete there. Reinstall olmod 0.5.14.
  goto :fail
)
if not exist "%OLPATH%\Overload.exe" echo   Note: olmod is outside the Overload folder - that's fine, olcoop goes next to olmod.
set "SRC=%~dp0Mod-olcoop.dll"
set "DST=%OLPATH%\Mod-olcoop.dll"
for /f "usebackq delims=" %%v in (`powershell -NoProfile -Command "(Get-Item -LiteralPath '%SRC%').VersionInfo.ProductVersion"`) do set "NEWVER=%%v"
for /f "usebackq delims=" %%v in (`powershell -NoProfile -Command "if (Test-Path -LiteralPath '%DST%') { (Get-Item -LiteralPath '%DST%').VersionInfo.ProductVersion } else { 'not installed' }"`) do set "OLDVER=%%v"
echo.
echo   Installing olcoop %NEWVER%
echo   Currently installed: %OLDVER%
echo   Target folder: %OLPATH%
echo.
copy /Y "%SRC%" "%DST%" >nul
if errorlevel 1 (
  echo [X] Could not copy the DLL. Close Overload, or right-click install.bat and choose "Run as administrator".
  goto :fail
)
copy /Y "%~dp0olcoop*.bat" "%OLPATH%\" >nul
if not exist "%OLPATH%\olcoop_logs" mkdir "%OLPATH%\olcoop_logs"
rem Shortcut with the olcoop icon (a .bat file itself cannot have an icon): Desktop + game folder, both start olcoop.bat.
copy /Y "%~dp0olcoop.ico" "%OLPATH%\" >nul 2>nul
powershell -NoProfile -ExecutionPolicy Bypass -Command "$w=New-Object -ComObject WScript.Shell; foreach($d in @([Environment]::GetFolderPath('Desktop'), $env:OLPATH)){ $l=$w.CreateShortcut((Join-Path $d 'olcoop.lnk')); $l.TargetPath=(Join-Path $env:OLPATH 'olcoop.bat'); $l.WorkingDirectory=$env:OLPATH; $l.IconLocation=(Join-Path $env:OLPATH 'olcoop.ico'); $l.Description='Overload co-op (olcoop)'; $l.Save() }" >nul 2>nul
powershell -NoProfile -ExecutionPolicy Bypass -Command "$w=New-Object -ComObject WScript.Shell; foreach($d in @([Environment]::GetFolderPath('Desktop'), $env:OLPATH)){ Remove-Item -LiteralPath (Join-Path $d 'olcoop VR.lnk') -ErrorAction SilentlyContinue; foreach($v in @(@('olcoop SteamVR','olcoop-steamvr.bat','SteamVR'),@('olcoop Oculus','olcoop-oculus.bat','Oculus'))){ $l=$w.CreateShortcut((Join-Path $d ($v[0]+'.lnk'))); $l.TargetPath=(Join-Path $env:OLPATH $v[1]); $l.WorkingDirectory=$env:OLPATH; $l.IconLocation=(Join-Path $env:OLPATH 'olcoop.ico'); $l.Description=('Overload co-op (olcoop) in VR - '+$v[2]); $l.Save() } }" >nul 2>nul
del /Q "%OLPATH%\olcoop-vr.bat" 2>nul
if exist "%OLPATH%\olcoop.lnk" (echo   Shortcuts "olcoop", "olcoop SteamVR" and "olcoop Oculus" created on your Desktop and in the olmod folder.) else (echo   Note: could not create the olcoop shortcuts; start olcoop.bat, olcoop-steamvr.bat or olcoop-oculus.bat directly.)
fc /b "%SRC%" "%DST%" >nul
if errorlevel 1 (
  echo [X] VERIFY FAILED: the installed DLL differs from this one. Is the game still running? Close it and retry.
  goto :fail
)
for /f "usebackq delims=" %%v in (`powershell -NoProfile -Command "(Get-Item -LiteralPath '%DST%').VersionInfo.ProductVersion"`) do set "GOTVER=%%v"
echo   =====================================================
echo     OK - olcoop %GOTVER% is installed and verified.
echo   =====================================================
echo.
echo   Play: with Steam running, start the olcoop shortcut (Desktop) or olcoop.bat in "%OLPATH%" (your olmod folder).
echo   VR:   SteamVR headsets: start SteamVR, then "olcoop SteamVR" (olcoop-steamvr.bat).
echo         Oculus/Meta headsets: start the Oculus app, then "olcoop Oculus" (olcoop-oculus.bat).
echo   Main menu: CO-OP: HOST / JOIN  (bottom right).
echo.
pause
exit /b 0
:fail
echo.
echo   INSTALL DID NOT COMPLETE.
pause
exit /b 1
