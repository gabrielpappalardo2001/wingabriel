Imports System
Imports System.Collections.Generic
Imports System.Globalization
Imports System.Linq
Imports System.Security.Cryptography
Imports System.Text
Imports Newtonsoft.Json.Linq
Imports Org.BouncyCastle.Crypto.Generators
Imports Org.BouncyCastle.Crypto.Kems
Imports Org.BouncyCastle.Crypto.Macs
Imports Org.BouncyCastle.Crypto.Modes
Imports Org.BouncyCastle.Crypto.Parameters
Imports Org.BouncyCastle.Crypto.Digests
Imports Org.BouncyCastle.Math.EC.Rfc7748
Imports Org.BouncyCastle.Security

' ============================================================================================
' WGChat v1 - cifratura end to end della chat di WinGabriel (stessa del browser: scripts\server\chat\sorgenti\cripto.js)
' ============================================================================================
'
' Stretta di mano Noise IK (X25519) con in più ML-KEM-768 (ibrido post-quantistico), poi Double Ratchet con cifratura delle
' intestazioni (come nella specifica di Signal), ChaCha20-Poly1305 e SHA-256. Le primitive sono quelle di BouncyCastle: qui non
' c'è nessun algoritmo scritto a mano. Il formato dei blocchi (e quindi il protocollo) è identico
' a quello del browser (cripto.js): chi usa il programma e chi usa il browser si capiscono.
' I segreti (chiavi, stati delle sessioni) non lasciano mai il dispositivo e si salvano cifrati (vedi ArchivioChat).
' ============================================================================================

Public Class CoppiaChiavi
    Public Priv As Byte()
    Public Pub As Byte()
End Class

''' <summary>Un invito: il codice da dare a chi deve scrivere, e i segreti della cassetta (da tenere).</summary>
Public Class Invito
    Public Codice As String
    Public Mb As String
    Public Rd As String
    Public Tk As String
End Class

Public Class CodiceLetto
    Public IkPub As Byte()
    Public Mb As String
    Public Tk As String
End Class

Public Class RichiestaRicevuta
    Public IkI As Byte()
    Public Dati As JObject
    Public Stato As JObject
End Class

Public Class RispostaStretta
    Public Blocco As Byte()
    Public Stato As JObject
End Class

Public Class StrettaCompletata
    Public Sessione As JObject
    Public Dati As JObject
End Class

Public Class PrimoMessaggio
    Public Sessione As JObject
    Public Testo As Byte()
End Class

Public NotInheritable Class CriptoChat

    Private Sub New()
    End Sub

    Public Const MaxSkip As Integer = 1000
    Public Const MaxChiaviSaltate As Integer = 2000
    Public Const MassimoTesto As Integer = 4000
    Private Shared ReadOnly Blocchi As Integer() = {256, 512, 1024, 2048, 4096, 8192}
    Private Shared ReadOnly Zeri32 As Byte() = New Byte(31) {}
    Private Shared ReadOnly Rng As RandomNumberGenerator = RandomNumberGenerator.Create()
    Private Const LungEh As Integer = 12 + 40 + 16
    Private Const T_Stretta1 As Byte = 1
    Private Const T_Stretta2 As Byte = 2
    Private Const T_Msg As Byte = 3
    Private Const T_MsgIniziale As Byte = 4
    Private Const LungCtKem As Integer = 1088
    Private Const LungEkKem As Integer = 1184

    ' ------------------------------------------------------------------ utilità
    Public Shared Function Casuali(n As Integer) As Byte()
        Try
            Dim b(n - 1) As Byte
            Rng.GetBytes(b)
            Return b
        Catch ex As Exception
            RegistroErrori.Registra("CriptoChat.Casuali", ex)
            Throw
        End Try
    End Function

    Public Shared Function Concat(ParamArray parti As Byte()()) As Byte()
        Try
            Dim tot As Integer = 0
            For Each p As Byte() In parti
                tot += p.Length
            Next
            Dim r(tot - 1) As Byte
            Dim o As Integer = 0
            For Each p As Byte() In parti
                Buffer.BlockCopy(p, 0, r, o, p.Length)
                o += p.Length
            Next
            Return r
        Catch ex As Exception
            RegistroErrori.Registra("CriptoChat.Concat", ex)
            Throw
        End Try
    End Function

    Public Shared Function Parte(b As Byte(), inizio As Integer, Optional lunghezza As Integer = -1) As Byte()
        Try
            If lunghezza < 0 Then lunghezza = b.Length - inizio
            If inizio < 0 OrElse lunghezza < 0 OrElse inizio + lunghezza > b.Length Then Throw New InvalidOperationException("blocco troppo corto")
            Dim r(lunghezza - 1) As Byte
            Buffer.BlockCopy(b, inizio, r, 0, lunghezza)
            Return r
        Catch ex As Exception
            RegistroErrori.Registra("CriptoChat.Parte", ex)
            Throw
        End Try
    End Function

    Public Shared Function InHex(b As Byte()) As String
        Try
            Dim sb As New StringBuilder(b.Length * 2)
            For Each x As Byte In b
                sb.Append(x.ToString("x2", CultureInfo.InvariantCulture))
            Next
            Return sb.ToString()
        Catch ex As Exception
            RegistroErrori.Registra("CriptoChat.InHex", ex)
            Throw
        End Try
    End Function

    Public Shared Function DaHex(s As String) As Byte()
        Try
            If s Is Nothing OrElse s.Length Mod 2 <> 0 Then Throw New FormatException("esadecimale non valido")
            Dim r(s.Length \ 2 - 1) As Byte
            For i As Integer = 0 To r.Length - 1
                r(i) = Byte.Parse(s.Substring(i * 2, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture)
            Next
            Return r
        Catch ex As Exception
            RegistroErrori.Registra("CriptoChat.DaHex", ex)
            Throw
        End Try
    End Function

    Private Shared Function Sha(dati As Byte()) As Byte()
        Try
            Using h As SHA256 = SHA256.Create()
                Return h.ComputeHash(dati)
            End Using
        Catch ex As Exception
            RegistroErrori.Registra("CriptoChat.Sha", ex)
            Throw
        End Try
    End Function

    Private Shared Function Eguali(a As Byte(), b As Byte()) As Boolean
        Try
            If a.Length <> b.Length Then Return False
            Dim d As Integer = 0
            For i As Integer = 0 To a.Length - 1
                d = d Or (a(i) Xor b(i))
            Next
            Return d = 0
        Catch ex As Exception
            RegistroErrori.Registra("CriptoChat.Eguali", ex)
            Throw
        End Try
    End Function

    Private Shared Function U32(n As Integer) As Byte()
        Try
            Return BitConverter.GetBytes(CUInt(n And &HFFFFFFFFL)) ' little endian su Windows
        Catch ex As Exception
            RegistroErrori.Registra("CriptoChat.U32", ex)
            Throw
        End Try
    End Function

    Private Shared Function LeggiU32(b As Byte(), o As Integer) As Integer
        Try
            Return CInt(BitConverter.ToUInt32(b, o) And &H7FFFFFFFUI)
        Catch ex As Exception
            RegistroErrori.Registra("CriptoChat.LeggiU32", ex)
            Throw
        End Try
    End Function

    Private Shared Function Kdf(salt As Byte(), ikm As Byte(), info As String, lunghezza As Integer) As Byte()
        Try
            Return Kdf(salt, ikm, Encoding.UTF8.GetBytes(info), lunghezza)
        Catch ex As Exception
            RegistroErrori.Registra("CriptoChat.Kdf", ex)
            Throw
        End Try
    End Function

    Private Shared Function Kdf(salt As Byte(), ikm As Byte(), info As Byte(), lunghezza As Integer) As Byte()
        Try
            Dim g As New HkdfBytesGenerator(New Sha256Digest())
            g.Init(New HkdfParameters(ikm, salt, info))
            Dim o(lunghezza - 1) As Byte
            g.GenerateBytes(o, 0, lunghezza)
            Return o
        Catch ex As Exception
            RegistroErrori.Registra("CriptoChat.Kdf", ex)
            Throw
        End Try
    End Function

    Private Shared Function Hmac256(chiave As Byte(), dati As Byte()) As Byte()
        Try
            Dim m As New Org.BouncyCastle.Crypto.Macs.HMac(New Sha256Digest())
            m.Init(New KeyParameter(chiave))
            m.BlockUpdate(dati, 0, dati.Length)
            Dim o(m.GetMacSize() - 1) As Byte
            m.DoFinal(o, 0)
            Return o
        Catch ex As Exception
            RegistroErrori.Registra("CriptoChat.Hmac256", ex)
            Throw
        End Try
    End Function

    ' ------------------------------------------------------------------ primitive
    Private Shared Function Dh(priv As Byte(), pub As Byte()) As Byte()
        Try
            If pub.Length <> 32 OrElse priv.Length <> 32 Then Throw New CryptographicException("chiave non valida")
            Dim r(31) As Byte
            If Not X25519.CalculateAgreement(priv, 0, pub, 0, r, 0) Then Throw New CryptographicException("chiave pubblica non valida")
            Return r
        Catch ex As Exception
            RegistroErrori.Registra("CriptoChat.Dh", ex)
            Throw
        End Try
    End Function

    Private Shared Function NuovaCoppia() As CoppiaChiavi
        Try
            Dim priv As Byte() = Casuali(32)
            X25519.ClampPrivateKey(priv)
            Dim pub(31) As Byte
            X25519.ScalarMultBase(priv, 0, pub, 0)
            Return New CoppiaChiavi With {.Priv = priv, .Pub = pub}
        Catch ex As Exception
            RegistroErrori.Registra("CriptoChat.NuovaCoppia", ex)
            Throw
        End Try
    End Function

    Private Shared Function AeadCifra(chiave As Byte(), nonce As Byte(), ad As Byte(), testo As Byte()) As Byte()
        Try
            Dim c As New ChaCha20Poly1305()
            c.Init(True, New AeadParameters(New KeyParameter(chiave), 128, nonce, ad))
            Dim o(c.GetOutputSize(testo.Length) - 1) As Byte
            Dim n As Integer = c.ProcessBytes(testo, 0, testo.Length, o, 0)
            c.DoFinal(o, n)
            Return o
        Catch ex As Exception
            RegistroErrori.Registra("CriptoChat.AeadCifra", ex)
            Throw
        End Try
    End Function

    Private Shared Function AeadDecifra(chiave As Byte(), nonce As Byte(), ad As Byte(), cifrato As Byte()) As Byte()
        Try
            Dim c As New ChaCha20Poly1305()
            c.Init(False, New AeadParameters(New KeyParameter(chiave), 128, nonce, ad))
            Dim o(c.GetOutputSize(cifrato.Length) - 1) As Byte
            Dim n As Integer = c.ProcessBytes(cifrato, 0, cifrato.Length, o, 0)
            c.DoFinal(o, n)
            Return o
        Catch ex As Exception
            RegistroErrori.Registra("CriptoChat.AeadDecifra", ex)
            Throw
        End Try
    End Function

    ' ------------------------------------------------------------------ identità e inviti
    Public Shared Function CreaIdentita() As CoppiaChiavi
        Try
            Return NuovaCoppia()
        Catch ex As Exception
            RegistroErrori.Registra("CriptoChat.CreaIdentita", ex)
            Throw
        End Try
    End Function

    Private Const Alfabeto As String = "ABCDEFGHIJKLMNOPQRSTUVWXYZ234567"

    Private Shared Function InBase32(b As Byte()) As String
        Try
            Dim bits As Integer = 0, val As Integer = 0
            Dim sb As New StringBuilder()
            For Each x As Byte In b
                val = ((val << 8) Or x) And &HFFFFF
                bits += 8
                While bits >= 5
                    sb.Append(Alfabeto((val >> (bits - 5)) And 31))
                    bits -= 5
                End While
            Next
            If bits > 0 Then sb.Append(Alfabeto((val << (5 - bits)) And 31))
            Return sb.ToString()
        Catch ex As Exception
            RegistroErrori.Registra("CriptoChat.InBase32", ex)
            Throw
        End Try
    End Function

    Private Shared Function DaBase32(s As String) As Byte()
        Try
            Dim bits As Integer = 0, val As Integer = 0
            Dim o As New List(Of Byte)()
            For Each c As Char In s
                Dim i As Integer = Alfabeto.IndexOf(c)
                If i < 0 Then Throw New FormatException("Il codice contiene caratteri non validi.")
                val = ((val << 5) Or i) And &HFFFFF
                bits += 5
                If bits >= 8 Then
                    o.Add(CByte((val >> (bits - 8)) And 255))
                    bits -= 8
                End If
            Next
            Return o.ToArray()
        Catch ex As Exception
            RegistroErrori.Registra("CriptoChat.DaBase32", ex)
            Throw
        End Try
    End Function

    Public Shared Function CreaInvito(identita As CoppiaChiavi) As Invito
        Try
            Dim mb As Byte() = Casuali(16), rd As Byte() = Casuali(32), tk As Byte() = Casuali(32)
            Dim corpo As Byte() = Concat(identita.Pub, mb, tk)
            Dim controllo As Byte() = Parte(Sha(Concat(Encoding.UTF8.GetBytes("WGC1"), corpo)), 0, 4)
            Dim testo As String = InBase32(Concat(corpo, controllo))
            Dim gruppi As New List(Of String)()
            For i As Integer = 0 To testo.Length - 1 Step 6
                gruppi.Add(testo.Substring(i, Math.Min(6, testo.Length - i)))
            Next
            Return New Invito With {.Codice = "WGC1-" & String.Join("-", gruppi), .Mb = InHex(mb), .Rd = InHex(rd), .Tk = InHex(tk)}
        Catch ex As Exception
            RegistroErrori.Registra("CriptoChat.CreaInvito", ex)
            Throw
        End Try
    End Function

    ''' <summary>Legge un codice scritto da una persona: ignora maiuscole, trattini e spazi.</summary>
    Public Shared Function LeggiCodice(testo As String) As CodiceLetto
        Try
            Dim s As String = New String((testo & "").ToUpperInvariant().Where(Function(c) Not Char.IsWhiteSpace(c) AndAlso c <> "-"c).ToArray())
            If Not s.StartsWith("WGC1", StringComparison.Ordinal) Then Throw New FormatException("Il codice non è valido: deve cominciare con WGC1.")
            s = s.Substring(4)
            Dim b As Byte()
            Try
                b = DaBase32(s)
            Catch ex As FormatException
                Throw New FormatException("Il codice contiene caratteri non validi.")
            End Try
            If b.Length < 84 Then Throw New FormatException("Il codice è incompleto.")
            Dim corpo As Byte() = Parte(b, 0, 80)
            Dim controllo As Byte() = Parte(Sha(Concat(Encoding.UTF8.GetBytes("WGC1"), corpo)), 0, 4)
            If Not Eguali(controllo, Parte(b, 80, 4)) Then Throw New FormatException("Il codice non è corretto: controlla di averlo scritto giusto.")
            Return New CodiceLetto With {.IkPub = Parte(corpo, 0, 32), .Mb = InHex(Parte(corpo, 32, 16)), .Tk = InHex(Parte(corpo, 48, 32))}
        Catch ex As Exception
            RegistroErrori.Registra("CriptoChat.LeggiCodice", ex)
            Throw
        End Try
    End Function

    ''' <summary>Numero di sicurezza (60 cifre) da confrontare a voce o di persona: uguale per i due interlocutori.</summary>
    Public Shared Function NumeroDiSicurezza(ikA As Byte(), ikB As Byte()) As String
        Try
            Dim a As Byte() = ikA, b As Byte() = ikB
            If String.CompareOrdinal(InHex(ikA), InHex(ikB)) >= 0 Then
                a = ikB
                b = ikA
            End If
            Dim d As Byte() = Kdf(Zeri32, Concat(a, b), "WGChat-sicurezza", 48)
            Dim gruppi As New List(Of String)()
            For i As Integer = 0 To 11
                Dim n As UInteger = (CUInt(d(i * 4)) << 24) Or (CUInt(d(i * 4 + 1)) << 16) Or (CUInt(d(i * 4 + 2)) << 8) Or CUInt(d(i * 4 + 3))
                gruppi.Add((n Mod 100000UI).ToString(CultureInfo.InvariantCulture).PadLeft(5, "0"c))
            Next
            Return String.Join(" ", gruppi)
        Catch ex As Exception
            RegistroErrori.Registra("CriptoChat.NumeroDiSicurezza", ex)
            Throw
        End Try
    End Function

    ' ------------------------------------------------------------------ stato simmetrico (stile Noise)
    Private Class Simm
        Public Ck As Byte()
        Public H As Byte()
        Public K As Byte()
        Public N As Integer

        Public Sub New()
            Try
                Ck = Sha(Encoding.UTF8.GetBytes("WGChat/1 Noise_IK_25519+MLKEM768_ChaChaPoly_SHA256"))
                H = Sha(Concat(Ck, Encoding.UTF8.GetBytes("WGChat/1")))
                K = Nothing
                N = 0
            Catch ex As Exception
                RegistroErrori.Registra("CriptoChat.Simm.New", ex)
                Throw
            End Try
        End Sub

        Public Shared Function DaJson(o As JObject) As Simm
            Try
                Dim s As New Simm()
                s.Ck = DaHex(CStr(o("ck")))
                s.H = DaHex(CStr(o("h")))
                s.K = If(o("k") Is Nothing OrElse o("k").Type = JTokenType.Null, Nothing, DaHex(CStr(o("k"))))
                s.N = CInt(o("n"))
                Return s
            Catch ex As Exception
                RegistroErrori.Registra("CriptoChat.Simm.DaJson", ex)
                Throw
            End Try
        End Function

        Public Function InJson() As JObject
            Try
                Return New JObject From {{"ck", InHex(Ck)}, {"h", InHex(H)}, {"k", If(K Is Nothing, Nothing, InHex(K))}, {"n", N}}
            Catch ex As Exception
                RegistroErrori.Registra("CriptoChat.Simm.InJson", ex)
                Throw
            End Try
        End Function

        Public Sub MixHash(d As Byte())
            Try
                H = Sha(Concat(H, d))
            Catch ex As Exception
                RegistroErrori.Registra("CriptoChat.Simm.MixHash", ex)
                Throw
            End Try
        End Sub

        Public Sub MixKey(dhOut As Byte())
            Try
                Dim o As Byte() = Kdf(Ck, dhOut, "mk", 64)
                Ck = Parte(o, 0, 32)
                K = Parte(o, 32, 32)
                N = 0
            Catch ex As Exception
                RegistroErrori.Registra("CriptoChat.Simm.MixKey", ex)
                Throw
            End Try
        End Sub

        Private Function Nonce() As Byte()
            Try
                Dim n12(11) As Byte
                Dim c As Byte() = BitConverter.GetBytes(CUInt(N))
                Buffer.BlockCopy(c, 0, n12, 4, 4)
                Return n12
            Catch ex As Exception
                RegistroErrori.Registra("CriptoChat.Simm.Nonce", ex)
                Throw
            End Try
        End Function

        Public Function Cifra(testo As Byte()) As Byte()
            Try
                If K Is Nothing Then Throw New InvalidOperationException("nessuna chiave")
                Dim c As Byte() = AeadCifra(K, Nonce(), H, testo)
                N += 1
                MixHash(c)
                Return c
            Catch ex As Exception
                RegistroErrori.Registra("CriptoChat.Simm.Cifra", ex)
                Throw
            End Try
        End Function

        Public Function Decifra(c As Byte()) As Byte()
            Try
                If K Is Nothing Then Throw New InvalidOperationException("nessuna chiave")
                Dim t As Byte() = AeadDecifra(K, Nonce(), H, c)
                N += 1
                MixHash(c)
                Return t
            Catch ex As Exception
                RegistroErrori.Registra("CriptoChat.Simm.Decifra", ex)
                Throw
            End Try
        End Function
    End Class

    Private Shared Function JsonBytes(o As JObject) As Byte()
        Try
            Return Encoding.UTF8.GetBytes(o.ToString(Newtonsoft.Json.Formatting.None))
        Catch ex As Exception
            RegistroErrori.Registra("CriptoChat.JsonBytes", ex)
            Throw
        End Try
    End Function

    Private Shared Function BytesJson(b As Byte()) As JObject
        Try
            Return JObject.Parse(Encoding.UTF8.GetString(b))
        Catch ex As Exception
            RegistroErrori.Registra("CriptoChat.BytesJson", ex)
            Throw
        End Try
    End Function

    Private Shared Function Imbottisci(testo As Byte()) As Byte()
        Try
            Dim tot As Integer = 2 + testo.Length
            Dim dim2 As Integer = 0
            For Each b As Integer In Blocchi
                If b >= tot Then
                    dim2 = b
                    Exit For
                End If
            Next
            If dim2 = 0 Then Throw New InvalidOperationException("Il messaggio è troppo lungo.")
            Dim r(dim2 - 1) As Byte
            r(0) = CByte(testo.Length >> 8)
            r(1) = CByte(testo.Length And 255)
            Buffer.BlockCopy(testo, 0, r, 2, testo.Length)
            Return r
        Catch ex As Exception
            RegistroErrori.Registra("CriptoChat.Imbottisci", ex)
            Throw
        End Try
    End Function

    Private Shared Function Spoglia(b As Byte()) As Byte()
        Try
            Dim n As Integer = (CInt(b(0)) << 8) Or b(1)
            If 2 + n > b.Length Then Throw New CryptographicException("riempimento non valido")
            Return Parte(b, 2, n)
        Catch ex As Exception
            RegistroErrori.Registra("CriptoChat.Spoglia", ex)
            Throw
        End Try
    End Function

    ' ------------------------------------------------------------------ stretta di mano
    ''' <summary>Chi inizia (I): ha il codice di R. dati = { n: nome mostrato, mb, tk: la propria cassetta per le risposte }.</summary>
    Public Shared Function IniziaStretta(identitaI As CoppiaChiavi, codiceR As CodiceLetto, dati As JObject) As RispostaStretta
        Try
            Dim s As New Simm()
            s.MixHash(codiceR.IkPub)
            Dim e As CoppiaChiavi = NuovaCoppia()
            s.MixHash(e.Pub)
            s.MixKey(Dh(e.Priv, codiceR.IkPub))
            Dim c1 As Byte() = s.Cifra(identitaI.Pub)
            s.MixKey(Dh(identitaI.Priv, codiceR.IkPub))
            Dim c2 As Byte() = s.Cifra(Imbottisci(JsonBytes(dati)))
            Dim msg1 As Byte() = Concat(New Byte() {T_Stretta1}, e.Pub, c1, c2)
            Return New RispostaStretta With {.Blocco = msg1, .Stato = New JObject From {{"simm", s.InJson()}, {"ePriv", InHex(e.Priv)}, {"ePub", InHex(e.Pub)}, {"ikR", InHex(codiceR.IkPub)}}}
        Catch ex As Exception
            RegistroErrori.Registra("CriptoChat.IniziaStretta", ex)
            Throw
        End Try
    End Function

    ''' <summary>R riceve la richiesta: scopre chi è I (chiave e nome dichiarato). Non risponde ancora: decide l'utente.</summary>
    Public Shared Function RiceviStretta1(identitaR As CoppiaChiavi, msg1 As Byte()) As RichiestaRicevuta
        Try
            If msg1.Length < 1 + 32 + 48 + 16 OrElse msg1(0) <> T_Stretta1 Then Throw New CryptographicException("richiesta non valida")
            Dim s As New Simm()
            s.MixHash(identitaR.Pub)
            Dim eI As Byte() = Parte(msg1, 1, 32)
            s.MixHash(eI)
            s.MixKey(Dh(identitaR.Priv, eI))
            Dim ikI As Byte() = s.Decifra(Parte(msg1, 33, 48))
            s.MixKey(Dh(identitaR.Priv, ikI))
            Dim dati As JObject = BytesJson(Spoglia(s.Decifra(Parte(msg1, 81))))
            Return New RichiestaRicevuta With {.IkI = ikI, .Dati = dati, .Stato = New JObject From {{"simm", s.InJson()}, {"eIPub", InHex(eI)}, {"ikI", InHex(ikI)}}}
        Catch ex As Exception
            RegistroErrori.Registra("CriptoChat.RiceviStretta1", ex)
            Throw
        End Try
    End Function

    ''' <summary>R accetta: risponde con la sua chiave effimera, la chiave ML-KEM e la propria cassetta.</summary>
    Public Shared Function RispondiStretta(statoR As JObject, dati As JObject) As RispostaStretta
        Try
            Dim s As Simm = Simm.DaJson(CType(statoR("simm"), JObject))
            Dim eI As Byte() = DaHex(CStr(statoR("eIPub"))), ikI As Byte() = DaHex(CStr(statoR("ikI")))
            Dim e As CoppiaChiavi = NuovaCoppia()
            Dim gen As New MLKemKeyPairGenerator()
            gen.Init(New MLKemKeyGenerationParameters(New SecureRandom(), MLKemParameters.ml_kem_768))
            Dim kp As Org.BouncyCastle.Crypto.AsymmetricCipherKeyPair = gen.GenerateKeyPair()
            Dim ek As Byte() = CType(kp.Public, MLKemPublicKeyParameters).GetEncoded()
            Dim dk As Byte() = CType(kp.Private, MLKemPrivateKeyParameters).GetEncoded()
            s.MixHash(e.Pub)
            s.MixKey(Dh(e.Priv, eI))
            s.MixKey(Dh(e.Priv, ikI))
            Dim d2 As JObject = CType(dati.DeepClone(), JObject)
            d2("ek") = Convert.ToBase64String(ek)
            Dim c3 As Byte() = s.Cifra(Imbottisci(JsonBytes(d2)))
            Dim msg2 As Byte() = Concat(New Byte() {T_Stretta2}, e.Pub, c3)
            Return New RispostaStretta With {.Blocco = msg2, .Stato = New JObject From {{"simm", s.InJson()}, {"ePriv", InHex(e.Priv)}, {"ePub", InHex(e.Pub)}, {"kemSk", InHex(dk)}, {"ikI", CStr(statoR("ikI"))}}}
        Catch ex As Exception
            RegistroErrori.Registra("CriptoChat.RispondiStretta", ex)
            Throw
        End Try
    End Function

    ''' <summary>I riceve la risposta di R e crea la sessione. Restituisce anche i dati di R (nome e cassetta).</summary>
    Public Shared Function FinisciStretta(identitaI As CoppiaChiavi, statoI As JObject, msg2 As Byte()) As StrettaCompletata
        Try
            If msg2.Length < 1 + 32 + 16 OrElse msg2(0) <> T_Stretta2 Then Throw New CryptographicException("risposta non valida")
            Dim s As Simm = Simm.DaJson(CType(statoI("simm"), JObject))
            Dim eR As Byte() = Parte(msg2, 1, 32)
            s.MixHash(eR)
            s.MixKey(Dh(DaHex(CStr(statoI("ePriv"))), eR))
            s.MixKey(Dh(identitaI.Priv, eR))
            Dim dati As JObject = BytesJson(Spoglia(s.Decifra(Parte(msg2, 33))))
            Dim ek As Byte() = Convert.FromBase64String(CStr(dati("ek")))
            If ek.Length <> LungEkKem Then Throw New CryptographicException("chiave non valida")
            dati.Remove("ek")
            Dim enc As New MLKemEncapsulator(MLKemParameters.ml_kem_768)
            enc.Init(MLKemPublicKeyParameters.FromEncoding(MLKemParameters.ml_kem_768, ek))
            Dim ct(enc.EncapsulationLength - 1) As Byte
            Dim ss(enc.SecretLength - 1) As Byte
            enc.Encapsulate(ct, 0, ct.Length, ss, 0, ss.Length)
            Dim sk As Byte() = Kdf(s.Ck, ss, Concat(Encoding.UTF8.GetBytes("WGChat-sk"), s.H), 32)
            Dim sess As JObject = IniziaRatchetAlice(sk, s.H, eR)
            sess("prefisso") = InHex(ct)
            sess("ikRemota") = CStr(statoI("ikR"))
            Return New StrettaCompletata With {.Sessione = sess, .Dati = dati}
        Catch ex As Exception
            RegistroErrori.Registra("CriptoChat.FinisciStretta", ex)
            Throw
        End Try
    End Function

    ''' <summary>R riceve il primo messaggio di I (che porta il testo cifrato ML-KEM) e crea la sessione.</summary>
    Public Shared Function AccettaPrimoMessaggio(statoR As JObject, blob As Byte()) As PrimoMessaggio
        Try
            If blob.Length < 1 + LungCtKem + LungEh + 16 OrElse blob(0) <> T_MsgIniziale Then Throw New CryptographicException("messaggio non valido")
            Dim s As Simm = Simm.DaJson(CType(statoR("simm"), JObject))
            Dim dk As MLKemPrivateKeyParameters = MLKemPrivateKeyParameters.FromEncoding(MLKemParameters.ml_kem_768, DaHex(CStr(statoR("kemSk"))))
            Dim dec As New MLKemDecapsulator(MLKemParameters.ml_kem_768)
            dec.Init(dk)
            Dim ss(dec.SecretLength - 1) As Byte
            dec.Decapsulate(blob, 1, LungCtKem, ss, 0, ss.Length)
            Dim sk As Byte() = Kdf(s.Ck, ss, Concat(Encoding.UTF8.GetBytes("WGChat-sk"), s.H), 32)
            Dim coppia As New CoppiaChiavi With {.Priv = DaHex(CStr(statoR("ePriv"))), .Pub = DaHex(CStr(statoR("ePub")))}
            Dim sess As JObject = IniziaRatchetBob(sk, s.H, coppia)
            sess("ikRemota") = CStr(statoR("ikI"))
            Dim testo As Byte() = Decifra(sess, blob)
            Return New PrimoMessaggio With {.Sessione = sess, .Testo = testo}
        Catch ex As Exception
            RegistroErrori.Registra("CriptoChat.AccettaPrimoMessaggio", ex)
            Throw
        End Try
    End Function

    ' ------------------------------------------------------------------ Double Ratchet con cifratura delle intestazioni
    Private Shared Sub KdfRk(rk As Byte(), dhOut As Byte(), ByRef rk2 As Byte(), ByRef ck As Byte(), ByRef nhk As Byte())
        Try
            Dim o As Byte() = Kdf(rk, dhOut, "WGChat-rk", 96)
            rk2 = Parte(o, 0, 32)
            ck = Parte(o, 32, 32)
            nhk = Parte(o, 64, 32)
        Catch ex As Exception
            RegistroErrori.Registra("CriptoChat.KdfRk", ex)
            Throw
        End Try
    End Sub

    Private Shared Sub KdfCk(ck As Byte(), ByRef ck2 As Byte(), ByRef mk As Byte())
        Try
            ck2 = Hmac256(ck, New Byte() {2})
            mk = Hmac256(ck, New Byte() {1})
        Catch ex As Exception
            RegistroErrori.Registra("CriptoChat.KdfCk", ex)
            Throw
        End Try
    End Sub

    Private Shared Function HCifra(hk As Byte(), intestazione As Byte()) As Byte()
        Try
            Dim nonce As Byte() = Casuali(12)
            Return Concat(nonce, AeadCifra(hk, nonce, New Byte() {}, intestazione))
        Catch ex As Exception
            RegistroErrori.Registra("CriptoChat.HCifra", ex)
            Throw
        End Try
    End Function

    Private Shared Function HDecifra(hk As Byte(), eh As Byte()) As Byte()
        Try
            If hk Is Nothing Then Return Nothing
            Try
                Return AeadDecifra(hk, Parte(eh, 0, 12), New Byte() {}, Parte(eh, 12))
            Catch ex As Exception
                RegistroErrori.Registra("CriptoChat.HDecifra", ex)
                Return Nothing
            End Try
        Catch ex As Exception
            RegistroErrori.Registra("CriptoChat.HDecifra", ex)
            Throw
        End Try
    End Function

    Private Shared Sub ChiaveMessaggio(mk As Byte(), ByRef chiave As Byte(), ByRef nonce As Byte())
        Try
            Dim o As Byte() = Kdf(Zeri32, mk, "WGChat-mk", 44)
            chiave = Parte(o, 0, 32)
            nonce = Parte(o, 32, 12)
        Catch ex As Exception
            RegistroErrori.Registra("CriptoChat.ChiaveMessaggio", ex)
            Throw
        End Try
    End Sub

    Private Shared Function IniziaRatchetAlice(sk As Byte(), ad As Byte(), dhrPub As Byte()) As JObject
        Try
            Dim hka As Byte() = Kdf(Zeri32, sk, "WGChat-hka", 32), nhkb As Byte() = Kdf(Zeri32, sk, "WGChat-nhkb", 32)
            Dim dhs As CoppiaChiavi = NuovaCoppia()
            Dim rk As Byte() = Nothing, cks As Byte() = Nothing, nhks As Byte() = Nothing
            KdfRk(sk, Dh(dhs.Priv, dhrPub), rk, cks, nhks)
            Return NuovaSessione(ad, dhs, dhrPub, rk, cks, Nothing, hka, Nothing, nhks, nhkb)
        Catch ex As Exception
            RegistroErrori.Registra("CriptoChat.IniziaRatchetAlice", ex)
            Throw
        End Try
    End Function

    Private Shared Function IniziaRatchetBob(sk As Byte(), ad As Byte(), coppia As CoppiaChiavi) As JObject
        Try
            Dim hka As Byte() = Kdf(Zeri32, sk, "WGChat-hka", 32), nhkb As Byte() = Kdf(Zeri32, sk, "WGChat-nhkb", 32)
            Return NuovaSessione(ad, coppia, Nothing, sk, Nothing, Nothing, Nothing, Nothing, nhkb, hka)
        Catch ex As Exception
            RegistroErrori.Registra("CriptoChat.IniziaRatchetBob", ex)
            Throw
        End Try
    End Function

    Private Shared Function Esa(b As Byte()) As JToken
        Try
            If b Is Nothing Then Return JValue.CreateNull()
            Return New JValue(InHex(b))
        Catch ex As Exception
            RegistroErrori.Registra("CriptoChat.Esa", ex)
            Throw
        End Try
    End Function

    Private Shared Function NuovaSessione(ad As Byte(), dhs As CoppiaChiavi, dhr As Byte(), rk As Byte(), cks As Byte(), ckr As Byte(), hks As Byte(), hkr As Byte(), nhks As Byte(), nhkr As Byte()) As JObject
        Try
            Return New JObject From {{"ad", InHex(ad)}, {"dhsPriv", InHex(dhs.Priv)}, {"dhsPub", InHex(dhs.Pub)}, {"dhr", Esa(dhr)}, {"rk", Esa(rk)}, {"cks", Esa(cks)}, {"ckr", Esa(ckr)},
                {"ns", 0}, {"nr", 0}, {"pn", 0}, {"hks", Esa(hks)}, {"hkr", Esa(hkr)}, {"nhks", Esa(nhks)}, {"nhkr", Esa(nhkr)}, {"saltate", New JObject()}, {"prefisso", JValue.CreateNull()}, {"ikRemota", JValue.CreateNull()}}
        Catch ex As Exception
            RegistroErrori.Registra("CriptoChat.NuovaSessione", ex)
            Throw
        End Try
    End Function

    Private Shared Function Esadecimale(t As JToken) As Byte()
        Try
            If t Is Nothing OrElse t.Type = JTokenType.Null Then Return Nothing
            Return DaHex(CStr(t))
        Catch ex As Exception
            RegistroErrori.Registra("CriptoChat.Esadecimale", ex)
            Throw
        End Try
    End Function

    ''' <summary>Cifra un messaggio per l'altro: restituisce il blocco da inviare.</summary>
    Public Shared Function Cifra(sess As JObject, testoBytes As Byte()) As Byte()
        Try
            Dim ad As Byte() = Esadecimale(sess("ad"))
            Dim ckAttuale As Byte() = Esadecimale(sess("cks"))
            If ckAttuale Is Nothing Then Throw New InvalidOperationException("La sessione non può ancora inviare.")
            Dim ck2 As Byte() = Nothing, mk As Byte() = Nothing
            KdfCk(ckAttuale, ck2, mk)
            Dim ns As Integer = CInt(sess("ns"))
            Dim intestazione As Byte() = Concat(Esadecimale(sess("dhsPub")), U32(CInt(sess("pn"))), U32(ns))
            Dim eh As Byte() = HCifra(Esadecimale(sess("hks")), intestazione)
            Dim chiave As Byte() = Nothing, nonce As Byte() = Nothing
            ChiaveMessaggio(mk, chiave, nonce)
            Dim corpo As Byte() = AeadCifra(chiave, nonce, Concat(ad, eh), Imbottisci(testoBytes))
            sess("cks") = InHex(ck2)
            sess("ns") = ns + 1
            Dim prefisso As Byte() = Esadecimale(sess("prefisso"))
            If prefisso IsNot Nothing Then Return Concat(New Byte() {T_MsgIniziale}, prefisso, eh, corpo)
            Return Concat(New Byte() {T_Msg}, eh, corpo)
        Catch ex As Exception
            RegistroErrori.Registra("CriptoChat.Cifra", ex)
            Throw
        End Try
    End Function

    ''' <summary>Decifra un blocco ricevuto. Se non va bene lancia un errore e la sessione resta com'era.</summary>
    Public Shared Function Decifra(sess As JObject, blob As Byte()) As Byte()
        Try
            If blob.Length < 1 + LungEh + 16 Then Throw New CryptographicException("messaggio troppo corto")
            Dim inizio As Integer = 1
            If blob(0) = T_MsgIniziale Then
                inizio = 1 + LungCtKem
            ElseIf blob(0) <> T_Msg Then
                Throw New CryptographicException("tipo di messaggio sconosciuto")
            End If
            Dim eh As Byte() = Parte(blob, inizio, LungEh)
            Dim corpo As Byte() = Parte(blob, inizio + LungEh)
            Dim st As JObject = CType(sess.DeepClone(), JObject)
            Dim testo As Byte() = DecifraHE(st, eh, corpo)
            st("prefisso") = JValue.CreateNull()
            sess.RemoveAll()
            For Each p As JProperty In st.Properties().ToList()
                sess.Add(p.Name, p.Value)
            Next
            Return testo
        Catch ex As Exception
            RegistroErrori.Registra("CriptoChat.Decifra", ex)
            Throw
        End Try
    End Function

    Private Shared Function DecifraHE(st As JObject, eh As Byte(), corpo As Byte()) As Byte()
        Try
            Dim ad As Byte() = Esadecimale(st("ad"))
            Dim saltate As JObject = CType(st("saltate"), JObject)
            ' 1. chiavi saltate
            For Each p As JProperty In saltate.Properties().ToList()
                Dim parti As String() = p.Name.Split(":"c)
                Dim intest As Byte() = HDecifra(DaHex(parti(0)), eh)
                If intest IsNot Nothing AndAlso LeggiU32(intest, 36) = Integer.Parse(parti(1), CultureInfo.InvariantCulture) Then
                    Dim chiave As Byte() = Nothing, nonce As Byte() = Nothing
                    ChiaveMessaggio(DaHex(CStr(p.Value)), chiave, nonce)
                    Dim t As Byte() = Spoglia(AeadDecifra(chiave, nonce, Concat(ad, eh), corpo))
                    saltate.Remove(p.Name)
                    Return t
                End If
            Next
            ' 2. intestazione
            Dim intestazione As Byte() = HDecifra(Esadecimale(st("hkr")), eh)
            Dim nuovoDh As Boolean = False
            If intestazione Is Nothing Then
                intestazione = HDecifra(Esadecimale(st("nhkr")), eh)
                nuovoDh = True
            End If
            If intestazione Is Nothing Then Throw New CryptographicException("messaggio non decifrabile")
            Dim dhPub As Byte() = Parte(intestazione, 0, 32)
            Dim pn As Integer = LeggiU32(intestazione, 32), n As Integer = LeggiU32(intestazione, 36)
            If nuovoDh Then
                Salta(st, pn)
                st("pn") = st("ns")
                st("ns") = 0
                st("nr") = 0
                st("hks") = st("nhks")
                st("hkr") = st("nhkr")
                st("dhr") = InHex(dhPub)
                Dim rk2 As Byte() = Nothing, ck As Byte() = Nothing, nhk As Byte() = Nothing
                KdfRk(Esadecimale(st("rk")), Dh(Esadecimale(st("dhsPriv")), dhPub), rk2, ck, nhk)
                st("rk") = InHex(rk2)
                st("ckr") = InHex(ck)
                st("nhkr") = InHex(nhk)
                Dim nuova As CoppiaChiavi = NuovaCoppia()
                st("dhsPriv") = InHex(nuova.Priv)
                st("dhsPub") = InHex(nuova.Pub)
                KdfRk(rk2, Dh(nuova.Priv, dhPub), rk2, ck, nhk)
                st("rk") = InHex(rk2)
                st("cks") = InHex(ck)
                st("nhks") = InHex(nhk)
            End If
            Salta(st, n)
            Dim ck3 As Byte() = Nothing, mk As Byte() = Nothing
            KdfCk(Esadecimale(st("ckr")), ck3, mk)
            st("ckr") = InHex(ck3)
            st("nr") = CInt(st("nr")) + 1
            Dim k As Byte() = Nothing, nn As Byte() = Nothing
            ChiaveMessaggio(mk, k, nn)
            Return Spoglia(AeadDecifra(k, nn, Concat(ad, eh), corpo))
        Catch ex As Exception
            RegistroErrori.Registra("CriptoChat.DecifraHE", ex)
            Throw
        End Try
    End Function

    Private Shared Sub Salta(st As JObject, fino As Integer)
        Try
            If CInt(st("nr")) + MaxSkip < fino Then Throw New CryptographicException("troppi messaggi saltati")
            If st("ckr") Is Nothing OrElse st("ckr").Type = JTokenType.Null Then Return
            Dim saltate As JObject = CType(st("saltate"), JObject)
            While CInt(st("nr")) < fino
                Dim ck2 As Byte() = Nothing, mk As Byte() = Nothing
                KdfCk(Esadecimale(st("ckr")), ck2, mk)
                st("ckr") = InHex(ck2)
                saltate(CStr(st("hkr")) & ":" & CInt(st("nr")).ToString(CultureInfo.InvariantCulture)) = InHex(mk)
                st("nr") = CInt(st("nr")) + 1
            End While
            Dim chiavi As List(Of JProperty) = saltate.Properties().ToList()
            If chiavi.Count > MaxChiaviSaltate Then
                For Each p As JProperty In chiavi.Take(chiavi.Count - MaxChiaviSaltate)
                    saltate.Remove(p.Name)
                Next
            End If
        Catch ex As Exception
            RegistroErrori.Registra("CriptoChat.Salta", ex)
            Throw
        End Try
    End Sub

    ' ------------------------------------------------------------------ messaggi dell'applicazione
    Public Shared Function TestoAByte(o As JObject) As Byte()
        Try
            Return JsonBytes(o)
        Catch ex As Exception
            RegistroErrori.Registra("CriptoChat.TestoAByte", ex)
            Throw
        End Try
    End Function

    Public Shared Function ByteATesto(b As Byte()) As JObject
        Try
            Return BytesJson(b)
        Catch ex As Exception
            RegistroErrori.Registra("CriptoChat.ByteATesto", ex)
            Throw
        End Try
    End Function

    ''' <summary>Impronta SHA-256 (esadecimale): riconosce i blocchi già visti e fa l'impronta del gettone di invio.</summary>
    Public Shared Function ShaHex(b As Byte()) As String
        Try
            Return InHex(Sha(b))
        Catch ex As Exception
            RegistroErrori.Registra("CriptoChat.ShaHex", ex)
            Throw
        End Try
    End Function

    Public Shared Function NuovoIdMessaggio() As String
        Try
            Return InHex(Casuali(8))
        Catch ex As Exception
            RegistroErrori.Registra("CriptoChat.NuovoIdMessaggio", ex)
            Throw
        End Try
    End Function

    ''' <summary>Tipo di un blocco ricevuto: "stretta1", "stretta2" o "messaggio".</summary>
    Public Shared Function TipoBlocco(blob As Byte()) As String
        Try
            If blob Is Nothing OrElse blob.Length = 0 Then Return "sconosciuto"
            Select Case blob(0)
                Case T_Stretta1 : Return "stretta1"
                Case T_Stretta2 : Return "stretta2"
                Case T_Msg, T_MsgIniziale : Return "messaggio"
            End Select
            Return "sconosciuto"
        Catch ex As Exception
            RegistroErrori.Registra("CriptoChat.TipoBlocco", ex)
            Throw
        End Try
    End Function

End Class
