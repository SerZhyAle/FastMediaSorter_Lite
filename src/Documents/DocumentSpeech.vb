#If Not NETFRAMEWORK Then
Option Explicit On
Option Strict On
Option Infer On
Option Compare Binary

Imports System
Imports System.Reflection
Imports System.Collections.Generic
Imports System.Runtime.InteropServices

' Installed Windows SAPI voices only; no process, cloud service or runtime download.
Friend NotInheritable Class DocumentSpeech
    Implements IDisposable
    Private voice As Object

    Public Shared Function InstalledVoices() As String()
        Dim result As New List(Of String)()
        Dim probe As Object = Nothing
        Dim tokens As Object = Nothing
        Try
            Dim kind = Type.GetTypeFromProgID("SAPI.SpVoice", throwOnError:=False)
            If kind Is Nothing Then Return result.ToArray()
            probe = Activator.CreateInstance(kind)
            tokens = Invoke(probe, "GetVoices", BindingFlags.InvokeMethod, {"", ""})
            Dim count = CInt(Invoke(tokens, "Count", BindingFlags.GetProperty, Nothing))
            For index = 0 To Math.Min(100, count) - 1
                Dim token = Invoke(tokens, "Item", BindingFlags.InvokeMethod, {index})
                Try
                    result.Add(CStr(Invoke(token, "GetAttribute", BindingFlags.InvokeMethod, {"Name"})))
                Finally
                    If Marshal.IsComObject(token) Then Marshal.FinalReleaseComObject(token)
                End Try
            Next
        Catch ex As Exception
            result.Clear()
        Finally
            If tokens IsNot Nothing AndAlso Marshal.IsComObject(tokens) Then Marshal.FinalReleaseComObject(tokens)
            If probe IsNot Nothing AndAlso Marshal.IsComObject(probe) Then Marshal.FinalReleaseComObject(probe)
        End Try
        Return result.ToArray()
    End Function

    Public Sub Speak(text As String, voiceName As String, rate As Double)
        If String.IsNullOrWhiteSpace(text) Then Return
        If voice Is Nothing Then
            Dim kind = Type.GetTypeFromProgID("SAPI.SpVoice", throwOnError:=True)
            voice = Activator.CreateInstance(kind)
        End If
        [Stop]()
        If Not String.IsNullOrEmpty(voiceName) Then
            Dim tokens = Invoke(voice, "GetVoices", BindingFlags.InvokeMethod, {"Name=" & voiceName, ""})
            Try
                Dim count = CInt(Invoke(tokens, "Count", BindingFlags.GetProperty, Nothing))
                If count = 0 Then Throw New InvalidOperationException("Installed voice is unavailable.")
                Dim selected = Invoke(tokens, "Item", BindingFlags.InvokeMethod, {0})
                Try
                    Invoke(voice, "Voice", BindingFlags.SetProperty, {selected})
                Finally
                    If Marshal.IsComObject(selected) Then Marshal.FinalReleaseComObject(selected)
                End Try
            Finally
                If Marshal.IsComObject(tokens) Then Marshal.FinalReleaseComObject(tokens)
            End Try
        End If
        Dim sapiRate = CInt(Math.Log(Math.Max(0.5, Math.Min(2, rate)), 2) * 5)
        Invoke(voice, "Rate", BindingFlags.SetProperty, {sapiRate})
        ' Async + purge; SVSFIsNotXML prevents document text from becoming speech markup.
        Invoke(voice, "Speak", BindingFlags.InvokeMethod, {text, 1 Or 2 Or 16})
    End Sub

    Private Shared Function Invoke(target As Object, name As String, flags As BindingFlags, args As Object()) As Object
        Return target.GetType().InvokeMember(name, flags, Nothing, target, args, Globalization.CultureInfo.InvariantCulture)
    End Function

    Public Sub [Stop]()
        If voice Is Nothing Then Return
        Invoke(voice, "Speak", BindingFlags.InvokeMethod, {"", 1 Or 2 Or 16})
    End Sub

    Public Sub Dispose() Implements IDisposable.Dispose
        If voice Is Nothing Then Return
        Try
            [Stop]()
        Finally
            If Marshal.IsComObject(voice) Then Marshal.FinalReleaseComObject(voice)
            voice = Nothing
        End Try
    End Sub
End Class
#End If
