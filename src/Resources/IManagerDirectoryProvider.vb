#If Not NETFRAMEWORK Then
Option Strict On

Imports System.Collections.Generic
Imports System.Threading

' What the file manager mode of the F3 panel needs from a provider that the viewer's IFileSystemProvider does not
' offer (257_SPECIFICATION_FILE_MANAGER_NAVIGATION_AND_PROVIDER_CONTEXT.md, slice N1): the IMMEDIATE children of a
' directory, folders included, with a typed answer for "empty", "denied", "missing", "not a directory", "cut short"
' and "the link did not answer". IFileSystemProvider.ListEntries keeps its meaning for every viewer caller; this is a
' second, additive interface a provider implements next to it. Modern build only.

''' <summary>What listing one directory came to. Empty, denied and missing are different facts and never share a value.</summary>
Friend Enum DirectoryOutcome
    ''' <summary>The directory was read; an empty one is a Listed result with no entries.</summary>
    Listed = 0
    Missing = 1
    ''' <summary>The path exists and is a file (or anything that is not a directory).</summary>
    NotADirectory = 2
    ''' <summary>The server or the OS refused the question.</summary>
    Denied = 3
    ''' <summary>Transport, timeout or any other failure: no verdict about the directory itself.</summary>
    Failed = 4
    Cancelled = 5
End Enum

''' <summary>The immediate children of one directory, or the reason there are none to show. Immutable.</summary>
Friend NotInheritable Class DirectoryListing

    Private Sub New(outcome As DirectoryOutcome, entries As IReadOnlyList(Of ResourceEntry), truncated As Boolean, failure As ResourceFailure)
        Me.Outcome = outcome
        Me.Entries = If(entries, New List(Of ResourceEntry)())
        Me.IsTruncated = truncated
        Me.Failure = failure
    End Sub

    Friend ReadOnly Property Outcome As DirectoryOutcome
    Friend ReadOnly Property Entries As IReadOnlyList(Of ResourceEntry)
    ''' <summary>The provider stopped at its bound: what is here is a prefix of the directory, never the directory. A truncated
    ''' result is shown as incomplete and is never an operation manifest (257).</summary>
    Friend ReadOnly Property IsTruncated As Boolean
    Friend ReadOnly Property Failure As ResourceFailure

    ''' <summary>The directory was read to its end.</summary>
    Friend ReadOnly Property IsComplete As Boolean
        Get
            Return Outcome = DirectoryOutcome.Listed AndAlso Not IsTruncated
        End Get
    End Property

    Friend Shared Function Listed(entries As IReadOnlyList(Of ResourceEntry), Optional truncated As Boolean = False) As DirectoryListing
        Return New DirectoryListing(DirectoryOutcome.Listed, entries, truncated, Nothing)
    End Function

    Friend Shared Function Missing() As DirectoryListing
        Return New DirectoryListing(DirectoryOutcome.Missing, Nothing, False, Nothing)
    End Function

    Friend Shared Function NotADirectory() As DirectoryListing
        Return New DirectoryListing(DirectoryOutcome.NotADirectory, Nothing, False, Nothing)
    End Function

    Friend Shared Function Denied(Optional failure As ResourceFailure = Nothing) As DirectoryListing
        Return New DirectoryListing(DirectoryOutcome.Denied, Nothing, False, If(failure, New ResourceFailure(ResourceFailureKind.SftpStatus, 3)))
    End Function

    Friend Shared Function Failed(failure As ResourceFailure) As DirectoryListing
        Return New DirectoryListing(DirectoryOutcome.Failed, Nothing, False, If(failure, New ResourceFailure(ResourceFailureKind.Transport)))
    End Function

    Friend Shared Function Cancelled() As DirectoryListing
        Return New DirectoryListing(DirectoryOutcome.Cancelled, Nothing, False, New ResourceFailure(ResourceFailureKind.Cancelled))
    End Function

    ''' <summary>Reads a classified failure as the outcome it stands for: no such path is Missing, a refusal is Denied,
    ''' a cancel is Cancelled, everything else is a link that did not answer.</summary>
    Friend Shared Function FromFailure(failure As ResourceFailure) As DirectoryListing
        If failure Is Nothing Then Return Failed(Nothing)
        Select Case failure.Kind
            Case ResourceFailureKind.Cancelled
                Return Cancelled()
            Case ResourceFailureKind.Auth
                Return Denied(failure)
            Case ResourceFailureKind.SftpStatus
                If failure.StatusCode.HasValue AndAlso failure.StatusCode.Value = 2 Then Return Missing()
                If failure.StatusCode.HasValue AndAlso failure.StatusCode.Value = 3 Then Return Denied(failure)
                Return Failed(failure)
            Case Else
                Return Failed(failure)
        End Select
    End Function
End Class

''' <summary>What removing an empty directory came to (consumed by 259; defined here because it is a directory primitive).</summary>
Friend Enum DirectoryRemoval
    Removed = 0
    Missing = 1
    ''' <summary>There is something inside; nothing was removed.</summary>
    NotEmpty = 2
    Denied = 3
    ''' <summary>The path is the place's root, or outside it, or a link: never removed.</summary>
    Refused = 4
    Failed = 5
End Enum

''' <summary>The directory side of a provider. Implemented by LocalProvider, SmbProvider and SftpProvider, next to
''' <see cref="IFileSystemProvider"/>; a fake in a test implements it to drive navigation without a disk or a server.</summary>
Friend Interface IManagerDirectoryProvider

    ''' <summary>The immediate children of <paramref name="dirPath"/> - folders and files, no recursion whatever the viewer's
    ''' "include subfolders" preference says. Never throws for a directory-level failure: it is a typed result.</summary>
    Function ListChildren(dirPath As String, ct As CancellationToken) As DirectoryListing

    ''' <summary>A typed stat that answers for a file AND for a directory (<see cref="ITypedStatProvider.Stat"/> keeps reporting a
    ''' local directory as Missing, for its viewer callers).</summary>
    Function StatEntry(path As String) As StatResult

    ''' <summary>Is <paramref name="path"/> the root or under it, by the provider's own canonical form? A path with a ".." segment is never contained.</summary>
    Function IsWithinRoot(rootPath As String, path As String) As Boolean

    ''' <summary>The same directory by the provider's own comparison (case-insensitive on Windows, exact on a POSIX server).</summary>
    Function PathsEqual(a As String, b As String) As Boolean

    ''' <summary>The parent of a path, or Nothing at the file system's root.</summary>
    Function ParentOf(path As String) As String

    ''' <summary>Removes one EMPTY directory. Refuses the root itself, a path outside it and a link; never removes content.</summary>
    Function RemoveEmptyDirectory(rootPath As String, path As String) As DirectoryRemoval
End Interface
#End If
