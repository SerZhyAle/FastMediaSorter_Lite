#If Not NETFRAMEWORK Then
Option Strict On

Imports System.IO
Imports System.Linq
Imports System.Text.RegularExpressions
Imports Xunit

''' <summary>
''' How a release build of the Share Manager receives the Google Drive OAuth client (specification 269,
''' Q269-7): build-time assembly metadata `FmsDriveClientId` / `FmsDriveClientSecret`, filled by the release
''' workflow from its secrets, read by ExchangeDriveClientConfig.Resolve. Read from the files that ship: the
''' Share Manager project and release.yml. A build without the values carries no metadata at all, and no
''' tracked file ever holds a value. Net10 only: these read repository files.
''' </summary>
Public Class DriveClientDeliveryTests

    <Fact>
    Public Sub TheShareManagerProjectEmitsTheClientOnlyWhenBothValuesAreGiven()
        Dim proj As String = ReadText("src", "FastMediaSorterCompanion", "FastMediaSorterCompanion.vbproj")
        For Each key As String In {"FmsDriveClientId", "FmsDriveClientSecret"}
            Dim line As String = proj.Split({vbLf}, StringSplitOptions.None).
                SingleOrDefault(Function(l) l.Contains("<AssemblyMetadata Include=""" & key & """"))
            Assert.NotNull(line)
            Assert.Contains("Value=""$(" & key & ")""", line)
            Assert.Contains("'$(FmsDriveClientId)' != ''", line)
            Assert.Contains("'$(FmsDriveClientSecret)' != ''", line)
        Next
    End Sub

    <Fact>
    Public Sub TheWorkflowPassesTheSecretsThroughTheEnvironmentOfTheShareManagerPublishOnly()
        Dim yml As String = ReadText(".github", "workflows", "release.yml")
        Dim stepStart As Integer = yml.IndexOf("- name: Publish Companion", StringComparison.Ordinal)
        Assert.True(stepStart >= 0)
        Dim stepEnd As Integer = yml.IndexOf("- name:", stepStart + 1, StringComparison.Ordinal)
        Dim publishStep As String = If(stepEnd < 0, yml.Substring(stepStart), yml.Substring(stepStart, stepEnd - stepStart))
        Assert.Contains("FmsDriveClientId: ${{ secrets.FMS_DRIVE_CLIENT_ID }}", publishStep)
        Assert.Contains("FmsDriveClientSecret: ${{ secrets.FMS_DRIVE_CLIENT_SECRET }}", publishStep)
        ' never on a command line, where a log or a process list could show it
        For Each l As String In yml.Split({vbLf}, StringSplitOptions.None).Where(Function(x) x.TrimStart().StartsWith("run:", StringComparison.Ordinal))
            Assert.DoesNotContain("FmsDriveClient", l)
            Assert.DoesNotContain("FMS_DRIVE_CLIENT", l)
        Next
        ' the secrets reach that step and the check right after it (L0308 section 2.1), no other
        Assert.Equal(2, Regex.Matches(yml, "secrets\.FMS_DRIVE_CLIENT_ID\b").Count)
        Assert.Equal(2, Regex.Matches(yml, "secrets\.FMS_DRIVE_CLIENT_SECRET\b").Count)
        Dim checkText As String = StepText(yml, "- name: Check the Share Manager carries the Google Drive client")
        Assert.Contains("FMS_DRIVE_CLIENT_ID: ${{ secrets.FMS_DRIVE_CLIENT_ID }}", checkText)
        Assert.Contains("FMS_DRIVE_CLIENT_SECRET: ${{ secrets.FMS_DRIVE_CLIENT_SECRET }}", checkText)
    End Sub

    <Fact>
    Public Sub L0308_the_release_stops_unless_the_published_Share_Manager_and_every_package_carry_the_pair()
        Dim yml As String = ReadText(".github", "workflows", "release.yml")
        Dim publishAt As Integer = yml.IndexOf("- name: Publish Companion", StringComparison.Ordinal)
        Dim checkAt As Integer = yml.IndexOf("- name: Check the Share Manager carries the Google Drive client", StringComparison.Ordinal)
        Assert.True(publishAt >= 0 AndAlso checkAt > publishAt)
        ' the check is the very next step after the publish, so nothing is staged from an unchecked exe
        Assert.Equal(checkAt, yml.IndexOf("- name:", publishAt + 1, StringComparison.Ordinal))
        Dim checkText As String = StepText(yml, "- name: Check the Share Manager carries the Google Drive client")
        Assert.Contains("Test-DriveClientArtifact.ps1 -Path companion-publish/FastMediaSorterCompanion.exe -Expect Configured -CompareEnvironment", checkText)
        Assert.Contains("::error::", checkText)
        Assert.Contains("exit 1", checkText)
        Assert.DoesNotContain("continue-on-error", checkText)

        ' the portable ZIP and both stages (the setups are compiled from them) carry the same pair
        Dim serverText As String = StepText(yml, "- name: Build Server edition installer")
        Assert.Matches("Test-DriveClientArtifact\.ps1 -Path ""dist/\$\{\{ steps\.pkg\.outputs\.name \}\},\$\{\{ steps\.stage\.outputs\.stage \}\},\$stage"" -Expect Configured", serverText)
    End Sub

    <Fact>
    Public Sub L0308_the_Store_package_is_built_with_the_pair_and_refused_without_it()
        Dim script As String = ReadText("publishing", "msix", "build-msix.ps1")
        Assert.Contains("[string] $DriveClientJson", script)
        Assert.Contains("[switch] $AllowUnconfiguredDrive", script)
        ' no pair and no explicit development switch: the build stops before it publishes
        Dim refuse As Integer = script.IndexOf("-and -not $AllowUnconfiguredDrive", StringComparison.Ordinal)
        Dim publish As Integer = script.IndexOf("& dotnet publish $companionProj", StringComparison.Ordinal)
        Assert.True(refuse > 0 AndAlso publish > refuse)
        ' the values live in the publish's environment only and the shell gets back what it had
        Assert.Contains("$env:FmsDriveClientId = $priorDriveId", script)
        Assert.Contains("$env:FmsDriveClientSecret = $priorDriveSecret", script)
        ' the published exe and the packed archive are both read back
        Assert.Contains("Test-DriveClientArtifact.ps1') -Path $companionExe -Expect Configured", script)
        Assert.Contains("Test-DriveClientArtifact.ps1') -Path $MsixPath -Expect Configured", script)
        ' no value is ever written out
        Assert.DoesNotMatch("Write-(Host|Output|Verbose)[^\n]*\$drive(Id|Secret)", script)
    End Sub

    Private Shared Function StepText(yml As String, header As String) As String
        Dim stepStart As Integer = yml.IndexOf(header, StringComparison.Ordinal)
        Assert.True(stepStart >= 0, "no step " & header)
        Dim stepEnd As Integer = yml.IndexOf("- name:", stepStart + 1, StringComparison.Ordinal)
        Return If(stepEnd < 0, yml.Substring(stepStart), yml.Substring(stepStart, stepEnd - stepStart))
    End Function

    <Fact>
    Public Sub NoTrackedSourceHoldsAGoogleClientValue()
        ' the shape of a Google desktop client id and of its secret; the values live in the release secrets only
        Dim clientId As New Regex("\d{6,}-[a-z0-9]{20,}\.apps\.googleusercontent\.com")
        Dim clientSecret As New Regex("GOCSPX-[A-Za-z0-9_\-]{10,}")
        Dim root As String = RepoRoot()
        Dim files = {"src", ".github", "publishing", "tools"}.
            Select(Function(d) Path.Combine(root, d)).Where(AddressOf Directory.Exists).
            SelectMany(Function(d) Directory.EnumerateFiles(d, "*.*", SearchOption.AllDirectories)).
            Where(Function(f) {".vb", ".vbproj", ".yml", ".ps1", ".json", ".iss", ".xml", ".props", ".targets"}.
                Contains(Path.GetExtension(f), StringComparer.OrdinalIgnoreCase)).
            Where(Function(f) Not f.Contains(Path.DirectorySeparatorChar & "bin" & Path.DirectorySeparatorChar) AndAlso
                              Not f.Contains(Path.DirectorySeparatorChar & "obj" & Path.DirectorySeparatorChar))
        For Each f As String In files
            Dim text As String = File.ReadAllText(f)
            Assert.False(clientId.IsMatch(text), "a Google client id in " & f)
            Assert.False(clientSecret.IsMatch(text), "a Google client secret in " & f)
        Next
    End Sub

    Private Shared Function ReadText(ParamArray parts As String()) As String
        Return File.ReadAllText(Path.Combine({RepoRoot()}.Concat(parts).ToArray())).Replace(vbCrLf, vbLf)
    End Function

    Private Shared Function RepoRoot() As String
        Dim dir = New DirectoryInfo(AppContext.BaseDirectory)
        While dir IsNot Nothing
            If File.Exists(Path.Combine(dir.FullName, "FastMediaSorter.sln")) Then Return dir.FullName
            dir = dir.Parent
        End While
        Throw New InvalidOperationException("repository root not found above " & AppContext.BaseDirectory)
    End Function

End Class
#End If
