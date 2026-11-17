Imports System.Collections.Generic
Imports UIAutomationClient

' Le parti dell'interfaccia dei componenti aggiuntivi che servono per fare qualcosa di più che descrivere un programma:
' parlare, guardare gli elementi dell'interfaccia, ricevere avvisi, avere comandi con i tasti e voci nel menu del lettore.

''' <summary>Un comando di un componente: un tasto, premuto con il tasto dei comandi del lettore (Insert o Bloc maiuscole).</summary>
Public NotInheritable Class ComandoComponente
    ''' <summary>Il nome con cui il componente riconosce il comando in EseguiComando.</summary>
    Public Id As String = ""
    ''' <summary>Che cosa fa, per l'aiuto dei tasti.</summary>
    Public Descrizione As String = ""
    ''' <summary>Il codice del tasto virtuale di Windows (VK_*), per esempio 0x55 per U.</summary>
    Public Tasto As Integer
    Public Ctrl As Boolean
    Public Maiuscolo As Boolean
    ''' <summary>Se vero il comando vale in ogni programma; altrimenti solo quando è in primo piano uno dei programmi del componente.</summary>
    Public Globale As Boolean
End Class

''' <summary>Una voce che il componente aggiunge al menu del lettore.</summary>
Public NotInheritable Class VoceMenuComponente
    Public Testo As String = ""
    Public Azione As Action
End Class

''' <summary>Un avviso che il lettore sta per dire: notifica, regione viva, avviso del programma.</summary>
Public NotInheritable Class EventoComponente
    ''' <summary>"notifica", "regioneViva" oppure "avviso".</summary>
    Public Tipo As String = ""
    Public Testo As String = ""
    Public Elemento As ElementoUi
End Class

''' <summary>Quello che il lettore offre ai componenti: parlare, sapere dov'è il fuoco, ricordare delle impostazioni.</summary>
Public NotInheritable Class ServiziComponente

    Private ReadOnly _nomeFile As String

    Friend Sub New(nomeFile As String)
        _nomeFile = nomeFile
    End Sub

    ''' <summary>Dice una frase dopo quello che il lettore sta già dicendo.</summary>
    Public Sub Annuncia(testo As String)
        Try
            If Not String.IsNullOrWhiteSpace(testo) Then WGLettoreSchermo.GetInstance().ParlaApp(testo, False)
        Catch ex As Exception
            RegistroErrori.Scrivi("ServiziComponente.Annuncia", ex)
        End Try
    End Sub

    ''' <summary>Dice una frase subito, interrompendo quello che il lettore stava dicendo.</summary>
    Public Sub AnnunciaSubito(testo As String)
        Try
            If Not String.IsNullOrWhiteSpace(testo) Then WGLettoreSchermo.GetInstance().ParlaApp(testo, True)
        Catch ex As Exception
            RegistroErrori.Scrivi("ServiziComponente.AnnunciaSubito", ex)
        End Try
    End Sub

    ''' <summary>Il nome del programma in primo piano, senza ".exe" e in minuscolo.</summary>
    Public ReadOnly Property ProgrammaInPrimoPiano As String
        Get
            Try
                Return WGLettoreSchermo.GetInstance().NomeProgrammaPrimoPiano()
            Catch ex As Exception
                RegistroErrori.Scrivi("ServiziComponente.ProgrammaInPrimoPiano", ex)
                Return ""
            End Try
        End Get
    End Property

    ''' <summary>L'elemento che ha il fuoco, o Nothing.</summary>
    Public Function ElementoConFuoco() As ElementoUi
        Try
            Dim lettore As WGLettoreSchermo = WGLettoreSchermo.GetInstance()
            Dim elemento As IUIAutomationElement = lettore.ElementoConFocusApp(False)
            If elemento Is Nothing Then Return Nothing
            Return New ElementoUi(elemento, lettore)
        Catch ex As Exception
            RegistroErrori.Scrivi("ServiziComponente.ElementoConFuoco", ex)
            Return Nothing
        End Try
    End Function

    ''' <summary>Legge un'impostazione del componente (ogni componente ha le sue, nella configurazione del lettore).</summary>
    Public Function Leggi(chiave As String, predefinito As String) As String
        Try
            Return ConfigLettore.Leggi(ChiaveCompleta(chiave), predefinito)
        Catch ex As Exception
            RegistroErrori.Scrivi("ServiziComponente.Leggi", ex)
            Return predefinito
        End Try
    End Function

    Public Sub Scrivi(chiave As String, valore As String)
        Try
            ConfigLettore.Scrivi(ChiaveCompleta(chiave), valore)
        Catch ex As Exception
            RegistroErrori.Scrivi("ServiziComponente.Scrivi", ex)
        End Try
    End Sub

    Private Function ChiaveCompleta(chiave As String) As String
        Return "lettore_schermo_componente_" & System.IO.Path.GetFileNameWithoutExtension(_nomeFile) & "_" & chiave
    End Function

End Class

''' <summary>Un elemento dell'interfaccia di un programma, come lo vede UI Automation, con le sole cose che servono a un componente.</summary>
Public NotInheritable Class ElementoUi

    Private ReadOnly _elemento As IUIAutomationElement
    Private ReadOnly _lettore As WGLettoreSchermo

    Friend Sub New(elemento As IUIAutomationElement, lettore As WGLettoreSchermo)
        _elemento = elemento
        _lettore = lettore
    End Sub

    Public ReadOnly Property Nome As String
        Get
            Return Sicuro(Function() If(_elemento.CurrentName, ""), "")
        End Get
    End Property

    ''' <summary>Il tipo di controllo: "Button", "Edit", "List", "ListItem", "Text", "Window" e così via.</summary>
    Public ReadOnly Property Tipo As String
        Get
            Return NomeTipo(Sicuro(Function() _elemento.CurrentControlType, 0))
        End Get
    End Property

    Public ReadOnly Property IdAutomazione As String
        Get
            Return Sicuro(Function() If(_elemento.CurrentAutomationId, ""), "")
        End Get
    End Property

    Public ReadOnly Property NomeClasse As String
        Get
            Return Sicuro(Function() If(_elemento.CurrentClassName, ""), "")
        End Get
    End Property

    Public ReadOnly Property Valore As String
        Get
            Return Sicuro(Function() TryCast(_elemento.GetCurrentPropertyValue(30045), String), "")
        End Get
    End Property

    ''' <summary>Vero se è un campo dove si può scrivere.</summary>
    Public ReadOnly Property Modificabile As Boolean
        Get
            Return Sicuro(Function() _lettore.DescrittoreApp.ECampoScrittura(_elemento), False)
        End Get
    End Property

    Public Function Genitore() As ElementoUi
        Try
            Dim padre As IUIAutomationElement = _lettore.AutomazioneApp.ControlViewWalker.GetParentElement(_elemento)
            If padre Is Nothing Then Return Nothing
            Return New ElementoUi(padre, _lettore)
        Catch ex As Exception
            RegistroErrori.Scrivi("ElementoUi.Genitore", ex)
            Return Nothing
        End Try
    End Function

    Public Function Figli(Optional massimo As Integer = 200) As List(Of ElementoUi)
        Dim elenco As New List(Of ElementoUi)()
        Try
            Dim camminatore As IUIAutomationTreeWalker = _lettore.AutomazioneApp.ControlViewWalker
            Dim figlio As IUIAutomationElement = camminatore.GetFirstChildElement(_elemento)
            While figlio IsNot Nothing AndAlso elenco.Count < massimo
                elenco.Add(New ElementoUi(figlio, _lettore))
                figlio = camminatore.GetNextSiblingElement(figlio)
            End While
        Catch ex As Exception
            RegistroErrori.Scrivi("ElementoUi.Figli", ex)
        End Try
        Return elenco
    End Function

    ''' <summary>Gli elementi sotto questo, a livelli: prima i figli, poi i nipoti e così via, fino a un numero massimo.</summary>
    Public Function Discendenti(Optional massimo As Integer = 500, Optional profondita As Integer = 8) As List(Of ElementoUi)
        Dim elenco As New List(Of ElementoUi)()
        Try
            Dim camminatore As IUIAutomationTreeWalker = _lettore.AutomazioneApp.ControlViewWalker
            Dim coda As New Queue(Of KeyValuePair(Of IUIAutomationElement, Integer))()
            coda.Enqueue(New KeyValuePair(Of IUIAutomationElement, Integer)(_elemento, 0))
            While coda.Count > 0 AndAlso elenco.Count < massimo
                Dim corrente As KeyValuePair(Of IUIAutomationElement, Integer) = coda.Dequeue()
                If corrente.Value >= profondita Then Continue While
                Dim figlio As IUIAutomationElement = camminatore.GetFirstChildElement(corrente.Key)
                While figlio IsNot Nothing AndAlso elenco.Count < massimo
                    elenco.Add(New ElementoUi(figlio, _lettore))
                    coda.Enqueue(New KeyValuePair(Of IUIAutomationElement, Integer)(figlio, corrente.Value + 1))
                    figlio = camminatore.GetNextSiblingElement(figlio)
                End While
            End While
        Catch ex As Exception
            RegistroErrori.Scrivi("ElementoUi.Discendenti", ex)
        End Try
        Return elenco
    End Function

    ''' <summary>I testi che stanno dentro questo elemento (i controlli di tipo Text), in ordine e senza ripetizioni.</summary>
    Public Function TestiDiscendenti(Optional massimo As Integer = 20) As List(Of String)
        Dim testi As New List(Of String)()
        Try
            For Each discendente As ElementoUi In Discendenti(300, 6)
                If discendente.Tipo <> "Text" Then Continue For
                Dim nome As String = discendente.Nome.Trim()
                If nome = "" OrElse testi.Contains(nome) Then Continue For
                testi.Add(nome)
                If testi.Count >= massimo Then Exit For
            Next
        Catch ex As Exception
            RegistroErrori.Scrivi("ElementoUi.TestiDiscendenti", ex)
        End Try
        Return testi
    End Function

    ''' <summary>Porta il fuoco su questo elemento. Vero se ci è riuscito.</summary>
    Public Function DaiFuoco() As Boolean
        Try
            _elemento.SetFocus()
            Return True
        Catch ex As Exception
            RegistroErrori.Scrivi("ElementoUi.DaiFuoco", ex)
            Return False
        End Try
    End Function

    Private Shared Function Sicuro(Of T)(lettura As Func(Of T), predefinito As T) As T
        Try
            Return lettura.Invoke()
        Catch ex As Exception
            RegistroErrori.Scrivi("ElementoUi.Sicuro", ex)
            Return predefinito
        End Try
    End Function

    Private Shared Function NomeTipo(codice As Integer) As String
        Select Case codice
            Case 50000 : Return "Button"
            Case 50002 : Return "CheckBox"
            Case 50003 : Return "ComboBox"
            Case 50004 : Return "Edit"
            Case 50005 : Return "Hyperlink"
            Case 50006 : Return "Image"
            Case 50007 : Return "ListItem"
            Case 50008 : Return "List"
            Case 50009 : Return "Menu"
            Case 50011 : Return "MenuItem"
            Case 50018 : Return "Tab"
            Case 50019 : Return "TabItem"
            Case 50020 : Return "Text"
            Case 50021 : Return "ToolBar"
            Case 50023 : Return "Tree"
            Case 50024 : Return "TreeItem"
            Case 50025 : Return "Custom"
            Case 50026 : Return "Group"
            Case 50028 : Return "DataGrid"
            Case 50029 : Return "DataItem"
            Case 50030 : Return "Document"
            Case 50032 : Return "Window"
            Case 50033 : Return "Pane"
            Case Else : Return "Altro"
        End Select
    End Function

End Class
