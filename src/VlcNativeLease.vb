#If Not NETFRAMEWORK Then
Option Strict On

Imports System.Threading

''' <summary>
''' Who may still be inside the native LibVLC instance (ticket 115). The instance, its player
''' and every Media made from it are plain native objects: freeing the instance while a pool
''' thread is in a stop, or in a tag parse of a Media it made, destroys a lock under a running
''' thread - the access violation in the crash dumps, with no managed trace.
'''
''' The rule, in one sentence: native work is counted in and out, and the instance is disposed
''' only when the count is zero and nobody can start new work. Pure - no LibVLC, no WinForms -
''' so the ordering is a test, not a scene somebody has to act out.
''' </summary>
Friend NotInheritable Class VlcNativeLease

    Private ReadOnly gate As New Object()
    Private active As Integer
    Private closing As Boolean

    ''' <summary>Native work in flight (a stop on a pool thread, a tag parse).</summary>
    Friend ReadOnly Property ActiveCount As Integer
        Get
            SyncLock gate
                Return active
            End SyncLock
        End Get
    End Property

    Friend ReadOnly Property IsClosing As Boolean
        Get
            SyncLock gate
                Return closing
            End SyncLock
        End Get
    End Property

    ''' <summary>Count one piece of native work in. False once the instance is closing - the
    ''' caller must then not touch it at all.</summary>
    Friend Function TryEnter() As Boolean
        SyncLock gate
            If closing Then Return False
            active += 1
            Return True
        End SyncLock
    End Function

    ''' <summary>Count it out. Every successful <see cref="TryEnter"/> is paired with exactly one.</summary>
    Friend Sub Leave()
        SyncLock gate
            If active > 0 Then active -= 1
            If active = 0 Then Monitor.PulseAll(gate)
        End SyncLock
    End Sub

    ''' <summary>Refuse new work from now on, then wait up to <paramref name="timeoutMs"/> for
    ''' the work in flight. True = idle, the instance may be disposed. False = something is still
    ''' inside: leave the instance alone (the process is going away; a leak is not a crash).</summary>
    Friend Function CloseAndWaitForIdle(timeoutMs As Integer) As Boolean
        Dim deadline As Long = Environment.TickCount64 + Math.Max(0, timeoutMs)
        SyncLock gate
            closing = True
            While active > 0
                Dim left As Long = deadline - Environment.TickCount64
                If left <= 0 Then Return False
                Monitor.Wait(gate, CInt(Math.Min(left, Integer.MaxValue)))
            End While
            Return True
        End SyncLock
    End Function

End Class
#End If
