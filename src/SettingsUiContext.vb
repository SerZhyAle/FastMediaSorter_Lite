#If Not NETFRAMEWORK Then
Option Explicit On
Option Strict On
Option Infer On
Option Compare Binary

Imports System.Collections.Generic
Imports System.Linq
Imports System.Text.Json

''' <summary>Private modern-viewer navigation state, excluded from preference exchange (120).
''' Schema 2 (210): the Share Manager's left-menu expansion joined the per-page state as
''' <see cref="SettingsPageContext.NavExpanded"/>; the viewer's per-group fold map is
''' unchanged. A v1 payload migrates on read (210 section 7.2).</summary>
Friend NotInheritable Class SettingsUiContext
    Public Property SchemaVersion As Integer = 2
    Public Property LastPageId As String = "general"
    Public Property Pages As New Dictionary(Of String, SettingsPageContext)(StringComparer.Ordinal)

    Public Sub New()
    End Sub

    Friend Function Page(id As String) As SettingsPageContext
        If Not Pages.ContainsKey(id) Then Pages(id) = New SettingsPageContext()
        Return Pages(id)
    End Function

    Friend Function ResolvePage(available As IEnumerable(Of String)) As String
        Dim ids As String() = available.ToArray()
        If ids.Contains(LastPageId, StringComparer.Ordinal) Then Return LastPageId
        Return If(ids.Contains("general", StringComparer.Ordinal), "general", ids.FirstOrDefault())
    End Function

    ''' <summary>Merge the former sharing page while retaining independent group and focus state.</summary>
    Friend Sub MergeSharingIntoResources()
        Dim sharing As SettingsPageContext = Nothing
        If Pages.TryGetValue("sharing", sharing) Then
            Dim resources As SettingsPageContext = Page("resources")
            Dim expanded As Boolean
            If sharing.Groups.TryGetValue("basic", expanded) AndAlso Not resources.Groups.ContainsKey("server") Then
                resources.Groups("server") = expanded
            End If
            If LastPageId = "sharing" Then
                resources.AnchorId = If(sharing.AnchorId = "sharing/basic", "resources/server", sharing.AnchorId)
                resources.FocusId = If(sharing.FocusId = "sharing/basic", "resources/server", sharing.FocusId)
                resources.AnchorOffsetLogical = sharing.AnchorOffsetLogical
            End If
            Pages.Remove("sharing")
        End If
        If LastPageId = "sharing" Then LastPageId = "resources"
    End Sub

    Friend Function ToJson() As String
        Return JsonSerializer.Serialize(Me)
    End Function

    Friend Shared Function Read(json As String, ByRef invalid As Boolean) As SettingsUiContext
        invalid = False
        If String.IsNullOrWhiteSpace(json) Then Return New SettingsUiContext()
        Try
            If json.Length > 131072 Then Throw New JsonException("UI context exceeds its size limit.")
            Dim state As SettingsUiContext = JsonSerializer.Deserialize(Of SettingsUiContext)(json)
            If state Is Nothing OrElse state.Pages Is Nothing OrElse state.Pages.Count > 64 OrElse
               (state.SchemaVersion <> 1 AndAlso state.SchemaVersion <> 2) Then
                Throw New JsonException("Unsupported UI context shape.")
            End If
            ' 210 section 7.2: a v1 context migrates - a page the user worked on with any
            ' group open opens its item list in the new menu.
            If state.SchemaVersion = 1 Then
                For Each pageState As SettingsPageContext In state.Pages.Values
                    If pageState IsNot Nothing AndAlso pageState.Groups IsNot Nothing AndAlso pageState.Groups.ContainsValue(True) Then
                        pageState.NavExpanded = True
                    End If
                Next
                state.SchemaVersion = 2
            End If
            If state.LastPageId Is Nothing Then state.LastPageId = "general"
            For Each id As String In state.Pages.Keys.ToArray()
                Dim pageState As SettingsPageContext = state.Pages(id)
                If pageState Is Nothing OrElse pageState.Groups Is Nothing OrElse pageState.Groups.Count > 64 Then
                    state.Pages.Remove(id)
                    Continue For
                End If
                pageState.AnchorId = If(pageState.AnchorId, String.Empty)
                pageState.FocusId = If(pageState.FocusId, String.Empty)
                pageState.AnchorOffsetLogical = Math.Max(-4096, Math.Min(4096, pageState.AnchorOffsetLogical))
            Next
            Return state
        Catch ex As JsonException
            invalid = True
            Return New SettingsUiContext()
        End Try
    End Function
End Class

Friend NotInheritable Class SettingsPageContext
    ''' <summary>The Share Manager's left menu (210): whether the page's item list is expanded.
    ''' Untouched by the viewer, whose fold state stays in <see cref="Groups"/>.</summary>
    Public Property NavExpanded As Boolean
    Public Property Groups As New Dictionary(Of String, Boolean)(StringComparer.Ordinal)
    Public Property AnchorId As String = String.Empty
    Public Property AnchorOffsetLogical As Integer
    Public Property FocusId As String = String.Empty

    Public Sub New()
    End Sub

    Friend Function Expanded(groupId As String, defaultValue As Boolean) As Boolean
        Dim value As Boolean
        Return If(Groups.TryGetValue(groupId, value), value, defaultValue)
    End Function

    ''' <summary>Rows inside a saved-collapsed group resolve to its header; never expand on restore.</summary>
    Friend Function ResolveAnchor(visibleIds As IEnumerable(Of String), owningGroups As IReadOnlyDictionary(Of String, String)) As String
        Dim ids As String() = visibleIds.ToArray()
        If ids.Contains(AnchorId, StringComparer.Ordinal) Then Return AnchorId
        Dim groupId As String = Nothing
        If owningGroups.TryGetValue(AnchorId, groupId) AndAlso ids.Contains(groupId, StringComparer.Ordinal) Then Return groupId
        Return ids.FirstOrDefault()
    End Function
End Class
#End If
