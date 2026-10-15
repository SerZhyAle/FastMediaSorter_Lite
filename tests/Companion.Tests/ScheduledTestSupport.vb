Option Strict On

Imports System.IO
Imports System.Threading
Imports FastMediaSorterCompanion
Imports Microsoft.VisualBasic
Imports Xunit

''' <summary>
''' Ticket 222 test kit: a scratch folder tree that is deleted afterwards, an isolated settings hive, a manual
''' clock and a timer factory that records what was armed. Nothing here touches a real user folder - the
''' specification's rule for this suite (section 8).
''' </summary>
<CollectionDefinition(ScheduledCollection.Name, DisableParallelization:=True)>
Public Class ScheduledCollection
    Public Const Name As String = "ScheduledOperations"
End Class

''' <summary>A scratch directory under the user's temp folder, removed with it.</summary>
Friend NotInheritable Class ScratchTree
    Implements IDisposable

    Friend ReadOnly Root As String

    Friend Sub New()
        Root = Path.Combine(Path.GetTempPath(), "fms-sched-" & Guid.NewGuid().ToString("N"))
        Directory.CreateDirectory(Root)
    End Sub

    Friend Function Dir(ParamArray parts As String()) As String
        Dim full As String = Path.Combine(New String() {Root}.Concat(parts).ToArray())
        Directory.CreateDirectory(full)
        Return full
    End Function

    ''' <summary>Writes a file (and its folders) with the given text and, optionally, a write time.</summary>
    Friend Function Write(relative As String, Optional content As String = "x", Optional writtenLocal As DateTime? = Nothing) As String
        Dim full As String = Path.Combine(Root, relative)
        Directory.CreateDirectory(Path.GetDirectoryName(full))
        File.WriteAllText(full, content)
        If writtenLocal.HasValue Then File.SetLastWriteTime(full, writtenLocal.Value)
        Return full
    End Function

    Friend Function Read(relative As String) As String
        Return File.ReadAllText(Path.Combine(Root, relative))
    End Function

    Friend Function Exists(relative As String) As Boolean
        Return File.Exists(Path.Combine(Root, relative))
    End Function

    Public Sub Dispose() Implements IDisposable.Dispose
        Try
            ' A read-only file would stop the recursive delete; this is the tree's own business.
            For Each f As String In Directory.EnumerateFiles(Root, "*", SearchOption.AllDirectories)
                File.SetAttributes(f, FileAttributes.Normal)
            Next
            Directory.Delete(Root, True)
        Catch
        End Try
    End Sub

End Class

''' <summary>An isolated VB settings hive for the machine switches, deleted afterwards.</summary>
Friend NotInheritable Class ScratchHive
    Implements IDisposable

    Private ReadOnly previous As String
    Private ReadOnly scratch As String

    Friend Sub New()
        previous = CompanionGlobals.SettingsAppNameOverride
        scratch = "FmsSchedTest_" & Guid.NewGuid().ToString("N")
        CompanionGlobals.SettingsAppNameOverride = scratch
    End Sub

    Public Sub Dispose() Implements IDisposable.Dispose
        CompanionGlobals.SettingsAppNameOverride = previous
        Try : Interaction.DeleteSetting(scratch) : Catch : End Try
    End Sub

End Class

''' <summary>A clock that moves only when told to.</summary>
Friend NotInheritable Class ManualClock
    Friend Property Now As DateTime

    Friend Sub New(start As DateTime)
        Now = start
    End Sub

    Friend Function Read() As DateTime
        Return Now
    End Function
End Class

''' <summary>A timer factory that never fires on its own: a test calls <see cref="FireNext"/>.</summary>
Friend NotInheritable Class RecordingTimers
    Implements IScheduleTimerFactory

    Friend NotInheritable Class Armed
        Friend Due As TimeSpan
        Friend Callback As Action
        Friend Disposed As Boolean
    End Class

    Friend ReadOnly All As New List(Of Armed)()

    Public Function Start(due As TimeSpan, callback As Action) As IDisposable Implements IScheduleTimerFactory.Start
        Dim item As New Armed With {.Due = due, .Callback = callback}
        All.Add(item)
        Return New Handle(item)
    End Function

    Friend ReadOnly Property Live As List(Of Armed)
        Get
            Return All.Where(Function(a) Not a.Disposed).ToList()
        End Get
    End Property

    ''' <summary>Fires every live timer once (they are one-shot) and returns how many were fired.</summary>
    Friend Function FireLive() As Integer
        Dim live As List(Of Armed) = Me.Live
        For Each item As Armed In live
            item.Disposed = True
            item.Callback()
        Next
        Return live.Count
    End Function

    Private NotInheritable Class Handle
        Implements IDisposable
        Private ReadOnly item As Armed
        Friend Sub New(item As Armed)
            Me.item = item
        End Sub
        Public Sub Dispose() Implements IDisposable.Dispose
            item.Disposed = True
        End Sub
    End Class

End Class
