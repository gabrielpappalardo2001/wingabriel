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

' Riduzione del rumore di fondo dai campioni audio registrati tramite trasformata di Fourier.
Public Class RiduttoreRumore
    Implements IDisposable

    Public Const LUNGHEZZA_BLOCCO As Integer = 512
    Public Const SPOSTAMENTO_BLOCCO As Integer = 128
    Private Const NUMERO_BIN As Integer = LUNGHEZZA_BLOCCO \ 2 + 1
    Private Const DIMENSIONE_STATO As Integer = 2 * 128 * 2
    Private Const RITARDO As Integer = LUNGHEZZA_BLOCCO - SPOSTAMENTO_BLOCCO

    Private _sessione1 As Microsoft.ML.OnnxRuntime.InferenceSession
    Private _sessione2 As Microsoft.ML.OnnxRuntime.InferenceSession
    Private _ingressoDati1 As String = ""
    Private _ingressoStato1 As String = ""
    Private _uscitaDati1 As String = ""
    Private _uscitaStato1 As String = ""
    Private _ingressoDati2 As String = ""
    Private _ingressoStato2 As String = ""
    Private _uscitaDati2 As String = ""
    Private _uscitaStato2 As String = ""

    Private _stato1 As Single()
    Private _stato2 As Single()
    Private ReadOnly _bufferIngresso(LUNGHEZZA_BLOCCO - 1) As Single
    Private ReadOnly _bufferUscita(LUNGHEZZA_BLOCCO - 1) As Single
    Private ReadOnly _attesa As New List(Of Single)
    Private ReadOnly _reale(LUNGHEZZA_BLOCCO - 1) As Double
    Private ReadOnly _immaginario(LUNGHEZZA_BLOCCO - 1) As Double
    Private ReadOnly _coseni(LUNGHEZZA_BLOCCO \ 2 - 1) As Double
    Private ReadOnly _seni(LUNGHEZZA_BLOCCO \ 2 - 1) As Double
    Private ReadOnly _inversione(LUNGHEZZA_BLOCCO - 1) As Integer

    Public Sub New(percorsoModello1 As String, percorsoModello2 As String)
        Try
            Dim opzioni As New Microsoft.ML.OnnxRuntime.SessionOptions()
            opzioni.IntraOpNumThreads = 1
            opzioni.InterOpNumThreads = 1
            opzioni.GraphOptimizationLevel = Microsoft.ML.OnnxRuntime.GraphOptimizationLevel.ORT_ENABLE_ALL
            _sessione1 = New Microsoft.ML.OnnxRuntime.InferenceSession(percorsoModello1, opzioni)
            _sessione2 = New Microsoft.ML.OnnxRuntime.InferenceSession(percorsoModello2, opzioni)

            If Not IdentificaNomi(_sessione1, _ingressoDati1, _ingressoStato1, _uscitaDati1, _uscitaStato1) OrElse Not IdentificaNomi(_sessione2, _ingressoDati2, _ingressoStato2, _uscitaDati2, _uscitaStato2) Then
                Throw New InvalidOperationException("i modelli di riduzione del rumore non hanno gli ingressi attesi")
            End If

            PreparaFft()
            Azzera()
        Catch ex As Exception
            log.ScriviLog("RiduttoreRumore.New: " & ex.Message & Environment.NewLine & ex.StackTrace)
            Dispose()
        End Try
    End Sub

    Public ReadOnly Property Pronto As Boolean
        Get
            Return _sessione1 IsNot Nothing AndAlso _sessione2 IsNot Nothing
        End Get
    End Property

    Private Shared Function IdentificaNomi(sessione As Microsoft.ML.OnnxRuntime.InferenceSession, ByRef ingressoDati As String, ByRef ingressoStato As String, ByRef uscitaDati As String, ByRef uscitaStato As String) As Boolean
        Try
            For Each voce As KeyValuePair(Of String, Microsoft.ML.OnnxRuntime.NodeMetadata) In sessione.InputMetadata
                If voce.Value.Dimensions.Length = 4 Then
                    ingressoStato = voce.Key
                Else
                    ingressoDati = voce.Key
                End If
            Next
            For Each voce As KeyValuePair(Of String, Microsoft.ML.OnnxRuntime.NodeMetadata) In sessione.OutputMetadata
                If voce.Value.Dimensions.Length = 4 Then
                    uscitaStato = voce.Key
                Else
                    uscitaDati = voce.Key
                End If
            Next
            Return ingressoDati.Length > 0 AndAlso ingressoStato.Length > 0 AndAlso uscitaDati.Length > 0 AndAlso uscitaStato.Length > 0
        Catch ex As Exception
            log.ScriviLog("RiduttoreRumore.IdentificaNomi: " & ex.Message)
            Return False
        End Try
    End Function

    Private Sub PreparaFft()
        Try
            Dim bit As Integer = CInt(Math.Round(Math.Log(LUNGHEZZA_BLOCCO, 2)))
            For indice As Integer = 0 To LUNGHEZZA_BLOCCO - 1
                Dim invertito As Integer = 0
                Dim valore As Integer = indice
                For passo As Integer = 1 To bit
                    invertito = (invertito << 1) Or (valore And 1)
                    valore >>= 1
                Next
                _inversione(indice) = invertito
            Next
            For indice As Integer = 0 To LUNGHEZZA_BLOCCO \ 2 - 1
                Dim angolo As Double = 2.0 * Math.PI * indice / LUNGHEZZA_BLOCCO
                _coseni(indice) = Math.Cos(angolo)
                _seni(indice) = Math.Sin(angolo)
            Next
        Catch ex As Exception
            log.ScriviLog("RiduttoreRumore.PreparaFft: " & ex.Message)
            Throw
        End Try
    End Sub

    Public Sub Azzera()
        Try
            _stato1 = New Single(DIMENSIONE_STATO - 1) {}
            _stato2 = New Single(DIMENSIONE_STATO - 1) {}
            Array.Clear(_bufferIngresso, 0, _bufferIngresso.Length)
            Array.Clear(_bufferUscita, 0, _bufferUscita.Length)
            _attesa.Clear()
        Catch ex As Exception
            log.ScriviLog("RiduttoreRumore.Azzera: " & ex.Message)
        End Try
    End Sub

    Private Sub Fft()
        Try
            For indice As Integer = 0 To LUNGHEZZA_BLOCCO - 1
                Dim altro As Integer = _inversione(indice)
                If altro > indice Then
                    Dim appoggio As Double = _reale(indice)
                    _reale(indice) = _reale(altro)
                    _reale(altro) = appoggio
                    appoggio = _immaginario(indice)
                    _immaginario(indice) = _immaginario(altro)
                    _immaginario(altro) = appoggio
                End If
            Next

            Dim dimensione As Integer = 2
            While dimensione <= LUNGHEZZA_BLOCCO
                Dim meta As Integer = dimensione \ 2
                Dim passoTabella As Integer = LUNGHEZZA_BLOCCO \ dimensione
                For inizio As Integer = 0 To LUNGHEZZA_BLOCCO - 1 Step dimensione
                    For k As Integer = 0 To meta - 1
                        Dim c As Double = _coseni(k * passoTabella)
                        Dim s As Double = _seni(k * passoTabella)
                        Dim a As Integer = inizio + k
                        Dim b As Integer = a + meta
                        Dim tr As Double = c * _reale(b) + s * _immaginario(b)
                        Dim ti As Double = c * _immaginario(b) - s * _reale(b)
                        _reale(b) = _reale(a) - tr
                        _immaginario(b) = _immaginario(a) - ti
                        _reale(a) += tr
                        _immaginario(a) += ti
                    Next
                Next
                dimensione *= 2
            End While
        Catch ex As Exception
            log.ScriviLog("RiduttoreRumore.Fft: " & ex.Message)
            Throw
        End Try
    End Sub

    Private Shared Function Esegui(sessione As Microsoft.ML.OnnxRuntime.InferenceSession, nomeDati As String, dati As Single(), formaDati As Integer(), nomeStato As String, stato As Single(), nomeUscitaDati As String, nomeUscitaStato As String, ByRef nuovoStato As Single()) As Single()
        Dim uscita As Single() = Nothing
        Try
            Dim ingressi As New List(Of Microsoft.ML.OnnxRuntime.NamedOnnxValue)
            ingressi.Add(Microsoft.ML.OnnxRuntime.NamedOnnxValue.CreateFromTensor(nomeDati, New Microsoft.ML.OnnxRuntime.Tensors.DenseTensor(Of Single)(dati, formaDati)))
            ingressi.Add(Microsoft.ML.OnnxRuntime.NamedOnnxValue.CreateFromTensor(nomeStato, New Microsoft.ML.OnnxRuntime.Tensors.DenseTensor(Of Single)(stato, New Integer() {1, 2, 128, 2})))
            Dim risultati As Microsoft.ML.OnnxRuntime.IDisposableReadOnlyCollection(Of Microsoft.ML.OnnxRuntime.DisposableNamedOnnxValue) = sessione.Run(ingressi)
            Try
                For Each risultato As Microsoft.ML.OnnxRuntime.DisposableNamedOnnxValue In risultati
                    If String.Equals(risultato.Name, nomeUscitaDati, StringComparison.Ordinal) Then
                        uscita = New List(Of Single)(risultato.AsEnumerable(Of Single)()).ToArray()
                    ElseIf String.Equals(risultato.Name, nomeUscitaStato, StringComparison.Ordinal) Then
                        Dim statoLetto As Single() = New List(Of Single)(risultato.AsEnumerable(Of Single)()).ToArray()
                        If statoLetto.Length = DIMENSIONE_STATO Then nuovoStato = statoLetto
                    End If
                Next
            Finally
                risultati.Dispose()
            End Try
        Catch ex As Exception
            log.ScriviLog("RiduttoreRumore.Esegui: " & ex.Message)
            Return Nothing
        End Try
        Return uscita
    End Function

    Private Function ElaboraBlocco() As Boolean
        Try
            For indice As Integer = 0 To LUNGHEZZA_BLOCCO - 1
                _reale(indice) = _bufferIngresso(indice)
                _immaginario(indice) = 0.0
            Next
            Fft()

            Dim modulo(NUMERO_BIN - 1) As Single
            For k As Integer = 0 To NUMERO_BIN - 1
                modulo(k) = CSng(Math.Sqrt(_reale(k) * _reale(k) + _immaginario(k) * _immaginario(k)))
            Next

            Dim maschera As Single() = Esegui(_sessione1, _ingressoDati1, modulo, New Integer() {1, 1, NUMERO_BIN}, _ingressoStato1, _stato1, _uscitaDati1, _uscitaStato1, _stato1)
            If maschera Is Nothing OrElse maschera.Length < NUMERO_BIN Then Return False

            For k As Integer = 0 To NUMERO_BIN - 1
                _reale(k) *= maschera(k)
                _immaginario(k) *= maschera(k)
            Next
            For k As Integer = NUMERO_BIN To LUNGHEZZA_BLOCCO - 1
                _reale(k) = _reale(LUNGHEZZA_BLOCCO - k)
                _immaginario(k) = -_immaginario(LUNGHEZZA_BLOCCO - k)
            Next
            For k As Integer = 0 To LUNGHEZZA_BLOCCO - 1
                _immaginario(k) = -_immaginario(k)
            Next
            Fft()

            Dim blocco(LUNGHEZZA_BLOCCO - 1) As Single
            For indice As Integer = 0 To LUNGHEZZA_BLOCCO - 1
                blocco(indice) = CSng(_reale(indice) / LUNGHEZZA_BLOCCO)
            Next

            Dim uscita As Single() = Esegui(_sessione2, _ingressoDati2, blocco, New Integer() {1, 1, LUNGHEZZA_BLOCCO}, _ingressoStato2, _stato2, _uscitaDati2, _uscitaStato2, _stato2)
            If uscita Is Nothing OrElse uscita.Length < LUNGHEZZA_BLOCCO Then Return False

            Array.Copy(_bufferUscita, SPOSTAMENTO_BLOCCO, _bufferUscita, 0, RITARDO)
            Array.Clear(_bufferUscita, RITARDO, SPOSTAMENTO_BLOCCO)
            For indice As Integer = 0 To LUNGHEZZA_BLOCCO - 1
                _bufferUscita(indice) += uscita(indice)
            Next
            Return True
        Catch ex As Exception
            log.ScriviLog("RiduttoreRumore.ElaboraBlocco: " & ex.Message)
            Return False
        End Try
    End Function

    Public Function Elabora(campioni As Single()) As Single()
        Dim risultato As New List(Of Single)
        Try
            If Not Pronto OrElse campioni Is Nothing Then Return campioni
            _attesa.AddRange(campioni)
            While _attesa.Count >= SPOSTAMENTO_BLOCCO
                Array.Copy(_bufferIngresso, SPOSTAMENTO_BLOCCO, _bufferIngresso, 0, RITARDO)
                _attesa.CopyTo(0, _bufferIngresso, RITARDO, SPOSTAMENTO_BLOCCO)
                Dim originale(SPOSTAMENTO_BLOCCO - 1) As Single
                _attesa.CopyTo(0, originale, 0, SPOSTAMENTO_BLOCCO)
                _attesa.RemoveRange(0, SPOSTAMENTO_BLOCCO)
                If ElaboraBlocco() Then
                    For indice As Integer = 0 To SPOSTAMENTO_BLOCCO - 1
                        risultato.Add(_bufferUscita(indice))
                    Next
                Else
                    risultato.AddRange(originale)
                End If
            End While
            Return risultato.ToArray()
        Catch ex As Exception
            log.ScriviLog("RiduttoreRumore.Elabora: " & ex.Message & Environment.NewLine & ex.StackTrace)
            Return If(risultato.Count > 0, risultato.ToArray(), campioni)
        End Try
    End Function

    Public Function ElaboraCompleto(campioni As Single()) As Single()
        Try
            If Not Pronto OrElse campioni Is Nothing OrElse campioni.Length = 0 Then Return campioni
            Azzera()
            Dim riempimento As Integer = RITARDO + SPOSTAMENTO_BLOCCO - (campioni.Length Mod SPOSTAMENTO_BLOCCO)
            Dim ingresso(campioni.Length + riempimento - 1) As Single
            Array.Copy(campioni, ingresso, campioni.Length)
            Dim uscita As Single() = Elabora(ingresso)
            Azzera()
            If uscita Is Nothing OrElse uscita.Length < RITARDO + campioni.Length Then Return campioni
            Dim risultato(campioni.Length - 1) As Single
            Array.Copy(uscita, RITARDO, risultato, 0, campioni.Length)
            Return risultato
        Catch ex As Exception
            log.ScriviLog("RiduttoreRumore.ElaboraCompleto: " & ex.Message & Environment.NewLine & ex.StackTrace)
            Return campioni
        End Try
    End Function

    Public Sub Dispose() Implements IDisposable.Dispose
        Try
            If _sessione1 IsNot Nothing Then _sessione1.Dispose()
            _sessione1 = Nothing
            If _sessione2 IsNot Nothing Then _sessione2.Dispose()
            _sessione2 = Nothing
        Catch ex As Exception
            log.ScriviLog("RiduttoreRumore.Dispose: " & ex.Message)
        End Try
    End Sub
End Class
