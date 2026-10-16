Option Explicit On
Option Strict On
Option Infer On
Option Compare Binary

Imports System.Collections.Generic
Imports System.IO
Imports System.Security.Cryptography
Imports System.Threading

' The two ends of an operation (ticket 222, 4.3), behind one seam.
'
' A side is "a folder I can list, check and give a name to a file in". The executor never asks whether
' it is a local disk or a server: that is the point of the provider layer the viewer already has
' (IFileSystemProvider), and the reason a registered resource is a second implementation of this
' interface rather than a branch inside the engine. The local-path side is complete here; the resource
' side is the Share Manager linking src\Resources (step 4 of the plan).

''' <summary>Why a side cannot be used. Each member is a condition a retry cannot fix by itself (6),
''' except <see cref="SideProblemKind.Offline"/> which skips the run (ticket 231, 3.5).</summary>
Friend Enum SideProblemKind
    None = 0
    ''' <summary>The folder is missing or the drive or share does not answer.</summary>
    Unreachable = 1
    ''' <summary>The signed-in user may not list or write there.</summary>
    Denied = 2
    ''' <summary>A resource whose password is not stored: the run is unattended and asks nothing (4.3).</summary>
    NoCredential = 3
    ''' <summary>A resource whose host key is not pinned, or is not the pin: never accepted sourcePath here (4.3).</summary>
    KeyNotPinned = 4
    ''' <summary>The resource is no longer in the registry (4.3): the operation is halted, not deleted.</summary>
    ResourceRemoved = 5
    ''' <summary>The device token was rejected as revoked by the exchange server (ticket 231, 3.1).</summary>
    DeviceRevoked = 6
    ''' <summary>The exchange server's TLS certificate does not match its pinned fingerprint (ticket 231, 3.1).</summary>
    ExchangeCertificateChanged = 7
    ''' <summary>The exchange entry or Google account is signed out (ticket 231, 3.1/3.2).</summary>
    SignedOut = 8
    ''' <summary>The resource requires a PIN that is missing or has changed since it was stored (ticket 231, 3.3).</summary>
    PinLocked = 9
    ''' <summary>The remote device, tunnel, or network is offline; the run is skipped (ticket 231, 3.5).</summary>
    Offline = 10
End Enum

Friend NotInheritable Class SideProblem
    Friend ReadOnly Kind As SideProblemKind
    ''' <summary>What was being done and what the system said - machine text, never translated.</summary>
    Friend ReadOnly Detail As String

    Friend Sub New(kind As SideProblemKind, detail As String)
        Me.Kind = kind
        Me.Detail = If(detail, "")
    End Sub

    Friend Shared ReadOnly Ok As New SideProblem(SideProblemKind.None, "")

    Friend ReadOnly Property IsProblem As Boolean
        Get
            Return Kind <> SideProblemKind.None
        End Get
    End Property
End Class

Friend Interface IScheduledSide
    Inherits IDisposable

    ReadOnly Property IsLocalPath As Boolean
    ''' <summary>The folder: a Windows path here, a server folder for a resource.</summary>
    ReadOnly Property Root As String
    ''' <summary>For notes and the journal.</summary>
    Function Describe() As String
    ''' <summary>Can the folder be read? Asked before anything is scanned.</summary>
    Function CheckReadable() As SideProblem
    ''' <summary>Can files be written into it? May create the folder when its parent exists. Asked before
    ''' anything is touched: this is the target pre-flight of section 6.</summary>
    Function CheckWritable() As SideProblem
    Function List(recursive As Boolean, ct As CancellationToken) As List(Of SideEntry)
    ''' <summary>The path or locator of <paramref name="relative"/> inside the folder.</summary>
    Function Combine(relative As String) As String
    Function Exists(relative As String) As Boolean
End Interface

''' <summary>How a move ended when it did not simply succeed.</summary>
Friend Enum MoveOutcome
    Moved = 0
    ''' <summary>Copied and verified, but the source was changed since the listing or could not be
    ''' removed: both copies stay and the run says so (the viewer's CopyOnly rule).</summary>
    CopiedSourceKept = 1
    ''' <summary>The source changed between the listing and the action: nothing was touched.</summary>
    SourceChanged = 2
End Enum

''' <summary>The byte-moving verbs between two sides. The executor decides WHAT; this does it.</summary>
Friend Interface IScheduledTransfer
    ''' <summary>Copies one file to <paramref name="target"/> at <paramref name="targetRelative"/>, never
    ''' leaving half a file under the final name. Returns the bytes written.</summary>
    Function CopyFile(source As IScheduledSide, entry As SideEntry, target As IScheduledSide,
                      targetRelative As String, overwrite As Boolean, ct As CancellationToken) As Long
    Function MoveFile(source As IScheduledSide, entry As SideEntry, target As IScheduledSide,
                      targetRelative As String, overwrite As Boolean, ct As CancellationToken) As MoveOutcome
    ''' <summary>Removes a file of a side that is NOT a local path - permanently, after proving it is still the
    ''' file that was listed - and returns the verdict ("permanent:..."). A local path is deleted by the executor
    ''' under the viewer's delete policy, because only there a Recycle Bin exists.</summary>
    Function DeleteFile(side As IScheduledSide, entry As SideEntry, ct As CancellationToken) As String
End Interface

''' <summary>A local drive or a UNC path, used under the signed-in user's own credentials.</summary>
Friend NotInheritable Class LocalSide
    Implements IScheduledSide

    Private ReadOnly folder As String

    ''' <param name="canonicalFolder">An absolute drive or UNC folder without a trailing separator.</param>
    Friend Sub New(canonicalFolder As String)
        folder = canonicalFolder
    End Sub

    Friend Shared Function ForLocation(location As ScheduledLocation) As LocalSide
        Dim canonical As String = CompanionCommand.CanonicalFolder(location.Path)
        If canonical.Length = 0 Then Throw New ArgumentException("Not an absolute folder: " & location.Path)
        Return New LocalSide(canonical)
    End Function

    Public ReadOnly Property IsLocalPath As Boolean Implements IScheduledSide.IsLocalPath
        Get
            Return True
        End Get
    End Property

    Public ReadOnly Property Root As String Implements IScheduledSide.Root
        Get
            Return folder
        End Get
    End Property

    Public Function Describe() As String Implements IScheduledSide.Describe
        Return folder
    End Function

    Public Sub Dispose() Implements IDisposable.Dispose
    End Sub

    Public Function Combine(relative As String) As String Implements IScheduledSide.Combine
        Return System.IO.Path.Combine(folder, relative)
    End Function

    Public Function Exists(relative As String) As Boolean Implements IScheduledSide.Exists
        Try
            Return System.IO.File.Exists(Combine(relative))
        Catch
            Return False
        End Try
    End Function

    Public Function CheckReadable() As SideProblem Implements IScheduledSide.CheckReadable
        Try
            Dim directory As New DirectoryInfo(folder)
            ' Asking for one entry makes the system name the reason (not found, denied, no answer) instead
            ' of Exists folding every one of them into False.
            For Each ignored As FileSystemInfo In directory.EnumerateFileSystemInfos("*", SearchOption.TopDirectoryOnly)
                Exit For
            Next
            If Not directory.Exists Then Return New SideProblem(SideProblemKind.Unreachable, folder)
            Return SideProblem.Ok
        Catch ex As Exception
            AppFileLogger.LogException("Scheduled operations: reading " & folder, ex)
            Return FromException(ex, folder)
        End Try
    End Function

    Public Function CheckWritable() As SideProblem Implements IScheduledSide.CheckWritable
        Try
            If Not Directory.Exists(folder) Then
                ' A missing folder is created when its PARENT exists (a new "2026" folder under a backup
                ' drive); a drive that is not there is not conjured up.
                Dim parent As String = System.IO.Path.GetDirectoryName(folder)
                If String.IsNullOrEmpty(parent) OrElse Not Directory.Exists(parent) Then
                    Return New SideProblem(SideProblemKind.Unreachable, folder)
                End If
                Directory.CreateDirectory(folder)
            End If
            ' A real write: an ACL answers "denied" only to a write.
            Dim probe As String = System.IO.Path.Combine(folder, ".fms-write-test-" & Guid.NewGuid().ToString("N") & ".tmp")
            Using stream As New FileStream(probe, FileMode.CreateNew, FileAccess.Write, FileShare.None, 1, FileOptions.DeleteOnClose)
                stream.WriteByte(0)
            End Using
            Return SideProblem.Ok
        Catch ex As Exception
            AppFileLogger.LogException("Scheduled operations: writing to " & folder, ex)
            Return FromException(ex, folder)
        End Try
    End Function

    ''' <summary>The problem, in kind and place only - what the system said is in the log.</summary>
    Private Shared Function FromException(ex As Exception, place As String) As SideProblem
        If PathFailure.Classify(ex) = PathFailure.PathFailureKind.Denied Then
            Return New SideProblem(SideProblemKind.Denied, place)
        End If
        Return New SideProblem(SideProblemKind.Unreachable, place)
    End Function

    Public Function List(recursive As Boolean, ct As CancellationToken) As List(Of SideEntry) Implements IScheduledSide.List
        Dim files As List(Of FileInfo) = FolderScan.EnumerateFiles(folder, recursive, ct)
        Dim result As New List(Of SideEntry)(files.Count)
        Dim prefixLength As Integer = folder.TrimEnd("\"c).Length + 1
        ' A file under a hidden or system FOLDER is hidden to its user all the same (".git" is the
        ' everyday case); each folder is asked once.
        Dim hiddenFolders As New Dictionary(Of String, Boolean)(StringComparer.OrdinalIgnoreCase)
        For Each info As FileInfo In files
            ct.ThrowIfCancellationRequested()
            Dim relative As String = If(info.FullName.Length > prefixLength, info.FullName.Substring(prefixLength), info.Name)
            Dim hidden As Boolean = (info.Attributes And (FileAttributes.Hidden Or FileAttributes.System)) <> 0
            If Not hidden Then hidden = IsUnderHiddenFolder(info.DirectoryName, folder, hiddenFolders)
            result.Add(New SideEntry With {.RelativePath = relative, .Name = info.Name, .Length = info.Length,
                .LastWriteUtc = info.LastWriteTimeUtc, .IsHiddenOrSystem = hidden})
        Next
        Return result
    End Function

    Private Shared Function IsUnderHiddenFolder(directory As String, root As String, cache As Dictionary(Of String, Boolean)) As Boolean
        Dim current As String = directory
        Dim rootTrimmed As String = root.TrimEnd("\"c)
        While Not String.IsNullOrEmpty(current) AndAlso current.Length > rootTrimmed.Length
            Dim hidden As Boolean
            If Not cache.TryGetValue(current, hidden) Then
                Try
                    hidden = (File.GetAttributes(current) And (FileAttributes.Hidden Or FileAttributes.System)) <> 0
                Catch
                    hidden = False
                End Try
                cache(current) = hidden
            End If
            If hidden Then Return True
            current = System.IO.Path.GetDirectoryName(current)
        End While
        Return False
    End Function

End Class

''' <summary>Local-to-local transfers: a temporary name, a size check, then the final name - so no
''' failure leaves half a file where a whole one was expected.</summary>
Friend NotInheritable Class LocalTransfer
    Implements IScheduledTransfer

    ''' <summary>The marker before the extension would let a format guess go wrong; this one is a suffix
    ''' of the FINAL name, so a leftover is recognisable and swept by the next attempt at the same name.</summary>
    Friend Const PartSuffix As String = FileManager.Partial_Move_Suffix

    ''' <summary>Injectable volume predicate so cross-volume move verification is provable in a unit test on one disk.</summary>
    Friend Property SameVolumePredicate As Func(Of String, String, Boolean) = AddressOf SameVolume
    ''' <summary>Test hook called after the destination file is published and before move verification runs.</summary>
    Friend Property AfterCopyPublished As Action(Of String)

    Public Function CopyFile(source As IScheduledSide, entry As SideEntry, target As IScheduledSide,
                             targetRelative As String, overwrite As Boolean, ct As CancellationToken) As Long Implements IScheduledTransfer.CopyFile
        Dim sourcePath As String = source.Combine(entry.RelativePath)
        Dim finalPath As String = target.Combine(targetRelative)
        Dim temp As String = finalPath & PartSuffix
        Directory.CreateDirectory(System.IO.Path.GetDirectoryName(finalPath))
        If System.IO.File.Exists(temp) Then System.IO.File.Delete(temp)
        ct.ThrowIfCancellationRequested()
        Try
            System.IO.File.Copy(sourcePath, temp, True)
            Dim written As Long = New FileInfo(temp).Length
            ' The source must still be the file that was listed, and the copy the whole of it.
            If Not SameAsListed(sourcePath, entry) Then Throw New SourceChangedException(sourcePath)
            If written <> entry.Length Then Throw New InvalidDataException("The copy of " & sourcePath & " is " & written & " bytes, the file is " & entry.Length & ".")
            If overwrite Then
                System.IO.File.Move(temp, finalPath, True)
            Else
                System.IO.File.Move(temp, finalPath)
            End If
            Return written
        Catch
            Try
                If System.IO.File.Exists(temp) Then System.IO.File.Delete(temp)
            Catch
            End Try
            Throw
        End Try
    End Function

    Public Function MoveFile(source As IScheduledSide, entry As SideEntry, target As IScheduledSide,
                             targetRelative As String, overwrite As Boolean, ct As CancellationToken) As MoveOutcome Implements IScheduledTransfer.MoveFile
        Dim sourcePath As String = source.Combine(entry.RelativePath)
        Dim finalPath As String = target.Combine(targetRelative)
        If Not SameAsListed(sourcePath, entry) Then Return MoveOutcome.SourceChanged

        Dim sameVol As Boolean = If(SameVolumePredicate, New Func(Of String, String, Boolean)(AddressOf SameVolume)).Invoke(sourcePath, finalPath)
        If sameVol AndAlso AfterCopyPublished Is Nothing Then
            ' One rename, no byte moves, atomic.
            Directory.CreateDirectory(System.IO.Path.GetDirectoryName(finalPath))
            If overwrite Then
                System.IO.File.Move(sourcePath, finalPath, True)
            Else
                System.IO.File.Move(sourcePath, finalPath)
            End If
            Return MoveOutcome.Moved
        End If

        ' Across volumes: copy, verify size + SHA-256 content hash, and remove the source only if it matches and is still what was copied (ticket 231, 3.6).
        Dim expectedHash As Byte() = HashOfFile(sourcePath)
        Dim written As Long
        Try
            written = CopyFile(source, entry, target, targetRelative, overwrite, ct)
        Catch ex As SourceChangedException
            Return MoveOutcome.SourceChanged
        End Try
        AfterCopyPublished?.Invoke(finalPath)
        If Not SameAsListed(sourcePath, entry) Then Return MoveOutcome.CopiedSourceKept
        If written <> entry.Length OrElse Not VerifiedDestination(finalPath, entry.Length, expectedHash) Then
            Throw New ScheduledTransferException("mismatch")
        End If
        Try
            System.IO.File.Delete(sourcePath)
        Catch ex As Exception When TypeOf ex Is IOException OrElse TypeOf ex Is UnauthorizedAccessException
            Return MoveOutcome.CopiedSourceKept
        End Try
        Return MoveOutcome.Moved
    End Function

    Private Shared Function VerifiedDestination(finalPath As String, expectedLength As Long, expectedHash As Byte()) As Boolean
        Try
            Dim info As New FileInfo(finalPath)
            If Not info.Exists OrElse info.Length <> expectedLength Then Return False
            Dim actualHash As Byte() = HashOfFile(finalPath)
            Return CryptographicOperations.FixedTimeEquals(expectedHash, actualHash)
        Catch
            Return False
        End Try
    End Function

    Private Shared Function HashOfFile(path As String) As Byte()
        Using stream As New FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite Or FileShare.Delete, 81920)
            Using sha As SHA256 = SHA256.Create()
                Return sha.ComputeHash(stream)
            End Using
        End Using
    End Function

    Public Function DeleteFile(side As IScheduledSide, entry As SideEntry, ct As CancellationToken) As String Implements IScheduledTransfer.DeleteFile
        Throw New NotSupportedException("A local path is deleted by the executor, under the delete policy.")
    End Function

    ''' <summary>Is the file still the one the scan listed - same size, same write time?</summary>
    Friend Shared Function SameAsListed(path As String, entry As SideEntry) As Boolean
        Try
            Dim info As New FileInfo(path)
            Return info.Exists AndAlso info.Length = entry.Length AndAlso info.LastWriteTimeUtc = entry.LastWriteUtc
        Catch
            Return False
        End Try
    End Function

    ''' <summary>The same volume: a drive letter's root, or a UNC server and share.</summary>
    Friend Shared Function SameVolume(a As String, b As String) As Boolean
        Dim rootA As String = DeleteVolumeProbe.VolumeRootOf(a)
        Dim rootB As String = DeleteVolumeProbe.VolumeRootOf(b)
        Return rootA.Length > 0 AndAlso String.Equals(rootA, rootB, StringComparison.OrdinalIgnoreCase)
    End Function

End Class

''' <summary>The source changed after it was listed: it stays where it is and the run says so.</summary>
Friend NotInheritable Class SourceChangedException
    Inherits IOException

    Friend Sub New(path As String)
        MyBase.New("The source changed after it was listed: " & path)
    End Sub
End Class
