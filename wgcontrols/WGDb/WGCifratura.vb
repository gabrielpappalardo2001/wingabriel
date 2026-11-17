Imports System
Imports System.IO
Imports System.Security.Cryptography
Imports System.Text

' Protezione con password di rubriche e agende. La password non viene mai salvata. Dal sale
' e dalla password si ricavano in un colpo solo due valori distinti: la chiave con cui i campi
' vengono cifrati e l'impronta con cui la password viene riconosciuta. L'impronta finisce nel
' database, la chiave resta soltanto in memoria finche' il contenitore e' sbloccato.
Public NotInheritable Class WGCifratura

    ' WGC1: formato precedente (AES-CBC senza autenticazione), solo in lettura, mai piu' scritto. WGC2: AES-256-GCM con
    ' la libreria nativa, usato per scrivere quando la libreria c'e'. WGC3: riserva gestita (AES-256-CBC cifra-poi-autentica
    ' con HMAC-SHA256), usata per scrivere quando la libreria nativa non e' disponibile, cosi' i dati nuovi restano
    ' autenticati anche senza di essa.
    Private Const PrefissoPrecedente As String = "WGC1:"
    Private Const PrefissoCifrato As String = "WGC2:"
    Private Const PrefissoRiserva As String = "WGC3:"
    Private Const LunghezzaTagMac As Integer = 32
    Private Const IterazioniDerivazione As Integer = 120000
    Private Const LunghezzaSale As Integer = 16
    Private Const LunghezzaChiave As Integer = 32
    Private Const LunghezzaImpronta As Integer = 32

    Private Shared ReadOnly _registro As New LoggerWGDb()
    Private Shared _avvisoSistemaPrecedente As Boolean = False

    Private Sub New()
    End Sub

    Private Shared Sub Registra(messaggio As String)
        Try
            _registro.ScriviLog(messaggio)
        Catch exLog As Exception
            RegistroErrori.Registra("WGCifratura.Registra", exLog)
        End Try
    End Sub

    Public Shared Function GeneraSale() As String
        Try
            Dim sale(LunghezzaSale - 1) As Byte
            Using generatore As RandomNumberGenerator = RandomNumberGenerator.Create()
                generatore.GetBytes(sale)
            End Using
            Return Convert.ToBase64String(sale)
        Catch ex As Exception
            Registra("WGCifratura.GeneraSale: " & ex.ToString())
            Return ""
        End Try
    End Function

    ' Deriva insieme chiave e impronta, per non ripetere due volte un calcolo lento.
    Private Shared Function DerivaMateriale(password As String, saleBase64 As String) As Byte()
        Try
            If String.IsNullOrEmpty(password) Then Return Nothing
            If String.IsNullOrWhiteSpace(saleBase64) Then Return Nothing
            Dim sale As Byte() = Convert.FromBase64String(saleBase64)
            Dim materiale As Byte() = Nothing
            If WGCifraturaNativa.Funzionante Then
                materiale = WGCifraturaNativa.DerivaPbkdf2(password, sale, IterazioniDerivazione, LunghezzaChiave + LunghezzaImpronta)
                If materiale Is Nothing Then Registra("WGCifratura.DerivaMateriale: derivazione nativa non riuscita (" & WGCifraturaNativa.DettaglioNativo() & "), uso il sistema precedente.")
            ElseIf Not _avvisoSistemaPrecedente Then
                _avvisoSistemaPrecedente = True
                Registra("WGCifratura: libreria nativa non utilizzabile (" & WGCifraturaNativa.MotivoNonFunzionante & "), si usa il sistema precedente.")
            End If
            If materiale Is Nothing Then
                ' Stesso risultato della libreria: PBKDF2-HMAC-SHA256 con le stesse iterazioni.
                Using derivazione As New Rfc2898DeriveBytes(password, sale, IterazioniDerivazione, HashAlgorithmName.SHA256)
                    materiale = derivazione.GetBytes(LunghezzaChiave + LunghezzaImpronta)
                End Using
            End If
            Return materiale
        Catch ex As Exception
            Registra("WGCifratura.DerivaMateriale: " & ex.ToString())
            Return Nothing
        End Try
    End Function

    Public Shared Function DerivaChiave(password As String, saleBase64 As String) As Byte()
        Try
            Dim materiale As Byte() = DerivaMateriale(password, saleBase64)
            If materiale Is Nothing Then Return Nothing
            Dim chiave(LunghezzaChiave - 1) As Byte
            Array.Copy(materiale, 0, chiave, 0, LunghezzaChiave)
            Return chiave
        Catch ex As Exception
            Registra("WGCifratura.DerivaChiave: " & ex.ToString())
            Return Nothing
        End Try
    End Function

    Public Shared Function CalcolaImpronta(password As String, saleBase64 As String) As String
        Try
            Dim materiale As Byte() = DerivaMateriale(password, saleBase64)
            If materiale Is Nothing Then Return ""
            Dim impronta(LunghezzaImpronta - 1) As Byte
            Array.Copy(materiale, LunghezzaChiave, impronta, 0, LunghezzaImpronta)
            Return Convert.ToBase64String(impronta)
        Catch ex As Exception
            Registra("WGCifratura.CalcolaImpronta: " & ex.ToString())
            Return ""
        End Try
    End Function

    ' Confronto a tempo costante, per non lasciare che la durata del confronto riveli quanti
    ' caratteri iniziali della password sono corretti.
    Public Shared Function PasswordCorretta(password As String, saleBase64 As String, improntaAttesa As String) As Boolean
        Try
            If String.IsNullOrWhiteSpace(improntaAttesa) Then Return False
            Dim calcolata As String = CalcolaImpronta(password, saleBase64)
            If String.IsNullOrWhiteSpace(calcolata) Then Return False

            Dim primo As Byte() = Convert.FromBase64String(calcolata)
            Dim secondo As Byte() = Convert.FromBase64String(improntaAttesa)
            If primo.Length <> secondo.Length Then Return False

            Dim differenza As Integer = 0
            For indice As Integer = 0 To primo.Length - 1
                differenza = differenza Or (primo(indice) Xor secondo(indice))
            Next
            Return differenza = 0
        Catch ex As Exception
            Registra("WGCifratura.PasswordCorretta: " & ex.ToString())
            Return False
        End Try
    End Function

    Public Shared Function ECifrato(valore As String) As Boolean
        Try
            If String.IsNullOrEmpty(valore) Then Return False
            Return valore.StartsWith(PrefissoCifrato, StringComparison.Ordinal) OrElse valore.StartsWith(PrefissoPrecedente, StringComparison.Ordinal) OrElse valore.StartsWith(PrefissoRiserva, StringComparison.Ordinal)
        Catch ex As Exception
            Registra("WGCifratura.ECifrato: " & ex.Message)
            Return False
        End Try
    End Function

    ' Chiave per l'HMAC del formato di riserva, ricavata da quella di cifratura con un'etichetta fissa: le due chiavi
    ' sono indipendenti (proprieta' delle funzioni pseudocasuali di HMAC) pur nascendo dalla stessa derivazione.
    Private Shared Function ChiaveMacRiserva(chiave As Byte()) As Byte()
        Using hmac As New HMACSHA256(chiave)
            Return hmac.ComputeHash(Encoding.UTF8.GetBytes("WGCifratura-riserva-mac-v1"))
        End Using
    End Function

    ' Ogni valore ha il proprio vettore di inizializzazione, generato al momento e conservato
    ' davanti al testo cifrato. Due campi con lo stesso contenuto danno quindi risultati diversi.
    Public Shared Function Cifra(testo As String, chiave As Byte()) As String
        Try
            If chiave Is Nothing OrElse chiave.Length <> LunghezzaChiave Then Return If(testo, "")
            If testo Is Nothing Then testo = ""
            If ECifrato(testo) Then Return testo

            If WGCifraturaNativa.Funzionante Then
                Try
                    Dim cifrato As Byte() = WGCifraturaNativa.CifraConChiave(chiave, Encoding.UTF8.GetBytes(testo))
                    If cifrato Is Nothing Then
                        Registra("WGCifratura.Cifra: cifratura nativa non riuscita (" & WGCifraturaNativa.DettaglioNativo() & "), uso il sistema precedente.")
                    Else
                        ' Prima di accettarla si rilegge: se il testo non torna uguale si usa il sistema precedente.
                        Dim riletto As Byte() = WGCifraturaNativa.DecifraConChiave(chiave, cifrato)
                        If riletto IsNot Nothing AndAlso String.Equals(Encoding.UTF8.GetString(riletto), testo, StringComparison.Ordinal) Then
                            Return PrefissoCifrato & Convert.ToBase64String(cifrato)
                        End If
                        Registra("WGCifratura.Cifra: la verifica di rilettura del testo cifrato non e riuscita (" & WGCifraturaNativa.DettaglioNativo() & "), uso il sistema precedente.")
                    End If
                Catch exNativa As Exception
                    Registra("WGCifratura.Cifra: errore con la libreria nativa, uso il sistema precedente: " & (exNativa.GetType().Name & ": " & exNativa.Message))
                End Try
            ElseIf Not _avvisoSistemaPrecedente Then
                _avvisoSistemaPrecedente = True
                Registra("WGCifratura: libreria nativa non utilizzabile (" & WGCifraturaNativa.MotivoNonFunzionante & "), si usa la riserva gestita.")
            End If
            Return CifraRiserva(testo, chiave)
        Catch ex As Exception
            Registra("WGCifratura.Cifra: " & ex.ToString())
            Return If(testo, "")
        End Try
    End Function

    Public Shared Function Decifra(valore As String, chiave As Byte()) As String
        Try
            If String.IsNullOrEmpty(valore) Then Return ""
            If Not ECifrato(valore) Then Return valore
            If chiave Is Nothing OrElse chiave.Length <> LunghezzaChiave Then Return ""

            If valore.StartsWith(PrefissoCifrato, StringComparison.Ordinal) Then
                Dim blob As Byte() = Convert.FromBase64String(valore.Substring(PrefissoCifrato.Length))
                Dim chiaro As Byte() = WGCifraturaNativa.DecifraConChiave(chiave, blob)
                If chiaro Is Nothing Then Return ""
                Return Encoding.UTF8.GetString(chiaro)
            End If

            If valore.StartsWith(PrefissoRiserva, StringComparison.Ordinal) Then Return DecifraRiserva(valore, chiave)

            Return DecifraPrecedente(valore, chiave)
        Catch ex As CryptographicException
            Registra("WGCifratura.Decifra chiave errata: " & ex.Message)
            Return ""
        Catch ex As Exception
            Registra("WGCifratura.Decifra: " & ex.ToString())
            Return ""
        End Try
    End Function



    ' Formato WGC1: AES-CBC senza autenticazione. Serve solo per leggere i dati scritti prima del passaggio alla libreria nativa.
    Private Shared Function DecifraPrecedente(valore As String, chiave As Byte()) As String
        Try
            Dim insieme As Byte() = Convert.FromBase64String(valore.Substring(PrefissoPrecedente.Length))
            If insieme.Length <= 16 Then Return ""

            Dim vettore(15) As Byte
            Array.Copy(insieme, 0, vettore, 0, 16)
            Dim cifrati(insieme.Length - 17) As Byte
            Array.Copy(insieme, 16, cifrati, 0, cifrati.Length)

            Using algoritmo As Aes = Aes.Create()
                algoritmo.KeySize = 256
                algoritmo.Mode = CipherMode.CBC
                algoritmo.Padding = PaddingMode.PKCS7
                algoritmo.Key = chiave
                algoritmo.IV = vettore
                Using trasformazione As ICryptoTransform = algoritmo.CreateDecryptor()
                    Dim chiari As Byte() = trasformazione.TransformFinalBlock(cifrati, 0, cifrati.Length)
                    Return Encoding.UTF8.GetString(chiari)
                End Using
            End Using
        Catch ex As CryptographicException
            Registra("WGCifratura.DecifraPrecedente chiave errata: " & ex.Message)
            Return ""
        Catch ex As Exception
            Registra("WGCifratura.DecifraPrecedente: " & ex.ToString())
            Return ""
        End Try
    End Function

    ' Formato WGC3 (riserva gestita): AES-256-CBC cifra-poi-autentica. insieme = IV(16) || cifrati || tag HMAC-SHA256(32),
    ' il tag copre IV e cifrati insieme, cosi' non si puo' alterare l'uno senza invalidare l'altro.
    Private Shared Function CifraRiserva(testo As String, chiave As Byte()) As String
        Try
            Using algoritmo As Aes = Aes.Create()
                algoritmo.KeySize = 256
                algoritmo.Mode = CipherMode.CBC
                algoritmo.Padding = PaddingMode.PKCS7
                algoritmo.Key = chiave
                algoritmo.GenerateIV()

                Dim datiChiari As Byte() = Encoding.UTF8.GetBytes(testo)
                Using trasformazione As ICryptoTransform = algoritmo.CreateEncryptor()
                    Dim cifrati As Byte() = trasformazione.TransformFinalBlock(datiChiari, 0, datiChiari.Length)
                    Dim daAutenticare(algoritmo.IV.Length + cifrati.Length - 1) As Byte
                    Array.Copy(algoritmo.IV, 0, daAutenticare, 0, algoritmo.IV.Length)
                    Array.Copy(cifrati, 0, daAutenticare, algoritmo.IV.Length, cifrati.Length)

                    Dim tag As Byte()
                    Using hmac As New HMACSHA256(ChiaveMacRiserva(chiave))
                        tag = hmac.ComputeHash(daAutenticare)
                    End Using

                    Dim insieme(daAutenticare.Length + tag.Length - 1) As Byte
                    Array.Copy(daAutenticare, 0, insieme, 0, daAutenticare.Length)
                    Array.Copy(tag, 0, insieme, daAutenticare.Length, tag.Length)
                    Return PrefissoRiserva & Convert.ToBase64String(insieme)
                End Using
            End Using
        Catch ex As Exception
            Registra("WGCifratura.CifraRiserva: " & ex.GetType().Name & ": " & ex.Message)
            Return testo
        End Try
    End Function

    Private Shared Function DecifraRiserva(valore As String, chiave As Byte()) As String
        Try
            Dim insieme As Byte() = Convert.FromBase64String(valore.Substring(PrefissoRiserva.Length))
            If insieme.Length <= 16 + LunghezzaTagMac Then Return ""

            Dim daAutenticare(insieme.Length - LunghezzaTagMac - 1) As Byte
            Array.Copy(insieme, 0, daAutenticare, 0, daAutenticare.Length)
            Dim tagRicevuto(LunghezzaTagMac - 1) As Byte
            Array.Copy(insieme, daAutenticare.Length, tagRicevuto, 0, LunghezzaTagMac)

            Dim tagCalcolato As Byte()
            Using hmac As New HMACSHA256(ChiaveMacRiserva(chiave))
                tagCalcolato = hmac.ComputeHash(daAutenticare)
            End Using
            ' Confronto a tempo costante: un tag che non corrisponde significa dati manomessi o chiave sbagliata.
            Dim differenza As Integer = 0
            For indice As Integer = 0 To LunghezzaTagMac - 1
                differenza = differenza Or (tagCalcolato(indice) Xor tagRicevuto(indice))
            Next
            If differenza <> 0 Then
                Registra("WGCifratura.DecifraRiserva: l'autenticazione non corrisponde (dati manomessi o chiave sbagliata).")
                Return ""
            End If

            Dim vettore(15) As Byte
            Array.Copy(daAutenticare, 0, vettore, 0, 16)
            Dim cifrati(daAutenticare.Length - 17) As Byte
            Array.Copy(daAutenticare, 16, cifrati, 0, cifrati.Length)

            Using algoritmo As Aes = Aes.Create()
                algoritmo.KeySize = 256
                algoritmo.Mode = CipherMode.CBC
                algoritmo.Padding = PaddingMode.PKCS7
                algoritmo.Key = chiave
                algoritmo.IV = vettore
                Using trasformazione As ICryptoTransform = algoritmo.CreateDecryptor()
                    Dim chiari As Byte() = trasformazione.TransformFinalBlock(cifrati, 0, cifrati.Length)
                    Return Encoding.UTF8.GetString(chiari)
                End Using
            End Using
        Catch ex As CryptographicException
            Registra("WGCifratura.DecifraRiserva chiave errata: " & ex.Message)
            Return ""
        Catch ex As Exception
            Registra("WGCifratura.DecifraRiserva: " & ex.ToString())
            Return ""
        End Try
    End Function

End Class
