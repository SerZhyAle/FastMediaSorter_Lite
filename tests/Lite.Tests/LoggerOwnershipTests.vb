Option Explicit On
Option Strict On
Option Infer On
Option Compare Binary

Imports System.Collections.Generic
Imports System.Diagnostics
Imports System.IO
Imports System.Linq
Imports System.Threading.Tasks
Imports Xunit

''' <summary>
''' 105 L07 (057.S13-10, S13-12): the log rotation never deletes a generation it does not own and
''' keeps stranded data, and initialization is transactional - a failure at any step gives back
''' exactly what that attempt acquired, a retry subscribes once and holds one handle.
''' </summary>
<Collection("AppFileLogger")>
Public Class LoggerOwnershipTests

    ' ------------------------------------------------------------------ rotation ----

    ''' <summary>An in-memory folder whose operations fail on demand.</summary>
    Private NotInheritable Class FakeFs
        Implements ILogFileOps

        Friend ReadOnly Files As New Dictionary(Of String, Long)(StringComparer.OrdinalIgnoreCase)
        Friend ReadOnly Calls As New List(Of String)()
        Friend FailOn As Func(Of String, Boolean) = Function(op) False

        Private Sub Maybe(op As String)
            Calls.Add(op)
            If FailOn(op) Then Throw New IOException("injected failure: " & op)
        End Sub

        Public Function Exists(path As String) As Boolean Implements ILogFileOps.Exists
            Return Files.ContainsKey(path)
        End Function

        Public Function Length(path As String) As Long Implements ILogFileOps.Length
            Return Files(path)
        End Function

        Public Sub Move(source As String, destination As String) Implements ILogFileOps.Move
            Maybe("move:" & IO.Path.GetFileName(source) & ">" & IO.Path.GetFileName(destination))
            If Files.ContainsKey(destination) Then Throw New IOException("destination exists")
            Files(destination) = Files(source)
            Files.Remove(source)
        End Sub

        Public Sub Replace(source As String, destination As String, backup As String) Implements ILogFileOps.Replace
            Maybe("replace")
            Files(backup) = Files(destination)
            Files(destination) = Files(source)
            Files.Remove(source)
        End Sub

        Public Sub Delete(path As String) Implements ILogFileOps.Delete
            Maybe("delete:" & IO.Path.GetFileName(path))
            Files.Remove(path)
        End Sub

        Public Function StagingFiles(folder As String, prefix As String) As IEnumerable(Of String) Implements ILogFileOps.StagingFiles
            Return Files.Keys.Where(Function(k) IO.Path.GetFileName(k).StartsWith(prefix, StringComparison.OrdinalIgnoreCase)).ToList()
        End Function
    End Class

    Private Const Dir As String = "X:\logs"
    Private Shared ReadOnly Current As String = Dir & "\current.log"
    Private Shared ReadOnly Previous As String = Dir & "\previous.log"

    Private Shared Function Oversized() As Long
        Return AppFileLogger.Max_Log_Bytes + 1
    End Function

    <Fact>
    Public Sub An_oversized_log_rolls_to_previous_and_the_current_name_is_free()
        Dim fs As New FakeFs()
        fs.Files(Current) = Oversized()
        Assert.True(AppFileLogger.RotateIfOversized(Current, fs))
        Assert.False(fs.Files.ContainsKey(Current))
        Assert.Equal(Oversized(), fs.Files(Previous))
        Assert.DoesNotContain(fs.Files.Keys, Function(k) k.Contains(AppFileLogger.Staging_Prefix))
    End Sub

    <Fact>
    Public Sub An_existing_previous_log_is_replaced_atomically_never_deleted_first()
        Dim fs As New FakeFs()
        fs.Files(Current) = Oversized()
        fs.Files(Previous) = 123
        Assert.True(AppFileLogger.RotateIfOversized(Current, fs))
        Assert.DoesNotContain("delete:previous.log", fs.Calls)
        Assert.Contains("replace", fs.Calls)
        Assert.Equal(Oversized(), fs.Files(Previous))
    End Sub

    <Fact>
    Public Sub A_locked_previous_log_puts_the_data_back_when_the_current_name_is_free()
        Dim fs As New FakeFs()
        fs.Files(Current) = Oversized()
        fs.Files(Previous) = 123
        fs.FailOn = Function(op) op = "replace"
        Assert.False(AppFileLogger.RotateIfOversized(Current, fs))
        Assert.Equal(Oversized(), fs.Files(Current)) ' nothing lost, nothing stranded
        Assert.Equal(123L, fs.Files(Previous)) ' the old generation is untouched
        Assert.DoesNotContain(fs.Files.Keys, Function(k) k.Contains(AppFileLogger.Staging_Prefix))
    End Sub

    <Fact>
    Public Sub When_another_process_already_started_a_new_log_the_staging_data_is_retained_not_overwritten()
        Dim fs As New FakeFs()
        fs.Files(Current) = Oversized()
        fs.Files(Previous) = 123
        ' The publication fails, and by then another start has created its own current.log.
        fs.FailOn = Function(op)
                        If op = "replace" Then
                            fs.Files(Current) = 77
                            Return True
                        End If
                        Return False
                    End Function
        Assert.False(AppFileLogger.RotateIfOversized(Current, fs))
        Assert.Equal(77L, fs.Files(Current)) ' the other writer's log is not clobbered
        Dim stranded As List(Of String) = fs.Files.Keys.Where(Function(k) k.Contains(AppFileLogger.Staging_Prefix)).ToList()
        Assert.Single(stranded) ' ...and the rolled-away data is still on disk
        Assert.Equal(Oversized(), fs.Files(stranded(0)))
    End Sub

    <Fact>
    Public Sub A_stranded_rotation_is_finished_by_the_next_start_when_previous_log_is_free()
        Dim fs As New FakeFs()
        Dim stranded As String = Dir & "\" & AppFileLogger.Staging_Prefix & "abc"
        fs.Files(stranded) = 999
        fs.Files(Current) = 10 ' small: no new rotation
        Assert.False(AppFileLogger.RotateIfOversized(Current, fs))
        Assert.Equal(999L, fs.Files(Previous))
        Assert.False(fs.Files.ContainsKey(stranded))
    End Sub

    <Fact>
    Public Sub A_stranded_rotation_never_overwrites_an_existing_previous_log()
        Dim fs As New FakeFs()
        Dim stranded As String = Dir & "\" & AppFileLogger.Staging_Prefix & "abc"
        fs.Files(stranded) = 999
        fs.Files(Previous) = 5
        fs.Files(Current) = 10
        AppFileLogger.RotateIfOversized(Current, fs)
        Assert.Equal(5L, fs.Files(Previous))
        Assert.True(fs.Files.ContainsKey(stranded)) ' retained
        Assert.DoesNotContain(fs.Calls, Function(c) c.StartsWith("delete"))
    End Sub

    <Fact>
    Public Sub Two_rotators_use_different_staging_names_and_the_loser_does_nothing()
        Dim fs As New FakeFs()
        fs.Files(Current) = Oversized()
        Dim names As New List(Of String)()
        fs.FailOn = Function(op)
                        If op.StartsWith("move:current.log>") Then names.Add(op)
                        Return False
                    End Function
        Assert.True(AppFileLogger.RotateIfOversized(Current, fs))
        ' The second rotator starts after the first moved the file away: nothing to rotate.
        Assert.False(AppFileLogger.RotateIfOversized(Current, fs))
        Assert.Single(names)
        Assert.Equal(Oversized(), fs.Files(Previous))
    End Sub

    <Fact>
    Public Sub A_failing_first_move_leaves_everything_as_it_was()
        Dim fs As New FakeFs()
        fs.Files(Current) = Oversized()
        fs.Files(Previous) = 5
        fs.FailOn = Function(op) op.StartsWith("move:current.log>")
        Assert.False(AppFileLogger.RotateIfOversized(Current, fs))
        Assert.Equal(Oversized(), fs.Files(Current))
        Assert.Equal(5L, fs.Files(Previous))
    End Sub

    ' ---------------------------------------------------------- transactional init ----

    Private NotInheritable Class Sandbox
        Implements IDisposable

        Friend ReadOnly Directory As String = Path.Combine(Path.GetTempPath(), "fms-logger-" & Guid.NewGuid().ToString("N"))
        Private ReadOnly listenersBefore As Integer

        Friend Sub New()
            System.IO.Directory.CreateDirectory(Directory)
            AppFileLogger.ResetForTesting()
            AppFileLogger.LogDirectoryOverride = Directory
            listenersBefore = Trace.Listeners.Count
        End Sub

        Friend ReadOnly Property ListenerDelta As Integer
            Get
                Return Trace.Listeners.Count - listenersBefore
            End Get
        End Property

        Public Sub Dispose() Implements IDisposable.Dispose
            AppFileLogger.InitializeFaultInjector = Nothing
            AppFileLogger.ResetForTesting()
            AppFileLogger.LogDirectoryOverride = Nothing
            Try : System.IO.Directory.Delete(Directory, True) : Catch : End Try
        End Sub
    End Class

    <Theory>
    <InlineData(1)>
    <InlineData(2)>
    <InlineData(3)>
    <InlineData(4)>
    <InlineData(5)>
    <InlineData(6)>
    <InlineData(7)>
    <InlineData(8)>
    <InlineData(9)>
    <InlineData(10)>
    Public Sub A_failure_at_any_step_gives_back_everything_that_attempt_acquired(failingStep As Integer)
        Using box As New Sandbox()
            AppFileLogger.InitializeFaultInjector = Sub(stepNumber)
                                                         If stepNumber = failingStep Then Throw New IOException("injected at " & stepNumber.ToString())
                                                     End Sub
            AppFileLogger.Initialize()

            Assert.False(AppFileLogger.IsReady)
            Assert.False(AppFileLogger.HandlersAttached)
            Assert.Equal(0, box.ListenerDelta)
            ' No handle is left open on the log: it can be opened exclusively and deleted.
            Dim logFile As String = Path.Combine(box.Directory, "current.log")
            If File.Exists(logFile) Then
                Using exclusive As New FileStream(logFile, FileMode.Open, FileAccess.ReadWrite, FileShare.None)
                End Using
                File.Delete(logFile)
            End If
        End Using
    End Sub

    <Theory>
    <InlineData(1)>
    <InlineData(3)>
    <InlineData(5)>
    <InlineData(6)>
    <InlineData(7)>
    <InlineData(8)>
    <InlineData(9)>
    <InlineData(10)>
    Public Sub A_retry_after_a_failure_subscribes_once_and_holds_one_handle(failingStep As Integer)
        Using box As New Sandbox()
            Dim failuresLeft As Integer = 1
            AppFileLogger.InitializeFaultInjector = Sub(stepNumber)
                                                         If stepNumber = failingStep AndAlso failuresLeft > 0 Then
                                                             failuresLeft -= 1
                                                             Throw New IOException("injected once")
                                                         End If
                                                     End Sub
            AppFileLogger.Initialize() ' fails
            Assert.False(AppFileLogger.IsReady)

            AppFileLogger.Initialize() ' retries
            Assert.True(AppFileLogger.IsReady)
            Assert.True(AppFileLogger.HandlersAttached)
            Assert.Equal(1, box.ListenerDelta)

            AppFileLogger.Initialize() ' a third call is a no-op, not a second subscription
            Assert.Equal(1, box.ListenerDelta)

            AppFileLogger.WriteLine("written after the retry")
            Dim logFile As String = Path.Combine(box.Directory, "current.log")
            Using reader As New FileStream(logFile, FileMode.Open, FileAccess.Read, FileShare.ReadWrite Or FileShare.Delete)
                Using sr As New StreamReader(reader)
                    Dim text As String = sr.ReadToEnd()
                    Assert.Contains("written after the retry", text)
                    Assert.Equal(1, text.Split(New String() {"Session started:"}, StringSplitOptions.None).Length - 1)
                End Using
            End Using
        End Using
    End Sub

    ' ----------------------------------------------------------- concurrency tests ----

    <Fact>
    Public Sub Concurrent_rotators_race_safely_and_preserve_all_data()
        Dim dir As String = Path.Combine(Path.GetTempPath(), "fms-rot-conc-" & Guid.NewGuid().ToString("N"))
        Directory.CreateDirectory(dir)
        Try
            Dim logFile As String = Path.Combine(dir, "current.log")
            Dim oversizedContent As Byte() = New Byte(CInt(AppFileLogger.Max_Log_Bytes + 512)) {}
            File.WriteAllBytes(logFile, oversizedContent)

            Dim tasks As New List(Of Task(Of Boolean))()
            For i As Integer = 1 To 10
                tasks.Add(Task.Run(Function() AppFileLogger.RotateIfOversized(logFile)))
            Next
            Task.WaitAll(tasks.ToArray())

            ' At most one caller reports the roll-over. Zero is legitimate too: a racer's
            ' CompleteStrandedRotations may finish the winner's staging file into previous.log
            ' before the winner reaches Publish, which then finds nothing left to publish.
            ' What must hold either way is that the data exists exactly once.
            Dim successCount As Integer = tasks.Where(Function(t) t.Result).Count()
            Assert.InRange(successCount, 0, 1)
            Assert.True(File.Exists(Path.Combine(dir, "previous.log")))
            Dim totalBytes As Long = Directory.GetFiles(dir).Sum(Function(f) New FileInfo(f).Length)
            Assert.Equal(oversizedContent.LongLength, totalBytes)
        Finally
            Try : Directory.Delete(dir, True) : Catch : End Try
        End Try
    End Sub

    <Fact>
    Public Sub Concurrent_writers_append_without_overwriting_or_corrupting_lines()
        Dim dir As String = Path.Combine(Path.GetTempPath(), "fms-log-conc-" & Guid.NewGuid().ToString("N"))
        Directory.CreateDirectory(dir)
        Try
            Dim logFile As String = Path.Combine(dir, "current.log")
            Dim lineCountPerThread As Integer = 30
            Dim threadCount As Integer = 6

            Dim tasks As New List(Of Task)()
            For t As Integer = 1 To threadCount
                Dim threadId As Integer = t
                tasks.Add(Task.Run(Sub()
                                       Using stream As Stream = AppFileLogger.OpenAppendOnly(logFile)
                                           Using writer As New StreamWriter(stream)
                                               writer.AutoFlush = True
                                               For i As Integer = 1 To lineCountPerThread
                                                   writer.WriteLine("THREAD_" & threadId.ToString() & "_LINE_" & i.ToString())
                                               Next
                                           End Using
                                       End Using
                                   End Sub))
            Next
            Task.WaitAll(tasks.ToArray())

            Dim lines As String() = File.ReadAllLines(logFile)
            Assert.Equal(threadCount * lineCountPerThread, lines.Length)
            For t As Integer = 1 To threadCount
                For i As Integer = 1 To lineCountPerThread
                    Dim expected As String = "THREAD_" & t.ToString() & "_LINE_" & i.ToString()
                    Assert.Contains(expected, lines)
                Next
            Next
        Finally
            Try : Directory.Delete(dir, True) : Catch : End Try
        End Try
    End Sub

    <Fact>
    Public Sub Stranded_staging_generations_are_preserved_when_previous_log_is_locked_and_multiple_rotations_attempted()
        Dim fs As New FakeFs()
        fs.Files(Current) = Oversized()
        fs.Files(Previous) = 50
        fs.FailOn = Function(op) op = "replace"

        ' First rotation attempt fails during publish/replace
        Assert.False(AppFileLogger.RotateIfOversized(Current, fs))
        ' Staging was put back because current name was free
        Assert.Equal(Oversized(), fs.Files(Current))

        ' Now simulate another process creating current.log before rollback move can happen
        fs.FailOn = Function(op)
                        If op = "replace" Then
                            fs.Files(Current) = 12 ' another process started writing current.log
                            Return True
                        End If
                        Return False
                    End Function

        Dim stagingCreated As String = ""
        fs.Files(Current) = Oversized()
        Assert.False(AppFileLogger.RotateIfOversized(Current, fs))

        ' The stranded staging generation must be preserved, not deleted
        Dim strandedList As List(Of String) = fs.Files.Keys.Where(Function(k) k.Contains(AppFileLogger.Staging_Prefix)).ToList()
        Assert.Single(strandedList)
        Assert.Equal(Oversized(), fs.Files(strandedList(0)))
        Assert.Equal(12L, fs.Files(Current))
        Assert.Equal(50L, fs.Files(Previous))
    End Sub

    Private NotInheritable Class FakeViewerProcess
        Implements IViewerProcess

        Friend Disposed As Boolean
        Friend ThrowOnId As Boolean
        Friend ThrowOnWindow As Boolean
        Friend PidValue As Integer
        Friend HandleValue As IntPtr

        Public ReadOnly Property Id As Integer Implements IViewerProcess.Id
            Get
                If ThrowOnId Then Throw New InvalidOperationException("The process has exited.")
                Return PidValue
            End Get
        End Property

        Public ReadOnly Property MainWindowHandle As IntPtr Implements IViewerProcess.MainWindowHandle
            Get
                If ThrowOnWindow Then Throw New InvalidOperationException("The process has exited.")
                Return HandleValue
            End Get
        End Property

        Public Sub Dispose() Implements IDisposable.Dispose
            Disposed = True
        End Sub
    End Class

    <Fact>
    Public Sub Concurrent_process_discovery_tolerates_target_disappearance_and_disposes_all()
        Dim totalTasks As Integer = 8
        Dim tasks As New List(Of Task)()

        For t As Integer = 1 To totalTasks
            Dim taskIndex As Integer = t
            tasks.Add(Task.Run(Sub()
                                   Dim p1 As New FakeViewerProcess With {.PidValue = 100 + taskIndex, .HandleValue = New IntPtr(100 + taskIndex)}
                                   Dim p2 As New FakeViewerProcess With {.ThrowOnId = True}
                                   Dim p3 As New FakeViewerProcess With {.PidValue = 200 + taskIndex, .ThrowOnWindow = True}
                                   Dim pCurrent As New FakeViewerProcess With {.PidValue = 999}
                                   Dim list As New List(Of IViewerProcess) From {p1, p2, p3, pCurrent}

                                   Dim targets As List(Of ViewerProcessTarget) = ViewerProcessDiscovery.Discover(
                                       {"FastMediaSorter_LITE", "FastMediaSorter_x86"}, 999,
                                       Function(name)
                                           If name = "FastMediaSorter_x86" AndAlso (taskIndex Mod 2 = 0) Then
                                               Throw New InvalidOperationException("process enumeration fault")
                                           End If
                                           Return list
                                       End Function,
                                       Function(pid)
                                           If pid Mod 3 = 0 Then Throw New InvalidOperationException("window scan fault")
                                           Return New List(Of IntPtr) From {New IntPtr(pid)}
                                       End Function)

                                   Assert.NotNull(targets)
                                   Assert.True(p1.Disposed)
                                   Assert.True(p2.Disposed)
                                   Assert.True(p3.Disposed)
                                   Assert.True(pCurrent.Disposed)
                               End Sub))
        Next
        Task.WaitAll(tasks.ToArray())
    End Sub

End Class
