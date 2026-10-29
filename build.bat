@echo off
setlocal
cd /d "%~dp0"

set "FW=%WINDIR%\Microsoft.NET\Framework64\v4.0.30319"
if not exist "%FW%\csc.exe" set "FW=%WINDIR%\Microsoft.NET\Framework\v4.0.30319"
if not exist "%FW%\csc.exe" goto nocsc

if not exist dist mkdir dist

echo [1/2] Lamplight.exe
"%FW%\csc.exe" /nologo /target:winexe /out:dist\Lamplight.exe ^
 /win32icon:assets\app.ico /win32manifest:assets\app.manifest ^
 /r:System.dll /r:System.Core.dll /r:System.Drawing.dll /r:System.Windows.Forms.dll ^
 /r:System.IO.Compression.dll /r:System.Xml.Linq.dll /r:Microsoft.CSharp.dll ^
 /r:"%FW%\System.Web.Extensions.dll" ^
 src\*.cs
if errorlevel 1 goto failed

echo [2/2] LamplightTts32.exe
"%FW%\csc.exe" /nologo /target:exe /platform:x86 /out:dist\LamplightTts32.exe helper\TtsHelper.cs
if errorlevel 1 goto failed

copy /y README.md dist\ >nul
copy /y LICENSE dist\ >nul
echo.
echo Build OK: %CD%\dist
goto end

:nocsc
echo csc.exe not found. Windows .NET Framework 4.x is required.
goto end

:failed
echo Build failed.

:end
pause
