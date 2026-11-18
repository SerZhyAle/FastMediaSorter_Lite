Option Explicit On
Option Strict On
Option Infer On
Option Compare Binary

''' <summary>
''' The two standing preferences of the live broadcast that the Manager settings page "Live broadcast" edits (ticket L0332,
''' SETTINGS-NAVIGATION 2.1 item A): "allow viewers on my network" (<see cref="LiveProducerSettings.AllowLanVideoViewers"/>) and
''' "show the cursor" (<see cref="LiveProducerSettings.ScreenCursor"/>). They used to be check boxes of the live broadcast
''' window; the window now keeps a link to the page and never writes either key.
'''
''' Where a preference is read: a start loads the whole <see cref="LiveProducerSettings"/> from the hive
''' (<c>LiveBroadcastHost.BuildPorts</c>, <c>LoadSettings</c> and the video-source factory), so a value saved here is the
''' value the next start uses. Where it is written: here only, always as RELOAD, change one field, save - the settings record
''' holds every other row of the window too (port, title, monitor..), and a stale copy of those must never be written back by
''' a change of one of these two.
''' </summary>
Friend NotInheritable Class LiveBroadcastOptions

    Private ReadOnly _read As Func(Of String, String)
    Private ReadOnly _write As Action(Of String, String)

    ''' <summary>The store is the two delegates the producer's settings record already speaks (<c>Load(read)</c>, <c>Save(write)</c>).</summary>
    Friend Sub New(read As Func(Of String, String), write As Action(Of String, String))
        If read Is Nothing Then Throw New ArgumentNullException(NameOf(read))
        If write Is Nothing Then Throw New ArgumentNullException(NameOf(write))
        _read = read
        _write = write
    End Sub

    ''' <summary>The Share Manager's own hive (<c>SZA\FastMediaSorter</c>), the one the controller reads at a start.</summary>
    Friend Shared Function ForHive() As LiveBroadcastOptions
        Return New LiveBroadcastOptions(
            Function(key) GetSetting(App_name, Second_App_Name, key, ""),
            Sub(key, value) SaveSetting(App_name, Second_App_Name, key, value))
    End Function

    ''' <summary>The stored settings as the next start would read them (a key that was never written reads as its default:
    ''' viewers on my network OFF, the cursor ON).</summary>
    Friend Function Load() As LiveProducerSettings
        Return LiveProducerSettings.Load(_read)
    End Function

    ''' <summary>Stores "show the cursor". The cursor is read when a screen source is made, so a change reaches the NEXT broadcast;
    ''' one already on air keeps the choice it started with.</summary>
    Friend Sub SetShowCursor(show As Boolean)
        Dim settings As LiveProducerSettings = Load()
        settings.ScreenCursor = show
        settings.Save(_write)
    End Sub

    ''' <summary>Stores "allow viewers on my network". While a broadcast runs the choice is applied to it FIRST (the HTTP listener is
    ''' rebound by <see cref="LiveBroadcastController.UpdateNetwork"/>) and is stored only when that succeeded: a refusal comes back
    ''' as the failed result, nothing is saved and the previous setting stays in force on air and on disk. With no broadcast
    ''' running it is only stored, and the next start uses it.</summary>
    Friend Function SetAllowLanViewers(allow As Boolean, controller As LiveBroadcastController) As LiveStartResult
        If controller Is Nothing OrElse Not controller.State.Running Then Return ApplyAllowLanViewers(allow, Nothing)
        Return ApplyAllowLanViewers(allow, Function(value) controller.UpdateNetwork(value))
    End Function

    ''' <summary>The rule of <see cref="SetAllowLanViewers"/> with the running broadcast reduced to the one call it needs: a function
    ''' that applies the choice to it (Nothing when none runs). The seam a test drives without building a controller.</summary>
    Friend Function ApplyAllowLanViewers(allow As Boolean, applyToRunning As Func(Of Boolean, LiveStartResult)) As LiveStartResult
        If applyToRunning IsNot Nothing Then
            Dim applied As LiveStartResult = applyToRunning(allow)
            If applied Is Nothing Then Return LiveStartResult.Failed(LiveStartRefusal.SourceUnavailable)
            If Not applied.Ok Then Return applied
        End If
        Dim settings As LiveProducerSettings = Load()
        settings.AllowLanVideoViewers = allow
        settings.Save(_write)
        Return New LiveStartResult With {.Ok = True}
    End Function

End Class
