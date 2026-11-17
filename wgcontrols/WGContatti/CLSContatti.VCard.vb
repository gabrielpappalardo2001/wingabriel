Imports System
Imports System.Collections.Generic
Imports System.Data
Imports System.IO
Imports System.Linq
Imports System.Text

Partial Public Class CLSContatti

    ' ============================================================================================
    ' ESPORTAZIONE
    ' ============================================================================================

    ' Scrive in un file .vcf i contatti della rubrica indicata, oppure tutti quelli leggibili
    ' se la rubrica non viene indicata. I contatti delle rubriche protette e ancora bloccate
    ' non escono, perche' il programma non li sa leggere.
    Public Function EsportaVCard(percorsoFile As String, Optional chiaveRubrica As String = "") As Integer
        Try
            If String.IsNullOrWhiteSpace(percorsoFile) Then
                _ultimoErrore = "Percorso del file non indicato."
                Return 0
            End If

            Dim contatti As DataTable = OttieniElenco("", "", chiaveRubrica)
            If contatti Is Nothing OrElse contatti.Rows.Count = 0 Then
                _ultimoErrore = "Non ci sono contatti da esportare."
                Return 0
            End If

            Dim testo As New StringBuilder()
            Dim scritti As Integer = 0
            For Each riga As DataRow In contatti.Rows
                Try
                    testo.Append(ComponiVCard(riga))
                    scritti += 1
                Catch exRiga As Exception
                    ScriviLog("CLSContatti.EsportaVCard riga: " & exRiga.Message)
                End Try
            Next

            Dim cartella As String = Path.GetDirectoryName(percorsoFile)
            If Not String.IsNullOrWhiteSpace(cartella) AndAlso Not Directory.Exists(cartella) Then Directory.CreateDirectory(cartella)
            File.WriteAllText(percorsoFile, testo.ToString(), New UTF8Encoding(False))
            Return scritti
        Catch ex As Exception
            ImpostaErrore("CLSContatti.EsportaVCard", ex)
            Return 0
        End Try
    End Function

    Private Function ComponiVCard(riga As DataRow) As String
        Dim testo As New StringBuilder()
        Try
            Dim nome As String = ValoreColonna(riga, "Nome")
            Dim cognome As String = ValoreColonna(riga, "Cognome")
            Dim nomeCompleto As String = (nome & " " & cognome).Trim()
            If nomeCompleto.Length = 0 Then nomeCompleto = ValoreColonna(riga, "Azienda")

            Dim chiaveContatto As String = ValoreColonna(riga, "ChiaveWG")
            Dim listaEmail As List(Of String) = OttieniRecapitiPerVCard(chiaveContatto, CategoriaRecapitoEmail, ValoreColonna(riga, "Email"))
            Dim listaTelefono As List(Of String) = OttieniRecapitiPerVCard(chiaveContatto, CategoriaRecapitoTelefono, ValoreColonna(riga, "Telefono"))
            Dim listaCellulare As List(Of String) = OttieniRecapitiPerVCard(chiaveContatto, CategoriaRecapitoCellulare, ValoreColonna(riga, "Cellulare"))
            Dim listaIndirizzo As List(Of String) = OttieniRecapiti(chiaveContatto, CategoriaRecapitoIndirizzo)
            Dim listaSito As List(Of String) = OttieniRecapiti(chiaveContatto, CategoriaRecapitoSito)

            testo.AppendLine("BEGIN:VCARD")
            testo.AppendLine("VERSION:3.0")
            testo.AppendLine("N:" & ProteggiVCard(cognome) & ";" & ProteggiVCard(nome) & ";;;")
            testo.AppendLine("FN:" & ProteggiVCard(nomeCompleto))

            Dim azienda As String = ValoreColonna(riga, "Azienda")
            If azienda.Length > 0 Then testo.AppendLine("ORG:" & ProteggiVCard(azienda))

            Dim rigaCompleanno As String = RigaVCardCompleanno(ValoreColonna(riga, "Compleanno"))
            If rigaCompleanno.Length > 0 Then testo.AppendLine(rigaCompleanno)

            For Each email As String In listaEmail
                testo.AppendLine("EMAIL;TYPE=INTERNET:" & ProteggiVCard(email))
            Next

            For Each telefono As String In listaTelefono
                testo.AppendLine("TEL;TYPE=VOICE:" & ProteggiVCard(telefono))
            Next

            For Each cellulare As String In listaCellulare
                testo.AppendLine("TEL;TYPE=CELL:" & ProteggiVCard(cellulare))
            Next

            For Each indirizzo As String In listaIndirizzo
                testo.AppendLine("ADR;TYPE=HOME:;;" & ProteggiVCard(indirizzo) & ";;;;")
            Next

            For Each sito As String In listaSito
                testo.AppendLine("URL:" & ProteggiVCard(sito))
            Next

            Dim note As String = ValoreColonna(riga, "Note")
            If note.Length > 0 Then testo.AppendLine("NOTE:" & ProteggiVCard(note))

            Dim uid As String = ValoreColonna(riga, "UidServer")
            If uid.Length = 0 Then uid = chiaveContatto
            If uid.Length > 0 Then testo.AppendLine("UID:" & ProteggiVCard(uid))

            testo.AppendLine("END:VCARD")
        Catch ex As Exception
            ScriviLog("CLSContatti.ComponiVCard: " & ex.ToString())
        End Try
        Return testo.ToString()
    End Function

    Private Function OttieniRecapitiPerVCard(chiaveContatto As String, categoria As String, valorePrincipale As String) As List(Of String)
        Try
            Dim lista As List(Of String) = OttieniRecapiti(chiaveContatto, categoria)
            If lista.Count = 0 AndAlso Not String.IsNullOrWhiteSpace(valorePrincipale) Then lista.Add(valorePrincipale)
            Return lista
        Catch ex As Exception
            ScriviLog("CLSContatti.OttieniRecapitiPerVCard (" & If(chiaveContatto, "") & "/" & If(categoria, "") & "): " & ex.ToString())
            Return New List(Of String)
        End Try
    End Function

    Private Function ProteggiVCard(valore As String) As String
        Try
            Dim testo As String = If(valore, "")
            testo = testo.Replace("\", "\\")
            testo = testo.Replace(";", "\;")
            testo = testo.Replace(",", "\,")
            testo = testo.Replace(vbCrLf, "\n")
            testo = testo.Replace(vbCr, "\n")
            testo = testo.Replace(vbLf, "\n")
            Return testo
        Catch ex As Exception
            ScriviLog("CLSContatti.ProteggiVCard: " & ex.Message)
            Return If(valore, "")
        End Try
    End Function

    ' ============================================================================================
    ' IMPORTAZIONE
    ' ============================================================================================

    ' Legge un file .vcf e aggiunge i contatti alla rubrica indicata. Restituisce il numero di
    ' contatti importati. Accetta sia il formato 3.0 sia il vecchio 2.1 con la codifica
    ' quoted printable, che molti telefoni usano ancora.
    Public Function ImportaVCard(percorsoFile As String, Optional chiaveRubrica As String = "") As Integer
        Try
            If String.IsNullOrWhiteSpace(percorsoFile) OrElse Not File.Exists(percorsoFile) Then
                _ultimoErrore = "File non trovato."
                Return 0
            End If
            If Not String.IsNullOrWhiteSpace(chiaveRubrica) AndAlso RubricaProtetta(chiaveRubrica) AndAlso Not RubricaSbloccata(chiaveRubrica) Then
                _ultimoErrore = "Sblocca la rubrica prima di importare."
                Return 0
            End If

            Dim contenuto As String = LeggiTestoVCard(percorsoFile)
            If String.IsNullOrWhiteSpace(contenuto) Then
                _ultimoErrore = "Il file è vuoto o illeggibile."
                Return 0
            End If

            Dim righe As List(Of String) = UnisciRigheVCard(contenuto)
            Dim importati As Integer = 0
            Dim dentro As Boolean = False
            Dim nome As String = ""
            Dim cognome As String = ""
            Dim nomeCompleto As String = ""
            Dim listaEmail As New List(Of String)
            Dim listaTelefono As New List(Of String)
            Dim listaCellulare As New List(Of String)
            Dim listaIndirizzo As New List(Of String)
            Dim listaSito As New List(Of String)
            Dim azienda As String = ""
            Dim note As String = ""
            Dim compleanno As String = ""

            For Each riga As String In righe
                Try
                    Dim testoRiga As String = riga.Trim()
                    If testoRiga.Length = 0 Then Continue For

                    If testoRiga.Equals("BEGIN:VCARD", StringComparison.OrdinalIgnoreCase) Then
                        dentro = True
                        nome = "" : cognome = "" : nomeCompleto = ""
                        listaEmail = New List(Of String)
                        listaTelefono = New List(Of String)
                        listaCellulare = New List(Of String)
                        listaIndirizzo = New List(Of String)
                        listaSito = New List(Of String)
                        azienda = "" : note = "" : compleanno = ""
                        Continue For
                    End If

                    If testoRiga.Equals("END:VCARD", StringComparison.OrdinalIgnoreCase) Then
                        If dentro Then
                            If nome.Length = 0 AndAlso cognome.Length = 0 AndAlso nomeCompleto.Length > 0 Then
                                Dim parti As String() = nomeCompleto.Split(" "c)
                                nome = parti(0)
                                If parti.Length > 1 Then cognome = String.Join(" ", parti, 1, parti.Length - 1)
                            End If
                            If nome.Length > 0 OrElse cognome.Length > 0 OrElse listaEmail.Count > 0 Then
                                Dim creata As String = AggiungiContatto(nome, cognome, listaEmail, listaTelefono, listaCellulare, listaIndirizzo, listaSito, azienda, "", note, False, "", chiaveRubrica, compleanno)
                                If Not String.IsNullOrWhiteSpace(creata) Then importati += 1
                            End If
                        End If
                        dentro = False
                        Continue For
                    End If

                    If Not dentro Then Continue For

                    Dim separatore As Integer = testoRiga.IndexOf(":"c)
                    If separatore <= 0 Then Continue For
                    Dim intestazione As String = testoRiga.Substring(0, separatore)
                    Dim valore As String = testoRiga.Substring(separatore + 1)
                    valore = DecodificaValore(intestazione, valore)

                    Dim proprieta As String = intestazione
                    Dim puntoEVirgola As Integer = intestazione.IndexOf(";"c)
                    If puntoEVirgola > 0 Then proprieta = intestazione.Substring(0, puntoEVirgola)
                    Dim duePunti As Integer = proprieta.LastIndexOf("."c)
                    If duePunti >= 0 Then proprieta = proprieta.Substring(duePunti + 1)
                    proprieta = proprieta.Trim().ToUpperInvariant()

                    Select Case proprieta
                        Case "N"
                            Dim parti As String() = SpezzaValoreVCard(valore)
                            If parti.Length > 0 Then cognome = parti(0)
                            If parti.Length > 1 Then nome = parti(1)
                        Case "FN"
                            nomeCompleto = LiberaVCard(valore)
                        Case "ORG"
                            Dim parti As String() = SpezzaValoreVCard(valore)
                            If parti.Length > 0 Then azienda = parti(0)
                        Case "EMAIL"
                            Dim indirizzoEmail As String = LiberaVCard(valore)
                            If indirizzoEmail.Length > 0 AndAlso Not listaEmail.Contains(indirizzoEmail, StringComparer.OrdinalIgnoreCase) Then listaEmail.Add(indirizzoEmail)
                        Case "TEL"
                            Dim etichetta As String = intestazione.ToUpperInvariant()
                            Dim numero As String = LiberaVCard(valore)
                            If numero.Length > 0 Then
                                If etichetta.Contains("CELL") OrElse etichetta.Contains("MOBILE") Then
                                    If Not listaCellulare.Contains(numero, StringComparer.OrdinalIgnoreCase) Then listaCellulare.Add(numero)
                                Else
                                    If Not listaTelefono.Contains(numero, StringComparer.OrdinalIgnoreCase) Then listaTelefono.Add(numero)
                                End If
                            End If
                        Case "ADR"
                            Dim componenti As String() = SpezzaValoreVCard(valore)
                            Dim parti As New List(Of String)
                            For Each componente As String In componenti
                                If Not String.IsNullOrWhiteSpace(componente) Then parti.Add(componente.Trim())
                            Next
                            If parti.Count > 0 Then
                                Dim indirizzoCompleto As String = String.Join(", ", parti)
                                If Not listaIndirizzo.Contains(indirizzoCompleto, StringComparer.OrdinalIgnoreCase) Then listaIndirizzo.Add(indirizzoCompleto)
                            End If
                        Case "URL"
                            Dim indirizzoSito As String = LiberaVCard(valore)
                            If indirizzoSito.Length > 0 AndAlso Not listaSito.Contains(indirizzoSito, StringComparer.OrdinalIgnoreCase) Then listaSito.Add(indirizzoSito)
                        Case "NOTE"
                            If note.Length = 0 Then note = LiberaVCard(valore)
                        Case "BDAY"
                            Dim data As String = NormalizzaCompleanno(LiberaVCard(valore))
                            If data IsNot Nothing Then compleanno = data
                    End Select
                Catch exRiga As Exception
                    ScriviLog("CLSContatti.ImportaVCard riga: " & exRiga.Message)
                End Try
            Next

            Return importati
        Catch ex As Exception
            ImpostaErrore("CLSContatti.ImportaVCard", ex)
            Return 0
        End Try
    End Function

    Private Function LeggiTestoVCard(percorsoFile As String) As String
        Try
            Dim dati As Byte() = File.ReadAllBytes(percorsoFile)
            If dati.Length >= 3 AndAlso dati(0) = &HEF AndAlso dati(1) = &HBB AndAlso dati(2) = &HBF Then
                Return New UTF8Encoding(False).GetString(dati, 3, dati.Length - 3)
            End If
            Dim testo As String = Encoding.UTF8.GetString(dati)
            If testo.Contains(ChrW(&HFFFD)) Then Return Encoding.Default.GetString(dati)
            Return testo
        Catch ex As Exception
            ScriviLog("CLSContatti.LeggiTestoVCard: " & ex.ToString())
            Return ""
        End Try
    End Function

    ' Nelle vCard una riga lunga viene spezzata e le continuazioni cominciano con uno spazio
    ' o una tabulazione. Qui vengono riunite prima di analizzarle.
    Private Function UnisciRigheVCard(contenuto As String) As List(Of String)
        Dim risultato As New List(Of String)
        Try
            Dim righe As String() = contenuto.Replace(vbCrLf, vbLf).Replace(vbCr, vbLf).Split(ChrW(10))
            Dim corrente As New StringBuilder()
            For Each riga As String In righe
                If riga.StartsWith(" ") OrElse riga.StartsWith(vbTab) Then
                    corrente.Append(riga.Substring(1))
                    Continue For
                End If
                If corrente.Length > 0 Then risultato.Add(corrente.ToString())
                corrente = New StringBuilder(riga)
            Next
            If corrente.Length > 0 Then risultato.Add(corrente.ToString())
        Catch ex As Exception
            ScriviLog("CLSContatti.UnisciRigheVCard: " & ex.ToString())
        End Try
        Return risultato
    End Function

    Private Function DecodificaValore(intestazione As String, valore As String) As String
        Try
            Dim etichetta As String = If(intestazione, "").ToUpperInvariant()
            If Not etichetta.Contains("QUOTED-PRINTABLE") Then Return valore

            Dim byteLetti As New List(Of Byte)
            Dim indice As Integer = 0
            While indice < valore.Length
                Dim carattere As Char = valore(indice)
                If carattere = "="c AndAlso indice + 2 < valore.Length Then
                    Dim coppia As String = valore.Substring(indice + 1, 2)
                    Dim numero As Integer = 0
                    If Integer.TryParse(coppia, Globalization.NumberStyles.HexNumber, Globalization.CultureInfo.InvariantCulture, numero) Then
                        byteLetti.Add(CByte(numero))
                        indice += 3
                        Continue While
                    End If
                End If
                byteLetti.AddRange(Encoding.UTF8.GetBytes(carattere.ToString()))
                indice += 1
            End While

            Dim codifica As Encoding = Encoding.UTF8
            If etichetta.Contains("CHARSET=ISO-8859-1") OrElse etichetta.Contains("CHARSET=WINDOWS-1252") Then codifica = Encoding.GetEncoding(1252)
            Return codifica.GetString(byteLetti.ToArray())
        Catch ex As Exception
            ScriviLog("CLSContatti.DecodificaValore: " & ex.Message)
            Return valore
        End Try
    End Function

    ' Divide un valore sui punti e virgola non protetti dalla barra rovesciata.
    Private Function SpezzaValoreVCard(valore As String) As String()
        Dim parti As New List(Of String)
        Try
            Dim corrente As New StringBuilder()
            Dim indice As Integer = 0
            While indice < valore.Length
                Dim carattere As Char = valore(indice)
                If carattere = "\"c AndAlso indice + 1 < valore.Length Then
                    corrente.Append(carattere)
                    corrente.Append(valore(indice + 1))
                    indice += 2
                    Continue While
                End If
                If carattere = ";"c Then
                    parti.Add(LiberaVCard(corrente.ToString()))
                    corrente = New StringBuilder()
                    indice += 1
                    Continue While
                End If
                corrente.Append(carattere)
                indice += 1
            End While
            parti.Add(LiberaVCard(corrente.ToString()))
        Catch ex As Exception
            ScriviLog("CLSContatti.SpezzaValoreVCard: " & ex.ToString())
        End Try
        Return parti.ToArray()
    End Function

    Private Function LiberaVCard(valore As String) As String
        Try
            Dim testo As String = If(valore, "")
            testo = testo.Replace("\n", vbCrLf).Replace("\N", vbCrLf)
            testo = testo.Replace("\,", ",")
            testo = testo.Replace("\;", ";")
            testo = testo.Replace("\\", "\")
            Return testo.Trim()
        Catch ex As Exception
            ScriviLog("CLSContatti.LiberaVCard: " & ex.Message)
            Return If(valore, "")
        End Try
    End Function

End Class