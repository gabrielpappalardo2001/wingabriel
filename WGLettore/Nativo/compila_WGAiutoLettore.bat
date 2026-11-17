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
cd /d "%~dp0"
call :compila x86 32
if errorlevel 1 exit /b 1
call :compila x64 64
if errorlevel 1 exit /b 1
del compila_WGAiutoLettore_errori.txt >nul 2>&1
endlocal
exit /b 0

:compila
setlocal
call "%VSDIR%\VC\Auxiliary\Build\vcvarsall.bat" %1 >nul
if errorlevel 1 (
    echo Impossibile caricare l'ambiente %1 di Visual Studio 2026.
    exit /b 1
)
cl /nologo /std:c++20 /EHsc /O2 /MT /W3 /utf-8 /DWIN32 /D_WINDOWS /DUNICODE /D_UNICODE /DWIN32_LEAN_AND_MEAN /DNOMINMAX /LD WGAiutoLettore.cpp /Fo:WGAiutoLettore%2.obj /Fe:WGAiutoLettore%2.dll /link /DEF:WGAiutoLettore.def oleacc.lib oleaut32.lib ole32.lib user32.lib uuid.lib >compila_WGAiutoLettore_errori.txt 2>&1
if errorlevel 1 (
    type compila_WGAiutoLettore_errori.txt
    echo Compilazione di WGAiutoLettore%2.dll non riuscita.
    exit /b 1
)
cl /nologo /std:c++20 /EHsc /O2 /MT /W3 /utf-8 /DWIN32 /D_WINDOWS /DUNICODE /D_UNICODE /DWIN32_LEAN_AND_MEAN /DNOMINMAX WGAiutoLettoreCarica.cpp /Fo:WGAiutoLettoreCarica%2.obj /Fe:WGAiutoLettoreCarica%2.exe /link /SUBSYSTEM:WINDOWS user32.lib shell32.lib >compila_WGAiutoLettore_errori.txt 2>&1
if errorlevel 1 (
    type compila_WGAiutoLettore_errori.txt
    echo Compilazione di WGAiutoLettoreCarica%2.exe non riuscita.
    exit /b 1
)
del WGAiutoLettore%2.obj WGAiutoLettore%2.lib WGAiutoLettore%2.exp WGAiutoLettoreCarica%2.obj >nul 2>&1
endlocal
exit /b 0
