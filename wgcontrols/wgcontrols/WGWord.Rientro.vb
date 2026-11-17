Imports System.Runtime.InteropServices
Imports System.Windows.Forms

Partial Public Class WGWord

    <DllImport("user32.dll", EntryPoint:="EnableWindow")>
    Private Shared Function AbilitaFinestraNativa(hWnd As IntPtr, bEnable As Boolean) As Boolean
    End Function

    <DllImport("user32.dll", EntryPoint:="ShowWindow")>
    Private Shared Function MostraFinestraNativa(hWnd As IntPtr, nCmdShow As Integer) As Boolean
    End Function

    Private Const SW_SHOW_RIENTRO As Integer = 5

    Public Sub RiprendiFocus()
        RiprendiFocus(True)
    End Sub

    Public Sub RiprendiFocus(annuncia As Boolean)
        Try
            If Me.IsDisposed OrElse Me.Disposing Then Return

            If Me.InvokeRequired Then
                Try
                    _annuncioRientro = annuncia
                    Me.BeginInvoke(New MethodInvoker(AddressOf EseguiRientroFocus))
                Catch exRinvio As Exception
                    logger.ScriviLog("WGWord.RiprendiFocus (rinvio): " & exRinvio.Message)
                End Try
                Return
            End If

            _annuncioRientro = annuncia
            EseguiRientroFocus()
        Catch ex As Exception
            logger.ScriviLog("WGWord.RiprendiFocus: " & ex.Message)
        End Try
    End Sub

    Private _annuncioRientro As Boolean = True
    Private _tickSoppressioneRientro As Integer = 0
    Private Const DURATA_SOPPRESSIONE_RIENTRO As Integer = 2000

    Public Sub SopprimiAnnuncioRientro()
        Try
            _tickSoppressioneRientro = Environment.TickCount
        Catch ex As Exception
            logger.ScriviLog("WGWord.SopprimiAnnuncioRientro: " & ex.Message)
        End Try
    End Sub

    Public Function ConsumaSoppressioneAnnuncioRientro() As Boolean
        Try
            If _tickSoppressioneRientro = 0 Then Return False
            If Environment.TickCount - _tickSoppressioneRientro > DURATA_SOPPRESSIONE_RIENTRO Then
                _tickSoppressioneRientro = 0
                Return False
            End If
            Return True
        Catch ex As Exception
            logger.ScriviLog("WGWord.ConsumaSoppressioneAnnuncioRientro: " & ex.Message)
            Return False
        End Try
    End Function

    Private Sub EseguiRientroFocus()
        Try
            If Me.IsDisposed OrElse Me.Disposing Then Return
            If Not Me.IsHandleCreated Then Return

            Dim annuncia As Boolean = _annuncioRientro
            _annuncioRientro = True

            Try
                If Not Me.Enabled Then Me.Enabled = True
                If Not Me.Visible Then Me.Visible = True
            Catch exStato As Exception
                logger.ScriviLog("WGWord.EseguiRientroFocus (stato): " & exStato.Message)
            End Try

            Try
                AbilitaFinestraNativa(Me.Handle, True)
                MostraFinestraNativa(Me.Handle, SW_SHOW_RIENTRO)
            Catch exNativa As Exception
                logger.ScriviLog("WGWord.EseguiRientroFocus (finestra): " & exNativa.Message)
            End Try

            Try
                Me.BringToFront()
            Catch exFronte As Exception
                logger.ScriviLog("WGWord.EseguiRientroFocus (fronte): " & exFronte.Message)
            End Try

            Try
                RipristinaCatenaControlloAttivo()
            Catch exCatena As Exception
                logger.ScriviLog("WGWord.EseguiRientroFocus (catena): " & exCatena.Message)
            End Try

            Try
                Me.Select()
                Me.Focus()
                SetFocus(Me.Handle)
            Catch exFocus As Exception
                logger.ScriviLog("WGWord.EseguiRientroFocus (focus): " & exFocus.Message)
            End Try

            Try
                attivahook()
            Catch exHook As Exception
                logger.ScriviLog("WGWord.EseguiRientroFocus (hook): " & exHook.Message)
            End Try

            Try
                AzzeraStatoModificatoriRientro()
            Catch exModificatori As Exception
                logger.ScriviLog("WGWord.EseguiRientroFocus (modificatori): " & exModificatori.Message)
            End Try

            Try
                UpdateCaretAndNotify(False)
            Catch exCursore As Exception
                logger.ScriviLog("WGWord.EseguiRientroFocus (cursore): " & exCursore.Message)
            End Try

            Try
                Me.Invalidate()
            Catch exDisegno As Exception
                logger.ScriviLog("WGWord.EseguiRientroFocus (disegno): " & exDisegno.Message)
            End Try

            If Not annuncia OrElse ConsumaSoppressioneAnnuncioRientro() Then Return

            Try
                If lettura IsNot Nothing AndAlso lettura.StaParlando Then Return
                LeggiParolaSottoCursore()
            Catch exLettura As Exception
                logger.ScriviLog("WGWord.EseguiRientroFocus (lettura): " & exLettura.Message)
            End Try
        Catch ex As Exception
            logger.ScriviLog("WGWord.EseguiRientroFocus: " & ex.Message)
        End Try
    End Sub

    Private Sub AzzeraStatoModificatoriRientro()
        Try
            _altPremutoDaHook = False
            _altUsatoConAltroTasto = False
            _altDestroPremutoDaHook = False
            _altDestroUsatoConAltroTasto = False
            _shiftPremutoDaHook = False
            _ctrlSinistroPremutoDaHook = False
            _ctrlDestroPremutoDaHook = False
            If _gestoreMovimento IsNot Nothing Then _gestoreMovimento.AzzeraModificatori()
        Catch ex As Exception
            logger.ScriviLog("WGWord.AzzeraStatoModificatoriRientro: " & ex.Message)
        End Try
    End Sub

    Private Sub RipristinaCatenaControlloAttivo()
        Try
            Dim figlio As Control = Me
            Dim contenitore As Control = Me.Parent
            Dim protezione As Integer = 0

            While contenitore IsNot Nothing AndAlso protezione < 32
                protezione += 1

                Dim ospite As ContainerControl = TryCast(contenitore, ContainerControl)
                If ospite IsNot Nothing AndAlso Not ospite.IsDisposed Then
                    Try
                        If figlio IsNot Nothing AndAlso Not figlio.IsDisposed AndAlso figlio.Visible AndAlso figlio.Enabled Then
                            If ospite.ActiveControl IsNot figlio Then ospite.ActiveControl = figlio
                        End If
                    Catch exAssegna As Exception
                        logger.ScriviLog("WGWord.RipristinaCatenaControlloAttivo (assegnazione): " & exAssegna.Message)
                    End Try
                End If

                figlio = contenitore
                contenitore = contenitore.Parent
            End While
        Catch ex As Exception
            logger.ScriviLog("WGWord.RipristinaCatenaControlloAttivo: " & ex.Message)
        End Try
    End Sub

End Class
