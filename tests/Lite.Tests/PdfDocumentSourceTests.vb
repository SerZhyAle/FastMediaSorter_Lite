#If Not NETFRAMEWORK Then
Option Explicit On
Option Strict On
Option Infer On
Option Compare Binary

Imports System
Imports System.IO
Imports System.Drawing
Imports System.Threading
Imports Xunit

Public Class PdfDocumentSourceTests

        <Fact>
        Public Sub Empty_or_nonexistent_pdf_fails_gracefully()
            Using src As New PdfDocumentSource()
                src.Open("C:\nonexistent_file_123456.pdf")
                Assert.False(src.IsOpen)
                Assert.Equal(0, src.PageCount)
            End Using

            Using src As New PdfDocumentSource()
                src.Open(Array.Empty(Of Byte)())
                Assert.False(src.IsOpen)
                Assert.Equal(0, src.PageCount)
            End Using
        End Sub

        <Fact>
        Public Sub Corrupted_pdf_bytes_fail_gracefully()
            Using src As New PdfDocumentSource()
                Dim garbage As Byte() = New Byte() {&H25, &H50, &H44, &H46, 0, 1, 2, 3, 4, 5}
                src.Open(garbage)
                Assert.False(src.IsOpen)
                Assert.Equal(0, src.PageCount)
            End Using
        End Sub

        <Fact>
        Public Sub ModernViewerPreferences_remembers_and_records_document_pages()
            Dim p As New ModernViewerPreferences()
            Dim path1 As String = "C:\books\novel.pdf"
            Dim ticks1 As Long = 123456789L
            Dim len1 As Long = 1048576L

            ' Initial lookup on fresh preferences returns page 1
            Assert.Equal(1, p.RememberedDocumentPage(path1, ticks1, len1))

            ' Record page 12
            p.RecordDocumentPage(path1, ticks1, len1, 12)
            Assert.Equal(12, p.RememberedDocumentPage(path1, ticks1, len1))

            ' Different write ticks returns default page 1
            Assert.Equal(1, p.RememberedDocumentPage(path1, ticks1 + 10, len1))

            ' Different file length returns default page 1
            Assert.Equal(1, p.RememberedDocumentPage(path1, ticks1, len1 + 100))

            ' Record another document
            Dim path2 As String = "C:\docs\manual.pdf"
            p.RecordDocumentPage(path2, 555L, 222L, 48)
            Assert.Equal(48, p.RememberedDocumentPage(path2, 555L, 222L))
            Assert.Equal(12, p.RememberedDocumentPage(path1, ticks1, len1))
        End Sub

        <Fact>
        Public Sub Real_pdf_renders_pages_to_landscape_viewport_with_white_paper()
            Using document As New PdfDocumentSource()
                document.Open(Path.Combine(AppContext.BaseDirectory, "TestData", "documents", "48-pages.pdf"))
                Assert.True(document.IsOpen, document.ErrorMessage)
                Assert.Equal(48, document.PageCount)
                For Each number As Integer In {1, 12, 48}
                    Using image As Bitmap = document.RenderPage(number, 800, 600)
                        Assert.NotNull(image)
                        Assert.Equal(300, image.Width)
                        Assert.Equal(600, image.Height)
                        Assert.Equal(Color.White.ToArgb(), image.GetPixel(0, 0).ToArgb())
                        Assert.Equal(Color.Black.ToArgb(), image.GetPixel(150, 300).ToArgb())
                    End Using
                Next
                Assert.Null(document.RenderPage(49))
                Assert.Throws(Of OperationCanceledException)(Sub() document.RenderPage(1, 800, 600, New CancellationToken(True)))
                document.Dispose()
                Assert.Null(document.RenderPage(1))
                Assert.Equal(0, document.PageCount)
            End Using
        End Sub

        <Fact>
        Public Sub Password_pdf_is_identified_by_native_error_code_and_reopen_clears_error()
            Using document As New PdfDocumentSource()
                Dim fixture As String = Path.Combine(AppContext.BaseDirectory, "TestData", "documents", "password.pdf")
                document.Open(fixture)
                Assert.False(document.IsOpen)
                Assert.True(document.IsPasswordProtected)
                document.Open(fixture, "preview")
                Assert.True(document.IsOpen, document.ErrorMessage)
                Assert.False(document.IsPasswordProtected)
                Assert.Equal("", document.ErrorMessage)
            End Using
        End Sub

        <Fact>
        Public Sub Document_history_is_bounded_and_ignores_malformed_entries()
            Dim preferences As New ModernViewerPreferences With {.DocumentPageHistory = "[null,{}]"}
            Assert.Equal(1, preferences.RememberedDocumentPage("missing.pdf", 1, 1))
            For i As Integer = 1 To 201
                preferences.RecordDocumentPage(i.ToString() & ".pdf", i, i, 12)
            Next
            Assert.Equal(1, preferences.RememberedDocumentPage("1.pdf", 1, 1))
            Assert.Equal(12, preferences.RememberedDocumentPage("201.pdf", 201, 201))
        End Sub

    End Class
#End If
