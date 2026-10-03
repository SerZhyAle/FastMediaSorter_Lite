#If Not NETFRAMEWORK Then
Option Strict On

Imports System.Collections.Generic
Imports System.Drawing
Imports System.Globalization
Imports System.IO
Imports System.Linq
Imports System.Threading
Imports System.Threading.Tasks
Imports System.Windows.Forms

''' <summary>
''' The resources the user has set up (007 §3.2): servers and network shares that are not a drive letter,
''' each opened like an ordinary folder. A small dialog, not a program of its own - list, add, change, remove,
''' test, import. Nothing is opened from here: choosing "Open" hands the resource back to the viewer, which
''' owns the connection, the trust prompt and the folder it becomes.
'''
''' Everything it stores goes through <see cref="ResourceCommit"/> (the credential first, the registry
''' reference last), so no order of clicks and no failure part-way leaves a list entry without its password.
''' </summary>
Friend NotInheritable Class Resource_Manager_Form
    Inherits Form

    Private ReadOnly list As ListView
    Private ReadOnly hint As Label
    Private ReadOnly status As Label
    Private ReadOnly openButton As Button
    Private ReadOnly addButton As Button
    Private ReadOnly editButton As Button
    Private ReadOnly removeButton As Button
    Private ReadOnly testButton As Button
    Private ReadOnly importFileButton As Button
    Private ReadOnly importClipboardButton As Button
    Private ReadOnly closeButton As Button
    Private ReadOnly secrets As IResourceSecretStore = New RegistryResourceSecretStore()
    Private ReadOnly removed As New List(Of String)()
    Private script_Font As Font
    Private testCancel As CancellationTokenSource

    ''' <summary>The resource the user chose to open, or Nothing.</summary>
    <System.ComponentModel.Browsable(False), System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)>
    Friend Property ToOpen As ResourceDescriptor

    ''' <summary>Ids removed while the dialog was open, so the viewer can close one that is on screen.</summary>
    Friend ReadOnly Property RemovedIds As IReadOnlyList(Of String)
        Get
            Return removed
        End Get
    End Property

    ''' <param name="draft">An address the user typed that matches no saved resource: the dialog opens the
    ''' "new resource" form with it already filled in.</param>
    Friend Sub New(Optional draft As ResourceDescriptor = Nothing)
        Me.Text = Localization.T("Ресурсы")
        Me.FormBorderStyle = FormBorderStyle.Sizable
        Me.MinimumSize = New Size(640, 340)
        Me.MaximizeBox = False
        Me.MinimizeBox = False
        Me.ShowInTaskbar = False
        Me.StartPosition = FormStartPosition.CenterParent
        Me.ClientSize = New Size(700, 372)

        Dim family As String = Localization.FontFamily()
        If family IsNot Nothing Then
            script_Font = New Font(family, Me.Font.Size)
            Me.Font = script_Font
        End If
        If Localization.IsRightToLeft() Then Me.RightToLeft = RightToLeft.Yes

        list = New ListView With {
            .Name = "res_List", .View = View.Details, .FullRowSelect = True, .MultiSelect = False, .HideSelection = False,
            .Location = New Point(12, 12), .Size = New Size(676, 214),
            .Anchor = AnchorStyles.Top Or AnchorStyles.Bottom Or AnchorStyles.Left Or AnchorStyles.Right}
        list.Columns.Add(Localization.T("Название"), 150)
        list.Columns.Add(Localization.T("Тип"), 120)
        list.Columns.Add(Localization.T("Адрес"), 210)
        list.Columns.Add(Localization.T("Доступ"), 90)
        list.Columns.Add(Localization.T("Открывался"), 96)
        AddHandler list.SelectedIndexChanged, Sub() UpdateButtons()
        AddHandler list.DoubleClick, Sub() OnOpen()

        hint = New Label With {
            .Name = "res_Hint", .AutoSize = False, .Location = New Point(24, 60), .Size = New Size(652, 80),
            .TextAlign = ContentAlignment.TopCenter, .ForeColor = SystemColors.GrayText,
            .Text = Localization.T("Здесь пока нет ресурсов. Добавьте сервер вручную или импортируйте файл настроек из Share Manager (.fmscfg)."),
            .Anchor = AnchorStyles.Top Or AnchorStyles.Left Or AnchorStyles.Right}

        status = New Label With {
            .Name = "res_Status", .AutoSize = False, .Location = New Point(12, 232), .Size = New Size(676, 34),
            .Anchor = AnchorStyles.Bottom Or AnchorStyles.Left Or AnchorStyles.Right}

        openButton = MakeButton("res_Open", Localization.T("Открыть"), 12, 272, AddressOf OnOpen)
        addButton = MakeButton("res_Add", Localization.T("Добавить.."), 104, 272, AddressOf OnAdd)
        editButton = MakeButton("res_Edit", Localization.T("Изменить.."), 196, 272, AddressOf OnEdit)
        removeButton = MakeButton("res_Remove", Localization.T("Удалить"), 288, 272, AddressOf OnRemove)
        testButton = MakeButton("res_Test", Localization.T("Проверить"), 380, 272, AddressOf OnTest)
        importFileButton = MakeButton("res_ImportFile", Localization.T("Импорт из файла.."), 12, 308, AddressOf OnImportFile)
        importFileButton.Size = New Size(150, 28)
        importClipboardButton = MakeButton("res_ImportClipboard", Localization.T("Импорт из буфера"), 170, 308, AddressOf OnImportClipboard)
        importClipboardButton.Size = New Size(150, 28)
        closeButton = New Button With {.Name = "res_Close", .Text = Localization.T("Закрыть"), .Size = New Size(92, 28),
                                       .Location = New Point(596, 330), .DialogResult = DialogResult.Cancel,
                                       .Anchor = AnchorStyles.Bottom Or AnchorStyles.Right}
        For Each b As Button In {openButton, addButton, editButton, removeButton, testButton, importFileButton, importClipboardButton}
            b.Anchor = AnchorStyles.Bottom Or AnchorStyles.Left
        Next

        Me.Controls.AddRange(New Control() {list, hint, status, openButton, addButton, editButton, removeButton, testButton,
                                            importFileButton, importClipboardButton, closeButton})
        hint.BringToFront()
        Me.CancelButton = closeButton
        Refill()
        Me.pendingDraft = draft
    End Sub

    Private pendingDraft As ResourceDescriptor

    Private Function MakeButton(controlName As String, text As String, x As Integer, y As Integer, handler As EventHandler) As Button
        Dim b As New Button With {.Name = controlName, .Text = text, .Location = New Point(x, y), .Size = New Size(86, 28)}
        AddHandler b.Click, handler
        Return b
    End Function

    Protected Overrides Sub OnShown(e As EventArgs)
        MyBase.OnShown(e)
        If pendingDraft IsNot Nothing Then
            Dim draft As ResourceDescriptor = pendingDraft
            pendingDraft = Nothing
            AddOrEdit(Nothing, draft)
        End If
    End Sub

    Protected Overrides Sub OnFormClosed(e As FormClosedEventArgs)
        testCancel?.Cancel()
        MyBase.OnFormClosed(e)
    End Sub

    Protected Overrides Sub Dispose(disposing As Boolean)
        MyBase.Dispose(disposing)
        If disposing Then
            testCancel?.Dispose()
            script_Font?.Dispose()
        End If
    End Sub

    ' ---------------------------------------------------------------------------- list ----

    Private Sub Refill(Optional select_Id As String = Nothing)
        list.BeginUpdate()
        Try
            list.Items.Clear()
            ' The most recently used first: the one opened an hour ago is the one wanted again.
            Dim ordered As IEnumerable(Of ResourceDescriptor) = ResourceRegistry.Load().
                OrderByDescending(Function(r) If(r.LastUsedUtc, DateTime.MinValue)).
                ThenBy(Function(r) r.DisplayName, StringComparer.CurrentCultureIgnoreCase)
            For Each r As ResourceDescriptor In ordered
                Dim item As New ListViewItem(r.DisplayName) With {.Tag = r}
                item.SubItems.Add(ResourceMessages.KindLabel(r.Kind))
                item.SubItems.Add(ResourceMessages.DisplayAddress(r))
                item.SubItems.Add(If(r.ReadOnlyHint, Localization.T("только чтение"), Localization.T("чтение и запись")))
                item.SubItems.Add(If(r.LastUsedUtc.HasValue, r.LastUsedUtc.Value.ToLocalTime().ToString("g", CultureInfo.CurrentCulture), ""))
                list.Items.Add(item)
                If select_Id IsNot Nothing AndAlso String.Equals(r.Id, select_Id, StringComparison.OrdinalIgnoreCase) Then item.Selected = True
            Next
            If list.SelectedItems.Count = 0 AndAlso list.Items.Count > 0 Then list.Items(0).Selected = True
        Finally
            list.EndUpdate()
        End Try
        hint.Visible = list.Items.Count = 0
        UpdateButtons()
    End Sub

    Private Function Selected() As ResourceDescriptor
        Return If(list.SelectedItems.Count = 0, Nothing, DirectCast(list.SelectedItems(0).Tag, ResourceDescriptor))
    End Function

    Private Sub UpdateButtons()
        Dim has As Boolean = Selected() IsNot Nothing
        openButton.Enabled = has
        editButton.Enabled = has
        removeButton.Enabled = has
        testButton.Enabled = has
    End Sub

    Private Sub Say(text As String)
        status.Text = text
    End Sub

    ' -------------------------------------------------------------------------- actions ----

    Private Sub OnOpen(Optional sender As Object = Nothing, Optional e As EventArgs = Nothing)
        Dim chosen As ResourceDescriptor = Selected()
        If chosen Is Nothing Then Return
        ToOpen = chosen
        Me.DialogResult = DialogResult.OK
        Me.Close()
    End Sub

    Private Sub OnAdd(sender As Object, e As EventArgs)
        AddOrEdit(Nothing, Nothing)
    End Sub

    Private Sub OnEdit(sender As Object, e As EventArgs)
        Dim current As ResourceDescriptor = Selected()
        If current IsNot Nothing Then AddOrEdit(current, Nothing)
    End Sub

    Private Sub AddOrEdit(existing As ResourceDescriptor, draft As ResourceDescriptor)
        Using dialog As New Resource_Edit_Form(existing, draft)
            If dialog.ShowDialog(Me) <> DialogResult.OK Then Return
            Dim result As ResourceCommitResult = ResourceCommit.Commit(dialog.Result, dialog.NewPassword,
                AddressOf ResourceRegistry.HiveRead, AddressOf ResourceRegistry.HiveWrite, secrets)
            If Not result.Succeeded Then
                AppFileLogger.WriteLine("Resource: save failed - " & result.Outcome.ToString())
                MessageBox.Show(Me, Localization.T("Не удалось сохранить ресурс. Ничего не потеряно: то, что было сохранено раньше, осталось как было"),
                                Me.Text, MessageBoxButtons.OK, MessageBoxIcon.Error)
                Return
            End If
            Refill(dialog.Result.Id)
        End Using
    End Sub

    Private Sub OnRemove(sender As Object, e As EventArgs)
        Dim current As ResourceDescriptor = Selected()
        If current Is Nothing Then Return
        If MessageBox.Show(Me, Localization.TF("Удалить ресурс «{0}»? Сохранённый для него пароль тоже будет удалён. Файлы на сервере не затрагиваются.", current.DisplayName),
                           Me.Text, MessageBoxButtons.YesNo, MessageBoxIcon.Question, MessageBoxDefaultButton.Button2) <> DialogResult.Yes Then Return
        Dim result As ResourceCommitResult = ResourceCommit.Remove(current.Id, AddressOf ResourceRegistry.HiveRead, AddressOf ResourceRegistry.HiveWrite, secrets)
        If result.Succeeded Then
            removed.Add(current.Id)
            Say("")
        Else
            Say(Localization.T("Не удалось удалить ресурс"))
        End If
        Refill()
    End Sub

    Private Async Sub OnTest(sender As Object, e As EventArgs)
        Dim current As ResourceDescriptor = Selected()
        If current Is Nothing Then Return
        testCancel?.Cancel()
        testCancel?.Dispose()
        Dim cancel As New CancellationTokenSource()
        testCancel = cancel
        testButton.Enabled = False
        Say(Localization.T("Проверка.."))
        Try
            Dim sentence As String
            If current.Kind = ResourceKind.Sftp Then
                ' Every address is asked, in the producer's order: a stale LAN address next to a live
                ' port forward is the ordinary state of an imported resource, and saying only the
                ' first verdict would call a working resource broken.
                Dim answers As New List(Of String)()
                For Each endpoint As FmsEndpoint In ResourceConnect.CandidatesOf(current).Distinct(New EndpointKeyComparer())
                    Dim verdict As ProbeVerdict = Await ResourceProbe.ProbeSftpAsync(endpoint.Host, endpoint.Port, cancel.Token)
                    If cancel.IsCancellationRequested Then Return
                    answers.Add(endpoint.Host & ":" & endpoint.Port.ToString(CultureInfo.InvariantCulture) & " - " & ResourceProbe.VerdictSentence(verdict))
                Next
                sentence = String.Join(vbLf, answers)
            Else
                sentence = Await TestShareAsync(current, cancel.Token)
            End If
            If Not cancel.IsCancellationRequested Then Say(sentence)
        Finally
            If testCancel Is cancel Then testButton.Enabled = Selected() IsNot Nothing
        End Try
    End Sub

    Private NotInheritable Class EndpointKeyComparer
        Implements IEqualityComparer(Of FmsEndpoint)
        Public Overloads Function Equals(x As FmsEndpoint, y As FmsEndpoint) As Boolean Implements IEqualityComparer(Of FmsEndpoint).Equals
            Return String.Equals(x.Key(), y.Key(), StringComparison.Ordinal)
        End Function
        Public Overloads Function GetHashCode(obj As FmsEndpoint) As Integer Implements IEqualityComparer(Of FmsEndpoint).GetHashCode
            Return obj.Key().GetHashCode()
        End Function
    End Class

    ''' <summary>A share is tested the only honest way: by opening it and reading its top folder, with the
    ''' stored credential, then letting go of the connection.</summary>
    Private Async Function TestShareAsync(current As ResourceDescriptor, ct As CancellationToken) As Task(Of String)
        Dim credential As CredentialReadResult = ResourceCommit.ReadCredential(current, secrets)
        If credential.Outcome <> CredentialReadOutcome.Ok Then Return Localization.T("Пароль не сохранён: введите его через «Изменить..»")
        Dim outcome As ConnectOutcome = Await ResourceConnect.ConnectAsync(current, credential.Secret, Nothing, Nothing, Nothing, ct)
        If Not outcome.Succeeded Then Return ResourceMessages.ForFailure(outcome.Failure)
        Try
            Await Task.Run(Sub() outcome.Provider.ListEntries(current.RootPath, ct))
            Return Localization.T("Папка открывается")
        Catch ex As Exception
            Return ResourceMessages.ForFailure(ResourceFailure.FromException(ex))
        Finally
            DirectCast(outcome.Provider, IDisposable)?.Dispose()
        End Try
    End Function

    ' --------------------------------------------------------------------------- import ----

    Private Sub OnImportFile(sender As Object, e As EventArgs)
        Using dialog As New OpenFileDialog With {
            .Title = Localization.T("Импорт ресурса"),
            .Filter = Localization.T("Файл настроек ресурса (*.fmscfg;*.json;*.txt)|*.fmscfg;*.json;*.txt|Все файлы (*.*)|*.*"),
            .CheckFileExists = True}
            If dialog.ShowDialog(Me) <> DialogResult.OK Then Return
            ImportAndRefresh(ResourceImportFlow.ReadFile(dialog.FileName))
        End Using
    End Sub

    Private Sub OnImportClipboard(sender As Object, e As EventArgs)
        Dim text As String = Nothing
        Try
            If Clipboard.ContainsText() Then text = Clipboard.GetText()
        Catch ex As Exception
            ' The clipboard can be held by another process for a moment; "nothing there" is the honest answer.
        End Try
        If String.IsNullOrWhiteSpace(text) Then
            Say(Localization.T("В буфере обмена нет настроек ресурса"))
            Return
        End If
        ImportAndRefresh(FmsConfigReader.ReadText(text))
    End Sub

    Private Sub ImportAndRefresh(read As FmsConfigReadResult)
        Dim stored As IReadOnlyList(Of String) = ResourceImportFlow.Run(Me, read)
        If stored.Count > 0 Then
            Say(Localization.TF("Добавлено или обновлено ресурсов: {0}", stored.Count))
            Refill()
        End If
    End Sub

End Class
#End If
