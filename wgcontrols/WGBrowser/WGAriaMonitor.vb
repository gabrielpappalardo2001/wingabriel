Imports System.Collections.Generic
Imports System.Linq
Imports System.Windows.Forms
Imports wgcontrols

' Monitoraggio degli annunci ARIA e delle notifiche dinamiche delle pagine web.
Public Class AnnuncioPagina
    Public Property Istante As DateTime = DateTime.Now
    Public Property Testo As String = ""
    Public Property Tipo As String = ""
    Public Property Pagina As String = ""
End Class

Public Class WGAriaMonitor
    Private ReadOnly log As New Logger()
    Private ReadOnly lettura As sintesivocale = sintesivocale.GetInstance()
    Private ReadOnly paginaWeb As WGPaginaWeb
    Private ReadOnly formPadre As Control

    Private monitorAttivo As Boolean = False
    Private ReadOnly ultimiTesti As New Dictionary(Of String, DateTime)()
    Private ReadOnly lockUltimi As New Object()

    Private Shared ReadOnly _storico As New List(Of AnnuncioPagina)()
    Private Shared ReadOnly _lockStorico As New Object()
    Private Shared _listaStorico As WGMenuLista = Nothing

    Private Const INTERVALLO_RIPETIZIONE_MS As Double = 3000
    Private Const INTERVALLO_DOPPIONI_NUOVI_MS As Double = 6000
    Private Const MASSIMO_STORICO As Integer = 300

    Public Property SchedaAttiva As Func(Of Boolean) = Nothing
    Public Property TitoloPagina As Func(Of String) = Nothing

    Public Sub New(pagina As WGPaginaWeb, controllo As Control)
        paginaWeb = pagina
        formPadre = controllo
    End Sub

    Public ReadOnly Property Attivo As Boolean
        Get
            Return monitorAttivo
        End Get
    End Property

    Public Sub Avvia()
        Try
            monitorAttivo = True
        Catch ex As Exception
            log.ScriviLog("WGAriaMonitor.Avvia: " & ex.Message)
        End Try
    End Sub

    Public Sub Ferma()
        Try
            monitorAttivo = False
            Reset()
        Catch ex As Exception
            log.ScriviLog("WGAriaMonitor.Ferma: " & ex.Message)
        End Try
    End Sub

    Public Sub Reset()
        Try
            SyncLock lockUltimi
                ultimiTesti.Clear()
            End SyncLock
        Catch ex As Exception
            log.ScriviLog("WGAriaMonitor.Reset: " & ex.Message)
        End Try
    End Sub

    Private Function EAttiva() As Boolean
        Try
            If SchedaAttiva Is Nothing Then Return True
            Return SchedaAttiva.Invoke()
        Catch ex As Exception
            log.ScriviLog("WGAriaMonitor.EAttiva: " & ex.Message)
            Return True
        End Try
    End Function

    Private Function NomePagina() As String
        Try
            If TitoloPagina Is Nothing Then Return ""
            Return If(TitoloPagina.Invoke(), "")
        Catch ex As Exception
            log.ScriviLog("WGAriaMonitor.NomePagina: " & ex.Message)
            Return ""
        End Try
    End Function

    Private Function GiaDetto(testo As String, intervalloMs As Double) As Boolean
        Try
            Dim ora As DateTime = DateTime.Now
            SyncLock lockUltimi
                Dim precedente As DateTime
                If ultimiTesti.TryGetValue(testo, precedente) AndAlso (ora - precedente).TotalMilliseconds < intervalloMs Then Return True
                ultimiTesti(testo) = ora
                If ultimiTesti.Count > 300 Then
                    Dim vecchi As List(Of String) = ultimiTesti.Where(Function(c) (ora - c.Value).TotalSeconds > 30).Select(Function(c) c.Key).ToList()
                    For Each chiave As String In vecchi
                        ultimiTesti.Remove(chiave)
                    Next
                    If ultimiTesti.Count > 300 Then ultimiTesti.Clear()
                End If
            End SyncLock
            Return False
        Catch ex As Exception
            log.ScriviLog("WGAriaMonitor.GiaDetto: " & ex.Message)
            Return False
        End Try
    End Function

    Public Function DettoDiRecente(testo As String) As Boolean
        Try
            If String.IsNullOrWhiteSpace(testo) Then Return False
            Dim ora As DateTime = DateTime.Now
            Dim pulito As String = testo.Trim()
            SyncLock lockUltimi
                For Each coppia As KeyValuePair(Of String, DateTime) In ultimiTesti
                    If (ora - coppia.Value).TotalMilliseconds > INTERVALLO_DOPPIONI_NUOVI_MS Then Continue For
                    If coppia.Key.IndexOf(pulito, StringComparison.CurrentCultureIgnoreCase) >= 0 Then Return True
                Next
            End SyncLock
            Return False
        Catch ex As Exception
            log.ScriviLog("WGAriaMonitor.DettoDiRecente: " & ex.Message)
            Return False
        End Try
    End Function

    Public Shared Sub Registra(testo As String, tipo As String, pagina As String)
        Try
            If String.IsNullOrWhiteSpace(testo) Then Return
            SyncLock _lockStorico
                _storico.Add(New AnnuncioPagina() With {.Istante = DateTime.Now, .Testo = testo.Trim(), .Tipo = If(tipo, ""), .Pagina = If(pagina, "")})
                While _storico.Count > MASSIMO_STORICO
                    _storico.RemoveAt(0)
                End While
            End SyncLock
        Catch ex As Exception
            Dim registro As New Logger()
            registro.ScriviLog("WGAriaMonitor.Registra: " & ex.Message)
        End Try
    End Sub

    Public Shared Function Storico() As List(Of AnnuncioPagina)
        Try
            SyncLock _lockStorico
                Return _storico.ToList()
            End SyncLock
        Catch ex As Exception
            Dim registro As New Logger()
            registro.ScriviLog("WGAriaMonitor.Storico: " & ex.Message)
            Return New List(Of AnnuncioPagina)()
        End Try
    End Function

    Private Shared Function NomeTipo(tipo As String) As String
        Select Case tipo
            Case "regione" : Return "annuncio"
            Case "notifica" : Return "notifica"
            Case "nuovo" : Return "nuovo contenuto"
            Case "dialogo" : Return "finestra"
            Case "download" : Return "download"
        End Select
        Return "messaggio"
    End Function

    Public Shared Sub MostraStorico()
        Try
            Dim elenco As List(Of AnnuncioPagina) = Storico()
            Dim lettura As sintesivocale = sintesivocale.GetInstance()
            If elenco.Count = 0 Then
                lettura.LeggiTesto("Nessun annuncio ricevuto dalle pagine")
                Return
            End If
            If _listaStorico IsNot Nothing Then
                Try
                    _listaStorico.abbandona()
                Catch exVecchia As Exception
                    Dim registroVecchia As New Logger()
                    registroVecchia.ScriviLog("WGAriaMonitor.MostraStorico chiusura: " & exVecchia.Message)
                End Try
                _listaStorico = Nothing
            End If
            Dim lista As New WGMenuLista()
            lista.multiselezioneConsentita = False
            lista.impostaTitolo("Annunci delle pagine, dal più recente")
            Dim testi As New Dictionary(Of String, String)()
            Dim indice As Integer = 0
            For i As Integer = elenco.Count - 1 To 0 Step -1
                Dim a As AnnuncioPagina = elenco(i)
                indice += 1
                Dim id As String = "a" & indice.ToString()
                testi(id) = a.Testo
                Dim voce As String = a.Testo & ". " & NomeTipo(a.Tipo) & " delle " & a.Istante.ToString("HH:mm") & If(a.Pagina <> "", ", " & a.Pagina, "")
                lista.aggiungiElemento(voce, id)
            Next
            AddHandler lista.ElementoSelezionato, Sub(id As String)
                                                      Try
                                                          Dim testo As String = Nothing
                                                          If testi.TryGetValue(id, testo) Then
                                                              Clipboard.SetText(testo)
                                                              lettura.LeggiTesto("Copiato negli appunti: " & testo)
                                                          End If
                                                      Catch exScelta As Exception
                                                          Dim registroScelta As New Logger()
                                                          registroScelta.ScriviLog("WGAriaMonitor.MostraStorico scelta: " & exScelta.Message)
                                                      End Try
                                                  End Sub
            lista.aggiungiOpzione("Svuota l'elenco degli annunci", Sub(s, e)
                                                                       Try
                                                                           SyncLock _lockStorico
                                                                               _storico.Clear()
                                                                           End SyncLock
                                                                           lista.abbandona()
                                                                           _listaStorico = Nothing
                                                                           lettura.LeggiTesto("Elenco degli annunci svuotato")
                                                                       Catch exSvuota As Exception
                                                                           Dim registroSvuota As New Logger()
                                                                           registroSvuota.ScriviLog("WGAriaMonitor.MostraStorico svuota: " & exSvuota.Message)
                                                                       End Try
                                                                   End Sub)
            AddHandler lista.escPremuto, Sub(s As Object, e As EventArgs)
                                             Try
                                                 lista.abbandona()
                                                 _listaStorico = Nothing
                                             Catch exEsc As Exception
                                                 Dim registroEsc As New Logger()
                                                 registroEsc.ScriviLog("WGAriaMonitor.MostraStorico esc: " & exEsc.Message)
                                             End Try
                                         End Sub
            _listaStorico = lista
            lista.avvia()
        Catch ex As Exception
            Dim registro As New Logger()
            registro.ScriviLog("WGAriaMonitor.MostraStorico: " & ex.ToString())
        End Try
    End Sub

    Public Sub RiceviNotifica(testo As String, assertivo As Boolean)
        Try
            If Not monitorAttivo Then Return
            If String.IsNullOrWhiteSpace(testo) Then Return
            If GiaDetto(testo, INTERVALLO_RIPETIZIONE_MS) Then Return
            Registra(testo, "regione", NomePagina())
            If Not EAttiva() Then Return
            If assertivo Then
                lettura.LeggiTesto(testo, False, "Alta")
            Else
                lettura.LeggiTesto(testo)
            End If
        Catch ex As Exception
            log.ScriviLog("WGAriaMonitor.RiceviNotifica: " & ex.Message)
        End Try
    End Sub

    Public Sub RiceviDialogo(riga As RigaVirtuale)
        Try
            If Not monitorAttivo OrElse riga Is Nothing Then Return
            Dim testo As String = "Finestra di dialogo"
            If Not String.IsNullOrWhiteSpace(riga.Testo) Then testo &= " " & riga.Testo
            If Not String.IsNullOrWhiteSpace(riga.Descrizione) Then testo &= ". " & riga.Descrizione
            If GiaDetto(testo, INTERVALLO_RIPETIZIONE_MS) Then Return
            Registra(testo, "dialogo", NomePagina())
            If Not EAttiva() Then Return
            lettura.LeggiTesto(testo, False, "Alta")
        Catch ex As Exception
            log.ScriviLog("WGAriaMonitor.RiceviDialogo: " & ex.Message)
        End Try
    End Sub

    Public Sub RiceviNotificaSito(titolo As String, corpo As String, sito As String, Optional parla As Boolean = True)
        Try
            Dim testo As String = If(titolo, "").Trim()
            If Not String.IsNullOrWhiteSpace(corpo) Then testo = If(testo = "", corpo.Trim(), testo & ": " & corpo.Trim())
            If testo = "" Then Return
            If GiaDetto("notifica|" & testo, INTERVALLO_RIPETIZIONE_MS) Then Return
            Dim pagina As String = If(String.IsNullOrWhiteSpace(sito), NomePagina(), sito)
            Registra(testo, "notifica", pagina)
            If Not parla Then Return
            If EAttiva() Then
                lettura.LeggiTesto("Notifica: " & testo)
            Else
                lettura.LeggiTesto("Notifica da " & If(pagina = "", "un'altra scheda", pagina) & ": " & testo)
            End If
        Catch ex As Exception
            log.ScriviLog("WGAriaMonitor.RiceviNotificaSito: " & ex.Message)
        End Try
    End Sub

    Public Function AnnunciaNuoviContenuti(righe As List(Of RigaVirtuale), daEscludere As String) As Integer
        Try
            If Not monitorAttivo OrElse righe Is Nothing OrElse righe.Count = 0 Then Return 0
            Dim testi As New List(Of String)()
            For Each r As RigaVirtuale In righe
                Dim testo As String = If(r.Ruolo = "text", r.Testo, r.TestoAnnuncio)
                If String.IsNullOrWhiteSpace(testo) Then Continue For
                testo = testo.Trim()
                If Not String.IsNullOrWhiteSpace(daEscludere) AndAlso testo.IndexOf(daEscludere.Trim(), StringComparison.CurrentCultureIgnoreCase) >= 0 Then Continue For
                If DettoDiRecente(testo) Then Continue For
                If testi.Contains(testo) Then Continue For
                If testo.Length > 300 Then testo = testo.Substring(0, 300)
                testi.Add(testo)
            Next
            If testi.Count = 0 Then Return 0
            Dim pagina As String = NomePagina()
            For Each testo As String In testi
                Registra(testo, "nuovo", pagina)
                GiaDetto(testo, INTERVALLO_DOPPIONI_NUOVI_MS)
            Next
            If Not EAttiva() Then Return testi.Count
            Dim annuncio As String = String.Join(". ", testi.Take(3))
            If testi.Count > 3 Then annuncio &= ". E altri " & (testi.Count - 3) & " elementi nuovi, Shift F8 per l'elenco"
            lettura.LeggiTesto(annuncio)
            Return testi.Count
        Catch ex As Exception
            log.ScriviLog("WGAriaMonitor.AnnunciaNuoviContenuti: " & ex.Message)
            Return 0
        End Try
    End Function
End Class
