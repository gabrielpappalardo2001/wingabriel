Imports System.Runtime.InteropServices
Imports System.Collections.Generic
Imports System.Linq
Imports System.Xml.Linq

' Motore di impaginazione e calcolo delle righe visibili per la visualizzazione corretta del documento a schermo.
Public Class WGWordLayoutMotore

    <DllImport("gdi32.dll")> Private Shared Function SelectObject(ByVal hdc As IntPtr, ByVal hgdiobj As IntPtr) As IntPtr
    End Function
    <DllImport("gdi32.dll")> Private Shared Function DeleteObject(ByVal hObject As IntPtr) As Boolean
    End Function
    <DllImport("gdi32.dll", CharSet:=CharSet.Unicode, EntryPoint:="CreateFontW")> Private Shared Function CreateFont(ByVal nHeight As Integer, ByVal nWidth As Integer, ByVal nEscapement As Integer, ByVal nOrientation As Integer, ByVal fnWeight As Integer, ByVal fdwItalic As UInteger, ByVal fdwUnderline As UInteger, ByVal fdwStrikeOut As UInteger, ByVal fdwCharSet As UInteger, ByVal fdwOutputPrecision As UInteger, ByVal fdwClipPrecision As UInteger, ByVal fdwQuality As UInteger, ByVal fdwPitchAndFamily As UInteger, ByVal lpszFace As String) As IntPtr
    End Function
    <DllImport("gdi32.dll", CharSet:=CharSet.Unicode, EntryPoint:="GetTextExtentPoint32W")> Private Shared Function GetTextExtentPoint32(ByVal hdc As IntPtr, ByVal lpString As String, ByVal cbString As Integer, ByRef lpSize As WGMisura) As Boolean
    End Function
    <DllImport("gdi32.dll")> Private Shared Function CreateCompatibleDC(ByVal hdc As IntPtr) As IntPtr
    End Function
    <DllImport("gdi32.dll")> Private Shared Function DeleteDC(ByVal hdc As IntPtr) As Boolean
    End Function
    <DllImport("user32.dll")> Private Shared Function GetDC(ByVal hwnd As IntPtr) As IntPtr
    End Function
    <DllImport("user32.dll")> Private Shared Function ReleaseDC(ByVal hwnd As IntPtr, ByVal hdc As IntPtr) As Integer
    End Function
    <DllImport("gdi32.dll")> Private Shared Function GetDeviceCaps(ByVal hdc As IntPtr, ByVal nIndex As Integer) As Integer
    End Function

    Private Const FW_NORMAL As Integer = 400
    Private Const FW_BOLD As Integer = 700
    Private Const ANSI_CHARSET As UInteger = 0
    Private Const OUT_DEFAULT_PRECIS As UInteger = 0
    Private Const CLIP_DEFAULT_PRECIS As UInteger = 0
    Private Const CLEARTYPE_QUALITY As UInteger = 5
    Private Const DEFAULT_PITCH As UInteger = 0
    Private Const LOGPIXELSY As Integer = 90

    Public Const ALTEZZA_RIGA_MINIMA As Integer = 20
    Public Const LARGHEZZA_INDENT As Integer = 40
    Public Const MARGINE_MISURA As Integer = 10
    Public Const SOGLIA_SPEZZA As Integer = 30

    <StructLayout(LayoutKind.Sequential)>
    Public Structure WGMisura
        Public cx As Integer
        Public cy As Integer
    End Structure

    ''' <summary>
    ''' Tiene lo stato del tema (colori e font) del documento correntemente aperto,
    ''' cosi il motore di layout e il render possono usare gli STESSI colori/font
    ''' scritti in word/theme/theme1.xml, invece di valori fissi indipendenti dal documento.
    ''' Chi apre un file .docx dovrebbe chiamare CaricaDaXmlTema() con il contenuto grezzo
    ''' di theme1.xml se presente (mainPart.ThemePart), oppure Ripristina() se il documento
    ''' non ha un tema proprio (cosi si torna al tema "Office" di default).
    ''' </summary>
    Public Class WGTemaDocumento
        Private Shared ReadOnly _logger As New Logger()
        Private Shared ReadOnly _lock As New Object()
        Private Shared ReadOnly NsA As XNamespace = "http://schemas.openxmlformats.org/drawingml/2006/main"

        Public Shared Property ColoreTestoScuro1 As String = "000000"
        Public Shared Property ColoreTestoChiaro1 As String = "FFFFFF"
        Public Shared Property ColoreTestoScuro2 As String = "44546A"
        Public Shared Property ColoreTestoChiaro2 As String = "E7E6E6"
        Public Shared Property ColoriAccento As String() = {"4472C4", "ED7D31", "A5A5A5", "FFC000", "5B9BD5", "70AD47"}
        Public Shared Property ColoreHyperlink As String = "0563C1"
        Public Shared Property ColoreHyperlinkVisitato As String = "954F72"
        Public Shared Property FontTitoli As String = "Calibri Light"
        Public Shared Property FontCorpo As String = "Calibri"
        Public Shared Property TemaCaricatoDalDocumento As Boolean = False

        ''' <summary>Riporta il tema ai valori di default (tema "Office"), es. per un documento nuovo o senza theme1.xml.</summary>
        Public Shared Sub Ripristina()
            SyncLock _lock
                ColoreTestoScuro1 = "000000"
                ColoreTestoChiaro1 = "FFFFFF"
                ColoreTestoScuro2 = "44546A"
                ColoreTestoChiaro2 = "E7E6E6"
                ColoriAccento = New String() {"4472C4", "ED7D31", "A5A5A5", "FFC000", "5B9BD5", "70AD47"}
                ColoreHyperlink = "0563C1"
                ColoreHyperlinkVisitato = "954F72"
                FontTitoli = "Calibri Light"
                FontCorpo = "Calibri"
                TemaCaricatoDalDocumento = False
            End SyncLock
        End Sub

        ''' <summary>
        ''' Analizza il contenuto grezzo di word/theme/theme1.xml (clrScheme e fontScheme)
        ''' e aggiorna colori/font condivisi da layout e render. Se il documento aperto non
        ''' ha un tema diverso da quello di default, i valori restano semplicemente invariati.
        ''' </summary>
        Public Shared Sub CaricaDaXmlTema(xmlTema As String)
            SyncLock _lock
                Try
                    If String.IsNullOrWhiteSpace(xmlTema) Then Return
                    Dim doc As XDocument = XDocument.Parse(xmlTema)

                    Dim clrScheme As XElement = doc.Descendants(NsA + "clrScheme").FirstOrDefault()
                    If clrScheme IsNot Nothing Then
                        ColoreTestoScuro1 = EstraiColoreSchema(clrScheme, "dk1", ColoreTestoScuro1)
                        ColoreTestoChiaro1 = EstraiColoreSchema(clrScheme, "lt1", ColoreTestoChiaro1)
                        ColoreTestoScuro2 = EstraiColoreSchema(clrScheme, "dk2", ColoreTestoScuro2)
                        ColoreTestoChiaro2 = EstraiColoreSchema(clrScheme, "lt2", ColoreTestoChiaro2)

                        Dim nuoviAccenti As New List(Of String)
                        For i As Integer = 1 To 6
                            Dim fallback As String = If(i <= ColoriAccento.Length, ColoriAccento(i - 1), "000000")
                            nuoviAccenti.Add(EstraiColoreSchema(clrScheme, "accent" & i, fallback))
                        Next
                        ColoriAccento = nuoviAccenti.ToArray()

                        ColoreHyperlink = EstraiColoreSchema(clrScheme, "hlink", ColoreHyperlink)
                        ColoreHyperlinkVisitato = EstraiColoreSchema(clrScheme, "folHlink", ColoreHyperlinkVisitato)
                    End If

                    Dim fontScheme As XElement = doc.Descendants(NsA + "fontScheme").FirstOrDefault()
                    If fontScheme IsNot Nothing Then
                        Dim major As String = fontScheme.Element(NsA + "majorFont")?.Element(NsA + "latin")?.Attribute("typeface")?.Value
                        Dim minor As String = fontScheme.Element(NsA + "minorFont")?.Element(NsA + "latin")?.Attribute("typeface")?.Value
                        If Not String.IsNullOrWhiteSpace(major) Then FontTitoli = major
                        If Not String.IsNullOrWhiteSpace(minor) Then FontCorpo = minor
                    End If

                    TemaCaricatoDalDocumento = True
                Catch ex As Exception
                    _logger.ScriviLog("WGTemaDocumento.CaricaDaXmlTema: " & ex.Message)
                End Try
            End SyncLock
        End Sub

        Private Shared Function EstraiColoreSchema(clrScheme As XElement, nomeNodo As String, fallback As String) As String
            Try
                Dim nodo As XElement = clrScheme.Element(NsA + nomeNodo)
                If nodo Is Nothing Then Return fallback

                Dim srgb As XElement = nodo.Element(NsA + "srgbClr")
                If srgb IsNot Nothing AndAlso srgb.Attribute("val") IsNot Nothing Then Return srgb.Attribute("val").Value

                Dim sysClr As XElement = nodo.Element(NsA + "sysClr")
                If sysClr IsNot Nothing AndAlso sysClr.Attribute("lastClr") IsNot Nothing Then Return sysClr.Attribute("lastClr").Value

                Return fallback
            Catch
                Return fallback
            End Try
        End Function
    End Class

    Public Class WGSegmentoRiga
        Public Property Run As DocRun
        Public Property Testo As String = ""
        Public Property OffsetAssoluto As Integer = 0
        Public Property X As Integer = 0
        Public Property Larghezza As Integer = 0
        Public Property Altezza As Integer = 0
        Public Property DimensioneFontEffettiva As Single = 11
        Public Property Sottolineato As Boolean = False
        Public Property Sopra As Boolean = False
        Public Property Sotto As Boolean = False
    End Class

    Public Class WGRigaVisuale
        Public Property OffsetInizio As Integer = 0
        Public Property Lunghezza As Integer = 0
        Public Property Larghezza As Integer = 0
        Public Property Altezza As Integer = ALTEZZA_RIGA_MINIMA
        Public Property Segmenti As New List(Of WGSegmentoRiga)
    End Class

    Private Shared _fontCache As New Dictionary(Of String, IntPtr)
    Private Shared ReadOnly _fontCacheLock As New Object()
    Private Shared _logger As New Logger()

    Public Shared Function OttieniFont(fontName As String, fontSizeEffettiva As Single, bold As Boolean, italic As Boolean, underline As Boolean, strikethrough As Boolean) As IntPtr
        SyncLock _fontCacheLock
            Try
                Dim safeFontName As String = If(String.IsNullOrEmpty(fontName), "Arial", fontName)
                Dim actualSize As Single = Math.Max(1.0F, fontSizeEffettiva)
                Dim key As String = safeFontName & "_" & actualSize.ToString(System.Globalization.CultureInfo.InvariantCulture) & "_" & bold & "_" & italic & "_" & underline & "_" & strikethrough

                If _fontCache.ContainsKey(key) Then Return _fontCache(key)

                Dim hdcScreen As IntPtr = GetDC(IntPtr.Zero)
                Dim dpiY As Integer = 96
                If hdcScreen <> IntPtr.Zero Then
                    dpiY = GetDeviceCaps(hdcScreen, LOGPIXELSY)
                    ReleaseDC(IntPtr.Zero, hdcScreen)
                End If

                Dim height As Integer = -CInt(actualSize * dpiY / 72.0F)
                Dim weight As Integer = If(bold, FW_BOLD, FW_NORMAL)
                Dim ulVal As UInteger = If(underline, 1UI, 0UI)
                Dim itVal As UInteger = If(italic, 1UI, 0UI)
                Dim stVal As UInteger = If(strikethrough, 1UI, 0UI)

                Dim hFont As IntPtr = CreateFont(height, 0, 0, 0, weight, itVal, ulVal, stVal, ANSI_CHARSET, OUT_DEFAULT_PRECIS, CLIP_DEFAULT_PRECIS, CLEARTYPE_QUALITY, DEFAULT_PITCH, safeFontName)
                If hFont <> IntPtr.Zero Then _fontCache(key) = hFont
                Return hFont
            Catch ex As Exception
                _logger.ScriviLog("WGWordLayoutMotore.OttieniFont: " & ex.Message)
                Return IntPtr.Zero
            End Try
        End SyncLock
    End Function

    Public Shared Sub SvuotaCache()
        Try
            SyncLock _fontCacheLock
                For Each h As IntPtr In _fontCache.Values
                    If h <> IntPtr.Zero Then DeleteObject(h)
                Next
                _fontCache.Clear()
            End SyncLock
        Catch ex As Exception
            _logger.ScriviLog("WGWordLayoutMotore.SvuotaCache: " & ex.Message)
        End Try
    End Sub

    Public Shared Function DimensioneEffettiva(run As DocRun, fontFactor As Single, zoom As Single) As Single
        Try
            If run Is Nothing Then Return Math.Max(1.0F, 11.0F * fontFactor * zoom)
            Dim baseSize As Single = run.FontSize * fontFactor
            Dim conSoprasotto As Single = If(run.IsSuperscript OrElse run.IsSubscript, baseSize * 0.7F, baseSize)
            Return Math.Max(1.0F, conSoprasotto * zoom)
        Catch ex As Exception
            _logger.ScriviLog("WGWordLayoutMotore.DimensioneEffettiva: " & ex.Message)
            Return Math.Max(1.0F, 11.0F * zoom)
        End Try
    End Function

    Public Shared Function CalcolaRighe(para As DocParagraph, larghezzaDisponibile As Integer, zoom As Single, fontFactor As Single) As List(Of WGRigaVisuale)
        Dim righe As New List(Of WGRigaVisuale)
        Dim hdc As IntPtr = IntPtr.Zero
        Try
            Dim zoomSicuro As Single = Math.Max(0.1F, zoom)

            If para Is Nothing OrElse para.Runs Is Nothing OrElse para.Runs.Count = 0 Then
                righe.Add(New WGRigaVisuale With {.Altezza = CInt(ALTEZZA_RIGA_MINIMA * zoomSicuro)})
                Return righe
            End If

            Dim larghezzaUtile As Integer = Math.Max(40, larghezzaDisponibile - CInt(MARGINE_MISURA * zoomSicuro))
            Dim sogliaSpezza As Integer = CInt(SOGLIA_SPEZZA * zoomSicuro)
            Dim altezzaMinima As Integer = CInt(ALTEZZA_RIGA_MINIMA * zoomSicuro)

            hdc = CreateCompatibleDC(IntPtr.Zero)
            If hdc = IntPtr.Zero Then
                righe.Add(New WGRigaVisuale With {.Altezza = altezzaMinima})
                Return righe
            End If

            Dim rigaCorrente As New WGRigaVisuale()
            Dim offsetGlobale As Integer = 0
            Dim xCorrente As Integer = 0
            Dim altezzaRigaCorrente As Integer = altezzaMinima
            Dim contenutoTrovato As Boolean = False

            Dim ChiudiRigaCorrente As Action =
                Sub()
                    rigaCorrente.Larghezza = xCorrente
                    rigaCorrente.Altezza = altezzaRigaCorrente
                    rigaCorrente.Lunghezza = offsetGlobale - rigaCorrente.OffsetInizio
                    righe.Add(rigaCorrente)
                    rigaCorrente = New WGRigaVisuale With {.OffsetInizio = offsetGlobale}
                    xCorrente = 0
                    altezzaRigaCorrente = altezzaMinima
                End Sub

            For Each run As DocRun In para.Runs
                If run Is Nothing OrElse run.IsImage Then Continue For

                Dim actSize As Single = DimensioneEffettiva(run, fontFactor, zoomSicuro)
                Dim isLink As Boolean = run.IsHyperlink OrElse Not String.IsNullOrEmpty(run.LinkUrl)
                Dim hFont As IntPtr = OttieniFont(run.FontFamily, actSize, run.Bold, run.Italic, run.Underline OrElse isLink, run.Strikethrough)
                If hFont = IntPtr.Zero Then Continue For

                Dim oldFont As IntPtr = SelectObject(hdc, hFont)

                Dim segmentiRun() As String = If(run.Text, "").Split(ControlChars.Lf)
                For sIdx As Integer = 0 To segmentiRun.Length - 1
                    Dim testoSegmento As String = segmentiRun(sIdx)
                    contenutoTrovato = True

                    Dim posizione As Integer = 0
                    While posizione < testoSegmento.Length
                        Dim spazioResiduo As Integer = larghezzaUtile - xCorrente
                        If spazioResiduo < sogliaSpezza AndAlso xCorrente > 0 Then
                            ChiudiRigaCorrente()
                            spazioResiduo = larghezzaUtile
                        End If

                        Dim fineTratto As Integer = posizione
                        Dim ultimoSpazio As Integer = -1
                        Dim larghezzaAccumulata As Integer = 0
                        While fineTratto < testoSegmento.Length
                            Dim misuraChar As WGMisura
                            GetTextExtentPoint32(hdc, testoSegmento(fineTratto).ToString(), 1, misuraChar)
                            If larghezzaAccumulata + misuraChar.cx > spazioResiduo AndAlso fineTratto > posizione Then Exit While
                            larghezzaAccumulata += misuraChar.cx
                            If testoSegmento(fineTratto) = " "c Then ultimoSpazio = fineTratto
                            fineTratto += 1
                        End While

                        If fineTratto < testoSegmento.Length AndAlso ultimoSpazio > posizione Then fineTratto = ultimoSpazio + 1
                        If fineTratto = posizione Then fineTratto = posizione + 1

                        Dim tratto As String = testoSegmento.Substring(posizione, Math.Min(fineTratto, testoSegmento.Length) - posizione)
                        Dim misuraTratto As WGMisura
                        If tratto.Length > 0 Then GetTextExtentPoint32(hdc, tratto, tratto.Length, misuraTratto)
                        If misuraTratto.cy <= 0 Then misuraTratto.cy = altezzaMinima

                        Dim segmento As New WGSegmentoRiga With {
                            .Run = run,
                            .Testo = tratto,
                            .OffsetAssoluto = offsetGlobale,
                            .X = xCorrente,
                            .Larghezza = misuraTratto.cx,
                            .Altezza = misuraTratto.cy,
                            .DimensioneFontEffettiva = actSize,
                            .Sottolineato = run.Underline OrElse isLink,
                            .Sopra = run.IsSuperscript,
                            .Sotto = run.IsSubscript
                        }
                        rigaCorrente.Segmenti.Add(segmento)
                        xCorrente += misuraTratto.cx
                        altezzaRigaCorrente = Math.Max(altezzaRigaCorrente, misuraTratto.cy)
                        offsetGlobale += tratto.Length
                        posizione = Math.Min(fineTratto, testoSegmento.Length)

                        If posizione < testoSegmento.Length Then ChiudiRigaCorrente()
                    End While

                    If sIdx < segmentiRun.Length - 1 Then
                        offsetGlobale += 1
                        ChiudiRigaCorrente()
                    End If
                Next

                SelectObject(hdc, oldFont)
            Next

            rigaCorrente.Larghezza = xCorrente
            rigaCorrente.Altezza = altezzaRigaCorrente
            rigaCorrente.Lunghezza = offsetGlobale - rigaCorrente.OffsetInizio
            righe.Add(rigaCorrente)

            If Not contenutoTrovato AndAlso righe.Count = 0 Then
                righe.Add(New WGRigaVisuale With {.Altezza = altezzaMinima})
            End If

            If righe.Count = 0 Then righe.Add(New WGRigaVisuale With {.Altezza = altezzaMinima})
        Catch ex As Exception
            _logger.ScriviLog("WGWordLayoutMotore.CalcolaRighe: " & ex.Message)
            righe.Clear()
            righe.Add(New WGRigaVisuale With {.Altezza = CInt(ALTEZZA_RIGA_MINIMA * Math.Max(0.1F, zoom))})
        Finally
            If hdc <> IntPtr.Zero Then DeleteDC(hdc)
        End Try
        Return righe
    End Function

    Public Shared Function AltezzaTotaleParagrafo(righe As List(Of WGRigaVisuale), spacing As Single) As Integer
        Try
            If righe Is Nothing OrElse righe.Count = 0 Then Return ALTEZZA_RIGA_MINIMA
            Dim spacingSicuro As Single = Math.Max(1.0F, spacing)
            Dim totale As Integer = 0
            For Each r As WGRigaVisuale In righe
                totale += CInt(r.Altezza * spacingSicuro)
            Next
            Return Math.Max(1, totale)
        Catch ex As Exception
            _logger.ScriviLog("WGWordLayoutMotore.AltezzaTotaleParagrafo: " & ex.Message)
            Return ALTEZZA_RIGA_MINIMA
        End Try
    End Function

    Public Shared Function TrovaRigaPerOffset(righe As List(Of WGRigaVisuale), offsetCarattere As Integer) As Integer
        Try
            If righe Is Nothing OrElse righe.Count = 0 Then Return -1
            For i As Integer = 0 To righe.Count - 1
                Dim inizio As Integer = righe(i).OffsetInizio
                Dim fine As Integer = inizio + righe(i).Lunghezza
                If offsetCarattere >= inizio AndAlso (offsetCarattere < fine OrElse i = righe.Count - 1) Then Return i
            Next
            Return righe.Count - 1
        Catch ex As Exception
            _logger.ScriviLog("WGWordLayoutMotore.TrovaRigaPerOffset: " & ex.Message)
            Return -1
        End Try
    End Function

End Class