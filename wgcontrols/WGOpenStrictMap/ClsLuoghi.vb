Imports System

Imports System.Collections.Generic
Imports System.Globalization
Imports System.IO
Imports System.Linq
Imports System.Net.Http
Imports System.Text
Imports System.Threading
Imports System.Threading.Tasks
Imports System.Windows.Forms
Imports Newtonsoft.Json.Linq

''' <summary>
''' Ricerca le attivita' presenti in un comune italiano per categoria, usando i dati
''' aperti di OpenStreetMap tramite il servizio Overpass. Restituisce nome, indirizzo,
''' telefono, orari e sito, senza indicazioni stradali.
''' Classe indipendente: non richiede altre classi del programma tranne il registro errori.
''' </summary>
Public Class CLSLuoghi

#Region "Tipi pubblici"

    Public Delegate Sub Avanzamento(testo As String)

    Public Class Attivita
        Public Property Nome As String
        Public Property Categoria As String
        Public Property Indirizzo As String
        Public Property Comune As String
        Public Property Telefono As String
        Public Property Sito As String
        Public Property Email As String
        Public Property Orari As String
        Public Property Latitudine As Double
        Public Property Longitudine As Double
        Public Property DistanzaKm As Double
        Public Property StatoApertura As Integer
        Public Property Fonte As String
        Public Property IdGers As String
        Public Property IdOsm As String
        Public Property Marca As String
        Public Property PercorsoTattile As String
        Public Property SegnalatoreAcustico As String
        Public Property Braille As String
        Public Property Corrimano As String
        Public Property DescrizionePerNonVedenti As String
        Public Property AccessoSediaRotelle As String
        Public Property BagnoAccessibile As String
        Public Property CaniGuidaAmmessi As String
        Public Property OrarioCucina As String
        Public Property OrarioNonRilevato As Boolean
        Public Property RigheEnciclopediche As List(Of String)
    End Class

    Private Class Categoria
        Public Property Gruppo As String
        Public Property Nome As String
        Public Property Filtri As String()
        Public Property CategorieOverture As String()
        Public Sub New(gruppo As String, nome As String, filtri As String(), categorieOverture As String())
            Me.Gruppo = gruppo
            Me.Nome = nome
            Me.Filtri = filtri
            Me.CategorieOverture = categorieOverture
        End Sub
    End Class

#End Region

#Region "Singleton e campi"

    Private Shared _istanza As CLSLuoghi
    Private Shared ReadOnly _lockIstanza As New Object()

    Private ReadOnly _percorsoFileLog As String
    Private Shared ReadOnly _lockLog As New Object()
    Private ReadOnly _overture As CLSOverture = CLSOverture.GetInstance()
    Private ReadOnly _wikidata As CLSWikidata = CLSWikidata.GetInstance()

    Private Const URL_NOMINATIM As String = "https://nominatim.openstreetmap.org/search"
    Private Const URL_OVERPASS As String = "https://overpass-api.de/api/interpreter"
    Private Const URL_OVERPASS_ALTERNATIVO As String = "https://overpass.kumi.systems/api/interpreter"

    ' Piu' server di riserva: quando quello principale e' sovraccarico risponde 504,
    ' e avere una sola alternativa non basta nelle ore di punta.
    Private Shared ReadOnly _serverOverpass As String() = {
        "https://overpass-api.de/api/interpreter",
        "https://overpass.kumi.systems/api/interpreter",
        "https://overpass.private.coffee/api/interpreter",
        "https://overpass-api.de/api/interpreter"
    }

    Private Shared ReadOnly _http As New HttpClient() With {
        .Timeout = TimeSpan.FromMinutes(3)
    }

    Private ReadOnly _cacheComuni As New Dictionary(Of String, Double())(StringComparer.OrdinalIgnoreCase)
    Private ReadOnly _cachePoligoni As New Dictionary(Of String, List(Of List(Of Tuple(Of Double, Double))))(StringComparer.OrdinalIgnoreCase)
    Private ReadOnly _lockCache As New Object()

    ' Nominatim consente al massimo una richiesta al secondo e blocca chi non rispetta
    ' il limite. In un programma distribuito a molte persone il rispetto di questa regola
    ' non e' facoltativo: senza, il servizio verrebbe negato a tutti gli utenti.
    Private Shared _ultimaChiamataNominatim As DateTime = DateTime.MinValue
    Private Shared ReadOnly _lockNominatim As New Object()
    Private ReadOnly _cacheRiferimenti As New Dictionary(Of String, List(Of Attivita))(StringComparer.OrdinalIgnoreCase)

    Shared Sub New()
        Try
            _http.DefaultRequestHeaders.UserAgent.ParseAdd("WinGabriel/1.0 (accessibilita)")
            _http.DefaultRequestHeaders.AcceptLanguage.ParseAdd("it-IT,it;q=0.9")
        Catch
        End Try
    End Sub

    Private Sub New()
        Try
            _percorsoFileLog = Path.Combine(wgcontrols.Logger.CartellaLogApplicazione(), "log.txt")
        Catch
        End Try
    End Sub

    Public Shared Function GetInstance() As CLSLuoghi
        Try
            SyncLock _lockIstanza
                If _istanza Is Nothing Then _istanza = New CLSLuoghi()
                Return _istanza
            End SyncLock
        Catch
            Return _istanza
        End Try
    End Function

#End Region

#Region "Categorie"

    Private Const G_SALUTE As String = "Salute"
    Private Const G_CIBO As String = "Cibo e spesa"
    Private Const G_SERVIZI As String = "Servizi e uffici"
    Private Const G_NEGOZI As String = "Negozi"
    Private Const G_TEMPO As String = "Tempo libero"
    Private Const G_TRASPORTI As String = "Trasporti e auto"
    Private Const G_ISTRUZIONE As String = "Istruzione"
    Private Const G_ALLOGGIO As String = "Dormire"

    Private Shared ReadOnly _categorie As Categoria() = {
        New Categoria(G_SALUTE, "Farmacie e parafarmacie", New String() {"amenity=pharmacy", "shop=chemist"}, New String() {"pharmacy", "drugstore"}),
        New Categoria(G_SALUTE, "Medici e ambulatori", New String() {"amenity=doctors", "amenity=clinic", "healthcare=doctor"}, New String() {"doctor", "medical_center", "family_medicine_practitioner", "internal_medicine_physician"}),
        New Categoria(G_SALUTE, "Dentisti", New String() {"amenity=dentist"}, New String() {"dentist", "dental_clinic"}),
        New Categoria(G_SALUTE, "Ospedali e pronto soccorso", New String() {"amenity=hospital"}, New String() {"hospital", "emergency_room"}),
        New Categoria(G_SALUTE, "Veterinari", New String() {"amenity=veterinary"}, New String() {"veterinarian", "animal_hospital"}),
        New Categoria(G_SALUTE, "Ottici", New String() {"shop=optician"}, New String() {"optometrist", "eyewear_and_optician"}),
        New Categoria(G_CIBO, "Supermercati e alimentari", New String() {"shop=supermarket", "shop=convenience", "shop=greengrocer", "shop=butcher", "shop=deli"}, New String() {"supermarket", "grocery_store", "butcher", "deli", "convenience_store", "food_and_beverage_retail"}),
        New Categoria(G_CIBO, "Panetterie e pasticcerie", New String() {"shop=bakery", "shop=pastry", "shop=confectionery"}, New String() {"bakery", "pastry_shop", "dessert_shop"}),
        New Categoria(G_CIBO, "Ristoranti", New String() {"amenity=restaurant"}, New String() {"restaurant", "italian_restaurant", "pizza_restaurant", "eat_and_drink"}),
        New Categoria(G_CIBO, "Pizzerie e cibo da asporto", New String() {"amenity=restaurant+cuisine~pizza", "amenity=fast_food", "shop=pizza", "amenity=fast_food+cuisine~pizza"}, New String() {"fast_food_restaurant", "pizza_restaurant", "food_court"}),
        New Categoria(G_CIBO, "Bar e caffe", New String() {"amenity=cafe", "amenity=bar", "amenity=pub"}, New String() {"cafe", "bar", "pub", "coffee_shop"}),
        New Categoria(G_CIBO, "Gelaterie", New String() {"amenity=ice_cream", "shop=ice_cream", "amenity=cafe+cuisine~ice_cream", "shop=confectionery+cuisine~ice_cream"}, New String() {"ice_cream_shop", "gelato_shop"}),
        New Categoria(G_SERVIZI, "Banche e sportelli bancomat", New String() {"amenity=bank", "amenity=atm"}, New String() {"bank", "atm", "financial_service"}),
        New Categoria(G_SERVIZI, "Uffici postali", New String() {"amenity=post_office"}, New String() {"post_office", "shipping_and_mailing_service"}),
        New Categoria(G_SERVIZI, "Municipio e uffici pubblici", New String() {"amenity=townhall", "office=government"}, New String() {"city_hall", "government_office", "public_and_government_office"}),
        New Categoria(G_SERVIZI, "Forze dell'ordine e vigili del fuoco", New String() {"amenity=police", "amenity=fire_station"}, New String() {"police_station", "fire_station"}),
        New Categoria(G_TRASPORTI, "Distributori di carburante", New String() {"amenity=fuel"}, New String() {"gas_station", "fuel_station"}),
        New Categoria(G_TRASPORTI, "Parcheggi", New String() {"amenity=parking"}, New String() {"parking", "parking_lot"}),
        New Categoria(G_TRASPORTI, "Officine e gommisti", New String() {"shop=car_repair", "shop=tyres"}, New String() {"automotive_repair_shop", "tire_shop", "auto_repair"}),
        New Categoria(G_TRASPORTI, "Fermate e stazioni", New String() {"public_transport=station", "amenity=bus_station", "railway=station"}, New String() {"train_station", "bus_station", "public_transportation"}),
        New Categoria(G_NEGOZI, "Parrucchieri ed estetisti", New String() {"shop=hairdresser", "shop=beauty"}, New String() {"hair_salon", "beauty_salon", "barber"}),
        New Categoria(G_NEGOZI, "Abbigliamento e calzature", New String() {"shop=clothes", "shop=shoes"}, New String() {"clothing_store", "shoe_store", "fashion"}),
        New Categoria(G_NEGOZI, "Librerie ed edicole", New String() {"shop=books", "shop=newsagent"}, New String() {"book_store", "newsstand"}),
        New Categoria(G_NEGOZI, "Ferramenta e fai da te", New String() {"shop=hardware", "shop=doityourself"}, New String() {"hardware_store", "home_improvement_store"}),
        New Categoria(G_NEGOZI, "Elettronica ed elettrodomestici", New String() {"shop=electronics", "shop=computer", "shop=mobile_phone"}, New String() {"electronics_store", "computer_store", "mobile_phone_store"}),
        New Categoria(G_NEGOZI, "Tabaccherie", New String() {"shop=tobacco", "shop=newsagent+tobacco=yes", "shop=kiosk+tobacco=yes"}, New String() {"tobacco_shop", "cigar_shop"}),
        New Categoria(G_TEMPO, "Biblioteche", New String() {"amenity=library"}, New String() {"library"}),
        New Categoria(G_TEMPO, "Cinema e teatri", New String() {"amenity=cinema", "amenity=theatre"}, New String() {"cinema", "movie_theater", "performing_arts_theater"}),
        New Categoria(G_TEMPO, "Musei e luoghi di interesse", New String() {"tourism=museum", "tourism=attraction"}, New String() {"museum", "tourist_attraction", "landmark_and_historical_building"}),
        New Categoria(G_TEMPO, "Palestre e impianti sportivi", New String() {"leisure=fitness_centre", "leisure=sports_centre"}, New String() {"gym", "fitness_center", "sports_club_and_league"}),
        New Categoria(G_TEMPO, "Parchi e giardini", New String() {"leisure=park", "leisure=garden"}, New String() {"park", "garden"}),
        New Categoria(G_TEMPO, "Chiese e luoghi di culto", New String() {"amenity=place_of_worship"}, New String() {"church_cathedral", "religious_organization"}),
        New Categoria(G_ISTRUZIONE, "Scuole e asili", New String() {"amenity=school", "amenity=kindergarten"}, New String() {"school", "kindergarten", "elementary_school", "high_school"}),
        New Categoria(G_ALLOGGIO, "Alberghi e bed and breakfast", New String() {"tourism=hotel", "tourism=guest_house", "tourism=bed_and_breakfast"}, New String() {"hotel", "bed_and_breakfast", "guest_house", "accommodation"}),
        New Categoria(G_ALLOGGIO, "Agriturismi e case vacanza", New String() {"tourism=apartment", "tourism=chalet"}, New String() {"agriturismo", "vacation_rental", "cottage"}),
        New Categoria(G_ALLOGGIO, "Campeggi e ostelli", New String() {"tourism=camp_site", "tourism=hostel", "tourism=caravan_site"}, New String() {"campground", "hostel", "rv_park"}),
        New Categoria(G_SALUTE, "Laboratori di analisi e centri prelievi", New String() {"healthcare=laboratory", "healthcare=sample_collection"}, New String() {"medical_lab", "diagnostic_services"}),
        New Categoria(G_SALUTE, "Fisioterapisti e riabilitazione", New String() {"healthcare=physiotherapist"}, New String() {"physical_therapy", "physiotherapist", "rehabilitation_center"}),
        New Categoria(G_SALUTE, "Psicologi e psicoterapeuti", New String() {"healthcare=psychotherapist"}, New String() {"psychologist", "mental_health_service", "counseling"}),
        New Categoria(G_SALUTE, "Case di riposo e assistenza anziani", New String() {"amenity=nursing_home", "amenity=social_facility"}, New String() {"nursing_home", "assisted_living_facility", "retirement_home"}),
        New Categoria(G_SALUTE, "Erboristerie e prodotti naturali", New String() {"shop=herbalist", "shop=nutrition_supplements"}, New String() {"herbal_shop", "vitamin_supplement_store", "health_food_store"}),
        New Categoria(G_CIBO, "Pescherie", New String() {"shop=seafood", "shop=fishmonger"}, New String() {"seafood_market", "fish_market"}),
        New Categoria(G_CIBO, "Enoteche e birrerie", New String() {"shop=wine", "shop=alcohol", "shop=beverages"}, New String() {"wine_store", "liquor_store", "beer_store", "brewery"}),
        New Categoria(G_CIBO, "Mercati e centri commerciali", New String() {"amenity=marketplace", "shop=mall", "shop=department_store"}, New String() {"shopping_mall", "market", "farmers_market", "department_store"}),
        New Categoria(G_SERVIZI, "Commercialisti e centri assistenza fiscale", New String() {"office=accountant", "office=tax_advisor"}, New String() {"accountant", "tax_service", "bookkeeping_service"}),
        New Categoria(G_SERVIZI, "Avvocati e notai", New String() {"office=lawyer", "office=notary"}, New String() {"lawyer", "notary_public", "legal_service"}),
        New Categoria(G_SERVIZI, "Assicurazioni", New String() {"office=insurance"}, New String() {"insurance_agency", "insurance_broker"}),
        New Categoria(G_SERVIZI, "Agenzie immobiliari", New String() {"office=estate_agent"}, New String() {"real_estate_agent", "real_estate_service"}),
        New Categoria(G_SERVIZI, "Agenzie di viaggio", New String() {"shop=travel_agency", "office=travel_agent"}, New String() {"travel_agency", "tour_operator"}),
        New Categoria(G_SERVIZI, "Lavanderie", New String() {"shop=laundry", "shop=dry_cleaning"}, New String() {"laundry_service", "dry_cleaner", "laundromat"}),
        New Categoria(G_SERVIZI, "Onoranze funebri e cimiteri", New String() {"shop=funeral_directors", "amenity=grave_yard", "landuse=cemetery"}, New String() {"funeral_home", "cemetery", "funeral_service"}),
        New Categoria(G_SERVIZI, "Centri per l'impiego e patronati", New String() {"office=employment_agency", "office=association"}, New String() {"employment_agency", "social_service_organization"}),
        New Categoria(G_NEGOZI, "Fiorai e vivai", New String() {"shop=florist", "shop=garden_centre"}, New String() {"florist", "garden_center", "nursery"}),
        New Categoria(G_NEGOZI, "Gioiellerie e orologerie", New String() {"shop=jewelry", "shop=watches"}, New String() {"jewelry_store", "watch_store"}),
        New Categoria(G_NEGOZI, "Cartolerie e copisterie", New String() {"shop=stationery", "shop=copyshop", "shop=printing"}, New String() {"office_supply_store", "print_shop", "copy_shop"}),
        New Categoria(G_NEGOZI, "Casalinghi e articoli per la casa", New String() {"shop=houseware", "shop=kitchen"}, New String() {"housewares_store", "kitchen_supply_store"}),
        New Categoria(G_NEGOZI, "Mobili e arredamento", New String() {"shop=furniture", "shop=interior_decoration"}, New String() {"furniture_store", "home_decor_store"}),
        New Categoria(G_NEGOZI, "Giocattoli e articoli per bambini", New String() {"shop=toys", "shop=baby_goods"}, New String() {"toy_store", "baby_store", "children_clothing_store"}),
        New Categoria(G_NEGOZI, "Articoli sportivi", New String() {"shop=sports", "shop=bicycle", "shop=outdoor"}, New String() {"sporting_goods_store", "bicycle_store", "outdoor_store"}),
        New Categoria(G_NEGOZI, "Negozi per animali", New String() {"shop=pet", "shop=pet_grooming"}, New String() {"pet_store", "pet_groomer", "pet_supply_store"}),
        New Categoria(G_NEGOZI, "Telefonia e servizi internet", New String() {"shop=mobile_phone", "shop=telecommunication"}, New String() {"mobile_phone_store", "telecommunication_service", "internet_service_provider"}),
        New Categoria(G_TRASPORTI, "Concessionarie auto", New String() {"shop=car"}, New String() {"car_dealer", "automotive_dealer"}),
        New Categoria(G_TRASPORTI, "Autoscuole", New String() {"office=driving_school", "amenity=driving_school"}, New String() {"driving_school"}),
        New Categoria(G_TRASPORTI, "Autonoleggi e taxi", New String() {"amenity=car_rental", "amenity=taxi"}, New String() {"car_rental", "taxi_service"}),
        New Categoria(G_TRASPORTI, "Ricarica auto elettriche", New String() {"amenity=charging_station"}, New String() {"ev_charging_station"}),
        New Categoria(G_TEMPO, "Piscine e centri benessere", New String() {"leisure=swimming_pool", "leisure=spa", "shop=massage"}, New String() {"swimming_pool", "spa", "massage_therapist", "wellness_center"}),
        New Categoria(G_TEMPO, "Circoli e associazioni", New String() {"amenity=community_centre", "amenity=social_centre"}, New String() {"community_center", "social_club", "association"}),
        New Categoria(G_TEMPO, "Parchi gioco per bambini", New String() {"leisure=playground"}, New String() {"playground"}),
        New Categoria(G_ISTRUZIONE, "Universita e istituti superiori", New String() {"amenity=university", "amenity=college"}, New String() {"university", "college"}),
        New Categoria(G_ISTRUZIONE, "Corsi e formazione", New String() {"amenity=training", "office=educational_institution", "amenity=language_school", "amenity=music_school"}, New String() {"training_center", "language_school", "music_school", "tutoring_service"})
    }

    ''' <summary>
    ''' Elenco dei nomi di categoria che si possono passare a CercaAttivita.
    ''' </summary>
    Public Function CategorieDisponibili() As List(Of String)
        Dim elenco As New List(Of String)
        Try
            For Each c In _categorie
                elenco.Add(c.Nome)
            Next
        Catch ex As Exception
            ScriviLog("CLSLuoghi.CategorieDisponibili: " & ex.ToString())
        End Try
        Return elenco
    End Function

    ''' <summary>
    ''' Elenco dei gruppi in cui sono raccolte le categorie, per non presentare
    ''' all'utente un unico elenco lunghissimo.
    ''' </summary>
    Public Function GruppiDisponibili() As List(Of String)
        Dim elenco As New List(Of String)
        Try
            For Each c In _categorie
                If Not elenco.Contains(c.Gruppo) Then elenco.Add(c.Gruppo)
            Next
        Catch ex As Exception
            ScriviLog("CLSLuoghi.GruppiDisponibili: " & ex.ToString())
        End Try
        Return elenco
    End Function

    ''' <summary>
    ''' Categorie appartenenti a un gruppo.
    ''' </summary>
    Public Function CategorieDelGruppo(gruppo As String) As List(Of String)
        Dim elenco As New List(Of String)
        Try
            If String.IsNullOrWhiteSpace(gruppo) Then Return elenco
            For Each c In _categorie
                If String.Equals(c.Gruppo, gruppo, StringComparison.OrdinalIgnoreCase) Then elenco.Add(c.Nome)
            Next
        Catch ex As Exception
            ScriviLog("CLSLuoghi.CategorieDelGruppo: " & ex.ToString())
        End Try
        Return elenco
    End Function

    Private Function TrovaCategoria(nome As String) As Categoria
        Try
            If String.IsNullOrWhiteSpace(nome) Then Return Nothing
            Dim cercato As String = nome.Trim()
            For Each c In _categorie
                If String.Equals(c.Nome, cercato, StringComparison.OrdinalIgnoreCase) Then Return c
            Next
            For Each c In _categorie
                If c.Nome.IndexOf(cercato, StringComparison.OrdinalIgnoreCase) >= 0 Then Return c
            Next
            Return Nothing
        Catch ex As Exception
            ScriviLog("CLSLuoghi.TrovaCategoria: " & ex.ToString())
            Return Nothing
        End Try
    End Function

#End Region

#Region "Ricerca"

    ''' <summary>
    ''' Cerca le attivita' di una categoria dentro i confini di un comune.
    ''' Le attivita' che ricadono in comuni diversi vengono escluse.
    ''' </summary>
    Public Function CercaAttivita(comune As String, categoria As String, Optional avanzamento As Avanzamento = Nothing) As List(Of Attivita)
        Dim risultato As New List(Of Attivita)
        Try
            If String.IsNullOrWhiteSpace(comune) OrElse String.IsNullOrWhiteSpace(categoria) Then Return risultato

            Dim cat As Categoria = TrovaCategoria(categoria)
            If cat Is Nothing Then Return risultato

            Segnala(avanzamento, "Individuo il comune di " & comune)

            Dim latitudine As Double = 0
            Dim longitudine As Double = 0
            Dim comuneRisolto As String = ""
            Dim bbox As Double() = Nothing
            Dim poligono As List(Of List(Of Tuple(Of Double, Double))) = Nothing

            If Not RisolviComune(comune, latitudine, longitudine, comuneRisolto, bbox, poligono) Then Return risultato

            Segnala(avanzamento, "Cerco " & cat.Nome.ToLowerInvariant() & " a " & comune)

            ' Overture viene interrogato in parallelo a OpenStreetMap: sono due fonti
            ' indipendenti e aspettare la prima per iniziare la seconda raddoppiava i tempi.
            Dim compitoOverture As Task(Of List(Of CLSOverture.LuogoOverture)) = Nothing
            Try
                compitoOverture = AvviaRicercaOverture(cat, bbox, latitudine, longitudine, avanzamento)
            Catch exAvvio As Exception
                ScriviLog("CLSLuoghi.CercaAttivita avvio overture: " & exAvvio.Message)
            End Try

            Dim json As String = InterrogaOverpass(cat, bbox, latitudine, longitudine)

            Dim comuneNormalizzato As String = Normalizza(If(comuneRisolto.Length > 0, comuneRisolto, comune))
            Dim adesso As DateTime = DateTime.Now

            If Not String.IsNullOrWhiteSpace(json) Then
              Dim radice As JObject = JObject.Parse(json)
              Dim elementi = radice("elements")
              If elementi IsNot Nothing AndAlso elementi.Type = JTokenType.Array Then
                For Each elemento As JToken In elementi.Children()
                Try
                    Dim a As Attivita = LeggiElemento(elemento, cat.Nome)
                    If a Is Nothing Then Continue For
                    If String.IsNullOrWhiteSpace(a.Nome) Then Continue For

                    ' Esclude le attivita' di altri comuni: se il dato dichiara un comune diverso
                    ' lo si scarta subito, altrimenti si verifica che il punto cada nei confini.
                    If Not String.IsNullOrWhiteSpace(a.Comune) Then
                        If Normalizza(a.Comune) <> comuneNormalizzato Then Continue For
                    ElseIf poligono IsNot Nothing AndAlso poligono.Count > 0 Then
                        If a.Latitudine = 0 OrElse a.Longitudine = 0 Then Continue For
                        If Not PuntoInPoligono(a.Latitudine, a.Longitudine, poligono) Then Continue For
                    End If

                    a.StatoApertura = ValutaApertura(a.Orari, adesso)

                    If a.Latitudine <> 0 AndAlso a.Longitudine <> 0 Then
                        a.DistanzaKm = DistanzaChilometri(latitudine, longitudine, a.Latitudine, a.Longitudine)
                    End If

                    If String.IsNullOrWhiteSpace(a.Comune) Then a.Comune = If(comuneRisolto.Length > 0, comuneRisolto, comune)

                    risultato.Add(a)
                Catch exElemento As Exception
                    ScriviLog("CLSLuoghi.CercaAttivita elemento: " & exElemento.Message)
                End Try
                Next
              End If
            End If

            Try
                Segnala(avanzamento, "Confronto con l'archivio Overture Maps")
                Dim luoghiOverture As List(Of CLSOverture.LuogoOverture) = Nothing
                If compitoOverture IsNot Nothing Then luoghiOverture = compitoOverture.GetAwaiter().GetResult()
                UnisciConOverture(risultato, luoghiOverture, cat, bbox, latitudine, longitudine, comuneNormalizzato, comuneRisolto, comune, poligono, adesso, avanzamento)
            Catch exOverture As Exception
                ScriviLog("CLSLuoghi.CercaAttivita overture: " & exOverture.ToString())
            End Try

            risultato = ScartaSenzaIndirizzo(risultato)
            OrdinaPerDistanza(risultato)
            Segnala(avanzamento, "Ricerca completata")
        Catch ex As Exception
            ScriviLog("CLSLuoghi.CercaAttivita: " & ex.ToString())
        End Try
        Return risultato
    End Function

    ''' <summary>
    ''' Come CercaAttivita, ma restituisce solo quelle che risultano aperte in questo momento
    ''' secondo l'orario dichiarato. Le attivita' senza orario indicato vengono escluse,
    ''' perche' non e' possibile sapere se sono aperte.
    ''' </summary>
    Public Function CercaAttivitaAperte(comune As String, categoria As String, Optional avanzamento As Avanzamento = Nothing) As List(Of Attivita)
        Dim aperte As New List(Of Attivita)
        Try
            Dim tutte = CercaAttivita(comune, categoria, avanzamento)
            For Each a In tutte
                If a.StatoApertura = 1 Then aperte.Add(a)
            Next
        Catch ex As Exception
            ScriviLog("CLSLuoghi.CercaAttivitaAperte: " & ex.ToString())
        End Try
        Return aperte
    End Function

    ''' <summary>
    ''' Cerca in un comune tutte le attivita' il cui nome contiene il testo indicato,
    ''' indipendentemente dalla categoria. Utile quando si conosce il nome del negozio
    ''' ma non si sa in quale categoria cercarlo.
    ''' </summary>
    Public Function CercaPerNome(comune As String, testo As String, Optional avanzamento As Avanzamento = Nothing) As List(Of Attivita)
        Dim risultato As New List(Of Attivita)
        Try
            If String.IsNullOrWhiteSpace(comune) OrElse String.IsNullOrWhiteSpace(testo) Then Return risultato

            Segnala(avanzamento, "Individuo il comune di " & comune)

            Dim latitudine As Double = 0
            Dim longitudine As Double = 0
            Dim comuneRisolto As String = ""
            Dim bbox As Double() = Nothing
            Dim poligono As List(Of List(Of Tuple(Of Double, Double))) = Nothing

            If Not RisolviComune(comune, latitudine, longitudine, comuneRisolto, bbox, poligono) Then Return risultato

            Segnala(avanzamento, "Cerco " & testo & " a " & comune)

            Dim json As String = InterrogaOverpassPerNome(testo, bbox, latitudine, longitudine)
            If String.IsNullOrWhiteSpace(json) Then Return risultato

            Dim comuneNormalizzato As String = Normalizza(If(comuneRisolto.Length > 0, comuneRisolto, comune))

            Dim radice As JObject = JObject.Parse(json)
            Dim elementi = radice("elements")
            If elementi Is Nothing OrElse elementi.Type <> JTokenType.Array Then Return risultato

            Dim adesso As DateTime = DateTime.Now
            Dim giaVisti As New HashSet(Of String)(StringComparer.OrdinalIgnoreCase)

            For Each elemento As JToken In elementi.Children()
                Try
                    Dim a As Attivita = LeggiElemento(elemento, DescriviCategoriaDaTag(elemento("tags")))
                    If a Is Nothing OrElse String.IsNullOrWhiteSpace(a.Nome) Then Continue For

                    Dim chiaveDoppione As String = Normalizza(a.Nome) & "|" & Normalizza(a.Indirizzo)
                    If giaVisti.Contains(chiaveDoppione) Then Continue For

                    If Not String.IsNullOrWhiteSpace(a.Comune) Then
                        If Normalizza(a.Comune) <> comuneNormalizzato Then Continue For
                    ElseIf poligono IsNot Nothing AndAlso poligono.Count > 0 Then
                        If a.Latitudine = 0 OrElse a.Longitudine = 0 Then Continue For
                        If Not PuntoInPoligono(a.Latitudine, a.Longitudine, poligono) Then Continue For
                    End If

                    giaVisti.Add(chiaveDoppione)
                    a.StatoApertura = ValutaApertura(a.Orari, adesso)
                    If a.Latitudine <> 0 AndAlso a.Longitudine <> 0 Then
                        a.DistanzaKm = DistanzaChilometri(latitudine, longitudine, a.Latitudine, a.Longitudine)
                    End If
                    If String.IsNullOrWhiteSpace(a.Comune) Then a.Comune = If(comuneRisolto.Length > 0, comuneRisolto, comune)
                    risultato.Add(a)
                Catch exElemento As Exception
                    ScriviLog("CLSLuoghi.CercaPerNome elemento: " & exElemento.Message)
                End Try
            Next

            risultato = ScartaSenzaIndirizzo(risultato)
            OrdinaPerDistanza(risultato)
            Segnala(avanzamento, "Ricerca completata")
        Catch ex As Exception
            ScriviLog("CLSLuoghi.CercaPerNome: " & ex.ToString())
        End Try
        Return risultato
    End Function

    ''' <summary>
    ''' Cerca le attivita' di una categoria entro un raggio da un indirizzo preciso,
    ''' invece che nell'intero comune.
    ''' </summary>
    Public Function CercaVicinoAIndirizzo(indirizzo As String, categoria As String, Optional raggioMetri As Integer = 1500, Optional avanzamento As Avanzamento = Nothing) As List(Of Attivita)
        Dim risultato As New List(Of Attivita)
        Try
            If String.IsNullOrWhiteSpace(indirizzo) OrElse String.IsNullOrWhiteSpace(categoria) Then Return risultato

            Dim cat As Categoria = TrovaCategoria(categoria)
            If cat Is Nothing Then Return risultato

            Segnala(avanzamento, "Individuo l'indirizzo indicato")

            Dim latitudine As Double = 0
            Dim longitudine As Double = 0
            Dim comuneRisolto As String = ""
            Dim bbox As Double() = Nothing
            Dim poligono As List(Of List(Of Tuple(Of Double, Double))) = Nothing

            If Not RisolviComune(indirizzo, latitudine, longitudine, comuneRisolto, bbox, poligono) Then Return risultato

            Segnala(avanzamento, "Cerco " & cat.Nome.ToLowerInvariant() & " nelle vicinanze")

            Dim json As String = InterrogaOverpass(cat, Nothing, latitudine, longitudine, raggioMetri)
            If String.IsNullOrWhiteSpace(json) Then Return risultato

            Dim radice As JObject = JObject.Parse(json)
            Dim elementi = radice("elements")
            If elementi Is Nothing OrElse elementi.Type <> JTokenType.Array Then Return risultato

            Dim adesso As DateTime = DateTime.Now

            For Each elemento As JToken In elementi.Children()
                Try
                    Dim a As Attivita = LeggiElemento(elemento, cat.Nome)
                    If a Is Nothing OrElse String.IsNullOrWhiteSpace(a.Nome) Then Continue For

                    a.StatoApertura = ValutaApertura(a.Orari, adesso)
                    If a.Latitudine <> 0 AndAlso a.Longitudine <> 0 Then
                        a.DistanzaKm = DistanzaChilometri(latitudine, longitudine, a.Latitudine, a.Longitudine)
                    End If
                    If String.IsNullOrWhiteSpace(a.Comune) AndAlso comuneRisolto.Length > 0 Then a.Comune = comuneRisolto
                    risultato.Add(a)
                Catch exElemento As Exception
                    ScriviLog("CLSLuoghi.CercaVicinoAIndirizzo elemento: " & exElemento.Message)
                End Try
            Next

            risultato = ScartaSenzaIndirizzo(risultato)
            OrdinaPerDistanza(risultato)
            Segnala(avanzamento, "Ricerca completata")
        Catch ex As Exception
            ScriviLog("CLSLuoghi.CercaVicinoAIndirizzo: " & ex.ToString())
        End Try
        Return risultato
    End Function

    ''' <summary>
    ''' Arricchisce i risultati di OpenStreetMap con quelli di Overture Maps.
    ''' Dove i due archivi descrivono lo stesso posto, si completano i campi mancanti
    ''' tenendo gli orari di OSM, che Overture non pubblica. I luoghi presenti solo in
    ''' Overture vengono aggiunti.
    ''' L'abbinamento usa prima l'identificativo OSM che Overture porta con se',
    ''' poi il nome unito alla vicinanza.
    ''' </summary>
    Private Sub CalcolaRiquadro(bbox As Double(), latitudine As Double, longitudine As Double,
                                ByRef latMin As Double, ByRef latMax As Double,
                                ByRef lonMin As Double, ByRef lonMax As Double)
        Try
            If bbox IsNot Nothing AndAlso bbox.Length = 4 Then
                latMin = bbox(0)
                latMax = bbox(1)
                lonMin = bbox(2)
                lonMax = bbox(3)
            Else
                Dim margine As Double = 0.08
                latMin = latitudine - margine
                latMax = latitudine + margine
                lonMin = longitudine - margine
                lonMax = longitudine + margine
            End If
        Catch ex As Exception
            ScriviLog("CLSLuoghi.CalcolaRiquadro: " & ex.Message)
        End Try
    End Sub

    Private Function AvviaRicercaOverture(cat As Categoria, bbox As Double(), latitudine As Double, longitudine As Double,
                                          avanzamento As Avanzamento) As Task(Of List(Of CLSOverture.LuogoOverture))
        Try
            If cat Is Nothing OrElse cat.CategorieOverture Is Nothing OrElse cat.CategorieOverture.Length = 0 Then Return Nothing

            _avanzamentoCorrente = avanzamento

            Dim latMin As Double = 0, latMax As Double = 0, lonMin As Double = 0, lonMax As Double = 0
            CalcolaRiquadro(bbox, latitudine, longitudine, latMin, latMax, lonMin, lonMax)

            Dim categorieRichieste As New List(Of String)(cat.CategorieOverture)

            Return Task.Run(Function() _overture.CercaLuoghi(latMin, latMax, lonMin, lonMax, categorieRichieste, 0.4, 800, New CLSOverture.Avanzamento(AddressOf Inoltra)))
        Catch ex As Exception
            ScriviLog("CLSLuoghi.AvviaRicercaOverture: " & ex.ToString())
            Return Nothing
        End Try
    End Function

    Private Sub UnisciConOverture(risultato As List(Of Attivita), luoghi As List(Of CLSOverture.LuogoOverture),
                                  cat As Categoria, bbox As Double(),
                                  latitudine As Double, longitudine As Double,
                                  comuneNormalizzato As String, comuneRisolto As String, comune As String,
                                  poligono As List(Of List(Of Tuple(Of Double, Double))), adesso As DateTime,
                                  avanzamento As Avanzamento)
        Try
            _avanzamentoCorrente = avanzamento

            If luoghi Is Nothing OrElse luoghi.Count = 0 Then
                RecuperaOrariMancanti(risultato, bbox, latitudine, longitudine, adesso, avanzamento)
                ArricchisciConWikidata(risultato, latitudine, longitudine, avanzamento)
                Return
            End If

            For Each l In luoghi
                Try
                    If String.IsNullOrWhiteSpace(l.Nome) Then Continue For

                    ' Esclude i luoghi di altri comuni con la stessa logica usata per OSM.
                    If Not String.IsNullOrWhiteSpace(l.Comune) Then
                        If Normalizza(l.Comune) <> comuneNormalizzato Then Continue For
                    ElseIf poligono IsNot Nothing AndAlso poligono.Count > 0 Then
                        If l.Latitudine = 0 OrElse l.Longitudine = 0 Then Continue For
                        If Not PuntoInPoligono(l.Latitudine, l.Longitudine, poligono) Then Continue For
                    End If

                    Dim esistente As Attivita = TrovaCorrispondente(risultato, l)

                    If esistente IsNot Nothing Then
                        CompletaDaOverture(esistente, l)
                    Else
                        ' Non si da' per buono il filtro fatto sul server: si controlla che la
                        ' categoria dichiarata dal luogo sia davvero fra quelle richieste.
                        ' Senza questo controllo un profumeria poteva comparire fra i ristoranti
                        ' solo perche' l'archivio remoto l'aveva restituita.
                        If Not CategoriaOvertureRichiesta(l.Categoria, cat) Then Continue For

                        Dim nuova As New Attivita()
                        nuova.Nome = l.Nome
                        nuova.Categoria = cat.Nome
                        nuova.Indirizzo = l.Indirizzo
                        nuova.Comune = If(String.IsNullOrWhiteSpace(l.Comune), If(comuneRisolto.Length > 0, comuneRisolto, comune), l.Comune)
                        nuova.Telefono = l.Telefono
                        nuova.Sito = l.Sito
                        nuova.Email = l.Email
                        nuova.Latitudine = l.Latitudine
                        nuova.Longitudine = l.Longitudine
                        nuova.IdGers = l.IdGers
                        nuova.Marca = l.Marca
                        nuova.Fonte = "Overture Maps"
                        nuova.StatoApertura = -1

                        If nuova.Latitudine <> 0 AndAlso nuova.Longitudine <> 0 Then
                            nuova.DistanzaKm = DistanzaChilometri(latitudine, longitudine, nuova.Latitudine, nuova.Longitudine)
                        End If

                        risultato.Add(nuova)
                    End If
                Catch exLuogo As Exception
                    ScriviLog("CLSLuoghi.UnisciConOverture luogo: " & exLuogo.Message)
                End Try
            Next

            RecuperaOrariMancanti(risultato, bbox, latitudine, longitudine, adesso, avanzamento)
            ArricchisciConWikidata(risultato, latitudine, longitudine, avanzamento)
        Catch ex As Exception
            ScriviLog("CLSLuoghi.UnisciConOverture: " & ex.ToString())
        End Try
    End Sub

    Private Function TrovaCorrispondente(elenco As List(Of Attivita), l As CLSOverture.LuogoOverture) As Attivita
        Try
            If elenco Is Nothing OrElse l Is Nothing Then Return Nothing

            ' Abbinamento certo: Overture riporta l'identificativo OSM di provenienza.
            If Not String.IsNullOrWhiteSpace(l.IdOsm) Then
                Dim cifreLuogo As String = SoloCifre(l.IdOsm)
                If cifreLuogo.Length > 0 Then
                    For Each a In elenco
                        If Not String.IsNullOrWhiteSpace(a.IdOsm) AndAlso SoloCifre(a.IdOsm) = cifreLuogo Then Return a
                    Next
                End If
            End If

            Dim nomeOverture As String = Normalizza(l.Nome)
            If nomeOverture.Length = 0 Then Return Nothing

            ' Abbinamento per nome piu' vicinanza: due punti a meno di 150 metri con lo
            ' stesso nome, o con un nome contenuto nell'altro, sono lo stesso posto.
            For Each a In elenco
                Try
                    Dim nomeEsistente As String = Normalizza(a.Nome)
                    If nomeEsistente.Length = 0 Then Continue For

                    Dim nomiCompatibili As Boolean = (nomeEsistente = nomeOverture)
                    If Not nomiCompatibili AndAlso nomeEsistente.Length >= 5 AndAlso nomeOverture.Length >= 5 Then
                        nomiCompatibili = nomeEsistente.Contains(nomeOverture) OrElse nomeOverture.Contains(nomeEsistente)
                    End If
                    If Not nomiCompatibili Then Continue For

                    If a.Latitudine <> 0 AndAlso a.Longitudine <> 0 AndAlso l.Latitudine <> 0 AndAlso l.Longitudine <> 0 Then
                        If DistanzaChilometri(a.Latitudine, a.Longitudine, l.Latitudine, l.Longitudine) <= 0.15 Then Return a
                    Else
                        Return a
                    End If
                Catch
                End Try
            Next

            Return Nothing
        Catch ex As Exception
            ScriviLog("CLSLuoghi.TrovaCorrispondente: " & ex.Message)
            Return Nothing
        End Try
    End Function

    ''' <summary>
    ''' Completa una attivita' con i dati Overture mancanti, senza sovrascrivere
    ''' quelli di OpenStreetMap che sono aggiornati dalla comunita' locale.
    ''' </summary>
    Private Sub CompletaDaOverture(a As Attivita, l As CLSOverture.LuogoOverture)
        Try
            If a Is Nothing OrElse l Is Nothing Then Return

            If String.IsNullOrWhiteSpace(a.Telefono) Then a.Telefono = l.Telefono
            If String.IsNullOrWhiteSpace(a.Sito) Then a.Sito = l.Sito
            If String.IsNullOrWhiteSpace(a.Email) Then a.Email = l.Email
            If String.IsNullOrWhiteSpace(a.Indirizzo) Then a.Indirizzo = l.Indirizzo
            If String.IsNullOrWhiteSpace(a.Comune) Then a.Comune = l.Comune
            If String.IsNullOrWhiteSpace(a.Marca) Then a.Marca = l.Marca
            If String.IsNullOrWhiteSpace(a.IdGers) Then a.IdGers = l.IdGers

            If a.Latitudine = 0 OrElse a.Longitudine = 0 Then
                a.Latitudine = l.Latitudine
                a.Longitudine = l.Longitudine
            End If

            a.Fonte = "OpenStreetMap e Overture Maps"
        Catch ex As Exception
            ScriviLog("CLSLuoghi.CompletaDaOverture: " & ex.Message)
        End Try
    End Sub

    Private Function SoloCifre(testo As String) As String
        Try
            If String.IsNullOrWhiteSpace(testo) Then Return ""
            Dim sb As New StringBuilder()
            For Each c In testo
                If Char.IsDigit(c) Then sb.Append(c)
            Next
            Return sb.ToString()
        Catch
            Return ""
        End Try
    End Function

    Private _avanzamentoCorrente As Avanzamento

    Private Sub Inoltra(testo As String)
        Try
            If _avanzamentoCorrente IsNot Nothing Then _avanzamentoCorrente(testo)
        Catch
        End Try
    End Sub

    ''' <summary>
    ''' Overture non pubblica gli orari di apertura, quindi i luoghi che arrivano solo da la'
    ''' restano senza orario. Con una sola interrogazione aggiuntiva si chiedono a
    ''' OpenStreetMap tutti i punti di interesse con orario dichiarato nella zona,
    ''' indipendentemente dalla categoria, e si abbinano per nome e vicinanza.
    ''' Una sola chiamata di rete, non una per ogni luogo.
    ''' </summary>
    Private Sub RecuperaOrariMancanti(elenco As List(Of Attivita), bbox As Double(),
                                     latitudine As Double, longitudine As Double,
                                     adesso As DateTime, avanzamento As Avanzamento)
        Try
            If elenco Is Nothing OrElse elenco.Count = 0 Then Return

            Dim senzaOrario As New List(Of Attivita)
            For Each a In elenco
                Dim mancaOrario As Boolean = String.IsNullOrWhiteSpace(a.Orari)
                Dim mancaAccessibilita As Boolean = String.IsNullOrWhiteSpace(a.PercorsoTattile) AndAlso
                                                    String.IsNullOrWhiteSpace(a.AccessoSediaRotelle) AndAlso
                                                    String.IsNullOrWhiteSpace(a.DescrizionePerNonVedenti)
                If mancaOrario OrElse mancaAccessibilita Then senzaOrario.Add(a)
            Next
            If senzaOrario.Count = 0 Then Return

            ' I riferimenti sono gli stessi per tutte le categorie della stessa zona:
            ' scaricarli una volta sola rende immediata la ricerca su piu' categorie.
            Dim chiaveArea As String = ComponiArea(bbox, latitudine, longitudine, 8000)
            Dim riferimenti As List(Of Attivita) = Nothing

            SyncLock _lockCache
                If _cacheRiferimenti.ContainsKey(chiaveArea) Then riferimenti = _cacheRiferimenti(chiaveArea)
            End SyncLock

            If riferimenti Is Nothing Then
                Segnala(avanzamento, "Cerco orari e accessibilita' mancanti su OpenStreetMap")

                riferimenti = New List(Of Attivita)

                Dim json As String = InterrogaOverpassOrari(bbox, latitudine, longitudine)
                If Not String.IsNullOrWhiteSpace(json) Then
                    Try
                        Dim radice As JObject = JObject.Parse(json)
                        Dim elementi = radice("elements")
                        If elementi IsNot Nothing AndAlso elementi.Type = JTokenType.Array Then
                            For Each elemento As JToken In elementi.Children()
                                Try
                                    Dim r As Attivita = LeggiElemento(elemento, "")
                                    If r Is Nothing Then Continue For
                                    If String.IsNullOrWhiteSpace(r.Nome) Then Continue For
                                    riferimenti.Add(r)
                                Catch
                                End Try
                            Next
                        End If
                    Catch exLettura As Exception
                        ScriviLog("CLSLuoghi.RecuperaOrariMancanti lettura: " & exLettura.Message)
                    End Try
                End If

                SyncLock _lockCache
                    _cacheRiferimenti(chiaveArea) = riferimenti
                End SyncLock
            End If

            If riferimenti.Count = 0 Then Return

            Dim completati As Integer = 0
            For Each a In senzaOrario
                Try
                    Dim nomeCercato As String = Normalizza(a.Nome)
                    If nomeCercato.Length = 0 Then Continue For

                    For Each r In riferimenti
                        Try
                            Dim nomeRiferimento As String = Normalizza(r.Nome)
                            If nomeRiferimento.Length = 0 Then Continue For

                            Dim compatibili As Boolean = (nomeRiferimento = nomeCercato)
                            If Not compatibili AndAlso nomeRiferimento.Length >= 5 AndAlso nomeCercato.Length >= 5 Then
                                compatibili = nomeRiferimento.Contains(nomeCercato) OrElse nomeCercato.Contains(nomeRiferimento)
                            End If
                            If Not compatibili Then Continue For

                            If a.Latitudine <> 0 AndAlso a.Longitudine <> 0 AndAlso r.Latitudine <> 0 AndAlso r.Longitudine <> 0 Then
                                If DistanzaChilometri(a.Latitudine, a.Longitudine, r.Latitudine, r.Longitudine) > 0.15 Then Continue For
                            End If

                            If String.IsNullOrWhiteSpace(a.Orari) Then
                                a.Orari = r.Orari
                                a.StatoApertura = ValutaApertura(a.Orari, adesso)
                            End If
                            If String.IsNullOrWhiteSpace(a.OrarioCucina) Then a.OrarioCucina = r.OrarioCucina
                            If String.IsNullOrWhiteSpace(a.PercorsoTattile) Then a.PercorsoTattile = r.PercorsoTattile
                            If String.IsNullOrWhiteSpace(a.SegnalatoreAcustico) Then a.SegnalatoreAcustico = r.SegnalatoreAcustico
                            If String.IsNullOrWhiteSpace(a.Braille) Then a.Braille = r.Braille
                            If String.IsNullOrWhiteSpace(a.Corrimano) Then a.Corrimano = r.Corrimano
                            If String.IsNullOrWhiteSpace(a.DescrizionePerNonVedenti) Then a.DescrizionePerNonVedenti = r.DescrizionePerNonVedenti
                            If String.IsNullOrWhiteSpace(a.AccessoSediaRotelle) Then a.AccessoSediaRotelle = r.AccessoSediaRotelle
                            If String.IsNullOrWhiteSpace(a.BagnoAccessibile) Then a.BagnoAccessibile = r.BagnoAccessibile
                            If String.IsNullOrWhiteSpace(a.CaniGuidaAmmessi) Then a.CaniGuidaAmmessi = r.CaniGuidaAmmessi
                            If String.IsNullOrWhiteSpace(a.Telefono) Then a.Telefono = r.Telefono
                            If String.IsNullOrWhiteSpace(a.Sito) Then a.Sito = r.Sito
                            If String.IsNullOrWhiteSpace(a.IdOsm) Then a.IdOsm = r.IdOsm
                            If Not String.IsNullOrWhiteSpace(a.Fonte) AndAlso a.Fonte.IndexOf("OpenStreetMap", StringComparison.OrdinalIgnoreCase) < 0 Then
                                a.Fonte = "OpenStreetMap e Overture Maps"
                            End If
                            completati += 1
                            Exit For
                        Catch
                        End Try
                    Next
                Catch exSingolo As Exception
                    ScriviLog("CLSLuoghi.RecuperaOrariMancanti elemento: " & exSingolo.Message)
                End Try
            Next

        Catch ex As Exception
            ScriviLog("CLSLuoghi.RecuperaOrariMancanti: " & ex.ToString())
        End Try
    End Sub

    Private Function InterrogaOverpassOrari(bbox As Double(), latitudine As Double, longitudine As Double) As String
        Try
            Dim area As String = ComponiArea(bbox, latitudine, longitudine, 8000)

            ' Una sola richiesta con nwr invece di decine di varianti: Overpass la risolve
            ' molto piu' in fretta e il risultato e' lo stesso.
            ' Si filtra per opening_hours e wheelchair, non per name: sono tag molto piu'
            ' rari, quindi Overpass li risolve con l'indice invece di scorrere tutto.
            ' Il controllo sul nome si fa poi in casa, dove non costa niente.
            Dim query As New StringBuilder()
            query.Append("[out:json][timeout:50];(")
            query.Append("nwr[""opening_hours""]").Append(area).Append(";")
            query.Append("nwr[""wheelchair""]").Append(area).Append(";")
            query.Append(");out center tags;")

            Return InviaOverpass(query.ToString())
        Catch ex As Exception
            ScriviLog("CLSLuoghi.InterrogaOverpassOrari: " & ex.ToString())
            Return ""
        End Try
    End Function

    ''' <summary>
    ''' Cerca tutte le attivita' di un comune senza dividerle per categoria: utile quando
    ''' non si sa in che categoria cercare, o si vuole semplicemente vedere cosa c'e'.
    ''' </summary>
    Public Function CercaTutteLeAttivita(comune As String, Optional soloAperte As Boolean = False,
                                        Optional avanzamento As Avanzamento = Nothing) As List(Of Attivita)
        Dim risultato As New List(Of Attivita)
        Try
            If String.IsNullOrWhiteSpace(comune) Then Return risultato

            Segnala(avanzamento, "Individuo il comune di " & comune)

            Dim latitudine As Double = 0
            Dim longitudine As Double = 0
            Dim comuneRisolto As String = ""
            Dim bbox As Double() = Nothing
            Dim poligono As List(Of List(Of Tuple(Of Double, Double))) = Nothing

            If Not RisolviComune(comune, latitudine, longitudine, comuneRisolto, bbox, poligono) Then Return risultato

            Segnala(avanzamento, "Cerco tutte le attivita' di " & comune)

            Dim json As String = InterrogaOverpassTutto(bbox, latitudine, longitudine)
            If String.IsNullOrWhiteSpace(json) Then Return risultato

            Dim comuneNormalizzato As String = Normalizza(If(comuneRisolto.Length > 0, comuneRisolto, comune))
            Dim adesso As DateTime = DateTime.Now
            Dim giaVisti As New HashSet(Of String)(StringComparer.OrdinalIgnoreCase)

            Dim radice As JObject = JObject.Parse(json)
            Dim elementi = radice("elements")
            If elementi Is Nothing OrElse elementi.Type <> JTokenType.Array Then Return risultato

            For Each elemento As JToken In elementi.Children()
                Try
                    Dim a As Attivita = LeggiElemento(elemento, DescriviCategoriaDaTag(elemento("tags")))
                    If a Is Nothing OrElse String.IsNullOrWhiteSpace(a.Nome) Then Continue For

                    Dim chiave As String = Normalizza(a.Nome) & "|" & Normalizza(a.Indirizzo)
                    If giaVisti.Contains(chiave) Then Continue For

                    If Not String.IsNullOrWhiteSpace(a.Comune) Then
                        If Normalizza(a.Comune) <> comuneNormalizzato Then Continue For
                    ElseIf poligono IsNot Nothing AndAlso poligono.Count > 0 Then
                        If a.Latitudine = 0 OrElse a.Longitudine = 0 Then Continue For
                        If Not PuntoInPoligono(a.Latitudine, a.Longitudine, poligono) Then Continue For
                    End If

                    giaVisti.Add(chiave)
                    a.StatoApertura = ValutaApertura(a.Orari, adesso)
                    If a.Latitudine <> 0 AndAlso a.Longitudine <> 0 Then
                        a.DistanzaKm = DistanzaChilometri(latitudine, longitudine, a.Latitudine, a.Longitudine)
                    End If
                    If String.IsNullOrWhiteSpace(a.Comune) Then a.Comune = If(comuneRisolto.Length > 0, comuneRisolto, comune)
                    risultato.Add(a)
                Catch exElemento As Exception
                    ScriviLog("CLSLuoghi.CercaTutteLeAttivita elemento: " & exElemento.Message)
                End Try
            Next

            Try
                Segnala(avanzamento, "Confronto con l'archivio Overture Maps")
                _avanzamentoCorrente = avanzamento
                UnisciConOvertureSenzaCategoria(risultato, bbox, latitudine, longitudine, comuneNormalizzato, comuneRisolto, comune, poligono, adesso, giaVisti)
            Catch exOverture As Exception
                ScriviLog("CLSLuoghi.CercaTutteLeAttivita overture: " & exOverture.ToString())
            End Try

            risultato = ScartaSenzaIndirizzo(risultato)
            OrdinaPerDistanza(risultato)

            If soloAperte Then
                Dim aperte As New List(Of Attivita)
                For Each a In risultato
                    If a.StatoApertura = 1 Then aperte.Add(a)
                Next
                Segnala(avanzamento, "Ricerca completata")
                Return aperte
            End If

            Segnala(avanzamento, "Ricerca completata")
        Catch ex As Exception
            ScriviLog("CLSLuoghi.CercaTutteLeAttivita: " & ex.ToString())
        End Try
        Return risultato
    End Function

    Private Function InterrogaOverpassTutto(bbox As Double(), latitudine As Double, longitudine As Double) As String
        Try
            Dim area As String = ComponiArea(bbox, latitudine, longitudine, 8000)

            Dim query As New StringBuilder()
            query.Append("[out:json][timeout:50];(")
            For Each chiave In New String() {"amenity", "shop", "tourism", "leisure", "office", "healthcare", "craft"}
                query.Append("nwr[""").Append(chiave).Append("""]").Append(area).Append(";")
            Next
            query.Append(");out center tags;")

            Return InviaOverpass(query.ToString())
        Catch ex As Exception
            ScriviLog("CLSLuoghi.InterrogaOverpassTutto: " & ex.ToString())
            Return ""
        End Try
    End Function

    Private Sub UnisciConOvertureSenzaCategoria(risultato As List(Of Attivita), bbox As Double(),
                                               latitudine As Double, longitudine As Double,
                                               comuneNormalizzato As String, comuneRisolto As String, comune As String,
                                               poligono As List(Of List(Of Tuple(Of Double, Double))), adesso As DateTime,
                                               giaVisti As HashSet(Of String))
        Try
            Dim latMin As Double, latMax As Double, lonMin As Double, lonMax As Double
            If bbox IsNot Nothing AndAlso bbox.Length = 4 Then
                latMin = bbox(0)
                latMax = bbox(1)
                lonMin = bbox(2)
                lonMax = bbox(3)
            Else
                Dim margine As Double = 0.08
                latMin = latitudine - margine
                latMax = latitudine + margine
                lonMin = longitudine - margine
                lonMax = longitudine + margine
            End If

            Dim luoghi = _overture.CercaLuoghi(latMin, latMax, lonMin, lonMax, Nothing, 0.5, 1500, New CLSOverture.Avanzamento(AddressOf Inoltra))
            If luoghi Is Nothing OrElse luoghi.Count = 0 Then Return

            For Each l In luoghi
                Try
                    If String.IsNullOrWhiteSpace(l.Nome) Then Continue For

                    If Not String.IsNullOrWhiteSpace(l.Comune) Then
                        If Normalizza(l.Comune) <> comuneNormalizzato Then Continue For
                    ElseIf poligono IsNot Nothing AndAlso poligono.Count > 0 Then
                        If l.Latitudine = 0 OrElse l.Longitudine = 0 Then Continue For
                        If Not PuntoInPoligono(l.Latitudine, l.Longitudine, poligono) Then Continue For
                    End If

                    Dim esistente As Attivita = TrovaCorrispondente(risultato, l)
                    If esistente IsNot Nothing Then
                        CompletaDaOverture(esistente, l)
                        Continue For
                    End If

                    Dim chiave As String = Normalizza(l.Nome) & "|" & Normalizza(l.Indirizzo)
                    If giaVisti.Contains(chiave) Then Continue For
                    giaVisti.Add(chiave)

                    Dim nuova As New Attivita()
                    nuova.Nome = l.Nome
                    nuova.Categoria = TraduciCategoriaOverture(l.Categoria)
                    nuova.Indirizzo = l.Indirizzo
                    nuova.Comune = If(String.IsNullOrWhiteSpace(l.Comune), If(comuneRisolto.Length > 0, comuneRisolto, comune), l.Comune)
                    nuova.Telefono = l.Telefono
                    nuova.Sito = l.Sito
                    nuova.Email = l.Email
                    nuova.Latitudine = l.Latitudine
                    nuova.Longitudine = l.Longitudine
                    nuova.IdGers = l.IdGers
                    nuova.IdOsm = l.IdOsm
                    nuova.Marca = l.Marca
                    nuova.Fonte = "Overture Maps"
                    nuova.StatoApertura = -1

                    If nuova.Latitudine <> 0 AndAlso nuova.Longitudine <> 0 Then
                        nuova.DistanzaKm = DistanzaChilometri(latitudine, longitudine, nuova.Latitudine, nuova.Longitudine)
                    End If

                    risultato.Add(nuova)
                Catch exLuogo As Exception
                    ScriviLog("CLSLuoghi.UnisciConOvertureSenzaCategoria luogo: " & exLuogo.Message)
                End Try
            Next

            RecuperaOrariMancanti(risultato, bbox, latitudine, longitudine, adesso, _avanzamentoCorrente)
            ArricchisciConWikidata(risultato, latitudine, longitudine, _avanzamentoCorrente)
        Catch ex As Exception
            ScriviLog("CLSLuoghi.UnisciConOvertureSenzaCategoria: " & ex.ToString())
        End Try
    End Sub

    ''' <summary>
    ''' Converte il nome tecnico di categoria usato da Overture nella corrispondente
    ''' categoria in italiano, quando riconosciuta.
    ''' </summary>
    ''' <summary>
    ''' Dice se la categoria dichiarata da Overture per un luogo e' fra quelle richieste
    ''' dalla categoria italiana cercata. Se il luogo non dichiara alcuna categoria non
    ''' viene accettato: meglio perderlo che mostrarlo sotto un'etichetta sbagliata.
    ''' </summary>
    Private Function CategoriaOvertureRichiesta(categoriaLuogo As String, cat As Categoria) As Boolean
        Try
            If cat Is Nothing OrElse cat.CategorieOverture Is Nothing Then Return False
            If String.IsNullOrWhiteSpace(categoriaLuogo) Then Return False

            For Each richiesta In cat.CategorieOverture
                If String.Equals(richiesta, categoriaLuogo, StringComparison.OrdinalIgnoreCase) Then Return True
            Next
            Return False
        Catch ex As Exception
            ScriviLog("CLSLuoghi.CategoriaOvertureRichiesta: " & ex.Message)
            Return False
        End Try
    End Function

    Private Function TraduciCategoriaOverture(categoriaOverture As String) As String
        Try
            If String.IsNullOrWhiteSpace(categoriaOverture) Then Return "Attivita"
            For Each c In _categorie
                If c.CategorieOverture Is Nothing Then Continue For
                For Each co In c.CategorieOverture
                    If String.Equals(co, categoriaOverture, StringComparison.OrdinalIgnoreCase) Then Return c.Nome
                Next
            Next
            Return categoriaOverture.Replace("_", " ")
        Catch
            Return "Attivita"
        End Try
    End Function

    ''' <summary>
    ''' Toglie dall'elenco i luoghi di cui non si conosce l'indirizzo. Va chiamata solo
    ''' alla fine, dopo la fusione con Overture: quell'archivio spesso possiede l'indirizzo
    ''' che manca a OpenStreetMap, quindi scartare prima farebbe perdere luoghi validi.
    ''' Un risultato senza indirizzo e' inutilizzabile per chi deve raggiungerlo, e nel
    ''' mezzo di un elenco letto a voce disorienta piu' che aiutare.
    ''' </summary>
    Private Function ScartaSenzaIndirizzo(elenco As List(Of Attivita)) As List(Of Attivita)
        Dim conservati As New List(Of Attivita)
        Try
            If elenco Is Nothing Then Return conservati

            Dim scartati As Integer = 0
            For Each a In elenco
                Try
                    If a Is Nothing Then Continue For
                    If String.IsNullOrWhiteSpace(a.Indirizzo) Then
                        scartati += 1
                        Continue For
                    End If
                    conservati.Add(a)
                Catch
                End Try
            Next

            If scartati > 0 Then
            End If
        Catch ex As Exception
            ScriviLog("CLSLuoghi.ScartaSenzaIndirizzo: " & ex.ToString())
            Return If(elenco, conservati)
        End Try
        Return conservati
    End Function

    Private Sub OrdinaPerDistanza(elenco As List(Of Attivita))
        Try
            If elenco Is Nothing Then Return
            elenco.Sort(Function(x, y)
                            If x.DistanzaKm > 0 AndAlso y.DistanzaKm > 0 Then Return x.DistanzaKm.CompareTo(y.DistanzaKm)
                            If x.DistanzaKm > 0 Then Return -1
                            If y.DistanzaKm > 0 Then Return 1
                            Return String.Compare(x.Nome, y.Nome, StringComparison.OrdinalIgnoreCase)
                        End Function)
        Catch ex As Exception
            ScriviLog("CLSLuoghi.OrdinaPerDistanza: " & ex.Message)
        End Try
    End Sub

    ''' <summary>
    ''' Riconosce la categoria di un elemento dai suoi tag. Fra piu' corrispondenze
    ''' vince quella con piu' condizioni soddisfatte: un ristorante con cucina pizza
    ''' e' una pizzeria, non un ristorante generico.
    ''' </summary>
    Private Function DescriviCategoriaDaTag(tag As JToken) As String
        Try
            If tag Is Nothing Then Return "Attivita"

            Dim migliore As String = ""
            Dim punteggioMigliore As Integer = 0

            For Each c In _categorie
                For Each filtro In c.Filtri
                    Try
                        Dim punteggio As Integer = PunteggioFiltro(tag, filtro)
                        If punteggio > punteggioMigliore Then
                            punteggioMigliore = punteggio
                            migliore = c.Nome
                        End If
                    Catch
                    End Try
                Next
            Next

            If migliore.Length > 0 Then Return migliore
            Return "Attivita"
        Catch
            Return "Attivita"
        End Try
    End Function

    ''' <summary>
    ''' Restituisce quante condizioni del filtro sono soddisfatte dai tag, oppure zero
    ''' se anche una sola non lo e'. Serve a scegliere la categoria piu' precisa.
    ''' </summary>
    Private Function PunteggioFiltro(tag As JToken, filtro As String) As Integer
        Try
            If tag Is Nothing OrElse String.IsNullOrWhiteSpace(filtro) Then Return 0

            Dim condizioni As Integer = 0

            For Each pezzo In filtro.Split("+"c)
                Dim p As String = pezzo.Trim()
                If p.Length = 0 Then Continue For

                Dim indiceCirca As Integer = p.IndexOf("~"c)
                Dim indiceUguale As Integer = p.IndexOf("="c)

                If indiceCirca > 0 Then
                    Dim chiave As String = p.Substring(0, indiceCirca).Trim()
                    Dim valore As String = p.Substring(indiceCirca + 1).Trim()
                    Dim presente As String = ValoreTag(tag, chiave)
                    If presente.Length = 0 Then Return 0
                    If presente.IndexOf(valore, StringComparison.OrdinalIgnoreCase) < 0 Then Return 0
                    condizioni += 1
                ElseIf indiceUguale > 0 Then
                    Dim chiave As String = p.Substring(0, indiceUguale).Trim()
                    Dim valore As String = p.Substring(indiceUguale + 1).Trim()
                    If Not String.Equals(ValoreTag(tag, chiave), valore, StringComparison.OrdinalIgnoreCase) Then Return 0
                    condizioni += 1
                Else
                    If ValoreTag(tag, p).Length = 0 Then Return 0
                    condizioni += 1
                End If
            Next

            Return condizioni
        Catch
            Return 0
        End Try
    End Function

    ''' <summary>
    ''' Dice se una attivita' risulta aperta adesso: 1 aperta, 0 chiusa, meno uno se
    ''' l'orario non e' indicato o non e' interpretabile.
    ''' </summary>
    Public Function ValutaApertura(orari As String, adesso As DateTime) As Integer
        Try
            If String.IsNullOrWhiteSpace(orari) Then Return -1
            Dim testo As String = orari.Trim()
            If testo = "24/7" Then Return 1

            Dim trovataRegola As Boolean = False
            Dim aperto As Boolean = False

            For Each regolaGrezza In testo.Split(";"c)
                Try
                    Dim regola As String = regolaGrezza.Trim()
                    If regola.Length = 0 Then Continue For
                    Dim virgolette As Integer = regola.IndexOf(""""c)
                    If virgolette >= 0 Then regola = regola.Substring(0, virgolette).Trim()
                    If regola.Length = 0 Then Continue For

                    Dim indiceOra As Integer = IndiceInizioOrari(regola)
                    Dim parteGiorni As String = If(indiceOra < 0, regola, regola.Substring(0, indiceOra).Trim())
                    Dim parteOrari As String = If(indiceOra < 0, "", regola.Substring(indiceOra).Trim())

                    If parteGiorni.Length > 0 AndAlso Not GiorniIncludonoOggi(parteGiorni, adesso) Then Continue For

                    trovataRegola = True

                    Dim minuscolo As String = parteOrari.ToLowerInvariant()
                    If minuscolo = "off" OrElse minuscolo = "closed" Then
                        aperto = False
                        Continue For
                    End If

                    For Each intervallo In parteOrari.Split(","c)
                        If IntervalloAttivo(intervallo.Trim(), adesso) Then
                            aperto = True
                            Exit For
                        End If
                    Next
                Catch
                End Try
            Next

            If Not trovataRegola Then Return 0
            Return If(aperto, 1, 0)
        Catch ex As Exception
            ScriviLog("CLSLuoghi.ValutaApertura: " & ex.Message)
            Return -1
        End Try
    End Function

    Public Function StatoAperturaLeggibile(stato As Integer) As String
        Try
            Select Case stato
                Case 1 : Return "aperto adesso"
                Case 0 : Return "chiuso adesso"
                Case Else : Return "orario non indicato"
            End Select
        Catch
            Return "orario non indicato"
        End Try
    End Function

    Private Function GiorniIncludonoOggi(parteGiorni As String, adesso As DateTime) As Boolean
        Try
            Dim p As String = parteGiorni.Trim().ToLowerInvariant()
            If p.Length = 0 Then Return True
            p = p.Replace("ph", "").Trim(","c, " "c)
            If p.Length = 0 Then Return True

            Dim oggi As Integer = CInt(adesso.DayOfWeek)
            oggi = If(oggi = 0, 6, oggi - 1)

            For Each blocco In p.Split(","c)
                Dim b As String = blocco.Trim()
                If b.Length = 0 Then Continue For
                If b.Contains("-") Then
                    Dim estremi = b.Split("-"c)
                    If estremi.Length = 2 Then
                        Dim gi As Integer = IndiceGiorno(estremi(0).Trim())
                        Dim gf As Integer = IndiceGiorno(estremi(1).Trim())
                        If gi < 0 OrElse gf < 0 Then Continue For
                        If gi <= gf Then
                            If oggi >= gi AndAlso oggi <= gf Then Return True
                        Else
                            If oggi >= gi OrElse oggi <= gf Then Return True
                        End If
                    End If
                Else
                    If IndiceGiorno(b) = oggi Then Return True
                End If
            Next
            Return False
        Catch ex As Exception
            ScriviLog("CLSLuoghi.GiorniIncludonoOggi: " & ex.Message)
            Return False
        End Try
    End Function

    Private Function IndiceGiorno(codice As String) As Integer
        Try
            Select Case codice.Trim().ToLowerInvariant()
                Case "mo", "mon" : Return 0
                Case "tu", "tue" : Return 1
                Case "we", "wed" : Return 2
                Case "th", "thu" : Return 3
                Case "fr", "fri" : Return 4
                Case "sa", "sat" : Return 5
                Case "su", "sun" : Return 6
                Case Else : Return -1
            End Select
        Catch
            Return -1
        End Try
    End Function

    Private Function IntervalloAttivo(intervallo As String, adesso As DateTime) As Boolean
        Try
            If intervallo.Length = 0 Then Return False
            Dim estremi = intervallo.Split("-"c)
            If estremi.Length <> 2 Then Return False

            Dim inizio As Integer = MinutiDaTesto(estremi(0).Trim())
            Dim fine As Integer = MinutiDaTesto(estremi(1).Trim())
            If inizio < 0 OrElse fine < 0 Then Return False

            Dim ora As Integer = adesso.Hour * 60 + adesso.Minute

            If fine >= inizio Then Return ora >= inizio AndAlso ora <= fine
            Return ora >= inizio OrElse ora <= fine
        Catch ex As Exception
            ScriviLog("CLSLuoghi.IntervalloAttivo: " & ex.Message)
            Return False
        End Try
    End Function

    Private Function MinutiDaTesto(testo As String) As Integer
        Try
            Dim parti = testo.Split(":"c)
            If parti.Length < 2 Then Return -1
            Dim ore As Integer = 0
            Dim minuti As Integer = 0
            If Not Integer.TryParse(parti(0).Trim(), ore) Then Return -1
            If Not Integer.TryParse(parti(1).Trim(), minuti) Then Return -1
            If ore < 0 OrElse ore > 48 OrElse minuti < 0 OrElse minuti > 59 Then Return -1
            Return (ore Mod 24) * 60 + minuti
        Catch
            Return -1
        End Try
    End Function

    ''' <summary>
    ''' Cerca in un comune le attivita' di piu' categorie contemporaneamente, unendo
    ''' i risultati in un unico elenco senza doppioni.
    ''' </summary>
    Public Function CercaAttivitaPiuCategorie(comune As String, categorie As List(Of String),
                                              Optional soloAperte As Boolean = False,
                                              Optional avanzamento As Avanzamento = Nothing) As List(Of Attivita)
        Dim risultato As New List(Of Attivita)
        Try
            If String.IsNullOrWhiteSpace(comune) OrElse categorie Is Nothing OrElse categorie.Count = 0 Then Return risultato

            Dim giaVisti As New HashSet(Of String)(StringComparer.OrdinalIgnoreCase)
            Dim indice As Integer = 0

            For Each categoria In categorie
                Try
                    indice += 1
                    Segnala(avanzamento, "Cerco " & categoria.ToLowerInvariant() & " (" & indice.ToString() & " di " & categorie.Count.ToString() & ")")

                    Dim parziale As List(Of Attivita)
                    If soloAperte Then
                        parziale = CercaAttivitaAperte(comune, categoria, avanzamento)
                    Else
                        parziale = CercaAttivita(comune, categoria, avanzamento)
                    End If

                    If parziale Is Nothing Then Continue For

                    For Each a In parziale
                        Try
                            Dim chiave As String = Normalizza(a.Nome) & "|" & Normalizza(a.Indirizzo)
                            If giaVisti.Contains(chiave) Then Continue For
                            giaVisti.Add(chiave)
                            risultato.Add(a)
                        Catch
                        End Try
                    Next
                Catch exCategoria As Exception
                    ScriviLog("CLSLuoghi.CercaAttivitaPiuCategorie categoria: " & exCategoria.Message)
                End Try
            Next

            risultato = ScartaSenzaIndirizzo(risultato)
            OrdinaPerDistanza(risultato)
            Segnala(avanzamento, "Ricerca completata")
        Catch ex As Exception
            ScriviLog("CLSLuoghi.CercaAttivitaPiuCategorie: " & ex.ToString())
        End Try
        Return risultato
    End Function

    ''' <summary>
    ''' Cerca le attivita' di piu' categorie entro un raggio da un indirizzo preciso,
    ''' unendo i risultati senza doppioni.
    ''' </summary>
    Public Function CercaVicinoPiuCategorie(indirizzo As String, categorie As List(Of String),
                                            Optional raggioMetri As Integer = 1500,
                                            Optional avanzamento As Avanzamento = Nothing) As List(Of Attivita)
        Dim risultato As New List(Of Attivita)
        Try
            If String.IsNullOrWhiteSpace(indirizzo) OrElse categorie Is Nothing OrElse categorie.Count = 0 Then Return risultato

            Dim giaVisti As New HashSet(Of String)(StringComparer.OrdinalIgnoreCase)
            Dim indice As Integer = 0

            For Each categoria In categorie
                Try
                    indice += 1
                    Segnala(avanzamento, "Cerco " & categoria.ToLowerInvariant() & " (" & indice.ToString() & " di " & categorie.Count.ToString() & ")")

                    Dim parziale = CercaVicinoAIndirizzo(indirizzo, categoria, raggioMetri, avanzamento)
                    If parziale Is Nothing Then Continue For

                    For Each a In parziale
                        Try
                            Dim chiave As String = Normalizza(a.Nome) & "|" & Normalizza(a.Indirizzo)
                            If giaVisti.Contains(chiave) Then Continue For
                            giaVisti.Add(chiave)
                            risultato.Add(a)
                        Catch
                        End Try
                    Next
                Catch exCategoria As Exception
                    ScriviLog("CLSLuoghi.CercaVicinoPiuCategorie categoria: " & exCategoria.Message)
                End Try
            Next

            risultato = ScartaSenzaIndirizzo(risultato)
            OrdinaPerDistanza(risultato)
            Segnala(avanzamento, "Ricerca completata")
        Catch ex As Exception
            ScriviLog("CLSLuoghi.CercaVicinoPiuCategorie: " & ex.ToString())
        End Try
        Return risultato
    End Function

    ''' <summary>
    ''' Elenco completo delle categorie, senza raggruppamento, per chi preferisce
    ''' scorrere una lista unica invece di passare dai gruppi.
    ''' </summary>
    Public Function TutteLeCategorie() As List(Of String)
        Return CategorieDisponibili()
    End Function

    ''' <summary>
    ''' Dice se il motore di interrogazione dei dati Overture e' gia' presente sul computer.
    ''' </summary>
    Public Function MotoreOverturePronto() As Boolean
        Try
            Return _overture.MotorePronto()
        Catch ex As Exception
            ScriviLog("CLSLuoghi.MotoreOverturePronto: " & ex.Message)
            Return False
        End Try
    End Function

    ''' <summary>
    ''' Restituisce le informazioni di una attivita' in righe pronte da leggere.
    ''' </summary>
    Private Shared ReadOnly _categorieEnciclopediche As String() = {
        "Musei e luoghi di interesse",
        "Chiese e luoghi di culto",
        "Cinema e teatri",
        "Biblioteche",
        "Parchi e giardini",
        "Universita e istituti superiori"
    }

    Private Function CategoriaMeritaEnciclopedia(categoria As String) As Boolean
        Try
            If String.IsNullOrWhiteSpace(categoria) Then Return False
            For Each c In _categorieEnciclopediche
                If String.Equals(c, categoria, StringComparison.OrdinalIgnoreCase) Then Return True
            Next
            Return False
        Catch
            Return False
        End Try
    End Function

    Private Sub ArricchisciConWikidata(elenco As List(Of Attivita), latitudine As Double, longitudine As Double, avanzamento As Avanzamento)
        Try
            If elenco Is Nothing OrElse elenco.Count = 0 Then Return

            Dim daArricchire As New List(Of Attivita)
            For Each a In elenco
                If CategoriaMeritaEnciclopedia(a.Categoria) Then daArricchire.Add(a)
            Next
            If daArricchire.Count = 0 Then Return

            Segnala(avanzamento, "Cerco notizie storiche su Wikidata")

            Dim voci = _wikidata.CercaIntorno(latitudine, longitudine, 8, 400)
            If voci Is Nothing OrElse voci.Count = 0 Then Return

            Dim arricchiti As Integer = 0
            For Each a In daArricchire
                Try
                    Dim v = _wikidata.TrovaPerNome(voci, a.Nome, a.Latitudine, a.Longitudine)
                    If v Is Nothing Then Continue For
                    Dim righe = _wikidata.InformazioniVoce(v)
                    If righe Is Nothing OrElse righe.Count = 0 Then Continue For
                    a.RigheEnciclopediche = righe
                    arricchiti += 1
                Catch exSingolo As Exception
                    ScriviLog("CLSLuoghi.ArricchisciConWikidata elemento: " & exSingolo.Message)
                End Try
            Next

        Catch ex As Exception
            ScriviLog("CLSLuoghi.ArricchisciConWikidata: " & ex.ToString())
        End Try
    End Sub

    Private Function RigheAccessibilita(a As Attivita) As List(Of String)
        Dim righe As New List(Of String)
        Try
            If a Is Nothing Then Return righe

            If Not String.IsNullOrWhiteSpace(a.DescrizionePerNonVedenti) Then
                righe.Add("Indicazioni per chi non vede: " & a.DescrizionePerNonVedenti)
            End If

            Select Case Normalizza(a.PercorsoTattile)
                Case "YES", "PRIMARY", "CONTRASTED" : righe.Add("Percorso tattile: presente")
                Case "NO" : righe.Add("Percorso tattile: assente")
                Case "INCORRECT" : righe.Add("Percorso tattile: presente ma segnalato come non corretto")
            End Select

            Select Case Normalizza(a.SegnalatoreAcustico)
                Case "YES" : righe.Add("Segnalatore acustico: presente")
                Case "NO" : righe.Add("Segnalatore acustico: assente")
            End Select

            Select Case Normalizza(a.Braille)
                Case "YES" : righe.Add("Scritte in braille: presenti")
                Case "NO" : righe.Add("Scritte in braille: assenti")
            End Select

            Select Case Normalizza(a.Corrimano)
                Case "YES" : righe.Add("Corrimano: presente")
                Case "NO" : righe.Add("Corrimano: assente")
            End Select

            Select Case Normalizza(a.CaniGuidaAmmessi)
                Case "YES", "LEASHED", "OFFICIAL" : righe.Add("Cani guida: ammessi")
                Case "NO" : righe.Add("Cani guida: non ammessi")
            End Select

            Select Case Normalizza(a.AccessoSediaRotelle)
                Case "YES" : righe.Add("Accesso in sedia a rotelle: possibile")
                Case "LIMITED" : righe.Add("Accesso in sedia a rotelle: parzialmente possibile")
                Case "NO" : righe.Add("Accesso in sedia a rotelle: non possibile")
            End Select

            Select Case Normalizza(a.BagnoAccessibile)
                Case "YES" : righe.Add("Bagno accessibile: presente")
                Case "LIMITED" : righe.Add("Bagno accessibile: presente con limitazioni")
                Case "NO" : righe.Add("Bagno accessibile: assente")
            End Select

            If righe.Count = 0 Then
                righe.Add("Informazioni sull'accessibilita': non ancora rilevate per questo luogo")
            End If
        Catch ex As Exception
            ScriviLog("CLSLuoghi.RigheAccessibilita: " & ex.Message)
        End Try
        Return righe
    End Function

    Public Function InformazioniAttivita(a As Attivita) As List(Of String)
        Dim righe As New List(Of String)
        Try
            If a Is Nothing Then Return righe

            righe.Add("Nome: " & If(String.IsNullOrWhiteSpace(a.Nome), "non disponibile", a.Nome))
            righe.Add("Categoria: " & If(String.IsNullOrWhiteSpace(a.Categoria), "non indicata", a.Categoria))

            Dim indirizzoCompleto As New StringBuilder()
            If Not String.IsNullOrWhiteSpace(a.Indirizzo) Then indirizzoCompleto.Append(a.Indirizzo)
            If Not String.IsNullOrWhiteSpace(a.Comune) Then
                If indirizzoCompleto.Length > 0 Then indirizzoCompleto.Append(", ")
                indirizzoCompleto.Append(a.Comune)
            End If
            righe.Add("Indirizzo: " & If(indirizzoCompleto.Length > 0, indirizzoCompleto.ToString(), "non disponibile"))

            righe.Add("Telefono: " & If(String.IsNullOrWhiteSpace(a.Telefono), "non disponibile", a.Telefono))

            If Not String.IsNullOrWhiteSpace(a.Email) Then righe.Add("Email: " & a.Email)
            If Not String.IsNullOrWhiteSpace(a.Sito) Then righe.Add("Sito internet: " & a.Sito)

            If String.IsNullOrWhiteSpace(a.Orari) Then
                If a.OrarioNonRilevato Then
                    righe.Add("Orario: l'attivita' ha un orario esposto, ma nessuno lo ha ancora registrato negli archivi")
                Else
                    righe.Add("Orario: non indicato")
                End If
            Else
                righe.Add("Orario: " & OrariLeggibili(a.Orari))
            End If

            If Not String.IsNullOrWhiteSpace(a.OrarioCucina) Then
                righe.Add("Orario della cucina: " & OrariLeggibili(a.OrarioCucina))
            End If

            righe.Add("Stato adesso: " & StatoAperturaLeggibile(a.StatoApertura))

            Try
                For Each riga In RigheAccessibilita(a)
                    righe.Add(riga)
                Next
            Catch exAcc As Exception
                ScriviLog("CLSLuoghi.InformazioniAttivita accessibilita: " & exAcc.Message)
            End Try

            Try
                If a.RigheEnciclopediche IsNot Nothing Then
                    For Each riga In a.RigheEnciclopediche
                        righe.Add(riga)
                    Next
                End If
            Catch exEnc As Exception
                ScriviLog("CLSLuoghi.InformazioniAttivita enciclopedia: " & exEnc.Message)
            End Try

            If a.DistanzaKm > 0 Then
                righe.Add("Distanza dal centro del comune: circa " & a.DistanzaKm.ToString("N1", New CultureInfo("it-IT")) & " chilometri")
            End If

            If Not String.IsNullOrWhiteSpace(a.Marca) Then righe.Add("Catena o marca: " & a.Marca)
            righe.Add("Fonte dei dati: " & If(String.IsNullOrWhiteSpace(a.Fonte), "OpenStreetMap", a.Fonte))
        Catch ex As Exception
            ScriviLog("CLSLuoghi.InformazioniAttivita: " & ex.ToString())
        End Try
        Return righe
    End Function

    ''' <summary>
    ''' Riga singola con i dati principali, adatta a un elenco.
    ''' </summary>
    Public Function EtichettaAttivita(a As Attivita) As String
        Try
            If a Is Nothing Then Return "Attivita"
            Dim e As New StringBuilder()
            e.Append(If(String.IsNullOrWhiteSpace(a.Nome), "Senza nome", a.Nome))
            If Not String.IsNullOrWhiteSpace(a.Indirizzo) Then e.Append(", ").Append(a.Indirizzo)
            If Not String.IsNullOrWhiteSpace(a.Comune) Then e.Append(", ").Append(a.Comune)
            If a.StatoApertura >= 0 Then e.Append(", ").Append(StatoAperturaLeggibile(a.StatoApertura))
            If Not String.IsNullOrWhiteSpace(a.Telefono) Then e.Append(", telefono ").Append(a.Telefono)
            If Not String.IsNullOrWhiteSpace(a.Orari) Then e.Append(", orario ").Append(OrariLeggibili(a.Orari))
            Return e.ToString()
        Catch ex As Exception
            ScriviLog("CLSLuoghi.EtichettaAttivita: " & ex.Message)
            Return "Attivita"
        End Try
    End Function

    Private Function LeggiElemento(elemento As JToken, nomeCategoria As String) As Attivita
        Try
            Dim tag = elemento("tags")
            If tag Is Nothing Then Return Nothing

            Dim a As New Attivita()
            a.Categoria = nomeCategoria
            a.Fonte = "OpenStreetMap"
            a.Nome = ValoreTag(tag, "name")

            Try
                Dim tipoElemento As String = If(elemento("type") Is Nothing, "node", elemento("type").ToString())
                Dim idElemento As String = If(elemento("id") Is Nothing, "", elemento("id").ToString())
                If idElemento.Length > 0 Then a.IdOsm = tipoElemento & idElemento
            Catch
            End Try

            Dim via As String = ValoreTag(tag, "addr:street")
            Dim civico As String = ValoreTag(tag, "addr:housenumber")
            If via.Length > 0 Then
                a.Indirizzo = If(civico.Length > 0, (via & " " & civico).Trim(), via)
            End If

            a.Comune = ValoreTag(tag, "addr:city")
            a.Telefono = ValoreTag(tag, "phone")
            If a.Telefono.Length = 0 Then a.Telefono = ValoreTag(tag, "contact:phone")
            a.Email = ValoreTag(tag, "email")
            If a.Email.Length = 0 Then a.Email = ValoreTag(tag, "contact:email")
            a.Sito = ValoreTag(tag, "website")
            If a.Sito.Length = 0 Then a.Sito = ValoreTag(tag, "contact:website")
            a.Orari = ValoreTag(tag, "opening_hours")
            If a.Orari.Length = 0 Then a.Orari = ValoreTag(tag, "service_times")
            a.OrarioCucina = ValoreTag(tag, "opening_hours:kitchen")
            a.OrarioNonRilevato = String.Equals(ValoreTag(tag, "opening_hours:signed"), "no", StringComparison.OrdinalIgnoreCase)

            a.PercorsoTattile = ValoreTag(tag, "tactile_paving")
            a.SegnalatoreAcustico = ValoreTag(tag, "traffic_signals:sound")
            If a.SegnalatoreAcustico.Length = 0 Then a.SegnalatoreAcustico = ValoreTag(tag, "acoustic")
            a.Braille = ValoreTag(tag, "braille")
            If a.Braille.Length = 0 Then a.Braille = ValoreTag(tag, "menu:braille")
            a.Corrimano = ValoreTag(tag, "handrail")
            a.DescrizionePerNonVedenti = ValoreTag(tag, "blind:description")
            If a.DescrizionePerNonVedenti.Length = 0 Then a.DescrizionePerNonVedenti = ValoreTag(tag, "description:blind")
            a.AccessoSediaRotelle = ValoreTag(tag, "wheelchair")
            a.BagnoAccessibile = ValoreTag(tag, "toilets:wheelchair")
            a.CaniGuidaAmmessi = ValoreTag(tag, "guide_dog")
            If a.CaniGuidaAmmessi.Length = 0 Then a.CaniGuidaAmmessi = ValoreTag(tag, "dog")

            If elemento("lat") IsNot Nothing AndAlso elemento("lon") IsNot Nothing Then
                a.Latitudine = ParseCoordinata(elemento("lat").ToString())
                a.Longitudine = ParseCoordinata(elemento("lon").ToString())
            ElseIf elemento("center") IsNot Nothing Then
                Dim centro = elemento("center")
                If centro("lat") IsNot Nothing Then a.Latitudine = ParseCoordinata(centro("lat").ToString())
                If centro("lon") IsNot Nothing Then a.Longitudine = ParseCoordinata(centro("lon").ToString())
            End If

            Return a
        Catch ex As Exception
            ScriviLog("CLSLuoghi.LeggiElemento: " & ex.Message)
            Return Nothing
        End Try
    End Function

#End Region

#Region "Rete"

    Private Function ComponiCondizioni(filtro As String) As String
        Try
            If String.IsNullOrWhiteSpace(filtro) Then Return ""

            Dim risultato As New StringBuilder()

            For Each pezzo In filtro.Split("+"c)
                Dim p As String = pezzo.Trim()
                If p.Length = 0 Then Continue For

                Dim indiceCirca As Integer = p.IndexOf("~"c)
                Dim indiceUguale As Integer = p.IndexOf("="c)

                If indiceCirca > 0 Then
                    Dim chiave As String = p.Substring(0, indiceCirca).Trim()
                    Dim valore As String = p.Substring(indiceCirca + 1).Trim()
                    If chiave.Length = 0 OrElse valore.Length = 0 Then Continue For
                    risultato.Append("[""").Append(chiave).Append("""~""").Append(valore).Append(""",i]")
                ElseIf indiceUguale > 0 Then
                    Dim chiave As String = p.Substring(0, indiceUguale).Trim()
                    Dim valore As String = p.Substring(indiceUguale + 1).Trim()
                    If chiave.Length = 0 OrElse valore.Length = 0 Then Continue For
                    risultato.Append("[""").Append(chiave).Append("""=""").Append(valore).Append("""]")
                Else
                    risultato.Append("[""").Append(p).Append("""]")
                End If
            Next

            Return risultato.ToString()
        Catch ex As Exception
            ScriviLog("CLSLuoghi.ComponiCondizioni: " & ex.Message)
            Return ""
        End Try
    End Function

    Private Function ComponiArea(bbox As Double(), latitudine As Double, longitudine As Double, raggioMetri As Integer) As String
        Try
            If bbox IsNot Nothing AndAlso bbox.Length = 4 Then
                Return "(" & bbox(0).ToString(CultureInfo.InvariantCulture) & "," &
                             bbox(2).ToString(CultureInfo.InvariantCulture) & "," &
                             bbox(1).ToString(CultureInfo.InvariantCulture) & "," &
                             bbox(3).ToString(CultureInfo.InvariantCulture) & ")"
            End If
            Return "(around:" & raggioMetri.ToString(CultureInfo.InvariantCulture) & "," &
                   latitudine.ToString(CultureInfo.InvariantCulture) & "," &
                   longitudine.ToString(CultureInfo.InvariantCulture) & ")"
        Catch ex As Exception
            ScriviLog("CLSLuoghi.ComponiArea: " & ex.Message)
            Return "(around:8000,0,0)"
        End Try
    End Function

    Private Function InviaOverpass(query As String) As String
        Try
            For Each url In _serverOverpass
                Try
                    Using contenuto As New StringContent("data=" & Uri.EscapeDataString(query), Encoding.UTF8, "application/x-www-form-urlencoded")
                        Dim risposta = _http.PostAsync(url, contenuto).GetAwaiter().GetResult()
                        If risposta.IsSuccessStatusCode Then
                            Dim testo As String = risposta.Content.ReadAsStringAsync().GetAwaiter().GetResult()
                            If Not String.IsNullOrWhiteSpace(testo) AndAlso testo.Contains("elements") Then Return testo
                        Else
                            Dim codice As Integer = CInt(risposta.StatusCode)
                            ' 429 significa troppe richieste, 504 che il server e' sovraccarico:
                            ' in entrambi i casi conviene lasciar respirare prima del prossimo.
                            If codice = 429 OrElse codice = 504 Then
                                Try
                                    Thread.Sleep(1500)
                                Catch
                                End Try
                            End If
                        End If
                    End Using
                Catch exSingolo As Exception
                    ScriviLog("CLSLuoghi.InviaOverpass " & url & ": " & exSingolo.Message)
                End Try
            Next
            Return ""
        Catch ex As Exception
            ScriviLog("CLSLuoghi.InviaOverpass: " & ex.ToString())
            Return ""
        End Try
    End Function

    Private Function InterrogaOverpass(cat As Categoria, bbox As Double(), latitudine As Double, longitudine As Double, Optional raggioMetri As Integer = 8000) As String
        Try
            Dim query As New StringBuilder()
            query.Append("[out:json][timeout:60];(")

            Dim area As String
            If bbox IsNot Nothing AndAlso bbox.Length = 4 Then
                area = "(" & bbox(0).ToString(CultureInfo.InvariantCulture) & "," &
                             bbox(2).ToString(CultureInfo.InvariantCulture) & "," &
                             bbox(1).ToString(CultureInfo.InvariantCulture) & "," &
                             bbox(3).ToString(CultureInfo.InvariantCulture) & ")"
            Else
                area = "(around:" & raggioMetri.ToString(CultureInfo.InvariantCulture) & "," & latitudine.ToString(CultureInfo.InvariantCulture) & "," & longitudine.ToString(CultureInfo.InvariantCulture) & ")"
            End If

            For Each filtro In cat.Filtri
                Dim condizioni As String = ComponiCondizioni(filtro)
                If condizioni.Length = 0 Then Continue For
                query.Append("nwr").Append(condizioni).Append(area).Append(";")
            Next

            query.Append(");out center tags;")

            Return InviaOverpass(query.ToString())
        Catch ex As Exception
            ScriviLog("CLSLuoghi.InterrogaOverpass: " & ex.ToString())
            Return ""
        End Try
    End Function

    Private Function InterrogaOverpassPerNome(testo As String, bbox As Double(), latitudine As Double, longitudine As Double) As String
        Try
            Dim area As String
            If bbox IsNot Nothing AndAlso bbox.Length = 4 Then
                area = "(" & bbox(0).ToString(CultureInfo.InvariantCulture) & "," &
                             bbox(2).ToString(CultureInfo.InvariantCulture) & "," &
                             bbox(1).ToString(CultureInfo.InvariantCulture) & "," &
                             bbox(3).ToString(CultureInfo.InvariantCulture) & ")"
            Else
                area = "(around:8000," & latitudine.ToString(CultureInfo.InvariantCulture) & "," & longitudine.ToString(CultureInfo.InvariantCulture) & ")"
            End If

            Dim testoPulito As String = testo.Trim().Replace("""", "").Replace("\", "")

            Dim query As New StringBuilder()
            query.Append("[out:json][timeout:60];(")
            query.Append("node[""name""~""").Append(testoPulito).Append(""",i]").Append(area).Append(";")
            query.Append("way[""name""~""").Append(testoPulito).Append(""",i]").Append(area).Append(";")
            query.Append(");out center tags;")

            Return InviaOverpass(query.ToString())
        Catch ex As Exception
            ScriviLog("CLSLuoghi.InterrogaOverpassPerNome: " & ex.ToString())
            Return ""
        End Try
    End Function

    Private Function RisolviComune(comune As String, ByRef latitudine As Double, ByRef longitudine As Double,
                                   ByRef comuneRisolto As String, ByRef bbox As Double(),
                                   ByRef poligono As List(Of List(Of Tuple(Of Double, Double)))) As Boolean
        Try
            comuneRisolto = ""
            bbox = Nothing
            poligono = Nothing

            Dim chiave As String = Normalizza(comune)

            SyncLock _lockCache
                If _cacheComuni.ContainsKey(chiave) Then
                    Dim v = _cacheComuni(chiave)
                    latitudine = v(0)
                    longitudine = v(1)
                    If v.Length >= 6 Then bbox = New Double() {v(2), v(3), v(4), v(5)}
                    If _cachePoligoni.ContainsKey(chiave) Then poligono = _cachePoligoni(chiave)
                    Return latitudine <> 0 AndAlso longitudine <> 0
                End If
            End SyncLock

            Dim url As String = URL_NOMINATIM & "?format=json&limit=1&countrycodes=it&addressdetails=1&polygon_geojson=1&q=" & Uri.EscapeDataString(comune)
            AttendiTurnoNominatim()
            Dim json As String = ScaricaTesto(url)
            If String.IsNullOrWhiteSpace(json) Then Return False

            Dim radice As JToken = JToken.Parse(json)
            If radice.Type <> JTokenType.Array OrElse Not radice.HasValues Then Return False

            Dim primo As JToken = radice(0)
            latitudine = ParseCoordinata(If(primo("lat") Is Nothing, "", primo("lat").ToString()))
            longitudine = ParseCoordinata(If(primo("lon") Is Nothing, "", primo("lon").ToString()))
            If latitudine = 0 OrElse longitudine = 0 Then Return False

            Dim indirizzo = primo("address")
            If indirizzo IsNot Nothing AndAlso indirizzo.Type = JTokenType.Object Then
                Dim oggetto As JObject = DirectCast(indirizzo, JObject)
                For Each campo In New String() {"city", "town", "village", "municipality"}
                    Dim v = oggetto(campo)
                    If v IsNot Nothing AndAlso v.Type = JTokenType.String Then
                        Dim t As String = v.ToString().Trim()
                        If t.Length > 0 Then
                            comuneRisolto = t
                            Exit For
                        End If
                    End If
                Next
            End If

            Dim box = primo("boundingbox")
            If box IsNot Nothing AndAlso box.Type = JTokenType.Array AndAlso box.Count() = 4 Then
                Try
                    Dim s As Double = ParseCoordinata(box(0).ToString())
                    Dim n As Double = ParseCoordinata(box(1).ToString())
                    Dim w As Double = ParseCoordinata(box(2).ToString())
                    Dim e As Double = ParseCoordinata(box(3).ToString())
                    If n > s AndAlso e > w Then bbox = New Double() {s, n, w, e}
                Catch
                End Try
            End If

            poligono = EstraiAnelli(primo("geojson"))

            SyncLock _lockCache
                Dim valori As Double()
                If bbox IsNot Nothing Then
                    valori = New Double() {latitudine, longitudine, bbox(0), bbox(1), bbox(2), bbox(3)}
                Else
                    valori = New Double() {latitudine, longitudine}
                End If
                _cacheComuni(chiave) = valori
                If poligono IsNot Nothing AndAlso poligono.Count > 0 Then _cachePoligoni(chiave) = poligono
            End SyncLock

            Return True
        Catch ex As Exception
            ScriviLog("CLSLuoghi.RisolviComune: " & ex.ToString())
            Return False
        End Try
    End Function

    ''' <summary>
    ''' Fa attendere il chiamante quel poco che serve a non superare una richiesta al
    ''' secondo verso Nominatim, come richiesto dalle sue condizioni d'uso.
    ''' </summary>
    Private Sub AttendiTurnoNominatim()
        Try
            SyncLock _lockNominatim
                Dim trascorso As Double = (DateTime.UtcNow - _ultimaChiamataNominatim).TotalMilliseconds
                If trascorso < 1100 Then
                    Dim daAttendere As Integer = CInt(1100 - trascorso)
                    If daAttendere > 0 AndAlso daAttendere <= 1100 Then
                        Try
                            Thread.Sleep(daAttendere)
                        Catch
                        End Try
                    End If
                End If
                _ultimaChiamataNominatim = DateTime.UtcNow
            End SyncLock
        Catch ex As Exception
            ScriviLog("CLSLuoghi.AttendiTurnoNominatim: " & ex.Message)
        End Try
    End Sub

    Private Function ScaricaTesto(url As String) As String
        For i As Integer = 1 To 3
            Try
                Using richiesta As New HttpRequestMessage(HttpMethod.Get, url)
                    Dim risposta = _http.SendAsync(richiesta).GetAwaiter().GetResult()
                    If risposta.IsSuccessStatusCode Then
                        Return risposta.Content.ReadAsStringAsync().GetAwaiter().GetResult()
                    End If
                End Using
            Catch
            End Try
            Try
                Thread.Sleep(1200 * i)
            Catch
            End Try
        Next
        Return ""
    End Function

#End Region

#Region "Geometria"

    Private Function EstraiAnelli(geojson As JToken) As List(Of List(Of Tuple(Of Double, Double)))
        Dim anelli As New List(Of List(Of Tuple(Of Double, Double)))
        Try
            If geojson Is Nothing Then Return anelli
            Dim tipo As String = If(geojson("type") Is Nothing, "", geojson("type").ToString())
            Dim coordinate = geojson("coordinates")
            If coordinate Is Nothing Then Return anelli

            If String.Equals(tipo, "Polygon", StringComparison.OrdinalIgnoreCase) Then
                AggiungiAnello(coordinate, anelli)
            ElseIf String.Equals(tipo, "MultiPolygon", StringComparison.OrdinalIgnoreCase) Then
                For Each poligono In coordinate.Children()
                    AggiungiAnello(poligono, anelli)
                Next
            End If
        Catch ex As Exception
            ScriviLog("CLSLuoghi.EstraiAnelli: " & ex.Message)
        End Try
        Return anelli
    End Function

    Private Sub AggiungiAnello(poligono As JToken, anelli As List(Of List(Of Tuple(Of Double, Double))))
        Try
            If poligono Is Nothing OrElse Not poligono.HasValues Then Return
            Dim esterno = poligono(0)
            If esterno Is Nothing Then Return

            Dim punti As New List(Of Tuple(Of Double, Double))
            For Each coppia In esterno.Children()
                Try
                    If coppia.Type = JTokenType.Array AndAlso coppia.Count() >= 2 Then
                        Dim lon As Double = ParseCoordinata(coppia(0).ToString())
                        Dim lat As Double = ParseCoordinata(coppia(1).ToString())
                        punti.Add(Tuple.Create(lat, lon))
                    End If
                Catch
                End Try
            Next

            If punti.Count >= 3 Then anelli.Add(punti)
        Catch ex As Exception
            ScriviLog("CLSLuoghi.AggiungiAnello: " & ex.Message)
        End Try
    End Sub

    Private Function PuntoInPoligono(lat As Double, lon As Double, anelli As List(Of List(Of Tuple(Of Double, Double)))) As Boolean
        Try
            If anelli Is Nothing OrElse anelli.Count = 0 Then Return False
            For Each anello In anelli
                If PuntoInAnello(lat, lon, anello) Then Return True
            Next
            Return False
        Catch ex As Exception
            ScriviLog("CLSLuoghi.PuntoInPoligono: " & ex.Message)
            Return False
        End Try
    End Function

    Private Function PuntoInAnello(lat As Double, lon As Double, anello As List(Of Tuple(Of Double, Double))) As Boolean
        Try
            If anello Is Nothing OrElse anello.Count < 3 Then Return False
            Dim dentro As Boolean = False
            Dim j As Integer = anello.Count - 1
            For i As Integer = 0 To anello.Count - 1
                Dim latI As Double = anello(i).Item1
                Dim lonI As Double = anello(i).Item2
                Dim latJ As Double = anello(j).Item1
                Dim lonJ As Double = anello(j).Item2

                If ((lonI > lon) <> (lonJ > lon)) AndAlso
                   (lat < (latJ - latI) * (lon - lonI) / (lonJ - lonI) + latI) Then
                    dentro = Not dentro
                End If
                j = i
            Next
            Return dentro
        Catch ex As Exception
            ScriviLog("CLSLuoghi.PuntoInAnello: " & ex.Message)
            Return False
        End Try
    End Function

    Private Function DistanzaChilometri(lat1 As Double, lon1 As Double, lat2 As Double, lon2 As Double) As Double
        Try
            Dim raggio As Double = 6371.0
            Dim dLat As Double = (lat2 - lat1) * Math.PI / 180.0
            Dim dLon As Double = (lon2 - lon1) * Math.PI / 180.0
            Dim a As Double = Math.Sin(dLat / 2) * Math.Sin(dLat / 2) +
                              Math.Cos(lat1 * Math.PI / 180.0) * Math.Cos(lat2 * Math.PI / 180.0) *
                              Math.Sin(dLon / 2) * Math.Sin(dLon / 2)
            Dim c As Double = 2 * Math.Atan2(Math.Sqrt(a), Math.Sqrt(1 - a))
            Return raggio * c
        Catch ex As Exception
            ScriviLog("CLSLuoghi.DistanzaChilometri: " & ex.Message)
            Return 0
        End Try
    End Function

#End Region

#Region "Utilita"

    Private Shared ReadOnly _nomiGiorni As String() = {"lunedi", "martedi", "mercoledi", "giovedi", "venerdi", "sabato", "domenica"}

    Public Function OrariLeggibili(orari As String) As String
        Try
            If String.IsNullOrWhiteSpace(orari) Then Return "orario non disponibile"
            Dim testo As String = orari.Trim()
            If testo = "24/7" Then Return "aperto 24 ore su 24, tutti i giorni"

            Dim frasi As New List(Of String)
            For Each regolaGrezza In testo.Split(";"c)
                Try
                    Dim regola As String = regolaGrezza.Trim()
                    If regola.Length = 0 Then Continue For
                    Dim virgolette As Integer = regola.IndexOf(""""c)
                    If virgolette >= 0 Then regola = regola.Substring(0, virgolette).Trim()
                    If regola.Length = 0 Then Continue For

                    Dim indiceOra As Integer = IndiceInizioOrari(regola)
                    Dim parteGiorni As String = If(indiceOra < 0, regola, regola.Substring(0, indiceOra).Trim())
                    Dim parteOrari As String = If(indiceOra < 0, "", regola.Substring(indiceOra).Trim())

                    Dim tg As String = GiorniLeggibili(parteGiorni)
                    Dim to_ As String = FasceLeggibili(parteOrari)

                    Dim frase As String
                    If tg.Length > 0 AndAlso to_.Length > 0 Then
                        frase = tg & " " & to_
                    ElseIf tg.Length > 0 Then
                        frase = tg
                    Else
                        frase = to_
                    End If
                    If frase.Length > 0 Then frasi.Add(frase)
                Catch
                End Try
            Next

            If frasi.Count = 0 Then Return "orario non disponibile"
            Return String.Join(". ", frasi)
        Catch ex As Exception
            ScriviLog("CLSLuoghi.OrariLeggibili: " & ex.Message)
            Return If(orari, "orario non disponibile")
        End Try
    End Function

    Private Function IndiceInizioOrari(regola As String) As Integer
        Try
            For i As Integer = 0 To regola.Length - 1
                If Char.IsDigit(regola(i)) Then
                    If i + 2 < regola.Length AndAlso regola(i + 2) = ":"c Then Return i
                    If i + 1 < regola.Length AndAlso regola(i + 1) = ":"c Then Return i
                End If
            Next
            Dim off As Integer = regola.IndexOf("off", StringComparison.OrdinalIgnoreCase)
            If off > 0 Then Return off
            Return -1
        Catch
            Return -1
        End Try
    End Function

    Private Function GiorniLeggibili(parte As String) As String
        Try
            Dim p As String = parte.Trim().ToLowerInvariant()
            If p.Length = 0 Then Return ""
            If p = "ph" Then Return "nei giorni festivi"
            p = p.Replace("ph", "").Trim(","c, " "c)
            If p.Length = 0 Then Return ""

            Dim parti As New List(Of String)
            For Each blocco In p.Split(","c)
                Dim b As String = blocco.Trim()
                If b.Length = 0 Then Continue For
                If b.Contains("-") Then
                    Dim estremi = b.Split("-"c)
                    If estremi.Length = 2 Then
                        Dim gi As String = NomeGiorno(estremi(0).Trim())
                        Dim gf As String = NomeGiorno(estremi(1).Trim())
                        If gi.Length > 0 AndAlso gf.Length > 0 Then parti.Add("dal " & gi & " al " & gf)
                    End If
                Else
                    Dim g As String = NomeGiorno(b)
                    If g.Length > 0 Then parti.Add(g)
                End If
            Next

            If parti.Count = 0 Then Return ""
            If parti.Count = 1 Then Return parti(0)
            Return String.Join(", ", parti.Take(parti.Count - 1)) & " e " & parti(parti.Count - 1)
        Catch ex As Exception
            ScriviLog("CLSLuoghi.GiorniLeggibili: " & ex.Message)
            Return ""
        End Try
    End Function

    Private Function NomeGiorno(codice As String) As String
        Try
            Select Case codice.Trim().ToLowerInvariant()
                Case "mo", "mon" : Return _nomiGiorni(0)
                Case "tu", "tue" : Return _nomiGiorni(1)
                Case "we", "wed" : Return _nomiGiorni(2)
                Case "th", "thu" : Return _nomiGiorni(3)
                Case "fr", "fri" : Return _nomiGiorni(4)
                Case "sa", "sat" : Return _nomiGiorni(5)
                Case "su", "sun" : Return _nomiGiorni(6)
                Case Else : Return ""
            End Select
        Catch
            Return ""
        End Try
    End Function

    Private Function FasceLeggibili(parte As String) As String
        Try
            Dim p As String = parte.Trim()
            If p.Length = 0 Then Return ""
            Dim minuscolo As String = p.ToLowerInvariant()
            If minuscolo = "off" OrElse minuscolo = "closed" Then Return "chiuso"

            Dim parti As New List(Of String)
            For Each intervalloGrezzo In p.Split(","c)
                Dim intervallo As String = intervalloGrezzo.Trim()
                If intervallo.Length = 0 Then Continue For
                Dim estremi = intervallo.Split("-"c)
                If estremi.Length = 2 Then
                    parti.Add("dalle " & estremi(0).Trim() & " alle " & estremi(1).Trim())
                Else
                    parti.Add(intervallo)
                End If
            Next

            If parti.Count = 0 Then Return ""
            Return String.Join(" e ", parti)
        Catch ex As Exception
            ScriviLog("CLSLuoghi.FasceLeggibili: " & ex.Message)
            Return parte
        End Try
    End Function

    Private Function ValoreTag(tag As JToken, nome As String) As String
        Try
            If tag Is Nothing Then Return ""
            Dim v = tag(nome)
            If v Is Nothing Then Return ""
            Return v.ToString().Trim()
        Catch
            Return ""
        End Try
    End Function

    Private Function ParseCoordinata(valore As String) As Double
        Try
            If String.IsNullOrWhiteSpace(valore) Then Return 0
            Dim s As String = valore.Trim().Replace(",", ".")
            Dim r As Double = 0
            If Double.TryParse(s, NumberStyles.Any, CultureInfo.InvariantCulture, r) Then Return r
            Return 0
        Catch
            Return 0
        End Try
    End Function

    Private Function Normalizza(testo As String) As String
        Try
            If String.IsNullOrWhiteSpace(testo) Then Return ""
            Dim s As String = testo.Trim().ToUpperInvariant()
            s = s.Replace("À", "A").Replace("È", "E").Replace("É", "E").Replace("Ì", "I").Replace("Ò", "O").Replace("Ù", "U")
            s = s.Replace("'", " ").Replace("`", " ").Replace("-", " ")
            While s.Contains("  ")
                s = s.Replace("  ", " ")
            End While
            Return s.Trim()
        Catch ex As Exception
            ScriviLog("CLSLuoghi.Normalizza: " & ex.Message)
            Return If(testo, "").ToUpperInvariant()
        End Try
    End Function

    Private Sub Segnala(avanzamento As Avanzamento, testo As String)
        Try
            If avanzamento IsNot Nothing Then avanzamento(testo)
        Catch
        End Try
    End Sub

    Private Sub ScriviLog(messaggio As String)
        Try
            SyncLock _lockLog
                Try
                    If String.IsNullOrWhiteSpace(_percorsoFileLog) Then Return
                    Dim cartella As String = Path.GetDirectoryName(_percorsoFileLog)
                    If Not String.IsNullOrWhiteSpace(cartella) AndAlso Not Directory.Exists(cartella) Then
                        Directory.CreateDirectory(cartella)
                    End If
                    File.AppendAllText(_percorsoFileLog, DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") & " - " & messaggio & Environment.NewLine, Encoding.UTF8)
                Catch
                End Try
            End SyncLock
        Catch
        End Try
    End Sub

#End Region

End Class