' ============================================================================================
' LibreriaEffetti - gli effetti sonori e visivi che l'editor video ha già dentro
' ============================================================================================
'
' Gli effetti non sono file: sono scritti qui, come filtri di ffmpeg. Quelli sonori si generano da soli (suoni di sintesi) e si
' mescolano all'audio del video senza coprirlo; quelli visivi si applicano solo al tratto scelto e lasciano il resto com'è.
'
' Nei modelli dei filtri visivi: {EN} diventa la condizione "il tempo è nel tratto" (between(t,inizio,fine)), {S} l'inizio,
' {E} la fine e {D} la durata, in secondi.
' ============================================================================================
Imports System
Imports wgcontrols
Imports System.Collections.Generic
Imports System.Globalization
Imports System.Linq

Public Enum TipoEffetto
    Visivo
    Sonoro
End Enum

Public NotInheritable Class DefinizioneEffetto
    Public Id As String
    Public Nome As String
    Public Tipo As TipoEffetto
    ''' <summary>Per i visivi il modello del filtro; per i sonori la sorgente ffmpeg che genera il suono.</summary>
    Public Modello As String
    ''' <summary>La durata del suono, o quella proposta per l'effetto visivo.</summary>
    Public DurataPredefinita As Double
    ''' <summary>Vero se l'effetto ha senso solo con una durata scelta (i visivi); i suoni durano quanto sono lunghi.</summary>
    Public ConDurata As Boolean
    ''' <summary>Vero se l'effetto mostra un testo scritto dall'utente: non compare tra gli effetti, ha una voce tutta sua.</summary>
    Public ConTesto As Boolean
End Class

Public NotInheritable Class LibreriaEffetti

    Private Shared ReadOnly _elenco As New List(Of DefinizioneEffetto)()

    Private Sub New()
    End Sub

    Shared Sub New()
        ' ---- suoni ---------------------------------------------------------------------------------------
        Suono("bip", "Bip", "sine=frequency=1000:duration=0.3:sample_rate=48000,afade=t=out:st=0.2:d=0.1", 0.3)
        Suono("campanello", "Campanello", "aevalsrc='0.5*sin(2*PI*1568*t)*exp(-3*t)+0.3*sin(2*PI*2349*t)*exp(-4*t)':d=1.5:s=48000", 1.5)
        Suono("gong", "Gong", "aevalsrc='0.6*sin(2*PI*110*t)*exp(-1.1*t)+0.3*sin(2*PI*221*t)*exp(-1.4*t)+0.15*sin(2*PI*331*t)*exp(-1.8*t)':d=3.5:s=48000", 3.5)
        Suono("colpo", "Colpo", "aevalsrc='0.9*sin(2*PI*(60+120*exp(-30*t))*t)*exp(-8*t)':d=0.6:s=48000", 0.6)
        Suono("tamburo", "Rullo di tamburo", "anoisesrc=d=2:c=brown:a=0.8:r=48000,tremolo=f=18:d=0.9,afade=t=in:st=0:d=1.5,afade=t=out:st=1.7:d=0.3", 2.0)
        Suono("swoosh", "Passaggio veloce", "anoisesrc=d=0.8:c=pink:a=0.6:r=48000,highpass=f=400,afade=t=in:st=0:d=0.4,afade=t=out:st=0.4:d=0.4", 0.8)
        Suono("applauso", "Applauso", "anoisesrc=d=3.5:c=white:a=0.5:r=48000,bandpass=f=3000:width_type=h:w=2500,tremolo=f=11:d=0.8,afade=t=in:st=0:d=0.3,afade=t=out:st=2.5:d=1", 3.5)
        Suono("pioggia", "Pioggia", "anoisesrc=d=6:c=white:a=0.3:r=48000,highpass=f=1500,lowpass=f=9000,afade=t=in:st=0:d=1,afade=t=out:st=5:d=1", 6.0)
        Suono("vento", "Vento", "anoisesrc=d=5:c=pink:a=0.5:r=48000,lowpass=f=700,tremolo=f=0.4:d=0.6,afade=t=in:st=0:d=1.2,afade=t=out:st=3.8:d=1.2", 5.0)
        Suono("telefono", "Squillo del telefono", "aevalsrc='0.4*(sin(2*PI*440*t)+sin(2*PI*480*t))*lt(mod(t,2),0.8)':d=4:s=48000", 4.0)
        Suono("sirena", "Sirena", "aevalsrc='0.4*sin(2*PI*(700+300*sin(2*PI*0.7*t))*t)':d=3:s=48000,afade=t=out:st=2.5:d=0.5", 3.0)
        Suono("laser", "Raggio laser", "aevalsrc='0.5*sin(2*PI*(2400*exp(-6*t)+300)*t)*exp(-3*t)':d=0.7:s=48000", 0.7)
        Suono("tic", "Tic dell'orologio", "aevalsrc='0.8*sin(2*PI*2200*t)*exp(-90*t)':d=0.15:s=48000", 0.15)
        Suono("moneta", "Moneta", "aevalsrc='0.5*sin(2*PI*988*t)*lt(t,0.08)+0.5*sin(2*PI*1319*t)*gte(t,0.08)*exp(-6*(t-0.08))':d=0.8:s=48000", 0.8)
        Suono("notifica", "Notifica a due note", "aevalsrc='0.4*sin(2*PI*880*t)*exp(-8*t)+0.4*sin(2*PI*1175*t)*gte(t,0.15)*exp(-8*(t-0.15))':d=0.8:s=48000", 0.8)
        Suono("successo", "Successo, tre note che salgono", "aevalsrc='0.4*sin(2*PI*523*t)*lt(t,0.15)+0.4*sin(2*PI*659*t)*gte(t,0.15)*lt(t,0.3)+0.4*sin(2*PI*784*t)*gte(t,0.3)*exp(-5*(t-0.3))':d=1.0:s=48000", 1.0)
        Suono("errore", "Errore, ronzio grave", "aevalsrc='0.35*(sin(2*PI*110*t)+sin(2*PI*117*t))*lt(t,0.5)':d=0.6:s=48000", 0.6)
        Suono("boing", "Molla", "aevalsrc='0.6*sin(2*PI*(300+250*sin(2*PI*8*t))*t)*exp(-3*t)':d=0.9:s=48000", 0.9)
        Suono("ping", "Ping del sonar", "aevalsrc='0.6*sin(2*PI*1000*t)*exp(-4*t)':d=1.5:s=48000", 1.5)
        Suono("dingdong", "Din don", "aevalsrc='0.5*sin(2*PI*659*t)*exp(-3*t)+0.5*sin(2*PI*523*t)*gte(t,0.6)*exp(-3*(t-0.6))':d=2.0:s=48000", 2.0)
        Suono("fischio", "Fischio", "aevalsrc='0.4*sin(2*PI*(1800+300*sin(2*PI*5*t))*t)*lt(t,0.6)':d=0.7:s=48000", 0.7)
        Suono("ticchettio", "Ticchettio per tre secondi", "aevalsrc='0.7*sin(2*PI*2200*t)*exp(-90*mod(t,1))':d=3:s=48000", 3.0)
        Suono("battito", "Battito del cuore", "aevalsrc='0.9*sin(2*PI*55*t)*(exp(-20*mod(t,0.9))+0.6*exp(-20*mod(t+0.65,0.9)))':d=4:s=48000", 4.0)
        Suono("tuono", "Tuono", "anoisesrc=d=4:c=brown:a=0.9:r=48000,lowpass=f=300,afade=t=in:st=0:d=0.3,afade=t=out:st=1.5:d=2.5", 4.0)
        Suono("mare", "Onde del mare", "anoisesrc=d=8:c=pink:a=0.5:r=48000,lowpass=f=1200,tremolo=f=0.18:d=0.7,afade=t=in:st=0:d=1.5,afade=t=out:st=6:d=2", 8.0)
        Suono("fuoco", "Fuoco che crepita", "anoisesrc=d=6:c=white:a=0.25:r=48000,highpass=f=2500,tremolo=f=23:d=0.9,afade=t=in:st=0:d=1,afade=t=out:st=5:d=1", 6.0)
        Suono("pugno", "Pugno", "aevalsrc='0.9*sin(2*PI*(90+100*exp(-40*t))*t)*exp(-14*t)':d=0.4:s=48000", 0.4)
        Suono("whooshinverso", "Passaggio che cresce", "anoisesrc=d=1.2:c=pink:a=0.6:r=48000,highpass=f=300,afade=t=in:st=0:d=1.0,afade=t=out:st=1.0:d=0.2", 1.2)
        ' ---- effetti visivi -------------------------------------------------------------------------------
        Visivo("bianconero", "Bianco e nero", "hue=s=0:enable='{EN}'", 3)
        Visivo("seppia", "Seppia", "colorchannelmixer=.393:.769:.189:0:.349:.686:.168:0:.272:.534:.131:enable='{EN}'", 3)
        Visivo("negativo", "Negativo", "negate=enable='{EN}'", 2)
        Visivo("sfocatura", "Sfocatura", "gblur=sigma=10:enable='{EN}'", 3)
        Visivo("nitidezza", "Più nitido", "unsharp=5:5:1.5:5:5:0.0:enable='{EN}'", 5)
        Visivo("specchio", "Immagine rovesciata come in uno specchio", "hflip=enable='{EN}'", 3)
        Visivo("luminoso", "Più luminoso", "eq=brightness=0.15:contrast=1.1:enable='{EN}'", 5)
        Visivo("scuro", "Più scuro", "eq=brightness=-0.15:enable='{EN}'", 5)
        Visivo("contrasto", "Più contrasto", "eq=contrast=1.4:enable='{EN}'", 5)
        Visivo("vivace", "Colori vivaci", "eq=saturation=1.7:enable='{EN}'", 5)
        Visivo("caldo", "Colori caldi", "colorbalance=rs=0.2:rm=0.15:bs=-0.2:bm=-0.15:enable='{EN}'", 5)
        Visivo("freddo", "Colori freddi", "colorbalance=rs=-0.2:rm=-0.15:bs=0.2:bm=0.15:enable='{EN}'", 5)
        Visivo("vecchiofilm", "Vecchio film", "hue=s=0.25:enable='{EN}',noise=alls=22:allf=t:enable='{EN}'", 5)
        Visivo("disturbo", "Disturbo di una televisione", "noise=alls=45:allf=t+u:enable='{EN}'", 2)
        Visivo("bordi", "Solo i contorni", "edgedetect=low=0.1:high=0.3:enable='{EN}'", 3)
        Visivo("lampeggio", "Lampeggio", "eq=brightness=0.6:enable='({EN})*lt(mod(t,0.25),0.06)'", 2)
        Visivo("flash", "Lampo bianco", "eq=brightness=0.8:enable='between(t,{S},{S}+0.12)'", 0.12, False)
        Visivo("dissolvenzain", "Dissolvenza dal nero", "fade=t=in:st={S}:d={D}", 2)
        Visivo("dissolvenzaout", "Dissolvenza verso il nero", "fade=t=out:st={S}:d={D}", 2)
        Visivo("dissolvenzabiancain", "Dissolvenza dal bianco", "fade=t=in:st={S}:d={D}:color=white", 2)
        Visivo("dissolvenzabiancaout", "Dissolvenza verso il bianco", "fade=t=out:st={S}:d={D}:color=white", 2)
        Visivo("sfocaturaleggera", "Sfocatura leggera", "gblur=sigma=3:enable='{EN}'", 3)
        Visivo("sfocaturaforte", "Sfocatura molto forte", "gblur=sigma=30:enable='{EN}'", 3)
        Visivo("grana", "Grana della pellicola", "noise=alls=14:allf=t:enable='{EN}'", 5)
        Visivo("capovolto", "Immagine capovolta", "vflip=enable='{EN}'", 3)
        Visivo("ruotato", "Immagine ruotata di mezzo giro", "hflip=enable='{EN}',vflip=enable='{EN}'", 3)
        Visivo("pastello", "Colori pastello", "eq=saturation=0.6:brightness=0.08:enable='{EN}'", 5)
        Visivo("sbiadito", "Colori sbiaditi", "eq=saturation=0.35:contrast=0.9:enable='{EN}'", 5)
        Visivo("notte", "Scena di notte", "colorbalance=bs=0.3:bm=0.2:enable='{EN}',eq=brightness=-0.15:saturation=0.8:enable='{EN}'", 5)
        Visivo("tramonto", "Luce del tramonto", "colorbalance=rs=0.25:rm=0.2:bs=-0.2:bm=-0.15:enable='{EN}',eq=saturation=1.2:enable='{EN}'", 5)
        Visivo("verde", "Tinta verde", "colorchannelmixer=rr=0.7:gg=1.1:bb=0.7:enable='{EN}'", 4)
        Visivo("rosso", "Tinta rossa", "colorchannelmixer=rr=1.2:gg=0.6:bb=0.6:enable='{EN}'", 4)
        Visivo("blu", "Tinta blu", "colorchannelmixer=rr=0.6:gg=0.7:bb=1.2:enable='{EN}'", 4)
        Visivo("contrastoalto", "Bianco e nero molto contrastato", "hue=s=0:enable='{EN}',eq=contrast=1.8:enable='{EN}'", 4)
        Visivo("vintage", "Aspetto d'epoca", "curves=preset=vintage:enable='{EN}'", 5)
        Visivo("processoincrociato", "Colori di un processo incrociato", "curves=preset=cross_process:enable='{EN}'", 5)
        Visivo("contrastoforte", "Contrasto forte delle luci", "curves=preset=strong_contrast:enable='{EN}'", 5)
        Visivo("colorinegativi", "Colori invertiti", "curves=preset=color_negative:enable='{EN}'", 3)
        Visivo("solarizza", "Solarizzazione", "lutrgb=r='if(gt(val,128),255-val,val)':g='if(gt(val,128),255-val,val)':b='if(gt(val,128),255-val,val)':enable='{EN}'", 3)
        Visivo("pulsazione", "Luce che pulsa", "eq=brightness='0.12*sin(2*PI*2*t)':eval=frame:enable='{EN}'", 4)
        Visivo("barrecinema", "Barre nere da cinema", "drawbox=x=0:y=0:w=iw:h=ih*0.12:color=black:t=fill:enable='{EN}',drawbox=x=0:y=ih*0.88:w=iw:h=ih*0.12:color=black:t=fill:enable='{EN}'", 10)
        Visivo("cornice", "Cornice nera", "drawbox=x=0:y=0:w=iw:h=ih:color=black:t=ih*0.04:enable='{EN}'", 10)
        Visivo("vignettatura", "Bordi scuri", "vignette=angle=PI/4:enable='{EN}'", 10)
        Visivo("testo", "Testo scritto sul video", "drawtext=fontfile='C\:/Windows/Fonts/arial.ttf':text='{T}':fontcolor=white:fontsize=h/12:box=1:boxcolor=black@0.55:boxborderw=14:x=(w-text_w)/2:y=h*0.82:enable='{EN}'", 4, True, True)
    End Sub

    Private Shared Sub Suono(id As String, nome As String, sorgente As String, durata As Double)
        _elenco.Add(New DefinizioneEffetto() With {.Id = id, .Nome = nome, .Tipo = TipoEffetto.Sonoro, .Modello = sorgente, .DurataPredefinita = durata, .ConDurata = False})
    End Sub

    Private Shared Sub Visivo(id As String, nome As String, modello As String, durata As Double, Optional conDurata As Boolean = True, Optional conTesto As Boolean = False)
        _elenco.Add(New DefinizioneEffetto() With {.Id = id, .Nome = nome, .Tipo = TipoEffetto.Visivo, .Modello = modello, .DurataPredefinita = durata, .ConDurata = conDurata, .ConTesto = conTesto})
    End Sub

    Public Shared Function Elenco(tipo As TipoEffetto) As List(Of DefinizioneEffetto)
        Return _elenco.Where(Function(e) e.Tipo = tipo AndAlso Not e.ConTesto).ToList()
    End Function

    Public Shared Function Trova(id As String) As DefinizioneEffetto
        Return _elenco.FirstOrDefault(Function(e) e.Id = id)
    End Function

    Private Shared Function Num(valore As Double) As String
        Return valore.ToString("0.###", CultureInfo.InvariantCulture)
    End Function

    ''' <summary>Toglie dal testo i caratteri che hanno un significato per ffmpeg.</summary>
    Public Shared Function TestoSicuro(testo As String) As String
        Dim sb As New System.Text.StringBuilder()
        For Each c As Char In If(testo, "")
            If c = "'"c OrElse c = ":"c OrElse c = "\"c OrElse c = "%"c OrElse c = ";"c OrElse c = "["c OrElse c = "]"c OrElse c = ","c OrElse c = vbCr OrElse c = vbLf Then
                sb.Append(" "c)
            Else
                sb.Append(c)
            End If
        Next
        Return sb.ToString().Trim()
    End Function

    ''' <summary>Il filtro visivo pronto, per un tratto che va da inizio a fine (secondi).</summary>
    Public Shared Function FiltroVisivo(effetto As DefinizioneEffetto, inizio As Double, fine As Double, Optional testo As String = "") As String
        Return effetto.Modello.Replace("{T}", TestoSicuro(testo)).Replace("{EN}", "between(t," & Num(inizio) & "," & Num(fine) & ")").
            Replace("{S}", Num(inizio)).Replace("{E}", Num(fine)).Replace("{D}", Num(Math.Max(0.05, fine - inizio)))
    End Function

End Class
