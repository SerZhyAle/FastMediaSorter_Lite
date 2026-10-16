Option Strict On

Imports System.IO
Imports System.Runtime.InteropServices
Imports System.Text

' Spec 230 T-3: the two desktop shortcuts - "send the clipboard" and "get the clipboard" - each a .lnk that runs the Share Manager
' with its switch. Made only when the person presses "Create desktop shortcuts.." (nothing is created silently, rule 2); a shortcut
' that is already on the desktop is left exactly as it is (never overwritten, so a person's renaming or icon survives).
'
' The plan is pure (names, switches, what exists) and the writer is a parameter, so the tests run without a shell; the real
' writer is the shell's own IShellLinkW.

''' <summary>One shortcut to write.</summary>
Friend NotInheritable Class ClipboardShortcutSpec

    Public ReadOnly LinkPath As String
    Public ReadOnly TargetPath As String
    Public ReadOnly Arguments As String
    Public ReadOnly WorkingDirectory As String
    Public ReadOnly Description As String

    Public Sub New(linkPath As String, targetPath As String, arguments As String, workingDirectory As String, description As String)
        Me.LinkPath = linkPath
        Me.TargetPath = targetPath
        Me.Arguments = arguments
        Me.WorkingDirectory = workingDirectory
        Me.Description = description
    End Sub

End Class

''' <summary>What a "create the shortcuts" press did: how many it made, how many were already there, how many it could not make.</summary>
Friend NotInheritable Class ClipboardShortcutsResult

    Public ReadOnly Created As Integer
    Public ReadOnly Existing As Integer
    Public ReadOnly Failed As Integer

    Public Sub New(created As Integer, existing As Integer, failed As Integer)
        Me.Created = created
        Me.Existing = existing
        Me.Failed = failed
    End Sub

End Class

Friend NotInheritable Class ClipboardShortcuts

    Private Sub New()
    End Sub

    ''' <summary>The two shortcuts for a desktop folder, in the order send, get. The names are the person's language, so a
    ''' file name that Windows would refuse is cleaned (a reserved character becomes a space; no trailing dot or space).</summary>
    Public Shared Function Plan(desktopFolder As String, exePath As String, sendName As String, getName As String,
                                description As String) As List(Of ClipboardShortcutSpec)
        Dim workDir As String = ""
        Try
            workDir = If(Path.GetDirectoryName(exePath), "")
        Catch ex As ArgumentException
        End Try
        Return New List(Of ClipboardShortcutSpec) From {
            New ClipboardShortcutSpec(Path.Combine(desktopFolder, SafeFileName(sendName) & ".lnk"), exePath, Program.ClipboardSendFlag, workDir, description),
            New ClipboardShortcutSpec(Path.Combine(desktopFolder, SafeFileName(getName) & ".lnk"), exePath, Program.ClipboardGetFlag, workDir, description)
        }
    End Function

    ''' <summary>Writes the shortcuts that are not there yet. A writer that throws counts as failed for that shortcut only.</summary>
    Public Shared Function Create(specs As IEnumerable(Of ClipboardShortcutSpec), exists As Func(Of String, Boolean),
                                  write As Action(Of ClipboardShortcutSpec)) As ClipboardShortcutsResult
        Dim created As Integer = 0
        Dim existing As Integer = 0
        Dim failed As Integer = 0
        For Each spec As ClipboardShortcutSpec In specs
            If exists.Invoke(spec.LinkPath) Then
                existing += 1
                Continue For
            End If
            Try
                write.Invoke(spec)
                created += 1
            Catch ex As Exception When TypeOf ex Is IOException OrElse TypeOf ex Is UnauthorizedAccessException OrElse TypeOf ex Is COMException OrElse TypeOf ex Is InvalidCastException
                failed += 1
            End Try
        Next
        Return New ClipboardShortcutsResult(created, existing, failed)
    End Function

    ''' <summary>The shortcuts on the person's real desktop (OneDrive-redirected or not), pointing at this program.</summary>
    Public Shared Function CreateOnDesktop(sendName As String, getName As String, description As String) As ClipboardShortcutsResult
        Dim desktop As String = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory)
        Dim exe As String = If(Environment.ProcessPath, "")
        If desktop.Length = 0 OrElse exe.Length = 0 Then Return New ClipboardShortcutsResult(0, 0, 2)
        Return Create(Plan(desktop, exe, sendName, getName, description), AddressOf File.Exists, AddressOf WriteShellLink)
    End Function

    Friend Shared Function SafeFileName(name As String) As String
        Dim cleaned As New StringBuilder()
        Dim invalid As Char() = Path.GetInvalidFileNameChars()
        For Each ch As Char In If(name, "")
            cleaned.Append(If(Array.IndexOf(invalid, ch) >= 0, " "c, ch))
        Next
        Dim text As String = cleaned.ToString().Trim().TrimEnd("."c, " "c)
        Return If(text.Length = 0, "Clipboard", text)
    End Function

    ' --- the shell's own writer -----------------------------------------------------------------------------

    Private Shared Sub WriteShellLink(spec As ClipboardShortcutSpec)
        Dim shellLinkType As Type = Type.GetTypeFromCLSID(New Guid("00021401-0000-0000-C000-000000000046"))
        Dim instance As Object = Activator.CreateInstance(shellLinkType)
        Try
            Dim link As IShellLinkW = DirectCast(instance, IShellLinkW)
            link.SetPath(spec.TargetPath)
            link.SetArguments(spec.Arguments)
            link.SetWorkingDirectory(spec.WorkingDirectory)
            link.SetDescription(spec.Description)
            ' The program's own icon: the shortcut reads as the Share Manager's, which is what it is.
            link.SetIconLocation(spec.TargetPath, 0)
            DirectCast(instance, IPersistFile).Save(spec.LinkPath, True)
        Finally
            Marshal.ReleaseComObject(instance)
        End Try
    End Sub

    <ComImport, Guid("000214F9-0000-0000-C000-000000000046"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)>
    Private Interface IShellLinkW
        Sub GetPath(<MarshalAs(UnmanagedType.LPWStr)> pszFile As StringBuilder, cch As Integer, pfd As IntPtr, fFlags As UInteger)
        Sub GetIDList(ByRef ppidl As IntPtr)
        Sub SetIDList(pidl As IntPtr)
        Sub GetDescription(<MarshalAs(UnmanagedType.LPWStr)> pszName As StringBuilder, cch As Integer)
        Sub SetDescription(<MarshalAs(UnmanagedType.LPWStr)> pszName As String)
        Sub GetWorkingDirectory(<MarshalAs(UnmanagedType.LPWStr)> pszDir As StringBuilder, cch As Integer)
        Sub SetWorkingDirectory(<MarshalAs(UnmanagedType.LPWStr)> pszDir As String)
        Sub GetArguments(<MarshalAs(UnmanagedType.LPWStr)> pszArgs As StringBuilder, cch As Integer)
        Sub SetArguments(<MarshalAs(UnmanagedType.LPWStr)> pszArgs As String)
        Sub GetHotkey(ByRef pwHotkey As UShort)
        Sub SetHotkey(wHotkey As UShort)
        Sub GetShowCmd(ByRef piShowCmd As Integer)
        Sub SetShowCmd(iShowCmd As Integer)
        Sub GetIconLocation(<MarshalAs(UnmanagedType.LPWStr)> pszIconPath As StringBuilder, cch As Integer, ByRef piIcon As Integer)
        Sub SetIconLocation(<MarshalAs(UnmanagedType.LPWStr)> pszIconPath As String, iIcon As Integer)
        Sub SetRelativePath(<MarshalAs(UnmanagedType.LPWStr)> pszPathRel As String, dwReserved As UInteger)
        Sub Resolve(hwnd As IntPtr, fFlags As UInteger)
        Sub SetPath(<MarshalAs(UnmanagedType.LPWStr)> pszFile As String)
    End Interface

    <ComImport, Guid("0000010B-0000-0000-C000-000000000046"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)>
    Private Interface IPersistFile
        Sub GetClassID(ByRef pClassID As Guid)
        <PreserveSig>
        Function IsDirty() As Integer
        Sub Load(<MarshalAs(UnmanagedType.LPWStr)> pszFileName As String, dwMode As UInteger)
        Sub Save(<MarshalAs(UnmanagedType.LPWStr)> pszFileName As String, <MarshalAs(UnmanagedType.Bool)> fRemember As Boolean)
        Sub SaveCompleted(<MarshalAs(UnmanagedType.LPWStr)> pszFileName As String)
        Sub GetCurFile(<MarshalAs(UnmanagedType.LPWStr)> ByRef ppszFileName As String)
    End Interface

End Class
