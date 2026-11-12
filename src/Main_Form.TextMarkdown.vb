#If Not NETFRAMEWORK Then
Option Explicit On
Option Strict On
Option Infer On
Option Compare Binary

Imports System
Imports System.Collections.Generic
Imports System.IO
Imports System.Text
Imports System.Windows.Forms

' Specification 277 step P3.7 (decision D07): the rendered view of a Markdown file in the full text reader.
' The page is built by the pure TextMarkdownRenderer and shown on the HTML host of ticket 275 - the same
' WebView2 route, sanitizer, request filter and toolbar as an .html file, never a second route. The page is held
' in memory only (a PackageHtmlDocumentSource over the bytes): no temporary file is written, so nothing plaintext
' is left on disk to clean up, and a file from a secure container is never offered a rendered copy.
'
' The text document stays loaded UNDER the page. That keeps one source of truth for the file, its scroll place and
' its encoding, keeps the editor's gate (FullTextReaderShown) true, and makes "show the source" a reload of the
' reader the file was opened in. Editing is always on the source: the editor calls ShowTextMarkdownSource first.
Partial Public Class Main_Form

    ' The key of the one document in the in-memory package; the host serves it under its own origin.
    Private Const TextMarkdownKey As String = "markdown.html"

    ''' <summary>True while the page on the HTML host is the rendered Markdown of the open file. Set by
    ''' <see cref="ShowTextMarkdownRendered"/>; ReleaseHtmlView clears it, so a stale value cannot reach a real page.</summary>
    Private text_Markdown_Page As Boolean
    ''' <summary>The file whose SOURCE the user chose (the toggle, the toolbar, an edit): the setting does not turn it back.</summary>
    Private text_Markdown_Source_Path As String = ""
    ''' <summary>The last file and navigation a rendered view was tried for. A failed host reloads the source reader
    ''' (FailHtmlView), which would try again at once; this pair stops that loop.</summary>
    Private text_Markdown_Attempt_Path As String = ""
    Private text_Markdown_Attempt_Generation As Integer = -1

    ''' <summary>The rendered page of the open file is on screen (the toggle reads "rendered").</summary>
    Private Function TextMarkdownRenderedShown() As Boolean
        Return text_Markdown_Page AndAlso IsHtmlViewShown() AndAlso IsTextDocumentShown()
    End Function

    ''' <summary>A whole Markdown file is open in the full reader: the one place a rendered view can be offered. A
    ''' paged file (over the editing ceiling) is only one window of the file, so it is never rendered.</summary>
    Private Function TextMarkdownCandidate() As Boolean
        If Not FullTextReaderShown() OrElse text_Virtual OrElse text_Binary_View Then Return False
        Return TextMarkdownRenderer.IsMarkdownExtension(MediaKindClassifier.ExtensionOf(text_Current_File_Path))
    End Function

    ''' <summary>The toggle is worth showing: the page is up, or the file could be rendered (viewer on, not secure).
    ''' Cheap on purpose - the panel asks it on every refresh; whether the runtime is installed is found out when
    ''' the page is asked for.</summary>
    Private Function TextMarkdownAvailable() As Boolean
        If TextMarkdownRenderedShown() Then Return True
        Return TextMarkdownCandidate() AndAlso GetModernPreferences().HtmlViewerEnabled AndAlso Not IsSecureContentOnScreen()
    End Function

    ''' <summary>Rendered to source or back; the one action behind the key, the document panel and the toolbar.</summary>
    Private Sub ToggleTextMarkdownView()
        If TextMarkdownRenderedShown() Then
            ShowTextMarkdownSource()
        ElseIf TextMarkdownCandidate() Then
            ShowTextMarkdownRendered(True)
        End If
    End Sub

    ''' <summary>
    ''' Back to the source reader: the page closes and the file is read again, at the place it was left. The choice
    ''' is remembered for this file, so the setting does not render it again behind the user (and an edit that
    ''' ends in a reload stays on the source).
    ''' </summary>
    Private Sub ShowTextMarkdownSource()
        If Not TextMarkdownRenderedShown() Then Return
        Dim path As String = text_Current_File_Path
        Dim scroll As Integer = text_Scroll_Offset
        text_Markdown_Source_Path = path
        ReleaseHtmlView()
        LoadTextDocument(path)
        If FullTextReaderShown() Then
            text_Scroll_Offset = scroll
            text_Pending_Top = Nothing
        End If
        ActiveControl = Nothing
        Focus()
        GetActivePictureBox()?.Invalidate()
    End Sub

    ''' <summary>
    ''' The end of a successful full-reader load: a Markdown file opens rendered when the setting says so and the
    ''' user has not chosen the source for it. Silent when the host cannot show it - the source reader is already
    ''' on screen and stays.
    ''' </summary>
    Private Sub OpenTextMarkdownIfWanted(filePath As String)
        If Not String.Equals(filePath, text_Markdown_Source_Path, StringComparison.OrdinalIgnoreCase) Then text_Markdown_Source_Path = ""
        If text_Markdown_Source_Path.Length > 0 Then Return
        If Not String.Equals(GetModernPreferences().TextMarkdownPresentation, "rendered", StringComparison.Ordinal) Then Return
        If Not TextMarkdownCandidate() Then Return
        If media_Generation = text_Markdown_Attempt_Generation AndAlso String.Equals(filePath, text_Markdown_Attempt_Path, StringComparison.OrdinalIgnoreCase) Then Return
        ShowTextMarkdownRendered(False)
    End Sub

    ''' <summary>
    ''' Puts the rendered page of the open Markdown file on the HTML host. <paramref name="userRequested"/> says the
    ''' reason out loud when it cannot (a notice); an automatic open leaves the source reader as it is.
    ''' </summary>
    Private Async Sub ShowTextMarkdownRendered(userRequested As Boolean)
        If Not TextMarkdownCandidate() Then Return
        Dim path As String = text_Current_File_Path
        Dim preferences As ModernViewerPreferences = GetModernPreferences()

        Dim refusal As HtmlOutcome = HtmlOutcome.Ok
        If IsSecureContentOnScreen() Then
            refusal = HtmlOutcome.SecureRefused
        ElseIf Not preferences.HtmlViewerEnabled Then
            refusal = HtmlOutcome.Disabled
        Else
            refusal = HtmlRuntimeService.Detect().Status
        End If
        If refusal <> HtmlOutcome.Ok Then
            If userRequested Then Notify(HtmlViewerText.Outcome(refusal), NotifyLevel.Warning)
            Return
        End If

        Dim source As PackageHtmlDocumentSource = Nothing
        Try
            source = BuildTextMarkdownSource(path, preferences)
        Catch ex As Exception When Not TypeOf ex Is OutOfMemoryException
            ' Never log the text or the path: the type of the failure is enough to find it.
            AppFileLogger.LogMessage("Markdown view not built: " & ex.GetType().Name)
        End Try
        If source Is Nothing Then
            If userRequested Then Notify(Localization.T("Файл слишком велик для оформленного вида - показан исходный текст"), NotifyLevel.Info)
            Return
        End If

        text_Markdown_Source_Path = ""
        text_Markdown_Attempt_Path = path
        text_Markdown_Attempt_Generation = media_Generation
        ' Any page already on the host (a second render, the settings changed) goes first; the text document stays.
        ReleaseHtmlView()
        html_View_File = path
        html_View_Identity = ""
        html_View_Online = Nothing
        text_Markdown_Page = True
        SyncDocumentPanel()
        Await OpenHtmlHostAsync(source, Nothing, False, media_Generation, path)
    End Sub

    ''' <summary>The page of the whole open file as bytes in memory, or Nothing when the file is too large to render.</summary>
    Private Function BuildTextMarkdownSource(path As String, preferences As ModernViewerPreferences) As PackageHtmlDocumentSource
        Dim lines As String() = text_Current_Result.Lines
        If lines Is Nothing OrElse text_Current_Result.TotalBytes > 4L * TextMarkdownRenderer.MaxSourceChars Then Return Nothing
        Dim markdown As String = String.Join(vbLf, lines)
        If Not TextMarkdownRenderer.IsRenderable(markdown) Then Return Nothing

        Dim systemBack As Drawing.Color = NeutralBackColor()
        Dim options As New TextMarkdownOptions With {
            .Theme = preferences.TextTheme,
            .SystemBack = systemBack,
            .SystemInk = GetOppositeColor(systemBack),
            .Title = "",
            .FontFamily = preferences.TextFontFamilyProportional,
            .CodeFontFamily = preferences.TextFontFamilyMonospace,
            .FontSizePoints = preferences.TextFontSize,
            .LineSpacing = preferences.TextLineSpacing}
        Dim page As String = TextMarkdownRenderer.Render(markdown, options)
        If page Is Nothing Then Return Nothing
        Dim bytes As Byte() = New UTF8Encoding(False).GetBytes(page)
        If bytes.LongLength > HtmlDocumentPolicy.MaxEntryBytes Then Return Nothing

        Dim entries As New Dictionary(Of String, HtmlPackageEntry)(StringComparer.Ordinal) From {
            {TextMarkdownKey, New HtmlPackageEntry(HtmlResourceKind.Document, "text/html; charset=utf-8")}}
        Dim request As New HtmlOpenRequest With {
            .SourceId = Guid.NewGuid().ToString("N"),
            .SourceKind = HtmlSourceKind.LocalFile,
            .DisplayName = System.IO.Path.GetFileName(path),
            .OriginalLocator = path,
            .EntryResourceKey = TextMarkdownKey,
            .Provenance = DocumentSourceProvenance(path),
            .IsSecure = False,
            .IsReadOnly = True}
        Return New PackageHtmlDocumentSource(request, entries, Function(requested, token) bytes)
    End Function

    ''' <summary>
    ''' The keys of this view, asked first by <c>TryHandleFullReaderKey</c>: Ctrl+Shift+M switches rendered and
    ''' source. While the page is up it owns the reader's keys: Ctrl+F finds in the page, and the commands that
    ''' belong to the source text say so instead of acting on text nobody sees. Every other key goes on.
    ''' </summary>
    Private Function TryHandleTextMarkdownKey(e As KeyEventArgs) As Boolean
        If e.KeyData = (Keys.Control Or Keys.Shift Or Keys.M) Then
            If Not TextMarkdownRenderedShown() AndAlso Not TextMarkdownCandidate() Then Return False
            ToggleTextMarkdownView()
            Return True
        End If
        If Not TextMarkdownRenderedShown() Then Return False
        Select Case e.KeyData
            Case Keys.Control Or Keys.F
                RunHtmlCommand(HtmlHostCommand.FindStart)
            Case Keys.Control Or Keys.Shift Or Keys.E, Keys.Control Or Keys.Shift Or Keys.S, Keys.F3, Keys.Shift Or Keys.F3,
                 Keys.Control Or Keys.G, Keys.Control Or Keys.PageDown, Keys.Control Or Keys.PageUp, Keys.Control Or Keys.Home, Keys.Control Or Keys.End
                Notify(Localization.T("Это действие доступно в исходном тексте - Ctrl+Shift+M переключает вид"), NotifyLevel.Info)
            Case Else
                Return False
        End Select
        Return True
    End Function

End Class
#End If
