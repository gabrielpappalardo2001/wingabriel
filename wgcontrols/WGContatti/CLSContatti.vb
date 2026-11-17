Imports System
Imports System.Collections.Generic
Imports System.Data
Imports System.IO
Imports System.Linq
Imports System.Text
Imports System.Windows.Forms
Imports WGDb
Imports wgcontrols

' Gestione del database dei contatti, delle rubriche indipendenti per account e delle categorie.
Partial Public Class CLSContatti
    Implements IDisposable

    Private Const NomeTabellaContatti As String = "contatti"
    Private Const NomeTabellaCategorie As String = "categorie"
    Private Const NomeTabellaEliminati As String = "contatti_eliminati"
    Private Const NomeTabellaRicercheRecenti As String = "ricerche_recenti"
    ' Rubriche indipendenti: ogni account ha almeno una rubrica "predefinita" (creata in
    ' automatico al primo utilizzo) e puo' averne altre create manualmente dall'utente
    ' (es. "Lavoro", "Famiglia"). ChiaveAccount lega la rubrica al suo account; Predefinita
    ' individua quella usata quando si importa/sincronizza senza indicare una rubrica precisa.
    Private Const NomeTabellaRubriche As String = "rubriche"
    Private Const NomeTabellaRecapiti As String = "contatti_recapiti"
    Private Const ChiaveConfigSincronizzaEliminazioni As String = "RubricaSincronizzaEliminazioni"
    Private Const MassimoRicercheRecenti As Integer = 10

    Public Const CategoriaRecapitoTelefono As String = "Telefono"
    Public Const CategoriaRecapitoCellulare As String = "Cellulare"
    Public Const CategoriaRecapitoEmail As String = "Email"
    Public Const CategoriaRecapitoIndirizzo As String = "Indirizzo"
    Public Const CategoriaRecapitoSito As String = "Sito"

    Private ReadOnly _db As WGDatabase
    Private _smaltito As Boolean = False
    Private _ultimoErrore As String = ""
    Private _accountPredefinito As String = ""

    Public ReadOnly Property UltimoErrore As String
        Get
            Return If(_ultimoErrore, "")
        End Get
    End Property

    Public Property AccountPredefinitoSincronizzazione As String
        Get
            Return If(_accountPredefinito, "")
        End Get
        Set(value As String)
            _accountPredefinito = If(value, "")
        End Set
    End Property

    Public Sub New()
        Try
            _db = New WGDatabase("rubrica", "wg-rubrica.sqlite")
            CreaTabelle()
            AggiornaSchemaSeNecessario()
            RiparaRubriche()
            PreparaSincronizzazioneFile()
        Catch ex As Exception
            ImpostaErrore("CLSContatti.New", ex)
        End Try
    End Sub

    Private Sub CreaTabelle()
        Try
            _db.CreaTabellaSeNonEsiste(NomeTabellaContatti,
                "Nome TEXT, Cognome TEXT, Email TEXT, Telefono TEXT, Cellulare TEXT, " &
                "Azienda TEXT, ChiaveCategoria TEXT, Note TEXT, UidServer TEXT, UrlServer TEXT, " &
                "ChiaveAccountOrigine TEXT, Preferito INTEGER DEFAULT 0, DaSincronizzare INTEGER DEFAULT 0, Compleanno TEXT")
            _db.CreaTabellaSeNonEsiste(NomeTabellaCategorie, "Nome TEXT, Colore TEXT")
            _db.CreaTabellaSeNonEsiste(NomeTabellaEliminati,
                "UidServer TEXT, UrlServer TEXT, ChiaveAccount TEXT, DataEliminazione TEXT")
            _db.CreaTabellaSeNonEsiste(NomeTabellaRicercheRecenti, "Termine TEXT, Data TEXT")
            _db.CreaTabellaSeNonEsiste(NomeTabellaRubriche, "ChiaveAccount TEXT, Nome TEXT, Predefinita INTEGER DEFAULT 0")
            _db.CreaTabellaSeNonEsiste(NomeTabellaRecapiti, "ChiaveContatto TEXT, Categoria TEXT, Valore TEXT, Ordine INTEGER DEFAULT 0")
        Catch ex As Exception
            ScriviLog("CLSContatti.CreaTabelle: " & ex.ToString())
        End Try
    End Sub

    Private Sub AggiornaSchemaSeNecessario()
        Try
            AggiungiColonnaSeManca(NomeTabellaContatti, "DaSincronizzare", "INTEGER DEFAULT 0")
            AggiungiColonnaSeManca(NomeTabellaContatti, "UrlServer", "TEXT")
            AggiungiColonnaSeManca(NomeTabellaContatti, "UidServer", "TEXT")
            AggiungiColonnaSeManca(NomeTabellaContatti, "ChiaveAccountOrigine", "TEXT")
            AggiungiColonnaSeManca(NomeTabellaContatti, "ChiaveRubrica", "TEXT")
            AggiungiColonnaSeManca(NomeTabellaContatti, "Compleanno", "TEXT")
            AggiungiColonnaSeManca(NomeTabellaRubriche, "Protetta", "INTEGER DEFAULT 0")
            AggiungiColonnaSeManca(NomeTabellaRubriche, "SalePassword", "TEXT")
            AggiungiColonnaSeManca(NomeTabellaRubriche, "ImprontaPassword", "TEXT")
            AggiungiColonnaSeManca(NomeTabellaRubriche, "ChiavePubblica", "TEXT")
            AggiungiColonnaSeManca(NomeTabellaRubriche, "ChiavePrivataCifrata", "TEXT")
            BackfillRubrichePerContattiEsistenti()
        Catch ex As Exception
            ScriviLog("CLSContatti.AggiornaSchemaSeNecessario: " & ex.ToString())
        End Try
    End Sub

    ''' <summary>
    ''' Migrazione una-tantum: i contatti creati prima dell'introduzione delle rubriche
    ''' multiple hanno ChiaveAccountOrigine valorizzato ma ChiaveRubrica vuoto. Li assegna
    ''' alla rubrica predefinita del loro account (creandola se non esiste ancora), cosi'
    ''' ogni account continua ad avere "la sua" rubrica anche per i dati gia' presenti.
    ''' </summary>
    Private Sub BackfillRubrichePerContattiEsistenti()
        Try
            ' Vengono sistemati anche i contatti senza account, che prima restavano fuori da
            ' ogni rubrica e sparivano quando si apriva una rubrica per vederne il contenuto.
            ' Quelli finiscono nella rubrica locale predefinita.
            Dim daSistemare As DataTable = _db.EseguiQuery(
                "SELECT ChiaveWG, ChiaveAccountOrigine FROM " & NomeTabellaContatti &
                " WHERE ChiaveRubrica IS NULL OR ChiaveRubrica = '';", Nothing)
            If daSistemare Is Nothing Then Return
            For Each riga As DataRow In daSistemare.Rows
                Try
                    Dim account As String = ""
                    If Not riga.IsNull("ChiaveAccountOrigine") Then account = riga("ChiaveAccountOrigine").ToString().Trim()
                    Dim chiaveRubrica As String = OttieniORCreaRubricaPredefinita(account)
                    If String.IsNullOrWhiteSpace(chiaveRubrica) Then Continue For
                    _db.AggiornaRecord(NomeTabellaContatti, riga("ChiaveWG").ToString(),
                        New Dictionary(Of String, Object) From {{"ChiaveRubrica", chiaveRubrica}})
                Catch exRiga As Exception
                    ScriviLog("CLSContatti.BackfillRubrichePerContattiEsistenti riga: " & exRiga.ToString())
                End Try
            Next
        Catch ex As Exception
            ScriviLog("CLSContatti.BackfillRubrichePerContattiEsistenti: " & ex.ToString())
        End Try
    End Sub

    ' ============================================================================================
    ' RUBRICHE INDIPENDENTI PER ACCOUNT
    ' ============================================================================================
    ' Ogni account di posta ha una propria rubrica predefinita (creata automaticamente al primo
    ' utilizzo) e puo' averne altre create dall'utente. In questo modo i contatti di account
    ' diversi restano separati e gestibili in modo indipendente, pur restando nello stesso
    ' database fisico (wg-rubrica.sqlite) per semplicita' di manutenzione.

    ''' <summary>
    ''' Ritorna la chiave della rubrica predefinita dell'account indicato, creandola al volo se
    ''' l'account non ne ha ancora una. Ritorna stringa vuota se chiaveAccount non e' valorizzato.
    ''' </summary>
    ' Con chiaveAccount vuota lavora sulle rubriche locali, quelle che non appartengono ad
    ' alcun account e non vengono mai sincronizzate.
    ' Un solo lock per tutte le istanze: evita che la sincronizzazione in background e
    ' l'interfaccia creino nello stesso momento due rubriche predefinite per lo stesso account.
    Private Shared ReadOnly _lockRubriche As New Object()

    ' Rubriche di UN account. Con chiaveAccount vuota ritorna SOLO quelle locali
    ' (OttieniRubriche con account vuoto, invece, ritorna tutte le rubriche).
    ' Ritorna Nothing se la lettura fallisce, cosi' chi chiama non scambia un errore per "nessuna rubrica".
    Private Function RubricheDiAccount(chiaveAccount As String) As DataTable
        Try
            Return _db.EseguiQuery(
                "SELECT * FROM " & NomeTabellaRubriche & " WHERE IFNULL(ChiaveAccount, '') = @account ORDER BY Predefinita DESC, Nome ASC;",
                New Dictionary(Of String, Object) From {{"@account", If(chiaveAccount, "").Trim()}})
        Catch ex As Exception
            ScriviLog("CLSContatti.RubricheDiAccount: " & ex.ToString())
            Return Nothing
        End Try
    End Function

    Private Function RubricaHaContatti(chiaveRubrica As String) As Boolean
        Try
            ' EseguiQuery, se il database e' occupato, restituisce una tabella vuota e non Nothing:
            ' leggerla come "zero contatti" farebbe eliminare una rubrica protetta e spostare
            ' contatti cifrati, che da quel momento non si potrebbero piu' leggere.
            ' EseguiScalare invece restituisce Nothing quando la lettura fallisce.
            Dim n As Object = _db.EseguiScalare(
                "SELECT COUNT(1) FROM " & NomeTabellaContatti & " WHERE ChiaveRubrica = @rubrica;",
                New Dictionary(Of String, Object) From {{"@rubrica", chiaveRubrica}})
            If n Is Nothing OrElse n Is DBNull.Value Then Return True
            Return Convert.ToInt32(n) > 0
        Catch ex As Exception
            ScriviLog("CLSContatti.RubricaHaContatti: " & ex.ToString())
            Return True ' nel dubbio si assume che ne contenga
        End Try
    End Function

    Public Function OttieniORCreaRubricaPredefinita(chiaveAccount As String) As String
        Try
            If chiaveAccount Is Nothing Then chiaveAccount = ""
            chiaveAccount = chiaveAccount.Trim()
            SyncLock _lockRubriche
                Dim esistenti As DataTable = _db.EseguiQuery(
                    "SELECT ChiaveWG FROM " & NomeTabellaRubriche & " WHERE IFNULL(ChiaveAccount, '') = @account AND Predefinita = 1 ORDER BY ChiaveWG LIMIT 1;",
                    New Dictionary(Of String, Object) From {{"@account", chiaveAccount}})
                If esistenti IsNot Nothing AndAlso esistenti.Rows.Count > 0 Then Return esistenti.Rows(0)("ChiaveWG").ToString()

                ' Nessuna rubrica predefinita: se l'account ha gia' altre rubriche (senza il flag,
                ' caso anomalo) ne promuove la prima, altrimenti ne crea una nuova.
                Dim altre As DataTable = _db.EseguiQuery(
                    "SELECT ChiaveWG FROM " & NomeTabellaRubriche & " WHERE IFNULL(ChiaveAccount, '') = @account ORDER BY ChiaveWG LIMIT 1;",
                    New Dictionary(Of String, Object) From {{"@account", chiaveAccount}})
                If altre IsNot Nothing AndAlso altre.Rows.Count > 0 Then
                    Dim chiaveEsistente As String = altre.Rows(0)("ChiaveWG").ToString()
                    _db.AggiornaRecord(NomeTabellaRubriche, chiaveEsistente, New Dictionary(Of String, Object) From {{"Predefinita", 1}})
                    Return chiaveEsistente
                End If

                Return CreaRubrica(chiaveAccount, If(chiaveAccount.Length = 0, "Rubrica personale", "Rubrica principale"), True)
            End SyncLock
        Catch ex As Exception
            ImpostaErrore("CLSContatti.OttieniORCreaRubricaPredefinita (" & If(chiaveAccount, "") & ")", ex)
            Return ""
        End Try
    End Function

    ''' <summary>
    ''' Crea una nuova rubrica indipendente per l'account indicato (es. "Lavoro", "Famiglia").
    ''' Se predefinita=True, diventa quella usata per le sincronizzazioni/importazioni che non
    ''' specificano una rubrica precisa, e toglie il flag alle altre rubriche dello stesso account.
    ''' In caso di errore ritorna "" e il motivo e' in UltimoErrore (e nel log).
    ''' </summary>
    Public Function CreaRubrica(chiaveAccount As String, nome As String, Optional predefinita As Boolean = False) As String
        Dim chiave As String = ""
        Try
            If String.IsNullOrWhiteSpace(nome) Then
                _ultimoErrore = "Indicare un nome per la rubrica."
                Return ""
            End If
            Dim account As String = If(chiaveAccount, "").Trim()
            Dim nomePulito As String = nome.Trim()

            SyncLock _lockRubriche
                Dim dellAccount As DataTable = RubricheDiAccount(account)
                If dellAccount Is Nothing Then
                    _ultimoErrore = "Impossibile leggere l'elenco delle rubriche esistenti."
                    Return ""
                End If

                For Each r As DataRow In dellAccount.Rows
                    If String.Equals(ValoreColonna(r, "Nome").Trim(), nomePulito, StringComparison.OrdinalIgnoreCase) Then
                        _ultimoErrore = "Esiste gia' una rubrica chiamata '" & nomePulito & "'."
                        Return ""
                    End If
                Next

                ' La prima rubrica di un account e' sempre quella predefinita.
                If dellAccount.Rows.Count = 0 Then predefinita = True

                ' Il flag si toglie alle altre PRIMA di inserire: cosi' non esistono mai due
                ' predefinite, nemmeno se qualcosa si interrompe a meta'.
                If predefinita AndAlso dellAccount.Rows.Count > 0 Then
                    _db.EseguiNonQuery("UPDATE " & NomeTabellaRubriche & " SET Predefinita = 0 WHERE IFNULL(ChiaveAccount, '') = @account;",
                        New Dictionary(Of String, Object) From {{"@account", account}})
                End If

                chiave = _db.InserisciRecord(NomeTabellaRubriche, New Dictionary(Of String, Object) From {
                    {"ChiaveAccount", account},
                    {"Nome", nomePulito},
                    {"Predefinita", If(predefinita, 1, 0)}
                })
                If String.IsNullOrWhiteSpace(chiave) Then
                    _ultimoErrore = "Il salvataggio della rubrica non e' riuscito."
                    ScriviLog("CLSContatti.CreaRubrica: InserisciRecord ha restituito una chiave vuota (" & nomePulito & ")")
                    ScriviLog(Diagnostica())
                    chiave = ""
                End If
            End SyncLock
            Return chiave
        Catch ex As Exception
            ImpostaErrore("CLSContatti.CreaRubrica (" & If(nome, "") & ")", ex)
            ScriviLog(Diagnostica())
            Return ""
        End Try
    End Function

    ''' <summary>
    ''' Elenco delle rubriche esistenti, opzionalmente limitato a quelle di un account: cosi'
    ''' ogni account puo' presentare ed gestire solo le proprie, in modo indipendente dagli altri.
    ''' </summary>
    Public Function OttieniRubriche(Optional chiaveAccount As String = "") As DataTable
        Try
            If String.IsNullOrWhiteSpace(chiaveAccount) Then
                Return _db.OttieniTuttiIRecord(NomeTabellaRubriche, "Nome ASC")
            End If
            Return _db.EseguiQuery(
                "SELECT * FROM " & NomeTabellaRubriche & " WHERE ChiaveAccount = @account ORDER BY Predefinita DESC, Nome ASC;",
                New Dictionary(Of String, Object) From {{"@account", chiaveAccount}})
        Catch ex As Exception
            ImpostaErrore("CLSContatti.OttieniRubriche (" & If(chiaveAccount, "") & ")", ex)
            Return New DataTable()
        End Try
    End Function

    Public Function NomeRubrica(chiaveRubrica As String) As String
        Try
            If String.IsNullOrWhiteSpace(chiaveRubrica) Then Return ""
            Dim riga As DataRow = _db.OttieniRecord(NomeTabellaRubriche, chiaveRubrica)
            If riga Is Nothing Then Return ""
            Return riga("Nome").ToString()
        Catch ex As Exception
            ImpostaErrore("CLSContatti.NomeRubrica (" & chiaveRubrica & ")", ex)
            Return ""
        End Try
    End Function

    Public Function RinominaRubrica(chiaveRubrica As String, nuovoNome As String) As Boolean
        Try
            If String.IsNullOrWhiteSpace(chiaveRubrica) OrElse String.IsNullOrWhiteSpace(nuovoNome) Then Return False
            Return _db.AggiornaRecord(NomeTabellaRubriche, chiaveRubrica, New Dictionary(Of String, Object) From {{"Nome", nuovoNome.Trim()}})
        Catch ex As Exception
            ImpostaErrore("CLSContatti.RinominaRubrica (" & chiaveRubrica & ")", ex)
            Return False
        End Try
    End Function

    ''' <summary>
    ''' Rende predefinita la rubrica indicata per il suo account, togliendo il flag alle altre
    ''' rubriche dello stesso account (una sola predefinita per account).
    ''' </summary>
    Public Function ImpostaRubricaPredefinita(chiaveRubrica As String) As Boolean
        Try
            If String.IsNullOrWhiteSpace(chiaveRubrica) Then Return False
            Dim riga As DataRow = _db.OttieniRecord(NomeTabellaRubriche, chiaveRubrica)
            If riga Is Nothing Then Return False
            Dim account As String = riga("ChiaveAccount").ToString()
            _db.EseguiNonQuery("UPDATE " & NomeTabellaRubriche & " SET Predefinita = 0 WHERE IFNULL(ChiaveAccount, '') = @account;",
                New Dictionary(Of String, Object) From {{"@account", account}})
            Return _db.AggiornaRecord(NomeTabellaRubriche, chiaveRubrica, New Dictionary(Of String, Object) From {{"Predefinita", 1}})
        Catch ex As Exception
            ImpostaErrore("CLSContatti.ImpostaRubricaPredefinita (" & chiaveRubrica & ")", ex)
            Return False
        End Try
    End Function

    ''' <summary>
    ''' Rimuove tutte le rubriche di un account che non puo' (piu') averne una propria, ad
    ''' esempio perche' e' un account Google e la sincronizzazione dei contatti non e' possibile
    ''' per quel provider: sposta i contatti eventualmente presenti nella rubrica locale
    ''' predefinita (quella senza account) ed elimina le rubriche ormai orfane dell'account.
    ''' Se l'account non ha nessuna rubrica non fa nulla.
    ''' </summary>
    Public Function RimuoviRubricaAccount(chiaveAccount As String) As Boolean
        Try
            If String.IsNullOrWhiteSpace(chiaveAccount) Then Return True
            Dim rubriche As DataTable = OttieniRubriche(chiaveAccount)
            If rubriche Is Nothing OrElse rubriche.Rows.Count = 0 Then Return True

            Dim rubricaLocale As String = OttieniORCreaRubricaPredefinita("")
            For Each riga As DataRow In rubriche.Rows
                Try
                    Dim chiaveRubrica As String = riga("ChiaveWG").ToString()
                    _db.EseguiNonQuery("UPDATE " & NomeTabellaContatti & " SET ChiaveRubrica = @nuova, ChiaveAccountOrigine = '' WHERE ChiaveRubrica = @vecchia;",
                        New Dictionary(Of String, Object) From {{"@nuova", rubricaLocale}, {"@vecchia", chiaveRubrica}})
                    _db.EliminaRecord(NomeTabellaRubriche, chiaveRubrica)
                Catch exRiga As Exception
                    ScriviLog("CLSContatti.RimuoviRubricaAccount riga: " & exRiga.ToString())
                End Try
            Next
            Return True
        Catch ex As Exception
            ImpostaErrore("CLSContatti.RimuoviRubricaAccount (" & If(chiaveAccount, "") & ")", ex)
            Return False
        End Try
    End Function

    ''' <summary>
    ''' Vero se la rubrica indicata e' l'unica di un account (non locale): serve a chi chiama
    ''' per avvisare l'utente prima di eliminarla, dato che i suoi contatti finiranno sulla
    ''' rubrica locale e l'account restera' senza una rubrica propria.
    ''' </summary>
    Public Function EUnicaRubricaAccount(chiaveRubrica As String) As Boolean
        Try
            If String.IsNullOrWhiteSpace(chiaveRubrica) Then Return False
            Dim riga As DataRow = _db.OttieniRecord(NomeTabellaRubriche, chiaveRubrica)
            If riga Is Nothing Then Return False
            Dim account As String = riga("ChiaveAccount").ToString()
            If String.IsNullOrWhiteSpace(account) Then Return False
            Dim tutte As DataTable = OttieniRubriche(account)
            Return tutte IsNot Nothing AndAlso tutte.Rows.Count <= 1
        Catch ex As Exception
            ScriviLog("CLSContatti.EUnicaRubricaAccount: " & ex.ToString())
            Return False
        End Try
    End Function

    ''' <summary>
    ''' Elimina una rubrica. Non elimina i contatti: li sposta su un'altra rubrica dello stesso
    ''' account se ce n'e' una, altrimenti (era l'unica rubrica di un account) sulla rubrica
    ''' locale predefinita, staccandoli dall'account che resta cosi' senza una rubrica propria
    ''' finche' non ne sincronizza una nuova. Resta vietato solo eliminare l'unica rubrica
    ''' locale (senza account): in quel caso non ci sarebbe nessun posto sensato dove spostare
    ''' i contatti.
    ''' </summary>
    Public Function EliminaRubrica(chiaveRubrica As String) As Boolean
        Try
            If String.IsNullOrWhiteSpace(chiaveRubrica) Then Return False
            Dim eliminata As Boolean = False

            SyncLock _lockRubriche
                Dim riga As DataRow = _db.OttieniRecord(NomeTabellaRubriche, chiaveRubrica)
                If riga Is Nothing Then Return False
                Dim account As String = ValoreColonna(riga, "ChiaveAccount").Trim()

                ' Solo le rubriche dello STESSO account (per una rubrica locale: solo le locali).
                ' Prima, con account vuoto, venivano contate anche le rubriche di tutti gli altri account.
                Dim tutteDellAccount As DataTable = RubricheDiAccount(account)
                If tutteDellAccount Is Nothing Then
                    _ultimoErrore = "Impossibile leggere l'elenco delle rubriche."
                    Return False
                End If
                Dim eUnicaDellAccount As Boolean = tutteDellAccount.Rows.Count <= 1

                If eUnicaDellAccount AndAlso account.Length = 0 Then
                    ImpostaErrore("CLSContatti.EliminaRubrica", New Exception("Non e' possibile eliminare l'unica rubrica locale."))
                    Return False
                End If

                ' I contatti di una rubrica protetta sono cifrati con la sua chiave: spostarli
                ' altrove li renderebbe illeggibili. Va prima svuotata.
                If RubricaProtetta(chiaveRubrica) AndAlso RubricaHaContatti(chiaveRubrica) Then
                    _ultimoErrore = "La rubrica e' protetta e contiene dei contatti: eliminali prima di eliminare la rubrica."
                    Return False
                End If

                ' Rubrica su cui spostare i contatti: un'altra NON protetta dello stesso account,
                ' altrimenti (era l'unica di un account) la rubrica locale predefinita.
                Dim chiaveDestinazione As String = ""
                If Not eUnicaDellAccount Then
                    For Each altraRiga As DataRow In tutteDellAccount.Rows
                        Dim chiaveAltra As String = ValoreColonna(altraRiga, "ChiaveWG")
                        If chiaveAltra.Length > 0 AndAlso chiaveAltra <> chiaveRubrica AndAlso Not RubricaProtetta(chiaveAltra) Then
                            chiaveDestinazione = chiaveAltra
                            Exit For
                        End If
                    Next
                Else
                    chiaveDestinazione = OttieniORCreaRubricaPredefinita("")
                End If

                If String.IsNullOrWhiteSpace(chiaveDestinazione) OrElse RubricaProtetta(chiaveDestinazione) Then
                    _ultimoErrore = "Non c'e' una rubrica non protetta su cui spostare i contatti."
                    Return False
                End If

                If eUnicaDellAccount Then
                    _db.EseguiNonQuery("UPDATE " & NomeTabellaContatti & " SET ChiaveRubrica = @nuova, ChiaveAccountOrigine = '' WHERE ChiaveRubrica = @vecchia;",
                        New Dictionary(Of String, Object) From {{"@nuova", chiaveDestinazione}, {"@vecchia", chiaveRubrica}})
                Else
                    _db.EseguiNonQuery("UPDATE " & NomeTabellaContatti & " SET ChiaveRubrica = @nuova WHERE ChiaveRubrica = @vecchia;",
                        New Dictionary(Of String, Object) From {{"@nuova", chiaveDestinazione}, {"@vecchia", chiaveRubrica}})
                End If

                eliminata = _db.EliminaRecord(NomeTabellaRubriche, chiaveRubrica)

                Dim eraPredefinita As Boolean = False
                Try
                    eraPredefinita = Convert.ToInt32(riga("Predefinita")) = 1
                Catch
                End Try
                If eliminata AndAlso eraPredefinita AndAlso Not eUnicaDellAccount Then ImpostaRubricaPredefinita(chiaveDestinazione)
            End SyncLock
            Return eliminata
        Catch ex As Exception
            ImpostaErrore("CLSContatti.EliminaRubrica (" & chiaveRubrica & ")", ex)
            Return False
        End Try
    End Function

    ''' <summary>
    ''' Determina quale rubrica usare per un nuovo contatto/importazione: quella indicata
    ''' esplicitamente se presente, altrimenti la predefinita dell'account indicato.
    ''' </summary>
    Private Function RubricaDaUsare(chiaveRubrica As String, chiaveAccount As String) As String
        Try
            If Not String.IsNullOrWhiteSpace(chiaveRubrica) Then Return chiaveRubrica.Trim()
            If Not String.IsNullOrWhiteSpace(chiaveAccount) Then Return OttieniORCreaRubricaPredefinita(chiaveAccount)
            Return ""
        Catch ex As Exception
            ScriviLog("CLSContatti.RubricaDaUsare: " & ex.ToString())
            Return ""
        End Try
    End Function

    Private Function ColonnaPresente(nomeTabella As String, nomeColonna As String) As Boolean
        Try
            If _db Is Nothing OrElse String.IsNullOrWhiteSpace(nomeTabella) OrElse String.IsNullOrWhiteSpace(nomeColonna) Then Return False
            Dim info As DataTable = _db.EseguiQuery("PRAGMA table_info(" & nomeTabella & ");", Nothing)
            If info Is Nothing Then Return False
            For Each riga As DataRow In info.Rows
                If String.Equals(riga("name").ToString(), nomeColonna, StringComparison.OrdinalIgnoreCase) Then Return True
            Next
            Return False
        Catch ex As Exception
            ScriviLog("CLSContatti.ColonnaPresente (" & nomeTabella & "." & nomeColonna & "): " & ex.ToString())
            Return False
        End Try
    End Function

    Private Sub AggiungiColonnaSeManca(nomeTabella As String, nomeColonna As String, definizione As String)
        Try
            If ColonnaPresente(nomeTabella, nomeColonna) Then Return
            _db.EseguiNonQuery("ALTER TABLE " & nomeTabella & " ADD COLUMN " & nomeColonna & " " & definizione & ";", Nothing)
        Catch ex As Exception
            ScriviLog("CLSContatti.AggiungiColonnaSeManca (" & nomeTabella & "." & nomeColonna & "): " & ex.ToString())
        End Try
    End Sub

    Private Shared ReadOnly _lockLog As New Object()

    Public Shared Sub ScriviLog(messaggio As String)
        Try
            SyncLock _lockLog
                Dim riga As String = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") & " - " & messaggio & Environment.NewLine
                ' Prova la cartella del programma e, se non e' scrivibile, quella dell'utente.
                For Each cartella As String In CartelleLog()
                    Try
                        If Not Directory.Exists(cartella) Then Directory.CreateDirectory(cartella)
                        File.AppendAllText(Path.Combine(cartella, "log.txt"), riga, New UTF8Encoding(False))
                        Return
                    Catch
                    End Try
                Next
            End SyncLock
        Catch
        End Try
    End Sub

    Private Sub ImpostaErrore(contesto As String, ex As Exception)
        Try
            _ultimoErrore = If(ex Is Nothing, "Errore sconosciuto.", ex.Message)
        Catch
            _ultimoErrore = "Errore sconosciuto."
        End Try
        ' Prima il contesto veniva ignorato e nel log non restava nessuna traccia.
        ScriviLog(If(contesto, "") & ": " & If(ex Is Nothing, "", ex.ToString()))
    End Sub

    Private Function ValoreColonna(riga As DataRow, nomeColonna As String) As String
        Try
            If riga Is Nothing Then Return ""
            If Not riga.Table.Columns.Contains(nomeColonna) Then Return ""
            If riga.IsNull(nomeColonna) Then Return ""
            Return riga(nomeColonna).ToString()
        Catch ex As Exception
            ScriviLog("CLSContatti.ValoreColonna (" & If(nomeColonna, "") & "): " & ex.ToString())
            Return ""
        End Try
    End Function

    Private Function AccountDaUsare(chiaveAccountSincronizzazione As String) As String
        Try
            If Not String.IsNullOrWhiteSpace(chiaveAccountSincronizzazione) Then Return chiaveAccountSincronizzazione.Trim()
            Return If(_accountPredefinito, "").Trim()
        Catch ex As Exception
            ScriviLog("CLSContatti.AccountDaUsare: " & ex.ToString())
            Return ""
        End Try
    End Function

    Private Function NormalizzaListaRecapiti(valori As List(Of String)) As List(Of String)
        Dim risultato As New List(Of String)
        Try
            If valori Is Nothing Then Return risultato
            For Each valore As String In valori
                Dim testo As String = If(valore, "").Trim()
                If testo.Length = 0 Then Continue For
                If Not risultato.Contains(testo, StringComparer.OrdinalIgnoreCase) Then risultato.Add(testo)
            Next
        Catch ex As Exception
            ScriviLog("CLSContatti.NormalizzaListaRecapiti: " & ex.ToString())
        End Try
        Return risultato
    End Function

    Private Function PrimoValoreLista(valori As List(Of String)) As String
        Try
            If valori Is Nothing OrElse valori.Count = 0 Then Return ""
            Return valori(0)
        Catch ex As Exception
            ScriviLog("CLSContatti.PrimoValoreLista: " & ex.ToString())
            Return ""
        End Try
    End Function

    Private Function SostituisciRecapiti(chiaveContatto As String, categoria As String, valori As List(Of String), chiaveRubrica As String) As Boolean
        Try
            If String.IsNullOrWhiteSpace(chiaveContatto) Then Return False
            Dim esistenti As DataTable = _db.EseguiQuery(
                "SELECT ChiaveWG FROM " & NomeTabellaRecapiti & " WHERE ChiaveContatto = @chiave AND Categoria = @categoria;",
                New Dictionary(Of String, Object) From {{"@chiave", chiaveContatto}, {"@categoria", categoria}})
            If esistenti IsNot Nothing Then
                For Each riga As DataRow In esistenti.Rows
                    _db.EliminaRecord(NomeTabellaRecapiti, riga("ChiaveWG").ToString())
                Next
            End If
            If valori Is Nothing Then Return True
            Dim ordine As Integer = 0
            For Each valore As String In valori
                Dim valoreDaSalvare As String = CifraValoreRecapito(chiaveRubrica, valore)
                _db.InserisciRecord(NomeTabellaRecapiti, New Dictionary(Of String, Object) From {
                    {"ChiaveContatto", chiaveContatto},
                    {"Categoria", categoria},
                    {"Valore", valoreDaSalvare},
                    {"Ordine", ordine}
                })
                ordine += 1
            Next
            Return True
        Catch ex As Exception
            ScriviLog("CLSContatti.SostituisciRecapiti (" & If(chiaveContatto, "") & "/" & If(categoria, "") & "): " & ex.ToString())
            Return False
        End Try
    End Function

    Public Function OttieniRecapiti(chiaveContatto As String, categoria As String) As List(Of String)
        Dim risultato As New List(Of String)
        Try
            If String.IsNullOrWhiteSpace(chiaveContatto) Then Return risultato
            Dim rigaContatto As DataRow = OttieniContattoGrezzo(chiaveContatto)
            Dim rubrica As String = ValoreColonna(rigaContatto, "ChiaveRubrica")
            If RubricaProtetta(rubrica) AndAlso Not RubricaSbloccata(rubrica) Then Return risultato

            Dim tabella As DataTable = _db.EseguiQuery(
                "SELECT Valore FROM " & NomeTabellaRecapiti & " WHERE ChiaveContatto = @chiave AND Categoria = @categoria ORDER BY Ordine ASC;",
                New Dictionary(Of String, Object) From {{"@chiave", chiaveContatto}, {"@categoria", categoria}})
            If tabella Is Nothing Then Return risultato
            For Each riga As DataRow In tabella.Rows
                Dim valore As String = ValoreColonna(riga, "Valore")
                If valore.Length = 0 Then Continue For
                risultato.Add(DecifraValoreRecapito(rubrica, valore))
            Next
        Catch ex As Exception
            ScriviLog("CLSContatti.OttieniRecapiti (" & If(chiaveContatto, "") & "/" & If(categoria, "") & "): " & ex.ToString())
        End Try
        Return risultato
    End Function

    Public Function OttieniTuttiIRecapiti(chiaveContatto As String) As Dictionary(Of String, List(Of String))
        Dim risultato As New Dictionary(Of String, List(Of String))
        Try
            risultato(CategoriaRecapitoTelefono) = OttieniRecapiti(chiaveContatto, CategoriaRecapitoTelefono)
            risultato(CategoriaRecapitoCellulare) = OttieniRecapiti(chiaveContatto, CategoriaRecapitoCellulare)
            risultato(CategoriaRecapitoEmail) = OttieniRecapiti(chiaveContatto, CategoriaRecapitoEmail)
            risultato(CategoriaRecapitoIndirizzo) = OttieniRecapiti(chiaveContatto, CategoriaRecapitoIndirizzo)
            risultato(CategoriaRecapitoSito) = OttieniRecapiti(chiaveContatto, CategoriaRecapitoSito)
        Catch ex As Exception
            ScriviLog("CLSContatti.OttieniTuttiIRecapiti (" & If(chiaveContatto, "") & "): " & ex.ToString())
        End Try
        Return risultato
    End Function

    Public Function EliminaRecapitiContatto(chiaveContatto As String) As Boolean
        Try
            If String.IsNullOrWhiteSpace(chiaveContatto) Then Return False
            Dim tabella As DataTable = _db.EseguiQuery(
                "SELECT ChiaveWG FROM " & NomeTabellaRecapiti & " WHERE ChiaveContatto = @chiave;",
                New Dictionary(Of String, Object) From {{"@chiave", chiaveContatto}})
            If tabella Is Nothing Then Return True
            For Each riga As DataRow In tabella.Rows
                _db.EliminaRecord(NomeTabellaRecapiti, riga("ChiaveWG").ToString())
            Next
            Return True
        Catch ex As Exception
            ScriviLog("CLSContatti.EliminaRecapitiContatto (" & If(chiaveContatto, "") & "): " & ex.ToString())
            Return False
        End Try
    End Function

    Public Function AggiungiContatto(nome As String, cognome As String,
                                      Optional email As List(Of String) = Nothing,
                                      Optional telefono As List(Of String) = Nothing,
                                      Optional cellulare As List(Of String) = Nothing,
                                      Optional indirizzo As List(Of String) = Nothing,
                                      Optional sito As List(Of String) = Nothing,
                                      Optional azienda As String = "",
                                      Optional chiaveCategoria As String = "",
                                      Optional note As String = "",
                                      Optional preferito As Boolean = False,
                                      Optional chiaveAccountSincronizzazione As String = "",
                                      Optional chiaveRubrica As String = "",
                                      Optional compleanno As String = Nothing) As String
        Try
            Dim listaEmail As List(Of String) = NormalizzaListaRecapiti(email)
            Dim listaTelefono As List(Of String) = NormalizzaListaRecapiti(telefono)
            Dim listaCellulare As List(Of String) = NormalizzaListaRecapiti(cellulare)
            Dim listaIndirizzo As List(Of String) = NormalizzaListaRecapiti(indirizzo)
            Dim listaSito As List(Of String) = NormalizzaListaRecapiti(sito)

            Dim account As String
            Dim rubrica As String
            If Not String.IsNullOrWhiteSpace(chiaveRubrica) Then
                ' Rubrica scelta esplicitamente: l'account e' quello della rubrica. Prima veniva
                ' usato l'account predefinito globale, quindi un contatto aggiunto a una rubrica
                ' locale o di un altro account finiva legato (e da sincronizzare) sull'account sbagliato.
                rubrica = chiaveRubrica.Trim()
                Dim rigaRubrica As DataRow = _db.OttieniRecord(NomeTabellaRubriche, rubrica)
                If rigaRubrica Is Nothing Then
                    _ultimoErrore = "La rubrica selezionata non esiste piu'."
                    Return ""
                End If
                account = ValoreColonna(rigaRubrica, "ChiaveAccount").Trim()
            Else
                account = AccountDaUsare(chiaveAccountSincronizzazione)
                rubrica = RubricaDaUsare("", account)
                ' Senza account e senza rubrica il contatto resterebbe fuori da ogni rubrica.
                If String.IsNullOrWhiteSpace(rubrica) Then rubrica = OttieniORCreaRubricaPredefinita("")
            End If

            If RubricaProtetta(rubrica) AndAlso Not RubricaSbloccata(rubrica) Then
                _ultimoErrore = "La rubrica è protetta: sbloccala prima di aggiungere un contatto."
                Return ""
            End If

            Dim valori As New Dictionary(Of String, Object) From {
                {"Nome", nome},
                {"Cognome", cognome},
                {"Email", PrimoValoreLista(listaEmail)},
                {"Telefono", PrimoValoreLista(listaTelefono)},
                {"Cellulare", PrimoValoreLista(listaCellulare)},
                {"Azienda", azienda},
                {"ChiaveCategoria", chiaveCategoria},
                {"Note", note},
                {"Preferito", If(preferito, 1, 0)},
                {"ChiaveAccountOrigine", account},
                {"ChiaveRubrica", rubrica},
                {"Compleanno", If(compleanno, "")},
                {"DaSincronizzare", If(String.IsNullOrWhiteSpace(account), 0, 1)}
            }
            valori = CifraValoriContatto(rubrica, valori)
            Dim chiave As String = _db.InserisciRecord(NomeTabellaContatti, valori)
            If String.IsNullOrWhiteSpace(chiave) Then Return ""

            SostituisciRecapiti(chiave, CategoriaRecapitoEmail, listaEmail, rubrica)
            SostituisciRecapiti(chiave, CategoriaRecapitoTelefono, listaTelefono, rubrica)
            SostituisciRecapiti(chiave, CategoriaRecapitoCellulare, listaCellulare, rubrica)
            SostituisciRecapiti(chiave, CategoriaRecapitoIndirizzo, listaIndirizzo, rubrica)
            SostituisciRecapiti(chiave, CategoriaRecapitoSito, listaSito, rubrica)

            Return chiave
        Catch ex As Exception
            ImpostaErrore("CLSContatti.AggiungiContatto", ex)
            Return ""
        End Try
    End Function

    Public Function AggiornaContatto(chiave As String, nome As String, cognome As String,
                                      Optional email As List(Of String) = Nothing,
                                      Optional telefono As List(Of String) = Nothing,
                                      Optional cellulare As List(Of String) = Nothing,
                                      Optional indirizzo As List(Of String) = Nothing,
                                      Optional sito As List(Of String) = Nothing,
                                      Optional azienda As String = "",
                                      Optional chiaveCategoria As String = "",
                                      Optional note As String = "",
                                      Optional preferito As Boolean = False,
                                      Optional chiaveAccountSincronizzazione As String = "",
                                      Optional chiaveRubrica As String = "",
                                      Optional compleanno As String = Nothing) As Boolean
        Try
            Dim listaEmail As List(Of String) = NormalizzaListaRecapiti(email)
            Dim listaTelefono As List(Of String) = NormalizzaListaRecapiti(telefono)
            Dim listaCellulare As List(Of String) = NormalizzaListaRecapiti(cellulare)
            Dim listaIndirizzo As List(Of String) = NormalizzaListaRecapiti(indirizzo)
            Dim listaSito As List(Of String) = NormalizzaListaRecapiti(sito)

            Dim valori As New Dictionary(Of String, Object) From {
                {"Nome", nome},
                {"Cognome", cognome},
                {"Email", PrimoValoreLista(listaEmail)},
                {"Telefono", PrimoValoreLista(listaTelefono)},
                {"Cellulare", PrimoValoreLista(listaCellulare)},
                {"Azienda", azienda},
                {"ChiaveCategoria", chiaveCategoria},
                {"Note", note},
                {"Preferito", If(preferito, 1, 0)}
            }

            Dim precedente As DataRow = OttieniContattoGrezzo(chiave)
            If precedente Is Nothing Then
                _ultimoErrore = "Il contatto non esiste piu'."
                Return False
            End If

            Dim rubricaDestinazione As String = If(chiaveRubrica, "").Trim()
            Dim account As String
            If rubricaDestinazione.Length > 0 Then
                ' Rubrica indicata (anche per spostare il contatto): l'account e' quello della
                ' rubrica. Spostandolo in una rubrica locale il contatto esce dalla sincronizzazione.
                Dim rigaRubrica As DataRow = _db.OttieniRecord(NomeTabellaRubriche, rubricaDestinazione)
                If rigaRubrica Is Nothing Then
                    _ultimoErrore = "La rubrica selezionata non esiste piu'."
                    Return False
                End If
                account = ValoreColonna(rigaRubrica, "ChiaveAccount").Trim()
                valori("ChiaveRubrica") = rubricaDestinazione
                valori("ChiaveAccountOrigine") = account
                valori("DaSincronizzare") = If(account.Length = 0, 0, 1)
            Else
                rubricaDestinazione = ValoreColonna(precedente, "ChiaveRubrica")
                account = If(chiaveAccountSincronizzazione, "").Trim()
                If account.Length = 0 Then account = ValoreColonna(precedente, "ChiaveAccountOrigine").Trim()
                If account.Length > 0 Then
                    valori("ChiaveAccountOrigine") = account
                    valori("DaSincronizzare") = 1
                End If
            End If

            If RubricaProtetta(rubricaDestinazione) AndAlso Not RubricaSbloccata(rubricaDestinazione) Then
                _ultimoErrore = "La rubrica è protetta: sbloccala prima di modificare un contatto."
                Return False
            End If
            ' il compleanno cambia solo se chi chiama lo dà (Nothing lascia quello che c'è)
            If compleanno IsNot Nothing Then valori("Compleanno") = compleanno
            valori = CifraValoriContatto(rubricaDestinazione, valori)

            Dim aggiornato As Boolean = _db.AggiornaRecord(NomeTabellaContatti, chiave, valori)
            If Not aggiornato Then Return False

            SostituisciRecapiti(chiave, CategoriaRecapitoEmail, listaEmail, rubricaDestinazione)
            SostituisciRecapiti(chiave, CategoriaRecapitoTelefono, listaTelefono, rubricaDestinazione)
            SostituisciRecapiti(chiave, CategoriaRecapitoCellulare, listaCellulare, rubricaDestinazione)
            SostituisciRecapiti(chiave, CategoriaRecapitoIndirizzo, listaIndirizzo, rubricaDestinazione)
            SostituisciRecapiti(chiave, CategoriaRecapitoSito, listaSito, rubricaDestinazione)

            Return True
        Catch ex As Exception
            ImpostaErrore("CLSContatti.AggiornaContatto (" & chiave & ")", ex)
            Return False
        End Try
    End Function

    Public Function ImpostaPreferito(chiave As String, preferito As Boolean) As Boolean
        Try
            Return _db.AggiornaRecord(NomeTabellaContatti, chiave, New Dictionary(Of String, Object) From {
                {"Preferito", If(preferito, 1, 0)}
            })
        Catch ex As Exception
            ImpostaErrore("CLSContatti.ImpostaPreferito (" & chiave & ")", ex)
            Return False
        End Try
    End Function

    Public Function MarcaPerSincronizzazione(chiave As String, chiaveAccount As String) As Boolean
        Try
            Return _db.AggiornaRecord(NomeTabellaContatti, chiave, New Dictionary(Of String, Object) From {
                {"ChiaveAccountOrigine", chiaveAccount},
                {"DaSincronizzare", 1}
            })
        Catch ex As Exception
            ImpostaErrore("CLSContatti.MarcaPerSincronizzazione (" & chiave & ")", ex)
            Return False
        End Try
    End Function

    Public Function MarcaTuttiPerSincronizzazione(chiaveAccount As String) As Integer
        Try
            If String.IsNullOrWhiteSpace(chiaveAccount) Then Return 0
            Dim tabella As DataTable = OttieniElenco()
            Dim contatore As Integer = 0
            For Each riga As DataRow In tabella.Rows
                Try
                    If MarcaPerSincronizzazione(riga("ChiaveWG").ToString(), chiaveAccount) Then contatore += 1
                Catch exRiga As Exception
                    ScriviLog("CLSContatti.MarcaTuttiPerSincronizzazione riga: " & exRiga.ToString())
                End Try
            Next
            Return contatore
        Catch ex As Exception
            ImpostaErrore("CLSContatti.MarcaTuttiPerSincronizzazione (" & If(chiaveAccount, "") & ")", ex)
            Return 0
        End Try
    End Function

    Public Shared Function SincronizzaEliminazioniAttiva() As Boolean
        Try
            Dim configurazione As New WGConfig()
            Dim valore As String = configurazione.LeggiValore(ChiaveConfigSincronizzaEliminazioni)
            If String.IsNullOrWhiteSpace(valore) Then Return True
            Return valore = "1"
        Catch ex As Exception
            ScriviLog("CLSContatti.SincronizzaEliminazioniAttiva: " & ex.ToString())
            Return True
        End Try
    End Function

    Public Shared Sub ImpostaSincronizzaEliminazioni(attiva As Boolean)
        Try
            Dim configurazione As New WGConfig()
            configurazione.SalvaValore(ChiaveConfigSincronizzaEliminazioni, If(attiva, "1", "0"))
        Catch ex As Exception
            ScriviLog("CLSContatti.ImpostaSincronizzaEliminazioni: " & ex.ToString())
        End Try
    End Sub

    Private Sub RegistraEliminazione(riga As DataRow)
        Try
            If riga Is Nothing Then Return
            If Not SincronizzaEliminazioniAttiva() Then Return

            Dim uid As String = ValoreColonna(riga, "UidServer")
            Dim url As String = ValoreColonna(riga, "UrlServer")
            Dim account As String = ValoreColonna(riga, "ChiaveAccountOrigine")

            If String.IsNullOrWhiteSpace(account) Then Return
            If String.IsNullOrWhiteSpace(uid) AndAlso String.IsNullOrWhiteSpace(url) Then Return

            _db.InserisciRecord(NomeTabellaEliminati, New Dictionary(Of String, Object) From {
                {"UidServer", uid},
                {"UrlServer", url},
                {"ChiaveAccount", account},
                {"DataEliminazione", DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss")}
            })
        Catch ex As Exception
            ScriviLog("CLSContatti.RegistraEliminazione: " & ex.ToString())
        End Try
    End Sub

    Public Function EliminaContatto(chiave As String) As Boolean
        Try
            Dim riga As DataRow = OttieniContatto(chiave)
            RegistraEliminazione(riga)
            EliminaRecapitiContatto(chiave)
            Return _db.EliminaRecord(NomeTabellaContatti, chiave)
        Catch ex As Exception
            ImpostaErrore("CLSContatti.EliminaContatto (" & chiave & ")", ex)
            Return False
        End Try
    End Function

    Public Function EliminaContatti(chiavi As List(Of String)) As Integer
        Dim contatore As Integer = 0
        Try
            If chiavi Is Nothing OrElse chiavi.Count = 0 Then Return 0
            For Each chiave As String In chiavi
                Try
                    If String.IsNullOrWhiteSpace(chiave) Then Continue For
                    If EliminaContatto(chiave) Then contatore += 1
                Catch exChiave As Exception
                    ScriviLog("CLSContatti.EliminaContatti (" & If(chiave, "") & "): " & exChiave.ToString())
                End Try
            Next
        Catch ex As Exception
            ImpostaErrore("CLSContatti.EliminaContatti", ex)
        End Try
        Return contatore
    End Function

    Public Function OttieniEliminazioniDaSincronizzare(Optional chiaveAccount As String = "") As DataTable
        Try
            If String.IsNullOrWhiteSpace(chiaveAccount) Then
                Return _db.OttieniTuttiIRecord(NomeTabellaEliminati, "DataEliminazione ASC")
            End If
            Return _db.EseguiQuery("SELECT * FROM " & NomeTabellaEliminati & " WHERE ChiaveAccount = @account ORDER BY DataEliminazione ASC;",
                New Dictionary(Of String, Object) From {{"@account", chiaveAccount}})
        Catch ex As Exception
            ImpostaErrore("CLSContatti.OttieniEliminazioniDaSincronizzare (" & If(chiaveAccount, "") & ")", ex)
            Return New DataTable()
        End Try
    End Function

    Public Function RimuoviEliminazioneSincronizzata(chiave As String) As Boolean
        Try
            If String.IsNullOrWhiteSpace(chiave) Then Return False
            Return _db.EliminaRecord(NomeTabellaEliminati, chiave)
        Catch ex As Exception
            ImpostaErrore("CLSContatti.RimuoviEliminazioneSincronizzata (" & chiave & ")", ex)
            Return False
        End Try
    End Function

    Public Function ContaEliminazioniDaSincronizzare(Optional chiaveAccount As String = "") As Integer
        Try
            Dim tabella As DataTable = OttieniEliminazioniDaSincronizzare(chiaveAccount)
            If tabella Is Nothing Then Return 0
            Return tabella.Rows.Count
        Catch ex As Exception
            ScriviLog("CLSContatti.ContaEliminazioniDaSincronizzare: " & ex.ToString())
            Return 0
        End Try
    End Function

    ' Riga cosi' come sta nel database, senza decifrare. Serve solo internamente.
    Friend Function OttieniContattoGrezzo(chiave As String) As DataRow
        Try
            Return _db.OttieniRecord(NomeTabellaContatti, chiave)
        Catch ex As Exception
            ScriviLog("CLSContatti.OttieniContattoGrezzo (" & If(chiave, "") & "): " & ex.ToString())
            Return Nothing
        End Try
    End Function

    Public Function OttieniContatto(chiave As String) As DataRow
        Try
            Dim riga As DataRow = _db.OttieniRecord(NomeTabellaContatti, chiave)
            If riga Is Nothing Then Return Nothing
            Dim tabella As DataTable = riga.Table.Clone()
            tabella.ImportRow(riga)
            tabella = PreparaContatti(tabella)
            If tabella Is Nothing OrElse tabella.Rows.Count = 0 Then Return Nothing
            Return tabella.Rows(0)
        Catch ex As Exception
            ImpostaErrore("CLSContatti.OttieniContatto (" & chiave & ")", ex)
            Return Nothing
        End Try
    End Function

    Public Function OttieniElenco(Optional chiaveCategoria As String = "",
                                   Optional chiaveAccount As String = "",
                                   Optional chiaveRubrica As String = "") As DataTable
        Try
            Dim condizioni As New List(Of String)
            Dim parametri As New Dictionary(Of String, Object)
            If Not String.IsNullOrWhiteSpace(chiaveCategoria) Then
                condizioni.Add("ChiaveCategoria = @chiaveCategoria")
                parametri("@chiaveCategoria") = chiaveCategoria
            End If
            If Not String.IsNullOrWhiteSpace(chiaveRubrica) Then
                condizioni.Add("ChiaveRubrica = @chiaveRubrica")
                parametri("@chiaveRubrica") = chiaveRubrica
            ElseIf Not String.IsNullOrWhiteSpace(chiaveAccount) Then
                condizioni.Add("ChiaveAccountOrigine = @chiaveAccount")
                parametri("@chiaveAccount") = chiaveAccount
            End If
            If condizioni.Count = 0 Then
                Return PreparaContatti(_db.OttieniTuttiIRecord(NomeTabellaContatti, "Cognome ASC, Nome ASC"))
            End If
            Return PreparaContatti(_db.EseguiQuery("SELECT * FROM " & NomeTabellaContatti & " WHERE " & String.Join(" AND ", condizioni) &
                " ORDER BY Cognome ASC, Nome ASC;", parametri))
        Catch ex As Exception
            ImpostaErrore("CLSContatti.OttieniElenco", ex)
            Return New DataTable()
        End Try
    End Function

    Public Function OttieniPreferiti() As DataTable
        Try
            Return PreparaContatti(_db.EseguiQuery("SELECT * FROM " & NomeTabellaContatti & " WHERE Preferito = 1 ORDER BY Cognome ASC, Nome ASC;", Nothing))
        Catch ex As Exception
            ImpostaErrore("CLSContatti.OttieniPreferiti", ex)
            Return New DataTable()
        End Try
    End Function

    ''' <summary>
    ''' Cerca fra i contatti. Se chiaveRubrica e' valorizzata, limita la ricerca a quella
    ''' rubrica soltanto (usata dalla rubrica corrente scelta in MenuRubrica); altrimenti, se
    ''' chiaveAccount e' valorizzato, limita la ricerca alla rubrica di quell'account soltanto
    ''' (ogni account ha la propria rubrica indipendente); lasciando entrambi vuoti cerca su
    ''' tutte le rubriche di tutti gli account, come in precedenza.
    ''' </summary>
    ' La ricerca avviene sui contatti gia' riportati in chiaro: su un campo cifrato il
    ' confronto del database non troverebbe niente.
    Public Function Cerca(testo As String, Optional chiaveAccount As String = "", Optional chiaveRubrica As String = "") As DataTable
        Try
            Dim tutti As DataTable = OttieniElenco("", chiaveAccount, chiaveRubrica)
            If String.IsNullOrWhiteSpace(testo) Then Return tutti
            Return FiltraPerTesto(tutti, testo, New String() {"Nome", "Cognome", "Email", "Telefono", "Cellulare", "Azienda", "Note"},
                TestoRecapitiPerContatto(tutti))
        Catch ex As Exception
            ImpostaErrore("CLSContatti.Cerca (" & testo & ")", ex)
            Return New DataTable()
        End Try
    End Function

    ' Come Cerca, ma limitata ai contatti che hanno un indirizzo email compilato: usata da
    ' MenuPosta per suggerire i destinatari quando in composizione non viene scritta la
    ' chiocciola (si presume quindi un nome/testo da cercare, non un indirizzo già completo).
    ' chiaveAccount limita la ricerca alla rubrica dell'account da cui si sta scrivendo il
    ' messaggio, cosi' come richiesto: si cerca sempre nella rubrica dell'account che si sta
    ' guardando, non in quella di tutti gli altri account configurati.
    ' Come Cerca, ma limitata ai contatti con un indirizzo di posta. Usata dalla composizione
    ' dei messaggi per suggerire i destinatari. Con chiaveRubrica valorizzata cerca in quella
    ' rubrica soltanto, comprese le rubriche locali non collegate ad alcun account.
    Public Function CercaConEmail(testo As String, Optional chiaveAccount As String = "", Optional chiaveRubrica As String = "") As DataTable
        Try
            Dim tutti As DataTable = OttieniElenco("", chiaveAccount, chiaveRubrica)
            If tutti Is Nothing Then Return New DataTable()

            For indice As Integer = tutti.Rows.Count - 1 To 0 Step -1
                If ValoreColonna(tutti.Rows(indice), "Email").Length = 0 Then tutti.Rows.RemoveAt(indice)
            Next
            tutti.AcceptChanges()

            If String.IsNullOrWhiteSpace(testo) Then Return tutti
            Return FiltraPerTesto(tutti, testo, New String() {"Nome", "Cognome", "Email", "Azienda"})
        Catch ex As Exception
            ImpostaErrore("CLSContatti.CercaConEmail (" & testo & ")", ex)
            Return New DataTable()
        End Try
    End Function

    Public Function AggiungiCategoria(nome As String, Optional colore As String = "") As String
        Try
            Return _db.InserisciRecord(NomeTabellaCategorie, New Dictionary(Of String, Object) From {{"Nome", nome}, {"Colore", colore}})
        Catch ex As Exception
            ImpostaErrore("CLSContatti.AggiungiCategoria", ex)
            Return ""
        End Try
    End Function

    Public Function AggiornaCategoria(chiave As String, nome As String, Optional colore As String = "") As Boolean
        Try
            Return _db.AggiornaRecord(NomeTabellaCategorie, chiave, New Dictionary(Of String, Object) From {{"Nome", nome}, {"Colore", colore}})
        Catch ex As Exception
            ImpostaErrore("CLSContatti.AggiornaCategoria (" & chiave & ")", ex)
            Return False
        End Try
    End Function

    Public Function EliminaCategoria(chiave As String) As Boolean
        Try
            _db.EseguiNonQuery("UPDATE " & NomeTabellaContatti & " SET ChiaveCategoria = '' WHERE ChiaveCategoria = @chiave;",
                New Dictionary(Of String, Object) From {{"@chiave", chiave}})
            Return _db.EliminaRecord(NomeTabellaCategorie, chiave)
        Catch ex As Exception
            ImpostaErrore("CLSContatti.EliminaCategoria (" & chiave & ")", ex)
            Return False
        End Try
    End Function

    Public Function OttieniCategorie() As DataTable
        Try
            Return _db.OttieniTuttiIRecord(NomeTabellaCategorie, "Nome ASC")
        Catch ex As Exception
            ImpostaErrore("CLSContatti.OttieniCategorie", ex)
            Return New DataTable()
        End Try
    End Function

    Public Function NomeCategoria(chiaveCategoria As String) As String
        Try
            If String.IsNullOrWhiteSpace(chiaveCategoria) Then Return ""
            Dim riga As DataRow = _db.OttieniRecord(NomeTabellaCategorie, chiaveCategoria)
            If riga Is Nothing Then Return ""
            Return riga("Nome").ToString()
        Catch ex As Exception
            ImpostaErrore("CLSContatti.NomeCategoria", ex)
            Return ""
        End Try
    End Function

    Public Function TrovaContattoPerUidServer(chiaveAccount As String, uidServer As String) As DataRow
        Try
            If String.IsNullOrWhiteSpace(uidServer) Then Return Nothing
            Dim tabella As DataTable = _db.EseguiQuery(
                "SELECT * FROM " & NomeTabellaContatti & " WHERE UidServer = @uid AND ChiaveAccountOrigine = @account LIMIT 1;",
                New Dictionary(Of String, Object) From {{"@uid", uidServer}, {"@account", If(chiaveAccount, "")}})
            If tabella.Rows.Count = 0 Then Return Nothing
            Return tabella.Rows(0)
        Catch ex As Exception
            ImpostaErrore("CLSContatti.TrovaContattoPerUidServer (" & uidServer & ")", ex)
            Return Nothing
        End Try
    End Function

    Public Function ImportaDaSincronizzazione(chiaveAccount As String, uidServer As String, nomeCompleto As String,
                                               Optional email As List(Of String) = Nothing,
                                               Optional telefono As List(Of String) = Nothing,
                                               Optional cellulare As List(Of String) = Nothing,
                                               Optional indirizzo As List(Of String) = Nothing,
                                               Optional sito As List(Of String) = Nothing,
                                               Optional azienda As String = "",
                                               Optional urlServer As String = "",
                                               Optional compleanno As String = Nothing) As String
        Try
            If String.IsNullOrWhiteSpace(uidServer) Then Return ""
            Dim nome As String = ""
            Dim cognome As String = ""
            SpezzaNomeCompleto(nomeCompleto, nome, cognome)
            Dim esistente As DataRow = TrovaContattoPerUidServer(chiaveAccount, uidServer)
            If esistente Is Nothing Then esistente = TrovaDoppioneDaServer(chiaveAccount, uidServer, nome, cognome, email, telefono, cellulare)

            Dim listaEmail As List(Of String) = NormalizzaListaRecapiti(email)
            Dim listaTelefono As List(Of String) = NormalizzaListaRecapiti(telefono)
            Dim listaCellulare As List(Of String) = NormalizzaListaRecapiti(cellulare)
            Dim listaIndirizzo As List(Of String) = NormalizzaListaRecapiti(indirizzo)
            Dim listaSito As List(Of String) = NormalizzaListaRecapiti(sito)

            ' Ogni account (CardDAV o Nylas/Google) sincronizza sempre nella propria rubrica
            ' predefinita, cosi' i dati restano separati e indipendenti tra un account e l'altro,
            ' esattamente come i contatti aggiunti manualmente per quell'account.
            Dim rubrica As String = OttieniORCreaRubricaPredefinita(chiaveAccount)
            Dim rubricaEffettiva As String = rubrica
            Dim daSincronizzare As Boolean = False
            If esistente IsNot Nothing Then
                Try
                    daSincronizzare = Convert.ToInt32(esistente("DaSincronizzare")) = 1
                Catch exStato As Exception
                    daSincronizzare = False
                End Try
                If daSincronizzare Then Return esistente("ChiaveWG").ToString()
                ' Se l'utente ha gia' spostato il contatto in un'altra rubrica dello stesso
                ' account, non lo riporta indietro nella predefinita ad ogni risincronizzazione.
                Dim rubricaGiaAssegnata As String = ValoreColonna(esistente, "ChiaveRubrica")
                If Not String.IsNullOrWhiteSpace(rubricaGiaAssegnata) Then rubricaEffettiva = rubricaGiaAssegnata
            End If

            If RubricaProtetta(rubricaEffettiva) AndAlso Not RubricaPuoRicevereTesto(rubricaEffettiva) Then Return ""

            Dim valori As New Dictionary(Of String, Object) From {
                {"Nome", nome}, {"Cognome", cognome},
                {"Email", PrimoValoreLista(listaEmail)},
                {"Telefono", PrimoValoreLista(listaTelefono)},
                {"Cellulare", PrimoValoreLista(listaCellulare)},
                {"Azienda", azienda}, {"UidServer", uidServer}, {"ChiaveAccountOrigine", If(chiaveAccount, "")}
            }
            If Not String.IsNullOrWhiteSpace(rubricaEffettiva) Then valori("ChiaveRubrica") = rubricaEffettiva
            If Not String.IsNullOrWhiteSpace(urlServer) Then valori("UrlServer") = urlServer
            If compleanno IsNot Nothing Then valori("Compleanno") = compleanno
            If esistente Is Nothing Then valori("IdSync") = IdSyncDaUidServer(uidServer)
            valori = CifraValoriContatto(rubricaEffettiva, valori)

            Dim chiaveContatto As String = ""
            If esistente IsNot Nothing Then
                chiaveContatto = esistente("ChiaveWG").ToString()
                _db.AggiornaRecord(NomeTabellaContatti, chiaveContatto, valori)
            Else
                chiaveContatto = _db.InserisciRecord(NomeTabellaContatti, valori)
            End If

            If Not String.IsNullOrWhiteSpace(chiaveContatto) Then
                SostituisciRecapiti(chiaveContatto, CategoriaRecapitoEmail, listaEmail, rubricaEffettiva)
                SostituisciRecapiti(chiaveContatto, CategoriaRecapitoTelefono, listaTelefono, rubricaEffettiva)
                SostituisciRecapiti(chiaveContatto, CategoriaRecapitoCellulare, listaCellulare, rubricaEffettiva)
                SostituisciRecapiti(chiaveContatto, CategoriaRecapitoIndirizzo, listaIndirizzo, rubricaEffettiva)
                SostituisciRecapiti(chiaveContatto, CategoriaRecapitoSito, listaSito, rubricaEffettiva)
            End If

            Return chiaveContatto
        Catch ex As Exception
            ImpostaErrore("CLSContatti.ImportaDaSincronizzazione (" & uidServer & ")", ex)
            Return ""
        End Try
    End Function

    Public Function OttieniContattiDaSincronizzare(Optional chiaveAccount As String = "") As DataTable
        Try
            Dim tabella As DataTable
            If String.IsNullOrWhiteSpace(chiaveAccount) Then
                tabella = _db.EseguiQuery("SELECT * FROM " & NomeTabellaContatti & " WHERE DaSincronizzare = 1 ORDER BY Cognome ASC, Nome ASC;", Nothing)
            Else
                tabella = _db.EseguiQuery(
                    "SELECT * FROM " & NomeTabellaContatti & " WHERE DaSincronizzare = 1 AND ChiaveAccountOrigine = @account ORDER BY Cognome ASC, Nome ASC;",
                    New Dictionary(Of String, Object) From {{"@account", chiaveAccount}})
            End If
            If tabella Is Nothing Then Return New DataTable()

            For indice As Integer = tabella.Rows.Count - 1 To 0 Step -1
                Dim rubrica As String = ValoreColonna(tabella.Rows(indice), "ChiaveRubrica")
                If RubricaProtetta(rubrica) AndAlso Not RubricaPuoInviareTesto(rubrica) Then tabella.Rows.RemoveAt(indice)
            Next
            tabella.AcceptChanges()

            Return PreparaContatti(tabella)
        Catch ex As Exception
            ImpostaErrore("CLSContatti.OttieniContattiDaSincronizzare (" & chiaveAccount & ")", ex)
            Return New DataTable()
        End Try
    End Function

    Public Function ContaContattiDaSincronizzare(Optional chiaveAccount As String = "") As Integer
        Try
            Dim tabella As DataTable = OttieniContattiDaSincronizzare(chiaveAccount)
            If tabella Is Nothing Then Return 0
            Return tabella.Rows.Count
        Catch ex As Exception
            ScriviLog("CLSContatti.ContaContattiDaSincronizzare: " & ex.ToString())
            Return 0
        End Try
    End Function

    Public Function CiSonoContattiDaSincronizzare(Optional chiaveAccount As String = "") As Boolean
        Try
            If ContaContattiDaSincronizzare(chiaveAccount) > 0 Then Return True
            Return ContaEliminazioniDaSincronizzare(chiaveAccount) > 0
        Catch ex As Exception
            ScriviLog("CLSContatti.CiSonoContattiDaSincronizzare: " & ex.ToString())
            Return False
        End Try
    End Function

    Public Function SegnaContattoSincronizzato(chiave As String, uidServer As String, Optional urlServer As String = "") As Boolean
        Try
            Dim valori As New Dictionary(Of String, Object) From {
                {"UidServer", uidServer}, {"DaSincronizzare", 0}}
            If Not String.IsNullOrWhiteSpace(urlServer) Then valori("UrlServer") = urlServer
            Return _db.AggiornaRecord(NomeTabellaContatti, chiave, valori)
        Catch ex As Exception
            ImpostaErrore("CLSContatti.SegnaContattoSincronizzato (" & chiave & ")", ex)
            Return False
        End Try
    End Function

    Private Sub SpezzaNomeCompleto(nomeCompleto As String, ByRef nome As String, ByRef cognome As String)
        nome = ""
        cognome = ""
        Try
            If String.IsNullOrWhiteSpace(nomeCompleto) Then Return
            Dim parti() As String = nomeCompleto.Trim().Split(" "c)
            If parti.Length = 1 Then
                nome = parti(0)
                Return
            End If
            cognome = parti(parti.Length - 1)
            nome = String.Join(" ", parti, 0, parti.Length - 1)
        Catch ex As Exception
            ScriviLog("CLSContatti.SpezzaNomeCompleto (" & nomeCompleto & "): " & ex.ToString())
        End Try
    End Sub

    Public Sub AggiungiRicercaRecente(testo As String)
        Try
            If String.IsNullOrWhiteSpace(testo) Then Return
            Dim termine As String = testo.Trim()
            If termine.Length > 200 Then termine = termine.Substring(0, 200)

            Dim esistenti As DataTable = _db.EseguiQuery(
                "SELECT ChiaveWG FROM " & NomeTabellaRicercheRecenti & " WHERE Termine = @termine COLLATE NOCASE;",
                New Dictionary(Of String, Object) From {{"@termine", termine}})
            If esistenti IsNot Nothing Then
                For Each riga As DataRow In esistenti.Rows
                    _db.EliminaRecord(NomeTabellaRicercheRecenti, riga("ChiaveWG").ToString())
                Next
            End If

            _db.InserisciRecord(NomeTabellaRicercheRecenti, New Dictionary(Of String, Object) From {
                {"Termine", termine},
                {"Data", DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss")}
            })

            Dim tutte As DataTable = _db.OttieniTuttiIRecord(NomeTabellaRicercheRecenti, "Data DESC")
            If tutte IsNot Nothing AndAlso tutte.Rows.Count > MassimoRicercheRecenti Then
                For indice As Integer = MassimoRicercheRecenti To tutte.Rows.Count - 1
                    _db.EliminaRecord(NomeTabellaRicercheRecenti, tutte.Rows(indice)("ChiaveWG").ToString())
                Next
            End If
        Catch ex As Exception
            ScriviLog("CLSContatti.AggiungiRicercaRecente (" & If(testo, "") & "): " & ex.ToString())
        End Try
    End Sub

    Public Function OttieniRicercheRecenti(Optional massimo As Integer = MassimoRicercheRecenti) As List(Of String)
        Dim risultato As New List(Of String)
        Try
            Dim tutte As DataTable = _db.OttieniTuttiIRecord(NomeTabellaRicercheRecenti, "Data DESC")
            If tutte Is Nothing Then Return risultato
            For Each riga As DataRow In tutte.Rows
                If risultato.Count >= massimo Then Exit For
                Dim termine As String = riga("Termine").ToString()
                If Not String.IsNullOrWhiteSpace(termine) Then risultato.Add(termine)
            Next
        Catch ex As Exception
            ScriviLog("CLSContatti.OttieniRicercheRecenti: " & ex.ToString())
        End Try
        Return risultato
    End Function

    Public Function SvuotaRicercheRecenti() As Boolean
        Try
            Dim tutte As DataTable = _db.OttieniTuttiIRecord(NomeTabellaRicercheRecenti, "Data DESC")
            If tutte Is Nothing Then Return True
            For Each riga As DataRow In tutte.Rows
                _db.EliminaRecord(NomeTabellaRicercheRecenti, riga("ChiaveWG").ToString())
            Next
            Return True
        Catch ex As Exception
            ScriviLog("CLSContatti.SvuotaRicercheRecenti: " & ex.ToString())
            Return False
        End Try
    End Function

    Public Sub Dispose() Implements IDisposable.Dispose
        Try
            If _smaltito Then Return
            _smaltito = True
            BloccaTutteLeRubriche() ' chiavi e password non devono restare in memoria
            If _db IsNot Nothing Then _db.Dispose()
        Catch ex As Exception
            ScriviLog("CLSContatti.Dispose: " & ex.ToString())
        End Try
    End Sub

End Class