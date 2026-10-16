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

    ''' <summary>The Share Manager has a second, deliberate writer: <c>ClipboardWin32.PlaceNow</c> puts a value the person asked to
    ''' receive (CLIPBOARD-GUARD item Q: no privacy marker, the value is supposed to stay). The scan above cannot see it, because it
    ''' calls the Win32 function and not the Clipboard class; this one holds that the only call site of <c>SetClipboardData</c> in
    ''' the Share Manager is inside that one function, so a new raw writer cannot appear unnoticed.</summary>
    <Fact>
    Public Sub ShareManager_HasOneWin32ClipboardWriter_AndItIsPlaceNow()
        Dim root As String = FindRepoRoot()
        Assert.False(root Is Nothing, "repository root not found above " & AppContext.BaseDirectory)
        Dim dir As String = Path.Combine(root, "src", "FastMediaSorterCompanion")
        Dim pattern As New Regex("\bSetClipboardData\s*\(", RegexOptions.CultureInvariant)

        Dim offenders As New List(Of String)()
        Dim sitesInPlaceNow As Integer = 0
        For Each file As String In Directory.GetFiles(dir, "*.vb", SearchOption.AllDirectories)
            If file.Contains(Path.DirectorySeparatorChar & "obj" & Path.DirectorySeparatorChar) Then Continue For
            Dim inPlaceNow As Boolean = False
            Dim lineNo As Integer = 0
            For Each line As String In System.IO.File.ReadAllLines(file)
                lineNo += 1
                Dim code As String = line.TrimStart()
                If code.StartsWith("'", StringComparison.Ordinal) Then Continue For
                If code.Contains("Function PlaceNow(") Then inPlaceNow = True
                If code.StartsWith("End Function", StringComparison.Ordinal) Then inPlaceNow = False
                ' The P/Invoke declaration is not a call.
                If code.Contains("Function SetClipboardData(") Then Continue For
                If Not pattern.IsMatch(line) Then Continue For
                If Path.GetFileName(file) = "ClipboardWin32.vb" AndAlso inPlaceNow Then
                    sitesInPlaceNow += 1
                Else
                    offenders.Add(Path.GetFileName(file) & ":" & lineNo)
                End If
            Next
        Next

        Assert.True(offenders.Count = 0, "SetClipboardData outside ClipboardWin32.PlaceNow: " & String.Join(", ", offenders))
        Assert.Equal(1, sitesInPlaceNow)
    End Sub

    ''' <summary>CLIPBOARD-GUARD item M: a send the person started sends what the clipboard holds, whatever markers it carries - no
    ''' preview, no filter. The reading path (the Win32 reader, the exchange and its coordinator) therefore never names a privacy
    ''' format; a marker check added there would silently hold back the password the person just chose to send.</summary>
    <Fact>
    Public Sub A_send_never_looks_at_the_privacy_markers_of_the_clipboard()
        Dim root As String = FindRepoRoot()
        Assert.False(root Is Nothing, "repository root not found above " & AppContext.BaseDirectory)
        Dim pattern As New Regex("ExcludeClipboardContentFromMonitorProcessing|CanIncludeInClipboardHistory|CanUploadToCloudClipboard|PrivacyFormats|SecureClipboard", RegexOptions.CultureInvariant)
        Dim offenders As New List(Of String)()
        For Each name As String In {"ClipboardWin32.vb", "ClipboardExchange.vb", "ClipboardCoordinator.vb"}
            Dim file As String = Path.Combine(root, "src", "FastMediaSorterCompanion", "Core", name)
            Assert.True(System.IO.File.Exists(file), "missing " & file)
            Dim lineNo As Integer = 0
            For Each line As String In System.IO.File.ReadAllLines(file)
                lineNo += 1
                If line.TrimStart().StartsWith("'", StringComparison.Ordinal) Then Continue For
                If pattern.IsMatch(line) Then offenders.Add(name & ":" & lineNo)
            Next
        Next
        Assert.True(offenders.Count = 0, "the reading path looks at privacy markers: " & String.Join(", ", offenders))
    End Sub

    ''' <summary>CLIPBOARD-GUARD item B: the clipboard is opened with a bounded retry of 12 attempts, 15 ms apart - a number the
    ''' contract states, so a change to it is a decision and not a drift.</summary>
    <Fact>
    Public Sub The_bounded_retry_is_twelve_attempts_fifteen_milliseconds_apart()
        Assert.Equal(12, ClipboardWin32.OpenAttempts)
        Assert.Equal(15, ClipboardWin32.OpenPauseMs)
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
