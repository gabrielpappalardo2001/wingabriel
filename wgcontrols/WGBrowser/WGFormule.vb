Imports System.Collections.Generic
Imports System.Globalization
Imports System.Linq
Imports System.Text
Imports System.Text.RegularExpressions
Imports System.Xml.Linq
Imports wgcontrols

''' <summary>
''' Formule matematiche delle pagine: converte il MathML (quello nativo, di Wikipedia, KaTeX e MathJax, che
''' includono tutti il MathML per l'accessibilità) in italiano parlato e in un albero da esplorare a pezzi.
''' Le parti semplici si leggono in forma breve ("a fratto b", "x al quadrato"), quelle composte con inizio e
''' fine ("frazione: numeratore ..., denominatore ..., fine frazione") per capire dove finiscono.
''' </summary>
Public Class WGFormula

    Public Class NodoFormula
        ' Ruolo del pezzo nel pezzo che lo contiene: "numeratore", "esponente", "riga 2"...
        Public Etichetta As String = ""
        Public Parlato As String = ""
        ' Un simbolo, un numero o un gruppo di al massimo due simboli: si legge senza "fine ...".
        Public Semplice As Boolean = False
        Public Testo As String = ""
        Public ReadOnly Figli As New List(Of NodoFormula)()

        Public ReadOnly Property Descrizione As String
            Get
                Return If(Etichetta <> "", Etichetta & ": ", "") & Parlato
            End Get
        End Property
    End Class

    Private Shared ReadOnly log As New Logger()

    Public Property Parlato As String = ""
    Public Property LaTeX As String = ""
    Public Property Radice As NodoFormula

    Private Shared ReadOnly Operatori As New Dictionary(Of String, String)(StringComparer.Ordinal) From {
        {"+", "più"}, {"-", "meno"}, {"−", "meno"}, {"–", "meno"}, {"×", "per"}, {"·", "per"}, {"⋅", "per"}, {"*", "per"}, {"∗", "per"},
        {"/", "diviso"}, {"÷", "diviso"}, {"∕", "diviso"}, {"=", "uguale"}, {"≠", "diverso da"}, {"<", "minore di"}, {">", "maggiore di"},
        {"≤", "minore o uguale a"}, {"≥", "maggiore o uguale a"}, {"⩽", "minore o uguale a"}, {"⩾", "maggiore o uguale a"},
        {"≪", "molto minore di"}, {"≫", "molto maggiore di"}, {"±", "più o meno"}, {"∓", "meno o più"}, {"≈", "circa uguale a"},
        {"≃", "asintotico a"}, {"∼", "simile a"}, {"≡", "equivalente a"}, {"∝", "proporzionale a"}, {"→", "tende a"}, {"⟶", "tende a"},
        {"←", "freccia a sinistra"}, {"↦", "va in"}, {"⇒", "implica"}, {"⟹", "implica"}, {"⇔", "se e solo se"}, {"⟺", "se e solo se"},
        {"∞", "infinito"}, {"∂", "derivata parziale"}, {"∇", "nabla"}, {"∈", "appartiene a"}, {"∉", "non appartiene a"}, {"∋", "contiene"},
        {"⊂", "contenuto in"}, {"⊆", "contenuto o uguale a"}, {"⊃", "contiene"}, {"⊇", "contiene o è uguale a"}, {"∪", "unione"},
        {"∩", "intersezione"}, {"∖", "meno"}, {"∅", "insieme vuoto"}, {"∀", "per ogni"}, {"∃", "esiste"}, {"∄", "non esiste"},
        {"¬", "non"}, {"∧", "e"}, {"∨", "o"}, {"°", "gradi"}, {"′", "primo"}, {"″", "secondo"}, {"!", "fattoriale"}, {"%", "per cento"},
        {",", "virgola"}, {";", "punto e virgola"}, {":", "tale che"}, {"|", "barra verticale"}, {"‖", "norma"}, {"∣", "divide"},
        {"(", "aperta parentesi"}, {")", "chiusa parentesi"}, {"[", "aperta quadra"}, {"]", "chiusa quadra"},
        {"{", "aperta graffa"}, {"}", "chiusa graffa"}, {"⟨", "aperta angolare"}, {"⟩", "chiusa angolare"},
        {"⌊", "parte intera inferiore di"}, {"⌋", "fine parte intera"}, {"⌈", "parte intera superiore di"}, {"⌉", "fine parte intera"},
        {"…", "puntini"}, {"⋯", "puntini"}, {"∘", "composto"}, {"⊥", "perpendicolare a"}, {"∥", "parallelo a"}, {"∠", "angolo"},
        {"△", "triangolo"}, {"∑", "sommatoria"}, {"∏", "produttoria"}, {"∫", "integrale"}, {"∬", "integrale doppio"},
        {"∭", "integrale triplo"}, {"∮", "integrale di linea chiuso"}, {"√", "radice di"}, {"⊕", "somma diretta"}, {"⊗", "prodotto tensoriale"}
    }

    Private Shared ReadOnly GrandiOperatori As New Dictionary(Of String, String)(StringComparer.Ordinal) From {
        {"∑", "sommatoria"}, {"∏", "produttoria"}, {"∐", "coprodotto"}, {"∫", "integrale"}, {"∬", "integrale doppio"},
        {"∭", "integrale triplo"}, {"∮", "integrale di linea chiuso"}, {"⋃", "unione"}, {"⋂", "intersezione"}
    }

    Private Shared ReadOnly Funzioni As New Dictionary(Of String, String)(StringComparer.Ordinal) From {
        {"sin", "seno"}, {"sen", "seno"}, {"cos", "coseno"}, {"tan", "tangente"}, {"tg", "tangente"}, {"cot", "cotangente"},
        {"cotg", "cotangente"}, {"sec", "secante"}, {"csc", "cosecante"}, {"arcsin", "arcoseno"}, {"arccos", "arcocoseno"},
        {"arctan", "arcotangente"}, {"sinh", "seno iperbolico"}, {"cosh", "coseno iperbolico"}, {"tanh", "tangente iperbolica"},
        {"log", "logaritmo"}, {"ln", "logaritmo naturale"}, {"lg", "logaritmo in base 10"}, {"exp", "esponenziale"},
        {"lim", "limite"}, {"max", "massimo"}, {"min", "minimo"}, {"sup", "estremo superiore"}, {"inf", "estremo inferiore"},
        {"det", "determinante"}, {"dim", "dimensione"}, {"mod", "modulo"}, {"gcd", "massimo comun divisore"}, {"mcd", "massimo comun divisore"}
    }

    Private Shared ReadOnly Lettere As New Dictionary(Of String, String)(StringComparer.Ordinal) From {
        {"α", "alfa"}, {"β", "beta"}, {"γ", "gamma"}, {"δ", "delta"}, {"ε", "epsilon"}, {"ϵ", "epsilon"}, {"ζ", "zeta"}, {"η", "eta"},
        {"θ", "theta"}, {"ϑ", "theta"}, {"ι", "iota"}, {"κ", "kappa"}, {"λ", "lambda"}, {"μ", "mu"}, {"ν", "nu"}, {"ξ", "xi"},
        {"π", "pi greco"}, {"ρ", "rho"}, {"σ", "sigma"}, {"ς", "sigma"}, {"τ", "tau"}, {"υ", "upsilon"}, {"φ", "fi"}, {"ϕ", "fi"},
        {"χ", "chi"}, {"ψ", "psi"}, {"ω", "omega"}, {"Γ", "gamma maiuscola"}, {"Δ", "delta maiuscola"}, {"Θ", "theta maiuscola"},
        {"Λ", "lambda maiuscola"}, {"Ξ", "xi maiuscola"}, {"Π", "pi maiuscola"}, {"Σ", "sigma maiuscola"}, {"Φ", "fi maiuscola"},
        {"Ψ", "psi maiuscola"}, {"Ω", "omega maiuscola"}, {"ℝ", "insieme dei numeri reali"}, {"ℕ", "insieme dei numeri naturali"},
        {"ℤ", "insieme dei numeri interi"}, {"ℚ", "insieme dei numeri razionali"}, {"ℂ", "insieme dei numeri complessi"},
        {"ℏ", "acca tagliato"}, {"ℓ", "elle"}, {"∞", "infinito"}
    }

    Private Shared ReadOnly Accenti As New Dictionary(Of String, String)(StringComparer.Ordinal) From {
        {"^", "cappuccio"}, {"ˆ", "cappuccio"}, {"̂", "cappuccio"}, {"¯", "segnato"}, {"‾", "segnato"}, {"_", "segnato"}, {"̄", "segnato"},
        {"→", "vettore"}, {"⃗", "vettore"}, {"˙", "punto"}, {"̇", "punto"}, {"¨", "due punti"}, {"̈", "due punti"}, {"~", "tilde"},
        {"˜", "tilde"}, {"̃", "tilde"}, {"⏞", "graffa sopra"}, {"⏟", "graffa sotto"}
    }

    ' Operatori invisibili del MathML: applicazione di funzione, prodotto e separatore sottintesi.
    Private Shared ReadOnly Invisibili As String() = {ChrW(&H2061), ChrW(&H2062), ChrW(&H2063), ChrW(&H2064), ChrW(&H200B)}

    ''' <summary>Converte il MathML serializzato; Nothing se il testo non è MathML valido.</summary>
    Public Shared Function DaMathML(xml As String) As WGFormula
        Try
            If String.IsNullOrWhiteSpace(xml) Then Return Nothing
            Dim documento As XDocument = XDocument.Parse(xml, LoadOptions.None)
            Dim math As XElement = documento.Root
            If math Is Nothing OrElse math.Name.LocalName <> "math" Then Return Nothing
            Dim formula As New WGFormula()
            Dim alternativo As String = If(CStr(math.Attribute("alttext")), "")
            Dim annotazione As XElement = math.Descendants().FirstOrDefault(Function(e) e.Name.LocalName = "annotation" AndAlso String.Equals(CStr(e.Attribute("encoding")), "application/x-tex", StringComparison.OrdinalIgnoreCase))
            formula.LaTeX = PulisciLaTeX(If(annotazione IsNot Nothing, annotazione.Value, alternativo))
            formula.Radice = Converti(math)
            If formula.Radice Is Nothing Then Return Nothing
            formula.Parlato = Pulisci(formula.Radice.Parlato)
            If formula.Parlato = "" Then Return Nothing
            Return formula
        Catch ex As Exception
            log.ScriviLog("WGFormula.DaMathML: " & ex.Message)
            Return Nothing
        End Try
    End Function

    Private Shared Function PulisciLaTeX(testo As String) As String
        Dim t As String = If(testo, "").Trim()
        Dim m As Match = Regex.Match(t, "^\{\\displaystyle\s*(.*)\}$", RegexOptions.Singleline)
        If m.Success Then t = m.Groups(1).Value.Trim()
        Return t
    End Function

    Private Shared Function Pulisci(testo As String) As String
        Return Regex.Replace(If(testo, ""), "\s+", " ").Replace(" ,", ",").Trim()
    End Function

    Private Shared Function TestoDi(el As XElement) As String
        Dim t As String = el.Value
        For Each invisibile As String In Invisibili
            t = t.Replace(invisibile, "")
        Next
        Return t.Replace(ChrW(&HA0), " ").Trim()
    End Function

    Private Shared Function Figli(el As XElement) As List(Of XElement)
        Return el.Elements().ToList()
    End Function

    Private Shared Function Converti(el As XElement) As NodoFormula
        Try
            Select Case el.Name.LocalName.ToLowerInvariant()
                Case "math", "mrow", "mstyle", "mpadded", "menclose", "merror", "mtd", "mphantom"
                    If el.Name.LocalName = "mphantom" Then Return Nothing
                    Return Sequenza(Figli(el))
                Case "semantics"
                    Dim primo As XElement = el.Elements().FirstOrDefault(Function(e) Not e.Name.LocalName.StartsWith("annotation", StringComparison.Ordinal))
                    Return If(primo Is Nothing, Nothing, Converti(primo))
                Case "annotation", "annotation-xml", "mspace", "none", "mprescripts", "maligngroup", "malignmark"
                    Return Nothing
                Case "mi" : Return Identificatore(TestoDi(el))
                Case "mn" : Return Numero(TestoDi(el))
                Case "mo" : Return Operatore(TestoDi(el))
                Case "mtext", "ms"
                    Dim t As String = TestoDi(el)
                    If t = "" Then Return Nothing
                    Return New NodoFormula() With {.Parlato = t, .Testo = t, .Semplice = Not t.Contains(" ")}
                Case "mfrac" : Return Frazione(Figli(el), CStr(el.Attribute("linethickness")))
                Case "msqrt" : Return RadiceDi(Sequenza(Figli(el)), Nothing)
                Case "mroot"
                    Dim parti As List(Of XElement) = Figli(el)
                    If parti.Count < 2 Then Return Sequenza(parti)
                    Return RadiceDi(Converti(parti(0)), Converti(parti(1)))
                Case "msup"
                    Dim parti As List(Of XElement) = Figli(el)
                    If parti.Count < 2 Then Return Sequenza(parti)
                    Return Potenza(Converti(parti(0)), Converti(parti(1)))
                Case "msub"
                    Dim parti As List(Of XElement) = Figli(el)
                    If parti.Count < 2 Then Return Sequenza(parti)
                    Return Pedice(Converti(parti(0)), Converti(parti(1)), Nothing)
                Case "msubsup"
                    Dim parti As List(Of XElement) = Figli(el)
                    If parti.Count < 3 Then Return Sequenza(parti)
                    Return Pedice(Converti(parti(0)), Converti(parti(1)), Converti(parti(2)))
                Case "munder"
                    Dim parti As List(Of XElement) = Figli(el)
                    If parti.Count < 2 Then Return Sequenza(parti)
                    Return SottoSopra(Converti(parti(0)), Converti(parti(1)), Nothing)
                Case "mover"
                    Dim parti As List(Of XElement) = Figli(el)
                    If parti.Count < 2 Then Return Sequenza(parti)
                    Return SottoSopra(Converti(parti(0)), Nothing, Converti(parti(1)))
                Case "munderover"
                    Dim parti As List(Of XElement) = Figli(el)
                    If parti.Count < 3 Then Return Sequenza(parti)
                    Return SottoSopra(Converti(parti(0)), Converti(parti(1)), Converti(parti(2)))
                Case "mmultiscripts"
                    Dim parti As List(Of XElement) = Figli(el).Where(Function(e) e.Name.LocalName <> "mprescripts").ToList()
                    If parti.Count < 3 Then Return Sequenza(parti)
                    Return Pedice(Converti(parti(0)), Converti(parti(1)), Converti(parti(2)))
                Case "mfenced"
                    Dim apertura As String = If(CStr(el.Attribute("open")), "(")
                    Dim chiusura As String = If(CStr(el.Attribute("close")), ")")
                    Dim elementi As New List(Of NodoFormula)()
                    elementi.Add(Operatore(apertura))
                    Dim interni As List(Of XElement) = Figli(el)
                    For i As Integer = 0 To interni.Count - 1
                        If i > 0 Then elementi.Add(Operatore(","))
                        elementi.Add(Converti(interni(i)))
                    Next
                    elementi.Add(Operatore(chiusura))
                    Return Unisci(elementi)
                Case "mtable" : Return Tabella(el)
                Case Else
                    Return Sequenza(Figli(el))
            End Select
        Catch ex As Exception
            log.ScriviLog("WGFormula.Converti " & el.Name.LocalName & ": " & ex.Message)
            Return Nothing
        End Try
    End Function

    Private Shared Function Sequenza(elementi As List(Of XElement)) As NodoFormula
        Return Unisci(elementi.Select(Function(e) Converti(e)).ToList())
    End Function

    ' Una fila di pezzi: se ne resta uno solo è lui stesso, altrimenti un gruppo che li contiene.
    ' Lo stesso vale per la sequenza "limite per x tende a 0 di" seguita dalla funzione: resta nel gruppo.
    Private Shared Function Unisci(nodi As List(Of NodoFormula)) As NodoFormula
        Dim validi As List(Of NodoFormula) = nodi.Where(Function(n) n IsNot Nothing AndAlso n.Parlato <> "").ToList()
        If validi.Count = 0 Then Return Nothing
        If validi.Count = 1 Then Return validi(0)
        Dim gruppo As New NodoFormula()
        gruppo.Figli.AddRange(validi)
        gruppo.Parlato = Pulisci(String.Join(" ", validi.Select(Function(n) n.Parlato)))
        gruppo.Semplice = validi.Count <= 2 AndAlso validi.All(Function(n) n.Semplice)
        Return gruppo
    End Function

    Private Shared Function Identificatore(testo As String) As NodoFormula
        If testo = "" Then Return Nothing
        Dim parlato As String = testo
        Dim nome As String = Nothing
        If Funzioni.TryGetValue(testo, nome) Then
            parlato = nome
        ElseIf Lettere.TryGetValue(testo, nome) Then
            parlato = nome
        ElseIf testo.Length > 1 AndAlso testo.All(Function(c) Lettere.ContainsKey(c.ToString())) Then
            parlato = String.Join(" ", testo.Select(Function(c) Lettere(c.ToString())))
        End If
        Return New NodoFormula() With {.Parlato = parlato, .Testo = testo, .Semplice = True}
    End Function

    Private Shared Function Numero(testo As String) As NodoFormula
        If testo = "" Then Return Nothing
        ' In italiano la virgola separa i decimali: "3.14" si legge "3,14".
        Dim parlato As String = If(Regex.IsMatch(testo, "^\d+\.\d+$"), testo.Replace(".", ","), testo)
        Return New NodoFormula() With {.Parlato = parlato, .Testo = testo, .Semplice = True}
    End Function

    Private Shared Function Operatore(testo As String) As NodoFormula
        If testo = "" Then Return Nothing
        Dim nome As String = Nothing
        Dim parlato As String = If(Operatori.TryGetValue(testo, nome), nome, If(Funzioni.TryGetValue(testo, nome), nome, testo))
        Return New NodoFormula() With {.Parlato = parlato, .Testo = testo, .Semplice = True}
    End Function

    Private Shared Function Etichetta(nodo As NodoFormula, testo As String) As NodoFormula
        If nodo Is Nothing Then nodo = New NodoFormula() With {.Parlato = "vuoto", .Semplice = True}
        nodo.Etichetta = testo
        Return nodo
    End Function

    Private Shared Function Frazione(parti As List(Of XElement), spessore As String) As NodoFormula
        If parti.Count < 2 Then Return Sequenza(parti)
        Dim numeratore As NodoFormula = Etichetta(Converti(parti(0)), "numeratore")
        Dim denominatore As NodoFormula = Etichetta(Converti(parti(1)), "denominatore")
        Dim nodo As New NodoFormula()
        nodo.Figli.Add(numeratore)
        nodo.Figli.Add(denominatore)
        If spessore = "0" OrElse spessore = "0px" Then
            ' Senza linea di frazione: il coefficiente binomiale.
            nodo.Parlato = "coefficiente binomiale " & numeratore.Parlato & " su " & denominatore.Parlato
        ElseIf numeratore.Semplice AndAlso denominatore.Semplice Then
            nodo.Parlato = numeratore.Parlato & " fratto " & denominatore.Parlato
        Else
            nodo.Parlato = "frazione: numeratore " & numeratore.Parlato & ", denominatore " & denominatore.Parlato & ", fine frazione"
        End If
        Return nodo
    End Function

    Private Shared Function RadiceDi(radicando As NodoFormula, indice As NodoFormula) As NodoFormula
        Dim nodo As New NodoFormula()
        radicando = Etichetta(radicando, "radicando")
        Dim nome As String = "radice quadrata"
        If indice IsNot Nothing Then
            Select Case indice.Testo
                Case "2" : nome = "radice quadrata"
                Case "3" : nome = "radice cubica"
                Case "4" : nome = "radice quarta"
                Case Else : nome = "radice di indice " & indice.Parlato
            End Select
            nodo.Figli.Add(Etichetta(indice, "indice"))
        End If
        nodo.Figli.Add(radicando)
        nodo.Parlato = nome & " di " & radicando.Parlato & If(radicando.Semplice, "", ", fine radice")
        Return nodo
    End Function

    Private Shared Function Potenza(baseNodo As NodoFormula, esponente As NodoFormula) As NodoFormula
        If baseNodo Is Nothing Then Return esponente
        If esponente Is Nothing Then Return baseNodo
        Dim nodo As New NodoFormula()
        nodo.Figli.Add(Etichetta(baseNodo, "base"))
        nodo.Figli.Add(Etichetta(esponente, "esponente"))
        Select Case esponente.Testo
            Case "2" : nodo.Parlato = baseNodo.Parlato & " al quadrato"
            Case "3" : nodo.Parlato = baseNodo.Parlato & " al cubo"
            Case "′", "'" : nodo.Parlato = baseNodo.Parlato & " primo"
            Case "″", "''" : nodo.Parlato = baseNodo.Parlato & " secondo"
            Case "∘", "°" : nodo.Parlato = baseNodo.Parlato & " gradi"
            Case "T" : nodo.Parlato = baseNodo.Parlato & " trasposta"
            Case Else
                nodo.Parlato = baseNodo.Parlato & " elevato a " & esponente.Parlato & If(esponente.Semplice, "", ", fine esponente")
        End Select
        Return nodo
    End Function

    Private Shared Function Pedice(baseNodo As NodoFormula, sotto As NodoFormula, sopra As NodoFormula) As NodoFormula
        If baseNodo Is Nothing Then Return Unisci(New List(Of NodoFormula) From {sotto, sopra})
        If GrandiOperatori.ContainsKey(baseNodo.Testo) Then Return GrandeOperatore(baseNodo, sotto, sopra)
        Dim nodo As New NodoFormula()
        nodo.Figli.Add(Etichetta(baseNodo, "base"))
        Dim parlato As String = baseNodo.Parlato
        If sotto IsNot Nothing Then
            nodo.Figli.Add(Etichetta(sotto, "pedice"))
            parlato &= " pedice " & sotto.Parlato & If(sotto.Semplice, "", ", fine pedice")
        End If
        If sopra IsNot Nothing Then
            nodo.Figli.Add(Etichetta(sopra, "esponente"))
            parlato &= " elevato a " & sopra.Parlato & If(sopra.Semplice, "", ", fine esponente")
        End If
        nodo.Parlato = parlato
        Return nodo
    End Function

    Private Shared Function SottoSopra(baseNodo As NodoFormula, sotto As NodoFormula, sopra As NodoFormula) As NodoFormula
        If baseNodo Is Nothing Then Return Unisci(New List(Of NodoFormula) From {sotto, sopra})
        If GrandiOperatori.ContainsKey(baseNodo.Testo) Then Return GrandeOperatore(baseNodo, sotto, sopra)
        Dim nodo As New NodoFormula()
        nodo.Figli.Add(Etichetta(baseNodo, "base"))
        If baseNodo.Testo = "lim" AndAlso sotto IsNot Nothing AndAlso sopra Is Nothing Then
            nodo.Figli.Add(Etichetta(sotto, "condizione"))
            nodo.Parlato = "limite per " & sotto.Parlato & " di"
            Return nodo
        End If
        Dim accento As String = Nothing
        If sopra IsNot Nothing AndAlso sotto Is Nothing AndAlso Accenti.TryGetValue(sopra.Testo, accento) Then
            nodo.Parlato = If(accento = "vettore", "vettore " & baseNodo.Parlato, baseNodo.Parlato & " " & accento)
            Return nodo
        End If
        If sotto IsNot Nothing AndAlso sopra Is Nothing AndAlso Accenti.TryGetValue(sotto.Testo, accento) Then
            nodo.Parlato = baseNodo.Parlato & " " & accento
            Return nodo
        End If
        Dim parlato As String = baseNodo.Parlato
        If sotto IsNot Nothing Then
            nodo.Figli.Add(Etichetta(sotto, "sotto"))
            parlato &= " con sotto " & sotto.Parlato
        End If
        If sopra IsNot Nothing Then
            nodo.Figli.Add(Etichetta(sopra, "sopra"))
            parlato &= If(sotto IsNot Nothing, " e", " con") & " sopra " & sopra.Parlato
        End If
        nodo.Parlato = parlato
        Return nodo
    End Function

    ' Sommatorie, produttorie e integrali: "sommatoria per i uguale 1 fino a n di", "integrale da 0 a 1 di".
    Private Shared Function GrandeOperatore(operatoreNodo As NodoFormula, sotto As NodoFormula, sopra As NodoFormula) As NodoFormula
        Dim nodo As New NodoFormula()
        Dim nome As String = GrandiOperatori(operatoreNodo.Testo)
        nodo.Figli.Add(Etichetta(operatoreNodo, "operatore"))
        Dim integrale As Boolean = nome.StartsWith("integrale", StringComparison.Ordinal)
        Dim parlato As String = nome
        If sotto IsNot Nothing Then
            nodo.Figli.Add(Etichetta(sotto, If(integrale, "estremo inferiore", "da")))
            parlato &= If(integrale, " da ", " per ") & sotto.Parlato
        End If
        If sopra IsNot Nothing Then
            nodo.Figli.Add(Etichetta(sopra, If(integrale, "estremo superiore", "fino a")))
            parlato &= If(integrale, " a ", " fino a ") & sopra.Parlato
        End If
        nodo.Parlato = parlato & " di"
        Return nodo
    End Function

    Private Shared Function Tabella(el As XElement) As NodoFormula
        Dim nodo As New NodoFormula()
        Dim righe As List(Of XElement) = el.Elements().Where(Function(e) e.Name.LocalName = "mtr" OrElse e.Name.LocalName = "mlabeledtr").ToList()
        Dim colonne As Integer = 0
        Dim testoRighe As New List(Of String)()
        For i As Integer = 0 To righe.Count - 1
            Dim celle As List(Of XElement) = righe(i).Elements().Where(Function(e) e.Name.LocalName = "mtd").ToList()
            ' Nelle righe con etichetta il primo elemento è il numero dell'equazione.
            If righe(i).Name.LocalName = "mlabeledtr" AndAlso celle.Count > 0 Then celle.RemoveAt(0)
            colonne = Math.Max(colonne, celle.Count)
            Dim riga As New NodoFormula() With {.Etichetta = "riga " & (i + 1)}
            For j As Integer = 0 To celle.Count - 1
                Dim cella As NodoFormula = Converti(celle(j))
                If cella Is Nothing Then cella = New NodoFormula() With {.Parlato = "vuoto", .Semplice = True}
                cella.Etichetta = "colonna " & (j + 1)
                riga.Figli.Add(cella)
            Next
            riga.Parlato = String.Join(", ", riga.Figli.Select(Function(c) c.Parlato))
            nodo.Figli.Add(riga)
            testoRighe.Add("riga " & (i + 1) & ": " & riga.Parlato)
        Next
        If righe.Count = 0 Then Return Nothing
        Dim intestazione As String = If(colonne <= 1, "gruppo di " & righe.Count & If(righe.Count = 1, " riga", " righe"), "matrice di " & righe.Count & If(righe.Count = 1, " riga", " righe") & " e " & colonne & " colonne")
        nodo.Parlato = intestazione & ": " & String.Join("; ", testoRighe) & "; fine " & If(colonne <= 1, "gruppo", "matrice")
        Return nodo
    End Function

End Class
