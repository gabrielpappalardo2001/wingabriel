Imports System.Collections.Generic
Imports System.Globalization

' Le impostazioni del lettore di schermo. Stanno nella configurazione di WinGabriel (WGConfig, chiavi che
' cominciano per lettore_schermo_): i due programmi, WinGabriel e il lettore, leggono e scrivono le stesse.
' Nessun file di testo a parte.
Friend NotInheritable Class ImpostazioniLettore

    Private Const Prefisso As String = "lettore_schermo_"

    Private ReadOnly _blocco As New Object()
    Private ReadOnly _gesti As New Dictionary(Of String, ComandoLettore)(StringComparer.OrdinalIgnoreCase)
    Private ReadOnly _comandiRimappati As New HashSet(Of ComandoLettore)()

    Public Property LivelloPunteggiatura As Integer = -1
    Public Property Numeri As String = "eredita"
    Public Property Maiuscole As String = "parla"
    Public Property Prolissita As Integer = 1
    Public Property Layout As String = "portatile"
    Public Property OcrAutomatico As Boolean = True
    Public Property Suoni As Boolean = True
    Public Property BrailleAttivo As Boolean = False
    Public Property PortaBraille As String = ""
    Public Property TracciamentoMouse As Boolean = False
    Public Property AnnunciaFormato As Boolean = False
    Public Property AiutoTasti As Boolean = False
    Public Property Rientri As Boolean = True

    Public Sub Carica()
        Try
            LivelloPunteggiatura = LeggiIntero("punteggiatura", -1)
            If LivelloPunteggiatura < -1 Then LivelloPunteggiatura = -1
            If LivelloPunteggiatura > 3 Then LivelloPunteggiatura = 3
            Numeri = Normalizza(Leggi("numeri", "eredita"), "eredita", "intero", "cifra", "coppie", "triplette")
            Maiuscole = Normalizza(Leggi("maiuscole", "parla"), "nessuna", "parla", "beep", "tutto")
            Prolissita = LeggiIntero("prolissita", 1)
            If Prolissita < 0 Then Prolissita = 0
            If Prolissita > 2 Then Prolissita = 2
            Layout = Normalizza(Leggi("layout", "portatile"), "portatile", "desktop")
            OcrAutomatico = LeggiBool("ocr", True)
            Suoni = LeggiBool("suoni", True)
            BrailleAttivo = LeggiBool("braille", False)
            PortaBraille = Leggi("portaBraille", "")
            TracciamentoMouse = LeggiBool("tracciamentoMouse", False)
            AnnunciaFormato = LeggiBool("annunciaFormato", False)
            Rientri = LeggiBool("rientri", True)
            CaricaRimappa()
        Catch ex As Exception
            RegistroErrori.Scrivi("ImpostazioniLettore.Carica", ex)
        End Try
    End Sub

    Public Sub Salva()
        Try
            Scrivi("punteggiatura", LivelloPunteggiatura.ToString(CultureInfo.InvariantCulture))
            Scrivi("numeri", Numeri)
            Scrivi("maiuscole", Maiuscole)
            Scrivi("prolissita", Prolissita.ToString(CultureInfo.InvariantCulture))
            Scrivi("layout", Layout)
            Scrivi("ocr", If(OcrAutomatico, "1", "0"))
            Scrivi("suoni", If(Suoni, "1", "0"))
            Scrivi("braille", If(BrailleAttivo, "1", "0"))
            Scrivi("portaBraille", PortaBraille)
            Scrivi("tracciamentoMouse", If(TracciamentoMouse, "1", "0"))
            Scrivi("annunciaFormato", If(AnnunciaFormato, "1", "0"))
            Scrivi("rientri", If(Rientri, "1", "0"))
        Catch ex As Exception
            RegistroErrori.Scrivi("ImpostazioniLettore.Salva", ex)
        End Try
    End Sub

    Public Sub ImpostaRimappa(comando As ComandoLettore, gesto As String)
        Try
            Dim chiave As String = "rimappa_" & comando.ToString().ToLowerInvariant()
            If String.IsNullOrWhiteSpace(gesto) Then
                ConfigLettore.Elimina(Prefisso & chiave)
            Else
                Scrivi(chiave, gesto.Trim().ToLowerInvariant())
            End If
            CaricaRimappa()
        Catch ex As Exception
            RegistroErrori.Scrivi("ImpostazioniLettore.ImpostaRimappa", ex)
        End Try
    End Sub

    Public Function ProvaGesto(gesto As String, ByRef comando As ComandoLettore) As Boolean
        Try
            If String.IsNullOrEmpty(gesto) Then Return False
            SyncLock _blocco
                Return _gesti.TryGetValue(gesto, comando)
            End SyncLock
        Catch exAtteso As Exception
            RegistroErrori.Scrivi("ImpostazioniLettore.ProvaGesto", exAtteso)
            Return False
        End Try
    End Function

    Public Function ComandoHaRimappa(comando As ComandoLettore) As Boolean
        SyncLock _blocco
            Return _comandiRimappati.Contains(comando)
        End SyncLock
    End Function

    Public Shared Function Descrizione(comando As ComandoLettore) As String
        Select Case comando
            Case ComandoLettore.Titolo : Return "legge il titolo della finestra"
            Case ComandoLettore.Focus : Return "legge l'elemento con il fuoco"
            Case ComandoLettore.Ora : Return "dice l'ora"
            Case ComandoLettore.Data : Return "dice la data"
            Case ComandoLettore.BarraStato : Return "legge la barra di stato"
            Case ComandoLettore.Riga : Return "legge la riga corrente"
            Case ComandoLettore.LetturaContinua : Return "legge da qui fino in fondo. Di nuovo: pausa o ripresa"
            Case ComandoLettore.Finestra : Return "legge il testo della finestra"
            Case ComandoLettore.AttivaDisattiva : Return "sospende o riattiva il lettore"
            Case ComandoLettore.Selezione : Return "legge il testo selezionato"
            Case ComandoLettore.Precedente : Return "elemento precedente, nella revisione"
            Case ComandoLettore.Successivo : Return "elemento successivo, nella revisione"
            Case ComandoLettore.Contenitore : Return "sale al contenitore"
            Case ComandoLettore.Contenuto : Return "entra nel contenuto"
            Case ComandoLettore.NavigazioneWeb : Return "entra o esce dalla navigazione"
            Case ComandoLettore.AttivaElemento : Return "attiva l'elemento corrente"
            Case ComandoLettore.Aiuto : Return "legge l'elenco dei comandi"
            Case ComandoLettore.AggiornaNavigazione : Return "aggiorna l'albero di accessibilità"
            Case ComandoLettore.Descrizione : Return "legge la descrizione"
            Case ComandoLettore.Compita : Return "compita la parola"
            Case ComandoLettore.Parola : Return "legge la parola"
            Case ComandoLettore.Carattere : Return "legge il carattere"
            Case ComandoLettore.EsciScrittura : Return "esce dalla scrittura e torna alla navigazione"
            Case ComandoLettore.StatoMaiuscole : Return "dice se il bloc maiuscole è attivo"
            Case ComandoLettore.Messaggio : Return "legge il messaggio o il paragrafo"
            Case ComandoLettore.RigaTabella : Return "legge la riga della tabella"
            Case ComandoLettore.ColonnaTabella : Return "legge la colonna della tabella"
            Case ComandoLettore.AiutoTasti : Return "aiuto tasti: dice che cosa fa un tasto, senza eseguirlo"
            Case ComandoLettore.Formattazione : Return "legge il formato del testo o della cella: carattere, colori, allineamento, errori, collegamento, formula"
            Case ComandoLettore.PausaLettura : Return "mette in pausa o riprende la lettura continua"
            Case ComandoLettore.TracciaMouse : Return "attiva o disattiva la lettura sotto il puntatore"
            Case ComandoLettore.PortaMouse : Return "sposta il puntatore sull'elemento in revisione"
            Case ComandoLettore.Clicca : Return "fa clic sull'elemento in revisione"
            Case ComandoLettore.ElencoElementi : Return "elenco degli elementi della pagina: link, intestazioni, campi, pulsanti e regioni; Invio ci va, Control Invio li attiva"
            Case ComandoLettore.CercaPagina : Return "cerca nella pagina: apre un campo di testo, si scrive e si conferma con Invio"
            Case ComandoLettore.ScaricaAlbero : Return "scrive su file l'albero dell'elemento con il fuoco"
            Case ComandoLettore.Prolissita : Return "cambia il livello di prolissità"
            Case ComandoLettore.Braille : Return "attiva o disattiva l'uscita Braille"
            Case ComandoLettore.RevisionePiatta : Return "revisione piatta del testo, oppure ad albero"
            Case ComandoLettore.Ocr : Return "legge con il riconoscimento ottico l'elemento sotto il fuoco"
            Case ComandoLettore.LettereRapide : Return "attiva o disattiva la navigazione con le lettere nelle pagine"
            Case ComandoLettore.MenuLettore : Return "apre il menu del lettore di schermo: impostazioni e uscita"
            Case ComandoLettore.RipetiUltimo : Return "ripete l'ultima frase detta dal lettore"
            Case ComandoLettore.StoricoVoce : Return "legge la frase detta prima, a ogni pressione una più indietro"
            Case ComandoLettore.CopiaUltimo : Return "copia negli appunti l'ultima frase detta"
            Case ComandoLettore.Appunti : Return "legge il testo che c'è negli appunti"
            Case ComandoLettore.SalvaProfilo : Return "salva prolissità ed echi attuali come profilo del programma in primo piano"
            Case ComandoLettore.TogliProfilo : Return "toglie il profilo del programma in primo piano"
            Case Else : Return "comando del lettore"
        End Select
    End Function

    Public Shared Function NomeTasto(codice As Integer) As String
        Select Case codice
            Case &H8 : Return "backspace"
            Case &H9 : Return "tabulatore"
            Case &HD : Return "invio"
            Case &H1B : Return "esc"
            Case &H20 : Return "spazio"
            Case &H21 : Return "pagina su"
            Case &H22 : Return "pagina giù"
            Case &H23 : Return "fine"
            Case &H24 : Return "inizio"
            Case &H25 : Return "freccia sinistra"
            Case &H26 : Return "freccia su"
            Case &H27 : Return "freccia destra"
            Case &H28 : Return "freccia giù"
            Case &H2E : Return "canc"
            Case &H70 To &H7B : Return "f" & (codice - &H6F).ToString()
            Case &H30 To &H39 : Return ChrW(codice).ToString()
            Case &H41 To &H5A : Return ChrW(codice).ToString().ToLowerInvariant()
            Case &H60 To &H69 : Return "tastierino " & (codice - &H60).ToString()
            Case Else : Return "tasto " & codice.ToString()
        End Select
    End Function

    Private Sub CaricaRimappa()
        SyncLock _blocco
            _gesti.Clear()
            _comandiRimappati.Clear()
            For Each comando As ComandoLettore In [Enum].GetValues(GetType(ComandoLettore))
                Dim gesto As String = Leggi("rimappa_" & comando.ToString().ToLowerInvariant(), "").Trim().ToLowerInvariant()
                If gesto = "" Then Continue For
                _gesti(gesto) = comando
                _comandiRimappati.Add(comando)
            Next
        End SyncLock
    End Sub

    Private Function Leggi(chiave As String, predefinito As String) As String
        Try
            If ConfigLettore.Esiste(Prefisso & chiave) Then
                Dim valore As String = ConfigLettore.Leggi(Prefisso & chiave)
                If Not String.IsNullOrEmpty(valore) Then Return valore
            End If
        Catch ex As Exception
            RegistroErrori.Scrivi("ImpostazioniLettore.Leggi", ex)
        End Try
        Return predefinito
    End Function

    Private Sub Scrivi(chiave As String, valore As String)
        ConfigLettore.Scrivi(Prefisso & chiave, If(valore, ""))
    End Sub

    Private Function LeggiIntero(chiave As String, predefinito As Integer) As Integer
        Dim testo As String = Leggi(chiave, "")
        Dim numero As Integer = 0
        If Integer.TryParse(testo, NumberStyles.Integer, CultureInfo.InvariantCulture, numero) Then Return numero
        Return predefinito
    End Function

    Private Function LeggiBool(chiave As String, predefinito As Boolean) As Boolean
        Dim testo As String = Leggi(chiave, "")
        If testo = "1" OrElse testo.Equals("true", StringComparison.OrdinalIgnoreCase) Then Return True
        If testo = "0" OrElse testo.Equals("false", StringComparison.OrdinalIgnoreCase) Then Return False
        Return predefinito
    End Function

    Private Function Normalizza(valore As String, ParamArray ammessi() As String) As String
        Dim testo As String = If(valore, "").Trim().ToLowerInvariant()
        For Each ammesso In ammessi
            If testo = ammesso Then Return ammesso
        Next
        Return ammessi(0)
    End Function

End Class
