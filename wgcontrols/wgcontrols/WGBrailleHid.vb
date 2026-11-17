' ============================================================================================
' WGBrailleHid - barre braille che parlano lo standard HID braille (pagina d'uso 0x41)
' ============================================================================================
'
' A cosa serve
'   Molte barre (via cavo USB o Bluetooth a basso consumo) si presentano a Windows come dispositivi HID
'   braille: per questi non serve nessun driver, perche' Windows li gestisce con il proprio driver HID.
'   Qui si trovano questi dispositivi, si leggono i tasti (punti, spazio, tasti panoramica, instradamento) e
'   si scrivono le celle, come fa il driver "hidBrailleStandard" di NVDA.
'
' Errori
'   Nessun metodo solleva eccezioni: gli errori vanno nel log e il dispositivo non viene usato.
' ============================================================================================
Imports System.IO
Imports System.Runtime.InteropServices
Imports System.Threading

Friend NotInheritable Class WGBrailleHid
    Implements IWGBrailleDisplay

    Private Const PaginaBraille As Integer = &H41
    Private Shared ReadOnly INVALID As New IntPtr(-1)

    <DllImport("hid.dll")>
    Private Shared Sub HidD_GetHidGuid(ByRef guid As Guid)
    End Sub

    <DllImport("hid.dll", SetLastError:=True)>
    Private Shared Function HidD_GetPreparsedData(h As IntPtr, ByRef dati As IntPtr) As Boolean
    End Function

    <DllImport("hid.dll", SetLastError:=True)>
    Private Shared Function HidD_FreePreparsedData(dati As IntPtr) As Boolean
    End Function

    <DllImport("hid.dll", SetLastError:=True)>
    Private Shared Function HidD_SetOutputReport(h As IntPtr, buffer As Byte(), lunghezza As Integer) As Boolean
    End Function

    <DllImport("hid.dll", CharSet:=CharSet.Unicode, SetLastError:=True)>
    Private Shared Function HidD_GetProductString(h As IntPtr, buffer As Byte(), lunghezza As Integer) As Boolean
    End Function

    <DllImport("hid.dll")>
    Private Shared Function HidP_GetCaps(dati As IntPtr, caps As IntPtr) As Integer
    End Function

    <DllImport("hid.dll")>
    Private Shared Function HidP_GetButtonCaps(tipo As Integer, caps As IntPtr, ByRef lunghezza As UShort, dati As IntPtr) As Integer
    End Function

    <DllImport("hid.dll")>
    Private Shared Function HidP_GetValueCaps(tipo As Integer, caps As IntPtr, ByRef lunghezza As UShort, dati As IntPtr) As Integer
    End Function

    <DllImport("hid.dll")>
    Private Shared Function HidP_GetData(tipo As Integer, elenco As IntPtr, ByRef lunghezza As UInteger, dati As IntPtr, report As Byte(), lunghezzaReport As UInteger) As Integer
    End Function

    <DllImport("hid.dll")>
    Private Shared Function HidP_SetUsageValueArray(tipo As Integer, pagina As UShort, collegamento As UShort, uso As UShort, valore As Byte(), lunghezzaValore As UShort, dati As IntPtr, report As Byte(), lunghezzaReport As UInteger) As Integer
    End Function

    <StructLayout(LayoutKind.Sequential)>
    Private Structure SP_DEVICE_INTERFACE_DATA
        Public cbSize As Integer
        Public InterfaceClassGuid As Guid
        Public Flags As Integer
        Public Reserved As IntPtr
    End Structure

    <DllImport("setupapi.dll", SetLastError:=True)>
    Private Shared Function SetupDiGetClassDevs(ByRef classGuid As Guid, enumerator As IntPtr, hwnd As IntPtr, flags As Integer) As IntPtr
    End Function

    <DllImport("setupapi.dll", SetLastError:=True)>
    Private Shared Function SetupDiEnumDeviceInterfaces(info As IntPtr, devInfo As IntPtr, ByRef interfaceClassGuid As Guid, indice As Integer, ByRef dati As SP_DEVICE_INTERFACE_DATA) As Boolean
    End Function

    <DllImport("setupapi.dll", CharSet:=CharSet.Unicode, SetLastError:=True)>
    Private Shared Function SetupDiGetDeviceInterfaceDetail(info As IntPtr, ByRef dati As SP_DEVICE_INTERFACE_DATA, dettaglio As IntPtr, dimensione As Integer, ByRef richiesta As Integer, devInfo As IntPtr) As Boolean
    End Function

    <DllImport("setupapi.dll", SetLastError:=True)>
    Private Shared Function SetupDiDestroyDeviceInfoList(info As IntPtr) As Boolean
    End Function

    <DllImport("kernel32.dll", CharSet:=CharSet.Unicode, SetLastError:=True)>
    Private Shared Function CreateFile(nome As String, accesso As UInteger, condivisione As UInteger, sicurezza As IntPtr, creazione As UInteger, attributi As UInteger, modello As IntPtr) As IntPtr
    End Function

    <DllImport("kernel32.dll", SetLastError:=True)>
    Private Shared Function ReadFile(h As IntPtr, buffer As Byte(), daLeggere As UInteger, ByRef letti As UInteger, overlapped As IntPtr) As Boolean
    End Function

    <DllImport("kernel32.dll", SetLastError:=True)>
    Private Shared Function CancelIoEx(h As IntPtr, overlapped As IntPtr) As Boolean
    End Function

    <DllImport("kernel32.dll", SetLastError:=True)>
    Private Shared Function CloseHandle(h As IntPtr) As Boolean
    End Function

    ' Un tasto o un'etichetta di cella trovata nella descrizione dei rapporti.
    Private Class CapoBottone
        Public DataIndexMin As Integer
        Public DataIndexMax As Integer
        Public UsoMin As Integer
        Public UsoMax As Integer
        Public Collegamento As Integer
        Public UsoCollegamento As Integer
        Public PrimoIndiceCollegamento As Integer
    End Class

    Private ReadOnly log As New Logger()
    Private _lettura As IntPtr = INVALID
    Private _scrittura As IntPtr = INVALID
    Private _preparsed As IntPtr = IntPtr.Zero
    Private _dimInput As Integer
    Private _dimOutput As Integer
    Private _celle As Integer
    Private _bitCella As Integer = 8
    Private _usoCella As Integer = 3
    Private _collCella As Integer
    Private _idRapportoOutput As Byte
    Private _otto As Boolean = True
    Private _nome As String = "Barra braille HID"
    Private _collegato As Boolean
    Private _disposto As Boolean
    Private _thread As Thread
    Private ReadOnly _bottoni As New List(Of CapoBottone)()
    Private _premuti As TastoBraille = TastoBraille.Nessuno
    Private _accordo As TastoBraille = TastoBraille.Nessuno
    Private ReadOnly _router As New HashSet(Of Integer)()

    Public Event TastiPremuti(tasti As TastoBraille) Implements IWGBrailleDisplay.TastiPremuti
    Public Event Instradamento(indice As Integer) Implements IWGBrailleDisplay.Instradamento
    Public Event Disconnessa() Implements IWGBrailleDisplay.Disconnessa

    Public ReadOnly Property Nome As String Implements IWGBrailleDisplay.Nome
        Get
            Return _nome
        End Get
    End Property

    Public ReadOnly Property NumeroCelle As Integer Implements IWGBrailleDisplay.NumeroCelle
        Get
            Return _celle
        End Get
    End Property

    Public ReadOnly Property OttoPunti As Boolean Implements IWGBrailleDisplay.OttoPunti
        Get
            Return _otto
        End Get
    End Property

    Public ReadOnly Property Collegato As Boolean Implements IWGBrailleDisplay.Collegato
        Get
            Return _collegato AndAlso Not _disposto
        End Get
    End Property

    Public ReadOnly Property Collegamento As String Implements IWGBrailleDisplay.Collegamento
        Get
            Return "Hid"
        End Get
    End Property

    ''' <summary>Elenca i dispositivi HID che dichiarano di essere una barra braille.</summary>
    Public Shared Function Elenca() As List(Of CandidatoBraille)
        Dim risultato As New List(Of CandidatoBraille)()
        Dim logS As New Logger()
        Dim info As IntPtr = IntPtr.Zero
        Try
            Dim g As Guid
            HidD_GetHidGuid(g)
            info = SetupDiGetClassDevs(g, IntPtr.Zero, IntPtr.Zero, &H12)
            If info = INVALID OrElse info = IntPtr.Zero Then Return risultato
            Dim indice As Integer = 0
            While True
                Dim dati As New SP_DEVICE_INTERFACE_DATA()
                dati.cbSize = Marshal.SizeOf(GetType(SP_DEVICE_INTERFACE_DATA))
                If Not SetupDiEnumDeviceInterfaces(info, IntPtr.Zero, g, indice, dati) Then Exit While
                indice += 1
                Dim percorso As String = PercorsoInterfaccia(info, dati)
                If String.IsNullOrEmpty(percorso) Then Continue While
                Dim h As IntPtr = CreateFile(percorso, 0UI, 3UI, IntPtr.Zero, 3UI, 0UI, IntPtr.Zero)
                If h = INVALID Then Continue While
                Try
                    Dim pre As IntPtr = IntPtr.Zero
                    If Not HidD_GetPreparsedData(h, pre) Then Continue While
                    Try
                        Dim caps As IntPtr = Marshal.AllocHGlobal(64)
                        Try
                            If HidP_GetCaps(pre, caps) <> &H110000 Then Continue While
                            Dim uso As Integer = Marshal.ReadInt16(caps, 0) And &HFFFF
                            Dim pagina As Integer = Marshal.ReadInt16(caps, 2) And &HFFFF
                            If pagina <> PaginaBraille OrElse uso <> 1 Then Continue While
                        Finally
                            Marshal.FreeHGlobal(caps)
                        End Try
                    Finally
                        HidD_FreePreparsedData(pre)
                    End Try
                    Dim c As New CandidatoBraille()
                    c.Tipo = "Hid"
                    c.Percorso = percorso
                    c.Descrizione = NomeProdotto(h)
                    c.Collegamento = If(percorso.IndexOf("bthle", StringComparison.OrdinalIgnoreCase) >= 0 OrElse percorso.IndexOf("bth", StringComparison.OrdinalIgnoreCase) >= 0, "Bluetooth", "Cavo")
                    c.Probabile = True
                    risultato.Add(c)
                Finally
                    CloseHandle(h)
                End Try
            End While
        Catch ex As Exception
            logS.ScriviLog("WGBrailleHid.Elenca: " & ex.Message)
        Finally
            If info <> IntPtr.Zero AndAlso info <> INVALID Then SetupDiDestroyDeviceInfoList(info)
        End Try
        Return risultato
    End Function

    Private Shared Function PercorsoInterfaccia(info As IntPtr, ByRef dati As SP_DEVICE_INTERFACE_DATA) As String
        Try
            Dim richiesta As Integer = 0
            SetupDiGetDeviceInterfaceDetail(info, dati, IntPtr.Zero, 0, richiesta, IntPtr.Zero)
            If richiesta <= 0 Then Return ""
            Dim buf As IntPtr = Marshal.AllocHGlobal(richiesta)
            Try
                Marshal.WriteInt32(buf, If(IntPtr.Size = 8, 8, 6))
                If Not SetupDiGetDeviceInterfaceDetail(info, dati, buf, richiesta, richiesta, IntPtr.Zero) Then Return ""
                Return Marshal.PtrToStringUni(IntPtr.Add(buf, 4))
            Finally
                Marshal.FreeHGlobal(buf)
            End Try
        Catch ex As Exception
            Return ""
        End Try
    End Function

    Private Shared Function NomeProdotto(h As IntPtr) As String
        Try
            Dim buf(255) As Byte
            If HidD_GetProductString(h, buf, buf.Length) Then
                Dim s As String = System.Text.Encoding.Unicode.GetString(buf)
                Dim fine As Integer = s.IndexOf(ChrW(0))
                If fine >= 0 Then s = s.Substring(0, fine)
                If s.Trim() <> "" Then Return s.Trim()
            End If
        Catch ex As Exception
            Dim logS As New Logger()
            logS.ScriviLog("WGBrailleHid.NomeProdotto: " & ex.Message)
        End Try
        Return "Barra braille HID"
    End Function

    ''' <summary>Apre il dispositivo e ne legge le caratteristiche. Restituisce Nothing se non e' una barra utilizzabile.</summary>
    Public Shared Function Prova(c As CandidatoBraille) As WGBrailleHid
        Dim logS As New Logger()
        Dim barra As New WGBrailleHid()
        Try
            If barra.Apri(c) Then Return barra
            barra.Dispose()
            Return Nothing
        Catch ex As Exception
            logS.ScriviLog("WGBrailleHid.Prova " & c.ToString() & ": " & ex.Message)
            Try
                barra.Dispose()
            Catch ex2 As Exception
                logS.ScriviLog("WGBrailleHid.Prova (chiusura): " & ex2.Message)
            End Try
            Return Nothing
        End Try
    End Function

    Private Sub New()
    End Sub

    Private Function Apri(c As CandidatoBraille) As Boolean
        Try
            _nome = If(String.IsNullOrEmpty(c.Descrizione), "Barra braille HID", c.Descrizione)
            ' Due maniglie: una resta ferma in lettura, l'altra serve a scrivere (con una sola, le letture bloccherebbero le scritture).
            _lettura = CreateFile(c.Percorso, &HC0000000UI, 3UI, IntPtr.Zero, 3UI, 0UI, IntPtr.Zero)
            _scrittura = CreateFile(c.Percorso, &HC0000000UI, 3UI, IntPtr.Zero, 3UI, 0UI, IntPtr.Zero)
            If _lettura = INVALID OrElse _scrittura = INVALID Then
                log.ScriviLog("WGBrailleHid: apertura fallita, errore " & Marshal.GetLastWin32Error().ToString())
                Return False
            End If
            If Not HidD_GetPreparsedData(_lettura, _preparsed) Then Return False
            Dim caps As IntPtr = Marshal.AllocHGlobal(64)
            Try
                If HidP_GetCaps(_preparsed, caps) <> &H110000 Then Return False
                _dimInput = Marshal.ReadInt16(caps, 4) And &HFFFF
                _dimOutput = Marshal.ReadInt16(caps, 6) And &HFFFF
            Finally
                Marshal.FreeHGlobal(caps)
            End Try
            If Not LeggiCelle() Then Return False
            LeggiBottoni()
            _collegato = True
            _thread = New Thread(AddressOf CicloLettura)
            _thread.IsBackground = True
            _thread.Start()
            Return True
        Catch ex As Exception
            log.ScriviLog("WGBrailleHid.Apri: " & ex.Message)
            Return False
        End Try
    End Function

    ' Le celle sono un valore a matrice nel rapporto di uscita, dentro la "riga braille".
    Private Function LeggiCelle() As Boolean
        Dim caps As IntPtr = IntPtr.Zero
        Try
            Dim n As UShort = 64
            caps = Marshal.AllocHGlobal(72 * n)
            If HidP_GetValueCaps(1, caps, n, _preparsed) <> &H110000 Then Return False
            Dim trovato8 As Boolean = False
            Dim trovato6 As Boolean = False
            For i As Integer = 0 To n - 1
                Dim o As IntPtr = IntPtr.Add(caps, i * 72)
                Dim pagina As Integer = Marshal.ReadInt16(o, 0) And &HFFFF
                If pagina <> PaginaBraille Then Continue For
                Dim idRapporto As Byte = Marshal.ReadByte(o, 2)
                Dim collegamento As Integer = Marshal.ReadInt16(o, 6) And &HFFFF
                Dim isRange As Boolean = Marshal.ReadByte(o, 12) <> 0
                Dim bitSize As Integer = Marshal.ReadInt16(o, 18) And &HFFFF
                Dim conteggio As Integer = Marshal.ReadInt16(o, 20) And &HFFFF
                Dim uso As Integer = Marshal.ReadInt16(o, 56) And &HFFFF
                If isRange Then Continue For
                If uso = 3 AndAlso conteggio > 0 AndAlso Not trovato8 Then
                    trovato8 = True
                    _usoCella = 3
                    _celle = conteggio
                    _bitCella = bitSize
                    _collCella = collegamento
                    _idRapportoOutput = idRapporto
                    _otto = True
                ElseIf uso = 4 AndAlso conteggio > 0 AndAlso Not trovato8 AndAlso Not trovato6 Then
                    trovato6 = True
                    _usoCella = 4
                    _celle = conteggio
                    _bitCella = bitSize
                    _collCella = collegamento
                    _idRapportoOutput = idRapporto
                    _otto = False
                End If
            Next
            Return _celle > 0
        Catch ex As Exception
            log.ScriviLog("WGBrailleHid.LeggiCelle: " & ex.Message)
            Return False
        Finally
            If caps <> IntPtr.Zero Then Marshal.FreeHGlobal(caps)
        End Try
    End Function

    Private Sub LeggiBottoni()
        Dim caps As IntPtr = IntPtr.Zero
        Try
            Dim n As UShort = 256
            caps = Marshal.AllocHGlobal(72 * n)
            If HidP_GetButtonCaps(0, caps, n, _preparsed) <> &H110000 Then Return
            Dim minPerCollegamento As New Dictionary(Of Integer, Integer)()
            Dim lista As New List(Of CapoBottone)()
            For i As Integer = 0 To n - 1
                Dim o As IntPtr = IntPtr.Add(caps, i * 72)
                Dim b As New CapoBottone()
                Dim pagina As Integer = Marshal.ReadInt16(o, 0) And &HFFFF
                If pagina <> PaginaBraille Then Continue For
                b.Collegamento = Marshal.ReadInt16(o, 6) And &HFFFF
                b.UsoCollegamento = Marshal.ReadInt16(o, 8) And &HFFFF
                Dim isRange As Boolean = Marshal.ReadByte(o, 12) <> 0
                If isRange Then
                    b.UsoMin = Marshal.ReadInt16(o, 56) And &HFFFF
                    b.UsoMax = Marshal.ReadInt16(o, 58) And &HFFFF
                    b.DataIndexMin = Marshal.ReadInt16(o, 68) And &HFFFF
                    b.DataIndexMax = Marshal.ReadInt16(o, 70) And &HFFFF
                Else
                    b.UsoMin = Marshal.ReadInt16(o, 56) And &HFFFF
                    b.UsoMax = b.UsoMin
                    b.DataIndexMin = Marshal.ReadInt16(o, 68) And &HFFFF
                    b.DataIndexMax = b.DataIndexMin
                End If
                lista.Add(b)
                If Not minPerCollegamento.ContainsKey(b.Collegamento) OrElse b.DataIndexMin < minPerCollegamento(b.Collegamento) Then
                    minPerCollegamento(b.Collegamento) = b.DataIndexMin
                End If
            Next
            For Each b As CapoBottone In lista
                b.PrimoIndiceCollegamento = minPerCollegamento(b.Collegamento)
                _bottoni.Add(b)
            Next
        Catch ex As Exception
            log.ScriviLog("WGBrailleHid.LeggiBottoni: " & ex.Message)
        Finally
            If caps <> IntPtr.Zero Then Marshal.FreeHGlobal(caps)
        End Try
    End Sub

    Private Sub CicloLettura()
        Try
            Dim buf(Math.Max(_dimInput, 8) - 1) As Byte
            While Not _disposto
                Dim letti As UInteger = 0
                If Not ReadFile(_lettura, buf, CUInt(buf.Length), letti, IntPtr.Zero) Then
                    If Not _disposto Then
                        log.ScriviLog("WGBrailleHid: lettura fallita, errore " & Marshal.GetLastWin32Error().ToString())
                        Perso()
                    End If
                    Exit While
                End If
                If letti > 0 Then ElaboraRapporto(buf, CInt(letti))
            End While
        Catch ex As Exception
            log.ScriviLog("WGBrailleHid.CicloLettura: " & ex.Message)
        End Try
    End Sub

    Private Sub ElaboraRapporto(buf As Byte(), lunghezza As Integer)
        Dim elenco As IntPtr = IntPtr.Zero
        Try
            Dim n As UInteger = 128
            elenco = Marshal.AllocHGlobal(CInt(n) * 8)
            If HidP_GetData(0, elenco, n, _preparsed, buf, CUInt(Math.Min(lunghezza, _dimInput))) <> &H110000 Then Return
            Dim premuti As TastoBraille = TastoBraille.Nessuno
            Dim routerOra As New HashSet(Of Integer)()
            For i As Integer = 0 To CInt(n) - 1
                Dim idx As Integer = Marshal.ReadInt16(elenco, i * 8) And &HFFFF
                Dim acceso As Boolean = Marshal.ReadByte(elenco, i * 8 + 4) <> 0
                If Not acceso Then Continue For
                For Each b As CapoBottone In _bottoni
                    If idx < b.DataIndexMin OrElse idx > b.DataIndexMax Then Continue For
                    Dim uso As Integer = b.UsoMin + If(b.UsoMax > b.UsoMin, idx - b.DataIndexMin, 0)
                    If uso = &H100 Then
                        routerOra.Add(idx - b.PrimoIndiceCollegamento)
                    Else
                        premuti = premuti Or TastoDaUso(uso)
                    End If
                    Exit For
                Next
            Next
            Dim nuovi As New List(Of Integer)()
            SyncLock _router
                For Each r As Integer In routerOra
                    If Not _router.Contains(r) Then nuovi.Add(r)
                Next
                _router.Clear()
                For Each r As Integer In routerOra
                    _router.Add(r)
                Next
            End SyncLock
            For Each r As Integer In nuovi
                RaiseEvent Instradamento(r)
            Next
            _accordo = _accordo Or premuti
            If premuti = TastoBraille.Nessuno AndAlso _premuti <> TastoBraille.Nessuno AndAlso _accordo <> TastoBraille.Nessuno Then
                Dim ac As TastoBraille = _accordo
                _accordo = TastoBraille.Nessuno
                RaiseEvent TastiPremuti(ac)
            End If
            _premuti = premuti
        Catch ex As Exception
            log.ScriviLog("WGBrailleHid.ElaboraRapporto: " & ex.Message)
        Finally
            If elenco <> IntPtr.Zero Then Marshal.FreeHGlobal(elenco)
        End Try
    End Sub

    Private Shared Function TastoDaUso(uso As Integer) As TastoBraille
        Select Case uso
            Case &H201 : Return TastoBraille.Punto1
            Case &H202 : Return TastoBraille.Punto2
            Case &H203 : Return TastoBraille.Punto3
            Case &H204 : Return TastoBraille.Punto4
            Case &H205 : Return TastoBraille.Punto5
            Case &H206 : Return TastoBraille.Punto6
            Case &H207 : Return TastoBraille.Punto7
            Case &H208 : Return TastoBraille.Punto8
            Case &H209 : Return TastoBraille.Spazio
            Case &H20A : Return TastoBraille.SpazioSinistro
            Case &H20B : Return TastoBraille.SpazioDestro
            Case &H211, &H216 : Return TastoBraille.FrecciaSu
            Case &H212, &H217 : Return TastoBraille.FrecciaGiu
            Case &H213, &H218 : Return TastoBraille.FrecciaSinistra
            Case &H214, &H219 : Return TastoBraille.FrecciaDestra
            Case &H21A : Return TastoBraille.PanSinistra
            Case &H21B : Return TastoBraille.PanDestra
            Case &H21C : Return TastoBraille.BasculaSu
            Case &H21D : Return TastoBraille.BasculaGiu
            Case &H20C : Return TastoBraille.F1
            Case &H20D : Return TastoBraille.F2
            Case &H20E : Return TastoBraille.F3
            Case &H20F : Return TastoBraille.F4
            Case Else : Return TastoBraille.Nessuno
        End Select
    End Function

    Public Sub Mostra(celle As Byte()) Implements IWGBrailleDisplay.Mostra
        Try
            If Not Collegato Then Return
            Dim n As Integer = _celle
            Dim dati(n - 1) As Byte
            If celle IsNot Nothing Then Array.Copy(celle, dati, Math.Min(n, celle.Length))
            Dim pack() As Byte = Impacchetta(dati)
            Dim rapporto(Math.Max(_dimOutput, 1) - 1) As Byte
            rapporto(0) = _idRapportoOutput
            Dim esito As Integer = HidP_SetUsageValueArray(1, CUShort(PaginaBraille), CUShort(_collCella), CUShort(_usoCella), pack, CUShort(pack.Length), _preparsed, rapporto, CUInt(rapporto.Length))
            If esito <> &H110000 Then
                log.ScriviLog("WGBrailleHid.Mostra: HidP_SetUsageValueArray ha risposto " & esito.ToString("X"))
                Return
            End If
            If Not HidD_SetOutputReport(_scrittura, rapporto, rapporto.Length) Then
                log.ScriviLog("WGBrailleHid.Mostra: scrittura fallita, errore " & Marshal.GetLastWin32Error().ToString())
                Perso()
            End If
        Catch ex As Exception
            log.ScriviLog("WGBrailleHid.Mostra: " & ex.Message)
            Perso()
        End Try
    End Sub

    ' Ogni cella occupa _bitCella bit, uno dopo l'altro.
    Private Function Impacchetta(celle As Byte()) As Byte()
        Dim bit As Integer = Math.Max(1, Math.Min(8, _bitCella))
        Dim totale As Integer = (celle.Length * bit + 7) \ 8
        Dim r(totale - 1) As Byte
        Dim maschera As Integer = (1 << bit) - 1
        For i As Integer = 0 To celle.Length - 1
            Dim v As Integer = celle(i) And maschera
            For k As Integer = 0 To bit - 1
                If (v And (1 << k)) <> 0 Then
                    Dim pos As Integer = i * bit + k
                    r(pos \ 8) = r(pos \ 8) Or CByte(1 << (pos Mod 8))
                End If
            Next
        Next
        Return r
    End Function

    Private Sub Perso()
        Dim eraCollegato As Boolean = _collegato
        _collegato = False
        If eraCollegato Then
            Try
                RaiseEvent Disconnessa()
            Catch ex As Exception
                log.ScriviLog("WGBrailleHid.Perso: " & ex.Message)
            End Try
        End If
    End Sub

    Public Sub Dispose() Implements IDisposable.Dispose
        Try
            _disposto = True
            _collegato = False
            If _lettura <> INVALID AndAlso _lettura <> IntPtr.Zero Then
                CancelIoEx(_lettura, IntPtr.Zero)
                CloseHandle(_lettura)
                _lettura = INVALID
            End If
            If _scrittura <> INVALID AndAlso _scrittura <> IntPtr.Zero Then
                CloseHandle(_scrittura)
                _scrittura = INVALID
            End If
            If _preparsed <> IntPtr.Zero Then
                HidD_FreePreparsedData(_preparsed)
                _preparsed = IntPtr.Zero
            End If
        Catch ex As Exception
            log.ScriviLog("WGBrailleHid.Dispose: " & ex.Message)
        End Try
    End Sub
End Class
