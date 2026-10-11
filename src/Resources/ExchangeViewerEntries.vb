#If Not NETFRAMEWORK Then
Option Strict On

' Ticket 187 T-3/T-4: the viewer's own entries as the rest of the viewer reaches them - one process-wide hub of sessions (empty and
' connection-free until something asks, 177 dual mode) and the one place that picks the sync of an attached resource.

Friend NotInheritable Class ExchangeViewerHub
    Private Sub New()
    End Sub

    Private Shared ReadOnly Gate As New Object()
    Private Shared _hub As ExchangeEntryHub

    ''' <summary>The hub of this run. Creating it opens nothing: a session connects only while a lease is held.</summary>
    Friend Shared Function Instance() As ExchangeEntryHub
        SyncLock Gate
            If _hub Is Nothing Then _hub = New ExchangeEntryHub(ExchangeConsumerPortsFactory.Create())
            Return _hub
        End SyncLock
    End Function

    ''' <summary>Closes every session (say `bye`) when the program ends.</summary>
    Friend Shared Sub Close()
        Dim h As ExchangeEntryHub
        SyncLock Gate
            h = _hub
            _hub = Nothing
        End SyncLock
        If h IsNot Nothing Then h.Dispose()
    End Sub
End Class

Friend NotInheritable Class ExchangeFollowSync
    Private Sub New()
    End Sub

    ''' <summary>The sync that follows the producer of this attached resource: through its own entry's session when it was attached through
    ''' one; the Share Manager's agent for a resource attached before entries existed; Nothing when its entry is gone (the resource then
    ''' keeps opening by its direct addresses, and its tunnel says "sign in again").</summary>
    Friend Shared Function [For](resource As ResourceDescriptor) As ExchangeSync
        If resource Is Nothing Then Return Nothing
        If String.IsNullOrEmpty(resource.ExchangeEntryId) Then Return ExchangeSync.CreateDefault()
        Dim entry As ExchangeEntry = ExchangeEntryStore.CreateDefault().Find(resource.ExchangeEntryId)
        If entry Is Nothing Then Return Nothing
        Return ExchangeSync.CreateFor(entry, New ExchangeEntryApi(ExchangeViewerHub.Instance().SessionFor(entry)))
    End Function
End Class
#End If
