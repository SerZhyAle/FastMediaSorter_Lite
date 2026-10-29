#If Not NETFRAMEWORK Then
Option Strict On
Option Explicit On

Imports System.IO
Imports System.Windows.Forms
Imports Xunit

''' <summary>
''' Ticket L0284: the help badge is laid over the top corner of a window (right, or left under right-to-left reading), so
''' a window gives that room away instead of running its content under the badge. The helpers are pure; the scan reads the
''' sources of the windows that were seen clipped (like the other scans of this suite - no window is built) and fails on
''' one that stops naming a helper.
''' </summary>
Public Class ContextHelpCornerTests

    <Fact>
    Public Sub The_badge_room_is_added_on_the_side_the_badge_takes()
        Dim basePadding As New Padding(10, 12, 10, 0)
        Dim leftToRight As Padding = ContextHelpUi.ClearOfBadge(basePadding, rtl:=False)
        Dim rightToLeft As Padding = ContextHelpUi.ClearOfBadge(basePadding, rtl:=True)
        Assert.Equal(New Padding(10, 12, 10 + ContextHelpUi.BadgeEdge, 0), leftToRight)
        Assert.Equal(New Padding(10 + ContextHelpUi.BadgeEdge, 12, 10, 0), rightToLeft)
    End Sub

    <Fact>
    Public Sub The_corner_band_is_a_top_strip_as_tall_as_the_badge()
        Using band As Panel = ContextHelpUi.CornerBand()
            Assert.Equal(DockStyle.Top, band.Dock)
            Assert.Equal(ContextHelpUi.BadgeEdge, band.Height)
            Assert.Equal("contextHelpBand", band.Name)
        End Using
    End Sub

    ''' <summary>Each window that was captured with a clipped or covered control, with the helper its source must still use.</summary>
    Private Shared ReadOnly SeenClipped As (File As String, Marker As String)() = {
        ("Resource_Manager_Form.vb", "ContextHelpUi.CornerBand"),
        ("Batch_Report_Form.vb", "ContextHelpUi.ClearOfBadge"),
        ("Exchange_Password_Change_Form.vb", "ContextHelpUi.BadgeEdge"),
        ("Image_Panel_Form.Modern.vb", "ContextHelpUi.BadgeEdge"),
        ("Image_Editor_Form.Tools.vb", "ContextHelpUi.BadgeEdge"),
        ("FastMediaSorterCompanion\Forms\Scheduled_Operation_Form.vb", "ContextHelpUi.BadgeEdge"),
        ("FastMediaSorterCompanion\Forms\Scheduled_History_Form.vb", "ContextHelpUi.CornerBand"),
        ("FastMediaSorterCompanion\Forms\Phone_Code_Intake_Form.vb", "ContextHelpUi.BadgeEdge"),
        ("FastMediaSorterCompanion\Forms\Exchange_Connection_String_Form.vb", "ContextHelpUi.BadgeEdge")
    }

    <Fact>
    Public Sub The_windows_seen_clipped_still_leave_the_badge_its_corner()
        Dim src As String = SourceRoot()
        For Each window In SeenClipped
            Dim text As String = File.ReadAllText(Path.Combine(src, window.File))
            Assert.True(text.Contains(window.Marker, StringComparison.Ordinal), window.File & " no longer uses " & window.Marker)
        Next
    End Sub

    Private Shared Function SourceRoot() As String
        Dim dir As New DirectoryInfo(AppContext.BaseDirectory)
        While dir IsNot Nothing
            Dim candidate As String = Path.Combine(dir.FullName, "src")
            If Directory.Exists(candidate) AndAlso File.Exists(Path.Combine(candidate, "Main_Form.vb")) Then Return candidate
            dir = dir.Parent
        End While
        Throw New DirectoryNotFoundException("The src folder was not found above " & AppContext.BaseDirectory)
    End Function

End Class
#End If
