Option Strict On

Imports System.Collections.Generic
Imports System.Diagnostics
Imports System.Runtime.InteropServices
Imports System.Text

' Ticket 263 T-2: the Win32 half of the window picker. It only READS: the top-level windows, their titles,
' styles and owning processes - nothing is opened, captured or changed. Every judgement (which windows the
' picker offers, how they are labelled, how a remembered one is found again) is the pure LiveWindowCatalog
' in the Producer library, so the suite needs no desktop.

''' <summary>Enumerates the desktop's top-level windows as LiveWindowCandidate rows.</summary>
Friend NotInheritable Class LiveWindowEnumerator

    Private Const GWL_EXSTYLE As Integer = -20
    Private Const WS_EX_TOOLWINDOW As Integer = &H80
    Private Const DWMWA_CLOAKED As Integer = 14

    Private Delegate Function EnumWindowsProc(hWnd As IntPtr, lParam As IntPtr) As <MarshalAs(UnmanagedType.Bool)> Boolean

    <DllImport("user32.dll", EntryPoint:="EnumWindows", SetLastError:=True)>
    Private Shared Function EnumWindowsNative(callback As EnumWindowsProc, lParam As IntPtr) As <MarshalAs(UnmanagedType.Bool)> Boolean
    End Function

    <DllImport("user32.dll", EntryPoint:="IsWindowVisible")>
    Private Shared Function IsWindowVisibleNative(hWnd As IntPtr) As <MarshalAs(UnmanagedType.Bool)> Boolean
    End Function

    <DllImport("user32.dll", EntryPoint:="IsIconic")>
    Private Shared Function IsIconicNative(hWnd As IntPtr) As <MarshalAs(UnmanagedType.Bool)> Boolean
    End Function

    <DllImport("user32.dll", EntryPoint:="GetWindowTextLengthW", CharSet:=CharSet.Unicode)>
    Private Shared Function GetWindowTextLengthNative(hWnd As IntPtr) As Integer
    End Function

    <DllImport("user32.dll", EntryPoint:="GetWindowTextW", CharSet:=CharSet.Unicode)>
    Private Shared Function GetWindowTextNative(hWnd As IntPtr, text As StringBuilder, maxCount As Integer) As Integer
    End Function

    <DllImport("user32.dll", EntryPoint:="GetClassNameW", CharSet:=CharSet.Unicode)>
    Private Shared Function GetClassNameNative(hWnd As IntPtr, text As StringBuilder, maxCount As Integer) As Integer
    End Function

    <DllImport("user32.dll", EntryPoint:="GetWindowThreadProcessId")>
    Private Shared Function GetWindowThreadProcessIdNative(hWnd As IntPtr, ByRef processId As UInteger) As UInteger
    End Function

    <DllImport("user32.dll", EntryPoint:="GetWindowLongW")>
    Private Shared Function GetWindowLongNative(hWnd As IntPtr, index As Integer) As Integer
    End Function

    <DllImport("dwmapi.dll", EntryPoint:="DwmGetWindowAttribute")>
    Private Shared Function DwmGetWindowAttributeNative(hWnd As IntPtr, attribute As Integer, ByRef value As Integer, size As Integer) As Integer
    End Function

    Private Sub New()
    End Sub

    ''' <summary>Every top-level window with a title, in the desktop's z-order (topmost first). A failure of
    ''' any single window skips that window; a failure of the enumeration returns what was collected.</summary>
    Friend Shared Function Enumerate() As List(Of LiveWindowCandidate)
        Dim collector As New Collector()
        Dim callback As New EnumWindowsProc(AddressOf collector.One)
        Try
            EnumWindowsNative(callback, IntPtr.Zero)
        Catch
        End Try
        GC.KeepAlive(callback)
        Return collector.Windows
    End Function

    ''' <summary>The enumeration callback's state - a plain method on an object, so the delegate's lifetime is
    ''' obvious (the same shape as the monitor enumeration).</summary>
    Private NotInheritable Class Collector
        Public ReadOnly Windows As New List(Of LiveWindowCandidate)()
        Private ReadOnly _processNames As New Dictionary(Of Integer, String)()

        Public Function One(hWnd As IntPtr, lParam As IntPtr) As Boolean
            Try
                If Not IsWindowVisibleNative(hWnd) Then Return True
                Dim titleLength As Integer = GetWindowTextLengthNative(hWnd)
                If titleLength <= 0 Then Return True

                Dim title As New StringBuilder(titleLength + 1)
                GetWindowTextNative(hWnd, title, title.Capacity)
                Dim className As New StringBuilder(256)
                GetClassNameNative(hWnd, className, className.Capacity)

                Dim cloaked As Integer = 0
                If DwmGetWindowAttributeNative(hWnd, DWMWA_CLOAKED, cloaked, 4) <> 0 Then cloaked = 0

                Dim processId As UInteger = 0UI
                GetWindowThreadProcessIdNative(hWnd, processId)

                Windows.Add(New LiveWindowCandidate With {
                    .Handle = hWnd,
                    .Title = title.ToString(),
                    .ClassName = className.ToString(),
                    .ProcessId = CInt(processId),
                    .ProcessName = ProcessNameOf(CInt(processId)),
                    .IsVisible = True,
                    .IsCloaked = cloaked <> 0,
                    .IsToolWindow = (GetWindowLongNative(hWnd, GWL_EXSTYLE) And WS_EX_TOOLWINDOW) <> 0,
                    .IsMinimized = IsIconicNative(hWnd)
                })
            Catch
                ' One window that cannot be read is one window the picker does not offer.
            End Try
            Return True
        End Function

        ''' <summary>The process name without ".exe", "" when Windows will not say (a protected process).
        ''' Cached per enumeration: an application usually owns several windows.</summary>
        Private Function ProcessNameOf(processId As Integer) As String
            If processId <= 0 Then Return ""
            Dim name As String = Nothing
            If _processNames.TryGetValue(processId, name) Then Return name
            name = ""
            Try
                Using owner As Process = Process.GetProcessById(processId)
                    name = owner.ProcessName
                End Using
            Catch
                name = ""
            End Try
            _processNames(processId) = name
            Return name
        End Function
    End Class

End Class
