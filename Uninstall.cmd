@echo off
powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%~dp0Patch.ps1" -Action Uninstall %*
set "PATCH_RESULT=%ERRORLEVEL%"
if not "%PATCH_NO_PAUSE%"=="1" pause
exit /b %PATCH_RESULT%
