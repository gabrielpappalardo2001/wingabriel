Imports System.Collections.Generic
Imports System.IO
Imports System.Linq
Imports System.Windows.Forms
Imports System.Xml.Linq
Imports wgcontrols

' Gestione del dizionario personale per la correzione e la pronuncia delle parole.
Public Class WGDizionario
    Private Shared log As New Logger()
    Private Shared instance As WGDizionario
    Private Const FILE_NAME As String = "personal.xml"
    Private dictionaryPath As String
    Private doc As XDocument

    Private Sub New()
        Try
            Dim dictionaryDir As String = Path.Combine(Application.StartupPath, "dictionary")
            If Not Directory.Exists(dictionaryDir) Then
                Directory.CreateDirectory(dictionaryDir)
            End If
            dictionaryPath = Path.Combine(dictionaryDir, FILE_NAME)
            CaricaODefault()
        Catch ex As Exception
            log.ScriviLog($"{DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss")}{Environment.NewLine}Errore nel costruttore WGDizionario: {ex.Message} {ex.StackTrace}")
            doc = New XDocument(New XElement("Dizionario")) ' Assicura che doc non sia Nothing
        End Try
    End Sub

    Public Shared Function GetInstance() As WGDizionario
        If instance Is Nothing Then
            instance = New WGDizionario()
        End If
        Return instance
    End Function

    Private Sub CaricaODefault()
        Try
            If File.Exists(dictionaryPath) Then
                doc = XDocument.Load(dictionaryPath)
                If doc.Root Is Nothing Then
                    doc = New XDocument(New XElement("Dizionario"))
                End If
            Else
                doc = New XDocument(New XElement("Dizionario"))
                doc.Save(dictionaryPath)
            End If
        Catch ex As Exception
            log.ScriviLog($"{DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss")}{Environment.NewLine}Errore caricando/creando dizionario XML: {ex.Message} {ex.StackTrace}")
            doc = New XDocument(New XElement("Dizionario"))
        End Try
    End Sub

    Private Sub Salva()
        Try
            If doc IsNot Nothing Then
                doc.Save(dictionaryPath)
            End If
        Catch ex As Exception
            log.ScriviLog($"{DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss")}{Environment.NewLine}Errore salvando dizionario XML: {ex.Message} {ex.StackTrace}")
        End Try
    End Sub

    Public Sub BackupDizionario()
        Try
            If File.Exists(dictionaryPath) Then
                File.Copy(dictionaryPath, dictionaryPath & ".bak", True)
            End If
        Catch ex As Exception
            log.ScriviLog($"{DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss")}{Environment.NewLine}Errore durante il backup del dizionario: {ex.Message} {ex.StackTrace}")
        End Try
    End Sub

    Public Function AggiungiParola(parola As String) As Boolean
        Try
            If String.IsNullOrWhiteSpace(parola) Then Return False
            parola = parola.Trim().ToLower()

            If doc Is Nothing OrElse doc.Root Is Nothing Then CaricaODefault()
            Dim root = doc.Root

            Dim exists = root.Elements("Parola").Any(Function(el) el.Value.Equals(parola, StringComparison.OrdinalIgnoreCase))

            If Not exists Then
                Dim maxId As Integer = 0
                If root.Elements("Parola").Any() Then
                    maxId = root.Elements("Parola").Max(Function(el) CInt(el.Attribute("id")))
                End If

                root.Add(New XElement("Parola", New XAttribute("id", maxId + 1), parola))
                Salva()
                BackupDizionario()
                Return True
            End If
            Return False
        Catch ex As Exception
            log.ScriviLog($"{DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss")}{Environment.NewLine}Errore in AggiungiParola: {ex.Message} {ex.StackTrace}")
            Return False
        End Try
    End Function

    Public Function EliminaParola(parola As String) As Boolean
        Try
            If String.IsNullOrWhiteSpace(parola) Then Return False
            parola = parola.Trim().ToLower()

            If doc Is Nothing OrElse doc.Root Is Nothing Then Return False
            Dim root = doc.Root

            Dim elementToDelete = root.Elements("Parola").FirstOrDefault(Function(el) el.Value.Equals(parola, StringComparison.OrdinalIgnoreCase))

            If elementToDelete IsNot Nothing Then
                elementToDelete.Remove()
                Salva()
                BackupDizionario()
                Return True
            End If
            Return False
        Catch ex As Exception
            log.ScriviLog($"{DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss")}{Environment.NewLine}Errore in EliminaParola: {ex.Message} {ex.StackTrace}")
            Return False
        End Try
    End Function

    Public Function ModificaParola(vecchiaParola As String, nuovaParola As String) As Boolean
        Try
            If String.IsNullOrWhiteSpace(vecchiaParola) OrElse String.IsNullOrWhiteSpace(nuovaParola) Then Return False
            vecchiaParola = vecchiaParola.Trim().ToLower()
            nuovaParola = nuovaParola.Trim().ToLower()

            If doc Is Nothing OrElse doc.Root Is Nothing Then Return False
            Dim root = doc.Root

            Dim newExists = root.Elements("Parola").Any(Function(el) el.Value.Equals(nuovaParola, StringComparison.OrdinalIgnoreCase))
            If newExists Then Return False ' La nuova parola esiste già

            Dim elementToModify = root.Elements("Parola").FirstOrDefault(Function(el) el.Value.Equals(vecchiaParola, StringComparison.OrdinalIgnoreCase))

            If elementToModify IsNot Nothing Then
                elementToModify.Value = nuovaParola
                Salva()
                BackupDizionario()
                Return True
            End If
            Return False
        Catch ex As Exception
            log.ScriviLog($"{DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss")}{Environment.NewLine}Errore in ModificaParola: {ex.Message} {ex.StackTrace}")
            Return False
        End Try
    End Function

    Public Function OttieniParole() As List(Of String)
        Dim parole As New List(Of String)()
        Try
            If doc Is Nothing OrElse doc.Root Is Nothing Then CaricaODefault()
            Dim root = doc.Root

            parole = root.Elements("Parola").Select(Function(el) el.Value).ToList()
            Return parole
        Catch ex As Exception
            log.ScriviLog($"{DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss")}{Environment.NewLine}Errore in OttieniParole: {ex.Message} {ex.StackTrace}")
            Return New List(Of String)()
        End Try
    End Function

    Public Function EsisteParola(parola As String) As Boolean
        Try
            If String.IsNullOrWhiteSpace(parola) Then Return False
            parola = parola.Trim().ToLower()

            If doc Is Nothing OrElse doc.Root Is Nothing Then
                CaricaODefault()
                If doc Is Nothing OrElse doc.Root Is Nothing Then Return False ' Fallback definitivo
            End If
            Dim root = doc.Root

            Return root.Elements("Parola").Any(Function(el) el.Value.Equals(parola, StringComparison.OrdinalIgnoreCase))
        Catch ex As Exception
            log.ScriviLog($"{DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss")}{Environment.NewLine}Errore in EsisteParola: {ex.Message} {ex.StackTrace}")
            Return False
        End Try
    End Function

End Class