#If Not NETFRAMEWORK Then
Option Strict On

Imports System.Collections.Generic
Imports System.IO
Imports System.Text.RegularExpressions
Imports Xunit

''' <summary>
''' Ticket 124 UX-213: the container password is checked OFF the UI thread. Argon2id (64 MiB over four lanes) and the
''' decrypt of a picture used to run on the UI thread after "Open", so the window stopped painting, stopped taking
''' keys and could not be told to move on. A form cannot be built in a test, so the rules are held as readings of
''' <c>Main_Form.Secure.vb</c>: where the heavy calls are allowed to be, what the UI half does after the await, and
''' what a window that was moved on from is allowed to leave behind.
''' </summary>
Public Class SecureOpenOffThreadTests

    Private Shared Function SecureSource() As String
        Dim dir As New DirectoryInfo(AppContext.BaseDirectory)
        While dir IsNot Nothing
            Dim candidate As String = Path.Combine(dir.FullName, "src", "Main_Form.Secure.vb")
            If File.Exists(candidate) Then Return File.ReadAllText(candidate)
            dir = dir.Parent
        End While
        Throw New FileNotFoundException("src\Main_Form.Secure.vb not found above " & AppContext.BaseDirectory)
    End Function

    ''' <summary>The body of a method of the partial, from its declaration to the next declaration.</summary>
    Private Shared Function MethodBody(source As String, declaration As String) As String
        Dim start As Integer = source.IndexOf(declaration, StringComparison.Ordinal)
        Assert.True(start >= 0, declaration & " is missing")
        Dim match As Match = Regex.Match(source.Substring(start + declaration.Length),
                                         "^\s*(?:Private|Friend|Public)\s+(?:Shared\s+|Async\s+)*(?:Sub|Function)\s", RegexOptions.Multiline)
        Dim length As Integer = If(match.Success, declaration.Length + match.Index, source.Length - start)
        Return source.Substring(start, length)
    End Function

    ''' <summary>The only places a container is opened, with the name of the method each is in.</summary>
    Private Shared Function OpenCallSites(source As String) As List(Of String)
        Dim sites As New List(Of String)()
        Dim current As String = ""
        For Each line As String In source.Split({vbLf}, StringSplitOptions.None)
            Dim declaration As Match = Regex.Match(line, "^\s*(?:Private|Friend|Public)\s+(?:Shared\s+|Async\s+)*(?:Sub|Function)\s+(\w+)")
            If declaration.Success Then current = declaration.Groups(1).Value
            If line.TrimStart().StartsWith("'", StringComparison.Ordinal) Then Continue For
            If line.Contains("FdSecContainer.OpenFile(") Then sites.Add(current)
        Next
        Return sites
    End Function

    ''' <summary>A new call site of the open on the UI thread is exactly the freeze this ticket removed: the open belongs
    ''' on a pool thread, or in the prefetch, which is one.</summary>
    <Fact>
    Public Sub A_container_is_opened_only_where_a_pool_thread_runs_it()
        Dim allowed As New HashSet(Of String) From {"OpenSecureContainerOffThread", "SecureVerifyCurrent", "PrefetchSecureContainer"}
        Dim sites As List(Of String) = OpenCallSites(SecureSource())
        Assert.NotEmpty(sites)
        For Each site As String In sites
            Assert.True(allowed.Contains(site), "FdSecContainer.OpenFile is called from " & site & ", which runs on the UI thread")
        Next
        Assert.Contains("OpenSecureContainerOffThread", sites)
    End Sub

    <Fact>
    Public Sub The_open_attempt_runs_on_the_pool_and_the_ui_thread_only_awaits_it()
        Dim body As String = MethodBody(SecureSource(), "Private Async Function TryShowSecureContentAsync")
        Assert.Contains("Task.Run(Function() OpenSecureContainerOffThread(", body)
        Assert.Contains("work = Await pool", body)
        ' The synchronous wait cursor of the old call is gone: the cursor now follows the attempt's lifetime.
        Assert.DoesNotContain("Cursor.Current", body)
        Assert.Contains("BeginSecureWait()", body)
        Assert.Contains("EndSecureWait()", body)
        ' No synchronous call of the old name survives anywhere.
        Assert.False(Regex.IsMatch(SecureSource(), "TryShowSecureContent\("), "a synchronous TryShowSecureContent( is back")
    End Sub

    ''' <summary>The pool half reads no field of the form and never throws: what went wrong travels in the result so
    ''' the UI half classifies it exactly as the synchronous code did.</summary>
    Private Shared Function PoolHalf() As String
        Return MethodBody(SecureSource(), "Private Shared Function OpenSecureContainerOffThread")
    End Function

    <Fact>
    Public Sub The_pool_half_touches_no_form_state_and_carries_failures_in_its_result()
        Dim body As String = PoolHalf()
        For Each forbidden As String In {"lbl_Status", "secure_", "Notify(", "ShowSecurePlaceholder", "Me.", "media_Generation", "Current_File_Name"}
            Assert.DoesNotContain(forbidden, body)
        Next
        Assert.Contains("work.Failure = ex", body)
        Assert.Contains("work.ImageFailure = ex", body)
        ' Shared: a Shared member cannot reach an instance field by accident.
        Assert.Contains("Private Shared Function OpenSecureContainerOffThread", SecureSource())
    End Sub

    ''' <summary>A picture's whole content is decrypted in the same off-thread half (the other freeze), into a buffer
    ''' that is wiped on release - and the display path no longer reads the content itself.</summary>
    <Fact>
    Public Sub A_pictures_content_is_decrypted_off_the_ui_thread_into_a_wiping_buffer()
        Assert.Contains("New FdSecWipingMemoryStream(", PoolHalf())
        Assert.Contains("content.CopyTo(buffer)", PoolHalf())
        Dim display As String = MethodBody(SecureSource(), "Private Function ShowSecureImage")
        Assert.DoesNotContain("CopyTo(", display)
        Assert.DoesNotContain("OpenContentStream", display)
        ' A chunk that failed its tag on the pool is raised in the display path, into the handler that names it.
        Assert.Contains("ExceptionDispatchInfo.Capture(contentFailure).Throw()", display)
    End Sub

    ''' <summary>The user may go on while the check runs: the window is not "locked" for the dialog, and a second
    ''' password window is refused only while the first one is actually up.</summary>
    <Fact>
    Public Sub The_password_window_flag_covers_the_window_and_not_the_check()
        Dim body As String = MethodBody(SecureSource(), "Private Async Sub AskForPasswordAndOpen")
        Dim raise As Integer = body.IndexOf("secure_Prompting = True", StringComparison.Ordinal)
        Dim lower As Integer = body.IndexOf("secure_Prompting = False", StringComparison.Ordinal)
        Dim check As Integer = body.IndexOf("Await TryShowSecureContentAsync(", StringComparison.Ordinal)
        Assert.True(raise > 0 AndAlso lower > raise AndAlso check > lower, "the flag must be lowered before the check is awaited")
        Assert.Contains("Finally", body.Substring(raise, lower - raise))
    End Sub

    ''' <summary>An attempt the user moved on from releases everything it opened and draws nothing - and the wait line
    ''' it put on the status is taken back.</summary>
    <Fact>
    Public Sub A_superseded_attempt_discards_what_it_opened_and_draws_nothing()
        Dim body As String = MethodBody(SecureSource(), "Private Async Function TryShowSecureContentAsync")
        Dim at As Integer = body.IndexOf("If superseded Then", StringComparison.Ordinal)
        Assert.True(at > 0, "the superseded branch is missing")
        Dim branch As String = body.Substring(at, body.IndexOf("Return result", at, StringComparison.Ordinal) - at)
        Assert.Contains("work.Discard()", branch)
        Assert.Contains("SecureOpenVerdict.Superseded", body.Substring(at, body.IndexOf("Return result", at, StringComparison.Ordinal) + 20 - at))
        ' The one thing it may draw is the "locked again" placeholder of a check cancelled in place: the same file,
        ' the same media, nothing newer checking it - never a refusal, never content.
        Assert.Contains("secure_Open_Active = 0", branch)
        Assert.Contains("generation = media_Generation", branch)
        Assert.Contains("locked:=True", branch)
        Assert.DoesNotContain("ShowSecureImage", branch)
        Assert.DoesNotContain("ShowSecurePlayback", branch)
        Assert.DoesNotContain("SecureOutcomeMessage", branch)
        ' ... and the four reasons to be superseded are all read.
        Dim rule As String = MethodBody(SecureSource(), "Private Function IsSecureOpenSuperseded")
        Assert.Contains("IsDisposed", rule)
        Assert.Contains("secure_Open_Active", rule)
        Assert.Contains("media_Generation", rule)
        Assert.Contains("Current_File_Name", rule)
    End Sub

    ''' <summary>Forgetting the password puts down a check that is still running with it.</summary>
    <Fact>
    Public Sub Forgetting_the_credential_cancels_the_check_in_flight()
        Dim body As String = MethodBody(SecureSource(), "Friend Sub SecureForgetCredential")
        Assert.Contains("CancelSecureOpen()", body)
    End Sub

    ''' <summary>While a check runs for a file, its placeholder is not an "unlock" surface: Enter or a click must not
    ''' open a second password window over it.</summary>
    <Fact>
    Public Sub A_placeholder_is_not_unlockable_while_its_check_runs()
        Dim body As String = MethodBody(SecureSource(), "Friend Function IsSecurePlaceholderLocked")
        Assert.Contains("IsSecureOpenRunningFor(Current_File_Name)", body)
    End Sub

    ''' <summary>The dispatcher used to finish what comes after a container is up (the title, the translate buttons):
    ''' it ran after the synchronous open and now runs before the content arrives, so the open does it.</summary>
    <Fact>
    Public Sub What_the_dispatcher_did_after_the_open_is_done_when_the_content_lands()
        Dim body As String = MethodBody(SecureSource(), "Private Async Function TryShowSecureContentAsync")
        Dim shown As Integer = body.LastIndexOf("SecureOpenVerdict.Shown", StringComparison.Ordinal)
        Dim title As Integer = body.LastIndexOf("UpdateContentWindowTitle()", StringComparison.Ordinal)
        Dim displayed As Integer = body.LastIndexOf("OnMediaDisplayed()", StringComparison.Ordinal)
        Assert.True(title > 0 AndAlso displayed > 0 AndAlso shown > title AndAlso shown > displayed)
    End Sub

    <Fact>
    Public Sub The_dispatcher_still_hands_a_container_to_the_one_entry_point()
        Dim dir As New DirectoryInfo(AppContext.BaseDirectory)
        While dir IsNot Nothing
            Dim candidate As String = Path.Combine(dir.FullName, "src", "Main_Form.MediaLoading.vb")
            If File.Exists(candidate) Then
                Assert.Contains("DisplaySecureContainer(Current_File_Name)", File.ReadAllText(candidate))
                Return
            End If
            dir = dir.Parent
        End While
        Throw New FileNotFoundException("src\Main_Form.MediaLoading.vb not found above " & AppContext.BaseDirectory)
    End Sub

End Class
#End If
