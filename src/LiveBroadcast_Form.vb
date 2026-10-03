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
    Private ReadOnly lblStatus As Label
    Private ReadOnly btnConnect As Button
    Private ReadOnly btnCancel As Button

    Private recentDescriptors As List(Of LiveBroadcastDescriptor)
    Private scriptFont As Font

    Public Property ResultDescriptor As LiveBroadcastDescriptor

    Public Sub New()
        Me.Text = Localization.T("Открыть live-трансляцию")
        Me.FormBorderStyle = FormBorderStyle.FixedDialog
        Me.MaximizeBox = False
        Me.MinimizeBox = False
        Me.ShowInTaskbar = False
        Me.StartPosition = FormStartPosition.CenterParent
        Me.ClientSize = New Size(540, 390)

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

        chkIsLive = New CheckBox With {
            .AutoSize = True,
            .Location = New Point(14, 68),
            .Text = Localization.T("Это live-трансляция"),
            .Checked = True
        }

        lblLanHint = New Label With {
            .AutoSize = False,
            .Location = New Point(14, 94),
            .Size = New Size(512, 28),
            .ForeColor = Color.DimGray,
            .Text = Localization.T("ПК и вещающее устройство должны находиться в одной локальной сети (Wi-Fi / Ethernet).")
        }

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

        lblStatus = New Label With {
            .AutoSize = False,
            .Location = New Point(14, 308),
            .Size = New Size(512, 34),
            .ForeColor = Color.DarkRed,
            .Text = ""
        }

        btnConnect = New Button With {
            .Location = New Point(310, 348),
            .Size = New Size(110, 30),
            .Text = Localization.T("Подключиться"),
            .DialogResult = DialogResult.None
        }
        AddHandler btnConnect.Click, AddressOf OnConnectClick

        btnCancel = New Button With {
            .Location = New Point(426, 348),
            .Size = New Size(100, 30),
            .Text = Localization.T("Отмена"),
            .DialogResult = DialogResult.Cancel
        }

        Me.Controls.AddRange(New Control() {
            lblPrompt, txtInput, btnBrowseFile, chkIsLive, lblLanHint,
            lblRecentTitle, lstRecent, lblStatus, btnConnect, btnCancel
        })

        Me.AcceptButton = btnConnect
        Me.CancelButton = btnCancel

        LoadRecentList()
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
            Dim titleText As String = If(Not String.IsNullOrEmpty(d.Title), d.Title, d.Url)
            Dim item As New ListViewItem(titleText)
            item.SubItems.Add(d.Url)
            item.Tag = d
            lstRecent.Items.Add(item)
        Next
    End Sub

    Private Sub OnBrowseFileClick(sender As Object, e As EventArgs)
        Using ofd As New OpenFileDialog()
            ofd.Filter = Localization.T("Файлы трансляций (*.fmsbcast)|*.fmsbcast|Все файлы (*.*)|*.*")
            ofd.Title = Localization.T("Выбрать файл .fmsbcast")
            If ofd.ShowDialog(Me) = DialogResult.OK Then
                Dim res As LiveBroadcastReadResult = LiveBroadcastReader.ReadFile(ofd.FileName)
                If res.Status = LiveReadStatus.Ok AndAlso res.Descriptor IsNot Nothing Then
                    ResultDescriptor = res.Descriptor
                    store.Upsert(ResultDescriptor)
                    Me.DialogResult = DialogResult.OK
                    Me.Close()
                Else
                    ShowReadError(res.Status)
                End If
            End If
        End Using
    End Sub

    Private Sub OnInputTextChanged(sender As Object, e As EventArgs)
        lblStatus.Text = ""
        Dim text As String = txtInput.Text.Trim()
        If String.IsNullOrEmpty(text) Then Return

        ' Auto-check IsLive for descriptors or recognized URLs
        If text.StartsWith("FMSBCAST1:", StringComparison.OrdinalIgnoreCase) OrElse
           text.StartsWith("intent:", StringComparison.OrdinalIgnoreCase) OrElse
           text.StartsWith("fmsbcast:", StringComparison.OrdinalIgnoreCase) OrElse
           text.StartsWith("{", StringComparison.Ordinal) Then
            chkIsLive.Checked = True
        End If
    End Sub

    Private Sub OnRecentSelectionChanged(sender As Object, e As EventArgs)
        If lstRecent.SelectedItems.Count > 0 Then
            Dim d As LiveBroadcastDescriptor = TryCast(lstRecent.SelectedItems(0).Tag, LiveBroadcastDescriptor)
            If d IsNot Nothing Then
                txtInput.Text = d.Url
                chkIsLive.Checked = True
                lblStatus.Text = ""
            End If
        End If
    End Sub

    Private Sub OnRecentDoubleClick(sender As Object, e As EventArgs)
        If lstRecent.SelectedItems.Count > 0 Then
            Dim d As LiveBroadcastDescriptor = TryCast(lstRecent.SelectedItems(0).Tag, LiveBroadcastDescriptor)
            If d IsNot Nothing Then
                ResultDescriptor = d
                store.Upsert(d)
                Me.DialogResult = DialogResult.OK
                Me.Close()
            End If
        End If
    End Sub

    Private Sub OnConnectClick(sender As Object, e As EventArgs)
        Dim text As String = txtInput.Text.Trim()
        If String.IsNullOrEmpty(text) Then
            lblStatus.Text = Localization.T("Введите адрес или код трансляции")
            Return
        End If

        ' Try reading text as descriptor / FMSBCAST1 / JSON / link
        Dim res As LiveBroadcastReadResult = LiveBroadcastReader.ReadText(text)
        If res.Status <> LiveReadStatus.Ok Then
            res = LiveBroadcastLinkReader.ReadLink(text)
        End If

        If res.Status = LiveReadStatus.Ok AndAlso res.Descriptor IsNot Nothing Then
            ResultDescriptor = res.Descriptor
            store.Upsert(ResultDescriptor)
            Me.DialogResult = DialogResult.OK
            Me.Close()
            Return
        End If

        ' If user checked "This is a live broadcast" on an arbitrary URL
        If chkIsLive.Checked AndAlso (text.StartsWith("http://", StringComparison.OrdinalIgnoreCase) OrElse text.StartsWith("https://", StringComparison.OrdinalIgnoreCase)) Then
            Dim descriptor As LiveBroadcastDescriptor = LiveBroadcastLinkReader.CreateExplicitLiveDescriptor(text)
            If descriptor IsNot Nothing Then
                ResultDescriptor = descriptor
                store.Upsert(ResultDescriptor)
                Me.DialogResult = DialogResult.OK
                Me.Close()
                Return
            End If
        End If

        ShowReadError(res.Status)
    End Sub

    Private Sub ShowReadError(status As LiveReadStatus)
        Select Case status
            Case LiveReadStatus.UnsupportedVersion
                lblStatus.Text = Localization.T("Требуется обновить приложение для этой трансляции")
            Case LiveReadStatus.UnsupportedMode
                lblStatus.Text = Localization.T("Этот режим пока не поддерживается")
            Case LiveReadStatus.LoopbackHost
                lblStatus.Text = Localization.T("Устройство передало локальный адрес (loopback), недоступный с ПК")
            Case LiveReadStatus.TooLarge
                lblStatus.Text = Localization.T("Превышен максимальный размер дескриптора (64 КБ)")
            Case LiveReadStatus.InvalidDocument
                lblStatus.Text = Localization.T("Неверный формат трансляции или повреждённые данные")
            Case Else
                lblStatus.Text = Localization.T("Не удалось распознать трансляцию")
        End Select
    End Sub

End Class
#End If
