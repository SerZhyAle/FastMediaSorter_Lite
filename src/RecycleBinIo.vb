#If Not NETFRAMEWORK Then
Option Strict On

Imports System.IO
Imports System.Runtime.ExceptionServices
Imports System.Security.Principal
Imports System.Threading

' The act of deleting, both ways - and, since Ф3, the act of taking it back.
' 017_SPECIFICATION_RECYCLE_BIN_AND_UNDO_DOTNET10.md §3.4 and §3.6. Modern build only - the
' x86 viewer keeps its bare File.Delete (invariant 10).
'
' Everything shell-affine in the application lives in this one file, which is the point:
' it is the only place that has to know about apartment state, and every route into it
' (the viewer's queue, the F3 panel's bulk loop) gets that guarantee for free. The restore
' half needs none of it - it is a directory listing and a File.Move - so it deliberately
' does NOT go through RunOnSta: an STA thread that buys nothing is a thread that will
' later be copied somewhere it costs something.

''' <summary>What came of an attempt to bring a file back out of the bin. It is a RESULT,
''' not an exception, because "the bin was emptied in between" is an ordinary answer the
''' user gets told about - while a denied access really is a failure and stays one.</summary>
Friend Enum BinRestoreResult
    ''' <summary>The operation never ran. Never shown; it is the default of a fresh op.</summary>
    NotAttempted
    ''' <summary>The file is back at the path the caller asked for.</summary>
    Restored
    ''' <summary>No record names that path any more, or its data file is gone - the bin has
    ''' been emptied, or cleaned by something else. The two cases are one message on purpose:
    ''' from the user's side they are the same event.</summary>
    NotInBin
    ''' <summary>The folder the file came from no longer exists. We do NOT recreate it -
    ''' silently rebuilding a tree the user deleted is a surprise, and this is a viewer.</summary>
    SourceFolderGone
End Enum

Friend Module RecycleBinIo

    ''' <summary>
    ''' Deletes exactly the way the decision said it would. The one place where the
    ''' outcome the user was shown turns into an action - a second executor somewhere
    ''' else is how the two would start to disagree (invariant 1).
    ''' </summary>
    Friend Sub DeleteAs(path As String, decision As DeleteDecision)
        If decision IsNot Nothing AndAlso decision.Outcome = DeleteOutcome.Recycle Then
            SendToBin(path)
        Else
            DeleteFile(path)
        End If
    End Sub

    ''' <summary>
    ''' Hands the file to the shell, which writes the $I/$R pair the Recycle Bin is made
    ''' of. We never write that pair ourselves: it carries the original path and the
    ''' deletion time, Explorer's Restore depends on it, and a hand-made one that is
    ''' subtly wrong is a file the user cannot get back.
    '''
    ''' UIOption.OnlyErrorDialogs keeps the shell's own confirmation out of the loop -
    ''' ours is better, because it already knows whether the bin will really take the
    ''' file. UICancelOption.ThrowException turns a shell refusal into an exception the
    ''' queue already knows how to carry back to the UI thread as a failed operation,
    ''' which is what makes the optimistic list mutation roll back.
    ''' </summary>
    Friend Sub SendToBin(path As String)
        RunOnSta(Sub()
                     Microsoft.VisualBasic.FileIO.FileSystem.DeleteFile(
                         path,
                         Microsoft.VisualBasic.FileIO.UIOption.OnlyErrorDialogs,
                         Microsoft.VisualBasic.FileIO.RecycleOption.SendToRecycleBin,
                         Microsoft.VisualBasic.FileIO.UICancelOption.ThrowException)
                 End Sub)
    End Sub

    ''' <summary>
    ''' Brings one file back out of the bin, to <paramref name="destinationPath"/> - which is
    ''' the path it was deleted from, unless something has taken that name in the meantime and
    ''' the caller resolved the collision first.
    '''
    ''' Plain file work, no shell and no STA: the $R IS the file, so putting it back is the
    ''' move the shell itself would perform. What is NOT here is any attempt to recreate the
    ''' folder (§3.6) - a tree the user deleted stays deleted.
    '''
    ''' It never reports success it has not seen: the last thing it does is ask whether the
    ''' file is really there, which is what lets the caller obey invariant 8 and touch the
    ''' file list only after the file exists again. A denied access is left to THROW - that
    ''' one is a genuine failure, and the queue already carries it back with its message.
    ''' </summary>
    Friend Function TryRestore(originalPath As String, deletedAtUtc As DateTime, destinationPath As String) As BinRestoreResult
        If String.IsNullOrEmpty(originalPath) Then Return BinRestoreResult.NotInBin

        Dim target As String = If(String.IsNullOrEmpty(destinationPath), originalPath, destinationPath)

        Dim folder As String = ""
        Try
            folder = Path.GetDirectoryName(target)
        Catch
        End Try
        If String.IsNullOrEmpty(folder) OrElse Not Directory.Exists(folder) Then Return BinRestoreResult.SourceFolderGone

        Dim record As RecycleBinRecord = FindRecord(originalPath, deletedAtUtc)
        If record Is Nothing OrElse String.IsNullOrEmpty(record.DataPath) OrElse Not File.Exists(record.DataPath) Then
            Return BinRestoreResult.NotInBin
        End If

        File.Move(record.DataPath, target)

        ' The data is home; an orphaned $I is Explorer's problem, not the user's file. It is
        ' deleted last and its failure is swallowed on purpose - throwing here would report a
        ' restore that plainly happened as an error.
        Try
            File.Delete(record.IndexPath)
        Catch
        End Try

        Return If(File.Exists(target), BinRestoreResult.Restored, BinRestoreResult.NotInBin)
    End Function

    ''' <summary>
    ''' The impure half of §3.6: every $I in the SID folder of the ORIGINAL path's volume,
    ''' parsed, then matched by RecycleBinIndex. Per-volume and per-user is how the bin is
    ''' built - a file deleted from D: is never in C:'s bin, and another account's records
    ''' are not ours to read.
    ''' </summary>
    Private Function FindRecord(originalPath As String, deletedAtUtc As DateTime) As RecycleBinRecord
        Dim root As String = ""
        Try
            root = Path.GetPathRoot(originalPath)
        Catch
        End Try
        If String.IsNullOrEmpty(root) Then Return Nothing

        Dim sid As SecurityIdentifier = WindowsIdentity.GetCurrent().User
        If sid Is Nothing Then Return Nothing

        Dim bin_Dir As String = Path.Combine(root, "$Recycle.Bin", sid.Value)
        If Not Directory.Exists(bin_Dir) Then Return Nothing

        Dim records As New List(Of RecycleBinRecord)()
        For Each index_Path As String In Directory.EnumerateFiles(bin_Dir, "$I*")
            Try
                Dim parsed As RecycleBinRecord = RecycleBinIndex.TryParse(File.ReadAllBytes(index_Path), index_Path)
                If parsed IsNot Nothing Then records.Add(parsed)
            Catch
                ' A record the shell is writing this instant, or one this account cannot
                ' read. One unreadable neighbour must not cost the user the file they are
                ' actually asking for.
            End Try
        Next

        Return RecycleBinIndex.BestMatch(records, originalPath, deletedAtUtc)
    End Function

    ''' <summary>
    ''' Runs one shell call on a private STA thread and rethrows what it threw, with its
    ''' type and its stack intact - FinishFileOp shows that message to the user.
    '''
    ''' The file-operation queue consumes on a thread-pool thread (FileOpQueue.vb), which
    ''' is MTA by definition, and the shell's file-operation plumbing is STA-affine. One
    ''' thread per call costs microseconds against a shell call that costs milliseconds,
    ''' and it leaves the queue's single-consumer ordering exactly as it is - the
    ''' alternative, an STA consumer thread, would change the transport for every
    ''' operation to fix one of them.
    ''' </summary>
    Private Sub RunOnSta(work As Action)
        Dim failure As Exception = Nothing

        Dim worker As New Thread(Sub()
                                     Try
                                         work()
                                     Catch ex As Exception
                                         failure = ex
                                     End Try
                                 End Sub)
        worker.IsBackground = True
        worker.SetApartmentState(ApartmentState.STA)
        worker.Start()
        worker.Join()

        If failure IsNot Nothing Then ExceptionDispatchInfo.Capture(failure).Throw()
    End Sub

End Module
#End If
