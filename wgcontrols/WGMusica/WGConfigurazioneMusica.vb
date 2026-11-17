Imports System
Imports System.Collections.Generic
Imports NAudio.Wave

''' <summary>
''' La configurazione della musica: volumi, riverbero e latenza di strumenti musicali e spartiti musicali, la cartella delle registrazioni (strumenti e spartiti la chiedono ogni volta che si esporta). Si apre dal menu Musica e da Impostazioni, Applicazioni; gli ambienti leggono i valori quando si aprono.
''' </summary>
Public NotInheritable Class ConfigurazioneMusica

    Private Shared ReadOnly log As New Logger()

    Private Sub New()
    End Sub

    Public Shared Sub Mostra()
        Try
            Dim menu As New WGMenu()
            menu.aggiungi("Strumenti musicali", AddressOf MostraStrumenti)
            menu.aggiungi("Spartiti musicali", AddressOf MostraSpartiti)
            menu.avvia("Configurazione")
        Catch ex As Exception
            log.ScriviLog("ConfigurazioneMusica.Mostra: " & ex.Message)
        End Try
    End Sub

    Private Shared Sub MostraStrumenti()
        Try
            Dim menu As New WGMenu()
            menu.aggiungi("Volume dello strumento", Sub() ScegliNumero("Volume dello strumento", "musica_volume_strumento", 80, 0, 100, 10, " per cento"))
            menu.aggiungi("Volume del sottofondo", Sub() ScegliNumero("Volume del sottofondo", "musica_volume_sottofondo", 70, 0, 100, 10, " per cento"))
            menu.aggiungi("Riverbero", Sub() ScegliNumero("Riverbero", "musica_riverbero", 100, 0, 150, 25, " per cento"))
            menu.aggiungi("Latenza audio", AddressOf ScegliLatenza)
            menu.aggiungi("Ripeti il sottofondo", Sub() ScegliSiNo("Ripeti il sottofondo", "musica_ripeti_sottofondo", False))
            menu.aggiungi("Leggi le note suonate", Sub() ScegliSiNo("Leggi le note suonate", "musica_leggi_note", False))
            menu.avvia("Configurazione degli strumenti musicali")
        Catch ex As Exception
            log.ScriviLog("ConfigurazioneMusica.MostraStrumenti: " & ex.Message)
        End Try
    End Sub

    Private Shared Sub MostraSpartiti()
        Try
            Dim menu As New WGMenu()
            menu.aggiungi("Volume", Sub() ScegliNumero("Volume degli spartiti", "spartiti_volume", 80, 0, 100, 10, " per cento"))
            menu.aggiungi("Riverbero", Sub() ScegliNumero("Riverbero degli spartiti", "spartiti_riverbero", 100, 0, 150, 25, " per cento"))
            menu.avvia("Configurazione degli spartiti musicali")
        Catch ex As Exception
            log.ScriviLog("ConfigurazioneMusica.MostraSpartiti: " & ex.Message)
        End Try
    End Sub

    ''' <summary>Un elenco di valori numerici, con quello attuale selezionato; la scelta si salva e si conferma con un messaggio.</summary>
    Public Shared Sub ScegliNumero(titolo As String, chiave As String, predefinito As Integer, minimo As Integer, massimo As Integer, passo As Integer, unita As String)
        Try
            Dim attuale As Integer = ImpostazioniMusica.LeggiIntero(chiave, predefinito)
            Dim menu As New WGMenu()
            Dim posizione As Integer = 0
            Dim distanza As Integer = Integer.MaxValue
            Dim indice As Integer = 0
            Dim v As Integer = minimo
            While v <= massimo
                Dim valore As Integer = v
                menu.aggiungi(valore.ToString() & unita, Sub()
                                                             ImpostazioniMusica.Scrivi(chiave, valore.ToString())
                                                             menu.chiudi()
                                                             AiutoMusica.Messaggio(titolo & ": " & valore.ToString() & unita & ".")
                                                         End Sub)
                If Math.Abs(valore - attuale) < distanza Then
                    distanza = Math.Abs(valore - attuale)
                    posizione = indice
                End If
                indice += 1
                v += passo
            End While
            menu.posiziona(posizione)
            menu.avvia(titolo & ", ora " & attuale.ToString() & unita, True)
        Catch ex As Exception
            log.ScriviLog("ConfigurazioneMusica.ScegliNumero (" & chiave & "): " & ex.Message)
        End Try
    End Sub

    Public Shared Sub ScegliSiNo(titolo As String, chiave As String, predefinito As Boolean)
        Try
            Dim attuale As Boolean = ImpostazioniMusica.LeggiBooleano(chiave, predefinito)
            Dim menu As New WGMenu()
            menu.aggiungi("Attivato", Sub()
                                          ImpostazioniMusica.ScriviBooleano(chiave, True)
                                          menu.chiudi()
                                          AiutoMusica.Messaggio(titolo & ": attivato.")
                                      End Sub)
            menu.aggiungi("Disattivato", Sub()
                                             ImpostazioniMusica.ScriviBooleano(chiave, False)
                                             menu.chiudi()
                                             AiutoMusica.Messaggio(titolo & ": disattivato.")
                                         End Sub)
            menu.posiziona(If(attuale, 0, 1))
            menu.avvia(titolo & ", ora " & If(attuale, "attivato", "disattivato"), True)
        Catch ex As Exception
            log.ScriviLog("ConfigurazioneMusica.ScegliSiNo (" & chiave & "): " & ex.Message)
        End Try
    End Sub

    Private Shared Sub ScegliLatenza()
        Try
            Dim nomi As String() = {"Minima, circa dieci millisecondi", "Bassa", "Normale", "Alta"}
            Dim attuale As Integer = Math.Max(0, Math.Min(nomi.Length - 1, ImpostazioniMusica.LeggiIntero("musica_latenza_v4", 0)))
            Dim menu As New WGMenu()
            For i As Integer = 0 To nomi.Length - 1
                Dim indice As Integer = i
                menu.aggiungi(nomi(i), Sub()
                                           ImpostazioniMusica.Scrivi("musica_latenza_v4", indice.ToString())
                                           menu.chiudi()
                                           AiutoMusica.Messaggio("Latenza audio: " & nomi(indice).ToLowerInvariant() & ". Se il suono si rovina, scegli una latenza più alta.")
                                       End Sub)
            Next
            menu.posiziona(attuale)
            menu.avvia("Latenza audio", True)
        Catch ex As Exception
            log.ScriviLog("ConfigurazioneMusica.ScegliLatenza: " & ex.Message)
        End Try
    End Sub

End Class
