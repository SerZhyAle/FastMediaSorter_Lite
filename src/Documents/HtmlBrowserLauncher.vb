#If Not NETFRAMEWORK Then
Option Explicit On
Option Strict On
Option Infer On
Option Compare Binary

Imports System
Imports System.Diagnostics
Imports System.IO
Imports System.Runtime.InteropServices
Imports System.Text

' Specification 275 section 11.3: user-initiated browser handoff - a normalized HTTP(S) URL, or a
' local entry passed to the resolved browser executable, never the .html association.
Friend Module HtmlBrowserLauncher
    Private Const AssocFlagNoTruncate As Integer = &H20
    Private Const AssocStringExecutable As Integer = 2
    Private ReadOnly OwnExecutables As String() = {"FastMediaSorter_LITE.exe", "FastMediaSorter_x86.exe"}

    <DllImport("shlwapi.dll", CharSet:=CharSet.Unicode, ExactSpelling:=True)>
    Private Function AssocQueryStringW(flags As Integer, str As Integer, pszAssoc As String, pszExtra As String,
                                       pszOut As StringBuilder, ByRef pcchOut As UInteger) As Integer
    End Function

    ''' <summary>The executable registered for https, or "" when none is usable (missing, not an .exe,
    ''' or this application itself).</summary>
    Friend Function ResolveBrowserExecutable() As String
        Try
            Dim size As UInteger = 1024
            Dim buffer As New StringBuilder(CInt(size))
            If AssocQueryStringW(AssocFlagNoTruncate, AssocStringExecutable, "https", "open", buffer, size) <> 0 Then Return ""
            Return UsableBrowser(buffer.ToString())
        Catch ex As Exception When TypeOf ex Is DllNotFoundException OrElse TypeOf ex Is EntryPointNotFoundException OrElse TypeOf ex Is ArgumentException
            Return ""
        End Try
    End Function

    ''' <summary>Pure half of the resolution, for the tests: an absolute, existing .exe that is not ours.</summary>
    Friend Function UsableBrowser(candidate As String) As String
        If String.IsNullOrWhiteSpace(candidate) Then Return ""
        Dim path = candidate.Trim().Trim(""""c)
        If Not IO.Path.IsPathFullyQualified(path) OrElse Not path.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) Then Return ""
        Dim name = IO.Path.GetFileName(path)
        For Each own In OwnExecutables
            If String.Equals(name, own, StringComparison.OrdinalIgnoreCase) Then Return ""
        Next
        If String.Equals(path, Environment.ProcessPath, StringComparison.OrdinalIgnoreCase) Then Return ""
        Return If(File.Exists(path), path, "")
    End Function

    ''' <summary>A normalized http(s) URL through the protocol handler; False when it is not one.</summary>
    Friend Function OpenUrl(url As String) As Boolean
        Dim normalized = HtmlDocumentPolicy.NormalizeExternalUrl(url)
        If normalized.Length = 0 Then Return False
        Process.Start(New ProcessStartInfo(normalized) With {.UseShellExecute = True})?.Dispose()
        Return True
    End Function

    ''' <summary>mailto: only, never with a line break that could add headers.</summary>
    Friend Function OpenMail(url As String) As Boolean
        Dim target As Uri = Nothing
        If String.IsNullOrWhiteSpace(url) OrElse url.IndexOfAny({ChrW(13), ChrW(10), ChrW(0)}) >= 0 Then Return False
        If Not Uri.TryCreate(url.Trim(), UriKind.Absolute, target) OrElse target.Scheme <> Uri.UriSchemeMailto Then Return False
        Process.Start(New ProcessStartInfo(target.AbsoluteUri) With {.UseShellExecute = True})?.Dispose()
        Return True
    End Function

    ''' <summary>The file URI the browser gets for a local entry: real path characters such as # and %
    ''' are escaped, never stripped as a fragment.</summary>
    Friend Function FileUriOf(localPath As String) As String
        Return New Uri(IO.Path.GetFullPath(localPath)).AbsoluteUri
    End Function

    ''' <summary>A local entry in the resolved browser, as one structured argument. False when no browser
    ''' could be resolved - the caller then lets the user choose one.</summary>
    Friend Function OpenLocalFile(localPath As String) As Boolean
        If String.IsNullOrWhiteSpace(localPath) OrElse Not File.Exists(localPath) Then Return False
        Dim browser = ResolveBrowserExecutable()
        If browser.Length = 0 Then Return False
        Dim info As New ProcessStartInfo(browser) With {.UseShellExecute = False}
        info.ArgumentList.Add(FileUriOf(localPath))
        Process.Start(info)?.Dispose()
        Return True
    End Function
End Module
#End If
