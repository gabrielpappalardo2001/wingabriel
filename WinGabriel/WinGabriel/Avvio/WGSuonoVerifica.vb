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

' Riproduzione dei suoni di verifica all'avvio con gestione dei cicli audio.
Public NotInheritable Class WGSuonoVerifica

    Private Declare Auto Function midiOutOpen Lib "winmm.dll" (ByRef lphMidiOut As IntPtr, ByVal uDeviceID As Integer, ByVal dwCallback As IntPtr, ByVal dwInstance As IntPtr, ByVal dwFlags As Integer) As Integer
    Private Declare Auto Function midiOutClose Lib "winmm.dll" (ByVal hMidiOut As IntPtr) As Integer
    Private Declare Auto Function midiOutShortMsg Lib "winmm.dll" (ByVal hMidiOut As IntPtr, ByVal dwMsg As UInteger) As Integer
    Private Declare Auto Function midiOutReset Lib "winmm.dll" (ByVal hMidiOut As IntPtr) As Integer

    Private Const MIDI_MAPPER As Integer = -1
    Private Const STATO_PROGRAMMA As UInteger = &HC0UI
    Private Const STATO_NOTE_ON As UInteger = &H90UI
    Private Const STATO_NOTE_OFF As UInteger = &H80UI
    Private Const STRUMENTO_ATTESA As UInteger = 11UI

    Private Shared handleMidi As IntPtr = IntPtr.Zero
    Private Shared threadCiclo As Threading.Thread = Nothing
    Private Shared inEsecuzione As Boolean = False
    Private Shared ReadOnly sincronizzatore As New Object()

    Public Shared Sub Avvia()
        Try
            SyncLock sincronizzatore
                If inEsecuzione Then
                    Return
                End If

                Dim risultatoApertura As Integer = midiOutOpen(handleMidi, MIDI_MAPPER, IntPtr.Zero, IntPtr.Zero, 0)

                If risultatoApertura <> 0 OrElse handleMidi = IntPtr.Zero Then
                    handleMidi = IntPtr.Zero
                    Return
                End If

                ImpostaStrumento(STRUMENTO_ATTESA)

                inEsecuzione = True
                threadCiclo = New Threading.Thread(AddressOf CicloRiproduzione)
                threadCiclo.IsBackground = True
                threadCiclo.Priority = Threading.ThreadPriority.BelowNormal
                threadCiclo.Start()
            End SyncLock
        Catch ex As Exception
            ScriviLogAvvio("WGSuonoVerifica.Avvia eccezione: " & ex.Message & Environment.NewLine & ex.StackTrace)
            inEsecuzione = False
        End Try
    End Sub

    Public Shared Sub Ferma()
        Try
            SyncLock sincronizzatore
                inEsecuzione = False
            End SyncLock

            Try
                If threadCiclo IsNot Nothing AndAlso threadCiclo.IsAlive Then
                    threadCiclo.Join(600)
                End If
            Catch exAttesa As Exception
                ScriviLogAvvio("WGSuonoVerifica.Ferma (attesa thread): " & exAttesa.Message)
            End Try

            SyncLock sincronizzatore
                Try
                    If handleMidi <> IntPtr.Zero Then
                        midiOutReset(handleMidi)
                        midiOutClose(handleMidi)
                    End If
                Catch exChiusura As Exception
                    ScriviLogAvvio("WGSuonoVerifica.Ferma (chiusura dispositivo): " & exChiusura.Message)
                Finally
                    handleMidi = IntPtr.Zero
                    threadCiclo = Nothing
                End Try
            End SyncLock
        Catch ex As Exception
            ScriviLogAvvio("WGSuonoVerifica.Ferma eccezione: " & ex.Message & Environment.NewLine & ex.StackTrace)
        End Try
    End Sub

    Private Shared Sub CicloRiproduzione()
        Try
            Dim note() As Byte = {67, 72}
            Dim indice As Integer = 0

            While inEsecuzione
                Try
                    Dim handleLocale As IntPtr = handleMidi

                    If handleLocale = IntPtr.Zero Then
                        Exit While
                    End If

                    Dim nota As Byte = note(indice Mod note.Length)
                    indice += 1

                    Dim messaggioOn As UInteger = STATO_NOTE_ON Or (CUInt(nota) << 8) Or (CUInt(34) << 16)
                    midiOutShortMsg(handleLocale, messaggioOn)

                    Threading.Thread.Sleep(120)

                    Dim messaggioOff As UInteger = STATO_NOTE_OFF Or (CUInt(nota) << 8)
                    midiOutShortMsg(handleLocale, messaggioOff)

                    Dim attesaResidua As Integer = 1300

                    While attesaResidua > 0 AndAlso inEsecuzione
                        Dim passo As Integer = Math.Min(150, attesaResidua)
                        Threading.Thread.Sleep(passo)
                        attesaResidua -= passo
                    End While
                Catch exNota As Exception
                    ScriviLogAvvio("WGSuonoVerifica.CicloRiproduzione (nota): " & exNota.Message)
                    Exit While
                End Try
            End While
        Catch ex As Exception
            ScriviLogAvvio("WGSuonoVerifica.CicloRiproduzione eccezione: " & ex.Message & Environment.NewLine & ex.StackTrace)
        End Try
    End Sub

    Private Shared Sub ImpostaStrumento(ByVal strumento As UInteger)
        Try
            Dim messaggio As UInteger = STATO_PROGRAMMA Or (strumento << 8)
            midiOutShortMsg(handleMidi, messaggio)
        Catch ex As Exception
            ScriviLogAvvio("WGSuonoVerifica.ImpostaStrumento: " & ex.Message)
        End Try
    End Sub

End Class
