Imports wgcontrols
Imports System.IO
Imports System.Media
Imports System.Reflection
Imports System.Threading
Imports Newtonsoft.Json.Linq

''' <summary>
''' I suoni tipici dei giochi: le carte (briscola, scopa e varianti, burraco), i dadi, i soldi e le case del monopoli.
''' Li sceglie il server: ogni evento di gioco può avere il campo "suono" (il nome di un file di suoni\, senza .wav) e la versione web
''' suona gli stessi. Sono incorporati nella libreria (suoni\*.wav, fatti da scripts\prepara-suoni.py). Servono anche alla chat (notifica dei messaggi).
''' Si accendono e spengono dalle impostazioni (Giochi, Suoni): la chiave di configurazione è SuoniGiochi, accesa se manca.
''' Non sollevano mai errori: senza audio si gioca in silenzio.
''' </summary>
Public NotInheritable Class SuoniGiochi

    Private Shared ReadOnly _coda As New Queue(Of String)()
    Private Shared _inCorso As Boolean = False
    Private Shared ReadOnly _lock As New Object()
    Private Shared ReadOnly _dati As New Dictionary(Of String, Byte())()
    ' i suoni che si ripetono molto hanno più varianti (nome, nome_2, nome_3...): se ne sceglie una a caso
    Private Shared ReadOnly _varianti As New Dictionary(Of String, Integer) From {{"carta", 4}, {"pesca", 3}, {"presa", 2}, {"calata", 2}, {"dadi", 3}, {"passo", 3}, {"soldi", 3}, {"casa", 2}, {"compra", 2}, {"colpito", 2}, {"posiziona", 2}}
    Private Shared ReadOnly _caso As New Random()

    Private Sub New()
    End Sub

    Public Const CHIAVE_SUONI As String = "SuoniGiochi"

    ''' <summary>I suoni dei giochi e della chat sono accesi? (Impostazioni, Giochi, Suoni; accesi se la scelta non c'è.)</summary>
    Public Shared Function Attivi() As Boolean
        Try
            Try
                Dim c As New WGConfig()
                If c.EsisteId(CHIAVE_SUONI) Then
                    Dim v As Boolean = True
                    Boolean.TryParse(c.LeggiValore(CHIAVE_SUONI), v)
                    Return v
                End If
            Catch exLog As Exception
                RegistroErrori.Registra("SuoniGiochi.Attivi", exLog)
            End Try
            Return True
        Catch ex As Exception
            RegistroErrori.Registra("SuoniGiochi.Attivi", ex)
            Throw
        End Try
    End Function

    ''' <summary>C'è un suono in riproduzione o in coda?</summary>
    Public Shared Function InCorso() As Boolean
        Try
            SyncLock _lock
                Return _inCorso OrElse _coda.Count > 0
            End SyncLock
        Catch ex As Exception
            RegistroErrori.Registra("SuoniGiochi.InCorso", ex)
            Throw
        End Try
    End Function

    ''' <summary>Un suono solo, per nome (per esempio "notifica" per un messaggio della chat, "richiesta" per chi vuole scriverti).</summary>
    Public Shared Sub SuonaNome(nome As String)
        Try
            If Not Attivi() Then Return
            Dim a As New JArray()
            a.Add(New JObject(New JProperty("suono", nome)))
            Suona(a)
        Catch exLog As Exception
            RegistroErrori.Registra("SuoniGiochi.SuonaNome", exLog)
        End Try
    End Sub

    ''' <summary>Suona i suoni degli eventi (se ne arrivano molti insieme, per esempio quando gioca il computer, solo gli ultimi quattro).</summary>
    Public Shared Sub Suona(eventi As JArray)
        Try
            If eventi Is Nothing OrElse eventi.Count = 0 Then Return
            Dim nomi As New List(Of String)()
            For Each e As JToken In eventi
                Dim s As JToken = e("suono")
                If s IsNot Nothing AndAlso s.Type = JTokenType.String Then nomi.Add(s.ToString())
            Next
            If nomi.Count = 0 OrElse Not Attivi() Then Return
            SyncLock _lock
                For Each n As String In nomi.Skip(Math.Max(0, nomi.Count - 4))
                    _coda.Enqueue(n)
                Next
                While _coda.Count > 4
                    _coda.Dequeue()
                End While
                If _inCorso Then Return
                _inCorso = True
            End SyncLock
            Dim t As New Thread(AddressOf Riproduci)
            t.IsBackground = True
            t.Start()
        Catch exLog As Exception
            RegistroErrori.Registra("SuoniGiochi.Suona", exLog)
        End Try
    End Sub

    Private Shared Sub Riproduci()
        Try
            Do
                Dim nome As String
                SyncLock _lock
                    If _coda.Count = 0 Then
                        _inCorso = False
                        Return
                    End If
                    nome = _coda.Dequeue()
                End SyncLock
                Dim n As Integer = 1
                If _varianti.TryGetValue(nome, n) AndAlso n > 1 Then
                    Dim k As Integer = _caso.Next(1, n + 1)
                    If k > 1 Then nome = nome & "_" & k.ToString()
                End If
                Dim dati As Byte() = Carica(nome)
                If dati IsNot Nothing Then
                    Using ms As New MemoryStream(dati)
                        Using p As New SoundPlayer(ms)
                            p.PlaySync()
                        End Using
                    End Using
                End If
                Thread.Sleep(60)
            Loop
        Catch exLog As Exception
            RegistroErrori.Registra("SuoniGiochi.Riproduci", exLog)
            SyncLock _lock
                _inCorso = False
            End SyncLock
        End Try
    End Sub

    Private Shared Function Carica(nome As String) As Byte()
        Try
            If nome Is Nothing OrElse Not System.Text.RegularExpressions.Regex.IsMatch(nome, "^[a-z0-9_]+$") Then Return Nothing
            SyncLock _dati
                Dim d As Byte() = Nothing
                If _dati.TryGetValue(nome, d) Then Return d
                Using s As Stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("suoni." & nome & ".wav")
                    If s IsNot Nothing Then
                        Using m As New MemoryStream()
                            s.CopyTo(m)
                            d = m.ToArray()
                        End Using
                    End If
                End Using
                _dati(nome) = d
                Return d
            End SyncLock
        Catch ex As Exception
            RegistroErrori.Registra("SuoniGiochi.Carica", ex)
            Throw
        End Try
    End Function

End Class
