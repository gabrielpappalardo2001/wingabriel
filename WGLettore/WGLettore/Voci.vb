Imports System.IO
Imports System.Runtime.CompilerServices
Imports System.Runtime.InteropServices
Imports System.Text
Imports System.Text.RegularExpressions
Imports System.Threading
Imports WGEspeakSintesi
Imports WGLettoreSchermo

''' <summary>Voce che il programma può configurare e chiudere, oltre a farla parlare.</summary>
Friend Interface IVoceGestita
    Sub Configura()
    Sub Termina()
End Interface

''' <summary>Il lettore parla sempre con una sua voce: eSpeak oppure le voci di Windows, secondo la scelta nelle impostazioni.</summary>
Friend Module SceltaVoce

    Friend Function Crea(sicuro As Boolean) As IVoceLettore
        Return New VoceInstradata()
    End Function

End Module

''' <summary>La voce propria del lettore, quella del programma WGLettoreVoce.</summary>
Friend NotInheritable Class VoceNativa
    Implements IVoceLettore
    Implements IVoceGestita

    Public Event OnFineLettura() Implements IVoceLettore.OnFineLettura
    Public Event OnStatoParlatoCambiato(staParlando As Boolean) Implements IVoceLettore.OnStatoParlatoCambiato

    Private ReadOnly _blocco As New Object()
    Private _processo As Process
    Private _scrittore As StreamWriter
    Private _parla As Integer
    Private _provatoSenzaEsito As Boolean

    Public ReadOnly Property StaParlando As Boolean Implements IVoceLettore.StaParlando
        Get
            Return Volatile.Read(_parla) = 1
        End Get
    End Property

    Public Sub LeggiTesto(testo As String, Optional priorita As String = "Normale", Optional escludiPunteggiatura As Boolean = False) Implements IVoceLettore.LeggiTesto
        Try
            If String.IsNullOrWhiteSpace(testo) Then Return
            Dim interrompi As Boolean = String.Equals(priorita, "Alta", StringComparison.OrdinalIgnoreCase)
            Dim pulito As String = testo.Replace(vbCr, " ").Replace(vbLf, " ")
            Manda("PARLA " & If(interrompi, "1", "0") & " " & pulito)
        Catch ex As Exception
            Trace.WriteLine("VoceNativa.LeggiTesto: " & ex.Message)
        End Try
    End Sub

    Public Sub Ferma() Implements IVoceLettore.Ferma
        Manda("FERMA")
    End Sub

    Public Sub Configura() Implements IVoceGestita.Configura
        Try
            Dim velocita As Integer = VelocitaSapi()
            Dim volume As Integer = VolumeSapi()
            Manda("VELOCITA " & velocita.ToString())
            Manda("VOLUME " & volume.ToString())
            Manda("VOCE " & ConfigLettore.Leggi("lettore_schermo_voce_nome", "").Trim())
        Catch ex As Exception
            Trace.WriteLine("VoceNativa.Configura: " & ex.Message)
        End Try
    End Sub

    ''' <summary>
    ''' Velocità da -10 a 10. Se nelle impostazioni del lettore non è stata scelta, segue la velocità di WinGabriel (SapiRate, da 0 a 100)
    ''' con lo stesso calcolo di WGSapi, così passando da una voce all'altra la velocità non cambia.
    ''' </summary>
    Private Shared Function VelocitaSapi() As Integer
        Try
            Dim valore As Integer
            If Integer.TryParse(ConfigLettore.Leggi("lettore_schermo_voce_velocita", "").Trim(), valore) Then Return Math.Max(-10, Math.Min(10, valore))
            Dim percentuale As Integer = 50
            Integer.TryParse(ConfigLettore.Leggi("SapiRate", "").Trim(), percentuale)
            percentuale = Math.Max(0, Math.Min(100, percentuale))
            Return Math.Max(-10, Math.Min(10, (percentuale \ 5) - 10))
        Catch ex As Exception
            Trace.WriteLine("VoceNativa.VelocitaSapi: " & ex.Message)
            Return 0
        End Try
    End Function

    Private Shared Function VolumeSapi() As Integer
        Try
            Dim valore As Integer
            If Integer.TryParse(ConfigLettore.Leggi("lettore_schermo_voce_volume", "").Trim(), valore) Then Return Math.Max(0, Math.Min(100, valore))
            If Integer.TryParse(ConfigLettore.Leggi("SapiVolume", "").Trim(), valore) Then Return Math.Max(0, Math.Min(100, valore))
            Return 100
        Catch ex As Exception
            Trace.WriteLine("VoceNativa.VolumeSapi: " & ex.Message)
            Return 100
        End Try
    End Function

    Private Shared Function LeggiIntero(chiave As String, predefinito As Integer) As Integer
        Try
            Dim valore As Integer
            If Integer.TryParse(ConfigLettore.Leggi(chiave, ""), valore) Then Return valore
            Return predefinito
        Catch ex As Exception
            Trace.WriteLine("VoceNativa.LeggiIntero: " & ex.Message)
            Return predefinito
        End Try
    End Function

    Private Sub Manda(riga As String)
        Try
            SyncLock _blocco
                If Not Assicura() Then Return
                _scrittore.WriteLine(riga)
                _scrittore.Flush()
            End SyncLock
        Catch ex As Exception
            Trace.WriteLine("VoceNativa.Manda: " & ex.Message)
            Chiudi()
        End Try
    End Sub

    Private Function Assicura() As Boolean
        If _processo IsNot Nothing AndAlso Not _processo.HasExited Then Return True
        If _provatoSenzaEsito Then Return False
        Dim exe As String = Path.Combine(NativoApp.CartellaProgramma(), "WGLettoreVoce.exe")
        If Not File.Exists(exe) Then
            _provatoSenzaEsito = True
            Return False
        End If
        Try
            Dim info As New ProcessStartInfo(exe)
            info.UseShellExecute = False
            info.CreateNoWindow = True
            info.RedirectStandardInput = True
            info.RedirectStandardOutput = True
            info.StandardOutputEncoding = Encoding.UTF8
            _processo = Process.Start(info)
            _scrittore = New StreamWriter(_processo.StandardInput.BaseStream, New UTF8Encoding(False))
            _scrittore.AutoFlush = True
            Dim lettore As New Thread(AddressOf LeggiRisposte)
            lettore.IsBackground = True
            lettore.Name = "WGLettoreVoceRisposte"
            lettore.Start(_processo)
            _scrittore.WriteLine("VELOCITA " & VelocitaSapi().ToString())
            _scrittore.WriteLine("VOLUME " & VolumeSapi().ToString())
            _scrittore.WriteLine("VOCE " & ConfigLettore.Leggi("lettore_schermo_voce_nome", "").Trim())
            Return True
        Catch ex As Exception
            Trace.WriteLine("VoceNativa.Assicura: " & ex.Message)
            _provatoSenzaEsito = True
            Chiudi()
            Return False
        End Try
    End Function

    Private Sub LeggiRisposte(stato As Object)
        Dim processo As Process = DirectCast(stato, Process)
        Try
            Dim lettore As New StreamReader(processo.StandardOutput.BaseStream, Encoding.UTF8)
            While True
                Dim riga As String = lettore.ReadLine()
                If riga Is Nothing Then Exit While
                If riga.StartsWith("STATO ", StringComparison.Ordinal) Then
                    Dim parla As Boolean = riga.EndsWith("1", StringComparison.Ordinal)
                    Volatile.Write(_parla, If(parla, 1, 0))
                    RaiseEvent OnStatoParlatoCambiato(parla)
                ElseIf riga = "FINE" Then
                    RaiseEvent OnFineLettura()
                End If
            End While
        Catch ex As Exception
            Trace.WriteLine("VoceNativa.LeggiRisposte: " & ex.Message)
        End Try
        Volatile.Write(_parla, 0)
    End Sub

    Private Sub Chiudi()
        SyncLock _blocco
            Try
                If _processo IsNot Nothing AndAlso Not _processo.HasExited Then _processo.Kill()
            Catch ex As Exception
                Trace.WriteLine("VoceNativa.Chiudi: " & ex.Message)
            End Try
            _processo = Nothing
            _scrittore = Nothing
        End SyncLock
    End Sub

    Public Sub Termina() Implements IVoceGestita.Termina
        Try
            SyncLock _blocco
                If _scrittore IsNot Nothing Then _scrittore.WriteLine("ESCI")
            End SyncLock
        Catch ex As Exception
            Trace.WriteLine("VoceNativa.Termina: " & ex.Message)
        End Try
        Chiudi()
    End Sub

End Class

''' <summary>Passa il parlato alla voce scelta (eSpeak o Windows) e la cambia quando cambiano le impostazioni, senza che il resto del lettore se ne accorga.</summary>
Friend NotInheritable Class VoceInstradata
    Implements IVoceLettore
    Implements IVoceGestita

    Public Event OnFineLettura() Implements IVoceLettore.OnFineLettura
    Public Event OnStatoParlatoCambiato(staParlando As Boolean) Implements IVoceLettore.OnStatoParlatoCambiato

    Private ReadOnly _nativa As New VoceNativa()
    Private ReadOnly _espeak As New VoceEspeakLettore()
    Private _usaEspeak As Boolean

    Public Sub New()
        AddHandler _nativa.OnFineLettura, Sub() RaiseEvent OnFineLettura()
        AddHandler _nativa.OnStatoParlatoCambiato, Sub(b As Boolean) RaiseEvent OnStatoParlatoCambiato(b)
        AddHandler _espeak.OnFineLettura, Sub() RaiseEvent OnFineLettura()
        AddHandler _espeak.OnStatoParlatoCambiato, Sub(b As Boolean) RaiseEvent OnStatoParlatoCambiato(b)
        _usaEspeak = ScegliEspeak()
    End Sub

    ' eSpeak solo se è la sintesi scelta e i suoi file ci sono: altrimenti le voci di Windows.
    Private Shared Function ScegliEspeak() As Boolean
        Try
            Return String.Equals(ConfigLettore.Leggi("lettore_schermo_motore", "windows").Trim(), "espeak", StringComparison.OrdinalIgnoreCase) AndAlso WGEspeak.Installato()
        Catch ex As Exception
            Trace.WriteLine("VoceInstradata.ScegliEspeak: " & ex.Message)
            Return False
        End Try
    End Function

    Public ReadOnly Property StaParlando As Boolean Implements IVoceLettore.StaParlando
        Get
            Return If(_usaEspeak, _espeak.StaParlando, _nativa.StaParlando)
        End Get
    End Property

    Public Sub LeggiTesto(testo As String, Optional priorita As String = "Normale", Optional escludiPunteggiatura As Boolean = False) Implements IVoceLettore.LeggiTesto
        If _usaEspeak Then
            _espeak.LeggiTesto(testo, priorita, escludiPunteggiatura)
        Else
            _nativa.LeggiTesto(testo, priorita, escludiPunteggiatura)
        End If
    End Sub

    Public Sub Ferma() Implements IVoceLettore.Ferma
        If _usaEspeak Then _espeak.Ferma() Else _nativa.Ferma()
    End Sub

    Public Sub Configura() Implements IVoceGestita.Configura
        Try
            Dim nuovo As Boolean = ScegliEspeak()
            If nuovo <> _usaEspeak Then
                If _usaEspeak Then _espeak.Ferma() Else _nativa.Ferma()
                _usaEspeak = nuovo
            End If
            If _usaEspeak Then _espeak.Configura() Else _nativa.Configura()
        Catch ex As Exception
            Trace.WriteLine("VoceInstradata.Configura: " & ex.Message)
        End Try
    End Sub

    Public Sub Termina() Implements IVoceGestita.Termina
        Try
            _espeak.Termina()
        Catch ex As Exception
            Trace.WriteLine("VoceInstradata.Termina (eSpeak): " & ex.Message)
        End Try
        _nativa.Termina()
    End Sub

End Class

' La voce di eSpeak, con la libreria WGEspeak: parla dentro il programma, senza processi a parte.
Friend NotInheritable Class VoceEspeakLettore
    Implements IVoceLettore
    Implements IVoceGestita

    Public Event OnFineLettura() Implements IVoceLettore.OnFineLettura
    Public Event OnStatoParlatoCambiato(staParlando As Boolean) Implements IVoceLettore.OnStatoParlatoCambiato

    Private Shared ReadOnly _puntoNelNome As New Regex("(?<=[\p{L}\p{N}])\.(?=\p{L})", RegexOptions.Compiled)

    Private ReadOnly _blocco As New Object()
    Private _motore As WGMotoreEspeak
    Private _id As Integer
    Private _parla As Integer

    Public ReadOnly Property StaParlando As Boolean Implements IVoceLettore.StaParlando
        Get
            Return Volatile.Read(_parla) = 1
        End Get
    End Property

    Private Function Motore() As WGMotoreEspeak
        SyncLock _blocco
            If _motore IsNot Nothing Then Return _motore
            Dim nuovo As WGMotoreEspeak = WGMotoreEspeak.Istanza()
            If nuovo Is Nothing Then Return Nothing
            AddHandler nuovo.FineMessaggio, AddressOf AlFineMessaggio
            _motore = nuovo
            Applica(nuovo)
            Return _motore
        End SyncLock
    End Function

    Public Sub LeggiTesto(testo As String, Optional priorita As String = "Normale", Optional escludiPunteggiatura As Boolean = False) Implements IVoceLettore.LeggiTesto
        Try
            If String.IsNullOrWhiteSpace(testo) Then Return
            Dim motore As WGMotoreEspeak = Me.Motore()
            If motore Is Nothing Then Return
            Dim pulito As String = _puntoNelNome.Replace(testo.Replace(vbCr, " ").Replace(vbLf, " "), " punto ")
            Dim id As Integer = Interlocked.Increment(_id)
            ImpostaParla(True)
            motore.Accoda(id, True, pulito)
        Catch ex As Exception
            Trace.WriteLine("VoceEspeak.LeggiTesto: " & ex.Message)
        End Try
    End Sub

    Private Sub AlFineMessaggio(messaggioId As Integer)
        Try
            If messaggioId <> Volatile.Read(_id) Then Return
            ImpostaParla(False)
            RaiseEvent OnFineLettura()
        Catch ex As Exception
            Trace.WriteLine("VoceEspeak.AlFineMessaggio: " & ex.Message)
        End Try
    End Sub

    Private Sub ImpostaParla(parla As Boolean)
        Dim nuovo As Integer = If(parla, 1, 0)
        If Interlocked.Exchange(_parla, nuovo) <> nuovo Then RaiseEvent OnStatoParlatoCambiato(parla)
    End Sub

    Public Sub Ferma() Implements IVoceLettore.Ferma
        Try
            Interlocked.Increment(_id)
            Dim motore As WGMotoreEspeak = _motore
            If motore IsNot Nothing Then motore.Ferma()
            ImpostaParla(False)
        Catch ex As Exception
            Trace.WriteLine("VoceEspeak.Ferma: " & ex.Message)
        End Try
    End Sub

    Public Sub Configura() Implements IVoceGestita.Configura
        Try
            Dim motore As WGMotoreEspeak = Me.Motore()
            If motore IsNot Nothing Then Applica(motore)
        Catch ex As Exception
            Trace.WriteLine("VoceEspeak.Configura: " & ex.Message)
        End Try
    End Sub

    ' Velocità da -10 a 10 come per le voci di Windows, portata alla scala 0-100 di eSpeak.
    Private Shared Sub Applica(motore As WGMotoreEspeak)
        Try
            Dim velocita As Integer = 3
            Integer.TryParse(ConfigLettore.Leggi("lettore_schermo_voce_velocita", "").Trim(), velocita)
            velocita = Math.Max(-10, Math.Min(10, velocita))
            motore.ImpostaVelocita(50 + velocita * 5)
            Dim volume As Integer = 100
            If Integer.TryParse(ConfigLettore.Leggi("lettore_schermo_voce_volume", "").Trim(), volume) Then volume = Math.Max(0, Math.Min(100, volume)) Else volume = 100
            motore.ImpostaVolume(volume)
            Dim identificatore As String = ConfigLettore.Leggi("lettore_schermo_voce_espeak", "it").Trim()
            Dim voci As List(Of VoceEspeak) = motore.Voci()
            For indice As Integer = 0 To voci.Count - 1
                If String.Equals(voci(indice).Identificatore, identificatore, StringComparison.OrdinalIgnoreCase) Then
                    motore.ImpostaVoce(indice.ToString())
                    Exit For
                End If
            Next
        Catch ex As Exception
            Trace.WriteLine("VoceEspeak.Applica: " & ex.Message)
        End Try
    End Sub

    Public Sub Termina() Implements IVoceGestita.Termina
        Ferma()
    End Sub

End Class
