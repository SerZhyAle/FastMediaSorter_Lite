#If Not NETFRAMEWORK Then
Option Strict On

Imports System.Windows.Forms

''' <summary>
''' The viewer's half of the dialog layer (ticket 124, UX-201): what <see cref="DialogLayer"/> does to a window
''' right before it is shown. It names Main_Form (for its z-order band) and the application theme service, so it
''' is not part of the unit-test assembly; <see cref="Install"/> puts it behind the layer's hooks when the viewer
''' starts.
''' </summary>
Friend NotInheritable Class ViewerDialogPrep

    Private Sub New()
    End Sub

    ''' <summary>Installs the viewer's preparation. Idempotent.</summary>
    Friend Shared Sub Install()
        DialogLayer.Preparation = AddressOf Prepare
        DialogLayer.ViewerLocator = AddressOf LocateViewer
    End Sub

    ''' <summary>The viewer window an owner belongs to: the owner itself, the owner of the owner, or - for a
    ''' window with no usable owner - the viewer found among the open windows. Nothing when there is none (a
    ''' dialog shown before the viewer exists).</summary>
    Friend Shared Function FindViewer(owner As IWin32Window) As Main_Form
        Dim f As Form = Nothing
        Try
            Dim c As Control = TryCast(owner, Control)
            If c IsNot Nothing Then f = If(TypeOf c Is Form, DirectCast(c, Form), c.FindForm())
            Dim guard As Integer = 0
            While f IsNot Nothing AndAlso guard < 16
                Dim viewer As Main_Form = TryCast(f, Main_Form)
                If viewer IsNot Nothing Then Return viewer
                f = f.Owner
                guard += 1
            End While
            For Each opened As Form In Application.OpenForms
                Dim candidate As Main_Form = TryCast(opened, Main_Form)
                If candidate IsNot Nothing Then Return candidate
            Next
        Catch ex As Exception
            AppFileLogger.LogException("DialogLayer find viewer", ex)
        End Try
        Return Nothing
    End Function

    Private Shared Function LocateViewer(owner As IWin32Window) As IWin32Window
        Return FindViewer(owner)
    End Function

    Private Shared Sub Prepare(window As Form, owner As IWin32Window)
        Dim viewer As Main_Form = FindViewer(owner)
        If viewer IsNot Nothing Then viewer.PinToViewerBand(window)
        Dim ownerForm As Form = TryCast(owner, Form)
        If ownerForm IsNot Nothing AndAlso ownerForm.TopMost AndAlso Not window.TopMost Then window.TopMost = True
        If window.StartPosition = FormStartPosition.WindowsDefaultLocation Then window.StartPosition = FormStartPosition.CenterParent
        If Localization.IsRightToLeft() AndAlso window.RightToLeft = RightToLeft.No Then window.RightToLeft = RightToLeft.Yes
        Dim ask As Ask_Dialog = TryCast(window, Ask_Dialog)
        If ask IsNot Nothing Then ask.ProblemInk = ThemeService.Palette.Danger
        ThemeService.Attach(window)
    End Sub

End Class
#End If
