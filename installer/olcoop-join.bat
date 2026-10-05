@echo off
rem Co-op JOINER. Default joins this same PC (127.0.0.1). To join another PC: olcoop-join.bat 192.168.1.50
cd /d "%~dp0"
set HOSTIP=%1
if "%HOSTIP%"=="" set HOSTIP=127.0.0.1
start "" olmod.exe -modded -coopjoin %HOSTIP% -coopport 7777 -runInBackground -screen-fullscreen 0 -screen-width 1280 -screen-height 720 -logFile olcoop_logs\unity-join.log
