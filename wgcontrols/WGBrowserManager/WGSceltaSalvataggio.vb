Imports System.IO
Imports wgcontrols

Public Class WGSceltaSalvataggio
    Public Event Confermato(percorso As String)
    Public Event Annullato As EventHandler

    Private ReadOnly _log As New Logger()
    Private Shared ReadOnly _logCondiviso As New Logger()
    Private ReadOnly _titoloNome As String = "Nome del file"
    Private ReadOnly _estensione As String = ""
    Private _nomeCorrente As String = ""
    Private _cartella As String = ""
    Private _dialogo As WGFileDialog
    Private _campi As Campi
    Private _concluso As Boolean = False

    Public Sub New(titoloNome As String, nomeProposto As String, Optional estensione As String = "")
        Try
            If Not String.IsNullOrWhiteSpace(titoloNome) Then _titoloNome = titoloNome.Trim()
            _estensione = NormalizzaEstensione(estensione)
            _nomeCorrente = NomeSicuro(nomeProposto)
        Catch ex As Exception
            _log.ScriviLog("WGSceltaSalvataggio.New: " & ex.ToString())
        End Try
    End Sub

    Public Sub Avvia()
        Try
            _dialogo = New WGFileDialog()
            _dialogo.MultiselezioneConsentita = False
            AddHandler _dialogo.singolaselezione, AddressOf CartellaScelta
            AddHandler _dialogo.selezioneterminata, AddressOf VociScelte
            AddHandler _dialogo.annullata, AddressOf DialogoAnnullato
            _dialogo.Avvia()
        Catch ex As Exception
            _log.ScriviLog("WGSceltaSalvataggio.Avvia: " & ex.ToString())
            StaccaDialogo()
            Annulla()
        End Try
    End Sub

    Private Sub StaccaDialogo()
        Try
            If _dialogo IsNot Nothing Then
                RemoveHandler _dialogo.singolaselezione, AddressOf CartellaScelta
                RemoveHandler _dialogo.selezioneterminata, AddressOf VociScelte
                RemoveHandler _dialogo.annullata, AddressOf DialogoAnnullato
            End If
        Catch ex As Exception
            _log.ScriviLog("WGSceltaSalvataggio.StaccaDialogo: " & ex.Message)
        End Try
        _dialogo = Nothing
    End Sub

    Private Sub StaccaCampi()
        Try
            If _campi IsNot Nothing Then
                RemoveHandler _campi.Fine, AddressOf NomeConfermato
                RemoveHandler _campi.Chiusura, AddressOf NomeAnnullato
            End If
        Catch ex As Exception
            _log.ScriviLog("WGSceltaSalvataggio.StaccaCampi: " & ex.Message)
        End Try
        _campi = Nothing
    End Sub

    Private Sub VociScelte(elementi As List(Of String))
        Try
            If elementi Is Nothing OrElse elementi.Count = 0 OrElse String.IsNullOrWhiteSpace(elementi(0)) Then
                StaccaDialogo()
                MostraMessaggio("Nessuna cartella scelta.")
                Annulla()
                Return
            End If
            CartellaScelta(elementi(0))
        Catch ex As Exception
            _log.ScriviLog("WGSceltaSalvataggio.VociScelte: " & ex.ToString())
            StaccaDialogo()
            Annulla()
        End Try
    End Sub

    Private Sub CartellaScelta(elemento As String)
        Try
            StaccaDialogo()
            If _concluso Then Return
            Dim cartella As String = CartellaDa(elemento)
            If cartella = "" Then
                MostraMessaggio("La cartella scelta non è disponibile.")
                Annulla()
                Return
            End If
            _cartella = cartella
            ChiediNome()
        Catch ex As Exception
            _log.ScriviLog("WGSceltaSalvataggio.CartellaScelta: " & ex.ToString())
            Annulla()
        End Try
    End Sub

    Private Sub DialogoAnnullato()
        Try
            StaccaDialogo()
            Annulla()
        Catch ex As Exception
            _log.ScriviLog("WGSceltaSalvataggio.DialogoAnnullato: " & ex.Message)
        End Try
    End Sub

    Private Sub ChiediNome()
        Try
            If _concluso Then Return
            StaccaCampi()
            _campi = New Campi()
            _campi.Aggiungi(_titoloNome & ", nella cartella " & NomeCartella(_cartella), "nome_file", True, False, _nomeCorrente)
            AddHandler _campi.Fine, AddressOf NomeConfermato
            AddHandler _campi.Chiusura, AddressOf NomeAnnullato
            _campi.Avvia()
        Catch ex As Exception
            _log.ScriviLog("WGSceltaSalvataggio.ChiediNome: " & ex.ToString())
            StaccaCampi()
            Annulla()
        End Try
    End Sub

    Private Sub NomeConfermato(sender As Object, e As EventArgs)
        Try
            Dim nome As String = ""
            Try
                If _campi IsNot Nothing Then nome = If(_campi.RecuperaValore("nome_file"), "")
            Catch exValore As Exception
                _log.ScriviLog("WGSceltaSalvataggio.NomeConfermato valore: " & exValore.Message)
            End Try
            StaccaCampi()
            If _concluso Then Return
            nome = NomeSicuro(nome)
            If nome = "" Then
                MostraMessaggio("Il nome del file non è valido. Scriverne un altro.")
                ChiediNome()
                Return
            End If
            If _estensione <> "" AndAlso Not nome.EndsWith(_estensione, StringComparison.OrdinalIgnoreCase) Then nome &= _estensione
            _nomeCorrente = nome
            Dim percorso As String = Path.Combine(_cartella, nome)
            If Directory.Exists(percorso) Then
                MostraMessaggio("Nella cartella " & NomeCartella(_cartella) & " c'è già una cartella che si chiama " & nome & ". Scrivere un altro nome.")
                ChiediNome()
                Return
            End If
            If File.Exists(percorso) Then
                ChiediSostituzione(percorso)
                Return
            End If
            Concludi(percorso)
        Catch ex As Exception
            _log.ScriviLog("WGSceltaSalvataggio.NomeConfermato: " & ex.ToString())
            Annulla()
        End Try
    End Sub

    Private Sub NomeAnnullato(sender As Object, e As EventArgs)
        Try
            StaccaCampi()
            Annulla()
        Catch ex As Exception
            _log.ScriviLog("WGSceltaSalvataggio.NomeAnnullato: " & ex.Message)
        End Try
    End Sub

    Private Sub ChiediSostituzione(percorso As String)
        Try
            Dim risposto As Boolean = False
            Dim domanda As New WGDomanda()
            AddHandler domanda.rispostadomanda, Sub(risposta As DialogResult)
                                                    Try
                                                        If risposto Then Return
                                                        risposto = True
                                                        If risposta = DialogResult.Yes Then
                                                            Concludi(percorso)
                                                        Else
                                                            ChiediNome()
                                                        End If
                                                    Catch exRisposta As Exception
                                                        _log.ScriviLog("WGSceltaSalvataggio.ChiediSostituzione risposta: " & exRisposta.ToString())
                                                        Annulla()
                                                    End Try
                                                End Sub
            domanda.avvia("Nella cartella " & NomeCartella(_cartella) & " c'è già il file " & Path.GetFileName(percorso) & ". Sostituirlo? Rispondendo no si può scrivere un altro nome.")
        Catch ex As Exception
            _log.ScriviLog("WGSceltaSalvataggio.ChiediSostituzione: " & ex.ToString())
            Annulla()
        End Try
    End Sub

    Private Sub Concludi(percorso As String)
        Try
            If _concluso Then Return
            _concluso = True
            RaiseEvent Confermato(percorso)
        Catch ex As Exception
            _log.ScriviLog("WGSceltaSalvataggio.Concludi: " & ex.ToString())
        End Try
    End Sub

    Private Sub Annulla()
        Try
            If _concluso Then Return
            _concluso = True
            RaiseEvent Annullato(Me, EventArgs.Empty)
        Catch ex As Exception
            _log.ScriviLog("WGSceltaSalvataggio.Annulla: " & ex.ToString())
        End Try
    End Sub

    Private Sub MostraMessaggio(testo As String)
        Try
            Dim messaggio As New WGMessaggi()
            messaggio.Messaggio(testo)
            messaggio.ShowDialog()
        Catch ex As Exception
            _log.ScriviLog("WGSceltaSalvataggio.MostraMessaggio: " & ex.Message)
        End Try
    End Sub

    Private Shared Function CartellaDa(elemento As String) As String
        Try
            Dim percorso As String = If(elemento, "").Trim()
            If percorso = "" Then Return ""
            If Directory.Exists(percorso) Then Return percorso
            If File.Exists(percorso) Then
                Dim cartella As String = Path.GetDirectoryName(percorso)
                If Not String.IsNullOrEmpty(cartella) AndAlso Directory.Exists(cartella) Then Return cartella
            End If
            Return ""
        Catch ex As Exception
            _logCondiviso.ScriviLog("WGSceltaSalvataggio.CartellaDa: " & ex.Message)
            Return ""
        End Try
    End Function

    Public Shared Function NomeCartella(percorso As String) As String
        Try
            Dim pulito As String = If(percorso, "").TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
            Dim nome As String = Path.GetFileName(pulito)
            If String.IsNullOrEmpty(nome) Then Return If(percorso, "")
            Return nome
        Catch ex As Exception
            _logCondiviso.ScriviLog("WGSceltaSalvataggio.NomeCartella: " & ex.Message)
            Return If(percorso, "")
        End Try
    End Function

    Private Shared Function NormalizzaEstensione(estensione As String) As String
        Try
            Dim valore As String = If(estensione, "").Trim().ToLowerInvariant()
            If valore = "" Then Return ""
            If Not valore.StartsWith(".") Then valore = "." & valore
            Return valore
        Catch ex As Exception
            _logCondiviso.ScriviLog("WGSceltaSalvataggio.NormalizzaEstensione: " & ex.Message)
            Return ""
        End Try
    End Function

    Public Shared Function NomeSicuro(nome As String) As String
        Try
            Dim valore As String = If(nome, "")
            For Each carattere As Char In Path.GetInvalidFileNameChars()
                valore = valore.Replace(carattere, "_"c)
            Next
            valore = valore.Trim().TrimEnd("."c, " "c)
            If valore.Replace("_", "").Trim() = "" Then Return ""
            Return valore
        Catch ex As Exception
            _logCondiviso.ScriviLog("WGSceltaSalvataggio.NomeSicuro: " & ex.Message)
            Return ""
        End Try
    End Function
End Class
