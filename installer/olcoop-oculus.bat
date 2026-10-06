@echo off
rem Overload co-op (olcoop) in VR through the Oculus / Meta runtime (Rift, Quest via Link or Air Link).
rem Start the Oculus app first, then this. Same as olcoop.bat plus -vrmode oculus.
cd /d "%~dp0"
if not exist olcoop_logs mkdir olcoop_logs
start "" olmod.exe -modded -vrmode oculus -runInBackground -logFile olcoop_logs\unity.log %*
