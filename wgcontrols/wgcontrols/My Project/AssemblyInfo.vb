Imports System
Imports System.Reflection
Imports System.Runtime.InteropServices

' Le informazioni generali relative a un assembly sono controllate dal seguente 
' set di attributi. Modificare i valori di questi attributi per modificare le informazioni
' associate a un assembly.

' Controllare i valori degli attributi degli assembly

<Assembly: AssemblyTitle("wgcontrols")>
<Assembly: AssemblyDescription("")>
<Assembly: AssemblyCompany("")>
<Assembly: AssemblyProduct("wgcontrols")>
<Assembly: AssemblyCopyright("Copyright ©  2023")>
<Assembly: AssemblyTrademark("")>

<Assembly: ComVisible(False)>

'' Le informazioni sulla versione di un assembly sono costituite dai seguenti quattro valori:
'
'      Versione principale
'      Versione secondaria
'      Numero di build
'      Revisione
'
' È possibile specificare tutti i valori oppure impostare valori predefiniti per i numeri relativi alla revisione e alla build
' usando l'asterisco '*' come illustrato di seguito:

<Assembly: AssemblyVersion("1.0.0.0")>
' Le librerie nate dal nucleo (musica e registratore) usano anche le parti interne del nucleo.
<Assembly: System.Runtime.CompilerServices.InternalsVisibleTo("WGMusica")>
<Assembly: System.Runtime.CompilerServices.InternalsVisibleTo("WGRegistratore")>
<Assembly: System.Runtime.CompilerServices.InternalsVisibleTo("WGVideoEditor")>
<Assembly: AssemblyFileVersion("1.0.0.0")>
