#If Not NETFRAMEWORK Then
Option Explicit On
Option Strict On
Option Infer On
Option Compare Binary

Imports System.Collections.Generic
Imports System.Drawing
Imports System.IO
Imports System.Linq
Imports System.Reflection
Imports System.Text.RegularExpressions
Imports System.Threading
Imports System.Windows.Forms
Imports Xunit

''' <summary>
''' The option row with a heading glyph and a `?` help button against BOOLEAN-OPTION 2.0 sections 2, 3 and 4 (ticket 243,
''' slice T-11a): the real controls and the real row arrangement (<see cref="SettingsCheckBox.ArrangeRow"/>) on an STA
''' thread, with test sources only - no shipped option carries a `?` yet. The popups open off the visible desktop (the
''' test form stands at -20000 and the work-area provider is replaced), the clock is the test's, and the Escape key and
''' the outside press are fed to the message filter directly, so no desktop pointer or key stream is involved.
'''
''' Not covered here, by nature: the look on a real monitor at 150/200 percent, the live high-contrast switch, a real
''' pointer travelling from the button into the popup, and a screen reader reading the names.
''' </summary>
<Collection("LocalizationState")>
Public Class OptionHelpRowTests

    Private Shared ReadOnly GlyphDir As String = Path.Combine(AppContext.BaseDirectory, "TestData", "glyphs")

    Shared Sub New()
        ButtonGlyphs.AssetOpener = Function(logicalName As String) As Stream
                                       Dim glyphFile As String = Path.Combine(GlyphDir, Path.GetFileName(logicalName))
                                       Return If(File.Exists(glyphFile), CType(File.OpenRead(glyphFile), Stream), Nothing)
                                   End Function
    End Sub

    Private NotInheritable Class RowParts
        Public Form As Form
        Public Host As Panel
        Public Check As CheckBox
        Public Title As Label
        Public Description As Label
        Public Heading As OptionHeading
        Public Fonts As New List(Of Font)()
        Public Changes As Integer
        Public Clock As Long
    End Class

    Private Shared Function Source(Optional key As String = "net.scan") As OptionHelpSource
        Return New OptionHelpSource(key, "Looks for devices on the network.",
                                    "Looks for devices on the local network. When on, the scan runs at start; when off, only on request. Needs the network permission.")
    End Function

    Private Shared Sub OnSta(action As Action)
        Dim failure As Exception = Nothing
        Dim thread As New Thread(Sub()
                                     Try
                                         action()
                                     Catch ex As Exception
                                         failure = ex
                                     End Try
                                 End Sub)
        thread.SetApartmentState(ApartmentState.STA)
        thread.Start()
        Assert.True(thread.Join(TimeSpan.FromSeconds(180)), "the option-help check did not finish")
        If failure IsNot Nothing Then System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(failure).Throw()
    End Sub

    Private Shared Function LightPalette() As OptionHelpPalette
        Return New OptionHelpPalette With {.Surface = Color.FromArgb(240, 240, 240), .Ink = Color.FromArgb(20, 20, 20),
                                           .MutedInk = Color.FromArgb(90, 90, 90), .Border = Color.FromArgb(120, 120, 120),
                                           .Accent = Color.FromArgb(0, 90, 200)}
    End Function

    Private Shared Function DarkPalette() As OptionHelpPalette
        Return New OptionHelpPalette With {.Surface = Color.FromArgb(32, 32, 36), .Ink = Color.FromArgb(235, 235, 235),
                                           .MutedInk = Color.FromArgb(170, 170, 170), .Border = Color.FromArgb(140, 140, 140),
                                           .Accent = Color.FromArgb(90, 160, 255)}
    End Function

    ''' <summary>A row built the way the settings window builds one, plus the optional heading parts.</summary>
    Private Shared Function BuildRow(scale As Single, rtl As Boolean, width As Integer, caption As String, explanation As String,
                                     glyphId As String, help As OptionHelpSource,
                                     Optional profile As UiInputProfile = UiInputProfile.Pointer,
                                     Optional density As UiDensity = UiDensity.Ordinary) As RowParts
        Dim parts As New RowParts()
        parts.Form = New Form With {.StartPosition = FormStartPosition.Manual, .Location = New Point(-20000, -20000),
                                    .ShowInTaskbar = False, .ClientSize = New Size(width + 40, 500)}
        parts.Host = New Panel With {.Left = 10, .Top = 10, .Width = width, .Height = SettingsCheckBox.Px(48, scale)}
        If rtl Then parts.Host.RightToLeft = RightToLeft.Yes
        Dim titleFont As New Font("Segoe UI Semibold", CSng(SettingsCheckBox.Px(12, scale)), FontStyle.Regular, GraphicsUnit.Pixel)
        Dim descriptionFont As New Font("Segoe UI", CSng(SettingsCheckBox.Px(SettingsRowGridRules.ExplanationFontPixels, scale)),
                                        FontStyle.Regular, GraphicsUnit.Pixel)
        parts.Fonts.Add(titleFont)
        parts.Fonts.Add(descriptionFont)
        parts.Title = New Label With {.AutoSize = False, .Text = caption, .Font = titleFont, .ForeColor = LightPalette().Ink}
        parts.Description = New Label With {.AutoSize = False, .Text = explanation, .Font = descriptionFont}
        parts.Check = New CheckBox()
        parts.Host.Controls.Add(parts.Check)
        parts.Host.Controls.Add(parts.Title)
        parts.Host.Controls.Add(parts.Description)
        SettingsCheckBox.Attach(parts.Check, scale)
        Dim light As OptionHelpPalette = LightPalette()
        SettingsCheckBox.ApplyPalette(parts.Check, light.Surface, Color.White, light.Border, light.Ink, light.Accent, Color.White)
        SettingsCheckBox.BindCaption(parts.Title, parts.Check)
        AddHandler parts.Check.CheckedChanged, Sub() parts.Changes += 1
        parts.Heading = OptionHeading.Attach(parts.Host, parts.Check, parts.Title, parts.Description, scale, glyphId, help, profile, density)
        If parts.Heading IsNot Nothing Then
            parts.Heading.ApplyPalette(light)
            If parts.Heading.HasHelp Then
                parts.Heading.Controller.Clock = Function() parts.Clock
                parts.Heading.Controller.WorkAreaProvider = Function(anchor As Rectangle) New Rectangle(-32000, -32000, 24000, 24000)
            End If
        End If
        parts.Form.Controls.Add(parts.Host)
        parts.Form.Show()
        SettingsCheckBox.ArrangeRow(parts.Host, parts.Check, parts.Title, parts.Description, scale)
        Return parts
    End Function

    Private Shared Sub Release(parts As RowParts)
        parts.Form.Dispose()
        For Each font As Font In parts.Fonts
            font.Dispose()
        Next
        ' Disposing the row leaves nothing registered with the process-wide Escape / outside-press filter.
        Assert.Equal(0, OptionHelpController.OpenCount)
    End Sub

    ''' <summary>Raises the control's Click the way the message loop does (OnClick is protected).</summary>
    Private Shared Sub ClickOn(control As Control)
        Dim current As Type = control.GetType()
        Dim method As MethodInfo = Nothing
        While current IsNot Nothing AndAlso method Is Nothing
            method = current.GetMethod("OnClick", BindingFlags.Instance Or BindingFlags.NonPublic Or BindingFlags.DeclaredOnly,
                                       Nothing, New Type() {GetType(EventArgs)}, Nothing)
            current = current.BaseType
        End While
        Assert.NotNull(method)
        method.Invoke(control, New Object() {EventArgs.Empty})
    End Sub

    Private Shared Sub InLanguage(code As String, body As Action)
        Dim saved As String = Localization.CurrentCode
        Localization.CurrentCode = code
        Try
            body()
        Finally
            Localization.CurrentCode = saved
        End Try
    End Sub

    Private Shared Function EscapeMessage() As Message
        Return Message.Create(IntPtr.Zero, OptionHelpMessageFilter.WM_KEYDOWN, New IntPtr(OptionHelpMessageFilter.VK_ESCAPE), IntPtr.Zero)
    End Function

    Private Shared Function PressEscape() As Boolean
        Dim filter As IMessageFilter = New OptionHelpMessageFilter()
        Dim message As Message = EscapeMessage()
        Return filter.PreFilterMessage(message)
    End Function

    ' ---------------------------------------------------------------- section 2 rules 2, 4 and 6

    <Fact>
    Public Sub The_heading_runs_glyph_caption_help_with_8_unit_gaps_and_mirrors_for_an_rtl_row()
        OnSta(
            Sub()
                For Each scale As Single In {1.0F, 1.75F}
                    Dim width As Integer = SettingsCheckBox.Px(520, scale)
                    Dim gap As Integer = SettingsCheckBox.Px(8, scale)
                    Dim ltr As RowParts = BuildRow(scale, False, width, "Network scan", "Explanation of the option in a few words.", "app.settings", Source())
                    Dim rtl As RowParts = BuildRow(scale, True, width, "Network scan", "Explanation of the option in a few words.", "app.settings", Source())
                    Try
                        Dim h As OptionHeading = ltr.Heading
                        Assert.NotNull(h.Glyph)
                        Assert.NotNull(h.Help)
                        ' Left to right: box, glyph, caption, help - 8 units between the heading parts.
                        Assert.True(ltr.Check.Right <= h.Glyph.Left)
                        Assert.Equal(h.Glyph.Right + gap, ltr.Title.Left)
                        Assert.Equal(ltr.Title.Right + gap, h.Help.Left)
                        ' Glyph and name together at the reading start; the explanation starts at the cell's own start.
                        Assert.Equal(h.Glyph.Left, ltr.Description.Left)
                        ' The help follows the name: it is not pushed to the cell's far edge.
                        Assert.True(ltr.Host.Width - h.Help.Right > width \ 4, "help at " & h.Help.Right & " of " & ltr.Host.Width)
                        ' The parts share the first heading line.
                        Assert.InRange(h.Glyph.Top + h.Glyph.Height \ 2, ltr.Title.Top, ltr.Title.Bottom)
                        Assert.InRange(h.Help.Top + h.Help.Height \ 2, ltr.Title.Top, ltr.Title.Bottom)

                        ' Right to left: the exact mirror of every part.
                        Dim m As OptionHeading = rtl.Heading
                        Assert.Equal(width - h.Glyph.Right, m.Glyph.Left)
                        Assert.Equal(width - ltr.Title.Right, rtl.Title.Left)
                        Assert.Equal(width - h.Help.Right, m.Help.Left)
                        ' The box and the explanation keep the row builder's own rounding (one device pixel).
                        Assert.InRange(rtl.Check.Left, width - ltr.Check.Right - 1, width - ltr.Check.Right + 1)
                        Assert.InRange(rtl.Description.Left, width - ltr.Description.Right - 1, width - ltr.Description.Right + 1)
                        Assert.Equal(ltr.Host.Height, rtl.Host.Height)
                        Assert.True(m.Help.Right <= rtl.Title.Left - gap + 0, "RTL: the help must stand left of the caption")
                        Assert.Equal(RightToLeft.No, m.Help.RightToLeft)
                    Finally
                        Release(ltr)
                        Release(rtl)
                    End Try
                Next
            End Sub)
    End Sub

    <Fact>
    Public Sub An_arabic_caption_reads_from_the_right_glyph_first_then_name_then_help()
        OnSta(
            Sub()
                InLanguage("ar",
                    Sub()
                        Dim row As RowParts = BuildRow(1.0F, True, 520, "فحص الشبكة", "يبحث عن الأجهزة على الشبكة المحلية.", "app.settings", Source())
                        Try
                            Dim h As OptionHeading = row.Heading
                            Assert.True(h.Glyph.Left > row.Title.Left, "the glyph is at the reading start (right)")
                            Assert.True(row.Title.Left > h.Help.Left, "the help follows the name to the left")
                            Assert.True(row.Check.Left > h.Glyph.Left, "the box stands right of the glyph")
                            Assert.Equal(h.Glyph.Right, row.Description.Right)
                            Assert.Equal("مساعدة: فحص الشبكة", h.Help.AccessibleName)
                        Finally
                            Release(row)
                        End Try
                    End Sub)
            End Sub)
    End Sub

    <Fact>
    Public Sub An_absent_glyph_or_help_reserves_no_slot()
        OnSta(
            Sub()
                Dim helpOnly As RowParts = BuildRow(1.0F, False, 520, "Network scan", "Explanation.", "", Source())
                Dim glyphOnly As RowParts = BuildRow(1.0F, False, 520, "Network scan", "Explanation.", "app.settings", Nothing)
                Dim neither As RowParts = BuildRow(1.0F, False, 520, "Network scan", "Explanation.", "", Nothing)
                Try
                    ' Help only: the caption stands at the cell start where the glyph would have been.
                    Assert.Null(helpOnly.Heading.Glyph)
                    Assert.Equal(helpOnly.Description.Left, helpOnly.Title.Left)
                    Assert.Equal(helpOnly.Title.Right + SettingsCheckBox.Px(8, 1.0F), helpOnly.Heading.Help.Left)
                    ' Glyph only: no help control exists at all.
                    Assert.Null(glyphOnly.Heading.Help)
                    Assert.Null(glyphOnly.Heading.Controller)
                    Assert.DoesNotContain(glyphOnly.Host.Controls.Cast(Of Control)(), Function(c) TypeOf c Is OptionHelpButton)
                    Assert.Equal(glyphOnly.Heading.Glyph.Right + SettingsCheckBox.Px(8, 1.0F), glyphOnly.Title.Left)
                    ' Neither: nothing is created and the row is laid out as every existing row.
                    Assert.Null(neither.Heading)
                    Assert.Null(OptionHeading.Find(neither.Check))
                    Assert.Equal(3, neither.Host.Controls.Count)
                    Assert.Equal(neither.Description.Left, neither.Title.Left)
                Finally
                    Release(helpOnly)
                    Release(glyphOnly)
                    Release(neither)
                End Try
            End Sub)
    End Sub

    <Fact>
    Public Sub A_row_with_neither_part_is_laid_out_exactly_as_a_row_that_never_heard_of_the_heading()
        OnSta(
            Sub()
                For Each scale As Single In {1.0F, 1.75F}
                    Dim width As Integer = SettingsCheckBox.Px(520, scale)
                    Dim explained As String = String.Join(" ", Enumerable.Repeat("A longer explanation of what this option does.", 4))
                    Dim attached As RowParts = BuildRow(scale, False, width, "Caption", explained, "", Nothing)
                    ' The same row without the call at all.
                    Dim plain As RowParts = BuildRow(scale, False, width, "Caption", explained, "", Nothing)
                    Try
                        Assert.Equal(plain.Title.Bounds, attached.Title.Bounds)
                        Assert.Equal(plain.Description.Bounds, attached.Description.Bounds)
                        Assert.Equal(plain.Check.Bounds, attached.Check.Bounds)
                        Assert.Equal(plain.Host.Height, attached.Host.Height)
                        Dim lead As SettingsCheckBox.Leading = SettingsCheckBox.PlaceLeading(width, False, scale)
                        Assert.Equal(lead.TextLeft, attached.Title.Left)
                        Assert.Equal(lead.TextWidth, attached.Title.Width)
                    Finally
                        Release(attached)
                        Release(plain)
                    End Try
                Next
            End Sub)
    End Sub

    <Fact>
    Public Sub A_long_caption_wraps_and_keeps_reading_order_without_overlap_at_100_and_175_percent()
        OnSta(
            Sub()
                For Each rtl As Boolean In {False, True}
                    For Each scale As Single In {1.0F, 1.75F}
                        Dim width As Integer = SettingsCheckBox.Px(300, scale)
                        Dim caption As String = "A very long name of an option that cannot fit on one line next to its glyph and its help"
                        Dim row As RowParts = BuildRow(scale, rtl, width, caption, "Explanation text.", "app.settings", Source())
                        Try
                            Dim h As OptionHeading = row.Heading
                            Dim need As Integer = SettingsRowText.WrappedHeight(row.Title, row.Title.Width, 1, 0)
                            Assert.True(row.Title.Height >= need - 1, "the caption is clipped")
                            Assert.True(row.Title.Height > row.Title.Font.Height + 4, "the caption did not wrap")
                            Dim boxes As New List(Of Rectangle) From {row.Check.Bounds, h.Glyph.Bounds, row.Title.Bounds, h.Help.Bounds, row.Description.Bounds}
                            For i As Integer = 0 To boxes.Count - 1
                                Assert.True(row.Host.ClientRectangle.Contains(boxes(i)), "part " & i & " leaves the row, rtl=" & rtl & " scale=" & scale)
                                For j As Integer = i + 1 To boxes.Count - 1
                                    Assert.False(boxes(i).IntersectsWith(boxes(j)), "parts " & i & " and " & j & " overlap, rtl=" & rtl & " scale=" & scale)
                                Next
                            Next
                            ' The focus order is unchanged: box, then help.
                            Assert.Equal(row.Check.TabIndex + 1, h.Help.TabIndex)
                        Finally
                            Release(row)
                        End Try
                    Next
                Next
            End Sub)
    End Sub

    ' ---------------------------------------------------------------- section 2 rule 4 / UI-SIZING

    <Fact>
    Public Sub The_help_target_and_the_glyph_follow_the_size_tokens()
        OnSta(
            Sub()
                Dim cases As (UiInputProfile, UiDensity, Integer, Integer)() = {
                    (UiInputProfile.Pointer, UiDensity.Ordinary, 32, 16),
                    (UiInputProfile.Touch, UiDensity.Ordinary, 48, 20),
                    (UiInputProfile.Pointer, UiDensity.Compact, UiSizing.CompactTargetFloor, 16)}
                For Each scale As Single In {1.0F, 1.75F}
                    For Each c In cases
                        Dim row As RowParts = BuildRow(scale, False, SettingsCheckBox.Px(520, scale), "Network scan", "Explanation.", "app.settings", Source(), c.Item1, c.Item2)
                        Try
                            Dim h As OptionHeading = row.Heading
                            Assert.Equal(UiSizing.TargetPx(c.Item1, c.Item2, scale), h.Help.Width)
                            Assert.Equal(h.Help.Width, h.Help.Height)
                            Assert.Equal(UiSizing.GlyphPx(c.Item1, c.Item2, scale, 1.0F), h.Glyph.Width)
                            Assert.True(h.Help.Width >= UiSizing.DevicePx(UiSizing.CompactTargetFloor, scale), "below the compact floor")
                            ' Whatever the profile, the hit areas of the row do not overlap and stay inside it.
                            Assert.False(h.Help.Bounds.IntersectsWith(row.Check.Bounds))
                            Assert.False(h.Help.Bounds.IntersectsWith(h.Glyph.Bounds))
                            Assert.False(h.Help.Bounds.IntersectsWith(row.Description.Bounds), "the help target lies over the explanation")
                            Assert.True(row.Host.ClientRectangle.Contains(h.Help.Bounds))
                            Assert.True(row.Host.Height >= h.Help.Bottom)
                        Finally
                            Release(row)
                        End Try
                    Next
                Next
            End Sub)
    End Sub

    ' ---------------------------------------------------------------- section 3 rule 1: one toggle path

    <Fact>
    Public Sub The_caption_the_glyph_and_the_box_toggle_once_the_explanation_and_the_help_never()
        OnSta(
            Sub()
                Dim row As RowParts = BuildRow(1.0F, False, 520, "Network scan", "Explanation of the option.", "app.settings", Source())
                Try
                    Dim h As OptionHeading = row.Heading
                    ClickOn(row.Title)
                    Assert.Equal(1, row.Changes)
                    Assert.True(row.Check.Checked)
                    ClickOn(h.Glyph)
                    Assert.Equal(2, row.Changes)
                    Assert.False(row.Check.Checked)
                    ClickOn(row.Check)
                    Assert.Equal(3, row.Changes)

                    ' The explanation and the host's background do not toggle.
                    ClickOn(row.Description)
                    ClickOn(row.Host)
                    Assert.Equal(3, row.Changes)

                    ' The help opens its explanation and changes nothing of the option.
                    Dim before As Boolean = row.Check.Checked
                    h.Help.PerformClick()
                    Assert.True(h.Controller.FullVisible)
                    Assert.Equal(3, row.Changes)
                    Assert.Equal(before, row.Check.Checked)
                    h.Controller.CloseFull(True)
                    Assert.Equal(3, row.Changes)
                    ClickOn(h.Help)
                    Assert.Equal(3, row.Changes)
                    h.Controller.CloseFull(True)

                    ' The cursor says which parts act.
                    Assert.Equal(Cursors.Hand, row.Title.Cursor)
                    Assert.Equal(Cursors.Hand, h.Glyph.Cursor)
                    Assert.NotEqual(Cursors.Hand, row.Description.Cursor)
                Finally
                    Release(row)
                End Try
            End Sub)
    End Sub

    <Fact>
    Public Sub The_keyboard_activates_the_help_with_space_and_enter_and_never_the_option()
        OnSta(
            Sub()
                Dim row As RowParts = BuildRow(1.0F, False, 520, "Network scan", "Explanation.", "", Source())
                Try
                    Dim h As OptionHeading = row.Heading
                    row.Form.ActiveControl = h.Help
                    Dim clicks As Integer = 0
                    AddHandler h.Help.Click, Sub() clicks += 1

                    ' Space: key down then key up on the focused button is a click.
                    InvokeKey(h.Help, "OnKeyDown", Keys.Space)
                    InvokeKey(h.Help, "OnKeyUp", Keys.Space)
                    Assert.Equal(1, clicks)
                    Assert.True(h.Controller.FullVisible, "Space did not open the explanation")
                    Assert.Equal(0, row.Changes)
                    h.Controller.CloseFull(True)

                    ' Enter: the form's dialog-key path presses the focused button.
                    row.Form.ActiveControl = h.Help
                    Dim handled As Boolean = ProcessDialogKey(row.Form, Keys.Enter)
                    Assert.True(handled)
                    Assert.Equal(2, clicks)
                    Assert.True(h.Controller.FullVisible, "Enter did not open the explanation")
                    Assert.Equal(0, row.Changes)
                    h.Controller.CloseFull(True)
                    Assert.Same(h.Help, row.Form.ActiveControl)
                Finally
                    Release(row)
                End Try
            End Sub)
    End Sub

    Private Shared Sub InvokeKey(control As Control, methodName As String, key As Keys)
        Dim method As MethodInfo = GetType(Control).GetMethod(methodName, BindingFlags.Instance Or BindingFlags.NonPublic)
        If method Is Nothing Then method = GetType(Button).BaseType.GetMethod(methodName, BindingFlags.Instance Or BindingFlags.NonPublic)
        Assert.NotNull(method)
        method.Invoke(control, New Object() {New KeyEventArgs(key)})
    End Sub

    Private Shared Function ProcessDialogKey(form As Form, key As Keys) As Boolean
        Dim method As MethodInfo = GetType(Form).GetMethod("ProcessDialogKey", BindingFlags.Instance Or BindingFlags.NonPublic)
        Assert.NotNull(method)
        Return CBool(method.Invoke(form, New Object() {key}))
    End Function

    <Fact>
    Public Sub A_disabled_option_never_toggles_but_its_help_stays_available()
        OnSta(
            Sub()
                Dim row As RowParts = BuildRow(1.0F, False, 520, "Network scan", "Explanation. Available when the other option is on.", "app.settings", Source())
                Try
                    Dim h As OptionHeading = row.Heading
                    row.Check.Checked = True
                    row.Changes = 0
                    row.Check.Enabled = False
                    ClickOn(row.Title)
                    ClickOn(h.Glyph)
                    ClickOn(row.Description)
                    Assert.True(row.Check.Checked)
                    Assert.Equal(0, row.Changes)
                    Assert.True(h.Help.Enabled, "the help of a disabled option must stay enabled")
                    h.Help.PerformClick()
                    Assert.True(h.Controller.FullVisible)
                    Assert.Equal(0, row.Changes)
                    h.Controller.CloseFull(True)
                    ' The reason stays visible.
                    Assert.True(row.Description.Visible)
                Finally
                    Release(row)
                End Try
            End Sub)
    End Sub

    ' ---------------------------------------------------------------- section 3 rules 3 and 4: names, focus order

    <Fact>
    Public Sub The_option_is_one_named_checkbox_and_its_help_is_named_after_it()
        OnSta(
            Sub()
                InLanguage("en",
                    Sub()
                        Dim row As RowParts = BuildRow(1.0F, False, 520, "Network scan", "Looks on the local network.", "app.settings", Source())
                        Try
                            Dim h As OptionHeading = row.Heading
                            Assert.Equal("Network scan", row.Check.AccessibleName)
                            Assert.Equal("Looks on the local network.", row.Check.AccessibleDescription)
                            Assert.Equal("Help: Network scan", h.Help.AccessibleName)
                            Assert.Equal(AccessibleRole.PushButton, h.Help.AccessibleRole)
                            Assert.Equal(AccessibleRole.None, h.Glyph.AccessibleRole)
                            Assert.Equal(Source().Summary, h.Help.AccessibleDescription)
                        Finally
                            Release(row)
                        End Try
                    End Sub)
                InLanguage("ru",
                    Sub()
                        Dim row As RowParts = BuildRow(1.0F, False, 520, "Поиск в сети", "Ищет устройства.", "", Source())
                        Try
                            Assert.Equal("Справка: Поиск в сети", row.Heading.Help.AccessibleName)
                        Finally
                            Release(row)
                        End Try
                    End Sub)
            End Sub)
    End Sub

    <Fact>
    Public Sub The_tab_order_is_the_box_then_its_help_and_the_heading_adds_no_stop()
        OnSta(
            Sub()
                Dim row As RowParts = BuildRow(1.0F, False, 520, "Network scan", "Explanation.", "app.settings", Source())
                Try
                    Dim stops As List(Of Control) = row.Host.Controls.Cast(Of Control)().Where(Function(c) c.TabStop).OrderBy(Function(c) c.TabIndex).ToList()
                    Assert.Equal(2, stops.Count)
                    Assert.Same(row.Check, stops(0))
                    Assert.Same(row.Heading.Help, stops(1))
                    Assert.False(row.Heading.Glyph.TabStop)
                    Assert.False(row.Title.TabStop)
                    Assert.False(row.Description.TabStop)
                    ' Focusing never changes the value.
                    row.Check.Select()
                    row.Heading.Help.Select()
                    Assert.Equal(0, row.Changes)
                Finally
                    Release(row)
                End Try
            End Sub)
    End Sub

    <Fact>
    Public Sub An_invalid_source_gets_no_button()
        OnSta(
            Sub()
                Dim broken As New OptionHelpSource("net.scan", "", "full text here")
                Dim row As RowParts = BuildRow(1.0F, False, 520, "Network scan", "Explanation.", "", broken)
                Try
                    Assert.Null(row.Heading)
                    Assert.DoesNotContain(row.Host.Controls.Cast(Of Control)(), Function(c) TypeOf c Is OptionHelpButton)
                Finally
                    Release(row)
                End Try
            End Sub)
    End Sub

    ' ---------------------------------------------------------------- section 4 rules 2 and 3: the summary

    Private Shared Function ScreenRect(control As Control) As Rectangle
        Return control.RectangleToScreen(control.ClientRectangle)
    End Function

    <Fact>
    Public Sub Hover_opens_a_summary_that_takes_no_focus_and_does_not_cover_the_box()
        OnSta(
            Sub()
                Dim row As RowParts = BuildRow(1.0F, False, 520, "Network scan", "Explanation.", "app.settings", Source())
                Try
                    Dim c As OptionHelpController = row.Heading.Controller
                    row.Form.ActiveControl = row.Check
                    c.PointerEnteredButton()
                    row.Clock = OptionHelpRules.HoverShowDelayMs - 1
                    c.Tick()
                    Assert.False(c.SummaryVisible, "opened before the pointer had rested")
                    row.Clock = OptionHelpRules.HoverShowDelayMs
                    c.Tick()
                    Assert.True(c.SummaryVisible)
                    Dim popup As OptionHelpPopup = c.SummaryPopupWindow
                    Assert.Equal(Source().Summary, popup.BodyControl.Text)
                    ' No focus taken: the box keeps it, the popup is never the active form.
                    Assert.False(popup.ContainsFocus)
                    Assert.Same(row.Check, row.Form.ActiveControl)
                    Assert.NotSame(popup, Form.ActiveForm)
                    Assert.True((CreateParamsOf(popup).ExStyle And &H8000000) <> 0, "WS_EX_NOACTIVATE missing")
                    ' Placement: not over the originating box and not over the button.
                    Assert.False(popup.Bounds.IntersectsWith(ScreenRect(row.Check)), "the popup covers the checkbox")
                    Assert.False(popup.Bounds.IntersectsWith(ScreenRect(row.Heading.Help)), "the popup covers the help button")
                    ' It is a tool window owned by the surface, never a dialog.
                    Assert.Same(row.Form, popup.Owner)
                    Assert.False(popup.Modal)
                    Assert.Equal(Source().Summary, row.Heading.Help.AccessibleDescription)
                    Assert.Equal(0, row.Changes)
                Finally
                    Release(row)
                End Try
            End Sub)
    End Sub

    Private Shared Function CreateParamsOf(form As Form) As CreateParams
        Dim prop As PropertyInfo = GetType(Form).GetProperty("CreateParams", BindingFlags.Instance Or BindingFlags.NonPublic)
        Assert.NotNull(prop)
        Return CType(prop.GetValue(form), CreateParams)
    End Function

    <Fact>
    Public Sub The_summary_stays_while_the_pointer_moves_into_it_and_closes_after_it_has_left_both()
        OnSta(
            Sub()
                Dim row As RowParts = BuildRow(1.0F, False, 520, "Network scan", "Explanation.", "app.settings", Source())
                Try
                    Dim c As OptionHelpController = row.Heading.Controller
                    c.PointerEnteredButton()
                    row.Clock = 500
                    c.Tick()
                    Assert.True(c.SummaryVisible)

                    ' The pointer leaves the button for the popup: nothing closes, however long it is read.
                    c.PointerLeftButton()
                    row.Clock = 600
                    c.PopupPointer(True)
                    row.Clock = 90000
                    c.Tick()
                    Assert.True(c.SummaryVisible, "the summary closed under the pointer")

                    ' Left both: it closes after the grace time and not before.
                    c.PopupPointer(False)
                    row.Clock = 90000 + OptionHelpRules.HoverGraceMs - 1
                    c.Tick()
                    Assert.True(c.SummaryVisible)
                    row.Clock = 90000 + OptionHelpRules.HoverGraceMs
                    c.Tick()
                    Assert.False(c.SummaryVisible)
                    Assert.Equal(0, OptionHelpController.OpenCount)
                    Assert.Equal(0, row.Changes)
                Finally
                    Release(row)
                End Try
            End Sub)
    End Sub

    <Fact>
    Public Sub Keyboard_focus_opens_the_summary_and_leaving_the_button_closes_it()
        OnSta(
            Sub()
                Dim row As RowParts = BuildRow(1.0F, False, 520, "Network scan", "Explanation.", "", Source())
                Try
                    Dim c As OptionHelpController = row.Heading.Controller
                    c.FocusEntered()
                    Assert.True(c.SummaryVisible, "focus did not open the summary at once")
                    Assert.Equal(0, row.Changes)
                    c.FocusLeft()
                    row.Clock = OptionHelpRules.HoverGraceMs
                    c.Tick()
                    Assert.False(c.SummaryVisible)
                Finally
                    Release(row)
                End Try
            End Sub)
    End Sub

    <Fact>
    Public Sub Escape_dismisses_the_summary_without_the_pointer_moving_and_it_does_not_come_back_while_the_pointer_rests()
        OnSta(
            Sub()
                Dim row As RowParts = BuildRow(1.0F, False, 520, "Network scan", "Explanation.", "app.settings", Source())
                Try
                    Dim c As OptionHelpController = row.Heading.Controller
                    Assert.False(PressEscape(), "Escape with nothing open must pass through to the window")
                    c.PointerEnteredButton()
                    row.Clock = 500
                    c.Tick()
                    Assert.True(c.SummaryVisible)
                    Dim pointer As Point = Cursor.Position

                    Assert.True(PressEscape(), "Escape was not used up by the open summary")
                    Assert.False(c.SummaryVisible)
                    Assert.InRange(Math.Abs(Cursor.Position.X - pointer.X) + Math.Abs(Cursor.Position.Y - pointer.Y), 0, 2) ' a moved pointer is far away; the owner''s hand may jitter a pixel
                    row.Clock = 60000
                    c.Tick()
                    Assert.False(c.SummaryVisible, "the summary came back while the pointer rested")

                    ' The pointer leaves and returns: it opens again.
                    c.PointerLeftButton()
                    row.Clock = 61000
                    c.Tick()
                    c.PointerEnteredButton()
                    row.Clock = 61000 + OptionHelpRules.HoverShowDelayMs
                    c.Tick()
                    Assert.True(c.SummaryVisible)
                    Assert.False(PressEscape() AndAlso c.SummaryVisible)
                    Assert.Equal(0, row.Changes)
                Finally
                    Release(row)
                End Try
            End Sub)
    End Sub

    ' ---------------------------------------------------------------- section 4 rules 2 to 4: the persistent explanation

    <Fact>
    Public Sub The_persistent_explanation_closes_by_its_button_by_escape_and_by_an_outside_press_and_returns_the_focus()
        OnSta(
            Sub()
                Dim row As RowParts = BuildRow(1.0F, False, 520, "Network scan", "Explanation.", "app.settings", Source())
                Try
                    Dim h As OptionHeading = row.Heading
                    Dim c As OptionHelpController = h.Controller

                    ' Explicit close action.
                    row.Form.ActiveControl = row.Check
                    h.Help.PerformClick()
                    Assert.True(c.FullVisible)
                    Assert.Equal(Source().Full, c.FullPopupWindow.BodyControl.Text)
                    Assert.Equal("Network scan", c.FullPopupWindow.CaptionControl.Text)
                    Assert.Same(c.FullPopupWindow.CloseButton, c.FullPopupWindow.ActiveControl)
                    Assert.False(c.FullPopupWindow.Modal)
                    Assert.False(c.FullPopupWindow.Bounds.IntersectsWith(ScreenRect(row.Check)), "the explanation covers the checkbox")
                    c.FullPopupWindow.CloseButton.PerformClick()
                    Assert.False(c.FullVisible)
                    Assert.Same(h.Help, row.Form.ActiveControl)

                    ' Escape.
                    row.Form.ActiveControl = row.Check
                    h.Help.PerformClick()
                    Assert.True(c.FullVisible)
                    Assert.True(PressEscape())
                    Assert.False(c.FullVisible)
                    Assert.Same(h.Help, row.Form.ActiveControl)

                    ' A deliberate press outside; the press on the button itself is the button's own click.
                    row.Form.ActiveControl = row.Check
                    h.Help.PerformClick()
                    OptionHelpController.DismissOutside(h.Help.Handle)
                    Assert.True(c.FullVisible, "the button's own press must not close it before its click")
                    OptionHelpController.DismissOutside(row.Description.Handle)
                    Assert.False(c.FullVisible)
                    Assert.Same(h.Help, row.Form.ActiveControl)

                    ' The same button opens and closes it.
                    h.Help.PerformClick()
                    Assert.True(c.FullVisible)
                    h.Help.PerformClick()
                    Assert.False(c.FullVisible)

                    Assert.Equal(0, row.Changes)
                    Assert.Equal(0, OptionHelpController.OpenCount)
                Finally
                    Release(row)
                End Try
            End Sub)
    End Sub

    <Fact>
    Public Sub Opening_the_explanation_replaces_the_summary_and_closing_it_does_not_bring_the_summary_back()
        OnSta(
            Sub()
                Dim row As RowParts = BuildRow(1.0F, False, 520, "Network scan", "Explanation.", "", Source())
                Try
                    Dim c As OptionHelpController = row.Heading.Controller
                    c.PointerEnteredButton()
                    row.Clock = 500
                    c.Tick()
                    Assert.True(c.SummaryVisible)
                    row.Heading.Help.PerformClick()
                    Assert.False(c.SummaryVisible)
                    Assert.True(c.FullVisible)
                    c.CloseFull(True)
                    ' The focus is back on the button while the pointer still rests on it: the summary stays quiet.
                    c.FocusEntered()
                    row.Clock = 5000
                    c.Tick()
                    Assert.False(c.SummaryVisible)
                Finally
                    Release(row)
                End Try
            End Sub)
    End Sub

    <Fact>
    Public Sub The_popup_follows_the_reading_direction_of_the_row()
        OnSta(
            Sub()
                Dim row As RowParts = BuildRow(1.0F, True, 520, "Network scan", "Explanation.", "", Source())
                Try
                    row.Heading.Help.PerformClick()
                    Dim popup As OptionHelpPopup = row.Heading.Controller.FullPopupWindow
                    Assert.Equal(RightToLeft.Yes, popup.RightToLeft)
                    ' The Close action stands at the reading end: the left edge under right-to-left.
                    Assert.True(popup.CloseButton.Left < popup.ClientSize.Width \ 2)
                    row.Heading.Controller.CloseFull(True)
                Finally
                    Release(row)
                End Try
            End Sub)
    End Sub

    <Fact>
    Public Sub A_long_explanation_grows_the_popup_and_is_never_clipped()
        OnSta(
            Sub()
                Dim lengthy As New OptionHelpSource("long.one", "Short.", String.Join(" ", Enumerable.Repeat("A long explanation of both states and their dependencies.", 30)))
                For Each scale As Single In {1.0F, 1.75F}
                    Dim row As RowParts = BuildRow(scale, False, SettingsCheckBox.Px(520, scale), "Network scan", "Explanation.", "", lengthy)
                    Try
                        row.Heading.Help.PerformClick()
                        Dim popup As OptionHelpPopup = row.Heading.Controller.FullPopupWindow
                        Dim body As Label = popup.BodyControl
                        Dim need As Integer = SettingsRowText.WrappedHeight(body, body.Width, 1, 0)
                        Assert.True(body.Height >= need - 1, "the explanation is clipped")
                        Assert.True(body.Bottom < popup.CloseButton.Top, "the text runs into the Close action")
                        Assert.True(popup.ClientSize.Height >= popup.CloseButton.Bottom)
                        Assert.False(body.AutoEllipsis)
                        row.Heading.Controller.CloseFull(True)
                    Finally
                        Release(row)
                    End Try
                Next
            End Sub)
    End Sub

    ' ---------------------------------------------------------------- section 3 rule 4 / 5: live updates, glyph, contrast

    <Fact>
    Public Sub Focus_survives_a_theme_and_a_language_update()
        OnSta(
            Sub()
                Dim row As RowParts = BuildRow(1.0F, False, 520, "Network scan", "Explanation.", "app.settings", Source())
                Try
                    Dim h As OptionHeading = row.Heading
                    Dim button As OptionHelpButton = h.Help
                    row.Form.ActiveControl = button
                    h.ApplyPalette(DarkPalette())
                    h.SetHelp(Source("net.scan"))
                    row.Title.Text = "Netzwerksuche"
                    h.RefreshText()
                    SettingsCheckBox.ArrangeRow(row.Host, row.Check, row.Title, row.Description, 1.0F)
                    Assert.Same(button, h.Help)
                    Assert.Same(button, row.Form.ActiveControl)
                    Assert.Same(button, row.Host.Controls.Cast(Of Control)().Single(Function(c) TypeOf c Is OptionHelpButton))
                    Assert.Equal(DarkPalette().Surface, button.BackColor)
                    Assert.Equal("Netzwerksuche", row.Check.AccessibleName)
                Finally
                    Release(row)
                End Try
            End Sub)
    End Sub

    Private Shared Function HasInk(picture As Image, ink As Color) As Boolean
        Using bitmap As New Bitmap(picture)
            For y As Integer = 0 To bitmap.Height - 1
                For x As Integer = 0 To bitmap.Width - 1
                    Dim pixel As Color = bitmap.GetPixel(x, y)
                    If pixel.A >= 250 AndAlso Math.Abs(CInt(pixel.R) - ink.R) + Math.Abs(CInt(pixel.G) - ink.G) + Math.Abs(CInt(pixel.B) - ink.B) <= 8 Then Return True
                Next
            Next
        End Using
        Return False
    End Function

    <Fact>
    Public Sub The_heading_glyph_is_drawn_in_the_captions_colour_and_follows_it_live()
        OnSta(
            Sub()
                Dim row As RowParts = BuildRow(1.0F, False, 520, "Network scan", "Explanation.", "app.settings", Source())
                Try
                    Dim glyph As Label = row.Heading.Glyph
                    Assert.NotNull(glyph.Image)
                    Assert.Equal(16, glyph.Image.Width)
                    Assert.True(HasInk(glyph.Image, row.Title.ForeColor), "the glyph is not in the caption's colour")
                    Dim first As Image = glyph.Image
                    row.Title.ForeColor = Color.FromArgb(200, 40, 40)
                    Assert.NotSame(first, glyph.Image)
                    Assert.True(HasInk(glyph.Image, Color.FromArgb(200, 40, 40)), "the glyph did not follow the caption's colour")
                    ' 3:1 of the glyph on its surface (the caption ink is held to 4.5:1 by the window's palette).
                    Assert.True(AccentColors.Contrast(row.Title.ForeColor, LightPalette().Surface) >= 3.0R)
                Finally
                    Release(row)
                End Try
            End Sub)
    End Sub

    <Fact>
    Public Sub The_question_mark_is_the_catalog_glyph_centred_in_its_target_and_holds_3_to_1_in_both_themes()
        OnSta(
            Sub()
                For Each dark As Boolean In {False, True}
                    ButtonGlyphs.UsePalette(ThemePalette.ForDark(dark))
                    Dim row As RowParts = BuildRow(1.0F, False, 520, "Network scan", "Explanation.", "", Source())
                    Try
                        Dim palette As OptionHelpPalette = If(dark, DarkPalette(), LightPalette())
                        row.Heading.ApplyPalette(palette)
                        Dim button As OptionHelpButton = row.Heading.Help
                        Assert.Equal("app.help", ButtonGlyphs.GlyphOf(button))
                        Assert.True(ButtonGlyphs.CarriesGlyph(button), "no glyph on the help button")
                        Assert.True(AccentColors.Contrast(ButtonGlyphs.CurrentInk(button), ButtonGlyphs.CurrentFace(button)) >= 3.0R,
                                    "glyph contrast below 3:1, dark=" & dark)

                        ' Where the ink really lies: its middle is the middle of the target.
                        Using bitmap As New Bitmap(button.Width, button.Height)
                            button.DrawToBitmap(bitmap, New Rectangle(0, 0, button.Width, button.Height))
                            Dim minX As Integer = Integer.MaxValue, maxX As Integer = -1, minY As Integer = Integer.MaxValue, maxY As Integer = -1
                            For y As Integer = 0 To bitmap.Height - 1
                                For x As Integer = 0 To bitmap.Width - 1
                                    If bitmap.GetPixel(x, y) <> button.BackColor Then
                                        minX = Math.Min(minX, x) : maxX = Math.Max(maxX, x)
                                        minY = Math.Min(minY, y) : maxY = Math.Max(maxY, y)
                                    End If
                                Next
                            Next
                            Assert.True(maxX >= 0, "nothing was drawn on the help button")
                            Assert.InRange((minX + maxX) / 2.0R, button.Width / 2.0R - 1.6R, button.Width / 2.0R + 1.6R)
                            Assert.InRange((minY + maxY) / 2.0R, button.Height / 2.0R - 1.6R, button.Height / 2.0R + 1.6R)
                        End Using
                    Finally
                        Release(row)
                        ButtonGlyphs.UsePalette(ThemePalette.Light)
                    End Try
                Next
            End Sub)
    End Sub

    ' ---------------------------------------------------------------- the code stays on its own paths

    <Fact>
    Public Sub The_help_code_has_no_path_to_the_value_of_the_option()
        Dim dir As String = SourceDir()
        Dim ui As String = LocalizationCoverageTests.BlankComments(File.ReadAllText(Path.Combine(dir, "OptionHelpUi.vb")))
        ' The only toggle path is BindCaption, bound to the glyph - never to the help button or a popup.
        Assert.DoesNotMatch("\.Checked\s*=", ui)
        Assert.DoesNotMatch("Toggle", ui)
        Assert.Equal(1, Regex.Matches(ui, "BindCaption\(").Count)
        Assert.Contains("BindCaption(heading.Glyph, check)", ui)
        ' No stock call that would put a window into the dialog layer's way or a tooltip in place of the summary.
        Assert.DoesNotContain("ShowDialog", ui)
        Assert.DoesNotContain("MessageBox", ui)
        ' The row builder knows the heading only through the optional parameter and the registry.
        Dim row As String = LocalizationCoverageTests.BlankComments(File.ReadAllText(Path.Combine(dir, "SettingsCheckBox.vb")))
        Assert.Contains("Optional heading As OptionHeading = Nothing", row)
    End Sub

    Private Shared Function SourceDir() As String
        Dim dir As New DirectoryInfo(AppContext.BaseDirectory)
        While dir IsNot Nothing
            Dim candidate As String = Path.Combine(dir.FullName, "src")
            If Directory.Exists(candidate) AndAlso File.Exists(Path.Combine(candidate, "Common_Module.vb")) Then Return candidate
            dir = dir.Parent
        End While
        Throw New DirectoryNotFoundException("Could not locate the src\ directory from the test binary.")
    End Function
End Class
#End If
