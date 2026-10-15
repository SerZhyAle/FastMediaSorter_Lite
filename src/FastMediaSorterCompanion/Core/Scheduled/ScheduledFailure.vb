Option Explicit On
Option Strict On
Option Infer On
Option Compare Binary

Imports System.IO

' How a failure is RECORDED by a scheduled operation (ticket 222, APP-BEHAVIOUR rule 6).
'
' No exception's text is ever stored for the user to read - the text of a system exception is a
' sentence in the machine's language about a call the user never made, and it travels: a Message put in
' a record is shown by a history window in another. What is stored is a CODE ("locked", "denied",
' "changed") and the file it was about, as "code|relative path" ("|" cannot be in a Windows file name);
' the sentence is composed at display time from the code, in the language of that moment
' (ScheduledText). The full exception goes to the log, which is what "send the log" ships.
Friend Module ScheduledFailure

    ''' <summary>The code of one file's failure. Every code has a sentence in ScheduledText.</summary>
    Friend Function CodeOf(ex As Exception) As String
        If TypeOf ex Is SourceChangedException Then Return "changed"
        Dim transfer As ScheduledTransferException = TryCast(ex, ScheduledTransferException)
        If transfer IsNot Nothing Then Return transfer.FailureCode
        If TypeOf ex Is InvalidDataException Then Return "mismatch"
        If IsDiskFull(ex) Then Return "disk-full"
        Select Case PathFailure.Classify(ex)
            Case PathFailure.PathFailureKind.Missing : Return "missing"
            Case PathFailure.PathFailureKind.Denied : Return "denied"
            Case PathFailure.PathFailureKind.Transport : Return "transport"
            Case PathFailure.PathFailureKind.Invalid : Return "invalid"
            Case PathFailure.PathFailureKind.Exists : Return "exists"
            Case PathFailure.PathFailureKind.Locked : Return "locked"
            Case PathFailure.PathFailureKind.OutOfMemory : Return "memory"
            Case Else : Return "failed"
        End Select
    End Function

    ''' <summary>The code of a failure the sides did NOT confirm as a lost link: it was about the file, and a
    ''' transport-looking one is just "could not be processed".</summary>
    Friend Function PerFileCode(ex As Exception) As String
        Dim c As String = CodeOf(ex)
        Return If(c = "transport" OrElse c = "disk-full", "failed", c)
    End Function

    ''' <summary>"code|relative path" - the form the first error of a run is kept in.</summary>
    Friend Function Coded(failureCode As String, relativePath As String) As String
        Return failureCode & "|" & If(relativePath, "")
    End Function

    ''' <summary>A failure that is about the destination or the transport, not about one file: nothing
    ''' after it can succeed either, so the run stops as a wall.</summary>
    Friend Function IsWall(ex As Exception) As Boolean
        Dim c As String = CodeOf(ex)
        Return c = "transport" OrElse c = "disk-full"
    End Function

    Private Function IsDiskFull(ex As Exception) As Boolean
        If Not TypeOf ex Is IOException Then Return False
        ' ERROR_HANDLE_DISK_FULL (0x27) and ERROR_DISK_FULL (0x70), as the HRESULT says them.
        Dim code As Integer = ex.HResult And &HFFFF
        Return code = &H27 OrElse code = &H70
    End Function

End Module
