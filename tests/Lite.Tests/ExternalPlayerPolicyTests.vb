Option Strict On

Imports System.Collections.Generic
Imports Xunit

''' <summary>
''' Ticket 090 (audit 057.S08-1): a video LibVLC could not open must not be handed to a shell
''' handler that is this very viewer, or the failed file comes straight back through
''' single-instance forwarding and the loop never ends.
''' </summary>
Public Class ExternalPlayerPolicyTests

    Private Shared ReadOnly Viewers As String() = {"FastMediaSorter_LITE", "FastMediaSorter_x86"}

    Private Shared Function Decide(handler As String, Optional tried As ISet(Of String) = Nothing,
                                   Optional automatic As Boolean = True,
                                   Optional path As String = "C:\v\a.mkv") As ExternalPlayerPolicy.Verdict
        Return ExternalPlayerPolicy.Decide(path, handler, Viewers, tried, automatic)
    End Function

    <Theory>
    <InlineData("C:\Program Files\FastMediaSorter\FastMediaSorter_LITE.exe")>
    <InlineData("C:\Program Files\FastMediaSorter\FastMediaSorter_x86.exe")>
    <InlineData("c:\apps\fastmediasorter_lite.EXE")>
    <InlineData("""C:\apps\FastMediaSorter_LITE.exe""")>
    Public Sub Either_viewer_exe_as_handler_is_refused(handler As String)
        Assert.Equal(ExternalPlayerPolicy.Verdict.HandlerIsViewer, Decide(handler))
    End Sub

    <Theory>
    <InlineData("C:\Program Files\VideoLAN\VLC\vlc.exe")>
    <InlineData("C:\Windows\System32\wmplayer.exe")>
    <InlineData("C:\tools\FastMediaSorter_LITE_helper.exe")>
    <InlineData("")>
    <InlineData("   ")>
    Public Sub Another_or_unknown_handler_is_allowed(handler As String)
        Assert.Equal(ExternalPlayerPolicy.Verdict.Allow, Decide(handler))
    End Sub

    <Fact>
    Public Sub A_handler_string_that_is_not_a_path_never_throws()
        Assert.Equal(ExternalPlayerPolicy.Verdict.Allow, Decide("C:\bad|<>path\vlc.exe"))
    End Sub

    <Fact>
    Public Sub A_path_already_handed_over_automatically_is_not_handed_over_again()
        Dim tried As ISet(Of String) = New HashSet(Of String) From {"C:\v\a.mkv"}
        Assert.Equal(ExternalPlayerPolicy.Verdict.AlreadyTried, Decide("C:\vlc.exe", tried))
        Assert.Equal(ExternalPlayerPolicy.Verdict.Allow, Decide("C:\vlc.exe", tried, path:="C:\v\other.mkv"))
    End Sub

    <Fact>
    Public Sub The_explicit_menu_item_ignores_the_session_set()
        Dim tried As ISet(Of String) = New HashSet(Of String) From {"C:\v\a.mkv"}
        Assert.Equal(ExternalPlayerPolicy.Verdict.Allow, Decide("C:\vlc.exe", tried, automatic:=False))
    End Sub

    <Fact>
    Public Sub The_explicit_menu_item_still_refuses_a_viewer_handler()
        Assert.Equal(ExternalPlayerPolicy.Verdict.HandlerIsViewer,
                     Decide("C:\x\FastMediaSorter_LITE.exe", automatic:=False))
    End Sub

    <Fact>
    Public Sub No_session_set_means_nothing_was_tried()
        Assert.Equal(ExternalPlayerPolicy.Verdict.Allow, Decide("C:\vlc.exe", Nothing))
    End Sub

End Class
