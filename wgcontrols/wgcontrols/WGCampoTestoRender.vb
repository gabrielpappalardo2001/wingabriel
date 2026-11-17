Imports System.Drawing
Imports System.Windows.Forms
Imports System.Runtime.InteropServices
Imports System.Text
Imports System.Collections.Generic
Imports System.Linq
Imports wgcontrols

Public Class SegmentoTesto
    Public Property Testo As String = ""
    Public Property NomeFont As String = "Arial"
    Public Property Dimensione As Single = 11.0F
    Public Property Colore As System.Drawing.Color = WGTema.Testo
    Public Property Grassetto As Boolean = False
    Public Property Corsivo As Boolean = False
    Public Property Sottolineato As Boolean = False

    Public Function Clone() As SegmentoTesto
        Try
            Dim s As New SegmentoTesto()
            s.Testo = Me.Testo
            s.NomeFont = Me.NomeFont
            s.Dimensione = Me.Dimensione
            s.Colore = Me.Colore
            s.Grassetto = Me.Grassetto
            s.Corsivo = Me.Corsivo
            s.Sottolineato = Me.Sottolineato
            Return s
        Catch ex As Exception
            Dim logSeg As New Logger()
            logSeg.ScriviLog("SegmentoTesto.Clone: " & ex.Message)
            Return New SegmentoTesto()
        End Try
    End Function
End Class

Public Class WGCampoTestoRender
    Inherits Control
    Implements IWGBrailleCampo

    Private _logger As New Logger()
    Private _sintesiVocale As sintesivocale = sintesivocale.GetInstance()
    Private _impostazioniComuni As New WGImpostazioniComuni()
    Private _confermatoLimiteVerticale As Boolean = False
    Private _direzioneLimiteConfermata As Integer = 0
    Private WithEvents _tastiera As WGTastiera

    Private _shiftPremuto As Boolean = False
    Private _altPremuto As Boolean = False
    Private _altDestro As Boolean = False
    Private _altDestroUsato As Boolean = False
    Private _ctrlSinistro As Boolean = False
    Private _ctrlDestro As Boolean = False

    Public Property Multiriga As Boolean = True
    Public Property SolaLettura As Boolean = False

    Private _segmenti As New List(Of SegmentoTesto)()
    Private _caretPos As Integer = 0
    Private _selStart As Integer = -1
    Private _selEnd As Integer = -1
    Private _scrollY As Integer = 0

    Private _fontDefault As String = "Arial"
    Private _dimensioneDefault As Single = 11.0F
    Private _coloreDefault As System.Drawing.Color = WGTema.Testo
    Private _grassettoDefault As Boolean = False
    Private _corsivoDefault As Boolean = False
    Private _sottolineatoDefault As Boolean = False

    Private _undoStack As New Stack(Of List(Of SegmentoTesto))()
    Private _redoStack As New Stack(Of List(Of SegmentoTesto))()

    Private _contatoreInvio As Integer = 0
    Private _ultimoInvio As DateTime = DateTime.MinValue
    Private _domandaChiusura As WGDomanda
    Private _domandaAperta As Boolean = False

    Public Event chiusuraConfermata(sender As Object, e As EventArgs)
    Public Event chiusuraAnnullata(sender As Object, e As EventArgs)
    Public Event menudestroapertura(sender As Object, e As EventArgs)

    Private Structure GlyphInfo
        Public Carattere As Char
        Public X As Single
        Public Y As Single
        Public Larghezza As Single
        Public Altezza As Single
        Public SegmentoIndex As Integer
        Public CharIndexInSegmento As Integer
    End Structure

    Private _glyphs As New List(Of GlyphInfo)()
    Private _totalHeight As Integer = 0
    Private _layoutValido As Boolean = False
    Private _padding As Integer = 4

    <DllImport("gdi32.dll", CharSet:=CharSet.Unicode)>
    Private Shared Function CreateFont(nHeight As Integer, nWidth As Integer, nEscapement As Integer, nOrientation As Integer, fnWeight As Integer, fdwItalic As UInteger, fdwUnderline As UInteger, fdwStrikeOut As UInteger, fdwCharSet As UInteger, fdwOutputPrecision As UInteger, fdwClipPrecision As UInteger, fdwQuality As UInteger, fdwPitchAndFamily As UInteger, lpszFace As String) As IntPtr
    End Function

    <DllImport("gdi32.dll")>
    Private Shared Function SelectObject(hdc As IntPtr, hObject As IntPtr) As IntPtr
    End Function

    <DllImport("gdi32.dll")>
    Private Shared Function DeleteObject(hObject As IntPtr) As Boolean
    End Function

    <DllImport("gdi32.dll")>
    Private Shared Function SetTextColor(hdc As IntPtr, crColor As Integer) As Integer
    End Function

    <DllImport("gdi32.dll")>
    Private Shared Function SetBkMode(hdc As IntPtr, iBkMode As Integer) As Integer
    End Function

    <DllImport("gdi32.dll", CharSet:=CharSet.Unicode)>
    Private Shared Function TextOut(hdc As IntPtr, nXStart As Integer, nYStart As Integer, lpString As String, cbString As Integer) As Boolean
    End Function

    <DllImport("gdi32.dll", CharSet:=CharSet.Unicode)>
    Private Shared Function GetTextExtentPoint32(hdc As IntPtr, lpString As String, cbString As Integer, ByRef lpSize As SIZE) As Boolean
    End Function

    <DllImport("user32.dll")>
    Private Shared Function CreateCaret(hWnd As IntPtr, hBitmap As IntPtr, nWidth As Integer, nHeight As Integer) As Boolean
    End Function

    <DllImport("user32.dll")>
    Private Shared Function ShowCaret(hWnd As IntPtr) As Boolean
    End Function

    <DllImport("user32.dll")>
    Private Shared Function HideCaret(hWnd As IntPtr) As Boolean
    End Function

    <DllImport("user32.dll")>
    Private Shared Function DestroyCaret() As Boolean
    End Function

    <DllImport("user32.dll")>
    Private Shared Function SetCaretPos(X As Integer, Y As Integer) As Boolean
    End Function

    <DllImport("user32.dll")>
    Private Shared Function GetKeyboardState(lpKeyState As Byte()) As Boolean
    End Function

    <DllImport("user32.dll")>
    Private Shared Function InvalidateRect(hWnd As IntPtr, lpRect As IntPtr, bErase As Boolean) As Boolean
    End Function

    <StructLayout(LayoutKind.Sequential)>
    Private Structure SIZE
        Public cx As Integer
        Public cy As Integer
    End Structure

    Private Const TRANSPARENT As Integer = 1
    Private Const FW_NORMAL As Integer = 400
    Private Const FW_BOLD As Integer = 700
    Private Const DEFAULT_CHARSET As UInteger = 1
    Private Const OUT_DEFAULT_PRECIS As UInteger = 0
    Private Const CLIP_DEFAULT_PRECIS As UInteger = 0
    Private Const CLEARTYPE_QUALITY As UInteger = 5
    Private Const DEFAULT_PITCH As UInteger = 0
    Private Const FF_DONTCARE As UInteger = 0
    Private Const WM_PAINT As Integer = &HF

    <StructLayout(LayoutKind.Sequential)>
    Private Structure PAINTSTRUCT
        Public hdc As IntPtr
        Public fErase As Boolean
        Public rcPaintLeft As Integer
        Public rcPaintTop As Integer
        Public rcPaintRight As Integer
        Public rcPaintBottom As Integer
        Public fRestore As Boolean
        Public fIncUpdate As Boolean
        Public reserved1 As Integer
        Public reserved2 As Integer
        Public reserved3 As Integer
        Public reserved4 As Integer
        Public reserved5 As Integer
        Public reserved6 As Integer
        Public reserved7 As Integer
        Public reserved8 As Integer
    End Structure

    <DllImport("user32.dll")>
    Private Shared Function BeginPaint(hWnd As IntPtr, ByRef lpPaint As PAINTSTRUCT) As IntPtr
    End Function

    <DllImport("user32.dll")>
    Private Shared Function EndPaint(hWnd As IntPtr, ByRef lpPaint As PAINTSTRUCT) As Boolean
    End Function

    <DllImport("user32.dll")>
    Private Shared Function GetDC(hWnd As IntPtr) As IntPtr
    End Function

    <DllImport("user32.dll")>
    Private Shared Function ReleaseDC(hWnd As IntPtr, hDC As IntPtr) As Integer
    End Function

    Public Sub New()
        Try
            _tastiera = WGTastiera.GetInstance()
            Me.SetStyle(ControlStyles.UserPaint Or ControlStyles.AllPaintingInWmPaint Or ControlStyles.Opaque Or ControlStyles.ResizeRedraw Or ControlStyles.Selectable, True)
            Me.BackColor = WGTema.Sfondo
            Me.Cursor = Cursors.IBeam
            WGBraille.Collega(Me)
            _segmenti.Add(New SegmentoTesto() With {.Testo = "", .NomeFont = _fontDefault, .Dimensione = _dimensioneDefault, .Colore = _coloreDefault})
            AddHandler WGTema.CambiatoTema, AddressOf OnCambiatoTema
        Catch ex As Exception
            _logger.ScriviLog("WGCampoTestoRender New: " & ex.Message)
        End Try
    End Sub

    ' Windows ha cambiato tema chiaro/scuro: il testo gia' scritto col colore predefinito di prima passa al nuovo,
    ' quello colorato apposta dall'utente resta com'e'.
    Private Sub OnCambiatoTema(sender As Object, e As EventArgs)
        Try
            If Me.IsDisposed Then Return
            If Me.InvokeRequired Then
                Me.BeginInvoke(New MethodInvoker(Sub() OnCambiatoTema(sender, e)))
                Return
            End If
            Dim vecchioDefault As System.Drawing.Color = _coloreDefault
            _coloreDefault = WGTema.Testo
            Me.BackColor = WGTema.Sfondo
            For Each s As SegmentoTesto In _segmenti
                If s.Colore = vecchioDefault Then s.Colore = _coloreDefault
            Next
            Me.Invalidate()
        Catch ex As Exception
            _logger.ScriviLog("WGCampoTestoRender.OnCambiatoTema: " & ex.Message)
        End Try
    End Sub


    Protected Overrides ReadOnly Property CreateParams As CreateParams
        Get
            Try
                Dim cp As CreateParams = MyBase.CreateParams
                cp.Style = cp.Style Or &H200000
                Return cp
            Catch ex As Exception
                _logger.ScriviLog("CreateParams: " & ex.Message)
                Return MyBase.CreateParams
            End Try
        End Get
    End Property

    Protected Overrides ReadOnly Property DefaultSize As System.Drawing.Size
        Get
            Try
                Return New System.Drawing.Size(300, 150)
            Catch ex As Exception
                _logger.ScriviLog("DefaultSize Get: " & ex.Message)
                Return New System.Drawing.Size(300, 150)
            End Try
        End Get
    End Property

    Public Function MetadatiTesto() As List(Of SegmentoTesto)
        Try
            Dim risultato As New List(Of SegmentoTesto)()
            For Each seg In _segmenti
                If Not String.IsNullOrEmpty(seg.Testo) Then
                    risultato.Add(seg.Clone())
                End If
            Next
            Return risultato
        Catch ex As Exception
            _logger.ScriviLog("MetadatiTesto: " & ex.Message)
            Return New List(Of SegmentoTesto)()
        End Try
    End Function

    Public Function TestoPiatto() As String
        Try
            Dim sb As New StringBuilder()
            For Each seg In _segmenti
                sb.Append(seg.Testo)
            Next
            Return sb.ToString()
        Catch ex As Exception
            _logger.ScriviLog("TestoPiatto: " & ex.Message)
            Return ""
        End Try
    End Function

    Public Function OttieniTestoSelezionato() As String
        Try
            If Not HaSelezione() Then Return String.Empty
            Dim da As Integer = Math.Min(_selStart, _selEnd)
            Dim a As Integer = Math.Max(_selStart, _selEnd)
            Dim testo As String = TestoPiatto()
            If da >= 0 AndAlso a <= testo.Length Then
                Return testo.Substring(da, a - da)
            End If
            Return String.Empty
        Catch ex As Exception
            _logger.ScriviLog("OttieniTestoSelezionato: " & ex.Message)
            Return String.Empty
        End Try
    End Function

    Public Sub EliminaSelezionePubblica()
        Try
            If HaSelezione() Then
                SalvaUndo()
                EliminaSelezione()
            End If
        Catch ex As Exception
            _logger.ScriviLog("EliminaSelezionePubblica: " & ex.Message)
        End Try
    End Sub

    Public Sub IncollaTestoEsterno(testo As String)
        Try
            If SolaLettura OrElse String.IsNullOrEmpty(testo) Then Return
            SalvaUndo()
            If HaSelezione() Then EliminaSelezione()
            InserisciTesto(testo)
        Catch ex As Exception
            _logger.ScriviLog("IncollaTestoEsterno: " & ex.Message)
        End Try
    End Sub

    Public Sub SelezionaTuttoPubblica()
        Try
            SelezionaTutto()
        Catch ex As Exception
            _logger.ScriviLog("SelezionaTuttoPubblica: " & ex.Message)
        End Try
    End Sub

    Public Sub EliminaIndietroPubblica()
        Try
            If SolaLettura Then Return
            EliminaIndietro()
        Catch ex As Exception
            _logger.ScriviLog("EliminaIndietroPubblica: " & ex.Message)
        End Try
    End Sub

#Region "Braille"

    Public ReadOnly Property BrailleModificabile As Boolean Implements IWGBrailleCampo.BrailleModificabile
        Get
            Return Not SolaLettura
        End Get
    End Property

    Public Function BrailleStato(ByRef riga As String, ByRef cursore As Integer) As Boolean Implements IWGBrailleCampo.BrailleStato
        Try
            WGBraille.RigaDaTesto(TestoPiatto(), _caretPos, riga, cursore)
            Return True
        Catch ex As Exception
            _logger.ScriviLog("BrailleStato: " & ex.Message)
            Return False
        End Try
    End Function

    Public Sub BrailleInserisci(testo As String) Implements IWGBrailleCampo.BrailleInserisci
        Try
            If SolaLettura OrElse String.IsNullOrEmpty(testo) Then Return
            For Each c As Char In testo
                IncollaTestoEsterno(c.ToString())
                GestisciEco(c)
                _contatoreInvio = 0
            Next
        Catch ex As Exception
            _logger.ScriviLog("BrailleInserisci: " & ex.Message)
        End Try
    End Sub

    Public Sub BrailleSposta(posizioneNellaRiga As Integer) Implements IWGBrailleCampo.BrailleSposta
        Try
            Dim testo As String = TestoPiatto()
            Dim inizio As Integer = TrovaInizioRigaCorrente(_caretPos)
            Dim fine As Integer = TrovaFineRigaCorrente(_caretPos)
            Dim nuova As Integer = Math.Max(inizio, Math.Min(fine, inizio + posizioneNellaRiga))
            _selStart = -1
            _selEnd = -1
            _caretPos = nuova
            RichiediRidisegno()
            AggiornaCaretVisivo()
            If nuova < testo.Length Then _sintesiVocale.LeggiTesto(TraduciCarattere(testo(nuova)))
        Catch ex As Exception
            _logger.ScriviLog("BrailleSposta: " & ex.Message)
        End Try
    End Sub

#End Region

    Public Sub SpostaCaretSuPubblica()
        Try
            SpostaCaretSu(False)
        Catch ex As Exception
            _logger.ScriviLog("SpostaCaretSuPubblica: " & ex.Message)
        End Try
    End Sub

    Public Sub SpostaCaretGiuPubblica()
        Try
            SpostaCaretGiu(False)
        Catch ex As Exception
            _logger.ScriviLog("SpostaCaretGiuPubblica: " & ex.Message)
        End Try
    End Sub

    Public Sub SpostaCaretDestraPubblica()
        Try
            SpostaCaretDestra(False, False)
        Catch ex As Exception
            _logger.ScriviLog("SpostaCaretDestraPubblica: " & ex.Message)
        End Try
    End Sub

    Public Sub SpostaCaretSinistraPubblica()
        Try
            SpostaCaretSinistra(False, False)
        Catch ex As Exception
            _logger.ScriviLog("SpostaCaretSinistraPubblica: " & ex.Message)
        End Try
    End Sub

    Public Sub LeggiTuttoDaInizioRigaCorrente()
        Try
            Dim testo As String = TestoPiatto()
            Dim inizioRiga As Integer = TrovaInizioRigaCorrente(_caretPos)
            If inizioRiga >= 0 AndAlso inizioRiga < testo.Length Then
                Dim testoRiga As String = testo.Substring(inizioRiga)
                If Not String.IsNullOrEmpty(testoRiga) Then
                    _sintesiVocale.LeggiTesto(testoRiga)
                End If
            End If
        Catch ex As Exception
            _logger.ScriviLog("LeggiTuttoDaInizioRigaCorrente: " & ex.Message)
        End Try
    End Sub

    Public Sub ImpostaSegmenti(segmenti As List(Of SegmentoTesto))
        Try
            SalvaUndo()
            _segmenti.Clear()
            If segmenti IsNot Nothing AndAlso segmenti.Count > 0 Then
                For Each s In segmenti
                    _segmenti.Add(s.Clone())
                Next
            Else
                _segmenti.Add(New SegmentoTesto() With {.Testo = "", .NomeFont = _fontDefault, .Dimensione = _dimensioneDefault, .Colore = _coloreDefault})
            End If
            _caretPos = TotaleLunghezza()
            _selStart = -1
            _selEnd = -1
            _layoutValido = False
            RichiediRidisegno()
        Catch ex As Exception
            _logger.ScriviLog("ImpostaSegmenti: " & ex.Message)
        End Try
    End Sub

    Public Sub ImpostaProprietaDefault(nomeFonte As String, dimensione As Single, colore As System.Drawing.Color, Optional grassetto As Boolean = False, Optional corsivo As Boolean = False, Optional sottolineato As Boolean = False)
        Try
            If Not String.IsNullOrEmpty(nomeFonte) Then _fontDefault = nomeFonte
            If dimensione > 0 Then _dimensioneDefault = dimensione
            _coloreDefault = colore
            _grassettoDefault = grassetto
            _corsivoDefault = corsivo
            _sottolineatoDefault = sottolineato
        Catch ex As Exception
            _logger.ScriviLog("ImpostaProprietaDefault: " & ex.Message)
        End Try
    End Sub

    Public Sub ApplicaFont(nomeFonte As String)
        Try
            If String.IsNullOrEmpty(nomeFonte) Then Return
            If HaSelezione() Then
                SalvaUndo()
                ApplicaProprietaSelezione(Sub(s) s.NomeFont = nomeFonte)
                _sintesiVocale.LeggiTesto("Carattere " & nomeFonte & " applicato")
            Else
                _fontDefault = nomeFonte
                _sintesiVocale.LeggiTesto("Carattere " & nomeFonte & " impostato come predefinito")
            End If
        Catch ex As Exception
            _logger.ScriviLog("ApplicaFont: " & ex.Message)
        End Try
    End Sub

    Public Sub ApplicaDimensione(dimensione As Single)
        Try
            If dimensione <= 0 Then Return
            If HaSelezione() Then
                SalvaUndo()
                ApplicaProprietaSelezione(Sub(s) s.Dimensione = dimensione)
                _sintesiVocale.LeggiTesto("Dimensione " & dimensione.ToString() & " applicata")
            Else
                _dimensioneDefault = dimensione
                _sintesiVocale.LeggiTesto("Dimensione " & dimensione.ToString() & " impostata come predefinita")
            End If
        Catch ex As Exception
            _logger.ScriviLog("ApplicaDimensione: " & ex.Message)
        End Try
    End Sub

    Public Sub ApplicaColore(colore As System.Drawing.Color)
        Try
            If HaSelezione() Then
                SalvaUndo()
                ApplicaProprietaSelezione(Sub(s) s.Colore = colore)
                _sintesiVocale.LeggiTesto("Colore applicato")
            Else
                _coloreDefault = colore
                _sintesiVocale.LeggiTesto("Colore impostato come predefinito")
            End If
        Catch ex As Exception
            _logger.ScriviLog("ApplicaColore: " & ex.Message)
        End Try
    End Sub

    Public Sub ApplicaGrassetto(attivo As Boolean)
        Try
            If HaSelezione() Then
                SalvaUndo()
                ApplicaProprietaSelezione(Sub(s) s.Grassetto = attivo)
                _sintesiVocale.LeggiTesto(If(attivo, "Grassetto attivato", "Grassetto disattivato"))
            Else
                _grassettoDefault = attivo
            End If
        Catch ex As Exception
            _logger.ScriviLog("ApplicaGrassetto: " & ex.Message)
        End Try
    End Sub

    Public Sub ApplicaCorsivo(attivo As Boolean)
        Try
            If HaSelezione() Then
                SalvaUndo()
                ApplicaProprietaSelezione(Sub(s) s.Corsivo = attivo)
                _sintesiVocale.LeggiTesto(If(attivo, "Corsivo attivato", "Corsivo disattivato"))
            Else
                _corsivoDefault = attivo
            End If
        Catch ex As Exception
            _logger.ScriviLog("ApplicaCorsivo: " & ex.Message)
        End Try
    End Sub

    Public Sub ApplicaSottolineato(attivo As Boolean)
        Try
            If HaSelezione() Then
                SalvaUndo()
                ApplicaProprietaSelezione(Sub(s) s.Sottolineato = attivo)
                _sintesiVocale.LeggiTesto(If(attivo, "Sottolineato attivato", "Sottolineato disattivato"))
            Else
                _sottolineatoDefault = attivo
            End If
        Catch ex As Exception
            _logger.ScriviLog("ApplicaSottolineato: " & ex.Message)
        End Try
    End Sub

    Public Sub Annulla()
        Try
            If _undoStack.Count = 0 Then
                _sintesiVocale.LeggiTesto("Niente da annullare")
                Return
            End If
            Dim copia As New List(Of SegmentoTesto)()
            For Each s In _segmenti
                copia.Add(s.Clone())
            Next
            _redoStack.Push(copia)
            _segmenti.Clear()
            For Each s In _undoStack.Pop()
                _segmenti.Add(s.Clone())
            Next
            _caretPos = Math.Min(_caretPos, TotaleLunghezza())
            _selStart = -1
            _selEnd = -1
            _layoutValido = False
            RichiediRidisegno()
            _sintesiVocale.LeggiTesto("Annullato")
        Catch ex As Exception
            _logger.ScriviLog("Annulla: " & ex.Message)
        End Try
    End Sub

    Public Sub Ripristina()
        Try
            If _redoStack.Count = 0 Then
                _sintesiVocale.LeggiTesto("Niente da ripristinare")
                Return
            End If
            Dim copia As New List(Of SegmentoTesto)()
            For Each s In _segmenti
                copia.Add(s.Clone())
            Next
            _undoStack.Push(copia)
            _segmenti.Clear()
            For Each s In _redoStack.Pop()
                _segmenti.Add(s.Clone())
            Next
            _caretPos = Math.Min(_caretPos, TotaleLunghezza())
            _selStart = -1
            _selEnd = -1
            _layoutValido = False
            RichiediRidisegno()
            _sintesiVocale.LeggiTesto("Ripristinato")
        Catch ex As Exception
            _logger.ScriviLog("Ripristina: " & ex.Message)
        End Try
    End Sub

    Private Sub ApplicaProprietaSelezione(modifica As Action(Of SegmentoTesto))
        Try
            Dim da As Integer = Math.Min(_selStart, _selEnd)
            Dim a As Integer = Math.Max(_selStart, _selEnd)
            SplitSegmentoInPosizione(da)
            SplitSegmentoInPosizione(a)
            Dim pos As Integer = 0
            For Each seg In _segmenti
                Dim fine As Integer = pos + seg.Testo.Length
                If pos >= da AndAlso fine <= a Then
                    modifica(seg)
                End If
                pos = fine
            Next
            UnisciSegmentiAdiacenti()
            _layoutValido = False
            RichiediRidisegno()
        Catch ex As Exception
            _logger.ScriviLog("ApplicaProprietaSelezione: " & ex.Message)
        End Try
    End Sub

    Private Sub SplitSegmentoInPosizione(posAssoluta As Integer)
        Try
            Dim pos As Integer = 0
            For i As Integer = 0 To _segmenti.Count - 1
                Dim seg As SegmentoTesto = _segmenti(i)
                Dim fine As Integer = pos + seg.Testo.Length
                If posAssoluta > pos AndAlso posAssoluta < fine Then
                    Dim offset As Integer = posAssoluta - pos
                    Dim s1 As SegmentoTesto = seg.Clone()
                    s1.Testo = seg.Testo.Substring(0, offset)
                    Dim s2 As SegmentoTesto = seg.Clone()
                    s2.Testo = seg.Testo.Substring(offset)
                    _segmenti.RemoveAt(i)
                    _segmenti.Insert(i, s2)
                    _segmenti.Insert(i, s1)
                    Return
                End If
                pos = fine
            Next
        Catch ex As Exception
            _logger.ScriviLog("SplitSegmentoInPosizione: " & ex.Message)
        End Try
    End Sub

    Private Sub UnisciSegmentiAdiacenti()
        Try
            Dim i As Integer = 0
            While i < _segmenti.Count - 1
                Dim a As SegmentoTesto = _segmenti(i)
                Dim b As SegmentoTesto = _segmenti(i + 1)
                If a.NomeFont = b.NomeFont AndAlso
                   Math.Abs(a.Dimensione - b.Dimensione) < 0.01F AndAlso
                   a.Colore = b.Colore AndAlso
                   a.Grassetto = b.Grassetto AndAlso
                   a.Corsivo = b.Corsivo AndAlso
                   a.Sottolineato = b.Sottolineato Then
                    a.Testo &= b.Testo
                    _segmenti.RemoveAt(i + 1)
                Else
                    i += 1
                End If
            End While
        Catch ex As Exception
            _logger.ScriviLog("UnisciSegmentiAdiacenti: " & ex.Message)
        End Try
    End Sub

    Private Function TotaleLunghezza() As Integer
        Try
            Dim tot As Integer = 0
            For Each s In _segmenti
                tot += s.Testo.Length
            Next
            Return tot
        Catch ex As Exception
            _logger.ScriviLog("TotaleLunghezza: " & ex.Message)
            Return 0
        End Try
    End Function

    Private Function CarattereaIndice(posAssoluta As Integer) As (segIdx As Integer, charIdx As Integer)
        Try
            Dim pos As Integer = 0
            For i As Integer = 0 To _segmenti.Count - 1
                Dim len As Integer = _segmenti(i).Testo.Length
                If posAssoluta <= pos + len Then
                    Return (i, posAssoluta - pos)
                End If
                pos += len
            Next
            Return (_segmenti.Count - 1, _segmenti(_segmenti.Count - 1).Testo.Length)
        Catch ex As Exception
            _logger.ScriviLog("CarattereaIndice: " & ex.Message)
            Return (0, 0)
        End Try
    End Function

    Private Sub SalvaUndo()
        Try
            Dim copia As New List(Of SegmentoTesto)()
            For Each s In _segmenti
                copia.Add(s.Clone())
            Next
            _undoStack.Push(copia)
            _redoStack.Clear()
        Catch ex As Exception
            _logger.ScriviLog("SalvaUndo: " & ex.Message)
        End Try
    End Sub

    Private Sub InserisciTesto(testo As String)
        Try
            If SolaLettura Then Return
            SalvaUndo()
            If HaSelezione() Then EliminaSelezione()
            Dim coord = CarattereaIndice(_caretPos)
            Dim seg As SegmentoTesto = _segmenti(coord.segIdx)
            Dim nuovoSeg As SegmentoTesto = seg.Clone()
            nuovoSeg.Testo = seg.Testo.Substring(0, coord.charIdx) & testo & seg.Testo.Substring(coord.charIdx)
            nuovoSeg.NomeFont = _fontDefault
            nuovoSeg.Dimensione = _dimensioneDefault
            nuovoSeg.Colore = _coloreDefault
            nuovoSeg.Grassetto = _grassettoDefault
            nuovoSeg.Corsivo = _corsivoDefault
            nuovoSeg.Sottolineato = _sottolineatoDefault
            If coord.charIdx = 0 AndAlso seg.Testo.Length > 0 Then
                _segmenti.Insert(coord.segIdx, nuovoSeg)
                nuovoSeg.Testo = testo
            ElseIf coord.charIdx = seg.Testo.Length Then
                Dim nuovoSolo As SegmentoTesto = New SegmentoTesto()
                nuovoSolo.Testo = testo
                nuovoSolo.NomeFont = _fontDefault
                nuovoSolo.Dimensione = _dimensioneDefault
                nuovoSolo.Colore = _coloreDefault
                nuovoSolo.Grassetto = _grassettoDefault
                nuovoSolo.Corsivo = _corsivoDefault
                nuovoSolo.Sottolineato = _sottolineatoDefault
                _segmenti.Insert(coord.segIdx + 1, nuovoSolo)
            Else
                SplitSegmentoInPosizione(_caretPos)
                Dim nuovoSolo As SegmentoTesto = New SegmentoTesto()
                nuovoSolo.Testo = testo
                nuovoSolo.NomeFont = _fontDefault
                nuovoSolo.Dimensione = _dimensioneDefault
                nuovoSolo.Colore = _coloreDefault
                nuovoSolo.Grassetto = _grassettoDefault
                nuovoSolo.Corsivo = _corsivoDefault
                nuovoSolo.Sottolineato = _sottolineatoDefault
                Dim posIns As Integer = 0
                For idx As Integer = 0 To _segmenti.Count - 1
                    If posIns = _caretPos Then
                        _segmenti.Insert(idx, nuovoSolo)
                        GoTo FineInserimento
                    End If
                    posIns += _segmenti(idx).Testo.Length
                Next
                _segmenti.Add(nuovoSolo)
FineInserimento:
            End If
            UnisciSegmentiAdiacenti()
            _caretPos += testo.Length
            _layoutValido = False
            RichiediRidisegno()
            AggiornaCaretVisivo()
        Catch ex As Exception
            _logger.ScriviLog("InserisciTesto: " & ex.Message)
        End Try
    End Sub

    Private Sub InserisciACapo()
        Try
            If SolaLettura Then Return
            SalvaUndo()
            If HaSelezione() Then EliminaSelezione()
            Dim coord = CarattereaIndice(_caretPos)
            Dim seg As SegmentoTesto = _segmenti(coord.segIdx)
            Dim prima As String = seg.Testo.Substring(0, coord.charIdx)
            Dim dopo As String = seg.Testo.Substring(coord.charIdx)
            seg.Testo = prima & vbLf
            Dim nuovoSeg As SegmentoTesto = seg.Clone()
            nuovoSeg.Testo = dopo
            _segmenti.Insert(coord.segIdx + 1, nuovoSeg)
            _caretPos += 1
            _layoutValido = False
            RichiediRidisegno()
            AggiornaCaretVisivo()
        Catch ex As Exception
            _logger.ScriviLog("InserisciACapo: " & ex.Message)
        End Try
    End Sub

    Private Sub EliminaIndietro()
        Try
            If SolaLettura Then Return
            If HaSelezione() Then
                SalvaUndo()
                EliminaSelezione()
                Return
            End If
            If _caretPos = 0 Then Return
            SalvaUndo()
            Dim coord = CarattereaIndice(_caretPos - 1)
            Dim seg As SegmentoTesto = _segmenti(coord.segIdx)
            Dim c As Char = seg.Testo(coord.charIdx)
            seg.Testo = seg.Testo.Remove(coord.charIdx, 1)
            If seg.Testo.Length = 0 AndAlso _segmenti.Count > 1 Then
                _segmenti.RemoveAt(coord.segIdx)
            End If
            _caretPos -= 1
            UnisciSegmentiAdiacenti()
            _layoutValido = False
            RichiediRidisegno()
            AggiornaCaretVisivo()
            _sintesiVocale.LeggiTesto(TraduciCarattere(c))
        Catch ex As Exception
            _logger.ScriviLog("EliminaIndietro: " & ex.Message)
        End Try
    End Sub

    Private Sub EliminaAvanti()
        Try
            If SolaLettura Then Return
            If HaSelezione() Then
                SalvaUndo()
                EliminaSelezione()
                Return
            End If
            Dim totale As Integer = TotaleLunghezza()
            If _caretPos >= totale Then Return
            SalvaUndo()
            Dim coord = CarattereaIndice(_caretPos)
            Dim seg As SegmentoTesto = _segmenti(coord.segIdx)
            Dim c As Char = seg.Testo(coord.charIdx)
            seg.Testo = seg.Testo.Remove(coord.charIdx, 1)
            If seg.Testo.Length = 0 AndAlso _segmenti.Count > 1 Then
                _segmenti.RemoveAt(coord.segIdx)
            End If
            UnisciSegmentiAdiacenti()
            _layoutValido = False
            RichiediRidisegno()
            AggiornaCaretVisivo()
            _sintesiVocale.LeggiTesto(TraduciCarattere(c))
        Catch ex As Exception
            _logger.ScriviLog("EliminaAvanti: " & ex.Message)
        End Try
    End Sub

    Private Sub EliminaSelezione()
        Try
            If Not HaSelezione() Then Return
            Dim da As Integer = Math.Min(_selStart, _selEnd)
            Dim a As Integer = Math.Max(_selStart, _selEnd)
            SplitSegmentoInPosizione(da)
            SplitSegmentoInPosizione(a)
            Dim pos As Integer = 0
            Dim i As Integer = 0
            While i < _segmenti.Count
                Dim seg As SegmentoTesto = _segmenti(i)
                Dim fine As Integer = pos + seg.Testo.Length
                If pos >= da AndAlso fine <= a Then
                    _segmenti.RemoveAt(i)
                Else
                    pos = fine
                    i += 1
                End If
            End While
            If _segmenti.Count = 0 Then
                _segmenti.Add(New SegmentoTesto() With {.Testo = "", .NomeFont = _fontDefault, .Dimensione = _dimensioneDefault, .Colore = _coloreDefault})
            End If
            UnisciSegmentiAdiacenti()
            _caretPos = da
            _selStart = -1
            _selEnd = -1
            _layoutValido = False
            RichiediRidisegno()
            AggiornaCaretVisivo()
        Catch ex As Exception
            _logger.ScriviLog("EliminaSelezione: " & ex.Message)
        End Try
    End Sub

    Private Function HaSelezione() As Boolean
        Try
            Return _selStart >= 0 AndAlso _selEnd >= 0 AndAlso _selStart <> _selEnd
        Catch ex As Exception
            _logger.ScriviLog("HaSelezione: " & ex.Message)
            Return False
        End Try
    End Function

    Private Sub SelezionaTutto()
        Try
            _selStart = 0
            _selEnd = TotaleLunghezza()
            _caretPos = _selEnd
            RichiediRidisegno()
            _sintesiVocale.LeggiTesto("Selezionato tutto")
        Catch ex As Exception
            _logger.ScriviLog("SelezionaTutto: " & ex.Message)
        End Try
    End Sub

    Private Sub CopiaTesto()
        Try
            If Not HaSelezione() Then
                _sintesiVocale.LeggiTesto("Nessun testo selezionato")
                Return
            End If
            Dim estratto As String = OttieniTestoSelezionato()
            If Not String.IsNullOrEmpty(estratto) Then
                Clipboard.SetText(estratto)
                _sintesiVocale.LeggiTesto("Copiato negli appunti")
            End If
        Catch ex As Exception
            _logger.ScriviLog("CopiaTesto: " & ex.Message)
        End Try
    End Sub

    Private Sub TagliaTesto()
        Try
            If SolaLettura Then Return
            CopiaTesto()
            If HaSelezione() Then
                SalvaUndo()
                EliminaSelezione()
                _sintesiVocale.LeggiTesto("Tagliato")
            End If
        Catch ex As Exception
            _logger.ScriviLog("TagliaTesto: " & ex.Message)
        End Try
    End Sub

    Private Sub RichiediRidisegno()
        Try
            If Me.IsHandleCreated Then InvalidateRect(Me.Handle, IntPtr.Zero, False)
        Catch ex As Exception
            _logger.ScriviLog("RichiediRidisegno: " & ex.Message)
        End Try
    End Sub

    Private Sub IncollaTesto()
        Try
            If SolaLettura Then Return
            If Not Clipboard.ContainsText() Then Return
            Dim testo As String = Clipboard.GetText()
            If Not String.IsNullOrEmpty(testo) Then
                If HaSelezione() Then EliminaSelezione()
                InserisciTesto(testo)
                _sintesiVocale.LeggiTesto("Incollato")
            End If
        Catch ex As Exception
            _logger.ScriviLog("IncollaTesto: " & ex.Message)
        End Try
    End Sub

    Private Sub RicalcolaLayout()
        Try
            _glyphs.Clear()
            Dim larghezzaMax As Single = Me.ClientSize.Width - _padding * 2
            If larghezzaMax < 10 Then larghezzaMax = 10
            Dim x As Single = _padding
            Dim y As Single = _padding
            Dim altezzaRiga As Single = 0

            Dim glyphsRigaCorrente As New List(Of GlyphInfo)()

            Dim flushRiga As Action(Of Boolean) = Sub(nuovaRiga As Boolean)
                                                      For Each gl In glyphsRigaCorrente
                                                          Dim glMod As GlyphInfo = gl
                                                          glMod.Y = y
                                                          _glyphs.Add(glMod)
                                                      Next
                                                      glyphsRigaCorrente.Clear()
                                                      If nuovaRiga Then
                                                          y += altezzaRiga
                                                          x = _padding
                                                          altezzaRiga = 0
                                                      End If
                                                  End Sub

            Dim hdcMisura As IntPtr = GetDC(Me.Handle)
            Try
                For segIdx As Integer = 0 To _segmenti.Count - 1
                    Dim seg As SegmentoTesto = _segmenti(segIdx)
                    Dim hFont As IntPtr = CreaGDIFont(seg)
                    Dim oldFont As IntPtr = SelectObject(hdcMisura, hFont)

                    For charIdx As Integer = 0 To seg.Testo.Length - 1
                        Dim c As Char = seg.Testo(charIdx)
                        If c = vbLf OrElse c = vbCr Then
                            Dim sz As SIZE
                            GetTextExtentPoint32(hdcMisura, " ", 1, sz)
                            Dim gl As New GlyphInfo()
                            gl.Carattere = c
                            gl.X = x
                            gl.Y = y
                            gl.Larghezza = 0
                            gl.Altezza = sz.cy
                            gl.SegmentoIndex = segIdx
                            gl.CharIndexInSegmento = charIdx
                            If sz.cy > altezzaRiga Then altezzaRiga = sz.cy
                            glyphsRigaCorrente.Add(gl)
                            flushRiga(True)
                            Continue For
                        End If

                        Dim szC As SIZE
                        GetTextExtentPoint32(hdcMisura, c.ToString(), 1, szC)

                        If Multiriga AndAlso x + szC.cx > larghezzaMax + _padding AndAlso x > _padding Then
                            flushRiga(True)
                        End If

                        Dim glyph As New GlyphInfo()
                        glyph.Carattere = c
                        glyph.X = x
                        glyph.Y = y
                        glyph.Larghezza = szC.cx
                        glyph.Altezza = szC.cy
                        glyph.SegmentoIndex = segIdx
                        glyph.CharIndexInSegmento = charIdx
                        If szC.cy > altezzaRiga Then altezzaRiga = szC.cy
                        glyphsRigaCorrente.Add(glyph)
                        x += szC.cx
                    Next

                    SelectObject(hdcMisura, oldFont)
                    DeleteObject(hFont)
                Next
            Finally
                ReleaseDC(Me.Handle, hdcMisura)
            End Try

            If altezzaRiga = 0 Then altezzaRiga = CInt(_dimensioneDefault * 1.4)
            flushRiga(False)
            _totalHeight = CInt(y + altezzaRiga + _padding)
            _layoutValido = True
        Catch ex As Exception
            _logger.ScriviLog("RicalcolaLayout: " & ex.Message)
            _layoutValido = True
        End Try
    End Sub

    Private Function CreaGDIFont(seg As SegmentoTesto) As IntPtr
        Try
            Dim altezzaPx As Integer = CInt(seg.Dimensione * 96.0F / 72.0F)
            Dim peso As Integer = If(seg.Grassetto, FW_BOLD, FW_NORMAL)
            Dim corsivo As UInteger = If(seg.Corsivo, 1UI, 0UI)
            Dim sotto As UInteger = If(seg.Sottolineato, 1UI, 0UI)
            Return CreateFont(-altezzaPx, 0, 0, 0, peso, corsivo, sotto, 0, DEFAULT_CHARSET, OUT_DEFAULT_PRECIS, CLIP_DEFAULT_PRECIS, CLEARTYPE_QUALITY, DEFAULT_PITCH Or FF_DONTCARE, seg.NomeFont)
        Catch ex As Exception
            _logger.ScriviLog("CreaGDIFont: " & ex.Message)
            Return IntPtr.Zero
        End Try
    End Function

    Protected Overrides Sub WndProc(ByRef m As Message)
        Try
            If m.Msg = WM_PAINT Then
                DisegnaControllo()
                Return
            End If
            MyBase.WndProc(m)
        Catch ex As Exception
            _logger.ScriviLog("WndProc: " & ex.Message)
        End Try
    End Sub

    Private Sub DisegnaControllo()
        Try
            If Not Me.IsHandleCreated Then Return
            Dim ps As New PAINTSTRUCT()
            Dim hdc As IntPtr = BeginPaint(Me.Handle, ps)
            If hdc = IntPtr.Zero Then Return

            Try
                Dim brushSfondo As IntPtr = NativeMethods.CreateSolidBrush(ColorToGDI(Me.BackColor))
                Dim rcSfondo As New NativeMethods.RECT With {.left = 0, .top = 0, .right = Me.Width, .bottom = Me.Height}
                NativeMethods.FillRect(hdc, rcSfondo, brushSfondo)
                NativeMethods.DeleteObject(brushSfondo)

                If Not _layoutValido Then RicalcolaLayout()

                SetBkMode(hdc, TRANSPARENT)

                Dim da As Integer = If(HaSelezione(), Math.Min(_selStart, _selEnd), -1)
                Dim a As Integer = If(HaSelezione(), Math.Max(_selStart, _selEnd), -1)
                Dim posAssoluta As Integer = 0

                For segIdx As Integer = 0 To _segmenti.Count - 1
                    Dim seg As SegmentoTesto = _segmenti(segIdx)
                    Dim hFont As IntPtr = CreaGDIFont(seg)
                    Dim oldFont As IntPtr = SelectObject(hdc, hFont)
                    Dim coloreGDI As Integer = ColorToGDI(seg.Colore)
                    SetTextColor(hdc, coloreGDI)

                    For charIdx As Integer = 0 To seg.Testo.Length - 1
                        Dim glyphIdx As Integer = TrovaGlyph(segIdx, charIdx)
                        If glyphIdx < 0 Then
                            posAssoluta += 1
                            Continue For
                        End If
                        Dim gl As GlyphInfo = _glyphs(glyphIdx)
                        Dim yCorr As Integer = CInt(gl.Y) - _scrollY

                        If yCorr + gl.Altezza < 0 OrElse yCorr > Me.ClientSize.Height Then
                            posAssoluta += 1
                            Continue For
                        End If

                        If da >= 0 AndAlso posAssoluta >= da AndAlso posAssoluta < a Then
                            Dim brush As IntPtr = NativeMethods.CreateSolidBrush(ColorToGDI(WGTema.Selezione))
                            Dim rc As NativeMethods.RECT
                            rc.left = CInt(gl.X)
                            rc.top = yCorr
                            rc.right = CInt(gl.X + gl.Larghezza)
                            rc.bottom = yCorr + CInt(gl.Altezza)
                            NativeMethods.FillRect(hdc, rc, brush)
                            NativeMethods.DeleteObject(brush)
                            SetTextColor(hdc, ColorToGDI(System.Drawing.Color.White))
                        Else
                            SetTextColor(hdc, coloreGDI)
                        End If

                        If gl.Carattere <> vbLf AndAlso gl.Carattere <> vbCr Then
                            TextOut(hdc, CInt(gl.X), yCorr, gl.Carattere.ToString(), 1)
                        End If

                        posAssoluta += 1
                    Next

                    SelectObject(hdc, oldFont)
                    DeleteObject(hFont)
                Next

                AggiornaCaretVisivo()
            Finally
                EndPaint(Me.Handle, ps)
            End Try
        Catch ex As Exception
            _logger.ScriviLog("DisegnaControllo: " & ex.Message)
        End Try
    End Sub

    Private Function TrovaGlyph(segIdx As Integer, charIdx As Integer) As Integer
        Try
            For i As Integer = 0 To _glyphs.Count - 1
                If _glyphs(i).SegmentoIndex = segIdx AndAlso _glyphs(i).CharIndexInSegmento = charIdx Then
                    Return i
                End If
            Next
            Return -1
        Catch ex As Exception
            _logger.ScriviLog("TrovaGlyph: " & ex.Message)
            Return -1
        End Try
    End Function

    Private Sub AggiornaCaretVisivo()
        Try
            If Not Me.Focused Then Return
            Dim puntoC = PuntoCaretDaPosizione(_caretPos)
            SetCaretPos(puntoC.X, puntoC.Y - _scrollY)
        Catch ex As Exception
            _logger.ScriviLog("AggiornaCaretVisivo: " & ex.Message)
        End Try
    End Sub

    Private Function PuntoCaretDaPosizione(pos As Integer) As System.Drawing.Point
        Try
            If _glyphs.Count = 0 Then Return New System.Drawing.Point(_padding, _padding)
            Dim posAssoluta As Integer = 0
            For i As Integer = 0 To _glyphs.Count - 1
                If posAssoluta = pos Then
                    Return New System.Drawing.Point(CInt(_glyphs(i).X), CInt(_glyphs(i).Y))
                End If
                posAssoluta += 1
            Next
            Dim ultimo As GlyphInfo = _glyphs(_glyphs.Count - 1)
            Return New System.Drawing.Point(CInt(ultimo.X + ultimo.Larghezza), CInt(ultimo.Y))
        Catch ex As Exception
            _logger.ScriviLog("PuntoCaretDaPosizione: " & ex.Message)
            Return New System.Drawing.Point(_padding, _padding)
        End Try
    End Function

    Private Function PosizioneDaCoordinate(x As Integer, y As Integer) As Integer
        Try
            Dim yAss As Integer = y + _scrollY
            Dim miglioreDist As Double = Double.MaxValue
            Dim migliorePos As Integer = 0
            Dim posAssoluta As Integer = 0
            For i As Integer = 0 To _glyphs.Count - 1
                Dim gl As GlyphInfo = _glyphs(i)
                Dim cx As Double = gl.X + gl.Larghezza / 2.0
                Dim cy As Double = gl.Y + gl.Altezza / 2.0
                Dim dist As Double = Math.Sqrt((cx - x) ^ 2 + (cy - yAss) ^ 2)
                If dist < miglioreDist Then
                    miglioreDist = dist
                    migliorePos = posAssoluta
                End If
                posAssoluta += 1
            Next
            Return migliorePos
        Catch ex As Exception
            _logger.ScriviLog("PosizioneDaCoordinate: " & ex.Message)
            Return 0
        End Try
    End Function

    Protected Overrides Sub OnMouseDown(e As MouseEventArgs)
        Try
            MyBase.OnMouseDown(e)
            Me.Focus()
            Dim nuovaPos As Integer = PosizioneDaCoordinate(e.X, e.Y)
            _caretPos = nuovaPos
            _selStart = nuovaPos
            _selEnd = nuovaPos
            RichiediRidisegno()
            AggiornaCaretVisivo()
        Catch ex As Exception
            _logger.ScriviLog("OnMouseDown: " & ex.Message)
        End Try
    End Sub

    Protected Overrides Sub OnMouseMove(e As MouseEventArgs)
        Try
            MyBase.OnMouseMove(e)
            If e.Button = MouseButtons.Left Then
                _selEnd = PosizioneDaCoordinate(e.X, e.Y)
                _caretPos = _selEnd
                RichiediRidisegno()
                AggiornaCaretVisivo()
            End If
        Catch ex As Exception
            _logger.ScriviLog("OnMouseMove: " & ex.Message)
        End Try
    End Sub

    Protected Overrides Sub OnGotFocus(e As EventArgs)
        Try
            MyBase.OnGotFocus(e)
            Dim altezza As Integer = CInt(_dimensioneDefault * 96.0F / 72.0F) + 2
            CreateCaret(Me.Handle, IntPtr.Zero, 2, altezza)
            ShowCaret(Me.Handle)
            AggiornaCaretVisivo()
            Dim touk As WGTouk = WGTouk.GetInstance()
            If touk IsNot Nothing Then touk.RiassegnaControllo(Me.Handle)
        Catch ex As Exception
            _logger.ScriviLog("OnGotFocus: " & ex.Message)
        End Try
    End Sub

    Protected Overrides Sub OnLostFocus(e As EventArgs)
        Try
            MyBase.OnLostFocus(e)
            HideCaret(Me.Handle)
            DestroyCaret()
            RichiediRidisegno()
        Catch ex As Exception
            _logger.ScriviLog("OnLostFocus: " & ex.Message)
        End Try
    End Sub

    Protected Overrides Sub OnResize(e As EventArgs)
        Try
            MyBase.OnResize(e)
            _layoutValido = False
            RichiediRidisegno()
        Catch ex As Exception
            _logger.ScriviLog("OnResize: " & ex.Message)
        End Try
    End Sub

    Private Sub _tastiera_ShiftPremuto(sender As Object, e As EventArgs) Handles _tastiera.ShiftPremuto
        Try
            _shiftPremuto = True
        Catch ex As Exception
            _logger.ScriviLog("_tastiera_ShiftPremuto: " & ex.Message)
        End Try
    End Sub

    Private Sub _tastiera_ShiftRilasciato(sender As Object, e As EventArgs) Handles _tastiera.ShiftRilasciato
        Try
            _shiftPremuto = False
        Catch ex As Exception
            _logger.ScriviLog("_tastiera_ShiftRilasciato: " & ex.Message)
        End Try
    End Sub

    Private Sub _tastiera_AltPremuto(sender As Object, e As EventArgs) Handles _tastiera.AltPremuto
        Try
            _altPremuto = True
        Catch ex As Exception
            _logger.ScriviLog("_tastiera_AltPremuto: " & ex.Message)
        End Try
    End Sub

    Private Sub _tastiera_AltRilasciato(sender As Object, e As EventArgs) Handles _tastiera.AltRilasciato
        Try
            _altPremuto = False
        Catch ex As Exception
            _logger.ScriviLog("_tastiera_AltRilasciato: " & ex.Message)
        End Try
    End Sub

    Private Sub _tastiera_AltDestroPremuto(sender As Object, e As EventArgs) Handles _tastiera.AltDestroPremuto
        Try
            _altDestro = True
            _altDestroUsato = False
        Catch ex As Exception
            _logger.ScriviLog("_tastiera_AltDestroPremuto: " & ex.Message)
        End Try
    End Sub

    Private Sub _tastiera_AltDestroRilasciato(sender As Object, e As EventArgs) Handles _tastiera.AltDestroRilasciato
        Try
            If _altDestro AndAlso Not _shiftPremuto AndAlso Not _altDestroUsato Then
                RaiseEvent menudestroapertura(Me, EventArgs.Empty)
            End If
            _altDestro = False
            _altDestroUsato = False
        Catch ex As Exception
            _logger.ScriviLog("_tastiera_AltDestroRilasciato: " & ex.Message)
        End Try
    End Sub

    Private Sub _tastiera_ControlSinistroPremuto(sender As Object, e As EventArgs) Handles _tastiera.ControlSinistroPremuto
        Try
            _ctrlSinistro = True
        Catch ex As Exception
            _logger.ScriviLog("_tastiera_ControlSinistroPremuto: " & ex.Message)
        End Try
    End Sub

    Private Sub _tastiera_ControlSinistroRilasciato(sender As Object, e As EventArgs) Handles _tastiera.ControlSinistroRilasciato
        Try
            _ctrlSinistro = False
        Catch ex As Exception
            _logger.ScriviLog("_tastiera_ControlSinistroRilasciato: " & ex.Message)
        End Try
    End Sub

    Private Sub _tastiera_ControlDestroPremuto(sender As Object, e As EventArgs) Handles _tastiera.ControlDestroPremuto
        Try
            _ctrlDestro = True
        Catch ex As Exception
            _logger.ScriviLog("_tastiera_ControlDestroPremuto: " & ex.Message)
        End Try
    End Sub

    Private Sub _tastiera_ControlDestroRilasciato(sender As Object, e As EventArgs) Handles _tastiera.ControlDestroRilasciato
        Try
            _ctrlDestro = False
        Catch ex As Exception
            _logger.ScriviLog("_tastiera_ControlDestroRilasciato: " & ex.Message)
        End Try
    End Sub

    Private Sub _tastiera_KeyDown(ByRef tasto As Keys) Handles _tastiera.KeyDown
        Try
            Dim altDxGlobale As Boolean = _altDestro OrElse (_tastiera IsNot Nothing AndAlso _tastiera.AltDestroAttualmentePremuto)
            Dim tastoBaseGlobale As Keys = tasto And Keys.KeyCode
            If altDxGlobale Then
                _altDestroUsato = True
            End If

            If Not Me.Focused Then Return

            Dim ctrl As Boolean = _ctrlSinistro OrElse _ctrlDestro
            Dim shift As Boolean = _shiftPremuto
            Dim alt As Boolean = _altPremuto
            Dim altDx As Boolean = altDxGlobale
            Dim tastoBase As Keys = tastoBaseGlobale

            If tastoBase <> Keys.Up AndAlso tastoBase <> Keys.Down Then
                _confermatoLimiteVerticale = False
            End If

            If alt AndAlso Not altDx Then Return

            Select Case tastoBase
                Case Keys.Escape
                    RaiseEvent chiusuraAnnullata(Me, EventArgs.Empty)
                    tasto = Keys.None
                    Return
                Case Keys.Left
                    SpostaCaretSinistra(ctrl, shift)
                    tasto = Keys.None
                    Return
                Case Keys.Right
                    SpostaCaretDestra(ctrl, shift)
                    tasto = Keys.None
                    Return
                Case Keys.Up
                    SpostaCaretSu(shift)
                    tasto = Keys.None
                    Return
                Case Keys.Down
                    SpostaCaretGiu(shift)
                    tasto = Keys.None
                    Return
                Case Keys.Home
                    SpostaCaretInizio(ctrl, shift)
                    tasto = Keys.None
                    Return
                Case Keys.End
                    SpostaCaretFine(ctrl, shift)
                    tasto = Keys.None
                    Return
                Case Keys.Back
                    If ctrl Then
                        EliminaParolaPrecedente()
                    Else
                        EliminaIndietro()
                    End If
                    tasto = Keys.None
                    Return
                Case Keys.Delete
                    EliminaAvanti()
                    tasto = Keys.None
                    Return
                Case Keys.Enter
                    If Multiriga AndAlso Not SolaLettura Then
                        GestisciInvio()
                        tasto = Keys.None
                    End If
                    Return
                Case Keys.Z
                    If ctrl Then Annulla() : tasto = Keys.None : Return
                Case Keys.Y
                    If ctrl Then Ripristina() : tasto = Keys.None : Return
                Case Keys.A
                    If ctrl Then SelezionaTutto() : tasto = Keys.None : Return
                Case Keys.C
                    If ctrl Then CopiaTesto() : tasto = Keys.None : Return
                Case Keys.X
                    If ctrl Then TagliaTesto() : tasto = Keys.None : Return
                Case Keys.V
                    If ctrl Then IncollaTesto() : tasto = Keys.None : Return
            End Select

            If ctrl AndAlso Not altDx Then Return

            Dim strChar As String = ConvertiTasto(tasto)
            If Not String.IsNullOrEmpty(strChar) AndAlso Not Char.IsControl(strChar(0)) Then
                Dim c As Char = strChar(0)
                If shift AndAlso Char.IsLetter(c) Then c = Char.ToUpper(c)
                If Not SolaLettura Then
                    InserisciTesto(c.ToString())
                    GestisciEco(c)
                    _contatoreInvio = 0
                End If
                tasto = Keys.None
            End If
        Catch ex As Exception
            _logger.ScriviLog("KeyDown: " & ex.Message)
        End Try
    End Sub

    Private Sub GestisciInvio()
        Try
            Dim ora As DateTime = DateTime.Now
            If (ora - _ultimoInvio).TotalSeconds > 2.0 Then _contatoreInvio = 0
            _ultimoInvio = ora
            _contatoreInvio += 1
            If _contatoreInvio >= 3 AndAlso Not _domandaAperta Then
                _contatoreInvio = 0
                _domandaAperta = True
                If _domandaChiusura IsNot Nothing Then
                    RemoveHandler _domandaChiusura.rispostadomanda, AddressOf RispostaDomanda
                End If
                _domandaChiusura = New WGDomanda()
                AddHandler _domandaChiusura.rispostadomanda, AddressOf RispostaDomanda
                _domandaChiusura.avvia("Si vuole concludere la scrittura nel campo?")
            Else
                InserisciACapo()
                _sintesiVocale.LeggiTesto("Nuova riga")
            End If
        Catch ex As Exception
            _logger.ScriviLog("GestisciInvio: " & ex.Message)
        End Try
    End Sub

    Private Sub RispostaDomanda(risposta As DialogResult)
        Try
            _domandaAperta = False
            If risposta = DialogResult.Yes Then
                RaiseEvent chiusuraConfermata(Me, EventArgs.Empty)
            End If
        Catch ex As Exception
            _logger.ScriviLog("RispostaDomanda: " & ex.Message)
        End Try
    End Sub

    Private Sub SpostaCaretSinistra(ctrl As Boolean, shift As Boolean)
        Try
            Dim vecchiaPos As Integer = _caretPos
            If ctrl Then
                _caretPos = TrovaInizioParolaPrecedente(_caretPos)
            Else
                If _caretPos > 0 Then _caretPos -= 1
            End If
            GestisciSelezioneDopoSpostamento(shift, vecchiaPos)
            LeggeCarattereCorrente()
            RichiediRidisegno()
            AggiornaCaretVisivo()
        Catch ex As Exception
            _logger.ScriviLog("SpostaCaretSinistra: " & ex.Message)
        End Try
    End Sub

    Private Sub SpostaCaretDestra(ctrl As Boolean, shift As Boolean)
        Try
            Dim vecchiaPos As Integer = _caretPos
            Dim tot As Integer = TotaleLunghezza()
            If ctrl Then
                _caretPos = TrovaInizioParolaSuccessiva(_caretPos)
            Else
                If _caretPos < tot Then _caretPos += 1
            End If
            GestisciSelezioneDopoSpostamento(shift, vecchiaPos)
            LeggeCarattereCorrente()
            RichiediRidisegno()
            AggiornaCaretVisivo()
        Catch ex As Exception
            _logger.ScriviLog("SpostaCaretDestra: " & ex.Message)
        End Try
    End Sub

    Private Sub SpostaCaretSu(shift As Boolean)
        Try
            Dim vecchiaPos As Integer = _caretPos
            Dim puntoC As System.Drawing.Point = PuntoCaretDaPosizione(_caretPos)
            Dim altezzaRiga As Integer = CInt(_dimensioneDefault * 96.0F / 72.0F)
            Dim nuovoPunto As New System.Drawing.Point(puntoC.X, puntoC.Y - altezzaRiga)
            If nuovoPunto.Y < _padding Then
                If _confermatoLimiteVerticale AndAlso _direzioneLimiteConfermata = -1 Then
                    _confermatoLimiteVerticale = False
                    _sintesiVocale.LeggiTesto("Inizio campo")
                Else
                    _confermatoLimiteVerticale = True
                    _direzioneLimiteConfermata = -1
                    LeggeRigaCorrente()
                End If
                Return
            End If
            _confermatoLimiteVerticale = False
            _caretPos = PosizioneDaCoordinate(nuovoPunto.X, nuovoPunto.Y)
            GestisciSelezioneDopoSpostamento(shift, vecchiaPos)
            LeggeRigaCorrente()
            RichiediRidisegno()
            AggiornaCaretVisivo()
        Catch ex As Exception
            _logger.ScriviLog("SpostaCaretSu: " & ex.Message)
        End Try
    End Sub

    Private Sub SpostaCaretGiu(shift As Boolean)
        Try
            Dim vecchiaPos As Integer = _caretPos
            Dim puntoC As System.Drawing.Point = PuntoCaretDaPosizione(_caretPos)
            Dim altezzaRiga As Integer = CInt(_dimensioneDefault * 96.0F / 72.0F)
            Dim nuovoPunto As New System.Drawing.Point(puntoC.X, puntoC.Y + altezzaRiga + 2)
            If nuovoPunto.Y > _totalHeight Then
                If _confermatoLimiteVerticale AndAlso _direzioneLimiteConfermata = 1 Then
                    _confermatoLimiteVerticale = False
                    _sintesiVocale.LeggiTesto("Fine campo")
                Else
                    _confermatoLimiteVerticale = True
                    _direzioneLimiteConfermata = 1
                    LeggeRigaCorrente()
                End If
                Return
            End If
            _confermatoLimiteVerticale = False
            _caretPos = PosizioneDaCoordinate(nuovoPunto.X, nuovoPunto.Y)
            GestisciSelezioneDopoSpostamento(shift, vecchiaPos)
            LeggeRigaCorrente()
            RichiediRidisegno()
            AggiornaCaretVisivo()
        Catch ex As Exception
            _logger.ScriviLog("SpostaCaretGiu: " & ex.Message)
        End Try
    End Sub

    Private Sub SpostaCaretInizio(ctrl As Boolean, shift As Boolean)
        Try
            Dim vecchiaPos As Integer = _caretPos
            If ctrl Then
                _caretPos = 0
            Else
                _caretPos = TrovaInizioRigaCorrente(_caretPos)
            End If
            GestisciSelezioneDopoSpostamento(shift, vecchiaPos)
            _sintesiVocale.LeggiTesto("Inizio riga")
            RichiediRidisegno()
            AggiornaCaretVisivo()
        Catch ex As Exception
            _logger.ScriviLog("SpostaCaretInizio: " & ex.Message)
        End Try
    End Sub

    Private Sub SpostaCaretFine(ctrl As Boolean, shift As Boolean)
        Try
            Dim vecchiaPos As Integer = _caretPos
            If ctrl Then
                _caretPos = TotaleLunghezza()
            Else
                _caretPos = TrovaFineRigaCorrente(_caretPos)
            End If
            GestisciSelezioneDopoSpostamento(shift, vecchiaPos)
            _sintesiVocale.LeggiTesto("Fine riga")
            RichiediRidisegno()
            AggiornaCaretVisivo()
        Catch ex As Exception
            _logger.ScriviLog("SpostaCaretFine: " & ex.Message)
        End Try
    End Sub

    Private Sub GestisciSelezioneDopoSpostamento(shift As Boolean, vecchiaPos As Integer)
        Try
            If shift Then
                If _selStart = -1 Then _selStart = vecchiaPos
                _selEnd = _caretPos
            Else
                _selStart = -1
                _selEnd = -1
            End If
        Catch ex As Exception
            _logger.ScriviLog("GestisciSelezioneDopoSpostamento: " & ex.Message)
        End Try
    End Sub

    Private Sub EliminaParolaPrecedente()
        Try
            If SolaLettura Then Return
            If HaSelezione() Then
                SalvaUndo()
                EliminaSelezione()
                Return
            End If
            Dim nuovaPos As Integer = TrovaInizioParolaPrecedente(_caretPos)
            If nuovaPos = _caretPos Then Return
            _selStart = nuovaPos
            _selEnd = _caretPos
            SalvaUndo()
            EliminaSelezione()
        Catch ex As Exception
            _logger.ScriviLog("EliminaParolaPrecedente: " & ex.Message)
        End Try
    End Sub

    Private Function TrovaInizioParolaPrecedente(pos As Integer) As Integer
        Try
            Dim testo As String = TestoPiatto()
            If pos <= 0 Then Return 0
            Dim i As Integer = pos - 1
            While i > 0 AndAlso (Char.IsWhiteSpace(testo(i)) OrElse Char.IsPunctuation(testo(i)))
                i -= 1
            End While
            While i > 0 AndAlso Not Char.IsWhiteSpace(testo(i - 1)) AndAlso Not Char.IsPunctuation(testo(i - 1))
                i -= 1
            End While
            Return i
        Catch ex As Exception
            _logger.ScriviLog("TrovaInizioParolaPrecedente: " & ex.Message)
            Return 0
        End Try
    End Function

    Private Function TrovaInizioParolaSuccessiva(pos As Integer) As Integer
        Try
            Dim testo As String = TestoPiatto()
            Dim len As Integer = testo.Length
            If pos >= len Then Return len
            Dim i As Integer = pos
            While i < len AndAlso Not Char.IsWhiteSpace(testo(i)) AndAlso Not Char.IsPunctuation(testo(i))
                i += 1
            End While
            While i < len AndAlso (Char.IsWhiteSpace(testo(i)) OrElse Char.IsPunctuation(testo(i)))
                i += 1
            End While
            Return i
        Catch ex As Exception
            Return pos
        End Try
    End Function

    Private Function TrovaInizioRigaCorrente(pos As Integer) As Integer
        Try
            Dim testo As String = TestoPiatto()
            If pos <= 0 Then Return 0
            Dim i As Integer = pos - 1
            While i > 0 AndAlso testo(i - 1) <> vbLf
                i -= 1
            End While
            Return i
        Catch ex As Exception
            _logger.ScriviLog("TrovaInizioRigaCorrente: " & ex.Message)
            Return 0
        End Try
    End Function

    Private Function TrovaFineRigaCorrente(pos As Integer) As Integer
        Try
            Dim testo As String = TestoPiatto()
            Dim len As Integer = testo.Length
            Dim i As Integer = pos
            While i < len AndAlso testo(i) <> vbLf
                i += 1
            End While
            Return i
        Catch ex As Exception
            _logger.ScriviLog("TrovaFineRigaCorrente: " & ex.Message)
            Return TotaleLunghezza()
        End Try
    End Function

    Private Sub LeggeCarattereCorrente()
        Try
            Dim testo As String = TestoPiatto()
            If _caretPos >= 0 AndAlso _caretPos < testo.Length Then
                _sintesiVocale.LeggiTesto(TraduciCarattere(testo(_caretPos)))
            ElseIf _caretPos = testo.Length AndAlso testo.Length > 0 Then
                _sintesiVocale.LeggiTesto("Fine testo")
            End If
        Catch ex As Exception
            _logger.ScriviLog("LeggeCarattereCorrente: " & ex.Message)
        End Try
    End Sub

    Private Sub LeggeRigaCorrente()
        Try
            Dim testo As String = TestoPiatto()
            Dim inizioRiga As Integer = TrovaInizioRigaCorrente(_caretPos)
            Dim fineRiga As Integer = TrovaFineRigaCorrente(_caretPos)
            Dim riga As String = ""
            If inizioRiga >= 0 AndAlso fineRiga <= testo.Length AndAlso fineRiga >= inizioRiga Then
                riga = testo.Substring(inizioRiga, fineRiga - inizioRiga)
            End If
            If String.IsNullOrWhiteSpace(riga) Then
                _sintesiVocale.LeggiTesto("Riga vuota")
            Else
                _sintesiVocale.LeggiTesto(riga)
            End If
        Catch ex As Exception
            _logger.ScriviLog("LeggeRigaCorrente: " & ex.Message)
        End Try
    End Sub

    Private Sub GestisciEco(c As Char)
        Try
            Dim eco As String = _impostazioniComuni.EchoDigitazione
            If eco = "0" Then Return
            Dim leggiPunteggiatura As Boolean = _impostazioniComuni.LetturaPunteggiaturaScrittura
            Dim isSimbolo As Boolean = Char.IsPunctuation(c) OrElse Char.IsSymbol(c)

            If c = " "c Then
                Dim parolaLetta As String = ""
                If eco = "2" OrElse eco = "3" Then
                    Dim testoSpc As String = TestoPiatto()
                    Dim inizioSpc As Integer = TrovaInizioParolaPrecedente(_caretPos)
                    If _caretPos > inizioSpc AndAlso inizioSpc >= 0 AndAlso _caretPos <= testoSpc.Length Then
                        parolaLetta = testoSpc.Substring(inizioSpc, _caretPos - inizioSpc).Trim()
                    End If
                End If
                If eco = "2" AndAlso parolaLetta.Length > 0 Then
                    _sintesiVocale.LeggiTesto(parolaLetta)
                ElseIf eco = "3" AndAlso parolaLetta.Length > 0 Then
                    _sintesiVocale.LeggiTesto(parolaLetta & ". spazio", escludiPunteggiatura:=True)
                Else
                    _sintesiVocale.LeggiTesto(". spazio", escludiPunteggiatura:=True)
                End If
                Return
            End If

            If eco = "1" Then
                If isSimbolo AndAlso Not leggiPunteggiatura Then Return
                Dim tradotto As String = TraduciCarattere(c)
                If Not String.IsNullOrWhiteSpace(tradotto) Then _sintesiVocale.LeggiTesto(tradotto)
            ElseIf eco = "2" OrElse eco = "3" Then
                If Char.IsPunctuation(c) Then
                    Dim testo As String = TestoPiatto()
                    Dim inizio As Integer = TrovaInizioParolaPrecedente(_caretPos)
                    Dim parola As String = ""
                    If _caretPos > inizio AndAlso inizio >= 0 AndAlso _caretPos <= testo.Length Then
                        parola = testo.Substring(inizio, _caretPos - inizio).Trim()
                    End If
                    If parola.Length > 0 Then
                        If eco = "3" AndAlso leggiPunteggiatura Then
                            Dim tradSegno As String = TraduciCarattere(c)
                            If Not String.IsNullOrWhiteSpace(tradSegno) Then
                                _sintesiVocale.LeggiTesto(parola & " " & tradSegno)
                            Else
                                _sintesiVocale.LeggiTesto(parola)
                            End If
                        Else
                            _sintesiVocale.LeggiTesto(parola)
                        End If
                    ElseIf eco = "3" AndAlso leggiPunteggiatura Then
                        Dim trad As String = TraduciCarattere(c)
                        If Not String.IsNullOrWhiteSpace(trad) Then _sintesiVocale.LeggiTesto(trad)
                    End If
                ElseIf eco = "3" Then
                    If Char.IsLetter(c) AndAlso Char.IsUpper(c) Then
                        _sintesiVocale.LeggiTesto(c.ToString() & " maiuscola")
                    Else
                        If isSimbolo AndAlso Not leggiPunteggiatura Then Return
                        Dim tradotto As String = TraduciCarattere(c)
                        If Not String.IsNullOrWhiteSpace(tradotto) Then _sintesiVocale.LeggiTesto(tradotto)
                    End If
                End If
            End If
        Catch ex As Exception
            _logger.ScriviLog("GestisciEco: " & ex.Message)
        End Try
    End Sub

    Private Function SimboloAltGr(baseKey As Keys, shift As Boolean) As String
        Select Case baseKey
            Case Keys.Oem3, Keys.Oem1
                Return "@"
            Case Keys.Oem7
                Return "#"
            Case Keys.Oem5, Keys.OemOpenBrackets
                Return If(shift, "{", "[")
            Case Keys.Oemplus, Keys.OemCloseBrackets
                Return If(shift, "}", "]")
            Case Keys.E
                Return "€"
        End Select
        Return ""
    End Function

    Private Function SimboloAltGrDaCarattere(carattere As String, shift As Boolean) As String
        Select Case carattere
            Case "ò", "Ò"
                Return "@"
            Case "à", "À"
                Return "#"
            Case "è", "È"
                Return If(shift, "{", "[")
            Case "+"
                Return If(shift, "}", "]")
            Case "e", "E"
                Return "€"
        End Select
        Return ""
    End Function

    Private Function CaratterePianoTasto(virtualKey As UInteger) As String
        Try
            Dim keyboardStatePiano(255) As Byte
            If Not GetKeyboardState(keyboardStatePiano) Then Return ""
            For i As Integer = 0 To 255
                keyboardStatePiano(i) = 0
            Next
            keyboardStatePiano(&H14) = CByte(If(Control.IsKeyLocked(Keys.CapsLock), 1, 0))
            ' La tastiera usata per tradurre il tasto e' quella scelta in WinGabriel, non quella che Windows ha attiva.
            Return WGTastiera.CarattereDaLayoutScelto(virtualKey, keyboardStatePiano)
        Catch ex As Exception
            _logger.ScriviLog("CaratterePianoTasto: " & ex.Message)
            Return ""
        End Try
    End Function

    Private Function ConvertiTasto(tasto As Keys) As String
        Try
            Dim virtualKey As UInteger = CUInt(tasto And Keys.KeyCode)

            ' Simboli italiani sopra i numeri solo con la tastiera italiana scelta in WinGabriel (vedi WGCampoMovimento
            ' per lo stesso controllo): con un'altra lingua di tastiera si usa CarattereDaLayoutScelto piu' sotto.
            If _shiftPremuto AndAlso virtualKey >= 48 AndAlso virtualKey <= 57 AndAlso WGTastiera.TastieraItalianaAttiva() Then
                Select Case virtualKey
                    Case 49 : Return "!"
                    Case 50 : Return """"
                    Case 51 : Return "£"
                    Case 52 : Return "$"
                    Case 53 : Return "%"
                    Case 54 : Return "&"
                    Case 55 : Return "/"
                    Case 56 : Return "("
                    Case 57 : Return ")"
                    Case 48 : Return "="
                End Select
            End If

            If virtualKey >= 16 AndAlso virtualKey <= 18 Then Return ""
            If virtualKey >= 33 AndAlso virtualKey <= 40 Then Return ""
            If virtualKey = 8 OrElse virtualKey = 9 OrElse virtualKey = 13 OrElse virtualKey = 27 OrElse virtualKey = 46 Then Return ""
            Dim keyboardState(255) As Byte
            If Not GetKeyboardState(keyboardState) Then Return ""
            Dim altDestroReale As Boolean = _altDestro OrElse (_tastiera IsNot Nothing AndAlso _tastiera.AltDestroAttualmentePremuto)

            ' Le combinazioni AltGr della tastiera italiana (@ # [ ] { } €) solo con quella tastiera scelta in
            ' WinGabriel: con un'altra lingua di tastiera si passa direttamente alla simulazione sotto
            ' (ctrlPerAltGr/altPerAltGr con CarattereDaLayoutScelto), che da' gia' da sola il carattere AltGr giusto.
            If altDestroReale AndAlso WGTastiera.TastieraItalianaAttiva() Then
                Dim risultatoAltGr As String = SimboloAltGr(tasto And Keys.KeyCode, _shiftPremuto)
                Dim caratterePiano As String = ""
                If risultatoAltGr = "" Then
                    caratterePiano = CaratterePianoTasto(virtualKey)
                    risultatoAltGr = SimboloAltGrDaCarattere(caratterePiano, _shiftPremuto)
                End If
                If wgcontrols.WGTastiera.LoggingVerboso Then
                End If
                If risultatoAltGr <> "" Then Return risultatoAltGr
            End If

            Dim ctrlPerAltGr As Boolean = _ctrlSinistro OrElse _ctrlDestro OrElse altDestroReale
            Dim altPerAltGr As Boolean = _altPremuto OrElse altDestroReale
            keyboardState(&H10) = CByte(If(_shiftPremuto, &H80, 0))
            keyboardState(&H11) = CByte(If(ctrlPerAltGr, &H80, 0))
            keyboardState(&H12) = CByte(If(altPerAltGr, &H80, 0))
            keyboardState(&HA2) = CByte(If(_ctrlSinistro, &H80, 0))
            keyboardState(&HA3) = CByte(If(_ctrlDestro OrElse altDestroReale, &H80, 0))
            keyboardState(&HA4) = CByte(If(_altPremuto, &H80, 0))
            keyboardState(&HA5) = CByte(If(altDestroReale, &H80, 0))
            keyboardState(&H14) = CByte(If(Control.IsKeyLocked(Keys.CapsLock), 1, 0))
            ' La tastiera usata per tradurre il tasto (anche AltGr) e' quella scelta in WinGabriel, non quella di Windows.
            Dim risultatoGenerico As String = WGTastiera.CarattereDaLayoutScelto(virtualKey, keyboardState)
            If risultatoGenerico <> "" Then Return risultatoGenerico
            If virtualKey = 32 Then Return " "
            Return ""
        Catch ex As Exception
            _logger.ScriviLog("ConvertiTasto: " & ex.Message)
            Return ""
        End Try
    End Function

    Private Function ColorToGDI(c As System.Drawing.Color) As Integer
        Try
            Return c.R Or (c.G << 8) Or (c.B << 16)
        Catch ex As Exception
            _logger.ScriviLog("ColorToGDI: " & ex.Message)
            Return 0
        End Try
    End Function

    Private Function TraduciCarattere(c As Char) As String
        Try
            Return WGTastiera.NomeSimbolo(c)
        Catch ex As Exception
            _logger.ScriviLog("TraduciCarattere: " & ex.Message)
            Return c.ToString()
        End Try
    End Function

    Protected Overrides Sub Dispose(disposing As Boolean)
        Try
            RemoveHandler WGTema.CambiatoTema, AddressOf OnCambiatoTema
            If disposing Then
                If _tastiera IsNot Nothing Then
                    RemoveHandler _tastiera.KeyDown, AddressOf _tastiera_KeyDown
                    RemoveHandler _tastiera.ShiftPremuto, AddressOf _tastiera_ShiftPremuto
                    RemoveHandler _tastiera.ShiftRilasciato, AddressOf _tastiera_ShiftRilasciato
                    RemoveHandler _tastiera.AltPremuto, AddressOf _tastiera_AltPremuto
                    RemoveHandler _tastiera.AltRilasciato, AddressOf _tastiera_AltRilasciato
                    RemoveHandler _tastiera.AltDestroPremuto, AddressOf _tastiera_AltDestroPremuto
                    RemoveHandler _tastiera.AltDestroRilasciato, AddressOf _tastiera_AltDestroRilasciato
                    RemoveHandler _tastiera.ControlSinistroPremuto, AddressOf _tastiera_ControlSinistroPremuto
                    RemoveHandler _tastiera.ControlSinistroRilasciato, AddressOf _tastiera_ControlSinistroRilasciato
                    RemoveHandler _tastiera.ControlDestroPremuto, AddressOf _tastiera_ControlDestroPremuto
                    RemoveHandler _tastiera.ControlDestroRilasciato, AddressOf _tastiera_ControlDestroRilasciato
                    _tastiera = Nothing
                End If
                If _domandaChiusura IsNot Nothing Then
                    RemoveHandler _domandaChiusura.rispostadomanda, AddressOf RispostaDomanda
                    _domandaChiusura = Nothing
                End If
            End If
        Catch ex As Exception
            _logger.ScriviLog("Dispose: " & ex.Message)
        End Try
        MyBase.Dispose(disposing)
    End Sub

    Private Class NativeMethods
        <StructLayout(LayoutKind.Sequential)>
        Public Structure RECT
            Public left As Integer
            Public top As Integer
            Public right As Integer
            Public bottom As Integer
        End Structure

        <DllImport("gdi32.dll")>
        Public Shared Function CreateSolidBrush(crColor As Integer) As IntPtr
        End Function

        <DllImport("user32.dll")>
        Public Shared Function FillRect(hDC As IntPtr, ByRef lprc As RECT, hbr As IntPtr) As Integer
        End Function

        <DllImport("gdi32.dll")>
        Public Shared Function DeleteObject(hObject As IntPtr) As Boolean
        End Function
    End Class
End Class