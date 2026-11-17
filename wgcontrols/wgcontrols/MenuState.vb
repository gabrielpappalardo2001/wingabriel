Imports System.IO
Imports System.Xml.Serialization

' L'ultima voce scelta in ogni menu: tabella MenuPosizioni del database dell'interfaccia (WGGui), come le altre cose del sistema
' (file recenti, sessione, ricerca, scrivania). Il vecchio menu.xml si porta nel database la prima volta e poi si toglie.
Public Class MenuStateManager
    Private Const Tabella As String = "MenuPosizioni"

    Private Shared _states As Dictionary(Of String, Integer)
    Private Shared ReadOnly _log As New wgcontrols.Logger()
    Private Shared ReadOnly _lock As New Object()

    Shared Sub New()
        Try
            _states = CaricaStati()
        Catch ex As Exception
            _log.ScriviLog("MenuStateManager: errore nell'inizializzazione: " & ex.Message & " " & ex.StackTrace)
            _states = New Dictionary(Of String, Integer)()
        End Try
    End Sub

    Private Shared _database As WGDb.WGDatabase

    Private Shared Function Db() As WGDb.WGDatabase
        If _database Is Nothing Then _database = WGGui.CreaTabella(Tabella, "Titolo TEXT, Indice INTEGER")
        Return _database
    End Function

    Private Shared Function CaricaStati() As Dictionary(Of String, Integer)
        Dim stati As New Dictionary(Of String, Integer)()
        SyncLock _lock
            Dim d As WGDb.WGDatabase = Db()
            If d Is Nothing Then Return stati

            ImportaVecchioFile(d)

            For Each riga As DataRow In d.OttieniTuttiIRecord(Tabella).Rows
                Try
                    Dim titolo As String = Convert.ToString(riga("Titolo"))
                    If String.IsNullOrWhiteSpace(titolo) Then Continue For
                    stati(titolo) = If(IsDBNull(riga("Indice")), 0, Convert.ToInt32(riga("Indice")))
                Catch exRiga As Exception
                    _log.ScriviLog("MenuStateManager.CaricaStati riga: " & exRiga.Message)
                End Try
            Next
        End SyncLock
        Return stati
    End Function

    ' Il vecchio menu.xml (e la sua copia .bak) si importano una volta sola, se la tabella è vuota, e si tolgono.
    Private Shared Sub ImportaVecchioFile(d As WGDb.WGDatabase)
        Try
            Dim cartella As String = WGDb.WGDatabase.CartellaDati("menu")
            If String.IsNullOrWhiteSpace(cartella) Then Return
            Dim vecchio As String = Path.Combine(cartella, "menu.xml")
            Dim copia As String = vecchio & ".bak"
            If Not File.Exists(vecchio) AndAlso Not File.Exists(copia) Then Return

            Dim contate As Object = d.EseguiScalare("SELECT COUNT(*) FROM " & Tabella & ";")
            Dim vuota As Boolean = (contate Is Nothing OrElse IsDBNull(contate) OrElse Convert.ToInt64(contate) = 0)
            If vuota Then
                Dim sorgente As String = If(File.Exists(vecchio), vecchio, copia)
                Try
                    Dim serializzatore As New XmlSerializer(GetType(List(Of MenuState)))
                    Using lettore As New StreamReader(sorgente)
                        For Each voce As MenuState In CType(serializzatore.Deserialize(lettore), List(Of MenuState))
                            If String.IsNullOrWhiteSpace(voce.Title) Then Continue For
                            d.InserisciRecord(Tabella, New Dictionary(Of String, Object) From {{"Titolo", voce.Title}, {"Indice", voce.LastIndex}})
                        Next
                    End Using
                Catch exLettura As Exception
                    _log.ScriviLog("MenuStateManager.ImportaVecchioFile: il vecchio file non si legge: " & exLettura.Message)
                End Try
            End If

            If File.Exists(vecchio) Then File.Delete(vecchio)
            If File.Exists(copia) Then File.Delete(copia)
        Catch ex As Exception
            _log.ScriviLog("MenuStateManager.ImportaVecchioFile: " & ex.Message)
        End Try
    End Sub

    Public Shared Sub SalvaStato(titolo As String, indice As Integer)
        If String.IsNullOrWhiteSpace(titolo) Then Return
        SyncLock _lock
            _states(titolo) = indice
            Try
                Dim d As WGDb.WGDatabase = Db()
                If d Is Nothing Then Return
                Dim parametri As New Dictionary(Of String, Object) From {{"@titolo", titolo}, {"@indice", indice}}
                If d.EseguiNonQuery("UPDATE " & Tabella & " SET Indice = @indice WHERE Titolo = @titolo;", parametri) <= 0 Then
                    d.InserisciRecord(Tabella, New Dictionary(Of String, Object) From {{"Titolo", titolo}, {"Indice", indice}})
                End If
            Catch ex As Exception
                _log.ScriviLog("MenuStateManager.SalvaStato: " & ex.Message)
            End Try
        End SyncLock
    End Sub

    Public Shared Function OttieniUltimoIndice(titolo As String) As Integer
        SyncLock _lock
            If String.IsNullOrWhiteSpace(titolo) OrElse Not _states.ContainsKey(titolo) Then
                Return 0
            End If
            Return _states(titolo)
        End SyncLock
    End Function
End Class

Public Class MenuState
    Public Property Title As String
    Public Property LastIndex As Integer
End Class
