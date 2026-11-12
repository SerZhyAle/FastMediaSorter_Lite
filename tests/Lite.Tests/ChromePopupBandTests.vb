#If Not NETFRAMEWORK Then
Option Strict On

Imports System.IO
Imports Xunit

''' <summary>
''' L0302: an open popup chain is the top of the viewer's own band. A window shown without activation is not lifted
''' over its owner's other owned windows, so the chain asks for the top itself when it opens, and every path that
''' shows or raises another viewer window while a chain is open hands the top back to it. Z-order cannot be observed
''' without a screen, so what is held HERE is the wiring, by reading the sources; the hand scene is the acceptance.
''' </summary>
Public Class ChromePopupBandTests

    Private Shared Function Source(name As String) As String
        Dim root As New DirectoryInfo(AppContext.BaseDirectory)
        While root IsNot Nothing AndAlso Not File.Exists(Path.Combine(root.FullName, "src", "Main_Form.vb"))
            root = root.Parent
        End While
        Assert.NotNull(root)
        Dim fullPath As String = Path.Combine(root.FullName, "src", name)
        Assert.True(File.Exists(fullPath), "missing source: " & name)
        Return File.ReadAllText(fullPath).Replace(vbCrLf, vbLf).Replace(vbLf, vbCrLf)
    End Function

    Private Shared Function MemberBody(text As String, declared As String) As String
        Dim start As Integer = text.IndexOf(declared, StringComparison.Ordinal)
        Assert.True(start >= 0, "declaration not found: " & declared)
        Dim endSub As Integer = text.IndexOf(vbCrLf & "    End Sub", start, StringComparison.Ordinal)
        Dim endFunction As Integer = text.IndexOf(vbCrLf & "    End Function", start, StringComparison.Ordinal)
        Dim [end] As Integer = If(endSub >= 0 AndAlso (endFunction < 0 OrElse endSub < endFunction), endSub, endFunction)
        Assert.True([end] > start, "member end not found: " & declared)
        Return text.Substring(start, [end] - start)
    End Function

    <Fact>
    Public Sub The_chain_raises_every_level_root_first_without_activation_and_never_becomes_topmost_on_its_own()
        Dim menu As String = Source(Path.Combine("Chrome", "ChromeMenu.vb"))
        Dim raise As String = MemberBody(menu, "Friend Shared Sub RaiseOpenChain()")
        Assert.Contains("If Not PopupVisible Then Return", raise)
        Assert.Contains("popup.RootMenu()", raise)
        Assert.Contains("current = current.childMenu", raise)
        Assert.Contains("SetWindowPos(current.Handle, IntPtr.Zero, 0, 0, 0, 0, Swp_Raise)", raise)
        ' HWND_TOP inside the band, SWP_NOSIZE | SWP_NOMOVE | SWP_NOACTIVATE: no topmost flag, no activation.
        Assert.Contains("Private Const Swp_Raise As UInteger = &H13UI", menu)
        Assert.DoesNotContain("TopMost = True", raise)
        Dim show As String = MemberBody(menu, "Public Sub ShowMenu(")
        Assert.True(show.IndexOf("RaiseOpenChain()", StringComparison.Ordinal) > show.IndexOf("Show()", StringComparison.Ordinal),
                    "the chain asks for the top after it is shown")
        Assert.Contains("TopMost = viewer IsNot Nothing AndAlso viewer.TopMost", show)
    End Sub

    <Fact>
    Public Sub Every_path_that_shows_or_raises_a_viewer_window_hands_the_top_back_to_an_open_chain()
        Dim chrome As String = Source("Main_Form.Chrome.vb")
        Dim protectedRaise As String = MemberBody(chrome, "Private Sub ChromeRaiseProtectedSurfaces()")
        Assert.True(protectedRaise.IndexOf("ChromeMenu.RaiseOpenChain()", StringComparison.Ordinal) >
                    protectedRaise.IndexOf("ChromeRaiseWindow(recipients_Overlay)", StringComparison.Ordinal),
                    "the protected surfaces are raised first, the open list last")
        Dim panelShow As String = MemberBody(chrome, "Private Sub ApplyChromeVisibility(")
        Assert.True(panelShow.IndexOf("ChromeMenu.RaiseOpenChain()", StringComparison.Ordinal) >
                    panelShow.IndexOf("p.Host.Visible = True", StringComparison.Ordinal))
        Dim overlayShow As String = MemberBody(chrome, "Private Sub ApplyRecipientsOverlayVisibility()")
        Assert.True(overlayShow.IndexOf("ChromeMenu.RaiseOpenChain()", StringComparison.Ordinal) >
                    overlayShow.IndexOf("recipients_Overlay.Visible = True", StringComparison.Ordinal))
        Dim transport As String = MemberBody(Source("Main_Form.VideoControls.vb"), "Friend Sub ShowVideoControls()")
        Assert.True(transport.IndexOf("ChromeMenu.RaiseOpenChain()", StringComparison.Ordinal) >
                    transport.IndexOf("video_Chrome_Host.Show()", StringComparison.Ordinal))
        ' A panel's ink window coming up stands over the list too; the chain's own ink windows travel with it.
        Dim present As String = MemberBody(Source(Path.Combine("Chrome", "ChromeHost.vb")), "Public Sub Present(host As Form,")
        Assert.Contains("If reordered AndAlso TypeOf host IsNot ChromeMenu Then ChromeMenu.RaiseOpenChain()", present)
    End Sub

End Class
#End If
