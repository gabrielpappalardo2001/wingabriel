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

' Comunicazione interprocesso tramite named pipe con messaggi strutturati per l'interazione con il servizio di sistema C++.
Public Class WGServiceInterop
    Public Shared Function InviaComando(comando As String) As String
        Try
            Using pipeClient As New IO.Pipes.NamedPipeClientStream(".", "WGServicePipe", IO.Pipes.PipeDirection.InOut)
                pipeClient.Connect(2000)
                pipeClient.ReadMode = IO.Pipes.PipeTransmissionMode.Message

                ' Niente StreamWriter/StreamReader: il servizio C++ crea la pipe con PIPE_TYPE_MESSAGE
                ' e fa una SOLA ReadFile() per connessione, quindi tutto il comando deve arrivare in
                ' un'unica scrittura fisica sulla pipe. Encoding.UTF8 (usato di default anche dal vecchio
                ' New StreamWriter(pipeClient)) emette pero' un BOM iniziale come scrittura separata dal
                ' testo: la pipe riceveva cosi' due messaggi distinti, il server leggeva solo il primo
                ' (i 3 byte del BOM), lo loggava come "Comando sconosciuto: ﻿" e chiudeva subito la
                ' connessione, scartando il comando vero che arrivava dopo su una pipe ormai disconnessa
                ' (da cui anche gli ObjectDisposedException "pipe chiusa" visti sul client). Con
                ' UTF8Encoding(False) (nessun BOM) e un'unica Write(), comando e newline finiscono in un
                ' solo messaggio, esattamente come si aspetta il server.
                Dim encodingSenzaBom As New Text.UTF8Encoding(False)
                Dim byteComando As Byte() = encodingSenzaBom.GetBytes(comando & vbCrLf)
                pipeClient.Write(byteComando, 0, byteComando.Length)
                pipeClient.Flush()

                ' Lettura della risposta: in modalita' messaggio un Read() puo' restituire solo un
                ' pezzo del messaggio se il buffer e' piu' piccolo della risposta, quindi si continua
                ' finche' IsMessageComplete non segnala che il messaggio e' stato letto per intero.
                Dim rispostaCompleta As New Text.StringBuilder()
                Dim bufferRisposta(511) As Byte
                Do
                    Dim byteLetti As Integer = pipeClient.Read(bufferRisposta, 0, bufferRisposta.Length)
                    If byteLetti <= 0 Then Exit Do
                    rispostaCompleta.Append(encodingSenzaBom.GetString(bufferRisposta, 0, byteLetti))
                Loop While Not pipeClient.IsMessageComplete

                Return rispostaCompleta.ToString().TrimEnd(ChrW(13), ChrW(10))
            End Using
        Catch ex As Exception

            Return "ERROR|" & ex.Message
        End Try
    End Function
End Class
