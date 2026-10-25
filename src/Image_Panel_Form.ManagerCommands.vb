#If Not NETFRAMEWORK Then
Option Explicit On
Option Strict On
Option Infer On
Option Compare Binary

Imports System.Collections.Generic
Imports System.Diagnostics
Imports System.IO
Imports System.Linq
Imports System.Threading
Imports System.Threading.Tasks
Imports System.Windows.Forms

Partial Public Class Image_Panel_Form
    Private selection_Clipboard As ManagerSelectionClipboard
    Private manager_Command_Starting As Boolean
    Private manager_Invalidated As Boolean
    Private ReadOnly manager_Buttons As New Dictionary(Of ManagerCommandKind, ToolStripButton)()
    Private manager_Paste As ToolStripButton
    Private manager_Clipboard_Label As ToolStripLabel
    Private manager_Menu As ContextMenuStrip

    Private Function SelectedManagerEntries() As List(Of ManagerEntry)
        Return model.SelectedPaths().Where(Function(id) entry_By_Identity.ContainsKey(id)).Select(Function(id) entry_By_Identity(id)).ToList()
    End Function

    Private Function ManagerAllows(kind As ManagerCommandKind) As Boolean
        If Not manager_Mode OrElse nav?.Current Is Nothing OrElse browser Is Nothing OrElse browser.IsBusy OrElse manager_Command_Starting OrElse Main_Form.BatchBusy() Then Return False
        Dim caps As ProviderCapabilities = browser.KnownCapabilities(nav.Current.Place)
        If caps Is Nothing Then Return False
        Try
            Return ManagerCommandPlanner.Allows(kind, CurrentManagerPolicy(nav.Current.Place, caps, browser.ConnectedDescriptor(nav.Current.Place)), SelectedManagerEntries(), nav.Current)
        Catch
            Return False
        End Try
    End Function

    Private Function CurrentManagerPolicy(place As ManagerPlace, caps As ProviderCapabilities,
                                          Optional connected As ResourceDescriptor = Nothing,
                                          Optional freshExchange As Boolean = False) As ProviderCapabilities
        If Not place.NeedsConnection Then Return caps
        Dim descriptor As ResourceDescriptor = LoadResources().FirstOrDefault(Function(d) String.Equals(d.Id, place.ResourceId, StringComparison.OrdinalIgnoreCase))
        If descriptor Is Nothing OrElse descriptor.Kind <> place.Kind OrElse Not ManagerPathRules.AreEqual(place.Kind, descriptor.RootPath, place.RootPath) OrElse
           ManagerPlace.ForResource(descriptor).ExchangeIdentity <> place.ExchangeIdentity Then
            Throw ManagerCommandPlanner.Failure(place.Title, ManagerFailure.Permission)
        End If
        If place.ExchangeEntryId.Length > 0 Then
            If freshExchange Then
                ManagerExchangeAccess.Validate(place, descriptor, CancellationToken.None)
            Else
                ManagerExchangeAccess.RequireAvailable(ManagerExchangeAccess.CachedState(place, manager_Exchange))
            End If
        End If
        If place.ExchangeEntryId.Length > 0 Then
            If descriptor.ProtectionUnsupported Then Throw New ManagerExchangeException(ManagerExchangeAvailability.Unsupported)
            If descriptor.PinRequired OrElse descriptor.PinChanged Then Throw New ManagerExchangeException(ManagerExchangeAvailability.Locked)
        ElseIf descriptor.PinRequired OrElse descriptor.PinChanged OrElse descriptor.ProtectionUnsupported Then
            Throw ManagerCommandPlanner.Failure(place.Title, ManagerFailure.Permission)
        End If
        If connected IsNot Nothing AndAlso (Not String.Equals(connected.Host, descriptor.Host, StringComparison.OrdinalIgnoreCase) OrElse
            connected.Port <> descriptor.Port OrElse Not String.Equals(connected.UserName, descriptor.UserName, StringComparison.Ordinal) OrElse
            Not String.Equals(connected.PinnedFingerprint, descriptor.PinnedFingerprint, StringComparison.Ordinal) OrElse
            connected.SecretHandle <> descriptor.SecretHandle OrElse ManagerPlace.ForResource(connected).ExchangeIdentity <> place.ExchangeIdentity) Then
            Throw ManagerCommandPlanner.Failure(place.Title, ManagerFailure.Permission)
        End If
        If Not descriptor.ReadOnlyHint Then Return caps
        Return New ProviderCapabilities(canWrite:=False, canDelete:=False, canRename:=False, canCreateFolder:=False,
                                        hasRecycleBin:=False, needsMaterialisation:=caps.NeedsMaterialisation, isMetered:=caps.IsMetered, costHint:=caps.CostHint)
    End Function

    Private Async Function CaptureManagerEndpoint(location As ManagerLocation, capturedBrowser As ManagerBrowser) As Task(Of ManagerEndpointLease)
        PrimeCredential(location.Place)
        Dim place As ManagerPlace = location.Place
        Return Await Task.Run(Function()
            Dim connected As ResourceDescriptor = Nothing
            Dim lease As ManagerEndpointLease = capturedBrowser.CaptureEndpoint(place, CancellationToken.None,
                Function(provider) CurrentManagerPolicy(place, provider.Capabilities, connected, freshExchange:=True))
            connected = lease.ConnectedDescriptor
            Return lease
        End Function)
    End Function

    Private Sub BuildManagerCommands()
        BuildManagerArchiveCommands()
        For Each kind As ManagerCommandKind In {ManagerCommandKind.Copy, ManagerCommandKind.Move, ManagerCommandKind.CreateDirectory}
            Dim command As ManagerCommandKind = kind
            Dim button As New ToolStripButton(ManagerCommandCaption(command)) With {.Visible = False}
            AddHandler button.Click, Sub() RunManagerCommand(command, chooseDestination:=command <> ManagerCommandKind.CreateDirectory)
            manager_Buttons.Add(command, button)
            toolbar.Items.Add(button)
        Next
        manager_Paste = New ToolStripButton(Localization.T("Вставить")) With {.Visible = False}
        AddHandler manager_Paste.Click, Sub() PasteManagerSelection()
        toolbar.Items.Add(manager_Paste)
        manager_Clipboard_Label = New ToolStripLabel With {.Visible = False}
        toolbar.Items.Add(manager_Clipboard_Label)
        manager_Menu = New ContextMenuStrip()
        AddHandler manager_Menu.Opening, Sub(s, e)
                                           manager_Menu.Items.Clear()
                                           If Not manager_Mode Then e.Cancel = True : Return
                                           If ManagerArchiveAllowed(False) Then manager_Menu.Items.Add(Localization.T("Создать ZIP"), Nothing, Sub() RunManagerArchive(False))
                                           If ManagerArchiveAllowed(True) Then manager_Menu.Items.Add(Localization.T("Распаковать здесь"), Nothing, Sub() RunManagerArchive(True))
                                           For Each kind As ManagerCommandKind In {ManagerCommandKind.Copy, ManagerCommandKind.Move, ManagerCommandKind.Rename,
                                               ManagerCommandKind.CreateDirectory, ManagerCommandKind.Delete, ManagerCommandKind.DeletePermanently}
                                               Dim command As ManagerCommandKind = kind
                                               If Not ManagerAllows(command) Then Continue For
                                               manager_Menu.Items.Add(ManagerCommandCaption(command), Nothing,
                                                   Sub() RunManagerCommand(command, chooseDestination:=command = ManagerCommandKind.Copy OrElse command = ManagerCommandKind.Move))
                                           Next
                                           If selection_Clipboard IsNot Nothing AndAlso ManagerDestinationWritable() Then
                                               manager_Menu.Items.Add(Localization.T("Вставить"), Nothing, Sub() PasteManagerSelection())
                                           End If
                                           If SelectedManagerEntries().Count = 1 Then
                                               manager_Menu.Items.Add(Localization.T("Свойства"), Nothing, Sub() ShowManagerProperties())
                                               If Not nav.Current.Place.IsRemotePosix Then
                                                   manager_Menu.Items.Add(Localization.T("Показать в проводнике"), Nothing, Sub() RevealManagerEntry())
                                               End If
                                           End If
                                           e.Cancel = manager_Menu.Items.Count = 0
                                       End Sub
    End Sub

    Private Shared Function ManagerCommandCaption(kind As ManagerCommandKind) As String
        Select Case kind
            Case ManagerCommandKind.Copy : Return Localization.T("Копировать в папку")
            Case ManagerCommandKind.Move : Return Localization.T("Переместить в папку")
            Case ManagerCommandKind.Rename : Return Localization.T("Переименовать")
            Case ManagerCommandKind.CreateDirectory : Return Localization.T("Новая папка")
            Case ManagerCommandKind.DeletePermanently : Return Localization.T("Удалить безвозвратно")
            Case Else : Return Localization.T("Удалить")
        End Select
    End Function

    Private Function ManagerDestinationWritable() As Boolean
        If nav?.Current Is Nothing OrElse browser Is Nothing Then Return False
        Try
            Dim caps As ProviderCapabilities = browser.KnownCapabilities(nav.Current.Place)
            Return caps IsNot Nothing AndAlso CurrentManagerPolicy(nav.Current.Place, caps, browser.ConnectedDescriptor(nav.Current.Place)).CanWrite
        Catch
            Return False
        End Try
    End Function

    Private Sub UpdateManagerCommands()
        UpdateManagerArchiveCommands()
        For Each pair In manager_Buttons
            pair.Value.Visible = manager_Mode AndAlso ManagerAllows(pair.Key)
        Next
        If manager_Paste Is Nothing Then Return
        manager_Paste.Visible = manager_Mode AndAlso selection_Clipboard IsNot Nothing AndAlso ManagerDestinationWritable()
        manager_Paste.Enabled = Not manager_Command_Starting AndAlso Not Main_Form.BatchBusy()
        manager_Clipboard_Label.Visible = manager_Mode AndAlso selection_Clipboard IsNot Nothing
        If selection_Clipboard IsNot Nothing Then manager_Clipboard_Label.Text = Localization.TF("{0}: {1}",
            If(selection_Clipboard.IsCut, Localization.T("Переместить"), Localization.T("Копировать")), selection_Clipboard.Entries.Count)
        If grid IsNot Nothing Then grid.ContextMenuStrip = If(manager_Mode, manager_Menu, Nothing)
        If details IsNot Nothing Then details.ContextMenuStrip = If(manager_Mode, manager_Menu, Nothing)
    End Sub

    Private Function ChooseManagerDestination() As ManagerLocation
        Dim allowed As List(Of ManagerPlace) = place_List.Where(
            Function(p) Not p.NeedsConnection OrElse resource_List.Any(Function(d) d.Id = p.ResourceId AndAlso Not d.ReadOnlyHint AndAlso
                                                                       Not d.PinRequired AndAlso Not d.PinChanged AndAlso Not d.ProtectionUnsupported AndAlso
                                                                       (p.ExchangeEntryId.Length = 0 OrElse ManagerExchangeAccess.CachedState(p, manager_Exchange) = ManagerExchangeAvailability.Available))).ToList()
        Using picker As New ManagerDestinationForm(allowed.Select(AddressOf ManagerDestinationTitle), nav.Current)
            If DialogLayer.ShowViewerDialog(picker, Me) <> DialogResult.OK Then Return Nothing
            Return picker.SelectedLocation
        End Using
    End Function

    Private Function ManagerDestinationTitle(place As ManagerPlace) As ManagerPlace
        If manager_Exchange Is Nothing OrElse place.ExchangeEntryId.Length = 0 Then Return place
        Dim nodes = manager_Exchange.Current.Nodes
        Dim root As ManagerExchangeNode = nodes.FirstOrDefault(Function(n) n.Place IsNot Nothing AndAlso n.Place.ExchangeIdentity = place.ExchangeIdentity)
        If root Is Nothing Then Return place
        Dim device As ManagerExchangeNode = nodes.FirstOrDefault(Function(n) n.Id = root.ParentId)
        Dim account As ManagerExchangeNode = nodes.FirstOrDefault(Function(n) n.Id = device?.ParentId)
        Dim title As String = String.Join(" / ", {account?.Title, device?.Title, root.Title})
        Return New ManagerPlace(place.Id, title, place.Kind, place.RootPath, place.ResourceId, place.ExchangeEntryId, place.ExchangeDeviceId, place.ExchangeResourceId)
    End Function

    Private Async Sub RunManagerCommand(kind As ManagerCommandKind, Optional chooseDestination As Boolean = False,
                                       Optional destinationLocation As ManagerLocation = Nothing)
        If Not ManagerAllows(kind) Then Return
        Dim location As ManagerLocation = nav.Current
        Dim generation As Integer = browser.Generation
        Dim epoch As Integer = manager_Epoch
        Dim entries As List(Of ManagerEntry) = SelectedManagerEntries()
        Dim capturedBrowser As ManagerBrowser = browser
        Dim source As ManagerEndpointLease = Nothing
        Dim destination As ManagerEndpointLease = Nothing
        manager_Command_Starting = True
        Try
            Dim leaf As String = ""
            If kind = ManagerCommandKind.Rename OrElse kind = ManagerCommandKind.CreateDirectory Then
                leaf = AskDialogs.Prompt(Me, New PromptSpec(ManagerCommandCaption(kind), Localization.T("Введите имя:"),
                    If(kind = ManagerCommandKind.Rename, entries(0).Name, ""), ManagerCommandCaption(kind),
                    Function(text) If(ManagerCommandPlanner.ValidLeaf(location.Place.Kind, text), Nothing, ManagerCommandPlanner.ReasonText(ManagerFailure.InvalidName))))
                If leaf Is Nothing Then Return
            End If
            If chooseDestination Then destinationLocation = ChooseManagerDestination()
            If (kind = ManagerCommandKind.Copy OrElse kind = ManagerCommandKind.Move) AndAlso destinationLocation Is Nothing Then Return
            source = Await CaptureManagerEndpoint(location, capturedBrowser)
            If destinationLocation IsNot Nothing Then destination = Await CaptureManagerEndpoint(destinationLocation, capturedBrowser)
            If IsDisposed OrElse epoch <> manager_Epoch Then Return
            Dim prefs As ModernViewerPreferences = Main_Form.GetModernPreferences()
            Dim request As New ManagerCommandRequest(kind, location, generation, entries, source, destination,
                If(destinationLocation Is Nothing, "", destinationLocation.DirectoryPath), leaf, prefs.NameCollisionPolicy, prefs.ConfirmDelete, prefs.DeleteToRecycleBin)
            source = Nothing
            destination = Nothing
            If Not Await Main_Form.StartManagerCommandAsync(Me, request) AndAlso Not IsDisposed Then
                ShowStrip(Main_Form.StatusLineText, False, False, True)
            End If
        Catch ex As Exception
            AppFileLogger.LogException("Manager command", ex)
            If Not IsDisposed Then ShowStrip(FailureText.Describe(ex), False, False, True)
        Finally
            source?.Dispose()
            destination?.Dispose()
            manager_Command_Starting = False
            If Not IsDisposed Then UpdateManagerCommands()
        End Try
    End Sub

    Private Async Sub CaptureManagerSelection(cut As Boolean)
        If Not ManagerAllows(If(cut, ManagerCommandKind.Move, ManagerCommandKind.Copy)) Then Return
        Dim entries As List(Of ManagerEntry) = SelectedManagerEntries()
        Dim location As ManagerLocation = nav.Current
        Dim generation As Integer = browser.Generation
        Dim epoch As Integer = manager_Epoch
        Dim capturedBrowser As ManagerBrowser = browser
        Dim source As ManagerEndpointLease = Nothing
        manager_Command_Starting = True
        Try
            source = Await CaptureManagerEndpoint(location, capturedBrowser)
            If IsDisposed OrElse epoch <> manager_Epoch Then Return
            selection_Clipboard?.Dispose()
            selection_Clipboard = New ManagerSelectionClipboard(location, generation, entries, source, cut)
            source = Nothing
        Catch ex As Exception
            AppFileLogger.LogException("Manager selection capture", ex)
            If Not IsDisposed Then ShowStrip(FailureText.Describe(ex), False, False, True)
        Finally
            source?.Dispose()
            manager_Command_Starting = False
            If Not IsDisposed Then UpdateManagerCommands()
        End Try
    End Sub

    Private Async Sub PasteManagerSelection()
        If selection_Clipboard Is Nothing OrElse manager_Command_Starting OrElse Main_Form.BatchBusy() OrElse Not ManagerDestinationWritable() Then Return
        Dim clipboard As ManagerSelectionClipboard = selection_Clipboard
        Dim location As ManagerLocation = nav.Current
        Dim epoch As Integer = manager_Epoch
        Dim capturedBrowser As ManagerBrowser = browser
        Dim destination As ManagerEndpointLease = Nothing
        manager_Command_Starting = True
        Try
            destination = Await CaptureManagerEndpoint(location, capturedBrowser)
            If IsDisposed OrElse epoch <> manager_Epoch OrElse clipboard IsNot selection_Clipboard Then Return
            Dim request As ManagerCommandRequest = clipboard.Paste(destination, location.DirectoryPath, Main_Form.GetModernPreferences().NameCollisionPolicy)
            destination = Nothing
            If Not Await Main_Form.StartManagerCommandAsync(Me, request) AndAlso Not IsDisposed Then ShowStrip(Main_Form.StatusLineText, False, False, True)
        Catch ex As Exception
            AppFileLogger.LogException("Manager paste", ex)
            If Not IsDisposed Then ShowStrip(FailureText.Describe(ex), False, False, True)
        Finally
            destination?.Dispose()
            manager_Command_Starting = False
            If Not IsDisposed Then UpdateManagerCommands()
        End Try
    End Sub

    Private Sub ManagerSlot(e As KeyEventArgs, slot As Integer, topRow As Boolean)
        Dim slotIndex As Integer = If(slot = 0, 10, slot)
        Dim resolved As ManagerAddressResult = ManagerAddress.Resolve(Hardkeys_to_move_mediafile(slotIndex), resource_List)
        If resolved.Kind <> ManagerAddressKind.Ok Then
            ShowStrip(Localization.T("Папка назначения недоступна"), False, False, True)
            Return
        End If
        RunManagerCommand(If(e.Shift AndAlso topRow, ManagerCommandKind.Copy, ManagerCommandKind.Move), destinationLocation:=resolved.Location)
    End Sub

    Private Async Sub ShowManagerProperties()
        Dim entries As List(Of ManagerEntry) = SelectedManagerEntries()
        If entries.Count <> 1 Then Return
        Dim entry As ManagerEntry = entries(0)
        If Not entry.IsDirectory AndAlso Not nav.Current.Place.IsRemotePosix Then
            Using details As New File_Details_Form(entry.DiskPath)
                DialogLayer.ShowViewerDialog(details, Me)
            End Using
            Return
        End If
        Dim lease As ManagerEndpointLease = Nothing
        Dim location As ManagerLocation = nav.Current
        Dim epoch As Integer = manager_Epoch
        Try
            lease = Await CaptureManagerEndpoint(location, browser)
            Dim metadata As ResourceEntry = Await Task.Run(Function() ManagerTreePlan.RequireEntry(lease, entry.ProviderPath))
            If IsDisposed OrElse epoch <> manager_Epoch Then Return
            Dim size As String = If(metadata.IsDirectory, Localization.T("Неизвестно"), PanelText.FormatSize(metadata.Length))
            AskDialogs.Tell(Me, Localization.T("Свойства"), Localization.TF("{0}: {1}", entry.Name,
                metadata.FullPath & Environment.NewLine & size & Environment.NewLine & PanelText.FormatDate(metadata.LastWriteUtc.ToLocalTime(), True)), AskIcon.None)
        Catch ex As Exception
            AppFileLogger.LogException("Manager properties", ex)
            If Not IsDisposed AndAlso epoch = manager_Epoch Then ShowStrip(FailureText.Describe(ex), False, False, True)
        Finally
            lease?.Dispose()
        End Try
    End Sub

    Private Sub RevealManagerEntry()
        Dim entries As List(Of ManagerEntry) = SelectedManagerEntries()
        If entries.Count <> 1 OrElse nav.Current.Place.IsRemotePosix Then Return
        Process.Start(New ProcessStartInfo("explorer.exe") With {.UseShellExecute = True,
            .Arguments = "/select,""" & entries(0).ProviderPath & """"})
    End Sub

    Private Sub OnManagerLocationChanged(sender As Object, e As ManagerLocationChangedEventArgs)
        If manager_Mode AndAlso nav?.Current IsNot Nothing AndAlso nav.Current.IsAffectedBy(e.Location, e.IncludeDescendants) Then manager_Invalidated = True
    End Sub

    Private Sub ClearManagerClipboard()
        selection_Clipboard?.Dispose()
        selection_Clipboard = Nothing
        UpdateManagerCommands()
    End Sub
End Class
#End If
