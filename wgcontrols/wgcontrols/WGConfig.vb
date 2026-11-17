Imports System
Imports System.Collections.Generic
Imports System.IO
Imports System.Text
Imports System.Threading
Imports System.Xml
Imports wgcontrols

Public Class WGConfig

    Private Shared ReadOnly log As New wgcontrols.Logger()

    Private Class GestoreCondivisoXml

        Private Shared _istanza As GestoreCondivisoXml
        Private Shared ReadOnly _syncRoot As New Object()

        Private ReadOnly _xmlDoc As XmlDocument
        Private ReadOnly _configFilePath As String
        Private ReadOnly _configFileTmpPath As String
        Private ReadOnly _configFileBakPath As String
        Private ReadOnly _fileLock As New Object()
        Private _ultimaModificaFile As DateTime = DateTime.MinValue
        Private _ultimoControllo As Integer

        Public Shared ReadOnly Property Istanza As GestoreCondivisoXml
            Get
                If _istanza Is Nothing Then
                    SyncLock _syncRoot
                        If _istanza Is Nothing Then
                            _istanza = New GestoreCondivisoXml()
                        End If
                    End SyncLock
                End If
                Return _istanza
            End Get
        End Property

        Private Sub New()
            Try
                Dim baseDir As String = AppDomain.CurrentDomain.BaseDirectory
                _configFilePath = RisolviPercorsoConfig(baseDir)
                Dim configDir As String = Path.GetDirectoryName(_configFilePath)
                _configFileTmpPath = _configFilePath & ".tmp"
                _configFileBakPath = _configFilePath & ".bak"
                _xmlDoc = New XmlDocument()
                _xmlDoc.XmlResolver = Nothing

                If Not Directory.Exists(configDir) Then
                    Directory.CreateDirectory(configDir)
                End If

                If Not File.Exists(_configFilePath) Then
                    CreaDocumentoVuoto()
                    Return
                End If

                If Not TentaCaricamento(_configFilePath) Then
                    If File.Exists(_configFileBakPath) AndAlso TentaCaricamento(_configFileBakPath) Then
                        SalvaAtomico()
                    ElseIf File.Exists(_configFileTmpPath) AndAlso TentaCaricamento(_configFileTmpPath) Then
                        SalvaAtomico()
                    Else
                        CreaDocumentoVuoto()
                    End If
                Else
                    MigraFormatoMultiplo()
                    If _xmlDoc.DocumentElement Is Nothing Then
                        CreaDocumentoVuoto()
                    End If
                End If
            Catch ex As Exception
                log.ScriviLog(DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") & Environment.NewLine & "Errore in New (GestoreCondivisoXml): " & ex.Message & Environment.NewLine & ex.StackTrace)
                Try
                    CreaDocumentoVuoto()
                Catch exFallback As Exception
                    log.ScriviLog(DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") & Environment.NewLine & "Errore critico nel fallback New: " & exFallback.Message)
                End Try
            End Try
        End Sub

        ''' <summary>
        ''' Il config.xml in uso. WinGabriel e il lettore di schermo tengono nel registro dell'utente un puntatore al file che condividono:
        ''' se WinGabriel ha già il suo, quello vale (e il puntatore lo segue); se non ce l'ha ma il lettore ne ha già uno, WinGabriel usa
        ''' quello. Così le impostazioni sono le stesse anche con i due programmi in cartelle diverse, comunque siano stati installati.
        ''' </summary>
        Private Shared Function RisolviPercorsoConfig(baseDir As String) As String
            ' la configurazione sta fuori dai dati: nei dati locali dell'utente se il programma è installato, accanto al programma se è portatile
            Dim propria As String = WGDb.WGPercorsiApplicazione.PercorsoConfig()
            Try
                Dim condiviso As String = ""
                Using chiave As Microsoft.Win32.RegistryKey = Microsoft.Win32.Registry.CurrentUser.OpenSubKey("Software\WinGabriel")
                    If chiave IsNot Nothing Then condiviso = Convert.ToString(chiave.GetValue("PercorsoConfigurazione"))
                End Using
                If Not File.Exists(propria) AndAlso Not String.IsNullOrWhiteSpace(condiviso) AndAlso File.Exists(condiviso) Then Return condiviso
                Using chiave As Microsoft.Win32.RegistryKey = Microsoft.Win32.Registry.CurrentUser.CreateSubKey("Software\WinGabriel")
                    chiave.SetValue("PercorsoConfigurazione", propria)
                End Using
            Catch
            End Try
            Return propria
        End Function

        Private Function XPathLiteral(value As String) As String
            If value Is Nothing Then Return "''"
            If Not value.Contains("'") Then Return "'" & value & "'"
            If Not value.Contains("""") Then Return """" & value & """"

            Dim parts As String() = value.Split("'"c)
            Dim sb As New StringBuilder()
            sb.Append("concat(")
            For i As Integer = 0 To parts.Length - 1
                If i > 0 Then
                    sb.Append(",""'"",")
                End If
                sb.Append("'")
                sb.Append(parts(i))
                sb.Append("'")
            Next
            sb.Append(")")
            Return sb.ToString()
        End Function

        Private Function TentaCaricamento(percorso As String) As Boolean
            Try
                Dim docTemp As New XmlDocument()
                docTemp.XmlResolver = Nothing
                docTemp.Load(percorso)
                If docTemp.DocumentElement Is Nothing Then Return False

                _xmlDoc.RemoveAll()
                For Each nodo As XmlNode In docTemp.ChildNodes
                    Dim nodoImportato As XmlNode = _xmlDoc.ImportNode(nodo, True)
                    _xmlDoc.AppendChild(nodoImportato)
                Next
                Try
                    _ultimaModificaFile = File.GetLastWriteTimeUtc(percorso)
                Catch
                End Try

                If _xmlDoc.DocumentElement Is Nothing Then
                    CreaDocumentoVuoto()
                End If

                Return True
            Catch ex As Exception
                log.ScriviLog(DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") & Environment.NewLine & "Errore in TentaCaricamento (" & percorso & "): " & ex.Message & Environment.NewLine & ex.StackTrace)
                Return False
            End Try
        End Function

        ''' <summary>
        ''' Un altro programma di WinGabriel (per esempio il lettore di schermo, che gira da solo) può avere
        ''' cambiato il file: se è più recente di quello caricato lo si rilegge, così nessuno dei due
        ''' sovrascrive le modifiche dell'altro. Il controllo si fa al massimo ogni 300 millisecondi.
        ''' </summary>
        Public Sub RicaricaSeCambiato()
            Try
                Dim adesso As Integer = Environment.TickCount
                If Math.Abs(adesso - _ultimoControllo) < 300 Then Return
                _ultimoControllo = adesso
                SyncLock _fileLock
                    If Not File.Exists(_configFilePath) Then Return
                    Dim modifica As DateTime = File.GetLastWriteTimeUtc(_configFilePath)
                    If modifica = _ultimaModificaFile Then Return
                    TentaCaricamento(_configFilePath)
                End SyncLock
            Catch
            End Try
        End Sub

        Private Sub CreaDocumentoVuoto()
            Try
                _xmlDoc.RemoveAll()
                Dim declaration As XmlDeclaration = _xmlDoc.CreateXmlDeclaration("1.0", "UTF-8", Nothing)
                Dim root As XmlElement = _xmlDoc.CreateElement("Configurazioni")
                _xmlDoc.AppendChild(declaration)
                _xmlDoc.AppendChild(root)
                SalvaAtomico()
            Catch ex As Exception
                log.ScriviLog(DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") & Environment.NewLine & "Errore in CreaDocumentoVuoto: " & ex.Message & Environment.NewLine & ex.StackTrace)
            End Try
        End Sub

        Private Sub AssicuraRoot()
            If _xmlDoc.DocumentElement Is Nothing Then
                Dim declaration As XmlDeclaration = Nothing
                For Each n As XmlNode In _xmlDoc.ChildNodes
                    If TypeOf n Is XmlDeclaration Then
                        declaration = CType(n, XmlDeclaration)
                        Exit For
                    End If
                Next

                _xmlDoc.RemoveAll()
                If declaration Is Nothing Then
                    declaration = _xmlDoc.CreateXmlDeclaration("1.0", "UTF-8", Nothing)
                End If
                _xmlDoc.AppendChild(declaration)
                _xmlDoc.AppendChild(_xmlDoc.CreateElement("Configurazioni"))
            End If
        End Sub

        Private Sub SalvaAtomico()
            SyncLock _fileLock
                Try
                    AssicuraRoot()
                    _xmlDoc.Save(_configFileTmpPath)

                    Dim docVerifica As New XmlDocument()
                    docVerifica.XmlResolver = Nothing
                    docVerifica.Load(_configFileTmpPath)
                    If docVerifica.DocumentElement Is Nothing Then
                        Throw New Exception("Verifica del file temporaneo fallita.")
                    End If

                    If File.Exists(_configFilePath) Then
                        Try
                            File.Copy(_configFilePath, _configFileBakPath, True)
                        Catch exBak As Exception
                            log.ScriviLog(DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") & Environment.NewLine & "Avviso backup config fallito: " & exBak.Message)
                        End Try
                    End If

                    File.Copy(_configFileTmpPath, _configFilePath, True)
                    Try
                        _ultimaModificaFile = File.GetLastWriteTimeUtc(_configFilePath)
                    Catch
                    End Try

                    Try
                        File.Delete(_configFileTmpPath)
                    Catch exClean As Exception
                        log.ScriviLog(DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") & Environment.NewLine & "Avviso: impossibile eliminare il file .tmp: " & exClean.Message)
                    End Try

                Catch ex As Exception
                    log.ScriviLog(DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") & Environment.NewLine & "Errore in SalvaAtomico: " & ex.Message & Environment.NewLine & ex.StackTrace)
                    Throw
                End Try
            End SyncLock
        End Sub

        Private Sub MigraFormatoMultiplo()
            SyncLock _fileLock
                Try
                    AssicuraRoot()

                    Dim modificato As Boolean = False
                    Dim ids As New List(Of String)()
                    Dim tuttiNodi As XmlNodeList = _xmlDoc.SelectNodes("//Configurazione")
                    If tuttiNodi IsNot Nothing Then
                        For Each n As XmlNode In tuttiNodi
                            Dim attr As XmlAttribute = n.Attributes("id")
                            If attr IsNot Nothing AndAlso Not ids.Contains(attr.Value) Then
                                ids.Add(attr.Value)
                            End If
                        Next
                    End If

                    For Each id As String In ids
                        Dim xpath As String = "//Configurazione[@id=" & XPathLiteral(id) & "]"
                        Dim nodi As XmlNodeList = _xmlDoc.SelectNodes(xpath)
                        If nodi IsNot Nothing AndAlso nodi.Count > 1 Then
                            modificato = True
                            Dim nuovoNodoPrincipale As XmlElement = _xmlDoc.CreateElement("Configurazione")
                            nuovoNodoPrincipale.SetAttribute("id", id)

                            Dim nodiDaRimuovere As New List(Of XmlNode)()
                            For Each nodo As XmlNode In nodi
                                Dim elemValore As XmlElement = _xmlDoc.CreateElement("Valore")
                                elemValore.SetAttribute("id", Guid.NewGuid().ToString())
                                elemValore.InnerText = nodo.InnerText
                                nuovoNodoPrincipale.AppendChild(elemValore)
                                nodiDaRimuovere.Add(nodo)
                            Next

                            For Each n As XmlNode In nodiDaRimuovere
                                If n.ParentNode IsNot Nothing Then n.ParentNode.RemoveChild(n)
                            Next
                            _xmlDoc.DocumentElement.AppendChild(nuovoNodoPrincipale)
                        End If
                    Next

                    If modificato Then
                        SalvaAtomico()
                    End If
                Catch ex As Exception
                    log.ScriviLog(DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") & Environment.NewLine & "Errore in MigraFormatoMultiplo: " & ex.Message & Environment.NewLine & ex.StackTrace)
                End Try
            End SyncLock
        End Sub

        Public Function AggiungiValore(id As String, valore As String) As Boolean
            SyncLock _fileLock
                Try
                    AssicuraRoot()
                    If String.IsNullOrWhiteSpace(id) Then Return False
                    If valore Is Nothing Then valore = ""

                    Dim xpath As String = "//Configurazione[@id=" & XPathLiteral(id) & "]"
                    Dim nodo As XmlNode = _xmlDoc.SelectSingleNode(xpath)

                    If nodo IsNot Nothing Then
                        If nodo.HasChildNodes AndAlso nodo.FirstChild IsNot Nothing AndAlso nodo.FirstChild.Name = "Valore" Then
                            Dim primoValore As XmlNode = nodo.SelectSingleNode("Valore[1]")
                            If primoValore IsNot Nothing Then
                                primoValore.InnerText = valore
                            Else
                                nodo.InnerText = valore
                            End If
                        Else
                            nodo.InnerText = valore
                        End If
                    Else
                        Dim nuovoNodo As XmlElement = _xmlDoc.CreateElement("Configurazione")
                        nuovoNodo.SetAttribute("id", id)
                        nuovoNodo.InnerText = valore
                        _xmlDoc.DocumentElement.AppendChild(nuovoNodo)
                    End If

                    SalvaAtomico()
                    Return True
                Catch ex As Exception
                    log.ScriviLog(DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") & Environment.NewLine & "Errore in AggiungiValore: " & ex.Message & Environment.NewLine & ex.StackTrace)
                    Return False
                End Try
            End SyncLock
        End Function

        Public Function AggiungiValoreMultiplo(id As String, valore As String) As String
            SyncLock _fileLock
                Try
                    AssicuraRoot()
                    If String.IsNullOrWhiteSpace(id) Then Return String.Empty
                    If valore Is Nothing Then valore = ""

                    Dim xpath As String = "//Configurazione[@id=" & XPathLiteral(id) & "]"
                    Dim nodoPrincipale As XmlNode = _xmlDoc.SelectSingleNode(xpath)

                    If nodoPrincipale Is Nothing Then
                        nodoPrincipale = _xmlDoc.CreateElement("Configurazione")
                        Dim attr As XmlAttribute = _xmlDoc.CreateAttribute("id")
                        attr.Value = id
                        nodoPrincipale.Attributes.Append(attr)
                        _xmlDoc.DocumentElement.AppendChild(nodoPrincipale)
                    Else
                        If nodoPrincipale.HasChildNodes AndAlso nodoPrincipale.FirstChild IsNot Nothing AndAlso nodoPrincipale.FirstChild.NodeType = XmlNodeType.Text Then
                            Dim vecchioValore As String = nodoPrincipale.InnerText
                            nodoPrincipale.InnerText = ""
                            If Not String.IsNullOrWhiteSpace(vecchioValore) Then
                                Dim elemValoreVecchio As XmlElement = _xmlDoc.CreateElement("Valore")
                                elemValoreVecchio.SetAttribute("id", Guid.NewGuid().ToString())
                                elemValoreVecchio.InnerText = vecchioValore
                                nodoPrincipale.AppendChild(elemValoreVecchio)
                            End If
                        End If
                    End If

                    Dim nuovoId As String = Guid.NewGuid().ToString()
                    Dim nuovoNodoValore As XmlElement = _xmlDoc.CreateElement("Valore")
                    nuovoNodoValore.SetAttribute("id", nuovoId)
                    nuovoNodoValore.InnerText = valore
                    nodoPrincipale.AppendChild(nuovoNodoValore)

                    SalvaAtomico()
                    Return nuovoId
                Catch ex As Exception
                    log.ScriviLog(DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") & Environment.NewLine & "Errore in AggiungiValoreMultiplo: " & ex.Message & Environment.NewLine & ex.StackTrace)
                    Return String.Empty
                End Try
            End SyncLock
        End Function

        Public Function AggiungiValori(id As String, ParamArray valori() As String) As List(Of String)
            Dim idsNuovi As New List(Of String)()
            Try
                If valori Is Nothing Then Return idsNuovi
                For Each valore As String In valori
                    Dim idVal As String = AggiungiValoreMultiplo(id, valore)
                    If Not String.IsNullOrWhiteSpace(idVal) Then
                        idsNuovi.Add(idVal)
                    End If
                Next
            Catch ex As Exception
                log.ScriviLog(DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") & Environment.NewLine & "Errore in AggiungiValori: " & ex.Message & Environment.NewLine & ex.StackTrace)
            End Try
            Return idsNuovi
        End Function

        Public Function LeggiValoriMultipli(id As String) As List(Of String)
            Dim lista As New List(Of String)()
            SyncLock _fileLock
                Try
                    If String.IsNullOrWhiteSpace(id) Then Return lista

                    Dim xpath As String = "//Configurazione[@id=" & XPathLiteral(id) & "]"
                    Dim nodoPrincipale As XmlNode = _xmlDoc.SelectSingleNode(xpath)
                    If nodoPrincipale IsNot Nothing Then
                        If nodoPrincipale.HasChildNodes AndAlso nodoPrincipale.FirstChild IsNot Nothing AndAlso nodoPrincipale.FirstChild.Name = "Valore" Then
                            For Each nodoValore As XmlNode In nodoPrincipale.SelectNodes("Valore")
                                lista.Add(nodoValore.InnerText)
                            Next
                        Else
                            If Not String.IsNullOrWhiteSpace(nodoPrincipale.InnerText) Then
                                lista.Add(nodoPrincipale.InnerText)
                            End If
                        End If
                    End If
                Catch ex As Exception
                    log.ScriviLog(DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") & Environment.NewLine & "Errore in LeggiValoriMultipli: " & ex.Message & Environment.NewLine & ex.StackTrace)
                End Try
            End SyncLock
            Return lista
        End Function

        Public Function LeggiValore(id As String, Optional indice As Integer = 0) As String
            SyncLock _fileLock
                Try
                    Dim valori As List(Of String) = LeggiValoriMultipli(id)
                    If valori Is Nothing OrElse valori.Count = 0 Then Return Nothing
                    If indice <= 0 Then Return valori(0)
                    If indice - 1 >= 0 AndAlso indice - 1 < valori.Count Then Return valori(indice - 1)
                    Return Nothing
                Catch ex As Exception
                    log.ScriviLog(DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") & Environment.NewLine & "Errore in LeggiValore: " & ex.Message & Environment.NewLine & ex.StackTrace)
                    Return Nothing
                End Try
            End SyncLock
        End Function

        Public Function LeggiValori(id As String) As List(Of String)
            Try
                Return GestoreCondivisoXml.Istanza.LeggiValoriMultipli(id)
            Catch ex As Exception
                log.ScriviLog(DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") & Environment.NewLine & "Errore in LeggiValori: " & ex.Message & Environment.NewLine & ex.StackTrace)
                Return New List(Of String)()
            End Try
        End Function

        Public Sub EliminaValore(id As String)
            SyncLock _fileLock
                Try
                    If String.IsNullOrWhiteSpace(id) Then Exit Sub
                    Dim xpath As String = "//Configurazione[@id=" & XPathLiteral(id) & "]"
                    Dim nodo As XmlNode = _xmlDoc.SelectSingleNode(xpath)
                    If nodo IsNot Nothing AndAlso nodo.ParentNode IsNot Nothing Then
                        nodo.ParentNode.RemoveChild(nodo)
                        SalvaAtomico()
                    End If
                Catch ex As Exception
                    log.ScriviLog(DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") & Environment.NewLine & "Errore in EliminaValore: " & ex.Message & Environment.NewLine & ex.StackTrace)
                End Try
            End SyncLock
        End Sub

        Public Sub EliminaValoreMultiplo(id As String, valore As String)
            SyncLock _fileLock
                Try
                    If String.IsNullOrWhiteSpace(id) Then Exit Sub
                    Dim xpath As String = "//Configurazione[@id=" & XPathLiteral(id) & "]"
                    Dim nodoPrincipale As XmlNode = _xmlDoc.SelectSingleNode(xpath)
                    If nodoPrincipale IsNot Nothing Then
                        If nodoPrincipale.HasChildNodes AndAlso nodoPrincipale.FirstChild IsNot Nothing AndAlso nodoPrincipale.FirstChild.Name = "Valore" Then
                            Dim nodoDaRimuovere As XmlNode = Nothing
                            For Each nodoValore As XmlNode In nodoPrincipale.SelectNodes("Valore")
                                If nodoValore.InnerText = valore Then
                                    nodoDaRimuovere = nodoValore
                                    Exit For
                                End If
                            Next
                            If nodoDaRimuovere IsNot Nothing Then
                                nodoPrincipale.RemoveChild(nodoDaRimuovere)
                                If nodoPrincipale.ChildNodes.Count = 0 AndAlso nodoPrincipale.ParentNode IsNot Nothing Then
                                    nodoPrincipale.ParentNode.RemoveChild(nodoPrincipale)
                                End If
                                SalvaAtomico()
                            End If
                        Else
                            If nodoPrincipale.InnerText = valore Then
                                If nodoPrincipale.ParentNode IsNot Nothing Then
                                    nodoPrincipale.ParentNode.RemoveChild(nodoPrincipale)
                                    SalvaAtomico()
                                End If
                            End If
                        End If
                    End If
                Catch ex As Exception
                    log.ScriviLog(DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") & Environment.NewLine & "Errore in EliminaValoreMultiplo: " & ex.Message & Environment.NewLine & ex.StackTrace)
                End Try
            End SyncLock
        End Sub

        Public Sub EliminaValorePerIdAutogenerato(id As String, idAutogenerato As String)
            SyncLock _fileLock
                Try
                    If String.IsNullOrWhiteSpace(id) OrElse String.IsNullOrWhiteSpace(idAutogenerato) Then Exit Sub
                    Dim xpath As String = "//Configurazione[@id=" & XPathLiteral(id) & "]"
                    Dim nodoPrincipale As XmlNode = _xmlDoc.SelectSingleNode(xpath)
                    If nodoPrincipale IsNot Nothing Then
                        Dim nodoDaRimuovere As XmlNode = nodoPrincipale.SelectSingleNode("Valore[@id=" & XPathLiteral(idAutogenerato) & "]")
                        If nodoDaRimuovere IsNot Nothing Then
                            nodoPrincipale.RemoveChild(nodoDaRimuovere)
                            If nodoPrincipale.ChildNodes.Count = 0 AndAlso nodoPrincipale.ParentNode IsNot Nothing Then
                                nodoPrincipale.ParentNode.RemoveChild(nodoPrincipale)
                            End If
                            SalvaAtomico()
                        End If
                    End If
                Catch ex As Exception
                    log.ScriviLog(DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") & Environment.NewLine & "Errore in EliminaValorePerIdAutogenerato: " & ex.Message & Environment.NewLine & ex.StackTrace)
                End Try
            End SyncLock
        End Sub

        Public Sub ModificaValore(id As String, nuovoValore As String)
            SyncLock _fileLock
                Try
                    If String.IsNullOrWhiteSpace(id) Then Exit Sub
                    If nuovoValore Is Nothing Then nuovoValore = ""

                    Dim xpath As String = "//Configurazione[@id=" & XPathLiteral(id) & "]"
                    Dim nodo As XmlNode = _xmlDoc.SelectSingleNode(xpath)
                    If nodo IsNot Nothing Then
                        If nodo.HasChildNodes AndAlso nodo.FirstChild IsNot Nothing AndAlso nodo.FirstChild.Name = "Valore" Then
                            Dim primoValore As XmlNode = nodo.SelectSingleNode("Valore[1]")
                            If primoValore IsNot Nothing Then
                                primoValore.InnerText = nuovoValore
                                SalvaAtomico()
                            End If
                        Else
                            nodo.InnerText = nuovoValore
                            SalvaAtomico()
                        End If
                    End If
                Catch ex As Exception
                    log.ScriviLog(DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") & Environment.NewLine & "Errore in ModificaValore: " & ex.Message & Environment.NewLine & ex.StackTrace)
                End Try
            End SyncLock
        End Sub

        Public Function EsisteId(id As String) As Boolean
            SyncLock _fileLock
                Try
                    If String.IsNullOrWhiteSpace(id) Then Return False
                    Dim xpath As String = "//Configurazione[@id=" & XPathLiteral(id) & "]"
                    Dim nodo As XmlNode = _xmlDoc.SelectSingleNode(xpath)
                    Return nodo IsNot Nothing
                Catch ex As Exception
                    log.ScriviLog(DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") & Environment.NewLine & "Errore in EsisteId: " & ex.Message & Environment.NewLine & ex.StackTrace)
                    Return False
                End Try
            End SyncLock
        End Function

        Public Sub SalvaConfigurazioneEsterna()
            SyncLock _fileLock
                Try
                    Dim percorso As String = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "wingabriel", "config", "config.xml")
                    Dim directoryBackup As String = Path.GetDirectoryName(percorso)
                    If Not Directory.Exists(directoryBackup) Then
                        Directory.CreateDirectory(directoryBackup)
                    End If
                    AssicuraRoot()
                    _xmlDoc.Save(percorso)
                Catch ex As Exception
                    log.ScriviLog(DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") & Environment.NewLine & "Errore in SalvaConfigurazioneEsterna: " & ex.Message & Environment.NewLine & ex.StackTrace)
                End Try
            End SyncLock
        End Sub

    End Class

    Private Shared Function Gestore() As GestoreCondivisoXml
        Dim g As GestoreCondivisoXml = GestoreCondivisoXml.Istanza
        g.RicaricaSeCambiato()
        Return g
    End Function

    Public Sub New()
        Dim inizializzazione = GestoreCondivisoXml.Istanza
    End Sub

    Public Function AggiungiValore(id As String, valore As String) As Boolean
        Return Gestore().AggiungiValore(id, valore)
    End Function

    Public Function AggiungiValoreMultiplo(id As String, valore As String) As String
        Return Gestore().AggiungiValoreMultiplo(id, valore)
    End Function

    Public Function AggiungiValori(id As String, ParamArray valori() As String) As List(Of String)
        Return Gestore().AggiungiValori(id, valori)
    End Function

    Public Function LeggiValoriMultipli(id As String) As List(Of String)
        Return Gestore().LeggiValoriMultipli(id)
    End Function

    Public Function LeggiValori(id As String) As List(Of String)
        Return Gestore().LeggiValori(id)
    End Function

    Public Function LeggiValore(id As String, Optional indice As Integer = 0) As String
        Return Gestore().LeggiValore(id, indice)
    End Function

    Public Function SalvaValore(id As String, valore As String) As Boolean
        Return Gestore().AggiungiValore(id, valore)
    End Function

    Public Sub EliminaValore(id As String)
        Gestore().EliminaValore(id)
    End Sub

    Public Sub EliminaValoreMultiplo(id As String, valore As String)
        Gestore().EliminaValoreMultiplo(id, valore)
    End Sub

    Public Sub EliminaValorePerIdAutogenerato(id As String, idAutogenerato As String)
        Gestore().EliminaValorePerIdAutogenerato(id, idAutogenerato)
    End Sub

    Public Sub ModificaValore(id As String, nuovoValore As String)
        Gestore().ModificaValore(id, nuovoValore)
    End Sub

    Public Function EsisteId(id As String) As Boolean
        Return Gestore().EsisteId(id)
    End Function

    Public Sub SalvaConfigurazione()
        Gestore().SalvaConfigurazioneEsterna()
    End Sub

End Class
