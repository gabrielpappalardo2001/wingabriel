' ============================================================================================
' WGTema - il colore chiaro o scuro di Windows per i controlli di WinGabriel
' ============================================================================================
'
' A cosa serve
'   Dice se Windows e' impostato su un tema chiaro o scuro (Impostazioni, Personalizzazione, Colori) e da'
'   i due colori principali (sfondo e testo) che i campi, i menu, il lettore multimediale e il contorno del
'   browser devono usare al posto di un nero o un bianco fissi. Se Windows cambia tema mentre il programma
'   e' aperto, avvisa chi si e' iscritto all'evento CambiatoTema, cosi' i controlli si possono ridipingere.
'   Tinge anche la barra del titolo delle finestre dello stesso tema (richiede Windows 10 1809 o successivo).
'
' Come si usa
'   Dim c As Color = WGTema.Sfondo                 ' nero se scuro, bianco se chiaro
'   Dim t As Color = WGTema.Testo                   ' il contrario di Sfondo
'   AddHandler WGTema.CambiatoTema, Sub() ... ridisegna ...
'   WGTema.ApplicaAFinestra(Me)                      ' tinge la barra del titolo della finestra
'
' Errori
'   Nessun metodo solleva eccezioni: se il tema non si legge si resta sul tema scuro (quello che il
'   programma ha sempre usato), e l'errore finisce nel log.
' ============================================================================================
Imports System.Runtime.InteropServices
Imports Microsoft.Win32

Public NotInheritable Class WGTema

    Private Shared ReadOnly log As New Logger()
    Private Shared ReadOnly _blocco As New Object()
    Private Shared _scuro As Boolean = True
    Private Shared _letto As Boolean = False
    Private Shared _iscritta As Boolean = False

    ''' <summary>Si avvisa quando il tema chiaro/scuro di Windows cambia mentre il programma e' aperto.</summary>
    Public Shared Event CambiatoTema As EventHandler

    Private Sub New()
    End Sub

    ''' <summary>Vero se Windows e' impostato sul tema scuro per le app (o se non si riesce a saperlo).</summary>
    Public Shared ReadOnly Property Scuro As Boolean
        Get
            AssicuraLettura()
            SyncLock _blocco
                Return _scuro
            End SyncLock
        End Get
    End Property

    ''' <summary>Il colore di sfondo da usare: nero con il tema scuro, bianco con quello chiaro.</summary>
    Public Shared ReadOnly Property Sfondo As Color
        Get
            Return If(Scuro, Color.Black, Color.White)
        End Get
    End Property

    ''' <summary>Il colore del testo da usare: il contrario di Sfondo.</summary>
    Public Shared ReadOnly Property Testo As Color
        Get
            Return If(Scuro, Color.White, Color.Black)
        End Get
    End Property

    ''' <summary>Un grigio per bordi e separatori poco invadenti, leggibile su entrambi i temi.</summary>
    Public Shared ReadOnly Property Bordo As Color
        Get
            Return If(Scuro, Color.FromArgb(90, 90, 90), Color.FromArgb(170, 170, 170))
        End Get
    End Property

    ''' <summary>Lo sfondo di una voce selezionata: l'azzurro di evidenziazione di Windows, adattato al tema.</summary>
    Public Shared ReadOnly Property Selezione As Color
        Get
            Return If(Scuro, Color.FromArgb(0, 90, 158), Color.FromArgb(0, 120, 215))
        End Get
    End Property

    Private Shared Sub AssicuraLettura()
        SyncLock _blocco
            If _letto Then Return
            _letto = True
            LeggiDalRegistro()
            Try
                If Not _iscritta Then
                    AddHandler SystemEvents.UserPreferenceChanged, AddressOf OnPreferenzeCambiate
                    _iscritta = True
                End If
            Catch ex As Exception
                log.ScriviLog("WGTema.AssicuraLettura (iscrizione): " & ex.Message)
            End Try
        End SyncLock
    End Sub

    Private Shared Sub LeggiDalRegistro()
        Try
            Using chiave As RegistryKey = Registry.CurrentUser.OpenSubKey("Software\Microsoft\Windows\CurrentVersion\Themes\Personalize")
                If chiave Is Nothing Then Return
                Dim valore As Object = chiave.GetValue("AppsUseLightTheme")
                If valore Is Nothing Then Return
                SyncLock _blocco
                    _scuro = (Convert.ToInt32(valore) = 0)
                End SyncLock
            End Using
        Catch ex As Exception
            log.ScriviLog("WGTema.LeggiDalRegistro: " & ex.Message)
        End Try
    End Sub

    Private Shared Sub OnPreferenzeCambiate(sender As Object, e As UserPreferenceChangedEventArgs)
        Try
            If e.Category <> UserPreferenceCategory.General AndAlso e.Category <> UserPreferenceCategory.VisualStyle Then Return
            Dim eraScuro As Boolean
            SyncLock _blocco
                eraScuro = _scuro
            End SyncLock
            LeggiDalRegistro()
            Dim adessoScuro As Boolean
            SyncLock _blocco
                adessoScuro = _scuro
            End SyncLock
            If eraScuro <> adessoScuro Then RaiseEvent CambiatoTema(Nothing, EventArgs.Empty)
        Catch ex As Exception
            log.ScriviLog("WGTema.OnPreferenzeCambiate: " & ex.Message)
        End Try
    End Sub

    <DllImport("dwmapi.dll")>
    Private Shared Function DwmSetWindowAttribute(hwnd As IntPtr, attribute As Integer, ByRef valore As Integer, dimensione As Integer) As Integer
    End Function

    Private Const DWMWA_USE_IMMERSIVE_DARK_MODE As Integer = 20
    Private Const DWMWA_USE_IMMERSIVE_DARK_MODE_VECCHIO As Integer = 19

    ''' <summary>Tinge la barra del titolo e il contorno della finestra dello stesso tema (nero/scuro o bianco/chiaro).
    ''' Senza effetto su Windows piu' vecchi di quelli che lo sanno fare: la finestra resta come prima.</summary>
    Public Shared Sub ApplicaAFinestra(f As Form)
        Try
            If f Is Nothing OrElse Not f.IsHandleCreated Then Return
            Dim scuro As Integer = If(Scuro, 1, 0)
            If DwmSetWindowAttribute(f.Handle, DWMWA_USE_IMMERSIVE_DARK_MODE, scuro, 4) <> 0 Then
                DwmSetWindowAttribute(f.Handle, DWMWA_USE_IMMERSIVE_DARK_MODE_VECCHIO, scuro, 4)
            End If
        Catch ex As Exception
            log.ScriviLog("WGTema.ApplicaAFinestra: " & ex.Message)
        End Try
    End Sub

End Class
