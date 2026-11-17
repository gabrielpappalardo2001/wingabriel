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

Public Class ComandoVocale
    Public Property Tipo As TipoComandoVocale = TipoComandoVocale.Sconosciuto
    Public Property TestoOriginale As String = ""
    Public Property Argomento As String = ""
    Public Property Destinatario As String = ""
    Public Property Oggetto As String = ""
    Public Property Corpo As String = ""
    Public Property Citta As String = ""
    Public Property GiorniDaOggi As Integer = 0
    Public Property Inizio As DateTime = DateTime.MinValue
    Public Property Fine As DateTime = DateTime.MinValue
    Public Property TuttoIlGiorno As Boolean = False
    Public Property SoloNonLetti As Boolean = True
    Public Property InvioConfermato As Boolean = False
    Public Property ApriPerControllo As Boolean = False
    Public Property OrigineOnline As Boolean = False
End Class
