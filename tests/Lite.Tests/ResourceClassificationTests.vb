#If Not NETFRAMEWORK Then
Option Strict On

Imports System.IO
Imports System.Linq
Imports System.Net.Sockets
Imports Xunit

''' <summary>
''' 101 response section 15 item 5, the S4033 row: the Android client classified a failure by matching the text of
''' its message and classed "connection refused" as a timeout; Windows must classify on typed exceptions only. The
''' classes decide what happens next (a transport failure is retried, an auth or key failure never is), so a
''' wrong class is a behaviour, not a label.
''' </summary>
Public Class ResourceClassificationTests

    Private Shared Function RepoFile(ParamArray parts As String()) As String
        Dim dir As New DirectoryInfo(AppContext.BaseDirectory)
        While dir IsNot Nothing
            If File.Exists(Path.Combine(dir.FullName, "FastMediaSorter.sln")) Then Return Path.Combine({dir.FullName}.Concat(parts).ToArray())
            dir = dir.Parent
        End While
        Throw New InvalidOperationException("Repository root not found.")
    End Function

    ''' <summary>The text of one function, comments removed, from its signature to its End.</summary>
    Private Shared Function BodyOf(sourcePath As String, signature As String) As String
        Dim lines As String() = File.ReadAllText(sourcePath).Split({vbLf}, StringSplitOptions.None).Select(Function(l) l.TrimEnd(ChrW(13))).ToArray()
        Dim start As Integer = Array.FindIndex(lines, Function(l) l.Contains(signature))
        Assert.True(start >= 0, signature & " not found in " & sourcePath)
        Dim body As New System.Text.StringBuilder()
        For i As Integer = start To lines.Length - 1
            If Not lines(i).TrimStart().StartsWith("'", StringComparison.Ordinal) Then body.AppendLine(lines(i))
            If i > start AndAlso lines(i).TrimStart().StartsWith("End Function", StringComparison.Ordinal) Then Exit For
        Next
        Return body.ToString()
    End Function

    <Fact>
    Public Sub No_classifier_reads_the_text_of_a_message()
        For Each pair As Tuple(Of String, String) In {
                Tuple.Create(RepoFile("src", "Resources", "SftpProvider.vb"), "Private Shared Function Classify("),
                Tuple.Create(RepoFile("src", "Resources", "ResourceFailure.vb"), "Friend Shared Function FromException(")}
            Dim body As String = BodyOf(pair.Item1, pair.Item2)
            For Each banned As String In {".Message", "ToString()", ".Contains(", ".IndexOf(", "StartsWith(", "EndsWith(", "Like ", "Regex"}
                Assert.False(body.Contains(banned), pair.Item2 & " must classify on the exception's type, not its text, but uses '" & banned & "'")
            Next
        Next
    End Sub

    <Theory>
    <InlineData(10061)> ' connection refused: the host answered, nothing listens - a transport failure, never a timeout
    <InlineData(10060)> ' connection timed out
    <InlineData(10054)> ' reset by peer
    Public Sub A_socket_failure_is_a_transport_failure_whatever_its_text_says(code As Integer)
        Dim failure As ResourceFailure = ResourceFailure.FromException(New SocketException(code))
        Assert.Equal(ResourceFailureKind.Transport, failure.Kind)
        Assert.True(failure.IsRetryable)
    End Sub

    <Fact>
    Public Sub Words_in_a_message_never_change_the_class()
        ' The text of an unrelated exception that SAYS "authentication", "timed out" or "host key" is still local I/O.
        For Each text As String In {"Authentication failed", "Operation timed out", "Host key mismatch", "Permission denied", "No such file"}
            Dim failure As ResourceFailure = ResourceFailure.FromException(New IOException(text))
            Assert.Equal(ResourceFailureKind.LocalIO, failure.Kind)
            Assert.False(failure.IsRetryable)
        Next
        Assert.Equal(ResourceFailureKind.Timeout, ResourceFailure.FromException(New TimeoutException("anything")).Kind)
        Assert.Equal(ResourceFailureKind.Cancelled, ResourceFailure.FromException(New OperationCanceledException("a timeout")).Kind)
    End Sub

    <Fact>
    Public Sub An_auth_a_key_and_a_status_failure_are_never_retryable_and_a_transport_one_is()
        Assert.False(New ResourceFailure(ResourceFailureKind.Auth).IsRetryable)
        Assert.False(New ResourceFailure(ResourceFailureKind.KeyMismatch).IsRetryable)
        Assert.False(New ResourceFailure(ResourceFailureKind.SftpStatus, 3, "permission denied").IsRetryable)
        Assert.True(New ResourceFailure(ResourceFailureKind.Transport).IsRetryable)
    End Sub

End Class
#End If
