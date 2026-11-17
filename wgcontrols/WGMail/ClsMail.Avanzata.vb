Imports System
Imports System.Collections.Generic
Imports System.Data
Imports System.Security.Cryptography
Imports System.Text
Imports MailKit
Imports MailKit.Net.Imap
Imports MailKit.Search
Imports WGDb

Partial Public Class CLSMail

    Private Const MarcatoreCifratura As String = "ENC1:"

    Private Const PassphraseChiaveAccount As String = "WinGabriel.CLSMail.PasswordAccount.v1"
    Private Const SaleChiaveAccount As String = "Wpk9ceQMiC9msxp9yVDiGA=="

    Private Shared _chiaveAccountFissa As Byte() = Nothing
    Private Shared _chiaveAccountFissaCalcolata As Boolean = False
    Private Shared ReadOnly _lockChiaveAccountFissa As New Object()

    Private Shared ReadOnly _decifratureFallite As New HashSet(Of String)(StringComparer.Ordinal)
    Private Shared ReadOnly _lockDecifratureFallite As New Object()

    Private _avanzata As PostaAvanzata

    Private Function Avanzata() As PostaAvanzata
        Try
            If _avanzata Is Nothing Then _avanzata = New PostaAvanzata()
        Catch ex As Exception
            ScriviLog("CLSMail.Avanzata: " & ex.ToString())
        End Try
        Return _avanzata
    End Function

    ' Chiave fissa per la cifratura delle password degli account, indipendente dal client:
    ' non e' legata all'utente o alla macchina Windows (a differenza della DPAPI usata prima),
    ' cosi' un account configurato resta leggibile anche cambiando computer o reinstallando
    ' Windows. Calcolata una sola volta e tenuta in memoria per il resto del processo.
    Private Shared Function ChiaveAccountFissa() As Byte()
        Try
            If _chiaveAccountFissaCalcolata Then Return _chiaveAccountFissa
            SyncLock _lockChiaveAccountFissa
                If Not _chiaveAccountFissaCalcolata Then
                    _chiaveAccountFissa = WGCifratura.DerivaChiave(PassphraseChiaveAccount, SaleChiaveAccount)
                    _chiaveAccountFissaCalcolata = True
                End If
            End SyncLock
            Return _chiaveAccountFissa
        Catch ex As Exception
            ScriviLog("CLSMail.ChiaveAccountFissa: " & ex.ToString())
            Return Nothing
        End Try
    End Function

    ' Scrive nel log una sola volta per ciascun valore cifrato che non si riesce a decifrare,
    ' invece che a ogni chiamata: un account con la password non piu' decifrabile viene
    ' interrogato ripetutamente (scaricamento in sottofondo, controllo automatico, recupero
    ' bozze), e senza questo filtro finirebbe per riempire il log con lo stesso errore.
    Private Shared Sub RegistraErroreDecifraUnaVolta(testoCifrato As String, messaggio As String)
        Try
            Dim chiaveVoce As String = If(testoCifrato, "")
            SyncLock _lockDecifratureFallite
                If _decifratureFallite.Contains(chiaveVoce) Then Return
                _decifratureFallite.Add(chiaveVoce)
            End SyncLock
            ScriviLog(messaggio)
        Catch
        End Try
    End Sub

    Private Const ScopoPasswordAccount As String = "mail-account"

    ' Le password degli account si proteggono con la libreria nativa (senza password dell'utente, chiave dello scopo):
    ' restano leggibili cambiando computer e passano nelle esportazioni dei dati.
    Public Shared Function CifraPassword(testo As String) As String
        Try
            If String.IsNullOrEmpty(testo) Then Return ""
            If WGCifraturaNativa.EProtetto(testo) Then Return testo
            If WGCifratura.ECifrato(testo) Then
                Dim precedente As String = DecifraPasswordConChiavePrecedente(testo)
                If String.IsNullOrEmpty(precedente) Then Return testo
                testo = precedente
            End If
            Dim protetto As String = WGCifraturaNativa.ProteggiTesto(ScopoPasswordAccount, testo)
            If protetto = "" Then Return testo
            Return protetto
        Catch ex As Exception
            ScriviLog("CLSMail.CifraPassword: " & ex.ToString())
            Return If(testo, "")
        End Try
    End Function

    Public Shared Function DecifraPassword(testo As String) As String
        Try
            If String.IsNullOrEmpty(testo) Then Return ""

            If WGCifraturaNativa.EProtetto(testo) Then
                Dim risultatoNativo As String = WGCifraturaNativa.RivelaTesto(ScopoPasswordAccount, testo)
                If String.IsNullOrEmpty(risultatoNativo) Then
                    RegistraErroreDecifraUnaVolta(testo, "CLSMail.DecifraPassword: impossibile decifrare la password memorizzata, verra' richiesta di nuovo.")
                    Return ""
                End If
                Return risultatoNativo
            End If

            If WGCifratura.ECifrato(testo) Then
                Dim risultato As String = DecifraPasswordConChiavePrecedente(testo)
                If String.IsNullOrEmpty(risultato) Then
                    RegistraErroreDecifraUnaVolta(testo, "CLSMail.DecifraPassword: impossibile decifrare la password memorizzata, verra' richiesta di nuovo.")
                    Return ""
                End If
                Return risultato
            End If

            If testo.StartsWith(MarcatoreCifratura, StringComparison.Ordinal) Then
                Dim risultatoPrecedente As String = DecifraPasswordFormatoPrecedente(testo)
                If String.IsNullOrEmpty(risultatoPrecedente) Then
                    RegistraErroreDecifraUnaVolta(testo, "CLSMail.DecifraPassword: impossibile decifrare la password nel formato precedente, verra' richiesta di nuovo.")
                    Return ""
                End If
                Return risultatoPrecedente
            End If

            Return testo
        Catch ex As Exception
            RegistraErroreDecifraUnaVolta(testo, "CLSMail.DecifraPassword: " & ex.ToString())
            Return ""
        End Try
    End Function

    ' Formato usato prima della libreria nativa: chiave derivata da una frase fissa. Serve solo per leggere e convertire i dati vecchi.
    Private Shared Function DecifraPasswordConChiavePrecedente(testo As String) As String
        Try
            Dim chiave As Byte() = ChiaveAccountFissa()
            If chiave Is Nothing Then Return ""
            Return WGCifratura.Decifra(testo, chiave)
        Catch ex As Exception
            ScriviLog("CLSMail.DecifraPasswordConChiavePrecedente: " & ex.Message)
            Return ""
        End Try
    End Function

    ' Formato usato prima del passaggio a WGCifratura (DPAPI, legata all'utente Windows
    ' corrente): serve solo per migrare le password gia' salvate, vedi CifraPasswordEsistenti.
    Private Shared Function DecifraPasswordFormatoPrecedente(testo As String) As String
        Try
            Dim base64 As String = testo.Substring(MarcatoreCifratura.Length)
            Dim protetto As Byte() = Convert.FromBase64String(base64)
            Dim inBytes As Byte() = ProtectedData.Unprotect(protetto, Nothing, DataProtectionScope.CurrentUser)
            Return Encoding.UTF8.GetString(inBytes)
        Catch
            Return ""
        End Try
    End Function

    Private Sub CifraPasswordEsistenti()
        Try
            Dim tabella As DataTable = _db.OttieniTuttiIRecord(NomeTabellaAccount, "")
            If tabella Is Nothing Then Return
            For Each riga As DataRow In tabella.Rows
                Try
                    If IsDBNull(riga("Password")) Then Continue For
                    Dim grezza As String = riga("Password").ToString()
                    If String.IsNullOrEmpty(grezza) Then Continue For
                    If WGCifraturaNativa.EProtetto(grezza) Then Continue For

                    Dim chiaveRiga As String = riga("ChiaveWG").ToString()
                    If String.IsNullOrWhiteSpace(chiaveRiga) Then Continue For

                    Dim testoInChiaro As String = grezza
                    If WGCifratura.ECifrato(grezza) Then
                        testoInChiaro = DecifraPasswordConChiavePrecedente(grezza)
                        If String.IsNullOrEmpty(testoInChiaro) Then Continue For
                    End If
                    If grezza.StartsWith(MarcatoreCifratura, StringComparison.Ordinal) Then
                        testoInChiaro = DecifraPasswordFormatoPrecedente(grezza)
                        If String.IsNullOrEmpty(testoInChiaro) Then
                            RegistraErroreDecifraUnaVolta(grezza,
                                "CLSMail.CifraPasswordEsistenti: impossibile decifrare la password precedente dell'account " &
                                chiaveRiga & ", andra' reinserita.")
                            Continue For
                        End If
                    End If

                    _db.AggiornaRecord(NomeTabellaAccount, chiaveRiga,
                        New Dictionary(Of String, Object) From {{"Password", CifraPassword(testoInChiaro)}})
                Catch exRiga As Exception
                    ScriviLog("CLSMail.CifraPasswordEsistenti riga: " & exRiga.ToString())
                End Try
                Try
                    CifraCampoGoogleEsistente(riga, "GoogleClientSecret")
                    CifraCampoGoogleEsistente(riga, "GoogleRefreshToken")
                    CifraCampoGoogleEsistente(riga, "GoogleAccessToken")
                Catch exGoogle As Exception
                    ScriviLog("CLSMail.CifraPasswordEsistenti (google): " & exGoogle.ToString())
                End Try
            Next
        Catch ex As Exception
            ScriviLog("CLSMail.CifraPasswordEsistenti: " & ex.ToString())
        End Try
    End Sub

    ' I segreti e i token dell'account Google (altrettanto sensibili di una password) si proteggono allo stesso modo,
    ' se non lo sono ancora: quelli salvati prima di questa protezione erano in chiaro nel database.
    Private Sub CifraCampoGoogleEsistente(riga As DataRow, nomeColonna As String)
        Try
            If Not riga.Table.Columns.Contains(nomeColonna) OrElse IsDBNull(riga(nomeColonna)) Then Return
            Dim grezzo As String = riga(nomeColonna).ToString()
            If String.IsNullOrEmpty(grezzo) Then Return
            If WGCifraturaNativa.EProtetto(grezzo) OrElse WGCifratura.ECifrato(grezzo) OrElse grezzo.StartsWith(MarcatoreCifratura, StringComparison.Ordinal) Then Return
            Dim chiaveRiga As String = riga("ChiaveWG").ToString()
            If String.IsNullOrWhiteSpace(chiaveRiga) Then Return
            _db.AggiornaRecord(NomeTabellaAccount, chiaveRiga, New Dictionary(Of String, Object) From {{nomeColonna, CifraPassword(grezzo)}})
        Catch ex As Exception
            ScriviLog("CLSMail.CifraCampoGoogleEsistente (" & nomeColonna & "): " & ex.ToString())
        End Try
    End Sub

    Public Function MarcaCartellaComeLetta(chiaveAccount As String, nomeCartella As String) As Integer
        Dim marcati As Integer = 0
        Try
            If String.IsNullOrWhiteSpace(chiaveAccount) Then Return 0
            Dim cartellaEffettiva As String = If(String.IsNullOrWhiteSpace(nomeCartella), "INBOX", nomeCartella)
            Dim impostazioni As ImpostazioniAccountMail = OttieniAccount(chiaveAccount)
            If impostazioni Is Nothing Then Return 0

            If Not UsaPop3(impostazioni) Then
                Try
                    Dim client As ImapClient = OttieniClientImapDaCache(chiaveAccount, impostazioni)
                    Try
                        Dim cartella As IMailFolder = client.GetFolder(cartellaEffettiva)
                        cartella.Open(FolderAccess.ReadWrite)
                        Dim daMarcare As IList(Of UniqueId) = cartella.Search(SearchQuery.NotSeen)
                        If daMarcare IsNot Nothing AndAlso daMarcare.Count > 0 Then
                            cartella.Store(daMarcare, New StoreFlagsRequest(StoreAction.Add, MessageFlags.Seen) With {.Silent = True})
                        End If
                        cartella.Close()
                    Finally
                        RestituisciClientImapAllaCache(chiaveAccount, client)
                    End Try
                Catch exImap As Exception
                    ScriviLog("CLSMail.MarcaCartellaComeLetta IMAP (account " & chiaveAccount & ", " & cartellaEffettiva & "): " & exImap.ToString())
                End Try
            End If

            Try
                Dim parametri As New Dictionary(Of String, Object) From {{"@a", chiaveAccount}, {"@c", cartellaEffettiva}}
                Dim nonLetti As DataTable = _db.EseguiQuery(
                    "SELECT COUNT(*) AS Totale FROM " & NomeTabellaMessaggi & " WHERE ChiaveAccount=@a AND Cartella=@c AND Letto=0;", parametri)
                If nonLetti IsNot Nothing AndAlso nonLetti.Rows.Count > 0 Then
                    marcati = Convert.ToInt32(nonLetti.Rows(0)("Totale"))
                End If
                _db.EseguiNonQuery(
                    "UPDATE " & NomeTabellaMessaggi & " SET Letto=1 WHERE ChiaveAccount=@a AND Cartella=@c AND Letto=0;", parametri)
            Catch exDb As Exception
                ScriviLog("CLSMail.MarcaCartellaComeLetta locale (account " & chiaveAccount & ", " & cartellaEffettiva & "): " & exDb.ToString())
            End Try
        Catch ex As Exception
            ScriviLog("CLSMail.MarcaCartellaComeLetta (account " & chiaveAccount & ", " & nomeCartella & "): " & ex.ToString())
            Return -1
        End Try
        Return marcati
    End Function

End Class