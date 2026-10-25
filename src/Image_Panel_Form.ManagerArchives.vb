#If Not NETFRAMEWORK Then
Option Explicit On
Option Strict On
Option Infer On
Option Compare Binary

Imports System.Collections.Generic
Imports System.IO
Imports System.Linq
Imports System.Windows.Forms

Partial Public Class Image_Panel_Form
    Private ReadOnly manager_Zip As New ToolStripButton()
    Private ReadOnly manager_Extract As New ToolStripButton()

    Private Sub BuildManagerArchiveCommands()
        manager_Zip.Text = Localization.T("Создать ZIP")
        manager_Extract.Text = Localization.T("Распаковать здесь")
        manager_Zip.Visible = False
        manager_Extract.Visible = False
        AddHandler manager_Zip.Click, Sub() RunManagerArchive(False)
        AddHandler manager_Extract.Click, Sub() RunManagerArchive(True)
        toolbar.Items.Add(manager_Zip)
        toolbar.Items.Add(manager_Extract)
    End Sub

    Private Function ManagerArchiveAllowed(extract As Boolean) As Boolean
        If Not manager_Mode OrElse Not ManagerAllows(ManagerCommandKind.Copy) Then Return False
        If Not extract Then Return True
        Dim selected As List(Of ManagerEntry) = SelectedManagerEntries()
        Return selected.Count = 1 AndAlso Not selected(0).IsDirectory AndAlso ManagerDestinationWritable() AndAlso
            {".zip", ".cbz", ".7z", ".rar", ".cbr"}.Contains(Path.GetExtension(selected(0).Name).ToLowerInvariant())
    End Function

    Private Sub UpdateManagerArchiveCommands()
        manager_Zip.Visible = ManagerArchiveAllowed(False)
        manager_Extract.Visible = ManagerArchiveAllowed(True)
    End Sub

    Private Sub OnManagerArchiveProgress(sender As Object, e As BatchProgressEventArgs)
        If IsDisposed Then Return
        If e Is Nothing Then
            run_Is_Active = Main_Form.BatchIsRunning
            If Not run_Is_Active Then ShowStrip(Main_Form.StatusLineText, False, False, False)
            Return
        End If
        run_Is_Active = True
        ShowStrip(Localization.TF("Архив: записей {0}; байт {1}", e.Index, e.BytesDone), True, False, False)
    End Sub

    Private Async Sub RunManagerArchive(extract As Boolean)
        If Not ManagerArchiveAllowed(extract) Then Return
        Dim location As ManagerLocation = nav.Current
        Dim generation As Integer = browser.Generation
        Dim epoch As Integer = manager_Epoch
        Dim entries As List(Of ManagerEntry) = SelectedManagerEntries()
        Dim capturedBrowser As ManagerBrowser = browser
        Dim source As ManagerEndpointLease = Nothing
        Dim destination As ManagerEndpointLease = Nothing
        Dim options As New ManagerArchiveOptions With {.Extract = extract}
        Dim leaf As String = ""
        Dim target As ManagerLocation = location
        Dim canDelete As Boolean = ManagerAllows(ManagerCommandKind.Delete)
        manager_Command_Starting = True
        Try
            If Not extract Then
                If Not ManagerDestinationWritable() Then
                    Using picker As New FolderBrowserDialog()
                        If picker.ShowDialog(Me) <> DialogResult.OK Then Return
                        Dim resolved As ManagerAddressResult = ManagerAddress.Resolve(picker.SelectedPath, resource_List)
                        If resolved.Kind <> ManagerAddressKind.Ok Then Return
                        target = resolved.Location
                    End Using
                End If
                Using dialog As New ManagerArchiveForm(target, AddressOf ChooseManagerDestination, canDelete)
                    If DialogLayer.ShowViewerDialog(dialog, Me) <> DialogResult.OK Then Return
                    target = dialog.Destination
                    leaf = dialog.ArchiveName
                    options.DeleteOriginals = dialog.DeleteOriginals
                End Using
            End If
            source = Await CaptureManagerEndpoint(location, capturedBrowser)
            destination = Await CaptureManagerEndpoint(target, capturedBrowser)
            If IsDisposed OrElse epoch <> manager_Epoch Then Return
            Dim prefs As ModernViewerPreferences = Main_Form.GetModernPreferences()
            Dim request As New ManagerCommandRequest(ManagerCommandKind.Copy, location, generation, entries, source, destination,
                target.DirectoryPath, leaf, prefs.NameCollisionPolicy, prefs.ConfirmDelete, prefs.DeleteToRecycleBin)
            source = Nothing
            destination = Nothing
            If Not Await Main_Form.StartManagerCommandAsync(Me, request, options) AndAlso Not IsDisposed Then ShowStrip(Main_Form.StatusLineText, False, False, True)
        Catch ex As Exception
            AppFileLogger.LogException("Manager archive", ex)
            If Not IsDisposed Then ShowStrip(FailureText.Describe(ex), False, False, True)
        Finally
            source?.Dispose()
            destination?.Dispose()
            manager_Command_Starting = False
            If Not IsDisposed Then UpdateManagerCommands()
        End Try
    End Sub
End Class
#End If
