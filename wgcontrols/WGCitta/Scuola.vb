Imports System
Imports System.Collections.Generic
Imports System.Linq

' ============================================================================================
' La scuola guida (facoltativa, dal menu iniziale): un istruttore insegna a guidare a orecchio partendo dall'autoscuola, senza traffico
' né polizia. Un passo alla volta: salire, accelerare, frenare, svoltare, il rischio delle curve, il sensore, il cambio di corsia,
' il semaforo, il navigatore. Le parole dell'istruttore le ha scritte Gemini (testi.json, sezione scuola).
' ============================================================================================

Public NotInheritable Class ScuolaGuida
    Private ReadOnly _m As Mondo
    Private ReadOnly _passi As String() = {"benvenuto", "sali", "accelera", "frena", "svolta", "curva", "sensore", "corsia", "semaforo", "navigatore", "fine"}
    Private _i As Integer = -1
    Private _attesa As Double
    Private _tratto As Tratto
    Private _angolo As Double
    Private _ostacolo As Veicolo
    Private _corsiaFatta As Boolean
    Private _meta As Luogo
    Public Finita As Boolean
    Public Event Conclusa()

    Public Sub New(m As Mondo)
        Try
            _m = m
        Catch ex As Exception
            RegistroErrori.Registra("ScuolaGuida.New", ex)
            Throw
        End Try
    End Sub

    Private ReadOnly Property PassoAttuale As String
        Get
            Return If(_i >= 0 AndAlso _i < _passi.Length, _passi(_i), "")
        End Get
    End Property

    Private Shared Function Durata(testo As String) As Double
        Try
            Return 1.5 + If(testo, "").Length / 13.0
        Catch ex As Exception
            RegistroErrori.Registra("ScuolaGuida.Durata", ex)
            Throw
        End Try
    End Function

    Private Sub Avanti(Optional bravo As Boolean = True)
        Try
            If bravo AndAlso _i > 0 Then
                Dim b As String = _m.Dati.TestoACaso("scuola", "bravo", _m.Caso())
                If b <> "" Then _m.Dici(b)
                _m.Suona(MotoreAudio.Bip(True), 0.6F)
            End If
            _i += 1
            If _i >= _passi.Length Then
                Finita = True
                RaiseEvent Conclusa()
                Return
            End If
            Dim testo As String = _m.Dati.Scuola(PassoAttuale)
            _m.Dici(testo)
            _attesa = _m.Tempo + Durata(testo)
            Select Case PassoAttuale
                Case "sali"
                    ' la macchina della scuola, qui davanti
                    Dim v As Veicolo = _m.CreaVeicolo("utilitaria", New Luogo With {.Tratto = _m.PosPiedi.Tratto, .Distanza = Math.Min(_m.PosPiedi.Tratto.Lunghezza - 3, _m.PosPiedi.Distanza + 4)}, RuoloVeicolo.Parcheggiato)
                    v.Tuo = True
                Case "svolta"
                    _tratto = _m.Pos.Tratto
                    _angolo = _m.Pos.Angolo
                Case "sensore"
                    Dim p As Posizione = _m.Pos
                    Dim d As Double = p.Distanza + p.Verso * Math.Min(45, p.AllIncrocio - 5)
                    _ostacolo = New Veicolo With {.Tipo = _m.Dati.Veicoli("furgone"), .Pos = New Posizione(p.Tratto, d, p.Verso), .Ruolo = RuoloVeicolo.Traffico, .Salute = 140, .VelocitaVoluta = 2.5, .Velocita = 2.5}
                    _m.Veicoli.Add(_ostacolo)
                Case "corsia"
                    _corsiaFatta = False
                Case "navigatore"
                    _meta = _m.Dati.Luoghi.Values.Where(Function(l) _m.DistanzaDa(l.X, l.Y) > 300 AndAlso _m.DistanzaDa(l.X, l.Y) < 550).OrderBy(Function(l) _m.DistanzaDa(l.X, l.Y)).FirstOrDefault()
                    If _meta IsNot Nothing Then _m.ImpostaMeta(_meta, True)
            End Select
        Catch ex As Exception
            RegistroErrori.Registra("ScuolaGuida.Avanti", ex)
            Throw
        End Try
    End Sub

    Public Sub Passo(dt As Double)
        Try
            If Finita Then Return
            If _i < 0 Then
                Avanti(False)
                Return
            End If
            If _m.Tempo < _attesa Then Return
            Select Case PassoAttuale
                Case "benvenuto", "curva"
                    Avanti(False)
                Case "sali"
                    If Not _m.APiedi Then Avanti()
                Case "accelera"
                    If _m.Velocita >= 11 Then Avanti()
                Case "frena"
                    If Not _m.APiedi AndAlso Math.Abs(_m.Velocita) < 0.5 Then Avanti()
                Case "svolta"
                    If _m.Pos.Tratto IsNot _tratto Then
                        Dim giro As Double = Mappa.Differenza(_m.Pos.Angolo, _angolo)
                        If giro > Math.PI / 4 Then
                            Avanti()
                        Else
                            _m.Dici(_m.Dati.Scuola("svolta"))
                            _attesa = _m.Tempo + 4
                        End If
                        _tratto = _m.Pos.Tratto
                        _angolo = _m.Pos.Angolo
                    End If
                Case "sensore"
                    ' riuscito se ti fermi dietro al furgone; se lo urti o passa troppo tempo si va avanti lo stesso
                    Dim urtato As Boolean = _ostacolo IsNot Nothing AndAlso _m.Tempo - _ostacolo.UltimoUrto < 1
                    If urtato Then _m.Dici(_m.Dati.Scuola("sensore"))
                    If _ostacolo Is Nothing OrElse urtato OrElse _m.Tempo - _attesa > 45 OrElse _m.OstacoloDavanti() < 25 AndAlso _m.Velocita < 4 Then
                        If _ostacolo IsNot Nothing Then _m.Veicoli.Remove(_ostacolo)
                        _ostacolo = Nothing
                        Avanti()
                    End If
                Case "corsia"
                    If _m.Corsia = 1 Then _corsiaFatta = True
                    If _corsiaFatta AndAlso _m.Corsia = 0 Then Avanti()
                Case "semaforo"
                    Avanti(False)
                Case "navigatore"
                    If _meta Is Nothing OrElse _m.DistanzaDa(_meta.X, _meta.Y) < Mondo.DISTANZA_LUOGO Then Avanti()
                Case "fine"
                    Avanti(False)
            End Select
        Catch ex As Exception
            RegistroErrori.Registra("ScuolaGuida.Passo", ex)
            Throw
        End Try
    End Sub
End Class
