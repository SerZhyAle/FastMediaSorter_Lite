#If Not NETFRAMEWORK Then
Option Strict On

Imports System.IO
Imports System.Threading

''' <summary>
''' The write path between this machine and a resource
''' (006_SPECIFICATION_FILESYSTEM_PROVIDER_LAYER_DOTNET10.md PV-5). One rule, borrowed from
''' FileManager's staged move: <b>a destination file never exists in an incomplete state
''' under its final name</b>, and a source is deleted only after its copy has been verified.
'''
''' Verification is by size. The listing's modification time is not compared: a provider
''' that does not preserve it on upload would fail every honest transfer, and one that
''' does adds nothing a size mismatch would not already catch for a truncation.
'''
''' A failed upload leaves its remote ".fms-part" where it is and throws: a retry starts
''' over rather than resuming, because resuming without verifying is how a truncated file
''' gets a final name.
''' </summary>
Friend Module ResourceTransfer

    Private Const HResultFileExists As Integer = &H80070050

    Private Function DestinationExists() As IOException
        Return New IOException("The destination file already exists.", HResultFileExists)
    End Function

    ''' <summary>Uploads <paramref name="localSource"/> to <paramref name="remoteDest"/>
    ''' through "remoteDest.fms-part", verified, then renamed into place.</summary>
    Friend Sub UploadVerified(provider As IFileSystemProvider, localSource As String, remoteDest As String,
                              overwrite As Boolean, Optional progress As IProgress(Of Long) = Nothing,
                              Optional ct As CancellationToken = Nothing)
        Dim expected As Long = New FileInfo(localSource).Length
        Dim destinationExisted As Boolean = ProbeExists(provider, remoteDest, Nothing)
        ' Refused before a byte is sent: finding the collision after 2 GB would waste them.
        If destinationExisted AndAlso Not overwrite Then Throw DestinationExists()

        Dim part As String = remoteDest & FileManager.Partial_Move_Suffix
        provider.Upload(localSource, part, progress, ct)
        RequireLength(provider, part, expected)

        If destinationExisted Then provider.DeleteFile(remoteDest)
        provider.Rename(part, remoteDest)
        RequireLength(provider, remoteDest, expected)
    End Sub

    ''' <summary>Downloads <paramref name="remoteSource"/> to <paramref name="localDest"/>
    ''' through "localDest.fms-part", verified against the remote size, then renamed.</summary>
    Friend Sub DownloadVerified(provider As IFileSystemProvider, remoteSource As String, localDest As String,
                                overwrite As Boolean, Optional progress As IProgress(Of Long) = Nothing,
                                Optional ct As CancellationToken = Nothing)
        Dim source As ResourceEntry = Nothing
        If Not ProbeExists(provider, remoteSource, source) Then
            Throw New FileNotFoundException("The file is not on the resource any more.", remoteSource)
        End If
        If Not overwrite AndAlso File.Exists(localDest) Then Throw DestinationExists()

        Dim part As String = localDest & FileManager.Partial_Move_Suffix
        Try
            provider.Download(remoteSource, part, progress, ct)
            Dim arrived As Long = New FileInfo(part).Length
            If source.Length >= 0 AndAlso arrived <> source.Length Then
                Throw New IOException("Downloaded " & arrived.ToString() & " of " & source.Length.ToString() & " bytes.")
            End If
            File.Move(part, localDest, overwrite)
        Catch
            ' Locally the debris is ours to take: nothing else will ever sweep this folder.
            Try
                If File.Exists(part) Then File.Delete(part)
            Catch
            End Try
            Throw
        End Try
    End Sub

    ''' <summary>A move INTO a resource: upload, verify, and only then delete the local
    ''' source. Never delete-then-upload.</summary>
    Friend Sub MoveInto(provider As IFileSystemProvider, localSource As String, remoteDest As String,
                        overwrite As Boolean, Optional progress As IProgress(Of Long) = Nothing,
                        Optional ct As CancellationToken = Nothing)
        UploadVerified(provider, localSource, remoteDest, overwrite, progress, ct)
        Dim attributes As FileAttributes = File.GetAttributes(localSource)
        If (attributes And FileAttributes.ReadOnly) = FileAttributes.ReadOnly Then
            File.SetAttributes(localSource, attributes And Not FileAttributes.ReadOnly)
        End If
        File.Delete(localSource)
    End Sub

    ''' <summary>A move OUT OF a resource: download, verify, then delete the remote source.</summary>
    Friend Sub MoveOutOf(provider As IFileSystemProvider, remoteSource As String, localDest As String,
                         overwrite As Boolean, Optional progress As IProgress(Of Long) = Nothing,
                         Optional ct As CancellationToken = Nothing)
        DownloadVerified(provider, remoteSource, localDest, overwrite, progress, ct)
        provider.DeleteFile(remoteSource)
    End Sub

    ''' <summary>
    ''' A move WITHIN one resource: a server-side rename when the provider has one, otherwise
    ''' the download/upload pair through <paramref name="stagingDir"/> - and the source goes
    ''' only after the upload is verified.
    ''' </summary>
    Friend Sub MoveWithin(provider As IFileSystemProvider, remoteSource As String, remoteDest As String,
                          overwrite As Boolean, stagingDir As String,
                          Optional progress As IProgress(Of Long) = Nothing,
                          Optional ct As CancellationToken = Nothing)
        ' "Replace" must never mean "replace the file with itself" (FileManager's C-02 guard,
        ' which the routing is asked BEFORE). Both branches below delete the destination or
        ' the source once the other side is in place, and with the two being one file that
        ' is the only copy. Case-insensitive: the worker serves a Windows folder, where
        ' "a.jpg" and "A.jpg" are one file (057.S07-3).
        If overwrite AndAlso String.Equals(remoteSource, remoteDest, StringComparison.OrdinalIgnoreCase) Then
            Throw New IOException("The source and the destination are the same file; nothing was replaced.")
        End If

        ' Case-only rename support (057.S07-7): allow renaming when paths differ only in case
        ' on case-insensitive servers, even when overwrite=False.
        Dim isCaseOnlyRename As Boolean = Not String.Equals(remoteSource, remoteDest, StringComparison.Ordinal) AndAlso
                                       String.Equals(remoteSource, remoteDest, StringComparison.OrdinalIgnoreCase)

        If provider.Capabilities.SupportsServerSideRename Then
            Dim existing As ResourceEntry = Nothing
            If ProbeExists(provider, remoteDest, existing) Then
                If Not overwrite AndAlso Not isCaseOnlyRename Then
                    Throw DestinationExists()
                End If
                If Not overwrite Then
                    ' Case-only rename: the destination is the source itself only when the server's
                    ' canonical spelling of it IS the source path (IFileSystemProvider.TryStat). A
                    ' case-sensitive server answers with the destination's own spelling - a different
                    ' file, a collision. Folding case here would take it for the source.
                    If isCaseOnlyRename AndAlso existing IsNot Nothing AndAlso
                       String.Equals(existing.FullPath, remoteSource, StringComparison.Ordinal) Then
                        provider.Rename(remoteSource, remoteDest)
                        Return
                    End If
                    Throw DestinationExists()
                End If
                provider.DeleteFile(remoteDest)
            End If
            provider.Rename(remoteSource, remoteDest)
            Return
        End If

        Directory.CreateDirectory(stagingDir)
        Dim staged As String = Path.Combine(stagingDir, "move-" & Guid.NewGuid().ToString("N") & ".tmp")
        Try
            ' For case-only rename, delete the destination first if it exists (it's the same file)
            If isCaseOnlyRename Then
                Try
                    provider.DeleteFile(remoteDest)
                Catch
                    ' Ignore delete failure for case-only rename - the upload will handle it
                End Try
            End If
            DownloadVerified(provider, remoteSource, staged, True, progress, ct)
            UploadVerified(provider, staged, remoteDest, overwrite OrElse isCaseOnlyRename, progress, ct)
            provider.DeleteFile(remoteSource)
        Finally
            Try
                If File.Exists(staged) Then File.Delete(staged)
            Catch
            End Try
        End Try
    End Sub

    Private Sub RequireLength(provider As IFileSystemProvider, remotePath As String, expected As Long)
        Dim landed As ResourceEntry = Nothing
        If Not ProbeExists(provider, remotePath, landed) Then
            Throw New IOException("The uploaded file is not on the resource: " & remotePath)
        End If
        If landed.Length >= 0 AndAlso landed.Length <> expected Then
            Throw New IOException("Uploaded " & landed.Length.ToString() & " of " & expected.ToString() & " bytes.")
        End If
    End Sub

    ''' <summary>
    ''' "Is it there" with the typed answer (100 §15.4 item 3): True for Found, False ONLY for
    ''' a definite Missing, and a permission refusal or a dead link throws its
    ''' <see cref="ResourceFailureException"/>. An overwrite check, a verification and a
    ''' "gone from the server" verdict must never rest on an error read as absence.
    ''' </summary>
    Private Function ProbeExists(provider As IFileSystemProvider, path As String, ByRef entry As ResourceEntry) As Boolean
        Dim result As StatResult = ResourceProviderCalls.Stat(provider, path)
        entry = result.Entry
        Select Case result.Outcome
            Case StatOutcome.Found
                Return True
            Case StatOutcome.Missing
                Return False
            Case Else
                Throw New ResourceFailureException(result.Failure)
        End Select
    End Function

    ' ------------------------------------------------------------ provider paths ----

    ''' <summary>The folder part of a provider path. A remote path uses "/", a UNC one "\";
    ''' whichever the path itself carries is the one used. Pure.</summary>
    Friend Function ParentOf(remotePath As String) As String
        If String.IsNullOrEmpty(remotePath) Then Return ""
        Dim cut As Integer = remotePath.LastIndexOfAny(New Char() {"/"c, "\"c})
        If cut < 0 Then Return ""
        If cut = 0 Then Return remotePath.Substring(0, 1)
        Return remotePath.Substring(0, cut)
    End Function

    ''' <summary><paramref name="name"/> inside <paramref name="parent"/>, with the parent's
    ''' own separator. Pure.</summary>
    Friend Function Combine(parent As String, name As String) As String
        If String.IsNullOrEmpty(parent) Then Return name
        Dim separator As Char = If(parent.Contains("/"c) OrElse Not parent.Contains("\"c), "/"c, "\"c)
        If parent.EndsWith("/", StringComparison.Ordinal) OrElse parent.EndsWith("\", StringComparison.Ordinal) Then
            Return parent & name
        End If
        Return parent & separator & name
    End Function

    ''' <summary>The file name part of a provider path. Pure.</summary>
    Friend Function FileNameOf(remotePath As String) As String
        If String.IsNullOrEmpty(remotePath) Then Return ""
        Dim cut As Integer = remotePath.LastIndexOfAny(New Char() {"/"c, "\"c})
        If cut < 0 Then Return remotePath
        Return remotePath.Substring(cut + 1)
    End Function

End Module
#End If
