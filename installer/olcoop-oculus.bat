@echo off
rem Overload co-op (olcoop) in VR through the Oculus / Meta runtime (Rift, Quest via Link or Air Link).
rem Start the Oculus app first, then this. Same as olcoop.bat plus -vrmode oculus.
cd /d "%~dp0"
if not exist olcoop_logs mkdir olcoop_logs
rem 0.8.0: runs olcoop's own olmod 0.5.14 from this folder (Overload\olcoop). olmod finds the game in the parent folder and
rem loads only the mods in this folder. The Unity log path is absolute: olmod switches to the game folder before starting it.
start "" "%~dp0olmod.exe" -modded -vrmode oculus -runInBackground -logFile "%~dp0olcoop_logs\unity.log" %*
