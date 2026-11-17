Imports System.IO
Imports System.Net
Imports System.Security.Policy
Imports System.Runtime.InteropServices
Imports System.Runtime.CompilerServices
Imports System.Text
Imports wgcontrols
Imports WGFileManager
Imports WGDati
Imports System.Threading
Imports System.Threading.Tasks
Imports System.Windows.Forms
Imports System.Xml
Imports System.ComponentModel
Imports System.Security
Imports System.Diagnostics
Imports WGYoutube
Imports System.Linq
Imports System.Collections.Concurrent
Imports System.Security.Principal
Imports System.Management
Imports WGRadioTv
Imports WGSalute
Imports WGOpenStrictMap
Imports Newtonsoft.Json.Linq
Imports HtmlAgilityPack
Imports System.Text.RegularExpressions
Imports System.Data.SQLite
Imports WGLibriTesto
Imports WGInternetArchive
Imports WGMediaPlayer
Imports WGIA
Imports WGMail
Imports WGStampante
Imports WGContatti
Imports WGConnessioni

' Gestione dell'assistente vocale, riconoscimento vocale offline con modelli Vosk e interazione con i comandi.
Public Class AssistenteVocale
    Inherits NativeWindow

    Public Const ID_CONFIG_ASSISTENTE_VOCALE As String = "assistente_vocale_attivo"
    Public Const ID_CONFIG_QUALITA_VOSK As String = "assistente_vocale_qualita_vosk"
    Public Const ID_CONFIG_PAROLA_ATTIVAZIONE As String = "assistente_vocale_parola_attivazione"
    Public Const ID_CONFIG_SENSIBILITA_JARVIS As String = "assistente_vocale_sensibilita_jarvis"
    Public Const ID_CONFIG_MOTORE_COMANDI As String = "assistente_vocale_motore_comandi"
    Public Const ID_CONFIG_MOTORE_MINERVA As String = "assistente_vocale_motore_minerva"
    Public Const ID_CONFIG_ELABORAZIONE_COMANDI As String = "assistente_vocale_elaborazione"

    Public Enum ModoAttivazione
        Nessuno
        Windows
        Vosk
        Jarvis
        Parakeet
    End Enum

    Public Enum ElaborazioneComandi
        Gemini
        Groq
        Locale
    End Enum

    Public Enum MotoreMinerva
        Vosk
        Parakeet
    End Enum

    Public Enum ParolaAttivazione
        HeyJarvis
        EhiMinerva
    End Enum

    Public Enum MotoreComandiLocale
        Vosk
        Parakeet
    End Enum

    Public Enum SensibilitaParola
        Alta
        Media
        Bassa
    End Enum

    Public Enum FaseAssistente
        Ascolto
        Registrazione
    End Enum

    Public Enum QualitaVosk
        Bassa
        Alta
    End Enum

    Private Const NOME_CARTELLA_VOSK As String = "vosk"
    Private Const NOME_CARTELLA_MODELLO As String = "modello"
    Private Const NOME_CARTELLA_MODELLO_ALTA As String = "modello_alta"
    Private Const NOME_CARTELLA_LIBRERIE As String = "librerie"
    Private Const NOME_CARTELLA_ASSISTENTE As String = "assistente"
    Private Const NOME_FILE_VAD As String = "silero_vad.onnx"
    Private Const URL_MODELLO As String = "https://alphacephei.com/vosk/models/vosk-model-small-it-0.22.zip"
    Private Const URL_MODELLO_ALTA As String = "https://alphacephei.com/vosk/models/vosk-model-it-0.22.zip"
    Private Const URL_RIDUTTORE_1 As String = "https://raw.githubusercontent.com/breizhn/DTLN/master/pretrained_model/model_1.onnx"
    Private Const URL_RIDUTTORE_2 As String = "https://raw.githubusercontent.com/breizhn/DTLN/master/pretrained_model/model_2.onnx"
    Private Const NOME_FILE_RIDUTTORE_1 As String = "dtln_1.onnx"
    Private Const NOME_FILE_RIDUTTORE_2 As String = "dtln_2.onnx"
    Private Const DIMENSIONE_MINIMA_RIDUTTORE As Long = 1000000L
    Private Const MEGABYTE As Long = 1048576L
    Private Const SPAZIO_SCARICAMENTO_BASSA As Long = 400L * MEGABYTE
    Private Const SPAZIO_SCARICAMENTO_ALTA As Long = 5120L * MEGABYTE
    Private Const MEMORIA_MINIMA_BASSA As Long = 4096L * MEGABYTE
    Private Const MEMORIA_MINIMA_ALTA As Long = 8192L * MEGABYTE
    Private Const MEMORIA_LIBERA_ALTA As Long = 3072L * MEGABYTE
    Private Const PROCESSORI_MINIMI_BASSA As Integer = 2
    Private Const PROCESSORI_MINIMI_ALTA As Integer = 4
    Private Const RAPPORTO_TEMPO_REALE_MASSIMO As Double = 0.5
    Private Const RAPPORTO_TEMPO_REALE_MASSIMO_FRASI As Double = 1.5
    Private Const MILLISECONDI_PROVA_PRESTAZIONI As Integer = 2000
    Private Const ALTERNATIVE_COMANDI As Integer = 5
    Private Const PF_SSE3_INSTRUCTIONS_AVAILABLE As Integer = 13
    Private Const MODELLO_STOP_LOCALE As String = "^(?:stop|basta|fine|chiudi|ferma(?:ti)?|esci|grazie\s+basta|ho\s+finito)\b"
    Private Const MODELLO_ERRORE_RETE_O_QUOTA As String = "\b(?:429|50[234])\b|quota|resource[_\s]?exhausted|rate[_\s]?limit|too\s+many\s+requests|\blimit|esaurit|time[d]?\s?out|scadut|connessi|connect|network|\brete\b|resolv|risolt|risolver|\bhost\b|internet|\bdns\b|unavailable|raggiungibil|socket|\bssl\b|\btls\b|webexception|httprequestexception|taskcanceled|operationcanceled"
    Private Const URL_LIBRERIE As String = "https://github.com/alphacep/vosk-api/releases/download/v0.3.45/vosk-win64-0.3.45.zip"
    Private Const URL_MODELLO_VAD As String = "https://raw.githubusercontent.com/snakers4/silero-vad/master/src/silero_vad/data/silero_vad.onnx"
    Private Const NOME_EVENTO_ATTIVAZIONE As String = "WGParolaAttivazioneRilevata"
    Private Const FREQUENZA_CAMPIONAMENTO As Integer = 16000
    Private Const BYTE_PER_SECONDO As Integer = FREQUENZA_CAMPIONAMENTO * 2
    Private Const BYTE_MASSIMI_FRASE As Integer = BYTE_PER_SECONDO * 30
    Private Const BYTE_BUFFER_CIRCOLARE As Integer = BYTE_PER_SECONDO * 3
    Private Const BYTE_ANTICIPO_ATTIVAZIONE As Integer = BYTE_PER_SECONDO * 3 \ 2
    Private Const BYTE_ANTICIPO_VERIFICA As Integer = BYTE_PER_SECONDO * 2
    Private Const BYTE_BLOCCO_LIVELLO As Integer = BYTE_PER_SECONDO \ 10
    Private Const FINESTRE_ANTICIPO_FRASE As Integer = 16
    Private Const MS_PER_FINESTRA As Integer = 32
    Private Const SOGLIA_INIZIO_VOCE As Single = 0.5F
    Private Const SOGLIA_FINE_VOCE As Single = 0.35F
    Private Const FINESTRE_CONFERMA_VOCE As Integer = 3
    Private Const MS_SILENZIO_FINE_FRASE As Integer = 1000
    Private Const MS_SILENZIO_FINE_TESTO_EMAIL As Integer = 2500
    Private Const MS_FINESTRA_FRASI_SPURIE As Integer = 2500
    Private Const BYTE_MASSIMI_FRASE_SPURIA As Integer = BYTE_PER_SECONDO * 7 \ 10
    Private Const BYTE_MINIMI_ANTICIPO_PARAKEET As Integer = BYTE_PER_SECONDO \ 10
    Private Const BYTE_MASSIMI_TESTO_EMAIL As Integer = BYTE_PER_SECONDO * 90
    Private Const MINUTI_VALIDITA_DIALOGO_EMAIL As Integer = 3
    Private Const NOME_FILE_SOSTITUZIONI As String = "sostituzioni_vocali.txt"
    Private Const INDIRIZZO_SUGGERIMENTI_YOUTUBE As String = "https://suggestqueries.google.com/complete/search?client=firefox&ds=yt&hl=it&q="
    Private Const SOMIGLIANZA_MINIMA_SUGGERIMENTO As Double = 0.6
    Private Const MODELLO_ANNULLA_EMAIL As String = "^(?:annulla(?:re)?|annulla\s+(?:la\s+)?(?:mail|e-?mail|posta)|lascia\s+perdere|non\s+inviare|non\s+la\s+mandare|stop)$"
    Private Const MODELLO_CONFERMA_EMAIL As String = "^(?:s[iì]|s[iì]\s+inviala|inviala|invia(?:la)?|manda(?:la)?|spedisci(?:la)?|conferm[oa]|ok|okay|va\s+bene|certo|d'accordo)$"
    Private Const MODELLO_VERBO_INVIO_EMAIL As String = "\b(?:in\s*vi[aà](?:la|mela|re)?|invio|manda(?:la|mela|re)?|spedisci(?:la)?|spedire)\b"
    Private Const MODELLO_CONTROLLO_EMAIL As String = "^(?:(?:voglio\s+)?controlla(?:re|la|rla)?|verifica(?:re|la|rla)?|apri(?:la|re)?|mostra(?:la|mela|re)?|fam+ela\s+vedere|fammi\s+vedere|la\s+controllo\s+io|controllo\s+io)(?:\s+(?:la\s+)?(?:mail|e-?mail|posta|a\s+mano))?$"
    Private Const MODELLO_NESSUN_OGGETTO As String = "^(?:nessuno|nessun\s+oggetto|senza\s+oggetto|niente|no|non\s+c'[eè])$"
    Private Const MODELLO_FINE_TESTO_EMAIL As String = "[\s,.;:!?]*\b(?:fine\s+(?:del\s+)?(?:testo|messaggio|mail))\b[\s,.;:!?]*$"

    Private Enum PassoEmail
        Nessuno
        Destinatario
        Oggetto
        Testo
        Conferma
    End Enum

    Private Shared ReadOnly _bloccoSostituzioni As New Object()
    Private Shared _sostituzioni As New List(Of Tuple(Of Regex, String))
    Private Shared _dataFileSostituzioni As DateTime = DateTime.MinValue
    Private Const MS_MINIMI_PARLATO As Integer = 300
    Private Const SECONDI_SILENZIO_CHIUSURA As Integer = 30
    Private Const SECONDI_SILENZIO_PRIMA_FRASE As Integer = 10
    Private Const MILLISECONDI_PAUSA_DOPO_SINTESI As Integer = 700
    Private Const SECONDI_ATTESA_INIZIO_SINTESI As Integer = 4
    Private Const INTERVALLO_CONTROLLO_MS As Integer = 150
    Private Const MILLISECONDI_BLOCCO_DOPO_CHIUSURA As Integer = 2000
    Private Const SECONDI_MASSIMI_ELABORAZIONE As Integer = 120
    Private Const SECONDI_MASSIMI_SENZA_DATI_MICROFONO As Integer = 3
    Private Const SECONDI_MINIMI_TRA_RIAVVII_MICROFONO As Integer = 3
    Private Const MILLISECONDI_TOLLERANZA_LETTURE As Integer = 2500
    Private Const SECONDI_MASSIMI_SINTESI_SENZA_EVENTI As Integer = 120
    Private Const GRAMMATICA_ATTIVAZIONE As String = "[""ehi minerva"", ""ei minerva"", ""hey minerva"", ""eh minerva"", ""e minerva"", ""minerva"", ""[unk]""]"
    Private Const MODELLO_PAROLA_ATTIVAZIONE As String = "\bminerv"
    Private Const MODELLO_PREFISSO_ATTIVAZIONE As String = "^(?:ehi|ei|hey|hei|eh|e)$"
    Private Const MODELLO_PREFISSO_ATTIVAZIONE_LIBERO As String = "^(?:ehi|ei|hey|hei|eh|e|ah|ai)$"
    Private Const MODELLO_VERIFICA_ATTIVAZIONE As String = "minerv|inerva|nerva|minerb|inerba"
    Private Const MODELLO_PAROLA_BREVE_INIZIALE As String = "^\p{L}{1,3}$"
    Private Const SOGLIA_CONFIDENZA_ATTIVAZIONE As Double = 0.7
    Private Const SOGLIA_CONFIDENZA_ATTIVAZIONE_LIBERA As Double = 0.65
    Private Const SOGLIA_CONFIDENZA_SENZA_VERIFICA As Double = 0.9
    Private Const SECONDI_MASSIMI_FRASE_ATTIVAZIONE As Integer = 4
    Private Const SOGLIA_CONFIDENZA_VERIFICA As Double = 0.45
    Private Const MODELLO_ATTIVAZIONE As String = "\b(?:(?:e[hiy]|ehi|hey|hei|ei)[\s,]+)?(?:jarvis|giarvis|jarvi|minerva)\b"
    Private Const CONFERME_ATTIVAZIONE As Integer = 2
    Private Const SECONDI_MASSIMI_ATTESA_AVVIO As Integer = 180
    Private Const SECONDI_MINIMI_TRA_LOG_ATTIVAZIONE As Integer = 5
    Private Shared ReadOnly NOMI_ATTIVAZIONE As String() = {"minerva"}
    Private Shared ReadOnly PREFISSI_ATTIVAZIONE As String() = {"ehi", "ei", "hey", "hei", "eh", "e"}
    Private Const SOGLIA_JARVIS_ALTA As Single = 0.15F
    Private Const CONFERME_JARVIS_ALTA As Integer = 2
    Private Const CONFERME_JARVIS_NORMALI As Integer = 1
    Private Const SOGLIA_JARVIS_MEDIA As Single = 0.35F
    Private Const SOGLIA_JARVIS_BASSA As Single = 0.5F
    Private Const MARGINE_LOG_JARVIS As Single = 0.15F
    Private Const MILLISECONDI_ATTESA_SEGNALE As Integer = 600
    Private Const SECONDI_CONTROLLO_CONNESSIONE_ASSENTE As Integer = 15
    Private Const SECONDI_CONTROLLO_CONNESSIONE_PRESENTE As Integer = 60
    Private Const SECONDI_CONTROLLO_RIAVVIO As Integer = 30
    Private Const MILLISECONDI_RITARDO_CAMBIO_RETE As Integer = 3000
    Private Const MILLISECONDI_DOPO_SEGNALE As Integer = 400
    Private Const MILLISECONDI_SENZA_DATI_REGISTRAZIONE As Integer = 1000
    Private Const RIAVVII_MASSIMI_MICROFONO_SESSIONE As Integer = 3
    Private Const MILLISECONDI_VALIDITA_SEGMENTO_PARAKEET As Integer = 3000
    Private Const URL_BASE_OPENWAKEWORD As String = "https://github.com/dscripka/openWakeWord/releases/download/v0.5.1/"
    Private Const NOME_FILE_MEL As String = "melspectrogram.onnx"
    Private Const NOME_FILE_EMBEDDING As String = "embedding_model.onnx"
    Private Const NOME_FILE_JARVIS As String = "hey_jarvis_v0.1.onnx"
    Private Const DIMENSIONE_MINIMA_MODELLO_PAROLA As Long = 500000L
    Private Const SOGLIA_RMS_ATTIVAZIONE As Double = 250.0

    Private Const WM_HOTKEY As Integer = &H312
    Private Const ID_TASTO_RAPIDO As Integer = &HA7C1
    Private Const MOD_CONTROL As Integer = &H2
    Private Const MOD_SHIFT As Integer = &H4
    Private Const MOD_NOREPEAT As Integer = &H4000
    Private Const LOAD_WITH_ALTERED_SEARCH_PATH As UInteger = &H8UI
    Private Const ORIGINE_ASSISTENTE As String = "assistente_vocale"

    Private Class RichiestaVocale
        Public Property Id As Integer = 0
        Public Property Testo As String = ""
        Public Property Audio As Byte()
        Public Property Sessione As Integer = -1
        Public Property PrimaFrase As Boolean = False
    End Class

    Private Class RisultatoEsterno
        Public Property IdRichiesta As Integer = 0
        Public Property Domanda As String = ""
        Public Property Comando As ComandoVocale
        Public Property Risposta As String = ""
        Public Property Errore As String = ""
        Public Property ChiudiSessione As Boolean = False
        Public Property Ignora As Boolean = False
        Public Property Sessione As Integer = -1
    End Class

    Private Class CandidatoLocale
        Public Property Testo As String = ""
        Public Property Ordine As Integer = 0
    End Class

    Private Class BufferCircolareAudio
        Private ReadOnly _dati As Byte()
        Private _posizione As Integer = 0
        Private _pieno As Boolean = False

        Public Sub New(dimensione As Integer)
            Try
                _dati = New Byte(Math.Max(2, dimensione) - 1) {}
            Catch ex As Exception
                log.ScriviLog("AssistenteVocale.BufferCircolareAudio.New: " & ex.Message)
                _dati = New Byte(1) {}
            End Try
        End Sub

        Public Sub Azzera()
            Try
                _posizione = 0
                _pieno = False
            Catch ex As Exception
                log.ScriviLog("AssistenteVocale.BufferCircolareAudio.Azzera: " & ex.Message)
            End Try
        End Sub

        Public Sub Accoda(buffer As Byte(), lunghezza As Integer)
            Try
                If buffer Is Nothing OrElse lunghezza <= 0 Then Exit Sub
                Dim scritti As Integer = 0
                While scritti < lunghezza
                    Dim blocco As Integer = Math.Min(lunghezza - scritti, _dati.Length - _posizione)
                    Array.Copy(buffer, scritti, _dati, _posizione, blocco)
                    scritti += blocco
                    _posizione += blocco
                    If _posizione >= _dati.Length Then
                        _posizione = 0
                        _pieno = True
                    End If
                End While
            Catch ex As Exception
                log.ScriviLog("AssistenteVocale.BufferCircolareAudio.Accoda: " & ex.Message)
            End Try
        End Sub

        Public Function Ultimi(quantiByte As Integer) As Byte()
            Try
                Dim disponibili As Integer = If(_pieno, _dati.Length, _posizione)
                Dim lunghezza As Integer = Math.Min(quantiByte, disponibili)
                lunghezza -= lunghezza Mod 2
                If lunghezza <= 0 Then Return New Byte() {}
                Dim risultato(lunghezza - 1) As Byte
                Dim inizio As Integer = _posizione - lunghezza
                If inizio >= 0 Then
                    Array.Copy(_dati, inizio, risultato, 0, lunghezza)
                Else
                    Dim primaParte As Integer = -inizio
                    Array.Copy(_dati, _dati.Length - primaParte, risultato, 0, primaParte)
                    Array.Copy(_dati, 0, risultato, primaParte, lunghezza - primaParte)
                End If
                Return risultato
            Catch ex As Exception
                log.ScriviLog("AssistenteVocale.BufferCircolareAudio.Ultimi: " & ex.Message)
                Return New Byte() {}
            End Try
        End Function
    End Class

    <StructLayout(LayoutKind.Sequential, CharSet:=CharSet.Auto)>
    Private Structure STATO_MEMORIA
        Public dwLength As UInteger
        Public dwMemoryLoad As UInteger
        Public ullTotalPhys As ULong
        Public ullAvailPhys As ULong
        Public ullTotalPageFile As ULong
        Public ullAvailPageFile As ULong
        Public ullTotalVirtual As ULong
        Public ullAvailVirtual As ULong
        Public ullAvailExtendedVirtual As ULong
    End Structure

    <DllImport("kernel32.dll", SetLastError:=True)>
    Private Shared Function GlobalMemoryStatusEx(ByRef stato As STATO_MEMORIA) As <MarshalAs(UnmanagedType.Bool)> Boolean
    End Function

    <DllImport("kernel32.dll")>
    Private Shared Function IsProcessorFeaturePresent(caratteristica As Integer) As <MarshalAs(UnmanagedType.Bool)> Boolean
    End Function

    <DllImport("user32.dll", SetLastError:=True)>
    Private Shared Function RegisterHotKey(hWnd As IntPtr, id As Integer, fsModifiers As Integer, vk As Integer) As Boolean
    End Function

    <DllImport("user32.dll", SetLastError:=True)>
    Private Shared Function UnregisterHotKey(hWnd As IntPtr, id As Integer) As Boolean
    End Function

    <DllImport("kernel32.dll", CharSet:=CharSet.Unicode, SetLastError:=True)>
    Private Shared Function LoadLibraryEx(lpFileName As String, hFile As IntPtr, dwFlags As UInteger) As IntPtr
    End Function

    <DllImport("libvosk.dll", CallingConvention:=CallingConvention.Cdecl)>
    Private Shared Sub vosk_set_log_level(livello As Integer)
    End Sub

    <DllImport("libvosk.dll", CallingConvention:=CallingConvention.Cdecl)>
    Private Shared Function vosk_model_new(percorso As Byte()) As IntPtr
    End Function

    <DllImport("libvosk.dll", CallingConvention:=CallingConvention.Cdecl)>
    Private Shared Sub vosk_model_free(modello As IntPtr)
    End Sub

    <DllImport("libvosk.dll", CallingConvention:=CallingConvention.Cdecl)>
    Private Shared Function vosk_model_find_word(modello As IntPtr, parola As Byte()) As Integer
    End Function

    <DllImport("libvosk.dll", CallingConvention:=CallingConvention.Cdecl)>
    Private Shared Function vosk_recognizer_new(modello As IntPtr, frequenza As Single) As IntPtr
    End Function

    <DllImport("libvosk.dll", CallingConvention:=CallingConvention.Cdecl)>
    Private Shared Function vosk_recognizer_new_grm(modello As IntPtr, frequenza As Single, grammatica As Byte()) As IntPtr
    End Function

    <DllImport("libvosk.dll", CallingConvention:=CallingConvention.Cdecl)>
    Private Shared Sub vosk_recognizer_free(riconoscitore As IntPtr)
    End Sub

    <DllImport("libvosk.dll", CallingConvention:=CallingConvention.Cdecl)>
    Private Shared Function vosk_recognizer_accept_waveform(riconoscitore As IntPtr, dati As Byte(), lunghezza As Integer) As Integer
    End Function

    <DllImport("libvosk.dll", CallingConvention:=CallingConvention.Cdecl)>
    Private Shared Function vosk_recognizer_result(riconoscitore As IntPtr) As IntPtr
    End Function

    <DllImport("libvosk.dll", CallingConvention:=CallingConvention.Cdecl)>
    Private Shared Function vosk_recognizer_partial_result(riconoscitore As IntPtr) As IntPtr
    End Function

    <DllImport("libvosk.dll", CallingConvention:=CallingConvention.Cdecl)>
    Private Shared Sub vosk_recognizer_reset(riconoscitore As IntPtr)
    End Sub

    <DllImport("libvosk.dll", CallingConvention:=CallingConvention.Cdecl)>
    Private Shared Sub vosk_recognizer_set_words(riconoscitore As IntPtr, attivo As Integer)
    End Sub

    <DllImport("libvosk.dll", CallingConvention:=CallingConvention.Cdecl)>
    Private Shared Function vosk_recognizer_final_result(riconoscitore As IntPtr) As IntPtr
    End Function

    <DllImport("libvosk.dll", CallingConvention:=CallingConvention.Cdecl)>
    Private Shared Sub vosk_recognizer_set_max_alternatives(riconoscitore As IntPtr, alternative As Integer)
    End Sub

    Private Shared _istanza As AssistenteVocale
    Private Shared _libreriaCaricata As Boolean = False
    Private Shared ReadOnly _bloccoLibreria As New Object()
    Private Shared _onnxVerificato As Boolean = False
    Private Shared _onnxDisponibile As Boolean = False
    Private Shared _motivoOnnx As String = ""

    Private Shared _scaricamentoInCorso As Boolean = False
    Private Shared _scaricamentoAnnullato As Boolean = False
    Private Shared _attesaScaricamento As WGProcessoInCorso
    Private Shared _marshalScaricamento As System.Windows.Forms.Control
    Private Shared _esitoScaricamento As String = ""

    Private Shared _attesaVerifica As WGProcessoInCorso
    Private Shared _modoVerificato As ModoAttivazione = ModoAttivazione.Nessuno
    Private Shared _verificaDaImpostazioni As Boolean = False
    Private Shared _domandaScaricamento As WGDomanda
    Private Shared _qualitaScaricamento As QualitaVosk = QualitaVosk.Bassa
    Private Shared _qualitaVerificata As QualitaVosk = QualitaVosk.Bassa
    Private Shared _localeVerificato As Boolean = False
    Private Shared _motivoLocaleVerificato As String = ""
    Private Shared _cloudVerificato As Boolean = False
    Private Shared _qualitaDaRipristinare As QualitaVosk = QualitaVosk.Bassa
    Private Shared _ripristinoQualitaInSospeso As Boolean = False

    Private Shared _parolaDaRipristinare As ParolaAttivazione = ParolaAttivazione.HeyJarvis
    Private Shared _ripristinoParolaInSospeso As Boolean = False
    Private Shared _motivoRipiegoParakeet As String = ""
    Private Shared _messaggioElaborazioneVerificata As String = ""
    Private Shared _elaborazioneDaRipristinare As ElaborazioneComandi = ElaborazioneComandi.Gemini
    Private Shared _ripristinoElaborazioneInSospeso As Boolean = False
    Private Shared _timerRiavvio As System.Windows.Forms.Timer
    Private Shared _verificaRiavvioInCorso As Integer = 0
    Private Shared _attesaAvvio As WGProcessoInCorso
    Private Shared _completamentoAvvio As Action
    Private Shared _timerAttesaAvvio As System.Windows.Forms.Timer
    Private _ultimoLogAttivazione As DateTime = DateTime.MinValue

    Private ReadOnly _interprete As New InterpreteComandiVocali()
    Private ReadOnly _bloccoRiconoscitore As New Object()
    Private _modo As ModoAttivazione = ModoAttivazione.Nessuno
    Private _marshal As System.Windows.Forms.Control
    Private _qualita As QualitaVosk = QualitaVosk.Bassa
    Private _localeRichiesto As Boolean = False
    Private _localeDisponibile As Boolean = False
    Private _modelloAttivazione As IntPtr = IntPtr.Zero
    Private _modelloComandi As IntPtr = IntPtr.Zero
    Private _riconoscitore As IntPtr = IntPtr.Zero
    Private _riconoscitoreAttivazione As IntPtr = IntPtr.Zero
    Private _riconoscitoreComandi As IntPtr = IntPtr.Zero
    Private _timerRilascioComandi As System.Threading.Timer
    Private _qualitaAttivazione As QualitaVosk = QualitaVosk.Bassa
    Private _probabilitaVoceMassima As Single = 0.0F
    Private _finestreAnalizzate As Integer = 0
    Private _bufferRicevutiSessione As Integer = 0
    Private _bufferSospesiSessione As Integer = 0
    Private _motiviSospensioneSessione As String = ""
    Private _livelloMassimo As Double = 0.0
    Private _guadagnoCorrente As Double = 2.0
    Private Const LIVELLO_OBIETTIVO As Double = 3000.0
    Private Const LIVELLO_RUMORE As Double = 80.0
    Private Const GUADAGNO_MASSIMO As Double = 10.0
    Private Const RISALITA_GUADAGNO As Double = 0.15
    Private _attivazioneSoloBassa As Boolean = False
    Private _verificaParakeetInCorso As Integer = 0
    Private Const MINUTI_RILASCIO_COMANDI As Integer = 5
    Private _rilevatoreParola As RilevatoreParolaAttivazione
    Private _rilevatoreDaRilasciare As RilevatoreVoce
    Private _rilevatoreParolaDaRilasciare As RilevatoreParolaAttivazione
    Private _sogliaJarvis As Single = SOGLIA_JARVIS_MEDIA
    Private _confermeJarvis As Integer = CONFERME_JARVIS_NORMALI
    Private _statoConnessione As Integer = -1
    Private _timerConnessione As System.Windows.Forms.Timer
    Private _verificaConnessioneInCorso As Integer = 0
    Private _eventiReteAgganciati As Boolean = False
    Private _timerRitardoRete As System.Threading.Timer
    Private ReadOnly _bloccoRitardoRete As New Object()
    Private _ignoraAudioFino As DateTime = DateTime.MinValue
    Private ReadOnly _bloccoComandi As New Object()
    Private _preElaborazioneAscolto As PreElaborazioneVoce
    Private _preElaborazioneFrasi As PreElaborazioneVoce
    Private _rilevatore As RilevatoreVoce
    Private _microfono As NAudio.Wave.WaveInEvent
    Private _timerControllo As System.Windows.Forms.Timer
    Private _timerControlloSospeso As Boolean = False
    Private _processoAttesaAi As WGProcessoInCorso = Nothing
    Private _tastoRegistrato As Boolean = False
    Private _arrestato As Boolean = False
    Private _threadServizio As Thread
    Private ReadOnly _fermaAscoltoServizio As New ManualResetEvent(False)

    Private ReadOnly _bufferGrezzo As New BufferCircolareAudio(BYTE_BUFFER_CIRCOLARE)
    Private ReadOnly _bufferNonAmplificato As New BufferCircolareAudio(BYTE_BUFFER_CIRCOLARE)
    Private ReadOnly _bufferElaborato As New BufferCircolareAudio(BYTE_BUFFER_CIRCOLARE)

    Private ReadOnly _campioniVad As New List(Of Single)
    Private ReadOnly _byteOriginaliVad As New List(Of Byte)
    Private ReadOnly _anticipoFrase As New Queue(Of Byte())
    Private _audioFrase As MemoryStream
    Private _inParlato As Boolean = False
    Private _finestreVoce As Integer = 0
    Private _msSilenzio As Integer = 0
    Private _msParlato As Integer = 0

    Private _fase As FaseAssistente = FaseAssistente.Ascolto
    Private _numeroSessione As Integer = 0
    Private _confermeAttivazione As Integer = 0
    Private _attivazioneBloccataFino As DateTime = DateTime.MinValue
    Private _frasiNellaSessione As Integer = 0
    Private _risposteNellaSessione As Integer = 0
    Private _microfonoSospeso As Boolean = False
    Private _azzeramentoRichiesto As Boolean = False
    Private _inAttesaRisposta As Boolean = False
    Private _ultimaVoce As DateTime = DateTime.MinValue
    Private _fineSintesi As DateTime = DateTime.MinValue
    Private _sintesiAssistenteAttesa As Boolean = False
    Private _sintesiAssistenteInCorso As Boolean = False
    Private _inizioAttesaSintesi As DateTime = DateTime.MinValue
    Private _sintesiInCorso As Boolean = False
    Private _dettaturaAttiva As Boolean = False
    Private _lettura As sintesivocale

    Private _conversazione As CLSAi
    Private _elaborazioneEsternaInCorso As Boolean = False
    Private _idRichiestaCorrente As Integer = 0
    Private _inizioElaborazioneEsterna As DateTime = DateTime.MinValue
    Private _microfonoAvviato As Boolean = False
    Private _ultimiDatiMicrofono As DateTime = DateTime.MinValue
    Private _ultimoRiavvioMicrofono As DateTime = DateTime.MinValue
    Private _chiamateMicrofonoSessione As Integer = 0
    Private _anticipoSessione As Boolean = False
    Private _fraseInAttesa As RichiestaVocale = Nothing
    Private ReadOnly _bloccoFraseInAttesa As New Object()
    Private ReadOnly _bloccoDialogoEmail As New Object()
    Private _passoEmail As PassoEmail = PassoEmail.Nessuno
    Private _emailDestinatario As String = ""
    Private _emailOggetto As String = Nothing
    Private ReadOnly _emailCorpo As New StringBuilder()
    Private _ultimoPassoEmail As DateTime = DateTime.MinValue
    Private _inizioSessione As DateTime = DateTime.MinValue
    Private _riavviiMicrofonoSessione As Integer = 0
    Private _segmentoParakeetInAttesa As Byte() = Nothing
    Private _segmentoParakeetInAttesaDal As DateTime = DateTime.MinValue
    Private ReadOnly _bloccoParakeet As New Object()
    Private _ignoraLettureEsterneFino As DateTime = DateTime.MinValue
    Private _inizioUltimaLettura As DateTime = DateTime.MinValue

    Private Const MESSAGGIO_CLOUD_MANCANTE As String = "L'assistente vocale richiede almeno un'intelligenza artificiale in cloud configurata. Impostare una chiave API di Gemini o di Groq da Impostazioni, Intelligenza artificiale, Motore AI predefinito."

    Private Shared _supportoLocaleVerificato As Boolean = False

    Public Shared ReadOnly Property SistemaSupportato As Boolean
        Get
            Try
                Return Environment.Is64BitProcess AndAlso Environment.Is64BitOperatingSystem
            Catch ex As Exception
                log.ScriviLog("AssistenteVocale.SistemaSupportato: " & ex.Message)
                Return False
            End Try
        End Get
    End Property

    Public Shared ReadOnly Property InEsecuzione As Boolean
        Get
            Try
                Return _istanza IsNot Nothing
            Catch ex As Exception
                log.ScriviLog("AssistenteVocale.InEsecuzione: " & ex.Message)
                Return False
            End Try
        End Get
    End Property

    Public Shared Function Attivo() As Boolean
        Try
            Return LeggiValoreConfigurazione(ID_CONFIG_ASSISTENTE_VOCALE) = "1"
        Catch ex As Exception
            log.ScriviLog("AssistenteVocale.Attivo: " & ex.Message)
            Return False
        End Try
    End Function

    Public Shared Sub ImpostaAttivo(valore As Boolean)
        Try
            ScriviValoreConfigurazione(ID_CONFIG_ASSISTENTE_VOCALE, If(valore, "1", "0"))
        Catch ex As Exception
            log.ScriviLog("AssistenteVocale.ImpostaAttivo: " & ex.Message)
        End Try
    End Sub

    Public Shared Function QualitaImpostata() As QualitaVosk
        Try
            If String.Equals(LeggiValoreConfigurazione(ID_CONFIG_QUALITA_VOSK).Trim(), "alta", StringComparison.OrdinalIgnoreCase) Then Return QualitaVosk.Alta
            Return QualitaVosk.Bassa
        Catch ex As Exception
            log.ScriviLog("AssistenteVocale.QualitaImpostata: " & ex.Message)
            Return QualitaVosk.Bassa
        End Try
    End Function

    Private Shared Sub SalvaQualita(qualita As QualitaVosk)
        Try
            ScriviValoreConfigurazione(ID_CONFIG_QUALITA_VOSK, If(qualita = QualitaVosk.Alta, "alta", "bassa"))
        Catch ex As Exception
            log.ScriviLog("AssistenteVocale.SalvaQualita: " & ex.Message)
        End Try
    End Sub

    Public Shared Function ParolaImpostata() As ParolaAttivazione
        Try
            If String.Equals(LeggiValoreConfigurazione(ID_CONFIG_PAROLA_ATTIVAZIONE).Trim(), "minerva", StringComparison.OrdinalIgnoreCase) Then Return ParolaAttivazione.EhiMinerva
            Return ParolaAttivazione.HeyJarvis
        Catch ex As Exception
            log.ScriviLog("AssistenteVocale.ParolaImpostata: " & ex.Message)
            Return ParolaAttivazione.HeyJarvis
        End Try
    End Function

    Private Shared Sub SalvaParola(parola As ParolaAttivazione)
        Try
            ScriviValoreConfigurazione(ID_CONFIG_PAROLA_ATTIVAZIONE, If(parola = ParolaAttivazione.EhiMinerva, "minerva", "jarvis"))
        Catch ex As Exception
            log.ScriviLog("AssistenteVocale.SalvaParola: " & ex.Message)
        End Try
    End Sub

    Public Shared Function ElaborazioneImpostata() As ElaborazioneComandi
        Try
            Dim valore As String = LeggiValoreConfigurazione(ID_CONFIG_ELABORAZIONE_COMANDI).Trim()
            If String.Equals(valore, "locale", StringComparison.OrdinalIgnoreCase) Then Return ElaborazioneComandi.Locale
            If String.Equals(valore, "groq", StringComparison.OrdinalIgnoreCase) Then Return ElaborazioneComandi.Groq
            If String.Equals(valore, "gemini", StringComparison.OrdinalIgnoreCase) Then Return ElaborazioneComandi.Gemini
            Return If(CLSAi.LeggiProviderPreferito() = CLSAi.ProviderAi.Groq, ElaborazioneComandi.Groq, ElaborazioneComandi.Gemini)
        Catch ex As Exception
            log.ScriviLog("AssistenteVocale.ElaborazioneImpostata: " & ex.Message)
            Return ElaborazioneComandi.Gemini
        End Try
    End Function

    Public Shared Function DescrizioneElaborazione(elaborazione As ElaborazioneComandi) As String
        Try
            Select Case elaborazione
                Case ElaborazioneComandi.Groq
                    Return "Groq"
                Case ElaborazioneComandi.Locale
                    Return "sul computer"
                Case Else
                    Return "Gemini"
            End Select
        Catch ex As Exception
            log.ScriviLog("AssistenteVocale.DescrizioneElaborazione: " & ex.Message)
            Return ""
        End Try
    End Function

    Public Shared Sub SalvaElaborazione(elaborazione As ElaborazioneComandi)
        Try
            Dim valore As String = "gemini"
            If elaborazione = ElaborazioneComandi.Groq Then valore = "groq"
            If elaborazione = ElaborazioneComandi.Locale Then valore = "locale"
            ScriviValoreConfigurazione(ID_CONFIG_ELABORAZIONE_COMANDI, valore)
        Catch ex As Exception
            log.ScriviLog("AssistenteVocale.SalvaElaborazione: " & ex.Message)
        End Try
    End Sub

    Public Shared Function ElaborazioneDisponibile(ByRef messaggio As String) As Boolean
        messaggio = ""
        Try
            If ElaborazioneImpostata() = ElaborazioneComandi.Locale Then
                If ComandiLocaliSuDisco() OrElse WGTrascrittoreLocale.ModelloUtilizzabile() Then Return True
                messaggio = "L'elaborazione dei comandi sul computer richiede un modello vocale locale. Scaricarlo da Impostazioni, Intelligenza artificiale, Assistente vocale, Motore vocale dell'assistente."
                Return False
            End If
            If CloudConfigurato() Then Return True
            messaggio = MESSAGGIO_CLOUD_MANCANTE
            Return False
        Catch ex As Exception
            log.ScriviLog("AssistenteVocale.ElaborazioneDisponibile: " & ex.Message & Environment.NewLine & ex.StackTrace)
            messaggio = MESSAGGIO_CLOUD_MANCANTE
            Return False
        End Try
    End Function

    Public Shared Sub ElaborazioneImpostataDaImpostazioni(elaborazione As ElaborazioneComandi)
        Try
            SalvaElaborazione(elaborazione)
            If Attivo() AndAlso _istanza Is Nothing Then
                AvviaVerifica(True)
                Exit Sub
            End If
            MostraMessaggio("Elaborazione dei comandi impostata: " & DescrizioneElaborazione(elaborazione) & ".")
        Catch ex As Exception
            log.ScriviLog("AssistenteVocale.ElaborazioneImpostataDaImpostazioni: " & ex.Message & Environment.NewLine & ex.StackTrace)
        End Try
    End Sub

    Public Shared Sub ImpostaElaborazioneLocaleDaImpostazioni()
        Try
            If MotoreComandiImpostato() = MotoreComandiLocale.Parakeet Then
                Dim motivoParakeet As String = ""
                If Not WGTrascrittoreLocale.SistemaSupportato(motivoParakeet) Then
                    MostraMessaggio("Il riconoscimento locale Parakeet non è disponibile su questo computer: " & motivoParakeet & ". Scegliere Vosk in Motore vocale dell'assistente.")
                    Exit Sub
                End If
                If Not WGTrascrittoreLocale.ModelloPresente() Then
                    ScaricamentoModelloLocale.Avvia(New Action(Of Boolean)(AddressOf ParakeetScaricatoPerElaborazione))
                    Exit Sub
                End If
                ElaborazioneImpostataDaImpostazioni(ElaborazioneComandi.Locale)
                Exit Sub
            End If

            Dim qualita As QualitaVosk = QualitaImpostata()
            Dim motivo As String = ""
            If Not SistemaLocaleSupportato(qualita, motivo) AndAlso Not SistemaLocaleSupportato(QualitaVosk.Bassa, motivo) Then
                MostraMessaggio("Il riconoscimento vocale locale non è disponibile su questo computer: " & motivo & ".")
                Exit Sub
            End If

            If ComandiLocaliSuDisco() Then
                ElaborazioneImpostataDaImpostazioni(ElaborazioneComandi.Locale)
                Exit Sub
            End If

            _elaborazioneDaRipristinare = ElaborazioneImpostata()
            _ripristinoElaborazioneInSospeso = True
            _ripristinoQualitaInSospeso = False
            _ripristinoParolaInSospeso = False
            SalvaElaborazione(ElaborazioneComandi.Locale)
            ChiediScaricamento("L'elaborazione dei comandi sul computer richiede il modello vocale italiano in " & DescrizioneQualita(qualita) & ", non ancora scaricato. Scaricarlo adesso, " & DimensioneScaricamento(qualita) & "?")
        Catch ex As Exception
            log.ScriviLog("AssistenteVocale.ImpostaElaborazioneLocaleDaImpostazioni: " & ex.Message & Environment.NewLine & ex.StackTrace)
        End Try
    End Sub

    Private Shared Sub ParakeetScaricatoPerElaborazione(riuscito As Boolean)
        Try
            If riuscito Then ElaborazioneImpostataDaImpostazioni(ElaborazioneComandi.Locale)
        Catch ex As Exception
            log.ScriviLog("AssistenteVocale.ParakeetScaricatoPerElaborazione: " & ex.Message)
        End Try
    End Sub

    Public Shared Function MotoreMinervaImpostato() As MotoreMinerva
        Try
            If String.Equals(LeggiValoreConfigurazione(ID_CONFIG_MOTORE_MINERVA).Trim(), "parakeet", StringComparison.OrdinalIgnoreCase) Then Return MotoreMinerva.Parakeet
            Return MotoreMinerva.Vosk
        Catch ex As Exception
            log.ScriviLog("AssistenteVocale.MotoreMinervaImpostato: " & ex.Message)
            Return MotoreMinerva.Vosk
        End Try
    End Function

    Public Shared Function DescrizioneMotoreMinerva(motore As MotoreMinerva) As String
        Try
            Return If(motore = MotoreMinerva.Parakeet, "Parakeet", "Vosk")
        Catch ex As Exception
            log.ScriviLog("AssistenteVocale.DescrizioneMotoreMinerva: " & ex.Message)
            Return ""
        End Try
    End Function

    Private Shared Sub SalvaMotoreMinerva(motore As MotoreMinerva)
        Try
            ScriviValoreConfigurazione(ID_CONFIG_MOTORE_MINERVA, If(motore = MotoreMinerva.Parakeet, "parakeet", "vosk"))
        Catch ex As Exception
            log.ScriviLog("AssistenteVocale.SalvaMotoreMinerva: " & ex.Message)
        End Try
    End Sub

    Public Shared Sub ImpostaMotoreMinervaDaImpostazioni(motore As MotoreMinerva)
        Try
            If motore = MotoreMinerva.Parakeet Then
                Dim motivo As String = ""
                If Not WGTrascrittoreLocale.SistemaSupportato(motivo) Then
                    MostraMessaggio("Il riconoscimento locale Parakeet non è disponibile su questo computer: " & motivo & ".")
                    Exit Sub
                End If
                If Not WGTrascrittoreLocale.ModelloPresente() Then
                    ScaricamentoModelloLocale.Avvia(New Action(Of Boolean)(AddressOf ParakeetScaricatoPerMinerva))
                    Exit Sub
                End If
            End If

            Dim precedente As Boolean = ParakeetScelto()
            SalvaMotoreMinerva(motore)
            SalvaMotoreComandi(If(motore = MotoreMinerva.Parakeet, MotoreComandiLocale.Parakeet, MotoreComandiLocale.Vosk))
            RiavviaDopoCambioMotoreMinerva(precedente <> (motore = MotoreMinerva.Parakeet), motore)
        Catch ex As Exception
            log.ScriviLog("AssistenteVocale.ImpostaMotoreMinervaDaImpostazioni: " & ex.Message & Environment.NewLine & ex.StackTrace)
        End Try
    End Sub

    Private Shared Sub ParakeetScaricatoPerMinerva(riuscito As Boolean)
        Try
            If Not riuscito Then Exit Sub
            Dim precedente As Boolean = ParakeetScelto()
            SalvaMotoreMinerva(MotoreMinerva.Parakeet)
            SalvaMotoreComandi(MotoreComandiLocale.Parakeet)
            RiavviaDopoCambioMotoreMinerva(Not precedente, MotoreMinerva.Parakeet)
        Catch ex As Exception
            log.ScriviLog("AssistenteVocale.ParakeetScaricatoPerMinerva: " & ex.Message & Environment.NewLine & ex.StackTrace)
        End Try
    End Sub

    Private Shared Sub RiavviaDopoCambioMotoreMinerva(cambiato As Boolean, motore As MotoreMinerva)
        Try
            If cambiato AndAlso _istanza IsNot Nothing Then
                Ferma()
                If Attivo() Then
                    AvviaVerifica(True)
                    Exit Sub
                End If
            End If
            MostraMessaggio("Motore vocale dell'assistente impostato: " & DescrizioneMotoreMinerva(motore) & ", per Ehi Minerva e per i comandi senza internet.")
        Catch ex As Exception
            log.ScriviLog("AssistenteVocale.RiavviaDopoCambioMotoreMinerva: " & ex.Message & Environment.NewLine & ex.StackTrace)
        End Try
    End Sub

    Public Shared Function MotoreComandiImpostato() As MotoreComandiLocale
        Try
            If String.Equals(LeggiValoreConfigurazione(ID_CONFIG_MOTORE_COMANDI).Trim(), "parakeet", StringComparison.OrdinalIgnoreCase) Then Return MotoreComandiLocale.Parakeet
            Return MotoreComandiLocale.Vosk
        Catch ex As Exception
            log.ScriviLog("AssistenteVocale.MotoreComandiImpostato: " & ex.Message)
            Return MotoreComandiLocale.Vosk
        End Try
    End Function

    Public Shared Function DescrizioneMotoreComandi(motore As MotoreComandiLocale) As String
        Try
            Return If(motore = MotoreComandiLocale.Parakeet, "Parakeet", "Vosk")
        Catch ex As Exception
            log.ScriviLog("AssistenteVocale.DescrizioneMotoreComandi: " & ex.Message)
            Return ""
        End Try
    End Function

    Private Shared Sub SalvaMotoreComandi(motore As MotoreComandiLocale)
        Try
            ScriviValoreConfigurazione(ID_CONFIG_MOTORE_COMANDI, If(motore = MotoreComandiLocale.Parakeet, "parakeet", "vosk"))
        Catch ex As Exception
            log.ScriviLog("AssistenteVocale.SalvaMotoreComandi: " & ex.Message)
        End Try
    End Sub

    Public Shared Sub ImpostaMotoreComandiDaImpostazioni(motore As MotoreComandiLocale)
        Try
            If motore = MotoreComandiLocale.Vosk Then
                Dim precedenteVosk As Boolean = ParakeetScelto()
                SalvaMotoreComandi(MotoreComandiLocale.Vosk)
                SalvaMotoreMinerva(MotoreMinerva.Vosk)
                RiavviaDopoCambioMotoreMinerva(precedenteVosk, MotoreMinerva.Vosk)
                Exit Sub
            End If

            Dim motivo As String = ""
            If Not WGTrascrittoreLocale.SistemaSupportato(motivo) Then
                MostraMessaggio("Il riconoscimento locale Parakeet non è disponibile su questo computer: " & motivo & ".")
                Exit Sub
            End If

            If WGTrascrittoreLocale.ModelloPresente() Then
                Dim precedenteParakeet As Boolean = ParakeetScelto()
                SalvaMotoreComandi(MotoreComandiLocale.Parakeet)
                SalvaMotoreMinerva(MotoreMinerva.Parakeet)
                RiavviaDopoCambioMotoreMinerva(Not precedenteParakeet, MotoreMinerva.Parakeet)
                Exit Sub
            End If

            ScaricamentoModelloLocale.Avvia(New Action(Of Boolean)(AddressOf ParakeetScaricatoPerComandi))
        Catch ex As Exception
            log.ScriviLog("AssistenteVocale.ImpostaMotoreComandiDaImpostazioni: " & ex.Message & Environment.NewLine & ex.StackTrace)
        End Try
    End Sub

    Private Shared Sub ParakeetScaricatoPerComandi(riuscito As Boolean)
        Try
            If Not riuscito Then Exit Sub
            Dim precedente As Boolean = ParakeetScelto()
            SalvaMotoreComandi(MotoreComandiLocale.Parakeet)
            SalvaMotoreMinerva(MotoreMinerva.Parakeet)
            RiavviaDopoCambioMotoreMinerva(Not precedente, MotoreMinerva.Parakeet)
        Catch ex As Exception
            log.ScriviLog("AssistenteVocale.ParakeetScaricatoPerComandi: " & ex.Message)
        End Try
    End Sub

    Private Shared Function ParakeetScelto() As Boolean
        Try
            Return MotoreComandiImpostato() = MotoreComandiLocale.Parakeet OrElse MotoreMinervaImpostato() = MotoreMinerva.Parakeet
        Catch ex As Exception
            log.ScriviLog("AssistenteVocale.ParakeetScelto: " & ex.Message)
            Return False
        End Try
    End Function

    Public Shared Function MotoreLocaleScelto() As MotoreComandiLocale
        Try
            Return If(ParakeetScelto(), MotoreComandiLocale.Parakeet, MotoreComandiLocale.Vosk)
        Catch ex As Exception
            log.ScriviLog("AssistenteVocale.MotoreLocaleScelto: " & ex.Message)
            Return MotoreComandiLocale.Vosk
        End Try
    End Function

    Private Shared Function ParakeetPerComandi() As Boolean
        Try
            Return ParakeetScelto() AndAlso WGTrascrittoreLocale.ModelloUtilizzabile()
        Catch ex As Exception
            log.ScriviLog("AssistenteVocale.ParakeetPerComandi: " & ex.Message)
            Return False
        End Try
    End Function

    Public Shared Function SensibilitaImpostata() As SensibilitaParola
        Try
            Dim valore As String = LeggiValoreConfigurazione(ID_CONFIG_SENSIBILITA_JARVIS).Trim()
            If String.Equals(valore, "alta", StringComparison.OrdinalIgnoreCase) Then Return SensibilitaParola.Alta
            If String.Equals(valore, "bassa", StringComparison.OrdinalIgnoreCase) Then Return SensibilitaParola.Bassa
            Return SensibilitaParola.Media
        Catch ex As Exception
            log.ScriviLog("AssistenteVocale.SensibilitaImpostata: " & ex.Message)
            Return SensibilitaParola.Media
        End Try
    End Function

    Public Shared Function DescrizioneSensibilita(sensibilita As SensibilitaParola) As String
        Try
            Select Case sensibilita
                Case SensibilitaParola.Alta
                    Return "alta"
                Case SensibilitaParola.Bassa
                    Return "bassa"
                Case Else
                    Return "media"
            End Select
        Catch ex As Exception
            log.ScriviLog("AssistenteVocale.DescrizioneSensibilita: " & ex.Message)
            Return ""
        End Try
    End Function

    Private Shared Function SogliaSensibilita(sensibilita As SensibilitaParola) As Single
        Try
            Select Case sensibilita
                Case SensibilitaParola.Alta
                    Return SOGLIA_JARVIS_ALTA
                Case SensibilitaParola.Bassa
                    Return SOGLIA_JARVIS_BASSA
                Case Else
                    Return SOGLIA_JARVIS_MEDIA
            End Select
        Catch ex As Exception
            log.ScriviLog("AssistenteVocale.SogliaSensibilita: " & ex.Message)
            Return SOGLIA_JARVIS_MEDIA
        End Try
    End Function

    Private Shared Function ConfermeSensibilita(sensibilita As SensibilitaParola) As Integer
        Try
            Return If(sensibilita = SensibilitaParola.Alta, CONFERME_JARVIS_ALTA, CONFERME_JARVIS_NORMALI)
        Catch ex As Exception
            log.ScriviLog("AssistenteVocale.ConfermeSensibilita: " & ex.Message)
            Return CONFERME_JARVIS_NORMALI
        End Try
    End Function

    Public Shared Sub ImpostaSensibilitaDaImpostazioni(sensibilita As SensibilitaParola)
        Try
            ScriviValoreConfigurazione(ID_CONFIG_SENSIBILITA_JARVIS, DescrizioneSensibilita(sensibilita))
            Dim istanza As AssistenteVocale = _istanza
            If istanza IsNot Nothing Then
                istanza._sogliaJarvis = SogliaSensibilita(sensibilita)
                istanza._confermeJarvis = ConfermeSensibilita(sensibilita)
            End If
            MostraMessaggio("Sensibilità di Hey Jarvis impostata su " & DescrizioneSensibilita(sensibilita) & ".")
        Catch ex As Exception
            log.ScriviLog("AssistenteVocale.ImpostaSensibilitaDaImpostazioni: " & ex.Message & Environment.NewLine & ex.StackTrace)
        End Try
    End Sub

    Public Shared Function DescrizioneParola(parola As ParolaAttivazione) As String
        Try
            Return If(parola = ParolaAttivazione.EhiMinerva, "Ehi Minerva", "Hey Jarvis")
        Catch ex As Exception
            log.ScriviLog("AssistenteVocale.DescrizioneParola: " & ex.Message)
            Return ""
        End Try
    End Function

    Private Shared Function IstruzioniUso() As String
        Try
            Return "Pronunciare " & DescrizioneParola(ParolaImpostata()) & " seguito dal comando o dalla domanda, oppure premere Ctrl più Maiuscolo più Spazio e parlare."
        Catch ex As Exception
            log.ScriviLog("AssistenteVocale.IstruzioniUso: " & ex.Message)
            Return ""
        End Try
    End Function

    Public Shared Sub ImpostaParolaDaImpostazioni(parola As ParolaAttivazione)
        Try
            Dim precedente As ParolaAttivazione = ParolaImpostata()

            If parola = ParolaAttivazione.EhiMinerva AndAlso MotoreMinervaImpostato() = MotoreMinerva.Vosk Then
                Dim qualita As QualitaVosk = QualitaImpostata()
                Dim motivo As String = ""
                If Not SistemaLocaleSupportato(qualita, motivo) AndAlso Not SistemaLocaleSupportato(QualitaVosk.Bassa, motivo) Then
                    MostraMessaggio("La parola di attivazione Ehi Minerva richiede il riconoscimento vocale locale, che non è disponibile su questo computer: " & motivo & ".")
                    Exit Sub
                End If
                SalvaParola(parola)
                If Not ComandiLocaliSuDisco() Then
                    _parolaDaRipristinare = precedente
                    _ripristinoParolaInSospeso = (precedente <> parola)
                    _ripristinoQualitaInSospeso = False
                    ChiediScaricamento("La parola di attivazione Ehi Minerva richiede il modello vocale italiano in " & DescrizioneQualita(qualita) & ", non ancora scaricato. Scaricarlo adesso, " & DimensioneScaricamento(qualita) & "?")
                    Exit Sub
                End If
            Else
                SalvaParola(parola)
            End If

            If parola <> precedente AndAlso _istanza IsNot Nothing Then
                Ferma()
                If Attivo() Then
                    AvviaVerifica(True)
                    Exit Sub
                End If
            End If

            MostraMessaggio("Parola di attivazione impostata su " & DescrizioneParola(parola) & ".")
        Catch ex As Exception
            log.ScriviLog("AssistenteVocale.ImpostaParolaDaImpostazioni: " & ex.Message & Environment.NewLine & ex.StackTrace)
        End Try
    End Sub

    Public Shared Function DescrizioneQualita(qualita As QualitaVosk) As String
        Try
            Return If(qualita = QualitaVosk.Alta, "alta qualità", "bassa qualità")
        Catch ex As Exception
            log.ScriviLog("AssistenteVocale.DescrizioneQualita: " & ex.Message)
            Return ""
        End Try
    End Function

    Private Shared Function DimensioneScaricamento(qualita As QualitaVosk) As String
        Try
            If qualita = QualitaVosk.Bassa Then Return "circa 60 megabyte"
            Return "circa 1,2 gigabyte"
        Catch ex As Exception
            log.ScriviLog("AssistenteVocale.DimensioneScaricamento: " & ex.Message)
            Return ""
        End Try
    End Function

    Public Shared Function CloudConfigurato() As Boolean
        Try
            Return CLSAi.AlmenoUnProviderConfigurato() OrElse WGDettatura.AlmenoUnProviderCloudConfigurato()
        Catch ex As Exception
            log.ScriviLog("AssistenteVocale.CloudConfigurato: " & ex.Message)
            Return False
        End Try
    End Function

    Public Shared Function CartellaVosk() As String
        Try
            Return Path.Combine(WGDb.WGPercorsiApplicazione.CartellaModelli(), NOME_CARTELLA_VOSK)
        Catch ex As Exception
            log.ScriviLog("AssistenteVocale.CartellaVosk: " & ex.Message)
            Return NOME_CARTELLA_VOSK
        End Try
    End Function

    Private Shared Function CartellaModello(qualita As QualitaVosk) As String
        Try
            Return Path.Combine(CartellaVosk(), If(qualita = QualitaVosk.Alta, NOME_CARTELLA_MODELLO_ALTA, NOME_CARTELLA_MODELLO))
        Catch ex As Exception
            log.ScriviLog("AssistenteVocale.CartellaModello: " & ex.Message)
            Return NOME_CARTELLA_MODELLO
        End Try
    End Function

    Private Shared Function CartellaLibrerie() As String
        Try
            Return Path.Combine(CartellaVosk(), NOME_CARTELLA_LIBRERIE)
        Catch ex As Exception
            log.ScriviLog("AssistenteVocale.CartellaLibrerie: " & ex.Message)
            Return NOME_CARTELLA_LIBRERIE
        End Try
    End Function

    Private Shared Function PercorsoFileAssistente(nomeFile As String) As String
        Try
            Return Path.Combine(WGDb.WGPercorsiApplicazione.CartellaModelli(), NOME_CARTELLA_ASSISTENTE, nomeFile)
        Catch ex As Exception
            log.ScriviLog("AssistenteVocale.PercorsoFileAssistente: " & ex.Message)
            Return nomeFile
        End Try
    End Function

    Private Shared Function PercorsoModelloVad() As String
        Return PercorsoFileAssistente(NOME_FILE_VAD)
    End Function

    Private Shared Function LibreriePresenti() As Boolean
        Try
            Return File.Exists(Path.Combine(CartellaLibrerie(), "libvosk.dll"))
        Catch ex As Exception
            log.ScriviLog("AssistenteVocale.LibreriePresenti: " & ex.Message)
            Return False
        End Try
    End Function

    Public Shared Function ModelloPresente() As Boolean
        Try
            Return ModelloPresente(QualitaImpostata())
        Catch ex As Exception
            log.ScriviLog("AssistenteVocale.ModelloPresente: " & ex.Message)
            Return False
        End Try
    End Function

    Public Shared Function ModelloPresente(qualita As QualitaVosk) As Boolean
        Try
            If Not LibreriePresenti() Then Return False
            Return Not String.IsNullOrEmpty(PercorsoModelloEffettivo(qualita))
        Catch ex As Exception
            log.ScriviLog("AssistenteVocale.ModelloPresente: " & ex.Message)
            Return False
        End Try
    End Function

    Private Shared Function PercorsoModelloEffettivo(qualita As QualitaVosk) As String
        Try
            Dim radice As String = CartellaModello(qualita)
            If Not Directory.Exists(radice) Then Return ""
            If Directory.Exists(Path.Combine(radice, "am")) AndAlso Directory.Exists(Path.Combine(radice, "conf")) Then Return radice
            For Each sottocartella As String In Directory.GetDirectories(radice)
                If Directory.Exists(Path.Combine(sottocartella, "am")) AndAlso Directory.Exists(Path.Combine(sottocartella, "conf")) Then Return sottocartella
            Next
            Return ""
        Catch ex As Exception
            log.ScriviLog("AssistenteVocale.PercorsoModelloEffettivo: " & ex.Message)
            Return ""
        End Try
    End Function

    Private Shared Function LeggiStatoMemoria(ByRef stato As STATO_MEMORIA) As Boolean
        Try
            stato = New STATO_MEMORIA()
            stato.dwLength = CUInt(Marshal.SizeOf(GetType(STATO_MEMORIA)))
            Return GlobalMemoryStatusEx(stato)
        Catch ex As Exception
            log.ScriviLog("AssistenteVocale.LeggiStatoMemoria: " & ex.Message)
            Return False
        End Try
    End Function

    Private Shared Function MemoriaLiberaSufficiente(qualita As QualitaVosk) As Boolean
        Try
            If qualita = QualitaVosk.Bassa Then Return True
            Dim stato As New STATO_MEMORIA()
            If Not LeggiStatoMemoria(stato) Then Return True
            Return CLng(Math.Min(stato.ullAvailPhys, CULng(Long.MaxValue))) >= MEMORIA_LIBERA_ALTA
        Catch ex As Exception
            log.ScriviLog("AssistenteVocale.MemoriaLiberaSufficiente: " & ex.Message)
            Return True
        End Try
    End Function

    Public Shared Function SistemaLocaleSupportato(qualita As QualitaVosk, ByRef motivo As String) As Boolean
        motivo = ""
        Try
            If Not SistemaSupportato Then
                motivo = "il riconoscimento vocale locale richiede Windows a 64 bit"
                Return False
            End If

            If Not IsProcessorFeaturePresent(PF_SSE3_INSTRUCTIONS_AVAILABLE) Then
                motivo = "il processore non ha le istruzioni necessarie al riconoscimento vocale locale"
                Return False
            End If

            Dim processoriMinimi As Integer = If(qualita = QualitaVosk.Alta, PROCESSORI_MINIMI_ALTA, PROCESSORI_MINIMI_BASSA)
            If Environment.ProcessorCount < processoriMinimi Then
                motivo = "il processore non è abbastanza potente per il riconoscimento vocale locale in " & DescrizioneQualita(qualita)
                Return False
            End If

            Dim stato As New STATO_MEMORIA()
            If LeggiStatoMemoria(stato) Then
                Dim memoriaMinima As Long = If(qualita = QualitaVosk.Alta, MEMORIA_MINIMA_ALTA, MEMORIA_MINIMA_BASSA)
                Dim memoriaTotale As Long = CLng(Math.Min(stato.ullTotalPhys, CULng(Long.MaxValue)))
                If memoriaTotale < memoriaMinima - 256L * MEGABYTE Then
                    motivo = "la memoria del computer non basta per il riconoscimento vocale locale in " & DescrizioneQualita(qualita)
                    Return False
                End If
            End If

            Return True
        Catch ex As Exception
            log.ScriviLog("AssistenteVocale.SistemaLocaleSupportato: " & ex.Message & Environment.NewLine & ex.StackTrace)
            motivo = "non è stato possibile verificare le caratteristiche del computer"
            Return False
        End Try
    End Function

    Private Shared Sub AssicuraMarshalCondiviso()
        Try
            If _marshalScaricamento IsNot Nothing AndAlso Not _marshalScaricamento.IsDisposed Then Exit Sub
            _marshalScaricamento = New System.Windows.Forms.Control()
            Dim riferimento As IntPtr = _marshalScaricamento.Handle
        Catch ex As Exception
            log.ScriviLog("AssistenteVocale.AssicuraMarshalCondiviso: " & ex.Message)
        End Try
    End Sub

    Private Shared Function ItalianoWindowsDisponibile() As Boolean
        Try
            Dim risposta As String = WGServiceInterop.InviaComando("SPEECH_CHECK")
            Return String.Equals(If(risposta, "").Trim(), "OK|1", StringComparison.OrdinalIgnoreCase)
        Catch ex As Exception
            log.ScriviLog("AssistenteVocale.ItalianoWindowsDisponibile: " & ex.Message)
            Return False
        End Try
    End Function

    Public Shared Sub AvviaSeAttivo()
        Try
            If Not SistemaSupportato Then Exit Sub
            If Not Attivo() Then Exit Sub
            If _istanza IsNot Nothing Then Exit Sub
            AvviaVerifica(False)
        Catch ex As Exception
            log.ScriviLog("AssistenteVocale.AvviaSeAttivo: " & ex.Message & Environment.NewLine & ex.StackTrace)
        End Try
    End Sub

    Public Shared Sub AvviaSeAttivoEAttendi(completamento As Action)
        Try
            If _completamentoAvvio IsNot Nothing Then
                Dim precedente As Action = _completamentoAvvio
                _completamentoAvvio = Nothing
                Try
                    If precedente IsNot Nothing Then precedente()
                Catch exPrecedente As Exception
                    log.ScriviLog("AssistenteVocale.AvviaSeAttivoEAttendi (completamento precedente): " & exPrecedente.Message & Environment.NewLine & exPrecedente.StackTrace)
                End Try
            End If

            _completamentoAvvio = completamento

            If Not SistemaSupportato OrElse Not Attivo() OrElse _istanza IsNot Nothing Then
                CompletaAttesaAvvio()
                Exit Sub
            End If

            AssicuraMarshalCondiviso()

            Try
                _attesaAvvio = New WGProcessoInCorso()
                AddHandler _attesaAvvio.cancella, AddressOf AttesaAvvioAnnullata
                _attesaAvvio.Avvia("Caricamento dell'assistente vocale in corso, attendere", True)
            Catch exAttesa As Exception
                log.ScriviLog("AssistenteVocale.AvviaSeAttivoEAttendi (finestra di attesa): " & exAttesa.Message & Environment.NewLine & exAttesa.StackTrace)
                _attesaAvvio = Nothing
            End Try

            Try
                _timerAttesaAvvio = New System.Windows.Forms.Timer()
                _timerAttesaAvvio.Interval = SECONDI_MASSIMI_ATTESA_AVVIO * 1000
                AddHandler _timerAttesaAvvio.Tick, AddressOf TimerAttesaAvvioScaduto
                _timerAttesaAvvio.Start()
            Catch exTimer As Exception
                log.ScriviLog("AssistenteVocale.AvviaSeAttivoEAttendi (timer): " & exTimer.Message & Environment.NewLine & exTimer.StackTrace)
            End Try

            AvviaVerifica(False)
        Catch ex As Exception
            log.ScriviLog("AssistenteVocale.AvviaSeAttivoEAttendi: " & ex.Message & Environment.NewLine & ex.StackTrace)
            If _completamentoAvvio Is Nothing AndAlso completamento IsNot Nothing Then _completamentoAvvio = completamento
            CompletaAttesaAvvio()
        End Try
    End Sub

    Private Shared Sub CompletaAttesaAvvio()
        Dim completamento As Action = Nothing
        Try
            If _timerAttesaAvvio IsNot Nothing Then
                Dim timerAttesa As System.Windows.Forms.Timer = _timerAttesaAvvio
                _timerAttesaAvvio = Nothing
                Try
                    timerAttesa.Stop()
                    RemoveHandler timerAttesa.Tick, AddressOf TimerAttesaAvvioScaduto
                    timerAttesa.Dispose()
                Catch exTimer As Exception
                    log.ScriviLog("AssistenteVocale.CompletaAttesaAvvio (timer): " & exTimer.Message)
                End Try
            End If

            If _attesaAvvio IsNot Nothing Then
                Dim attesa As WGProcessoInCorso = _attesaAvvio
                _attesaAvvio = Nothing
                Try
                    RemoveHandler attesa.cancella, AddressOf AttesaAvvioAnnullata
                    attesa.Fine()
                Catch exAttesa As Exception
                    log.ScriviLog("AssistenteVocale.CompletaAttesaAvvio (finestra di attesa): " & exAttesa.Message)
                End Try
            End If
        Catch ex As Exception
            log.ScriviLog("AssistenteVocale.CompletaAttesaAvvio: " & ex.Message & Environment.NewLine & ex.StackTrace)
        Finally
            completamento = _completamentoAvvio
            _completamentoAvvio = Nothing
        End Try

        Try
            If completamento IsNot Nothing Then completamento()
        Catch ex As Exception
            log.ScriviLog("AssistenteVocale.CompletaAttesaAvvio (completamento): " & ex.Message & Environment.NewLine & ex.StackTrace)
        End Try
    End Sub

    Private Shared Sub CompletaAttesaAvvioSuInterfaccia(stato As Object)
        Try
            CompletaAttesaAvvio()
        Catch ex As Exception
            log.ScriviLog("AssistenteVocale.CompletaAttesaAvvioSuInterfaccia: " & ex.Message & Environment.NewLine & ex.StackTrace)
        End Try
    End Sub

    Private Shared Sub AttesaAvvioAnnullata(sender As Object, e As EventArgs)
        Try
            If _attesaAvvio IsNot Nothing Then
                Try
                    RemoveHandler _attesaAvvio.cancella, AddressOf AttesaAvvioAnnullata
                Catch exEvento As Exception
                    log.ScriviLog("AssistenteVocale.AttesaAvvioAnnullata (evento): " & exEvento.Message)
                End Try
                _attesaAvvio = Nothing
            End If
            CompletaAttesaAvvio()
        Catch ex As Exception
            log.ScriviLog("AssistenteVocale.AttesaAvvioAnnullata: " & ex.Message & Environment.NewLine & ex.StackTrace)
        End Try
    End Sub

    Private Shared Sub TimerAttesaAvvioScaduto(sender As Object, e As EventArgs)
        Try
            log.ScriviLog("AssistenteVocale.TimerAttesaAvvioScaduto: il caricamento dell'assistente vocale non si è concluso entro " & SECONDI_MASSIMI_ATTESA_AVVIO.ToString(System.Globalization.CultureInfo.InvariantCulture) & " secondi, il menu iniziale si apre senza attendere oltre")
            CompletaAttesaAvvio()
        Catch ex As Exception
            log.ScriviLog("AssistenteVocale.TimerAttesaAvvioScaduto: " & ex.Message & Environment.NewLine & ex.StackTrace)
        End Try
    End Sub

    Private Shared Sub AggiornaAttesaAvvio(testo As String)
        Try
            If _attesaAvvio Is Nothing OrElse String.IsNullOrEmpty(testo) Then Exit Sub
            _attesaAvvio.AggiornaStato(testo)
        Catch ex As Exception
            log.ScriviLog("AssistenteVocale.AggiornaAttesaAvvio: " & ex.Message)
        End Try
    End Sub

    Public Shared Sub AttivaDaImpostazioni()
        Try
            Dim messaggioElaborazione As String = ""
            If Not ElaborazioneDisponibile(messaggioElaborazione) Then
                MostraMessaggio(messaggioElaborazione)
                Exit Sub
            End If
            ImpostaAttivo(True)
            If _istanza IsNot Nothing Then
                MostraMessaggio("L'assistente vocale è già attivo.")
                Exit Sub
            End If
            AvviaVerifica(True)
        Catch ex As Exception
            log.ScriviLog("AssistenteVocale.AttivaDaImpostazioni: " & ex.Message & Environment.NewLine & ex.StackTrace)
        End Try
    End Sub

    Private Shared Sub AvviaVerifica(daImpostazioni As Boolean)
        Try
            AssicuraMarshalCondiviso()
            _verificaDaImpostazioni = daImpostazioni
            If daImpostazioni Then
                If _attesaVerifica IsNot Nothing Then
                    Try
                        _attesaVerifica.Fine()
                    Catch exAttesa As Exception
                        log.ScriviLog("AssistenteVocale.AvviaVerifica (attesa precedente): " & exAttesa.Message)
                    End Try
                End If
                _attesaVerifica = New WGProcessoInCorso()
                _attesaVerifica.Avvia("Verifica del riconoscimento vocale in corso, attendere", False)
            End If
            Task.Run(New Action(AddressOf LavoroVerificaModo))
        Catch ex As Exception
            log.ScriviLog("AssistenteVocale.AvviaVerifica: " & ex.Message & Environment.NewLine & ex.StackTrace)
        End Try
    End Sub

    Private Shared Sub LavoroVerificaModo()
        Try
            _modoVerificato = ModoAttivazione.Nessuno
            _localeVerificato = False
            _supportoLocaleVerificato = False
            _motivoLocaleVerificato = ""
            _qualitaVerificata = QualitaImpostata()
            Dim messaggioElaborazione As String = ""
            _cloudVerificato = ElaborazioneDisponibile(messaggioElaborazione)
            _messaggioElaborazioneVerificata = messaggioElaborazione
            If Not _cloudVerificato Then Exit Sub

            _motivoRipiegoParakeet = ""
            Dim parola As ParolaAttivazione = ParolaImpostata()
            Dim motivo As String = ""
            _supportoLocaleVerificato = SistemaLocaleSupportato(_qualitaVerificata, motivo)
            If Not _supportoLocaleVerificato AndAlso _qualitaVerificata = QualitaVosk.Alta Then
                Dim motivoBassa As String = ""
                If SistemaLocaleSupportato(QualitaVosk.Bassa, motivoBassa) Then
                    _qualitaVerificata = QualitaVosk.Bassa
                    _supportoLocaleVerificato = True
                    motivo = ""
                End If
            End If

            If _supportoLocaleVerificato Then
                If ModelloPresente(_qualitaVerificata) Then
                    _localeVerificato = True
                ElseIf _qualitaVerificata = QualitaVosk.Alta AndAlso ModelloPresente(QualitaVosk.Bassa) Then
                    _qualitaVerificata = QualitaVosk.Bassa
                    _localeVerificato = True
                Else
                    motivo = "il modello vocale locale in " & DescrizioneQualita(_qualitaVerificata) & " non è ancora stato scaricato"
                End If
            End If

            _motivoLocaleVerificato = motivo

            If parola = ParolaAttivazione.HeyJarvis Then
                _modoVerificato = ModoAttivazione.Jarvis
            ElseIf ParakeetScelto() Then
                Dim motivoParakeet As String = ""
                If WGTrascrittoreLocale.SistemaSupportato(motivoParakeet) AndAlso WGTrascrittoreLocale.ModelloUtilizzabile() Then
                    _modoVerificato = ModoAttivazione.Parakeet
                ElseIf _localeVerificato Then
                    _modoVerificato = ModoAttivazione.Vosk
                    _motivoRipiegoParakeet = If(String.IsNullOrEmpty(motivoParakeet), "il modello Parakeet non è scaricato", motivoParakeet)
                End If
            ElseIf _localeVerificato Then
                _modoVerificato = ModoAttivazione.Vosk
            End If
        Catch ex As Exception
            log.ScriviLog("AssistenteVocale.LavoroVerificaModo: " & ex.Message & Environment.NewLine & ex.StackTrace)
            _modoVerificato = ModoAttivazione.Nessuno
        Finally
            PubblicaScaricamento(AddressOf VerificaModoCompletata, Nothing)
        End Try
    End Sub

    Private Shared Sub VerificaModoCompletata(stato As Object)
        Try
            If _attesaVerifica IsNot Nothing Then
                _attesaVerifica.Fine()
                _attesaVerifica = Nothing
            End If

            Dim modo As ModoAttivazione = _modoVerificato
            Dim daImpostazioni As Boolean = _verificaDaImpostazioni
            _verificaDaImpostazioni = False

            If Not _cloudVerificato Then
                CompletaAttesaAvvio()
                MostraMessaggio(If(String.IsNullOrEmpty(_messaggioElaborazioneVerificata), MESSAGGIO_CLOUD_MANCANTE, _messaggioElaborazioneVerificata))
                Exit Sub
            End If

            If modo <> ModoAttivazione.Nessuno Then
                If Avvia(modo, _qualitaVerificata, _localeVerificato) Then
                    AggiornaAttesaAvvio("Caricamento del modello vocale in " & DescrizioneQualita(_qualitaVerificata) & " in corso, attendere")
                    If daImpostazioni Then
                        If modo = ModoAttivazione.Jarvis Then
                            If _localeVerificato Then
                                MostraMessaggio("Assistente vocale attivo, sperimentale.")
                            Else
                                MostraMessaggio("Assistente vocale attivo, sperimentale.")
                            End If
                        ElseIf modo = ModoAttivazione.Parakeet Then
                            MostraMessaggio("Assistente vocale attivo, sperimentale.")
                        ElseIf Not String.IsNullOrEmpty(_motivoRipiegoParakeet) Then
                            MostraMessaggio("Assistente vocale attivo, sperimentale.")
                        Else
                            MostraMessaggio("Assistente vocale attivo, sperimentale.")
                        End If
                    End If
                Else
                    CompletaAttesaAvvio()
                    If daImpostazioni Then MostraMessaggio("Non è stato possibile avviare l'assistente vocale.")
                End If
                Exit Sub
            End If

            CompletaAttesaAvvio()

            If Not _supportoLocaleVerificato Then
                MostraMessaggio("L'assistente vocale non è disponibile: " & _motivoLocaleVerificato & ".")
                Exit Sub
            End If

            If Not daImpostazioni Then
                MostraMessaggio("L'assistente vocale non è disponibile: " & _motivoLocaleVerificato & ". Il modello si scarica da Impostazioni, Intelligenza artificiale, Assistente vocale.")
                Exit Sub
            End If

            _ripristinoQualitaInSospeso = False
            _ripristinoParolaInSospeso = False
            ChiediScaricamento("Il riconoscimento vocale locale Vosk non è disponibile su questo computer. Scaricare il modello vocale italiano in " & DescrizioneQualita(_qualitaVerificata) & ", " & DimensioneScaricamento(_qualitaVerificata) & "?")
        Catch ex As Exception
            log.ScriviLog("AssistenteVocale.VerificaModoCompletata: " & ex.Message & Environment.NewLine & ex.StackTrace)
            CompletaAttesaAvvio()
        End Try
    End Sub

    Private Shared Sub ChiediScaricamento(domanda As String)
        Try
            If _domandaScaricamento IsNot Nothing Then
                RemoveHandler _domandaScaricamento.rispostadomanda, AddressOf RispostaScaricamento
                _domandaScaricamento = Nothing
            End If
            _domandaScaricamento = New WGDomanda()
            AddHandler _domandaScaricamento.rispostadomanda, AddressOf RispostaScaricamento
            _domandaScaricamento.avvia(domanda)
        Catch ex As Exception
            log.ScriviLog("AssistenteVocale.ChiediScaricamento: " & ex.Message & Environment.NewLine & ex.StackTrace)
        End Try
    End Sub

    Private Shared Sub RispostaScaricamento(risposta As DialogResult)
        Try
            If _domandaScaricamento IsNot Nothing Then
                RemoveHandler _domandaScaricamento.rispostadomanda, AddressOf RispostaScaricamento
                _domandaScaricamento = Nothing
            End If

            Dim ripristina As Boolean = _ripristinoQualitaInSospeso
            _ripristinoQualitaInSospeso = False
            Dim ripristinaParola As Boolean = _ripristinoParolaInSospeso
            _ripristinoParolaInSospeso = False
            Dim ripristinaElaborazione As Boolean = _ripristinoElaborazioneInSospeso
            _ripristinoElaborazioneInSospeso = False

            If risposta = DialogResult.Yes Then
                ScaricaModello()
            ElseIf ripristina Then
                SalvaQualita(_qualitaDaRipristinare)
                MostraMessaggio("Il modello vocale non è stato scaricato: il motore di riconoscimento resta in " & DescrizioneQualita(_qualitaDaRipristinare) & ".")
            ElseIf ripristinaParola Then
                SalvaParola(_parolaDaRipristinare)
                MostraMessaggio("Il modello vocale non è stato scaricato: la parola di attivazione resta " & DescrizioneParola(_parolaDaRipristinare) & ".")
            ElseIf ripristinaElaborazione Then
                SalvaElaborazione(_elaborazioneDaRipristinare)
                MostraMessaggio("Il modello vocale non è stato scaricato: l'elaborazione dei comandi resta " & DescrizioneElaborazione(_elaborazioneDaRipristinare) & ".")
            Else
                MostraMessaggio("Il modello vocale non è stato scaricato. Il modello si scarica in qualunque momento da Impostazioni, Intelligenza artificiale, Assistente vocale.")
            End If
        Catch ex As Exception
            log.ScriviLog("AssistenteVocale.RispostaScaricamento: " & ex.Message)
        End Try
    End Sub

    Public Shared Sub ImpostaQualitaDaImpostazioni(qualita As QualitaVosk)
        Try
            Dim motivo As String = ""
            If Not SistemaLocaleSupportato(qualita, motivo) Then
                MostraMessaggio("Il riconoscimento vocale locale in " & DescrizioneQualita(qualita) & " non è disponibile su questo computer: " & motivo & ".")
                Exit Sub
            End If

            Dim precedente As QualitaVosk = QualitaImpostata()
            SalvaQualita(qualita)

            If Not ModelloPresente(qualita) Then
                _qualitaDaRipristinare = precedente
                _ripristinoQualitaInSospeso = (precedente <> qualita)
                _ripristinoParolaInSospeso = False
                ChiediScaricamento("Il motore di riconoscimento è impostato in " & DescrizioneQualita(qualita) & ", ma il suo modello non è ancora stato scaricato. Scaricarlo adesso, " & DimensioneScaricamento(qualita) & "?")
                Exit Sub
            End If

            If qualita <> precedente AndAlso _istanza IsNot Nothing Then
                Ferma()
                If Attivo() Then
                    AvviaVerifica(True)
                    Exit Sub
                End If
            End If

            MostraMessaggio("Motore di riconoscimento impostato in " & DescrizioneQualita(qualita) & ".")
        Catch ex As Exception
            log.ScriviLog("AssistenteVocale.ImpostaQualitaDaImpostazioni: " & ex.Message & Environment.NewLine & ex.StackTrace)
        End Try
    End Sub

    Private Shared Function Avvia(modo As ModoAttivazione, qualita As QualitaVosk, localeRichiesto As Boolean) As Boolean
        Try
            If _istanza IsNot Nothing Then Return True
            If modo = ModoAttivazione.Nessuno Then Return False
            Dim messaggioElaborazione As String = ""
            If Not ElaborazioneDisponibile(messaggioElaborazione) Then Return False
            If modo = ModoAttivazione.Vosk AndAlso (Not localeRichiesto OrElse Not ModelloPresente(qualita)) Then Return False

            Dim nuova As New AssistenteVocale()
            If Not nuova.Inizializza(modo, qualita, localeRichiesto AndAlso ModelloPresente(qualita)) Then
                nuova.Rilascia()
                Return False
            End If
            _istanza = nuova
            Return True
        Catch ex As Exception
            log.ScriviLog("AssistenteVocale.Avvia: " & ex.Message & Environment.NewLine & ex.StackTrace)
            Return False
        End Try
    End Function

    Public Shared Sub Ferma()
        Try
            If _istanza Is Nothing Then Exit Sub
            Dim vecchia As AssistenteVocale = _istanza
            _istanza = Nothing
            vecchia.Rilascia()
        Catch ex As Exception
            log.ScriviLog("AssistenteVocale.Ferma: " & ex.Message & Environment.NewLine & ex.StackTrace)
        End Try
    End Sub

    Public Shared Sub EseguiTesto(testo As String)
        Try
            If _istanza IsNot Nothing Then
                _istanza.ElaboraComando(testo)
            Else
                Dim temporaneo As New AssistenteVocale()
                temporaneo.ElaboraComando(testo)
            End If
        Catch ex As Exception
            log.ScriviLog("AssistenteVocale.EseguiTesto: " & ex.Message & Environment.NewLine & ex.StackTrace)
        End Try
    End Sub

    Private Shared Function CaricaLibreria() As Boolean
        Try
            SyncLock _bloccoLibreria
                If _libreriaCaricata Then Return True
                Dim percorso As String = Path.Combine(CartellaLibrerie(), "libvosk.dll")
                If Not File.Exists(percorso) Then Return False
                Dim modulo As IntPtr = LoadLibraryEx(percorso, IntPtr.Zero, LOAD_WITH_ALTERED_SEARCH_PATH)
                If modulo = IntPtr.Zero Then
                    log.ScriviLog("AssistenteVocale.CaricaLibreria: impossibile caricare libvosk.dll, errore " & Marshal.GetLastWin32Error().ToString())
                    Return False
                End If
                vosk_set_log_level(-1)
                _libreriaCaricata = True
                Return True
            End SyncLock
        Catch ex As Exception
            log.ScriviLog("AssistenteVocale.CaricaLibreria: " & ex.Message & Environment.NewLine & ex.StackTrace)
            Return False
        End Try
    End Function

    ' I file piccoli del modello (rilevamento della voce, riduzione del rumore, parola di attivazione) si scaricano dal centro di controllo, senza
    ' attese a video: qui, su un thread di sottofondo, si aspetta solo la fine dello scaricamento. Restituisce vero se tutti i file ci sono.
    Private Shared Function AssicuraFileScaricati(chiave As String, descrizione As String, voci As List(Of Tuple(Of String, String, Long))) As Boolean
        Try
            Dim mancanti As New List(Of Tuple(Of String, String, Long))()
            For Each voce As Tuple(Of String, String, Long) In voci
                If Not (File.Exists(voce.Item2) AndAlso New FileInfo(voce.Item2).Length >= voce.Item3) Then mancanti.Add(voce)
            Next
            If mancanti.Count = 0 Then Return True

            Dim elementi As New List(Of ElementoModello)()
            For Each voce As Tuple(Of String, String, Long) In mancanti
                elementi.Add(New ElementoModello() With {.Indirizzo = voce.Item1, .NomeFile = Path.GetFileName(voce.Item2), .DimensioneMinima = voce.Item3})
            Next

            Dim installa As Func(Of String, String) =
                Function(temporanea As String) As String
                    For Each voce As Tuple(Of String, String, Long) In mancanti
                        Directory.CreateDirectory(Path.GetDirectoryName(voce.Item2))
                        If File.Exists(voce.Item2) Then File.Delete(voce.Item2)
                        File.Move(Path.Combine(temporanea, Path.GetFileName(voce.Item2)), voce.Item2)
                    Next
                    Return ""
                End Function

            Using fine As New ManualResetEventSlim(False)
                Dim avviato As Boolean = WGCentroDiControllo.GetInstance().AggiungiScaricamentoModelli(chiave, descrizione, elementi, installa, Sub(esito As String) fine.Set())
                If avviato Then fine.Wait(TimeSpan.FromMinutes(60))
                ' se lo stesso scaricamento era già in corso lo si aspetta controllando i file
                Dim scadenza As DateTime = DateTime.Now.AddMinutes(60)
                While Not avviato AndAlso DateTime.Now < scadenza
                    If mancanti.All(Function(voce) File.Exists(voce.Item2) AndAlso New FileInfo(voce.Item2).Length >= voce.Item3) Then Exit While
                    Thread.Sleep(2000)
                End While
            End Using

            Return mancanti.All(Function(voce) File.Exists(voce.Item2) AndAlso New FileInfo(voce.Item2).Length >= voce.Item3)
        Catch ex As Exception
            log.ScriviLog("AssistenteVocale.AssicuraFileScaricati (" & chiave & "): " & ex.Message & Environment.NewLine & ex.StackTrace)
            Return False
        End Try
    End Function

    Private Shared Function AssicuraModelloVad() As Boolean
        Try
            Return AssicuraFileScaricati("assistente-vad", "modello di rilevamento della voce", New List(Of Tuple(Of String, String, Long)) From {
                Tuple.Create(URL_MODELLO_VAD, PercorsoModelloVad(), 100000L)})
        Catch ex As Exception
            log.ScriviLog("AssistenteVocale.AssicuraModelloVad: " & ex.Message & Environment.NewLine & ex.StackTrace)
            Return False
        End Try
    End Function

    Private Shared Function AssicuraRiduttoreRumore() As Boolean
        Try
            Return AssicuraFileScaricati("assistente-riduttore", "modello di riduzione del rumore", New List(Of Tuple(Of String, String, Long)) From {
                Tuple.Create(URL_RIDUTTORE_1, PercorsoFileAssistente(NOME_FILE_RIDUTTORE_1), DIMENSIONE_MINIMA_RIDUTTORE),
                Tuple.Create(URL_RIDUTTORE_2, PercorsoFileAssistente(NOME_FILE_RIDUTTORE_2), DIMENSIONE_MINIMA_RIDUTTORE)})
        Catch ex As Exception
            log.ScriviLog("AssistenteVocale.AssicuraRiduttoreRumore: " & ex.Message & Environment.NewLine & ex.StackTrace)
            Return False
        End Try
    End Function

    Private Shared Function AssicuraModelliJarvis() As Boolean
        Try
            Return AssicuraFileScaricati("assistente-jarvis", "modelli della parola di attivazione Hey Jarvis", New List(Of Tuple(Of String, String, Long)) From {
                Tuple.Create(URL_BASE_OPENWAKEWORD & NOME_FILE_MEL, PercorsoFileAssistente(NOME_FILE_MEL), DIMENSIONE_MINIMA_MODELLO_PAROLA),
                Tuple.Create(URL_BASE_OPENWAKEWORD & NOME_FILE_EMBEDDING, PercorsoFileAssistente(NOME_FILE_EMBEDDING), DIMENSIONE_MINIMA_MODELLO_PAROLA),
                Tuple.Create(URL_BASE_OPENWAKEWORD & NOME_FILE_JARVIS, PercorsoFileAssistente(NOME_FILE_JARVIS), DIMENSIONE_MINIMA_MODELLO_PAROLA)})
        Catch ex As Exception
            log.ScriviLog("AssistenteVocale.AssicuraModelliJarvis: " & ex.Message & Environment.NewLine & ex.StackTrace)
            Return False
        End Try
    End Function

    Private Shared Function CreaRiduttoreRumore() As RiduttoreRumore
        Try
            Dim riduttore As New RiduttoreRumore(PercorsoFileAssistente(NOME_FILE_RIDUTTORE_1), PercorsoFileAssistente(NOME_FILE_RIDUTTORE_2))
            If riduttore.Pronto Then Return riduttore
            riduttore.Dispose()
            Return Nothing
        Catch ex As Exception
            log.ScriviLog("AssistenteVocale.CreaRiduttoreRumore: " & ex.Message & Environment.NewLine & ex.StackTrace)
            Return Nothing
        End Try
    End Function

    Private Function Inizializza(modo As ModoAttivazione, qualita As QualitaVosk, localeRichiesto As Boolean) As Boolean
        Try
            If WGTrascrittoreLocale.VarianteScelta <> WGTrascrittoreLocale.VarianteModello.Standard Then WGTrascrittoreLocale.VarianteScelta = WGTrascrittoreLocale.VarianteModello.Standard
            _modo = modo
            _qualita = qualita
            _localeRichiesto = localeRichiesto
            _sogliaJarvis = SogliaSensibilita(SensibilitaImpostata())
            _confermeJarvis = ConfermeSensibilita(SensibilitaImpostata())
            _localeDisponibile = False
            _marshal = New System.Windows.Forms.Control()
            Dim riferimento As IntPtr = _marshal.Handle

            CreateHandle(New CreateParams())
            _tastoRegistrato = RegisterHotKey(Handle, ID_TASTO_RAPIDO, MOD_CONTROL Or MOD_SHIFT Or MOD_NOREPEAT, CInt(Keys.Space))
            If Not _tastoRegistrato Then
                log.ScriviLog("AssistenteVocale.Inizializza: registrazione del tasto Ctrl+Maiusc+Spazio non riuscita, errore " & Marshal.GetLastWin32Error().ToString())
            End If

            Try
                _lettura = sintesivocale.GetInstance()
                If _lettura IsNot Nothing Then
                    AddHandler _lettura.OnInizioLettura, AddressOf InizioLetturaSintesi
                    AddHandler _lettura.OnStatoParlatoCambiato, AddressOf StatoParlatoSintesi
                    _sintesiInCorso = _lettura.StaParlando
                End If
            Catch exSintesi As Exception
                log.ScriviLog("AssistenteVocale.Inizializza (sintesi): " & exSintesi.Message)
            End Try

            AddHandler WGDettatura.StatoDettaturaCambiato, AddressOf StatoDettatura
            _dettaturaAttiva = WGDettatura.DettaturaInCorso

            _timerControllo = New System.Windows.Forms.Timer()
            _timerControllo.Interval = INTERVALLO_CONTROLLO_MS
            AddHandler _timerControllo.Tick, AddressOf ControlloPeriodico
            _timerControllo.Start()

            Try
                _timerConnessione = New System.Windows.Forms.Timer()
                _timerConnessione.Interval = SECONDI_CONTROLLO_CONNESSIONE_ASSENTE * 1000
                AddHandler _timerConnessione.Tick, AddressOf TimerConnessioneScaduto
                _timerConnessione.Start()
                AddHandler System.Net.NetworkInformation.NetworkChange.NetworkAvailabilityChanged, AddressOf DisponibilitaReteCambiata
                AddHandler System.Net.NetworkInformation.NetworkChange.NetworkAddressChanged, AddressOf IndirizzoReteCambiato
                _eventiReteAgganciati = True
                AvviaVerificaConnessione()
            Catch exConnessione As Exception
                log.ScriviLog("AssistenteVocale.Inizializza (controllo della connessione): " & exConnessione.Message & Environment.NewLine & exConnessione.StackTrace)
            End Try

            Task.Run(New Action(AddressOf CaricaModelliInSottofondo))
            Return True
        Catch ex As Exception
            log.ScriviLog("AssistenteVocale.Inizializza: " & ex.Message & Environment.NewLine & ex.StackTrace)
            Return False
        End Try
    End Function

    Private Shared Function CaricaOnnxRuntime(ByRef motivo As String) As Boolean
        motivo = ""
        Try
            SyncLock _bloccoLibreria
                If _onnxVerificato Then
                    motivo = _motivoOnnx
                    Return _onnxDisponibile
                End If

                Dim esito As Boolean = VerificaECaricaOnnxRuntime(motivo)
                _motivoOnnx = motivo
                _onnxDisponibile = esito
                _onnxVerificato = True
                Return esito
            End SyncLock
        Catch ex As Exception
            log.ScriviLog("AssistenteVocale.CaricaOnnxRuntime: " & ex.Message & Environment.NewLine & ex.StackTrace)
            motivo = "non è stato possibile caricare la libreria onnxruntime.dll"
            Return False
        End Try
    End Function

    Public Shared Sub PrecaricaLibrerieNative()
        Try
            Dim motivo As String = ""
            CaricaOnnxRuntime(motivo)
            WGTrascrittoreLocale.PrecaricaLibrerie()
        Catch ex As Exception
            log.ScriviLog("AssistenteVocale.PrecaricaLibrerieNative: " & ex.Message & Environment.NewLine & ex.StackTrace)
        End Try
    End Sub

    Private Shared Function TrovaLibreriaNativa(nomeFile As String) As String
        Try
            Dim cartelle As String() = {Application.StartupPath, Path.Combine(Application.StartupPath, "runtimes", "win-x64", "native"), Path.Combine(Application.StartupPath, "x64")}
            For Each cartella As String In cartelle
                Dim percorso As String = Path.Combine(cartella, nomeFile)
                If File.Exists(percorso) Then Return percorso
            Next
            Return ""
        Catch ex As Exception
            log.ScriviLog("AssistenteVocale.TrovaLibreriaNativa: " & ex.Message)
            Return ""
        End Try
    End Function

    Private Shared Function VerificaECaricaOnnxRuntime(ByRef motivo As String) As Boolean
        motivo = ""
        Try
            Dim percorso As String = TrovaLibreriaNativa("onnxruntime.dll")
            If String.IsNullOrEmpty(percorso) Then
                motivo = "manca la libreria onnxruntime.dll nella cartella del programma"
                Return False
            End If

            Dim nativa As FileVersionInfo = FileVersionInfo.GetVersionInfo(percorso)
            Dim gestita As Version = GetType(Microsoft.ML.OnnxRuntime.SessionOptions).Assembly.GetName().Version
            If nativa.FileMajorPart <> gestita.Major OrElse nativa.FileMinorPart < gestita.Minor Then
                motivo = "la libreria onnxruntime.dll del programma è in versione " & nativa.FileVersion & ", mentre il programma richiede la " & gestita.Major.ToString(System.Globalization.CultureInfo.InvariantCulture) & "." & gestita.Minor.ToString(System.Globalization.CultureInfo.InvariantCulture) & " o successiva"
                Return False
            End If

            Dim modulo As IntPtr = LoadLibraryEx(percorso, IntPtr.Zero, LOAD_WITH_ALTERED_SEARCH_PATH)
            If modulo = IntPtr.Zero Then
                motivo = "non è stato possibile caricare la libreria onnxruntime.dll, errore " & Marshal.GetLastWin32Error().ToString(System.Globalization.CultureInfo.InvariantCulture)
                Return False
            End If
            Return True
        Catch ex As Exception
            log.ScriviLog("AssistenteVocale.VerificaECaricaOnnxRuntime: " & ex.Message & Environment.NewLine & ex.StackTrace)
            motivo = "non è stato possibile verificare la libreria onnxruntime.dll"
            Return False
        End Try
    End Function

    Private Sub CaricaModelliInSottofondo()
        Try
            Dim motivoOnnx As String = ""
            If Not CaricaOnnxRuntime(motivoOnnx) Then
                PubblicaSuInterfaccia(AddressOf NonDisponibileSuInterfaccia, "L'assistente vocale non è disponibile: " & motivoOnnx & ".")
                Exit Sub
            End If

            If Not AssicuraModelloVad() Then
                PubblicaSuInterfaccia(AddressOf NonDisponibilePerConnessioneSuInterfaccia, "L'assistente vocale non è disponibile: non è stato possibile scaricare il modello di rilevamento della voce.")
                Exit Sub
            End If

            Dim rilevatore As New RilevatoreVoce(PercorsoModelloVad())
            If Not rilevatore.Pronto Then
                rilevatore.Dispose()
                PubblicaSuInterfaccia(AddressOf NonDisponibileSuInterfaccia, "L'assistente vocale non è disponibile: il computer non riesce a caricare il modello di rilevamento della voce.")
                Exit Sub
            End If

            SyncLock _bloccoRiconoscitore
                If _arrestato Then
                    rilevatore.Dispose()
                    Exit Sub
                End If
                _rilevatore = rilevatore
            End SyncLock

            If _modo = ModoAttivazione.Jarvis Then
                If Not AssicuraModelliJarvis() Then
                    PubblicaSuInterfaccia(AddressOf NonDisponibilePerConnessioneSuInterfaccia, "L'assistente vocale non è disponibile: non è stato possibile scaricare il modello della parola di attivazione Hey Jarvis.")
                    Exit Sub
                End If

                Dim rilevatoreParola As New RilevatoreParolaAttivazione(PercorsoFileAssistente(NOME_FILE_MEL), PercorsoFileAssistente(NOME_FILE_EMBEDDING), PercorsoFileAssistente(NOME_FILE_JARVIS))
                If Not rilevatoreParola.Pronto Then
                    rilevatoreParola.Dispose()
                    PubblicaSuInterfaccia(AddressOf NonDisponibileSuInterfaccia, "L'assistente vocale non è disponibile: il computer non riesce a caricare il modello della parola di attivazione Hey Jarvis.")
                    Exit Sub
                End If

                SyncLock _bloccoRiconoscitore
                    If _arrestato Then
                        rilevatoreParola.Dispose()
                        Exit Sub
                    End If
                    _rilevatoreParola = rilevatoreParola
                End SyncLock

                If _localeRichiesto AndAlso Not ParakeetPerComandi() Then
                    Dim motivoLocale As String = ""
                    If CaricaLocale(False, motivoLocale) Then
                        _localeDisponibile = ComandiLocaliSuDisco()
                    Else
                        _localeDisponibile = False
                        RilasciaLocale()
                        If Not _arrestato Then PubblicaSuInterfaccia(AddressOf ErroreAvvioSuInterfaccia, "Il riconoscimento vocale locale non è disponibile: " & motivoLocale & ". Senza internet o a richieste esaurite l'assistente non potrà rispondere.")
                    End If
                End If

                If Not _arrestato Then PubblicaSuInterfaccia(AddressOf AvviaMicrofonoSuInterfaccia, Nothing)
                Exit Sub
            End If

            If _modo = ModoAttivazione.Windows Then
                Dim risposta As String = WGServiceInterop.InviaComando("WAKE_ON")
                If Not String.Equals(If(risposta, "").Trim(), "OK|1", StringComparison.OrdinalIgnoreCase) Then
                    log.ScriviLog("AssistenteVocale.CaricaModelliInSottofondo: avvio del riconoscimento di Windows non riuscito: " & risposta)
                    PubblicaSuInterfaccia(AddressOf NonDisponibileSuInterfaccia, "L'assistente vocale non è disponibile: non è stato possibile avviare il riconoscimento vocale di Windows.")
                    Exit Sub
                End If
                AvviaAscoltoServizio()
                PubblicaSuInterfaccia(AddressOf AvviaMicrofonoSuInterfaccia, Nothing)

                If _localeRichiesto AndAlso Not ParakeetPerComandi() Then
                    Dim motivoLocale As String = ""
                    If CaricaLocale(False, motivoLocale) Then
                        _localeDisponibile = ComandiLocaliSuDisco()
                    Else
                        _localeDisponibile = False
                        RilasciaLocale()
                        If Not _arrestato Then PubblicaSuInterfaccia(AddressOf ErroreAvvioSuInterfaccia, "Il riconoscimento vocale locale non è disponibile: " & motivoLocale & ". Senza internet o a richieste esaurite l'assistente non potrà rispondere.")
                    End If
                End If
                Exit Sub
            End If

            If _modo = ModoAttivazione.Parakeet Then
                Dim erroreParakeet As String = ""
                If WGTrascrittoreLocale.Precarica(erroreParakeet) Then
                    WGTrascrittoreLocale.MantieniInMemoria = True
                    If Not _arrestato Then PubblicaSuInterfaccia(AddressOf AvviaMicrofonoSuInterfaccia, Nothing)
                    Exit Sub
                End If
                If Not ComandiLocaliSuDisco() Then
                    If Not _arrestato Then PubblicaSuInterfaccia(AddressOf NonDisponibileSuInterfaccia, "L'assistente vocale non è disponibile: " & erroreParakeet)
                    Exit Sub
                End If
                _modo = ModoAttivazione.Vosk
                If Not _arrestato Then PubblicaSuInterfaccia(AddressOf ErroreAvvioSuInterfaccia, "Parakeet non si è caricato, Ehi Minerva userà Vosk.")
            End If

            Dim motivo As String = ""
            Dim pronto As Boolean = CaricaLocale(True, motivo) AndAlso VerificaPrestazioniLocali(True, motivo)
            If Not pronto AndAlso Not _arrestato AndAlso _qualitaAttivazione = QualitaVosk.Alta AndAlso ModelloPresente(QualitaVosk.Bassa) Then
                RilasciaLocale()
                _attivazioneSoloBassa = True
                motivo = ""
                pronto = CaricaLocale(True, motivo) AndAlso VerificaPrestazioniLocali(True, motivo)
            End If
            If Not pronto Then
                RilasciaLocale()
                If Not _arrestato Then PubblicaSuInterfaccia(AddressOf NonDisponibileSuInterfaccia, "L'assistente vocale non è disponibile: " & motivo & ".")
                Exit Sub
            End If
            _localeDisponibile = ComandiLocaliSuDisco()

            PubblicaSuInterfaccia(AddressOf AvviaMicrofonoSuInterfaccia, Nothing)
        Catch ex As Exception
            log.ScriviLog("AssistenteVocale.CaricaModelliInSottofondo: " & ex.Message & Environment.NewLine & ex.StackTrace)
        Finally
            PubblicaScaricamento(AddressOf CompletaAttesaAvvioSuInterfaccia, Nothing)
        End Try
    End Sub

    Private Sub ErroreAvvioSuInterfaccia(stato As Object)
        Try
            CompletaAttesaAvvio()
            Dim messaggio As String = TryCast(stato, String)
            If Not String.IsNullOrWhiteSpace(messaggio) Then Parla(messaggio)
        Catch ex As Exception
            log.ScriviLog("AssistenteVocale.ErroreAvvioSuInterfaccia: " & ex.Message)
        End Try
    End Sub

    Private Sub NonDisponibileSuInterfaccia(stato As Object)
        Try
            CompletaAttesaAvvio()
            Dim messaggio As String = TryCast(stato, String)
            If _istanza Is Me Then
                Ferma()
            Else
                Rilascia()
            End If
            If Not String.IsNullOrWhiteSpace(messaggio) Then MostraMessaggio(messaggio)
        Catch ex As Exception
            log.ScriviLog("AssistenteVocale.NonDisponibileSuInterfaccia: " & ex.Message & Environment.NewLine & ex.StackTrace)
        End Try
    End Sub

    Private Sub NonDisponibilePerConnessioneSuInterfaccia(stato As Object)
        Try
            CompletaAttesaAvvio()
            Dim messaggio As String = TryCast(stato, String)
            If _istanza Is Me Then
                Ferma()
            Else
                Rilascia()
            End If
            AttendiConnessionePerAvvio()
            If Not String.IsNullOrWhiteSpace(messaggio) Then MostraMessaggio(messaggio & " L'assistente vocale si riavvia da solo quando torna la connessione a internet.")
        Catch ex As Exception
            log.ScriviLog("AssistenteVocale.NonDisponibilePerConnessioneSuInterfaccia: " & ex.Message & Environment.NewLine & ex.StackTrace)
        End Try
    End Sub

    Private Shared Sub AttendiConnessionePerAvvio()
        Try
            AssicuraMarshalCondiviso()
            If _timerRiavvio IsNot Nothing Then Exit Sub
            _timerRiavvio = New System.Windows.Forms.Timer()
            _timerRiavvio.Interval = SECONDI_CONTROLLO_RIAVVIO * 1000
            AddHandler _timerRiavvio.Tick, AddressOf TimerRiavvioScaduto
            _timerRiavvio.Start()
        Catch ex As Exception
            log.ScriviLog("AssistenteVocale.AttendiConnessionePerAvvio: " & ex.Message & Environment.NewLine & ex.StackTrace)
        End Try
    End Sub

    Private Shared Sub FermaAttesaRiavvio()
        Try
            If _timerRiavvio Is Nothing Then Exit Sub
            Dim timerAttesa As System.Windows.Forms.Timer = _timerRiavvio
            _timerRiavvio = Nothing
            timerAttesa.Stop()
            RemoveHandler timerAttesa.Tick, AddressOf TimerRiavvioScaduto
            timerAttesa.Dispose()
        Catch ex As Exception
            log.ScriviLog("AssistenteVocale.FermaAttesaRiavvio: " & ex.Message)
        End Try
    End Sub

    Private Shared Sub TimerRiavvioScaduto(sender As Object, e As EventArgs)
        Try
            If _istanza IsNot Nothing OrElse Not Attivo() Then
                FermaAttesaRiavvio()
                Exit Sub
            End If
            If Interlocked.CompareExchange(_verificaRiavvioInCorso, 1, 0) <> 0 Then Exit Sub
            Task.Run(New Action(AddressOf LavoroVerificaRiavvio))
        Catch ex As Exception
            Interlocked.Exchange(_verificaRiavvioInCorso, 0)
            log.ScriviLog("AssistenteVocale.TimerRiavvioScaduto: " & ex.Message & Environment.NewLine & ex.StackTrace)
        End Try
    End Sub

    Private Shared Sub LavoroVerificaRiavvio()
        Try
            If InternetDisponibile() Then PubblicaScaricamento(AddressOf RiavviaDopoConnessioneSuInterfaccia, Nothing)
        Catch ex As Exception
            log.ScriviLog("AssistenteVocale.LavoroVerificaRiavvio: " & ex.Message & Environment.NewLine & ex.StackTrace)
        Finally
            Interlocked.Exchange(_verificaRiavvioInCorso, 0)
        End Try
    End Sub

    Private Shared Sub RiavviaDopoConnessioneSuInterfaccia(stato As Object)
        Try
            FermaAttesaRiavvio()
            If _istanza IsNot Nothing OrElse Not Attivo() Then Exit Sub
            AvviaVerifica(False)
        Catch ex As Exception
            log.ScriviLog("AssistenteVocale.RiavviaDopoConnessioneSuInterfaccia: " & ex.Message & Environment.NewLine & ex.StackTrace)
        End Try
    End Sub

    Private Sub DisponibilitaReteCambiata(sender As Object, e As System.Net.NetworkInformation.NetworkAvailabilityEventArgs)
        Try
            AvviaVerificaConnessioneRitardata()
        Catch ex As Exception
            log.ScriviLog("AssistenteVocale.DisponibilitaReteCambiata: " & ex.Message)
        End Try
    End Sub

    Private Sub IndirizzoReteCambiato(sender As Object, e As EventArgs)
        Try
            AvviaVerificaConnessioneRitardata()
        Catch ex As Exception
            log.ScriviLog("AssistenteVocale.IndirizzoReteCambiato: " & ex.Message)
        End Try
    End Sub

    Private Sub AvviaVerificaConnessioneRitardata()
        Try
            If _arrestato Then Exit Sub
            SyncLock _bloccoRitardoRete
                If _timerRitardoRete Is Nothing Then
                    _timerRitardoRete = New System.Threading.Timer(AddressOf RitardoReteScaduto, Nothing, MILLISECONDI_RITARDO_CAMBIO_RETE, System.Threading.Timeout.Infinite)
                Else
                    _timerRitardoRete.Change(MILLISECONDI_RITARDO_CAMBIO_RETE, System.Threading.Timeout.Infinite)
                End If
            End SyncLock
        Catch ex As Exception
            log.ScriviLog("AssistenteVocale.AvviaVerificaConnessioneRitardata: " & ex.Message)
        End Try
    End Sub

    Private Sub RitardoReteScaduto(stato As Object)
        Try
            AvviaVerificaConnessione()
        Catch ex As Exception
            log.ScriviLog("AssistenteVocale.RitardoReteScaduto: " & ex.Message)
        End Try
    End Sub

    Private Sub TimerConnessioneScaduto(sender As Object, e As EventArgs)
        Try
            AvviaVerificaConnessione()
        Catch ex As Exception
            log.ScriviLog("AssistenteVocale.TimerConnessioneScaduto: " & ex.Message)
        End Try
    End Sub

    Private Sub AvviaVerificaConnessione()
        Try
            If _arrestato Then Exit Sub
            If Interlocked.CompareExchange(_verificaConnessioneInCorso, 1, 0) <> 0 Then Exit Sub
            Task.Run(New Action(AddressOf LavoroVerificaConnessione))
        Catch ex As Exception
            Interlocked.Exchange(_verificaConnessioneInCorso, 0)
            log.ScriviLog("AssistenteVocale.AvviaVerificaConnessione: " & ex.Message & Environment.NewLine & ex.StackTrace)
        End Try
    End Sub

    Private Sub LavoroVerificaConnessione()
        Try
            If _arrestato Then Exit Sub
            Dim disponibile As Boolean = InternetDisponibile()
            If Not _arrestato Then PubblicaSuInterfaccia(AddressOf ConnessioneVerificataSuInterfaccia, disponibile)
        Catch ex As Exception
            log.ScriviLog("AssistenteVocale.LavoroVerificaConnessione: " & ex.Message & Environment.NewLine & ex.StackTrace)
        Finally
            Interlocked.Exchange(_verificaConnessioneInCorso, 0)
        End Try
    End Sub

    Private Sub ConnessioneVerificataSuInterfaccia(stato As Object)
        Try
            AggiornaStatoConnessione(CBool(stato))
        Catch ex As Exception
            log.ScriviLog("AssistenteVocale.ConnessioneVerificataSuInterfaccia: " & ex.Message)
        End Try
    End Sub

    Private Sub AggiornaStatoConnessione(disponibile As Boolean)
        Try
            If _arrestato Then Exit Sub
            _statoConnessione = If(disponibile, 1, 0)
            If _timerConnessione IsNot Nothing Then
                Dim intervallo As Integer = If(disponibile, SECONDI_CONTROLLO_CONNESSIONE_PRESENTE, SECONDI_CONTROLLO_CONNESSIONE_ASSENTE) * 1000
                If _timerConnessione.Interval <> intervallo Then _timerConnessione.Interval = intervallo
            End If
        Catch ex As Exception
            log.ScriviLog("AssistenteVocale.AggiornaStatoConnessione: " & ex.Message & Environment.NewLine & ex.StackTrace)
        End Try
    End Sub

    Private Sub FermaControlloConnessione()
        Try
            SyncLock _bloccoRitardoRete
                If _timerRitardoRete IsNot Nothing Then
                    _timerRitardoRete.Dispose()
                    _timerRitardoRete = Nothing
                End If
            End SyncLock
            If _eventiReteAgganciati Then
                RemoveHandler System.Net.NetworkInformation.NetworkChange.NetworkAvailabilityChanged, AddressOf DisponibilitaReteCambiata
                RemoveHandler System.Net.NetworkInformation.NetworkChange.NetworkAddressChanged, AddressOf IndirizzoReteCambiato
                _eventiReteAgganciati = False
            End If
            If _timerConnessione IsNot Nothing Then
                _timerConnessione.Stop()
                RemoveHandler _timerConnessione.Tick, AddressOf TimerConnessioneScaduto
                _timerConnessione.Dispose()
                _timerConnessione = Nothing
            End If
        Catch ex As Exception
            log.ScriviLog("AssistenteVocale.FermaControlloConnessione: " & ex.Message)
        End Try
    End Sub

    Private Shared Function ProviderAiInUso() As CLSAi.ProviderAi
        Try
            Dim elaborazione As ElaborazioneComandi = ElaborazioneImpostata()
            Dim preferito As CLSAi.ProviderAi = CLSAi.LeggiProviderPreferito()
            If elaborazione = ElaborazioneComandi.Gemini Then preferito = CLSAi.ProviderAi.Gemini
            If elaborazione = ElaborazioneComandi.Groq Then preferito = CLSAi.ProviderAi.Groq
            If CLSAi.ChiaveApiConfigurata(preferito) Then Return preferito
            Dim altro As CLSAi.ProviderAi = If(preferito = CLSAi.ProviderAi.Groq, CLSAi.ProviderAi.Gemini, CLSAi.ProviderAi.Groq)
            If CLSAi.ChiaveApiConfigurata(altro) Then Return altro
            Return preferito
        Catch ex As Exception
            log.ScriviLog("AssistenteVocale.ProviderAiInUso: " & ex.Message & Environment.NewLine & ex.StackTrace)
            Return CLSAi.ProviderAi.Gemini
        End Try
    End Function

    Private Function MsSilenzioFineFrase() As Integer
        Try
            Return If(_passoEmail = PassoEmail.Testo, MS_SILENZIO_FINE_TESTO_EMAIL, MS_SILENZIO_FINE_FRASE)
        Catch ex As Exception
            log.ScriviLog("AssistenteVocale.MsSilenzioFineFrase: " & ex.Message)
            Return MS_SILENZIO_FINE_FRASE
        End Try
    End Function

    Private Function ByteMassimiFrase() As Integer
        Try
            Return If(_passoEmail = PassoEmail.Testo, BYTE_MASSIMI_TESTO_EMAIL, BYTE_MASSIMI_FRASE)
        Catch ex As Exception
            log.ScriviLog("AssistenteVocale.ByteMassimiFrase: " & ex.Message)
            Return BYTE_MASSIMI_FRASE
        End Try
    End Function

    Private Function DialogoEmailAttivo() As Boolean
        Try
            SyncLock _bloccoDialogoEmail
                If _passoEmail = PassoEmail.Nessuno Then Return False
                If (DateTime.Now - _ultimoPassoEmail).TotalMinutes > MINUTI_VALIDITA_DIALOGO_EMAIL Then
                    AzzeraDialogoEmail()
                    Return False
                End If
                Return True
            End SyncLock
        Catch ex As Exception
            log.ScriviLog("AssistenteVocale.DialogoEmailAttivo: " & ex.Message & Environment.NewLine & ex.StackTrace)
            Return False
        End Try
    End Function

    Private Sub AzzeraDialogoEmail()
        Try
            _passoEmail = PassoEmail.Nessuno
            _emailDestinatario = ""
            _emailOggetto = Nothing
            _emailCorpo.Clear()
            _ultimoPassoEmail = DateTime.MinValue
        Catch ex As Exception
            log.ScriviLog("AssistenteVocale.AzzeraDialogoEmail: " & ex.Message & Environment.NewLine & ex.StackTrace)
        End Try
    End Sub

    Private Sub AnnullaDialogoEmail()
        Try
            SyncLock _bloccoDialogoEmail
                AzzeraDialogoEmail()
            End SyncLock
        Catch ex As Exception
            log.ScriviLog("AssistenteVocale.AnnullaDialogoEmail: " & ex.Message & Environment.NewLine & ex.StackTrace)
        End Try
    End Sub

    Private Function ProssimoPassoEmail() As PassoEmail
        Try
            If String.IsNullOrWhiteSpace(_emailDestinatario) Then Return PassoEmail.Destinatario
            If _emailOggetto Is Nothing Then Return PassoEmail.Oggetto
            If _emailCorpo.Length = 0 Then Return PassoEmail.Testo
            Return PassoEmail.Conferma
        Catch ex As Exception
            log.ScriviLog("AssistenteVocale.ProssimoPassoEmail: " & ex.Message & Environment.NewLine & ex.StackTrace)
            Return PassoEmail.Destinatario
        End Try
    End Function

    Private Function DomandaPassoEmail(passo As PassoEmail) As String
        Try
            Select Case passo
                Case PassoEmail.Destinatario
                    Return "A chi devo inviare la mail? Dire il nome di un contatto oppure l'indirizzo, per esempio mario punto rossi chiocciola gmail punto com."
                Case PassoEmail.Oggetto
                    Return "Qual è l'oggetto? Dire nessuno per lasciarlo vuoto."
                Case PassoEmail.Testo
                    Return "Dettare il testo. Per la punteggiatura dire virgola, punto, punto di domanda, punto esclamativo, a capo."
                Case PassoEmail.Conferma
                    Return RiepilogoEmail()
            End Select
            Return ""
        Catch ex As Exception
            log.ScriviLog("AssistenteVocale.DomandaPassoEmail: " & ex.Message & Environment.NewLine & ex.StackTrace)
            Return ""
        End Try
    End Function

    Private Function RiepilogoEmail() As String
        Try
            Dim riepilogo As New StringBuilder()
            riepilogo.Append("Destinatario: ").Append(_emailDestinatario).Append(". ")
            riepilogo.Append("Oggetto: ").Append(If(String.IsNullOrWhiteSpace(_emailOggetto), "nessuno", _emailOggetto)).Append(". ")
            riepilogo.Append("Testo: ").Append(_emailCorpo.ToString().Trim()).Append(" ")
            riepilogo.Append("Dire invia per inviare, controlla per aprirla e verificarla a mano, aggiungi per continuare il testo, riscrivi per dettarlo da capo, cambia oggetto, cambia destinatario oppure annulla.")
            Return riepilogo.ToString()
        Catch ex As Exception
            log.ScriviLog("AssistenteVocale.RiepilogoEmail: " & ex.Message & Environment.NewLine & ex.StackTrace)
            Return "Dire invia per inviare oppure annulla."
        End Try
    End Function

    Private Sub AvviaDialogoEmail(comando As ComandoVocale, risultato As RisultatoEsterno)
        Try
            SyncLock _bloccoDialogoEmail
                AzzeraDialogoEmail()
                Dim destinatarioRicevuto As String = TogliParolaAttivazione(If(comando.Destinatario, "")).Trim(" "c, ","c, "."c, ";"c, ":"c)
                If Not String.IsNullOrWhiteSpace(destinatarioRicevuto) Then _emailDestinatario = ConvertiIndirizzoDettato(destinatarioRicevuto)
                If Not String.IsNullOrWhiteSpace(comando.Oggetto) Then _emailOggetto = PreparaOggettoEmail(comando.Oggetto)
                If Not String.IsNullOrWhiteSpace(comando.Corpo) Then _emailCorpo.Append(PreparaTestoEmail(comando.Corpo))
                _passoEmail = ProssimoPassoEmail()
                _ultimoPassoEmail = DateTime.Now
                risultato.Risposta = DomandaPassoEmail(_passoEmail)
            End SyncLock
        Catch ex As Exception
            log.ScriviLog("AssistenteVocale.AvviaDialogoEmail: " & ex.Message & Environment.NewLine & ex.StackTrace)
            AnnullaDialogoEmail()
            risultato.Errore = "Non è stato possibile avviare la scrittura della mail."
        End Try
    End Sub

    Private Function TrascriviPerDialogo(audio As Byte(), testoFornito As String, connesso As Boolean) As String
        Try
            If audio Is Nothing OrElse audio.Length < BYTE_PER_SECONDO \ 3 Then Return If(testoFornito, "").Trim()
            If Not connesso OrElse ElaborazioneImpostata() = ElaborazioneComandi.Locale Then Return ""

            Dim provider As WGDettatura.ProviderDettatura = If(ElaborazioneImpostata() = ElaborazioneComandi.Groq, WGDettatura.ProviderDettatura.Groq, WGDettatura.ProviderDettatura.Gemini)
            Dim percorso As String = Path.Combine(Path.GetTempPath(), "wgassistente_" & Guid.NewGuid().ToString("N") & ".wav")
            Try
                Using scrittore As New NAudio.Wave.WaveFileWriter(percorso, New NAudio.Wave.WaveFormat(FREQUENZA_CAMPIONAMENTO, 16, 1))
                    scrittore.Write(audio, 0, audio.Length)
                End Using
                Dim trascrittore As New WGDettatura()
                Try
                    Dim esito As Tuple(Of String, String) = trascrittore.TrascriviFileConProviderAsync(percorso, provider).Result
                    If esito Is Nothing Then Return ""
                    If String.IsNullOrWhiteSpace(esito.Item1) Then
                        If Not String.IsNullOrEmpty(esito.Item2) Then log.ScriviLog("AssistenteVocale.TrascriviPerDialogo: " & esito.Item2)
                        Return ""
                    End If
                    Return esito.Item1.Trim()
                Finally
                    trascrittore.Dispose()
                End Try
            Finally
                Try
                    If File.Exists(percorso) Then File.Delete(percorso)
                Catch exPulizia As Exception
                    log.ScriviLog("AssistenteVocale.TrascriviPerDialogo (pulizia): " & exPulizia.Message)
                End Try
            End Try
        Catch ex As Exception
            log.ScriviLog("AssistenteVocale.TrascriviPerDialogo: " & ex.Message & Environment.NewLine & ex.StackTrace)
            Return ""
        End Try
    End Function

    Private Shared Function SembraInvia(breve As String) As Boolean
        Try
            If String.IsNullOrWhiteSpace(breve) OrElse Regex.IsMatch(breve, "\bnon\b") Then Return False
            Dim parole As String() = Regex.Split(breve.Trim(), "\s+")
            If parole.Length = 0 OrElse parole.Length > 3 Then Return False
            Dim candidati As New List(Of String)
            candidati.Add(String.Join("", parole))
            candidati.AddRange(parole)
            For Each candidato As String In candidati
                For Each obiettivo As String In New String() {"invia", "inviala", "invio", "inviare", "envía", "enviar"}
                    If SomiglianzaTesti(candidato, obiettivo) >= 0.7 Then Return True
                Next
            Next
            Return False
        Catch ex As Exception
            log.ScriviLog("AssistenteVocale.SembraInvia: " & ex.Message)
            Return False
        End Try
    End Function

    Private Shared Function NormalizzaRispostaBreve(testo As String) As String
        Try
            Dim risultato As String = If(testo, "").ToLowerInvariant()
            risultato = Regex.Replace(risultato, "[.,;:!?""«»]", " ")
            risultato = Regex.Replace(risultato, "\s+", " ").Trim()
            Return risultato
        Catch ex As Exception
            log.ScriviLog("AssistenteVocale.NormalizzaRispostaBreve: " & ex.Message)
            Return If(testo, "").Trim().ToLowerInvariant()
        End Try
    End Function

    Private Shared Function ConvertiIndirizzoDettato(testo As String) As String
        Try
            Dim risultato As String = Regex.Replace(If(testo, ""), "^(?:a|ad|per|al|alla|allo|all')\s+", "", RegexOptions.IgnoreCase).Trim()
            risultato = risultato.Trim(" "c, "."c, ","c, "!"c, "?"c, ";"c, ":"c)
            Dim sembraIndirizzo As Boolean = risultato.Contains("@") OrElse Regex.IsMatch(risultato, "\b(?:chiocciol[ae]|chiocciolina|at)\b", RegexOptions.IgnoreCase)
            If Not sembraIndirizzo Then Return risultato

            risultato = risultato.ToLowerInvariant()
            risultato = Regex.Replace(risultato, "\b(?:chiocciol[ae]|chiocciolina|at)\b", "@")
            risultato = Regex.Replace(risultato, "\b(?:trattino\s+basso|underscore|sottolineato)\b", "_")
            risultato = Regex.Replace(risultato, "\b(?:trattino|meno)\b", "-")
            risultato = Regex.Replace(risultato, "\bpunto\b", ".")
            risultato = Regex.Replace(risultato, "\s+", "")
            risultato = risultato.Trim("."c, ","c)
            Return risultato
        Catch ex As Exception
            log.ScriviLog("AssistenteVocale.ConvertiIndirizzoDettato: " & ex.Message & Environment.NewLine & ex.StackTrace)
            Return If(testo, "").Trim()
        End Try
    End Function

    Private Const SALUTI_INIZIALI As String = "(?:ciao|salve|buongiorno|buonasera|gentile|gentilissim[oa]|car[oaie]|egregi[oa]|spettabile|hello|hi)"
    Private Const SALUTI_CON_NOME As String = "(?:gentile|gentilissim[oa]|car[oaie]|egregi[oa]|spettabile)"
    Private Const SALUTI_FINALI As String = "(?:cordiali saluti|distinti saluti|cari saluti|tanti saluti|un caro saluto|un saluto|saluti|un abbraccio|un bacio|a presto|a dopo|a domani|grazie mille|grazie ancora|buona giornata|buona serata|buona domenica|ciao ciao|baci|con affetto)"

    Private Shared Function InizialeMaiuscola(testo As String) As String
        Try
            If String.IsNullOrEmpty(testo) Then Return If(testo, "")
            Return testo.Substring(0, 1).ToUpper(System.Globalization.CultureInfo.GetCultureInfo("it-IT")) & testo.Substring(1)
        Catch ex As Exception
            log.ScriviLog("AssistenteVocale.InizialeMaiuscola: " & ex.Message)
            Return If(testo, "")
        End Try
    End Function

    Private Shared Function FormattaCorpoEmail(testo As String) As String
        Try
            Dim corpo As String = Regex.Replace(If(testo, "").Trim(), "[ \t]+", " ")
            corpo = Regex.Replace(corpo, "\s*\n\s*", vbCrLf)
            If corpo = "" Then Return ""

            Dim testa As String = ""
            Dim coda As String = ""

            Dim inizio As Match = Regex.Match(corpo, "^(" & SALUTI_INIZIALI & "\b[^,.!?\r\n]{0,40}?)\s*[,!]\s*", RegexOptions.IgnoreCase)
            If Not inizio.Success Then inizio = Regex.Match(corpo, "^(" & SALUTI_CON_NOME & "\s+\S+)\s+", RegexOptions.IgnoreCase)
            If Not inizio.Success Then inizio = Regex.Match(corpo, "^((?i:" & SALUTI_INIZIALI & ")(?:\s+[A-ZÀ-Ý]\S*)?)\s+")
            If Not inizio.Success Then inizio = Regex.Match(corpo, "^(" & SALUTI_INIZIALI & ")\s+", RegexOptions.IgnoreCase)
            If inizio.Success AndAlso corpo.Length > inizio.Length Then
                testa = InizialeMaiuscola(inizio.Groups(1).Value.Trim().TrimEnd(","c, "!"c)) & ","
                corpo = corpo.Substring(inizio.Length)
            End If

            Dim fine As Match = Regex.Match(corpo, "(?:^|(?<=[\s.!?,]))(" & SALUTI_FINALI & ")[\s,]*([^\r\n.!?,]{0,40}?)([.!]?)\s*$", RegexOptions.IgnoreCase)
            If fine.Success Then
                Dim firma As String = fine.Groups(2).Value.Trim()
                If firma.Split(New Char() {" "c}, StringSplitOptions.RemoveEmptyEntries).Length <= 4 Then
                    corpo = corpo.Substring(0, fine.Groups(1).Index).Trim().TrimEnd(","c)
                    Dim chiusura As String = InizialeMaiuscola(fine.Groups(1).Value)
                    If firma <> "" Then
                        coda = chiusura & "," & vbCrLf & InizialeMaiuscola(firma)
                    Else
                        coda = chiusura & If(fine.Groups(3).Value = "", ".", fine.Groups(3).Value)
                    End If
                End If
            End If

            corpo = InizialeMaiuscola(corpo.Trim())
            If corpo <> "" AndAlso Not ".!?:".Contains(corpo.Substring(corpo.Length - 1)) Then corpo &= "."

            Dim parti As New List(Of String)
            If testa <> "" Then parti.Add(testa)
            If corpo <> "" Then parti.Add(corpo)
            If coda <> "" Then parti.Add(coda)
            Return String.Join(vbCrLf & vbCrLf, parti)
        Catch ex As Exception
            log.ScriviLog("AssistenteVocale.FormattaCorpoEmail: " & ex.Message & Environment.NewLine & ex.StackTrace)
            Return If(testo, "")
        End Try
    End Function

    Private Shared Function PreparaTestoEmail(testo As String) As String
        Try
            Dim risultato As String = WGDettatura.ApplicaPunteggiaturaDettata(ApplicaSostituzioni(If(testo, "")))
            Return risultato.Trim()
        Catch ex As Exception
            log.ScriviLog("AssistenteVocale.PreparaTestoEmail: " & ex.Message & Environment.NewLine & ex.StackTrace)
            Return If(testo, "").Trim()
        End Try
    End Function

    Private Shared Function PreparaOggettoEmail(testo As String) As String
        Try
            Dim risultato As String = PreparaTestoEmail(testo)
            risultato = Regex.Replace(risultato, "\s*[\r\n]+\s*", " ")
            Return risultato.TrimEnd("."c, " "c)
        Catch ex As Exception
            log.ScriviLog("AssistenteVocale.PreparaOggettoEmail: " & ex.Message & Environment.NewLine & ex.StackTrace)
            Return If(testo, "").Trim()
        End Try
    End Function

    Private Sub ElaborazioneDialogoEmail(richiesta As RichiestaVocale, audioValido As Boolean, risultato As RisultatoEsterno, connesso As Boolean)
        Try
            If ElaborazioneImpostata() = ElaborazioneComandi.Locale Then
                AnnullaDialogoEmail()
                risultato.Risposta = MessaggioMailSoloGemini()
                Exit Sub
            End If
            If Not connesso Then
                AnnullaDialogoEmail()
                risultato.Risposta = "La connessione a internet non è disponibile, quindi la mail non è stata inviata."
                Exit Sub
            End If
            Dim testo As String = TogliParolaAttivazione(TrascriviPerDialogo(If(audioValido, richiesta.Audio, Nothing), richiesta.Testo, connesso)).Trim()
            risultato.Domanda = testo
            If String.IsNullOrWhiteSpace(testo) Then
                risultato.Risposta = "Non ho sentito bene. " & DomandaPassoEmail(_passoEmail)
                Exit Sub
            End If

            Dim breve As String = NormalizzaRispostaBreve(testo)
            SyncLock _bloccoDialogoEmail
                _ultimoPassoEmail = DateTime.Now

                If Regex.IsMatch(breve, MODELLO_ANNULLA_EMAIL) Then
                    AzzeraDialogoEmail()
                    risultato.Risposta = "Mail annullata."
                    Exit Sub
                End If

                Select Case _passoEmail
                    Case PassoEmail.Destinatario
                        _emailDestinatario = ConvertiIndirizzoDettato(ApplicaSostituzioni(testo))
                        If String.IsNullOrWhiteSpace(_emailDestinatario) Then
                            risultato.Risposta = DomandaPassoEmail(PassoEmail.Destinatario)
                            Exit Sub
                        End If

                    Case PassoEmail.Oggetto
                        _emailOggetto = If(Regex.IsMatch(breve, MODELLO_NESSUN_OGGETTO), "", PreparaOggettoEmail(testo))

                    Case PassoEmail.Testo
                        Dim parte As String = Regex.Replace(testo, MODELLO_FINE_TESTO_EMAIL, "", RegexOptions.IgnoreCase).Trim()
                        parte = PreparaTestoEmail(parte)
                        If parte <> "" Then
                            If _emailCorpo.Length > 0 Then _emailCorpo.Append(" ")
                            _emailCorpo.Append(parte)
                        End If
                        If _emailCorpo.Length = 0 Then
                            risultato.Risposta = DomandaPassoEmail(PassoEmail.Testo)
                            Exit Sub
                        End If

                    Case PassoEmail.Conferma
                        If Regex.IsMatch(breve, MODELLO_CONFERMA_EMAIL) OrElse (Regex.IsMatch(breve, MODELLO_VERBO_INVIO_EMAIL) AndAlso Not Regex.IsMatch(breve, "\bnon\b")) OrElse SembraInvia(breve) Then
                            Dim invio As New ComandoVocale()
                            invio.Tipo = TipoComandoVocale.InviaEmail
                            invio.TestoOriginale = testo
                            invio.Destinatario = _emailDestinatario
                            invio.Oggetto = If(_emailOggetto, "")
                            invio.Corpo = FormattaCorpoEmail(_emailCorpo.ToString())
                            invio.InvioConfermato = True
                            invio.OrigineOnline = True
                            AzzeraDialogoEmail()
                            If Not connesso Then
                                risultato.Risposta = "Senza connessione a internet non posso inviare la posta. Riprova quando la connessione torna."
                                Exit Sub
                            End If
                            risultato.Comando = invio
                            Exit Sub
                        ElseIf Regex.IsMatch(breve, "^(?:aggiungi|continua|aggiungi\s+(?:del\s+)?testo|continua\s+(?:il\s+)?testo)$") Then
                            _passoEmail = PassoEmail.Testo
                            risultato.Risposta = "Continuare a dettare il testo."
                            Exit Sub
                        ElseIf Regex.IsMatch(breve, "^(?:riscrivi|ridetta|da\s+capo|ricomincia|cancella\s+(?:il\s+)?testo|riscrivi\s+(?:il\s+)?testo)$") Then
                            _emailCorpo.Clear()
                            _passoEmail = PassoEmail.Testo
                            risultato.Risposta = DomandaPassoEmail(PassoEmail.Testo)
                            Exit Sub
                        ElseIf Regex.IsMatch(breve, "^(?:cambia|modifica|correggi)\s+(?:l'\s*)?oggetto$") Then
                            _emailOggetto = Nothing
                            _passoEmail = PassoEmail.Oggetto
                            risultato.Risposta = DomandaPassoEmail(PassoEmail.Oggetto)
                            Exit Sub
                        ElseIf Regex.IsMatch(breve, "^(?:cambia|modifica|correggi)\s+(?:il\s+)?destinatario$") Then
                            _emailDestinatario = ""
                            _passoEmail = PassoEmail.Destinatario
                            risultato.Risposta = DomandaPassoEmail(PassoEmail.Destinatario)
                            Exit Sub
                        ElseIf Regex.IsMatch(breve, MODELLO_CONTROLLO_EMAIL) Then
                            Dim controllo As New ComandoVocale()
                            controllo.Tipo = TipoComandoVocale.InviaEmail
                            controllo.TestoOriginale = testo
                            controllo.Destinatario = _emailDestinatario
                            controllo.Oggetto = If(_emailOggetto, "")
                            controllo.Corpo = FormattaCorpoEmail(_emailCorpo.ToString())
                            controllo.ApriPerControllo = True
                            controllo.OrigineOnline = True
                            AzzeraDialogoEmail()
                            risultato.Comando = controllo
                            Exit Sub
                        ElseIf Regex.IsMatch(breve, "^(?:no|non\s+inviare)$") Then
                            AzzeraDialogoEmail()
                            risultato.Risposta = "Mail annullata."
                            Exit Sub
                        ElseIf Regex.IsMatch(breve, "^(?:rileggi|ripeti|rileggila)$") Then
                            risultato.Risposta = RiepilogoEmail()
                            Exit Sub
                        Else
                            risultato.Risposta = "Non ho capito. Dire invia per inviare, oppure controlla, aggiungi, riscrivi, cambia oggetto, cambia destinatario, annulla."
                            Exit Sub
                        End If
                End Select

                _passoEmail = ProssimoPassoEmail()
                risultato.Risposta = DomandaPassoEmail(_passoEmail)
            End SyncLock
        Catch ex As Exception
            log.ScriviLog("AssistenteVocale.ElaborazioneDialogoEmail: " & ex.Message & Environment.NewLine & ex.StackTrace)
            AnnullaDialogoEmail()
            risultato.Errore = "Si è verificato un errore durante la scrittura della mail. La mail non è stata inviata."
        End Try
    End Sub

    Private Shared Function PercorsoSostituzioni() As String
        Try
            Return Path.Combine(WGDb.WGPercorsiApplicazione.CartellaConfig(), NOME_FILE_SOSTITUZIONI)
        Catch ex As Exception
            log.ScriviLog("AssistenteVocale.PercorsoSostituzioni: " & ex.Message)
            Return NOME_FILE_SOSTITUZIONI
        End Try
    End Function

    Private Shared Sub CreaSostituzioniPredefinite(percorso As String)
        Try
            If File.Exists(percorso) Then Exit Sub
            Dim cartella As String = Path.GetDirectoryName(percorso)
            If Not String.IsNullOrEmpty(cartella) AndAlso Not Directory.Exists(cartella) Then Directory.CreateDirectory(cartella)
            Dim righe As String() = {
                "iu tub = YouTube",
                "iutub = YouTube",
                "iu tube = YouTube",
                "you tub = YouTube",
                "uotsap = WhatsApp",
                "uatsap = WhatsApp",
                "uazzap = WhatsApp",
                "wazzap = WhatsApp",
                "feisbuk = Facebook",
                "feisbuc = Facebook",
                "feis buk = Facebook",
                "gugol = Google",
                "gugl = Google",
                "spotifai = Spotify",
                "spoti fai = Spotify",
                "instagram = Instagram",
                "tic toc = TikTok",
                "tik tok = TikTok",
                "aifon = iPhone",
                "ai fon = iPhone",
                "pleilist = playlist",
                "plei list = playlist",
                "treiler = trailer",
                "liriks = lyrics",
                "ofiscial = official",
                "laiv = live",
                "podcast = podcast"
            }
            File.WriteAllLines(percorso, righe, New UTF8Encoding(False))
        Catch ex As Exception
            log.ScriviLog("AssistenteVocale.CreaSostituzioniPredefinite: " & ex.Message & Environment.NewLine & ex.StackTrace)
        End Try
    End Sub

    Private Shared Sub CaricaSostituzioniSeCambiate()
        Try
            Dim percorso As String = PercorsoSostituzioni()
            CreaSostituzioniPredefinite(percorso)
            If Not File.Exists(percorso) Then Exit Sub
            Dim dataFile As DateTime = File.GetLastWriteTimeUtc(percorso)
            SyncLock _bloccoSostituzioni
                If dataFile = _dataFileSostituzioni Then Exit Sub
                Dim nuove As New List(Of Tuple(Of Regex, String))
                Dim numeroRiga As Integer = 0
                For Each riga As String In File.ReadAllLines(percorso, Encoding.UTF8)
                    numeroRiga += 1
                    If String.IsNullOrWhiteSpace(riga) Then Continue For
                    Dim separatore As Integer = riga.IndexOf("="c)
                    If separatore <= 0 Then
                        log.ScriviLog("AssistenteVocale.CaricaSostituzioniSeCambiate: riga " & numeroRiga.ToString(System.Globalization.CultureInfo.InvariantCulture) & " di " & NOME_FILE_SOSTITUZIONI & " senza il segno uguale, ignorata: " & riga)
                        Continue For
                    End If
                    Dim parlato As String = riga.Substring(0, separatore).Trim()
                    Dim scritto As String = riga.Substring(separatore + 1).Trim()
                    If parlato = "" Then Continue For
                    Dim parole As String = Regex.Escape(parlato).Replace("\ ", "\s+")
                    Try
                        nuove.Add(New Tuple(Of Regex, String)(New Regex("(?<![\p{L}\p{N}])" & parole & "(?![\p{L}\p{N}])", RegexOptions.IgnoreCase Or RegexOptions.CultureInvariant), scritto))
                    Catch exRiga As Exception
                        log.ScriviLog("AssistenteVocale.CaricaSostituzioniSeCambiate (riga " & numeroRiga.ToString(System.Globalization.CultureInfo.InvariantCulture) & "): " & exRiga.Message)
                    End Try
                Next
                nuove.Sort(Function(a As Tuple(Of Regex, String), b As Tuple(Of Regex, String)) b.Item1.ToString().Length.CompareTo(a.Item1.ToString().Length))
                _sostituzioni = nuove
                _dataFileSostituzioni = dataFile
            End SyncLock
        Catch ex As Exception
            log.ScriviLog("AssistenteVocale.CaricaSostituzioniSeCambiate: " & ex.Message & Environment.NewLine & ex.StackTrace)
        End Try
    End Sub

    Private Shared Function ApplicaSostituzioni(testo As String) As String
        Try
            If String.IsNullOrWhiteSpace(testo) Then Return If(testo, "")
            testo = WGTrascrittoreLocale.CorreggiTrascrizione(testo)
            CaricaSostituzioniSeCambiate()
            Dim elenco As List(Of Tuple(Of Regex, String))
            SyncLock _bloccoSostituzioni
                elenco = _sostituzioni
            End SyncLock
            Dim risultato As String = testo
            For Each sostituzione As Tuple(Of Regex, String) In elenco
                Dim scritto As String = sostituzione.Item2
                risultato = sostituzione.Item1.Replace(risultato, Function(m As Match) scritto)
            Next
            Return risultato
        Catch ex As Exception
            log.ScriviLog("AssistenteVocale.ApplicaSostituzioni: " & ex.Message & Environment.NewLine & ex.StackTrace)
            Return testo
        End Try
    End Function

    Private Shared Function TestoPerConfronto(testo As String) As String
        Try
            Return Regex.Replace(If(testo, "").ToLowerInvariant(), "[^\p{L}\p{N}]", "")
        Catch ex As Exception
            log.ScriviLog("AssistenteVocale.TestoPerConfronto: " & ex.Message)
            Return If(testo, "").ToLowerInvariant()
        End Try
    End Function

    Private Shared Function SomiglianzaTesti(primo As String, secondo As String) As Double
        Try
            Dim a As String = TestoPerConfronto(primo)
            Dim b As String = TestoPerConfronto(secondo)
            If a.Length = 0 OrElse b.Length = 0 Then Return 0.0
            Dim precedente(b.Length) As Integer
            Dim corrente(b.Length) As Integer
            For j As Integer = 0 To b.Length
                precedente(j) = j
            Next
            For i As Integer = 1 To a.Length
                corrente(0) = i
                For j As Integer = 1 To b.Length
                    Dim costo As Integer = If(a(i - 1) = b(j - 1), 0, 1)
                    corrente(j) = Math.Min(Math.Min(corrente(j - 1) + 1, precedente(j) + 1), precedente(j - 1) + costo)
                Next
                Dim scambio As Integer() = precedente
                precedente = corrente
                corrente = scambio
            Next
            Return 1.0 - precedente(b.Length) / CDbl(Math.Max(a.Length, b.Length))
        Catch ex As Exception
            log.ScriviLog("AssistenteVocale.SomiglianzaTesti: " & ex.Message & Environment.NewLine & ex.StackTrace)
            Return 0.0
        End Try
    End Function

    Private Shared Function ChiaveFoneticaTitolo(testo As String) As String
        Try
            Dim scomposto As String = If(testo, "").ToLowerInvariant().Normalize(NormalizationForm.FormD)
            Dim senzaAccenti As New StringBuilder()
            For Each carattere As Char In scomposto
                If System.Globalization.CharUnicodeInfo.GetUnicodeCategory(carattere) <> System.Globalization.UnicodeCategory.NonSpacingMark Then senzaAccenti.Append(carattere)
            Next
            Dim chiave As String = Regex.Replace(senzaAccenti.ToString(), "[^a-z0-9]", "")
            For Each coppia As String() In New String()() {
                New String() {"sch", "s"}, New String() {"ph", "f"}, New String() {"th", "t"}, New String() {"sh", "s"},
                New String() {"ck", "c"}, New String() {"gh", "g"}, New String() {"ch", "c"}, New String() {"k", "c"},
                New String() {"q", "c"}, New String() {"y", "i"}, New String() {"w", "u"}, New String() {"j", "g"},
                New String() {"x", "cs"}, New String() {"h", ""}}
                chiave = chiave.Replace(coppia(0), coppia(1))
            Next
            Return Regex.Replace(chiave, "(.)\1+", "$1")
        Catch ex As Exception
            log.ScriviLog("AssistenteVocale.ChiaveFoneticaTitolo: " & ex.Message)
            Return TestoPerConfronto(testo)
        End Try
    End Function

    Private Shared Function SomiglianzaChiavi(a As String, b As String) As Double
        Try
            If String.IsNullOrEmpty(a) OrElse String.IsNullOrEmpty(b) Then Return 0.0
            Dim precedente(b.Length) As Integer
            Dim corrente(b.Length) As Integer
            For j As Integer = 0 To b.Length
                precedente(j) = j
            Next
            For i As Integer = 1 To a.Length
                corrente(0) = i
                For j As Integer = 1 To b.Length
                    Dim costo As Integer = If(a(i - 1) = b(j - 1), 0, 1)
                    corrente(j) = Math.Min(Math.Min(corrente(j - 1) + 1, precedente(j) + 1), precedente(j - 1) + costo)
                Next
                Dim scambio As Integer() = precedente
                precedente = corrente
                corrente = scambio
            Next
            Return 1.0 - precedente(b.Length) / CDbl(Math.Max(a.Length, b.Length))
        Catch ex As Exception
            log.ScriviLog("AssistenteVocale.SomiglianzaChiavi: " & ex.Message)
            Return 0.0
        End Try
    End Function

    Private Shared Function SuggerimentiYoutube(client As System.Net.Http.HttpClient, richiesta As String) As List(Of String)
        Dim risultato As New List(Of String)
        Try
            If String.IsNullOrWhiteSpace(richiesta) Then Return risultato
            Dim corpo As String = client.GetStringAsync(INDIRIZZO_SUGGERIMENTI_YOUTUBE & Uri.EscapeDataString(richiesta.Trim())).Result
            If String.IsNullOrWhiteSpace(corpo) Then Return risultato
            Dim dati As JArray = JArray.Parse(corpo)
            If dati.Count < 2 OrElse dati(1).Type <> JTokenType.Array Then Return risultato
            For Each voce As JToken In CType(dati(1), JArray)
                Dim suggerimento As String = Convert.ToString(voce).Trim()
                If suggerimento <> "" Then risultato.Add(suggerimento)
            Next
        Catch ex As Exception
            log.ScriviLog("AssistenteVocale.SuggerimentiYoutube (" & richiesta & "): " & ex.Message)
        End Try
        Return risultato
    End Function

    Private Shared Function RichiesteSuggerimenti(titolo As String) As List(Of String)
        Dim richieste As New List(Of String)
        Try
            Dim parole As String() = Regex.Split(titolo.Trim(), "\s+")
            Dim candidati As New List(Of String)
            candidati.Add(titolo.Trim())
            If parole.Length >= 3 Then candidati.Add(parole(0) & " " & parole(1))
            If parole.Length >= 2 AndAlso parole(0).Length >= 3 Then candidati.Add(parole(0))
            If parole.Length >= 1 AndAlso parole(0).Length > 4 Then candidati.Add(parole(0).Substring(0, 4))
            For Each candidato As String In candidati
                If Not richieste.Exists(Function(r As String) String.Equals(r, candidato, StringComparison.OrdinalIgnoreCase)) Then richieste.Add(candidato)
            Next
        Catch ex As Exception
            log.ScriviLog("AssistenteVocale.RichiesteSuggerimenti: " & ex.Message)
            If richieste.Count = 0 Then richieste.Add(titolo)
        End Try
        Return richieste
    End Function

    Private Shared Function CorreggiTitoloYoutube(titolo As String) As String
        Try
            If String.IsNullOrWhiteSpace(titolo) OrElse titolo.Trim().Length < 3 Then Return If(titolo, "")
            Dim chiaveTitolo As String = ChiaveFoneticaTitolo(titolo)
            If chiaveTitolo.Length < 3 Then Return titolo

            Dim migliore As String = ""
            Dim punteggioMigliore As Double = 0.0
            Using client As New System.Net.Http.HttpClient()
                client.Timeout = TimeSpan.FromSeconds(3)
                For Each richiesta As String In RichiesteSuggerimenti(titolo)
                    For Each suggerimento As String In SuggerimentiYoutube(client, richiesta)
                        Dim parole As String() = Regex.Split(suggerimento, "\s+")
                        For quante As Integer = 1 To parole.Length
                            Dim inizio As String = String.Join(" ", parole, 0, quante)
                            Dim chiaveInizio As String = ChiaveFoneticaTitolo(inizio)
                            If chiaveInizio.Length = 0 Then Continue For
                            Dim rapporto As Double = Math.Min(chiaveInizio.Length, chiaveTitolo.Length) / CDbl(Math.Max(chiaveInizio.Length, chiaveTitolo.Length))
                            If rapporto < 0.75 Then Continue For
                            Dim punteggio As Double = SomiglianzaChiavi(chiaveTitolo, chiaveInizio)
                            If punteggio > punteggioMigliore Then
                                punteggioMigliore = punteggio
                                migliore = inizio
                            End If
                        Next
                    Next
                    If punteggioMigliore >= 0.9 Then Exit For
                Next
            End Using

            If migliore <> "" AndAlso punteggioMigliore >= SOMIGLIANZA_MINIMA_SUGGERIMENTO Then Return migliore
            Return titolo
        Catch ex As Exception
            log.ScriviLog("AssistenteVocale.CorreggiTitoloYoutube: " & ex.Message & Environment.NewLine & ex.StackTrace)
            Return If(titolo, "")
        End Try
    End Function

    Private Shared Function MessaggioMailSoloGemini() As String
        Try
            If ElaborazioneImpostata() = ElaborazioneComandi.Locale Then
                Return "Con l'elaborazione dei comandi sul computer non è possibile inviare mail. Per inviarle, scegliere Gemini o Groq in Elaborazione dei comandi."
            End If
            Return "L'intelligenza artificiale non ha risposto, quindi non posso inviare la mail. Riprovare tra poco."
        Catch ex As Exception
            log.ScriviLog("AssistenteVocale.MessaggioMailSoloGemini: " & ex.Message)
            Return "L'invio delle mail funziona solo con Gemini o con Groq."
        End Try
    End Function

    Private Shared Function AzioneInternet(comando As ComandoVocale) As String
        Try
            If comando Is Nothing Then Return ""
            Select Case comando.Tipo
                Case TipoComandoVocale.RiproduciVideo, TipoComandoVocale.CercaVideo
                    Return "usare YouTube"
                Case TipoComandoVocale.AscoltaRadio
                    Return "ascoltare la radio"
                Case TipoComandoVocale.InviaEmail
                    Return "inviare la posta"
                Case TipoComandoVocale.LeggiPosta, TipoComandoVocale.LeggiUltimaEmail
                    Return "leggere la posta"
                Case TipoComandoVocale.Meteo
                    Return "consultare il meteo"
                Case TipoComandoVocale.ApriSezione
                    Select Case If(comando.Argomento, "").Trim().ToLowerInvariant()
                        Case "posta"
                            Return "aprire la posta"
                        Case "youtube"
                            Return "aprire YouTube"
                        Case "meteo"
                            Return "aprire il meteo"
                        Case "radio"
                            Return "aprire la radio"
                        Case "ai"
                            Return "aprire l'intelligenza artificiale"
                    End Select
            End Select
            Return ""
        Catch ex As Exception
            log.ScriviLog("AssistenteVocale.AzioneInternet: " & ex.Message)
            Return ""
        End Try
    End Function

    Private Shared Function CaricaModelloVosk(percorsoModello As String) As IntPtr
        Try
            If String.IsNullOrEmpty(percorsoModello) Then Return IntPtr.Zero
            Dim modello As IntPtr = vosk_model_new(Encoding.UTF8.GetBytes(percorsoModello & ChrW(0)))
            Return modello
        Catch ex As Exception
            log.ScriviLog("AssistenteVocale.CaricaModelloVosk: " & ex.Message & Environment.NewLine & ex.StackTrace)
            Return IntPtr.Zero
        End Try
    End Function

    Private Function CaricaLocale(conAttivazione As Boolean, ByRef motivo As String) As Boolean
        Dim modelloPiccolo As IntPtr = IntPtr.Zero
        Dim riconoscitoreVerifica As IntPtr = IntPtr.Zero
        Dim riconoscitoreAttivazione As IntPtr = IntPtr.Zero
        Dim riduttoreAscolto As RiduttoreRumore = Nothing
        Dim riduttoreFrasi As RiduttoreRumore = Nothing
        Dim assegnati As Boolean = False
        Try
            If Not CaricaLibreria() Then
                motivo = "non è stato possibile caricare le librerie del riconoscimento vocale locale"
                Return False
            End If

            Dim qualitaAttivazione As QualitaVosk = QualitaVosk.Bassa
            If conAttivazione Then
                qualitaAttivazione = QualitaAttivazioneUtilizzabile()
                If Not ModelloPresente(qualitaAttivazione) Then
                    motivo = "il modello vocale locale non è stato scaricato"
                    Return False
                End If

                modelloPiccolo = CaricaModelloVosk(PercorsoModelloEffettivo(qualitaAttivazione))
                If modelloPiccolo = IntPtr.Zero Then
                    motivo = "non è stato possibile caricare il modello vocale locale in " & DescrizioneQualita(qualitaAttivazione)
                    Return False
                End If

                If qualitaAttivazione = QualitaVosk.Bassa Then
                    riconoscitoreVerifica = vosk_recognizer_new(modelloPiccolo, CSng(FREQUENZA_CAMPIONAMENTO))
                    riconoscitoreAttivazione = vosk_recognizer_new_grm(modelloPiccolo, CSng(FREQUENZA_CAMPIONAMENTO), Encoding.UTF8.GetBytes(GrammaticaAttivazione(modelloPiccolo) & ChrW(0)))
                    If riconoscitoreVerifica = IntPtr.Zero OrElse riconoscitoreAttivazione = IntPtr.Zero Then
                        motivo = "non è stato possibile preparare il riconoscimento della parola di attivazione"
                        Return False
                    End If
                    vosk_recognizer_set_words(riconoscitoreVerifica, 1)
                Else
                    riconoscitoreAttivazione = vosk_recognizer_new(modelloPiccolo, CSng(FREQUENZA_CAMPIONAMENTO))
                    If riconoscitoreAttivazione = IntPtr.Zero Then
                        motivo = "non è stato possibile preparare il riconoscimento della parola di attivazione"
                        Return False
                    End If
                End If
                vosk_recognizer_set_words(riconoscitoreAttivazione, 1)
            End If

            If AssicuraRiduttoreRumore() Then
                If conAttivazione Then riduttoreAscolto = CreaRiduttoreRumore()
                riduttoreFrasi = CreaRiduttoreRumore()
            End If

            SyncLock _bloccoRiconoscitore
                SyncLock _bloccoComandi
                    If _arrestato Then
                        motivo = "l'assistente è stato fermato"
                        Return False
                    End If
                    _modelloAttivazione = modelloPiccolo
                    _qualitaAttivazione = qualitaAttivazione
                    _riconoscitore = riconoscitoreVerifica
                    _riconoscitoreAttivazione = riconoscitoreAttivazione
                    _preElaborazioneAscolto = If(conAttivazione, New PreElaborazioneVoce(riduttoreAscolto), Nothing)
                    _preElaborazioneFrasi = New PreElaborazioneVoce(riduttoreFrasi)
                    assegnati = True
                End SyncLock
            End SyncLock
            Return True
        Catch ex As Exception
            log.ScriviLog("AssistenteVocale.CaricaLocale: " & ex.Message & Environment.NewLine & ex.StackTrace)
            motivo = "si è verificato un errore nel caricamento del riconoscimento vocale locale"
            Return False
        Finally
            If Not assegnati Then
                Try
                    If riconoscitoreVerifica <> IntPtr.Zero Then vosk_recognizer_free(riconoscitoreVerifica)
                    If riconoscitoreAttivazione <> IntPtr.Zero Then vosk_recognizer_free(riconoscitoreAttivazione)
                    If modelloPiccolo <> IntPtr.Zero Then vosk_model_free(modelloPiccolo)
                    If riduttoreAscolto IsNot Nothing Then riduttoreAscolto.Dispose()
                    If riduttoreFrasi IsNot Nothing Then riduttoreFrasi.Dispose()
                Catch exPulizia As Exception
                    log.ScriviLog("AssistenteVocale.CaricaLocale (pulizia): " & exPulizia.Message)
                End Try
            End If
        End Try
    End Function

    Private Function QualitaAttivazioneUtilizzabile() As QualitaVosk
        Try
            If _qualita = QualitaVosk.Alta AndAlso Not _attivazioneSoloBassa AndAlso ModelloPresente(QualitaVosk.Alta) AndAlso MemoriaLiberaSufficiente(QualitaVosk.Alta) Then Return QualitaVosk.Alta
            If ModelloPresente(QualitaVosk.Bassa) Then Return QualitaVosk.Bassa
            If Not _attivazioneSoloBassa AndAlso ModelloPresente(QualitaVosk.Alta) AndAlso MemoriaLiberaSufficiente(QualitaVosk.Alta) Then Return QualitaVosk.Alta
            Return QualitaVosk.Bassa
        Catch ex As Exception
            log.ScriviLog("AssistenteVocale.QualitaAttivazioneUtilizzabile: " & ex.Message)
            Return QualitaVosk.Bassa
        End Try
    End Function

    Private Shared Function ComandiLocaliSuDisco() As Boolean
        Try
            Return ModelloPresente(QualitaVosk.Bassa) OrElse ModelloPresente(QualitaVosk.Alta)
        Catch ex As Exception
            log.ScriviLog("AssistenteVocale.ComandiLocaliSuDisco: " & ex.Message)
            Return False
        End Try
    End Function

    Private Function AssicuraRiconoscitoreComandi() As Boolean
        Try
            If _arrestato Then Return False
            If _riconoscitoreComandi <> IntPtr.Zero Then
                ProgrammaRilascioComandi()
                Return True
            End If
            If Not CaricaLibreria() Then Return False

            If _modelloAttivazione <> IntPtr.Zero Then
                If Not CaricaModelloComandi(_qualitaAttivazione) Then Return False
                ProgrammaRilascioComandi()
                Return True
            End If

            Dim qualita As QualitaVosk = _qualita
            Dim grandeGiaCaricato As Boolean = _modelloAttivazione <> IntPtr.Zero AndAlso _qualitaAttivazione = QualitaVosk.Alta
            If qualita = QualitaVosk.Alta AndAlso Not grandeGiaCaricato AndAlso (Not ModelloPresente(QualitaVosk.Alta) OrElse Not MemoriaLiberaSufficiente(QualitaVosk.Alta)) Then qualita = QualitaVosk.Bassa
            If qualita = QualitaVosk.Bassa AndAlso Not ModelloPresente(QualitaVosk.Bassa) AndAlso grandeGiaCaricato Then qualita = QualitaVosk.Alta

            If qualita = QualitaVosk.Alta Then
                If Not CaricaModelloComandi(QualitaVosk.Alta) OrElse (Not grandeGiaCaricato AndAlso Not PrestazioniComandiSufficienti()) Then
                    RilasciaComandiInterno()
                    qualita = QualitaVosk.Bassa
                End If
            End If

            If qualita = QualitaVosk.Bassa AndAlso _riconoscitoreComandi = IntPtr.Zero Then
                If Not ModelloPresente(QualitaVosk.Bassa) Then Return False
                CaricaModelloComandi(QualitaVosk.Bassa)
            End If

            If _riconoscitoreComandi = IntPtr.Zero Then Return False
            ProgrammaRilascioComandi()
            Return True
        Catch ex As Exception
            log.ScriviLog("AssistenteVocale.AssicuraRiconoscitoreComandi: " & ex.Message & Environment.NewLine & ex.StackTrace)
            Return False
        End Try
    End Function

    Private Function CaricaModelloComandi(qualita As QualitaVosk) As Boolean
        Dim modello As IntPtr = IntPtr.Zero
        Dim proprio As Boolean = False
        Try
            If _modelloAttivazione <> IntPtr.Zero AndAlso _qualitaAttivazione = qualita Then
                modello = _modelloAttivazione
            Else
                modello = CaricaModelloVosk(PercorsoModelloEffettivo(qualita))
                proprio = True
            End If
            If modello = IntPtr.Zero Then Return False

            Dim riconoscitore As IntPtr = vosk_recognizer_new(modello, CSng(FREQUENZA_CAMPIONAMENTO))
            If riconoscitore = IntPtr.Zero Then
                If proprio Then vosk_model_free(modello)
                Return False
            End If
            vosk_recognizer_set_max_alternatives(riconoscitore, ALTERNATIVE_COMANDI)

            _riconoscitoreComandi = riconoscitore
            _modelloComandi = If(proprio, modello, IntPtr.Zero)
            Return True
        Catch ex As Exception
            log.ScriviLog("AssistenteVocale.CaricaModelloComandi: " & ex.Message & Environment.NewLine & ex.StackTrace)
            Try
                If proprio AndAlso modello <> IntPtr.Zero AndAlso _modelloComandi <> modello Then vosk_model_free(modello)
            Catch exPulizia As Exception
                log.ScriviLog("AssistenteVocale.CaricaModelloComandi (pulizia): " & exPulizia.Message)
            End Try
            Return False
        End Try
    End Function

    Private Function PrestazioniComandiSufficienti() As Boolean
        Try
            Dim audio As Byte() = AudioDiProva(MILLISECONDI_PROVA_PRESTAZIONI)
            Dim riscaldamento As Byte() = AudioDiProva(250)
            If audio.Length = 0 Then Return True
            RiconosciLocalmente(riscaldamento)
            Dim cronometro As Stopwatch = Stopwatch.StartNew()
            RiconosciLocalmente(audio)
            cronometro.Stop()
            Return cronometro.Elapsed.TotalMilliseconds / MILLISECONDI_PROVA_PRESTAZIONI <= RAPPORTO_TEMPO_REALE_MASSIMO_FRASI
        Catch ex As Exception
            log.ScriviLog("AssistenteVocale.PrestazioniComandiSufficienti: " & ex.Message & Environment.NewLine & ex.StackTrace)
            Return False
        End Try
    End Function

    Private Sub ProgrammaRilascioComandi()
        Try
            If _timerRilascioComandi Is Nothing Then
                _timerRilascioComandi = New System.Threading.Timer(AddressOf RilascioComandiScaduto, Nothing, MINUTI_RILASCIO_COMANDI * 60000, System.Threading.Timeout.Infinite)
            Else
                _timerRilascioComandi.Change(MINUTI_RILASCIO_COMANDI * 60000, System.Threading.Timeout.Infinite)
            End If
        Catch ex As Exception
            log.ScriviLog("AssistenteVocale.ProgrammaRilascioComandi: " & ex.Message)
        End Try
    End Sub

    Private Sub RilascioComandiScaduto(stato As Object)
        Try
            SyncLock _bloccoComandi
                RilasciaComandiInterno()
            End SyncLock
        Catch ex As Exception
            log.ScriviLog("AssistenteVocale.RilascioComandiScaduto: " & ex.Message)
        End Try
    End Sub

    Private Sub RilasciaComandiInterno()
        Try
            If _riconoscitoreComandi <> IntPtr.Zero Then vosk_recognizer_free(_riconoscitoreComandi)
            _riconoscitoreComandi = IntPtr.Zero
            If _modelloComandi <> IntPtr.Zero Then vosk_model_free(_modelloComandi)
            _modelloComandi = IntPtr.Zero
        Catch ex As Exception
            log.ScriviLog("AssistenteVocale.RilasciaComandiInterno: " & ex.Message & Environment.NewLine & ex.StackTrace)
        End Try
    End Sub

    Private Shared Function ParolaNelVocabolario(modello As IntPtr, parola As String) As Boolean
        Try
            If modello = IntPtr.Zero OrElse String.IsNullOrWhiteSpace(parola) Then Return False
            Return vosk_model_find_word(modello, Encoding.UTF8.GetBytes(parola & ChrW(0))) >= 0
        Catch exFunzione As EntryPointNotFoundException
            log.ScriviLog("AssistenteVocale.ParolaNelVocabolario: la libreria Vosk non espone vosk_model_find_word, " & exFunzione.Message)
            Return True
        Catch ex As Exception
            log.ScriviLog("AssistenteVocale.ParolaNelVocabolario (" & parola & "): " & ex.Message)
            Return True
        End Try
    End Function

    Private Shared Function FiltraVocabolario(modello As IntPtr, parole As String()) As List(Of String)
        Dim presenti As New List(Of String)
        Try
            For Each parola As String In parole
                If ParolaNelVocabolario(modello, parola) AndAlso Not presenti.Contains(parola) Then presenti.Add(parola)
            Next
        Catch ex As Exception
            log.ScriviLog("AssistenteVocale.FiltraVocabolario: " & ex.Message)
        End Try
        Return presenti
    End Function

    Private Shared Function GrammaticaAttivazione(modello As IntPtr) As String
        Try
            Dim nomi As List(Of String) = FiltraVocabolario(modello, NOMI_ATTIVAZIONE)
            Dim prefissi As List(Of String) = FiltraVocabolario(modello, PREFISSI_ATTIVAZIONE)

            If nomi.Count = 0 Then
                log.ScriviLog("AssistenteVocale.GrammaticaAttivazione: il vocabolario del modello vocale non contiene la parola " & String.Join(", ", NOMI_ATTIVAZIONE) & ", la parola di attivazione Ehi Minerva non può essere riconosciuta")
                Return GRAMMATICA_ATTIVAZIONE
            End If

            If prefissi.Count = 0 Then
                log.ScriviLog("AssistenteVocale.GrammaticaAttivazione: il vocabolario del modello vocale non contiene nessun prefisso della parola di attivazione (" & String.Join(", ", PREFISSI_ATTIVAZIONE) & "), la parola di attivazione Ehi Minerva non può essere riconosciuta")
            End If

            Dim frasi As New JArray()
            For Each nome As String In nomi
                frasi.Add(nome)
                For Each prefisso As String In prefissi
                    frasi.Add(prefisso & " " & nome)
                Next
            Next
            frasi.Add("[unk]")
            Return frasi.ToString(Newtonsoft.Json.Formatting.None)
        Catch ex As Exception
            log.ScriviLog("AssistenteVocale.GrammaticaAttivazione: " & ex.Message & Environment.NewLine & ex.StackTrace)
            Return GRAMMATICA_ATTIVAZIONE
        End Try
    End Function

    Private Sub RilasciaLocale()
        Try
            SyncLock _bloccoRiconoscitore
                SyncLock _bloccoComandi
                    _localeDisponibile = False
                    If _timerRilascioComandi IsNot Nothing Then
                        _timerRilascioComandi.Dispose()
                        _timerRilascioComandi = Nothing
                    End If
                    If _riconoscitore <> IntPtr.Zero Then vosk_recognizer_free(_riconoscitore)
                    _riconoscitore = IntPtr.Zero
                    If _riconoscitoreAttivazione <> IntPtr.Zero Then vosk_recognizer_free(_riconoscitoreAttivazione)
                    _riconoscitoreAttivazione = IntPtr.Zero
                    RilasciaComandiInterno()
                    If _modelloAttivazione <> IntPtr.Zero Then vosk_model_free(_modelloAttivazione)
                    _modelloAttivazione = IntPtr.Zero
                    If _preElaborazioneAscolto IsNot Nothing Then _preElaborazioneAscolto.Dispose()
                    _preElaborazioneAscolto = Nothing
                    If _preElaborazioneFrasi IsNot Nothing Then _preElaborazioneFrasi.Dispose()
                    _preElaborazioneFrasi = Nothing
                End SyncLock
            End SyncLock
        Catch ex As Exception
            log.ScriviLog("AssistenteVocale.RilasciaLocale: " & ex.Message & Environment.NewLine & ex.StackTrace)
        End Try
    End Sub

    Private Shared Function AudioDiProva(millisecondi As Integer) As Byte()
        Try
            Dim campioni As Integer = FREQUENZA_CAMPIONAMENTO * millisecondi \ 1000
            Dim risultato(campioni * 2 - 1) As Byte
            Dim casuale As New Random(4242)
            For indice As Integer = 0 To campioni - 1
                Dim tempo As Double = indice / CDbl(FREQUENZA_CAMPIONAMENTO)
                Dim inviluppo As Double = 0.5 + 0.5 * Math.Sin(2.0 * Math.PI * 3.0 * tempo)
                Dim valore As Double = inviluppo * (0.25 * Math.Sin(2.0 * Math.PI * 180.0 * tempo) + 0.12 * Math.Sin(2.0 * Math.PI * 720.0 * tempo) + 0.06 * Math.Sin(2.0 * Math.PI * 2400.0 * tempo)) + 0.03 * (casuale.NextDouble() * 2.0 - 1.0)
                Dim intero As Short = CShort(Math.Max(-32768.0, Math.Min(32767.0, valore * 32767.0)))
                risultato(indice * 2) = CByte(intero And &HFF)
                risultato(indice * 2 + 1) = CByte((intero >> 8) And &HFF)
            Next
            Return risultato
        Catch ex As Exception
            log.ScriviLog("AssistenteVocale.AudioDiProva: " & ex.Message)
            Return New Byte() {}
        End Try
    End Function

    Private Function VerificaPrestazioniLocali(conAttivazione As Boolean, ByRef motivo As String) As Boolean
        Try
            Dim audio As Byte() = AudioDiProva(MILLISECONDI_PROVA_PRESTAZIONI)
            Dim riscaldamento As Byte() = AudioDiProva(250)
            If audio.Length = 0 Then Return True

            Dim cronometro As New Stopwatch()

            If conAttivazione Then
                SyncLock _bloccoRiconoscitore
                    If _arrestato OrElse _riconoscitoreAttivazione = IntPtr.Zero OrElse _rilevatore Is Nothing Then
                        motivo = "l'assistente è stato fermato"
                        Return False
                    End If
                    If _preElaborazioneAscolto IsNot Nothing Then _preElaborazioneAscolto.ElaboraFlusso(riscaldamento, riscaldamento.Length)
                    vosk_recognizer_accept_waveform(_riconoscitoreAttivazione, riscaldamento, riscaldamento.Length)
                    vosk_recognizer_reset(_riconoscitoreAttivazione)

                    cronometro.Restart()
                    Dim elaborato As Byte() = audio
                    If _preElaborazioneAscolto IsNot Nothing Then elaborato = _preElaborazioneAscolto.ElaboraFlusso(audio, audio.Length)
                    Dim posizione As Integer = 0
                    While posizione < elaborato.Length
                        Dim blocco As Integer = Math.Min(BYTE_PER_SECONDO \ 16, elaborato.Length - posizione)
                        Dim parte(blocco - 1) As Byte
                        Array.Copy(elaborato, posizione, parte, 0, blocco)
                        If vosk_recognizer_accept_waveform(_riconoscitoreAttivazione, parte, blocco) = 0 Then vosk_recognizer_partial_result(_riconoscitoreAttivazione)
                        posizione += blocco
                    End While
                    For inizio As Integer = 0 To audio.Length - RilevatoreVoce.CAMPIONI_FINESTRA * 2 Step RilevatoreVoce.CAMPIONI_FINESTRA * 2
                        Dim finestra(RilevatoreVoce.CAMPIONI_FINESTRA - 1) As Single
                        For j As Integer = 0 To RilevatoreVoce.CAMPIONI_FINESTRA - 1
                            finestra(j) = BitConverter.ToInt16(audio, inizio + j * 2) / 32768.0F
                        Next
                        _rilevatore.Probabilita(finestra)
                    Next
                    cronometro.Stop()

                    vosk_recognizer_reset(_riconoscitoreAttivazione)
                    _rilevatore.Azzera()
                    If _preElaborazioneAscolto IsNot Nothing Then _preElaborazioneAscolto.Azzera()
                End SyncLock

                If cronometro.Elapsed.TotalMilliseconds / MILLISECONDI_PROVA_PRESTAZIONI > RAPPORTO_TEMPO_REALE_MASSIMO Then
                    motivo = "il computer non è abbastanza veloce per ascoltare la parola di attivazione con il modello locale"
                    Return False
                End If
            End If

            Return True
        Catch ex As Exception
            log.ScriviLog("AssistenteVocale.VerificaPrestazioniLocali: " & ex.Message & Environment.NewLine & ex.StackTrace)
            motivo = "non è stato possibile misurare la velocità del computer"
            Return False
        End Try
    End Function

    Private Sub AvviaAscoltoServizio()
        Try
            _fermaAscoltoServizio.Reset()
            _threadServizio = New Thread(AddressOf AscoltoServizio)
            _threadServizio.IsBackground = True
            _threadServizio.Start()
        Catch ex As Exception
            log.ScriviLog("AssistenteVocale.AvviaAscoltoServizio: " & ex.Message)
        End Try
    End Sub

    Private Sub AscoltoServizio()
        Dim evento As EventWaitHandle = Nothing
        Try
            evento = New EventWaitHandle(False, EventResetMode.AutoReset, NOME_EVENTO_ATTIVAZIONE)
            Dim attese As WaitHandle() = {evento, _fermaAscoltoServizio}
            While Not _arrestato
                Dim indice As Integer = WaitHandle.WaitAny(attese)
                If indice <> 0 OrElse _arrestato Then Exit While
                PubblicaSuInterfaccia(AddressOf AttivazioneDaServizio, Nothing)
            End While
        Catch ex As Exception
            log.ScriviLog("AssistenteVocale.AscoltoServizio: " & ex.Message & Environment.NewLine & ex.StackTrace)
        Finally
            If evento IsNot Nothing Then evento.Dispose()
        End Try
    End Sub

    Private Sub AttivazioneDaServizio(stato As Object)
        Try
            If _arrestato OrElse _fase = FaseAssistente.Registrazione Then Exit Sub

            Dim accettata As Boolean = False
            If MicrofonoDaSospendere() Then RegistraAttivazioneScartata("microfono sospeso per " & MotivoSospensione(), "")
            If Not MicrofonoDaSospendere() AndAlso DateTime.Now >= _attivazioneBloccataFino Then
                SyncLock _bloccoRiconoscitore
                    If _rilevatore IsNot Nothing Then
                        AvviaFaseRegistrazione(False)
                        accettata = True
                    End If
                End SyncLock
            End If

            If accettata Then
                InizioRegistrazioneSuInterfaccia(Nothing)
            Else
                Task.Run(New Action(AddressOf RiarmaRiconoscimentoServizio))
            End If
        Catch ex As Exception
            log.ScriviLog("AssistenteVocale.AttivazioneDaServizio: " & ex.Message)
        End Try
    End Sub

    Private Sub AvviaMicrofonoSuInterfaccia(stato As Object)
        Try
            If _arrestato Then Exit Sub
            Try
                WGAudio.AttivaMicrofonoPredefinitoSeChiuso()
            Catch exAttivazione As Exception
                log.ScriviLog("AssistenteVocale.AvviaMicrofonoSuInterfaccia (attivazione microfono): " & exAttivazione.Message)
            End Try

            FermaMicrofono()
            _microfonoAvviato = True
            _ultimiDatiMicrofono = DateTime.Now
            _microfono = New NAudio.Wave.WaveInEvent()
            _microfono.DeviceNumber = -1
            _microfono.WaveFormat = New NAudio.Wave.WaveFormat(FREQUENZA_CAMPIONAMENTO, 16, 1)
            _microfono.BufferMilliseconds = 64
            AddHandler _microfono.DataAvailable, AddressOf DatiMicrofono
            AddHandler _microfono.RecordingStopped, AddressOf MicrofonoFermato
            _microfono.StartRecording()
        Catch ex As Exception
            log.ScriviLog("AssistenteVocale.AvviaMicrofonoSuInterfaccia: " & ex.Message & Environment.NewLine & ex.StackTrace)
        End Try
    End Sub

    Private Sub FermaMicrofono()
        Try
            If _microfono Is Nothing Then Exit Sub
            Dim vecchio As NAudio.Wave.WaveInEvent = _microfono
            _microfono = Nothing
            RemoveHandler vecchio.DataAvailable, AddressOf DatiMicrofono
            RemoveHandler vecchio.RecordingStopped, AddressOf MicrofonoFermato
            Try
                vecchio.StopRecording()
            Catch exArresto As Exception
                log.ScriviLog("AssistenteVocale.FermaMicrofono (arresto): " & exArresto.Message)
            End Try
            vecchio.Dispose()
        Catch ex As Exception
            log.ScriviLog("AssistenteVocale.FermaMicrofono: " & ex.Message)
        End Try
    End Sub

    Private Sub RiavviaMicrofono()
        Try
            If _arrestato OrElse Not _microfonoAvviato Then Exit Sub
            If (DateTime.Now - _ultimoRiavvioMicrofono).TotalSeconds < SECONDI_MINIMI_TRA_RIAVVII_MICROFONO Then Exit Sub
            _ultimoRiavvioMicrofono = DateTime.Now
            SyncLock _bloccoRiconoscitore
                _azzeramentoRichiesto = True
            End SyncLock
            AvviaMicrofonoSuInterfaccia(Nothing)
        Catch ex As Exception
            log.ScriviLog("AssistenteVocale.RiavviaMicrofono: " & ex.Message & Environment.NewLine & ex.StackTrace)
        End Try
    End Sub

    Private Sub RiavviaMicrofonoForzato()
        Try
            If _arrestato OrElse Not _microfonoAvviato Then Exit Sub
            _ultimoRiavvioMicrofono = DateTime.Now
            SyncLock _bloccoRiconoscitore
                _azzeramentoRichiesto = True
            End SyncLock
            AvviaMicrofonoSuInterfaccia(Nothing)
        Catch ex As Exception
            log.ScriviLog("AssistenteVocale.RiavviaMicrofonoForzato: " & ex.Message & Environment.NewLine & ex.StackTrace)
        End Try
    End Sub

    Private Function DescriviDispositiviMicrofono() As String
        Try
            Dim numero As Integer = NAudio.Wave.WaveInEvent.DeviceCount
            If numero <= 0 Then Return "nessun dispositivo di registrazione trovato"
            Dim nomi As New List(Of String)
            For indice As Integer = 0 To numero - 1
                Try
                    nomi.Add(NAudio.Wave.WaveInEvent.GetCapabilities(indice).ProductName)
                Catch exDispositivo As Exception
                    log.ScriviLog("AssistenteVocale.DescriviDispositiviMicrofono (dispositivo " & indice.ToString(System.Globalization.CultureInfo.InvariantCulture) & "): " & exDispositivo.Message)
                End Try
            Next
            Return "dispositivi di registrazione: " & String.Join(", ", nomi)
        Catch ex As Exception
            log.ScriviLog("AssistenteVocale.DescriviDispositiviMicrofono: " & ex.Message & Environment.NewLine & ex.StackTrace)
            Return ""
        End Try
    End Function

    Private Sub ControllaDatiMicrofonoInRegistrazione(adesso As DateTime)
        Try
            If _fase <> FaseAssistente.Registrazione OrElse Not _microfonoAvviato Then Exit Sub
            If (adesso - _ultimiDatiMicrofono).TotalMilliseconds < MILLISECONDI_SENZA_DATI_REGISTRAZIONE Then Exit Sub
            If (adesso - _ultimoRiavvioMicrofono).TotalMilliseconds < MILLISECONDI_SENZA_DATI_REGISTRAZIONE Then Exit Sub

            If _riavviiMicrofonoSessione >= RIAVVII_MASSIMI_MICROFONO_SESSIONE Then
                log.ScriviLog("AssistenteVocale.ControllaDatiMicrofonoInRegistrazione: il microfono non invia audio durante l'ascolto della richiesta nemmeno dopo " & _riavviiMicrofonoSessione.ToString(System.Globalization.CultureInfo.InvariantCulture) & " riavvii, sessione chiusa, " & DescriviDispositiviMicrofono())
                TornaFaseAscolto(True)
                Parla("Il microfono non invia audio. Ripetere la parola di attivazione.")
                Exit Sub
            End If

            _riavviiMicrofonoSessione += 1
            log.ScriviLog("AssistenteVocale.ControllaDatiMicrofonoInRegistrazione: nessun audio dal microfono da " & CInt((adesso - _ultimiDatiMicrofono).TotalMilliseconds).ToString(System.Globalization.CultureInfo.InvariantCulture) & " millisecondi durante l'ascolto della richiesta, riavvio " & _riavviiMicrofonoSessione.ToString(System.Globalization.CultureInfo.InvariantCulture) & " di " & RIAVVII_MASSIMI_MICROFONO_SESSIONE.ToString(System.Globalization.CultureInfo.InvariantCulture) & ", blocchi arrivati " & Interlocked.CompareExchange(_chiamateMicrofonoSessione, 0, 0).ToString(System.Globalization.CultureInfo.InvariantCulture) & ", blocchi elaborati " & _bufferRicevutiSessione.ToString(System.Globalization.CultureInfo.InvariantCulture))
            RiavviaMicrofonoForzato()
            _ultimaVoce = DateTime.Now
        Catch ex As Exception
            log.ScriviLog("AssistenteVocale.ControllaDatiMicrofonoInRegistrazione: " & ex.Message & Environment.NewLine & ex.StackTrace)
        End Try
    End Sub

    Private Sub RiavviaMicrofonoSuInterfaccia(stato As Object)
        Try
            RiavviaMicrofono()
        Catch ex As Exception
            log.ScriviLog("AssistenteVocale.RiavviaMicrofonoSuInterfaccia: " & ex.Message)
        End Try
    End Sub

    Private Sub MicrofonoFermato(sender As Object, e As NAudio.Wave.StoppedEventArgs)
        Try
            If e IsNot Nothing AndAlso e.Exception IsNot Nothing Then
                log.ScriviLog("AssistenteVocale.MicrofonoFermato: " & e.Exception.Message)
            End If
            If Not _arrestato Then PubblicaSuInterfaccia(AddressOf RiavviaMicrofonoSuInterfaccia, Nothing)
        Catch ex As Exception
            log.ScriviLog("AssistenteVocale.MicrofonoFermato: " & ex.Message)
        End Try
    End Sub

    Private Sub DatiMicrofono(sender As Object, e As NAudio.Wave.WaveInEventArgs)
        Try
            If e Is Nothing OrElse e.BytesRecorded <= 0 Then Exit Sub
            _ultimiDatiMicrofono = DateTime.Now
            If _fase = FaseAssistente.Registrazione Then Interlocked.Increment(_chiamateMicrofonoSessione)

            Dim attivazioneRilevata As Boolean = False
            Dim frasi As New List(Of RichiestaVocale)
            Dim voceRilevata As Boolean = False

            SyncLock _bloccoRiconoscitore
                If _arrestato OrElse _rilevatore Is Nothing Then Exit Sub
                If _fase = FaseAssistente.Registrazione Then _bufferRicevutiSessione += 1
                If _microfonoSospeso Then
                    If _fase = FaseAssistente.Registrazione Then _bufferSospesiSessione += 1
                    Exit Sub
                End If

                Dim audio As Byte() = AmplificaAudio(e.Buffer, e.BytesRecorded)
                If audio.Length = 0 Then Exit Sub
                Dim originale As Byte() = CopiaNonAmplificata(e.Buffer, audio.Length)
                _bufferGrezzo.Accoda(audio, audio.Length)
                _bufferNonAmplificato.Accoda(originale, originale.Length)

                If _azzeramentoRichiesto Then
                    _azzeramentoRichiesto = False
                    AzzeraSegmentazione()
                    If _riconoscitoreAttivazione <> IntPtr.Zero Then vosk_recognizer_reset(_riconoscitoreAttivazione)
                    If _preElaborazioneAscolto IsNot Nothing Then _preElaborazioneAscolto.Azzera()
                    If _rilevatoreParola IsNot Nothing Then _rilevatoreParola.Azzera()
                    _bufferElaborato.Azzera()
                End If

                If _fase = FaseAssistente.Registrazione Then
                    If DateTime.Now >= _ignoraAudioFino Then voceRilevata = SegmentaAudio(audio, originale, audio.Length, frasi)
                ElseIf _modo = ModoAttivazione.Jarvis AndAlso _rilevatoreParola IsNot Nothing Then
                    attivazioneRilevata = ControllaAttivazioneJarvis(audio, audio.Length)
                ElseIf _modo = ModoAttivazione.Parakeet Then
                    Dim segmenti As New List(Of RichiestaVocale)
                    SegmentaAudio(audio, originale, audio.Length, segmenti)
                    For Each segmento As RichiestaVocale In segmenti
                        AvviaVerificaParakeet(segmento.Audio)
                    Next
                ElseIf _modo = ModoAttivazione.Vosk AndAlso _riconoscitoreAttivazione <> IntPtr.Zero Then
                    Dim elaborato As Byte()
                    If _preElaborazioneAscolto IsNot Nothing Then
                        elaborato = _preElaborazioneAscolto.ElaboraFlusso(originale, originale.Length)
                    Else
                        elaborato = New Byte(audio.Length - 1) {}
                        Array.Copy(audio, elaborato, audio.Length)
                    End If
                    If elaborato IsNot Nothing AndAlso elaborato.Length > 0 Then
                        _bufferElaborato.Accoda(elaborato, elaborato.Length)
                        attivazioneRilevata = ControllaAttivazioneVosk(elaborato, elaborato.Length)
                    End If
                End If
            End SyncLock

            If voceRilevata Then _ultimaVoce = DateTime.Now
            If attivazioneRilevata Then PubblicaSuInterfaccia(AddressOf InizioRegistrazioneSuInterfaccia, Nothing)
            For Each frase As RichiestaVocale In frasi
                PubblicaSuInterfaccia(AddressOf FraseSuInterfaccia, frase)
            Next
        Catch ex As Exception
            log.ScriviLog("AssistenteVocale.DatiMicrofono: " & ex.Message & Environment.NewLine & ex.StackTrace)
        End Try
    End Sub

    Private Sub AvviaVerificaParakeet(audio As Byte(), Optional fineSegmento As DateTime = Nothing)
        Try
            If audio Is Nothing OrElse audio.Length < BYTE_PER_SECONDO \ 3 OrElse audio.Length > BYTE_PER_SECONDO * SECONDI_MASSIMI_FRASE_ATTIVAZIONE Then Exit Sub
            If DateTime.Now < _attivazioneBloccataFino Then Exit Sub
            If fineSegmento = DateTime.MinValue Then fineSegmento = DateTime.Now
            If Interlocked.CompareExchange(_verificaParakeetInCorso, 1, 0) <> 0 Then
                SyncLock _bloccoParakeet
                    _segmentoParakeetInAttesa = audio
                    _segmentoParakeetInAttesaDal = fineSegmento
                End SyncLock
                Exit Sub
            End If
            Task.Factory.StartNew(New Action(Of Object)(AddressOf LavoroVerificaParakeet), New Tuple(Of Byte(), DateTime)(audio, fineSegmento))
        Catch ex As Exception
            Interlocked.Exchange(_verificaParakeetInCorso, 0)
            log.ScriviLog("AssistenteVocale.AvviaVerificaParakeet: " & ex.Message & Environment.NewLine & ex.StackTrace)
        End Try
    End Sub

    Private NotInheritable Class DatiAttivazioneParakeet
        Public Property Resto As String = ""
        Public Property FineSegmento As DateTime = DateTime.MinValue
        Public Property AudioRichiesta As Byte() = Nothing
    End Class

    Private Function TagliaDopoAttivazione(audio As Byte(), paroleTempi As List(Of Tuple(Of String, Single))) As Byte()
        Try
            If audio Is Nothing OrElse paroleTempi Is Nothing OrElse paroleTempi.Count = 0 Then Return Nothing
            Dim normali As New List(Of String)
            Dim tempi As New List(Of Single)
            For Each voce As Tuple(Of String, Single) In paroleTempi
                For Each parola As String In ParoleDaTesto(Regex.Replace(If(voce.Item1, ""), "[^\p{L}\s']", " "))
                    normali.Add(parola)
                    tempi.Add(voce.Item2)
                Next
            Next
            Dim indice As Integer = IndiceParolaAttivazione(normali, MODELLO_PREFISSO_ATTIVAZIONE_LIBERO)
            If indice < 0 OrElse indice + 1 >= normali.Count Then Return Nothing
            Dim secondi As Double = Math.Max(0.0, tempi(indice + 1) - 0.05)
            Dim inizio As Integer = CInt(secondi * BYTE_PER_SECONDO)
            inizio -= inizio Mod 2
            If inizio <= 0 OrElse audio.Length - inizio < BYTE_PER_SECONDO \ 3 Then Return Nothing
            Dim taglio(audio.Length - inizio - 1) As Byte
            Array.Copy(audio, inizio, taglio, 0, taglio.Length)
            Return taglio
        Catch ex As Exception
            log.ScriviLog("AssistenteVocale.TagliaDopoAttivazione: " & ex.Message & Environment.NewLine & ex.StackTrace)
            Return Nothing
        End Try
    End Function

    Private Sub LavoroVerificaParakeet(stato As Object)
        Try
            Dim dati As Tuple(Of Byte(), DateTime) = TryCast(stato, Tuple(Of Byte(), DateTime))
            If dati Is Nothing OrElse dati.Item1 Is Nothing OrElse _arrestato Then Exit Sub
            Dim audio As Byte() = dati.Item1
            Dim errore As String = ""
            Dim paroleTempi As List(Of Tuple(Of String, Single)) = Nothing
            Dim testo As String = WGTrascrittoreLocale.TrascriviPcm16ConTempi(PreElaborazioneVoce.PreparaPerParakeet(audio), FREQUENZA_CAMPIONAMENTO, errore, paroleTempi)
            If Not String.IsNullOrEmpty(errore) Then log.ScriviLog("AssistenteVocale.LavoroVerificaParakeet: " & errore)
            If String.IsNullOrWhiteSpace(testo) OrElse _arrestato Then Exit Sub

            Dim pulito As String = Regex.Replace(testo, "[^\p{L}\s']", " ")
            Dim parole As List(Of String) = ParoleDaTesto(pulito)
            Dim indiceAttivazione As Integer = IndiceParolaAttivazione(parole, MODELLO_PREFISSO_ATTIVAZIONE_LIBERO)
            If indiceAttivazione >= 0 Then
                SyncLock _bloccoParakeet
                    _segmentoParakeetInAttesa = Nothing
                End SyncLock
                Dim resto As String = ""
                If indiceAttivazione + 1 < parole.Count Then resto = String.Join(" ", parole.GetRange(indiceAttivazione + 1, parole.Count - indiceAttivazione - 1)).Trim()
                Dim datiAttivazione As New DatiAttivazioneParakeet With {.Resto = resto, .FineSegmento = dati.Item2}
                If resto <> "" Then
                    datiAttivazione.AudioRichiesta = TagliaDopoAttivazione(audio, paroleTempi)
                    If datiAttivazione.AudioRichiesta Is Nothing Then log.ScriviLog("AssistenteVocale.LavoroVerificaParakeet: non è stato possibile separare la richiesta dalla parola di attivazione, la richiesta viene inviata come testo trascritto sul computer")
                End If
                PubblicaSuInterfaccia(AddressOf AttivazioneParakeetSuInterfaccia, datiAttivazione)
            ElseIf Regex.IsMatch(pulito, MODELLO_PAROLA_ATTIVAZIONE, RegexOptions.IgnoreCase) Then
                RegistraAttivazioneScartata("il nome si trova dentro una frase", testo)
            End If
        Catch ex As Exception
            log.ScriviLog("AssistenteVocale.LavoroVerificaParakeet: " & ex.Message & Environment.NewLine & ex.StackTrace)
        Finally
            Interlocked.Exchange(_verificaParakeetInCorso, 0)
            AvviaSegmentoParakeetInAttesa()
        End Try
    End Sub

    Private Sub AvviaSegmentoParakeetInAttesa()
        Try
            Dim prossimo As Byte() = Nothing
            Dim fineProssimo As DateTime = DateTime.MinValue
            SyncLock _bloccoParakeet
                If _segmentoParakeetInAttesa IsNot Nothing AndAlso (DateTime.Now - _segmentoParakeetInAttesaDal).TotalMilliseconds <= MILLISECONDI_VALIDITA_SEGMENTO_PARAKEET Then
                    prossimo = _segmentoParakeetInAttesa
                    fineProssimo = _segmentoParakeetInAttesaDal
                End If
                _segmentoParakeetInAttesa = Nothing
            End SyncLock
            If prossimo Is Nothing OrElse _arrestato OrElse _fase = FaseAssistente.Registrazione Then Exit Sub
            AvviaVerificaParakeet(prossimo, fineProssimo)
        Catch ex As Exception
            log.ScriviLog("AssistenteVocale.AvviaSegmentoParakeetInAttesa: " & ex.Message & Environment.NewLine & ex.StackTrace)
        End Try
    End Sub

    Private Sub AttivazioneParakeetSuInterfaccia(stato As Object)
        Try
            If _arrestato OrElse _fase = FaseAssistente.Registrazione Then Exit Sub
            If MicrofonoDaSospendere() Then
                RegistraAttivazioneScartata("microfono sospeso per " & MotivoSospensione(), "")
                Exit Sub
            End If
            If DateTime.Now < _attivazioneBloccataFino Then Exit Sub
            Dim dati As DatiAttivazioneParakeet = TryCast(stato, DatiAttivazioneParakeet)
            Dim resto As String = If(dati IsNot Nothing, If(dati.Resto, "").Trim(), "")
            Dim byteAnticipo As Integer = 0
            If dati IsNot Nothing AndAlso dati.FineSegmento > DateTime.MinValue Then
                Dim trascorsi As Double = Math.Max(0.0, (DateTime.Now - dati.FineSegmento).TotalMilliseconds)
                byteAnticipo = CInt(Math.Min(CDbl(BYTE_BUFFER_CIRCOLARE), trascorsi * BYTE_PER_SECONDO / 1000.0))
                byteAnticipo -= byteAnticipo Mod 2
            End If
            Dim accettata As Boolean = False
            SyncLock _bloccoRiconoscitore
                If _rilevatore IsNot Nothing Then
                    If resto = "" AndAlso byteAnticipo >= BYTE_MINIMI_ANTICIPO_PARAKEET Then
                        AvviaFaseRegistrazione(True, byteAnticipo)
                    Else
                        AvviaFaseRegistrazione(False)
                    End If
                    accettata = True
                End If
            End SyncLock
            If Not accettata Then Exit Sub
            InizioRegistrazioneSuInterfaccia(Nothing)
            If resto <> "" AndAlso _fase = FaseAssistente.Registrazione Then
                Dim audioRichiesta As Byte() = If(dati IsNot Nothing, dati.AudioRichiesta, Nothing)
                Dim richiesta As New RichiestaVocale With {.Testo = If(audioRichiesta IsNot Nothing, "", resto), .Audio = audioRichiesta, .Sessione = _numeroSessione, .PrimaFrase = True}
                _frasiNellaSessione += 1
                AvviaElaborazioneEsterna(richiesta)
            End If
        Catch ex As Exception
            log.ScriviLog("AssistenteVocale.AttivazioneParakeetSuInterfaccia: " & ex.Message & Environment.NewLine & ex.StackTrace)
        End Try
    End Sub

    Private Function ControllaAttivazioneVosk(buffer As Byte(), lunghezza As Integer) As Boolean
        Try
            Dim modelloPrefisso As String = If(_qualitaAttivazione = QualitaVosk.Alta, MODELLO_PREFISSO_ATTIVAZIONE_LIBERO, MODELLO_PREFISSO_ATTIVAZIONE)
            Dim risultatoAttivazione As String
            If vosk_recognizer_accept_waveform(_riconoscitoreAttivazione, buffer, lunghezza) <> 0 Then
                _confermeAttivazione = 0
                risultatoAttivazione = LeggiStringaUtf8(vosk_recognizer_result(_riconoscitoreAttivazione))
                If Not Regex.IsMatch(LeggiCampoJson(risultatoAttivazione, "text"), MODELLO_PAROLA_ATTIVAZIONE, RegexOptions.IgnoreCase) Then Return False
            Else
                Dim parziale As String = LeggiCampoJson(LeggiStringaUtf8(vosk_recognizer_partial_result(_riconoscitoreAttivazione)), "partial")
                If IndiceParolaAttivazione(ParoleDaTesto(parziale), modelloPrefisso) < 0 Then
                    _confermeAttivazione = 0
                    Return False
                End If
                _confermeAttivazione += 1
                If _confermeAttivazione < CONFERME_ATTIVAZIONE Then Return False
                _confermeAttivazione = 0
                risultatoAttivazione = LeggiStringaUtf8(vosk_recognizer_final_result(_riconoscitoreAttivazione))
            End If
            vosk_recognizer_reset(_riconoscitoreAttivazione)

            Dim soglia As Double = If(_qualitaAttivazione = QualitaVosk.Alta, SOGLIA_CONFIDENZA_ATTIVAZIONE_LIBERA, SOGLIA_CONFIDENZA_ATTIVAZIONE)
            Dim confidenza As Double = ConfidenzaAttivazione(risultatoAttivazione, modelloPrefisso)
            If confidenza < 0 Then
                If Not String.IsNullOrWhiteSpace(LeggiCampoJson(risultatoAttivazione, "text")) Then RegistraAttivazioneScartata("il nome si trova dentro una frase", risultatoAttivazione)
                Return False
            End If
            If confidenza < soglia Then
                RegistraAttivazioneScartata("confidenza " & confidenza.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture) & " inferiore alla soglia " & soglia.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture), risultatoAttivazione)
                Return False
            End If
            If DateTime.Now < _attivazioneBloccataFino Then Return False
            If Not LivelloVoceSufficiente() Then
                RegistraAttivazioneScartata("livello del microfono sotto la soglia minima", risultatoAttivazione)
                Return False
            End If
            If confidenza < SOGLIA_CONFIDENZA_SENZA_VERIFICA AndAlso Not VerificaAttivazioneVosk() Then
                RegistraAttivazioneScartata("il riconoscimento di controllo non conferma il nome", risultatoAttivazione)
                Return False
            End If

            AvviaFaseRegistrazione(False)
            Return True
        Catch ex As Exception
            log.ScriviLog("AssistenteVocale.ControllaAttivazioneVosk: " & ex.Message)
            Return False
        End Try
    End Function

    Private Function ControllaAttivazioneJarvis(buffer As Byte(), lunghezza As Integer) As Boolean
        Try
            Dim soglia As Single = _sogliaJarvis
            Dim punteggio As Single = _rilevatoreParola.Elabora(buffer, lunghezza, soglia)
            If punteggio < SOGLIA_JARVIS_BASSA AndAlso punteggio >= soglia AndAlso _rilevatoreParola.ConsecutiviSopraSoglia < _confermeJarvis Then Return False
            If punteggio < soglia Then
                If punteggio >= Math.Max(0.1F, soglia - MARGINE_LOG_JARVIS) AndAlso DateTime.Now >= _attivazioneBloccataFino Then
                    RegistraAttivazioneScartata("punteggio sotto la soglia " & soglia.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture), "punteggio " & punteggio.ToString("0.000", System.Globalization.CultureInfo.InvariantCulture))
                End If
                Return False
            End If
            If DateTime.Now < _attivazioneBloccataFino Then Return False
            If Not LivelloVoceSufficiente() Then
                RegistraAttivazioneScartata("livello del microfono sotto la soglia minima", "punteggio " & punteggio.ToString("0.000", System.Globalization.CultureInfo.InvariantCulture))
                Return False
            End If

            AvviaFaseRegistrazione(False)
            Return True
        Catch ex As Exception
            log.ScriviLog("AssistenteVocale.ControllaAttivazioneJarvis: " & ex.Message & Environment.NewLine & ex.StackTrace)
            Return False
        End Try
    End Function

    Private Sub RegistraAttivazioneScartata(motivo As String, risultato As String)
        Try
            If (DateTime.Now - _ultimoLogAttivazione).TotalSeconds < SECONDI_MINIMI_TRA_LOG_ATTIVAZIONE Then Exit Sub
            _ultimoLogAttivazione = DateTime.Now
            Dim testo As String = If(risultato, "").Replace(Environment.NewLine, " ").Replace(vbLf, " ").Replace(vbCr, " ")
            Dim motore As String = "Vosk in " & DescrizioneQualita(_qualitaAttivazione)
            If _modo = ModoAttivazione.Jarvis Then motore = "openWakeWord"
            If _modo = ModoAttivazione.Parakeet Then motore = "Parakeet"
            log.ScriviLog("AssistenteVocale.ControllaAttivazione: attivazione vocale " & DescrizioneParola(If(_modo = ModoAttivazione.Jarvis, ParolaAttivazione.HeyJarvis, ParolaAttivazione.EhiMinerva)) & " scartata, " & motivo & ", motore " & motore & ", riconosciuto: " & testo)
        Catch ex As Exception
            log.ScriviLog("AssistenteVocale.RegistraAttivazioneScartata: " & ex.Message)
        End Try
    End Sub

    Private Shared Function ParoleDaTesto(testo As String) As List(Of String)
        Dim parole As New List(Of String)
        Try
            If String.IsNullOrWhiteSpace(testo) Then Return parole
            For Each parola As String In testo.Split(New Char() {" "c, ControlChars.Tab, ControlChars.Cr, ControlChars.Lf}, StringSplitOptions.RemoveEmptyEntries)
                parole.Add(parola.Trim().ToLowerInvariant())
            Next
        Catch ex As Exception
            log.ScriviLog("AssistenteVocale.ParoleDaTesto: " & ex.Message)
        End Try
        Return parole
    End Function

    Private Shared ReadOnly _chiaveMinerva As String = ChiaveMinerva("minerva")

    Private Shared Function ChiaveMinerva(testo As String) As String
        Try
            Dim scomposto As String = If(testo, "").ToLowerInvariant().Normalize(NormalizationForm.FormD)
            Dim lettere As New StringBuilder()
            For Each carattere As Char In scomposto
                If System.Globalization.CharUnicodeInfo.GetUnicodeCategory(carattere) = System.Globalization.UnicodeCategory.NonSpacingMark Then Continue For
                If carattere >= "a"c AndAlso carattere <= "z"c Then lettere.Append(carattere)
            Next
            Dim chiave As String = lettere.ToString().Replace("r", "").Replace("h", "").Replace("b", "v").Replace("w", "v")
            Return Regex.Replace(chiave, "(.)\1+", "$1")
        Catch ex As Exception
            log.ScriviLog("AssistenteVocale.ChiaveMinerva: " & ex.Message)
            Return ""
        End Try
    End Function

    Private Shared Function SembraMinerva(parola As String) As Boolean
        Try
            Dim chiave As String = ChiaveMinerva(parola)
            If chiave.Length < 4 OrElse chiave.Length > 8 Then Return False
            Return SomiglianzaTesti(chiave, _chiaveMinerva) >= 0.8
        Catch ex As Exception
            log.ScriviLog("AssistenteVocale.SembraMinerva: " & ex.Message)
            Return False
        End Try
    End Function

    Private Shared Function IndiceParolaAttivazione(parole As List(Of String), modelloPrefisso As String) As Integer
        Try
            If parole Is Nothing OrElse parole.Count = 0 Then Return -1
            For indice As Integer = 0 To parole.Count - 1
                Dim parola As String = If(parole(indice), "")
                Dim fine As Integer = indice
                If Not SembraMinerva(parola) Then
                    If indice + 1 < parole.Count AndAlso SembraMinerva(parola & If(parole(indice + 1), "")) Then
                        fine = indice + 1
                    Else
                        Continue For
                    End If
                End If
                If indice = 0 Then Return fine
                If indice <> 1 Then Return -1
                Dim primaParola As String = If(parole(0), "")
                If Regex.IsMatch(primaParola, modelloPrefisso, RegexOptions.IgnoreCase) OrElse Regex.IsMatch(primaParola, MODELLO_PAROLA_BREVE_INIZIALE, RegexOptions.IgnoreCase) Then Return fine
                Return -1
            Next
            Return -1
        Catch ex As Exception
            log.ScriviLog("AssistenteVocale.IndiceParolaAttivazione: " & ex.Message)
            Return -1
        End Try
    End Function

    Private Shared Function ConfidenzaAttivazione(json As String, modelloPrefisso As String) As Double
        Try
            Dim parole As JArray = ParoleRiconosciute(json)
            If parole Is Nothing Then Return -1.0

            Dim testi As New List(Of String)
            For Each elemento As JToken In parole
                Dim parola As JObject = TryCast(elemento, JObject)
                testi.Add(If(parola Is Nothing, "", TestoParola(parola).ToLowerInvariant()))
            Next

            Dim indice As Integer = IndiceParolaAttivazione(testi, modelloPrefisso)
            If indice < 0 Then Return -1.0

            Dim nome As JObject = TryCast(parole(indice), JObject)
            If nome Is Nothing Then Return -1.0
            Return ConfidenzaParola(nome)
        Catch ex As Exception
            log.ScriviLog("AssistenteVocale.ConfidenzaAttivazione: " & ex.Message)
            Return -1.0
        End Try
    End Function

    Private Shared Function ConfidenzaVerifica(json As String) As Double
        Try
            Dim parole As JArray = ParoleRiconosciute(json)
            If parole Is Nothing Then Return -1.0
            Dim migliore As Double = -1.0
            For Each elemento As JToken In parole
                Dim parola As JObject = TryCast(elemento, JObject)
                If parola Is Nothing Then Continue For
                If Not Regex.IsMatch(TestoParola(parola), MODELLO_VERIFICA_ATTIVAZIONE, RegexOptions.IgnoreCase) Then Continue For
                Dim confidenza As Double = ConfidenzaParola(parola)
                If confidenza > migliore Then migliore = confidenza
            Next
            Return migliore
        Catch ex As Exception
            log.ScriviLog("AssistenteVocale.ConfidenzaVerifica: " & ex.Message)
            Return -1.0
        End Try
    End Function

    Private Shared Function ParoleRiconosciute(json As String) As JArray
        Try
            If String.IsNullOrWhiteSpace(json) Then Return Nothing
            Dim oggetto As JObject = JObject.Parse(json)
            Dim parole As JArray = TryCast(oggetto("result"), JArray)
            If parole Is Nothing OrElse parole.Count = 0 Then Return Nothing
            Return parole
        Catch ex As Exception
            log.ScriviLog("AssistenteVocale.ParoleRiconosciute: " & ex.Message)
            Return Nothing
        End Try
    End Function

    Private Shared Function TestoParola(parola As JObject) As String
        Try
            Dim valore As JToken = parola("word")
            If valore Is Nothing OrElse valore.Type = JTokenType.Null Then Return ""
            Return Convert.ToString(valore, System.Globalization.CultureInfo.InvariantCulture).Trim()
        Catch ex As Exception
            log.ScriviLog("AssistenteVocale.TestoParola: " & ex.Message)
            Return ""
        End Try
    End Function

    Private Shared Function ConfidenzaParola(parola As JObject) As Double
        Try
            Dim valore As JToken = parola("conf")
            If valore Is Nothing OrElse valore.Type = JTokenType.Null Then Return 0.0
            If valore.Type = JTokenType.Float OrElse valore.Type = JTokenType.Integer Then Return valore.ToObject(Of Double)()
            Dim numero As Double = 0.0
            Double.TryParse(Convert.ToString(valore, System.Globalization.CultureInfo.InvariantCulture), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, numero)
            Return numero
        Catch ex As Exception
            log.ScriviLog("AssistenteVocale.ConfidenzaParola: " & ex.Message)
            Return 0.0
        End Try
    End Function

    Private Function VerificaAttivazioneVosk() As Boolean
        Try
            If _riconoscitore = IntPtr.Zero Then Return True
            Dim anticipo As Byte() = _bufferElaborato.Ultimi(BYTE_ANTICIPO_VERIFICA)
            If anticipo.Length = 0 Then Return False
            vosk_recognizer_reset(_riconoscitore)
            Dim confidenza As Double = -1.0
            If vosk_recognizer_accept_waveform(_riconoscitore, anticipo, anticipo.Length) <> 0 Then
                confidenza = ConfidenzaVerifica(LeggiStringaUtf8(vosk_recognizer_result(_riconoscitore)))
            End If
            confidenza = Math.Max(confidenza, ConfidenzaVerifica(LeggiStringaUtf8(vosk_recognizer_final_result(_riconoscitore))))
            vosk_recognizer_reset(_riconoscitore)
            Return confidenza >= SOGLIA_CONFIDENZA_VERIFICA
        Catch ex As Exception
            log.ScriviLog("AssistenteVocale.VerificaAttivazioneVosk: " & ex.Message)
            Return False
        End Try
    End Function

    Private Function AmplificaAudio(buffer As Byte(), lunghezza As Integer) As Byte()
        Try
            Dim campioni As Integer = lunghezza \ 2
            Dim risultato(campioni * 2 - 1) As Byte
            If campioni <= 0 Then Return New Byte() {}

            Dim somma As Double = 0.0
            For indice As Integer = 0 To campioni - 1
                Dim valore As Double = BitConverter.ToInt16(buffer, indice * 2)
                somma += valore * valore
            Next
            Dim livello As Double = Math.Sqrt(somma / campioni)
            If _fase = FaseAssistente.Registrazione AndAlso livello > _livelloMassimo Then _livelloMassimo = livello

            If livello > LIVELLO_RUMORE Then
                Dim obiettivo As Double = Math.Max(1.0, Math.Min(GUADAGNO_MASSIMO, LIVELLO_OBIETTIVO / livello))
                If obiettivo < _guadagnoCorrente Then
                    _guadagnoCorrente = obiettivo
                Else
                    _guadagnoCorrente += (obiettivo - _guadagnoCorrente) * RISALITA_GUADAGNO
                End If
            End If

            For indice As Integer = 0 To campioni - 1
                Dim valore As Double = BitConverter.ToInt16(buffer, indice * 2) * _guadagnoCorrente
                Dim limitato As Short = CShort(Math.Max(-32768.0, Math.Min(32767.0, Math.Round(valore))))
                risultato(indice * 2) = CByte(limitato And &HFF)
                risultato(indice * 2 + 1) = CByte((limitato >> 8) And &HFF)
            Next
            Return risultato
        Catch ex As Exception
            log.ScriviLog("AssistenteVocale.AmplificaAudio: " & ex.Message)
            Dim copia(Math.Max(0, lunghezza) - 1) As Byte
            If lunghezza > 0 Then Array.Copy(buffer, copia, lunghezza)
            Return copia
        End Try
    End Function

    Private Shared Function CopiaNonAmplificata(buffer As Byte(), lunghezza As Integer) As Byte()
        Try
            If buffer Is Nothing OrElse lunghezza <= 0 Then Return New Byte() {}
            Dim quanti As Integer = Math.Min(lunghezza, buffer.Length)
            quanti -= quanti Mod 2
            If quanti <= 0 Then Return New Byte() {}
            Dim copia(quanti - 1) As Byte
            Array.Copy(buffer, copia, quanti)
            Return copia
        Catch ex As Exception
            log.ScriviLog("AssistenteVocale.CopiaNonAmplificata: " & ex.Message & Environment.NewLine & ex.StackTrace)
            Return New Byte() {}
        End Try
    End Function

    Private Shared Function DescriviLivello(livello As Double) As String
        Try
            If livello <= 0.0 Then Return "nullo"
            Return (20.0 * Math.Log10(livello / 32768.0)).ToString("0", System.Globalization.CultureInfo.InvariantCulture) & " dBFS"
        Catch ex As Exception
            log.ScriviLog("AssistenteVocale.DescriviLivello: " & ex.Message)
            Return ""
        End Try
    End Function

    Private Sub AzzeraSegmentazione()
        Try
            _campioniVad.Clear()
            _byteOriginaliVad.Clear()
            _anticipoFrase.Clear()
            If _audioFrase IsNot Nothing Then _audioFrase.Dispose()
            _audioFrase = Nothing
            _inParlato = False
            _finestreVoce = 0
            _msSilenzio = 0
            _msParlato = 0
            If _rilevatore IsNot Nothing Then _rilevatore.Azzera()
        Catch ex As Exception
            log.ScriviLog("AssistenteVocale.AzzeraSegmentazione: " & ex.Message)
        End Try
    End Sub

    Private Function SegmentaAudio(buffer As Byte(), originale As Byte(), lunghezza As Integer, frasi As List(Of RichiestaVocale)) As Boolean
        Dim voceRilevata As Boolean = False
        Try
            If buffer Is Nothing OrElse lunghezza <= 1 Then Return False
            Dim sorgenteFrase As Byte() = If(originale IsNot Nothing AndAlso originale.Length >= lunghezza - (lunghezza Mod 2), originale, buffer)
            If _byteOriginaliVad.Count <> _campioniVad.Count * 2 Then
                log.ScriviLog("AssistenteVocale.SegmentaAudio: code audio disallineate, " & _campioniVad.Count.ToString(System.Globalization.CultureInfo.InvariantCulture) & " campioni e " & _byteOriginaliVad.Count.ToString(System.Globalization.CultureInfo.InvariantCulture) & " byte originali")
                _campioniVad.Clear()
                _byteOriginaliVad.Clear()
            End If

            For i As Integer = 0 To lunghezza - 2 Step 2
                _campioniVad.Add(BitConverter.ToInt16(buffer, i) / 32768.0F)
                _byteOriginaliVad.Add(sorgenteFrase(i))
                _byteOriginaliVad.Add(sorgenteFrase(i + 1))
            Next

            While _campioniVad.Count >= RilevatoreVoce.CAMPIONI_FINESTRA
                Dim finestra As Single() = _campioniVad.GetRange(0, RilevatoreVoce.CAMPIONI_FINESTRA).ToArray()
                _campioniVad.RemoveRange(0, RilevatoreVoce.CAMPIONI_FINESTRA)

                Dim byteFinestra As Byte() = _byteOriginaliVad.GetRange(0, RilevatoreVoce.CAMPIONI_FINESTRA * 2).ToArray()
                _byteOriginaliVad.RemoveRange(0, RilevatoreVoce.CAMPIONI_FINESTRA * 2)

                Dim probabilita As Single = _rilevatore.Probabilita(finestra)
                If _fase = FaseAssistente.Registrazione Then
                    _finestreAnalizzate += 1
                    If probabilita > _probabilitaVoceMassima Then _probabilitaVoceMassima = probabilita
                End If

                If Not _inParlato Then
                    _anticipoFrase.Enqueue(byteFinestra)
                    While _anticipoFrase.Count > FINESTRE_ANTICIPO_FRASE
                        _anticipoFrase.Dequeue()
                    End While

                    If probabilita >= SOGLIA_INIZIO_VOCE Then
                        _finestreVoce += 1
                    Else
                        _finestreVoce = 0
                    End If

                    If _finestreVoce >= FINESTRE_CONFERMA_VOCE Then
                        _inParlato = True
                        _msSilenzio = 0
                        _msParlato = _finestreVoce * MS_PER_FINESTRA
                        _audioFrase = New MemoryStream()
                        For Each blocco As Byte() In _anticipoFrase
                            _audioFrase.Write(blocco, 0, blocco.Length)
                        Next
                        _anticipoFrase.Clear()
                        voceRilevata = True
                    End If
                Else
                    If _audioFrase IsNot Nothing AndAlso _audioFrase.Length + byteFinestra.Length <= ByteMassimiFrase() Then
                        _audioFrase.Write(byteFinestra, 0, byteFinestra.Length)
                    End If

                    If probabilita < SOGLIA_FINE_VOCE Then
                        _msSilenzio += MS_PER_FINESTRA
                    Else
                        _msSilenzio = 0
                        _msParlato += MS_PER_FINESTRA
                        voceRilevata = True
                    End If

                    Dim fraseLunga As Boolean = _audioFrase IsNot Nothing AndAlso _audioFrase.Length >= ByteMassimiFrase()
                    If _msSilenzio >= MsSilenzioFineFrase() OrElse fraseLunga Then
                        If _msParlato >= MS_MINIMI_PARLATO AndAlso _audioFrase IsNot Nothing Then
                            frasi.Add(New RichiestaVocale With {.Testo = "", .Audio = _audioFrase.ToArray(), .Sessione = _numeroSessione})
                        End If
                        If _audioFrase IsNot Nothing Then _audioFrase.Dispose()
                        _audioFrase = Nothing
                        _inParlato = False
                        _finestreVoce = 0
                        _msSilenzio = 0
                        _msParlato = 0
                    End If
                End If
            End While
        Catch ex As Exception
            log.ScriviLog("AssistenteVocale.SegmentaAudio: " & ex.Message)
        End Try
        Return voceRilevata
    End Function

    Private Function LivelloVoceSufficiente() As Boolean
        Try
            Dim campioni As Byte() = _bufferGrezzo.Ultimi(BYTE_ANTICIPO_VERIFICA)
            If campioni.Length < 2 Then Return False
            Dim posizione As Integer = 0
            While posizione < campioni.Length - 1
                Dim fineBlocco As Integer = Math.Min(campioni.Length, posizione + BYTE_BLOCCO_LIVELLO)
                Dim somma As Double = 0
                Dim conteggio As Integer = 0
                For i As Integer = posizione To fineBlocco - 2 Step 2
                    Dim valore As Double = BitConverter.ToInt16(campioni, i)
                    somma += valore * valore
                    conteggio += 1
                Next
                If conteggio > 0 AndAlso Math.Sqrt(somma / conteggio) >= SOGLIA_RMS_ATTIVAZIONE Then Return True
                posizione = fineBlocco
            End While
            Return False
        Catch ex As Exception
            log.ScriviLog("AssistenteVocale.LivelloVoceSufficiente: " & ex.Message)
            Return True
        End Try
    End Function

    Private Sub AvviaFaseRegistrazione(conAnticipo As Boolean, Optional byteAnticipo As Integer = BYTE_ANTICIPO_ATTIVAZIONE)
        Try
            AzzeraSegmentazione()
            _inizioSessione = DateTime.Now
            _ultimaVoce = _inizioSessione
            _fase = FaseAssistente.Registrazione
            _numeroSessione += 1
            _probabilitaVoceMassima = 0.0F
            _finestreAnalizzate = 0
            _livelloMassimo = 0.0
            _bufferRicevutiSessione = 0
            _bufferSospesiSessione = 0
            Interlocked.Exchange(_chiamateMicrofonoSessione, 0)
            _riavviiMicrofonoSessione = 0
            _motiviSospensioneSessione = ""
            _anticipoSessione = conAnticipo
            SyncLock _bloccoFraseInAttesa
                _fraseInAttesa = Nothing
            End SyncLock
            _ignoraAudioFino = If(conAnticipo, DateTime.MinValue, DateTime.Now.AddMilliseconds(MILLISECONDI_ATTESA_SEGNALE))
            If conAnticipo Then
                Dim quantiAnticipo As Integer = Math.Max(2, Math.Min(byteAnticipo, BYTE_BUFFER_CIRCOLARE))
                quantiAnticipo -= quantiAnticipo Mod 2
                Dim anticipo As Byte() = _bufferGrezzo.Ultimi(quantiAnticipo)
                Dim anticipoOriginale As Byte() = _bufferNonAmplificato.Ultimi(quantiAnticipo)
                Dim comune As Integer = Math.Min(anticipo.Length, anticipoOriginale.Length)
                comune -= comune Mod 2
                If comune > 0 Then
                    Dim amplificato(comune - 1) As Byte
                    Dim nonAmplificato(comune - 1) As Byte
                    Array.Copy(anticipo, anticipo.Length - comune, amplificato, 0, comune)
                    Array.Copy(anticipoOriginale, anticipoOriginale.Length - comune, nonAmplificato, 0, comune)
                    Dim scartate As New List(Of RichiestaVocale)
                    SegmentaAudio(amplificato, nonAmplificato, comune, scartate)
                End If
            End If
        Catch ex As Exception
            log.ScriviLog("AssistenteVocale.AvviaFaseRegistrazione: " & ex.Message)
        End Try
    End Sub

    Private Sub InizioRegistrazioneSuInterfaccia(stato As Object)
        Try
            If _fase <> FaseAssistente.Registrazione Then
                log.ScriviLog("AssistenteVocale.InizioRegistrazioneSuInterfaccia: attivazione arrivata quando la sessione era già chiusa, segnale non emesso")
                Exit Sub
            End If
            _ultimaVoce = DateTime.Now
            _inAttesaRisposta = False
            _frasiNellaSessione = 0
            _risposteNellaSessione = 0
            If _conversazione Is Nothing Then _conversazione = New CLSAi()
            _conversazione.NuovaConversazione()
            SegnaleAttivazione(Nothing)
        Catch ex As Exception
            log.ScriviLog("AssistenteVocale.InizioRegistrazioneSuInterfaccia: " & ex.Message)
        End Try
    End Sub

    Private Sub AvviaRegistrazioneDaTasto()
        Try
            SyncLock _bloccoRiconoscitore
                If _rilevatore Is Nothing Then
                    MostraMessaggio("L'assistente vocale sta ancora caricando i modelli.")
                    Exit Sub
                End If
                AvviaFaseRegistrazione(False)
            End SyncLock
            InizioRegistrazioneSuInterfaccia(Nothing)
        Catch ex As Exception
            log.ScriviLog("AssistenteVocale.AvviaRegistrazioneDaTasto: " & ex.Message)
        End Try
    End Sub

    Private Sub TornaFaseAscolto(conSegnale As Boolean)
        Try
            Dim eraInRegistrazione As Boolean = (_fase = FaseAssistente.Registrazione)
            PrendiFraseInAttesa()
            SyncLock _bloccoRiconoscitore
                _fase = FaseAssistente.Ascolto
                _anticipoSessione = False
                _numeroSessione += 1
                AzzeraSegmentazione()
                _azzeramentoRichiesto = True
                _confermeAttivazione = 0
                _attivazioneBloccataFino = DateTime.Now.AddMilliseconds(MILLISECONDI_BLOCCO_DOPO_CHIUSURA)
            End SyncLock
            _inAttesaRisposta = False
            If eraInRegistrazione AndAlso _modo = ModoAttivazione.Windows Then Task.Run(New Action(AddressOf RiarmaRiconoscimentoServizio))
            If eraInRegistrazione AndAlso conSegnale Then SegnaleChiusura()
        Catch ex As Exception
            log.ScriviLog("AssistenteVocale.TornaFaseAscolto: " & ex.Message)
        End Try
    End Sub

    Private Sub MettiFraseInAttesa(richiesta As RichiestaVocale)
        Try
            If richiesta Is Nothing OrElse richiesta.Audio Is Nothing Then Exit Sub
            SyncLock _bloccoFraseInAttesa
                If _fraseInAttesa Is Nothing OrElse _fraseInAttesa.Sessione <> richiesta.Sessione OrElse _fraseInAttesa.Audio Is Nothing Then
                    _fraseInAttesa = New RichiestaVocale With {.Testo = "", .Audio = richiesta.Audio, .Sessione = richiesta.Sessione}
                    Exit Sub
                End If
                If _fraseInAttesa.Audio.Length + richiesta.Audio.Length > BYTE_MASSIMI_TESTO_EMAIL Then Exit Sub
                Dim unito(_fraseInAttesa.Audio.Length + richiesta.Audio.Length - 1) As Byte
                Array.Copy(_fraseInAttesa.Audio, 0, unito, 0, _fraseInAttesa.Audio.Length)
                Array.Copy(richiesta.Audio, 0, unito, _fraseInAttesa.Audio.Length, richiesta.Audio.Length)
                _fraseInAttesa.Audio = unito
            End SyncLock
        Catch ex As Exception
            log.ScriviLog("AssistenteVocale.MettiFraseInAttesa: " & ex.Message & Environment.NewLine & ex.StackTrace)
        End Try
    End Sub

    Private Function PrendiFraseInAttesa() As RichiestaVocale
        Try
            SyncLock _bloccoFraseInAttesa
                Dim frase As RichiestaVocale = _fraseInAttesa
                _fraseInAttesa = Nothing
                Return frase
            End SyncLock
        Catch ex As Exception
            log.ScriviLog("AssistenteVocale.PrendiFraseInAttesa: " & ex.Message & Environment.NewLine & ex.StackTrace)
            Return Nothing
        End Try
    End Function

    Private Sub FraseSuInterfaccia(stato As Object)
        Try
            Dim richiesta As RichiestaVocale = TryCast(stato, RichiestaVocale)
            If richiesta Is Nothing Then Exit Sub
            If _fase <> FaseAssistente.Registrazione OrElse richiesta.Sessione <> _numeroSessione Then
                log.ScriviLog("AssistenteVocale.FraseSuInterfaccia: frase scartata perché la sessione di ascolto è già cambiata")
                Exit Sub
            End If
            If richiesta.Audio Is Nothing OrElse richiesta.Audio.Length < BYTE_PER_SECONDO \ 3 Then Exit Sub
            If _frasiNellaSessione = 0 AndAlso Not DialogoEmailAttivo() AndAlso richiesta.Audio.Length < BYTE_MASSIMI_FRASE_SPURIA AndAlso (DateTime.Now - _inizioSessione).TotalMilliseconds < MS_FINESTRA_FRASI_SPURIE Then
                _ultimaVoce = DateTime.Now
                Exit Sub
            End If
            If _inAttesaRisposta OrElse _elaborazioneEsternaInCorso Then
                MettiFraseInAttesa(richiesta)
                Exit Sub
            End If
            richiesta.PrimaFrase = (_frasiNellaSessione = 0)

            _frasiNellaSessione += 1
            AvviaElaborazioneEsterna(richiesta)
        Catch ex As Exception
            log.ScriviLog("AssistenteVocale.FraseSuInterfaccia: " & ex.Message & Environment.NewLine & ex.StackTrace)
        End Try
    End Sub

    Private Sub ElaboraComando(testo As String)
        Try
            If String.IsNullOrWhiteSpace(testo) Then Exit Sub
            AvviaElaborazioneEsterna(New RichiestaVocale With {.Testo = testo, .Audio = Nothing, .Sessione = -1})
        Catch ex As Exception
            log.ScriviLog("AssistenteVocale.ElaboraComando: " & ex.Message & Environment.NewLine & ex.StackTrace)
        End Try
    End Sub

    Private Sub AvviaElaborazioneEsterna(richiesta As RichiestaVocale)
        Try
            Dim messaggioElaborazione As String = ""
            If Not ElaborazioneDisponibile(messaggioElaborazione) Then
                Parla(messaggioElaborazione)
                Exit Sub
            End If
            If _elaborazioneEsternaInCorso Then Exit Sub

            AssicuraMarshal()
            If _conversazione Is Nothing Then _conversazione = New CLSAi()
            _elaborazioneEsternaInCorso = True
            If richiesta.Sessione >= 0 Then
                _inAttesaRisposta = True
                SospendiControlliPeriodici()
            End If
            AggiornaSospensione()
            _idRichiestaCorrente += 1
            richiesta.Id = _idRichiestaCorrente
            _inizioElaborazioneEsterna = DateTime.Now
            SegnaleElaborazione()
            Task.Factory.StartNew(New Action(Of Object)(AddressOf LavoroElaborazioneEsterna), richiesta)
        Catch ex As Exception
            _elaborazioneEsternaInCorso = False
            _inAttesaRisposta = False
            log.ScriviLog("AssistenteVocale.AvviaElaborazioneEsterna: " & ex.Message & Environment.NewLine & ex.StackTrace)
        End Try
    End Sub

    Private Sub AssicuraMarshal()
        Try
            If _marshal IsNot Nothing AndAlso Not _marshal.IsDisposed Then Exit Sub
            _marshal = New System.Windows.Forms.Control()
            Dim riferimento As IntPtr = _marshal.Handle
        Catch ex As Exception
            log.ScriviLog("AssistenteVocale.AssicuraMarshal: " & ex.Message & Environment.NewLine & ex.StackTrace)
        End Try
    End Sub

    Private Sub LavoroElaborazioneEsterna(stato As Object)
        Dim risultato As New RisultatoEsterno()
        Try
            Dim richiesta As RichiestaVocale = TryCast(stato, RichiestaVocale)
            If richiesta Is Nothing Then Exit Sub
            risultato.IdRichiesta = richiesta.Id
            risultato.Domanda = richiesta.Testo
            risultato.Sessione = richiesta.Sessione

            Dim audioValido As Boolean = richiesta.Audio IsNot Nothing AndAlso richiesta.Audio.Length >= BYTE_PER_SECONDO \ 3
            Dim erroriCloud As New List(Of String)
            Dim connesso As Boolean = InternetDisponibile()
            PubblicaSuInterfaccia(AddressOf ConnessioneVerificataSuInterfaccia, connesso)

            If DialogoEmailAttivo() Then
                ElaborazioneDialogoEmail(richiesta, audioValido, risultato, connesso)
                Exit Sub
            End If

            If ElaborazioneImpostata() = ElaborazioneComandi.Locale Then
                ElaborazioneLocale(richiesta, audioValido, risultato, connesso)
                Exit Sub
            End If

            If connesso Then
                If ElaborazioneCloud(richiesta, audioValido, risultato, erroriCloud) Then Exit Sub
            End If

            If Not connesso OrElse ErroriDiReteOQuota(erroriCloud) Then
                risultato.Errore = ""
                ElaborazioneLocale(richiesta, audioValido, risultato, connesso)
                Exit Sub
            End If

            risultato.Errore = If(erroriCloud.Count > 0, erroriCloud(erroriCloud.Count - 1), "Nessuna risposta dall'intelligenza artificiale.")
        Catch ex As Exception
            log.ScriviLog("AssistenteVocale.LavoroElaborazioneEsterna: " & ex.Message & Environment.NewLine & ex.StackTrace)
            If String.IsNullOrEmpty(risultato.Errore) Then risultato.Errore = "Non è stato possibile elaborare la richiesta."
        Finally
            PubblicaSuInterfaccia(AddressOf ElaborazioneEsternaCompletata, risultato)
        End Try
    End Sub

    Private Function ElaborazioneCloud(richiesta As RichiestaVocale, audioValido As Boolean, risultato As RisultatoEsterno, erroriCloud As List(Of String)) As Boolean
        Try
            If ProviderAiInUso() = CLSAi.ProviderAi.Gemini AndAlso CLSAi.ChiaveApiConfigurata(CLSAi.ProviderAi.Gemini) Then
                Dim wav As Byte() = If(audioValido, CreaWav(richiesta.Audio), Nothing)
                If wav IsNot Nothing OrElse Not String.IsNullOrWhiteSpace(richiesta.Testo) Then
                    Dim esito As CLSAi.RispostaFunzioneAi = _conversazione.InterrogaGeminiConFunzioniAsync(IstruzioniFunzioni(), wav, richiesta.Testo, DichiarazioniFunzioni()).Result
                    If esito IsNot Nothing AndAlso Not String.IsNullOrEmpty(esito.NomeFunzione) Then
                        MappaFunzione(esito.NomeFunzione, esito.Argomenti, risultato)
                        Return True
                    End If
                    If esito IsNot Nothing AndAlso Not String.IsNullOrWhiteSpace(esito.Testo) Then
                        risultato.Risposta = PulisciTestoParlato(esito.Testo)
                        Return True
                    End If
                    If esito IsNot Nothing AndAlso Not String.IsNullOrEmpty(esito.Errore) Then erroriCloud.Add(esito.Errore)
                End If
            End If

            Dim domanda As String = richiesta.Testo
            If audioValido AndAlso WGDettatura.AlmenoUnProviderConfigurato() Then
                Dim erroreTrascrizione As String = ""
                Dim trascritto As String = TrascriviAudio(richiesta.Audio, erroreTrascrizione)
                If Not String.IsNullOrWhiteSpace(trascritto) Then
                    domanda = trascritto
                    risultato.Domanda = trascritto
                ElseIf Not String.IsNullOrEmpty(erroreTrascrizione) Then
                    erroriCloud.Add(erroreTrascrizione)
                End If
            End If

            If String.IsNullOrWhiteSpace(domanda) Then
                If erroriCloud.Count > 0 Then Return False
                log.ScriviLog("AssistenteVocale.ElaborazioneCloud: la trascrizione della richiesta è vuota, richiesta ignorata")
                risultato.Ignora = True
                Return True
            End If

            If CLSAi.AlmenoUnProviderConfigurato() Then
                Dim esitoTesto As Tuple(Of String, String) = _conversazione.InterrogaConIstruzioniAsync(IstruzioniAssistente(), domanda, providerPrimo:=ProviderAiInUso()).Result
                If esitoTesto IsNot Nothing AndAlso Not String.IsNullOrWhiteSpace(esitoTesto.Item1) Then
                    InterpretaRispostaAi(esitoTesto.Item1, risultato)
                    Return True
                ElseIf esitoTesto IsNot Nothing AndAlso Not String.IsNullOrEmpty(esitoTesto.Item2) Then
                    erroriCloud.Add(esitoTesto.Item2)
                End If
            End If

            Return False
        Catch ex As Exception
            log.ScriviLog("AssistenteVocale.ElaborazioneCloud: " & ex.Message & Environment.NewLine & ex.StackTrace)
            erroriCloud.Add(DescriviEccezione(ex))
            Return False
        End Try
    End Function

    Private Shared Function DescriviEccezione(ex As Exception) As String
        Try
            Dim testo As New StringBuilder()
            Dim corrente As Exception = ex
            While corrente IsNot Nothing
                If testo.Length > 0 Then testo.Append(" ")
                testo.Append(corrente.GetType().Name).Append(": ").Append(corrente.Message)
                corrente = corrente.InnerException
            End While
            Return testo.ToString()
        Catch exDescrizione As Exception
            log.ScriviLog("AssistenteVocale.DescriviEccezione: " & exDescrizione.Message)
            Return If(ex IsNot Nothing, ex.Message, "")
        End Try
    End Function

    Private Shared Function InternetDisponibile() As Boolean
        Try
            If Not System.Net.NetworkInformation.NetworkInterface.GetIsNetworkAvailable() Then Return False
            Dim verifica As New connessione()
            Return verifica.VerificaConnessione()
        Catch ex As Exception
            log.ScriviLog("AssistenteVocale.InternetDisponibile: " & ex.Message)
            Return False
        End Try
    End Function

    Private Shared Function ErroriDiReteOQuota(errori As List(Of String)) As Boolean
        Try
            If errori Is Nothing OrElse errori.Count = 0 Then Return True
            For Each errore As String In errori
                If Regex.IsMatch(If(errore, ""), MODELLO_ERRORE_RETE_O_QUOTA, RegexOptions.IgnoreCase) Then Return True
            Next
            Return False
        Catch ex As Exception
            log.ScriviLog("AssistenteVocale.ErroriDiReteOQuota: " & ex.Message)
            Return True
        End Try
    End Function

    Private Sub ElaborazioneLocale(richiesta As RichiestaVocale, audioValido As Boolean, risultato As RisultatoEsterno, connesso As Boolean)
        Try
            Dim motivo As String = If(connesso, "le richieste disponibili per l'intelligenza artificiale sono esaurite o il servizio non risponde", "manca la connessione a internet")
            If ElaborazioneImpostata() = ElaborazioneComandi.Locale Then motivo = "l'elaborazione dei comandi è impostata sul computer"
            Dim candidati As New List(Of String)

            If audioValido Then
                Dim usaParakeet As Boolean = ParakeetPerComandi()
                If Not usaParakeet AndAlso Not _localeDisponibile Then
                    risultato.Errore = "L'assistente vocale non è disponibile: " & motivo & " e il riconoscimento vocale locale non è attivo."
                    Exit Sub
                End If

                If usaParakeet Then
                    Dim erroreParakeet As String = ""
                    Dim testoParakeet As String = TogliParolaAttivazione(WGTrascrittoreLocale.TrascriviPcm16(PreElaborazioneVoce.PreparaPerParakeet(richiesta.Audio), FREQUENZA_CAMPIONAMENTO, erroreParakeet))
                    If Not String.IsNullOrEmpty(erroreParakeet) Then log.ScriviLog("AssistenteVocale.ElaborazioneLocale (Parakeet): " & erroreParakeet)
                    If Not String.IsNullOrWhiteSpace(testoParakeet) Then
                        candidati.Add(testoParakeet.Trim().TrimEnd("."c, "!"c, "?"c))
                    End If
                End If

                If Not usaParakeet AndAlso _localeDisponibile Then
                    Dim riconosciuti As List(Of CandidatoLocale) = Nothing
                    SyncLock _bloccoComandi
                        If AssicuraRiconoscitoreComandi() Then riconosciuti = RiconosciLocalmente(richiesta.Audio)
                    End SyncLock
                    If riconosciuti Is Nothing Then riconosciuti = New List(Of CandidatoLocale)
                    For Each candidato As CandidatoLocale In riconosciuti
                        Dim pulito As String = TogliParolaAttivazione(candidato.Testo)
                        If Not String.IsNullOrWhiteSpace(pulito) AndAlso Not candidati.Contains(pulito) Then candidati.Add(pulito)
                    Next
                End If
            ElseIf Not String.IsNullOrWhiteSpace(richiesta.Testo) Then
                candidati.Add(richiesta.Testo.Trim())
            End If

            For indiceCandidato As Integer = candidati.Count - 1 To 0 Step -1
                candidati(indiceCandidato) = ApplicaSostituzioni(candidati(indiceCandidato)).Trim()
                If String.IsNullOrWhiteSpace(candidati(indiceCandidato)) Then candidati.RemoveAt(indiceCandidato)
            Next

            If candidati.Count = 0 Then
                risultato.Ignora = True
                Exit Sub
            End If

            If Regex.IsMatch(candidati(0), MODELLO_STOP_LOCALE, RegexOptions.IgnoreCase) Then
                risultato.Domanda = candidati(0)
                risultato.ChiudiSessione = True
                Exit Sub
            End If

            For Each candidato As String In candidati
                Dim comando As ComandoVocale = _interprete.Interpreta(candidato)
                If comando IsNot Nothing AndAlso comando.Tipo <> TipoComandoVocale.Sconosciuto Then
                    risultato.Domanda = candidato
                    Dim azione As String = If(connesso, "", AzioneInternet(comando))
                    If Not String.IsNullOrEmpty(azione) Then
                        risultato.Risposta = "Senza connessione a internet non posso " & azione & ". Riprova quando la connessione torna."
                        Exit Sub
                    End If
                    If comando.Tipo = TipoComandoVocale.InviaEmail Then
                        risultato.Risposta = MessaggioMailSoloGemini()
                        Exit Sub
                    End If
                    If (comando.Tipo = TipoComandoVocale.CercaVideo OrElse comando.Tipo = TipoComandoVocale.RiproduciVideo) AndAlso connesso Then
                        comando.Argomento = CorreggiTitoloYoutube(comando.Argomento)
                    End If
                    risultato.Comando = comando
                    Exit Sub
                End If
            Next

            risultato.Domanda = candidati(0)
            If richiesta.PrimaFrase AndAlso Regex.Split(candidati(0).Trim(), "\s+").Length <= 2 Then
                risultato.Ignora = True
                Exit Sub
            End If
            If ElaborazioneImpostata() = ElaborazioneComandi.Locale Then
                risultato.Risposta = "Non ho capito. Ho sentito: " & candidati(0) & "."
            Else
                risultato.Risposta = "L'intelligenza artificiale non è raggiungibile perché " & motivo & ": in questo momento eseguo solo i comandi del programma. Ho sentito: " & candidati(0) & "."
            End If
        Catch ex As Exception
            log.ScriviLog("AssistenteVocale.ElaborazioneLocale: " & ex.Message & Environment.NewLine & ex.StackTrace)
            risultato.Errore = "Non è stato possibile elaborare la richiesta con il riconoscimento vocale locale."
        End Try
    End Sub

    Private Function RiconosciLocalmente(audio As Byte()) As List(Of CandidatoLocale)
        Dim candidati As New List(Of CandidatoLocale)
        Try
            If _riconoscitoreComandi = IntPtr.Zero OrElse audio Is Nothing OrElse audio.Length < 2 Then Return candidati

            Dim varianti As List(Of Byte())
            If _preElaborazioneFrasi IsNot Nothing Then
                varianti = _preElaborazioneFrasi.PreparaVarianti(audio)
            Else
                varianti = New List(Of Byte())
                varianti.Add(PreElaborazioneVoce.PreparaPerVosk(audio))
            End If

            Dim ordineVariante As Integer = 0
            For Each variante As Byte() In varianti
                If variante Is Nothing OrElse variante.Length < 2 Then Continue For
                Dim segmenti As New List(Of List(Of String))
                vosk_recognizer_reset(_riconoscitoreComandi)
                Dim posizione As Integer = 0
                While posizione < variante.Length
                    Dim blocco As Integer = Math.Min(BYTE_PER_SECONDO \ 4, variante.Length - posizione)
                    blocco -= blocco Mod 2
                    If blocco <= 0 Then Exit While
                    Dim parte(blocco - 1) As Byte
                    Array.Copy(variante, posizione, parte, 0, blocco)
                    If vosk_recognizer_accept_waveform(_riconoscitoreComandi, parte, blocco) <> 0 Then
                        AggiungiSegmento(LeggiStringaUtf8(vosk_recognizer_result(_riconoscitoreComandi)), segmenti)
                    End If
                    posizione += blocco
                End While
                AggiungiSegmento(LeggiStringaUtf8(vosk_recognizer_final_result(_riconoscitoreComandi)), segmenti)
                vosk_recognizer_reset(_riconoscitoreComandi)

                If segmenti.Count > 0 Then
                    Dim massimo As Integer = 0
                    For Each segmento As List(Of String) In segmenti
                        massimo = Math.Max(massimo, segmento.Count)
                    Next
                    For indice As Integer = 0 To massimo - 1
                        Dim testo As New StringBuilder()
                        For Each segmento As List(Of String) In segmenti
                            Dim parola As String = If(indice < segmento.Count, segmento(indice), segmento(0))
                            If Not String.IsNullOrWhiteSpace(parola) Then
                                If testo.Length > 0 Then testo.Append(" ")
                                testo.Append(parola.Trim())
                            End If
                        Next
                        If testo.Length > 0 Then candidati.Add(New CandidatoLocale With {.Testo = testo.ToString(), .Ordine = indice * 10 + ordineVariante})
                    Next
                End If
                ordineVariante += 1
            Next

            candidati.Sort(New Comparison(Of CandidatoLocale)(AddressOf ConfrontaCandidati))
        Catch ex As Exception
            log.ScriviLog("AssistenteVocale.RiconosciLocalmente: " & ex.Message & Environment.NewLine & ex.StackTrace)
        End Try
        Return candidati
    End Function

    Private Shared Function ConfrontaCandidati(primo As CandidatoLocale, secondo As CandidatoLocale) As Integer
        Try
            Return primo.Ordine.CompareTo(secondo.Ordine)
        Catch ex As Exception
            log.ScriviLog("AssistenteVocale.ConfrontaCandidati: " & ex.Message)
            Return 0
        End Try
    End Function

    Private Shared Sub AggiungiSegmento(json As String, segmenti As List(Of List(Of String)))
        Try
            If String.IsNullOrWhiteSpace(json) Then Exit Sub
            Dim oggetto As JObject = JObject.Parse(json)
            Dim alternative As New List(Of String)
            Dim elenco As JArray = TryCast(oggetto("alternatives"), JArray)
            If elenco IsNot Nothing Then
                For Each elemento As JToken In elenco
                    Dim voce As JObject = TryCast(elemento, JObject)
                    If voce Is Nothing Then Continue For
                    Dim testo As JToken = voce("text")
                    If testo Is Nothing OrElse testo.Type = JTokenType.Null Then Continue For
                    Dim valore As String = Convert.ToString(testo, System.Globalization.CultureInfo.InvariantCulture).Trim()
                    If valore.Length > 0 Then alternative.Add(valore)
                Next
            Else
                Dim testoSingolo As String = LeggiCampoJson(json, "text")
                If testoSingolo.Length > 0 Then alternative.Add(testoSingolo)
            End If
            If alternative.Count > 0 Then segmenti.Add(alternative)
        Catch ex As Exception
            log.ScriviLog("AssistenteVocale.AggiungiSegmento: " & ex.Message)
        End Try
    End Sub

    Private Function IstruzioniFunzioni() As String
        Try
            Dim cultura As System.Globalization.CultureInfo = System.Globalization.CultureInfo.GetCultureInfo("it-IT")
            Dim istruzioni As New StringBuilder()
            istruzioni.Append("Sei l'assistente vocale di WinGabriel, un programma per persone non vedenti e ipovedenti. L'utente ti parla a voce: la sua richiesta è nell'audio allegato. Le tue risposte vengono lette da una sintesi vocale. ")
            istruzioni.Append("Oggi è ").Append(DateTime.Now.ToString("dddd d MMMM yyyy", cultura)).Append(" e sono le ").Append(DateTime.Now.ToString("HH:mm", System.Globalization.CultureInfo.InvariantCulture)).Append(". ")
            istruzioni.Append("Ignora la parola di attivazione " & DescrizioneParola(ParolaImpostata()) & " all'inizio dell'audio. ")
            istruzioni.Append("Chiama sempre una e una sola funzione. ")
            istruzioni.Append("Per domande, chiacchiere e ogni richiesta che non corrisponda a un'altra funzione chiama rispondi, con la trascrizione di quello che l'utente ha detto e la tua risposta in italiano, come in un dialogo parlato: testo semplice da ascoltare, senza elenchi, senza simboli di formattazione, senza link, frasi brevi, al massimo sei frasi salvo richiesta diversa. ")
            istruzioni.Append("Per YouTube chiama riproduci_video quando l'utente vuole ascoltare o guardare qualcosa, cerca_video solo quando chiede di cercare o di vedere l'elenco. ")
            istruzioni.Append("Chiama chiudi_conversazione quando l'utente vuole terminare. ")
            istruzioni.Append("Chiama ignora quando nell'audio non c'è una richiesta rivolta all'assistente: rumore, musica, televisione, radio, persone che parlano tra loro, frasi tronche o prive di senso. Nel dubbio fra ignora e una richiesta vera, scegli ignora. ")
            istruzioni.Append("Tieni conto dei messaggi precedenti della conversazione.")
            Return istruzioni.ToString()
        Catch ex As Exception
            log.ScriviLog("AssistenteVocale.IstruzioniFunzioni: " & ex.Message)
            Return "Chiama sempre una funzione."
        End Try
    End Function

    Private Shared Function Proprieta(tipo As String, descrizione As String) As JObject
        Dim oggetto As New JObject()
        Try
            oggetto("type") = tipo
            oggetto("description") = descrizione
        Catch ex As Exception
            log.ScriviLog("AssistenteVocale.Proprieta: " & ex.Message)
        End Try
        Return oggetto
    End Function

    Private Shared Function Funzione(nome As String, descrizione As String, proprieta As JObject, obbligatori As String()) As JObject
        Dim oggetto As New JObject()
        Try
            oggetto("name") = nome
            oggetto("description") = descrizione
            If proprieta IsNot Nothing AndAlso proprieta.Count > 0 Then
                Dim parametri As New JObject()
                parametri("type") = "object"
                parametri("properties") = proprieta
                If obbligatori IsNot Nothing AndAlso obbligatori.Length > 0 Then
                    Dim richiesti As New JArray()
                    For Each nomeRichiesto As String In obbligatori
                        richiesti.Add(nomeRichiesto)
                    Next
                    parametri("required") = richiesti
                End If
                oggetto("parameters") = parametri
            End If
        Catch ex As Exception
            log.ScriviLog("AssistenteVocale.Funzione: " & ex.Message)
        End Try
        Return oggetto
    End Function

    Private Shared Function DichiarazioniFunzioni() As JArray
        Dim elenco As New JArray()
        Try
            Dim p As JObject

            p = New JObject()
            p("testo") = Proprieta("string", "Titolo o argomento da cercare su YouTube.")
            elenco.Add(Funzione("riproduci_video", "Cerca su YouTube e riproduce subito il primo risultato, mettendo in coda gli altri.", p, New String() {"testo"}))

            p = New JObject()
            p("testo") = Proprieta("string", "Testo da cercare su YouTube.")
            elenco.Add(Funzione("cerca_video", "Cerca su YouTube e mostra soltanto l'elenco dei risultati, senza riprodurre nulla.", p, New String() {"testo"}))

            p = New JObject()
            p("nome") = Proprieta("string", "Nome della stazione radio.")
            elenco.Add(Funzione("ascolta_radio", "Fa partire una stazione radio.", p, New String() {"nome"}))

            p = New JObject()
            p("destinatario") = Proprieta("string", "Nome del contatto o indirizzo email, solo se detto nella richiesta. Minerva e Jarvis sono i nomi dell'assistente e non sono mai destinatari. Se il destinatario non è stato detto, lasciare vuoto.")
            p("oggetto") = Proprieta("string", "Oggetto della mail.")
            p("corpo") = Proprieta("string", "Testo della mail, con maiuscole e punteggiatura corrette.")
            elenco.Add(Funzione("invia_mail", "Prepara l'invio di una mail. I dati mancanti vengono chiesti dal programma, che chiede conferma prima di spedirla.", p, Nothing))

            p = New JObject()
            p("solo_non_letti") = Proprieta("boolean", "Vero per leggere solo i messaggi non letti.")
            elenco.Add(Funzione("leggi_posta", "Riepiloga i messaggi della posta in arrivo.", p, Nothing))

            p = New JObject()
            p("solo_non_letti") = Proprieta("boolean", "Vero per considerare solo i messaggi non letti.")
            elenco.Add(Funzione("leggi_ultima_mail", "Apre e legge il messaggio più recente della posta in arrivo.", p, Nothing))

            p = New JObject()
            p("descrizione") = Proprieta("string", "Descrizione dell'appuntamento.")
            p("data") = Proprieta("string", "Data nel formato aaaa-mm-gg.")
            p("ora_inizio") = Proprieta("string", "Ora di inizio nel formato hh:mm, vuota per tutta la giornata.")
            p("ora_fine") = Proprieta("string", "Ora di fine nel formato hh:mm, facoltativa.")
            elenco.Add(Funzione("aggiungi_appuntamento", "Aggiunge un appuntamento all'agenda.", p, New String() {"descrizione", "data"}))

            p = New JObject()
            p("data") = Proprieta("string", "Giorno richiesto nel formato aaaa-mm-gg.")
            elenco.Add(Funzione("appuntamenti_giorno", "Legge gli appuntamenti di un giorno.", p, New String() {"data"}))

            p = New JObject()
            p("citta") = Proprieta("string", "Città richiesta, vuota per la posizione dell'utente.")
            p("giorni_da_oggi") = Proprieta("integer", "Numero di giorni da oggi, da 0 a 6.")
            elenco.Add(Funzione("meteo", "Legge il meteo di un giorno.", p, Nothing))

            elenco.Add(Funzione("ora_data", "Dice l'ora e la data attuali.", Nothing, Nothing))

            p = New JObject()
            Dim sezione As JObject = Proprieta("string", "Sezione da aprire.")
            Dim valoriSezione As New JArray()
            For Each valoreSezione As String In New String() {"posta", "agenda", "rubrica", "meteo", "radio", "youtube", "ai", "calendario"}
                valoriSezione.Add(valoreSezione)
            Next
            sezione("enum") = valoriSezione
            p("sezione") = sezione
            elenco.Add(Funzione("apri_sezione", "Apre una sezione del programma.", p, New String() {"sezione"}))

            p = New JObject()
            p("trascrizione") = Proprieta("string", "Quello che l'utente ha detto, scritto correttamente.")
            p("risposta") = Proprieta("string", "La risposta da leggere all'utente.")
            elenco.Add(Funzione("rispondi", "Risponde a domande e conversazioni.", p, New String() {"trascrizione", "risposta"}))

            elenco.Add(Funzione("chiudi_conversazione", "Termina la conversazione con l'assistente.", Nothing, Nothing))
            elenco.Add(Funzione("ignora", "Scarta l'audio perché non contiene una richiesta rivolta all'assistente.", Nothing, Nothing))
        Catch ex As Exception
            log.ScriviLog("AssistenteVocale.DichiarazioniFunzioni: " & ex.Message)
        End Try
        Return elenco
    End Function

    Private Sub MappaFunzione(nome As String, argomenti As JObject, risultato As RisultatoEsterno)
        Try
            Dim dati As JObject = If(argomenti, New JObject())
            Dim comando As New ComandoVocale()
            comando.TestoOriginale = risultato.Domanda

            Select Case If(nome, "").ToLowerInvariant()
                Case "riproduci_video"
                    comando.Tipo = TipoComandoVocale.RiproduciVideo
                    comando.Argomento = LeggiCampoAi(dati, "testo")
                Case "cerca_video"
                    comando.Tipo = TipoComandoVocale.CercaVideo
                    comando.Argomento = LeggiCampoAi(dati, "testo")
                Case "ascolta_radio"
                    comando.Tipo = TipoComandoVocale.AscoltaRadio
                    comando.Argomento = LeggiCampoAi(dati, "nome")
                Case "invia_mail"
                    comando.Tipo = TipoComandoVocale.InviaEmail
                    comando.Destinatario = LeggiCampoAi(dati, "destinatario")
                    comando.Oggetto = LeggiCampoAi(dati, "oggetto")
                    comando.Corpo = LeggiCampoAi(dati, "corpo")
                    comando.OrigineOnline = True
                Case "leggi_posta"
                    comando.Tipo = TipoComandoVocale.LeggiPosta
                    comando.SoloNonLetti = Not String.Equals(LeggiCampoAi(dati, "solo_non_letti"), "false", StringComparison.OrdinalIgnoreCase)
                Case "leggi_ultima_mail"
                    comando.Tipo = TipoComandoVocale.LeggiUltimaEmail
                    comando.SoloNonLetti = Not String.Equals(LeggiCampoAi(dati, "solo_non_letti"), "false", StringComparison.OrdinalIgnoreCase)
                Case "aggiungi_appuntamento"
                    comando.Tipo = TipoComandoVocale.NuovoAppuntamento
                    comando.Argomento = LeggiCampoAi(dati, "descrizione")
                    LeggiDataOraAi(dati, comando)
                Case "appuntamenti_giorno"
                    comando.Tipo = TipoComandoVocale.AppuntamentiGiorno
                    Dim giorno As DateTime
                    If DateTime.TryParseExact(LeggiCampoAi(dati, "data"), "yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.None, giorno) Then
                        comando.Inizio = giorno.Date
                    Else
                        comando.Inizio = DateTime.Today
                    End If
                Case "meteo"
                    comando.Tipo = TipoComandoVocale.Meteo
                    comando.Citta = LeggiCampoAi(dati, "citta")
                    Dim giorni As Integer = 0
                    Integer.TryParse(LeggiCampoAi(dati, "giorni_da_oggi"), giorni)
                    If giorni < 0 Then giorni = 0
                    If giorni > 6 Then giorni = 6
                    comando.GiorniDaOggi = giorni
                Case "ora_data"
                    comando.Tipo = TipoComandoVocale.OraData
                Case "apri_sezione"
                    comando.Tipo = TipoComandoVocale.ApriSezione
                    comando.Argomento = LeggiCampoAi(dati, "sezione").ToLowerInvariant().Replace(" ", "")
                Case "rispondi"
                    Dim trascrizione As String = LeggiCampoAi(dati, "trascrizione")
                    If Not String.IsNullOrWhiteSpace(trascrizione) Then risultato.Domanda = TogliParolaAttivazione(trascrizione)
                    Dim testoRisposta As String = LeggiCampoAi(dati, "risposta")
                    If String.IsNullOrWhiteSpace(testoRisposta) Then testoRisposta = "Non ho una risposta per questa richiesta."
                    risultato.Risposta = PulisciTestoParlato(testoRisposta)
                    Exit Sub
                Case "chiudi_conversazione"
                    risultato.ChiudiSessione = True
                    Exit Sub
                Case Else
                    log.ScriviLog("AssistenteVocale.MappaFunzione: Gemini ha restituito la funzione " & If(nome, "") & " e la richiesta è stata ignorata")
                    risultato.Ignora = True
                    Exit Sub
            End Select

            If comando.Tipo = TipoComandoVocale.InviaEmail Then
                AvviaDialogoEmail(comando, risultato)
                Exit Sub
            End If

            risultato.Comando = comando
        Catch ex As Exception
            log.ScriviLog("AssistenteVocale.MappaFunzione: " & ex.Message & Environment.NewLine & ex.StackTrace)
            risultato.Errore = "Non è stato possibile interpretare la risposta."
        End Try
    End Sub

    Private Shared Function CreaWav(audio As Byte()) As Byte()
        Try
            Using flusso As New MemoryStream()
                Using scrittore As New NAudio.Wave.WaveFileWriter(flusso, New NAudio.Wave.WaveFormat(FREQUENZA_CAMPIONAMENTO, 16, 1))
                    scrittore.Write(audio, 0, audio.Length)
                    scrittore.Flush()
                    Return flusso.ToArray()
                End Using
            End Using
        Catch ex As Exception
            log.ScriviLog("AssistenteVocale.CreaWav: " & ex.Message)
            Return Nothing
        End Try
    End Function

    Protected Overrides Sub WndProc(ByRef m As System.Windows.Forms.Message)
        Try
            If m.Msg = WM_HOTKEY AndAlso m.WParam.ToInt32() = ID_TASTO_RAPIDO Then
                If _dettaturaAttiva OrElse WGDettatura.DettaturaInCorso Then
                    Console.Beep(300, 150)
                    Exit Sub
                End If
                If _fase = FaseAssistente.Registrazione Then
                    TornaFaseAscolto(True)
                Else
                    AvviaRegistrazioneDaTasto()
                End If
                Exit Sub
            End If
            MyBase.WndProc(m)
        Catch ex As Exception
            log.ScriviLog("AssistenteVocale.WndProc: " & ex.Message & Environment.NewLine & ex.StackTrace)
        End Try
    End Sub

    Private Sub Rilascia()
        Try
            _arrestato = True
            _fermaAscoltoServizio.Set()
            FermaControlloConnessione()
            If _modo = ModoAttivazione.Parakeet Then WGTrascrittoreLocale.MantieniInMemoria = False

            If _modo = ModoAttivazione.Windows Then SpegniRiconoscimentoServizio()

            If _timerControllo IsNot Nothing Then
                _timerControllo.Stop()
                RemoveHandler _timerControllo.Tick, AddressOf ControlloPeriodico
                _timerControllo.Dispose()
                _timerControllo = Nothing
            End If
            _timerControlloSospeso = False
            _microfonoAvviato = False

            If _processoAttesaAi IsNot Nothing Then
                Try
                    _processoAttesaAi.Fine()
                Catch exProcesso As Exception
                    log.ScriviLog("AssistenteVocale.Rilascia (processoAttesaAi): " & exProcesso.Message)
                End Try
                _processoAttesaAi = Nothing
            End If

            Try
                If _lettura IsNot Nothing Then
                    RemoveHandler _lettura.OnInizioLettura, AddressOf InizioLetturaSintesi
                    RemoveHandler _lettura.OnStatoParlatoCambiato, AddressOf StatoParlatoSintesi
                End If
                RemoveHandler WGDettatura.StatoDettaturaCambiato, AddressOf StatoDettatura
            Catch exEventi As Exception
                log.ScriviLog("AssistenteVocale.Rilascia (eventi): " & exEventi.Message)
            End Try

            If _tastoRegistrato AndAlso Handle <> IntPtr.Zero Then UnregisterHotKey(Handle, ID_TASTO_RAPIDO)
            _tastoRegistrato = False

            If _microfono IsNot Nothing Then
                Try
                    RemoveHandler _microfono.DataAvailable, AddressOf DatiMicrofono
                    RemoveHandler _microfono.RecordingStopped, AddressOf MicrofonoFermato
                    _microfono.StopRecording()
                    _microfono.Dispose()
                Catch exMicrofono As Exception
                    log.ScriviLog("AssistenteVocale.Rilascia (microfono): " & exMicrofono.Message)
                End Try
                _microfono = Nothing
            End If

            If _conversazione IsNot Nothing Then
                Try
                    _conversazione.Dispose()
                Catch exConversazione As Exception
                    log.ScriviLog("AssistenteVocale.Rilascia (conversazione): " & exConversazione.Message)
                End Try
                _conversazione = Nothing
            End If

            SyncLock _bloccoRiconoscitore
                AzzeraSegmentazione()
                _rilevatoreDaRilasciare = _rilevatore
                _rilevatore = Nothing
                _rilevatoreParolaDaRilasciare = _rilevatoreParola
                _rilevatoreParola = Nothing
            End SyncLock
            Task.Run(New Action(AddressOf RilasciaRisorseInSottofondo))

            If Handle <> IntPtr.Zero Then DestroyHandle()
            If _marshal IsNot Nothing AndAlso Not _marshal.IsDisposed Then _marshal.Dispose()
            _marshal = Nothing
        Catch ex As Exception
            log.ScriviLog("AssistenteVocale.Rilascia: " & ex.Message & Environment.NewLine & ex.StackTrace)
        End Try
    End Sub

    Private Sub RilasciaRisorseInSottofondo()
        Try
            If _rilevatoreDaRilasciare IsNot Nothing Then _rilevatoreDaRilasciare.Dispose()
            _rilevatoreDaRilasciare = Nothing
            If _rilevatoreParolaDaRilasciare IsNot Nothing Then _rilevatoreParolaDaRilasciare.Dispose()
            _rilevatoreParolaDaRilasciare = Nothing
            RilasciaLocale()
        Catch ex As Exception
            log.ScriviLog("AssistenteVocale.RilasciaRisorseInSottofondo: " & ex.Message & Environment.NewLine & ex.StackTrace)
        End Try
    End Sub

    Private Shared Sub RiarmaRiconoscimentoServizio()
        Try
            If _istanza Is Nothing OrElse _istanza._arrestato OrElse _istanza._modo <> ModoAttivazione.Windows Then Exit Sub
            WGServiceInterop.InviaComando("WAKE_ON")
        Catch ex As Exception
            log.ScriviLog("AssistenteVocale.RiarmaRiconoscimentoServizio: " & ex.Message)
        End Try
    End Sub

    Private Shared Sub SpegniRiconoscimentoServizio()
        Try
            Dim risposta As String = WGServiceInterop.InviaComando("WAKE_OFF")
            If Not String.Equals(If(risposta, "").Trim(), "OK|0", StringComparison.OrdinalIgnoreCase) Then
                log.ScriviLog("AssistenteVocale.SpegniRiconoscimentoServizio: " & risposta)
            End If
        Catch ex As Exception
            log.ScriviLog("AssistenteVocale.SpegniRiconoscimentoServizio: " & ex.Message)
        End Try
    End Sub

    Private Sub InizioLetturaSintesi(testo As String, origine As String)
        Try
            PubblicaSuInterfaccia(AddressOf InizioLetturaSuInterfaccia, If(origine, ""))
        Catch ex As Exception
            log.ScriviLog("AssistenteVocale.InizioLetturaSintesi: " & ex.Message)
        End Try
    End Sub

    Private Sub InizioLetturaSuInterfaccia(stato As Object)
        Try
            Dim origine As String = TryCast(stato, String)
            If String.Equals(origine, ORIGINE_ASSISTENTE, StringComparison.Ordinal) Then
                _sintesiAssistenteAttesa = False
                _sintesiAssistenteInCorso = True
            Else
                _sintesiAssistenteInCorso = False
                If _fase = FaseAssistente.Registrazione AndAlso Not _inAttesaRisposta AndAlso Not _elaborazioneEsternaInCorso AndAlso DateTime.Now >= _ignoraLettureEsterneFino Then
                    log.ScriviLog("AssistenteVocale.InizioLetturaSuInterfaccia: sessione chiusa perché WinGabriel ha iniziato un'altra lettura, origine " & If(origine, ""))
                    TornaFaseAscolto(False)
                End If
            End If
            _sintesiInCorso = True
            _inizioUltimaLettura = DateTime.Now
            AggiornaSospensione()
        Catch ex As Exception
            log.ScriviLog("AssistenteVocale.InizioLetturaSuInterfaccia: " & ex.Message)
        End Try
    End Sub

    Private Sub StatoParlatoSintesi(staParlando As Boolean)
        Try
            PubblicaSuInterfaccia(AddressOf StatoParlatoSuInterfaccia, staParlando)
        Catch ex As Exception
            log.ScriviLog("AssistenteVocale.StatoParlatoSintesi: " & ex.Message)
        End Try
    End Sub

    Private Sub StatoParlatoSuInterfaccia(stato As Object)
        Try
            Dim parla As Boolean = CBool(stato)
            If parla Then
                _sintesiInCorso = True
                _inizioUltimaLettura = DateTime.Now
            Else
                _sintesiInCorso = False
                _fineSintesi = DateTime.Now
                If _sintesiAssistenteInCorso Then
                    _sintesiAssistenteInCorso = False
                    _ultimaVoce = DateTime.Now
                End If
            End If
            AggiornaSospensione()
        Catch ex As Exception
            log.ScriviLog("AssistenteVocale.StatoParlatoSuInterfaccia: " & ex.Message)
        End Try
    End Sub

    Private Sub StatoDettatura(attiva As Boolean)
        Try
            PubblicaSuInterfaccia(AddressOf StatoDettaturaSuInterfaccia, attiva)
        Catch ex As Exception
            log.ScriviLog("AssistenteVocale.StatoDettatura: " & ex.Message)
        End Try
    End Sub

    Private Sub StatoDettaturaSuInterfaccia(stato As Object)
        Try
            _dettaturaAttiva = CBool(stato)
            If _dettaturaAttiva AndAlso _fase = FaseAssistente.Registrazione Then TornaFaseAscolto(False)
            AggiornaSospensione()
        Catch ex As Exception
            log.ScriviLog("AssistenteVocale.StatoDettaturaSuInterfaccia: " & ex.Message)
        End Try
    End Sub

    Private Function MicrofonoDaSospendere() As Boolean
        Try
            If _dettaturaAttiva OrElse WGDettatura.DettaturaInCorso Then Return True
            If _sintesiInCorso OrElse _sintesiAssistenteAttesa Then Return True
            Return (DateTime.Now - _fineSintesi).TotalMilliseconds < MILLISECONDI_PAUSA_DOPO_SINTESI
        Catch ex As Exception
            log.ScriviLog("AssistenteVocale.MicrofonoDaSospendere: " & ex.Message)
            Return False
        End Try
    End Function

    Private Function MotivoSospensione() As String
        Try
            Dim motivi As New List(Of String)
            If _dettaturaAttiva OrElse WGDettatura.DettaturaInCorso Then motivi.Add("dettatura in corso")
            If _sintesiInCorso Then motivi.Add(If(_sintesiAssistenteInCorso, "sintesi dell'assistente", "sintesi di WinGabriel"))
            If _sintesiAssistenteAttesa Then motivi.Add("attesa dell'inizio della sintesi")
            If (DateTime.Now - _fineSintesi).TotalMilliseconds < MILLISECONDI_PAUSA_DOPO_SINTESI Then motivi.Add("pausa dopo la sintesi")
            Return String.Join(" e ", motivi)
        Catch ex As Exception
            log.ScriviLog("AssistenteVocale.MotivoSospensione: " & ex.Message)
            Return ""
        End Try
    End Function

    Private Sub AggiornaSospensione()
        Try
            Dim sospeso As Boolean = MicrofonoDaSospendere()
            If sospeso AndAlso _fase = FaseAssistente.Registrazione Then
                Dim motivo As String = MotivoSospensione()
                If Not String.IsNullOrEmpty(motivo) AndAlso _motiviSospensioneSessione.IndexOf(motivo, StringComparison.Ordinal) < 0 Then
                    _motiviSospensioneSessione = If(_motiviSospensioneSessione.Length = 0, motivo, _motiviSospensioneSessione & ", " & motivo)
                End If
            End If
            SyncLock _bloccoRiconoscitore
                If _microfonoSospeso AndAlso Not sospeso Then _azzeramentoRichiesto = True
                _microfonoSospeso = sospeso
            End SyncLock
        Catch ex As Exception
            log.ScriviLog("AssistenteVocale.AggiornaSospensione: " & ex.Message)
        End Try
    End Sub

    Private Sub SospendiControlliPeriodici()
        Try
            _ignoraLettureEsterneFino = DateTime.Now.AddMilliseconds(MILLISECONDI_TOLLERANZA_LETTURE)
            If _processoAttesaAi Is Nothing Then
                _processoAttesaAi = New WGProcessoInCorso()
                _processoAttesaAi.Avvia("Elaborazione della richiesta in corso, attendere", False)
            End If
        Catch ex As Exception
            log.ScriviLog("AssistenteVocale.SospendiControlliPeriodici: " & ex.Message)
        End Try
    End Sub

    Private Sub RipristinaControlliPeriodici()
        Try
            If _processoAttesaAi IsNot Nothing Then
                Dim processo As WGProcessoInCorso = _processoAttesaAi
                _processoAttesaAi = Nothing
                _ignoraLettureEsterneFino = DateTime.Now.AddMilliseconds(MILLISECONDI_TOLLERANZA_LETTURE)
                processo.Fine()
            End If
            _ultimaVoce = DateTime.Now
            If _timerControllo IsNot Nothing AndAlso Not _timerControllo.Enabled Then _timerControllo.Start()
        Catch ex As Exception
            log.ScriviLog("AssistenteVocale.RipristinaControlliPeriodici: " & ex.Message)
        End Try
    End Sub

    Private Sub AnnullaElaborazioneBloccata()
        Try
            _idRichiestaCorrente += 1
            _elaborazioneEsternaInCorso = False
            _inAttesaRisposta = False
            RipristinaControlliPeriodici()
            AggiornaSospensione()
            Parla("La richiesta non ha ricevuto risposta in tempo. Ripeterla.")
        Catch ex As Exception
            log.ScriviLog("AssistenteVocale.AnnullaElaborazioneBloccata: " & ex.Message & Environment.NewLine & ex.StackTrace)
        End Try
    End Sub

    Private Sub ControlloPeriodico(sender As Object, e As EventArgs)
        Try
            If _arrestato Then Exit Sub
            Dim adesso As DateTime = DateTime.Now

            If _elaborazioneEsternaInCorso AndAlso (adesso - _inizioElaborazioneEsterna).TotalSeconds > SECONDI_MASSIMI_ELABORAZIONE Then
                AnnullaElaborazioneBloccata()
            End If

            If _fase = FaseAssistente.Registrazione Then
                ControllaDatiMicrofonoInRegistrazione(adesso)
                If _fase <> FaseAssistente.Registrazione Then Exit Sub
            ElseIf _microfonoAvviato AndAlso (adesso - _ultimiDatiMicrofono).TotalSeconds > SECONDI_MASSIMI_SENZA_DATI_MICROFONO Then
                RiavviaMicrofono()
            End If

            If _lettura IsNot Nothing AndAlso _sintesiInCorso AndAlso Not _lettura.StaParlando AndAlso (adesso - _fineSintesi).TotalSeconds > 2 Then
                StatoParlatoSuInterfaccia(False)
            ElseIf _sintesiInCorso AndAlso (adesso - _inizioUltimaLettura).TotalSeconds > SECONDI_MASSIMI_SINTESI_SENZA_EVENTI Then
                StatoParlatoSuInterfaccia(False)
            End If

            If _sintesiAssistenteAttesa AndAlso (adesso - _inizioAttesaSintesi).TotalSeconds > SECONDI_ATTESA_INIZIO_SINTESI Then
                _sintesiAssistenteAttesa = False
                _ultimaVoce = adesso
            End If

            If _dettaturaAttiva AndAlso Not WGDettatura.DettaturaInCorso Then _dettaturaAttiva = False

            AggiornaSospensione()

            Dim limiteSilenzio As Integer = If(_frasiNellaSessione = 0, SECONDI_SILENZIO_PRIMA_FRASE, SECONDI_SILENZIO_CHIUSURA)
            Dim riferimentoSilenzio As DateTime = If(_inizioSessione > _ultimaVoce, _inizioSessione, _ultimaVoce)
            If _fase = FaseAssistente.Registrazione AndAlso Not _microfonoSospeso AndAlso Not _inAttesaRisposta AndAlso Not _elaborazioneEsternaInCorso AndAlso (adesso - riferimentoSilenzio).TotalSeconds >= limiteSilenzio Then
                Dim dialogoInterrotto As Boolean = DialogoEmailAttivo()
                If dialogoInterrotto Then AnnullaDialogoEmail()
                If _frasiNellaSessione = 0 Then log.ScriviLog("AssistenteVocale.ControlloPeriodico: sessione chiusa senza nessuna frase dopo il segnale, probabilità di voce massima " & _probabilitaVoceMassima.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture) & ", finestre analizzate " & _finestreAnalizzate.ToString(System.Globalization.CultureInfo.InvariantCulture) & ", livello massimo del microfono " & DescriviLivello(_livelloMassimo) & ", guadagno " & _guadagnoCorrente.ToString("0.0", System.Globalization.CultureInfo.InvariantCulture) & ", blocchi audio ricevuti " & _bufferRicevutiSessione.ToString(System.Globalization.CultureInfo.InvariantCulture) & " di cui scartati per microfono sospeso " & _bufferSospesiSessione.ToString(System.Globalization.CultureInfo.InvariantCulture) & ", blocchi arrivati dal microfono " & Interlocked.CompareExchange(_chiamateMicrofonoSessione, 0, 0).ToString(System.Globalization.CultureInfo.InvariantCulture) & ", riavvii del microfono " & _riavviiMicrofonoSessione.ToString(System.Globalization.CultureInfo.InvariantCulture) & If(_motiviSospensioneSessione.Length > 0, ", motivo della sospensione: " & _motiviSospensioneSessione, ""))
                TornaFaseAscolto(True)
                If dialogoInterrotto Then Parla("La mail non è stata inviata perché non ho sentito risposta. Ripetere la richiesta per ricominciare.")
            End If
        Catch ex As Exception
            log.ScriviLog("AssistenteVocale.ControlloPeriodico: " & ex.Message & Environment.NewLine & ex.StackTrace)
        End Try
    End Sub

    Private Sub ElaborazioneEsternaCompletata(stato As Object)
        Try
            Dim risultato As RisultatoEsterno = TryCast(stato, RisultatoEsterno)
            If risultato Is Nothing OrElse risultato.IdRichiesta <> _idRichiestaCorrente Then Exit Sub

            RipristinaControlliPeriodici()
            _elaborazioneEsternaInCorso = False
            _inAttesaRisposta = False
            AggiornaSospensione()

            If risultato.Sessione >= 0 Then
                If _fase <> FaseAssistente.Registrazione OrElse risultato.Sessione <> _numeroSessione Then Exit Sub
                _ultimaVoce = DateTime.Now
            End If

            If risultato.ChiudiSessione Then
                TornaFaseAscolto(True)
                Exit Sub
            End If

            If risultato.Ignora Then
                If risultato.Sessione >= 0 AndAlso _fase = FaseAssistente.Registrazione Then
                    _ultimaVoce = DateTime.Now
                    Dim successiva As RichiestaVocale = PrendiFraseInAttesa()
                    If successiva IsNot Nothing AndAlso successiva.Sessione = _numeroSessione Then
                        _frasiNellaSessione += 1
                        AvviaElaborazioneEsterna(successiva)
                    End If
                End If
                Exit Sub
            End If

            PrendiFraseInAttesa()
            _risposteNellaSessione += 1

            If risultato.Comando IsNot Nothing Then
                TornaFaseAscolto(False)
                SegnaleComando()
                EseguiComando(risultato.Comando)
                Exit Sub
            End If

            If Not String.IsNullOrWhiteSpace(risultato.Risposta) Then
                Try
                    If _conversazione IsNot Nothing Then _conversazione.RegistraScambio(risultato.Domanda, risultato.Risposta, "Assistente vocale: ")
                Catch exRegistra As Exception
                    log.ScriviLog("AssistenteVocale.ElaborazioneEsternaCompletata (registrazione): " & exRegistra.Message)
                End Try
                Parla(risultato.Risposta)
                Exit Sub
            End If

            Parla(If(String.IsNullOrWhiteSpace(risultato.Errore), "Richiesta non compresa: " & risultato.Domanda, risultato.Errore))
        Catch ex As Exception
            log.ScriviLog("AssistenteVocale.ElaborazioneEsternaCompletata: " & ex.Message & Environment.NewLine & ex.StackTrace)
        End Try
    End Sub

    Private Sub Parla(testo As String)
        Try
            If String.IsNullOrWhiteSpace(testo) Then Exit Sub

            _ignoraLettureEsterneFino = DateTime.Now.AddMilliseconds(MILLISECONDI_TOLLERANZA_LETTURE)
            _sintesiAssistenteAttesa = True
            _inizioAttesaSintesi = DateTime.Now
            AggiornaSospensione()

            Try
                Dim contenitore As WGContenitore = WGContenitore.Istanza
                If contenitore IsNot Nothing AndAlso Not contenitore.IsDisposed Then contenitore.MostraTestoAssistente(testo)
            Catch exContenitore As Exception
                log.ScriviLog("AssistenteVocale.Parla (contenitore): " & exContenitore.Message)
            End Try

            Dim lettura As sintesivocale = sintesivocale.GetInstance()
            lettura.LeggiTesto(testo, origine:=ORIGINE_ASSISTENTE)
        Catch ex As Exception
            _sintesiAssistenteAttesa = False
            log.ScriviLog("AssistenteVocale.Parla: " & ex.Message & Environment.NewLine & ex.StackTrace)
        End Try
    End Sub

    Private Sub PubblicaSuInterfaccia(callback As SendOrPostCallback, stato As Object)
        Try
            If _marshal IsNot Nothing AndAlso Not _marshal.IsDisposed AndAlso _marshal.IsHandleCreated Then
                If _marshal.InvokeRequired Then
                    _marshal.BeginInvoke(callback, New Object() {stato})
                Else
                    callback(stato)
                End If
            End If
        Catch ex As Exception
            log.ScriviLog("AssistenteVocale.PubblicaSuInterfaccia: " & ex.Message & Environment.NewLine & ex.StackTrace)
        End Try
    End Sub

    Private Function IstruzioniAssistente() As String
        Try
            Dim cultura As System.Globalization.CultureInfo = System.Globalization.CultureInfo.GetCultureInfo("it-IT")
            Dim istruzioni As New StringBuilder()
            istruzioni.Append("Sei l'assistente vocale di WinGabriel, un programma per persone non vedenti e ipovedenti. L'utente ti parla a voce e le tue risposte vengono lette da una sintesi vocale. ")
            istruzioni.Append("Se c'è un audio allegato, la richiesta è quella pronunciata nell'audio e il testo che lo accompagna è una trascrizione automatica imprecisa. Ignora la parola di attivazione " & DescrizioneParola(ParolaImpostata()) & ". ")
            istruzioni.Append("Oggi è ").Append(DateTime.Now.ToString("dddd d MMMM yyyy", cultura)).Append(" e sono le ").Append(DateTime.Now.ToString("HH:mm", System.Globalization.CultureInfo.InvariantCulture)).Append(". ")
            istruzioni.Append("Rispondi sempre e solo con un oggetto JSON, senza altro testo, con questi campi: ")
            istruzioni.Append("trascrizione, cioè la richiesta dell'utente scritta correttamente; ")
            istruzioni.Append("azione, uno tra video, cerca_video, radio, invia_mail, leggi_posta, leggi_ultima_mail, appuntamento, appuntamenti_giorno, meteo, ora_data, apri, chiudi, ignora, conversazione; ")
            istruzioni.Append("argomento, cioè il testo da cercare su YouTube, il nome della radio, la descrizione dell'appuntamento oppure la sezione da aprire tra posta, agenda, rubrica, meteo, radio, youtube, ai, calendario; ")
            istruzioni.Append("usa video quando l'utente vuole ascoltare o guardare qualcosa su YouTube, e cerca_video solo quando chiede esplicitamente di cercare o di vedere l'elenco dei risultati; ")
            istruzioni.Append("destinatario, oggetto e corpo per le mail, con il corpo scritto con maiuscole e punteggiatura corrette, lasciando vuoti i campi non detti e senza mai usare come destinatario Minerva o Jarvis, che sono i nomi dell'assistente; ")
            istruzioni.Append("citta e giorni_da_oggi (numero intero da 0 a 6) per il meteo; ")
            istruzioni.Append("data (aaaa-mm-gg), ora_inizio e ora_fine (hh:mm) per gli appuntamenti; ")
            istruzioni.Append("data (aaaa-mm-gg) del giorno di cui l'utente chiede gli appuntamenti per appuntamenti_giorno; ")
            istruzioni.Append("solo_non_letti (true o false) per la posta; ")
            istruzioni.Append("risposta, solo per l'azione conversazione. ")
            istruzioni.Append("Usa chiudi quando l'utente vuole terminare la conversazione. ")
            istruzioni.Append("Usa ignora quando nell'audio non c'è una richiesta rivolta all'assistente: rumore, musica, televisione, radio, persone che parlano tra loro, frasi tronche o prive di senso. Nel dubbio fra ignora e una richiesta vera, scegli ignora. ")
            istruzioni.Append("Usa conversazione per domande, chiacchiere e ogni richiesta che non sia una delle azioni elencate, e scrivi in risposta la tua risposta in italiano, come in un dialogo parlato: testo semplice da ascoltare, senza elenchi puntati, senza simboli di formattazione, senza link, con frasi brevi e al massimo sei frasi, salvo che l'utente chieda di più. ")
            istruzioni.Append("Tieni conto dei messaggi precedenti della conversazione. Lascia vuoti i campi che non servono.")
            Return istruzioni.ToString()
        Catch ex As Exception
            log.ScriviLog("AssistenteVocale.IstruzioniAssistente: " & ex.Message)
            Return "Rispondi solo con un oggetto JSON con i campi trascrizione, azione e risposta."
        End Try
    End Function

    Private Sub SegnaleElaborazione()
        Try
            Console.Beep(740, 60)
        Catch ex As Exception
            log.ScriviLog("AssistenteVocale.SegnaleElaborazione: " & ex.Message)
        End Try
    End Sub

    Private Sub SegnaleChiusura()
        Try
            Console.Beep(520, 90)
            Console.Beep(390, 120)
        Catch ex As Exception
            log.ScriviLog("AssistenteVocale.SegnaleChiusura: " & ex.Message)
        End Try
    End Sub

    Private Shared Function LeggiStringaUtf8(puntatore As IntPtr) As String
        Try
            If puntatore = IntPtr.Zero Then Return ""
            Dim lunghezza As Integer = 0
            While Marshal.ReadByte(puntatore, lunghezza) <> 0
                lunghezza += 1
            End While
            If lunghezza = 0 Then Return ""
            Dim bytes(lunghezza - 1) As Byte
            Marshal.Copy(puntatore, bytes, 0, lunghezza)
            Return Encoding.UTF8.GetString(bytes)
        Catch ex As Exception
            log.ScriviLog("AssistenteVocale.LeggiStringaUtf8: " & ex.Message)
            Return ""
        End Try
    End Function

    Private Shared Function LeggiCampoJson(json As String, campo As String) As String
        Try
            If String.IsNullOrWhiteSpace(json) Then Return ""
            Dim oggetto As JObject = JObject.Parse(json)
            Dim valore As JToken = oggetto(campo)
            If valore Is Nothing OrElse valore.Type = JTokenType.Null Then Return ""
            Return Convert.ToString(valore).Trim()
        Catch ex As Exception
            log.ScriviLog("AssistenteVocale.LeggiCampoJson: " & ex.Message)
            Return ""
        End Try
    End Function

    Private Function TrascriviAudio(audio As Byte(), ByRef errore As String) As String
        errore = ""
        Dim percorso As String = Path.Combine(Path.GetTempPath(), "wgassistente_" & Guid.NewGuid().ToString("N") & ".wav")
        Try
            Using scrittore As New NAudio.Wave.WaveFileWriter(percorso, New NAudio.Wave.WaveFormat(FREQUENZA_CAMPIONAMENTO, 16, 1))
                scrittore.Write(audio, 0, audio.Length)
            End Using

            Dim trascrittore As New WGDettatura()
            Try
                Dim esito As Tuple(Of String, String) = trascrittore.TrascriviFileAudioAsync(percorso).Result
                If esito Is Nothing OrElse String.IsNullOrWhiteSpace(esito.Item1) Then
                    If esito IsNot Nothing AndAlso Not String.IsNullOrEmpty(esito.Item2) Then errore = esito.Item2
                    Return ""
                End If
                Return TogliParolaAttivazione(esito.Item1)
            Finally
                trascrittore.Dispose()
            End Try
        Catch ex As Exception
            log.ScriviLog("AssistenteVocale.TrascriviAudio: " & ex.Message & Environment.NewLine & ex.StackTrace)
            errore = DescriviEccezione(ex)
            Return ""
        Finally
            Try
                If File.Exists(percorso) Then File.Delete(percorso)
            Catch exPulizia As Exception
                log.ScriviLog("AssistenteVocale.TrascriviAudio (pulizia): " & exPulizia.Message)
            End Try
        End Try
    End Function

    Private Shared Function TogliParolaAttivazione(testo As String) As String
        Try
            Dim t As String = Regex.Replace(If(testo, ""), "\s+", " ").Trim()
            Dim m As Match = Regex.Match(t, "^.*?(?:(?:e[hiy]|ehi|hey|hei|ei)[\s,]+)?(?:jarvis|giarvis|jarvi|minerva)\b[\s,.!?:;]*", RegexOptions.IgnoreCase)
            If m.Success AndAlso m.Index = 0 Then t = t.Substring(m.Length)
            t = Regex.Replace(t, "^" & MODELLO_ATTIVAZIONE & "[\s,.!?:;]*", "", RegexOptions.IgnoreCase)
            Dim pezzi As String() = t.Split(New Char() {" "c}, StringSplitOptions.RemoveEmptyEntries)
            If pezzi.Length > 0 Then
                Dim iniziali As New List(Of String)
                For indice As Integer = 0 To Math.Min(pezzi.Length, 3) - 1
                    iniziali.Add(Regex.Replace(pezzi(indice), "[^\p{L}']", "").ToLowerInvariant())
                Next
                Dim trovata As Integer = IndiceParolaAttivazione(iniziali, MODELLO_PREFISSO_ATTIVAZIONE_LIBERO)
                If trovata >= 0 Then t = String.Join(" ", pezzi, trovata + 1, pezzi.Length - trovata - 1)
            End If
            Return t.Trim(" "c, ","c, "."c, "!"c, "?"c, ":"c, ";"c).Trim()
        Catch ex As Exception
            log.ScriviLog("AssistenteVocale.TogliParolaAttivazione: " & ex.Message)
            Return If(testo, "").Trim()
        End Try
    End Function

    Private Sub InterpretaRispostaAi(contenuto As String, risultato As RisultatoEsterno)
        Try
            Dim testoJson As String = EstraiOggettoJson(contenuto)
            If String.IsNullOrEmpty(testoJson) Then
                risultato.Risposta = PulisciTestoParlato(contenuto)
                Exit Sub
            End If

            Dim dati As JObject = JObject.Parse(testoJson)
            Dim trascrizione As String = LeggiCampoAi(dati, "trascrizione")
            If Not String.IsNullOrWhiteSpace(trascrizione) Then risultato.Domanda = TogliParolaAttivazione(trascrizione)
            Dim azione As String = LeggiCampoAi(dati, "azione").ToLowerInvariant()
            Dim comando As New ComandoVocale()
            comando.TestoOriginale = risultato.Domanda
            comando.Argomento = LeggiCampoAi(dati, "argomento")

            Select Case azione
                Case "video"
                    comando.Tipo = TipoComandoVocale.RiproduciVideo
                Case "radio"
                    comando.Tipo = TipoComandoVocale.AscoltaRadio
                Case "invia_mail"
                    comando.Tipo = TipoComandoVocale.InviaEmail
                    comando.Destinatario = LeggiCampoAi(dati, "destinatario")
                    comando.Oggetto = LeggiCampoAi(dati, "oggetto")
                    comando.Corpo = LeggiCampoAi(dati, "corpo")
                    comando.OrigineOnline = True
                Case "leggi_posta"
                    comando.Tipo = TipoComandoVocale.LeggiPosta
                    comando.SoloNonLetti = Not String.Equals(LeggiCampoAi(dati, "solo_non_letti"), "false", StringComparison.OrdinalIgnoreCase)
                Case "leggi_ultima_mail"
                    comando.Tipo = TipoComandoVocale.LeggiUltimaEmail
                    comando.SoloNonLetti = Not String.Equals(LeggiCampoAi(dati, "solo_non_letti"), "false", StringComparison.OrdinalIgnoreCase)
                Case "appuntamento"
                    comando.Tipo = TipoComandoVocale.NuovoAppuntamento
                    LeggiDataOraAi(dati, comando)
                Case "meteo"
                    comando.Tipo = TipoComandoVocale.Meteo
                    comando.Citta = LeggiCampoAi(dati, "citta")
                    Dim giorni As Integer = 0
                    Integer.TryParse(LeggiCampoAi(dati, "giorni_da_oggi"), giorni)
                    If giorni < 0 Then giorni = 0
                    If giorni > 6 Then giorni = 6
                    comando.GiorniDaOggi = giorni
                Case "ora_data"
                    comando.Tipo = TipoComandoVocale.OraData
                Case "apri"
                    comando.Tipo = TipoComandoVocale.ApriSezione
                    comando.Argomento = comando.Argomento.ToLowerInvariant().Replace(" ", "")
                Case "appuntamenti_giorno"
                    comando.Tipo = TipoComandoVocale.AppuntamentiGiorno
                    Dim giornoRichiesto As DateTime
                    If DateTime.TryParseExact(LeggiCampoAi(dati, "data"), "yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.None, giornoRichiesto) Then
                        comando.Inizio = giornoRichiesto.Date
                    Else
                        comando.Inizio = DateTime.Today
                    End If
                Case "cerca_video"
                    comando.Tipo = TipoComandoVocale.CercaVideo
                Case "ignora"
                    log.ScriviLog("AssistenteVocale.InterpretaRispostaAi: l'intelligenza artificiale ha giudicato la richiesta non rivolta all'assistente, trascrizione: " & If(risultato.Domanda, ""))
                    risultato.Ignora = True
                    Exit Sub
                Case "chiudi"
                    risultato.ChiudiSessione = True
                    Exit Sub
                Case Else
                    Dim testoRisposta As String = LeggiCampoAi(dati, "risposta")
                    If String.IsNullOrWhiteSpace(testoRisposta) Then testoRisposta = "Non ho una risposta per questa richiesta."
                    risultato.Risposta = PulisciTestoParlato(testoRisposta)
                    Exit Sub
            End Select

            If comando.Tipo = TipoComandoVocale.InviaEmail Then
                AvviaDialogoEmail(comando, risultato)
                Exit Sub
            End If

            risultato.Comando = comando
        Catch ex As Exception
            log.ScriviLog("AssistenteVocale.InterpretaRispostaAi: " & ex.Message & Environment.NewLine & ex.StackTrace)
            risultato.Risposta = PulisciTestoParlato(contenuto)
        End Try
    End Sub

    Private Shared Function EstraiOggettoJson(testo As String) As String
        Try
            Dim t As String = If(testo, "")
            Dim inizio As Integer = t.IndexOf("{"c)
            Dim fine As Integer = t.LastIndexOf("}"c)
            If inizio < 0 OrElse fine <= inizio Then Return ""
            Return t.Substring(inizio, fine - inizio + 1)
        Catch ex As Exception
            log.ScriviLog("AssistenteVocale.EstraiOggettoJson: " & ex.Message)
            Return ""
        End Try
    End Function

    Private Shared Function PulisciTestoParlato(testo As String) As String
        Try
            Dim t As String = If(testo, "")
            t = Regex.Replace(t, "\*\*|__|\*|#+\s*|`+", "")
            t = Regex.Replace(t, "^\s*[-•]\s+", "", RegexOptions.Multiline)
            t = Regex.Replace(t, "\[([^\]]+)\]\([^)]+\)", "$1")
            t = Regex.Replace(t, "[ \t]+", " ")
            Return t.Trim()
        Catch ex As Exception
            log.ScriviLog("AssistenteVocale.PulisciTestoParlato: " & ex.Message)
            Return If(testo, "").Trim()
        End Try
    End Function

    Private Function LeggiCampoAi(dati As JObject, nome As String) As String
        Try
            Dim valore As JToken = dati(nome)
            If valore Is Nothing OrElse valore.Type = JTokenType.Null Then Return ""
            Return Convert.ToString(valore, System.Globalization.CultureInfo.InvariantCulture).Trim()
        Catch ex As Exception
            log.ScriviLog("AssistenteVocale.LeggiCampoAi: " & ex.Message)
            Return ""
        End Try
    End Function

    Private Sub LeggiDataOraAi(dati As JObject, comando As ComandoVocale)
        Try
            Dim data As DateTime
            If Not DateTime.TryParseExact(LeggiCampoAi(dati, "data"), "yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.None, data) Then
                comando.Inizio = DateTime.MinValue
                Exit Sub
            End If

            Dim oraInizio As DateTime
            If DateTime.TryParseExact(LeggiCampoAi(dati, "ora_inizio"), "H:mm", System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.None, oraInizio) Then
                comando.Inizio = data.Date.Add(oraInizio.TimeOfDay)
                comando.TuttoIlGiorno = False
                Dim oraFine As DateTime
                If DateTime.TryParseExact(LeggiCampoAi(dati, "ora_fine"), "H:mm", System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.None, oraFine) AndAlso oraFine.TimeOfDay > oraInizio.TimeOfDay Then
                    comando.Fine = data.Date.Add(oraFine.TimeOfDay)
                End If
            Else
                comando.Inizio = data.Date
                comando.Fine = data.Date
                comando.TuttoIlGiorno = True
            End If
        Catch ex As Exception
            log.ScriviLog("AssistenteVocale.LeggiDataOraAi: " & ex.Message)
            comando.Inizio = DateTime.MinValue
        End Try
    End Sub

    Private Sub EseguiComando(comando As ComandoVocale)
        Try
            If comando Is Nothing Then Exit Sub
            Select Case comando.Tipo
                Case TipoComandoVocale.RiproduciVideo
                    Dim menuVideo As New MenuYoutube()
                    If String.IsNullOrWhiteSpace(comando.Argomento) Then
                        menuVideo.avvia()
                    Else
                        menuVideo.RiproduciVideoPerTitolo(comando.Argomento)
                    End If

                Case TipoComandoVocale.CercaVideo
                    Dim menuRicercaVideo As New MenuYoutube()
                    menuRicercaVideo.CercaVideoPerVoce(comando.Argomento)

                Case TipoComandoVocale.AscoltaRadio
                    Dim menuRadioVocale As New MenuRadio()
                    If String.IsNullOrWhiteSpace(comando.Argomento) Then
                        menuRadioVocale.Avvia()
                    Else
                        menuRadioVocale.RiproduciStazionePerNome(comando.Argomento)
                    End If

                Case TipoComandoVocale.InviaEmail
                    If Not comando.OrigineOnline Then
                        Parla(MessaggioMailSoloGemini())
                        Exit Select
                    End If
                    Dim posta As New MenuPosta()
                    posta.ComandoInviaEmail(comando.Destinatario, comando.Oggetto, comando.Corpo, comando.InvioConfermato AndAlso Not comando.ApriPerControllo, comando.ApriPerControllo)

                Case TipoComandoVocale.LeggiPosta
                    Dim posta As New MenuPosta()
                    posta.ComandoLeggiPostaInArrivo(comando.SoloNonLetti)

                Case TipoComandoVocale.LeggiUltimaEmail
                    Dim posta As New MenuPosta()
                    posta.ComandoLeggiUltimoMessaggio(comando.SoloNonLetti)

                Case TipoComandoVocale.NuovoAppuntamento
                    Dim agendaVocale As New MenuAgenda()
                    agendaVocale.ComandoNuovoAppuntamento(If(String.IsNullOrWhiteSpace(comando.Argomento), "Appuntamento", comando.Argomento), comando.Inizio, comando.Fine, comando.TuttoIlGiorno)

                Case TipoComandoVocale.AppuntamentiGiorno
                    Dim agendaGiorno As New MenuAgenda()
                    agendaGiorno.ComandoAppuntamentiGiorno(If(comando.Inizio = DateTime.MinValue, DateTime.Today, comando.Inizio.Date))

                Case TipoComandoVocale.Meteo
                    Dim meteoVocale As New MenuMeteo()
                    If comando.GiorniDaOggi <= 0 Then
                        meteoVocale.ComandoMeteoOggi(comando.Citta)
                    Else
                        meteoVocale.ComandoMeteoGiorno(comando.Citta, comando.GiorniDaOggi)
                    End If

                Case TipoComandoVocale.OraData
                    Dim cultura As System.Globalization.CultureInfo = System.Globalization.CultureInfo.GetCultureInfo("it-IT")
                    Dim adesso As DateTime = DateTime.Now
                    Dim richiesta As String = If(comando.Argomento, "").Trim().ToLowerInvariant()
                    If richiesta = "ora" Then
                        Parla("Sono le " & adesso.ToString("HH:mm", System.Globalization.CultureInfo.InvariantCulture) & ".")
                    ElseIf richiesta = "data" Then
                        Parla("Oggi è " & adesso.ToString("dddd d MMMM yyyy", cultura) & ".")
                    Else
                        Parla("Sono le " & adesso.ToString("HH:mm", System.Globalization.CultureInfo.InvariantCulture) & " di " & adesso.ToString("dddd d MMMM yyyy", cultura) & ".")
                    End If

                Case TipoComandoVocale.ApriSezione
                    ApriSezione(comando.Argomento)

                Case Else
                    Parla("Comando non riconosciuto: " & comando.TestoOriginale)
            End Select
        Catch ex As Exception
            log.ScriviLog("AssistenteVocale.EseguiComando: " & ex.Message & Environment.NewLine & ex.StackTrace)
            MostraMessaggio("Non è stato possibile eseguire il comando.")
        End Try
    End Sub

    Private Sub ApriSezione(sezione As String)
        Try
            Select Case If(sezione, "").ToLowerInvariant()
                Case "posta", "mail", "email"
                    Dim posta As New MenuPosta()
                    posta.Avvia()
                Case "agenda"
                    Dim agendaVocale As New MenuAgenda()
                    agendaVocale.avvia()
                Case "rubrica", "contatti"
                    Dim rubricaVocale As New MenuRubrica()
                    rubricaVocale.Avvia()
                Case "meteo"
                    Dim meteoVocale As New MenuMeteo()
                    meteoVocale.Avvia()
                Case "radio"
                    Dim menuRadioVocale As New MenuRadio()
                    menuRadioVocale.Avvia()
                Case "youtube"
                    Dim menuVideo As New MenuYoutube()
                    menuVideo.avvia()
                Case "ai", "intelligenzaartificiale"
                    Dim menuIntelligenza As New MenuAi()
                    menuIntelligenza.Avvia()
                Case "calendario"
                    Dim calendarioVocale As New MenuCalendario()
                    calendarioVocale.Avvia()
                Case Else
                    MostraMessaggio("Sezione non riconosciuta: " & sezione)
            End Select
        Catch ex As Exception
            log.ScriviLog("AssistenteVocale.ApriSezione: " & ex.Message & Environment.NewLine & ex.StackTrace)
        End Try
    End Sub

    Private Shared Sub MostraMessaggio(testo As String)
        Try
            Dim messaggio As New WGMessaggi()
            messaggio.Messaggio(testo)
            messaggio.ShowDialog()
        Catch ex As Exception
            log.ScriviLog("AssistenteVocale.MostraMessaggio: " & ex.Message & Environment.NewLine & ex.StackTrace)
        End Try
    End Sub

    Private Sub SegnaleAttivazione(stato As Object)
        Try
            Console.Beep(880, 120)
        Catch ex As Exception
            log.ScriviLog("AssistenteVocale.SegnaleAttivazione: " & ex.Message)
        Finally
            Try
                SyncLock _bloccoRiconoscitore
                    If Not _anticipoSessione Then
                        AzzeraSegmentazione()
                        Dim fineAttesa As DateTime = DateTime.Now.AddMilliseconds(MILLISECONDI_DOPO_SEGNALE)
                        If fineAttesa > _ignoraAudioFino Then _ignoraAudioFino = fineAttesa
                    End If
                End SyncLock
                _ultimaVoce = DateTime.Now
            Catch exAttesa As Exception
                log.ScriviLog("AssistenteVocale.SegnaleAttivazione (fine segnale): " & exAttesa.Message)
            End Try
        End Try
    End Sub

    Private Sub SegnaleComando()
        Try
            Console.Beep(660, 90)
        Catch ex As Exception
            log.ScriviLog("AssistenteVocale.SegnaleComando: " & ex.Message)
        End Try
    End Sub

    Private Shared Function SpazioLiberoSufficiente(qualita As QualitaVosk, ByRef messaggio As String) As Boolean
        messaggio = ""
        Try
            Dim necessario As Long = If(qualita = QualitaVosk.Alta, SPAZIO_SCARICAMENTO_ALTA, SPAZIO_SCARICAMENTO_BASSA)
            For Each cartella As String In New String() {Path.GetTempPath(), Application.StartupPath}
                Dim radice As String = Path.GetPathRoot(Path.GetFullPath(cartella))
                If String.IsNullOrEmpty(radice) Then Continue For
                Dim unita As New DriveInfo(radice)
                If unita.IsReady AndAlso unita.AvailableFreeSpace < necessario Then
                    messaggio = "Lo spazio libero sull'unità " & radice & " non basta per il modello vocale in " & DescrizioneQualita(qualita) & ": servono circa " & (necessario \ MEGABYTE).ToString(System.Globalization.CultureInfo.GetCultureInfo("it-IT")) & " megabyte liberi."
                    Return False
                End If
            Next
            Return True
        Catch ex As Exception
            log.ScriviLog("AssistenteVocale.SpazioLiberoSufficiente: " & ex.Message)
            Return True
        End Try
    End Function

    ' Lo scaricamento va avanti nel centro di controllo, con la percentuale e senza attese: l'assistente si ferma solo all'ultimo momento,
    ' per installare i file, e al termine riparte.
    Public Shared Sub ScaricaModello()
        Try
            If _scaricamentoInCorso Then
                MostraMessaggio("Lo scaricamento del modello vocale è già in corso: lo trovi nel centro di controllo.")
                Exit Sub
            End If

            Dim qualita As QualitaVosk = QualitaImpostata()
            Dim motivo As String = ""
            If Not SistemaLocaleSupportato(qualita, motivo) Then
                MostraMessaggio("Il modello vocale in " & DescrizioneQualita(qualita) & " non può funzionare su questo computer: " & motivo & ".")
                Exit Sub
            End If

            Dim messaggioSpazio As String = ""
            If Not SpazioLiberoSufficiente(qualita, messaggioSpazio) Then
                MostraMessaggio(messaggioSpazio)
                Exit Sub
            End If

            _qualitaScaricamento = qualita
            AssicuraMarshalCondiviso()
            ServicePointManager.SecurityProtocol = ServicePointManager.SecurityProtocol Or SecurityProtocolType.Tls12

            Dim scaricaLibrerie As Boolean = Not _libreriaCaricata AndAlso Not LibreriePresenti()
            Dim scaricaBassa As Boolean = qualita = QualitaVosk.Bassa OrElse String.IsNullOrEmpty(PercorsoModelloEffettivo(QualitaVosk.Bassa))
            Dim scaricaAlta As Boolean = qualita = QualitaVosk.Alta

            Dim elementi As New List(Of ElementoModello)()
            If scaricaLibrerie Then elementi.Add(New ElementoModello() With {.Indirizzo = URL_LIBRERIE, .NomeFile = "librerie.zip", .DimensioneMinima = 1000000L, .Estrazione = TipoEstrazioneModello.Zip})
            If scaricaBassa Then elementi.Add(New ElementoModello() With {.Indirizzo = URL_MODELLO, .NomeFile = "modello_bassa.zip", .DimensioneMinima = 10000000L, .Estrazione = TipoEstrazioneModello.Zip})
            If scaricaAlta Then elementi.Add(New ElementoModello() With {.Indirizzo = URL_MODELLO_ALTA, .NomeFile = "modello_alta.zip", .DimensioneMinima = 100000000L, .Estrazione = TipoEstrazioneModello.Zip})

            Dim installa As Func(Of String, String) =
                Function(temporanea As String) As String
                    Return InstallaDaScaricamento(temporanea, qualita, scaricaLibrerie, scaricaBassa, scaricaAlta)
                End Function

            _scaricamentoInCorso = True
            Dim avviato As Boolean = WGCentroDiControllo.GetInstance().AggiungiScaricamentoModelli("vosk", "modello vocale italiano in " & DescrizioneQualita(qualita), elementi, installa, AddressOf ScaricamentoTerminato)
            If Not avviato Then
                _scaricamentoInCorso = False
                Exit Sub
            End If
            If _scaricamentoInCorso Then MostraMessaggio("Ho avviato lo scaricamento del modello vocale italiano in " & DescrizioneQualita(qualita) & ", " & DimensioneScaricamento(qualita) & ". Puoi continuare a lavorare: lo trovi nel centro di controllo, e quando è pronto ti avviso.")
        Catch ex As Exception
            _scaricamentoInCorso = False
            log.ScriviLog("AssistenteVocale.ScaricaModello: " & ex.Message & Environment.NewLine & ex.StackTrace)
        End Try
    End Sub

    ' Cerca nei file scaricati le librerie e i modelli e li mette al loro posto. Restituisce "" se è riuscito. Gira sul thread del centro di controllo.
    Private Shared Function InstallaDaScaricamento(temporanea As String, qualita As QualitaVosk, scaricaLibrerie As Boolean, scaricaBassa As Boolean, scaricaAlta As Boolean) As String
        Try
            Dim cartellaLibrerieScaricate As String = ""
            If scaricaLibrerie Then
                Dim libvosk As String() = Directory.GetFiles(Path.Combine(temporanea, "librerie"), "libvosk.dll", SearchOption.AllDirectories)
                If libvosk.Length = 0 Then Return "Il pacchetto delle librerie vocali non contiene libvosk.dll."
                cartellaLibrerieScaricate = Path.GetDirectoryName(libvosk(0))
            End If

            Dim origineBassa As String = ""
            Dim origineAlta As String = ""
            If scaricaBassa Then
                origineBassa = TrovaCartellaModello(Path.Combine(temporanea, "modello_bassa"))
                If origineBassa = "" Then Return "Il pacchetto del modello vocale in bassa qualità non è valido."
            End If
            If scaricaAlta Then
                origineAlta = TrovaCartellaModello(Path.Combine(temporanea, "modello_alta"))
                If origineAlta = "" Then Return "Il pacchetto del modello vocale in alta qualità non è valido."
            End If

            PubblicaScaricamento(AddressOf FermaPerAggiornamentoSuInterfaccia, Nothing)

            If Not String.IsNullOrEmpty(cartellaLibrerieScaricate) AndAlso Not _libreriaCaricata Then
                If Directory.Exists(CartellaLibrerie()) Then Directory.Delete(CartellaLibrerie(), True)
                Directory.CreateDirectory(CartellaLibrerie())
                For Each fileLibreria As String In Directory.GetFiles(cartellaLibrerieScaricate, "*.dll")
                    File.Copy(fileLibreria, Path.Combine(CartellaLibrerie(), Path.GetFileName(fileLibreria)), True)
                Next
            End If

            If origineBassa <> "" Then InstallaModello(origineBassa, QualitaVosk.Bassa)
            If origineAlta <> "" Then InstallaModello(origineAlta, QualitaVosk.Alta)

            If Not ModelloPresente(qualita) Then Return "Il modello vocale non risulta installato correttamente."
            Return ""
        Catch ex As Exception
            log.ScriviLog("AssistenteVocale.InstallaDaScaricamento: " & ex.Message & Environment.NewLine & ex.StackTrace)
            Return "Installazione del modello vocale non riuscita: " & ex.Message
        End Try
    End Function

    ' La cartella del modello è quella che contiene la sottocartella "am".
    Private Shared Function TrovaCartellaModello(estrazione As String) As String
        If Not Directory.Exists(estrazione) Then Return ""
        For Each cartellaAm As String In Directory.GetDirectories(estrazione, "am", SearchOption.AllDirectories)
            Return Path.GetDirectoryName(cartellaAm)
        Next
        Return ""
    End Function

    Private Shared Sub InstallaModello(origineModello As String, qualita As QualitaVosk)
        Try
            Dim destinazione As String = CartellaModello(qualita)
            If Directory.Exists(destinazione) Then Directory.Delete(destinazione, True)
            CopiaCartella(origineModello, destinazione)
        Catch ex As Exception
            log.ScriviLog("AssistenteVocale.InstallaModello: " & ex.Message & Environment.NewLine & ex.StackTrace)
            Throw
        End Try
    End Sub

    ' Chiamato dal centro di controllo, sul thread dell'interfaccia, quando lo scaricamento finisce: "" se è riuscito, "annullato" o il motivo
    ' dell'errore (che il centro ha già comunicato).
    Private Shared Sub ScaricamentoTerminato(esito As String)
        Try
            _scaricamentoInCorso = False
            If Not String.IsNullOrEmpty(esito) Then
                If Attivo() Then AvviaSeAttivo()
                Exit Sub
            End If

            If Attivo() Then
                Ferma()
                AvviaVerifica(True)
            Else
                MostraMessaggio("Modello vocale in " & DescrizioneQualita(_qualitaScaricamento) & " installato. L'assistente vocale si attiva dalla voce Assistente vocale.")
            End If
        Catch ex As Exception
            log.ScriviLog("AssistenteVocale.ScaricamentoTerminato: " & ex.Message & Environment.NewLine & ex.StackTrace)
        End Try
    End Sub

    Private Shared Sub CopiaCartella(origine As String, destinazione As String)
        Try
            Directory.CreateDirectory(destinazione)
            For Each fileOrigine As String In Directory.GetFiles(origine)
                File.Copy(fileOrigine, Path.Combine(destinazione, Path.GetFileName(fileOrigine)), True)
            Next
            For Each sottocartella As String In Directory.GetDirectories(origine)
                CopiaCartella(sottocartella, Path.Combine(destinazione, Path.GetFileName(sottocartella)))
            Next
        Catch ex As Exception
            log.ScriviLog("AssistenteVocale.CopiaCartella: " & ex.Message & Environment.NewLine & ex.StackTrace)
            Throw
        End Try
    End Sub

    Private Shared Sub PubblicaScaricamento(callback As SendOrPostCallback, stato As Object)
        Try
            If _marshalScaricamento IsNot Nothing AndAlso Not _marshalScaricamento.IsDisposed AndAlso _marshalScaricamento.IsHandleCreated Then
                If _marshalScaricamento.InvokeRequired Then
                    _marshalScaricamento.Invoke(callback, New Object() {stato})
                Else
                    callback(stato)
                End If
            End If
        Catch ex As Exception
            log.ScriviLog("AssistenteVocale.PubblicaScaricamento: " & ex.Message & Environment.NewLine & ex.StackTrace)
        End Try
    End Sub

    Private Shared Sub FermaPerAggiornamentoSuInterfaccia(stato As Object)
        Try
            Ferma()
        Catch ex As Exception
            log.ScriviLog("AssistenteVocale.FermaPerAggiornamentoSuInterfaccia: " & ex.Message)
        End Try
    End Sub

End Class
