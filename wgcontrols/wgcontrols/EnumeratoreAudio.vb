' L'enumeratore dei dispositivi audio di NAudio.
'
' New MMDeviceEnumerator() fa un cast alla classe COM di NAudio. Se prima un'altra parte del
' programma ha creato lo stesso oggetto di Windows (le cuffie, l'uscita audio diretta), il CLR
' restituisce un COM generico e il cast fallisce. Qui si riprova creando l'oggetto per
' interfaccia, che passa da QueryInterface e non dalla classe di NAudio.
Imports System.Reflection
Imports System.Runtime.InteropServices
Imports System.Runtime.Serialization
Imports NAudio.CoreAudioApi

Friend NotInheritable Class EnumeratoreAudio

    Private Shared ReadOnly ClsidEnumeratore As New Guid("BCDE0395-E52F-467C-8E3D-C4579291692E")

    Private Sub New()
    End Sub

    Public Shared Function Crea() As MMDeviceEnumerator
        Try
            Return New MMDeviceEnumerator()
        Catch ex As InvalidCastException
            Return CreaDaInterfaccia()
        Catch ex As Exception
            If ex.Message.IndexOf("MMDeviceEnumeratorComObject", StringComparison.Ordinal) < 0 AndAlso TypeOf ex.InnerException IsNot InvalidCastException Then Throw
            Return CreaDaInterfaccia()
        End Try
    End Function

    ' L'interfaccia di NAudio è Friend: non si può nominare. Si chiede a COM e si infila nel campo privato.
    Private Shared Function CreaDaInterfaccia() As MMDeviceEnumerator
        Dim tipoInterfaccia As Type = GetType(MMDeviceEnumerator).Assembly.GetType("NAudio.CoreAudioApi.Interfaces.IMMDeviceEnumerator", True)
        Dim oggetto As Object = Activator.CreateInstance(Type.GetTypeFromCLSID(ClsidEnumeratore, True))
        Dim sconosciuto As IntPtr = Marshal.GetIUnknownForObject(oggetto)
        Try
            Dim interfaccia As Object = Marshal.GetTypedObjectForIUnknown(sconosciuto, tipoInterfaccia)
            Dim enumeratore As MMDeviceEnumerator = CType(FormatterServices.GetUninitializedObject(GetType(MMDeviceEnumerator)), MMDeviceEnumerator)
            Dim campo As FieldInfo = GetType(MMDeviceEnumerator).GetField("realEnumerator", BindingFlags.Instance Or BindingFlags.NonPublic)
            If campo Is Nothing Then Throw New InvalidOperationException("Enumeratore audio non disponibile")
            campo.SetValue(enumeratore, interfaccia)
            Return enumeratore
        Finally
            If sconosciuto <> IntPtr.Zero Then Marshal.Release(sconosciuto)
        End Try
    End Function

End Class
