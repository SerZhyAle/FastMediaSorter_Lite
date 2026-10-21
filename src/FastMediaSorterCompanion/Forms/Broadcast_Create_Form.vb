Option Strict On

Imports System.ComponentModel
Imports System.Drawing
Imports System.IO
Imports System.Windows.Forms

''' <summary>
''' Dialog for creating a new StreamsPlayer broadcast from a file or folder path.
''' </summary>
Public NotInheritable Class Broadcast_Create_Form
    Inherits Form

    Private _iconHandle As IntPtr
    Private ReadOnly _targetPath As String
    Private ReadOnly _isFolder As Boolean
    Private ReadOnly _settings As ShareSettings

    Private txtTitle As TextBox
    Private cboLifetime As ComboBox
    Private chkRecursive As CheckBox
    Private chkExternal As CheckBox
    Private lblWarning As Label
    Private _answerRow As DialogButtonRow

    <DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)>
    Public Property BroadcastTitle As String = ""
    <DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)>
    Public Property TtlSeconds As Long = 86400
    <DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)>
    Public Property Recursive As Boolean = False
    <DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)>
    Public Property IncludeExternal As Boolean = False
    <DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)>
    Public ReadOnly Property IsFolderKind As Boolean
        Get
            Return _isFolder
        End Get
    End Property
    <DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)>
    Public ReadOnly Property TargetPath As String
        Get
            Return _targetPath
        End Get
    End Property

    Public Sub New(targetPath As String, Optional settings As ShareSettings = Nothing)
        _targetPath = If(targetPath, "").Trim()
        _isFolder = Directory.Exists(_targetPath)
        _settings = settings
        BuildUi()
    End Sub

    Private Sub BuildUi()
        UiLanguage.ApplyTo(Me)
        Me.Text = Localization.T("Создать трансляцию")
        Me.Icon = ShareIcons.CreateIcon(_iconHandle)
        AddHandler Me.FormClosed, Sub() ShareIcons.FreeIcon(Me.Icon, _iconHandle)
        Me.FormBorderStyle = FormBorderStyle.FixedDialog
        Me.MaximizeBox = False
        Me.MinimizeBox = False
        Me.ShowInTaskbar = False
        Me.StartPosition = FormStartPosition.CenterParent
        Me.AutoSize = True
        Me.AutoSizeMode = AutoSizeMode.GrowAndShrink

        Dim settings As ShareSettings = _settings
        If settings Is Nothing Then
            settings = New ShareSettings()
            settings.Load()
        End If

        Dim defaultTitle As String = ""
        Try
            If _isFolder Then
                defaultTitle = Path.GetFileName(_targetPath.TrimEnd("\"c, "/"c))
            Else
                defaultTitle = Path.GetFileNameWithoutExtension(_targetPath)
            End If
        Catch
        End Try
        If String.IsNullOrEmpty(defaultTitle) Then defaultTitle = "Broadcast"

        Dim tlp As New TableLayoutPanel With {
            .Dock = DockStyle.Fill,
            .AutoSize = True,
            .AutoSizeMode = AutoSizeMode.GrowAndShrink,
            .ColumnCount = 2,
            .Padding = New Padding(18, 16, 18, 6),
            .GrowStyle = TableLayoutPanelGrowStyle.AddRows
        }
        tlp.ColumnStyles.Add(New ColumnStyle(SizeType.AutoSize))
        tlp.ColumnStyles.Add(New ColumnStyle(SizeType.Percent, 100.0F))

        Dim r As Integer = 0

        ' Path info row
        tlp.Controls.Add(New Label With {
            .AutoSize = True,
            .Anchor = AnchorStyles.Left,
            .Margin = New Padding(0, 6, 16, 6),
            .ForeColor = Color.DimGray,
            .Text = If(_isFolder, Localization.T("Папка:"), Localization.T("Файл:"))
        }, 0, r)

        Dim lblPath As New Label With {
            .AutoSize = True,
            .Anchor = AnchorStyles.Left,
            .MaximumSize = New Size(360, 0),
            .Margin = New Padding(0, 6, 0, 6),
            .Text = _targetPath
        }
        tlp.Controls.Add(lblPath, 1, r)
        r += 1

        ' Title row
        tlp.Controls.Add(New Label With {
            .AutoSize = True,
            .Anchor = AnchorStyles.Left,
            .Margin = New Padding(0, 6, 16, 6),
            .ForeColor = Color.DimGray,
            .Text = Localization.T("Название канала:")
        }, 0, r)

        txtTitle = New TextBox With {
            .Dock = DockStyle.Fill,
            .Margin = New Padding(0, 4, 0, 6),
            .Text = defaultTitle,
            .Width = 320
        }
        tlp.Controls.Add(txtTitle, 1, r)
        r += 1

        ' Lifetime row
        tlp.Controls.Add(New Label With {
            .AutoSize = True,
            .Anchor = AnchorStyles.Left,
            .Margin = New Padding(0, 6, 16, 6),
            .ForeColor = Color.DimGray,
            .Text = Localization.T("Срок действия:")
        }, 0, r)

        cboLifetime = New ComboBox With {
            .DropDownStyle = ComboBoxStyle.DropDownList,
            .Dock = DockStyle.Fill,
            .Margin = New Padding(0, 4, 0, 6)
        }
        cboLifetime.Items.Add(Localization.T("24 часа"))
        cboLifetime.Items.Add(Localization.T("1 час"))
        cboLifetime.Items.Add(Localization.T("7 дней"))
        cboLifetime.Items.Add(Localization.T("До отзыва"))
        cboLifetime.SelectedIndex = 0
        tlp.Controls.Add(cboLifetime, 1, r)
        r += 1

        ' Recursive checkbox (for folders)
        If _isFolder Then
            chkRecursive = New CheckBox With {
                .AutoSize = True,
                .Margin = New Padding(0, 6, 0, 6),
                .Text = Localization.T("Включая подпапки"),
                .Checked = False
            }
            ' Each option is a boolean row (BOOLEAN-OPTION 2.0, ticket 243 T-11c-b2a): the check box keeps its field, handlers and
            ' place in the Tab order; the row around it is what the table holds.
            Dim recursiveRow As OptionRowHost = OptionRows.InFlow(chkRecursive)
            tlp.Controls.Add(recursiveRow, 0, r)
            tlp.SetColumnSpan(recursiveRow, 2)
            r += 1
        End If

        ' External access checkbox
        chkExternal = New CheckBox With {
            .AutoSize = True,
            .Margin = New Padding(0, 6, 0, 4),
            .Text = Localization.T("Также через интернет"),
            .Checked = Not settings.LanOnlyExport
        }

        ' The security warning is the EXPLANATION of the internet option (BOOLEAN-OPTION 2.0 section 2): it stands under the
        ' caption, inside the row, only while the option is on (an empty explanation takes no room), as it appeared before.
        Dim warningText As String = Localization.T("Внимание: HTTP-трафик не шифруется. Содержимое и токен трансляции будут передаваться в открытом виде.")
        lblWarning = New Label With {
            .AutoSize = True,
            .ForeColor = Color.Firebrick,
            .Text = If(chkExternal.Checked, warningText, "")
        }
        Dim externalRow As OptionRowHost = OptionRows.InFlow(chkExternal, lblWarning)
        AddHandler chkExternal.CheckedChanged, Sub()
                                                   externalRow.SetExplanation(If(chkExternal.Checked, warningText, ""))
                                                   ' Suggested lifetime defaults to shortest (1 hour) when internet is checked (§7.3)
                                                   If chkExternal.Checked AndAlso cboLifetime.SelectedIndex = 0 Then
                                                       cboLifetime.SelectedIndex = 1
                                                   End If
                                               End Sub
        If chkExternal.Checked Then cboLifetime.SelectedIndex = 1
        tlp.Controls.Add(externalRow, 0, r)
        tlp.SetColumnSpan(externalRow, 2)
        r += 1

        ' The answers ride the shared bottom row (ticket 243 T-8b, BUTTONS 2.0 sections 3 and 4): Cancel first, Broadcast
        ' last, widths from the caption and its glyph. Bind keeps their meaning: Enter broadcasts (nothing is sent before
        ' this window is answered, and the warning above says what an internet broadcast exposes), Escape cancels.
        Dim btnBroadcast As New Button With {
            .Name = "xbc_Ok",
            .AutoSize = True,
            .Text = Localization.T("Транслировать"),
            .DialogResult = DialogResult.OK
        }
        Dim btnCancel As New Button With {
            .Name = "xbc_Cancel",
            .AutoSize = True,
            .Text = Localization.T("Отмена"),
            .DialogResult = DialogResult.Cancel
        }
        AddHandler btnBroadcast.Click, AddressOf OnAccept
        _answerRow = New DialogButtonRow() With {.Dock = DockStyle.Bottom, .Name = "xbc_Buttons", .Padding = New Padding(18, 4, 18, 12)}
        DpiLayout.AddAnswer(_answerRow, btnBroadcast, DialogButtonRole.Affirmative, "media.cast")
        DpiLayout.AddAnswer(_answerRow, btnCancel, DialogButtonRole.Dismissing, "action.cancel")

        Me.Controls.Add(tlp)
        ' Last in the collection: the docked row takes its edge first and reads after the fields in the Tab order.
        Me.Controls.Add(_answerRow)
        _answerRow.Bind(Me)
        Me.MinimumSize = New Size(440, 0)
        DpiLayout.ApplyAutoScale(Me)
    End Sub

    Protected Overrides Sub OnLoad(e As EventArgs)
        MyBase.OnLoad(e)
        ' The boolean rows are laid out now that the window has its scale (they are as tall as caption and explanation need).
        OptionRows.ArrangeAll(Me)
        DpiLayout.ClampToWorkingArea(Me)
    End Sub

    Private Sub OnAccept(sender As Object, e As EventArgs)
        BroadcastTitle = txtTitle.Text.Trim()
        Select Case cboLifetime.SelectedIndex
            Case 0 : TtlSeconds = 86400
            Case 1 : TtlSeconds = 3600
            Case 2 : TtlSeconds = 604800
            Case 3 : TtlSeconds = 0
            Case Else : TtlSeconds = 86400
        End Select
        Recursive = chkRecursive IsNot Nothing AndAlso chkRecursive.Checked
        IncludeExternal = chkExternal.Checked
        Me.DialogResult = DialogResult.OK
        Me.Close()
    End Sub

End Class
