Imports System.Windows.Forms
Imports System.Collections.Generic
Imports System.ComponentModel
Imports System.Runtime.InteropServices
Imports System.Text
Imports System.Drawing
Imports System.Threading

Public Class WGListaView
    Inherits Control
    Implements IWGElementoTattile

    <StructLayout(LayoutKind.Sequential)>
    Private Structure RECT
        Public Left As Integer
        Public Top As Integer
        Public Right As Integer
        Public Bottom As Integer
    End Structure

    <StructLayout(LayoutKind.Sequential)>
    Private Structure SCROLLINFO
        Public cbSize As Integer
        Public fMask As Integer
        Public nMin As Integer
        Public nMax As Integer
        Public nPage As Integer
        Public nPos As Integer
        Public nTrackPos As Integer
    End Structure

    <StructLayout(LayoutKind.Sequential)>
    Private Structure PAINTSTRUCT
        Public hdc As IntPtr
        Public fErase As Boolean
        Public rcPaint As RECT
        Public fRestore As Boolean
        Public fIncUpdate As Boolean
        <MarshalAs(UnmanagedType.ByValArray, SizeConst:=32)>
        Public rgbReserved() As Byte
    End Structure

    <StructLayout(LayoutKind.Sequential)>
    Private Structure TRIVERTEX
        Public x As Integer
        Public y As Integer
        Public Red As UShort
        Public Green As UShort
        Public Blue As UShort
        Public Alpha As UShort
    End Structure

    <StructLayout(LayoutKind.Sequential)>
    Private Structure GRADIENT_RECT
        Public UpperLeft As UInteger
        Public LowerRight As UInteger
    End Structure

    <DllImport("user32.dll", SetLastError:=True)>
    Private Shared Function IsWindowEnabled(ByVal hWnd As IntPtr) As Boolean
    End Function

    <DllImport("user32.dll", SetLastError:=True)>
    Private Shared Function EnableWindow(ByVal hWnd As IntPtr, ByVal bEnable As Boolean) As Boolean
    End Function

    <DllImport("user32.dll", SetLastError:=True)>
    Private Shared Function GetWindowRect(ByVal hWnd As IntPtr, ByRef lpRect As RECT) As Boolean
    End Function

    <DllImport("user32.dll", SetLastError:=True)>
    Private Shared Function SetWindowPos(ByVal hWnd As IntPtr, ByVal hWndInsertAfter As IntPtr, ByVal X As Integer, ByVal Y As Integer, ByVal cx As Integer, ByVal cy As Integer, ByVal uFlags As UInteger) As Boolean
    End Function

    <DllImport("user32.dll", SetLastError:=True)>
    Private Shared Function MapWindowPoints(ByVal hWndFrom As IntPtr, ByVal hWndTo As IntPtr, ByRef lpPoints As RECT, ByVal cPoints As UInteger) As Integer
    End Function

    <DllImport("user32.dll", SetLastError:=True)>
    Private Shared Function GetParent(ByVal hWnd As IntPtr) As IntPtr
    End Function

    <DllImport("user32.dll", SetLastError:=True)>
    Private Shared Function UpdateWindow(ByVal hWnd As IntPtr) As Boolean
    End Function

    <DllImport("user32.dll")>
    Private Shared Function BeginPaint(ByVal hWnd As IntPtr, ByRef lpPaint As PAINTSTRUCT) As IntPtr
    End Function

    <DllImport("user32.dll")>
    Private Shared Function EndPaint(ByVal hWnd As IntPtr, ByRef lpPaint As PAINTSTRUCT) As Boolean
    End Function

    <DllImport("gdi32.dll")>
    Private Shared Function CreateCompatibleDC(ByVal hDC As IntPtr) As IntPtr
    End Function

    <DllImport("gdi32.dll")>
    Private Shared Function CreateCompatibleBitmap(ByVal hDC As IntPtr, ByVal nWidth As Integer, ByVal nHeight As Integer) As IntPtr
    End Function

    <DllImport("gdi32.dll")>
    Private Shared Function SelectObject(ByVal hDC As IntPtr, ByVal hObject As IntPtr) As IntPtr
    End Function

    <DllImport("gdi32.dll")>
    Private Shared Function DeleteObject(ByVal hObject As IntPtr) As Boolean
    End Function

    <DllImport("gdi32.dll")>
    Private Shared Function DeleteDC(ByVal hDC As IntPtr) As Boolean
    End Function

    <DllImport("gdi32.dll")>
    Private Shared Function BitBlt(ByVal hDestDC As IntPtr, ByVal x As Integer, ByVal y As Integer, ByVal nWidth As Integer, ByVal nHeight As Integer, ByVal hSrcDC As IntPtr, ByVal xSrc As Integer, ByVal ySrc As Integer, ByVal dwRop As Integer) As Boolean
    End Function

    <DllImport("gdi32.dll")>
    Private Shared Function StretchBlt(ByVal hDestDC As IntPtr, ByVal x As Integer, ByVal y As Integer, ByVal nWidth As Integer, ByVal nHeight As Integer, ByVal hSrcDC As IntPtr, ByVal xSrc As Integer, ByVal ySrc As Integer, ByVal nSrcWidth As Integer, ByVal nSrcHeight As Integer, ByVal dwRop As Integer) As Boolean
    End Function

    <DllImport("gdi32.dll")>
    Private Shared Function SetStretchBltMode(ByVal hdc As IntPtr, ByVal mode As Integer) As Integer
    End Function

    <DllImport("msimg32.dll")>
    Private Shared Function GradientFill(ByVal hdc As IntPtr, ByVal pVertex As TRIVERTEX(), ByVal nVertex As UInteger, ByVal pMesh As GRADIENT_RECT(), ByVal nMesh As UInteger, ByVal ulMode As UInteger) As Boolean
    End Function

    <DllImport("gdi32.dll")>
    Private Shared Function CreateSolidBrush(ByVal crColor As Integer) As IntPtr
    End Function

    <DllImport("user32.dll")>
    Private Shared Function FillRect(ByVal hDC As IntPtr, ByRef lprc As RECT, ByVal hbr As IntPtr) As Integer
    End Function

    <DllImport("user32.dll")>
    Private Shared Function FrameRect(ByVal hDC As IntPtr, ByRef lprc As RECT, ByVal hbr As IntPtr) As Integer
    End Function

    <DllImport("gdi32.dll")>
    Private Shared Function SetTextColor(ByVal hDC As IntPtr, ByVal crColor As Integer) As Integer
    End Function

    <DllImport("gdi32.dll")>
    Private Shared Function SetBkMode(ByVal hDC As IntPtr, ByVal iBkMode As Integer) As Integer
    End Function

    <DllImport("user32.dll", CharSet:=CharSet.Unicode)>
    Private Shared Function DrawText(ByVal hDC As IntPtr, ByVal lpString As String, ByVal nCount As Integer, ByRef lpRect As RECT, ByVal uFormat As UInteger) As Integer
    End Function

    <DllImport("user32.dll")>
    Private Shared Function DrawEdge(ByVal hdc As IntPtr, ByRef qrc As RECT, ByVal edge As Integer, ByVal grfFlags As Integer) As Boolean
    End Function

    <DllImport("gdi32.dll", EntryPoint:="CreateFontW", CharSet:=CharSet.Unicode)>
    Private Shared Function CreateFontPtr(ByVal nHeight As Integer, ByVal nWidth As Integer, ByVal nEscapement As Integer, ByVal nOrientation As Integer, ByVal fnWeight As Integer, ByVal fdwItalic As UInteger, ByVal fdwUnderline As UInteger, ByVal fdwStrikeOut As UInteger, ByVal fdwCharSet As UInteger, ByVal fdwOutputPrecision As UInteger, ByVal fdwClipPrecision As UInteger, ByVal fdwQuality As UInteger, ByVal fdwPitchAndFamily As UInteger, ByVal lpszFace As IntPtr) As IntPtr
    End Function

    <DllImport("user32.dll")>
    Private Shared Function SetScrollInfo(ByVal hWnd As IntPtr, ByVal nBar As Integer, ByRef lpsi As SCROLLINFO, ByVal redraw As Boolean) As Integer
    End Function

    <DllImport("user32.dll")>
    Private Shared Function ShowScrollBar(ByVal hWnd As IntPtr, ByVal wBar As Integer, ByVal bShow As Boolean) As Boolean
    End Function

    <DllImport("user32.dll")>
    Private Shared Function GetFocus() As IntPtr
    End Function

    <DllImport("user32.dll")>
    Private Shared Function SetFocus(ByVal hWnd As IntPtr) As IntPtr
    End Function

    <DllImport("user32.dll")>
    Private Shared Function InvalidateRect(ByVal hWnd As IntPtr, ByVal lpRect As IntPtr, ByVal bErase As Boolean) As Boolean
    End Function

    <DllImport("gdi32.dll")>
    Private Shared Function MoveToEx(ByVal hdc As IntPtr, ByVal x As Integer, ByVal y As Integer, ByVal lpPoint As IntPtr) As Boolean
    End Function

    <DllImport("gdi32.dll")>
    Private Shared Function LineTo(ByVal hdc As IntPtr, ByVal x As Integer, ByVal y As Integer) As Boolean
    End Function

    <DllImport("gdi32.dll")>
    Private Shared Function CreatePen(ByVal fnPenStyle As Integer, ByVal nWidth As Integer, ByVal crColor As Integer) As IntPtr
    End Function

    <DllImport("gdi32.dll")>
    Private Shared Function GetDeviceCaps(ByVal hdc As IntPtr, ByVal nIndex As Integer) As Integer
    End Function

    <DllImport("user32.dll")>
    Private Shared Function GetDC(ByVal hWnd As IntPtr) As IntPtr
    End Function

    <DllImport("user32.dll")>
    Private Shared Function ReleaseDC(ByVal hWnd As IntPtr, ByVal hDC As IntPtr) As Integer
    End Function

    <DllImport("user32.dll")>
    Private Shared Function GetForegroundWindow() As IntPtr
    End Function

    <DllImport("user32.dll")>
    Private Shared Function IsWindowVisible(ByVal hWnd As IntPtr) As Boolean
    End Function

    <DllImport("user32.dll")>
    Private Shared Function ShowWindow(ByVal hWnd As IntPtr, ByVal nCmdShow As Integer) As Boolean
    End Function

    <DllImport("user32.dll")>
    Private Shared Function PostMessage(ByVal hWnd As IntPtr, ByVal Msg As Integer, ByVal wParam As IntPtr, ByVal lParam As IntPtr) As Boolean
    End Function

    <DllImport("user32.dll", SetLastError:=True)>
    Private Shared Function GetAncestor(ByVal hwnd As IntPtr, ByVal gaFlags As UInteger) As IntPtr
    End Function

    <DllImport("user32.dll")>
    Private Shared Function GetSystemMetrics(ByVal nIndex As Integer) As Integer
    End Function

    <DllImport("user32.dll")>
    Private Shared Function GetWindowThreadProcessId(ByVal hWnd As IntPtr, ByVal lpdwProcessId As IntPtr) As UInteger
    End Function

    <DllImport("kernel32.dll")>
    Private Shared Function GetCurrentThreadId() As UInteger
    End Function

    <DllImport("user32.dll")>
    Private Shared Function AttachThreadInput(ByVal idAttach As UInteger, ByVal idAttachTo As UInteger, ByVal fAttach As Boolean) As Boolean
    End Function

    <DllImport("user32.dll")>
    Private Shared Function SetForegroundWindow(ByVal hWnd As IntPtr) As Boolean
    End Function

    Private WithEvents _tastiera As WGTastiera
    Private _touk As WGTouk
    Private _isHookActive As Boolean = False
    Private _ctrlPremuto As Boolean = False

    Private Const WM_PAINT As Integer = &HF
    Private Const WM_ERASEBKGND As Integer = &H14
    Private Const WM_SIZE As Integer = &H5
    Private Const WM_VSCROLL As Integer = &H115
    Private Const WM_MOUSEWHEEL As Integer = &H20A
    Private Const WM_LBUTTONDOWN As Integer = &H201
    Private Const WM_DESTROY As Integer = &H2
    Private Const WM_SETFOCUS As Integer = &H7
    Private Const WM_KILLFOCUS As Integer = &H8
    Private Const WM_SHOWWINDOW As Integer = &H18
    Private Const WM_KEYDOWN As Integer = &H100
    Private Const WM_SYSKEYDOWN As Integer = &H104
    Private Const WM_USER As Integer = &H400
    Private Const WM_SEARCH_RESULT As Integer = WM_USER + 100
    Private Const WM_SEARCH_NOTFOUND As Integer = WM_USER + 102

    Private Const SB_VERT As Integer = 1
    Private Const SIF_ALL As Integer = &H17
    Private Const SB_LINEUP As Integer = 0
    Private Const SB_LINEDOWN As Integer = 1
    Private Const SB_PAGEUP As Integer = 2
    Private Const SB_PAGEDOWN As Integer = 3
    Private Const SB_THUMBTRACK As Integer = 5
    Private Const SB_THUMBPOSITION As Integer = 4

    Private Const WS_VSCROLL As Integer = &H200000
    Private Const WS_BORDER As Integer = &H800000
    Private Const SRCCOPY As Integer = &HCC0020
    Private Const BDR_RAISEDINNER As Integer = &H4
    Private Const BDR_SUNKENOUTER As Integer = &H2
    Private Const BF_RECT As Integer = &HF
    Private Const GRADIENT_FILL_RECT_V As UInteger = &H1
    Private Const PS_SOLID As Integer = 0
    Private Const HALFTONE As Integer = 4
    Private Const LOGPIXELSY As Integer = 90
    Private Const SM_CYSCREEN As Integer = 1
    Private Const SW_HIDE As Integer = 0
    Private Const SW_SHOW As Integer = 5
    Private Const SWP_NOSIZE As UInteger = &H1
    Private Const SWP_NOMOVE As UInteger = &H2
    Private Const SWP_NOZORDER As UInteger = &H4
    Private Const GA_ROOT As UInteger = 2
    Private Const DT_CENTER As UInteger = &H1
    Private Const DT_SINGLELINE As UInteger = &H20
    Private Const DT_VCENTER As UInteger = &H4
    Private Const DT_END_ELLIPSIS As UInteger = &H8000
    Private Const DT_NOPREFIX As UInteger = &H800
    Private Const DT_WORDBREAK As UInteger = &H10
    Private Const DT_CALCRECT As UInteger = &H400

    Private logger As New wgcontrols.Logger()
    Private config As New WGConfig()
    Private lettura As sintesivocale = sintesivocale.GetInstance()
    Private _suoni As New WGSuoni()

    Friend _syncLock As New Object()

    Private Class SearchData
        Public ItemsText As String()
        Public SearchText As String
        Public StartIndex As Integer
        Public OwnerHandle As IntPtr
        Public SoloPrimaLettera As Boolean
    End Class

    Private Sub ThreadCercaFunc(lpParam As Object)
        Try
            Dim data As SearchData = CType(lpParam, SearchData)
            Dim count As Integer = data.ItemsText.Length
            If count = 0 Then Return
            Dim trovato As Boolean = False
            For i As Integer = 0 To count - 1
                Dim idx As Integer = (If(data.StartIndex + 1 < count, data.StartIndex + 1, 0) + i) Mod count
                Dim txt As String = data.ItemsText(idx)
                Dim match As Boolean = If(data.SoloPrimaLettera,
                    txt.StartsWith(data.SearchText, StringComparison.OrdinalIgnoreCase),
                    txt.IndexOf(data.SearchText, StringComparison.OrdinalIgnoreCase) >= 0)
                If match Then
                    PostMessage(data.OwnerHandle, WM_SEARCH_RESULT, New IntPtr(idx), IntPtr.Zero)
                    trovato = True
                    Exit For
                End If
            Next
            If Not trovato Then
                PostMessage(data.OwnerHandle, WM_SEARCH_NOTFOUND, IntPtr.Zero, IntPtr.Zero)
            End If
        Catch ex As Exception
            logger.ScriviLog($"ThreadCercaFunc: {ex.ToString()}")
        End Try
    End Sub

    Public Class WGObjectCollection
        Implements IList(Of Object)

        Private _owner As WGListaView
        Private _list As New List(Of Object)()

        Public Sub New(owner As WGListaView)
            _owner = owner
        End Sub

        Public Sub Add(item As Object) Implements ICollection(Of Object).Add
            Add(item, False)
        End Sub

        Public Sub Add(item As Object, selezionato As Boolean)
            Try
                SyncLock _owner._syncLock
                    _owner.SegnalaCaricamentoInCorso()
                    _list.Add(item)
                    _owner.InvalidaCacheAltezze()
                    If selezionato Then
                        If _owner._selectionMode = SelectionMode.One Then
                            _owner._selectedIndices.Clear()
                        End If
                        _owner._selectedIndices.Add(_list.Count - 1)
                        _owner._selectedIndex = _list.Count - 1
                    End If
                    _owner.InvalidateSicuro()
                End SyncLock
            Catch ex As Exception
                _owner.logger.ScriviLog($"WGObjectCollection.Add: {ex.ToString()}")
            End Try
        End Sub

        Public Sub AddRange(items() As Object)
            Try
                If items Is Nothing OrElse items.Length = 0 Then Return
                SyncLock _owner._syncLock
                    _owner.SegnalaCaricamentoInCorso()
                    _list.AddRange(items)
                    _owner.InvalidaCacheAltezze()
                    _owner.InvalidateSicuro()
                End SyncLock
            Catch ex As Exception
                _owner.logger.ScriviLog($"WGObjectCollection.AddRange: {ex.ToString()}")
            End Try
        End Sub

        Public Sub Clear() Implements ICollection(Of Object).Clear
            Try
                SyncLock _owner._syncLock
                    _owner.SalvaStatoPrimaDiClear()
                    _list.Clear()
                    _owner.InvalidaCacheAltezze()
                    _owner.ResetPuntatori()
                    _owner.AggiornaConteggi()
                    _owner.SegnalaSvuotamento()
                    _owner.InvalidateSicuro()
                End SyncLock
            Catch ex As Exception
                _owner.logger.ScriviLog($"WGObjectCollection.Clear: {ex.ToString()}")
            End Try
        End Sub

        Public Function Contains(item As Object) As Boolean Implements ICollection(Of Object).Contains
            Try
                SyncLock _owner._syncLock
                    Return _list.Contains(item)
                End SyncLock
            Catch ex As Exception
                _owner.logger.ScriviLog($"WGObjectCollection.Contains: {ex.ToString()}")
                Return False
            End Try
        End Function

        Public Sub CopyTo(array() As Object, arrayIndex As Integer) Implements ICollection(Of Object).CopyTo
            Try
                SyncLock _owner._syncLock
                    _list.CopyTo(array, arrayIndex)
                End SyncLock
            Catch ex As Exception
                _owner.logger.ScriviLog($"WGObjectCollection.CopyTo: {ex.ToString()}")
            End Try
        End Sub

        Public ReadOnly Property Count As Integer Implements ICollection(Of Object).Count
            Get
                Try
                    SyncLock _owner._syncLock
                        Return _list.Count
                    End SyncLock
                Catch ex As Exception
                    _owner.logger.ScriviLog($"WGObjectCollection.Count: {ex.ToString()}")
                    Return 0
                End Try
            End Get
        End Property

        Public ReadOnly Property IsReadOnly As Boolean Implements ICollection(Of Object).IsReadOnly
            Get
                Return False
            End Get
        End Property

        Public Function Remove(item As Object) As Boolean Implements ICollection(Of Object).Remove
            Try
                SyncLock _owner._syncLock
                    Dim index As Integer = _list.IndexOf(item)
                    If index >= 0 Then
                        RemoveAt(index)
                        Return True
                    End If
                    Return False
                End SyncLock
            Catch ex As Exception
                _owner.logger.ScriviLog($"WGObjectCollection.Remove: {ex.ToString()}")
                Return False
            End Try
        End Function

        Public Sub RemoveAt(index As Integer) Implements IList(Of Object).RemoveAt
            Try
                SyncLock _owner._syncLock
                    If index >= 0 AndAlso index < _list.Count Then
                        _list.RemoveAt(index)
                        _owner.InvalidaCacheAltezze()
                        _owner.GestisciRimozione()
                        _owner.AggiornaConteggi()
                        _owner.InvalidateSicuro()
                    End If
                End SyncLock
            Catch ex As Exception
                _owner.logger.ScriviLog($"WGObjectCollection.RemoveAt: {ex.ToString()}")
            End Try
        End Sub

        Default Public Property Item(index As Integer) As Object Implements IList(Of Object).Item
            Get
                Try
                    SyncLock _owner._syncLock
                        If index >= 0 AndAlso index < _list.Count Then
                            Return _list(index)
                        End If
                        Return Nothing
                    End SyncLock
                Catch ex As Exception
                    _owner.logger.ScriviLog($"WGObjectCollection.Item Get: {ex.ToString()}")
                    Return Nothing
                End Try
            End Get
            Set(value As Object)
                Try
                    SyncLock _owner._syncLock
                        If index >= 0 AndAlso index < _list.Count Then
                            _list(index) = value
                            _owner.InvalidaCacheAltezze()
                            _owner.InvalidateSicuro()
                        End If
                    End SyncLock
                Catch ex As Exception
                    _owner.logger.ScriviLog($"WGObjectCollection.Item Set: {ex.ToString()}")
                End Try
            End Set
        End Property

        Public Function IndexOf(item As Object) As Integer Implements IList(Of Object).IndexOf
            Try
                SyncLock _owner._syncLock
                    Return _list.IndexOf(item)
                End SyncLock
            Catch ex As Exception
                _owner.logger.ScriviLog($"WGObjectCollection.IndexOf: {ex.ToString()}")
                Return -1
            End Try
        End Function

        Public Sub Insert(index As Integer, item As Object) Implements IList(Of Object).Insert
            Try
                SyncLock _owner._syncLock
                    _owner.SegnalaCaricamentoInCorso()
                    If index >= 0 AndAlso index <= _list.Count Then
                        _list.Insert(index, item)
                        _owner.InvalidaCacheAltezze()
                        _owner.InvalidateSicuro()
                    End If
                End SyncLock
            Catch ex As Exception
                _owner.logger.ScriviLog($"WGObjectCollection.Insert: {ex.ToString()}")
            End Try
        End Sub

        Public Sub InsertAfter(targetItem As Object, newItem As Object)
            Try
                SyncLock _owner._syncLock
                    Dim index As Integer = IndexOf(targetItem)
                    If index >= 0 Then
                        Insert(index + 1, newItem)
                    End If
                End SyncLock
            Catch ex As Exception
                _owner.logger.ScriviLog($"WGObjectCollection.InsertAfter: {ex.ToString()}")
            End Try
        End Sub

        Public Sub InsertBefore(targetItem As Object, newItem As Object)
            Try
                SyncLock _owner._syncLock
                    Dim index As Integer = IndexOf(targetItem)
                    If index >= 0 Then
                        Insert(index, newItem)
                    End If
                End SyncLock
            Catch ex As Exception
                _owner.logger.ScriviLog($"WGObjectCollection.InsertBefore: {ex.ToString()}")
            End Try
        End Sub

        Public Function GetEnumerator() As IEnumerator(Of Object) Implements IEnumerable(Of Object).GetEnumerator
            Try
                SyncLock _owner._syncLock
                    Return _list.ToList().GetEnumerator()
                End SyncLock
            Catch ex As Exception
                _owner.logger.ScriviLog($"WGObjectCollection.GetEnumerator: {ex.ToString()}")
                Return New List(Of Object)().GetEnumerator()
            End Try
        End Function

        Private Function IEnumerable_GetEnumerator() As IEnumerator Implements IEnumerable.GetEnumerator
            Return GetEnumerator()
        End Function

        Friend Sub Destroy()
            Try
                If _owner Is Nothing OrElse _owner.Disposing OrElse _owner.IsDisposed Then
                    Clear()
                End If
            Catch ex As Exception
                If _owner IsNot Nothing AndAlso _owner.logger IsNot Nothing Then
                    _owner.logger.ScriviLog("WGObjectCollection.Destroy: " & ex.Message)
                End If
            End Try
        End Sub
    End Class

    Public Event SelectedIndexChanged As EventHandler
    Public Event SelectionChanged As EventHandler
    Public Event InizioListaRaggiunto As EventHandler
    Public Event FineListaRaggiunta As EventHandler
    Public Event Avanti(sender As Object, e As EventArgs)
    Public Event Indietro(sender As Object, e As EventArgs)
    Public Event SelezioneAttivata As EventHandler
    Public Event SelezioneDisattivata As EventHandler
    Public Event InvioPremuto(sender As Object, e As EventArgs)
    Public Event EscPremuto(sender As Object, e As EventArgs)
    Public Event OpzioniElementoRichieste(sender As Object, e As EventArgs)

    ''' <summary>
    ''' Azioni disponibili sulla voce corrente, scorribili con il flick verticale e annunciate a
    ''' voce senza aprire alcun menu, come le azioni di VoiceOver. Chi possiede la lista riempie
    ''' questo elenco e reagisce a AzioneAttivata eseguendo l'azione corrispondente al nome.
    ''' Lasciandolo vuoto il flick verso l'alto continua a chiedere le opzioni come prima.
    ''' </summary>
    Public Property AzioniDisponibili As List(Of String)
        Get
            Try
                SyncLock _syncLock
                    Return New List(Of String)(_azioniDisponibili)
                End SyncLock
            Catch ex As Exception
                logger.ScriviLog($"AzioniDisponibili Get: {ex.ToString()}")
                Return New List(Of String)()
            End Try
        End Get
        Set(value As List(Of String))
            Try
                SyncLock _syncLock
                    Dim nuove As New List(Of String)()
                    If value IsNot Nothing Then
                        For Each nome As String In value
                            If Not String.IsNullOrWhiteSpace(nome) Then nuove.Add(nome)
                        Next
                    End If
                    ' Chi usa la lista rimette le azioni a ogni colpo in su: se sono le stesse la posizione
                    ' resta, altrimenti lo scorrimento ripartirebbe sempre dall'ultima e non farebbe il giro.
                    If Not nuove.SequenceEqual(_azioniDisponibili) Then
                        _azioniDisponibili.Clear()
                        _azioniDisponibili.AddRange(nuove)
                        _indiceAzione = -1
                    End If
                End SyncLock
            Catch ex As Exception
                logger.ScriviLog($"AzioniDisponibili Set: {ex.ToString()}")
            End Try
        End Set
    End Property

    Public Event AzioneAttivata(nomeAzione As String)

    Private _azioniDisponibili As New List(Of String)
    Private _indiceAzione As Integer = -1

    Private Sub AzzeraAzioneCorrente()
        Try
            SyncLock _syncLock
                _indiceAzione = -1
            End SyncLock
        Catch ex As Exception
            logger.ScriviLog($"AzzeraAzioneCorrente: {ex.ToString()}")
        End Try
    End Sub

    Private Function ScorriAzioni(avanti As Boolean) As Boolean
        Try
            Dim nomeAzione As String = Nothing

            SyncLock _syncLock
                If _azioniDisponibili.Count = 0 Then Return False

                If _indiceAzione < 0 Then
                    _indiceAzione = If(avanti, 0, _azioniDisponibili.Count - 1)
                ElseIf avanti Then
                    _indiceAzione = (_indiceAzione + 1) Mod _azioniDisponibili.Count
                Else
                    _indiceAzione = (_indiceAzione - 1 + _azioniDisponibili.Count) Mod _azioniDisponibili.Count
                End If

                nomeAzione = _azioniDisponibili(_indiceAzione)
            End SyncLock

            If lettura IsNot Nothing AndAlso Not String.IsNullOrEmpty(nomeAzione) Then
                lettura.LeggiTesto(nomeAzione, escludiPunteggiatura:=True)
            End If

            Return True
        Catch ex As Exception
            logger.ScriviLog($"ScorriAzioni: {ex.ToString()}")
            Return False
        End Try
    End Function

    Private Function EseguiAzioneCorrente() As Boolean
        Try
            Dim nomeAzione As String = Nothing

            SyncLock _syncLock
                If _indiceAzione < 0 OrElse _indiceAzione >= _azioniDisponibili.Count Then Return False
                nomeAzione = _azioniDisponibili(_indiceAzione)
                _indiceAzione = -1
            End SyncLock

            If String.IsNullOrEmpty(nomeAzione) Then Return False

            RaiseEvent AzioneAttivata(nomeAzione)
            Return True
        Catch ex As Exception
            logger.ScriviLog($"EseguiAzioneCorrente: {ex.ToString()}")
            Return False
        End Try
    End Function

    Private _items As WGObjectCollection
    Private _selectedIndices As New HashSet(Of Integer)()
    Private _selectedIndex As Integer = -1
    Private _topIndex As Integer = 0
    Private _selectionMode As SelectionMode = SelectionMode.One

    Private _isUpdating As Boolean = False

    Private _itemHeight As Integer = 35
    Private Const LarghezzaMinimaCellaTouch As Integer = 220
    Private Const LarghezzaMinimaAssolutaCella As Integer = 120
    Private Const AltezzaCellaGriglia As Integer = 80

    Private Structure InfoAltezzaElemento
        Public Altezza As Integer
        Public MultiRiga As Boolean
    End Structure

    Private _cacheInfoElementi As New Dictionary(Of Integer, InfoAltezzaElemento)
    Private _cacheAltezzeWidth As Integer = -1
    Private _hFont As IntPtr = IntPtr.Zero
    Private _hTitleFont As IntPtr = IntPtr.Zero
    Private _hHighResFont As IntPtr = IntPtr.Zero
    Private _fontNamePtr As IntPtr = IntPtr.Zero
    Private _backColor As Integer = &HFFFFFF
    Private _foreColor As Integer = &H0
    Private _selectionColor As Integer = &HFFCCAA
    Private _focusColor As Integer = &HFF0000
    Private _titleBackColor1 As Integer = &H404040
    Private _titleBackColor2 As Integer = &H101010
    Private _titleForeColor As Integer = &HFFFFFF
    Private _highContrast As Boolean = False
    Private _borderThickness As Integer = 2

    Private _titolo As String = ""
    Private _titoloHeight As Integer = 50

    Private searchBuffer As String = ""
    Private lastKeyPressTime As DateTime = DateTime.MinValue

    Private _resolutionScale As Integer = 4
    Private _eraDisattivato As Boolean = False
    Private _focusAttivo As Boolean = False

    Private _listaBackup As New List(Of String)()
    Private _selectedItemsBackup As New List(Of String)()
    Private _indiceBackup As Integer = -1

    Private _selezioneConsentita As Boolean = False

    Private _ciclono As Boolean = False

    Private _isLoading As Boolean = False
    Private _initialLoadComplete As Boolean = False
    Private _lastItemAddTime As DateTime = DateTime.MinValue
    Private _spinnerFrame As Integer = 0
    Private _hasAnnouncedLoading As Boolean = False
    Private _ultimoConteggioStabile As Integer = -1
    Private _confermeStabilita As Integer = 0
    Private Const ConfermeStabilitaRichieste As Integer = 3

    Private _spinnerTxt As String = "Caricamento in corso..."
    Private _spinnerPens(11) As IntPtr

    Private _vertArray(1) As TRIVERTEX
    Private _meshArray(0) As GRADIENT_RECT

    Private _leggiTitoloIngresso As Boolean = True
    Private _titoloLetto As Boolean = False
    Private _ultimoAnnuncioIngresso As String = ""
    Private _ultimoAnnuncioIngressoTick As Long = 0
    Private _attesaCaricamentoDopoVisibilita As Boolean = False
    Private _annuncioDaRipetere As Boolean = False
    Private _appenaRicompartoSincrono As Boolean = False

    Private _annuncioRichiesto As Boolean = False
    Private _annuncioInCorso As Boolean = False
    Private _tentativiAnnuncio As Integer = 0
    Private Const MaxTentativiAnnuncio As Integer = 120
    Private Const IntervalloCoalescenzaAnnuncio As Integer = 60
    Private Const IntervalloAntiDuplicatoAnnuncio As Integer = 1500
    Private Const IntervalloFocusTransitorio As Integer = 300
    Private _firmaUltimoAnnuncio As String = ""
    Private _tickUltimaFirmaAnnuncio As Long = 0
    Private _tickUltimoKillFocus As Long = 0

    Private ReadOnly _processoInCorsoLock As New Object()

    Private _lastConfigHash As String = ""

    Private _hMemDC As IntPtr = IntPtr.Zero
    Private _hMemBitmap As IntPtr = IntPtr.Zero
    Private _hOldMemBitmap As IntPtr = IntPtr.Zero
    Private _memWidth As Integer = 0
    Private _memHeight As Integer = 0

    Private _hScratchDC As IntPtr = IntPtr.Zero
    Private _hScratchBitmap As IntPtr = IntPtr.Zero
    Private _hOldScratchBitmap As IntPtr = IntPtr.Zero
    Private _scratchWidth As Integer = 0
    Private _scratchHeight As Integer = 0
    Private Const RIGHE_MASSIME_ELEMENTO As Integer = 5

    Private WithEvents tmrSpinner As New System.Windows.Forms.Timer()
    Private WithEvents tmrCheckLoad As New System.Windows.Forms.Timer()
    Private WithEvents tmrConfigMonitor As New System.Windows.Forms.Timer()
    Private WithEvents tmrAnnuncio As New System.Windows.Forms.Timer()

    Public Sub New()
        Try
            Me.SetStyle(ControlStyles.Opaque, True)
            Me.SetStyle(ControlStyles.UserPaint, False)
            Me.SetStyle(ControlStyles.AllPaintingInWmPaint, False)
            Me.SetStyle(ControlStyles.Selectable, True)
            Me.SetStyle(ControlStyles.ResizeRedraw, True)

            _tastiera = WGTastiera.GetInstance()
            _touk = WGTouk.GetInstance()

            _fontNamePtr = Marshal.StringToHGlobalUni("Segoe UI")

            For i As Integer = 0 To 11
                Dim val As Integer = 100 + (i * 40)
                Dim colore As Integer = (val) Or (val << 8) Or (val << 16)
                _spinnerPens(i) = CreatePen(PS_SOLID, 3, colore)
            Next

            _items = New WGObjectCollection(Me)
            Me.AccessibleRole = AccessibleRole.None
            Me.AccessibleName = " "
            CaricaConfigurazione()

            tmrSpinner.Interval = 33
            tmrCheckLoad.Interval = 10
            tmrAnnuncio.Interval = IntervalloCoalescenzaAnnuncio

            tmrConfigMonitor.Interval = 1000
            tmrConfigMonitor.Start()

        Catch ex As Exception
            logger.ScriviLog($"Errore Costruttore WGListaView: {ex.ToString()}")
        End Try
    End Sub

    Private Function PuoElaborareInput() As Boolean
        Try
            Dim contenitore As WGContenitore = WGContenitore.Istanza
            If contenitore Is Nothing OrElse contenitore.IsDisposed Then Return False

            Dim schermata As Control = Me
            While schermata IsNot Nothing AndAlso schermata.Parent IsNot Nothing AndAlso schermata.Parent IsNot contenitore
                schermata = schermata.Parent
            End While

            If schermata IsNot Nothing AndAlso schermata.Parent Is contenitore Then
                Return contenitore.PuoRicevereInput(schermata)
            End If

            Dim formRadice As Form = Me.FindForm()
            If formRadice Is Nothing Then Return False
            Return contenitore.PuoRicevereInput(formRadice)
        Catch ex As Exception
            logger.ScriviLog($"PuoElaborareInput: {ex.ToString()}")
            Return False
        End Try
    End Function

    Private Sub _tastiera_KeyDown(ByRef tasto As Keys)
        Try
            If tasto = Keys.None Then Return
            If Me.IsDisposed OrElse Not Me.IsHandleCreated Then Return
            If Not Me.Visible Then Return
            If _appenaRicompartoSincrono Then Return

            If Not PuoElaborareInput() Then Return

            _isHookActive = True

            If GestisciKeyDown(tasto) Then
                tasto = Keys.None
            End If

        Catch ex As Exception
            logger.ScriviLog($"_tastiera_KeyDown: {ex.ToString()}")
        End Try
    End Sub

    Private Sub OnCtrlSinistroPremuto() Handles _tastiera.ControlSinistroPremuto
        Try
            _ctrlPremuto = True
        Catch ex As Exception
            logger.ScriviLog($"OnCtrlSinistroPremuto: {ex.ToString()}")
        End Try
    End Sub

    Private Sub OnCtrlSinistroRilasciato() Handles _tastiera.ControlSinistroRilasciato
        Try
            _ctrlPremuto = False
        Catch ex As Exception
            logger.ScriviLog($"OnCtrlSinistroRilasciato: {ex.ToString()}")
        End Try
    End Sub

    ''' <summary>
    ''' Sposta il fuoco passando dallo stesso percorso delle frecce. La versione precedente
    ''' calcolava l'indice per conto proprio e chiamava SetFocusIndex direttamente, saltando
    ''' tutto quello che GestisciKeyDown fa intorno al movimento: il ciclo della lista, gli
    ''' eventi FineListaRaggiunta e InizioListaRaggiunto su cui i menu appendono la propria
    ''' logica, l'azzeramento del buffer di ricerca e il caso della lista con un solo elemento.
    ''' Per questo con il flick i menu non si muovevano come con la tastiera fisica.
    ''' </summary>
    Private Sub SpostaFuocoDaGestoTouch(direzione As Integer)
        Try
            Dim conteggio As Integer = 0
            SyncLock _syncLock
                conteggio = _items.Count
            End SyncLock

            If conteggio = 0 Then Return

            AzzeraAzioneCorrente()
            GestisciKeyDown(If(direzione > 0, Keys.Down, Keys.Up))
        Catch ex As Exception
            logger.ScriviLog($"SpostaFuocoDaGestoTouch: {ex.ToString()}")
        End Try
    End Sub

    Private Sub _touk_FlickAvanti()
        Try
            If Me.IsDisposed OrElse Not Me.IsHandleCreated Then Return
            If Not Me.Visible Then Return
            SpostaFuocoDaGestoTouch(1)
        Catch ex As Exception
            logger.ScriviLog($"_touk_FlickAvanti: {ex.ToString()}")
        End Try
    End Sub

    Private Sub _touk_FlickIndietro()
        Try
            If Me.IsDisposed OrElse Not Me.IsHandleCreated Then Return
            If Not Me.Visible Then Return
            SpostaFuocoDaGestoTouch(-1)
        Catch ex As Exception
            logger.ScriviLog($"_touk_FlickIndietro: {ex.ToString()}")
        End Try
    End Sub

    Private Sub _touk_FlickSu()
        Try
            If Me.IsDisposed OrElse Not Me.IsHandleCreated Then Return
            If Not Me.Visible Then Return
            If Not PuoElaborareInput() Then Return
            If ScorriAzioni(avanti:=False) Then Return
            RaiseEvent OpzioniElementoRichieste(Me, EventArgs.Empty)
        Catch ex As Exception
            logger.ScriviLog($"_touk_FlickSu: {ex.ToString()}")
        End Try
    End Sub

    Private Sub _touk_FlickGiu()
        Try
            If Me.IsDisposed OrElse Not Me.IsHandleCreated Then Return
            If Not Me.Visible Then Return
            If Not PuoElaborareInput() Then Return
            ScorriAzioni(avanti:=True)
        Catch ex As Exception
            logger.ScriviLog($"_touk_FlickGiu: {ex.ToString()}")
        End Try
    End Sub

    Private Sub _touk_TapDoppio()
        Try
            If Me.IsDisposed OrElse Not Me.IsHandleCreated Then Return
            If Not Me.Visible Then Return
            If EseguiAzioneCorrente() Then Return
            GestisciKeyDown(Keys.Enter)
        Catch ex As Exception
            logger.ScriviLog($"_touk_TapDoppio: {ex.ToString()}")
        End Try
    End Sub

    Private Sub _touk_TapDoppioProlungato()
        Try
            shiftpremuto()
        Catch ex As Exception
            logger.ScriviLog($"_touk_TapDoppioProlungato: {ex.ToString()}")
        End Try
    End Sub

    Private Sub _touk_EscapeGesto()
        Try
            If Me.IsDisposed OrElse Not Me.IsHandleCreated Then Return
            If Not Me.Visible Then Return
            GestisciKeyDown(Keys.Escape)
        Catch ex As Exception
            logger.ScriviLog($"_touk_EscapeGesto: {ex.ToString()}")
        End Try
    End Sub

    Private Sub _touk_StatoCambiato()
        Try
            If Me.IsDisposed OrElse Not Me.IsHandleCreated Then Return
            If Me.InvokeRequired Then
                Me.BeginInvoke(New MethodInvoker(AddressOf _touk_StatoCambiato))
                Return
            End If
            Dim indiceCorrente As Integer = -1
            SyncLock _syncLock
                indiceCorrente = _selectedIndex
            End SyncLock
            If indiceCorrente >= 0 Then
                EnsureVisible(indiceCorrente)
            End If
            Me.Invalidate()
        Catch ex As Exception
            logger.ScriviLog($"_touk_StatoCambiato: {ex.ToString()}")
        End Try
    End Sub

    ''' <summary>Falso per non dire "menù" prima del titolo (per esempio nel centro notifiche).</summary>
    Public Property TitoloComeMenu As Boolean = True

    Private Function CostruisciNomeTitolo(titolo As String) As String
        Try
            If Not TitoloComeMenu Then Return If(titolo, "")
            If String.IsNullOrEmpty(titolo) Then Return "menu"
            If titolo.Contains("?") Then
                Dim testo As String = titolo.Replace("?", "").Trim()
                If String.IsNullOrEmpty(testo) Then Return "domanda"
                Return "domanda " & testo
            End If
            If titolo.Contains("Menu") OrElse titolo.Contains("menu") OrElse
               titolo.Contains("Menù") OrElse titolo.Contains("menù") OrElse
               titolo.Contains("Domanda") OrElse titolo.Contains("domanda") Then
                Return titolo
            End If
            Return "menù " & titolo
        Catch ex As Exception
            logger.ScriviLog($"CostruisciNomeTitolo: {ex.ToString()}")
            Return titolo
        End Try
    End Function

    Private Function CostruisciStringaIngresso() As String
        Try
            Dim leggiTitolo As Boolean = True
            Dim leggiVoci As Boolean = True
            If config.EsisteId("LeggiTitoloIngresso") Then Boolean.TryParse(config.LeggiValore("LeggiTitoloIngresso"), leggiTitolo)
            If config.EsisteId("LeggiNumeroVoci") Then Boolean.TryParse(config.LeggiValore("LeggiNumeroVoci"), leggiVoci)

            Dim nomeMenu As String = ""
            If leggiTitolo Then
                nomeMenu = CostruisciNomeTitolo(_titolo)
            End If

            Dim count As Integer = 0
            SyncLock _syncLock
                count = _items.Count
            End SyncLock

            Dim vociStr As String = ""
            If leggiVoci Then
                vociStr = If(count = 0, "nessuna voce", count.ToString() & " voci")
            End If

            If Not String.IsNullOrEmpty(nomeMenu) AndAlso Not String.IsNullOrEmpty(vociStr) Then
                Return nomeMenu & ", " & vociStr
            ElseIf Not String.IsNullOrEmpty(nomeMenu) Then
                Return nomeMenu
            ElseIf Not String.IsNullOrEmpty(vociStr) Then
                Return vociStr
            End If
            Return ""
        Catch ex As Exception
            logger.ScriviLog($"CostruisciStringaIngresso: {ex.ToString()}")
            Return "menu"
        End Try
    End Function

    Public Sub LeggiStatoIngresso()
        Try
            RichiediAnnuncioIngresso()
        Catch ex As Exception
            logger.ScriviLog($"LeggiStatoIngresso: {ex.ToString()}")
        End Try
    End Sub

    Private Sub RichiediAnnuncioIngresso()
        Try
            If Me.IsDisposed Then Return
            If Not _annuncioRichiesto Then
                _tentativiAnnuncio = 0
            End If
            _annuncioRichiesto = True
            tmrAnnuncio.Stop()
            tmrAnnuncio.Start()
        Catch ex As Exception
            logger.ScriviLog($"RichiediAnnuncioIngresso: {ex.ToString()}")
        End Try
    End Sub

    Private Sub AnnullaAnnuncioIngresso()
        Try
            _annuncioRichiesto = False
            _tentativiAnnuncio = 0
            tmrAnnuncio.Stop()
        Catch ex As Exception
            logger.ScriviLog($"AnnullaAnnuncioIngresso: {ex.ToString()}")
        End Try
    End Sub

    Private Sub tmrAnnuncio_Tick(sender As Object, e As EventArgs) Handles tmrAnnuncio.Tick
        Try
            tmrAnnuncio.Stop()
            If Me.IsDisposed OrElse Not Me.IsHandleCreated Then
                _annuncioRichiesto = False
                _tentativiAnnuncio = 0
                Return
            End If
            If Not _annuncioRichiesto Then Return
            If Not Me.Visible Then
                _annuncioRichiesto = False
                _tentativiAnnuncio = 0
                Return
            End If

            _annuncioRichiesto = False
            Dim completato As Boolean = EseguiAnnuncioIngresso()

            If completato Then
                _tentativiAnnuncio = 0
            Else
                _tentativiAnnuncio += 1
                If _tentativiAnnuncio >= MaxTentativiAnnuncio Then
                    _tentativiAnnuncio = 0
                Else
                    _annuncioRichiesto = True
                    tmrAnnuncio.Start()
                End If
            End If
        Catch ex As Exception
            logger.ScriviLog($"tmrAnnuncio_Tick: {ex.ToString()}")
        End Try
    End Sub

    Private Function CostruisciFirmaAnnuncio(count As Integer, voceCorrente As String) As String
        Try
            Return _titolo & "|" & count.ToString() & "|" & voceCorrente & "|" & _selezioneConsentita.ToString()
        Catch ex As Exception
            logger.ScriviLog($"CostruisciFirmaAnnuncio: {ex.ToString()}")
            Return ""
        End Try
    End Function

    Private Function EseguiAnnuncioIngresso() As Boolean
        Try
            If _annuncioInCorso Then Return True
            If _titoloLetto Then Return True
            If _isLoading Then Return False
            If Not Me.Visible Then Return True
            If Not PuoElaborareInput() Then Return False

            Dim countCorrente As Integer = 0
            SyncLock _syncLock
                countCorrente = _items.Count
            End SyncLock
            If countCorrente = 0 Then Return False

            _annuncioInCorso = True
            Try
                If _selectedIndex = -1 Then
                    ControllaSelezioneIniziale()
                End If

                Dim voceCorrente As String = ""
                SyncLock _syncLock
                    If _items.Count > 0 AndAlso _selectedIndex >= 0 AndAlso _selectedIndex < _items.Count Then
                        Dim itemObj As Object = _items(_selectedIndex)
                        If itemObj IsNot Nothing Then
                            voceCorrente = itemObj.ToString()
                        End If
                    End If
                End SyncLock

                Dim firma As String = CostruisciFirmaAnnuncio(countCorrente, voceCorrente)
                Dim adesso As Long = Environment.TickCount

                _titoloLetto = True
                _leggiTitoloIngresso = False
                _annuncioDaRipetere = False
                _attesaCaricamentoDopoVisibilita = False

                If Not String.IsNullOrEmpty(firma) AndAlso firma = _firmaUltimoAnnuncio AndAlso (adesso - _tickUltimaFirmaAnnuncio) < IntervalloAntiDuplicatoAnnuncio Then
                    _tickUltimaFirmaAnnuncio = adesso
                    Return True
                End If

                _firmaUltimoAnnuncio = firma
                _tickUltimaFirmaAnnuncio = adesso

                Dim intestazione As String = CostruisciStringaIngresso()

                Dim testo As String = ""
                If Not String.IsNullOrEmpty(intestazione) AndAlso Not String.IsNullOrEmpty(voceCorrente) Then
                    testo = intestazione & ". " & voceCorrente
                ElseIf Not String.IsNullOrEmpty(intestazione) Then
                    testo = intestazione
                ElseIf Not String.IsNullOrEmpty(voceCorrente) Then
                    testo = voceCorrente
                End If

                If _selezioneConsentita AndAlso Not String.IsNullOrEmpty(testo) Then
                    testo &= ". Selezione multipla consentita"
                End If

                If Not String.IsNullOrEmpty(testo) Then
                    If testo = _ultimoAnnuncioIngresso AndAlso (adesso - _ultimoAnnuncioIngressoTick) < IntervalloAntiDuplicatoAnnuncio Then
                        Return True
                    End If
                    _ultimoAnnuncioIngresso = testo
                    _ultimoAnnuncioIngressoTick = adesso
                    lettura.LeggiTesto(testo, escludiPunteggiatura:=True)
                End If

                Return True
            Finally
                _annuncioInCorso = False
            End Try
        Catch ex As Exception
            logger.ScriviLog($"EseguiAnnuncioIngresso: {ex.ToString()}")
            _annuncioInCorso = False
            Return True
        End Try
    End Function

    ''' <summary>
    ''' Rete di sicurezza, chiamata poco dopo l'apertura dell'elenco: se per qualsiasi motivo l'elenco non
    ''' ha finito di caricarsi, non si è disegnato o non si è annunciato (i suoi timer non sono scattati, il
    ''' fuoco non è arrivato), lo fa adesso, direttamente. Se è già tutto a posto non fa niente.
    ''' </summary>
    Friend Sub AssicuraIngresso()
        Try
            If Me.IsDisposed OrElse Not Me.Visible Then Return
            If Not Me.IsHandleCreated Then Me.CreateControl()
            If _titoloLetto Then Return
            Dim quanti As Integer
            SyncLock _syncLock
                quanti = _items.Count
            End SyncLock
            If quanti = 0 OrElse Not PuoElaborareInput() Then Return
            If _isLoading Then
                _isLoading = False
                _initialLoadComplete = True
                tmrSpinner.Stop()
                tmrCheckLoad.Stop()
                AggiornaConteggi()
                ControllaSelezioneIniziale()
                GestisciFineCaricamento()
            End If
            Me.Invalidate()
            Me.Update()
            If Not Me.ContainsFocus Then Me.Focus()
            AnnullaAnnuncioIngresso()
            EseguiAnnuncioIngresso()
        Catch ex As Exception
            logger.ScriviLog($"AssicuraIngresso: {ex.ToString()}")
        End Try
    End Sub

    Public Sub ReimpostaTitoloIngresso()
        Try
            _titoloLetto = False
            _leggiTitoloIngresso = True
            _firmaUltimoAnnuncio = ""
            _tickUltimaFirmaAnnuncio = 0
            _ultimoAnnuncioIngresso = ""
            _ultimoAnnuncioIngressoTick = 0
        Catch ex As Exception
            logger.ScriviLog($"ReimpostaTitoloIngresso: {ex.ToString()}")
        End Try
    End Sub

    Public Sub LeggiElementoCorrente()
        Try
            If Not _titoloLetto Then Return

            If _selectedIndex = -1 Then
                ControllaSelezioneIniziale()
            End If

            Dim testoDaLeggere As String = ""

            SyncLock _syncLock
                If _selectedIndex >= 0 AndAlso _selectedIndex < _items.Count Then
                    Dim stato As String = ""
                    If _selectionMode = SelectionMode.MultiSimple OrElse _selectionMode = SelectionMode.MultiExtended Then
                        stato = If(_selectedIndices.Contains(_selectedIndex), "Selezionato ", "Non selezionato ")
                    End If
                    Dim itemObj As Object = _items(_selectedIndex)
                    If itemObj IsNot Nothing Then
                        testoDaLeggere &= stato & itemObj.ToString()
                    End If
                End If
            End SyncLock

            If Not String.IsNullOrEmpty(testoDaLeggere) Then
                lettura.LeggiTesto(testoDaLeggere, escludiPunteggiatura:=True)
            End If
        Catch ex As Exception
            logger.ScriviLog($"LeggiElementoCorrente: {ex.ToString()}")
        End Try
    End Sub

    Private Sub tmrConfigMonitor_Tick(sender As Object, e As EventArgs) Handles tmrConfigMonitor.Tick
        Try
            If Me.IsDisposed OrElse Not Me.IsHandleCreated Then Return
            Dim currentHash As String = OttieniHashConfigurazione()
            If _lastConfigHash <> "" AndAlso currentHash <> _lastConfigHash Then
                CaricaConfigurazione()
                Me.Invalidate()
            End If
            _lastConfigHash = currentHash
        Catch ex As Exception
            logger.ScriviLog($"tmrConfigMonitor_Tick: {ex.ToString()}")
        End Try
    End Sub

    Private Sub tmrSpinner_Tick(sender As Object, e As EventArgs) Handles tmrSpinner.Tick
        Try
            If Me.IsDisposed OrElse Not Me.IsHandleCreated Then Return
            If _isLoading Then
                _spinnerFrame = (_spinnerFrame + 1) Mod 12
                Me.Invalidate()
            Else
                tmrSpinner.Stop()
            End If
        Catch ex As Exception
            logger.ScriviLog($"tmrSpinner_Tick: {ex.ToString()}")
        End Try
    End Sub

    Private Sub tmrCheckLoad_Tick(sender As Object, e As EventArgs) Handles tmrCheckLoad.Tick
        Try
            If Me.IsDisposed OrElse Not Me.IsHandleCreated Then Return
            Dim elapsed As Double = (DateTime.Now - _lastItemAddTime).TotalMilliseconds

            If elapsed <= 20 Then
                ' Sono ancora arrivati elementi di recente: si riparte a contare le conferme.
                _confermeStabilita = 0
                _ultimoConteggioStabile = -1
                Return
            End If

            Dim countAttuale As Integer = 0
            SyncLock _syncLock
                countAttuale = _items.Count
            End SyncLock

            If countAttuale = _ultimoConteggioStabile Then
                _confermeStabilita += 1
            Else
                _ultimoConteggioStabile = countAttuale
                _confermeStabilita = 1
            End If

            ' Il numero di elementi deve restare invariato per più controlli consecutivi
            ' prima di considerare la lista davvero stabile: evita di annunciare il titolo
            ' su una lista che sembra ferma ma sta per ricevere altri elementi.
            If _confermeStabilita < ConfermeStabilitaRichieste Then Return

            _confermeStabilita = 0
            _ultimoConteggioStabile = -1

            If _isLoading Then
                _isLoading = False
                _initialLoadComplete = True
                tmrSpinner.Stop()
                AggiornaConteggi()
                ControllaSelezioneIniziale()
                GestisciFineCaricamento()
                Me.Invalidate()
                If Me.Visible AndAlso (_attesaCaricamentoDopoVisibilita OrElse _annuncioDaRipetere) Then
                    _annuncioDaRipetere = False
                    _attesaCaricamentoDopoVisibilita = False
                    If Not _titoloLetto Then
                        PianificaAnnuncioIniziale(True)
                    End If
                End If
            ElseIf _annuncioDaRipetere Then
                AggiornaConteggi()
                ControllaSelezioneIniziale()
                GestisciFineCaricamento()
                Me.Invalidate()
                If Me.Visible Then
                    _annuncioDaRipetere = False
                    If Not _titoloLetto Then
                        PianificaAnnuncioIniziale(True)
                    End If
                End If
            Else
                AggiornaConteggi()
                Me.Invalidate()
            End If
            tmrCheckLoad.Stop()
        Catch ex As Exception
            logger.ScriviLog($"tmrCheckLoad_Tick: {ex.ToString()}")
        End Try
    End Sub

    Private Sub PianificaAnnuncioIniziale(Optional forzaRitardo As Boolean = False)
        Try
            RichiediAnnuncioIngresso()
        Catch ex As Exception
            logger.ScriviLog($"PianificaAnnuncioIniziale: {ex.ToString()}")
        End Try
    End Sub

    Private Function OttieniHashConfigurazione() As String
        Try
            Dim stato As New StringBuilder()
            Dim chiavi() As String = {"FontDimensione", "ContrastoAlto", "MenuCarattere", "LeggiTitoloIngresso", "ColoreSfondo", "ColoreTesto", "ColoreSelezione", "ColoreFuoco"}
            For Each k As String In chiavi
                stato.Append(config.LeggiValore(k))
            Next
            ' Nella stessa hash anche il tema di Windows: se cambia mentre il menu è aperto, lo stesso timer che
            ' controlla la configurazione se ne accorge e richiama CaricaConfigurazione().
            stato.Append("|tema=").Append(WGTema.Scuro)
            Return stato.ToString()
        Catch
            Return ""
        End Try
    End Function





    Protected Overrides Sub OnVisibleChanged(e As EventArgs)
        Try
            MyBase.OnVisibleChanged(e)
            If Me.Visible Then
                _isHookActive = True
                _titoloLetto = False

                ' Segnalo che sono ridiventato visibile in questo stesso istante: se un tasto
                ' (es. Escape) è già in corso di dispatch nello stesso ciclo sincrono che mi ha
                ' reso di nuovo visibile - tipicamente perché un menu figlio si è appena chiuso
                ' sopra di me - non deve essere rielaborato anche da me. La bandiera viene tolta
                ' al giro successivo della coda messaggi, quando siamo certi che il tasto corrente
                ' sia stato completamente smaltito.
                _appenaRicompartoSincrono = True
                If Me.IsHandleCreated Then
                    Try
                        Me.BeginInvoke(New MethodInvoker(Sub() _appenaRicompartoSincrono = False))
                    Catch ex As Exception
                        _appenaRicompartoSincrono = False
                    End Try
                Else
                    _appenaRicompartoSincrono = False
                End If

                If Not Me.IsHandleCreated Then
                    _attesaCaricamentoDopoVisibilita = True
                    Return
                End If
                Dim count As Integer = 0
                SyncLock _syncLock
                    count = _items.Count
                End SyncLock
                If count = 0 Then
                    _attesaCaricamentoDopoVisibilita = True

                Else
                    If _isLoading Then
                        Dim msDaUltimoItem As Double = (DateTime.Now - _lastItemAddTime).TotalMilliseconds
                        If msDaUltimoItem > 60 Then
                            _isLoading = False
                            _initialLoadComplete = True
                            tmrSpinner.Stop()
                            tmrCheckLoad.Stop()
                            AggiornaConteggi()
                            ControllaSelezioneIniziale()
                            GestisciFineCaricamento()
                        End If
                    End If
                    Me.Invalidate()
                    If _annuncioDaRipetere Then
                        PianificaAnnuncioIniziale(True)
                    Else
                        PianificaAnnuncioIniziale()
                    End If
                End If
            Else
                _isHookActive = False
                _titoloLetto = False
                _focusAttivo = False
                _firmaUltimoAnnuncio = ""
                _tickUltimaFirmaAnnuncio = 0
                _ultimoAnnuncioIngresso = ""
                _ultimoAnnuncioIngressoTick = 0
                AnnullaAnnuncioIngresso()
            End If
        Catch ex As Exception
            logger.ScriviLog($"OnVisibleChanged: {ex.ToString()}")
        End Try
    End Sub

    Friend Sub SegnalaSvuotamento()
        Try
            _initialLoadComplete = False
            _isLoading = False
            _annuncioDaRipetere = True
            _titoloLetto = False
            _confermeStabilita = 0
            _ultimoConteggioStabile = -1
            _firmaUltimoAnnuncio = ""
            _tickUltimaFirmaAnnuncio = 0
            _ultimoAnnuncioIngresso = ""
            _ultimoAnnuncioIngressoTick = 0
            tmrSpinner.Stop()
            tmrCheckLoad.Stop()
        Catch ex As Exception
            logger.ScriviLog($"SegnalaSvuotamento: {ex.ToString()}")
        End Try
    End Sub

    Friend Sub SegnalaCaricamentoInCorso()
        Try
            _lastItemAddTime = DateTime.Now
            _annuncioDaRipetere = True
            _confermeStabilita = 0
            _ultimoConteggioStabile = -1

            If _initialLoadComplete Then
                tmrCheckLoad.Start()
                Return
            End If

            If Not _isLoading Then
                _isLoading = True
                _hasAnnouncedLoading = False
                tmrSpinner.Start()
                tmrCheckLoad.Start()
                Me.Invalidate()
            End If
        Catch ex As Exception
            logger.ScriviLog($"SegnalaCaricamentoInCorso: {ex.ToString()}")
        End Try
    End Sub

    Protected Overrides ReadOnly Property CreateParams As CreateParams
        Get
            Try
                Dim cp = MyBase.CreateParams
                cp.Style = cp.Style Or WS_VSCROLL Or WS_BORDER
                Return cp
            Catch ex As Exception
                logger.ScriviLog($"CreateParams Get: {ex.ToString()}")
                Return MyBase.CreateParams
            End Try
        End Get
    End Property

    Protected Overrides Sub WndProc(ByRef m As Message)
        Try
            Dim visibile As Boolean = Me.Visible
            Dim abilitato As Boolean = Me.Enabled

            If m.Msg = WM_DESTROY Then
                If Me.Disposing OrElse Me.IsDisposed Then
                    tmrSpinner.Stop()
                    tmrCheckLoad.Stop()
                    tmrConfigMonitor.Stop()
                    If _hFont <> IntPtr.Zero Then
                        DeleteObject(_hFont)
                        _hFont = IntPtr.Zero
                    End If
                    If _hTitleFont <> IntPtr.Zero Then
                        DeleteObject(_hTitleFont)
                        _hTitleFont = IntPtr.Zero
                    End If
                    If _hHighResFont <> IntPtr.Zero Then
                        DeleteObject(_hHighResFont)
                        _hHighResFont = IntPtr.Zero
                    End If
                    If _tastiera IsNot Nothing Then
                        RemoveHandler _tastiera.KeyDown, AddressOf _tastiera_KeyDown
                        RemoveHandler _tastiera.ControlSinistroPremuto, AddressOf OnCtrlSinistroPremuto
                        RemoveHandler _tastiera.ControlSinistroRilasciato, AddressOf OnCtrlSinistroRilasciato
                    End If
                    _items.Destroy()
                End If
            End If

            Select Case m.Msg
                Case WM_LBUTTONDOWN, WM_MOUSEWHEEL
                    If Not (visibile And abilitato) Then
                        m.Result = IntPtr.Zero
                        Return
                    End If
            End Select

            Select Case m.Msg
                Case WM_PAINT
                    GestisciPaint()
                Case WM_ERASEBKGND
                    m.Result = New IntPtr(1)
                Case WM_VSCROLL
                    GestisciVScroll(m.WParam)
                Case WM_MOUSEWHEEL
                    GestisciMouseWheel(m.WParam)
                Case WM_LBUTTONDOWN
                    GestisciMouseDown(m.LParam)
                Case WM_SIZE
                    CaricaConfigurazione()
                    AggiornaConteggi()
                    RidisegnaDopoRidimensionamento()
                Case WM_SHOWWINDOW
                    MyBase.WndProc(m)
                Case WM_KEYDOWN, WM_SYSKEYDOWN
                    Dim tasto As Keys = CType(CInt(m.WParam), Keys)
                    Select Case tasto
                        Case Keys.Up, Keys.Down, Keys.Left, Keys.Right, Keys.PageUp, Keys.PageDown, Keys.Home, Keys.End
                            If GestisciKeyDown(tasto) Then
                                m.Result = IntPtr.Zero
                                Return
                            End If
                    End Select
                    MyBase.WndProc(m)
                Case WM_KILLFOCUS
                    _isHookActive = False
                    _focusAttivo = False
                    _ctrlPremuto = False
                    _tickUltimoKillFocus = Environment.TickCount
                    AnnullaAnnuncioIngresso()
                    Me.Invalidate()
                Case WM_SETFOCUS
                    _isHookActive = True
                    _focusAttivo = True
                    Dim nuovaTastiera As WGTastiera = WGTastiera.GetInstance()
                    If nuovaTastiera IsNot Nothing Then
                        If Not ReferenceEquals(nuovaTastiera, _tastiera) Then
                            If _tastiera IsNot Nothing Then
                                RemoveHandler _tastiera.KeyDown, AddressOf _tastiera_KeyDown
                                RemoveHandler _tastiera.ControlSinistroPremuto, AddressOf OnCtrlSinistroPremuto
                                RemoveHandler _tastiera.ControlSinistroRilasciato, AddressOf OnCtrlSinistroRilasciato
                            End If
                            _tastiera = nuovaTastiera
                            AddHandler _tastiera.KeyDown, AddressOf _tastiera_KeyDown
                            AddHandler _tastiera.ControlSinistroPremuto, AddressOf OnCtrlSinistroPremuto
                            AddHandler _tastiera.ControlSinistroRilasciato, AddressOf OnCtrlSinistroRilasciato
                        End If
                    End If
                    Try
                        If _touk IsNot Nothing Then _touk.RiassegnaControllo(Me.Handle)
                    Catch exTouk As Exception
                        logger.ScriviLog($"WM_SETFOCUS (RiassegnaControllo): {exTouk.Message}")
                    End Try
                    If Me.Visible Then
                        Dim daUltimoKill As Long = Environment.TickCount - _tickUltimoKillFocus
                        If _tickUltimoKillFocus <> 0 AndAlso daUltimoKill >= IntervalloFocusTransitorio Then
                            _titoloLetto = False
                        End If
                        RichiediAnnuncioIngresso()
                    End If
                    Me.Invalidate()
                Case WM_SEARCH_RESULT
                    Dim foundIndex As Integer = CInt(m.WParam)
                    SetFocusIndex(foundIndex)
                Case WM_SEARCH_NOTFOUND
                    WGSuoni.EmettiDin()
                    lettura.LeggiTesto("nessuna voce trovata")
                Case Else
                    MyBase.WndProc(m)
            End Select
        Catch ex As Exception
            logger.ScriviLog($"WndProc ({m.Msg}): {ex.ToString()}")
        End Try
    End Sub

    Private Sub DisegnaSpinner(hDC As IntPtr, width As Integer, height As Integer)
        Try
            Dim centerX As Integer = width \ 2
            Dim centerY As Integer = height \ 2
            Dim numRaggi As Integer = 12
            Dim stepAngolo As Double = (2 * Math.PI) / numRaggi
            Dim hOldPen As IntPtr = IntPtr.Zero

            For i As Integer = 0 To numRaggi - 1
                Dim distanza As Integer = (i + numRaggi - _spinnerFrame) Mod numRaggi
                Dim hPen As IntPtr = _spinnerPens(distanza)
                hOldPen = SelectObject(hDC, hPen)
                Dim angolo As Double = i * stepAngolo
                Dim x1 As Integer = centerX + CInt(Math.Cos(angolo) * 15)
                Dim y1 As Integer = centerY + CInt(Math.Sin(angolo) * 15)
                Dim x2 As Integer = centerX + CInt(Math.Cos(angolo) * 30)
                Dim y2 As Integer = centerY + CInt(Math.Sin(angolo) * 30)
                MoveToEx(hDC, x1, y1, IntPtr.Zero)
                LineTo(hDC, x2, y2)
                SelectObject(hDC, hOldPen)
            Next
            SetTextColor(hDC, _foreColor)
            SetBkMode(hDC, 1)
            Dim hOldFont As IntPtr = SelectObject(hDC, _hFont)
            If Not String.IsNullOrEmpty(_spinnerTxt) Then
                Dim rectText As New RECT With {.Left = 0, .Top = centerY + 40, .Right = width, .Bottom = height}
                DrawText(hDC, _spinnerTxt, _spinnerTxt.Length, rectText, DT_SINGLELINE Or DT_CENTER)
            End If
            SelectObject(hDC, hOldFont)
        Catch ex As Exception
            logger.ScriviLog($"DisegnaSpinner: {ex.ToString()}")
        End Try
    End Sub

    Private Function AssicuraScratchBitmap(hDCRiferimento As IntPtr, larghezzaRichiesta As Integer, altezzaRichiesta As Integer) As Boolean
        Try
            Dim larghezzaEffettiva As Integer = Math.Max(1, larghezzaRichiesta)
            Dim altezzaEffettiva As Integer = Math.Max(1, altezzaRichiesta)

            If _hScratchBitmap <> IntPtr.Zero AndAlso larghezzaEffettiva <= _scratchWidth AndAlso altezzaEffettiva <= _scratchHeight Then
                Return True
            End If

            Dim nuovaLarghezza As Integer = Math.Max(larghezzaEffettiva, _scratchWidth)
            Dim nuovaAltezza As Integer = Math.Max(altezzaEffettiva, _scratchHeight)

            Dim hNuovoDC As IntPtr = CreateCompatibleDC(hDCRiferimento)
            If hNuovoDC = IntPtr.Zero Then
                Return False
            End If

            Dim hNuovoBitmap As IntPtr = CreateCompatibleBitmap(hDCRiferimento, nuovaLarghezza, nuovaAltezza)
            If hNuovoBitmap = IntPtr.Zero Then
                DeleteDC(hNuovoDC)
                Return False
            End If

            If _hScratchBitmap <> IntPtr.Zero Then
                SelectObject(_hScratchDC, _hOldScratchBitmap)
                DeleteObject(_hScratchBitmap)
            End If
            If _hScratchDC <> IntPtr.Zero Then
                DeleteDC(_hScratchDC)
            End If

            _hScratchDC = hNuovoDC
            _hScratchBitmap = hNuovoBitmap
            _hOldScratchBitmap = SelectObject(_hScratchDC, _hScratchBitmap)
            _scratchWidth = nuovaLarghezza
            _scratchHeight = nuovaAltezza
            Return True
        Catch ex As Exception
            logger.ScriviLog($"AssicuraScratchBitmap: {ex.ToString()}")
            Return False
        End Try
    End Function

    Private Sub RilasciaScratchBitmap()
        Try
            If _hScratchBitmap <> IntPtr.Zero Then
                SelectObject(_hScratchDC, _hOldScratchBitmap)
                DeleteObject(_hScratchBitmap)
                _hScratchBitmap = IntPtr.Zero
            End If
            If _hScratchDC <> IntPtr.Zero Then
                DeleteDC(_hScratchDC)
                _hScratchDC = IntPtr.Zero
            End If
            _scratchWidth = 0
            _scratchHeight = 0
        Catch ex As Exception
            logger.ScriviLog($"RilasciaScratchBitmap: {ex.ToString()}")
        End Try
    End Sub

    Friend Sub InvalidaCacheAltezze()
        Try
            _cacheInfoElementi.Clear()
        Catch ex As Exception
            logger.ScriviLog($"InvalidaCacheAltezze: {ex.ToString()}")
        End Try
    End Sub

    ' Chiamare Invalidate() direttamente da un thread diverso da quello della UI lancia
    ' InvalidOperationException ("cross-thread operation not valid"). WGObjectCollection puo'
    ' essere modificata da qualunque thread del programma (hook, monitor, lettura vocale), quindi
    ' qui si passa sempre dal thread giusto, con BeginInvoke per non bloccare il chiamante.
    Friend Sub InvalidateSicuro()
        Try
            If Me.IsDisposed OrElse Me.Disposing Then Return
            If Not Me.IsHandleCreated Then Return
            If Me.InvokeRequired Then
                Me.BeginInvoke(New MethodInvoker(AddressOf InvalidateSicuro))
            Else
                Me.Invalidate()
            End If
        Catch ex As Exception
            logger.ScriviLog($"InvalidateSicuro: {ex.ToString()}")
        End Try
    End Sub

    Private Function CalcolaInfoElemento(txt As String, larghezzaDisponibile As Integer) As InfoAltezzaElemento
        Dim risultato As New InfoAltezzaElemento With {.Altezza = _itemHeight, .MultiRiga = False}
        Try
            If String.IsNullOrEmpty(txt) Then Return risultato
            Dim hDC As IntPtr = GetDC(IntPtr.Zero)
            If hDC = IntPtr.Zero Then
                Return risultato
            End If
            Try
                Dim hOldFont As IntPtr = SelectObject(hDC, _hFont)
                Try
                    Dim rectSingola As New RECT With {.Left = 0, .Top = 0, .Right = 0, .Bottom = 0}
                    DrawText(hDC, txt, txt.Length, rectSingola, DT_CALCRECT Or DT_SINGLELINE Or DT_NOPREFIX)
                    Dim larghezzaNecessaria As Integer = rectSingola.Right - rectSingola.Left
                    Dim altezzaRigaSingola As Integer = Math.Max(1, rectSingola.Bottom - rectSingola.Top)
                    If larghezzaNecessaria <= larghezzaDisponibile Then
                        Return risultato
                    End If

                    Dim rectMultipla As New RECT With {.Left = 0, .Top = 0, .Right = Math.Max(10, larghezzaDisponibile), .Bottom = 0}
                    DrawText(hDC, txt, txt.Length, rectMultipla, DT_CALCRECT Or DT_WORDBREAK Or DT_NOPREFIX)
                    Dim altezzaTesto As Integer = rectMultipla.Bottom - rectMultipla.Top
                    Dim altezzaCalcolata As Integer = Math.Max(_itemHeight, altezzaTesto + 14)
                    Dim altezzaMassimaConsentita As Integer = Math.Max(_itemHeight, (altezzaRigaSingola * RIGHE_MASSIME_ELEMENTO) + 14)

                    risultato.Altezza = Math.Min(altezzaCalcolata, altezzaMassimaConsentita)
                    risultato.MultiRiga = True
                    Return risultato
                Finally
                    SelectObject(hDC, hOldFont)
                End Try
            Finally
                ReleaseDC(IntPtr.Zero, hDC)
            End Try
        Catch ex As Exception
            logger.ScriviLog($"CalcolaInfoElemento: {ex.ToString()}")
            Return New InfoAltezzaElemento With {.Altezza = _itemHeight, .MultiRiga = False}
        End Try
    End Function

    Private Function OttieniInfoElemento(index As Integer) As InfoAltezzaElemento
        Try
            SyncLock _syncLock
                If _cacheAltezzeWidth <> Me.Width Then
                    _cacheInfoElementi.Clear()
                    _cacheAltezzeWidth = Me.Width
                End If

                Dim info As InfoAltezzaElemento
                If _cacheInfoElementi.TryGetValue(index, info) Then Return info

                Dim itemObj As Object = If(index >= 0 AndAlso index < _items.Count, _items(index), Nothing)
                Dim txt As String = If(itemObj IsNot Nothing, itemObj.ToString(), "")
                Dim larghezzaDisponibile As Integer = Math.Max(10, Me.Width - 4 - 60)
                info = CalcolaInfoElemento(txt, larghezzaDisponibile)
                _cacheInfoElementi(index) = info
                Return info
            End SyncLock
        Catch ex As Exception
            logger.ScriviLog($"OttieniInfoElemento: {ex.ToString()}")
            Return New InfoAltezzaElemento With {.Altezza = _itemHeight, .MultiRiga = False}
        End Try
    End Function

    Private Function OttieniAltezzaElemento(index As Integer) As Integer
        Try
            Return OttieniInfoElemento(index).Altezza
        Catch ex As Exception
            logger.ScriviLog($"OttieniAltezzaElemento: {ex.ToString()}")
            Return _itemHeight
        End Try
    End Function

    Private Function OttieniNumeroColonne() As Integer
        Try
            Dim touk As WGTouk = WGTouk.GetInstance()
            If touk Is Nothing OrElse Not touk.TouchAttivo Then Return 1
            If touk.LayoutAdattivo Then
                If Me.Width < LarghezzaMinimaAssolutaCella * 2 Then Return 1
                Return Math.Max(2, Me.Width \ LarghezzaMinimaCellaTouch)
            End If
            If Me.Width < LarghezzaMinimaCellaTouch * 2 Then Return 1
            Dim colonne As Integer = Me.Width \ LarghezzaMinimaCellaTouch
            Return Math.Max(1, colonne)
        Catch ex As Exception
            logger.ScriviLog($"OttieniNumeroColonne: {ex.ToString()}")
            Return 1
        End Try
    End Function

    Private Function OttieniLineeVisibili(indiceIniziale As Integer) As Integer
        Try
            SyncLock _syncLock
                If _items.Count = 0 Then Return 1
                Dim numeroColonne As Integer = OttieniNumeroColonne()
                If numeroColonne > 1 Then
                    Dim disponibileGriglia As Integer = Math.Max(1, Me.Height - _titoloHeight)
                    Dim righeVisibili As Integer = Math.Max(1, disponibileGriglia \ AltezzaCellaGriglia)
                    Return Math.Max(1, righeVisibili * numeroColonne)
                End If
                Dim disponibile As Integer = Math.Max(1, Me.Height - _titoloHeight)
                Dim yPos As Integer = 0
                Dim i As Integer = Math.Max(0, indiceIniziale)
                Dim conteggio As Integer = 0
                Do While i < _items.Count
                    Dim altezzaElemento As Integer = OttieniAltezzaElemento(i)
                    If yPos + altezzaElemento > disponibile AndAlso conteggio > 0 Then Exit Do
                    yPos += altezzaElemento
                    conteggio += 1
                    i += 1
                    If yPos >= disponibile Then Exit Do
                Loop
                Return Math.Max(1, conteggio)
            End SyncLock
        Catch ex As Exception
            logger.ScriviLog($"OttieniLineeVisibili: {ex.ToString()}")
            Return 1
        End Try
    End Function

    Private Sub GestisciPaint()
        Dim ps As New PAINTSTRUCT()
        Dim hDC As IntPtr = BeginPaint(Me.Handle, ps)
        If hDC = IntPtr.Zero Then
            Return
        End If

        ' Da qui in poi hDC e' stato ottenuto da BeginPaint: qualunque uscita (normale o per
        ' eccezione) DEVE passare da EndPaint nel Finally, altrimenti il DC resta agganciato e,
        ' ripetendosi ad ogni WM_PAINT, esaurisce le risorse GDI del processo nel tempo.
        Try
            If Me.Width <= 0 OrElse Me.Height <= 0 Then
                Return
            End If

            If Me.Width <> _memWidth OrElse Me.Height <> _memHeight Then
                If _hMemDC <> IntPtr.Zero Then
                    SelectObject(_hMemDC, _hOldMemBitmap)
                    DeleteObject(_hMemBitmap)
                    DeleteDC(_hMemDC)
                    _hMemDC = IntPtr.Zero
                    _hMemBitmap = IntPtr.Zero
                End If
                _hMemDC = CreateCompatibleDC(hDC)
                If _hMemDC = IntPtr.Zero Then
                    Return
                End If
                _hMemBitmap = CreateCompatibleBitmap(hDC, Me.Width, Me.Height)
                If _hMemBitmap = IntPtr.Zero Then
                    DeleteDC(_hMemDC)
                    _hMemDC = IntPtr.Zero
                    Return
                End If
                _hOldMemBitmap = SelectObject(_hMemDC, _hMemBitmap)
                _memWidth = Me.Width
                _memHeight = Me.Height
            End If

            If _hMemDC = IntPtr.Zero Then
                Return
            End If

            Dim rectClient As New RECT()
            rectClient.Right = Me.Width
            rectClient.Bottom = Me.Height
            Dim hBrushBack As IntPtr = CreateSolidBrush(_backColor)
            If hBrushBack <> IntPtr.Zero Then
                FillRect(_hMemDC, rectClient, hBrushBack)
                DeleteObject(hBrushBack)
            End If

            If _isLoading Then
                Dim countAttuale As Integer = 0
                SyncLock _syncLock
                    countAttuale = _items.Count
                End SyncLock

                Dim msDaUltimoItem As Double = (DateTime.Now - _lastItemAddTime).TotalMilliseconds

                If countAttuale > 0 AndAlso msDaUltimoItem > 60 Then
                    _isLoading = False
                    _initialLoadComplete = True
                    Try
                        tmrSpinner.Stop()
                        tmrCheckLoad.Stop()
                    Catch exTmr As Exception
                        logger.ScriviLog($"GestisciPaint (stop timer): {exTmr.Message}")
                    End Try
                    AggiornaConteggi()
                    If Me.Visible AndAlso _annuncioDaRipetere AndAlso Not _titoloLetto Then
                        _annuncioDaRipetere = False
                        PianificaAnnuncioIniziale(True)
                    End If
                Else
                    DisegnaSpinner(_hMemDC, rectClient.Right, rectClient.Bottom)
                    BitBlt(hDC, 0, 0, Me.Width, Me.Height, _hMemDC, 0, 0, SRCCOPY)
                    Return
                End If
            End If

            Dim currentY As Integer = 0
            Dim hOldMainFont As IntPtr = IntPtr.Zero

            If Not String.IsNullOrEmpty(_titolo) Then
                Dim r1 As Byte = CByte(_titleBackColor1 And &HFF)
                Dim g1 As Byte = CByte((_titleBackColor1 >> 8) And &HFF)
                Dim b1 As Byte = CByte((_titleBackColor1 >> 16) And &HFF)
                Dim r2 As Byte = CByte(_titleBackColor2 And &HFF)
                Dim g2 As Byte = CByte((_titleBackColor2 >> 8) And &HFF)
                Dim b2 As Byte = CByte((_titleBackColor2 >> 16) And &HFF)
                _vertArray(0).x = 0
                _vertArray(0).y = 0
                _vertArray(0).Red = CUShort(r1) << 8
                _vertArray(0).Green = CUShort(g1) << 8
                _vertArray(0).Blue = CUShort(b1) << 8
                _vertArray(0).Alpha = 0
                _vertArray(1).x = Me.Width
                _vertArray(1).y = _titoloHeight
                _vertArray(1).Red = CUShort(r2) << 8
                _vertArray(1).Green = CUShort(g2) << 8
                _vertArray(1).Blue = CUShort(b2) << 8
                _vertArray(1).Alpha = 0
                _meshArray(0).UpperLeft = 0
                _meshArray(0).LowerRight = 1
                GradientFill(_hMemDC, _vertArray, 2, _meshArray, 1, GRADIENT_FILL_RECT_V)
                SetBkMode(_hMemDC, 1)
                SetTextColor(_hMemDC, _titleForeColor)
                hOldMainFont = SelectObject(_hMemDC, _hTitleFont)
                Dim rectTitolo As New RECT With {.Left = 20, .Top = 0, .Right = Me.Width, .Bottom = _titoloHeight}
                DrawText(_hMemDC, _titolo, _titolo.Length, rectTitolo, DT_SINGLELINE Or DT_VCENTER)
                currentY = _titoloHeight
            End If

            Dim rectBordo As New RECT With {.Top = currentY, .Right = Me.Width, .Bottom = Me.Height}
            DrawEdge(_hMemDC, rectBordo, BDR_SUNKENOUTER Or BDR_RAISEDINNER, BF_RECT)

            SyncLock _syncLock
                If _items.Count > 0 Then
                    Dim startIndex As Integer = _topIndex

                    If hOldMainFont = IntPtr.Zero Then
                        hOldMainFont = SelectObject(_hMemDC, _hFont)
                    Else
                        SelectObject(_hMemDC, _hFont)
                    End If

                    Dim hPenLine As IntPtr = IntPtr.Zero
                    Dim hOldPen As IntPtr = IntPtr.Zero
                    Dim hBrushSel As IntPtr = IntPtr.Zero
                    Dim hBrushFocus As IntPtr = IntPtr.Zero
                    Dim hBrushBackNormal As IntPtr = IntPtr.Zero
                    Try
                        hPenLine = CreatePen(PS_SOLID, 1, _foreColor)
                        If hPenLine <> IntPtr.Zero Then hOldPen = SelectObject(_hMemDC, hPenLine)
                        hBrushSel = CreateSolidBrush(_selectionColor)
                        hBrushFocus = CreateSolidBrush(_focusColor)
                        hBrushBackNormal = CreateSolidBrush(_backColor)

                        SetStretchBltMode(_hMemDC, HALFTONE)

                        Dim numeroColonne As Integer = OttieniNumeroColonne()

                        If numeroColonne > 1 Then
                            Dim larghezzaCella As Integer = Me.Width \ numeroColonne
                            Dim yPosGriglia As Integer = currentY
                            Dim indiceGriglia As Integer = startIndex
                            Do While indiceGriglia < _items.Count AndAlso yPosGriglia < Me.Height
                                Dim colonna As Integer = 0
                                Do While colonna < numeroColonne AndAlso indiceGriglia < _items.Count
                                    Dim xPos As Integer = colonna * larghezzaCella
                                    Dim rectCella As New RECT With {.Left = xPos + 2, .Top = yPosGriglia, .Right = xPos + larghezzaCella - 2, .Bottom = yPosGriglia + AltezzaCellaGriglia}
                                    Dim isFocusedCella As Boolean = (indiceGriglia = _selectedIndex)
                                    Dim isSelectedCella As Boolean = _selectedIndices.Contains(indiceGriglia)

                                    If isSelectedCella AndAlso hBrushSel <> IntPtr.Zero Then FillRect(_hMemDC, rectCella, hBrushSel)
                                    If isFocusedCella AndAlso hBrushFocus <> IntPtr.Zero Then FrameRect(_hMemDC, rectCella, hBrushFocus)

                                    Dim itemObjGriglia As Object = _items(indiceGriglia)
                                    If itemObjGriglia IsNot Nothing Then
                                        Dim txtGriglia As String = itemObjGriglia.ToString()
                                        Dim scaledWidthCella As Integer = rectCella.Right - rectCella.Left
                                        Dim scaledHeightCella As Integer = rectCella.Bottom - rectCella.Top
                                        Dim rectHighResCella As New RECT With {.Right = scaledWidthCella * _resolutionScale, .Bottom = scaledHeightCella * _resolutionScale}
                                        Dim hBrushBackCella As IntPtr = If(isSelectedCella, hBrushSel, hBrushBackNormal)

                                        If AssicuraScratchBitmap(hDC, rectHighResCella.Right, rectHighResCella.Bottom) Then
                                            If hBrushBackCella <> IntPtr.Zero Then FillRect(_hScratchDC, rectHighResCella, hBrushBackCella)
                                            SetBkMode(_hScratchDC, 1)
                                            SetTextColor(_hScratchDC, _foreColor)

                                            Dim hOldItemFontCella As IntPtr = SelectObject(_hScratchDC, _hHighResFont)
                                            Try
                                                Dim leftPaddingCella As Integer = 10 * _resolutionScale
                                                Dim rectTextCella As RECT = rectHighResCella
                                                rectTextCella.Left += leftPaddingCella
                                                rectTextCella.Right -= leftPaddingCella
                                                DrawText(_hScratchDC, txtGriglia, txtGriglia.Length, rectTextCella, DT_WORDBREAK Or DT_CENTER Or DT_END_ELLIPSIS Or DT_NOPREFIX)
                                            Finally
                                                SelectObject(_hScratchDC, hOldItemFontCella)
                                            End Try

                                            StretchBlt(_hMemDC, rectCella.Left, rectCella.Top, scaledWidthCella, scaledHeightCella, _hScratchDC, 0, 0, rectHighResCella.Right, rectHighResCella.Bottom, SRCCOPY)
                                        End If
                                    End If

                                    FrameRect(_hMemDC, rectCella, hBrushBackNormal)

                                    colonna += 1
                                    indiceGriglia += 1
                                Loop
                                yPosGriglia += AltezzaCellaGriglia
                            Loop
                        Else
                            Dim yPos As Integer = currentY
                            Dim i As Integer = startIndex
                            Do While i < _items.Count AndAlso yPos < Me.Height
                                Dim infoElemento As InfoAltezzaElemento = OttieniInfoElemento(i)
                                Dim altezzaElemento As Integer = infoElemento.Altezza
                                Dim rectItem As New RECT With {.Left = 2, .Top = yPos, .Right = Me.Width - 2, .Bottom = yPos + altezzaElemento}
                                Dim isFocused As Boolean = (i = _selectedIndex)
                                Dim isSelected As Boolean = _selectedIndices.Contains(i)

                                If isSelected AndAlso hBrushSel <> IntPtr.Zero Then FillRect(_hMemDC, rectItem, hBrushSel)
                                If isFocused AndAlso hBrushFocus <> IntPtr.Zero Then FrameRect(_hMemDC, rectItem, hBrushFocus)

                                Dim itemObj As Object = _items(i)
                                If itemObj IsNot Nothing Then
                                    Dim txt As String = itemObj.ToString()
                                    Dim txtLen As Integer = txt.Length
                                    Dim scaledWidth As Integer = rectItem.Right - rectItem.Left
                                    Dim scaledHeight As Integer = rectItem.Bottom - rectItem.Top
                                    Dim rectHighRes As New RECT With {.Right = scaledWidth * _resolutionScale, .Bottom = scaledHeight * _resolutionScale}
                                    Dim hBrushBackItem As IntPtr = If(isSelected, hBrushSel, hBrushBackNormal)

                                    If AssicuraScratchBitmap(hDC, rectHighRes.Right, rectHighRes.Bottom) Then
                                        If hBrushBackItem <> IntPtr.Zero Then FillRect(_hScratchDC, rectHighRes, hBrushBackItem)
                                        SetBkMode(_hScratchDC, 1)
                                        SetTextColor(_hScratchDC, _foreColor)

                                        Dim hOldItemFont As IntPtr = SelectObject(_hScratchDC, _hHighResFont)
                                        Try
                                            Dim leftPadding As Integer = 30 * _resolutionScale
                                            Dim rectText As RECT = rectHighRes
                                            rectText.Left += leftPadding
                                            rectText.Right -= leftPadding

                                            If infoElemento.MultiRiga Then
                                                rectText.Top += 4 * _resolutionScale
                                                rectText.Bottom -= 4 * _resolutionScale
                                                DrawText(_hScratchDC, txt, txtLen, rectText, DT_WORDBREAK Or DT_NOPREFIX)
                                            Else
                                                DrawText(_hScratchDC, txt, txtLen, rectText, DT_SINGLELINE Or DT_VCENTER Or DT_END_ELLIPSIS Or DT_NOPREFIX)
                                            End If
                                        Finally
                                            SelectObject(_hScratchDC, hOldItemFont)
                                        End Try

                                        StretchBlt(_hMemDC, rectItem.Left + 2, rectItem.Top, scaledWidth - 4, scaledHeight, _hScratchDC, 0, 0, rectHighRes.Right, rectHighRes.Bottom, SRCCOPY)
                                    Else
                                    End If
                                End If
                                MoveToEx(_hMemDC, rectItem.Left, rectItem.Bottom - 1, IntPtr.Zero)
                                LineTo(_hMemDC, rectItem.Right, rectItem.Bottom - 1)

                                yPos += altezzaElemento
                                i += 1
                            Loop
                        End If
                    Finally
                        If hBrushSel <> IntPtr.Zero Then DeleteObject(hBrushSel)
                        If hBrushFocus <> IntPtr.Zero Then DeleteObject(hBrushFocus)
                        If hBrushBackNormal <> IntPtr.Zero Then DeleteObject(hBrushBackNormal)
                        If hOldPen <> IntPtr.Zero Then SelectObject(_hMemDC, hOldPen)
                        If hPenLine <> IntPtr.Zero Then DeleteObject(hPenLine)
                    End Try
                End If
            End SyncLock

            If hOldMainFont <> IntPtr.Zero Then
                SelectObject(_hMemDC, hOldMainFont)
            End If

            BitBlt(hDC, 0, 0, Me.Width, Me.Height, _hMemDC, 0, 0, SRCCOPY)
        Catch ex As Exception
            logger.ScriviLog($"GestisciPaint: {ex.ToString()}")
        Finally
            EndPaint(Me.Handle, ps)
        End Try
    End Sub

    Private Sub GestisciVScroll(wParam As IntPtr)
        Try
            If _isLoading Then Return

            Dim request As Integer = CInt(wParam) And &HFFFF
            Dim currentPos As Integer = _topIndex
            Dim min As Integer = 0
            Dim max As Integer = 0
            SyncLock _syncLock
                max = Math.Max(0, _items.Count - 1)
            End SyncLock
            Dim largeChange As Integer = OttieniLineeVisibili(_topIndex)
            Dim numeroColonneScroll As Integer = OttieniNumeroColonne()
            Dim passoLinea As Integer = Math.Max(1, numeroColonneScroll)

            Select Case request
                Case SB_LINEUP
                    currentPos -= passoLinea
                Case SB_LINEDOWN
                    currentPos += passoLinea
                Case SB_PAGEUP
                    currentPos -= largeChange
                Case SB_PAGEDOWN
                    currentPos += largeChange
                Case SB_THUMBTRACK, SB_THUMBPOSITION
                    currentPos = (CInt(wParam) >> 16) And &HFFFF
            End Select

            If currentPos < min Then currentPos = min
            If currentPos > max - largeChange + 1 Then currentPos = Math.Max(min, max - largeChange + 1)
            TopIndex = currentPos
        Catch ex As Exception
            logger.ScriviLog($"GestisciVScroll: {ex.ToString()}")
        End Try
    End Sub

    Private Sub GestisciMouseWheel(wParam As IntPtr)
        Try
            If _isLoading Then Return

            Dim delta As Integer = (CInt(wParam) >> 16) And &HFFFF
            If delta > 32768 Then delta = delta - 65536
            Dim scrollAmount As Integer = If(delta > 0, -1, 1)

            Dim newIndex As Integer = 0
            SyncLock _syncLock
                newIndex = Math.Max(0, Math.Min(_selectedIndex + scrollAmount, _items.Count - 1))
            End SyncLock

            SetFocusIndex(newIndex)
        Catch ex As Exception
            logger.ScriviLog($"GestisciMouseWheel: {ex.ToString()}")
        End Try
    End Sub

    ''' <summary>
    ''' Esplorazione tattile: quando un dito tocca lo schermo dentro quest'elenco, sposta il
    ''' fuoco sulla voce che si trova sotto il dito, che viene cosi' letta ad alta voce. Non
    ''' attiva nulla: serve solo per capire dove ci si trova, l'attivazione resta al doppio tocco.
    ''' Le coordinate arrivano riferite allo schermo e vanno convertite in coordinate locali.
    ''' </summary>
    Private Sub _touk_EsplorazioneTocco(xSchermo As Integer, ySchermo As Integer)
        Try
            If Me.IsDisposed OrElse Not Me.IsHandleCreated Then Return
            If Not Me.Visible OrElse Not Me.Enabled Then Return
            If _isLoading Then Return
            If Not PuoElaborareInput() Then Return

            Dim indiceToccato As Integer = IndiceVoceInPunto(New Point(xSchermo, ySchermo))
            If indiceToccato < 0 Then Return
            If indiceToccato = _selectedIndex Then Return
            SetFocusIndex(indiceToccato)
        Catch ex As Exception
            logger.ScriviLog($"_touk_EsplorazioneTocco: {ex.ToString()}")
        End Try
    End Sub

    Private Function IndiceVoceInPunto(puntoSchermo As Point) As Integer
        Try
            If Me.IsDisposed OrElse Not Me.IsHandleCreated Then Return -1

            Dim puntoLocale As Point = Me.PointToClient(puntoSchermo)
            If puntoLocale.X < 0 OrElse puntoLocale.Y < 0 Then Return -1
            If puntoLocale.X >= Me.Width OrElse puntoLocale.Y >= Me.Height Then Return -1

            Dim startY As Integer = If(String.IsNullOrEmpty(_titolo), 0, _titoloHeight)
            ' Anche un tocco sulla fascia del titolo viene ricondotto alla prima voce visibile,
            ' invece di essere ignorato: non deve esistere una zona "morta" dello schermo.
            Dim yEffettiva As Integer = Math.Max(puntoLocale.Y, startY)
            puntoLocale = New Point(puntoLocale.X, yEffettiva)

            Dim indiceToccato As Integer = -1

            SyncLock _syncLock
                If _items.Count = 0 Then Return -1
                Dim numeroColonne As Integer = OttieniNumeroColonne()

                ' L'intera area sotto il titolo viene ripartita tra le voci attualmente
                ' visibili, invece di usare l'altezza reale di ciascuna: cosi' QUALUNQUE punto
                ' dello schermo corrisponde sempre a una voce e si puo' esplorare col dito da
                ' ogni parte, anche quando le voci sono poche e occuperebbero solo la fascia
                ' in alto. Cambia solo il calcolo del punto toccato, non il disegno.
                Dim disponibile As Integer = Math.Max(1, Me.Height - startY)
                Dim vociVisibili As Integer = OttieniLineeVisibili(_topIndex)
                If vociVisibili < 1 Then vociVisibili = 1
                Dim vociRimanenti As Integer = _items.Count - _topIndex
                If vociRimanenti < 1 Then vociRimanenti = 1
                If vociVisibili > vociRimanenti Then vociVisibili = vociRimanenti

                If numeroColonne > 1 Then
                    Dim righeVisibili As Integer = CInt(Math.Ceiling(vociVisibili / CDbl(numeroColonne)))
                    If righeVisibili < 1 Then righeVisibili = 1

                    Dim larghezzaCella As Integer = Math.Max(1, Me.Width \ numeroColonne)
                    Dim altezzaRigaTouch As Integer = Math.Max(1, disponibile \ righeVisibili)

                    Dim colonna As Integer = puntoLocale.X \ larghezzaCella
                    If colonna >= numeroColonne Then colonna = numeroColonne - 1
                    If colonna < 0 Then colonna = 0

                    Dim riga As Integer = (puntoLocale.Y - startY) \ altezzaRigaTouch
                    If riga >= righeVisibili Then riga = righeVisibili - 1
                    If riga < 0 Then riga = 0

                    Dim indiceTentativo As Integer = _topIndex + (riga * numeroColonne) + colonna
                    If indiceTentativo >= _items.Count Then indiceTentativo = _items.Count - 1
                    If indiceTentativo >= 0 Then indiceToccato = indiceTentativo
                Else
                    Dim altezzaVoceTouch As Integer = Math.Max(1, disponibile \ vociVisibili)
                    Dim posizione As Integer = (puntoLocale.Y - startY) \ altezzaVoceTouch
                    If posizione >= vociVisibili Then posizione = vociVisibili - 1
                    If posizione < 0 Then posizione = 0

                    Dim indiceTentativo As Integer = _topIndex + posizione
                    If indiceTentativo >= _items.Count Then indiceTentativo = _items.Count - 1
                    If indiceTentativo >= 0 Then indiceToccato = indiceTentativo
                End If
            End SyncLock

            Return indiceToccato
        Catch ex As Exception
            logger.ScriviLog($"IndiceVoceInPunto: {ex.ToString()}")
            Return -1
        End Try
    End Function

    ''' <summary>
    ''' Identificativo della voce che si trova sotto il dito, chiesto da WGTouk durante
    ''' l'esplorazione tattile. L'identificativo e' l'indice della voce, non la sua posizione
    ''' sullo schermo, quindi resta lo stesso anche se la lista scorre sotto il dito e la voce
    ''' non viene riannunciata; passando su una voce vicina di striscio non viene letta.
    ''' </summary>
    Public Function IdentificativoElementoTattile(puntoSchermo As Point) As String Implements IWGElementoTattile.IdentificativoElementoTattile
        Try
            If Me.IsDisposed OrElse Not Me.IsHandleCreated Then Return Nothing
            If Not Me.Visible OrElse Not Me.Enabled Then Return Nothing
            If _isLoading Then Return Nothing
            If Not PuoElaborareInput() Then Return Nothing

            Dim indice As Integer = IndiceVoceInPunto(puntoSchermo)
            If indice < 0 Then Return Nothing

            Return "voce:" & indice.ToString()
        Catch ex As Exception
            logger.ScriviLog($"IdentificativoElementoTattile: {ex.ToString()}")
            Return Nothing
        End Try
    End Function

    Private Sub GestisciMouseDown(lParam As IntPtr)
        Try
            If _isLoading Then Return

            Dim lp As Integer = CInt(lParam)
            Dim x As Integer = lp And &HFFFF
            Dim y As Integer = (lp >> 16)
            Dim startY As Integer = If(String.IsNullOrEmpty(_titolo), 0, _titoloHeight)

            If y < startY Then Return

            SyncLock _syncLock
                Dim clickedIndex As Integer = -1
                Dim numeroColonneClic As Integer = OttieniNumeroColonne()

                If numeroColonneClic > 1 Then
                    Dim larghezzaCellaClic As Integer = Me.Width \ numeroColonneClic
                    If larghezzaCellaClic > 0 Then
                        Dim colonnaClic As Integer = x \ larghezzaCellaClic
                        If colonnaClic >= numeroColonneClic Then colonnaClic = numeroColonneClic - 1
                        If colonnaClic < 0 Then colonnaClic = 0
                        Dim rigaClic As Integer = (y - startY) \ AltezzaCellaGriglia
                        Dim indiceTentativo As Integer = _topIndex + (rigaClic * numeroColonneClic) + colonnaClic
                        If indiceTentativo >= 0 AndAlso indiceTentativo < _items.Count Then
                            clickedIndex = indiceTentativo
                        End If
                    End If
                Else
                    Dim yPos As Integer = startY
                    Dim i As Integer = _topIndex
                    Do While i < _items.Count
                        Dim altezzaElemento As Integer = OttieniAltezzaElemento(i)
                        If y >= yPos AndAlso y < yPos + altezzaElemento Then
                            clickedIndex = i
                            Exit Do
                        End If
                        yPos += altezzaElemento
                        If yPos > Me.Height Then Exit Do
                        i += 1
                    Loop
                End If

                If clickedIndex >= 0 AndAlso clickedIndex < _items.Count Then
                    SetFocusIndex(clickedIndex)
                    If _selectionMode <> SelectionMode.One Then
                        ToggleSelection(clickedIndex)
                    End If
                End If
            End SyncLock
        Catch ex As Exception
            logger.ScriviLog($"GestisciMouseDown: {ex.ToString()}")
        End Try
    End Sub

    Private Sub shiftpremuto() Handles _tastiera.ShiftPremuto
        Try
            If Me.IsDisposed OrElse Not Me.IsHandleCreated Then Return
            If Not Me.Visible Then Return
            If Not PuoElaborareInput() Then Return
            If Not _selezioneConsentita Then Return
            SyncLock _syncLock
                If _selectionMode = SelectionMode.One Then
                    _selectionMode = SelectionMode.MultiSimple
                    Me.Invalidate()
                    RaiseEvent SelezioneAttivata(Me, EventArgs.Empty)
                    lettura.LeggiTesto("Modalità MultiSelezione attivata")
                Else
                    _selectionMode = SelectionMode.One
                    _selectedIndices.Clear()
                    If _selectedIndex >= 0 Then _selectedIndices.Add(_selectedIndex)
                    Me.Invalidate()
                    RaiseEvent SelezioneDisattivata(Me, EventArgs.Empty)
                    lettura.LeggiTesto("Modalità MultiSelezione disattivata")
                End If
            End SyncLock
        Catch ex As Exception
            logger.ScriviLog($"shiftpremuto: {ex.ToString()}")
        End Try
    End Sub

    Private Function LeggiCicloLista() As Boolean
        Try
            If config.EsisteId("CicloLista") Then
                Dim v As Boolean = True
                Boolean.TryParse(config.LeggiValore("CicloLista"), v)
                Return v
            End If
            Return True
        Catch ex As Exception
            logger.ScriviLog($"LeggiCicloLista: {ex.ToString()}")
            Return True
        End Try
    End Function

    Private Function GestisciKeyDown(tasto As Keys) As Boolean
        Try
            If _isLoading Then
                If tasto = Keys.Up OrElse tasto = Keys.Down OrElse tasto = Keys.PageUp OrElse tasto = Keys.PageDown Then
                    lettura.LeggiTesto("Caricamento in corso")
                    Return True
                End If
                Return False
            End If


            If _ctrlPremuto Then
                Select Case tasto
                    Case Keys.A
                        EseguiSelezionaTutto()
                        Return True
                    Case Keys.D
                        EseguiDeselezionaTutto()
                        Return True
                    Case Keys.I
                        EseguiInformazioniSelezione()
                        Return True
                End Select
                Return False
            End If

            Dim ciclico As Boolean = LeggiCicloLista()
            If _ciclono Then ciclico = False

            Select Case tasto
                Case Keys.Down
                    SyncLock _syncLock
                        If _items.Count = 0 Then Return True
                        If _items.Count = 1 Then
                            LeggiElementoCorrente()
                        Else
                            Dim nextIndex As Integer = _selectedIndex + 1
                            If nextIndex >= _items.Count Then
                                If ciclico Then
                                    nextIndex = 0
                                Else
                                    RaiseEvent FineListaRaggiunta(Me, EventArgs.Empty)
                                    LeggiElementoCorrente()
                                    Return True
                                End If
                            End If
                            ResetSearchBuffer()
                            SetFocusIndex(nextIndex)
                        End If
                    End SyncLock
                    Return True
                Case Keys.Up
                    SyncLock _syncLock
                        If _items.Count = 0 Then Return True
                        If _items.Count = 1 Then
                            LeggiElementoCorrente()
                        Else
                            Dim prevIndex As Integer = _selectedIndex - 1
                            If prevIndex < 0 Then
                                If ciclico Then
                                    prevIndex = _items.Count - 1
                                Else
                                    RaiseEvent InizioListaRaggiunto(Me, EventArgs.Empty)
                                    LeggiElementoCorrente()
                                    Return True
                                End If
                            End If
                            ResetSearchBuffer()
                            SetFocusIndex(prevIndex)
                        End If
                    End SyncLock
                    Return True
                Case Keys.Space
                    If _selectedIndex >= 0 Then
                        If Not _selezioneConsentita Then
                            If lettura IsNot Nothing Then
                                lettura.LeggiTesto("In questo menu non è consentito selezionare più di un elemento")
                            End If
                            Return True
                        End If
                        ToggleSelection(_selectedIndex)
                        Return True
                    End If
                    Return False
                Case Keys.Right
                    RaiseEvent Avanti(Me, EventArgs.Empty)
                    Return True
                Case Keys.Left
                    RaiseEvent Indietro(Me, EventArgs.Empty)
                    Return True
                Case Keys.Home
                    SyncLock _syncLock
                        If _items.Count = 0 Then Return True
                        ResetSearchBuffer()
                        SetFocusIndex(0)
                    End SyncLock
                    Return True
                Case Keys.End
                    SyncLock _syncLock
                        If _items.Count = 0 Then Return True
                        ResetSearchBuffer()
                        SetFocusIndex(_items.Count - 1)
                    End SyncLock
                    Return True
                Case Keys.PageUp
                    SyncLock _syncLock
                        If _items.Count = 0 Then Return True
                    End SyncLock
                    ResetSearchBuffer()
                    GestisciPaginaSu()
                    Return True
                Case Keys.PageDown
                    SyncLock _syncLock
                        If _items.Count = 0 Then Return True
                    End SyncLock
                    ResetSearchBuffer()
                    GestisciPaginaGiu()
                    Return True
                Case Keys.Enter, Keys.Return
                    RaiseEvent InvioPremuto(Me, EventArgs.Empty)
                    Return False
                Case Keys.Escape
                    RaiseEvent EscPremuto(Me, EventArgs.Empty)
                    Return False
                Case Keys.Insert
                    LeggiElementoCorrente()
                    Return True
                Case Keys.Delete
                    ResetSearchBuffer()
                    Return True
                Case Else
                    Return GestioneRicercaLettera(tasto)
            End Select
        Catch ex As Exception
            logger.ScriviLog($"GestisciKeyDown: {ex.ToString()}")
            Return False
        End Try
    End Function

    Private Sub ResetSearchBuffer()
        Try
            searchBuffer = ""
            lastKeyPressTime = DateTime.MinValue
        Catch ex As Exception
            logger.ScriviLog($"ResetSearchBuffer: {ex.ToString()}")
        End Try
    End Sub

    Private Sub EseguiSelezionaTutto()
        Try
            If Not _selezioneConsentita Then _selezioneConsentita = True
            If _selectionMode = SelectionMode.One Then
                _selectionMode = SelectionMode.MultiSimple
                RaiseEvent SelezioneAttivata(Me, EventArgs.Empty)
            End If
            Dim count As Integer = 0
            SyncLock _syncLock
                _selectedIndices.Clear()
                For i As Integer = 0 To _items.Count - 1
                    _selectedIndices.Add(i)
                Next
                count = _items.Count
                Me.Invalidate()
                RaiseEvent SelectionChanged(Me, EventArgs.Empty)
            End SyncLock
            lettura.LeggiTesto("selezionati " & count.ToString())
        Catch ex As Exception
            logger.ScriviLog($"EseguiSelezionaTutto: {ex.ToString()}")
        End Try
    End Sub

    Private Sub EseguiDeselezionaTutto()
        Try
            If Not _selezioneConsentita Then Return
            SyncLock _syncLock
                _selectedIndices.Clear()
                If _selectedIndex >= 0 AndAlso _selectionMode = SelectionMode.One Then
                    _selectedIndices.Add(_selectedIndex)
                End If
                Me.Invalidate()
                RaiseEvent SelectionChanged(Me, EventArgs.Empty)
            End SyncLock
            lettura.LeggiTesto("deselezionato tutto")
        Catch ex As Exception
            logger.ScriviLog($"EseguiDeselezionaTutto: {ex.ToString()}")
        End Try
    End Sub

    Private Sub EseguiInformazioniSelezione()
        Try
            Dim count As Integer = 0
            SyncLock _syncLock
                count = _selectedIndices.Count
            End SyncLock
            lettura.LeggiTesto(count.ToString() & " selezionati")
        Catch ex As Exception
            logger.ScriviLog($"EseguiInformazioniSelezione: {ex.ToString()}")
        End Try
    End Sub

    Public Property Titolo As String
        Get
            Try
                Return _titolo
            Catch ex As Exception
                logger.ScriviLog($"Titolo Get: {ex.ToString()}")
                Return ""
            End Try
        End Get
        Set(value As String)
            Try
                If _titolo <> value Then
                    _titolo = value
                    Me.Invalidate()
                    If Me.Visible AndAlso Me.IsHandleCreated Then
                        _titoloLetto = False
                        RichiediAnnuncioIngresso()
                    End If
                End If
            Catch ex As Exception
                logger.ScriviLog($"Titolo Set: {ex.ToString()}")
            End Try
        End Set
    End Property

    Private Sub CaricaConfigurazione()
        Try
            Dim hDC As IntPtr = GetDC(IntPtr.Zero)
            Dim dpiY As Integer = 96
            If hDC <> IntPtr.Zero Then
                dpiY = GetDeviceCaps(hDC, LOGPIXELSY)
                ReleaseDC(IntPtr.Zero, hDC)
            End If
            Dim scaleFactor As Single = dpiY / 96.0F
            Dim screenHeight As Integer = GetSystemMetrics(SM_CYSCREEN)

            Dim baseFontSize As Single = 14 * scaleFactor
            If screenHeight >= 2160 Then
                baseFontSize = 48 * scaleFactor
            ElseIf screenHeight >= 1440 Then
                baseFontSize = 32 * scaleFactor
            ElseIf screenHeight >= 1080 Then
                baseFontSize = 24 * scaleFactor
            ElseIf screenHeight >= 768 Then
                baseFontSize = 18 * scaleFactor
            End If

            Dim highContrast As Boolean = False

            If _fontNamePtr = IntPtr.Zero Then
                _fontNamePtr = Marshal.StringToHGlobalUni("Segoe UI")
            End If

            _leggiTitoloIngresso = True
            ' Sfondo e testo seguono il tema chiaro/scuro di Windows, come il resto dell'interfaccia; una voce di
            ' configurazione (ColoreSfondo/ColoreTesto, sotto) o l'alto contrasto (ConfiguraAccessibilita) restano piu' forti.
            If WGTema.Scuro Then
                _backColor = &H0
                _foreColor = &HFFFFFF
            Else
                _backColor = &HFFFFFF
                _foreColor = &H0
            End If
            _selectionColor = &HFFCCAA
            _focusColor = &HFF0000

            If config IsNot Nothing Then
                If config.EsisteId("FontDimensione") Then
                    Dim configVal As Single
                    If Single.TryParse(config.LeggiValore("FontDimensione"), configVal) Then
                        baseFontSize = configVal * scaleFactor
                    End If
                End If
                If config.EsisteId("ContrastoAlto") Then
                    Boolean.TryParse(config.LeggiValore("ContrastoAlto"), highContrast)
                End If
                If config.EsisteId("MenuCarattere") Then
                    Dim nFont As String = config.LeggiValore("MenuCarattere")
                    If Not String.IsNullOrEmpty(nFont) Then
                        If _fontNamePtr <> IntPtr.Zero Then
                            Marshal.FreeHGlobal(_fontNamePtr)
                        End If
                        _fontNamePtr = Marshal.StringToHGlobalUni(nFont)
                    End If
                End If
                If config.EsisteId("LeggiTitoloIngresso") Then
                    Boolean.TryParse(config.LeggiValore("LeggiTitoloIngresso"), _leggiTitoloIngresso)
                End If
                If config.EsisteId("ColoreSfondo") Then
                    Integer.TryParse(config.LeggiValore("ColoreSfondo"), _backColor)
                End If
                If config.EsisteId("ColoreTesto") Then
                    Integer.TryParse(config.LeggiValore("ColoreTesto"), _foreColor)
                End If
                If config.EsisteId("ColoreSelezione") Then
                    Integer.TryParse(config.LeggiValore("ColoreSelezione"), _selectionColor)
                End If
                If config.EsisteId("ColoreFuoco") Then
                    Integer.TryParse(config.LeggiValore("ColoreFuoco"), _focusColor)
                End If
            End If

            ConfiguraAccessibilita(baseFontSize, highContrast)
        Catch ex As Exception
            logger.ScriviLog($"CaricaConfigurazione: {ex.ToString()}")
            ConfiguraAccessibilita(24, False)
        End Try
    End Sub

    Public ReadOnly Property IsUpdating As Boolean
        Get
            Try
                Return _isUpdating
            Catch ex As Exception
                logger.ScriviLog($"IsUpdating Get: {ex.ToString()}")
                Return False
            End Try
        End Get
    End Property

    <DesignerSerializationVisibility(DesignerSerializationVisibility.Content)>
    Public ReadOnly Property Items As WGObjectCollection
        Get
            Try
                Return _items
            Catch ex As Exception
                logger.ScriviLog($"Items Get: {ex.ToString()}")
                Return Nothing
            End Try
        End Get
    End Property

    Public Property SelectionMode As SelectionMode
        Get
            Try
                Return _selectionMode
            Catch ex As Exception
                logger.ScriviLog($"SelectionMode Get: {ex.ToString()}")
                Return SelectionMode.One
            End Try
        End Get
        Set(value As SelectionMode)
            Try
                If Not _selezioneConsentita AndAlso (value = SelectionMode.MultiSimple OrElse value = SelectionMode.MultiExtended) Then
                    value = SelectionMode.One
                End If
                _selectionMode = value
                _selectedIndices.Clear()
                Me.Invalidate()
                RaiseEvent SelectionChanged(Me, EventArgs.Empty)
            Catch ex As Exception
                logger.ScriviLog($"SelectionMode Set: {ex.ToString()}")
            End Try
        End Set
    End Property

    Public Property SelezioneConsentita As Boolean
        Get
            Try
                Return _selezioneConsentita
            Catch ex As Exception
                logger.ScriviLog($"SelezioneConsentita Get: {ex.ToString()}")
                Return True
            End Try
        End Get
        Set(value As Boolean)
            Try
                _selezioneConsentita = value
                If Not value AndAlso (_selectionMode = SelectionMode.MultiSimple OrElse _selectionMode = SelectionMode.MultiExtended) Then
                    _selectionMode = SelectionMode.One
                    _selectedIndices.Clear()
                    If _selectedIndex >= 0 Then _selectedIndices.Add(_selectedIndex)
                    RaiseEvent SelezioneDisattivata(Me, EventArgs.Empty)
                    Me.Invalidate()
                    RaiseEvent SelectionChanged(Me, EventArgs.Empty)
                End If
            Catch ex As Exception
                logger.ScriviLog($"SelezioneConsentita Set: {ex.ToString()}")
            End Try
        End Set
    End Property

    Public Property ciclono As Boolean
        Get
            Try
                Return _ciclono
            Catch ex As Exception
                logger.ScriviLog($"ciclono Get: {ex.ToString()}")
                Return False
            End Try
        End Get
        Set(value As Boolean)
            Try
                _ciclono = value
            Catch ex As Exception
                logger.ScriviLog($"ciclono Set: {ex.ToString()}")
            End Try
        End Set
    End Property

    Public Property SelectedIndex As Integer
        Get
            Try
                Return _selectedIndex
            Catch ex As Exception
                logger.ScriviLog($"SelectedIndex Get: {ex.ToString()}")
                Return -1
            End Try
        End Get
        Set(value As Integer)
            Try
                Dim cambiato As Boolean = False
                SyncLock _syncLock
                    If value < -1 OrElse value >= _items.Count Then Return
                    If _selectionMode = SelectionMode.One Then
                        _selectedIndices.Clear()
                        If value >= 0 Then _selectedIndices.Add(value)
                    End If
                    If _selectedIndex <> value Then
                        _selectedIndex = value
                        EnsureVisible(_selectedIndex)
                        Me.Invalidate()
                        cambiato = True
                    End If
                End SyncLock
                If cambiato Then
                    RaiseEvent SelectedIndexChanged(Me, EventArgs.Empty)
                    RaiseEvent SelectionChanged(Me, EventArgs.Empty)
                End If
            Catch ex As Exception
                logger.ScriviLog($"SelectedIndex Set: {ex.ToString()}")
            End Try
        End Set
    End Property

    Public ReadOnly Property SelectedItem As Object
        Get
            Try
                SyncLock _syncLock
                    If _selectedIndex >= 0 AndAlso _selectedIndex < _items.Count Then
                        Return _items(_selectedIndex)
                    End If
                    Return Nothing
                End SyncLock
            Catch ex As Exception
                logger.ScriviLog($"SelectedItem Get: {ex.ToString()}")
                Return Nothing
            End Try
        End Get
    End Property

    Public Property TopIndex As Integer
        Get
            Try
                Return _topIndex
            Catch ex As Exception
                logger.ScriviLog($"TopIndex Get: {ex.ToString()}")
                Return 0
            End Try
        End Get
        Set(value As Integer)
            Try
                SyncLock _syncLock
                    If value < 0 Then value = 0
                    Dim numeroColonne As Integer = OttieniNumeroColonne()
                    If numeroColonne > 1 Then
                        value -= (value Mod numeroColonne)
                    End If
                    Dim maxTop As Integer = Math.Max(0, _items.Count - 1)
                    If numeroColonne > 1 Then
                        maxTop -= (maxTop Mod numeroColonne)
                    End If
                    If value > maxTop Then value = maxTop
                    If _topIndex <> value Then
                        _topIndex = value
                        AggiornaConteggi()
                        Me.Invalidate()
                    End If
                End SyncLock
            Catch ex As Exception
                logger.ScriviLog($"TopIndex Set: {ex.ToString()}")
            End Try
        End Set
    End Property

    Public Sub BeginUpdate()
        Try
            _isUpdating = True
        Catch ex As Exception
            logger.ScriviLog($"BeginUpdate: {ex.ToString()}")
        End Try
    End Sub

    Public Sub EndUpdate()
        Try
            _isUpdating = False
            AggiornaConteggi()
            Me.Invalidate()
        Catch ex As Exception
            logger.ScriviLog($"EndUpdate: {ex.ToString()}")
        End Try
    End Sub

    Public Sub CaricaElementiThreadSafe(elementi As IEnumerable(Of Object), Optional sostituisci As Boolean = True, Optional indiceDaSelezionare As Integer = -1)
        Try
            If Me.IsDisposed OrElse Me.Disposing Then Return
            If Not Me.IsHandleCreated Then
                Return
            End If
            If Me.InvokeRequired Then
                Try
                    Me.Invoke(Sub() CaricaElementiThreadSafeInterno(elementi, sostituisci, indiceDaSelezionare))
                Catch ex As ObjectDisposedException
                    logger.ScriviLog($"CaricaElementiThreadSafe (Invoke ObjectDisposed): {ex.ToString()}")
                Catch ex As InvalidOperationException
                    logger.ScriviLog($"CaricaElementiThreadSafe (Invoke InvalidOperation): {ex.ToString()}")
                Catch ex As Exception
                    logger.ScriviLog($"CaricaElementiThreadSafe (Invoke): {ex.ToString()}")
                End Try
                Return
            End If
            CaricaElementiThreadSafeInterno(elementi, sostituisci, indiceDaSelezionare)
        Catch ex As Exception
            logger.ScriviLog($"CaricaElementiThreadSafe: {ex.ToString()}")
        End Try
    End Sub

    Private Sub CaricaElementiThreadSafeInterno(elementi As IEnumerable(Of Object), sostituisci As Boolean, indiceDaSelezionare As Integer)
        Try
            Me.BeginUpdate()
            Try
                SyncLock _syncLock
                    If sostituisci Then
                        _items.Clear()
                    End If
                    If elementi IsNot Nothing Then
                        For Each itm In elementi
                            If itm Is Nothing Then Continue For
                            _items.Add(itm)
                        Next
                    End If
                End SyncLock
            Finally
                Me.EndUpdate()
            End Try

            If indiceDaSelezionare >= 0 Then
                Dim countAttuale As Integer = 0
                SyncLock _syncLock
                    countAttuale = _items.Count
                End SyncLock
                If indiceDaSelezionare < countAttuale Then
                    Me.SelectedIndex = indiceDaSelezionare
                End If
            End If
        Catch ex As Exception
            logger.ScriviLog($"CaricaElementiThreadSafeInterno: {ex.ToString()}")
        End Try
    End Sub

    Private Sub ConfiguraAccessibilita(fontSize As Single, highContrast As Boolean)
        Try
            _highContrast = highContrast
            If highContrast Then
                _backColor = &H0
                _foreColor = &HFFFFFF
                _selectionColor = &HFF0000
                _focusColor = &HFFFF00
                _titleBackColor1 = &H0
                _titleBackColor2 = &H0
                _titleForeColor = &HFFFFFF
            End If

            If _hFont <> IntPtr.Zero Then DeleteObject(_hFont)
            If _hTitleFont <> IntPtr.Zero Then DeleteObject(_hTitleFont)
            If _hHighResFont <> IntPtr.Zero Then DeleteObject(_hHighResFont)

            Dim currentDPI As Integer = 96
            Dim hDC As IntPtr = GetDC(IntPtr.Zero)
            If hDC <> IntPtr.Zero Then
                currentDPI = GetDeviceCaps(hDC, LOGPIXELSY)
                ReleaseDC(IntPtr.Zero, hDC)
            End If

            Dim logicalHeightMain As Integer = CInt(-Math.Round((fontSize * currentDPI) / 72.0F))
            _hFont = CreateFontPtr(logicalHeightMain, 0, 0, 0, 400, 0, 0, 0, 1, 0, 0, 4, 0, _fontNamePtr)

            Dim titleSize As Single = fontSize * 1.5F
            Dim logicalHeightTitle As Integer = CInt(-Math.Round((titleSize * currentDPI) / 72.0F))
            _hTitleFont = CreateFontPtr(logicalHeightTitle, 0, 0, 0, 700, 0, 0, 0, 1, 0, 0, 4, 0, _fontNamePtr)

            Dim highResSize As Single = fontSize * _resolutionScale
            Dim logicalHeightHighRes As Integer = CInt(-Math.Round((highResSize * currentDPI) / 72.0F))
            _hHighResFont = CreateFontPtr(logicalHeightHighRes, 0, 0, 0, 400, 0, 0, 0, 1, 0, 0, 4, 0, _fontNamePtr)

            _itemHeight = Math.Max(25, CInt(fontSize * 2))
            _titoloHeight = Math.Max(40, CInt(titleSize * 2.5F))
            InvalidaCacheAltezze()
            Me.Invalidate()
        Catch ex As Exception
            logger.ScriviLog($"ConfiguraAccessibilita: {ex.ToString()}")
        End Try
    End Sub

    Public Sub SelectAll()
        Try
            If _selectionMode = SelectionMode.One OrElse Not _selezioneConsentita Then Return
            SyncLock _syncLock
                _selectedIndices.Clear()
                For i As Integer = 0 To _items.Count - 1
                    _selectedIndices.Add(i)
                Next
                Me.Invalidate()
            End SyncLock
            RaiseEvent SelectionChanged(Me, EventArgs.Empty)
        Catch ex As Exception
            logger.ScriviLog($"SelectAll: {ex.ToString()}")
        End Try
    End Sub

    Public Sub ClearSelected()
        Try
            SyncLock _syncLock
                _selectedIndices.Clear()
                If _selectedIndex >= 0 AndAlso _selectionMode = SelectionMode.One Then
                    _selectedIndices.Add(_selectedIndex)
                End If
                Me.Invalidate()
            End SyncLock
            RaiseEvent SelectionChanged(Me, EventArgs.Empty)
        Catch ex As Exception
            logger.ScriviLog($"ClearSelected: {ex.ToString()}")
        End Try
    End Sub

    Public Sub SetSelected(index As Integer, value As Boolean)
        Try
            If Not _selezioneConsentita AndAlso (_selectionMode = SelectionMode.MultiSimple OrElse _selectionMode = SelectionMode.MultiExtended) Then Return
            SyncLock _syncLock
                If index < 0 OrElse index >= _items.Count Then Return
                If _selectionMode = SelectionMode.One Then
                    If value Then
                        _selectedIndices.Clear()
                        _selectedIndices.Add(index)
                        _selectedIndex = index
                        EnsureVisible(index)
                    End If
                Else
                    If value Then
                        _selectedIndices.Add(index)
                    Else
                        _selectedIndices.Remove(index)
                    End If
                End If
                Me.Invalidate()
            End SyncLock
            RaiseEvent SelectionChanged(Me, EventArgs.Empty)
        Catch ex As Exception
            logger.ScriviLog($"SetSelected: {ex.ToString()}")
        End Try
    End Sub

    Public Function GetSelected(index As Integer) As Boolean
        Try
            SyncLock _syncLock
                If index >= 0 AndAlso index < _items.Count Then
                    Return _selectedIndices.Contains(index)
                End If
                Return False
            End SyncLock
        Catch ex As Exception
            logger.ScriviLog($"GetSelected: {ex.ToString()}")
            Return False
        End Try
    End Function

    Private Sub ToggleSelection(index As Integer)
        Try
            If _selectionMode = SelectionMode.One OrElse Not _selezioneConsentita Then Return
            Dim aggiunto As Boolean = False
            SyncLock _syncLock
                If _selectedIndices.Contains(index) Then
                    _selectedIndices.Remove(index)
                    aggiunto = False
                Else
                    _selectedIndices.Add(index)
                    aggiunto = True
                End If
                Me.Invalidate()
            End SyncLock
            RaiseEvent SelectionChanged(Me, EventArgs.Empty)
            _suoni.ClickClack()
            If lettura IsNot Nothing Then
                lettura.LeggiTesto(If(aggiunto, "selezionato", "deselezionato"))
            End If
        Catch ex As Exception
            logger.ScriviLog($"ToggleSelection: {ex.ToString()}")
        End Try
    End Sub

    Private Sub SetFocusIndex(index As Integer)
        Try
            Dim indiceCambiato As Boolean = False
            Dim modoUno As Boolean = False
            SyncLock _syncLock
                If index < 0 OrElse index >= _items.Count Then Return
                If _selectedIndex <> index Then
                    _selectedIndex = index
                    modoUno = (_selectionMode = SelectionMode.One)
                    If modoUno Then
                        _selectedIndices.Clear()
                        _selectedIndices.Add(index)
                    End If
                    EnsureVisible(index)
                    Me.Invalidate()
                    indiceCambiato = True
                End If
            End SyncLock
            If indiceCambiato Then
                RaiseEvent SelectedIndexChanged(Me, EventArgs.Empty)
                If modoUno Then RaiseEvent SelectionChanged(Me, EventArgs.Empty)
            End If
            LeggiElementoCorrente()
        Catch ex As Exception
            logger.ScriviLog($"SetFocusIndex: {ex.ToString()}")
        End Try
    End Sub

    Private Sub EnsureVisible(index As Integer)
        Try
            SyncLock _syncLock
                If index < 0 OrElse index >= _items.Count Then Return

                Dim numeroColonneVisibili As Integer = OttieniNumeroColonne()
                If numeroColonneVisibili > 1 Then
                    Dim riga As Integer = index \ numeroColonneVisibili
                    Dim rigaTop As Integer = _topIndex \ numeroColonneVisibili
                    Dim disponibileGriglia As Integer = Math.Max(1, Me.Height - _titoloHeight)
                    Dim righeVisibili As Integer = Math.Max(1, disponibileGriglia \ AltezzaCellaGriglia)
                    If riga < rigaTop Then
                        TopIndex = riga * numeroColonneVisibili
                    ElseIf riga >= rigaTop + righeVisibili Then
                        TopIndex = (riga - righeVisibili + 1) * numeroColonneVisibili
                    End If
                    Return
                End If

                If index < _topIndex Then
                    TopIndex = index
                    Return
                End If

                Dim disponibile As Integer = Math.Max(1, Me.Height - _titoloHeight)
                Dim yPos As Integer = 0
                Dim i As Integer = _topIndex
                Dim rientra As Boolean = False
                Do While i <= index
                    Dim altezzaElemento As Integer = OttieniAltezzaElemento(i)
                    If i = index Then
                        rientra = (yPos + altezzaElemento) <= disponibile
                        Exit Do
                    End If
                    yPos += altezzaElemento
                    i += 1
                Loop

                If Not rientra Then
                    Dim nuovoTop As Integer = index
                    Dim altezzaAccumulata As Integer = OttieniAltezzaElemento(index)
                    Do While nuovoTop > 0
                        Dim altezzaPrecedente As Integer = OttieniAltezzaElemento(nuovoTop - 1)
                        If altezzaAccumulata + altezzaPrecedente > disponibile Then Exit Do
                        altezzaAccumulata += altezzaPrecedente
                        nuovoTop -= 1
                    Loop
                    TopIndex = nuovoTop
                End If
            End SyncLock
        Catch ex As Exception
            logger.ScriviLog($"EnsureVisible: {ex.ToString()}")
        End Try
    End Sub

    Private Function GestioneRicercaLettera(tasto As Keys) As Boolean
        Try
            Dim code As Integer = CInt(tasto)
            If (code >= Keys.A AndAlso code <= Keys.Z) OrElse (code >= Keys.D0 AndAlso code <= Keys.D9) OrElse (code >= Keys.NumPad0 AndAlso code <= Keys.NumPad9) Then
                Dim c As Char
                If code >= Keys.NumPad0 AndAlso code <= Keys.NumPad9 Then
                    c = ChrW(AscW("0"c) + (code - Keys.NumPad0))
                Else
                    c = ChrW(code)
                End If

                Dim timeSinceLast As Double = (DateTime.Now - lastKeyPressTime).TotalMilliseconds
                If timeSinceLast > 1000 Then searchBuffer = ""

                searchBuffer &= c.ToString().ToLowerInvariant()
                lastKeyPressTime = DateTime.Now

                Dim itemsText() As String
                Dim searchStr As String
                Dim startIdx As Integer
                Dim ownHandle As IntPtr
                Dim soloPrimaLettera As Boolean = (searchBuffer.Length = 1)

                SyncLock _syncLock
                    If _items.Count = 0 Then Return True
                    ReDim itemsText(_items.Count - 1)
                    For i As Integer = 0 To _items.Count - 1
                        itemsText(i) = If(_items(i) IsNot Nothing, _items(i).ToString(), "")
                    Next
                    searchStr = searchBuffer
                    startIdx = _selectedIndex
                    ownHandle = Me.Handle
                End SyncLock

                Dim searchDataObj As New SearchData With {
                    .ItemsText = itemsText,
                    .SearchText = searchStr,
                    .StartIndex = startIdx,
                    .OwnerHandle = ownHandle,
                    .SoloPrimaLettera = soloPrimaLettera
                }
                ThreadPool.QueueUserWorkItem(AddressOf ThreadCercaFunc, searchDataObj)
                Return True
            End If
            Return False
        Catch ex As Exception
            logger.ScriviLog($"GestioneRicercaLettera: {ex.ToString()}")
            Return False
        End Try
    End Function

    Private Sub GestisciPaginaSu()
        Try
            SyncLock _syncLock
                If _items.Count = 0 Then Return
                Dim numeroColonnePagina As Integer = OttieniNumeroColonne()
                If numeroColonnePagina > 1 Then
                    Dim righeVisibiliPagina As Integer = Math.Max(1, OttieniLineeVisibili(_topIndex) \ numeroColonnePagina)
                    Dim newIndexGriglia As Integer = Math.Max(0, _selectedIndex - (righeVisibiliPagina * numeroColonnePagina))
                    SetFocusIndex(newIndexGriglia)
                    Return
                End If
                Dim disponibile As Integer = Math.Max(1, Me.Height - _titoloHeight)
                Dim newIndex As Integer = _selectedIndex
                Dim altezzaAccumulata As Integer = 0
                Do While newIndex > 0
                    Dim altezzaElemento As Integer = OttieniAltezzaElemento(newIndex - 1)
                    If altezzaAccumulata + altezzaElemento > disponibile Then Exit Do
                    altezzaAccumulata += altezzaElemento
                    newIndex -= 1
                Loop
                If newIndex = _selectedIndex AndAlso newIndex > 0 Then newIndex -= 1
                SetFocusIndex(newIndex)
            End SyncLock
        Catch ex As Exception
            logger.ScriviLog($"GestisciPaginaSu: {ex.ToString()}")
        End Try
    End Sub

    Private Sub GestisciPaginaGiu()
        Try
            SyncLock _syncLock
                If _items.Count = 0 Then Return
                Dim numeroColonnePagina As Integer = OttieniNumeroColonne()
                If numeroColonnePagina > 1 Then
                    Dim righeVisibiliPagina As Integer = Math.Max(1, OttieniLineeVisibili(_topIndex) \ numeroColonnePagina)
                    Dim newIndexGriglia As Integer = Math.Min(_items.Count - 1, _selectedIndex + (righeVisibiliPagina * numeroColonnePagina))
                    SetFocusIndex(newIndexGriglia)
                    Return
                End If
                Dim disponibile As Integer = Math.Max(1, Me.Height - _titoloHeight)
                Dim newIndex As Integer = _selectedIndex
                Dim altezzaAccumulata As Integer = 0
                Do While newIndex < _items.Count - 1
                    Dim altezzaElemento As Integer = OttieniAltezzaElemento(newIndex + 1)
                    If altezzaAccumulata + altezzaElemento > disponibile Then Exit Do
                    altezzaAccumulata += altezzaElemento
                    newIndex += 1
                Loop
                If newIndex = _selectedIndex AndAlso newIndex < _items.Count - 1 Then newIndex += 1
                SetFocusIndex(newIndex)
            End SyncLock
        Catch ex As Exception
            logger.ScriviLog($"GestisciPaginaGiu: {ex.ToString()}")
        End Try
    End Sub

    Private Sub RidisegnaDopoRidimensionamento()
        Try
            If Me.IsDisposed OrElse Not Me.IsHandleCreated Then Return
            Dim indiceCorrente As Integer = -1
            SyncLock _syncLock
                indiceCorrente = _selectedIndex
            End SyncLock
            If indiceCorrente >= 0 Then EnsureVisible(indiceCorrente)
            Me.Invalidate()
        Catch ex As Exception
            logger.ScriviLog($"RidisegnaDopoRidimensionamento: {ex.ToString()}")
        End Try
    End Sub

    Private Sub AggiornaConteggi()
        Try
            If _isUpdating Then Return
            Dim total As Integer = 0
            SyncLock _syncLock
                total = _items.Count
            End SyncLock
            If total = 0 Then
                ShowScrollBar(Me.Handle, SB_VERT, False)
                Return
            End If
            Dim visibleLines As Integer = OttieniLineeVisibili(_topIndex)
            If total <= visibleLines Then
                ShowScrollBar(Me.Handle, SB_VERT, False)
            Else
                Dim si As New SCROLLINFO()
                si.cbSize = Marshal.SizeOf(GetType(SCROLLINFO))
                si.fMask = SIF_ALL
                si.nMin = 0
                si.nMax = total - 1
                si.nPage = visibleLines
                SyncLock _syncLock
                    si.nPos = _topIndex
                End SyncLock
                SetScrollInfo(Me.Handle, SB_VERT, si, True)
                ShowScrollBar(Me.Handle, SB_VERT, True)
            End If
        Catch ex As Exception
            logger.ScriviLog($"AggiornaConteggi: {ex.ToString()}")
        End Try
    End Sub

    Private Sub SalvaStatoPrimaDiClear()
        Try
            SyncLock _syncLock
                _listaBackup.Clear()
                _selectedItemsBackup.Clear()
                For i As Integer = 0 To _items.Count - 1
                    Dim val = _items(i)
                    If val IsNot Nothing Then
                        Dim s As String = val.ToString()
                        _listaBackup.Add(s)
                        If _selectedIndices.Contains(i) Then
                            _selectedItemsBackup.Add(s)
                        End If
                    End If
                Next
                _indiceBackup = _selectedIndex
            End SyncLock
        Catch ex As Exception
            logger.ScriviLog($"SalvaStatoPrimaDiClear: {ex.ToString()}")
        End Try
    End Sub

    Private Sub ResetPuntatori()
        Try
            _selectedIndices.Clear()
            _selectedIndex = -1
            _topIndex = 0
        Catch ex As Exception
            logger.ScriviLog($"ResetPuntatori: {ex.ToString()}")
        End Try
    End Sub

    Private Sub GestisciFineCaricamento()
        Try
            SyncLock _syncLock
                If _listaBackup.Count > 0 AndAlso _items.Count > 0 Then
                    Dim currentItems As New List(Of String)()
                    For i As Integer = 0 To _items.Count - 1
                        Dim val = _items(i)
                        If val IsNot Nothing Then currentItems.Add(val.ToString())
                    Next

                    Dim tuttiUguali As Boolean = (_listaBackup.Count = currentItems.Count)
                    If tuttiUguali Then
                        For i As Integer = 0 To _listaBackup.Count - 1
                            If _listaBackup(i) <> currentItems(i) Then
                                tuttiUguali = False
                                Exit For
                            End If
                        Next
                    End If

                    If tuttiUguali Then
                        _selectedIndices.Clear()
                        For i As Integer = 0 To currentItems.Count - 1
                            If _selectedItemsBackup.Contains(currentItems(i)) Then
                                _selectedIndices.Add(i)
                            End If
                        Next
                        If _indiceBackup >= 0 AndAlso _indiceBackup < _items.Count Then
                            _selectedIndex = _indiceBackup
                            EnsureVisible(_selectedIndex)
                        End If
                    Else
                        If _selectedIndices.Count = 0 AndAlso _selectedIndex = -1 Then
                            _selectedIndex = 0
                            _selectedIndices.Add(0)
                        End If
                    End If
                End If
            End SyncLock

        Catch ex As Exception
            logger.ScriviLog($"GestisciFineCaricamento: {ex.ToString()}")
        End Try
    End Sub

    Private Sub GestisciRimozione()
        Try
            SyncLock _syncLock
                If _items.Count = 0 Then
                    ResetPuntatori()
                    Return
                End If
                If _selectedIndex >= _items.Count Then
                    _selectedIndex = _items.Count - 1
                End If
                If _selectedIndex >= 0 Then
                    _selectedIndices.Clear()
                    _selectedIndices.Add(_selectedIndex)
                    EnsureVisible(_selectedIndex)
                End If
            End SyncLock
        Catch ex As Exception
            logger.ScriviLog($"GestisciRimozione: {ex.ToString()}")
        End Try
    End Sub

    Private Sub ControllaSelezioneIniziale()
        Try
            SyncLock _syncLock
                If _items.Count > 0 AndAlso _selectedIndex = -1 AndAlso _selectedIndices.Count = 0 Then
                    _selectedIndex = 0
                    _selectedIndices.Add(0)
                End If
            End SyncLock
        Catch ex As Exception
            logger.ScriviLog($"ControllaSelezioneIniziale: {ex.ToString()}")
        End Try
    End Sub

    Public Sub ImpostaFuocoInvisibile()
        Try
            _eraDisattivato = True
            Me.Invalidate()
        Catch ex As Exception
            logger.ScriviLog($"ImpostaFuocoInvisibile: {ex.ToString()}")
        End Try
    End Sub

    Public Sub RipristinaFuoco()
        Try
            _eraDisattivato = False
            Me.Invalidate()
        Catch ex As Exception
            logger.ScriviLog($"RipristinaFuoco: {ex.ToString()}")
        End Try
    End Sub

    Public Function OttieniStatoSpinner() As Boolean
        Try
            Return _isLoading
        Catch ex As Exception
            logger.ScriviLog($"OttieniStatoSpinner: {ex.ToString()}")
            Return False
        End Try
    End Function
    Protected Overrides Function IsInputKey(keyData As Keys) As Boolean
        Try
            Select Case keyData And Keys.KeyCode
                Case Keys.Up, Keys.Down, Keys.Left, Keys.Right, Keys.PageUp, Keys.PageDown, Keys.Home, Keys.End
                    Return True
            End Select
            Return MyBase.IsInputKey(keyData)
        Catch ex As Exception
            logger.ScriviLog($"IsInputKey: {ex.ToString()}")
            Return MyBase.IsInputKey(keyData)
        End Try
    End Function

    Protected Overrides Sub OnHandleCreated(e As EventArgs)
        Try
            MyBase.OnHandleCreated(e)
            If _tastiera IsNot Nothing Then
                RemoveHandler _tastiera.KeyDown, AddressOf _tastiera_KeyDown
                AddHandler _tastiera.KeyDown, AddressOf _tastiera_KeyDown
                RemoveHandler _tastiera.ControlSinistroPremuto, AddressOf OnCtrlSinistroPremuto
                AddHandler _tastiera.ControlSinistroPremuto, AddressOf OnCtrlSinistroPremuto
                RemoveHandler _tastiera.ControlSinistroRilasciato, AddressOf OnCtrlSinistroRilasciato
                AddHandler _tastiera.ControlSinistroRilasciato, AddressOf OnCtrlSinistroRilasciato
            End If
            If _touk IsNot Nothing Then
                RemoveHandler _touk.FlickAvanti, AddressOf _touk_FlickAvanti
                AddHandler _touk.FlickAvanti, AddressOf _touk_FlickAvanti
                RemoveHandler _touk.FlickIndietro, AddressOf _touk_FlickIndietro
                AddHandler _touk.FlickIndietro, AddressOf _touk_FlickIndietro
                RemoveHandler _touk.FlickSu, AddressOf _touk_FlickSu
                AddHandler _touk.FlickSu, AddressOf _touk_FlickSu
                RemoveHandler _touk.FlickGiu, AddressOf _touk_FlickGiu
                AddHandler _touk.FlickGiu, AddressOf _touk_FlickGiu
                RemoveHandler _touk.TapDoppio, AddressOf _touk_TapDoppio
                AddHandler _touk.TapDoppio, AddressOf _touk_TapDoppio
                RemoveHandler _touk.TapDoppioProlungato, AddressOf _touk_TapDoppioProlungato
                AddHandler _touk.TapDoppioProlungato, AddressOf _touk_TapDoppioProlungato
                RemoveHandler _touk.EscapeGesto, AddressOf _touk_EscapeGesto
                AddHandler _touk.EscapeGesto, AddressOf _touk_EscapeGesto
                RemoveHandler _touk.StatoCambiato, AddressOf _touk_StatoCambiato
                AddHandler _touk.StatoCambiato, AddressOf _touk_StatoCambiato
                RemoveHandler _touk.EsplorazioneTocco, AddressOf _touk_EsplorazioneTocco
                AddHandler _touk.EsplorazioneTocco, AddressOf _touk_EsplorazioneTocco
            End If
            If Me.Visible AndAlso _attesaCaricamentoDopoVisibilita Then
                Dim count As Integer = 0
                SyncLock _syncLock
                    count = _items.Count
                End SyncLock
                If count > 0 Then
                    If _annuncioDaRipetere Then
                        PianificaAnnuncioIniziale(True)
                    Else
                        PianificaAnnuncioIniziale()
                    End If
                End If
            End If
        Catch ex As Exception
            logger.ScriviLog($"OnHandleCreated: {ex.ToString()}")
        End Try
    End Sub

    Protected Overrides Sub OnHandleDestroyed(e As EventArgs)
        Try
            If Me.Disposing OrElse Me.IsDisposed Then
                If _tastiera IsNot Nothing Then
                    RemoveHandler _tastiera.KeyDown, AddressOf _tastiera_KeyDown
                    RemoveHandler _tastiera.ControlSinistroPremuto, AddressOf OnCtrlSinistroPremuto
                    RemoveHandler _tastiera.ControlSinistroRilasciato, AddressOf OnCtrlSinistroRilasciato
                End If
                If _touk IsNot Nothing Then
                    RemoveHandler _touk.FlickAvanti, AddressOf _touk_FlickAvanti
                    RemoveHandler _touk.FlickIndietro, AddressOf _touk_FlickIndietro
                    RemoveHandler _touk.FlickSu, AddressOf _touk_FlickSu
                    RemoveHandler _touk.FlickGiu, AddressOf _touk_FlickGiu
                    RemoveHandler _touk.TapDoppio, AddressOf _touk_TapDoppio
                    RemoveHandler _touk.TapDoppioProlungato, AddressOf _touk_TapDoppioProlungato
                    RemoveHandler _touk.EscapeGesto, AddressOf _touk_EscapeGesto
                    RemoveHandler _touk.StatoCambiato, AddressOf _touk_StatoCambiato
                    RemoveHandler _touk.EsplorazioneTocco, AddressOf _touk_EsplorazioneTocco
                End If
            End If
        Catch ex As Exception
            logger.ScriviLog($"OnHandleDestroyed: {ex.ToString()}")
        Finally
            MyBase.OnHandleDestroyed(e)
        End Try
    End Sub
    Protected Overrides Sub Dispose(disposing As Boolean)
        Try
            If disposing Then
                If _tastiera IsNot Nothing Then
                    RemoveHandler _tastiera.KeyDown, AddressOf _tastiera_KeyDown
                    RemoveHandler _tastiera.ControlSinistroPremuto, AddressOf OnCtrlSinistroPremuto
                    RemoveHandler _tastiera.ControlSinistroRilasciato, AddressOf OnCtrlSinistroRilasciato
                End If
                If _touk IsNot Nothing Then
                    RemoveHandler _touk.FlickAvanti, AddressOf _touk_FlickAvanti
                    RemoveHandler _touk.FlickIndietro, AddressOf _touk_FlickIndietro
                    RemoveHandler _touk.FlickSu, AddressOf _touk_FlickSu
                    RemoveHandler _touk.FlickGiu, AddressOf _touk_FlickGiu
                    RemoveHandler _touk.TapDoppio, AddressOf _touk_TapDoppio
                    RemoveHandler _touk.TapDoppioProlungato, AddressOf _touk_TapDoppioProlungato
                    RemoveHandler _touk.EscapeGesto, AddressOf _touk_EscapeGesto
                    RemoveHandler _touk.StatoCambiato, AddressOf _touk_StatoCambiato
                    RemoveHandler _touk.EsplorazioneTocco, AddressOf _touk_EsplorazioneTocco
                End If
                _annuncioDaRipetere = False
                _annuncioRichiesto = False
                If _suoni IsNot Nothing Then _suoni.Dispose()
                tmrAnnuncio.Stop() : tmrAnnuncio.Dispose()
                tmrSpinner.Stop() : tmrSpinner.Dispose()
                tmrCheckLoad.Stop() : tmrCheckLoad.Dispose()
                tmrConfigMonitor.Stop() : tmrConfigMonitor.Dispose()

                For i As Integer = 0 To 11
                    If _spinnerPens(i) <> IntPtr.Zero Then
                        DeleteObject(_spinnerPens(i))
                        _spinnerPens(i) = IntPtr.Zero
                    End If
                Next
                If _hFont <> IntPtr.Zero Then DeleteObject(_hFont) : _hFont = IntPtr.Zero
                If _hTitleFont <> IntPtr.Zero Then DeleteObject(_hTitleFont) : _hTitleFont = IntPtr.Zero
                If _hHighResFont <> IntPtr.Zero Then DeleteObject(_hHighResFont) : _hHighResFont = IntPtr.Zero
                If _hMemBitmap <> IntPtr.Zero Then
                    SelectObject(_hMemDC, _hOldMemBitmap)
                    DeleteObject(_hMemBitmap)
                    _hMemBitmap = IntPtr.Zero
                End If
                If _hMemDC <> IntPtr.Zero Then DeleteDC(_hMemDC) : _hMemDC = IntPtr.Zero
                RilasciaScratchBitmap()
                If _fontNamePtr <> IntPtr.Zero Then Marshal.FreeHGlobal(_fontNamePtr) : _fontNamePtr = IntPtr.Zero
            End If
        Catch ex As Exception
            logger.ScriviLog($"Dispose: {ex.ToString()}")
        Finally
            MyBase.Dispose(disposing)
        End Try
    End Sub
End Class