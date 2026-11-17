Imports System
Imports System.Collections.Generic
Imports System.Globalization
Imports System.IO
Imports System.Text
Imports System.Threading
Imports System.Threading.Tasks
Imports System.Windows.Forms
Imports GabrielSync.Filen
Imports Newtonsoft.Json.Linq
Imports wgcontrols

Friend NotInheritable Class VoceMenuCloud

    Private ReadOnly _chiave As String

    Public Event Scelta(chiave As String, valore As Boolean)

    Public Sub New(chiave As String)
        _chiave = If(chiave, "")
    End Sub

    Public ReadOnly Property Chiave As String
        Get
            Return _chiave
        End Get
    End Property

    Public Sub Si()
        RaiseEvent Scelta(_chiave, True)
    End Sub

    Public Sub No()
        RaiseEvent Scelta(_chiave, False)
    End Sub

End Class

Friend NotInheritable Class VoceNomeCloud

    Private ReadOnly _nome As String

    Public Event Scelto(nome As String)

    Public Sub New(nome As String)
        _nome = If(nome, "")
    End Sub

    Public Sub Scegli()
        RaiseEvent Scelto(_nome)
    End Sub

End Class

Friend NotInheritable Class InfoBackupCloud
    Public Property Nome As String = ""
    Public Property Data As DateTime = DateTime.MinValue
    Public Property Dimensione As Long = 0
    Public Property Uuid As String = ""

    Public ReadOnly Property Descrizione As String
        Get
            Try
                Dim testo As String = If(Data <> DateTime.MinValue, Data.ToString("dd/MM/yyyy HH:mm", CultureInfo.InvariantCulture), Nome)
                If Dimensione > 0 Then testo &= ", " & WGDb.WGBackup.DescriviDimensione(Dimensione)
                Return testo
            Catch
                Return Nome
            End Try
        End Get
    End Property
End Class

Friend NotInheritable Class CartellaDatiCloud
    Public Property Nome As String = ""
    Public Property Dimensione As Long = 0
    Public Property File As Integer = 0
    Public Property Inclusa As Boolean = True
End Class

Friend NotInheritable Class WGCloudDati

    Private Class FileLocale
        Public Property Relativo As String = ""
        Public Property Percorso As String = ""
        Public Property Modificato As Long = 0
        Public Property Dimensione As Long = 0
    End Class

    Private Class ContestoDati
        Public Property Radice As String = ""
        Public Property Client As FilenClient
        Public Property Elenco As List(Of FilenClient.FilenFile)
        Public Property Stato As Dictionary(Of String, Long())
        Public Property Inviati As Integer = 0
        Public Property Scaricati As Integer = 0
        Public Property Conflitti As Integer = 0
        Public Property InUso As Integer = 0
        Public Property Errori As Integer = 0
    End Class

    Private Const PrefissoNome As String = "wgdati|"
    Private Const NomePrincipale As String = "*"
    Private Const ChiaveDatiAbilitati As String = "SincronizzazioneCloudDati"
    Private Const ChiaveEscluse As String = "SincronizzazioneCloudDatiEscluse"
    Private Const ChiaveBackupSuCloud As String = "CloudBackupAbilitato"
    Private Const TolleranzaSecondi As Long = 2
    Private Const LimiteCicloSecondi As Integer = 600
    Private Const DimensioneBufferCopia As Integer = 1048576

    Private Shared ReadOnly _log As New Logger()
    Private Shared ReadOnly _bloccoDati As New Object()
    Private Shared ReadOnly _bloccoBackup As New Object()
    Private Shared ReadOnly _epoca As New DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc)
    Private Shared _riepilogo As String = ""
    Private Shared _registrata As Integer = 0

    Private Sub New()
    End Sub

    Friend Shared Sub Registra()
        Try
            If Interlocked.Exchange(_registrata, 1) = 1 Then Return
            WGSincronizzazioneCloud.RegistraProcedura("datiWinGabriel", AddressOf SincronizzaDati)
            AddHandler WGDb.WGBackup.BackupCompletato, AddressOf AlBackupCompletato
        Catch ex As Exception
            _log.ScriviLog("WGCloudDati.Registra: " & ex.ToString())
        End Try
    End Sub

    Friend Shared ReadOnly Property UltimoRiepilogo As String
        Get
            Return If(_riepilogo, "")
        End Get
    End Property

    Private Shared Function LeggiImpostazione(chiave As String) As String
        Try
            Dim configurazione As New WGConfig()
            If Not configurazione.EsisteId(chiave) Then Return ""
            Return If(configurazione.LeggiValore(chiave), "").Trim()
        Catch ex As Exception
            _log.ScriviLog("WGCloudDati.LeggiImpostazione (" & If(chiave, "") & "): " & ex.ToString())
            Return ""
        End Try
    End Function

    Private Shared Sub SalvaImpostazione(chiave As String, valore As String)
        Try
            Dim configurazione As New WGConfig()
            If configurazione.EsisteId(chiave) Then
                configurazione.ModificaValore(chiave, If(valore, ""))
            Else
                configurazione.AggiungiValore(chiave, If(valore, ""))
            End If
        Catch ex As Exception
            _log.ScriviLog("WGCloudDati.SalvaImpostazione (" & If(chiave, "") & "): " & ex.ToString())
        End Try
    End Sub

    Friend Shared Property DatiAbilitati As Boolean
        Get
            Return LeggiImpostazione(ChiaveDatiAbilitati) = "1"
        End Get
        Set(value As Boolean)
            SalvaImpostazione(ChiaveDatiAbilitati, If(value, "1", "0"))
        End Set
    End Property

    Friend Shared Property BackupSuCloud As Boolean
        Get
            Return LeggiImpostazione(ChiaveBackupSuCloud) = "1"
        End Get
        Set(value As Boolean)
            SalvaImpostazione(ChiaveBackupSuCloud, If(value, "1", "0"))
        End Set
    End Property

    Private Shared Function CartelleEscluseDallUtente() As HashSet(Of String)
        Dim insieme As New HashSet(Of String)(StringComparer.OrdinalIgnoreCase)
        Try
            For Each nome As String In LeggiImpostazione(ChiaveEscluse).Split(New Char() {";"c}, StringSplitOptions.RemoveEmptyEntries)
                If Not String.IsNullOrWhiteSpace(nome) Then insieme.Add(nome.Trim())
            Next
        Catch ex As Exception
            _log.ScriviLog("WGCloudDati.CartelleEscluseDallUtente: " & ex.ToString())
        End Try
        Return insieme
    End Function

    Private Shared Function CartelleSempreEscluse() As HashSet(Of String)
        Dim insieme As New HashSet(Of String)(StringComparer.OrdinalIgnoreCase)
        Try
            insieme.Add("config")
            insieme.Add("cloud")
            insieme.Add("backup")
            insieme.Add("libri")
            insieme.Add("wikisource")
            insieme.Add("internetarchive")
            insieme.Add("librivox")
            ' i suoni e il traduttore (programma e modelli) si scaricano di nuovo, non si sincronizzano
            insieme.Add("suoni")
            insieme.Add("traduttore")
            ' le notifiche, i brani scaricati e le cache di Navidrome e gli spartiti non si sincronizzano mai
            insieme.Add("notifiche")
            insieme.Add("navidrome")
            insieme.Add("spartiti")
            For Each nome As String In WGDb.WGBackup.CartelleEscluseDalSalvataggio()
                If Not String.IsNullOrWhiteSpace(nome) Then insieme.Add(nome.Trim())
            Next
        Catch ex As Exception
            _log.ScriviLog("WGCloudDati.CartelleSempreEscluse: " & ex.ToString())
        End Try
        Return insieme
    End Function

    ' Sottocartelle che non si sincronizzano mai, dentro cartelle che invece si sincronizzano: il programma di riconoscimento degli spartiti
    ' e gli episodi dei podcast scaricati.
    Private Shared ReadOnly PercorsiSempreEsclusi() As String = {"spartiti\audiveris", "podcast\episodi", "scrivania\File"}

    Private Shared Function PercorsoEscluso(relativo As String) As Boolean
        If String.IsNullOrEmpty(relativo) Then Return False
        Dim r As String = relativo.Replace("/"c, "\"c).TrimStart("\"c)
        For Each p As String In PercorsiSempreEsclusi
            If String.Equals(r, p, StringComparison.OrdinalIgnoreCase) OrElse r.StartsWith(p & "\", StringComparison.OrdinalIgnoreCase) Then Return True
        Next
        ' la chat: si sincronizza solo lo storico dei messaggi (chat\<nome utente>\storico). Lo stato del dispositivo (chat\<nome utente>\dispositivo:
        ' chiave d'identità, sessioni cifrate, coda) è di quel computer e basta: copiato su un altro rovinerebbe le sessioni di entrambi
        Dim parti As String() = r.Split("\"c)
        If parti.Length >= 3 AndAlso String.Equals(parti(0), "chat", StringComparison.OrdinalIgnoreCase) AndAlso String.Equals(parti(2), "dispositivo", StringComparison.OrdinalIgnoreCase) Then Return True
        Return False
    End Function

    ' Quanto pesano, dentro una cartella dei dati, le sottocartelle che non si sincronizzano mai.
    Private Shared Function DimensioneEsclusaDentro(radice As String, nomeCartella As String, ByRef quantiFile As Integer) As Long
        Dim totale As Long = 0
        For Each p As String In PercorsiSempreEsclusi
            If Not p.StartsWith(nomeCartella & "\", StringComparison.OrdinalIgnoreCase) Then Continue For
            Dim cartella As String = Path.Combine(radice, p)
            If Not Directory.Exists(cartella) Then Continue For
            Dim dimensione As Long = 0
            Dim conteggio As Integer = 0
            ContaCartella(cartella, dimensione, conteggio)
            totale += dimensione
            quantiFile += conteggio
        Next
        Return totale
    End Function

    Private Shared Function InsiemeEscluse() As HashSet(Of String)
        Dim insieme As HashSet(Of String) = CartelleSempreEscluse()
        Try
            For Each nome As String In CartelleEscluseDallUtente()
                insieme.Add(nome)
            Next
        Catch ex As Exception
            _log.ScriviLog("WGCloudDati.InsiemeEscluse: " & ex.ToString())
        End Try
        Return insieme
    End Function

    Friend Shared Sub ImpostaCartellaSincronizzata(nome As String, sincronizzata As Boolean)
        Try
            If String.IsNullOrWhiteSpace(nome) Then Return
            Dim escluse As HashSet(Of String) = CartelleEscluseDallUtente()
            If sincronizzata Then
                escluse.Remove(nome.Trim())
            Else
                escluse.Add(nome.Trim())
            End If
            SalvaImpostazione(ChiaveEscluse, String.Join(";", escluse))
        Catch ex As Exception
            _log.ScriviLog("WGCloudDati.ImpostaCartellaSincronizzata: " & ex.ToString())
        End Try
    End Sub

    Friend Shared ReadOnly Property NomeCartellaPrincipale As String
        Get
            Return NomePrincipale
        End Get
    End Property

    Friend Shared Function DescriviNomeCartella(nome As String) As String
        If String.Equals(nome, NomePrincipale, StringComparison.Ordinal) Then Return "File nella cartella principale dei dati"
        Return If(nome, "")
    End Function

    Private Shared Function RadiceDati() As String
        Try
            Dim radice As String = WGDb.WGBackup.CartellaRadiceDati()
            If String.IsNullOrWhiteSpace(radice) OrElse Not Directory.Exists(radice) Then Return ""
            Return Path.GetFullPath(radice).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
        Catch ex As Exception
            _log.ScriviLog("WGCloudDati.RadiceDati: " & ex.ToString())
            Return ""
        End Try
    End Function

    Private Shared Function CartellaImpostazioni() As String
        Try
            Dim percorso As String = AvvioAmministratore.PercorsoConfigurazione()
            If String.IsNullOrWhiteSpace(percorso) Then Return ""
            Return Path.GetDirectoryName(percorso)
        Catch ex As Exception
            _log.ScriviLog("WGCloudDati.CartellaImpostazioni: " & ex.ToString())
            Return ""
        End Try
    End Function

    Private Shared _vecchiaCartellaPortata As Boolean = False

    ' Il file di stato della sincronizzazione (e, se servono, le copie dei conflitti e i backup scaricati) stanno direttamente nella cartella
    ' della configurazione, senza una cartella "cloud" a parte.
    Private Shared Function CartellaLavoro() As String
        Try
            Dim impostazioni As String = CartellaImpostazioni()
            If String.IsNullOrWhiteSpace(impostazioni) Then impostazioni = Path.Combine(Application.StartupPath, "config")
            If Not Directory.Exists(impostazioni) Then Directory.CreateDirectory(impostazioni)
            PortaVecchiaCartellaCloud(impostazioni)
            Return impostazioni
        Catch ex As Exception
            _log.ScriviLog("WGCloudDati.CartellaLavoro: " & ex.ToString())
            Return Path.GetTempPath()
        End Try
    End Function

    ' Chi aveva già la cartella "cloud" (accanto alla configurazione) ritrova tutto nella configurazione, una volta sola.
    Private Shared Sub PortaVecchiaCartellaCloud(impostazioni As String)
        If _vecchiaCartellaPortata Then Return
        _vecchiaCartellaPortata = True
        Try
            Dim superiore As String = Path.GetDirectoryName(impostazioni)
            If String.IsNullOrWhiteSpace(superiore) Then Return
            Dim vecchia As String = Path.Combine(superiore, "cloud")
            If Not Directory.Exists(vecchia) Then Return

            Dim vecchioStato As String = Path.Combine(vecchia, "stato-dati.json")
            Dim nuovoStato As String = Path.Combine(impostazioni, "stato-dati.json")
            If File.Exists(vecchioStato) Then
                If File.Exists(nuovoStato) Then File.Delete(vecchioStato) Else File.Move(vecchioStato, nuovoStato)
            End If
            For Each nome As String In New String() {"conflitti", "scaricati"}
                Dim daSpostare As String = Path.Combine(vecchia, nome)
                Dim destinazione As String = Path.Combine(impostazioni, nome)
                If Directory.Exists(daSpostare) AndAlso Not Directory.Exists(destinazione) Then Directory.Move(daSpostare, destinazione)
            Next
            ' la vecchia cartella si toglie solo se è rimasta vuota
            If Directory.GetFileSystemEntries(vecchia).Length = 0 Then Directory.Delete(vecchia)
        Catch ex As Exception
            _log.ScriviLog("WGCloudDati.PortaVecchiaCartellaCloud: " & ex.Message)
        End Try
    End Sub

    Private Shared Function PercorsoStato() As String
        Return Path.Combine(CartellaLavoro(), "stato-dati.json")
    End Function

    Private Shared Function DaUnix(secondi As Long) As DateTime
        Return _epoca.AddSeconds(secondi)
    End Function

    Private Shared Function InUnix(data As DateTime) As Long
        Return CLng(Math.Floor((data.ToUniversalTime() - _epoca).TotalSeconds))
    End Function

    Private Shared Function NomeRemoto(relativo As String) As String
        Return PrefissoNome & relativo.Replace("\"c, "|"c)
    End Function

    Private Shared Function PrimoSegmento(relativo As String) As String
        Dim indice As Integer = relativo.IndexOf("\"c)
        If indice < 0 Then Return NomePrincipale
        Return relativo.Substring(0, indice)
    End Function

    Private Shared Function RelativoValido(relativo As String) As Boolean
        Try
            If String.IsNullOrWhiteSpace(relativo) Then Return False
            If Path.IsPathRooted(relativo) Then Return False
            Dim invalidi As Char() = Path.GetInvalidFileNameChars()
            For Each segmento As String In relativo.Split("\"c)
                If segmento.Length = 0 OrElse segmento = "." OrElse segmento = ".." Then Return False
                If segmento.IndexOfAny(invalidi) >= 0 Then Return False
            Next
            Return True
        Catch ex As Exception
            _log.ScriviLog("WGCloudDati.RelativoValido: " & ex.ToString())
            Return False
        End Try
    End Function

    Private Shared Function FileDaIgnorare(nomeFile As String) As Boolean
        Try
            Dim estensione As String = Path.GetExtension(nomeFile)
            If String.Equals(estensione, ".tmp", StringComparison.OrdinalIgnoreCase) Then Return True
            If String.Equals(estensione, ".bak", StringComparison.OrdinalIgnoreCase) Then Return True
            If String.Equals(estensione, ".importato", StringComparison.OrdinalIgnoreCase) Then Return True
            If nomeFile.EndsWith("-shm", StringComparison.OrdinalIgnoreCase) Then Return True
            If nomeFile.EndsWith("-wal", StringComparison.OrdinalIgnoreCase) Then Return True
            If nomeFile.EndsWith("-journal", StringComparison.OrdinalIgnoreCase) Then Return True
            If nomeFile.EndsWith(".inCorso", StringComparison.OrdinalIgnoreCase) Then Return True
            Return False
        Catch ex As Exception
            _log.ScriviLog("WGCloudDati.FileDaIgnorare: " & ex.ToString())
            Return True
        End Try
    End Function

    Private Shared Sub AggiungiFileLocale(radice As String, percorso As String, locali As Dictionary(Of String, FileLocale))
        Try
            If FileDaIgnorare(Path.GetFileName(percorso)) Then Return
            Dim informazioni As New FileInfo(percorso)
            If informazioni.Length = 0 Then Return
            Dim relativo As String = percorso.Substring(radice.Length).TrimStart(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
            If Not RelativoValido(relativo) Then Return
            If PercorsoEscluso(relativo) Then Return
            Dim voce As New FileLocale()
            voce.Relativo = relativo
            voce.Percorso = percorso
            voce.Modificato = InUnix(informazioni.LastWriteTimeUtc)
            voce.Dimensione = informazioni.Length
            locali(relativo) = voce
        Catch ex As Exception
            _log.ScriviLog("WGCloudDati.AggiungiFileLocale (" & If(percorso, "") & "): " & ex.ToString())
        End Try
    End Sub

    Private Shared Sub ScansionaCartella(radice As String, cartella As String, locali As Dictionary(Of String, FileLocale), ct As CancellationToken)
        Dim daVisitare As New Stack(Of String)()
        daVisitare.Push(cartella)
        While daVisitare.Count > 0
            If ct.IsCancellationRequested Then Return
            Dim corrente As String = daVisitare.Pop()
            Try
                For Each percorso As String In Directory.GetFiles(corrente)
                    AggiungiFileLocale(radice, percorso, locali)
                Next
                For Each sotto As String In Directory.GetDirectories(corrente)
                    Try
                        If (File.GetAttributes(sotto) And FileAttributes.ReparsePoint) = FileAttributes.ReparsePoint Then Continue For
                        If PercorsoEscluso(sotto.Substring(radice.Length).TrimStart(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)) Then Continue For
                        daVisitare.Push(sotto)
                    Catch exSotto As Exception
                        _log.ScriviLog("WGCloudDati.ScansionaCartella (" & sotto & "): " & exSotto.Message)
                    End Try
                Next
            Catch ex As Exception
                _log.ScriviLog("WGCloudDati.ScansionaCartella (" & corrente & "): " & ex.Message)
            End Try
        End While
    End Sub

    Private Shared Function ScansionaLocali(radice As String, escluse As HashSet(Of String), ct As CancellationToken) As Dictionary(Of String, FileLocale)
        Dim locali As New Dictionary(Of String, FileLocale)(StringComparer.OrdinalIgnoreCase)
        Try
            If Not escluse.Contains(NomePrincipale) Then
                For Each percorso As String In Directory.GetFiles(radice)
                    AggiungiFileLocale(radice, percorso, locali)
                Next
            End If
            For Each cartella As String In Directory.GetDirectories(radice)
                Try
                    If ct.IsCancellationRequested Then Exit For
                    Dim nome As String = Path.GetFileName(cartella)
                    If escluse.Contains(nome) Then Continue For
                    If (File.GetAttributes(cartella) And FileAttributes.ReparsePoint) = FileAttributes.ReparsePoint Then Continue For
                    ScansionaCartella(radice, cartella, locali, ct)
                Catch exCartella As Exception
                    _log.ScriviLog("WGCloudDati.ScansionaLocali (" & cartella & "): " & exCartella.Message)
                End Try
            Next
        Catch ex As Exception
            _log.ScriviLog("WGCloudDati.ScansionaLocali: " & ex.ToString())
        End Try
        Return locali
    End Function

    Private Shared Function MappaRemoti(elenco As List(Of FilenClient.FilenFile), escluse As HashSet(Of String), daCestinare As List(Of String)) As Dictionary(Of String, FilenClient.FilenFile)
        Dim remoti As New Dictionary(Of String, FilenClient.FilenFile)(StringComparer.OrdinalIgnoreCase)
        Try
            For Each voce As FilenClient.FilenFile In elenco
                Try
                    If voce Is Nothing OrElse String.IsNullOrEmpty(voce.Nome) Then Continue For
                    If Not voce.Nome.StartsWith(PrefissoNome, StringComparison.Ordinal) Then Continue For
                    Dim relativo As String = voce.Nome.Substring(PrefissoNome.Length).Replace("|"c, "\"c)
                    If Not RelativoValido(relativo) Then Continue For
                    If PercorsoEscluso(relativo) Then Continue For
                    If FileDaIgnorare(Path.GetFileName(relativo)) Then Continue For
                    If escluse.Contains(PrimoSegmento(relativo)) Then Continue For
                    Dim esistente As FilenClient.FilenFile = Nothing
                    If remoti.TryGetValue(relativo, esistente) Then
                        If voce.Timestamp > esistente.Timestamp Then
                            daCestinare.Add(esistente.Uuid)
                            remoti(relativo) = voce
                        Else
                            daCestinare.Add(voce.Uuid)
                        End If
                    Else
                        remoti(relativo) = voce
                    End If
                Catch exVoce As Exception
                    _log.ScriviLog("WGCloudDati.MappaRemoti voce: " & exVoce.ToString())
                End Try
            Next
        Catch ex As Exception
            _log.ScriviLog("WGCloudDati.MappaRemoti: " & ex.ToString())
        End Try
        Return remoti
    End Function

    Private Shared Function CaricaStato() As Dictionary(Of String, Long())
        Dim stato As New Dictionary(Of String, Long())(StringComparer.OrdinalIgnoreCase)
        Try
            Dim percorso As String = PercorsoStato()
            If Not File.Exists(percorso) Then Return stato
            Dim radice As JObject = JObject.Parse(File.ReadAllText(percorso, Encoding.UTF8))
            Dim voci As JObject = TryCast(radice("file"), JObject)
            If voci Is Nothing Then Return stato
            For Each proprieta As JProperty In voci.Properties()
                Dim valori As JArray = TryCast(proprieta.Value, JArray)
                If valori Is Nothing OrElse valori.Count < 2 Then Continue For
                stato(proprieta.Name) = New Long() {CLng(valori(0)), CLng(valori(1))}
            Next
        Catch ex As Exception
            _log.ScriviLog("WGCloudDati.CaricaStato: " & ex.ToString())
            stato.Clear()
        End Try
        Return stato
    End Function

    Private Shared Sub SalvaStato(stato As Dictionary(Of String, Long()))
        Try
            Dim voci As New JObject()
            For Each coppia As KeyValuePair(Of String, Long()) In stato
                Dim valori As New JArray()
                valori.Add(coppia.Value(0))
                valori.Add(coppia.Value(1))
                voci(coppia.Key) = valori
            Next
            Dim radice As New JObject()
            radice("file") = voci
            Dim percorso As String = PercorsoStato()
            Dim temporaneo As String = percorso & "." & Guid.NewGuid().ToString("N") & ".tmp"
            File.WriteAllText(temporaneo, radice.ToString(Newtonsoft.Json.Formatting.None), New UTF8Encoding(False))
            If File.Exists(percorso) Then
                File.Replace(temporaneo, percorso, Nothing)
            Else
                File.Move(temporaneo, percorso)
            End If
        Catch ex As Exception
            _log.ScriviLog("WGCloudDati.SalvaStato: " & ex.ToString())
        End Try
    End Sub

    Friend Shared Sub SincronizzaDati(client As FilenClient, elenco As List(Of FilenClient.FilenFile), ct As CancellationToken, esito As WGSincronizzazioneCloud.EsitoSincronizzazione)
        Dim acquisito As Boolean = False
        Try
            If Not DatiAbilitati Then Return
            Monitor.TryEnter(_bloccoDati, acquisito)
            If Not acquisito Then Return

            Dim radice As String = RadiceDati()
            If String.IsNullOrWhiteSpace(radice) Then Return

            Dim escluse As HashSet(Of String) = InsiemeEscluse()
            Dim locali As Dictionary(Of String, FileLocale) = ScansionaLocali(radice, escluse, ct)
            Dim daCestinare As New List(Of String)()
            Dim remoti As Dictionary(Of String, FilenClient.FilenFile) = MappaRemoti(elenco, escluse, daCestinare)

            Dim contesto As New ContestoDati()
            contesto.Radice = radice
            contesto.Client = client
            contesto.Elenco = elenco
            contesto.Stato = CaricaStato()

            Dim chiavi As New SortedSet(Of String)(StringComparer.OrdinalIgnoreCase)
            For Each chiave As String In locali.Keys
                chiavi.Add(chiave)
            Next
            For Each chiave As String In remoti.Keys
                chiavi.Add(chiave)
            Next

            Dim interrotto As Boolean = False
            Dim erroriConsecutivi As Integer = 0
            Using limite As CancellationTokenSource = CancellationTokenSource.CreateLinkedTokenSource(ct)
                limite.CancelAfter(TimeSpan.FromSeconds(LimiteCicloSecondi))
                For Each relativo As String In chiavi
                    If limite.IsCancellationRequested Then
                        interrotto = True
                        Exit For
                    End If
                    Try
                        Dim locale As FileLocale = Nothing
                        Dim remoto As FilenClient.FilenFile = Nothing
                        locali.TryGetValue(relativo, locale)
                        remoti.TryGetValue(relativo, remoto)
                        ElaboraVoce(contesto, relativo, locale, remoto, limite.Token)
                        erroriConsecutivi = 0
                    Catch exAnnullato As OperationCanceledException
                        interrotto = True
                        Exit For
                    Catch exVoce As Exception
                        contesto.Errori += 1
                        erroriConsecutivi += 1
                        _log.ScriviLog("WGCloudDati.SincronizzaDati (" & relativo & "): " & exVoce.ToString())
                        If erroriConsecutivi >= 3 Then
                            interrotto = True
                            Exit For
                        End If
                    End Try
                Next
            End Using

            SalvaStato(contesto.Stato)

            For Each uuid As String In daCestinare
                Try
                    If ct.IsCancellationRequested Then Exit For
                    client.CestinaFileAsync(uuid, ct).GetAwaiter().GetResult()
                Catch exCestino As Exception
                    _log.ScriviLog("WGCloudDati.SincronizzaDati cestino: " & exCestino.Message)
                End Try
            Next

            Dim riepilogo As New StringBuilder()
            riepilogo.Append("Dati di WinGabriel: " & contesto.Inviati.ToString(CultureInfo.InvariantCulture) & " file inviati, " & contesto.Scaricati.ToString(CultureInfo.InvariantCulture) & " scaricati")
            If contesto.Conflitti > 0 Then riepilogo.Append(", " & contesto.Conflitti.ToString(CultureInfo.InvariantCulture) & " con modifiche su più computer")
            If contesto.InUso > 0 Then riepilogo.Append(", " & contesto.InUso.ToString(CultureInfo.InvariantCulture) & " in uso e rimandati")
            If contesto.Errori > 0 Then riepilogo.Append(", " & contesto.Errori.ToString(CultureInfo.InvariantCulture) & " non riusciti")
            riepilogo.Append(".")
            _riepilogo = riepilogo.ToString()

            If esito Is Nothing Then Return
            If contesto.Conflitti > 0 Then esito.Avvisi.Add("Su alcuni file dei dati c'erano modifiche su più computer: è stata tenuta la versione più recente. Se è stata sostituita la copia di questo computer, la trovi nella cartella dei conflitti del cloud.")
            If contesto.InUso > 0 Then esito.Avvisi.Add("Alcuni file dei dati erano in uso e sono stati rimandati al prossimo passaggio.")
            If contesto.Errori > 0 Then esito.Avvisi.Add("Non è stato possibile sincronizzare alcuni file dei dati.")
            If interrotto Then esito.Avvisi.Add("La sincronizzazione dei dati è lunga: prosegue al prossimo passaggio.")
        Catch ex As Exception
            _log.ScriviLog("WGCloudDati.SincronizzaDati: " & ex.ToString())
            If esito IsNot Nothing Then esito.Avvisi.Add("Non è stato possibile sincronizzare i dati di WinGabriel.")
        Finally
            If acquisito Then
                Try
                    Monitor.Exit(_bloccoDati)
                Catch exUscita As Exception
                    _log.ScriviLog("WGCloudDati.SincronizzaDati uscita: " & exUscita.Message)
                End Try
            End If
        End Try
    End Sub

    Private Shared Sub ElaboraVoce(contesto As ContestoDati, relativo As String, locale As FileLocale, remoto As FilenClient.FilenFile, ct As CancellationToken)
        Dim stato As Long() = Nothing
        contesto.Stato.TryGetValue(relativo, stato)

        If locale IsNot Nothing AndAlso remoto Is Nothing Then
            InviaFile(contesto, relativo, locale, ct)
            Return
        End If
        If locale Is Nothing AndAlso remoto IsNot Nothing Then
            ScaricaFile(contesto, relativo, remoto, ct)
            Return
        End If
        If locale Is Nothing OrElse remoto Is Nothing Then Return

        If Math.Abs(locale.Modificato - remoto.Modificato) <= TolleranzaSecondi Then
            contesto.Stato(relativo) = New Long() {locale.Modificato, remoto.Modificato}
            Return
        End If

        Dim localeCambiato As Boolean = (stato Is Nothing) OrElse Math.Abs(locale.Modificato - stato(0)) > TolleranzaSecondi
        Dim remotoCambiato As Boolean = (stato Is Nothing) OrElse Math.Abs(remoto.Modificato - stato(1)) > TolleranzaSecondi

        If localeCambiato AndAlso Not remotoCambiato Then
            InviaFile(contesto, relativo, locale, ct)
        ElseIf remotoCambiato AndAlso Not localeCambiato Then
            ScaricaFile(contesto, relativo, remoto, ct)
        Else
            contesto.Conflitti += 1
            If locale.Modificato >= remoto.Modificato Then
                InviaFile(contesto, relativo, locale, ct)
            Else
                SalvaCopiaConflitto(locale)
                ScaricaFile(contesto, relativo, remoto, ct)
            End If
        End If
    End Sub

    Private Shared Sub InviaFile(contesto As ContestoDati, relativo As String, locale As FileLocale, ct As CancellationToken)
        Dim temporaneo As String = Path.Combine(Path.GetTempPath(), "wgcloud-" & Guid.NewGuid().ToString("N") & ".tmp")
        Try
            Dim eDatabase As Boolean = WGDb.WGBackup.ConsolidaDatabase(locale.Percorso)
            Using origine As New FileStream(locale.Percorso, FileMode.Open, FileAccess.Read, FileShare.ReadWrite)
                Using destinazione As New FileStream(temporaneo, FileMode.Create, FileAccess.Write, FileShare.None)
                    origine.CopyTo(destinazione, DimensioneBufferCopia)
                End Using
            End Using
            If New FileInfo(temporaneo).Length = 0 Then Return

            Dim nome As String = NomeRemoto(relativo)
            Dim nuovo As String = contesto.Client.CaricaFileAsync(nome, temporaneo, locale.Modificato, ct).GetAwaiter().GetResult()

            For Each vecchio As FilenClient.FilenFile In contesto.Elenco
                Try
                    If vecchio Is Nothing Then Continue For
                    If Not String.Equals(vecchio.Nome, nome, StringComparison.Ordinal) Then Continue For
                    If String.Equals(vecchio.Uuid, nuovo, StringComparison.Ordinal) Then Continue For
                    contesto.Client.CestinaFileAsync(vecchio.Uuid, ct).GetAwaiter().GetResult()
                Catch exAnnullata As OperationCanceledException
                    Throw
                Catch exVecchia As Exception
                    ' Filen tiene da solo le versioni precedenti di un file con lo stesso nome e per questo rifiuta di cestinarle
                    ' (This file is versioned): non è un errore, la versione nuova è già quella in uso.
                    If exVecchia.Message.IndexOf("versioned", StringComparison.OrdinalIgnoreCase) < 0 Then
                        _log.ScriviLog("WGCloudDati.InviaFile versione precedente (" & nome & "): " & exVecchia.Message)
                    End If
                End Try
            Next

            contesto.Stato(relativo) = New Long() {locale.Modificato, locale.Modificato}
            contesto.Inviati += 1
        Finally
            Try
                If File.Exists(temporaneo) Then File.Delete(temporaneo)
            Catch exPulizia As Exception
                _log.ScriviLog("WGCloudDati.InviaFile pulizia: " & exPulizia.Message)
            End Try
        End Try
    End Sub

    Private Shared Sub ScaricaFile(contesto As ContestoDati, relativo As String, remoto As FilenClient.FilenFile, ct As CancellationToken)
        Dim destinazione As String = Path.GetFullPath(Path.Combine(contesto.Radice, relativo))
        If Not destinazione.StartsWith(contesto.Radice & Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) Then
            Throw New InvalidOperationException("Percorso fuori dalla cartella dei dati: " & relativo)
        End If
        Dim cartella As String = Path.GetDirectoryName(destinazione)
        If Not Directory.Exists(cartella) Then Directory.CreateDirectory(cartella)

        Dim temporaneo As String = destinazione & "." & Guid.NewGuid().ToString("N") & ".tmp"
        Dim sostituito As Boolean = False
        Try
            contesto.Client.ScaricaFileAsync(remoto, temporaneo, ct).GetAwaiter().GetResult()
            If New FileInfo(temporaneo).Length = 0 Then Throw New InvalidOperationException("File scaricato vuoto: " & relativo)

            Try
                If File.Exists(destinazione) Then
                    File.Replace(temporaneo, destinazione, Nothing)
                Else
                    File.Move(temporaneo, destinazione)
                End If
                sostituito = True
            Catch exUso As IOException
                contesto.InUso += 1
            Catch exAccesso As UnauthorizedAccessException
                contesto.InUso += 1
            End Try

            If Not sostituito Then Return

            File.SetLastWriteTimeUtc(destinazione, DaUnix(remoto.Modificato))
            Try
                If File.Exists(destinazione & "-wal") Then File.Delete(destinazione & "-wal")
                If File.Exists(destinazione & "-shm") Then File.Delete(destinazione & "-shm")
            Catch exScia As Exception
                _log.ScriviLog("WGCloudDati.ScaricaFile pulizia wal (" & destinazione & "): " & exScia.Message)
            End Try
            contesto.Stato(relativo) = New Long() {remoto.Modificato, remoto.Modificato}
            contesto.Scaricati += 1
        Finally
            Try
                If File.Exists(temporaneo) Then File.Delete(temporaneo)
            Catch exPulizia As Exception
                _log.ScriviLog("WGCloudDati.ScaricaFile pulizia: " & exPulizia.Message)
            End Try
        End Try
    End Sub

    Private Shared Sub SalvaCopiaConflitto(locale As FileLocale)
        Try
            Dim cartella As String = Path.Combine(CartellaLavoro(), "conflitti")
            If Not Directory.Exists(cartella) Then Directory.CreateDirectory(cartella)
            Dim nome As String = locale.Relativo.Replace("\"c, "_"c) & "." & DateTime.Now.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture)
            Using origine As New FileStream(locale.Percorso, FileMode.Open, FileAccess.Read, FileShare.ReadWrite)
                Using destinazione As New FileStream(Path.Combine(cartella, nome), FileMode.Create, FileAccess.Write, FileShare.None)
                    origine.CopyTo(destinazione, DimensioneBufferCopia)
                End Using
            End Using
        Catch ex As Exception
            _log.ScriviLog("WGCloudDati.SalvaCopiaConflitto (" & If(locale Is Nothing, "", locale.Relativo) & "): " & ex.ToString())
        End Try
    End Sub

    Private Shared Sub ContaCartella(percorso As String, ByRef dimensione As Long, ByRef quantiFile As Integer)
        Dim daVisitare As New Stack(Of String)()
        daVisitare.Push(percorso)
        While daVisitare.Count > 0
            Dim corrente As String = daVisitare.Pop()
            Try
                For Each voce As String In Directory.GetFiles(corrente)
                    Try
                        dimensione += New FileInfo(voce).Length
                        quantiFile += 1
                    Catch exFile As Exception
                        _log.ScriviLog("WGCloudDati.ContaCartella file (" & voce & "): " & exFile.Message)
                    End Try
                Next
                For Each sotto As String In Directory.GetDirectories(corrente)
                    Try
                        If (File.GetAttributes(sotto) And FileAttributes.ReparsePoint) = FileAttributes.ReparsePoint Then Continue For
                        daVisitare.Push(sotto)
                    Catch exSotto As Exception
                        _log.ScriviLog("WGCloudDati.ContaCartella cartella (" & sotto & "): " & exSotto.Message)
                    End Try
                Next
            Catch ex As Exception
                _log.ScriviLog("WGCloudDati.ContaCartella (" & corrente & "): " & ex.Message)
            End Try
        End While
    End Sub

    Friend Shared Function ElencoCartelle() As List(Of CartellaDatiCloud)
        Dim risultato As New List(Of CartellaDatiCloud)()
        Try
            Dim radice As String = RadiceDati()
            If String.IsNullOrWhiteSpace(radice) Then Return risultato
            Dim fisse As HashSet(Of String) = CartelleSempreEscluse()
            Dim utente As HashSet(Of String) = CartelleEscluseDallUtente()

            Dim principale As New CartellaDatiCloud()
            principale.Nome = NomePrincipale
            For Each voce As String In Directory.GetFiles(radice)
                Try
                    If FileDaIgnorare(Path.GetFileName(voce)) Then Continue For
                    principale.Dimensione += New FileInfo(voce).Length
                    principale.File += 1
                Catch exFile As Exception
                    _log.ScriviLog("WGCloudDati.ElencoCartelle file: " & exFile.Message)
                End Try
            Next
            principale.Inclusa = Not utente.Contains(NomePrincipale)
            If principale.File > 0 Then risultato.Add(principale)

            For Each cartella As String In Directory.GetDirectories(radice)
                Try
                    Dim nome As String = Path.GetFileName(cartella)
                    If fisse.Contains(nome) Then Continue For
                    Dim voce As New CartellaDatiCloud()
                    voce.Nome = nome
                    ContaCartella(cartella, voce.Dimensione, voce.File)
                    Dim fileEsclusi As Integer = 0
                    voce.Dimensione -= DimensioneEsclusaDentro(radice, nome, fileEsclusi)
                    voce.File -= fileEsclusi
                    voce.Inclusa = Not utente.Contains(nome)
                    risultato.Add(voce)
                Catch exCartella As Exception
                    _log.ScriviLog("WGCloudDati.ElencoCartelle (" & cartella & "): " & exCartella.Message)
                End Try
            Next
        Catch ex As Exception
            _log.ScriviLog("WGCloudDati.ElencoCartelle: " & ex.ToString())
        End Try
        Return risultato
    End Function

    Friend Shared Sub AlBackupCompletato(percorsoArchivio As String)
        Try
            If Not BackupSuCloud Then Return
            If Not WGSincronizzazioneCloud.Collegato Then Return
            Dim lavoro As New LavoroBackupCloud(percorsoArchivio)
            Task.Run(New Action(AddressOf lavoro.Esegui))
        Catch ex As Exception
            _log.ScriviLog("WGCloudDati.AlBackupCompletato: " & ex.ToString())
        End Try
    End Sub

    Private NotInheritable Class LavoroBackupCloud

        Private ReadOnly _percorso As String

        Public Sub New(percorso As String)
            _percorso = percorso
        End Sub

        Public Sub Esegui()
            Try
                Dim messaggio As String = ""
                WGCloudDati.CaricaBackupSulCloud(_percorso, messaggio)
            Catch ex As Exception
                Dim registro As New Logger()
                registro.ScriviLog("LavoroBackupCloud.Esegui: " & ex.ToString())
            End Try
        End Sub

    End Class

    Friend Shared Function UltimoBackupLocale() As String
        Try
            Dim elenco As List(Of WGDb.InfoBackup) = WGDb.WGBackup.ElencoBackup(WGDb.WGBackup.CartellaBackup)
            If elenco Is Nothing OrElse elenco.Count = 0 Then Return ""
            Return elenco(0).Percorso
        Catch ex As Exception
            _log.ScriviLog("WGCloudDati.UltimoBackupLocale: " & ex.ToString())
            Return ""
        End Try
    End Function

    Friend Shared Function CaricaBackupSulCloud(percorsoArchivio As String, ByRef messaggio As String) As Boolean
        messaggio = ""
        Dim acquisito As Boolean = False
        Try
            If String.IsNullOrWhiteSpace(percorsoArchivio) OrElse Not File.Exists(percorsoArchivio) Then
                messaggio = "Non è stato trovato nessun backup da caricare."
                Return False
            End If
            Monitor.TryEnter(_bloccoBackup, acquisito)
            If Not acquisito Then
                messaggio = "È già in corso il caricamento di un backup."
                Return False
            End If

            Dim client As FilenClient = WGSincronizzazioneCloud.NuovoClient()
            If client Is Nothing Then
                messaggio = "Non è stato eseguito l'accesso a Filen."
                Return False
            End If

            Dim nome As String = Path.GetFileName(percorsoArchivio)
            Dim informazioni As New FileInfo(percorsoArchivio)
            Dim elenco As List(Of FilenClient.FilenFile) = client.ElencaAsync(CancellationToken.None).GetAwaiter().GetResult()
            Dim giaPresente As Boolean = False
            For Each voce As FilenClient.FilenFile In elenco
                If String.Equals(voce.Nome, nome, StringComparison.Ordinal) Then
                    giaPresente = True
                    Exit For
                End If
            Next

            If Not giaPresente Then
                client.CaricaFileAsync(nome, percorsoArchivio, InUnix(informazioni.LastWriteTimeUtc), CancellationToken.None).GetAwaiter().GetResult()
            End If

            Try
                TieniSoloQuestoBackup(client, nome)
            Catch exPulizia As Exception
                _log.ScriviLog("WGCloudDati.CaricaBackupSulCloud pulizia: " & exPulizia.ToString())
            End Try

            If giaPresente Then
                messaggio = "Questo backup è già sul cloud. Sul cloud resta solo questo."
            Else
                messaggio = "Il backup è stato copiato sul cloud, " & WGDb.WGBackup.DescriviDimensione(informazioni.Length) & ". Sul cloud resta solo questo: i backup precedenti sono stati tolti per non occupare spazio."
            End If
            Return True
        Catch ex As Exception
            _log.ScriviLog("WGCloudDati.CaricaBackupSulCloud: " & ex.ToString())
            messaggio = WGSincronizzazioneCloud.DescriviErrore(ex, "copiare il backup sul cloud")
            Return False
        Finally
            If acquisito Then
                Try
                    Monitor.Exit(_bloccoBackup)
                Catch exUscita As Exception
                    _log.ScriviLog("WGCloudDati.CaricaBackupSulCloud uscita: " & exUscita.Message)
                End Try
            End If
        End Try
    End Function

    Private Shared Function DataDaNome(nome As String, ripiego As DateTime) As DateTime
        Try
            Dim testo As String = nome
            If testo.StartsWith(WGDb.WGBackup.PrefissoArchivio, StringComparison.OrdinalIgnoreCase) Then testo = testo.Substring(WGDb.WGBackup.PrefissoArchivio.Length)
            If testo.EndsWith(WGDb.WGBackup.EstensioneArchivio, StringComparison.OrdinalIgnoreCase) Then testo = testo.Substring(0, testo.Length - WGDb.WGBackup.EstensioneArchivio.Length)
            Dim valore As DateTime = DateTime.MinValue
            If DateTime.TryParseExact(testo, "yyyyMMdd-HHmmss", CultureInfo.InvariantCulture, DateTimeStyles.None, valore) Then Return valore
            Return ripiego
        Catch ex As Exception
            _log.ScriviLog("WGCloudDati.DataDaNome: " & ex.ToString())
            Return ripiego
        End Try
    End Function

    Private Shared Function ConfrontaBackupCloud(a As InfoBackupCloud, b As InfoBackupCloud) As Integer
        Return b.Data.CompareTo(a.Data)
    End Function

    Private Shared Function BackupDaElenco(elenco As List(Of FilenClient.FilenFile), duplicati As List(Of String)) As List(Of InfoBackupCloud)
        Dim risultato As New List(Of InfoBackupCloud)()
        Try
            Dim perNome As New Dictionary(Of String, FilenClient.FilenFile)(StringComparer.Ordinal)
            For Each voce As FilenClient.FilenFile In elenco
                If voce Is Nothing OrElse String.IsNullOrEmpty(voce.Nome) Then Continue For
                If Not voce.Nome.StartsWith(WGDb.WGBackup.PrefissoArchivio, StringComparison.OrdinalIgnoreCase) Then Continue For
                If Not voce.Nome.EndsWith(WGDb.WGBackup.EstensioneArchivio, StringComparison.OrdinalIgnoreCase) Then Continue For
                Dim esistente As FilenClient.FilenFile = Nothing
                If perNome.TryGetValue(voce.Nome, esistente) Then
                    If voce.Timestamp > esistente.Timestamp Then
                        If duplicati IsNot Nothing Then duplicati.Add(esistente.Uuid)
                        perNome(voce.Nome) = voce
                    ElseIf duplicati IsNot Nothing Then
                        duplicati.Add(voce.Uuid)
                    End If
                Else
                    perNome(voce.Nome) = voce
                End If
            Next
            For Each voce As FilenClient.FilenFile In perNome.Values
                Dim info As New InfoBackupCloud()
                info.Nome = voce.Nome
                info.Uuid = voce.Uuid
                info.Dimensione = voce.Dimensione
                info.Data = DataDaNome(voce.Nome, DaUnix(voce.Modificato).ToLocalTime())
                risultato.Add(info)
            Next
            risultato.Sort(AddressOf ConfrontaBackupCloud)
        Catch ex As Exception
            _log.ScriviLog("WGCloudDati.BackupDaElenco: " & ex.ToString())
        End Try
        Return risultato
    End Function

    Private Shared Sub TieniSoloQuestoBackup(client As FilenClient, nome As String)
        Dim elenco As List(Of FilenClient.FilenFile) = client.ElencaAsync(CancellationToken.None).GetAwaiter().GetResult()
        Dim conservato As FilenClient.FilenFile = FilenClient.PiuRecente(elenco, nome)
        If conservato Is Nothing Then Return
        For Each voce As FilenClient.FilenFile In elenco
            Try
                If voce Is Nothing OrElse String.IsNullOrEmpty(voce.Nome) Then Continue For
                If Not voce.Nome.StartsWith(WGDb.WGBackup.PrefissoArchivio, StringComparison.OrdinalIgnoreCase) Then Continue For
                If Not voce.Nome.EndsWith(WGDb.WGBackup.EstensioneArchivio, StringComparison.OrdinalIgnoreCase) Then Continue For
                If String.Equals(voce.Uuid, conservato.Uuid, StringComparison.Ordinal) Then Continue For
                client.CestinaFileAsync(voce.Uuid, CancellationToken.None).GetAwaiter().GetResult()
            Catch exVoce As Exception
                _log.ScriviLog("WGCloudDati.TieniSoloQuestoBackup (" & If(voce Is Nothing, "", voce.Nome) & "): " & exVoce.Message)
            End Try
        Next
    End Sub

    Friend Shared Function EliminaBackupDalCloud(nome As String, ByRef messaggio As String) As Boolean
        messaggio = ""
        Try
            Dim client As FilenClient = WGSincronizzazioneCloud.NuovoClient()
            If client Is Nothing Then
                messaggio = "Non è stato eseguito l'accesso a Filen."
                Return False
            End If
            Dim elenco As List(Of FilenClient.FilenFile) = client.ElencaAsync(CancellationToken.None).GetAwaiter().GetResult()
            Dim eliminati As Integer = 0
            For Each voce As FilenClient.FilenFile In elenco
                If voce Is Nothing Then Continue For
                If Not String.Equals(voce.Nome, nome, StringComparison.Ordinal) Then Continue For
                client.CestinaFileAsync(voce.Uuid, CancellationToken.None).GetAwaiter().GetResult()
                eliminati += 1
            Next
            If eliminati = 0 Then
                messaggio = "Il backup non è più sul cloud."
                Return False
            End If
            messaggio = "Il backup è stato eliminato dal cloud."
            Return True
        Catch ex As Exception
            _log.ScriviLog("WGCloudDati.EliminaBackupDalCloud: " & ex.ToString())
            messaggio = WGSincronizzazioneCloud.DescriviErrore(ex, "eliminare il backup dal cloud")
            Return False
        End Try
    End Function

    Friend Shared Function ElencoBackupCloud(ByRef messaggio As String) As List(Of InfoBackupCloud)
        messaggio = ""
        Try
            Dim client As FilenClient = WGSincronizzazioneCloud.NuovoClient()
            If client Is Nothing Then
                messaggio = "Non è stato eseguito l'accesso a Filen."
                Return New List(Of InfoBackupCloud)()
            End If
            Dim elenco As List(Of FilenClient.FilenFile) = client.ElencaAsync(CancellationToken.None).GetAwaiter().GetResult()
            Return BackupDaElenco(elenco, Nothing)
        Catch ex As Exception
            _log.ScriviLog("WGCloudDati.ElencoBackupCloud: " & ex.ToString())
            messaggio = WGSincronizzazioneCloud.DescriviErrore(ex, "leggere i backup sul cloud")
            Return New List(Of InfoBackupCloud)()
        End Try
    End Function

    Friend Shared Function ScaricaBackupDalCloud(nome As String, ByRef percorso As String, ByRef messaggio As String) As Boolean
        percorso = ""
        messaggio = ""
        Dim temporaneo As String = ""
        Try
            Dim client As FilenClient = WGSincronizzazioneCloud.NuovoClient()
            If client Is Nothing Then
                messaggio = "Non è stato eseguito l'accesso a Filen."
                Return False
            End If

            Dim cartella As String = WGDb.WGBackup.CartellaBackup
            If String.IsNullOrWhiteSpace(cartella) OrElse Not Directory.Exists(cartella) OrElse Not WGDb.WGDatabase.VerificaCartellaScrivibile(cartella) Then
                cartella = Path.Combine(CartellaLavoro(), "scaricati")
                If Not Directory.Exists(cartella) Then Directory.CreateDirectory(cartella)
            End If

            Dim elenco As List(Of FilenClient.FilenFile) = client.ElencaAsync(CancellationToken.None).GetAwaiter().GetResult()
            Dim scelto As FilenClient.FilenFile = FilenClient.PiuRecente(elenco, nome)
            If scelto Is Nothing Then
                messaggio = "Il backup non è più sul cloud."
                Return False
            End If

            Dim destinazione As String = Path.Combine(cartella, nome)
            If File.Exists(destinazione) AndAlso scelto.Dimensione > 0 AndAlso New FileInfo(destinazione).Length = scelto.Dimensione Then
                percorso = destinazione
                Return True
            End If

            temporaneo = destinazione & ".inCorso"
            client.ScaricaFileAsync(scelto, temporaneo, CancellationToken.None).GetAwaiter().GetResult()
            If File.Exists(destinazione) Then File.Delete(destinazione)
            File.Move(temporaneo, destinazione)
            temporaneo = ""
            percorso = destinazione
            Return True
        Catch ex As Exception
            _log.ScriviLog("WGCloudDati.ScaricaBackupDalCloud: " & ex.ToString())
            messaggio = WGSincronizzazioneCloud.DescriviErrore(ex, "scaricare il backup dal cloud")
            Return False
        Finally
            Try
                If Not String.IsNullOrEmpty(temporaneo) AndAlso File.Exists(temporaneo) Then File.Delete(temporaneo)
            Catch exPulizia As Exception
                _log.ScriviLog("WGCloudDati.ScaricaBackupDalCloud pulizia: " & exPulizia.Message)
            End Try
        End Try
    End Function

    Friend Shared Function RiepilogoSpazio() As String
        Dim testo As New StringBuilder()
        Try
            Dim impostazioni As String = CartellaImpostazioni()
            If Not String.IsNullOrWhiteSpace(impostazioni) AndAlso Directory.Exists(impostazioni) Then
                Dim dimensione As Long = 0
                Dim conteggio As Integer = 0
                ContaCartella(impostazioni, dimensione, conteggio)
                testo.Append("Impostazioni di WinGabriel: " & WGDb.WGBackup.DescriviDimensione(dimensione) & " in " & conteggio.ToString(CultureInfo.InvariantCulture) & " file. ")
            End If

            Dim radice As String = RadiceDati()
            If Not String.IsNullOrWhiteSpace(radice) Then
                Dim totale As Long = 0
                Dim totaleFile As Integer = 0
                Dim dettaglio As New StringBuilder()
                Dim libri As Long = 0
                Dim utente As HashSet(Of String) = CartelleEscluseDallUtente()
                Dim sincronizzati As Long = 0

                Dim principaleDimensione As Long = 0
                Dim principaleFile As Integer = 0
                For Each voce As String In Directory.GetFiles(radice)
                    Try
                        principaleDimensione += New FileInfo(voce).Length
                        principaleFile += 1
                    Catch exFile As Exception
                        _log.ScriviLog("WGCloudDati.RiepilogoSpazio file: " & exFile.Message)
                    End Try
                Next
                totale += principaleDimensione
                totaleFile += principaleFile
                If Not utente.Contains(NomePrincipale) Then sincronizzati += principaleDimensione

                For Each cartella As String In Directory.GetDirectories(radice)
                    Dim nome As String = Path.GetFileName(cartella)
                    Dim dimensione As Long = 0
                    Dim conteggio As Integer = 0
                    ContaCartella(cartella, dimensione, conteggio)
                    totale += dimensione
                    totaleFile += conteggio
                    If String.Equals(nome, "libri", StringComparison.OrdinalIgnoreCase) Then
                        libri += dimensione
                        Continue For
                    End If
                    Dim sincronizzabile As Long = dimensione
                    Dim fileEsclusi As Integer = 0
                    sincronizzabile -= DimensioneEsclusaDentro(radice, nome, fileEsclusi)
                    If sincronizzabile > 0 AndAlso Not CartelleSempreEscluse().Contains(nome) Then
                        If dettaglio.Length > 0 Then dettaglio.Append(", ")
                        dettaglio.Append(nome & " " & WGDb.WGBackup.DescriviDimensione(sincronizzabile))
                    End If
                    If Not CartelleSempreEscluse().Contains(nome) AndAlso Not utente.Contains(nome) Then sincronizzati += sincronizzabile
                Next

                testo.Append("Cartella dei dati: " & WGDb.WGBackup.DescriviDimensione(totale) & " in " & totaleFile.ToString(CultureInfo.InvariantCulture) & " file")
                If dettaglio.Length > 0 Then testo.Append(". Per cartella: " & dettaglio.ToString())
                testo.Append(". ")
                testo.Append("Se la sincronizzazione dei dati è attiva, vengono inviati al cloud circa " & WGDb.WGBackup.DescriviDimensione(sincronizzati) & ". ")
            End If

            Dim cartellaBackup As String = WGDb.WGBackup.CartellaBackup
            If Not String.IsNullOrWhiteSpace(cartellaBackup) AndAlso Directory.Exists(cartellaBackup) Then
                Dim elencoLocale As List(Of WGDb.InfoBackup) = WGDb.WGBackup.ElencoBackup(cartellaBackup)
                Dim dimensioneBackup As Long = 0
                For Each voce As WGDb.InfoBackup In elencoLocale
                    dimensioneBackup += voce.Dimensione
                Next
                testo.Append("Backup su questo computer: " & elencoLocale.Count.ToString(CultureInfo.InvariantCulture) & " copie, " & WGDb.WGBackup.DescriviDimensione(dimensioneBackup) & ". ")
            End If

            If WGSincronizzazioneCloud.Collegato Then
                Try
                    Dim client As FilenClient = WGSincronizzazioneCloud.NuovoClient()
                    If client IsNot Nothing Then
                        Dim elenco As List(Of FilenClient.FilenFile) = client.ElencaAsync(CancellationToken.None).GetAwaiter().GetResult()
                        Dim dimensioneDati As Long = 0
                        Dim fileDati As Integer = 0
                        Dim dimensioneAltri As Long = 0
                        For Each voce As FilenClient.FilenFile In elenco
                            If voce.Nome.StartsWith(PrefissoNome, StringComparison.Ordinal) Then
                                dimensioneDati += voce.Dimensione
                                fileDati += 1
                            ElseIf voce.Nome.StartsWith("wingabriel-sincronizzazione", StringComparison.Ordinal) Then
                                dimensioneAltri += voce.Dimensione
                            End If
                        Next
                        Dim backupCloud As List(Of InfoBackupCloud) = BackupDaElenco(elenco, Nothing)
                        Dim dimensioneBackupCloud As Long = 0
                        For Each voce As InfoBackupCloud In backupCloud
                            dimensioneBackupCloud += voce.Dimensione
                        Next
                        testo.Append("Sul cloud: dati sincronizzati " & WGDb.WGBackup.DescriviDimensione(dimensioneDati) & " in " & fileDati.ToString(CultureInfo.InvariantCulture) & " file, backup " & WGDb.WGBackup.DescriviDimensione(dimensioneBackupCloud) & " in " & backupCloud.Count.ToString(CultureInfo.InvariantCulture) & " copie, rubrica, agenda e radio " & WGDb.WGBackup.DescriviDimensione(dimensioneAltri) & ".")
                    End If
                Catch exCloud As Exception
                    _log.ScriviLog("WGCloudDati.RiepilogoSpazio cloud: " & exCloud.ToString())
                    testo.Append(WGSincronizzazioneCloud.DescriviErrore(exCloud, "leggere lo spazio occupato sul cloud"))
                End Try
            End If
        Catch ex As Exception
            _log.ScriviLog("WGCloudDati.RiepilogoSpazio: " & ex.ToString())
            If testo.Length = 0 Then testo.Append("Non è stato possibile calcolare lo spazio occupato.")
        End Try
        Return testo.ToString().Trim()
    End Function

End Class
