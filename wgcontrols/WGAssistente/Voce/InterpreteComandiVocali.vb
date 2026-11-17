Imports System.IO
Imports System.Net
Imports System.Security.Policy
Imports System.Runtime.InteropServices
Imports System.Runtime.CompilerServices
Imports System.Text
Imports wgcontrols
Imports WGFileManager
Imports WGDati
Imports System.Threading
Imports System.Threading.Tasks
Imports System.Windows.Forms
Imports System.Xml
Imports System.ComponentModel
Imports System.Security
Imports System.Diagnostics
Imports WGYoutube
Imports System.Linq
Imports System.Collections.Concurrent
Imports System.Security.Principal
Imports System.Management
Imports WGRadioTv
Imports WGSalute
Imports WGOpenStrictMap
Imports Newtonsoft.Json.Linq
Imports HtmlAgilityPack
Imports System.Text.RegularExpressions
Imports System.Data.SQLite
Imports WGLibriTesto
Imports WGInternetArchive
Imports WGMediaPlayer
Imports WGIA
Imports WGMail
Imports WGStampante
Imports WGContatti
Imports WGConnessioni

' Analisi e interpretazione delle frasi pronunciate per trasformarle in comandi eseguibili.
Public Class InterpreteComandiVocali

    Private Const OPZIONI As RegexOptions = RegexOptions.IgnoreCase Or RegexOptions.CultureInvariant

    Private Const NOMI_MAIL As String = "(?:e-?\s?mail|email|mail|posta\s+elettronica|messaggio\s+di\s+posta|messaggio)"
    Private Const VERBI_INVIO As String = "(?:manda|mandami|mandare|invia|inviare|scrivi|scrivere|spedisci|spedire|componi|comporre|prepara|preparare)"
    Private Const VERBI_RIPRODUZIONE As String = "(?:metti|mettimi|mettere|riproduci|riproducimi|riprodurre|fai\s+partire|far\s+partire|avvia|avviare|fammi\s+(?:vedere|sentire|ascoltare)|vedere|sentire|guarda|guardare|ascolta|ascoltare|accendi|accendere|cerca|cercare|trova|trovare|apri|aprire|sintonizza(?:ti)?(?:\s+su)?)"
    Private Const MESI As String = "(?:gennaio|febbraio|marzo|aprile|maggio|giugno|luglio|agosto|settembre|ottobre|novembre|dicembre)"
    Private Const GIORNI_SETTIMANA As String = "(?:luned[iì]|marted[iì]|mercoled[iì]|gioved[iì]|venerd[iì]|sabato|domenica)"
    Private Const YOUTUBE As String = "(?:you\s*tube|iu\s*tub[e]?|yu\s*tub[e]?|you\s*tub)"
    Private Const OGGETTI_VIDEO As String = "(?:vide[oi]|clip|canzon[ei]|bran[oi])"
    Private Const VERBI_RICERCA_VIDEO As String = "(?:cerca(?:mi|re|rmi|ci)?|cerchi(?:amo)?|cercheresti|ricerca(?:re)?|fai(?:mi)?\s+una\s+ricerca|trova(?:mi|re|rmi)?|trovi|elenca(?:mi|re)?|mostra(?:mi)?\s+(?:i|dei|degli|le|l'elenco\s+dei)\s+(?:vide[oi]|clip|canzon[ei]|bran[oi]))"
    Private Const ELENCO_VIDEO As String = "(?:l'elenco|elenco\s+de[il]|la\s+lista\s+de[il]|lista\s+de[il]|i\s+risultati|risultati\s+della\s+ricerca|quali\s+(?:vide[oi]|canzon[ei])|che\s+(?:vide[oi]|canzon[ei])\s+ci\s+sono)"
    Private Const VERBI_RIPRODUZIONE_VIDEO As String = "(?:metti(?:mi|lo|la|li|le)?|mettere|riproduc[ie](?:mi|lo|la|li|le)?|riprodurre|fa(?:i|mmi)?\s+partire|far\s+partire|fall[oa]\s+partire|avvia(?:mi|lo|la|li|re)?|fammi\s+(?:vedere|sentire|ascoltare)|fa(?:i|mmi)\s+sentire|vedere|sentire|guarda(?:re)?|ascolta(?:re)?|suona(?:mi|re)?|play|trasmetti|manda\s+in\s+onda|apri(?:mi|re)?)"
    Private Const SEGUITO_RIPRODUZIONE As String = "(?:metti(?:lo|la|li|le)?|riproduc[ie](?:lo|la|li|le)?|avvia(?:lo|la|li|le)?|fall[oa]\s+partire|fai\s+partire|mandal[oa])"

    Private Shared ReadOnly _numeri As Dictionary(Of String, Integer) = CreaNumeri()
    Private Shared ReadOnly _paroleNumeri As String = CreaAlternativaNumeri()
    Private Shared ReadOnly _cultura As System.Globalization.CultureInfo = System.Globalization.CultureInfo.GetCultureInfo("it-IT")

    Private Class DataOraEstratta
        Public Property HaData As Boolean = False
        Public Property Data As DateTime = DateTime.Today
        Public Property OraInizio As TimeSpan = TimeSpan.Zero
        Public Property HaOraInizio As Boolean = False
        Public Property OraFine As TimeSpan = TimeSpan.Zero
        Public Property HaOraFine As Boolean = False
        Public Property DurataMinuti As Integer = 0
        Public Property Periodo As String = ""
        Public Property Resto As String = ""
    End Class

    Private Shared Function CreaNumeri() As Dictionary(Of String, Integer)
        Dim risultato As New Dictionary(Of String, Integer)(StringComparer.OrdinalIgnoreCase)
        Try
            Dim unita As String() = {"zero", "uno", "due", "tre", "quattro", "cinque", "sei", "sette", "otto", "nove", "dieci",
                                     "undici", "dodici", "tredici", "quattordici", "quindici", "sedici", "diciassette", "diciotto", "diciannove"}
            For i As Integer = 0 To unita.Length - 1
                risultato(unita(i)) = i
            Next

            Dim decine As String() = {"venti", "trenta", "quaranta", "cinquanta"}
            For d As Integer = 0 To decine.Length - 1
                Dim valoreDecina As Integer = (d + 2) * 10
                Dim radice As String = decine(d)
                risultato(radice) = valoreDecina
                For u As Integer = 1 To 9
                    Dim parolaUnita As String = unita(u)
                    Dim composta As String
                    If u = 1 OrElse u = 8 Then
                        composta = radice.Substring(0, radice.Length - 1) & parolaUnita
                    Else
                        composta = radice & parolaUnita
                    End If
                    risultato(composta) = valoreDecina + u
                    If u = 3 Then risultato(radice & "tré") = valoreDecina + 3
                Next
            Next

            risultato("una") = 1
            risultato("un") = 1
            risultato("primo") = 1
        Catch ex As Exception
            log.ScriviLog("InterpreteComandiVocali.CreaNumeri: " & ex.Message & Environment.NewLine & ex.StackTrace)
        End Try
        Return risultato
    End Function

    Private Shared Function CreaAlternativaNumeri() As String
        Try
            Dim chiavi As New List(Of String)(_numeri.Keys)
            chiavi.Sort(AddressOf ConfrontaPerLunghezzaDecrescente)
            Dim escape As New List(Of String)
            For Each chiave As String In chiavi
                escape.Add(Regex.Escape(chiave))
            Next
            Return String.Join("|", escape)
        Catch ex As Exception
            log.ScriviLog("InterpreteComandiVocali.CreaAlternativaNumeri: " & ex.Message & Environment.NewLine & ex.StackTrace)
            Return "una|due|tre"
        End Try
    End Function

    Private Shared Function ConfrontaPerLunghezzaDecrescente(a As String, b As String) As Integer
        Try
            Return b.Length.CompareTo(a.Length)
        Catch ex As Exception
            log.ScriviLog("InterpreteComandiVocali.ConfrontaPerLunghezzaDecrescente: " & ex.Message)
            Return 0
        End Try
    End Function

    Private Shared Function NumeroGenerico() As String
        Return "(?:\d{1,2}|" & _paroleNumeri & ")"
    End Function

    Private Shared Function ModelloOrario(suffisso As String) As String
        Dim numero As String = NumeroGenerico()
        Return "(?<h" & suffisso & ">" & numero & ")" &
               "(?:(?:[:.](?<m" & suffisso & ">\d{2}))|(?:\s+e\s+(?<mp" & suffisso & ">mezza|mezzo|un\s+quarto|tre\s+quarti|" & numero & "))|(?:\s+meno\s+(?:un\s+)?(?<mq" & suffisso & ">quarto)))?"
    End Function

    Public Function Interpreta(testo As String) As ComandoVocale
        Dim comando As New ComandoVocale()
        Try
            comando.TestoOriginale = If(testo, "").Trim()
            Dim frase As String = PulisciFrase(comando.TestoOriginale)
            If frase = "" Then Return comando

            If ProvaInvioEmail(frase, comando) Then Return comando
            If ProvaLetturaPosta(frase, comando) Then Return comando
            If ProvaAppuntamentiGiorno(frase, comando) Then Return comando
            If ProvaAppuntamento(frase, comando) Then Return comando
            If ProvaVideo(frase, comando) Then Return comando
            If ProvaRadio(frase, comando) Then Return comando
            If ProvaMeteo(frase, comando) Then Return comando
            If ProvaOraData(frase, comando) Then Return comando
            If ProvaApriSezione(frase, comando) Then Return comando
            If ProvaComandoSomigliante(frase, comando) Then Return comando
        Catch ex As Exception
            log.ScriviLog("InterpreteComandiVocali.Interpreta: " & ex.Message & Environment.NewLine & ex.StackTrace)
        End Try
        comando.Tipo = TipoComandoVocale.Sconosciuto
        Return comando
    End Function

    Private Function PulisciFrase(testo As String) As String
        Try
            Dim t As String = If(testo, "").Replace(ChrW(&H2019), "'").Replace(ChrW(&H2018), "'").Replace("`", "'")
            t = t.Replace(ChrW(&HAB), "").Replace(ChrW(&HBB), "").Replace("""", "")
            t = Regex.Replace(t, "\s+", " ").Trim()
            Dim caratteriBordo As Char() = {" "c, "."c, "!"c, "?"c, ","c, ";"c, ":"c}
            t = t.Trim(caratteriBordo)
            For passaggio As Integer = 1 To 2
                t = Regex.Replace(t, "^(?:(?:ehi|ehm|ok|okay|allora|senti|dunque|per\s+favore|per\s+piacere|e[hi]\s+win\s*gabriel|win\s*gabriel|gabriel|gabriele)\b[\s,]*)+", "", OPZIONI)
                t = Regex.Replace(t, "^(?:(?:puoi|potresti|riesci\s+a|vorrei\s+che\s+tu|vorrei|voglio|devi|devo|dovrei|dobbiamo|ho\s+bisogno\s+di|mi\s+serve|mi|me\s+lo|me\s+la)\s+)+", "", OPZIONI)
                t = Regex.Replace(t, "[\s,]*(?:per\s+favore|per\s+piacere|grazie(?:\s+mille)?)$", "", OPZIONI)
                t = t.Trim(caratteriBordo)
            Next
            Return t
        Catch ex As Exception
            log.ScriviLog("InterpreteComandiVocali.PulisciFrase: " & ex.Message & Environment.NewLine & ex.StackTrace)
            Return If(testo, "").Trim()
        End Try
    End Function

    Private Function ProvaInvioEmail(frase As String, comando As ComandoVocale) As Boolean
        Try
            Dim resto As String = Nothing

            Dim m As Match = Regex.Match(frase, "^" & VERBI_INVIO & "\s+(?:(?:una|un'|un|la|l')\s*)?(?:nuova\s+|nuovo\s+)?" & NOMI_MAIL & "\s+(?:a|ad|per)\s+(?<resto>.+)$", OPZIONI)
            If m.Success Then resto = m.Groups("resto").Value

            If resto Is Nothing Then
                m = Regex.Match(frase, "^" & VERBI_INVIO & "\s+(?:a|ad)\s+(?<dest>.+?)\s+(?:una|un'|un)\s*" & NOMI_MAIL & "(?<coda>.*)$", OPZIONI)
                If m.Success Then resto = m.Groups("dest").Value & " " & m.Groups("coda").Value.Trim()
            End If

            If resto Is Nothing Then
                m = Regex.Match(frase, "^" & VERBI_INVIO & "\s+(?:(?:una|un'|un|la|l')\s*)?(?:nuova\s+|nuovo\s+)?" & NOMI_MAIL & "$", OPZIONI)
                If m.Success Then resto = ""
            End If

            If resto Is Nothing Then Return False

            comando.Tipo = TipoComandoVocale.InviaEmail
            SeparaDatiEmail(resto.Trim(), comando)
            Return True
        Catch ex As Exception
            log.ScriviLog("InterpreteComandiVocali.ProvaInvioEmail: " & ex.Message & Environment.NewLine & ex.StackTrace)
            Return False
        End Try
    End Function

    Private Class MarcatoreEmail
        Public Property Indice As Integer
        Public Property Lunghezza As Integer
        Public Property Tipo As String
    End Class

    Private Function ConfrontaMarcatori(a As MarcatoreEmail, b As MarcatoreEmail) As Integer
        Try
            Return a.Indice.CompareTo(b.Indice)
        Catch ex As Exception
            log.ScriviLog("InterpreteComandiVocali.ConfrontaMarcatori: " & ex.Message)
            Return 0
        End Try
    End Function

    Private Sub SeparaDatiEmail(resto As String, comando As ComandoVocale)
        Try
            If String.IsNullOrWhiteSpace(resto) Then Exit Sub

            Dim marcatori As New List(Of MarcatoreEmail)
            Dim mCorpo As Match = Regex.Match(resto, "(?:\s*[:,]\s*|\s+)(?:dicendo(?:gli|le)?|scrivendo(?:gli|le)?|che\s+dice|con\s+scritto|con\s+(?:il\s+)?testo|con\s+(?:il\s+)?messaggio|e\s+(?:scrivi|scrivigli|scrivile|digli|dille|di')|testo)\s*:?\s*", OPZIONI)
            If Not mCorpo.Success Then mCorpo = Regex.Match(resto, "\s*:\s*", OPZIONI)
            If mCorpo.Success Then marcatori.Add(New MarcatoreEmail With {.Indice = mCorpo.Index, .Lunghezza = mCorpo.Length, .Tipo = "corpo"})

            Dim mOggetto As Match = Regex.Match(resto, "(?:\s*,\s*|\s+)(?:e\s+)?(?:con\s+)?(?:l'\s*|come\s+)?oggetto\s*:?\s*", OPZIONI)
            If mOggetto.Success Then marcatori.Add(New MarcatoreEmail With {.Indice = mOggetto.Index, .Lunghezza = mOggetto.Length, .Tipo = "oggetto"})

            marcatori.Sort(AddressOf ConfrontaMarcatori)

            Dim fineDestinatario As Integer = If(marcatori.Count > 0, marcatori(0).Indice, resto.Length)
            comando.Destinatario = PulisciBordi(resto.Substring(0, fineDestinatario))

            For i As Integer = 0 To marcatori.Count - 1
                Dim inizioValore As Integer = marcatori(i).Indice + marcatori(i).Lunghezza
                Dim fineValore As Integer = If(i + 1 < marcatori.Count, marcatori(i + 1).Indice, resto.Length)
                If fineValore < inizioValore Then Continue For
                Dim valore As String = PulisciBordi(resto.Substring(inizioValore, fineValore - inizioValore))
                If marcatori(i).Tipo = "oggetto" Then
                    comando.Oggetto = Maiuscola(valore)
                Else
                    valore = Regex.Replace(valore, "^che\s+", "", OPZIONI)
                    valore = Maiuscola(valore)
                    If valore <> "" AndAlso Not valore.EndsWith(".") AndAlso Not valore.EndsWith("?") AndAlso Not valore.EndsWith("!") Then valore &= "."
                    comando.Corpo = valore
                End If
            Next
        Catch ex As Exception
            log.ScriviLog("InterpreteComandiVocali.SeparaDatiEmail: " & ex.Message & Environment.NewLine & ex.StackTrace)
        End Try
    End Sub

    Private Function ProvaLetturaPosta(frase As String, comando As ComandoVocale) As Boolean
        Try
            Dim nomi As String = "(?:posta|e-?\s?mail|email|mail|messaggi|messaggio)"
            Dim verbi As String = "(?:leggi|leggimi|leggere|controlla|controllare|verifica|verificare|dimmi|guarda|c'è|c'e'|ci\s+sono|ho|abbiamo|arrivat[aoie]|apri|aprimi|aprire|fammi\s+sentire)"

            If Regex.IsMatch(frase, "\b" & verbi & "\b", OPZIONI) AndAlso
               Regex.IsMatch(frase, "\bultim[aoie]\b|\bpi[uù]\s+recente\b", OPZIONI) AndAlso
               Regex.IsMatch(frase, "\b" & nomi & "\b", OPZIONI) Then
                comando.Tipo = TipoComandoVocale.LeggiUltimaEmail
                comando.SoloNonLetti = Not Regex.IsMatch(frase, "\bgi[aà]\s+lett|\banche\s+lett", OPZIONI)
                Return True
            End If

            If Regex.IsMatch(frase, "^(?:apri|aprimi|aprire)\s+(?:la\s+)?posta$", OPZIONI) Then Return False

            If Regex.IsMatch(frase, "^" & verbi & "\b.*\b" & nomi & "\b", OPZIONI) OrElse
               Regex.IsMatch(frase, "^(?:nuov[ei]\s+)?(?:e-?\s?mail|email|mail|messaggi)(?:\s+nuov[ei])?$", OPZIONI) Then
                If Regex.IsMatch(frase, "^(?:apri|aprimi|aprire)\b", OPZIONI) AndAlso Not Regex.IsMatch(frase, "\bnuov|\bda\s+leggere|\bnon\s+lett", OPZIONI) Then Return False
                comando.Tipo = TipoComandoVocale.LeggiPosta
                comando.SoloNonLetti = Not Regex.IsMatch(frase, "\btutt[aeio]\b", OPZIONI)
                Return True
            End If
            Return False
        Catch ex As Exception
            log.ScriviLog("InterpreteComandiVocali.ProvaLetturaPosta: " & ex.Message & Environment.NewLine & ex.StackTrace)
            Return False
        End Try
    End Function

    Private Function ProvaAppuntamento(frase As String, comando As ComandoVocale) As Boolean
        Try
            Dim resto As String = Nothing
            Dim nomiEvento As String = "(?:appuntamento|impegno|promemoria|evento|visita|riunione)"

            Dim m As Match = Regex.Match(frase, "^(?:aggiungi|aggiungere|crea|creare|segna|segnare|metti|mettere|inserisci|inserire|fissa|fissare|programma|programmare|annota|annotare|nuovo|nuova)\s+(?:(?:un|uno|una|un'|l'|il)\s*)?(?:nuovo\s+)?" & nomiEvento & "\b(?<resto>.*)$", OPZIONI)
            If m.Success Then resto = m.Groups("resto").Value

            If resto Is Nothing Then
                m = Regex.Match(frase, "^(?:ricordami|ricordarmi|ricordi|ricorda|ricordare)\s+(?<resto>.+)$", OPZIONI)
                If m.Success Then resto = m.Groups("resto").Value
            End If

            If resto Is Nothing Then
                m = Regex.Match(frase, "^(?:segna|segnare|metti|mettere|aggiungi|aggiungere|scrivi|scrivere|annota|annotare)\s+(?:in|nell'|nella|sull'|sulla)\s*agenda\s*(?<resto>.*)$", OPZIONI)
                If m.Success Then resto = m.Groups("resto").Value
            End If

            If resto Is Nothing Then
                m = Regex.Match(frase, "^(?:ho|avr[oò])\s+(?:un|una|un')\s*" & nomiEvento & "\b(?<resto>.*)$", OPZIONI)
                If m.Success Then resto = m.Groups("resto").Value
            End If

            If resto Is Nothing Then Return False

            comando.Tipo = TipoComandoVocale.NuovoAppuntamento
            Dim estratta As DataOraEstratta = EstraiDataOra(resto)
            ApplicaDataOra(estratta, comando)
            comando.Argomento = PulisciDescrizione(estratta.Resto)
            Return True
        Catch ex As Exception
            log.ScriviLog("InterpreteComandiVocali.ProvaAppuntamento: " & ex.Message & Environment.NewLine & ex.StackTrace)
            Return False
        End Try
    End Function

    Private Function ProvaAppuntamentiGiorno(frase As String, comando As ComandoVocale) As Boolean
        Try
            Dim nomi As String = "(?:appuntament[io]|impegn[io]|programma|agenda|scadenz[ae])"
            Dim trovato As Boolean =
                Regex.IsMatch(frase, "^(?:che|quali|quanti)\s+(?:appuntament[io]|impegn[io]|scadenz[ae])\s+(?:ho|abbiamo|c'[eè]|ci\s+sono)\b", OPZIONI) OrElse
                Regex.IsMatch(frase, "^(?:cosa|che\s+cosa)\s+(?:ho|c'[eè])\s+(?:in\s+agenda|da\s+fare|in\s+programma|oggi|domani|dopodomani|" & GIORNI_SETTIMANA & ")", OPZIONI) OrElse
                Regex.IsMatch(frase, "^(?:leggi|leggimi|dimmi|mostrami|elenca|controlla|ripetimi)\s+(?:(?:gli|i|il|l'|la|le)\s*)?(?:miei\s+|mie\s+)?" & nomi & "\b", OPZIONI) OrElse
                Regex.IsMatch(frase, "^(?:ho|abbiamo)\s+(?:degli\s+|qualche\s+)?(?:appuntament[io]|impegn[io])\b(?!\s+(?:con|dal|dalla|dallo|alle|per|a|al|alla|di)\b)", OPZIONI) OrElse
                Regex.IsMatch(frase, "^(?:il\s+)?(?:programma|agenda|impegni|appuntamenti)\s+(?:di|per)\s+(?:oggi|domani|dopodomani|" & GIORNI_SETTIMANA & ")$", OPZIONI)
            If Not trovato Then Return False

            comando.Tipo = TipoComandoVocale.AppuntamentiGiorno
            Dim estratta As DataOraEstratta = EstraiDataOra(frase)
            comando.Inizio = If(estratta.HaData, estratta.Data.Date, DateTime.Today)
            comando.GiorniDaOggi = CInt((comando.Inizio - DateTime.Today).TotalDays)
            Return True
        Catch ex As Exception
            log.ScriviLog("InterpreteComandiVocali.ProvaAppuntamentiGiorno: " & ex.Message & Environment.NewLine & ex.StackTrace)
            Return False
        End Try
    End Function

    Private Sub ApplicaDataOra(estratta As DataOraEstratta, comando As ComandoVocale)
        Try
            If estratta Is Nothing Then Exit Sub

            Dim ora As TimeSpan = estratta.OraInizio
            Dim oraFine As TimeSpan = estratta.OraFine
            If estratta.HaOraInizio Then ora = CorreggiPeriodo(ora, estratta.Periodo)
            If estratta.HaOraFine Then
                oraFine = CorreggiPeriodo(oraFine, estratta.Periodo)
                If estratta.HaOraInizio AndAlso oraFine <= ora AndAlso oraFine.Hours < 12 Then oraFine = oraFine.Add(TimeSpan.FromHours(12))
            End If

            If Not estratta.HaData AndAlso Not estratta.HaOraInizio Then
                comando.Inizio = DateTime.MinValue
                Exit Sub
            End If

            Dim data As DateTime = estratta.Data.Date
            If Not estratta.HaData Then
                data = DateTime.Today
                If DateTime.Today.Add(ora) <= DateTime.Now Then data = DateTime.Today.AddDays(1)
            End If

            If Not estratta.HaOraInizio Then
                comando.TuttoIlGiorno = True
                comando.Inizio = data
                comando.Fine = data
                Exit Sub
            End If

            comando.TuttoIlGiorno = False
            comando.Inizio = data.Add(ora)
            If estratta.HaOraFine AndAlso oraFine > ora Then
                comando.Fine = data.Add(oraFine)
            ElseIf estratta.DurataMinuti > 0 Then
                comando.Fine = comando.Inizio.AddMinutes(estratta.DurataMinuti)
            Else
                comando.Fine = DateTime.MinValue
            End If
        Catch ex As Exception
            log.ScriviLog("InterpreteComandiVocali.ApplicaDataOra: " & ex.Message & Environment.NewLine & ex.StackTrace)
        End Try
    End Sub

    Private Function CorreggiPeriodo(ora As TimeSpan, periodo As String) As TimeSpan
        Try
            Dim ore As Integer = ora.Hours
            Dim p As String = If(periodo, "").ToLowerInvariant()
            If p.Contains("pomeriggio") OrElse p.Contains("sera") Then
                If ore < 12 Then ore += 12
            ElseIf p.Contains("notte") Then
                If ore = 12 Then ore = 0
            ElseIf p = "" Then
                If ore >= 1 AndAlso ore <= 6 Then ore += 12
            End If
            If ore > 23 Then ore -= 12
            Return New TimeSpan(ore, ora.Minutes, 0)
        Catch ex As Exception
            log.ScriviLog("InterpreteComandiVocali.CorreggiPeriodo: " & ex.Message)
            Return ora
        End Try
    End Function

    Private Function EstraiDataOra(testo As String) As DataOraEstratta
        Dim risultato As New DataOraEstratta()
        Try
            Dim lavoro As String = " " & If(testo, "") & " "
            Dim m As Match

            m = Regex.Match(lavoro, "\b(?:dalle|dall'|dalla|da)\s*(?:ore\s+)?" & ModelloOrario("1") & "\s+(?:alle|all'|a)\s*(?:ore\s+)?" & ModelloOrario("2") & "\b", OPZIONI)
            If m.Success Then
                Dim inizio As TimeSpan
                Dim fine As TimeSpan
                If LeggiOrario(m, "1", inizio) AndAlso LeggiOrario(m, "2", fine) Then
                    risultato.OraInizio = inizio
                    risultato.HaOraInizio = True
                    risultato.OraFine = fine
                    risultato.HaOraFine = True
                    lavoro = Rimuovi(lavoro, m)
                End If
            End If

            m = Regex.Match(lavoro, "\bper\s+(?<d>(?:un'?\s*|una\s+)ora(?:\s+e\s+mezza)?|mezz'?\s*ora|" & NumeroGenerico() & "\s+ore(?:\s+e\s+mezza)?)\b", OPZIONI)
            If m.Success Then
                risultato.DurataMinuti = LeggiDurata(m.Groups("d").Value)
                lavoro = Rimuovi(lavoro, m)
            End If

            m = Regex.Match(lavoro, "\b(?:fino\s+alle|fino\s+all'|fino\s+a)\s*(?:ore\s+)?" & ModelloOrario("2") & "\b", OPZIONI)
            If m.Success Then
                Dim fine As TimeSpan
                If LeggiOrario(m, "2", fine) Then
                    risultato.OraFine = fine
                    risultato.HaOraFine = True
                    lavoro = Rimuovi(lavoro, m)
                End If
            End If

            If Not risultato.HaOraInizio Then
                m = Regex.Match(lavoro, "\b(?:alle\s+ore|alle|all'|verso\s+le|verso\s+l'|per\s+le|per\s+l'|entro\s+le|ore)\s*" & ModelloOrario("1") & "\b", OPZIONI)
                If m.Success Then
                    Dim inizio As TimeSpan
                    If LeggiOrario(m, "1", inizio) Then
                        risultato.OraInizio = inizio
                        risultato.HaOraInizio = True
                        lavoro = Rimuovi(lavoro, m)
                    End If
                End If
            End If

            If Not risultato.HaOraInizio Then
                m = Regex.Match(lavoro, "\b(?:a\s+|per\s+|verso\s+)?mezzogiorno(?<mezza>\s+e\s+mezza)?\b", OPZIONI)
                If m.Success Then
                    risultato.OraInizio = New TimeSpan(12, If(m.Groups("mezza").Success, 30, 0), 0)
                    risultato.HaOraInizio = True
                    risultato.Periodo = "mezzogiorno"
                    lavoro = Rimuovi(lavoro, m)
                End If
            End If

            If Not risultato.HaOraInizio Then
                m = Regex.Match(lavoro, "\b(?:a\s+|per\s+|verso\s+)?mezzanotte\b", OPZIONI)
                If m.Success Then
                    risultato.OraInizio = New TimeSpan(0, 0, 0)
                    risultato.HaOraInizio = True
                    risultato.Periodo = "notte"
                    lavoro = Rimuovi(lavoro, m)
                End If
            End If

            m = Regex.Match(lavoro, "\bdopodomani\b", OPZIONI)
            If m.Success Then
                ImpostaData(risultato, DateTime.Today.AddDays(2))
                lavoro = Rimuovi(lavoro, m)
            End If

            If Not risultato.HaData Then
                m = Regex.Match(lavoro, "\bdomani\b", OPZIONI)
                If m.Success Then
                    ImpostaData(risultato, DateTime.Today.AddDays(1))
                    lavoro = Rimuovi(lavoro, m)
                End If
            End If

            If Not risultato.HaData Then
                m = Regex.Match(lavoro, "\b(?:tra|fra)\s+(?<n>" & NumeroGenerico() & ")\s+(?<u>giorn[oi]|settiman[ae])\b|\b(?:tra|fra)\s+una\s+(?<u>settimana)\b", OPZIONI)
                If m.Success Then
                    Dim quanti As Integer = If(m.Groups("n").Success, NumeroDaTesto(m.Groups("n").Value), 1)
                    If quanti < 0 Then quanti = 1
                    If m.Groups("u").Value.ToLowerInvariant().StartsWith("settiman") Then quanti *= 7
                    ImpostaData(risultato, DateTime.Today.AddDays(quanti))
                    lavoro = Rimuovi(lavoro, m)
                End If
            End If

            If Not risultato.HaData Then
                m = Regex.Match(lavoro, "\b(?:(?:il|la|di|per)\s+)?(?:prossim[oa]\s+)?(?<g>" & GIORNI_SETTIMANA & ")(?:\s+prossim[oa])?\b", OPZIONI)
                If m.Success Then
                    ImpostaData(risultato, ProssimoGiornoSettimana(m.Groups("g").Value, False))
                    lavoro = Rimuovi(lavoro, m)
                End If
            End If

            If Not risultato.HaData Then
                m = Regex.Match(lavoro, "\b(?:(?:il|l'|per\s+il|per\s+l')\s*)?(?<gg>\d{1,2}|" & _paroleNumeri & ")\s+(?<mm>" & MESI & ")(?:\s+(?<aa>\d{4}))?\b", OPZIONI)
                If m.Success Then
                    Dim data As DateTime
                    If CostruisciData(NumeroDaTesto(m.Groups("gg").Value), NumeroMese(m.Groups("mm").Value), If(m.Groups("aa").Success, CInt(m.Groups("aa").Value), 0), data) Then
                        ImpostaData(risultato, data)
                        lavoro = Rimuovi(lavoro, m)
                    End If
                End If
            End If

            If Not risultato.HaData Then
                m = Regex.Match(lavoro, "\b(?:(?:il|l'|per\s+il)\s*)?(?<gg>\d{1,2})[/-](?<mm>\d{1,2})(?:[/-](?<aa>\d{2,4}))?\b", OPZIONI)
                If m.Success Then
                    Dim anno As Integer = 0
                    If m.Groups("aa").Success Then
                        anno = CInt(m.Groups("aa").Value)
                        If anno < 100 Then anno += 2000
                    End If
                    Dim data As DateTime
                    If CostruisciData(CInt(m.Groups("gg").Value), CInt(m.Groups("mm").Value), anno, data) Then
                        ImpostaData(risultato, data)
                        lavoro = Rimuovi(lavoro, m)
                    End If
                End If
            End If

            If Not risultato.HaData Then
                m = Regex.Match(lavoro, "\b(?:il|per\s+il)\s+(?<gg>\d{1,2}|primo)\b(?!\s*(?:ore|:))", OPZIONI)
                If m.Success Then
                    Dim giorno As Integer = NumeroDaTesto(m.Groups("gg").Value)
                    Dim data As DateTime
                    If CostruisciData(giorno, DateTime.Today.Month, DateTime.Today.Year, data) Then
                        If data < DateTime.Today Then data = data.AddMonths(1)
                        ImpostaData(risultato, data)
                        lavoro = Rimuovi(lavoro, m)
                    End If
                End If
            End If

            m = Regex.Match(lavoro, "\b(?<p>stamattina|stamani|stasera|stanotte)\b", OPZIONI)
            If m.Success Then
                If Not risultato.HaData Then ImpostaData(risultato, DateTime.Today)
                If risultato.Periodo = "" Then risultato.Periodo = m.Groups("p").Value.ToLowerInvariant().Replace("sta", "")
                lavoro = Rimuovi(lavoro, m)
            End If

            m = Regex.Match(lavoro, "\b(?:di|del|della|in|nel|nella)?\s*(?<p>mattin[ao]|mattinata|pomeriggio|serata|sera|notte)\b", OPZIONI)
            If m.Success Then
                If risultato.Periodo = "" OrElse risultato.Periodo = "mezzogiorno" Then risultato.Periodo = m.Groups("p").Value.ToLowerInvariant()
                lavoro = Rimuovi(lavoro, m)
            End If

            m = Regex.Match(lavoro, "\boggi\b", OPZIONI)
            If m.Success Then
                If Not risultato.HaData Then ImpostaData(risultato, DateTime.Today)
                lavoro = Rimuovi(lavoro, m)
            End If

            risultato.Resto = Regex.Replace(lavoro, "\s+", " ").Trim()
        Catch ex As Exception
            log.ScriviLog("InterpreteComandiVocali.EstraiDataOra: " & ex.Message & Environment.NewLine & ex.StackTrace)
            risultato.Resto = If(testo, "")
        End Try
        Return risultato
    End Function

    Private Sub ImpostaData(risultato As DataOraEstratta, data As DateTime)
        Try
            risultato.Data = data.Date
            risultato.HaData = True
        Catch ex As Exception
            log.ScriviLog("InterpreteComandiVocali.ImpostaData: " & ex.Message)
        End Try
    End Sub

    Private Function Rimuovi(testo As String, m As Match) As String
        Try
            Return testo.Remove(m.Index, m.Length).Insert(m.Index, " ")
        Catch ex As Exception
            log.ScriviLog("InterpreteComandiVocali.Rimuovi: " & ex.Message)
            Return testo
        End Try
    End Function

    Private Function LeggiOrario(m As Match, suffisso As String, ByRef ora As TimeSpan) As Boolean
        Try
            Dim ore As Integer = NumeroDaTesto(m.Groups("h" & suffisso).Value)
            If ore < 0 OrElse ore > 24 Then Return False
            Dim minuti As Integer = 0

            If m.Groups("m" & suffisso).Success Then
                minuti = CInt(m.Groups("m" & suffisso).Value)
            ElseIf m.Groups("mp" & suffisso).Success Then
                Dim parte As String = Regex.Replace(m.Groups("mp" & suffisso).Value.ToLowerInvariant(), "\s+", " ")
                Select Case parte
                    Case "mezza", "mezzo"
                        minuti = 30
                    Case "un quarto"
                        minuti = 15
                    Case "tre quarti"
                        minuti = 45
                    Case Else
                        minuti = NumeroDaTesto(parte)
                End Select
            ElseIf m.Groups("mq" & suffisso).Success Then
                ore -= 1
                If ore < 0 Then ore = 23
                minuti = 45
            End If

            If minuti < 0 OrElse minuti > 59 Then Return False
            If ore = 24 Then ore = 0
            ora = New TimeSpan(ore, minuti, 0)
            Return True
        Catch ex As Exception
            log.ScriviLog("InterpreteComandiVocali.LeggiOrario: " & ex.Message)
            Return False
        End Try
    End Function

    Private Function LeggiDurata(testo As String) As Integer
        Try
            Dim t As String = Regex.Replace(If(testo, "").ToLowerInvariant(), "\s+", " ").Trim()
            If t.StartsWith("mezz") Then Return 30
            Dim minuti As Integer = 0
            Dim m As Match = Regex.Match(t, "^(?<n>\d{1,2}|" & _paroleNumeri & ")\s+ore", OPZIONI)
            If m.Success Then
                minuti = NumeroDaTesto(m.Groups("n").Value) * 60
            Else
                minuti = 60
            End If
            If t.Contains("e mezza") Then minuti += 30
            If minuti <= 0 Then minuti = 60
            Return minuti
        Catch ex As Exception
            log.ScriviLog("InterpreteComandiVocali.LeggiDurata: " & ex.Message)
            Return 60
        End Try
    End Function

    Private Function NumeroDaTesto(testo As String) As Integer
        Try
            Dim t As String = If(testo, "").Trim().ToLowerInvariant()
            If t = "" Then Return -1
            Dim valore As Integer
            If Integer.TryParse(t, valore) Then Return valore
            If _numeri.TryGetValue(t, valore) Then Return valore
            Return -1
        Catch ex As Exception
            log.ScriviLog("InterpreteComandiVocali.NumeroDaTesto: " & ex.Message)
            Return -1
        End Try
    End Function

    Private Function NumeroMese(testo As String) As Integer
        Try
            Dim mesi As String() = {"gennaio", "febbraio", "marzo", "aprile", "maggio", "giugno", "luglio", "agosto", "settembre", "ottobre", "novembre", "dicembre"}
            Dim indice As Integer = Array.IndexOf(mesi, If(testo, "").Trim().ToLowerInvariant())
            Return indice + 1
        Catch ex As Exception
            log.ScriviLog("InterpreteComandiVocali.NumeroMese: " & ex.Message)
            Return 0
        End Try
    End Function

    Private Function CostruisciData(giorno As Integer, mese As Integer, anno As Integer, ByRef data As DateTime) As Boolean
        Try
            If giorno < 1 OrElse giorno > 31 OrElse mese < 1 OrElse mese > 12 Then Return False
            Dim annoEffettivo As Integer = If(anno > 0, anno, DateTime.Today.Year)
            If giorno > DateTime.DaysInMonth(annoEffettivo, mese) Then Return False
            data = New DateTime(annoEffettivo, mese, giorno)
            If anno <= 0 AndAlso data < DateTime.Today Then
                If giorno > DateTime.DaysInMonth(annoEffettivo + 1, mese) Then Return False
                data = New DateTime(annoEffettivo + 1, mese, giorno)
            End If
            Return True
        Catch ex As Exception
            log.ScriviLog("InterpreteComandiVocali.CostruisciData: " & ex.Message)
            Return False
        End Try
    End Function

    Private Function GiornoSettimanaDaTesto(testo As String) As Integer
        Try
            Dim t As String = If(testo, "").Trim().ToLowerInvariant().Replace("ì", "i")
            Select Case t
                Case "lunedi" : Return DayOfWeek.Monday
                Case "martedi" : Return DayOfWeek.Tuesday
                Case "mercoledi" : Return DayOfWeek.Wednesday
                Case "giovedi" : Return DayOfWeek.Thursday
                Case "venerdi" : Return DayOfWeek.Friday
                Case "sabato" : Return DayOfWeek.Saturday
                Case "domenica" : Return DayOfWeek.Sunday
            End Select
            Return -1
        Catch ex As Exception
            log.ScriviLog("InterpreteComandiVocali.GiornoSettimanaDaTesto: " & ex.Message)
            Return -1
        End Try
    End Function

    Private Function ProssimoGiornoSettimana(testo As String, ammettiOggi As Boolean) As DateTime
        Try
            Dim obiettivo As Integer = GiornoSettimanaDaTesto(testo)
            If obiettivo < 0 Then Return DateTime.Today
            Dim differenza As Integer = (obiettivo - CInt(DateTime.Today.DayOfWeek) + 7) Mod 7
            If differenza = 0 AndAlso Not ammettiOggi Then differenza = 7
            Return DateTime.Today.AddDays(differenza)
        Catch ex As Exception
            log.ScriviLog("InterpreteComandiVocali.ProssimoGiornoSettimana: " & ex.Message)
            Return DateTime.Today
        End Try
    End Function

    Private Function PulisciDescrizione(testo As String) As String
        Try
            Dim d As String = If(testo, "")
            d = Regex.Replace(d, "\b(?:in|nell'|nella|sull'|sulla)\s*agenda\b", " ", OPZIONI)
            d = Regex.Replace(d, "\s+", " ").Trim()
            For passaggio As Integer = 1 To 3
                d = Regex.Replace(d, "^[\s,.:;-]*(?:(?:di|per|che|un|una|uno|il|lo|la|le|i|gli|a|ad|e)\s+|l'\s*|un'\s*)", "", OPZIONI)
                d = Regex.Replace(d, "(?:\s+(?:per|di|a|ad|il|la|alle|dalle|e|con))+[\s,.:;-]*$", "", OPZIONI)
                d = d.Trim(" "c, ","c, "."c, ":"c, ";"c, "-"c)
            Next
            If d = "" Then Return "Appuntamento"
            Return Maiuscola(d)
        Catch ex As Exception
            log.ScriviLog("InterpreteComandiVocali.PulisciDescrizione: " & ex.Message)
            Return "Appuntamento"
        End Try
    End Function

    Private Function ProvaMeteo(frase As String, comando As ComandoVocale) As Boolean
        Try
            Dim modelloTrigger As String = "\b(?:che\s+tempo\s+(?:fa|far[aà]|ci\s+sar[aà]|c'[eè]|ha\s+fatto)|com'[eè]\s+il\s+tempo|come\s+sar[aà]\s+il\s+tempo|il\s+meteo|meteo|previsioni(?:\s+del\s+tempo|\s+meteo)?|piove|piover[aà]|nevica|nevicher[aà]|quanti\s+gradi|che\s+temperatura|la\s+temperatura|temperatura|fa\s+(?:caldo|freddo)|far[aà]\s+(?:caldo|freddo)|serve\s+l'ombrello|servir[aà]\s+l'ombrello)\b"
            Dim m As Match = Regex.Match(frase, modelloTrigger, OPZIONI)
            If Not m.Success Then Return False

            comando.Tipo = TipoComandoVocale.Meteo
            comando.GiorniDaOggi = 0

            Dim lavoro As String = " " & Rimuovi(frase, m) & " "
            Dim g As Match = Regex.Match(lavoro, "\bdopodomani\b", OPZIONI)
            If g.Success Then
                comando.GiorniDaOggi = 2
                lavoro = Rimuovi(lavoro, g)
            Else
                g = Regex.Match(lavoro, "\bdomani\b", OPZIONI)
                If g.Success Then
                    comando.GiorniDaOggi = 1
                    lavoro = Rimuovi(lavoro, g)
                Else
                    g = Regex.Match(lavoro, "\b(?:(?:il|la|di|per)\s+)?(?:prossim[oa]\s+)?(?<g>" & GIORNI_SETTIMANA & ")(?:\s+prossim[oa])?\b", OPZIONI)
                    If g.Success Then
                        Dim data As DateTime = ProssimoGiornoSettimana(g.Groups("g").Value, True)
                        comando.GiorniDaOggi = CInt((data - DateTime.Today).TotalDays)
                        lavoro = Rimuovi(lavoro, g)
                    End If
                End If
            End If
            If comando.GiorniDaOggi > 6 Then comando.GiorniDaOggi = 6

            lavoro = Regex.Replace(lavoro, "\b(?:oggi|adesso|ora|in\s+questo\s+momento|stamattina|stasera|stanotte|questa\s+(?:mattina|sera|notte|settimana)|nel\s+pomeriggio|fuori|qui|qua|da\s+noi|ci\s+sono|ci\s+sar[aà]|c'[eè]|sar[aà]|far[aà]|fa|[eè]|e'|dimmi|mi\s+dici|sai|com'[eè]|come)\b", " ", OPZIONI)
            lavoro = Regex.Replace(lavoro, "\s+", " ").Trim(" "c, "?"c, "."c, ","c, "!"c)
            For passaggio As Integer = 1 To 3
                lavoro = Regex.Replace(lavoro, "^(?:a|ad|in|su|per|di|del|della|dello|nel|nella|sul|sulla|il|la|lo|l')\s*", "", OPZIONI).Trim()
            Next
            lavoro = Regex.Replace(lavoro, "(?:\s+(?:oggi|domani|per|di|a))+$", "", OPZIONI).Trim()

            If Regex.IsMatch(lavoro, "^[\p{L}' -]{2,40}$") Then
                comando.Citta = Maiuscola(lavoro)
            Else
                comando.Citta = ""
            End If
            Return True
        Catch ex As Exception
            log.ScriviLog("InterpreteComandiVocali.ProvaMeteo: " & ex.Message & Environment.NewLine & ex.StackTrace)
            Return False
        End Try
    End Function

    Private Function ProvaVideo(frase As String, comando As ComandoVocale) As Boolean
        Try
            Dim testo As String = Regex.Replace(If(frase, ""), "[,;:]", " ")
            testo = Regex.Replace(testo, "\s+", " ").Trim()
            If testo = "" Then Return False

            Dim haYoutube As Boolean = Regex.IsMatch(testo, "\b" & YOUTUBE & "\b", OPZIONI)
            Dim haOggetto As Boolean = Regex.IsMatch(testo, "\b" & OGGETTI_VIDEO & "\b", OPZIONI)
            If Not haYoutube AndAlso Not haOggetto Then Return False

            Dim ricerca As Match = Regex.Match(testo, "\b" & VERBI_RICERCA_VIDEO & "\b", OPZIONI)
            Dim riproduzione As Match = Regex.Match(testo, "\b" & VERBI_RIPRODUZIONE_VIDEO & "\b", OPZIONI)
            Dim elenco As Match = Regex.Match(testo, "\b" & ELENCO_VIDEO & "\b", OPZIONI)
            If Not ricerca.Success AndAlso Not riproduzione.Success AndAlso Not elenco.Success AndAlso Not haYoutube Then Return False
            If Regex.IsMatch(testo, "^(?:apri|aprire|apriamo)\s+(?:(?:il|l')\s*)?(?:programma\s+|sezione\s+)?(?:di\s+)?" & YOUTUBE & "$", OPZIONI) Then Return False

            Dim cerca As Boolean = False
            If elenco.Success Then
                cerca = True
            ElseIf ricerca.Success AndAlso (Not riproduzione.Success OrElse ricerca.Index < riproduzione.Index) Then
                cerca = True
            End If
            If cerca AndAlso Regex.IsMatch(testo, "\be\s+(?:poi\s+)?" & SEGUITO_RIPRODUZIONE & "\b", OPZIONI) Then cerca = False

            comando.Tipo = If(cerca, TipoComandoVocale.CercaVideo, TipoComandoVocale.RiproduciVideo)
            comando.Argomento = EstraiTitoloVideo(testo)
            Return True
        Catch ex As Exception
            log.ScriviLog("InterpreteComandiVocali.ProvaVideo: " & ex.Message & Environment.NewLine & ex.StackTrace)
            Return False
        End Try
    End Function

    Private Function EstraiTitoloVideo(testo As String) As String
        Try
            Dim titolo As String = If(testo, "")
            For passaggio As Integer = 1 To 3
                titolo = Regex.Replace(titolo, "^(?:(?:" & VERBI_RICERCA_VIDEO & "|" & VERBI_RIPRODUZIONE_VIDEO & "|" & ELENCO_VIDEO & "|e|ed|poi|anche|subito|adesso|ora|per\s+me|me|mi|ti\s+prego|voglio|vorrei|ci\s+sono|c'[eè])\b[\s']*)+", "", OPZIONI).Trim()
                titolo = Regex.Replace(titolo, "\s*\b(?:su|da|con|in|di)\s+" & YOUTUBE & "\b", "", OPZIONI).Trim()
                titolo = Regex.Replace(titolo, "^" & YOUTUBE & "\b\s*", "", OPZIONI).Trim()
                titolo = Regex.Replace(titolo, "^(?:(?:il|lo|la|l'|i|gli|le|un|uno|una|un'|dei|degli|delle|del|qualche|alcuni|alcune)\s*)?" & OGGETTI_VIDEO & "\b\s*(?:(?:di|del|della|dello|dei|delle|degli|dell'|d'|intitolat[oaie]|chiamat[oaie]|sul|sulla|sui|sugli|sulle|su|con|riguardo\s+a|che\s+parla(?:no)?\s+di)\b\s*)?", "", OPZIONI).Trim()
                titolo = Regex.Replace(titolo, "\s+e\s+(?:poi\s+)?" & SEGUITO_RIPRODUZIONE & "\b.*$", "", OPZIONI).Trim()
                titolo = Regex.Replace(titolo, "^(?:ci\s+sono\s+|c'[eè]\s+)?(?:di|del|della|dello|dei|degli|delle|dell'|su|sul|sulla|sui|per|riguardo\s+a)\s+", "", OPZIONI).Trim()
            Next
            Return PulisciBordi(titolo)
        Catch ex As Exception
            log.ScriviLog("InterpreteComandiVocali.EstraiTitoloVideo: " & ex.Message & Environment.NewLine & ex.StackTrace)
            Return PulisciBordi(testo)
        End Try
    End Function

    Private Function ProvaRadio(frase As String, comando As ComandoVocale) As Boolean
        Try
            Dim m As Match = Regex.Match(frase, "^" & VERBI_RIPRODUZIONE & "\s+(?:(?:la|una|il)\s+)?(?:stazione\s+(?:radio\s+)?|radio\b\s*)(?<n>.*)$", OPZIONI)
            If Not m.Success Then Return False

            comando.Tipo = TipoComandoVocale.AscoltaRadio
            comando.Argomento = PulisciBordi(m.Groups("n").Value)
            If comando.Argomento <> "" AndAlso Regex.IsMatch(frase, "\bradio\s+" & Regex.Escape(comando.Argomento) & "$", OPZIONI) AndAlso comando.Argomento.Split(" "c).Length <= 2 Then
                comando.Argomento = "Radio " & comando.Argomento
            End If
            Return True
        Catch ex As Exception
            log.ScriviLog("InterpreteComandiVocali.ProvaRadio: " & ex.Message & Environment.NewLine & ex.StackTrace)
            Return False
        End Try
    End Function

    Private Function ProvaOraData(frase As String, comando As ComandoVocale) As Boolean
        Try
            Dim chiedeOra As Boolean = Regex.IsMatch(frase, "\bche\s+or[ae]\s+(?:sono|[eè]|e'|abbiamo|si\s+sono\s+fatte|fanno)\b|\bche\s+or[ae]\s*\??$|\b(?:dimmi|dici|dirmi|sai|sapresti|qual\s+[eè]|quale\s+[eè])\b.*\b(?:l'ora|l'orario|che\s+or[ae])\b|^(?:l'ora|l'orario|ora\s+esatta|l'ora\s+esatta|orario|ora)(?:\s+esatta)?$|\bora\s+esatta\b", OPZIONI)
            Dim chiedeData As Boolean = Regex.IsMatch(frase, "\bche\s+giorno\s+(?:[eè]|e'|siamo|abbiamo|della\s+settimana)\b|\bche\s+data\s+(?:[eè]|e'|abbiamo)\b|\bquanti\s+ne\s+abbiamo\b|\b(?:dimmi|dici|dirmi|sai|sapresti|qual\s+[eè]|quale\s+[eè])\b.*\b(?:la\s+data|che\s+giorno)\b|^(?:la\s+)?data(?:\s+di\s+oggi)?$|\bdata\s+di\s+oggi\b|\boggi\s+che\s+giorno\b|\bche\s+giorno\s*\??$", OPZIONI)
            If Regex.IsMatch(frase, "\bdata\s+e\s+ora\b|\bora\s+e\s+data\b|\bl'ora\s+e\s+la\s+data\b|\bla\s+data\s+e\s+l'ora\b", OPZIONI) Then
                chiedeOra = True
                chiedeData = True
            End If
            If Not chiedeOra AndAlso Not chiedeData Then Return False
            comando.Tipo = TipoComandoVocale.OraData
            If chiedeOra AndAlso chiedeData Then
                comando.Argomento = ""
            ElseIf chiedeOra Then
                comando.Argomento = "ora"
            Else
                comando.Argomento = "data"
            End If
            Return True
        Catch ex As Exception
            log.ScriviLog("InterpreteComandiVocali.ProvaOraData: " & ex.Message & Environment.NewLine & ex.StackTrace)
            Return False
        End Try
    End Function

    Private Function ProvaApriSezione(frase As String, comando As ComandoVocale) As Boolean
        Try
            Dim m As Match = Regex.Match(frase, "^(?:apri|aprimi|aprire|vai\s+(?:a|al|alla|alle|ai|in|nella|nel|sulla|sul)|mostra(?:mi)?|portami\s+(?:a|al|alla|in|nella|nel))\s+(?:(?:la|il|l'|le|i|lo|gli)\s*)?(?<s>posta|e-?\s?mail|mail|email|agenda|rubrica|contatti|meteo|radio|" & YOUTUBE & "|intelligenza\s+artificiale|calendario)\b", OPZIONI)
            If Not m.Success Then Return False

            Dim sezione As String = m.Groups("s").Value.ToLowerInvariant().Replace(" ", "").Replace("-", "")
            If sezione = "email" OrElse sezione = "mail" Then sezione = "posta"
            If sezione = "contatti" Then sezione = "rubrica"
            If sezione = "intelligenzaartificiale" Then sezione = "ai"
            If sezione.Contains("tub") Then sezione = "youtube"

            comando.Tipo = TipoComandoVocale.ApriSezione
            comando.Argomento = sezione
            Return True
        Catch ex As Exception
            log.ScriviLog("InterpreteComandiVocali.ProvaApriSezione: " & ex.Message & Environment.NewLine & ex.StackTrace)
            Return False
        End Try
    End Function

    Private Shared ReadOnly _comandiBrevi As Tuple(Of String, TipoComandoVocale, String)() = {
        New Tuple(Of String, TipoComandoVocale, String)("che ore sono", TipoComandoVocale.OraData, "ora"),
        New Tuple(Of String, TipoComandoVocale, String)("che ora è", TipoComandoVocale.OraData, "ora"),
        New Tuple(Of String, TipoComandoVocale, String)("dimmi l'ora", TipoComandoVocale.OraData, "ora"),
        New Tuple(Of String, TipoComandoVocale, String)("dimmi che ore sono", TipoComandoVocale.OraData, "ora"),
        New Tuple(Of String, TipoComandoVocale, String)("sai che ore sono", TipoComandoVocale.OraData, "ora"),
        New Tuple(Of String, TipoComandoVocale, String)("che giorno è", TipoComandoVocale.OraData, "data"),
        New Tuple(Of String, TipoComandoVocale, String)("che giorno è oggi", TipoComandoVocale.OraData, "data"),
        New Tuple(Of String, TipoComandoVocale, String)("che giorno siamo", TipoComandoVocale.OraData, "data"),
        New Tuple(Of String, TipoComandoVocale, String)("dimmi la data", TipoComandoVocale.OraData, "data"),
        New Tuple(Of String, TipoComandoVocale, String)("che tempo fa", TipoComandoVocale.Meteo, ""),
        New Tuple(Of String, TipoComandoVocale, String)("che tempo fa oggi", TipoComandoVocale.Meteo, ""),
        New Tuple(Of String, TipoComandoVocale, String)("com'è il tempo", TipoComandoVocale.Meteo, ""),
        New Tuple(Of String, TipoComandoVocale, String)("leggi la posta", TipoComandoVocale.LeggiPosta, ""),
        New Tuple(Of String, TipoComandoVocale, String)("leggi le mail", TipoComandoVocale.LeggiPosta, ""),
        New Tuple(Of String, TipoComandoVocale, String)("metti la radio", TipoComandoVocale.AscoltaRadio, ""),
        New Tuple(Of String, TipoComandoVocale, String)("accendi la radio", TipoComandoVocale.AscoltaRadio, ""),
        New Tuple(Of String, TipoComandoVocale, String)("apri youtube", TipoComandoVocale.ApriSezione, "youtube"),
        New Tuple(Of String, TipoComandoVocale, String)("apri la posta", TipoComandoVocale.ApriSezione, "posta"),
        New Tuple(Of String, TipoComandoVocale, String)("apri l'agenda", TipoComandoVocale.ApriSezione, "agenda"),
        New Tuple(Of String, TipoComandoVocale, String)("apri la rubrica", TipoComandoVocale.ApriSezione, "rubrica")
    }

    Private Shared Function LettereComando(testo As String) As String
        Try
            Return Regex.Replace(If(testo, "").ToLowerInvariant().Replace("è", "e").Replace("é", "e").Replace("ò", "o").Replace("à", "a").Replace("ì", "i").Replace("ù", "u"), "[^a-z0-9]", "")
        Catch ex As Exception
            log.ScriviLog("InterpreteComandiVocali.LettereComando: " & ex.Message)
            Return If(testo, "").ToLowerInvariant()
        End Try
    End Function

    Private Shared Function SomiglianzaComando(primo As String, secondo As String) As Double
        Try
            Dim a As String = LettereComando(primo)
            Dim b As String = LettereComando(secondo)
            If a.Length = 0 OrElse b.Length = 0 Then Return 0.0
            Dim precedente(b.Length) As Integer
            Dim corrente(b.Length) As Integer
            For j As Integer = 0 To b.Length
                precedente(j) = j
            Next
            For i As Integer = 1 To a.Length
                corrente(0) = i
                For j As Integer = 1 To b.Length
                    Dim costo As Integer = If(a(i - 1) = b(j - 1), 0, 1)
                    corrente(j) = Math.Min(Math.Min(corrente(j - 1) + 1, precedente(j) + 1), precedente(j - 1) + costo)
                Next
                Dim scambio As Integer() = precedente
                precedente = corrente
                corrente = scambio
            Next
            Return 1.0 - precedente(b.Length) / CDbl(Math.Max(a.Length, b.Length))
        Catch ex As Exception
            log.ScriviLog("InterpreteComandiVocali.SomiglianzaComando: " & ex.Message & Environment.NewLine & ex.StackTrace)
            Return 0.0
        End Try
    End Function

    Private Function ProvaComandoSomigliante(frase As String, comando As ComandoVocale) As Boolean
        Try
            Dim lettere As String = LettereComando(frase)
            If lettere.Length < 6 OrElse lettere.Length > 30 Then Return False
            Dim migliore As Tuple(Of String, TipoComandoVocale, String) = Nothing
            Dim punteggioMigliore As Double = 0.0
            For Each voce As Tuple(Of String, TipoComandoVocale, String) In _comandiBrevi
                Dim punteggio As Double = SomiglianzaComando(frase, voce.Item1)
                If punteggio > punteggioMigliore Then
                    punteggioMigliore = punteggio
                    migliore = voce
                End If
            Next
            If migliore Is Nothing OrElse punteggioMigliore < 0.8 Then Return False
            comando.Tipo = migliore.Item2
            comando.Argomento = migliore.Item3
            comando.GiorniDaOggi = 0
            Return True
        Catch ex As Exception
            log.ScriviLog("InterpreteComandiVocali.ProvaComandoSomigliante: " & ex.Message & Environment.NewLine & ex.StackTrace)
            Return False
        End Try
    End Function

    Private Function PulisciBordi(testo As String) As String
        Try
            Return Regex.Replace(If(testo, ""), "\s+", " ").Trim(" "c, ","c, "."c, ":"c, ";"c, "!"c, "?"c, "'"c, """"c)
        Catch ex As Exception
            log.ScriviLog("InterpreteComandiVocali.PulisciBordi: " & ex.Message)
            Return If(testo, "").Trim()
        End Try
    End Function

    Private Function Maiuscola(testo As String) As String
        Try
            If String.IsNullOrEmpty(testo) Then Return ""
            Return testo.Substring(0, 1).ToUpper(_cultura) & testo.Substring(1)
        Catch ex As Exception
            log.ScriviLog("InterpreteComandiVocali.Maiuscola: " & ex.Message)
            Return If(testo, "")
        End Try
    End Function

End Class
