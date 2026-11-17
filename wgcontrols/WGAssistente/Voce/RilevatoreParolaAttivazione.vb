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

Public Class RilevatoreParolaAttivazione
    Implements IDisposable

    Private Const CAMPIONI_BLOCCO As Integer = 1280
    Private Const CAMPIONI_CONTESTO As Integer = 480
    Private Const FINESTRA_MEL As Integer = 76
    Private Const PASSO_MEL As Integer = 8
    Private Const BANDE_MEL As Integer = 32
    Private Const DIMENSIONE_EMBEDDING As Integer = 96
    Private Const MASSIMO_FRAME_MEL As Integer = 970
    Private Const MASSIMO_EMBEDDING As Integer = 120
    Private Const PREVISIONI_RISCALDAMENTO As Integer = 5
    Private Const CAMPIONI_RUMORE_INIZIALE As Integer = 64000

    Private _sessioneMel As Microsoft.ML.OnnxRuntime.InferenceSession
    Private _sessioneEmbedding As Microsoft.ML.OnnxRuntime.InferenceSession
    Private _sessioneParola As Microsoft.ML.OnnxRuntime.InferenceSession
    Private _ingressoMel As String = "input"
    Private _ingressoEmbedding As String = "input_1"
    Private _ingressoParola As String = ""
    Private _frameParola As Integer = 16
    Private ReadOnly _blocco As New Object()
    Private ReadOnly _campioniInAttesa As New List(Of Single)
    Private ReadOnly _campioniRecenti As New List(Of Single)
    Private ReadOnly _mel As New List(Of Single())
    Private ReadOnly _embedding As New List(Of Single())
    Private _previsioni As Integer = 0
    Private _consecutivi As Integer = 0
    Private _consecutiviMassimi As Integer = 0

    Public ReadOnly Property ConsecutiviSopraSoglia As Integer
        Get
            Return _consecutiviMassimi
        End Get
    End Property

    Public Sub New(percorsoMel As String, percorsoEmbedding As String, percorsoParola As String)
        Try
            _sessioneMel = CreaSessione(percorsoMel)
            _sessioneEmbedding = CreaSessione(percorsoEmbedding)
            _sessioneParola = CreaSessione(percorsoParola)

            _ingressoMel = PrimoIngresso(_sessioneMel, "input")
            _ingressoEmbedding = PrimoIngresso(_sessioneEmbedding, "input_1")
            _ingressoParola = PrimoIngresso(_sessioneParola, "")
            If String.IsNullOrEmpty(_ingressoParola) Then Throw New InvalidOperationException("il modello della parola di attivazione non dichiara ingressi")

            Dim dimensioni As Integer() = _sessioneParola.InputMetadata(_ingressoParola).Dimensions
            If dimensioni IsNot Nothing AndAlso dimensioni.Length >= 2 AndAlso dimensioni(1) > 0 Then _frameParola = dimensioni(1)

            Azzera()
        Catch ex As Exception
            log.ScriviLog("RilevatoreParolaAttivazione.New: " & ex.Message & Environment.NewLine & ex.StackTrace)
            Dispose()
        End Try
    End Sub

    Private Shared Function CreaSessione(percorso As String) As Microsoft.ML.OnnxRuntime.InferenceSession
        Dim opzioni As New Microsoft.ML.OnnxRuntime.SessionOptions()
        Try
            opzioni.IntraOpNumThreads = 1
            opzioni.InterOpNumThreads = 1
            opzioni.GraphOptimizationLevel = Microsoft.ML.OnnxRuntime.GraphOptimizationLevel.ORT_ENABLE_ALL
            Return New Microsoft.ML.OnnxRuntime.InferenceSession(percorso, opzioni)
        Catch ex As Exception
            log.ScriviLog("RilevatoreParolaAttivazione.CreaSessione (" & percorso & "): " & ex.Message & Environment.NewLine & ex.StackTrace)
            Throw
        End Try
    End Function

    Private Shared Function PrimoIngresso(sessione As Microsoft.ML.OnnxRuntime.InferenceSession, predefinito As String) As String
        Try
            For Each nome As String In sessione.InputMetadata.Keys
                Return nome
            Next
            Return predefinito
        Catch ex As Exception
            log.ScriviLog("RilevatoreParolaAttivazione.PrimoIngresso: " & ex.Message)
            Return predefinito
        End Try
    End Function

    Public ReadOnly Property Pronto As Boolean
        Get
            Return _sessioneMel IsNot Nothing AndAlso _sessioneEmbedding IsNot Nothing AndAlso _sessioneParola IsNot Nothing AndAlso Not String.IsNullOrEmpty(_ingressoParola)
        End Get
    End Property

    Public Sub Azzera()
        Try
            SyncLock _blocco
                _campioniInAttesa.Clear()
                _campioniRecenti.Clear()
                _mel.Clear()
                _embedding.Clear()
                _previsioni = 0
                _consecutivi = 0
                _consecutiviMassimi = 0

                For indice As Integer = 1 To FINESTRA_MEL
                    Dim uno(BANDE_MEL - 1) As Single
                    For banda As Integer = 0 To BANDE_MEL - 1
                        uno(banda) = 1.0F
                    Next
                    _mel.Add(uno)
                Next

                If _sessioneMel Is Nothing OrElse _sessioneEmbedding Is Nothing Then Exit Sub

                Dim casuale As New Random(4242)
                Dim rumore(CAMPIONI_RUMORE_INIZIALE - 1) As Single
                For indice As Integer = 0 To rumore.Length - 1
                    rumore(indice) = CSng(casuale.Next(-1000, 1000))
                Next
                Dim melRumore As List(Of Single()) = CalcolaMel(rumore)
                Dim inizio As Integer = 0
                While inizio + FINESTRA_MEL <= melRumore.Count
                    Dim vettore As Single() = CalcolaEmbedding(melRumore, inizio)
                    If vettore IsNot Nothing Then _embedding.Add(vettore)
                    inizio += PASSO_MEL
                End While
            End SyncLock
        Catch ex As Exception
            log.ScriviLog("RilevatoreParolaAttivazione.Azzera: " & ex.Message & Environment.NewLine & ex.StackTrace)
        End Try
    End Sub

    Public Function Elabora(buffer As Byte(), lunghezza As Integer, sogliaConferma As Single) As Single
        Dim migliore As Single = 0.0F
        Try
            _consecutiviMassimi = 0
            If Not Pronto OrElse buffer Is Nothing OrElse lunghezza < 2 Then Return 0.0F
            SyncLock _blocco
                _consecutiviMassimi = _consecutivi
                For posizione As Integer = 0 To Math.Min(lunghezza, buffer.Length) - 2 Step 2
                    _campioniInAttesa.Add(CSng(BitConverter.ToInt16(buffer, posizione)))
                Next

                While _campioniInAttesa.Count >= CAMPIONI_BLOCCO
                    _campioniRecenti.AddRange(_campioniInAttesa.GetRange(0, CAMPIONI_BLOCCO))
                    _campioniInAttesa.RemoveRange(0, CAMPIONI_BLOCCO)
                    If _campioniRecenti.Count > CAMPIONI_BLOCCO + CAMPIONI_CONTESTO Then _campioniRecenti.RemoveRange(0, _campioniRecenti.Count - (CAMPIONI_BLOCCO + CAMPIONI_CONTESTO))

                    _mel.AddRange(CalcolaMel(_campioniRecenti.ToArray()))
                    If _mel.Count > MASSIMO_FRAME_MEL Then _mel.RemoveRange(0, _mel.Count - MASSIMO_FRAME_MEL)
                    If _mel.Count < FINESTRA_MEL Then Continue While

                    Dim vettore As Single() = CalcolaEmbedding(_mel, _mel.Count - FINESTRA_MEL)
                    If vettore Is Nothing Then Continue While
                    _embedding.Add(vettore)
                    If _embedding.Count > MASSIMO_EMBEDDING Then _embedding.RemoveRange(0, _embedding.Count - MASSIMO_EMBEDDING)
                    If _embedding.Count < _frameParola Then Continue While

                    Dim punteggio As Single = CalcolaPunteggio()
                    _previsioni += 1
                    If _previsioni <= PREVISIONI_RISCALDAMENTO Then punteggio = 0.0F
                    If punteggio >= sogliaConferma Then
                        _consecutivi += 1
                    Else
                        _consecutivi = 0
                    End If
                    If _consecutivi > _consecutiviMassimi Then _consecutiviMassimi = _consecutivi
                    If punteggio > migliore Then migliore = punteggio
                End While
            End SyncLock
        Catch ex As Exception
            log.ScriviLog("RilevatoreParolaAttivazione.Elabora: " & ex.Message & Environment.NewLine & ex.StackTrace)
        End Try
        Return migliore
    End Function

    Private Function CalcolaMel(campioni As Single()) As List(Of Single())
        Dim frame As New List(Of Single())
        Try
            If campioni Is Nothing OrElse campioni.Length < 400 Then Return frame
            Dim tensore As New Microsoft.ML.OnnxRuntime.Tensors.DenseTensor(Of Single)(campioni, New Integer() {1, campioni.Length})
            Dim ingressi As New List(Of Microsoft.ML.OnnxRuntime.NamedOnnxValue)
            ingressi.Add(Microsoft.ML.OnnxRuntime.NamedOnnxValue.CreateFromTensor(_ingressoMel, tensore))
            Dim valori As New List(Of Single)
            Dim risultati As Microsoft.ML.OnnxRuntime.IDisposableReadOnlyCollection(Of Microsoft.ML.OnnxRuntime.DisposableNamedOnnxValue) = _sessioneMel.Run(ingressi)
            Try
                For Each risultato As Microsoft.ML.OnnxRuntime.DisposableNamedOnnxValue In risultati
                    valori.AddRange(risultato.AsEnumerable(Of Single)())
                    Exit For
                Next
            Finally
                risultati.Dispose()
            End Try

            Dim numeroFrame As Integer = valori.Count \ BANDE_MEL
            For indice As Integer = 0 To numeroFrame - 1
                Dim riga(BANDE_MEL - 1) As Single
                For banda As Integer = 0 To BANDE_MEL - 1
                    riga(banda) = valori(indice * BANDE_MEL + banda) / 10.0F + 2.0F
                Next
                frame.Add(riga)
            Next
        Catch ex As Exception
            log.ScriviLog("RilevatoreParolaAttivazione.CalcolaMel: " & ex.Message & Environment.NewLine & ex.StackTrace)
        End Try
        Return frame
    End Function

    Private Function CalcolaEmbedding(mel As List(Of Single()), inizio As Integer) As Single()
        Try
            If mel Is Nothing OrElse inizio < 0 OrElse inizio + FINESTRA_MEL > mel.Count Then Return Nothing
            Dim dati(FINESTRA_MEL * BANDE_MEL - 1) As Single
            For indice As Integer = 0 To FINESTRA_MEL - 1
                Array.Copy(mel(inizio + indice), 0, dati, indice * BANDE_MEL, BANDE_MEL)
            Next
            Dim tensore As New Microsoft.ML.OnnxRuntime.Tensors.DenseTensor(Of Single)(dati, New Integer() {1, FINESTRA_MEL, BANDE_MEL, 1})
            Dim ingressi As New List(Of Microsoft.ML.OnnxRuntime.NamedOnnxValue)
            ingressi.Add(Microsoft.ML.OnnxRuntime.NamedOnnxValue.CreateFromTensor(_ingressoEmbedding, tensore))
            Dim valori As New List(Of Single)
            Dim risultati As Microsoft.ML.OnnxRuntime.IDisposableReadOnlyCollection(Of Microsoft.ML.OnnxRuntime.DisposableNamedOnnxValue) = _sessioneEmbedding.Run(ingressi)
            Try
                For Each risultato As Microsoft.ML.OnnxRuntime.DisposableNamedOnnxValue In risultati
                    valori.AddRange(risultato.AsEnumerable(Of Single)())
                    Exit For
                Next
            Finally
                risultati.Dispose()
            End Try
            If valori.Count <> DIMENSIONE_EMBEDDING Then
                log.ScriviLog("RilevatoreParolaAttivazione.CalcolaEmbedding: il modello ha restituito " & valori.Count.ToString(System.Globalization.CultureInfo.InvariantCulture) & " valori invece di " & DIMENSIONE_EMBEDDING.ToString(System.Globalization.CultureInfo.InvariantCulture))
                Return Nothing
            End If
            Return valori.ToArray()
        Catch ex As Exception
            log.ScriviLog("RilevatoreParolaAttivazione.CalcolaEmbedding: " & ex.Message & Environment.NewLine & ex.StackTrace)
            Return Nothing
        End Try
    End Function

    Private Function CalcolaPunteggio() As Single
        Try
            Dim dati(_frameParola * DIMENSIONE_EMBEDDING - 1) As Single
            Dim inizio As Integer = _embedding.Count - _frameParola
            For indice As Integer = 0 To _frameParola - 1
                Array.Copy(_embedding(inizio + indice), 0, dati, indice * DIMENSIONE_EMBEDDING, DIMENSIONE_EMBEDDING)
            Next
            Dim tensore As New Microsoft.ML.OnnxRuntime.Tensors.DenseTensor(Of Single)(dati, New Integer() {1, _frameParola, DIMENSIONE_EMBEDDING})
            Dim ingressi As New List(Of Microsoft.ML.OnnxRuntime.NamedOnnxValue)
            ingressi.Add(Microsoft.ML.OnnxRuntime.NamedOnnxValue.CreateFromTensor(_ingressoParola, tensore))
            Dim valore As Single = 0.0F
            Dim risultati As Microsoft.ML.OnnxRuntime.IDisposableReadOnlyCollection(Of Microsoft.ML.OnnxRuntime.DisposableNamedOnnxValue) = _sessioneParola.Run(ingressi)
            Try
                For Each risultato As Microsoft.ML.OnnxRuntime.DisposableNamedOnnxValue In risultati
                    For Each numero As Single In risultato.AsEnumerable(Of Single)()
                        valore = numero
                        Exit For
                    Next
                    Exit For
                Next
            Finally
                risultati.Dispose()
            End Try
            Return valore
        Catch ex As Exception
            log.ScriviLog("RilevatoreParolaAttivazione.CalcolaPunteggio: " & ex.Message & Environment.NewLine & ex.StackTrace)
            Return 0.0F
        End Try
    End Function

    Public Sub Dispose() Implements IDisposable.Dispose
        Try
            SyncLock _blocco
                If _sessioneMel IsNot Nothing Then _sessioneMel.Dispose()
                _sessioneMel = Nothing
                If _sessioneEmbedding IsNot Nothing Then _sessioneEmbedding.Dispose()
                _sessioneEmbedding = Nothing
                If _sessioneParola IsNot Nothing Then _sessioneParola.Dispose()
                _sessioneParola = Nothing
            End SyncLock
        Catch ex As Exception
            log.ScriviLog("RilevatoreParolaAttivazione.Dispose: " & ex.Message)
        End Try
    End Sub
End Class
