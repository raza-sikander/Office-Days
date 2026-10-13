@echo off
setlocal
rem Builds OfficeDays.exe with the C# compiler that ships with Windows (.NET Framework 4.x).
rem Usage: build.cmd [output path]   (default: bin\OfficeDays.exe)

set "CSC=%WINDIR%\Microsoft.NET\Framework64\v4.0.30319\csc.exe"
if not exist "%CSC%" set "CSC=%WINDIR%\Microsoft.NET\Framework\v4.0.30319\csc.exe"
if not exist "%CSC%" (
    echo Could not find the .NET Framework 4 C# compiler ^(csc.exe^).
    exit /b 1
)

set "OUT=%~1"
if "%OUT%"=="" set "OUT=%~dp0bin\OfficeDays.exe"
for %%F in ("%OUT%") do if not exist "%%~dpF" mkdir "%%~dpF"

"%CSC%" /nologo /target:winexe /optimize+ /codepage:65001 /out:"%OUT%" ^
    /reference:System.dll /reference:System.Core.dll /reference:System.Drawing.dll /reference:System.Windows.Forms.dll ^
    "%~dp0src\*.cs"
if errorlevel 1 (
    echo Build failed.
    exit /b 1
)
echo Built "%OUT%"
