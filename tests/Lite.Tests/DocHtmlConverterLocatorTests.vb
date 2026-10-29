#If Not NETFRAMEWORK Then
Option Strict On

Imports Xunit

' Specification 276 T-2: the explicit converter location and the readiness text. The process probe itself is a
' hand check; these facts hold the pure halves.
Public Class DocHtmlConverterLocatorTests

    <Theory>
    <InlineData("C:\Tools\doc-html-translate.exe")>
    <InlineData("C:\Tools\DOC-HTML-TRANSLATE.EXE")>
    <InlineData("\\server\apps\doc-html-translate.exe")>
    Public Sub TheCommandLineExecutable_IsAccepted(path As String)
        Assert.True(DocHtmlConverterLocator.IsAcceptableExplicitPath(path))
    End Sub

    <Theory>
    <InlineData("")>
    <InlineData("doc-html-translate.exe")>
    <InlineData("C:\Tools\doc-html-ui.exe")>
    <InlineData("C:\Windows\System32\cmd.exe")>
    <InlineData("C:\Tools\doc-html-translate.exe.lnk")>
    <InlineData("C:\Tools\")>
    Public Sub AnythingElse_IsRefused(path As String)
        Assert.False(DocHtmlConverterLocator.IsAcceptableExplicitPath(path))
    End Sub

    <Fact>
    Public Sub VersionLine_TakesTheFirstNonEmptyLine()
        Assert.Equal("doc-html-translate 1.4.2", DocHtmlConverterLocator.VersionLine(vbCrLf & "  doc-html-translate 1.4.2  " & vbCrLf & "built 2026"))
    End Sub

    <Fact>
    Public Sub VersionLine_DropsControlCharactersAndIsBounded()
        Assert.Equal("v1", DocHtmlConverterLocator.VersionLine(ChrW(27) & "v" & ChrW(7) & "1"))
        Dim longLine As New String("x"c, 500)
        Assert.Equal(DocHtmlConverterLocator.VersionTextMaxLength, DocHtmlConverterLocator.VersionLine(longLine).Length)
    End Sub

    <Fact>
    Public Sub VersionLine_OfNothing_IsEmpty()
        Assert.Equal("", DocHtmlConverterLocator.VersionLine(Nothing))
        Assert.Equal("", DocHtmlConverterLocator.VersionLine(vbCrLf & "   " & vbLf))
    End Sub

    <Fact>
    Public Sub AnUnusableExplicitChoice_IsMissing_AndDiscoveryDoesNotReplaceIt()
        Dim probe = DocHtmlConverterLocator.Probe("C:\no-such-folder-276\doc-html-translate.exe")
        Assert.Equal(DocHtmlConverterState.Missing, probe.State)
        Assert.True(probe.FromExplicitChoice)
        Assert.Equal("C:\no-such-folder-276\doc-html-translate.exe", probe.ExePath)
    End Sub
End Class
#End If
