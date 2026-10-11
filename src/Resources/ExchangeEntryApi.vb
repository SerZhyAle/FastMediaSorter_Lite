#If Not NETFRAMEWORK Then
Option Strict On

Imports System.Threading
Imports System.Threading.Tasks

' Ticket 187 T-3: the viewer's feed and sync ask <see cref="IExchangeAgentApi"/> - state, directory, access. For one of the viewer's own
' entries the answers come from its own session (ExchangeEntrySession, the shared library), not from the Share Manager's agent. This is
' the whole adapter: nothing else of the viewer knows which road an answer came by.

Friend NotInheritable Class ExchangeEntryApi
    Implements IExchangeAgentApi

    Private ReadOnly _session As ExchangeEntrySession

    Friend Sub New(session As ExchangeEntrySession)
        If session Is Nothing Then Throw New ArgumentNullException(NameOf(session))
        _session = session
    End Sub

    Friend ReadOnly Property Session As ExchangeEntrySession
        Get
            Return _session
        End Get
    End Property

    Public Function GetStateAsync(ct As CancellationToken) As Task(Of ExchangeAgentStateResult) Implements IExchangeAgentApi.GetStateAsync
        Return _session.GetStateAsync(ct)
    End Function

    Public Function GetDirectoryAsync(sinceRevision As Long, waitMs As Integer, ct As CancellationToken) As Task(Of ExchangeAgentDirectoryResult) Implements IExchangeAgentApi.GetDirectoryAsync
        Return _session.GetDirectoryAsync(sinceRevision, waitMs, ct)
    End Function

    Public Function GetAccessAsync(resourceId As String, ct As CancellationToken) As Task(Of ExchangeAgentAccessResult) Implements IExchangeAgentApi.GetAccessAsync
        Return _session.GetAccessAsync(resourceId, ct)
    End Function

End Class
#End If
