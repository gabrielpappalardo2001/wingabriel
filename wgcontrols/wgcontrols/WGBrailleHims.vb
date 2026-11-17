' ============================================================================================
' WGBrailleHims - driver delle barre HIMS: Braille Sense, Braille Edge, Smart Beetle
' ============================================================================================
'
' A cosa serve
'   Parla il protocollo delle barre HIMS (le stesse che NVDA pilota con il suo driver "hims"): le
'   Braille Sense 6 e 6 mini, le Braille Sense Classic e Polaris, le Braille Edge, le Smart Beetle.
'   Il collegamento puo' essere una porta seriale (Bluetooth SPP o cavo con porta COM virtuale) oppure
'   WinUSB (cavo, quando il dispositivo e' agganciato a WinUSB).
'
' Protocollo (ricavato dal driver di NVDA)
'   Barra verso computer: pacchetti di 10 byte, FA, modo, ?, dato, quattro byte di tasti, somma, FD.
'     modo 0: tasto di instradamento (dato = numero della cella, da 0)
'     modo 1: tasti premuti (maschera a 32 bit, little endian)
'     modo 2: numero di celle (dato)
'   Identificazione: 1C, due lettere, 1F.
'   Computer verso barra: FC FC 01 F0 <lunghezza 2 byte> <celle> F1 F2 00 00 F3 00 00 00 00 <somma> FD FD
'   Richiesta delle celle: come sopra con FB FB e 32 byte a zero.
'
' Errori
'   Nessun metodo solleva eccezioni: gli errori vanno nel log e la barra risulta scollegata.
' ============================================================================================
Imports System.IO
Imports System.IO.Ports
Imports System.Runtime.InteropServices
Imports System.Threading

Friend Interface ICollegamentoByte
    Inherits IDisposable
    Sub Scrivi(dati As Byte())
    Event Ricevuti(dati As Byte())
    Event Chiuso()
End Interface

Friend NotInheritable Class CollegamentoSeriale
    Implements ICollegamentoByte

    Private ReadOnly log As New Logger()
    Private _porta As SerialPort
    Private _chiuso As Boolean

    Public Event Ricevuti(dati As Byte()) Implements ICollegamentoByte.Ricevuti
    Public Event Chiuso() Implements ICollegamentoByte.Chiuso

    Public Sub New(nomePorta As String, baud As Integer)
        _porta = New SerialPort(nomePorta, baud, Parity.None, 8, StopBits.One)
        _porta.ReadTimeout = 200
        _porta.WriteTimeout = 1500
        _porta.Handshake = Handshake.None
        ' Come pyserial (che usa NVDA): DTR e RTS accesi.
        _porta.DtrEnable = True
        _porta.RtsEnable = True
        _porta.Open()
        AddHandler _porta.DataReceived, AddressOf OnDati
        AddHandler _porta.ErrorReceived, AddressOf OnErrore
    End Sub

    Private Sub OnDati(sender As Object, e As SerialDataReceivedEventArgs)
        Try
            Dim p As SerialPort = _porta
            If p Is Nothing OrElse Not p.IsOpen Then Return
            Dim n As Integer = p.BytesToRead
            If n <= 0 Then Return
            Dim buf(n - 1) As Byte
            Dim letti As Integer = p.Read(buf, 0, n)
            If letti <= 0 Then Return
            If letti < n Then ReDim Preserve buf(letti - 1)
            RaiseEvent Ricevuti(buf)
        Catch ex As Exception
            log.ScriviLog("CollegamentoSeriale.OnDati: " & ex.Message)
        End Try
    End Sub

    Private Sub OnErrore(sender As Object, e As SerialErrorReceivedEventArgs)
        Try
            log.ScriviLog("CollegamentoSeriale errore porta: " & e.EventType.ToString())
        Catch ex As Exception
            log.ScriviLog("CollegamentoSeriale.OnErrore: " & ex.Message)
        End Try
    End Sub

    Public Sub Scrivi(dati As Byte()) Implements ICollegamentoByte.Scrivi
        Try
            Dim p As SerialPort = _porta
            If p Is Nothing OrElse Not p.IsOpen Then Throw New IOException("porta chiusa")
            p.Write(dati, 0, dati.Length)
        Catch ex As Exception
            log.ScriviLog("CollegamentoSeriale.Scrivi: " & ex.Message)
            Segnala()
            Throw
        End Try
    End Sub

    Private Sub Segnala()
        If _chiuso Then Return
        _chiuso = True
        Try
            RaiseEvent Chiuso()
        Catch ex As Exception
            log.ScriviLog("CollegamentoSeriale.Segnala: " & ex.Message)
        End Try
    End Sub

    Public Sub Dispose() Implements IDisposable.Dispose
        Try
            _chiuso = True
            Dim p As SerialPort = _porta
            _porta = Nothing
            If p IsNot Nothing Then
                RemoveHandler p.DataReceived, AddressOf OnDati
                RemoveHandler p.ErrorReceived, AddressOf OnErrore
                Try
                    If p.IsOpen Then p.Close()
                Catch ex As Exception
                    log.ScriviLog("CollegamentoSeriale.Dispose (chiusura): " & ex.Message)
                End Try
                p.Dispose()
            End If
        Catch ex As Exception
            log.ScriviLog("CollegamentoSeriale.Dispose: " & ex.Message)
        End Try
    End Sub
End Class

''' <summary>Collegamento via WinUSB ai dispositivi HIMS che espongono l'interfaccia GUID_DEVINTERFACE_HIMS_WINUSB.</summary>
Friend NotInheritable Class CollegamentoWinUsb
    Implements ICollegamentoByte

    Public Shared ReadOnly GuidHims As New Guid("78A1C341-4539-11d3-B88D-00C04FAD5171")

    Private ReadOnly log As New Logger()
    Private _file As IntPtr = INVALID
    Private _winusb As IntPtr = IntPtr.Zero
    Private _pipeIn As Byte
    Private _pipeOut As Byte
    Private _chiuso As Boolean
    Private _thread As Thread

    Private Shared ReadOnly INVALID As New IntPtr(-1)

    Public Event Ricevuti(dati As Byte()) Implements ICollegamentoByte.Ricevuti
    Public Event Chiuso() Implements ICollegamentoByte.Chiuso

    <StructLayout(LayoutKind.Sequential)>
    Private Structure SP_DEVICE_INTERFACE_DATA
        Public cbSize As Integer
        Public InterfaceClassGuid As Guid
        Public Flags As Integer
        Public Reserved As IntPtr
    End Structure

    <StructLayout(LayoutKind.Sequential)>
    Private Structure USB_INTERFACE_DESCRIPTOR
        Public bLength As Byte
        Public bDescriptorType As Byte
        Public bInterfaceNumber As Byte
        Public bAlternateSetting As Byte
        Public bNumEndpoints As Byte
        Public bInterfaceClass As Byte
        Public bInterfaceSubClass As Byte
        Public bInterfaceProtocol As Byte
        Public iInterface As Byte
    End Structure

    <StructLayout(LayoutKind.Sequential)>
    Private Structure WINUSB_PIPE_INFORMATION
        Public PipeType As Integer
        Public PipeId As Byte
        Public MaximumPacketSize As UShort
        Public Interval As Byte
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
    Private Shared Function CloseHandle(h As IntPtr) As Boolean
    End Function

    <DllImport("winusb.dll", SetLastError:=True)>
    Private Shared Function WinUsb_Initialize(file As IntPtr, ByRef handle As IntPtr) As Boolean
    End Function

    <DllImport("winusb.dll", SetLastError:=True)>
    Private Shared Function WinUsb_Free(handle As IntPtr) As Boolean
    End Function

    <DllImport("winusb.dll", SetLastError:=True)>
    Private Shared Function WinUsb_QueryInterfaceSettings(handle As IntPtr, alt As Byte, ByRef descrittore As USB_INTERFACE_DESCRIPTOR) As Boolean
    End Function

    <DllImport("winusb.dll", SetLastError:=True)>
    Private Shared Function WinUsb_QueryPipe(handle As IntPtr, alt As Byte, indice As Byte, ByRef info As WINUSB_PIPE_INFORMATION) As Boolean
    End Function

    <DllImport("winusb.dll", SetLastError:=True)>
    Private Shared Function WinUsb_SetPipePolicy(handle As IntPtr, pipe As Byte, tipoPolicy As UInteger, lunghezza As UInteger, ByRef valore As UInteger) As Boolean
    End Function

    <DllImport("winusb.dll", SetLastError:=True)>
    Private Shared Function WinUsb_ReadPipe(handle As IntPtr, pipe As Byte, buffer As Byte(), lunghezza As UInteger, ByRef letti As UInteger, overlapped As IntPtr) As Boolean
    End Function

    <DllImport("winusb.dll", SetLastError:=True)>
    Private Shared Function WinUsb_WritePipe(handle As IntPtr, pipe As Byte, buffer As Byte(), lunghezza As UInteger, ByRef scritti As UInteger, overlapped As IntPtr) As Boolean
    End Function

    ''' <summary>Il percorso del primo dispositivo HIMS WinUSB presente, o stringa vuota.</summary>
    Public Shared Function TrovaPercorso() As String
        Dim info As IntPtr = IntPtr.Zero
        Dim logS As New Logger()
        Try
            Dim g As Guid = GuidHims
            info = SetupDiGetClassDevs(g, IntPtr.Zero, IntPtr.Zero, &H12) ' DIGCF_PRESENT | DIGCF_DEVICEINTERFACE
            If info = INVALID OrElse info = IntPtr.Zero Then Return ""
            Dim dati As New SP_DEVICE_INTERFACE_DATA()
            dati.cbSize = Marshal.SizeOf(GetType(SP_DEVICE_INTERFACE_DATA))
            If Not SetupDiEnumDeviceInterfaces(info, IntPtr.Zero, g, 0, dati) Then Return ""
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
            logS.ScriviLog("CollegamentoWinUsb.TrovaPercorso: " & ex.Message)
            Return ""
        Finally
            If info <> IntPtr.Zero AndAlso info <> INVALID Then SetupDiDestroyDeviceInfoList(info)
        End Try
    End Function

    Public Sub New(percorso As String)
        ' GENERIC_READ|GENERIC_WRITE, condivisione lettura/scrittura, OPEN_EXISTING, FILE_FLAG_OVERLAPPED
        _file = CreateFile(percorso, &HC0000000UI, 3UI, IntPtr.Zero, 3UI, &H40000000UI, IntPtr.Zero)
        If _file = INVALID Then Throw New IOException("apertura WinUSB fallita, errore " & Marshal.GetLastWin32Error().ToString())
        If Not WinUsb_Initialize(_file, _winusb) Then
            Dim err As Integer = Marshal.GetLastWin32Error()
            Dispose()
            Throw New IOException("WinUsb_Initialize fallita, errore " & err.ToString())
        End If
        Dim d As New USB_INTERFACE_DESCRIPTOR()
        If Not WinUsb_QueryInterfaceSettings(_winusb, 0, d) Then
            Dispose()
            Throw New IOException("interfaccia WinUSB non leggibile")
        End If
        Dim haIn As Boolean = False
        Dim haOut As Boolean = False
        For i As Integer = 0 To d.bNumEndpoints - 1
            Dim p As New WINUSB_PIPE_INFORMATION()
            If Not WinUsb_QueryPipe(_winusb, 0, CByte(i), p) Then Continue For
            If p.PipeType <> 2 Then Continue For ' bulk
            If (p.PipeId And &H80) <> 0 Then
                _pipeIn = p.PipeId
                haIn = True
            Else
                _pipeOut = p.PipeId
                haOut = True
            End If
        Next
        If Not (haIn AndAlso haOut) Then
            Dispose()
            Throw New IOException("canali bulk non trovati")
        End If
        Dim timeout As UInteger = 3000UI
        WinUsb_SetPipePolicy(_winusb, _pipeIn, 3UI, 4UI, timeout) ' PIPE_TRANSFER_TIMEOUT
        _thread = New Thread(AddressOf CicloLettura)
        _thread.IsBackground = True
        _thread.Start()
    End Sub

    Private Sub CicloLettura()
        Try
            Dim buf(63) As Byte
            While Not _chiuso
                Dim letti As UInteger = 0
                If WinUsb_ReadPipe(_winusb, _pipeIn, buf, 64UI, letti, IntPtr.Zero) Then
                    If letti > 0 Then
                        Dim copia(CInt(letti) - 1) As Byte
                        Array.Copy(buf, copia, CInt(letti))
                        RaiseEvent Ricevuti(copia)
                    End If
                Else
                    Dim err As Integer = Marshal.GetLastWin32Error()
                    If err = 121 Then Continue While ' timeout
                    If Not _chiuso Then
                        log.ScriviLog("CollegamentoWinUsb: lettura fallita, errore " & err.ToString())
                        _chiuso = True
                        RaiseEvent Chiuso()
                    End If
                    Exit While
                End If
            End While
        Catch ex As Exception
            log.ScriviLog("CollegamentoWinUsb.CicloLettura: " & ex.Message)
        End Try
    End Sub

    Public Sub Scrivi(dati As Byte()) Implements ICollegamentoByte.Scrivi
        Try
            If _chiuso OrElse _winusb = IntPtr.Zero Then Throw New IOException("collegamento chiuso")
            Dim scritti As UInteger = 0
            If Not WinUsb_WritePipe(_winusb, _pipeOut, dati, CUInt(dati.Length), scritti, IntPtr.Zero) Then
                Throw New IOException("scrittura WinUSB fallita, errore " & Marshal.GetLastWin32Error().ToString())
            End If
        Catch ex As Exception
            log.ScriviLog("CollegamentoWinUsb.Scrivi: " & ex.Message)
            Throw
        End Try
    End Sub

    Public Sub Dispose() Implements IDisposable.Dispose
        Try
            _chiuso = True
            If _winusb <> IntPtr.Zero Then
                WinUsb_Free(_winusb)
                _winusb = IntPtr.Zero
            End If
            If _file <> INVALID AndAlso _file <> IntPtr.Zero Then
                CloseHandle(_file)
                _file = INVALID
            End If
        Catch ex As Exception
            log.ScriviLog("CollegamentoWinUsb.Dispose: " & ex.Message)
        End Try
    End Sub
End Class

Friend NotInheritable Class WGBrailleHims
    Implements IWGBrailleDisplay

    ''' <summary>Vero se l'ultima apertura e' fallita perche' la porta e' usata da un altro programma.</summary>
    Public Shared Property PortaOccupata As Boolean

    Private ReadOnly log As New Logger()
    Private ReadOnly _blocco As New Object()
    Private ReadOnly _scrittura As New Object()
    Private ReadOnly _buffer As New List(Of Byte)()
    Private ReadOnly _celleRicevute As New ManualResetEventSlim(False)
    Private ReadOnly _identificato As New ManualResetEventSlim(False)
    Private WithEvents _link As ICollegamentoByte
    Private _numeroCelle As Integer = 0
    Private _idModello As String = ""
    Private _collegato As Boolean
    Private _collegamento As String = "Cavo"
    Private _nome As String = "Barra HIMS"
    Private _disposto As Boolean

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
            Return _numeroCelle
        End Get
    End Property

    Public ReadOnly Property OttoPunti As Boolean Implements IWGBrailleDisplay.OttoPunti
        Get
            Return True
        End Get
    End Property

    Public ReadOnly Property Collegato As Boolean Implements IWGBrailleDisplay.Collegato
        Get
            Return _collegato AndAlso Not _disposto
        End Get
    End Property

    Public ReadOnly Property Collegamento As String Implements IWGBrailleDisplay.Collegamento
        Get
            Return _collegamento
        End Get
    End Property

    ''' <summary>Apre il candidato e controlla che dall'altra parte ci sia una barra HIMS. Restituisce Nothing se non c'e'.</summary>
    Public Shared Function Prova(c As CandidatoBraille) As WGBrailleHims
        Dim logS As New Logger()
        Dim barra As WGBrailleHims = Nothing
        Try
            Dim link As ICollegamentoByte
            If c.Tipo = "WinUsb" Then
                link = New CollegamentoWinUsb(c.Percorso)
            Else
                link = New CollegamentoSeriale(c.Percorso, 115200)
            End If
            barra = New WGBrailleHims()
            barra._collegamento = c.Collegamento
            barra._link = link
            If Not barra.Avvia(c) Then
                barra.Dispose()
                Return Nothing
            End If
            Return barra
        Catch exOccupata As UnauthorizedAccessException
            PortaOccupata = True
            logS.ScriviLog("WGBrailleHims.Prova " & c.ToString() & ": porta occupata da un altro programma (" & exOccupata.Message & ")")
            Return Nothing
        Catch ex As Exception
            logS.ScriviLog("WGBrailleHims.Prova " & c.ToString() & ": " & ex.Message)
            Try
                If barra IsNot Nothing Then barra.Dispose()
            Catch ex2 As Exception
                logS.ScriviLog("WGBrailleHims.Prova (chiusura): " & ex2.Message)
            End Try
            Return Nothing
        End Try
    End Function

    Private Sub New()
    End Sub

    Private Function Avvia(c As CandidatoBraille) As Boolean
        Try
            For tentativo As Integer = 1 To 3
                _celleRicevute.Reset()
                InviaPacchetto(&HFB, &H1, New Byte(31) {}, Nothing)
                If _celleRicevute.Wait(If(c.Collegamento = "Bluetooth", 1500, 700)) Then Exit For
            Next
            If _numeroCelle <= 0 Then Return False

            ' L'identificazione serve solo a distinguere i tasti: se non risponde si usa la mappa delle Braille Sense.
            For Each id As String In New String() {"LX", "BS", "BE", "QX", "SX"}
                _identificato.Reset()
                Scrivi(New Byte() {&H1C, CByte(AscW(id(0))), CByte(AscW(id(1))), &H1F})
                If _identificato.Wait(200) Then Exit For
            Next

            _collegato = True
            _nome = NomeModello(c)
            Return True
        Catch ex As Exception
            log.ScriviLog("WGBrailleHims.Avvia: " & ex.Message)
            Return False
        End Try
    End Function

    Private Function NomeModello(c As CandidatoBraille) As String
        Try
            Select Case _idModello
                Case "BE" : Return "Braille Edge"
                Case "QX", "SX" : Return "Braille Sense QWERTY"
                Case "BS" : Return "Braille Sense Classic"
            End Select
            If _numeroCelle = 14 Then Return "Smart Beetle"
            If _numeroCelle >= 40 Then Return "Braille Edge"
            If Not String.IsNullOrEmpty(c.Descrizione) AndAlso c.Descrizione.IndexOf("Braille", StringComparison.OrdinalIgnoreCase) >= 0 Then Return c.Descrizione
            Return "Braille Sense"
        Catch ex As Exception
            log.ScriviLog("WGBrailleHims.NomeModello: " & ex.Message)
            Return "Barra HIMS"
        End Try
    End Function

    Private Sub Scrivi(dati As Byte())
        SyncLock _scrittura
            _link.Scrivi(dati)
        End SyncLock
    End Sub

    Private Sub InviaPacchetto(tipo As Byte, modo As Byte, dati1 As Byte(), dati2 As Byte())
        If dati2 Is Nothing Then dati2 = New Byte() {}
        Dim p As New List(Of Byte)()
        p.Add(tipo) : p.Add(tipo)
        p.Add(modo)
        p.Add(&HF0)
        p.Add(CByte(dati1.Length And &HFF)) : p.Add(CByte((dati1.Length >> 8) And &HFF))
        p.AddRange(dati1)
        p.Add(&HF1)
        p.Add(&HF2)
        p.Add(CByte(dati2.Length And &HFF)) : p.Add(CByte((dati2.Length >> 8) And &HFF))
        p.AddRange(dati2)
        p.Add(&HF3)
        p.Add(0) : p.Add(0) : p.Add(0) : p.Add(0)
        Dim posSomma As Integer = p.Count
        p.Add(0)
        p.Add(&HFD) : p.Add(&HFD)
        Dim somma As Integer = 0
        For i As Integer = 0 To p.Count - 1
            If i <> posSomma Then somma += p(i)
        Next
        p(posSomma) = CByte(somma And &HFF)
        Scrivi(p.ToArray())
    End Sub

    Public Sub Mostra(celle As Byte()) Implements IWGBrailleDisplay.Mostra
        Try
            If Not Collegato Then Return
            Dim n As Integer = _numeroCelle
            Dim dati(n - 1) As Byte
            If celle IsNot Nothing Then Array.Copy(celle, dati, Math.Min(n, celle.Length))
            InviaPacchetto(&HFC, &H1, dati, Nothing)
        Catch ex As Exception
            log.ScriviLog("WGBrailleHims.Mostra: " & ex.Message)
            Perso()
        End Try
    End Sub

    ''' <summary>Controlla che la barra ci sia ancora (la richiesta del numero di celle risponde sempre).</summary>
    Public Function Verifica() As Boolean
        Try
            If Not Collegato Then Return False
            InviaPacchetto(&HFB, &H1, New Byte(31) {}, Nothing)
            Return True
        Catch ex As Exception
            log.ScriviLog("WGBrailleHims.Verifica: " & ex.Message)
            Perso()
            Return False
        End Try
    End Function

    Private Sub Perso()
        Dim eraCollegato As Boolean = _collegato
        _collegato = False
        If eraCollegato Then
            Try
                RaiseEvent Disconnessa()
            Catch ex As Exception
                log.ScriviLog("WGBrailleHims.Perso: " & ex.Message)
            End Try
        End If
    End Sub

    Private Sub OnRicevuti(dati As Byte()) Handles _link.Ricevuti
        Try
            Dim pacchetti As New List(Of Byte())()
            SyncLock _blocco
                _buffer.AddRange(dati)
                While _buffer.Count > 0
                    Dim b As Byte = _buffer(0)
                    If b = &HFA Then
                        If _buffer.Count < 10 Then Exit While
                        pacchetti.Add(_buffer.GetRange(0, 10).ToArray())
                        _buffer.RemoveRange(0, 10)
                    ElseIf b = &H1C Then
                        If _buffer.Count < 3 Then Exit While
                        _idModello = ChrW(_buffer(1)) & ChrW(_buffer(2))
                        Dim togli As Integer = If(_buffer.Count >= 4 AndAlso _buffer(3) = &H1F, 4, 3)
                        _buffer.RemoveRange(0, togli)
                        _identificato.Set()
                    Else
                        _buffer.RemoveAt(0)
                    End If
                End While
            End SyncLock
            For Each p As Byte() In pacchetti
                ElaboraPacchetto(p)
            Next
        Catch ex As Exception
            log.ScriviLog("WGBrailleHims.OnRicevuti: " & ex.Message)
        End Try
    End Sub

    Private Sub OnChiuso() Handles _link.Chiuso
        Perso()
    End Sub

    Private Sub ElaboraPacchetto(p As Byte())
        Try
            Select Case p(1)
                Case 0
                    RaiseEvent Instradamento(p(3))
                Case 1
                    Dim maschera As UInteger = BitConverter.ToUInt32(p, 4)
                    Dim tasti As TastoBraille = Traduci(maschera)
                    If tasti <> TastoBraille.Nessuno Then RaiseEvent TastiPremuti(tasti)
                Case 2
                    _numeroCelle = p(3)
                    _celleRicevute.Set()
            End Select
        Catch ex As Exception
            log.ScriviLog("WGBrailleHims.ElaboraPacchetto: " & ex.Message)
        End Try
    End Sub

    ''' <summary>Dalla maschera dei tasti HIMS ai tasti comuni.</summary>
    Private Function Traduci(m As UInteger) As TastoBraille
        Dim t As TastoBraille = TastoBraille.Nessuno
        Try
            t = CType(CInt(m And &HFFUI), TastoBraille) ' i punti 1-8 coincidono
            If (m And 256UI) <> 0 Then t = t Or TastoBraille.Spazio
            Dim smart As Boolean = (_numeroCelle = 14)
            If smart Then
                If (m And 512UI) <> 0 Then t = t Or TastoBraille.F1
                If (m And 4096UI) <> 0 Then t = t Or TastoBraille.F2
                If (m And 2048UI) <> 0 Then t = t Or TastoBraille.F3
                If (m And 1024UI) <> 0 Then t = t Or TastoBraille.F4
                If (m And 262144UI) <> 0 Then t = t Or TastoBraille.PanSinistra
                If (m And 524288UI) <> 0 Then t = t Or TastoBraille.PanDestra
            Else
                If (m And 512UI) <> 0 Then t = t Or TastoBraille.F1
                If (m And 1024UI) <> 0 Then t = t Or TastoBraille.F2
                If (m And 2048UI) <> 0 Then t = t Or TastoBraille.F3
                If (m And 4096UI) <> 0 Then t = t Or TastoBraille.F4
                If (m And 8192UI) <> 0 Then t = t Or TastoBraille.PanSinistra
                If (m And 16384UI) <> 0 Then t = t Or TastoBraille.PanDestra
                If _idModello = "BE" Then
                    If (m And (65536UI Or 131072UI)) <> 0 Then t = t Or TastoBraille.PanSinistra
                    If (m And (262144UI Or 524288UI)) <> 0 Then t = t Or TastoBraille.PanDestra
                Else
                    If (m And (65536UI Or 262144UI)) <> 0 Then t = t Or TastoBraille.PanSinistra
                    If (m And (131072UI Or 524288UI)) <> 0 Then t = t Or TastoBraille.PanDestra
                End If
            End If
            ' Tasti di movimento accanto allo spazio: quelli di sinistra e di destra, a quattro frecce ciascuno
            ' (stessi bit delle Braille Edge nel driver di NVDA); servono tutti a scorrere i menu.
            If (m And (16777216UI Or 268435456UI)) <> 0 Then t = t Or TastoBraille.FrecciaSu
            If (m And (33554432UI Or 536870912UI)) <> 0 Then t = t Or TastoBraille.FrecciaGiu
            If (m And (67108864UI Or 1073741824UI)) <> 0 Then t = t Or TastoBraille.FrecciaSinistra
            If (m And (134217728UI Or 2147483648UI)) <> 0 Then t = t Or TastoBraille.FrecciaDestra
            Dim sconosciuti As UInteger = m And Not (&HFFFFUI Or 65536UI Or 131072UI Or 262144UI Or 524288UI Or 16777216UI Or 33554432UI Or 67108864UI Or 134217728UI Or 268435456UI Or 536870912UI Or 1073741824UI Or 2147483648UI)
            If sconosciuti <> 0UI Then log.ScriviLog("WGBrailleHims: tasti non riconosciuti 0x" & sconosciuti.ToString("X") & " (maschera completa 0x" & m.ToString("X") & ")")
        Catch ex As Exception
            log.ScriviLog("WGBrailleHims.Traduci: " & ex.Message)
        End Try
        Return t
    End Function

    Public Sub Dispose() Implements IDisposable.Dispose
        Try
            _disposto = True
            _collegato = False
            Dim l As ICollegamentoByte = _link
            _link = Nothing
            If l IsNot Nothing Then l.Dispose()
        Catch ex As Exception
            log.ScriviLog("WGBrailleHims.Dispose: " & ex.Message)
        End Try
    End Sub
End Class
