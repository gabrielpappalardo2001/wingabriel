' Interfacce per la gestione delle schede, la chiusura delle finestre e gli annunci vocali di ingresso.
Public Interface IWGControlloConSchede
    Function CambiaScheda(avanti As Boolean) As Boolean
End Interface

''' <summary>Finestre che sanno dire cosa sono quando ci si arriva cambiando applicazione (campi, browser).</summary>
''' <summary>Finestre che sanno chiudersi come se lo chiedesse l'utente (con le loro domande di salvataggio).</summary>
Public Interface IWGChiudibile
    Sub RichiediChiusura()
End Interface

Public Interface IWGAnnuncioIngresso
    Sub AnnunciaIngresso()
End Interface
