Imports System
Imports System.Collections.Generic
Imports System.IO
Imports System.Linq
Imports System.Speech.AudioFormat
Imports System.Speech.Synthesis

''' <summary>
''' Le voci di Windows che parlano dentro il gioco (il conduttore della radio, la gente per strada, gli amici al telefono): il testo si
''' registra in memoria e si suona nel mixer del gioco, così ha la sua posizione e il suo volume e non si confonde con la sintesi di
''' WinGabriel. Si preferiscono le voci italiane; per variare si cambia voce, velocità e, al telefono, si filtra il suono.
''' </summary>
Public Module VociCitta
    Private _voci As List(Of VoiceInfo)
    Private ReadOnly _blocco As New Object()

    Private Function Italiane() As List(Of VoiceInfo)
        Try
            SyncLock _blocco
                If _voci IsNot Nothing Then Return _voci
                Using s As New SpeechSynthesizer()
                    Dim tutte = s.GetInstalledVoices().Where(Function(v) v.Enabled).Select(Function(v) v.VoiceInfo).ToList()
                    _voci = tutte.Where(Function(v) v.Culture.TwoLetterISOLanguageName = "it").ToList()
                    If _voci.Count = 0 Then _voci = tutte
                End Using
                Return _voci
            End SyncLock
        Catch ex As Exception
            RegistroErrori.Registra("VociCitta.Italiane", ex)
            Return New List(Of VoiceInfo)()
        End Try
    End Function

    ''' <summary>
    ''' La voce in memoria, a 44100 Hz mono (Nothing se non riesce). donna: voce femminile se c'è; velocita: da -3 a 4; variante: quale voce
    ''' tra quelle dello stesso tipo; telefono: suono stretto, come in cornetta.
    ''' </summary>
    Public Function Sintetizza(testo As String, donna As Boolean, Optional velocita As Integer = 1, Optional variante As Integer = 0, Optional telefono As Boolean = False) As Single()
        Try
            If String.IsNullOrWhiteSpace(testo) Then Return Nothing
            Using s As New SpeechSynthesizer()
                Dim voci As List(Of VoiceInfo) = Italiane()
                Dim adatte = voci.Where(Function(v) v.Gender = If(donna, VoiceGender.Female, VoiceGender.Male)).ToList()
                If adatte.Count = 0 Then adatte = voci
                If adatte.Count > 0 Then s.SelectVoice(adatte(Math.Abs(variante) Mod adatte.Count).Name)
                s.Rate = Math.Max(-10, Math.Min(10, velocita))
                Using ms As New MemoryStream()
                    s.SetOutputToAudioStream(ms, New SpeechAudioFormatInfo(22050, AudioBitsPerSample.Sixteen, AudioChannel.Mono))
                    s.Speak(testo)
                    s.SetOutputToNull()
                    Dim b As Byte() = ms.ToArray()
                    Dim n As Integer = b.Length \ 2
                    If n < 2 Then Return Nothing
                    Dim r(n * 2 - 1) As Single
                    Dim basso As Single = 0, alto As Single = 0
                    For i As Integer = 0 To n - 1
                        Dim v As Single = BitConverter.ToInt16(b, 2 * i) / 32768.0F
                        Dim prossimo As Single = If(i + 1 < n, BitConverter.ToInt16(b, 2 * i + 2) / 32768.0F, v)
                        Dim a1 As Single = v * 0.9F
                        Dim a2 As Single = (v + prossimo) * 0.45F
                        If telefono Then
                            ' in cornetta: niente bassi e niente acuti, un po' saturato
                            basso += (a1 - basso) * 0.06F
                            a1 -= basso
                            alto += (a1 - alto) * 0.45F
                            a1 = CSng(Math.Tanh(alto * 2.2) * 0.6)
                            basso += (a2 - basso) * 0.06F
                            a2 -= basso
                            alto += (a2 - alto) * 0.45F
                            a2 = CSng(Math.Tanh(alto * 2.2) * 0.6)
                        End If
                        r(2 * i) = a1
                        r(2 * i + 1) = a2
                    Next
                    Return r
                End Using
            End Using
        Catch ex As Exception
            RegistroErrori.Registra("VociCitta.Sintetizza", ex)
            Return Nothing
        End Try
    End Function
End Module
