#If Not NETFRAMEWORK Then
Option Explicit On
Option Strict On
Option Infer On
Option Compare Binary

Partial Public Class Main_Form
    ' Runtime detection is lazy and occurs only after the EPUB opt-in and source guard.
    Friend Const EpubReaderHostAvailable As Boolean = True

    Private Function TryLoadDocumentContent(path As String) As Boolean
        Dim preferences = GetModernPreferences()
        Dim route = DocumentContentPolicy.SelectRoute(MediaKindClassifier.ExtensionOf(path), preferences.FullPdfReader, preferences.FullEpubReader, EpubReaderHostAvailable)
        Select Case route
            Case DocumentContentRoute.EpubReader
                LoadEpubReader(path)
                Return True
            Case DocumentContentRoute.PdfReader, DocumentContentRoute.PdfPreview
                LoadPdfDocument(path)
                Return True
            Case DocumentContentRoute.EpubMetadata
                ShowContentPlaceholder(path)
                Return True
            Case Else
                If TryLoadHtmlView(path, preferences) Then Return True
                If Not TextFileReader.IsTextExtension(MediaKindClassifier.ExtensionOf(path)) Then Return False
                LoadTextDocument(path)
                Return True
        End Select
    End Function

    Private Function DocumentSourceProvenance(path As String) As String
        If IsArchiveMode() Then Return "archive:" & OpenArchivePath() & "|" & CurrentFileDisplayName(path)
        If IsResourceMode() Then Return "resource:" & ActiveResourceSession().Locator & "|" & CurrentFileDisplayName(path)
        Return ""
    End Function

    Private Function DocumentReaderIdentity(path As String, ticks As Long, length As Long) As DocumentIdentity
        Dim result As New DocumentIdentity With {.Path = path, .LastWriteUtcTicks = ticks, .Length = length}
        If IsArchiveMode() Then
            Dim entry = archive_Session.Entries.FirstOrDefault(Function(item) String.Equals(item.TempPath, path, StringComparison.OrdinalIgnoreCase))
            If entry IsNot Nothing Then
                Dim archive As New IO.FileInfo(OpenArchivePath())
                result.Path = archive.FullName & "|" & entry.EntryName
                result.Provenance = "archive:" & archive.LastWriteTimeUtc.Ticks.ToString(Globalization.CultureInfo.InvariantCulture) & ":" & archive.Length.ToString(Globalization.CultureInfo.InvariantCulture)
                result.LastWriteUtcTicks = entry.LastWrite.ToUniversalTime().Ticks
                result.Length = entry.Size
            End If
        ElseIf IsResourceMode() Then
            Dim session = ActiveResourceSession()
            Dim entry = session.EntryAt(path)
            If entry IsNot Nothing Then
                result.Path = session.Locator & "|" & entry.FullPath
                result.Provenance = "resource:" & entry.VersionTag
                result.LastWriteUtcTicks = entry.LastWriteUtc.Ticks
                result.Length = entry.Length
            End If
        End If
        Return result
    End Function

    Private Sub OpenDocumentConversion(path As String)
        If IsSecureContentOnScreen() OrElse IsArchiveMode() OrElse IsResourceMode() Then
            Notify(Localization.T("Сначала извлеките файл из архива"), NotifyLevel.Warning)
            Return
        End If
        SlideShowStop()
        Dim exe = DocHtmlTranslate.FindExe()
        If exe.Length = 0 Then
            OfferInstallCompanion()
            Return
        End If
        Try
            Process.Start(New ProcessStartInfo(exe, DocHtmlTranslate.BuildArguments("", False, "", path)) With {.UseShellExecute = False})
        Catch ex As Exception
            Notify(Localization.TF("Не удалось открыть файл: {0}", IO.Path.GetFileName(path)), NotifyLevel.Warning)
        End Try
    End Sub
End Class
#End If
