Imports System.Collections.Concurrent
Imports System.Collections.Generic
Imports System.Diagnostics
Imports System.Drawing
Imports System.Globalization
Imports System.IO
Imports System.Net
Imports System.Net.Http
Imports System.Runtime.InteropServices
Imports System.Speech.AudioFormat
Imports System.Speech.Synthesis
Imports System.Text
Imports System.Threading
Imports System.Windows.Forms
Imports System.Xml.Linq

' Procedura di aggiornamento per il lettore di schermo, con verifica dei rilasci firmati e gestione delle configurazioni locali.
Friend NotInheritable Class ImprontaSha256

    Private Shared ReadOnly _costanti As UInteger() = New UInteger() { _
        &H428A2F98UI, &H71374491UI, &HB5C0FBCFUI, &HE9B5DBA5UI, _
        &H3956C25BUI, &H59F111F1UI, &H923F82A4UI, &HAB1C5ED5UI, _
        &HD807AA98UI, &H12835B01UI, &H243185BEUI, &H550C7DC3UI, _
        &H72BE5D74UI, &H80DEB1FEUI, &H9BDC06A7UI, &HC19BF174UI, _
        &HE49B69C1UI, &HEFBE4786UI, &H0FC19DC6UI, &H240CA1CCUI, _
        &H2DE92C6FUI, &H4A7484AAUI, &H5CB0A9DCUI, &H76F988DAUI, _
        &H983E5152UI, &HA831C66DUI, &HB00327C8UI, &HBF597FC7UI, _
        &HC6E00BF3UI, &HD5A79147UI, &H06CA6351UI, &H14292967UI, _
        &H27B70A85UI, &H2E1B2138UI, &H4D2C6DFCUI, &H53380D13UI, _
        &H650A7354UI, &H766A0ABBUI, &H81C2C92EUI, &H92722C85UI, _
        &HA2BFE8A1UI, &HA81A664BUI, &HC24B8B70UI, &HC76C51A3UI, _
        &HD192E819UI, &HD6990624UI, &HF40E3585UI, &H106AA070UI, _
        &H19A4C116UI, &H1E376C08UI, &H2748774CUI, &H34B0BCB5UI, _
        &H391C0CB3UI, &H4ED8AA4AUI, &H5B9CCA4FUI, &H682E6FF3UI, _
        &H748F82EEUI, &H78A5636FUI, &H84C87814UI, &H8CC70208UI, _
        &H90BEFFFAUI, &HA4506CEBUI, &HBEF9A3F7UI, &HC67178F2UI _
    }

    Private ReadOnly _stato As UInteger() = New UInteger() {&H6A09E667UI, &HBB67AE85UI, &H3C6EF372UI, &HA54FF53AUI, &H510E527FUI, &H9B05688CUI, &H1F83D9ABUI, &H5BE0CD19UI}
    Private ReadOnly _residuo As Byte() = New Byte(63) {}
    Private ReadOnly _parole As UInteger() = New UInteger(63) {}
    Private _lunghezzaResiduo As Integer = 0
    Private _byteTotali As Long = 0

    Private Shared Function RuotaDestra(ByVal valore As UInteger, ByVal posizioni As Integer) As UInteger
        Dim esteso As ULong = CULng(valore)
        Return CUInt(((esteso >> posizioni) Or (esteso << (32 - posizioni))) And &HFFFFFFFFUL)
    End Function

    Private Shared Function Somma(ByVal a As UInteger, ByVal b As UInteger) As UInteger
        Return CUInt((CULng(a) + CULng(b)) And &HFFFFFFFFUL)
    End Function

    Private Sub ElaboraBlocco(ByVal dati As Byte(), ByVal partenza As Integer)
        Dim indice As Integer

        For indice = 0 To 15
            Dim posizione As Integer = partenza + (indice * 4)
            Dim valore As ULong = (CULng(dati(posizione)) << 24) Or (CULng(dati(posizione + 1)) << 16) Or (CULng(dati(posizione + 2)) << 8) Or CULng(dati(posizione + 3))
            _parole(indice) = CUInt(valore And &HFFFFFFFFUL)
        Next

        For indice = 16 To 63
            Dim precedente As UInteger = _parole(indice - 15)
            Dim s0 As UInteger = RuotaDestra(precedente, 7) Xor RuotaDestra(precedente, 18) Xor CUInt(CULng(precedente) >> 3)

            Dim ultima As UInteger = _parole(indice - 2)
            Dim s1 As UInteger = RuotaDestra(ultima, 17) Xor RuotaDestra(ultima, 19) Xor CUInt(CULng(ultima) >> 10)

            _parole(indice) = Somma(Somma(_parole(indice - 16), s0), Somma(_parole(indice - 7), s1))
        Next

        Dim a As UInteger = _stato(0)
        Dim b As UInteger = _stato(1)
        Dim c As UInteger = _stato(2)
        Dim d As UInteger = _stato(3)
        Dim e As UInteger = _stato(4)
        Dim f As UInteger = _stato(5)
        Dim g As UInteger = _stato(6)
        Dim h As UInteger = _stato(7)

        For indice = 0 To 63
            Dim sigma1 As UInteger = RuotaDestra(e, 6) Xor RuotaDestra(e, 11) Xor RuotaDestra(e, 25)
            Dim scelta As UInteger = (e And f) Xor ((Not e) And g)
            Dim t1 As UInteger = Somma(Somma(Somma(h, sigma1), Somma(scelta, _costanti(indice))), _parole(indice))

            Dim sigma0 As UInteger = RuotaDestra(a, 2) Xor RuotaDestra(a, 13) Xor RuotaDestra(a, 22)
            Dim maggioranza As UInteger = (a And b) Xor (a And c) Xor (b And c)
            Dim t2 As UInteger = Somma(sigma0, maggioranza)

            h = g
            g = f
            f = e
            e = Somma(d, t1)
            d = c
            c = b
            b = a
            a = Somma(t1, t2)
        Next

        _stato(0) = Somma(_stato(0), a)
        _stato(1) = Somma(_stato(1), b)
        _stato(2) = Somma(_stato(2), c)
        _stato(3) = Somma(_stato(3), d)
        _stato(4) = Somma(_stato(4), e)
        _stato(5) = Somma(_stato(5), f)
        _stato(6) = Somma(_stato(6), g)
        _stato(7) = Somma(_stato(7), h)
    End Sub

    Private Sub AggiungiDati(ByVal dati As Byte(), ByVal lunghezza As Integer, ByVal conteggia As Boolean)
        If lunghezza <= 0 Then
            Return
        End If

        If conteggia Then
            _byteTotali += lunghezza
        End If

        Dim posizione As Integer = 0

        If _lunghezzaResiduo > 0 Then
            Dim mancanti As Integer = 64 - _lunghezzaResiduo

            If lunghezza < mancanti Then
                Array.Copy(dati, 0, _residuo, _lunghezzaResiduo, lunghezza)
                _lunghezzaResiduo += lunghezza
                Return
            End If

            Array.Copy(dati, 0, _residuo, _lunghezzaResiduo, mancanti)
            ElaboraBlocco(_residuo, 0)
            posizione = mancanti
            _lunghezzaResiduo = 0
        End If

        While posizione + 64 <= lunghezza
            ElaboraBlocco(dati, posizione)
            posizione += 64
        End While

        Dim resto As Integer = lunghezza - posizione

        If resto > 0 Then
            Array.Copy(dati, posizione, _residuo, 0, resto)
            _lunghezzaResiduo = resto
        End If
    End Sub

    Friend Sub Aggiorna(ByVal dati As Byte(), ByVal lunghezza As Integer)
        AggiungiDati(dati, lunghezza, True)
    End Sub

    Friend Function Finalizza() As String
        Dim bitTotali As Long = _byteTotali * 8L

        Dim terminatore As Byte() = New Byte(0) {}
        terminatore(0) = &H80
        AggiungiDati(terminatore, 1, False)

        Dim riempimento As Byte() = New Byte(0) {}
        riempimento(0) = 0

        While _lunghezzaResiduo <> 56
            AggiungiDati(riempimento, 1, False)
        End While

        Dim lunghezzaFinale As Byte() = New Byte(7) {}
        Dim indice As Integer

        For indice = 0 To 7
            lunghezzaFinale(indice) = CByte((bitTotali >> (56 - (8 * indice))) And &HFFL)
        Next

        AggiungiDati(lunghezzaFinale, 8, False)

        Dim risultato As New StringBuilder(64)

        For indice = 0 To 7
            risultato.Append(_stato(indice).ToString("x8", Globalization.CultureInfo.InvariantCulture))
        Next

        Return risultato.ToString()
    End Function

    Friend Shared Function DaFile(ByVal percorsoFile As String) As String
        Dim calcolatore As New ImprontaSha256()
        Dim buffer As Byte() = New Byte(65535) {}

        Using flusso As New IO.FileStream(percorsoFile, IO.FileMode.Open, IO.FileAccess.Read, IO.FileShare.ReadWrite, 65536)
            Dim letti As Integer = flusso.Read(buffer, 0, buffer.Length)

            While letti > 0
                calcolatore.Aggiorna(buffer, letti)
                letti = flusso.Read(buffer, 0, buffer.Length)
            End While
        End Using

        Return calcolatore.Finalizza()
    End Function

End Class

Friend NotInheritable Class ApiGdi

    Private Sub New()
    End Sub

    Friend Const TRANSPARENT As Integer = 1
    Friend Const DT_LEFT As Integer = &H0
    Friend Const DT_CENTER As Integer = &H1
    Friend Const DT_VCENTER As Integer = &H4
    Friend Const DT_SINGLELINE As Integer = &H20
    Friend Const DT_NOPREFIX As Integer = &H800
    Friend Const DT_END_ELLIPSIS As Integer = &H8000
    Friend Const FW_NORMAL As Integer = 400
    Friend Const FW_BOLD As Integer = 700
    Friend Const DEFAULT_CHARSET As Integer = 1
    Friend Const OUT_DEFAULT_PRECIS As Integer = 0
    Friend Const CLIP_DEFAULT_PRECIS As Integer = 0
    Friend Const CLEARTYPE_QUALITY As Integer = 5
    Friend Const DEFAULT_PITCH As Integer = 0

    <StructLayout(LayoutKind.Sequential)>
    Friend Structure RECT
        Public Left As Integer
        Public Top As Integer
        Public Right As Integer
        Public Bottom As Integer
    End Structure

    <DllImport("gdi32.dll", SetLastError:=True)>
    Friend Shared Function CreateSolidBrush(ByVal colore As Integer) As IntPtr
    End Function

    <DllImport("user32.dll", SetLastError:=True)>
    Friend Shared Function FillRect(ByVal hdc As IntPtr, ByRef area As RECT, ByVal pennello As IntPtr) As Integer
    End Function

    <DllImport("user32.dll", SetLastError:=True)>
    Friend Shared Function FrameRect(ByVal hdc As IntPtr, ByRef area As RECT, ByVal pennello As IntPtr) As Integer
    End Function

    <DllImport("gdi32.dll", SetLastError:=True)>
    Friend Shared Function DeleteObject(ByVal oggetto As IntPtr) As Boolean
    End Function

    <DllImport("gdi32.dll", SetLastError:=True)>
    Friend Shared Function SelectObject(ByVal hdc As IntPtr, ByVal oggetto As IntPtr) As IntPtr
    End Function

    <DllImport("gdi32.dll", SetLastError:=True)>
    Friend Shared Function SetBkMode(ByVal hdc As IntPtr, ByVal modalita As Integer) As Integer
    End Function

    <DllImport("gdi32.dll", SetLastError:=True)>
    Friend Shared Function SetTextColor(ByVal hdc As IntPtr, ByVal colore As Integer) As Integer
    End Function

    <DllImport("user32.dll", CharSet:=CharSet.Unicode, SetLastError:=True)>
    Friend Shared Function DrawText(ByVal hdc As IntPtr, ByVal testo As String, ByVal lunghezza As Integer, ByRef area As RECT, ByVal formato As Integer) As Integer
    End Function

    <DllImport("gdi32.dll", CharSet:=CharSet.Unicode, SetLastError:=True)>
    Friend Shared Function CreateFont(ByVal altezza As Integer, ByVal larghezza As Integer, ByVal escapement As Integer, ByVal orientamento As Integer, ByVal peso As Integer, ByVal corsivo As Integer, ByVal sottolineato As Integer, ByVal barrato As Integer, ByVal setCaratteri As Integer, ByVal precisioneOutput As Integer, ByVal precisioneClip As Integer, ByVal qualita As Integer, ByVal passo As Integer, ByVal nomeCarattere As String) As IntPtr
    End Function

    Friend Shared Function DaColore(ByVal colore As Color) As Integer
        Return CInt(colore.R) Or (CInt(colore.G) << 8) Or (CInt(colore.B) << 16)
    End Function

    Friend Shared Function CreaRettangolo(ByVal area As Rectangle) As RECT
        Dim risultato As RECT
        risultato.Left = area.Left
        risultato.Top = area.Top
        risultato.Right = area.Right
        risultato.Bottom = area.Bottom
        Return risultato
    End Function

End Class

Friend NotInheritable Class ApiFile

    Private Sub New()
    End Sub

    Friend Const MOVEFILE_REPLACE_EXISTING As Integer = &H1
    Friend Const MOVEFILE_DELAY_UNTIL_REBOOT As Integer = &H4

    <DllImport("kernel32.dll", CharSet:=CharSet.Unicode, SetLastError:=True)>
    Friend Shared Function DeleteFileW(ByVal percorsoFile As String) As Boolean
    End Function

    <DllImport("kernel32.dll", CharSet:=CharSet.Unicode, SetLastError:=True)>
    Friend Shared Function MoveFileExW(ByVal percorsoOrigine As String, ByVal percorsoDestinazione As String, ByVal opzioni As Integer) As Boolean
    End Function

End Class

Friend NotInheritable Class RegistroAggiornamento

    Private Shared ReadOnly _blocco As New Object()
    Private Shared _percorso As String = Nothing

    Private Sub New()
    End Sub

    Friend Shared Sub Scrivi(ByVal messaggio As String)
        Try
            SyncLock _blocco
                If String.IsNullOrEmpty(_percorso) Then
                    _percorso = Path.Combine(Application.StartupPath, "wg_updater_log.txt")
                End If

                Try
                    Dim informazioni As New FileInfo(_percorso)
                    If informazioni.Exists AndAlso informazioni.Length > 2097152 Then
                        Dim percorsoStorico As String = _percorso & ".old"
                        If File.Exists(percorsoStorico) Then
                            File.Delete(percorsoStorico)
                        End If
                        File.Move(_percorso, percorsoStorico)
                    End If
                Catch
                End Try

                File.AppendAllText(_percorso, DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture) & " - " & messaggio & Environment.NewLine, Encoding.UTF8)
            End SyncLock
        Catch
        End Try
    End Sub

End Class

Friend NotInheritable Class LettoriSchermo

    Private Shared ReadOnly _nomiProcesso As String() = New String() {"nvda", "jfw", "Narrator", "Magnify", "dolphinsam", "supernova"}

    Private Sub New()
    End Sub

    Friend Shared Function Disattiva() As Boolean
        Dim chiusoQualcosa As Boolean = False

        Try
            For Each nome As String In _nomiProcesso
                Try
                    Dim processi() As Process = Process.GetProcessesByName(nome)

                    If processi Is Nothing OrElse processi.Length = 0 Then
                        Continue For
                    End If

                    For Each processo As Process In processi
                        Try
                            If processo.HasExited Then
                                Continue For
                            End If

                            If String.Equals(nome, "nvda", StringComparison.OrdinalIgnoreCase) Then
                                If ChiudiNvdaConComando(processo) Then
                                    chiusoQualcosa = True
                                    Continue For
                                End If
                            End If

                            Try
                                processo.CloseMainWindow()
                            Catch
                            End Try

                            If Not processo.WaitForExit(2500) Then
                                processo.Kill()
                                processo.WaitForExit(4000)
                            End If

                            chiusoQualcosa = True
                        Catch exProcesso As Exception
                            RegistroAggiornamento.Scrivi("LettoriSchermo.Disattiva (processo " & nome & "): " & exProcesso.Message)
                        Finally
                            Try
                                processo.Dispose()
                            Catch
                            End Try
                        End Try
                    Next
                Catch exNome As Exception
                    RegistroAggiornamento.Scrivi("LettoriSchermo.Disattiva (" & nome & "): " & exNome.Message)
                End Try
            Next

            If chiusoQualcosa Then
                Try
                    Thread.Sleep(700)
                Catch
                End Try
            End If
        Catch ex As Exception
            RegistroAggiornamento.Scrivi("LettoriSchermo.Disattiva: " & ex.Message)
        End Try

        Return chiusoQualcosa
    End Function

    Private Shared Function ChiudiNvdaConComando(ByVal processo As Process) As Boolean
        Try
            Dim percorso As String = String.Empty

            Try
                percorso = processo.MainModule.FileName
            Catch exModulo As Exception
                RegistroAggiornamento.Scrivi("LettoriSchermo.ChiudiNvdaConComando (modulo): " & exModulo.Message)
                Return False
            End Try

            If String.IsNullOrEmpty(percorso) OrElse Not File.Exists(percorso) Then
                Return False
            End If

            Dim informazioni As New ProcessStartInfo()
            informazioni.FileName = percorso
            informazioni.Arguments = "-q"
            informazioni.UseShellExecute = False
            informazioni.CreateNoWindow = True

            Process.Start(informazioni)

            Return processo.WaitForExit(4000)
        Catch ex As Exception
            RegistroAggiornamento.Scrivi("LettoriSchermo.ChiudiNvdaConComando: " & ex.Message)
            Return False
        End Try
    End Function

End Class

Friend NotInheritable Class ConfigurazioneAggiornamento

    ' La cartella della configurazione di WinGabriel: nei dati locali dell'utente se il programma è installato in Programmi\WinGabriel, altrimenti
    ' config accanto al programma. Le versioni di prima la tenevano in dati\config: se la nuova non c'è ancora si usa quella.
    Private Shared Function CartellaConfigurazione() As String
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

    ' Il file di configurazione: quello nuovo o, se non c'è, quello delle versioni di prima.
    Private Shared Function PercorsoConfigurazioneAggiornamento() As String
        Return Path.Combine(CartellaConfigurazione(), "config.xml")
    End Function

    Private ReadOnly _valori As New Dictionary(Of String, String)(StringComparer.OrdinalIgnoreCase)

    Friend Sub New()
        Try
            Dim percorso As String = PercorsoConfigurazioneAggiornamento()

            If Not File.Exists(percorso) Then
                Return
            End If

            Dim documento As XDocument = XDocument.Load(percorso)

            For Each elemento As XElement In documento.Descendants("Configurazione")
                Try
                    Dim attributo As XAttribute = elemento.Attribute("id")
                    If attributo Is Nothing OrElse String.IsNullOrWhiteSpace(attributo.Value) Then
                        Continue For
                    End If
                    _valori(attributo.Value.Trim()) = elemento.Value
                Catch ex As Exception
                    RegistroAggiornamento.Scrivi("ConfigurazioneAggiornamento: errore lettura voce: " & ex.Message)
                End Try
            Next
        Catch ex As Exception
            RegistroAggiornamento.Scrivi("ConfigurazioneAggiornamento: errore caricamento: " & ex.Message)
        End Try
    End Sub

    Friend Function LeggiTesto(ByVal chiave As String, ByVal predefinito As String) As String
        Try
            Dim valore As String = Nothing
            If _valori.TryGetValue(chiave, valore) AndAlso Not String.IsNullOrWhiteSpace(valore) Then
                Return valore.Trim()
            End If
        Catch ex As Exception
            RegistroAggiornamento.Scrivi("ConfigurazioneAggiornamento.LeggiTesto " & chiave & ": " & ex.Message)
        End Try
        Return predefinito
    End Function

    Friend Function LeggiIntero(ByVal chiave As String, ByVal predefinito As Integer) As Integer
        Try
            Dim testo As String = LeggiTesto(chiave, Nothing)
            If String.IsNullOrWhiteSpace(testo) Then
                Return predefinito
            End If

            Dim risultato As Integer = 0
            If Integer.TryParse(testo, NumberStyles.Integer, CultureInfo.InvariantCulture, risultato) Then
                Return risultato
            End If

            Dim decimale As Double = 0
            If Double.TryParse(testo.Replace(","c, "."c), NumberStyles.Float, CultureInfo.InvariantCulture, decimale) Then
                Return CInt(Math.Round(decimale))
            End If
        Catch ex As Exception
            RegistroAggiornamento.Scrivi("ConfigurazioneAggiornamento.LeggiIntero " & chiave & ": " & ex.Message)
        End Try
        Return predefinito
    End Function

End Class

Friend NotInheritable Class ApiWasapi

    Private Sub New()
    End Sub

    Friend Const CLSCTX_ALL As Integer = 23
    Friend Const AUDCLNT_SHAREMODE_SHARED As Integer = 0
    Friend Const AUDCLNT_STREAMFLAGS_AUTOCONVERTPCM As Integer = &H80000000
    Friend Const AUDCLNT_STREAMFLAGS_SRC_DEFAULT_QUALITY As Integer = &H8000000
    Friend Const AUDCLNT_E_DEVICE_INVALIDATED As Integer = &H88890004
    Friend Const WAVE_FORMAT_PCM As Short = 1
    Friend Const eRender As Integer = 0
    Friend Const eConsole As Integer = 0

    <StructLayout(LayoutKind.Sequential, Pack:=2)>
    Friend Structure WAVEFORMATEX
        Public wFormatTag As Short
        Public nChannels As Short
        Public nSamplesPerSec As Integer
        Public nAvgBytesPerSec As Integer
        Public nBlockAlign As Short
        Public wBitsPerSample As Short
        Public cbSize As Short
    End Structure

    <ComImport()>
    <Guid("BCDE0395-E52F-467C-8E3D-C4579291692E")>
    Friend Class MMDeviceEnumeratorComObject
    End Class

    <ComImport()>
    <Guid("A95664D2-9614-4F35-A746-DE8DB63617E6")>
    <InterfaceType(ComInterfaceType.InterfaceIsIUnknown)>
    Friend Interface IMMDeviceEnumerator
        <PreserveSig()>
        Function EnumAudioEndpoints(ByVal dataFlow As Integer, ByVal stateMask As Integer, ByRef ppDevices As IntPtr) As Integer

        <PreserveSig()>
        Function GetDefaultAudioEndpoint(ByVal dataFlow As Integer, ByVal role As Integer, <MarshalAs(UnmanagedType.Interface)> ByRef ppEndpoint As IMMDevice) As Integer

        <PreserveSig()>
        Function GetDevice(<MarshalAs(UnmanagedType.LPWStr)> ByVal pwstrId As String, <MarshalAs(UnmanagedType.Interface)> ByRef ppDevice As IMMDevice) As Integer

        <PreserveSig()>
        Function RegisterEndpointNotificationCallback(ByVal pClient As IntPtr) As Integer

        <PreserveSig()>
        Function UnregisterEndpointNotificationCallback(ByVal pClient As IntPtr) As Integer
    End Interface

    <ComImport()>
    <Guid("D666063F-1587-4E43-81F1-B948E807363F")>
    <InterfaceType(ComInterfaceType.InterfaceIsIUnknown)>
    Friend Interface IMMDevice
        <PreserveSig()>
        Function Activate(ByRef iid As Guid, ByVal dwClsCtx As Integer, ByVal pActivationParams As IntPtr, <MarshalAs(UnmanagedType.IUnknown)> ByRef ppInterface As Object) As Integer

        <PreserveSig()>
        Function OpenPropertyStore(ByVal stgmAccess As Integer, ByRef ppProperties As IntPtr) As Integer

        <PreserveSig()>
        Function GetId(<MarshalAs(UnmanagedType.LPWStr)> ByRef ppstrId As String) As Integer

        <PreserveSig()>
        Function GetState(ByRef pdwState As Integer) As Integer
    End Interface

    <ComImport()>
    <Guid("1CB9AD4C-DBFA-4C32-B178-C2F568A703B2")>
    <InterfaceType(ComInterfaceType.InterfaceIsIUnknown)>
    Friend Interface IAudioClient
        <PreserveSig()>
        Function Initialize(ByVal shareMode As Integer, ByVal streamFlags As Integer, ByVal hnsBufferDuration As Long, ByVal hnsPeriodicity As Long, ByRef pFormat As WAVEFORMATEX, ByVal audioSessionGuid As IntPtr) As Integer

        <PreserveSig()>
        Function GetBufferSize(ByRef pNumBufferFrames As Integer) As Integer

        <PreserveSig()>
        Function GetStreamLatency(ByRef phnsLatency As Long) As Integer

        <PreserveSig()>
        Function GetCurrentPadding(ByRef pNumPaddingFrames As Integer) As Integer

        <PreserveSig()>
        Function IsFormatSupported(ByVal shareMode As Integer, ByRef pFormat As WAVEFORMATEX, ByRef ppClosestMatch As IntPtr) As Integer

        <PreserveSig()>
        Function GetMixFormat(ByRef ppDeviceFormat As IntPtr) As Integer

        <PreserveSig()>
        Function GetDevicePeriod(ByRef phnsDefaultDevicePeriod As Long, ByRef phnsMinimumDevicePeriod As Long) As Integer

        <PreserveSig()>
        Function Start() As Integer

        <PreserveSig()>
        Function [Stop]() As Integer

        <PreserveSig()>
        Function Reset() As Integer

        <PreserveSig()>
        Function SetEventHandle(ByVal eventHandle As IntPtr) As Integer

        <PreserveSig()>
        Function GetService(ByRef riid As Guid, <MarshalAs(UnmanagedType.IUnknown)> ByRef ppv As Object) As Integer
    End Interface

    <ComImport()>
    <Guid("F294ACFC-3146-4483-A7BF-ADDCA7C260E2")>
    <InterfaceType(ComInterfaceType.InterfaceIsIUnknown)>
    Friend Interface IAudioRenderClient
        <PreserveSig()>
        Function GetBuffer(ByVal numFramesRequested As Integer, ByRef ppData As IntPtr) As Integer

        <PreserveSig()>
        Function ReleaseBuffer(ByVal numFramesWritten As Integer, ByVal dwFlags As Integer) As Integer
    End Interface

End Class

Friend NotInheritable Class CodaAudioWasapi

    Private ReadOnly _blocco As New Object()
    Private ReadOnly _dati As New List(Of Byte)()
    Private _posizioneLettura As Integer = 0

    Friend Sub Aggiungi(ByVal buffer As Byte(), ByVal lunghezza As Integer)
        Try
            If buffer Is Nothing OrElse lunghezza <= 0 Then
                Return
            End If

            SyncLock _blocco
                Dim indice As Integer

                For indice = 0 To lunghezza - 1
                    _dati.Add(buffer(indice))
                Next
            End SyncLock
        Catch ex As Exception
            RegistroAggiornamento.Scrivi("CodaAudioWasapi.Aggiungi: " & ex.Message)
        End Try
    End Sub

    Friend Function ByteDisponibili() As Integer
        Try
            SyncLock _blocco
                Dim disponibili As Integer = _dati.Count - _posizioneLettura
                If disponibili < 0 Then
                    Return 0
                End If
                Return disponibili
            End SyncLock
        Catch ex As Exception
            RegistroAggiornamento.Scrivi("CodaAudioWasapi.ByteDisponibili: " & ex.Message)
            Return 0
        End Try
    End Function

    Friend Function Preleva(ByVal destinazione As Byte(), ByVal quantita As Integer) As Integer
        Try
            SyncLock _blocco
                Dim disponibili As Integer = _dati.Count - _posizioneLettura

                If disponibili <= 0 Then
                    _dati.Clear()
                    _posizioneLettura = 0
                    Return 0
                End If

                Dim daLeggere As Integer = quantita

                If daLeggere > disponibili Then
                    daLeggere = disponibili
                End If

                Dim indice As Integer

                For indice = 0 To daLeggere - 1
                    destinazione(indice) = _dati(_posizioneLettura + indice)
                Next

                _posizioneLettura += daLeggere

                If _posizioneLettura >= _dati.Count Then
                    _dati.Clear()
                    _posizioneLettura = 0
                ElseIf _posizioneLettura > 262144 Then
                    _dati.RemoveRange(0, _posizioneLettura)
                    _posizioneLettura = 0
                End If

                Return daLeggere
            End SyncLock
        Catch ex As Exception
            RegistroAggiornamento.Scrivi("CodaAudioWasapi.Preleva: " & ex.Message)
            Return 0
        End Try
    End Function

    Friend Sub Svuota()
        Try
            SyncLock _blocco
                Dim disponibili As Integer = _dati.Count - _posizioneLettura

                If disponibili < 4 Then
                    _dati.Clear()
                    _posizioneLettura = 0
                    Return
                End If

                Dim byteDissolvenza As Integer = 882

                If byteDissolvenza > disponibili Then
                    byteDissolvenza = disponibili
                    If (byteDissolvenza Mod 2) <> 0 Then
                        byteDissolvenza -= 1
                    End If
                End If

                Dim campioni As Integer = byteDissolvenza \ 2
                Dim indice As Integer

                For indice = 0 To campioni - 1
                    Dim posizione As Integer = _posizioneLettura + (indice * 2)
                    Dim valore As Integer = CInt(_dati(posizione)) Or (CInt(_dati(posizione + 1)) << 8)

                    If valore > 32767 Then
                        valore -= 65536
                    End If

                    Dim guadagno As Double = 1.0R - (CDbl(indice) / CDbl(campioni))
                    Dim attenuato As Integer = CInt(valore * guadagno)

                    If attenuato > 32767 Then attenuato = 32767
                    If attenuato < -32768 Then attenuato = -32768
                    If attenuato < 0 Then attenuato += 65536

                    _dati(posizione) = CByte(attenuato And &HFF)
                    _dati(posizione + 1) = CByte((attenuato >> 8) And &HFF)
                Next

                Dim nuovaFine As Integer = _posizioneLettura + byteDissolvenza

                If nuovaFine < _dati.Count Then
                    _dati.RemoveRange(nuovaFine, _dati.Count - nuovaFine)
                End If
            End SyncLock
        Catch ex As Exception
            RegistroAggiornamento.Scrivi("CodaAudioWasapi.Svuota: " & ex.Message)
        End Try
    End Sub

End Class

Friend NotInheritable Class LettoreWasapi
    Implements IDisposable

    Private Const FREQUENZA As Integer = 44100
    Private Const CANALI As Short = 1
    Private Const BIT As Short = 16

    Private ReadOnly _coda As New CodaAudioWasapi()
    Private _thread As Thread
    Private _attivo As Integer = 0
    Private _eliminato As Boolean = False

    Friend ReadOnly Property Coda As CodaAudioWasapi
        Get
            Return _coda
        End Get
    End Property

    Friend Shared Function FormatoAudio() As WGFormatoAudio
        Dim formato As New WGFormatoAudio()
        formato.Frequenza = FREQUENZA
        formato.Canali = CANALI
        formato.Bit = BIT
        Return formato
    End Function

    Friend Sub Avvia()
        Try
            If Interlocked.CompareExchange(_attivo, 1, 0) <> 0 Then
                Return
            End If

            _thread = New Thread(New ThreadStart(AddressOf CicloRiproduzione))
            _thread.IsBackground = True
            _thread.Name = "WgAudioWasapi"
            _thread.Priority = ThreadPriority.Highest
            _thread.Start()
        Catch ex As Exception
            RegistroAggiornamento.Scrivi("LettoreWasapi.Avvia: " & ex.Message)
            Interlocked.Exchange(_attivo, 0)
        End Try
    End Sub

    Friend Sub Ferma()
        Try
            Interlocked.Exchange(_attivo, 0)

            If _thread IsNot Nothing AndAlso _thread.IsAlive Then
                _thread.Join(1500)
            End If

            _thread = Nothing
        Catch ex As Exception
            RegistroAggiornamento.Scrivi("LettoreWasapi.Ferma: " & ex.Message)
        End Try
    End Sub

    Private Sub CicloRiproduzione()
        Try
            Try
                CoInitializeEx(IntPtr.Zero, 0)
            Catch exCom As Exception
                RegistroAggiornamento.Scrivi("LettoreWasapi.CicloRiproduzione (CoInitializeEx): " & exCom.Message)
            End Try

            While Interlocked.CompareExchange(_attivo, 1, 1) = 1
                Try
                    EseguiSessioneAudio()
                Catch exSessione As Exception
                    RegistroAggiornamento.Scrivi("LettoreWasapi.CicloRiproduzione (sessione): " & exSessione.Message)
                End Try

                If Interlocked.CompareExchange(_attivo, 1, 1) = 1 Then
                    Try
                        Thread.Sleep(500)
                    Catch
                    End Try
                End If
            End While
        Catch ex As Exception
            RegistroAggiornamento.Scrivi("LettoreWasapi.CicloRiproduzione: " & ex.Message & " " & ex.StackTrace)
        End Try
    End Sub

    Private Sub EseguiSessioneAudio()
        Dim enumeratore As ApiWasapi.IMMDeviceEnumerator = Nothing
        Dim dispositivo As ApiWasapi.IMMDevice = Nothing
        Dim cliente As ApiWasapi.IAudioClient = Nothing
        Dim renderizzatore As ApiWasapi.IAudioRenderClient = Nothing

        Try
            enumeratore = CType(New ApiWasapi.MMDeviceEnumeratorComObject(), ApiWasapi.IMMDeviceEnumerator)

            If enumeratore.GetDefaultAudioEndpoint(ApiWasapi.eRender, ApiWasapi.eConsole, dispositivo) <> 0 OrElse dispositivo Is Nothing Then
                Return
            End If

            Dim guidAudioClient As New Guid("1CB9AD4C-DBFA-4C32-B178-C2F568A703B2")
            Dim oggettoCliente As Object = Nothing

            If dispositivo.Activate(guidAudioClient, ApiWasapi.CLSCTX_ALL, IntPtr.Zero, oggettoCliente) <> 0 OrElse oggettoCliente Is Nothing Then
                Return
            End If

            cliente = CType(oggettoCliente, ApiWasapi.IAudioClient)

            Dim formato As New ApiWasapi.WAVEFORMATEX()
            formato.wFormatTag = ApiWasapi.WAVE_FORMAT_PCM
            formato.nChannels = CANALI
            formato.nSamplesPerSec = FREQUENZA
            formato.wBitsPerSample = BIT
            formato.nBlockAlign = CShort((CInt(CANALI) * CInt(BIT)) \ 8)
            formato.nAvgBytesPerSec = FREQUENZA * CInt(formato.nBlockAlign)
            formato.cbSize = 0

            Dim opzioni As Integer = ApiWasapi.AUDCLNT_STREAMFLAGS_AUTOCONVERTPCM Or ApiWasapi.AUDCLNT_STREAMFLAGS_SRC_DEFAULT_QUALITY

            If cliente.Initialize(ApiWasapi.AUDCLNT_SHAREMODE_SHARED, opzioni, 0, 0, formato, IntPtr.Zero) <> 0 Then
                Return
            End If

            Dim guidRenderClient As New Guid("F294ACFC-3146-4483-A7BF-ADDCA7C260E2")
            Dim oggettoRender As Object = Nothing

            If cliente.GetService(guidRenderClient, oggettoRender) <> 0 OrElse oggettoRender Is Nothing Then
                Return
            End If

            renderizzatore = CType(oggettoRender, ApiWasapi.IAudioRenderClient)

            If cliente.Start() <> 0 Then
                Return
            End If

            Dim allineamento As Integer = CInt(formato.nBlockAlign)
            Dim bufferTemporaneo As Byte() = New Byte(65535) {}

            While Interlocked.CompareExchange(_attivo, 1, 1) = 1
                Dim frameTotali As Integer = 0
                Dim frameOccupati As Integer = 0

                Dim esitoDimensione As Integer = cliente.GetBufferSize(frameTotali)
                Dim esitoOccupati As Integer = cliente.GetCurrentPadding(frameOccupati)

                If esitoOccupati = ApiWasapi.AUDCLNT_E_DEVICE_INVALIDATED Then
                    Exit While
                End If

                If esitoDimensione <> 0 OrElse esitoOccupati <> 0 OrElse frameOccupati > frameTotali Then
                    Thread.Sleep(2)
                    Continue While
                End If

                Dim frameLiberi As Integer = frameTotali - frameOccupati
                Dim byteInCoda As Integer = _coda.ByteDisponibili()

                If frameLiberi > 0 AndAlso byteInCoda > 0 Then
                    Dim frameDaScrivere As Integer = frameLiberi
                    Dim byteDaScrivere As Integer = frameDaScrivere * allineamento

                    If byteDaScrivere > byteInCoda Then
                        byteDaScrivere = byteInCoda - (byteInCoda Mod allineamento)
                        frameDaScrivere = byteDaScrivere \ allineamento
                    End If

                    If byteDaScrivere > bufferTemporaneo.Length Then
                        byteDaScrivere = bufferTemporaneo.Length - (bufferTemporaneo.Length Mod allineamento)
                        frameDaScrivere = byteDaScrivere \ allineamento
                    End If

                    If frameDaScrivere > 0 Then
                        Dim puntatore As IntPtr = IntPtr.Zero
                        Dim esitoBuffer As Integer = renderizzatore.GetBuffer(frameDaScrivere, puntatore)

                        If esitoBuffer = ApiWasapi.AUDCLNT_E_DEVICE_INVALIDATED Then
                            Exit While
                        End If

                        If esitoBuffer = 0 AndAlso puntatore <> IntPtr.Zero Then
                            Dim letti As Integer = _coda.Preleva(bufferTemporaneo, byteDaScrivere)

                            If letti < byteDaScrivere Then
                                Dim indice As Integer

                                For indice = letti To byteDaScrivere - 1
                                    bufferTemporaneo(indice) = 0
                                Next
                            End If

                            Marshal.Copy(bufferTemporaneo, 0, puntatore, byteDaScrivere)
                            renderizzatore.ReleaseBuffer(frameDaScrivere, 0)
                        End If
                    End If
                End If

                Thread.Sleep(2)
            End While

            cliente.Stop()
        Catch ex As Exception
            RegistroAggiornamento.Scrivi("LettoreWasapi.EseguiSessioneAudio: " & ex.Message)
        Finally
            Try
                If renderizzatore IsNot Nothing Then Marshal.ReleaseComObject(renderizzatore)
            Catch
            End Try

            Try
                If cliente IsNot Nothing Then Marshal.ReleaseComObject(cliente)
            Catch
            End Try

            Try
                If dispositivo IsNot Nothing Then Marshal.ReleaseComObject(dispositivo)
            Catch
            End Try

            Try
                If enumeratore IsNot Nothing Then Marshal.ReleaseComObject(enumeratore)
            Catch
            End Try
        End Try
    End Sub

    <DllImport("ole32.dll")>
    Private Shared Function CoInitializeEx(ByVal pvReserved As IntPtr, ByVal dwCoInit As Integer) As Integer
    End Function

    Public Sub Dispose() Implements IDisposable.Dispose
        Try
            If _eliminato Then
                Return
            End If

            _eliminato = True
            Ferma()
        Catch ex As Exception
            RegistroAggiornamento.Scrivi("LettoreWasapi.Dispose: " & ex.Message)
        End Try
    End Sub

End Class

Friend Class WGFormatoAudio
    Public Property Frequenza As Integer
    Public Property Canali As Short
    Public Property Bit As Short
End Class

Friend NotInheritable Class SintesiVocaleAggiornamento
    Implements IDisposable

    Private ReadOnly _blocco As New Object()
    Private ReadOnly _lettore As New LettoreWasapi()
    Private ReadOnly _codaTesti As New Queue(Of String)()
    Private ReadOnly _bloccoCoda As New Object()

    Private _sintetizzatore As SpeechSynthesizer
    Private _threadSintesi As Thread
    Private _disponibile As Boolean = False
    Private _eliminato As Boolean = False
    Private _attivo As Integer = 0
    Private _generazione As Integer = 0

    Friend Sub New(ByVal configurazione As ConfigurazioneAggiornamento)
        Try
            _sintetizzatore = New SpeechSynthesizer()

            ApplicaVoce(configurazione)
            ApplicaVelocita(configurazione)
            ApplicaVolume(configurazione)

            Riscalda()

            _lettore.Avvia()

            Interlocked.Exchange(_attivo, 1)

            _threadSintesi = New Thread(New ThreadStart(AddressOf CicloSintesi))
            _threadSintesi.IsBackground = True
            _threadSintesi.Name = "WgSintesiAggiornamento"
            _threadSintesi.Start()

            _disponibile = True
        Catch ex As Exception
            RegistroAggiornamento.Scrivi("SintesiVocaleAggiornamento.New: " & ex.Message)
            _disponibile = False
        End Try
    End Sub

    Private Sub ApplicaVoce(ByVal configurazione As ConfigurazioneAggiornamento)
        Try
            Dim voci As New List(Of InstalledVoice)()

            For Each voce As InstalledVoice In _sintetizzatore.GetInstalledVoices()
                Try
                    If voce IsNot Nothing AndAlso voce.Enabled AndAlso voce.VoiceInfo IsNot Nothing Then
                        voci.Add(voce)
                    End If
                Catch
                End Try
            Next

            If voci.Count = 0 Then
                Return
            End If

            Dim indice As Integer = configurazione.LeggiIntero("SapiVoice", -1)

            If indice >= 0 AndAlso indice < voci.Count Then
                Try
                    _sintetizzatore.SelectVoice(voci(indice).VoiceInfo.Name)
                    Return
                Catch ex As Exception
                    RegistroAggiornamento.Scrivi("SintesiVocaleAggiornamento.ApplicaVoce (indice): " & ex.Message)
                End Try
            End If

            For Each voce As InstalledVoice In voci
                Try
                    _sintetizzatore.SelectVoice(voce.VoiceInfo.Name)
                    Return
                Catch
                End Try
            Next
        Catch ex As Exception
            RegistroAggiornamento.Scrivi("SintesiVocaleAggiornamento.ApplicaVoce: " & ex.Message)
        End Try
    End Sub

    Private Sub ApplicaVelocita(ByVal configurazione As ConfigurazioneAggiornamento)
        Try
            Dim valore As Integer = configurazione.LeggiIntero("SapiRate", 50)

            If valore < 0 Then valore = 0
            If valore > 100 Then valore = 100

            Dim velocita As Integer = (valore \ 5) - 10

            If velocita < -10 Then velocita = -10
            If velocita > 10 Then velocita = 10

            _sintetizzatore.Rate = velocita
        Catch ex As Exception
            RegistroAggiornamento.Scrivi("SintesiVocaleAggiornamento.ApplicaVelocita: " & ex.Message)
        End Try
    End Sub

    Private Sub ApplicaVolume(ByVal configurazione As ConfigurazioneAggiornamento)
        Try
            Dim valore As Integer = configurazione.LeggiIntero("SapiVolume", 100)

            If valore < 0 Then valore = 0
            If valore > 100 Then valore = 100

            _sintetizzatore.Volume = valore
        Catch ex As Exception
            RegistroAggiornamento.Scrivi("SintesiVocaleAggiornamento.ApplicaVolume: " & ex.Message)
        End Try
    End Sub

    Private Sub Riscalda()
        Try
            Using flusso As New MemoryStream()
                _sintetizzatore.SetOutputToAudioStream(flusso, FormatoSintesi())
                _sintetizzatore.Speak(".")
            End Using
        Catch ex As Exception
            RegistroAggiornamento.Scrivi("SintesiVocaleAggiornamento.Riscalda: " & ex.Message)
        End Try
    End Sub

    Private Shared Function FormatoSintesi() As SpeechAudioFormatInfo
        Return New SpeechAudioFormatInfo(44100, AudioBitsPerSample.Sixteen, AudioChannel.Mono)
    End Function

    Friend ReadOnly Property Disponibile As Boolean
        Get
            Return _disponibile
        End Get
    End Property

    Friend Sub Parla(ByVal testo As String, ByVal interrompi As Boolean)
        Try
            If Not _disponibile OrElse _eliminato Then
                Return
            End If

            If String.IsNullOrWhiteSpace(testo) Then
                Return
            End If

            SyncLock _bloccoCoda
                If interrompi Then
                    Interlocked.Increment(_generazione)
                    _codaTesti.Clear()
                    _lettore.Coda.Svuota()
                End If

                _codaTesti.Enqueue(testo)
                Monitor.Pulse(_bloccoCoda)
            End SyncLock
        Catch ex As Exception
            RegistroAggiornamento.Scrivi("SintesiVocaleAggiornamento.Parla: " & ex.Message)
        End Try
    End Sub

    Friend Sub Silenzia()
        Try
            SyncLock _bloccoCoda
                Interlocked.Increment(_generazione)
                _codaTesti.Clear()
                Monitor.Pulse(_bloccoCoda)
            End SyncLock

            _lettore.Coda.Svuota()
        Catch ex As Exception
            RegistroAggiornamento.Scrivi("SintesiVocaleAggiornamento.Silenzia: " & ex.Message)
        End Try
    End Sub

    Private Sub CicloSintesi()
        Try
            While Interlocked.CompareExchange(_attivo, 1, 1) = 1
                Dim testo As String = Nothing

                SyncLock _bloccoCoda
                    While _codaTesti.Count = 0 AndAlso Interlocked.CompareExchange(_attivo, 1, 1) = 1
                        Monitor.Wait(_bloccoCoda, 200)
                    End While

                    If _codaTesti.Count > 0 Then
                        testo = _codaTesti.Dequeue()
                    End If
                End SyncLock

                If String.IsNullOrEmpty(testo) Then
                    Continue While
                End If

                Dim generazioneCorrente As Integer = Interlocked.CompareExchange(_generazione, 0, 0)

                Try
                    SintetizzaEAccoda(testo, generazioneCorrente)
                Catch exSintesi As Exception
                    RegistroAggiornamento.Scrivi("SintesiVocaleAggiornamento.CicloSintesi (sintesi): " & exSintesi.Message)
                End Try
            End While
        Catch ex As Exception
            RegistroAggiornamento.Scrivi("SintesiVocaleAggiornamento.CicloSintesi: " & ex.Message)
        End Try
    End Sub

    Private Sub SintetizzaEAccoda(ByVal testo As String, ByVal generazione As Integer)
        Dim campioni As Byte() = Nothing

        SyncLock _blocco
            If _sintetizzatore Is Nothing OrElse _eliminato Then
                Return
            End If

            Using flusso As New MemoryStream()
                _sintetizzatore.SetOutputToAudioStream(flusso, FormatoSintesi())
                _sintetizzatore.Speak(testo)
                campioni = flusso.ToArray()
            End Using
        End SyncLock

        If campioni Is Nothing OrElse campioni.Length = 0 Then
            Return
        End If

        If Interlocked.CompareExchange(_generazione, 0, 0) <> generazione Then
            Return
        End If

        _lettore.Coda.Aggiungi(campioni, campioni.Length)
    End Sub

    Public Sub Dispose() Implements IDisposable.Dispose
        Try
            If _eliminato Then
                Return
            End If

            _eliminato = True
            Interlocked.Exchange(_attivo, 0)

            SyncLock _bloccoCoda
                _codaTesti.Clear()
                Monitor.PulseAll(_bloccoCoda)
            End SyncLock

            Try
                If _threadSintesi IsNot Nothing AndAlso _threadSintesi.IsAlive Then
                    _threadSintesi.Join(1500)
                End If
            Catch
            End Try

            Try
                _lettore.Dispose()
            Catch
            End Try

            SyncLock _blocco
                If _sintetizzatore IsNot Nothing Then
                    Try
                        _sintetizzatore.Dispose()
                    Catch
                    End Try
                    _sintetizzatore = Nothing
                End If
            End SyncLock
        Catch ex As Exception
            RegistroAggiornamento.Scrivi("SintesiVocaleAggiornamento.Dispose: " & ex.Message)
        End Try
    End Sub

End Class

Friend NotInheritable Class HookTastieraAggiornamento
    Implements IDisposable

    Private Const WH_KEYBOARD_LL As Integer = 13
    Private Const WM_KEYDOWN As Integer = &H100
    Private Const WM_SYSKEYDOWN As Integer = &H104
    Private Const HC_ACTION As Integer = 0

    Private Delegate Function ProceduraHook(ByVal nCode As Integer, ByVal wParam As IntPtr, ByVal lParam As IntPtr) As IntPtr

    <DllImport("user32.dll", CharSet:=CharSet.Auto, SetLastError:=True)>
    Private Shared Function SetWindowsHookEx(ByVal idHook As Integer, ByVal lpfn As ProceduraHook, ByVal hMod As IntPtr, ByVal dwThreadId As UInteger) As IntPtr
    End Function

    <DllImport("user32.dll", CharSet:=CharSet.Auto, SetLastError:=True)>
    Private Shared Function UnhookWindowsHookEx(ByVal hhk As IntPtr) As Boolean
    End Function

    <DllImport("user32.dll", CharSet:=CharSet.Auto, SetLastError:=True)>
    Private Shared Function CallNextHookEx(ByVal hhk As IntPtr, ByVal nCode As Integer, ByVal wParam As IntPtr, ByVal lParam As IntPtr) As IntPtr
    End Function

    <DllImport("kernel32.dll", CharSet:=CharSet.Auto, SetLastError:=True)>
    Private Shared Function GetModuleHandle(ByVal lpModuleName As String) As IntPtr
    End Function

    Private _riferimentoProcedura As ProceduraHook
    Private _identificatore As IntPtr = IntPtr.Zero
    Private _eliminato As Boolean = False

    Public Event TastoPremuto As EventHandler(Of TastoAggiornamentoEventArgs)

    Friend Sub Installa()
        Try
            If _identificatore <> IntPtr.Zero Then
                Return
            End If

            _riferimentoProcedura = New ProceduraHook(AddressOf Procedura)

            Dim modulo As IntPtr = IntPtr.Zero

            Try
                modulo = GetModuleHandle(Process.GetCurrentProcess().MainModule.ModuleName)
            Catch exModulo As Exception
                RegistroAggiornamento.Scrivi("HookTastieraAggiornamento.Installa (modulo): " & exModulo.Message)
                modulo = IntPtr.Zero
            End Try

            _identificatore = SetWindowsHookEx(WH_KEYBOARD_LL, _riferimentoProcedura, modulo, 0)

            If _identificatore = IntPtr.Zero Then
                RegistroAggiornamento.Scrivi("HookTastieraAggiornamento.Installa: installazione non riuscita, codice " & Marshal.GetLastWin32Error().ToString(CultureInfo.InvariantCulture))
            End If
        Catch ex As Exception
            RegistroAggiornamento.Scrivi("HookTastieraAggiornamento.Installa: " & ex.Message)
        End Try
    End Sub

    Friend Sub Rimuovi()
        Try
            If _identificatore = IntPtr.Zero Then
                Return
            End If

            UnhookWindowsHookEx(_identificatore)
            _identificatore = IntPtr.Zero
            _riferimentoProcedura = Nothing
        Catch ex As Exception
            RegistroAggiornamento.Scrivi("HookTastieraAggiornamento.Rimuovi: " & ex.Message)
        End Try
    End Sub

    Private Function Procedura(ByVal nCode As Integer, ByVal wParam As IntPtr, ByVal lParam As IntPtr) As IntPtr
        Try
            If nCode < HC_ACTION Then
                Return CallNextHookEx(_identificatore, nCode, wParam, lParam)
            End If

            Dim messaggio As Integer = wParam.ToInt32()

            If messaggio <> WM_KEYDOWN AndAlso messaggio <> WM_SYSKEYDOWN Then
                Return CallNextHookEx(_identificatore, nCode, wParam, lParam)
            End If

            Dim codice As Integer = Marshal.ReadInt32(lParam)
            Dim tasto As Keys = CType(codice, Keys)

            Dim argomenti As New TastoAggiornamentoEventArgs(tasto)

            Try
                RaiseEvent TastoPremuto(Me, argomenti)
            Catch exEvento As Exception
                RegistroAggiornamento.Scrivi("HookTastieraAggiornamento.Procedura (evento): " & exEvento.Message)
            End Try

            If argomenti.Bloccato Then
                Return New IntPtr(1)
            End If

            Return CallNextHookEx(_identificatore, nCode, wParam, lParam)
        Catch ex As Exception
            RegistroAggiornamento.Scrivi("HookTastieraAggiornamento.Procedura: " & ex.Message)
            Return CallNextHookEx(_identificatore, nCode, wParam, lParam)
        End Try
    End Function

    Public Sub Dispose() Implements IDisposable.Dispose
        Try
            If _eliminato Then
                Return
            End If

            _eliminato = True
            Rimuovi()
        Catch ex As Exception
            RegistroAggiornamento.Scrivi("HookTastieraAggiornamento.Dispose: " & ex.Message)
        End Try
    End Sub

End Class

Friend Class TastoAggiornamentoEventArgs
    Inherits EventArgs

    Private ReadOnly _tasto As Keys

    Friend Sub New(ByVal tasto As Keys)
        _tasto = tasto
    End Sub

    Friend ReadOnly Property Tasto As Keys
        Get
            Return _tasto
        End Get
    End Property

    Friend Property Bloccato As Boolean = False

End Class

Friend Class MenuGdi
    Inherits Control

    Private ReadOnly _voci As New List(Of String)()
    Private _indiceSelezionato As Integer = 0
    Private _titolo As String = String.Empty
    Private Const ALTEZZA_VOCE As Integer = 48
    Private Const ALTEZZA_TITOLO As Integer = 56

    Public Event SelezioneCambiata As EventHandler
    Public Event VoceAttivata As EventHandler
    Public Event UscitaRichiesta As EventHandler

    Friend Sub New()
        Try
            SetStyle(ControlStyles.UserPaint Or ControlStyles.AllPaintingInWmPaint Or ControlStyles.OptimizedDoubleBuffer Or ControlStyles.Selectable Or ControlStyles.ResizeRedraw, True)
            TabStop = True
            BackColor = Color.Black
        Catch ex As Exception
            RegistroAggiornamento.Scrivi("MenuGdi.New: " & ex.Message)
        End Try
    End Sub

    Friend Property Titolo As String
        Get
            Return _titolo
        End Get
        Set(ByVal value As String)
            Try
                _titolo = If(value, String.Empty)
                Invalidate()
            Catch ex As Exception
                RegistroAggiornamento.Scrivi("MenuGdi.Titolo: " & ex.Message)
            End Try
        End Set
    End Property

    Friend Sub AggiungiVoce(ByVal testo As String)
        Try
            If String.IsNullOrWhiteSpace(testo) Then
                Return
            End If
            _voci.Add(testo)
            Invalidate()
        Catch ex As Exception
            RegistroAggiornamento.Scrivi("MenuGdi.AggiungiVoce: " & ex.Message)
        End Try
    End Sub

    Friend ReadOnly Property NumeroVoci As Integer
        Get
            Return _voci.Count
        End Get
    End Property

    Friend Property IndiceSelezionato As Integer
        Get
            Return _indiceSelezionato
        End Get
        Set(ByVal value As Integer)
            Try
                If _voci.Count = 0 Then
                    _indiceSelezionato = 0
                    Return
                End If

                Dim nuovo As Integer = value
                If nuovo < 0 Then nuovo = 0
                If nuovo > _voci.Count - 1 Then nuovo = _voci.Count - 1

                If nuovo <> _indiceSelezionato Then
                    _indiceSelezionato = nuovo
                    Invalidate()
                    RaiseEvent SelezioneCambiata(Me, EventArgs.Empty)
                End If
            Catch ex As Exception
                RegistroAggiornamento.Scrivi("MenuGdi.IndiceSelezionato: " & ex.Message)
            End Try
        End Set
    End Property

    Friend ReadOnly Property TestoSelezionato As String
        Get
            Try
                If _indiceSelezionato >= 0 AndAlso _indiceSelezionato < _voci.Count Then
                    Return _voci(_indiceSelezionato)
                End If
            Catch ex As Exception
                RegistroAggiornamento.Scrivi("MenuGdi.TestoSelezionato: " & ex.Message)
            End Try
            Return String.Empty
        End Get
    End Property

    Protected Overrides Function IsInputKey(ByVal chiave As Keys) As Boolean
        Try
            Select Case chiave
                Case Keys.Up, Keys.Down, Keys.Left, Keys.Right, Keys.Enter, Keys.Home, Keys.End
                    Return True
            End Select
        Catch ex As Exception
            RegistroAggiornamento.Scrivi("MenuGdi.IsInputKey: " & ex.Message)
        End Try
        Return MyBase.IsInputKey(chiave)
    End Function

    Protected Overrides Sub OnKeyDown(ByVal e As KeyEventArgs)
        Try
            Select Case e.KeyCode
                Case Keys.Up, Keys.Left
                    If _voci.Count > 0 Then
                        If _indiceSelezionato = 0 Then
                            IndiceSelezionato = _voci.Count - 1
                        Else
                            IndiceSelezionato = _indiceSelezionato - 1
                        End If
                    End If
                    e.Handled = True

                Case Keys.Down, Keys.Right
                    If _voci.Count > 0 Then
                        If _indiceSelezionato >= _voci.Count - 1 Then
                            IndiceSelezionato = 0
                        Else
                            IndiceSelezionato = _indiceSelezionato + 1
                        End If
                    End If
                    e.Handled = True

                Case Keys.Home
                    IndiceSelezionato = 0
                    e.Handled = True

                Case Keys.End
                    IndiceSelezionato = _voci.Count - 1
                    e.Handled = True

                Case Keys.Enter, Keys.Space
                    RaiseEvent VoceAttivata(Me, EventArgs.Empty)
                    e.Handled = True

                Case Keys.Escape
                    RaiseEvent UscitaRichiesta(Me, EventArgs.Empty)
                    e.Handled = True
            End Select
        Catch ex As Exception
            RegistroAggiornamento.Scrivi("MenuGdi.OnKeyDown: " & ex.Message)
        End Try

        MyBase.OnKeyDown(e)
    End Sub

    Protected Overrides Sub OnMouseDown(ByVal e As MouseEventArgs)
        Try
            Focus()

            Dim posizione As Integer = e.Y - ALTEZZA_TITOLO
            If posizione < 0 Then
                MyBase.OnMouseDown(e)
                Return
            End If

            Dim indice As Integer = posizione \ ALTEZZA_VOCE
            If indice >= 0 AndAlso indice < _voci.Count Then
                If indice = _indiceSelezionato Then
                    RaiseEvent VoceAttivata(Me, EventArgs.Empty)
                Else
                    IndiceSelezionato = indice
                End If
            End If
        Catch ex As Exception
            RegistroAggiornamento.Scrivi("MenuGdi.OnMouseDown: " & ex.Message)
        End Try

        MyBase.OnMouseDown(e)
    End Sub

    Protected Overrides Sub OnGotFocus(ByVal e As EventArgs)
        Try
            Invalidate()
        Catch ex As Exception
            RegistroAggiornamento.Scrivi("MenuGdi.OnGotFocus: " & ex.Message)
        End Try
        MyBase.OnGotFocus(e)
    End Sub

    Protected Overrides Sub OnLostFocus(ByVal e As EventArgs)
        Try
            Invalidate()
        Catch ex As Exception
            RegistroAggiornamento.Scrivi("MenuGdi.OnLostFocus: " & ex.Message)
        End Try
        MyBase.OnLostFocus(e)
    End Sub

    Protected Overrides Sub OnPaint(ByVal e As PaintEventArgs)
        Dim hdc As IntPtr = IntPtr.Zero
        Dim pennelloSfondo As IntPtr = IntPtr.Zero
        Dim pennelloSelezione As IntPtr = IntPtr.Zero
        Dim pennelloBordo As IntPtr = IntPtr.Zero
        Dim carattereTitolo As IntPtr = IntPtr.Zero
        Dim carattereVoce As IntPtr = IntPtr.Zero
        Dim carattereOriginale As IntPtr = IntPtr.Zero

        Try
            hdc = e.Graphics.GetHdc()

            pennelloSfondo = ApiGdi.CreateSolidBrush(ApiGdi.DaColore(Color.Black))
            pennelloSelezione = ApiGdi.CreateSolidBrush(ApiGdi.DaColore(Color.FromArgb(0, 90, 180)))
            pennelloBordo = ApiGdi.CreateSolidBrush(ApiGdi.DaColore(Color.FromArgb(200, 200, 200)))

            carattereTitolo = ApiGdi.CreateFont(-24, 0, 0, 0, ApiGdi.FW_BOLD, 0, 0, 0, ApiGdi.DEFAULT_CHARSET, ApiGdi.OUT_DEFAULT_PRECIS, ApiGdi.CLIP_DEFAULT_PRECIS, ApiGdi.CLEARTYPE_QUALITY, ApiGdi.DEFAULT_PITCH, "Segoe UI")
            carattereVoce = ApiGdi.CreateFont(-22, 0, 0, 0, ApiGdi.FW_NORMAL, 0, 0, 0, ApiGdi.DEFAULT_CHARSET, ApiGdi.OUT_DEFAULT_PRECIS, ApiGdi.CLIP_DEFAULT_PRECIS, ApiGdi.CLEARTYPE_QUALITY, ApiGdi.DEFAULT_PITCH, "Segoe UI")

            Dim areaTotale As ApiGdi.RECT = ApiGdi.CreaRettangolo(New Rectangle(0, 0, Width, Height))
            ApiGdi.FillRect(hdc, areaTotale, pennelloSfondo)

            ApiGdi.SetBkMode(hdc, ApiGdi.TRANSPARENT)

            carattereOriginale = ApiGdi.SelectObject(hdc, carattereTitolo)
            ApiGdi.SetTextColor(hdc, ApiGdi.DaColore(Color.White))

            Dim areaTitolo As ApiGdi.RECT = ApiGdi.CreaRettangolo(New Rectangle(16, 0, Math.Max(Width - 32, 10), ALTEZZA_TITOLO))
            ApiGdi.DrawText(hdc, _titolo, _titolo.Length, areaTitolo, ApiGdi.DT_LEFT Or ApiGdi.DT_VCENTER Or ApiGdi.DT_SINGLELINE Or ApiGdi.DT_NOPREFIX Or ApiGdi.DT_END_ELLIPSIS)

            ApiGdi.SelectObject(hdc, carattereVoce)

            For indice As Integer = 0 To _voci.Count - 1
                Dim superiore As Integer = ALTEZZA_TITOLO + (indice * ALTEZZA_VOCE)
                Dim areaVoce As ApiGdi.RECT = ApiGdi.CreaRettangolo(New Rectangle(12, superiore + 2, Math.Max(Width - 24, 10), ALTEZZA_VOCE - 4))

                If indice = _indiceSelezionato Then
                    ApiGdi.FillRect(hdc, areaVoce, pennelloSelezione)
                    If Focused Then
                        ApiGdi.FrameRect(hdc, areaVoce, pennelloBordo)
                    End If
                    ApiGdi.SetTextColor(hdc, ApiGdi.DaColore(Color.White))
                Else
                    ApiGdi.SetTextColor(hdc, ApiGdi.DaColore(Color.FromArgb(215, 215, 215)))
                End If

                Dim areaTesto As ApiGdi.RECT = ApiGdi.CreaRettangolo(New Rectangle(28, superiore + 2, Math.Max(Width - 48, 10), ALTEZZA_VOCE - 4))
                Dim testo As String = _voci(indice)
                ApiGdi.DrawText(hdc, testo, testo.Length, areaTesto, ApiGdi.DT_LEFT Or ApiGdi.DT_VCENTER Or ApiGdi.DT_SINGLELINE Or ApiGdi.DT_NOPREFIX Or ApiGdi.DT_END_ELLIPSIS)
            Next
        Catch ex As Exception
            RegistroAggiornamento.Scrivi("MenuGdi.OnPaint: " & ex.Message)
        Finally
            Try
                If carattereOriginale <> IntPtr.Zero AndAlso hdc <> IntPtr.Zero Then
                    ApiGdi.SelectObject(hdc, carattereOriginale)
                End If
                If carattereTitolo <> IntPtr.Zero Then ApiGdi.DeleteObject(carattereTitolo)
                If carattereVoce <> IntPtr.Zero Then ApiGdi.DeleteObject(carattereVoce)
                If pennelloSfondo <> IntPtr.Zero Then ApiGdi.DeleteObject(pennelloSfondo)
                If pennelloSelezione <> IntPtr.Zero Then ApiGdi.DeleteObject(pennelloSelezione)
                If pennelloBordo <> IntPtr.Zero Then ApiGdi.DeleteObject(pennelloBordo)
                If hdc <> IntPtr.Zero Then e.Graphics.ReleaseHdc(hdc)
            Catch
            End Try
        End Try
    End Sub

End Class

Friend Class BarraAvanzamentoGdi
    Inherits Control

    Private _valore As Integer = 0
    Private _testo As String = String.Empty

    Friend Sub New()
        Try
            SetStyle(ControlStyles.UserPaint Or ControlStyles.AllPaintingInWmPaint Or ControlStyles.OptimizedDoubleBuffer Or ControlStyles.ResizeRedraw, True)
            TabStop = False
            BackColor = Color.Black
        Catch ex As Exception
            RegistroAggiornamento.Scrivi("BarraAvanzamentoGdi.New: " & ex.Message)
        End Try
    End Sub

    Friend Property Valore As Integer
        Get
            Return _valore
        End Get
        Set(ByVal value As Integer)
            Try
                Dim nuovo As Integer = value
                If nuovo < 0 Then nuovo = 0
                If nuovo > 100 Then nuovo = 100

                If nuovo <> _valore Then
                    _valore = nuovo
                    Invalidate()
                End If
            Catch ex As Exception
                RegistroAggiornamento.Scrivi("BarraAvanzamentoGdi.Valore: " & ex.Message)
            End Try
        End Set
    End Property

    Friend Property Testo As String
        Get
            Return _testo
        End Get
        Set(ByVal value As String)
            Try
                Dim nuovo As String = If(value, String.Empty)
                If Not String.Equals(nuovo, _testo, StringComparison.Ordinal) Then
                    _testo = nuovo
                    Invalidate()
                End If
            Catch ex As Exception
                RegistroAggiornamento.Scrivi("BarraAvanzamentoGdi.Testo: " & ex.Message)
            End Try
        End Set
    End Property

    Protected Overrides Sub OnPaint(ByVal e As PaintEventArgs)
        Dim hdc As IntPtr = IntPtr.Zero
        Dim pennelloSfondo As IntPtr = IntPtr.Zero
        Dim pennelloCanale As IntPtr = IntPtr.Zero
        Dim pennelloRiempimento As IntPtr = IntPtr.Zero
        Dim pennelloBordo As IntPtr = IntPtr.Zero
        Dim carattere As IntPtr = IntPtr.Zero
        Dim carattereOriginale As IntPtr = IntPtr.Zero

        Try
            hdc = e.Graphics.GetHdc()

            pennelloSfondo = ApiGdi.CreateSolidBrush(ApiGdi.DaColore(Color.Black))
            pennelloCanale = ApiGdi.CreateSolidBrush(ApiGdi.DaColore(Color.FromArgb(45, 45, 45)))
            pennelloRiempimento = ApiGdi.CreateSolidBrush(ApiGdi.DaColore(Color.FromArgb(0, 150, 90)))
            pennelloBordo = ApiGdi.CreateSolidBrush(ApiGdi.DaColore(Color.FromArgb(200, 200, 200)))

            carattere = ApiGdi.CreateFont(-22, 0, 0, 0, ApiGdi.FW_BOLD, 0, 0, 0, ApiGdi.DEFAULT_CHARSET, ApiGdi.OUT_DEFAULT_PRECIS, ApiGdi.CLIP_DEFAULT_PRECIS, ApiGdi.CLEARTYPE_QUALITY, ApiGdi.DEFAULT_PITCH, "Segoe UI")

            Dim areaTotale As ApiGdi.RECT = ApiGdi.CreaRettangolo(New Rectangle(0, 0, Width, Height))
            ApiGdi.FillRect(hdc, areaTotale, pennelloSfondo)

            Dim altezzaBarra As Integer = 40
            Dim superiore As Integer = Math.Max((Height - altezzaBarra) \ 2, 0)
            Dim larghezzaCanale As Integer = Math.Max(Width - 40, 10)

            Dim areaCanale As ApiGdi.RECT = ApiGdi.CreaRettangolo(New Rectangle(20, superiore, larghezzaCanale, altezzaBarra))
            ApiGdi.FillRect(hdc, areaCanale, pennelloCanale)

            Dim larghezzaRiempita As Integer = CInt(Math.Floor((larghezzaCanale * _valore) / 100.0R))
            If larghezzaRiempita > 0 Then
                Dim areaRiempita As ApiGdi.RECT = ApiGdi.CreaRettangolo(New Rectangle(20, superiore, larghezzaRiempita, altezzaBarra))
                ApiGdi.FillRect(hdc, areaRiempita, pennelloRiempimento)
            End If

            ApiGdi.FrameRect(hdc, areaCanale, pennelloBordo)

            ApiGdi.SetBkMode(hdc, ApiGdi.TRANSPARENT)
            carattereOriginale = ApiGdi.SelectObject(hdc, carattere)
            ApiGdi.SetTextColor(hdc, ApiGdi.DaColore(Color.White))

            Dim etichetta As String = If(String.IsNullOrEmpty(_testo), _valore.ToString(CultureInfo.InvariantCulture) & " per cento", _testo)
            Dim areaTesto As ApiGdi.RECT = ApiGdi.CreaRettangolo(New Rectangle(20, superiore, larghezzaCanale, altezzaBarra))
            ApiGdi.DrawText(hdc, etichetta, etichetta.Length, areaTesto, ApiGdi.DT_CENTER Or ApiGdi.DT_VCENTER Or ApiGdi.DT_SINGLELINE Or ApiGdi.DT_NOPREFIX)
        Catch ex As Exception
            RegistroAggiornamento.Scrivi("BarraAvanzamentoGdi.OnPaint: " & ex.Message)
        Finally
            Try
                If carattereOriginale <> IntPtr.Zero AndAlso hdc <> IntPtr.Zero Then
                    ApiGdi.SelectObject(hdc, carattereOriginale)
                End If
                If carattere <> IntPtr.Zero Then ApiGdi.DeleteObject(carattere)
                If pennelloSfondo <> IntPtr.Zero Then ApiGdi.DeleteObject(pennelloSfondo)
                If pennelloCanale <> IntPtr.Zero Then ApiGdi.DeleteObject(pennelloCanale)
                If pennelloRiempimento <> IntPtr.Zero Then ApiGdi.DeleteObject(pennelloRiempimento)
                If pennelloBordo <> IntPtr.Zero Then ApiGdi.DeleteObject(pennelloBordo)
                If hdc <> IntPtr.Zero Then e.Graphics.ReleaseHdc(hdc)
            Catch
            End Try
        End Try
    End Sub

End Class

Friend NotInheritable Class CacheImpronte

    Private ReadOnly _blocco As New Object()
    Private ReadOnly _voci As New Dictionary(Of String, String)(StringComparer.OrdinalIgnoreCase)
    Private ReadOnly _percorsoFile As String
    Private ReadOnly _cartellaBase As String
    Private _modificata As Boolean = False

    Friend Sub New(ByVal cartellaBase As String)
        Try
            _cartellaBase = cartellaBase
            _percorsoFile = Path.Combine(cartellaBase, "dati", "cacke", "cacke.dat")
            Carica()
        Catch ex As Exception
            RegistroAggiornamento.Scrivi("CacheImpronte.New: " & ex.Message)
        End Try
    End Sub

    Private Function PercorsoRelativoBase(ByVal percorsoAssoluto As String) As String
        Try
            If String.IsNullOrEmpty(_cartellaBase) OrElse String.IsNullOrEmpty(percorsoAssoluto) Then
                Return percorsoAssoluto
            End If

            Dim base As String = _cartellaBase

            If Not base.EndsWith(Path.DirectorySeparatorChar) AndAlso Not base.EndsWith(Path.AltDirectorySeparatorChar) Then
                base &= Path.DirectorySeparatorChar
            End If

            If percorsoAssoluto.StartsWith(base, StringComparison.OrdinalIgnoreCase) Then
                Return percorsoAssoluto.Substring(base.Length)
            End If

            Return percorsoAssoluto
        Catch ex As Exception
            RegistroAggiornamento.Scrivi("CacheImpronte.PercorsoRelativoBase " & percorsoAssoluto & ": " & ex.Message)
            Return percorsoAssoluto
        End Try
    End Function

    Private Function PercorsoAssolutoDaRelativo(ByVal percorsoRelativoOAssoluto As String) As String
        Try
            If String.IsNullOrEmpty(percorsoRelativoOAssoluto) Then
                Return percorsoRelativoOAssoluto
            End If

            If Path.IsPathRooted(percorsoRelativoOAssoluto) Then
                Return percorsoRelativoOAssoluto
            End If

            Return Path.Combine(_cartellaBase, percorsoRelativoOAssoluto)
        Catch ex As Exception
            RegistroAggiornamento.Scrivi("CacheImpronte.PercorsoAssolutoDaRelativo " & percorsoRelativoOAssoluto & ": " & ex.Message)
            Return percorsoRelativoOAssoluto
        End Try
    End Function

    Private Sub AssicuraCartella()
        Try
            If String.IsNullOrEmpty(_percorsoFile) Then
                Return
            End If

            Dim cartella As String = Path.GetDirectoryName(_percorsoFile)

            If Not String.IsNullOrEmpty(cartella) AndAlso Not Directory.Exists(cartella) Then
                Directory.CreateDirectory(cartella)
            End If
        Catch ex As Exception
            RegistroAggiornamento.Scrivi("CacheImpronte.AssicuraCartella: " & ex.Message)
        End Try
    End Sub

    Private Sub Carica()
        Try
            If String.IsNullOrEmpty(_percorsoFile) OrElse Not File.Exists(_percorsoFile) Then
                Return
            End If

            For Each riga As String In File.ReadAllLines(_percorsoFile, Encoding.UTF8)
                Try
                    If String.IsNullOrWhiteSpace(riga) Then
                        Continue For
                    End If

                    Dim separatore As Integer = riga.LastIndexOf("|"c)

                    If separatore <= 0 OrElse separatore >= riga.Length - 1 Then
                        Continue For
                    End If

                    _voci(riga.Substring(0, separatore)) = riga.Substring(separatore + 1)
                Catch exRiga As Exception
                    RegistroAggiornamento.Scrivi("CacheImpronte.Carica (riga): " & exRiga.Message)
                End Try
            Next
        Catch ex As Exception
            RegistroAggiornamento.Scrivi("CacheImpronte.Carica: " & ex.Message)
        End Try
    End Sub

    Private Function ChiaveDi(ByVal percorsoFile As String, ByVal informazioni As FileInfo) As String
        Return PercorsoRelativoBase(percorsoFile) & "|" & informazioni.Length.ToString(CultureInfo.InvariantCulture) & "|" & informazioni.LastWriteTimeUtc.Ticks.ToString(CultureInfo.InvariantCulture)
    End Function

    Friend Function Impronta(ByVal percorsoFile As String, ByVal informazioni As FileInfo) As String
        Try
            Dim chiave As String = ChiaveDi(percorsoFile, informazioni)

            SyncLock _blocco
                Dim memorizzata As String = Nothing

                If _voci.TryGetValue(chiave, memorizzata) AndAlso Not String.IsNullOrEmpty(memorizzata) Then
                    Return memorizzata
                End If
            End SyncLock

            Dim calcolata As String = ImprontaSha256.DaFile(percorsoFile)

            If String.IsNullOrEmpty(calcolata) Then
                Return String.Empty
            End If

            SyncLock _blocco
                _voci(chiave) = calcolata
                _modificata = True
            End SyncLock

            Return calcolata
        Catch ex As Exception
            RegistroAggiornamento.Scrivi("CacheImpronte.Impronta " & percorsoFile & ": " & ex.Message)
            Return String.Empty
        End Try
    End Function

    Friend Sub Registra(ByVal percorsoFile As String, ByVal impronta As String)
        Try
            If String.IsNullOrEmpty(impronta) OrElse Not File.Exists(percorsoFile) Then
                Return
            End If

            Dim informazioni As New FileInfo(percorsoFile)
            Dim chiave As String = ChiaveDi(percorsoFile, informazioni)

            SyncLock _blocco
                _voci(chiave) = impronta
                _modificata = True
            End SyncLock
        Catch ex As Exception
            RegistroAggiornamento.Scrivi("CacheImpronte.Registra " & percorsoFile & ": " & ex.Message)
        End Try
    End Sub

    Friend Sub Salva()
        Try
            SyncLock _blocco
                If Not _modificata OrElse String.IsNullOrEmpty(_percorsoFile) Then
                    Return
                End If

                Dim contenuto As New StringBuilder()

                For Each coppia As KeyValuePair(Of String, String) In _voci
                    Try
                        Dim separatore As Integer = coppia.Key.IndexOf("|"c)

                        If separatore <= 0 Then
                            Continue For
                        End If

                        Dim percorsoVoce As String = coppia.Key.Substring(0, separatore)
                        Dim percorsoVoceAssoluto As String = PercorsoAssolutoDaRelativo(percorsoVoce)

                        If Not File.Exists(percorsoVoceAssoluto) Then
                            Continue For
                        End If

                        Dim informazioni As New FileInfo(percorsoVoceAssoluto)

                        If Not String.Equals(ChiaveDi(percorsoVoceAssoluto, informazioni), coppia.Key, StringComparison.OrdinalIgnoreCase) Then
                            Continue For
                        End If

                        contenuto.Append(coppia.Key)
                        contenuto.Append("|"c)
                        contenuto.Append(coppia.Value)
                        contenuto.Append(Environment.NewLine)
                    Catch exVoce As Exception
                        RegistroAggiornamento.Scrivi("CacheImpronte.Salva (voce): " & exVoce.Message)
                    End Try
                Next

                AssicuraCartella()

                File.WriteAllText(_percorsoFile, contenuto.ToString(), Encoding.UTF8)
                _modificata = False
            End SyncLock
        Catch ex As Exception
            RegistroAggiornamento.Scrivi("CacheImpronte.Salva: " & ex.Message)
        End Try
    End Sub

End Class

Friend NotInheritable Class SuonoAggiornamento

    Private Declare Auto Function midiOutOpen Lib "winmm.dll" (ByRef lphMidiOut As IntPtr, ByVal uDeviceID As Integer, ByVal dwCallback As IntPtr, ByVal dwInstance As IntPtr, ByVal dwFlags As Integer) As Integer
    Private Declare Auto Function midiOutClose Lib "winmm.dll" (ByVal hMidiOut As IntPtr) As Integer
    Private Declare Auto Function midiOutShortMsg Lib "winmm.dll" (ByVal hMidiOut As IntPtr, ByVal dwMsg As UInteger) As Integer
    Private Declare Auto Function midiOutReset Lib "winmm.dll" (ByVal hMidiOut As IntPtr) As Integer

    Private Const MIDI_MAPPER As Integer = -1
    Private Const STATO_PROGRAMMA As UInteger = &HC0UI
    Private Const STATO_NOTE_ON As UInteger = &H90UI
    Private Const STATO_NOTE_OFF As UInteger = &H80UI
    Private Const STRUMENTO_ATTESA As UInteger = 11UI

    Private Shared handleMidi As IntPtr = IntPtr.Zero
    Private Shared threadCiclo As Thread = Nothing
    Private Shared inEsecuzione As Boolean = False
    Private Shared ReadOnly sincronizzatore As New Object()

    Friend Shared Sub Avvia()
        Try
            SyncLock sincronizzatore
                If inEsecuzione Then
                    Return
                End If

                Dim risultatoApertura As Integer = midiOutOpen(handleMidi, MIDI_MAPPER, IntPtr.Zero, IntPtr.Zero, 0)

                If risultatoApertura <> 0 OrElse handleMidi = IntPtr.Zero Then
                    RegistroAggiornamento.Scrivi("SuonoAggiornamento.Avvia: apertura dispositivo MIDI fallita, codice " & risultatoApertura.ToString(CultureInfo.InvariantCulture))
                    handleMidi = IntPtr.Zero
                    Return
                End If

                ImpostaStrumento(STRUMENTO_ATTESA)

                inEsecuzione = True
                threadCiclo = New Thread(AddressOf CicloRiproduzione)
                threadCiclo.IsBackground = True
                threadCiclo.Priority = ThreadPriority.BelowNormal
                threadCiclo.Name = "WgSuonoAggiornamento"
                threadCiclo.Start()
            End SyncLock
        Catch ex As Exception
            RegistroAggiornamento.Scrivi("SuonoAggiornamento.Avvia eccezione: " & ex.Message & Environment.NewLine & ex.StackTrace)
            inEsecuzione = False
        End Try
    End Sub

    Friend Shared Sub Ferma()
        Try
            SyncLock sincronizzatore
                inEsecuzione = False
            End SyncLock

            Try
                If threadCiclo IsNot Nothing AndAlso threadCiclo.IsAlive Then
                    threadCiclo.Join(600)
                End If
            Catch exAttesa As Exception
                RegistroAggiornamento.Scrivi("SuonoAggiornamento.Ferma (attesa thread): " & exAttesa.Message)
            End Try

            SyncLock sincronizzatore
                Try
                    If handleMidi <> IntPtr.Zero Then
                        midiOutReset(handleMidi)
                        midiOutClose(handleMidi)
                    End If
                Catch exChiusura As Exception
                    RegistroAggiornamento.Scrivi("SuonoAggiornamento.Ferma (chiusura dispositivo): " & exChiusura.Message)
                Finally
                    handleMidi = IntPtr.Zero
                    threadCiclo = Nothing
                End Try
            End SyncLock
        Catch ex As Exception
            RegistroAggiornamento.Scrivi("SuonoAggiornamento.Ferma eccezione: " & ex.Message & Environment.NewLine & ex.StackTrace)
        End Try
    End Sub

    Private Shared Sub CicloRiproduzione()
        Try
            Dim note() As Byte = {67, 72}
            Dim indice As Integer = 0

            While inEsecuzione
                Try
                    Dim handleLocale As IntPtr = handleMidi

                    If handleLocale = IntPtr.Zero Then
                        Exit While
                    End If

                    Dim nota As Byte = note(indice Mod note.Length)
                    indice += 1

                    Dim messaggioOn As UInteger = STATO_NOTE_ON Or (CUInt(nota) << 8) Or (CUInt(34) << 16)
                    midiOutShortMsg(handleLocale, messaggioOn)

                    Thread.Sleep(120)

                    Dim messaggioOff As UInteger = STATO_NOTE_OFF Or (CUInt(nota) << 8)
                    midiOutShortMsg(handleLocale, messaggioOff)

                    Dim attesaResidua As Integer = 1300

                    While attesaResidua > 0 AndAlso inEsecuzione
                        Dim passo As Integer = Math.Min(150, attesaResidua)
                        Thread.Sleep(passo)
                        attesaResidua -= passo
                    End While
                Catch exNota As Exception
                    RegistroAggiornamento.Scrivi("SuonoAggiornamento.CicloRiproduzione (nota): " & exNota.Message)
                    Exit While
                End Try
            End While
        Catch ex As Exception
            RegistroAggiornamento.Scrivi("SuonoAggiornamento.CicloRiproduzione eccezione: " & ex.Message & Environment.NewLine & ex.StackTrace)
        End Try
    End Sub

    Private Shared Sub ImpostaStrumento(ByVal strumento As UInteger)
        Try
            Dim messaggio As UInteger = STATO_PROGRAMMA Or (strumento << 8)
            midiOutShortMsg(handleMidi, messaggio)
        Catch ex As Exception
            RegistroAggiornamento.Scrivi("SuonoAggiornamento.ImpostaStrumento: " & ex.Message)
        End Try
    End Sub

End Class

Friend Class VoceFileAggiornamento
    Public Property PercorsoRelativo As String = String.Empty
    Public Property Tipo As String = "normale"
    Public Property DataModificaRemota As DateTime = DateTime.MinValue
    Public Property DimensioneRemota As Long = -1
    Public Property DimensioneRemotaX86 As Long = -1
    Public Property DimensioneRemotaX64 As Long = -1
    Public Property ImprontaRemota As String = String.Empty
    Public Property ImprontaRemotaX86 As String = String.Empty
    Public Property ImprontaRemotaX64 As String = String.Empty
    Public Property UrlRelativaX86 As String = String.Empty
    Public Property UrlRelativaX64 As String = String.Empty

    Public Function DimensioneAttesa() As Long
        Try
            If Tipo.Equals("libmpv", StringComparison.OrdinalIgnoreCase) Then
                If Environment.Is64BitOperatingSystem Then
                    Return DimensioneRemotaX64
                End If
                Return DimensioneRemotaX86
            End If

            Return DimensioneRemota
        Catch ex As Exception
            RegistroAggiornamento.Scrivi("VoceFileAggiornamento.DimensioneAttesa: " & ex.Message)
            Return -1
        End Try
    End Function

    Public Function ImprontaAttesa() As String
        Try
            If Tipo.Equals("libmpv", StringComparison.OrdinalIgnoreCase) Then
                If Environment.Is64BitOperatingSystem Then
                    Return ImprontaRemotaX64
                End If
                Return ImprontaRemotaX86
            End If

            Return ImprontaRemota
        Catch ex As Exception
            RegistroAggiornamento.Scrivi("VoceFileAggiornamento.ImprontaAttesa: " & ex.Message)
            Return String.Empty
        End Try
    End Function
End Class

Friend Class ElencoFileAggiornamento
    Public Property Cartelle As New List(Of String)
    Public Property Voci As New List(Of VoceFileAggiornamento)
End Class

Friend Class RisultatoAggiornamento
    Public Property Successo As Boolean = False
    Public Property Annullato As Boolean = False
    Public Property FileScaricati As Integer = 0
    Public Property FileSaltati As Integer = 0
    Public Property FileFalliti As Integer = 0
    Public Property Errori As New List(Of String)
End Class

Friend Class ContestoLavoratore
    Public Property Indice As Integer
    Public Property Gestore As GestoreAggiornamento
End Class

Friend Class GestoreAggiornamento

    Private Const CHIAVE_ACCESSO_ELENCO_FILE As String = "Wg#7fA1-92Lm-Elenco-File-2026!"
    Private Const APP_NOME As String = "wglettore"
    Private Const URL_ELENCO_FILE As String = "https://www.wingabriel.net/wglettore/DownloadFile.aspx"
    Private Const URL_BASE_DOWNLOAD As String = "https://www.wingabriel.net/wglettore/"
    Private Const NUMERO_MASSIMO_DOWNLOAD_PARALLELI As Integer = 6
    Private Const TOLLERANZA_SECONDI_DATA As Integer = 2
    Private Const TENTATIVI_PER_FILE As Integer = 3
    Private Const DIMENSIONE_BUFFER As Integer = 131072
    Private Const CARTELLA_BACKUP As String = "hold"
    Private Const ESTENSIONE_BACKUP As String = ".WGHold"
    Private Const NOME_MANIFESTO As String = "WGHold.elenco"
    Private Const NOME_INFORMAZIONI As String = "WGHold.info"

    Private ReadOnly _httpClient As HttpClient
    Private ReadOnly _cartellaBase As String
    Private ReadOnly _coda As New ConcurrentQueue(Of VoceFileAggiornamento)()
    Private ReadOnly _erroriBlocco As New Object()
    Private ReadOnly _errori As New List(Of String)()
    Private ReadOnly _manifestoBlocco As New Object()
    Private ReadOnly _cacheImpronte As CacheImpronte

    Private _frazioni As Double()
    Private _fileTotali As Integer = 0
    Private _fileCompletati As Integer = 0
    Private _fileRiusciti As Integer = 0
    Private _fileFalliti As Integer = 0
    Private _tokenAnnullamento As CancellationToken
    Private _descrizioneCorrente As String = String.Empty
    ' Il rilascio firmato da cui viene l'elenco (Nothing se si usa il vecchio sistema) e l'indirizzo da cui scaricare i file.
    Private _manifestRilascio As RilascioFirmato.Manifest = Nothing
    Private _urlBaseDownload As String = URL_BASE_DOWNLOAD

    Friend Sub New(ByVal cartellaBase As String)
        _cartellaBase = cartellaBase
        _cacheImpronte = New CacheImpronte(cartellaBase)
        _httpClient = CreaHttpClient()
    End Sub

    Private Shared Function CreaHttpClient() As HttpClient
        Dim client As HttpClient = Nothing

        Try
            ServicePointManager.SecurityProtocol = CType(3072, SecurityProtocolType)
            ServicePointManager.Expect100Continue = False
            ServicePointManager.UseNagleAlgorithm = False
            ServicePointManager.DefaultConnectionLimit = Math.Max(ServicePointManager.DefaultConnectionLimit, NUMERO_MASSIMO_DOWNLOAD_PARALLELI * 2)
        Catch ex As Exception
            RegistroAggiornamento.Scrivi("GestoreAggiornamento.CreaHttpClient configurazione: " & ex.Message)
        End Try

        Try
            Dim gestore As New HttpClientHandler()

            Try
                gestore.AutomaticDecompression = DecompressionMethods.GZip Or DecompressionMethods.Deflate
            Catch
            End Try

            client = New HttpClient(gestore)
            client.Timeout = TimeSpan.FromMinutes(15)
            client.DefaultRequestHeaders.UserAgent.ParseAdd("WGLettore-Updater/2.0")
        Catch ex As Exception
            RegistroAggiornamento.Scrivi("GestoreAggiornamento.CreaHttpClient: " & ex.Message)
            client = New HttpClient()
            client.Timeout = TimeSpan.FromMinutes(15)
        End Try

        Return client
    End Function

    Friend ReadOnly Property PercentualeCorrente As Integer
        Get
            Try
                Dim totale As Integer = Interlocked.CompareExchange(_fileTotali, 0, 0)
                If totale <= 0 Then
                    Return 0
                End If

                Dim completati As Integer = Interlocked.CompareExchange(_fileCompletati, 0, 0)
                Dim parziale As Double = 0

                If _frazioni IsNot Nothing Then
                    For indice As Integer = 0 To _frazioni.Length - 1
                        parziale += Interlocked.CompareExchange(_frazioni(indice), 0.0R, 0.0R)
                    Next
                End If

                Dim avanzamento As Double = (completati + parziale) / CDbl(totale) * 100.0R
                Dim percentuale As Integer = CInt(Math.Floor(avanzamento))

                If percentuale < 0 Then percentuale = 0
                If percentuale > 100 Then percentuale = 100

                Return percentuale
            Catch ex As Exception
                RegistroAggiornamento.Scrivi("GestoreAggiornamento.PercentualeCorrente: " & ex.Message)
                Return 0
            End Try
        End Get
    End Property

    Friend ReadOnly Property DescrizioneCorrente As String
        Get
            Return _descrizioneCorrente
        End Get
    End Property

    Friend Function Esegui(ByVal tokenAnnullamento As CancellationToken) As RisultatoAggiornamento
        Dim risultato As New RisultatoAggiornamento()

        Try
            _tokenAnnullamento = tokenAnnullamento
            _descrizioneCorrente = "Lettura elenco file dal server"

            PulisciResiduiPrecedenti(_cartellaBase)

            Dim elenco As ElencoFileAggiornamento = OttieniElencoFileRemoto()

            If elenco Is Nothing OrElse (elenco.Voci.Count = 0 AndAlso elenco.Cartelle.Count = 0) Then
                risultato.Successo = True
                Return risultato
            End If

            FiltraVociPerArchitettura(elenco)
            CreaCartelleMancanti(elenco)

            Dim daScaricare As New List(Of VoceFileAggiornamento)()

            For Each voce As VoceFileAggiornamento In elenco.Voci
                If tokenAnnullamento.IsCancellationRequested Then
                    risultato.Annullato = True
                    Return risultato
                End If

                If FileDaAggiornare(voce) Then
                    daScaricare.Add(voce)
                End If
            Next

            Try
                _cacheImpronte.Salva()
            Catch exCache As Exception
                RegistroAggiornamento.Scrivi("Esegui (salvataggio cache impronte): " & exCache.Message)
            End Try

            risultato.FileSaltati = elenco.Voci.Count - daScaricare.Count

            If daScaricare.Count = 0 Then
                risultato.Successo = True
                SalvaStatoRilascio()
                Return risultato
            End If

            For Each voce As VoceFileAggiornamento In daScaricare
                _coda.Enqueue(voce)
            Next

            PreparaCartellaBackup()

            Interlocked.Exchange(_fileTotali, daScaricare.Count)
            Interlocked.Exchange(_fileCompletati, 0)
            Interlocked.Exchange(_fileRiusciti, 0)
            Interlocked.Exchange(_fileFalliti, 0)

            Dim numeroLavoratori As Integer = Math.Min(NUMERO_MASSIMO_DOWNLOAD_PARALLELI, daScaricare.Count)
            _frazioni = New Double(numeroLavoratori - 1) {}

            _descrizioneCorrente = "Scaricamento file"

            Dim lavoratori(numeroLavoratori - 1) As Thread

            For indice As Integer = 0 To numeroLavoratori - 1
                Dim contesto As New ContestoLavoratore()
                contesto.Indice = indice
                contesto.Gestore = Me

                lavoratori(indice) = New Thread(New ParameterizedThreadStart(AddressOf EseguiLavoratore))
                lavoratori(indice).IsBackground = True
                lavoratori(indice).Name = "WgDownload" & indice.ToString(CultureInfo.InvariantCulture)
                lavoratori(indice).Start(contesto)
            Next

            For indice As Integer = 0 To numeroLavoratori - 1
                Try
                    If lavoratori(indice) IsNot Nothing Then
                        lavoratori(indice).Join()
                    End If
                Catch ex As Exception
                    RegistroAggiornamento.Scrivi("Esegui: errore attesa lavoratore " & indice & ": " & ex.Message)
                End Try
            Next

            Try
                _cacheImpronte.Salva()
            Catch exCacheFinale As Exception
                RegistroAggiornamento.Scrivi("Esegui (salvataggio cache dopo i download): " & exCacheFinale.Message)
            End Try

            risultato.FileScaricati = Interlocked.CompareExchange(_fileRiusciti, 0, 0)
            risultato.FileFalliti = Interlocked.CompareExchange(_fileFalliti, 0, 0)
            risultato.Annullato = tokenAnnullamento.IsCancellationRequested

            SyncLock _erroriBlocco
                risultato.Errori = New List(Of String)(_errori)
            End SyncLock

            risultato.Successo = Not risultato.Annullato AndAlso risultato.FileFalliti = 0

            If risultato.Successo Then
                RimuoviMarchioZonaRicorsivo(_cartellaBase)
                SalvaStatoRilascio()
            End If

        Catch ex As Exception
            RegistroAggiornamento.Scrivi("GestoreAggiornamento.Esegui: " & ex.Message & " " & ex.StackTrace)
            risultato.Successo = False
            risultato.Errori.Add(ex.Message)
        End Try

        Return risultato
    End Function

    Private Shared Sub EseguiLavoratore(ByVal parametro As Object)
        Dim contesto As ContestoLavoratore = Nothing

        Try
            contesto = TryCast(parametro, ContestoLavoratore)
            If contesto Is Nothing OrElse contesto.Gestore Is Nothing Then
                Return
            End If

            contesto.Gestore.CicloLavoratore(contesto.Indice)
        Catch ex As Exception
            RegistroAggiornamento.Scrivi("GestoreAggiornamento.EseguiLavoratore: " & ex.Message)
        End Try
    End Sub

    Private Sub CicloLavoratore(ByVal indiceLavoratore As Integer)
        Try
            Dim voce As VoceFileAggiornamento = Nothing

            While _coda.TryDequeue(voce)
                If _tokenAnnullamento.IsCancellationRequested Then
                    Return
                End If

                Dim completato As Boolean = False

                Try
                    completato = ScaricaConTentativi(voce, indiceLavoratore)
                Catch ex As Exception
                    RegistroAggiornamento.Scrivi("CicloLavoratore " & voce.PercorsoRelativo & ": " & ex.Message)
                    completato = False
                End Try

                Try
                    Interlocked.Exchange(_frazioni(indiceLavoratore), 0.0R)
                Catch
                End Try

                If completato Then
                    Interlocked.Increment(_fileRiusciti)
                ElseIf Not _tokenAnnullamento.IsCancellationRequested Then
                    Interlocked.Increment(_fileFalliti)
                End If

                Interlocked.Increment(_fileCompletati)
            End While
        Catch ex As Exception
            RegistroAggiornamento.Scrivi("GestoreAggiornamento.CicloLavoratore: " & ex.Message)
        End Try
    End Sub

    Private Function ScaricaConTentativi(ByVal voce As VoceFileAggiornamento, ByVal indiceLavoratore As Integer) As Boolean
        Dim ultimoErrore As String = String.Empty

        For tentativo As Integer = 1 To TENTATIVI_PER_FILE
            If _tokenAnnullamento.IsCancellationRequested Then
                Return False
            End If

            Try
                ScaricaVoceFile(voce, indiceLavoratore)
                Return True
            Catch ex As OperationCanceledException
                Return False
            Catch ex As Exception
                ultimoErrore = ex.Message
                RegistroAggiornamento.Scrivi("Tentativo " & tentativo & " fallito per " & voce.PercorsoRelativo & ": " & ex.Message)

                Try
                    Interlocked.Exchange(_frazioni(indiceLavoratore), 0.0R)
                Catch
                End Try

                If tentativo < TENTATIVI_PER_FILE Then
                    Try
                        Thread.Sleep(1000 * tentativo)
                    Catch
                    End Try
                End If
            End Try
        Next

        SyncLock _erroriBlocco
            _errori.Add(voce.PercorsoRelativo & ": " & ultimoErrore)
        End SyncLock

        Return False
    End Function

    Private Sub ScaricaVoceFile(ByVal voce As VoceFileAggiornamento, ByVal indiceLavoratore As Integer)
        Dim urlRelativa As String

        If voce.Tipo.Equals("libmpv", StringComparison.OrdinalIgnoreCase) Then
            urlRelativa = If(Environment.Is64BitOperatingSystem, voce.UrlRelativaX64, voce.UrlRelativaX86)
        Else
            urlRelativa = voce.PercorsoRelativo
        End If

        If String.IsNullOrWhiteSpace(urlRelativa) Then
            Throw New InvalidOperationException("URL di download non disponibile per la voce.")
        End If

        Dim urlCompleto As String = RilascioFirmato.UrlFile(_urlBaseDownload, urlRelativa)
        Dim percorsoLocale As String = OttieniPercorsoLocale(voce)
        Dim cartellaFile As String = Path.GetDirectoryName(percorsoLocale)

        If Not String.IsNullOrEmpty(cartellaFile) AndAlso Not Directory.Exists(cartellaFile) Then
            Directory.CreateDirectory(cartellaFile)
        End If

        Dim percorsoTemporaneo As String = percorsoLocale & ".wgtmp"

        If File.Exists(percorsoTemporaneo) Then
            Try
                File.Delete(percorsoTemporaneo)
            Catch ex As Exception
                RegistroAggiornamento.Scrivi("ScaricaVoceFile: impossibile eliminare temporaneo " & percorsoTemporaneo & ": " & ex.Message)
            End Try
        End If

        Dim byteScritti As Long = 0
        Dim lunghezzaAttesa As Long = voce.DimensioneAttesa()

        Using richiesta As New HttpRequestMessage(HttpMethod.Get, urlCompleto)
            Using risposta As HttpResponseMessage = _httpClient.SendAsync(richiesta, HttpCompletionOption.ResponseHeadersRead, _tokenAnnullamento).GetAwaiter().GetResult()
                risposta.EnsureSuccessStatusCode()

                If lunghezzaAttesa < 0 AndAlso risposta.Content.Headers.ContentLength.HasValue Then
                    lunghezzaAttesa = risposta.Content.Headers.ContentLength.Value
                End If

                Using flussoRemoto As Stream = risposta.Content.ReadAsStreamAsync().GetAwaiter().GetResult()
                    Using flussoLocale As New FileStream(percorsoTemporaneo, FileMode.Create, FileAccess.Write, FileShare.None, DIMENSIONE_BUFFER)
                        Dim buffer(DIMENSIONE_BUFFER - 1) As Byte
                        Dim letti As Integer = flussoRemoto.Read(buffer, 0, buffer.Length)

                        While letti > 0
                            If _tokenAnnullamento.IsCancellationRequested Then
                                Throw New OperationCanceledException()
                            End If

                            flussoLocale.Write(buffer, 0, letti)
                            byteScritti += letti

                            If lunghezzaAttesa > 0 Then
                                Try
                                    Dim frazione As Double = byteScritti / CDbl(lunghezzaAttesa)
                                    If frazione > 1.0R Then frazione = 1.0R
                                    Interlocked.Exchange(_frazioni(indiceLavoratore), frazione)
                                Catch
                                End Try
                            End If

                            letti = flussoRemoto.Read(buffer, 0, buffer.Length)
                        End While

                        flussoLocale.Flush(True)
                    End Using
                End Using
            End Using
        End Using

        If lunghezzaAttesa >= 0 AndAlso byteScritti <> lunghezzaAttesa Then
            Try
                File.Delete(percorsoTemporaneo)
            Catch
            End Try
            Throw New IOException("Dimensione scaricata non corrispondente per " & voce.PercorsoRelativo & ": attesi " & lunghezzaAttesa & " byte, ricevuti " & byteScritti & ".")
        End If

        If byteScritti = 0 Then
            Try
                File.Delete(percorsoTemporaneo)
            Catch
            End Try
            Throw New IOException("File scaricato vuoto: " & voce.PercorsoRelativo)
        End If

        RimuoviMarchioZona(percorsoTemporaneo)

        Dim esistevaPrima As Boolean = File.Exists(percorsoLocale)

        If esistevaPrima Then
            CreaCopiaDiSicurezza(percorsoLocale)
        End If

        SostituisciFile(percorsoTemporaneo, percorsoLocale)

        RimuoviMarchioZona(percorsoLocale)

        RegistraNelManifesto(If(esistevaPrima, "S", "N"), percorsoLocale)

        If voce.DataModificaRemota <> DateTime.MinValue Then
            Try
                File.SetLastWriteTimeUtc(percorsoLocale, voce.DataModificaRemota)
            Catch ex As Exception
                RegistroAggiornamento.Scrivi("ScaricaVoceFile: errore impostazione data " & percorsoLocale & ": " & ex.Message)
            End Try
        End If

        Try
            Dim improntaScaricata As String = voce.ImprontaAttesa()

            If Not String.IsNullOrEmpty(improntaScaricata) Then
                _cacheImpronte.Registra(percorsoLocale, improntaScaricata)
            End If
        Catch exCache As Exception
            RegistroAggiornamento.Scrivi("ScaricaVoceFile (registrazione impronta): " & exCache.Message)
        End Try
    End Sub

    Private Sub SostituisciFile(ByVal origine As String, ByVal destinazione As String)
        Dim ultimoErrore As Exception = Nothing

        For tentativo As Integer = 1 To 6
            Try
                If File.Exists(destinazione) Then
                    File.Replace(origine, destinazione, Nothing, True)
                Else
                    File.Move(origine, destinazione)
                End If
                Return
            Catch ex As Exception
                ultimoErrore = ex

                Try
                    Thread.Sleep(400 * tentativo)
                Catch
                End Try
            End Try
        Next

        Try
            Dim percorsoVecchio As String = destinazione & ".wgold"

            If File.Exists(percorsoVecchio) Then
                Try
                    File.Delete(percorsoVecchio)
                Catch
                End Try
            End If

            If File.Exists(destinazione) Then
                File.Move(destinazione, percorsoVecchio)
            End If

            File.Move(origine, destinazione)

            Try
                If File.Exists(percorsoVecchio) Then
                    ApiFile.MoveFileExW(percorsoVecchio, Nothing, ApiFile.MOVEFILE_DELAY_UNTIL_REBOOT)
                End If
            Catch
            End Try

            Return
        Catch ex As Exception
            RegistroAggiornamento.Scrivi("SostituisciFile: fallita anche la sostituzione con rinomina per " & destinazione & ": " & ex.Message)
        End Try

        If ultimoErrore IsNot Nothing Then
            Throw ultimoErrore
        End If
    End Sub

    Friend Shared Sub RimuoviMarchioZona(ByVal percorsoFile As String)
        Try
            If String.IsNullOrWhiteSpace(percorsoFile) OrElse Not File.Exists(percorsoFile) Then
                Return
            End If

            ApiFile.DeleteFileW(percorsoFile & ":Zone.Identifier")
        Catch ex As Exception
            RegistroAggiornamento.Scrivi("RimuoviMarchioZona " & percorsoFile & ": " & ex.Message)
        End Try
    End Sub

    Friend Shared Sub RimuoviMarchioZonaRicorsivo(ByVal cartella As String)
        Try
            If String.IsNullOrWhiteSpace(cartella) OrElse Not Directory.Exists(cartella) Then
                Return
            End If

            For Each percorsoFile As String In Directory.GetFiles(cartella, "*.*", SearchOption.AllDirectories)
                Try
                    ApiFile.DeleteFileW(percorsoFile & ":Zone.Identifier")
                Catch
                End Try
            Next
        Catch ex As Exception
            RegistroAggiornamento.Scrivi("RimuoviMarchioZonaRicorsivo: " & ex.Message)
        End Try
    End Sub

    Private Shared Sub PulisciResiduiPrecedenti(ByVal cartella As String)
        Try
            If String.IsNullOrWhiteSpace(cartella) OrElse Not Directory.Exists(cartella) Then
                Return
            End If

            Dim modelli() As String = {"*.wgtmp", "*.wgold"}

            For Each modello As String In modelli
                Try
                    For Each percorsoFile As String In Directory.GetFiles(cartella, modello, SearchOption.AllDirectories)
                        Try
                            File.Delete(percorsoFile)
                        Catch
                        End Try
                    Next
                Catch ex As Exception
                    RegistroAggiornamento.Scrivi("PulisciResiduiPrecedenti modello " & modello & ": " & ex.Message)
                End Try
            Next
        Catch ex As Exception
            RegistroAggiornamento.Scrivi("PulisciResiduiPrecedenti: " & ex.Message)
        End Try
    End Sub

    ' Il rilascio firmato del canale scelto (stabile o beta). Restituisce Nothing se il sito non lo ha o se qualcosa non torna (firma,
    ' impronta del manifest): in quel caso si usa il vecchio elenco.
    Private Function OttieniElencoDaRilascio() As ElencoFileAggiornamento
        Try
            Dim canale As String = RilascioFirmato.NormalizzaCanale(New ConfigurazioneAggiornamento().LeggiTesto("canale_aggiornamenti", "stable"))
            Dim errore As String = ""
            Dim puntatore As RilascioFirmato.Puntatore = RilascioFirmato.LeggiPuntatore(APP_NOME, canale, errore)
            If puntatore Is Nothing Then
                If errore <> "" Then RegistroAggiornamento.Scrivi("Rilascio firmato non disponibile: " & errore)
                Return Nothing
            End If
            If RilascioFirmato.PiuVecchioDelloInstallato(CartellaStatoRilascio(), puntatore) Then
                RegistroAggiornamento.Scrivi("Il puntatore del rilascio è più vecchio di quello applicato: ignorato.")
                Return Nothing
            End If
            Dim manifest As RilascioFirmato.Manifest = RilascioFirmato.LeggiManifest(puntatore, errore)
            If manifest Is Nothing Then
                RegistroAggiornamento.Scrivi("Manifest del rilascio non valido: " & errore)
                Return Nothing
            End If

            Dim elenco As New ElencoFileAggiornamento()
            For Each c As String In manifest.Cartelle
                If PercorsoRelativoValido(c) Then elenco.Cartelle.Add(c)
            Next
            For Each v As RilascioFirmato.VoceManifest In manifest.Voci
                If Not PercorsoRelativoValido(v.Percorso) Then Continue For
                Dim voce As New VoceFileAggiornamento()
                voce.PercorsoRelativo = v.Percorso
                Dim dataModifica As DateTime
                If DateTime.TryParse(v.DataUtc, CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal Or DateTimeStyles.AssumeUniversal, dataModifica) Then voce.DataModificaRemota = dataModifica
                If v.Libmpv Then
                    voce.Tipo = "libmpv"
                    voce.UrlRelativaX86 = v.UrlX86
                    voce.UrlRelativaX64 = v.UrlX64
                    voce.DimensioneRemotaX86 = v.DimensioneX86
                    voce.DimensioneRemotaX64 = v.DimensioneX64
                    voce.ImprontaRemotaX86 = v.ImprontaX86
                    voce.ImprontaRemotaX64 = v.ImprontaX64
                Else
                    voce.Tipo = "normale"
                    voce.DimensioneRemota = v.Dimensione
                    voce.ImprontaRemota = v.Impronta
                End If
                elenco.Voci.Add(voce)
            Next
            If elenco.Voci.Count = 0 Then Return Nothing
            _manifestRilascio = manifest
            _urlBaseDownload = manifest.UrlBaseFile
            RegistroAggiornamento.Scrivi("Rilascio " & puntatore.Versione & " del canale " & puntatore.Canale & ": " & elenco.Voci.Count & " voci.")
            Return elenco
        Catch ex As Exception
            RegistroAggiornamento.Scrivi("OttieniElencoDaRilascio: " & ex.Message)
            _manifestRilascio = Nothing
            _urlBaseDownload = URL_BASE_DOWNLOAD
            Return Nothing
        End Try
    End Function

    Private Function CartellaStatoRilascio() As String
        Return Path.Combine(_cartellaBase, "dati", "cacke")
    End Function

    Private Sub SalvaStatoRilascio()
        Try
            If _manifestRilascio IsNot Nothing Then
                RilascioFirmato.SalvaStato(CartellaStatoRilascio(), _manifestRilascio.Puntatore)
                RilascioFirmato.SalvaFotografia(CartellaStatoRilascio(), _cartellaBase, _manifestRilascio)
            End If
        Catch ex As Exception
            RegistroAggiornamento.Scrivi("SalvaStatoRilascio: " & ex.Message)
        End Try
    End Sub

    Private Function OttieniElencoFileRemoto() As ElencoFileAggiornamento
        _manifestRilascio = Nothing
        _urlBaseDownload = URL_BASE_DOWNLOAD
        Dim daRilascio As ElencoFileAggiornamento = OttieniElencoDaRilascio()
        If daRilascio IsNot Nothing Then Return daRilascio

        Dim elenco As New ElencoFileAggiornamento()

        For tentativo As Integer = 1 To 3
            Try
                Dim contenutoForm As New Dictionary(Of String, String)()
                contenutoForm.Add("chiave", CHIAVE_ACCESSO_ELENCO_FILE)
                contenutoForm.Add("app", APP_NOME)

                Using contenuto As New FormUrlEncodedContent(contenutoForm)
                    Using risposta As HttpResponseMessage = _httpClient.PostAsync(URL_ELENCO_FILE, contenuto, _tokenAnnullamento).GetAwaiter().GetResult()
                        risposta.EnsureSuccessStatusCode()

                        Dim xmlTesto As String = risposta.Content.ReadAsStringAsync().GetAwaiter().GetResult()

                        If String.IsNullOrWhiteSpace(xmlTesto) Then
                            Throw New InvalidOperationException("Risposta vuota dal server.")
                        End If

                        Dim documento As XDocument = XDocument.Parse(xmlTesto)

                        For Each elCartella As XElement In documento.Descendants("Cartella")
                            Try
                                Dim attrPercorso As XAttribute = elCartella.Attribute("percorso")
                                If attrPercorso IsNot Nothing AndAlso Not String.IsNullOrWhiteSpace(attrPercorso.Value) Then
                                    elenco.Cartelle.Add(attrPercorso.Value)
                                End If
                            Catch ex As Exception
                                RegistroAggiornamento.Scrivi("OttieniElencoFileRemoto cartella: " & ex.Message)
                            End Try
                        Next

                        For Each elVoce As XElement In documento.Descendants("Voce")
                            Try
                                Dim voce As New VoceFileAggiornamento()

                                Dim attrPercorso As XAttribute = elVoce.Attribute("percorso")
                                voce.PercorsoRelativo = If(attrPercorso IsNot Nothing, attrPercorso.Value, String.Empty)

                                Dim attrTipo As XAttribute = elVoce.Attribute("tipo")
                                voce.Tipo = If(attrTipo IsNot Nothing AndAlso Not String.IsNullOrWhiteSpace(attrTipo.Value), attrTipo.Value, "normale")

                                Dim attrData As XAttribute = elVoce.Attribute("dataModifica")
                                If attrData IsNot Nothing Then
                                    Dim dataModifica As DateTime
                                    If DateTime.TryParse(attrData.Value, CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal Or DateTimeStyles.AssumeUniversal, dataModifica) Then
                                        voce.DataModificaRemota = dataModifica
                                    End If
                                End If

                                voce.DimensioneRemota = LeggiDimensione(elVoce, "dimensione")
                                voce.ImprontaRemota = LeggiImpronta(elVoce, "hash")

                                If voce.Tipo.Equals("libmpv", StringComparison.OrdinalIgnoreCase) Then
                                    Dim attrX86 As XAttribute = elVoce.Attribute("urlX86")
                                    Dim attrX64 As XAttribute = elVoce.Attribute("urlX64")
                                    voce.UrlRelativaX86 = If(attrX86 IsNot Nothing, attrX86.Value, String.Empty)
                                    voce.UrlRelativaX64 = If(attrX64 IsNot Nothing, attrX64.Value, String.Empty)
                                    voce.DimensioneRemotaX86 = LeggiDimensione(elVoce, "dimensioneX86")
                                    voce.DimensioneRemotaX64 = LeggiDimensione(elVoce, "dimensioneX64")
                                    voce.ImprontaRemotaX86 = LeggiImpronta(elVoce, "hashX86")
                                    voce.ImprontaRemotaX64 = LeggiImpronta(elVoce, "hashX64")
                                End If

                                If Not String.IsNullOrWhiteSpace(voce.PercorsoRelativo) AndAlso PercorsoRelativoValido(voce.PercorsoRelativo) AndAlso Not RilascioFirmato.FileDaNonScaricare(voce.PercorsoRelativo) Then
                                    elenco.Voci.Add(voce)
                                End If
                            Catch ex As Exception
                                RegistroAggiornamento.Scrivi("OttieniElencoFileRemoto voce: " & ex.Message)
                            End Try
                        Next
                    End Using
                End Using

                Return elenco
            Catch ex As OperationCanceledException
                Return elenco
            Catch ex As Exception
                RegistroAggiornamento.Scrivi("OttieniElencoFileRemoto tentativo " & tentativo & ": " & ex.Message)

                If tentativo < 3 Then
                    Try
                        Thread.Sleep(1500 * tentativo)
                    Catch
                    End Try
                End If
            End Try
        Next

        Return elenco
    End Function

    Private Shared Function PercorsoRelativoValido(ByVal percorsoRelativo As String) As Boolean
        Try
            If String.IsNullOrWhiteSpace(percorsoRelativo) Then
                Return False
            End If

            Dim normalizzato As String = percorsoRelativo.Replace("\"c, "/"c)

            If normalizzato.Contains("..") Then
                Return False
            End If

            If normalizzato.StartsWith("/", StringComparison.Ordinal) Then
                Return False
            End If

            If normalizzato.Length > 1 AndAlso normalizzato.Chars(1) = ":"c Then
                Return False
            End If

            Return True
        Catch ex As Exception
            RegistroAggiornamento.Scrivi("PercorsoRelativoValido: " & ex.Message)
            Return False
        End Try
    End Function

    Private Sub FiltraVociPerArchitettura(ByVal elenco As ElencoFileAggiornamento)
        Try
            Dim cartellaArchSbagliata As String = If(Environment.Is64BitOperatingSystem, "x86", "x64")

            Dim cartelleTenute As New List(Of String)()
            For Each cartella As String In elenco.Cartelle
                If Not PercorsoInCartella(cartella, cartellaArchSbagliata) Then
                    cartelleTenute.Add(cartella)
                End If
            Next
            elenco.Cartelle = cartelleTenute

            Dim vociTenute As New List(Of VoceFileAggiornamento)()
            For Each voce As VoceFileAggiornamento In elenco.Voci
                If voce.Tipo.Equals("libmpv", StringComparison.OrdinalIgnoreCase) Then
                    vociTenute.Add(voce)
                ElseIf Not PercorsoInCartella(voce.PercorsoRelativo, cartellaArchSbagliata) Then
                    vociTenute.Add(voce)
                End If
            Next
            elenco.Voci = vociTenute
        Catch ex As Exception
            RegistroAggiornamento.Scrivi("FiltraVociPerArchitettura: " & ex.Message)
        End Try
    End Sub

    Private Shared Function PercorsoInCartella(ByVal percorsoRelativo As String, ByVal nomeCartella As String) As Boolean
        Try
            If String.IsNullOrEmpty(percorsoRelativo) Then
                Return False
            End If

            Dim normalizzato As String = percorsoRelativo.Replace("\"c, "/"c).TrimStart("/"c)

            Return normalizzato.Equals(nomeCartella, StringComparison.OrdinalIgnoreCase) OrElse normalizzato.StartsWith(nomeCartella & "/", StringComparison.OrdinalIgnoreCase)
        Catch ex As Exception
            RegistroAggiornamento.Scrivi("PercorsoInCartella: " & ex.Message)
            Return False
        End Try
    End Function

    Private Sub CreaCartelleMancanti(ByVal elenco As ElencoFileAggiornamento)
        Try
            For Each percorsoCartella As String In elenco.Cartelle
                Try
                    If Not PercorsoRelativoValido(percorsoCartella) Then
                        Continue For
                    End If

                    Dim percorsoCompleto As String = Path.Combine(_cartellaBase, percorsoCartella.Replace("/"c, Path.DirectorySeparatorChar))

                    If Not Directory.Exists(percorsoCompleto) Then
                        Directory.CreateDirectory(percorsoCompleto)
                    End If
                Catch ex As Exception
                    RegistroAggiornamento.Scrivi("CreaCartelleMancanti " & percorsoCartella & ": " & ex.Message)
                End Try
            Next

            For Each voce As VoceFileAggiornamento In elenco.Voci
                Try
                    If voce.Tipo.Equals("libmpv", StringComparison.OrdinalIgnoreCase) Then
                        Continue For
                    End If

                    Dim percorsoCompleto As String = OttieniPercorsoLocale(voce)
                    Dim cartellaFile As String = Path.GetDirectoryName(percorsoCompleto)

                    If Not String.IsNullOrEmpty(cartellaFile) AndAlso Not Directory.Exists(cartellaFile) Then
                        Directory.CreateDirectory(cartellaFile)
                    End If
                Catch ex As Exception
                    RegistroAggiornamento.Scrivi("CreaCartelleMancanti file " & voce.PercorsoRelativo & ": " & ex.Message)
                End Try
            Next
        Catch ex As Exception
            RegistroAggiornamento.Scrivi("CreaCartelleMancanti: " & ex.Message)
        End Try
    End Sub

    Private Function OttieniPercorsoLocale(ByVal voce As VoceFileAggiornamento) As String
        Try
            If voce.Tipo.Equals("libmpv", StringComparison.OrdinalIgnoreCase) Then
                Return Path.Combine(_cartellaBase, Path.GetFileName(voce.PercorsoRelativo))
            End If

            Return Path.Combine(_cartellaBase, voce.PercorsoRelativo.Replace("/"c, Path.DirectorySeparatorChar))
        Catch ex As Exception
            RegistroAggiornamento.Scrivi("OttieniPercorsoLocale " & voce.PercorsoRelativo & ": " & ex.Message)
            Throw
        End Try
    End Function

    Private Shared Function LeggiDimensione(ByVal elemento As XElement, ByVal nomeAttributo As String) As Long
        Try
            Dim attributo As XAttribute = elemento.Attribute(nomeAttributo)

            If attributo Is Nothing OrElse String.IsNullOrWhiteSpace(attributo.Value) Then
                Return -1
            End If

            Dim valore As Long = 0

            If Long.TryParse(attributo.Value, NumberStyles.Integer, CultureInfo.InvariantCulture, valore) AndAlso valore >= 0 Then
                Return valore
            End If

            Return -1
        Catch ex As Exception
            RegistroAggiornamento.Scrivi("LeggiDimensione " & nomeAttributo & ": " & ex.Message)
            Return -1
        End Try
    End Function

    Private Shared Function LeggiImpronta(ByVal elemento As XElement, ByVal nomeAttributo As String) As String
        Try
            Dim attributo As XAttribute = elemento.Attribute(nomeAttributo)

            If attributo Is Nothing OrElse String.IsNullOrWhiteSpace(attributo.Value) Then
                Return String.Empty
            End If

            Return attributo.Value.Trim()
        Catch ex As Exception
            RegistroAggiornamento.Scrivi("LeggiImpronta " & nomeAttributo & ": " & ex.Message)
            Return String.Empty
        End Try
    End Function

    Private Shared Function CalcolaImprontaLocale(ByVal percorsoFile As String) As String
        Try
            Return ImprontaSha256.DaFile(percorsoFile)
        Catch ex As Exception
            RegistroAggiornamento.Scrivi("CalcolaImprontaLocale " & percorsoFile & ": " & ex.Message)
            Return String.Empty
        End Try
    End Function

    Private Function FileDaAggiornare(ByVal voce As VoceFileAggiornamento) As Boolean
        Try
            Dim percorsoLocale As String = OttieniPercorsoLocale(voce)

            If Not File.Exists(percorsoLocale) Then
                Return True
            End If

            Dim informazioniLocali As New FileInfo(percorsoLocale)
            Dim dimensioneLocale As Long = informazioniLocali.Length
            Dim dataLocale As DateTime = informazioniLocali.LastWriteTimeUtc
            Dim dimensioneAttesa As Long = voce.DimensioneAttesa()

            If dimensioneAttesa >= 0 AndAlso dimensioneAttesa <> dimensioneLocale Then
                Return True
            End If

            Dim improntaAttesa As String = voce.ImprontaAttesa()

            If Not String.IsNullOrEmpty(improntaAttesa) Then
                Dim improntaLocale As String = _cacheImpronte.Impronta(percorsoLocale, informazioniLocali)

                If String.IsNullOrEmpty(improntaLocale) Then
                    Return True
                End If

                Return Not String.Equals(improntaLocale, improntaAttesa, StringComparison.OrdinalIgnoreCase)
            End If

            ' La data non decide mai: un file "più recente" sul server può essere di un'altra versione.
            Return False
        Catch ex As Exception
            RegistroAggiornamento.Scrivi("FileDaAggiornare " & voce.PercorsoRelativo & ": " & ex.Message)
            Return True
        End Try
    End Function

    Friend Shared Function PercorsoCartellaBackup(ByVal cartellaBase As String) As String
        Return Path.Combine(cartellaBase, CARTELLA_BACKUP)
    End Function

    Friend Shared Function PercorsoManifesto(ByVal cartellaBase As String) As String
        Return Path.Combine(PercorsoCartellaBackup(cartellaBase), NOME_MANIFESTO)
    End Function

    Friend Shared Function PercorsoInformazioni(ByVal cartellaBase As String) As String
        Return Path.Combine(PercorsoCartellaBackup(cartellaBase), NOME_INFORMAZIONI)
    End Function

    Friend Shared Function EsisteVersionePrecedente(ByVal cartellaBase As String) As Boolean
        Try
            Dim fileManifesto As String = PercorsoManifesto(cartellaBase)

            If Not File.Exists(fileManifesto) Then
                Return False
            End If

            Dim righe() As String = File.ReadAllLines(fileManifesto, Encoding.UTF8)

            For Each riga As String In righe
                If Not String.IsNullOrWhiteSpace(riga) Then
                    Return True
                End If
            Next

            Return False
        Catch ex As Exception
            RegistroAggiornamento.Scrivi("EsisteVersionePrecedente: " & ex.Message)
            Return False
        End Try
    End Function

    Friend Shared Function DescrizioneVersionePrecedente(ByVal cartellaBase As String) As String
        Try
            Dim fileInformazioni As String = PercorsoInformazioni(cartellaBase)

            If Not File.Exists(fileInformazioni) Then
                Return String.Empty
            End If

            Dim contenuto As String = File.ReadAllText(fileInformazioni, Encoding.UTF8).Trim()

            If String.IsNullOrWhiteSpace(contenuto) Then
                Return String.Empty
            End If

            Return contenuto
        Catch ex As Exception
            RegistroAggiornamento.Scrivi("DescrizioneVersionePrecedente: " & ex.Message)
            Return String.Empty
        End Try
    End Function

    Private Function PercorsoRelativoDaBase(ByVal percorsoCompleto As String) As String
        Try
            Dim radice As String = Path.GetFullPath(_cartellaBase).TrimEnd(Path.DirectorySeparatorChar) & Path.DirectorySeparatorChar
            Dim completo As String = Path.GetFullPath(percorsoCompleto)

            If completo.StartsWith(radice, StringComparison.OrdinalIgnoreCase) Then
                Return completo.Substring(radice.Length)
            End If

            Return Path.GetFileName(completo)
        Catch ex As Exception
            RegistroAggiornamento.Scrivi("PercorsoRelativoDaBase " & percorsoCompleto & ": " & ex.Message)
            Return Path.GetFileName(percorsoCompleto)
        End Try
    End Function

    Private Sub PreparaCartellaBackup()
        Try
            Dim cartella As String = PercorsoCartellaBackup(_cartellaBase)

            If Directory.Exists(cartella) Then
                Try
                    Directory.Delete(cartella, True)
                Catch ex As Exception
                    RegistroAggiornamento.Scrivi("PreparaCartellaBackup: impossibile eliminare la cartella precedente: " & ex.Message)

                    Try
                        For Each percorsoFile As String In Directory.GetFiles(cartella, "*.*", SearchOption.AllDirectories)
                            Try
                                File.Delete(percorsoFile)
                            Catch
                            End Try
                        Next
                    Catch
                    End Try
                End Try
            End If

            If Not Directory.Exists(cartella) Then
                Directory.CreateDirectory(cartella)
            End If

            Try
                Dim informazioni As New StringBuilder()
                informazioni.Append("Copia della versione precedente creata il ")
                informazioni.Append(DateTime.Now.ToString("dd/MM/yyyy", CultureInfo.InvariantCulture))
                informazioni.Append(" alle ")
                informazioni.Append(DateTime.Now.ToString("HH:mm", CultureInfo.InvariantCulture))

                File.WriteAllText(PercorsoInformazioni(_cartellaBase), informazioni.ToString(), Encoding.UTF8)
            Catch ex As Exception
                RegistroAggiornamento.Scrivi("PreparaCartellaBackup: errore scrittura informazioni: " & ex.Message)
            End Try
        Catch ex As Exception
            RegistroAggiornamento.Scrivi("PreparaCartellaBackup: " & ex.Message)
        End Try
    End Sub

    Private Sub CreaCopiaDiSicurezza(ByVal percorsoLocale As String)
        Try
            If Not File.Exists(percorsoLocale) Then
                Return
            End If

            Dim relativo As String = PercorsoRelativoDaBase(percorsoLocale)

            If String.IsNullOrWhiteSpace(relativo) Then
                Return
            End If

            Dim destinazione As String = Path.Combine(PercorsoCartellaBackup(_cartellaBase), relativo & ESTENSIONE_BACKUP)
            Dim cartellaDestinazione As String = Path.GetDirectoryName(destinazione)

            If Not String.IsNullOrEmpty(cartellaDestinazione) AndAlso Not Directory.Exists(cartellaDestinazione) Then
                Directory.CreateDirectory(cartellaDestinazione)
            End If

            File.Copy(percorsoLocale, destinazione, True)
        Catch ex As Exception
            RegistroAggiornamento.Scrivi("CreaCopiaDiSicurezza " & percorsoLocale & ": " & ex.Message)
        End Try
    End Sub

    Private Sub RegistraNelManifesto(ByVal tipo As String, ByVal percorsoLocale As String)
        Try
            Dim relativo As String = PercorsoRelativoDaBase(percorsoLocale)

            If String.IsNullOrWhiteSpace(relativo) Then
                Return
            End If

            SyncLock _manifestoBlocco
                File.AppendAllText(PercorsoManifesto(_cartellaBase), tipo & "|" & relativo.Replace(Path.DirectorySeparatorChar, "/"c) & Environment.NewLine, Encoding.UTF8)
            End SyncLock
        Catch ex As Exception
            RegistroAggiornamento.Scrivi("RegistraNelManifesto " & percorsoLocale & ": " & ex.Message)
        End Try
    End Sub

    Friend Function EseguiRipristino(ByVal tokenAnnullamento As CancellationToken) As RisultatoAggiornamento
        Dim risultato As New RisultatoAggiornamento()

        Try
            _tokenAnnullamento = tokenAnnullamento
            _descrizioneCorrente = "Lettura della versione precedente"

            Dim fileManifesto As String = PercorsoManifesto(_cartellaBase)

            If Not File.Exists(fileManifesto) Then
                risultato.Successo = False
                risultato.Errori.Add("Nessuna versione precedente disponibile.")
                Return risultato
            End If

            Dim righe() As String = File.ReadAllLines(fileManifesto, Encoding.UTF8)
            Dim voci As New List(Of String)()

            For Each riga As String In righe
                If Not String.IsNullOrWhiteSpace(riga) Then
                    voci.Add(riga.Trim())
                End If
            Next

            If voci.Count = 0 Then
                risultato.Successo = False
                risultato.Errori.Add("Nessuna versione precedente disponibile.")
                Return risultato
            End If

            Interlocked.Exchange(_fileTotali, voci.Count)
            Interlocked.Exchange(_fileCompletati, 0)
            _frazioni = Nothing
            _descrizioneCorrente = "Ripristino dei file"

            Dim ripristinati As Integer = 0
            Dim falliti As Integer = 0

            For Each riga As String In voci
                If tokenAnnullamento.IsCancellationRequested Then
                    risultato.Annullato = True
                    Exit For
                End If

                Try
                    Dim separatore As Integer = riga.IndexOf("|"c)

                    If separatore <= 0 Then
                        Interlocked.Increment(_fileCompletati)
                        Continue For
                    End If

                    Dim tipo As String = riga.Substring(0, separatore)
                    Dim relativo As String = riga.Substring(separatore + 1).Replace("/"c, Path.DirectorySeparatorChar)

                    If String.IsNullOrWhiteSpace(relativo) OrElse relativo.Contains("..") Then
                        Interlocked.Increment(_fileCompletati)
                        Continue For
                    End If

                    Dim percorsoLocale As String = Path.Combine(_cartellaBase, relativo)

                    If String.Equals(tipo, "N", StringComparison.OrdinalIgnoreCase) Then
                        Try
                            If File.Exists(percorsoLocale) Then
                                File.Delete(percorsoLocale)
                            End If
                            ripristinati += 1
                        Catch ex As Exception
                            falliti += 1
                            RegistroAggiornamento.Scrivi("EseguiRipristino: impossibile eliminare il file aggiunto " & percorsoLocale & ": " & ex.Message)

                            SyncLock _erroriBlocco
                                _errori.Add(relativo & ": " & ex.Message)
                            End SyncLock
                        End Try
                    Else
                        Dim percorsoBackup As String = Path.Combine(PercorsoCartellaBackup(_cartellaBase), relativo & ESTENSIONE_BACKUP)

                        If Not File.Exists(percorsoBackup) Then
                            falliti += 1

                            SyncLock _erroriBlocco
                                _errori.Add(relativo & ": copia di sicurezza mancante.")
                            End SyncLock
                        Else
                            Try
                                Dim cartellaFile As String = Path.GetDirectoryName(percorsoLocale)

                                If Not String.IsNullOrEmpty(cartellaFile) AndAlso Not Directory.Exists(cartellaFile) Then
                                    Directory.CreateDirectory(cartellaFile)
                                End If

                                Dim percorsoTemporaneo As String = percorsoLocale & ".wgtmp"

                                If File.Exists(percorsoTemporaneo) Then
                                    Try
                                        File.Delete(percorsoTemporaneo)
                                    Catch
                                    End Try
                                End If

                                File.Copy(percorsoBackup, percorsoTemporaneo, True)
                                RimuoviMarchioZona(percorsoTemporaneo)
                                SostituisciFile(percorsoTemporaneo, percorsoLocale)
                                RimuoviMarchioZona(percorsoLocale)

                                ripristinati += 1
                            Catch ex As Exception
                                falliti += 1
                                RegistroAggiornamento.Scrivi("EseguiRipristino: errore ripristino " & percorsoLocale & ": " & ex.Message)

                                SyncLock _erroriBlocco
                                    _errori.Add(relativo & ": " & ex.Message)
                                End SyncLock
                            End Try
                        End If
                    End If
                Catch ex As Exception
                    falliti += 1
                    RegistroAggiornamento.Scrivi("EseguiRipristino: errore su riga " & riga & ": " & ex.Message)
                Finally
                    Interlocked.Increment(_fileCompletati)
                End Try
            Next

            risultato.FileScaricati = ripristinati
            risultato.FileFalliti = falliti

            SyncLock _erroriBlocco
                risultato.Errori = New List(Of String)(_errori)
            End SyncLock

            risultato.Successo = Not risultato.Annullato AndAlso falliti = 0

        Catch ex As Exception
            RegistroAggiornamento.Scrivi("GestoreAggiornamento.EseguiRipristino: " & ex.Message & " " & ex.StackTrace)
            risultato.Successo = False
            risultato.Errori.Add(ex.Message)
        End Try

        Return risultato
    End Function

    Friend Sub Chiudi()
        Try
            If _httpClient IsNot Nothing Then
                _httpClient.Dispose()
            End If
        Catch ex As Exception
            RegistroAggiornamento.Scrivi("GestoreAggiornamento.Chiudi: " & ex.Message)
        End Try
    End Sub

End Class

Friend Class FinestraAvanzamentoAggiornamento
    Inherits Form

    Private ReadOnly _sintesi As SintesiVocaleAggiornamento
    Private ReadOnly _gestore As GestoreAggiornamento
    Private ReadOnly _sorgenteAnnullamento As CancellationTokenSource
    Private ReadOnly _modalitaRipristino As Boolean

    Private WithEvents _timerAvanzamento As System.Windows.Forms.Timer
    Private WithEvents _timerAnnuncio As System.Windows.Forms.Timer
    Private _hookTastiera As HookTastieraAggiornamento
    Private _barra As BarraAvanzamentoGdi
    Private _threadAggiornamento As Thread
    Private _risultato As RisultatoAggiornamento
    Private _terminato As Boolean = False
    Private _ultimaPercentualeAnnunciata As Integer = -1
    Private _chiusuraConfermata As Boolean = False

    Friend Sub New(ByVal sintesi As SintesiVocaleAggiornamento, ByVal gestore As GestoreAggiornamento, ByVal modalitaRipristino As Boolean)
        Try
            _sintesi = sintesi
            _gestore = gestore
            _modalitaRipristino = modalitaRipristino
            _sorgenteAnnullamento = New CancellationTokenSource()

            If _modalitaRipristino Then
                Text = "Ripristino della versione precedente in corso"
            Else
                Text = "Aggiornamento del lettore di schermo in corso"
            End If

            StartPosition = FormStartPosition.CenterScreen
            FormBorderStyle = FormBorderStyle.FixedDialog
            MaximizeBox = False
            MinimizeBox = False
            ClientSize = New Size(640, 200)
            BackColor = Color.Black
            KeyPreview = True

            _barra = New BarraAvanzamentoGdi()
            _barra.Dock = DockStyle.Fill
            _barra.Testo = NomeOperazione() & " in corso, 0 per cento"
            _barra.AccessibleName = "Avanzamento " & NomeOperazione()
            _barra.AccessibleRole = AccessibleRole.ProgressBar
            Controls.Add(_barra)

            _timerAvanzamento = New System.Windows.Forms.Timer()
            _timerAvanzamento.Interval = 250

            _timerAnnuncio = New System.Windows.Forms.Timer()
            _timerAnnuncio.Interval = 10000
        Catch ex As Exception
            RegistroAggiornamento.Scrivi("FinestraAvanzamentoAggiornamento.New: " & ex.Message)
        End Try
    End Sub

    Private Function NomeOperazione() As String
        If _modalitaRipristino Then
            Return "Ripristino"
        End If
        Return "Aggiornamento"
    End Function

    Private Sub AlCaricamento(ByVal sender As Object, ByVal e As EventArgs) Handles MyBase.Load
        Try
            _timerAvanzamento.Start()
            _timerAnnuncio.Start()

            Try
                _hookTastiera = New HookTastieraAggiornamento()
                AddHandler _hookTastiera.TastoPremuto, AddressOf AlTastoDaHook
                _hookTastiera.Installa()
            Catch exHook As Exception
                RegistroAggiornamento.Scrivi("FinestraAvanzamentoAggiornamento.AlCaricamento (hook): " & exHook.Message)
            End Try

            Try
                SuonoAggiornamento.Avvia()
            Catch exSuono As Exception
                RegistroAggiornamento.Scrivi("FinestraAvanzamentoAggiornamento.AlCaricamento (suono): " & exSuono.Message)
            End Try

            _threadAggiornamento = New Thread(New ThreadStart(AddressOf EseguiAggiornamento))
            _threadAggiornamento.IsBackground = True
            _threadAggiornamento.Name = "WgAggiornamento"
            _threadAggiornamento.Start()

            Parla(NomeOperazione() & " del lettore di schermo in corso. Premere freccia su o freccia giu per conoscere la percentuale.", True)
        Catch ex As Exception
            RegistroAggiornamento.Scrivi("FinestraAvanzamentoAggiornamento.AlCaricamento: " & ex.Message)
        End Try
    End Sub

    Private Sub EseguiAggiornamento()
        Dim risultato As RisultatoAggiornamento = Nothing

        Try
            If _modalitaRipristino Then
                risultato = _gestore.EseguiRipristino(_sorgenteAnnullamento.Token)
            Else
                risultato = _gestore.Esegui(_sorgenteAnnullamento.Token)
            End If
        Catch ex As Exception
            RegistroAggiornamento.Scrivi("FinestraAvanzamentoAggiornamento.EseguiAggiornamento: " & ex.Message & " " & ex.StackTrace)
            risultato = New RisultatoAggiornamento()
            risultato.Successo = False
            risultato.Errori.Add(ex.Message)
        End Try

        Try
            _risultato = risultato

            If IsHandleCreated AndAlso Not IsDisposed Then
                BeginInvoke(New MethodInvoker(AddressOf AlTermineAggiornamento))
            End If
        Catch ex As Exception
            RegistroAggiornamento.Scrivi("FinestraAvanzamentoAggiornamento: errore notifica termine: " & ex.Message)
        End Try
    End Sub

    Private Sub AlTermineAggiornamento()
        Try
            _terminato = True

            Try
                SuonoAggiornamento.Ferma()
            Catch exSuono As Exception
                RegistroAggiornamento.Scrivi("FinestraAvanzamentoAggiornamento.AlTermineAggiornamento (suono): " & exSuono.Message)
            End Try

            If _timerAvanzamento IsNot Nothing Then
                _timerAvanzamento.Stop()
            End If

            If _timerAnnuncio IsNot Nothing Then
                _timerAnnuncio.Stop()
            End If

            Dim messaggio As String

            If _risultato Is Nothing Then
                messaggio = NomeOperazione() & " terminato con esito sconosciuto."
            ElseIf _risultato.Annullato Then
                messaggio = NomeOperazione() & " annullato."
            ElseIf _risultato.Successo Then
                _barra.Valore = 100

                If _modalitaRipristino Then
                    messaggio = "Ripristino completato. File ripristinati: " & _risultato.FileScaricati.ToString(CultureInfo.InvariantCulture) & "."
                ElseIf _risultato.FileScaricati > 0 Then
                    messaggio = "Aggiornamento completato. File aggiornati: " & _risultato.FileScaricati.ToString(CultureInfo.InvariantCulture) & "."
                Else
                    messaggio = "Nessun aggiornamento necessario, i file sono gia' aggiornati."
                End If
            Else
                messaggio = NomeOperazione() & " terminato con " & _risultato.FileFalliti.ToString(CultureInfo.InvariantCulture) & " errori. Il dettaglio si trova nel file wg_updater_log.txt."
            End If

            _barra.Testo = messaggio
            Text = NomeOperazione() & " terminato"

            Parla(messaggio & " Premere invio per riaprire il lettore di schermo.", True)
        Catch ex As Exception
            RegistroAggiornamento.Scrivi("FinestraAvanzamentoAggiornamento.AlTermineAggiornamento: " & ex.Message)
        End Try
    End Sub

    Private Sub AlTickAvanzamento(ByVal sender As Object, ByVal e As EventArgs) Handles _timerAvanzamento.Tick
        Try
            If _terminato Then
                Return
            End If

            Dim percentuale As Integer = _gestore.PercentualeCorrente
            _barra.Valore = percentuale
            _barra.Testo = NomeOperazione() & " in corso, " & percentuale.ToString(CultureInfo.InvariantCulture) & " per cento"
        Catch ex As Exception
            RegistroAggiornamento.Scrivi("FinestraAvanzamentoAggiornamento.AlTickAvanzamento: " & ex.Message)
        End Try
    End Sub

    Private Sub AlTastoDaHook(ByVal sender As Object, ByVal e As TastoAggiornamentoEventArgs)
        Try
            Dim tasto As Keys = e.Tasto And Keys.KeyCode

            Select Case tasto
                Case Keys.Up, Keys.Down
                    e.Bloccato = True
                    EseguiSuInterfaccia(New MethodInvoker(AddressOf AnnunciaPercentuale))

                Case Keys.Enter
                    e.Bloccato = True

                    If _terminato Then
                        EseguiSuInterfaccia(New MethodInvoker(AddressOf ChiudiEAvviaApplicazione))
                    Else
                        EseguiSuInterfaccia(New MethodInvoker(AddressOf AnnunciaPercentuale))
                    End If

                Case Keys.Escape
                    e.Bloccato = True

                    If _terminato Then
                        EseguiSuInterfaccia(New MethodInvoker(AddressOf ChiudiEAvviaApplicazione))
                    Else
                        EseguiSuInterfaccia(New MethodInvoker(AddressOf AnnullaAggiornamento))
                    End If
            End Select
        Catch ex As Exception
            RegistroAggiornamento.Scrivi("FinestraAvanzamentoAggiornamento.AlTastoDaHook: " & ex.Message)
        End Try
    End Sub

    Private Sub EseguiSuInterfaccia(ByVal azione As MethodInvoker)
        Try
            If azione Is Nothing Then
                Return
            End If

            If IsDisposed OrElse Not IsHandleCreated Then
                Return
            End If

            If InvokeRequired Then
                BeginInvoke(azione)
            Else
                azione.Invoke()
            End If
        Catch ex As Exception
            RegistroAggiornamento.Scrivi("FinestraAvanzamentoAggiornamento.EseguiSuInterfaccia: " & ex.Message)
        End Try
    End Sub

    Private Sub AlTickAnnuncio(ByVal sender As Object, ByVal e As EventArgs) Handles _timerAnnuncio.Tick
        Try
            If _terminato Then
                If _timerAnnuncio IsNot Nothing Then
                    _timerAnnuncio.Stop()
                End If
                Return
            End If

            AnnunciaPercentuale()
        Catch ex As Exception
            RegistroAggiornamento.Scrivi("FinestraAvanzamentoAggiornamento.AlTickAnnuncio: " & ex.Message)
        End Try
    End Sub

    Protected Overrides Sub OnKeyDown(ByVal e As KeyEventArgs)
        Try
            Select Case e.KeyCode
                Case Keys.Up, Keys.Down
                    AnnunciaPercentuale()
                    e.Handled = True
                    e.SuppressKeyPress = True

                Case Keys.Enter
                    If _terminato Then
                        e.Handled = True
                        e.SuppressKeyPress = True
                        ChiudiEAvviaApplicazione()
                    Else
                        AnnunciaPercentuale()
                        e.Handled = True
                        e.SuppressKeyPress = True
                    End If

                Case Keys.Escape
                    e.Handled = True
                    e.SuppressKeyPress = True

                    If _terminato Then
                        ChiudiEAvviaApplicazione()
                    Else
                        AnnullaAggiornamento()
                    End If
            End Select
        Catch ex As Exception
            RegistroAggiornamento.Scrivi("FinestraAvanzamentoAggiornamento.OnKeyDown: " & ex.Message)
        End Try

        MyBase.OnKeyDown(e)
    End Sub

    Private Sub AnnunciaPercentuale()
        Try
            Dim percentuale As Integer

            If _terminato Then
                percentuale = 100
            Else
                percentuale = _gestore.PercentualeCorrente
            End If

            _ultimaPercentualeAnnunciata = percentuale
            Parla(percentuale.ToString(CultureInfo.InvariantCulture) & " per cento", True)
        Catch ex As Exception
            RegistroAggiornamento.Scrivi("FinestraAvanzamentoAggiornamento.AnnunciaPercentuale: " & ex.Message)
        End Try
    End Sub

    Private Sub AnnullaAggiornamento()
        Try
            Parla("Annullamento in corso, attendere.", True)

            If _sorgenteAnnullamento IsNot Nothing Then
                _sorgenteAnnullamento.Cancel()
            End If
        Catch ex As Exception
            RegistroAggiornamento.Scrivi("FinestraAvanzamentoAggiornamento.AnnullaAggiornamento: " & ex.Message)
        End Try
    End Sub

    Private Sub ChiudiEAvviaApplicazione()
        Try
            If _chiusuraConfermata Then
                Return
            End If

            _chiusuraConfermata = True

            If _sintesi IsNot Nothing Then
                _sintesi.Silenzia()
            End If

            AvviaApplicazionePrincipale()

            Close()
        Catch ex As Exception
            RegistroAggiornamento.Scrivi("FinestraAvanzamentoAggiornamento.ChiudiEAvviaApplicazione: " & ex.Message)
        End Try
    End Sub

    Friend Shared Sub AvviaApplicazionePrincipale()
        Try
            Dim percorso As String = Path.Combine(Application.StartupPath, "WGLettore.exe")

            If Not File.Exists(percorso) Then
                Return
            End If

            RimuoviMarchioZonaEseguibile(percorso)

            Dim informazioni As New ProcessStartInfo()
            informazioni.FileName = percorso
            informazioni.WorkingDirectory = Application.StartupPath
            informazioni.UseShellExecute = False

            Process.Start(informazioni)
        Catch ex As Exception
            RegistroAggiornamento.Scrivi("AvviaApplicazionePrincipale: " & ex.Message)
        End Try
    End Sub

    Private Shared Sub RimuoviMarchioZonaEseguibile(ByVal percorso As String)
        Try
            GestoreAggiornamento.RimuoviMarchioZona(percorso)
        Catch ex As Exception
            RegistroAggiornamento.Scrivi("RimuoviMarchioZonaEseguibile: " & ex.Message)
        End Try
    End Sub

    Private Sub Parla(ByVal testo As String, ByVal interrompi As Boolean)
        Try
            If _sintesi IsNot Nothing Then
                _sintesi.Parla(testo, interrompi)
            End If
        Catch ex As Exception
            RegistroAggiornamento.Scrivi("FinestraAvanzamentoAggiornamento.Parla: " & ex.Message)
        End Try
    End Sub

    Protected Overrides Sub OnFormClosing(ByVal e As FormClosingEventArgs)
        Try
            If Not _terminato AndAlso Not _chiusuraConfermata Then
                If _sorgenteAnnullamento IsNot Nothing Then
                    _sorgenteAnnullamento.Cancel()
                End If

                If _threadAggiornamento IsNot Nothing AndAlso _threadAggiornamento.IsAlive Then
                    Try
                        _threadAggiornamento.Join(8000)
                    Catch
                    End Try
                End If
            End If

            If _timerAvanzamento IsNot Nothing Then
                _timerAvanzamento.Stop()
            End If

            If _timerAnnuncio IsNot Nothing Then
                _timerAnnuncio.Stop()
            End If

            If _hookTastiera IsNot Nothing Then
                _hookTastiera.Rimuovi()
            End If

            Try
                SuonoAggiornamento.Ferma()
            Catch exSuono As Exception
                RegistroAggiornamento.Scrivi("FinestraAvanzamentoAggiornamento.OnFormClosing (suono): " & exSuono.Message)
            End Try
        Catch ex As Exception
            RegistroAggiornamento.Scrivi("FinestraAvanzamentoAggiornamento.OnFormClosing: " & ex.Message)
        End Try

        MyBase.OnFormClosing(e)
    End Sub

    Protected Overrides Sub Dispose(ByVal eliminazione As Boolean)
        Try
            If eliminazione Then
                If _timerAvanzamento IsNot Nothing Then
                    _timerAvanzamento.Dispose()
                    _timerAvanzamento = Nothing
                End If

                If _timerAnnuncio IsNot Nothing Then
                    _timerAnnuncio.Dispose()
                    _timerAnnuncio = Nothing
                End If

                If _hookTastiera IsNot Nothing Then
                    _hookTastiera.Dispose()
                    _hookTastiera = Nothing
                End If

                If _sorgenteAnnullamento IsNot Nothing Then
                    _sorgenteAnnullamento.Dispose()
                End If

                If _gestore IsNot Nothing Then
                    _gestore.Chiudi()
                End If
            End If
        Catch ex As Exception
            RegistroAggiornamento.Scrivi("FinestraAvanzamentoAggiornamento.Dispose: " & ex.Message)
        End Try

        MyBase.Dispose(eliminazione)
    End Sub

End Class

Public Class UpdateForm

    Private _sintesi As SintesiVocaleAggiornamento
    Private _configurazione As ConfigurazioneAggiornamento
    Private _menu As MenuGdi
    Private _hookTastiera As HookTastieraAggiornamento
    Private _avviato As Boolean = False
    Private _modalitaRipristino As Boolean = False

    Private Shared _risoluzioneRegistrata As Boolean = False

    Private Const VOCE_INSTALLA As String = "Installa"
    Private Const VOCE_ANNULLA As String = "Annulla"
    Private Const VOCE_RIPRISTINA As String = "Ripristina"

    Friend Shared Function RipristinoRichiesto() As Boolean
        Try
            Dim argomenti() As String = Environment.GetCommandLineArgs()

            If argomenti Is Nothing OrElse argomenti.Length < 2 Then
                Return False
            End If

            For indice As Integer = 1 To argomenti.Length - 1
                Dim argomento As String = argomenti(indice)

                If String.IsNullOrWhiteSpace(argomento) Then
                    Continue For
                End If

                Dim pulito As String = argomento.Trim().TrimStart("/"c, "-"c)

                If String.Equals(pulito, "ripristina", StringComparison.OrdinalIgnoreCase) OrElse
                   String.Equals(pulito, "ripristino", StringComparison.OrdinalIgnoreCase) OrElse
                   String.Equals(pulito, "restore", StringComparison.OrdinalIgnoreCase) OrElse
                   String.Equals(pulito, "rollback", StringComparison.OrdinalIgnoreCase) Then
                    Return True
                End If
            Next

            Return False
        Catch ex As Exception
            RegistroAggiornamento.Scrivi("UpdateForm.RipristinoRichiesto: " & ex.Message)
            Return False
        End Try
    End Function

    Private Shared Sub RegistraRisoluzioneAssembly()
        Try
            If _risoluzioneRegistrata Then
                Return
            End If

            _risoluzioneRegistrata = True
            AddHandler AppDomain.CurrentDomain.AssemblyResolve, AddressOf RisolviAssembly
        Catch ex As Exception
            RegistroAggiornamento.Scrivi("UpdateForm.RegistraRisoluzioneAssembly: " & ex.Message)
        End Try
    End Sub

    Private Shared Function RisolviAssembly(ByVal sender As Object, ByVal argomenti As ResolveEventArgs) As Reflection.Assembly
        Try
            Dim nomeRichiesto As String = argomenti.Name

            If nomeRichiesto.Contains(",") Then
                nomeRichiesto = nomeRichiesto.Substring(0, nomeRichiesto.IndexOf(","c))
            End If

            nomeRichiesto = nomeRichiesto.Trim()

            If String.IsNullOrWhiteSpace(nomeRichiesto) Then
                Return Nothing
            End If

            If nomeRichiesto.EndsWith(".XmlSerializers", StringComparison.OrdinalIgnoreCase) Then
                Return Nothing
            End If

            If nomeRichiesto.EndsWith(".resources", StringComparison.OrdinalIgnoreCase) Then
                Return Nothing
            End If

            Dim percorso As String = Path.Combine(Application.StartupPath, nomeRichiesto & ".dll")

            If File.Exists(percorso) Then
                Return Reflection.Assembly.LoadFrom(percorso)
            End If

            Return Nothing
        Catch ex As Exception
            RegistroAggiornamento.Scrivi("UpdateForm.RisolviAssembly: " & ex.Message)
            Return Nothing
        End Try
    End Function

    Private Sub UpdateForm_Load(ByVal sender As Object, ByVal e As EventArgs) Handles MyBase.Load
        Try
            RegistraRisoluzioneAssembly()

            _modalitaRipristino = RipristinoRichiesto()

            If _modalitaRipristino Then
                Text = "Ripristino versione precedente del lettore di schermo"
            Else
                Text = "Aggiornamento del lettore di schermo"
            End If

            StartPosition = FormStartPosition.CenterScreen
            FormBorderStyle = FormBorderStyle.FixedDialog
            MaximizeBox = False
            MinimizeBox = False
            ClientSize = New Size(640, 200)
            BackColor = Color.Black
            KeyPreview = False

            Try
                LettoriSchermo.Disattiva()
            Catch exLettori As Exception
                RegistroAggiornamento.Scrivi("UpdateForm.UpdateForm_Load (lettori schermo): " & exLettori.Message)
            End Try

            _configurazione = New ConfigurazioneAggiornamento()
            _sintesi = New SintesiVocaleAggiornamento(_configurazione)

            _menu = New MenuGdi()
            _menu.Dock = DockStyle.Fill
            _menu.AccessibleName = "Menu aggiornamento"
            _menu.AccessibleRole = AccessibleRole.List

            If _modalitaRipristino Then
                _menu.Titolo = "Si vuole ripristinare la versione precedente del lettore di schermo?"
                _menu.AggiungiVoce(VOCE_RIPRISTINA)
                _menu.AggiungiVoce(VOCE_ANNULLA)
            Else
                _menu.Titolo = "Si vuole aggiornare il lettore di schermo?"
                _menu.AggiungiVoce(VOCE_INSTALLA)
                _menu.AggiungiVoce(VOCE_ANNULLA)
            End If

            AddHandler _menu.SelezioneCambiata, AddressOf AlCambioSelezione
            AddHandler _menu.VoceAttivata, AddressOf AlAttivazioneVoce
            AddHandler _menu.UscitaRichiesta, AddressOf AlUscitaRichiesta

            Controls.Add(_menu)
            _menu.Select()

            Try
                _hookTastiera = New HookTastieraAggiornamento()
                AddHandler _hookTastiera.TastoPremuto, AddressOf AlTastoDaHook
                _hookTastiera.Installa()
            Catch exHook As Exception
                RegistroAggiornamento.Scrivi("UpdateForm.UpdateForm_Load (hook): " & exHook.Message)
            End Try

            AnnunciaAperturaMenu()
        Catch ex As Exception
            RegistroAggiornamento.Scrivi("UpdateForm.UpdateForm_Load: " & ex.Message & " " & ex.StackTrace)
        End Try
    End Sub

    Private Sub AnnunciaAperturaMenu()
        Try
            If _modalitaRipristino Then
                Parla("Inizializzazione ripristino del lettore di schermo.", True)

                If Not GestoreAggiornamento.EsisteVersionePrecedente(Application.StartupPath) Then
                    _menu.Titolo = "Nessuna versione precedente disponibile"
                    Parla("Nessuna versione precedente disponibile. Premere invio per riaprire il lettore di schermo.", False)
                    Return
                End If

                Dim descrizione As String = GestoreAggiornamento.DescrizioneVersionePrecedente(Application.StartupPath)

                If Not String.IsNullOrWhiteSpace(descrizione) Then
                    Parla(descrizione & ".", False)
                End If

                Parla("Si vuole ripristinare la versione precedente del lettore di schermo?", False)
            Else
                Parla("Inizializzazione aggiornamento del lettore di schermo.", True)
                Parla("Si vuole aggiornare il lettore di schermo?", False)
            End If

            Parla(_menu.TestoSelezionato, False)
        Catch ex As Exception
            RegistroAggiornamento.Scrivi("UpdateForm.AnnunciaAperturaMenu: " & ex.Message)
        End Try
    End Sub

    Private Sub AlTastoDaHook(ByVal sender As Object, ByVal e As TastoAggiornamentoEventArgs)
        Try
            If _avviato Then
                Return
            End If

            Dim tasto As Keys = e.Tasto And Keys.KeyCode

            Select Case tasto
                Case Keys.Up, Keys.Left
                    e.Bloccato = True
                    EseguiSuInterfaccia(New MethodInvoker(AddressOf SelezionaVocePrecedente))

                Case Keys.Down, Keys.Right
                    e.Bloccato = True
                    EseguiSuInterfaccia(New MethodInvoker(AddressOf SelezionaVoceSuccessiva))

                Case Keys.Enter, Keys.Space
                    e.Bloccato = True
                    EseguiSuInterfaccia(New MethodInvoker(AddressOf AttivaVoceCorrente))

                Case Keys.Escape
                    e.Bloccato = True
                    EseguiSuInterfaccia(New MethodInvoker(AddressOf AnnullaEChiudi))
            End Select
        Catch ex As Exception
            RegistroAggiornamento.Scrivi("UpdateForm.AlTastoDaHook: " & ex.Message)
        End Try
    End Sub

    Private Sub EseguiSuInterfaccia(ByVal azione As MethodInvoker)
        Try
            If azione Is Nothing Then
                Return
            End If

            If IsDisposed OrElse Not IsHandleCreated Then
                Return
            End If

            If InvokeRequired Then
                BeginInvoke(azione)
            Else
                azione.Invoke()
            End If
        Catch ex As Exception
            RegistroAggiornamento.Scrivi("UpdateForm.EseguiSuInterfaccia: " & ex.Message)
        End Try
    End Sub

    Private Sub SelezionaVocePrecedente()
        Try
            If _menu Is Nothing OrElse _menu.NumeroVoci = 0 Then
                Return
            End If

            If _menu.IndiceSelezionato = 0 Then
                _menu.IndiceSelezionato = _menu.NumeroVoci - 1
            Else
                _menu.IndiceSelezionato = _menu.IndiceSelezionato - 1
            End If
        Catch ex As Exception
            RegistroAggiornamento.Scrivi("UpdateForm.SelezionaVocePrecedente: " & ex.Message)
        End Try
    End Sub

    Private Sub SelezionaVoceSuccessiva()
        Try
            If _menu Is Nothing OrElse _menu.NumeroVoci = 0 Then
                Return
            End If

            If _menu.IndiceSelezionato >= _menu.NumeroVoci - 1 Then
                _menu.IndiceSelezionato = 0
            Else
                _menu.IndiceSelezionato = _menu.IndiceSelezionato + 1
            End If
        Catch ex As Exception
            RegistroAggiornamento.Scrivi("UpdateForm.SelezionaVoceSuccessiva: " & ex.Message)
        End Try
    End Sub

    Private Sub AttivaVoceCorrente()
        Try
            AlAttivazioneVoce(Me, EventArgs.Empty)
        Catch ex As Exception
            RegistroAggiornamento.Scrivi("UpdateForm.AttivaVoceCorrente: " & ex.Message)
        End Try
    End Sub

    Private Sub AlCambioSelezione(ByVal sender As Object, ByVal e As EventArgs)
        Try
            Parla(_menu.TestoSelezionato, True)
        Catch ex As Exception
            RegistroAggiornamento.Scrivi("UpdateForm.AlCambioSelezione: " & ex.Message)
        End Try
    End Sub

    Private Sub AlUscitaRichiesta(ByVal sender As Object, ByVal e As EventArgs)
        Try
            AnnullaEChiudi()
        Catch ex As Exception
            RegistroAggiornamento.Scrivi("UpdateForm.AlUscitaRichiesta: " & ex.Message)
        End Try
    End Sub

    Private Sub AlAttivazioneVoce(ByVal sender As Object, ByVal e As EventArgs)
        Try
            If _avviato Then
                Return
            End If

            If _modalitaRipristino AndAlso Not GestoreAggiornamento.EsisteVersionePrecedente(Application.StartupPath) Then
                AnnullaEChiudi()
                Return
            End If

            Dim scelta As String = _menu.TestoSelezionato

            If String.Equals(scelta, VOCE_ANNULLA, StringComparison.OrdinalIgnoreCase) Then
                AnnullaEChiudi()
                Return
            End If

            If _modalitaRipristino Then
                If Not String.Equals(scelta, VOCE_RIPRISTINA, StringComparison.OrdinalIgnoreCase) Then
                    Return
                End If
            Else
                If Not String.Equals(scelta, VOCE_INSTALLA, StringComparison.OrdinalIgnoreCase) Then
                    Return
                End If
            End If

            _avviato = True

            If _modalitaRipristino Then
                Parla("Avvio del ripristino.", True)
            Else
                Parla("Avvio dell'aggiornamento.", True)
            End If

            If Not ChiudiApplicazionePrincipale() Then
                _avviato = False
                Parla("Impossibile chiudere il lettore di schermo. Chiudere manualmente il programma e riprovare.", True)
                Return
            End If

            ApriFinestraAvanzamento()
        Catch ex As Exception
            RegistroAggiornamento.Scrivi("UpdateForm.AlAttivazioneVoce: " & ex.Message & " " & ex.StackTrace)
            _avviato = False
            Parla("Si e' verificato un errore all'avvio dell'operazione.", True)
        End Try
    End Sub

    Private Sub ApriFinestraAvanzamento()
        Try
            Try
                If _hookTastiera IsNot Nothing Then
                    _hookTastiera.Rimuovi()
                End If
            Catch exHook As Exception
                RegistroAggiornamento.Scrivi("UpdateForm.ApriFinestraAvanzamento (hook): " & exHook.Message)
            End Try

            Dim gestore As New GestoreAggiornamento(Application.StartupPath)
            Dim finestra As New FinestraAvanzamentoAggiornamento(_sintesi, gestore, _modalitaRipristino)

            Hide()

            finestra.ShowDialog()

            Close()
        Catch ex As Exception
            RegistroAggiornamento.Scrivi("UpdateForm.ApriFinestraAvanzamento: " & ex.Message & " " & ex.StackTrace)
            Close()
        End Try
    End Sub

    Private Function ChiudiApplicazionePrincipale() As Boolean
        Try
            For Each voce As Process In Process.GetProcessesByName("WGLettoreVoce")
                Try
                    voce.Kill()
                    voce.WaitForExit(3000)
                Catch
                End Try
            Next
            Dim processi() As Process = Process.GetProcessesByName("wglettore")

            If processi Is Nothing OrElse processi.Length = 0 Then
                Return True
            End If

            For Each processo As Process In processi
                Try
                    If processo.HasExited Then
                        Continue For
                    End If

                    Try
                        processo.CloseMainWindow()
                    Catch
                    End Try

                    If Not processo.WaitForExit(4000) Then
                        processo.Kill()
                        processo.WaitForExit(8000)
                    End If
                Catch ex As Exception
                    RegistroAggiornamento.Scrivi("ChiudiApplicazionePrincipale: errore su processo: " & ex.Message)
                Finally
                    Try
                        processo.Dispose()
                    Catch
                    End Try
                End Try
            Next

            Try
                Thread.Sleep(600)
            Catch
            End Try

            Dim rimasti() As Process = Process.GetProcessesByName("wglettore")

            If rimasti IsNot Nothing AndAlso rimasti.Length > 0 Then
                For Each processo As Process In rimasti
                    Try
                        processo.Dispose()
                    Catch
                    End Try
                Next

                Return False
            End If

            Return True
        Catch ex As Exception
            RegistroAggiornamento.Scrivi("ChiudiApplicazionePrincipale: " & ex.Message)
            Return False
        End Try
    End Function

    Private Sub AnnullaEChiudi()
        Try
            If _modalitaRipristino Then
                Parla("Ripristino annullato.", True)
            Else
                Parla("Aggiornamento annullato.", True)
            End If

            FinestraAvanzamentoAggiornamento.AvviaApplicazionePrincipale()

            Close()
        Catch ex As Exception
            RegistroAggiornamento.Scrivi("UpdateForm.AnnullaEChiudi: " & ex.Message)
            Close()
        End Try
    End Sub

    Private Sub Parla(ByVal testo As String, ByVal interrompi As Boolean)
        Try
            If _sintesi IsNot Nothing Then
                _sintesi.Parla(testo, interrompi)
            End If
        Catch ex As Exception
            RegistroAggiornamento.Scrivi("UpdateForm.Parla: " & ex.Message)
        End Try
    End Sub

    Private Sub UpdateForm_FormClosed(ByVal sender As Object, ByVal e As FormClosedEventArgs) Handles MyBase.FormClosed
        Try
            If _hookTastiera IsNot Nothing Then
                _hookTastiera.Dispose()
                _hookTastiera = Nothing
            End If
        Catch ex As Exception
            RegistroAggiornamento.Scrivi("UpdateForm.UpdateForm_FormClosed (hook): " & ex.Message)
        End Try

        Try
            If _sintesi IsNot Nothing Then
                _sintesi.Dispose()
                _sintesi = Nothing
            End If
        Catch ex As Exception
            RegistroAggiornamento.Scrivi("UpdateForm.UpdateForm_FormClosed: " & ex.Message)
        End Try

        Try
            Application.Exit()
        Catch
        End Try
    End Sub

End Class