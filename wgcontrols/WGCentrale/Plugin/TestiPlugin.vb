''' <summary>
''' I testi che WinGabriel dice quando gestisce i plugin. Quelli lunghi sono stati scritti da Gemini; {nome} è il nome del plugin.
''' </summary>
Friend Module TestiPlugin

    Friend Const DomandaNonFirmato As String = "Il plugin «{nome}» non è stato creato dagli sviluppatori di WinGabriel. Il suo codice viene controllato e non può leggere i tuoi dati senza permesso, ma può usare internet e mostrare messaggi. Vuoi attivarlo?"
    Friend Const DomandaAggiornato As String = "Il file del plugin «{nome}» è diverso da quando lo hai attivato. Vuoi attivare la nuova versione?"
    Friend Const DomandaPerdeFirma As String = "Il plugin «{nome}» non è più firmato dagli sviluppatori di WinGabriel. Questo fatto è insolito, quindi ti consigliamo di attivarlo solo se sai perché è cambiato. Vuoi attivarlo?"
    Friend Const PluginNonSicuro As String = "Il plugin «{nome}» non è stato caricato perché usa funzioni non consentite."
    Friend Const PluginRevocato As String = "Il plugin «{nome}» è stato bloccato perché non è più considerato sicuro."
    Friend Const DisattivatoPerErrori As String = "Il plugin «{nome}» è stato disattivato perché ha dato troppi errori di seguito. Il resto di WinGabriel continua a funzionare."
    Friend Const DisattivatoPerAbuso As String = "Il plugin «{nome}» è stato disattivato perché inviava troppe richieste o troppi messaggi in poco tempo."
    Friend Const NonPartitoInTempo As String = "Il plugin «{nome}» è stato disattivato perché ci ha messo troppo tempo ad avviarsi."
    Friend Const AvvioSicuroTutti As String = "WinGabriel non si era chiuso bene l'ultima volta, quindi per sicurezza i plugin non sono stati caricati. Puoi riattivarli nelle impostazioni alla voce Plugin."
    Friend Const AvvioSicuroUno As String = "Il plugin «{nome}» sembra aver bloccato WinGabriel all'avvio ed è stato disattivato. Puoi riattivarlo dalle impostazioni, alla voce Plugin."
    Friend Const DomandaPermesso As String = "Il plugin «{nome}» chiede di {cosa}. Vuoi consentirlo?"
    Friend Const DomandaPermessoMotivo As String = "Il plugin «{nome}» chiede di {cosa}. Il motivo fornito è: {motivo}. Vuoi consentirlo?"
    Friend Const PermessoConcesso As String = "Il plugin «{nome}» ora può {cosa}. Puoi revocare questo permesso dalle impostazioni, alla voce Plugin."
    Friend Const PermessoRifiutato As String = "Il plugin «{nome}» non potrà {cosa}."
    Friend Const DomandaRevocaPermesso As String = "Vuoi revocare al plugin «{nome}» il permesso di {cosa}?"
    Friend Const PermessoRevocato As String = "Il plugin «{nome}» non può più {cosa}."
    Friend Const PermessoNonConcesso As String = "Il permesso per {cosa} non è stato concesso. Il plugin «{nome}» lo chiederà quando gli servirà."
    Friend Const NessunPermessoPossibile As String = "Il plugin «{nome}» non ha alcun permesso concesso."
    Friend Const RevocaElenco As String = "L'elenco dei plugin bloccati è stato aggiornato e il plugin «{nome}» è stato disattivato perché bloccato."
    Friend Const TitoloPassword As String = "Password per il plugin «{nome}»"
    Friend Const PasswordErrata As String = "Password errata."
    Friend Const PasswordNonScritta As String = "Password non indicata."
    Friend Const PluginAttivato As String = "Il plugin «{nome}» è attivo ed è già in funzione."
    Friend Const PluginRifiutato As String = "Il plugin «{nome}» resta disattivato. Puoi attivarlo in qualsiasi momento nelle impostazioni, alla voce Plugin."
    Friend Const PluginDisattivato As String = "Il plugin «{nome}» è stato disattivato e non è più in funzione."
    Friend Const ErroreAvvio As String = "Il plugin «{nome}» non si è avviato."
    Friend Const ErroreAzione As String = "Il plugin «{nome}» ha avuto un problema e non ha terminato l'operazione. Il resto di WinGabriel continua a funzionare normalmente."
    Friend Const VersioneNonCompatibile As String = "Il plugin «{nome}» è stato creato per una versione diversa dell'interfaccia e non è stato caricato."
    Friend Const DomandaDisinstalla As String = "Tutti i file del plugin «{nome}» verranno cancellati e non si potranno più recuperare. Vuoi disinstallare il plugin?"
    Friend Const PluginDisinstallato As String = "Il plugin «{nome}» è stato disinstallato."
    Friend Const DisinstallazioneFallita As String = "Non è stato possibile cancellare i file del plugin «{nome}»."
    Friend Const NessunPlugin As String = "Nessun plugin installato."

    ''' <summary>Il testo con il nome del plugin al posto di {nome}.</summary>
    Friend Function Con(testo As String, nome As String) As String
        Return testo.Replace("{nome}", If(nome, ""))
    End Function

End Module
