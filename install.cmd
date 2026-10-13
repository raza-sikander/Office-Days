@echo off
setlocal
rem Builds Office Days, installs it for the current user (no admin needed) and starts it.

set "APPDIR=%LOCALAPPDATA%\Programs\OfficeAttendance"
set "EXE=%APPDIR%\OfficeDays.exe"
set "OLD=%APPDIR%\OfficeAttendance.exe"

echo Installing Office Days into "%APPDIR%" ...
if exist "%OLD%" (
    "%OLD%" --exit
    ping -n 2 127.0.0.1 >nul
)
if exist "%EXE%" (
    "%EXE%" --exit
    ping -n 2 127.0.0.1 >nul
)
taskkill /im OfficeAttendance.exe /f >nul 2>&1
taskkill /im OfficeDays.exe /f >nul 2>&1

call "%~dp0build.cmd" "%EXE%"
if errorlevel 1 exit /b 1
if exist "%OLD%" del /f "%OLD%" >nul 2>&1

del "%APPDATA%\Microsoft\Windows\Start Menu\Programs\Office Attendance.lnk" >nul 2>&1
powershell -NoProfile -ExecutionPolicy Bypass -Command ^
    "$s = (New-Object -ComObject WScript.Shell).CreateShortcut([Environment]::GetFolderPath('Programs') + '\Office Days.lnk');" ^
    "$s.TargetPath = $env:EXE; $s.WorkingDirectory = $env:APPDIR; $s.Description = 'Tracks the days you are in the office'; $s.Save()"

set MIGRATE=
reg query "HKCU\Software\Microsoft\Windows\CurrentVersion\Run" /v OfficeAttendance >nul 2>&1
if not errorlevel 1 set MIGRATE=1
reg query "HKCU\Software\Microsoft\Windows\CurrentVersion\Run" /v OfficeDays >nul 2>&1
if not errorlevel 1 set MIGRATE=1
if "%MIGRATE%"=="1" (
    reg add "HKCU\Software\Microsoft\Windows\CurrentVersion\Run" /v OfficeDays /d "\"%EXE%\" --background" /f >nul
    reg delete "HKCU\Software\Microsoft\Windows\CurrentVersion\Run" /v OfficeAttendance /f >nul 2>&1
)

start "" "%EXE%"
echo.
echo Done. Office Days is running - look for its icon in the notification area (bottom-right of the taskbar).
