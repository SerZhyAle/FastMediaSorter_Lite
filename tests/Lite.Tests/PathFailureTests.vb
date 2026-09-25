Option Strict On

Imports System.ComponentModel
Imports System.IO
Imports System.Reflection
Imports System.Security
Imports System.Threading.Tasks
Imports Xunit

''' <summary>
''' Ф1 of 011_SPECIFICATION_SLOT_HEALTH_AND_HONEST_FAILURES_DOTNET10.md §4 - the acceptance the
''' spec asks for in points 4 of that phase.
'''
''' What is really under test is a decision the viewer makes several times a second while
''' someone sorts a folder: does this failure mean "throw the file out of the list"? It used
''' to always mean that, so ten seconds of a dropped SMB session removed one file per keypress
''' from a healthy folder and finished by announcing that the folder had nothing readable in
''' it. The classifier is the whole of the fix, and its correctness is entirely a question of
''' exception DERIVATION - which is exactly the sort of thing that reads as right and is not.
''' </summary>
Public Class PathFailureTests

    ' ------------------------------------------------------- the derivation trap ----

    ''' <summary>
    ''' The one that would have shipped as a bug. FileNotFoundException and
    ''' DirectoryNotFoundException both DERIVE from IOException, so a classifier that asks
    ''' "TypeOf ex Is IOException" first calls every missing file a transport failure - and
    ''' then the list never self-cleans, which is the mirror image of the defect being fixed.
    ''' </summary>
    <Fact>
    Public Sub A_missing_file_is_Missing_and_not_Transport()
        Assert.Equal(PathFailureKind.Missing, PathFailure.Classify(New FileNotFoundException("gone")))
        Assert.Equal(PathFailureKind.Missing, PathFailure.Classify(New DirectoryNotFoundException("gone")))

        ' Both really are IOExceptions - the trap is real, not hypothetical.
        Assert.IsAssignableFrom(Of IOException)(New FileNotFoundException())
        Assert.IsAssignableFrom(Of IOException)(New DirectoryNotFoundException())
    End Sub

    ''' <summary>The other half of the same trap, in the opposite direction:
    ''' UnauthorizedAccessException is NOT an IOException, so an "IOException means denied
    ''' or transport" shortcut misses it entirely.</summary>
    <Fact>
    Public Sub Denial_is_recognised_even_though_it_is_not_an_IOException()
        Assert.Equal(PathFailureKind.Denied, PathFailure.Classify(New UnauthorizedAccessException()))
        Assert.Equal(PathFailureKind.Denied, PathFailure.Classify(New SecurityException()))
        ' The mirror of the assertion above cannot be written: the compiler rejects
        ' "TypeOf New UnauthorizedAccessException() Is IOException" outright with BC31430,
        ' which is the proof itself - the two hierarchies do not meet.
    End Sub

    ''' <summary>PathTooLongException is an IOException too, and it is about the path rather
    ''' than about the network.</summary>
    <Fact>
    Public Sub A_path_that_cannot_be_used_is_Invalid()
        Assert.Equal(PathFailureKind.Invalid, PathFailure.Classify(New PathTooLongException()))
        Assert.Equal(PathFailureKind.Invalid, PathFailure.Classify(New ArgumentException("bad path")))
        Assert.Equal(PathFailureKind.Invalid, PathFailure.Classify(New ArgumentNullException("path")))
        Assert.Equal(PathFailureKind.Invalid, PathFailure.Classify(New NotSupportedException()))
    End Sub

    ' ------------------------------------------------------------- the categories ----

    ''' <summary>The category the whole change exists for: an IOException that is none of the
    ''' special cases is the transport, and the transport is never the file's fault.</summary>
    <Fact>
    Public Sub A_dropped_session_is_Transport()
        Assert.Equal(PathFailureKind.Transport, PathFailure.Classify(New IOException("The specified network name is no longer available")))
        Assert.Equal(PathFailureKind.Transport, PathFailure.Classify(New EndOfStreamException()))
        Assert.Equal(PathFailureKind.Transport, PathFailure.Classify(New DriveNotFoundException()))
    End Sub

    <Fact>
    Public Sub Out_of_memory_keeps_its_own_category()
        Assert.Equal(PathFailureKind.OutOfMemory, PathFailure.Classify(New OutOfMemoryException()))
    End Sub

    <Fact>
    Public Sub Nothing_classifies_as_None()
        Assert.Equal(PathFailureKind.None, PathFailure.Classify(Nothing))
    End Sub

    ''' <summary>An exception type nobody has classified stays Unknown - and Unknown drops the
    ''' file, which is today's behaviour. The conservative direction: an unseen failure must
    ''' not silently start keeping unreadable files in the list (§6.4).</summary>
    <Fact>
    Public Sub An_unrecognised_failure_is_Unknown_and_still_about_the_file()
        Assert.Equal(PathFailureKind.Unknown, PathFailure.Classify(New Win32Exception(5)))
        Assert.Equal(PathFailureKind.Unknown, PathFailure.Classify(New InvalidOperationException()))
        Assert.True(PathFailure.IsAboutTheFile(PathFailureKind.Unknown))
    End Sub

    ' ------------------------------------------------------------ the one inversion ----

    ''' <summary>
    ''' The single line the defect turns on. Everything that accuses the file drops it from
    ''' the list; the two that accuse the transport do not.
    ''' </summary>
    <Fact>
    Public Sub Only_transport_denial_and_a_lock_leave_the_list_alone()
        Assert.False(PathFailure.IsAboutTheFile(PathFailureKind.Transport))
        Assert.False(PathFailure.IsAboutTheFile(PathFailureKind.Denied))
        ' Another program holding the file open accuses neither the file nor the list; it
        ' used to be classified as Transport, which kept the file too (audit 047, C-17).
        Assert.False(PathFailure.IsAboutTheFile(PathFailureKind.Locked))
        ' A slow LOCAL decode is not a verdict on the file either (audit 048, A-18).
        Assert.False(PathFailure.IsAboutTheFile(PathFailureKind.SlowDecode))

        Assert.True(PathFailure.IsAboutTheFile(PathFailureKind.Missing))
        Assert.True(PathFailure.IsAboutTheFile(PathFailureKind.Invalid))
        Assert.True(PathFailure.IsAboutTheFile(PathFailureKind.OutOfMemory))
        Assert.True(PathFailure.IsAboutTheFile(PathFailureKind.Content))
        Assert.True(PathFailure.IsAboutTheFile(PathFailureKind.Unknown))
    End Sub

    ''' <summary>Every kind must answer the question - a new member added without a decision
    ''' would otherwise inherit whatever the Select Case falls through to, unnoticed.</summary>
    <Fact>
    Public Sub Every_kind_has_an_answer()
        For Each raw As Object In [Enum].GetValues(GetType(PathFailureKind))
            Dim kind As PathFailureKind = CType(raw, PathFailureKind)
            Dim about As Boolean = PathFailure.IsAboutTheFile(kind)
            Assert.True(about OrElse kind = PathFailureKind.Transport OrElse kind = PathFailureKind.Denied OrElse
                        kind = PathFailureKind.Locked OrElse kind = PathFailureKind.SlowDecode,
                        "Unexpected keep-the-file verdict for " & kind.ToString())
        Next
    End Sub

    ' ------------------------------------------- audit 047: the HResult cases ----

    ''' <summary>C-17: a name taken between the check and the write - MoveFile's own
    ''' refusal, File.Move/File.Copy's ERROR_FILE_EXISTS, CreateDirectory's
    ''' ERROR_ALREADY_EXISTS - read "no connection to the folder" and sent the user to
    ''' check a NAS that was fine.</summary>
    <Theory>
    <InlineData(&H80070050)>
    <InlineData(&H800700B7)>
    Public Sub A_taken_name_is_Exists_not_Transport(hresult As Integer)
        Assert.Equal(PathFailureKind.Exists, PathFailure.Classify(New IOException("exists", hresult)))
    End Sub

    ''' <summary>C-17: the source open in another program (sharing / lock violation).</summary>
    <Theory>
    <InlineData(&H80070020)>
    <InlineData(&H80070021)>
    Public Sub A_sharing_or_lock_violation_is_Locked(hresult As Integer)
        Assert.Equal(PathFailureKind.Locked, PathFailure.Classify(New IOException("in use", hresult)))
    End Sub

    ''' <summary>C-24: enumerating a path that names a FILE answers ERROR_DIRECTORY, and a
    ''' slot set to a file by mistake read "no connection, press again" for ever.</summary>
    <Fact>
    Public Sub A_directory_name_that_is_a_file_is_Invalid()
        Assert.Equal(PathFailureKind.Invalid, PathFailure.Classify(New IOException("The directory name is invalid.", &H8007010B)))
    End Sub

    ''' <summary>The same, from the real call the slot probe makes.</summary>
    <Fact>
    Public Sub Enumerating_a_file_as_a_folder_is_Invalid()
        Dim file_Path As String = Path.GetTempFileName()
        Try
            Dim caught As Exception = Nothing
            Try
                Using walker = Directory.EnumerateFileSystemEntries(file_Path).GetEnumerator()
                    walker.MoveNext()
                End Using
            Catch ex As Exception
                caught = ex
            End Try
            Assert.NotNull(caught)
            Assert.Equal(PathFailureKind.Invalid, PathFailure.Classify(caught))
        Finally
            File.Delete(file_Path)
        End Try
    End Sub

    ''' <summary>A real sharing violation, not a hand-made HResult.</summary>
    <Fact>
    Public Sub Opening_a_file_another_program_holds_is_Locked()
        Dim file_Path As String = Path.GetTempFileName()
        Try
            Using holder As New FileStream(file_Path, FileMode.Open, FileAccess.ReadWrite, FileShare.None)
                Dim caught As Exception = Nothing
                Try
                    Using reader As New FileStream(file_Path, FileMode.Open, FileAccess.Read, FileShare.Read)
                    End Using
                Catch ex As Exception
                    caught = ex
                End Try
                Assert.NotNull(caught)
                Assert.Equal(PathFailureKind.Locked, PathFailure.Classify(caught))
            End Using
        Finally
            File.Delete(file_Path)
        End Try
    End Sub

    ''' <summary>An IOException that names none of those keeps its Transport reading.</summary>
    <Fact>
    Public Sub Other_HResults_stay_Transport()
        Assert.Equal(PathFailureKind.Transport, PathFailure.Classify(New IOException("network name gone", &H80070040)))
    End Sub

    ' ------------------------------------------------------------------- wrappers ----

    ''' <summary>
    ''' The decode runs on a pool thread, so the transport failure often arrives wrapped.
    ''' An AggregateException classified as Unknown would drop exactly the files this change
    ''' exists to keep.
    ''' </summary>
    <Fact>
    Public Sub A_wrapped_transport_failure_is_still_Transport()
        Assert.Equal(PathFailureKind.Transport,
                     PathFailure.Classify(New AggregateException(New IOException("session dropped"))))
        Assert.Equal(PathFailureKind.Transport,
                     PathFailure.Classify(New TargetInvocationException(New IOException("session dropped"))))
        ' Nested wrappers - a continuation over a task that itself faulted.
        Assert.Equal(PathFailureKind.Transport,
                     PathFailure.Classify(New AggregateException(New AggregateException(New IOException("x")))))
    End Sub

    <Fact>
    Public Sub Unwrapping_does_not_change_a_verdict_about_the_file()
        Assert.Equal(PathFailureKind.Missing,
                     PathFailure.Classify(New AggregateException(New FileNotFoundException("gone"))))
    End Sub

    ''' <summary>An empty AggregateException has nothing to unwrap and must not loop or throw.</summary>
    <Fact>
    Public Sub An_empty_wrapper_does_not_hang()
        Assert.Equal(PathFailureKind.Unknown, PathFailure.Classify(New AggregateException()))
    End Sub

    ' ------------------------------------------------- the failures as thrown for real ----

    ''' <summary>
    ''' The types above are constructed by hand, which proves the Select Case and nothing
    ''' about what the filesystem actually throws. These two come from the real API, so a
    ''' future runtime that changes what it throws breaks the test rather than the user's
    ''' file list.
    ''' </summary>
    <Fact>
    Public Sub A_real_missing_file_throws_something_classified_as_Missing()
        Dim absent As String = Path.Combine(Path.GetTempPath(), "fms-tests-" & Guid.NewGuid().ToString("N") & ".jpg")
        Dim caught As Exception = Nothing
        Try
            File.ReadAllBytes(absent)
        Catch ex As Exception
            caught = ex
        End Try

        Assert.NotNull(caught)
        Assert.Equal(PathFailureKind.Missing, PathFailure.Classify(caught))
    End Sub

    ''' <summary>
    ''' A file another process holds open - the everyday shape of "the bytes are not
    ''' available right now, and the file is fine". It is also the case the viewer already
    ''' treats as temporary elsewhere (ProcessArgument watches a locked file and opens it
    ''' when it unlocks), so dropping it from the list was inconsistent as well as wrong.
    '''
    ''' Chosen over a dead UNC path on purpose: this reproduces in milliseconds on any
    ''' machine, while an unreachable host costs a DNS/NetBIOS timeout and depends on the
    ''' network the test runs on.
    ''' </summary>
    <Fact>
    Public Sub A_real_locked_file_throws_something_the_list_survives()
        Dim locked As String = Path.Combine(Path.GetTempPath(), "fms-tests-" & Guid.NewGuid().ToString("N") & ".jpg")
        File.WriteAllBytes(locked, New Byte() {1, 2, 3})

        Dim caught As Exception = Nothing
        Try
            Using hold As New FileStream(locked, FileMode.Open, FileAccess.Read, FileShare.None)
                Try
                    File.ReadAllBytes(locked)
                Catch ex As Exception
                    caught = ex
                End Try
            End Using
        Finally
            Try
                File.Delete(locked)
            Catch
            End Try
        End Try

        Assert.NotNull(caught)
        Dim kind As PathFailureKind = PathFailure.Classify(caught)
        Assert.False(PathFailure.IsAboutTheFile(kind),
                     "A locked file classified as " & kind.ToString() & " would be dropped from the list.")
    End Sub

End Class
