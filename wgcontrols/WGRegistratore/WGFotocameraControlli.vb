' ============================================================================================
' WGFotocameraControlli - i controlli della videocamera e i formati che offre
' ============================================================================================
'
' Ogni videocamera dichiara a Windows i suoi controlli (DirectShow: IAMCameraControl e IAMVideoProcAmp): luminosità, contrasto,
' esposizione, messa a fuoco, zoom, bilanciamento del bianco e così via. Qui si leggono quelli che la videocamera e il suo driver
' espongono davvero, con i loro limiti, e si impostano. I valori scelti restano nella configurazione (cam_p_<sigla>, cam_a_<sigla>) e
' si rimettono a ogni apertura della fotocamera.
'
' I formati (risoluzioni e fotogrammi al secondo) si chiedono a ffmpeg, che li legge dal driver.
' ============================================================================================
Imports System
Imports System.Collections.Generic
Imports System.Globalization
Imports System.Linq
Imports System.Runtime.InteropServices
Imports System.Runtime.InteropServices.ComTypes
Imports System.Text.RegularExpressions

''' <summary>Un controllo della videocamera, con i suoi limiti e il valore di adesso.</summary>
Public NotInheritable Class ProprietaCamera
    ''' <summary>La sigla con cui si salva: "C" per i controlli della camera, "V" per quelli dell'immagine, poi il codice.</summary>
    Public Id As String = ""
    Public Nome As String = ""
    Public Gruppo As Integer
    Public Codice As Integer
    Public Minimo As Integer
    Public Massimo As Integer
    Public Passo As Integer = 1
    Public Predefinito As Integer
    Public Corrente As Integer
    Public AutomaticoPossibile As Boolean
    Public ManualePossibile As Boolean
    Public InAutomatico As Boolean

    Public Function Descrizione() As String
        Dim t As String = Nome & ": "
        If InAutomatico Then
            t &= "automatico"
        Else
            t &= Corrente.ToString()
        End If
        Return t
    End Function
End Class

''' <summary>Un formato che la videocamera sa dare.</summary>
Public NotInheritable Class FormatoCamera
    Public Larghezza As Integer
    Public Altezza As Integer
    Public FpsMassimi As Double
    ''' <summary>"mjpeg" oppure vuoto.</summary>
    Public Codec As String = ""
    Public FormatoPixel As String = ""

    Public Function Testo() As String
        Return Larghezza.ToString() & " per " & Altezza.ToString()
    End Function
End Class

Public NotInheritable Class ControlliFotocamera

    <ComImport(), Guid("29840822-5B84-11D0-BD3B-00A0C911CE86"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)>
    Friend Interface ICreateDevEnum
        <PreserveSig()> Function CreateClassEnumerator(ByRef clsidDeviceClass As Guid, ByRef ppEnumMoniker As IEnumMoniker, dwFlags As Integer) As Integer
    End Interface

    <ComImport(), Guid("55272A00-42CB-11CE-8135-00AA004BB851"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)>
    Friend Interface IPropertyBag
        <PreserveSig()> Function Read(<MarshalAs(UnmanagedType.LPWStr)> pszPropName As String, ByRef pVar As Object, pErrorLog As IntPtr) As Integer
    End Interface

    <ComImport(), Guid("C6E13370-30AC-11D0-A18C-00A0C9118956"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)>
    Friend Interface IAMCameraControl
        <PreserveSig()> Function GetRange(prop As Integer, ByRef pMin As Integer, ByRef pMax As Integer, ByRef pPasso As Integer, ByRef pPredefinito As Integer, ByRef pFlag As Integer) As Integer
        <PreserveSig()> Function SetValore(prop As Integer, valore As Integer, flags As Integer) As Integer
        <PreserveSig()> Function GetValore(prop As Integer, ByRef valore As Integer, ByRef flags As Integer) As Integer
    End Interface

    <ComImport(), Guid("C6E13360-30AC-11D0-A18C-00A0C9118956"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)>
    Friend Interface IAMVideoProcAmp
        <PreserveSig()> Function GetRange(prop As Integer, ByRef pMin As Integer, ByRef pMax As Integer, ByRef pPasso As Integer, ByRef pPredefinito As Integer, ByRef pFlag As Integer) As Integer
        <PreserveSig()> Function SetValore(prop As Integer, valore As Integer, flags As Integer) As Integer
        <PreserveSig()> Function GetValore(prop As Integer, ByRef valore As Integer, ByRef flags As Integer) As Integer
    End Interface

    Private Shared ReadOnly _log As New Logger()
    Private Shared ReadOnly ClsidSystemDeviceEnum As New Guid("62BE5D10-60EB-11D0-BD3B-00A0C911CE86")
    Private Shared ReadOnly ClsidVideoInputDeviceCategory As New Guid("860BB310-5D01-11D0-BD3B-00A0C911CE86")
    Private Shared ReadOnly IidPropertyBag As New Guid("55272A00-42CB-11CE-8135-00AA004BB851")
    Private Shared ReadOnly IidBaseFilter As New Guid("56A86895-0AD4-11CE-B03A-0020AF0BA770")

    Private Const FlagAutomatico As Integer = 1
    Private Const FlagManuale As Integer = 2

    Private Shared ReadOnly NomiCamera() As String = {"Panoramica", "Inclinazione", "Rotazione", "Zoom", "Esposizione", "Diaframma", "Messa a fuoco"}
    Private Shared ReadOnly NomiImmagine() As String = {"Luminosità", "Contrasto", "Tonalità", "Saturazione", "Nitidezza", "Gamma", "Colore attivo", "Bilanciamento del bianco", "Compensazione del controluce", "Guadagno"}

    Private Sub New()
    End Sub

    ''' <summary>Il filtro DirectShow della videocamera con quel nome (Nothing se non c'è). Chi lo chiede deve rilasciarlo.</summary>
    Private Shared Function TrovaFiltro(nomeCamera As String) As Object
        Dim enumeratore As ICreateDevEnum = Nothing
        Dim monikers As IEnumMoniker = Nothing
        Try
            enumeratore = CType(Activator.CreateInstance(Type.GetTypeFromCLSID(ClsidSystemDeviceEnum)), ICreateDevEnum)
            Dim categoria As Guid = ClsidVideoInputDeviceCategory
            If enumeratore.CreateClassEnumerator(categoria, monikers, 0) <> 0 OrElse monikers Is Nothing Then Return Nothing
            Dim m(0) As IMoniker
            While monikers.Next(1, m, IntPtr.Zero) = 0
                Dim moniker As IMoniker = m(0)
                Dim nome As String = ""
                Try
                    Dim sacco As Object = Nothing
                    Dim iid As Guid = IidPropertyBag
                    moniker.BindToStorage(Nothing, Nothing, iid, sacco)
                    If sacco IsNot Nothing Then
                        Dim valore As Object = Nothing
                        CType(sacco, IPropertyBag).Read("FriendlyName", valore, IntPtr.Zero)
                        nome = If(valore, "").ToString()
                        Marshal.ReleaseComObject(sacco)
                    End If
                Catch
                End Try
                If String.Equals(nome, nomeCamera, StringComparison.OrdinalIgnoreCase) Then
                    Dim filtro As Object = Nothing
                    Dim iidFiltro As Guid = IidBaseFilter
                    moniker.BindToObject(Nothing, Nothing, iidFiltro, filtro)
                    Marshal.ReleaseComObject(moniker)
                    Return filtro
                End If
                Marshal.ReleaseComObject(moniker)
            End While
        Catch ex As Exception
            _log.ScriviLog("ControlliFotocamera.TrovaFiltro: " & ex.Message)
        Finally
            Try
                If monikers IsNot Nothing Then Marshal.ReleaseComObject(monikers)
                If enumeratore IsNot Nothing Then Marshal.ReleaseComObject(enumeratore)
            Catch
            End Try
        End Try
        Return Nothing
    End Function

    ''' <summary>I controlli che la videocamera espone, con limiti e valori di adesso.</summary>
    Public Shared Function Elenco(nomeCamera As String) As List(Of ProprietaCamera)
        Dim risultato As New List(Of ProprietaCamera)()
        Dim filtro As Object = Nothing
        Try
            filtro = TrovaFiltro(nomeCamera)
            If filtro Is Nothing Then Return risultato
            Dim camera As IAMCameraControl = TryCast(filtro, IAMCameraControl)
            If camera IsNot Nothing Then
                For i As Integer = 0 To NomiCamera.Length - 1
                    Dim p As ProprietaCamera = Leggi(0, i, NomiCamera(i), Function(c As Integer, ByRef a As Integer, ByRef b As Integer, ByRef d As Integer, ByRef e As Integer, ByRef f As Integer) camera.GetRange(c, a, b, d, e, f),
                                                      Function(c As Integer, ByRef v As Integer, ByRef f As Integer) camera.GetValore(c, v, f))
                    If p IsNot Nothing Then risultato.Add(p)
                Next
            End If
            Dim immagine As IAMVideoProcAmp = TryCast(filtro, IAMVideoProcAmp)
            If immagine IsNot Nothing Then
                For i As Integer = 0 To NomiImmagine.Length - 1
                    Dim p As ProprietaCamera = Leggi(1, i, NomiImmagine(i), Function(c As Integer, ByRef a As Integer, ByRef b As Integer, ByRef d As Integer, ByRef e As Integer, ByRef f As Integer) immagine.GetRange(c, a, b, d, e, f),
                                                      Function(c As Integer, ByRef v As Integer, ByRef f As Integer) immagine.GetValore(c, v, f))
                    If p IsNot Nothing Then risultato.Add(p)
                Next
            End If
        Catch ex As Exception
            _log.ScriviLog("ControlliFotocamera.Elenco: " & ex.Message)
        Finally
            Try
                If filtro IsNot Nothing Then Marshal.ReleaseComObject(filtro)
            Catch
            End Try
        End Try
        Return risultato
    End Function

    Private Delegate Function LeggiIntervallo(codice As Integer, ByRef minimo As Integer, ByRef massimo As Integer, ByRef passo As Integer, ByRef predefinito As Integer, ByRef flag As Integer) As Integer
    Private Delegate Function LeggiValoreDelegato(codice As Integer, ByRef valore As Integer, ByRef flag As Integer) As Integer

    Private Shared Function Leggi(gruppo As Integer, codice As Integer, nome As String, intervallo As LeggiIntervallo, valore As LeggiValoreDelegato) As ProprietaCamera
        Dim p As New ProprietaCamera()
        Dim caps As Integer
        If intervallo(codice, p.Minimo, p.Massimo, p.Passo, p.Predefinito, caps) <> 0 Then Return Nothing
        If p.Massimo < p.Minimo Then Return Nothing
        If p.Passo < 1 Then p.Passo = 1
        Dim flag As Integer
        If valore(codice, p.Corrente, flag) <> 0 Then p.Corrente = p.Predefinito
        p.AutomaticoPossibile = (caps And FlagAutomatico) <> 0
        p.ManualePossibile = (caps And FlagManuale) <> 0 OrElse Not p.AutomaticoPossibile
        p.InAutomatico = (flag And FlagAutomatico) <> 0
        p.Gruppo = gruppo
        p.Codice = codice
        p.Id = If(gruppo = 0, "C", "V") & codice.ToString()
        p.Nome = nome
        ' un controllo che non può cambiare (minimo uguale al massimo) non serve a nessuno
        If p.Minimo = p.Massimo AndAlso Not p.AutomaticoPossibile Then Return Nothing
        Return p
    End Function

    ''' <summary>Imposta un controllo: in automatico oppure al valore indicato. Restituisce vero se la videocamera l'ha accettato.</summary>
    Public Shared Function Imposta(nomeCamera As String, p As ProprietaCamera, valore As Integer, automatico As Boolean) As Boolean
        Dim filtro As Object = Nothing
        Try
            filtro = TrovaFiltro(nomeCamera)
            If filtro Is Nothing Then Return False
            Dim flag As Integer = If(automatico, FlagAutomatico, FlagManuale)
            Dim v As Integer = Math.Max(p.Minimo, Math.Min(p.Massimo, valore))
            Dim esito As Integer = -1
            If p.Gruppo = 0 Then
                Dim c As IAMCameraControl = TryCast(filtro, IAMCameraControl)
                If c IsNot Nothing Then esito = c.SetValore(p.Codice, v, flag)
            Else
                Dim c As IAMVideoProcAmp = TryCast(filtro, IAMVideoProcAmp)
                If c IsNot Nothing Then esito = c.SetValore(p.Codice, v, flag)
            End If
            Return esito = 0
        Catch ex As Exception
            _log.ScriviLog("ControlliFotocamera.Imposta: " & ex.Message)
            Return False
        Finally
            Try
                If filtro IsNot Nothing Then Marshal.ReleaseComObject(filtro)
            Catch
            End Try
        End Try
    End Function

    ''' <summary>Rimette alla videocamera i valori salvati nella configurazione.</summary>
    Public Shared Sub ApplicaSalvati(nomeCamera As String)
        Try
            For Each p As ProprietaCamera In Elenco(nomeCamera)
                Dim salvato As String = ImpostazioniMusica.Leggi("cam_p_" & p.Id, "")
                Dim auto As String = ImpostazioniMusica.Leggi("cam_a_" & p.Id, "")
                If salvato = "" AndAlso auto = "" Then Continue For
                Dim valore As Integer
                If Not Integer.TryParse(salvato, NumberStyles.Integer, CultureInfo.InvariantCulture, valore) Then valore = p.Corrente
                Imposta(nomeCamera, p, valore, auto = "true")
            Next
        Catch ex As Exception
            _log.ScriviLog("ControlliFotocamera.ApplicaSalvati: " & ex.Message)
        End Try
    End Sub

    ' ---- i formati ------------------------------------------------------------------------------------------

    ''' <summary>Le risoluzioni che la videocamera offre, con i fotogrammi al secondo massimi, dalla più grande.</summary>
    Public Shared Function Formati(nomeCamera As String) As List(Of FormatoCamera)
        Dim risultato As New List(Of FormatoCamera)()
        Try
            If FfmpegUtil.Percorso() = "" Then Return risultato
            Dim codice As Integer
            Dim testo As String = FfmpegUtil.Esegui("-hide_banner -list_options true -f dshow -i video=" & FfmpegUtil.Virgolette(nomeCamera), codice, 20000)
            Dim rx As New Regex("(pixel_format|vcodec)=(\S+)\s+min s=(\d+)x(\d+) fps=([\d.]+) max s=(\d+)x(\d+) fps=([\d.]+)")
            For Each m As Match In rx.Matches(testo)
                For Each coppia As Integer() In New Integer()() {New Integer() {3, 4}, New Integer() {6, 7}}
                    Dim l As Integer = CInt(m.Groups(coppia(0)).Value)
                    Dim a As Integer = CInt(m.Groups(coppia(1)).Value)
                    Dim fps As Double = Double.Parse(m.Groups(8).Value, CultureInfo.InvariantCulture)
                    Dim esistente As FormatoCamera = risultato.FirstOrDefault(Function(f) f.Larghezza = l AndAlso f.Altezza = a)
                    Dim eMjpeg As Boolean = m.Groups(1).Value = "vcodec"
                    If esistente Is Nothing Then
                        risultato.Add(New FormatoCamera() With {.Larghezza = l, .Altezza = a, .FpsMassimi = fps, .Codec = If(eMjpeg, m.Groups(2).Value, ""), .FormatoPixel = If(eMjpeg, "", m.Groups(2).Value)})
                    ElseIf fps > esistente.FpsMassimi Then
                        esistente.FpsMassimi = fps
                        esistente.Codec = If(eMjpeg, m.Groups(2).Value, "")
                        esistente.FormatoPixel = If(eMjpeg, "", m.Groups(2).Value)
                    End If
                Next
            Next
        Catch ex As Exception
            _log.ScriviLog("ControlliFotocamera.Formati: " & ex.Message)
        End Try
        Return risultato.OrderByDescending(Function(f) f.Larghezza * f.Altezza).ToList()
    End Function

End Class
