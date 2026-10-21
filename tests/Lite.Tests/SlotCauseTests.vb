#If Not NETFRAMEWORK Then
Option Strict On

Imports System
Imports System.IO
Imports Xunit

''' <summary>
''' The pure half of "the status line names the real reason" (owner request 2026-10-17): which cause a Windows
''' error CODE is, and which place in the path the sentence names. The code is read from the exception's HRESULT -
''' never from its message, which is in whatever language Windows speaks (APP-BEHAVIOUR rule 6) - so these tests
''' hand the mapping synthetic exceptions instead of a network that has to be down.
'''
''' The values are the ones measured on .NET 10 against a name that does not resolve (win32 53, an
''' <see cref="IOException"/>) and against a missing share on a live computer (win32 67).
''' Modern-only, like the feature.
''' </summary>
Public Class SlotCauseTests

    Private Shared Function Win32(code As Integer) As IOException
        ' HRESULT_FROM_WIN32: facility 7 in the top half, the code in the bottom.
        Return New IOException("x", &H80070000 Or code)
    End Function

    <Theory>
    <InlineData(53, SlotCause.HostNotFound)>
    <InlineData(51, SlotCause.HostNotFound)>
    <InlineData(1203, SlotCause.HostNotFound)>
    <InlineData(67, SlotCause.ShareNotFound)>
    <InlineData(1222, SlotCause.NetworkDown)>
    <InlineData(1231, SlotCause.NetworkDown)>
    <InlineData(1232, SlotCause.NetworkDown)>
    <InlineData(64, SlotCause.ConnectionLost)>
    <InlineData(59, SlotCause.ConnectionLost)>
    <InlineData(121, SlotCause.ConnectionLost)>
    <InlineData(1236, SlotCause.ConnectionLost)>
    <InlineData(21, SlotCause.NotReady)>
    <InlineData(1167, SlotCause.NotReady)>
    <InlineData(5, SlotCause.None)>
    <InlineData(3, SlotCause.None)>
    <InlineData(0, SlotCause.None)>
    Public Sub A_win32_code_maps_to_one_cause(code As Integer, expected As SlotCause)
        Assert.Equal(expected, SlotCauses.OfWin32(code))
        Assert.Equal(expected, SlotCauses.OfException(Win32(code)))
    End Sub

    ''' <summary>A missing folder and a denial are other states with their own sentences: they must not be
    ''' re-read as a network cause even though they are IOExceptions with a Win32 HRESULT.</summary>
    <Fact>
    Public Sub A_missing_folder_is_not_a_network_cause()
        Assert.Equal(SlotCause.None, SlotCauses.OfException(New DirectoryNotFoundException()))
        Assert.Equal(SlotCause.None, SlotCauses.OfException(New FileNotFoundException()))
        Assert.Equal(SlotCause.None, SlotCauses.OfException(New UnauthorizedAccessException()))
    End Sub

    <Fact>
    Public Sub An_IOException_without_a_win32_facility_names_nothing()
        ' COR_E_IO (0x80131620) is the framework's own, not an operating-system code.
        Assert.Equal(SlotCause.None, SlotCauses.OfException(New IOException("x", &H80131620)))
        Assert.Equal(SlotCause.None, SlotCauses.OfException(New IOException("x")))
    End Sub

    <Fact>
    Public Sub A_timeout_is_no_answer()
        Assert.Equal(SlotCause.NoAnswer, SlotCauses.OfException(New TimeoutException()))
    End Sub

    ''' <summary>A task wraps the real failure; the code is found under it.</summary>
    <Fact>
    Public Sub An_aggregate_is_unwrapped()
        Assert.Equal(SlotCause.HostNotFound, SlotCauses.OfException(New AggregateException(Win32(53))))
        Assert.Equal(SlotCause.None, SlotCauses.OfException(New AggregateException()))
        Assert.Equal(SlotCause.None, SlotCauses.OfException(Nothing))
    End Sub

    ' ------------------------------------------------------------------ the place ----

    <Theory>
    <InlineData("\\p7\down\_i2", "p7")>
    <InlineData("\\p7\down", "p7")>
    <InlineData("\\p7", "p7")>
    <InlineData("  \\NAS-1\media  ", "NAS-1")>
    <InlineData("\\?\UNC\p7\down\x", "p7")>
    <InlineData("Z:\x", "")>
    <InlineData("C:\Users\me", "")>
    <InlineData("\\?\C:\x", "")>
    <InlineData("", "")>
    Public Sub The_host_is_read_from_a_unc_path_only(path As String, expected As String)
        Assert.Equal(expected, SlotCauses.HostOf(path))
    End Sub

    <Theory>
    <InlineData("\\p7\down\_i2", "\\p7\down")>
    <InlineData("\\p7\down", "\\p7\down")>
    <InlineData("\\p7", "")>
    <InlineData("\\?\UNC\p7\down\x", "\\p7\down")>
    <InlineData("Z:\x", "")>
    Public Sub The_share_root_needs_a_share_segment(path As String, expected As String)
        Assert.Equal(expected, SlotCauses.ShareRootOf(path))
    End Sub

    <Fact>
    Public Sub A_missing_share_names_the_share_and_anything_else_names_the_computer()
        Assert.Equal("\\p7\down", SlotCauses.PlaceFor("\\p7\down\_i2", SlotCause.ShareNotFound))
        Assert.Equal("p7", SlotCauses.PlaceFor("\\p7\down\_i2", SlotCause.HostNotFound))
        Assert.Equal("p7", SlotCauses.PlaceFor("\\p7\down\_i2", SlotCause.NoAnswer))
    End Sub

    <Fact>
    Public Sub A_lettered_path_names_its_drive_and_a_bare_path_names_itself()
        Assert.Equal("Z:", SlotCauses.PlaceFor("Z:\x\y", SlotCause.NotReady))
        Assert.Equal("Z:", SlotCauses.PlaceFor("Z:\x\y", SlotCause.ShareNotFound))
        Assert.Equal("incoming", SlotCauses.PlaceFor("incoming", SlotCause.NoAnswer))
        Assert.Equal("p7", SlotCauses.PlaceFor("  \\p7\down  ", SlotCause.HostNotFound))
    End Sub

    ' ---------------------------------------------------------------- the verdict ----

    ''' <summary>The old call shape still works and carries no cause: the policy tests and every probe branch
    ''' that has nothing to name keep their meaning.</summary>
    <Fact>
    Public Sub A_verdict_carries_a_cause_and_a_reason_only_when_given_one()
        Dim plain As SlotVerdict = SlotVerdict.ForState(SlotState.Unreachable, DateTime.UtcNow)
        Assert.Equal(SlotCause.None, plain.Cause)
        Assert.Equal("", plain.ReasonText)

        Dim named As SlotVerdict = SlotVerdict.ForState(SlotState.Unreachable, DateTime.UtcNow, "d", SlotCause.HostNotFound)
        Assert.Equal(SlotCause.HostNotFound, named.Cause)
        Assert.Equal("d", named.Detail)

        Dim reasoned As SlotVerdict = SlotVerdict.ForState(SlotState.Denied, DateTime.UtcNow).WithReason("x")
        Assert.Equal("x", reasoned.ReasonText)
        Assert.Equal("", SlotVerdict.ForState(SlotState.Denied, DateTime.UtcNow).WithReason(Nothing).ReasonText)
    End Sub

End Class
#End If
