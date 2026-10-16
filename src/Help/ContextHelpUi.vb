#If Not NETFRAMEWORK Then
Option Strict On
Option Explicit On

Imports System.Collections.Generic
Imports System.Drawing
Imports System.Linq
Imports System.Runtime.CompilerServices
Imports System.Windows.Forms

''' <summary>
''' CONTEXT-HELP rules 1, 2, 4: attaches the '?' help button (hit target >= 44x44, glyph 'app.help')
''' and non-global F1 key handling to every form according to help-map.json.
''' </summary>
Public NotInheritable Class ContextHelpUi

    Private Sub New()
    End Sub

    Private Shared ReadOnly Buttons As New ConditionalWeakTable(Of Form, Button)()
    Private Shared ReadOnly Tips As New ConditionalWeakTable(Of Button, ToolTip)()

    Public Shared Function AttachedButton(form As Form) As Button
        Dim btn As Button = Nothing
        If form IsNot Nothing AndAlso Buttons.TryGetValue(form, btn) Then
            Return btn
        End If
        Return Nothing
    End Function

    Public Shared Sub RefreshLanguage(form As Form)
        If form Is Nothing Then Return
        Dim btn As Button = Nothing
        If Not Buttons.TryGetValue(form, btn) Then Return
        Dim text As String = Localization.T("Справка") & " (F1)"
        btn.AccessibleName = text
        Dim tip As ToolTip = Nothing
        If Tips.TryGetValue(btn, tip) Then
            tip.SetToolTip(btn, text)
        End If
    End Sub

    Private Shared Function CreateButton(form As Form) As Button
        Dim edge As Integer = Math.Max(44, CInt(Math.Round(44.0 * form.DeviceDpi / 96.0)))
        Dim helpText As String = Localization.T("Справка") & " (F1)"
        Dim button As New Button With {
            .Name = "contextHelp",
            .Size = New Size(edge, edge),
            .AutoSize = False,
            .TabStop = True,
            .Margin = Padding.Empty,
            .AccessibleName = helpText,
            .UseMnemonic = False,
            .FlatStyle = FlatStyle.Flat,
            .DialogResult = DialogResult.None
        }
        button.FlatAppearance.BorderSize = 0
        ButtonGlyphs.Apply(button, "app.help")
        Dim tip As New ToolTip With {.ShowAlways = True}
        Tips.Add(button, tip)
        tip.SetToolTip(button, helpText)
        AddHandler button.Disposed, Sub() tip.Dispose()
        AddHandler button.Click, Sub()
            ContextHelp.Open(form)
        End Sub
        Return button
    End Function

    Public Shared Sub Attach(form As Form)
        If form Is Nothing OrElse form.IsDisposed Then Return
        Dim existing As Button = Nothing
        If Buttons.TryGetValue(form, existing) Then Return
        ' Avoid attaching duplicate button to Main_Form which owns its custom chrome panel
        If form.GetType().Name = "Main_Form" Then Return

        Dim fullName = form.GetType().FullName
        Dim row = ContextHelp.Inventory.surfaces.FirstOrDefault(Function(s) String.Equals(s.type, fullName, StringComparison.Ordinal))
        If row Is Nothing OrElse row.form = "none" Then Return

        Dim btn = CreateButton(form)
        Buttons.Add(form, btn)

        Dim edge = btn.Width
        btn.Anchor = AnchorStyles.Top Or If(form.RightToLeft = RightToLeft.Yes, AnchorStyles.Left, AnchorStyles.Right)
        form.Controls.Add(btn)

        Dim position = Sub()
            If form.IsDisposed OrElse btn.IsDisposed Then Return
            Dim x = If(form.RightToLeft = RightToLeft.Yes, 0, Math.Max(0, form.ClientSize.Width - btn.Width))
            btn.Location = New Point(x, 0)
            btn.BringToFront()
        End Sub

        AddHandler form.ClientSizeChanged, Sub() position()
        AddHandler form.RightToLeftChanged, Sub() position()
        position()

        ' Non-global F1 hook: active form only
        form.KeyPreview = True
        AddHandler form.KeyDown, Sub(sender, e)
            If e.KeyCode = Keys.F1 AndAlso Not e.Alt AndAlso Not e.Control AndAlso Not e.Shift Then
                e.Handled = True
                e.SuppressKeyPress = True
                ContextHelp.Open(form)
            End If
        End Sub
        AddHandler form.HelpRequested, Sub(sender, hlpevent)
            hlpevent.Handled = True
            ContextHelp.Open(form)
        End Sub
    End Sub

End Class
#End If
