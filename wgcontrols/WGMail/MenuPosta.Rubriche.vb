Imports System
Imports System.Collections.Generic
Imports System.Data
Imports WGContatti
Imports wgcontrols

Partial Public Class MenuPosta

    ' Scelta della rubrica in cui cercare i destinatari. Le rubriche possono essere piu' d'una,
    ' comprese quelle locali che non appartengono ad alcun account, quindi prima di proporre i
    ' contatti il programma domanda dove cercare. La scelta vale per tutta la composizione.
    Private Const TutteLeRubriche As String = "__tutte__"

    Private _rubricaDestinatari As String = ""
    Private _rubricaDestinatariScelta As Boolean = False
    Private _testoDestinatarioInAttesa As String = ""
    Private _listaRubricheDestinatari As WGMenuLista
    Private _campiPasswordRubricaPosta As Campi
    Private _rubricaDaSbloccare As String = ""

    Public Sub AzzeraRubricaDestinatari()
        Try
            _rubricaDestinatari = ""
            _rubricaDestinatariScelta = False
        Catch ex As Exception
            log.ScriviLog("MenuPosta.AzzeraRubricaDestinatari: " & ex.Message)
        End Try
    End Sub

    Private Function ElencoRubricheDisponibili() As DataTable
        Try
            Dim rubrica As CLSContatti = Me.Rubrica
            If rubrica Is Nothing Then Return New DataTable()
            Dim tutte As DataTable = rubrica.OttieniRubriche()
            If tutte Is Nothing Then Return New DataTable()

            ' Stessa regola gia' usata per la sincronizzazione (CLSMail.AccountSupportaRubrica):
            ' un account che non puo' sincronizzare la rubrica (es. Google) non deve comparire
            ' nemmeno come posto dove cercare i destinatari.
            For indice As Integer = tutte.Rows.Count - 1 To 0 Step -1
                Try
                    Dim account As String = tutte.Rows(indice)("ChiaveAccount").ToString()
                    If String.IsNullOrWhiteSpace(account) Then Continue For
                    If _posta IsNot Nothing AndAlso Not _posta.AccountSupportaRubrica(account) Then tutte.Rows.RemoveAt(indice)
                Catch exRiga As Exception
                    log.ScriviLog("MenuPosta.ElencoRubricheDisponibili riga: " & exRiga.Message)
                End Try
            Next
            tutte.AcceptChanges()
            Return tutte
        Catch ex As Exception
            log.ScriviLog("MenuPosta.ElencoRubricheDisponibili: " & ex.Message)
            Return New DataTable()
        End Try
    End Function

    Private Sub PreparaRubricaDestinatari(testo As String)
        Try
            _testoDestinatarioInAttesa = If(testo, "")

            If _rubricaDestinatariScelta Then
                MostraRisultatiDestinatario(_testoDestinatarioInAttesa)
                Return
            End If

            Dim rubriche As DataTable = ElencoRubricheDisponibili()
            If rubriche Is Nothing OrElse rubriche.Rows.Count <= 1 Then
                _rubricaDestinatari = ""
                _rubricaDestinatariScelta = True
                MostraRisultatiDestinatario(_testoDestinatarioInAttesa)
                Return
            End If

            AbbandonaListaRubricheDestinatari()
            _listaRubricheDestinatari = New WGMenuLista()
            _listaRubricheDestinatari.multiselezioneConsentita = False
            _listaRubricheDestinatari.impostaTitolo("In quale rubrica cercare il destinatario")
            _listaRubricheDestinatari.aggiungiElemento("Tutte le rubriche disponibili", TutteLeRubriche)

            Dim contatti As CLSContatti = Me.Rubrica
            For Each riga As DataRow In rubriche.Rows
                Try
                    Dim chiave As String = riga("ChiaveWG").ToString()
                    Dim etichetta As String = riga("Nome").ToString()
                    If contatti IsNot Nothing AndAlso contatti.RubricaProtetta(chiave) Then
                        etichetta &= If(contatti.RubricaSbloccata(chiave), ", protetta, aperta", ", protetta, chiusa")
                    End If
                    _listaRubricheDestinatari.aggiungiElemento(etichetta, chiave)
                Catch exRiga As Exception
                    log.ScriviLog("MenuPosta.PreparaRubricaDestinatari riga: " & exRiga.Message)
                End Try
            Next

            AddHandler _listaRubricheDestinatari.ElementoSelezionato, AddressOf RubricaDestinatariScelta
            AddHandler _listaRubricheDestinatari.escPremuto, AddressOf ListaRubricheDestinatariEsc
            _listaRubricheDestinatari.avvia()
        Catch ex As Exception
            log.ScriviLog("MenuPosta.PreparaRubricaDestinatari: " & ex.Message)
        End Try
    End Sub

    Private Sub AbbandonaListaRubricheDestinatari()
        Try
            If _listaRubricheDestinatari Is Nothing Then Return
            Dim precedente As WGMenuLista = _listaRubricheDestinatari
            _listaRubricheDestinatari = Nothing
            Try
                RemoveHandler precedente.ElementoSelezionato, AddressOf RubricaDestinatariScelta
                RemoveHandler precedente.escPremuto, AddressOf ListaRubricheDestinatariEsc
            Catch exHandler As Exception
                log.ScriviLog("MenuPosta.AbbandonaListaRubricheDestinatari handler: " & exHandler.Message)
            End Try
            If Not precedente.IsDisposed Then precedente.abbandona()
        Catch ex As Exception
            log.ScriviLog("MenuPosta.AbbandonaListaRubricheDestinatari: " & ex.Message)
            _listaRubricheDestinatari = Nothing
        End Try
    End Sub

    Private Sub ListaRubricheDestinatariEsc(sender As Object, e As EventArgs)
        Try
            AbbandonaListaRubricheDestinatari()
            ApriComposizioneDestinatario()
        Catch ex As Exception
            log.ScriviLog("MenuPosta.ListaRubricheDestinatariEsc: " & ex.Message)
        End Try
    End Sub

    Private Sub RubricaDestinatariScelta(id As String)
        Try
            If String.IsNullOrWhiteSpace(id) Then Return
            AbbandonaListaRubricheDestinatari()

            If String.Equals(id, TutteLeRubriche, StringComparison.Ordinal) Then
                _rubricaDestinatari = ""
                _rubricaDestinatariScelta = True
                MostraRisultatiDestinatario(_testoDestinatarioInAttesa)
                Return
            End If

            Dim contatti As CLSContatti = Me.Rubrica
            If contatti IsNot Nothing AndAlso contatti.RubricaProtetta(id) AndAlso Not contatti.RubricaSbloccata(id) Then
                ChiediPasswordRubricaPosta(id)
                Return
            End If

            _rubricaDestinatari = id
            _rubricaDestinatariScelta = True
            MostraRisultatiDestinatario(_testoDestinatarioInAttesa)
        Catch ex As Exception
            log.ScriviLog("MenuPosta.RubricaDestinatariScelta: " & ex.Message)
        End Try
    End Sub

    ' ============================================================================================
    ' PASSWORD DELLA RUBRICA CHIESTA DALLA POSTA
    ' ============================================================================================

    Private Sub ChiediPasswordRubricaPosta(chiaveRubrica As String)
        Try
            _rubricaDaSbloccare = If(chiaveRubrica, "")
            Dim nome As String = ""
            Dim contatti As CLSContatti = Me.Rubrica
            If contatti IsNot Nothing Then nome = contatti.NomeRubrica(chiaveRubrica)

            _campiPasswordRubricaPosta = New Campi()
            _campiPasswordRubricaPosta.SalvaSuEscape = False
            _campiPasswordRubricaPosta.Aggiungi("Password della rubrica " & nome, "posta.rubricapassword", True, False, "")
            AddHandler _campiPasswordRubricaPosta.Fine, AddressOf CampiPasswordRubricaPostaFine
            AddHandler _campiPasswordRubricaPosta.Chiusura, AddressOf CampiPasswordRubricaPostaChiusura
            _campiPasswordRubricaPosta.Avvia()
        Catch ex As Exception
            log.ScriviLog("MenuPosta.ChiediPasswordRubricaPosta: " & ex.Message)
        End Try
    End Sub

    Private Sub LiberaCampiPasswordRubricaPosta()
        Try
            If _campiPasswordRubricaPosta Is Nothing Then Return
            RemoveHandler _campiPasswordRubricaPosta.Fine, AddressOf CampiPasswordRubricaPostaFine
            RemoveHandler _campiPasswordRubricaPosta.Chiusura, AddressOf CampiPasswordRubricaPostaChiusura
            _campiPasswordRubricaPosta = Nothing
        Catch
            _campiPasswordRubricaPosta = Nothing
        End Try
    End Sub

    Private Sub CampiPasswordRubricaPostaChiusura(sender As Object, e As EventArgs)
        Try
            LiberaCampiPasswordRubricaPosta()
            PreparaRubricaDestinatari(_testoDestinatarioInAttesa)
        Catch ex As Exception
            log.ScriviLog("MenuPosta.CampiPasswordRubricaPostaChiusura: " & ex.Message)
        End Try
    End Sub

    Private Sub CampiPasswordRubricaPostaFine(sender As Object, e As EventArgs)
        Try
            Dim password As String = ""
            If _campiPasswordRubricaPosta IsNot Nothing Then password = _campiPasswordRubricaPosta.RecuperaValore("posta.rubricapassword")
            LiberaCampiPasswordRubricaPosta()

            Dim contatti As CLSContatti = Me.Rubrica
            If contatti Is Nothing Then Return

            ' La password identifica la rubrica: se appartiene a un'altra, si passa a quella.
            Dim trovata As String = contatti.TrovaRubricaPerPassword(password)
            If String.IsNullOrWhiteSpace(trovata) Then
                MostraMessaggio("Password errata.")
                PreparaRubricaDestinatari(_testoDestinatarioInAttesa)
                Return
            End If

            If Not String.Equals(trovata, _rubricaDaSbloccare, StringComparison.Ordinal) Then
                MostraMessaggio("Questa password apre la rubrica " & contatti.NomeRubrica(trovata) & ".")
            End If

            _rubricaDestinatari = trovata
            _rubricaDestinatariScelta = True
            MostraRisultatiDestinatario(_testoDestinatarioInAttesa)
        Catch ex As Exception
            log.ScriviLog("MenuPosta.CampiPasswordRubricaPostaFine: " & ex.Message)
        End Try
    End Sub

    Private Sub OpzioneCambiaRubricaDestinatari(sender As Object, e As EventArgs)
        Try
            If _listaDestinatari IsNot Nothing Then _listaDestinatari.abbandona()
            _listaDestinatari = Nothing
            _rubricaDestinatariScelta = False
            PreparaRubricaDestinatari(_testoDestinatarioInAttesa)
        Catch ex As Exception
            log.ScriviLog("MenuPosta.OpzioneCambiaRubricaDestinatari: " & ex.Message)
        End Try
    End Sub

End Class