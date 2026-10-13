@echo off
setlocal
rem Removes Office Days. Your attendance history in %APPDATA%\OfficeAttendance is kept.

set "APPDIR=%LOCALAPPDATA%\Programs\OfficeAttendance"
if exist "%APPDIR%\OfficeDays.exe" (
    "%APPDIR%\OfficeDays.exe" --exit
    ping -n 2 127.0.0.1 >nul
)
if exist "%APPDIR%\OfficeAttendance.exe" (
    "%APPDIR%\OfficeAttendance.exe" --exit
    ping -n 2 127.0.0.1 >nul
)
taskkill /im OfficeDays.exe /f >nul 2>&1
taskkill /im OfficeAttendance.exe /f >nul 2>&1
reg delete "HKCU\Software\Microsoft\Windows\CurrentVersion\Run" /v OfficeDays /f >nul 2>&1
reg delete "HKCU\Software\Microsoft\Windows\CurrentVersion\Run" /v OfficeAttendance /f >nul 2>&1
del "%APPDATA%\Microsoft\Windows\Start Menu\Programs\Office Days.lnk" >nul 2>&1
del "%APPDATA%\Microsoft\Windows\Start Menu\Programs\Office Attendance.lnk" >nul 2>&1
if exist "%APPDIR%" rmdir /s /q "%APPDIR%"

echo Office Days has been removed.
echo Your attendance history is still in "%APPDATA%\OfficeAttendance" - delete that folder if you no longer need it.
