param([string]$Configurazione = "Release")

# Prepara la cartella del sito (dist\wglettore, quella che WGStrumenti carica sul server) e lo zip (dist\wglettore.zip).
# Dentro ci sono i file del lettore, il programma di aggiornamento, i componenti aggiuntivi (cartella moduli) e eSpeak
# (cartella espeak: libespeak-ng.dll e i dati), così il lettore parte subito con la voce di eSpeak, senza scaricare niente.

$ErrorActionPreference = "Stop"
try {
    $radice = $PSScriptRoot
    $bin = Join-Path $radice "WGLettore\bin\$Configurazione"
    $cartellaUscita = Join-Path $radice "dist"
    $cartellaSito = Join-Path $cartellaUscita "wglettore"
    $uscita = Join-Path $cartellaUscita "wglettore.zip"

    $obbligatori = @("WGLettore.exe", "WGLettoreSchermo.dll", "WGLettoreVoce.exe", "WGLettoreServizio.exe",
                     "WGAiutoLettore32.dll", "WGAiutoLettore64.dll", "WGAiutoLettoreCarica32.exe", "WGAiutoLettoreCarica64.exe", "WGEspeak.dll")
    $facoltativi = @("WGLettore.exe.config", "Microsoft.Win32.Registry.dll", "System.Runtime.WindowsRuntime.dll",
                     "System.Security.AccessControl.dll", "System.Security.Principal.Windows.dll")
    $elenco = @()
    foreach ($nome in $obbligatori) {
        $percorso = Join-Path $bin $nome
        if (-not (Test-Path $percorso)) { throw "Manca $nome in $bin" }
        $elenco += $percorso
    }
    foreach ($nome in $facoltativi) {
        $percorso = Join-Path $bin $nome
        if (Test-Path $percorso) { $elenco += $percorso }
    }
    $aggiornatore = Join-Path $radice "WGLettoreAggiornatore\bin\$Configurazione\WGLettoreAggiornatore.exe"
    if (-not (Test-Path $aggiornatore)) { throw "Manca WGLettoreAggiornatore.exe in $aggiornatore" }
    $elenco += $aggiornatore
    $naudio = @(Get-ChildItem -Path $bin -Filter "NAudio*.dll" | ForEach-Object { $_.FullName })
    if ($naudio.Count -eq 0) { throw "Mancano le librerie NAudio in $bin" }
    $elenco += $naudio

    # eSpeak: la cartella con la libreria e i dati dell'italiano. Si cerca accanto al lettore compilato, poi in WinGabriel.
    $repoRadice = Split-Path -Parent $radice
    $candidateEspeak = @(
        (Join-Path $bin "espeak"),
        (Join-Path $repoRadice "WinGabriel\WinGabriel\bin\$Configurazione\espeak"),
        (Join-Path $repoRadice "WinGabriel\WinGabriel\bin\Release\espeak"),
        (Join-Path $repoRadice "WinGabriel\WinGabriel\bin\Debug\espeak"),
        (Join-Path $env:ProgramFiles "WinGabriel\espeak"),
        (Join-Path ${env:ProgramFiles(x86)} "WinGabriel\espeak")
    )
    $sorgenteEspeak = ""
    foreach ($cartella in $candidateEspeak) {
        if ((Test-Path (Join-Path $cartella "libespeak-ng.dll")) -and (Test-Path (Join-Path $cartella "espeak-ng-data\it_dict"))) { $sorgenteEspeak = $cartella; break }
    }
    if ($sorgenteEspeak -eq "") {
        throw ("Manca eSpeak (libespeak-ng.dll e espeak-ng-data\it_dict). Cercato in: " + ($candidateEspeak -join "; ") + ". Avvia il lettore una volta, che lo scarica accanto a sé, e rilancia.")
    }

    New-Item -ItemType Directory -Force $cartellaUscita | Out-Null
    if (Test-Path $cartellaSito) { Remove-Item $cartellaSito -Recurse -Force }
    New-Item -ItemType Directory -Force $cartellaSito | Out-Null
    foreach ($percorso in $elenco) { Copy-Item -LiteralPath $percorso -Destination $cartellaSito -Force }

    $destinazioneEspeak = Join-Path $cartellaSito "espeak"
    New-Item -ItemType Directory -Force $destinazioneEspeak | Out-Null
    Copy-Item -LiteralPath (Join-Path $sorgenteEspeak "libespeak-ng.dll") -Destination $destinazioneEspeak -Force
    Copy-Item -LiteralPath (Join-Path $sorgenteEspeak "espeak-ng-data") -Destination $destinazioneEspeak -Recurse -Force

    # Componenti aggiuntivi già compilati (per esempio Telegram.dll): la cartella moduli accanto al lettore.
    $moduli = Join-Path $bin "moduli"
    if (Test-Path $moduli) {
        $destinazioneModuli = Join-Path $cartellaSito "moduli"
        New-Item -ItemType Directory -Force $destinazioneModuli | Out-Null
        Get-ChildItem -LiteralPath $moduli -File | ForEach-Object { Copy-Item -LiteralPath $_.FullName -Destination $destinazioneModuli -Force }
    }

    # Lo zip è la cartella del sito, con i percorsi a barra normale (con le classi di .NET Framework le voci avrebbero la barra rovesciata).
    if (Test-Path $uscita) { Remove-Item $uscita -Force }
    Add-Type -AssemblyName System.IO.Compression
    Add-Type -AssemblyName System.IO.Compression.FileSystem
    $archivio = [System.IO.Compression.ZipFile]::Open($uscita, [System.IO.Compression.ZipArchiveMode]::Create)
    try {
        $base = (Resolve-Path $cartellaSito).Path.TrimEnd([char]92)
        foreach ($file in (Get-ChildItem -LiteralPath $base -File -Recurse)) {
            $nomeVoce = $file.FullName.Substring($base.Length + 1).Replace([string][char]92, '/')
            [void][System.IO.Compression.ZipFileExtensions]::CreateEntryFromFile($archivio, $file.FullName, $nomeVoce, [System.IO.Compression.CompressionLevel]::Optimal)
        }
    }
    finally { $archivio.Dispose() }
    Write-Output ("crea-wglettore-zip: fatto, eSpeak preso da " + $sorgenteEspeak)
    exit 0
}
catch {
    Write-Output ("crea-wglettore-zip: " + $_.Exception.Message)
    exit 1
}
