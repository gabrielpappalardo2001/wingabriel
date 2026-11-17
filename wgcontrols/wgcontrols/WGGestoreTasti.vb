Imports System

''' <summary>Chi riceve i tasti direttamente dal gancio della tastiera (prima della coda degli eventi).</summary>
Public Interface IGestoreTastiMusicali
    ''' <summary>Vero se il tasto è stato usato e non deve arrivare a nessun altro.</summary>
    Function GestisciTasto(vkCode As Integer, scanCode As Integer, premuto As Boolean, shift As Boolean, modificatori As Boolean) As Boolean
End Interface
