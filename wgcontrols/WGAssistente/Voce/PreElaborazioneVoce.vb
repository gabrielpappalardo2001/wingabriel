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

' Elaborazione preliminare del flusso audio con riduzione del rumore, filtri e normalizzazione.
Public Class PreElaborazioneVoce
    Implements IDisposable

    Private Const FREQUENZA As Double = 16000.0
    Private Const FREQUENZA_TAGLIO As Double = 90.0
    Private Const CAMPIONI_BLOCCO As Integer = 128
    Private Const CAMPIONI_BLOCCO_FRASE As Integer = 320
    Private Const RMS_OBIETTIVO As Double = 0.1
    Private Const GUADAGNO_MINIMO As Double = 0.5
    Private Const GUADAGNO_MASSIMO As Double = 10.0
    Private Const GUADAGNO_MASSIMO_FRASE As Double = 12.0
    Private Const PICCO_MASSIMO As Double = 0.95
    Private Const RMS_OBIETTIVO_PARAKEET As Double = 0.05
    Private Const GUADAGNO_MINIMO_PARAKEET As Double = 1.0
    Private Const GUADAGNO_MASSIMO_PARAKEET As Double = 4.0
    Private Const PICCO_MASSIMO_PARAKEET As Double = 0.9
    Private Const RMS_MINIMO_VOCE As Double = 0.004
    Private Const RUMORE_ATTIVA_RIDUZIONE As Double = 0.008
    Private Const RUMORE_DISATTIVA_RIDUZIONE As Double = 0.005
    Private Const PAVIMENTO_INIZIALE As Double = 0.003

    Private _riduttore As RiduttoreRumore
    Private ReadOnly _coefficiente As Double
    Private _ingressoPrecedente As Double = 0.0
    Private _uscitaPrecedente As Double = 0.0
    Private _guadagno As Double = 1.0
    Private _pavimentoRumore As Double = PAVIMENTO_INIZIALE
    Private _riduzioneAttiva As Boolean = False

    Public Sub New(riduttore As RiduttoreRumore)
        Try
            _riduttore = riduttore
            _coefficiente = 1.0 / (1.0 + 2.0 * Math.PI * FREQUENZA_TAGLIO / FREQUENZA)
        Catch ex As Exception
            log.ScriviLog("PreElaborazioneVoce.New: " & ex.Message)
        End Try
    End Sub

    Public ReadOnly Property RiduzioneDisponibile As Boolean
        Get
            Return _riduttore IsNot Nothing AndAlso _riduttore.Pronto
        End Get
    End Property

    Public Sub Azzera()
        Try
            _ingressoPrecedente = 0.0
            _uscitaPrecedente = 0.0
            _guadagno = 1.0
            _pavimentoRumore = PAVIMENTO_INIZIALE
            _riduzioneAttiva = False
            If RiduzioneDisponibile Then _riduttore.Azzera()
        Catch ex As Exception
            log.ScriviLog("PreElaborazioneVoce.Azzera: " & ex.Message)
        End Try
    End Sub

    Private Shared Function InCampioni(buffer As Byte(), lunghezza As Integer) As Single()
        Try
            Dim numero As Integer = Math.Max(0, Math.Min(lunghezza, buffer.Length) \ 2)
            Dim campioni(numero - 1) As Single
            For indice As Integer = 0 To numero - 1
                campioni(indice) = BitConverter.ToInt16(buffer, indice * 2) / 32768.0F
            Next
            Return campioni
        Catch ex As Exception
            log.ScriviLog("PreElaborazioneVoce.InCampioni: " & ex.Message)
            Return New Single() {}
        End Try
    End Function

    Private Shared Function InByte(campioni As Single()) As Byte()
        Try
            Dim risultato(campioni.Length * 2 - 1) As Byte
            For indice As Integer = 0 To campioni.Length - 1
                Dim valore As Double = Math.Max(-1.0, Math.Min(1.0, CDbl(campioni(indice))))
                Dim intero As Short = CShort(Math.Max(-32768.0, Math.Min(32767.0, Math.Round(valore * 32767.0))))
                risultato(indice * 2) = CByte(intero And &HFF)
                risultato(indice * 2 + 1) = CByte((intero >> 8) And &HFF)
            Next
            Return risultato
        Catch ex As Exception
            log.ScriviLog("PreElaborazioneVoce.InByte: " & ex.Message)
            Return New Byte() {}
        End Try
    End Function

    Private Function FiltraPassaAlto(campioni As Single(), ByRef ingressoPrecedente As Double, ByRef uscitaPrecedente As Double) As Single()
        Try
            Dim risultato(campioni.Length - 1) As Single
            For indice As Integer = 0 To campioni.Length - 1
                Dim ingresso As Double = campioni(indice)
                Dim uscita As Double = _coefficiente * (uscitaPrecedente + ingresso - ingressoPrecedente)
                ingressoPrecedente = ingresso
                uscitaPrecedente = uscita
                risultato(indice) = CSng(uscita)
            Next
            Return risultato
        Catch ex As Exception
            log.ScriviLog("PreElaborazioneVoce.FiltraPassaAlto: " & ex.Message)
            Return campioni
        End Try
    End Function

    Private Shared Function Rms(campioni As Single(), inizio As Integer, quanti As Integer) As Double
        Try
            If quanti <= 0 Then Return 0.0
            Dim somma As Double = 0.0
            For indice As Integer = inizio To inizio + quanti - 1
                somma += CDbl(campioni(indice)) * campioni(indice)
            Next
            Return Math.Sqrt(somma / quanti)
        Catch ex As Exception
            log.ScriviLog("PreElaborazioneVoce.Rms: " & ex.Message)
            Return 0.0
        End Try
    End Function

    Private Sub AggiornaPavimento(campioni As Single())
        Try
            Dim posizione As Integer = 0
            While posizione < campioni.Length
                Dim quanti As Integer = Math.Min(CAMPIONI_BLOCCO, campioni.Length - posizione)
                Dim valore As Double = Rms(campioni, posizione, quanti)
                If valore < _pavimentoRumore Then
                    _pavimentoRumore = _pavimentoRumore * 0.9 + valore * 0.1
                Else
                    _pavimentoRumore = _pavimentoRumore * 0.999 + valore * 0.001
                End If
                If _pavimentoRumore < 0.00001 Then _pavimentoRumore = 0.00001
                posizione += quanti
            End While
        Catch ex As Exception
            log.ScriviLog("PreElaborazioneVoce.AggiornaPavimento: " & ex.Message)
        End Try
    End Sub

    Private Sub ApplicaGuadagnoAutomatico(campioni As Single())
        Try
            Dim posizione As Integer = 0
            While posizione < campioni.Length
                Dim quanti As Integer = Math.Min(CAMPIONI_BLOCCO, campioni.Length - posizione)
                Dim valore As Double = Rms(campioni, posizione, quanti)
                If valore > Math.Max(_pavimentoRumore * 3.0, RMS_MINIMO_VOCE) Then
                    Dim desiderato As Double = Math.Max(GUADAGNO_MINIMO, Math.Min(GUADAGNO_MASSIMO, RMS_OBIETTIVO / valore))
                    If desiderato < _guadagno Then
                        _guadagno += (desiderato - _guadagno) * 0.3
                    Else
                        _guadagno += (desiderato - _guadagno) * 0.05
                    End If
                End If
                For indice As Integer = posizione To posizione + quanti - 1
                    campioni(indice) = CSng(Math.Max(-PICCO_MASSIMO, Math.Min(PICCO_MASSIMO, campioni(indice) * _guadagno)))
                Next
                posizione += quanti
            End While
        Catch ex As Exception
            log.ScriviLog("PreElaborazioneVoce.ApplicaGuadagnoAutomatico: " & ex.Message)
        End Try
    End Sub

    Public Function ElaboraFlusso(buffer As Byte(), lunghezza As Integer) As Byte()
        Try
            If buffer Is Nothing OrElse lunghezza <= 1 Then Return New Byte() {}
            Dim campioni As Single() = FiltraPassaAlto(InCampioni(buffer, lunghezza), _ingressoPrecedente, _uscitaPrecedente)
            AggiornaPavimento(campioni)

            If RiduzioneDisponibile Then
                If Not _riduzioneAttiva AndAlso _pavimentoRumore >= RUMORE_ATTIVA_RIDUZIONE Then
                    _riduzioneAttiva = True
                    _riduttore.Azzera()
                ElseIf _riduzioneAttiva AndAlso _pavimentoRumore < RUMORE_DISATTIVA_RIDUZIONE Then
                    _riduzioneAttiva = False
                End If
                If _riduzioneAttiva Then campioni = _riduttore.Elabora(campioni)
            End If

            ApplicaGuadagnoAutomatico(campioni)
            Return InByte(campioni)
        Catch ex As Exception
            log.ScriviLog("PreElaborazioneVoce.ElaboraFlusso: " & ex.Message & Environment.NewLine & ex.StackTrace)
            Try
                Dim copia(lunghezza - 1) As Byte
                Array.Copy(buffer, copia, lunghezza)
                Return copia
            Catch exCopia As Exception
                log.ScriviLog("PreElaborazioneVoce.ElaboraFlusso (copia): " & exCopia.Message)
                Return New Byte() {}
            End Try
        End Try
    End Function

    Private Shared Function StimaRumore(campioni As Single()) As Double
        Try
            Dim valori As New List(Of Double)
            Dim posizione As Integer = 0
            While posizione + CAMPIONI_BLOCCO_FRASE <= campioni.Length
                valori.Add(Rms(campioni, posizione, CAMPIONI_BLOCCO_FRASE))
                posizione += CAMPIONI_BLOCCO_FRASE
            End While
            If valori.Count = 0 Then Return 0.0
            valori.Sort()
            Return valori(Math.Min(valori.Count - 1, valori.Count \ 10))
        Catch ex As Exception
            log.ScriviLog("PreElaborazioneVoce.StimaRumore: " & ex.Message)
            Return 0.0
        End Try
    End Function

    Private Shared Function Normalizza(campioni As Single(), rumore As Double) As Single()
        Try
            Return NormalizzaConLimiti(campioni, rumore, RMS_OBIETTIVO, GUADAGNO_MINIMO, GUADAGNO_MASSIMO_FRASE, PICCO_MASSIMO, 1.5)
        Catch ex As Exception
            log.ScriviLog("PreElaborazioneVoce.Normalizza: " & ex.Message)
            Return campioni
        End Try
    End Function

    Private Shared Function NormalizzaConLimiti(campioni As Single(), rumore As Double, obiettivo As Double, guadagnoMinimo As Double, guadagnoMassimo As Double, piccoMassimo As Double, tolleranzaPicco As Double) As Single()
        Try
            If campioni Is Nothing OrElse campioni.Length = 0 Then Return campioni
            Dim somma As Double = 0.0
            Dim conteggio As Integer = 0
            Dim picco As Double = 0.0
            Dim posizione As Integer = 0
            While posizione < campioni.Length
                Dim quanti As Integer = Math.Min(CAMPIONI_BLOCCO_FRASE, campioni.Length - posizione)
                Dim valore As Double = Rms(campioni, posizione, quanti)
                If valore > Math.Max(rumore * 3.0, RMS_MINIMO_VOCE) Then
                    somma += valore * valore * quanti
                    conteggio += quanti
                End If
                posizione += quanti
            End While
            For Each campione As Single In campioni
                picco = Math.Max(picco, Math.Abs(CDbl(campione)))
            Next
            Dim livello As Double = If(conteggio > 0, Math.Sqrt(somma / conteggio), Rms(campioni, 0, campioni.Length))
            If livello <= 0.0 OrElse picco <= 0.0 Then Return campioni
            Dim guadagno As Double = Math.Max(guadagnoMinimo, Math.Min(guadagnoMassimo, obiettivo / livello))
            guadagno = Math.Min(guadagno, piccoMassimo / picco * tolleranzaPicco)
            Dim risultato(campioni.Length - 1) As Single
            For indice As Integer = 0 To campioni.Length - 1
                risultato(indice) = CSng(Math.Max(-piccoMassimo, Math.Min(piccoMassimo, campioni(indice) * guadagno)))
            Next
            Return risultato
        Catch ex As Exception
            log.ScriviLog("PreElaborazioneVoce.NormalizzaConLimiti: " & ex.Message)
            Return campioni
        End Try
    End Function

    Private Shared Function FiltraPassaAltoIsolato(campioni As Single()) As Single()
        Try
            If campioni Is Nothing OrElse campioni.Length = 0 Then Return campioni
            Dim coefficiente As Double = 1.0 / (1.0 + 2.0 * Math.PI * FREQUENZA_TAGLIO / FREQUENZA)
            Dim ingressoPrecedente As Double = 0.0
            Dim uscitaPrecedente As Double = 0.0
            Dim risultato(campioni.Length - 1) As Single
            For indice As Integer = 0 To campioni.Length - 1
                Dim ingresso As Double = campioni(indice)
                Dim uscita As Double = coefficiente * (uscitaPrecedente + ingresso - ingressoPrecedente)
                ingressoPrecedente = ingresso
                uscitaPrecedente = uscita
                risultato(indice) = CSng(uscita)
            Next
            Return risultato
        Catch ex As Exception
            log.ScriviLog("PreElaborazioneVoce.FiltraPassaAltoIsolato: " & ex.Message)
            Return campioni
        End Try
    End Function

    Public Shared Function PreparaPerVosk(audio As Byte()) As Byte()
        Try
            If audio Is Nothing OrElse audio.Length < 2 Then Return If(audio, New Byte() {})
            Dim filtrati As Single() = FiltraPassaAltoIsolato(InCampioni(audio, audio.Length))
            Dim risultato As Byte() = InByte(Normalizza(filtrati, StimaRumore(filtrati)))
            If risultato Is Nothing OrElse risultato.Length = 0 Then Return audio
            Return risultato
        Catch ex As Exception
            log.ScriviLog("PreElaborazioneVoce.PreparaPerVosk: " & ex.Message & Environment.NewLine & ex.StackTrace)
            Return audio
        End Try
    End Function

    Public Shared Function PreparaPerParakeet(audio As Byte()) As Byte()
        Try
            If audio Is Nothing OrElse audio.Length < 2 Then Return If(audio, New Byte() {})
            Dim filtrati As Single() = FiltraPassaAltoIsolato(InCampioni(audio, audio.Length))
            Dim normalizzati As Single() = NormalizzaConLimiti(filtrati, StimaRumore(filtrati), RMS_OBIETTIVO_PARAKEET, GUADAGNO_MINIMO_PARAKEET, GUADAGNO_MASSIMO_PARAKEET, PICCO_MASSIMO_PARAKEET, 1.0)
            Dim risultato As Byte() = InByte(normalizzati)
            If risultato Is Nothing OrElse risultato.Length = 0 Then Return audio
            Return risultato
        Catch ex As Exception
            log.ScriviLog("PreElaborazioneVoce.PreparaPerParakeet: " & ex.Message & Environment.NewLine & ex.StackTrace)
            Return audio
        End Try
    End Function

    Public Function PreparaVarianti(audio As Byte()) As List(Of Byte())
        Dim varianti As New List(Of Byte())
        Try
            If audio Is Nothing OrElse audio.Length < 2 Then Return varianti
            Dim ingressoPrecedente As Double = 0.0
            Dim uscitaPrecedente As Double = 0.0
            Dim filtrati As Single() = FiltraPassaAlto(InCampioni(audio, audio.Length), ingressoPrecedente, uscitaPrecedente)
            Dim rumore As Double = StimaRumore(filtrati)

            varianti.Add(InByte(Normalizza(filtrati, rumore)))

            If RiduzioneDisponibile Then
                Dim ridotti As Single() = _riduttore.ElaboraCompleto(filtrati)
                If ridotti IsNot Nothing AndAlso ridotti.Length > 0 AndAlso ridotti IsNot filtrati Then
                    Dim variante As Byte() = InByte(Normalizza(ridotti, StimaRumore(ridotti)))
                    If rumore >= RUMORE_ATTIVA_RIDUZIONE Then
                        varianti.Insert(0, variante)
                    Else
                        varianti.Add(variante)
                    End If
                End If
            End If
        Catch ex As Exception
            log.ScriviLog("PreElaborazioneVoce.PreparaVarianti: " & ex.Message & Environment.NewLine & ex.StackTrace)
            If varianti.Count = 0 Then varianti.Add(audio)
        End Try
        Return varianti
    End Function

    Public Sub Dispose() Implements IDisposable.Dispose
        Try
            If _riduttore IsNot Nothing Then _riduttore.Dispose()
            _riduttore = Nothing
        Catch ex As Exception
            log.ScriviLog("PreElaborazioneVoce.Dispose: " & ex.Message)
        End Try
    End Sub
End Class
