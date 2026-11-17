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

' Impedisce la sospensione automatica del sistema operativo durante l'esecuzione di operazioni prolungate.
Public NotInheritable Class WGNoStandby

    <DllImport("kernel32.dll", CharSet:=CharSet.Auto, SetLastError:=True)>
    Private Shared Function SetThreadExecutionState(ByVal esFlags As EXECUTION_STATE) As EXECUTION_STATE
    End Function

    <Flags>
    Private Enum EXECUTION_STATE As UInteger
        ES_CONTINUOUS = &H80000000UI
        ES_SYSTEM_REQUIRED = &H1
    End Enum

    Private Shared _attivo As Boolean = False
    Private Shared ReadOnly _lock As New Object()

    Public Shared ReadOnly Property Attivo As Boolean
        Get
            Return _attivo
        End Get
    End Property

    Public Shared Sub Avvia()
        SyncLock _lock
            Try
                SetThreadExecutionState(EXECUTION_STATE.ES_CONTINUOUS Or EXECUTION_STATE.ES_SYSTEM_REQUIRED)
                _attivo = True
            Catch ex As Exception
                ScriviLogAvvio("WGNoStandby.Avvia eccezione: " & ex.Message)
            End Try
        End SyncLock
    End Sub

    Public Shared Sub Ferma()
        SyncLock _lock
            Try
                SetThreadExecutionState(EXECUTION_STATE.ES_CONTINUOUS)
                _attivo = False
            Catch ex As Exception
                ScriviLogAvvio("WGNoStandby.Ferma eccezione: " & ex.Message)
            End Try
        End SyncLock
    End Sub

End Class
