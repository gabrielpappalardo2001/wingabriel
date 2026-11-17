Imports System.Collections.Generic
Imports System.ComponentModel
Imports System.Drawing
Imports System.Drawing.Drawing2D
Imports System.Globalization
Imports System.Linq
Imports System.Reflection
Imports System.Runtime.InteropServices
Imports System.Text.RegularExpressions
Imports System.Threading
Imports System.Threading.Tasks
Imports System.Windows.Forms
Imports Microsoft.VisualBasic
Imports wgcontrols
Imports WGStampante

Friend Enum TipoCampo
    Alfanumerico
    Numerico
    Password
    Data
    Orario
    CartaDiCredito
    CVC
    DataScadenza
    Multiriga
    Host
    Word
End Enum

Partial Public Class Campi


    Private _motoreGrafico As CampiFormInterno
    Private _disposto As Boolean = False
    Private _log As New wgcontrols.Logger()

    Public Event Chiusura As EventHandler
    Public Event Fine As EventHandler
    Public Event CampoConfermato(id As String)

    Public Sub New()
        Try
            _motoreGrafico = New CampiFormInterno()
            AddHandler _motoreGrafico.EventoChiusura, AddressOf GestisciEventoChiusura
            AddHandler _motoreGrafico.EventoFine, AddressOf GestisciEventoFine
            AddHandler _motoreGrafico.EventoCampoConfermato, AddressOf GestisciEventoCampoConfermato
        Catch ex As Exception
            _log.ScriviLog("Campi.New: " & ex.Message)
        End Try
    End Sub

    Private Sub GestisciEventoChiusura(sender As Object, e As EventArgs)
        Try
            RaiseEvent Chiusura(Me, EventArgs.Empty)
        Catch ex As Exception
            _log.ScriviLog("Campi.GestisciEventoChiusura: " & ex.Message)
        End Try
    End Sub

    Private Sub GestisciEventoFine(sender As Object, e As EventArgs)
        Try
            RaiseEvent Fine(Me, EventArgs.Empty)
        Catch ex As Exception
            _log.ScriviLog("Campi.GestisciEventoFine: " & ex.Message)
        End Try
    End Sub

    Private Sub GestisciEventoCampoConfermato(id As String)
        Try
            RaiseEvent CampoConfermato(id)
        Catch ex As Exception
            _log.ScriviLog("Campi.GestisciEventoCampoConfermato: " & ex.Message)
        End Try
    End Sub

    Public Sub AggiornaTesto(id As String, nuovoTesto As String)
        Try
            If _motoreGrafico IsNot Nothing AndAlso Not _motoreGrafico.IsDisposed Then
                _motoreGrafico.AggiornaTestoCampo(id, nuovoTesto)
            End If
        Catch ex As Exception
            _log.ScriviLog("Campi.AggiornaTesto: " & ex.Message)
        End Try
    End Sub

    Public Property SalvaSuEscape As Boolean
        Get
            Try
                If _motoreGrafico IsNot Nothing Then Return _motoreGrafico.SalvaSuEscape
                Return False
            Catch ex As Exception
                _log.ScriviLog("Campi.SalvaSuEscape Get: " & ex.Message)
                Return False
            End Try
        End Get
        Set(value As Boolean)
            Try
                If _motoreGrafico IsNot Nothing Then _motoreGrafico.SalvaSuEscape = value
            Catch ex As Exception
                _log.ScriviLog("Campi.SalvaSuEscape Set: " & ex.Message)
            End Try
        End Set
    End Property

    Public Sub Avvia()
        Try
            If _motoreGrafico IsNot Nothing AndAlso Not _motoreGrafico.IsDisposed Then
                _motoreGrafico.AvviaForm()
            End If
        Catch ex As Exception
            _log.ScriviLog("Campi.Avvia: " & ex.Message)
        End Try
    End Sub

    ''' <summary>
    ''' Chiude la schermata senza sollevare Chiusura ne' Fine: da usare quando il chiamante ha
    ''' gia' gestito da solo il proprio flusso (es. dopo aver inviato un messaggio) e vuole solo
    ''' chiudere la finestra, senza un secondo evento che duplicherebbe la logica gia' eseguita.
    ''' </summary>
    Public Sub Chiudi()
        Try
            If _motoreGrafico IsNot Nothing AndAlso Not _motoreGrafico.IsDisposed Then
                _motoreGrafico.ChiudiSilenziosamente()
            End If
        Catch ex As Exception
            _log.ScriviLog("Campi.Chiudi: " & ex.Message)
        End Try
    End Sub

    Public Sub ImpostaImpostazioniTemporanee(echoDigitazione As String, livelloPunteggiaturaScrittura As Integer)
        Try
            If _motoreGrafico IsNot Nothing AndAlso Not _motoreGrafico.IsDisposed Then
                _motoreGrafico.ImpostaImpostazioniTemporaneeForm(echoDigitazione, livelloPunteggiaturaScrittura)
            End If
        Catch ex As Exception
            _log.ScriviLog("Campi.ImpostaImpostazioniTemporanee: " & ex.Message)
        End Try
    End Sub

    Public Sub ImpostaMaiuscolaAutomatica(id As String, attiva As Boolean)
        Try
            If _motoreGrafico IsNot Nothing AndAlso Not _motoreGrafico.IsDisposed Then
                _motoreGrafico.ImpostaMaiuscolaAutomaticaCampo(id, attiva)
            End If
        Catch ex As Exception
            _log.ScriviLog("Campi.ImpostaMaiuscolaAutomatica: " & ex.Message)
        End Try
    End Sub

    Public Sub SelezionaCampo(id As String)
        Try
            If _motoreGrafico IsNot Nothing AndAlso Not _motoreGrafico.IsDisposed Then
                _motoreGrafico.SelezionaCampoPerId(id)
            End If
        Catch ex As Exception
            _log.ScriviLog("Campi.SelezionaCampo: " & ex.Message)
        End Try
    End Sub

    ''' <summary>
    ''' Aggiunge un campo menu: non si scrive, ma si sceglie da un elenco che si apre appena ci si arriva (o con Invio). Ogni voce ha
    ''' un identificativo; il campo mostra il testo della voce scelta e RecuperaIdScelta dà il suo identificativo. Con Ctrl freccia su
    ''' e Ctrl freccia giù, dall'elenco, si passa al campo precedente e al successivo.
    ''' </summary>
    Public Sub AggiungiScelta(titolo As String, id As String, voci As IEnumerable(Of KeyValuePair(Of String, String)), Optional obbligatorio As Boolean = True, Optional idPredefinito As String = "")
        Try
            ValidateEngineStatus()
            _motoreGrafico.AggiungiCampoScelta(titolo, id, voci, obbligatorio, idPredefinito)
        Catch ex As Exception
            _log.ScriviLog("Campi.AggiungiScelta: " & ex.Message)
        End Try
    End Sub

    ''' <summary>Toglie un campo (per esempio un campo menu che non serve più): non si toglie quello in cui ci si trova.</summary>
    Public Sub RimuoviCampo(id As String)
        Try
            If _motoreGrafico IsNot Nothing AndAlso Not _motoreGrafico.IsDisposed Then _motoreGrafico.RimuoviCampoPerId(id)
        Catch ex As Exception
            _log.ScriviLog("Campi.RimuoviCampo: " & ex.Message)
        End Try
    End Sub

    ''' <summary>L'identificativo della voce scelta in un campo menu (stringa vuota se non c'è).</summary>
    Public Function RecuperaIdScelta(id As String) As String
        Try
            If _motoreGrafico IsNot Nothing AndAlso Not _motoreGrafico.IsDisposed Then Return _motoreGrafico.OttieniIdScelta(id)
        Catch ex As Exception
            _log.ScriviLog("Campi.RecuperaIdScelta: " & ex.Message)
        End Try
        Return ""
    End Function

    ''' <summary>
    ''' Fa aprire un elenco da cui scegliere quando si preme Invio su quel campo. "costruisci" riceve il testo scritto nel campo e
    ''' un'azione "conferma": restituisce l'elenco (un WGMenuLista non ancora avviato, con una voce per ogni scelta) oppure Nothing
    ''' se non c'è niente da mostrare. Quando l'utente sceglie, chi ha costruito l'elenco lo chiude con abbandona e chiama
    ''' conferma con il testo da mettere nel campo: il campo viene confermato e si passa al successivo, come con Invio.
    ''' Con Esc l'elenco si chiude e il campo resta com'è.
    ''' </summary>
    Public Sub ImpostaElenco(id As String, costruisci As Func(Of String, Action(Of String), Task(Of WGMenuLista)))
        Try
            If _motoreGrafico IsNot Nothing AndAlso Not _motoreGrafico.IsDisposed Then
                _motoreGrafico.ImpostaElencoCampo(id, costruisci)
            End If
        Catch ex As Exception
            _log.ScriviLog("Campi.ImpostaElenco: " & ex.Message)
        End Try
    End Sub

    Public Sub Aggiungi(titolo As String, Optional id As String = "", Optional obbligatorio As Boolean = False, Optional solaLettura As Boolean = False, Optional testoPredefinito As String = "")
        Try
            ValidateEngineStatus()
            _motoreGrafico.AggiungiCampoGenerico(titolo, id, obbligatorio, solaLettura, testoPredefinito, TipoCampo.Alfanumerico, False)
        Catch ex As Exception
            _log.ScriviLog("Campi.Aggiungi: " & ex.Message)
        End Try
    End Sub

    Public Sub AggiungiNumerico(titolo As String, Optional id As String = "", Optional obbligatorio As Boolean = False, Optional solaLettura As Boolean = False, Optional testoPredefinito As String = "")
        Try
            ValidateEngineStatus()
            _motoreGrafico.AggiungiCampoGenerico(titolo, id, obbligatorio, solaLettura, testoPredefinito, TipoCampo.Numerico, False)
        Catch ex As Exception
            _log.ScriviLog("Campi.AggiungiNumerico: " & ex.Message)
        End Try
    End Sub

    Public Sub AggiungiMultiriga(titolo As String, Optional id As String = "", Optional obbligatorio As Boolean = False, Optional solaLettura As Boolean = False, Optional testoPredefinito As String = "")
        Try
            ValidateEngineStatus()
            _motoreGrafico.AggiungiCampoGenerico(titolo, id, obbligatorio, solaLettura, testoPredefinito, TipoCampo.Multiriga, False)
        Catch ex As Exception
            _log.ScriviLog("Campi.AggiungiMultiriga: " & ex.Message)
        End Try
    End Sub

    Public Sub AggiungiPassword(titolo As String, Optional id As String = "", Optional obbligatorio As Boolean = False, Optional solaLettura As Boolean = False, Optional testoPredefinito As String = "")
        Try
            ValidateEngineStatus()
            _motoreGrafico.AggiungiCampoGenerico(titolo, id, obbligatorio, solaLettura, testoPredefinito, TipoCampo.Password, False)
        Catch ex As Exception
            _log.ScriviLog("Campi.AggiungiPassword: " & ex.Message)
        End Try
    End Sub

    Public Sub AggiungiData(titolo As String, Optional id As String = "", Optional obbligatorio As Boolean = False, Optional solaLettura As Boolean = False, Optional testoPredefinito As String = "")
        Try
            ValidateEngineStatus()
            _motoreGrafico.AggiungiCampoGenerico(titolo, id, obbligatorio, solaLettura, testoPredefinito, TipoCampo.Data, False)
        Catch ex As Exception
            _log.ScriviLog("Campi.AggiungiData: " & ex.Message)
        End Try
    End Sub

    Public Sub AggiungiOrario(titolo As String, Optional id As String = "", Optional obbligatorio As Boolean = False, Optional solaLettura As Boolean = False, Optional testoPredefinito As String = "")
        Try
            ValidateEngineStatus()
            _motoreGrafico.AggiungiCampoGenerico(titolo, id, obbligatorio, solaLettura, testoPredefinito, TipoCampo.Orario, False)
        Catch ex As Exception
            _log.ScriviLog("Campi.AggiungiOrario: " & ex.Message)
        End Try
    End Sub

    Public Sub AggiungiHost(titolo As String, Optional id As String = "", Optional obbligatorio As Boolean = False, Optional solaLettura As Boolean = False, Optional testoPredefinito As String = "")
        Try
            ValidateEngineStatus()
            _motoreGrafico.AggiungiCampoGenerico(titolo, id, obbligatorio, solaLettura, testoPredefinito, TipoCampo.Host, False)
        Catch ex As Exception
            _log.ScriviLog("Campi.AggiungiHost: " & ex.Message)
        End Try
    End Sub

    Public Sub AggiungiNumeroCarta(titolo As String, Optional id As String = "", Optional obbligatorio As Boolean = False, Optional solaLettura As Boolean = False, Optional testoPredefinito As String = "")
        Try
            ValidateEngineStatus()
            _motoreGrafico.AggiungiCampoGenerico(titolo, id, obbligatorio, solaLettura, testoPredefinito, TipoCampo.CartaDiCredito, False)
        Catch ex As Exception
            _log.ScriviLog("Campi.AggiungiNumeroCarta: " & ex.Message)
        End Try
    End Sub

    Public Sub AggiungiCVC(titolo As String, Optional id As String = "", Optional obbligatorio As Boolean = False, Optional solaLettura As Boolean = False, Optional testoPredefinito As String = "")
        Try
            ValidateEngineStatus()
            _motoreGrafico.AggiungiCampoGenerico(titolo, id, obbligatorio, solaLettura, testoPredefinito, TipoCampo.CVC, False)
        Catch ex As Exception
            _log.ScriviLog("Campi.AggiungiCVC: " & ex.Message)
        End Try
    End Sub

    Public Sub AggiungiDataScadenza(titolo As String, Optional id As String = "", Optional obbligatorio As Boolean = False, Optional solaLettura As Boolean = False, Optional testoPredefinito As String = "")
        Try
            ValidateEngineStatus()
            _motoreGrafico.AggiungiCampoGenerico(titolo, id, obbligatorio, solaLettura, testoPredefinito, TipoCampo.DataScadenza, False)
        Catch ex As Exception
            _log.ScriviLog("Campi.AggiungiDataScadenza: " & ex.Message)
        End Try
    End Sub

    Public Sub AggiungiMulti(titolo As String, id As String, Optional obbligatorio As Boolean = True, Optional limite As Integer = 0)
        Try
            ValidateEngineStatus()
            _motoreGrafico.AggiungiCampoGenerico(titolo, id, obbligatorio, False, String.Empty, TipoCampo.Alfanumerico, True, limite)
        Catch ex As Exception
            _log.ScriviLog("Campi.AggiungiMulti: " & ex.Message)
        End Try
    End Sub

    Public Sub AggiungiMultiNumerico(titolo As String, id As String, Optional obbligatorio As Boolean = True, Optional limite As Integer = 0)
        Try
            ValidateEngineStatus()
            _motoreGrafico.AggiungiCampoGenerico(titolo, id, obbligatorio, False, String.Empty, TipoCampo.Numerico, True, limite)
        Catch ex As Exception
            _log.ScriviLog("Campi.AggiungiMultiNumerico: " & ex.Message)
        End Try
    End Sub

    Public Sub AggiungiMultiPassword(titolo As String, id As String, Optional obbligatorio As Boolean = True, Optional limite As Integer = 0)
        Try
            ValidateEngineStatus()
            _motoreGrafico.AggiungiCampoGenerico(titolo, id, obbligatorio, False, String.Empty, TipoCampo.Password, True, limite)
        Catch ex As Exception
            _log.ScriviLog("Campi.AggiungiMultiPassword: " & ex.Message)
        End Try
    End Sub

    Public Sub AggiungiMultiData(titolo As String, id As String, Optional obbligatorio As Boolean = True, Optional limite As Integer = 0)
        Try
            ValidateEngineStatus()
            _motoreGrafico.AggiungiCampoGenerico(titolo, id, obbligatorio, False, String.Empty, TipoCampo.Data, True, limite)
        Catch ex As Exception
            _log.ScriviLog("Campi.AggiungiMultiData: " & ex.Message)
        End Try
    End Sub

    Public Sub AggiungiMultiOrario(titolo As String, id As String, Optional obbligatorio As Boolean = True, Optional limite As Integer = 0)
        Try
            ValidateEngineStatus()
            _motoreGrafico.AggiungiCampoGenerico(titolo, id, obbligatorio, False, String.Empty, TipoCampo.Orario, True, limite)
        Catch ex As Exception
            _log.ScriviLog("Campi.AggiungiMultiOrario: " & ex.Message)
        End Try
    End Sub

    Public Sub AggiungiMultiNumeroCarta(titolo As String, id As String, Optional obbligatorio As Boolean = True, Optional limite As Integer = 0)
        Try
            ValidateEngineStatus()
            _motoreGrafico.AggiungiCampoGenerico(titolo, id, obbligatorio, False, String.Empty, TipoCampo.CartaDiCredito, True, limite)
        Catch ex As Exception
            _log.ScriviLog("Campi.AggiungiMultiNumeroCarta: " & ex.Message)
        End Try
    End Sub

    Public Sub AggiungiMultiCVC(titolo As String, id As String, Optional obbligatorio As Boolean = True, Optional limite As Integer = 0)
        Try
            ValidateEngineStatus()
            _motoreGrafico.AggiungiCampoGenerico(titolo, id, obbligatorio, False, String.Empty, TipoCampo.CVC, True, limite)
        Catch ex As Exception
            _log.ScriviLog("Campi.AggiungiMultiCVC: " & ex.Message)
        End Try
    End Sub

    Public Sub AggiungiMultiDataScadenza(titolo As String, id As String, Optional obbligatorio As Boolean = True, Optional limite As Integer = 0)
        Try
            ValidateEngineStatus()
            _motoreGrafico.AggiungiCampoGenerico(titolo, id, obbligatorio, False, String.Empty, TipoCampo.DataScadenza, True, limite)
        Catch ex As Exception
            _log.ScriviLog("Campi.AggiungiMultiDataScadenza: " & ex.Message)
        End Try
    End Sub

    Public Sub AggiungiMultiConValori(titolo As String, id As String, valori As IEnumerable(Of String), Optional obbligatorio As Boolean = False, Optional limite As Integer = 0)
        Try
            ValidateEngineStatus()
            _motoreGrafico.AggiungiCampoMultiploConValori(titolo, id, valori, obbligatorio, TipoCampo.Alfanumerico, limite)
        Catch ex As Exception
            _log.ScriviLog("Campi.AggiungiMultiConValori: " & ex.Message)
        End Try
    End Sub

    Public Function RecuperaValore(id As String) As String
        Try
            ValidateEngineStatus()
            Return _motoreGrafico.OttieniValoreCampo(id)
        Catch ex As Exception
            _log.ScriviLog("Campi.RecuperaValore: " & ex.Message)
            Return String.Empty
        End Try
    End Function

    Public Function RecuperaListaValori(id As String) As List(Of String)
        Try
            ValidateEngineStatus()
            Return _motoreGrafico.OttieniListaValoriMultipli(id)
        Catch ex As Exception
            _log.ScriviLog("Campi.RecuperaListaValori: " & ex.Message)
            Return New List(Of String)()
        End Try
    End Function

    Public Function TuttiObbligatoriRiempiti() As Boolean
        Try
            ValidateEngineStatus()
            Return _motoreGrafico.VerificaTuttiCampiObbligatori()
        Catch ex As Exception
            _log.ScriviLog("Campi.TuttiObbligatoriRiempiti: " & ex.Message)
            Return False
        End Try
    End Function

    Public Sub AggiungiOpzione(testoVoce As String, evento As EventHandler)
        Try
            ValidateEngineStatus()
            _motoreGrafico.AggiungiOpzione(testoVoce, evento)
        Catch ex As Exception
            _log.ScriviLog("Campi.AggiungiOpzione: " & ex.Message)
        End Try
    End Sub

    Public Sub RimuoviOpzione(testoVoce As String)
        Try
            ValidateEngineStatus()
            _motoreGrafico.RimuoviOpzione(testoVoce)
        Catch ex As Exception
            _log.ScriviLog("Campi.RimuoviOpzione: " & ex.Message)
        End Try
    End Sub

    Public Sub SvuotaOpzioni()
        Try
            ValidateEngineStatus()
            _motoreGrafico.SvuotaOpzioni()
        Catch ex As Exception
            _log.ScriviLog("Campi.SvuotaOpzioni: " & ex.Message)
        End Try
    End Sub

    Private Sub ValidateEngineStatus()
        Try
            If _disposto Then
                Throw New ObjectDisposedException("Campi", "L'oggetto Campi è stato eliminato e non può più essere utilizzato.")
            End If
            If _motoreGrafico Is Nothing OrElse _motoreGrafico.IsDisposed Then
                Throw New InvalidOperationException("Il motore grafico interno non è inizializzato o è stato eliminato.")
            End If
        Catch ex As ObjectDisposedException
            _log.ScriviLog("Campi.ValidateEngineStatus (ObjectDisposed): " & ex.Message)
            Throw
        Catch ex As InvalidOperationException
            _log.ScriviLog("Campi.ValidateEngineStatus (InvalidOperation): " & ex.Message)
            Throw
        Catch ex As Exception
            _log.ScriviLog("Campi.ValidateEngineStatus: " & ex.Message)
            Throw
        End Try
    End Sub


    Protected Overridable Sub Dispose(disposing As Boolean)
        Try
            If Not _disposto Then
                If disposing Then
                    If _motoreGrafico IsNot Nothing Then
                        Try
                            RemoveHandler _motoreGrafico.EventoChiusura, AddressOf GestisciEventoChiusura
                            RemoveHandler _motoreGrafico.EventoFine, AddressOf GestisciEventoFine
                            RemoveHandler _motoreGrafico.EventoCampoConfermato, AddressOf GestisciEventoCampoConfermato
                            _motoreGrafico = Nothing
                        Catch ex As Exception
                            _log.ScriviLog("Campi.Dispose (MotoreGrafico): " & ex.Message)
                        End Try
                    End If
                End If
                _disposto = True
            End If
        Catch ex As Exception
            _log.ScriviLog("Campi.Dispose (disposing): " & ex.Message)
        End Try
    End Sub
End Class

Partial Friend Class CampiFormInterno
    Inherits System.Windows.Forms.UserControl

    Private Class MetadatiCampo
        Public Property Controllo As WGCampiMovimento
        Public Property ControlloWord As WGWord
        Public Property EWord As Boolean
        Public Property Identificativo As String
        Public Property Obbligatorio As Boolean
        Public Property Tipo As TipoCampo
        Public Property IdBaseMultiplo As String
        Public Property LimiteMultiplo As Integer
        Public Property EMultiplo As Boolean
        Public Property TitoloBase As String
        ''' <summary>Cosa si apre premendo Invio sul campo (vedi Campi.ImpostaElenco), o Nothing.</summary>
        Public Property Elenco As Func(Of String, Action(Of String), Task(Of WGMenuLista))
        ''' <summary>Vero dopo "Scrivi la data a mano": il prossimo Invio conferma il campo data senza aprire l'elenco.</summary>
        Public Property DataManuale As Boolean
        ''' <summary>Vero se il campo è di sola lettura fin dall'inizio: allora l'elenco delle date non si apre.</summary>
        Public Property SolaLettura As Boolean
        ''' <summary>Le scelte di un campo menu (identificativo e testo) o Nothing se il campo non è un campo menu.</summary>
        Public Property Scelte As List(Of KeyValuePair(Of String, String))
        ''' <summary>L'identificativo della scelta fatta in un campo menu.</summary>
        Public Property IdScelto As String = ""
    End Class

    Private _elencoAperto As WGMenuLista = Nothing
    Private _elencoInCostruzione As Boolean = False
    ''' <summary>Quando si è chiuso l'ultimo elenco di un campo: serve a non riaprire l'elenco delle date appena il focus torna sul campo.</summary>
    Private _chiusuraElencoTick As Integer = 0
    Private _campoChiusuraElenco As WGCampiMovimento = Nothing
    Private _aperturaDatePianificata As Boolean = False

    Private Class Voce
        Public Property Nome As String
        Public Property Evento As EventHandler
    End Class

    Private _logger As New Logger()
    Private _sospendiAutoAggiuntaMultiplo As Boolean = False
    Private _listaCampi As New List(Of MetadatiCampo)
    Private _valoriSalvati As New Dictionary(Of MetadatiCampo, String)
    Private _mappaIndiciCampi As New Dictionary(Of WGCampiMovimento, MetadatiCampo)
    Private _sintesiVocale As sintesivocale = sintesivocale.GetInstance()
    Private _utilsForm As WGFormUtils = WGFormUtils.GetInstance()
    Private _impostazioniComuni As New WGImpostazioniComuni()

    Private _chiusoCorrettamente As Boolean = False
    Private _messaggioErroreAttivo As Boolean = False
    Private _chiusuraDaEscape As Boolean = False

    Private WithEvents menuOpzioni As WGListaView
    Private eventiOpzioni As New List(Of Voce)()
    Private menuOpzioniAperto As Boolean = False
    Private ultimoElementoOpzioni As Integer = -1
    Private _campoConFocusMenu As WGCampiMovimento
    Private _listaStampantiCampi As WGMenuLista

    ''' <summary>
    ''' Come _campoConFocusMenu, ma tipizzato Control invece di WGCampiMovimento: serve a non
    ''' perdere il riferimento al campo che aveva il focus quando quel campo e' un WGWord (che
    ''' non e' un WGCampiMovimento, quindi il cast in TryCast(..., WGCampiMovimento) fallisce e
    ''' tornerebbe Nothing). Usato solo per ridare il focus in chiudiOpzioni, non per le voci
    ''' Taglia/Copia/Incolla del menu, che restano valide solo per i campi normali.
    ''' </summary>
    Private _controlloConFocusMenu As Control

    Private _threadMonitorVisibilita As Thread
    Private _monitorVisibilitaInEsecuzione As Boolean = True

    Private WithEvents _tastiera As WGTastiera
    Private WithEvents _touk As WGTouk
    Private _tastieraTouch As WGTastieraTouch
    Private Const QuotaCampiTouk As Double = 0.6
    Private _altDestroPremuto As Boolean = False
    Private _altDestroUsato As Boolean = False

    Private _indiceAzioneTouch As Integer = 0
    Private _campoAzioneTouch As Control = Nothing
    Private _dettaturaCampi As WGDettatura
    Private _dettaturaCampiInCorso As Boolean = False
    Private _campoDettaturaCorrente As WGCampiMovimento

    Private _dettaturaCampiWord As WGDettatura
    Private _dettaturaCampiWordInCorso As Boolean = False
    Private _campoWordDettaturaCorrente As WGWord
    Private _tastoDettatura As WGTasto
    Private _dettaturaWordAttiva As Boolean = False

    Private WithEvents _timerLettereNonConsentite As New System.Windows.Forms.Timer()
    Private _messaggioLettereNonConsentite As String = Nothing
    Private _campoMessaggioLettereNonConsentite As WGCampiMovimento = Nothing

    Public Event EventoChiusura As EventHandler
    Public Event EventoFine As EventHandler
    Public Event EventoCampoConfermato(id As String)

    Private Const SPAZIO_SUPERIORE As Integer = 15
    Private Const SPAZIO_SINISTRA As Integer = 20
    Private Const DISTANZA_VERTICALE As Integer = 80
    Private Const TITOLO_ALTEZZA As Integer = 35
    Private Const DIMENSIONE_FONT_ETICHETTA As Integer = 18
    Private Const DIMENSIONE_FONT_CASELLA As Integer = 16

    <DllImport("user32.dll", SetLastError:=True)>
    Private Shared Function SetFocus(hWnd As IntPtr) As IntPtr
    End Function

    <DllImport("user32.dll")>
    Private Shared Function GetForegroundWindow() As IntPtr
    End Function

    <DllImport("user32.dll")>
    Private Shared Function SetForegroundWindow(hWnd As IntPtr) As Boolean
    End Function

    <DllImport("user32.dll")>
    Private Shared Function BringWindowToTop(hWnd As IntPtr) As Boolean
    End Function

    <DllImport("user32.dll")>
    Private Shared Function GetWindowThreadProcessId(hWnd As IntPtr, ByRef lpdwProcessId As UInteger) As UInteger
    End Function

    <DllImport("kernel32.dll")>
    Private Shared Function GetCurrentThreadId() As UInteger
    End Function

    <DllImport("user32.dll")>
    Private Shared Function AttachThreadInput(idAttach As UInteger, idAttachTo As UInteger, fAttach As Boolean) As Boolean
    End Function

    <DllImport("user32.dll")>
    Private Shared Function ShowWindow(hWnd As IntPtr, nCmdShow As Integer) As Boolean
    End Function

    <DllImport("user32.dll")>
    Private Shared Function IsWindowVisible(hWnd As IntPtr) As Boolean
    End Function

    Private Const SW_HIDE As Integer = 0
    Private Const SW_SHOW As Integer = 5

    Public Sub New()
        Try
            Me.Size = New Size(500, 350)
            Me.DoubleBuffered = True
            Me.AccessibleRole = AccessibleRole.None
            Me.AccessibleName = Nothing

            _tastiera = WGTastiera.GetInstance()
            _touk = WGTouk.GetInstance()

            Try
                _timerLettereNonConsentite.Interval = 2000
            Catch ex As Exception
                _logger.ScriviLog("CampiFormInterno.New (Timer Lettere): " & ex.Message)
            End Try

            Try
                _threadMonitorVisibilita = New Thread(AddressOf MonitorVisibilitaLoop)
                _threadMonitorVisibilita.IsBackground = True
                _threadMonitorVisibilita.Start()
            Catch ex As Exception
                _logger.ScriviLog("CampiFormInterno.New (Thread Monitor): " & ex.Message)
            End Try

            AggiungiOpzione("Riassumi il testo", AddressOf OpzioneRiassumiTestoGlobale)
            AggiornaTastieraTouk()
        Catch ex As Exception
            _logger.ScriviLog("CampiFormInterno.New: " & ex.Message)
        End Try
    End Sub

    Private Sub MonitorVisibilitaLoop()
        Return
    End Sub

    Private Async Sub OpzioneRiassumiTestoGlobale(sender As Object, e As EventArgs)
        Try
            Dim campoAttivo As WGCampiMovimento = TryCast(Me.ActiveControl, WGCampiMovimento)
            If campoAttivo Is Nothing Then Return

            Dim testo As String = campoAttivo.Text
            If String.IsNullOrWhiteSpace(testo) Then
                Dim msgVuoto As New WGMessaggi()
                msgVuoto.Messaggio("Il campo è vuoto, non c'è nulla da riassumere.")
                msgVuoto.ShowDialog()
                Return
            End If
            If Not WGAi.AlmenoUnProviderConfigurato() Then
                Dim msgConfig As New WGMessaggi()
                msgConfig.Messaggio("Nessuna chiave API configurata per l'intelligenza artificiale. Impostala dalle impostazioni, sezione Intelligenza artificiale.")
                msgConfig.ShowDialog()
                Return
            End If

            Dim processo As New WGProcessoInCorso()
            processo.Avvia("Sto elaborando il riassunto, attendere", False)

            Dim risultato As Tuple(Of String, String) = Await WGAi.RiassumiTestoAsync(testo)

            processo.Fine()

            If risultato Is Nothing OrElse String.IsNullOrEmpty(risultato.Item1) Then
                Dim msgErrore As New WGMessaggi()
                msgErrore.Messaggio("Non è stato possibile ottenere il riassunto: " & If(risultato IsNot Nothing, risultato.Item2, "errore sconosciuto"))
                msgErrore.ShowDialog()
                Return
            End If

            Dim campoRisultato As New WGCampo()
            campoRisultato.init("Riassunto", risultato.Item1, True, True, False, False)
            campoRisultato.avvia()
        Catch ex As Exception
            _logger.ScriviLog("CampiFormInterno.OpzioneRiassumiTestoGlobale: " & ex.Message)
        End Try
    End Sub

    Private Sub _tastiera_KeyDown(ByRef tasto As Keys) Handles _tastiera.KeyDown
        Try
            If _altDestroPremuto OrElse (_tastiera IsNot Nothing AndAlso _tastiera.AltDestroAttualmentePremuto) Then
                _altDestroUsato = True
            End If

            If Not Me.Visible OrElse WGContenitore.Istanza Is Nothing OrElse Not WGContenitore.Istanza.PuoRicevereInput(Me) Then Return
            If (tasto And Keys.KeyCode) = Keys.None Then
                Return
            End If

            If Not menuOpzioniAperto AndAlso CampoWordAttivoConFocus() Then
                If tasto = Keys.Escape Then
                    tasto = Keys.None
                    If Me.InvokeRequired Then
                        Me.Invoke(New MethodInvoker(AddressOf GestisciTastoEscape))
                    Else
                        GestisciTastoEscape()
                    End If
                End If
                Return
            End If

            Dim ctrlPremuto As Boolean = My.Computer.Keyboard.CtrlKeyDown
            Dim shiftPremuto As Boolean = My.Computer.Keyboard.ShiftKeyDown
            Dim altPremuto As Boolean = My.Computer.Keyboard.AltKeyDown

            If tasto = Keys.F1 AndAlso Not menuOpzioniAperto Then
                tasto = Keys.None
                WGGuida.MostraGuidaCampi()
                Return
            End If

            If tasto = Keys.Escape Then
                If menuOpzioniAperto Then
                    tasto = Keys.None
                    If Me.InvokeRequired Then
                        Me.Invoke(New MethodInvoker(AddressOf ChiudiOpzioniConAnnuncioCampo))
                    Else
                        ChiudiOpzioniConAnnuncioCampo()
                    End If
                Else
                    tasto = Keys.None
                    GestisciTastoEscape()
                End If
                Return
            End If

            If tasto = Keys.Tab AndAlso altPremuto Then
                If menuOpzioniAperto Then
                    If Me.InvokeRequired Then
                        Me.Invoke(New MethodInvoker(AddressOf chiudiOpzioni))
                    Else
                        chiudiOpzioni()
                    End If
                End If
                Return
            End If

            If menuOpzioniAperto AndAlso Not IsControlDisposed(menuOpzioni) Then
                Select Case tasto
                    Case Keys.Back
                        tasto = Keys.None
                        If Me.InvokeRequired Then
                            Me.Invoke(New MethodInvoker(AddressOf chiudiOpzioni))
                        Else
                            chiudiOpzioni()
                        End If
                    Case Keys.Enter, Keys.Right
                        tasto = Keys.None
                        Me.Invoke(Sub()
                                      If menuOpzioni.SelectedItem IsNot Nothing Then
                                          Dim testoVoce As String = menuOpzioni.SelectedItem.ToString()
                                          Dim handled As Boolean = True

                                          Select Case testoVoce
                                              Case "Stampa"
                                                  chiudiOpzioni()
                                                  StampaCampoCorrente()
                                                  Return
                                              Case "Taglia"
                                                  If _campoConFocusMenu IsNot Nothing Then _campoConFocusMenu.TagliaTesto()
                                              Case "Copia"
                                                  If _campoConFocusMenu IsNot Nothing Then _campoConFocusMenu.CopiaTesto()
                                              Case "Incolla"
                                                  If _campoConFocusMenu IsNot Nothing Then _campoConFocusMenu.IncollaTesto()
                                              Case "Incolla e sostituisci"
                                                  If _campoConFocusMenu IsNot Nothing AndAlso Not _campoConFocusMenu.ReadOnly AndAlso Clipboard.ContainsText() Then
                                                      _campoConFocusMenu.Text = Clipboard.GetText()
                                                      _campoConFocusMenu.SelectionStart = _campoConFocusMenu.TextLength
                                                      _sintesiVocale.LeggiTesto("Incollato e sostituito")
                                                  End If
                                              Case "Copia tutto"
                                                  If _campoConFocusMenu IsNot Nothing AndAlso _campoConFocusMenu.TextLength > 0 Then
                                                      If _campoConFocusMenu.IsCampoPassword AndAlso Not _campoConFocusMenu.leggipassword Then
                                                          _sintesiVocale.LeggiTesto("Impossibile copiare il contenuto di un campo password protetto")
                                                      Else
                                                          Clipboard.SetText(_campoConFocusMenu.Text)
                                                          _sintesiVocale.LeggiTesto("Tutto copiato negli appunti")
                                                      End If
                                                  End If
                                              Case "Seleziona tutto"
                                                  If _campoConFocusMenu IsNot Nothing Then _campoConFocusMenu.SelezionaTutto()
                                              Case "Svuota campo"
                                                  If _campoConFocusMenu IsNot Nothing AndAlso Not _campoConFocusMenu.ReadOnly Then
                                                      _campoConFocusMenu.Text = ""
                                                      _sintesiVocale.LeggiTesto("Campo svuotato")
                                                  End If
                                              Case "Dettatura"
                                                  chiudiOpzioni()
                                                  If TypeOf _controlloConFocusMenu Is WGWord Then
                                                      AvviaDettaturaCampoWord()
                                                  Else
                                                      AvviaDettaturaCampi()
                                                  End If
                                                  Return
                                              Case "Chiudi e conferma"
                                                  chiudiOpzioni()
                                                  EseguiInvioFinale()
                                                  Return
                                              Case "Mostra password"
                                                  If _campoConFocusMenu IsNot Nothing Then
                                                      _campoConFocusMenu.UseSystemPasswordChar = False
                                                      _sintesiVocale.LeggiTesto("Password in chiaro")
                                                  End If
                                              Case "Nascondi password"
                                                  If _campoConFocusMenu IsNot Nothing Then
                                                      _campoConFocusMenu.UseSystemPasswordChar = True
                                                      _sintesiVocale.LeggiTesto("Password nascosta")
                                                  End If
                                              Case "Leggi la password in chiaro durante la digitazione"
                                                  If _campoConFocusMenu IsNot Nothing Then
                                                      _campoConFocusMenu.leggipassword = True
                                                      _sintesiVocale.LeggiTesto("Lettura password in chiaro attivata")
                                                  End If
                                              Case "Non leggere la password durante la digitazione"
                                                  If _campoConFocusMenu IsNot Nothing Then
                                                      _campoConFocusMenu.leggipassword = False
                                                      _sintesiVocale.LeggiTesto("Lettura password disattivata")
                                                  End If
                                              Case Else
                                                  handled = False
                                          End Select

                                          If Not handled Then
                                              Dim voceEvento = eventiOpzioni.FirstOrDefault(Function(v) v.Nome = testoVoce)
                                              If voceEvento IsNot Nothing Then
                                                  voceEvento.Evento?.Invoke(Me, EventArgs.Empty)
                                              End If
                                          End If
                                      End If
                                      chiudiOpzioni()
                                  End Sub)
                    Case Keys.Left
                        tasto = Keys.None
                        If Me.InvokeRequired Then
                            Me.Invoke(New MethodInvoker(AddressOf chiudiOpzioni))
                        Else
                            chiudiOpzioni()
                        End If
                End Select
            Else
                If GestisciPressioneTastoHook(tasto, ctrlPremuto, shiftPremuto) Then
                    tasto = Keys.None
                End If
            End If
        Catch ex As Exception
            _logger.ScriviLog("CampiFormInterno._tastiera_KeyDown: " & ex.Message)
        End Try
    End Sub

    Private Sub _tastiera_AltPremuto(sender As Object, e As EventArgs) Handles _tastiera.AltPremuto
        Try
            If Not Me.Visible OrElse WGContenitore.Istanza Is Nothing OrElse Not WGContenitore.Istanza.PuoRicevereInput(Me) Then Return
            If CampoWordAttivoConFocus() Then Return
            Me.Invoke(New MethodInvoker(AddressOf apriOpzioni))
        Catch ex As Exception
            _logger.ScriviLog("CampiFormInterno._tastiera_AltPremuto: " & ex.Message)
        End Try
    End Sub

    Private Sub _tastiera_AltDestroPremuto(sender As Object, e As EventArgs) Handles _tastiera.AltDestroPremuto
        Try
            _altDestroPremuto = True
            _altDestroUsato = False
        Catch ex As Exception
            _logger.ScriviLog("CampiFormInterno._tastiera_AltDestroPremuto: " & ex.Message)
        End Try
    End Sub

    Private Sub _tastiera_AltDestroRilasciato(sender As Object, e As EventArgs) Handles _tastiera.AltDestroRilasciato
        Try
            _altDestroPremuto = False
            _altDestroUsato = False
        Catch ex As Exception
            _logger.ScriviLog("CampiFormInterno._tastiera_AltDestroRilasciato: " & ex.Message)
        End Try
    End Sub

    Private Sub _touk_FlickAvanti() Handles _touk.FlickAvanti
        Try
            If Not Me.Visible OrElse WGContenitore.Istanza Is Nothing OrElse Not WGContenitore.Istanza.PuoRicevereInput(Me) Then Return
            If menuOpzioniAperto Then Return
            EseguiMovimentoTouchSecondoModalita(avanti:=True)
        Catch ex As Exception
            _logger.ScriviLog("CampiFormInterno._touk_FlickAvanti: " & ex.Message)
        End Try
    End Sub

    Private Sub _touk_FlickIndietro() Handles _touk.FlickIndietro
        Try
            If Not Me.Visible OrElse WGContenitore.Istanza Is Nothing OrElse Not WGContenitore.Istanza.PuoRicevereInput(Me) Then Return
            If menuOpzioniAperto Then Return
            EseguiMovimentoTouchSecondoModalita(avanti:=False)
        Catch ex As Exception
            _logger.ScriviLog("CampiFormInterno._touk_FlickIndietro: " & ex.Message)
        End Try
    End Sub

    Private Sub EseguiMovimentoTouchSecondoModalita(avanti As Boolean)
        Try
            If _touk Is Nothing Then Return

            If CampoWordAttivoConFocus() Then
                Dim editorAttivo As WGWord = TryCast(Me.ActiveControl, WGWord)

                Select Case _touk.ModalitaNavigazione
                    Case WGTouk.ModalitaNavigazioneTouch.Riga
                        GestisciFlickTestoWord(0, If(avanti, 1, -1))
                    Case WGTouk.ModalitaNavigazioneTouch.Paragrafo
                        If editorAttivo IsNot Nothing AndAlso Not editorAttivo.IsDisposed Then
                            If avanti Then
                                editorAttivo.SpostaParagrafoAvantiPubblica()
                            Else
                                editorAttivo.SpostaParagrafoIndietroPubblica()
                            End If
                        End If
                    Case WGTouk.ModalitaNavigazioneTouch.Carattere
                        If editorAttivo IsNot Nothing AndAlso Not editorAttivo.IsDisposed Then
                            If avanti Then
                                editorAttivo.SpostaCarattereAvantiPubblica()
                            Else
                                editorAttivo.SpostaCarattereIndietroPubblica()
                            End If
                        End If
                    Case WGTouk.ModalitaNavigazioneTouch.Parola
                        ' Non si passa da GestisciKeyDown con Keys.Control: quel percorso legge lo
                        ' stato reale del Control fisico, che durante un gesto non e' premuto, e il
                        ' movimento resterebbe per carattere invece che per parola.
                        If editorAttivo IsNot Nothing AndAlso Not editorAttivo.IsDisposed Then
                            If avanti Then
                                editorAttivo.SpostaParolaAvantiPubblica()
                            Else
                                editorAttivo.SpostaParolaIndietroPubblica()
                            End If
                        End If
                    Case Else
                        ' Campo: ci spostiamo al campo successivo/precedente anche mentre si e'
                        ' dentro un editor Word.
                        If avanti Then
                            NavigaAlProssimoCampoGenerico()
                        Else
                            NavigaAlCampoPrecedente()
                        End If
                End Select
                Return
            End If

            ' Instradamento: il campo interno che ha il fuoco gestisce da se' il movimento nel
            ' testo. Questa schermata interviene solo per il cambio di campo, e solo quando la
            ' modalita' e' Campo: senza questo i due gestori agirebbero sullo stesso gesto.
            If _touk.ModalitaNavigazione <> WGTouk.ModalitaNavigazioneTouch.Campo Then Return
            If Not _touk.EContenitoreDelFuoco(Me) AndAlso Not _touk.EDestinatarioDiretto(Me) Then Return

            Dim casellaCorrente As WGCampiMovimento = TryCast(Me.ActiveControl, WGCampiMovimento)
            If casellaCorrente Is Nothing Then Return
            If avanti Then
                GestisciFrecciaGiu(casellaCorrente)
            Else
                GestisciFrecciaSu(casellaCorrente)
            End If
        Catch ex As Exception
            _logger.ScriviLog("CampiFormInterno.EseguiMovimentoTouchSecondoModalita: " & ex.Message)
        End Try
    End Sub

    Private Sub _touk_RichiestaLetturaCompleta() Handles _touk.RichiestaLetturaCompleta
        Try
            If Not Me.Visible OrElse WGContenitore.Istanza Is Nothing OrElse Not WGContenitore.Istanza.PuoRicevereInput(Me) Then Return
            If menuOpzioniAperto Then Return

            If CampoWordAttivoConFocus() Then
                Dim editor As WGWord = TryCast(Me.ActiveControl, WGWord)
                If editor IsNot Nothing AndAlso Not editor.IsDisposed Then
                    editor.AttivaDisattivaLetturaCompletaTouch()
                End If
                Return
            End If

            Dim casellaCorrente As WGCampiMovimento = TryCast(Me.ActiveControl, WGCampiMovimento)
            If casellaCorrente Is Nothing Then Return
            _sintesiVocale.LeggiTesto(casellaCorrente.Text)
        Catch ex As Exception
            _logger.ScriviLog("CampiFormInterno._touk_RichiestaLetturaCompleta: " & ex.Message)
        End Try
    End Sub

    Private Sub _touk_FlickSu() Handles _touk.FlickSu
        Try
            If Not Me.Visible OrElse WGContenitore.Istanza Is Nothing OrElse Not WGContenitore.Istanza.PuoRicevereInput(Me) Then Return
            If menuOpzioniAperto Then Return
            Dim editor As WGWord = TryCast(Me.ActiveControl, WGWord)
            If editor Is Nothing OrElse editor.IsDisposed Then Return
            editor.MostraMenuFormattazione()
        Catch ex As Exception
            _logger.ScriviLog("CampiFormInterno._touk_FlickSu: " & ex.Message)
        End Try
    End Sub

    Private Sub GestisciFlickTestoWord(deltaOrizzontale As Integer, deltaVerticale As Integer)
        Try
            Dim editor As WGWord = TryCast(Me.ActiveControl, WGWord)
            If editor Is Nothing OrElse editor.IsDisposed Then Return

            If deltaOrizzontale <> 0 Then
                If editor.SiamoInTabella Then
                    If deltaOrizzontale > 0 Then
                        editor.VaiCellaSuccessivaPubblica()
                    Else
                        editor.VaiCellaPrecedentePubblica()
                    End If
                Else
                    editor.GestisciKeyDown(If(deltaOrizzontale > 0, Keys.Right, Keys.Left))
                End If
            ElseIf deltaVerticale <> 0 Then
                editor.GestisciKeyDown(If(deltaVerticale > 0, Keys.Down, Keys.Up))
            End If
        Catch ex As Exception
            _logger.ScriviLog("CampiFormInterno.GestisciFlickTestoWord: " & ex.Message)
        End Try
    End Sub

    Private Sub MostraTastoDettatura()
        Try
            If _tastoDettatura Is Nothing Then
                _tastoDettatura = New WGTasto()
                AddHandler _tastoDettatura.Interrotto, AddressOf OnTastoDettaturaInterrotto
            End If
            _tastoDettatura.Mostra("Sto ascoltando. Parla ora. Premi un tasto qualsiasi per terminare la dettatura.")
        Catch ex As Exception
            _logger.ScriviLog("CampiFormInterno.MostraTastoDettatura: " & ex.Message)
        End Try
    End Sub

    Private Sub NascondiTastoDettatura()
        Try
            If _tastoDettatura IsNot Nothing Then
                _tastoDettatura.Chiudi()
            End If
        Catch ex As Exception
            _logger.ScriviLog("CampiFormInterno.NascondiTastoDettatura: " & ex.Message)
        End Try
    End Sub

    Private Sub OnTastoDettaturaInterrotto(sender As Object, e As EventArgs)
        Try
            If Me.InvokeRequired Then
                Me.BeginInvoke(New EventHandler(AddressOf OnTastoDettaturaInterrotto), sender, e)
                Return
            End If
            NascondiTastoDettatura()
            If _dettaturaWordAttiva Then
                FermaDettaturaCampoWord()
            Else
                FermaDettaturaCampi()
            End If
        Catch ex As Exception
            _logger.ScriviLog("CampiFormInterno.OnTastoDettaturaInterrotto: " & ex.Message)
        End Try
    End Sub

    Private Sub AvviaDettaturaCampoWord(Optional editorSpecifico As WGWord = Nothing)
        Try
            If Not WGDettatura.SistemaSupportato Then Return
            Dim editor As WGWord = If(editorSpecifico, TryCast(_controlloConFocusMenu, WGWord))
            If editor Is Nothing OrElse editor.IsDisposed OrElse editor.SolaLettura Then Return

            If _dettaturaCampiWord Is Nothing Then
                _dettaturaCampiWord = New WGDettatura()
                AddHandler _dettaturaCampiWord.TrascrizioneCompletata, AddressOf OnTrascrizioneCompletataCampoWord
                AddHandler _dettaturaCampiWord.TrascrizioneFallita, AddressOf OnTrascrizioneFallitaCampoWord
            End If

            If Not _dettaturaCampiWord.ModelloDisponibile Then
                _sintesiVocale.LeggiTesto("Nessuna chiave API di dettatura configurata")
                Return
            End If

            _campoWordDettaturaCorrente = editor
            _dettaturaCampiWordInCorso = True
            _dettaturaWordAttiva = True
            MostraTastoDettatura()
            _dettaturaCampiWord.IniziaRegistrazione()
        Catch ex As Exception
            _logger.ScriviLog("CampiFormInterno.AvviaDettaturaCampoWord: " & ex.Message)
            _dettaturaCampiWordInCorso = False
        End Try
    End Sub

    Private Sub CampoWordRichiedeDettatura(sender As Object, e As EventArgs)
        Try
            Dim editor As WGWord = TryCast(sender, WGWord)
            If editor Is Nothing Then Return
            AvviaDettaturaCampoWord(editor)
        Catch ex As Exception
            _logger.ScriviLog("CampiFormInterno.CampoWordRichiedeDettatura: " & ex.Message)
        End Try
    End Sub

    Private _processoDettatura As WGProcessoInCorso

    Private Sub MostraProcessoDettatura()
        Try
            If _processoDettatura Is Nothing Then
                _processoDettatura = New WGProcessoInCorso()
            End If
            _processoDettatura.Avvia("Riconoscimento del parlato in corso, attendere", False)
        Catch ex As Exception
            _logger.ScriviLog("CampiFormInterno.MostraProcessoDettatura: " & ex.Message)
        End Try
    End Sub

    Private Sub NascondiProcessoDettatura()
        Try
            If _processoDettatura IsNot Nothing Then
                _processoDettatura.Fine()
            End If
        Catch ex As Exception
            _logger.ScriviLog("CampiFormInterno.NascondiProcessoDettatura: " & ex.Message)
        End Try
    End Sub

    Private Sub FermaDettaturaCampoWord()
        Try
            _dettaturaCampiWordInCorso = False
            NascondiTastoDettatura()
            If _dettaturaCampiWord IsNot Nothing Then
                MostraProcessoDettatura()
                _dettaturaCampiWord.FermaRegistrazione()
            End If
        Catch ex As Exception
            _logger.ScriviLog("CampiFormInterno.FermaDettaturaCampoWord: " & ex.Message)
        End Try
    End Sub

    Private Sub OnTrascrizioneCompletataCampoWord(testo As String)
        Try
            If Me.InvokeRequired Then
                Me.BeginInvoke(New Action(Of String)(AddressOf OnTrascrizioneCompletataCampoWord), testo)
                Return
            End If
            NascondiProcessoDettatura()
            If _campoWordDettaturaCorrente IsNot Nothing AndAlso Not _campoWordDettaturaCorrente.IsDisposed AndAlso Not String.IsNullOrEmpty(testo) Then
                For Each carattereTesto As Char In testo
                    _campoWordDettaturaCorrente.GestisciChar(carattereTesto)
                Next
            End If
        Catch ex As Exception
            _logger.ScriviLog("CampiFormInterno.OnTrascrizioneCompletataCampoWord: " & ex.Message)
        End Try
    End Sub

    Private Sub OnTrascrizioneFallitaCampoWord(messaggioErrore As String)
        Try
            If Me.InvokeRequired Then
                Me.BeginInvoke(New Action(Of String)(AddressOf OnTrascrizioneFallitaCampoWord), messaggioErrore)
                Return
            End If
            NascondiProcessoDettatura()
            _sintesiVocale.LeggiTesto(messaggioErrore)
        Catch ex As Exception
            _logger.ScriviLog("CampiFormInterno.OnTrascrizioneFallitaCampoWord: " & ex.Message)
        End Try
    End Sub

    Private Sub CicloAzioneTouch(direzione As Integer)
        Try
            Dim controlloCorrente As Control = Me.ActiveControl
            If controlloCorrente Is Nothing Then Return
            Dim casellaCorrente As WGCampiMovimento = TryCast(controlloCorrente, WGCampiMovimento)

            Dim nomiAzioni As List(Of String) = CostruisciNomiAzioniTouch(casellaCorrente, controlloCorrente)
            If nomiAzioni.Count = 0 Then Return

            If Not ReferenceEquals(controlloCorrente, _campoAzioneTouch) Then
                _campoAzioneTouch = controlloCorrente
                _indiceAzioneTouch = 0
            End If

            _indiceAzioneTouch += direzione
            If _indiceAzioneTouch < 0 Then _indiceAzioneTouch = nomiAzioni.Count - 1
            If _indiceAzioneTouch >= nomiAzioni.Count Then _indiceAzioneTouch = 0

            _sintesiVocale.LeggiTesto(nomiAzioni(_indiceAzioneTouch))
        Catch ex As Exception
            _logger.ScriviLog("CampiFormInterno.CicloAzioneTouch: " & ex.Message)
        End Try
    End Sub

    Private Function CostruisciNomiAzioniTouch(casellaCorrente As WGCampiMovimento, controlloCorrente As Control) As List(Of String)
        Dim nomi As New List(Of String)
        Try
            nomi.Add("Scrivi")

            If casellaCorrente IsNot Nothing Then
                nomi.Add("Stampa")
                nomi.Add("Taglia")
                nomi.Add("Copia")
                nomi.Add("Incolla")
                nomi.Add("Incolla e sostituisci")
                nomi.Add("Copia tutto")
                nomi.Add("Seleziona tutto")
                nomi.Add("Svuota campo")
                If WGDettatura.SistemaSupportato Then
                    nomi.Add("Dettatura")
                End If
                nomi.Add("Chiudi e conferma")

                Dim dati As MetadatiCampo = Nothing
                If _mappaIndiciCampi.ContainsKey(casellaCorrente) Then
                    dati = _mappaIndiciCampi(casellaCorrente)
                End If

                If dati IsNot Nothing AndAlso dati.Tipo = TipoCampo.Password Then
                    If casellaCorrente.UseSystemPasswordChar Then
                        nomi.Add("Mostra password")
                    Else
                        nomi.Add("Nascondi password")
                    End If

                    If _impostazioniComuni.LeggiPasswordAttivo Then
                        nomi.Add("Non leggere la password durante la digitazione")
                    Else
                        nomi.Add("Leggi la password in chiaro durante la digitazione")
                    End If
                End If
            ElseIf TypeOf controlloCorrente Is WGWord Then
                nomi.Add("Stampa")
                If WGDettatura.SistemaSupportato Then
                    nomi.Add("Dettatura")
                End If
                nomi.Add("Chiudi e conferma")
            End If

            For Each v As Voce In eventiOpzioni
                If Not nomi.Contains(v.Nome) Then
                    nomi.Add(v.Nome)
                End If
            Next
        Catch ex As Exception
            _logger.ScriviLog("CampiFormInterno.CostruisciNomiAzioniTouch: " & ex.Message)
        End Try
        Return nomi
    End Function

    Private Sub EseguiAzioneTouch(testoVoce As String)
        Try
            Dim handled As Boolean = True
            Select Case testoVoce
                Case "Scrivi"
                    Return
                Case "Stampa"
                    StampaCampoCorrente()
                Case "Taglia"
                    If _campoConFocusMenu IsNot Nothing Then _campoConFocusMenu.TagliaTesto()
                Case "Copia"
                    If _campoConFocusMenu IsNot Nothing Then _campoConFocusMenu.CopiaTesto()
                Case "Incolla"
                    If _campoConFocusMenu IsNot Nothing Then _campoConFocusMenu.IncollaTesto()
                Case "Incolla e sostituisci"
                    If _campoConFocusMenu IsNot Nothing AndAlso Not _campoConFocusMenu.ReadOnly AndAlso Clipboard.ContainsText() Then
                        _campoConFocusMenu.Text = Clipboard.GetText()
                        _campoConFocusMenu.SelectionStart = _campoConFocusMenu.TextLength
                        _sintesiVocale.LeggiTesto("Incollato e sostituito")
                    End If
                Case "Copia tutto"
                    If _campoConFocusMenu IsNot Nothing AndAlso _campoConFocusMenu.TextLength > 0 Then
                        If _campoConFocusMenu.IsCampoPassword AndAlso Not _campoConFocusMenu.leggipassword Then
                            _sintesiVocale.LeggiTesto("Impossibile copiare il contenuto di un campo password protetto")
                        Else
                            Clipboard.SetText(_campoConFocusMenu.Text)
                            _sintesiVocale.LeggiTesto("Tutto copiato negli appunti")
                        End If
                    End If
                Case "Seleziona tutto"
                    If _campoConFocusMenu IsNot Nothing Then _campoConFocusMenu.SelezionaTutto()
                Case "Svuota campo"
                    If _campoConFocusMenu IsNot Nothing AndAlso Not _campoConFocusMenu.ReadOnly Then
                        _campoConFocusMenu.Text = ""
                        _sintesiVocale.LeggiTesto("Campo svuotato")
                    End If
                Case "Dettatura"
                    If TypeOf _controlloConFocusMenu Is WGWord Then
                        AvviaDettaturaCampoWord()
                    Else
                        AvviaDettaturaCampi()
                    End If
                Case "Chiudi e conferma"
                    EseguiInvioFinale()
                Case "Mostra password"
                    If _campoConFocusMenu IsNot Nothing Then
                        _campoConFocusMenu.UseSystemPasswordChar = False
                        _sintesiVocale.LeggiTesto("Password in chiaro")
                    End If
                Case "Nascondi password"
                    If _campoConFocusMenu IsNot Nothing Then
                        _campoConFocusMenu.UseSystemPasswordChar = True
                        _sintesiVocale.LeggiTesto("Password nascosta")
                    End If
                Case "Leggi la password in chiaro durante la digitazione"
                    If _campoConFocusMenu IsNot Nothing Then
                        _campoConFocusMenu.leggipassword = True
                        _sintesiVocale.LeggiTesto("Lettura password in chiaro attivata")
                    End If
                Case "Non leggere la password durante la digitazione"
                    If _campoConFocusMenu IsNot Nothing Then
                        _campoConFocusMenu.leggipassword = False
                        _sintesiVocale.LeggiTesto("Lettura password disattivata")
                    End If
                Case Else
                    handled = False
            End Select

            If Not handled Then
                Dim voceEvento = eventiOpzioni.FirstOrDefault(Function(v) v.Nome = testoVoce)
                If voceEvento IsNot Nothing Then
                    voceEvento.Evento?.Invoke(Me, EventArgs.Empty)
                End If
            End If
        Catch ex As Exception
            _logger.ScriviLog("CampiFormInterno.EseguiAzioneTouch: " & ex.Message)
        End Try
    End Sub

    Private Sub AvviaDettaturaCampi()
        Try
            If Not WGDettatura.SistemaSupportato Then Return
            If _campoConFocusMenu Is Nothing Then Return

            If _dettaturaCampi Is Nothing Then
                _dettaturaCampi = New WGDettatura()
                AddHandler _dettaturaCampi.TrascrizioneCompletata, AddressOf OnTrascrizioneCompletataCampi
                AddHandler _dettaturaCampi.TrascrizioneFallita, AddressOf OnTrascrizioneFallitaCampi
            End If

            If Not _dettaturaCampi.ModelloDisponibile Then
                _sintesiVocale.LeggiTesto("Chiave API di Gemini non configurata")
                Return
            End If

            _campoDettaturaCorrente = _campoConFocusMenu
            _dettaturaCampiInCorso = True
            _dettaturaWordAttiva = False
            MostraTastoDettatura()
            _dettaturaCampi.IniziaRegistrazione()
        Catch ex As Exception
            _logger.ScriviLog("CampiFormInterno.AvviaDettaturaCampi: " & ex.Message)
            _dettaturaCampiInCorso = False
        End Try
    End Sub

    Private Sub FermaDettaturaCampi()
        Try
            _dettaturaCampiInCorso = False
            NascondiTastoDettatura()
            If _dettaturaCampi IsNot Nothing Then
                MostraProcessoDettatura()
                _dettaturaCampi.FermaRegistrazione()
            End If
        Catch ex As Exception
            _logger.ScriviLog("CampiFormInterno.FermaDettaturaCampi: " & ex.Message)
        End Try
    End Sub

    Private Sub OnTrascrizioneCompletataCampi(testo As String)
        Try
            If Me.InvokeRequired Then
                Me.BeginInvoke(New Action(Of String)(AddressOf OnTrascrizioneCompletataCampi), testo)
                Return
            End If
            NascondiProcessoDettatura()
            If _campoDettaturaCorrente IsNot Nothing AndAlso Not _campoDettaturaCorrente.IsDisposed AndAlso Not String.IsNullOrEmpty(testo) Then
                _campoDettaturaCorrente.SelectedText = testo
            End If
        Catch ex As Exception
            _logger.ScriviLog("CampiFormInterno.OnTrascrizioneCompletataCampi: " & ex.Message)
        End Try
    End Sub

    Private Sub OnTrascrizioneFallitaCampi(messaggioErrore As String)
        Try
            If Me.InvokeRequired Then
                Me.BeginInvoke(New Action(Of String)(AddressOf OnTrascrizioneFallitaCampi), messaggioErrore)
                Return
            End If
            NascondiProcessoDettatura()
            _sintesiVocale.LeggiTesto(messaggioErrore)
        Catch ex As Exception
            _logger.ScriviLog("CampiFormInterno.OnTrascrizioneFallitaCampi: " & ex.Message)
        End Try
    End Sub

    Private Sub _touk_TapDoppio() Handles _touk.TapDoppio
        Try
            ' Un doppio tocco sulla tastiera a schermo conferma spazio/elimina/invio li', e le
            ' lettere sono gia' state scritte al pointer-up. Trattarlo come Invio faceva passare
            ' al campo successivo premendo due volte una lettera. Solo il tasto Invio passa oltre.
            If WGTastieraTouch.GestoSullaTastieraAttiva(_touk) Then Return
            If _touk IsNot Nothing AndAlso _touk.GestoIniziatoSuTastieraTouch() Then Return

            Dim controlloCorrente As Control = Me.ActiveControl
            If controlloCorrente IsNot Nothing AndAlso ReferenceEquals(controlloCorrente, _campoAzioneTouch) AndAlso _indiceAzioneTouch > 0 Then
                Dim casellaCorrente As WGCampiMovimento = TryCast(controlloCorrente, WGCampiMovimento)
                Dim nomiAzioni As List(Of String) = CostruisciNomiAzioniTouch(casellaCorrente, controlloCorrente)
                If _indiceAzioneTouch < nomiAzioni.Count Then
                    Dim nomeAzione As String = nomiAzioni(_indiceAzioneTouch)
                    _campoConFocusMenu = casellaCorrente
                    _controlloConFocusMenu = controlloCorrente
                    _indiceAzioneTouch = 0
                    _campoAzioneTouch = Nothing
                    EseguiAzioneTouch(nomeAzione)
                    Return
                End If
            End If
            Dim t As Keys = Keys.Enter
            _tastiera_KeyDown(t)
        Catch ex As Exception
            _logger.ScriviLog("CampiFormInterno._touk_TapDoppio: " & ex.Message)
        End Try
    End Sub

    Private Sub _touk_EscapeGesto() Handles _touk.EscapeGesto
        Try
            Dim t As Keys = Keys.Escape
            _tastiera_KeyDown(t)
        Catch ex As Exception
            _logger.ScriviLog("CampiFormInterno._touk_EscapeGesto: " & ex.Message)
        End Try
    End Sub

    Private Function IsControlDisposed(ctrl As Control) As Boolean
        Try
            Return ctrl Is Nothing OrElse ctrl.IsDisposed
        Catch ex As Exception
            _logger.ScriviLog("CampiFormInterno.IsControlDisposed: " & ex.Message)
            Return True
        End Try
    End Function

    Public Sub AggiungiOpzione(testoVoce As String, evento As EventHandler)
        If Me.InvokeRequired Then
            Me.Invoke(New MethodInvoker(Sub() AggiungiOpzione(testoVoce, evento)))
            Return
        End If
        Try
            If Not eventiOpzioni.Any(Function(v) v.Nome = testoVoce) Then
                eventiOpzioni.Add(New Voce With {.Nome = testoVoce, .Evento = evento})
                If menuOpzioni IsNot Nothing AndAlso Not menuOpzioni.IsDisposed AndAlso Not menuOpzioni.Items.Contains(testoVoce) Then
                    menuOpzioni.Items.Add(testoVoce)
                End If
            End If
        Catch ex As Exception
            _logger.ScriviLog("CampiFormInterno.AggiungiOpzione: " & ex.Message)
        End Try
    End Sub

    Public Sub RimuoviOpzione(testoVoce As String)
        If Me.InvokeRequired Then
            Me.Invoke(New MethodInvoker(Sub() RimuoviOpzione(testoVoce)))
            Return
        End If
        Try
            eventiOpzioni.RemoveAll(Function(v) v.Nome = testoVoce)
            If menuOpzioni IsNot Nothing AndAlso Not menuOpzioni.IsDisposed AndAlso menuOpzioni.Items.Contains(testoVoce) Then
                menuOpzioni.Items.Remove(testoVoce)
            End If
        Catch ex As Exception
            _logger.ScriviLog("CampiFormInterno.RimuoviOpzione: " & ex.Message)
        End Try
    End Sub

    Public Sub SvuotaOpzioni()
        If Me.InvokeRequired Then
            Me.Invoke(New MethodInvoker(AddressOf SvuotaOpzioni))
            Return
        End If
        Try
            eventiOpzioni.Clear()
            If menuOpzioni IsNot Nothing AndAlso Not menuOpzioni.IsDisposed Then
                menuOpzioni.Items.Clear()
            End If
        Catch ex As Exception
            _logger.ScriviLog("CampiFormInterno.SvuotaOpzioni: " & ex.Message)
        End Try
    End Sub

    Private Sub apriOpzioni()
        If Me.IsDisposed OrElse Me.Disposing Then Return

        Try
            If menuOpzioniAperto Then Return

            _campoConFocusMenu = TryCast(Me.ActiveControl, WGCampiMovimento)
            _controlloConFocusMenu = Me.ActiveControl

            If menuOpzioni Is Nothing OrElse menuOpzioni.IsDisposed Then
                menuOpzioni = New WGListaView()
                menuOpzioni.Visible = False
                Me.Controls.Add(menuOpzioni)
                AddHandler menuOpzioni.SelectedIndexChanged, AddressOf menuOpzioni_SelectedIndexChanged
                AddHandler menuOpzioni.Indietro, AddressOf vaiIndietroOpzioni
                AddHandler menuOpzioni.Avanti, AddressOf vaiAvantiOpzioni
            End If

            menuOpzioniAperto = True

            For Each campo In _listaCampi
                If campo.Controllo IsNot Nothing AndAlso campo.Controllo.Visible Then
                    ShowWindow(campo.Controllo.Handle, SW_HIDE)
                End If
            Next

            menuOpzioni.Titolo = "Opzioni Campi"
            menuOpzioni.Visible = True
            menuOpzioni.Dock = DockStyle.Fill
            menuOpzioni.BringToFront()

            menuOpzioni.Items.Clear()

            If _campoConFocusMenu IsNot Nothing Then
                menuOpzioni.Items.Add("Stampa")
                menuOpzioni.Items.Add("Taglia")
                menuOpzioni.Items.Add("Copia")
                menuOpzioni.Items.Add("Incolla")
                menuOpzioni.Items.Add("Incolla e sostituisci")
                menuOpzioni.Items.Add("Copia tutto")
                menuOpzioni.Items.Add("Seleziona tutto")
                menuOpzioni.Items.Add("Svuota campo")
                If WGDettatura.SistemaSupportato Then
                    menuOpzioni.Items.Add("Dettatura")
                End If
                menuOpzioni.Items.Add("Chiudi e conferma")

                Dim dati As MetadatiCampo = Nothing
                If _mappaIndiciCampi.ContainsKey(_campoConFocusMenu) Then
                    dati = _mappaIndiciCampi(_campoConFocusMenu)
                End If

                If dati IsNot Nothing AndAlso dati.Tipo = TipoCampo.Password Then
                    If _campoConFocusMenu.UseSystemPasswordChar Then
                        menuOpzioni.Items.Add("Mostra password")
                    Else
                        menuOpzioni.Items.Add("Nascondi password")
                    End If

                    If _impostazioniComuni.LeggiPasswordAttivo Then
                        menuOpzioni.Items.Add("Non leggere la password durante la digitazione")
                    Else
                        menuOpzioni.Items.Add("Leggi la password in chiaro durante la digitazione")
                    End If
                End If
            ElseIf TypeOf _controlloConFocusMenu Is WGWord Then
                menuOpzioni.Items.Add("Stampa")
                If WGDettatura.SistemaSupportato Then
                    menuOpzioni.Items.Add("Dettatura")
                End If
                menuOpzioni.Items.Add("Chiudi e conferma")
            End If

            For Each v As Voce In eventiOpzioni
                If Not menuOpzioni.Items.Contains(v.Nome) Then
                    menuOpzioni.Items.Add(v.Nome)
                End If
            Next

            If menuOpzioni.Items.Count = 0 Then
                chiudiOpzioni()
                Dim messaggio As New WGMessaggi()
                messaggio.Messaggio("Non ci sono opzioni disponibili")
                messaggio.ShowDialog()
                Return
            End If

            If ultimoElementoOpzioni >= 0 AndAlso ultimoElementoOpzioni < menuOpzioni.Items.Count Then
                menuOpzioni.SelectedIndex = ultimoElementoOpzioni
            Else
                menuOpzioni.SelectedIndex = 0
            End If
            menuOpzioni.Focus()
            _sintesiVocale.LeggiTesto(menuOpzioni.Titolo)

        Catch ex As Exception
            _logger.ScriviLog("CampiFormInterno.apriOpzioni: " & ex.Message)
        End Try
    End Sub

    Private Sub ChiudiOpzioniConAnnuncioCampo()
        Try
            chiudiOpzioni()
            Dim casellaCorrente As WGCampiMovimento = TryCast(Me.ActiveControl, WGCampiMovimento)
            If casellaCorrente IsNot Nothing Then
                LeggiCampoAttivo(casellaCorrente)
            End If
        Catch ex As Exception
            _logger.ScriviLog("CampiFormInterno.ChiudiOpzioniConAnnuncioCampo: " & ex.Message)
        End Try
    End Sub

    Private Sub chiudiOpzioni()
        If Me.IsDisposed OrElse Me.Disposing Then Return
        If Me.InvokeRequired Then
            Me.Invoke(New MethodInvoker(AddressOf chiudiOpzioni))
            Return
        End If

        Try
            If menuOpzioni IsNot Nothing AndAlso Not menuOpzioni.IsDisposed Then
                ultimoElementoOpzioni = menuOpzioni.SelectedIndex
                menuOpzioni.Dispose()
            End If
            menuOpzioniAperto = False

            Me.Controls.Remove(menuOpzioni)

            ' A livello di Control (anziche' solo WGCampiMovimento) per non perdere il
            ' riferimento quando il campo che aveva il focus prima di aprire il menu era un
            ' campo Word (WGWord): un TryCast(..., WGCampiMovimento) su un WGWord torna sempre
            ' Nothing, perche' WGWord non eredita da WGCampiMovimento, e il campo restava senza
            ' focus reale una volta richiuso il menu.
            Dim controlloCorrente As Control = Nothing
            If ControlloDaRipristinareValido(_controlloConFocusMenu) Then
                controlloCorrente = _controlloConFocusMenu
            End If
            If controlloCorrente Is Nothing AndAlso ControlloDaRipristinareValido(Me.ActiveControl) Then
                controlloCorrente = Me.ActiveControl
            End If
            If controlloCorrente Is Nothing AndAlso _listaCampi.Count > 0 Then
                controlloCorrente = If(_listaCampi(0).EWord, CType(_listaCampi(0).ControlloWord, Control), _listaCampi(0).Controllo)
            End If

            If controlloCorrente IsNot Nothing Then
                ' AggiornaVisibilitaCampi lavora sulla logica di visibilita' dei campi normali
                ' (WGCampiMovimento): per un campo Word la visibilita' e' gia' gestita altrove
                ' (DisponiSoloCampoWord/NascondiTuttiICampiWord), quindi la invochiamo solo se il
                ' campo da ripristinare non e' un campo Word.
                Dim campoNormaleCorrente As WGCampiMovimento = TryCast(controlloCorrente, WGCampiMovimento)
                If campoNormaleCorrente IsNot Nothing Then
                    AggiornaVisibilitaCampi(campoNormaleCorrente)
                End If

                ' Vedi ForzaRipristinoFocusFinestra: senza questo passaggio la finestra puo' restare
                ' visivamente in primo piano ma non avere davvero il focus di Windows, e i tasti
                ' digitati nel campo non arriverebbero da nessuna parte pur sembrando tutto normale
                ' (bordo blu, cursore lampeggiante). Stesso fix usato in RipristinaFocusDopoMessaggio.
                ' Vale per qualunque tipo di campo, Word compreso.
                ForzaRipristinoFocusFinestra()
                RipristinaFocusSuCampo(controlloCorrente)
            End If

        Catch ex As Exception
            _logger.ScriviLog("CampiFormInterno.chiudiOpzioni: " & ex.Message)
        End Try
    End Sub

    Private Function ControlloDaRipristinareValido(controllo As Control) As Boolean
        Try
            If controllo Is Nothing Then Return False
            If IsControlDisposed(controllo) Then Return False
            For Each dati As MetadatiCampo In _listaCampi
                If dati Is Nothing Then Continue For
                If dati.Controllo IsNot Nothing AndAlso dati.Controllo Is controllo Then Return True
                If dati.ControlloWord IsNot Nothing AndAlso dati.ControlloWord Is controllo Then Return True
            Next
            Return False
        Catch ex As Exception
            _logger.ScriviLog("CampiFormInterno.ControlloDaRipristinareValido: " & ex.Message)
            Return False
        End Try
    End Function

    Private Sub menuOpzioni_SelectedIndexChanged(sender As Object, e As EventArgs)
        If Me.IsDisposed OrElse Me.Disposing Then Return
        If menuOpzioni Is Nothing OrElse menuOpzioni.IsDisposed Then Return
        Try
            If menuOpzioni.SelectedItem IsNot Nothing Then
                ultimoElementoOpzioni = menuOpzioni.SelectedIndex
            End If
        Catch ex As Exception
            _logger.ScriviLog("CampiFormInterno.menuOpzioni_SelectedIndexChanged: " & ex.Message)
        End Try
    End Sub

    Private Sub vaiIndietroOpzioni()
        Try
            chiudiOpzioni()
        Catch ex As Exception
            _logger.ScriviLog("CampiFormInterno.vaiIndietroOpzioni: " & ex.Message)
        End Try
    End Sub

    Private Sub vaiAvantiOpzioni()
        Try
            If menuOpzioni IsNot Nothing AndAlso Not menuOpzioni.IsDisposed Then
                If menuOpzioni.SelectedItem IsNot Nothing Then
                    Dim testoVoce As String = menuOpzioni.SelectedItem.ToString()
                    Dim handled As Boolean = True

                    Select Case testoVoce
                        Case "Stampa"
                            chiudiOpzioni()
                            StampaCampoCorrente()
                            Return
                        Case "Taglia"
                            If _campoConFocusMenu IsNot Nothing Then _campoConFocusMenu.TagliaTesto()
                        Case "Copia"
                            If _campoConFocusMenu IsNot Nothing Then _campoConFocusMenu.CopiaTesto()
                        Case "Incolla"
                            If _campoConFocusMenu IsNot Nothing Then _campoConFocusMenu.IncollaTesto()
                        Case "Incolla e sostituisci"
                            If _campoConFocusMenu IsNot Nothing AndAlso Not _campoConFocusMenu.ReadOnly AndAlso Clipboard.ContainsText() Then
                                _campoConFocusMenu.Text = Clipboard.GetText()
                                _campoConFocusMenu.SelectionStart = _campoConFocusMenu.TextLength
                                _sintesiVocale.LeggiTesto("Incollato e sostituito")
                            End If
                        Case "Copia tutto"
                            If _campoConFocusMenu IsNot Nothing AndAlso _campoConFocusMenu.TextLength > 0 Then
                                If _campoConFocusMenu.IsCampoPassword AndAlso Not _campoConFocusMenu.leggipassword Then
                                    _sintesiVocale.LeggiTesto("Impossibile copiare il contenuto di un campo password protetto")
                                Else
                                    Clipboard.SetText(_campoConFocusMenu.Text)
                                    _sintesiVocale.LeggiTesto("Tutto copiato negli appunti")
                                End If
                            End If
                        Case "Seleziona tutto"
                            If _campoConFocusMenu IsNot Nothing Then _campoConFocusMenu.SelezionaTutto()
                        Case "Svuota campo"
                            If _campoConFocusMenu IsNot Nothing AndAlso Not _campoConFocusMenu.ReadOnly Then
                                _campoConFocusMenu.Text = ""
                                _sintesiVocale.LeggiTesto("Campo svuotato")
                            End If
                        Case "Chiudi e conferma"
                            chiudiOpzioni()
                            EseguiInvioFinale()
                            Return
                        Case "Mostra password"
                            If _campoConFocusMenu IsNot Nothing Then
                                _campoConFocusMenu.UseSystemPasswordChar = False
                                _sintesiVocale.LeggiTesto("Password in chiaro")
                            End If
                        Case "Nascondi password"
                            If _campoConFocusMenu IsNot Nothing Then
                                _campoConFocusMenu.UseSystemPasswordChar = True
                                _sintesiVocale.LeggiTesto("Password nascosta")
                            End If
                        Case "Leggi la password in chiaro durante la digitazione"
                            If _campoConFocusMenu IsNot Nothing Then
                                _campoConFocusMenu.leggipassword = True
                                _sintesiVocale.LeggiTesto("Lettura password in chiaro attivata")
                            End If
                        Case "Non leggere la password durante la digitazione"
                            If _campoConFocusMenu IsNot Nothing Then
                                _campoConFocusMenu.leggipassword = False
                                _sintesiVocale.LeggiTesto("Lettura password disattivata")
                            End If
                        Case Else
                            handled = False
                    End Select

                    If Not handled Then
                        Dim voceEvento = eventiOpzioni.FirstOrDefault(Function(v) v.Nome = testoVoce)
                        If voceEvento IsNot Nothing Then
                            voceEvento.Evento?.Invoke(Me, EventArgs.Empty)
                        End If
                    End If
                End If
            End If
            chiudiOpzioni()
        Catch ex As Exception
            _logger.ScriviLog("CampiFormInterno.vaiAvantiOpzioni: " & ex.Message)
        End Try
    End Sub

    Public Sub AvviaForm()
        If Me.InvokeRequired Then
            Me.Invoke(New MethodInvoker(AddressOf AvviaForm))
            Return
        End If
        Try
            If WGContenitore.Istanza IsNot Nothing Then WGContenitore.Istanza.MostraControllo(Me)
            AggiornaTastieraTouk()
            SelezionaPrimoCampoDisponibile()
        Catch ex As Exception
            _logger.ScriviLog("CampiFormInterno.AvviaForm: " & ex.Message)
        End Try
    End Sub

    Private Function GestisciPressioneTastoHook(key As Keys, ctrlPremuto As Boolean, shiftPremuto As Boolean) As Boolean
        Try
            If _messaggioErroreAttivo Then Return False

            ' Mentre un elenco scelto da un campo è aperto, la tastiera è sua; mentre si prepara, Invio non fa niente.
            If _elencoInCostruzione Then Return True
            If _elencoAperto IsNot Nothing Then
                If _elencoAperto.Chiusa OrElse Not _elencoAperto.PuoRicevereInput() Then
                    _elencoAperto = Nothing
                Else
                    Return False
                End If
            End If

            Dim insertPremuto As Boolean = Control.IsKeyLocked(Keys.Insert)

            Dim casellaCorrente As WGCampiMovimento = TryCast(Me.ActiveControl, WGCampiMovimento)
            If casellaCorrente Is Nothing Then Return False

            If key = Keys.Apps OrElse (key = Keys.F10 AndAlso shiftPremuto) Then
                If Me.InvokeRequired Then
                    Me.Invoke(New MethodInvoker(AddressOf apriOpzioni))
                Else
                    apriOpzioni()
                End If
                Return True
            End If

            If key = Keys.Down AndAlso insertPremuto Then
                LeggiTuttoDaCursore(casellaCorrente)
                Return True
            End If

            Select Case key
                Case Keys.Enter
                    If casellaCorrente.Multiline AndAlso Not ctrlPremuto Then Return False
                    GestisciTastoInvio(casellaCorrente)
                    Return True
            End Select

            Return False

        Catch ex As Exception
            _logger.ScriviLog("CampiFormInterno.GestisciPressioneTastoHook: " & ex.Message)
            Return False
        End Try
    End Function

    Private Sub LeggiTuttoDaCursore(campo As WGCampiMovimento)
        Try
            Dim index As Integer = campo.SelectionStart
            If index >= 0 AndAlso index < campo.TextLength Then
                Dim testo As String = campo.Text.Substring(index)
                If Not String.IsNullOrEmpty(testo) Then
                    _sintesiVocale.LeggiTesto(testo)
                End If
            End If
        Catch ex As Exception
            _logger.ScriviLog("CampiFormInterno.LeggiTuttoDaCursore: " & ex.Message)
        End Try
    End Sub

    Private _ultimaCasellaAnnunciata As WGCampiMovimento = Nothing
    Private _tickUltimoAnnuncioCampo As Integer = 0

    Private Sub LeggiCampoAttivo(casellaAttiva As WGCampiMovimento)
        Try
            If casellaAttiva Is _ultimaCasellaAnnunciata AndAlso (Environment.TickCount - _tickUltimoAnnuncioCampo) < 900 Then Return
            _ultimaCasellaAnnunciata = casellaAttiva
            _tickUltimoAnnuncioCampo = Environment.TickCount
            If casellaAttiva IsNot Nothing AndAlso _mappaIndiciCampi.ContainsKey(casellaAttiva) Then
                Dim dati As MetadatiCampo = _mappaIndiciCampi(casellaAttiva)
                ' Un campo data da scegliere dall'elenco non si annuncia come campo di editazione: si apre l'elenco, che dice "Data".
                If dati.Tipo = TipoCampo.Data AndAlso Not dati.DataManuale AndAlso Not dati.SolaLettura Then Return
                If dati.Scelte IsNot Nothing Then Return
                Dim titoloClean As String = dati.TitoloBase.Trim()
                Dim testoLettura As String = titoloClean & ". "
                If dati.EMultiplo Then
                    Dim elementiGruppo = _listaCampi.Where(Function(x) x.IdBaseMultiplo = dati.IdBaseMultiplo).ToList()
                    Dim posInGruppo As Integer = elementiGruppo.IndexOf(dati) + 1
                    testoLettura &= OttieniNumeroOrdinale(posInGruppo) & " "
                End If

                If dati.Obbligatorio Then
                    testoLettura &= "campo di editazione obbligatorio"
                Else
                    testoLettura &= "campo di editazione"
                End If

                If casellaAttiva.IsCampoPassword AndAlso Not casellaAttiva.leggipassword Then
                ElseIf Not String.IsNullOrEmpty(casellaAttiva.Text) Then
                    ' Entrando in un campo su più righe si legge solo la prima riga, non tutto il testo.
                    Dim testoDaLeggere As String = casellaAttiva.Text
                    If casellaAttiva.Multiline Then
                        Dim finePrimaRiga As Integer = testoDaLeggere.IndexOfAny(New Char() {ControlChars.Lf, ControlChars.Cr})
                        If finePrimaRiga >= 0 Then testoDaLeggere = testoDaLeggere.Substring(0, finePrimaRiga)
                    End If
                    If Not String.IsNullOrWhiteSpace(testoDaLeggere) Then testoLettura &= ", " & testoDaLeggere
                End If
                _sintesiVocale.LeggiTesto(testoLettura, escludiPunteggiatura:=True)
            End If
        Catch ex As Exception
            _logger.ScriviLog("CampiFormInterno.LeggiCampoAttivo: " & ex.Message)
        End Try
    End Sub

    Public Sub AggiungiCampoScelta(titolo As String, id As String, voci As IEnumerable(Of KeyValuePair(Of String, String)), obbligatorio As Boolean, idPredefinito As String)
        If Me.InvokeRequired Then
            Me.Invoke(New MethodInvoker(Sub() Me.AggiungiCampoScelta(titolo, id, voci, obbligatorio, idPredefinito)))
            Return
        End If
        Try
            Dim scelte As New List(Of KeyValuePair(Of String, String))(voci)
            Dim predefinita As String = ""
            For Each s As KeyValuePair(Of String, String) In scelte
                If s.Key = idPredefinito Then predefinita = s.Value
            Next
            AggiungiCampoGenerico(titolo, id, obbligatorio, False, predefinita, TipoCampo.Alfanumerico, False)
            Dim dati As MetadatiCampo = _listaCampi.LastOrDefault(Function(x) x.Identificativo = id AndAlso Not x.EMultiplo)
            If dati Is Nothing Then Return
            dati.Scelte = scelte
            dati.IdScelto = If(predefinita <> "", idPredefinito, "")
            dati.Controllo.ReadOnly = True
            dati.Elenco = Function(testo As String, conferma As Action(Of String)) Task.FromResult(CostruisciElencoScelta(dati, conferma))
        Catch ex As Exception
            _logger.ScriviLog("CampiFormInterno.AggiungiCampoScelta: " & ex.Message)
        End Try
    End Sub

    Public Function OttieniIdScelta(id As String) As String
        Try
            Dim dati As MetadatiCampo = _listaCampi.FirstOrDefault(Function(x) x.Identificativo = id AndAlso Not x.EMultiplo)
            If dati IsNot Nothing AndAlso dati.Scelte IsNot Nothing Then Return If(dati.IdScelto, "")
        Catch ex As Exception
            _logger.ScriviLog("CampiFormInterno.OttieniIdScelta: " & ex.Message)
        End Try
        Return ""
    End Function

    ''' <summary>L'elenco di un campo menu: una voce per scelta, posizionato su quella attuale.</summary>
    Private Function CostruisciElencoScelta(dati As MetadatiCampo, conferma As Action(Of String)) As WGMenuLista
        Dim lista As New WGMenuLista()
        lista.invioNonConsentito = False
        lista.multiselezioneConsentita = False
        lista.impostaTitolo(dati.TitoloBase)
        For Each s As KeyValuePair(Of String, String) In dati.Scelte
            lista.aggiungiElemento(s.Value, s.Key)
        Next
        If dati.IdScelto <> "" Then lista.posiziona(dati.IdScelto)
        AddHandler lista.ElementoSelezionato,
            Sub(idVoce As String)
                Try
                    Dim testo As String = ""
                    For Each s As KeyValuePair(Of String, String) In dati.Scelte
                        If s.Key = idVoce Then testo = s.Value
                    Next
                    dati.IdScelto = idVoce
                    lista.abbandona()
                    conferma(testo)
                Catch ex As Exception
                    _logger.ScriviLog("CampiFormInterno.CostruisciElencoScelta (scelta): " & ex.Message)
                End Try
            End Sub
        Return lista
    End Function

    ''' <summary>Toglie un campo dalla schermata (non quello in cui ci si trova). I campi che restano si ricollegano fra loro.</summary>
    Public Sub RimuoviCampoPerId(id As String)
        If Me.InvokeRequired Then
            Me.Invoke(New MethodInvoker(Sub() Me.RimuoviCampoPerId(id)))
            Return
        End If
        Try
            Dim dati As MetadatiCampo = _listaCampi.FirstOrDefault(Function(x) x.Identificativo = id AndAlso Not x.EMultiplo AndAlso Not x.EWord)
            If dati Is Nothing OrElse dati.Controllo Is Nothing Then Return
            If ReferenceEquals(Me.ActiveControl, dati.Controllo) Then Return
            _listaCampi.Remove(dati)
            _valoriSalvati.Remove(dati)
            _mappaIndiciCampi.Remove(dati.Controllo)
            Me.Controls.Remove(dati.Controllo)
            dati.Controllo.Dispose()
            For Each altro As MetadatiCampo In _listaCampi
                If altro.Controllo IsNot Nothing Then altro.Controllo.NavigazioneDirettaCampi = (_listaCampi.Count > 1)
            Next
        Catch ex As Exception
            _logger.ScriviLog("CampiFormInterno.RimuoviCampoPerId: " & ex.Message)
        End Try
    End Sub

    Public Sub ImpostaElencoCampo(id As String, costruisci As Func(Of String, Action(Of String), Task(Of WGMenuLista)))
        Try
            For Each dati As MetadatiCampo In _listaCampi
                If dati.Identificativo = id AndAlso Not dati.EMultiplo Then
                    dati.Elenco = costruisci
                    Return
                End If
            Next
        Catch ex As Exception
            _logger.ScriviLog("CampiFormInterno.ImpostaElencoCampo: " & ex.Message)
        End Try
    End Sub

    ''' <summary>Invio su un campo: se ha un elenco (o è un campo data) si apre l'elenco da cui scegliere, altrimenti si conferma il campo.</summary>
    Private Sub GestisciTastoInvio(casellaCorrente As WGCampiMovimento)
        Try
            If casellaCorrente Is Nothing Then Return
            Dim dati As MetadatiCampo = Nothing
            _mappaIndiciCampi.TryGetValue(casellaCorrente, dati)
            If dati IsNot Nothing Then
                If dati.Elenco IsNot Nothing Then
                    ApriElencoCampo(casellaCorrente, dati)
                    Return
                End If
                If dati.Tipo = TipoCampo.Data AndAlso Not dati.DataManuale AndAlso Not dati.SolaLettura Then
                    ApriElencoDate(casellaCorrente, dati)
                    Return
                End If
            End If
            ConfermaCampoECambia(casellaCorrente)
        Catch ex As Exception
            _logger.ScriviLog("CampiFormInterno.GestisciTastoInvio: " & ex.Message)
        End Try
    End Sub

    ''' <summary>Mette il testo scelto nel campo, lo conferma e passa al successivo.</summary>
    Private Sub ConfermaConTesto(casella As WGCampiMovimento, testo As String)
        Try
            _chiusuraElencoTick = Environment.TickCount
            _campoChiusuraElenco = casella
            _elencoAperto = Nothing
            Me.BeginInvoke(New MethodInvoker(
                Sub()
                    Try
                        If casella.IsDisposed Then Return
                        casella.Text = testo
                        ' Il campo appena riempito non si rilegge (titolo e testo) mentre il focus ci torna: si passa subito al prossimo.
                        _ultimaCasellaAnnunciata = casella
                        _tickUltimoAnnuncioCampo = Environment.TickCount
                        RipristinaFocusSuCampo(casella)
                        ConfermaCampoECambia(casella)
                    Catch ex As Exception
                        _logger.ScriviLog("CampiFormInterno.ConfermaConTesto (interno): " & ex.Message)
                    End Try
                End Sub))
        Catch ex As Exception
            _logger.ScriviLog("CampiFormInterno.ConfermaConTesto: " & ex.Message)
        End Try
    End Sub

    ''' <summary>
    ''' Mostra l'elenco di un campo. Con esciConEsc (sempre, per ogni elenco di un campo: menu, date, suggerimenti) tornare indietro con Esc
    ''' chiude tutto il modulo, come Esc su un campo: vuol dire che non si vuole compilare niente, e il campo non deve ricomparire.
    ''' </summary>
    Private Sub MostraElencoDelCampo(lista As WGMenuLista, casella As WGCampiMovimento, Optional esciConEsc As Boolean = False)
        _elencoAperto = lista
        ' Ctrl freccia giù e Ctrl freccia su: l'elenco si chiude e si passa al campo successivo o precedente.
        AddHandler lista.ScorciatoiaCtrl,
            Sub(mittente As Object, e As ScorciatoiaCtrlEventArgs)
                Try
                    If e.Codice <> Keys.Down AndAlso e.Codice <> Keys.Up Then Return
                    e.Gestita = True
                    Dim avanti As Boolean = (e.Codice = Keys.Down)
                    _chiusuraElencoTick = Environment.TickCount
                    _campoChiusuraElenco = casella
                    _elencoAperto = Nothing
                    lista.abbandona()
                    Me.BeginInvoke(New MethodInvoker(
                        Sub()
                            Try
                                If avanti Then NavigaAlProssimoCampo(casella) Else NavigaAlCampoPrecedente()
                            Catch ex2 As Exception
                                _logger.ScriviLog("CampiFormInterno.MostraElencoDelCampo (Ctrl su/giù interno): " & ex2.Message)
                            End Try
                        End Sub))
                Catch ex As Exception
                    _logger.ScriviLog("CampiFormInterno.MostraElencoDelCampo (Ctrl su/giù): " & ex.Message)
                End Try
            End Sub
        AddHandler lista.escPremuto,
            Sub()
                Try
                    _chiusuraElencoTick = Environment.TickCount
                    _campoChiusuraElenco = casella
                    _elencoAperto = Nothing
                    lista.abbandona()
                    If esciConEsc Then
                        Me.BeginInvoke(New MethodInvoker(AddressOf GestisciTastoEscape))
                    Else
                        Me.BeginInvoke(New MethodInvoker(Sub() RipristinaFocusSuCampo(casella)))
                    End If
                Catch ex As Exception
                    _logger.ScriviLog("CampiFormInterno.MostraElencoDelCampo (Esc): " & ex.Message)
                End Try
            End Sub
        lista.avvia()
    End Sub

    Private Async Sub ApriElencoCampo(casella As WGCampiMovimento, dati As MetadatiCampo)
        Try
            _elencoInCostruzione = True
            Dim lista As WGMenuLista = Nothing
            Try
                lista = Await dati.Elenco(casella.Text, Sub(testo As String) ConfermaConTesto(casella, testo))
            Finally
                _elencoInCostruzione = False
            End Try
            If lista Is Nothing Then Return
            MostraElencoDelCampo(lista, casella, True)
        Catch ex As Exception
            _elencoInCostruzione = False
            _logger.ScriviLog("CampiFormInterno.ApriElencoCampo: " & ex.Message)
        End Try
    End Sub

    ''' <summary>Passa il campo data dalla scelta nell'elenco alla scrittura a mano e viceversa.</summary>
    Private Sub ImpostaDataManuale(dati As MetadatiCampo, manuale As Boolean)
        Try
            dati.DataManuale = manuale
            If dati.Controllo IsNot Nothing AndAlso Not dati.SolaLettura Then dati.Controllo.ReadOnly = Not manuale
        Catch ex As Exception
            _logger.ScriviLog("CampiFormInterno.ImpostaDataManuale: " & ex.Message)
        End Try
    End Sub

    ''' <summary>Vero se un elenco scelto da un campo è aperto e riceve la tastiera adesso; se è già chiuso lo dimentica.</summary>
    Private Function ElencoAttivo() As Boolean
        If _elencoAperto Is Nothing Then Return False
        If _elencoAperto.Chiusa OrElse Not _elencoAperto.PuoRicevereInput() Then
            _elencoAperto = Nothing
            Return False
        End If
        Return True
    End Function

    ''' <summary>
    ''' Arrivando su un campo data si apre subito l'elenco delle date, senza dover premere Invio: il campo da scrivere si usa solo con
    ''' "Scrivi la data a mano". Non si riapre se l'elenco di quel campo si è appena chiuso (Esc o scelta) e il focus sta tornando sul campo; passando da un campo con elenco al campo data, invece, si apre.
    ''' </summary>
    Private Sub ApriElencoDateAlIngresso(casella As WGCampiMovimento)
        Try
            Dim dati As MetadatiCampo = Nothing
            If Not _mappaIndiciCampi.TryGetValue(casella, dati) Then Return
            Dim campoMenu As Boolean = (dati.Scelte IsNot Nothing)
            If Not campoMenu AndAlso (dati.Tipo <> TipoCampo.Data OrElse dati.DataManuale OrElse dati.SolaLettura OrElse dati.Elenco IsNot Nothing) Then Return
            If ElencoAttivo() OrElse _elencoInCostruzione OrElse _aperturaDatePianificata Then Return
            If ReferenceEquals(casella, _campoChiusuraElenco) AndAlso Environment.TickCount - _chiusuraElencoTick < 800 Then Return
            If Not Me.IsHandleCreated Then Return
            _aperturaDatePianificata = True
            Me.BeginInvoke(New MethodInvoker(
                Sub()
                    Try
                        _aperturaDatePianificata = False
                        If casella.IsDisposed OrElse Not ReferenceEquals(Me.ActiveControl, casella) Then Return
                        If ElencoAttivo() OrElse dati.DataManuale Then Return
                        If ReferenceEquals(casella, _campoChiusuraElenco) AndAlso Environment.TickCount - _chiusuraElencoTick < 800 Then Return
                        If dati.Scelte IsNot Nothing Then ApriElencoCampo(casella, dati) Else ApriElencoDate(casella, dati)
                    Catch ex As Exception
                        _logger.ScriviLog("CampiFormInterno.ApriElencoDateAlIngresso (interno): " & ex.Message)
                    End Try
                End Sub))
        Catch ex As Exception
            _logger.ScriviLog("CampiFormInterno.ApriElencoDateAlIngresso: " & ex.Message)
        End Try
    End Sub

    ''' <summary>
    ''' L'elenco dei giorni che si apre con Invio su un campo data: oggi, domani e così via per due mesi. Con Alt c'è "Scrivi la data a
    ''' mano": il campo resta com'è e il prossimo Invio lo conferma senza elenco.
    ''' </summary>
    Private Sub ApriElencoDate(casella As WGCampiMovimento, dati As MetadatiCampo)
        Try
            Dim it As CultureInfo = CultureInfo.GetCultureInfo("it-IT")
            Dim oggi As DateTime = DateTime.Today
            Dim lista As New WGMenuLista()
            lista.invioNonConsentito = False
            lista.multiselezioneConsentita = False
            lista.impostaTitolo("Data")
            Dim scritta As DateTime? = Nothing
            Dim analisi As DateTime? = Nothing
            If New WGUtilsDataOra().AnalizzaData(casella.Text, analisi) AndAlso analisi.HasValue Then scritta = analisi.Value.Date

            ' Le voci di un gruppo di giorni consecutivi a partire da uno dato.
            Dim vociDal As Func(Of DateTime, Integer, List(Of ElementoLista)) =
                Function(primo As DateTime, quanti As Integer) As List(Of ElementoLista)
                    Dim voci As New List(Of ElementoLista)()
                    For i As Integer = 0 To quanti - 1
                        Dim g As DateTime = primo.AddDays(i)
                        Dim testo As String = If(g = oggi, "Oggi, ", If(g = oggi.AddDays(1), "Domani, ", "")) & g.ToString(If(g.Year = oggi.Year, "dddd d MMMM", "dddd d MMMM yyyy"), it)
                        voci.Add(New ElementoLista(testo, g.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)))
                    Next
                    Return voci
                End Function

            ' Cinquanta giorni subito; arrivando in fondo se ne caricano altri sessanta, e così via.
            Const GiorniPerVolta As Integer = 60
            Const GiorniMassimi As Integer = 1500
            Dim prossimoGiorno As DateTime = oggi
            ' Se la data scritta è prima di oggi o molto più avanti, l'elenco parte da lei.
            If scritta.HasValue AndAlso (scritta.Value < oggi OrElse scritta.Value > oggi.AddDays(GiorniPerVolta - 1)) Then prossimoGiorno = scritta.Value
            lista.AggiungiElementi(vociDal(prossimoGiorno, GiorniPerVolta))
            prossimoGiorno = prossimoGiorno.AddDays(GiorniPerVolta)
            AddHandler lista.finelista,
                Sub(s, e)
                    Try
                        If (prossimoGiorno - oggi).TotalDays > GiorniMassimi Then Return
                        lista.AggiungiElementi(vociDal(prossimoGiorno, GiorniPerVolta))
                        prossimoGiorno = prossimoGiorno.AddDays(GiorniPerVolta)
                    Catch ex As Exception
                        _logger.ScriviLog("CampiFormInterno.ApriElencoDate (altre date): " & ex.Message)
                    End Try
                End Sub
            If scritta.HasValue Then lista.posiziona(scritta.Value.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture))
            lista.aggiungiOpzione("Scrivi la data a mano",
                Sub(s, e)
                    Try
                        ImpostaDataManuale(dati, True)
                        _chiusuraElencoTick = Environment.TickCount
                        _campoChiusuraElenco = casella
                        _elencoAperto = Nothing
                        lista.abbandona()
                        Me.BeginInvoke(New MethodInvoker(Sub() RipristinaFocusSuCampo(casella)))
                    Catch ex As Exception
                        _logger.ScriviLog("CampiFormInterno.ApriElencoDate (a mano): " & ex.Message)
                    End Try
                End Sub)
            AddHandler lista.ElementoSelezionato,
                Sub(id As String)
                    Try
                        Dim giorno As DateTime = DateTime.ParseExact(id, "yyyy-MM-dd", CultureInfo.InvariantCulture)
                        _chiusuraElencoTick = Environment.TickCount
                        _campoChiusuraElenco = casella
                        _elencoAperto = Nothing
                        lista.abbandona()
                        ConfermaConTesto(casella, giorno.ToShortDateString())
                    Catch ex As Exception
                        _logger.ScriviLog("CampiFormInterno.ApriElencoDate (scelta): " & ex.Message)
                    End Try
                End Sub
            MostraElencoDelCampo(lista, casella, True)
        Catch ex As Exception
            _logger.ScriviLog("CampiFormInterno.ApriElencoDate: " & ex.Message)
        End Try
    End Sub

    Private Sub ConfermaCampoECambia(casellaCorrente As WGCampiMovimento)
        Try
            If casellaCorrente Is Nothing Then Return
            Dim campoValido As Boolean = EseguiValidazioneCampo(casellaCorrente)

            If campoValido Then
                Dim datiCampoConfermato As MetadatiCampo = Nothing
                _mappaIndiciCampi.TryGetValue(casellaCorrente, datiCampoConfermato)
                If datiCampoConfermato IsNot Nothing AndAlso datiCampoConfermato.Tipo = TipoCampo.Data Then ImpostaDataManuale(datiCampoConfermato, False)
                RaiseEvent EventoCampoConfermato(If(datiCampoConfermato IsNot Nothing, datiCampoConfermato.Identificativo, ""))

                Dim indiceCorrente As Integer = OttieniIndiceCampoDaControllo(casellaCorrente)
                Dim eUltimoCampo As Boolean = (indiceCorrente = _listaCampi.Count - 1)

                If eUltimoCampo Then
                    If _azioniUscita IsNot Nothing AndAlso _azioniUscita.Count > 0 Then
                        MostraMenuUscita()
                    Else
                        EseguiInvioFinale()
                    End If
                Else
                    NavigaAlProssimoCampo(casellaCorrente)
                End If
            End If
        Catch ex As Exception
            _logger.ScriviLog("CampiFormInterno.ConfermaCampoECambia: " & ex.Message)
        End Try
    End Sub

    Private Sub GestisciFrecciaGiu(casellaCorrente As WGCampiMovimento)
        Try
            If casellaCorrente IsNot Nothing Then
                If EseguiValidazioneCampo(casellaCorrente) Then
                    Dim indiceCorrente As Integer = OttieniIndiceCampoDaControllo(casellaCorrente)
                    Dim eUltimoCampo As Boolean = (indiceCorrente = _listaCampi.Count - 1)

                    If eUltimoCampo Then
                        _sintesiVocale.LeggiTesto("fine testo")
                    Else
                        NavigaAlProssimoCampo(casellaCorrente)
                    End If
                End If
            End If
        Catch ex As Exception
            _logger.ScriviLog("CampiFormInterno.GestisciFrecciaGiu: " & ex.Message)
        End Try
    End Sub

    Private Sub GestisciFrecciaSu(casellaCorrente As WGCampiMovimento)
        Try
            Dim indiceCorrente As Integer = OttieniIndiceCampoDaControllo(casellaCorrente)
            If indiceCorrente = 0 Then
                _sintesiVocale.LeggiTesto("inizio testo")
            Else
                NavigaAlCampoPrecedente()
            End If
        Catch ex As Exception
            _logger.ScriviLog("CampiFormInterno.GestisciFrecciaSu: " & ex.Message)
        End Try
    End Sub

    Friend Property SalvaSuEscape As Boolean = False

    Private Sub SalvaDaDomandaEscape(sender As Object, e As EventArgs)
        Try
            EseguiInvioFinale()
        Catch ex As Exception
            _logger.ScriviLog("CampiFormInterno.SalvaDaDomandaEscape: " & ex.Message)
        End Try
    End Sub

    Private Sub NonSalvareDaDomandaEscape(sender As Object, e As EventArgs)
        Try
            EseguiChiusuraDaEscape()
        Catch ex As Exception
            _logger.ScriviLog("CampiFormInterno.NonSalvareDaDomandaEscape: " & ex.Message)
        End Try
    End Sub

    Private Sub GestisciTastoEscape()
        If Me.InvokeRequired Then
            Me.Invoke(New MethodInvoker(AddressOf GestisciTastoEscape))
            Return
        End If
        Try
            If _azioniUscita IsNot Nothing AndAlso _azioniUscita.Count > 0 Then
                MostraMenuUscita()
                Return
            End If

            If SalvaSuEscape Then
                Dim campoConTesto As Boolean = False
                For Each datiCampo As MetadatiCampo In _listaCampi
                    If datiCampo.Controllo IsNot Nothing AndAlso Not String.IsNullOrEmpty(datiCampo.Controllo.Text.Trim()) Then
                        campoConTesto = True
                        Exit For
                    End If
                Next
                If campoConTesto Then
                    Dim menuDomanda As New WGMenu()
                    menuDomanda.aggiungi("Salva le modifiche", AddressOf SalvaDaDomandaEscape)
                    menuDomanda.aggiungi("Non salvare", AddressOf NonSalvareDaDomandaEscape)
                    menuDomanda.avvia("Vuoi salvare le modifiche?", True)
                    Return
                End If
            End If

            EseguiChiusuraDaEscape()
        Catch ex As Exception
            _logger.ScriviLog("CampiFormInterno.GestisciTastoEscape: " & ex.Message)
        End Try
    End Sub

    Private Sub EseguiChiusuraDaEscape()
        If Me.InvokeRequired Then
            Me.Invoke(New MethodInvoker(AddressOf EseguiChiusuraDaEscape))
            Return
        End If
        Try
            _chiusuraDaEscape = True

            If _tastiera IsNot Nothing Then
                RemoveHandler _tastiera.KeyDown, AddressOf _tastiera_KeyDown
                RemoveHandler _tastiera.AltPremuto, AddressOf _tastiera_AltPremuto
                RemoveHandler _tastiera.AltDestroPremuto, AddressOf _tastiera_AltDestroPremuto
                RemoveHandler _tastiera.AltDestroRilasciato, AddressOf _tastiera_AltDestroRilasciato
            End If
            If _touk IsNot Nothing Then
                RemoveHandler _touk.FlickAvanti, AddressOf _touk_FlickAvanti
                RemoveHandler _touk.FlickIndietro, AddressOf _touk_FlickIndietro
                RemoveHandler _touk.TapDoppio, AddressOf _touk_TapDoppio
                RemoveHandler _touk.EscapeGesto, AddressOf _touk_EscapeGesto
                RemoveHandler _touk.RichiestaLetturaCompleta, AddressOf _touk_RichiestaLetturaCompleta
                RemoveHandler _touk.StatoCambiato, AddressOf _touk_StatoCambiato
                _touk = Nothing
            End If
            If _dettaturaCampi IsNot Nothing Then
                RemoveHandler _dettaturaCampi.TrascrizioneCompletata, AddressOf OnTrascrizioneCompletataCampi
                RemoveHandler _dettaturaCampi.TrascrizioneFallita, AddressOf OnTrascrizioneFallitaCampi
                _dettaturaCampi.Dispose()
                _dettaturaCampi = Nothing
            End If
            If _dettaturaCampiWord IsNot Nothing Then
                RemoveHandler _dettaturaCampiWord.TrascrizioneCompletata, AddressOf OnTrascrizioneCompletataCampoWord
                RemoveHandler _dettaturaCampiWord.TrascrizioneFallita, AddressOf OnTrascrizioneFallitaCampoWord
                _dettaturaCampiWord.Dispose()
                _dettaturaCampiWord = Nothing
            End If
            If _tastoDettatura IsNot Nothing Then
                RemoveHandler _tastoDettatura.Interrotto, AddressOf OnTastoDettaturaInterrotto
                If Not _tastoDettatura.IsDisposed Then _tastoDettatura.Dispose()
                _tastoDettatura = Nothing
            End If
            RaiseEvent EventoChiusura(Me, Nothing)
            If WGContenitore.Istanza IsNot Nothing Then WGContenitore.Istanza.RimuoviControllo(Me)
            Me.Dispose(True)
        Catch ex As Exception
            _logger.ScriviLog("CampiFormInterno.EseguiChiusuraDaEscape: " & ex.Message)
        End Try
    End Sub

    ''' <summary>
    ''' Chiude questa schermata in modo pulito (rimuove i gestori, esce dallo stack di
    ''' WGContenitore, libera le risorse) senza sollevare EventoChiusura ne' EventoFine: da usare
    ''' quando chi ha creato Campi ha gia' gestito da solo il proprio flusso post-chiusura (es.
    ''' dopo l'invio di un messaggio) e vuole solo che la finestra si chiuda, senza ricevere un
    ''' secondo evento che duplicherebbe la logica gia' eseguita.
    ''' </summary>
    Friend Sub ChiudiSilenziosamente()
        If Me.InvokeRequired Then
            Try
                Me.Invoke(New MethodInvoker(AddressOf ChiudiSilenziosamente))
            Catch ex As Exception
                _logger.ScriviLog("CampiFormInterno.ChiudiSilenziosamente (Invoke): " & ex.Message)
            End Try
            Return
        End If
        Try
            If Me.IsDisposed OrElse Me.Disposing Then Return

            If _tastiera IsNot Nothing Then
                RemoveHandler _tastiera.KeyDown, AddressOf _tastiera_KeyDown
                RemoveHandler _tastiera.AltPremuto, AddressOf _tastiera_AltPremuto
                RemoveHandler _tastiera.AltDestroPremuto, AddressOf _tastiera_AltDestroPremuto
                RemoveHandler _tastiera.AltDestroRilasciato, AddressOf _tastiera_AltDestroRilasciato
            End If
            If _touk IsNot Nothing Then
                RemoveHandler _touk.FlickAvanti, AddressOf _touk_FlickAvanti
                RemoveHandler _touk.FlickIndietro, AddressOf _touk_FlickIndietro
                RemoveHandler _touk.TapDoppio, AddressOf _touk_TapDoppio
                RemoveHandler _touk.EscapeGesto, AddressOf _touk_EscapeGesto
                RemoveHandler _touk.RichiestaLetturaCompleta, AddressOf _touk_RichiestaLetturaCompleta
                RemoveHandler _touk.StatoCambiato, AddressOf _touk_StatoCambiato
                _touk = Nothing
            End If
            If _dettaturaCampi IsNot Nothing Then
                RemoveHandler _dettaturaCampi.TrascrizioneCompletata, AddressOf OnTrascrizioneCompletataCampi
                RemoveHandler _dettaturaCampi.TrascrizioneFallita, AddressOf OnTrascrizioneFallitaCampi
                _dettaturaCampi.Dispose()
                _dettaturaCampi = Nothing
            End If
            If _dettaturaCampiWord IsNot Nothing Then
                RemoveHandler _dettaturaCampiWord.TrascrizioneCompletata, AddressOf OnTrascrizioneCompletataCampoWord
                RemoveHandler _dettaturaCampiWord.TrascrizioneFallita, AddressOf OnTrascrizioneFallitaCampoWord
                _dettaturaCampiWord.Dispose()
                _dettaturaCampiWord = Nothing
            End If
            If _tastoDettatura IsNot Nothing Then
                RemoveHandler _tastoDettatura.Interrotto, AddressOf OnTastoDettaturaInterrotto
                If Not _tastoDettatura.IsDisposed Then _tastoDettatura.Dispose()
                _tastoDettatura = Nothing
            End If
            If WGContenitore.Istanza IsNot Nothing Then WGContenitore.Istanza.RimuoviControllo(Me)
            Me.Dispose(True)
        Catch ex As Exception
            _logger.ScriviLog("CampiFormInterno.ChiudiSilenziosamente: " & ex.Message)
        End Try
    End Sub

    Private Function OttieniIndiceCampoDaControllo(controllo As WGCampiMovimento) As Integer
        Try
            For i As Integer = 0 To _listaCampi.Count - 1
                If _listaCampi(i).Controllo Is controllo Then
                    Return i
                End If
            Next
            Return -1
        Catch ex As Exception
            _logger.ScriviLog("CampiFormInterno.OttieniIndiceCampoDaControllo: " & ex.Message)
            Return -1
        End Try
    End Function

    Private Function EseguiValidazioneCampo(casellaCorrente As WGCampiMovimento) As Boolean
        Try
            If casellaCorrente Is Nothing OrElse Not _mappaIndiciCampi.ContainsKey(casellaCorrente) Then
                Return False
            End If

            Dim dati As MetadatiCampo = _mappaIndiciCampi(casellaCorrente)
            Dim testoPulito As String = casellaCorrente.Text.Trim()

            If String.IsNullOrEmpty(testoPulito) Then
                If dati.EMultiplo Then
                    Dim elementiGruppo = _listaCampi.Where(Function(x) x.IdBaseMultiplo = dati.IdBaseMultiplo).ToList()
                    If elementiGruppo.IndexOf(dati) > 0 Then
                        Return True
                    End If
                End If

                If dati.Obbligatorio Then
                    MostraMessaggioErrore("Non è stato inserito alcun valore: riempi il campo.")
                    Return False
                Else
                    Return True
                End If
            End If

            Select Case dati.Tipo
                Case TipoCampo.CartaDiCredito
                    If Not Regex.IsMatch(testoPulito, "^\d{16}$") Then
                        MostraMessaggioErrore("Il numero carta deve essere composto esattamente da 16 cifre.")
                        Return False
                    End If

                Case TipoCampo.CVC
                    If Not Regex.IsMatch(testoPulito, "^\d{3}$") Then
                        MostraMessaggioErrore("Il CVC deve essere composto esattamente da 3 cifre.")
                        Return False
                    End If

                Case TipoCampo.DataScadenza
                    If Not Regex.IsMatch(testoPulito, "^(0[1-9]|1[0-2])\/\d{2}$") Then
                        MostraMessaggioErrore("La data di scadenza deve essere nel formato MM/YY (es. 01/27).")
                        Return False
                    End If

                Case TipoCampo.Data
                    Dim parserData As New WGUtilsDataOra()
                    Dim dataRisultato As DateTime? = Nothing
                    If Not parserData.AnalizzaData(casellaCorrente.Text, dataRisultato) Then
                        MostraMessaggioErrore("La data inserita non è valida.")
                        Return False
                    End If

                Case TipoCampo.Orario
                    Dim parserOrario As New WGUtilsDataOra()
                    Dim oraRisultato As DateTime? = Nothing
                    If Not parserOrario.AnalizzaOrario(casellaCorrente.Text, oraRisultato) Then
                        MostraMessaggioErrore("L'orario inserito non è valida.")
                        Return False
                    End If

                Case TipoCampo.Host
                    Dim tipoHost As UriHostNameType = Uri.CheckHostName(testoPulito)
                    If tipoHost = UriHostNameType.Unknown Then
                        MostraMessaggioErrore("L'indirizzo inserito non è un host o un indirizzo IP valido.")
                        Return False
                    End If

                Case TipoCampo.Numerico
                    If Not IsNumeric(testoPulito) Then
                        MostraMessaggioErrore("Il campo deve essere numerico.")
                        Return False
                    End If
            End Select

            Return True
        Catch ex As Exception
            _logger.ScriviLog("CampiFormInterno.EseguiValidazioneCampo: " & ex.Message)
            Return False
        End Try
    End Function

    Private _campoDaRipristinareAllaRicomparsa As Control = Nothing

    Protected Overrides Sub OnVisibleChanged(e As EventArgs)
        Try
            MyBase.OnVisibleChanged(e)
            If Me.IsDisposed OrElse Me.Disposing Then Return

            If Not Me.Visible Then
                ' Un controllo esterno (menu, lista, campo di dialogo) sta per essere mostrato
                ' sopra questa schermata: memorizziamo qual e' il campo con il focus adesso, cosi'
                ' quando questa schermata torna visibile possiamo restituirglielo esattamente.
                If Me.ActiveControl IsNot Nothing Then
                    _campoDaRipristinareAllaRicomparsa = Me.ActiveControl
                End If
            Else
                If _campoDaRipristinareAllaRicomparsa IsNot Nothing Then
                    Dim campo As Control = _campoDaRipristinareAllaRicomparsa
                    _campoDaRipristinareAllaRicomparsa = Nothing
                    If Not campo.IsDisposed AndAlso campo.IsHandleCreated Then
                        ForzaRipristinoFocusFinestra()
                        RipristinaFocusSuCampo(campo)
                    End If
                End If
            End If
        Catch ex As Exception
            _logger.ScriviLog("CampiFormInterno.OnVisibleChanged: " & ex.Message)
        End Try
    End Sub

    Private Sub MostraMessaggioErrore(messaggio As String)
        If Me.InvokeRequired Then
            Me.Invoke(New Action(Of String)(AddressOf MostraMessaggioErrore), messaggio)
            Return
        End If
        Try
            _messaggioErroreAttivo = True
            ' A livello di Control (anziche' solo WGCampiMovimento) per non perdere il
            ' riferimento quando il campo che aveva il focus prima di aprire il messaggio era un
            ' campo Word (WGWord): un TryCast(..., WGCampiMovimento) su un WGWord torna sempre
            ' Nothing, perche' WGWord non eredita da WGCampiMovimento, e il campo restava senza
            ' focus reale (e quindi sordo alla tastiera) una volta richiuso il messaggio.
            ' Stesso fix gia' applicato in chiudiOpzioni.
            Dim campoDaRipristinare As Control = Me.ActiveControl
            Dim boxMessaggi As New WGMessaggi()
            Try
                boxMessaggi.Messaggio(messaggio)
                boxMessaggi.ShowDialog()
            Finally
                _messaggioErroreAttivo = False
                RipristinaFocusDopoMessaggio(campoDaRipristinare)
            End Try
        Catch ex As Exception
            _logger.ScriviLog("CampiFormInterno.MostraMessaggioErrore: " & ex.Message)
        End Try
    End Sub

    Private Sub RipristinaFocusDopoMessaggio(campoDaRipristinare As Control)
        Try
            Dim attesaMassima As DateTime = DateTime.UtcNow.AddSeconds(2)
            While Not Me.IsDisposed AndAlso Me.IsHandleCreated AndAlso Not Me.Visible AndAlso DateTime.UtcNow < attesaMassima
                Application.DoEvents()
                Thread.Sleep(15)
            End While

            If Me.IsDisposed OrElse Not Me.IsHandleCreated OrElse Not Me.Visible Then
                Return
            End If

            ' Vedi ForzaRipristinoFocusFinestra: senza questo passaggio la finestra puo' restare
            ' visivamente in primo piano ma non avere davvero il focus di Windows, e i tasti
            ' digitati nel campo non arriverebbero da nessuna parte pur sembrando tutto normale
            ' (bordo blu, cursore lampeggiante).
            ForzaRipristinoFocusFinestra()

            If campoDaRipristinare IsNot Nothing AndAlso Not campoDaRipristinare.IsDisposed AndAlso campoDaRipristinare.IsHandleCreated Then
                RipristinaFocusSuCampo(campoDaRipristinare)
            ElseIf Me.ActiveControl IsNot Nothing AndAlso Not Me.ActiveControl.IsDisposed AndAlso Me.ActiveControl.IsHandleCreated Then
                RipristinaFocusSuCampo(Me.ActiveControl)
            End If
        Catch ex As Exception
            _logger.ScriviLog("RipristinaFocusDopoMessaggio: " & ex.Message)
        End Try
    End Sub

    ''' <summary>
    ''' Riporta in primo piano, a livello di sistema operativo (non solo a livello WinForms), la
    ''' finestra che ospita questo pannello campi.
    '''
    ''' QUESTO E' IL CUORE DEL BUG "il campo non ritorna in focus e la tastiera non funziona"
    ''' segnalato dopo la chiusura di un avviso: i tasti digitati non passano dai normali eventi
    ''' KeyDown di WinForms, ma da un hook globale di tastiera (WGTastiera), che consegna ogni
    ''' tasto a un campo solo se PuoGestireTasti() (in WGCampiMovimento) conferma che la finestra
    ''' ha davvero il focus di Windows - non basta che WinForms creda di averlo. Un box di avviso
    ''' (WGMessaggi) o una domanda (WGDomanda) sono finestre separate: quando si chiudono, a
    ''' volte Windows non restituisce subito il focus reale a questa finestra pur rendendola di
    ''' nuovo visibile, e allora PuoGestireTasti continua a rifiutarsi di consegnare i tasti anche
    ''' se il campo appare visivamente selezionato (bordo blu, cursore lampeggiante: quelli sono
    ''' solo lo stato WinForms, aggiornato correttamente da ActiveControl/Focus() qui sotto, ma
    ''' non bastano da soli). Da qui la necessita' di forzare esplicitamente il ritorno in primo
    ''' piano. SetForegroundWindow da solo fallisce se questo thread non e' agganciato a quello
    ''' della finestra che ha il fuoco: si aggancia, si riprova, poi si chiede il focus sul campo.
    ''' </summary>
    Private Sub ForzaRipristinoFocusFinestra()
        Try
            Dim finestra As Form = Me.FindForm()
            If finestra Is Nothing OrElse finestra.IsDisposed OrElse Not finestra.IsHandleCreated Then Return
            If GetForegroundWindow() = finestra.Handle Then Return ' gia' in primo piano: nulla da fare

            Try
                finestra.Activate()
                BringWindowToTop(finestra.Handle)
                SetForegroundWindow(finestra.Handle)
            Catch exForeground As Exception
                _logger.ScriviLog("ForzaRipristinoFocusFinestra (SetForegroundWindow): " & exForeground.Message)
            End Try

            If GetForegroundWindow() <> finestra.Handle Then
                Dim hwndCorrente As IntPtr = GetForegroundWindow()
                If hwndCorrente <> IntPtr.Zero AndAlso hwndCorrente <> finestra.Handle Then
                    Dim pidCorrente As UInteger = 0
                    Dim threadCorrente As UInteger = GetWindowThreadProcessId(hwndCorrente, pidCorrente)
                    Dim threadMio As UInteger = GetCurrentThreadId()
                    If threadCorrente <> 0 AndAlso threadCorrente <> threadMio Then
                        Dim aggancioRiuscito As Boolean = False
                        Try
                            aggancioRiuscito = AttachThreadInput(threadMio, threadCorrente, True)
                            BringWindowToTop(finestra.Handle)
                            SetForegroundWindow(finestra.Handle)
                        Catch exAggancio As Exception
                            _logger.ScriviLog("ForzaRipristinoFocusFinestra (AttachThreadInput): " & exAggancio.Message)
                        Finally
                            If aggancioRiuscito Then
                                Try
                                    AttachThreadInput(threadMio, threadCorrente, False)
                                Catch exSgancio As Exception
                                    _logger.ScriviLog("ForzaRipristinoFocusFinestra (sgancio): " & exSgancio.Message)
                                End Try
                            End If
                        End Try
                    End If
                End If
            End If

            ' Lascia che Windows elabori i messaggi WM_ACTIVATE/WM_SETFOCUS prima di procedere:
            ' se si richiedesse subito dopo il focus sul campo specifico, rischierebbe di arrivare
            ' troppo presto e non avere effetto (la stessa causa del bug che si sta risolvendo).
            Dim tentativi As Integer = 0
            While GetForegroundWindow() <> finestra.Handle AndAlso tentativi < 10
                Application.DoEvents()
                Thread.Sleep(10)
                tentativi += 1
            End While
        Catch ex As Exception
            _logger.ScriviLog("ForzaRipristinoFocusFinestra: " & ex.Message)
        End Try
    End Sub

    ''' <summary>
    ''' Assegna il focus al campo indicato sia a livello Win32 (SetFocus diretto sull'handle, che
    ''' e' in ultima analisi quanto verifica PuoGestireTasti/ContainsFocus tramite Control.Focused)
    ''' sia a livello WinForms (ActiveControl + Focus(), che fa scattare OnGotFocus e quindi
    ''' ricrea correttamente il caret del campo, vedi WGCampiMovimento.OnGotFocus e
    ''' WGWord.OnGotFocus): i due livelli vanno tenuti allineati, altrimenti si ripresenta lo
    ''' stesso sintomo (campo apparentemente selezionato ma sordo alla tastiera).
    '''
    ''' Tipizzato Control (anziche' WGCampiMovimento) cosi' da funzionare allo stesso modo anche
    ''' per il campo Word (WGWord), che non e' un WGCampiMovimento ma espone comunque Focus() e
    ''' Handle in quanto Control: senza questa generalizzazione, dopo la chiusura di un popup un
    ''' campo Word restava visibile ma incapace di ricevere input da tastiera (vedi
    ''' WGWordGestoreMovimento._tastiera_KeyDown, che consegna i tasti solo se _word.ContainsFocus
    ''' e' vero a livello di sistema operativo).
    ''' </summary>
    Private Sub RipristinaFocusSuCampo(campo As Control)
        Try
            Me.ActiveControl = campo
        Catch exAttivo As Exception
            _logger.ScriviLog("RipristinaFocusSuCampo (ActiveControl): " & exAttivo.Message)
        End Try
        Try
            campo.Focus()
        Catch exFocus As Exception
            _logger.ScriviLog("RipristinaFocusSuCampo (Focus): " & exFocus.Message)
        End Try
        Try
            If campo.IsHandleCreated Then SetFocus(campo.Handle)
        Catch exSetFocus As Exception
            _logger.ScriviLog("RipristinaFocusSuCampo (SetFocus Win32): " & exSetFocus.Message)
        End Try
    End Sub

    Private Sub GestoreEventoUscitaCasella(sender As Object, e As EventArgs)
        Try
            Dim casellaCorrente As WGCampiMovimento = TryCast(sender, WGCampiMovimento)
            If casellaCorrente Is Nothing OrElse Not _mappaIndiciCampi.ContainsKey(casellaCorrente) Then Return

            Dim dati As MetadatiCampo = _mappaIndiciCampi(casellaCorrente)
            ' La scrittura a mano di una data vale finché si resta nel campo.
            If dati.Tipo = TipoCampo.Data AndAlso _elencoAperto Is Nothing Then ImpostaDataManuale(dati, False)

            If dati.Tipo = TipoCampo.Data Then
                Dim parser As New WGUtilsDataOra()
                Dim risultato As DateTime? = Nothing
                If parser.AnalizzaData(casellaCorrente.Text, risultato) Then
                    casellaCorrente.Text = risultato.Value.ToShortDateString()
                End If
            ElseIf dati.Tipo = TipoCampo.Orario Then
                Dim parser As New WGUtilsDataOra()
                Dim risultato As DateTime? = Nothing
                If parser.AnalizzaOrario(casellaCorrente.Text, risultato) Then
                    casellaCorrente.Text = risultato.Value.ToString("HH:mm")
                End If
            ElseIf dati.Tipo = TipoCampo.DataScadenza Then
                NormalizzaDataScadenza(casellaCorrente)
            End If
        Catch ex As Exception
            _logger.ScriviLog("CampiFormInterno.GestoreEventoUscitaCasella: " & ex.Message)
        End Try
    End Sub

    Private Sub NormalizzaDataScadenza(casella As WGCampiMovimento)
        Try
            Dim testo As String = casella.Text.ToLower().Trim()
            Dim dataParsata As DateTime
            Dim risolto As Boolean = False

            If testo.Contains("anno prossimo") Then
                dataParsata = DateTime.Now.AddYears(1)
                risolto = True
            ElseIf testo.Contains("mese prossimo") Then
                dataParsata = DateTime.Now.AddMonths(1)
                risolto = True
            ElseIf testo.Contains("tra due anni") Then
                dataParsata = DateTime.Now.AddYears(2)
                risolto = True
            ElseIf DateTime.TryParse(casella.Text, New CultureInfo("it-IT"), DateTimeStyles.None, dataParsata) Then
                risolto = True
            End If

            If risolto Then
                casella.Text = dataParsata.ToString("MM/yy")
            End If
        Catch ex As Exception
            _logger.ScriviLog("CampiFormInterno.NormalizzaDataScadenza: " & ex.Message)
        End Try
    End Sub

    Private Sub GestoreCambiamentoTestoCarta(sender As Object, e As EventArgs)
        Try
            Dim casellaCorrente As WGCampiMovimento = TryCast(sender, WGCampiMovimento)
            If casellaCorrente IsNot Nothing AndAlso casellaCorrente.Focused AndAlso casellaCorrente.Text.Length = 16 Then
                If EseguiValidazioneCampo(casellaCorrente) Then
                    NavigaAlProssimoCampo(casellaCorrente)
                End If
            End If
        Catch ex As Exception
            _logger.ScriviLog("CampiFormInterno.GestoreCambiamentoTestoCarta: " & ex.Message)
        End Try
    End Sub

    Private Sub GestoreCambiamentoTestoMultiplo(sender As Object, e As EventArgs)
        Try
            If _sospendiAutoAggiuntaMultiplo Then Return
            If _listaCampi.Count >= 250000 Then Return

            Dim casellaCorrente As WGCampiMovimento = TryCast(sender, WGCampiMovimento)
            If casellaCorrente IsNot Nothing AndAlso _mappaIndiciCampi.ContainsKey(casellaCorrente) Then
                Dim dati As MetadatiCampo = _mappaIndiciCampi(casellaCorrente)

                If dati.EMultiplo Then
                    Dim elementiGruppo = _listaCampi.Where(Function(x) x.IdBaseMultiplo = dati.IdBaseMultiplo).ToList()
                    Dim eUltimo As Boolean = elementiGruppo.Last().Controllo Is casellaCorrente

                    If eUltimo AndAlso casellaCorrente.Text.Length > 0 Then
                        If dati.LimiteMultiplo = 0 OrElse elementiGruppo.Count < dati.LimiteMultiplo Then
                            Dim indiceCorrente As Integer = _listaCampi.IndexOf(dati)
                            Dim nuovoIndice As Integer = indiceCorrente + 1
                            Dim nuovoNumero As Integer = elementiGruppo.Count + 1

                            Dim testoTitolo As String = dati.TitoloBase & " " & OttieniNumeroOrdinale(nuovoNumero)
                            If dati.Obbligatorio Then testoTitolo &= " *"

                            Dim nuoviDati As New MetadatiCampo()
                            nuoviDati.TitoloBase = dati.TitoloBase
                            nuoviDati.Controllo = CreaCasellaTesto(False, dati.Tipo = TipoCampo.Password)
                            nuoviDati.Identificativo = dati.Identificativo
                            nuoviDati.Obbligatorio = dati.Obbligatorio
                            nuoviDati.Tipo = dati.Tipo
                            nuoviDati.EMultiplo = True
                            nuoviDati.IdBaseMultiplo = dati.IdBaseMultiplo
                            nuoviDati.LimiteMultiplo = dati.LimiteMultiplo

                            ConfiguraCampoPerTipo(nuoviDati, dati.Tipo, dati.LimiteMultiplo)
                            AddHandler nuoviDati.Controllo.Enter, AddressOf GestoreIngressoCasella

                            _listaCampi.Insert(nuovoIndice, nuoviDati)
                            _mappaIndiciCampi.Add(nuoviDati.Controllo, nuoviDati)
                            Me.Controls.Add(nuoviDati.Controllo)
                            ' Anche la nuova voce del gruppo passa al campo vicino con la freccia su e giù.
                            For Each datiNav As MetadatiCampo In _listaCampi
                                If datiNav.Controllo IsNot Nothing Then datiNav.Controllo.NavigazioneDirettaCampi = (_listaCampi.Count > 1)
                            Next

                            AggiornaVisibilitaCampi(casellaCorrente)
                        End If
                    End If
                End If
            End If
        Catch ex As Exception
            _logger.ScriviLog("CampiFormInterno.GestoreCambiamentoTestoMultiplo: " & ex.Message)
        End Try
    End Sub

    Private Function OttieniNumeroOrdinale(numero As Integer) As String
        Try
            If numero <= 10 Then
                Select Case numero
                    Case 1 : Return "Primo"
                    Case 2 : Return "Secondo"
                    Case 3 : Return "Terzo"
                    Case 4 : Return "Quarto"
                    Case 5 : Return "Quinto"
                    Case 6 : Return "Sesto"
                    Case 7 : Return "Settimo"
                    Case 8 : Return "Ottavo"
                    Case 9 : Return "Nono"
                    Case 10 : Return "Decimo"
                End Select
            End If

            Dim cardinale As String = ConvertiNumeroInParole(numero)

            If cardinale.EndsWith("tre") Then
                If cardinale.EndsWith("tré") Then
                    cardinale = cardinale.Substring(0, cardinale.Length - 1) & "tre"
                End If
                Return cardinale & "esimo"
            ElseIf cardinale.EndsWith("mila") Then
                Return cardinale.Substring(0, cardinale.Length - 1) & "lesimo"
            ElseIf cardinale.EndsWith("mille") Then
                Return cardinale.Substring(0, cardinale.Length - 1) & "lesimo"
            Else
                Dim ultimaLettera As Char = cardinale.Last()
                If "aeiou".Contains(ultimaLettera) Then
                    cardinale = cardinale.Substring(0, cardinale.Length - 1)
                End If
                Return cardinale & "esimo"
            End If
        Catch ex As Exception
            _logger.ScriviLog("CampiFormInterno.OttieniNumeroOrdinale: " & ex.Message)
            Return numero.ToString()
        End Try
    End Function

    Private Function ConvertiNumeroInParole(n As Integer) As String
        Try
            If n = 0 Then Return "Zero"
            Dim parole As String = ""

            If (n \ 1000000) > 0 Then
                parole &= If((n \ 1000000) = 1, "Unmilione", ConvertiNumeroInParole(n \ 1000000) & "milioni")
                n = n Mod 1000000
            End If

            If (n \ 1000) > 0 Then
                parole &= If((n \ 1000) = 1, "Mille", ConvertiNumeroInParole(n \ 1000) & "mila")
                n = n Mod 1000
            End If

            If (n \ 100) > 0 Then
                parole &= If((n \ 100) = 1, "Cento", ConvertiNumeroInParole(n \ 100) & "cento")
                n = n Mod 100
            End If

            If n > 0 Then
                If n < 20 Then
                    Dim unita() As String = {"", "Uno", "Due", "Tre", "Quattro", "Cinque", "Sei", "Sette", "Otto", "Nove", "Dieci", "Undici", "Dodici", "Tredici", "Quattordici", "Quindici", "Sedici", "Diciassette", "Diciotto", "Diciannove"}
                    parole &= unita(n)
                Else
                    Dim decine() As String = {"", "", "Venti", "Trenta", "Quaranta", "Cinquanta", "Sessanta", "Settanta", "Ottanta", "Novanta"}
                    Dim decina As Integer = n \ 10
                    Dim unitaNum As Integer = n Mod 10
                    Dim testoDecina As String = decine(decina)
                    Dim testoUnita As String = ""

                    If unitaNum > 0 Then
                        Dim unitaParole() As String = {"", "Uno", "Due", "Tre", "Quattro", "Cinque", "Sei", "Sette", "Otto", "Nove"}
                        testoUnita = unitaParole(unitaNum)
                    End If

                    If unitaNum = 1 OrElse unitaNum = 8 Then
                        testoDecina = testoDecina.Substring(0, testoDecina.Length - 1)
                    End If

                    parole &= testoDecina & testoUnita
                End If
            End If

            Return parole
        Catch ex As Exception
            _logger.ScriviLog("CampiFormInterno.ConvertiNumeroInParole: " & ex.Message)
            Return n.ToString()
        End Try
    End Function

    Private Sub AggiornaVisibilitaCampi(casellaCorrente As WGCampiMovimento)
        Try
            If _listaCampi.Count = 0 Then Return

            Dim indiceCorrente As Integer = OttieniIndiceCampoDaControllo(casellaCorrente)

            If indiceCorrente >= 0 AndAlso indiceCorrente < _listaCampi.Count AndAlso _listaCampi(indiceCorrente).EWord Then
                DisponiSoloCampoWord(indiceCorrente)
                Return
            End If

            Dim indiciVisibili As New List(Of Integer)()

            If indiceCorrente = 0 Then
                indiciVisibili.Add(0)
                If _listaCampi.Count > 1 Then indiciVisibili.Add(1)
                If _listaCampi.Count > 2 Then indiciVisibili.Add(2)
            ElseIf indiceCorrente = _listaCampi.Count - 1 Then
                If _listaCampi.Count > 2 Then indiciVisibili.Add(indiceCorrente - 2)
                If _listaCampi.Count > 1 Then indiciVisibili.Add(indiceCorrente - 1)
                indiciVisibili.Add(indiceCorrente)
            Else
                indiciVisibili.Add(indiceCorrente - 1)
                indiciVisibili.Add(indiceCorrente)
                indiciVisibili.Add(indiceCorrente + 1)
            End If

            Me.SuspendLayout()

            SalvaTuttiIValori()
            NascondiTuttiICampiWord()

            Dim areaCampi As Rectangle = AreaCampoGdiTouk()
            For i As Integer = 0 To _listaCampi.Count - 1
                Dim visibile As Boolean = indiciVisibili.Contains(i)
                _listaCampi(i).Controllo.Visible = visibile
                If visibile Then
                    Dim offset As Integer = indiciVisibili.IndexOf(i)
                    _listaCampi(i).Controllo.Top = areaCampi.Top + SPAZIO_SUPERIORE + (offset * DISTANZA_VERTICALE) + TITOLO_ALTEZZA
                    _listaCampi(i).Controllo.Left = SPAZIO_SINISTRA
                End If
            Next

            Me.ResumeLayout()
            Me.Invalidate()
        Catch ex As ObjectDisposedException
            _logger.ScriviLog("CampiFormInterno.AggiornaVisibilitaCampi (ObjectDisposed): " & ex.Message)
        Catch ex As Exception
            _logger.ScriviLog("CampiFormInterno.AggiornaVisibilitaCampi: " & ex.Message)
        End Try
    End Sub

    Private Sub _touk_StatoCambiato() Handles _touk.StatoCambiato
        Try
            If Me.IsDisposed OrElse Me.Disposing Then Return
            If Me.InvokeRequired Then
                Me.BeginInvoke(New MethodInvoker(AddressOf _touk_StatoCambiato))
                Return
            End If
            AggiornaTastieraTouk()
        Catch ex As Exception
            _logger.ScriviLog("CampiFormInterno._touk_StatoCambiato: " & ex.Message)
        End Try
    End Sub

    Private Function TastieraToukVisibile() As Boolean
        Try
            Return _tastieraTouch IsNot Nothing AndAlso Not _tastieraTouch.IsDisposed AndAlso _tastieraTouch.Visible AndAlso Me.Controls.Contains(_tastieraTouch)
        Catch
            Return False
        End Try
    End Function

    Private Function QuotaCampiEffettiva() As Double
        Try
            If _touk Is Nothing Then Return QuotaCampiTouk
            Return 1.0 - _touk.QuotaAltezzaTastiera(Me.ClientSize.Width, Me.ClientSize.Height, 1.0 - QuotaCampiTouk)
        Catch ex As Exception
            _logger.ScriviLog("CampiFormInterno.QuotaCampiEffettiva: " & ex.Message)
            Return QuotaCampiTouk
        End Try
    End Function

    Private Function AltezzaCampiTouk() As Integer
        Try
            Dim clientH As Integer = Me.ClientSize.Height
            If Not TastieraToukVisibile() Then Return Math.Max(1, clientH)
            Dim campiH As Integer = CInt(clientH * QuotaCampiEffettiva())
            If campiH < 1 Then campiH = 1
            If campiH >= clientH Then campiH = Math.Max(1, clientH - 1)
            Return campiH
        Catch ex As Exception
            _logger.ScriviLog("CampiFormInterno.AltezzaCampiTouk: " & ex.Message)
            Return Math.Max(1, Me.ClientSize.Height)
        End Try
    End Function

    Private Function AreaCampoGdiTouk() As Rectangle
        Try
            Return New Rectangle(0, 0, Me.ClientSize.Width, AltezzaCampiTouk())
        Catch
            Return New Rectangle(0, 0, Me.ClientSize.Width, Me.ClientSize.Height)
        End Try
    End Function

    Friend Sub AggiornaTastieraTouk()
        Try
            If Me.IsDisposed OrElse Me.Disposing Then Return
            Dim toukAttivo As Boolean = (_touk IsNot Nothing AndAlso _touk.TouchAttivo)
            If toukAttivo Then
                MostraTastieraTouk()
            Else
                NascondiTastieraTouk()
            End If
            DisponiTastieraToukECampi()
        Catch ex As Exception
            _logger.ScriviLog("CampiFormInterno.AggiornaTastieraTouk: " & ex.Message)
        End Try
    End Sub

    Private Sub MostraTastieraTouk()
        Try
            If _tastieraTouch Is Nothing OrElse _tastieraTouch.IsDisposed Then
                _tastieraTouch = New WGTastieraTouch()
                _tastieraTouch.LayoutGestitoDalContenitore = True
                AddHandler _tastieraTouch.CarattereInserito, AddressOf OnCarattereInseritoDaTastieraCampi
                AddHandler _tastieraTouch.EliminaRichiesto, AddressOf OnEliminaRichiestoDaTastieraCampi
                AddHandler _tastieraTouch.InvioRichiesto, AddressOf OnInvioRichiestoDaTastieraCampi
                AddHandler _tastieraTouch.ChiudiRichiesto, AddressOf OnChiudiRichiestoDaTastieraCampi
                AddHandler _tastieraTouch.TestoDettatoInserito, AddressOf OnTestoDettatoDaTastieraCampi
            End If

            _tastieraTouch.LayoutGestitoDalContenitore = True
            _tastieraTouch.Dock = DockStyle.None
            If Not Me.Controls.Contains(_tastieraTouch) Then
                Me.Controls.Add(_tastieraTouch)
            End If
            _tastieraTouch.Mostra()
            _tastieraTouch.Visible = True
            _tastieraTouch.BringToFront()
        Catch ex As Exception
            _logger.ScriviLog("CampiFormInterno.MostraTastieraTouk: " & ex.Message)
        End Try
    End Sub

    Private Sub NascondiTastieraTouk()
        Try
            If _tastieraTouch Is Nothing OrElse _tastieraTouch.IsDisposed Then Return
            _tastieraTouch.Nascondi()
            _tastieraTouch.Visible = False
            If Me.Controls.Contains(_tastieraTouch) Then
                Me.Controls.Remove(_tastieraTouch)
            End If
        Catch ex As Exception
            _logger.ScriviLog("CampiFormInterno.NascondiTastieraTouk: " & ex.Message)
        End Try
    End Sub

    Private Sub DisponiTastieraToukECampi()
        Try
            If Me.IsDisposed OrElse Me.Disposing Then Return
            Dim clientW As Integer = Me.ClientSize.Width
            Dim clientH As Integer = Me.ClientSize.Height
            If clientW <= 0 OrElse clientH <= 0 Then Return

            If TastieraToukVisibile() Then
                Dim campiH As Integer = AltezzaCampiTouk()
                _tastieraTouch.SetBounds(0, campiH, clientW, clientH - campiH)
                _tastieraTouch.BringToFront()
            End If

            Dim casellaCorrente As WGCampiMovimento = TryCast(Me.ActiveControl, WGCampiMovimento)
            If casellaCorrente IsNot Nothing Then
                AggiornaVisibilitaCampi(casellaCorrente)
            Else
                Dim datiWord As MetadatiCampo = CampoWordAttivo()
                If datiWord IsNot Nothing AndAlso datiWord.ControlloWord IsNot Nothing Then
                    PosizionaCampoWord(datiWord.ControlloWord)
                End If
            End If
            Me.Invalidate(AreaCampoGdiTouk())
        Catch ex As Exception
            _logger.ScriviLog("CampiFormInterno.DisponiTastieraToukECampi: " & ex.Message)
        End Try
    End Sub

    Private Sub OnCarattereInseritoDaTastieraCampi(carattere As String)
        Try
            If Me.InvokeRequired Then
                Me.BeginInvoke(New Action(Of String)(AddressOf OnCarattereInseritoDaTastieraCampi), carattere)
                Return
            End If
            Dim datiWord As MetadatiCampo = CampoWordAttivo()
            If datiWord IsNot Nothing Then
                OnCarattereInseritoDaTastieraWord(carattere)
                Return
            End If
            Dim campo As WGCampiMovimento = TryCast(Me.ActiveControl, WGCampiMovimento)
            If campo IsNot Nothing Then campo.InserisciCarattereDaTastieraTouch(carattere)
        Catch ex As Exception
            _logger.ScriviLog("CampiFormInterno.OnCarattereInseritoDaTastieraCampi: " & ex.Message)
        End Try
    End Sub

    Private Sub OnEliminaRichiestoDaTastieraCampi()
        Try
            If Me.InvokeRequired Then
                Me.BeginInvoke(New MethodInvoker(AddressOf OnEliminaRichiestoDaTastieraCampi))
                Return
            End If
            Dim datiWord As MetadatiCampo = CampoWordAttivo()
            If datiWord IsNot Nothing Then
                OnEliminaRichiestoDaTastieraWord()
                Return
            End If
            Dim campo As WGCampiMovimento = TryCast(Me.ActiveControl, WGCampiMovimento)
            If campo IsNot Nothing Then campo.EliminaDaTastieraTouch()
        Catch ex As Exception
            _logger.ScriviLog("CampiFormInterno.OnEliminaRichiestoDaTastieraCampi: " & ex.Message)
        End Try
    End Sub

    Private Sub OnInvioRichiestoDaTastieraCampi()
        Try
            If Me.InvokeRequired Then
                Me.BeginInvoke(New MethodInvoker(AddressOf OnInvioRichiestoDaTastieraCampi))
                Return
            End If
            Dim datiWord As MetadatiCampo = CampoWordAttivo()
            If datiWord IsNot Nothing Then
                OnInvioRichiestoDaTastieraWord()
                Return
            End If
            Dim campo As WGCampiMovimento = TryCast(Me.ActiveControl, WGCampiMovimento)
            If campo IsNot Nothing Then campo.InvioDaTastieraTouch()
        Catch ex As Exception
            _logger.ScriviLog("CampiFormInterno.OnInvioRichiestoDaTastieraCampi: " & ex.Message)
        End Try
    End Sub

    Private Sub OnChiudiRichiestoDaTastieraCampi()
        Try
            If Me.InvokeRequired Then
                Me.BeginInvoke(New MethodInvoker(AddressOf OnChiudiRichiestoDaTastieraCampi))
                Return
            End If
            NascondiTastieraTouk()
            DisponiTastieraToukECampi()
        Catch ex As Exception
            _logger.ScriviLog("CampiFormInterno.OnChiudiRichiestoDaTastieraCampi: " & ex.Message)
        End Try
    End Sub

    Private Sub OnTestoDettatoDaTastieraCampi(testo As String)
        Try
            If Me.InvokeRequired Then
                Me.BeginInvoke(New Action(Of String)(AddressOf OnTestoDettatoDaTastieraCampi), testo)
                Return
            End If
            Dim datiWord As MetadatiCampo = CampoWordAttivo()
            If datiWord IsNot Nothing Then
                OnTestoDettatoDaTastieraWord(testo)
                Return
            End If
            Dim campo As WGCampiMovimento = TryCast(Me.ActiveControl, WGCampiMovimento)
            If campo IsNot Nothing Then campo.InserisciTestoDettatoDaTastieraTouch(testo)
        Catch ex As Exception
            _logger.ScriviLog("CampiFormInterno.OnTestoDettatoDaTastieraCampi: " & ex.Message)
        End Try
    End Sub

    Protected Overrides Sub OnSizeChanged(e As EventArgs)
        Try
            MyBase.OnSizeChanged(e)
            DisponiTastieraToukECampi()
        Catch ex As Exception
            _logger.ScriviLog("CampiFormInterno.OnSizeChanged: " & ex.Message)
        End Try
    End Sub

    Protected Overrides Sub OnPaint(e As PaintEventArgs)
        MyBase.OnPaint(e)
        Try
            Dim g As Graphics = e.Graphics
            g.SmoothingMode = SmoothingMode.AntiAlias

            Dim areaCampi As Rectangle = AreaCampoGdiTouk()
            If TastieraToukVisibile() Then
                g.SetClip(areaCampi)
            End If

            Dim datiWordAttivo As MetadatiCampo = CampoWordAttivo()
            If datiWordAttivo IsNot Nothing Then
                DisegnaTitoloCampoWord(g, datiWordAttivo)
                Return
            End If

            Dim casellaCorrente As WGCampiMovimento = TryCast(Me.ActiveControl, WGCampiMovimento)
            If casellaCorrente Is Nothing OrElse Not _mappaIndiciCampi.ContainsKey(casellaCorrente) Then Return

            Dim indiceAttivo As Integer = OttieniIndiceCampoDaControllo(casellaCorrente)
            Dim indiciVisibili As New List(Of Integer)()

            If indiceAttivo = 0 Then
                indiciVisibili.Add(0)
                If _listaCampi.Count > 1 Then indiciVisibili.Add(1)
                If _listaCampi.Count > 2 Then indiciVisibili.Add(2)
            ElseIf indiceAttivo = _listaCampi.Count - 1 Then
                If _listaCampi.Count > 2 Then indiciVisibili.Add(indiceAttivo - 2)
                If _listaCampi.Count > 1 Then indiciVisibili.Add(indiceAttivo - 1)
                indiciVisibili.Add(indiceAttivo)
            Else
                indiciVisibili.Add(indiceAttivo - 1)
                indiciVisibili.Add(indiceAttivo)
                indiciVisibili.Add(indiceAttivo + 1)
            End If

            For i As Integer = 0 To indiciVisibili.Count - 1
                Dim indiceCampo As Integer = indiciVisibili(i)
                Dim dati As MetadatiCampo = _listaCampi(indiceCampo)

                Dim rectTitolo As New Rectangle(SPAZIO_SINISTRA, areaCampi.Top + SPAZIO_SUPERIORE + (i * DISTANZA_VERTICALE), 400, TITOLO_ALTEZZA)
                Using lgb As New LinearGradientBrush(rectTitolo, Color.FromArgb(60, 60, 60), Color.Black, LinearGradientMode.Vertical)
                    g.FillRectangle(lgb, rectTitolo)
                End Using

                Dim testo As String = dati.TitoloBase
                If dati.EMultiplo Then
                    Dim elementiGruppo = _listaCampi.Where(Function(x) x.IdBaseMultiplo = dati.IdBaseMultiplo).ToList()
                    Dim posInGruppo As Integer = elementiGruppo.IndexOf(dati) + 1
                    testo &= " " & OttieniNumeroOrdinale(posInGruppo)
                End If
                If dati.Obbligatorio Then testo &= " *"

                Dim evidenziaErroreLettere As Boolean = _messaggioLettereNonConsentite IsNot Nothing AndAlso dati.Controllo Is _campoMessaggioLettereNonConsentite
                If evidenziaErroreLettere Then testo = _messaggioLettereNonConsentite

                Using f As New Font("Arial", DIMENSIONE_FONT_ETICHETTA, FontStyle.Bold)
                    Using sf As New StringFormat()
                        sf.Alignment = StringAlignment.Near
                        sf.LineAlignment = StringAlignment.Center
                        g.DrawString(testo, f, If(evidenziaErroreLettere, Brushes.Red, Brushes.White), rectTitolo, sf)
                    End Using
                End Using
            Next
        Catch ex As Exception
            _logger.ScriviLog("CampiFormInterno.OnPaint: " & ex.Message)
        End Try
    End Sub

    Private Sub NavigaAlCampoPrecedente()
        Try
            Dim indiceCorrente As Integer = IndiceCampoAttivo()
            If indiceCorrente >= 0 AndAlso _listaCampi.Count > 0 Then
                Dim indiceSuccessivo As Integer = If(indiceCorrente - 1 < 0, _listaCampi.Count - 1, indiceCorrente - 1)
                AttivaCampoPerIndice(indiceSuccessivo)
            End If
        Catch ex As ObjectDisposedException
            _logger.ScriviLog("CampiFormInterno.NavigaAlCampoPrecedente (ObjectDisposed): " & ex.Message)
        Catch ex As Exception
            _logger.ScriviLog("CampiFormInterno.NavigaAlCampoPrecedente: " & ex.Message)
        End Try
    End Sub

    Private Sub NavigaAlProssimoCampo(casellaCorrente As WGCampiMovimento)
        Try
            If casellaCorrente IsNot Nothing AndAlso _listaCampi.Count > 0 Then
                Dim indiceCorrente As Integer = OttieniIndiceCampoDaControllo(casellaCorrente)
                Dim indiceSuccessivo As Integer = indiceCorrente + 1
                If indiceSuccessivo < 0 OrElse indiceSuccessivo >= _listaCampi.Count Then indiceSuccessivo = 0
                AttivaCampoPerIndice(indiceSuccessivo)
            End If
        Catch ex As ObjectDisposedException
            _logger.ScriviLog("CampiFormInterno.NavigaAlProssimoCampo (ObjectDisposed): " & ex.Message)
        Catch ex As Exception
            _logger.ScriviLog("CampiFormInterno.NavigaAlProssimoCampo: " & ex.Message)
        End Try
    End Sub

    Private Sub NavigaAlProssimoCampoGenerico()
        Try
            Dim indiceCorrente As Integer = IndiceCampoAttivo()
            If indiceCorrente < 0 OrElse _listaCampi.Count = 0 Then Return
            Dim indiceSuccessivo As Integer = indiceCorrente + 1
            If indiceSuccessivo >= _listaCampi.Count Then indiceSuccessivo = 0
            AttivaCampoPerIndice(indiceSuccessivo)
        Catch ex As ObjectDisposedException
            _logger.ScriviLog("CampiFormInterno.NavigaAlProssimoCampoGenerico (ObjectDisposed): " & ex.Message)
        Catch ex As Exception
            _logger.ScriviLog("CampiFormInterno.NavigaAlProssimoCampoGenerico: " & ex.Message)
        End Try
    End Sub

    Private Sub EseguiInvioFinale()
        Try
            If VerificaTuttiCampiObbligatori() Then
                _chiusoCorrettamente = True
                SalvaTuttiIValori()
                RaiseEvent EventoFine(Me, EventArgs.Empty)
                If WGContenitore.Istanza IsNot Nothing Then WGContenitore.Istanza.RimuoviControllo(Me)
                Me.Dispose()
            Else
                MostraMessaggioErrore("Compila tutti i campi obbligatori prima di proseguire.")
            End If
        Catch ex As Exception
            _logger.ScriviLog("CampiFormInterno.EseguiInvioFinale: " & ex.Message)
        End Try
    End Sub

    Friend Function VerificaTuttiCampiObbligatori() As Boolean
        Try
            For Each dati As MetadatiCampo In _listaCampi
                If dati.EMultiplo Then
                    Dim elementiGruppo = _listaCampi.Where(Function(x) x.IdBaseMultiplo = dati.IdBaseMultiplo).ToList()
                    If elementiGruppo.IndexOf(dati) > 0 Then Continue For
                End If

                If dati.Obbligatorio AndAlso String.IsNullOrEmpty(TestoDiCampo(dati).Trim()) Then
                    Return False
                End If
            Next
            Return True
        Catch ex As Exception
            _logger.ScriviLog("CampiFormInterno.VerificaTuttiCampiObbligatori: " & ex.Message)
            Return False
        End Try
    End Function

    Private Sub AssicuraFocusCampoAttivo()
        Try
            If Me.IsDisposed OrElse Not Me.IsHandleCreated Then Return
            If CampoWordAttivo() IsNot Nothing Then Return
            Dim daAttivare As WGCampiMovimento = TryCast(Me.ActiveControl, WGCampiMovimento)
            If daAttivare Is Nothing AndAlso _listaCampi.Count > 0 Then daAttivare = _listaCampi(0).Controllo
            If daAttivare Is Nothing OrElse daAttivare.IsDisposed Then Return
            If daAttivare.Focused Then Return
            If Not daAttivare.Visible Then AggiornaVisibilitaCampi(daAttivare)
            Me.ActiveControl = daAttivare
            daAttivare.Focus()
        Catch ex As Exception
            _logger.ScriviLog("CampiFormInterno.AssicuraFocusCampoAttivo: " & ex.Message)
        End Try
    End Sub

    Private Sub SelezionaPrimoCampoDisponibile()
        Try
            For Each datiNav As MetadatiCampo In _listaCampi
                If datiNav.Controllo IsNot Nothing Then
                    datiNav.Controllo.NavigazioneDirettaCampi = (_listaCampi.Count > 1)
                    If _overridePunteggiaturaForm.HasValue Then datiNav.Controllo.ImpostaImpostazioniTemporanee(_overrideEchoForm, _overridePunteggiaturaForm.Value)
                End If
            Next

            If _listaCampi.Count > 0 Then
                If _listaCampi(0).EWord Then
                    AttivaCampoPerIndice(0)
                    Return
                End If
                Dim primoCtrl As WGCampiMovimento = _listaCampi(0).Controllo
                Dim avevaFocus As Boolean = primoCtrl.Focused
                AggiornaVisibilitaCampi(primoCtrl)
                Me.ActiveControl = primoCtrl
                primoCtrl.Focus()
                If Not avevaFocus Then LeggiCampoAttivo(primoCtrl)
                ApriElencoDateAlIngresso(primoCtrl)
                If Not primoCtrl.Focused AndAlso Me.IsHandleCreated Then
                    Try
                        Me.BeginInvoke(New MethodInvoker(AddressOf AssicuraFocusCampoAttivo))
                    Catch exRinvio As Exception
                        _logger.ScriviLog("CampiFormInterno.SelezionaPrimoCampoDisponibile (rinvio focus): " & exRinvio.Message)
                    End Try
                End If
            End If
        Catch ex As ObjectDisposedException
            _logger.ScriviLog("CampiFormInterno.SelezionaPrimoCampoDisponibile (ObjectDisposed): " & ex.Message)
        Catch ex As Exception
            _logger.ScriviLog("CampiFormInterno.SelezionaPrimoCampoDisponibile: " & ex.Message)
        End Try
    End Sub

    Private _overrideEchoForm As String = Nothing
    Private _overridePunteggiaturaForm As Nullable(Of Integer) = Nothing

    Public Sub ImpostaImpostazioniTemporaneeForm(echoDigitazione As String, livelloPunteggiaturaScrittura As Integer)
        Try
            _overrideEchoForm = echoDigitazione
            _overridePunteggiaturaForm = livelloPunteggiaturaScrittura
            For Each dati As MetadatiCampo In _listaCampi
                If dati.Controllo IsNot Nothing Then dati.Controllo.ImpostaImpostazioniTemporanee(echoDigitazione, livelloPunteggiaturaScrittura)
            Next
        Catch ex As Exception
            _logger.ScriviLog("CampiFormInterno.ImpostaImpostazioniTemporaneeForm: " & ex.Message)
        End Try
    End Sub

    Public Sub ImpostaMaiuscolaAutomaticaCampo(id As String, attiva As Boolean)
        Try
            For Each dati As MetadatiCampo In _listaCampi
                If dati.Identificativo = id AndAlso dati.Controllo IsNot Nothing Then
                    dati.Controllo.MaiuscolaAutomatica = attiva
                    Return
                End If
            Next
        Catch ex As Exception
            _logger.ScriviLog("CampiFormInterno.ImpostaMaiuscolaAutomaticaCampo: " & ex.Message)
        End Try
    End Sub

    Public Sub SelezionaCampoPerId(id As String)
        If Me.InvokeRequired Then
            Me.Invoke(New MethodInvoker(Sub() Me.SelezionaCampoPerId(id)))
            Return
        End If
        Try
            For Each dati As MetadatiCampo In _listaCampi
                If dati.Identificativo = id AndAlso dati.Controllo IsNot Nothing Then
                    If menuOpzioniAperto Then
                        _campoConFocusMenu = dati.Controllo
                        _controlloConFocusMenu = dati.Controllo
                    End If
                    AggiornaVisibilitaCampi(dati.Controllo)
                    ' Stesso fix di RipristinaFocusDopoMessaggio: chi chiama SelezionaCampo
                    ' dall'esterno (es. MenuPosta, dopo un proprio avviso mostrato SOPRA questo
                    ' form campi) ha bisogno della stessa garanzia, non solo del focus WinForms.
                    ForzaRipristinoFocusFinestra()
                    RipristinaFocusSuCampo(dati.Controllo)
                    ApriElencoDateAlIngresso(dati.Controllo)
                    Return
                End If
            Next
            SelezionaPrimoCampoDisponibile()
        Catch ex As ObjectDisposedException
            _logger.ScriviLog("CampiFormInterno.SelezionaCampoPerId (ObjectDisposed): " & ex.Message)
        Catch ex As Exception
            _logger.ScriviLog("CampiFormInterno.SelezionaCampoPerId: " & ex.Message)
        End Try
    End Sub

    Public Sub AggiungiCampoGenerico(titolo As String, id As String, obbligatorio As Boolean, solaLettura As Boolean, testoPredefinito As String, tipo As TipoCampo, eMultiplo As Boolean, Optional limite As Integer = 0)
        If Me.InvokeRequired Then
            Me.Invoke(New MethodInvoker(Sub() Me.AggiungiCampoGenerico(titolo, id, obbligatorio, solaLettura, testoPredefinito, tipo, eMultiplo, limite)))
            Return
        End If
        Try
            Dim nuoviDati As New MetadatiCampo()
            nuoviDati.TitoloBase = titolo
            nuoviDati.Controllo = CreaCasellaTesto(solaLettura, tipo = TipoCampo.Password)
            nuoviDati.Identificativo = id
            nuoviDati.Obbligatorio = obbligatorio
            nuoviDati.Tipo = tipo
            nuoviDati.EMultiplo = eMultiplo
            nuoviDati.IdBaseMultiplo = id
            nuoviDati.LimiteMultiplo = limite

            If tipo = TipoCampo.Multiriga Then nuoviDati.Controllo.Multiline = True

            ConfiguraCampoPerTipo(nuoviDati, tipo, limite)

            _listaCampi.Add(nuoviDati)
            _mappaIndiciCampi.Add(nuoviDati.Controllo, nuoviDati)
            Me.Controls.Add(nuoviDati.Controllo)
            nuoviDati.Controllo.Text = testoPredefinito
            ' Con più di un campo la freccia su e giù passa al campo vicino: vale anche per i campi aggiunti dopo
            ' l'apertura, come le nuove voci dei gruppi (email, telefoni, indirizzi).
            For Each datiNav As MetadatiCampo In _listaCampi
                If datiNav.Controllo IsNot Nothing Then datiNav.Controllo.NavigazioneDirettaCampi = (_listaCampi.Count > 1)
            Next
        Catch ex As Exception
            _logger.ScriviLog("CampiFormInterno.AggiungiCampoGenerico: " & ex.Message)
        End Try
    End Sub

    Public Sub AggiungiCampoMultiploConValori(titolo As String, id As String, valori As IEnumerable(Of String), obbligatorio As Boolean, tipo As TipoCampo, limite As Integer)
        If Me.InvokeRequired Then
            Me.Invoke(New MethodInvoker(Sub() Me.AggiungiCampoMultiploConValori(titolo, id, valori, obbligatorio, tipo, limite)))
            Return
        End If
        Try
            Dim elenco As New List(Of String)
            If valori IsNot Nothing Then
                For Each valore As String In valori
                    If Not String.IsNullOrWhiteSpace(valore) Then elenco.Add(valore)
                Next
            End If

            If elenco.Count = 0 Then
                AggiungiCampoGenerico(titolo, id, obbligatorio, False, String.Empty, tipo, True, limite)
                Return
            End If

            _sospendiAutoAggiuntaMultiplo = True
            Try
                For Each valore As String In elenco
                    AggiungiCampoGenerico(titolo, id, obbligatorio, False, valore, tipo, True, limite)
                Next
            Finally
                _sospendiAutoAggiuntaMultiplo = False
            End Try

            If limite = 0 OrElse elenco.Count < limite Then
                AggiungiCampoGenerico(titolo, id, obbligatorio, False, String.Empty, tipo, True, limite)
            End If
        Catch ex As Exception
            _logger.ScriviLog("CampiFormInterno.AggiungiCampoMultiploConValori: " & ex.Message)
        End Try
    End Sub

    Private Function CreaCasellaTesto(solaLettura As Boolean, ePassword As Boolean) As WGCampiMovimento
        Try
            Dim casella As New WGCampiMovimento()
            casella.Width = 350
            casella.Font = New Font("Arial", DIMENSIONE_FONT_CASELLA, FontStyle.Bold)
            casella.ReadOnly = solaLettura
            casella.TabStop = False
            casella.AccessibleRole = AccessibleRole.None
            casella.AccessibleName = Nothing
            If ePassword Then
                casella.IsCampoPassword = True
            End If
            AddHandler casella.Enter, AddressOf GestoreIngressoCasella
            AddHandler casella.RichiestaCampoAdiacente, AddressOf CampoRichiedeAdiacente
            AddHandler casella.RichiestaConfermaCampo, AddressOf CampoRichiedeConferma
            Return casella
        Catch ex As Exception
            _logger.ScriviLog("CampiFormInterno.CreaCasellaTesto: " & ex.Message)
            Return New WGCampiMovimento()
        End Try
    End Function

    Private Sub CampoRichiedeConferma(sender As Object)
        Try
            Dim casellaCorrente As WGCampiMovimento = TryCast(sender, WGCampiMovimento)
            If casellaCorrente Is Nothing Then Return
            GestisciTastoInvio(casellaCorrente)
        Catch ex As Exception
            _logger.ScriviLog("CampiFormInterno.CampoRichiedeConferma: " & ex.Message)
        End Try
    End Sub

    Private Sub CampoRichiedeAdiacente(sender As Object, direzione As Integer)
        Try
            Dim casellaCorrente As WGCampiMovimento = TryCast(sender, WGCampiMovimento)
            If casellaCorrente Is Nothing Then Return
            If direzione > 0 Then
                GestisciFrecciaGiu(casellaCorrente)
            Else
                GestisciFrecciaSu(casellaCorrente)
            End If
        Catch ex As Exception
            _logger.ScriviLog("CampiFormInterno.CampoRichiedeAdiacente: " & ex.Message)
        End Try
    End Sub

    Private Sub ConfiguraCampoPerTipo(dati As MetadatiCampo, tipo As TipoCampo, limite As Integer)
        Try
            If dati.EMultiplo Then
                dati.Controllo.Tag = "Multiplo"
                AddHandler dati.Controllo.TextChanged, AddressOf GestoreCambiamentoTestoMultiplo
            End If

            Select Case tipo
                Case TipoCampo.Numerico, TipoCampo.CVC, TipoCampo.CartaDiCredito
                    AddHandler dati.Controllo.KeyPress, AddressOf GestorePressioneTastiNumerici
                    If tipo = TipoCampo.CartaDiCredito Then
                        dati.Controllo.MaxLength = 16
                        If Not dati.EMultiplo Then
                            dati.Controllo.Tag = "CartaDiCredito"
                            AddHandler dati.Controllo.TextChanged, AddressOf GestoreCambiamentoTestoCarta
                        End If
                    ElseIf tipo = TipoCampo.CVC Then
                        dati.Controllo.MaxLength = 3
                        If Not dati.EMultiplo Then dati.Controllo.Tag = "CVC"
                    Else
                        If Not dati.EMultiplo Then dati.Controllo.Tag = "Numerico"
                    End If

                Case TipoCampo.Data
                    If Not dati.EMultiplo Then dati.Controllo.Tag = "Data"
                    ' La data si sceglie dall'elenco: il campo da scrivere si apre solo con "Scrivi la data a mano".
                    dati.SolaLettura = dati.Controllo.ReadOnly
                    If Not dati.SolaLettura Then dati.Controllo.ReadOnly = True
                    AddHandler dati.Controllo.Leave, AddressOf GestoreEventoUscitaCasella

                Case TipoCampo.Orario
                    If Not dati.EMultiplo Then dati.Controllo.Tag = "Orario"
                    AddHandler dati.Controllo.Leave, AddressOf GestoreEventoUscitaCasella

                Case TipoCampo.Host
                    If Not dati.EMultiplo Then dati.Controllo.Tag = "Host"
                    dati.Controllo.MaxLength = 255

                Case TipoCampo.DataScadenza
                    If Not dati.EMultiplo Then dati.Controllo.Tag = "DataScadenza"
                    AddHandler dati.Controllo.Leave, AddressOf GestoreEventoUscitaCasella

                Case TipoCampo.Password
                    If Not dati.EMultiplo Then dati.Controllo.Tag = "Password"
                    dati.Controllo.MaxLength = 255

                Case TipoCampo.Alfanumerico
                    If Not dati.EMultiplo Then dati.Controllo.Tag = "Alfanumerico"
                    dati.Controllo.MaxLength = 255

                Case TipoCampo.Multiriga
                    If Not dati.EMultiplo Then dati.Controllo.Tag = "Multiriga"
                    dati.Controllo.Multiline = True
                    dati.Controllo.Width = 700
                    dati.Controllo.Height = DIMENSIONE_FONT_CASELLA * 16

            End Select
        Catch ex As Exception
            _logger.ScriviLog("CampiFormInterno.ConfiguraCampoPerTipo: " & ex.Message)
        End Try
    End Sub

    ''' <summary>Restituisce il testo di un campo: se e' gia' stato fatto uno scatto (snapshot) del suo
    ''' valore, usa quello; altrimenti legge dal controllo live. Lo snapshot serve perche' il form nasconde
    ''' i campi fuori dalla finestra dei 3 visibili alla volta, e leggere direttamente dal controllo dopo
    ''' che e' stato nascosto o dopo che il form si e' chiuso non e' affidabile.</summary>
    Private Function TestoDiCampo(dati As MetadatiCampo) As String
        Try
            If dati Is Nothing Then Return String.Empty
            If dati.EWord Then Return TestoDocumentoWord(dati)
            If dati.Controllo Is Nothing Then Return String.Empty

            Dim casella As WGCampiMovimento = dati.Controllo

            If Not casella.IsDisposed AndAlso casella.Visible AndAlso Me.ActiveControl Is casella Then
                If casella.IsCampoPassword Then
                    casella.UseSystemPasswordChar = False
                    Dim testoInChiaroLive As String = casella.Text
                    casella.UseSystemPasswordChar = True
                    Return testoInChiaroLive
                End If
                Return casella.Text
            End If

            Dim valoreSalvato As String = Nothing
            If _valoriSalvati.TryGetValue(dati, valoreSalvato) Then Return valoreSalvato

            If casella.IsCampoPassword Then
                casella.UseSystemPasswordChar = False
                Dim testoInChiaro As String = casella.Text
                casella.UseSystemPasswordChar = True
                Return testoInChiaro
            End If
            Return casella.Text
        Catch ex As Exception
            _logger.ScriviLog("CampiFormInterno.TestoDiCampo: " & ex.Message)
            Return String.Empty
        End Try
    End Function

    Private Sub SalvaValoreCampo(dati As MetadatiCampo)
        Try
            If dati Is Nothing Then Return
            If dati.EWord Then Return
            If dati.Controllo Is Nothing Then Return

            Dim casella As WGCampiMovimento = dati.Controllo
            Dim testo As String
            If casella.IsCampoPassword Then
                casella.UseSystemPasswordChar = False
                testo = casella.Text
                casella.UseSystemPasswordChar = True
            Else
                testo = casella.Text
            End If

            _valoriSalvati(dati) = testo
        Catch ex As Exception
            _logger.ScriviLog("CampiFormInterno.SalvaValoreCampo: " & ex.Message)
        End Try
    End Sub

    Private Sub SalvaTuttiIValori()
        Try
            For Each dati As MetadatiCampo In _listaCampi
                SalvaValoreCampo(dati)
            Next
        Catch ex As Exception
            _logger.ScriviLog("CampiFormInterno.SalvaTuttiIValori: " & ex.Message)
        End Try
    End Sub

    Public Sub AggiornaTestoCampo(id As String, nuovoTesto As String)
        If Me.InvokeRequired Then
            Me.Invoke(New MethodInvoker(Sub() Me.AggiornaTestoCampo(id, nuovoTesto)))
            Return
        End If
        Try
            Dim campoTarget = _listaCampi.FirstOrDefault(Function(x) x.Identificativo = id AndAlso Not x.EMultiplo)
            If campoTarget Is Nothing OrElse campoTarget.EWord OrElse campoTarget.Controllo Is Nothing Then Return
            If campoTarget.Controllo.IsDisposed Then Return
            campoTarget.Controllo.Text = If(nuovoTesto, "")
        Catch ex As Exception
            _logger.ScriviLog("CampiFormInterno.AggiornaTestoCampo: " & ex.Message)
        End Try
    End Sub

    Public Function OttieniValoreCampo(id As String) As String
        Try
            Dim campoTarget = _listaCampi.FirstOrDefault(Function(x) x.Identificativo = id AndAlso Not x.EMultiplo)
            If campoTarget IsNot Nothing Then
                Return TestoDiCampo(campoTarget)
            Else
                Dim valoriMultipli = OttieniListaValoriMultipli(id)
                Return If(valoriMultipli.Count > 0, String.Join("|", valoriMultipli), String.Empty)
            End If
        Catch ex As Exception
            _logger.ScriviLog("CampiFormInterno.OttieniValoreCampo: " & ex.Message)
            Return String.Empty
        End Try
    End Function

    Public Function OttieniListaValoriMultipli(id As String) As List(Of String)
        Dim risultato As New List(Of String)
        Try
            Dim campiMultipli = _listaCampi.Where(Function(x) x.IdBaseMultiplo = id).ToList()
            For Each elemento In campiMultipli
                Dim testo As String = TestoDiCampo(elemento).Trim()
                If Not String.IsNullOrEmpty(testo) Then risultato.Add(testo)
            Next
        Catch ex As Exception
            _logger.ScriviLog("CampiFormInterno.OttieniListaValoriMultipli: " & ex.Message)
        End Try
        Return risultato
    End Function

    Private Sub GestorePressioneTastiNumerici(sender As Object, e As KeyPressEventArgs)
        Try
            If Not Char.IsControl(e.KeyChar) AndAlso Not Char.IsDigit(e.KeyChar) Then
                e.Handled = True
                SegnalaLettereNonConsentite(TryCast(sender, WGCampiMovimento))
            End If
        Catch ex As Exception
            _logger.ScriviLog("CampiFormInterno.GestorePressioneTastiNumerici: " & ex.Message)
        End Try
    End Sub

    Private Sub SegnalaLettereNonConsentite(campo As WGCampiMovimento)
        Try
            _sintesiVocale.LeggiTesto("Lettere non consentite")
            _messaggioLettereNonConsentite = "Lettere non consentite"
            _campoMessaggioLettereNonConsentite = campo
            _timerLettereNonConsentite.Stop()
            _timerLettereNonConsentite.Start()
            Me.Invalidate()
        Catch ex As Exception
            _logger.ScriviLog("CampiFormInterno.SegnalaLettereNonConsentite: " & ex.Message)
        End Try
    End Sub

    Private Sub _timerLettereNonConsentite_Tick(sender As Object, e As EventArgs) Handles _timerLettereNonConsentite.Tick
        Try
            _timerLettereNonConsentite.Stop()
            _messaggioLettereNonConsentite = Nothing
            _campoMessaggioLettereNonConsentite = Nothing
            Me.Invalidate()
        Catch ex As Exception
            _logger.ScriviLog("CampiFormInterno._timerLettereNonConsentite_Tick: " & ex.Message)
        End Try
    End Sub

    Private Sub GestoreIngressoCasella(sender As Object, e As EventArgs)
        Try
            If _messaggioErroreAttivo Then Return
            Dim casellaCorrente As WGCampiMovimento = TryCast(sender, WGCampiMovimento)
            If casellaCorrente IsNot Nothing Then
                RicordaIndiceCampo(OttieniIndiceCampoDaControllo(casellaCorrente))
                LeggiCampoAttivo(casellaCorrente)
                AggiornaTastieraTouk()
                AggiornaVisibilitaCampi(casellaCorrente)
                If Not ReferenceEquals(casellaCorrente, _campoAzioneTouch) Then
                    _campoAzioneTouch = Nothing
                    _indiceAzioneTouch = 0
                End If
                ApriElencoDateAlIngresso(casellaCorrente)
            End If
        Catch ex As Exception
            _logger.ScriviLog("CampiFormInterno.GestoreIngressoCasella: " & ex.Message)
        End Try
    End Sub

    Protected Overrides Function CreateAccessibilityInstance() As AccessibleObject
        Try
            Return New CampiFormAccessibleObject(Me)
        Catch ex As Exception
            _logger.ScriviLog("CampiFormInterno.CreateAccessibilityInstance: " & ex.Message)
            Return MyBase.CreateAccessibilityInstance()
        End Try
    End Function

    Private Class CampiFormAccessibleObject
        Inherits ControlAccessibleObject
        Public Sub New(owner As Control)
            MyBase.New(owner)
        End Sub
        Public Overrides Property Name As String
            Get
                Return String.Empty
            End Get
            Set(value As String)
            End Set
        End Property
    End Class

    Protected Overrides Sub Dispose(disposing As Boolean)
        Try
            If disposing Then
                If Not _chiusoCorrettamente AndAlso Not _chiusuraDaEscape Then
                    Try
                        RaiseEvent EventoChiusura(Me, EventArgs.Empty)
                    Catch exChiusura As Exception
                        _logger.ScriviLog("CampiFormInterno.Dispose (EventoChiusura): " & exChiusura.Message)
                    End Try
                End If
                _monitorVisibilitaInEsecuzione = False
                Try
                    If WGContenitore.Istanza IsNot Nothing Then WGContenitore.Istanza.RimuoviControllo(Me)
                Catch ex As Exception
                    _logger.ScriviLog("CampiFormInterno.Dispose (RimuoviWGForm): " & ex.Message)
                End Try

                If menuOpzioni IsNot Nothing AndAlso Not menuOpzioni.IsDisposed Then
                    Try
                        menuOpzioni.Dispose()
                    Catch ex As Exception
                        _logger.ScriviLog("CampiFormInterno.Dispose (menuOpzioni): " & ex.Message)
                    End Try
                End If

                Try
                    RilasciaCampiWord()
                Catch exCampiWord As Exception
                    _logger.ScriviLog("CampiFormInterno.Dispose (campi word): " & exCampiWord.Message)
                End Try

                If _tastiera IsNot Nothing Then
                    Try
                        RemoveHandler _tastiera.KeyDown, AddressOf _tastiera_KeyDown
                        RemoveHandler _tastiera.AltPremuto, AddressOf _tastiera_AltPremuto
                        RemoveHandler _tastiera.AltDestroPremuto, AddressOf _tastiera_AltDestroPremuto
                        RemoveHandler _tastiera.AltDestroRilasciato, AddressOf _tastiera_AltDestroRilasciato
                        _tastiera = Nothing
                    Catch ex As Exception
                        _logger.ScriviLog("CampiFormInterno.Dispose (Tastiera): " & ex.Message)
                    End Try
                End If

                If _touk IsNot Nothing Then
                    Try
                        RemoveHandler _touk.FlickAvanti, AddressOf _touk_FlickAvanti
                        RemoveHandler _touk.FlickIndietro, AddressOf _touk_FlickIndietro
                        RemoveHandler _touk.TapDoppio, AddressOf _touk_TapDoppio
                        RemoveHandler _touk.EscapeGesto, AddressOf _touk_EscapeGesto
                        RemoveHandler _touk.RichiestaLetturaCompleta, AddressOf _touk_RichiestaLetturaCompleta
                        RemoveHandler _touk.StatoCambiato, AddressOf _touk_StatoCambiato
                        _touk = Nothing
                    Catch ex As Exception
                        _logger.ScriviLog("CampiFormInterno.Dispose (Touk): " & ex.Message)
                    End Try
                End If

                If _dettaturaCampi IsNot Nothing Then
                    Try
                        RemoveHandler _dettaturaCampi.TrascrizioneCompletata, AddressOf OnTrascrizioneCompletataCampi
                        RemoveHandler _dettaturaCampi.TrascrizioneFallita, AddressOf OnTrascrizioneFallitaCampi
                        _dettaturaCampi.Dispose()
                        _dettaturaCampi = Nothing
                    Catch ex As Exception
                        _logger.ScriviLog("CampiFormInterno.Dispose (Dettatura): " & ex.Message)
                    End Try
                End If

                If _dettaturaCampiWord IsNot Nothing Then
                    Try
                        RemoveHandler _dettaturaCampiWord.TrascrizioneCompletata, AddressOf OnTrascrizioneCompletataCampoWord
                        RemoveHandler _dettaturaCampiWord.TrascrizioneFallita, AddressOf OnTrascrizioneFallitaCampoWord
                        _dettaturaCampiWord.Dispose()
                        _dettaturaCampiWord = Nothing
                    Catch ex As Exception
                        _logger.ScriviLog("CampiFormInterno.Dispose (DettaturaWord): " & ex.Message)
                    End Try
                End If

                If _tastoDettatura IsNot Nothing Then
                    Try
                        RemoveHandler _tastoDettatura.Interrotto, AddressOf OnTastoDettaturaInterrotto
                        If Not _tastoDettatura.IsDisposed Then _tastoDettatura.Dispose()
                        _tastoDettatura = Nothing
                    Catch ex As Exception
                        _logger.ScriviLog("CampiFormInterno.Dispose (TastoDettatura): " & ex.Message)
                    End Try
                End If

                RilasciaTastieraTouk()

                For Each campo In _listaCampi
                    If campo.Controllo IsNot Nothing Then
                        Try
                            RemoveHandler campo.Controllo.Enter, AddressOf GestoreIngressoCasella
                            RemoveHandler campo.Controllo.Leave, AddressOf GestoreEventoUscitaCasella
                            RemoveHandler campo.Controllo.TextChanged, AddressOf GestoreCambiamentoTestoCarta
                            RemoveHandler campo.Controllo.TextChanged, AddressOf GestoreCambiamentoTestoMultiplo
                            campo.Controllo.Dispose()
                        Catch ex As Exception
                            _logger.ScriviLog("CampiFormInterno.Dispose (Campo): " & ex.Message)
                        End Try
                    End If
                Next
                _listaCampi.Clear()
                _mappaIndiciCampi.Clear()
            End If
            MyBase.Dispose(disposing)
        Catch ex As Exception
            _logger.ScriviLog("CampiFormInterno.Dispose: " & ex.Message)
        End Try
    End Sub
    Private Sub RilasciaTastieraTouk()
        Try
            If _tastieraTouch Is Nothing Then Return
            Try
                RemoveHandler _tastieraTouch.CarattereInserito, AddressOf OnCarattereInseritoDaTastieraCampi
                RemoveHandler _tastieraTouch.EliminaRichiesto, AddressOf OnEliminaRichiestoDaTastieraCampi
                RemoveHandler _tastieraTouch.InvioRichiesto, AddressOf OnInvioRichiestoDaTastieraCampi
                RemoveHandler _tastieraTouch.ChiudiRichiesto, AddressOf OnChiudiRichiestoDaTastieraCampi
                RemoveHandler _tastieraTouch.TestoDettatoInserito, AddressOf OnTestoDettatoDaTastieraCampi
            Catch
            End Try
            If Not _tastieraTouch.IsDisposed Then _tastieraTouch.Dispose()
            _tastieraTouch = Nothing
        Catch ex As Exception
            _logger.ScriviLog("CampiFormInterno.RilasciaTastieraTouk: " & ex.Message)
        End Try
    End Sub

    Private Sub StampaCampoCorrente()
        Try
            If _campoConFocusMenu IsNot Nothing AndAlso _campoConFocusMenu.IsCampoPassword Then
                Dim msgPassword As New WGMessaggi()
                msgPassword.Messaggio("Non si stampa un campo password.")
                msgPassword.ShowDialog()
                Return
            End If
            Dim nomeStampante As String = New WGImpostazioniComuni().NomeStampanteDaUsare()
            If String.IsNullOrWhiteSpace(nomeStampante) Then
                Dim msgManca As New WGMessaggi()
                msgManca.Messaggio("Nessuna stampante impostata. Sceglila dalle impostazioni.")
                msgManca.ShowDialog()
                Return
            End If
            StampanteCampiSelezionata(nomeStampante)
        Catch ex As Exception
            _logger.ScriviLog("CampiFormInterno.StampaCampoCorrente: " & ex.Message)
        End Try
    End Sub
    Private Sub ListaStampantiCampiEsc()
        Try
            If _listaStampantiCampi IsNot Nothing Then _listaStampantiCampi.abbandona()
        Catch ex As Exception
            _logger.ScriviLog("CampiFormInterno.ListaStampantiCampiEsc: " & ex.Message)
        End Try
    End Sub

    Private Sub StampanteCampiSelezionata(nomeStampante As String)
        Try
            If String.IsNullOrWhiteSpace(nomeStampante) Then Return
            Dim ok As Boolean = False
            Dim editorWord As WGWord = TryCast(_controlloConFocusMenu, WGWord)
            Dim impostazioniStampa As WGImpostazioniStampa = New WGImpostazioniComuni().CostruisciImpostazioniStampa()
            If editorWord IsNot Nothing AndAlso Not editorWord.IsDisposed Then
                ok = editorWord.Stampa(nomeStampante, "Documento")
            ElseIf _campoConFocusMenu IsNot Nothing Then
                Dim testo As String = _campoConFocusMenu.Text
                ok = Global.WGStampante.WGStampante.StampaTesto(testo, nomeStampante, "Campo", impostazioniStampa)
            Else
                ImpostaMessaggioStampaFallita("Non c'è nulla da stampare.")
                If _listaStampantiCampi IsNot Nothing Then _listaStampantiCampi.abbandona()
                Return
            End If
            If _listaStampantiCampi IsNot Nothing Then _listaStampantiCampi.abbandona()
            Dim msg As New WGMessaggi()
            If ok Then
                msg.Messaggio("Stampa avviata.")
            Else
                Dim errore As String = Global.WGStampante.WGStampante.UltimoErrore()
                If String.IsNullOrWhiteSpace(errore) Then errore = "La stampa non è riuscita."
                msg.Messaggio(errore)
            End If
            msg.ShowDialog()
        Catch ex As Exception
            _logger.ScriviLog("CampiFormInterno.StampanteCampiSelezionata: " & ex.Message)
            Try
                If _listaStampantiCampi IsNot Nothing Then _listaStampantiCampi.abbandona()
            Catch
            End Try
        End Try
    End Sub

    Private Sub ImpostaMessaggioStampaFallita(testo As String)
        Try
            Dim msg As New WGMessaggi()
            msg.Messaggio(testo)
            msg.ShowDialog()
        Catch ex As Exception
            _logger.ScriviLog("CampiFormInterno.ImpostaMessaggioStampaFallita: " & ex.Message)
        End Try
    End Sub

    Public Overloads Sub dispose()
        Dispose(True)

    End Sub
End Class