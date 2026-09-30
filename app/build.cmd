@echo off
rem Builds EchoVRShade.exe with the custom ReShade DLL embedded (uses the C# compiler that ships with Windows).
cd /d "%~dp0"
"%WINDIR%\Microsoft.NET\Framework64\v4.0.30319\csc.exe" /nologo /target:winexe /platform:x64 /optimize ^
  /out:..\EchoVRShade.exe ^
  /resource:..\ReShade64-libovr.dll,ReShade64-libovr.dll ^
  /r:System.Windows.Forms.dll /r:System.Drawing.dll /r:System.Core.dll ^
  EchoVRShade.cs
