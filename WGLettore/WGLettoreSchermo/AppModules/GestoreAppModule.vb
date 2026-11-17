Imports System.Collections.Concurrent
Imports UIAutomationClient

' Gestore per il caricamento e il recupero dei moduli applicativi specifici in base al programma attivo.
Friend NotInheritable Class GestoreAppModule

    Private ReadOnly _lettore As WGLettoreSchermo
    Private ReadOnly _moduli As New ConcurrentDictionary(Of String, AppModuleLettore)(StringComparer.Ordinal)

    Public Sub New(lettore As WGLettoreSchermo)
        _lettore = lettore
    End Sub

    Public Function OttieniModulo(pid As UInteger) As AppModuleLettore
        Try
            Dim nome As String = _lettore.NomeEseguibileApp(pid)
            Return OttieniModuloPerNome(nome)
        Catch exAtteso As Exception
            RegistroErrori.Scrivi("GestoreAppModule.OttieniModulo", exAtteso)
            Return OttieniModuloPerNome("")
        End Try
    End Function

    Public Function OttieniModulo(elemento As IUIAutomationElement) As AppModuleLettore
        Try
            If elemento Is Nothing OrElse _lettore.DescrittoreApp Is Nothing Then Return OttieniModuloPerNome("")
            Dim pid As Integer = _lettore.DescrittoreApp.IdProcesso(elemento)
            If pid <= 0 Then Return OttieniModuloPerNome("")
            Return OttieniModulo(CUInt(pid))
        Catch exAtteso As Exception
            RegistroErrori.Scrivi("GestoreAppModule.OttieniModulo", exAtteso)
            Return OttieniModuloPerNome("")
        End Try
    End Function

    Public Function OttieniModuloPerNome(nomeEseguibile As String) As AppModuleLettore
        Dim nome As String = If(nomeEseguibile, "").Trim().ToLowerInvariant()
        Dim modulo As AppModuleLettore = Nothing
        If _moduli.TryGetValue(nome, modulo) Then Return modulo
        modulo = AppModuleLettore.Crea(nome, _lettore)
        _moduli(nome) = modulo
        Return modulo
    End Function

    Public Sub Svuota()
        _moduli.Clear()
    End Sub

End Class
