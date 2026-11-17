' ============================================================================================
' SessioneEditorVideo.Progetto - salvare il progetto di montaggio
' ============================================================================================
'
' Il progetto si salva dal menu "Esegui modifica" (Salva il progetto, Salva con un altro nome) e quando si esce con Esc, se è cambiato
' qualcosa dall'ultimo salvataggio. La prima volta si chiede il nome. Per sapere se ci sono cose da salvare si confronta l'elenco delle
' modifiche di adesso con quello dell'ultimo salvataggio (o di quando il progetto è stato aperto).
' ============================================================================================
Imports System
Imports wgcontrols
Imports System.IO
Imports System.Threading.Tasks

Partial Public NotInheritable Class SessioneEditorVideo

    Private _firma As String = ""

    Private Sub MemorizzaFirma()
        _firma = ArchivioProgettiVideo.Firma(_p, _p.Cartella)
    End Sub

    Private Function Modificato() As Boolean
        Return ArchivioProgettiVideo.Firma(_p, _p.Cartella) <> _firma
    End Function

    ''' <summary>Salva il progetto; se non ha ancora un nome lo chiede. dopo si esegue a salvataggio fatto.</summary>
    Friend Sub SalvaProgetto(dopo As Action)
        If _p.Cartella = "" OrElse _p.Nome = "" Then
            ChiediNomeProgetto(False, dopo)
        Else
            EseguiSalvataggio(_p.Cartella, _p.Nome, dopo)
        End If
    End Sub

    Friend Sub ChiediNomeProgetto(comeNuovo As Boolean, dopo As Action)
        Dim suggerito As String = If(_p.Nome <> "", _p.Nome, Path.GetFileNameWithoutExtension(_p.Sorgente) & " montaggio")
        AiutoRevisione.ChiediTesto("Nome del progetto", suggerito,
                                   Sub(testo As String)
                                       Try
                                           Dim nome As String = AiutoMusica.NomeFileSicuro(If(testo, "")).Trim()
                                           If nome = "" Then Return
                                           Dim base As String = nome
                                           Dim destinazione As String = ArchivioProgettiVideo.CartellaDi(nome)
                                           Dim attuale As String = If(_p.Cartella <> "", Path.GetFullPath(_p.Cartella).TrimEnd(Path.DirectorySeparatorChar), "")
                                           Dim n As Integer = 2
                                           While Directory.Exists(destinazione) AndAlso (comeNuovo OrElse Not String.Equals(Path.GetFullPath(destinazione).TrimEnd(Path.DirectorySeparatorChar), attuale, StringComparison.OrdinalIgnoreCase))
                                               nome = base & " (" & n.ToString() & ")"
                                               destinazione = ArchivioProgettiVideo.CartellaDi(nome)
                                               n += 1
                                           End While
                                           EseguiSalvataggio(destinazione, nome, dopo)
                                       Catch ex As Exception
                                           _log.ScriviLog("SessioneEditorVideo.ChiediNomeProgetto: " & ex.Message)
                                           AiutoMusica.Messaggio("Non è stato possibile dare il nome al progetto: " & ex.Message)
                                       End Try
                                   End Sub)
    End Sub

    Private Async Sub EseguiSalvataggio(cartella As String, nome As String, dopo As Action)
        Try
            _p.Nome = nome
            _p.Posizione = PosizioneCorrente()
            Dim progetto As ProgettoVideo = _p
            Await WGProcessoInCorso.EseguiAsync("Sto salvando il progetto, attendere", Function() Task.Run(Sub() ArchivioProgettiVideo.Salva(progetto, cartella)))
            MemorizzaFirma()
            AiutoMusica.Annuncia("Progetto salvato con il nome " & nome)
            If dopo IsNot Nothing Then dopo()
        Catch ex As Exception
            _log.ScriviLog("SessioneEditorVideo.EseguiSalvataggio: " & ex.Message & Environment.NewLine & ex.StackTrace)
            AiutoMusica.Messaggio("Non è stato possibile salvare il progetto: " & ex.Message)
        End Try
    End Sub

End Class
