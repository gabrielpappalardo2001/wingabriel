@echo off
setlocal EnableExtensions
cd /d "%~dp0"
set "RADICE=%~dp0"
set "NATIVO=%~dp0Nativo"
set "FORZA=0"
if /i "%~1"=="/forza" set "FORZA=1"

set "VSWHERE=%ProgramFiles(x86)%\Microsoft Visual Studio\Installer\vswhere.exe"
if not exist "%VSWHERE%" (
    echo vswhere.exe non trovato: installare Visual Studio 2026.
    exit /b 1
)
set "MSBUILD="
for /f "usebackq tokens=*" %%i in (`"%VSWHERE%" -latest -version [18.0^,19.0^) -products * -prerelease -requires Microsoft.Component.MSBuild -find MSBuild\**\Bin\MSBuild.exe`) do if not defined MSBUILD set "MSBUILD=%%i"
if not defined MSBUILD (
    echo MSBuild di Visual Studio 2026 non trovato.
    exit /b 1
)

call :nativo compila_WGLettoreVoce.bat "WGLettoreVoce.cpp" "WGLettoreVoce.exe"
if errorlevel 1 goto errore
call :nativo compila_WGLettoreServizio.bat "WGLettoreServizio.cpp" "WGLettoreServizio.exe"
if errorlevel 1 goto errore
call :nativo compila_WGAiutoLettore.bat "WGAiutoLettore.cpp WGAiutoLettoreCarica.cpp WGAiutoLettore.def" "WGAiutoLettore32.dll WGAiutoLettore64.dll WGAiutoLettoreCarica32.exe WGAiutoLettoreCarica64.exe"
if errorlevel 1 goto errore

cd /d "%RADICE%"
"%MSBUILD%" WGLettore.sln -restore -t:Rebuild -p:Configuration=Release -p:Platform="Any CPU" -m -nologo -v:quiet -clp:ErrorsOnly
if errorlevel 1 (
    echo Compilazione della soluzione WGLettore.sln non riuscita.
    goto errore
)

powershell -NoProfile -ExecutionPolicy Bypass -File "%RADICE%crea-wglettore-zip.ps1" -Configurazione Release
if errorlevel 1 (
    echo Creazione del pacchetto non riuscita.
    goto errore
)
endlocal
exit /b 0

:errore
endlocal
exit /b 1

:nativo
setlocal
cd /d "%NATIVO%"
if "%FORZA%"=="1" goto nativoCompila
powershell -NoProfile -ExecutionPolicy Bypass -Command "try { $s = '%~2'.Split(' '); $u = '%~3'.Split(' '); $m = $s | ForEach-Object { (Get-Item -LiteralPath $_).LastWriteTimeUtc } | Sort-Object | Select-Object -Last 1; foreach ($x in $u) { if (-not (Test-Path -LiteralPath $x)) { exit 1 }; if ((Get-Item -LiteralPath $x).LastWriteTimeUtc -lt $m) { exit 1 } }; exit 0 } catch { Write-Output ('Controllo date: ' + $_.Exception.Message); exit 1 }"
if not errorlevel 1 (
    endlocal
    exit /b 0
)
:nativoCompila
call "%NATIVO%\%~1"
if errorlevel 1 (
    echo %~1 non riuscito.
    endlocal
    exit /b 1
)
for %%f in (%~3) do (
    if not exist "%NATIVO%\%%f" (
        echo Dopo %~1 manca %%f.
        endlocal
        exit /b 1
    )
)
endlocal
exit /b 0
