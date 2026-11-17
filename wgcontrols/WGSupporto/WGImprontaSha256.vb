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

' Calcolo delle impronte digitali SHA-256 per i file. Implementa l'algoritmo di sicurezza per la verifica dei binari.
Public NotInheritable Class WGImprontaSha256

    Private Shared ReadOnly _costanti As UInteger() = New UInteger() {
                &H428A2F98UI, &H71374491UI, &HB5C0FBCFUI, &HE9B5DBA5UI,
                &H3956C25BUI, &H59F111F1UI, &H923F82A4UI, &HAB1C5ED5UI,
                &HD807AA98UI, &H12835B01UI, &H243185BEUI, &H550C7DC3UI,
                &H72BE5D74UI, &H80DEB1FEUI, &H9BDC06A7UI, &HC19BF174UI,
                &HE49B69C1UI, &HEFBE4786UI, &HFC19DC6UI, &H240CA1CCUI,
                &H2DE92C6FUI, &H4A7484AAUI, &H5CB0A9DCUI, &H76F988DAUI,
                &H983E5152UI, &HA831C66DUI, &HB00327C8UI, &HBF597FC7UI,
                &HC6E00BF3UI, &HD5A79147UI, &H6CA6351UI, &H14292967UI,
                &H27B70A85UI, &H2E1B2138UI, &H4D2C6DFCUI, &H53380D13UI,
                &H650A7354UI, &H766A0ABBUI, &H81C2C92EUI, &H92722C85UI,
                &HA2BFE8A1UI, &HA81A664BUI, &HC24B8B70UI, &HC76C51A3UI,
                &HD192E819UI, &HD6990624UI, &HF40E3585UI, &H106AA070UI,
                &H19A4C116UI, &H1E376C08UI, &H2748774CUI, &H34B0BCB5UI,
                &H391C0CB3UI, &H4ED8AA4AUI, &H5B9CCA4FUI, &H682E6FF3UI,
                &H748F82EEUI, &H78A5636FUI, &H84C87814UI, &H8CC70208UI,
                &H90BEFFFAUI, &HA4506CEBUI, &HBEF9A3F7UI, &HC67178F2UI
            }

    Private Const DIMENSIONE_BUFFER_LETTURA As Integer = 1048576

    <ThreadStatic()> Private Shared _statoBuffer As UInteger()
    <ThreadStatic()> Private Shared _residuoBuffer As Byte()
    <ThreadStatic()> Private Shared _paroleBuffer As UInteger()

    Private ReadOnly _stato As UInteger()
    Private ReadOnly _residuo As Byte()
    Private ReadOnly _parole As UInteger()
    Private _lunghezzaResiduo As Integer = 0
    Private _byteTotali As Long = 0

    Public Sub New()
        If _statoBuffer Is Nothing Then
            _statoBuffer = New UInteger(7) {}
            _residuoBuffer = New Byte(63) {}
            _paroleBuffer = New UInteger(63) {}
        End If

        _stato = _statoBuffer
        _residuo = _residuoBuffer
        _parole = _paroleBuffer

        _stato(0) = &H6A09E667UI
        _stato(1) = &HBB67AE85UI
        _stato(2) = &H3C6EF372UI
        _stato(3) = &HA54FF53AUI
        _stato(4) = &H510E527FUI
        _stato(5) = &H9B05688CUI
        _stato(6) = &H1F83D9ABUI
        _stato(7) = &H5BE0CD19UI
        _lunghezzaResiduo = 0
        _byteTotali = 0
    End Sub

    <MethodImpl(MethodImplOptions.AggressiveInlining)>
    Private Shared Function RuotaDestra(ByVal valore As UInteger, ByVal posizioni As Integer) As UInteger
        Return (valore >> posizioni) Or (valore << (32 - posizioni))
    End Function

    <MethodImpl(MethodImplOptions.AggressiveInlining)>
    Private Shared Function Somma(ByVal a As UInteger, ByVal b As UInteger) As UInteger
        Return CUInt((CULng(a) + CULng(b)) And &HFFFFFFFFUL)
    End Function

    Private Sub ElaboraBlocco(ByVal dati As Byte(), ByVal partenza As Integer)
        Dim indice As Integer
        Dim stato As UInteger() = _stato
        Dim parole As UInteger() = _parole
        Dim costanti As UInteger() = _costanti

        For indice = 0 To 15
            Dim posizione As Integer = partenza + (indice * 4)
            parole(indice) = (CUInt(dati(posizione)) << 24) Or (CUInt(dati(posizione + 1)) << 16) Or (CUInt(dati(posizione + 2)) << 8) Or CUInt(dati(posizione + 3))
        Next

        For indice = 16 To 63
            Dim precedente As UInteger = parole(indice - 15)
            Dim s0 As UInteger = RuotaDestra(precedente, 7) Xor RuotaDestra(precedente, 18) Xor (precedente >> 3)

            Dim ultima As UInteger = parole(indice - 2)
            Dim s1 As UInteger = RuotaDestra(ultima, 17) Xor RuotaDestra(ultima, 19) Xor (ultima >> 10)

            parole(indice) = Somma(Somma(parole(indice - 16), s0), Somma(parole(indice - 7), s1))
        Next

        Dim a As UInteger = stato(0)
        Dim b As UInteger = stato(1)
        Dim c As UInteger = stato(2)
        Dim d As UInteger = stato(3)
        Dim e As UInteger = stato(4)
        Dim f As UInteger = stato(5)
        Dim g As UInteger = stato(6)
        Dim h As UInteger = stato(7)

        For indice = 0 To 63
            Dim sigma1 As UInteger = RuotaDestra(e, 6) Xor RuotaDestra(e, 11) Xor RuotaDestra(e, 25)
            Dim scelta As UInteger = (e And f) Xor ((Not e) And g)
            Dim t1 As UInteger = Somma(Somma(Somma(h, sigma1), Somma(scelta, costanti(indice))), parole(indice))

            Dim sigma0 As UInteger = RuotaDestra(a, 2) Xor RuotaDestra(a, 13) Xor RuotaDestra(a, 22)
            Dim maggioranza As UInteger = (a And b) Xor (a And c) Xor (b And c)
            Dim t2 As UInteger = Somma(sigma0, maggioranza)

            h = g
            g = f
            f = e
            e = Somma(d, t1)
            d = c
            c = b
            b = a
            a = Somma(t1, t2)
        Next

        stato(0) = Somma(stato(0), a)
        stato(1) = Somma(stato(1), b)
        stato(2) = Somma(stato(2), c)
        stato(3) = Somma(stato(3), d)
        stato(4) = Somma(stato(4), e)
        stato(5) = Somma(stato(5), f)
        stato(6) = Somma(stato(6), g)
        stato(7) = Somma(stato(7), h)
    End Sub

    Private Sub AggiungiDati(ByVal dati As Byte(), ByVal lunghezza As Integer, ByVal conteggia As Boolean)
        If lunghezza <= 0 Then
            Return
        End If

        If conteggia Then
            _byteTotali += lunghezza
        End If

        Dim posizione As Integer = 0

        If _lunghezzaResiduo > 0 Then
            Dim mancanti As Integer = 64 - _lunghezzaResiduo

            If lunghezza < mancanti Then
                Buffer.BlockCopy(dati, 0, _residuo, _lunghezzaResiduo, lunghezza)
                _lunghezzaResiduo += lunghezza
                Return
            End If

            Buffer.BlockCopy(dati, 0, _residuo, _lunghezzaResiduo, mancanti)
            ElaboraBlocco(_residuo, 0)
            posizione = mancanti
            _lunghezzaResiduo = 0
        End If

        While posizione + 64 <= lunghezza
            ElaboraBlocco(dati, posizione)
            posizione += 64
        End While

        Dim resto As Integer = lunghezza - posizione

        If resto > 0 Then
            Buffer.BlockCopy(dati, posizione, _residuo, 0, resto)
            _lunghezzaResiduo = resto
        End If
    End Sub

    Public Sub Aggiorna(ByVal dati As Byte(), ByVal lunghezza As Integer)
        AggiungiDati(dati, lunghezza, True)
    End Sub

    Public Function Finalizza() As String
        Dim bitTotali As Long = _byteTotali * 8L

        Dim terminatore As Byte() = New Byte(0) {}
        terminatore(0) = &H80
        AggiungiDati(terminatore, 1, False)

        Dim riempimento As Byte() = New Byte(0) {}
        riempimento(0) = 0

        While _lunghezzaResiduo <> 56
            AggiungiDati(riempimento, 1, False)
        End While

        Dim lunghezzaFinale As Byte() = New Byte(7) {}
        Dim indice As Integer

        For indice = 0 To 7
            lunghezzaFinale(indice) = CByte((bitTotali >> (56 - (8 * indice))) And &HFFL)
        Next

        AggiungiDati(lunghezzaFinale, 8, False)

        Dim risultato As New Text.StringBuilder(64)

        For indice = 0 To 7
            risultato.Append(_stato(indice).ToString("x8", Globalization.CultureInfo.InvariantCulture))
        Next

        Return risultato.ToString()
    End Function

    <ThreadStatic()> Private Shared _bufferLetturaCondiviso As Byte()

    Public Shared Function DaFile(ByVal percorsoFile As String) As String
        Dim calcolatore As New WGImprontaSha256()

        If _bufferLetturaCondiviso Is Nothing Then
            _bufferLetturaCondiviso = New Byte(DIMENSIONE_BUFFER_LETTURA - 1) {}
        End If

        Dim buffer As Byte() = _bufferLetturaCondiviso

        Using flusso As New IO.FileStream(percorsoFile, IO.FileMode.Open, IO.FileAccess.Read, IO.FileShare.ReadWrite, DIMENSIONE_BUFFER_LETTURA, IO.FileOptions.SequentialScan)
            Dim letti As Integer = flusso.Read(buffer, 0, DIMENSIONE_BUFFER_LETTURA)

            While letti > 0
                calcolatore.Aggiorna(buffer, letti)
                letti = flusso.Read(buffer, 0, DIMENSIONE_BUFFER_LETTURA)
            End While
        End Using

        Return calcolatore.Finalizza()
    End Function

End Class
