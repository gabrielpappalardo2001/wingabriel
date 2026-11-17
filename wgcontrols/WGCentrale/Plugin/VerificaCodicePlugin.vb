Imports System.Collections.Generic
Imports System.Text

''' <summary>
''' Controlla il codice di un plugin PRIMA di caricarlo, leggendo i metadati del file .dll (senza eseguire niente). Un plugin può usare
''' solo un elenco ristretto di tipi del framework: niente file, niente cartelle, niente registro, niente processi, niente riflessione,
''' niente codice nativo (P/Invoke), niente puntatori. Così un plugin non può leggere né scrivere i file di WinGabriel in nessun modo,
''' né con le API di Windows né con le funzioni di .NET Framework: per lui quelle funzioni non esistono. Quello che gli serve lo ottiene
''' dai servizi di WinGabriel (IWGServiziPlugin), che controllano ogni richiesta e, per i dati dell'utente, chiedono il permesso.
'''
''' Ogni tipo e ogni membro a cui il codice si riferisce sta nelle tabelle dei metadati (TypeRef, MemberRef): se ce n'è uno fuori
''' dall'elenco, il plugin non si carica. Anche le librerie del plugin passano da qui. Non c'è nessun'eccezione, nemmeno per i plugin
''' firmati.
'''
''' Limite noto: un file scritto a mano in linguaggio intermedio, con aritmetica sui puntatori nativi, potrebbe leggere la memoria del
''' processo; non può comunque aprire file perché non può nominare nessuna funzione che lo faccia.
''' </summary>
Friend NotInheritable Class VerificaCodicePlugin

    Private Sub New()
    End Sub

    ''' <summary>Gli assembly del framework a cui un plugin può riferirsi (più WGPlugin e le sue librerie).</summary>
    Private Shared ReadOnly AssemblyBase As String() = {"mscorlib", "System", "System.Core", "Microsoft.VisualBasic"}

    ''' <summary>I namespace interi che si possono usare (tranne i tipi in TipiVietati).</summary>
    Private Shared ReadOnly NamespaceAmmessi As New HashSet(Of String)(StringComparer.Ordinal) From {
        "System", "System.Collections", "System.Collections.Generic", "System.Collections.ObjectModel",
        "System.Collections.Concurrent", "System.Collections.Specialized", "System.Linq", "System.Text",
        "System.Text.RegularExpressions", "System.Globalization", "System.Threading", "System.Threading.Tasks",
        "Microsoft.VisualBasic", "Microsoft.VisualBasic.CompilerServices"}

    ''' <summary>Tipi singoli ammessi in namespace che per il resto sono vietati.</summary>
    Private Shared ReadOnly TipiAmmessi As New HashSet(Of String)(StringComparer.Ordinal) From {
        "System.IO.MemoryStream", "System.IO.Stream", "System.IO.StringReader", "System.IO.StringWriter", "System.IO.TextReader",
        "System.IO.TextWriter", "System.IO.BinaryReader", "System.IO.BinaryWriter", "System.IO.SeekOrigin", "System.IO.IOException",
        "System.IO.EndOfStreamException", "System.IO.FileNotFoundException", "System.IO.DirectoryNotFoundException",
        "System.IO.PathTooLongException", "System.IO.InvalidDataException", "System.IO.Path",
        "System.Diagnostics.Stopwatch", "System.Diagnostics.Debug", "System.Diagnostics.DebuggerNonUserCodeAttribute",
        "System.Diagnostics.DebuggerStepThroughAttribute", "System.Diagnostics.DebuggerHiddenAttribute",
        "System.Diagnostics.DebuggerBrowsableAttribute", "System.Diagnostics.DebuggerBrowsableState",
        "System.Diagnostics.DebuggerDisplayAttribute", "System.Diagnostics.DebuggableAttribute",
        "System.Diagnostics.DebuggerStepperBoundaryAttribute", "System.Diagnostics.ConditionalAttribute",
        "System.Runtime.CompilerServices.ExtensionAttribute", "System.Runtime.CompilerServices.CompilerGeneratedAttribute",
        "System.Runtime.CompilerServices.IteratorStateMachineAttribute", "System.Runtime.CompilerServices.AsyncStateMachineAttribute",
        "System.Runtime.CompilerServices.StateMachineAttribute", "System.Runtime.CompilerServices.IAsyncStateMachine",
        "System.Runtime.CompilerServices.AsyncVoidMethodBuilder", "System.Runtime.CompilerServices.AsyncTaskMethodBuilder",
        "System.Runtime.CompilerServices.TaskAwaiter", "System.Runtime.CompilerServices.ConfiguredTaskAwaitable",
        "System.Runtime.CompilerServices.YieldAwaitable", "System.Runtime.CompilerServices.INotifyCompletion",
        "System.Runtime.CompilerServices.ICriticalNotifyCompletion", "System.Runtime.CompilerServices.RuntimeHelpers",
        "System.Runtime.CompilerServices.CompilationRelaxationsAttribute", "System.Runtime.CompilerServices.RuntimeCompatibilityAttribute",
        "System.Runtime.CompilerServices.IsVolatile", "System.Runtime.CompilerServices.FormattableStringFactory",
        "System.Runtime.CompilerServices.TupleElementNamesAttribute", "System.Runtime.CompilerServices.ConditionalWeakTable",
        "System.Runtime.CompilerServices.MethodImplAttribute", "System.Runtime.CompilerServices.MethodImplOptions",
        "System.Runtime.CompilerServices.CallerMemberNameAttribute", "System.Runtime.CompilerServices.CallerFilePathAttribute",
        "System.Runtime.CompilerServices.CallerLineNumberAttribute", "System.Runtime.CompilerServices.AccessedThroughPropertyAttribute",
        "System.Runtime.CompilerServices.StrongBox", "System.Runtime.CompilerServices.SpecialNameAttribute",
        "System.Runtime.CompilerServices.DateTimeConstantAttribute", "System.Runtime.CompilerServices.DecimalConstantAttribute",
        "System.Runtime.CompilerServices.IndexerNameAttribute",
        "System.Runtime.Versioning.TargetFrameworkAttribute",
        "System.Runtime.InteropServices.ComVisibleAttribute", "System.Runtime.InteropServices.GuidAttribute",
        "System.Runtime.InteropServices.InAttribute", "System.Runtime.InteropServices.OutAttribute",
        "System.Reflection.AssemblyTitleAttribute", "System.Reflection.AssemblyDescriptionAttribute",
        "System.Reflection.AssemblyCompanyAttribute", "System.Reflection.AssemblyProductAttribute",
        "System.Reflection.AssemblyCopyrightAttribute", "System.Reflection.AssemblyTrademarkAttribute",
        "System.Reflection.AssemblyCultureAttribute", "System.Reflection.AssemblyVersionAttribute",
        "System.Reflection.AssemblyFileVersionAttribute", "System.Reflection.AssemblyInformationalVersionAttribute",
        "System.Reflection.AssemblyConfigurationAttribute", "System.Reflection.AssemblyMetadataAttribute",
        "System.Reflection.DefaultMemberAttribute",
        "System.Security.Cryptography.SHA256", "System.Security.Cryptography.SHA256Managed",
        "System.Security.Cryptography.SHA256CryptoServiceProvider", "System.Security.Cryptography.SHA384",
        "System.Security.Cryptography.SHA512", "System.Security.Cryptography.SHA1", "System.Security.Cryptography.SHA1Managed",
        "System.Security.Cryptography.MD5", "System.Security.Cryptography.MD5CryptoServiceProvider",
        "System.Security.Cryptography.HashAlgorithm", "System.Security.Cryptography.RandomNumberGenerator",
        "System.Security.Cryptography.RNGCryptoServiceProvider", "System.Security.Cryptography.HMAC",
        "System.Security.Cryptography.HMACSHA256", "System.Security.Cryptography.Rfc2898DeriveBytes",
        "System.Security.Cryptography.CryptographicException",
        "System.Media.SystemSounds", "System.Media.SystemSound",
        "System.Net.WebUtility",
        "System.CodeDom.Compiler.GeneratedCodeAttribute", "System.ComponentModel.EditorBrowsableAttribute",
        "System.ComponentModel.EditorBrowsableState", "System.ComponentModel.Design.HelpKeywordAttribute",
        "Microsoft.VisualBasic.ApplicationServices.ApplicationBase", "Microsoft.VisualBasic.ApplicationServices.User",
        "Microsoft.VisualBasic.Devices.Computer", "Microsoft.VisualBasic.MyServices.Internal.ContextValue"}

    ''' <summary>Tipi vietati anche dentro i namespace ammessi: danno accesso a codice, riflessione, processo o file.</summary>
    Private Shared ReadOnly TipiVietati As New HashSet(Of String)(StringComparer.Ordinal) From {
        "System.AppDomain", "System.AppDomainManager", "System.AppDomainSetup", "System.AppDomainInitializer",
        "System.TypedReference", "System.ArgIterator", "System.AppContext", "System.Threading.Overlapped",
        "System.Threading.NativeOverlapped", "System.Threading.PreAllocatedOverlapped", "System.Threading.ThreadPoolBoundHandle",
        "System.Text.RegularExpressions.RegexCompilationInfo",
        "Microsoft.VisualBasic.FileSystem", "Microsoft.VisualBasic.VBCodeProvider", "Microsoft.VisualBasic.ComClassAttribute",
        "Microsoft.VisualBasic.CompilerServices.NewLateBinding", "Microsoft.VisualBasic.CompilerServices.LateBinding",
        "Microsoft.VisualBasic.CompilerServices.Versioned", "Microsoft.VisualBasic.CompilerServices.InternalXmlHelper",
        "Microsoft.VisualBasic.CompilerServices.Symbols"}

    ''' <summary>Per certi tipi non si vieta tutto: solo alcuni membri (Tranne) oppure si ammettono solo alcuni membri (Solo).</summary>
    ''' <remarks>
    ''' Il codice che Visual Basic genera da solo (il modello My, con MyType Windows) si accetta, così il programmatore non deve cambiare
    ''' niente nel progetto: di quelle classi si ammette solo il costruttore (e Value di ContextValue), quindi My.Computer.FileSystem,
    ''' My.Computer.Network e simili restano vietati. Activator si ammette solo nella forma generica CreateInstance(Of T), che crea
    ''' solo tipi già nominati nel codice (controllati a loro volta).
    ''' </remarks>
    Private Shared ReadOnly RegoleMembri As New Dictionary(Of String, Tuple(Of Boolean, HashSet(Of String)))(StringComparer.Ordinal) From {
        {"Microsoft.VisualBasic.ApplicationServices.ApplicationBase", Tuple.Create(True, New HashSet(Of String) From {".ctor"})},
        {"Microsoft.VisualBasic.ApplicationServices.User", Tuple.Create(True, New HashSet(Of String) From {".ctor"})},
        {"Microsoft.VisualBasic.Devices.Computer", Tuple.Create(True, New HashSet(Of String) From {".ctor"})},
        {"Microsoft.VisualBasic.MyServices.Internal.ContextValue", Tuple.Create(True, New HashSet(Of String) From {".ctor", "get_Value", "set_Value"})},
        {"System.Activator", Tuple.Create(True, New HashSet(Of String) From {"CreateInstance"})},
        {"System.Type", Tuple.Create(True, New HashSet(Of String) From {"GetTypeFromHandle", "op_Equality", "op_Inequality", "get_Name", "get_FullName", "ToString", "Equals", "GetHashCode"})},
        {"System.Runtime.CompilerServices.RuntimeHelpers", Tuple.Create(True, New HashSet(Of String) From {"InitializeArray", "GetObjectValue", "GetHashCode", "Equals", "get_OffsetToStringData"})},
        {"Microsoft.VisualBasic.Interaction", Tuple.Create(True, New HashSet(Of String) From {"IIf", "Choose", "Switch"})},
        {"System.Environment", Tuple.Create(False, New HashSet(Of String) From {"Exit", "FailFast", "SetEnvironmentVariable", "set_CurrentDirectory", "set_ExitCode"})},
        {"System.Delegate", Tuple.Create(False, New HashSet(Of String) From {"CreateDelegate"})},
        {"System.IO.Path", Tuple.Create(False, New HashSet(Of String) From {"GetTempFileName"})},
        {"Microsoft.VisualBasic.DateAndTime", Tuple.Create(False, New HashSet(Of String) From {"set_Today", "set_TimeOfDay"})}}

    ''' <summary>
    ''' Controlla il file. Restituisce vero se il plugin usa solo quello che è permesso. Altrimenti motivi dice, in breve, che cosa non va
    ''' (finiscono nel registro, non all'utente). In caso di dubbio (file illeggibile, struttura strana) il risultato è falso.
    ''' </summary>
    ''' <param name="dati">Il contenuto del file .dll.</param>
    ''' <param name="assemblyDelPlugin">I nomi delle librerie del plugin (nella cartella librerie): sono ammesse, ma controllate a loro volta.</param>
    Friend Shared Function Controlla(dati As Byte(), assemblyDelPlugin As IEnumerable(Of String), motivi As List(Of String)) As Boolean
        Try
            Dim lettore As New Lettore(dati, assemblyDelPlugin, motivi)
            lettore.Esegui()
        Catch ex As Exception
            motivi.Add("file non leggibile come assembly sicuro (" & ex.Message & ")")
        End Try
        Return motivi.Count = 0
    End Function

    Private NotInheritable Class Lettore

        Private ReadOnly _d As Byte()
        Private ReadOnly _motivi As List(Of String)
        Private ReadOnly _ammessi As New HashSet(Of String)(StringComparer.OrdinalIgnoreCase)
        Private ReadOnly _fratelli As New HashSet(Of String)(StringComparer.OrdinalIgnoreCase)
        Private _sezioni As New List(Of Long())()
        Private _strOff, _blobOff, _tilde As Integer
        Private _strLargo, _guidLargo, _blobLargo As Boolean
        Private ReadOnly _righe(63) As Long
        Private ReadOnly _offTab(63) As Integer
        Private ReadOnly _dimRiga(63) As Integer
        Private ReadOnly _colonne(63) As List(Of Integer)
        Private ReadOnly _offCol(63) As List(Of Integer)
        Private _sp, _sFine As Integer

        ' (bit del tag, tabelle) di ogni indice codificato; -1 = tag non usato
        Private Shared ReadOnly Codificati As New Dictionary(Of String, Tuple(Of Integer, Integer())) From {
            {"TDR", Tuple.Create(2, New Integer() {&H2, &H1, &H1B})},
            {"RS", Tuple.Create(2, New Integer() {&H0, &H1A, &H23, &H1})},
            {"HC", Tuple.Create(2, New Integer() {&H4, &H8, &H17})},
            {"HCA", Tuple.Create(5, New Integer() {&H6, &H4, &H1, &H2, &H8, &H9, &HA, &H0, &HE, &H17, &H14, &H11, &H1A, &H1B, &H20, &H23, &H26, &H27, &H28, &H2A, &H2C, &H2B})},
            {"HFM", Tuple.Create(1, New Integer() {&H4, &H8})},
            {"HDS", Tuple.Create(2, New Integer() {&H2, &H6, &H20})},
            {"MRP", Tuple.Create(3, New Integer() {&H2, &H1, &H1A, &H6, &H1B})},
            {"HS", Tuple.Create(1, New Integer() {&H14, &H17})},
            {"MDR", Tuple.Create(1, New Integer() {&H6, &HA})},
            {"MF", Tuple.Create(1, New Integer() {&H4, &H6})},
            {"IMP", Tuple.Create(2, New Integer() {&H26, &H23, &H27})},
            {"CAT", Tuple.Create(3, New Integer() {-1, -1, &H6, &HA, -1})},
            {"TMD", Tuple.Create(1, New Integer() {&H2, &H6})}}

        ' le colonne di ogni tabella: 1,2,4 = byte fissi; S, G, B = indici negli heap; tXX = indice di tabella; c:NOME = indice codificato
        Private Shared ReadOnly Schemi As String() = {
            "2 S G G G", "c:RS S S", "4 S S c:TDR t04 t06", "t04", "2 S B", "t06", "4 2 2 S B t08", "t08",
            "2 2 S", "t02 c:TDR", "c:MRP S B", "2 c:HC B", "c:HCA c:CAT B", "c:HFM B", "2 c:HDS B", "2 4 t02",
            "4 t04", "B", "t02 t14", "t14", "2 S c:TDR", "t02 t17", "t17", "2 S B",
            "2 t06 c:HS", "t02 c:MDR c:MDR", "S", "B", "2 c:MF S t1A", "4 t04", "4 4", "4",
            "4 2 2 2 2 4 B S S", "4", "4 4 4", "2 2 2 2 4 B S S B", "4 t23", "4 4 4 t23", "4 S B", "4 4 S S c:IMP",
            "4 4 S c:IMP", "t02 t02", "2 2 c:TMD S", "c:MDR B", "t2A c:TDR"}

        Public Sub New(dati As Byte(), assemblyDelPlugin As IEnumerable(Of String), motivi As List(Of String))
            _d = dati
            _motivi = motivi
            For Each nome As String In AssemblyBase
                _ammessi.Add(nome)
            Next
            If assemblyDelPlugin IsNot Nothing Then
                For Each nome As String In assemblyDelPlugin
                    If Not String.IsNullOrWhiteSpace(nome) Then _fratelli.Add(nome)
                Next
            End If
        End Sub

#Region "Lettura del file"

        Private Function U8(o As Integer) As Integer
            Return _d(o)
        End Function

        Private Function U16(o As Integer) As Integer
            Return CInt(_d(o)) Or (CInt(_d(o + 1)) << 8)
        End Function

        Private Function U32(o As Integer) As Long
            Return CLng(_d(o)) Or (CLng(_d(o + 1)) << 8) Or (CLng(_d(o + 2)) << 16) Or (CLng(_d(o + 3)) << 24)
        End Function

        Private Function Offset(rva As Long) As Integer
            For Each s As Long() In _sezioni
                Dim va As Long = s(0)
                Dim lunghezza As Long = Math.Max(s(1), s(2))
                If rva >= va AndAlso rva < va + lunghezza Then Return CInt(s(3) + (rva - va))
            Next
            Throw New InvalidOperationException("indirizzo fuori dalle sezioni")
        End Function

        Private Sub Motivo(testo As String)
            If _motivi.Count < 12 AndAlso Not _motivi.Contains(testo) Then _motivi.Add(testo)
        End Sub

#End Region

#Region "Struttura"

        Public Sub Esegui()
            If _d Is Nothing OrElse _d.Length < 128 Then Throw New InvalidOperationException("file troppo corto")
            If U16(0) <> &H5A4D Then Throw New InvalidOperationException("non è un file eseguibile")
            Dim pe As Integer = CInt(U32(&H3C))
            If U32(pe) <> &H4550 Then Throw New InvalidOperationException("intestazione PE non valida")
            Dim coff As Integer = pe + 4
            Dim nSezioni As Integer = U16(coff + 2)
            Dim dimOpzionale As Integer = U16(coff + 16)
            Dim opz As Integer = coff + 20
            Dim magic As Integer = U16(opz)
            Dim cartelle As Integer = opz + If(magic = &H20B, 112, 96)
            Dim cliRva As Long = U32(cartelle + 14 * 8)
            If cliRva = 0 Then Throw New InvalidOperationException("non è un assembly gestito")
            Dim primaSezione As Integer = opz + dimOpzionale
            For i As Integer = 0 To nSezioni - 1
                Dim s As Integer = primaSezione + 40 * i
                _sezioni.Add(New Long() {U32(s + 12), U32(s + 8), U32(s + 16), U32(s + 20)})
            Next
            Dim cli As Integer = Offset(cliRva)
            Dim flag As Long = U32(cli + 16)
            If (flag And 1) = 0 Then Motivo("contiene codice nativo (non è solo codice gestito)")
            If (flag And &H10) <> 0 Then Motivo("contiene un punto d'ingresso nativo")
            Dim meta As Integer = Offset(U32(cli + 8))
            If U32(meta) <> &H424A5342 Then Throw New InvalidOperationException("metadati non validi")
            Dim lungVersione As Integer = CInt(U32(meta + 12))
            Dim nFlussi As Integer = U16(meta + 18 + lungVersione)
            Dim p As Integer = meta + 20 + lungVersione
            _tilde = -1
            _strOff = -1
            _blobOff = -1
            For i As Integer = 0 To nFlussi - 1
                Dim off As Integer = meta + CInt(U32(p))
                Dim nome As New StringBuilder()
                Dim q As Integer = p + 8
                While _d(q) <> 0
                    nome.Append(ChrW(_d(q)))
                    q += 1
                End While
                Dim lungNome As Integer = nome.Length + 1
                p += 8 + ((lungNome + 3) \ 4) * 4
                Select Case nome.ToString()
                    Case "#~" : _tilde = off
                    Case "#-" : Throw New InvalidOperationException("metadati non compressi")
                    Case "#Strings" : _strOff = off
                    Case "#Blob" : _blobOff = off
                End Select
            Next
            If _tilde < 0 OrElse _strOff < 0 OrElse _blobOff < 0 Then Throw New InvalidOperationException("flussi dei metadati mancanti")
            LeggiTabelle()
            ControllaTabelleVietate()
            ControllaAssemblyRiferiti()
            ControllaTipiERiferimenti()
            ControllaMetodiEFirme()
        End Sub

        Private Function Coded(nome As String) As Integer
            Dim def As Tuple(Of Integer, Integer()) = Codificati(nome)
            Dim massimo As Long = 0
            For Each t As Integer In def.Item2
                If t >= 0 AndAlso _righe(t) > massimo Then massimo = _righe(t)
            Next
            Return If(massimo < (1L << (16 - def.Item1)), 2, 4)
        End Function

        Private Sub LeggiTabelle()
            Dim t As Integer = _tilde
            Dim heap As Integer = U8(t + 6)
            _strLargo = (heap And 1) <> 0
            _guidLargo = (heap And 2) <> 0
            _blobLargo = (heap And 4) <> 0
            Dim validoBasso As Long = U32(t + 8)
            Dim validoAlto As Long = U32(t + 12)
            Dim pos As Integer = t + 24
            For i As Integer = 0 To 63
                Dim presente As Boolean = If(i < 32, ((validoBasso >> i) And 1) <> 0, ((validoAlto >> (i - 32)) And 1) <> 0)
                If presente Then
                    If i > &H2C Then Throw New InvalidOperationException("tabella sconosciuta")
                    _righe(i) = U32(pos)
                    pos += 4
                End If
            Next
            For i As Integer = 0 To &H2C
                _colonne(i) = New List(Of Integer)()
                _offCol(i) = New List(Of Integer)()
                Dim somma As Integer = 0
                For Each gettone As String In Schemi(i).Split(" "c)
                    Dim dim1 As Integer
                    Select Case gettone
                        Case "1" : dim1 = 1
                        Case "2" : dim1 = 2
                        Case "4" : dim1 = 4
                        Case "S" : dim1 = If(_strLargo, 4, 2)
                        Case "G" : dim1 = If(_guidLargo, 4, 2)
                        Case "B" : dim1 = If(_blobLargo, 4, 2)
                        Case Else
                            If gettone.StartsWith("c:") Then
                                dim1 = Coded(gettone.Substring(2))
                            Else
                                dim1 = If(_righe(Convert.ToInt32(gettone.Substring(1), 16)) >= 65536, 4, 2)
                            End If
                    End Select
                    _colonne(i).Add(dim1)
                    _offCol(i).Add(somma)
                    somma += dim1
                Next
                _dimRiga(i) = somma
                _offTab(i) = pos
                pos += CInt(_righe(i)) * somma
            Next
        End Sub

        ''' <summary>Il valore di una colonna; riga da 1.</summary>
        Private Function Cella(tabella As Integer, riga As Long, colonna As Integer) As Long
            Dim o As Integer = _offTab(tabella) + CInt(riga - 1) * _dimRiga(tabella) + _offCol(tabella)(colonna)
            Select Case _colonne(tabella)(colonna)
                Case 1 : Return U8(o)
                Case 2 : Return U16(o)
                Case Else : Return U32(o)
            End Select
        End Function

        Private Function Stringa(indice As Long) As String
            Dim o As Integer = _strOff + CInt(indice)
            Dim fine As Integer = o
            While _d(fine) <> 0
                fine += 1
            End While
            Return Encoding.UTF8.GetString(_d, o, fine - o)
        End Function

        Private Function Compresso(ByRef o As Integer) As Integer
            Dim b As Integer = _d(o)
            If (b And &H80) = 0 Then
                o += 1
                Return b
            ElseIf (b And &HC0) = &H80 Then
                Dim v As Integer = ((b And &H3F) << 8) Or _d(o + 1)
                o += 2
                Return v
            ElseIf (b And &HE0) = &HC0 Then
                Dim v As Integer = ((b And &H1F) << 24) Or (CInt(_d(o + 1)) << 16) Or (CInt(_d(o + 2)) << 8) Or _d(o + 3)
                o += 4
                Return v
            End If
            Throw New InvalidOperationException("numero compresso non valido")
        End Function

#End Region

#Region "Controlli"

        Private Sub ControllaTabelleVietate()
            If _righe(&H1C) > 0 Then Motivo("usa codice nativo (P/Invoke)")
            If _righe(&H1A) > 0 Then Motivo("si riferisce a moduli esterni")
            If _righe(&H26) > 0 Then Motivo("è fatto di più file")
            If _righe(&HE) > 0 Then Motivo("ha attributi di sicurezza dichiarativi")
            If _righe(&H27) > 0 Then Motivo("inoltra tipi ad altri assembly")
            For Each t As Integer In New Integer() {&H3, &H5, &H7, &H13, &H16}
                If _righe(t) > 0 Then Throw New InvalidOperationException("metadati non standard")
            Next
        End Sub

        Private Sub ControllaAssemblyRiferiti()
            For riga As Long = 1 To _righe(&H23)
                Dim nome As String = Stringa(Cella(&H23, riga, 6))
                If Not (_ammessi.Contains(nome) OrElse String.Equals(nome, "WGPlugin", StringComparison.OrdinalIgnoreCase) OrElse _fratelli.Contains(nome)) Then
                    Motivo("usa l'assembly " & nome)
                End If
            Next
        End Sub

        Private Function NomeAssembly(riga As Long) As String
            Return Stringa(Cella(&H23, riga, 6))
        End Function

        ''' <summary>Il tipo di primo livello a cui punta una riga di TypeRef (se è annidato, quello che lo contiene).</summary>
        Private Sub TipoSuperiore(riga As Long, ByRef assembly As String, ByRef spazio As String, ByRef nome As String)
            Dim corrente As Long = riga
            For giro As Integer = 0 To 20
                Dim scope As Long = Cella(1, corrente, 0)
                Dim tag As Integer = CInt(scope And 3)
                Dim indice As Long = scope >> 2
                If tag = 3 Then
                    corrente = indice
                    Continue For
                End If
                spazio = Stringa(Cella(1, corrente, 2))
                nome = Stringa(Cella(1, corrente, 1))
                assembly = If(tag = 2, NomeAssembly(indice), "?")
                Return
            Next
            Throw New InvalidOperationException("tipi annidati troppo profondi")
        End Sub

        Private Shared Function NomeSenzaArita(nome As String) As String
            Dim i As Integer = nome.IndexOf("`"c)
            Return If(i >= 0, nome.Substring(0, i), nome)
        End Function

        Private Function PienoNome(spazio As String, nome As String) As String
            Dim n As String = NomeSenzaArita(nome)
            Return If(spazio = "", n, spazio & "." & n)
        End Function

        ''' <summary>Vuoto se il tipo si può usare, altrimenti il nome del tipo vietato.</summary>
        Private Function TipoVietato(assembly As String, spazio As String, nome As String) As String
            Dim pieno As String = PienoNome(spazio, nome)
            If String.Equals(assembly, "WGPlugin", StringComparison.OrdinalIgnoreCase) OrElse _fratelli.Contains(assembly) Then Return ""
            If assembly = "?" OrElse Not _ammessi.Contains(assembly) Then Return pieno
            If TipiVietati.Contains(pieno) Then Return pieno
            If NamespaceAmmessi.Contains(spazio) Then Return ""
            If TipiAmmessi.Contains(pieno) Then Return ""
            Return pieno
        End Function

        Private Sub ControllaTipiERiferimenti()
            ' tutti i tipi esterni a cui il codice si riferisce
            For riga As Long = 1 To _righe(1)
                Dim scope As Long = Cella(1, riga, 0)
                If (scope And 3) = 3 Then Continue For
                Dim assembly As String = "", spazio As String = "", nome As String = ""
                TipoSuperiore(riga, assembly, spazio, nome)
                Dim vietato As String = TipoVietato(assembly, spazio, nome)
                If vietato <> "" Then Motivo("usa " & vietato)
            Next
            ' i membri di certi tipi
            For riga As Long = 1 To _righe(&HA)
                Dim parente As Long = Cella(&HA, riga, 0)
                If (parente And 7) <> 1 Then Continue For
                Dim assembly As String = "", spazio As String = "", nome As String = ""
                TipoSuperiore(parente >> 3, assembly, spazio, nome)
                If Not _ammessi.Contains(assembly) Then Continue For
                Dim regola As Tuple(Of Boolean, HashSet(Of String)) = Nothing
                Dim pieno As String = PienoNome(spazio, nome)
                If Not RegoleMembri.TryGetValue(pieno, regola) Then Continue For
                Dim membro As String = Stringa(Cella(&HA, riga, 1))
                Dim contenuto As Boolean = regola.Item2.Contains(membro)
                If contenuto AndAlso pieno = "System.Activator" Then
                    ' solo CreateInstance generico: la firma del metodo ha il bit "generico" (0x10)
                    Dim o As Integer = _blobOff + CInt(Cella(&HA, riga, 2))
                    Compresso(o)
                    If (_d(o) And &H10) = 0 Then contenuto = False
                End If
                If (regola.Item1 AndAlso Not contenuto) OrElse (Not regola.Item1 AndAlso contenuto) Then Motivo("usa " & pieno & "." & membro)
            Next
        End Sub

        Private Sub ControllaMetodiEFirme()
            For riga As Long = 1 To _righe(6)
                Dim flagMetodo As Long = Cella(6, riga, 2)
                Dim flagImpl As Long = Cella(6, riga, 1)
                If (flagMetodo And &H2000) <> 0 Then Motivo("ha metodi nativi (P/Invoke)")
                If (flagImpl And &H1000) <> 0 Then Motivo("ha metodi interni del runtime")
                If (flagImpl And 4) <> 0 Then Motivo("ha metodi non gestiti")
                Dim tipoCodice As Long = flagImpl And 3
                If tipoCodice = 1 OrElse tipoCodice = 2 Then Motivo("ha metodi in codice nativo")
                ControllaFirma(Cella(6, riga, 4), False)
            Next
            For riga As Long = 1 To _righe(4)
                ControllaFirma(Cella(4, riga, 2), False)
            Next
            For riga As Long = 1 To _righe(&HA)
                ControllaFirma(Cella(&HA, riga, 2), False)
            Next
            For riga As Long = 1 To _righe(&H11)
                ControllaFirma(Cella(&H11, riga, 0), False)
            Next
            For riga As Long = 1 To _righe(&H1B)
                ControllaFirma(Cella(&H1B, riga, 0), True)
            Next
            For riga As Long = 1 To _righe(&H2B)
                ControllaFirma(Cella(&H2B, riga, 1), False)
            Next
            For riga As Long = 1 To _righe(&H17)
                ControllaFirma(Cella(&H17, riga, 2), False)
            Next
        End Sub

#End Region

#Region "Firme (puntatori e chiamate native)"

        Private Sub ControllaFirma(indiceBlob As Long, soloTipo As Boolean)
            Dim o As Integer = _blobOff + CInt(indiceBlob)
            Dim lunghezza As Integer = Compresso(o)
            If lunghezza = 0 Then Return
            _sp = o
            _sFine = o + lunghezza
            If soloTipo Then
                LeggiTipo(0)
            Else
                LeggiFirma()
            End If
        End Sub

        Private Function Byte1() As Integer
            If _sp >= _sFine Then Throw New InvalidOperationException("firma troncata")
            Dim b As Integer = _d(_sp)
            _sp += 1
            Return b
        End Function

        Private Function Num() As Integer
            If _sp >= _sFine Then Throw New InvalidOperationException("firma troncata")
            Return Compresso(_sp)
        End Function

        Private Sub LeggiFirma()
            Dim b As Integer = Byte1()
            Dim convenzione As Integer = b And &HF
            Select Case convenzione
                Case 6
                    LeggiTipo(0)
                Case 7, &HA
                    Dim n As Integer = Num()
                    For i As Integer = 1 To n
                        LeggiTipo(0)
                    Next
                Case Else
                    If convenzione >= 1 AndAlso convenzione <= 4 Then Motivo("chiama funzioni native")
                    If convenzione > &H8 Then Throw New InvalidOperationException("convenzione di chiamata sconosciuta")
                    If convenzione = 8 Then
                        ' proprietà: numero di parametri, tipo, parametri
                    ElseIf (b And &H10) <> 0 Then
                        Num()
                    End If
                    Dim parametri As Integer = Num()
                    LeggiTipo(0)
                    For i As Integer = 1 To parametri
                        LeggiTipo(0)
                    Next
            End Select
        End Sub

        Private Sub LeggiTipo(profondita As Integer)
            If profondita > 40 Then Throw New InvalidOperationException("firma troppo annidata")
            Dim t As Integer = Byte1()
            Select Case t
                Case 1 To &HE, &H16, &H18, &H19, &H1C
                    Return
                Case &HF
                    Motivo("usa puntatori")
                    LeggiTipo(profondita + 1)
                Case &H10, &H1D, &H41
                    LeggiTipo(profondita + 1)
                Case &H11, &H12, &H13, &H1E
                    Num()
                Case &H14
                    LeggiTipo(profondita + 1)
                    Num()
                    Dim nDim As Integer = Num()
                    For i As Integer = 1 To nDim
                        Num()
                    Next
                    Dim nLimiti As Integer = Num()
                    For i As Integer = 1 To nLimiti
                        Num()
                    Next
                Case &H15
                    Byte1()
                    Num()
                    Dim nArg As Integer = Num()
                    For i As Integer = 1 To nArg
                        LeggiTipo(profondita + 1)
                    Next
                Case &H1B
                    Motivo("usa puntatori a funzione")
                    Dim b As Integer = Byte1()
                    If (b And &H10) <> 0 Then Num()
                    Dim nPar As Integer = Num()
                    LeggiTipo(profondita + 1)
                    For i As Integer = 1 To nPar
                        LeggiTipo(profondita + 1)
                    Next
                Case &H1F, &H20
                    Num()
                    LeggiTipo(profondita + 1)
                Case &H45
                    Motivo("usa codice non verificabile (puntatori fissati)")
                    LeggiTipo(profondita + 1)
                Case Else
                    Throw New InvalidOperationException("elemento di firma sconosciuto")
            End Select
        End Sub

#End Region

    End Class

End Class
