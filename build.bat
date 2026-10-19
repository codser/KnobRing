@echo off
rem Builds KnobRing.exe with the C# compiler that ships with Windows (.NET Framework 4.x).
setlocal
cd /d "%~dp0"
set CSC=%WINDIR%\Microsoft.NET\Framework64\v4.0.30319\csc.exe
if not exist "%CSC%" set CSC=%WINDIR%\Microsoft.NET\Framework\v4.0.30319\csc.exe
if not exist out mkdir out
"%CSC%" /nologo /target:winexe /optimize+ /out:out\KnobRing.exe /win32icon:src\app.ico /resource:src\rings.default.json,rings.default.json /resource:src\bridge\manifest.xml,bridge.manifest.xml /resource:src\bridge\index.html,bridge.index.html /resource:src\bridge\main.js,bridge.main.js /resource:src\bridge\host.jsx,bridge.host.jsx /resource:src\bridge\panel\index.html,bridge.panel.index.html /resource:src\bridge\panel\panel.js,bridge.panel.panel.js /resource:src\bridge\panel\icons.js,bridge.panel.icons.js /r:System.dll /r:System.Drawing.dll /r:System.Windows.Forms.dll /r:System.Web.Extensions.dll src\*.cs
if errorlevel 1 (echo Build failed & exit /b 1)
echo Built out\KnobRing.exe
