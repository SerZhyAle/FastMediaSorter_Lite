Option Explicit On
Option Strict On
Option Infer On
Option Compare Binary

Imports System.IO
Imports System.Linq
Imports System.Text
Imports System.Text.Json.Nodes
Imports Microsoft.VisualBasic

' The store of scheduled operations (ticket 222, section 4 "Storage"): one JSON file per operation under
' the user's own %LOCALAPPDATA%\SZA\FastMediaSorter\ tree - writable inside the MSIX package - and the
' machine-level switches in the shared SZA\FastMediaSorter hive.
'
' The worker's data directory (%LOCALAPPDATA%\FastMediaSorterCompanion\) is NEVER used or touched: it
' holds the host key every paired phone has pinned.
'
' TWO WRITERS, ONE FILE. The editor changes the DEFINITION on the UI thread; the run changes the RUN
' STATE (the record, the halt) on a pool thread. A whole-object write from either would silently undo
' the other's, so each writes only its own half: the definition is saved with the run state read back
' from disk, and the run state is patched into the stored document in place - which also means a file
' written by a newer build (a field, an action this one does not know) is never rewritten into
' something it did not say.
Friend NotInheritable Class ScheduledStore

    Private ReadOnly folder As String
    Private ReadOnly gate As New Object()

    Friend Sub New(folder As String)
        Me.folder = folder
    End Sub

    ''' <summary>%LOCALAPPDATA%\SZA\FastMediaSorter\scheduled-operations.</summary>
    Friend Shared Function DefaultFolder() As String
        Return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                            "SZA", "FastMediaSorter", "scheduled-operations")
    End Function

    Friend ReadOnly Property FolderPath As String
        Get
            Return folder
        End Get
    End Property

    Private Function FileFor(id As String) As String
        ' An id is a GUID this program wrote; anything else could name a path outside the folder.
        Dim safe As String = New String(id.Where(Function(c) Char.IsLetterOrDigit(c) OrElse c = "-"c).ToArray())
        If safe.Length = 0 OrElse safe <> id Then Throw New ArgumentException("Not an operation id.", NameOf(id))
        Return Path.Combine(folder, safe & ".json")
    End Function

    ''' <summary>Every readable operation, oldest first. A file that cannot be read is logged and skipped:
    ''' one damaged definition must not hide the rest.</summary>
    Friend Function LoadAll() As List(Of ScheduledOperation)
        Dim result As New List(Of ScheduledOperation)()
        SyncLock gate
            If Not Directory.Exists(folder) Then Return result
            For Each file As String In Directory.EnumerateFiles(folder, "*.json")
                Try
                    Dim op As ScheduledOperation = ScheduledJson.Deserialize(System.IO.File.ReadAllText(file, Encoding.UTF8))
                    If op Is Nothing Then
                        AppFileLogger.WriteLine("Scheduled operations: unreadable definition skipped: " & Path.GetFileName(file))
                    Else
                        result.Add(op)
                    End If
                Catch ex As Exception
                    AppFileLogger.LogException("Scheduled operations: reading " & Path.GetFileName(file), ex)
                End Try
            Next
        End SyncLock
        result.Sort(Function(a, b) a.CreatedAt.CompareTo(b.CreatedAt))
        Return result
    End Function

    Friend Function TryLoad(id As String) As ScheduledOperation
        SyncLock gate
            Try
                Dim file As String = FileFor(id)
                If Not System.IO.File.Exists(file) Then Return Nothing
                Return ScheduledJson.Deserialize(System.IO.File.ReadAllText(file, Encoding.UTF8))
            Catch ex As Exception
                AppFileLogger.LogException("Scheduled operations: loading " & id, ex)
                Return Nothing
            End Try
        End SyncLock
    End Function

    ''' <summary>
    ''' Writes the definition. The run state (record, halt) is read back from disk when the operation is
    ''' already stored, so an edit never undoes what a run wrote in between - except that an edit is the
    ''' user fixing the operation: <paramref name="clearHalt"/> lifts a wall.
    ''' </summary>
    Friend Sub SaveDefinition(op As ScheduledOperation, Optional clearHalt As Boolean = False)
        SyncLock gate
            Directory.CreateDirectory(folder)
            Dim file As String = FileFor(op.Id)
            Dim toWrite As ScheduledOperation = op.Clone()
            If System.IO.File.Exists(file) Then
                Dim stored As ScheduledOperation = ScheduledJson.Deserialize(System.IO.File.ReadAllText(file, Encoding.UTF8))
                If stored IsNot Nothing Then
                    toWrite.Record = stored.Record
                    If Not clearHalt Then
                        toWrite.Halt = stored.Halt
                        toWrite.HaltDetail = stored.HaltDetail
                    End If
                End If
            End If
            If clearHalt Then
                toWrite.Halt = ScheduledHalt.None
                toWrite.HaltDetail = ""
            End If
            WriteAtomic(file, ScheduledJson.Serialize(toWrite))
        End SyncLock
    End Sub

    ''' <summary>Patches the run state into the stored document, leaving every other field as written.
    ''' Returns False when the operation is no longer stored (deleted while it ran).</summary>
    Friend Function SaveRunState(id As String, record As ScheduledRunRecord, halt As ScheduledHalt, haltDetail As String) As Boolean
        SyncLock gate
            Dim file As String = FileFor(id)
            If Not System.IO.File.Exists(file) Then Return False
            Dim node As JsonNode = JsonNode.Parse(System.IO.File.ReadAllText(file, Encoding.UTF8))
            Dim root As JsonObject = TryCast(node, JsonObject)
            If root Is Nothing Then Return False
            root("record") = JsonNode.Parse(ScheduledJson.SerializeRecord(record))
            ' The halt of a definition this build cannot read stays as its writer left it.
            If Not (root.ContainsKey("action") AndAlso Not IsKnownAction(root("action"))) Then
                root("halt") = ScheduledJson.HaltText(halt)
                root("haltDetail") = If(haltDetail, "")
            End If
            WriteAtomic(file, root.ToJsonString(New System.Text.Json.JsonSerializerOptions With {.WriteIndented = True}))
            Return True
        End SyncLock
    End Function

    Private Shared Function IsKnownAction(node As JsonNode) As Boolean
        Dim text As String = Nothing
        Try
            text = node?.GetValue(Of String)()
        Catch
        End Try
        Dim action As ScheduledAction
        Return ScheduledJson.TryParseAction(text, action)
    End Function

    Friend Sub Delete(id As String)
        SyncLock gate
            Dim file As String = FileFor(id)
            If System.IO.File.Exists(file) Then System.IO.File.Delete(file)
        End SyncLock
    End Sub

    ''' <summary>tmp + swap: no failure leaves half a definition (the same rule as the image writer).</summary>
    Private Shared Sub WriteAtomic(target As String, text As String)
        Dim temp As String = target & ".tmp"
        System.IO.File.WriteAllText(temp, text, New UTF8Encoding(False))
        If System.IO.File.Exists(target) Then
            System.IO.File.Replace(temp, target, Nothing)
        Else
            System.IO.File.Move(temp, target)
        End If
    End Sub

End Class

''' <summary>
''' The machine-level switches of 4.4: this PC, not one operation. Kept in the shared hive, each read
''' fresh, each written the moment it changes - so the tray, the page and a second window agree and a
''' restart changes nothing.
''' </summary>
Friend NotInheritable Class ScheduledSettings

    Private Const FeatureKey As String = "Scheduled_FeatureEnabled"
    Private Const PausedKey As String = "Scheduled_Paused"
    Private Const MissedKey As String = "Scheduled_MissedRuns"
    ''' <summary>The viewer's own setting, read - never written - here (D3): the bin, where one exists.</summary>
    Private Const DeleteToBinKey As String = "DeleteToRecycleBin"

    ''' <summary>Master switch. Follows list emptiness: switched on when an operation is added and off
    ''' when the list becomes empty (<see cref="FollowList"/>); the user may also switch it off while
    ''' operations exist - the timers are then not armed.</summary>
    Friend Shared Property FeatureEnabled As Boolean
        Get
            Return Read(FeatureKey, "1") = "1"
        End Get
        Set(value As Boolean)
            Write(FeatureKey, If(value, "1", "0"))
        End Set
    End Property

    ''' <summary>Durable Pause all: no scheduled run and no Run all now while set; nothing is deleted.</summary>
    Friend Shared Property Paused As Boolean
        Get
            Return Read(PausedKey, "0") = "1"
        End Get
        Set(value As Boolean)
            Write(PausedKey, If(value, "1", "0"))
        End Set
    End Property

    ''' <summary>What to do with a slot that came due while the program was not running. The default is
    ''' "run once at start" (12.2 default 2): the host starts at sign-in, not around the clock.</summary>
    Friend Shared Property MissedRuns As MissedRunPolicy
        Get
            Return If(Read(MissedKey, "start") = "skip", MissedRunPolicy.Skip, MissedRunPolicy.RunOnceAtStart)
        End Get
        Set(value As MissedRunPolicy)
            Write(MissedKey, If(value = MissedRunPolicy.Skip, "skip", "start"))
        End Set
    End Property

    ''' <summary>The viewer's global "delete to the Recycle Bin" preference, default on, as the viewer
    ''' defaults it. Read at EVERY run (D3): a later change of the setting changes later runs.</summary>
    Friend Shared ReadOnly Property DeleteToRecycleBin As Boolean
        Get
            Return Read(DeleteToBinKey, "1") <> "0"
        End Get
    End Property

    ''' <summary>The feature switch follows the list: on with the first operation, off with the last.</summary>
    Friend Shared Sub FollowList(operationCount As Integer)
        Dim want As Boolean = operationCount > 0
        If FeatureEnabled <> want Then FeatureEnabled = want
    End Sub

    Private Shared Function Read(key As String, fallback As String) As String
        Try
            Return GetSetting(App_name, Second_App_Name, key, fallback)
        Catch
            Return fallback
        End Try
    End Function

    Private Shared Sub Write(key As String, value As String)
        Try
            SaveSetting(App_name, Second_App_Name, key, value)
        Catch ex As Exception
            AppFileLogger.LogException("Scheduled operations: writing " & key, ex)
        End Try
    End Sub

End Class
