Imports System
Imports System.IO

''' <summary>
''' Dove stanno le cose del programma che non sono dati dell'utente: la configurazione, i suoni e i programmi che WinGabriel scarica
''' (il riconoscimento degli spartiti, il traduttore, eSpeak). Nella cartella dei dati stanno solo i dati: spartiti scaricati e
''' riconosciuti, posta, rubrica, e così via.
'''
'''   Programma installato (in Programmi\WinGabriel):
'''       radice dell'applicazione = la cartella di installazione     configurazione = %LocalAppData%\WinGabriel\config
'''   Programma portatile (qualsiasi altra cartella):
'''       radice dell'applicazione = la cartella del programma       configurazione = config, accanto al programma
'''
''' Suoni e programmi stanno quindi sempre con il programma. Chi non può scrivere in Programmi (WinGabriel non avviato come amministratore)
''' ripiega su %LocalAppData%\WinGabriel. La cartella dei dati scelta dall'utente non c'entra e non sposta niente di quello che sta qui. Chi aggiorna dalla versione di prima trova le cartelle nei dati: la prima
''' volta si spostano da sole (MigraConfigurazione, MigraComponenti), senza sovrascrivere niente.
''' </summary>
Public NotInheritable Class WGPercorsiApplicazione

    Private Shared ReadOnly _blocco As New Object()
    Private Shared _configMigrata As Boolean
    Private Shared _componentiMigrati As Boolean

    ''' <summary>Le cartelle che contengono programmi e suoni: stanno nella radice dell'applicazione, non nei dati.</summary>
    Public Shared ReadOnly Componenti As String() = {"suoni", "audiveris", "traduttore", "espeak", "models", "trafilatura", "duckdb"}

    Private Sub New()
    End Sub

    Private Shared _logMigrati As Boolean
    Private Shared _cacheMigrata As Boolean
    Private Shared _browserMigrato As Boolean

    Private Shared Function CartellaLogSenzaSpostare() As String
        If EInstallato() Then Return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "WinGabriel", "logs")
        Return Path.Combine(CartellaProgramma(), "logs")
    End Function

    '
    ' La cartella dei log: nei dati locali dell'utente (%LocalAppData%\WinGabriel\logs) se il programma è installato, nella radice
    ' dell'applicazione (logs, accanto al programma) se è portatile. I log delle versioni di prima stavano in dati\logs: la prima volta si
    ' spostano qui, senza sovrascrivere niente.

    Public Shared Function CartellaLog() As String
        Dim c As String = CartellaLogSenzaSpostare()
        Try
            SyncLock _blocco
                If Not _logMigrati Then
                    _logMigrati = True
                    Sposta(Path.Combine(CartellaProgramma(), "dati", "logs"), c)
                End If
            End SyncLock
            If Not Directory.Exists(c) Then Directory.CreateDirectory(c)
        Catch
        End Try
        Return c
    End Function

    Private Shared Sub Scrivi(testo As String)
        Try
            Dim cartella As String = CartellaLogSenzaSpostare()
            If Not Directory.Exists(cartella) Then Directory.CreateDirectory(cartella)
            File.AppendAllText(Path.Combine(cartella, "log.txt"), DateTime.Now.ToString("dd/MM/yyyy HH:mm:ss") & ": WGPercorsiApplicazione: " & testo & Environment.NewLine)
        Catch
        End Try
    End Sub

    ''' <summary>La cartella del programma, senza barra finale.</summary>
    Public Shared Function CartellaProgramma() As String
        Return AppDomain.CurrentDomain.BaseDirectory.TrimEnd("\"c, "/"c)
    End Function

    Private Shared Function Uguali(a As String, b As String) As Boolean
        Try
            Return String.Equals(Path.GetFullPath(a).TrimEnd("\"c), Path.GetFullPath(b).TrimEnd("\"c), StringComparison.OrdinalIgnoreCase)
        Catch
            Return False
        End Try
    End Function

    ''' <summary>Vero se il programma gira dalla cartella di installazione, Programmi\WinGabriel.</summary>
    Public Shared Function EInstallato() As Boolean
        Try
            Dim programma As String = CartellaProgramma()
            For Each cartella As Environment.SpecialFolder In New Environment.SpecialFolder() {Environment.SpecialFolder.ProgramFiles, Environment.SpecialFolder.ProgramFilesX86}
                Dim programmi As String = Environment.GetFolderPath(cartella)
                If programmi <> "" AndAlso Uguali(programma, Path.Combine(programmi, "WinGabriel")) Then Return True
            Next
        Catch
        End Try
        Return False
    End Function

    ''' <summary>La radice dell'applicazione: dove stanno i suoni e i programmi scaricati.</summary>
    Public Shared Function RadiceApplicazione() As String
        Dim programma As String = CartellaProgramma()
        If Not EInstallato() OrElse ProgrammaScrivibile() Then Return programma
        ' in Programmi si scrive solo con i privilegi di amministratore: senza, suoni e programmi vanno nei dati locali dell'utente
        Return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "WinGabriel")
    End Function

    Private Shared _scrivibile As Integer = -1

    ' Vero se nella cartella del programma si può creare un file (una prova sola per esecuzione).
    Private Shared Function ProgrammaScrivibile() As Boolean
        If _scrivibile >= 0 Then Return _scrivibile = 1
        Dim ok As Boolean = False
        Try
            Dim prova As String = Path.Combine(CartellaProgramma(), "prova-scrittura-" & Guid.NewGuid().ToString("N") & ".tmp")
            File.WriteAllText(prova, "x")
            File.Delete(prova)
            ok = True
        Catch
        End Try
        _scrivibile = If(ok, 1, 0)
        Return ok
    End Function

    ''' <summary>La cartella della configurazione: nei dati locali dell'utente se il programma è installato, accanto al programma se è portatile.</summary>
    Public Shared Function CartellaConfig() As String
        Dim c As String
        If EInstallato() Then
            c = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "WinGabriel", "config")
        Else
            c = Path.Combine(CartellaProgramma(), "config")
        End If
        MigraConfigurazione(c)
        Return c
    End Function

    ' La cartella della cache dell'aggiornamento (impronte dei file, ultimo elenco ricevuto, file preparati): come la configurazione, nei dati
    ' locali dell'utente se il programma è installato, accanto al programma (cache) se è portatile. Prima stava in dati\cacke.
    Public Shared Function CartellaCache() As String
        Dim c As String
        If EInstallato() Then
            c = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "WinGabriel", "cache")
        Else
            c = Path.Combine(CartellaProgramma(), "cache")
        End If
        Try
            SyncLock _blocco
                If Not _cacheMigrata Then
                    _cacheMigrata = True
                    Sposta(Path.Combine(CartellaProgramma(), "dati", "cacke"), c)
                End If
            End SyncLock
            If Not Directory.Exists(c) Then Directory.CreateDirectory(c)
        Catch
        End Try
        Return c
    End Function

    ''' <summary>La cartella dei modelli di intelligenza artificiale scaricati (voce, riconoscimento del testo, traduzione): models, nella radice dell'applicazione.</summary>
    Public Shared Function CartellaModelli() As String
        Return CartellaComponente("models")
    End Function

    ' I dati del browser (profilo di WebView2: accessi, cookie, cache): come la configurazione, ognuno ha i suoi. Nei dati locali dell'utente
    ' (%LocalAppData%\WinGabriel\browser) se il programma è installato, nella cartella del programma (browser) se è portatile. Prima stavano
    ' in dati\browsercache.
    Public Shared Function CartellaDatiBrowser() As String
        Dim c As String
        If EInstallato() Then
            c = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "WinGabriel", "browser")
        Else
            c = Path.Combine(CartellaProgramma(), "browser")
        End If
        Try
            SyncLock _blocco
                If Not _browserMigrato Then
                    _browserMigrato = True
                    Sposta(Path.Combine(CartellaProgramma(), "dati", "browsercache"), c)
                    Sposta(Path.Combine(RadiceApplicazione(), "traduttore", "profilo"), Path.Combine(c, "traduttore"))
                End If
            End SyncLock
            If Not Directory.Exists(c) Then Directory.CreateDirectory(c)
        Catch
        End Try
        Return c
    End Function

    ''' <summary>Il config.xml.</summary>
    Public Shared Function PercorsoConfig() As String
        Return Path.Combine(CartellaConfig(), "config.xml")
    End Function

    ''' <summary>Il percorso di una cartella della radice dell'applicazione (suoni, audiveris, traduttore, espeak), creata se manca.</summary>
    Public Shared Function CartellaComponente(nome As String) As String
        MigraComponenti()
        Dim c As String = Path.Combine(RadiceApplicazione(), nome)
        Try
            If Not Directory.Exists(c) Then Directory.CreateDirectory(c)
        Catch ex As Exception
            Scrivi("CartellaComponente (" & nome & "): " & ex.Message)
        End Try
        Return c
    End Function

    ' ---- lo spostamento dalla versione di prima ------------------------------------------------------------------

    Private Shared Sub CopiaSenzaSovrascrivere(origine As String, destinazione As String)
        Directory.CreateDirectory(destinazione)
        For Each f As String In Directory.GetFiles(origine)
            Dim d As String = Path.Combine(destinazione, Path.GetFileName(f))
            If Not File.Exists(d) Then File.Copy(f, d)
        Next
        For Each sotto As String In Directory.GetDirectories(origine)
            CopiaSenzaSovrascrivere(sotto, Path.Combine(destinazione, Path.GetFileName(sotto)))
        Next
    End Sub

    ''' <summary>
    ''' Sposta una cartella: se la destinazione non c'è (o è vuota) la si sposta intera, che sullo stesso disco è istantaneo; altrimenti si copia
    ''' quello che manca. Vero se la cartella di prima non c'è più alla fine.
    ''' </summary>
    Private Shared Function Sposta(origine As String, destinazione As String) As Boolean
        Try
            If Not Directory.Exists(origine) OrElse Uguali(origine, destinazione) Then Return False
            Dim destinazioneVuota As Boolean = Not Directory.Exists(destinazione) OrElse (Directory.GetFileSystemEntries(destinazione).Length = 0)
            If destinazioneVuota Then
                Try
                    If Directory.Exists(destinazione) Then Directory.Delete(destinazione)
                    Dim superiore As String = Path.GetDirectoryName(destinazione)
                    If Not Directory.Exists(superiore) Then Directory.CreateDirectory(superiore)
                    Directory.Move(origine, destinazione)
                    Scrivi("spostata " & origine & " in " & destinazione)
                    Return True
                Catch ex As Exception
                    Scrivi("Sposta (rinomina) " & origine & ": " & ex.Message & ", la copio")
                End Try
            End If
            CopiaSenzaSovrascrivere(origine, destinazione)
            Try
                Directory.Delete(origine, True)
            Catch ex As Exception
                Scrivi("Sposta (pulizia) " & origine & ": " & ex.Message)
                Return False
            End Try
            Scrivi("copiata " & origine & " in " & destinazione)
            Return True
        Catch ex As Exception
            Scrivi("Sposta " & origine & ": " & ex.Message)
            Return False
        End Try
    End Function

    ''' <summary>La configurazione stava in dati\config, accanto al programma: la prima volta si sposta nella cartella nuova.</summary>
    Private Shared Sub MigraConfigurazione(nuova As String)
        SyncLock _blocco
            If _configMigrata Then Return
            _configMigrata = True
            Try
                Dim vecchia As String = Path.Combine(CartellaProgramma(), "dati", "config")
                If Directory.Exists(vecchia) AndAlso Not File.Exists(Path.Combine(nuova, "config.xml")) Then
                    Sposta(vecchia, nuova)
                End If
            Catch ex As Exception
                Scrivi("MigraConfigurazione: " & ex.Message)
            End Try
        End SyncLock
    End Sub

    ''' <summary>
    ''' Suoni, riconoscimento degli spartiti, traduttore ed eSpeak stavano nella cartella dei dati: la prima volta si spostano nella radice
    ''' dell'applicazione. Si cerca sia nei dati accanto al programma sia nella cartella dei dati scelta dall'utente, e nella vecchia
    ''' cartella dei suoni nei dati locali.
    ''' </summary>
    Private Shared Sub MigraComponenti()
        SyncLock _blocco
            If _componentiMigrati Then Return
            _componentiMigrati = True
            Try
                Dim radice As String = RadiceApplicazione()
                Dim basi As New System.Collections.Generic.List(Of String)()
                basi.Add(Path.Combine(CartellaProgramma(), "dati"))
                Try
                    Dim scelta As String = WGDatabase.CartellaBaseDatiEffettiva()
                    If Not String.IsNullOrWhiteSpace(scelta) AndAlso Not basi.Exists(Function(b) Uguali(b, scelta)) Then basi.Add(scelta)
                Catch ex As Exception
                    Scrivi("MigraComponenti (cartella dati): " & ex.Message)
                End Try
                For Each baseDati As String In basi
                    Sposta(Path.Combine(baseDati, "suoni"), Path.Combine(radice, "suoni"))
                    Sposta(Path.Combine(baseDati, "spartiti", "audiveris"), Path.Combine(radice, "audiveris"))
                    Sposta(Path.Combine(baseDati, "traduttore"), Path.Combine(radice, "traduttore"))
                    Sposta(Path.Combine(baseDati, "espeak"), Path.Combine(radice, "espeak"))
                Next
                ' i modelli di intelligenza artificiale stavano sparsi accanto al programma: adesso stanno tutti in models
                Dim programma As String = CartellaProgramma()
                Dim models As String = Path.Combine(radice, "models")
                Sposta(Path.Combine(programma, "modelli"), models)
                Sposta(Path.Combine(programma, "vosk"), Path.Combine(models, "vosk"))
                Sposta(Path.Combine(programma, "assistente"), Path.Combine(models, "assistente"))
                Sposta(Path.Combine(programma, "tessdata"), Path.Combine(models, "tessdata"))
                Sposta(Path.Combine(radice, "traduttore", "modelli"), Path.Combine(models, "traduttore"))
                ' le versioni che li tenevano nei dati locali dell'utente (e chi, in Programmi, non poteva scrivere e ripiegava lì)
                Dim locale As String = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "WinGabriel")
                For Each nome As String In Componenti
                    Sposta(Path.Combine(locale, nome), Path.Combine(radice, nome))
                Next
            Catch ex As Exception
                Scrivi("MigraComponenti: " & ex.Message)
            End Try
        End SyncLock
    End Sub

End Class
