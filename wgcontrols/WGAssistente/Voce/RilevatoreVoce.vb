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

' Rilevamento della presenza di voce nei campioni audio in ingresso.
Public Class RilevatoreVoce
    Implements IDisposable

    Public Const CAMPIONI_FINESTRA As Integer = 512
    Private Const CAMPIONI_CONTESTO As Integer = 64
    Private Const FREQUENZA As Long = 16000

    Private _sessione As Microsoft.ML.OnnxRuntime.InferenceSession
    Private _stato As Single()
    Private _contesto As Single()

    Public Sub New(percorsoModello As String)
        Try
            Dim opzioni As New Microsoft.ML.OnnxRuntime.SessionOptions()
            opzioni.IntraOpNumThreads = 1
            opzioni.InterOpNumThreads = 1
            opzioni.GraphOptimizationLevel = Microsoft.ML.OnnxRuntime.GraphOptimizationLevel.ORT_ENABLE_ALL
            _sessione = New Microsoft.ML.OnnxRuntime.InferenceSession(percorsoModello, opzioni)
            Azzera()
        Catch ex As Exception
            log.ScriviLog("RilevatoreVoce.New: " & ex.Message & Environment.NewLine & ex.StackTrace)
            _sessione = Nothing
        End Try
    End Sub

    Public ReadOnly Property Pronto As Boolean
        Get
            Return _sessione IsNot Nothing
        End Get
    End Property

    Public Sub Azzera()
        Try
            _stato = New Single(2 * 1 * 128 - 1) {}
            _contesto = New Single(CAMPIONI_CONTESTO - 1) {}
        Catch ex As Exception
            log.ScriviLog("RilevatoreVoce.Azzera: " & ex.Message)
        End Try
    End Sub

    Public Function Probabilita(campioni As Single()) As Single
        Try
            If _sessione Is Nothing OrElse campioni Is Nothing OrElse campioni.Length <> CAMPIONI_FINESTRA Then Return 0.0F

            Dim ingresso(CAMPIONI_CONTESTO + CAMPIONI_FINESTRA - 1) As Single
            Array.Copy(_contesto, 0, ingresso, 0, CAMPIONI_CONTESTO)
            Array.Copy(campioni, 0, ingresso, CAMPIONI_CONTESTO, CAMPIONI_FINESTRA)

            Dim tensoreIngresso As New Microsoft.ML.OnnxRuntime.Tensors.DenseTensor(Of Single)(ingresso, New Integer() {1, CAMPIONI_CONTESTO + CAMPIONI_FINESTRA})
            Dim tensoreStato As New Microsoft.ML.OnnxRuntime.Tensors.DenseTensor(Of Single)(_stato, New Integer() {2, 1, 128})
            Dim tensoreFrequenza As New Microsoft.ML.OnnxRuntime.Tensors.DenseTensor(Of Long)(New Long() {FREQUENZA}, New Integer() {1})

            Dim ingressi As New List(Of Microsoft.ML.OnnxRuntime.NamedOnnxValue)
            ingressi.Add(Microsoft.ML.OnnxRuntime.NamedOnnxValue.CreateFromTensor("input", tensoreIngresso))
            ingressi.Add(Microsoft.ML.OnnxRuntime.NamedOnnxValue.CreateFromTensor("state", tensoreStato))
            ingressi.Add(Microsoft.ML.OnnxRuntime.NamedOnnxValue.CreateFromTensor("sr", tensoreFrequenza))

            Dim valore As Single = 0.0F
            Dim risultati As Microsoft.ML.OnnxRuntime.IDisposableReadOnlyCollection(Of Microsoft.ML.OnnxRuntime.DisposableNamedOnnxValue) = _sessione.Run(ingressi)
            Try
                For Each risultato As Microsoft.ML.OnnxRuntime.DisposableNamedOnnxValue In risultati
                    If String.Equals(risultato.Name, "output", StringComparison.Ordinal) Then
                        For Each numero As Single In risultato.AsEnumerable(Of Single)()
                            valore = numero
                            Exit For
                        Next
                    ElseIf String.Equals(risultato.Name, "stateN", StringComparison.Ordinal) Then
                        Dim nuovoStato As New List(Of Single)(risultato.AsEnumerable(Of Single)())
                        If nuovoStato.Count = _stato.Length Then _stato = nuovoStato.ToArray()
                    End If
                Next
            Finally
                risultati.Dispose()
            End Try

            Array.Copy(campioni, CAMPIONI_FINESTRA - CAMPIONI_CONTESTO, _contesto, 0, CAMPIONI_CONTESTO)
            Return valore
        Catch ex As Exception
            log.ScriviLog("RilevatoreVoce.Probabilita: " & ex.Message)
            Return 0.0F
        End Try
    End Function

    Public Sub Dispose() Implements IDisposable.Dispose
        Try
            If _sessione IsNot Nothing Then _sessione.Dispose()
            _sessione = Nothing
        Catch ex As Exception
            log.ScriviLog("RilevatoreVoce.Dispose: " & ex.Message)
        End Try
    End Sub
End Class
