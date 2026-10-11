Option Explicit On
Option Strict On
Option Infer On
Option Compare Binary

Imports System.Runtime.InteropServices
Imports System.Threading
Imports System.Windows.Forms
Imports Xunit

' The production pinning partial needs only a form and the preference checkbox.
Partial Public Class Main_Form
    Inherits Form

    Friend ReadOnly chkbox_Top_Most As New CheckBox()

    Protected Overrides Sub Dispose(disposing As Boolean)
        If disposing Then chkbox_Top_Most.Dispose()
        MyBase.Dispose(disposing)
    End Sub
End Class

Public Class WindowPinningTests
    <DllImport("user32.dll")>
    Private Shared Function GetWindowLong(hwnd As IntPtr, index As Integer) As Integer
    End Function

    Private Shared Function NativeTopMost(window As Form) As Boolean
        Return (GetWindowLong(window.Handle, -20) And &H8) <> 0
    End Function

    Private Shared Sub OnStaThread(test As Action)
        Dim failure As Exception = Nothing
        Dim thread As New Thread(
            Sub()
                Try
                    test()
                Catch ex As Exception
                    failure = ex
                End Try
            End Sub)
        thread.SetApartmentState(ApartmentState.STA)
        thread.IsBackground = True
        thread.Start()
        Assert.True(thread.Join(TimeSpan.FromSeconds(15)), "Window pinning test did not finish.")
        If failure IsNot Nothing Then Throw failure
    End Sub

    <Fact>
    Public Sub Reopening_settings_repairs_a_band_changed_by_an_owned_sibling()
        OnStaThread(
            Sub()
                Using viewer As New Main_Form(), settings As New Form(), sibling As New Form()
                    viewer.TopMost = True
                    Dim viewerHandle As IntPtr = viewer.Handle
                    settings.Owner = viewer
                    viewer.PinToViewerBand(settings)
                    Dim settingsHandle As IntPtr = settings.Handle
                    sibling.Owner = viewer
                    sibling.TopMost = True
                    Dim siblingHandle As IntPtr = sibling.Handle
                    Assert.True(NativeTopMost(settings))

                    ' Windows demotes the owner and its owned windows together;
                    ' WinForms retains the viewer/settings TopMost property values.
                    sibling.TopMost = False
                    Assert.True(viewer.TopMost)
                    Assert.True(settings.TopMost)
                    Assert.False(NativeTopMost(settings))

                    viewer.PinToViewerBand(settings)
                    Assert.True(NativeTopMost(settings))
                End Using
            End Sub)
    End Sub

    <Fact>
    Public Sub Settings_follow_pin_toggle_and_temporary_suspension()
        OnStaThread(
            Sub()
                Using viewer As New Main_Form(), settings As New Form()
                    Dim viewerHandle As IntPtr = viewer.Handle
                    settings.Owner = viewer
                    Dim settingsHandle As IntPtr = settings.Handle
                    viewer.chkbox_Top_Most.Checked = True
                    viewer.TopMost = True
                    viewer.PinToViewerBand(settings)
                    Assert.True(NativeTopMost(settings))

                    Dim wasPinned As Boolean = viewer.SuspendAlwaysOnTop()
                    viewer.PinToViewerBand(settings)
                    Assert.True(wasPinned)
                    Assert.False(NativeTopMost(settings))

                    viewer.ResumeAlwaysOnTop(wasPinned)
                    viewer.PinToViewerBand(settings)
                    Assert.True(NativeTopMost(settings))

                    viewer.chkbox_Top_Most.Checked = False
                    viewer.TopMost = False
                    viewer.PinToViewerBand(settings)
                    Assert.False(NativeTopMost(settings))
                End Using
            End Sub)
    End Sub
End Class
