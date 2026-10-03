#If Not NETFRAMEWORK Then
Option Strict On

Imports System.IO
Imports System.Threading

''' <summary>
''' Where FileManager's copy / move / delete / rename meet the provider layer
''' (006_SPECIFICATION_FILESYSTEM_PROVIDER_LAYER_DOTNET10.md PV-4, PV-5).
'''
''' Each FileManager verb asks here first, and gets False - "not mine, carry on exactly as
''' before" - unless one side of the operation is a materialised entry of the open
''' resource. With only local folders there is never such an entry (LocalProvider
''' materialises nothing), so every call returns False on its first line and the local
''' file operations are the same System.IO calls they always were.
'''
''' When a side IS a resource entry, the file list's path is only the local copy: the verb
''' is carried to the provider through ResourceTransfer, so the remote file is what moves,
''' and the cached copy is invalidated afterwards so nothing shows the old bytes.
''' </summary>
Friend Module ResourceRouting

    Private attached_Session As ResourceSession

    ''' <summary>Called by ResourceSessionSlot when a resource opens.</summary>
    Friend Sub Attach(session As ResourceSession)
        Volatile.Write(attached_Session, session)
    End Sub

    ''' <summary>Called by ResourceSessionSlot when it closes - before the session is
    ''' disposed, so no operation can reach a closing provider through here.</summary>
    Friend Sub Detach(session As ResourceSession)
        Interlocked.CompareExchange(attached_Session, Nothing, session)
    End Sub

    ''' <summary>The open session when it owns <paramref name="localPath"/> and has to
    ''' materialise; Nothing otherwise - including always, for a local folder.</summary>
    Private Function OwnerOf(localPath As String) As ResourceSession
        Dim session As ResourceSession = Volatile.Read(attached_Session)
        If session Is Nothing OrElse Not session.NeedsMaterialisation Then Return Nothing
        If Not session.OwnsLocalPath(localPath) Then Return Nothing
        Return session
    End Function

    ''' <summary>Makes sure the local copy is complete before anything reads it, and says
    ''' so honestly when it cannot be.</summary>
    Private Sub RequireLocal(session As ResourceSession, localPath As String)
        Select Case session.TryEnsureLocal(localPath)
            Case EnsureLocalOutcome.Ready, EnsureLocalOutcome.NotOurs
                Return
            Case EnsureLocalOutcome.Cancelled
                Throw New OperationCanceledException("The resource was closed while the file was being fetched.")
            Case Else
                Throw New IOException("The file could not be fetched from the resource.")
        End Select
    End Sub

    ' ------------------------------------------------ the staged transfer (100 T10) ----

    ''' <summary>A plain-file endpoint for transfers whose other side is not a resource: the
    ''' local file system behind the same provider seam. Its capabilities carry the stable-
    ''' source-delete guarantee a Windows file has always had - re-check, then delete - which
    ''' is exactly what an arbitrary SFTP resource does not.</summary>
    Private ReadOnly local_Transfer_Provider As New LocalProvider(Function() False)

    ''' <summary>Where the bounded read-back copies live. On this machine, outside the cache
    ''' session: a transfer must not depend on files the session sweep may take while it runs.
    ''' The executor creates it and takes its own GUID-named files away.</summary>
    Friend ReadOnly TransferScratchDir As String = Path.Combine(Path.GetTempPath(), "fms-transfer-scratch")

    ''' <summary>The endpoint behind a file of the open resource: the provider's own path,
    ''' never the cache copy.</summary>
    Private Function EndpointOf(session As ResourceSession, localPath As String) As TransferEndpoint
        Dim entry As ResourceEntry = session.EntryAt(localPath)
        If entry Is Nothing Then Throw New FileNotFoundException("The file is not in the resource's listing any more.", localPath)
        Return New TransferEndpoint(session.Provider, entry.FullPath, If(session.Resource IsNot Nothing, session.Resource.Id, ""))
    End Function

    ''' <summary>The endpoint behind a server-folder file - a recipient slot that names a saved
    ''' resource (007 §4.2) - or Nothing for any other path. Connects through the slot connections
    ''' and throws the failure's class when it cannot.</summary>
    Private Function SlotEndpointOf(path As String) As TransferEndpoint
        If Not ResourceLocator.IsLocator(path) Then Return Nothing
        Dim slots As RemoteSlotConnections = RemoteSlotConnections.Current
        If slots Is Nothing Then
            Throw New ResourceFailureException(New ResourceFailure(ResourceFailureKind.Unsupported, Nothing, "no slot connections"))
        End If
        Return slots.Endpoint(path)
    End Function

    ''' <summary>One side of a transfer: a server-folder file, a file of the open resource, or a plain
    ''' file on this machine - in that order.</summary>
    Private Function SideOf(path As String, slot As TransferEndpoint, session As ResourceSession) As TransferEndpoint
        If slot IsNot Nothing Then Return slot
        If session IsNot Nothing Then Return EndpointOf(session, path)
        Return New TransferEndpoint(local_Transfer_Provider, path, "")
    End Function

    ''' <summary>
    ''' A copy or a move where at least one side is a server folder named by a slot: one staged,
    ''' content-verified plan, whatever the other side is (this PC, the open resource, or the same
    ''' server). The open resource's cached copies are invalidated afterwards, as for its own moves.
    ''' </summary>
    Private Function RunSlotTransfer(kind As TransferKind, sourceFile As String, destFile As String, overwrite As Boolean,
                                     fromSlot As TransferEndpoint, intoSlot As TransferEndpoint,
                                     ByRef outcome As TransferOutcome) As Boolean
        Dim fromResource As ResourceSession = If(fromSlot Is Nothing, OwnerOf(sourceFile), Nothing)
        Dim intoResource As ResourceSession = If(intoSlot Is Nothing, OwnerOf(destFile), Nothing)
        Dim plan As New TransferPlan(kind, SideOf(sourceFile, fromSlot, fromResource), SideOf(destFile, intoSlot, intoResource),
                                     ConflictOf(overwrite), TransferScratchDir)
        Dim ct As CancellationToken = If(fromResource IsNot Nothing, fromResource.Cancellation,
                                         If(intoResource IsNot Nothing, intoResource.Cancellation, CancellationToken.None))
        outcome = Executed(plan, ct)
        If fromResource IsNot Nothing AndAlso kind = TransferKind.Move Then fromResource.Invalidate(sourceFile)
        intoResource?.Invalidate(destFile)
        Return True
    End Function

    Private Function ConflictOf(overwrite As Boolean) As ConflictPolicy
        Return If(overwrite, ConflictPolicy.Replace, ConflictPolicy.Fail)
    End Function

    ''' <summary>Runs a plan and accepts it only when it completed. Everything else comes
    ''' back as the typed exception the completion path reads: a move that kept both copies,
    ''' an uncertain last step, a refusal or a plain failure - each saying what DID happen,
    ''' because "the move failed" alone would hide a copy that exists.</summary>
    Private Function Executed(plan As TransferPlan, ct As CancellationToken) As TransferOutcome
        Dim outcome As TransferOutcome = TransferExecutor.Execute(plan, Nothing, ct)
        If outcome.Result <> TransferResult.Completed Then
            Throw New TransferOutcomeException(outcome, DescribeOutcome(outcome))
        End If
        Return outcome
    End Function

    ''' <summary>What an outcome says, for the log and for the completion path's own
    ''' sentence. The two copies are named where there are two.</summary>
    Friend Function DescribeOutcome(outcome As TransferOutcome) As String
        If outcome Is Nothing Then Return "The transfer produced no outcome."
        Select Case outcome.Result
            Case TransferResult.CopyOnly
                Return "The copy is complete, the move is not: a copy of '" &
                       ResourceTransfer.FileNameOf(outcome.Plan.Source.Path) & "' is at '" & outcome.Plan.Destination.Path &
                       "' and the source was kept (" & outcome.Reason.ToString() & ")."
            Case TransferResult.Indeterminate
                Return "The last step of the transfer was lost in flight and could not be settled; both copies must be checked by hand."
            Case TransferResult.Refused
                Return "The transfer was refused: " & outcome.Reason.ToString() & "."
            Case Else
                Return "The transfer failed: " & outcome.Reason.ToString() & "."
        End Select
    End Function

    ''' <summary>
    ''' Copy. Out of a resource the only extra step is the fetch, so this materialises the
    ''' source and returns False - FileManager's own staged copy then takes it from the
    ''' cache, verified by the fetch. Into a resource it is the staged, content-verified
    ''' transfer (100 §14.2): SHA-256 of the source against a read-back of the stage,
    ''' published only once proven.
    ''' </summary>
    Friend Function TryCopy(sourceFile As String, destFile As String, overwrite As Boolean) As Boolean
        Dim ignored As TransferOutcome = Nothing
        Return TryCopy(sourceFile, destFile, overwrite, ignored)
    End Function

    Friend Function TryCopy(sourceFile As String, destFile As String, overwrite As Boolean,
                            ByRef outcome As TransferOutcome) As Boolean
        ' A server folder on either side (a remote recipient slot, 007 §4.2) - asked first, and
        ' answered False at once for two plain paths, which is every local copy.
        Dim intoSlot As TransferEndpoint = SlotEndpointOf(destFile)
        Dim fromSlot As TransferEndpoint = SlotEndpointOf(sourceFile)
        If intoSlot IsNot Nothing OrElse fromSlot IsNot Nothing Then
            Return RunSlotTransfer(TransferKind.Copy, sourceFile, destFile, overwrite, fromSlot, intoSlot, outcome)
        End If

        Dim fromResource As ResourceSession = OwnerOf(sourceFile)
        Dim intoResource As ResourceSession = OwnerOf(destFile)
        If fromResource Is Nothing AndAlso intoResource Is Nothing Then Return False

        If fromResource IsNot Nothing Then RequireLocal(fromResource, sourceFile)
        If intoResource Is Nothing Then Return False

        Dim source As TransferEndpoint = If(fromResource IsNot Nothing,
                                            EndpointOf(fromResource, sourceFile),
                                            New TransferEndpoint(local_Transfer_Provider, sourceFile, ""))
        Dim plan As New TransferPlan(TransferKind.Copy, source, EndpointOf(intoResource, destFile),
                                     ConflictOf(overwrite), TransferScratchDir)
        outcome = Executed(plan, intoResource.Cancellation)
        intoResource.Invalidate(destFile)
        Return True
    End Function

    ''' <summary>Move, in whichever direction a side is a resource entry. Every branch is
    ''' the staged state machine: copy -> verify by content -> publish -> (move only) a
    ''' source delete conditioned on the source still being the version that was copied.</summary>
    Friend Function TryMove(sourceFile As String, destFile As String, overwrite As Boolean) As Boolean
        Dim ignored As TransferOutcome = Nothing
        Return TryMove(sourceFile, destFile, overwrite, ignored)
    End Function

    Friend Function TryMove(sourceFile As String, destFile As String, overwrite As Boolean,
                            ByRef outcome As TransferOutcome) As Boolean
        ' Into a server folder (a remote slot) or back out of one (its undo): the same staged
        ' machine - copy, verify by content, publish, then delete the source only when it is
        ' provably the version that was copied.
        Dim intoSlot As TransferEndpoint = SlotEndpointOf(destFile)
        Dim fromSlot As TransferEndpoint = SlotEndpointOf(sourceFile)
        If intoSlot IsNot Nothing OrElse fromSlot IsNot Nothing Then
            Return RunSlotTransfer(TransferKind.Move, sourceFile, destFile, overwrite, fromSlot, intoSlot, outcome)
        End If

        Dim fromResource As ResourceSession = OwnerOf(sourceFile)
        Dim intoResource As ResourceSession = OwnerOf(destFile)
        If fromResource Is Nothing AndAlso intoResource Is Nothing Then Return False

        If fromResource IsNot Nothing AndAlso intoResource IsNot Nothing Then
            ' Within one resource: a server-side rename implements the move - never a copy.
            ' Without one, the same staged machine runs and the source goes only when the
            ' copy is proven and the source is provably stable.
            Dim plan As New TransferPlan(TransferKind.Move, EndpointOf(fromResource, sourceFile),
                                         EndpointOf(intoResource, destFile), ConflictOf(overwrite), TransferScratchDir)
            outcome = Executed(plan, fromResource.Cancellation)
            fromResource.Invalidate(sourceFile)
            intoResource.Invalidate(destFile)
            Return True
        End If

        If fromResource IsNot Nothing Then
            ' Out of the resource: the executor fetches and verifies by content itself - the
            ' cache copy is not part of the move any more - and the remote file is deleted
            ' only afterwards, and only if it is still the version that was copied. A source
            ' without a stable-delete guarantee keeps its file and the outcome says CopyOnly.
            Dim plan As New TransferPlan(TransferKind.Move, EndpointOf(fromResource, sourceFile),
                                         New TransferEndpoint(local_Transfer_Provider, destFile, ""),
                                         ConflictOf(overwrite), TransferScratchDir)
            outcome = Executed(plan, fromResource.Cancellation)
            fromResource.Invalidate(sourceFile)
            Return True
        End If

        ' Into the resource - an undo carrying a file back, typically.
        Dim plan_Into As New TransferPlan(TransferKind.Move, New TransferEndpoint(local_Transfer_Provider, sourceFile, ""),
                                          EndpointOf(intoResource, destFile), ConflictOf(overwrite), TransferScratchDir)
        outcome = Executed(plan_Into, intoResource.Cancellation)
        intoResource.Invalidate(destFile)
        Return True
    End Function

    ''' <summary>Delete: the remote file goes, then its local copy.</summary>
    Friend Function TryDelete(filePath As String) As Boolean
        ' A file in a server folder named by a slot (an undo of a copy made there).
        Dim slot As TransferEndpoint = SlotEndpointOf(filePath)
        If slot IsNot Nothing Then
            slot.Provider.DeleteFile(slot.Path)
            Return True
        End If

        Dim owner As ResourceSession = OwnerOf(filePath)
        If owner Is Nothing Then Return False
        owner.Provider.DeleteFile(owner.EntryAt(filePath).FullPath)
        owner.Invalidate(filePath)
        Return True
    End Function

    ''' <summary>Rename inside the entry's own remote folder. <paramref name="renamedPath"/>
    ''' is the entry's new LOCAL path - what the file list holds from now on.</summary>
    Friend Function TryRename(currentFileName As String, newFileNameWithExtension As String,
                              ByRef renamedPath As String) As Boolean
        renamedPath = currentFileName
        Dim owner As ResourceSession = OwnerOf(currentFileName)
        If owner Is Nothing Then Return False

        Dim entry As ResourceEntry = owner.EntryAt(currentFileName)
        Dim newRemote As String = ResourceTransfer.Combine(ResourceTransfer.ParentOf(entry.FullPath),
                                                           newFileNameWithExtension)
        If String.Equals(newRemote, entry.FullPath, StringComparison.Ordinal) Then Return True
        ResourceTransfer.MoveWithin(owner.Provider, entry.FullPath, newRemote, False,
                                    owner.SessionDir, ct:=owner.Cancellation)
        renamedPath = owner.Rekey(currentFileName, newRemote, newFileNameWithExtension)
        Return True
    End Function

End Module

''' <summary>
''' Which verbs a resource allows (PV-7) - the one decision behind every refusal, so the key,
''' the overlay and the menus cannot disagree. Pure.
''' </summary>
Friend Module ResourceRefusalPolicy

    Friend Function IsAllowed(capabilities As ProviderCapabilities, verb As ResourceVerb) As Boolean
        If capabilities Is Nothing Then Return True
        Select Case verb
            Case ResourceVerb.Copy
                ' Copying OUT only reads the source - a read-only resource can do that.
                Return True
            Case ResourceVerb.Move, ResourceVerb.Delete
                Return capabilities.CanDelete
            Case ResourceVerb.Rename
                Return capabilities.CanRename
            Case ResourceVerb.WriteBeside
                Return capabilities.CanWrite AndAlso capabilities.CanDelete
            Case Else
                Return False
        End Select
    End Function

End Module
#End If
