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

    ''' <summary>
    ''' Copy. Out of a resource the only extra step is the fetch, so this materialises the
    ''' source and returns False - FileManager's own staged copy then takes it from the
    ''' cache, verified by the fetch. Into a resource it is a verified upload.
    ''' </summary>
    Friend Function TryCopy(sourceFile As String, destFile As String, overwrite As Boolean) As Boolean
        Dim fromResource As ResourceSession = OwnerOf(sourceFile)
        Dim intoResource As ResourceSession = OwnerOf(destFile)
        If fromResource Is Nothing AndAlso intoResource Is Nothing Then Return False

        If fromResource IsNot Nothing Then RequireLocal(fromResource, sourceFile)
        If intoResource Is Nothing Then Return False

        Dim target As ResourceEntry = intoResource.EntryAt(destFile)
        ResourceTransfer.UploadVerified(intoResource.Provider, sourceFile, target.FullPath, overwrite,
                                        ct:=intoResource.Cancellation)
        intoResource.Invalidate(destFile)
        Return True
    End Function

    ''' <summary>Move, in whichever direction a side is a resource entry.</summary>
    Friend Function TryMove(sourceFile As String, destFile As String, overwrite As Boolean) As Boolean
        Dim fromResource As ResourceSession = OwnerOf(sourceFile)
        Dim intoResource As ResourceSession = OwnerOf(destFile)
        If fromResource Is Nothing AndAlso intoResource Is Nothing Then Return False

        If fromResource IsNot Nothing AndAlso intoResource IsNot Nothing Then
            Dim source As ResourceEntry = fromResource.EntryAt(sourceFile)
            Dim target As ResourceEntry = intoResource.EntryAt(destFile)
            ResourceTransfer.MoveWithin(fromResource.Provider, source.FullPath, target.FullPath, overwrite,
                                        fromResource.SessionDir, ct:=fromResource.Cancellation)
            fromResource.Invalidate(sourceFile)
            intoResource.Invalidate(destFile)
            Return True
        End If

        If fromResource IsNot Nothing Then
            ' Out of the resource: fetched and verified into the cache, copied from there by
            ' FileManager's staged copy, checked, and only THEN deleted on the resource.
            Dim source As ResourceEntry = fromResource.EntryAt(sourceFile)
            RequireLocal(fromResource, sourceFile)
            FileManager.CopyFile(sourceFile, destFile, overwrite)
            If source.Length >= 0 AndAlso New FileInfo(destFile).Length <> source.Length Then
                Throw New IOException("The copy does not match the resource's file; the original was left in place.")
            End If
            fromResource.Provider.DeleteFile(source.FullPath)
            fromResource.Invalidate(sourceFile)
            Return True
        End If

        ' Into the resource - an undo carrying a file back, typically.
        Dim destination As ResourceEntry = intoResource.EntryAt(destFile)
        ResourceTransfer.MoveInto(intoResource.Provider, sourceFile, destination.FullPath, overwrite,
                                  ct:=intoResource.Cancellation)
        intoResource.Invalidate(destFile)
        Return True
    End Function

    ''' <summary>Delete: the remote file goes, then its local copy.</summary>
    Friend Function TryDelete(filePath As String) As Boolean
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
