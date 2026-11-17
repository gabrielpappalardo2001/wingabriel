Imports System.ComponentModel
Imports System.Runtime.Remoting.Metadata.W3cXsd2001
Imports System.Security.Cryptography
Imports System.Xml
Imports wgcontrols
Imports WGDati
Imports System.Windows.Forms
Imports System.Globalization
Imports System.Threading
Imports System.Threading.Tasks
Imports System.Collections.Generic
Imports System.IO
Imports WGMail

' Gestione delle note testuali e dei documenti Word. Permette la scrittura, la lettura, la ricerca e la protezione tramite
' password.
Friend Class MenuNote
    Private id As String
    Private password As String
    Private passwordcampi As Campi
    Private domanda As WGDomanda
    Private titolo As String
    Private testo As String
    Private idnota As String
    Private WithEvents titolocampo As WGCampoTesto
    Private WithEvents campi As Campi
    Private WithEvents testocampo As WGCampo
    Private WithEvents menu As WGMenu
    Private WithEvents listanote As WGMenuLista
    Private note As New CLSNote
    Private data As Date
    Private esterno As Boolean
    Private ReadOnly _logger As Logger
    Private StringaRicercaAttiva As String = ""
    Private DataRicercaAttiva As Date? = Nothing
    Private ModalitaRicerca As Integer = 0
    Private OrdinamentoCorrente As OrdinamentoNote = OrdinamentoNote.PiuRecenti
    Private processoincorso As WGProcessoInCorso
    Private attesa As WGProcessoInCorso
    Private _uiContext As SynchronizationContext
    Private _elencoNoteTemp As List(Of ElementoLista)

    ' Supporto per l'editor Word delle note (scrittura/lettura/modifica in formato .docx)
    Private WithEvents campiNota As Campi
    Private _percorsoTempNota As String = ""

    Public Sub New()
        Try
            _logger = New Logger()
        Catch ex As Exception
        End Try
    End Sub

    Public Sub Avvia()
        Try
            If _uiContext Is Nothing Then
                _uiContext = SynchronizationContext.Current
            End If
            menu = New WGMenu
            menu.aggiungi("Nuova nota", AddressOf nuovanota)
            menu.aggiungi("Consulta note", AddressOf consultanote)
            menu.aggiungi("Cerca note", AddressOf cercanote)
            menu.aggiungi("Impostazioni note", AddressOf impostazioninote)
            menu.avvia("Menu note")
        Catch ex As InvalidOperationException
            Dim msgBox As New WGMessaggi()
            msgBox.Messaggio("Errore di operazione non valida durante l'avvio del menu note: " & ex.Message)
            msgBox.ShowDialog()
            _logger.ScriviLog("InvalidOperationException in Avvia: " & ex.Message & " StackTrace: " & ex.StackTrace)
        Catch ex As Exception
            Dim msgBox As New WGMessaggi()
            msgBox.Messaggio("Si è verificato un errore inatteso durante l'avvio del menu note: " & ex.Message)
            msgBox.ShowDialog()
            _logger.ScriviLog("Exception in Avvia: " & ex.Message & " StackTrace: " & ex.StackTrace)
        End Try
    End Sub

    Private Function RecuperaIdSelezionato() As String
        Try
            If listanote Is Nothing Then Return String.Empty
            Dim listaElementi As List(Of String) = listanote.ottieniElementi()
            If listaElementi IsNot Nothing AndAlso listaElementi.Count > 0 Then
                Dim primoElemento As String = listaElementi(0)
                If IsNumeric(primoElemento) Then
                    Return primoElemento
                Else
                    Return String.Empty
                End If
            Else
                Return String.Empty
            End If
        Catch ex As Exception
            _logger.ScriviLog("Errore critico in RecuperaIdSelezionato: " & ex.Message)
            Return String.Empty
        End Try
    End Function

#Region "Impostazioni editor Word"

    Private Sub impostazioninote()
        Try
            menu = New WGMenu()

            menu.Aggiungi("Lettura", addressof lettura)
Menu.Aggiungi("Scrittura", AddressOf scrittura)
menu.avvia("Impostazioni", true)         
Catch ex As Exception
            Dim msgBox As New WGMessaggi()
            msgBox.Messaggio("Si è verificato un errore inatteso durante l'apertura delle impostazioni delle note: " & ex.Message)
            msgBox.ShowDialog()
            _logger.ScriviLog("Exception in impostazioninote: " & ex.Message & " StackTrace: " & ex.StackTrace)
        End Try
    End Sub

    
private sub lettura()
try
dim letturamenu as new WGMenu()
letturamenu.Aggiungi("Usa editor word per la lettura delle note testuali", AddressOf usaword)
letturamenu.Aggiungi("Usa editor standard per lettura note testuali", AddressOf usastandard)
letturamenu.Avvia("Lettura", true)
Catch ex As Exception
            Dim msgBox As New WGMessaggi()
            msgBox.Messaggio("Si è verificato un errore inatteso durante l'apertura delle impostazioni di lettura  delle note: " & ex.Message)
            msgBox.ShowDialog()
            _logger.ScriviLog("Exception in impostazioninote: " & ex.Message & " StackTrace: " & ex.StackTrace)
        End Try
end sub

private sub usaword()

try
note.UsaEditorWordLettura = true
dim messaggio as new WGMessaggi()
messaggio.messaggio("Editor word per la lettura delle note solo testo impostato correttamente")
messaggio.showdialog()
Catch ex As Exception
            Dim msgBox As New WGMessaggi()
            msgBox.Messaggio("Si è verificato un errore inatteso durante l'impostazione della modalità word per la lettura delle note: " & ex.Message)
            msgBox.ShowDialog()
            _logger.ScriviLog("Exception in impostazioninote: " & ex.Message & " StackTrace: " & ex.StackTrace)
        End Try
end sub
private sub usastandard()
try
note.UsaEditorWordLettura  = false
dim messaggio as new WGMessaggi()
messaggio.messaggio("Editor standard per la lettura delle note solo testo impostato correttamente")
messaggio.showdialog()
Catch ex As Exception
            Dim msgBox As New WGMessaggi()
            msgBox.Messaggio("Si è verificato un errore inatteso durante l'impostazione dell'editor standard per la lettura delle note solo testo: " & ex.Message)
            msgBox.ShowDialog()
            _logger.ScriviLog("Exception in impostazioninote: " & ex.Message & " StackTrace: " & ex.StackTrace)
        End Try
end sub
private sub scrittura()
try
dim scritturamenu as new WGMenu()
scritturamenu.aggiungi("Usa word come editor predefinito per la scrittura di nuove note",AddressOf  usawordscrittura)
scritturamenu.Aggiungi("Usa editor standard per scrittura note", AddressOf usastandardscrittura)
scritturamenu.avvia("scrittura", true)

Catch ex As Exception
            Dim msgBox As New WGMessaggi()
            msgBox.Messaggio("Si è verificato un errore inatteso durante l'apertura delle impostazioni di scrittura delle note: " & ex.Message)
            msgBox.ShowDialog()
            _logger.ScriviLog("Exception in impostazioninote: " & ex.Message & " StackTrace: " & ex.StackTrace)
        End Try
end sub
private sub usawordscrittura()
try
note.UsaEditorWord = true
dim messaggio as new WGMessaggi()
messaggio.messaggio("Editor word impostato correttamente per la scrittura delle nuove note")
messaggio.showdialog()
Catch ex As Exception
            Dim msgBox As New WGMessaggi()
            msgBox.Messaggio("Si è verificato un errore inatteso durante l'impostazione dell'editor word come metodo di scrittura delle nuove note:" & ex.Message)
            msgBox.ShowDialog()
            _logger.ScriviLog("Exception in impostazioninote: " & ex.Message & " StackTrace: " & ex.StackTrace)
        End Try
end sub
private sub usastandardscrittura()
try
note.UsaEditorWord  = false
dim messaggio as new WGMessaggi()
messaggio.messaggio("Editor standard per le nuove note impostato correttamente")
messaggio.showdialog()
Catch ex As Exception
            Dim msgBox As New WGMessaggi()
            msgBox.Messaggio("Si è verificato un errore inatteso durante l'impostazione dell'editor standard per la scrittura delle nuove note: " & ex.Message)
            msgBox.ShowDialog()
            _logger.ScriviLog("Exception in impostazioninote: " & ex.Message & " StackTrace: " & ex.StackTrace)
        End Try
end sub


    #End Region

#Region "Supporto file temporanei .docx"

    ''' <summary>Il file .docx temporaneo in cui si apre una nota in formato Word (nella cartella temp del programma).</summary>
    Private Function PercorsoTempNotaDocx() As String
        Try
            Dim cartella As String = Path.Combine(Application.StartupPath, "temp")
            If Not Directory.Exists(cartella) Then Directory.CreateDirectory(cartella)
            Return Path.Combine(cartella, "nota.docx")
        Catch ex As Exception
            ' la cartella del programma non è scrivibile: si usa quella temporanea di Windows
            _logger.ScriviLog("Errore in PercorsoTempNotaDocx: " & ex.Message)
            Return Path.Combine(Path.GetTempPath(), "nota_" & Guid.NewGuid().ToString("N") & ".docx")
        End Try
    End Function

    Private Sub EliminaFileTempNota(percorso As String)
        Try
            If Not String.IsNullOrWhiteSpace(percorso) AndAlso File.Exists(percorso) Then
                File.Delete(percorso)
            End If
        Catch ex As Exception
            _logger.ScriviLog("Errore in EliminaFileTempNota: " & ex.Message)
        End Try
    End Sub

#End Region

#Region "Creazione nuova nota con editor Word"

    ' Punto unico di ingresso nella scrittura di una nuova nota: sceglie l'editor
    ' testuale classico o l'editor Word in base all'impostazione UsaEditorWord.
    ' 'titolo', 'data' e 'password' sono già stati valorizzati dal flusso esistente
    ' (titolo/data/eventuale domanda sulla protezione) prima di essere richiamato.
    Private Sub AvviaScritturaNuovaNota()
        Try
            If note.UsaEditorWord Then
                campiNota = New Campi()
                campiNota.SalvaSuEscape = True
                campiNota.AggiungiWord(titolo, "nota.testo", false, False)
                AddHandler campiNota.Fine, AddressOf completaScritturaNuovaNotaWord
AddHandler campinota.chiusura, AddressOf chiudi
                campiNota.Avvia()
            Else
                testocampo = New WGCampo()
                testocampo.init("Scrivi il testo della nota", , True)
                AddHandler testocampo.chiusura, AddressOf testoinserito
                testocampo.avvia()
            End If
        Catch ex As Exception
            Dim msgBox As New WGMessaggi()
            msgBox.Messaggio("Si è verificato un errore inatteso durante l'apertura dell'editor per la nuova nota: " & ex.Message)
            msgBox.ShowDialog()
            _logger.ScriviLog("Exception in AvviaScritturaNuovaNota: " & ex.Message & " StackTrace: " & ex.StackTrace)
        End Try
    End Sub

    ''' <summary>completaScritturaNuovaNotaWord con la firma esatta dell'evento: così AddHandler e RemoveHandler agganciano e staccano lo stesso gestore.</summary>
    Private Sub completaScritturaNuovaNotaWord(senderEvento As Object, eEvento As EventArgs)
        completaScritturaNuovaNotaWord()
    End Sub

    Private Sub completaScritturaNuovaNotaWord()
        Try
            RemoveHandler campiNota.Fine, AddressOf completaScritturaNuovaNotaWord
            Dim contenuto As WGContenutoPosta = campiNota.RecuperaContenutoPosta("nota.testo")
            campiNota = Nothing

            Dim risultato As Boolean
            If DataValorizzata(data) Then
                risultato = note.AggiungiNotaHtml(titolo, contenuto, password, data)
            Else
                risultato = note.AggiungiNotaHtml(titolo, contenuto, password)
            End If

            If risultato Then
                Dim messaggio As New WGMessaggi()
                messaggio.Messaggio("Nota inserita")
                messaggio.ShowDialog()
            Else
                Dim messaggio As New WGMessaggi()
                messaggio.Messaggio("La nota non è stata aggiunta. Verificare i file di log, nella cartella logs: accanto al programma se è portatile, in local\wingabriel\logs se è installato.")
                messaggio.ShowDialog()
            End If
        Catch ex As Exception
            Dim msgBox As New WGMessaggi()
            msgBox.Messaggio("Si è verificato un errore inatteso durante il salvataggio della nuova nota Word: " & ex.Message)
            msgBox.ShowDialog()
            _logger.ScriviLog("Exception in completaScritturaNuovaNotaWord: " & ex.Message & " StackTrace: " & ex.StackTrace)
        End Try
    End Sub
private sub chiudi()
Try
            Dim contenuto As WGContenutoPosta = campiNota.RecuperaContenutoPosta("nota.testo")
            campiNota = Nothing

            Dim risultato As Boolean
            If DataValorizzata(data) Then
                risultato = note.AggiungiNotaHtml(titolo, contenuto, password, data)
            Else
                risultato = note.AggiungiNotaHtml(titolo, contenuto, password)
            End If

            If risultato Then
                Dim messaggio As New WGMessaggi()
                messaggio.Messaggio("Nota inserita")
                messaggio.ShowDialog()
            Else
                Dim messaggio As New WGMessaggi()
                messaggio.Messaggio("La nota non è stata aggiunta, verificare i file di log nella cartella WinGabriel nella cartella documenti")
                messaggio.ShowDialog()
            End If
        Catch ex As Exception
            Dim msgBox As New WGMessaggi()
            msgBox.Messaggio("Si è verificato un errore inatteso durante il salvataggio della nuova nota Word: " & ex.Message)
            msgBox.ShowDialog()
            _logger.ScriviLog("Exception in completaScritturaNuovaNotaWord: " & ex.Message & " StackTrace: " & ex.StackTrace)
        End Try

    End Sub
    Private Sub salva()
        Try
            Dim contenuto As WGContenutoPosta = campiNota.RecuperaContenutoPosta("nota.testo")

            Dim risultato As Boolean
            If DataValorizzata(data) Then
                risultato = note.AggiungiNotaHtml(titolo, contenuto, password, data)
            Else
                risultato = note.AggiungiNotaHtml(titolo, contenuto, password)
            End If

            If risultato Then
                Dim messaggio As New WGMessaggi()
                messaggio.Messaggio("Nota inserita")
                messaggio.ShowDialog()
            Else
                Dim messaggio As New WGMessaggi()
                messaggio.Messaggio("La nota non è stata aggiunta, verificare i file di log nella cartella WinGabriel nella cartella documenti")
                messaggio.ShowDialog()
            End If
        Catch ex As Exception
        Dim msgBox As New WGMessaggi()
        msgBox.Messaggio("Si è verificato un errore inatteso durante il salvataggio della nuova nota Word: " & ex.Message)
        msgBox.ShowDialog()
        _logger.ScriviLog("Exception in completaScritturaNuovaNotaWord: " & ex.Message & " StackTrace: " & ex.StackTrace)
        End Try

    End Sub

#End Region

#Region "Lettura note (editor standard o Word)"

    ' Wrapper con firma (sender, e) per riusare le stesse azioni già esistenti
    ' (abilitaprotezionenota/togliprotezione) anche dalle opzioni di Campi (Word),
    ' che si aspettano un gestore in stile EventHandler.
    Private Sub OpzioneAbilitaProtezioneNotaWord(sender As Object, e As EventArgs)
        abilitaprotezionenota()
    End Sub

    Private Sub OpzioneTogliProtezioneNotaWord(sender As Object, e As EventArgs)
        togliprotezione()
    End Sub

    ' Punto unico di apertura in lettura di una nota già esistente (protetta o no).
    ' Prima di chiamarla vanno valorizzate le variabili di modulo id/idnota/password
    ' esattamente come già avveniva nel codice originale.
    Private Sub ApriNotaInLettura(nota As Nota)
        Try
            If nota Is Nothing Then Return

            If String.IsNullOrEmpty(nota.Testo) Then
                Dim messaggio2 As New WGMessaggi()
                messaggio2.Messaggio("Non è stato possibile recuperare il testo della nota selezionata, verificare i log nella cartella WinGabriel in documenti per maggiori dettagli")
                messaggio2.ShowDialog()
                Return
            End If

            If nota.Formato = FormatoNota.Html Then
                If DataValorizzata(nota.DataCreazione) Then
                    Dim msgData As New WGMessaggi()
                    msgData.Messaggio(DataLeggibile(nota.DataCreazione))
                    msgData.ShowDialog()
                End If
                ApriNotaWordInLettura(nota)
                Return
            End If

            If note.UsaEditorWordLettura Then
                ApriNotaTestoInLetturaConWord(nota)
                Return
            End If

            ' Percorso classico, invariato rispetto al codice originale
            Dim testodaleggere As String
            If DataValorizzata(nota.DataCreazione) Then
                testodaleggere = DataLeggibile(nota.DataCreazione) & vbCrLf & nota.Testo
            Else
                testodaleggere = nota.Testo
            End If

            Dim testonotacampo As New WGCampo
            testonotacampo.init(nota.Titolo, testodaleggere, True, True)
            If nota.Protetta Then
                testonotacampo.aggiungiopzione("Disabilita protezione", AddressOf togliprotezione)
            Else
                testonotacampo.aggiungiopzione("Abilita protezione nota", AddressOf abilitaprotezionenota)
            End If
            testonotacampo.avvia()
        Catch ex As Exception
            Dim msgBox As New WGMessaggi()
            msgBox.Messaggio("Si è verificato un errore inatteso durante l'apertura della nota in lettura: " & ex.Message)
            msgBox.ShowDialog()
            _logger.ScriviLog("Exception in ApriNotaInLettura: " & ex.Message & " StackTrace: " & ex.StackTrace)
        End Try
    End Sub

    ' Nota già salvata come nota "stile Word" (Nota.Formato = Html): recupera il
    ' contenuto direttamente in memoria e lo mostra con l'editor Word in sola lettura.
    Private Sub ApriNotaWordInLettura(nota As Nota)
        Try
            Dim contenuto As WGContenutoPosta
            If nota.Protetta Then
                contenuto = note.RecuperaContenutoNotaHtml(nota.Id, password)
            Else
                contenuto = note.RecuperaContenutoNotaHtml(nota.Id)
            End If

            If contenuto Is Nothing Then
                Dim msg As New WGMessaggi()
                msg.Messaggio("Non è stato possibile preparare il documento Word della nota selezionata.")
                msg.ShowDialog()
                Return
            End If

            campiNota = New Campi()
            campiNota.SalvaSuEscape = False
            campiNota.AggiungiWord(nota.Titolo, "nota.lettura", False, True)
            If nota.Protetta Then
                campiNota.AggiungiOpzione("Disabilita protezione", AddressOf OpzioneTogliProtezioneNotaWord)
            Else
                campiNota.AggiungiOpzione("Abilita protezione nota", AddressOf OpzioneAbilitaProtezioneNotaWord)
            End If
            AddHandler campiNota.Chiusura, AddressOf ChiusuraLetturaNotaWord

            Dim immaginiInline As New Dictionary(Of String, Byte())
            If contenuto.Immagini IsNot Nothing Then
                For Each img As WGImmaginePosta In contenuto.Immagini
                    If img IsNot Nothing AndAlso Not String.IsNullOrEmpty(img.Cid) Then
                        immaginiInline(img.Cid) = img.Dati
                    End If
                Next
            End If
            campiNota.CaricaWordDaHtml("nota.lettura", contenuto.Html, immaginiInline, nota.Titolo)

            campiNota.Avvia()
        Catch ex As Exception
            Dim msgBox As New WGMessaggi()
            msgBox.Messaggio("Si è verificato un errore inatteso durante l'apertura della nota Word in lettura: " & ex.Message)
            msgBox.ShowDialog()
            _logger.ScriviLog("Exception in ApriNotaWordInLettura: " & ex.Message & " StackTrace: " & ex.StackTrace)
        End Try
    End Sub

    Private Sub ChiusuraLetturaNotaWord(sender As Object, e As EventArgs)
        Try
            RemoveHandler campiNota.Chiusura, AddressOf ChiusuraLetturaNotaWord
            campiNota = Nothing
        Catch ex As Exception
            _logger.ScriviLog("Errore in ChiusuraLetturaNotaWord: " & ex.Message)
        End Try
    End Sub

    ' Nota in formato Testo, ma con l'impostazione "Lettura con editor Word" attiva:
    ' il contenuto viene convertito al volo in HTML e mostrato con l'editor Word,
    ' senza modificare in alcun modo il formato realmente salvato sul database.
    Private Sub ApriNotaTestoInLetturaConWord(nota As Nota)
        Try
            Dim html As String = CLSMail.TestoSempliceInHtml(If(nota.Testo, ""))
            If DataValorizzata(nota.DataCreazione) Then
                html = "<p><strong>" & System.Web.HttpUtility.HtmlEncode(DataLeggibile(nota.DataCreazione)) & "</strong></p>" & html
            End If

            campiNota = New Campi()
            campiNota.SalvaSuEscape = False
            campiNota.AggiungiWord(nota.Titolo, "nota.lettura", False, True)
            If nota.Protetta Then
                campiNota.AggiungiOpzione("Disabilita protezione", AddressOf OpzioneTogliProtezioneNotaWord)
            Else
                campiNota.AggiungiOpzione("Abilita protezione nota", AddressOf OpzioneAbilitaProtezioneNotaWord)
            End If
            AddHandler campiNota.Chiusura, AddressOf ChiusuraLetturaNotaWordSemplice
            campiNota.Avvia()
            campiNota.CaricaWordDaHtml("nota.lettura", html, Nothing, nota.Titolo)
        Catch ex As Exception
            Dim msgBox As New WGMessaggi()
            msgBox.Messaggio("Si è verificato un errore inatteso durante la visualizzazione della nota con l'editor Word: " & ex.Message)
            msgBox.ShowDialog()
            _logger.ScriviLog("Exception in ApriNotaTestoInLetturaConWord: " & ex.Message & " StackTrace: " & ex.StackTrace)
        End Try
    End Sub

    Private Sub ChiusuraLetturaNotaWordSemplice(sender As Object, e As EventArgs)
        Try
            RemoveHandler campiNota.Chiusura, AddressOf ChiusuraLetturaNotaWordSemplice
            campiNota = Nothing
        Catch ex As Exception
            _logger.ScriviLog("Errore in ChiusuraLetturaNotaWordSemplice: " & ex.Message)
        End Try
    End Sub

#End Region

#Region "Modifica testo di una nota Word esistente"

    ' Nota già salvata come nota "stile Word" (Nota.Formato = Html): recupera il
    ' contenuto corrente direttamente in memoria e lo riapre nell'editor Word per la
    ' modifica; passwordNota è "" se la nota non è protetta.
    Private Sub AvviaModificaNotaWord(idNotaDaModificare As Integer, passwordNota As String)
        Try
            idnota = idNotaDaModificare.ToString()
            id = idnota
            password = passwordNota

            Dim contenuto As WGContenutoPosta = note.RecuperaContenutoNotaHtml(idNotaDaModificare, passwordNota)
            If contenuto Is Nothing Then
                Dim msg As New WGMessaggi()
                msg.Messaggio("Non è stato possibile preparare il documento Word della nota selezionata per la modifica.")
                msg.ShowDialog()
                Return
            End If

            campiNota = New Campi()
            
            campiNota.AggiungiWord("Modifica testo nota", "nota.modifica", True, False)
            If String.IsNullOrEmpty(passwordNota) Then
                campiNota.AggiungiOpzione("Abilita protezione nota", AddressOf OpzioneAbilitaProtezioneNotaWord)
            Else
                campiNota.AggiungiOpzione("Disabilita protezione nota", AddressOf OpzioneTogliProtezioneNotaWord)
            End If
            AddHandler campiNota.Fine, AddressOf completaModificaNotaWord
AddHandler campiNota.Chiusura, AddressOf completaModificaNotaWord
            Dim immaginiInline As New Dictionary(Of String, Byte())
            If contenuto.Immagini IsNot Nothing Then
                For Each img As WGImmaginePosta In contenuto.Immagini
                    If img IsNot Nothing AndAlso Not String.IsNullOrEmpty(img.Cid) Then
                        immaginiInline(img.Cid) = img.Dati
                    End If
                Next
            End If
            campiNota.CaricaWordDaHtml("nota.modifica", contenuto.Html, immaginiInline, "Modifica testo nota")

            campiNota.Avvia()
        Catch ex As Exception
            Dim msgBox As New WGMessaggi()
            msgBox.Messaggio("Si è verificato un errore inatteso durante l'apertura della nota Word per la modifica: " & ex.Message)
            msgBox.ShowDialog()
            _logger.ScriviLog("Exception in AvviaModificaNotaWord: " & ex.Message & " StackTrace: " & ex.StackTrace)
        End Try
    End Sub

    ''' <summary>completaModificaNotaWord con la firma esatta dell'evento: così AddHandler e RemoveHandler agganciano e staccano lo stesso gestore.</summary>
    Private Sub completaModificaNotaWord(senderEvento As Object, eEvento As EventArgs)
        completaModificaNotaWord()
    End Sub

    Private Sub completaModificaNotaWord()
        Try
            RemoveHandler campiNota.Fine, AddressOf completaModificaNotaWord
RemoveHandler campiNota.Chiusura, AddressOf completaModificaNotaWord
            Dim contenuto As WGContenutoPosta = campiNota.RecuperaContenutoPosta("nota.modifica")
            campiNota = Nothing

            Dim risultato As Boolean = note.ModificaNotaHtml(CInt(idnota), contenuto, password)

            If risultato Then
                Dim messaggio As New WGMessaggi()
                messaggio.Messaggio("Nota aggiornata")
                messaggio.ShowDialog()
            Else
                Dim messaggio As New WGMessaggi()
                messaggio.Messaggio("Non è stato possibile aggiornare il testo della nota, verificare i log nella cartella WinGabriel in documenti")
                messaggio.ShowDialog()
            End If
        Catch ex As Exception
            Dim msgBox As New WGMessaggi()
            msgBox.Messaggio("Si è verificato un errore inatteso durante il salvataggio della nota Word modificata: " & ex.Message)
            msgBox.ShowDialog()
            _logger.ScriviLog("Exception in completaModificaNotaWord: " & ex.Message & " StackTrace: " & ex.StackTrace)
        End Try
    End Sub

#End Region

    Private Sub nuovanota()
        Try
            domanda = New WGDomanda()
            AddHandler domanda.rispostadomanda, AddressOf rispostadata
            domanda.avvia("Si vuole inserire la data nella nota?")
        Catch ex As NullReferenceException
            Dim msgBox As New WGMessaggi()
            msgBox.Messaggio("Errore: Riferimento a oggetto non impostato durante la creazione della domanda: " & ex.Message)
            msgBox.ShowDialog()
            _logger.ScriviLog("NullReferenceException in nuovanota: " & ex.Message & " StackTrace: " & ex.StackTrace)
        Catch ex As Exception
            Dim msgBox As New WGMessaggi()
            msgBox.Messaggio("Si è verificato un errore inatteso durante l'inizializzazione della nuova nota: " & ex.Message)
            msgBox.ShowDialog()
            _logger.ScriviLog("Exception in nuovanota: " & ex.Message & " StackTrace: " & ex.StackTrace)
        End Try
    End Sub

    Private Sub rispostadata(risposta As DialogResult)
        Try
            domanda = Nothing
            If risposta = DialogResult.Yes Then
                campi = New Campi()
                campi.AggiungiData("Inserisci la data", "c.data", True)
                campi.Aggiungi("Inserisci titolo", "c.titolo", True)
                AddHandler campi.Fine, AddressOf recupera
                campi.Avvia()
            ElseIf risposta = DialogResult.No Then
                NotaExt()
            End If
        Catch ex As NullReferenceException
            Dim msgBox As New WGMessaggi()
            msgBox.Messaggio("Errore: Riferimento a oggetto non impostato durante la gestione della risposta data: " & ex.Message)
            msgBox.ShowDialog()
            _logger.ScriviLog("NullReferenceException in rispostadata: " & ex.Message & " StackTrace: " & ex.StackTrace)
        Catch ex As Exception
            Dim msgBox As New WGMessaggi()
            msgBox.Messaggio("Si è verificato un errore inatteso durante la gestione della risposta data: " & ex.Message)
            msgBox.ShowDialog()
            _logger.ScriviLog("Exception in rispostadata: " & ex.Message & " StackTrace: " & ex.StackTrace)
        End Try
    End Sub

    Private Sub recupera()
        Try
            titolo = campi.RecuperaValore("c.titolo")
            Dim datastringa = campi.RecuperaValore("c.data")
            campi = Nothing

            If datastringa = "" OrElse datastringa = " " Then
                Exit Sub
            Else
                data = StringaAData(datastringa)
            End If

            If titolo = " " Then
                Exit Sub
            Else
                domanda = New WGDomanda()
                AddHandler domanda.rispostadomanda, AddressOf risposta
                domanda.avvia("Si vuole applicare una protezione alla nota?", True)
            End If
        Catch ex As FormatException
            Dim msgBox As New WGMessaggi()
            msgBox.Messaggio("Errore di formato della data inserita: " & ex.Message)
            msgBox.ShowDialog()
            _logger.ScriviLog("FormatException in campinota: " & ex.Message & " Data Stringa: " & campi.RecuperaValore("c.data") & " StackTrace: " & ex.StackTrace)
        Catch ex As NullReferenceException
            Dim msgBox As New WGMessaggi()
            msgBox.Messaggio("Errore: Riferimento a oggetto non impostato durante la gestione dei campi nota: " & ex.Message)
            msgBox.ShowDialog()
            _logger.ScriviLog("NullReferenceException in campinota: " & ex.Message & " StackTrace: " & ex.StackTrace)
        Catch ex As Exception
            Dim msgBox As New WGMessaggi()
            msgBox.Messaggio("Si è verificato un errore inatteso durante la gestione dei campi nota: " & ex.Message)
            msgBox.ShowDialog()
            _logger.ScriviLog("Exception in campinota: " & ex.Message & " StackTrace: " & ex.StackTrace)
        End Try
    End Sub

    Public Sub NotaExt(Optional datainserimento As String = "")
        Try
            If datainserimento = "" OrElse datainserimento = " " Then
                esterno = True
            Else
                esterno = False
                data = StringaAData(datainserimento)
            End If

            titolocampo = New WGCampoTesto()
            titolocampo.init("Inserisci il titolo che vuoi dare alla nota", " ", False, False, False, True)
            AddHandler titolocampo.chiusura, AddressOf titoloinserito
            titolocampo.avvia()
        Catch ex As FormatException
            Dim msgBox As New WGMessaggi()
            msgBox.Messaggio("Errore di formato della data per Nota Esterna: " & ex.Message)
            msgBox.ShowDialog()
            _logger.ScriviLog("FormatException in NotaExt: " & ex.Message & " Data Stringa: " & datainserimento & " StackTrace: " & ex.StackTrace)
        Catch ex As NullReferenceException
            Dim msgBox As New WGMessaggi()
            msgBox.Messaggio("Errore: Riferimento a oggetto non impostato durante l'inizializzazione di NotaEst: " & ex.Message)
            msgBox.ShowDialog()
            _logger.ScriviLog("NullReferenceException in NotaExt: " & ex.Message & " StackTrace: " & ex.StackTrace)
        Catch ex As Exception
            Dim msgBox As New WGMessaggi()
            msgBox.Messaggio("Si è verificato un errore inatteso durante la creazione di una nota esterna: " & ex.Message)
            msgBox.ShowDialog()
            _logger.ScriviLog("Exception in NotaExt: " & ex.Message & " StackTrace: " & ex.StackTrace)
        End Try
    End Sub

    Private Sub titoloinserito()
        Try
            titolo = titolocampo.ottienitesto()
            titolocampo.chiudi()
            If titolo = " " Then
                Exit Sub
            Else
                domanda = New WGDomanda()
                AddHandler domanda.rispostadomanda, AddressOf risposta
                domanda.avvia("Si vuole applicare una protezione alla nota?", True)
            End If
        Catch ex As NullReferenceException
            Dim msgBox As New WGMessaggi()
            msgBox.Messaggio("Errore: Riferimento a oggetto non impostato dopo l'inserimento del titolo: " & ex.Message)
            msgBox.ShowDialog()
            _logger.ScriviLog("NullReferenceException in titoloinserito: " & ex.Message & " StackTrace: " & ex.StackTrace)
        Catch ex As Exception
            Dim msgBox As New WGMessaggi()
            msgBox.Messaggio("Si è verificato un errore inatteso dopo l'inserimento del titolo: " & ex.Message)
            msgBox.ShowDialog()
            _logger.ScriviLog("Exception in titoloinserito: " & ex.Message & " StackTrace: " & ex.StackTrace)
        End Try
    End Sub

    Private Sub risposta(rispostadom As DialogResult)
        Try
            domanda = Nothing
            If rispostadom = DialogResult.Yes Then
                passwordcampi = New Campi()
                passwordcampi.AggiungiPassword("inserisci la password per la nota", "c.notapassword", True)
                AddHandler passwordcampi.Fine, AddressOf notapassword
                passwordcampi.avvia()
            ElseIf rispostadom = DialogResult.No Then
                password = ""
                AvviaScritturaNuovaNota()
            Else
                Exit Sub
            End If
        Catch ex As NullReferenceException
            Dim msgBox As New WGMessaggi()
            msgBox.Messaggio("Errore: Riferimento a oggetto non impostato durante la gestione della risposta sulla protezione: " & ex.Message)
            msgBox.ShowDialog()
            _logger.ScriviLog("NullReferenceException in risposta: " & ex.Message & " StackTrace: " & ex.StackTrace)
        Catch ex As Exception
            Dim msgBox As New WGMessaggi()
            msgBox.Messaggio("Si è verificato un errore inatteso durante la gestione della risposta sulla protezione: " & ex.Message)
            msgBox.ShowDialog()
            _logger.ScriviLog("Exception in risposta: " & ex.Message & " StackTrace: " & ex.StackTrace)
        End Try
    End Sub

    Private Sub notapassword()
        Try
            password = passwordcampi.RecuperaValore("c.notapassword")
            passwordcampi = Nothing
            If password = " " Then
                Dim messaggiopassword As New WGMessaggi()
                messaggiopassword.Messaggio("Il campo della password risulta vuoto, pertanto la protezione non è stata applicata e la creazione della nota è stata annullata.")
                messaggiopassword.ShowDialog()
                Exit Sub
            Else
                AvviaScritturaNuovaNota()
            End If
        Catch ex As NullReferenceException
            Dim msgBox As New WGMessaggi()
            msgBox.Messaggio("Errore: Riferimento a oggetto non impostato durante l'inserimento della password della nota: " & ex.Message)
            msgBox.ShowDialog()
            _logger.ScriviLog("NullReferenceException in notapassword: " & ex.Message & " StackTrace: " & ex.StackTrace)
        Catch ex As Exception
            Dim msgBox As New WGMessaggi()
            msgBox.Messaggio("Si è verificato un errore inatteso durante l'inserimento della password della nota: " & ex.Message)
            msgBox.ShowDialog()
            _logger.ScriviLog("Exception in notapassword: " & ex.Message & " StackTrace: " & ex.StackTrace)
        End Try
    End Sub

    Private Sub testoinserito()
        Try
            testo = testocampo.ottienitesto()
            testocampo.chiudi()

            If testo = "" Then
                Dim messaggio As New WGMessaggi()
                messaggio.Messaggio("Il testo inserito risulta vuoto, pertanto la creazione della nota è stata annullata")
                messaggio.ShowDialog()
                Exit Sub
            Else
                Dim risultato As Boolean

                If password = "" Then
                    If DataValorizzata(data) = True Then
                        risultato = note.AggiungiNota(titolo, testo, , data)
                    Else
                        risultato = note.AggiungiNota(titolo, testo)
                    End If
                Else
                    If DataValorizzata(data) = True Then
                        risultato = note.AggiungiNota(titolo, testo, password, data)
                    Else
                        risultato = note.AggiungiNota(titolo, testo, password)
                    End If
                End If

                If risultato = True Then
                    Dim messaggio As New WGMessaggi()
                    messaggio.Messaggio("Nota inserita")
                    messaggio.ShowDialog()
                Else
                    Dim messaggio As New WGMessaggi()
                    messaggio.Messaggio("La nota non è stata aggiunta, verificare i file di log nella cartella WinGabriel nella cartella documenti")
                    messaggio.ShowDialog()
                End If
            End If
        Catch ex As CryptographicException
            Dim msgBox As New WGMessaggi()
            msgBox.Messaggio("Errore di crittografia durante il salvataggio della nota: " & ex.Message)
            msgBox.ShowDialog()
            _logger.ScriviLog("CryptographicException in testoinserito: " & ex.Message & " StackTrace: " & ex.StackTrace)
        Catch ex As ArgumentNullException
            Dim msgBox As New WGMessaggi()
            msgBox.Messaggio("Errore: Il testo o il titolo della nota non possono essere nulli: " & ex.Message)
            msgBox.ShowDialog()
            _logger.ScriviLog("ArgumentNullException in testoinserito: " & ex.Message & " StackTrace: " & ex.StackTrace)
        Catch ex As NullReferenceException
            Dim msgBox As New WGMessaggi()
            msgBox.Messaggio("Errore: Riferimento a oggetto non impostato durante l'inserimento del testo della nota: " & ex.Message)
            msgBox.ShowDialog()
            _logger.ScriviLog("NullReferenceException in testoinserito: " & ex.Message & " StackTrace: " & ex.StackTrace)
        Catch ex As Exception
            Dim msgBox As New WGMessaggi()
            msgBox.Messaggio("Si è verificato un errore inatteso durante l'inserimento del testo della nota: " & ex.Message)
            msgBox.ShowDialog()
            _logger.ScriviLog("Exception in testoinserito: " & ex.Message & " StackTrace: " & ex.StackTrace)
        End Try
    End Sub

    Private Sub cercanote()
        Try
            menu = New WGMenu()
            menu.aggiungi("Ricerca per parola chiave", AddressOf cercaparola)
            menu.aggiungi("Ricerca per data", AddressOf cercadata)
            menu.avvia("Ricerca Note", True)
        Catch ex As Exception
            _logger.ScriviLog("Errore in cercanote: " & ex.Message)
        End Try
    End Sub

    Private Sub cercaparola()
        Try
            campi = New Campi()
            campi.Aggiungi("Scrivi termine da cercare", "c.ricerca", True)
            AddHandler campi.Fine, AddressOf avviaricercaparola
            campi.Avvia()
        Catch ex As Exception
            _logger.ScriviLog("Errore in cercaparola: " & ex.Message)
        End Try
    End Sub

    Private Sub cercadata()
        Try
            campi = New Campi()
            campi.AggiungiData("Inserisci la data da cercare", "c.data", True)
            AddHandler campi.Fine, AddressOf avviaricercadata
            campi.Avvia()
        Catch ex As Exception
            _logger.ScriviLog("Errore in cercadata: " & ex.Message)
        End Try
    End Sub

    Private Sub avviaricercaparola()
        Try
            Dim valore As String = campi.RecuperaValore("c.ricerca")
            campi = Nothing

            If valore <> "" AndAlso valore <> " " Then
                ModalitaRicerca = 1
                StringaRicercaAttiva = valore
                DataRicercaAttiva = Nothing
                AvviaCaricamento()
            Else
                Dim messaggio As New WGMessaggi()
                messaggio.Messaggio("Nessun termine di ricerca inserito.")
                messaggio.ShowDialog()
            End If
        Catch ex As Exception
            _logger.ScriviLog("Errore in avviaricercaparola: " & ex.Message)
        End Try
    End Sub

    Private Sub avviaricercadata()
        Try
            Dim datastringa As String = campi.RecuperaValore("c.data")
            campi = Nothing

            If datastringa <> "" AndAlso datastringa <> " " Then
                ModalitaRicerca = 2
                DataRicercaAttiva = StringaAData(datastringa)
                StringaRicercaAttiva = ""
                AvviaCaricamento()
            Else
                Dim messaggio As New WGMessaggi()
                messaggio.Messaggio("Nessuna data di ricerca inserita.")
                messaggio.ShowDialog()
            End If
        Catch ex As Exception
            _logger.ScriviLog("Errore in avviaricercadata: " & ex.Message)
        End Try
    End Sub

    Public Sub Consulta(Optional dataRiferimento As String = "")
        Try
            If Not String.IsNullOrWhiteSpace(dataRiferimento) Then
                Try
                    DataRicercaAttiva = StringaAData(dataRiferimento)
                    ModalitaRicerca = 2
                    StringaRicercaAttiva = ""
                Catch ex As Exception
                    ModalitaRicerca = 0
                    DataRicercaAttiva = Nothing
                End Try
            Else
                ModalitaRicerca = 0
                DataRicercaAttiva = Nothing
                StringaRicercaAttiva = ""
            End If

            OrdinamentoCorrente = OrdinamentoNote.PiuRecenti
            AvviaCaricamento()
        Catch ex As Exception
            _logger.ScriviLog("Errore in Consulta pubblica: " & ex.Message)
        End Try
    End Sub

    Private Sub consultanote()
        Consulta()
    End Sub

    Private Sub AvviaCaricamento()
        Try
            If attesa Is Nothing Then
                attesa = New WGProcessoInCorso()
            End If
            attesa.Avvia("Caricamento note in corso...")
            Dim t As New Thread(AddressOf ThreadCaricaDati)
            t.IsBackground = True
            t.Start()
        Catch ex As Exception
            _logger.ScriviLog("Errore in AvviaCaricamento: " & ex.Message)
        End Try
    End Sub

    Private Sub ThreadCaricaDati()
        Try
            Dim dataFineRicerca As Date = Date.MinValue
            If DataRicercaAttiva.HasValue Then
                dataFineRicerca = DataRicercaAttiva.Value.AddDays(1).AddSeconds(-1)
            End If

            Dim limitMassivo As Integer = Integer.MaxValue

            Select Case ModalitaRicerca
                Case 0
                    _elencoNoteTemp = note.RecuperaNotePaginata(1, limitMassivo, OrdinamentoCorrente)
                Case 1
                    _elencoNoteTemp = note.CercaNotePaginata(StringaRicercaAttiva, 1, limitMassivo, OrdinamentoCorrente)
                Case 2
                    _elencoNoteTemp = note.RecuperaNotePaginata(1, limitMassivo, OrdinamentoCorrente, DataRicercaAttiva, dataFineRicerca)
            End Select

            If _uiContext IsNot Nothing Then
                _uiContext.Post(Sub(state) UI_MostraLista(), Nothing)
            Else
                UI_MostraLista()
            End If
        Catch ex As Exception
            _logger.ScriviLog("Errore in ThreadCaricaDati: " & ex.Message)
            If _uiContext IsNot Nothing Then
                _uiContext.Post(Sub(state)
                                    If attesa IsNot Nothing Then attesa.Fine()
                                End Sub, Nothing)
            Else
                If attesa IsNot Nothing Then attesa.Fine()
            End If
        End Try
    End Sub

    Private Sub UI_MostraLista()
        Try
            If attesa IsNot Nothing Then
                attesa.Fine()
                attesa = Nothing
            End If

            Dim titoloLista As String
            Select Case ModalitaRicerca
                Case 1
                    titoloLista = "Risultati ricerca: " & StringaRicercaAttiva
                Case 2
                    titoloLista = If(DataRicercaAttiva.HasValue, "Note del " & DataLeggibile(DataRicercaAttiva.Value), "Consultazione note")
                Case Else
                    titoloLista = "Consultazione note"
            End Select

            If _elencoNoteTemp IsNot Nothing AndAlso _elencoNoteTemp.Count > 0 Then
                Dim isNew As Boolean = False
                If listanote Is Nothing OrElse listanote.IsDisposed Then
                    listanote = New WGMenuLista()
                    listanote.multiselezioneConsentita = False
                    listanote.ciclono = True

                    AddHandler listanote.ElementoSelezionato, AddressOf notaselezionata
                    AddHandler listanote.escPremuto, AddressOf chiudinote

                    listanote.aggiungiOpzione("Modifica titolo nota", AddressOf modificatitolo)
                    listanote.aggiungiOpzione("Modifica testo nota", AddressOf modificatesto)
                    listanote.aggiungiOpzione("Elimina nota", AddressOf eliminanota)
                    listanote.aggiungiOpzione("Ordinamento", AddressOf menuordinamento)
                    listanote.aggiungiOpzione("Compattazione dati", AddressOf compattadati)

                    isNew = True
                Else
                    listanote.pulisci()
                End If

                listanote.impostaTitolo(titoloLista)
                listanote.CaricaListaDinamica(_elencoNoteTemp)

                If isNew Then
                    listanote.avvia()
                End If
            Else
                If listanote IsNot Nothing AndAlso Not listanote.IsDisposed Then
                    chiudinote()
                End If
                Dim messaggio As New WGMessaggi()
                messaggio.Messaggio("Nessuna nota trovata.")
                messaggio.ShowDialog()
            End If
        Catch ex As Exception
            _logger.ScriviLog("Errore in UI_MostraLista: " & ex.Message)
        End Try
    End Sub

    Private Sub compattadati()
        Try
            processoincorso = New WGProcessoInCorso()
            processoincorso.Avvia("Compattazione dati in corso, attendere")

            Dim ctx = System.Threading.SynchronizationContext.Current

            Task.Run(Sub()
                         Dim esito As Boolean = False
                         Try
                             esito = note.CompattaDatabase()
                         Catch ex As Exception
                             _logger.ScriviLog("Errore critico durante compattazione in task: " & ex.Message)
                         End Try

                         ctx.Post(Sub(state)
                                      Try
                                          processoincorso.Fine()
                                          Dim msg As New WGMessaggi()
                                          If esito Then
                                              msg.Messaggio("Compattazione completata.")
                                          Else
                                              msg.Messaggio("Errore durante la compattazione.")
                                          End If
                                          msg.ShowDialog()
                                      Catch ex As Exception
                                          _logger.ScriviLog("Errore durante la chiusura del processo di compattazione: " & ex.Message)
                                      End Try
                                  End Sub, Nothing)
                     End Sub)
        Catch ex As Exception
            _logger.ScriviLog("Errore avvio compattazione: " & ex.Message)
        End Try
    End Sub

    Private Sub menuordinamento()
        Try
            menu = New WGMenu()
            menu.aggiungi("Più recenti", AddressOf ordinaPiuRecenti)
            menu.aggiungi("Meno recenti", AddressOf ordinaMenoRecenti)
            menu.aggiungi("Alfabetico A-Z", AddressOf ordinaAlfabeticoAZ)
            menu.aggiungi("Alfabetico Z-A", AddressOf ordinaAlfabeticoZA)
            menu.avvia("Ordinamento", True)
        Catch ex As Exception
            _logger.ScriviLog("Errore in menuordinamento: " & ex.Message)
        End Try
    End Sub

    Private Sub ordinaPiuRecenti()
        OrdinamentoCorrente = OrdinamentoNote.PiuRecenti
        AvviaCaricamento()
    End Sub

    Private Sub ordinaMenoRecenti()
        OrdinamentoCorrente = OrdinamentoNote.MenoRecenti
        AvviaCaricamento()
    End Sub

    Private Sub ordinaAlfabeticoAZ()
        OrdinamentoCorrente = OrdinamentoNote.AlfabeticoAZ
        AvviaCaricamento()
    End Sub

    Private Sub ordinaAlfabeticoZA()
        OrdinamentoCorrente = OrdinamentoNote.AlfabeticoZA
        AvviaCaricamento()
    End Sub

    Private Sub modificatitolo()
        Try
            Dim idRecuperato As String = RecuperaIdSelezionato()
            If idRecuperato = String.Empty Then
                Dim msg As New WGMessaggi()
                msg.Messaggio("Seleziona una nota prima di premere Modifica.")
                msg.ShowDialog()
                Exit Sub
            End If

            idnota = idRecuperato

            titolocampo = New WGCampoTesto()
            titolocampo.init("Scrivi il nuovo titolo da dare alla nota", " ", False, False, False, True)
            AddHandler titolocampo.chiusura, AddressOf modificatitolonota
            titolocampo.avvia()

        Catch ex As NullReferenceException
            Dim msgBox As New WGMessaggi()
            msgBox.Messaggio("Errore: Riferimento a oggetto non impostato durante l'inizializzazione del campo titolo: " & ex.Message)
            msgBox.ShowDialog()
            _logger.ScriviLog("NullReferenceException in modificatitolo: " & ex.Message & " StackTrace: " & ex.StackTrace)
        Catch ex As Exception
            Dim msgBox As New WGMessaggi()
            msgBox.Messaggio("Si è verificato un errore inatteso durante la modifica del titolo: " & ex.Message)
            msgBox.ShowDialog()
            _logger.ScriviLog("Exception in modificatitolo: " & ex.Message & " StackTrace: " & ex.StackTrace)
        End Try
    End Sub

    Private Sub modificatitolonota()
        Try
            Dim titolonotanuovo As String = titolocampo.ottienitesto()
            titolocampo.chiudi()
            If titolonotanuovo = " " Then
                Dim messaggiotitolo As New WGMessaggi()
                messaggiotitolo.Messaggio("Il nuovo titolo della nota risulta vuoto, pertanto non è stato modificato")
                messaggiotitolo.ShowDialog()
            Else
                Dim risultato As Boolean
                risultato = note.ModificaTitoloNota(CInt(idnota), titolonotanuovo)
                If risultato = True Then
                    Dim messaggio As New WGMessaggi()
                    messaggio.Messaggio("Nota aggiornata")
                    messaggio.ShowDialog()
                    AvviaCaricamento()
                Else
                    Dim messaggio As New WGMessaggi()
                    messaggio.Messaggio("Il titolo non è stato modificato, verificare i log nella cartella WinGabriel in documenti")
                    messaggio.ShowDialog()
                End If
            End If
        Catch ex As FormatException
            Dim msgBox As New WGMessaggi()
            msgBox.Messaggio("Errore di formato dell'ID nota: " & ex.Message)
            msgBox.ShowDialog()
            _logger.ScriviLog("FormatException in modificatitolonota (conversione ID): " & ex.Message & " ID Nota: " & idnota & " StackTrace: " & ex.StackTrace)
        Catch ex As NullReferenceException
            Dim msgBox As New WGMessaggi()
            msgBox.Messaggio("Errore: Riferimento a oggetto non impostato durante la modifica del titolo della nota: " & ex.Message)
            msgBox.ShowDialog()
            _logger.ScriviLog("NullReferenceException in modificatitolonota: " & ex.Message & " StackTrace: " & ex.StackTrace)
        Catch ex As Exception
            Dim msgBox As New WGMessaggi()
            msgBox.Messaggio("Si è verificato un errore inatteso durante la modifica del titolo della nota: " & ex.Message)
            msgBox.ShowDialog()
            _logger.ScriviLog("Exception in modificatitolonota: " & ex.Message & " StackTrace: " & ex.StackTrace)
        End Try
    End Sub

    Private Sub modificatesto()
        Try
            Dim idRecuperato As String = RecuperaIdSelezionato()
            If idRecuperato = String.Empty Then
                Dim msg As New WGMessaggi()
                msg.Messaggio("Seleziona una nota prima di premere Modifica Testo.")
                msg.ShowDialog()
                Exit Sub
            End If

            idnota = idRecuperato
            id = idnota

            If note.NotaProtetta(CInt(idnota)) = True Then
                passwordcampi = New Campi()
                passwordcampi.AggiungiPassword("Inserisci la password per aprire la nota", "c.notapassword", True)
                AddHandler passwordcampi.fine, AddressOf modificanotapassword
                passwordcampi.avvia()
            Else
                Dim notaInfo As Nota = note.RecuperaNota(CInt(idnota))
                If notaInfo IsNot Nothing AndAlso notaInfo.Formato = FormatoNota.Html Then
                    AvviaModificaNotaWord(CInt(idnota), "")
                Else
                    testocampo = New WGCampo()
                    AddHandler testocampo.chiusura, AddressOf modificatestonota
                    testocampo.init("Modifica testo nota", note.VisualizzaTestoNota(CInt(idnota)), True)
                    testocampo.aggiungiopzione("Abilita protezione nota", AddressOf abilitaprotezionenota)
                    testocampo.avvia()
                End If
            End If
        Catch ex As FormatException
            Dim msgBox As New WGMessaggi()
            msgBox.Messaggio("Errore di formato dell'ID nota: " & ex.Message)
                MsgBox.ShowDialog()
            _logger.ScriviLog("FormatException in modificatesto (conversione ID): " & ex.Message & " ID Nota: " & idnota & " StackTrace: " & ex.StackTrace)
        Catch ex As NullReferenceException
            Dim msgBox As New WGMessaggi()
            msgBox.Messaggio("Errore: Riferimento a oggetto non impostato durante la preparazione della modifica del testo: " & ex.Message)
            msgBox.ShowDialog()
            _logger.ScriviLog("NullReferenceException in modificatesto: " & ex.Message & " StackTrace: " & ex.StackTrace)
        Catch ex As Exception
            Dim msgBox As New WGMessaggi()
            msgBox.Messaggio("Si è verificato un errore inatteso durante la modifica del testo: " & ex.Message)
            msgBox.ShowDialog()
            _logger.ScriviLog("Exception in modificatesto: " & ex.Message & " StackTrace: " & ex.StackTrace)
        End Try
    End Sub

    Private Sub modificanotapassword()
        Try
            password = passwordcampi.RecuperaValore("c.notapassword")
            passwordcampi = Nothing
            If password = " " Then
                Dim messaggiopassword As New WGMessaggi()
                messaggiopassword.Messaggio("Il campo della password risulta vuoto, pertanto non è possibile modificare il testo della nota selezionata in quanto protetta da password")
                messaggiopassword.ShowDialog()
            Else
                Dim notaInfo As Nota = note.RecuperaNota(CInt(id))
                If notaInfo IsNot Nothing AndAlso notaInfo.Formato = FormatoNota.Html Then
                    AvviaModificaNotaWord(CInt(id), password)
                Else
                    testocampo = New WGCampo()
                    AddHandler testocampo.chiusura, AddressOf modificatestonota
                    testocampo.aggiungiopzione("Disabilita protezione nota", AddressOf togliprotezione)
                    testocampo.init("Modifica testo nota", note.VisualizzaTestoNota(CInt(id), password), True)
                    testocampo.avvia()
                End If
            End If
        Catch ex As CryptographicException
            Dim msgBox As New WGMessaggi()
            msgBox.Messaggio("Errore di crittografia durante la modifica della nota protetta: " & ex.Message)
            msgBox.ShowDialog()
            _logger.ScriviLog("CryptographicException in modificanotapassword: " & ex.Message & " StackTrace: " & ex.StackTrace)
        Catch ex As NullReferenceException
            Dim msgBox As New WGMessaggi()
            msgBox.Messaggio("Errore: Riferimento a oggetto non impostato durante la gestione della password della nota: " & ex.Message)
            msgBox.ShowDialog()
            _logger.ScriviLog("NullReferenceException in modificanotapassword: " & ex.Message & " StackTrace: " & ex.StackTrace)
        Catch ex As FormatException
            Dim msgBox As New WGMessaggi()
            msgBox.Messaggio("Errore di formato dell'ID nota durante la modifica di una nota protetta: " & ex.Message)
            msgBox.ShowDialog()
            _logger.ScriviLog("FormatException in modificanotapassword: " & ex.Message & " ID: " & id & " StackTrace: " & ex.StackTrace)
        Catch ex As Exception
            Dim msgBox As New WGMessaggi()
            msgBox.Messaggio("Si è verificato un errore inatteso durante la gestione della password per la modifica della nota: " & ex.Message)
            msgBox.ShowDialog()
            _logger.ScriviLog("Exception in modificanotapassword: " & ex.Message & " StackTrace: " & ex.StackTrace)
        End Try
    End Sub

    Private Sub togliprotezione()
        Try
            domanda = New WGDomanda()
            AddHandler domanda.rispostadomanda, AddressOf scelta
            domanda.avvia("Si vuole disabilitare la protezione della nota selezionata?")
        Catch ex As NullReferenceException
            Dim msgBox As New WGMessaggi()
            msgBox.Messaggio("Errore: Riferimento a oggetto non impostato durante la richiesta di rimozione protezione: " & ex.Message)
            msgBox.ShowDialog()
            _logger.ScriviLog("NullReferenceException in togliprotezione: " & ex.Message & " StackTrace: " & ex.StackTrace)
        Catch ex As Exception
            Dim msgBox As New WGMessaggi()
            msgBox.Messaggio("Si è verificato un errore inatteso durante la richiesta di rimozione protezione: " & ex.Message)
            msgBox.ShowDialog()
            _logger.ScriviLog("Exception in togliprotezione: " & ex.Message & " StackTrace: " & ex.StackTrace)
        End Try
    End Sub

    Private Sub scelta(rispostadom As DialogResult)
        Try
            domanda = Nothing
            If rispostadom = DialogResult.Yes Then
                passwordcampi = New Campi()
                passwordcampi.AggiungiPassword("inserisci password nota", "c.notapassword", True)
                AddHandler passwordcampi.Fine, AddressOf togliprotezionepassword
                passwordcampi.avvia()
            Else
                Exit Sub
            End If
        Catch ex As NullReferenceException
            Dim msgBox As New WGMessaggi()
            msgBox.Messaggio("Errore: Riferimento a oggetto non impostato durante la scelta della protezione: " & ex.Message)
            msgBox.ShowDialog()
            _logger.ScriviLog("NullReferenceException in scelta: " & ex.Message & " StackTrace: " & ex.StackTrace)
        Catch ex As Exception
            Dim msgBox As New WGMessaggi()
            msgBox.Messaggio("Si è verificato un errore inatteso durante la gestione della scelta di protezione: " & ex.Message)
            msgBox.ShowDialog()
            _logger.ScriviLog("Exception in scelta: " & ex.Message & " StackTrace: " & ex.StackTrace)
        End Try
    End Sub

    Private Sub togliprotezionepassword()
        Try
            password = passwordcampi.RecuperaValore("c.notapassword")
            passwordcampi = Nothing
            If password = " " Then
                Dim messaggiopassword As New WGMessaggi()
                messaggiopassword.Messaggio("Non è stata inserita una password valida per la decriptazione della nota, pertanto la protezione non è stata eliminata")
                messaggiopassword.ShowDialog()
            Else
                Dim risultato As Boolean
                risultato = note.RimuoviProtezioneNota(CInt(id), password)
                If risultato = True Then
                    Dim messaggiotrue As New WGMessaggi()
                    messaggiotrue.Messaggio("Protezione eliminata")
                    messaggiotrue.ShowDialog()
                Else
                    Dim messaggiofalse As New WGMessaggi()
                    messaggiofalse.Messaggio("Non è stato possibile eliminare la protezione dalla nota")
                    messaggiofalse.ShowDialog()
                End If
            End If
        Catch ex As CryptographicException
            Dim msgBox As New WGMessaggi()
            msgBox.Messaggio("Errore di crittografia durante la rimozione della protezione: " & ex.Message)
            msgBox.ShowDialog()
            _logger.ScriviLog("CryptographicException in togliprotezionepassword: " & ex.Message & " StackTrace: " & ex.StackTrace)
        Catch ex As FormatException
            Dim msgBox As New WGMessaggi()
            msgBox.Messaggio("Errore di formato dell'ID nota durante la rimozione della protezione: " & ex.Message)
            msgBox.ShowDialog()
            _logger.ScriviLog("FormatException in togliprotezionepassword: " & ex.Message & " ID: " & id & " StackTrace: " & ex.StackTrace)
        Catch ex As NullReferenceException
            Dim msgBox As New WGMessaggi()
            msgBox.Messaggio("Errore: Riferimento a oggetto non impostato durante la gestione della password di rimozione protezione: " & ex.Message)
            msgBox.ShowDialog()
            _logger.ScriviLog("NullReferenceException in togliprotezionepassword: " & ex.Message & " StackTrace: " & ex.StackTrace)
        Catch ex As Exception
            Dim msgBox As New WGMessaggi()
            msgBox.Messaggio("Si è verificato un errore inatteso durante la gestione della password di rimozione protezione: " & ex.Message)
            msgBox.ShowDialog()
            _logger.ScriviLog("Exception in togliprotezionepassword: " & ex.Message & " StackTrace: " & ex.StackTrace)
        End Try
    End Sub

    Private Sub modificatestonota()
        Try
            testo = testocampo.ottienitesto()
            If testo = " " Then
                Dim messaggio As New WGMessaggi()
                messaggio.Messaggio("Il testo inserito risulta vuoto, pertanto la modifica della nota è stata annullata")
                messaggio.ShowDialog()
                testocampo.chiudi()
                Exit Sub
            Else
                testocampo.chiudi()
                Dim risultato As Boolean

                If note.NotaProtetta(CInt(id)) = True Then
                    risultato = note.ModificaTestoNota(CInt(idnota), testo, password)
                Else
                    risultato = note.ModificaTestoNota(CInt(idnota), testo)
                End If

                If risultato = True Then
                    Dim messaggio As New WGMessaggi()
                    messaggio.Messaggio("Nota aggiornata")
                    messaggio.ShowDialog()
                Else
                    Dim messaggio As New WGMessaggi()
                    messaggio.Messaggio("Non è stato possibile aggiornare il testo della nota, verificare i log nella cartella WinGabriel in documenti")
                    messaggio.ShowDialog()
                End If
            End If
        Catch ex As CryptographicException
            Dim msgBox As New WGMessaggi()
            msgBox.Messaggio("Errore di crittografia durante l'aggiornamento del testo della nota: " & ex.Message)
            msgBox.ShowDialog()
            _logger.ScriviLog("CryptographicException in modificatestonota: " & ex.Message & " StackTrace: " & ex.StackTrace)
        Catch ex As FormatException
            Dim msgBox As New WGMessaggi()
            msgBox.Messaggio("Errore di formato dell'ID nota durante la modifica del testo: " & ex.Message)
            msgBox.ShowDialog()
            _logger.ScriviLog("FormatException in modificatestonota: " & ex.Message & " ID: " & id & " StackTrace: " & ex.StackTrace)
        Catch ex As NullReferenceException
            Dim msgBox As New WGMessaggi()
            msgBox.Messaggio("Errore: Riferimento a oggetto non impostato durante la modifica del testo della nota: " & ex.Message)
            msgBox.ShowDialog()
            _logger.ScriviLog("NullReferenceException in modificatestonota: " & ex.Message & " StackTrace: " & ex.StackTrace)
        Catch ex As ArgumentNullException
            Dim msgBox As New WGMessaggi()
            msgBox.Messaggio("Errore: Il testo della nota non può essere nullo: " & ex.Message)
            msgBox.ShowDialog()
            _logger.ScriviLog("ArgumentNullException in modificatestonota: " & ex.Message & " StackTrace: " & ex.StackTrace)
        Catch ex As Exception
            Dim msgBox As New WGMessaggi()
            msgBox.Messaggio("Si è verificato un errore inatteso durante la modifica del testo della nota: " & ex.Message)
            msgBox.ShowDialog()
            _logger.ScriviLog("Exception in modificatestonota: " & ex.Message & " StackTrace: " & ex.StackTrace)
        End Try
    End Sub

    Private Sub eliminanota()
        Try
            Dim idRecuperato As String = RecuperaIdSelezionato()
            If idRecuperato = String.Empty Then
                Dim msg As New WGMessaggi()
                msg.Messaggio("Seleziona una nota prima di premere Elimina.")
                msg.ShowDialog()
                Exit Sub
            End If

            idnota = idRecuperato
            id = idnota

            If String.IsNullOrEmpty(idnota) Then
                Dim messaggio As New WGMessaggi()
                messaggio.Messaggio("Nessun ID nota recuperato. Selezionare una nota dalla lista.")
                messaggio.ShowDialog()
                Return
            End If

            Dim domanda As New WGDomanda()
            AddHandler domanda.rispostadomanda, AddressOf rispostaelimina
            domanda.avvia("Si vuole eliminare veramente la nota selezionata?")
        Catch ex As NullReferenceException
            Dim msgBox As New WGMessaggi()
            msgBox.Messaggio("Errore: Riferimento a oggetto non impostato durante la richiesta di eliminazione nota: " & ex.Message)
            msgBox.ShowDialog()
            _logger.ScriviLog("NullReferenceException in eliminanota: " & ex.Message & " StackTrace: " & ex.StackTrace)
        Catch ex As Exception
            Dim msgBox As New WGMessaggi()
            msgBox.Messaggio("Si è verificato un errore inatteso durante la richiesta di eliminazione nota: " & ex.Message)
            msgBox.ShowDialog()
            _logger.ScriviLog("Exception in eliminanota: " & ex.Message & " StackTrace: " & ex.StackTrace)
        End Try
    End Sub

    Private Sub rispostaelimina(risposta As DialogResult)
        Try
            If risposta = DialogResult.Yes Then
                Dim risultato As Boolean
                risultato = note.EliminaNota(CInt(idnota))
                If risultato = True Then
                    Dim messaggio As New WGMessaggi()
                    messaggio.Messaggio("Nota eliminata")
                    messaggio.ShowDialog()
                    AvviaCaricamento()
                Else
                    Dim messaggio As New WGMessaggi()
                    messaggio.Messaggio("Non è stato possibile eliminare la nota, verificare i log nella cartella WinGabriel in documenti")
                    messaggio.ShowDialog()
                End If
            Else
                Exit Sub
            End If
        Catch ex As FormatException
            Dim msgBox As New WGMessaggi()
            msgBox.Messaggio("Errore di formato dell'ID nota durante l'eliminazione: " & ex.Message)
            msgBox.ShowDialog()
            _logger.ScriviLog("FormatException in rispostaelimina (conversione ID): " & ex.Message & " ID Nota: " & idnota & " StackTrace: " & ex.StackTrace)
        Catch ex As NullReferenceException
            Dim msgBox As New WGMessaggi()
            msgBox.Messaggio("Errore: Riferimento a oggetto non impostato durante la gestione della risposta di eliminazione: " & ex.Message)
            msgBox.ShowDialog()
            _logger.ScriviLog("NullReferenceException in rispostaelimina: " & ex.Message & " StackTrace: " & ex.StackTrace)
        Catch ex As Exception
            Dim msgBox As New WGMessaggi()
            msgBox.Messaggio("Si è verificato un errore inatteso durante la gestione della risposta di eliminazione: " & ex.Message)
            msgBox.ShowDialog()
            _logger.ScriviLog("Exception in rispostaelimina: " & ex.Message & " StackTrace: " & ex.StackTrace)
        End Try
    End Sub

    ''' <summary>notaselezionata con la firma esatta dell'evento: così AddHandler e RemoveHandler agganciano e staccano lo stesso gestore.</summary>
    Private Sub notaselezionata(idEvento As String)
        notaselezionata()
    End Sub

    Private Sub notaselezionata()
        Try
            Dim idRecuperato As String = RecuperaIdSelezionato()
            If idRecuperato = String.Empty Then
                Exit Sub
            End If

            idnota = idRecuperato
            id = idnota

            If String.IsNullOrEmpty(idnota) Then
                Dim messaggio As New WGMessaggi()
                messaggio.Messaggio("Nessun ID nota recuperato. Selezionare una nota dalla lista.")
                messaggio.ShowDialog()
                Return
            End If

            If note.NotaProtetta(CInt(id)) = True Then
                passwordcampi = New Campi()
                passwordcampi.AggiungiPassword("Inserisci la password per aprire la nota", "c.notapassword", True)
                AddHandler passwordcampi.Fine, AddressOf inserimentopasswordaperturanota
                passwordcampi.avvia()
            Else
                Dim nota As Nota = note.RecuperaNota(CInt(idnota))

                If nota Is Nothing Then
                    Dim messaggio2 As New WGMessaggi()
                    messaggio2.Messaggio("Non è stato possibile recuperare la nota selezionata, verificare i log nella cartella WinGabriel in documenti per maggiori dettagli")
                    messaggio2.ShowDialog()
                    Return
                End If

                ApriNotaInLettura(nota)
            End If
        Catch ex As FormatException
            Dim msgBox As New WGMessaggi()
            msgBox.Messaggio("Errore di formato dell'ID nota durante la selezione: " & ex.Message)
            msgBox.ShowDialog()
            _logger.ScriviLog("FormatException in notaselezionata (conversione ID): " & ex.Message & " ID Nota: " & idnota & " StackTrace: " & ex.StackTrace)
        Catch ex As NullReferenceException
            Dim msgBox As New WGMessaggi()
            msgBox.Messaggio("Errore: Riferimento a oggetto non impostato durante la selezione della nota: " & ex.Message)
            msgBox.ShowDialog()
            _logger.ScriviLog("NullReferenceException in notaselezionata: " & ex.Message & " StackTrace: " & ex.StackTrace)
        Catch ex As Exception
            Dim msgBox As New WGMessaggi()
            msgBox.Messaggio("Si è verificato un errore inatteso durante la selezione della nota: " & ex.Message)
            msgBox.ShowDialog()
            _logger.ScriviLog("Exception in notaselezionata: " & ex.Message & " StackTrace: " & ex.StackTrace)
        End Try
    End Sub

    Private Sub abilitaprotezionenota()
        Try
            passwordcampi = New Campi()
            passwordcampi.AggiungiPassword("Inserisci nuova password nota", "c.notapassword", True)
            AddHandler passwordcampi.Fine, AddressOf abilitaprotezione
            passwordcampi.avvia()
        Catch ex As NullReferenceException
            Dim msgBox As New WGMessaggi()
            msgBox.Messaggio("Errore: Riferimento a oggetto non impostato durante la richiesta di abilitazione protezione: " & ex.Message)
            msgBox.ShowDialog()
            _logger.ScriviLog("NullReferenceException in abilitaprotezionenota: " & ex.Message & " StackTrace: " & ex.StackTrace)
        Catch ex As Exception
            Dim msgBox As New WGMessaggi()
            msgBox.Messaggio("Si è verificato un errore inatteso durante la richiesta di abilitazione protezione: " & ex.Message)
            msgBox.ShowDialog()
            _logger.ScriviLog("Exception in abilitaprotezionenota: " & ex.Message & " StackTrace: " & ex.StackTrace)
        End Try
    End Sub

    Private Sub abilitaprotezione()
        Try
            password = passwordcampi.RecuperaValore("c.notapassword")
            passwordcampi = Nothing
            If password = " " Then
                Dim messaggiopassword As New WGMessaggi()
                messaggiopassword.Messaggio("Non è stata inserita alcuna password, pertanto la nota non è stata protetta")
                messaggiopassword.ShowDialog()
            Else
                Dim risultato As Boolean
                risultato = note.ProteggiNota(CInt(id), password)
                If risultato = True Then
                    Dim messaggiotrue As New WGMessaggi()
                    messaggiotrue.Messaggio("Nota protetta")
                    messaggiotrue.ShowDialog()
                Else
                    Dim messaggiofalse As New WGMessaggi()
                    messaggiofalse.Messaggio("Non è stato possibile modificare lo stato di protezione della nota")
                    messaggiofalse.ShowDialog()
                End If
            End If
        Catch ex As CryptographicException
            Dim msgBox As New WGMessaggi()
            msgBox.Messaggio("Errore di crittografia durante l'abilitazione della protezione: " & ex.Message)
            msgBox.ShowDialog()
            _logger.ScriviLog("CryptographicException in abilitaprotezione: " & ex.Message & " StackTrace: " & ex.StackTrace)
        Catch ex As FormatException
            Dim msgBox As New WGMessaggi()
            msgBox.Messaggio("Errore di formato dell'ID nota durante l'abilitazione della protezione: " & ex.Message)
            msgBox.ShowDialog()
            _logger.ScriviLog("FormatException in abilitaprotezione: " & ex.Message & " ID: " & id & " StackTrace: " & ex.StackTrace)
        Catch ex As NullReferenceException
            Dim msgBox As New WGMessaggi()
            msgBox.Messaggio("Errore: Riferimento a oggetto non impostato durante la gestione della password di abilitazione: " & ex.Message)
            msgBox.ShowDialog()
            _logger.ScriviLog("NullReferenceException in abilitaprotezione: " & ex.Message & " StackTrace: " & ex.StackTrace)
        Catch ex As Exception
            Dim msgBox As New WGMessaggi()
            msgBox.Messaggio("Si è verificato un errore inatteso durante l'abilitazione della protezione: " & ex.Message)
            msgBox.ShowDialog()
            _logger.ScriviLog("Exception in abilitaprotezione: " & ex.Message & " StackTrace: " & ex.StackTrace)
        End Try
    End Sub

    Private Sub inserimentopasswordaperturanota()
        Try
            password = passwordcampi.RecuperaValore("c.notapassword")
            passwordcampi = Nothing
            If password = " " Then
                Dim messaggiopassword As New WGMessaggi()
                messaggiopassword.Messaggio("Non è stata inserita alcuna password, pertanto non è possibile aprire la nota selezionata in quanto protetta")
                messaggiopassword.ShowDialog()
            Else
                Dim nota As Nota = note.RecuperaNota(CInt(idnota), password)

                If nota Is Nothing Then
                    Dim messaggio2 As New WGMessaggi()
                    messaggio2.Messaggio("Non è stato possibile recuperare la nota selezionata o la password non è corretta, verificare i log nella cartella WinGabriel in documenti per maggiori dettagli")
                    messaggio2.ShowDialog()
                    Return
                End If

                ApriNotaInLettura(nota)
            End If
        Catch ex As CryptographicException
            Dim msgBox As New WGMessaggi()
            msgBox.Messaggio("Errore di crittografia durante l'apertura della nota: " & ex.Message)
            msgBox.ShowDialog()
            _logger.ScriviLog("CryptographicException in inserimentopasswordaperturanota: " & ex.Message & " StackTrace: " & ex.StackTrace)
        Catch ex As FormatException
            Dim msgBox As New WGMessaggi()
            msgBox.Messaggio("Errore di formato dell'ID nota durante l'apertura della nota: " & ex.Message)
            msgBox.ShowDialog()
            _logger.ScriviLog("FormatException in inserimentopasswordaperturanota: " & ex.Message & " ID Nota: " & idnota & " StackTrace: " & ex.StackTrace)
        Catch ex As NullReferenceException
            Dim msgBox As New WGMessaggi()
            msgBox.Messaggio("Errore: Riferimento a oggetto non impostato durante l'inserimento della password di apertura: " & ex.Message)
            msgBox.ShowDialog()
            _logger.ScriviLog("NullReferenceException in inserimentopasswordaperturanota: " & ex.Message & " StackTrace: " & ex.StackTrace)
        Catch ex As Exception
            Dim msgBox As New WGMessaggi()
            msgBox.Messaggio("Si è verificato un errore inatteso durante l'apertura della nota protetta: " & ex.Message)
            msgBox.ShowDialog()
            _logger.ScriviLog("Exception in inserimentopasswordaperturanota: " & ex.Message & " StackTrace: " & ex.StackTrace)
        End Try
    End Sub

    ''' <summary>chiudinote con la firma esatta dell'evento: così AddHandler e RemoveHandler agganciano e staccano lo stesso gestore.</summary>
    Private Sub chiudinote(senderEvento As Object, eEvento As EventArgs)
        chiudinote()
    End Sub

    Private Sub chiudinote()
        Try
            If listanote IsNot Nothing Then
                Try
                    RemoveHandler listanote.ElementoSelezionato, AddressOf notaselezionata
                    RemoveHandler listanote.escPremuto, AddressOf chiudinote
                Catch exHandler As Exception
                    _logger.ScriviLog("Errore rimozione gestori in chiudinote: " & exHandler.Message)
                End Try
                listanote.abbandona()
                listanote = Nothing
            End If
        Catch ex As Exception
            Dim msgBox As New WGMessaggi()
            msgBox.Messaggio("Si è verificato un errore inatteso durante la chiusura delle note: " & ex.Message)
            msgBox.ShowDialog()
            _logger.ScriviLog("Exception in chiudinote: " & ex.Message & " StackTrace: " & ex.StackTrace)
        End Try
    End Sub

    Private Function DataValorizzata(ByVal valoreData As Date?) As Boolean
        Return valoreData.HasValue AndAlso valoreData.Value <> Date.MinValue
    End Function

    Private Function DataLeggibile(ByVal valoreData As Date?) As String
        Try
            If Not DataValorizzata(valoreData) Then
                Return "Data non valorizzata"
            End If
            Dim giornoSettimana As String = valoreData.Value.ToString("dddd", New Globalization.CultureInfo("it-IT"))
            Dim dataFormattata As String = valoreData.Value.ToString("dd MMMM yyyy", New Globalization.CultureInfo("it-IT"))
            Return $"{giornoSettimana} {dataFormattata}"
        Catch ex As Exception
            _logger.ScriviLog("Exception in DataLeggibile: " & ex.Message & " Valore Data: " & valoreData.ToString() & " StackTrace: " & ex.StackTrace)
            Return "Errore data"
        End Try
    End Function

    Private Function StringaAData(ByVal stringaData As String) As Date
        Dim dataConvertita As Date
        If Date.TryParse(stringaData, dataConvertita) Then
            Return dataConvertita
        Else
            Throw New FormatException("La stringa fornita non è in un formato di data valido.")
        End If
    End Function
End Class