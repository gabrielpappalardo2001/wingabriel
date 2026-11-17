Imports System
Imports System.Collections.Generic
Imports NAudio.Wave
Imports NAudio.Wave.SampleProviders

' ============================================================================================
' L'audio di Porto Franco. Le sirene, l'elicottero, la pioggia, i rumori dei quartieri e tanti suoni brevi (portiere, clacson,
' frenate, campane, gabbiani, navi, passi) sono registrazioni vere (BigSoundBank, licenza CC0, preparate da scripts\citta\prepara-suoni.py
' e incorporate nella libreria in MP3), con l'effetto Doppler. I motori no: sono sintetizzati al momento (Motori.vb). Se una
' registrazione manca si usa il suono fatto al momento (sintesi), che c'era prima. Anche i segnali del gioco (navigatore, sensore,
' radar) si fanno al momento. Le voci del conduttore della radio e della gente per strada sono voci di Windows registrate in memoria
' e suonate qui (VoceCampioni).
'
' Dove si sente un suono: ogni voce ha un azimut (0 davanti, positivo a sinistra, pi greco dietro). In cuffia (audio 3D) si imita
' l'orecchio: quello più lontano sente un attimo dopo e un po' più piano, e i suoni da dietro sono più smorzati; senza cuffie si usa la
' semplice posizione tra destra e sinistra. C'è un'eco che cambia con il posto (vicoli, galleria, aperto) e due volumi: effetti e radio.
' Il mixer somma tutto in un flusso stereo a 44100 Hz che NAudio manda alla scheda audio.
' ============================================================================================

Public Enum GruppoAudio
    Effetti
    Radio
End Enum

''' <summary>Una voce del mixer: riempie un pezzo di audio. Falso quando ha finito (per i suoni brevi).</summary>
Public MustInherit Class VoceAudio
    ''' <summary>Volume da 0 a 1.</summary>
    Public Volume As Single = 1
    ''' <summary>Da dove viene: 0 davanti, positivo a sinistra, negativo a destra, pi greco dietro (radianti).</summary>
    Public Azimut As Single = 0
    ''' <summary>Falso per i suoni "dentro la testa" (radio, interfaccia): niente posizione né eco.</summary>
    Public Spaziale As Boolean = True
    Public Gruppo As GruppoAudio = GruppoAudio.Effetti
    Public Attiva As Boolean = True
    Protected VolAttuale As Single = 0
    Private _azAttuale As Single = 0
    Private ReadOnly _ritardo(63) As Single
    Private _scrivi As Integer
    Private _filtro As Single

    Protected Const FREQ As Integer = 44100

    ''' <summary>Il prossimo campione mono (tra -1 e 1).</summary>
    Protected MustOverride Function Campione() As Single
    ''' <summary>Vero se il suono è finito e la voce si può togliere.</summary>
    Protected Overridable ReadOnly Property Finita As Boolean
        Get
            Return False
        End Get
    End Property

    ''' <summary>Aggiunge la voce al buffer stereo (e alla mandata dell'eco). modo3D: cuffia; guadagno: il volume del suo gruppo.</summary>
    Public Function Riempi(buffer As Single(), offset As Integer, frames As Integer, modo3D As Boolean, guadagno As Single, eco As Single()) As Boolean
        Try
            For i As Integer = 0 To frames - 1
                VolAttuale += (Volume - VolAttuale) * 0.0015F
                Dim s As Single = Campione() * VolAttuale * guadagno
                If Not Spaziale Then
                    buffer(offset + 2 * i) += s
                    buffer(offset + 2 * i + 1) += s
                    Continue For
                End If
                ' l'azimut si muove piano (dalla parte più corta)
                Dim diff As Single = Azimut - _azAttuale
                If diff > Math.PI Then diff -= CSng(2 * Math.PI)
                If diff < -Math.PI Then diff += CSng(2 * Math.PI)
                _azAttuale += diff * 0.002F
                Dim lato As Double = Math.Sin(_azAttuale)        ' 1 tutto a sinistra, -1 tutto a destra
                Dim dietro As Boolean = Math.Cos(_azAttuale) < 0
                If eco IsNot Nothing Then eco(i) += s * 0.5F
                If modo3D Then
                    ' da dietro il suono arriva più cupo
                    _filtro += (s - _filtro) * If(dietro, 0.22F, 0.9F)
                    Dim x As Single = _filtro
                    _ritardo(_scrivi And 63) = x
                    ' differenza di tempo tra le orecchie: fino a 0,66 millisecondi (29 campioni)
                    Dim ritardo As Integer = CInt(Math.Round(Math.Abs(lato) * 29))
                    Dim tardi As Single = _ritardo((_scrivi - ritardo) And 63)
                    _scrivi += 1
                    Dim vicino As Single = CSng(0.75 + 0.25 * Math.Abs(lato))
                    Dim lontano As Single = CSng(0.75 - 0.4 * Math.Abs(lato))
                    If lato >= 0 Then
                        buffer(offset + 2 * i) += x * vicino
                        buffer(offset + 2 * i + 1) += tardi * lontano
                    Else
                        buffer(offset + 2 * i) += tardi * lontano
                        buffer(offset + 2 * i + 1) += x * vicino
                    End If
                Else
                    Dim angolo As Double = (1 - lato) * Math.PI / 4
                    Dim smorzo As Single = If(dietro, 0.8F, 1.0F)
                    buffer(offset + 2 * i) += s * smorzo * CSng(Math.Cos(angolo))
                    buffer(offset + 2 * i + 1) += s * smorzo * CSng(Math.Sin(angolo))
                End If
            Next
            Return Not Finita AndAlso Attiva
        Catch ex As Exception
            RegistroErrori.Registra("VoceAudio.Riempi", ex)
            ' una voce che sbaglia si toglie e le altre continuano
            Return False
        End Try
    End Function
End Class

''' <summary>
''' Le registrazioni vere, incorporate nella libreria (suoni\*.mp3): si decodificano una volta sola e si tengono in memoria, mono a
''' 22050 Hz. I suoni brevi possono avere più varianti (nome_1, nome_2...), scritte in suoni\elenco.json.
''' </summary>
Public Module Registrazioni
    Private ReadOnly _cache As New Dictionary(Of String, Short())()
    Private ReadOnly _blocco As New Object()
    Private _elenco As Dictionary(Of String, Integer)
    Private ReadOnly _caso As New Random()
    Public Const FREQ_REGISTRAZIONI As Integer = 22050

    Private Function Elenco() As Dictionary(Of String, Integer)
        Try
            SyncLock _blocco
                If _elenco IsNot Nothing Then Return _elenco
                _elenco = New Dictionary(Of String, Integer)()
                Using s As IO.Stream = Reflection.Assembly.GetExecutingAssembly().GetManifestResourceStream("suoni.elenco.json")
                    If s IsNot Nothing Then
                        Using r As New IO.StreamReader(s, Text.Encoding.UTF8)
                            Dim j As Newtonsoft.Json.Linq.JObject = Newtonsoft.Json.Linq.JObject.Parse(r.ReadToEnd())
                            For Each p As Newtonsoft.Json.Linq.JProperty In j.Properties()
                                _elenco(p.Name) = CInt(p.Value)
                            Next
                        End Using
                    End If
                End Using
                Return _elenco
            End SyncLock
        Catch ex As Exception
            RegistroErrori.Registra("Registrazioni.Elenco", ex)
            _elenco = New Dictionary(Of String, Integer)()
            Return _elenco
        End Try
    End Function

    ''' <summary>Vero se c'è la registrazione (o le sue varianti).</summary>
    Public Function Esiste(nome As String) As Boolean
        Try
            Return Elenco().ContainsKey(nome)
        Catch ex As Exception
            RegistroErrori.Registra("Registrazioni.Esiste", ex)
            Throw
        End Try
    End Function

    ''' <summary>Il nome del file di una variante a caso ("" se il suono non c'è).</summary>
    Public Function Variante(nome As String) As String
        Try
            Dim n As Integer
            If Not Elenco().TryGetValue(nome, n) Then Return ""
            If n <= 0 Then Return nome
            SyncLock _caso
                Return nome & "_" & (_caso.Next(n) + 1).ToString()
            End SyncLock
        Catch ex As Exception
            RegistroErrori.Registra("Registrazioni.Variante", ex)
            Throw
        End Try
    End Function

    ''' <summary>I campioni di un file (nome del file, senza .mp3); Nothing se manca.</summary>
    Public Function Campioni(file As String) As Short()
        Try
            If String.IsNullOrEmpty(file) Then Return Nothing
            SyncLock _blocco
                Dim c As Short() = Nothing
                If _cache.TryGetValue(file, c) Then Return c
            End SyncLock
            Dim dati As Short() = Nothing
            Using s As IO.Stream = Reflection.Assembly.GetExecutingAssembly().GetManifestResourceStream("suoni." & file & ".mp3")
                If s IsNot Nothing Then
                    Using lettore As New Mp3FileReader(s)
                        Dim pcm As New List(Of Short)(CInt(Math.Min(Integer.MaxValue, lettore.Length \ 2)))
                        Dim buf(16383) As Byte
                        Dim canali As Integer = lettore.WaveFormat.Channels
                        Do
                            Dim letti As Integer = lettore.Read(buf, 0, buf.Length)
                            If letti <= 0 Then Exit Do
                            For i As Integer = 0 To letti - 2 Step 2 * canali
                                pcm.Add(BitConverter.ToInt16(buf, i))
                            Next
                        Loop
                        dati = pcm.ToArray()
                    End Using
                End If
            End Using
            SyncLock _blocco
                _cache(file) = dati
            End SyncLock
            Return dati
        Catch ex As Exception
            RegistroErrori.Registra("Registrazioni.Campioni", ex)
            SyncLock _blocco
                _cache(file) = Nothing
            End SyncLock
            Return Nothing
        End Try
    End Function

    ''' <summary>Decodifica tutto in anticipo (all'inizio della partita, in sottofondo), così il gioco non si ferma la prima volta.</summary>
    Public Sub Precarica()
        Try
            For Each kv As KeyValuePair(Of String, Integer) In Elenco().ToList()
                If kv.Value <= 0 Then
                    Campioni(kv.Key)
                Else
                    For k As Integer = 1 To kv.Value
                        Campioni(kv.Key & "_" & k.ToString())
                    Next
                End If
            Next
        Catch ex As Exception
            RegistroErrori.Registra("Registrazioni.Precarica", ex)
        End Try
    End Sub

    ''' <summary>Il campione alla posizione (frazionaria) pos, interpolato; con anello si riparte dall'inizio.</summary>
    Public Function Leggi(d As Short(), pos As Double) As Single
        Try
            Dim n As Integer = d.Length
            Dim i As Integer = CInt(Math.Floor(pos))
            Dim f As Single = CSng(pos - i)
            i = ((i Mod n) + n) Mod n
            Dim j As Integer = If(i + 1 >= n, 0, i + 1)
            Return (d(i) * (1 - f) + d(j) * f) / 32768.0F
        Catch ex As Exception
            RegistroErrori.Registra("Registrazioni.Leggi", ex)
            Throw
        End Try
    End Function
End Module

''' <summary>Una canzone delle radio (brani con licenza Creative Commons, vedi musica\LICENZA.txt).</summary>
Public NotInheritable Class Brano
    Public File As String = ""
    Public Stile As String = ""
    Public Titolo As String = ""
    Public Autore As String = ""
End Class

''' <summary>Le canzoni incorporate nella libreria (musica\*.mp3 e musica\elenco.json): si decodificano quando servono.</summary>
Public Module Canzoni
    Private _brani As List(Of Brano)
    Private ReadOnly _blocco As New Object()

    Public Function Elenco() As List(Of Brano)
        Try
            SyncLock _blocco
                If _brani IsNot Nothing Then Return _brani
                _brani = New List(Of Brano)()
                Using s As IO.Stream = Reflection.Assembly.GetExecutingAssembly().GetManifestResourceStream("musica.elenco.json")
                    If s IsNot Nothing Then
                        Using r As New IO.StreamReader(s, Text.Encoding.UTF8)
                            For Each j As Newtonsoft.Json.Linq.JToken In Newtonsoft.Json.Linq.JArray.Parse(r.ReadToEnd())
                                _brani.Add(New Brano With {.File = j("file").ToString(), .Stile = j("stile").ToString(), .Titolo = j("titolo").ToString(), .Autore = j("autore").ToString()})
                            Next
                        End Using
                    End If
                End Using
                Return _brani
            End SyncLock
        Catch ex As Exception
            RegistroErrori.Registra("Canzoni.Elenco", ex)
            _brani = New List(Of Brano)()
            Return _brani
        End Try
    End Function

    ''' <summary>I campioni di una canzone (mono, 22050 Hz); Nothing se non si riesce.</summary>
    Public Function Carica(b As Brano) As Short()
        Try
            Using s As IO.Stream = Reflection.Assembly.GetExecutingAssembly().GetManifestResourceStream("musica." & b.File & ".mp3")
                If s Is Nothing Then Return Nothing
                Using lettore As New Mp3FileReader(s)
                    Dim pcm As New List(Of Short)(22050 * 90)
                    Dim buf(16383) As Byte
                    Dim canali As Integer = lettore.WaveFormat.Channels
                    Do
                        Dim letti As Integer = lettore.Read(buf, 0, buf.Length)
                        If letti <= 0 Then Exit Do
                        For i As Integer = 0 To letti - 2 Step 2 * canali
                            pcm.Add(BitConverter.ToInt16(buf, i))
                        Next
                    Loop
                    Return pcm.ToArray()
                End Using
            End Using
        Catch ex As Exception
            RegistroErrori.Registra("Canzoni.Carica", ex)
            Return Nothing
        End Try
    End Function
End Module

''' <summary>
''' Una registrazione vera, una volta sola o ad anello. Velocita la fa più acuta e veloce (2 = un'ottava sopra); Doppler idem, per chi passa.
''' </summary>
Public NotInheritable Class VoceRegistrata
    Inherits VoceAudio
    Private _dati As Short()
    Private ReadOnly _anello As Boolean
    Private _pos As Double
    Public Velocita As Single = 1
    Public Doppler As Single = 1
    Private _vel As Single = 1
    Private _dop As Single = 1

    ''' <summary>nome: il suono (se ha varianti se ne sceglie una a caso).</summary>
    Public Sub New(nome As String, Optional anello As Boolean = False)
        Try
            _anello = anello
            _dati = Registrazioni.Campioni(Registrazioni.Variante(nome))
            If anello AndAlso _dati IsNot Nothing AndAlso _dati.Length > 0 Then _pos = New Random().Next(_dati.Length)
            If Not anello Then VolAttuale = 1
        Catch ex As Exception
            RegistroErrori.Registra("VoceRegistrata.New", ex)
            Throw
        End Try
    End Sub

    ''' <summary>Dai campioni già pronti (le canzoni della radio).</summary>
    Public Sub New(dati As Short())
        Try
            _dati = dati
            _anello = False
            VolAttuale = 1
        Catch ex As Exception
            RegistroErrori.Registra("VoceRegistrata.New (dati)", ex)
            Throw
        End Try
    End Sub

    Public ReadOnly Property Presente As Boolean
        Get
            Return _dati IsNot Nothing AndAlso _dati.Length > 1
        End Get
    End Property

    ''' <summary>Vero quando un suono che non si ripete è arrivato in fondo.</summary>
    Public ReadOnly Property FinitaOra As Boolean
        Get
            Return Finita
        End Get
    End Property

    ''' <summary>Quanti secondi dura (alla velocità normale).</summary>
    Public ReadOnly Property Durata As Double
        Get
            Return If(_dati Is Nothing, 0, _dati.Length / CDbl(Registrazioni.FREQ_REGISTRAZIONI))
        End Get
    End Property

    Protected Overrides ReadOnly Property Finita As Boolean
        Get
            Return Not _anello AndAlso (_dati Is Nothing OrElse _pos >= _dati.Length - 1)
        End Get
    End Property

    Protected Overrides Function Campione() As Single
        Try
            If _dati Is Nothing OrElse _dati.Length < 2 Then Return 0
            If Not _anello AndAlso _pos >= _dati.Length - 1 Then Return 0
            _vel += (Velocita - _vel) * 0.001F
            _dop += (Doppler - _dop) * 0.001F
            Dim s As Single = Registrazioni.Leggi(_dati, _pos)
            _pos += Registrazioni.FREQ_REGISTRAZIONI / CDbl(FREQ) * _vel * _dop
            If _anello AndAlso _pos >= _dati.Length Then _pos -= _dati.Length
            Return s
        Catch ex As Exception
            RegistroErrori.Registra("VoceRegistrata.Campione", ex)
            Throw
        End Try
    End Function
End Class

''' <summary>La sirena della polizia (o dell'ambulanza): la registrazione ad anello, con l'effetto Doppler; senza, due toni fatti al momento.</summary>
Public NotInheritable Class VoceSirena
    Inherits VoceAudio
    Public Doppler As Single = 1
    Private _dop As Single = 1
    Private _fase As Double
    Private _t As Double
    Private _dati As Short()
    Private _pos As Double

    Public Sub New(Optional registrazione As String = "sirena_polizia")
        Try
            _dati = Registrazioni.Campioni(Registrazioni.Variante(registrazione))
            If _dati IsNot Nothing AndAlso _dati.Length > 0 Then _pos = New Random().Next(_dati.Length)
        Catch ex As Exception
            RegistroErrori.Registra("VoceSirena.New", ex)
            Throw
        End Try
    End Sub

    Protected Overrides Function Campione() As Single
        Try
            _t += 1.0 / FREQ
            _dop += (Doppler - _dop) * 0.001F
            Dim d As Short() = _dati
            If d IsNot Nothing AndAlso d.Length > 1 Then
                Dim s As Single = Registrazioni.Leggi(d, _pos)
                _pos += Registrazioni.FREQ_REGISTRAZIONI / CDbl(FREQ) * _dop
                If _pos >= d.Length Then _pos -= d.Length
                Return s * 0.8F
            End If
            Dim f As Double = If((CInt(Math.Floor(_t / 0.55)) Mod 2) = 0, 660, 880) * _dop
            _fase += f / FREQ
            If _fase >= 1 Then _fase -= 1
            Return CSng(Math.Tanh(Math.Sin(2 * Math.PI * _fase) * 3) * 0.3)
        Catch ex As Exception
            RegistroErrori.Registra("VoceSirena.Campione", ex)
            Throw
        End Try
    End Function
End Class

''' <summary>L'elicottero della polizia: colpi delle pale e rombo.</summary>
Public NotInheritable Class VoceElicottero
    Inherits VoceAudio
    Private _t As Double
    Private _f As Single
    Private ReadOnly _caso As New Random()
    Private ReadOnly _dati As Short() = Registrazioni.Campioni(Registrazioni.Variante("elicottero"))
    Private _pos As Double

    Protected Overrides Function Campione() As Single
        Try
            _t += 1.0 / FREQ
            If _dati IsNot Nothing AndAlso _dati.Length > 1 Then
                Dim s As Single = Registrazioni.Leggi(_dati, _pos)
                _pos += Registrazioni.FREQ_REGISTRAZIONI / CDbl(FREQ)
                If _pos >= _dati.Length Then _pos -= _dati.Length
                Return s
            End If
            Dim fase As Double = (_t * 17) Mod 1
            _f += (CSng(_caso.NextDouble() * 2 - 1) - _f) * 0.12F
            Dim colpo As Double = Math.Exp(-fase * 9)
            Return CSng(_f * (0.35 + 1.4 * colpo) + Math.Sin(2 * Math.PI * 55 * _t) * 0.1)
        Catch ex As Exception
            RegistroErrori.Registra("VoceElicottero.Campione", ex)
            Throw
        End Try
    End Function
End Class

''' <summary>La pioggia: fruscio e gocce; in macchina il tamburellare sul tetto.</summary>
Public NotInheritable Class VocePioggia
    Inherits VoceAudio
    Public InMacchina As Boolean
    Private _a As Single
    Private _b As Single
    Private _goccia As Double
    Private ReadOnly _caso As New Random()

    Public Sub New()
        Try
            Spaziale = False
        Catch ex As Exception
            RegistroErrori.Registra("VocePioggia.New", ex)
            Throw
        End Try
    End Sub

    Private ReadOnly _fuori As Short() = Registrazioni.Campioni(Registrazioni.Variante("pioggia"))
    Private ReadOnly _tetto As Short() = Registrazioni.Campioni(Registrazioni.Variante("pioggia_tetto"))
    Private _pos As Double

    Protected Overrides Function Campione() As Single
        Try
            Dim d As Short() = If(InMacchina, _tetto, _fuori)
            If d IsNot Nothing AndAlso d.Length > 1 Then
                Dim s As Single = Registrazioni.Leggi(d, _pos)
                _pos += Registrazioni.FREQ_REGISTRAZIONI / CDbl(FREQ)
                If _pos >= d.Length Then _pos -= d.Length
                Return s
            End If
            Dim r As Single = CSng(_caso.NextDouble() * 2 - 1)
            _a += (r - _a) * 0.45F
            _b += (_a - _b) * 0.05F
            Dim fruscio As Single = (_a - _b) * 0.6F
            If _caso.NextDouble() < If(InMacchina, 0.0025, 0.0012) Then _goccia = 1
            _goccia *= If(InMacchina, 0.992, 0.97)
            Dim tetto As Double = If(InMacchina, Math.Sin(_goccia * 40) * _goccia * 0.5, _goccia * r * 0.4)
            Return CSng(fruscio * If(InMacchina, 0.45, 1.0) + tetto)
        Catch ex As Exception
            RegistroErrori.Registra("VocePioggia.Campione", ex)
            Throw
        End Try
    End Function
End Class

''' <summary>Un suono già pronto, campione per campione (la voce del conduttore della radio).</summary>
Public NotInheritable Class VoceCampioni
    Inherits VoceAudio
    Private ReadOnly _c As Single()
    Private _i As Integer

    Public Sub New(campioni As Single())
        Try
            _c = campioni
            VolAttuale = 1
        Catch ex As Exception
            RegistroErrori.Registra("VoceCampioni.New", ex)
            Throw
        End Try
    End Sub

    Public ReadOnly Property FinitaOra As Boolean
        Get
            Return _i >= _c.Length
        End Get
    End Property

    Protected Overrides ReadOnly Property Finita As Boolean
        Get
            Return _i >= _c.Length
        End Get
    End Property

    Protected Overrides Function Campione() As Single
        Try
            If _i >= _c.Length Then Return 0
            Dim s As Single = _c(_i)
            _i += 1
            Return s
        Catch ex As Exception
            RegistroErrori.Registra("VoceCampioni.Campione", ex)
            Throw
        End Try
    End Function
End Class

''' <summary>Un suono breve fatto di toni e rumore, con un inviluppo che decade.</summary>
Public NotInheritable Class VoceBreve
    Inherits VoceAudio
    Private ReadOnly _toni As Double()
    Private ReadOnly _durataTono As Double
    Private ReadOnly _rumore As Double
    Private ReadOnly _durata As Double
    Private ReadOnly _decadimento As Double
    Private ReadOnly _quadra As Boolean
    Private _t As Double
    Private _fase As Double
    Private _filtro As Single
    Private ReadOnly _caso As New Random()

    Public Sub New(toni As Double(), durataTono As Double, rumore As Double, decadimento As Double, Optional quadra As Boolean = False)
        Try
            _toni = toni
            _durataTono = durataTono
            _rumore = rumore
            _decadimento = decadimento
            _quadra = quadra
            _durata = Math.Max(durataTono * Math.Max(1, toni.Length), 0.05)
            VolAttuale = 1
        Catch ex As Exception
            RegistroErrori.Registra("VoceBreve.New", ex)
            Throw
        End Try
    End Sub

    Protected Overrides ReadOnly Property Finita As Boolean
        Get
            Return _t >= _durata
        End Get
    End Property

    Protected Overrides Function Campione() As Single
        Try
            _t += 1.0 / FREQ
            If _t >= _durata Then Return 0
            Dim s As Double = 0
            If _toni.Length > 0 Then
                Dim k As Integer = Math.Min(_toni.Length - 1, CInt(Math.Floor(_t / _durataTono)))
                _fase += _toni(k) / FREQ
                If _fase >= 1 Then _fase -= 1
                Dim onda As Double = Math.Sin(2 * Math.PI * _fase)
                If _quadra Then onda = Math.Sign(onda) * 0.7
                Dim tInTono As Double = _t - k * _durataTono
                s += onda * Math.Exp(-tInTono * _decadimento) * (1 - _rumore)
            End If
            If _rumore > 0 Then
                _filtro += (CSng(_caso.NextDouble() * 2 - 1) - _filtro) * 0.35F
                s += _filtro * _rumore * Math.Exp(-_t * _decadimento)
            End If
            Dim attacco As Double = Math.Min(1, _t / 0.003)
            Return CSng(s * 0.6 * attacco)
        Catch ex As Exception
            RegistroErrori.Registra("VoceBreve.Campione", ex)
            Throw
        End Try
    End Function
End Class

''' <summary>Il rumore di fondo della città o del mare.</summary>
Public NotInheritable Class VoceAmbiente
    Inherits VoceAudio
    ''' <summary>0 = città (traffico lontano), 1 = mare (onde).</summary>
    Public Mare As Single = 0
    Private _f1 As Single
    Private _f2 As Single
    Private _t As Double
    Private ReadOnly _caso As New Random()

    Public Sub New()
        Try
            Spaziale = False
        Catch ex As Exception
            RegistroErrori.Registra("VoceAmbiente.New", ex)
            Throw
        End Try
    End Sub

    Protected Overrides Function Campione() As Single
        Try
            _t += 1.0 / FREQ
            Dim r As Single = CSng(_caso.NextDouble() * 2 - 1)
            _f1 += (r - _f1) * 0.02F
            _f2 += (_f1 - _f2) * 0.05F
            Dim onde As Double = 0.5 + 0.5 * Math.Sin(2 * Math.PI * _t / 7.0) * Math.Sin(2 * Math.PI * _t / 3.1)
            Dim citta As Double = 0.7 + 0.3 * Math.Sin(2 * Math.PI * _t / 11.0)
            Return CSng(_f2 * 2.2 * (Mare * onde + (1 - Mare) * citta))
        Catch ex As Exception
            RegistroErrori.Registra("VoceAmbiente.Campione", ex)
            Throw
        End Try
    End Function
End Class

''' <summary>
''' La musica di una radio, fatta al momento: un giro di accordi con basso, batteria e uno strumento che cambia con lo stile
''' (melodica: mandolino pizzicato; rock: chitarre distorte; elettronica: arpeggi e cassa dritta). Ogni brano ha tonalità e tempo suoi.
''' Abbassa: mentre parla il conduttore la musica si abbassa.
''' </summary>
Public NotInheritable Class VoceMusica
    Inherits VoceAudio
    Public Stile As String = "melodica"
    Public Abbassa As Boolean
    Private _liv As Single = 1
    Private ReadOnly _caso As New Random()
    Private _bpm As Double = 100
    Private _radice As Double = 220
    Private _giro As Integer() = {0, 5, 3, 4}
    Private _t As Double
    Private _passoPrima As Integer = -1
    Private _fBasso As Double, _fBassoFase As Double, _bassoEnv As Double
    Private _fMelo As Double, _meloFase As Double, _meloEnv As Double
    Private _accordo As Double() = {1, 1.26, 1.5}
    Private ReadOnly _faseAcc(2) As Double
    Private _cassa As Double, _rullante As Double, _charleston As Double
    Private _faseCassa As Double

    Public Sub New(stile As String)
        Try
            Me.Stile = stile
            Spaziale = False
            Gruppo = GruppoAudio.Radio
            NuovoBrano()
        Catch ex As Exception
            RegistroErrori.Registra("VoceMusica.New", ex)
            Throw
        End Try
    End Sub

    ''' <summary>Cambia tonalità, tempo e giro di accordi.</summary>
    Public Sub NuovoBrano()
        Try
            Dim radici As Double() = {196, 220, 246.9, 261.6, 293.7}
            _radice = radici(_caso.Next(radici.Length))
            Select Case Stile
                Case "rock" : _bpm = 118 + _caso.Next(30)
                Case "elettronica" : _bpm = 120 + _caso.Next(10)
                Case Else : _bpm = 84 + _caso.Next(30)
            End Select
            Dim giri As Integer()() = {New Integer() {0, 5, 3, 4}, New Integer() {0, 3, 4, 4}, New Integer() {5, 3, 0, 4}, New Integer() {0, 4, 5, 3}}
            _giro = giri(_caso.Next(giri.Length))
        Catch ex As Exception
            RegistroErrori.Registra("VoceMusica.NuovoBrano", ex)
            Throw
        End Try
    End Sub

    Private Shared ReadOnly GradiMaggiore As Double() = {0, 2, 4, 5, 7, 9, 11}

    Private Function Nota(grado As Integer, ottava As Integer) As Double
        Try
            Dim g As Integer = ((grado Mod 7) + 7) Mod 7
            Dim sopra As Integer = CInt(Math.Floor(grado / 7.0))
            Return _radice * Math.Pow(2, (GradiMaggiore(g) + 12 * (ottava + sopra)) / 12.0)
        Catch ex As Exception
            RegistroErrori.Registra("VoceMusica.Nota", ex)
            Throw
        End Try
    End Function

    Protected Overrides Function Campione() As Single
        Try
            _t += 1.0 / FREQ
            _liv += (If(Abbassa, 0.25F, 1.0F) - _liv) * 0.0005F
            Dim sedicesimo As Double = 60 / _bpm / 4
            Dim passo As Integer = CInt(Math.Floor(_t / sedicesimo))
            If passo <> _passoPrima Then
                _passoPrima = passo
                Dim battuta As Integer = (passo \ 16) Mod 4
                Dim nel As Integer = passo Mod 16
                Dim grado As Integer = _giro(battuta)
                _accordo = New Double() {Nota(grado, 0) / _radice, Nota(grado + 2, 0) / _radice, Nota(grado + 4, 0) / _radice}
                If nel Mod 4 = 0 Then
                    _fBasso = Nota(grado, -1)
                    _bassoEnv = 1
                End If
                Select Case Stile
                    Case "rock"
                        If nel = 0 OrElse nel = 8 OrElse nel = 10 Then _cassa = 1
                        If nel = 4 OrElse nel = 12 Then _rullante = 1
                        If nel Mod 2 = 0 Then _charleston = 0.6
                        If nel Mod 8 = 6 AndAlso _caso.NextDouble() < 0.5 Then
                            _fMelo = Nota(grado + _caso.Next(5), 1)
                            _meloEnv = 1
                        End If
                    Case "elettronica"
                        If nel Mod 4 = 0 Then _cassa = 1
                        If nel Mod 4 = 2 Then _charleston = 0.8
                        If nel = 12 Then _rullante = 0.7
                        _fMelo = Nota(grado + {0, 2, 4, 7}(nel Mod 4), 1)
                        _meloEnv = 0.8
                    Case Else
                        If nel = 0 Then _cassa = 0.7
                        If nel = 8 Then _rullante = 0.4
                        If nel Mod 2 = 0 Then
                            _fMelo = Nota(grado + {0, 2, 4, 2, 7, 4, 2, 4}((nel \ 2) Mod 8) + If(_caso.NextDouble() < 0.2, 1, 0), 1)
                            _meloEnv = 1
                        End If
                End Select
            End If
            _fBassoFase += _fBasso / FREQ
            If _fBassoFase >= 1 Then _fBassoFase -= 1
            _bassoEnv *= 0.99993
            Dim basso As Double = Math.Sin(2 * Math.PI * _fBassoFase) * _bassoEnv * 0.35
            Dim acc As Double = 0
            For k As Integer = 0 To 2
                _faseAcc(k) += _radice * _accordo(k) / FREQ
                If _faseAcc(k) >= 1 Then _faseAcc(k) -= 1
                If Stile = "rock" Then
                    acc += Math.Tanh((2 * _faseAcc(k) - 1) * 4) * 0.07
                ElseIf Stile = "elettronica" Then
                    acc += Math.Sin(2 * Math.PI * _faseAcc(k)) * 0.04
                Else
                    acc += Math.Sin(2 * Math.PI * _faseAcc(k)) * 0.035
                End If
            Next
            _meloFase += _fMelo / FREQ
            If _meloFase >= 1 Then _meloFase -= 1
            _meloEnv *= If(Stile = "melodica", 0.99975, 0.9998)
            Dim melo As Double
            If Stile = "elettronica" Then
                melo = If(_meloFase < 0.5, 1, -1) * _meloEnv * 0.07
            Else
                melo = (Math.Sin(2 * Math.PI * _meloFase) + 0.3 * Math.Sin(4 * Math.PI * _meloFase)) * _meloEnv * 0.15
            End If
            _faseCassa += (50 + 90 * _cassa) / FREQ
            _cassa *= 0.9993
            Dim cassa As Double = Math.Sin(2 * Math.PI * _faseCassa) * _cassa * 0.5
            Dim rum As Double = _caso.NextDouble() * 2 - 1
            _rullante *= 0.9995
            _charleston *= 0.996
            Dim piatti As Double = rum * (_rullante * 0.25 + _charleston * 0.08)
            Return CSng((basso + acc + melo + cassa + piatti) * _liv)
        Catch ex As Exception
            RegistroErrori.Registra("VoceMusica.Campione", ex)
            Throw
        End Try
    End Function
End Class

''' <summary>Il mixer: somma tutte le voci in un flusso stereo, aggiunge l'eco e lo manda alla scheda audio.</summary>
Public NotInheritable Class MotoreAudio
    Implements ISampleProvider

    Private ReadOnly _voci As New List(Of VoceAudio)()
    Private ReadOnly _blocco As New Object()
    Private ReadOnly _formato As WaveFormat = WaveFormat.CreateIeeeFloatWaveFormat(44100, 2)
    Private _uscita As WaveOutEvent
    Public VolumeEffetti As Single = 0.8F
    Public VolumeRadio As Single = 0.6F
    ''' <summary>Vero: audio 3D per le cuffie.</summary>
    Public Modo3D As Boolean
    ''' <summary>Quanta eco (0 all'aperto, 0,6 in galleria).</summary>
    Public Eco As Single = 0.1F
    Private _ecoAttuale As Single = 0.1F
    Private _mandata As Single() = New Single(8191) {}
    ' l'eco: quattro filtri a pettine e due passa-tutto (Schroeder)
    Private ReadOnly _pettini As Single()() = {New Single(1556) {}, New Single(1616) {}, New Single(1490) {}, New Single(1421) {}}
    Private ReadOnly _posPettini(3) As Integer
    Private ReadOnly _passaTutto As Single()() = {New Single(224) {}, New Single(555) {}}
    Private ReadOnly _posPassa(1) As Integer

    Public ReadOnly Property WaveFormat As WaveFormat Implements ISampleProvider.WaveFormat
        Get
            Return _formato
        End Get
    End Property

    Public Sub Avvia()
        Try
            If _uscita IsNot Nothing Then Return
            _uscita = New WaveOutEvent() With {.DesiredLatency = 120, .NumberOfBuffers = 3}
            _uscita.Init(New SampleToWaveProvider16(Me))
            _uscita.Play()
        Catch ex As Exception
            RegistroErrori.Registra("MotoreAudio.Avvia", ex)
            Throw
        End Try
    End Sub

    Public Sub Ferma()
        Try
            If _uscita Is Nothing Then Return
            Try
                _uscita.Stop()
            Finally
                _uscita.Dispose()
                _uscita = Nothing
            End Try
            SyncLock _blocco
                _voci.Clear()
            End SyncLock
        Catch ex As Exception
            RegistroErrori.Registra("MotoreAudio.Ferma", ex)
            Throw
        End Try
    End Sub

    Public Sub Aggiungi(v As VoceAudio)
        Try
            SyncLock _blocco
                If Not _voci.Contains(v) Then _voci.Add(v)
            End SyncLock
        Catch ex As Exception
            RegistroErrori.Registra("MotoreAudio.Aggiungi", ex)
            Throw
        End Try
    End Sub

    Public Sub Togli(v As VoceAudio)
        Try
            SyncLock _blocco
                _voci.Remove(v)
            End SyncLock
        Catch ex As Exception
            RegistroErrori.Registra("MotoreAudio.Togli", ex)
            Throw
        End Try
    End Sub

    ''' <summary>Un suono breve, una volta sola, dalla direzione data (azimut: 0 davanti, positivo a sinistra).</summary>
    Public Sub Suona(v As VoceAudio, Optional volume As Single = 1, Optional azimut As Single = 0)
        Try
            v.Volume = volume
            v.Azimut = azimut
            Aggiungi(v)
        Catch ex As Exception
            RegistroErrori.Registra("MotoreAudio.Suona", ex)
            Throw
        End Try
    End Sub

    Public Function Read(buffer As Single(), offset As Integer, count As Integer) As Integer Implements ISampleProvider.Read
        Try
            Array.Clear(buffer, offset, count)
            Dim frames As Integer = count \ 2
            If _mandata.Length < frames Then _mandata = New Single(frames - 1) {}
            Array.Clear(_mandata, 0, frames)
            SyncLock _blocco
                For i As Integer = _voci.Count - 1 To 0 Step -1
                    Dim v As VoceAudio = _voci(i)
                    Dim g As Single = If(v.Gruppo = GruppoAudio.Radio, VolumeRadio, VolumeEffetti)
                    If Not v.Riempi(buffer, offset, frames, Modo3D, g, If(v.Spaziale, _mandata, Nothing)) Then _voci.RemoveAt(i)
                Next
            End SyncLock
            For i As Integer = 0 To frames - 1
                _ecoAttuale += (Eco - _ecoAttuale) * 0.0002F
                If _ecoAttuale < 0.005F Then Continue For
                Dim x As Single = _mandata(i)
                Dim somma As Single = 0
                For k As Integer = 0 To 3
                    Dim p As Single() = _pettini(k)
                    Dim y As Single = p(_posPettini(k))
                    p(_posPettini(k)) = x + y * 0.78F
                    _posPettini(k) = (_posPettini(k) + 1) Mod p.Length
                    somma += y
                Next
                somma *= 0.25F
                For k As Integer = 0 To 1
                    Dim p As Single() = _passaTutto(k)
                    Dim y As Single = p(_posPassa(k))
                    Dim nuovo As Single = somma + y * 0.5F
                    p(_posPassa(k)) = nuovo
                    _posPassa(k) = (_posPassa(k) + 1) Mod p.Length
                    somma = y - nuovo * 0.5F
                Next
                buffer(offset + 2 * i) += somma * _ecoAttuale
                buffer(offset + 2 * i + 1) += somma * _ecoAttuale
            Next
            For i As Integer = offset To offset + count - 1
                ' un limitatore morbido: tante voci insieme non gracchiano
                buffer(i) = CSng(Math.Tanh(buffer(i)))
            Next
            Return count
        Catch ex As Exception
            RegistroErrori.Registra("MotoreAudio.Read", ex)
            ' il filo dell'audio non si deve mai fermare: per questo giro silenzio
            Return count
        End Try
    End Function

    Private Shared Function Interfaccia(v As VoceBreve) As VoceAudio
        Try
            v.Spaziale = False
            Return v
        Catch ex As Exception
            RegistroErrori.Registra("MotoreAudio.Interfaccia", ex)
            Throw
        End Try
    End Function

    ' ---- i suoni brevi del gioco
    Public Shared Function Bip(alto As Boolean) As VoceAudio
        Try
            Return New VoceBreve(If(alto, New Double() {1320}, New Double() {880}), 0.09, 0, 18)
        Catch ex As Exception
            RegistroErrori.Registra("MotoreAudio.Bip", ex)
            Throw
        End Try
    End Function

    ''' <summary>Il doppio tono, in salita, che si sente nel punto in cui il navigatore fa girare.</summary>
    Public Shared Function SvoltaNavigatore() As VoceAudio
        Try
            Return New VoceBreve(New Double() {523, 784}, 0.16, 0, 6, True)
        Catch ex As Exception
            RegistroErrori.Registra("MotoreAudio.SvoltaNavigatore", ex)
            Throw
        End Try
    End Function

    ''' <summary>Il tonfo basso e rotondo che si sente dalla parte della svolta del navigatore (non somiglia a nessun altro suono).</summary>
    Public Shared Function TicSvolta() As VoceAudio
        Try
            Return New VoceBreve(New Double() {330}, 0.16, 0, 11)
        Catch ex As Exception
            RegistroErrori.Registra("MotoreAudio.TicSvolta", ex)
            Throw
        End Try
    End Function

    ''' <summary>Il doppio colpetto squadrato che annuncia una curva.</summary>
    Public Shared Function TicCurva() As VoceAudio
        Try
            Return New VoceBreve(New Double() {740, 0, 740}, 0.05, 0, 14, True)
        Catch ex As Exception
            RegistroErrori.Registra("MotoreAudio.TicCurva", ex)
            Throw
        End Try
    End Function

    ''' <summary>Il bip squadrato, acuto e secco, del sensore di prossimità laterale.</summary>
    Public Shared Function Prossimita() As VoceAudio
        Try
            Return New VoceBreve(New Double() {1480}, 0.07, 0, 16, True)
        Catch ex As Exception
            RegistroErrori.Registra("MotoreAudio.Prossimita", ex)
            Throw
        End Try
    End Function

    Public Shared Function Urto(forte As Boolean) As VoceAudio
        Try
            Return New VoceBreve(New Double() {70, 55}, If(forte, 0.25, 0.12), 0.75, If(forte, 4.5, 9))
        Catch ex As Exception
            RegistroErrori.Registra("MotoreAudio.Urto", ex)
            Throw
        End Try
    End Function

    ''' <summary>Una registrazione vera, una volta sola (una variante a caso); se manca, il suono di riserva (o un attimo di silenzio).</summary>
    Public Shared Function Registrato(nome As String, Optional riserva As VoceAudio = Nothing) As VoceAudio
        Try
            Dim v As New VoceRegistrata(nome)
            If v.Presente Then Return v
            Return If(riserva, New VoceBreve(New Double() {}, 0.01, 0, 1))
        Catch ex As Exception
            RegistroErrori.Registra("MotoreAudio.Registrato", ex)
            Throw
        End Try
    End Function

    Public Shared Function Passo() As VoceAudio
        Try
            Return Registrato("passo", New VoceBreve(New Double() {120}, 0.05, 0.85, 45))
        Catch ex As Exception
            RegistroErrori.Registra("MotoreAudio.Passo", ex)
            Throw
        End Try
    End Function

    ''' <summary>Un passo di corsa.</summary>
    Public Shared Function Corsa() As VoceAudio
        Try
            Return Registrato("corsa", New VoceBreve(New Double() {140}, 0.04, 0.85, 55))
        Catch ex As Exception
            RegistroErrori.Registra("MotoreAudio.Corsa", ex)
            Throw
        End Try
    End Function

    Public Shared Function Clacson(Optional camion As Boolean = False) As VoceAudio
        Try
            Return Registrato(If(camion, "clacson_camion", If(New Random().Next(2) = 0, "clacson", "clacson_lungo")), New VoceBreve(New Double() {415, 415}, 0.22, 0, 0.5, True))
        Catch ex As Exception
            RegistroErrori.Registra("MotoreAudio.Clacson", ex)
            Throw
        End Try
    End Function

    Public Shared Function Portiera() As VoceAudio
        Try
            Return Registrato({"portiera", "portiera2", "portiera3"}(New Random().Next(3)), New VoceBreve(New Double() {180, 95}, 0.07, 0.5, 14))
        Catch ex As Exception
            RegistroErrori.Registra("MotoreAudio.Portiera", ex)
            Throw
        End Try
    End Function

    ''' <summary>Il segnale di un oggetto nascosto o da raccogliere: un luccichio di tre note.</summary>
    Public Shared Function Luccichio() As VoceAudio
        Try
            Return New VoceBreve(New Double() {1568, 2093, 2637}, 0.07, 0.05, 9)
        Catch ex As Exception
            RegistroErrori.Registra("MotoreAudio.Luccichio", ex)
            Throw
        End Try
    End Function

    ''' <summary>Il bip del radar: acuto per la polizia, medio per il traffico, basso per le auto ferme.</summary>
    Public Shared Function BipRadar(frequenza As Double) As VoceAudio
        Try
            Return New VoceBreve(New Double() {frequenza}, 0.07, 0, 25)
        Catch ex As Exception
            RegistroErrori.Registra("MotoreAudio.BipRadar", ex)
            Throw
        End Try
    End Function

    ''' <summary>Lo squillo del telefono (due trilli).</summary>
    Public Shared Function Squillo() As VoceAudio
        Try
            Return Interfaccia(New VoceBreve(New Double() {1400, 1800, 1400, 1800, 0, 0, 1400, 1800, 1400, 1800}, 0.06, 0, 2, True))
        Catch ex As Exception
            RegistroErrori.Registra("MotoreAudio.Squillo", ex)
            Throw
        End Try
    End Function

    ''' <summary>Il conto alla rovescia delle sfide: tre bip bassi e uno alto.</summary>
    Public Shared Function Via(ultimo As Boolean) As VoceAudio
        Try
            Return Interfaccia(New VoceBreve(New Double() {If(ultimo, 1320, 660)}, If(ultimo, 0.4, 0.15), 0, 4))
        Catch ex As Exception
            RegistroErrori.Registra("MotoreAudio.Via", ex)
            Throw
        End Try
    End Function

    ''' <summary>Un trofeo: un piccolo squillo di fanfara.</summary>
    Public Shared Function Fanfara() As VoceAudio
        Try
            Return Interfaccia(New VoceBreve(New Double() {784, 784, 784, 1047, 1319, 1568}, 0.11, 0, 3, True))
        Catch ex As Exception
            RegistroErrori.Registra("MotoreAudio.Fanfara", ex)
            Throw
        End Try
    End Function

    Public Shared Function Soldi() As VoceAudio
        Try
            Return Interfaccia(New VoceBreve(New Double() {988, 1319, 1760}, 0.08, 0, 10))
        Catch ex As Exception
            RegistroErrori.Registra("MotoreAudio.Soldi", ex)
            Throw
        End Try
    End Function

    Public Shared Function Successo() As VoceAudio
        Try
            Return Interfaccia(New VoceBreve(New Double() {523, 659, 784, 1047}, 0.13, 0, 5))
        Catch ex As Exception
            RegistroErrori.Registra("MotoreAudio.Successo", ex)
            Throw
        End Try
    End Function

    Public Shared Function Fallimento() As VoceAudio
        Try
            Return Interfaccia(New VoceBreve(New Double() {392, 330, 262}, 0.2, 0, 4))
        Catch ex As Exception
            RegistroErrori.Registra("MotoreAudio.Fallimento", ex)
            Throw
        End Try
    End Function

    Public Shared Function Stella() As VoceAudio
        Try
            Return Interfaccia(New VoceBreve(New Double() {700, 900, 700, 900}, 0.1, 0, 6, True))
        Catch ex As Exception
            RegistroErrori.Registra("MotoreAudio.Stella", ex)
            Throw
        End Try
    End Function

    Public Shared Function Sgommata() As VoceAudio
        Try
            Return Registrato(If(New Random().Next(2) = 0, "sgommata", "sgommata2"), New VoceBreve(New Double() {}, 0.45, 1, 3))
        Catch ex As Exception
            RegistroErrori.Registra("MotoreAudio.Sgommata", ex)
            Throw
        End Try
    End Function

    Public Shared Function Tic() As VoceAudio
        Try
            Return New VoceBreve(New Double() {2000}, 0.02, 0.3, 60)
        Catch ex As Exception
            RegistroErrori.Registra("MotoreAudio.Tic", ex)
            Throw
        End Try
    End Function

    ''' <summary>Il ticchettio del semaforo rosso (più basso e più lento del sensore).</summary>
    Public Shared Function TicSemaforo() As VoceAudio
        Try
            Return New VoceBreve(New Double() {650}, 0.04, 0.2, 35)
        Catch ex As Exception
            RegistroErrori.Registra("MotoreAudio.TicSemaforo", ex)
            Throw
        End Try
    End Function

    ''' <summary>Il bip del sensore per chi arriva di fronte (più acuto e doppio).</summary>
    Public Shared Function Allarme() As VoceAudio
        Try
            Return New VoceBreve(New Double() {2600, 0, 2600}, 0.04, 0, 20)
        Catch ex As Exception
            RegistroErrori.Registra("MotoreAudio.Allarme", ex)
            Throw
        End Try
    End Function

    ''' <summary>Il fischio di un pedone o di un vigile.</summary>
    Public Shared Function Fischio() As VoceAudio
        Try
            Return Registrato("fischietto", New VoceBreve(New Double() {2400, 2900}, 0.12, 0.05, 3))
        Catch ex As Exception
            RegistroErrori.Registra("MotoreAudio.Fischio", ex)
            Throw
        End Try
    End Function

    Public Shared Function Monete() As VoceAudio
        Try
            Return New VoceBreve(New Double() {2093, 2637}, 0.06, 0.1, 12)
        Catch ex As Exception
            RegistroErrori.Registra("MotoreAudio.Monete", ex)
            Throw
        End Try
    End Function
End Class
