Imports System.IO
Imports System.Xml

''' <summary>
''' La configurazione condivisa con WinGabriel. È lo stesso file di WGConfig (dati\config\config.xml, nella cartella del
''' programma) e lo stesso formato, ma letto e scritto da qui, così il lettore di schermo non ha bisogno di wgcontrols.dll
''' per funzionare da solo. Quando i due programmi stanno nella stessa cartella vedono le stesse impostazioni.
''' Prima di ogni lettura o scrittura il file viene riletto se è cambiato: un programma non sovrascrive mai le modifiche
''' fatte dall'altro nel frattempo.
''' </summary>
Public NotInheritable Class ConfigLettore

    Private Const IdCartellaBaseDati As String = "WGDb.CartellaBaseDati"

    Private Shared ReadOnly _blocco As New Object()
    Private Shared _documento As XmlDocument
    Private Shared _ultimaModifica As DateTime = DateTime.MinValue
    Private Shared _percorsoPersonalizzato As String = ""

    Private Sub New()
    End Sub

    ''' <summary>Per usare un file di configurazione diverso da quello accanto al programma (per esempio nel desktop sicuro).</summary>
    Public Shared Sub ImpostaPercorso(percorso As String)
        SyncLock _blocco
            _percorsoPersonalizzato = If(percorso, "")
            _percorsoRisolto = ""
            _documento = Nothing
            _ultimaModifica = DateTime.MinValue
        End SyncLock
    End Sub

    Private Const ChiaveRegistro As String = "Software\WinGabriel"
    Private Const ValoreRegistro As String = "PercorsoConfigurazione"
    Private Shared _percorsoRisolto As String = ""

    ''' <summary>
    ''' Il file di configurazione in uso. Se è stato indicato (/config) vale quello. Altrimenti, nell'ordine: il puntatore che WinGabriel e
    ''' il lettore tengono nel registro dell'utente, se il file esiste (così i due programmi condividono le impostazioni anche in cartelle
    ''' diverse, e chi arriva dopo usa quelle di chi c'era già); il config.xml accanto al programma, se esiste o se la cartella è
    ''' scrivibile; in ultimo AppData\WGLettore. Il lettore lascia il puntatore dove l'ha trovato, o lo scrive se manca.
    ''' </summary>
    Public Shared Function Percorso() As String
        If _percorsoPersonalizzato <> "" Then Return _percorsoPersonalizzato
        If _percorsoRisolto <> "" Then Return _percorsoRisolto
        Dim propria As String = Path.Combine(CartellaConfigurazioneProgramma(), "config.xml")
        Dim scelta As String = ""
        Try
            Dim condiviso As String = LeggiPuntatore()
            If condiviso <> "" AndAlso File.Exists(condiviso) Then
                scelta = condiviso
            ElseIf File.Exists(propria) OrElse CartellaScrivibile(Path.GetDirectoryName(propria)) Then
                scelta = propria
                ScriviPuntatore(scelta)
            Else
                scelta = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "WGLettore", "config.xml")
                ScriviPuntatore(scelta)
            End If
        Catch
            scelta = propria
        End Try
        _percorsoRisolto = scelta
        Return scelta
    End Function

    ' La cartella della configurazione di WinGabriel: nei dati locali dell'utente se il programma è installato in Programmi\WinGabriel, altrimenti
    ' config accanto al programma. Le versioni di prima la tenevano in dati\config: se la nuova non c'è ancora si usa quella.
    Private Shared Function CartellaConfigurazioneProgramma() As String
        Try
            Dim programma As String = AppDomain.CurrentDomain.BaseDirectory.TrimEnd("\"c)
            For Each cartella As Environment.SpecialFolder In New Environment.SpecialFolder() {Environment.SpecialFolder.ProgramFiles, Environment.SpecialFolder.ProgramFilesX86}
                If String.Equals(programma, Path.Combine(Environment.GetFolderPath(cartella), "WinGabriel").TrimEnd("\"c), StringComparison.OrdinalIgnoreCase) Then
                    Return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "WinGabriel", "config")
                End If
            Next
            Dim nuova As String = Path.Combine(programma, "config")
            Dim vecchia As String = Path.Combine(programma, "dati", "config")
            If Not File.Exists(Path.Combine(nuova, "config.xml")) AndAlso File.Exists(Path.Combine(vecchia, "config.xml")) Then Return vecchia
            Return nuova
        Catch
            Return Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "config")
        End Try
    End Function

    Private Shared Function LeggiPuntatore() As String
        Try
            Using chiave As Microsoft.Win32.RegistryKey = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(ChiaveRegistro)
                If chiave Is Nothing Then Return ""
                Return Convert.ToString(chiave.GetValue(ValoreRegistro))
            End Using
        Catch
            Return ""
        End Try
    End Function

    Private Shared Sub ScriviPuntatore(percorso As String)
        Try
            Using chiave As Microsoft.Win32.RegistryKey = Microsoft.Win32.Registry.CurrentUser.CreateSubKey(ChiaveRegistro)
                chiave.SetValue(ValoreRegistro, percorso)
            End Using
        Catch
        End Try
    End Sub

    Private Shared Function CartellaScrivibile(cartella As String) As Boolean
        Try
            Directory.CreateDirectory(cartella)
            Dim prova As String = Path.Combine(cartella, ".prova" & Guid.NewGuid().ToString("N"))
            File.WriteAllText(prova, "")
            File.Delete(prova)
            Return True
        Catch
            Return False
        End Try
    End Function

    Public Shared Function Esiste(id As String) As Boolean
        SyncLock _blocco
            Return Nodo(id) IsNot Nothing
        End SyncLock
    End Function

    Public Shared Function Leggi(id As String, Optional predefinito As String = "") As String
        SyncLock _blocco
            Dim n As XmlNode = Nodo(id)
            If n Is Nothing Then Return predefinito
            Dim valore As XmlNode = n.SelectSingleNode("Valore[1]")
            Dim testo As String = If(valore IsNot Nothing, valore.InnerText, n.InnerText)
            Return If(testo, predefinito)
        End SyncLock
    End Function

    Public Shared Sub Scrivi(id As String, valore As String)
        If String.IsNullOrWhiteSpace(id) Then Return
        SyncLock _blocco
            Try
                Dim doc As XmlDocument = Documento()
                Dim n As XmlNode = Nodo(id)
                If n Is Nothing Then
                    Dim nuovo As XmlElement = doc.CreateElement("Configurazione")
                    nuovo.SetAttribute("id", id)
                    nuovo.InnerText = If(valore, "")
                    doc.DocumentElement.AppendChild(nuovo)
                Else
                    Dim primo As XmlNode = n.SelectSingleNode("Valore[1]")
                    If primo IsNot Nothing Then primo.InnerText = If(valore, "") Else n.InnerText = If(valore, "")
                End If
                Salva(doc)
            Catch ex As Exception
                RegistroErrori.Scrivi("ConfigLettore.Scrivi", ex)
            End Try
        End SyncLock
    End Sub

    Public Shared Sub Elimina(id As String)
        SyncLock _blocco
            Try
                Dim n As XmlNode = Nodo(id)
                If n Is Nothing OrElse n.ParentNode Is Nothing Then Return
                n.ParentNode.RemoveChild(n)
                Salva(_documento)
            Catch ex As Exception
                RegistroErrori.Scrivi("ConfigLettore.Elimina", ex)
            End Try
        End SyncLock
    End Sub

    ''' <summary>La cartella dei dati di WinGabriel (quella scelta dall'utente, se c'è, altrimenti dati accanto al programma) e una sottocartella.</summary>
    Public Shared Function CartellaDati(sottocartella As String) As String
        ' I dati stanno accanto alla configurazione in uso: dati\config\config.xml dà la cartella dati; altrimenti la cartella del file.
        Dim baseDati As String = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "dati")
        Try
            Dim cartellaConfig As String = Path.GetDirectoryName(Percorso())
            If String.Equals(Path.GetFileName(cartellaConfig), "config", StringComparison.OrdinalIgnoreCase) Then
                ' la configurazione sta accanto ai dati (versioni di prima: dati\config) o fuori (config accanto al programma)
                Dim sopra As String = Path.GetDirectoryName(cartellaConfig)
                baseDati = If(String.Equals(Path.GetFileName(sopra), "dati", StringComparison.OrdinalIgnoreCase), sopra, Path.Combine(sopra, "dati"))
            Else
                baseDati = cartellaConfig
            End If
        Catch ex As Exception
            RegistroErrori.Scrivi("ConfigLettore.CartellaDati", ex)
        End Try
        Try
            Dim scelta As String = Leggi(IdCartellaBaseDati, "").Trim()
            If scelta <> "" AndAlso Directory.Exists(scelta) Then
                Dim dedicata As String = Path.Combine(scelta, "WinGabriel")
                baseDati = If(Directory.Exists(dedicata), dedicata, scelta)
            End If
        Catch ex As Exception
            RegistroErrori.Scrivi("ConfigLettore.CartellaDati", ex)
        End Try
        Dim cartella As String = Path.Combine(baseDati, sottocartella)
        Try
            Directory.CreateDirectory(cartella)
        Catch ex As Exception
            RegistroErrori.Scrivi("ConfigLettore.CartellaDati", ex)
        End Try
        Return cartella
    End Function

    ''' <summary>Gli identificatori delle voci il cui nome comincia per il prefisso.</summary>
    Public Shared Function ChiaviConPrefisso(prefisso As String) As List(Of String)
        Dim elenco As New List(Of String)()
        SyncLock _blocco
            Try
                For Each n As XmlNode In Documento().DocumentElement.ChildNodes
                    Dim attributo As XmlAttribute = If(n.Attributes Is Nothing, Nothing, n.Attributes("id"))
                    If attributo IsNot Nothing AndAlso attributo.Value.StartsWith(prefisso, StringComparison.Ordinal) Then elenco.Add(attributo.Value)
                Next
            Catch ex As Exception
                RegistroErrori.Scrivi("ConfigLettore.ChiaviConPrefisso", ex)
            End Try
        End SyncLock
        Return elenco
    End Function

    ''' <summary>Scrive in un altro file di configurazione le sole voci il cui nome comincia per il prefisso (per esempio lettore_schermo_).</summary>
    Public Shared Sub EsportaPrefisso(prefisso As String, destinazione As String)
        SyncLock _blocco
            Try
                Dim origine As XmlDocument = Documento()
                Dim uscita As New XmlDocument()
                uscita.XmlResolver = Nothing
                uscita.AppendChild(uscita.CreateXmlDeclaration("1.0", "UTF-8", Nothing))
                Dim radice As XmlElement = uscita.CreateElement("Configurazioni")
                uscita.AppendChild(radice)
                For Each n As XmlNode In origine.DocumentElement.ChildNodes
                    Dim attributo As XmlAttribute = If(n.Attributes Is Nothing, Nothing, n.Attributes("id"))
                    If attributo Is Nothing OrElse Not attributo.Value.StartsWith(prefisso, StringComparison.Ordinal) Then Continue For
                    radice.AppendChild(uscita.ImportNode(n, True))
                Next
                Directory.CreateDirectory(Path.GetDirectoryName(destinazione))
                uscita.Save(destinazione)
            Catch ex As Exception
                RegistroErrori.Scrivi("ConfigLettore.EsportaPrefisso", ex)
            End Try
        End SyncLock
    End Sub

    Private Shared Function Nodo(id As String) As XmlNode
        Try
            Dim doc As XmlDocument = Documento()
            If doc Is Nothing OrElse doc.DocumentElement Is Nothing OrElse String.IsNullOrEmpty(id) Then Return Nothing
            For Each n As XmlNode In doc.DocumentElement.ChildNodes
                Dim attributo As XmlAttribute = If(n.Attributes Is Nothing, Nothing, n.Attributes("id"))
                If attributo IsNot Nothing AndAlso String.Equals(attributo.Value, id, StringComparison.Ordinal) Then Return n
            Next
        Catch ex As Exception
            RegistroErrori.Scrivi("ConfigLettore.Nodo", ex)
        End Try
        Return Nothing
    End Function

    Private Shared Function Documento() As XmlDocument
        Dim percorso As String = ConfigLettore.Percorso()
        Dim modifica As DateTime = DateTime.MinValue
        Try
            If File.Exists(percorso) Then modifica = File.GetLastWriteTimeUtc(percorso)
        Catch
        End Try
        If _documento IsNot Nothing AndAlso modifica = _ultimaModifica Then Return _documento
        Dim doc As New XmlDocument()
        doc.XmlResolver = Nothing
        Try
            If File.Exists(percorso) Then doc.Load(percorso)
        Catch ex As Exception
            RegistroErrori.Scrivi("ConfigLettore.Documento", ex)
            doc = New XmlDocument()
            doc.XmlResolver = Nothing
        End Try
        If doc.DocumentElement Is Nothing Then
            doc.RemoveAll()
            doc.AppendChild(doc.CreateXmlDeclaration("1.0", "UTF-8", Nothing))
            doc.AppendChild(doc.CreateElement("Configurazioni"))
        End If
        _documento = doc
        _ultimaModifica = modifica
        Return doc
    End Function

    Private Shared Sub Salva(doc As XmlDocument)
        Dim percorso As String = ConfigLettore.Percorso()
        Directory.CreateDirectory(Path.GetDirectoryName(percorso))
        Dim temporaneo As String = percorso & ".tmp"
        doc.Save(temporaneo)
        File.Copy(temporaneo, percorso, True)
        Try
            File.Delete(temporaneo)
        Catch
        End Try
        Try
            _ultimaModifica = File.GetLastWriteTimeUtc(percorso)
        Catch
        End Try
    End Sub

End Class
