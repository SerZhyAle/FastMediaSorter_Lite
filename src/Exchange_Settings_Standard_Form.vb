#If Not NETFRAMEWORK Then
Option Strict On

Imports System.Collections.Generic
Imports System.Drawing
Imports System.Linq
Imports System.Windows.Forms

''' <summary>
''' The confirmation of "Save as STANDARD" and "Read STANDARD" (ticket L0296, DEVICE-EXCHANGE 1.12 items EQ and ER): what the
''' action replaces, that paths, secrets and window sizes never travel, that the server's operator can read a saved standard,
''' and one check box per settings group. The window itself sends nothing: it returns the chosen groups, and the caller holds
''' the session that speaks.
''' Built in code from 96-dpi positions; DialogLayout scales it (the last line of the builder).
''' </summary>
Friend NotInheritable Class Exchange_Settings_Standard_Form
    Inherits Form

    ''' <summary>Height at 96 dpi of the heading row that carries the exchange mark (40 px) and its gap to the first row.</summary>
    Private Const HeadingBand As Integer = 46

    Private ReadOnly heading As Label
    Private ReadOnly explanation As Label
    Private ReadOnly operatorNote As Label
    Private ReadOnly groupBoxes As New List(Of KeyValuePair(Of String, CheckBox))
    Private ReadOnly statusLabel As Label
    Private ReadOnly okButton As Button
    Private ReadOnly cancelButton_ As Button
    Private ReadOnly answerRow As DialogButtonRow
    Private script_Font As Font
    Private _chosen As IReadOnlyList(Of String) = Array.Empty(Of String)()

    ''' <summary>The group ids ticked at the moment of OK, in the program's order; empty when the window closed otherwise.</summary>
    Friend ReadOnly Property ChosenGroups As IReadOnlyList(Of String)
        Get
            Return _chosen
        End Get
    End Property

    ''' <param name="save">True for "Save as STANDARD", False for "Read STANDARD".</param>
    ''' <param name="serverName">The entry's name, in the title: the destination never changes inside the window.</param>
    Friend Sub New(save As Boolean, serverName As String)
        Me.Text = If(save, ExchangeEntryText.SaveStandardTitle(serverName), ExchangeEntryText.ReadStandardTitle(serverName))
        Me.FormBorderStyle = FormBorderStyle.FixedDialog
        Me.MaximizeBox = False
        Me.MinimizeBox = False
        Me.ShowInTaskbar = False
        Me.StartPosition = FormStartPosition.CenterParent
        ' The authored content ends at 294 (the status line under the four group rows); the bottom row below it is 50 tall.
        Me.ClientSize = New Size(420, 344)

        Dim family As String = Localization.FontFamily()
        If family IsNot Nothing Then
            script_Font = New Font(family, Me.Font.Size)
            Me.Font = script_Font
        End If
        If Localization.IsRightToLeft() Then Me.RightToLeft = RightToLeft.Yes

        explanation = New Label With {.Name = "xss_Explanation", .AutoSize = False, .Location = New Point(14, 14), .Size = New Size(392, 64),
                                      .Text = If(save, ExchangeEntryText.SaveStandardExplanation(), ExchangeEntryText.ReadStandardExplanation())}
        operatorNote = New Label With {.Name = "xss_Operator", .AutoSize = False, .Location = New Point(14, 80), .Size = New Size(392, 32),
                                       .ForeColor = SystemColors.GrayText, .Text = ExchangeEntryText.OperatorCanReadNote()}
        Dim authored As New List(Of Control) From {explanation, operatorNote}
        Dim top As Integer = 114
        For Each groupId As String In ExchangeSettingsStandard.Groups
            ' Every group is ticked at the start: the whole standard is the common case, a narrower one is the person's choice.
            Dim box As New CheckBox With {.Name = "xss_Group_" & groupId, .Text = ExchangeEntryText.GroupTitle(groupId), .Checked = True}
            groupBoxes.Add(New KeyValuePair(Of String, CheckBox)(groupId, box))
            authored.Add(DialogOptionRow.CreateAt(box, Nothing, 14, top, 420))
            top += DialogOptionRow.RowHeightLogical
        Next
        statusLabel = New Label With {.Name = "xss_Status", .AutoSize = False, .Location = New Point(14, top + 2), .Size = New Size(392, 18),
                                      .ForeColor = Color.Firebrick, .Text = ""}
        authored.Add(statusLabel)
        okButton = New Button With {.Name = "xss_Ok", .Text = If(save, ExchangeEntryText.SaveStandardConfirm(), ExchangeEntryText.ReadStandardConfirm())}
        AddHandler okButton.Click, AddressOf OnOk
        cancelButton_ = New Button With {.Name = "xss_Cancel", .Text = Localization.T("Отмена"), .DialogResult = DialogResult.Cancel}

        ' The answers ride the shared bottom row (BUTTONS 2.0 sections 3 and 4): Cancel first, the action last. Enter is the
        ' action - the window opened on the person's own press and names what it replaces - Escape is Cancel.
        answerRow = New DialogButtonRow() With {.Dock = DockStyle.Bottom, .Name = "xss_Buttons", .Padding = New Padding(14, 8, 14, 10)}
        answerRow.Add(okButton, DialogButtonRole.Affirmative, If(save, "action.upload", "action.download"))
        answerRow.Add(cancelButton_, DialogButtonRole.Dismissing, "action.cancel")

        Me.Controls.AddRange(authored.ToArray())
        ' EXCHANGE-MARK (spec 248 V4): a heading band with the mark above the authored rows, never in the title bar.
        For Each child As Control In Me.Controls
            child.Top += HeadingBand
        Next
        Me.ClientSize = New Size(Me.ClientSize.Width, Me.ClientSize.Height + HeadingBand)
        heading = New Label With {.Name = "xss_Heading", .AutoSize = False, .UseMnemonic = False,
                                  .Location = New Point(If(Me.RightToLeft = RightToLeft.Yes, 14 + ContextHelpUi.BadgeEdge, 14), 12),
                                  .Size = New Size(392 - ContextHelpUi.BadgeEdge, 40), .Text = Me.Text}
        ExchangeMark.Apply(heading, 32, AddressOf RuntimeBootstrap.OpenBundledAsset)
        Me.Controls.Add(heading)
        Me.Controls.SetChildIndex(heading, 0)
        Me.Controls.Add(answerRow)
        answerRow.Bind(Me)

        ' LAST line of the builder: scaling touches only the children that exist by now.
        DialogLayout.ApplyAutoScale(Me)
    End Sub

    Protected Overrides Sub OnLoad(e As EventArgs)
        MyBase.OnLoad(e)
        DialogLayout.FitContent(Me)
        DialogLayout.KeepRoomForRow(Me, answerRow)
    End Sub

    Protected Overrides Sub Dispose(disposing As Boolean)
        MyBase.Dispose(disposing)
        If disposing Then script_Font?.Dispose()
    End Sub

    Private Sub OnOk(sender As Object, e As EventArgs)
        Dim chosen As IReadOnlyList(Of String) = ExchangeSettingsStandard.ChosenGroups(groupBoxes.Where(Function(p) p.Value.Checked).Select(Function(p) p.Key))
        If chosen.Count = 0 Then
            statusLabel.Text = ExchangeEntryText.NoGroupChosen()
            Return
        End If
        _chosen = chosen
        DialogResult = DialogResult.OK
        Close()
    End Sub

End Class
#End If
