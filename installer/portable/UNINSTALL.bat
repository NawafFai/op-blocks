@echo off
REM ============================================================
REM   ONE PROCESS Blocks - uninstaller (portable)
REM   Removes all 25 CAPE-OPEN block registrations.
REM   Double-click and approve the Administrator prompt.
REM ============================================================
echo.
echo   Removing ONE PROCESS Blocks...
echo   (a Windows "Yes/No" Administrator prompt will appear)
echo.
powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%~dp0scripts\register-all-blocks.ps1" -Unregister
echo.
pause
