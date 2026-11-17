@echo off
setlocal
set "VSWHERE=%ProgramFiles(x86)%\Microsoft Visual Studio\Installer\vswhere.exe"
if not exist "%VSWHERE%" (
    echo vswhere.exe non trovato: installare Visual Studio 2026 con gli strumenti C++.
    exit /b 1
)
set "VSDIR="
for /f "usebackq tokens=*" %%i in (`"%VSWHERE%" -version [18.0^,19.0^) -products * -prerelease -requires Microsoft.VisualStudio.Component.VC.Tools.x86.x64 -property installationPath`) do set "VSDIR=%%i"
if not defined VSDIR (
    echo Visual Studio 2026 con gli strumenti C++ non trovato.
    exit /b 1
)
call "%VSDIR%\VC\Auxiliary\Build\vcvarsall.bat" x64 >nul
if errorlevel 1 (
    echo Impossibile caricare l'ambiente a 64 bit di Visual Studio 2026.
    exit /b 1
)
cd /d "%~dp0"
cl /nologo /std:c++20 /EHsc /O2 /MT /W3 /utf-8 /DWIN32 /D_WINDOWS /DUNICODE /D_UNICODE WGLettoreVoce.cpp /Fe:WGLettoreVoce.exe /link /SUBSYSTEM:CONSOLE >compila_WGLettoreVoce_errori.txt 2>&1
if errorlevel 1 (
    type compila_WGLettoreVoce_errori.txt
    echo Compilazione non riuscita.
    exit /b 1
)
del compila_WGLettoreVoce_errori.txt >nul 2>&1
del WGLettoreVoce.obj >nul 2>&1
endlocal

