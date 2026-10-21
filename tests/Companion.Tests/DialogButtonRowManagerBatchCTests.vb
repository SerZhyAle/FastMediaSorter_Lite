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
Imports System.Threading.Tasks
Imports System.Windows.Forms
Imports FastMediaSorterCompanion
Imports Xunit

''' <summary>
''' BUTTONS 2.0 sections 3 and 4 as the third and last batch of the Share Manager's dialogs holds them after they moved to
''' the shared <see cref="DialogButtonRow"/> (ticket 243, slice T-8b, Share Manager batch C): the scheduled-operation
''' editor with its preview, the resource picker and the history, the "enable sharing" dialog, the folder parameters, the
''' disks window with its two option dialogs, the phone code intake, the support-bundle consent, the guided add of a
''' resource (a wizard: Cancel and Back first, the forward button last) and the package window. Batches A and B are held by
''' <c>DialogButtonRowManagerBatchATests</c> and <c>DialogButtonRowManagerBatchBTests</c> in the same way; the row itself
''' by the viewer's <c>DialogButtonRowTests</c>.
''' What is held here is that each window reads its answers Cancel first (the final affirmative last, a command before
''' both), binds Escape and Enter to the right buttons, never makes a command or a destructive button the default, and
''' that the row neither lies over the content nor leaves the client area - at 100 % and 175 %, in a left-to-right
''' language (en, ru) and in a right-to-left one (ar). A scale is EMULATED (the font grown by the factor, the fixed pixels
''' scaled with Control.Scale), the way the batch A and B tests do it; how a window LOOKS stays manual acceptance.
'''
''' NOT converted, with the reason (kept here so the next reader does not take them for forgotten):
'''   * the field-local commands - the folder pick buttons of the editor, the copy / QR / save / e-mail commands of the
'''     package window, the three intake doors of the phone window, the disk toolbar and per-disk buttons, the links of the
'''     disks window - are commands of the field or the row they sit beside, not answers of the dialog;
'''   * <c>Share_Root_Params_Form.Hosting.vb</c> only moves the editor's rows into the guided add and builds no answer;
'''   * the MainWindow.* command surfaces and the chrome of MainWindow.
''' The collection is the scheduled one (no parallelism): the windows read and write the settings hive, which a scratch
''' hive stands in for, and the tests set the UI language.
''' </summary>
<Collection(ScheduledCollection.Name)>
Public Class DialogButtonRowManagerBatchCTests

    ''' <summary>What a window must look like to its user: the buttons of its answer row in reading order (names), the
    ''' button Escape and Enter reach, how many masked (password) fields it holds.</summary>
    Private NotInheritable Class Spec
        Friend ReadOnly Name As String
        Friend ReadOnly Make As Func(Of Form)
        Friend ReadOnly Reading As String()
        Friend ReadOnly CancelName As String
        Friend ReadOnly AcceptName As String
        Friend ReadOnly Masked As Integer
        ''' <summary>False for the windows whose first Shown event reaches the outside (the worker, the status poll): the
        ''' window is shown, laid out and closed without the message loop delivering that event.</summary>
        Friend ReadOnly Pump As Boolean
        ''' <summary>True when the window sets its AcceptButton itself, on purpose, to something other than the row's default
        ''' (the consent dialog defaults to Cancel while its row's affirmative is Send).</summary>
        Friend ReadOnly OwnAccept As Boolean

        Friend Sub New(name As String, make As Func(Of Form), reading As String(), cancelName As String, acceptName As String, masked As Integer,
                       Optional pump As Boolean = True, Optional ownAccept As Boolean = False)
            Me.Name = name
            Me.Make = make
            Me.Reading = reading
            Me.CancelName = cancelName
            Me.AcceptName = acceptName
            Me.Masked = masked
            Me.Pump = pump
            Me.OwnAccept = ownAccept
        End Sub
    End Class

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

    Private Const Flags As BindingFlags = BindingFlags.Instance Or BindingFlags.NonPublic

    Private Shared Function Invoke(target As Object, name As String, ParamArray args As Object()) As Object
        Return target.GetType().GetMethod(name, Flags).Invoke(target, args)
    End Function

    Private Shared Sub Advance(form As Resource_Add_Form)
        DirectCast(Invoke(form, "AdvanceAsync"), Task).GetAwaiter().GetResult()
    End Sub

    Private Shared Function Find(Of T As Control)(root As Control, name As String) As T
        For Each child As Control In root.Controls
            If child.Name = name AndAlso TypeOf child Is T Then Return DirectCast(child, T)
            Dim nested As T = Find(Of T)(child, name)
            If nested IsNot Nothing Then Return nested
        Next
        Return Nothing
    End Function

    Private Shared Function Package(oversize As Boolean) As LogPackage.LogPackageResult
        Return New LogPackage.LogPackageResult With {
            .Path = Path.Combine(Path.GetTempPath(), "fms-logs-sample.zip"),
            .SizeBytes = If(oversize, 40L * 1024 * 1024, 12345L),
            .Entries = New List(Of String) From {"companion.log", "worker.log", "settings.txt"},
            .OverSizeLimit = oversize}
    End Function

    Private Shared Function PreviewForm() As Form
        Dim view As New ScheduledPreview With {.WouldAct = 4, .WouldSkip = 2, .Bytes = 5L * 1024 * 1024, .Truncated = True}
        For i As Integer = 1 To 12
            view.Lines.Add("photo-" & i.ToString(CultureInfo.InvariantCulture) & ".jpg")
        Next
        Return New Scheduled_Preview_Form(view)
    End Function

    Private Shared Function DeletingEditor(kit As Kit) As Form
        Dim form As New Scheduled_Operation_Form(kit.Engine, Nothing)
        Find(Of ComboBox)(form, "cmbSchedAction").SelectedIndex = CInt(ScheduledAction.Delete)
        Return form
    End Function

    ''' <summary>The guided add at a step: 0 = the first (nothing picked), 1 = the questions, 2 = the last, 3 = after the add.</summary>
    Private Shared Function GuidedAdd(stage As Integer) As Form
        Dim form As New Resource_Add_Form(New ResourceAddTests.Actions(), ResourceAddEntry.EmptyList)
        If stage = 0 Then Return form
        Invoke(form, "AcceptPick", "C:\ResourceScene")
        Invoke(form, "ShowStep", ResourceAddStep.Questions)
        If stage >= 2 Then Advance(form)
        If stage >= 3 Then Advance(form)
        Return form
    End Function

    Private Shared Function Specs(kit As Kit) As List(Of Spec)
        Return New List(Of Spec) From {
            New Spec("Scheduled_Operation_Form", Function() New Scheduled_Operation_Form(kit.Engine, Nothing),
                     {"btnSchedPreview", "btnSchedCancel", "btnSchedSave"}, "btnSchedCancel", "btnSchedSave", 0),
            New Spec("Scheduled_Operation_Form (delete)", Function() DeletingEditor(kit),
                     {"btnSchedPreview", "btnSchedCancel", "btnSchedSave"}, "btnSchedCancel", "", 0),
            New Spec("Scheduled_Preview_Form", Function() PreviewForm(),
                     {"btnSchedPreviewClose"}, "btnSchedPreviewClose", "btnSchedPreviewClose", 0),
            New Spec("Scheduled_Resource_Picker_Form", Function() New Scheduled_Resource_Picker_Form(New ScheduledLocation()),
                     {"btnSchedResourceCancel", "btnSchedResourceOk"}, "btnSchedResourceCancel", "btnSchedResourceOk", 0),
            New Spec("Scheduled_History_Form", Function() New Scheduled_History_Form(kit.Engine),
                     {"btnSchedHistoryRefresh", "btnSchedHistoryClear", "btnSchedHistoryClose"}, "btnSchedHistoryClose", "btnSchedHistoryClose", 0),
            New Spec("Share_Enable_Form", Function() New Share_Enable_Form(),
                     {"xse_Cancel", "xse_Enable"}, "xse_Cancel", "xse_Enable", 0),
            New Spec("Share_Root_Params_Form", Function() New Share_Root_Params_Form("Photos", New ShareRootParams()),
                     {"xrp_Cancel", "xrp_Ok"}, "xrp_Cancel", "xrp_Ok", 0),
            New Spec("Share_Disks_Form", Function() New Share_Disks_Form(),
                     {"xsd_Close"}, "xsd_Close", "", 0, pump:=False),
            New Spec("DiskOptionsForm (register)", Function() New DiskOptionsForm(True, True),
                     {"xdo_Cancel", "xdo_Ok"}, "xdo_Cancel", "", 1),
            New Spec("DiskOptionsForm (open)", Function() New DiskOptionsForm(False, True, False),
                     {"xdo_Cancel", "xdo_Ok"}, "xdo_Cancel", "", 1),
            New Spec("Phone_Code_Intake_Form", Function() New Phone_Code_Intake_Form(),
                     {"xpi_Cancel", "xpi_Relay"}, "xpi_Cancel", "xpi_Relay", 1),
            New Spec("SendLogsFlow consent", Function() SendLogsFlow.BuildConsentDialog(Nothing, Package(False)),
                     {"xsl_Reveal", "xsl_Cancel", "xsl_Send"}, "xsl_Cancel", "xsl_Cancel", 0, ownAccept:=True),
            New Spec("SendLogsFlow consent (oversize)", Function() SendLogsFlow.BuildConsentDialog(Nothing, Package(True)),
                     {"xsl_Reveal", "xsl_Cancel", "xsl_Send"}, "xsl_Cancel", "xsl_Cancel", 0, ownAccept:=True),
            New Spec("Resource_Add_Form (nothing picked)", Function() GuidedAdd(0),
                     {"xra_Cancel", "xra_Back", "xra_Next"}, "xra_Cancel", "", 0),
            New Spec("Resource_Add_Form (questions)", Function() GuidedAdd(1),
                     {"xra_Cancel", "xra_Back", "xra_Next"}, "xra_Cancel", "", 1),
            New Spec("Resource_Add_Form (last step)", Function() GuidedAdd(2),
                     {"xra_Cancel", "xra_Back", "xra_Next"}, "xra_Cancel", "", 1),
            New Spec("Resource_Add_Form (added)", Function() GuidedAdd(3),
                     {"xra_Cancel", "xra_Back", "xra_Next"}, "xra_Cancel", "", 1),
            New Spec("PackageWizardForm", Function() New PackageWizardForm(New List(Of String)()),
                     {"btnPackageClose"}, "btnPackageClose", "btnPackageClose", 0, pump:=False)}
    End Function

    ' --- helpers -----------------------------------------------------------------------------

    Private Shared Function All(parent As Control) As List(Of Control)
        Dim found As New List(Of Control)()
        For Each c As Control In parent.Controls
            found.Add(c)
            found.AddRange(All(c))
        Next
        Return found
    End Function

    Private Shared Function RowsOf(parent As Control) As List(Of DialogButtonRow)
        Return All(parent).OfType(Of DialogButtonRow)().ToList()
    End Function

    ''' <summary>Builds a window, emulates a display scale and shows it (OnLoad runs its own fitting).</summary>
    Private Shared Function ShownAt(spec As Spec, factor As Single) As Form
        Dim form As Form = spec.Make()
        Dim auto As Single = form.DeviceDpi / 96.0F
        Dim extra As Single = factor / auto
        If Math.Abs(extra - 1.0F) > 0.01F Then
            form.Font = New Font(form.Font.FontFamily, form.Font.SizeInPoints * extra)
            form.Scale(New SizeF(extra, extra))
        End If
        form.StartPosition = FormStartPosition.Manual
        form.Location = New Point(20, 20)
        form.Show()
        If spec.Pump Then Application.DoEvents()
        Return form
    End Function

    Private Shared Sub Release(form As Form)
        Try
            form.Close()
        Finally
            form.Dispose()
        End Try
    End Sub

    Private Shared Function OnForm(form As Form, c As Control) As Rectangle
        Return form.RectangleToClient(c.RectangleToScreen(c.ClientRectangle))
    End Function

    ''' <summary>The visible part of a control on the window: its rectangle cut by every container between it and the window.</summary>
    Private Shared Function ClippedOnForm(form As Form, c As Control) As Rectangle
        Dim visible As Rectangle = OnForm(form, c)
        Dim p As Control = c.Parent
        While p IsNot Nothing AndAlso Not Object.ReferenceEquals(p, form)
            visible = Rectangle.Intersect(visible, OnForm(form, p))
            p = p.Parent
        End While
        Return visible
    End Function

    ''' <summary>True when the window itself or a container above the control scrolls.</summary>
    Private Shared Function UnderScroller(form As Form, c As Control) As Boolean
        If form.AutoScroll Then Return True
        Dim p As Control = c.Parent
        While p IsNot Nothing AndAlso Not Object.ReferenceEquals(p, form)
            Dim sc As ScrollableControl = TryCast(p, ScrollableControl)
            If sc IsNot Nothing AndAlso sc.AutoScroll Then Return True
            p = p.Parent
        End While
        Return False
    End Function

    ''' <summary>True when <paramref name="a"/> reads before <paramref name="b"/>: on an earlier line, or further along the
    ''' reading direction of the same one.</summary>
    Private Shared Function ReadsBefore(form As Form, a As Control, b As Control, rightToLeft As Boolean) As Boolean
        Dim ra As Rectangle = OnForm(form, a)
        Dim rb As Rectangle = OnForm(form, b)
        If ra.Top <> rb.Top Then Return ra.Top < rb.Top
        Return If(rightToLeft, ra.Left > rb.Left, ra.Left < rb.Left)
    End Function

    ''' <summary>The sentences that are wrong with the answer row of a shown window (empty = sound).</summary>
    Private Shared Function RowProblems(label As String, form As Form) As List(Of String)
        Dim found As New List(Of String)()
        Dim rows As List(Of DialogButtonRow) = RowsOf(form)
        If rows.Count <> 1 Then
            found.Add(label & ": expected exactly one DialogButtonRow, found " & rows.Count.ToString(CultureInfo.InvariantCulture))
            Return found
        End If
        Dim row As DialogButtonRow = rows(0)
        Dim client As Rectangle = form.ClientRectangle

        ' every button of the row holds its caption and stays inside the client area
        For Each b As Button In row.ReadingButtons
            Dim need As Integer = TextRenderer.MeasureText(b.Text.Replace("&", ""), b.Font).Width + ButtonGlyphs.ExtraWidth(b)
            If b.Width < need Then found.Add(label & ": " & b.Name & " '" & b.Text & "' is " & b.Width.ToString(CultureInfo.InvariantCulture) & " wide and its caption needs " & need.ToString(CultureInfo.InvariantCulture))
            Dim at As Rectangle = OnForm(form, b)
            If at.Left < client.Left - 1 OrElse at.Right > client.Right + 1 OrElse at.Top < client.Top - 1 OrElse at.Bottom > client.Bottom + 1 Then
                found.Add(label & ": " & b.Name & " " & at.ToString() & " is clipped by the window " & client.Size.ToString())
            End If
        Next

        ' no two buttons of the row overlap
        Dim buttons As IReadOnlyList(Of Button) = row.ReadingButtons
        For i As Integer = 0 To buttons.Count - 1
            For j As Integer = i + 1 To buttons.Count - 1
                Dim overlap As Rectangle = Rectangle.Intersect(OnForm(form, buttons(i)), OnForm(form, buttons(j)))
                If overlap.Width > 0 AndAlso overlap.Height > 0 Then found.Add(label & ": " & buttons(i).Name & " lies over " & buttons(j).Name)
            Next
        Next

        ' one height per line, never below the target
        For Each line In buttons.GroupBy(Function(b) b.Top)
            If line.Select(Function(b) b.Height).Distinct().Count() <> 1 Then
                found.Add(label & ": the buttons of one line differ in height: " & String.Join(",", line.Select(Function(b) b.Height.ToString(CultureInfo.InvariantCulture))))
            End If
            If line.Any(Function(b) b.Height < 32) Then found.Add(label & ": a button is below the 32 target")
        Next

        ' the row is inside the client area and lies over none of the content: not a leaf control of the window, and not a
        ' container whose own rectangle takes in the row (a flow or a table that ends above it)
        Dim rowBounds As Rectangle = OnForm(form, row)
        If rowBounds.Left < client.Left - 1 OrElse rowBounds.Right > client.Right + 1 OrElse rowBounds.Bottom > client.Bottom + 1 Then
            found.Add(label & ": the row " & rowBounds.ToString() & " leaves the client area " & client.Size.ToString())
        End If
        For Each c As Control In All(form)
            If Object.ReferenceEquals(c, row) OrElse row.Contains(c) OrElse Not c.Visible OrElse c.Controls.Count > 0 Then Continue For
            ' what a scrolling container clips away is not drawn over the row
            Dim at As Rectangle = ClippedOnForm(form, c)
            Dim overlap As Rectangle = Rectangle.Intersect(at, rowBounds)
            If overlap.Width > 1 AndAlso overlap.Height > 1 Then
                found.Add(label & ": " & If(String.IsNullOrEmpty(c.Name), c.GetType().Name, c.Name) & " " & at.ToString() & " lies over the answer row " & rowBounds.ToString())
            End If
        Next

        ' nothing visible of the window is cut by the client area on the way down (an auto-sized or fitted window); what sits
        ' in a scrolling container is reached by scrolling, so it may lie beyond the edge
        For Each c As Control In All(form)
            If Not c.Visible OrElse c.Controls.Count > 0 OrElse UnderScroller(form, c) Then Continue For
            ' The "?" badge belongs to the help chrome (ThemeService.Attach, after the form is built), not to the window: with an
            ' EMULATED scale it is attached after the emulation, so a pending autoscale pass multiplies its place by the factor
            ' (a window without docked content, the phone intake or the camera, shows it beyond the edge). At a real display scale
            ' the form is scaled once, before the badge exists. Its place is the help chrome's own business.
            If c.Name = "contextHelp" Then Continue For
            Dim at As Rectangle = OnForm(form, c)
            If at.Bottom > client.Bottom + 1 OrElse at.Right > client.Right + 1 Then
                found.Add(label & ": " & If(String.IsNullOrEmpty(c.Name), c.GetType().Name, c.Name) & " " & at.ToString() & " leaves the client area " & client.Size.ToString())
            End If
        Next
        Return found
    End Function

    ''' <summary>Runs a check with the UI language set, the glyph source open and everything put back afterwards.</summary>
    Private Shared Sub InLanguage(code As String, check As Action(Of Kit))
        ExchangeTestSupport.OnUi(
            Sub()
                Dim saved As String = Localization.CurrentCode
                Try
                    Localization.CurrentCode = code
                    ButtonGlyphs.AssetOpener = AddressOf CompanionAssets.OpenBundledAsset
                    Using kit As New Kit()
                        check(kit)
                    End Using
                Finally
                    Localization.CurrentCode = saved
                End Try
            End Sub)
    End Sub

    ' --- the checks --------------------------------------------------------------------------

    <Theory>
    <InlineData("en")>
    <InlineData("ru")>
    <InlineData("ar")>
    Public Sub The_third_batch_keeps_the_row_clear_of_the_content_and_inside_the_window_at_100_and_175_percent(code As String)
        InLanguage(code,
            Sub(kit As Kit)
                Dim problems As New List(Of String)()
                For Each spec As Spec In Specs(kit)
                    For Each factor As Single In New Single() {1.0F, 1.75F}
                        Dim label As String = spec.Name & " " & code & " @" & CInt(factor * 100).ToString(CultureInfo.InvariantCulture) & "%"
                        Dim form As Form = ShownAt(spec, factor)
                        Try
                            problems.AddRange(RowProblems(label, form))
                            ' the window is within the working area of its monitor
                            Dim area As Rectangle = Screen.FromControl(form).WorkingArea
                            If form.Width > area.Width OrElse form.Height > area.Height Then
                                problems.Add(label & ": the window " & form.Size.ToString() & " is larger than the working area " & area.Size.ToString())
                            End If
                        Finally
                            Release(form)
                        End Try
                    Next
                Next
                Assert.True(problems.Count = 0, String.Join(vbLf, problems))
            End Sub)
    End Sub

    <Theory>
    <InlineData("en", False)>
    <InlineData("ru", False)>
    <InlineData("ar", True)>
    Public Sub The_third_batch_reads_cancel_first_and_binds_escape_and_enter(code As String, mirrored As Boolean)
        InLanguage(code,
            Sub(kit As Kit)
                Assert.Equal(mirrored, Localization.IsRightToLeft())
                For Each spec As Spec In Specs(kit)
                    Dim form As Form = ShownAt(spec, 1.0F)
                    Try
                        Assert.Equal(If(mirrored, RightToLeft.Yes, RightToLeft.No), form.RightToLeft)
                        Dim row As DialogButtonRow = RowsOf(form).Single()
                        Dim reading As IReadOnlyList(Of Button) = row.ReadingButtons
                        Assert.Equal(spec.Reading, reading.Select(Function(b) b.Name).ToArray())

                        ' the reading order is what the eye sees: earlier lines first, mirrored under right-to-left
                        For i As Integer = 0 To reading.Count - 2
                            Assert.True(ReadsBefore(form, reading(i), reading(i + 1), mirrored),
                                        spec.Name & " " & code & ": " & reading(i).Name & " must read before " & reading(i + 1).Name)
                        Next

                        ' Escape = the dismissing answer, bound by the row
                        Dim cancel As Button = TryCast(form.CancelButton, Button)
                        Assert.NotNull(cancel)
                        Assert.Equal(spec.CancelName, cancel.Name)
                        Assert.Same(row.CancelTarget, cancel)
                        Assert.Equal(ButtonGlyphRole.Dismissing, ButtonGlyphs.RoleOf(cancel))

                        ' Enter = the safe default of the row, or nothing where the window decides about its accept button itself
                        Dim accept As Button = TryCast(form.AcceptButton, Button)
                        If spec.AcceptName.Length = 0 Then
                            Assert.Null(accept)
                        Else
                            Assert.NotNull(accept)
                            Assert.Equal(spec.AcceptName, accept.Name)
                            Assert.Contains(accept, reading)
                            If Not spec.OwnAccept Then Assert.Same(row.DefaultTarget, accept)
                            ' never a command (they open, preview or refresh) and never the irreversible one
                            Dim role As ButtonGlyphRole = ButtonGlyphs.RoleOf(accept)
                            Assert.NotEqual(ButtonGlyphRole.Irreversible, role)
                            Assert.True(accept.Name = spec.CancelName OrElse role = ButtonGlyphRole.Affirmative OrElse role = ButtonGlyphRole.Caution,
                                        spec.Name & ": the default " & accept.Name & " is neither the dismissing nor the affirmative answer")
                        End If
                    Finally
                        Release(form)
                    End Try
                Next
            End Sub)
    End Sub

    <Fact>
    Public Sub The_third_batch_keeps_its_answer_names_inside_the_row_and_numbers_the_tab_order_in_reading_order()
        InLanguage("en",
            Sub(kit As Kit)
                For Each spec As Spec In Specs(kit)
                    Dim form As Form = ShownAt(spec, 1.0F)
                    Try
                        Dim row As DialogButtonRow = RowsOf(form).Single()
                        For Each name As String In spec.Reading
                            Dim found As Control() = form.Controls.Find(name, True)
                            Assert.True(found.Length = 1 AndAlso TypeOf found(0) Is Button, spec.Name & ": '" & name & "' is exactly one button")
                            Assert.Contains(DirectCast(found(0), Button), row.ReadingButtons)
                        Next
                        ' the fields keep their order and the answers come after all of them
                        Assert.Equal(row.Parent.Controls.Count - 1, row.Parent.Controls.GetChildIndex(row))
                        Dim tabs As Integer() = row.ReadingButtons.Select(Function(b) b.TabIndex).ToArray()
                        Assert.Equal(Enumerable.Range(0, tabs.Length).ToArray(), tabs)
                        ' every answer carries a glyph (the row applies it) and no button of the row keeps the old inner padding
                        For Each b As Button In row.ReadingButtons
                            Assert.True(ButtonGlyphs.CarriesGlyph(b), spec.Name & ": " & b.Name & " has no glyph")
                            Assert.Equal(Padding.Empty, b.Padding)
                        Next
                    Finally
                        Release(form)
                    End Try
                Next
            End Sub)
    End Sub

    <Fact>
    Public Sub The_third_batch_keeps_its_code_and_password_fields_masked_and_empty()
        InLanguage("en",
            Sub(kit As Kit)
                For Each spec As Spec In Specs(kit)
                    Dim form As Form = ShownAt(spec, 1.0F)
                    Try
                        Dim masked As List(Of TextBox) = All(form).OfType(Of TextBox)().Where(Function(t) t.UseSystemPasswordChar).ToList()
                        Assert.True(masked.Count = spec.Masked, spec.Name & ": expected " & spec.Masked.ToString(CultureInfo.InvariantCulture) & " masked fields, found " & masked.Count.ToString(CultureInfo.InvariantCulture))
                        For Each t As TextBox In masked
                            Assert.False(t.Multiline, spec.Name & ": a multiline field ignores the password character and shows its text in clear")
                            Assert.Equal("", t.Text)
                        Next
                    Finally
                        Release(form)
                    End Try
                Next
            End Sub)
    End Sub

    ''' <summary>The editor's Enter must not save while the action removes files (a delete, or an archive that deletes its
    ''' originals): UpdateVerdict takes the accept button away and gives it back when the action stops removing.</summary>
    <Fact>
    Public Sub Enter_in_the_scheduled_editor_never_saves_while_the_action_removes_files()
        InLanguage("en",
            Sub(kit As Kit)
                Using form As New Scheduled_Operation_Form(kit.Engine, Nothing)
                    form.Show()
                    Application.DoEvents()
                    Assert.Equal("btnSchedSave", DirectCast(form.AcceptButton, Button).Name)
                    Assert.Equal("btnSchedCancel", DirectCast(form.CancelButton, Button).Name)

                    Dim action As ComboBox = Find(Of ComboBox)(form, "cmbSchedAction")
                    action.SelectedIndex = CInt(ScheduledAction.Delete)
                    Assert.Null(form.AcceptButton)
                    Assert.Equal("btnSchedCancel", DirectCast(form.CancelButton, Button).Name)

                    action.SelectedIndex = CInt(ScheduledAction.Archive)
                    Dim originals As CheckBox = Find(Of CheckBox)(form, "chkDeleteOriginals")
                    If originals IsNot Nothing Then
                        originals.Checked = True
                        Assert.Null(form.AcceptButton)
                        originals.Checked = False
                    End If
                    Assert.Equal("btnSchedSave", DirectCast(form.AcceptButton, Button).Name)

                    action.SelectedIndex = CInt(ScheduledAction.Copy)
                    Assert.Equal("btnSchedSave", DirectCast(form.AcceptButton, Button).Name)
                End Using
            End Sub)
    End Sub

    ''' <summary>Cancel of the editor dismisses without handing a saved id out; the preview and history close the same way.</summary>
    <Fact>
    Public Sub Cancel_in_the_scheduled_windows_dismisses_without_saving()
        InLanguage("en",
            Sub(kit As Kit)
                Using form As New Scheduled_Operation_Form(kit.Engine, Nothing)
                    form.Show()
                    Application.DoEvents()
                    DirectCast(form.CancelButton, Button).PerformClick()
                    Assert.Equal(DialogResult.Cancel, form.DialogResult)
                    Assert.True(String.IsNullOrEmpty(form.SavedId))
                End Using
                Assert.Empty(kit.Engine.Snapshot())
            End Sub)
    End Sub

    ''' <summary>A support bundle is sent only on a deliberate click: Enter and Escape both mean Cancel, the Send answer is
    ''' the only one that returns OK, the reveal command returns nothing, and an oversize bundle cannot be sent at all.</summary>
    <Fact>
    Public Sub The_consent_dialog_defaults_to_cancel_and_sends_only_on_a_click()
        InLanguage("en",
            Sub(kit As Kit)
                For Each oversize As Boolean In New Boolean() {False, True}
                    Using dialog As Form = SendLogsFlow.BuildConsentDialog(Nothing, Package(oversize))
                        Dim cancel As Button = Find(Of Button)(dialog, "xsl_Cancel")
                        Dim send As Button = Find(Of Button)(dialog, "xsl_Send")
                        Dim reveal As Button = Find(Of Button)(dialog, "xsl_Reveal")
                        Assert.Same(cancel, dialog.AcceptButton)
                        Assert.Same(cancel, dialog.CancelButton)
                        Assert.Equal(DialogResult.Cancel, cancel.DialogResult)
                        Assert.Equal(DialogResult.OK, send.DialogResult)
                        Assert.Equal(DialogResult.None, reveal.DialogResult)
                        Assert.Equal(Not oversize, send.Enabled)
                        Assert.True(reveal.Enabled)
                    End Using
                Next
            End Sub)
    End Sub

    ''' <summary>The disk windows never let Enter grant a disk or open a protected one by itself: none of the three has an
    ''' accept button, and Escape is Cancel (the disks window itself only closes).</summary>
    <Fact>
    Public Sub The_disk_windows_have_no_accept_button()
        InLanguage("en",
            Sub(kit As Kit)
                Using register As New DiskOptionsForm(True, True)
                    Assert.Null(register.AcceptButton)
                    Assert.Equal("xdo_Cancel", DirectCast(register.CancelButton, Button).Name)
                End Using
                Using open As New DiskOptionsForm(False, True, False)
                    Assert.Null(open.AcceptButton)
                    Assert.Equal("xdo_Cancel", DirectCast(open.CancelButton, Button).Name)
                End Using
                Using disks As New Share_Disks_Form()
                    Assert.Null(disks.AcceptButton)
                    Assert.Equal("xsd_Close", DirectCast(disks.CancelButton, Button).Name)
                End Using
            End Sub)
    End Sub

    ''' <summary>The guided add is a wizard: Cancel, then Back, then the forward button last - and the forward button is the
    ''' affirmative answer until the add is done, when it becomes a plain Close.</summary>
    <Fact>
    Public Sub The_guided_add_reads_cancel_back_next_and_the_forward_button_is_the_affirmative_until_it_is_done()
        InLanguage("en",
            Sub(kit As Kit)
                For stage As Integer = 0 To 3
                    Using form As Form = GuidedAdd(stage)
                        form.Show()
                        Application.DoEvents()
                        Dim row As DialogButtonRow = RowsOf(form).Single()
                        Assert.Equal(New String() {"xra_Cancel", "xra_Back", "xra_Next"}, row.ReadingButtons.Select(Function(b) b.Name).ToArray())
                        Dim [next] As Button = row.ReadingButtons(2)
                        Assert.Equal(If(stage = 3, ButtonGlyphRole.Neutral, ButtonGlyphRole.Affirmative), ButtonGlyphs.RoleOf([next]))
                        Assert.Null(form.AcceptButton)
                        Assert.Equal(stage = 3, Not row.ReadingButtons(1).Visible)
                        ' nothing picked: the forward button waits for a folder; from the questions on it is open
                        Assert.Equal(stage >= 1, [next].Enabled)
                    End Using
                Next
            End Sub)
    End Sub

    ''' <summary>The windows are adopters, not look-alikes: their source builds the row, binds it, and no longer sets
    ''' CancelButton or places an answer button by hand. A window that decides about its accept button itself says so with
    ''' bindAccept:=False (the editor, the disks window, the guided add, the consent dialog).</summary>
    <Fact>
    Public Sub The_third_batch_sources_adopt_the_row()
        Dim dir As New DirectoryInfo(AppContext.BaseDirectory)
        Dim src As String = Nothing
        While dir IsNot Nothing AndAlso src Is Nothing
            Dim candidate As String = Path.Combine(dir.FullName, "src")
            If Directory.Exists(candidate) AndAlso File.Exists(Path.Combine(candidate, "DialogButtonRow.vb")) Then src = candidate
            dir = dir.Parent
        End While
        Assert.NotNull(src)
        Dim ownAccept As New HashSet(Of String)() From {"Scheduled_Operation_Form", "Share_Disks_Form", "Resource_Add_Form", "SendLogsFlow"}
        For Each file As String In New String() {"Scheduled_Operation_Form", "Scheduled_Resource_Picker_Form", "Scheduled_History_Form", "Share_Enable_Form",
                                                 "Share_Root_Params_Form", "Share_Disks_Form", "Phone_Code_Intake_Form", "SendLogsFlow",
                                                 "Resource_Add_Form", "PackageWizardForm"}
            Dim text As String = System.IO.File.ReadAllText(Path.Combine(src, "FastMediaSorterCompanion", "Forms", file & ".vb"))
            Assert.Contains("New DialogButtonRow()", text)
            Assert.Contains(".Bind(", text)
            Assert.Contains("DpiLayout.AddAnswer(", text)
            Assert.DoesNotMatch("\bCancelButton\s*=", text)
            If ownAccept.Contains(file) Then
                Assert.Contains("bindAccept:=False", text)
            Else
                Assert.DoesNotMatch("\bAcceptButton\s*=", text)
                Assert.DoesNotContain("bindAccept:=False", text)
            End If
        Next
        ' the disks window's inline "mounted disk" picker and its option dialog are bound too
        Dim disks As String = System.IO.File.ReadAllText(Path.Combine(src, "FastMediaSorterCompanion", "Forms", "Share_Disks_Form.vb"))
        Assert.Contains("xsd_MountedButtons", disks)
        Assert.Contains(".Bind(picker", disks)
        Assert.Contains("xdo_Buttons", disks)
        ' the folder-parameters editor's hosting part builds no answer of its own
        Dim hosting As String = System.IO.File.ReadAllText(Path.Combine(src, "FastMediaSorterCompanion", "Forms", "Share_Root_Params_Form.Hosting.vb"))
        Assert.DoesNotContain("DialogButtonRow", hosting)
    End Sub

End Class
