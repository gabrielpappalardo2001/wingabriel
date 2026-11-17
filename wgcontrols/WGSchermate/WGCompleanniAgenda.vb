Imports System
Imports System.Collections.Generic
Imports System.Threading.Tasks
Imports wgcontrols
Imports WGContatti
Imports WGDati

' ============================================================================================
' WGCompleanniAgenda - il collegamento tra la rubrica e l'agenda per i compleanni
' ============================================================================================
'
' Se è attivo (impostazione "Compleanni in agenda", nel menu della rubrica), ogni contatto con un compleanno o una data di nascita
' ha in agenda un appuntamento annuale alle 9, nella categoria "Compleanni", con l'avviso il giorno stesso oppure il giorno prima.
' L'agenda si allinea da sola all'avvio di WinGabriel, ogni volta che si salva o si elimina un contatto e quando si cambia l'impostazione.
' Spegnendo l'impostazione si tolgono dall'agenda tutti i compleanni generati.
'
'   WGCompleanniAgenda.SincronizzaInSottofondo()    ' dopo una modifica dei contatti, e all'avvio
'   WGCompleanniAgenda.Imposta(WGCompleanniAgenda.AvvisoGiornoPrima)
' ============================================================================================
Public NotInheritable Class WGCompleanniAgenda

    Public Const ChiaveConfigurazione As String = "compleanni_agenda"
    ''' <summary>Nessun appuntamento: la funzione è spenta.</summary>
    Public Const Spento As String = ""
    ''' <summary>Appuntamento alle 9 del giorno del compleanno, con l'avviso a quell'ora.</summary>
    Public Const AvvisoGiornoStesso As String = "stesso"
    ''' <summary>Appuntamento alle 9 del giorno del compleanno, con l'avviso il giorno prima.</summary>
    Public Const AvvisoGiornoPrima As String = "prima"

    Private Const CategoriaAgenda As String = "Compleanni"
    Private Const MinutiUnGiorno As Integer = 1440

    Private Shared ReadOnly _log As New Logger()
    Private Shared ReadOnly _lock As New Object()

    Private Sub New()
    End Sub

    ''' <summary>La scelta fatta: Spento, AvvisoGiornoStesso o AvvisoGiornoPrima.</summary>
    Public Shared Function Modalita() As String
        Try
            Dim config As New WGConfig()
            If Not config.EsisteId(ChiaveConfigurazione) Then Return Spento
            Dim valore As String = If(config.LeggiValore(ChiaveConfigurazione), "").Replace(vbCr, "").Replace(vbLf, "").Trim().ToLowerInvariant()
            If valore = AvvisoGiornoStesso OrElse valore = AvvisoGiornoPrima Then Return valore
            Return Spento
        Catch ex As Exception
            _log.ScriviLog("WGCompleanniAgenda.Modalita: " & ex.Message)
            Return Spento
        End Try
    End Function

    Public Shared Function Descrizione(Optional modalita As String = Nothing) As String
        Select Case If(modalita, WGCompleanniAgenda.Modalita())
            Case AvvisoGiornoStesso : Return "attivi, con avviso il giorno del compleanno"
            Case AvvisoGiornoPrima : Return "attivi, con avviso il giorno prima"
        End Select
        Return "spenti"
    End Function

    ''' <summary>Salva la scelta e allinea subito l'agenda: con Spento toglie tutti i compleanni generati.</summary>
    Public Shared Sub Imposta(modalita As String)
        Try
            Dim valore As String = If(modalita, "")
            If valore <> AvvisoGiornoStesso AndAlso valore <> AvvisoGiornoPrima Then valore = Spento

            Dim config As New WGConfig()
            If config.EsisteId(ChiaveConfigurazione) Then config.ModificaValore(ChiaveConfigurazione, valore) Else config.AggiungiValore(ChiaveConfigurazione, valore)

            AvviaInSottofondo()
        Catch ex As Exception
            _log.ScriviLog("WGCompleanniAgenda.Imposta: " & ex.Message)
        End Try
    End Sub

    ''' <summary>Allinea l'agenda ai compleanni della rubrica, se la funzione è attiva. Non aspetta e non disturba chi chiama.</summary>
    Public Shared Sub SincronizzaInSottofondo()
        Try
            If Modalita() = Spento Then Return
            AvviaInSottofondo()
        Catch ex As Exception
            _log.ScriviLog("WGCompleanniAgenda.SincronizzaInSottofondo: " & ex.Message)
        End Try
    End Sub

    Private Shared Sub AvviaInSottofondo()
        Task.Run(Sub()
                     Try
                         SyncLock _lock
                             Allinea()
                         End SyncLock
                     Catch ex As Exception
                         _log.ScriviLog("WGCompleanniAgenda.AvviaInSottofondo: " & ex.ToString())
                     End Try
                 End Sub)
    End Sub

    Private Shared Sub Allinea()
        Dim scelta As String = Modalita()
        Dim agenda As CLSAgenda = CLSAgenda.GetInstance()

        If scelta = Spento Then
            ' funzione spenta: via tutti i compleanni generati (con elenco vuoto e niente da conservare)
            agenda.SincronizzaCompleanni(New List(Of CLSAgenda.VoceCompleannoAgenda)(), New HashSet(Of String)(StringComparer.OrdinalIgnoreCase), 0, CategoriaAgenda)
            Return
        End If

        Using contatti As New CLSContatti()
            Dim nonLeggibili As HashSet(Of String) = Nothing
            Dim compleanni As List(Of CLSContatti.VoceCompleanno) = contatti.OttieniCompleanni(nonLeggibili)
            ' se la rubrica non si è potuta leggere non si tocca niente: un errore non deve cancellare i compleanni dall'agenda
            If compleanni Is Nothing Then Return

            Dim voci As New List(Of CLSAgenda.VoceCompleannoAgenda)()
            For Each c As CLSContatti.VoceCompleanno In compleanni
                voci.Add(New CLSAgenda.VoceCompleannoAgenda() With {.Chiave = c.Chiave, .NomeCompleto = c.NomeCompleto, .Giorno = c.Giorno, .Mese = c.Mese, .Anno = c.Anno})
            Next

            agenda.SincronizzaCompleanni(voci, nonLeggibili, If(scelta = AvvisoGiornoPrima, MinutiUnGiorno, 0), CategoriaAgenda)
        End Using
    End Sub

End Class
