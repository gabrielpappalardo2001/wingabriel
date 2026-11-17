Imports System
Imports System.Collections.Generic
Imports System.Globalization
Imports System.Media
Imports System.Text
Imports System.Text.RegularExpressions
Imports System.Threading
Imports WGPlugin

''' <summary>
''' Farmaci: si scrivono i farmaci con la dose e gli orari, e all'ora giusta WinGabriel avvisa con un messaggio e un suono. Dopo venti
''' minuti, se la dose non risulta presa, avvisa ancora una volta. Le assunzioni si segnano dal plugin e restano in un registro, che si
''' può rileggere (oggi o negli ultimi sette giorni). I dati stanno nei file del plugin. Non dà consigli: ricorda quello che ha scritto
''' l'utente.
''' </summary>
Public Class PluginFarmaci
    Inherits WGPluginBase

    Private Class Farmaco
        Public Id As String = ""
        Public Nome As String = ""
        Public Dose As String = ""
        ''' <summary>Gli orari, in minuti dall'inizio del giorno, in ordine.</summary>
        Public Orari As New List(Of Integer)()
        Public Attivo As Boolean = True
    End Class

    Private Const FileFarmaci As String = "farmaci.txt"
    Private Const FileRegistro As String = "registro.txt"
    Private Const MassimoRigheRegistro As Integer = 1500
    Private Const MinutiPrimaDelSecondoAvviso As Integer = 20
    Private Const MinutiMassimiDiRitardo As Integer = 180

    Private _servizi As IWGServiziPlugin
    Private ReadOnly _blocco As New Object()
    Private ReadOnly _farmaci As New List(Of Farmaco)()
    ' ogni riga: "yyyy-MM-dd HH:mm|idFarmaco|nome|dose|programmata" (programmata: HH:mm oppure vuoto se fuori orario)
    Private ReadOnly _registro As New List(Of String)()
    Private ReadOnly _avvisati As New HashSet(Of String)()
    Private ReadOnly _riavvisati As New HashSet(Of String)()
    Private _contatore As System.Threading.Timer

    Public Overrides ReadOnly Property Nome As String
        Get
            Return "Farmaci"
        End Get
    End Property

    Public Overrides ReadOnly Property Descrizione As String
        Get
            Return "Ricorda l'ora dei farmaci e tiene il registro di quelli presi."
        End Get
    End Property

    Public Overrides ReadOnly Property Autore As String
        Get
            Return "WinGabriel"
        End Get
    End Property

#Region "Avvio e arresto"

    Public Overrides Sub Avvia(servizi As IWGServiziPlugin)
        Try
            _servizi = servizi
            Carica()
            SegnaGiaPassatiComeAvvisati()
            ' sta in un posto solo: nel menu Salute
            servizi.AggiungiVoceAMenu("Salute", "Farmaci e promemoria", AddressOf ApriMenu)
            _contatore = New System.Threading.Timer(AddressOf ControllaOrari, Nothing, 5000, 20000)
        Catch ex As Exception
            servizi.RegistraErrore("PluginFarmaci.Avvia", ex)
        End Try
    End Sub

    Public Overrides Sub Arresta()
        Try
            If _contatore IsNot Nothing Then
                _contatore.Dispose()
                _contatore = Nothing
            End If
        Catch ex As Exception
            If _servizi IsNot Nothing Then _servizi.RegistraErrore("PluginFarmaci.Arresta", ex)
        End Try
    End Sub

    ''' <summary>
    ''' Le dosi di oggi che erano già passate da più di mezz'ora quando WinGabriel è partito non si ricordano più (si vedono nel
    ''' registro del giorno come non segnate); quelle di mezz'ora fa o meno si ricordano.
    ''' </summary>
    Private Sub SegnaGiaPassatiComeAvvisati()
        Try
            Dim adesso As DateTime = DateTime.Now
            SyncLock _blocco
                For Each f As Farmaco In _farmaci
                    For Each orario As Integer In f.Orari
                        If adesso.Date.AddMinutes(orario) < adesso.AddMinutes(-30) Then _avvisati.Add(ChiaveDose(adesso.Date, f.Id, orario))
                    Next
                Next
            End SyncLock
        Catch ex As Exception
            _servizi.RegistraErrore("PluginFarmaci.SegnaGiaPassatiComeAvvisati", ex)
        End Try
    End Sub

#End Region

#Region "Dati: leggere e scrivere"

    Private Shared Function Pulisci(testo As String) As String
        Return Regex.Replace(If(testo, "").Replace("|", "/"), "\s+", " ").Trim()
    End Function

    Private Shared Function ChiaveDose(giorno As Date, id As String, orario As Integer) As String
        Return giorno.ToString("yyyyMMdd", CultureInfo.InvariantCulture) & "|" & id & "|" & orario.ToString()
    End Function

    Friend Shared Function InOra(minutiDelGiorno As Integer) As String
        Return (minutiDelGiorno \ 60).ToString("00") & ":" & (minutiDelGiorno Mod 60).ToString("00")
    End Function

    Private Sub Carica()
        Try
            SyncLock _blocco
                _farmaci.Clear()
                _registro.Clear()
                Dim datiFarmaci As Byte() = _servizi.LeggiFileDati(FileFarmaci)
                If datiFarmaci IsNot Nothing Then
                    For Each riga As String In Encoding.UTF8.GetString(datiFarmaci).Replace(vbCr, "").Split(ChrW(10))
                        Dim parti As String() = riga.Split(New Char() {"|"c}, 5)
                        If parti.Length < 5 OrElse parti(0) = "" Then Continue For
                        Dim f As New Farmaco()
                        f.Id = parti(0)
                        f.Attivo = parti(1) = "1"
                        For Each pezzo As String In parti(2).Split(","c)
                            Dim minuti As Integer
                            If Integer.TryParse(pezzo, minuti) AndAlso minuti >= 0 AndAlso minuti < 1440 Then f.Orari.Add(minuti)
                        Next
                        f.Orari.Sort()
                        f.Nome = parti(3)
                        f.Dose = parti(4)
                        _farmaci.Add(f)
                    Next
                End If
                Dim datiRegistro As Byte() = _servizi.LeggiFileDati(FileRegistro)
                If datiRegistro IsNot Nothing Then
                    For Each riga As String In Encoding.UTF8.GetString(datiRegistro).Replace(vbCr, "").Split(ChrW(10))
                        If riga.Trim() <> "" Then _registro.Add(riga)
                    Next
                End If
            End SyncLock
        Catch ex As Exception
            _servizi.RegistraErrore("PluginFarmaci.Carica", ex)
        End Try
    End Sub

    Private Sub SalvaFarmaci()
        Try
            Dim testo As New StringBuilder()
            SyncLock _blocco
                For Each f As Farmaco In _farmaci
                    testo.Append(f.Id).Append("|").Append(If(f.Attivo, "1", "0")).Append("|").Append(String.Join(",", f.Orari)).Append("|").Append(f.Nome).Append("|").Append(f.Dose).Append(vbLf)
                Next
            End SyncLock
            _servizi.ScriviFileDati(FileFarmaci, New UTF8Encoding(False).GetBytes(testo.ToString()))
        Catch ex As Exception
            _servizi.RegistraErrore("PluginFarmaci.SalvaFarmaci", ex)
        End Try
    End Sub

    Private Sub SalvaRegistro()
        Try
            Dim testo As New StringBuilder()
            SyncLock _blocco
                If _registro.Count > MassimoRigheRegistro Then _registro.RemoveRange(0, _registro.Count - MassimoRigheRegistro)
                For Each riga As String In _registro
                    testo.Append(riga).Append(vbLf)
                Next
            End SyncLock
            _servizi.ScriviFileDati(FileRegistro, New UTF8Encoding(False).GetBytes(testo.ToString()))
        Catch ex As Exception
            _servizi.RegistraErrore("PluginFarmaci.SalvaRegistro", ex)
        End Try
    End Sub

    ''' <summary>Vero se la dose di quell'ora, di quel giorno, risulta presa.</summary>
    Private Function GiaPresa(giorno As Date, id As String, orario As Integer) As Boolean
        Dim inizio As String = giorno.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)
        For Each riga As String In _registro
            If Not riga.StartsWith(inizio, StringComparison.Ordinal) Then Continue For
            Dim parti As String() = riga.Split("|"c)
            If parti.Length >= 5 AndAlso parti(1) = id AndAlso parti(4) = InOra(orario) Then Return True
        Next
        Return False
    End Function

    Private Function TrovaFarmaco(id As String) As Farmaco
        SyncLock _blocco
            For Each f As Farmaco In _farmaci
                If f.Id = id Then Return f
            Next
        End SyncLock
        Return Nothing
    End Function

    Private Function NomeEDose(f As Farmaco) As String
        Return If(f.Dose <> "", f.Nome & ", " & f.Dose, f.Nome)
    End Function

    ''' <summary>Segna una dose come presa adesso. orario = -1 per un'assunzione fuori orario.</summary>
    Private Sub Registra(f As Farmaco, orario As Integer)
        Dim adesso As DateTime = DateTime.Now
        Dim riga As String = adesso.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture) & "|" & f.Id & "|" & f.Nome & "|" & f.Dose & "|" & If(orario >= 0, InOra(orario), "")
        SyncLock _blocco
            _registro.Add(riga)
        End SyncLock
        SalvaRegistro()
    End Sub

#End Region

#Region "Ricordare all'ora giusta"

    Private Class DoseDaAvvisare
        Public F As Farmaco
        Public Orario As Integer
        Public Seconda As Boolean
    End Class

    ''' <summary>Chiamato ogni venti secondi da un thread di lavoro: cerca le dosi di cui è l'ora e le dice con un messaggio.</summary>
    Private Sub ControllaOrari(stato As Object)
        Try
            Dim adesso As DateTime = DateTime.Now
            Dim oggi As Date = adesso.Date
            Dim minutiAdesso As Integer = adesso.Hour * 60 + adesso.Minute
            Dim daAvvisare As New List(Of DoseDaAvvisare)()
            SyncLock _blocco
                ' le chiavi di altri giorni non servono più
                Dim oggiTesto As String = oggi.ToString("yyyyMMdd", CultureInfo.InvariantCulture)
                _avvisati.RemoveWhere(Function(k) Not k.StartsWith(oggiTesto, StringComparison.Ordinal))
                _riavvisati.RemoveWhere(Function(k) Not k.StartsWith(oggiTesto, StringComparison.Ordinal))
                For Each f As Farmaco In _farmaci
                    If Not f.Attivo Then Continue For
                    For Each orario As Integer In f.Orari
                        Dim chiave As String = ChiaveDose(oggi, f.Id, orario)
                        Dim ritardo As Integer = minutiAdesso - orario
                        If ritardo < 0 OrElse ritardo > MinutiMassimiDiRitardo Then Continue For
                        If GiaPresa(oggi, f.Id, orario) Then Continue For
                        If Not _avvisati.Contains(chiave) Then
                            _avvisati.Add(chiave)
                            daAvvisare.Add(New DoseDaAvvisare() With {.F = f, .Orario = orario})
                        ElseIf ritardo >= MinutiPrimaDelSecondoAvviso AndAlso Not _riavvisati.Contains(chiave) Then
                            _riavvisati.Add(chiave)
                            daAvvisare.Add(New DoseDaAvvisare() With {.F = f, .Orario = orario, .Seconda = True})
                        End If
                    Next
                Next
            End SyncLock
            If daAvvisare.Count = 0 Then Return
            Dim prime As New List(Of String)()
            Dim seconde As New List(Of String)()
            For Each d As DoseDaAvvisare In daAvvisare
                If d.Seconda Then seconde.Add(NomeEDose(d.F)) Else prime.Add(NomeEDose(d.F))
            Next
            Try
                SystemSounds.Exclamation.Play()
            Catch
            End Try
            If prime.Count > 0 Then _servizi.Messaggio("È l'ora di prendere: " & String.Join("; ", prime) & ".")
            If seconde.Count > 0 Then _servizi.Messaggio("Non risulta ancora preso: " & String.Join("; ", seconde) & ".")
        Catch ex As Exception
            _servizi.RegistraErrore("PluginFarmaci.ControllaOrari", ex)
        End Try
    End Sub

#End Region

#Region "Menu"

    Private Sub ApriMenu()
        Try
            Dim menu As IWGMenuPlugin = _servizi.NuovoMenu("Farmaci")
            menu.Aggiungi("Da prendere ora", AddressOf DaPrendereOra)
            menu.Aggiungi("Segna come preso", AddressOf SegnaComePreso)
            menu.Aggiungi("Registro di oggi", AddressOf RegistroDiOggi)
            menu.Aggiungi("Prossime assunzioni", AddressOf ProssimeAssunzioni)
            menu.Aggiungi("I miei farmaci", AddressOf ElencoFarmaci)
            menu.Aggiungi("Aggiungi un farmaco", AddressOf AggiungiFarmaco)
            menu.AggiungiOpzione("Registro degli ultimi sette giorni", AddressOf RegistroSettimana)
            menu.Mostra()
        Catch ex As Exception
            _servizi.RegistraErrore("PluginFarmaci.ApriMenu", ex)
        End Try
    End Sub

    ''' <summary>Le dosi di oggi con l'ora già passata o nella prossima ora, che non risultano prese.</summary>
    Private Function DosiDaPrendere() As List(Of WGVoceElenco)
        Dim voci As New List(Of WGVoceElenco)()
        Dim adesso As DateTime = DateTime.Now
        Dim minutiAdesso As Integer = adesso.Hour * 60 + adesso.Minute
        SyncLock _blocco
            For Each f As Farmaco In _farmaci
                If Not f.Attivo Then Continue For
                For Each orario As Integer In f.Orari
                    If orario <= minutiAdesso + 60 AndAlso Not GiaPresa(adesso.Date, f.Id, orario) Then
                        voci.Add(New WGVoceElenco(f.Id & "|" & orario.ToString(), NomeEDose(f) & ", delle " & InOra(orario)))
                    End If
                Next
            Next
        End SyncLock
        Return voci
    End Function

    Private Sub DaPrendereOra()
        Try
            Dim voci As List(Of WGVoceElenco) = DosiDaPrendere()
            If voci.Count = 0 Then
                _servizi.Messaggio("Non c'è niente da prendere adesso.")
                Return
            End If
            _servizi.MostraElencoConId("Da prendere", voci, AddressOf DoseScelta)
        Catch ex As Exception
            _servizi.RegistraErrore("PluginFarmaci.DaPrendereOra", ex)
        End Try
    End Sub

    Private Sub DoseScelta(id As String)
        Try
            Dim parti As String() = id.Split("|"c)
            Dim orario As Integer
            If parti.Length <> 2 OrElse Not Integer.TryParse(parti(1), orario) Then Return
            Dim f As Farmaco = TrovaFarmaco(parti(0))
            If f Is Nothing Then Return
            Registra(f, orario)
            _servizi.Messaggio("Segnato: " & NomeEDose(f) & ", alle " & DateTime.Now.ToString("HH:mm", CultureInfo.InvariantCulture) & ".")
        Catch ex As Exception
            _servizi.RegistraErrore("PluginFarmaci.DoseScelta", ex)
        End Try
    End Sub

    Private Sub SegnaComePreso()
        Try
            Dim voci As New List(Of WGVoceElenco)()
            SyncLock _blocco
                For Each f As Farmaco In _farmaci
                    If f.Attivo Then voci.Add(New WGVoceElenco(f.Id, NomeEDose(f)))
                Next
            End SyncLock
            If voci.Count = 0 Then
                _servizi.Messaggio("Non hai farmaci attivi. Aggiungine uno da Aggiungi un farmaco.")
                Return
            End If
            _servizi.MostraElencoConId("Quale farmaco hai preso", voci, AddressOf FarmacoPreso)
        Catch ex As Exception
            _servizi.RegistraErrore("PluginFarmaci.SegnaComePreso", ex)
        End Try
    End Sub

    ''' <summary>Un farmaco preso: conta per la dose programmata più vicina di oggi che non è ancora presa (entro due ore), altrimenti è fuori orario.</summary>
    Private Sub FarmacoPreso(id As String)
        Try
            Dim f As Farmaco = TrovaFarmaco(id)
            If f Is Nothing Then Return
            Dim adesso As DateTime = DateTime.Now
            Dim minutiAdesso As Integer = adesso.Hour * 60 + adesso.Minute
            Dim scelta As Integer = -1
            Dim distanza As Integer = Integer.MaxValue
            SyncLock _blocco
                For Each orario As Integer In f.Orari
                    If GiaPresa(adesso.Date, f.Id, orario) Then Continue For
                    Dim d As Integer = Math.Abs(orario - minutiAdesso)
                    If d <= 120 AndAlso d < distanza Then
                        scelta = orario
                        distanza = d
                    End If
                Next
            End SyncLock
            Registra(f, scelta)
            _servizi.Messaggio("Segnato: " & NomeEDose(f) & ", alle " & adesso.ToString("HH:mm", CultureInfo.InvariantCulture) & If(scelta < 0, ", fuori dagli orari previsti.", "."))
        Catch ex As Exception
            _servizi.RegistraErrore("PluginFarmaci.FarmacoPreso", ex)
        End Try
    End Sub

    ''' <summary>Il giorno dose per dose: preso (a che ora), da prendere, oppure non segnato.</summary>
    Private Function TestoDelGiorno(giorno As Date) As String
        Dim righe As New List(Of String)()
        Dim adesso As DateTime = DateTime.Now
        Dim minutiAdesso As Integer = adesso.Hour * 60 + adesso.Minute
        Dim inizio As String = giorno.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)
        SyncLock _blocco
            Dim dosi As New List(Of KeyValuePair(Of Integer, String))()
            For Each f As Farmaco In _farmaci
                If Not f.Attivo Then Continue For
                For Each orario As Integer In f.Orari
                    Dim stato As String = ""
                    For Each riga As String In _registro
                        If Not riga.StartsWith(inizio, StringComparison.Ordinal) Then Continue For
                        Dim parti As String() = riga.Split("|"c)
                        If parti.Length >= 5 AndAlso parti(1) = f.Id AndAlso parti(4) = InOra(orario) Then
                            stato = "preso alle " & parti(0).Substring(11, 5)
                            Exit For
                        End If
                    Next
                    If stato = "" Then
                        If giorno < adesso.Date OrElse (giorno = adesso.Date AndAlso orario + MinutiMassimiDiRitardo < minutiAdesso) Then
                            stato = "non segnato"
                        ElseIf giorno = adesso.Date AndAlso orario <= minutiAdesso Then
                            stato = "da prendere"
                        Else
                            stato = "in programma"
                        End If
                    End If
                    dosi.Add(New KeyValuePair(Of Integer, String)(orario, InOra(orario) & ", " & NomeEDose(f) & ": " & stato))
                Next
            Next
            dosi.Sort(Function(x, y) x.Key.CompareTo(y.Key))
            For Each d As KeyValuePair(Of Integer, String) In dosi
                righe.Add(d.Value)
            Next
            ' assunzioni fuori orario
            For Each riga As String In _registro
                If Not riga.StartsWith(inizio, StringComparison.Ordinal) Then Continue For
                Dim parti As String() = riga.Split("|"c)
                If parti.Length >= 5 AndAlso parti(4) = "" Then righe.Add(parti(0).Substring(11, 5) & ", " & If(parti(3) <> "", parti(2) & ", " & parti(3), parti(2)) & ": preso fuori orario")
            Next
        End SyncLock
        Return String.Join(vbLf, righe)
    End Function

    Private Sub RegistroDiOggi()
        Try
            Dim testo As String = TestoDelGiorno(DateTime.Today)
            If testo = "" Then
                _servizi.Messaggio("Oggi non ci sono dosi in programma né assunzioni segnate.")
                Return
            End If
            _servizi.MostraTesto("Registro di oggi", testo)
        Catch ex As Exception
            _servizi.RegistraErrore("PluginFarmaci.RegistroDiOggi", ex)
        End Try
    End Sub

    Private Sub RegistroSettimana()
        Try
            Dim testo As New StringBuilder()
            For indietro As Integer = 0 To 6
                Dim giorno As Date = DateTime.Today.AddDays(-indietro)
                Dim parte As String = TestoDelGiorno(giorno)
                If parte <> "" Then testo.Append(giorno.ToString("dddd d MMMM", CultureInfo.GetCultureInfo("it-IT"))).Append(vbLf).Append(parte).Append(vbLf).Append(vbLf)
            Next
            If testo.Length = 0 Then
                _servizi.Messaggio("Negli ultimi sette giorni non c'è niente nel registro.")
                Return
            End If
            _servizi.MostraTesto("Registro degli ultimi sette giorni", testo.ToString().Trim())
        Catch ex As Exception
            _servizi.RegistraErrore("PluginFarmaci.RegistroSettimana", ex)
        End Try
    End Sub

    Private Sub ProssimeAssunzioni()
        Try
            Dim adesso As DateTime = DateTime.Now
            Dim minutiAdesso As Integer = adesso.Hour * 60 + adesso.Minute
            Dim prossime As New List(Of KeyValuePair(Of Integer, String))()
            SyncLock _blocco
                For Each f As Farmaco In _farmaci
                    If Not f.Attivo Then Continue For
                    For Each orario As Integer In f.Orari
                        ' dopo mezzanotte conta il giorno dopo
                        Dim distanza As Integer = If(orario > minutiAdesso, orario - minutiAdesso, orario + 1440 - minutiAdesso)
                        prossime.Add(New KeyValuePair(Of Integer, String)(distanza, NomeEDose(f) & ", alle " & InOra(orario) & If(orario <= minutiAdesso, " di domani", "")))
                    Next
                Next
            End SyncLock
            If prossime.Count = 0 Then
                _servizi.Messaggio("Non hai ancora aggiunto farmaci.")
                Return
            End If
            prossime.Sort(Function(x, y) x.Key.CompareTo(y.Key))
            Dim testo As New List(Of String)()
            For indice As Integer = 0 To Math.Min(2, prossime.Count - 1)
                testo.Add(prossime(indice).Value)
            Next
            _servizi.Messaggio("Le prossime: " & String.Join("; ", testo) & ".")
        Catch ex As Exception
            _servizi.RegistraErrore("PluginFarmaci.ProssimeAssunzioni", ex)
        End Try
    End Sub

#End Region

#Region "Aggiungere e cambiare i farmaci"

    ''' <summary>
    ''' Legge gli orari scritti a mano: "8 14 21", "8:30 e 20:30", "mattina e sera", "ogni 8 ore dalle 7". Restituisce i minuti dall'inizio
    ''' del giorno, in ordine e senza doppioni. Se qualcosa non torna, errore dice cosa.
    ''' </summary>
    Friend Shared Function TentaLeggiOrari(testo As String, ByRef orari As List(Of Integer), ByRef errore As String) As Boolean
        orari = New List(Of Integer)()
        errore = ""
        Try
            Dim t As String = AiutoMinuscolo(testo)
            If t = "" Then
                errore = "Scrivi almeno un orario, per esempio 8 14 21."
                Return False
            End If
            Dim ogni As Match = Regex.Match(t, "ogni\s+(\d{1,2})\s+or[ae](?:\s+(?:dalle|da|a partire dalle|a partire da|partendo dalle)\s+(\d{1,2})(?:[:.](\d{2}))?)?")
            If ogni.Success Then
                Dim passo As Integer = Integer.Parse(ogni.Groups(1).Value)
                If passo < 1 OrElse passo > 12 OrElse 24 Mod passo <> 0 Then
                    errore = "Ogni " & passo.ToString() & " ore non si ripete uguale ogni giorno. Scrivi gli orari uno per uno."
                    Return False
                End If
                Dim ora As Integer = If(ogni.Groups(2).Success, Integer.Parse(ogni.Groups(2).Value), 8)
                Dim minuto As Integer = If(ogni.Groups(3).Success, Integer.Parse(ogni.Groups(3).Value), 0)
                If ora > 23 OrElse minuto > 59 Then
                    errore = "L'ora di partenza non è valida."
                    Return False
                End If
                Dim inizio As Integer = ora * 60 + minuto
                For k As Integer = 0 To 24 \ passo - 1
                    orari.Add((inizio + k * passo * 60) Mod 1440)
                Next
                orari = Distinti(orari)
                Return True
            End If
            Dim trovato As Boolean = False
            For Each m As Match In Regex.Matches(t, "(\d{1,2})(?:\s*[:.]\s*(\d{2}))?|colazione|mattina|pranzo|pomeriggio|cena|sera|notte")
                trovato = True
                If m.Groups(1).Success Then
                    Dim ora As Integer = Integer.Parse(m.Groups(1).Value)
                    Dim minuto As Integer = If(m.Groups(2).Success, Integer.Parse(m.Groups(2).Value), 0)
                    If ora > 23 Then
                        errore = m.Groups(1).Value & " non è un'ora. Per le mezze scrivi per esempio 8:30."
                        Return False
                    End If
                    If minuto > 59 Then
                        errore = "I minuti vanno da 0 a 59."
                        Return False
                    End If
                    orari.Add(ora * 60 + minuto)
                Else
                    Select Case m.Value
                        Case "colazione", "mattina" : orari.Add(8 * 60)
                        Case "pranzo" : orari.Add(13 * 60)
                        Case "pomeriggio" : orari.Add(16 * 60)
                        Case "cena" : orari.Add(20 * 60)
                        Case "sera" : orari.Add(21 * 60)
                        Case "notte" : orari.Add(22 * 60)
                    End Select
                End If
            Next
            If Not trovato OrElse orari.Count = 0 Then
                errore = "Non ho capito gli orari. Scrivi per esempio 8 14 21, oppure 8:30 e 20:30."
                Return False
            End If
            orari = Distinti(orari)
            Return True
        Catch ex As Exception
            errore = "Non ho capito gli orari."
            Return False
        End Try
    End Function

    Private Shared Function AiutoMinuscolo(testo As String) As String
        Return Regex.Replace(If(testo, "").ToLowerInvariant().Replace("à", "a").Replace("è", "e").Replace("é", "e").Replace("ì", "i").Replace("ò", "o").Replace("ù", "u"), "\s+", " ").Trim()
    End Function

    Private Shared Function Distinti(orari As List(Of Integer)) As List(Of Integer)
        Dim risultato As New List(Of Integer)()
        For Each o As Integer In orari
            If Not risultato.Contains(o) Then risultato.Add(o)
        Next
        risultato.Sort()
        Return risultato
    End Function

    Friend Shared Function DescriviOrari(orari As List(Of Integer)) As String
        Dim ore As New List(Of String)()
        For Each o As Integer In orari
            ore.Add(InOra(o))
        Next
        If ore.Count = 1 Then Return "alle " & ore(0)
        Return "alle " & String.Join(", ", ore.GetRange(0, ore.Count - 1)) & " e " & ore(ore.Count - 1)
    End Function

    Private Sub AggiungiFarmaco()
        Try
            Dim modulo As IWGCampiPlugin = _servizi.NuovoModulo()
            modulo.AggiungiTesto("Nome del farmaco", "nome", True, False, "")
            modulo.AggiungiTesto("Dose", "dose", False, False, "")
            modulo.AggiungiTesto("Orari", "orari", True, False, "")
            modulo.AlConferma = Sub()
                                    Dim nome As String = Pulisci(modulo.Valore("nome"))
                                    If nome = "" Then
                                        _servizi.Messaggio("Scrivi il nome del farmaco.")
                                        Return
                                    End If
                                    Dim orari As List(Of Integer) = Nothing
                                    Dim errore As String = ""
                                    If Not TentaLeggiOrari(modulo.Valore("orari"), orari, errore) Then
                                        _servizi.Messaggio(errore)
                                        Return
                                    End If
                                    Dim f As New Farmaco()
                                    f.Id = Guid.NewGuid().ToString("N").Substring(0, 8)
                                    f.Nome = nome
                                    f.Dose = Pulisci(modulo.Valore("dose"))
                                    f.Orari = orari
                                    SyncLock _blocco
                                        _farmaci.Add(f)
                                    End SyncLock
                                    SalvaFarmaci()
                                    modulo.Chiudi()
                                    _servizi.Messaggio("Aggiunto: " & NomeEDose(f) & ", " & DescriviOrari(orari) & ".")
                                End Sub
            modulo.Mostra()
        Catch ex As Exception
            _servizi.RegistraErrore("PluginFarmaci.AggiungiFarmaco", ex)
        End Try
    End Sub

    Private Function TestoVoce(f As Farmaco) As String
        Return NomeEDose(f) & ", " & DescriviOrari(f.Orari) & If(f.Attivo, "", ", sospeso")
    End Function

    ''' <summary>
    ''' I farmaci in un elenco di WinGabriel: Invio dice come è impostato il farmaco; le azioni sono opzioni dell'elenco (Alt) e
    ''' riguardano il farmaco su cui ci si trova.
    ''' </summary>
    Private Sub ElencoFarmaci()
        Try
            Dim farmaci As New List(Of Farmaco)()
            SyncLock _blocco
                farmaci.AddRange(_farmaci)
            End SyncLock
            If farmaci.Count = 0 Then
                _servizi.Messaggio("Non hai ancora aggiunto farmaci.")
                Return
            End If
            Dim elenco As IWGElencoPlugin = _servizi.NuovoElenco("I miei farmaci")
            For Each f As Farmaco In farmaci
                elenco.Aggiungi(f.Id, TestoVoce(f))
            Next
            elenco.AlScelto = AddressOf InfoFarmaco
            elenco.AggiungiOpzioneSuVoce("Segna come preso", AddressOf FarmacoPreso)
            elenco.AggiungiOpzioneSuVoce("Sospendi o riattiva i promemoria", Sub(id As String) CambiaStato(id, elenco))
            elenco.AggiungiOpzioneSuVoce("Cambia gli orari", Sub(id As String) ChiediOrari(id, elenco))
            elenco.AggiungiOpzioneSuVoce("Cambia la dose", Sub(id As String) ChiediDose(id, elenco))
            elenco.AggiungiOpzioneSuVoce("Elimina", Sub(id As String) ChiediEliminazione(id, elenco))
            elenco.Mostra()
        Catch ex As Exception
            _servizi.RegistraErrore("PluginFarmaci.ElencoFarmaci", ex)
        End Try
    End Sub

    Private Sub InfoFarmaco(id As String)
        Try
            Dim f As Farmaco = TrovaFarmaco(id)
            If f Is Nothing Then Return
            _servizi.Messaggio(TestoVoce(f) & ".")
        Catch ex As Exception
            _servizi.RegistraErrore("PluginFarmaci.InfoFarmaco", ex)
        End Try
    End Sub

    Private Sub ChiediOrari(id As String, elenco As IWGElencoPlugin)
        Try
            Dim f As Farmaco = TrovaFarmaco(id)
            If f Is Nothing Then Return
            _servizi.ChiediTesto("Orari", String.Join(" ", ElencoOrariScritti(f)), Sub(testo As String) CambiaOrari(id, testo, elenco))
        Catch ex As Exception
            _servizi.RegistraErrore("PluginFarmaci.ChiediOrari", ex)
        End Try
    End Sub

    Private Sub ChiediDose(id As String, elenco As IWGElencoPlugin)
        Try
            Dim f As Farmaco = TrovaFarmaco(id)
            If f Is Nothing Then Return
            _servizi.ChiediTesto("Dose", f.Dose, Sub(testo As String) CambiaDose(id, testo, elenco))
        Catch ex As Exception
            _servizi.RegistraErrore("PluginFarmaci.ChiediDose", ex)
        End Try
    End Sub

    Private Shared Function ElencoOrariScritti(f As Farmaco) As List(Of String)
        Dim testo As New List(Of String)()
        For Each o As Integer In f.Orari
            testo.Add((o \ 60).ToString() & ":" & (o Mod 60).ToString("00"))
        Next
        Return testo
    End Function

    Private Sub CambiaStato(id As String, elenco As IWGElencoPlugin)
        Try
            Dim f As Farmaco = TrovaFarmaco(id)
            If f Is Nothing Then Return
            f.Attivo = Not f.Attivo
            SalvaFarmaci()
            elenco.AggiornaTesto(f.Id, TestoVoce(f))
            _servizi.Messaggio(If(f.Attivo, "Promemoria riattivati: ", "Promemoria sospesi: ") & f.Nome & ".")
        Catch ex As Exception
            _servizi.RegistraErrore("PluginFarmaci.CambiaStato", ex)
        End Try
    End Sub

    Private Sub CambiaOrari(id As String, testo As String, elenco As IWGElencoPlugin)
        Try
            Dim f As Farmaco = TrovaFarmaco(id)
            If f Is Nothing Then Return
            Dim orari As List(Of Integer) = Nothing
            Dim errore As String = ""
            If Not TentaLeggiOrari(testo, orari, errore) Then
                _servizi.Messaggio(errore)
                Return
            End If
            SyncLock _blocco
                f.Orari = orari
                ' gli orari nuovi riguardano da adesso in poi: le dosi di oggi già passate non si ricordano
                _avvisati.RemoveWhere(Function(k) k.Contains("|" & f.Id & "|"))
                Dim adesso As DateTime = DateTime.Now
                For Each orario As Integer In orari
                    If adesso.Date.AddMinutes(orario) < adesso.AddMinutes(-30) Then _avvisati.Add(ChiaveDose(adesso.Date, f.Id, orario))
                Next
            End SyncLock
            SalvaFarmaci()
            elenco.AggiornaTesto(f.Id, TestoVoce(f))
            _servizi.Messaggio("Orari cambiati: " & f.Nome & ", " & DescriviOrari(orari) & ".")
        Catch ex As Exception
            _servizi.RegistraErrore("PluginFarmaci.CambiaOrari", ex)
        End Try
    End Sub

    Private Sub CambiaDose(id As String, testo As String, elenco As IWGElencoPlugin)
        Try
            Dim f As Farmaco = TrovaFarmaco(id)
            If f Is Nothing Then Return
            f.Dose = Pulisci(testo)
            SalvaFarmaci()
            elenco.AggiornaTesto(f.Id, TestoVoce(f))
            _servizi.Messaggio("Dose cambiata: " & NomeEDose(f) & ".")
        Catch ex As Exception
            _servizi.RegistraErrore("PluginFarmaci.CambiaDose", ex)
        End Try
    End Sub

    Private Async Sub ChiediEliminazione(id As String, elenco As IWGElencoPlugin)
        Try
            Dim f As Farmaco = TrovaFarmaco(id)
            If f Is Nothing Then Return
            Dim elimina As Boolean = Await _servizi.ChiediAsync("Vuoi eliminare " & f.Nome & " e i suoi promemoria? Le assunzioni già segnate restano nel registro.")
            If Not elimina Then Return
            SyncLock _blocco
                _farmaci.Remove(f)
            End SyncLock
            SalvaFarmaci()
            elenco.Rimuovi(f.Id)
            _servizi.Messaggio("Eliminato: " & f.Nome & ".")
        Catch ex As Exception
            _servizi.RegistraErrore("PluginFarmaci.ChiediEliminazione", ex)
        End Try
    End Sub

#End Region

End Class
