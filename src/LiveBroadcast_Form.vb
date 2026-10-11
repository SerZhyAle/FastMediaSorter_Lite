#If Not NETFRAMEWORK Then
Option Strict On

Imports System.Collections.Generic
Imports System.Drawing
Imports System.IO
Imports System.Windows.Forms

' 023_SPECIFICATION_FMS_LIVE_BROADCAST_CONSUMER.md §1, §4, §5.
' Dialog to open or import live broadcast descriptors, paste links, or select recent devices.

Friend NotInheritable Class LiveBroadcast_Form
    Inherits Form

    Private ReadOnly store As New LiveBroadcastStore()
    Private ReadOnly txtInput As TextBox
    Private ReadOnly chkIsLive As CheckBox
    Private ReadOnly btnBrowseFile As Button
    Private ReadOnly lstRecent As ListView
    Private ReadOnly lblLanHint As Label
    Private ReadOnly lblRecentNote As Label
    Private ReadOnly lblStatus As Label
    Private ReadOnly btnConnect As Button
    Private ReadOnly btnCancel As Button

    Private recentDescriptors As List(Of LiveBroadcastDescriptor)
    Private selectedRecent As LiveBroadcastDescriptor
    Private scriptFont As Font

    ''' <summary>The broadcast to play. Set when the text was a descriptor, a link, a file or a marked-live address.</summary>
    <System.ComponentModel.Browsable(False), System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)>
    Public Property ResultDescriptor As LiveBroadcastDescriptor

    ''' <summary>
    ''' An address the user did NOT mark as live and that is not one of ours: it goes to the ordinary "Open URL.."
    ''' path, unchanged (section 4.3).
    ''' </summary>
    <System.ComponentModel.Browsable(False), System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)>
    Public Property ResultOnDemandUrl As String

    Public Sub New()
        Me.Text = Localization.T("Открыть live-трансляцию")
        Me.FormBorderStyle = FormBorderStyle.FixedDialog
        Me.MaximizeBox = False
        Me.MinimizeBox = False
        Me.ShowInTaskbar = False
        Me.StartPosition = FormStartPosition.CenterParent
        Me.ClientSize = New Size(540, 414)

        Dim family As String = Localization.FontFamily()
        If family IsNot Nothing Then
            scriptFont = New Font(family, Me.Font.Size)
            Me.Font = scriptFont
        End If
        If Localization.IsRightToLeft() Then Me.RightToLeft = RightToLeft.Yes

        Dim lblPrompt As New Label With {
            .AutoSize = True,
            .Location = New Point(14, 14),
            .Text = Localization.T("Вставьте код FMSBCAST1:, ссылку или URL потока:")
        }

        txtInput = New TextBox With {
            .Location = New Point(14, 36),
            .Size = New Size(400, 24)
        }
        AddHandler txtInput.TextChanged, AddressOf OnInputTextChanged

        btnBrowseFile = New Button With {
            .Location = New Point(420, 35),
            .Size = New Size(106, 26),
            .Text = Localization.T("Файл .fmsbcast..")
        }
        AddHandler btnBrowseFile.Click, AddressOf OnBrowseFileClick
        ButtonGlyphs.Apply(btnBrowseFile, "action.choose-file")

        ' Section 4.3: for an address that is not one of ours the user says it is live. Unticked, it is an ordinary URL.
        chkIsLive = New CheckBox With {
            .AutoSize = True,
            .Location = New Point(14, 68),
            .Text = Localization.T("Это live-трансляция"),
            .Checked = False
        }

        lblLanHint = New Label With {
            .AutoSize = False,
            .Location = New Point(14, 94),
            .Size = New Size(512, 28),
            .ForeColor = Color.DimGray,
            .Text = Localization.T("ПК и вещающее устройство должны находиться в одной локальной сети (Wi-Fi / Ethernet).")
        }
        CheckBoxLayout.BindHint(chkIsLive, lblLanHint)

        Dim lblRecentTitle As New Label With {
            .AutoSize = True,
            .Location = New Point(14, 128),
            .Text = Localization.T("Недавние трансляции:")
        }

        lstRecent = New ListView With {
            .Location = New Point(14, 148),
            .Size = New Size(512, 150),
            .View = View.Details,
            .FullRowSelect = True,
            .MultiSelect = False,
            .HeaderStyle = ColumnHeaderStyle.Nonclickable
        }
        lstRecent.Columns.Add(Localization.T("Устройство / Название"), 240)
        lstRecent.Columns.Add(Localization.T("Адрес"), 260)
        AddHandler lstRecent.SelectedIndexChanged, AddressOf OnRecentSelectionChanged
        AddHandler lstRecent.DoubleClick, AddressOf OnRecentDoubleClick

        ' Section 5: a remembered entry promises nothing - the device may have stopped, or moved to another port.
        lblRecentNote = New Label With {
            .AutoSize = False,
            .Location = New Point(14, 302),
            .Size = New Size(512, 30),
            .ForeColor = Color.DimGray,
            .Text = Localization.T("Адреса в списке - последние известные: устройство могло перестать вещать или сменить порт.")
        }

        lblStatus = New Label With {
            .AutoSize = False,
            .Location = New Point(14, 336),
            .Size = New Size(512, 34),
            .ForeColor = Color.DarkRed,
            .Text = ""
        }

        btnConnect = New Button With {
            .Location = New Point(310, 374),
            .Size = New Size(110, 30),
            .Text = Localization.T("Подключиться"),
            .DialogResult = DialogResult.None
        }
        AddHandler btnConnect.Click, AddressOf OnConnectClick
        ButtonGlyphs.Apply(btnConnect, "action.confirm", ButtonGlyphRole.Affirmative)

        btnCancel = New Button With {
            .Location = New Point(426, 374),
            .Size = New Size(100, 30),
            .Text = Localization.T("Отмена"),
            .DialogResult = DialogResult.Cancel
        }
        ButtonGlyphs.Apply(btnCancel, "action.cancel", ButtonGlyphRole.Dismissing)

        Me.Controls.AddRange(New Control() {
            lblPrompt, txtInput, btnBrowseFile, chkIsLive, lblLanHint,
            lblRecentTitle, lstRecent, lblRecentNote, lblStatus, btnConnect, btnCancel
        })

        Me.AcceptButton = btnConnect
        Me.CancelButton = btnCancel

        LoadRecentList()

        ' LAST line of the builder: scaling touches only the children that exist by now (124 UX-202).
        DialogLayout.ApplyAutoScale(Me)
    End Sub

    Protected Overrides Sub OnLoad(e As EventArgs)
        MyBase.OnLoad(e)
        ' WinForms never scales a ListView's column widths; they are logical 96-dpi pixels until the window has its DPI.
        lstRecent.Columns(0).Width = DialogLayout.Device(lstRecent, 240)
        lstRecent.Columns(1).Width = DialogLayout.Device(lstRecent, 260)
        DialogLayout.FitContent(Me)
    End Sub

    Protected Overrides Sub Dispose(disposing As Boolean)
        If disposing Then
            If scriptFont IsNot Nothing Then
                scriptFont.Dispose()
                scriptFont = Nothing
            End If
        End If
        MyBase.Dispose(disposing)
    End Sub

    Private Sub LoadRecentList()
        lstRecent.Items.Clear()
        recentDescriptors = store.LoadAll()
        For Each d As LiveBroadcastDescriptor In recentDescriptors
            Dim titleText As String = If(Not String.IsNullOrEmpty(d.Title), d.Title, HostOf(d.Url))
            Dim item As New ListViewItem(titleText)
            item.SubItems.Add(HostOf(d.Url))
            item.Tag = d
            lstRecent.Items.Add(item)
        Next
    End Sub

    ''' <summary>host:port - never the path or the query, which can carry a token.</summary>
    Private Shared Function HostOf(url As String) As String
        Dim parsed As Uri = Nothing
        If Uri.TryCreate(url, UriKind.Absolute, parsed) Then Return parsed.Authority
        Return url
    End Function

    ''' <summary>Takes a descriptor as the result, remembers it and closes.</summary>
    Private Sub Accept(descriptor As LiveBroadcastDescriptor)
        ResultDescriptor = descriptor
        store.Upsert(descriptor)
        Me.DialogResult = DialogResult.OK
        Me.Close()
    End Sub

    Private Sub OnBrowseFileClick(sender As Object, e As EventArgs)
        Using ofd As New OpenFileDialog()
            ofd.Filter = Localization.T("Файлы трансляций (*.fmsbcast)|*.fmsbcast|Все файлы (*.*)|*.*")
            ofd.Title = Localization.T("Выбрать файл .fmsbcast")
            If ofd.ShowDialog(Me) = DialogResult.OK Then
                Dim res As LiveBroadcastReadResult = LiveBroadcastReader.ReadFile(ofd.FileName)
                If res.Status = LiveReadStatus.Ok AndAlso res.Descriptor IsNot Nothing Then
                    Accept(res.Descriptor)
                Else
                    ShowReadError(res.Status)
                End If
            End If
        End Using
    End Sub

    ''' <summary>Reads what is typed the way the Connect button will, without side effects.</summary>
    Private Shared Function Read(text As String) As LiveBroadcastReadResult
        Dim res As LiveBroadcastReadResult = LiveBroadcastIngress.TryRead(text)
        If res IsNot Nothing Then Return res

        res = LiveBroadcastReader.ReadText(text)
        If res.Status = LiveReadStatus.NotBroadcast Then
            Dim link As LiveBroadcastReadResult = LiveBroadcastLinkReader.ReadLink(text)
            If link.Status = LiveReadStatus.Ok Then Return link
        End If
        Return res
    End Function

    Private Sub OnInputTextChanged(sender As Object, e As EventArgs)
        lblStatus.Text = ""
        Dim text As String = txtInput.Text.Trim()
        If selectedRecent IsNot Nothing AndAlso Not String.Equals(text, selectedRecent.Url, StringComparison.Ordinal) Then selectedRecent = Nothing
        If String.IsNullOrEmpty(text) Then
            chkIsLive.Enabled = True
            Return
        End If

        ' Everything that arrives through a hand-off channel, and every address of our own shape, IS live: the box
        ' has nothing left to ask (section 1 item 5). For any other address the user decides.
        If selectedRecent IsNot Nothing OrElse Read(text).Status = LiveReadStatus.Ok Then
            chkIsLive.Checked = True
            chkIsLive.Enabled = False
        Else
            chkIsLive.Enabled = True
        End If
    End Sub

    Private Sub OnRecentSelectionChanged(sender As Object, e As EventArgs)
        If lstRecent.SelectedItems.Count > 0 Then
            Dim d As LiveBroadcastDescriptor = TryCast(lstRecent.SelectedItems(0).Tag, LiveBroadcastDescriptor)
            If d IsNot Nothing Then
                txtInput.Text = d.Url
                ' After the text: setting it clears the selection of a different text.
                selectedRecent = d
                chkIsLive.Checked = True
                chkIsLive.Enabled = False
                lblStatus.Text = ""
            End If
        End If
    End Sub

    Private Sub OnRecentDoubleClick(sender As Object, e As EventArgs)
        If lstRecent.SelectedItems.Count > 0 Then
            Dim d As LiveBroadcastDescriptor = TryCast(lstRecent.SelectedItems(0).Tag, LiveBroadcastDescriptor)
            If d IsNot Nothing Then Accept(d)
        End If
    End Sub

    Private Sub OnConnectClick(sender As Object, e As EventArgs)
        Dim text As String = txtInput.Text.Trim()
        If String.IsNullOrEmpty(text) Then
            lblStatus.Text = Localization.T("Введите адрес или код трансляции")
            DialogLayout.FitLabel(lblStatus)
            Return
        End If

        ' A remembered entry plays as it was stored (title, sourceId, endpoints), not rebuilt from its address.
        If selectedRecent IsNot Nothing AndAlso String.Equals(text, selectedRecent.Url, StringComparison.Ordinal) Then
            Accept(selectedRecent)
            Return
        End If

        Dim res As LiveBroadcastReadResult = Read(text)
        If res.Status = LiveReadStatus.Ok AndAlso res.Descriptor IsNot Nothing Then
            Accept(res.Descriptor)
            Return
        End If

        ' Not a hand-off, not one of our addresses: a plain address. Marked live it plays as a live stream; not
        ' marked, it is the ordinary "Open URL.." path and nothing here changes it.
        If res.Status = LiveReadStatus.NotBroadcast AndAlso IsHttpAddress(text) Then
            If chkIsLive.Checked Then
                Dim descriptor As LiveBroadcastDescriptor = LiveBroadcastLinkReader.CreateExplicitLiveDescriptor(text)
                If descriptor IsNot Nothing Then
                    Accept(descriptor)
                    Return
                End If
            Else
                ResultOnDemandUrl = text
                Me.DialogResult = DialogResult.OK
                Me.Close()
                Return
            End If
        End If

        ShowReadError(res.Status)
    End Sub

    Private Shared Function IsHttpAddress(text As String) As Boolean
        Return text.StartsWith("http://", StringComparison.OrdinalIgnoreCase) OrElse text.StartsWith("https://", StringComparison.OrdinalIgnoreCase)
    End Function

    Private Sub ShowReadError(status As LiveReadStatus)
        lblStatus.Text = LiveBroadcastMessages.ForReadStatus(status)
        DialogLayout.FitLabel(lblStatus)
    End Sub

End Class
#End If
