#If Not NETFRAMEWORK Then
Option Strict On

Imports System.Collections.Generic
Imports System.IO
Imports System.Linq
Imports System.Text
Imports System.Windows.Forms

''' <summary>
''' Importing a Share Manager document, as a person meets it (007 §3.1): read it with the one reader, show
''' what it would do, ask only what is needed, store it. The same flow serves every entry - a file, the
''' clipboard and a dropped file - because the reader does, and a rule that held on one surface and not
''' another is exactly what a single reader exists to prevent.
'''
''' Nothing here connects to anything. The host key arrives from the document, so the first connection is
''' already verified; a document without one leaves the usual explicit trust prompt for later.
''' </summary>
Friend NotInheritable Class ResourceImportFlow

    Private Sub New()
    End Sub

    Friend Const MaxFileBytes As Long = 2L * 1024L * 1024L

    ''' <summary>Reads a file for import. Bounded BEFORE it is read, because the reader's own limits count
    ''' bytes it already holds: a multi-gigabyte file named .fmscfg must not be pulled into memory first.</summary>
    Friend Shared Function ReadFile(path As String) As FmsConfigReadResult
        Try
            Dim info As New FileInfo(path)
            If info.Length > MaxFileBytes Then Return New FmsConfigReadResult With {.Failure = FmsConfigFailure.TooLarge}
            Return FmsConfigReader.ReadBytes(File.ReadAllBytes(path))
        Catch ex As Exception When TypeOf ex Is IOException OrElse TypeOf ex Is UnauthorizedAccessException
            Return New FmsConfigReadResult With {.Failure = FmsConfigFailure.Empty}
        End Try
    End Function

    ''' <summary>The extensions an import accepts from the open dialog and from a drop. A producer's own
    ''' name for the file is .fmscfg; the others are what a copied document tends to be saved as.</summary>
    Friend Shared Function IsImportFile(path As String) As Boolean
        Dim ext As String = System.IO.Path.GetExtension(If(path, "")).ToLowerInvariant()
        Return ext = ".fmscfg" OrElse ext = ".json" OrElse ext = ".txt"
    End Function

    ''' <summary>Runs the whole flow for one document. Returns the ids of the resources that were stored
    ''' (empty when the user declined or nothing could be stored); the reason has already been shown.</summary>
    Friend Shared Function Run(owner As IWin32Window, read As FmsConfigReadResult) As IReadOnlyList(Of String)
        Dim title As String = Localization.T("Импорт ресурса")
        If read Is Nothing OrElse Not read.Succeeded Then
            ViewerBox.Show(owner, ResourceMessages.ForImportFailure(If(read, New FmsConfigReadResult()).Failure), title,
                            MessageBoxButtons.OK, MessageBoxIcon.Warning)
            Return New List(Of String)()
        End If

        Dim existing As List(Of ResourceDescriptor) = ResourceRegistry.Load()
        Dim preview As ImportPreview = ResourceImport.Preview(read.Plan, existing, AddressOf ResourceImport.NewResourceId)

        If preview.Refusal = ImportRefusal.PinnedRoot Then
            Dim locked As FmsRootPlan = read.Plan.RefusedRoots.FirstOrDefault()
            ViewerBox.Show(owner,
                Localization.TF("Файл не импортирован: папка «{0}» защищена PIN-кодом, а Fast Media Sorter пока не умеет открывать такие ресурсы.",
                                If(locked IsNot Nothing, If(locked.Label.Length > 0, locked.Label, locked.VirtualPath), "")),
                title, MessageBoxButtons.OK, MessageBoxIcon.Information)
            Return New List(Of String)()
        End If
        If Not preview.CanApply Then
            ViewerBox.Show(owner, Localization.T("В файле нет ни одной папки для импорта"), title, MessageBoxButtons.OK, MessageBoxIcon.Information)
            Return New List(Of String)()
        End If

        If ViewerBox.Show(owner, Summary(preview), title, MessageBoxButtons.OKCancel, MessageBoxIcon.Question) <> DialogResult.OK Then Return New List(Of String)()

        Dim password As String = Nothing
        If preview.NeedsPassword Then
            Using dialog As New Resource_Credential_Form(read.Plan.ResourceName, read.Plan.UserName, retry:=False, showRemember:=False)
                If DialogLayer.ShowViewerDialog(dialog, owner) <> DialogResult.OK Then Return New List(Of String)()
                password = dialog.Credential
            End Using
        End If

        Dim confirmedPin As Boolean
        If preview.RequiresPinConfirmation Then
            Dim changed As ImportItem = preview.Items.First(Function(i) i.Pin.RequiresConfirmation)
            ' Cancel is the default: a changed key is the one thing in an import that must be a deliberate yes.
            confirmedPin = ViewerBox.Show(owner,
                Localization.TF("Ключ сервера в файле отличается от запомненного.{0}Запомнен: {1}{0}В файле: {2}{0}{0}Заменить его? Если вы не ждали этого, ответьте «Нет».",
                                vbLf, changed.Pin.StoredPin, changed.Pin.ImportedPin),
                title, MessageBoxButtons.YesNo, MessageBoxIcon.Warning, MessageBoxDefaultButton.Button2) = DialogResult.Yes
        End If

        Dim result As ImportApplyResult = ResourceImport.Apply(preview, password, confirmedPin,
            AddressOf ResourceRegistry.HiveRead, AddressOf ResourceRegistry.HiveWrite, New RegistryResourceSecretStore())
        If result.Outcome <> ImportApplyOutcome.Applied Then
            AppFileLogger.WriteLine("Resource: import stopped - " & result.Outcome.ToString() & "/" & result.Failure.ToString() & " after " & result.Committed.ToString())
            ViewerBox.Show(owner, Localization.T("Не удалось сохранить ресурс. Ничего не потеряно: то, что было сохранено раньше, осталось как было"),
                            title, MessageBoxButtons.OK, MessageBoxIcon.Error)
        End If
        Return result.Ids
    End Function

    ''' <summary>What the confirmation says: the resource, what each folder becomes, and what is NOT in the
    ''' document that the user will meet later. No password, no fingerprint beyond whether there is one.</summary>
    Private Shared Function Summary(preview As ImportPreview) As String
        Dim sb As New StringBuilder()
        Dim plan As FmsConfigPlan = preview.Plan
        sb.Append(Localization.TF("Ресурс «{0}», пользователь {1}", If(plan.ResourceName.Length > 0, plan.ResourceName, plan.Endpoints.First().Host), plan.UserName)).Append(vbLf).Append(vbLf)
        For Each item As ImportItem In preview.Items
            sb.Append("• ").Append(item.Root.VirtualPath)
            sb.Append("  ").Append(If(item.IsUpdate, Localization.T("(обновить)"), Localization.T("(новый)")))
            If item.Descriptor.ReadOnlyHint Then sb.Append("  ").Append(Localization.T("только чтение"))
            sb.Append(vbLf)
        Next
        sb.Append(vbLf)
        If plan.IsPinned Then
            sb.Append(Localization.T("Ключ сервера есть в файле: он будет проверен уже при первом подключении."))
        Else
            sb.Append(Localization.T("Ключа сервера в файле нет: при первом подключении вы увидите его отпечаток и решите, доверять ли ему."))
        End If
        Return sb.ToString()
    End Function

End Class
#End If
