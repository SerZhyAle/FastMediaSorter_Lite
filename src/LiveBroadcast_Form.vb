#If Not NETFRAMEWORK Then
Option Strict On

Imports System.Collections.Generic
Imports System.Drawing
Imports System.IO
Imports System.Windows.Forms
Imports System.Threading

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
    Private ReadOnly answerRow As DialogButtonRow

    Private recentDescriptors As List(Of LiveBroadcastDescriptor)
    Private selectedRecent As LiveBroadcastDescriptor
    Private scriptFont As Font
    Private ReadOnly directoryCancellation As New CancellationTokenSource()
    Private ReadOnly directoryIds As New Dictionary(Of LiveBroadcastDescriptor, String)()
    ''' <summary>Broadcast ids of refused directory records already written to the log: the list is rebuilt on every revision, the line is not.</summary>
    Private ReadOnly refusalLogged As New HashSet(Of String)()
    Private directoryStarted As Boolean
    Private accepting As Boolean
    Private ReadOnly autoCast As CheckBox

    Friend ReadOnly Property AutoAcceptCast As Boolean
        Get
            Return autoCast.Checked
        End Get
    End Property

    ''' <summary>The broadcast to play. Set when the text was a descriptor, a link, a file or a marked-live address.</summary>
    <System.ComponentModel.Browsable(False), System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)>
    Public Property ResultDescriptor As LiveBroadcastDescriptor

    ''' <summary>
    ''' An address the user did NOT mark as live and that is not one of ours: it goes to the ordinary "Open URL.."
    ''' path, unchanged (section 4.3).
    ''' </summary>
    <System.ComponentModel.Browsable(False), System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)>
    Public Property ResultOnDemandUrl As String

    Public Sub New(Optional autoAcceptCast As Boolean = False)
        Me.Text = Localization.T("Открыть live-трансляцию")
        Me.FormBorderStyle = FormBorderStyle.FixedDialog
        Me.MaximizeBox = False
        Me.MinimizeBox = False
        Me.ShowInTaskbar = False
        Me.StartPosition = FormStartPosition.CenterParent
        ' With the exchange on, the automatic-accept option takes a row of its own under "this is live" (and the LAN hint steps
        ' aside, as before), so everything under the options moves down by that row; otherwise the LAN hint is the explanation of
        ' "this is live". Both rows are 40 px tall at 96 dpi (ticket 243 T-11b, BOOLEAN-OPTION 2.0).
        Dim exchangeOn As Boolean = ExchangeFeatures.IsEnabled()
        Dim shift As Integer = If(exchangeOn, 24, 0)
        ' The authored content ends at 370 (the status line); the bottom row below it is 50 tall (8 + one 32 line + 10) at 96 dpi.
        Me.ClientSize = New Size(540, 420 + shift)

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
            .Name = "live_IsLive",
            .Text = Localization.T("Это live-трансляция"),
            .Checked = False
        }

        lblLanHint = New Label With {
            .Name = "live_LanHint",
            .AutoSize = False,
            .Size = New Size(512, 28),
            .ForeColor = Color.DimGray,
            .Text = Localization.T("ПК и вещающее устройство должны находиться в одной локальной сети (Wi-Fi / Ethernet).")
        }
        ' The row ends left of the Paste button (x 420), so the two hit areas never overlap. The hint is its explanation (it
        ' wraps under the caption and never toggles); with the exchange on the hint is not shown at all, as it never was.
        Dim liveRow As OptionRowHost = DialogOptionRow.CreateAt(chkIsLive, If(exchangeOn, Nothing, lblLanHint), 14, 62, 414, If(exchangeOn, 40, 64))
        If exchangeOn Then lblLanHint.Visible = False

        Dim lblRecentTitle As New Label With {
            .AutoSize = True,
            .Location = New Point(14, 128 + shift),
            .Text = Localization.T("Недавние трансляции:")
        }

        lstRecent = New ListView With {
            .Location = New Point(14, 148 + shift),
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
            .Location = New Point(14, 302 + shift),
            .Size = New Size(512, 30),
            .ForeColor = Color.DimGray,
            .Text = Localization.T("Адреса в списке - последние известные: устройство могло перестать вещать или сменить порт.")
        }

        lblStatus = New Label With {
            .AutoSize = False,
            .Location = New Point(14, 336 + shift),
            .Size = New Size(512, 34),
            .ForeColor = Color.DarkRed,
            .Text = ""
        }

        btnConnect = New Button With {
            .Text = Localization.T("Подключиться"),
            .DialogResult = DialogResult.None
        }
        AddHandler btnConnect.Click, AddressOf OnConnectClick

        btnCancel = New Button With {
            .Text = Localization.T("Отмена"),
            .DialogResult = DialogResult.Cancel
        }

        ' The answers ride the shared bottom row (ticket 243 T-8b, BUTTONS 2.0 sections 3 and 4): Cancel first, the final
        ' affirmative answer last, widths from the caption and its glyph. Bind keeps the meaning they had: Escape is
        ' Cancel, Enter is Connect (it only starts the connection the typed text names). The file / paste / devices
        ' buttons are helpers of the input and the list, not answers, and stay beside them.
        answerRow = New DialogButtonRow() With {.Dock = DockStyle.Bottom, .Name = "live_Buttons", .Padding = New Padding(14, 8, 14, 10)}
        answerRow.Add(btnConnect, DialogButtonRole.Affirmative, "action.confirm")
        answerRow.Add(btnCancel, DialogButtonRole.Dismissing, "action.cancel")

        Me.Controls.AddRange(New Control() {
            lblPrompt, txtInput, btnBrowseFile, liveRow,
            lblRecentTitle, lstRecent, lblRecentNote, lblStatus
        })

        LoadRecentList()

        autoCast = New CheckBox With {.Name = "live_AutoCast", .Text = Localization.T("Принимать трансляции автоматически"), .Checked = autoAcceptCast,
                                     .Visible = exchangeOn}
        Dim autoCastRow As OptionRowHost = DialogOptionRow.CreateAt(autoCast, Nothing, 14, 102, 540)
        autoCastRow.Visible = exchangeOn
        Me.Controls.Add(autoCastRow)

        Dim paste As New Button With {.Text = Localization.T("Вставить"), .Location = New Point(420, 66), .Size = New Size(106, 26)}
        AddHandler paste.Click, Sub()
                                   Dim qr As QrDecodeResult = QrCodeDecoder.DecodeClipboard()
                                   If qr.Succeeded Then
                                       txtInput.Text = qr.Text
                                   Else
                                       lblStatus.Text = qr.FailureMessage
                                   End If
                               End Sub
        Me.Controls.Add(paste)

        If ExchangeFeatures.IsEnabled() Then
            Dim devices As New Button With {.Text = Localization.T("Мои устройства"), .Location = New Point(310, 120 + shift), .Size = New Size(216, 26)}
            AddHandler devices.Click, Sub()
                                          lblRecentTitle.Text = Localization.T("Мои устройства")
                                          lblRecentNote.Text = ""
                                          LoadDevicesAsync()
                                      End Sub
            Me.Controls.Add(devices)
        End If

        ' Added after every field, so the answers come last in the tab order.
        Me.Controls.Add(answerRow)
        answerRow.Bind(Me)

        ' LAST line of the builder: scaling touches only the children that exist by now (124 UX-202).
        DialogLayout.ApplyAutoScale(Me)
    End Sub

    Protected Overrides Sub OnLoad(e As EventArgs)
        MyBase.OnLoad(e)
        ' WinForms never scales a ListView's column widths; they are logical 96-dpi pixels until the window has its DPI.
        lstRecent.Columns(0).Width = DialogLayout.Device(lstRecent, 240)
        lstRecent.Columns(1).Width = DialogLayout.Device(lstRecent, 260)
        DialogLayout.FitContent(Me)
        DialogLayout.KeepRoomForRow(Me, answerRow)
    End Sub

    Protected Overrides Sub Dispose(disposing As Boolean)
        If disposing Then
            directoryCancellation.Cancel()
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
    Private Async Sub Accept(descriptor As LiveBroadcastDescriptor)
        If accepting Then Return
        accepting = True
        Dim broadcastId As String = Nothing
        If directoryIds.TryGetValue(descriptor, broadcastId) Then
            Dim client As New ExchangeAgentClient()
            Dim reply As ExchangeAgentReply = Await client.SendAsync(ExchangeAgentPipe.BuildRequest(ExchangeAgentPipe.RequestGetBroadcast,
                Sub(o) o("broadcastId") = broadcastId), 4000, directoryCancellation.Token)
            If IsDisposed Then Return
            If Not reply.IsOk Then
                accepting = False
                ShowReadError(LiveReadStatus.NotBroadcast)
                Return
            End If
            Dim parsed As LiveBroadcastReadResult = LiveBroadcastRecords.ReadRecord(reply.DataJson)
            If parsed.Status <> LiveReadStatus.Ok Then
                accepting = False
                ShowReadError(parsed.Status)
                Return
            End If
            descriptor = parsed.Descriptor
            Dim sealError As String = Await LiveBroadcastRecords.PrepareAsync(descriptor, directoryCancellation.Token)
            If IsDisposed Then Return
            If sealError.Length > 0 Then
                accepting = False
                lblStatus.Text = ConnectionDiagnosisLocalization.GetMessageText(ExchangeAgentPipe.ErrorMessage(sealError).Code)
                DialogLayout.FitLabel(lblStatus)
                Return
            End If
        End If
        ResultDescriptor = descriptor
        store.Upsert(descriptor)
        Me.DialogResult = DialogResult.OK
        Me.Close()
    End Sub

    Private Async Sub LoadDevicesAsync()
        If directoryStarted Then Return
        directoryStarted = True
        Dim client As New ExchangeAgentClient()
        Dim revision As Long = -1
        Try
            While Not directoryCancellation.IsCancellationRequested
                Dim result As ExchangeAgentDirectoryResult = Await client.GetDirectoryAsync(revision, If(revision < 0, 0, 20000), directoryCancellation.Token, liveBroadcasts:=True)
                If IsDisposed OrElse directoryCancellation.IsCancellationRequested Then Return
                If Not result.Reply.IsOk OrElse result.Snapshot Is Nothing Then
                    lblStatus.Text = Localization.T("Подключение..")
                    Return
                End If
                revision = result.Snapshot.Revision
                Dim selectedId As String = Nothing
                If selectedRecent IsNot Nothing Then directoryIds.TryGetValue(selectedRecent, selectedId)
                lstRecent.BeginUpdate()
                lstRecent.Items.Clear()
                lstRecent.Groups.Clear()
                directoryIds.Clear()
                For Each device As ExchangeDeviceRecord In result.Snapshot.Devices
                    lstRecent.Groups.Add(device.DeviceId, device.DeviceName)
                Next
                Dim broadcasts As IReadOnlyList(Of ExchangeBroadcastRecord) = result.Snapshot.Broadcasts
                Dim rows As New List(Of LiveDirectoryRow)(broadcasts.Count)
                For Each record As ExchangeBroadcastRecord In broadcasts
                    rows.Add(LiveBroadcastRecords.RowOf(record.Raw))
                Next
                ' LIVE-BROADCAST item G as DEVICE-EXCHANGE item BP resolves it: two records of one sourceId are one channel, the later startedAt.
                Dim superseded As HashSet(Of Integer) = LiveBroadcastRecords.SupersededIndexes(rows)
                For i As Integer = 0 To broadcasts.Count - 1
                    If superseded.Contains(i) Then Continue For
                    Dim record As ExchangeBroadcastRecord = broadcasts(i)
                    Dim row As LiveDirectoryRow = rows(i)
                    If Not row.IsPlayable Then
                        ' LIVE-BROADCAST 1.1: a record this program cannot use is listed with the reason, never dropped without a word.
                        ' It is not remembered in directoryIds, so it can be neither selected for playing nor accepted.
                        Dim refused As New ListViewItem(If(String.IsNullOrWhiteSpace(record.Title), row.RefusalText, record.Title)) With {
                            .Tag = row, .ForeColor = Color.DimGray, .Group = lstRecent.Groups(record.DeviceId)}
                        refused.SubItems.Add(row.RefusalText)
                        lstRecent.Items.Add(refused)
                        If refusalLogged.Add(record.BroadcastId) Then LiveBroadcastMessages.LogRefusal(row.Status, "the directory list")
                        Continue For
                    End If
                    Dim d As LiveBroadcastDescriptor = row.Descriptor
                    directoryIds(d) = record.BroadcastId
                    Dim item As New ListViewItem(If(d.Title, record.Title)) With {.Tag = d, .Group = lstRecent.Groups(record.DeviceId)}
                    item.SubItems.Add(HostOf(d.Url))
                    lstRecent.Items.Add(item)
                    If selectedId IsNot Nothing AndAlso record.BroadcastId = selectedId Then
                        selectedRecent = d
                        item.Selected = True
                    End If
                Next
                If selectedId IsNot Nothing AndAlso Not directoryIds.ContainsKey(selectedRecent) Then
                    selectedRecent = Nothing
                    txtInput.Clear()
                End If
                lstRecent.EndUpdate()
            End While
        Catch ex As OperationCanceledException
        Finally
            directoryStarted = False
        End Try
    End Sub

    Private Sub OnBrowseFileClick(sender As Object, e As EventArgs)
        Using ofd As New OpenFileDialog()
            ofd.Filter = Localization.T("Файлы трансляций (*.fmsbcast)|*.fmsbcast|Все файлы (*.*)|*.*")
            ofd.Title = Localization.T("Выбрать файл .fmsbcast")
            If ofd.ShowDialog(Me) = DialogResult.OK Then
                Dim res As LiveBroadcastReadResult
                If PhoneConnectionIntake.IsImageFile(ofd.FileName) Then
                    Dim qr As QrDecodeResult = QrCodeDecoder.DecodeFile(ofd.FileName)
                    If Not qr.Succeeded Then
                        lblStatus.Text = qr.FailureMessage
                        Return
                    End If
                    res = Read(qr.Text)
                Else
                    res = LiveBroadcastReader.ReadFile(ofd.FileName)
                End If
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
            Else
                ShowRefusedRow(TryCast(lstRecent.SelectedItems(0).Tag, LiveDirectoryRow))
            End If
        End If
    End Sub

    ''' <summary>A refused directory record says why when it is chosen; it is never played and never fills the address box.</summary>
    Private Sub ShowRefusedRow(row As LiveDirectoryRow)
        If row Is Nothing OrElse row.IsPlayable Then Return
        ' The address box still holds the previous list choice; Connect must not play that one while this row is the highlighted one.
        If selectedRecent IsNot Nothing Then
            selectedRecent = Nothing
            txtInput.Clear()
        End If
        lblStatus.Text = row.RefusalText
        DialogLayout.FitLabel(lblStatus)
    End Sub

    Private Sub OnRecentDoubleClick(sender As Object, e As EventArgs)
        If lstRecent.SelectedItems.Count > 0 Then
            Dim d As LiveBroadcastDescriptor = TryCast(lstRecent.SelectedItems(0).Tag, LiveBroadcastDescriptor)
            If d IsNot Nothing Then
                Accept(d)
            Else
                ShowRefusedRow(TryCast(lstRecent.SelectedItems(0).Tag, LiveDirectoryRow))
            End If
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
