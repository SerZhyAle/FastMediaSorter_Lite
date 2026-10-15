#If Not NETFRAMEWORK Then
Option Strict On
Option Explicit On

Imports System.Collections.Generic
Imports System.IO
Imports System.Linq
Imports System.Text.RegularExpressions
Imports System.Threading
Imports System.Windows.Forms
Imports Xunit

''' <summary>
''' WINDOWS-UI section 5, amended 0.2: a page is changed with the layout of its subtree HELD and laid out ONCE
''' when it is complete, and a remembered scroll position is restored only after that layout has run. This is the
''' gate of the viewer's settings window for the two things a person does most there - switching a page and
''' toggling a group.
'''
''' Two halves, like the other gates of the settings window:
'''  * a runtime half on the one piece that can be built here, <see cref="SettingsCollapsibleGroup"/>: a toggle is
'''    one layout pass of the group and of the page it stands in, and its event is raised after that pass;
'''  * a source half on the window itself (<c>Table_Form</c> drags LibVLC and Tesseract in, so it is read, not
'''    built): the entry points of a page switch and of a group toggle hold the layout, lay out the SHOWN page
'''    only, and read positions after the layout. The built window is held to the same rules by the hand-run
'''    <c>tests/Integration/SettingsLayoutScenes</c> (layout-event counts, every control's bounds and rendered
'''    pixels compared with the unoptimized build).
''' </summary>
Public Class SettingsLayoutPassTests

    ' ------------------------------------------------------------------ runtime half ----

    ''' <summary>A HeldPage with three groups of eight rows each, in a window that is shown.</summary>
    Private NotInheritable Class HeldPage
        Public ReadOnly Form As New Form()
        Public ReadOnly Flow As New FlowLayoutPanel()
        Public ReadOnly Groups As New List(Of SettingsCollapsibleGroup)()
        Public FlowLayouts As Integer
        Public ReadOnly Trace As New List(Of String)()

        Public Sub New()
            Flow.Dock = DockStyle.Fill
            Flow.FlowDirection = FlowDirection.TopDown
            Flow.WrapContents = False
            Flow.AutoScroll = True
            Form.Controls.Add(Flow)
            Form.ClientSize = New System.Drawing.Size(500, 400)
            For index As Integer = 0 To 2
                Dim group As New SettingsCollapsibleGroup("page/g" & index.ToString(), 1.0F, System.Drawing.SystemFonts.MessageBoxFont)
                group.Width = 460
                group.Caption = "Group " & index.ToString()
                For row As Integer = 0 To 7
                    group.Body.Controls.Add(New Panel With {.Height = 40, .Width = 440})
                Next
                group.ArrangeContents()
                Flow.Controls.Add(group)
                Groups.Add(group)
            Next
            Form.Show()
            Application.DoEvents()
            AddHandler Flow.Layout,
                Sub(s As Object, e As LayoutEventArgs)
                    FlowLayouts += 1
                    Trace.Add(If(e.AffectedProperty, "(none)") & ":" & If(e.AffectedControl Is Nothing, "-", e.AffectedControl.Name))
                End Sub
        End Sub
    End Class

    Private Shared Sub OnStaThread(body As Action)
        Dim failure As Exception = Nothing
        Dim thread As New Thread(
            Sub()
                Try
                    body()
                Catch ex As Exception
                    failure = ex
                End Try
            End Sub)
        thread.SetApartmentState(ApartmentState.STA)
        thread.Start()
        Assert.True(thread.Join(TimeSpan.FromSeconds(20)), "The layout test did not finish.")
        If failure IsNot Nothing Then Throw failure
    End Sub

    ''' <summary>The layout passes one change cost: the page, the group and the group's body.</summary>
    Private Structure Passes
        Public Page As Integer
        Public Group As Integer
        Public Body As Integer
        Public ReadOnly Property Total As Integer
            Get
                Return Page + Group + Body
            End Get
        End Property
        Public Overrides Function ToString() As String
            Return "page " & Page & ", group " & Group & ", body " & Body
        End Function
    End Structure

    ''' <summary>Counts the passes <paramref name="change"/> costs the middle group of a fresh page.</summary>
    Private Shared Function Measure(change As Action(Of HeldPage, SettingsCollapsibleGroup)) As Passes
        Dim result As Passes = Nothing
        OnStaThread(
            Sub()
                Dim page As New HeldPage()
                Using page.Form
                    Dim group As SettingsCollapsibleGroup = page.Groups(1)
                    Dim groupLayouts As Integer = 0
                    Dim bodyLayouts As Integer = 0
                    AddHandler group.Layout, Sub() groupLayouts += 1
                    AddHandler group.Body.Layout, Sub() bodyLayouts += 1
                    page.FlowLayouts = 0
                    change(page, group)
                    result.Page = page.FlowLayouts
                    result.Group = groupLayouts
                    result.Body = bodyLayouts
                End Using
            End Sub)
        Return result
    End Function

    <Fact>
    Public Sub A_group_toggle_is_held_and_costs_fewer_passes_than_the_same_change_made_live()
        Dim held As Passes = Measure(Sub(page, group) group.Expanded = False)
        ' The change as the setter made it before: one property at a time, the layout live.
        Dim flags As System.Reflection.BindingFlags = System.Reflection.BindingFlags.Instance Or System.Reflection.BindingFlags.NonPublic
        Dim live As Passes = Measure(
            Sub(page, group)
                GetType(SettingsCollapsibleGroup).GetField("expandedValue", flags).SetValue(group, False)
                group.Body.Visible = False
                group.Caption = group.Caption
                GetType(SettingsCollapsibleGroup).GetMethod("RefreshDisclosure", flags).Invoke(group, Nothing)
                group.ArrangeContents()
            End Sub)
        Assert.True(held.Total < live.Total, "held: " & held.ToString() & " - live: " & live.ToString())
        ' The page: one pass for the new height and, at most, the one the scroll range of an auto-scrolling
        ' panel takes after it. The group and its body: each at most once.
        Assert.True(held.Page <= 2, "held: " & held.ToString())
        Assert.True(held.Group <= 1, "held: " & held.ToString())
        Assert.True(held.Body <= 1, "held: " & held.ToString())
    End Sub

    <Fact>
    Public Sub The_event_of_a_toggle_arrives_after_the_page_has_been_laid_out()
        OnStaThread(
            Sub()
                Dim page As New HeldPage()
                Using page.Form
                    Dim middle As SettingsCollapsibleGroup = page.Groups(1)
                    Dim below As SettingsCollapsibleGroup = page.Groups(2)
                    ' What a listener sees the moment the event arrives: the group below already stands directly
                    ' under the toggled one - the position a restored scroll is computed from.
                    Dim belowTopAtEvent As Integer = Integer.MinValue
                    Dim expectedAtEvent As Integer = Integer.MinValue
                    Dim raised As Integer = 0
                    AddHandler middle.ExpansionChanged,
                        Sub()
                            raised += 1
                            belowTopAtEvent = below.Top
                            expectedAtEvent = middle.Bottom + middle.Margin.Bottom + below.Margin.Top
                        End Sub

                    middle.Expanded = False
                    Assert.Equal(1, raised)
                    Assert.Equal(expectedAtEvent, belowTopAtEvent)
                    Assert.Equal(middle.Header.Height, middle.Height)

                    middle.Expanded = True
                    Assert.Equal(2, raised)
                    Assert.Equal(expectedAtEvent, belowTopAtEvent)
                    Assert.Equal(middle.Bottom + middle.Margin.Bottom + below.Margin.Top, below.Top)
                End Using
            End Sub)
    End Sub

    <Fact>
    Public Sub Expand_all_held_by_the_page_costs_no_more_than_the_scroll_range_settle()
        OnStaThread(
            Sub()
                Dim page As New HeldPage()
                Using page.Form
                    page.FlowLayouts = 0
                    page.Flow.SuspendLayout()
                    Try
                        For Each group As SettingsCollapsibleGroup In page.Groups
                            group.Expanded = False
                        Next
                    Finally
                        page.Flow.ResumeLayout(True)
                    End Try
                    ' Three groups changed, one pass for the page (and the one the scroll range takes).
                    Assert.True(page.FlowLayouts <= 2, "page layouts: " & String.Join(" ; ", page.Trace))
                    For Each group As SettingsCollapsibleGroup In page.Groups
                        Assert.False(group.Expanded)
                        Assert.Equal(group.Header.Height, group.Height)
                    Next
                End Using
            End Sub)
    End Sub

    ' ------------------------------------------------------------------ source half ----

    Private Shared Function SourceDir() As String
        Dim dir As New DirectoryInfo(AppContext.BaseDirectory)
        While dir IsNot Nothing
            Dim candidate As String = Path.Combine(dir.FullName, "src")
            If Directory.Exists(candidate) AndAlso File.Exists(Path.Combine(candidate, "Main_Form.vb")) Then Return candidate
            dir = dir.Parent
        End While
        Throw New DirectoryNotFoundException("src/ not found above " & AppContext.BaseDirectory)
    End Function

    Private Shared Function ReadSource(name As String) As String
        Return LocalizationCoverageTests.BlankComments(File.ReadAllText(Path.Combine(SourceDir(), name)))
    End Function

    Private Shared Function IndentOf(line As String) As Integer
        Return line.Length - line.TrimStart().Length
    End Function

    ''' <summary>The lines from the first one that matches <paramref name="startPattern"/> (after the line that
    ''' matches <paramref name="anchorPattern"/>, when one is given) to the first later line of the same indentation
    ''' that matches <paramref name="endPattern"/>. A nested lambda ends deeper, so it never closes the block.</summary>
    Friend Shared Function BlockOf(source As String, startPattern As String, endPattern As String, Optional anchorPattern As String = Nothing) As String
        Dim lines As String() = source.Split({vbCrLf, vbLf}, StringSplitOptions.None)
        Dim from As Integer = 0
        If anchorPattern IsNot Nothing Then
            from = Array.FindIndex(lines, Function(l) Regex.IsMatch(l, anchorPattern))
            If from < 0 Then Return Nothing
        End If
        Dim start As Integer = Array.FindIndex(lines, from, Function(l) Regex.IsMatch(l, startPattern))
        If start < 0 Then Return Nothing
        Dim indent As Integer = IndentOf(lines(start))
        For index As Integer = start + 1 To lines.Length - 1
            If lines(index).Trim().Length > 0 AndAlso IndentOf(lines(index)) = indent AndAlso Regex.IsMatch(lines(index), endPattern) Then
                Return String.Join(vbLf, lines.Skip(start).Take(index - start + 1))
            End If
        Next
        Return Nothing
    End Function

    Private Const AllPagesCall As String = "LayoutModernSettingsPages()"

    ''' <summary>The entry points of a page switch and of a group toggle, by name: each must lay out the shown
    ''' page only and read positions after it. Returns what breaks a rule, empty when none does.</summary>
    Friend Shared Function EntryPointFindings(groups As String, layout As String, discovery As String, group As String) As List(Of String)
        Dim findings As New List(Of String)()

        ' The page switch: the restore that runs after SelectedIndexChanged.
        Dim restore As String = BlockOf(groups, "^\s*Private Sub QueueRestoreSettingsContext\(\)", "^\s*End Sub")
        If restore Is Nothing Then
            findings.Add("QueueRestoreSettingsContext was not found")
        Else
            If restore.Contains(AllPagesCall) Then findings.Add("the page-switch restore re-measures every page (" & AllPagesCall & ")")
            Dim at As Integer = restore.IndexOf("LayoutModernSettingsPage(", StringComparison.Ordinal)
            Dim anchors As Integer = restore.IndexOf("ContextAnchors(", StringComparison.Ordinal)
            Dim scroll As Integer = restore.IndexOf("AutoScrollPosition =", StringComparison.Ordinal)
            If at < 0 Then findings.Add("the page-switch restore does not lay the shown page out")
            If at >= 0 AndAlso anchors >= 0 AndAlso anchors < at Then findings.Add("the page-switch restore reads anchors before the layout")
            If at >= 0 AndAlso scroll >= 0 AndAlso scroll < at Then findings.Add("the page-switch restore scrolls before the layout")
        End If

        ' The group toggle: the handler of ExpansionChanged.
        Dim toggled As String = BlockOf(groups, "^\s*Sub\(\)\s*$", "^\s*End Sub", "AddHandler group\.ExpansionChanged,")
        If toggled Is Nothing Then
            findings.Add("the ExpansionChanged handler was not found")
        Else
            If toggled.Contains(AllPagesCall) Then findings.Add("a group toggle re-measures every page (" & AllPagesCall & ")")
            Dim at As Integer = toggled.IndexOf("LayoutModernSettingsPage(", StringComparison.Ordinal)
            Dim headerAt As Integer = toggled.IndexOf("PointToScreen", StringComparison.Ordinal)
            Dim scroll As Integer = toggled.IndexOf("AutoScrollPosition =", StringComparison.Ordinal)
            If at < 0 Then findings.Add("a group toggle does not lay its page out")
            If at >= 0 AndAlso headerAt >= 0 AndAlso headerAt < at Then findings.Add("a group toggle reads the header position before the layout")
            If at >= 0 AndAlso scroll >= 0 AndAlso scroll < at Then findings.Add("a group toggle scrolls before the layout")
        End If

        ' Expand all / collapse all.
        Dim all As String = BlockOf(discovery, "^\s*Friend Sub SetPageGroupsExpanded\(", "^\s*End Sub")
        If all Is Nothing Then
            findings.Add("SetPageGroupsExpanded was not found")
        Else
            If all.Contains(AllPagesCall) Then findings.Add("expand all re-measures every page (" & AllPagesCall & ")")
            If Not all.Contains("SuspendLayout()") OrElse Not all.Contains("ResumeLayout(True)") Then findings.Add("expand all does not hold the page")
        End If

        ' A resize of the content (a page switch can change the header's height and come through here).
        Dim resized As String = BlockOf(layout, "^\s*Private Sub ModernSettingsContentResized\(", "^\s*End Sub")
        If resized Is Nothing Then
            findings.Add("ModernSettingsContentResized was not found")
        ElseIf resized.Contains(AllPagesCall) Then
            findings.Add("a content resize re-measures every page (" & AllPagesCall & ")")
        End If

        ' The page's own pass: every row sized with the page and the group bodies held, then released.
        Dim sized As String = BlockOf(layout, "^\s*Private Sub ResizeModernPageItems\(", "^\s*End Sub")
        If sized Is Nothing Then
            findings.Add("ResizeModernPageItems was not found")
        Else
            If Not sized.Contains("flow.SuspendLayout()") Then findings.Add("ResizeModernPageItems does not hold the page")
            If Not sized.Contains("Body.SuspendLayout()") Then findings.Add("ResizeModernPageItems does not hold the group bodies")
            If Not sized.Contains("flow.ResumeLayout(True)") Then findings.Add("ResizeModernPageItems does not lay the page out once at the end")
        End If

        ' The group's own setter: held, laid out, and only then the event.
        Dim setter As String = BlockOf(group, "^\s*Set\(value As Boolean\)", "^\s*End Set")
        If setter Is Nothing Then
            findings.Add("the Expanded setter was not found")
        Else
            Dim suspendAt As Integer = setter.IndexOf("SuspendLayout()", StringComparison.Ordinal)
            Dim resumeAt As Integer = setter.LastIndexOf("ResumeLayout(True)", StringComparison.Ordinal)
            Dim raisedAt As Integer = setter.LastIndexOf("RaiseEvent ExpansionChanged", StringComparison.Ordinal)
            If suspendAt < 0 OrElse resumeAt < 0 Then findings.Add("the Expanded setter does not hold and release its layout")
            If resumeAt >= 0 AndAlso raisedAt >= 0 AndAlso raisedAt < resumeAt Then findings.Add("ExpansionChanged is raised before the layout has run")
        End If
        Return findings
    End Function

    <Fact>
    Public Sub The_page_switch_and_the_group_toggle_hold_one_layout_pass_in_the_real_sources()
        Dim findings As List(Of String) = EntryPointFindings(
            ReadSource("Table_Form.SettingsGroups.vb"), ReadSource("Table_Form.ModernLayout.vb"),
            ReadSource("Table_Form.SettingsDiscovery.vb"),
            ReadSource("SettingsCollapsibleGroup.vb"))
        Assert.True(findings.Count = 0, "WINDOWS-UI section 5 (amended 0.2):" & vbLf & String.Join(vbLf, findings))
    End Sub

    ''' <summary>Every hold is released: a method that suspends a layout and has no ResumeLayout left a page
    ''' frozen - the window would show an empty page and no error. Counted per method over the settings sources.</summary>
    Friend Shared Function UnreleasedHolds(source As String, fileName As String) As List(Of String)
        Dim result As New List(Of String)()
        Dim lines As String() = source.Split({vbCrLf, vbLf}, StringSplitOptions.None)
        Dim methodStart As New Regex("^\s*(?:Private |Friend |Public |Protected )?(?:Shared )?(?:Overrides )?(?:Sub|Function)\s+(\w+)|^\s*(Set|Get)\(?")
        Dim name As String = "(file)"
        Dim suspends As Integer = 0
        Dim resumes As Integer = 0
        Dim flush As Action =
            Sub()
                If suspends > resumes Then result.Add(fileName & ": " & name & " suspends layout " & suspends & " time(s) and resumes " & resumes)
                suspends = 0
                resumes = 0
            End Sub
        For Each line As String In lines
            Dim m As Match = methodStart.Match(line)
            If m.Success AndAlso Not line.TrimStart().StartsWith("End ", StringComparison.Ordinal) Then
                ' A lambda is "Sub(" with no name: it belongs to the method that holds it.
                If m.Groups(1).Success Then
                    flush()
                    name = m.Groups(1).Value
                End If
            End If
            suspends += Regex.Matches(line, "\.SuspendLayout\(\)|^\s*SuspendLayout\(\)").Count
            resumes += Regex.Matches(line, "\.ResumeLayout\(|^\s*ResumeLayout\(").Count
        Next
        flush()
        Return result
    End Function

    <Fact>
    Public Sub Every_layout_hold_of_the_settings_window_is_released()
        Dim findings As New List(Of String)()
        Dim names As String() = {"Table_Form.ModernLayout.vb", "Table_Form.SettingsGroups.vb", "Table_Form.SettingsDiscovery.vb",
                                 "Table_Form.ExpandedSettings.vb", "SettingsCollapsibleGroup.vb", "Resource_List_Panel.vb"}
        Dim holds As Integer = 0
        For Each name As String In names
            Dim source As String = ReadSource(name)
            holds += Regex.Matches(source, "SuspendLayout\(\)").Count
            findings.AddRange(UnreleasedHolds(source, name))
        Next
        Assert.True(holds >= 8, "only " & holds & " holds found - the scan lost the sources")
        Assert.True(findings.Count = 0, String.Join(vbLf, findings))
    End Sub

    ' ------------------------------------------------------------- the scanners ----

    Private Const CleanGroups As String =
        "    Private Sub QueueRestoreSettingsContext()" & vbLf &
        "        BeginInvoke(New MethodInvoker(" & vbLf &
        "            Sub()" & vbLf &
        "                LayoutModernSettingsPage(PageFlow(page))" & vbLf &
        "                Dim anchors = ContextAnchors(page)" & vbLf &
        "                flow.AutoScrollPosition = New Point(0, top)" & vbLf &
        "            End Sub))" & vbLf &
        "    End Sub" & vbLf &
        "    Private Sub AddSettingsGroup()" & vbLf &
        "        AddHandler group.ExpansionChanged," & vbLf &
        "            Sub()" & vbLf &
        "                LayoutModernSettingsPage(PageFlow(page))" & vbLf &
        "                Dim top = group.Header.PointToScreen(Point.Empty).Y" & vbLf &
        "                flow.AutoScrollPosition = New Point(0, top)" & vbLf &
        "            End Sub" & vbLf &
        "    End Sub"

    Private Const CleanLayout As String =
        "    Private Sub ModernSettingsContentResized(sender As Object, e As EventArgs)" & vbLf &
        "        LayoutShownSettingsPage()" & vbLf &
        "    End Sub" & vbLf &
        "    Private Sub ResizeModernPageItems(flow As FlowLayoutPanel)" & vbLf &
        "        flow.SuspendLayout()" & vbLf &
        "        For Each group In held" & vbLf &
        "            group.Body.SuspendLayout()" & vbLf &
        "        Next" & vbLf &
        "        Try" & vbLf &
        "            SizeModernPageItems(flow)" & vbLf &
        "        Finally" & vbLf &
        "            flow.ResumeLayout(True)" & vbLf &
        "        End Try" & vbLf &
        "    End Sub"

    Private Const CleanDiscovery As String =
        "    Friend Sub SetPageGroupsExpanded(expanded As Boolean)" & vbLf &
        "        flow.SuspendLayout()" & vbLf &
        "        Try" & vbLf &
        "        Finally" & vbLf &
        "            flow.ResumeLayout(True)" & vbLf &
        "        End Try" & vbLf &
        "        LayoutModernSettingsPage(flow)" & vbLf &
        "    End Sub"

    Private Const CleanGroup As String =
        "        Set(value As Boolean)" & vbLf &
        "            SuspendLayout()" & vbLf &
        "            Try" & vbLf &
        "                expandedValue = value" & vbLf &
        "            Finally" & vbLf &
        "                ResumeLayout(True)" & vbLf &
        "            End Try" & vbLf &
        "            RaiseEvent ExpansionChanged(Me, EventArgs.Empty)" & vbLf &
        "        End Set"

    <Fact>
    Public Sub The_entry_point_scanner_passes_the_held_shape()
        Assert.Empty(EntryPointFindings(CleanGroups, CleanLayout, CleanDiscovery, CleanGroup))
    End Sub

    <Fact>
    Public Sub The_entry_point_scanner_finds_each_way_to_lose_the_pass()
        ' The toggle goes back to measuring every page.
        Dim everyPage As String = CleanGroups.Replace("LayoutModernSettingsPage(PageFlow(page))", "LayoutModernSettingsPages()")
        Assert.Contains(EntryPointFindings(everyPage, CleanLayout, CleanDiscovery, CleanGroup), Function(f) f.Contains("every page"))
        ' The restore scrolls from a layout that has not run yet.
        Dim early As String = CleanGroups.Replace(
            "                LayoutModernSettingsPage(PageFlow(page))" & vbLf & "                Dim anchors = ContextAnchors(page)" & vbLf & "                flow.AutoScrollPosition = New Point(0, top)",
            "                flow.AutoScrollPosition = New Point(0, top)" & vbLf & "                LayoutModernSettingsPage(PageFlow(page))" & vbLf & "                Dim anchors = ContextAnchors(page)")
        Assert.Contains(EntryPointFindings(early, CleanLayout, CleanDiscovery, CleanGroup), Function(f) f.Contains("scrolls before the layout"))
        ' Rows sized one by one with the page live.
        Dim live As String = CleanLayout.Replace("flow.SuspendLayout()", "Dim noop = 0")
        Assert.Contains(EntryPointFindings(CleanGroups, live, CleanDiscovery, CleanGroup), Function(f) f.Contains("does not hold the page"))
        ' The event is raised from inside the held region.
        Dim eventInside As String =
            "        Set(value As Boolean)" & vbLf &
            "            SuspendLayout()" & vbLf &
            "            Try" & vbLf &
            "                expandedValue = value" & vbLf &
            "                RaiseEvent ExpansionChanged(Me, EventArgs.Empty)" & vbLf &
            "            Finally" & vbLf &
            "                ResumeLayout(True)" & vbLf &
            "            End Try" & vbLf &
            "        End Set"
        Assert.Contains(EntryPointFindings(CleanGroups, CleanLayout, CleanDiscovery, eventInside), Function(f) f.Contains("before the layout has run"))
        ' A setter that never holds.
        Dim neverHeld As String =
            "        Set(value As Boolean)" & vbLf &
            "            expandedValue = value" & vbLf &
            "            RaiseEvent ExpansionChanged(Me, EventArgs.Empty)" & vbLf &
            "        End Set"
        Assert.Contains(EntryPointFindings(CleanGroups, CleanLayout, CleanDiscovery, neverHeld), Function(f) f.Contains("hold and release"))
        ' A content resize that measures every page again.
        Dim resizeAll As String = CleanLayout.Replace("LayoutShownSettingsPage()", "LayoutModernSettingsPages()")
        Assert.Contains(EntryPointFindings(CleanGroups, resizeAll, CleanDiscovery, CleanGroup), Function(f) f.Contains("content resize"))
    End Sub

    <Fact>
    Public Sub The_release_scanner_finds_a_hold_that_is_never_released()
        Dim held As String =
            "    Private Sub Fine()" & vbLf &
            "        flow.SuspendLayout()" & vbLf &
            "        flow.ResumeLayout(True)" & vbLf &
            "    End Sub"
        Assert.Empty(UnreleasedHolds(held, "Fixture.vb"))
        Dim frozen As String =
            "    Private Sub Fine()" & vbLf &
            "        flow.SuspendLayout()" & vbLf &
            "        flow.ResumeLayout(True)" & vbLf &
            "    End Sub" & vbLf &
            "    Private Sub Frozen()" & vbLf &
            "        flow.SuspendLayout()" & vbLf &
            "        DoSomething()" & vbLf &
            "    End Sub"
        Assert.Contains("Frozen", Assert.Single(UnreleasedHolds(frozen, "Fixture.vb")))
    End Sub

End Class
#End If
