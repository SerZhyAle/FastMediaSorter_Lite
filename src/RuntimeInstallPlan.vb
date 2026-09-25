Option Strict On

Imports System.IO

Friend Enum OptionalRuntimeKind
    Ocr
    Vlc
#If Not NETFRAMEWORK Then
    ''' <summary>The video encoder behind "Replace with video"
    ''' (SPECIFICATION_DECODE_CACHE_AND_ANIMATION_TO_VIDEO_DOTNET10.md §8). Fenced with the
    ''' feature it serves: the x86 fallback has no button that could ask for it, and an
    ''' enum member it can never reach is a promise it does not keep.</summary>
    Ffmpeg
#End If
End Enum

''' <summary>What a downloaded runtime tree on disk amounts to (audit 050 D-09).</summary>
Friend Enum RuntimeTreeState
    ''' <summary>No folder at all.</summary>
    Missing
    ''' <summary>A folder that is not a whole runtime: an extraction stopped half-way, a
    ''' file deleted or truncated since. Never loaded, never offered the VC++ runtime for -
    ''' it is downloaded again.</summary>
    Incomplete
    ''' <summary>Its completion marker is there and the tree still matches it.</summary>
    Complete
    ''' <summary>No marker, but exactly the file count and byte total of the pinned package
    ''' for this architecture: a runtime installed by a build before the marker existed. The
    ''' caller writes the marker once and treats it as Complete, instead of making every
    ''' existing user download 100 MB again.</summary>
    CompleteUnmarked
End Enum

''' <summary>A file count and byte total of a runtime tree.</summary>
Friend Structure RuntimeTreeTotals
    Public Files As Integer
    Public Bytes As Long

    Public Sub New(files As Integer, bytes As Long)
        Me.Files = files
        Me.Bytes = bytes
    End Sub
End Structure

''' <summary>
''' The disk-level rules of the optional runtime installer (VLC, OCR, FFmpeg), outside
''' OptionalRuntimeManager so they can be proven without a download (audit 050 D-09, D-13).
''' The shape, and why each part exists:
'''
'''   * A package is extracted into a sibling staging folder "&lt;dir&gt;.partial-&lt;guid&gt;", and
'''     only the CURRENT architecture: the old code wrote both win-x64 and win-x86 (about
'''     2 x 100 MB for VLC) straight into the final folder, so a full disk, an AV lock or a
'''     logoff left a folder that "looked installed" - it was never downloaded again, shadowed
'''     a good bundled copy, and its missing sibling DLL was misreported as a missing VC++
'''     runtime.
'''   * The staging folder receives a ".complete" marker (file count and byte total) and is
'''     then renamed into place in one step. Only a tree whose marker matches it is a runtime.
'''   * Every archive entry must land INSIDE the staging folder: an entry such as
'''     "x64/../../evil.dll" refuses the whole package (zip-slip).
'''   * Both NuGet packages are pinned by SHA-256, like FFmpeg always was. The digests are of
'''     the exact versions OptionalRuntimeManager downloads, and equal the vendored copies in
'''     packages/.
''' </summary>
Friend Module RuntimeInstallPlan

    Friend Const CompleteMarkerName As String = ".complete"
    Friend Const StagingInfix As String = ".partial-"

    ''' <summary>SHA-256 of https://www.nuget.org/api/v2/package/Tesseract/5.2.0
    ''' (Tesseract.5.2.0.nupkg, 5 697 774 bytes), computed 2026-09-25.</summary>
    Friend Const OcrPackageSha256 As String = "202d82fc7c7d8384df7da57206d5e1f456ccdabd648c46e67cdfaa3a911d4795"

    ''' <summary>SHA-256 of https://www.nuget.org/api/v2/package/VideoLAN.LibVLC.Windows/3.0.21
    ''' (VideoLAN.LibVLC.Windows.3.0.21.nupkg, 89 989 754 bytes), computed 2026-09-25.</summary>
    Friend Const VlcPackageSha256 As String = "814ac28de8ab1544e98816388858fbf1feadd386928742885728959b43c1b0ab"

    ''' <summary>The FFmpeg executable inside the pinned archive.</summary>
    Friend Const FfmpegExeName As String = "ffmpeg.exe"

    ''' <summary>
    ''' Maps one archive entry to a path RELATIVE to the runtime folder of the given
    ''' architecture, or "" when the entry is not part of that runtime (another architecture,
    ''' a folder entry, the .nuspec, a .lib). The mapped path is NOT yet known to be safe -
    ''' <see cref="ContainedDestination"/> decides that, so a hostile entry is refused rather
    ''' than silently skipped.
    ''' </summary>
    Friend Function MapEntry(kind As OptionalRuntimeKind, fullName As String, is64BitProcess As Boolean) As String
        If String.IsNullOrEmpty(fullName) Then Return ""
        Dim normalized As String = fullName.Replace("\"c, "/"c)
        If normalized.EndsWith("/", StringComparison.Ordinal) Then Return ""

        Dim prefix As String
        Select Case kind
            Case OptionalRuntimeKind.Ocr
                prefix = If(is64BitProcess, "x64/", "x86/")
            Case OptionalRuntimeKind.Vlc
                prefix = If(is64BitProcess, "build/x64/", "build/x86/")
#If Not NETFRAMEWORK Then
            Case OptionalRuntimeKind.Ffmpeg
                ' One versioned root folder over bin/ + doc/ + LICENSE. Exactly one entry is
                ' taken, flattened: matching the tail rather than the root's name keeps this
                ' working when the pin moves to a build whose folder is called otherwise.
                If normalized.EndsWith("/bin/" & FfmpegExeName, StringComparison.OrdinalIgnoreCase) Then Return FfmpegExeName
                Return ""
#End If
            Case Else
                Return ""
        End Select

        If Not normalized.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) Then Return ""
        Return normalized.Substring(prefix.Length)
    End Function

    ''' <summary>
    ''' The full path an entry mapped to <paramref name="relative"/> is written to under
    ''' <paramref name="root"/>, or Nothing when it would land anywhere else - a "..", a
    ''' rooted path, a drive or a stream name. The caller refuses the whole package on
    ''' Nothing: a package carrying one such entry is not the package that was pinned.
    ''' </summary>
    Friend Function ContainedDestination(root As String, relative As String) As String
        If String.IsNullOrEmpty(root) OrElse String.IsNullOrEmpty(relative) Then Return Nothing
        If relative.IndexOf(":"c) >= 0 Then Return Nothing
        Dim local As String = relative.Replace("/"c, Path.DirectorySeparatorChar)
        If Path.IsPathRooted(local) Then Return Nothing
        Try
            Dim fullRoot As String = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar) & Path.DirectorySeparatorChar
            Dim fullDestination As String = Path.GetFullPath(Path.Combine(fullRoot, local))
            If Not fullDestination.StartsWith(fullRoot, StringComparison.OrdinalIgnoreCase) Then Return Nothing
            If fullDestination.Length <= fullRoot.Length Then Return Nothing
            Return fullDestination
        Catch
            Return Nothing
        End Try
    End Function

    ''' <summary>A fresh staging folder beside <paramref name="runtimeDir"/>.</summary>
    Friend Function StagingDirFor(runtimeDir As String) As String
        Return runtimeDir.TrimEnd(Path.DirectorySeparatorChar) & StagingInfix & Guid.NewGuid().ToString("N")
    End Function

    ''' <summary>Removes staging folders a killed or failed install left beside
    ''' <paramref name="runtimeDir"/>. Called under the download mutex only, so it never
    ''' pulls a folder from under a running install.</summary>
    Friend Sub SweepStaging(runtimeDir As String)
        Try
            Dim trimmed As String = runtimeDir.TrimEnd(Path.DirectorySeparatorChar)
            Dim parent As String = Path.GetDirectoryName(trimmed)
            If String.IsNullOrEmpty(parent) OrElse Not Directory.Exists(parent) Then Return
            For Each leftover As String In Directory.GetDirectories(parent, Path.GetFileName(trimmed) & StagingInfix & "*")
                Try
                    Directory.Delete(leftover, True)
                Catch
                End Try
            Next
        Catch
        End Try
    End Sub

    ''' <summary>Every file under <paramref name="dir"/> except the marker itself.</summary>
    Friend Function MeasureTree(dir As String) As RuntimeTreeTotals
        Dim totals As New RuntimeTreeTotals(0, 0)
        For Each file As String In Directory.GetFiles(dir, "*", SearchOption.AllDirectories)
            If String.Equals(Path.GetFileName(file), CompleteMarkerName, StringComparison.OrdinalIgnoreCase) AndAlso
               String.Equals(Path.GetDirectoryName(file).TrimEnd(Path.DirectorySeparatorChar),
                             dir.TrimEnd(Path.DirectorySeparatorChar), StringComparison.OrdinalIgnoreCase) Then
                Continue For
            End If
            totals.Files += 1
            totals.Bytes += New FileInfo(file).Length
        Next
        Return totals
    End Function

    ''' <summary>Writes the marker that makes <paramref name="dir"/> a runtime: what it
    ''' holds, measured now. Written LAST, into the staging folder, before the rename.</summary>
    Friend Sub WriteCompleteMarker(dir As String)
        Dim totals As RuntimeTreeTotals = MeasureTree(dir)
        File.WriteAllText(Path.Combine(dir, CompleteMarkerName),
                          "files=" & totals.Files.ToString(Globalization.CultureInfo.InvariantCulture) & Environment.NewLine &
                          "bytes=" & totals.Bytes.ToString(Globalization.CultureInfo.InvariantCulture) & Environment.NewLine)
    End Sub

    ''' <summary>The counts a marker records, or False when it is not a readable marker.</summary>
    Friend Function TryReadMarker(markerText As String, ByRef totals As RuntimeTreeTotals) As Boolean
        If String.IsNullOrEmpty(markerText) Then Return False
        Dim files As Integer = -1
        Dim bytes As Long = -1
        For Each rawLine As String In markerText.Split({ControlChars.Cr, ControlChars.Lf}, StringSplitOptions.RemoveEmptyEntries)
            Dim line As String = rawLine.Trim()
            If line.StartsWith("files=", StringComparison.Ordinal) Then
                Integer.TryParse(line.Substring(6), Globalization.NumberStyles.None, Globalization.CultureInfo.InvariantCulture, files)
            ElseIf line.StartsWith("bytes=", StringComparison.Ordinal) Then
                Long.TryParse(line.Substring(6), Globalization.NumberStyles.None, Globalization.CultureInfo.InvariantCulture, bytes)
            End If
        Next
        If files <= 0 OrElse bytes < 0 Then Return False
        totals = New RuntimeTreeTotals(files, bytes)
        Return True
    End Function

    ''' <summary>
    ''' Whether <paramref name="dir"/> is a whole runtime. <paramref name="pinnedTotals"/> is
    ''' what the pinned package holds for this architecture - it lets a tree installed before
    ''' the marker existed be adopted (<see cref="RuntimeTreeState.CompleteUnmarked"/>) while a
    ''' half-extracted one of the same age is not.
    ''' </summary>
    Friend Function ReadTreeState(dir As String, pinnedTotals As RuntimeTreeTotals) As RuntimeTreeState
        Try
            If String.IsNullOrEmpty(dir) OrElse Not Directory.Exists(dir) Then Return RuntimeTreeState.Missing
            Dim actual As RuntimeTreeTotals = MeasureTree(dir)
            Dim marker As String = Path.Combine(dir, CompleteMarkerName)
            If File.Exists(marker) Then
                Dim recorded As RuntimeTreeTotals
                If TryReadMarker(File.ReadAllText(marker), recorded) AndAlso
                   recorded.Files = actual.Files AndAlso recorded.Bytes = actual.Bytes Then
                    Return RuntimeTreeState.Complete
                End If
                Return RuntimeTreeState.Incomplete
            End If
            If pinnedTotals.Files > 0 AndAlso actual.Files = pinnedTotals.Files AndAlso actual.Bytes = pinnedTotals.Bytes Then
                Return RuntimeTreeState.CompleteUnmarked
            End If
            Return RuntimeTreeState.Incomplete
        Catch
            ' An unreadable tree is not a runtime to load.
            Return RuntimeTreeState.Incomplete
        End Try
    End Function

    ''' <summary>
    ''' What the pinned package extracts for one architecture, measured from the exact
    ''' packages OptionalRuntimeManager downloads (2026-09-25). Used only to adopt a tree
    ''' installed before the marker existed; a new install is judged by its own marker.
    ''' Change these together with the pinned version and hash.
    ''' </summary>
    Friend Function PinnedTotals(kind As OptionalRuntimeKind, is64BitProcess As Boolean) As RuntimeTreeTotals
        Select Case kind
            Case OptionalRuntimeKind.Ocr
                Return If(is64BitProcess, New RuntimeTreeTotals(2, 6956544L), New RuntimeTreeTotals(2, 5716480L))
            Case OptionalRuntimeKind.Vlc
                Return If(is64BitProcess, New RuntimeTreeTotals(522, 106703521L), New RuntimeTreeTotals(522, 100066911L))
#If Not NETFRAMEWORK Then
            Case OptionalRuntimeKind.Ffmpeg
                Return New RuntimeTreeTotals(1, 102856192L)
#End If
            Case Else
                Return New RuntimeTreeTotals(0, 0)
        End Select
    End Function

End Module
