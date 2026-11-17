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

' Gestione del campanellino di avvio e riproduzione dei suoni tramite strumenti musicali o fallback.
Public NotInheritable Class WGCampanellinoAvvio

    Private Declare Auto Function midiOutOpen Lib "winmm.dll" (ByRef lphMidiOut As IntPtr, ByVal uDeviceID As Integer, ByVal dwCallback As IntPtr, ByVal dwInstance As IntPtr, ByVal dwFlags As Integer) As Integer
    Private Declare Auto Function midiOutClose Lib "winmm.dll" (ByVal hMidiOut As IntPtr) As Integer
    Private Declare Auto Function midiOutShortMsg Lib "winmm.dll" (ByVal hMidiOut As IntPtr, ByVal dwMsg As UInteger) As Integer

    Private Const MIDI_MAPPER As Integer = -1
    Private Const STATO_PROGRAMMA As UInteger = &HC0UI
    Private Const STATO_NOTE_ON As UInteger = &H90UI
    Private Const STATO_NOTE_OFF As UInteger = &H80UI
    Private Const STRUMENTO_CAMPANELLINO As UInteger = 14UI

    Public Shared Sub Suona()
        Try
            If SuoniVolumeAvvioDisattivati() Then Return
        Catch exConfig As Exception
            ScriviLogAvvio("WGCampanellinoAvvio.Suona (verifica configurazione): " & exConfig.Message)
        End Try

        Dim handle As IntPtr = IntPtr.Zero

        Try
            Dim risultatoApertura As Integer = midiOutOpen(handle, MIDI_MAPPER, IntPtr.Zero, IntPtr.Zero, 0)

            If risultatoApertura <> 0 OrElse handle = IntPtr.Zero Then
                SuonaFallback()
                Return
            End If

            Try
                ImpostaStrumento(handle, STRUMENTO_CAMPANELLINO)

                SuonaNota(handle, 72, 90, 150)
                SuonaNota(handle, 76, 90, 150)
                SuonaNota(handle, 79, 95, 150)
                SuonaNota(handle, 84, 100, 350)
            Finally
                Try
                    midiOutClose(handle)
                Catch exChiusura As Exception
                    ScriviLogAvvio("WGCampanellinoAvvio.Suona (chiusura dispositivo): " & exChiusura.Message)
                End Try
            End Try
        Catch ex As Exception
            ScriviLogAvvio("WGCampanellinoAvvio.Suona eccezione: " & ex.Message & Environment.NewLine & ex.StackTrace)
            SuonaFallback()
        End Try
    End Sub

    Private Shared Sub ImpostaStrumento(ByVal handle As IntPtr, ByVal strumento As UInteger)
        Try
            Dim messaggio As UInteger = STATO_PROGRAMMA Or (strumento << 8)
            midiOutShortMsg(handle, messaggio)
        Catch ex As Exception
            ScriviLogAvvio("WGCampanellinoAvvio.ImpostaStrumento: " & ex.Message)
        End Try
    End Sub

    Private Shared Sub SuonaNota(ByVal handle As IntPtr, ByVal nota As Byte, ByVal velocita As Byte, ByVal durataMillisecondi As Integer)
        Try
            Dim messaggioOn As UInteger = STATO_NOTE_ON Or (CUInt(nota) << 8) Or (CUInt(velocita) << 16)
            midiOutShortMsg(handle, messaggioOn)

            Threading.Thread.Sleep(durataMillisecondi)

            Dim messaggioOff As UInteger = STATO_NOTE_OFF Or (CUInt(nota) << 8)
            midiOutShortMsg(handle, messaggioOff)
        Catch ex As Exception
            ScriviLogAvvio("WGCampanellinoAvvio.SuonaNota: " & ex.Message)
        End Try
    End Sub

    Private Shared Sub SuonaFallback()
        Try
            Console.Beep(1046, 120)
            Console.Beep(1318, 120)
            Console.Beep(1568, 240)
        Catch ex As Exception
            ScriviLogAvvio("WGCampanellinoAvvio.SuonaFallback: " & ex.Message)
        End Try
    End Sub

End Class
