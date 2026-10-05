@echo off
setlocal
if "%OLPATH%"=="" set "OLPATH=C:\Program Files (x86)\Steam\steamapps\common\Overload"
del /Q "%OLPATH%\Mod-olcoop.dll" 2>nul
del /Q "%OLPATH%\olcoop.bat" "%OLPATH%\olcoop-host.bat" "%OLPATH%\olcoop-join.bat" 2>nul
del /Q "%OLPATH%\olcoop.ico" "%OLPATH%\olcoop.lnk" 2>nul
powershell -NoProfile -Command "Remove-Item -LiteralPath (Join-Path ([Environment]::GetFolderPath('Desktop')) 'olcoop.lnk') -ErrorAction SilentlyContinue" >nul 2>nul
echo Removed olcoop. olmod and the game are untouched. (Logs in "%OLPATH%\olcoop_logs" were kept.)
pause
