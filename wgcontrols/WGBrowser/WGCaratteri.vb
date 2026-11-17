Imports System.Collections.Generic
Imports System.Runtime.InteropServices
Imports System.Text
Imports System.Windows.Forms
Imports wgcontrols

' Traduzione dei tasti premuti in caratteri pronunciabili e gestione dei layout di tastiera.
Public NotInheritable Class WGCaratteri
    Private Shared ReadOnly log As New Logger()

    Private Shared ReadOnly mappaSimboliNormali As New Dictionary(Of Keys, String) From {
        {Keys.D1, "1"}, {Keys.D2, "2"}, {Keys.D3, "3"}, {Keys.D4, "4"}, {Keys.D5, "5"},
        {Keys.D6, "6"}, {Keys.D7, "7"}, {Keys.D8, "8"}, {Keys.D9, "9"}, {Keys.D0, "0"},
        {Keys.OemMinus, "'"}, {Keys.Oemplus, "ì"}, {Keys.OemOpenBrackets, "è"},
        {Keys.OemCloseBrackets, "+"}, {Keys.OemPipe, "ù"}, {Keys.OemSemicolon, "ò"},
        {Keys.OemQuotes, "à"}, {Keys.Oemcomma, ","}, {Keys.OemPeriod, "."},
        {Keys.OemQuestion, "-"}, {Keys.Space, " "}, {Keys.OemBackslash, "<"},
        {Keys.NumPad0, "0"}, {Keys.NumPad1, "1"}, {Keys.NumPad2, "2"}, {Keys.NumPad3, "3"}, {Keys.NumPad4, "4"},
        {Keys.NumPad5, "5"}, {Keys.NumPad6, "6"}, {Keys.NumPad7, "7"}, {Keys.NumPad8, "8"}, {Keys.NumPad9, "9"},
        {Keys.Multiply, "*"}, {Keys.Add, "+"}, {Keys.Subtract, "-"}, {Keys.Divide, "/"}, {Keys.Decimal, ","}
    }

    Private Shared ReadOnly mappaSimboliShift As New Dictionary(Of Keys, String) From {
        {Keys.D1, "!"}, {Keys.D2, """"}, {Keys.D3, "£"}, {Keys.D4, "$"}, {Keys.D5, "%"},
        {Keys.D6, "&"}, {Keys.D7, "/"}, {Keys.D8, "("}, {Keys.D9, ")"}, {Keys.D0, "="},
        {Keys.OemMinus, "?"}, {Keys.Oemplus, "^"}, {Keys.OemOpenBrackets, "é"},
        {Keys.OemCloseBrackets, "*"}, {Keys.OemPipe, ChrW(167)}, {Keys.OemSemicolon, "ç"},
        {Keys.OemQuotes, ChrW(176)}, {Keys.Oemcomma, ";"}, {Keys.OemPeriod, ":"},
        {Keys.OemQuestion, "_"}, {Keys.Space, " "}, {Keys.OemBackslash, ">"}
    }

    Private Shared ReadOnly mappaSimboliAltDestro As New Dictionary(Of Keys, String) From {
        {Keys.OemSemicolon, "@"}, {Keys.OemQuotes, "#"}, {Keys.OemOpenBrackets, "["},
        {Keys.OemCloseBrackets, "]"}, {Keys.E, "€"}
    }

    Private Shared ReadOnly accentiCombinanti As New Dictionary(Of Char, Char) From {
        {"^"c, ChrW(&H302)}, {ChrW(&H2C6), ChrW(&H302)},
        {ChrW(&HB4), ChrW(&H301)}, {"'"c, ChrW(&H301)}, {ChrW(&H2CA), ChrW(&H301)},
        {"`"c, ChrW(&H300)}, {ChrW(&H2CB), ChrW(&H300)},
        {ChrW(&HA8), ChrW(&H308)}, {""""c, ChrW(&H308)},
        {"~"c, ChrW(&H303)}, {ChrW(&H2DC), ChrW(&H303)},
        {ChrW(&H2C7), ChrW(&H30C)}, {ChrW(&H2D8), ChrW(&H306)},
        {ChrW(&HB0), ChrW(&H30A)}, {ChrW(&H2DA), ChrW(&H30A)},
        {ChrW(&HB8), ChrW(&H327)}, {ChrW(&H2DB), ChrW(&H328)},
        {ChrW(&H2DD), ChrW(&H30B)}, {ChrW(&H2D9), ChrW(&H307)}, {ChrW(&HAF), ChrW(&H304)}
    }

    <DllImport("user32.dll")>
    Private Shared Function MapVirtualKey(uCode As UInteger, uMapType As UInteger) As UInteger
    End Function

    <DllImport("user32.dll", CharSet:=CharSet.Unicode)>
    Private Shared Function ToUnicodeEx(wVirtKey As UInteger, wScanCode As UInteger, lpKeyState As Byte(), <Out, MarshalAs(UnmanagedType.LPWStr)> pwszBuff As StringBuilder, cchBuff As Integer, wFlags As UInteger, dwhkl As IntPtr) As Integer
    End Function

    <DllImport("user32.dll")>
    Private Shared Function GetKeyboardLayout(idThread As UInteger) As IntPtr
    End Function

    <DllImport("user32.dll")>
    Private Shared Function GetForegroundWindow() As IntPtr
    End Function

    <DllImport("user32.dll")>
    Private Shared Function GetWindowThreadProcessId(hWnd As IntPtr, ByRef lpdwProcessId As UInteger) As UInteger
    End Function

    Private Sub New()
    End Sub

    Public Shared Function TastoSenzaCarattere(tasto As Keys) As Boolean
        Try
            Select Case tasto
                Case Keys.Back, Keys.Tab, Keys.Enter, Keys.Escape, Keys.Delete, Keys.Insert,
                     Keys.Left, Keys.Right, Keys.Up, Keys.Down, Keys.Home, Keys.End, Keys.PageUp, Keys.PageDown,
                     Keys.ShiftKey, Keys.LShiftKey, Keys.RShiftKey, Keys.ControlKey, Keys.LControlKey, Keys.RControlKey,
                     Keys.Menu, Keys.LMenu, Keys.RMenu, Keys.LWin, Keys.RWin, Keys.Apps, Keys.CapsLock, Keys.NumLock, Keys.Scroll,
                     Keys.PrintScreen, Keys.Pause, Keys.Clear, Keys.BrowserBack, Keys.BrowserForward, Keys.BrowserRefresh,
                     Keys.BrowserHome, Keys.BrowserSearch, Keys.BrowserFavorites, Keys.BrowserStop, Keys.VolumeDown, Keys.VolumeUp,
                     Keys.VolumeMute, Keys.MediaNextTrack, Keys.MediaPlayPause, Keys.MediaPreviousTrack, Keys.MediaStop, Keys.Sleep
                    Return True
            End Select
            If tasto >= Keys.F1 AndAlso tasto <= Keys.F24 Then Return True
            Return False
        Catch ex As Exception
            log.ScriviLog("WGCaratteri.TastoSenzaCarattere: " & ex.Message)
            Return True
        End Try
    End Function

    Private Shared Function LayoutTastiera() As IntPtr
        Try
            Dim processo As UInteger = 0
            Dim finestra As IntPtr = GetForegroundWindow()
            Dim thread As UInteger = 0
            If finestra <> IntPtr.Zero Then thread = GetWindowThreadProcessId(finestra, processo)
            Return GetKeyboardLayout(thread)
        Catch ex As Exception
            log.ScriviLog("WGCaratteri.LayoutTastiera: " & ex.Message)
            Return GetKeyboardLayout(0)
        End Try
    End Function

    Public Shared Function RisolviCarattere(tasto As Keys, shift As Boolean, altGr As Boolean, ByRef accentoInSospeso As String) As String
        Try
            If accentoInSospeso Is Nothing Then accentoInSospeso = ""
            If tasto = Keys.Back OrElse tasto = Keys.Escape OrElse tasto = Keys.Enter OrElse tasto = Keys.Tab Then
                accentoInSospeso = ""
                Return ""
            End If
            If TastoSenzaCarattere(tasto) Then Return ""
            Dim codice As UInteger = CUInt(tasto And Keys.KeyCode)
            Dim stato(255) As Byte
            If shift Then
                stato(&H10) = &H80
                stato(&HA0) = &H80
            End If
            If altGr Then
                stato(&H11) = &H80
                stato(&H12) = &H80
                stato(&HA2) = &H80
                stato(&HA5) = &H80
            End If
            If Control.IsKeyLocked(Keys.CapsLock) Then stato(&H14) = 1
            If Control.IsKeyLocked(Keys.NumLock) Then stato(&H90) = 1
            Dim sb As New StringBuilder(8)
            Dim risultato As Integer = ToUnicodeEx(codice, MapVirtualKey(codice, 0), stato, sb, sb.Capacity, 4UI, LayoutTastiera())
            If risultato < 0 Then
                Dim morto As String = If(sb.Length > 0, sb.ToString(0, 1), "")
                If morto = "" Then Return ""
                If accentoInSospeso <> "" Then
                    Dim doppio As String = accentoInSospeso & morto
                    accentoInSospeso = ""
                    Return doppio
                End If
                accentoInSospeso = morto
                Return ""
            End If
            If risultato > 0 Then
                Dim s As String = sb.ToString(0, Math.Min(risultato, sb.Length))
                If s.Length > 0 AndAlso Not Char.IsControl(s(0)) Then Return ComponiConAccento(s, accentoInSospeso)
            End If
        Catch ex As Exception
            log.ScriviLog("WGCaratteri.RisolviCarattere: " & ex.Message)
        End Try
        Try
            Dim riserva As String = CarattereDiRiserva(tasto, shift, altGr)
            If riserva <> "" Then Return ComponiConAccento(riserva, accentoInSospeso)
        Catch ex As Exception
            log.ScriviLog("WGCaratteri.RisolviCarattere riserva: " & ex.Message)
        End Try
        Return ""
    End Function

    Private Shared Function CarattereDiRiserva(tasto As Keys, shift As Boolean, altGr As Boolean) As String
        Try
            If altGr Then
                If mappaSimboliAltDestro.ContainsKey(tasto) Then Return mappaSimboliAltDestro(tasto)
                Return ""
            End If
            Dim maiuscolo As Boolean = shift Xor Control.IsKeyLocked(Keys.CapsLock)
            If tasto >= Keys.A AndAlso tasto <= Keys.Z Then Return If(maiuscolo, tasto.ToString().ToUpperInvariant(), tasto.ToString().ToLowerInvariant())
            If shift AndAlso mappaSimboliShift.ContainsKey(tasto) Then Return mappaSimboliShift(tasto)
            If mappaSimboliNormali.ContainsKey(tasto) Then Return mappaSimboliNormali(tasto)
        Catch ex As Exception
            log.ScriviLog("WGCaratteri.CarattereDiRiserva: " & ex.Message)
        End Try
        Return ""
    End Function

    Private Shared Function ComponiConAccento(testo As String, ByRef accentoInSospeso As String) As String
        Try
            If String.IsNullOrEmpty(accentoInSospeso) Then Return testo
            Dim accento As String = accentoInSospeso
            accentoInSospeso = ""
            If testo = " " Then Return accento
            Dim combinante As Char
            If Not accentiCombinanti.TryGetValue(accento(0), combinante) Then Return accento & testo
            Dim composto As String = (testo & combinante).Normalize(NormalizationForm.FormC)
            If composto.Length = 1 Then Return composto
            Return accento & testo
        Catch ex As Exception
            log.ScriviLog("WGCaratteri.ComponiConAccento: " & ex.Message)
            accentoInSospeso = ""
            Return testo
        End Try
    End Function

    Public Shared Function TraduciCarattere(c As Char, Optional pronunciaSegni As Boolean = True) As String
        Try
            Dim s As String = c.ToString()
            If Not pronunciaSegni AndAlso (Char.IsPunctuation(c) OrElse Char.IsSymbol(c)) Then Return s
            If AscW(c) = 160 Then Return "Spazio"
            ' "emme" invece della sola lettera: a voce letta in fretta "m" ed "n" si confondono facilmente.
            If c = "m"c OrElse c = "M"c Then Return "emme"
            Return wgcontrols.WGTastiera.NomeSimbolo(c)
        Catch ex As Exception
            log.ScriviLog("WGCaratteri.TraduciCarattere: " & ex.Message)
            Return c.ToString()
        End Try
    End Function

    Public Shared Function NomeCarattere(c As Char, pronunciaSegni As Boolean) As String
        Try
            If Char.IsLetter(c) AndAlso Char.IsUpper(c) Then Return c.ToString() & " maiuscola"
            Return TraduciCarattere(c, pronunciaSegni)
        Catch ex As Exception
            log.ScriviLog("WGCaratteri.NomeCarattere: " & ex.Message)
            Return c.ToString()
        End Try
    End Function

    Private Shared ReadOnly CodiciDaScansione As New Dictionary(Of UInteger, String) From {
        {&H1UI, "Escape"}, {&H2UI, "Digit1"}, {&H3UI, "Digit2"}, {&H4UI, "Digit3"}, {&H5UI, "Digit4"}, {&H6UI, "Digit5"},
        {&H7UI, "Digit6"}, {&H8UI, "Digit7"}, {&H9UI, "Digit8"}, {&HAUI, "Digit9"}, {&HBUI, "Digit0"}, {&HCUI, "Minus"},
        {&HDUI, "Equal"}, {&HEUI, "Backspace"}, {&HFUI, "Tab"}, {&H10UI, "KeyQ"}, {&H11UI, "KeyW"}, {&H12UI, "KeyE"},
        {&H13UI, "KeyR"}, {&H14UI, "KeyT"}, {&H15UI, "KeyY"}, {&H16UI, "KeyU"}, {&H17UI, "KeyI"}, {&H18UI, "KeyO"},
        {&H19UI, "KeyP"}, {&H1AUI, "BracketLeft"}, {&H1BUI, "BracketRight"}, {&H1CUI, "Enter"}, {&H1DUI, "ControlLeft"},
        {&H1EUI, "KeyA"}, {&H1FUI, "KeyS"}, {&H20UI, "KeyD"}, {&H21UI, "KeyF"}, {&H22UI, "KeyG"}, {&H23UI, "KeyH"},
        {&H24UI, "KeyJ"}, {&H25UI, "KeyK"}, {&H26UI, "KeyL"}, {&H27UI, "Semicolon"}, {&H28UI, "Quote"}, {&H29UI, "Backquote"},
        {&H2AUI, "ShiftLeft"}, {&H2BUI, "Backslash"}, {&H2CUI, "KeyZ"}, {&H2DUI, "KeyX"}, {&H2EUI, "KeyC"}, {&H2FUI, "KeyV"},
        {&H30UI, "KeyB"}, {&H31UI, "KeyN"}, {&H32UI, "KeyM"}, {&H33UI, "Comma"}, {&H34UI, "Period"}, {&H35UI, "Slash"},
        {&H36UI, "ShiftRight"}, {&H38UI, "AltLeft"}, {&H39UI, "Space"}, {&H3AUI, "CapsLock"}, {&H56UI, "IntlBackslash"}
    }

    Private Shared ReadOnly CodiciDaTasto As New Dictionary(Of Integer, String) From {
        {&H8, "Backspace"}, {&H9, "Tab"}, {&HD, "Enter"}, {&H10, "ShiftLeft"}, {&H11, "ControlLeft"}, {&H12, "AltLeft"},
        {&H13, "Pause"}, {&H14, "CapsLock"}, {&H1B, "Escape"}, {&H20, "Space"}, {&H21, "PageUp"}, {&H22, "PageDown"},
        {&H23, "End"}, {&H24, "Home"}, {&H25, "ArrowLeft"}, {&H26, "ArrowUp"}, {&H27, "ArrowRight"}, {&H28, "ArrowDown"},
        {&H2C, "PrintScreen"}, {&H2D, "Insert"}, {&H2E, "Delete"}, {&H5B, "MetaLeft"}, {&H5C, "MetaRight"}, {&H5D, "ContextMenu"},
        {&H60, "Numpad0"}, {&H61, "Numpad1"}, {&H62, "Numpad2"}, {&H63, "Numpad3"}, {&H64, "Numpad4"}, {&H65, "Numpad5"},
        {&H66, "Numpad6"}, {&H67, "Numpad7"}, {&H68, "Numpad8"}, {&H69, "Numpad9"}, {&H6A, "NumpadMultiply"}, {&H6B, "NumpadAdd"},
        {&H6D, "NumpadSubtract"}, {&H6E, "NumpadDecimal"}, {&H6F, "NumpadDivide"}, {&H90, "NumLock"}, {&H91, "ScrollLock"},
        {&HA0, "ShiftLeft"}, {&HA1, "ShiftRight"}, {&HA2, "ControlLeft"}, {&HA3, "ControlRight"}, {&HA4, "AltLeft"}, {&HA5, "AltRight"}
    }

    Private Shared ReadOnly ChiaviDaTasto As New Dictionary(Of Integer, String) From {
        {&H8, "Backspace"}, {&H9, "Tab"}, {&HD, "Enter"}, {&H10, "Shift"}, {&H11, "Control"}, {&H12, "Alt"},
        {&H13, "Pause"}, {&H14, "CapsLock"}, {&H1B, "Escape"}, {&H20, " "}, {&H21, "PageUp"}, {&H22, "PageDown"},
        {&H23, "End"}, {&H24, "Home"}, {&H25, "ArrowLeft"}, {&H26, "ArrowUp"}, {&H27, "ArrowRight"}, {&H28, "ArrowDown"},
        {&H2C, "PrintScreen"}, {&H2D, "Insert"}, {&H2E, "Delete"}, {&H5B, "Meta"}, {&H5C, "Meta"}, {&H5D, "ContextMenu"},
        {&H6A, "*"}, {&H6B, "+"}, {&H6D, "-"}, {&H6F, "/"}, {&H90, "NumLock"}, {&H91, "ScrollLock"},
        {&HA0, "Shift"}, {&HA1, "Shift"}, {&HA2, "Control"}, {&HA3, "Control"}, {&HA4, "Alt"}, {&HA5, "AltGraph"}
    }

    Public Shared Function CodiceDom(codiceVirtuale As Integer) As String
        Try
            Dim codice As String = Nothing
            If codiceVirtuale >= &H70 AndAlso codiceVirtuale <= &H87 Then Return "F" & (codiceVirtuale - &H6F).ToString()
            If CodiciDaTasto.TryGetValue(codiceVirtuale, codice) Then Return codice
            If codiceVirtuale >= &H41 AndAlso codiceVirtuale <= &H5A Then
                Dim scansioneLettera As UInteger = MapVirtualKey(CUInt(codiceVirtuale), 0UI)
                If CodiciDaScansione.TryGetValue(scansioneLettera, codice) Then Return codice
                Return "Key" & ChrW(codiceVirtuale)
            End If
            If codiceVirtuale >= &H30 AndAlso codiceVirtuale <= &H39 Then Return "Digit" & ChrW(codiceVirtuale)
            Dim scansione As UInteger = MapVirtualKey(CUInt(codiceVirtuale), 0UI)
            If CodiciDaScansione.TryGetValue(scansione, codice) Then Return codice
        Catch ex As Exception
            log.ScriviLog("WGCaratteri.CodiceDom: " & ex.Message)
        End Try
        Return ""
    End Function

    Public Shared Function ChiaveDom(codiceVirtuale As Integer, testo As String, shift As Boolean) As String
        Try
            If Not String.IsNullOrEmpty(testo) AndAlso testo <> vbCr AndAlso Not Char.IsControl(testo(0)) Then Return testo
            Dim chiave As String = Nothing
            If codiceVirtuale >= &H70 AndAlso codiceVirtuale <= &H87 Then Return "F" & (codiceVirtuale - &H6F).ToString()
            If ChiaviDaTasto.TryGetValue(codiceVirtuale, chiave) Then Return chiave
            If codiceVirtuale >= &H41 AndAlso codiceVirtuale <= &H5A Then
                Dim lettera As String = ChrW(codiceVirtuale).ToString()
                Return If(shift, lettera.ToUpperInvariant(), lettera.ToLowerInvariant())
            End If
            If codiceVirtuale >= &H30 AndAlso codiceVirtuale <= &H39 Then Return ChrW(codiceVirtuale).ToString()
            If codiceVirtuale >= &H60 AndAlso codiceVirtuale <= &H69 Then Return ChrW(&H30 + codiceVirtuale - &H60).ToString()
            If codiceVirtuale = &H6E Then Return ","
        Catch ex As Exception
            log.ScriviLog("WGCaratteri.ChiaveDom: " & ex.Message)
        End Try
        Return "Unidentified"
    End Function

    Public Shared Function PosizioneTasto(codiceVirtuale As Integer) As Integer
        Try
            If codiceVirtuale >= &H60 AndAlso codiceVirtuale <= &H6F Then Return 3
            Select Case codiceVirtuale
                Case &HA0, &HA2, &HA4, &H5B : Return 1
                Case &HA1, &HA3, &HA5, &H5C : Return 2
            End Select
        Catch ex As Exception
            log.ScriviLog("WGCaratteri.PosizioneTasto: " & ex.Message)
        End Try
        Return 0
    End Function

    Public Shared Function NomeAccento(accento As String) As String
        Try
            If String.IsNullOrEmpty(accento) Then Return ""
            Select Case accento(0)
                Case "^"c, ChrW(&H2C6) : Return "accento circonflesso"
                Case ChrW(&HB4), "'"c, ChrW(&H2CA) : Return "accento acuto"
                Case "`"c, ChrW(&H2CB) : Return "accento grave"
                Case ChrW(&HA8), """"c : Return "dieresi"
                Case "~"c, ChrW(&H2DC) : Return "tilde"
                Case ChrW(&H2C7) : Return "caron"
                Case ChrW(&HB8) : Return "cediglia"
            End Select
            Return TraduciCarattere(accento(0), True)
        Catch ex As Exception
            log.ScriviLog("WGCaratteri.NomeAccento: " & ex.Message)
            Return ""
        End Try
    End Function
End Class
