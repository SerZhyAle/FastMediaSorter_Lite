Option Strict On

Imports System.IO
Imports System.Text.RegularExpressions
Imports System.Windows.Forms
Imports FastMediaSorterCompanion
Imports Xunit

''' <summary>
''' CLIPBOARD-GUARD rule 6 (ticket 041): a credential reaches the clipboard only with the three privacy
''' formats, and only through <see cref="SecureClipboard"/>. The clipboard itself is process-global and
''' STA-bound, so what is testable is the DataObject that gets written and the source that writes it;
''' "the password is not in Win+V" stays a manual check on Windows 11.
''' </summary>
Public Class SecureClipboardTests

    <Fact>
    Public Sub SecretText_CarriesUnicodeTextAndAllThreePrivacyFormats()
        Dim data As DataObject = SecureClipboard.BuildSecretText("p@ss word")

        Assert.Equal("p@ss word", data.GetText(TextDataFormat.UnicodeText))
        Assert.Equal(3, SecureClipboard.PrivacyFormats.Length)
        For Each format As String In SecureClipboard.PrivacyFormats
            Assert.True(data.GetDataPresent(format, False), "missing privacy format " & format)
        Next
    End Sub

    <Fact>
    Public Sub PrivacyFormats_AreTheNamesWindowsDefines()
        ' Spelled out so a typo in the constants cannot pass by agreeing with itself.
        Assert.Equal("ExcludeClipboardContentFromMonitorProcessing", SecureClipboard.ExcludeFromMonitorFormat)
        Assert.Equal("CanIncludeInClipboardHistory", SecureClipboard.HistoryFormat)
        Assert.Equal("CanUploadToCloudClipboard", SecureClipboard.CloudFormat)
    End Sub

    <Fact>
    Public Sub HistoryAndCloudFlags_AreDwordZero()
        Dim data As DataObject = SecureClipboard.BuildSecretText("x")
        For Each format As String In {SecureClipboard.HistoryFormat, SecureClipboard.CloudFormat}
#Disable Warning WFDEV005 ' the raw stream is exactly what is under test; the typed reader would hide it
            Dim stream As MemoryStream = Assert.IsType(Of MemoryStream)(data.GetData(format, False))
#Enable Warning WFDEV005
            Assert.Equal(New Byte() {0, 0, 0, 0}, stream.ToArray())
        Next
    End Sub

    <Fact>
    Public Sub SetSecret_RefusesEmptyText()
        Assert.False(SecureClipboard.SetSecret(""))
        Assert.False(SecureClipboard.SetSecret(Nothing))
    End Sub

    ''' <summary>The one that keeps the rule true after this ticket: any new credential copy in the Share
    ''' Manager that reaches for <c>Clipboard.Set*</c> directly is a copy without the privacy flags.
    ''' The one exemption is named in the loop, with its reason.</summary>
    <Fact>
    Public Sub ShareManager_NeverWritesTheClipboardOutsideTheHelper()
        Dim root As String = FindRepoRoot()
        Assert.False(root Is Nothing, "repository root not found above " & AppContext.BaseDirectory)
        Dim dir As String = Path.Combine(root, "src", "FastMediaSorterCompanion")
        Dim pattern As New Regex("\bClipboard\s*\.\s*Set\w*\s*\(", RegexOptions.CultureInvariant)

        Dim offenders As New List(Of String)()
        For Each file As String In Directory.GetFiles(dir, "*.vb", SearchOption.AllDirectories)
            If file.Contains(Path.DirectorySeparatorChar & "obj" & Path.DirectorySeparatorChar) Then Continue For
            If Path.GetFileName(file) = "SecureClipboard.vb" Then Continue For
            ' The log archive is a redacted file drop (spec 039), not a credential.
            If Path.GetFileName(file) = "SendLogsFlow.vb" Then Continue For
            Dim lineNo As Integer = 0
            For Each line As String In System.IO.File.ReadAllLines(file)
                lineNo += 1
                Dim code As String = line.TrimStart()
                If code.StartsWith("'", StringComparison.Ordinal) Then Continue For
                If pattern.IsMatch(line) Then offenders.Add(Path.GetFileName(file) & ":" & lineNo)
            Next
        Next

        Assert.True(offenders.Count = 0, "Clipboard.Set* outside SecureClipboard: " & String.Join(", ", offenders))
    End Sub

    Private Shared Function FindRepoRoot() As String
        Dim dir = New DirectoryInfo(AppContext.BaseDirectory)
        While dir IsNot Nothing
            If System.IO.File.Exists(Path.Combine(dir.FullName, "FastMediaSorter.sln")) Then Return dir.FullName
            dir = dir.Parent
        End While
        Return Nothing
    End Function

End Class
