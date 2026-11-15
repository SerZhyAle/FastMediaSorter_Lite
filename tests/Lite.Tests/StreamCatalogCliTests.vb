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
#Else
        ' Ticket L0317: the usage code moved from 2 to 10 and a missing input is 11 (APP-COMMANDS rule 11), on the
        ' modern leg only - the net48 branch refuses with 1 and is unchanged.
        Private Shared Function MissingPath(name As String) As String
            Return System.IO.Path.Combine(System.IO.Path.GetTempPath(), "fms-l0317-" & Guid.NewGuid().ToString("N"), name)
        End Function

        <Fact>
        Public Sub The_exit_codes_are_the_numbers_of_rule_11()
            Assert.Equal(0, StreamCatalogCli.ExitPass)
            Assert.Equal(1, StreamCatalogCli.ExitFailure)
            Assert.Equal(10, StreamCatalogCli.ExitUsage)
            Assert.Equal(11, StreamCatalogCli.ExitInputMissing)
        End Sub

        <Fact>
        Public Sub No_arguments_or_no_mode_is_usage_exit_10()
            Assert.Equal(10, StreamCatalogCli.Run(New String() {}))
            Assert.Equal(10, StreamCatalogCli.Run(Nothing))
            Assert.Equal(10, StreamCatalogCli.Run(New String() {"-o", "x.zip"}))
        End Sub

        <Theory>
        <InlineData("--build-stream-catalog")>
        <InlineData("/build-catalog")>
        <InlineData("--check-stream-catalog")>
        <InlineData("-check-catalog")>
        Public Sub A_switch_without_its_input_is_usage_exit_10(switch As String)
            Assert.Equal(10, StreamCatalogCli.Run(New String() {switch}))
        End Sub

        <Theory>
        <InlineData("--build-stream-catalog")>
        <InlineData("-build-catalog")>
        <InlineData("/build-stream-catalog")>
        <InlineData("--check-stream-catalog")>
        <InlineData("/check-catalog")>
        <InlineData("-check-stream-catalog")>
        Public Sub A_switch_whose_input_is_missing_is_exit_11(switch As String)
            Assert.Equal(11, StreamCatalogCli.Run(New String() {switch, MissingPath("none.m3u")}))
        End Sub

        <Fact>
        Public Sub The_shared_cores_report_a_missing_input_without_the_switch_text()
            Dim built As StreamCatalogRunResult = StreamCatalogCli.BuildCore(MissingPath("none.m3u"), Nothing, False, False)
            Assert.Equal(StreamCatalogCli.ExitInputMissing, built.ExitCode)
            Assert.False(built.Passed)
            Dim checked As StreamCatalogRunResult = StreamCatalogCli.CheckCore(MissingPath("none.zip"))
            Assert.Equal(StreamCatalogCli.ExitInputMissing, checked.ExitCode)
            Assert.False(checked.Passed)
        End Sub

        <Fact>
        Public Sub The_help_switch_is_still_exit_0()
            Assert.Equal(0, StreamCatalogCli.Run(New String() {"--help"}))
        End Sub
#End If

    End Class

End Namespace
