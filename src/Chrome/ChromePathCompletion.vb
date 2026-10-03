#If Not NETFRAMEWORK Then
Option Explicit On
Option Strict On
Option Infer On
Option Compare Binary
Imports System.IO
Imports System.Threading
Friend NotInheritable Class ChromePathCompletion
    Private Sub New()
    End Sub
    Friend Shared Function Find(typed As String, token As CancellationToken) As String()
        token.ThrowIfCancellationRequested()
        If String.IsNullOrWhiteSpace(typed) Then Return Array.Empty(Of String)()
        Dim result As New List(Of String)()
        Try
            Dim parent As String = Path.GetDirectoryName(typed)
            Dim prefix As String = Path.GetFileName(typed)
            If String.IsNullOrEmpty(parent) Then Return result.ToArray()
            For Each folder As String In Directory.EnumerateDirectories(parent)
                token.ThrowIfCancellationRequested()
                If Path.GetFileName(folder).StartsWith(prefix, StringComparison.OrdinalIgnoreCase) Then
                    result.Add(folder)
                    If result.Count >= ChromeMetrics.CompletionLimit Then Exit For
                End If
            Next
        Catch ex As IOException
        Catch ex As UnauthorizedAccessException
        Catch ex As ArgumentException
        End Try
        Return result.ToArray()
    End Function
End Class
#End If
