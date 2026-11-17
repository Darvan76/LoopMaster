@echo off
setlocal
cd /d "%~dp0"
set "CSC=%WINDIR%\Microsoft.NET\Framework64\v4.0.30319\csc.exe"
if not exist "%CSC%" set "CSC=%WINDIR%\Microsoft.NET\Framework\v4.0.30319\csc.exe"
if not exist "%CSC%" (
    echo No se encontro el compilador de .NET Framework.
    pause
    exit /b 1
)
"%CSC%" /nologo /target:winexe /optimize+ /utf8output /codepage:65001 /win32manifest:app.manifest /reference:System.dll /reference:System.Core.dll /reference:System.Drawing.dll /reference:System.Windows.Forms.dll /reference:System.Net.Http.dll /reference:System.Runtime.Serialization.dll /reference:System.IO.Compression.dll /reference:System.IO.Compression.FileSystem.dll /out:LooperMaster.exe LooperMaster.cs Interfaz.cs Ventana.cs FfmpegSetup.cs Preparacion.cs
if errorlevel 1 (
    echo No se pudo compilar LooperMaster.
    pause
    exit /b 1
)
echo Compilacion terminada: LooperMaster.exe
endlocal
