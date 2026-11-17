Imports System.Collections.Generic
Imports System.Data
Imports System.Data.SQLite
Imports System.Globalization
Imports System.IO
Imports System.Text
Imports System.Windows.Forms
Imports System.Xml.Serialization
Imports wgcontrols
Imports WGDb

Public Class ElementoSalvato
    Public Property Id As String
    Public Property Titolo As String
    Public Property Url As String
    Public Property Tipo As YoutubeItemType
    Public Property Categoria As String
    Public Property DataAggiunta As Date
End Class

Public Class ElementoCronologia
    Public Property Testo As String
    Public Property DataAggiunta As Date
    ''' <summary>Tipo di ricerca salvato (Video, Channel, Playlist, Unknown = misti).</summary>
    Public Property Tipo As YoutubeItemType = YoutubeItemType.Unknown
End Class

Public Class ContenitorePreferiti
    Public Property VideoPreferiti As New List(Of ElementoSalvato)
    Public Property CanaliPreferiti As New List(Of ElementoSalvato)
    Public Property PlaylistPreferite As New List(Of ElementoSalvato)
    Public Property Categorie As New List(Of String)
End Class

Public Class ContenitoreRecenti
    Public Property VideoRecenti As New List(Of ElementoSalvato)
    Public Property CanaliRecenti As New List(Of ElementoSalvato)
    Public Property PlaylistRecenti As New List(Of ElementoSalvato)
    Public Property RicercheRecenti As New List(Of ElementoCronologia)
End Class

Public Class ConteggioCategoria
    Public Property Nome As String
    Public Property Quantita As Long
End Class

Public Class IscrizioneCanale
    Public Property IdCanale As String
    Public Property Titolo As String
    Public Property Url As String
    Public Property DataIscrizione As Date
    Public Property UltimoControllo As Date
    Public Property Attiva As Boolean
    Public Property NuoviNonVisti As Long
End Class

Public Class VideoNuovo
    Public Property IdVideo As String
    Public Property IdCanale As String
    Public Property NomeCanale As String
    Public Property Titolo As String
    Public Property Url As String
    Public Property Durata As String
    Public Property DataPubblicazione As Date
    Public Property DataRilevamento As Date
    Public Property Visto As Boolean
End Class

Public Class CanaleConNuoviVideo
    Public Property IdCanale As String
    Public Property Titolo As String
    Public Property Quantita As Long
End Class

Public Enum OrdinamentoElementi
    DataDiscendente = 0
    DataAscendente = 1
    TitoloAscendente = 2
    TitoloDiscendente = 3
End Enum

Public Class YoutubeDb
    Implements IDisposable

    Public Const CategoriaPredefinita As String = "Senza categoria"

    Private Const VersioneSchema As Integer = 2

    Private ReadOnly logger As New Logger()
    Private ReadOnly _blocco As New Object()
    Private Shared ReadOnly _epoca As New DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc)

    Private _percorsoCartella As String
    Private _percorsoDatabase As String
    Private _filePreferitiXml As String
    Private _fileRecentiXml As String

    Private _connessione As SQLiteConnection
    Private _ftsDisponibile As Boolean = False
    Private _pronto As Boolean = False
    Private _smaltito As Boolean = False

    Private _limiteRecenti As Integer = 50
    Private _limiteRicerche As Integer = 20
    Private _limiteLetturaLegacy As Integer = 100000
    Private _limiteVideoNotiPerCanale As Integer = 300

    Public Sub New()
        Me.New(RisolviCartellaYoutube())
    End Sub

    Private Shared Function RisolviCartellaYoutube() As String
        Try
            Dim cartella As String = WGDatabase.CartellaDati("youtube")
            If Not String.IsNullOrWhiteSpace(cartella) Then Return cartella
        Catch
        End Try
        Try
            Return Path.Combine(Application.StartupPath, "dati", "youtube")
        Catch
            Return "dati_youtube"
        End Try
    End Function

    Public Sub New(cartella As String)
        Try
            _percorsoCartella = cartella
            _percorsoDatabase = Path.Combine(_percorsoCartella, "youtube.db")
            _filePreferitiXml = Path.Combine(_percorsoCartella, "preferiti.xml")
            _fileRecentiXml = Path.Combine(_percorsoCartella, "recenti.xml")

            If Not Directory.Exists(_percorsoCartella) Then
                Directory.CreateDirectory(_percorsoCartella)
            End If

            ApriConnessione()
            ApplicaParametriMotore()
            CreaSchema()
            CreaIndiciTestuali()
            AssicuraCategoriaPredefinita()
            _pronto = True

            If Not ImportazioneEseguita() Then
                ImportaDaXml(_filePreferitiXml, _fileRecentiXml)
            End If
        Catch ex As Exception
            logger.ScriviLog("ERRORE [YoutubeDb.New]: " & ex.ToString())
        End Try
    End Sub

    Private Sub ApriConnessione()
        Try
            Dim stringa As New StringBuilder()
            stringa.Append("Data Source=")
            stringa.Append(_percorsoDatabase)
            stringa.Append(";Version=3;Pooling=False;FailIfMissing=False;BusyTimeout=15000;DateTimeKind=Utc;")

            _connessione = New SQLiteConnection(stringa.ToString())
            _connessione.Open()
        Catch ex As Exception
            logger.ScriviLog("ERRORE [YoutubeDb.ApriConnessione]: " & ex.ToString())
        End Try
    End Sub

    Private Sub ApplicaParametriMotore()
        Try
            EseguiComando("PRAGMA page_size=4096;")
            EseguiComando("PRAGMA journal_mode=WAL;")
            EseguiComando("PRAGMA synchronous=NORMAL;")
            EseguiComando("PRAGMA foreign_keys=ON;")
            EseguiComando("PRAGMA temp_store=MEMORY;")
            EseguiComando("PRAGMA cache_size=-32768;")
            EseguiComando("PRAGMA mmap_size=268435456;")
            EseguiComando("PRAGMA busy_timeout=15000;")
        Catch ex As Exception
            logger.ScriviLog("ERRORE [YoutubeDb.ApplicaParametriMotore]: " & ex.ToString())
        End Try
    End Sub

    Private Sub CreaSchema()
        Try
            Dim sql As New StringBuilder()

            sql.Append("CREATE TABLE IF NOT EXISTS impostazioni (")
            sql.Append("chiave TEXT PRIMARY KEY NOT NULL,")
            sql.Append("valore TEXT NOT NULL);")

            sql.Append("CREATE TABLE IF NOT EXISTS categorie (")
            sql.Append("id_categoria INTEGER PRIMARY KEY AUTOINCREMENT,")
            sql.Append("nome TEXT NOT NULL,")
            sql.Append("nome_ord TEXT NOT NULL,")
            sql.Append("ordine INTEGER NOT NULL DEFAULT 0,")
            sql.Append("protetta INTEGER NOT NULL DEFAULT 0);")
            sql.Append("CREATE UNIQUE INDEX IF NOT EXISTS ix_categorie_nome ON categorie(nome_ord);")
            sql.Append("CREATE INDEX IF NOT EXISTS ix_categorie_ordine ON categorie(protetta DESC, ordine, id_categoria);")

            sql.Append("CREATE TABLE IF NOT EXISTS preferiti (")
            sql.Append("id_preferito INTEGER PRIMARY KEY AUTOINCREMENT,")
            sql.Append("codice TEXT NOT NULL,")
            sql.Append("tipo INTEGER NOT NULL,")
            sql.Append("titolo TEXT NOT NULL,")
            sql.Append("titolo_ord TEXT NOT NULL,")
            sql.Append("url TEXT NOT NULL,")
            sql.Append("id_categoria INTEGER NOT NULL,")
            sql.Append("data_aggiunta INTEGER NOT NULL,")
            sql.Append("FOREIGN KEY (id_categoria) REFERENCES categorie(id_categoria));")
            sql.Append("CREATE UNIQUE INDEX IF NOT EXISTS ix_pref_chiave ON preferiti(tipo, codice);")
            sql.Append("CREATE INDEX IF NOT EXISTS ix_pref_tipo_data ON preferiti(tipo, data_aggiunta, id_preferito);")
            sql.Append("CREATE INDEX IF NOT EXISTS ix_pref_tipo_titolo ON preferiti(tipo, titolo_ord, id_preferito);")
            sql.Append("CREATE INDEX IF NOT EXISTS ix_pref_cat_data ON preferiti(id_categoria, tipo, data_aggiunta, id_preferito);")
            sql.Append("CREATE INDEX IF NOT EXISTS ix_pref_cat_titolo ON preferiti(id_categoria, tipo, titolo_ord, id_preferito);")

            sql.Append("CREATE TABLE IF NOT EXISTS recenti (")
            sql.Append("id_recente INTEGER PRIMARY KEY AUTOINCREMENT,")
            sql.Append("codice TEXT NOT NULL,")
            sql.Append("tipo INTEGER NOT NULL,")
            sql.Append("titolo TEXT NOT NULL,")
            sql.Append("titolo_ord TEXT NOT NULL,")
            sql.Append("url TEXT NOT NULL,")
            sql.Append("data_aggiunta INTEGER NOT NULL);")
            sql.Append("CREATE UNIQUE INDEX IF NOT EXISTS ix_rec_chiave ON recenti(tipo, codice);")
            sql.Append("CREATE INDEX IF NOT EXISTS ix_rec_tipo_data ON recenti(tipo, data_aggiunta, id_recente);")
            sql.Append("CREATE INDEX IF NOT EXISTS ix_rec_tipo_titolo ON recenti(tipo, titolo_ord, id_recente);")

            sql.Append("CREATE TABLE IF NOT EXISTS ricerche (")
            sql.Append("id_ricerca INTEGER PRIMARY KEY AUTOINCREMENT,")
            sql.Append("testo TEXT NOT NULL,")
            sql.Append("testo_ord TEXT NOT NULL,")
            sql.Append("data_aggiunta INTEGER NOT NULL,")
            sql.Append("tipo INTEGER NOT NULL DEFAULT 6);")
            sql.Append("CREATE UNIQUE INDEX IF NOT EXISTS ix_ricerche_testo ON ricerche(testo_ord);")
            sql.Append("CREATE INDEX IF NOT EXISTS ix_ricerche_data ON ricerche(data_aggiunta, id_ricerca);")

            sql.Append("CREATE TABLE IF NOT EXISTS iscrizioni (")
            sql.Append("id_iscrizione INTEGER PRIMARY KEY AUTOINCREMENT,")
            sql.Append("codice TEXT NOT NULL,")
            sql.Append("titolo TEXT NOT NULL,")
            sql.Append("titolo_ord TEXT NOT NULL,")
            sql.Append("url TEXT NOT NULL,")
            sql.Append("data_iscrizione INTEGER NOT NULL,")
            sql.Append("ultimo_controllo INTEGER NOT NULL DEFAULT 0,")
            sql.Append("attiva INTEGER NOT NULL DEFAULT 1);")
            sql.Append("CREATE UNIQUE INDEX IF NOT EXISTS ix_iscrizioni_codice ON iscrizioni(codice);")
            sql.Append("CREATE INDEX IF NOT EXISTS ix_iscrizioni_titolo ON iscrizioni(titolo_ord, id_iscrizione);")
            sql.Append("CREATE INDEX IF NOT EXISTS ix_iscrizioni_attiva ON iscrizioni(attiva, ultimo_controllo);")

            sql.Append("CREATE TABLE IF NOT EXISTS video_noti (")
            sql.Append("codice_canale TEXT NOT NULL,")
            sql.Append("codice_video TEXT NOT NULL,")
            sql.Append("data_registrazione INTEGER NOT NULL,")
            sql.Append("PRIMARY KEY (codice_canale, codice_video));")
            sql.Append("CREATE INDEX IF NOT EXISTS ix_noti_canale ON video_noti(codice_canale, data_registrazione DESC);")

            sql.Append("CREATE TABLE IF NOT EXISTS video_nuovi (")
            sql.Append("id_nuovo INTEGER PRIMARY KEY AUTOINCREMENT,")
            sql.Append("codice_video TEXT NOT NULL,")
            sql.Append("codice_canale TEXT NOT NULL,")
            sql.Append("titolo_canale TEXT NOT NULL,")
            sql.Append("titolo TEXT NOT NULL,")
            sql.Append("titolo_ord TEXT NOT NULL,")
            sql.Append("url TEXT NOT NULL,")
            sql.Append("durata TEXT NOT NULL DEFAULT '',")
            sql.Append("data_pubblicazione INTEGER NOT NULL DEFAULT 0,")
            sql.Append("data_rilevamento INTEGER NOT NULL,")
            sql.Append("visto INTEGER NOT NULL DEFAULT 0);")
            sql.Append("CREATE UNIQUE INDEX IF NOT EXISTS ix_nuovi_chiave ON video_nuovi(codice_canale, codice_video);")
            sql.Append("CREATE INDEX IF NOT EXISTS ix_nuovi_visto ON video_nuovi(visto, data_pubblicazione, id_nuovo);")
            sql.Append("CREATE INDEX IF NOT EXISTS ix_nuovi_canale ON video_nuovi(codice_canale, visto, data_pubblicazione);")

            EseguiComando(sql.ToString())

            ' Migrazione: aggiunge colonna tipo se il DB esisteva già senza
            AssicuraColonnaTipoRicerche()

            Try
                WGDatabase.AssicuraColonnaChiave(_connessione, "categorie")
                WGDatabase.AssicuraColonnaChiave(_connessione, "preferiti")
                WGDatabase.AssicuraColonnaChiave(_connessione, "recenti")
                WGDatabase.AssicuraColonnaChiave(_connessione, "ricerche")
                WGDatabase.AssicuraColonnaChiave(_connessione, "iscrizioni")
                WGDatabase.AssicuraColonnaChiave(_connessione, "video_nuovi")
            Catch exChiave As Exception
                logger.ScriviLog("ERRORE [YoutubeDb.CreaSchema.AssicuraColonnaChiave]: " & exChiave.ToString())
            End Try

            ScriviImpostazione("versione_schema", VersioneSchema.ToString(CultureInfo.InvariantCulture))
        Catch ex As Exception
            logger.ScriviLog("ERRORE [YoutubeDb.CreaSchema]: " & ex.ToString())
        End Try
    End Sub

    Private Sub CreaIndiciTestuali()
        Try
            Dim sql As New StringBuilder()

            sql.Append("CREATE VIRTUAL TABLE IF NOT EXISTS preferiti_fts USING fts5(")
            sql.Append("titolo, content='preferiti', content_rowid='id_preferito', tokenize='unicode61');")

            sql.Append("CREATE TRIGGER IF NOT EXISTS trg_pref_ins AFTER INSERT ON preferiti BEGIN ")
            sql.Append("INSERT INTO preferiti_fts(rowid, titolo) VALUES (new.id_preferito, new.titolo); END;")

            sql.Append("CREATE TRIGGER IF NOT EXISTS trg_pref_del AFTER DELETE ON preferiti BEGIN ")
            sql.Append("INSERT INTO preferiti_fts(preferiti_fts, rowid, titolo) VALUES ('delete', old.id_preferito, old.titolo); END;")

            sql.Append("CREATE TRIGGER IF NOT EXISTS trg_pref_upd AFTER UPDATE ON preferiti BEGIN ")
            sql.Append("INSERT INTO preferiti_fts(preferiti_fts, rowid, titolo) VALUES ('delete', old.id_preferito, old.titolo); ")
            sql.Append("INSERT INTO preferiti_fts(rowid, titolo) VALUES (new.id_preferito, new.titolo); END;")

            sql.Append("CREATE VIRTUAL TABLE IF NOT EXISTS recenti_fts USING fts5(")
            sql.Append("titolo, content='recenti', content_rowid='id_recente', tokenize='unicode61');")

            sql.Append("CREATE TRIGGER IF NOT EXISTS trg_rec_ins AFTER INSERT ON recenti BEGIN ")
            sql.Append("INSERT INTO recenti_fts(rowid, titolo) VALUES (new.id_recente, new.titolo); END;")

            sql.Append("CREATE TRIGGER IF NOT EXISTS trg_rec_del AFTER DELETE ON recenti BEGIN ")
            sql.Append("INSERT INTO recenti_fts(recenti_fts, rowid, titolo) VALUES ('delete', old.id_recente, old.titolo); END;")

            sql.Append("CREATE TRIGGER IF NOT EXISTS trg_rec_upd AFTER UPDATE ON recenti BEGIN ")
            sql.Append("INSERT INTO recenti_fts(recenti_fts, rowid, titolo) VALUES ('delete', old.id_recente, old.titolo); ")
            sql.Append("INSERT INTO recenti_fts(rowid, titolo) VALUES (new.id_recente, new.titolo); END;")

            EseguiComando(sql.ToString())
            EseguiComando("INSERT INTO preferiti_fts(preferiti_fts) VALUES ('rebuild');")
            EseguiComando("INSERT INTO recenti_fts(recenti_fts) VALUES ('rebuild');")
            _ftsDisponibile = True
        Catch ex As Exception
            _ftsDisponibile = False
            logger.ScriviLog("ERRORE [YoutubeDb.CreaIndiciTestuali]: " & ex.ToString())
        End Try
    End Sub

    Private Sub AssicuraCategoriaPredefinita()
        Try
            Using cmd As SQLiteCommand = _connessione.CreateCommand()
                cmd.CommandText = "INSERT OR IGNORE INTO categorie (nome, nome_ord, ordine, protetta, ChiaveWG) VALUES (@n, @o, -1, 1, @chiave);"
                cmd.Parameters.AddWithValue("@n", CategoriaPredefinita)
                cmd.Parameters.AddWithValue("@o", Normalizza(CategoriaPredefinita))
                cmd.Parameters.AddWithValue("@chiave", WGDatabase.GeneraChiaveUnivocaPerTabella(_connessione, "categorie"))
                cmd.ExecuteNonQuery()
            End Using
        Catch ex As Exception
            logger.ScriviLog("ERRORE [YoutubeDb.AssicuraCategoriaPredefinita]: " & ex.ToString())
        End Try
    End Sub

    ''' <summary>Aggiunge la colonna tipo alle ricerche se manca (DB già esistenti).</summary>
    Private Sub AssicuraColonnaTipoRicerche()
        Try
            Dim haColonna As Boolean = False
            Using cmd As SQLiteCommand = _connessione.CreateCommand()
                cmd.CommandText = "PRAGMA table_info(ricerche);"
                Using lettore As SQLiteDataReader = cmd.ExecuteReader()
                    While lettore.Read()
                        Dim nomeCol As String = Convert.ToString(lettore("name"))
                        If String.Equals(nomeCol, "tipo", StringComparison.OrdinalIgnoreCase) Then
                            haColonna = True
                            Exit While
                        End If
                    End While
                End Using
            End Using

            If Not haColonna Then
                ' Unknown = 6 nell'enum YoutubeItemType
                EseguiComando("ALTER TABLE ricerche ADD COLUMN tipo INTEGER NOT NULL DEFAULT 6;")
            End If
        Catch ex As Exception
            logger.ScriviLog("ERRORE [YoutubeDb.AssicuraColonnaTipoRicerche]: " & ex.ToString())
        End Try
    End Sub

    Private Sub EseguiComando(sql As String)
        Try
            Using cmd As SQLiteCommand = _connessione.CreateCommand()
                cmd.CommandText = sql
                cmd.ExecuteNonQuery()
            End Using
        Catch ex As Exception
            logger.ScriviLog("ERRORE [YoutubeDb.EseguiComando]: " & ex.ToString())
            Throw
        End Try
    End Sub

    Private Function Normalizza(testo As String) As String
        Try
            If String.IsNullOrEmpty(testo) Then Return ""

            Dim scomposto As String = testo.Trim().ToLowerInvariant().Normalize(NormalizationForm.FormD)
            Dim risultato As New StringBuilder(scomposto.Length)

            For i As Integer = 0 To scomposto.Length - 1
                If CharUnicodeInfo.GetUnicodeCategory(scomposto(i)) <> UnicodeCategory.NonSpacingMark Then
                    risultato.Append(scomposto(i))
                End If
            Next

            Return risultato.ToString().Normalize(NormalizationForm.FormC)
        Catch ex As Exception
            logger.ScriviLog("ERRORE [YoutubeDb.Normalizza]: " & ex.ToString())
            If testo Is Nothing Then Return ""
            Return testo.ToLowerInvariant()
        End Try
    End Function

    Private Function DataInNumero(valore As Date) As Long
        Try
            If valore = Date.MinValue Then Return CLng((DateTime.UtcNow - _epoca).TotalMilliseconds)

            Dim utc As DateTime
            If valore.Kind = DateTimeKind.Utc Then
                utc = valore
            ElseIf valore.Kind = DateTimeKind.Unspecified Then
                utc = DateTime.SpecifyKind(valore, DateTimeKind.Local).ToUniversalTime()
            Else
                utc = valore.ToUniversalTime()
            End If

            Return CLng((utc - _epoca).TotalMilliseconds)
        Catch ex As Exception
            logger.ScriviLog("ERRORE [YoutubeDb.DataInNumero]: " & ex.ToString())
            Return CLng((DateTime.UtcNow - _epoca).TotalMilliseconds)
        End Try
    End Function

    Private Function NumeroInData(valore As Long) As Date
        Try
            Return _epoca.AddMilliseconds(valore).ToLocalTime()
        Catch ex As Exception
            logger.ScriviLog("ERRORE [YoutubeDb.NumeroInData]: " & ex.ToString())
            Return Date.MinValue
        End Try
    End Function

    Private Function OraCorrente() As Long
        Try
            Return CLng((DateTime.UtcNow - _epoca).TotalMilliseconds)
        Catch ex As Exception
            logger.ScriviLog("ERRORE [YoutubeDb.OraCorrente]: " & ex.ToString())
            Return 0
        End Try
    End Function

    Private Function ClausolaOrdinamento(ordine As OrdinamentoElementi, prefisso As String, colonnaChiave As String) As String
        Try
            Select Case ordine
                Case OrdinamentoElementi.DataAscendente
                    Return " ORDER BY " & prefisso & "data_aggiunta ASC, " & prefisso & colonnaChiave & " ASC "
                Case OrdinamentoElementi.TitoloAscendente
                    Return " ORDER BY " & prefisso & "titolo_ord ASC, " & prefisso & colonnaChiave & " ASC "
                Case OrdinamentoElementi.TitoloDiscendente
                    Return " ORDER BY " & prefisso & "titolo_ord DESC, " & prefisso & colonnaChiave & " DESC "
                Case Else
                    Return " ORDER BY " & prefisso & "data_aggiunta DESC, " & prefisso & colonnaChiave & " DESC "
            End Select
        Catch ex As Exception
            logger.ScriviLog("ERRORE [YoutubeDb.ClausolaOrdinamento]: " & ex.ToString())
            Return " ORDER BY " & prefisso & "data_aggiunta DESC, " & prefisso & colonnaChiave & " DESC "
        End Try
    End Function

    Private Function IdCategoria(nome As String, creaSeAssente As Boolean) As Long
        Try
            Dim effettivo As String = nome
            If String.IsNullOrWhiteSpace(effettivo) Then effettivo = CategoriaPredefinita

            Dim chiave As String = Normalizza(effettivo)

            Using cmd As SQLiteCommand = _connessione.CreateCommand()
                cmd.CommandText = "SELECT id_categoria FROM categorie WHERE nome_ord = @o LIMIT 1;"
                cmd.Parameters.AddWithValue("@o", chiave)
                Dim risultato As Object = cmd.ExecuteScalar()
                If risultato IsNot Nothing AndAlso Not IsDBNull(risultato) Then
                    Return Convert.ToInt64(risultato, CultureInfo.InvariantCulture)
                End If
            End Using

            If Not creaSeAssente Then Return -1

            Using cmd As SQLiteCommand = _connessione.CreateCommand()
                cmd.CommandText = "INSERT INTO categorie (nome, nome_ord, ordine, protetta, ChiaveWG) VALUES (@n, @o, (SELECT IFNULL(MAX(ordine), 0) + 1 FROM categorie), 0, @chiavewg);"
                cmd.Parameters.AddWithValue("@n", effettivo)
                cmd.Parameters.AddWithValue("@o", chiave)
                cmd.Parameters.AddWithValue("@chiavewg", WGDatabase.GeneraChiaveUnivocaPerTabella(_connessione, "categorie"))
                cmd.ExecuteNonQuery()
            End Using

            Using cmd As SQLiteCommand = _connessione.CreateCommand()
                cmd.CommandText = "SELECT last_insert_rowid();"
                Return Convert.ToInt64(cmd.ExecuteScalar(), CultureInfo.InvariantCulture)
            End Using
        Catch ex As Exception
            logger.ScriviLog("ERRORE [YoutubeDb.IdCategoria]: " & ex.ToString())
            Return -1
        End Try
    End Function

    Private Function LeggiImpostazione(chiave As String) As String
        Try
            Using cmd As SQLiteCommand = _connessione.CreateCommand()
                cmd.CommandText = "SELECT valore FROM impostazioni WHERE chiave = @c LIMIT 1;"
                cmd.Parameters.AddWithValue("@c", chiave)
                Dim risultato As Object = cmd.ExecuteScalar()
                If risultato Is Nothing OrElse IsDBNull(risultato) Then Return Nothing
                Return Convert.ToString(risultato)
            End Using
        Catch ex As Exception
            logger.ScriviLog("ERRORE [YoutubeDb.LeggiImpostazione]: " & ex.ToString())
            Return Nothing
        End Try
    End Function

    Private Sub ScriviImpostazione(chiave As String, valore As String)
        Try
            Using cmd As SQLiteCommand = _connessione.CreateCommand()
                cmd.CommandText = "INSERT OR REPLACE INTO impostazioni (chiave, valore) VALUES (@c, @v);"
                cmd.Parameters.AddWithValue("@c", chiave)
                cmd.Parameters.AddWithValue("@v", valore)
                cmd.ExecuteNonQuery()
            End Using
        Catch ex As Exception
            logger.ScriviLog("ERRORE [YoutubeDb.ScriviImpostazione]: " & ex.ToString())
        End Try
    End Sub

    Public Function ImportazioneEseguita() As Boolean
        Try
            SyncLock _blocco
                If _connessione Is Nothing Then Return True
                Return LeggiImpostazione("importazione_xml") = "1"
            End SyncLock
        Catch ex As Exception
            logger.ScriviLog("ERRORE [YoutubeDb.ImportazioneEseguita]: " & ex.ToString())
            Return True
        End Try
    End Function

    Public Function ImportaDaXml(percorsoPreferiti As String, percorsoRecenti As String) As Long
        Dim totale As Long = 0
        Try
            SyncLock _blocco
                If _connessione Is Nothing Then Return 0

                Dim preferiti As ContenitorePreferiti = LeggiXmlPreferiti(percorsoPreferiti)
                Dim recenti As ContenitoreRecenti = LeggiXmlRecenti(percorsoRecenti)

                Using transazione As SQLiteTransaction = _connessione.BeginTransaction()
                    Try
                        If preferiti IsNot Nothing Then
                            For Each nome As String In preferiti.Categorie
                                Try
                                    If Not String.IsNullOrWhiteSpace(nome) Then IdCategoria(nome, True)
                                Catch exCat As Exception
                                    logger.ScriviLog("ERRORE [YoutubeDb.ImportaDaXml.Categoria]: " & exCat.ToString())
                                End Try
                            Next

                            totale += ImportaElencoPreferiti(preferiti.VideoPreferiti, YoutubeItemType.Video)
                            totale += ImportaElencoPreferiti(preferiti.CanaliPreferiti, YoutubeItemType.Channel)
                            totale += ImportaElencoPreferiti(preferiti.PlaylistPreferite, YoutubeItemType.Playlist)
                        End If

                        If recenti IsNot Nothing Then
                            totale += ImportaElencoRecenti(recenti.VideoRecenti, YoutubeItemType.Video)
                            totale += ImportaElencoRecenti(recenti.CanaliRecenti, YoutubeItemType.Channel)
                            totale += ImportaElencoRecenti(recenti.PlaylistRecenti, YoutubeItemType.Playlist)
                            totale += ImportaElencoRicerche(recenti.RicercheRecenti)
                        End If

                        ScriviImpostazione("importazione_xml", "1")
                        ScriviImpostazione("importazione_xml_data", DateTime.UtcNow.ToString("o", CultureInfo.InvariantCulture))
                        ScriviImpostazione("importazione_xml_record", totale.ToString(CultureInfo.InvariantCulture))

                        transazione.Commit()
                    Catch exTrans As Exception
                        logger.ScriviLog("ERRORE [YoutubeDb.ImportaDaXml.Transazione]: " & exTrans.ToString())
                        Try
                            transazione.Rollback()
                        Catch exRoll As Exception
                            logger.ScriviLog("ERRORE [YoutubeDb.ImportaDaXml.Rollback]: " & exRoll.ToString())
                        End Try
                        Return 0
                    End Try
                End Using

                Ottimizza()
            End SyncLock
        Catch ex As Exception
            logger.ScriviLog("ERRORE [YoutubeDb.ImportaDaXml]: " & ex.ToString())
        End Try
        Return totale
    End Function

    Private Function LeggiXmlPreferiti(percorso As String) As ContenitorePreferiti
        Try
            If String.IsNullOrWhiteSpace(percorso) OrElse Not File.Exists(percorso) Then Return Nothing

            Dim serializzatore As New XmlSerializer(GetType(ContenitorePreferiti))
            Using lettore As New StreamReader(percorso)
                Return CType(serializzatore.Deserialize(lettore), ContenitorePreferiti)
            End Using
        Catch ex As Exception
            logger.ScriviLog("ERRORE [YoutubeDb.LeggiXmlPreferiti]: " & ex.ToString())
            Return Nothing
        End Try
    End Function

    Private Function LeggiXmlRecenti(percorso As String) As ContenitoreRecenti
        Try
            If String.IsNullOrWhiteSpace(percorso) OrElse Not File.Exists(percorso) Then Return Nothing

            Dim serializzatore As New XmlSerializer(GetType(ContenitoreRecenti))
            Using lettore As New StreamReader(percorso)
                Return CType(serializzatore.Deserialize(lettore), ContenitoreRecenti)
            End Using
        Catch ex As Exception
            logger.ScriviLog("ERRORE [YoutubeDb.LeggiXmlRecenti]: " & ex.ToString())
            Return Nothing
        End Try
    End Function

    Private Function ImportaElencoPreferiti(elenco As List(Of ElementoSalvato), tipo As YoutubeItemType) As Long
        Dim contatore As Long = 0
        Try
            If elenco Is Nothing Then Return 0

            Using cmd As SQLiteCommand = _connessione.CreateCommand()
                cmd.CommandText = "INSERT OR IGNORE INTO preferiti (codice, tipo, titolo, titolo_ord, url, id_categoria, data_aggiunta, ChiaveWG) VALUES (@cod, @tip, @tit, @ord, @url, @cat, @dat, @chiavewg);"
                Dim pCod As SQLiteParameter = cmd.Parameters.Add("@cod", DbType.String)
                Dim pTip As SQLiteParameter = cmd.Parameters.Add("@tip", DbType.Int32)
                Dim pTit As SQLiteParameter = cmd.Parameters.Add("@tit", DbType.String)
                Dim pOrd As SQLiteParameter = cmd.Parameters.Add("@ord", DbType.String)
                Dim pUrl As SQLiteParameter = cmd.Parameters.Add("@url", DbType.String)
                Dim pCat As SQLiteParameter = cmd.Parameters.Add("@cat", DbType.Int64)
                Dim pDat As SQLiteParameter = cmd.Parameters.Add("@dat", DbType.Int64)
                Dim pChiave As SQLiteParameter = cmd.Parameters.Add("@chiavewg", DbType.String)

                For Each voce As ElementoSalvato In elenco
                    Try
                        If voce Is Nothing OrElse String.IsNullOrWhiteSpace(voce.Id) Then Continue For

                        Dim titolo As String = voce.Titolo
                        If String.IsNullOrWhiteSpace(titolo) Then titolo = voce.Id

                        Dim url As String = voce.Url
                        If url Is Nothing Then url = ""

                        pCod.Value = voce.Id
                        pTip.Value = CInt(tipo)
                        pTit.Value = titolo
                        pOrd.Value = Normalizza(titolo)
                        pUrl.Value = url
                        pCat.Value = IdCategoria(voce.Categoria, True)
                        pDat.Value = DataInNumero(voce.DataAggiunta)
                        pChiave.Value = WGDatabase.GeneraChiave()

                        contatore += cmd.ExecuteNonQuery()
                    Catch exVoce As Exception
                        logger.ScriviLog("ERRORE [YoutubeDb.ImportaElencoPreferiti.Voce]: " & exVoce.ToString())
                    End Try
                Next
            End Using
        Catch ex As Exception
            logger.ScriviLog("ERRORE [YoutubeDb.ImportaElencoPreferiti]: " & ex.ToString())
        End Try
        Return contatore
    End Function

    Private Function ImportaElencoRecenti(elenco As List(Of ElementoSalvato), tipo As YoutubeItemType) As Long
        Dim contatore As Long = 0
        Try
            If elenco Is Nothing Then Return 0

            Using cmd As SQLiteCommand = _connessione.CreateCommand()
                cmd.CommandText = "INSERT OR IGNORE INTO recenti (codice, tipo, titolo, titolo_ord, url, data_aggiunta, ChiaveWG) VALUES (@cod, @tip, @tit, @ord, @url, @dat, @chiavewg);"
                Dim pCod As SQLiteParameter = cmd.Parameters.Add("@cod", DbType.String)
                Dim pTip As SQLiteParameter = cmd.Parameters.Add("@tip", DbType.Int32)
                Dim pTit As SQLiteParameter = cmd.Parameters.Add("@tit", DbType.String)
                Dim pOrd As SQLiteParameter = cmd.Parameters.Add("@ord", DbType.String)
                Dim pUrl As SQLiteParameter = cmd.Parameters.Add("@url", DbType.String)
                Dim pDat As SQLiteParameter = cmd.Parameters.Add("@dat", DbType.Int64)
                Dim pChiave As SQLiteParameter = cmd.Parameters.Add("@chiavewg", DbType.String)

                Dim posizione As Integer = elenco.Count

                For Each voce As ElementoSalvato In elenco
                    Try
                        If voce Is Nothing OrElse String.IsNullOrWhiteSpace(voce.Id) Then
                            posizione -= 1
                            Continue For
                        End If

                        Dim titolo As String = voce.Titolo
                        If String.IsNullOrWhiteSpace(titolo) Then titolo = voce.Id

                        Dim url As String = voce.Url
                        If url Is Nothing Then url = ""

                        Dim data As Long = DataInNumero(voce.DataAggiunta)
                        If voce.DataAggiunta = Date.MinValue Then data = OraCorrente() - CLng(posizione) * 1000L

                        pCod.Value = voce.Id
                        pTip.Value = CInt(tipo)
                        pTit.Value = titolo
                        pOrd.Value = Normalizza(titolo)
                        pUrl.Value = url
                        pDat.Value = data
                        pChiave.Value = WGDatabase.GeneraChiave()

                        contatore += cmd.ExecuteNonQuery()
                        posizione -= 1
                    Catch exVoce As Exception
                        logger.ScriviLog("ERRORE [YoutubeDb.ImportaElencoRecenti.Voce]: " & exVoce.ToString())
                    End Try
                Next
            End Using
        Catch ex As Exception
            logger.ScriviLog("ERRORE [YoutubeDb.ImportaElencoRecenti]: " & ex.ToString())
        End Try
        Return contatore
    End Function

    Private Function ImportaElencoRicerche(elenco As List(Of ElementoCronologia)) As Long
        Dim contatore As Long = 0
        Try
            If elenco Is Nothing Then Return 0

            Using cmd As SQLiteCommand = _connessione.CreateCommand()
                cmd.CommandText = "INSERT OR IGNORE INTO ricerche (testo, testo_ord, data_aggiunta, tipo, ChiaveWG) VALUES (@tes, @ord, @dat, @tip, @chiavewg);"
                Dim pTes As SQLiteParameter = cmd.Parameters.Add("@tes", DbType.String)
                Dim pOrd As SQLiteParameter = cmd.Parameters.Add("@ord", DbType.String)
                Dim pDat As SQLiteParameter = cmd.Parameters.Add("@dat", DbType.Int64)
                Dim pTip As SQLiteParameter = cmd.Parameters.Add("@tip", DbType.Int32)
                Dim pChiave As SQLiteParameter = cmd.Parameters.Add("@chiavewg", DbType.String)

                Dim posizione As Integer = elenco.Count

                For Each voce As ElementoCronologia In elenco
                    Try
                        If voce Is Nothing OrElse String.IsNullOrWhiteSpace(voce.Testo) Then
                            posizione -= 1
                            Continue For
                        End If

                        Dim data As Long = DataInNumero(voce.DataAggiunta)
                        If voce.DataAggiunta = Date.MinValue Then data = OraCorrente() - CLng(posizione) * 1000L

                        pTes.Value = voce.Testo
                        pOrd.Value = Normalizza(voce.Testo)
                        pDat.Value = data
                        pTip.Value = CInt(voce.Tipo)
                        pChiave.Value = WGDatabase.GeneraChiave()

                        contatore += cmd.ExecuteNonQuery()
                        posizione -= 1
                    Catch exVoce As Exception
                        logger.ScriviLog("ERRORE [YoutubeDb.ImportaElencoRicerche.Voce]: " & exVoce.ToString())
                    End Try
                Next
            End Using
        Catch ex As Exception
            logger.ScriviLog("ERRORE [YoutubeDb.ImportaElencoRicerche]: " & ex.ToString())
        End Try
        Return contatore
    End Function

    Private Function LeggiElementi(cmd As SQLiteCommand, categoriaFissa As String) As List(Of ElementoSalvato)
        Dim risultato As New List(Of ElementoSalvato)
        Try
            Using lettore As SQLiteDataReader = cmd.ExecuteReader()
                While lettore.Read()
                    Try
                        Dim voce As New ElementoSalvato()
                        voce.Id = lettore.GetString(0)
                        voce.Tipo = CType(lettore.GetInt32(1), YoutubeItemType)
                        voce.Titolo = lettore.GetString(2)
                        voce.Url = lettore.GetString(3)
                        voce.DataAggiunta = NumeroInData(lettore.GetInt64(4))

                        If categoriaFissa IsNot Nothing Then
                            voce.Categoria = categoriaFissa
                        ElseIf lettore.FieldCount > 5 AndAlso Not lettore.IsDBNull(5) Then
                            voce.Categoria = lettore.GetString(5)
                        Else
                            voce.Categoria = Nothing
                        End If

                        risultato.Add(voce)
                    Catch exRiga As Exception
                        logger.ScriviLog("ERRORE [YoutubeDb.LeggiElementi.Riga]: " & exRiga.ToString())
                    End Try
                End While
            End Using
        Catch ex As Exception
            logger.ScriviLog("ERRORE [YoutubeDb.LeggiElementi]: " & ex.ToString())
        End Try
        Return risultato
    End Function

    Public Function OttieniPreferiti(tipo As YoutubeItemType) As List(Of ElementoSalvato)
        Try
            Return OttieniPreferitiPagina(tipo, Nothing, OrdinamentoElementi.DataAscendente, 0, _limiteLetturaLegacy)
        Catch ex As Exception
            logger.ScriviLog("ERRORE [YoutubeDb.OttieniPreferiti]: " & ex.ToString())
            Return New List(Of ElementoSalvato)
        End Try
    End Function

    Public Function OttieniPreferitiPagina(tipo As YoutubeItemType, categoria As String, ordine As OrdinamentoElementi, scarto As Long, limite As Long) As List(Of ElementoSalvato)
        Try
            SyncLock _blocco
                If Not _pronto OrElse _connessione Is Nothing Then Return New List(Of ElementoSalvato)

                Dim idCat As Long = -1
                If Not String.IsNullOrWhiteSpace(categoria) Then
                    idCat = IdCategoria(categoria, False)
                    If idCat < 0 Then Return New List(Of ElementoSalvato)
                End If

                Dim sql As New StringBuilder()
                sql.Append("SELECT p.codice, p.tipo, p.titolo, p.url, p.data_aggiunta, c.nome FROM preferiti p ")
                sql.Append("LEFT JOIN categorie c ON c.id_categoria = p.id_categoria WHERE p.tipo = @tip ")
                If idCat >= 0 Then sql.Append("AND p.id_categoria = @cat ")
                sql.Append(ClausolaOrdinamento(ordine, "p.", "id_preferito"))
                sql.Append("LIMIT @lim OFFSET @sca;")

                Using cmd As SQLiteCommand = _connessione.CreateCommand()
                    cmd.CommandText = sql.ToString()
                    cmd.Parameters.AddWithValue("@tip", CInt(tipo))
                    If idCat >= 0 Then cmd.Parameters.AddWithValue("@cat", idCat)
                    cmd.Parameters.AddWithValue("@lim", limite)
                    cmd.Parameters.AddWithValue("@sca", scarto)
                    Return LeggiElementi(cmd, Nothing)
                End Using
            End SyncLock
        Catch ex As Exception
            logger.ScriviLog("ERRORE [YoutubeDb.OttieniPreferitiPagina]: " & ex.ToString())
            Return New List(Of ElementoSalvato)
        End Try
    End Function

    Public Function OttieniPreferitiPerCategorie(tipo As YoutubeItemType, categorie As List(Of String), ordine As OrdinamentoElementi, limite As Long) As List(Of ElementoSalvato)
        Try
            SyncLock _blocco
                If Not _pronto OrElse _connessione Is Nothing Then Return New List(Of ElementoSalvato)
                If categorie Is Nothing OrElse categorie.Count = 0 Then Return New List(Of ElementoSalvato)

                Dim identificativi As New List(Of Long)
                For Each nome As String In categorie
                    Try
                        Dim id As Long = IdCategoria(nome, False)
                        If id >= 0 AndAlso Not identificativi.Contains(id) Then identificativi.Add(id)
                    Catch exCat As Exception
                        logger.ScriviLog("ERRORE [YoutubeDb.OttieniPreferitiPerCategorie.Categoria]: " & exCat.ToString())
                    End Try
                Next

                If identificativi.Count = 0 Then Return New List(Of ElementoSalvato)

                Dim sql As New StringBuilder()
                sql.Append("SELECT p.codice, p.tipo, p.titolo, p.url, p.data_aggiunta, c.nome FROM preferiti p ")
                sql.Append("LEFT JOIN categorie c ON c.id_categoria = p.id_categoria WHERE p.tipo = @tip AND p.id_categoria IN (")

                For i As Integer = 0 To identificativi.Count - 1
                    If i > 0 Then sql.Append(",")
                    sql.Append("@c")
                    sql.Append(i.ToString(CultureInfo.InvariantCulture))
                Next

                sql.Append(") ")
                sql.Append(ClausolaOrdinamento(ordine, "p.", "id_preferito"))
                sql.Append("LIMIT @lim;")

                Using cmd As SQLiteCommand = _connessione.CreateCommand()
                    cmd.CommandText = sql.ToString()
                    cmd.Parameters.AddWithValue("@tip", CInt(tipo))
                    For i As Integer = 0 To identificativi.Count - 1
                        cmd.Parameters.AddWithValue("@c" & i.ToString(CultureInfo.InvariantCulture), identificativi(i))
                    Next
                    cmd.Parameters.AddWithValue("@lim", limite)
                    Return LeggiElementi(cmd, Nothing)
                End Using
            End SyncLock
        Catch ex As Exception
            logger.ScriviLog("ERRORE [YoutubeDb.OttieniPreferitiPerCategorie]: " & ex.ToString())
            Return New List(Of ElementoSalvato)
        End Try
    End Function

    Public Function ContaPreferiti(tipo As YoutubeItemType, categoria As String) As Long
        Try
            SyncLock _blocco
                If Not _pronto OrElse _connessione Is Nothing Then Return 0

                Dim idCat As Long = -1
                If Not String.IsNullOrWhiteSpace(categoria) Then
                    idCat = IdCategoria(categoria, False)
                    If idCat < 0 Then Return 0
                End If

                Using cmd As SQLiteCommand = _connessione.CreateCommand()
                    If idCat >= 0 Then
                        cmd.CommandText = "SELECT COUNT(*) FROM preferiti WHERE id_categoria = @cat AND tipo = @tip;"
                        cmd.Parameters.AddWithValue("@cat", idCat)
                    Else
                        cmd.CommandText = "SELECT COUNT(*) FROM preferiti WHERE tipo = @tip;"
                    End If
                    cmd.Parameters.AddWithValue("@tip", CInt(tipo))
                    Return Convert.ToInt64(cmd.ExecuteScalar(), CultureInfo.InvariantCulture)
                End Using
            End SyncLock
        Catch ex As Exception
            logger.ScriviLog("ERRORE [YoutubeDb.ContaPreferiti]: " & ex.ToString())
            Return 0
        End Try
    End Function

    Public Function OttieniRecenti(tipo As YoutubeItemType) As List(Of ElementoSalvato)
        Try
            Return OttieniRecentiPagina(tipo, OrdinamentoElementi.DataDiscendente, 0, _limiteLetturaLegacy)
        Catch ex As Exception
            logger.ScriviLog("ERRORE [YoutubeDb.OttieniRecenti]: " & ex.ToString())
            Return New List(Of ElementoSalvato)
        End Try
    End Function

    Public Function OttieniRecentiPagina(tipo As YoutubeItemType, ordine As OrdinamentoElementi, scarto As Long, limite As Long) As List(Of ElementoSalvato)
        Try
            SyncLock _blocco
                If Not _pronto OrElse _connessione Is Nothing Then Return New List(Of ElementoSalvato)

                Dim sql As New StringBuilder()
                sql.Append("SELECT codice, tipo, titolo, url, data_aggiunta FROM recenti WHERE tipo = @tip ")
                sql.Append(ClausolaOrdinamento(ordine, "", "id_recente"))
                sql.Append("LIMIT @lim OFFSET @sca;")

                Using cmd As SQLiteCommand = _connessione.CreateCommand()
                    cmd.CommandText = sql.ToString()
                    cmd.Parameters.AddWithValue("@tip", CInt(tipo))
                    cmd.Parameters.AddWithValue("@lim", limite)
                    cmd.Parameters.AddWithValue("@sca", scarto)
                    Return LeggiElementi(cmd, Nothing)
                End Using
            End SyncLock
        Catch ex As Exception
            logger.ScriviLog("ERRORE [YoutubeDb.OttieniRecentiPagina]: " & ex.ToString())
            Return New List(Of ElementoSalvato)
        End Try
    End Function

    Public Function ContaRecenti(tipo As YoutubeItemType) As Long
        Try
            SyncLock _blocco
                If Not _pronto OrElse _connessione Is Nothing Then Return 0

                Using cmd As SQLiteCommand = _connessione.CreateCommand()
                    cmd.CommandText = "SELECT COUNT(*) FROM recenti WHERE tipo = @tip;"
                    cmd.Parameters.AddWithValue("@tip", CInt(tipo))
                    Return Convert.ToInt64(cmd.ExecuteScalar(), CultureInfo.InvariantCulture)
                End Using
            End SyncLock
        Catch ex As Exception
            logger.ScriviLog("ERRORE [YoutubeDb.ContaRecenti]: " & ex.ToString())
            Return 0
        End Try
    End Function

    Public Function OttieniRicercheRecenti() As List(Of ElementoCronologia)
        Try
            Return OttieniRicerchePagina(0, _limiteLetturaLegacy)
        Catch ex As Exception
            logger.ScriviLog("ERRORE [YoutubeDb.OttieniRicercheRecenti]: " & ex.ToString())
            Return New List(Of ElementoCronologia)
        End Try
    End Function

    Public Function OttieniRicerchePagina(scarto As Long, limite As Long) As List(Of ElementoCronologia)
        Dim risultato As New List(Of ElementoCronologia)
        Try
            SyncLock _blocco
                If Not _pronto OrElse _connessione Is Nothing Then Return risultato

                Using cmd As SQLiteCommand = _connessione.CreateCommand()
                    cmd.CommandText = "SELECT testo, data_aggiunta, tipo FROM ricerche ORDER BY data_aggiunta DESC, id_ricerca DESC LIMIT @lim OFFSET @sca;"
                    cmd.Parameters.AddWithValue("@lim", limite)
                    cmd.Parameters.AddWithValue("@sca", scarto)

                    Using lettore As SQLiteDataReader = cmd.ExecuteReader()
                        While lettore.Read()
                            Try
                                Dim voce As New ElementoCronologia()
                                voce.Testo = lettore.GetString(0)
                                voce.DataAggiunta = NumeroInData(lettore.GetInt64(1))
                                If Not lettore.IsDBNull(2) Then
                                    Dim tipVal As Integer = lettore.GetInt32(2)
                                    If [Enum].IsDefined(GetType(YoutubeItemType), tipVal) Then
                                        voce.Tipo = CType(tipVal, YoutubeItemType)
                                    Else
                                        voce.Tipo = YoutubeItemType.Unknown
                                    End If
                                Else
                                    voce.Tipo = YoutubeItemType.Unknown
                                End If
                                risultato.Add(voce)
                            Catch exRiga As Exception
                                logger.ScriviLog("ERRORE [YoutubeDb.OttieniRicerchePagina.Riga]: " & exRiga.ToString())
                            End Try
                        End While
                    End Using
                End Using
            End SyncLock
        Catch ex As Exception
            logger.ScriviLog("ERRORE [YoutubeDb.OttieniRicerchePagina]: " & ex.ToString())
        End Try
        Return risultato
    End Function

    Public Function ContaRicerche() As Long
        Try
            SyncLock _blocco
                If Not _pronto OrElse _connessione Is Nothing Then Return 0

                Using cmd As SQLiteCommand = _connessione.CreateCommand()
                    cmd.CommandText = "SELECT COUNT(*) FROM ricerche;"
                    Return Convert.ToInt64(cmd.ExecuteScalar(), CultureInfo.InvariantCulture)
                End Using
            End SyncLock
        Catch ex As Exception
            logger.ScriviLog("ERRORE [YoutubeDb.ContaRicerche]: " & ex.ToString())
            Return 0
        End Try
    End Function

    Public Sub AggiungiPreferito(id As String, titolo As String, url As String, tipo As YoutubeItemType, Optional categoria As String = CategoriaPredefinita)
        Try
            SyncLock _blocco
                If Not _pronto OrElse _connessione Is Nothing Then Return
                If String.IsNullOrWhiteSpace(id) Then Return

                Dim titoloEffettivo As String = titolo
                If String.IsNullOrWhiteSpace(titoloEffettivo) Then titoloEffettivo = id

                Dim urlEffettivo As String = url
                If urlEffettivo Is Nothing Then urlEffettivo = ""

                Using transazione As SQLiteTransaction = _connessione.BeginTransaction()
                    Try
                        Dim idCat As Long = IdCategoria(categoria, True)
                        If idCat < 0 Then idCat = IdCategoria(CategoriaPredefinita, True)

                        Using cmd As SQLiteCommand = _connessione.CreateCommand()
                            cmd.CommandText = "INSERT OR IGNORE INTO preferiti (codice, tipo, titolo, titolo_ord, url, id_categoria, data_aggiunta, ChiaveWG) VALUES (@cod, @tip, @tit, @ord, @url, @cat, @dat, @chiavewg);"
                            cmd.Parameters.AddWithValue("@cod", id)
                            cmd.Parameters.AddWithValue("@tip", CInt(tipo))
                            cmd.Parameters.AddWithValue("@tit", titoloEffettivo)
                            cmd.Parameters.AddWithValue("@ord", Normalizza(titoloEffettivo))
                            cmd.Parameters.AddWithValue("@url", urlEffettivo)
                            cmd.Parameters.AddWithValue("@cat", idCat)
                            cmd.Parameters.AddWithValue("@dat", OraCorrente())
                            cmd.Parameters.AddWithValue("@chiavewg", WGDatabase.GeneraChiaveUnivocaPerTabella(_connessione, "preferiti"))
                            cmd.ExecuteNonQuery()
                        End Using

                        transazione.Commit()
                    Catch exTrans As Exception
                        logger.ScriviLog("ERRORE [YoutubeDb.AggiungiPreferito.Transazione]: " & exTrans.ToString())
                        Try
                            transazione.Rollback()
                        Catch exRoll As Exception
                            logger.ScriviLog("ERRORE [YoutubeDb.AggiungiPreferito.Rollback]: " & exRoll.ToString())
                        End Try
                    End Try
                End Using
            End SyncLock
        Catch ex As Exception
            logger.ScriviLog("ERRORE [YoutubeDb.AggiungiPreferito]: " & ex.ToString())
        End Try
    End Sub

    Public Sub RimuoviPreferito(id As String, tipo As YoutubeItemType)
        Try
            SyncLock _blocco
                If Not _pronto OrElse _connessione Is Nothing Then Return
                If String.IsNullOrWhiteSpace(id) Then Return

                Using cmd As SQLiteCommand = _connessione.CreateCommand()
                    cmd.CommandText = "DELETE FROM preferiti WHERE tipo = @tip AND codice = @cod;"
                    cmd.Parameters.AddWithValue("@tip", CInt(tipo))
                    cmd.Parameters.AddWithValue("@cod", id)
                    cmd.ExecuteNonQuery()
                End Using
            End SyncLock
        Catch ex As Exception
            logger.ScriviLog("ERRORE [YoutubeDb.RimuoviPreferito]: " & ex.ToString())
        End Try
    End Sub

    Public Function EPreferito(id As String, tipo As YoutubeItemType) As Boolean
        Try
            SyncLock _blocco
                If Not _pronto OrElse _connessione Is Nothing Then Return False
                If String.IsNullOrWhiteSpace(id) Then Return False

                Using cmd As SQLiteCommand = _connessione.CreateCommand()
                    cmd.CommandText = "SELECT 1 FROM preferiti WHERE tipo = @tip AND codice = @cod LIMIT 1;"
                    cmd.Parameters.AddWithValue("@tip", CInt(tipo))
                    cmd.Parameters.AddWithValue("@cod", id)
                    Dim risultato As Object = cmd.ExecuteScalar()
                    Return risultato IsNot Nothing AndAlso Not IsDBNull(risultato)
                End Using
            End SyncLock
        Catch ex As Exception
            logger.ScriviLog("ERRORE [YoutubeDb.EPreferito]: " & ex.ToString())
            Return False
        End Try
    End Function

    Public Sub AggiungiRecente(id As String, titolo As String, url As String, tipo As YoutubeItemType)
        Try
            SyncLock _blocco
                If Not _pronto OrElse _connessione Is Nothing Then Return
                If String.IsNullOrWhiteSpace(id) Then Return

                Dim titoloEffettivo As String = titolo
                If String.IsNullOrWhiteSpace(titoloEffettivo) Then titoloEffettivo = id

                Dim urlEffettivo As String = url
                If urlEffettivo Is Nothing Then urlEffettivo = ""

                Using transazione As SQLiteTransaction = _connessione.BeginTransaction()
                    Try
                        Dim chiaveWGEsistente As String = ""
                        Using cmdChiave As SQLiteCommand = _connessione.CreateCommand()
                            cmdChiave.CommandText = "SELECT ChiaveWG FROM recenti WHERE tipo = @tip AND codice = @cod LIMIT 1;"
                            cmdChiave.Parameters.AddWithValue("@tip", CInt(tipo))
                            cmdChiave.Parameters.AddWithValue("@cod", id)
                            Dim risultatoChiave As Object = cmdChiave.ExecuteScalar()
                            If risultatoChiave IsNot Nothing AndAlso Not IsDBNull(risultatoChiave) Then
                                chiaveWGEsistente = Convert.ToString(risultatoChiave)
                            End If
                        End Using
                        If String.IsNullOrWhiteSpace(chiaveWGEsistente) Then chiaveWGEsistente = WGDatabase.GeneraChiaveUnivocaPerTabella(_connessione, "recenti")

                        Using cmd As SQLiteCommand = _connessione.CreateCommand()
                            cmd.CommandText = "DELETE FROM recenti WHERE tipo = @tip AND codice = @cod;"
                            cmd.Parameters.AddWithValue("@tip", CInt(tipo))
                            cmd.Parameters.AddWithValue("@cod", id)
                            cmd.ExecuteNonQuery()
                        End Using

                        Using cmd As SQLiteCommand = _connessione.CreateCommand()
                            cmd.CommandText = "INSERT INTO recenti (codice, tipo, titolo, titolo_ord, url, data_aggiunta, ChiaveWG) VALUES (@cod, @tip, @tit, @ord, @url, @dat, @chiavewg);"
                            cmd.Parameters.AddWithValue("@cod", id)
                            cmd.Parameters.AddWithValue("@tip", CInt(tipo))
                            cmd.Parameters.AddWithValue("@tit", titoloEffettivo)
                            cmd.Parameters.AddWithValue("@ord", Normalizza(titoloEffettivo))
                            cmd.Parameters.AddWithValue("@url", urlEffettivo)
                            cmd.Parameters.AddWithValue("@dat", OraCorrente())
                            cmd.Parameters.AddWithValue("@chiavewg", chiaveWGEsistente)
                            cmd.ExecuteNonQuery()
                        End Using

                        PotaRecenti(tipo)

                        transazione.Commit()
                    Catch exTrans As Exception
                        logger.ScriviLog("ERRORE [YoutubeDb.AggiungiRecente.Transazione]: " & exTrans.ToString())
                        Try
                            transazione.Rollback()
                        Catch exRoll As Exception
                            logger.ScriviLog("ERRORE [YoutubeDb.AggiungiRecente.Rollback]: " & exRoll.ToString())
                        End Try
                    End Try
                End Using
            End SyncLock
        Catch ex As Exception
            logger.ScriviLog("ERRORE [YoutubeDb.AggiungiRecente]: " & ex.ToString())
        End Try
    End Sub

    Private Sub PotaRecenti(tipo As YoutubeItemType)
        Try
            If _limiteRecenti <= 0 Then Return

            Dim quanti As Long = 0
            Using cmd As SQLiteCommand = _connessione.CreateCommand()
                cmd.CommandText = "SELECT COUNT(*) FROM recenti WHERE tipo = @tip;"
                cmd.Parameters.AddWithValue("@tip", CInt(tipo))
                quanti = Convert.ToInt64(cmd.ExecuteScalar(), CultureInfo.InvariantCulture)
            End Using

            If quanti <= _limiteRecenti Then Return

            Using cmd As SQLiteCommand = _connessione.CreateCommand()
                cmd.CommandText = "DELETE FROM recenti WHERE id_recente IN (SELECT id_recente FROM recenti WHERE tipo = @tip ORDER BY data_aggiunta DESC, id_recente DESC LIMIT -1 OFFSET @lim);"
                cmd.Parameters.AddWithValue("@tip", CInt(tipo))
                cmd.Parameters.AddWithValue("@lim", _limiteRecenti)
                cmd.ExecuteNonQuery()
            End Using
        Catch ex As Exception
            logger.ScriviLog("ERRORE [YoutubeDb.PotaRecenti]: " & ex.ToString())
        End Try
    End Sub

    Public Sub RimuoviRecente(id As String, tipo As YoutubeItemType)
        Try
            SyncLock _blocco
                If Not _pronto OrElse _connessione Is Nothing Then Return
                If String.IsNullOrWhiteSpace(id) Then Return

                Using cmd As SQLiteCommand = _connessione.CreateCommand()
                    cmd.CommandText = "DELETE FROM recenti WHERE tipo = @tip AND codice = @cod;"
                    cmd.Parameters.AddWithValue("@tip", CInt(tipo))
                    cmd.Parameters.AddWithValue("@cod", id)
                    cmd.ExecuteNonQuery()
                End Using
            End SyncLock
        Catch ex As Exception
            logger.ScriviLog("ERRORE [YoutubeDb.RimuoviRecente]: " & ex.ToString())
        End Try
    End Sub

    Public Sub AggiungiRicercaRecente(query As String, Optional tipo As YoutubeItemType = YoutubeItemType.Unknown)
        Try
            SyncLock _blocco
                If Not _pronto OrElse _connessione Is Nothing Then Return
                If String.IsNullOrWhiteSpace(query) Then Return

                Using transazione As SQLiteTransaction = _connessione.BeginTransaction()
                    Try
                        Dim chiave As String = Normalizza(query)

                        Dim chiaveWGEsistente As String = ""
                        Using cmdChiave As SQLiteCommand = _connessione.CreateCommand()
                            cmdChiave.CommandText = "SELECT ChiaveWG FROM ricerche WHERE testo_ord = @ord LIMIT 1;"
                            cmdChiave.Parameters.AddWithValue("@ord", chiave)
                            Dim risultatoChiave As Object = cmdChiave.ExecuteScalar()
                            If risultatoChiave IsNot Nothing AndAlso Not IsDBNull(risultatoChiave) Then
                                chiaveWGEsistente = Convert.ToString(risultatoChiave)
                            End If
                        End Using
                        If String.IsNullOrWhiteSpace(chiaveWGEsistente) Then chiaveWGEsistente = WGDatabase.GeneraChiaveUnivocaPerTabella(_connessione, "ricerche")

                        Using cmd As SQLiteCommand = _connessione.CreateCommand()
                            cmd.CommandText = "DELETE FROM ricerche WHERE testo_ord = @ord;"
                            cmd.Parameters.AddWithValue("@ord", chiave)
                            cmd.ExecuteNonQuery()
                        End Using

                        Using cmd As SQLiteCommand = _connessione.CreateCommand()
                            cmd.CommandText = "INSERT INTO ricerche (testo, testo_ord, data_aggiunta, tipo, ChiaveWG) VALUES (@tes, @ord, @dat, @tip, @chiavewg);"
                            cmd.Parameters.AddWithValue("@tes", query.Trim())
                            cmd.Parameters.AddWithValue("@ord", chiave)
                            cmd.Parameters.AddWithValue("@dat", OraCorrente())
                            cmd.Parameters.AddWithValue("@tip", CInt(tipo))
                            cmd.Parameters.AddWithValue("@chiavewg", chiaveWGEsistente)
                            cmd.ExecuteNonQuery()
                        End Using

                        If _limiteRicerche > 0 Then
                            Using cmd As SQLiteCommand = _connessione.CreateCommand()
                                cmd.CommandText = "DELETE FROM ricerche WHERE id_ricerca IN (SELECT id_ricerca FROM ricerche ORDER BY data_aggiunta DESC, id_ricerca DESC LIMIT -1 OFFSET @lim);"
                                cmd.Parameters.AddWithValue("@lim", _limiteRicerche)
                                cmd.ExecuteNonQuery()
                            End Using
                        End If

                        transazione.Commit()
                    Catch exTrans As Exception
                        logger.ScriviLog("ERRORE [YoutubeDb.AggiungiRicercaRecente.Transazione]: " & exTrans.ToString())
                        Try
                            transazione.Rollback()
                        Catch exRoll As Exception
                            logger.ScriviLog("ERRORE [YoutubeDb.AggiungiRicercaRecente.Rollback]: " & exRoll.ToString())
                        End Try
                    End Try
                End Using
            End SyncLock
        Catch ex As Exception
            logger.ScriviLog("ERRORE [YoutubeDb.AggiungiRicercaRecente]: " & ex.ToString())
        End Try
    End Sub

    Public Sub RimuoviRicercaRecente(query As String)
        Try
            SyncLock _blocco
                If Not _pronto OrElse _connessione Is Nothing Then Return
                If String.IsNullOrWhiteSpace(query) Then Return

                Using cmd As SQLiteCommand = _connessione.CreateCommand()
                    cmd.CommandText = "DELETE FROM ricerche WHERE testo_ord = @ord;"
                    cmd.Parameters.AddWithValue("@ord", Normalizza(query))
                    cmd.ExecuteNonQuery()
                End Using
            End SyncLock
        Catch ex As Exception
            logger.ScriviLog("ERRORE [YoutubeDb.RimuoviRicercaRecente]: " & ex.ToString())
        End Try
    End Sub

    Public Function OttieniCategorie() As List(Of String)
        Dim risultato As New List(Of String)
        Try
            SyncLock _blocco
                If Not _pronto OrElse _connessione Is Nothing Then
                    risultato.Add(CategoriaPredefinita)
                    Return risultato
                End If

                Using cmd As SQLiteCommand = _connessione.CreateCommand()
                    cmd.CommandText = "SELECT nome FROM categorie ORDER BY protetta DESC, ordine ASC, id_categoria ASC;"
                    Using lettore As SQLiteDataReader = cmd.ExecuteReader()
                        While lettore.Read()
                            Try
                                risultato.Add(lettore.GetString(0))
                            Catch exRiga As Exception
                                logger.ScriviLog("ERRORE [YoutubeDb.OttieniCategorie.Riga]: " & exRiga.ToString())
                            End Try
                        End While
                    End Using
                End Using

                If risultato.Count = 0 Then risultato.Add(CategoriaPredefinita)
            End SyncLock
        Catch ex As Exception
            logger.ScriviLog("ERRORE [YoutubeDb.OttieniCategorie]: " & ex.ToString())
            If risultato.Count = 0 Then risultato.Add(CategoriaPredefinita)
        End Try
        Return risultato
    End Function

    Public Function OttieniConteggioCategorie(tipo As YoutubeItemType) As List(Of ConteggioCategoria)
        Dim risultato As New List(Of ConteggioCategoria)
        Try
            SyncLock _blocco
                If Not _pronto OrElse _connessione Is Nothing Then Return risultato

                Using cmd As SQLiteCommand = _connessione.CreateCommand()
                    cmd.CommandText = "SELECT c.nome, COUNT(p.id_preferito) FROM categorie c LEFT JOIN preferiti p ON p.id_categoria = c.id_categoria AND p.tipo = @tip GROUP BY c.id_categoria ORDER BY c.protetta DESC, c.ordine ASC, c.id_categoria ASC;"
                    cmd.Parameters.AddWithValue("@tip", CInt(tipo))

                    Using lettore As SQLiteDataReader = cmd.ExecuteReader()
                        While lettore.Read()
                            Try
                                Dim voce As New ConteggioCategoria()
                                voce.Nome = lettore.GetString(0)
                                voce.Quantita = lettore.GetInt64(1)
                                risultato.Add(voce)
                            Catch exRiga As Exception
                                logger.ScriviLog("ERRORE [YoutubeDb.OttieniConteggioCategorie.Riga]: " & exRiga.ToString())
                            End Try
                        End While
                    End Using
                End Using
            End SyncLock
        Catch ex As Exception
            logger.ScriviLog("ERRORE [YoutubeDb.OttieniConteggioCategorie]: " & ex.ToString())
        End Try
        Return risultato
    End Function

    Public Function HaPiuCategorie() As Boolean
        Try
            SyncLock _blocco
                If Not _pronto OrElse _connessione Is Nothing Then Return False

                Using cmd As SQLiteCommand = _connessione.CreateCommand()
                    cmd.CommandText = "SELECT COUNT(*) FROM categorie;"
                    Return Convert.ToInt64(cmd.ExecuteScalar(), CultureInfo.InvariantCulture) > 1
                End Using
            End SyncLock
        Catch ex As Exception
            logger.ScriviLog("ERRORE [YoutubeDb.HaPiuCategorie]: " & ex.ToString())
            Return False
        End Try
    End Function

    Public Sub AggiungiCategoria(nome As String)
        Try
            SyncLock _blocco
                If Not _pronto OrElse _connessione Is Nothing Then Return
                If String.IsNullOrWhiteSpace(nome) Then Return

                IdCategoria(nome.Trim(), True)
            End SyncLock
        Catch ex As Exception
            logger.ScriviLog("ERRORE [YoutubeDb.AggiungiCategoria]: " & ex.ToString())
        End Try
    End Sub

    Public Sub EliminaCategoria(nome As String)
        Try
            SyncLock _blocco
                If Not _pronto OrElse _connessione Is Nothing Then Return
                If String.IsNullOrWhiteSpace(nome) Then Return
                If Normalizza(nome) = Normalizza(CategoriaPredefinita) Then Return

                Dim idCat As Long = IdCategoria(nome, False)
                If idCat < 0 Then Return

                Dim idPredefinita As Long = IdCategoria(CategoriaPredefinita, True)
                If idPredefinita < 0 Then Return

                Using transazione As SQLiteTransaction = _connessione.BeginTransaction()
                    Try
                        Using cmd As SQLiteCommand = _connessione.CreateCommand()
                            cmd.CommandText = "UPDATE preferiti SET id_categoria = @pre WHERE id_categoria = @vec;"
                            cmd.Parameters.AddWithValue("@pre", idPredefinita)
                            cmd.Parameters.AddWithValue("@vec", idCat)
                            cmd.ExecuteNonQuery()
                        End Using

                        Using cmd As SQLiteCommand = _connessione.CreateCommand()
                            cmd.CommandText = "DELETE FROM categorie WHERE id_categoria = @vec AND protetta = 0;"
                            cmd.Parameters.AddWithValue("@vec", idCat)
                            cmd.ExecuteNonQuery()
                        End Using

                        transazione.Commit()
                    Catch exTrans As Exception
                        logger.ScriviLog("ERRORE [YoutubeDb.EliminaCategoria.Transazione]: " & exTrans.ToString())
                        Try
                            transazione.Rollback()
                        Catch exRoll As Exception
                            logger.ScriviLog("ERRORE [YoutubeDb.EliminaCategoria.Rollback]: " & exRoll.ToString())
                        End Try
                    End Try
                End Using
            End SyncLock
        Catch ex As Exception
            logger.ScriviLog("ERRORE [YoutubeDb.EliminaCategoria]: " & ex.ToString())
        End Try
    End Sub

    Public Sub RinominaCategoria(vecchioNome As String, nuovoNome As String)
        Try
            SyncLock _blocco
                If Not _pronto OrElse _connessione Is Nothing Then Return
                If String.IsNullOrWhiteSpace(vecchioNome) OrElse String.IsNullOrWhiteSpace(nuovoNome) Then Return
                If Normalizza(vecchioNome) = Normalizza(CategoriaPredefinita) Then Return

                Dim idCat As Long = IdCategoria(vecchioNome, False)
                If idCat < 0 Then Return

                Using cmd As SQLiteCommand = _connessione.CreateCommand()
                    cmd.CommandText = "UPDATE categorie SET nome = @n, nome_ord = @o WHERE id_categoria = @id AND protetta = 0;"
                    cmd.Parameters.AddWithValue("@n", nuovoNome.Trim())
                    cmd.Parameters.AddWithValue("@o", Normalizza(nuovoNome))
                    cmd.Parameters.AddWithValue("@id", idCat)
                    cmd.ExecuteNonQuery()
                End Using
            End SyncLock
        Catch ex As Exception
            logger.ScriviLog("ERRORE [YoutubeDb.RinominaCategoria]: " & ex.ToString())
        End Try
    End Sub

    Public Sub ImpostaCategoriaElemento(id As String, tipo As YoutubeItemType, nuovaCategoria As String)
        Try
            SyncLock _blocco
                If Not _pronto OrElse _connessione Is Nothing Then Return
                If String.IsNullOrWhiteSpace(id) Then Return

                Dim idCat As Long = IdCategoria(nuovaCategoria, True)
                If idCat < 0 Then Return

                Using cmd As SQLiteCommand = _connessione.CreateCommand()
                    cmd.CommandText = "UPDATE preferiti SET id_categoria = @cat WHERE tipo = @tip AND codice = @cod;"
                    cmd.Parameters.AddWithValue("@cat", idCat)
                    cmd.Parameters.AddWithValue("@tip", CInt(tipo))
                    cmd.Parameters.AddWithValue("@cod", id)
                    cmd.ExecuteNonQuery()
                End Using
            End SyncLock
        Catch ex As Exception
            logger.ScriviLog("ERRORE [YoutubeDb.ImpostaCategoriaElemento]: " & ex.ToString())
        End Try
    End Sub

    Private Function CostruisciEspressioneFts(testo As String) As String
        Try
            Dim normalizzato As String = Normalizza(testo)
            If String.IsNullOrWhiteSpace(normalizzato) Then Return Nothing

            Dim pezzi() As String = normalizzato.Split(New Char() {" "c, ControlChars.Tab}, StringSplitOptions.RemoveEmptyEntries)
            If pezzi.Length = 0 Then Return Nothing

            Dim espressione As New StringBuilder()

            For i As Integer = 0 To pezzi.Length - 1
                Dim parola As New StringBuilder()
                For j As Integer = 0 To pezzi(i).Length - 1
                    If Char.IsLetterOrDigit(pezzi(i)(j)) Then parola.Append(pezzi(i)(j))
                Next

                If parola.Length > 0 Then
                    If espressione.Length > 0 Then espressione.Append(" AND ")
                    espressione.Append(Chr(34))
                    espressione.Append(parola.ToString())
                    espressione.Append(Chr(34))
                    espressione.Append("*")
                End If
            Next

            If espressione.Length = 0 Then Return Nothing
            Return espressione.ToString()
        Catch ex As Exception
            logger.ScriviLog("ERRORE [YoutubeDb.CostruisciEspressioneFts]: " & ex.ToString())
            Return Nothing
        End Try
    End Function

    Public Function CercaPreferiti(testo As String, tipo As YoutubeItemType, limite As Long) As List(Of ElementoSalvato)
        Try
            SyncLock _blocco
                If Not _pronto OrElse _connessione Is Nothing Then Return New List(Of ElementoSalvato)
                If String.IsNullOrWhiteSpace(testo) Then Return New List(Of ElementoSalvato)

                If _ftsDisponibile Then
                    Dim espressione As String = CostruisciEspressioneFts(testo)
                    If Not String.IsNullOrEmpty(espressione) Then
                        Try
                            Using cmd As SQLiteCommand = _connessione.CreateCommand()
                                cmd.CommandText = "SELECT p.codice, p.tipo, p.titolo, p.url, p.data_aggiunta, c.nome FROM preferiti_fts f JOIN preferiti p ON p.id_preferito = f.rowid LEFT JOIN categorie c ON c.id_categoria = p.id_categoria WHERE preferiti_fts MATCH @q AND p.tipo = @tip ORDER BY rank LIMIT @lim;"
                                cmd.Parameters.AddWithValue("@q", espressione)
                                cmd.Parameters.AddWithValue("@tip", CInt(tipo))
                                cmd.Parameters.AddWithValue("@lim", limite)
                                Return LeggiElementi(cmd, Nothing)
                            End Using
                        Catch exFts As Exception
                            logger.ScriviLog("ERRORE [YoutubeDb.CercaPreferiti.Fts]: " & exFts.ToString())
                        End Try
                    End If
                End If

                Using cmd As SQLiteCommand = _connessione.CreateCommand()
                    cmd.CommandText = "SELECT p.codice, p.tipo, p.titolo, p.url, p.data_aggiunta, c.nome FROM preferiti p LEFT JOIN categorie c ON c.id_categoria = p.id_categoria WHERE p.tipo = @tip AND p.titolo_ord LIKE @q ORDER BY p.titolo_ord ASC LIMIT @lim;"
                    cmd.Parameters.AddWithValue("@tip", CInt(tipo))
                    cmd.Parameters.AddWithValue("@q", "%" & Normalizza(testo) & "%")
                    cmd.Parameters.AddWithValue("@lim", limite)
                    Return LeggiElementi(cmd, Nothing)
                End Using
            End SyncLock
        Catch ex As Exception
            logger.ScriviLog("ERRORE [YoutubeDb.CercaPreferiti]: " & ex.ToString())
            Return New List(Of ElementoSalvato)
        End Try
    End Function

    Public Function CercaRecenti(testo As String, tipo As YoutubeItemType, limite As Long) As List(Of ElementoSalvato)
        Try
            SyncLock _blocco
                If Not _pronto OrElse _connessione Is Nothing Then Return New List(Of ElementoSalvato)
                If String.IsNullOrWhiteSpace(testo) Then Return New List(Of ElementoSalvato)

                If _ftsDisponibile Then
                    Dim espressione As String = CostruisciEspressioneFts(testo)
                    If Not String.IsNullOrEmpty(espressione) Then
                        Try
                            Using cmd As SQLiteCommand = _connessione.CreateCommand()
                                cmd.CommandText = "SELECT r.codice, r.tipo, r.titolo, r.url, r.data_aggiunta FROM recenti_fts f JOIN recenti r ON r.id_recente = f.rowid WHERE recenti_fts MATCH @q AND r.tipo = @tip ORDER BY rank LIMIT @lim;"
                                cmd.Parameters.AddWithValue("@q", espressione)
                                cmd.Parameters.AddWithValue("@tip", CInt(tipo))
                                cmd.Parameters.AddWithValue("@lim", limite)
                                Return LeggiElementi(cmd, Nothing)
                            End Using
                        Catch exFts As Exception
                            logger.ScriviLog("ERRORE [YoutubeDb.CercaRecenti.Fts]: " & exFts.ToString())
                        End Try
                    End If
                End If

                Using cmd As SQLiteCommand = _connessione.CreateCommand()
                    cmd.CommandText = "SELECT codice, tipo, titolo, url, data_aggiunta FROM recenti WHERE tipo = @tip AND titolo_ord LIKE @q ORDER BY data_aggiunta DESC LIMIT @lim;"
                    cmd.Parameters.AddWithValue("@tip", CInt(tipo))
                    cmd.Parameters.AddWithValue("@q", "%" & Normalizza(testo) & "%")
                    cmd.Parameters.AddWithValue("@lim", limite)
                    Return LeggiElementi(cmd, Nothing)
                End Using
            End SyncLock
        Catch ex As Exception
            logger.ScriviLog("ERRORE [YoutubeDb.CercaRecenti]: " & ex.ToString())
            Return New List(Of ElementoSalvato)
        End Try
    End Function

    Public Sub SvuotaPreferiti(tipo As YoutubeItemType)
        Try
            SyncLock _blocco
                If Not _pronto OrElse _connessione Is Nothing Then Return

                Using cmd As SQLiteCommand = _connessione.CreateCommand()
                    cmd.CommandText = "DELETE FROM preferiti WHERE tipo = @tip;"
                    cmd.Parameters.AddWithValue("@tip", CInt(tipo))
                    cmd.ExecuteNonQuery()
                End Using
            End SyncLock
        Catch ex As Exception
            logger.ScriviLog("ERRORE [YoutubeDb.SvuotaPreferiti]: " & ex.ToString())
        End Try
    End Sub

    Public Sub SvuotaRecenti(tipo As YoutubeItemType)
        Try
            SyncLock _blocco
                If Not _pronto OrElse _connessione Is Nothing Then Return

                Using cmd As SQLiteCommand = _connessione.CreateCommand()
                    cmd.CommandText = "DELETE FROM recenti WHERE tipo = @tip;"
                    cmd.Parameters.AddWithValue("@tip", CInt(tipo))
                    cmd.ExecuteNonQuery()
                End Using
            End SyncLock
        Catch ex As Exception
            logger.ScriviLog("ERRORE [YoutubeDb.SvuotaRecenti]: " & ex.ToString())
        End Try
    End Sub

    Public Sub SvuotaRicerche()
        Try
            SyncLock _blocco
                If Not _pronto OrElse _connessione Is Nothing Then Return
                EseguiComando("DELETE FROM ricerche;")
            End SyncLock
        Catch ex As Exception
            logger.ScriviLog("ERRORE [YoutubeDb.SvuotaRicerche]: " & ex.ToString())
        End Try
    End Sub

    Public Sub ImpostaLimiteRecenti(valore As Integer)
        Try
            SyncLock _blocco
                If valore < 0 Then valore = 0
                _limiteRecenti = valore
            End SyncLock
        Catch ex As Exception
            logger.ScriviLog("ERRORE [YoutubeDb.ImpostaLimiteRecenti]: " & ex.ToString())
        End Try
    End Sub

    Public Sub ImpostaLimiteRicerche(valore As Integer)
        Try
            SyncLock _blocco
                If valore < 0 Then valore = 0
                _limiteRicerche = valore
            End SyncLock
        Catch ex As Exception
            logger.ScriviLog("ERRORE [YoutubeDb.ImpostaLimiteRicerche]: " & ex.ToString())
        End Try
    End Sub

    Public Sub ImpostaLimiteLetturaCompleta(valore As Integer)
        Try
            SyncLock _blocco
                If valore < 1 Then valore = 1
                _limiteLetturaLegacy = valore
            End SyncLock
        Catch ex As Exception
            logger.ScriviLog("ERRORE [YoutubeDb.ImpostaLimiteLetturaCompleta]: " & ex.ToString())
        End Try
    End Sub

    Public Sub RicostruisciIndiciTestuali()
        Try
            SyncLock _blocco
                If _connessione Is Nothing Then Return
                If Not _ftsDisponibile Then Return
                EseguiComando("INSERT INTO preferiti_fts(preferiti_fts) VALUES ('rebuild');")
                EseguiComando("INSERT INTO recenti_fts(recenti_fts) VALUES ('rebuild');")
            End SyncLock
        Catch ex As Exception
            logger.ScriviLog("ERRORE [YoutubeDb.RicostruisciIndiciTestuali]: " & ex.ToString())
        End Try
    End Sub

    Public Sub Ottimizza()
        Try
            SyncLock _blocco
                If _connessione Is Nothing Then Return
                EseguiComando("ANALYZE;")
                EseguiComando("PRAGMA optimize;")
                EseguiComando("PRAGMA wal_checkpoint(TRUNCATE);")
            End SyncLock
        Catch ex As Exception
            logger.ScriviLog("ERRORE [YoutubeDb.Ottimizza]: " & ex.ToString())
        End Try
    End Sub

    Public Sub Compatta()
        Try
            SyncLock _blocco
                If _connessione Is Nothing Then Return
                EseguiComando("VACUUM;")
                EseguiComando("ANALYZE;")
            End SyncLock
        Catch ex As Exception
            logger.ScriviLog("ERRORE [YoutubeDb.Compatta]: " & ex.ToString())
        End Try
    End Sub

    Public Function LeggiImpostazioneDb(chiave As String) As String
        Try
            SyncLock _blocco
                If Not _pronto OrElse _connessione Is Nothing Then Return Nothing
                If String.IsNullOrWhiteSpace(chiave) Then Return Nothing
                Return LeggiImpostazione(chiave)
            End SyncLock
        Catch ex As Exception
            logger.ScriviLog("ERRORE [YoutubeDb.LeggiImpostazioneDb]: " & ex.ToString())
            Return Nothing
        End Try
    End Function

    Public Sub ScriviImpostazioneDb(chiave As String, valore As String)
        Try
            SyncLock _blocco
                If Not _pronto OrElse _connessione Is Nothing Then Return
                If String.IsNullOrWhiteSpace(chiave) Then Return
                Dim valoreEffettivo As String = valore
                If valoreEffettivo Is Nothing Then valoreEffettivo = ""
                ScriviImpostazione(chiave, valoreEffettivo)
            End SyncLock
        Catch ex As Exception
            logger.ScriviLog("ERRORE [YoutubeDb.ScriviImpostazioneDb]: " & ex.ToString())
        End Try
    End Sub

    Public Sub ImpostaLimiteVideoNotiPerCanale(valore As Integer)
        Try
            SyncLock _blocco
                If valore < 20 Then valore = 20
                _limiteVideoNotiPerCanale = valore
            End SyncLock
        Catch ex As Exception
            logger.ScriviLog("ERRORE [YoutubeDb.ImpostaLimiteVideoNotiPerCanale]: " & ex.ToString())
        End Try
    End Sub

    Public Function AggiungiIscrizione(idCanale As String, titolo As String, url As String) As Boolean
        Try
            SyncLock _blocco
                If Not _pronto OrElse _connessione Is Nothing Then Return False
                If String.IsNullOrWhiteSpace(idCanale) Then Return False

                Dim titoloEffettivo As String = titolo
                If String.IsNullOrWhiteSpace(titoloEffettivo) Then titoloEffettivo = idCanale

                Dim urlEffettivo As String = url
                If String.IsNullOrWhiteSpace(urlEffettivo) Then urlEffettivo = "https://www.youtube.com/channel/" & idCanale

                Using cmd As SQLiteCommand = _connessione.CreateCommand()
                    cmd.CommandText = "INSERT OR IGNORE INTO iscrizioni (codice, titolo, titolo_ord, url, data_iscrizione, ultimo_controllo, attiva, ChiaveWG) VALUES (@cod, @tit, @ord, @url, @dat, 0, 1, @chiavewg);"
                    cmd.Parameters.AddWithValue("@cod", idCanale)
                    cmd.Parameters.AddWithValue("@tit", titoloEffettivo)
                    cmd.Parameters.AddWithValue("@ord", Normalizza(titoloEffettivo))
                    cmd.Parameters.AddWithValue("@url", urlEffettivo)
                    cmd.Parameters.AddWithValue("@dat", OraCorrente())
                    cmd.Parameters.AddWithValue("@chiavewg", WGDatabase.GeneraChiaveUnivocaPerTabella(_connessione, "iscrizioni"))
                    Return cmd.ExecuteNonQuery() > 0
                End Using
            End SyncLock
        Catch ex As Exception
            logger.ScriviLog("ERRORE [YoutubeDb.AggiungiIscrizione]: " & ex.ToString())
            Return False
        End Try
    End Function

    Public Sub RimuoviIscrizione(idCanale As String)
        Try
            SyncLock _blocco
                If Not _pronto OrElse _connessione Is Nothing Then Return
                If String.IsNullOrWhiteSpace(idCanale) Then Return

                Using transazione As SQLiteTransaction = _connessione.BeginTransaction()
                    Try
                        Using cmd As SQLiteCommand = _connessione.CreateCommand()
                            cmd.CommandText = "DELETE FROM iscrizioni WHERE codice = @cod;"
                            cmd.Parameters.AddWithValue("@cod", idCanale)
                            cmd.ExecuteNonQuery()
                        End Using

                        Using cmd As SQLiteCommand = _connessione.CreateCommand()
                            cmd.CommandText = "DELETE FROM video_noti WHERE codice_canale = @cod;"
                            cmd.Parameters.AddWithValue("@cod", idCanale)
                            cmd.ExecuteNonQuery()
                        End Using

                        Using cmd As SQLiteCommand = _connessione.CreateCommand()
                            cmd.CommandText = "DELETE FROM video_nuovi WHERE codice_canale = @cod;"
                            cmd.Parameters.AddWithValue("@cod", idCanale)
                            cmd.ExecuteNonQuery()
                        End Using

                        transazione.Commit()
                    Catch exTrans As Exception
                        logger.ScriviLog("ERRORE [YoutubeDb.RimuoviIscrizione.Transazione]: " & exTrans.ToString())
                        Try
                            transazione.Rollback()
                        Catch exRoll As Exception
                            logger.ScriviLog("ERRORE [YoutubeDb.RimuoviIscrizione.Rollback]: " & exRoll.ToString())
                        End Try
                    End Try
                End Using
            End SyncLock
        Catch ex As Exception
            logger.ScriviLog("ERRORE [YoutubeDb.RimuoviIscrizione]: " & ex.ToString())
        End Try
    End Sub

    Public Function EIscritto(idCanale As String) As Boolean
        Try
            SyncLock _blocco
                If Not _pronto OrElse _connessione Is Nothing Then Return False
                If String.IsNullOrWhiteSpace(idCanale) Then Return False

                Using cmd As SQLiteCommand = _connessione.CreateCommand()
                    cmd.CommandText = "SELECT 1 FROM iscrizioni WHERE codice = @cod LIMIT 1;"
                    cmd.Parameters.AddWithValue("@cod", idCanale)
                    Dim risultato As Object = cmd.ExecuteScalar()
                    Return risultato IsNot Nothing AndAlso Not IsDBNull(risultato)
                End Using
            End SyncLock
        Catch ex As Exception
            logger.ScriviLog("ERRORE [YoutubeDb.EIscritto]: " & ex.ToString())
            Return False
        End Try
    End Function

    Public Sub ImpostaIscrizioneAttiva(idCanale As String, attiva As Boolean)
        Try
            SyncLock _blocco
                If Not _pronto OrElse _connessione Is Nothing Then Return
                If String.IsNullOrWhiteSpace(idCanale) Then Return

                Using cmd As SQLiteCommand = _connessione.CreateCommand()
                    cmd.CommandText = "UPDATE iscrizioni SET attiva = @att WHERE codice = @cod;"
                    cmd.Parameters.AddWithValue("@att", If(attiva, 1, 0))
                    cmd.Parameters.AddWithValue("@cod", idCanale)
                    cmd.ExecuteNonQuery()
                End Using
            End SyncLock
        Catch ex As Exception
            logger.ScriviLog("ERRORE [YoutubeDb.ImpostaIscrizioneAttiva]: " & ex.ToString())
        End Try
    End Sub

    Public Function ContaIscrizioni(soloAttive As Boolean) As Long
        Try
            SyncLock _blocco
                If Not _pronto OrElse _connessione Is Nothing Then Return 0

                Using cmd As SQLiteCommand = _connessione.CreateCommand()
                    If soloAttive Then
                        cmd.CommandText = "SELECT COUNT(*) FROM iscrizioni WHERE attiva = 1;"
                    Else
                        cmd.CommandText = "SELECT COUNT(*) FROM iscrizioni;"
                    End If
                    Return Convert.ToInt64(cmd.ExecuteScalar(), CultureInfo.InvariantCulture)
                End Using
            End SyncLock
        Catch ex As Exception
            logger.ScriviLog("ERRORE [YoutubeDb.ContaIscrizioni]: " & ex.ToString())
            Return 0
        End Try
    End Function

    Public Function OttieniIscrizioni(soloAttive As Boolean) As List(Of IscrizioneCanale)
        Dim risultato As New List(Of IscrizioneCanale)
        Try
            SyncLock _blocco
                If Not _pronto OrElse _connessione Is Nothing Then Return risultato

                Dim sql As New StringBuilder()
                sql.Append("SELECT i.codice, i.titolo, i.url, i.data_iscrizione, i.ultimo_controllo, i.attiva, ")
                sql.Append("(SELECT COUNT(*) FROM video_nuovi n WHERE n.codice_canale = i.codice AND n.visto = 0) ")
                sql.Append("FROM iscrizioni i ")
                If soloAttive Then sql.Append("WHERE i.attiva = 1 ")
                sql.Append("ORDER BY i.titolo_ord ASC, i.id_iscrizione ASC;")

                Using cmd As SQLiteCommand = _connessione.CreateCommand()
                    cmd.CommandText = sql.ToString()
                    Using lettore As SQLiteDataReader = cmd.ExecuteReader()
                        While lettore.Read()
                            Try
                                Dim voce As New IscrizioneCanale()
                                voce.IdCanale = lettore.GetString(0)
                                voce.Titolo = lettore.GetString(1)
                                voce.Url = lettore.GetString(2)
                                voce.DataIscrizione = NumeroInDataSicura(lettore.GetInt64(3))
                                voce.UltimoControllo = NumeroInDataSicura(lettore.GetInt64(4))
                                voce.Attiva = (lettore.GetInt32(5) <> 0)
                                voce.NuoviNonVisti = lettore.GetInt64(6)
                                risultato.Add(voce)
                            Catch exRiga As Exception
                                logger.ScriviLog("ERRORE [YoutubeDb.OttieniIscrizioni.Riga]: " & exRiga.ToString())
                            End Try
                        End While
                    End Using
                End Using
            End SyncLock
        Catch ex As Exception
            logger.ScriviLog("ERRORE [YoutubeDb.OttieniIscrizioni]: " & ex.ToString())
        End Try
        Return risultato
    End Function

    Public Function OttieniIscrizione(idCanale As String) As IscrizioneCanale
        Try
            SyncLock _blocco
                If Not _pronto OrElse _connessione Is Nothing Then Return Nothing
                If String.IsNullOrWhiteSpace(idCanale) Then Return Nothing

                Using cmd As SQLiteCommand = _connessione.CreateCommand()
                    cmd.CommandText = "SELECT codice, titolo, url, data_iscrizione, ultimo_controllo, attiva FROM iscrizioni WHERE codice = @cod LIMIT 1;"
                    cmd.Parameters.AddWithValue("@cod", idCanale)
                    Using lettore As SQLiteDataReader = cmd.ExecuteReader()
                        If lettore.Read() Then
                            Dim voce As New IscrizioneCanale()
                            voce.IdCanale = lettore.GetString(0)
                            voce.Titolo = lettore.GetString(1)
                            voce.Url = lettore.GetString(2)
                            voce.DataIscrizione = NumeroInDataSicura(lettore.GetInt64(3))
                            voce.UltimoControllo = NumeroInDataSicura(lettore.GetInt64(4))
                            voce.Attiva = (lettore.GetInt32(5) <> 0)
                            Return voce
                        End If
                    End Using
                End Using
            End SyncLock
            Return Nothing
        Catch ex As Exception
            logger.ScriviLog("ERRORE [YoutubeDb.OttieniIscrizione]: " & ex.ToString())
            Return Nothing
        End Try
    End Function

    Public Sub AggiornaUltimoControlloIscrizione(idCanale As String)
        Try
            SyncLock _blocco
                If Not _pronto OrElse _connessione Is Nothing Then Return
                If String.IsNullOrWhiteSpace(idCanale) Then Return

                Using cmd As SQLiteCommand = _connessione.CreateCommand()
                    cmd.CommandText = "UPDATE iscrizioni SET ultimo_controllo = @dat WHERE codice = @cod;"
                    cmd.Parameters.AddWithValue("@dat", OraCorrente())
                    cmd.Parameters.AddWithValue("@cod", idCanale)
                    cmd.ExecuteNonQuery()
                End Using
            End SyncLock
        Catch ex As Exception
            logger.ScriviLog("ERRORE [YoutubeDb.AggiornaUltimoControlloIscrizione]: " & ex.ToString())
        End Try
    End Sub

    Public Function RegistraVideoNotiIniziali(idCanale As String, identificativi As List(Of String)) As Long
        Dim contatore As Long = 0
        Try
            SyncLock _blocco
                If Not _pronto OrElse _connessione Is Nothing Then Return 0
                If String.IsNullOrWhiteSpace(idCanale) Then Return 0
                If identificativi Is Nothing OrElse identificativi.Count = 0 Then Return 0

                Using transazione As SQLiteTransaction = _connessione.BeginTransaction()
                    Try
                        Using cmd As SQLiteCommand = _connessione.CreateCommand()
                            cmd.CommandText = "INSERT OR IGNORE INTO video_noti (codice_canale, codice_video, data_registrazione) VALUES (@can, @vid, @dat);"
                            Dim pCan As SQLiteParameter = cmd.Parameters.Add("@can", DbType.String)
                            Dim pVid As SQLiteParameter = cmd.Parameters.Add("@vid", DbType.String)
                            Dim pDat As SQLiteParameter = cmd.Parameters.Add("@dat", DbType.Int64)
                            Dim adesso As Long = OraCorrente()

                            For Each identificativo As String In identificativi
                                Try
                                    If String.IsNullOrWhiteSpace(identificativo) Then Continue For
                                    pCan.Value = idCanale
                                    pVid.Value = identificativo
                                    pDat.Value = adesso
                                    contatore += cmd.ExecuteNonQuery()
                                Catch exVoce As Exception
                                    logger.ScriviLog("ERRORE [YoutubeDb.RegistraVideoNotiIniziali.Voce]: " & exVoce.ToString())
                                End Try
                            Next
                        End Using

                        PotaVideoNoti(idCanale)
                        transazione.Commit()
                    Catch exTrans As Exception
                        logger.ScriviLog("ERRORE [YoutubeDb.RegistraVideoNotiIniziali.Transazione]: " & exTrans.ToString())
                        Try
                            transazione.Rollback()
                        Catch exRoll As Exception
                            logger.ScriviLog("ERRORE [YoutubeDb.RegistraVideoNotiIniziali.Rollback]: " & exRoll.ToString())
                        End Try
                        Return 0
                    End Try
                End Using
            End SyncLock
        Catch ex As Exception
            logger.ScriviLog("ERRORE [YoutubeDb.RegistraVideoNotiIniziali]: " & ex.ToString())
        End Try
        Return contatore
    End Function

    Public Function EVideoNoto(idCanale As String, idVideo As String) As Boolean
        Try
            SyncLock _blocco
                If Not _pronto OrElse _connessione Is Nothing Then Return True
                If String.IsNullOrWhiteSpace(idCanale) OrElse String.IsNullOrWhiteSpace(idVideo) Then Return True

                Using cmd As SQLiteCommand = _connessione.CreateCommand()
                    cmd.CommandText = "SELECT 1 FROM video_noti WHERE codice_canale = @can AND codice_video = @vid LIMIT 1;"
                    cmd.Parameters.AddWithValue("@can", idCanale)
                    cmd.Parameters.AddWithValue("@vid", idVideo)
                    Dim risultato As Object = cmd.ExecuteScalar()
                    Return risultato IsNot Nothing AndAlso Not IsDBNull(risultato)
                End Using
            End SyncLock
        Catch ex As Exception
            logger.ScriviLog("ERRORE [YoutubeDb.EVideoNoto]: " & ex.ToString())
            Return True
        End Try
    End Function

    Public Function RegistraVideoRilevato(idCanale As String, nomeCanale As String, idVideo As String, titolo As String, url As String, durata As String, dataPubblicazione As Date) As Boolean
        Try
            SyncLock _blocco
                If Not _pronto OrElse _connessione Is Nothing Then Return False
                If String.IsNullOrWhiteSpace(idCanale) OrElse String.IsNullOrWhiteSpace(idVideo) Then Return False

                Dim giaNoto As Boolean = False
                Using cmd As SQLiteCommand = _connessione.CreateCommand()
                    cmd.CommandText = "SELECT 1 FROM video_noti WHERE codice_canale = @can AND codice_video = @vid LIMIT 1;"
                    cmd.Parameters.AddWithValue("@can", idCanale)
                    cmd.Parameters.AddWithValue("@vid", idVideo)
                    Dim risultato As Object = cmd.ExecuteScalar()
                    giaNoto = risultato IsNot Nothing AndAlso Not IsDBNull(risultato)
                End Using

                If giaNoto Then Return False

                Dim titoloEffettivo As String = titolo
                If String.IsNullOrWhiteSpace(titoloEffettivo) Then titoloEffettivo = idVideo

                Dim nomeCanaleEffettivo As String = nomeCanale
                If String.IsNullOrWhiteSpace(nomeCanaleEffettivo) Then nomeCanaleEffettivo = idCanale

                Dim urlEffettivo As String = url
                If String.IsNullOrWhiteSpace(urlEffettivo) Then urlEffettivo = "https://www.youtube.com/watch?v=" & idVideo

                Dim durataEffettiva As String = durata
                If durataEffettiva Is Nothing Then durataEffettiva = ""

                Dim dataPub As Long = 0
                If dataPubblicazione <> Date.MinValue Then dataPub = DataInNumero(dataPubblicazione)

                Using transazione As SQLiteTransaction = _connessione.BeginTransaction()
                    Try
                        Using cmd As SQLiteCommand = _connessione.CreateCommand()
                            cmd.CommandText = "INSERT OR IGNORE INTO video_noti (codice_canale, codice_video, data_registrazione) VALUES (@can, @vid, @dat);"
                            cmd.Parameters.AddWithValue("@can", idCanale)
                            cmd.Parameters.AddWithValue("@vid", idVideo)
                            cmd.Parameters.AddWithValue("@dat", OraCorrente())
                            cmd.ExecuteNonQuery()
                        End Using

                        Using cmd As SQLiteCommand = _connessione.CreateCommand()
                            cmd.CommandText = "INSERT OR IGNORE INTO video_nuovi (codice_video, codice_canale, titolo_canale, titolo, titolo_ord, url, durata, data_pubblicazione, data_rilevamento, visto, ChiaveWG) VALUES (@vid, @can, @tca, @tit, @ord, @url, @dur, @pub, @ril, 0, @chiavewg);"
                            cmd.Parameters.AddWithValue("@vid", idVideo)
                            cmd.Parameters.AddWithValue("@can", idCanale)
                            cmd.Parameters.AddWithValue("@tca", nomeCanaleEffettivo)
                            cmd.Parameters.AddWithValue("@tit", titoloEffettivo)
                            cmd.Parameters.AddWithValue("@ord", Normalizza(titoloEffettivo))
                            cmd.Parameters.AddWithValue("@url", urlEffettivo)
                            cmd.Parameters.AddWithValue("@dur", durataEffettiva)
                            cmd.Parameters.AddWithValue("@pub", dataPub)
                            cmd.Parameters.AddWithValue("@ril", OraCorrente())
                            cmd.Parameters.AddWithValue("@chiavewg", WGDatabase.GeneraChiaveUnivocaPerTabella(_connessione, "video_nuovi"))
                            cmd.ExecuteNonQuery()
                        End Using

                        PotaVideoNoti(idCanale)
                        transazione.Commit()
                    Catch exTrans As Exception
                        logger.ScriviLog("ERRORE [YoutubeDb.RegistraVideoRilevato.Transazione]: " & exTrans.ToString())
                        Try
                            transazione.Rollback()
                        Catch exRoll As Exception
                            logger.ScriviLog("ERRORE [YoutubeDb.RegistraVideoRilevato.Rollback]: " & exRoll.ToString())
                        End Try
                        Return False
                    End Try
                End Using

                Return True
            End SyncLock
        Catch ex As Exception
            logger.ScriviLog("ERRORE [YoutubeDb.RegistraVideoRilevato]: " & ex.ToString())
            Return False
        End Try
    End Function

    Private Sub PotaVideoNoti(idCanale As String)
        Try
            If _connessione Is Nothing Then Return
            If String.IsNullOrWhiteSpace(idCanale) Then Return
            If _limiteVideoNotiPerCanale <= 0 Then Return

            Using cmd As SQLiteCommand = _connessione.CreateCommand()
                cmd.CommandText = "DELETE FROM video_noti WHERE codice_canale = @can AND codice_video IN (SELECT codice_video FROM video_noti WHERE codice_canale = @can ORDER BY data_registrazione DESC, codice_video DESC LIMIT -1 OFFSET @lim);"
                cmd.Parameters.AddWithValue("@can", idCanale)
                cmd.Parameters.AddWithValue("@lim", _limiteVideoNotiPerCanale)
                cmd.ExecuteNonQuery()
            End Using
        Catch ex As Exception
            logger.ScriviLog("ERRORE [YoutubeDb.PotaVideoNoti]: " & ex.ToString())
        End Try
    End Sub

    Public Function OttieniVideoNuovi(idCanale As String, soloNonVisti As Boolean, scarto As Long, limite As Long) As List(Of VideoNuovo)
        Dim risultato As New List(Of VideoNuovo)
        Try
            SyncLock _blocco
                If Not _pronto OrElse _connessione Is Nothing Then Return risultato

                Dim sql As New StringBuilder()
                sql.Append("SELECT codice_video, codice_canale, titolo_canale, titolo, url, durata, data_pubblicazione, data_rilevamento, visto FROM video_nuovi WHERE 1 = 1 ")
                If Not String.IsNullOrWhiteSpace(idCanale) Then sql.Append("AND codice_canale = @can ")
                If soloNonVisti Then sql.Append("AND visto = 0 ")
                sql.Append("ORDER BY data_pubblicazione DESC, data_rilevamento DESC, id_nuovo DESC LIMIT @lim OFFSET @sca;")

                Using cmd As SQLiteCommand = _connessione.CreateCommand()
                    cmd.CommandText = sql.ToString()
                    If Not String.IsNullOrWhiteSpace(idCanale) Then cmd.Parameters.AddWithValue("@can", idCanale)
                    cmd.Parameters.AddWithValue("@lim", limite)
                    cmd.Parameters.AddWithValue("@sca", scarto)

                    Using lettore As SQLiteDataReader = cmd.ExecuteReader()
                        While lettore.Read()
                            Try
                                Dim voce As New VideoNuovo()
                                voce.IdVideo = lettore.GetString(0)
                                voce.IdCanale = lettore.GetString(1)
                                voce.NomeCanale = lettore.GetString(2)
                                voce.Titolo = lettore.GetString(3)
                                voce.Url = lettore.GetString(4)
                                voce.Durata = lettore.GetString(5)
                                voce.DataPubblicazione = NumeroInDataSicura(lettore.GetInt64(6))
                                voce.DataRilevamento = NumeroInDataSicura(lettore.GetInt64(7))
                                voce.Visto = (lettore.GetInt32(8) <> 0)
                                risultato.Add(voce)
                            Catch exRiga As Exception
                                logger.ScriviLog("ERRORE [YoutubeDb.OttieniVideoNuovi.Riga]: " & exRiga.ToString())
                            End Try
                        End While
                    End Using
                End Using
            End SyncLock
        Catch ex As Exception
            logger.ScriviLog("ERRORE [YoutubeDb.OttieniVideoNuovi]: " & ex.ToString())
        End Try
        Return risultato
    End Function

    Public Function ContaVideoNuovi(idCanale As String, soloNonVisti As Boolean) As Long
        Try
            SyncLock _blocco
                If Not _pronto OrElse _connessione Is Nothing Then Return 0

                Dim sql As New StringBuilder()
                sql.Append("SELECT COUNT(*) FROM video_nuovi WHERE 1 = 1 ")
                If Not String.IsNullOrWhiteSpace(idCanale) Then sql.Append("AND codice_canale = @can ")
                If soloNonVisti Then sql.Append("AND visto = 0 ")
                sql.Append(";")

                Using cmd As SQLiteCommand = _connessione.CreateCommand()
                    cmd.CommandText = sql.ToString()
                    If Not String.IsNullOrWhiteSpace(idCanale) Then cmd.Parameters.AddWithValue("@can", idCanale)
                    Return Convert.ToInt64(cmd.ExecuteScalar(), CultureInfo.InvariantCulture)
                End Using
            End SyncLock
        Catch ex As Exception
            logger.ScriviLog("ERRORE [YoutubeDb.ContaVideoNuovi]: " & ex.ToString())
            Return 0
        End Try
    End Function

    Public Function OttieniCanaliConNuoviVideo() As List(Of CanaleConNuoviVideo)
        Dim risultato As New List(Of CanaleConNuoviVideo)
        Try
            SyncLock _blocco
                If Not _pronto OrElse _connessione Is Nothing Then Return risultato

                Using cmd As SQLiteCommand = _connessione.CreateCommand()
                    cmd.CommandText = "SELECT n.codice_canale, MAX(n.titolo_canale), COUNT(*) FROM video_nuovi n WHERE n.visto = 0 GROUP BY n.codice_canale ORDER BY COUNT(*) DESC, MAX(n.titolo_canale) ASC;"
                    Using lettore As SQLiteDataReader = cmd.ExecuteReader()
                        While lettore.Read()
                            Try
                                Dim voce As New CanaleConNuoviVideo()
                                voce.IdCanale = lettore.GetString(0)
                                voce.Titolo = lettore.GetString(1)
                                voce.Quantita = lettore.GetInt64(2)
                                risultato.Add(voce)
                            Catch exRiga As Exception
                                logger.ScriviLog("ERRORE [YoutubeDb.OttieniCanaliConNuoviVideo.Riga]: " & exRiga.ToString())
                            End Try
                        End While
                    End Using
                End Using
            End SyncLock
        Catch ex As Exception
            logger.ScriviLog("ERRORE [YoutubeDb.OttieniCanaliConNuoviVideo]: " & ex.ToString())
        End Try
        Return risultato
    End Function

    Public Sub SegnaVideoVisti(identificativi As List(Of String))
        Try
            SyncLock _blocco
                If Not _pronto OrElse _connessione Is Nothing Then Return
                If identificativi Is Nothing OrElse identificativi.Count = 0 Then Return

                Using transazione As SQLiteTransaction = _connessione.BeginTransaction()
                    Try
                        Using cmd As SQLiteCommand = _connessione.CreateCommand()
                            cmd.CommandText = "UPDATE video_nuovi SET visto = 1 WHERE codice_video = @vid;"
                            Dim pVid As SQLiteParameter = cmd.Parameters.Add("@vid", DbType.String)

                            For Each identificativo As String In identificativi
                                Try
                                    If String.IsNullOrWhiteSpace(identificativo) Then Continue For
                                    pVid.Value = identificativo
                                    cmd.ExecuteNonQuery()
                                Catch exVoce As Exception
                                    logger.ScriviLog("ERRORE [YoutubeDb.SegnaVideoVisti.Voce]: " & exVoce.ToString())
                                End Try
                            Next
                        End Using

                        transazione.Commit()
                    Catch exTrans As Exception
                        logger.ScriviLog("ERRORE [YoutubeDb.SegnaVideoVisti.Transazione]: " & exTrans.ToString())
                        Try
                            transazione.Rollback()
                        Catch exRoll As Exception
                            logger.ScriviLog("ERRORE [YoutubeDb.SegnaVideoVisti.Rollback]: " & exRoll.ToString())
                        End Try
                    End Try
                End Using
            End SyncLock
        Catch ex As Exception
            logger.ScriviLog("ERRORE [YoutubeDb.SegnaVideoVisti]: " & ex.ToString())
        End Try
    End Sub

    Public Sub SegnaVideoVistiCanale(idCanale As String)
        Try
            SyncLock _blocco
                If Not _pronto OrElse _connessione Is Nothing Then Return
                If String.IsNullOrWhiteSpace(idCanale) Then Return

                Using cmd As SQLiteCommand = _connessione.CreateCommand()
                    cmd.CommandText = "UPDATE video_nuovi SET visto = 1 WHERE codice_canale = @can AND visto = 0;"
                    cmd.Parameters.AddWithValue("@can", idCanale)
                    cmd.ExecuteNonQuery()
                End Using
            End SyncLock
        Catch ex As Exception
            logger.ScriviLog("ERRORE [YoutubeDb.SegnaVideoVistiCanale]: " & ex.ToString())
        End Try
    End Sub

    Public Sub SegnaTuttiVideoVisti()
        Try
            SyncLock _blocco
                If Not _pronto OrElse _connessione Is Nothing Then Return
                EseguiComando("UPDATE video_nuovi SET visto = 1 WHERE visto = 0;")
            End SyncLock
        Catch ex As Exception
            logger.ScriviLog("ERRORE [YoutubeDb.SegnaTuttiVideoVisti]: " & ex.ToString())
        End Try
    End Sub

    Public Sub SvuotaVideoNuovi()
        Try
            SyncLock _blocco
                If Not _pronto OrElse _connessione Is Nothing Then Return
                EseguiComando("DELETE FROM video_nuovi;")
            End SyncLock
        Catch ex As Exception
            logger.ScriviLog("ERRORE [YoutubeDb.SvuotaVideoNuovi]: " & ex.ToString())
        End Try
    End Sub

    Public Sub EliminaVideoNuoviVistiVecchi(giorni As Integer)
        Try
            SyncLock _blocco
                If Not _pronto OrElse _connessione Is Nothing Then Return
                If giorni <= 0 Then Return

                Dim soglia As Long = OraCorrente() - CLng(giorni) * 86400000L

                Using cmd As SQLiteCommand = _connessione.CreateCommand()
                    cmd.CommandText = "DELETE FROM video_nuovi WHERE visto = 1 AND data_rilevamento < @sog;"
                    cmd.Parameters.AddWithValue("@sog", soglia)
                    cmd.ExecuteNonQuery()
                End Using
            End SyncLock
        Catch ex As Exception
            logger.ScriviLog("ERRORE [YoutubeDb.EliminaVideoNuoviVistiVecchi]: " & ex.ToString())
        End Try
    End Sub

    Private Function NumeroInDataSicura(valore As Long) As Date
        Try
            If valore <= 0 Then Return Date.MinValue
            Return NumeroInData(valore)
        Catch ex As Exception
            logger.ScriviLog("ERRORE [YoutubeDb.NumeroInDataSicura]: " & ex.ToString())
            Return Date.MinValue
        End Try
    End Function
    Public Sub Dispose() Implements IDisposable.Dispose
        Try
            SyncLock _blocco
                If _smaltito Then Return
                _smaltito = True
                _pronto = False

                If _connessione IsNot Nothing Then
                    Try
                        EseguiComando("PRAGMA optimize;")
                    Catch exOtt As Exception
                        logger.ScriviLog("ERRORE [YoutubeDb.Dispose.Ottimizza]: " & exOtt.ToString())
                    End Try

                    Try
                        _connessione.Close()
                    Catch exChiudi As Exception
                        logger.ScriviLog("ERRORE [YoutubeDb.Dispose.Chiudi]: " & exChiudi.ToString())
                    End Try

                    _connessione.Dispose()
                    _connessione = Nothing
                End If
            End SyncLock

            GC.SuppressFinalize(Me)
        Catch ex As Exception
            logger.ScriviLog("ERRORE [YoutubeDb.Dispose]: " & ex.ToString())
        End Try
    End Sub
End Class

Public Class XmlManager
    Inherits YoutubeDb
End Class