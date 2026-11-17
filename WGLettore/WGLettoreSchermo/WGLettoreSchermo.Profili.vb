Imports System.Collections.Generic
Imports System.Threading

' Profili per programma: prolissità e echi diversi in ogni applicazione, salvati con Control 0 e tolti con Shift 0.
Partial Public NotInheritable Class WGLettoreSchermo

    Private Const PrefissoProfilo As String = "lettore_schermo_profilo_"

    Private _profiloAttivo As String = ""
    Private _baseProlissita As Integer = 1
    Private _baseEcoCaratteri As Boolean = True
    Private _baseEcoParole As Boolean = True

    Private Function NomeProgrammaFinestra(finestra As IntPtr) As String
        Try
            If finestra = IntPtr.Zero Then Return ""
            Dim pid As UInteger = 0UI
            NativoLettore.GetWindowThreadProcessId(finestra, pid)
            If pid = 0UI Then Return ""
            Return NomeEseguibileApp(pid)
        Catch ex As Exception
            RegistroErrori.Scrivi("WGLettoreSchermo.NomeProgrammaFinestra", ex)
            Return ""
        End Try
    End Function

    Private Sub MemorizzaImpostazioniBase()
        _baseProlissita = Prolissita
        _baseEcoCaratteri = EcoCaratteri
        _baseEcoParole = EcoParole
    End Sub

    ' Applica solo in memoria, senza scrivere la configurazione: le scelte generali restano quelle di prima.
    Private Sub ImpostaValoriProfilo(prolissita As Integer, ecoCaratteri As Boolean, ecoParole As Boolean)
        AssicuraImpostazioni()
        Dim livello As Integer = Math.Max(0, Math.Min(2, prolissita))
        _impostazioniLettore.Prolissita = livello
        DescrittoreElementi.ProlissitaApplicata = livello
        EcoCaratteri = ecoCaratteri
        EcoParole = ecoParole
    End Sub

    Private Sub RipristinaImpostazioniBase()
        If _profiloAttivo = "" Then Return
        ImpostaValoriProfilo(_baseProlissita, _baseEcoCaratteri, _baseEcoParole)
        _profiloAttivo = ""
    End Sub

    Private Sub ApplicaProfilo(finestra As IntPtr)
        Try
            Dim nome As String = NomeProgrammaFinestra(finestra)
            If nome = "" Then Return
            Dim valore As String = ConfigLettore.Leggi(PrefissoProfilo & nome, "")
            If valore = "" Then
                RipristinaImpostazioniBase()
                Return
            End If
            If String.Equals(_profiloAttivo, nome, StringComparison.Ordinal) Then Return
            If _profiloAttivo = "" Then MemorizzaImpostazioniBase()
            Dim prolissita As Integer = _baseProlissita
            Dim ecoC As Boolean = _baseEcoCaratteri
            Dim ecoP As Boolean = _baseEcoParole
            For Each parte As String In valore.Split(";"c)
                Dim pezzi As String() = parte.Split("="c)
                If pezzi.Length <> 2 Then Continue For
                Select Case pezzi(0).Trim()
                    Case "p"
                        Integer.TryParse(pezzi(1), prolissita)
                    Case "c"
                        ecoC = pezzi(1).Trim() = "1"
                    Case "w"
                        ecoP = pezzi(1).Trim() = "1"
                End Select
            Next
            ImpostaValoriProfilo(prolissita, ecoC, ecoP)
            _profiloAttivo = nome
        Catch ex As Exception
            RegistroErrori.Scrivi("WGLettoreSchermo.ApplicaProfilo", ex)
        End Try
    End Sub

    Private Sub SalvaProfiloProgramma()
        Try
            Dim nome As String = NomeProgrammaFinestra(NativoLettore.GetForegroundWindow())
            If nome = "" Then
                Parla("Programma non riconosciuto", True)
                Return
            End If
            If _profiloAttivo = "" Then MemorizzaImpostazioniBase()
            Dim valore As String = "p=" & Prolissita.ToString() & ";c=" & If(EcoCaratteri, "1", "0") & ";w=" & If(EcoParole, "1", "0")
            ConfigLettore.Scrivi(PrefissoProfilo & nome, valore)
            _profiloAttivo = nome
            Parla("Profilo salvato per " & nome, True)
        Catch ex As Exception
            RegistroErrori.Scrivi("WGLettoreSchermo.SalvaProfiloProgramma", ex)
            Parla("Profilo non salvato", True)
        End Try
    End Sub

    Private Sub TogliProfiloProgramma()
        Try
            Dim nome As String = NomeProgrammaFinestra(NativoLettore.GetForegroundWindow())
            If nome = "" Then
                Parla("Programma non riconosciuto", True)
                Return
            End If
            If Not ConfigLettore.Esiste(PrefissoProfilo & nome) Then
                Parla("Per " & nome & " non c'è un profilo", True)
                Return
            End If
            ConfigLettore.Elimina(PrefissoProfilo & nome)
            If String.Equals(_profiloAttivo, nome, StringComparison.Ordinal) Then RipristinaImpostazioniBase()
            Parla("Profilo di " & nome & " tolto", True)
        Catch ex As Exception
            RegistroErrori.Scrivi("WGLettoreSchermo.TogliProfiloProgramma", ex)
            Parla("Profilo non tolto", True)
        End Try
    End Sub

End Class
