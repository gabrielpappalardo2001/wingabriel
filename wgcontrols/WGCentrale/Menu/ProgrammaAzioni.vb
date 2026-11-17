Imports System.Diagnostics
Imports System.Threading
Imports wgcontrols

' ============================================================================================
' WGAzioniProgrammate - lo spegnimento del computer programmato
' ============================================================================================
'
' Si programma dal Timer (plugin) con l'opzione "Spegni il computer". Il tempo vive qui, non nel plugin: resta programmato
' anche se si esce dal Timer, finché WinGabriel è aperto.
'
' Quando il tempo è passato compare un menu che dice che il computer si spegne fra un minuto: si può annullare o spegnere
' subito; con Esc si annulla. Per spegnere si chiude WinGabriel nel modo solito (sincronizza il cloud e saluta le altre parti)
' e si chiede a Windows di spegnersi fra trenta secondi, chiudendo anche i programmi rimasti aperti, perché chi usa
' WinGabriel non può rispondere alle finestre di Windows.
' ============================================================================================

Friend NotInheritable Class WGAzioniProgrammate

    Private Shared ReadOnly _log As New Logger()
    Private Shared _timer As System.Threading.Timer
    Private Shared _scadenza As DateTime

    ''' <summary>La chiusura normale di WinGabriel, da eseguire prima che Windows si spenga. La imposta il menu iniziale.</summary>
    Public Shared ChiudiWinGabriel As Action

    Private Sub New()
    End Sub

    Private Shared Sub SuInterfaccia(azione As Action)
        Try
            Dim c As WGContenitore = WGContenitore.Istanza
            If c IsNot Nothing AndAlso Not c.IsDisposed AndAlso c.IsHandleCreated Then
                c.BeginInvoke(azione)
            Else
                azione()
            End If
        Catch ex As Exception
            _log.ScriviLog("WGAzioniProgrammate.SuInterfaccia: " & ex.Message)
        End Try
    End Sub

    Private Shared Sub Messaggio(testo As String)
        Try
            Dim m As New WGMessaggi()
            m.Messaggio(testo)
            m.ShowDialog()
        Catch ex As Exception
            _log.ScriviLog("WGAzioniProgrammate.Messaggio: " & ex.Message)
        End Try
    End Sub

    Public Shared Function Rimanente() As TimeSpan
        If _timer Is Nothing Then Return TimeSpan.Zero
        Dim r As TimeSpan = _scadenza - DateTime.Now
        Return If(r < TimeSpan.Zero, TimeSpan.Zero, r)
    End Function

    Public Shared Sub ProgrammaSpegnimento(durata As TimeSpan)
        Try
            AnnullaSpegnimento()
            _scadenza = DateTime.Now + durata
            _timer = New System.Threading.Timer(
                Sub(o)
                    Try
                        AnnullaSpegnimento()
                        SuInterfaccia(AddressOf AllaScadenza)
                    Catch ex As Exception
                        _log.ScriviLog("WGAzioniProgrammate.Scaduto: " & ex.Message)
                    End Try
                End Sub, Nothing, durata, Timeout.InfiniteTimeSpan)
        Catch ex As Exception
            _log.ScriviLog("WGAzioniProgrammate.ProgrammaSpegnimento: " & ex.Message)
        End Try
    End Sub

    Public Shared Sub AnnullaSpegnimento()
        Try
            Dim t As System.Threading.Timer = _timer
            _timer = Nothing
            If t IsNot Nothing Then t.Dispose()
        Catch ex As Exception
            _log.ScriviLog("WGAzioniProgrammate.AnnullaSpegnimento: " & ex.Message)
        End Try
    End Sub

    ''' <summary>Il tempo è passato: un minuto per cambiare idea. Sul thread dell'interfaccia.</summary>
    Private Shared Sub AllaScadenza()
        Try
            Dim finito As Boolean = False
            Dim attesa As System.Threading.Timer = Nothing
            Dim m As New WGMenu()
            m.aggiungi("Annulla lo spegnimento",
                Sub(a, b)
                    finito = True
                    If attesa IsNot Nothing Then attesa.Dispose()
                    m.abbandona()
                End Sub)
            m.aggiungi("Spegni subito",
                Sub(a, b)
                    finito = True
                    If attesa IsNot Nothing Then attesa.Dispose()
                    m.abbandona()
                    Spegni()
                End Sub)
            AddHandler m.abbandonato,
                Sub(a, b)
                    If finito Then Return
                    finito = True
                    If attesa IsNot Nothing Then attesa.Dispose()
                End Sub
            attesa = New System.Threading.Timer(
                Sub(o)
                    Try
                        SuInterfaccia(
                            Sub()
                                If finito Then Return
                                finito = True
                                m.abbandona()
                                Spegni()
                            End Sub)
                    Catch ex As Exception
                        _log.ScriviLog("WGAzioniProgrammate.AllaScadenza timer: " & ex.Message)
                    End Try
                End Sub, Nothing, 60000, Timeout.Infinite)
            m.avvia("Il computer si spegne tra un minuto", True)
        Catch ex As Exception
            _log.ScriviLog("WGAzioniProgrammate.AllaScadenza: " & ex.Message & Environment.NewLine & ex.StackTrace)
        End Try
    End Sub

    Private Shared Sub Spegni()
        Try
            Dim info As New ProcessStartInfo("shutdown.exe", "/s /f /t 30")
            info.CreateNoWindow = True
            info.UseShellExecute = False
            Process.Start(info)
            If ChiudiWinGabriel IsNot Nothing Then ChiudiWinGabriel()
        Catch ex As Exception
            _log.ScriviLog("WGAzioniProgrammate.Spegni: " & ex.Message & Environment.NewLine & ex.StackTrace)
            Messaggio("Non sono riuscito a spegnere il computer.")
        End Try
    End Sub

End Class
