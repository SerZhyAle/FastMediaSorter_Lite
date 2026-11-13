#If Not NETFRAMEWORK Then
Option Strict On

Imports System.Collections.Generic
Imports System.Drawing
Imports System.Globalization
Imports System.Linq
Imports System.Threading
Imports System.Windows.Forms
Imports Xunit

''' <summary>
''' Ticket 124 section 3.1 (UX-202, UX-203): the layout test the specification asks for - each secondary window is
''' built and shown at 100 %, 150 % and 175 % and nothing may leave its client area, clip its own text or lie over
''' a sibling. A window cannot be looked at here, so a scale is EMULATED the way WinForms applies one: the font grows
''' by the factor (a point font does that on its own under PerMonitorV2) and the fixed pixels are scaled with
''' <c>Control.Scale</c> (what AutoScaleMode.Dpi does at the first layout). The reflow the windows run from OnLoad
''' (DialogLayout.FitContent) is then what is under test - exactly the code that runs on a real 175 % display.
'''
''' What this cannot say: how the window LOOKS at 175 % on a real monitor (pixel spacing, the 13 languages with
''' real fonts) - that stays manual acceptance 2 of the ticket.
''' </summary>
<Collection("AppFileLogger")>
Public Class DialogLayoutTests
    Implements IDisposable

    ' The windows built here call DialogLayout.ApplyAutoScale, which composes the Windows Text size of the machine
    ' (ticket 279, T-2); the scales below are EMULATED on top of the ordinary one, so the Text size is pinned to 1.0.
    Public Sub New()
        UiSizing.TextFactorOverride = 1.0F
    End Sub

    Public Sub Dispose() Implements IDisposable.Dispose
        UiSizing.TextFactorOverride = 0.0F
    End Sub

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
        Assert.True(thread.Join(60000), "the UI check did not finish")
        If failure IsNot Nothing Then System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(failure).Throw()
    End Sub

    ' --- the audit --------------------------------------------------------------------------------

    ''' <summary>Everything wrong with the window as laid out now, as sentences (empty = fine).</summary>
    Friend Shared Function Problems(form As Form) As List(Of String)
        Dim found As New List(Of String)()
        Dim client As Rectangle = form.ClientRectangle
        Dim area As Rectangle = Screen.FromControl(form).WorkingArea
        If form.Width > area.Width OrElse form.Height > area.Height Then
            found.Add("the window " & form.Size.ToString() & " is larger than the working area " & area.Size.ToString())
        End If
        AuditContainer(form, client, found, form.Name & "/")
        Return found
    End Function

    Private Shared Sub AuditContainer(container As Control, client As Rectangle, found As List(Of String), path As String)
        Dim placed As New List(Of Control)()
        For Each c As Control In container.Controls
            If Not c.Visible Then Continue For
            Dim here As String = path & If(String.IsNullOrEmpty(c.Name), c.GetType().Name, c.Name)
            If c.Dock = DockStyle.None Then
                placed.Add(c)
                If c.Left < -1 OrElse c.Top < -1 OrElse c.Right > client.Width + 1 OrElse c.Bottom > client.Height + 1 Then
                    found.Add(here & " " & c.Bounds.ToString() & " leaves the client area " & client.Size.ToString())
                End If
            End If
            AuditText(c, found, here)
            If TypeOf c Is TableLayoutPanel OrElse TypeOf c Is FlowLayoutPanel OrElse TypeOf c Is Panel Then
                AuditContainer(c, c.ClientRectangle, found, here & "/")
            End If
        Next
        For i As Integer = 0 To placed.Count - 1
            For j As Integer = i + 1 To placed.Count - 1
                Dim overlap As Rectangle = Rectangle.Intersect(placed(i).Bounds, placed(j).Bounds)
                If overlap.Width > 1 AndAlso overlap.Height > 1 Then
                    found.Add(path & Describe(placed(i)) & " " & placed(i).Bounds.ToString() & " lies over " & Describe(placed(j)) & " " & placed(j).Bounds.ToString())
                End If
            Next
        Next
    End Sub

    Private Shared Function Describe(c As Control) As String
        Return If(String.IsNullOrEmpty(c.Name), c.GetType().Name, c.Name)
    End Function

    Private Shared Sub AuditText(c As Control, found As List(Of String), here As String)
        Dim label As Label = TryCast(c, Label)
        If label IsNot Nothing AndAlso Not label.AutoSize AndAlso Not label.AutoEllipsis AndAlso label.Text.Length > 0 Then
            Dim flags As TextFormatFlags = TextFormatFlags.WordBreak Or If(label.UseMnemonic, TextFormatFlags.Default, TextFormatFlags.NoPrefix)
            Dim need As Integer = TextRenderer.MeasureText(label.Text, label.Font, New Size(Math.Max(1, label.Width - label.Padding.Horizontal), Integer.MaxValue), flags).Height
            If need > label.Height + 2 Then found.Add(here & " is " & label.Height.ToString() & " px high and its text needs " & need.ToString())
        End If
        Dim button As Button = TryCast(c, Button)
        If button IsNot Nothing AndAlso Not button.AutoSize AndAlso button.Text.Length > 0 Then
            Dim width As Integer = TextRenderer.MeasureText(button.Text.Replace("&", ""), button.Font).Width
            If width + 8 > button.Width Then found.Add(here & " is " & button.Width.ToString() & " px wide and its caption needs " & (width + 8).ToString())
        End If
    End Sub

    ''' <summary>Builds a window, emulates a display scale, shows it (OnLoad runs the reflow) and audits it.</summary>
    Private Shared Function AtScale(factory As Func(Of Form), factor As Single) As List(Of String)
        Dim form As Form = factory()
        Try
            Dim auto As Single = form.DeviceDpi / 96.0F
            Dim extra As Single = factor / auto
            If Math.Abs(extra - 1.0F) > 0.01F Then
                form.Font = New Font(form.Font.FontFamily, form.Font.SizeInPoints * extra)
                form.Scale(New SizeF(extra, extra))
            End If
            form.StartPosition = FormStartPosition.Manual
            form.Location = New Point(20, 20)
            form.Show()
            Return Problems(form)
        Finally
            form.Dispose()
        End Try
    End Function

    Private Shared Sub AssertCleanAtEveryScale(name As String, factory As Func(Of Form))
        Dim all As New List(Of String)()
        For Each factor As Single In New Single() {1.0F, 1.5F, 1.75F}
            For Each p As String In AtScale(factory, factor)
                all.Add(name & " @" & CInt(factor * 100).ToString() & "%: " & p)
            Next
        Next
        Assert.True(all.Count = 0, String.Join(vbLf, all))
    End Sub

    Private Const LongName As String = "A-very-long-container-name-that-has-no-spaces-at-all-and-keeps-going-photo-archive-2026-01-02.fd-sec"

    ''' <summary>A window with a label too short for its wrapped text and a button right under it - the shape
    ''' of the old password window with a retry: the audit must see it, and the reflow must repair it.</summary>
    Private Class CrampedForm
        Inherits Form

        Friend ReadOnly Message As New Label()
        Friend ReadOnly Below As New Button()
        Friend ReadOnly Wide As New Button()

        Friend Sub New()
            FormBorderStyle = FormBorderStyle.FixedDialog
            ClientSize = New Size(300, 130)
            Message.Name = "message"
            Message.AutoSize = False
            Message.SetBounds(10, 10, 280, 16)
            Message.Text = "This sentence is deliberately long enough to need three or four lines at this width so that it cannot fit into sixteen pixels of height at all."
            Below.Name = "below"
            Below.SetBounds(10, 40, 80, 28)
            Below.Text = "OK"
            Wide.Name = "wide"
            Wide.SetBounds(200, 90, 60, 28)
            Wide.Text = "A button caption that is far too long for it"
            Controls.Add(Message)
            Controls.Add(Below)
            Controls.Add(Wide)
            DialogLayout.ApplyAutoScale(Me)
        End Sub

        Friend Sub Reflow()
            DialogLayout.FitContent(Me)
        End Sub
    End Class

    <Fact, Trait("Kind", "UiForm"), Trait("Form", "DialogLayout")>
    Public Sub The_audit_catches_a_clipped_label_and_the_reflow_repairs_it()
        OnSta(Sub()
                  Using form As New CrampedForm()
                      form.StartPosition = FormStartPosition.Manual
                      form.Location = New Point(20, 20)
                      form.Show()
                      ' Show ran OnLoad, but this form does not reflow by itself - exactly the state before the fix.
                      Dim before As List(Of String) = Problems(form)
                      Assert.NotEmpty(before)
                      Assert.Contains(before, Function(s) s.Contains("its text needs"))
                      form.Reflow()
                      Dim after As List(Of String) = Problems(form)
                      Assert.True(after.Count = 0, String.Join(vbLf, after))
                      Assert.True(form.Message.Height > 16, "the label grew")
                      Assert.True(form.Below.Top >= form.Message.Bottom, "the button moved below the grown label")
                      Assert.True(form.Wide.Width > 60, "the long caption widened its button")
                  End Using
              End Sub)
    End Sub

    ' --- the Windows Text size (ticket 279, T-2) ---------------------------------------------------

    ''' <summary>A window with one fixed label, built at an explicit text factor; the second call must change nothing.</summary>
    Private Class TextScaledForm
        Inherits Form

        Friend ReadOnly Message As New Label()

        Friend Sub New(textFactor As Single)
            FormBorderStyle = FormBorderStyle.FixedDialog
            ClientSize = New Size(300, 100)
            Message.AutoSize = False
            Message.SetBounds(10, 10, 280, 40)
            Message.Text = "A fixed label."
            Controls.Add(Message)
            DialogLayout.ApplyAutoScale(Me, textFactor)
            DialogLayout.ApplyAutoScale(Me, textFactor)
        End Sub
    End Class

    <Fact, Trait("Kind", "UiForm"), Trait("Form", "DialogLayout")>
    Public Sub A_text_enlargement_grows_the_font_and_the_authored_layout_together_and_the_ordinary_size_changes_nothing()
        OnSta(Sub()
                  Using plain As New TextScaledForm(1.0F)
                      Using grown As New TextScaledForm(1.5F)
                          ' AutoScaleDimensions is not asserted: WinForms normalises it to the current DPI once it has
                          ' scaled the window, so it cannot be read back. The growth below is what the base 96 / 1.5 does.
                          Assert.Equal(AutoScaleMode.Dpi, plain.AutoScaleMode)
                          Assert.Equal(AutoScaleMode.Dpi, grown.AutoScaleMode)
                          ' 1.5 times once, not 2.25 times: the second ApplyAutoScale call of the builder is ignored.
                          Assert.Equal(CDbl(plain.Font.SizeInPoints * 1.5F), CDbl(grown.Font.SizeInPoints), 2)
                          Assert.Equal(CDbl(grown.Font.SizeInPoints), CDbl(grown.Message.Font.SizeInPoints), 2)
                          plain.StartPosition = FormStartPosition.Manual
                          grown.StartPosition = FormStartPosition.Manual
                          plain.Location = New Point(20, 20)
                          grown.Location = New Point(20, 20)
                          plain.Show()
                          grown.Show()
                          Dim width As Integer = CInt(Math.Round(plain.Message.Width * 1.5R))
                          Dim height As Integer = CInt(Math.Round(plain.Message.Height * 1.5R))
                          Assert.InRange(grown.Message.Width, width - 2, width + 2)
                          Assert.InRange(grown.Message.Height, height - 2, height + 2)
                      End Using
                  End Using
              End Sub)
    End Sub

    ''' <summary>A window whose children carry fonts of their own - a bold heading derived from the window font, a panel with
    ''' a monospaced face and a child that inherits it, a pixel-sized label - built at an explicit text factor.</summary>
    Private Class OwnFontForm
        Inherits Form

        Friend ReadOnly Heading As New Label()
        Friend ReadOnly Plain As New Label()
        Friend ReadOnly Box As New Panel()
        Friend ReadOnly Inner As New Label()
        Friend ReadOnly Pixels As New Label()
        Private ReadOnly headingFont As Font
        Private ReadOnly boxFont As Font
        Private ReadOnly pixelFont As Font

        Friend Sub New(textFactor As Single)
            FormBorderStyle = FormBorderStyle.FixedDialog
            ClientSize = New Size(400, 200)
            headingFont = New Font(Me.Font, FontStyle.Bold)
            boxFont = New Font("Consolas", 10.0F)
            pixelFont = New Font(Me.Font.FontFamily, 12.0F, FontStyle.Regular, GraphicsUnit.Pixel)
            Heading.AutoSize = False
            Heading.SetBounds(10, 10, 280, 30)
            Heading.Font = headingFont
            Heading.Text = "A bold heading."
            Plain.AutoSize = False
            Plain.SetBounds(10, 50, 280, 30)
            Plain.Text = "Inherits the window font."
            Box.SetBounds(10, 90, 280, 40)
            Box.Font = boxFont
            Inner.AutoSize = False
            Inner.SetBounds(0, 0, 200, 30)
            Inner.Text = "Inherits the panel font."
            Box.Controls.Add(Inner)
            Pixels.AutoSize = False
            Pixels.SetBounds(10, 140, 280, 30)
            Pixels.Font = pixelFont
            Pixels.Text = "A pixel font."
            Controls.AddRange(New Control() {Heading, Plain, Box, Pixels})
            DialogLayout.ApplyAutoScale(Me, textFactor)
        End Sub

        Protected Overrides Sub Dispose(disposing As Boolean)
            MyBase.Dispose(disposing)
            If disposing Then
                headingFont.Dispose()
                boxFont.Dispose()
                pixelFont.Dispose()
            End If
        End Sub
    End Class

    <Fact, Trait("Kind", "UiForm"), Trait("Form", "DialogLayout")>
    Public Sub A_control_with_its_own_font_grows_with_the_window_text_size_and_the_ordinary_size_keeps_it()
        OnSta(Sub()
                  Using plain As New OwnFontForm(1.0F)
                      Using grown As New OwnFontForm(1.5F)
                          ' At the ordinary text size nothing is touched: the heading is still the window font, bold.
                          Assert.Equal(CDbl(plain.Font.SizeInPoints), CDbl(plain.Heading.Font.SizeInPoints), 2)
                          ' An own font grows by the same factor as the window font, once, with its face and style kept.
                          Assert.Equal(CDbl(plain.Heading.Font.SizeInPoints * 1.5F), CDbl(grown.Heading.Font.SizeInPoints), 2)
                          Assert.True(grown.Heading.Font.Bold)
                          Assert.Equal(plain.Heading.Font.FontFamily.Name, grown.Heading.Font.FontFamily.Name)
                          Assert.Equal(CDbl(plain.Box.Font.SizeInPoints * 1.5F), CDbl(grown.Box.Font.SizeInPoints), 2)
                          Assert.Equal(plain.Box.Font.FontFamily.Name, grown.Box.Font.FontFamily.Name)
                          ' What inherits follows its parent exactly once (never 1.5 x 1.5).
                          Assert.Equal(CDbl(grown.Font.SizeInPoints), CDbl(grown.Plain.Font.SizeInPoints), 2)
                          Assert.Equal(CDbl(grown.Box.Font.SizeInPoints), CDbl(grown.Inner.Font.SizeInPoints), 2)
                          ' A pixel-sized font is sized by its own control from the scale it reads, so it is left alone.
                          Assert.Equal(GraphicsUnit.Pixel, grown.Pixels.Font.Unit)
                          Assert.Equal(CDbl(plain.Pixels.Font.Size), CDbl(grown.Pixels.Font.Size), 2)
                      End Using
                  End Using
              End Sub)
    End Sub

    ''' <summary>Builds a window the way the product does - its builder reads the Windows Text size, pinned here - shows it
    ''' and audits it. The display scale is left as it is; the text size is the only thing varied.</summary>
    Private Shared Function AtTextSize(factory As Func(Of Form), text As Single) As List(Of String)
        UiSizing.TextFactorOverride = text
        Try
            Dim form As Form = factory()
            Try
                form.StartPosition = FormStartPosition.Manual
                form.Location = New Point(20, 20)
                form.Show()
                Return Problems(form)
            Finally
                form.Dispose()
            End Try
        Finally
            UiSizing.TextFactorOverride = 1.0F
        End Try
    End Function

    <Fact, Trait("Kind", "UiForm"), Trait("Form", "Secure_Password_Form"), Trait("Form", "Resource_Credential_Form")>
    Public Sub Two_secondary_windows_stay_clean_at_a_150_percent_Windows_text_size()
        OnSta(Sub()
                  Dim all As New List(Of String)()
                  all.AddRange(AtTextSize(Function() New Secure_Password_Form(LongName, retry:=True), 1.5F))
                  all.AddRange(AtTextSize(Function() New Resource_Credential_Form("My server with a rather long display name", "somebody-with-a-long-user-name", retry:=True), 1.5F))
                  Assert.True(all.Count = 0, String.Join(vbLf, all))
              End Sub)
    End Sub

    ''' <summary>One row per shipped language, read from the engine so a fourteenth language joins the matrix on its own.</summary>
    Public Shared ReadOnly Property ShippedLanguages As TheoryData(Of String)
        Get
            Dim rows As New TheoryData(Of String)()
            For Each code As String In Localization.Codes
                rows.Add(code)
            Next
            Return rows
        End Get
    End Property

    <Fact>
    Public Sub The_text_size_matrix_reads_all_thirteen_shipped_languages()
        ' The data source above is built from Localization.Codes, so the matrix cannot drift from the engine; this
        ' pins the count the ticket promises (13) and fails the day a language is added without a look at the matrix.
        Assert.Equal(13, Localization.Codes.Length)
    End Sub

    ''' <summary>Ticket 279 T-2 "Done when": a Windows Text size of 100, 150 and 200 percent reflows the viewer's code-built
    ''' dialogs without a clipped label or button, in each of the 13 languages. The password windows and the two resource
    ''' windows are the long-caption ones; every question of <c>AskSpecs.All()</c> is built too, so a new question joins the
    ''' matrix on its own. The display scale is the one of the machine; the Text size is pinned per window by
    ''' <c>AtTextSize</c>. The look on a real screen (real fonts, RTL, the Settings window) stays the hand run of T-5.</summary>
    <Theory, Trait("Kind", "UiForm"), Trait("Form", "Secure_Password_Form"), Trait("Form", "Resource_Credential_Form"), Trait("Form", "Resource_Edit_Form"), Trait("Form", "Ask_Dialog")>
    <MemberData(NameOf(ShippedLanguages))>
    Public Sub The_secondary_windows_and_the_questions_stay_clean_at_100_150_and_200_percent_Windows_text_size(code As String)
        Dim saved As String = Localization.CurrentCode
        Try
            Localization.CurrentCode = code
            OnSta(Sub()
                      Dim all As New List(Of String)()
                      For Each text As Single In New Single() {1.0F, 1.5F, 2.0F}
                          Dim prefix As String = code & " @text " & CInt(text * 100).ToString(CultureInfo.InvariantCulture) & "%: "
                          For Each p As String In AtTextSize(Function() New Secure_Password_Form(LongName, retry:=True), text)
                              all.Add(prefix & "Secure_Password_Form " & p)
                          Next
                          For Each p As String In AtTextSize(Function() New Resource_Credential_Form("My server with a rather long display name", "somebody-with-a-long-user-name", retry:=True), text)
                              all.Add(prefix & "Resource_Credential_Form " & p)
                          Next
                          For Each p As String In AtTextSize(Function() New Resource_Edit_Form(Nothing), text)
                              all.Add(prefix & "Resource_Edit_Form " & p)
                          Next
                          For Each spec As AskSpec In AskSpecs.All()
                              Dim question As AskSpec = spec
                              For Each p As String In AtTextSize(Function() New Ask_Dialog(question), text)
                                  all.Add(prefix & "Ask_Dialog '" & question.Title & "' " & p)
                              Next
                          Next
                      Next
                      Assert.True(all.Count = 0, String.Join(vbLf, all))
                  End Sub)
        Finally
            Localization.CurrentCode = saved
        End Try
    End Sub

    ' --- the windows ------------------------------------------------------------------------------

    <Fact, Trait("Kind", "UiForm"), Trait("Form", "Secure_Password_Form")>
    Public Sub The_container_password_window_fits_at_every_scale_with_a_long_name_and_a_retry()
        OnSta(Sub() AssertCleanAtEveryScale("Secure_Password_Form", Function() New Secure_Password_Form(LongName, retry:=True)))
    End Sub

    <Fact, Trait("Kind", "UiForm"), Trait("Form", "Secure_Password_Form")>
    Public Sub The_container_password_window_fits_on_the_first_ask()
        OnSta(Sub() AssertCleanAtEveryScale("Secure_Password_Form(first)", Function() New Secure_Password_Form("a.fd-sec", retry:=False)))
    End Sub

    <Fact, Trait("Kind", "UiForm"), Trait("Form", "Resource_Credential_Form")>
    Public Sub The_resource_password_window_fits_at_every_scale()
        OnSta(Sub() AssertCleanAtEveryScale("Resource_Credential_Form", Function() New Resource_Credential_Form("My server with a rather long display name", "somebody-with-a-long-user-name", retry:=True)))
    End Sub

    <Fact, Trait("Kind", "UiForm"), Trait("Form", "Resource_Edit_Form")>
    Public Sub The_new_resource_window_fits_at_every_scale()
        OnSta(Sub() AssertCleanAtEveryScale("Resource_Edit_Form(new)", Function() New Resource_Edit_Form(Nothing)))
    End Sub

    <Fact, Trait("Kind", "UiForm"), Trait("Form", "Resource_Edit_Form")>
    Public Sub The_edit_resource_window_with_a_pinned_key_fits_at_every_scale()
        Dim existing As New ResourceDescriptor With {
            .Id = "r1", .DisplayName = "NAS", .Kind = ResourceKind.Sftp, .Host = "nas.local", .Port = 22, .UserName = "me", .RootPath = "/photos",
            .PinnedFingerprint = "SHA256:abcdefghijklmnopqrstuvwxyz0123456789ABCDEFG"}
        OnSta(Sub() AssertCleanAtEveryScale("Resource_Edit_Form(edit)", Function() New Resource_Edit_Form(existing)))
    End Sub

    ''' <summary>UX-203: the edit window gained the reveal the password window has; it starts hidden.</summary>
    <Fact>
    Public Sub The_edit_resource_window_offers_a_password_reveal_that_starts_off()
        OnSta(Sub()
                  Using form As New Resource_Edit_Form(Nothing)
                      ' The check box lives in its option row now (ticket 243 T-11b); it is the same control with the same name.
                      Dim reveal As CheckBox = form.Controls.OfType(Of OptionRowHost)().Select(Function(r) r.Check).FirstOrDefault(Function(c) c.Name = "res_Reveal")
                      Dim password As TextBox = form.Controls.OfType(Of TextBox)().First(Function(c) c.Name = "res_Password")
                      Assert.NotNull(reveal)
                      Assert.False(reveal.Checked)
                      Assert.True(password.UseSystemPasswordChar)
                      reveal.Checked = True
                      Assert.False(password.UseSystemPasswordChar)
                  End Using
              End Sub)
    End Sub

    Private NotInheritable Class NoSecrets
        Implements IResourceSecretStore

        Public Sub PutBlob(handle As String, protectedBlob As String) Implements IResourceSecretStore.PutBlob
        End Sub

        Public Function GetBlob(handle As String) As String Implements IResourceSecretStore.GetBlob
            Return Nothing
        End Function

        Public Sub DeleteBlob(handle As String) Implements IResourceSecretStore.DeleteBlob
        End Sub

        Public Function AllHandles() As IReadOnlyList(Of String) Implements IResourceSecretStore.AllHandles
            Return New List(Of String)()
        End Function
    End Class

    <Fact, Trait("Kind", "UiForm"), Trait("Form", "Resource_Export_Form")>
    Public Sub The_export_resource_window_fits_at_every_scale()
        Dim existing As New ResourceDescriptor With {
            .Id = "r1", .DisplayName = "NAS", .Kind = ResourceKind.Sftp, .Host = "nas.local", .Port = 22, .UserName = "me", .RootPath = "/photos",
            .PinnedFingerprint = "SHA256:abcdefghijklmnopqrstuvwxyz0123456789ABCDEFG"}
        OnSta(Sub() AssertCleanAtEveryScale("Resource_Export_Form", Function() New Resource_Export_Form(existing, New NoSecrets())))
    End Sub

    <Fact, Trait("Kind", "UiForm"), Trait("Form", "LiveBroadcast_Form")>
    Public Sub The_live_broadcast_window_fits_at_every_scale()
        OnSta(Sub() AssertCleanAtEveryScale("LiveBroadcast_Form", Function() New LiveBroadcast_Form()))
    End Sub

    <Fact, Trait("Kind", "UiForm"), Trait("Form", "Video_Convert_Form"), Trait("Form", "Video_Replace_Confirm_Form")>
    Public Sub The_video_progress_and_confirmation_windows_fit_at_every_scale()
        OnSta(Sub()
                  AssertCleanAtEveryScale("Video_Convert_Form", Function() New Video_Convert_Form(determinate:=True))
                  AssertCleanAtEveryScale("Video_Replace_Confirm_Form", Function() New Video_Replace_Confirm_Form(LongName & ".webp", LongName & ".mp4", warnAboutAlpha:=True))
              End Sub)
    End Sub

    <Fact, Trait("Kind", "UiForm"), Trait("Form", "Hotkeys_Form"), Trait("Form", "Batch_Report_Form")>
    Public Sub The_hotkey_and_report_windows_stay_on_the_monitor_at_every_scale()
        Dim report As New BatchReport()
        report.Results.Add(New BatchItemResult With {.Path = "C:\a\b.jpg", .State = BatchItemState.Done})
        OnSta(Sub()
                  For Each factor As Single In New Single() {1.0F, 1.75F}
                      Dim p1 As List(Of String) = AtScale(Function() New Hotkeys_Form(New Dictionary(Of String, String)()), factor)
                      Dim p2 As List(Of String) = AtScale(Function() New Batch_Report_Form(report), factor)
                      Assert.True(p1.All(Function(s) s.Contains("larger than the working area") = False), String.Join(vbLf, p1))
                      Assert.True(p2.All(Function(s) s.Contains("larger than the working area") = False), String.Join(vbLf, p2))
                  Next
              End Sub)
    End Sub

    ''' <summary>The full audit, not only the monitor bound: "Сбросить всё" used to start 30 px inside "Снять
    ''' сочетание" on the capture row, so one hid part of the other.</summary>
    <Fact, Trait("Kind", "UiForm"), Trait("Form", "Hotkeys_Form")>
    Public Sub The_hotkey_window_has_no_overlapping_controls_at_every_scale()
        OnSta(Sub() AssertCleanAtEveryScale("Hotkeys_Form", Function() New Hotkeys_Form(New Dictionary(Of String, String)())))
    End Sub

    <Fact, Trait("Kind", "UiForm"), Trait("Form", "Batch_Report_Form")>
    Public Sub The_batch_report_window_is_clean_at_every_scale()
        Dim report As New BatchReport()
        report.Results.Add(New BatchItemResult With {.Path = "C:\a\b.jpg", .State = BatchItemState.Done})
        OnSta(Sub() AssertCleanAtEveryScale("Batch_Report_Form", Function() New Batch_Report_Form(report)))
    End Sub

    ''' <summary>A window that grows in OnLoad (a wrapped warning) must still open centred on its owner: the reflow
    ''' changes its size after the start position was decided.</summary>
    <Fact, Trait("Kind", "UiForm"), Trait("Form", "Secure_Password_Form")>
    Public Sub A_window_that_grew_in_OnLoad_is_still_centred_on_its_owner()
        OnSta(Sub()
                  Using owner As New Form()
                      owner.StartPosition = FormStartPosition.Manual
                      owner.Bounds = New Rectangle(100, 100, 1000, 700)
                      owner.Show()
                      For Each retry As Boolean In New Boolean() {False, True}
                      Using dialog As New Secure_Password_Form(LongName, retry)
                          Dim before As Size = dialog.Size
                          Dim shown As Rectangle = Rectangle.Empty
                          AddHandler dialog.Shown, Sub()
                                                       shown = dialog.Bounds
                                                       dialog.Close()
                                                   End Sub
                          dialog.ShowDialog(owner)
                          dialog.Bounds = shown
                          Assert.True(dialog.Height >= before.Height, "the retry warning made the window taller")
                          Dim ownerCentre As Point = New Point(owner.Left + owner.Width \ 2, owner.Top + owner.Height \ 2)
                          Dim centre As Point = New Point(dialog.Left + dialog.Width \ 2, dialog.Top + dialog.Height \ 2)
                          Assert.True(Math.Abs(centre.X - ownerCentre.X) <= 6 AndAlso Math.Abs(centre.Y - ownerCentre.Y) <= 6,
                                      "retry=" & retry.ToString() & " owner " & owner.Bounds.ToString() & " dialog before " & before.ToString() & " after " & dialog.Bounds.ToString())
                      End Using
                      Next
                  End Using
              End Sub)
    End Sub

    ' --- the question window -----------------------------------------------------------------------

    <Fact, Trait("Kind", "UiForm"), Trait("Form", "Ask_Dialog")>
    Public Sub The_name_collision_window_fits_at_every_scale_with_both_files_described()
        Dim facts As New AskFileFacts(5L * 1024 * 1024, New Date(2026, 1, 2, 3, 4, 5))
        OnSta(Sub() AssertCleanAtEveryScale("Ask_Dialog(collision)", Function() New Ask_Dialog(AskSpecs.NameCollision(LongName & ".jpg", facts, facts))))
    End Sub

    <Fact, Trait("Kind", "UiForm"), Trait("Form", "Ask_Dialog")>
    Public Sub The_prompt_windows_fit_at_every_scale()
        OnSta(Sub()
                  AssertCleanAtEveryScale("Ask_Dialog(rename)", Function() New Ask_Dialog(AskSpecs.RenamePrompt("photo", ".jpg", Nothing)))
                  AssertCleanAtEveryScale("Ask_Dialog(url)", Function() New Ask_Dialog(AskSpecs.OpenUrlPrompt("http://server/video.mp4")))
                  AssertCleanAtEveryScale("Ask_Dialog(jump)", Function() New Ask_Dialog(AskSpecs.JumpToNumberPrompt(3, 42)))
              End Sub)
    End Sub

    <Fact, Trait("Kind", "UiForm"), Trait("Form", "Ask_Dialog")>
    Public Sub The_import_and_conflict_windows_fit_at_every_scale()
        OnSta(Sub()
                  AssertCleanAtEveryScale("Ask_Dialog(import)", Function() New Ask_Dialog(AskSpecs.SettingsImport("Changed: 12" & vbCrLf & "Merging changes: 3" & vbCrLf & String.Join(vbCrLf, Enumerable.Range(1, 12).Select(Function(i) "Setting number " & i.ToString())))))
                  AssertCleanAtEveryScale("Ask_Dialog(conflict)", Function() New Ask_Dialog(AskSpecs.HotkeyConflict("Ctrl+Shift+Alt+K", "A rather long action caption that goes on")))
              End Sub)
    End Sub

    ''' <summary>The safe button holds the keyboard when the window opens, and Enter acts on it.</summary>
    <Fact>
    Public Sub The_default_button_is_the_accept_button_and_the_safe_one()
        OnSta(Sub()
                  Dim facts As New AskFileFacts(1, Nothing)
                  Using form As New Ask_Dialog(AskSpecs.NameCollision("a.jpg", facts, facts))
                      Assert.Equal("ask_Button2", DirectCast(form.AcceptButton, Button).Name)
                      Assert.Equal("ask_Button2", DirectCast(form.CancelButton, Button).Name)
                  End Using
              End Sub)
    End Sub

    ''' <summary>Escape (the cancel button) and the close box answer with the spec's cancel index.</summary>
    <Fact>
    Public Sub Escape_and_the_close_box_answer_with_the_cancel_index()
        OnSta(Sub()
                  Dim facts As New AskFileFacts(1, Nothing)
                  Using form As New Ask_Dialog(AskSpecs.NameCollision("a.jpg", facts, facts))
                      form.StartPosition = FormStartPosition.Manual
                      form.Show()
                      form.Close()
                      Assert.Equal(AskSpecs.CollisionSkip, form.Answer)
                  End Using
              End Sub)
    End Sub

    <Fact>
    Public Sub A_button_press_answers_with_its_own_index()
        OnSta(Sub()
                  Dim facts As New AskFileFacts(1, Nothing)
                  Using form As New Ask_Dialog(AskSpecs.NameCollision("a.jpg", facts, facts))
                      form.StartPosition = FormStartPosition.Manual
                      form.Show()
                      form.Controls.Find("ask_Button1", True).OfType(Of Button)().First().PerformClick()
                      Assert.Equal(AskSpecs.CollisionKeepBoth, form.Answer)
                  End Using
              End Sub)
    End Sub

    ''' <summary>UX-204: a refused name keeps what was typed and says why beside the field; the window stays up.</summary>
    <Fact>
    Public Sub A_refused_name_keeps_the_text_and_shows_the_reason_beside_the_field()
        OnSta(Sub()
                  Using form As New Ask_Dialog(AskSpecs.RenamePrompt("photo", ".jpg", Nothing))
                      form.StartPosition = FormStartPosition.Manual
                      form.Show()
                      Dim field As TextBox = form.Controls.Find("ask_Field", True).OfType(Of TextBox)().First()
                      field.Text = "bad/name"
                      form.Controls.Find("ask_Button0", True).OfType(Of Button)().First().PerformClick()
                      Dim problem As Label = form.Controls.Find("ask_Problem", True).OfType(Of Label)().First()
                      Assert.False(form.IsDisposed)
                      Assert.True(form.Visible, "the window stays open on a refusal")
                      Assert.Equal(-1, form.Answer)
                      Assert.Equal("bad/name", field.Text)
                      Assert.False(String.IsNullOrWhiteSpace(problem.Text))
                      ' Typing again clears the reason.
                      field.Text = "good"
                      Assert.Equal(String.Empty, problem.Text)
                      form.Controls.Find("ask_Button0", True).OfType(Of Button)().First().PerformClick()
                      Assert.Equal(0, form.Answer)
                  End Using
              End Sub)
    End Sub

    ''' <summary>Modal, the way the viewer runs it: the typed text is still there after ShowDialog returns.</summary>
    <Fact>
    Public Sub A_modal_prompt_hands_back_the_text_that_was_accepted()
        OnSta(Sub()
                  Using form As New Ask_Dialog(AskSpecs.JumpToNumberPrompt(3, 42))
                      form.StartPosition = FormStartPosition.Manual
                      AddHandler form.Shown,
                          Sub()
                              form.Controls.Find("ask_Field", True).OfType(Of TextBox)().First().Text = "17"
                              form.Controls.Find("ask_Button0", True).OfType(Of Button)().First().PerformClick()
                          End Sub
                      form.ShowDialog()
                      Assert.Equal(0, form.Answer)
                      Assert.Equal("17", form.TypedText)
                  End Using
              End Sub)
    End Sub

    <Fact>
    Public Sub Cancelling_a_prompt_is_not_an_empty_answer()
        OnSta(Sub()
                  Using form As New Ask_Dialog(AskSpecs.OpenUrlPrompt(""))
                      form.StartPosition = FormStartPosition.Manual
                      form.Show()
                      form.Controls.Find("ask_Button1", True).OfType(Of Button)().First().PerformClick()
                      Assert.Equal(1, form.Answer)
                  End Using
              End Sub)
    End Sub

End Class
#End If
