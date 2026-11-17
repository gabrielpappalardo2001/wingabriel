Imports System.IO
Imports System.IO.Ports
Imports System.Text
Imports System.Threading

' Gestione della comunicazione con i display Braille, traduzione del testo in caratteri Braille e controllo della porta seriale o
' USB.
Friend NotInheritable Class UscitaBraille

    Public Event Aggiornato(testo As String)

    Private ReadOnly log As New Logger()
    Private ReadOnly _bloccoScrittura As New Object()
    Private _porta As SerialPort
    Private _nomePorta As String = ""
    Private _ultimo As String = ""

    Public ReadOnly Property UltimoTesto As String
        Get
            Return _ultimo
        End Get
    End Property

    Public Sub ImpostaPorta(nome As String)
        Try
            If String.Equals(_nomePorta, If(nome, ""), StringComparison.OrdinalIgnoreCase) AndAlso _porta IsNot Nothing Then Return
            ChiudiPorta()
            _nomePorta = If(nome, "").Trim()
            If _nomePorta = "" Then Return
            _porta = New SerialPort(_nomePorta, 9600, Parity.None, 8, StopBits.One)
            _porta.Encoding = Encoding.UTF8
            _porta.Open()
        Catch ex As Exception
            log.ScriviLog("UscitaBraille.ImpostaPorta: " & ex.Message)
            ChiudiPorta()
        End Try
    End Sub

    Public Sub Chiudi()
        ChiudiPorta()
    End Sub

    Private Sub ScriviUscita(stato As Object)
        Try
            Dim daScrivere As String = TryCast(stato, String)
            If daScrivere Is Nothing Then Return
            SyncLock _bloccoScrittura
                Dim porta As SerialPort = _porta
                If porta IsNot Nothing AndAlso porta.IsOpen Then porta.WriteLine(daScrivere)
            End SyncLock
        Catch ex As Exception
            log.ScriviLog("UscitaBraille.ScriviUscita: " & ex.Message)
        End Try
    End Sub

    Public Sub Mostra(testo As String, cursore As Integer)
        Try
            Dim braille As String = Traduci(testo, cursore)
            _ultimo = braille
            ThreadPool.QueueUserWorkItem(AddressOf ScriviUscita, braille)
            RaiseEvent Aggiornato(braille)
        Catch ex As Exception
            log.ScriviLog("UscitaBraille.Mostra: " & ex.Message)
        End Try
    End Sub

    Public Shared Function Traduci(testo As String, cursore As Integer) As String
        If testo Is Nothing Then testo = ""
        Dim sb As New StringBuilder(testo.Length * 2)
        Dim inNumero As Boolean = False
        Dim indice As Integer = 0
        While indice < testo.Length
            If cursore = indice Then sb.Append(ChrW(&H2800 + &H40))
            Dim carattere As Char = testo(indice)
            If Char.IsDigit(carattere) Then
                If Not inNumero Then
                    sb.Append(Cella(3, 4, 5, 6))
                    inNumero = True
                End If
                sb.Append(Cifra(carattere))
                indice += 1
                Continue While
            End If
            inNumero = False
            If Char.IsLetter(carattere) AndAlso Char.IsUpper(carattere) Then
                Dim fine As Integer = indice + 1
                While fine < testo.Length AndAlso Char.IsLetter(testo(fine)) AndAlso Char.IsUpper(testo(fine))
                    fine += 1
                End While
                sb.Append(Cella(4, 6))
                If fine > indice + 1 Then sb.Append(Cella(4, 6))
                While indice < fine
                    sb.Append(Lettera(Char.ToLowerInvariant(testo(indice))))
                    indice += 1
                End While
                Continue While
            End If
            sb.Append(Lettera(carattere))
            indice += 1
        End While
        If cursore >= testo.Length Then sb.Append(ChrW(&H2800 + &H40))
        Return sb.ToString()
    End Function

    Public Shared Function Verifica() As String
        Dim casa As String = Traduci("casa", -1)
        If casa <> "⠉⠁⠎⠁" Then Return "casa"
        Dim maiuscola As String = Traduci("A", -1)
        If maiuscola <> "⠨⠁" Then Return "maiuscola"
        Dim numero As String = Traduci("10", -1)
        If numero <> "⠼⠁⠚" Then Return "numero"
        Dim accento As String = Traduci("è", -1)
        If accento <> "⠮" Then Return "accento"
        Return ""
    End Function

    Private Shared Function Cifra(carattere As Char) As Char
        Select Case carattere
            Case "1"c : Return Cella(1)
            Case "2"c : Return Cella(1, 2)
            Case "3"c : Return Cella(1, 4)
            Case "4"c : Return Cella(1, 4, 5)
            Case "5"c : Return Cella(1, 5)
            Case "6"c : Return Cella(1, 2, 4)
            Case "7"c : Return Cella(1, 2, 4, 5)
            Case "8"c : Return Cella(1, 2, 5)
            Case "9"c : Return Cella(2, 4)
            Case Else : Return Cella(2, 4, 5)
        End Select
    End Function

    Private Shared Function Lettera(carattere As Char) As Char
        Select Case carattere
            Case "a"c : Return Cella(1)
            Case "b"c : Return Cella(1, 2)
            Case "c"c : Return Cella(1, 4)
            Case "d"c : Return Cella(1, 4, 5)
            Case "e"c : Return Cella(1, 5)
            Case "f"c : Return Cella(1, 2, 4)
            Case "g"c : Return Cella(1, 2, 4, 5)
            Case "h"c : Return Cella(1, 2, 5)
            Case "i"c : Return Cella(2, 4)
            Case "j"c : Return Cella(2, 4, 5)
            Case "k"c : Return Cella(1, 3)
            Case "l"c : Return Cella(1, 2, 3)
            Case "m"c : Return Cella(1, 3, 4)
            Case "n"c : Return Cella(1, 3, 4, 5)
            Case "o"c : Return Cella(1, 3, 5)
            Case "p"c : Return Cella(1, 2, 3, 4)
            Case "q"c : Return Cella(1, 2, 3, 4, 5)
            Case "r"c : Return Cella(1, 2, 3, 5)
            Case "s"c : Return Cella(2, 3, 4)
            Case "t"c : Return Cella(2, 3, 4, 5)
            Case "u"c : Return Cella(1, 3, 6)
            Case "v"c : Return Cella(1, 2, 3, 6)
            Case "w"c : Return Cella(2, 4, 5, 6)
            Case "x"c : Return Cella(1, 3, 4, 6)
            Case "y"c : Return Cella(1, 3, 4, 5, 6)
            Case "z"c : Return Cella(1, 3, 5, 6)
            Case "à"c : Return Cella(1, 2, 3, 5, 6)
            Case "è"c : Return Cella(2, 3, 4, 6)
            Case "é"c : Return Cella(1, 2, 3, 4, 5, 6)
            Case "ì"c : Return Cella(3, 4)
            Case "ò"c : Return Cella(3, 4, 6)
            Case "ù"c : Return Cella(2, 3, 4, 5, 6)
            Case " "c : Return " "c
            Case ","c : Return Cella(2)
            Case ";"c : Return Cella(2, 3)
            Case ":"c : Return Cella(2, 5)
            Case "."c : Return Cella(2, 5, 6)
            Case "?"c : Return Cella(2, 6)
            Case "!"c : Return Cella(2, 3, 5)
            Case "'"c : Return Cella(6)
            Case "-"c, "–"c, "—"c : Return Cella(3, 6)
            Case "("c : Return Cella(2, 3, 6)
            Case ")"c : Return Cella(3, 5, 6)
            Case "/"c : Return Cella(3, 4)
            Case Else : Return carattere
        End Select
    End Function

    Private Shared Function Cella(ParamArray punti() As Integer) As Char
        Dim bit As Integer = 0
        For Each punto In punti
            If punto >= 1 AndAlso punto <= 8 Then bit = bit Or (1 << (punto - 1))
        Next
        Return ChrW(&H2800 + bit)
    End Function

    Private Sub ChiudiPorta()
        Try
            If _porta IsNot Nothing Then
                If _porta.IsOpen Then _porta.Close()
                _porta.Dispose()
            End If
        Catch exAtteso As Exception
            RegistroErrori.Scrivi("UscitaBraille.ChiudiPorta", exAtteso)
        Finally
            _porta = Nothing
            _nomePorta = ""
        End Try
    End Sub

End Class
