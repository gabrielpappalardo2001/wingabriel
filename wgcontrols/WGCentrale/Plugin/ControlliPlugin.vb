Imports System.Collections.Generic
Imports wgcontrols
Imports WGPlugin

' ============================================================================================
' I controlli di WinGabriel (WGMenu, WGMenuLista, WGCampo, Campi, WGGriglia) offerti ai plugin.
'
' Ogni oggetto si costruisce senza toccare l'interfaccia: le operazioni fatte prima di Mostra restano in memoria e il controllo vero
' si crea in Mostra, sul thread dell'interfaccia. Dopo Mostra le operazioni vanno al controllo aperto. Le azioni del plugin passano
' sempre da ServiziPlugin.Protetta: un errore del plugin finisce nel registro e non arriva a WinGabriel.
' ============================================================================================

Friend MustInherit Class ControlloPlugin(Of T As Class)

    Protected ReadOnly Servizi As ServiziPlugin
    Protected Reale As T
    Private ReadOnly _pendenti As New List(Of Action(Of T))()
    Private ReadOnly _blocco As New Object()

    Protected Sub New(servizi As ServiziPlugin)
        Me.Servizi = servizi
    End Sub

    Protected MustOverride Function Crea() As T
    Protected MustOverride Sub Collega(reale As T)
    Protected MustOverride Sub Avvia(reale As T)
    Protected MustOverride Sub ChiudiReale(reale As T)

    ''' <summary>Esegue l'operazione sul controllo aperto, oppure la tiene da parte fino a Mostra.</summary>
    Protected Sub Applica(operazione As Action(Of T), origine As String)
        Try
            Dim aperto As Boolean
            SyncLock _blocco
                aperto = Reale IsNot Nothing
                If Not aperto Then _pendenti.Add(operazione)
            End SyncLock
            If aperto Then
                ServiziPlugin.SuInterfaccia(Sub() Esegui(operazione, origine))
            End If
        Catch ex As Exception
            Servizi.ScriviErrore(origine, ex)
        End Try
    End Sub

    Private Sub Esegui(operazione As Action(Of T), origine As String)
        Try
            operazione.Invoke(Reale)
        Catch ex As Exception
            Servizi.ScriviErrore(origine, ex)
        End Try
    End Sub

    Public Sub Mostra()
        Try
            ' un plugin non può aprire controlli di continuo: al massimo 20 al minuto
            If Not Servizi.Consenti("controlli", 20) Then Return
            ServiziPlugin.SuInterfaccia(Sub()
                                            Try
                                                If Not Servizi.Attivo Then Return
                                                Dim nuovo As T = Crea()
                                                Dim tenute As List(Of Action(Of T))
                                                SyncLock _blocco
                                                    tenute = New List(Of Action(Of T))(_pendenti)
                                                    _pendenti.Clear()
                                                    Reale = nuovo
                                                End SyncLock
                                                For Each operazione As Action(Of T) In tenute
                                                    Esegui(operazione, "ControlloPlugin.Mostra (operazione)")
                                                Next
                                                Collega(nuovo)
                                                Avvia(nuovo)
                                            Catch ex As Exception
                                                Servizi.ScriviErrore("ControlloPlugin.Mostra", ex)
                                            End Try
                                        End Sub)
        Catch ex As Exception
            Servizi.ScriviErrore("ControlloPlugin.Mostra (esterno)", ex)
        End Try
    End Sub

    Public Sub Chiudi()
        Try
            Dim aperto As T
            SyncLock _blocco
                aperto = Reale
            End SyncLock
            If aperto Is Nothing Then Return
            ServiziPlugin.SuInterfaccia(Sub()
                                            Try
                                                ChiudiReale(aperto)
                                            Catch ex As Exception
                                                Servizi.ScriviErrore("ControlloPlugin.Chiudi", ex)
                                            End Try
                                        End Sub)
        Catch ex As Exception
            Servizi.ScriviErrore("ControlloPlugin.Chiudi (esterno)", ex)
        End Try
    End Sub

    ''' <summary>Un'azione del plugin come gestore di evento di WinGabriel.</summary>
    Protected Function ComeGestore(azione As Action, origine As String) As EventHandler
        Dim protetta As Action = Servizi.Protetta(azione, origine)
        Return Sub(mittente As Object, e As EventArgs) protetta.Invoke()
    End Function

End Class

''' <summary>Il menu di WinGabriel (WGMenu) per i plugin.</summary>
Friend NotInheritable Class MenuPluginImpl
    Inherits ControlloPlugin(Of WGMenu)
    Implements IWGMenuPlugin

    Private ReadOnly _titolo As String

    Public Sub New(servizi As ServiziPlugin, titolo As String)
        MyBase.New(servizi)
        _titolo = If(titolo, "")
    End Sub

    Protected Overrides Function Crea() As WGMenu
        Return New WGMenu()
    End Function

    Protected Overrides Sub Collega(reale As WGMenu)
    End Sub

    Protected Overrides Sub Avvia(reale As WGMenu)
        reale.avvia(_titolo)
    End Sub

    Protected Overrides Sub ChiudiReale(reale As WGMenu)
        reale.chiudi()
    End Sub

    Public Sub Aggiungi(testo As String, azione As Action) Implements IWGMenuPlugin.Aggiungi
        Dim gestore As EventHandler = ComeGestore(azione, "MenuPlugin.voce (plugin)")
        Applica(Sub(m As WGMenu) m.aggiungi(testo, gestore), "MenuPlugin.Aggiungi")
    End Sub

    Public Sub AggiungiDopo(vocePrecedente As String, testo As String, azione As Action) Implements IWGMenuPlugin.AggiungiDopo
        Dim gestore As EventHandler = ComeGestore(azione, "MenuPlugin.voce (plugin)")
        Applica(Sub(m As WGMenu) m.AggiungiDopo(vocePrecedente, testo, gestore), "MenuPlugin.AggiungiDopo")
    End Sub

    Public Sub AggiungiOpzione(testo As String, azione As Action) Implements IWGMenuPlugin.AggiungiOpzione
        Dim gestore As EventHandler = ComeGestore(azione, "MenuPlugin.opzione (plugin)")
        Applica(Sub(m As WGMenu) m.aggiungiOpzione(testo, gestore), "MenuPlugin.AggiungiOpzione")
    End Sub

    Public Sub RimuoviVoce(testo As String) Implements IWGMenuPlugin.RimuoviVoce
        Applica(Sub(m As WGMenu) m.RimuoviVoce(testo), "MenuPlugin.RimuoviVoce")
    End Sub

    Public Sub RimuoviOpzione(testo As String) Implements IWGMenuPlugin.RimuoviOpzione
        Applica(Sub(m As WGMenu) m.RimuoviOpzione(testo), "MenuPlugin.RimuoviOpzione")
    End Sub

    Public Sub RinominaVoce(testoAttuale As String, nuovoTesto As String) Implements IWGMenuPlugin.RinominaVoce
        Applica(Sub(m As WGMenu) m.RinominaVoce(testoAttuale, nuovoTesto), "MenuPlugin.RinominaVoce")
    End Sub

    Public Sub Posiziona(indice As Integer) Implements IWGMenuPlugin.Posiziona
        Applica(Sub(m As WGMenu) m.posiziona(indice), "MenuPlugin.Posiziona")
    End Sub

    Private Sub IWGMenuPlugin_Mostra() Implements IWGMenuPlugin.Mostra
        Mostra()
    End Sub

    Private Sub IWGMenuPlugin_Chiudi() Implements IWGMenuPlugin.Chiudi
        Chiudi()
    End Sub

End Class

''' <summary>
''' Un menu di WinGabriel già esistente (sta per aprirsi), visto dal plugin: le modifiche vanno subito al menu. Mostra non fa niente
''' (il menu lo apre WinGabriel); Chiudi lo chiude.
''' </summary>
Friend NotInheritable Class MenuEsistenteImpl
    Implements IWGMenuPlugin

    Private ReadOnly _servizi As ServiziPlugin
    Private ReadOnly _menu As WGMenu

    Public Sub New(servizi As ServiziPlugin, menu As WGMenu)
        _servizi = servizi
        _menu = menu
    End Sub

    Private Function Gestore(azione As Action, origine As String) As EventHandler
        Dim protetta As Action = _servizi.Protetta(azione, origine)
        Return Sub(mittente As Object, e As EventArgs) protetta.Invoke()
    End Function

    Public Sub Aggiungi(testo As String, azione As Action) Implements IWGMenuPlugin.Aggiungi
        Try
            If Not _servizi.Attivo Then Return
            _menu.aggiungi(testo, Gestore(azione, "MenuEsistente.voce (plugin)"))
            _servizi.RicordaVoceInMenu(_menu, testo, False)
        Catch ex As Exception
            _servizi.ScriviErrore("MenuEsistente.Aggiungi", ex)
        End Try
    End Sub

    Public Sub AggiungiDopo(vocePrecedente As String, testo As String, azione As Action) Implements IWGMenuPlugin.AggiungiDopo
        Try
            If Not _servizi.Attivo Then Return
            _menu.AggiungiDopo(vocePrecedente, testo, Gestore(azione, "MenuEsistente.voce (plugin)"))
            _servizi.RicordaVoceInMenu(_menu, testo, False)
        Catch ex As Exception
            _servizi.ScriviErrore("MenuEsistente.AggiungiDopo", ex)
        End Try
    End Sub

    Public Sub AggiungiOpzione(testo As String, azione As Action) Implements IWGMenuPlugin.AggiungiOpzione
        Try
            If Not _servizi.Attivo Then Return
            _menu.aggiungiOpzione(testo, Gestore(azione, "MenuEsistente.opzione (plugin)"))
            _servizi.RicordaVoceInMenu(_menu, testo, True)
        Catch ex As Exception
            _servizi.ScriviErrore("MenuEsistente.AggiungiOpzione", ex)
        End Try
    End Sub

    Public Sub RimuoviVoce(testo As String) Implements IWGMenuPlugin.RimuoviVoce
        Try
            If Not _servizi.Attivo Then Return
            _menu.RimuoviVoce(testo)
        Catch ex As Exception
            _servizi.ScriviErrore("MenuEsistente.RimuoviVoce", ex)
        End Try
    End Sub

    Public Sub RimuoviOpzione(testo As String) Implements IWGMenuPlugin.RimuoviOpzione
        Try
            If Not _servizi.Attivo Then Return
            _menu.RimuoviOpzione(testo)
        Catch ex As Exception
            _servizi.ScriviErrore("MenuEsistente.RimuoviOpzione", ex)
        End Try
    End Sub

    Public Sub RinominaVoce(testoAttuale As String, nuovoTesto As String) Implements IWGMenuPlugin.RinominaVoce
        Try
            If Not _servizi.Attivo Then Return
            _menu.RinominaVoce(testoAttuale, nuovoTesto)
        Catch ex As Exception
            _servizi.ScriviErrore("MenuEsistente.RinominaVoce", ex)
        End Try
    End Sub

    Public Sub Posiziona(indice As Integer) Implements IWGMenuPlugin.Posiziona
        Try
            If Not _servizi.Attivo Then Return
            _menu.posiziona(indice)
        Catch ex As Exception
            _servizi.ScriviErrore("MenuEsistente.Posiziona", ex)
        End Try
    End Sub

    Public Sub Mostra() Implements IWGMenuPlugin.Mostra
        ' Il menu lo apre WinGabriel: qui non c'è niente da fare.
    End Sub

    Public Sub Chiudi() Implements IWGMenuPlugin.Chiudi
        Try
            If Not _servizi.Attivo Then Return
            _menu.chiudi()
        Catch ex As Exception
            _servizi.ScriviErrore("MenuEsistente.Chiudi", ex)
        End Try
    End Sub

End Class

''' <summary>L'elenco con identificatori di WinGabriel (WGMenuLista) per i plugin.</summary>
Friend NotInheritable Class ElencoPluginImpl
    Inherits ControlloPlugin(Of WGMenuLista)
    Implements IWGElencoPlugin

    Private ReadOnly _titolo As String
    Private _multiplo As Boolean
    Private _alScelto As Action(Of String)
    Private _alleScelte As Action(Of IList(Of String))

    Public Sub New(servizi As ServiziPlugin, titolo As String)
        MyBase.New(servizi)
        _titolo = If(titolo, "")
    End Sub

    Public Property Multiplo As Boolean Implements IWGElencoPlugin.Multiplo
        Get
            Return _multiplo
        End Get
        Set(value As Boolean)
            _multiplo = value
        End Set
    End Property

    Public Property AlScelto As Action(Of String) Implements IWGElencoPlugin.AlScelto
        Get
            Return _alScelto
        End Get
        Set(value As Action(Of String))
            _alScelto = value
        End Set
    End Property

    Public Property AlleScelte As Action(Of IList(Of String)) Implements IWGElencoPlugin.AlleScelte
        Get
            Return _alleScelte
        End Get
        Set(value As Action(Of IList(Of String)))
            _alleScelte = value
        End Set
    End Property

    Public ReadOnly Property IdCorrente As String Implements IWGElencoPlugin.IdCorrente
        Get
            Try
                Dim aperta As WGMenuLista = Reale
                If aperta Is Nothing Then Return ""
                Return If(aperta.ValoreSelezionato, "")
            Catch ex As Exception
                Servizi.ScriviErrore("ElencoPlugin.IdCorrente", ex)
                Return ""
            End Try
        End Get
    End Property

    Protected Overrides Function Crea() As WGMenuLista
        Dim lista As New WGMenuLista()
        lista.multiselezioneConsentita = _multiplo
        lista.impostaTitolo(_titolo)
        Return lista
    End Function

    Protected Overrides Sub Collega(reale As WGMenuLista)
        AddHandler reale.ElementoSelezionato, Sub(id As String)
                                                  Try
                                                      Dim azione As Action(Of String) = _alScelto
                                                      If azione Is Nothing OrElse Not Servizi.Attivo Then Return
                                                      azione.Invoke(id)
                                                  Catch ex As Exception
                                                      Servizi.ScriviErrore("ElencoPlugin.AlScelto (plugin)", ex)
                                                      Servizi.MostraErroreAzione()
                                                  End Try
                                              End Sub
        AddHandler reale.ElementiSelezionati, Sub(ids As List(Of String))
                                                  Try
                                                      Dim azione As Action(Of IList(Of String)) = _alleScelte
                                                      If azione Is Nothing OrElse Not Servizi.Attivo Then Return
                                                      azione.Invoke(If(ids, New List(Of String)()))
                                                  Catch ex As Exception
                                                      Servizi.ScriviErrore("ElencoPlugin.AlleScelte (plugin)", ex)
                                                      Servizi.MostraErroreAzione()
                                                  End Try
                                              End Sub
    End Sub

    Protected Overrides Sub Avvia(reale As WGMenuLista)
        reale.avvia()
    End Sub

    Protected Overrides Sub ChiudiReale(reale As WGMenuLista)
        reale.abbandona()
    End Sub

    Public Sub Aggiungi(id As String, testo As String) Implements IWGElencoPlugin.Aggiungi
        Applica(Sub(l As WGMenuLista) l.aggiungiElemento(testo, id), "ElencoPlugin.Aggiungi")
    End Sub

    Public Sub AggiungiDopo(idPrecedente As String, id As String, testo As String) Implements IWGElencoPlugin.AggiungiDopo
        Applica(Sub(l As WGMenuLista) l.AggiungiElementoDopo(idPrecedente, testo, id), "ElencoPlugin.AggiungiDopo")
    End Sub

    Public Sub Rimuovi(id As String) Implements IWGElencoPlugin.Rimuovi
        Applica(Sub(l As WGMenuLista) l.RimuoviElemento(id), "ElencoPlugin.Rimuovi")
    End Sub

    Public Sub AggiornaTesto(id As String, nuovoTesto As String) Implements IWGElencoPlugin.AggiornaTesto
        Applica(Sub(l As WGMenuLista) l.AggiornaTestoElemento(id, nuovoTesto), "ElencoPlugin.AggiornaTesto")
    End Sub

    Public Sub Svuota() Implements IWGElencoPlugin.Svuota
        Applica(Sub(l As WGMenuLista) l.svuotaLista(), "ElencoPlugin.Svuota")
    End Sub

    Public Sub Posiziona(id As String) Implements IWGElencoPlugin.Posiziona
        Applica(Sub(l As WGMenuLista) l.posiziona(id), "ElencoPlugin.Posiziona")
    End Sub

    Public Sub AggiungiOpzione(testo As String, azione As Action) Implements IWGElencoPlugin.AggiungiOpzione
        Dim gestore As EventHandler = ComeGestore(azione, "ElencoPlugin.opzione (plugin)")
        Applica(Sub(l As WGMenuLista) l.aggiungiOpzione(testo, gestore), "ElencoPlugin.AggiungiOpzione")
    End Sub

    Public Sub RimuoviOpzione(testo As String) Implements IWGElencoPlugin.RimuoviOpzione
        Applica(Sub(l As WGMenuLista) l.rimuoviOpzione(testo), "ElencoPlugin.RimuoviOpzione")
    End Sub

    Public Sub AggiungiOpzioneSuVoce(testo As String, azione As Action(Of String)) Implements IWGElencoPlugin.AggiungiOpzioneSuVoce
        If azione Is Nothing Then Return
        Dim sulla As Action = Sub()
                                  Dim id As String = IdCorrente
                                  If id <> "" Then azione.Invoke(id)
                              End Sub
        AggiungiOpzione(testo, sulla)
    End Sub

    Private Sub IWGElencoPlugin_Mostra() Implements IWGElencoPlugin.Mostra
        Mostra()
    End Sub

    Private Sub IWGElencoPlugin_Chiudi() Implements IWGElencoPlugin.Chiudi
        Chiudi()
    End Sub

End Class

''' <summary>Il campo di testo grande di WinGabriel (WGCampo) per i plugin.</summary>
Friend NotInheritable Class CampoPluginImpl
    Inherits ControlloPlugin(Of WGCampo)
    Implements IWGCampoPlugin

    Private ReadOnly _titolo As String
    Private ReadOnly _testoIniziale As String
    Private ReadOnly _soloLettura As Boolean
    Private ReadOnly _comeHtml As Boolean
    Private _testoFinale As String
    Private _allaChiusura As Action
    Private _chiuso As Boolean

    Public Sub New(servizi As ServiziPlugin, titolo As String, testo As String, soloLettura As Boolean, comeHtml As Boolean)
        MyBase.New(servizi)
        _titolo = If(titolo, "")
        _testoIniziale = If(testo, "")
        _soloLettura = soloLettura
        _comeHtml = comeHtml
        _testoFinale = _testoIniziale
    End Sub

    Public ReadOnly Property Testo As String Implements IWGCampoPlugin.Testo
        Get
            Try
                Dim aperto As WGCampo = Reale
                If aperto Is Nothing OrElse _chiuso Then Return _testoFinale
                Return If(aperto.ottienitesto(), "")
            Catch ex As Exception
                Servizi.ScriviErrore("CampoPlugin.Testo", ex)
                Return _testoFinale
            End Try
        End Get
    End Property

    Public Property AllaChiusura As Action Implements IWGCampoPlugin.AllaChiusura
        Get
            Return _allaChiusura
        End Get
        Set(value As Action)
            _allaChiusura = value
        End Set
    End Property

    Protected Overrides Function Crea() As WGCampo
        Dim campo As New WGCampo()
        If _comeHtml Then
            campo.CaricaHtml(_titolo, _testoIniziale)
        Else
            campo.init(_titolo, _testoIniziale, True, _soloLettura)
        End If
        Return campo
    End Function

    Protected Overrides Sub Collega(reale As WGCampo)
        AddHandler reale.chiusura, Sub(mittente As Object, e As EventArgs)
                                       Try
                                           If _chiuso Then Return
                                           _chiuso = True
                                           Try
                                               _testoFinale = If(reale.ottienitesto(), "")
                                           Catch exTesto As Exception
                                               Servizi.ScriviErrore("CampoPlugin.chiusura (testo)", exTesto)
                                           End Try
                                           Dim azione As Action = _allaChiusura
                                           If azione IsNot Nothing AndAlso Servizi.Attivo Then azione.Invoke()
                                       Catch ex As Exception
                                           Servizi.ScriviErrore("CampoPlugin.AllaChiusura (plugin)", ex)
                                           Servizi.MostraErroreAzione()
                                       End Try
                                   End Sub
    End Sub

    Protected Overrides Sub Avvia(reale As WGCampo)
        reale.avvia()
    End Sub

    Protected Overrides Sub ChiudiReale(reale As WGCampo)
        reale.chiudi()
    End Sub

    Public Sub AggiungiOpzione(testo As String, azione As Action) Implements IWGCampoPlugin.AggiungiOpzione
        Dim gestore As EventHandler = ComeGestore(azione, "CampoPlugin.opzione (plugin)")
        Applica(Sub(c As WGCampo) c.aggiungiopzione(testo, gestore), "CampoPlugin.AggiungiOpzione")
    End Sub

    Private Sub IWGCampoPlugin_Mostra() Implements IWGCampoPlugin.Mostra
        Mostra()
    End Sub

    Private Sub IWGCampoPlugin_Chiudi() Implements IWGCampoPlugin.Chiudi
        Chiudi()
    End Sub

End Class

''' <summary>Il modulo con più campi di WinGabriel (Campi) per i plugin.</summary>
Friend NotInheritable Class CampiPluginImpl
    Inherits ControlloPlugin(Of Campi)
    Implements IWGCampiPlugin

    Private _alConferma As Action
    Private _allaChiusura As Action
    Private _alCampoConfermato As Action(Of String)

    Public Sub New(servizi As ServiziPlugin)
        MyBase.New(servizi)
    End Sub

    Public Property AlConferma As Action Implements IWGCampiPlugin.AlConferma
        Get
            Return _alConferma
        End Get
        Set(value As Action)
            _alConferma = value
        End Set
    End Property

    Public Property AllaChiusura As Action Implements IWGCampiPlugin.AllaChiusura
        Get
            Return _allaChiusura
        End Get
        Set(value As Action)
            _allaChiusura = value
        End Set
    End Property

    Public Property AlCampoConfermato As Action(Of String) Implements IWGCampiPlugin.AlCampoConfermato
        Get
            Return _alCampoConfermato
        End Get
        Set(value As Action(Of String))
            _alCampoConfermato = value
        End Set
    End Property

    Protected Overrides Function Crea() As Campi
        Return New Campi()
    End Function

    Private Sub Chiama(azione As Action, origine As String)
        Try
            If azione Is Nothing OrElse Not Servizi.Attivo Then Return
            azione.Invoke()
        Catch ex As Exception
            Servizi.ScriviErrore(origine, ex)
            Servizi.MostraErroreAzione()
        End Try
    End Sub

    Protected Overrides Sub Collega(reale As Campi)
        AddHandler reale.Fine, Sub(mittente As Object, e As EventArgs) Chiama(_alConferma, "CampiPlugin.AlConferma (plugin)")
        AddHandler reale.Chiusura, Sub(mittente As Object, e As EventArgs) Chiama(_allaChiusura, "CampiPlugin.AllaChiusura (plugin)")
        AddHandler reale.CampoConfermato, Sub(id As String)
                                              Try
                                                  Dim azione As Action(Of String) = _alCampoConfermato
                                                  If azione Is Nothing OrElse Not Servizi.Attivo Then Return
                                                  azione.Invoke(id)
                                              Catch ex As Exception
                                                  Servizi.ScriviErrore("CampiPlugin.AlCampoConfermato (plugin)", ex)
                                                  Servizi.MostraErroreAzione()
                                              End Try
                                          End Sub
    End Sub

    Protected Overrides Sub Avvia(reale As Campi)
        reale.Avvia()
    End Sub

    Protected Overrides Sub ChiudiReale(reale As Campi)
        reale.Chiudi()
    End Sub

    Public Sub AggiungiTesto(titolo As String, id As String, Optional obbligatorio As Boolean = False, Optional soloLettura As Boolean = False, Optional predefinito As String = "") Implements IWGCampiPlugin.AggiungiTesto
        Applica(Sub(c As Campi) c.Aggiungi(titolo, id, obbligatorio, soloLettura, predefinito), "CampiPlugin.AggiungiTesto")
    End Sub

    Public Sub AggiungiNumero(titolo As String, id As String, Optional obbligatorio As Boolean = False, Optional soloLettura As Boolean = False, Optional predefinito As String = "") Implements IWGCampiPlugin.AggiungiNumero
        Applica(Sub(c As Campi) c.AggiungiNumerico(titolo, id, obbligatorio, soloLettura, predefinito), "CampiPlugin.AggiungiNumero")
    End Sub

    Public Sub AggiungiTestoLungo(titolo As String, id As String, Optional obbligatorio As Boolean = False, Optional soloLettura As Boolean = False, Optional predefinito As String = "") Implements IWGCampiPlugin.AggiungiTestoLungo
        Applica(Sub(c As Campi) c.AggiungiMultiriga(titolo, id, obbligatorio, soloLettura, predefinito), "CampiPlugin.AggiungiTestoLungo")
    End Sub

    Public Sub AggiungiPassword(titolo As String, id As String, Optional obbligatorio As Boolean = False, Optional soloLettura As Boolean = False, Optional predefinito As String = "") Implements IWGCampiPlugin.AggiungiPassword
        Applica(Sub(c As Campi) c.AggiungiPassword(titolo, id, obbligatorio, soloLettura, predefinito), "CampiPlugin.AggiungiPassword")
    End Sub

    Public Sub AggiungiData(titolo As String, id As String, Optional obbligatorio As Boolean = False, Optional soloLettura As Boolean = False, Optional predefinito As String = "") Implements IWGCampiPlugin.AggiungiData
        Applica(Sub(c As Campi) c.AggiungiData(titolo, id, obbligatorio, soloLettura, predefinito), "CampiPlugin.AggiungiData")
    End Sub

    Public Sub AggiungiOrario(titolo As String, id As String, Optional obbligatorio As Boolean = False, Optional soloLettura As Boolean = False, Optional predefinito As String = "") Implements IWGCampiPlugin.AggiungiOrario
        Applica(Sub(c As Campi) c.AggiungiOrario(titolo, id, obbligatorio, soloLettura, predefinito), "CampiPlugin.AggiungiOrario")
    End Sub

    Public Sub AggiungiMultiplo(titolo As String, id As String, Optional obbligatorio As Boolean = True, Optional limite As Integer = 0) Implements IWGCampiPlugin.AggiungiMultiplo
        Applica(Sub(c As Campi) c.AggiungiMulti(titolo, id, obbligatorio, limite), "CampiPlugin.AggiungiMultiplo")
    End Sub

    Public Function Valore(id As String) As String Implements IWGCampiPlugin.Valore
        Try
            Dim aperto As Campi = Reale
            If aperto Is Nothing Then Return ""
            Return If(aperto.RecuperaValore(id), "")
        Catch ex As Exception
            Servizi.ScriviErrore("CampiPlugin.Valore", ex)
            Return ""
        End Try
    End Function

    Public Function Valori(id As String) As IList(Of String) Implements IWGCampiPlugin.Valori
        Try
            Dim aperto As Campi = Reale
            If aperto Is Nothing Then Return New List(Of String)()
            Return If(aperto.RecuperaListaValori(id), New List(Of String)())
        Catch ex As Exception
            Servizi.ScriviErrore("CampiPlugin.Valori", ex)
            Return New List(Of String)()
        End Try
    End Function

    Public Function ObbligatoriCompilati() As Boolean Implements IWGCampiPlugin.ObbligatoriCompilati
        Try
            Dim aperto As Campi = Reale
            If aperto Is Nothing Then Return False
            Return aperto.TuttiObbligatoriRiempiti()
        Catch ex As Exception
            Servizi.ScriviErrore("CampiPlugin.ObbligatoriCompilati", ex)
            Return False
        End Try
    End Function

    Public Sub AggiornaTesto(id As String, nuovoTesto As String) Implements IWGCampiPlugin.AggiornaTesto
        Applica(Sub(c As Campi) c.AggiornaTesto(id, nuovoTesto), "CampiPlugin.AggiornaTesto")
    End Sub

    Public Sub SelezionaCampo(id As String) Implements IWGCampiPlugin.SelezionaCampo
        Applica(Sub(c As Campi) c.SelezionaCampo(id), "CampiPlugin.SelezionaCampo")
    End Sub

    Public Sub AggiungiOpzione(testo As String, azione As Action) Implements IWGCampiPlugin.AggiungiOpzione
        Dim gestore As EventHandler = ComeGestore(azione, "CampiPlugin.opzione (plugin)")
        Applica(Sub(c As Campi) c.AggiungiOpzione(testo, gestore), "CampiPlugin.AggiungiOpzione")
    End Sub

    Public Sub RimuoviOpzione(testo As String) Implements IWGCampiPlugin.RimuoviOpzione
        Applica(Sub(c As Campi) c.RimuoviOpzione(testo), "CampiPlugin.RimuoviOpzione")
    End Sub

    Private Sub IWGCampiPlugin_Mostra() Implements IWGCampiPlugin.Mostra
        Mostra()
    End Sub

    Private Sub IWGCampiPlugin_Chiudi() Implements IWGCampiPlugin.Chiudi
        Chiudi()
    End Sub

End Class

''' <summary>La griglia di WinGabriel (WGGriglia) per i plugin.</summary>
Friend NotInheritable Class GrigliaPluginImpl
    Inherits ControlloPlugin(Of WGGriglia)
    Implements IWGGrigliaPlugin

    Private ReadOnly _titolo As String
    Private ReadOnly _voci As List(Of String)
    Private _alScelto As Action(Of Integer)

    Public Sub New(servizi As ServiziPlugin, titolo As String, voci As IList(Of String))
        MyBase.New(servizi)
        _titolo = If(titolo, "")
        _voci = If(voci IsNot Nothing, New List(Of String)(voci), New List(Of String)())
    End Sub

    Public Property AlScelto As Action(Of Integer) Implements IWGGrigliaPlugin.AlScelto
        Get
            Return _alScelto
        End Get
        Set(value As Action(Of Integer))
            _alScelto = value
        End Set
    End Property

    Public ReadOnly Property IndiceCorrente As Integer Implements IWGGrigliaPlugin.IndiceCorrente
        Get
            Try
                Dim aperta As WGGriglia = Reale
                If aperta Is Nothing Then Return 0
                Return aperta.IndiceCorrente
            Catch ex As Exception
                Servizi.ScriviErrore("GrigliaPlugin.IndiceCorrente", ex)
                Return 0
            End Try
        End Get
    End Property

    Protected Overrides Function Crea() As WGGriglia
        Return New WGGriglia(_titolo, _voci)
    End Function

    Protected Overrides Sub Collega(reale As WGGriglia)
        AddHandler reale.ElementoScelto, Sub(indice As Integer)
                                             Try
                                                 Dim azione As Action(Of Integer) = _alScelto
                                                 If azione Is Nothing OrElse Not Servizi.Attivo Then Return
                                                 azione.Invoke(indice)
                                             Catch ex As Exception
                                                 Servizi.ScriviErrore("GrigliaPlugin.AlScelto (plugin)", ex)
                                                 Servizi.MostraErroreAzione()
                                             End Try
                                         End Sub
        ' Con Esc la griglia si chiude, come ogni altra schermata di WinGabriel.
        AddHandler reale.EscPremuto, Sub()
                                         Try
                                             reale.Chiudi()
                                         Catch ex As Exception
                                             Servizi.ScriviErrore("GrigliaPlugin.EscPremuto", ex)
                                         End Try
                                     End Sub
    End Sub

    Protected Overrides Sub Avvia(reale As WGGriglia)
        reale.Avvia()
    End Sub

    Protected Overrides Sub ChiudiReale(reale As WGGriglia)
        reale.Chiudi()
    End Sub

    Public Sub AggiornaVoce(indice As Integer, nuovoTesto As String) Implements IWGGrigliaPlugin.AggiornaVoce
        Applica(Sub(g As WGGriglia) g.AggiornaVoce(indice, nuovoTesto), "GrigliaPlugin.AggiornaVoce")
    End Sub

    Private Sub IWGGrigliaPlugin_Mostra() Implements IWGGrigliaPlugin.Mostra
        Mostra()
    End Sub

    Private Sub IWGGrigliaPlugin_Chiudi() Implements IWGGrigliaPlugin.Chiudi
        Chiudi()
    End Sub

End Class
