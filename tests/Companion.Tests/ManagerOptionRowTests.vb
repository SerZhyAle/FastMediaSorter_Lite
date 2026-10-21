Option Explicit On
Option Strict On
Option Infer On
Option Compare Binary

Imports System.Collections.Generic
Imports System.Drawing
Imports System.Globalization
Imports System.IO
Imports System.Linq
Imports System.Reflection
Imports System.Text.RegularExpressions
Imports System.Threading
Imports System.Windows.Forms
Imports FastMediaSorterCompanion
Imports Xunit

''' <summary>
''' BOOLEAN-OPTION 2.0 in the Share Manager (ticket 243, slice T-11c-b1): the 30 check boxes of the scheduled-operation editor
''' (15) and of the folder parameters window (15) stand in <see cref="OptionRowHost"/> rows made by <see cref="OptionRows"/>
''' - the viewer's own row, linked: a drawn box of at least 20 px inside a 32 px hit target at the reading start, a caption
''' that toggles once, an explanation that never toggles, one accessible check box, and a row as tall as its content.
''' The real windows are built and shown on an STA thread, at 100 % and 175 % (the scale is EMULATED the way the other
''' layout tests do it: a larger font plus <c>Control.Scale</c>), in left-to-right and right-to-left languages. A click is the
''' <c>OnClick</c> a mouse up raises, and the Space route is the Click of the focused native box - how a window LOOKS under a
''' real pointer, on a 150-200 % monitor, in High Contrast or under Narrator stays manual acceptance.
''' The collection is the scheduled one (no parallelism): the windows read and write the settings hive, which a scratch
''' hive stands in for, and the tests set the UI language.
''' </summary>
<Collection(ScheduledCollection.Name)>
Public Class ManagerOptionRowTests

    ' --- the subjects ----------------------------------------------------------------------------------

    ''' <summary>One window with the check boxes it holds, in the order they were authored (which is their Tab order).</summary>
    Private NotInheritable Class Subject
        Friend ReadOnly Window As String
        Friend ReadOnly Make As Func(Of Kit, Form)
        Friend ReadOnly Setup As Action(Of Form)
        ''' <summary>Field name of each check box (an array element is name[i]) and its Checked value in a fresh window.</summary>
        Friend ReadOnly Fields As List(Of KeyValuePair(Of String, Boolean))
        ''' <summary>The Name each check box had and keeps (empty for a window that never named them).</summary>
        Friend ReadOnly Names As Dictionary(Of String, String)
        ''' <summary>The fields whose check box starts disabled in a fresh window.</summary>
        Friend ReadOnly StartsDisabled As HashSet(Of String)

        Friend Sub New(window As String, make As Func(Of Kit, Form), setup As Action(Of Form), fields As List(Of KeyValuePair(Of String, Boolean)),
                       names As Dictionary(Of String, String), startsDisabled As HashSet(Of String))
            Me.Window = window
            Me.Make = make
            Me.Setup = setup
            Me.Fields = fields
            Me.Names = names
            Me.StartsDisabled = startsDisabled
        End Sub

        Public Overrides Function ToString() As String
            Return Window
        End Function
    End Class

    Private Shared Function Pair(key As String, checkedAtStart As Boolean) As KeyValuePair(Of String, Boolean)
        Return New KeyValuePair(Of String, Boolean)(key, checkedAtStart)
    End Function

    Private Shared Function ScheduledSubject() As Subject
        Dim fields As New List(Of KeyValuePair(Of String, Boolean)) From {
            Pair("chkRecursive", True), Pair("chkDeleteOriginals", False),
            Pair("chkAllFiles", True), Pair("chkImages", False), Pair("chkAudio", False), Pair("chkVideo", False), Pair("chkDocuments", False),
            Pair("chkOlder", False), Pair("chkYounger", False), Pair("chkLarger", False), Pair("chkSmaller", False), Pair("chkHidden", False),
            Pair("chkEnabled", True), Pair("chkRetryMissed", True), Pair("chkBalloonOnError", False)}
        Dim names As New Dictionary(Of String, String)(StringComparer.Ordinal) From {
            {"chkRecursive", "chkSchedRecursive"}, {"chkDeleteOriginals", "chkSchedDeleteOriginals"},
            {"chkAllFiles", "chkSchedAllFiles"}, {"chkImages", "chkSchedImages"}, {"chkAudio", "chkSchedAudio"},
            {"chkVideo", "chkSchedVideo"}, {"chkDocuments", "chkSchedDocuments"},
            {"chkOlder", "chkSchedOlder"}, {"chkYounger", "chkSchedYounger"}, {"chkLarger", "chkSchedLarger"},
            {"chkSmaller", "chkSchedSmaller"}, {"chkHidden", "chkSchedHidden"},
            {"chkEnabled", "chkSchedEnabled"}, {"chkRetryMissed", "chkSchedRetryMissed"}, {"chkBalloonOnError", "chkSchedBalloonOnError"}}
        Return New Subject("Scheduled_Operation_Form",
                           Function(kit As Kit) New Scheduled_Operation_Form(kit.Engine, Nothing),
                           Sub(form As Form)
                               ' An archive offers to delete its originals, so all 15 rows stand in the page; the conditions open.
                               Find(Of ComboBox)(form, "cmbSchedAction").SelectedIndex = CInt(ScheduledAction.Archive)
                               Find(Of Control)(form, "pnlSchedConditions").Visible = True
                           End Sub,
                           fields, names, New HashSet(Of String)(StringComparer.Ordinal) From {"chkImages", "chkAudio", "chkVideo", "chkDocuments"})
    End Function

    Private Shared Function RootParamsSubject() As Subject
        Dim fields As New List(Of KeyValuePair(Of String, Boolean))()
        For i As Integer = 0 To 7
            fields.Add(Pair("_mediaChecks[" & i.ToString(CultureInfo.InvariantCulture) & "]", False))
        Next
        fields.AddRange({Pair("chkScanSub", True), Pair("chkSubItems", False), Pair("chkHidden", False), Pair("chkAllFiles", False),
                         Pair("chkReadOnly", False), Pair("chkSoftReadOnly", False), Pair("chkDestination", False)})
        Return New Subject("Share_Root_Params_Form",
                           Function(kit As Kit) New Share_Root_Params_Form("Photos", New ShareRootParams()),
                           Nothing, fields, New Dictionary(Of String, String)(StringComparer.Ordinal), New HashSet(Of String)(StringComparer.Ordinal))
    End Function

    Private Shared Function Subjects() As List(Of Subject)
        Return New List(Of Subject) From {ScheduledSubject(), RootParamsSubject()}
    End Function

    ''' <summary>What the windows need: a scratch settings hive, an empty store of folder parameters, a scheduled engine over a
    ''' scratch folder. Made once per test, removed after it.</summary>
    Private NotInheritable Class Kit
        Implements IDisposable

        Private Shared ReadOnly MapField As FieldInfo = GetType(ShareRootParamsStore).GetField("_map", BindingFlags.NonPublic Or BindingFlags.Static)

        Friend ReadOnly Tree As New ScratchTree()
        Friend ReadOnly Hive As New ScratchHive()
        Friend ReadOnly Engine As ScheduledEngine
        Private ReadOnly oldMap As Object

        Friend Sub New()
            oldMap = MapField.GetValue(Nothing)
            MapField.SetValue(Nothing, Nothing)
            Dim folder As String = Tree.Dir("state")
            Engine = New ScheduledEngine(New ScheduledStore(folder), New ScheduledJournal(folder),
                                         New ScheduledServices With {.Clock = Function() New DateTime(2026, 10, 13, 12, 0, 0, DateTimeKind.Unspecified)},
                                         New RecordingTimers())
            Engine.Start()
        End Sub

        Public Sub Dispose() Implements IDisposable.Dispose
            Engine.Dispose()
            MapField.SetValue(Nothing, oldMap)
            Hive.Dispose()
            Tree.Dispose()
        End Sub
    End Class

    ' --- helpers ---------------------------------------------------------------------------------------

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
        Assert.True(thread.Join(240000), "the UI check did not finish")
        If failure IsNot Nothing Then System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(failure).Throw()
    End Sub

    ''' <summary>Runs a check on an STA thread with the UI language set and everything put back afterwards. A control that throws
    ''' while it paints or handles a message fails the test instead of opening WinForms' modal "unhandled exception" box.</summary>
    Private Shared Sub InLanguage(code As String, check As Action(Of Kit))
        OnSta(Sub()
                  Dim saved As String = Localization.CurrentCode
                  Dim raised As New List(Of Exception)()
                  Dim handler As ThreadExceptionEventHandler = Sub(sender As Object, e As ThreadExceptionEventArgs) raised.Add(e.Exception)
                  Try
                      Localization.CurrentCode = code
                      ButtonGlyphs.AssetOpener = AddressOf CompanionAssets.OpenBundledAsset
                      Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException)
                      AddHandler Application.ThreadException, handler
                      Using kit As New Kit()
                          check(kit)
                      End Using
                  Finally
                      RemoveHandler Application.ThreadException, handler
                      Localization.CurrentCode = saved
                  End Try
                  Assert.True(raised.Count = 0, "a control raised while painting or handling a message: " & String.Join(vbLf, raised.Select(Function(x) x.ToString())))
              End Sub)
    End Sub

    Private Shared Function Find(Of T As Control)(root As Control, name As String) As T
        For Each child As Control In root.Controls
            If child.Name = name AndAlso TypeOf child Is T Then Return DirectCast(child, T)
            Dim nested As T = Find(Of T)(child, name)
            If nested IsNot Nothing Then Return nested
        Next
        Return Nothing
    End Function

    Private Shared Function AllOf(parent As Control) As List(Of Control)
        Dim found As New List(Of Control)()
        For Each c As Control In parent.Controls
            found.Add(c)
            found.AddRange(AllOf(c))
        Next
        Return found
    End Function

    ''' <summary>A window shown at a scale, released together with what it needed.</summary>
    Private NotInheritable Class Shown
        Implements IDisposable

        Friend ReadOnly Form As Form

        Friend Sub New(form As Form)
            Me.Form = form
        End Sub

        Public Sub Dispose() Implements IDisposable.Dispose
            Try
                Form.Close()
            Finally
                Form.Dispose()
            End Try
        End Sub
    End Class

    ''' <summary>Builds the subject's window in the current language, emulates a display scale, shows it (OnLoad arranges the
    ''' rows) and applies the subject's set-up.</summary>
    Private Shared Function Open(subject As Subject, kit As Kit, factor As Single, Optional applySetup As Boolean = True) As Shown
        Dim form As Form = subject.Make(kit)
        Try
            Dim extra As Single = factor / (form.DeviceDpi / 96.0F)
            If Math.Abs(extra - 1.0F) > 0.01F Then
                form.Font = New Font(form.Font.FontFamily, form.Font.SizeInPoints * extra)
                form.Scale(New SizeF(extra, extra))
            End If
            form.StartPosition = FormStartPosition.Manual
            form.Location = New Point(20, 20)
            form.Show()
            Application.DoEvents()
            If applySetup AndAlso subject.Setup IsNot Nothing Then
                subject.Setup(form)
                Application.DoEvents()
            End If
            Return New Shown(form)
        Catch
            form.Dispose()
            Throw
        End Try
    End Function

    Private Shared Function Mark(subject As Subject, code As String, factor As Single) As String
        Return subject.Window & " " & code & " @" & CInt(factor * 100).ToString(CultureInfo.InvariantCulture) & "%"
    End Function

    ''' <summary>The field name of every check box the window keeps in a field (an array element is name[i]).</summary>
    Private Shared Function KeysOf(form As Form) As Dictionary(Of CheckBox, String)
        Dim map As New Dictionary(Of CheckBox, String)()
        For Each f As FieldInfo In form.GetType().GetFields(BindingFlags.Instance Or BindingFlags.NonPublic Or BindingFlags.Public)
            Dim value As Object = f.GetValue(form)
            Dim one As CheckBox = TryCast(value, CheckBox)
            If one IsNot Nothing Then
                map(one) = f.Name
                Continue For
            End If
            Dim many As CheckBox() = TryCast(value, CheckBox())
            If many IsNot Nothing Then
                For i As Integer = 0 To many.Length - 1
                    map(many(i)) = f.Name & "[" & i.ToString(CultureInfo.InvariantCulture) & "]"
                Next
            End If
        Next
        Return map
    End Function

    ''' <summary>The check box of a field of the subject, found through the window's own field.</summary>
    Private Shared Function CheckOf(form As Form, key As String) As CheckBox
        For Each pair As KeyValuePair(Of CheckBox, String) In KeysOf(form)
            If String.Equals(pair.Value, key, StringComparison.Ordinal) Then Return pair.Key
        Next
        Throw New InvalidOperationException("no check box field " & key & " in " & form.GetType().Name)
    End Function

    Private Shared Function RowOf(check As CheckBox) As OptionRowHost
        Dim row As OptionRowHost = TryCast(check.Parent, OptionRowHost)
        Assert.True(row IsNot Nothing, check.Name & " does not stand in an option row")
        Return row
    End Function

    ''' <summary>Raises Click the way a mouse up over the control does (Control.OnClick is protected).</summary>
    Private Shared Sub ClickOn(c As Control)
        GetType(Control).GetMethod("OnClick", BindingFlags.Instance Or BindingFlags.NonPublic).Invoke(c, New Object() {EventArgs.Empty})
    End Sub

    Private Shared Function OnForm(form As Form, c As Control) As Rectangle
        Return form.RectangleToClient(c.RectangleToScreen(c.ClientRectangle))
    End Function

    ''' <summary>The part of a control that can be reached with a pointer: its rectangle cut by every container between it and the
    ''' window (a row scrolled out of its panel is not under the docked answer row).</summary>
    Private Shared Function ClippedOnForm(form As Form, c As Control) As Rectangle
        Dim visible As Rectangle = OnForm(form, c)
        Dim p As Control = c.Parent
        While p IsNot Nothing AndAlso Not Object.ReferenceEquals(p, form)
            visible = Rectangle.Intersect(visible, OnForm(form, p))
            p = p.Parent
        End While
        Return visible
    End Function

    ''' <summary>The bounding box of what the check box really paints on its surface: the box with its ring, measured on the pixels.</summary>
    Private Shared Function PaintedBox(row As OptionRowHost) As Rectangle
        Dim check As CheckBox = row.Check
        Dim surface As Color = Color.FromArgb(240, 240, 240)
        row.ApplyColors(surface, Color.White, Color.FromArgb(110, 110, 110), Color.FromArgb(20, 20, 20), Color.FromArgb(0, 90, 200), Color.White)
        Application.DoEvents()
        Using bitmap As New Bitmap(check.Width, check.Height)
            check.DrawToBitmap(bitmap, New Rectangle(0, 0, check.Width, check.Height))
            Dim minX As Integer = Integer.MaxValue
            Dim minY As Integer = Integer.MaxValue
            Dim maxX As Integer = -1
            Dim maxY As Integer = -1
            For y As Integer = 0 To bitmap.Height - 1
                For x As Integer = 0 To bitmap.Width - 1
                    Dim p As Color = bitmap.GetPixel(x, y)
                    If Math.Abs(CInt(p.R) - surface.R) + Math.Abs(CInt(p.G) - surface.G) + Math.Abs(CInt(p.B) - surface.B) > 24 Then
                        minX = Math.Min(minX, x)
                        minY = Math.Min(minY, y)
                        maxX = Math.Max(maxX, x)
                        maxY = Math.Max(maxY, y)
                    End If
                Next
            Next
            If maxX < 0 Then Return Rectangle.Empty
            Return New Rectangle(minX, minY, maxX - minX + 1, maxY - minY + 1)
        End Using
    End Function

    ''' <summary>True when a label's wrapped text fits the height the label has.</summary>
    Private Shared Function TextFits(l As Label) As Boolean
        If l Is Nothing OrElse Not l.Visible OrElse l.Text.Length = 0 Then Return True
        Dim flags As TextFormatFlags = TextFormatFlags.WordBreak Or If(l.UseMnemonic, TextFormatFlags.Default, TextFormatFlags.NoPrefix)
        Dim need As Integer = TextRenderer.MeasureText(l.Text, l.Font, New Size(Math.Max(1, l.Width - l.Padding.Horizontal), Integer.MaxValue), flags).Height
        Return need <= l.Height + 2
    End Function

    Private Shared Function Describe(c As Control) As String
        Return If(String.IsNullOrEmpty(c.Name), c.GetType().Name & "(" & c.Text & ")", c.Name)
    End Function

    ' --- the subjects are what they were -------------------------------------------------------------

    <Fact>
    Public Sub The_two_windows_hold_thirty_check_boxes_and_every_one_stands_in_an_option_row()
        InLanguage("en",
            Sub(kit As Kit)
                Dim total As Integer = 0
                For Each subject As Subject In Subjects()
                    Using shown As Shown = Open(subject, kit, 1.0F)
                        Dim keys As Dictionary(Of CheckBox, String) = KeysOf(shown.Form)
                        Assert.Equal(15, subject.Fields.Count)
                        Assert.Equal(subject.Fields.Select(Function(f) f.Key).OrderBy(Function(k) k, StringComparer.Ordinal).ToArray(),
                                     keys.Values.OrderBy(Function(k) k, StringComparer.Ordinal).ToArray())
                        Dim everyCheck As List(Of CheckBox) = AllOf(shown.Form).OfType(Of CheckBox)().ToList()
                        Assert.Equal(15, everyCheck.Count)
                        For Each check As CheckBox In everyCheck
                            Assert.True(TypeOf check.Parent Is OptionRowHost, subject.Window & ": " & keys(check) & " is a native check box on " & check.Parent.GetType().Name)
                            Assert.True(SettingsCheckBox.IsAttached(check), subject.Window & ": " & keys(check) & " is not the drawn Settings box")
                        Next
                        total += everyCheck.Count
                    End Using
                Next
                Assert.Equal(30, total)
            End Sub)
    End Sub

    <Fact>
    Public Sub The_check_boxes_keep_their_names_defaults_and_state_and_the_row_follows_the_box_when_it_is_disabled()
        InLanguage("en",
            Sub(kit As Kit)
                For Each subject As Subject In Subjects()
                    Using shown As Shown = Open(subject, kit, 1.0F, applySetup:=False)
                        For Each field As KeyValuePair(Of String, Boolean) In subject.Fields
                            Dim check As CheckBox = CheckOf(shown.Form, field.Key)
                            Dim row As OptionRowHost = RowOf(check)
                            Dim lbl As String = subject.Window & "/" & field.Key
                            Assert.True(check.Checked = field.Value, lbl & ": Checked default")
                            Dim name As String = Nothing
                            Assert.Equal(If(subject.Names.TryGetValue(field.Key, name), name, ""), check.Name)
                            Assert.Equal(If(name Is Nothing, "", name & "_Row"), row.Name)
                            Assert.Equal(If(name Is Nothing, "", name & "_Caption"), row.Caption.Name)
                            Assert.Equal(String.Empty, check.Text)
                            Assert.NotEqual(String.Empty, row.Caption.Text)
                            Assert.Equal(check.Enabled, row.Caption.Enabled)
                            Assert.Equal(Not subject.StartsDisabled.Contains(field.Key), check.Enabled)
                        Next
                    End Using
                Next
            End Sub)
    End Sub

    ''' <summary>The destination note is the destination's explanation now: it stands under its caption for good, in the same
    ''' row, and the check box carries it as its description.</summary>
    <Fact>
    Public Sub The_destination_note_is_the_explanation_of_the_destination_row()
        InLanguage("en",
            Sub(kit As Kit)
                Dim subject As Subject = RootParamsSubject()
                Using shown As Shown = Open(subject, kit, 1.0F)
                    Dim check As CheckBox = CheckOf(shown.Form, "chkDestination")
                    Dim row As OptionRowHost = RowOf(check)
                    Assert.NotNull(row.Explanation)
                    Assert.True(row.Explanation.Visible, "the explanation stands under the caption without the box being ticked")
                    Assert.Equal(row.Explanation.Text, check.AccessibleDescription)
                    Assert.Equal(3, row.Controls.Count)
                    Assert.False(check.Checked)
                    ' every other row of the two windows has no explanation
                    For Each other As OptionRowHost In AllOf(shown.Form).OfType(Of OptionRowHost)()
                        If Not Object.ReferenceEquals(other, row) Then Assert.Null(other.Explanation)
                    Next
                End Using
            End Sub)
    End Sub

    ''' <summary>The row hides and shows as one: a caption left behind by a hidden box would be the old bug in a new place.</summary>
    <Fact>
    Public Sub The_delete_originals_row_hides_and_shows_with_the_action_caption_and_all()
        InLanguage("en",
            Sub(kit As Kit)
                Using shown As Shown = Open(ScheduledSubject(), kit, 1.0F, applySetup:=False)
                    Dim form As Form = shown.Form
                    Dim row As OptionRowHost = RowOf(CheckOf(form, "chkDeleteOriginals"))
                    Dim action As ComboBox = Find(Of ComboBox)(form, "cmbSchedAction")
                    Assert.Equal(CInt(ScheduledAction.Copy), action.SelectedIndex)
                    Assert.False(row.Visible, "a copy offers no deletion of originals")
                    Assert.False(row.Caption.Visible)
                    action.SelectedIndex = CInt(ScheduledAction.Archive)
                    Application.DoEvents()
                    Assert.True(row.Visible)
                    Assert.True(row.Caption.Visible)
                    action.SelectedIndex = CInt(ScheduledAction.Delete)
                    Application.DoEvents()
                    Assert.False(row.Visible)
                    Assert.False(row.Caption.Visible)
                    ' the other rows of the page stay
                    Assert.True(RowOf(CheckOf(form, "chkRecursive")).Visible)
                    Assert.True(RowOf(CheckOf(form, "chkEnabled")).Visible)
                End Using
            End Sub)
    End Sub

    ''' <summary>A hidden control takes no cell in a TableLayoutPanel, so a page row that hides must hide its empty cell too: in
    ''' every shape of the editor each field and each full-width row stands in the wide column and each caption in the narrow
    ''' one (a shift by one cell put the full-width rows into the narrow column).</summary>
    <Fact>
    Public Sub The_editor_page_keeps_its_two_columns_in_every_shape()
        InLanguage("en",
            Sub(kit As Kit)
                Using shown As Shown = Open(ScheduledSubject(), kit, 1.0F, applySetup:=False)
                    Dim form As Form = shown.Form
                    Dim action As ComboBox = Find(Of ComboBox)(form, "cmbSchedAction")
                    Dim page As TableLayoutPanel = DirectCast(Find(Of TextBox)(form, "txtSchedName").Parent, TableLayoutPanel)
                    Dim conditions As Button = Find(Of Button)(form, "btnSchedConditions")
                    Dim problems As New List(Of String)()
                    For Each kind As ScheduledAction In New ScheduledAction() {ScheduledAction.Copy, ScheduledAction.Move, ScheduledAction.Delete, ScheduledAction.Archive}
                        For Each open As Boolean In New Boolean() {False, True}
                            action.SelectedIndex = CInt(kind)
                            If Find(Of Control)(form, "pnlSchedConditions").Visible <> open Then conditions.PerformClick()
                            Application.DoEvents()
                            Dim shape As String = kind.ToString() & If(open, " + conditions", "")
                            For Each c As Control In page.Controls
                                If Not c.Visible Then Continue For
                                Dim lab As Label = TryCast(c, Label)
                                If lab IsNot Nothing AndAlso lab.Text.Length = 0 AndAlso lab.Name.Length = 0 Then Continue For ' an empty cell
                                Dim wanted As Integer = If(lab IsNot Nothing AndAlso lab.Name.Length = 0, 0, 1)
                                ' GetColumn and GetCellPosition answer -1 for a control the table placed itself (auto-flow); where the
                                ' control stands against the first column's right edge is the real answer (this check runs in English: ltr).
                                Dim column As Integer = If(c.Left < page.Padding.Left + page.GetColumnWidths()(0), 0, 1)
                                If column <> wanted Then problems.Add(shape & ": " & Describe(c) & " stands in column " & column.ToString(CultureInfo.InvariantCulture) & ", not " & wanted.ToString(CultureInfo.InvariantCulture))
                                Dim row As OptionRowHost = TryCast(c, OptionRowHost)
                                If row IsNot Nothing AndAlso row.Width < page.Width \ 3 Then problems.Add(shape & ": " & Describe(row.Check) & " is " & row.Width.ToString(CultureInfo.InvariantCulture) & " px wide in a " & page.Width.ToString(CultureInfo.InvariantCulture) & " px page")
                            Next
                        Next
                    Next
                    Assert.True(problems.Count = 0, String.Join(vbLf, problems))
                End Using
            End Sub)
    End Sub

    ' --- the size of the box and its target, the layout, the overlaps -------------------------------------

    <Fact>
    Public Sub The_box_is_at_least_20_px_and_its_hit_target_at_least_32_px_at_100_and_175_percent()
        InLanguage("en",
            Sub(kit As Kit)
                Dim found As New List(Of String)()
                For Each factor As Single In New Single() {1.0F, 1.75F}
                    For Each subject As Subject In Subjects()
                        Using shown As Shown = Open(subject, kit, factor)
                            For Each pair As KeyValuePair(Of CheckBox, String) In KeysOf(shown.Form)
                                Dim check As CheckBox = pair.Key
                                Dim row As OptionRowHost = RowOf(check)
                                Dim lbl As String = Mark(subject, "en", factor) & " " & pair.Value
                                Dim seen As Single = check.Width / CSng(UiSizing.PointerTarget)
                                If Math.Abs(seen - factor) > 0.05F Then found.Add(lbl & ": the row is laid out at " & seen.ToString("0.00", CultureInfo.InvariantCulture) & ", not at the emulated scale")
                                Dim target As Integer = SettingsCheckBox.Px(UiSizing.PointerTarget, factor)
                                If check.Width < target - 1 OrElse check.Height < target - 1 Then found.Add(lbl & ": the hit target is " & check.Size.ToString() & ", needs " & target.ToString(CultureInfo.InvariantCulture))
                                Dim side As Integer = SettingsCheckBox.Px(20, factor)
                                Dim box As Rectangle = PaintedBox(row)
                                If box.Width < side OrElse box.Height < side Then found.Add(lbl & ": the painted box is " & box.Size.ToString() & ", needs " & side.ToString(CultureInfo.InvariantCulture))
                                If SettingsCheckBox.BoxSide(factor) < side Then found.Add(lbl & ": BoxSide is below 20 logical px")
                            Next
                        End Using
                    Next
                Next
                Assert.True(found.Count = 0, String.Join(vbLf, found))
            End Sub)
    End Sub

    <Theory>
    <InlineData("en")>
    <InlineData("de")>
    <InlineData("ru")>
    <InlineData("ar")>
    Public Sub No_row_clips_or_overlaps_and_no_hit_target_reaches_a_neighbour_at_100_and_175_percent(code As String)
        InLanguage(code,
            Sub(kit As Kit)
                Dim found As New List(Of String)()
                For Each factor As Single In New Single() {1.0F, 1.75F}
                    For Each subject As Subject In Subjects()
                        Using shown As Shown = Open(subject, kit, factor)
                            Audit(subject, shown.Form, Mark(subject, code, factor), factor, found)
                        End Using
                    Next
                Next
                Assert.True(found.Count = 0, String.Join(vbLf, found))
            End Sub)
    End Sub

    Private Shared Sub Audit(subject As Subject, form As Form, lbl As String, factor As Single, found As List(Of String))
        Dim keys As Dictionary(Of CheckBox, String) = KeysOf(form)
        Dim rows As List(Of OptionRowHost) = AllOf(form).OfType(Of OptionRowHost)().ToList()
        If rows.Count <> subject.Fields.Count Then found.Add(lbl & ": " & rows.Count.ToString(CultureInfo.InvariantCulture) & " rows, expected " & subject.Fields.Count.ToString(CultureInfo.InvariantCulture))

        For Each row As OptionRowHost In rows
            Dim check As CheckBox = row.Check
            Dim tag As String = lbl & " " & If(keys.ContainsKey(check), keys(check), Describe(check))
            If Not row.Visible Then
                found.Add(tag & ": the row is not visible")
                Continue For
            End If

            ' The row inside the container that holds it, and over none of its neighbours.
            Dim host As Control = row.Parent
            Dim client As Rectangle = host.ClientRectangle
            If row.Left < -1 OrElse row.Top < -1 OrElse row.Right > client.Width + 1 OrElse row.Bottom > client.Height + 1 Then
                found.Add(tag & ": the row " & row.Bounds.ToString() & " leaves its container " & client.Size.ToString())
            End If
            For Each s As Control In host.Controls
                If Object.ReferenceEquals(s, row) OrElse Not s.Visible Then Continue For
                Dim overlap As Rectangle = Rectangle.Intersect(row.Bounds, s.Bounds)
                If overlap.Width > 1 AndAlso overlap.Height > 1 Then found.Add(tag & ": the row " & row.Bounds.ToString() & " lies over " & Describe(s) & " " & s.Bounds.ToString())
            Next

            ' Inside the row: the box, the caption and the explanation stay in it, none on another, no text cut.
            Dim inner As New List(Of Control) From {check, row.Caption}
            If row.Explanation IsNot Nothing AndAlso row.Explanation.Visible Then inner.Add(row.Explanation)
            Dim rowClient As Rectangle = row.ClientRectangle
            For i As Integer = 0 To inner.Count - 1
                Dim c As Control = inner(i)
                If c.Left < -1 OrElse c.Top < -1 OrElse c.Right > rowClient.Width + 1 OrElse c.Bottom > rowClient.Height + 1 Then
                    found.Add(tag & ": " & Describe(c) & " " & c.Bounds.ToString() & " leaves its row " & rowClient.Size.ToString())
                End If
                For j As Integer = i + 1 To inner.Count - 1
                    Dim overlap As Rectangle = Rectangle.Intersect(c.Bounds, inner(j).Bounds)
                    If overlap.Width > 1 AndAlso overlap.Height > 1 Then found.Add(tag & ": " & Describe(c) & " " & c.Bounds.ToString() & " lies over " & Describe(inner(j)) & " " & inner(j).Bounds.ToString())
                Next
            Next
            If Not TextFits(row.Caption) Then found.Add(tag & ": the caption '" & row.Caption.Text & "' is cut (" & row.Caption.Height.ToString(CultureInfo.InvariantCulture) & " px high, " & row.Caption.Width.ToString(CultureInfo.InvariantCulture) & " wide)")
            If Not TextFits(row.Explanation) Then found.Add(tag & ": the explanation is cut")
            If row.Caption.Bottom > row.Height OrElse (row.Explanation IsNot Nothing AndAlso row.Explanation.Visible AndAlso row.Explanation.Bottom > row.Height) Then
                found.Add(tag & ": the row is " & row.Height.ToString(CultureInfo.InvariantCulture) & " px high and its text reaches below it")
            End If

            ' The row is as tall as its content needs: asking it again changes nothing.
            Dim before As Integer = row.Height
            DialogOptionRow.NeededHeight(row)
            If Math.Abs(row.Height - before) > 1 Then found.Add(tag & ": the row was " & before.ToString(CultureInfo.InvariantCulture) & " px high and needs " & row.Height.ToString(CultureInfo.InvariantCulture))
            If check.Width < SettingsCheckBox.Px(UiSizing.PointerTarget, factor) - 1 Then found.Add(tag & ": the hit target is narrower than 32 px at this scale")
        Next

        ' Hit areas: every check box's target against every other target and every other control that takes a pointer.
        Dim targets As New List(Of KeyValuePair(Of String, Rectangle))()
        For Each row As OptionRowHost In rows
            If row.Visible Then targets.Add(New KeyValuePair(Of String, Rectangle)(If(keys.ContainsKey(row.Check), keys(row.Check), Describe(row.Check)), ClippedOnForm(form, row.Check)))
        Next
        For i As Integer = 0 To targets.Count - 1
            For j As Integer = i + 1 To targets.Count - 1
                Dim overlap As Rectangle = Rectangle.Intersect(targets(i).Value, targets(j).Value)
                If overlap.Width > 0 AndAlso overlap.Height > 0 Then found.Add(lbl & ": the hit target of " & targets(i).Key & " " & targets(i).Value.ToString() & " overlaps " & targets(j).Key & " " & targets(j).Value.ToString())
            Next
            For Each c As Control In AllOf(form)
                If Not c.Visible OrElse TypeOf c.Parent Is OptionRowHost Then Continue For
                If Not (TypeOf c Is ButtonBase OrElse TypeOf c Is TextBox OrElse TypeOf c Is ComboBox OrElse TypeOf c Is NumericUpDown OrElse TypeOf c Is DateTimePicker) Then Continue For
                Dim reach As Rectangle = ClippedOnForm(form, c)
                Dim overlap As Rectangle = Rectangle.Intersect(targets(i).Value, reach)
                If overlap.Width > 0 AndAlso overlap.Height > 0 Then found.Add(lbl & ": the hit target of " & targets(i).Key & " " & targets(i).Value.ToString() & " overlaps " & Describe(c) & " " & reach.ToString())
            Next
        Next

        ' The window holds its content and stays on its monitor.
        Dim area As Rectangle = Screen.FromControl(form).WorkingArea
        If form.Width > area.Width OrElse form.Height > area.Height Then
            found.Add(lbl & ": the window " & form.Size.ToString() & " is larger than the working area " & area.Size.ToString())
        ElseIf form.Left < area.Left - 1 OrElse form.Top < area.Top - 1 OrElse form.Right > area.Right + 1 OrElse form.Bottom > area.Bottom + 1 Then
            found.Add(lbl & ": the window " & form.Bounds.ToString() & " is outside the working area " & area.ToString())
        End If
        For Each row As OptionRowHost In rows
            Dim at As Rectangle = OnForm(form, row)
            Dim scrolled As Boolean = False
            Dim p As Control = row.Parent
            While p IsNot Nothing AndAlso Not Object.ReferenceEquals(p, form)
                Dim sc As ScrollableControl = TryCast(p, ScrollableControl)
                If sc IsNot Nothing AndAlso sc.AutoScroll Then scrolled = True
                p = p.Parent
            End While
            ' Horizontally a row is never beyond the window, scrolled or not: only the vertical axis scrolls.
            If at.Left < -1 OrElse at.Right > form.ClientSize.Width + 1 Then
                found.Add(lbl & ": " & Describe(row.Check) & " " & at.ToString() & " is beyond the window width " & form.ClientSize.Width.ToString(CultureInfo.InvariantCulture) & If(scrolled, " (scrolled)", ""))
            End If
        Next
    End Sub

    ' --- what a click does ----------------------------------------------------------------------------

    <Fact>
    Public Sub A_click_on_the_caption_toggles_once_a_click_on_the_explanation_never_does_and_the_key_route_toggles()
        InLanguage("en",
            Sub(kit As Kit)
                For Each subject As Subject In Subjects()
                    Using shown As Shown = Open(subject, kit, 1.0F)
                        Dim form As Form = shown.Form
                        For Each field As KeyValuePair(Of String, Boolean) In subject.Fields
                            Dim check As CheckBox = CheckOf(form, field.Key)
                            Dim row As OptionRowHost = RowOf(check)
                            Dim lbl As String = subject.Window & "/" & field.Key
                            Dim initiallyEnabled As Boolean = check.Enabled
                            check.Enabled = True
                            Application.DoEvents()
                            Dim flips As Integer = 0
                            Dim counter As EventHandler = Sub(sender As Object, e As EventArgs) flips += 1
                            AddHandler check.CheckedChanged, counter
                            Dim start As Boolean = check.Checked

                            Assert.True(Object.ReferenceEquals(Cursors.Hand, row.Caption.Cursor), lbl & ": the caption has the hand cursor")
                            ClickOn(row.Caption)
                            Assert.True(flips = 1, lbl & ": one click on the caption flips the box exactly once, not " & flips.ToString(CultureInfo.InvariantCulture))
                            Assert.Equal(Not start, check.Checked)
                            ClickOn(row.Caption)
                            Assert.True(flips = 2 AndAlso check.Checked = start, lbl & ": a second click flips it back")

                            If row.Explanation IsNot Nothing Then
                                Assert.False(Object.ReferenceEquals(Cursors.Hand, row.Explanation.Cursor), lbl & ": the explanation is not a hand")
                                ClickOn(row.Explanation)
                                ClickOn(row.Explanation)
                                Assert.True(flips = 2 AndAlso check.Checked = start, lbl & ": a click on the explanation never toggles")
                            End If
                            ClickOn(row)
                            Assert.True(flips = 2, lbl & ": a click on the row's own surface never toggles")

                            ' The keyboard route: Space on the focused native check box is its Click (the real key needs the
                            ' foreground, so the Click itself is raised); the box stays a focusable stock CheckBox.
                            Assert.True(check.TabStop AndAlso check.GetType() Is GetType(CheckBox), lbl & ": the box stays a focusable native check box")
                            ClickOn(check)
                            Assert.True(flips = 3 AndAlso check.Checked = Not start, lbl & ": the key route toggles once")

                            RemoveHandler check.CheckedChanged, counter
                            If check.Checked <> start Then check.Checked = start
                            check.Enabled = initiallyEnabled
                            Application.DoEvents()
                        Next
                    End Using
                Next
            End Sub)
    End Sub

    <Fact>
    Public Sub A_disabled_row_never_toggles_and_toggles_again_when_enabled()
        InLanguage("en",
            Sub(kit As Kit)
                For Each subject As Subject In Subjects()
                    Using shown As Shown = Open(subject, kit, 1.0F)
                        For Each field As KeyValuePair(Of String, Boolean) In subject.Fields
                            Dim check As CheckBox = CheckOf(shown.Form, field.Key)
                            Dim row As OptionRowHost = RowOf(check)
                            Dim lbl As String = subject.Window & "/" & field.Key
                            Dim initiallyEnabled As Boolean = check.Enabled
                            Dim flips As Integer = 0
                            Dim counter As EventHandler = Sub(sender As Object, e As EventArgs) flips += 1
                            AddHandler check.CheckedChanged, counter
                            Dim start As Boolean = check.Checked
                            check.Enabled = False
                            Application.DoEvents()
                            Assert.False(row.Caption.Enabled, lbl & ": the caption is disabled with the box")
                            ClickOn(row.Caption)
                            If row.Explanation IsNot Nothing Then ClickOn(row.Explanation)
                            Assert.True(flips = 0 AndAlso check.Checked = start, lbl & ": a disabled row does not toggle")
                            check.Enabled = True
                            Application.DoEvents()
                            Assert.True(row.Caption.Enabled, lbl & ": the caption is enabled again")
                            ClickOn(row.Caption)
                            Assert.True(flips = 1 AndAlso check.Checked = Not start, lbl & ": it toggles again once enabled")
                            RemoveHandler check.CheckedChanged, counter
                            If check.Checked <> start Then check.Checked = start
                            check.Enabled = initiallyEnabled
                            Application.DoEvents()
                        Next
                    End Using
                Next
            End Sub)
    End Sub

    ''' <summary>A window's own interlock stays: all-files switches the four types off and disables them, and the sub-items
    ''' option follows the scan option - through the rows exactly as through the stock boxes.</summary>
    <Fact>
    Public Sub The_windows_own_interlocks_work_through_the_rows()
        InLanguage("en",
            Sub(kit As Kit)
                Using shown As Shown = Open(ScheduledSubject(), kit, 1.0F, applySetup:=False)
                    Dim all As CheckBox = CheckOf(shown.Form, "chkAllFiles")
                    Dim images As CheckBox = CheckOf(shown.Form, "chkImages")
                    Assert.True(all.Checked)
                    Assert.False(images.Enabled)
                    ClickOn(RowOf(all).Caption)
                    Assert.False(all.Checked)
                    Assert.True(images.Enabled)
                    Assert.True(RowOf(images).Caption.Enabled)
                    ClickOn(RowOf(images).Caption)
                    Assert.True(images.Checked)
                    ClickOn(RowOf(all).Caption)
                    Assert.True(all.Checked)
                    Assert.False(images.Checked)
                    Assert.False(RowOf(images).Caption.Enabled)
                End Using
                Using shown As Shown = Open(RootParamsSubject(), kit, 1.0F)
                    Dim scan As CheckBox = CheckOf(shown.Form, "chkScanSub")
                    Dim subItems As CheckBox = CheckOf(shown.Form, "chkSubItems")
                    Dim readOnlyBox As CheckBox = CheckOf(shown.Form, "chkReadOnly")
                    Dim destination As CheckBox = CheckOf(shown.Form, "chkDestination")
                    Dim soft As CheckBox = CheckOf(shown.Form, "chkSoftReadOnly")
                    Assert.True(subItems.Enabled)
                    ClickOn(RowOf(scan).Caption)
                    Assert.False(scan.Checked)
                    Assert.False(subItems.Enabled)
                    Assert.False(RowOf(subItems).Caption.Enabled)
                    ClickOn(RowOf(scan).Caption)
                    Assert.True(subItems.Enabled)
                    ' read-only and destination exclude each other; a destination greys the soft hint
                    ClickOn(RowOf(readOnlyBox).Caption)
                    Assert.True(readOnlyBox.Checked)
                    ClickOn(RowOf(destination).Caption)
                    Assert.True(destination.Checked)
                    Assert.False(readOnlyBox.Checked)
                    Assert.False(soft.Enabled)
                    Assert.False(RowOf(soft).Caption.Enabled)
                End Using
            End Sub)
    End Sub

    ' --- order, direction, accessibility ----------------------------------------------------------------

    <Fact>
    Public Sub The_tab_order_of_each_window_is_the_one_it_had()
        InLanguage("en",
            Sub(kit As Kit)
                For Each subject As Subject In Subjects()
                    Using shown As Shown = Open(subject, kit, 1.0F)
                        Dim form As Form = shown.Form
                        Dim keys As Dictionary(Of CheckBox, String) = KeysOf(form)
                        Dim walked As New List(Of String)()
                        Dim current As Control = form
                        For guard As Integer = 0 To 600
                            current = form.GetNextControl(current, True)
                            If current Is Nothing Then Exit For
                            Dim check As CheckBox = TryCast(current, CheckBox)
                            If check IsNot Nothing AndAlso check.TabStop AndAlso check.Visible Then walked.Add(keys(check))
                        Next
                        ' The windows' own order: the order the check boxes were authored in (every one is visible in this state).
                        Assert.Equal(subject.Fields.Select(Function(f) f.Key).ToArray(), walked.ToArray())
                        ' A row and its labels are never a tab stop of their own; only the box is.
                        For Each row As OptionRowHost In AllOf(form).OfType(Of OptionRowHost)()
                            Assert.False(row.TabStop)
                            Assert.False(row.Caption.TabStop)
                            If row.Explanation IsNot Nothing Then Assert.False(row.Explanation.TabStop)
                        Next
                    End Using
                Next
            End Sub)
    End Sub

    <Theory>
    <InlineData("en", False)>
    <InlineData("ar", True)>
    Public Sub The_box_leads_at_the_reading_start_and_mirrors_under_right_to_left(code As String, isRtl As Boolean)
        InLanguage(code,
            Sub(kit As Kit)
                Assert.Equal(isRtl, Localization.IsRightToLeft())
                For Each subject As Subject In Subjects()
                    Using shown As Shown = Open(subject, kit, 1.0F)
                        Assert.Equal(If(isRtl, RightToLeft.Yes, RightToLeft.No), shown.Form.RightToLeft)
                        For Each pair As KeyValuePair(Of CheckBox, String) In KeysOf(shown.Form)
                            Dim check As CheckBox = pair.Key
                            Dim row As OptionRowHost = RowOf(check)
                            Dim lbl As String = Mark(subject, code, 1.0F) & " " & pair.Value
                            Assert.Equal(isRtl, row.RightToLeft = RightToLeft.Yes)
                            Dim centre As Integer = check.Left + check.Width \ 2
                            If isRtl Then
                                Assert.True(centre > row.ClientSize.Width \ 2, lbl & ": the box stands at the right")
                                Assert.True(row.Caption.Right <= check.Left + 1, lbl & ": the caption reads to the left of the box")
                                Assert.Equal(RightToLeft.Yes, row.Caption.RightToLeft)
                                If row.Explanation IsNot Nothing Then Assert.True(row.Explanation.Right <= check.Left + 1, lbl & ": the explanation is beside the caption")
                            Else
                                Assert.True(centre < row.ClientSize.Width \ 2, lbl & ": the box stands at the left")
                                Assert.True(check.Right <= row.Caption.Left + 1, lbl & ": the caption reads to the right of the box")
                                If row.Explanation IsNot Nothing Then Assert.True(check.Right <= row.Explanation.Left + 1, lbl & ": the explanation is beside the caption")
                            End If
                            ' The visible box stands 8 px from its text (BOOLEAN-OPTION 2.0 section 2 rule 2), counted from the
                            ' painted box, not from the larger hit target around it.
                            Dim inset As Integer = (check.Width - SettingsCheckBox.BoxSide(1.0F)) \ 2
                            Dim boxLeft As Integer = check.Left + inset
                            Dim gap As Integer = If(isRtl, boxLeft - row.Caption.Right, row.Caption.Left - (boxLeft + SettingsCheckBox.BoxSide(1.0F)))
                            Assert.True(Math.Abs(gap - 8) <= 1, lbl & ": the visible box stands " & gap.ToString(CultureInfo.InvariantCulture) & " px from its text, not 8")
                        Next
                    End Using
                Next
            End Sub)
    End Sub

    <Fact>
    Public Sub The_accessible_name_is_the_caption_and_the_description_the_explanation()
        InLanguage("en",
            Sub(kit As Kit)
                For Each subject As Subject In Subjects()
                    Using shown As Shown = Open(subject, kit, 1.0F)
                        For Each pair As KeyValuePair(Of CheckBox, String) In KeysOf(shown.Form)
                            Dim check As CheckBox = pair.Key
                            Dim row As OptionRowHost = RowOf(check)
                            Dim expectedDescription As String = If(row.Explanation Is Nothing, "", row.Explanation.Text)
                            Assert.Equal(row.Caption.Text, check.AccessibleName)
                            Assert.Equal(row.Caption.Text, check.AccessibilityObject.Name)
                            Assert.Equal(expectedDescription, If(check.AccessibleDescription, ""))
                            ' one accessible check box per option: the labels beside it are not exposed as a second control
                            Assert.Equal(1, row.Controls.OfType(Of CheckBox)().Count())
                        Next
                    End Using
                Next
            End Sub)
    End Sub

    ''' <summary>The theme walk reaches the rows through the seam, so a Manager window needs no explicit attach: a dark palette
    ''' repaints the box and the row without an error.</summary>
    <Fact>
    Public Sub A_theme_change_reaches_the_rows_of_both_windows()
        InLanguage("en",
            Sub(kit As Kit)
                If SystemInformation.HighContrast Then Return ' the system colours rule there; the palette is not read
                For Each subject As Subject In Subjects()
                    Using shown As Shown = Open(subject, kit, 1.0F)
                        For Each row As OptionRowHost In AllOf(shown.Form).OfType(Of OptionRowHost)()
                            row.ApplyColors(Color.FromArgb(39, 41, 45), Color.FromArgb(20, 20, 24), Color.FromArgb(177, 181, 189), Color.FromArgb(244, 245, 247), Color.FromArgb(200, 60, 60), Color.FromArgb(250, 250, 250))
                            row.Check.Enabled = Not row.Check.Enabled
                            Application.DoEvents()
                            row.Check.Enabled = Not row.Check.Enabled
                            Application.DoEvents()
                        Next
                        shown.Form.Invalidate(True)
                        Application.DoEvents()
                    End Using
                Next
            End Sub)
    End Sub

    ' --- the guided add hosts the folder parameters ---------------------------------------------------

    <Fact>
    Public Sub The_guided_add_arranges_the_rows_of_the_editor_it_hosts()
        InLanguage("en",
            Sub(kit As Kit)
                Dim form As New Resource_Add_Form(New ResourceAddTests.Actions(), ResourceAddEntry.EmptyList)
                Try
                    form.StartPosition = FormStartPosition.Manual
                    form.Location = New Point(20, 20)
                    form.Show()
                    Application.DoEvents()
                    ' The pick arrives after the window is shown, as in the program.
                    Dim flags As BindingFlags = BindingFlags.Instance Or BindingFlags.NonPublic
                    GetType(Resource_Add_Form).GetMethod("AcceptPick", flags).Invoke(form, New Object() {"C:\ResourceScene"})
                    GetType(Resource_Add_Form).GetMethod("ShowStep", flags).Invoke(form, New Object() {ResourceAddStep.Questions})
                    Application.DoEvents()
                    Dim rows As List(Of OptionRowHost) = AllOf(form).OfType(Of OptionRowHost)().ToList()
                    Assert.Equal(15, rows.Count)
                    For Each row As OptionRowHost In rows
                        Assert.True(row.Width > 0, "the hosted row has a width")
                        Dim before As Integer = row.Height
                        DialogOptionRow.NeededHeight(row)
                        Assert.True(Math.Abs(row.Height - before) <= 1, "a hosted row is as tall as its content: " & before.ToString(CultureInfo.InvariantCulture) & " vs " & row.Height.ToString(CultureInfo.InvariantCulture))
                        Assert.True(row.Check.Width >= SettingsCheckBox.Px(UiSizing.PointerTarget, 1.0F) - 1, "the hosted hit target is 32 px")
                        Assert.True(TextFits(row.Caption), "the hosted caption '" & row.Caption.Text & "' is cut")
                        Assert.True(TextFits(row.Explanation), "the hosted explanation is cut")
                    Next
                Finally
                    form.Close()
                    form.Dispose()
                End Try
            End Sub)
    End Sub

    ' --- the facade for a hand-positioned window and the fitting --------------------------------------

    <Theory>
    <InlineData(1.0F)>
    <InlineData(1.75F)>
    Public Sub A_hand_positioned_window_gets_its_rows_in_place_and_the_controls_under_a_grown_row_move_down(factor As Single)
        InLanguage("en",
            Sub(kit As Kit)
                Dim form As New Form With {.ClientSize = New Size(340, 230), .FormBorderStyle = FormBorderStyle.FixedDialog,
                    .StartPosition = FormStartPosition.Manual, .Location = New Point(20, 20)}
                Try
                    Dim first As New CheckBox With {.Name = "chkFirst", .Text = "First option", .AutoSize = True, .Location = New Point(20, 12), .TabIndex = 0}
                    Dim firstHint As New Label With {.Name = "lblFirstHint", .AutoSize = True, .Location = New Point(44, 36),
                        .Text = "This explanation is long enough to wrap onto several lines in a narrow window, and the rows under it must move down."}
                    Dim second As New CheckBox With {.Name = "chkSecond", .Text = "Second option", .AutoSize = True, .Location = New Point(20, 90), .TabIndex = 2, .Checked = True}
                    Dim ok As New Button With {.Name = "btnOk", .Text = "OK", .Location = New Point(20, 150), .Size = New Size(90, 30), .TabIndex = 3}
                    form.Controls.AddRange(New Control() {first, firstHint, second, ok})
                    Dim before As Integer = form.Controls.IndexOf(second)

                    Dim rowOne As OptionRowHost = OptionRows.At(first, firstHint, 20, 8, form.ClientSize.Width)
                    Dim rowTwo As OptionRowHost = OptionRows.At(second, Nothing, 20, 70, form.ClientSize.Width)

                    ' In place: the rows stand where the boxes stood in the control list, and the hint is the explanation of its row.
                    Assert.Same(form, rowOne.Parent)
                    Assert.Same(rowOne, first.Parent)
                    Assert.Same(rowOne, firstHint.Parent)
                    Assert.True(form.Controls.IndexOf(rowOne) < form.Controls.IndexOf(rowTwo))
                    Assert.True(form.Controls.IndexOf(rowTwo) < form.Controls.IndexOf(ok))
                    Assert.True(before >= 1)
                    Assert.Equal("First option", rowOne.Caption.Text)

                    Dim extra As Single = factor / (form.DeviceDpi / 96.0F)
                    If Math.Abs(extra - 1.0F) > 0.01F Then
                        form.Font = New Font(form.Font.FontFamily, form.Font.SizeInPoints * extra)
                        form.Scale(New SizeF(extra, extra))
                    End If
                    form.Show()
                    Application.DoEvents()
                    Dim okTop As Integer = ok.Top
                    Dim secondTop As Integer = rowTwo.Top
                    Dim heightBefore As Integer = form.Height
                    OptionRows.Fit(form)
                    Application.DoEvents()

                    ' The first row grew (caption plus a wrapped explanation), what stood under it moved down by the growth, the
                    ' window grew by it, and nothing lies over anything.
                    Assert.True(rowOne.Height > SettingsCheckBox.Px(DialogOptionRow.RowHeightLogical, factor), "the row with a wrapped explanation is taller than a one-line row")
                    Assert.True(rowTwo.Top >= rowOne.Bottom, "the second row starts under the first")
                    Assert.True(rowTwo.Top > secondTop, "the second row moved down")
                    Assert.True(ok.Top >= rowTwo.Bottom, "the button stays under the second row")
                    Assert.True(ok.Top > okTop, "the button moved down")
                    Assert.True(ok.Bottom <= form.ClientSize.Height, "the window holds the button")
                    Assert.True(form.Height >= heightBefore, "the window did not shrink")
                    Assert.True(TextFits(rowOne.Explanation), "the explanation is not cut")
                    Dim area As Rectangle = Screen.FromControl(form).WorkingArea
                    Assert.True(form.Width <= area.Width AndAlso form.Height <= area.Height, "the window stays inside the working area")

                    ' Tab order: box, box, button.
                    Dim walked As New List(Of String)()
                    Dim current As Control = form
                    For guard As Integer = 0 To 40
                        current = form.GetNextControl(current, True)
                        If current Is Nothing Then Exit For
                        If current.TabStop AndAlso (TypeOf current Is CheckBox OrElse TypeOf current Is Button) Then walked.Add(current.Name)
                    Next
                    Assert.Equal(New String() {"chkFirst", "chkSecond", "btnOk"}, walked.ToArray())

                    ' The behaviour: the caption toggles once, the explanation never.
                    Dim flips As Integer = 0
                    AddHandler first.CheckedChanged, Sub() flips += 1
                    ClickOn(rowOne.Caption)
                    Assert.Equal(1, flips)
                    ClickOn(firstHint)
                    Assert.Equal(1, flips)
                    Assert.Equal("First option", first.AccessibleName)
                    Assert.Equal(firstHint.Text, first.AccessibleDescription)

                    ' Fitting again changes nothing (rows that need no more room are not touched).
                    Dim after As Rectangle = ok.Bounds
                    OptionRows.Fit(form)
                    Assert.Equal(after, ok.Bounds)
                Finally
                    form.Close()
                    form.Dispose()
                End Try
            End Sub)
    End Sub

    <Fact>
    Public Sub Growth_moves_what_is_under_a_row_by_the_largest_chain_above_it_and_leaves_the_beside_alone()
        Dim bounds As New List(Of Rectangle) From {
            New Rectangle(0, 0, 100, 40), New Rectangle(0, 50, 100, 40), New Rectangle(0, 100, 100, 40), New Rectangle(200, 0, 100, 40)}
        Dim needed As New List(Of Integer) From {70, 60, 0, 0}
        Dim clientGrowth As Integer
        Dim grown As Rectangle() = OptionRows.GrowVertical(bounds, needed, clientGrowth)
        Assert.Equal(New Rectangle(0, 0, 100, 70), grown(0))
        Assert.Equal(New Rectangle(0, 80, 100, 60), grown(1))        ' moved by the 30 above it, grew by 20
        Assert.Equal(New Rectangle(0, 150, 100, 40), grown(2))       ' 30 + 20 above it
        Assert.Equal(bounds(3), grown(3))                            ' beside the first row, not under it
        Assert.Equal(50, clientGrowth)
        Dim none As Integer
        Dim same As Rectangle() = OptionRows.GrowVertical(bounds, New List(Of Integer) From {0, 0, 0, 0}, none)
        Assert.Equal(bounds.ToArray(), same)
        Assert.Equal(0, none)
    End Sub

    ' --- the source scan -------------------------------------------------------------------------------

    ''' <summary>The text between the parentheses of every call that places a control: Controls.Add / AddRange, and the windows' own
    ''' AddRow / AddFullRow helpers.</summary>
    Private Shared Function PlacingArguments(code As String) As List(Of String)
        Dim result As New List(Of String)()
        For Each m As Match In Regex.Matches(code, "\b(?:Controls\.(?:Add|AddRange)|AddRow|AddFullRow)\(")
            Dim depth As Integer = 1
            Dim i As Integer = m.Index + m.Length
            Dim start As Integer = i
            While i < code.Length AndAlso depth > 0
                If code(i) = "("c Then depth += 1
                If code(i) = ")"c Then depth -= 1
                i += 1
            End While
            result.Add(code.Substring(start, Math.Max(0, i - start - 1)))
        Next
        Return result
    End Function

    ''' <summary>The argument without the calls that wrap a check box into its row: a box handed to those is the right way in.</summary>
    Private Shared Function WithoutRowCalls(argument As String) As String
        Dim result As String = argument
        For Each marker As String In New String() {"OptionRows.InFlow(", "OptionRows.At(", "DialogOptionRow.Create(", "DialogOptionRow.CreateAt("}
            Dim at As Integer = result.IndexOf(marker, StringComparison.Ordinal)
            While at >= 0
                Dim depth As Integer = 1
                Dim i As Integer = at + marker.Length
                While i < result.Length AndAlso depth > 0
                    If result(i) = "("c Then depth += 1
                    If result(i) = ")"c Then depth -= 1
                    i += 1
                End While
                result = result.Remove(at, i - at)
                at = result.IndexOf(marker, StringComparison.Ordinal)
            End While
        Next
        Return result
    End Function

    <Fact>
    Public Sub Neither_window_adds_a_native_check_box_to_its_parent_every_one_goes_through_the_option_row()
        Dim problems As New List(Of String)()
        Dim expected As New Dictionary(Of String, Integer)(StringComparer.Ordinal) From {
            {Path.Combine("Forms", "Scheduled_Operation_Form.vb"), 15}, {Path.Combine("Forms", "Share_Root_Params_Form.vb"), 8}}
        For Each pair As KeyValuePair(Of String, Integer) In expected
            Dim code As String = ExchangeTestSupport.CodeOnly(ExchangeTestSupport.ReadSource(pair.Key))
            Dim made As Integer = Regex.Matches(code, "\bNew CheckBox\b").Count
            Dim rows As Integer = Regex.Matches(code, "\bOptionRows\.(?:InFlow|At)\(").Count
            If made <> pair.Value Then problems.Add(pair.Key & ": " & made.ToString(CultureInfo.InvariantCulture) & " 'New CheckBox', expected " & pair.Value.ToString(CultureInfo.InvariantCulture))
            If rows <> made Then problems.Add(pair.Key & ": " & made.ToString(CultureInfo.InvariantCulture) & " check boxes but " & rows.ToString(CultureInfo.InvariantCulture) & " option rows")
            If code.IndexOf("OptionRows.ArrangeAll(Me)", StringComparison.Ordinal) < 0 Then problems.Add(pair.Key & ": never arranges its rows once it has its scale (OptionRows.ArrangeAll)")
            Dim names As New List(Of String)()
            For Each m As Match In Regex.Matches(code, "\b(?:Private|Dim|Friend|Public)\s+(?:ReadOnly\s+)?(\w+)\s*(?:\([^)]*\))?\s+As\s+(?:New\s+)?CheckBox\b")
                names.Add(m.Groups(1).Value)
            Next
            If names.Count = 0 Then problems.Add(pair.Key & ": no check box field found by the scan (the pattern is stale)")
            For Each argument As String In PlacingArguments(code)
                Dim bare As String = WithoutRowCalls(argument)
                For Each name As String In names
                    If Regex.IsMatch(bare, "\b" & Regex.Escape(name) & "\b") Then problems.Add(pair.Key & ": the check box " & name & " is placed directly (" & bare.Trim() & ")")
                Next
            Next
        Next
        Assert.True(problems.Count = 0, String.Join(vbLf, problems))
    End Sub

    <Fact>
    Public Sub The_hosted_editor_hands_a_row_the_width_of_the_column_and_never_the_touch_sizing_of_a_stock_box()
        Dim hosting As String = ExchangeTestSupport.CodeOnly(ExchangeTestSupport.ReadSource(Path.Combine("Forms", "Share_Root_Params_Form.Hosting.vb")))
        Dim branch As Integer = hosting.IndexOf("TypeOf control Is OptionRowHost", StringComparison.Ordinal)
        Dim general As Integer = hosting.IndexOf("control.MaximumSize = New Size(width, 0)", StringComparison.Ordinal)
        Assert.True(branch >= 0 AndAlso general > branch, "an option row is handled before the generic touch-sizing of a control")
    End Sub
End Class
