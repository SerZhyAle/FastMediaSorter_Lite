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

    Private txtTitle As TextBox
    Private cboLifetime As ComboBox
    Private chkRecursive As CheckBox
    Private chkExternal As CheckBox
    Private lblWarning As Label

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

    Public Sub New(targetPath As String)
        _targetPath = If(targetPath, "").Trim()
        _isFolder = Directory.Exists(_targetPath)
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

        Dim settings As New ShareSettings()
        Try
            settings.Load()
        Catch
        End Try

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
            .Padding = New Padding(18, 16, 18, 12),
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
            tlp.Controls.Add(chkRecursive, 0, r)
            tlp.SetColumnSpan(chkRecursive, 2)
            r += 1
        End If

        ' External access checkbox
        chkExternal = New CheckBox With {
            .AutoSize = True,
            .Margin = New Padding(0, 6, 0, 4),
            .Text = Localization.T("Также через интернет"),
            .Checked = Not settings.LanOnlyExport
        }
        tlp.Controls.Add(chkExternal, 0, r)
        tlp.SetColumnSpan(chkExternal, 2)
        r += 1

        ' Security warning label
        lblWarning = New Label With {
            .AutoSize = True,
            .MaximumSize = New Size(420, 0),
            .ForeColor = Color.Firebrick,
            .Margin = New Padding(0, 2, 0, 8),
            .Text = Localization.T("Внимание: HTTP-трафик не шифруется. Содержимое и токен трансляции будут передаваться в открытом виде."),
            .Visible = chkExternal.Checked
        }
        AddHandler chkExternal.CheckedChanged, Sub()
                                                   lblWarning.Visible = chkExternal.Checked
                                                   ' Suggested lifetime defaults to shortest (1 hour) when internet is checked (§7.3)
                                                   If chkExternal.Checked AndAlso cboLifetime.SelectedIndex = 0 Then
                                                       cboLifetime.SelectedIndex = 1
                                                   End If
                                               End Sub
        tlp.Controls.Add(lblWarning, 0, r)
        CheckBoxLayout.BindHint(chkExternal, lblWarning)
        tlp.SetColumnSpan(lblWarning, 2)
        r += 1

        ' Button row
        Dim btnRow As New FlowLayoutPanel With {
            .AutoSize = True,
            .Anchor = AnchorStyles.Right,
            .FlowDirection = FlowDirection.LeftToRight,
            .WrapContents = False,
            .Margin = New Padding(0, 16, 0, 0)
        }
        Dim btnBroadcast As New Button With {
            .AutoSize = True,
            .AutoSizeMode = AutoSizeMode.GrowAndShrink,
            .Padding = New Padding(14, 5, 14, 5),
            .Margin = New Padding(0, 0, 8, 0),
            .Text = Localization.T("Транслировать"),
            .DialogResult = DialogResult.OK
        }
        Dim btnCancel As New Button With {
            .AutoSize = True,
            .AutoSizeMode = AutoSizeMode.GrowAndShrink,
            .Padding = New Padding(14, 5, 14, 5),
            .Text = Localization.T("Отмена"),
            .DialogResult = DialogResult.Cancel
        }
        ButtonGlyphs.Apply(btnBroadcast, "media.cast", ButtonGlyphRole.Affirmative)
        ButtonGlyphs.Apply(btnCancel, "action.cancel", ButtonGlyphRole.Dismissing)
        AddHandler btnBroadcast.Click, AddressOf OnAccept
        btnRow.Controls.AddRange(New Control() {btnBroadcast, btnCancel})
        tlp.Controls.Add(btnRow, 0, r)
        tlp.SetColumnSpan(btnRow, 2)

        Me.Controls.Add(tlp)
        Me.AcceptButton = btnBroadcast
        Me.CancelButton = btnCancel
        Me.MinimumSize = New Size(440, 0)
        DpiLayout.ApplyAutoScale(Me)
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
