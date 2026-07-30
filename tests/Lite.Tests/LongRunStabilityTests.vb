Option Strict On

Imports System.IO
Imports System.Threading
Imports System.Threading.Tasks
Imports Xunit

''' <summary>
''' Guards the long-run stability fixes (SPECIFICATION_LONG_RUN_STABILITY.md §7).
'''
''' Each of these covers something that only shows up after hours or days of use - a chain
''' that outgrows the stack, a log that never stops growing, a cache nothing evicts, a queue
''' that accepts work it will not do. None of them can be caught by opening the app and
''' looking at it, which is precisely why they need tests.
''' </summary>
Public Class LongRunStabilityTests

    ' ---------------------------------------------------------------- A-2 ----
    ' The auto-skip chain bound. Before the fix the bound WAS the recursion depth, so a
    ' folder of thousands of unreachable files (a NAS that dropped its SMB session) killed
    ' the process with StackOverflowException - taking every unsaved setting with it.

    <Fact>
    Public Sub Auto_skip_continues_while_the_chain_is_shorter_than_the_folder()
        Assert.True(AutoSkipPolicy.ShouldContinue(1, 5000))
        Assert.True(AutoSkipPolicy.ShouldContinue(4999, 5000))
    End Sub

    <Fact>
    Public Sub Auto_skip_stops_once_the_chain_has_covered_the_whole_folder()
        ' The index wraps round; the count must not. One pass over the folder is the answer
        ' to "is anything here readable", and a second pass would be the same answer.
        Assert.True(AutoSkipPolicy.ShouldContinue(5000, 5000))
        Assert.False(AutoSkipPolicy.ShouldContinue(5001, 5000))
    End Sub

    <Fact>
    Public Sub Auto_skip_allows_one_attempt_in_a_single_file_or_empty_folder()
        ' Math.Max(1, ..): a one-file folder still gets its attempt, and an empty list - where
        ' the caller has already bailed by other means - does not ask for zero attempts.
        Assert.True(AutoSkipPolicy.ShouldContinue(1, 1))
        Assert.False(AutoSkipPolicy.ShouldContinue(2, 1))
        Assert.True(AutoSkipPolicy.ShouldContinue(1, 0))
        Assert.False(AutoSkipPolicy.ShouldContinue(2, 0))
    End Sub

    ' ---------------------------------------------------------------- A-3 ----
    ' current.log is opened Append and nothing else in the app ever shrank it, so it grew for
    ' the life of the installation - megabytes a day for a heavy user.

    <Fact>
    Public Sub An_oversized_log_is_rolled_over_to_previous_log()
        Using dir As New TempDir()
            Dim logPath As String = Path.Combine(dir.Directory_Path, "current.log")
            File.WriteAllText(logPath, New String("x"c, 64))
            ' Overshoot the cap by a byte's worth of intent, not by writing 8 MB of text.
            Using fs As New FileStream(logPath, FileMode.Open, FileAccess.Write)
                fs.SetLength(AppFileLogger.Max_Log_Bytes + 1)
            End Using

            Assert.True(AppFileLogger.RotateIfOversized(logPath))

            Assert.False(File.Exists(logPath), "current.log should have been moved aside")
            Dim rolled As String = Path.Combine(dir.Directory_Path, "previous.log")
            Assert.True(File.Exists(rolled), "the old log should be kept as previous.log")
            Assert.True(New FileInfo(rolled).Length > AppFileLogger.Max_Log_Bytes)
        End Using
    End Sub

    <Fact>
    Public Sub A_log_under_the_cap_is_left_alone()
        Using dir As New TempDir()
            Dim logPath As String = Path.Combine(dir.Directory_Path, "current.log")
            File.WriteAllText(logPath, "one short session")

            Assert.False(AppFileLogger.RotateIfOversized(logPath))

            Assert.True(File.Exists(logPath))
            Assert.Equal("one short session", File.ReadAllText(logPath))
            Assert.False(File.Exists(Path.Combine(dir.Directory_Path, "previous.log")))
        End Using
    End Sub

    <Fact>
    Public Sub Rotation_keeps_exactly_one_generation()
        Using dir As New TempDir()
            Dim logPath As String = Path.Combine(dir.Directory_Path, "current.log")
            Dim rolled As String = Path.Combine(dir.Directory_Path, "previous.log")
            File.WriteAllText(rolled, "the session before last")
            Oversized(logPath, "the last session")

            Assert.True(AppFileLogger.RotateIfOversized(logPath))

            ' One generation back, not a numbered series - a series is another thing that grows.
            ' (Assert on the whole directory rather than on a "*.log.*" glob: on Windows that
            ' pattern also matches plain "previous.log", so it proved nothing.)
            Assert.StartsWith("the last session", File.ReadAllText(rolled))
            Dim left As String() = Directory.GetFiles(dir.Directory_Path)
            Assert.Single(left)
            Assert.Equal("previous.log", Path.GetFileName(left(0)))
        End Using
    End Sub

    <Fact>
    Public Sub Rotating_a_missing_log_is_not_an_error()
        Using dir As New TempDir()
            ' Logging must never break startup, so every failure path is a quiet False.
            Assert.False(AppFileLogger.RotateIfOversized(Path.Combine(dir.Directory_Path, "current.log")))
            Assert.False(AppFileLogger.RotateIfOversized(""))
        End Using
    End Sub

    ' ---------------------------------------------------------------- D-1 ----
    ' The OCR disk cache and the browser-translate cache both grew without limit. Worse, the
    ' megabyte budget the settings window offered was read, persisted and displayed - and
    ' enforced by no code at all.

    <Fact>
    Public Sub Trimming_removes_the_oldest_entries_until_the_budget_is_met()
        Using dir As New TempDir()
            ' Four 1 MB entries, a 2 MB budget: the two oldest have to go.
            WriteSized(dir.Directory_Path, "oldest.json", 1, #2020-01-01#)
            WriteSized(dir.Directory_Path, "older.json", 1, #2021-01-01#)
            WriteSized(dir.Directory_Path, "newer.json", 1, #2022-01-01#)
            WriteSized(dir.Directory_Path, "newest.json", 1, #2023-01-01#)

            Dim removed As Integer = DiskCacheTrim.TrimToBudget(dir.Directory_Path, 2)

            Assert.Equal(2, removed)
            Assert.False(File.Exists(Path.Combine(dir.Directory_Path, "oldest.json")))
            Assert.False(File.Exists(Path.Combine(dir.Directory_Path, "older.json")))
            Assert.True(File.Exists(Path.Combine(dir.Directory_Path, "newer.json")))
            Assert.True(File.Exists(Path.Combine(dir.Directory_Path, "newest.json")))
        End Using
    End Sub

    <Fact>
    Public Sub Trimming_does_nothing_while_the_cache_is_under_budget()
        Using dir As New TempDir()
            WriteSized(dir.Directory_Path, "a.json", 1, #2020-01-01#)
            WriteSized(dir.Directory_Path, "b.json", 1, #2021-01-01#)

            Assert.Equal(0, DiskCacheTrim.TrimToBudget(dir.Directory_Path, 250))
            Assert.Equal(2, Directory.GetFiles(dir.Directory_Path, "*.json").Length)
        End Using
    End Sub

    <Fact>
    Public Sub A_zero_budget_means_no_limit_and_deletes_nothing()
        Using dir As New TempDir()
            WriteSized(dir.Directory_Path, "a.json", 4, #2020-01-01#)

            ' This is the reading the settings hint documents; deleting the cache on "0" would
            ' be the opposite of what a user setting no limit asked for.
            Assert.Equal(0, DiskCacheTrim.TrimToBudget(dir.Directory_Path, 0))
            Assert.True(File.Exists(Path.Combine(dir.Directory_Path, "a.json")))
        End Using
    End Sub

    <Fact>
    Public Sub Trimming_ignores_files_outside_the_pattern()
        Using dir As New TempDir()
            WriteSized(dir.Directory_Path, "cached.json", 4, #2020-01-01#)
            WriteSized(dir.Directory_Path, "notes.txt", 4, #2019-01-01#)

            DiskCacheTrim.TrimToBudget(dir.Directory_Path, 1)

            ' Only what this cache owns. The .txt is older and bigger - and none of our business.
            Assert.True(File.Exists(Path.Combine(dir.Directory_Path, "notes.txt")))
        End Using
    End Sub

    <Fact>
    Public Sub Folder_entries_are_measured_and_removed_whole()
        Using dir As New TempDir()
            ' The browser-translate cache keeps one FOLDER per image (an HTML page + assets).
            WriteSizedInFolder(dir.Directory_Path, "old-page", 2, #2020-01-01#)
            WriteSizedInFolder(dir.Directory_Path, "new-page", 2, #2023-01-01#)

            Dim removed As Integer = DiskCacheTrim.TrimToBudget(dir.Directory_Path, 3, entries_Are_Folders:=True)

            Assert.Equal(1, removed)
            Assert.False(Directory.Exists(Path.Combine(dir.Directory_Path, "old-page")))
            Assert.True(Directory.Exists(Path.Combine(dir.Directory_Path, "new-page")))
        End Using
    End Sub

    <Fact>
    Public Sub Trimming_a_missing_directory_is_not_an_error()
        Using dir As New TempDir()
            Assert.Equal(0, DiskCacheTrim.TrimToBudget(Path.Combine(dir.Directory_Path, "never-created"), 1))
            Assert.Equal(0, DiskCacheTrim.TrimToBudget("", 1))
        End Using
    End Sub

#If Not NETFRAMEWORK Then
    ' ---------------------------------------------------------------- B-2 ----
    ' Modern only - FileOpQueue is whole-file "#If Not NETFRAMEWORK". After DrainAsync closed
    ' the queue, Enqueue silently dropped the operation while the window stayed interactive
    ' for up to 15 s and the UI had already reported "moved"/"deleted".

    <Fact>
    Public Async Function An_open_queue_accepts_and_runs_the_operation() As Task
        Dim ran As New ManualResetEventSlim(False)
        Dim queue As New FileOpQueue(Of String)(Sub(op) ran.Set())

        Assert.True(queue.Enqueue("work"))
        Assert.False(queue.IsClosed)

        Assert.True(Await queue.DrainAsync(TimeSpan.FromSeconds(5)))
        Assert.True(ran.IsSet, "the queued operation should have run before the drain returned")
        Assert.Equal(0, queue.PendingCount)
    End Function

    <Fact>
    Public Async Function A_drained_queue_refuses_further_operations_out_loud() As Task
        Dim ran As Integer = 0
        Dim queue As New FileOpQueue(Of String)(Sub(op) Interlocked.Increment(ran))

        Await queue.DrainAsync(TimeSpan.FromSeconds(5))

        ' The refusal has to be visible: the caller has already removed the file from the list
        ' and told the user it was moved, and only a reported failure rolls that back.
        Assert.True(queue.IsClosed)
        Assert.False(queue.Enqueue("too late"))
        Assert.Equal(0, Volatile.Read(ran))
    End Function

    <Fact>
    Public Async Function A_refused_operation_does_not_leave_the_pending_count_stuck() As Task
        Dim queue As New FileOpQueue(Of String)(Sub(op)
                                                End Sub)
        Await queue.DrainAsync(TimeSpan.FromSeconds(5))

        queue.Enqueue("too late")

        ' A stuck counter would keep FormClosing waiting for work that will never arrive.
        Assert.Equal(0, queue.PendingCount)
    End Function
#End If

    ' ---------------------------------------------------------------- helpers ----

    ' VB is case-insensitive, so a local called "path" shadows System.IO.Path and every
    ' Path.Combine below becomes a String member lookup. Hence "target"/"entry".
    Private Shared Sub Oversized(target As String, marker As String)
        File.WriteAllText(target, marker)
        Using fs As New FileStream(target, FileMode.Open, FileAccess.Write)
            fs.SetLength(AppFileLogger.Max_Log_Bytes + 1)
        End Using
    End Sub

    ''' <summary>A file of exactly <paramref name="mb"/> megabytes, stamped with a write time -
    ''' SetLength keeps the test fast (sparse where the file system allows it).</summary>
    Private Shared Sub WriteSized(dir As String, name As String, mb As Integer, written As Date)
        Dim target As String = Path.Combine(dir, name)
        Using fs As New FileStream(target, FileMode.Create, FileAccess.Write)
            fs.SetLength(CLng(mb) * 1024L * 1024L)
        End Using
        File.SetLastWriteTimeUtc(target, written)
    End Sub

    Private Shared Sub WriteSizedInFolder(root As String, folder As String, mb As Integer, written As Date)
        Dim entry As String = Path.Combine(root, folder)
        Directory.CreateDirectory(entry)
        WriteSized(entry, "page.html", mb, written)
        Directory.SetLastWriteTimeUtc(entry, written)
    End Sub

    Private NotInheritable Class TempDir
        Implements IDisposable

        ''' <summary>Not named "Path" - that shadows System.IO.Path inside the class and turns
        ''' every Path.Combine here into a String member lookup.</summary>
        Public ReadOnly Property Directory_Path As String

        Public Sub New()
            _Directory_Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(),
                                                    "fms-longrun-" & Guid.NewGuid().ToString("N"))
            System.IO.Directory.CreateDirectory(_Directory_Path)
        End Sub

        Public Sub Dispose() Implements IDisposable.Dispose
            Try
                System.IO.Directory.Delete(_Directory_Path, True)
            Catch
            End Try
        End Sub
    End Class

End Class
