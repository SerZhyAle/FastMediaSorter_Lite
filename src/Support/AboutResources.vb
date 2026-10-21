#If Not NETFRAMEWORK Then
Option Explicit On
Option Strict On
Option Compare Binary
Option Infer On

Imports System.Diagnostics
Imports System.IO

''' <summary>
''' What a click on an About or Resources link does, shared by the viewer's settings window and the Share Manager
''' (linked, like SitePages.vb). SETTINGS-NAVIGATION 2.0 section 3 rule 3.4: opening About contacts nothing remote -
''' the pages only hold addresses (<see cref="ProductLinks"/>), and this class is reached from a click handler and from
''' nowhere else. Windows-only and modern-only, like the help it opens (ContextHelp.vb): the x86 fallback has no
''' About page of this shape.
''' <para>The licence, the notices and the help are the offline choice: the file that ships beside the program (the
''' installer, the ZIP and the MSIX all copy LICENSE and THIRD-PARTY-NOTICES.txt) or the help pages the program
''' carries open from local disk; only when they are not there is the matching page on the web opened instead.
''' The help HTML corpus is not packaged yet, so on an installed copy the help link lands on the site's topics page.</para>
''' </summary>
Public NotInheritable Class AboutResources

    Private Sub New()
    End Sub

    ''' <summary>How a web address, a mailto: or a file URI is handed to Windows. A test swaps it so no process starts.</summary>
    Public Shared Property Launch As Action(Of String) = Sub(address)
                                                             Process.Start(New ProcessStartInfo(address) With {.UseShellExecute = True})
                                                         End Sub

    ''' <summary>How a bundled text file is shown. Notepad on purpose: LICENSE has no extension, so the shell would
    ''' ask which program to open it with.</summary>
    Public Shared Property LaunchFile As Action(Of String) = Sub(path)
                                                                 Process.Start(New ProcessStartInfo("notepad.exe", """" & path & """") With {.UseShellExecute = True})
                                                             End Sub

    ''' <summary>The folder <see cref="Locate"/> starts from when it is not given one; "" is the program's own folder.
    ''' A test points it at a temporary folder, so whether LICENSE sits beside the test host never matters.</summary>
    Public Shared Property SearchFrom As String = ""

    ''' <summary>
    ''' The full path of <paramref name="fileName"/> beside the program, or "". An installed copy has it in the
    ''' program's own folder; a build run from the source tree (bin\Release below the solution) finds it at the
    ''' solution root - only in a folder that holds FastMediaSorter.sln, so a LICENSE in some unrelated parent
    ''' folder is never opened as ours.
    ''' </summary>
    Public Shared Function Locate(fileName As String, Optional baseDirectory As String = Nothing) As String
        If String.IsNullOrEmpty(fileName) Then Return ""
        Dim start As String = If(String.IsNullOrEmpty(SearchFrom), AppContext.BaseDirectory, SearchFrom)
        Dim folder As String = If(String.IsNullOrEmpty(baseDirectory), start, baseDirectory)
        Dim beside As String = Path.Combine(folder, fileName)
        If File.Exists(beside) Then Return Path.GetFullPath(beside)
        Dim parent As String = folder
        For level As Integer = 1 To 5
            parent = Path.GetDirectoryName(parent.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar))
            If String.IsNullOrEmpty(parent) Then Exit For
            If File.Exists(Path.Combine(parent, "FastMediaSorter.sln")) Then
                Dim atRoot As String = Path.Combine(parent, fileName)
                If File.Exists(atRoot) Then Return Path.GetFullPath(atRoot)
                Exit For
            End If
        Next
        Return ""
    End Function

    ''' <summary>
    ''' The file URI of the shipped help page for <paramref name="surfaceId"/> (help-map.json) in
    ''' <paramref name="languageCode"/>, or "" when no copy of it is on this computer.
    ''' </summary>
    Public Shared Function HelpTarget(surfaceId As String, languageCode As String) As String
        Try
            Dim relative As String = ContextHelp.Resolve(surfaceId, languageCode)
            If String.IsNullOrEmpty(relative) Then Return ""
            Dim address As String = ContextHelp.Extract(relative)
            Return If(File.Exists(New Uri(address).LocalPath), address, "")
        Catch ex As Exception
            AppFileLogger.LogException("About: locating the shipped help", ex)
            Return ""
        End Try
    End Function

    ''' <summary>
    ''' Opens <paramref name="address"/> (a <see cref="ProductLinks"/> address): a bundled file or the shipped help
    ''' from local disk when it is there, its web page otherwise, any other address as it is. Returns what was
    ''' opened. A start that fails throws for the caller to say so beside the address; a local file that cannot be
    ''' started falls back to its web page first.
    ''' </summary>
    Public Shared Function Open(address As String, languageCode As String) As String
        If String.IsNullOrEmpty(address) Then Return ""
        Dim local As String = ""
        If address.StartsWith(ProductLinks.LocalScheme, StringComparison.Ordinal) Then
            local = Locate(address.Substring(ProductLinks.LocalScheme.Length))
            If local.Length > 0 Then
                Try
                    LaunchFile.Invoke(local)
                    Return local
                Catch ex As Exception
                    AppFileLogger.LogException("About: opening a bundled file", ex)
                End Try
            End If
        ElseIf address.StartsWith(ProductLinks.HelpScheme, StringComparison.Ordinal) Then
            local = HelpTarget(address.Substring(ProductLinks.HelpScheme.Length), languageCode)
            If local.Length > 0 Then
                Try
                    Launch.Invoke(local)
                    Return local
                Catch ex As Exception
                    AppFileLogger.LogException("About: opening the shipped help", ex)
                End Try
            End If
        Else
            Launch.Invoke(address)
            Return address
        End If
        Dim online As String = ProductLinks.OnlineFor(address, languageCode)
        If online.Length = 0 Then Return ""
        Launch.Invoke(online)
        Return online
    End Function

    ''' <summary>The address to print when opening failed, so the reader can open it by hand: the web page for a
    ''' bundled file or the help, the address itself otherwise.</summary>
    Public Shared Function ManualAddress(address As String, languageCode As String) As String
        Return ProductLinks.OnlineFor(address, languageCode)
    End Function

End Class
#End If
