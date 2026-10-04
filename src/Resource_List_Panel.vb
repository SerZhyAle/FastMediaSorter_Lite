#If Not NETFRAMEWORK Then
Option Strict On

Imports System.Collections.Generic
Imports System.Drawing
Imports System.Globalization
Imports System.Linq
Imports System.Threading
Imports System.Threading.Tasks
Imports System.Windows.Forms

''' <summary>
''' The resource list and its actions (ticket 118 Ф-1, one implementation for two hosts): the
''' list, hint, status line and buttons of the former manager-only dialog, extracted verbatim so
''' the manager dialog keeps its window chrome and the new Settings page embeds this same panel.
''' There is deliberately no second, drifting copy of the list, the buttons or the commit order.
'''
''' Everything it stores goes through <see cref="ResourceCommit"/> (the credential first, the
''' registry reference last), so no order of clicks and no failure part-way leaves a list entry
''' without its password. The hosts learn what happened through three events:
''' <see cref="ResourcesChanged"/> (any registry write), <see cref="ItemActivated"/> (a
''' double-click; only the manager dialog answers it) and <see cref="SelectionChanged"/>.
''' </summary>
Friend NotInheritable Class Resource_List_Panel
    Inherits UserControl

    Private ReadOnly list As ListView
    Private ReadOnly hint As Label
    Private ReadOnly status As Label
    Private ReadOnly addButton As Button
    Private ReadOnly editButton As Button
    Private ReadOnly removeButton As Button
    Private ReadOnly testButton As Button
    Private ReadOnly exportButton As Button
    Private ReadOnly importFileButton As Button
    Private ReadOnly importClipboardButton As Button
    Private ReadOnly buttonsFlow As FlowLayoutPanel
    Private ReadOnly removed As New List(Of String)()
    Private testCancel As CancellationTokenSource
    Private ReadOnly panelScale As Single

    ' Persistence and prompts, injectable so the tests drive the same code the two hosts ship
    ' with; every default is exactly what the dialog used before the extraction.
    Private ReadOnly hiveRead As Func(Of String, String)
    Private ReadOnly hiveWrite As Action(Of String, String)
    Private ReadOnly secrets As IResourceSecretStore
    Private ReadOnly confirmRemove As Func(Of ResourceDescriptor, Boolean)
    Private ReadOnly customStore As Boolean

    ''' <summary>Any registry write happened through this panel: adds, edits, removals, imports.
    ''' Carries the ids removed so far, so a host can close a resource that is on screen.</summary>
    Friend Event ResourcesChanged(removedIds As IReadOnlyList(Of String))

    ''' <summary>The user double-clicked a resource. Only the manager dialog subscribes: there it
    ''' means Open. In the Settings page a double-click only selects (Q118-13).</summary>
    Friend Event ItemActivated(resource As ResourceDescriptor)

    ''' <summary>The selection moved; the argument says whether anything is selected.</summary>
    Friend Event SelectionChanged(hasSelection As Boolean)

    ''' <param name="scale">The interface scale: 1.0 for the dialog (its exact former metrics),
    ''' the settings window's own scale for the Settings page.</param>
    Friend Sub New(Optional scale As Single = 1.0F,
                   Optional readValue As Func(Of String, String) = Nothing,
                   Optional writeValue As Action(Of String, String) = Nothing,
                   Optional secretStore As IResourceSecretStore = Nothing,
                   Optional removeConfirmation As Func(Of ResourceDescriptor, Boolean) = Nothing)
        panelScale = If(scale > 0.0F, scale, 1.0F)
        customStore = readValue IsNot Nothing OrElse writeValue IsNot Nothing OrElse secretStore IsNot Nothing
        hiveRead = If(readValue, AddressOf ResourceRegistry.HiveRead)
        hiveWrite = If(writeValue, AddressOf ResourceRegistry.HiveWrite)
        secrets = If(secretStore, New RegistryResourceSecretStore())
        confirmRemove = If(removeConfirmation, AddressOf AskRemoveConfirmation)

        If Localization.IsRightToLeft() Then RightToLeft = RightToLeft.Yes

        list = New ListView With {
            .Name = "res_List", .View = View.Details, .FullRowSelect = True, .MultiSelect = False, .HideSelection = False,
            .Location = New Point(Sp(12), Sp(12)),
            .Anchor = AnchorStyles.Top Or AnchorStyles.Bottom Or AnchorStyles.Left Or AnchorStyles.Right}
        list.Columns.Add(Localization.T("Название"), Sp(150))
        list.Columns.Add(Localization.T("Тип"), Sp(120))
        list.Columns.Add(Localization.T("Адрес"), Sp(210))
        list.Columns.Add(Localization.T("Доступ"), Sp(90))
        list.Columns.Add(Localization.T("Открывался"), Sp(96))
        AddHandler list.SelectedIndexChanged,
            Sub()
                UpdateButtons()
                RaiseEvent SelectionChanged(Selected() IsNot Nothing)
            End Sub
        AddHandler list.DoubleClick,
            Sub()
                Dim chosen As ResourceDescriptor = Selected()
                If chosen IsNot Nothing Then RaiseEvent ItemActivated(chosen)
            End Sub

        hint = New Label With {
            .Name = "res_Hint", .AutoSize = False, .Location = New Point(Sp(24), Sp(60)), .Size = New Size(Sp(0), Sp(80)),
            .TextAlign = ContentAlignment.TopCenter, .ForeColor = SystemColors.GrayText,
            .Text = Localization.T("Здесь пока нет ресурсов. Добавьте сервер вручную или импортируйте файл настроек из Share Manager (.fmscfg)."),
            .Anchor = AnchorStyles.Top Or AnchorStyles.Left Or AnchorStyles.Right}

        status = New Label With {
            .Name = "res_Status", .AutoSize = False, .Height = Sp(34),
            .Anchor = AnchorStyles.Bottom Or AnchorStyles.Left Or AnchorStyles.Right}

        buttonsFlow = New FlowLayoutPanel With {
            .Dock = DockStyle.Bottom, .AutoSize = True, .AutoSizeMode = AutoSizeMode.GrowAndShrink,
            .WrapContents = True, .Margin = Padding.Empty, .Padding = New Padding(Sp(8), Sp(4), Sp(8), Sp(8))}
        addButton = MakeButton("res_Add", Localization.T("Добавить.."), AddressOf OnAdd)
        editButton = MakeButton("res_Edit", Localization.T("Изменить.."), AddressOf OnEdit)
        removeButton = MakeButton("res_Remove", Localization.T("Удалить"), AddressOf OnRemove)
        testButton = MakeButton("res_Test", Localization.T("Проверить"), AddressOf OnTest)
        exportButton = MakeButton("res_Export", Localization.T("Экспорт.."), AddressOf OnExport)
        importFileButton = MakeButton("res_ImportFile", Localization.T("Импорт из файла.."), AddressOf OnImportFile)
        importClipboardButton = MakeButton("res_ImportClipboard", Localization.T("Импорт из буфера"), AddressOf OnImportClipboard)
        For Each b As Button In {addButton, editButton, removeButton, testButton, exportButton, importFileButton, importClipboardButton}
            b.AutoSize = True
            b.AutoSizeMode = AutoSizeMode.GrowAndShrink
            b.MinimumSize = New Size(0, Sp(28)) ' the ICON-RENDER floor, scaled with the host window
            buttonsFlow.Controls.Add(b)
        Next

        Controls.Add(list)
        Controls.Add(hint)
        Controls.Add(status)
        Controls.Add(buttonsFlow)
        hint.BringToFront()

        AddHandler Resize, Sub() Relayout()
        Relayout()
        Refill()
    End Sub

    Private Function Sp(value As Integer) As Integer
        If panelScale = 1.0F Then Return value
        Return CInt(Math.Round(value * panelScale))
    End Function

    Private Function MakeButton(controlName As String, text As String, handler As EventHandler) As Button
        Dim b As New Button With {.Name = controlName, .Text = text}
        AddHandler b.Click, handler
        Return b
    End Function

    ''' <summary>The list fills the panel above the status line and the button row; the hint sits
    ''' over the empty list, centred. Everything follows the panel's own size, so the two hosts
    ''' (a 700x372 dialog and a settings page) need no layout code of their own.</summary>
    Private Sub Relayout()
        Dim width As Integer = Math.Max(Sp(120), ClientSize.Width)
        list.Size = New Size(width - Sp(24), Math.Max(Sp(60), ClientSize.Height - Sp(12) - status.Height - Sp(10) - buttonsFlow.Height - Sp(4)))
        hint.Size = New Size(width - Sp(48), Sp(80))
        status.SetBounds(Sp(12), ClientSize.Height - buttonsFlow.Height - status.Height - Sp(10), width - Sp(24), status.Height)
        list.BringToFront()
        hint.BringToFront()
        status.BringToFront()
        buttonsFlow.BringToFront()
    End Sub

    ''' <summary>Ids removed while this panel was open, so a host can close one that is on screen.</summary>
    Friend ReadOnly Property RemovedIds As IReadOnlyList(Of String)
        Get
            Return removed
        End Get
    End Property

    ''' <summary>Starts the "new resource" form with <paramref name="draft"/> filled in. The manager
    ''' dialog calls it from OnShown for a typed sftp:// address.</summary>
    Friend Sub BeginAdd(draft As ResourceDescriptor)
        AddOrEdit(Nothing, draft)
    End Sub

    ' ---------------------------------------------------------------------------- list ----

    ''' <summary>The list's source. The real hive keeps its own recovery path
    ''' (<see cref="ResourceRegistry.Load"/>); an injected reader is the tests' in-memory registry.</summary>
    Private Function LoadResources() As List(Of ResourceDescriptor)
        If customStore Then Return ResourceRegistry.Parse(hiveRead(ResourceRegistry.Value_Name))
        Return ResourceRegistry.Load()
    End Function

    Private Sub Refill(Optional select_Id As String = Nothing)
        list.BeginUpdate()
        Try
            list.Items.Clear()
            ' The most recently used first: the one opened an hour ago is the one wanted again.
            Dim ordered As IEnumerable(Of ResourceDescriptor) = LoadResources().
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

    ''' <summary>The resource of the selected row, or Nothing - what a host's own Open needs.</summary>
    Friend ReadOnly Property SelectedResource As ResourceDescriptor
        Get
            Return Selected()
        End Get
    End Property

    Private Sub UpdateButtons()
        Dim has As Boolean = Selected() IsNot Nothing
        editButton.Enabled = has
        removeButton.Enabled = has
        testButton.Enabled = has
        exportButton.Enabled = has
    End Sub

    Private Sub Say(text As String)
        status.Text = text
    End Sub

    ' -------------------------------------------------------------------------- actions ----

    Private Sub OnAdd(sender As Object, e As EventArgs)
        AddOrEdit(Nothing, Nothing)
    End Sub

    Private Sub OnEdit(sender As Object, e As EventArgs)
        Dim current As ResourceDescriptor = Selected()
        If current IsNot Nothing Then AddOrEdit(current, Nothing)
    End Sub

    Private Sub AddOrEdit(existing As ResourceDescriptor, draft As ResourceDescriptor)
        Dim owner As IWin32Window = If(FindForm(), DirectCast(Me, IWin32Window))
        Using dialog As New Resource_Edit_Form(existing, draft)
            If dialog.ShowDialog(owner) <> DialogResult.OK Then Return
            Dim result As ResourceCommitResult = ResourceCommit.Commit(dialog.Result, dialog.NewPassword, hiveRead, hiveWrite, secrets)
            If Not result.Succeeded Then
                AppFileLogger.WriteLine("Resource: save failed - " & result.Outcome.ToString())
                MessageBox.Show(owner, Localization.T("Не удалось сохранить ресурс. Ничего не потеряно: то, что было сохранено раньше, осталось как было"),
                                dialog.Text, MessageBoxButtons.OK, MessageBoxIcon.Error)
                Return
            End If
            Refill(dialog.Result.Id)
            RaiseEvent ResourcesChanged(removed)
        End Using
    End Sub

    ''' <summary>The dialog's own question, the default answer No. Injectable for the tests.</summary>
    Private Function AskRemoveConfirmation(current As ResourceDescriptor) As Boolean
        Dim owner As IWin32Window = If(FindForm(), DirectCast(Me, IWin32Window))
        Return MessageBox.Show(owner,
                               Localization.TF("Удалить ресурс «{0}»? Сохранённый для него пароль тоже будет удалён. Файлы на сервере не затрагиваются.", current.DisplayName),
                               If(FindForm()?.Text, Localization.T("Ресурсы")),
                               MessageBoxButtons.YesNo, MessageBoxIcon.Question, MessageBoxDefaultButton.Button2) = DialogResult.Yes
    End Function

    Private Sub OnRemove(sender As Object, e As EventArgs)
        Dim current As ResourceDescriptor = Selected()
        If current Is Nothing Then Return
        If Not confirmRemove(current) Then Return
        Dim result As ResourceCommitResult = ResourceCommit.Remove(current.Id, hiveRead, hiveWrite, secrets)
        If result.Succeeded Then
            removed.Add(current.Id)
            Say("")
            RaiseEvent ResourcesChanged(removed)
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
        Dim owner As IWin32Window = If(FindForm(), DirectCast(Me, IWin32Window))
        Using dialog As New OpenFileDialog With {
            .Title = Localization.T("Импорт ресурса"),
            .Filter = Localization.T("Файл настроек ресурса (*.fmscfg;*.json;*.txt)|*.fmscfg;*.json;*.txt|Все файлы (*.*)|*.*"),
            .CheckFileExists = True}
            If dialog.ShowDialog(owner) <> DialogResult.OK Then Return
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
        Dim owner As IWin32Window = If(FindForm(), DirectCast(Me, IWin32Window))
        Dim stored As IReadOnlyList(Of String) = ResourceImportFlow.Run(owner, read)
        If stored.Count > 0 Then
            Say(Localization.TF("Добавлено или обновлено ресурсов: {0}", stored.Count))
            Refill()
            RaiseEvent ResourcesChanged(removed)
        End If
    End Sub

    ' --------------------------------------------------------------------------- export ----

    Private Sub OnExport(sender As Object, e As EventArgs)
        Dim current As ResourceDescriptor = Selected()
        If current Is Nothing Then Return
        Dim owner As IWin32Window = If(FindForm(), DirectCast(Me, IWin32Window))
        Using dialog As New Resource_Export_Form(current, secrets)
            dialog.ShowDialog(owner)
        End Using
    End Sub

    Protected Overrides Sub Dispose(disposing As Boolean)
        MyBase.Dispose(disposing)
        If disposing Then
            testCancel?.Cancel()
            testCancel?.Dispose()
        End If
    End Sub

End Class
#End If
