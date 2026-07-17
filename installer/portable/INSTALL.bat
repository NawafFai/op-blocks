@echo off
REM ============================================================
REM   ONE PROCESS Blocks - one-click installer (portable)
REM   Registers all 25 CAPE-OPEN blocks for Aspen Plus V14.
REM   Double-click this file and approve the Administrator prompt.
REM ============================================================
echo.
echo   Installing ONE PROCESS Blocks...
echo   (a Windows "Yes/No" Administrator prompt will appear)
echo.
powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%~dp0scripts\register-all-blocks.ps1"
echo.
echo   If a blue window opened and said SUCCESS, you are done.
echo   Open Aspen Plus V14, then Model Palette to the CAPE-OPEN tab.
echo.
pause
