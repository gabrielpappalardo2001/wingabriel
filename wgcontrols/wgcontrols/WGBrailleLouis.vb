' ============================================================================================
' WGBrailleLouis - traduzione in braille con liblouis
' ============================================================================================
'
' A cosa serve
'   Ponte verso liblouis (liblouis64.dll a 64 bit, liblouis32.dll a 32 bit, la stessa libreria di NVDA, con le sue
'   tabelle). Traduce un testo in celle braille (maschere di punti da 0 a 255, il punto 1 e' il bit 0) tenendo conto
'   della posizione del cursore e di quale lettera sta sotto ogni cella, e fa il percorso inverso per i punti
'   battuti sulla tastiera braille. Se la libreria o le tabelle mancano si usa una tabella interna in braille
'   informatico, cosi' la barra funziona comunque.
'
' Come si usa
'   WGBrailleLouis.Traduci("ciao", cursore, otto, tabella, celle, posizioniInput)
'   Dim c As String = WGBrailleLouis.DaPunti(&H3, otto, tabella)
'
' Errori
'   Nessun metodo solleva eccezioni: tutto finisce nel log e si passa alla tabella interna.
' ============================================================================================
Imports System.IO
Imports System.Runtime.InteropServices
Imports System.Text

Friend NotInheritable Class WGBrailleLouis

    Private Shared ReadOnly log As New Logger()

    <UnmanagedFunctionPointer(CallingConvention.Cdecl)>
    Private Delegate Function DCharSize() As Integer

    <UnmanagedFunctionPointer(CallingConvention.Cdecl)>
    Private Delegate Function DTranslate(<MarshalAs(UnmanagedType.LPStr)> tabelle As String, inbuf As IntPtr, ByRef inlen As Integer, outbuf As IntPtr, ByRef outlen As Integer, typeform As IntPtr, spacing As IntPtr, outputPos As IntPtr, inputPos As IntPtr, cursorPos As IntPtr, modo As Integer) As Integer

    <UnmanagedFunctionPointer(CallingConvention.Cdecl)>
    Private Delegate Function DDotsToChar(<MarshalAs(UnmanagedType.LPStr)> tabelle As String, inbuf As IntPtr, outbuf As IntPtr, lunghezza As Integer, modo As Integer) As Integer

    <DllImport("kernel32.dll", CharSet:=CharSet.Unicode, SetLastError:=True)>
    Private Shared Function LoadLibraryW(nomeFile As String) As IntPtr
    End Function

    <DllImport("kernel32.dll", CharSet:=CharSet.Ansi, ExactSpelling:=True, SetLastError:=True)>
    Private Shared Function GetProcAddress(modulo As IntPtr, nome As String) As IntPtr
    End Function

    ' Modi di liblouis (gli stessi usati da NVDA).
    Private Const ModoPuntiInOut As Integer = 4
    Private Const ModoSenzaPuntiIndefiniti As Integer = 128

    ' La tabella ASCII braille standard: l'indice e' la maschera dei punti (0-63), il carattere e' quello del
    ' braille informatico a 6 punti.
    Private Const TabellaAscii6 As String = " A1B'K2L@CIF/MSP""E3H9O6R^DJG>NTQ,*5<-U8V.%[$+X!&;:4\0Z7(_?W]#Y)="

    Private Shared ReadOnly _blocco As New Object()
    Private Shared _provato As Boolean = False
    Private Shared _caricata As Boolean = False
    Private Shared _dimensioneChar As Integer = 2
    Private Shared _traduci As DTranslate
    Private Shared _puntiAChar As DDotsToChar
    Private Shared _cartellaTabelle As String = ""
    Private Shared _ultimoErrore As String = ""

    Private Sub New()
    End Sub

    ''' <summary>Vero se liblouis e' stata caricata e funziona.</summary>
    Public Shared ReadOnly Property Disponibile As Boolean
        Get
            Try
                Carica()
                Return _caricata
            Catch ex As Exception
                log.ScriviLog("WGBrailleLouis.Disponibile: " & ex.Message)
                Return False
            End Try
        End Get
    End Property

    Public Shared ReadOnly Property UltimoErrore As String
        Get
            Return _ultimoErrore
        End Get
    End Property

    ''' <summary>Le tabelle che si propongono nelle impostazioni: nome del file, descrizione, punti.</summary>
    Public Shared Function TabelleProposte() As List(Of TabellaBraille)
        Dim elenco As New List(Of TabellaBraille)()
        Try
            elenco.Add(New TabellaBraille("it-it-comp6.utb", "Italiano, braille a 6 punti", 6))
            elenco.Add(New TabellaBraille("it-it-comp8.utb", "Italiano, braille informatico a 8 punti", 8))
            elenco.Add(New TabellaBraille("en-us-g1.ctb", "Inglese (Stati Uniti), grado 1", 6))
            elenco.Add(New TabellaBraille("en-us-comp8.ctb", "Inglese (Stati Uniti), braille informatico a 8 punti", 8))
            elenco.Add(New TabellaBraille("en-gb-g1.utb", "Inglese (Regno Unito), grado 1", 6))
            elenco.Add(New TabellaBraille("en-gb-comp8.ctb", "Inglese (Regno Unito), braille informatico a 8 punti", 8))
            elenco.Add(New TabellaBraille("fr-bfu-comp6.utb", "Francese, braille a 6 punti", 6))
            elenco.Add(New TabellaBraille("fr-bfu-comp8.utb", "Francese, braille informatico a 8 punti", 8))
            elenco.Add(New TabellaBraille("de-g0.utb", "Tedesco, braille a 6 punti", 6))
            elenco.Add(New TabellaBraille("de-de-comp8.ctb", "Tedesco, braille informatico a 8 punti", 8))
            elenco.Add(New TabellaBraille("es-g1.ctb", "Spagnolo, grado 1", 6))
            elenco.Add(New TabellaBraille("pt-pt-g1.utb", "Portoghese, grado 1", 6))
            elenco.Add(New TabellaBraille("pt-pt-comp8.ctb", "Portoghese, braille informatico a 8 punti", 8))
            elenco.Add(New TabellaBraille("nl-comp8.utb", "Olandese, braille informatico a 8 punti", 8))
            elenco.Add(New TabellaBraille("Pl-Pl-g1.utb", "Polacco, grado 1", 6))
            elenco.Add(New TabellaBraille("pl-pl-comp8.ctb", "Polacco, braille informatico a 8 punti", 8))
            elenco.Add(New TabellaBraille("ru-ru-g1.ctb", "Russo, grado 1", 6))
            elenco.Add(New TabellaBraille("ru-comp8.utb", "Russo, braille informatico a 8 punti", 8))
            elenco.Add(New TabellaBraille("el.ctb", "Greco, braille a 6 punti", 6))
            elenco.Add(New TabellaBraille("tr-g1.ctb", "Turco, grado 1", 6))
            elenco.Add(New TabellaBraille("sv-g1.ctb", "Svedese, grado 1", 6))
            elenco.Add(New TabellaBraille("sv-8g1d.ctb", "Svedese, braille informatico a 8 punti", 8))
            elenco.Add(New TabellaBraille("da-dk-g16.ctb", "Danese, braille a 6 punti", 6))
            elenco.Add(New TabellaBraille("da-dk-g18.ctb", "Danese, braille informatico a 8 punti", 8))
            elenco.Add(New TabellaBraille("no-no-g0.utb", "Norvegese, braille a 6 punti", 6))
            elenco.Add(New TabellaBraille("no-no-comp8.ctb", "Norvegese, braille informatico a 8 punti", 8))
            elenco.Add(New TabellaBraille("fi-fi-8dot.ctb", "Finlandese, braille informatico a 8 punti", 8))
            elenco.Add(New TabellaBraille("cs-g1.ctb", "Ceco, grado 1", 6))
            elenco.Add(New TabellaBraille("cs-comp8.utb", "Ceco, braille informatico a 8 punti", 8))
            elenco.Add(New TabellaBraille("ro-g0.utb", "Rumeno, braille a 6 punti", 6))
            elenco.Add(New TabellaBraille("", "Tabella interna di WinGabriel, a 6 punti", 6))
        Catch ex As Exception
            log.ScriviLog("WGBrailleLouis.TabelleProposte: " & ex.Message)
        End Try
        Return elenco
    End Function

    ''' <summary>La tabella proposta per la lingua indicata (codice a due lettere, es. "en") e i punti per cella dati,
    ''' guardando l'inizio del nome del file (it-it-..., en-us-...). Nothing se non c'e'.</summary>
    ''' <summary>I file delle tabelle di liblouis non usano sempre lo stesso codice della lingua di WinGabriel: qui
    ''' si traduce quando serve (per esempio il norvegese e' "nb" per WinGabriel ma i file iniziano per "no-no-").</summary>
    Private Shared Function PrefissoTabellaPerLingua(codiceLingua As String) As String
        Select Case codiceLingua
            Case "nb" : Return "no"
            Case Else : Return codiceLingua
        End Select
    End Function

    Public Shared Function TabellaPerLingua(codiceLingua As String, punti As Integer) As TabellaBraille
        Try
            If String.IsNullOrWhiteSpace(codiceLingua) Then Return Nothing
            Dim prefisso As String = PrefissoTabellaPerLingua(codiceLingua.Trim().ToLowerInvariant()) & "-"
            For Each t As TabellaBraille In TabelleProposte()
                If t.File <> "" AndAlso t.Punti = punti AndAlso t.File.ToLowerInvariant().StartsWith(prefisso, StringComparison.Ordinal) Then Return t
            Next
            Return Nothing
        Catch ex As Exception
            log.ScriviLog("WGBrailleLouis.TabellaPerLingua: " & ex.Message)
            Return Nothing
        End Try
    End Function

    Private Shared Sub Carica()
        If _provato Then Return
        SyncLock _blocco
            If _provato Then Return
            _provato = True
            Try
                Dim nomeDll As String = If(IntPtr.Size = 8, "liblouis64.dll", "liblouis32.dll")
                Dim basePrg As String = AppDomain.CurrentDomain.BaseDirectory
                Dim candidati() As String = {
                    Path.Combine(basePrg, "braille", nomeDll),
                    Path.Combine(basePrg, nomeDll)}
                Dim percorso As String = ""
                For Each c As String In candidati
                    If File.Exists(c) Then
                        percorso = c
                        Exit For
                    End If
                Next
                If percorso = "" Then
                    _ultimoErrore = nomeDll & " non trovata"
                    log.ScriviLog("WGBrailleLouis: " & _ultimoErrore)
                    Return
                End If
                _cartellaTabelle = Path.Combine(Path.GetDirectoryName(percorso), "tabelle")
                If Not Directory.Exists(_cartellaTabelle) Then
                    _ultimoErrore = "cartella delle tabelle braille non trovata"
                    log.ScriviLog("WGBrailleLouis: " & _ultimoErrore & ": " & _cartellaTabelle)
                    Return
                End If
                Dim h As IntPtr = LoadLibraryW(percorso)
                If h = IntPtr.Zero Then
                    _ultimoErrore = "caricamento di liblouis fallito, errore " & Marshal.GetLastWin32Error().ToString()
                    log.ScriviLog("WGBrailleLouis: " & _ultimoErrore)
                    Return
                End If
                Dim pSize As IntPtr = GetProcAddress(h, "lou_charSize")
                Dim pTrad As IntPtr = GetProcAddress(h, "lou_translate")
                Dim pPunti As IntPtr = GetProcAddress(h, "lou_dotsToChar")
                If pSize = IntPtr.Zero OrElse pTrad = IntPtr.Zero OrElse pPunti = IntPtr.Zero Then
                    _ultimoErrore = "funzioni di liblouis mancanti"
                    log.ScriviLog("WGBrailleLouis: " & _ultimoErrore)
                    Return
                End If
                _dimensioneChar = Marshal.GetDelegateForFunctionPointer(Of DCharSize)(pSize)()
                If _dimensioneChar <> 2 AndAlso _dimensioneChar <> 4 Then
                    _ultimoErrore = "dimensione dei caratteri di liblouis non prevista: " & _dimensioneChar.ToString()
                    log.ScriviLog("WGBrailleLouis: " & _ultimoErrore)
                    Return
                End If
                _traduci = Marshal.GetDelegateForFunctionPointer(Of DTranslate)(pTrad)
                _puntiAChar = Marshal.GetDelegateForFunctionPointer(Of DDotsToChar)(pPunti)
                _caricata = True
            Catch ex As Exception
                _ultimoErrore = ex.Message
                log.ScriviLog("WGBrailleLouis.Carica: " & ex.Message)
                _caricata = False
            End Try
        End SyncLock
    End Sub

    Private Shared Function PercorsoTabella(tabella As String) As String
        Try
            If String.IsNullOrEmpty(tabella) Then Return ""
            Dim p As String = Path.Combine(_cartellaTabelle, tabella)
            If File.Exists(p) Then Return p
            Return ""
        Catch ex As Exception
            log.ScriviLog("WGBrailleLouis.PercorsoTabella: " & ex.Message)
            Return ""
        End Try
    End Function

    Private Shared Function ScriviWide(valori() As Integer) As IntPtr
        Dim buf As IntPtr = Marshal.AllocHGlobal(Math.Max(1, valori.Length) * _dimensioneChar)
        For i As Integer = 0 To valori.Length - 1
            If _dimensioneChar = 2 Then
                Marshal.WriteInt16(buf, i * 2, CShort(valori(i) And &HFFFF))
            Else
                Marshal.WriteInt32(buf, i * 4, valori(i))
            End If
        Next
        Return buf
    End Function

    Private Shared Function LeggiWide(buf As IntPtr, indice As Integer) As Integer
        If _dimensioneChar = 2 Then Return Marshal.ReadInt16(buf, indice * 2) And &HFFFF
        Return Marshal.ReadInt32(buf, indice * 4)
    End Function

    ''' <summary>
    ''' Traduce un testo in celle. celle: maschere dei punti (bit 0 = punto 1). posizioniInput: per ogni cella, l'indice
    ''' del carattere del testo da cui nasce. Il cursore (se >= 0) non entra nelle celle: lo mette chi mostra.
    ''' </summary>
    Public Shared Function Traduci(testo As String, tabella As String, ByRef celle As Integer(), ByRef posizioniInput As Integer()) As Boolean
        celle = New Integer() {}
        posizioniInput = New Integer() {}
        Try
            If String.IsNullOrEmpty(testo) Then Return True
            Dim percorso As String = PercorsoTabella(tabella)
            If Disponibile AndAlso percorso <> "" Then
                If TraduciConLouis(testo, percorso, celle, posizioniInput) Then Return True
            End If
            TraduciInterno(testo, celle, posizioniInput)
            Return True
        Catch ex As Exception
            log.ScriviLog("WGBrailleLouis.Traduci: " & ex.Message)
            Try
                TraduciInterno(If(testo, ""), celle, posizioniInput)
            Catch ex2 As Exception
                log.ScriviLog("WGBrailleLouis.Traduci (interna): " & ex2.Message)
            End Try
            Return False
        End Try
    End Function

    Private Shared Function TraduciConLouis(testo As String, percorsoTabella As String, ByRef celle As Integer(), ByRef posizioniInput As Integer()) As Boolean
        Dim bufIn As IntPtr = IntPtr.Zero
        Dim bufOut As IntPtr = IntPtr.Zero
        Dim bufPosIn As IntPtr = IntPtr.Zero
        Try
            Dim punti As New List(Of Integer)(testo.Length)
            For Each ch As Char In testo
                punti.Add(AscW(ch))
            Next
            Dim lenIn As Integer = punti.Count
            Dim capienza As Integer = lenIn * 4 + 64
            Dim lenOut As Integer = capienza
            bufIn = ScriviWide(punti.ToArray())
            bufOut = Marshal.AllocHGlobal(capienza * _dimensioneChar)
            bufPosIn = Marshal.AllocHGlobal(capienza * 4)
            For i As Integer = 0 To capienza - 1
                Marshal.WriteInt32(bufPosIn, i * 4, 0)
            Next
            Dim esito As Integer
            SyncLock _blocco
                esito = _traduci(percorsoTabella, bufIn, lenIn, bufOut, lenOut, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, bufPosIn, IntPtr.Zero, ModoPuntiInOut Or ModoSenzaPuntiIndefiniti)
            End SyncLock
            If esito = 0 Then
                log.ScriviLog("WGBrailleLouis: lou_translate ha fallito con la tabella " & percorsoTabella)
                Return False
            End If
            Dim outCelle(lenOut - 1) As Integer
            Dim outPos(lenOut - 1) As Integer
            For i As Integer = 0 To lenOut - 1
                Dim v As Integer = LeggiWide(bufOut, i)
                If v >= &H8000 Then
                    outCelle(i) = v And &HFF
                ElseIf v >= &H2800 AndAlso v <= &H28FF Then
                    outCelle(i) = v And &HFF
                Else
                    outCelle(i) = 0
                End If
                outPos(i) = Math.Max(0, Math.Min(testo.Length - 1, Marshal.ReadInt32(bufPosIn, i * 4)))
            Next
            celle = outCelle
            posizioniInput = outPos
            Return True
        Catch ex As Exception
            log.ScriviLog("WGBrailleLouis.TraduciConLouis: " & ex.Message)
            Return False
        Finally
            If bufIn <> IntPtr.Zero Then Marshal.FreeHGlobal(bufIn)
            If bufOut <> IntPtr.Zero Then Marshal.FreeHGlobal(bufOut)
            If bufPosIn <> IntPtr.Zero Then Marshal.FreeHGlobal(bufPosIn)
        End Try
    End Function

    ''' <summary>Tabella interna: braille informatico ASCII a 6 punti, con le lettere accentate piu' comuni.</summary>
    Private Shared Sub TraduciInterno(testo As String, ByRef celle As Integer(), ByRef posizioniInput As Integer())
        Dim lc As New List(Of Integer)(testo.Length)
        Dim lp As New List(Of Integer)(testo.Length)
        For i As Integer = 0 To testo.Length - 1
            lc.Add(CarattereAPunti(testo(i)))
            lp.Add(i)
        Next
        celle = lc.ToArray()
        posizioniInput = lp.ToArray()
    End Sub

    Private Shared Function CarattereAPunti(c As Char) As Integer
        Try
            Select Case c
                Case "à"c : Return &H37
                Case "è"c, "é"c : Return &H2E
                Case "ì"c : Return &HC
                Case "ò"c : Return &H2C
                Case "ù"c : Return &H3E
                Case Else
            End Select
            Dim u As Char = Char.ToUpperInvariant(c)
            Dim indice As Integer = TabellaAscii6.IndexOf(u)
            If indice >= 0 Then Return indice
            If c = ChrW(&HA0) Then Return 0
            Return &H3F
        Catch ex As Exception
            log.ScriviLog("WGBrailleLouis.CarattereAPunti: " & ex.Message)
            Return 0
        End Try
    End Function

    ''' <summary>
    ''' Il carattere che corrisponde a una cella battuta sulla tastiera braille (braille informatico). Restituisce una
    ''' stringa vuota se la combinazione non corrisponde a niente.
    ''' </summary>
    Public Shared Function DaPunti(punti As Integer, tabella As String) As String
        Try
            punti = punti And &HFF
            If punti = 0 Then Return ""
            Dim percorso As String = PercorsoTabella(tabella)
            If Disponibile AndAlso percorso <> "" Then
                Dim bufIn As IntPtr = IntPtr.Zero
                Dim bufOut As IntPtr = IntPtr.Zero
                Try
                    bufIn = ScriviWide(New Integer() {&H8000 Or punti})
                    bufOut = Marshal.AllocHGlobal(4 * _dimensioneChar)
                    Dim esito As Integer
                    SyncLock _blocco
                        esito = _puntiAChar(percorso, bufIn, bufOut, 1, 0)
                    End SyncLock
                    If esito <> 0 Then
                        Dim v As Integer = LeggiWide(bufOut, 0)
                        If v > 0 AndAlso v < &H2800 Then Return ChrW(v)
                    End If
                Finally
                    If bufIn <> IntPtr.Zero Then Marshal.FreeHGlobal(bufIn)
                    If bufOut <> IntPtr.Zero Then Marshal.FreeHGlobal(bufOut)
                End Try
            End If
            ' Tabella interna (solo 6 punti).
            If (punti And &HC0) <> 0 Then Return ""
            Dim ch As Char = TabellaAscii6(punti)
            If ch = " "c Then Return ""
            Return Char.ToLowerInvariant(ch).ToString()
        Catch ex As Exception
            log.ScriviLog("WGBrailleLouis.DaPunti: " & ex.Message)
            Return ""
        End Try
    End Function

End Class

Friend Class TabellaBraille
    Public Property File As String
    Public Property Descrizione As String
    Public Property Punti As Integer

    Public Sub New(file As String, descrizione As String, punti As Integer)
        Me.File = file
        Me.Descrizione = descrizione
        Me.Punti = punti
    End Sub
End Class
