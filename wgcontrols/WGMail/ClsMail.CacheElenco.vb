Imports System
Imports System.Collections.Generic
Imports System.Data
Imports System.Globalization
Imports MailKit
Imports MimeKit

' Gestione della cache locale per l'elenco dei messaggi e la cronologia delle ricerche.
Partial Public Class CLSMail

    Private Const NomeTabellaCacheElenco As String = "cache_elenco"
    Private Const NomeTabellaCacheStato As String = "cache_elenco_stato"
    Private Const MassimoRiepiloghiPerCartella As Integer = 800
    Private Const RighePerInserimento As Integer = 60

    Private _tabelleCachePronte As Boolean = False
    Private ReadOnly _lockTabelleCache As New Object()

    Private Sub PreparaTabelleCacheElenco()
        Try
            SyncLock _lockTabelleCache
                If _tabelleCachePronte Then Return
                _db.EseguiNonQuery("CREATE TABLE IF NOT EXISTS " & NomeTabellaCacheElenco & " (" &
                                   "ChiaveAccount TEXT NOT NULL, Cartella TEXT NOT NULL, Uid INTEGER NOT NULL, " &
                                   "Oggetto TEXT, Mittente TEXT, Data TEXT, DataUtc TEXT, Letto INTEGER DEFAULT 0, " &
                                   "PRIMARY KEY (ChiaveAccount, Cartella, Uid));")

                ' Copia di 'Data' normalizzata in UTC (stesso fuso orario per tutti i messaggi):
                ' e' quella su cui si ordina e si filtra in SQL. 'Data' da sola non va bene per
                ' l'ordinamento perche' viene salvata con il fuso orario originale del mittente, e
                ' due messaggi vicini nel tempo ma con fusi diversi possono confrontarsi male come
                ' semplice testo pur essendo entrambi in formato ISO 8601.
                Try
                    _db.EseguiNonQuery("ALTER TABLE " & NomeTabellaCacheElenco & " ADD COLUMN DataUtc TEXT;")
                Catch
                    ' Colonna gia' presente (installazioni precedenti all'aggiunta): non e' un errore.
                End Try

                ' Indice per l'elenco ordinato per data (l'ordine che l'utente vede davvero, non
                ' l'ordine di arrivo lato server) e indice dedicato per il filtro veloce dei soli
                ' messaggi non letti, gia' ordinati per data.
                _db.EseguiNonQuery("CREATE INDEX IF NOT EXISTS idx_cache_elenco_data ON " & NomeTabellaCacheElenco &
                                   " (ChiaveAccount, Cartella, DataUtc DESC);")
                _db.EseguiNonQuery("CREATE INDEX IF NOT EXISTS idx_cache_elenco_non_letti ON " & NomeTabellaCacheElenco &
                                   " (ChiaveAccount, Cartella, Letto, DataUtc DESC);")

                _db.EseguiNonQuery("CREATE TABLE IF NOT EXISTS " & NomeTabellaCacheStato & " (" &
                                   "ChiaveAccount TEXT NOT NULL, Cartella TEXT NOT NULL, UidValidity INTEGER DEFAULT 0, " &
                                   "Totale INTEGER DEFAULT 0, Aggiornato TEXT, " &
                                   "PRIMARY KEY (ChiaveAccount, Cartella));")
                _tabelleCachePronte = True
            End SyncLock
        Catch ex As Exception
            ScriviLog("CLSMail.PreparaTabelleCacheElenco: " & ex.ToString())
        End Try
    End Sub

    Public Sub SvuotaCacheElenco(chiaveAccount As String, nomeCartella As String)
        Try
            PreparaTabelleCacheElenco()
            Dim parametri As New Dictionary(Of String, Object) From {
                {"@account", If(chiaveAccount, "")},
                {"@cartella", If(nomeCartella, "")}
            }
            _db.EseguiNonQuery("DELETE FROM " & NomeTabellaCacheElenco & " WHERE ChiaveAccount=@account AND Cartella=@cartella;", parametri)
            _db.EseguiNonQuery("DELETE FROM " & NomeTabellaCacheStato & " WHERE ChiaveAccount=@account AND Cartella=@cartella;", parametri)
        Catch ex As Exception
            ScriviLog("CLSMail.SvuotaCacheElenco (" & If(chiaveAccount, "") & ", " & If(nomeCartella, "") & "): " & ex.ToString())
        End Try
    End Sub

    Public Sub RimuoviDallaCacheElenco(chiaveAccount As String, nomeCartella As String, identificativi As List(Of String))
        Try
            If identificativi Is Nothing OrElse identificativi.Count = 0 Then Return
            PreparaTabelleCacheElenco()
            For Each identificativo As String In identificativi
                Try
                    Dim numero As UInteger = 0
                    If Not UInteger.TryParse(If(identificativo, "").Trim(), numero) Then Continue For
                    _db.EseguiNonQuery("DELETE FROM " & NomeTabellaCacheElenco &
                                       " WHERE ChiaveAccount=@account AND Cartella=@cartella AND Uid=@uid;",
                                       New Dictionary(Of String, Object) From {
                                           {"@account", If(chiaveAccount, "")},
                                           {"@cartella", If(nomeCartella, "")},
                                           {"@uid", Convert.ToInt64(numero)}
                                       })
                Catch exSingolo As Exception
                    ScriviLog("CLSMail.RimuoviDallaCacheElenco singolo: " & exSingolo.Message)
                End Try
            Next
        Catch ex As Exception
            ScriviLog("CLSMail.RimuoviDallaCacheElenco: " & ex.ToString())
        End Try
    End Sub

    Public Sub SegnaLettoNellaCacheElenco(chiaveAccount As String, nomeCartella As String, identificativo As String, letto As Boolean)
        Try
            Dim numero As UInteger = 0
            If Not UInteger.TryParse(If(identificativo, "").Trim(), numero) Then Return
            PreparaTabelleCacheElenco()
            _db.EseguiNonQuery("UPDATE " & NomeTabellaCacheElenco & " SET Letto=@letto" &
                               " WHERE ChiaveAccount=@account AND Cartella=@cartella AND Uid=@uid;",
                               New Dictionary(Of String, Object) From {
                                   {"@letto", If(letto, 1, 0)},
                                   {"@account", If(chiaveAccount, "")},
                                   {"@cartella", If(nomeCartella, "")},
                                   {"@uid", Convert.ToInt64(numero)}
                               })
        Catch ex As Exception
            ScriviLog("CLSMail.SegnaLettoNellaCacheElenco: " & ex.Message)
        End Try
    End Sub

    Private Function ValiditaCacheCartella(chiaveAccount As String, nomeCartella As String) As Long
        Try
            PreparaTabelleCacheElenco()
            Dim valore As Object = _db.EseguiScalare("SELECT UidValidity FROM " & NomeTabellaCacheStato &
                                                     " WHERE ChiaveAccount=@account AND Cartella=@cartella;",
                                                     New Dictionary(Of String, Object) From {
                                                         {"@account", If(chiaveAccount, "")},
                                                         {"@cartella", If(nomeCartella, "")}
                                                     })
            If valore Is Nothing OrElse valore Is DBNull.Value Then Return -1
            Return Convert.ToInt64(valore)
        Catch ex As Exception
            ScriviLog("CLSMail.ValiditaCacheCartella: " & ex.Message)
            Return -1
        End Try
    End Function

    Private Sub ScriviStatoCacheCartella(chiaveAccount As String, nomeCartella As String, validita As Long, totale As Integer)
        Try
            PreparaTabelleCacheElenco()
            _db.EseguiNonQuery("INSERT OR REPLACE INTO " & NomeTabellaCacheStato &
                               " (ChiaveAccount, Cartella, UidValidity, Totale, Aggiornato)" &
                               " VALUES (@account, @cartella, @validita, @totale, @aggiornato);",
                               New Dictionary(Of String, Object) From {
                                   {"@account", If(chiaveAccount, "")},
                                   {"@cartella", If(nomeCartella, "")},
                                   {"@validita", validita},
                                   {"@totale", totale},
                                   {"@aggiornato", DateTime.UtcNow.ToString("o", CultureInfo.InvariantCulture)}
                               })
        Catch ex As Exception
            ScriviLog("CLSMail.ScriviStatoCacheCartella: " & ex.Message)
        End Try
    End Sub

    ' Confronta la UIDVALIDITY dichiarata dal server con quella dell'ultima volta: se il server
    ' l'ha cambiata gli identificativi di prima non valgono piu' e la copia locale va buttata.
    Private Sub VerificaValiditaCacheCartella(chiaveAccount As String, nomeCartella As String, cartella As IMailFolder)
        Try
            If cartella Is Nothing Then Return
            Dim validitaServer As Long = Convert.ToInt64(cartella.UidValidity)
            Dim validitaLocale As Long = ValiditaCacheCartella(chiaveAccount, nomeCartella)
            If validitaLocale >= 0 AndAlso validitaLocale <> validitaServer Then
                SvuotaCacheElenco(chiaveAccount, nomeCartella)
            End If
            ScriviStatoCacheCartella(chiaveAccount, nomeCartella, validitaServer, cartella.Count)
        Catch ex As Exception
            ScriviLog("CLSMail.VerificaValiditaCacheCartella: " & ex.Message)
        End Try
    End Sub

    ' Salva nella copia locale le intestazioni appena lette dal server. Non serve a leggere i
    ' messaggi offline: serve a far comparire l'elenco all'istante la volta dopo, mentre il
    ' server viene interrogato in sottofondo.
    Private Sub SalvaRiepiloghiInCache(chiaveAccount As String, nomeCartella As String, riepiloghi As List(Of RiepilogoMessaggio))
        Try
            If riepiloghi Is Nothing OrElse riepiloghi.Count = 0 Then Return
            PreparaTabelleCacheElenco()

            Dim indice As Integer = 0
            While indice < riepiloghi.Count
                Try
                    Dim quanti As Integer = Math.Min(RighePerInserimento, riepiloghi.Count - indice)
                    Dim pezzi As New List(Of String)
                    Dim parametri As New Dictionary(Of String, Object)
                    Dim scritte As Integer = 0

                    For posizione As Integer = indice To indice + quanti - 1
                        Dim voce As RiepilogoMessaggio = riepiloghi(posizione)
                        If voce Is Nothing Then Continue For
                        Dim numero As UInteger = 0
                        If Not UInteger.TryParse(If(voce.Id, "").Trim(), numero) Then Continue For

                        Dim suffisso As String = scritte.ToString(CultureInfo.InvariantCulture)
                        pezzi.Add("(@a" & suffisso & ", @c" & suffisso & ", @u" & suffisso & ", @o" & suffisso & ", @m" & suffisso & ", @d" & suffisso & ", @du" & suffisso & ", @l" & suffisso & ")")
                        parametri.Add("@a" & suffisso, If(chiaveAccount, ""))
                        parametri.Add("@c" & suffisso, If(nomeCartella, ""))
                        parametri.Add("@u" & suffisso, Convert.ToInt64(numero))
                        parametri.Add("@o" & suffisso, If(voce.Oggetto, ""))
                        parametri.Add("@m" & suffisso, If(voce.Mittente, ""))
                        parametri.Add("@d" & suffisso, voce.Data.ToString("o", CultureInfo.InvariantCulture))
                        parametri.Add("@du" & suffisso, voce.Data.UtcDateTime.ToString("o", CultureInfo.InvariantCulture))
                        parametri.Add("@l" & suffisso, If(voce.Letto, 1, 0))
                        scritte += 1
                    Next

                    If scritte > 0 Then
                        _db.EseguiNonQuery("INSERT OR REPLACE INTO " & NomeTabellaCacheElenco &
                                           " (ChiaveAccount, Cartella, Uid, Oggetto, Mittente, Data, DataUtc, Letto) VALUES " &
                                           String.Join(", ", pezzi.ToArray()) & ";", parametri)
                    End If

                    indice += quanti
                Catch exBlocco As Exception
                    ScriviLog("CLSMail.SalvaRiepiloghiInCache blocco: " & exBlocco.Message)
                    indice += RighePerInserimento
                End Try
            End While

            PotaCacheCartella(chiaveAccount, nomeCartella)
        Catch ex As Exception
            ScriviLog("CLSMail.SalvaRiepiloghiInCache: " & ex.ToString())
        End Try
    End Sub

    ' La copia locale non deve crescere all'infinito: si tengono le intestazioni piu' recenti,
    ' che sono quelle che si aprono davvero. "Piu' recenti" e' per data (DataUtc), non per Uid:
    ' un Uid piu' alto di solito corrisponde a un messaggio arrivato dopo, ma non sempre (un
    ' messaggio spostato o importato in cartella puo' ricevere un Uid alto pur avendo una data
    ' vecchia), quindi potare per Uid rischiava di buttare via proprio le intestazioni piu'
    ' recenti in quei casi. A parita' di data, l'Uid resta un criterio di riserva per stabilita'.
    Private Sub PotaCacheCartella(chiaveAccount As String, nomeCartella As String)
        Try
            _db.EseguiNonQuery("DELETE FROM " & NomeTabellaCacheElenco &
                               " WHERE ChiaveAccount=@account AND Cartella=@cartella AND Uid NOT IN (" &
                               "SELECT Uid FROM " & NomeTabellaCacheElenco &
                               " WHERE ChiaveAccount=@account AND Cartella=@cartella ORDER BY DataUtc DESC, Uid DESC LIMIT @limite);",
                               New Dictionary(Of String, Object) From {
                                   {"@account", If(chiaveAccount, "")},
                                   {"@cartella", If(nomeCartella, "")},
                                   {"@limite", MassimoRiepiloghiPerCartella}
                               })
        Catch ex As Exception
            ScriviLog("CLSMail.PotaCacheCartella: " & ex.Message)
        End Try
    End Sub

    ' Restituisce senza toccare la rete la pagina richiesta, presa dalla copia locale. Se la
    ' copia non c'e' ancora restituisce una pagina vuota e chi chiama passa direttamente al
    ' server.
    ' soloNonLetti=True restituisce solo le intestazioni ancora segnate come non lette nella
    ' copia locale (sfrutta idx_cache_elenco_non_letti, quindi resta veloce anche su cartelle
    ' piene): in questo caso Totale e' il numero di non letti trovati in cache, non il totale
    ' della cartella, per coerenza con OttieniPaginaMessaggi lato server quando soloNonLetti=True.
    Public Function OttieniPaginaDallaCache(chiaveAccount As String, nomeCartella As String,
                                            indiceInizio As Integer, quantita As Integer,
                                            Optional soloNonLetti As Boolean = False) As PaginaMessaggi
        Dim pagina As New PaginaMessaggi()
        Try
            If indiceInizio < 0 Then indiceInizio = 0
            If quantita <= 0 Then quantita = 75
            PreparaTabelleCacheElenco()

            Dim parametriBase As New Dictionary(Of String, Object) From {
                {"@account", If(chiaveAccount, "")},
                {"@cartella", If(nomeCartella, "")}
            }

            Dim filtroLetto As String = ""
            If soloNonLetti Then filtroLetto = " AND Letto=0"

            Dim conservate As Object = _db.EseguiScalare("SELECT COUNT(*) FROM " & NomeTabellaCacheElenco &
                                                         " WHERE ChiaveAccount=@account AND Cartella=@cartella" & filtroLetto & ";", parametriBase)

            Dim quanteInLocale As Integer = 0
            If conservate IsNot Nothing AndAlso conservate IsNot DBNull.Value Then quanteInLocale = Convert.ToInt32(conservate)
            If quanteInLocale = 0 Then Return pagina

            pagina.Totale = quanteInLocale
            If Not soloNonLetti Then
                ' Il totale dichiarato dal server (puo' essere piu' alto di quanto tenuto in
                ' cache, che e' limitata a MassimoRiepiloghiPerCartella) non ha senso quando si
                ' stanno contando solo i non letti: qui "Totale" deve restare il conteggio dei
                ' non letti trovati, non quello dell'intera cartella.
                Dim totaleNoto As Object = _db.EseguiScalare("SELECT Totale FROM " & NomeTabellaCacheStato &
                                                             " WHERE ChiaveAccount=@account AND Cartella=@cartella;", parametriBase)
                If totaleNoto IsNot Nothing AndAlso totaleNoto IsNot DBNull.Value Then
                    Dim dichiarato As Integer = Convert.ToInt32(totaleNoto)
                    If dichiarato > pagina.Totale Then pagina.Totale = dichiarato
                End If
            End If

            ' Ordine per data effettiva (DataUtc, indicizzata), non per Uid: e' l'ordine che
            ' l'utente vede davvero, e regge anche i casi in cui Uid e data non vanno di pari
            ' passo (messaggi spostati/importati). A parita' di data, l'Uid e' solo un criterio
            ' di riserva per rendere stabile l'ordine.
            Dim righe As DataTable = _db.EseguiQuery("SELECT Uid, Oggetto, Mittente, Data, Letto FROM " & NomeTabellaCacheElenco &
                                                     " WHERE ChiaveAccount=@account AND Cartella=@cartella" & filtroLetto &
                                                     " ORDER BY DataUtc DESC, Uid DESC LIMIT @quantita OFFSET @salta;",
                                                     New Dictionary(Of String, Object) From {
                                                         {"@account", If(chiaveAccount, "")},
                                                         {"@cartella", If(nomeCartella, "")},
                                                         {"@quantita", quantita},
                                                         {"@salta", indiceInizio}
                                                     })

            For Each riga As DataRow In righe.Rows
                Try
                    Dim data As DateTimeOffset = DateTimeOffset.MinValue
                    Try
                        Dim testoData As String = If(riga("Data") Is DBNull.Value, "", riga("Data").ToString())
                        If Not String.IsNullOrWhiteSpace(testoData) Then
                            DateTimeOffset.TryParse(testoData, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, data)
                        End If
                    Catch exData As Exception
                        ScriviLog("CLSMail.OttieniPaginaDallaCache data: " & exData.Message)
                    End Try

                    pagina.Messaggi.Add(New RiepilogoMessaggio() With {
                        .ChiaveAccount = chiaveAccount,
                        .Id = Convert.ToInt64(riga("Uid")).ToString(CultureInfo.InvariantCulture),
                        .Oggetto = If(riga("Oggetto") Is DBNull.Value, "", riga("Oggetto").ToString()),
                        .Mittente = If(riga("Mittente") Is DBNull.Value, "", riga("Mittente").ToString()),
                        .Data = data,
                        .Letto = Not (riga("Letto") Is DBNull.Value) AndAlso Convert.ToInt32(riga("Letto")) = 1
                    })
                Catch exRiga As Exception
                    ScriviLog("CLSMail.OttieniPaginaDallaCache riga: " & exRiga.Message)
                End Try
            Next
        Catch ex As Exception
            ScriviLog("CLSMail.OttieniPaginaDallaCache (" & If(chiaveAccount, "") & ", " & If(nomeCartella, "") & "): " & ex.ToString())
        End Try
        Return pagina
    End Function

    ' ============================================================================================
    ' RICERCHE RECENTI
    ' ============================================================================================

    Private Const NomeTabellaRicercheRecentiMail As String = "ricerche_recenti_mail"
    Private Const MassimoRicercheRecenti As Integer = 25

    Private Sub PreparaTabellaRicercheRecenti()
        Try
            _db.EseguiNonQuery("CREATE TABLE IF NOT EXISTS " & NomeTabellaRicercheRecentiMail &
                               " (Termine TEXT PRIMARY KEY, Data TEXT);")
        Catch ex As Exception
            ScriviLog("CLSMail.PreparaTabellaRicercheRecenti: " & ex.ToString())
        End Try
    End Sub

    Public Sub SalvaRicercaRecente(testo As String)
        Try
            Dim termine As String = If(testo, "").Trim()
            If String.IsNullOrWhiteSpace(termine) Then Return
            If termine.Length > 200 Then termine = termine.Substring(0, 200)
            PreparaTabellaRicercheRecenti()

            _db.EseguiNonQuery("INSERT OR REPLACE INTO " & NomeTabellaRicercheRecentiMail &
                               " (Termine, Data) VALUES (@termine, @data);",
                               New Dictionary(Of String, Object) From {
                                   {"@termine", termine},
                                   {"@data", DateTime.UtcNow.ToString("o", CultureInfo.InvariantCulture)}
                               })

            _db.EseguiNonQuery("DELETE FROM " & NomeTabellaRicercheRecentiMail &
                               " WHERE Termine NOT IN (SELECT Termine FROM " & NomeTabellaRicercheRecentiMail &
                               " ORDER BY Data DESC LIMIT @limite);",
                               New Dictionary(Of String, Object) From {{"@limite", MassimoRicercheRecenti}})
        Catch ex As Exception
            ScriviLog("CLSMail.SalvaRicercaRecente: " & ex.ToString())
        End Try
    End Sub

    Public Function OttieniRicercheRecenti() As List(Of String)
        Dim elenco As New List(Of String)
        Try
            PreparaTabellaRicercheRecenti()
            Dim righe As DataTable = _db.EseguiQuery("SELECT Termine FROM " & NomeTabellaRicercheRecentiMail &
                                                     " ORDER BY Data DESC LIMIT @limite;",
                                                     New Dictionary(Of String, Object) From {{"@limite", MassimoRicercheRecenti}})
            For Each riga As DataRow In righe.Rows
                Try
                    Dim termine As String = If(riga("Termine") Is DBNull.Value, "", riga("Termine").ToString())
                    If Not String.IsNullOrWhiteSpace(termine) Then elenco.Add(termine)
                Catch exRiga As Exception
                    ScriviLog("CLSMail.OttieniRicercheRecenti riga: " & exRiga.Message)
                End Try
            Next
        Catch ex As Exception
            ScriviLog("CLSMail.OttieniRicercheRecenti: " & ex.ToString())
        End Try
        Return elenco
    End Function

    Public Sub SvuotaRicercheRecenti()
        Try
            PreparaTabellaRicercheRecenti()
            _db.EseguiNonQuery("DELETE FROM " & NomeTabellaRicercheRecentiMail & ";")
        Catch ex As Exception
            ScriviLog("CLSMail.SvuotaRicercheRecenti: " & ex.ToString())
        End Try
    End Sub

End Class