@echo off
rem Launch Overload with olmod + olcoop. Extra options: -coopnolog, -coopdump <seconds>
cd /d "%~dp0"
start "" olmod.exe -modded %*
