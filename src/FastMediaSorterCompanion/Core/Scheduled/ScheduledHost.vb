Option Explicit On
Option Strict On
Option Infer On
Option Compare Binary

Imports Microsoft.Win32

' The Share Manager's one scheduler (ticket 222, section 0): created lazily, started by the tray host, and
' read by the settings page and the tray menu. Nothing here is a service and nothing runs in the viewer -
' the engine lives and dies with this process, in the signed-in user's session.
'
' Reading <see cref="Engine"/> never starts it: a window opened in a test, or before the tray has run,
' sees an empty list and does no I/O. Only <see cref="Start"/> - called once, by the tray host - loads the
' stored operations and arms them.
Friend Module ScheduledHost

    Private ReadOnly Gate As New Object()
    Private _engine As ScheduledEngine
    Private _wired As Boolean

    Friend ReadOnly Property Engine As ScheduledEngine
        Get
            SyncLock Gate
                If _engine Is Nothing Then
                    Dim folder As String = ScheduledStore.DefaultFolder()
                    Dim journal As New ScheduledJournal(folder)
                    _engine = New ScheduledEngine(New ScheduledStore(folder), journal, ScheduledServices.ForProduct(journal))
                End If
                Return _engine
            End SyncLock
        End Get
    End Property

    ''' <summary>Loads the stored operations and arms them. Safe to call twice.</summary>
    Friend Sub Start()
        Try
            Engine.Start()
            WireSystemEvents()
        Catch ex As Exception
            AppFileLogger.LogException("Scheduled operations: start", ex)
        End Try
    End Sub

    ''' <summary>Stops the timers and cancels what is running. Called when the manager exits.</summary>
    Friend Sub Shutdown()
        SyncLock Gate
            If _wired Then
                _wired = False
                Try
                    RemoveHandler SystemEvents.PowerModeChanged, AddressOf OnPowerModeChanged
                    RemoveHandler SystemEvents.TimeChanged, AddressOf OnTimeChanged
                Catch
                End Try
            End If
            If _engine IsNot Nothing Then
                Try : _engine.Dispose() : Catch : End Try
            End If
        End SyncLock
    End Sub

    ''' <summary>A machine that slept through a slot, or a clock that was moved, must not move a run by
    ''' hours: the engine re-reads the clock and runs what is due.</summary>
    Private Sub WireSystemEvents()
        SyncLock Gate
            If _wired Then Return
            _wired = True
            AddHandler SystemEvents.PowerModeChanged, AddressOf OnPowerModeChanged
            AddHandler SystemEvents.TimeChanged, AddressOf OnTimeChanged
        End SyncLock
    End Sub

    Private Sub OnPowerModeChanged(sender As Object, e As PowerModeChangedEventArgs)
        If e.Mode = PowerModes.Resume Then Wake()
    End Sub

    Private Sub OnTimeChanged(sender As Object, e As EventArgs)
        Wake()
    End Sub

    Private Sub Wake()
        Try
            Dim engine As ScheduledEngine
            SyncLock Gate
                engine = _engine
            End SyncLock
            If engine IsNot Nothing Then engine.Wake()
        Catch ex As Exception
            AppFileLogger.LogException("Scheduled operations: wake", ex)
        End Try
    End Sub

End Module
