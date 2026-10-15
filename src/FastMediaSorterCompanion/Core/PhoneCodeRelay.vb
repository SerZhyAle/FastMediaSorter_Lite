Option Explicit On
Option Strict On
Option Infer On
Option Compare Binary

Imports System.Diagnostics
Imports System.IO
Imports System.Runtime.InteropServices

''' <summary>
''' Spec 227 T-3, the mirror image of the viewer's wake: the Share Manager read the phone's
''' connection code (its camera, a paste, a file or a clipboard image in the add-resource manager)
''' and hands it to the viewer - the ONE importer - over WM_COPYDATA with the viewer's own tag.
'''
''' The gate is the same written-decision shape as the viewer's own probes: the payload travels only
''' to a viewer whose file version is proven to understand it (an older one would take the payload
''' for a path and put the code on the screen in a "file not found" box). Nobody running: the viewer
''' is cold-started WITHOUT the code on its command line (a secret holder never rides a command line,
''' which every process on the machine can read) and the bounded send loop waits for its window.
'''
''' This class only forwards; the clipboard fallback belongs to the caller, on the UI thread and
''' through <c>SecureClipboard</c>. The code is never written to any log here - only outcomes are.
''' </summary>
Friend NotInheritable Class PhoneCodeRelay

    Friend Enum RelayOutcome
        ''' <summary>A viewer answered "handled"; the import dialog is opening there.</summary>
        Delivered = 0
        ''' <summary>A viewer is installed but none proven capable took the payload (an older version
        ''' running, or no window answered in the budget). The caller falls back to the clipboard.</summary>
        ViewerNotCapable = 1
        ''' <summary>No viewer installed on this machine (a standalone Share Manager).</summary>
        NoViewer = 2
    End Enum

    Private ReadOnly _viewerExePath As Func(Of String)

    Friend Sub New(viewerExePath As Func(Of String))
        _viewerExePath = viewerExePath
    End Sub

    ''' <summary>The code text rides to <c>::fms-phone-code::&lt;code&gt;</c>.</summary>
    Friend Function RelayCode(code As String) As RelayOutcome
        Return Relay(ShareActivationPolicy.PhoneCodeCommandPrefix & If(code, "").Trim())
    End Function

    ''' <summary>A document the user opened as a file rides as its path: the viewer reads it through
    ''' its own bounded door, so a multi-line document never has to fit this transport.</summary>
    Friend Function RelayDocumentFile(path As String) As RelayOutcome
        Return Relay(ShareActivationPolicy.PhoneCodeFileCommandPrefix & If(path, "").Trim())
    End Function

    Private Function Relay(payload As String) As RelayOutcome
        Dim bytes As Byte() = Nothing
        Dim refusal As PayloadRefusal
        If Not CopyDataTransport.TryEncode(payload, bytes, refusal) Then Return RelayOutcome.ViewerNotCapable

        Dim running As Version = RunningViewerVersion()
        If running IsNot Nothing Then
            If Not ShareActivationPolicy.IsPhoneCodeCapable(running) Then Return RelayOutcome.ViewerNotCapable
            Return SendFor(bytes, CopyDataTransport.Total_Budget_Ms)
        End If

        ' Nobody running under either viewer name. An installed, capable exe is started plainly and
        ' waited for; nothing installed answers NoTarget at once, which is its own outcome.
        Dim exe As String = ""
        Try
            exe = If(_viewerExePath, Function() "")()
        Catch
        End Try
        If String.IsNullOrEmpty(exe) OrElse Not File.Exists(exe) Then Return RelayOutcome.NoViewer
        If Not ShareActivationPolicy.IsPhoneCodeCapable(FileVersionOf(exe)) Then Return RelayOutcome.ViewerNotCapable
        Try
            Process.Start(New ProcessStartInfo(exe) With {.UseShellExecute = True})
            AppFileLogger.WriteLine("PhoneCodeRelay: viewer cold-started for the phone code")
        Catch ex As Exception
            AppFileLogger.LogException("PhoneCodeRelay cold start", ex)
            Return RelayOutcome.ViewerNotCapable
        End Try
        ' A self-contained single-file exe reaches its window in seconds; the budget is the
        ' forward budget doubled, and an uncertain end never replays the payload.
        Return SendFor(bytes, CopyDataTransport.Total_Budget_Ms * 2)
    End Function

    Private Function SendFor(bytes As Byte(), budgetMs As Integer) As RelayOutcome
        Dim outcome As ForwardOutcome = CopyDataTransport.RunForward(
            AddressOf MonotonicClock.NowMs,
            Sub(pauseMs) Threading.Thread.Sleep(pauseMs),
            Function(sendTimeoutMs) TrySendOnce(bytes, sendTimeoutMs), budgetMs)
        If outcome = ForwardOutcome.Delivered Then
            AppFileLogger.WriteLine("PhoneCodeRelay: code handed to the viewer")
            Return RelayOutcome.Delivered
        End If
        AppFileLogger.WriteLine("PhoneCodeRelay: no viewer acknowledged (" & outcome.ToString() & ")")
        Return RelayOutcome.ViewerNotCapable
    End Function

    ''' <summary>One pass over every running viewer process. Acknowledged only when a window answered
    ''' "handled" - the shape of the viewer's own forward to this program (helper windows answer 0).</summary>
    Private Function TrySendOnce(bytes As Byte(), sendTimeoutMs As Integer) As ForwardAttempt
        Dim delivered As Boolean = False
        For Each processName As String In ViewerProcessNames
            Dim processes As Process() = Nothing
            Try
                processes = Process.GetProcessesByName(processName)
                For Each proc As Process In processes
                    For Each handle As IntPtr In TopLevelHandlesOf(proc.Id)
                        Dim cds As Program.COPYDATASTRUCT
                        cds.dwData = ViewerCopyDataTag
                        cds.cbData = bytes.Length
                        cds.lpData = Marshal.AllocHGlobal(bytes.Length)
                        Try
                            Marshal.Copy(bytes, 0, cds.lpData, bytes.Length)
                            Dim result As IntPtr = IntPtr.Zero
                            Dim sent As IntPtr = SendMessageTimeoutPhoneCode(handle, Program.WM_COPYDATA, IntPtr.Zero, cds,
                                SMTO_ABORTIFHUNG, CUInt(Math.Max(100, sendTimeoutMs)), result)
                            If sent <> IntPtr.Zero AndAlso result.ToInt64() = HandledResult Then
                                delivered = True
                            End If
                        Finally
                            Marshal.FreeHGlobal(cds.lpData)
                        End Try
                        If delivered Then Exit For
                    Next
                    If delivered Then Exit For
                Next
            Catch ex As Exception
                AppFileLogger.LogException("PhoneCodeRelay send", ex)
            Finally
                If processes IsNot Nothing Then
                    For Each proc As Process In processes
                        proc.Dispose()
                    Next
                End If
            End Try
            If delivered Then Exit For
        Next
        Return If(delivered, ForwardAttempt.Acknowledged, ForwardAttempt.NotAcknowledged)
    End Function

    ''' <summary>The oldest readable file version among the running viewers, or Nothing when none runs
    ''' or any of them cannot be read (an elevated one refuses MainModule) - unknown is never proof.</summary>
    Private Function RunningViewerVersion() As Version
        Dim oldest As Version = Nothing
        Dim anyProcess As Boolean = False
        For Each processName As String In ViewerProcessNames
            Dim processes As Process() = Nothing
            Try
                processes = Process.GetProcessesByName(processName)
                For Each proc As Process In processes
                    anyProcess = True
                    Dim v As Version = Nothing
                    Try
                        v = FileVersionOf(proc.MainModule.FileName)
                    Catch
                    End Try
                    If v Is Nothing Then Return Nothing
                    If oldest Is Nothing OrElse v < oldest Then oldest = v
                Next
            Catch
            Finally
                If processes IsNot Nothing Then
                    For Each proc As Process In processes
                        proc.Dispose()
                    Next
                End If
            End Try
        Next
        Return If(anyProcess, oldest, Nothing)
    End Function

    Private Function FileVersionOf(path As String) As Version
        Try
            Dim info As FileVersionInfo = FileVersionInfo.GetVersionInfo(path)
            Return ShareActivationPolicy.VersionOf(info.FileMajorPart, info.FileMinorPart, info.FileBuildPart, info.FilePrivatePart)
        Catch
            Return Nothing
        End Try
    End Function

    ''' <summary>All top-level windows of one process, including the hidden ones - the viewer's own
    ''' shape of the same walk (Process.MainWindowHandle is zero for a window in the tray).</summary>
    Private Function TopLevelHandlesOf(processId As Integer) As List(Of IntPtr)
        Dim result As New List(Of IntPtr)
        Try
            EnumWindowsPhoneCode(Function(hWnd As IntPtr, lParam As IntPtr) As Boolean
                                     Dim windowPid As UInteger = 0
                                     GetWindowThreadProcessIdPhoneCode(hWnd, windowPid)
                                     If CInt(windowPid) = processId Then result.Add(hWnd)
                                     Return True
                                 End Function, IntPtr.Zero)
        Catch
        End Try
        Return result
    End Function

    ''' <summary>Both shipped viewer exe names, under either of which a running window may answer.</summary>
    Private Shared Function ViewerProcessNames() As String()
        Return {"FastMediaSorter_LITE", "FastMediaSorter_x86"}
    End Function

    ' --- declarations, each renamed so no other partial/module's import collides -------------------

    Private Const SMTO_ABORTIFHUNG As UInteger = &H2

    ''' <summary>The viewer's tag ("FMSV"), one source: this project links SingleInstanceProtocol.vb,
    ''' the same file the viewer compiles, so the value cannot drift.</summary>
    Private Shared ReadOnly ViewerCopyDataTag As IntPtr = SingleInstanceProtocol.Tag_Pointer

    Private Const HandledResult As Long = SingleInstanceProtocol.Handled_Result

    Private Delegate Function EnumWindowsPhoneCodeProc(hWnd As IntPtr, lParam As IntPtr) As Boolean

    <DllImport("user32.dll", EntryPoint:="EnumWindows", SetLastError:=True)>
    Private Shared Function EnumWindowsPhoneCode(callback As EnumWindowsPhoneCodeProc, lParam As IntPtr) As Boolean
    End Function

    <DllImport("user32.dll", EntryPoint:="GetWindowThreadProcessId", SetLastError:=True)>
    Private Shared Function GetWindowThreadProcessIdPhoneCode(hWnd As IntPtr, ByRef processId As UInteger) As UInteger
    End Function

    <DllImport("user32.dll", EntryPoint:="SendMessageTimeoutW", SetLastError:=True, CharSet:=CharSet.Unicode, ExactSpelling:=True)>
    Private Shared Function SendMessageTimeoutPhoneCode(hWnd As IntPtr, msg As Integer, wParam As IntPtr,
                                                        ByRef lParam As Program.COPYDATASTRUCT,
                                                        flags As UInteger, timeout As UInteger,
                                                        ByRef result As IntPtr) As IntPtr
    End Function

End Class
