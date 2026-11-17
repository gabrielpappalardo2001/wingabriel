Imports System
Imports System.Collections.Generic
Imports System.Data.SQLite
Imports System.Globalization
Imports System.IO
Imports System.IO.Compression
Imports System.Text
Imports System.Threading
Imports System.Windows.Forms
Imports System.Xml

' Gestione completa dei backup dei dati di WinGabriel: creazione, archiviazione, compressione, ripristino e applicazione delle
' regole di ritenzione.
Public Enum IntervalloBackup
    Mai = 0
    Quotidiano = 1
    Settimanale = 2
    Mensile = 3
End Enum

Public Class InfoBackup
    Public Property Percorso As String = ""
    Public Property Nome As String = ""
    Public Property Data As DateTime = DateTime.MinValue
    Public Property Dimensione As Long = 0

    Public ReadOnly Property Descrizione As String
        Get
            Try
                Dim parti As New StringBuilder()
                If Data <> DateTime.MinValue Then
                    parti.Append(Data.ToString("dd/MM/yyyy HH:mm", CultureInfo.InvariantCulture))
                Else
                    parti.Append(Nome)
                End If
                If Dimensione > 0 Then
                    parti.Append(", ").Append(WGBackup.DescriviDimensione(Dimensione))
                End If
                Return parti.ToString()
            Catch
                Return Nome
            End Try
        End Get
    End Property
End Class

Public NotInheritable Class WGBackup

    Private Shared ReadOnly _registro As New LoggerWGDb()
    Private Shared ReadOnly _lockOrigini As New Object()
    Private Shared ReadOnly _lockImpostazioni As New Object()
    Private Shared ReadOnly _origini As New Dictionary(Of String, String)(StringComparer.OrdinalIgnoreCase)
    Private Shared ReadOnly _preparazioni As New Dictionary(Of String, Action)(StringComparer.OrdinalIgnoreCase)

    Public Shared Event BackupCompletato(percorsoArchivio As String)

    Public Const PrefissoArchivio As String = "WinGabriel-backup-"
    Public Const EstensioneArchivio As String = ".zip"
    Private Const NomeFileImpostazioni As String = "backup.xml"
    Private Const NomeCartellaConfigurazione As String = "config"
    Private Const NomeCartellaDatiNellArchivio As String = "dati"
    Private Const NomeCartellaConfigNellArchivio As String = "config"
    Private Const NomeCartellaOriginiNellArchivio As String = "moduli"

    Private Shared ReadOnly EstensioniDatabase As String() = {".db", ".sqlite", ".sqlite3", ".db3"}
    Private Shared ReadOnly IntestazioneSqlite As Byte() = {83, 81, 76, 105, 116, 101, 32, 102, 111, 114, 109, 97, 116, 32, 51, 0}
    Private Const DimensioneBufferCopia As Integer = 1048576

    Private Sub New()
    End Sub

    Public Shared Function DescriviDimensione(byte_totali As Long) As String
        Try
            If byte_totali < 1024 Then Return byte_totali.ToString(CultureInfo.InvariantCulture) & " byte"
            Dim kb As Double = byte_totali / 1024.0
            If kb < 1024 Then Return Math.Round(kb, 0).ToString(CultureInfo.InvariantCulture) & " kilobyte"
            Dim mb As Double = kb / 1024.0
            If mb < 1024 Then Return Math.Round(mb, 1).ToString(CultureInfo.InvariantCulture) & " megabyte"
            Return Math.Round(mb / 1024.0, 2).ToString(CultureInfo.InvariantCulture) & " gigabyte"
        Catch ex As Exception
            _registro.ScriviLog("WGBackup.DescriviDimensione: " & ex.ToString())
            Return byte_totali.ToString(CultureInfo.InvariantCulture)
        End Try
    End Function

    Public Shared Sub RegistraOrigine(nome As String, percorsoCartella As String)
        Try
            If String.IsNullOrWhiteSpace(nome) OrElse String.IsNullOrWhiteSpace(percorsoCartella) Then Return
            SyncLock _lockOrigini
                _origini(nome.Trim()) = percorsoCartella.Trim()
            End SyncLock
        Catch ex As Exception
            _registro.ScriviLog("WGBackup.RegistraOrigine (" & If(nome, "") & "): " & ex.ToString())
        End Try
    End Sub

    Public Shared Sub RegistraPreparazione(nome As String, azione As Action)
        Try
            If String.IsNullOrWhiteSpace(nome) OrElse azione Is Nothing Then Return
            SyncLock _lockOrigini
                _preparazioni(nome.Trim()) = azione
            End SyncLock
        Catch ex As Exception
            _registro.ScriviLog("WGBackup.RegistraPreparazione (" & If(nome, "") & "): " & ex.ToString())
        End Try
    End Sub

    Private Shared Sub EseguiPreparazioni()
        Try
            Dim azioni As New List(Of KeyValuePair(Of String, Action))()
            SyncLock _lockOrigini
                azioni.AddRange(_preparazioni)
            End SyncLock
            For Each coppia As KeyValuePair(Of String, Action) In azioni
                Try
                    coppia.Value.Invoke()
                Catch exAzione As Exception
                    _registro.ScriviLog("WGBackup.EseguiPreparazioni (" & coppia.Key & "): " & exAzione.ToString())
                End Try
            Next
        Catch ex As Exception
            _registro.ScriviLog("WGBackup.EseguiPreparazioni: " & ex.ToString())
        End Try
    End Sub

    Public Shared Function OriginiRegistrate() As Dictionary(Of String, String)
        Dim copia As New Dictionary(Of String, String)(StringComparer.OrdinalIgnoreCase)
        Try
            SyncLock _lockOrigini
                For Each coppia As KeyValuePair(Of String, String) In _origini
                    copia(coppia.Key) = coppia.Value
                Next
            End SyncLock
        Catch ex As Exception
            _registro.ScriviLog("WGBackup.OriginiRegistrate: " & ex.ToString())
        End Try
        Return copia
    End Function

    Private Shared Function PercorsoFileImpostazioni() As String
        Try
            Dim cartella As String = WGPercorsiApplicazione.CartellaConfig()
            If Not Directory.Exists(cartella) Then Directory.CreateDirectory(cartella)
            Return Path.Combine(cartella, NomeFileImpostazioni)
        Catch ex As Exception
            _registro.ScriviLog("WGBackup.PercorsoFileImpostazioni: " & ex.ToString())
            Return ""
        End Try
    End Function

    Private Shared Function LeggiImpostazione(nome As String) As String
        SyncLock _lockImpostazioni
            Return LeggiImpostazioneProtetta(nome)
        End SyncLock
    End Function

    Private Shared Function LeggiImpostazioneProtetta(nome As String) As String
        Try
            Dim percorso As String = PercorsoFileImpostazioni()
            If String.IsNullOrWhiteSpace(percorso) OrElse Not File.Exists(percorso) Then Return ""

            Dim documento As New XmlDocument()
            documento.XmlResolver = Nothing
            documento.Load(percorso)
            If documento.DocumentElement Is Nothing Then Return ""

            Dim nodo As XmlNode = documento.DocumentElement.SelectSingleNode(nome)
            If nodo Is Nothing Then Return ""
            Return If(nodo.InnerText, "").Trim()
        Catch ex As Exception
            _registro.ScriviLog("WGBackup.LeggiImpostazione (" & If(nome, "") & "): " & ex.ToString())
            Return ""
        End Try
    End Function

    Private Shared Sub ScriviImpostazione(nome As String, valore As String)
        SyncLock _lockImpostazioni
            ScriviImpostazioneProtetta(nome, valore)
        End SyncLock
    End Sub

    ' Il file delle impostazioni viene scritto sia dal timer del backup sia dai menu:
    ' con un nome fisso per il file temporaneo le due scritture si pestavano i piedi.
    ' Nome temporaneo univoco, e in caso di collisione si riprova invece di perdere il dato.
    Private Shared Sub ScriviImpostazioneProtetta(nome As String, valore As String)
        Try
            Dim percorso As String = PercorsoFileImpostazioni()
            If String.IsNullOrWhiteSpace(percorso) Then Return

            Dim documento As New XmlDocument()
            documento.XmlResolver = Nothing

            If File.Exists(percorso) Then
                Try
                    documento.Load(percorso)
                Catch exLettura As Exception
                    _registro.ScriviLog("WGBackup.ScriviImpostazione lettura (" & percorso & "): " & exLettura.ToString())
                    documento = New XmlDocument()
                    documento.XmlResolver = Nothing
                End Try
            End If

            If documento.DocumentElement Is Nothing Then
                Dim dichiarazione As XmlDeclaration = documento.CreateXmlDeclaration("1.0", "utf-8", Nothing)
                documento.AppendChild(dichiarazione)
                documento.AppendChild(documento.CreateElement("Backup"))
            End If

            Dim nodo As XmlNode = documento.DocumentElement.SelectSingleNode(nome)
            If nodo Is Nothing Then
                nodo = documento.CreateElement(nome)
                documento.DocumentElement.AppendChild(nodo)
            End If
            nodo.InnerText = If(valore, "")

            Dim temporaneo As String = percorso & "." & Guid.NewGuid().ToString("N") & ".tmp"
            documento.Save(temporaneo)

            Dim scritto As Boolean = False
            For tentativo As Integer = 1 To 5
                Try
                    If File.Exists(percorso) Then File.Delete(percorso)
                    File.Move(temporaneo, percorso)
                    scritto = True
                    Exit For
                Catch exSostituzione As IOException
                    System.Threading.Thread.Sleep(60)
                End Try
            Next

            If Not scritto Then
                Try
                    If File.Exists(temporaneo) Then File.Delete(temporaneo)
                Catch exPulizia As Exception
                    _registro.ScriviLog("WGBackup.ScriviImpostazioneProtetta pulizia (" & temporaneo & "): " & exPulizia.ToString())
                End Try
            End If
        Catch ex As Exception
            _registro.ScriviLog("WGBackup.ScriviImpostazioneProtetta (" & If(nome, "") & "): " & ex.ToString())
        End Try
    End Sub

    Public Shared Property CartellaBackup As String
        Get
            Try
                Return LeggiImpostazione("Cartella")
            Catch ex As Exception
                _registro.ScriviLog("WGBackup.CartellaBackup Get: " & ex.ToString())
                Return ""
            End Try
        End Get
        Set(value As String)
            Try
                ScriviImpostazione("Cartella", If(value, "").Trim())
            Catch ex As Exception
                _registro.ScriviLog("WGBackup.CartellaBackup Set: " & ex.ToString())
            End Try
        End Set
    End Property

    Public Shared Property Intervallo As IntervalloBackup
        Get
            Try
                Dim testo As String = LeggiImpostazione("Intervallo")
                Dim valore As Integer = 0
                If Not Integer.TryParse(testo, valore) Then Return IntervalloBackup.Mai
                If valore < 0 OrElse valore > 3 Then Return IntervalloBackup.Mai
                Return CType(valore, IntervalloBackup)
            Catch ex As Exception
                _registro.ScriviLog("WGBackup.Intervallo Get: " & ex.ToString())
                Return IntervalloBackup.Mai
            End Try
        End Get
        Set(value As IntervalloBackup)
            Try
                ScriviImpostazione("Intervallo", CInt(value).ToString(CultureInfo.InvariantCulture))
            Catch ex As Exception
                _registro.ScriviLog("WGBackup.Intervallo Set: " & ex.ToString())
            End Try
        End Set
    End Property

    Public Shared Property GiorniRitenzione As Integer
        Get
            Try
                Dim testo As String = LeggiImpostazione("GiorniRitenzione")
                Dim valore As Integer = 0
                If Not Integer.TryParse(testo, valore) OrElse valore <= 0 Then Return 30
                Return valore
            Catch ex As Exception
                _registro.ScriviLog("WGBackup.GiorniRitenzione Get: " & ex.ToString())
                Return 30
            End Try
        End Get
        Set(value As Integer)
            Try
                Dim valore As Integer = value
                If valore <= 0 Then valore = 30
                ScriviImpostazione("GiorniRitenzione", valore.ToString(CultureInfo.InvariantCulture))
            Catch ex As Exception
                _registro.ScriviLog("WGBackup.GiorniRitenzione Set: " & ex.ToString())
            End Try
        End Set
    End Property

    Public Shared Property CopieMinimeDaConservare As Integer
        Get
            Try
                Dim testo As String = LeggiImpostazione("CopieMinime")
                Dim valore As Integer = 0
                If Not Integer.TryParse(testo, valore) OrElse valore <= 0 Then Return 3
                Return valore
            Catch ex As Exception
                _registro.ScriviLog("WGBackup.CopieMinimeDaConservare Get: " & ex.ToString())
                Return 3
            End Try
        End Get
        Set(value As Integer)
            Try
                Dim valore As Integer = value
                If valore <= 0 Then valore = 3
                ScriviImpostazione("CopieMinime", valore.ToString(CultureInfo.InvariantCulture))
            Catch ex As Exception
                _registro.ScriviLog("WGBackup.CopieMinimeDaConservare Set: " & ex.ToString())
            End Try
        End Set
    End Property

    Public Shared Property UltimoBackup As DateTime
        Get
            Try
                Dim testo As String = LeggiImpostazione("UltimoBackup")
                If String.IsNullOrWhiteSpace(testo) Then Return DateTime.MinValue
                Dim valore As DateTime = DateTime.MinValue
                If DateTime.TryParseExact(testo, "yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture, DateTimeStyles.None, valore) Then Return valore
                If DateTime.TryParse(testo, CultureInfo.InvariantCulture, DateTimeStyles.None, valore) Then Return valore
                Return DateTime.MinValue
            Catch ex As Exception
                _registro.ScriviLog("WGBackup.UltimoBackup Get: " & ex.ToString())
                Return DateTime.MinValue
            End Try
        End Get
        Set(value As DateTime)
            Try
                ScriviImpostazione("UltimoBackup", value.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture))
            Catch ex As Exception
                _registro.ScriviLog("WGBackup.UltimoBackup Set: " & ex.ToString())
            End Try
        End Set
    End Property

    Public Shared Function DescriviIntervallo(valore As IntervalloBackup) As String
        Try
            Select Case valore
                Case IntervalloBackup.Quotidiano
                    Return "Ogni giorno"
                Case IntervalloBackup.Settimanale
                    Return "Ogni settimana"
                Case IntervalloBackup.Mensile
                    Return "Ogni mese"
                Case Else
                    Return "Nessun backup automatico"
            End Select
        Catch ex As Exception
            _registro.ScriviLog("WGBackup.DescriviIntervallo: " & ex.ToString())
            Return "Nessun backup automatico"
        End Try
    End Function

    Private Shared Function CartelleDaEscludere() As List(Of String)
        Dim elenco As New List(Of String)()
        Try
            For Each nome As String In WGDatabase.CartelleSempreNelPercorsoApplicazione
                elenco.Add(nome)
            Next
        Catch ex As Exception
            _registro.ScriviLog("WGBackup.CartelleDaEscludere: " & ex.ToString())
        End Try
        Return elenco
    End Function

    Public Shared Function CartelleEscluseDalSalvataggio() As List(Of String)
        Try
            Return CartelleDaEscludere()
        Catch ex As Exception
            _registro.ScriviLog("WGBackup.CartelleEscluseDalSalvataggio: " & ex.ToString())
            Return New List(Of String)()
        End Try
    End Function

    Private Shared Function CartellaDaSaltare(percorsoRelativo As String) As Boolean
        Try
            If String.IsNullOrWhiteSpace(percorsoRelativo) Then Return False
            Dim primoSegmento As String = percorsoRelativo.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)(0)
            For Each esclusa As String In CartelleDaEscludere()
                If String.Equals(primoSegmento, esclusa, StringComparison.OrdinalIgnoreCase) Then Return True
            Next
            Return False
        Catch ex As Exception
            _registro.ScriviLog("WGBackup.CartellaDaSaltare (" & If(percorsoRelativo, "") & "): " & ex.ToString())
            Return False
        End Try
    End Function

    Private Shared Function EstensioneDaDatabase(percorso As String) As Boolean
        Try
            If String.IsNullOrWhiteSpace(percorso) Then Return False
            Dim estensione As String = Path.GetExtension(percorso)
            If String.IsNullOrWhiteSpace(estensione) Then Return False
            For Each valida As String In EstensioniDatabase
                If String.Equals(estensione, valida, StringComparison.OrdinalIgnoreCase) Then Return True
            Next
            Return False
        Catch ex As Exception
            _registro.ScriviLog("WGBackup.EstensioneDaDatabase (" & If(percorso, "") & "): " & ex.ToString())
            Return False
        End Try
    End Function

    Private Shared Function IntestazioneEDiDatabase(intestazione As Byte(), letti As Integer) As Boolean
        Try
            If intestazione Is Nothing Then Return False
            If letti < IntestazioneSqlite.Length Then Return False
            For indice As Integer = 0 To IntestazioneSqlite.Length - 1
                If intestazione(indice) <> IntestazioneSqlite(indice) Then Return False
            Next
            Return True
        Catch ex As Exception
            _registro.ScriviLog("WGBackup.IntestazioneEDiDatabase: " & ex.ToString())
            Return False
        End Try
    End Function

    Private Shared Function FlussoEDiDatabase(flusso As Stream) As Boolean
        Try
            If flusso Is Nothing Then Return False
            Dim intestazione(IntestazioneSqlite.Length - 1) As Byte
            Dim letti As Integer = 0
            While letti < intestazione.Length
                Dim porzione As Integer = flusso.Read(intestazione, letti, intestazione.Length - letti)
                If porzione <= 0 Then Exit While
                letti += porzione
            End While
            Return IntestazioneEDiDatabase(intestazione, letti)
        Catch ex As Exception
            _registro.ScriviLog("WGBackup.FlussoEDiDatabase: " & ex.ToString())
            Return False
        End Try
    End Function

    Private Shared Function EFileDatabase(percorso As String) As Boolean
        Try
            If String.IsNullOrWhiteSpace(percorso) OrElse Not File.Exists(percorso) Then Return False
            Using flusso As New FileStream(percorso, FileMode.Open, FileAccess.Read, FileShare.ReadWrite)
                If FlussoEDiDatabase(flusso) Then Return True
            End Using
            Return False
        Catch ex As Exception
            _registro.ScriviLog("WGBackup.EFileDatabase (" & If(percorso, "") & "): " & ex.ToString())
            Return EstensioneDaDatabase(percorso)
        End Try
    End Function

    Private Shared Function EVoceDatabase(voce As ZipArchiveEntry) As Boolean
        Try
            If voce Is Nothing Then Return False
            If voce.Length > 0 AndAlso voce.Length < IntestazioneSqlite.Length Then Return False
            Using flusso As Stream = voce.Open()
                If FlussoEDiDatabase(flusso) Then Return True
            End Using
            Return False
        Catch ex As Exception
            _registro.ScriviLog("WGBackup.EVoceDatabase: " & ex.ToString())
            Return EstensioneDaDatabase(If(voce Is Nothing, "", voce.FullName))
        End Try
    End Function

    Public Shared Function ConsolidaDatabase(percorso As String) As Boolean
        Try
            If Not EFileDatabase(percorso) Then Return False
            Dim connessione As SQLiteConnection = WGDatabase.ApriConnessione(percorso, "Version=3;Pooling=False;Default Timeout=10;")
            If connessione Is Nothing Then Return True
            Try
                WGDatabase.EseguiPragma(connessione, "PRAGMA wal_checkpoint(TRUNCATE);")
            Finally
                Try
                    connessione.Close()
                Catch exChiusura As Exception
                    _registro.ScriviLog("WGBackup.ConsolidaDatabase chiusura (" & percorso & "): " & exChiusura.Message)
                End Try
                Try
                    connessione.Dispose()
                Catch exSmaltimento As Exception
                    _registro.ScriviLog("WGBackup.ConsolidaDatabase smaltimento (" & percorso & "): " & exSmaltimento.Message)
                End Try
            End Try
            Return True
        Catch ex As Exception
            _registro.ScriviLog("WGBackup.ConsolidaDatabase (" & If(percorso, "") & "): " & ex.ToString())
            Return False
        End Try
    End Function

    Private Shared Sub AggiungiCartellaAllArchivio(archivio As ZipArchive, cartellaOrigine As String, prefissoNellArchivio As String, applicaEsclusioni As Boolean, ByRef contatoreFile As Integer, ByRef contatoreDatabase As Integer, token As CancellationToken)
        Try
            If archivio Is Nothing OrElse String.IsNullOrWhiteSpace(cartellaOrigine) Then Return
            If Not Directory.Exists(cartellaOrigine) Then Return

            Dim radice As String = Path.GetFullPath(cartellaOrigine).TrimEnd(Path.DirectorySeparatorChar)

            For Each fileOrigine As String In Directory.GetFiles(radice, "*", SearchOption.AllDirectories)
                Try
                    If token.IsCancellationRequested Then Return

                    Dim percorsoRelativo As String = fileOrigine.Substring(radice.Length).TrimStart(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
                    If applicaEsclusioni AndAlso CartellaDaSaltare(percorsoRelativo) Then Continue For

                    Dim estensione As String = Path.GetExtension(fileOrigine)
                    If String.Equals(estensione, ".tmp", StringComparison.OrdinalIgnoreCase) Then Continue For
                    If String.Equals(estensione, ".bak", StringComparison.OrdinalIgnoreCase) Then Continue For

                    Dim nomeSolo As String = Path.GetFileName(fileOrigine)
                    If nomeSolo.EndsWith("-shm", StringComparison.OrdinalIgnoreCase) Then Continue For
                    If nomeSolo.EndsWith("-wal", StringComparison.OrdinalIgnoreCase) Then Continue For
                    If nomeSolo.EndsWith("-journal", StringComparison.OrdinalIgnoreCase) Then Continue For
                    If nomeSolo.EndsWith(".inCorso", StringComparison.OrdinalIgnoreCase) Then Continue For

                    If ConsolidaDatabase(fileOrigine) Then contatoreDatabase += 1

                    Dim nomeNellArchivio As String = prefissoNellArchivio & "/" & percorsoRelativo.Replace(Path.DirectorySeparatorChar, "/"c)
                    Dim voce As ZipArchiveEntry = archivio.CreateEntry(nomeNellArchivio, CompressionLevel.Optimal)
                    Using flussoOrigine As New FileStream(fileOrigine, FileMode.Open, FileAccess.Read, FileShare.ReadWrite, DimensioneBufferCopia)
                        Using flussoDestinazione As Stream = voce.Open()
                            flussoOrigine.CopyTo(flussoDestinazione, DimensioneBufferCopia)
                        End Using
                    End Using
                    contatoreFile += 1
                Catch exFile As Exception
                    _registro.ScriviLog("WGBackup.AggiungiCartellaAllArchivio file (" & fileOrigine & "): " & exFile.ToString())
                End Try
            Next
        Catch ex As Exception
            _registro.ScriviLog("WGBackup.AggiungiCartellaAllArchivio (" & If(cartellaOrigine, "") & "): " & ex.ToString())
        End Try
    End Sub

    Public Shared Function CartellaRadiceDati() As String
        Try
            Dim effettiva As String = WGDatabase.CartellaBaseDatiEffettiva()
            If Not String.IsNullOrWhiteSpace(effettiva) AndAlso Directory.Exists(effettiva) Then Return effettiva
            Return WGDatabase.CartellaBaseDatiPredefinita()
        Catch ex As Exception
            _registro.ScriviLog("WGBackup.CartellaRadiceDati: " & ex.ToString())
            Return WGDatabase.CartellaBaseDatiPredefinita()
        End Try
    End Function

    Private Shared Function CartellaConfigurazione() As String
        Try
            Return WGPercorsiApplicazione.CartellaConfig()
        Catch ex As Exception
            _registro.ScriviLog("WGBackup.CartellaConfigurazione: " & ex.ToString())
            Return ""
        End Try
    End Function

    Public Shared Function EseguiBackup(cartellaDestinazione As String, ByRef percorsoArchivio As String, ByRef messaggio As String, Optional token As CancellationToken = Nothing) As Boolean
        percorsoArchivio = ""
        messaggio = ""
        Try
            Dim destinazione As String = If(cartellaDestinazione, "").Trim()
            If String.IsNullOrWhiteSpace(destinazione) Then destinazione = CartellaBackup
            If String.IsNullOrWhiteSpace(destinazione) Then
                messaggio = "Non è stata scelta nessuna cartella per il backup."
                Return False
            End If

            If Not Directory.Exists(destinazione) Then
                Directory.CreateDirectory(destinazione)
            End If

            If Not WGDatabase.VerificaCartellaScrivibile(destinazione) Then
                messaggio = "La cartella del backup non è scrivibile: " & destinazione
                Return False
            End If

            EseguiPreparazioni()

            Dim radiceDati As String = CartellaRadiceDati()
            If String.IsNullOrWhiteSpace(radiceDati) OrElse Not Directory.Exists(radiceDati) Then
                messaggio = "Non è stata trovata la cartella dei dati."
                Return False
            End If

            Dim nomeArchivio As String = PrefissoArchivio & DateTime.Now.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture) & EstensioneArchivio
            Dim percorsoCompleto As String = Path.Combine(destinazione, nomeArchivio)
            Dim percorsoTemporaneo As String = percorsoCompleto & ".inCorso"

            Dim contatoreFile As Integer = 0
            Dim contatoreDatabase As Integer = 0

            Using flusso As New FileStream(percorsoTemporaneo, FileMode.Create, FileAccess.Write, FileShare.None)
                Using archivio As New ZipArchive(flusso, ZipArchiveMode.Create)
                    AggiungiCartellaAllArchivio(archivio, radiceDati, NomeCartellaDatiNellArchivio, True, contatoreFile, contatoreDatabase, token)

                    If Not token.IsCancellationRequested Then
                        Dim cartellaConfig As String = CartellaConfigurazione()
                        If Not String.IsNullOrWhiteSpace(cartellaConfig) AndAlso Directory.Exists(cartellaConfig) Then
                            AggiungiCartellaAllArchivio(archivio, cartellaConfig, NomeCartellaConfigNellArchivio, False, contatoreFile, contatoreDatabase, token)
                        End If
                    End If

                    If Not token.IsCancellationRequested Then
                        For Each coppia As KeyValuePair(Of String, String) In OriginiRegistrate()
                            Try
                                If token.IsCancellationRequested Then Exit For
                                If String.IsNullOrWhiteSpace(coppia.Value) OrElse Not Directory.Exists(coppia.Value) Then Continue For
                                Dim completoOrigine As String = Path.GetFullPath(coppia.Value).TrimEnd(Path.DirectorySeparatorChar)
                                Dim completoRadice As String = Path.GetFullPath(radiceDati).TrimEnd(Path.DirectorySeparatorChar)
                                If completoOrigine.StartsWith(completoRadice, StringComparison.OrdinalIgnoreCase) Then Continue For
                                AggiungiCartellaAllArchivio(archivio, coppia.Value, NomeCartellaOriginiNellArchivio & "/" & coppia.Key, False, contatoreFile, contatoreDatabase, token)
                            Catch exOrigine As Exception
                                _registro.ScriviLog("WGBackup.EseguiBackup origine (" & coppia.Key & "): " & exOrigine.ToString())
                            End Try
                        Next
                    End If

                    Try
                        Dim manifesto As ZipArchiveEntry = archivio.CreateEntry("manifesto.txt", CompressionLevel.Optimal)
                        Using scrittore As New StreamWriter(manifesto.Open(), New UTF8Encoding(False))
                            scrittore.WriteLine("WinGabriel backup")
                            scrittore.WriteLine("Data: " & DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture))
                            scrittore.WriteLine("Cartella dati: " & radiceDati)
                            scrittore.WriteLine("File inclusi: " & contatoreFile.ToString(CultureInfo.InvariantCulture))
                            scrittore.WriteLine("Database inclusi: " & contatoreDatabase.ToString(CultureInfo.InvariantCulture))
                            If token.IsCancellationRequested Then
                                scrittore.WriteLine("Backup annullato: incompleto.")
                            End If
                        End Using
                    Catch exManifesto As Exception
                        _registro.ScriviLog("WGBackup.EseguiBackup manifesto: " & exManifesto.ToString())
                    End Try
                End Using
            End Using

            If token.IsCancellationRequested Then
                Try
                    If File.Exists(percorsoTemporaneo) Then File.Delete(percorsoTemporaneo)
                Catch exPulizia As Exception
                    _registro.ScriviLog("WGBackup.EseguiBackup pulizia annullamento: " & exPulizia.ToString())
                End Try
                messaggio = "Backup annullato."
                Return False
            End If

            If File.Exists(percorsoCompleto) Then File.Delete(percorsoCompleto)
            File.Move(percorsoTemporaneo, percorsoCompleto)

            percorsoArchivio = percorsoCompleto
            UltimoBackup = DateTime.Now

            Try
                ApplicaRitenzione(destinazione)
            Catch exRitenzione As Exception
                _registro.ScriviLog("WGBackup.EseguiBackup ritenzione: " & exRitenzione.ToString())
            End Try

            Try
                Dim cartellaScelta As String = CartellaBackup
                If Not String.IsNullOrWhiteSpace(cartellaScelta) Then
                    Dim primaCartella As String = Path.GetFullPath(destinazione).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
                    Dim secondaCartella As String = Path.GetFullPath(cartellaScelta).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
                    If String.Equals(primaCartella, secondaCartella, StringComparison.OrdinalIgnoreCase) Then
                        RaiseEvent BackupCompletato(percorsoCompleto)
                    End If
                End If
            Catch exEvento As Exception
                _registro.ScriviLog("WGBackup.EseguiBackup notifica: " & exEvento.ToString())
            End Try

            messaggio = "Backup completato: " & contatoreFile.ToString(CultureInfo.InvariantCulture) & " file salvati, di cui " & contatoreDatabase.ToString(CultureInfo.InvariantCulture) & " database."
            Return True
        Catch ex As Exception
            _registro.ScriviLog("WGBackup.EseguiBackup: " & ex.ToString())
            messaggio = "Backup non riuscito: " & ex.Message
            Return False
        End Try
    End Function

    Public Shared Function ElencoBackup(cartellaDestinazione As String) As List(Of InfoBackup)
        Dim elenco As New List(Of InfoBackup)()
        Try
            Dim destinazione As String = If(cartellaDestinazione, "").Trim()
            If String.IsNullOrWhiteSpace(destinazione) Then destinazione = CartellaBackup
            If String.IsNullOrWhiteSpace(destinazione) OrElse Not Directory.Exists(destinazione) Then Return elenco

            For Each percorso As String In Directory.GetFiles(destinazione, PrefissoArchivio & "*" & EstensioneArchivio, SearchOption.TopDirectoryOnly)
                Try
                    Dim informazioni As New FileInfo(percorso)
                    Dim voce As New InfoBackup()
                    voce.Percorso = percorso
                    voce.Nome = informazioni.Name
                    voce.Dimensione = informazioni.Length
                    voce.Data = DataDaNomeArchivio(informazioni.Name, informazioni.LastWriteTime)
                    elenco.Add(voce)
                Catch exVoce As Exception
                    _registro.ScriviLog("WGBackup.ElencoBackup voce (" & percorso & "): " & exVoce.ToString())
                End Try
            Next

            elenco.Sort(AddressOf ConfrontaBackupPerDataDiscendente)
        Catch ex As Exception
            _registro.ScriviLog("WGBackup.ElencoBackup: " & ex.ToString())
        End Try
        Return elenco
    End Function

    Private Shared Function ConfrontaBackupPerDataDiscendente(a As InfoBackup, b As InfoBackup) As Integer
        Try
            If a Is Nothing AndAlso b Is Nothing Then Return 0
            If a Is Nothing Then Return 1
            If b Is Nothing Then Return -1
            Return b.Data.CompareTo(a.Data)
        Catch ex As Exception
            _registro.ScriviLog("WGBackup.ConfrontaBackupPerDataDiscendente: " & ex.ToString())
            Return 0
        End Try
    End Function

    Private Shared Function DataDaNomeArchivio(nome As String, ripiego As DateTime) As DateTime
        Try
            If String.IsNullOrWhiteSpace(nome) Then Return ripiego
            Dim senzaPrefisso As String = nome
            If senzaPrefisso.StartsWith(PrefissoArchivio, StringComparison.OrdinalIgnoreCase) Then
                senzaPrefisso = senzaPrefisso.Substring(PrefissoArchivio.Length)
            End If
            If senzaPrefisso.EndsWith(EstensioneArchivio, StringComparison.OrdinalIgnoreCase) Then
                senzaPrefisso = senzaPrefisso.Substring(0, senzaPrefisso.Length - EstensioneArchivio.Length)
            End If
            Dim valore As DateTime = DateTime.MinValue
            If DateTime.TryParseExact(senzaPrefisso, "yyyyMMdd-HHmmss", CultureInfo.InvariantCulture, DateTimeStyles.None, valore) Then Return valore
            Return ripiego
        Catch ex As Exception
            _registro.ScriviLog("WGBackup.DataDaNomeArchivio (" & If(nome, "") & "): " & ex.ToString())
            Return ripiego
        End Try
    End Function

    Public Shared Function ApplicaRitenzione(cartellaDestinazione As String) As Integer
        Dim eliminati As Integer = 0
        Try
            Dim elenco As List(Of InfoBackup) = ElencoBackup(cartellaDestinazione)
            If elenco Is Nothing OrElse elenco.Count = 0 Then Return 0

            Dim minime As Integer = CopieMinimeDaConservare
            Dim giorni As Integer = GiorniRitenzione
            Dim limite As DateTime = DateTime.Now.AddDays(-giorni)

            For indice As Integer = 0 To elenco.Count - 1
                Try
                    If indice < minime Then Continue For
                    Dim voce As InfoBackup = elenco(indice)
                    If voce Is Nothing OrElse String.IsNullOrWhiteSpace(voce.Percorso) Then Continue For
                    If voce.Data >= limite Then Continue For
                    File.Delete(voce.Percorso)
                    eliminati += 1
                Catch exVoce As Exception
                    _registro.ScriviLog("WGBackup.ApplicaRitenzione voce: " & exVoce.ToString())
                End Try
            Next
        Catch ex As Exception
            _registro.ScriviLog("WGBackup.ApplicaRitenzione: " & ex.ToString())
        End Try
        Return eliminati
    End Function

    Public Shared Function ElencoContenutoArchivio(percorsoArchivio As String) As List(Of String)
        Dim elenco As New List(Of String)()
        Try
            If String.IsNullOrWhiteSpace(percorsoArchivio) OrElse Not File.Exists(percorsoArchivio) Then Return elenco
            Using archivio As ZipArchive = ZipFile.OpenRead(percorsoArchivio)
                For Each voce As ZipArchiveEntry In archivio.Entries
                    Try
                        If String.IsNullOrWhiteSpace(voce.FullName) Then Continue For
                        If String.IsNullOrWhiteSpace(voce.Name) Then Continue For
                        If Not EVoceDatabase(voce) Then Continue For
                        elenco.Add(voce.FullName)
                    Catch exVoce As Exception
                        _registro.ScriviLog("WGBackup.ElencoContenutoArchivio voce: " & exVoce.ToString())
                    End Try
                Next
            End Using
            elenco.Sort()
        Catch ex As Exception
            _registro.ScriviLog("WGBackup.ElencoContenutoArchivio (" & If(percorsoArchivio, "") & "): " & ex.ToString())
        End Try
        Return elenco
    End Function

    Private Shared Function DestinazioneDiVoce(nomeNellArchivio As String) As String
        Try
            If String.IsNullOrWhiteSpace(nomeNellArchivio) Then Return ""
            Dim normalizzato As String = nomeNellArchivio.Replace("/"c, Path.DirectorySeparatorChar)
            Dim separatore As Integer = normalizzato.IndexOf(Path.DirectorySeparatorChar)
            If separatore <= 0 Then Return ""
            Dim primoSegmento As String = normalizzato.Substring(0, separatore)
            Dim resto As String = normalizzato.Substring(separatore + 1)
            If String.IsNullOrWhiteSpace(resto) Then Return ""

            If String.Equals(primoSegmento, NomeCartellaDatiNellArchivio, StringComparison.OrdinalIgnoreCase) Then
                Return DentroLaCartella(CartellaRadiceDati(), resto)
            End If

            If String.Equals(primoSegmento, NomeCartellaConfigNellArchivio, StringComparison.OrdinalIgnoreCase) Then
                Return DentroLaCartella(CartellaConfigurazione(), resto)
            End If

            If String.Equals(primoSegmento, NomeCartellaOriginiNellArchivio, StringComparison.OrdinalIgnoreCase) Then
                Dim secondoSeparatore As Integer = resto.IndexOf(Path.DirectorySeparatorChar)
                If secondoSeparatore <= 0 Then Return ""
                Dim nomeOrigine As String = resto.Substring(0, secondoSeparatore)
                Dim restoOrigine As String = resto.Substring(secondoSeparatore + 1)
                Dim origini As Dictionary(Of String, String) = OriginiRegistrate()
                If Not origini.ContainsKey(nomeOrigine) Then Return ""
                Return DentroLaCartella(origini(nomeOrigine), restoOrigine)
            End If

            Return ""
        Catch ex As Exception
            _registro.ScriviLog("WGBackup.DestinazioneDiVoce (" & If(nomeNellArchivio, "") & "): " & ex.ToString())
            Return ""
        End Try
    End Function

    ''' <summary>
    ''' Il percorso completo di una voce dell'archivio dentro la cartella indicata; vuoto se la voce ne uscirebbe (per esempio con "..\"
    ''' o un percorso assoluto): un backup costruito apposta non può scrivere file fuori dalle cartelle dei dati.
    ''' </summary>
    Private Shared Function DentroLaCartella(cartella As String, relativo As String) As String
        Try
            If String.IsNullOrWhiteSpace(cartella) OrElse String.IsNullOrWhiteSpace(relativo) OrElse Path.IsPathRooted(relativo) Then Return ""
            Dim radice As String = Path.GetFullPath(cartella).TrimEnd(Path.DirectorySeparatorChar) & Path.DirectorySeparatorChar
            Dim completo As String = Path.GetFullPath(Path.Combine(radice, relativo))
            If Not completo.StartsWith(radice, StringComparison.OrdinalIgnoreCase) Then
                _registro.ScriviLog("WGBackup.DentroLaCartella: voce rifiutata perché uscirebbe dalla cartella (" & relativo & ")")
                Return ""
            End If
            Return completo
        Catch ex As Exception
            _registro.ScriviLog("WGBackup.DentroLaCartella (" & If(relativo, "") & "): " & ex.ToString())
            Return ""
        End Try
    End Function

    Public Shared Function Ripristina(percorsoArchivio As String, soloQuestaVoce As String, ByRef messaggio As String) As Boolean
        messaggio = ""
        Try
            If String.IsNullOrWhiteSpace(percorsoArchivio) OrElse Not File.Exists(percorsoArchivio) Then
                messaggio = "Il file di backup non è stato trovato."
                Return False
            End If

            Dim copiaDiSicurezza As String = ""
            Try
                Dim cartellaSicurezza As String = Path.Combine(WGPercorsiApplicazione.CartellaCache(), "backup")
                If Not Directory.Exists(cartellaSicurezza) Then Directory.CreateDirectory(cartellaSicurezza)
                Dim messaggioSicurezza As String = ""
                EseguiBackup(cartellaSicurezza, copiaDiSicurezza, messaggioSicurezza)
            Catch exSicurezza As Exception
                _registro.ScriviLog("WGBackup.Ripristina copia di sicurezza: " & exSicurezza.ToString())
            End Try

            Dim ripristinati As Integer = 0

            Using archivio As ZipArchive = ZipFile.OpenRead(percorsoArchivio)
                For Each voce As ZipArchiveEntry In archivio.Entries
                    Try
                        If String.IsNullOrWhiteSpace(voce.Name) Then Continue For
                        If Not String.IsNullOrWhiteSpace(soloQuestaVoce) AndAlso
                           Not String.Equals(voce.FullName, soloQuestaVoce, StringComparison.OrdinalIgnoreCase) Then Continue For
                        If String.Equals(voce.FullName, "manifesto.txt", StringComparison.OrdinalIgnoreCase) Then Continue For

                        Dim destinazione As String = DestinazioneDiVoce(voce.FullName)
                        If String.IsNullOrWhiteSpace(destinazione) Then Continue For

                        Dim cartella As String = Path.GetDirectoryName(destinazione)
                        If Not String.IsNullOrWhiteSpace(cartella) AndAlso Not Directory.Exists(cartella) Then
                            Directory.CreateDirectory(cartella)
                        End If

                        Using flussoVoce As Stream = voce.Open()
                            Using flussoDestinazione As New FileStream(destinazione, FileMode.Create, FileAccess.Write, FileShare.None)
                                flussoVoce.CopyTo(flussoDestinazione)
                            End Using
                        End Using

                        Try
                            Dim scia As String = destinazione & "-wal"
                            If File.Exists(scia) Then File.Delete(scia)
                            Dim memoria As String = destinazione & "-shm"
                            If File.Exists(memoria) Then File.Delete(memoria)
                        Catch exScia As Exception
                            _registro.ScriviLog("WGBackup.Ripristina pulizia wal (" & destinazione & "): " & exScia.Message)
                        End Try

                        ripristinati += 1
                    Catch exVoce As Exception
                        _registro.ScriviLog("WGBackup.Ripristina voce (" & voce.FullName & "): " & exVoce.ToString())
                    End Try
                Next
            End Using

            If ripristinati = 0 Then
                messaggio = "Non è stato ripristinato nessun file."
                Return False
            End If

            messaggio = "Ripristinati " & ripristinati.ToString(CultureInfo.InvariantCulture) & " file. Chiudi e riapri WinGabriel per usare i dati ripristinati."
            Return True
        Catch ex As Exception
            _registro.ScriviLog("WGBackup.Ripristina (" & If(percorsoArchivio, "") & "): " & ex.ToString())
            messaggio = "Ripristino non riuscito: " & ex.Message
            Return False
        End Try
    End Function

    Public Shared Function BackupDovuto() As Boolean
        Try
            Dim frequenza As IntervalloBackup = Intervallo
            If frequenza = IntervalloBackup.Mai Then Return False
            If String.IsNullOrWhiteSpace(CartellaBackup) Then Return False

            Dim ultimo As DateTime = UltimoBackup
            If ultimo = DateTime.MinValue Then Return True

            Select Case frequenza
                Case IntervalloBackup.Quotidiano
                    Return ultimo.Date < DateTime.Now.Date
                Case IntervalloBackup.Settimanale
                    Return (DateTime.Now - ultimo).TotalDays >= 7
                Case IntervalloBackup.Mensile
                    Return ultimo < DateTime.Now.AddMonths(-1)
            End Select

            Return False
        Catch ex As Exception
            _registro.ScriviLog("WGBackup.BackupDovuto: " & ex.ToString())
            Return False
        End Try
    End Function

    Public Shared Function EseguiBackupAutomaticoSeDovuto(ByRef messaggio As String) As Boolean
        messaggio = ""
        Try
            If Not BackupDovuto() Then Return False
            Dim archivio As String = ""
            Return EseguiBackup(CartellaBackup, archivio, messaggio)
        Catch ex As Exception
            _registro.ScriviLog("WGBackup.EseguiBackupAutomaticoSeDovuto: " & ex.ToString())
            Return False
        End Try
    End Function

End Class