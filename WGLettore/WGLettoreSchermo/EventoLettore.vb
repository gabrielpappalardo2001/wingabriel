Imports UIAutomationClient

' Definizione degli eventi, dei tasti, dei comandi e delle azioni gestite dal lettore di schermo.
Friend Enum TipoEventoLettore
    Focus
    PrimoPiano
    CambioValore
    SelezioneTesto
    Tasto
    Comando
    SelezioneElemento
    Avviso
    RegioneViva
    FocusMsaa
    Notifica
    TestoModificato
    ProprietaCambiata
    StrutturaCambiata
    MenuAperto
    MenuChiuso
    FinestraChiusa
    PosizioneTesto
    Tooltip
    Ricerca
    VoceUtente
    FocusJava
    MenuTeamsTrovato
    MenuBarraTrovato
End Enum

Friend Enum AzioneTastoLettore
    Navigazione
    Modifica
    Selezione
    NavigazioneWeb
    Copia
    SelezioneVoce
    SoloModulo
End Enum

Friend Enum ComandoLettore
    Titolo
    Focus
    Ora
    Data
    BarraStato
    Riga
    LetturaContinua
    Finestra
    AttivaDisattiva
    Selezione
    Precedente
    Successivo
    Contenitore
    Contenuto
    NavigazioneWeb
    AttivaElemento
    Aiuto
    AggiornaNavigazione
    Descrizione
    Compita
    Parola
    Carattere
    EsciScrittura
    StatoMaiuscole
    Commutatore
    Messaggio
    RigaTabella
    ColonnaTabella
    AiutoTasti
    Formattazione
    PausaLettura
    TracciaMouse
    PortaMouse
    Clicca
    ElencoElementi
    CercaPagina
    ScaricaAlbero
    Prolissita
    Braille
    RevisionePiatta
    Ocr
    LettereRapide
    MenuLettore
    RipetiUltimo
    StoricoVoce
    CopiaUltimo
    Appunti
    SalvaProfilo
    TogliProfilo
    ComponenteEsterno
End Enum

Friend NotInheritable Class EventoLettore
    Public Property Tipo As TipoEventoLettore
    Public Property Elemento As IUIAutomationElement
    Public Property Finestra As IntPtr
    Public Property Sequenza As Integer
    Public Property CodiceTasto As Integer
    Public Property Ctrl As Boolean
    Public Property Maiuscolo As Boolean
    Public Property Alt As Boolean
    Public Property Comando As ComandoLettore
    Public Property Azione As AzioneTastoLettore
    Public Property FinestraAperta As Boolean
    Public Property IdOggetto As Integer
    Public Property IdFiglio As Integer
    Public Property Testo As String
    Public Property PrioritaVoce As Integer
    Public Property IdProprieta As Integer
    Public Property Epoca As Integer
    Public Property ChiaveSostituzione As String = ""
    Public Property IdAttivita As String = ""
    Public Property EcoDetto As Boolean
    Public Property AltGr As Boolean
End Class