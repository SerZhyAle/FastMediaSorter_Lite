Option Explicit On
Option Strict On
Option Infer On
Option Compare Binary

Imports System.Collections.Generic
Imports System.Drawing
Imports System.Linq
Imports System.Threading
Imports System.Windows.Forms
Imports FastMediaSorterCompanion
Imports Xunit

''' <summary>
''' L0326, WINDOWS-UI sections 5 and 9: opening or folding a group is ONE layout transaction, not a layout per row. The
''' settings-page form of <see cref="CollapsibleSection"/> (<c>ConfigureSettingsHeader</c>) holds its whole body and every
''' ancestor up to the form (<c>Expanded</c>: <c>SuspendLayout</c> on each, one <c>ResumeLayout(True)</c> on each at the end),
''' so a toggle that lays out a container twice - or lays out every row of a long body on its own - is the regression that
''' makes a Manager page stutter. The count is the <c>Layout</c> event of each control during one toggle, read after the
''' message queue has drained.
'''
''' What is asserted and why that number: the section, its held ancestors and its body each lay out AT MOST ONCE (they are
''' resumed once, by the code under test); a control inside the body at most TWICE (it is arranged once by its parent and
''' may be sized again by it - never a pass per sibling). The bounds are read from the code of <c>Expanded</c>; this check
''' belongs to the per-form run (<c>tools\Test-UiForms.ps1 -Forms CollapsibleSection</c>), never to the release.
''' </summary>
<Collection(UiLanguageCollection.Name), Trait("Kind", "UiForm"), Trait("Form", "CollapsibleSection")>
Public Class CollapsibleSectionToggleTests

    Private Const RowsInBody As Integer = 14

    Private Shared Sub OnUi(test As Action)
        Dim failure As Exception = Nothing
        Dim thread As New Thread(Sub()
                                     Try
                                         test()
                                     Catch ex As Exception
                                         failure = ex
                                     End Try
                                 End Sub)
        thread.SetApartmentState(ApartmentState.STA)
        thread.Start()
        Assert.True(thread.Join(TimeSpan.FromSeconds(60)), "The held-toggle check did not finish.")
        If failure IsNot Nothing Then Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(failure).Throw()
    End Sub

    Private Shared Sub Collect(root As Control, into As List(Of Control))
        into.Add(root)
        For Each child As Control In root.Controls
            Collect(child, into)
        Next
    End Sub

    ''' <summary>One layout counter per control of <paramref name="roots"/> and everything below them.</summary>
    Private Shared Function CountLayouts(roots As IEnumerable(Of Control)) As Dictionary(Of Control, Integer)
        Dim counts As New Dictionary(Of Control, Integer)()
        Dim all As New List(Of Control)()
        For Each root As Control In roots
            Collect(root, all)
        Next
        For Each control As Control In all
            If counts.ContainsKey(control) Then Continue For
            counts(control) = 0
            Dim captured As Control = control
            AddHandler control.Layout, Sub(sender As Object, e As LayoutEventArgs) counts(captured) += 1
        Next
        Return counts
    End Function

    Private Shared Sub ResetCounts(counts As Dictionary(Of Control, Integer))
        For Each key As Control In counts.Keys.ToList()
            counts(key) = 0
        Next
    End Sub

    Private Shared Function Describe(control As Control, counts As Dictionary(Of Control, Integer)) As String
        Return control.GetType().Name & "(" & control.Name & ")=" & counts(control).ToString(Globalization.CultureInfo.InvariantCulture)
    End Function

    <Fact>
    Public Sub Opening_and_folding_a_settings_group_lays_each_container_out_once()
        OnUi(Sub()
                 Using form As New Form With {.ClientSize = New Size(900, 700), .ShowInTaskbar = False,
                     .StartPosition = FormStartPosition.Manual, .Location = New Point(-5000, -5000)}
                     Dim page As New Panel With {.Name = "toggle_Page", .Dock = DockStyle.Fill}
                     Dim section As New CollapsibleSection("toggle", "Connection and security") With {.Name = "toggle_Section"}
                     section.ConfigureSettingsHeader()
                     For index As Integer = 1 To RowsInBody
                         Dim row As New CheckBox With {.Name = "toggle_Row" & index.ToString(Globalization.CultureInfo.InvariantCulture),
                             .Text = "Option " & index.ToString(Globalization.CultureInfo.InvariantCulture), .AutoSize = True}
                         section.AddBodyRow(row)
                     Next
                     Dim note As New Label With {.Name = "toggle_Note", .AutoSize = True, .Text = "A note that ends the group"}
                     section.AddBodyRow(note)
                     page.Controls.Add(section)
                     form.Controls.Add(page)
                     Dim toggles As Integer = 0
                     AddHandler section.ExpandedChanged, Sub() toggles += 1
                     form.Show()
                     Application.DoEvents()

                     Dim body As Control = section.Body
                     Dim held As New List(Of Control) From {section, page}
                     Dim counts As Dictionary(Of Control, Integer) = CountLayouts({page})

                     For Each target As Boolean In {True, False, True}
                         ResetCounts(counts)
                         Dim before As Integer = toggles
                         section.Expanded = target
                         Application.DoEvents()

                         Assert.Equal(before + 1, toggles)
                         Assert.Equal(target, body.Visible)
                         For Each ancestor As Control In held
                             Assert.True(counts(ancestor) <= 1,
                                         "A toggle to " & target & " laid a held container out more than once: " & Describe(ancestor, counts))
                         Next
                         Assert.True(counts(body) <= 1, "The body laid out more than once: " & Describe(body, counts))
                         Dim inside As New List(Of Control)()
                         For Each child As Control In body.Controls
                             Collect(child, inside)
                         Next
                         Dim tooMany As List(Of String) = inside.Where(Function(c) counts(c) > 2).Select(Function(c) Describe(c, counts)).ToList()
                         Assert.True(tooMany.Count = 0,
                                     "A control inside the body was laid out more than twice in one toggle: " & String.Join("; ", tooMany))
                     Next
                     form.Close()
                 End Using
             End Sub)
    End Sub

End Class
