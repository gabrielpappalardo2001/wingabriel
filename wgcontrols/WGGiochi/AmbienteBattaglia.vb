Imports System
Imports System.Collections.Generic
Imports System.Globalization
Imports System.Linq
Imports System.Windows.Forms
Imports Newtonsoft.Json.Linq
Imports wgcontrols

''' <summary>
''' La battaglia navale nell'ambiente di gioco: sullo schermo c'è il casellario (disegnato a mano, in GDI, da SchermoGioco), che si percorre con le frecce
''' e si legge come un foglio di calcolo ("acqua, B 3").
'''  - Posizionamento: Invio mette la nave scelta con la prua sulla casella del cursore, R la gira, C mette a caso le navi che mancano, X toglie l'ultima,
'''    P dice che la flotta è pronta. La nave dice se ci sta mentre ci si muove.
'''  - Battaglia: quando tocca a te sei sul mare nemico e Invio spara sulla casella del cursore. M passa dal mare nemico alla tua flotta e viceversa.
'''  - T dice la situazione, Q le navi nemiche che hai affondato, L riletture dei messaggi, W scrivi un messaggio.
''' Il casellario e il cursore restano gli stessi da una mossa all'altra (stanno nei dati della partita).
''' </summary>
Partial Public Class MenuGiochi

    Private Shared Function StatoDaCarattere(ch As Char) As StatoCasella
        Try
            Select Case ch
                Case "N"c : Return StatoCasella.Nave
                Case "o"c : Return StatoCasella.Acqua
                Case "X"c : Return StatoCasella.Colpita
                Case "#"c : Return StatoCasella.Affondata
                Case Else : Return StatoCasella.Vuota
            End Select
        Catch ex As Exception
            RegistroErrori.Registra("MenuGiochi.StatoDaCarattere", ex)
            Throw
        End Try
    End Function

    ''' <summary>Riempie il casellario con le righe del server: "." niente, "N" nave, "o" acqua, "X" colpita, "#" affondata.</summary>
    Private Shared Sub RiempiCasellario(c As CasellarioNavale, righe As JToken)
        Try
            Dim arr As JArray = TryCast(righe, JArray)
            If arr Is Nothing Then Return
            For r As Integer = 0 To Math.Min(arr.Count, c.Righe) - 1
                Dim riga As String = Txt(arr(r))
                For col As Integer = 0 To c.Colonne - 1
                    c.Imposta(r, col, If(col < riga.Length, StatoDaCarattere(riga(col)), StatoCasella.Vuota))
                Next
            Next
        Catch ex As Exception
            RegistroErrori.Registra("MenuGiochi.RiempiCasellario", ex)
            Throw
        End Try
    End Sub

    Private Function MareAttuale(s As SessioneSchermo) As CasellarioNavale
        Try
            Return If(s.D.MareAttivo = "mia", s.D.MiaFlotta, s.D.MareNemico)
        Catch ex As Exception
            RegistroErrori.Registra("MenuGiochi.MareAttuale", ex)
            Throw
        End Try
    End Function

    Private Sub ComponiBattaglia(s As SessioneSchermo)
        Try
            Dim v As JObject = Vista(s.Stato)
            Dim d As DatiPartita = s.D
            Dim fase As String = Txt(v("fase"))
            Dim righe As Integer = Math.Max(1, Num(v("righe")))
            Dim colonne As Integer = Math.Max(1, Num(v("colonne")))
            If d.MiaFlotta Is Nothing OrElse d.MiaFlotta.Righe <> righe OrElse d.MiaFlotta.Colonne <> colonne Then
                d.MiaFlotta = New CasellarioNavale(righe, colonne, "La tua flotta")
                d.MareNemico = New CasellarioNavale(righe, colonne, "Mare nemico")
                d.MareAttivo = "mia"
            End If
            RiempiCasellario(d.MiaFlotta, v("mia"))
            RiempiCasellario(d.MareNemico, v("nemica"))
            ' quale mare si vede: in posizionamento la propria flotta; in battaglia il mare nemico, finché non si preme M (a ogni novità si torna al mare nemico)
            Dim novita As Integer = Num(s.Stato("ultimo_evento"))
            If fase = "posizionamento" Then
                d.MareAttivo = "mia"
            ElseIf novita <> d.UltimoEventoComposto OrElse d.MareAttivo = "" Then
                d.MareAttivo = "nemica"
            End If
            d.UltimoEventoComposto = novita
            d.MiaFlotta.LunghezzaNave = If(fase = "posizionamento" AndAlso Ha(s, "nave"), Num(v("prossima")), 0)
            d.MiaFlotta.NaviDistanziate = Vero(v("distanza"))
            d.MareNemico.LunghezzaNave = 0
            s.Schermo.ImpostaCasellario(MareAttuale(s))

            Dim sFlotte As New SezioneGioco With {.Titolo = "Le flotte"}
            If fase = "posizionamento" Then
                Dim mancano As JArray = If(TryCast(v("da_posizionare"), JArray), New JArray())
                sFlotte.Aggiungi("Navi messe: " & (Num(v("navi_nemiche_numero")) - mancano.Count).ToString(CultureInfo.InvariantCulture) & " su " & Num(v("navi_nemiche_numero")).ToString(CultureInfo.InvariantCulture))
                If mancano.Count > 0 Then sFlotte.Aggiungi("Prossima: nave da " & Num(v("prossima")).ToString(CultureInfo.InvariantCulture) & " caselle")
            Else
                sFlotte.Aggiungi("Le tue navi in gioco: " & Num(v("mie_navi_rimaste")).ToString(CultureInfo.InvariantCulture))
                sFlotte.Aggiungi("Navi nemiche in gioco: " & Num(v("nemiche_rimaste")).ToString(CultureInfo.InvariantCulture))
            End If
            s.Schermo.Sezioni.Add(sFlotte)
        Catch ex As Exception
            RegistroErrori.Registra("MenuGiochi.ComponiBattaglia", ex)
            Throw
        End Try
    End Sub

    Private Function IstruzioneBattaglia(s As SessioneSchermo) As String
        Try
            Dim v As JObject = Vista(s.Stato)
            Dim fase As String = Txt(v("fase"))
            Dim mia As CasellarioNavale = s.D.MiaFlotta
            If fase = "posizionamento" Then
                If Ha(s, "nave") AndAlso mia IsNot Nothing Then
                    Dim nave As String = "Posiziona la nave da " & Num(v("prossima")).ToString(CultureInfo.InvariantCulture) & ", " & If(mia.Orizzontale, "orizzontale", "verticale") & "."
                    Return Istr(nave, nave & " Frecce per muoverti, Invio la mette, R la gira, Ctrl e freccia destra la mette in verticale, Ctrl e freccia sinistra in orizzontale. C mette a caso le navi che mancano, X toglie l'ultima.")
                End If
                If Ha(s, "pronto") Then Return Istr("Hai messo tutte le navi.", "Hai messo tutte le navi. Premi P quando la flotta è pronta, X toglie l'ultima nave, o muoviti sul casellario per riguardarla.")
                Return "La tua flotta è pronta. Aspetto l'altro giocatore."
            End If
            If fase = "finita" Then Return "La partita è finita."
            If Ha(s, "spara") Then
                Dim dove As String = If(s.D.MareAttivo = "nemica", "sei sul mare nemico", "sei sulla tua flotta")
                Return Istr("Tocca a te, " & dove & ".", "Tocca a te, " & dove & ". " & If(s.D.MareAttivo = "nemica", "Frecce per scegliere la casella, Invio per sparare. M passa alla tua flotta.", "M torna al mare nemico per sparare.") & " T dice la situazione.")
            End If
            Return "Non tocca a te."
        Catch ex As Exception
            RegistroErrori.Registra("MenuGiochi.IstruzioneBattaglia", ex)
            Throw
        End Try
    End Function

    ''' <summary>Tasti della battaglia navale oltre a quelli del casellario (frecce, Invio, R, M) che gestisce SchermoGioco. Vero se il tasto è stato usato.</summary>
    Private Function TastoBattaglia(s As SessioneSchermo, k As Keys) As Boolean
        Try
            Dim v As JObject = Vista(s.Stato)
            Select Case k
                Case Keys.C
                    If Ha(s, "casuale") Then Esegui(s, "casuale") Else NonSiPuo(s)
                Case Keys.X
                    If Ha(s, "annulla") Then Esegui(s, "annulla") Else NonSiPuo(s)
                Case Keys.P
                    If Ha(s, "pronto") Then Esegui(s, "pronto") Else NonSiPuo(s)
                Case Keys.T
                    Informa(s, TestoSituazioneBattaglia(s))
                Case Keys.Q
                    Informa(s, TestoAffondateBattaglia(s))
                Case Else
                    Return False
            End Select
            Return True
        Catch ex As Exception
            RegistroErrori.Registra("MenuGiochi.TastoBattaglia", ex)
            Throw
        End Try
    End Function

    ''' <summary>
    ''' Il computer risponde subito, quindi nello stesso stato ci sono il tuo colpo e il suo. Si mostra un colpo alla volta: qui si tengono solo gli
    ''' eventi fino al primo colpo (ogni colpo comincia con il cannone) e si segna fino a dove si è arrivati; il resto arriva dopo, a suono e voce finiti.
    ''' Nel frattempo non tocca a te. Vero se lo stato è stato spezzato.
    ''' </summary>
    Private Shared Function SpezzaTurniBattaglia(stato As JObject, d As DatiPartita) As Boolean
        Try
            If Txt(stato("stato")) <> "in corso" Then Return False   ' a fine partita si mostra tutto insieme
            Dim ev As JArray = TryCast(stato("eventi"), JArray)
            If ev Is Nothing Then Return False
            Dim colpi As New List(Of Integer)()
            For i As Integer = 0 To ev.Count - 1
                If Txt(ev(i)("suono")) = "cannone" Then colpi.Add(i)
            Next
            If colpi.Count < 2 Then Return False
            Dim testa As New JArray(ev.Take(colpi(1)))
            Dim ultimo As JToken = testa.Last()("n")
            If ultimo Is Nothing OrElse ultimo.Type = JTokenType.Null Then Return False
            stato("eventi") = testa
            stato("mosse") = New JArray()
            Dim posto As Integer = Num(stato("posto"))
            stato("turno") = 1 - posto
            stato("attore") = 1 - posto
            d.Da = Convert.ToInt32(ultimo)
            Return True
        Catch exLog As Exception
            RegistroErrori.Registra("MenuGiochi.SpezzaTurniBattaglia", exLog)
            Return False
        End Try
    End Function

    ''' <summary>Aspetta che finiscano i suoni e la voce (al massimo una ventina di secondi).</summary>
    Private Shared Async Function AttendiFineAudioAsync() As Task
        Try
            Await Task.Delay(400)
            Dim voce As sintesivocale = sintesivocale.GetInstance()
            Dim t As Integer = 0
            While (SuoniGiochi.InCorso() OrElse voce.StaParlando) AndAlso t < 20000
                Await Task.Delay(100)
                t += 100
            End While
            Await Task.Delay(250)
        Catch exLog As Exception
            RegistroErrori.Registra("MenuGiochi.AttendiFineAudioAsync", exLog)
        End Try
    End Function

    Private Function TestoAffondateBattaglia(s As SessioneSchermo) As String
        Try
            Dim perse As JArray = If(TryCast(Vista(s.Stato)("navi_nemiche_perse"), JArray), New JArray())
            If perse.Count = 0 Then Return "Non hai ancora affondato nessuna nave."
            Return "Hai affondato: " & String.Join(", ", perse.Select(Function(x) "nave da " & Txt(x))) & "."
        Catch ex As Exception
            RegistroErrori.Registra("MenuGiochi.TestoAffondateBattaglia", ex)
            Throw
        End Try
    End Function

    Private Function TestoSituazioneBattaglia(s As SessioneSchermo) As String
        Try
            Dim v As JObject = Vista(s.Stato)
            If Txt(v("fase")) = "posizionamento" Then
                Dim mancano As JArray = If(TryCast(v("da_posizionare"), JArray), New JArray())
                Return "Hai messo " & (Num(v("navi_nemiche_numero")) - mancano.Count).ToString(CultureInfo.InvariantCulture) & " navi su " & Num(v("navi_nemiche_numero")).ToString(CultureInfo.InvariantCulture) & "."
            End If
            Return "Hai ancora " & Num(v("mie_navi_rimaste")).ToString(CultureInfo.InvariantCulture) & " navi, il nemico ne ha " & Num(v("nemiche_rimaste")).ToString(CultureInfo.InvariantCulture) & "."
        Catch ex As Exception
            RegistroErrori.Registra("MenuGiochi.TestoSituazioneBattaglia", ex)
            Throw
        End Try
    End Function

    ''' <summary>Invio sul casellario: posiziona la nave o spara.</summary>
    Private Sub CasellaBattaglia(s As SessioneSchermo, riga As Integer, colonna As Integer)
        Try
            Dim v As JObject = Vista(s.Stato)
            Dim fase As String = Txt(v("fase"))
            Dim c As CasellarioNavale = MareAttuale(s)
            If fase = "posizionamento" Then
                If Ha(s, "nave") Then
                    If Not c.PuoPosizionare(riga, colonna) Then
                        Leggi("Qui la nave da " & c.LunghezzaNave.ToString(CultureInfo.InvariantCulture) & " non ci sta. " & c.Frase())
                        Return
                    End If
                    Esegui(s, "nave|" & riga.ToString(CultureInfo.InvariantCulture) & "|" & colonna.ToString(CultureInfo.InvariantCulture) & "|" & If(c.Orizzontale, "o", "v"))
                ElseIf Ha(s, "pronto") Then
                    Leggi(Istr("Hai messo tutte le navi.", "Hai messo tutte le navi. Premi P quando la flotta è pronta."))
                Else
                    Leggi(s.Istruzione)
                End If
                Return
            End If
            If fase = "battaglia" Then
                If s.D.MareAttivo <> "nemica" Then
                    Leggi(c.Frase())
                ElseIf Not Ha(s, "spara") Then
                    Leggi("Non tocca a te.")
                ElseIf c.Stato(riga, colonna) <> StatoCasella.Vuota Then
                    Leggi("Hai già sparato qui. " & c.Frase())
                Else
                    Esegui(s, "spara|" & riga.ToString(CultureInfo.InvariantCulture) & "|" & colonna.ToString(CultureInfo.InvariantCulture))
                End If
                Return
            End If
            Leggi(c.Frase())
        Catch ex As Exception
            _logger.ScriviLog("MenuGiochi.CasellaBattaglia: " & ex.ToString())
        End Try
    End Sub

    ''' <summary>R: gira la nave da posizionare.</summary>
    Private Sub RuotaBattaglia(s As SessioneSchermo, Optional orizzontale As Boolean? = Nothing)
        Try
            Dim m As CasellarioNavale = s.D.MiaFlotta
            If m Is Nothing OrElse m.LunghezzaNave <= 0 OrElse s.D.MareAttivo <> "mia" Then
                NonSiPuo(s)
                Return
            End If
            ' R gira la nave; Ctrl+sinistra la mette in orizzontale, Ctrl+destra in verticale
            If orizzontale.HasValue Then m.Orizzontale = orizzontale.Value Else m.Ruota()
            Leggi(m.FraseNaveScelta() & " " & If(m.PuoPosizionare(m.Riga, m.Colonna), "Qui ci sta.", "Qui non ci sta."))
            s.Schermo.Aggiorna()
            s.Istruzione = IstruzioneBattaglia(s)
            s.Schermo.Istruzione = s.Istruzione
        Catch ex As Exception
            RegistroErrori.Registra("MenuGiochi.RuotaBattaglia", ex)
            Throw
        End Try
    End Sub

    ''' <summary>M: dal mare nemico alla propria flotta e viceversa (solo durante la battaglia).</summary>
    Private Sub CambiaMareBattaglia(s As SessioneSchermo)
        Try
            Dim v As JObject = Vista(s.Stato)
            If Txt(v("fase")) = "posizionamento" Then
                NonSiPuo(s)
                Return
            End If
            s.D.MareAttivo = If(s.D.MareAttivo = "mia", "nemica", "mia")
            s.Schermo.ImpostaCasellario(MareAttuale(s))
            s.Istruzione = IstruzioneBattaglia(s)
            s.Schermo.Istruzione = s.Istruzione
            Dim c As CasellarioNavale = MareAttuale(s)
            Leggi(c.Titolo & ". " & c.Frase())
        Catch ex As Exception
            RegistroErrori.Registra("MenuGiochi.CambiaMareBattaglia", ex)
            Throw
        End Try
    End Sub

End Class
