Imports System.IO
Imports System.Xml.Serialization
Imports System.Collections.Generic
Imports wgcontrols

' Consultazione dell'elenco dei comuni italiani, dei codici catastali, dei CAP e delle informazioni geografiche da XML.
Public Class CLSComuni
    Private xmlFilePath As String
    Private ReadOnly _logger As New Logger()

    Public Sub New(xmlFilePath As String)
        Me.xmlFilePath = xmlFilePath
    End Sub

    Private Function LeggiXml() As List(Of Comune)
        Try
            If Not File.Exists(xmlFilePath) Then
                Return New List(Of Comune)()
            End If

            Dim comuni As New List(Of Comune)
            Dim serializer As New XmlSerializer(GetType(List(Of Comune)))
            Using sr As New StreamReader(xmlFilePath)
                comuni = CType(serializer.Deserialize(sr), List(Of Comune))
            End Using
            Return comuni
        Catch ex As FileNotFoundException
            _logger.ScriviLog($"{DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss")}{Environment.NewLine}LeggiXml - File non trovato: {ex.Message}{Environment.NewLine}Percorso: {xmlFilePath}{Environment.NewLine}Dettaglio: {ex.StackTrace}")
            Return New List(Of Comune)()
        Catch ex As DirectoryNotFoundException
            _logger.ScriviLog($"{DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss")}{Environment.NewLine}LeggiXml - Directory non trovata: {ex.Message}{Environment.NewLine}Percorso: {xmlFilePath}{Environment.NewLine}Dettaglio: {ex.StackTrace}")
            Return New List(Of Comune)()
        Catch ex As IOException
            _logger.ScriviLog($"{DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss")}{Environment.NewLine}LeggiXml - Errore di I/O durante la lettura del file XML: {ex.Message}{Environment.NewLine}Percorso: {xmlFilePath}{Environment.NewLine}Dettaglio: {ex.StackTrace}")
            Return New List(Of Comune)()
        Catch ex As UnauthorizedAccessException
            _logger.ScriviLog($"{DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss")}{Environment.NewLine}LeggiXml - Accesso non autorizzato al file XML: {ex.Message}{Environment.NewLine}Percorso: {xmlFilePath}{Environment.NewLine}Dettaglio: {ex.StackTrace}")
            Return New List(Of Comune)()
        Catch ex As InvalidOperationException
            _logger.ScriviLog($"{DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss")}{Environment.NewLine}LeggiXml - Errore di operazione non valida (es. XML malformato): {ex.Message}{Environment.NewLine}Percorso: {xmlFilePath}{Environment.NewLine}Dettaglio: {ex.StackTrace}")
            Return New List(Of Comune)()
        Catch ex As System.Security.SecurityException
            _logger.ScriviLog($"{DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss")}{Environment.NewLine}LeggiXml - Errore di sicurezza durante l'accesso al file XML: {ex.Message}{Environment.NewLine}Percorso: {xmlFilePath}{Environment.NewLine}Dettaglio: {ex.StackTrace}")
            Return New List(Of Comune)()
        Catch ex As NotSupportedException
            _logger.ScriviLog($"{DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss")}{Environment.NewLine}LeggiXml - Operazione non supportata: {ex.Message}{Environment.NewLine}Percorso: {xmlFilePath}{Environment.NewLine}Dettaglio: {ex.StackTrace}")
            Return New List(Of Comune)()
        Catch ex As Exception
            _logger.ScriviLog($"{DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss")}{Environment.NewLine}LeggiXml - Errore generico durante la lettura del file XML: {ex.Message}{Environment.NewLine}Percorso: {xmlFilePath}{Environment.NewLine}Dettaglio: {ex.StackTrace}")
            Return New List(Of Comune)()
        End Try
    End Function

    Public Function CercaPerNome(nome As String) As List(Of ComuneElenco)
        Try
            If String.IsNullOrWhiteSpace(nome) Then
                Return New List(Of ComuneElenco)()
            End If

            Dim comuni = LeggiXml()
            If comuni Is Nothing OrElse comuni.Count = 0 Then
                Return New List(Of ComuneElenco)()
            End If

            Dim risultati As New List(Of ComuneElenco)
            Dim comuniUnici As New HashSet(Of Comune)(New ComuneComparer())

            For Each comune In comuni
                If comune IsNot Nothing AndAlso comune.DenominazioneIta IsNot Nothing AndAlso comune.Cap IsNot Nothing Then
                    If comune.DenominazioneIta.IndexOf(nome, StringComparison.OrdinalIgnoreCase) >= 0 Then
                        If comuniUnici.Add(comune) Then
                            risultati.Add(New ComuneElenco With {.Nome = comune.DenominazioneIta, .Cap = comune.Cap})
                        End If
                    End If
                Else
                End If
            Next

            Return risultati
        Catch ex As NullReferenceException
            _logger.ScriviLog($"{DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss")}{Environment.NewLine}CercaPerNome - Errore di riferimento nullo: {ex.Message}{Environment.NewLine}Dettaglio: {ex.StackTrace}")
            Return New List(Of ComuneElenco)()
        Catch ex As ArgumentNullException
            _logger.ScriviLog($"{DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss")}{Environment.NewLine}CercaPerNome - Errore di argomento nullo: {ex.Message}{Environment.NewLine}Dettaglio: {ex.StackTrace}")
            Return New List(Of ComuneElenco)()
        Catch ex As Exception
            _logger.ScriviLog($"{DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss")}{Environment.NewLine}CercaPerNome - Errore generico: {ex.Message}{Environment.NewLine}Dettaglio: {ex.StackTrace}")
            Return New List(Of ComuneElenco)()
        End Try
    End Function

    Public Function CercaPerCap(cap As String) As Comune
        Try
            If String.IsNullOrWhiteSpace(cap) Then
                Return Nothing
            End If

            Dim comuni = LeggiXml()
            If comuni Is Nothing OrElse comuni.Count = 0 Then
                Return Nothing
            End If

            For Each comune In comuni
                If comune IsNot Nothing AndAlso comune.Cap IsNot Nothing Then
                    If comune.Cap = cap Then
                        Return comune
                    End If
                Else
                End If
            Next
            Return Nothing
        Catch ex As NullReferenceException
            _logger.ScriviLog($"{DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss")}{Environment.NewLine}CercaPerCap - Errore di riferimento nullo: {ex.Message}{Environment.NewLine}Dettaglio: {ex.StackTrace}")
            Return Nothing
        Catch ex As ArgumentNullException
            _logger.ScriviLog($"{DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss")}{Environment.NewLine}CercaPerCap - Errore di argomento nullo: {ex.Message}{Environment.NewLine}Dettaglio: {ex.StackTrace}")
            Return Nothing
        Catch ex As Exception
            _logger.ScriviLog($"{DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss")}{Environment.NewLine}CercaPerCap - Errore generico: {ex.Message}{Environment.NewLine}Dettaglio: {ex.StackTrace}")
            Return Nothing
        End Try
    End Function

    Public Function VisualizzaDettaglio(cap As String) As Comune
        Try
            If String.IsNullOrWhiteSpace(cap) Then
                Return Nothing
            End If
            Return CercaPerCap(cap)
        Catch ex As NullReferenceException
            _logger.ScriviLog($"{DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss")}{Environment.NewLine}VisualizzaDettaglio - Errore di riferimento nullo: {ex.Message}{Environment.NewLine}Dettaglio: {ex.StackTrace}")
            Return Nothing
        Catch ex As ArgumentNullException
            _logger.ScriviLog($"{DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss")}{Environment.NewLine}VisualizzaDettaglio - Errore di argomento nullo: {ex.Message}{Environment.NewLine}Dettaglio: {ex.StackTrace}")
            Return Nothing
        Catch ex As Exception
            _logger.ScriviLog($"{DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss")}{Environment.NewLine}VisualizzaDettaglio - Errore generico: {ex.Message}{Environment.NewLine}Dettaglio: {ex.StackTrace}")
            Return Nothing
        End Try
    End Function
End Class

Public Class Comune
    Public Property CodiceIstat As String
    Public Property DenominazioneItaAltra As String
    Public Property DenominazioneIta As String
    Public Property DenominazioneAltra As String
    Public Property Cap As String
    Public Property SiglaProvincia As String
    Public Property DenominazioneProvincia As String
    Public Property TipologiaProvincia As String
    Public Property CodiceRegione As String
    Public Property DenominazioneRegione As String
    Public Property TipologiaRegione As String
    Public Property RipartizioneGeografica As String
    Public Property FlagCapoluogo As String
    Public Property CodiceBelfiore As String
    Public Property Lat As String
    Public Property Lon As String
    Public Property SuperficieKmq As String
End Class

Public Class ComuneElenco
    Public Property Nome As String
    Public Property Cap As String
End Class

Public Class ComuneComparer
    Implements IEqualityComparer(Of Comune)

    Public Overloads Function Equals(x As Comune, y As Comune) As Boolean Implements IEqualityComparer(Of Comune).Equals
        Try
            If x Is Nothing OrElse y Is Nothing Then
                Return x Is y
            End If

            Return String.Equals(x.CodiceIstat, y.CodiceIstat, StringComparison.Ordinal) AndAlso
                   String.Equals(x.DenominazioneIta, y.DenominazioneIta, StringComparison.OrdinalIgnoreCase) AndAlso
                   String.Equals(x.Cap, y.Cap, StringComparison.Ordinal) AndAlso
                   String.Equals(x.SiglaProvincia, y.SiglaProvincia, StringComparison.OrdinalIgnoreCase) AndAlso
                   String.Equals(x.DenominazioneProvincia, y.DenominazioneProvincia, StringComparison.OrdinalIgnoreCase) AndAlso
                   String.Equals(x.TipologiaProvincia, y.TipologiaProvincia, StringComparison.OrdinalIgnoreCase) AndAlso
                   String.Equals(x.CodiceRegione, y.CodiceRegione, StringComparison.Ordinal) AndAlso
                   String.Equals(x.DenominazioneRegione, y.DenominazioneRegione, StringComparison.OrdinalIgnoreCase) AndAlso
                   String.Equals(x.TipologiaRegione, y.TipologiaRegione, StringComparison.OrdinalIgnoreCase) AndAlso
                   String.Equals(x.RipartizioneGeografica, y.RipartizioneGeografica, StringComparison.OrdinalIgnoreCase) AndAlso
                   String.Equals(x.FlagCapoluogo, y.FlagCapoluogo, StringComparison.OrdinalIgnoreCase) AndAlso
                   String.Equals(x.CodiceBelfiore, y.CodiceBelfiore, StringComparison.OrdinalIgnoreCase) AndAlso
                   String.Equals(x.Lat, y.Lat, StringComparison.Ordinal) AndAlso
                   String.Equals(x.Lon, y.Lon, StringComparison.Ordinal) AndAlso
                   String.Equals(x.SuperficieKmq, y.SuperficieKmq, StringComparison.Ordinal)
        Catch ex As NullReferenceException
            Dim logger As New Logger()
            logger.ScriviLog($"{DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss")}{Environment.NewLine}ComuneComparer.Equals - Errore di riferimento nullo: {ex.Message}{Environment.NewLine}Dettaglio: {ex.StackTrace}")
            Return False
        Catch ex As Exception
            Dim logger As New Logger()
            logger.ScriviLog($"{DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss")}{Environment.NewLine}ComuneComparer.Equals - Errore generico: {ex.Message}{Environment.NewLine}Dettaglio: {ex.StackTrace}")
            Return False
        End Try
    End Function

    Public Overloads Function GetHashCode(obj As Comune) As Integer Implements IEqualityComparer(Of Comune).GetHashCode
        Try
            If obj Is Nothing Then
                Return 0
            End If

            Dim hash As Integer = 17
            hash = hash * 23 + If(obj.CodiceIstat IsNot Nothing, obj.CodiceIstat.GetHashCode(), 0)
            hash = hash * 23 + If(obj.DenominazioneIta IsNot Nothing, obj.DenominazioneIta.GetHashCode(), 0)
            hash = hash * 23 + If(obj.Cap IsNot Nothing, obj.Cap.GetHashCode(), 0)
            hash = hash * 23 + If(obj.SiglaProvincia IsNot Nothing, obj.SiglaProvincia.GetHashCode(), 0)
            hash = hash * 23 + If(obj.DenominazioneProvincia IsNot Nothing, obj.DenominazioneProvincia.GetHashCode(), 0)
            hash = hash * 23 + If(obj.TipologiaProvincia IsNot Nothing, obj.TipologiaProvincia.GetHashCode(), 0)
            hash = hash * 23 + If(obj.CodiceRegione IsNot Nothing, obj.CodiceRegione.GetHashCode(), 0)
            hash = hash * 23 + If(obj.DenominazioneRegione IsNot Nothing, obj.DenominazioneRegione.GetHashCode(), 0)
            hash = hash * 23 + If(obj.TipologiaRegione IsNot Nothing, obj.TipologiaRegione.GetHashCode(), 0)
            hash = hash * 23 + If(obj.RipartizioneGeografica IsNot Nothing, obj.RipartizioneGeografica.GetHashCode(), 0)
            hash = hash * 23 + If(obj.FlagCapoluogo IsNot Nothing, obj.FlagCapoluogo.GetHashCode(), 0)
            hash = hash * 23 + If(obj.CodiceBelfiore IsNot Nothing, obj.CodiceBelfiore.GetHashCode(), 0)
            hash = hash * 23 + If(obj.Lat IsNot Nothing, obj.Lat.GetHashCode(), 0)
            hash = hash * 23 + If(obj.Lon IsNot Nothing, obj.Lon.GetHashCode(), 0)
            hash = hash * 23 + If(obj.SuperficieKmq IsNot Nothing, obj.SuperficieKmq.GetHashCode(), 0)

            Return hash
        Catch ex As NullReferenceException
            Dim logger As New Logger()
            logger.ScriviLog($"{DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss")}{Environment.NewLine}ComuneComparer.GetHashCode - Errore di riferimento nullo: {ex.Message}{Environment.NewLine}Dettaglio: {ex.StackTrace}")
            Return 0
        Catch ex As Exception
            Dim logger As New Logger()
            logger.ScriviLog($"{DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss")}{Environment.NewLine}ComuneComparer.GetHashCode - Errore generico: {ex.Message}{Environment.NewLine}Dettaglio: {ex.StackTrace}")
            Return 0
        End Try
    End Function
End Class