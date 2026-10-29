#If Not NETFRAMEWORK Then
Option Explicit On
Option Strict On
Option Infer On
Option Compare Binary

Imports System
Imports System.IO
Imports System.Runtime.InteropServices
Imports System.Security
Imports System.Security.Cryptography
Imports System.Text
Imports System.Threading
Imports Microsoft.Win32.SafeHandles

' One bounded read: either a typed refusal or the bytes plus the version they were read at.
Friend NotInheritable Class HtmlFileRead
    Public Property Status As HtmlOutcome
    Public Property Bytes As Byte() = New Byte() {}
    Public Property Version As HtmlSourceVersion

    Friend Shared Function Failed(status As HtmlOutcome) As HtmlFileRead
        Return New HtmlFileRead With {.Status = If(status = HtmlOutcome.Ok, HtmlOutcome.UnsupportedResource, status)}
    End Function
End Class

' Specification 275 section 10.3 step 5: Windows handle validation, reparse-point and
' case-sensitive-tree refusal, short-lived reads that never keep the original open.
' Authorization is the OPENED handle (final path under the canonical root, not a link, not a
' directory) - never File.Exists plus a string prefix followed by an open of something else.
Friend Module HtmlFileAccess
    Private Const GenericRead As UInteger = &H80000000UI
    Private Const FileReadAttributes As UInteger = &H80UI
    Private Const ShareAll As UInteger = 7UI
    Private Const OpenExisting As UInteger = 3UI
    Private Const FlagBackupSemantics As UInteger = &H2000000UI
    Private Const FlagOpenReparsePoint As UInteger = &H200000UI
    Private Const FlagSequentialScan As UInteger = &H8000000UI
    Private Const AttributeDirectory As UInteger = &H10UI
    Private Const AttributeReparsePoint As UInteger = &H400UI
    Private Const FileCaseSensitiveInfoClass As Integer = 23
    Private Const CaseSensitiveDirFlag As UInteger = 1UI
    Private Const ErrorAccessDenied As Integer = 5
    Private Const ReadChunkBytes As Integer = 65536

    <StructLayout(LayoutKind.Sequential)>
    Private Structure ByHandleFileInformation
        Public FileAttributes As UInteger
        Public CreationLow As UInteger
        Public CreationHigh As UInteger
        Public LastAccessLow As UInteger
        Public LastAccessHigh As UInteger
        Public LastWriteLow As UInteger
        Public LastWriteHigh As UInteger
        Public VolumeSerialNumber As UInteger
        Public FileSizeHigh As UInteger
        Public FileSizeLow As UInteger
        Public NumberOfLinks As UInteger
        Public FileIndexHigh As UInteger
        Public FileIndexLow As UInteger
    End Structure

    <StructLayout(LayoutKind.Sequential)>
    Private Structure CaseSensitiveInfo
        Public Flags As UInteger
    End Structure

    <DllImport("kernel32.dll", EntryPoint:="CreateFileW", SetLastError:=True, CharSet:=CharSet.Unicode)>
    Private Function NativeCreateFile(fileName As String, desiredAccess As UInteger, shareMode As UInteger,
                                      securityAttributes As IntPtr, creationDisposition As UInteger,
                                      flagsAndAttributes As UInteger, templateFile As IntPtr) As SafeFileHandle
    End Function

    <DllImport("kernel32.dll", EntryPoint:="GetFileInformationByHandle", SetLastError:=True)>
    Private Function NativeGetFileInformation(file As SafeFileHandle, ByRef information As ByHandleFileInformation) As Boolean
    End Function

    <DllImport("kernel32.dll", EntryPoint:="GetFileInformationByHandleEx", SetLastError:=True)>
    Private Function NativeGetCaseSensitiveInfo(file As SafeFileHandle, informationClass As Integer,
                                                ByRef information As CaseSensitiveInfo, bufferSize As UInteger) As Boolean
    End Function

    <DllImport("kernel32.dll", EntryPoint:="GetFinalPathNameByHandleW", SetLastError:=True, CharSet:=CharSet.Unicode)>
    Private Function NativeGetFinalPath(file As SafeFileHandle, filePath As StringBuilder,
                                        pathLength As UInteger, flags As UInteger) As UInteger
    End Function

    ' ---- public surface -------------------------------------------------------------------

    ' Canonicalizes a chosen root once. A reparse-point root is refused (RootEscape), a
    ' case-sensitive directory tree is refused (UnsupportedResource) in this first implementation.
    Friend Function CanonicalRoot(directory As String, ByRef canonical As String) As HtmlOutcome
        canonical = ""
        Dim full As String = Nothing
        Dim prepared As HtmlOutcome = FullPathOf(directory, full)
        If prepared <> HtmlOutcome.Ok Then Return prepared

        Dim handle As SafeFileHandle = Nothing
        Dim errorCode As Integer = OpenHandle(ToExtendedPath(full), FileReadAttributes, FlagBackupSemantics Or FlagOpenReparsePoint, handle)
        If errorCode <> 0 Then Return OutcomeOfError(errorCode)
        Using handle
            Dim info As ByHandleFileInformation
            If Not NativeGetFileInformation(handle, info) Then Return HtmlOutcome.AccessDenied
            If (info.FileAttributes And AttributeDirectory) = 0UI Then Return HtmlOutcome.UnsupportedResource
            If (info.FileAttributes And AttributeReparsePoint) <> 0UI Then Return HtmlOutcome.RootEscape
            Select Case CaseSensitivityOf(handle)
                Case 1 : Return HtmlOutcome.UnsupportedResource
                Case 2 : Return HtmlOutcome.AccessDenied
            End Select
            Dim finalPath As String = ""
            If Not FinalPathOf(handle, finalPath) Then Return HtmlOutcome.RootEscape
            canonical = finalPath
            Return HtmlOutcome.Ok
        End Using
    End Function

    ' The canonical (final) path of an existing regular file, resolved through its handle. The
    ' handle asks for attributes only, so a file another program holds exclusively still resolves;
    ' the read reports the lock. A link or a directory is refused.
    Friend Function CanonicalFile(filePath As String, ByRef canonical As String) As HtmlOutcome
        canonical = ""
        Dim full As String = Nothing
        Dim prepared As HtmlOutcome = FullPathOf(filePath, full)
        If prepared <> HtmlOutcome.Ok Then Return prepared

        Dim handle As SafeFileHandle = Nothing
        Dim errorCode As Integer = OpenHandle(ToExtendedPath(full), FileReadAttributes, FlagOpenReparsePoint, handle)
        If errorCode <> 0 Then Return OpenFailure(errorCode, full)
        Using handle
            Dim info As ByHandleFileInformation
            If Not NativeGetFileInformation(handle, info) Then Return HtmlOutcome.AccessDenied
            If (info.FileAttributes And AttributeDirectory) <> 0UI Then Return HtmlOutcome.UnsupportedResource
            If (info.FileAttributes And AttributeReparsePoint) <> 0UI Then Return HtmlOutcome.RootEscape
            Dim finalPath As String = ""
            If Not FinalPathOf(handle, finalPath) Then Return HtmlOutcome.RootEscape
            canonical = finalPath
            Return HtmlOutcome.Ok
        End Using
    End Function

    ' True when path is strictly inside root at a separator boundary ("C:\root2" is not under "C:\root").
    Friend Function IsUnderRoot(root As String, path As String) As Boolean
        If String.IsNullOrEmpty(root) OrElse String.IsNullOrEmpty(path) Then Return False
        If path.Length <= root.Length Then Return False
        If Not path.StartsWith(root, StringComparison.OrdinalIgnoreCase) Then Return False
        If root(root.Length - 1) = "\"c Then Return True
        Return path(root.Length) = "\"c
    End Function

    ' Lexical join of an allowed key under the root for the typed opener; "" when the key is refused.
    Friend Function LocalPathOf(root As String, key As String) As String
        If String.IsNullOrEmpty(root) OrElse Not HtmlDocumentPolicy.ValidateKey(key).IsAllowed Then Return ""
        Dim current As String = root
        For Each segment As String In key.Split("/"c)
            current = JoinPath(current, segment)
        Next
        Return current
    End Function

    ' Reads one logical key under an already canonical root. The key must be a parsed logical key
    ' (it is re-validated here); the bytes come from a short-lived handle that is closed before return.
    Friend Function ReadUnderRoot(canonicalRoot As String, key As String, maxBytes As Long, token As CancellationToken) As HtmlFileRead
        If token.IsCancellationRequested Then Return HtmlFileRead.Failed(HtmlOutcome.Cancelled)
        If String.IsNullOrEmpty(canonicalRoot) Then Return HtmlFileRead.Failed(HtmlOutcome.SourceMissing)
        Dim keyCheck As HtmlKeyResolution = HtmlDocumentPolicy.ValidateKey(key)
        If Not keyCheck.IsAllowed Then Return HtmlFileRead.Failed(keyCheck.Status)

        ' Preliminary check: no directory below the root is a link or a case-sensitive tree.
        Dim segments As String() = key.Split("/"c)
        Dim current As String = canonicalRoot
        For i As Integer = 0 To segments.Length - 2
            current = JoinPath(current, segments(i))
            Dim verdict As HtmlOutcome = CheckDirectoryComponent(current)
            If verdict <> HtmlOutcome.Ok Then Return HtmlFileRead.Failed(verdict)
        Next
        current = JoinPath(current, segments(segments.Length - 1))

        ' The authorization below happens on the opened handle, so a link swapped in after the
        ' preliminary check cannot redirect the read outside the root.
        Dim handle As SafeFileHandle = Nothing
        Dim errorCode As Integer = OpenHandle(ToExtendedPath(current), GenericRead, FlagOpenReparsePoint Or FlagSequentialScan, handle)
        If errorCode <> 0 Then Return HtmlFileRead.Failed(OpenFailure(errorCode, current))
        Try
            Return ReadFromHandle(handle, canonicalRoot, maxBytes, token)
        Catch ex As IOException
            Return HtmlFileRead.Failed(HtmlOutcome.AccessDenied)
        Catch ex As UnauthorizedAccessException
            Return HtmlFileRead.Failed(HtmlOutcome.AccessDenied)
        Finally
            handle.Dispose()
        End Try
    End Function

    ' ---- handle work ----------------------------------------------------------------------

    Private Function ReadFromHandle(handle As SafeFileHandle, canonicalRoot As String, maxBytes As Long, token As CancellationToken) As HtmlFileRead
        Dim info As ByHandleFileInformation
        If Not NativeGetFileInformation(handle, info) Then Return HtmlFileRead.Failed(HtmlOutcome.AccessDenied)
        If (info.FileAttributes And AttributeDirectory) <> 0UI Then Return HtmlFileRead.Failed(HtmlOutcome.UnsupportedResource)
        If (info.FileAttributes And AttributeReparsePoint) <> 0UI Then Return HtmlFileRead.Failed(HtmlOutcome.RootEscape)

        Dim finalPath As String = ""
        If Not FinalPathOf(handle, finalPath) OrElse Not IsUnderRoot(canonicalRoot, finalPath) Then
            Return HtmlFileRead.Failed(HtmlOutcome.RootEscape)
        End If

        Dim size As Long = SizeOf(info)
        If size > maxBytes OrElse size > Integer.MaxValue Then Return HtmlFileRead.Failed(HtmlOutcome.LimitExceeded)
        Dim writeTicks As Long = LastWriteTicksOf(info)

        Dim data(CInt(size) - 1) As Byte
        ' The stream takes over the handle and closes it when this block ends, so the
        ' original is released before any caller sees the bytes.
        Using stream As New FileStream(handle, FileAccess.Read, 4096, False)
            Dim total As Integer = 0
            While total < data.Length
                If token.IsCancellationRequested Then Return HtmlFileRead.Failed(HtmlOutcome.Cancelled)
                Dim count As Integer = stream.Read(data, total, Math.Min(ReadChunkBytes, data.Length - total))
                If count <= 0 Then Exit While
                total += count
            End While
            ' Shorter or longer than the handle said, or rewritten meanwhile: the file changed under the read.
            If total <> data.Length OrElse stream.ReadByte() >= 0 Then Return HtmlFileRead.Failed(HtmlOutcome.SourceChanged)
            Dim after As ByHandleFileInformation
            If NativeGetFileInformation(handle, after) AndAlso (SizeOf(after) <> size OrElse LastWriteTicksOf(after) <> writeTicks) Then
                Return HtmlFileRead.Failed(HtmlOutcome.SourceChanged)
            End If
        End Using

        Return New HtmlFileRead With {
            .Status = HtmlOutcome.Ok,
            .Bytes = data,
            .Version = New HtmlSourceVersion With {
                .Length = size,
                .LastWriteUtcTicks = writeTicks,
                .Digest = HtmlDocumentPolicy.ToLowerHex(SHA256.HashData(data))}}
    End Function

    ' A directory below the root: not a link, not a case-sensitive tree.
    Private Function CheckDirectoryComponent(directoryPath As String) As HtmlOutcome
        Dim handle As SafeFileHandle = Nothing
        Dim errorCode As Integer = OpenHandle(ToExtendedPath(directoryPath), FileReadAttributes, FlagBackupSemantics Or FlagOpenReparsePoint, handle)
        If errorCode <> 0 Then Return OutcomeOfError(errorCode)
        Using handle
            Dim info As ByHandleFileInformation
            If Not NativeGetFileInformation(handle, info) Then Return HtmlOutcome.AccessDenied
            ' A file where a folder was expected is a missing resource, not a directory.
            If (info.FileAttributes And AttributeDirectory) = 0UI Then Return HtmlOutcome.SourceMissing
            If (info.FileAttributes And AttributeReparsePoint) <> 0UI Then Return HtmlOutcome.RootEscape
            Select Case CaseSensitivityOf(handle)
                Case 1 : Return HtmlOutcome.UnsupportedResource
                Case 2 : Return HtmlOutcome.AccessDenied
            End Select
            Return HtmlOutcome.Ok
        End Using
    End Function

    ' 0 not case-sensitive (or the system cannot tell: before Windows 10 1803 the query is
    ' rejected with ERROR_INVALID_PARAMETER), 1 case-sensitive directory, 2 access denied.
    Private Function CaseSensitivityOf(handle As SafeFileHandle) As Integer
        Dim info As CaseSensitiveInfo
        If NativeGetCaseSensitiveInfo(handle, FileCaseSensitiveInfoClass, info, CUInt(Marshal.SizeOf(GetType(CaseSensitiveInfo)))) Then
            Return If((info.Flags And CaseSensitiveDirFlag) <> 0UI, 1, 0)
        End If
        Return If(Marshal.GetLastWin32Error() = ErrorAccessDenied, 2, 0)
    End Function

    ' 0 on success, otherwise the Win32 error; the handle is Nothing on failure.
    Private Function OpenHandle(path As String, access As UInteger, flags As UInteger, ByRef handle As SafeFileHandle) As Integer
        handle = NativeCreateFile(path, access, ShareAll, IntPtr.Zero, OpenExisting, flags, IntPtr.Zero)
        If handle.IsInvalid Then
            Dim errorCode As Integer = Marshal.GetLastWin32Error()
            handle.Dispose()
            handle = Nothing
            Return If(errorCode = 0, 1, errorCode)
        End If
        Return 0
    End Function

    Private Function FinalPathOf(handle As SafeFileHandle, ByRef path As String) As Boolean
        path = ""
        Dim builder As New StringBuilder(512)
        Dim length As UInteger = NativeGetFinalPath(handle, builder, CUInt(builder.Capacity), 0UI)
        If length = 0UI Then Return False
        If length >= CUInt(builder.Capacity) Then
            builder = New StringBuilder(CInt(length) + 1)
            length = NativeGetFinalPath(handle, builder, CUInt(builder.Capacity), 0UI)
            If length = 0UI OrElse length >= CUInt(builder.Capacity) Then Return False
        End If
        path = TrimSeparator(NormalizeFinalPath(builder.ToString()))
        Return path.Length > 0
    End Function

    ' ---- path helpers ---------------------------------------------------------------------

    Private Function FullPathOf(path As String, ByRef full As String) As HtmlOutcome
        full = ""
        If String.IsNullOrWhiteSpace(path) Then Return HtmlOutcome.SourceMissing
        Try
            full = System.IO.Path.GetFullPath(path)
        Catch ex As ArgumentException
            Return HtmlOutcome.SourceMissing
        Catch ex As NotSupportedException
            Return HtmlOutcome.SourceMissing
        Catch ex As PathTooLongException
            Return HtmlOutcome.UnsupportedResource
        Catch ex As SecurityException
            Return HtmlOutcome.AccessDenied
        End Try
        ' Raw device namespace ("\\.\") is not a document location.
        If full.StartsWith("\\.\", StringComparison.Ordinal) Then Return HtmlOutcome.UnsupportedResource
        Return HtmlOutcome.Ok
    End Function

    ' The extended-length form skips Win32 name normalization; every segment was validated first.
    Private Function ToExtendedPath(fullPath As String) As String
        If fullPath.StartsWith("\\?\", StringComparison.Ordinal) Then Return fullPath
        If fullPath.StartsWith("\\", StringComparison.Ordinal) Then Return "\\?\UNC\" & fullPath.Substring(2)
        Return "\\?\" & fullPath
    End Function

    Private Function NormalizeFinalPath(finalPath As String) As String
        If finalPath.StartsWith("\\?\UNC\", StringComparison.OrdinalIgnoreCase) Then Return "\\" & finalPath.Substring(8)
        If finalPath.StartsWith("\\?\", StringComparison.Ordinal) AndAlso finalPath.Length >= 6 AndAlso finalPath(5) = ":"c Then
            Return finalPath.Substring(4)
        End If
        Return finalPath
    End Function

    ' No trailing separator except on a drive root ("C:\").
    Private Function TrimSeparator(path As String) As String
        If path.Length > 3 AndAlso path(path.Length - 1) = "\"c Then Return path.Substring(0, path.Length - 1)
        Return path
    End Function

    Private Function JoinPath(left As String, segment As String) As String
        If left.Length > 0 AndAlso left(left.Length - 1) = "\"c Then Return left & segment
        Return left & "\" & segment
    End Function

    Private Function SizeOf(info As ByHandleFileInformation) As Long
        Return (CLng(info.FileSizeHigh) << 32) Or CLng(info.FileSizeLow)
    End Function

    Private Function LastWriteTicksOf(info As ByHandleFileInformation) As Long
        Dim fileTime As Long = (CLng(info.LastWriteHigh) << 32) Or CLng(info.LastWriteLow)
        Try
            Return DateTime.FromFileTimeUtc(fileTime).Ticks
        Catch ex As ArgumentOutOfRangeException
            Return fileTime
        End Try
    End Function

    ' CreateFile on a directory without backup semantics fails with "access denied"; that is a
    ' wrong kind of object, not a permission problem. Only the message differs, never the decision.
    Private Function OpenFailure(errorCode As Integer, path As String) As HtmlOutcome
        If errorCode = 5 Then
            Try
                If Directory.Exists(path) Then Return HtmlOutcome.UnsupportedResource
            Catch ex As IOException
            Catch ex As UnauthorizedAccessException
            End Try
        End If
        Return OutcomeOfError(errorCode)
    End Function

    Private Function OutcomeOfError(errorCode As Integer) As HtmlOutcome
        Select Case errorCode
            Case 2, 3, 53, 67, 123, 161, 267
                ' file/path not found, bad network path or name, invalid name, bad path name, directory name invalid
                Return HtmlOutcome.SourceMissing
            Case 206
                Return HtmlOutcome.UnsupportedResource
            Case Else
                ' access denied, sharing and lock violations, logon failure and anything unexpected
                Return HtmlOutcome.AccessDenied
        End Select
    End Function
End Module
#End If
