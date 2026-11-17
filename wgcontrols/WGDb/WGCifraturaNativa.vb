Imports System
Imports System.IO
Imports System.Runtime.InteropServices
Imports System.Text

''' <summary>
''' Ponte verso la libreria nativa di cifratura (WGCifratura32.dll / WGCifratura64.dll, sorgenti in WGCifraturaNativa).
''' Tutta la cifratura di WinGabriel passa da qui: il codice gestito si decompila, quello nativo no.
'''   Senza password: la chiave dipende da uno "scopo" (una parola che dice a cosa serve il dato) e da un segreto dentro la libreria.
'''                   Il dato si legge su qualunque computer e utente di Windows, quindi si esporta e si sincronizza.
'''   Con password:   la chiave deriva dalla password: senza non si apre.
''' I testi protetti iniziano con "WGX:" seguito dal blob in base64.
''' </summary>
Public NotInheritable Class WGCifraturaNativa

    Public Const PrefissoTesto As String = "WGX:"

    Public Const ErroreParametri As Integer = -1
    Public Const ErroreBufferPiccolo As Integer = -2
    Public Const ErroreCifratura As Integer = -3
    Public Const ErroreAutenticazione As Integer = -4
    Public Const ErroreFile As Integer = -5
    Public Const ErroreFormato As Integer = -6
    Public Const ErroreLibreria As Integer = -100

    <UnmanagedFunctionPointer(CallingConvention.StdCall)>
    Private Delegate Function DVersione() As Integer

    <UnmanagedFunctionPointer(CallingConvention.StdCall)>
    Private Delegate Function DCasuali(buf As Byte(), n As Integer) As Integer

    <UnmanagedFunctionPointer(CallingConvention.StdCall)>
    Private Delegate Function DPbkdf2(pw As Byte(), pwLen As Integer, sale As Byte(), saleLen As Integer, iter As Integer, out As Byte(), outLen As Integer) As Integer

    <UnmanagedFunctionPointer(CallingConvention.StdCall)>
    Private Delegate Function DChiaveIncorporata(<MarshalAs(UnmanagedType.LPStr)> scopo As String, out32 As Byte()) As Integer

    <UnmanagedFunctionPointer(CallingConvention.StdCall)>
    Private Delegate Function DGcm(chiave As Byte(), dati As Byte(), n As Integer, aad As Byte(), aadLen As Integer, out As Byte(), outCap As Integer) As Integer

    <UnmanagedFunctionPointer(CallingConvention.StdCall)>
    Private Delegate Function DBlob(<MarshalAs(UnmanagedType.LPStr)> scopo As String, pw As Byte(), pwLen As Integer, dati As Byte(), n As Integer, out As Byte(), outCap As Integer) As Integer

    <UnmanagedFunctionPointer(CallingConvention.StdCall)>
    Private Delegate Function DFile(<MarshalAs(UnmanagedType.LPWStr)> origine As String, <MarshalAs(UnmanagedType.LPWStr)> destinazione As String, pw As Byte(), pwLen As Integer) As Integer

    <UnmanagedFunctionPointer(CallingConvention.StdCall)>
    Private Delegate Function DUltimoErrore(buf As System.Text.StringBuilder, cap As Integer) As Integer

    <DllImport("kernel32.dll", CharSet:=CharSet.Unicode, SetLastError:=True)>
    Private Shared Function LoadLibraryW(nomeFile As String) As IntPtr
    End Function

    <DllImport("kernel32.dll", CharSet:=CharSet.Ansi, ExactSpelling:=True, SetLastError:=True)>
    Private Shared Function GetProcAddress(modulo As IntPtr, nome As String) As IntPtr
    End Function

    Private Shared ReadOnly _blocco As New Object()
    Private Shared _provato As Boolean = False
    Private Shared _caricata As Boolean = False
    Private Shared _errore As String = ""
    Private Shared _casuali As DCasuali
    Private Shared _pbkdf2 As DPbkdf2
    Private Shared _chiaveIncorporata As DChiaveIncorporata
    Private Shared _gcmCifra As DGcm
    Private Shared _gcmDecifra As DGcm
    Private Shared _proteggi As DBlob
    Private Shared _rivela As DBlob
    Private Shared _cifraFile As DFile
    Private Shared _decifraFile As DFile
    Private Shared _ultimoErrore As DUltimoErrore
    Private Shared _controllato As Boolean = False
    Private Shared _funzionante As Boolean = False
    Private Shared _motivoNonFunzionante As String = ""

    Private Shared ReadOnly _registro As New LoggerWGDb()

    Private Sub New()
    End Sub

    Private Shared Sub Registra(messaggio As String)
        Try
            _registro.ScriviLog(messaggio)
        Catch exLog As Exception
            RegistroErrori.Registra("WGCifraturaNativa.Registra", exLog)
        End Try
    End Sub

    Private Shared Function Funzione(Of T As Class)(modulo As IntPtr, nome As String) As T
        Try
            Dim indirizzo As IntPtr = GetProcAddress(modulo, nome)
            If indirizzo = IntPtr.Zero Then Throw New EntryPointNotFoundException("Nella libreria di cifratura manca " & nome)
            Return CType(CType(Marshal.GetDelegateForFunctionPointer(indirizzo, GetType(T)), Object), T)
        Catch ex As Exception
            RegistroErrori.Registra("WGCifraturaNativa.Funzione", ex)
            Throw
        End Try
    End Function

    Private Shared Function Cartelle() As String()
        Try
            Dim elenco As New System.Collections.Generic.List(Of String)()
            Try
                Dim a As String = AppDomain.CurrentDomain.BaseDirectory
                If Not String.IsNullOrEmpty(a) Then elenco.Add(a)
            Catch exLog As Exception
                RegistroErrori.Registra("WGCifraturaNativa.Cartelle", exLog)
            End Try
            Try
                Dim b As String = Path.GetDirectoryName(GetType(WGCifraturaNativa).Assembly.Location)
                If Not String.IsNullOrEmpty(b) AndAlso Not elenco.Contains(b) Then elenco.Add(b)
            Catch exLog As Exception
                RegistroErrori.Registra("WGCifraturaNativa.Cartelle", exLog)
            End Try
            Return elenco.ToArray()
        Catch ex As Exception
            RegistroErrori.Registra("WGCifraturaNativa.Cartelle", ex)
            Throw
        End Try
    End Function

    Private Shared Sub Carica()
        Try
            If _provato Then Return
            SyncLock _blocco
                If _provato Then Return
                Try
                    Dim nome As String = "WGCifratura" & If(Environment.Is64BitProcess, "64", "32") & ".dll"
                    Dim modulo As IntPtr = IntPtr.Zero
                    Dim cercati As New System.Text.StringBuilder()
                    For Each cartella As String In Cartelle()
                        Dim percorso As String = Path.Combine(cartella, nome)
                        If File.Exists(percorso) Then
                            modulo = LoadLibraryW(percorso)
                            If modulo <> IntPtr.Zero Then Exit For
                            cercati.Append("non caricabile, errore Windows " & Marshal.GetLastWin32Error())
                        Else
                            If cercati.Length = 0 Then cercati.Append("file non trovato")
                        End If
                    Next
                    If modulo = IntPtr.Zero Then
                        _errore = nome & ": " & cercati.ToString()
                        Registra("WGCifraturaNativa: libreria non caricata (" & _errore & ")")
                        Return
                    End If
                    Dim versione As DVersione = Funzione(Of DVersione)(modulo, "WGC_Versione")
                    If versione() <> 1 Then
                        _errore = "Versione della libreria di cifratura non compatibile."
                        Registra("WGCifraturaNativa.Carica: " & _errore)
                        Return
                    End If
                    _casuali = Funzione(Of DCasuali)(modulo, "WGC_Casuali")
                    _pbkdf2 = Funzione(Of DPbkdf2)(modulo, "WGC_Pbkdf2")
                    _chiaveIncorporata = Funzione(Of DChiaveIncorporata)(modulo, "WGC_ChiaveIncorporata")
                    _gcmCifra = Funzione(Of DGcm)(modulo, "WGC_GcmCifra")
                    _gcmDecifra = Funzione(Of DGcm)(modulo, "WGC_GcmDecifra")
                    _proteggi = Funzione(Of DBlob)(modulo, "WGC_Proteggi")
                    _rivela = Funzione(Of DBlob)(modulo, "WGC_Rivela")
                    _cifraFile = Funzione(Of DFile)(modulo, "WGC_CifraFile")
                    _decifraFile = Funzione(Of DFile)(modulo, "WGC_DecifraFile")
                    _ultimoErrore = Funzione(Of DUltimoErrore)(modulo, "WGC_UltimoErrore")
                    _caricata = True
                Catch ex As Exception
                    _errore = ex.Message
                    Registra("WGCifraturaNativa.Carica: " & Breve(ex))
                Finally
                    _provato = True
                End Try
            End SyncLock
        Catch ex As Exception
            RegistroErrori.Registra("WGCifraturaNativa.Carica", ex)
            Throw
        End Try
    End Sub

    ''' <summary>Vero se la libreria nativa è stata caricata e funziona.</summary>
    Public Shared ReadOnly Property Disponibile As Boolean
        Get
            Carica()
            Return _caricata
        End Get
    End Property

    Public Shared ReadOnly Property ErroreCaricamento As String
        Get
            Carica()
            Return _errore
        End Get
    End Property

    ' Ultimo errore interno della libreria nativa su questo thread: il passo fallito e il codice di Windows.
    Public Shared Function DettaglioNativo() As String
        Try
            If Not _caricata OrElse _ultimoErrore Is Nothing Then Return "libreria non caricata"
            Dim testo As New System.Text.StringBuilder(200)
            _ultimoErrore(testo, 200)
            Return testo.ToString()
        Catch ex As Exception
            RegistroErrori.Registra("WGCifraturaNativa.DettaglioNativo", ex)
            Return "dettaglio non leggibile: " & ex.Message
        End Try
    End Function

    Public Shared Function DescriviCodice(codice As Integer) As String
        Try
            Select Case codice
                Case ErroreParametri : Return "parametri non validi"
                Case ErroreBufferPiccolo : Return "buffer troppo piccolo"
                Case ErroreCifratura : Return "errore di Windows"
                Case ErroreAutenticazione : Return "password o chiave sbagliata, o dati alterati"
                Case ErroreFile : Return "errore di file"
                Case ErroreFormato : Return "formato o modo diverso"
                Case ErroreLibreria : Return "libreria non disponibile"
                Case Else : Return "codice " & codice
            End Select
        Catch ex As Exception
            RegistroErrori.Registra("WGCifraturaNativa.DescriviCodice", ex)
            Throw
        End Try
    End Function

    Private Shared Function Breve(ex As Exception) As String
        Try
            Return ex.GetType().Name & ": " & ex.Message
        Catch exErr As Exception
            RegistroErrori.Registra("WGCifraturaNativa.Breve", exErr)
            Throw
        End Try
    End Function

    Private Shared Sub Fallimento(funzione As String, codice As Integer)
        Try
            Registra("WGCifraturaNativa." & funzione & ": " & DescriviCodice(codice) & "; " & DettaglioNativo())
        Catch ex As Exception
            RegistroErrori.Registra("WGCifraturaNativa.Fallimento", ex)
            Throw
        End Try
    End Sub

    ' Vero solo se la libreria e caricata e ha superato un autocontrollo (PBKDF2 uguale a quello di .NET, AES-GCM andata e ritorno,
    ' alterazione riconosciuta, cifratura senza password). Chi sceglie il formato nuovo deve usare questa: se e falsa si resta nel
    ' sistema precedente. Il motivo preciso e nel log e in MotivoNonFunzionante.
    Public Shared ReadOnly Property Funzionante As Boolean
        Get
            If _controllato Then Return _funzionante
            SyncLock _blocco
                If _controllato Then Return _funzionante
                Try
                    If Not Disponibile Then
                        _motivoNonFunzionante = _errore
                    Else
                        _motivoNonFunzionante = Autocontrollo()
                    End If
                    _funzionante = _motivoNonFunzionante = ""
                    If _funzionante Then
                        Registra("WGCifraturaNativa: libreria verificata (" & If(Environment.Is64BitProcess, "64", "32") & " bit).")
                    Else
                        Registra("WGCifraturaNativa: libreria non utilizzabile (" & _motivoNonFunzionante & "), si usa il sistema precedente.")
                    End If
                Catch ex As Exception
                    _funzionante = False
                    _motivoNonFunzionante = "autocontrollo interrotto, " & Breve(ex)
                    Registra("WGCifraturaNativa: libreria non utilizzabile (" & _motivoNonFunzionante & "), si usa il sistema precedente.")
                Finally
                    _controllato = True
                End Try
            End SyncLock
            Return _funzionante
        End Get
    End Property

    Public Shared ReadOnly Property MotivoNonFunzionante As String
        Get
            Dim ok As Boolean = Funzionante
            Return _motivoNonFunzionante
        End Get
    End Property

    ' Restituisce "" se tutto torna, altrimenti il passo che non torna.
    Private Shared Function Autocontrollo() As String
        Try
            Dim sale As Byte() = Casuali(16)
            If sale Is Nothing Then Return "numeri casuali: " & DettaglioNativo()

            Dim nativa As Byte() = DerivaPbkdf2("controllo", sale, 1000, 64)
            If nativa Is Nothing Then Return "PBKDF2 nativo non riuscito: " & DettaglioNativo()
            Dim gestita As Byte()
            Using kdf As New System.Security.Cryptography.Rfc2898DeriveBytes("controllo", sale, 1000, System.Security.Cryptography.HashAlgorithmName.SHA256)
                gestita = kdf.GetBytes(64)
            End Using
            If Convert.ToBase64String(nativa) <> Convert.ToBase64String(gestita) Then Return "PBKDF2 nativo diverso da quello di .NET"

            Dim chiave As Byte() = Casuali(32)
            If chiave Is Nothing Then Return "chiave casuale: " & DettaglioNativo()
            Dim chiaro As Byte() = Encoding.UTF8.GetBytes("prova di cifratura " & Convert.ToBase64String(sale))
            Dim cifrato As Byte() = CifraConChiave(chiave, chiaro)
            If cifrato Is Nothing Then Return "AES-GCM, cifratura non riuscita: " & DettaglioNativo()
            Dim riletto As Byte() = DecifraConChiave(chiave, cifrato)
            If riletto Is Nothing Then Return "AES-GCM, decifratura non riuscita: " & DettaglioNativo()
            If Convert.ToBase64String(riletto) <> Convert.ToBase64String(chiaro) Then Return "AES-GCM, il testo riletto e diverso"
            cifrato(cifrato.Length - 1) = CByte(cifrato(cifrato.Length - 1) Xor 1)
            If DecifraConChiave(chiave, cifrato) IsNot Nothing Then Return "AES-GCM, un dato alterato non e stato riconosciuto"

            Dim blob As Byte() = Proteggi("autocontrollo", chiaro)
            If blob Is Nothing Then Return "cifratura senza password non riuscita: " & DettaglioNativo()
            Dim esito As Integer = 0
            Dim aperto As Byte() = Rivela("autocontrollo", blob, "", esito)
            If aperto Is Nothing Then Return "lettura senza password non riuscita: " & DescriviCodice(esito) & "; " & DettaglioNativo()
            If Convert.ToBase64String(aperto) <> Convert.ToBase64String(chiaro) Then Return "cifratura senza password, il testo riletto e diverso"
            Return ""
        Catch ex As Exception
            RegistroErrori.Registra("WGCifraturaNativa.Autocontrollo", ex)
            Throw
        End Try
    End Function

    Private Shared Function PasswordInByte(password As String) As Byte()
        Try
            If String.IsNullOrEmpty(password) Then Return New Byte() {}
            Return Encoding.UTF8.GetBytes(password)
        Catch ex As Exception
            RegistroErrori.Registra("WGCifraturaNativa.PasswordInByte", ex)
            Throw
        End Try
    End Function

    ' ------------------------------------------------------------------------------------------
    ' Primitive
    ' ------------------------------------------------------------------------------------------

    Public Shared Function Casuali(quanti As Integer) As Byte()
        Try
            If Not Disponibile OrElse quanti < 0 Then Return Nothing
            Dim buf(quanti - 1) As Byte
            If quanti = 0 Then Return buf
            If _casuali(buf, quanti) <> 0 Then Return Nothing
            Return buf
        Catch ex As Exception
            Registra("WGCifraturaNativa.Casuali: " & Breve(ex))
            Return Nothing
        End Try
    End Function

    ''' <summary>PBKDF2-HMAC-SHA256. Dà gli stessi byte di Rfc2898DeriveBytes con SHA256.</summary>
    Public Shared Function DerivaPbkdf2(password As String, sale As Byte(), iterazioni As Integer, lunghezza As Integer) As Byte()
        Try
            If Not Disponibile OrElse String.IsNullOrEmpty(password) OrElse sale Is Nothing OrElse sale.Length = 0 Then Return Nothing
            Dim pw As Byte() = Encoding.UTF8.GetBytes(password)
            Dim uscita(lunghezza - 1) As Byte
            Dim r As Integer = _pbkdf2(pw, pw.Length, sale, sale.Length, iterazioni, uscita, lunghezza)
            Array.Clear(pw, 0, pw.Length)
            If r <> 0 Then
                Fallimento("DerivaPbkdf2", r)
                Return Nothing
            End If
            Return uscita
        Catch ex As Exception
            Registra("WGCifraturaNativa.DerivaPbkdf2: " & Breve(ex))
            Return Nothing
        End Try
    End Function

    ''' <summary>Chiave di 32 byte legata a uno scopo, derivata dal segreto della libreria.</summary>
    Public Shared Function ChiaveScopo(scopo As String) As Byte()
        Try
            If Not Disponibile Then Return Nothing
            Dim chiave(31) As Byte
            If _chiaveIncorporata(If(scopo, ""), chiave) <> 0 Then Return Nothing
            Return chiave
        Catch ex As Exception
            Registra("WGCifraturaNativa.ChiaveScopo: " & Breve(ex))
            Return Nothing
        End Try
    End Function

    ''' <summary>AES-256-GCM con una chiave di 32 byte. Il risultato è nonce(12) + cifrato + tag(16). Nothing se fallisce.</summary>
    Public Shared Function CifraConChiave(chiave As Byte(), chiaro As Byte(), Optional aad As Byte() = Nothing) As Byte()
        Try
            If Not Disponibile OrElse chiave Is Nothing OrElse chiave.Length <> 32 OrElse chiaro Is Nothing Then Return Nothing
            If aad Is Nothing Then aad = New Byte() {}
            Dim uscita(chiaro.Length + 27) As Byte
            Dim r As Integer = _gcmCifra(chiave, chiaro, chiaro.Length, aad, aad.Length, uscita, uscita.Length)
            If r < 0 Then
                Fallimento("CifraConChiave", r)
                Return Nothing
            End If
            If r <> uscita.Length Then Array.Resize(uscita, r)
            Return uscita
        Catch ex As Exception
            Registra("WGCifraturaNativa.CifraConChiave: " & Breve(ex))
            Return Nothing
        End Try
    End Function

    ''' <summary>Inverso di CifraConChiave. Nothing se la chiave è sbagliata o i dati sono alterati.</summary>
    Public Shared Function DecifraConChiave(chiave As Byte(), cifrato As Byte(), Optional aad As Byte() = Nothing) As Byte()
        Try
            If Not Disponibile OrElse chiave Is Nothing OrElse chiave.Length <> 32 OrElse cifrato Is Nothing OrElse cifrato.Length < 28 Then Return Nothing
            If aad Is Nothing Then aad = New Byte() {}
            Dim uscita(cifrato.Length - 28) As Byte
            Dim r As Integer = _gcmDecifra(chiave, cifrato, cifrato.Length, aad, aad.Length, uscita, uscita.Length)
            If r < 0 Then
                If r <> ErroreAutenticazione Then Fallimento("DecifraConChiave", r)
                Return Nothing
            End If
            If r <> uscita.Length Then Array.Resize(uscita, r)
            Return uscita
        Catch ex As Exception
            Registra("WGCifraturaNativa.DecifraConChiave: " & Breve(ex))
            Return Nothing
        End Try
    End Function

    ' ------------------------------------------------------------------------------------------
    ' Blob e testi, senza password o con password
    ' ------------------------------------------------------------------------------------------

    ''' <summary>Protegge dei byte. password vuota: senza password, con la chiave dello scopo. Nothing se fallisce.</summary>
    Public Shared Function Proteggi(scopo As String, chiaro As Byte(), Optional password As String = "") As Byte()
        Try
            If Not Disponibile OrElse chiaro Is Nothing Then Return Nothing
            Dim pw As Byte() = PasswordInByte(password)
            Dim uscita(chiaro.Length + 63) As Byte
            Dim r As Integer = _proteggi(If(scopo, ""), pw, pw.Length, chiaro, chiaro.Length, uscita, uscita.Length)
            If pw.Length > 0 Then Array.Clear(pw, 0, pw.Length)
            If r < 0 Then
                Fallimento("Proteggi", r)
                Return Nothing
            End If
            If r <> uscita.Length Then Array.Resize(uscita, r)
            Return uscita
        Catch ex As Exception
            Registra("WGCifraturaNativa.Proteggi: " & Breve(ex))
            Return Nothing
        End Try
    End Function

    ''' <summary>
    ''' Apre dei byte protetti. esito: 0 riuscito, ErroreAutenticazione password sbagliata o dati alterati,
    ''' ErroreFormato serve la password (o non serve), ErroreLibreria libreria assente.
    ''' </summary>
    Public Shared Function Rivela(scopo As String, protetto As Byte(), password As String, ByRef esito As Integer) As Byte()
        Try
            esito = 0
            Try
                If Not Disponibile Then
                    esito = ErroreLibreria
                    Return Nothing
                End If
                If protetto Is Nothing OrElse protetto.Length < 4 Then
                    esito = ErroreFormato
                    Return Nothing
                End If
                Dim pw As Byte() = PasswordInByte(password)
                Dim uscita(protetto.Length) As Byte
                Dim r As Integer = _rivela(If(scopo, ""), pw, pw.Length, protetto, protetto.Length, uscita, uscita.Length)
                If pw.Length > 0 Then Array.Clear(pw, 0, pw.Length)
                If r < 0 Then
                    esito = r
                    If r <> ErroreAutenticazione AndAlso r <> ErroreFormato Then Fallimento("Rivela", r)
                    Return Nothing
                End If
                If r <> uscita.Length Then Array.Resize(uscita, r)
                Return uscita
            Catch ex As Exception
                esito = ErroreCifratura
                Registra("WGCifraturaNativa.Rivela: " & Breve(ex))
                Return Nothing
            End Try
        Catch ex As Exception
            RegistroErrori.Registra("WGCifraturaNativa.Rivela", ex)
            Throw
        End Try
    End Function

    Public Shared Function EProtetto(valore As String) As Boolean
        Try
            Return Not String.IsNullOrEmpty(valore) AndAlso valore.StartsWith(PrefissoTesto, StringComparison.Ordinal)
        Catch ex As Exception
            RegistroErrori.Registra("WGCifraturaNativa.EProtetto", ex)
            Throw
        End Try
    End Function

    ''' <summary>Protegge un testo e lo restituisce come "WGX:" + base64. Stringa vuota se il testo è vuoto o la cifratura fallisce.</summary>
    Public Shared Function ProteggiTesto(scopo As String, testo As String, Optional password As String = "") As String
        Try
            If String.IsNullOrEmpty(testo) Then Return ""
            Dim blob As Byte() = Proteggi(scopo, Encoding.UTF8.GetBytes(testo), password)
            If blob Is Nothing Then Return ""
            Return PrefissoTesto & Convert.ToBase64String(blob)
        Catch ex As Exception
            Registra("WGCifraturaNativa.ProteggiTesto: " & Breve(ex))
            Return ""
        End Try
    End Function

    ''' <summary>Apre un testo "WGX:...". Stringa vuota se non si riesce (esito dice perché).</summary>
    Public Shared Function RivelaTesto(scopo As String, protetto As String, password As String, ByRef esito As Integer) As String
        Try
            esito = 0
            Try
                If Not EProtetto(protetto) Then
                    esito = ErroreFormato
                    Return ""
                End If
                Dim blob As Byte() = Convert.FromBase64String(protetto.Substring(PrefissoTesto.Length))
                Dim chiaro As Byte() = Rivela(scopo, blob, password, esito)
                If chiaro Is Nothing Then Return ""
                Return Encoding.UTF8.GetString(chiaro)
            Catch ex As Exception
                esito = ErroreFormato
                Registra("WGCifraturaNativa.RivelaTesto: " & Breve(ex))
                Return ""
            End Try
        Catch ex As Exception
            RegistroErrori.Registra("WGCifraturaNativa.RivelaTesto", ex)
            Throw
        End Try
    End Function

    Public Shared Function RivelaTesto(scopo As String, protetto As String) As String
        Try
            Dim esito As Integer = 0
            Return RivelaTesto(scopo, protetto, "", esito)
        Catch ex As Exception
            RegistroErrori.Registra("WGCifraturaNativa.RivelaTesto", ex)
            Throw
        End Try
    End Function

    ' ------------------------------------------------------------------------------------------
    ' File (esportazioni): a blocchi, senza tenere tutto in memoria
    ' ------------------------------------------------------------------------------------------

    ''' <summary>Cifra un file. password vuota: senza password. Restituisce 0 se riuscito, altrimenti un codice di errore.</summary>
    Public Shared Function CifraFile(origine As String, destinazione As String, Optional password As String = "") As Integer
        Try
            If Not Disponibile Then Return ErroreLibreria
            Dim pw As Byte() = PasswordInByte(password)
            Dim r As Integer = _cifraFile(origine, destinazione, pw, pw.Length)
            If pw.Length > 0 Then Array.Clear(pw, 0, pw.Length)
            If r <> 0 Then Fallimento("CifraFile", r)
            Return r
        Catch ex As Exception
            Registra("WGCifraturaNativa.CifraFile: " & Breve(ex))
            Return ErroreCifratura
        End Try
    End Function

    ''' <summary>Apre un file cifrato con CifraFile. Restituisce 0 se riuscito; ErroreAutenticazione se la password è sbagliata.</summary>
    Public Shared Function DecifraFile(origine As String, destinazione As String, Optional password As String = "") As Integer
        Try
            If Not Disponibile Then Return ErroreLibreria
            Dim pw As Byte() = PasswordInByte(password)
            Dim r As Integer = _decifraFile(origine, destinazione, pw, pw.Length)
            If pw.Length > 0 Then Array.Clear(pw, 0, pw.Length)
            If r <> 0 AndAlso r <> ErroreAutenticazione AndAlso r <> ErroreFormato Then Fallimento("DecifraFile", r)
            Return r
        Catch ex As Exception
            Registra("WGCifraturaNativa.DecifraFile: " & Breve(ex))
            Return ErroreCifratura
        End Try
    End Function

    ''' <summary>Dice se un file è cifrato con CifraFile: "" no, "N" senza password, "P" con password.</summary>
    Public Shared Function TipoFileCifrato(percorso As String) As String
        Try
            If Not File.Exists(percorso) Then Return ""
            Dim testa(3) As Byte
            Using f As New FileStream(percorso, FileMode.Open, FileAccess.Read, FileShare.ReadWrite)
                If f.Read(testa, 0, 4) < 4 Then Return ""
            End Using
            Dim sigla As String = Encoding.ASCII.GetString(testa)
            If sigla = "WGFP" Then Return "P"
            If sigla = "WGFN" Then Return "N"
            Return ""
        Catch ex As Exception
            Registra("WGCifraturaNativa.TipoFileCifrato: " & Breve(ex))
            Return ""
        End Try
    End Function

End Class
