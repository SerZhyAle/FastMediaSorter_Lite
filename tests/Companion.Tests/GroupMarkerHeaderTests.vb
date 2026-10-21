Option Explicit On
Option Strict On
Option Infer On
Option Compare Binary

Imports System.Collections.Generic
Imports System.Drawing
Imports System.IO
Imports System.Reflection
Imports System.Runtime.InteropServices
Imports System.Threading
Imports System.Windows.Forms
Imports FastMediaSorterCompanion
Imports Xunit

''' <summary>
''' COLLAPSIBLE-GROUP 1.1 section 3 in the Share Manager (ticket 243, T-11c-a): the two collapsible header kinds
''' of the Manager - the group card of the settings pages (<c>SettingsGroupCard</c>) and the section header of the
''' main window and of the pages (<c>CollapsibleSection</c>, in its single-line and its wrapping form) - carry the
''' FILLED STATE TRIANGLE the viewer's settings groups carry: at the reading start, right when collapsed, down when
''' open, left when collapsed under RTL, in the caption's own ink, tier 16 scaled with the window, 8 logical px from
''' the caption, which wraps instead of running under it. The whole header stays the one toggle.
''' Real controls, rendered with <c>DrawToBitmap</c> on a known background and read pixel by pixel; the palette is the
''' real one, set the way <c>ThemeReopenTests</c> sets it (same collection, so nothing else reads it meanwhile).
''' What stays manual: the look on a physical 150/200 percent monitor (the factor is named through the test seam
''' <c>SetMarkerScale</c>), under a live high-contrast switch and a real pointer, and Narrator.
''' </summary>
<Collection("Theme service state")>
Public Class GroupMarkerHeaderTests

    Private Const Flags As BindingFlags = BindingFlags.Instance Or BindingFlags.Public Or BindingFlags.NonPublic
    Private Const WM_LBUTTONDOWN As UInteger = &H201UI
    Private Const WM_LBUTTONUP As UInteger = &H202UI
    Private Const MK_LBUTTON As Integer = 1

    ''' <summary>A pixel is ink when its three channels differ from the background by at least this much in sum.</summary>
    Private Const InkDistance As Integer = 150

    <DllImport("user32.dll")>
    Private Shared Function SendMessage(handle As IntPtr, message As UInteger, wParam As IntPtr, lParam As IntPtr) As IntPtr
    End Function

    Private Shared ReadOnly Kinds As String() = {"card", "section", "section-wrap"}
    Private Const LongRussian As String = "Дополнительные параметры подключения и безопасности сервера обмена файлами"
    Private Const LongGerman As String = "Erweiterte Verbindungs- und Sicherheitseinstellungen des Dateiaustauschservers"

    ' --- harness ------------------------------------------------------------------------------------

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
        Assert.True(thread.Join(TimeSpan.FromSeconds(60)), "The group marker check did not finish.")
        If failure IsNot Nothing Then Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(failure).Throw()
    End Sub

    ''' <summary>One header of a kind in a window of a known background, with its container's toggle counted.</summary>
    Private NotInheritable Class Rig
        Implements IDisposable

        Public ReadOnly Form As New Form()
        Public Header As Control
        Public Background As Color
        Public Ink As Color
        Public Toggles As Integer
        Public IsExpanded As Func(Of Boolean)
        Public Kind As String
        Public Factor As Single
        Public SavedPalette As Object
        Private ReadOnly paletteField As FieldInfo = GetType(ThemeService).GetField("_palette", BindingFlags.Static Or BindingFlags.NonPublic)
        Private ReadOnly font As Font

        Public Sub New(factor As Single)
            SavedPalette = paletteField.GetValue(Nothing)
            Me.Factor = factor
            font = New Font("Segoe UI", 9.0F * factor)
        End Sub

        Public Sub SetPalette(palette As ThemePalette)
            paletteField.SetValue(Nothing, palette)
        End Sub

        Public Sub ApplyFont()
            Form.Font = font
        End Sub

        Public Sub Dispose() Implements IDisposable.Dispose
            Form.Dispose()
            font.Dispose()
            paletteField.SetValue(Nothing, SavedPalette)
        End Sub
    End Class

    Private Shared Function HeaderOf(section As CollapsibleSection) As Control
        Return DirectCast(GetType(CollapsibleSection).GetField("_header", Flags).GetValue(section), Control)
    End Function

    Private Shared Sub SetScale(header As Control, factor As Single)
        header.GetType().GetMethod("SetMarkerScale", Flags).Invoke(header, New Object() {factor})
    End Sub

    Private Shared Function CellOf(header As Control) As Rectangle
        Return DirectCast(header.GetType().GetMethod("MarkerBounds", Flags).Invoke(header, Nothing), Rectangle)
    End Function

    Private Shared Sub Press(header As Control, key As Keys)
        header.GetType().GetMethod("OnKeyDown", Flags).Invoke(header, New Object() {New KeyEventArgs(key)})
    End Sub

    ''' <summary>A header of <paramref name="kind"/> in a shown, off-screen window <paramref name="width"/> wide.</summary>
    Private Shared Function Build(kind As String, caption As String, expanded As Boolean, rtl As Boolean,
                                  dark As Boolean, factor As Single, width As Integer) As Rig
        Dim rig As New Rig(factor) With {.Kind = kind}
        Dim palette As ThemePalette = ThemePalette.ForDark(dark)
        rig.SetPalette(palette)
        Dim form As Form = rig.Form
        form.FormBorderStyle = FormBorderStyle.None
        form.ShowInTaskbar = False
        form.StartPosition = FormStartPosition.Manual
        form.SetBounds(-20000, 0, width, 360)
        If rtl Then form.RightToLeft = RightToLeft.Yes
        rig.ApplyFont()
        ' Keeps the focus off the header, so no focus rectangle is drawn into the picture.
        Dim holder As New TextBox() With {.Dock = DockStyle.Bottom, .TabIndex = 0}
        Select Case kind
            Case "card"
                Dim card As New SettingsGroupCard(caption, expanded)
                card.Body.ResumeLayout(False)
                card.ResumeLayout(False)
                AddHandler card.ExpandedChanged, Sub() rig.Toggles += 1
                rig.IsExpanded = Function() card.Expanded
                rig.Header = card.Header
                rig.Background = SettingsGroupCard.HeaderFill(palette)
                rig.Ink = ThemePalette.ReadableText(palette.TextPrimary, rig.Background)
                form.Controls.Add(card)
            Case Else
                form.BackColor = If(dark, Color.Black, Color.White)
                Dim section As New CollapsibleSection("test", caption)
                If kind = "section-wrap" Then section.ConfigureSettingsHeader() Else section.Dock = DockStyle.Top
                section.Expanded = expanded
                AddHandler section.ExpandedChanged, Sub() rig.Toggles += 1
                rig.IsExpanded = Function() section.Expanded
                rig.Header = HeaderOf(section)
                rig.Background = form.BackColor
                rig.Ink = palette.TextPrimary
                form.Controls.Add(section)
        End Select
        form.Controls.Add(holder)
        SetScale(rig.Header, factor)
        form.ActiveControl = holder
        form.Show()
        Application.DoEvents()
        ' Let the header size itself for the factor and the width, exactly as it does when the window opens.
        rig.ApplyFont()
        form.PerformLayout()
        Application.DoEvents()
        rig.Toggles = 0
        Return rig
    End Function

    Private Shared Function Render(header As Control) As Bitmap
        Dim bitmap As New Bitmap(header.Width, header.Height)
        header.DrawToBitmap(bitmap, New Rectangle(0, 0, bitmap.Width, bitmap.Height))
        Return bitmap
    End Function

    Private Shared Function Distance(a As Color, b As Color) As Integer
        Return Math.Abs(CInt(a.R) - CInt(b.R)) + Math.Abs(CInt(a.G) - CInt(b.G)) + Math.Abs(CInt(a.B) - CInt(b.B))
    End Function

    Private Shared Function IsInk(c As Color, background As Color) As Boolean
        Return Distance(c, background) >= InkDistance
    End Function

    Private Shared Sub Click(rig As Rig, x As Integer, y As Integer)
        Dim position As New IntPtr((y << 16) Or (x And &HFFFF))
        SendMessage(rig.Header.Handle, WM_LBUTTONDOWN, New IntPtr(MK_LBUTTON), position)
        SendMessage(rig.Header.Handle, WM_LBUTTONUP, IntPtr.Zero, position)
        Application.DoEvents()
    End Sub

    ''' <summary>The point of the 16-unit grid that lies inside the triangle of <paramref name="direction"/>, at the cell's size.</summary>
    Private Shared Function InsidePoint(direction As GroupMarker.Direction, cell As Rectangle) As Point
        Dim s As Integer = cell.Width
        Select Case direction
            Case GroupMarker.Direction.Down
                Return New Point(cell.X + 8 * s \ 16, cell.Y + 7 * s \ 16)
            Case GroupMarker.Direction.Left
                Return New Point(cell.X + 9 * s \ 16, cell.Y + 8 * s \ 16)
            Case Else
                Return New Point(cell.X + 7 * s \ 16, cell.Y + 8 * s \ 16)
        End Select
    End Function

    ''' <summary>The bounding box of the ink inside <paramref name="area"/>; empty when there is none.</summary>
    Private Shared Function InkBox(bitmap As Bitmap, area As Rectangle, background As Color) As Rectangle
        Dim minX As Integer = Integer.MaxValue
        Dim minY As Integer = Integer.MaxValue
        Dim maxX As Integer = -1
        Dim maxY As Integer = -1
        area.Intersect(New Rectangle(0, 0, bitmap.Width, bitmap.Height))
        For y As Integer = area.Top To area.Bottom - 1
            For x As Integer = area.Left To area.Right - 1
                If IsInk(bitmap.GetPixel(x, y), background) Then
                    minX = Math.Min(minX, x)
                    maxX = Math.Max(maxX, x)
                    minY = Math.Min(minY, y)
                    maxY = Math.Max(maxY, y)
                End If
            Next
        Next
        If maxX < 0 Then Return Rectangle.Empty
        Return New Rectangle(minX, minY, maxX - minX + 1, maxY - minY + 1)
    End Function

    ''' <summary>The ink-weighted centroid of the cell, in pixels from its top-left corner.</summary>
    Private Shared Function Centroid(bitmap As Bitmap, cell As Rectangle, background As Color) As PointF
        Dim sumX As Double = 0
        Dim sumY As Double = 0
        Dim total As Double = 0
        For y As Integer = 0 To cell.Height - 1
            For x As Integer = 0 To cell.Width - 1
                Dim weight As Double = Math.Min(Distance(bitmap.GetPixel(cell.X + x, cell.Y + y), background), 600)
                sumX += weight * (x + 0.5)
                sumY += weight * (y + 0.5)
                total += weight
            Next
        Next
        If total <= 0 Then Return New PointF(-1, -1)
        Return New PointF(CSng(sumX / total), CSng(sumY / total))
    End Function

    Private Shared Sub AssertTriangle(rig As Rig, bitmap As Bitmap, direction As GroupMarker.Direction, what As String)
        Dim cell As Rectangle = CellOf(rig.Header)
        Assert.True(cell.Left >= 0 AndAlso cell.Top >= 0 AndAlso cell.Right <= bitmap.Width AndAlso cell.Bottom <= bitmap.Height,
                    what & ": the marker cell " & cell.ToString() & " leaves the header " & bitmap.Size.ToString() & ".")
        ' Pixel-for-pixel the shared triangle in the caption's ink, at the cell's size.
        Using reference As New Bitmap(cell.Width, cell.Height)
            Using g As Graphics = Graphics.FromImage(reference)
                g.Clear(rig.Background)
                GroupMarker.Fill(g, rig.Ink, direction, New Rectangle(0, 0, cell.Width, cell.Height))
            End Using
            Dim wrong As Integer = 0
            For y As Integer = 0 To cell.Height - 1
                For x As Integer = 0 To cell.Width - 1
                    If Distance(bitmap.GetPixel(cell.X + x, cell.Y + y), reference.GetPixel(x, y)) > 40 Then wrong += 1
                Next
            Next
            Assert.True(wrong = 0, what & ": " & wrong & " pixels of the marker cell differ from the shared triangle pointing " & direction.ToString() & ".")
        End Using
        ' The direction read from the pixels themselves, not from the same code that drew them.
        Dim centre As PointF = Centroid(bitmap, cell, rig.Background)
        Dim middle As Single = cell.Width / 2.0F
        Select Case direction
            Case GroupMarker.Direction.Right
                Assert.True(centre.X > 0 AndAlso centre.X < middle - 0.5F, what & ": a right triangle's weight sits left of centre, was " & centre.X & " of " & middle & ".")
            Case GroupMarker.Direction.Left
                Assert.True(centre.X > middle + 0.5F, what & ": a left triangle's weight sits right of centre, was " & centre.X & " of " & middle & ".")
            Case Else
                Assert.True(centre.Y > 0 AndAlso centre.Y < middle - 0.5F, what & ": a down triangle's weight sits above centre, was " & centre.Y & " of " & middle & ".")
        End Select
    End Sub

    ' --- the pure parts -----------------------------------------------------------------------------

    <Fact>
    Public Sub Cell_is_tier_16_scaled_never_smaller_and_the_gap_is_8_logical_px()
        Assert.Equal(16, GroupMarker.CellPx(1.0F))
        Assert.Equal(20, GroupMarker.CellPx(1.25F))
        Assert.Equal(24, GroupMarker.CellPx(1.5F))
        Assert.Equal(28, GroupMarker.CellPx(1.75F))
        Assert.Equal(32, GroupMarker.CellPx(2.0F))
        Assert.Equal(16, GroupMarker.CellPx(0.5F))
        Assert.Equal(16, GroupMarker.CellPx(0.0F))
        Assert.Equal(8, GroupMarker.GapPx(1.0F))
        Assert.Equal(14, GroupMarker.GapPx(1.75F))
        Assert.Equal(8, GroupMarker.GapPx(0.5F))
        Assert.Equal(UiSizing.MarkerTier, GroupMarker.TierLogical)
        Assert.Equal(UiSizing.GlyphGap, GroupMarker.GapLogical)
    End Sub

    <Fact>
    Public Sub Direction_points_by_state_and_toward_the_reading_end()
        Assert.Equal(GroupMarker.Direction.Right, GroupMarker.DirectionFor(False, False))
        Assert.Equal(GroupMarker.Direction.Left, GroupMarker.DirectionFor(False, True))
        Assert.Equal(GroupMarker.Direction.Down, GroupMarker.DirectionFor(True, False))
        Assert.Equal(GroupMarker.Direction.Down, GroupMarker.DirectionFor(True, True))
    End Sub

    <Fact>
    Public Sub First_line_centre_follows_the_first_line_of_a_wrapped_caption()
        ' One 20 px line in a 44 px bar: the line starts at 12, its centre is 22.
        Assert.Equal(22, GroupMarker.FirstLineCentre(0, 44, 20, 20))
        ' Two lines (40 px) in a 56 px bar: the block starts at 8, the first line's centre is 18 - not the block's 28.
        Assert.Equal(18, GroupMarker.FirstLineCentre(0, 56, 40, 20))
        ' A block shorter than a line is a line.
        Assert.Equal(22, GroupMarker.FirstLineCentre(0, 44, 0, 20))
    End Sub

    ' --- the headers --------------------------------------------------------------------------------

    <Fact>
    Public Sub Both_header_kinds_draw_the_state_triangle_at_the_reading_start_pointing_by_state()
        OnUi(Sub()
                 For Each kind In Kinds
                     For Each rtl In {False, True}
                         For Each expanded In {False, True}
                             Dim what As String = kind & If(rtl, " rtl", " ltr") & If(expanded, " open", " collapsed")
                             Using rig As Rig = Build(kind, "Folder access", expanded, rtl, False, 1.0F, 420)
                                 Using bitmap As Bitmap = Render(rig.Header)
                                     Dim cell As Rectangle = CellOf(rig.Header)
                                     If rtl Then
                                         Assert.True(cell.Right > bitmap.Width \ 2 AndAlso cell.Right <= bitmap.Width - 4, what & ": the marker is not at the right (reading) start, cell " & cell.ToString() & ".")
                                     Else
                                         Assert.True(cell.Left < bitmap.Width \ 2 AndAlso cell.Left >= 4, what & ": the marker is not at the left (reading) start, cell " & cell.ToString() & ".")
                                     End If
                                     AssertTriangle(rig, bitmap, GroupMarker.DirectionFor(expanded, rtl), what)
                                 End Using
                             End Using
                         Next
                     Next
                 Next
             End Sub)
    End Sub

    <Fact>
    Public Sub The_marker_cell_is_16_px_at_100_percent_and_28_px_at_175_percent_and_stays_inside_the_header()
        OnUi(Sub()
                 For Each kind In Kinds
                     For Each pair In {(1.0F, 16), (1.75F, 28)}
                         Dim what As String = kind & " x" & pair.Item1
                         Using rig As Rig = Build(kind, "Folder access", False, False, False, pair.Item1, 420)
                             Dim cell As Rectangle = CellOf(rig.Header)
                             Assert.True(cell.Width >= pair.Item2 AndAlso cell.Height = cell.Width, what & ": the marker cell is " & cell.Size.ToString() & ", wanted at least " & pair.Item2 & " square.")
                             Assert.True(cell.Width >= 16, what & ": smaller than tier 16.")
                             Assert.True(cell.Top >= 0 AndAlso cell.Bottom <= rig.Header.Height, what & ": the cell " & cell.ToString() & " leaves the header, height " & rig.Header.Height & ".")
                             Using bitmap As Bitmap = Render(rig.Header)
                                 AssertTriangle(rig, bitmap, GroupMarker.Direction.Right, what)
                                 ' The triangle's own extent: 12 of the 16 grid units along its base, 8 deep.
                                 Dim box As Rectangle = InkBox(bitmap, cell, rig.Background)
                                 Assert.True(box.Height >= cell.Width * 11 \ 16, what & ": the triangle is only " & box.Height & " px tall in a " & cell.Width & " px cell.")
                             End Using
                         End Using
                     Next
                 Next
             End Sub)
    End Sub

    <Fact>
    Public Sub The_marker_is_drawn_in_the_caption_ink_in_a_light_and_a_dark_palette()
        OnUi(Sub()
                 For Each kind In Kinds
                     For Each dark In {False, True}
                         Dim what As String = kind & If(dark, " dark", " light")
                         Using rig As Rig = Build(kind, "Folder access", True, False, dark, 1.0F, 420)
                             Using bitmap As Bitmap = Render(rig.Header)
                                 Dim cell As Rectangle = CellOf(rig.Header)
                                 Dim inside As Point = InsidePoint(GroupMarker.Direction.Down, cell)
                                 Dim marker As Color = bitmap.GetPixel(inside.X, inside.Y)
                                 Assert.True(Distance(marker, rig.Ink) <= 8, what & ": the marker is " & marker.ToString() & ", the caption ink is " & rig.Ink.ToString() & ".")
                                 ' The caption really is drawn in that ink: some glyph pixel of it is the ink itself.
                                 Dim nearest As Integer = Integer.MaxValue
                                 Dim textLeft As Integer = cell.Right + GroupMarker.GapPx(1.0F)
                                 For y As Integer = 3 To bitmap.Height - 5
                                     For x As Integer = textLeft To bitmap.Width - 8
                                         nearest = Math.Min(nearest, Distance(bitmap.GetPixel(x, y), rig.Ink))
                                     Next
                                 Next
                                 Assert.True(nearest <= 24, what & ": no pixel of the caption is in the ink " & rig.Ink.ToString() & " (nearest distance " & nearest & ").")
                                 Dim contrast As Integer = Distance(rig.Ink, rig.Background)
                                 Assert.True(contrast >= 300, what & ": ink and background are too close (" & contrast & ") to read the marker.")
                             End Using
                         End Using
                     Next
                 Next
             End Sub)
    End Sub

    <Fact>
    Public Sub The_marker_mirrors_under_rtl()
        OnUi(Sub()
                 For Each kind In Kinds
                     Using left As Rig = Build(kind, "Folder access", False, False, False, 1.0F, 420)
                         Using right As Rig = Build(kind, "Folder access", False, True, False, 1.0F, 420)
                             Dim cellLeft As Rectangle = CellOf(left.Header)
                             Dim cellRight As Rectangle = CellOf(right.Header)
                             Assert.Equal(cellLeft.Size, cellRight.Size)
                             Assert.Equal(cellLeft.Y, cellRight.Y)
                             Assert.Equal(left.Header.Width - cellLeft.Right, cellRight.X)
                             Using a As Bitmap = Render(left.Header)
                                 Using b As Bitmap = Render(right.Header)
                                     ' The rasteriser's anti-aliasing is not bit-exact under a mirror (an edge pixel can differ by a
                                     ' sixth of the contrast), so the check is: no pixel differs by more than half the contrast, and
                                     ' the two markers carry the same ink within 3 percent.
                                     Dim contrast As Integer = Distance(left.Ink, left.Background)
                                     Dim wrong As Integer = 0
                                     Dim weightA As Double = 0
                                     Dim weightB As Double = 0
                                     For y As Integer = 0 To cellLeft.Height - 1
                                         For x As Integer = 0 To cellLeft.Width - 1
                                             Dim pixelA As Color = a.GetPixel(cellLeft.X + x, cellLeft.Y + y)
                                             Dim pixelB As Color = b.GetPixel(cellRight.X + cellRight.Width - 1 - x, cellRight.Y + y)
                                             If Distance(pixelA, pixelB) > contrast \ 2 Then wrong += 1
                                             weightA += Distance(pixelA, left.Background)
                                             weightB += Distance(pixelB, right.Background)
                                         Next
                                     Next
                                     Assert.True(wrong = 0, kind & ": " & wrong & " pixels of the rtl marker are not the mirror of the ltr one.")
                                     Assert.True(Math.Abs(weightA - weightB) <= 0.03 * Math.Max(weightA, weightB), kind & ": the mirrored markers carry different ink (" & weightA & " against " & weightB & ").")
                                 End Using
                             End Using
                         End Using
                     End Using
                 Next
             End Sub)
    End Sub

    <Fact>
    Public Sub A_long_russian_or_german_caption_wraps_beside_the_marker_instead_of_running_under_it()
        OnUi(Sub()
                 For Each kind In {"card", "section-wrap"}
                     For Each caption In {LongRussian, LongGerman}
                         For Each rtl In {False, True}
                             Dim what As String = kind & If(rtl, " rtl ", " ltr ") & caption.Substring(0, 8)
                             Using rig As Rig = Build(kind, caption, False, rtl, False, 1.0F, 260)
                                 Dim header As Control = rig.Header
                                 Using bold As New Font(header.Font, FontStyle.Bold)
                                     Dim line As Integer = bold.Height
                                     Assert.True(header.Height >= 2 * line, what & ": the header is " & header.Height & " px tall, a caption of " & caption.Length & " letters in 260 px must take two lines of " & line & ".")
                                     Using bitmap As Bitmap = Render(header)
                                         Dim cell As Rectangle = CellOf(header)
                                         Dim gap As Integer = GroupMarker.GapPx(1.0F)
                                         ' The strip between the cell and the caption holds no ink at all (the focus rectangle and the
                                         ' hairline are on the rows the strip leaves out).
                                         Dim strip As Rectangle = If(rtl,
                                             New Rectangle(cell.Left - gap + 2, 3, gap - 3, bitmap.Height - 7),
                                             New Rectangle(cell.Right + 1, 3, gap - 3, bitmap.Height - 7))
                                         Dim intruder As Rectangle = InkBox(bitmap, strip, rig.Background)
                                         Assert.True(intruder.IsEmpty, what & ": ink in the gap between marker and caption at " & intruder.ToString() & ".")
                                         ' Beyond the strip the caption really is there, on at least two lines.
                                         Dim textArea As Rectangle = If(rtl,
                                             New Rectangle(4, 3, cell.Left - gap - 4, bitmap.Height - 7),
                                             New Rectangle(cell.Right + gap, 3, bitmap.Width - cell.Right - gap - 4, bitmap.Height - 7))
                                         Dim text As Rectangle = InkBox(bitmap, textArea, rig.Background)
                                         Assert.False(text.IsEmpty, what & ": the caption is not drawn beside the marker.")
                                         Assert.True(text.Height >= CInt(1.6 * line), what & ": the caption's ink is " & text.Height & " px tall - it did not wrap onto a second line of " & line & ".")
                                         ' The marker stays beside the FIRST line, not at the middle of the block.
                                         Dim centre As Integer = cell.Y + cell.Height \ 2
                                         Assert.True(centre < header.Height \ 2 - 3, what & ": the marker centre " & centre & " is not above the middle " & header.Height \ 2 & " of a wrapped header.")
                                     End Using
                                 End Using
                             End Using
                         Next
                     Next
                 Next
             End Sub)
    End Sub

    <Fact>
    Public Sub A_one_line_caption_keeps_the_marker_centred_and_clear_of_the_text()
        OnUi(Sub()
                 For Each kind In Kinds
                     For Each rtl In {False, True}
                         Dim what As String = kind & If(rtl, " rtl", " ltr")
                         Using rig As Rig = Build(kind, "Folder access", False, rtl, False, 1.0F, 420)
                             Using bitmap As Bitmap = Render(rig.Header)
                                 Dim cell As Rectangle = CellOf(rig.Header)
                                 Dim centre As Integer = cell.Y + cell.Height \ 2
                                 Assert.True(Math.Abs(centre - rig.Header.Height \ 2) <= 2, what & ": the marker centre " & centre & " is not the middle " & rig.Header.Height \ 2 & " of a one-line header.")
                                 Dim gap As Integer = GroupMarker.GapPx(1.0F)
                                 Dim strip As Rectangle = If(rtl,
                                     New Rectangle(cell.Left - gap + 2, 3, gap - 3, bitmap.Height - 7),
                                     New Rectangle(cell.Right + 1, 3, gap - 3, bitmap.Height - 7))
                                 Assert.True(InkBox(bitmap, strip, rig.Background).IsEmpty, what & ": ink in the gap between marker and caption.")
                             End Using
                         End Using
                     Next
                 Next
             End Sub)
    End Sub

    <Fact>
    Public Sub A_click_anywhere_on_the_header_toggles_it_once()
        OnUi(Sub()
                 For Each kind In Kinds
                     Using rig As Rig = Build(kind, "Folder access", False, False, False, 1.0F, 420)
                         Dim header As Control = rig.Header
                         Dim cell As Rectangle = CellOf(header)
                         Dim middle As Integer = header.Height \ 2
                         Dim points As Point() = {
                             New Point(cell.X + cell.Width \ 2, cell.Y + cell.Height \ 2),
                             New Point(2, 2),
                             New Point(cell.Right + 40, middle),
                             New Point(header.Width \ 2, middle),
                             New Point(header.Width - 3, middle),
                             New Point(header.Width - 3, header.Height - 4)}
                         Dim expected As Boolean = False
                         For Each point In points
                             Dim before As Integer = rig.Toggles
                             Click(rig, point.X, point.Y)
                             expected = Not expected
                             Assert.True(rig.Toggles = before + 1, kind & ": a click at " & point.ToString() & " raised " & (rig.Toggles - before) & " toggles, wanted one.")
                             Assert.Equal(expected, rig.IsExpanded())
                         Next
                     End Using
                 Next
             End Sub)
    End Sub

    <Fact>
    Public Sub Space_and_enter_toggle_and_the_arrows_follow_the_reading_direction_and_the_state_reaches_assistive_tech()
        OnUi(Sub()
                 For Each kind In Kinds
                     For Each rtl In {False, True}
                         Dim what As String = kind & If(rtl, " rtl", " ltr")
                         Using rig As Rig = Build(kind, "Folder access", False, rtl, False, 1.0F, 420)
                             Dim header As Control = rig.Header
                             Assert.True((header.AccessibilityObject.State And AccessibleStates.Collapsed) <> 0, what & ": a collapsed header does not report Collapsed.")
                             Press(header, Keys.Space)
                             Assert.True(rig.IsExpanded(), what & ": Space did not open it.")
                             Assert.Equal(1, rig.Toggles)
                             Assert.True((header.AccessibilityObject.State And AccessibleStates.Expanded) <> 0, what & ": an open header does not report Expanded.")
                             Press(header, Keys.Enter)
                             Assert.False(rig.IsExpanded(), what & ": Enter did not close it.")
                             ' Collapsed: the arrow toward the reading end opens it, the other one does nothing.
                             Dim opens As Keys = If(rtl, Keys.Left, Keys.Right)
                             Dim idle As Keys = If(rtl, Keys.Right, Keys.Left)
                             Press(header, idle)
                             Assert.False(rig.IsExpanded(), what & ": the arrow away from the reading end opened a collapsed header.")
                             Press(header, opens)
                             Assert.True(rig.IsExpanded(), what & ": the arrow toward the reading end did not open it.")
                             Press(header, opens)
                             Assert.True(rig.IsExpanded(), what & ": the same arrow closed an open header.")
                             Press(header, idle)
                             Assert.False(rig.IsExpanded(), what & ": the arrow back did not close it.")
                         End Using
                     Next
                 Next
             End Sub)
    End Sub

    ' --- the source ---------------------------------------------------------------------------------

    <Fact>
    Public Sub Neither_header_draws_the_disclosure_chevron_any_more()
        Dim forms As String = Path.Combine(CompanionSourceDir(), "Forms")
        For Each name In {"SettingsGroupCard.vb", "CollapsibleSection.vb"}
            Dim source As String = File.ReadAllText(Path.Combine(forms, name))
            Assert.DoesNotContain("collapsibleSection", source)
            Assert.DoesNotContain("GlyphRenderer", source)
            Assert.Contains("GroupMarker.Fill(", source)
        Next
    End Sub

    Private Shared Function CompanionSourceDir() As String
        Dim dir As New DirectoryInfo(AppContext.BaseDirectory)
        While dir IsNot Nothing
            Dim candidate As String = Path.Combine(dir.FullName, "src", "FastMediaSorterCompanion")
            If Directory.Exists(candidate) AndAlso File.Exists(Path.Combine(candidate, "Program.vb")) Then Return candidate
            dir = dir.Parent
        End While
        Throw New DirectoryNotFoundException("Could not locate src\FastMediaSorterCompanion from the test binary.")
    End Function
End Class
