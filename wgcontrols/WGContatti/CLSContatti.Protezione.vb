Imports System
Imports System.Collections.Generic
Imports System.Data
Imports System.Globalization
Imports System.IO
Imports System.Security.Cryptography
Imports System.Text
Imports WGDb

' Protezione e cifratura delle rubriche con password e gestione delle coppie di chiavi crittografiche per i dati personali.
Partial Public Class CLSContatti

    ' I campi personali dei contatti di una rubrica protetta vengono cifrati nel database. La
    ' chiave si ricava dalla password e resta soltanto in memoria: chiudendo la rubrica sparisce
    ' e i contatti tornano illeggibili anche per il programma.
    Private Shared ReadOnly CampiRiservati As String() = {"Nome", "Cognome", "Email", "Telefono", "Cellulare", "Azienda", "Note", "Compleanno"}
    Private Const PrefissoCifraturaIbrida As String = "WGRSA1:"
    ' WGRSA2: chiave casuale cifrata con RSA e dati cifrati con AES-GCM della libreria nativa (WGRSA1 resta solo in lettura)
    Private Const PrefissoCifraturaIbridaGcm As String = "WGRSA2:"

    Private ReadOnly _rubricheSbloccate As New Dictionary(Of String, Byte())(StringComparer.OrdinalIgnoreCase)
    Private ReadOnly _chiaviPrivateRubriche As New Dictionary(Of String, RSA)(StringComparer.OrdinalIgnoreCase)
    Private ReadOnly _lockProtezione As New Object()

    ' ============================================================================================
    ' STATO DELLE RUBRICHE PROTETTE
    ' ============================================================================================

    Private Function RigaRubrica(chiaveRubrica As String) As DataRow
        Try
            If String.IsNullOrWhiteSpace(chiaveRubrica) Then Return Nothing
            Return _db.OttieniRecord(NomeTabellaRubriche, chiaveRubrica.Trim())
        Catch ex As Exception
            ScriviLog("CLSContatti.RigaRubrica (" & If(chiaveRubrica, "") & "): " & ex.ToString())
            Return Nothing
        End Try
    End Function

    Public Function RubricaProtetta(chiaveRubrica As String) As Boolean
        Try
            Dim riga As DataRow = RigaRubrica(chiaveRubrica)
            If riga Is Nothing Then Return False
            Return ValoreColonna(riga, "ImprontaPassword").Length > 0
        Catch ex As Exception
            ScriviLog("CLSContatti.RubricaProtetta: " & ex.ToString())
            Return False
        End Try
    End Function

    Public Function RubricaSbloccata(chiaveRubrica As String) As Boolean
        Try
            If String.IsNullOrWhiteSpace(chiaveRubrica) Then Return True
            If Not RubricaProtetta(chiaveRubrica) Then Return True
            SyncLock _lockProtezione
                Return _rubricheSbloccate.ContainsKey(chiaveRubrica.Trim())
            End SyncLock
        Catch ex As Exception
            ScriviLog("CLSContatti.RubricaSbloccata: " & ex.ToString())
            Return False
        End Try
    End Function

    Public Function EsistonoRubricheProtette() As Boolean
        Try
            Dim rubriche As DataTable = OttieniRubriche()
            If rubriche Is Nothing Then Return False
            For Each riga As DataRow In rubriche.Rows
                If ValoreColonna(riga, "ImprontaPassword").Length > 0 Then Return True
            Next
            Return False
        Catch ex As Exception
            ScriviLog("CLSContatti.EsistonoRubricheProtette: " & ex.ToString())
            Return False
        End Try
    End Function

    Public Function SbloccaRubrica(chiaveRubrica As String, password As String) As Boolean
        Try
            Dim riga As DataRow = RigaRubrica(chiaveRubrica)
            If riga Is Nothing Then Return False
            Dim sale As String = ValoreColonna(riga, "SalePassword")
            Dim impronta As String = ValoreColonna(riga, "ImprontaPassword")
            If impronta.Length = 0 Then Return True
            If Not WGCifratura.PasswordCorretta(password, sale, impronta) Then Return False

            Dim chiave As Byte() = WGCifratura.DerivaChiave(password, sale)
            If chiave Is Nothing Then Return False
            SyncLock _lockProtezione
                _rubricheSbloccate(chiaveRubrica.Trim()) = chiave
            End SyncLock

            If Not CaricaOGeneraChiavePrivataRubrica(chiaveRubrica, chiave) Then
                BloccaRubrica(chiaveRubrica)
                _ultimoErrore = "Non è stato possibile caricare la chiave della rubrica."
                Return False
            End If
            RegistraIstanzaAperta()
            Return True
        Catch ex As Exception
            ImpostaErrore("CLSContatti.SbloccaRubrica", ex)
            Return False
        End Try
    End Function

    ' Dopo l'introduzione della cifratura a coppia di chiavi, una rubrica protetta creata prima
    ' potrebbe non avere ancora la sua coppia: viene generata al volo al primo sblocco. Da quel
    ' momento la sincronizzazione automatica potra' scrivere in quella rubrica anche da bloccata,
    ' perche' per cifrare basta la chiave pubblica, sempre disponibile una volta generata.
    Private Function CaricaOGeneraChiavePrivataRubrica(chiaveRubrica As String, chiaveSimmetrica As Byte()) As Boolean
        Try
            Dim riga As DataRow = RigaRubrica(chiaveRubrica)
            If riga Is Nothing Then Return False
            Dim pubblicaBase64 As String = ValoreColonna(riga, "ChiavePubblica")
            Dim privataCifrata As String = ValoreColonna(riga, "ChiavePrivataCifrata")

            If String.IsNullOrWhiteSpace(pubblicaBase64) OrElse String.IsNullOrWhiteSpace(privataCifrata) Then
                ' Rubrica creata prima della cifratura a chiavi: resta utilizzabile con la sola
                ' chiave simmetrica anche se la generazione della coppia non riesce (e' loggata).
                GeneraEImpostaCoppiaChiaviRubrica(chiaveRubrica, chiaveSimmetrica)
                Return True
            End If

            ' Le chiavi esistono: se non si riesce a caricarle lo sblocco NON deve riuscire,
            ' altrimenti i contatti si vedrebbero vuoti e un salvataggio li azzererebbe.
            Dim privataBase64 As String = WGCifratura.Decifra(privataCifrata, chiaveSimmetrica)
            If String.IsNullOrWhiteSpace(privataBase64) Then
                ScriviLog("CLSContatti.CaricaOGeneraChiavePrivataRubrica: chiave privata non decifrabile (" & chiaveRubrica & ")")
                Return False
            End If

            Dim rsaMemoria As RSA = ImportaChiavePrivata(Convert.FromBase64String(privataBase64))
            SyncLock _lockProtezione
                _chiaviPrivateRubriche(chiaveRubrica.Trim()) = rsaMemoria
            End SyncLock
            Return True
        Catch ex As Exception
            ScriviLog("CLSContatti.CaricaOGeneraChiavePrivataRubrica: " & ex.ToString())
            Return False
        End Try
    End Function

    ' Genera una nuova coppia di chiavi RSA per la rubrica: la pubblica resta in chiaro nel
    ' database (cifrare con quella non richiede la password), la privata viene cifrata con la
    ' chiave simmetrica derivata dalla password e torna leggibile solo sbloccando la rubrica.
    Private Function GeneraEImpostaCoppiaChiaviRubrica(chiaveRubrica As String, chiaveSimmetrica As Byte()) As Boolean
        Try
            ' RSA.Create(2048) puo' restituire, a seconda del provider crittografico configurato
            ' su Windows, un RSACryptoServiceProvider basato su CAPI: quel provider supporta solo
            ' il padding OAEP con SHA-1 e lancia CryptographicException ("La modalita' di
            ' riempimento specificata non e' valida per questo algoritmo") appena si prova a
            ' cifrare/decifrare con OaepSHA256. RSACng usa invece CNG, che supporta OAEP-SHA256
            ' senza problemi ed e' quindi la scelta esplicita da usare qui.
            Using rsaNuova As RSA = New RSACng(2048)
                Dim pubblicaBase64 As String = Convert.ToBase64String(EsportaChiavePubblica(rsaNuova))
                Dim privataBase64 As String = Convert.ToBase64String(EsportaChiavePrivata(rsaNuova))
                Dim privataCifrata As String = WGCifratura.Cifra(privataBase64, chiaveSimmetrica)

                If Not _db.AggiornaRecord(NomeTabellaRubriche, chiaveRubrica.Trim(), New Dictionary(Of String, Object) From {
                    {"ChiavePubblica", pubblicaBase64},
                    {"ChiavePrivataCifrata", privataCifrata}
                }) Then
                    ScriviLog("CLSContatti.GeneraEImpostaCoppiaChiaviRubrica: salvataggio delle chiavi non riuscito (" & chiaveRubrica & ")")
                    Return False
                End If

                Dim rsaMemoria As RSA = ImportaChiavePrivata(EsportaChiavePrivata(rsaNuova))
                SyncLock _lockProtezione
                    _chiaviPrivateRubriche(chiaveRubrica.Trim()) = rsaMemoria
                End SyncLock
            End Using
            Return True
        Catch ex As Exception
            ScriviLog("CLSContatti.GeneraEImpostaCoppiaChiaviRubrica: " & ex.ToString())
            Return False
        End Try
    End Function

    ' Chiave pubblica della rubrica: non e' un segreto, leggerla dal database non richiede la
    ' password. Serve a cifrare contenuti (es. la sincronizzazione in entrata) anche a rubrica
    ' bloccata.
    Friend Function ChiavePubblicaRubrica(chiaveRubrica As String) As RSA
        Try
            Dim riga As DataRow = RigaRubrica(chiaveRubrica)
            If riga Is Nothing Then Return Nothing
            Dim pubblicaBase64 As String = ValoreColonna(riga, "ChiavePubblica")
            If String.IsNullOrWhiteSpace(pubblicaBase64) Then Return Nothing
            Return ImportaChiavePubblica(Convert.FromBase64String(pubblicaBase64))
        Catch ex As Exception
            ScriviLog("CLSContatti.ChiavePubblicaRubrica: " & ex.ToString())
            Return Nothing
        End Try
    End Function

    ' Chiave privata della rubrica: esiste in memoria solo se la rubrica e' stata sbloccata con
    ' la password in questa sessione. Serve a decifrare, quindi a mostrare i contenuti veri.
    Friend Function ChiavePrivataRubrica(chiaveRubrica As String) As RSA
        Try
            If String.IsNullOrWhiteSpace(chiaveRubrica) Then Return Nothing
            SyncLock _lockProtezione
                Dim rsa As RSA = Nothing
                If _chiaviPrivateRubriche.TryGetValue(chiaveRubrica.Trim(), rsa) Then Return rsa
            End SyncLock
            Return Nothing
        Catch ex As Exception
            ScriviLog("CLSContatti.ChiavePrivataRubrica: " & ex.ToString())
            Return Nothing
        End Try
    End Function

    ' True se la rubrica puo' ricevere testo cifrato in scrittura: basta la chiave pubblica,
    ' quindi vale anche da bloccata (a meno che la rubrica non abbia ancora generato la sua
    ' coppia di chiavi, cosa che succede automaticamente al primo sblocco dopo l'aggiornamento).
    Public Function RubricaPuoRicevereTesto(chiaveRubrica As String) As Boolean
        Try
            If Not RubricaProtetta(chiaveRubrica) Then Return True
            Using pubblica As RSA = ChiavePubblicaRubrica(chiaveRubrica)
                Return pubblica IsNot Nothing
            End Using
        Catch ex As Exception
            ScriviLog("CLSContatti.RubricaPuoRicevereTesto: " & ex.ToString())
            Return True
        End Try
    End Function

    ' True se la rubrica puo' fornire testo in chiaro (serve la chiave privata, quindi solo se
    ' sbloccata con la password in questa sessione). Usata prima di inviare i contenuti veri a un
    ' server esterno, dove non basta la sola chiave pubblica.
    Public Function RubricaPuoInviareTesto(chiaveRubrica As String) As Boolean
        Try
            If Not RubricaProtetta(chiaveRubrica) Then Return True
            Return ChiavePrivataRubrica(chiaveRubrica) IsNot Nothing
        Catch ex As Exception
            ScriviLog("CLSContatti.RubricaPuoInviareTesto: " & ex.ToString())
            Return True
        End Try
    End Function

    Private Function DeserializzaCampiChiave(dati As Byte()) As Byte()()
        Using memoria As New MemoryStream(dati)
            Using lettore As New BinaryReader(memoria)
                Dim conteggio As Integer = lettore.ReadInt32()
                Dim risultato(conteggio - 1)() As Byte
                For indice As Integer = 0 To conteggio - 1
                    Dim lunghezza As Integer = lettore.ReadInt32()
                    risultato(indice) = If(lunghezza > 0, lettore.ReadBytes(lunghezza), New Byte() {})
                Next
                Return risultato
            End Using
        End Using
    End Function
    ' ============================================================================================
    ' CIFRATURA IBRIDA (RSA + AES) PER LA SCRITTURA SENZA PASSWORD
    ' ============================================================================================
    ' Una chiave AES-256 e una chiave HMAC-SHA256 vengono generate a caso per ogni valore, usate
    ' per cifrare il testo (AES-CBC) e autenticarlo (cifra-poi-autentica); le due chiavi insieme
    ' vengono poi cifrate con la chiave pubblica RSA della rubrica. Chi scrive (anche la
    ' sincronizzazione automatica, senza password) ha sempre la pubblica; chi legge ha bisogno
    ' della privata, quindi della password.

    ' .NET Framework non ha i metodi ExportRSAPrivateKey/ImportRSAPrivateKey/ExportRSAPublicKey/
    ' ImportRSAPublicKey (arrivati solo con .NET Core 3.0/.NET 5+): la coppia di chiavi viene
    ' quindi serializzata a mano a partire da RSAParameters, disponibile fin dalle prime versioni
    ' del Framework.
    Private Function SerializzaCampiChiave(ParamArray campi As Byte()()) As Byte()
        Using memoria As New MemoryStream()
            Using scrittore As New BinaryWriter(memoria)
                scrittore.Write(campi.Length)
                For Each campo As Byte() In campi
                    Dim lunghezza As Integer = If(campo Is Nothing, 0, campo.Length)
                    scrittore.Write(lunghezza)
                    If lunghezza > 0 Then scrittore.Write(campo)
                Next
            End Using
            Return memoria.ToArray()
        End Using
    End Function


    Private Function EsportaChiavePubblica(rsa As RSA) As Byte()
        Dim parametri As RSAParameters = rsa.ExportParameters(False)
        Return SerializzaCampiChiave(parametri.Modulus, parametri.Exponent)
    End Function

    Private Function EsportaChiavePrivata(rsa As RSA) As Byte()
        Dim parametri As RSAParameters = rsa.ExportParameters(True)
        Return SerializzaCampiChiave(parametri.Modulus, parametri.Exponent, parametri.D,
            parametri.P, parametri.Q, parametri.DP, parametri.DQ, parametri.InverseQ)
    End Function

    Private Function ImportaChiavePubblica(dati As Byte()) As RSA
        Dim campi As Byte()() = DeserializzaCampiChiave(dati)
        Dim parametri As New RSAParameters()
        parametri.Modulus = campi(0)
        parametri.Exponent = campi(1)
        ' RSACng (CNG) al posto di RSA.Create(): quest'ultimo puo' restituire un
        ' RSACryptoServiceProvider (CAPI) che non supporta il padding OaepSHA256 usato da
        ' CifraIbrido/DecifraIbrido, causando "La modalita' di riempimento specificata non e'
        ' valida per questo algoritmo".
        Dim rsa As RSA = New RSACng()
        rsa.ImportParameters(parametri)
        Return rsa
    End Function

    Private Function ImportaChiavePrivata(dati As Byte()) As RSA
        Dim campi As Byte()() = DeserializzaCampiChiave(dati)
        Dim parametri As New RSAParameters()
        parametri.Modulus = campi(0)
        parametri.Exponent = campi(1)
        parametri.D = campi(2)
        parametri.P = campi(3)
        parametri.Q = campi(4)
        parametri.DP = campi(5)
        parametri.DQ = campi(6)
        parametri.InverseQ = campi(7)
        ' Vedi nota in ImportaChiavePubblica: serve RSACng, non RSA.Create(), per poter usare
        ' OaepSHA256 in CifraIbrido/DecifraIbrido.
        Dim rsa As RSA = New RSACng()
        rsa.ImportParameters(parametri)
        Return rsa
    End Function

    ' Cifra con la libreria nativa (WGRSA2). Se non e utilizzabile o fallisce scrive il motivo nel log e usa il sistema precedente (WGRSA1).
    Private Function CifraIbrido(valore As String, chiavePubblica As RSA) As String
        Try
            If chiavePubblica Is Nothing Then Return valore
            If WGCifraturaNativa.Funzionante Then
                Dim nativo As String = CifraIbridoNativo(valore, chiavePubblica)
                If nativo IsNot Nothing Then Return nativo
            Else
                ScriviLog("CifraIbrido: libreria nativa non utilizzabile (" & WGCifraturaNativa.MotivoNonFunzionante & "), uso il sistema precedente.")
            End If
        Catch ex As Exception
            ScriviLog("CifraIbrido: " & (ex.GetType().Name & ": " & ex.Message))
        End Try
        Return CifraIbridoPrecedente(valore, chiavePubblica)
    End Function

    Private Function CifraIbridoNativo(valore As String, chiavePubblica As RSA) As String
        Try
            Dim chiaveDati As Byte() = WGCifraturaNativa.Casuali(32)
            If chiaveDati Is Nothing Then
                ScriviLog("CifraIbridoNativo: numeri casuali non riusciti (" & WGCifraturaNativa.DettaglioNativo() & "), uso il sistema precedente.")
                Return Nothing
            End If
            Dim cifrato As Byte() = WGCifraturaNativa.CifraConChiave(chiaveDati, Encoding.UTF8.GetBytes(valore))
            If cifrato Is Nothing Then
                ScriviLog("CifraIbridoNativo: cifratura nativa non riuscita (" & WGCifraturaNativa.DettaglioNativo() & "), uso il sistema precedente.")
                Return Nothing
            End If
            ' Prima di accettarla si rilegge: se il testo non torna uguale si usa il sistema precedente.
            Dim riletto As Byte() = WGCifraturaNativa.DecifraConChiave(chiaveDati, cifrato)
            If riletto Is Nothing OrElse Not String.Equals(Encoding.UTF8.GetString(riletto), valore, StringComparison.Ordinal) Then
                ScriviLog("CifraIbridoNativo: la verifica di rilettura non e riuscita (" & WGCifraturaNativa.DettaglioNativo() & "), uso il sistema precedente.")
                Return Nothing
            End If
            Dim chiaveCifrata As Byte() = chiavePubblica.Encrypt(chiaveDati, RSAEncryptionPadding.OaepSHA256)
            Array.Clear(chiaveDati, 0, chiaveDati.Length)
            Return PrefissoCifraturaIbridaGcm & Convert.ToBase64String(chiaveCifrata) & "." & Convert.ToBase64String(cifrato)
        Catch ex As Exception
            ScriviLog("CifraIbridoNativo: " & (ex.GetType().Name & ": " & ex.Message) & " Si usa il sistema precedente.")
            Return Nothing
        End Try
    End Function

    Private Function CifraIbridoPrecedente(valore As String, chiavePubblica As RSA) As String
        Try
            If chiavePubblica Is Nothing Then Return valore
            Dim datiCombinati(63) As Byte
            Using generatore As RandomNumberGenerator = RandomNumberGenerator.Create()
                generatore.GetBytes(datiCombinati)
            End Using
            Dim chiaveAes(31) As Byte
            Dim chiaveMac(31) As Byte
            Array.Copy(datiCombinati, 0, chiaveAes, 0, 32)
            Array.Copy(datiCombinati, 32, chiaveMac, 0, 32)

            Dim testoCifrato As Byte()
            Dim iv As Byte()
            Using aes As Aes = Aes.Create()
                aes.KeySize = 256
                aes.Key = chiaveAes
                aes.GenerateIV()
                iv = aes.IV
                Using cifratore As ICryptoTransform = aes.CreateEncryptor()
                    Dim testoChiaro As Byte() = Encoding.UTF8.GetBytes(valore)
                    testoCifrato = cifratore.TransformFinalBlock(testoChiaro, 0, testoChiaro.Length)
                End Using
            End Using

            Dim tag As Byte()
            Using hmac As New HMACSHA256(chiaveMac)
                Dim datiAutenticati(iv.Length + testoCifrato.Length - 1) As Byte
                Array.Copy(iv, 0, datiAutenticati, 0, iv.Length)
                Array.Copy(testoCifrato, 0, datiAutenticati, iv.Length, testoCifrato.Length)
                tag = hmac.ComputeHash(datiAutenticati)
            End Using

            Dim chiaviCifrate As Byte() = chiavePubblica.Encrypt(datiCombinati, RSAEncryptionPadding.OaepSHA256)

            Return PrefissoCifraturaIbrida &
                Convert.ToBase64String(chiaviCifrate) & "." &
                Convert.ToBase64String(iv) & "." &
                Convert.ToBase64String(testoCifrato) & "." &
                Convert.ToBase64String(tag)
        Catch ex As Exception
            ScriviLog("CLSContatti.CifraIbrido: " & ex.ToString())
            Throw ' prima restituiva il testo in chiaro e lo salvava come se fosse cifrato
        End Try
    End Function

    Private Function DecifraIbrido(valoreCifrato As String, chiavePrivata As RSA) As String
        Try
            If chiavePrivata Is Nothing Then Return ""
            If valoreCifrato.StartsWith(PrefissoCifraturaIbridaGcm, StringComparison.Ordinal) Then
                Dim partiGcm As String() = valoreCifrato.Substring(PrefissoCifraturaIbridaGcm.Length).Split("."c)
                If partiGcm.Length <> 2 Then Return ""
                Dim chiaveDati As Byte() = chiavePrivata.Decrypt(Convert.FromBase64String(partiGcm(0)), RSAEncryptionPadding.OaepSHA256)
                Dim chiaro As Byte() = WGCifraturaNativa.DecifraConChiave(chiaveDati, Convert.FromBase64String(partiGcm(1)))
                Array.Clear(chiaveDati, 0, chiaveDati.Length)
                If chiaro Is Nothing Then Return ""
                Return Encoding.UTF8.GetString(chiaro)
            End If
        Catch ex As Exception
            ScriviLog("CLSContatti.CifraturaIbrida: " & (ex.GetType().Name & ": " & ex.Message))
            Return ""
        End Try
        Return DecifraIbridoPrecedente(valoreCifrato, chiavePrivata)
    End Function

    ' Formato WGRSA1 (AES-CBC con HMAC gestiti): solo per leggere i valori scritti prima della libreria nativa.
    Private Function DecifraIbridoPrecedente(valoreCifrato As String, chiavePrivata As RSA) As String
        Try
            If chiavePrivata Is Nothing Then Return ""
            Dim corpo As String = valoreCifrato.Substring(PrefissoCifraturaIbrida.Length)
            Dim parti As String() = corpo.Split("."c)
            If parti.Length <> 4 Then Return ""

            Dim chiaviCifrate As Byte() = Convert.FromBase64String(parti(0))
            Dim iv As Byte() = Convert.FromBase64String(parti(1))
            Dim testoCifrato As Byte() = Convert.FromBase64String(parti(2))
            Dim tag As Byte() = Convert.FromBase64String(parti(3))

            Dim datiCombinati As Byte() = chiavePrivata.Decrypt(chiaviCifrate, RSAEncryptionPadding.OaepSHA256)
            If datiCombinati.Length <> 64 Then Return ""
            Dim chiaveAes(31) As Byte
            Dim chiaveMac(31) As Byte
            Array.Copy(datiCombinati, 0, chiaveAes, 0, 32)
            Array.Copy(datiCombinati, 32, chiaveMac, 0, 32)

            Dim tagAtteso As Byte()
            Using hmac As New HMACSHA256(chiaveMac)
                Dim datiAutenticati(iv.Length + testoCifrato.Length - 1) As Byte
                Array.Copy(iv, 0, datiAutenticati, 0, iv.Length)
                Array.Copy(testoCifrato, 0, datiAutenticati, iv.Length, testoCifrato.Length)
                tagAtteso = hmac.ComputeHash(datiAutenticati)
            End Using
            If Not ConfrontaByteACiclo(tag, tagAtteso) Then Return ""

            Using aes As Aes = Aes.Create()
                aes.KeySize = 256
                aes.Key = chiaveAes
                aes.IV = iv
                Using decifratore As ICryptoTransform = aes.CreateDecryptor()
                    Dim testoChiaro As Byte() = decifratore.TransformFinalBlock(testoCifrato, 0, testoCifrato.Length)
                    Return Encoding.UTF8.GetString(testoChiaro)
                End Using
            End Using
        Catch ex As Exception
            ScriviLog("CLSContatti.DecifraIbrido: " & ex.ToString())
            Return ""
        End Try
    End Function

    Private Function EValoreIbrido(valore As String) As Boolean
        Try
            Return Not String.IsNullOrEmpty(valore) AndAlso (valore.StartsWith(PrefissoCifraturaIbrida, StringComparison.Ordinal) OrElse valore.StartsWith(PrefissoCifraturaIbridaGcm, StringComparison.Ordinal))
        Catch ex As Exception
            Return False
        End Try
    End Function

    Private Function ConfrontaByteACiclo(a As Byte(), b As Byte()) As Boolean
        Try
            If a Is Nothing OrElse b Is Nothing Then Return False
            If a.Length <> b.Length Then Return False
            Dim differenza As Integer = 0
            For indice As Integer = 0 To a.Length - 1
                differenza = differenza Or (a(indice) Xor b(indice))
            Next
            Return differenza = 0
        Catch ex As Exception
            Return False
        End Try
    End Function

    ' Prova la password su tutte le rubriche protette e restituisce quella che corrisponde,
    ' gia' sbloccata. Serve a passare da una rubrica all'altra digitando soltanto la password
    ' giusta, senza doverla scegliere prima da un elenco.
    Public Function TrovaRubricaPerPassword(password As String) As String
        Try
            If String.IsNullOrEmpty(password) Then Return ""
            Dim rubriche As DataTable = OttieniRubriche()
            If rubriche Is Nothing Then Return ""
            For Each riga As DataRow In rubriche.Rows
                Try
                    Dim impronta As String = ValoreColonna(riga, "ImprontaPassword")
                    If impronta.Length = 0 Then Continue For
                    Dim sale As String = ValoreColonna(riga, "SalePassword")
                    If Not WGCifratura.PasswordCorretta(password, sale, impronta) Then Continue For

                    Dim chiaveRubrica As String = ValoreColonna(riga, "ChiaveWG")
                    Dim chiave As Byte() = WGCifratura.DerivaChiave(password, sale)
                    If chiave Is Nothing Then Continue For
                    SyncLock _lockProtezione
                        _rubricheSbloccate(chiaveRubrica) = chiave
                    End SyncLock
                    If Not CaricaOGeneraChiavePrivataRubrica(chiaveRubrica, chiave) Then
                        BloccaRubrica(chiaveRubrica)
                        Continue For
                    End If
                    Return chiaveRubrica
                Catch exRiga As Exception
                    ScriviLog("CLSContatti.TrovaRubricaPerPassword riga: " & exRiga.Message)
                End Try
            Next
            Return ""
        Catch ex As Exception
            ImpostaErrore("CLSContatti.TrovaRubricaPerPassword", ex)
            Return ""
        End Try
    End Function

    Public Sub BloccaRubrica(chiaveRubrica As String)
        Try
            If String.IsNullOrWhiteSpace(chiaveRubrica) Then Return
            SyncLock _lockProtezione
                If _rubricheSbloccate.ContainsKey(chiaveRubrica.Trim()) Then _rubricheSbloccate.Remove(chiaveRubrica.Trim())
                Dim rsaPrivata As RSA = Nothing
                If _chiaviPrivateRubriche.TryGetValue(chiaveRubrica.Trim(), rsaPrivata) Then
                    _chiaviPrivateRubriche.Remove(chiaveRubrica.Trim())
                    rsaPrivata.Dispose()
                End If
            End SyncLock
        Catch ex As Exception
            ScriviLog("CLSContatti.BloccaRubrica: " & ex.ToString())
        End Try
    End Sub

    Public Sub BloccaTutteLeRubriche()
        Try
            SyncLock _lockProtezione
                _rubricheSbloccate.Clear()
                For Each rsaPrivata As RSA In _chiaviPrivateRubriche.Values
                    Try
                        rsaPrivata.Dispose()
                    Catch
                    End Try
                Next
                _chiaviPrivateRubriche.Clear()
            End SyncLock
        Catch ex As Exception
            ScriviLog("CLSContatti.BloccaTutteLeRubriche: " & ex.ToString())
        End Try
    End Sub

    Friend Function ChiaveDiCifratura(chiaveRubrica As String) As Byte()
        Try
            If String.IsNullOrWhiteSpace(chiaveRubrica) Then Return Nothing
            SyncLock _lockProtezione
                Dim chiave As Byte() = Nothing
                If _rubricheSbloccate.TryGetValue(chiaveRubrica.Trim(), chiave) Then Return chiave
            End SyncLock
            Return Nothing
        Catch ex As Exception
            ScriviLog("CLSContatti.ChiaveDiCifratura: " & ex.ToString())
            Return Nothing
        End Try
    End Function

    ' ============================================================================================
    ' IMPOSTAZIONE E RIMOZIONE DELLA PASSWORD
    ' ============================================================================================

    ' Mette la password su una rubrica e cifra subito i contatti che contiene. La rubrica
    ' rimane sbloccata per la sessione in corso.
    Public Function ImpostaPasswordRubrica(chiaveRubrica As String, password As String) As Boolean
        Try
            If String.IsNullOrWhiteSpace(chiaveRubrica) Then Return False
            If String.IsNullOrEmpty(password) Then
                _ultimoErrore = "La password non può essere vuota."
                Return False
            End If
            Dim riga As DataRow = RigaRubrica(chiaveRubrica)
            If riga Is Nothing Then
                _ultimoErrore = "Rubrica non trovata."
                Return False
            End If
            If ValoreColonna(riga, "ImprontaPassword").Length > 0 Then
                _ultimoErrore = "Questa rubrica ha già una password."
                Return False
            End If

            Dim sale As String = WGCifratura.GeneraSale()
            Dim impronta As String = WGCifratura.CalcolaImpronta(password, sale)
            Dim chiave As Byte() = WGCifratura.DerivaChiave(password, sale)
            If String.IsNullOrWhiteSpace(sale) OrElse String.IsNullOrWhiteSpace(impronta) OrElse chiave Is Nothing Then
                _ultimoErrore = "Non è stato possibile preparare la protezione."
                Return False
            End If

            Dim rubrica As String = chiaveRubrica.Trim()

            ' 1) Prima si registra la protezione e si generano le chiavi, poi si cifrano i dati.
            ' Nell'ordine inverso, un errore a meta' lasciava contatti cifrati in una rubrica
            ' non protetta, quindi illeggibili. Cosi' al peggio restano contatti ancora in chiaro.
            If Not _db.AggiornaRecord(NomeTabellaRubriche, rubrica, New Dictionary(Of String, Object) From {
                {"Protetta", 1},
                {"SalePassword", sale},
                {"ImprontaPassword", impronta}
            }) Then
                _ultimoErrore = "Non è stato possibile salvare la protezione della rubrica."
                Return False
            End If

            SyncLock _lockProtezione
                _rubricheSbloccate(rubrica) = chiave
            End SyncLock

            If Not GeneraEImpostaCoppiaChiaviRubrica(rubrica, chiave) Then
                ' Annulla: senza chiave pubblica la rubrica protetta salverebbe i nuovi contatti in chiaro.
                _db.AggiornaRecord(NomeTabellaRubriche, rubrica, New Dictionary(Of String, Object) From {
                    {"Protetta", 0},
                    {"SalePassword", ""},
                    {"ImprontaPassword", ""},
                    {"ChiavePubblica", ""},
                    {"ChiavePrivataCifrata", ""}
                })
                BloccaRubrica(rubrica)
                _ultimoErrore = "Non è stato possibile generare le chiavi di cifratura della rubrica."
                Return False
            End If

            ' 2) Cifratura dei contatti gia' presenti
            Dim contatti As DataTable = _db.EseguiQuery(
                "SELECT * FROM " & NomeTabellaContatti & " WHERE ChiaveRubrica = @rubrica;",
                New Dictionary(Of String, Object) From {{"@rubrica", rubrica}})

            If contatti IsNot Nothing Then
                For Each contatto As DataRow In contatti.Rows
                    Try
                        Dim valori As New Dictionary(Of String, Object)
                        For Each campo As String In CampiRiservati
                            Dim valore As String = ValoreColonna(contatto, campo)
                            If valore.Length = 0 Then Continue For
                            valori(campo) = WGCifratura.Cifra(valore, chiave)
                        Next
                        If valori.Count > 0 Then _db.AggiornaRecord(NomeTabellaContatti, ValoreColonna(contatto, "ChiaveWG"), valori)
                    Catch exContatto As Exception
                        ScriviLog("CLSContatti.ImpostaPasswordRubrica contatto: " & exContatto.ToString())
                    End Try
                Next
            End If

            ' 3) Cifratura dei recapiti
            Dim recapiti As DataTable = _db.EseguiQuery(
                "SELECT " & NomeTabellaRecapiti & ".ChiaveWG AS ChiaveRecapito, " & NomeTabellaRecapiti & ".Valore AS Valore FROM " & NomeTabellaRecapiti &
                " INNER JOIN " & NomeTabellaContatti & " ON " & NomeTabellaContatti & ".ChiaveWG = " & NomeTabellaRecapiti & ".ChiaveContatto" &
                " WHERE " & NomeTabellaContatti & ".ChiaveRubrica = @rubrica;",
                New Dictionary(Of String, Object) From {{"@rubrica", rubrica}})
            If recapiti IsNot Nothing Then
                For Each recapito As DataRow In recapiti.Rows
                    Try
                        Dim valore As String = ValoreColonna(recapito, "Valore")
                        If valore.Length = 0 Then Continue For
                        Dim valoreCifrato As String = CifraValoreRecapito(rubrica, valore)
                        _db.AggiornaRecord(NomeTabellaRecapiti, ValoreColonna(recapito, "ChiaveRecapito"),
                            New Dictionary(Of String, Object) From {{"Valore", valoreCifrato}})
                    Catch exRecapito As Exception
                        ScriviLog("CLSContatti.ImpostaPasswordRubrica recapito: " & exRecapito.ToString())
                    End Try
                Next
            End If

            Return True
        Catch ex As Exception
            ImpostaErrore("CLSContatti.ImpostaPasswordRubrica", ex)
            Return False
        End Try
    End Function

    ' Toglie la password e riporta in chiaro i contatti. La rubrica deve essere sbloccata,
    ' altrimenti i dati non sarebbero leggibili e verrebbero persi.
    Public Function TogliPasswordRubrica(chiaveRubrica As String) As Boolean
        Try
            If String.IsNullOrWhiteSpace(chiaveRubrica) Then Return False
            If Not RubricaProtetta(chiaveRubrica) Then Return True
            Dim chiave As Byte() = ChiaveDiCifratura(chiaveRubrica)
            If chiave Is Nothing Then
                _ultimoErrore = "Sblocca la rubrica prima di togliere la password."
                Return False
            End If
            Dim rsaPrivata As RSA = ChiavePrivataRubrica(chiaveRubrica)
            Dim rubrica As String = chiaveRubrica.Trim()

            ' Fase 1: si decifra tutto in memoria. Se anche un solo valore non si decifra
            ' (la funzione restituisce "") non si scrive niente: prima quel campo veniva
            ' riscritto vuoto e la password tolta, quindi il dato andava perso per sempre.
            Dim errori As Integer = 0
            Dim daScrivereContatti As New List(Of Tuple(Of String, Dictionary(Of String, Object)))
            Dim daScrivereRecapiti As New List(Of Tuple(Of String, String))

            Dim contatti As DataTable = _db.EseguiQuery(
                "SELECT * FROM " & NomeTabellaContatti & " WHERE ChiaveRubrica = @rubrica;",
                New Dictionary(Of String, Object) From {{"@rubrica", rubrica}})

            If contatti IsNot Nothing Then
                For Each contatto As DataRow In contatti.Rows
                    Try
                        Dim valori As New Dictionary(Of String, Object)
                        For Each campo As String In CampiRiservati
                            Dim valore As String = ValoreColonna(contatto, campo)
                            Dim chiaro As String = ""
                            If EValoreIbrido(valore) Then
                                chiaro = DecifraIbrido(valore, rsaPrivata)
                            ElseIf WGCifratura.ECifrato(valore) Then
                                chiaro = WGCifratura.Decifra(valore, chiave)
                            Else
                                Continue For
                            End If
                            If String.IsNullOrEmpty(chiaro) Then
                                errori += 1
                            Else
                                valori(campo) = chiaro
                            End If
                        Next
                        If valori.Count > 0 Then daScrivereContatti.Add(Tuple.Create(ValoreColonna(contatto, "ChiaveWG"), valori))
                    Catch exContatto As Exception
                        errori += 1
                        ScriviLog("CLSContatti.TogliPasswordRubrica contatto: " & exContatto.ToString())
                    End Try
                Next
            End If

            Dim recapiti As DataTable = _db.EseguiQuery(
                "SELECT " & NomeTabellaRecapiti & ".ChiaveWG AS ChiaveRecapito, " & NomeTabellaRecapiti & ".Valore AS Valore FROM " & NomeTabellaRecapiti &
                " INNER JOIN " & NomeTabellaContatti & " ON " & NomeTabellaContatti & ".ChiaveWG = " & NomeTabellaRecapiti & ".ChiaveContatto" &
                " WHERE " & NomeTabellaContatti & ".ChiaveRubrica = @rubrica;",
                New Dictionary(Of String, Object) From {{"@rubrica", rubrica}})

            If recapiti IsNot Nothing Then
                For Each recapito As DataRow In recapiti.Rows
                    Try
                        Dim valore As String = ValoreColonna(recapito, "Valore")
                        Dim valoreChiaro As String = ""
                        If EValoreIbrido(valore) Then
                            valoreChiaro = DecifraIbrido(valore, rsaPrivata)
                        ElseIf WGCifratura.ECifrato(valore) Then
                            valoreChiaro = WGCifratura.Decifra(valore, chiave)
                        Else
                            Continue For
                        End If
                        If String.IsNullOrEmpty(valoreChiaro) Then
                            errori += 1
                        Else
                            daScrivereRecapiti.Add(Tuple.Create(ValoreColonna(recapito, "ChiaveRecapito"), valoreChiaro))
                        End If
                    Catch exRecapito As Exception
                        errori += 1
                        ScriviLog("CLSContatti.TogliPasswordRubrica recapito: " & exRecapito.ToString())
                    End Try
                Next
            End If

            If errori > 0 Then
                ScriviLog("CLSContatti.TogliPasswordRubrica: " & errori & " valori non decifrabili, operazione annullata (" & rubrica & ")")
                _ultimoErrore = "Alcuni dati non si riescono a decifrare: la password non è stata tolta e i contatti non sono stati modificati."
                Return False
            End If

            ' Fase 2: scrittura
            For Each elemento As Tuple(Of String, Dictionary(Of String, Object)) In daScrivereContatti
                _db.AggiornaRecord(NomeTabellaContatti, elemento.Item1, elemento.Item2)
            Next
            For Each elemento As Tuple(Of String, String) In daScrivereRecapiti
                _db.AggiornaRecord(NomeTabellaRecapiti, elemento.Item1,
                    New Dictionary(Of String, Object) From {{"Valore", elemento.Item2}})
            Next

            _db.AggiornaRecord(NomeTabellaRubriche, rubrica, New Dictionary(Of String, Object) From {
                {"Protetta", 0},
                {"SalePassword", ""},
                {"ImprontaPassword", ""},
                {"ChiavePubblica", ""},
                {"ChiavePrivataCifrata", ""}
            })
            BloccaRubrica(chiaveRubrica)
            Return True
        Catch ex As Exception
            ImpostaErrore("CLSContatti.TogliPasswordRubrica", ex)
            Return False
        End Try
    End Function

    Public Function CambiaPasswordRubrica(chiaveRubrica As String, nuovaPassword As String) As Boolean
        Try
            If Not TogliPasswordRubrica(chiaveRubrica) Then Return False
            Return ImpostaPasswordRubrica(chiaveRubrica, nuovaPassword)
        Catch ex As Exception
            ImpostaErrore("CLSContatti.CambiaPasswordRubrica", ex)
            Return False
        End Try
    End Function

    ' ============================================================================================
    ' LETTURA E SCRITTURA DEI CAMPI CIFRATI
    ' ============================================================================================

    ' Toglie dalla tabella i contatti delle rubriche bloccate e riporta in chiaro quelli delle
    ' rubriche aperte. Chi chiama riceve dati leggibili senza sapere nulla della cifratura.
    Friend Function PreparaContatti(tabella As DataTable) As DataTable
        Try
            If tabella Is Nothing OrElse tabella.Rows.Count = 0 Then Return tabella
            If Not tabella.Columns.Contains("ChiaveRubrica") Then Return tabella

            Dim protette As New Dictionary(Of String, Boolean)(StringComparer.OrdinalIgnoreCase)
            Dim rubriche As DataTable = OttieniRubriche()
            If rubriche IsNot Nothing Then
                For Each riga As DataRow In rubriche.Rows
                    protette(ValoreColonna(riga, "ChiaveWG")) = ValoreColonna(riga, "ImprontaPassword").Length > 0
                Next
            End If

            For indice As Integer = tabella.Rows.Count - 1 To 0 Step -1
                Try
                    Dim riga As DataRow = tabella.Rows(indice)
                    Dim rubrica As String = ValoreColonna(riga, "ChiaveRubrica")
                    Dim eProtetta As Boolean = False
                    If rubrica.Length > 0 AndAlso protette.ContainsKey(rubrica) Then eProtetta = protette(rubrica)
                    If Not eProtetta Then Continue For

                    Dim chiave As Byte() = ChiaveDiCifratura(rubrica)
                    Dim rsaPrivata As RSA = ChiavePrivataRubrica(rubrica)
                    If chiave Is Nothing AndAlso rsaPrivata Is Nothing Then
                        tabella.Rows.RemoveAt(indice)
                        Continue For
                    End If

                    For Each campo As String In CampiRiservati
                        If Not tabella.Columns.Contains(campo) Then Continue For
                        Dim valore As String = ValoreColonna(riga, campo)
                        If EValoreIbrido(valore) Then
                            riga(campo) = DecifraIbrido(valore, rsaPrivata)
                        ElseIf WGCifratura.ECifrato(valore) Then
                            If chiave Is Nothing Then Continue For
                            riga(campo) = WGCifratura.Decifra(valore, chiave)
                        End If
                    Next
                Catch exRiga As Exception
                    ScriviLog("CLSContatti.PreparaContatti riga: " & exRiga.Message)
                End Try
            Next
            tabella.AcceptChanges()
            Return tabella
        Catch ex As Exception
            ScriviLog("CLSContatti.PreparaContatti: " & ex.ToString())
            Return tabella
        End Try
    End Function

    ' Cifra i campi personali quando la rubrica di destinazione ha una password. Basta la chiave
    ' pubblica, sempre disponibile una volta generata (vedi GeneraEImpostaCoppiaChiaviRubrica):
    ' funziona quindi anche a rubrica bloccata, cosi' la sincronizzazione automatica non ha mai
    ' bisogno della password per scrivere.
    Friend Function CifraValoriContatto(chiaveRubrica As String, valori As Dictionary(Of String, Object)) As Dictionary(Of String, Object)
        Try
            If valori Is Nothing Then Return valori
            If String.IsNullOrWhiteSpace(chiaveRubrica) Then Return valori
            If Not RubricaProtetta(chiaveRubrica) Then Return valori
            Using pubblica As RSA = ChiavePubblicaRubrica(chiaveRubrica)
                If pubblica Is Nothing Then Throw New InvalidOperationException("La rubrica protetta non ha la chiave di cifratura: salvataggio annullato.")

                For Each campo As String In CampiRiservati
                    If Not valori.ContainsKey(campo) Then Continue For
                    Dim valore As String = ""
                    If valori(campo) IsNot Nothing Then valore = valori(campo).ToString()
                    If valore.Length = 0 Then Continue For
                    valori(campo) = CifraIbrido(valore, pubblica)
                Next
            End Using
            Return valori
        Catch ex As Exception
            ScriviLog("CLSContatti.CifraValoriContatto: " & ex.ToString())
            Throw
        End Try
    End Function

    ' Cifra il valore di un singolo recapito multiplo (telefono, email, indirizzo o sito
    ' aggiuntivo in contatti_recapiti) quando la rubrica del contatto ha una password. Basta la
    ' chiave pubblica: funziona anche a rubrica bloccata.
    Friend Function CifraValoreRecapito(chiaveRubrica As String, valore As String) As String
        Try
            If String.IsNullOrEmpty(valore) Then Return valore
            If String.IsNullOrWhiteSpace(chiaveRubrica) Then Return valore
            If Not RubricaProtetta(chiaveRubrica) Then Return valore
            Using pubblica As RSA = ChiavePubblicaRubrica(chiaveRubrica)
                If pubblica Is Nothing Then Throw New InvalidOperationException("La rubrica protetta non ha la chiave di cifratura: salvataggio annullato.")
                Return CifraIbrido(valore, pubblica)
            End Using
        Catch ex As Exception
            ScriviLog("CLSContatti.CifraValoreRecapito: " & ex.ToString())
            Throw
        End Try
    End Function

    ' Decifra il valore di un singolo recapito multiplo, simmetrico a CifraValoreRecapito.
    ' Riconosce sia il vecchio formato simmetrico sia il nuovo formato a coppia di chiavi.
    Friend Function DecifraValoreRecapito(chiaveRubrica As String, valore As String) As String
        Try
            If String.IsNullOrEmpty(valore) Then Return valore
            If String.IsNullOrWhiteSpace(chiaveRubrica) Then Return valore
            If EValoreIbrido(valore) Then
                Dim rsaPrivata As RSA = ChiavePrivataRubrica(chiaveRubrica)
                If rsaPrivata Is Nothing Then Return ""
                Return DecifraIbrido(valore, rsaPrivata)
            End If
            If Not WGCifratura.ECifrato(valore) Then Return valore
            Dim chiave As Byte() = ChiaveDiCifratura(chiaveRubrica)
            If chiave Is Nothing Then Return ""
            Return WGCifratura.Decifra(valore, chiave)
        Catch ex As Exception
            ScriviLog("CLSContatti.DecifraValoreRecapito: " & ex.ToString())
            Return valore
        End Try
    End Function

    ' Minuscolo e senza accenti, cosi' "Nicolo" trova "Nicolò".
    Private Shared Function NormalizzaPerRicerca(testo As String) As String
        Try
            If String.IsNullOrEmpty(testo) Then Return ""
            Dim scomposto As String = testo.Normalize(NormalizationForm.FormD)
            Dim sb As New StringBuilder(scomposto.Length)
            For Each carattere As Char In scomposto
                If CharUnicodeInfo.GetUnicodeCategory(carattere) <> UnicodeCategory.NonSpacingMark Then sb.Append(carattere)
            Next
            Return sb.ToString().ToLowerInvariant()
        Catch
            Return testo.ToLowerInvariant()
        End Try
    End Function

    ' Per ogni contatto, il testo di tutti i suoi recapiti multipli (email, telefoni, indirizzi,
    ' siti) gia' in chiaro, cosi' la ricerca trova anche il secondo numero o la seconda email.
    ' Una sola query per tutti i contatti; le rubriche protette si leggono solo se aperte.
    Friend Function TestoRecapitiPerContatto(tabella As DataTable) As Dictionary(Of String, String)
        Dim risultato As New Dictionary(Of String, String)(StringComparer.OrdinalIgnoreCase)
        Try
            If tabella Is Nothing OrElse tabella.Rows.Count = 0 Then Return risultato
            Dim rubricaDi As New Dictionary(Of String, String)(StringComparer.OrdinalIgnoreCase)
            For Each riga As DataRow In tabella.Rows
                rubricaDi(ValoreColonna(riga, "ChiaveWG")) = ValoreColonna(riga, "ChiaveRubrica")
            Next
            Dim recapiti As DataTable = _db.EseguiQuery("SELECT ChiaveContatto, Valore FROM " & NomeTabellaRecapiti & ";", Nothing)
            If recapiti Is Nothing Then Return risultato
            For Each riga As DataRow In recapiti.Rows
                Try
                    Dim contatto As String = ValoreColonna(riga, "ChiaveContatto")
                    Dim rubrica As String = Nothing
                    If Not rubricaDi.TryGetValue(contatto, rubrica) Then Continue For
                    Dim valore As String = DecifraValoreRecapito(rubrica, ValoreColonna(riga, "Valore"))
                    If String.IsNullOrEmpty(valore) Then Continue For
                    If risultato.ContainsKey(contatto) Then
                        risultato(contatto) = risultato(contatto) & " " & valore
                    Else
                        risultato(contatto) = valore
                    End If
                Catch exRiga As Exception
                    ScriviLog("CLSContatti.TestoRecapitiPerContatto riga: " & exRiga.Message)
                End Try
            Next
        Catch ex As Exception
            ScriviLog("CLSContatti.TestoRecapitiPerContatto: " & ex.ToString())
        End Try
        Return risultato
    End Function

    ' Filtro in memoria, usato al posto della ricerca SQL quando esistono rubriche protette:
    ' su un campo cifrato la ricerca del database non troverebbe niente.
    ' Il testo si divide in parole e ogni parola deve comparire in uno qualsiasi dei campi,
    ' in qualunque ordine: "mario rossi", "rossi mario" e "mar ros" trovano tutti Mario Rossi.
    ' Prima si cercava l'intero testo dentro un solo campo alla volta, quindi nome e cognome
    ' insieme non venivano mai trovati. Maiuscole e accenti non contano.
    Friend Function FiltraPerTesto(tabella As DataTable, testo As String, campi As String(),
                                   Optional extra As Dictionary(Of String, String) = Nothing) As DataTable
        Try
            If tabella Is Nothing Then Return New DataTable()
            Dim cercato As String = If(testo, "").Trim()
            If cercato.Length = 0 Then Return tabella
            Dim parole As String() = NormalizzaPerRicerca(cercato).Split(New Char() {" "c, ChrW(9)}, StringSplitOptions.RemoveEmptyEntries)
            If parole.Length = 0 Then Return tabella

            For indice As Integer = tabella.Rows.Count - 1 To 0 Step -1
                Try
                    Dim riga As DataRow = tabella.Rows(indice)
                    Dim pagliaio As New StringBuilder()
                    For Each campo As String In campi
                        If Not tabella.Columns.Contains(campo) Then Continue For
                        pagliaio.Append(ValoreColonna(riga, campo)).Append(" ")
                    Next
                    If extra IsNot Nothing Then
                        Dim valoreExtra As String = Nothing
                        If extra.TryGetValue(ValoreColonna(riga, "ChiaveWG"), valoreExtra) Then pagliaio.Append(valoreExtra)
                    End If

                    Dim testoContatto As String = NormalizzaPerRicerca(pagliaio.ToString())
                    Dim tutteTrovate As Boolean = True
                    For Each parola As String In parole
                        If testoContatto.IndexOf(parola, StringComparison.Ordinal) < 0 Then
                            tutteTrovate = False
                            Exit For
                        End If
                    Next
                    If Not tutteTrovate Then tabella.Rows.RemoveAt(indice)
                Catch exRiga As Exception
                    ScriviLog("CLSContatti.FiltraPerTesto riga: " & exRiga.Message)
                End Try
            Next
            tabella.AcceptChanges()
            Return tabella
        Catch ex As Exception
            ScriviLog("CLSContatti.FiltraPerTesto: " & ex.ToString())
            Return tabella
        End Try
    End Function

    ' ============================================================================================
    ' RUBRICHE LOCALI
    ' ============================================================================================

    ' Rubrica senza account: esiste solo nel computer e non viene mai sincronizzata.
    Public Function CreaRubricaLocale(nome As String, Optional predefinita As Boolean = False) As String
        Try
            Return CreaRubrica("", nome, predefinita)
        Catch ex As Exception
            ImpostaErrore("CLSContatti.CreaRubricaLocale", ex)
            Return ""
        End Try
    End Function

    Public Function RubricaELocale(chiaveRubrica As String) As Boolean
        Try
            Dim riga As DataRow = RigaRubrica(chiaveRubrica)
            If riga Is Nothing Then Return False
            Return ValoreColonna(riga, "ChiaveAccount").Length = 0
        Catch ex As Exception
            ScriviLog("CLSContatti.RubricaELocale: " & ex.ToString())
            Return False
        End Try
    End Function

    Public Function AccountDellaRubrica(chiaveRubrica As String) As String
        Try
            Dim riga As DataRow = RigaRubrica(chiaveRubrica)
            If riga Is Nothing Then Return ""
            Return ValoreColonna(riga, "ChiaveAccount")
        Catch ex As Exception
            ScriviLog("CLSContatti.AccountDellaRubrica: " & ex.ToString())
            Return ""
        End Try
    End Function

    Public Function CollegaRubricaAdAccount(chiaveRubrica As String, chiaveAccount As String) As Boolean
        Try
            If String.IsNullOrWhiteSpace(chiaveRubrica) Then Return False
            Return _db.AggiornaRecord(NomeTabellaRubriche, chiaveRubrica.Trim(), New Dictionary(Of String, Object) From {
                {"ChiaveAccount", If(chiaveAccount, "").Trim()}
            })
        Catch ex As Exception
            ImpostaErrore("CLSContatti.CollegaRubricaAdAccount", ex)
            Return False
        End Try
    End Function

End Class