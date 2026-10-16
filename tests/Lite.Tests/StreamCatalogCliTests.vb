Option Strict On
Option Explicit On

Imports System
Imports fmsl.Streams
Imports Xunit

Namespace Lite.Tests

    ''' <summary>
    ''' Unit tests for StreamCatalogCli command line recognition (Criteria 17-21).
    ''' Runs on BOTH net48 and net10 legs.
    ''' </summary>
    Public Class StreamCatalogCliTests

        <Theory>
        <InlineData("--build-stream-catalog")>
        <InlineData("-build-stream-catalog")>
        <InlineData("/build-stream-catalog")>
        <InlineData("--build-catalog")>
        <InlineData("-build-catalog")>
        <InlineData("/build-catalog")>
        <InlineData("--check-stream-catalog")>
        <InlineData("-check-stream-catalog")>
        <InlineData("/check-stream-catalog")>
        <InlineData("--check-catalog")>
        <InlineData("-check-catalog")>
        <InlineData("/check-catalog")>
        Public Sub CatalogSwitches_AreRecognized(switch As String)
            Dim args = New String() {switch, "test.m3u"}
            Assert.True(StreamCatalogCli.IsCatalogCommandLine(args))
        End Sub

        <Theory>
        <InlineData("C:\media\photo.jpg")>
        <InlineData("https://example.com/stream.mp4")>
        <InlineData("/fullscreen")>
        <InlineData("-v")>
        Public Sub NormalFilePathsAndOtherSwitches_AreNotCatalogCommands(arg As String)
            Dim args = New String() {arg}
            Assert.False(StreamCatalogCli.IsCatalogCommandLine(args))
        End Sub

        <Fact>
        Public Sub EmptyOrNullArgs_ReturnFalse()
            Assert.False(StreamCatalogCli.IsCatalogCommandLine(Nothing))
            Assert.False(StreamCatalogCli.IsCatalogCommandLine(New String() {}))
            Assert.False(StreamCatalogCli.IsCatalogCommandLine(New String() {"", "   "}))
        End Sub

#If NETFRAMEWORK Then
        <Fact>
        Public Sub NetFramework_RefusesSwitch()
            ' Criterion 21: on net48, calling RefuseNetFramework sets non-zero exit code
            Environment.ExitCode = 0
            Try
                StreamCatalogCli.RefuseNetFramework()
                Assert.NotEqual(0, Environment.ExitCode)
            Finally
                ' The exit code is the test host's own: left at 1 it ends the whole net48 run as "aborted".
                Environment.ExitCode = 0
            End Try
        End Sub
#End If

    End Class

End Namespace
