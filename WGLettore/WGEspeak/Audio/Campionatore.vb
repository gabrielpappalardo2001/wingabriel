' ============================================================================================
' Campionatore - cambia la frequenza dei campioni audio, e la coda da cui l'uscita li prende
' ============================================================================================
'
' La voce di eSpeak è a 22050 Hz, la scheda audio di solito a 48000: se ne accorge Windows e fa da sé la
' conversione, ma così ci mette del tempo suo. Qui la conversione la facciamo noi, prima, in un passaggio
' solo con la stessa frequenza della scheda. L'interpolazione è cubica (Catmull-Rom) e lavora a blocchi,
' ricordandosi i campioni di confine: la voce passa a pezzi e non si sentono stacchi.
' ============================================================================================
Imports System.Threading

''' <summary>Converte da una frequenza a un'altra, a blocchi e senza stacchi. Un campionatore per ogni flusso.</summary>
Public NotInheritable Class CampionatoreCubico

    Private ReadOnly _passo As Double
    Private _tampone(4095) As Single
    Private _conta As Integer
    Private _posizione As Double

    Public Sub New(frequenzaIngresso As Integer, frequenzaUscita As Integer)
        If frequenzaIngresso <= 0 OrElse frequenzaUscita <= 0 Then Throw New ArgumentException("Le frequenze devono essere positive")
        _passo = frequenzaIngresso / CDbl(frequenzaUscita)
        Azzera()
    End Sub

    ''' <summary>Ricomincia da capo: dopo un taglio non deve restare niente del testo di prima.</summary>
    Public Sub Azzera()
        ' L'interpolazione guarda un campione prima e due dopo quello corrente: il primo campione prima è silenzio.
        _tampone(0) = 0.0F
        _conta = 1
        _posizione = 1.0
    End Sub

    ''' <summary>Converte i primi "quanti" campioni (da -1 a 1) e restituisce quelli alla nuova frequenza.</summary>
    Public Function Converti(ingresso As Single(), quanti As Integer) As Single()
        Dim necessari As Integer = _conta + quanti
        If necessari > _tampone.Length Then Array.Resize(_tampone, Math.Max(necessari, _tampone.Length * 2))
        Array.Copy(ingresso, 0, _tampone, _conta, quanti)
        _conta = necessari

        Dim massimo As Integer = CInt(Math.Ceiling((_conta - _posizione) / _passo)) + 2
        Dim uscita(Math.Max(massimo, 1) - 1) As Single
        Dim n As Integer = 0
        While CInt(Math.Floor(_posizione)) + 2 < _conta
            Dim i As Integer = CInt(Math.Floor(_posizione))
            Dim t As Single = CSng(_posizione - i)
            Dim x0 As Single = _tampone(i - 1)
            Dim x1 As Single = _tampone(i)
            Dim x2 As Single = _tampone(i + 1)
            Dim x3 As Single = _tampone(i + 2)
            Dim a0 As Single = -0.5F * x0 + 1.5F * x1 - 1.5F * x2 + 0.5F * x3
            Dim a1 As Single = x0 - 2.5F * x1 + 2.0F * x2 - 0.5F * x3
            Dim a2 As Single = -0.5F * x0 + 0.5F * x2
            ' La cubica sfora oltre -1..1: quel picco, una volta tagliato dalla scheda, è uno scoppiettio.
            Dim campione As Single = ((a0 * t + a1) * t + a2) * t + x1
            If campione > 1.0F Then campione = 1.0F
            If campione < -1.0F Then campione = -1.0F
            uscita(n) = campione
            n += 1
            _posizione += _passo
        End While

        ' Si scartano i campioni che non servono più, tenendo quello prima del corrente.
        Dim daTenere As Integer = CInt(Math.Floor(_posizione)) - 1
        If daTenere > 0 Then
            Array.Copy(_tampone, daTenere, _tampone, 0, _conta - daTenere)
            _conta -= daTenere
            _posizione -= daTenere
        End If
        Array.Resize(uscita, n)
        Return uscita
    End Function

End Class

''' <summary>
''' La coda dei campioni (mono, da -1 a 1, alla frequenza dell'uscita) tra chi scrive, il thread che
''' sintetizza la voce, e chi legge, il thread dell'uscita audio. Un blocco brevissimo protegge gli indici.
''' </summary>
Friend NotInheritable Class CodaCampioni

    Private ReadOnly _dati As Single()
    Private _inizio As Integer
    Private _conta As Integer
    Private ReadOnly _blocco As New Object()

    Public Sub New(capacita As Integer)
        _dati = New Single(Math.Max(capacita, 16) - 1) {}
    End Sub

    Public ReadOnly Property Conteggio As Integer
        Get
            SyncLock _blocco
                Return _conta
            End SyncLock
        End Get
    End Property

    ''' <summary>Aggiunge i campioni; se la coda è piena quelli che non entrano si perdono. Restituisce quanti sono entrati.</summary>
    Public Function Scrivi(campioni As Single(), quanti As Integer) As Integer
        SyncLock _blocco
            Dim liberi As Integer = _dati.Length - _conta
            Dim daScrivere As Integer = Math.Min(quanti, liberi)
            Dim fine As Integer = (_inizio + _conta) Mod _dati.Length
            Dim primoPezzo As Integer = Math.Min(daScrivere, _dati.Length - fine)
            Array.Copy(campioni, 0, _dati, fine, primoPezzo)
            If daScrivere > primoPezzo Then Array.Copy(campioni, primoPezzo, _dati, 0, daScrivere - primoPezzo)
            _conta += daScrivere
            Return daScrivere
        End SyncLock
    End Function

    ''' <summary>Prende fino a "quanti" campioni dalla coda; restituisce quanti ne ha presi.</summary>
    Public Function Leggi(destinazione As Single(), quanti As Integer) As Integer
        SyncLock _blocco
            Dim daLeggere As Integer = Math.Min(quanti, _conta)
            Dim primoPezzo As Integer = Math.Min(daLeggere, _dati.Length - _inizio)
            Array.Copy(_dati, _inizio, destinazione, 0, primoPezzo)
            If daLeggere > primoPezzo Then Array.Copy(_dati, 0, destinazione, primoPezzo, daLeggere - primoPezzo)
            _inizio = (_inizio + daLeggere) Mod _dati.Length
            _conta -= daLeggere
            Return daLeggere
        End SyncLock
    End Function

    Public Sub Azzera()
        SyncLock _blocco
            _inizio = 0
            _conta = 0
        End SyncLock
    End Sub

End Class
