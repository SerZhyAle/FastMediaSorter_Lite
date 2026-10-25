#If Not NETFRAMEWORK Then
Option Strict On

Imports System.Collections.Generic
Imports System.Linq

' The state of the file manager mode of the F3 panel that is NOT a control (257_SPECIFICATION_FILE_MANAGER_NAVIGATION_AND_PROVIDER_CONTEXT.md,
' slice N3): where the manager is standing, where Back and Forward lead, which entries are selected. Pure - no form, no provider, no
' clock - so the rule "a failed, denied or cancelled navigation keeps the prior listing and the history position" is a unit test.
' Modern build only.

''' <summary>Why a listing was asked for: it decides what a finished listing does to the history.</summary>
Friend Enum ManagerIntent
    ''' <summary>The first listing of a manager session; there is no history yet.</summary>
    Start
    ''' <summary>A folder entered, a place picked, an address typed.</summary>
    Go
    Back
    Forward
    Up
    ''' <summary>The same folder again: the history does not move, the selection is reconciled by identity.</summary>
    Refresh
End Enum

''' <summary>What a listing came to, in the words the user is told. Distinct on purpose (AC257-2): a denied folder, a share that is
''' gone and a listing the user cancelled never share a sentence, and none of them is "the folder is empty".</summary>
Friend Enum ManagerStatusKind
    ''' <summary>Read to its end. An empty folder is Ok with no entries - the empty state, not an error.</summary>
    Ok
    ''' <summary>A prefix of the folder: shown as incomplete, never a manifest for a bulk command.</summary>
    Truncated
    Missing
    NotADirectory
    Denied
    ''' <summary>The link did not answer: a share that went away, a server that dropped.</summary>
    Disconnected
    Cancelled
    ''' <summary>The path leaves the place's root, by its text or by the provider's canonical form.</summary>
    OutsideRoot
    ''' <summary>A resource with a stored credential that cannot be read, or none at all: the manager never asks for one.</summary>
    SignInNeeded
    ''' <summary>An unknown server key: the manager never asks for trust from a worker.</summary>
    HostKeyUnknown
    ''' <summary>The place is not in the resource list any more, or cannot be browsed.</summary>
    Unavailable
End Enum

''' <summary>The outcome of one listing, immutable, published whole. Carries entries only when it is a success.</summary>
Friend NotInheritable Class ManagerListingResult

    Friend Sub New(generation As Integer, intent As ManagerIntent, target As ManagerLocation, status As ManagerStatusKind,
                   entries As IReadOnlyList(Of ManagerEntry), failure As ResourceFailure)
        Me.Generation = generation
        Me.Intent = intent
        Me.Target = target
        Me.Status = status
        Me.Entries = If(entries, New List(Of ManagerEntry)())
        Me.Failure = failure
    End Sub

    ''' <summary>The browser's counter at the moment the listing began: a result older than the newest is stale and is dropped.</summary>
    Friend ReadOnly Property Generation As Integer
    Friend ReadOnly Property Intent As ManagerIntent
    Friend ReadOnly Property Target As ManagerLocation
    Friend ReadOnly Property Status As ManagerStatusKind
    Friend ReadOnly Property Entries As IReadOnlyList(Of ManagerEntry)
    Friend ReadOnly Property Failure As ResourceFailure

    ''' <summary>The directory was read (completely or as a prefix): the only results that may move the manager.</summary>
    Friend ReadOnly Property IsListed As Boolean
        Get
            Return Status = ManagerStatusKind.Ok OrElse Status = ManagerStatusKind.Truncated
        End Get
    End Property

    Friend ReadOnly Property IsTruncated As Boolean
        Get
            Return Status = ManagerStatusKind.Truncated
        End Get
    End Property
End Class

''' <summary>What applying a result did.</summary>
Friend Enum ManagerApply
    ''' <summary>Nothing changed: not a success, or the history moved under the navigation. The prior listing stays.</summary>
    Kept
    ''' <summary>The manager stands in a different folder now: the selection starts empty.</summary>
    Moved
    ''' <summary>The same folder read again: the selection is reconciled by identity.</summary>
    Reloaded
End Enum

Friend NotInheritable Class ManagerNavigation

    ''' <summary>The newest successful locations kept for Back (and, mirrored, for Forward). Not persisted, not exported.</summary>
    Friend Const MaxHistory As Integer = 100

    Private ReadOnly back_Stack As New List(Of ManagerLocation)()
    Private ReadOnly forward_Stack As New List(Of ManagerLocation)()
    Private current_Location As ManagerLocation
    Private refresh_Count As Integer
    Private ReadOnly selected_Ids As New HashSet(Of String)(StringComparer.Ordinal)
    Private focused_Id As String

    ''' <summary>The last location that listed successfully, or Nothing before the first.</summary>
    Friend ReadOnly Property Current As ManagerLocation
        Get
            Return current_Location
        End Get
    End Property

    ''' <summary>Counts the listings that were committed: a snapshot taken at one value is not the one at the next.</summary>
    Friend ReadOnly Property RefreshGeneration As Integer
        Get
            Return refresh_Count
        End Get
    End Property

    Friend ReadOnly Property BackCount As Integer
        Get
            Return back_Stack.Count
        End Get
    End Property

    Friend ReadOnly Property ForwardCount As Integer
        Get
            Return forward_Stack.Count
        End Get
    End Property

    Friend ReadOnly Property CanBack As Boolean
        Get
            Return back_Stack.Count > 0
        End Get
    End Property

    Friend ReadOnly Property CanForward As Boolean
        Get
            Return forward_Stack.Count > 0
        End Get
    End Property

    Friend ReadOnly Property CanGoUp As Boolean
        Get
            Return current_Location IsNot Nothing AndAlso current_Location.Parent() IsNot Nothing
        End Get
    End Property

    ''' <summary>Where Back would go, without going: the history moves only when that listing succeeds.</summary>
    Friend Function PeekBack() As ManagerLocation
        Return If(back_Stack.Count = 0, Nothing, back_Stack(back_Stack.Count - 1))
    End Function

    Friend Function PeekForward() As ManagerLocation
        Return If(forward_Stack.Count = 0, Nothing, forward_Stack(forward_Stack.Count - 1))
    End Function

    ''' <summary>The folder Up would list, or Nothing at the place's root.</summary>
    Friend Function ParentTarget() As ManagerLocation
        Return If(current_Location Is Nothing, Nothing, current_Location.Parent())
    End Function

    ''' <summary>
    ''' The one door through which a finished listing reaches the state. A result that is not a success - denied, missing, cancelled,
    ''' disconnected, outside the root - changes nothing at all: not the location, not either stack, not the selection. A success
    ''' commits by its intent. Back and Forward commit only if the stack still leads where the listing went (a stale listing is
    ''' dropped by the browser's generation first; this is the belt for the braces).
    ''' </summary>
    Friend Function Apply(result As ManagerListingResult) As ManagerApply
        If result Is Nothing OrElse Not result.IsListed OrElse result.Target Is Nothing Then Return ManagerApply.Kept
        Dim target As ManagerLocation = result.Target

        Select Case result.Intent
            Case ManagerIntent.Start
                back_Stack.Clear()
                forward_Stack.Clear()
                current_Location = target
                ClearSelection()

            Case ManagerIntent.Back
                If back_Stack.Count = 0 OrElse Not back_Stack(back_Stack.Count - 1).SameAs(target) Then Return ManagerApply.Kept
                back_Stack.RemoveAt(back_Stack.Count - 1)
                If current_Location IsNot Nothing Then Push(forward_Stack, current_Location)
                current_Location = target
                ClearSelection()

            Case ManagerIntent.Forward
                If forward_Stack.Count = 0 OrElse Not forward_Stack(forward_Stack.Count - 1).SameAs(target) Then Return ManagerApply.Kept
                forward_Stack.RemoveAt(forward_Stack.Count - 1)
                If current_Location IsNot Nothing Then Push(back_Stack, current_Location)
                current_Location = target
                ClearSelection()

            Case ManagerIntent.Refresh
                ' A refresh of a folder the manager has since left says nothing about the one it is in.
                If current_Location Is Nothing OrElse Not current_Location.SameAs(target) Then Return ManagerApply.Kept
                refresh_Count += 1
                Return ManagerApply.Reloaded

            Case Else  ' Go, Up
                If current_Location IsNot Nothing AndAlso current_Location.SameAs(target) Then
                    ' The folder it is already in (an address retyped): not a new history entry.
                    refresh_Count += 1
                    Return ManagerApply.Reloaded
                End If
                If current_Location IsNot Nothing Then Push(back_Stack, current_Location)
                forward_Stack.Clear()
                current_Location = target
                ClearSelection()
        End Select

        refresh_Count += 1
        Return ManagerApply.Moved
    End Function

    Private Shared Sub Push(stack As List(Of ManagerLocation), location As ManagerLocation)
        If stack.Count > 0 AndAlso stack(stack.Count - 1).SameAs(location) Then Return
        stack.Add(location)
        While stack.Count > MaxHistory
            stack.RemoveAt(0)
        End While
    End Sub

    ' ------------------------------------------------------------------ selection ----

    Friend ReadOnly Property SelectedIds As IReadOnlyCollection(Of String)
        Get
            Return selected_Ids
        End Get
    End Property

    Friend ReadOnly Property FocusedId As String
        Get
            Return focused_Id
        End Get
    End Property

    ''' <summary>What the view holds right now, remembered so a refresh can put back what survives.</summary>
    Friend Sub RememberSelection(ids As IEnumerable(Of String), focusedEntryId As String)
        selected_Ids.Clear()
        If ids IsNot Nothing Then
            For Each id As String In ids
                If Not String.IsNullOrEmpty(id) Then selected_Ids.Add(id)
            Next
        End If
        focused_Id = focusedEntryId
    End Sub

    Friend Sub ClearSelection()
        selected_Ids.Clear()
        focused_Id = Nothing
    End Sub

    ''' <summary>Refresh reconciles by identity: what is still listed stays selected, what is gone leaves, and nothing new is ever
    ''' selected. Returns the surviving selection; the focus is kept only if its entry survives.</summary>
    Friend Function Reconcile(listedIds As IEnumerable(Of String)) As List(Of String)
        Dim present As New HashSet(Of String)(If(listedIds, Enumerable.Empty(Of String)()), StringComparer.Ordinal)
        Dim kept As List(Of String) = selected_Ids.Where(Function(id) present.Contains(id)).ToList()
        selected_Ids.Clear()
        For Each id As String In kept
            selected_Ids.Add(id)
        Next
        If focused_Id IsNot Nothing AndAlso Not present.Contains(focused_Id) Then focused_Id = Nothing
        Return kept
    End Function
End Class
#End If
