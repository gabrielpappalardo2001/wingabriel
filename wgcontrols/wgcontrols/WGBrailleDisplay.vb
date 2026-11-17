' ============================================================================================
' WGBrailleDisplay - tipi comuni dei driver delle barre braille
' ============================================================================================
'
' A cosa serve
'   Definisce che cosa deve saper fare una barra braille per WinGabriel (IWGBrailleDisplay) e i tasti
'   che i driver traducono in un linguaggio unico (TastoBraille), cosi' il resto del programma non sa
'   niente dei protocolli. I driver sono WGBrailleHims (Braille Sense, Braille Edge, Smart Beetle, via
'   Bluetooth, cavo seriale o WinUSB) e WGBrailleHid (barre che parlano lo standard HID braille, senza driver).
' ============================================================================================

''' <summary>I tasti di una barra braille, uniti in una combinazione (si battono insieme come un accordo).</summary>
<Flags>
Friend Enum TastoBraille As Integer
    Nessuno = 0
    Punto1 = 1
    Punto2 = 2
    Punto3 = 4
    Punto4 = 8
    Punto5 = 16
    Punto6 = 32
    Punto7 = 64
    Punto8 = 128
    Spazio = 256
    PanSinistra = 512
    PanDestra = 1024
    F1 = 2048
    F2 = 4096
    F3 = 8192
    F4 = 16384
    ''' <summary>Tasto di sinistra dello spazio, nei modelli che lo hanno separato dai punti.</summary>
    SpazioSinistro = 32768
    ''' <summary>Tasto di destra dello spazio, nei modelli che lo hanno separato dai punti.</summary>
    SpazioDestro = 65536
    BasculaSu = 131072
    BasculaGiu = 262144
    ''' <summary>I tasti di movimento accanto allo spazio (quelli che il sistema usa per scorrere i menu).</summary>
    FrecciaSu = 524288
    FrecciaGiu = 1048576
    FrecciaSinistra = 2097152
    FrecciaDestra = 4194304
End Enum

Friend Interface IWGBrailleDisplay
    Inherits IDisposable

    ReadOnly Property Nome As String
    ReadOnly Property NumeroCelle As Integer
    ''' <summary>Vero se la barra ha otto punti per cella (altrimenti sei).</summary>
    ReadOnly Property OttoPunti As Boolean
    ReadOnly Property Collegato As Boolean
    ''' <summary>Come e' collegata: Bluetooth, Cavo, Hid.</summary>
    ReadOnly Property Collegamento As String

    ''' <summary>Scrive le celle (una maschera per cella, il punto 1 e' il bit 0). Se sono meno delle celle, il resto resta vuoto.</summary>
    Sub Mostra(celle As Byte())

    Event TastiPremuti(tasti As TastoBraille)
    Event Instradamento(indice As Integer)
    Event Disconnessa()
End Interface

''' <summary>Una porta o un dispositivo dove potrebbe esserci una barra braille.</summary>
Friend Class CandidatoBraille
    ''' <summary>"Seriale", "Hid" o "WinUsb".</summary>
    Public Property Tipo As String = ""
    ''' <summary>Nome della porta COM o percorso del dispositivo.</summary>
    Public Property Percorso As String = ""
    Public Property Descrizione As String = ""
    ''' <summary>"Bluetooth" o "Cavo".</summary>
    Public Property Collegamento As String = ""
    ''' <summary>Vero se dal nome o dall'identificativo sembra proprio una barra braille.</summary>
    Public Property Probabile As Boolean

    Public Overrides Function ToString() As String
        Return Descrizione & " (" & Percorso & ")"
    End Function
End Class
