@echo off
setlocal
title olcoop installer
rem Installs Mod-olcoop.dll + olcoop.bat into your Overload folder and VERIFIES the installed copy.
rem Needs olmod (https://olmod.overloadmaps.com) already installed in the Overload folder.
rem Finds Overload: next to this folder, OLPATH if set, Steam's library list (any drive), then common folders on every drive.

if not "%OLPATH%"=="" goto :check
if exist "%~dp0..\olmod.exe" (set "OLPATH=%~dp0.." & goto :check)

echo Looking for Overload on all drives...
for /f "usebackq delims=" %%p in (`powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0find-overload.ps1"`) do set "OLPATH=%%p"
if not "%OLPATH%"=="" echo Found: %OLPATH%

if "%OLPATH%"=="" (
  echo Overload was not found automatically.
  echo Type the full path of your Overload folder
  echo ^(Steam: right-click Overload, Manage, Browse local files, copy the address bar^).
  set /p "OLPATH=Overload folder: "
)

:check
set "OLPATH=%OLPATH:"=%"
if not exist "%OLPATH%\Overload.exe" (
  echo [X] Overload.exe not found in "%OLPATH%".
  goto :fail
)
if not exist "%OLPATH%\olmod.exe" (
  echo [X] Found Overload in "%OLPATH%", but olmod is not installed there.
  echo     Install olmod 0.5.14 into that folder first, then run this again.
  goto :fail
)
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
if exist "%OLPATH%\olcoop.lnk" (echo   Shortcut "olcoop" with the olcoop icon created on your Desktop and in the Overload folder.) else (echo   Note: could not create the olcoop shortcut; start olcoop.bat directly.)
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
echo   Play: with Steam running, start the olcoop shortcut (Desktop) or olcoop.bat in "%OLPATH%".
echo   Main menu: CO-OP: HOST / JOIN  (bottom right).
echo.
pause
exit /b 0
:fail
echo.
echo   INSTALL DID NOT COMPLETE.
pause
exit /b 1
