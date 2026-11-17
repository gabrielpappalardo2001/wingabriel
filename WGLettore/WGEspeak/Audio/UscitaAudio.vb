' ============================================================================================
' UscitaAudio - l'uscita audio di WinGabriel a bassa latenza
' ============================================================================================
'
' Suona voce (mono, 16 bit) nel modo più rapido che il sistema permette, scegliendo da sola:
'   - Windows 10 e 11: WASAPI a bassa latenza (IAudioClient3), con il periodo più corto del motore audio;
'   - Windows Vista e successivi, o se la bassa latenza non parte: WASAPI normale, con eventi;
'   - dove WASAPI non c'è o non parte: le vecchie API di Windows (WinMM, waveOut).
' La conversione alla frequenza e al formato della scheda la fa questa libreria, prima di dare l'audio
' a Windows, così il motore audio non deve convertire. Non usa librerie esterne.
'
'   Dim uscita As UscitaAudio = UscitaAudio.Apri(22050)
'   If uscita IsNot Nothing Then
'       uscita.Scrivi(campioniPcm16, 0, campioniPcm16.Length)   ' si può chiamare a pezzi, anche piccoli
'       ...
'       uscita.Svuota()                                          ' taglia subito quello che sta suonando
'       uscita.MillisecondiInCoda()                              ' quanto audio deve ancora suonare
'       uscita.Dispose()
'   End If
'   If Not uscita.Valida Then ...                                ' la scheda è cambiata o scollegata: riaprire
' ============================================================================================
Imports System.Threading

''' <summary>Quale sistema usare; Automatica sceglie il migliore disponibile.</summary>
Public Enum PreferenzaUscita
    Automatica = 0
    WasapiBassaLatenza = 1
    Wasapi = 2
    WinMm = 3
End Enum

''' <summary>Quello che sta sotto l'uscita: un sistema audio di Windows e il thread che lo alimenta.</summary>
Friend MustInherit Class MotoreUscita
    Implements IDisposable

    ''' <summary>La coda da cui il thread dell'uscita prende i campioni, alla frequenza dell'uscita.</summary>
    Friend Coda As CodaCampioni
    ''' <summary>La frequenza a cui l'uscita vuole i campioni.</summary>
    Friend FrequenzaUscita As Integer
    ''' <summary>Perché, se non è partito.</summary>
    Friend Diagnosi As String = ""

    Friend MustOverride ReadOnly Property Nome As String
    Friend MustOverride ReadOnly Property LatenzaMs As Double
    Friend MustOverride ReadOnly Property Valido As Boolean
    Friend MustOverride ReadOnly Property MillisecondiInHardware As Integer
    Friend MustOverride Function Apri(frequenzaIngresso As Integer) As Boolean
    ''' <summary>Chiede al thread audio di tagliare il buffer della scheda. Vuoto se il motore non ne ha uno.</summary>
    Friend Overridable Sub SvuotaHardware()
    End Sub
    ''' <summary>La prossima voce, se in hardware c'è solo silenzio, deve partire dall'inizio del buffer.</summary>
    Friend Overridable Sub RichiediAvvio()
    End Sub
    Public MustOverride Sub Dispose() Implements IDisposable.Dispose
End Class

Public NotInheritable Class UscitaAudio
    Implements IDisposable

    Private ReadOnly _motore As MotoreUscita
    Private ReadOnly _campionatore As CampionatoreCubico
    Private ReadOnly _frequenzaIngresso As Integer
    Private ReadOnly _bloccoScrittura As New Object()
    Private _chiusa As Boolean

    Private Shared _diagnosi As String = ""

    ''' <summary>Se nessun sistema è partito, o se uno è partito dopo altri che non sono riusciti, perché.</summary>
    Public Shared ReadOnly Property Diagnosi As String
        Get
            Return _diagnosi
        End Get
    End Property

    Private Sub New(motore As MotoreUscita, frequenzaIngresso As Integer)
        _motore = motore
        _frequenzaIngresso = frequenzaIngresso
        If motore.FrequenzaUscita <> frequenzaIngresso Then _campionatore = New CampionatoreCubico(frequenzaIngresso, motore.FrequenzaUscita)
    End Sub

    ''' <summary>
    ''' Apre l'uscita per audio mono a 16 bit alla frequenza indicata. Restituisce Nothing se nessun sistema audio
    ''' parte (Diagnosi dice perché). Con una preferenza diversa da Automatica prova solo quel sistema.
    ''' </summary>
    Public Shared Function Apri(frequenzaIngresso As Integer, Optional preferenza As PreferenzaUscita = PreferenzaUscita.Automatica) As UscitaAudio
        _diagnosi = ""
        Dim candidati As New List(Of Func(Of MotoreUscita))()
        Select Case preferenza
            Case PreferenzaUscita.WasapiBassaLatenza
                candidati.Add(Function() New UscitaWasapi(True))
            Case PreferenzaUscita.Wasapi
                candidati.Add(Function() New UscitaWasapi(False))
            Case PreferenzaUscita.WinMm
                candidati.Add(Function() New UscitaWinMm())
            Case Else
                Dim versione As Version = VersioneWindows()
                If versione.Major >= 10 Then candidati.Add(Function() New UscitaWasapi(True))
                If versione.Major >= 6 Then candidati.Add(Function() New UscitaWasapi(False))
                candidati.Add(Function() New UscitaWinMm())
        End Select

        For Each crea As Func(Of MotoreUscita) In candidati
            Dim motore As MotoreUscita = Nothing
            Try
                motore = crea()
                If motore.Apri(frequenzaIngresso) Then
                    _diagnosi &= motore.Diagnosi
                    Return New UscitaAudio(motore, frequenzaIngresso)
                End If
                _diagnosi &= motore.Diagnosi & " "
                motore.Dispose()
            Catch ex As Exception
                _diagnosi &= If(motore Is Nothing, "", motore.Nome & ": ") & ex.Message & " "
                Try
                    If motore IsNot Nothing Then motore.Dispose()
                Catch
                End Try
            End Try
        Next
        Return Nothing
    End Function

    ''' <summary>Il sistema in uso: "WASAPI a bassa latenza", "WASAPI" o "WinMM".</summary>
    Public ReadOnly Property Nome As String
        Get
            Return _motore.Nome
        End Get
    End Property

    ''' <summary>Il ritardo di base dell'uscita, in millisecondi (il periodo del motore audio).</summary>
    Public ReadOnly Property LatenzaMs As Double
        Get
            Return _motore.LatenzaMs
        End Get
    End Property

    ''' <summary>La frequenza a cui suona la scheda (quella che Windows usa per mescolare i suoni).</summary>
    Public ReadOnly Property FrequenzaUscita As Integer
        Get
            Return _motore.FrequenzaUscita
        End Get
    End Property

    ''' <summary>Falso se la scheda è stata scollegata o è cambiata quella predefinita: l'uscita va chiusa e riaperta.</summary>
    Public ReadOnly Property Valida As Boolean
        Get
            Return Not _chiusa AndAlso _motore.Valido
        End Get
    End Property

    ''' <summary>
    ''' Aggiunge audio mono a 16 bit (little endian, come quello di eSpeak). Si può chiamare a pezzi di qualsiasi
    ''' misura. Falso se la coda è piena o l'uscita non è valida.
    ''' </summary>
    Public Function Scrivi(pcm16 As Byte(), offset As Integer, quanti As Integer) As Boolean
        If _chiusa OrElse Not _motore.Valido Then Return False
        Dim campioni As Integer = quanti \ 2
        If campioni <= 0 Then Return True
        Dim valori(campioni - 1) As Single
        For i As Integer = 0 To campioni - 1
            valori(i) = BitConverter.ToInt16(pcm16, offset + i * 2) / 32768.0F
        Next
        SyncLock _bloccoScrittura
            Dim daScrivere As Single() = valori
            Dim numero As Integer = campioni
            If _campionatore IsNot Nothing Then
                daScrivere = _campionatore.Converti(valori, campioni)
                numero = daScrivere.Length
            End If
            Dim scritto As Boolean = _motore.Coda.Scrivi(daScrivere, numero) = numero
            ' Buffer pieno di silenzio: senza un riavvio la voce partiva solo dopo quel silenzio.
            If scritto AndAlso _motore.MillisecondiInHardware = 0 Then _motore.RichiediAvvio()
            Return scritto
        End SyncLock
    End Function

    ''' <summary>Taglia quello che sta suonando e quello che aspetta: dopo pochi millisecondi c'è silenzio.</summary>
    Public Sub Svuota()
        SyncLock _bloccoScrittura
            If _campionatore IsNot Nothing Then _campionatore.Azzera()
            _motore.Coda.Azzera()
            _motore.SvuotaHardware()
        End SyncLock
    End Sub

    ''' <summary>Quanto audio deve ancora suonare, in millisecondi: la coda e quello già dato a Windows.</summary>
    Public Function MillisecondiInCoda() As Integer
        Dim frequenza As Integer = Math.Max(1, _motore.FrequenzaUscita)
        Return CInt(_motore.Coda.Conteggio * 1000L \ frequenza) + _motore.MillisecondiInHardware
    End Function

    Public Sub Dispose() Implements IDisposable.Dispose
        If _chiusa Then Return
        _chiusa = True
        _motore.Dispose()
    End Sub

End Class
