#If Not NETFRAMEWORK Then
Option Explicit On
Option Strict On
Option Infer On
Option Compare Binary

Imports System
Imports System.Collections.Generic
Imports System.Diagnostics
Imports System.IO
Imports System.Text
Imports System.Threading
Imports System.Threading.Tasks
Imports System.Reflection
Imports Xunit
Imports Xunit.Abstractions

Public Class PdfReaderSourceTests
    Private ReadOnly output As ITestOutputHelper
    Public Sub New(output As ITestOutputHelper)
        Me.output = output
    End Sub

    Friend Shared Function Fixture(Optional pageCount As Integer = 2) As Byte()
        Dim objects As New List(Of String) From {
            "<< /Type /Catalog /Pages 2 0 R /Outlines 4 0 R >>", "",
            "<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica >>",
            "<< /Type /Outlines /First 5 0 R /Last 5 0 R /Count 1 >>",
            "<< /Title (Chapter one) /Parent 4 0 R /Dest [6 0 R /Fit] >>"}
        Dim pages As New StringBuilder()
        For index = 0 To pageCount - 1
            Dim pageObject = objects.Count + 1
            pages.Append(pageObject).Append(" 0 R ")
            objects.Add("<< /Type /Page /Parent 2 0 R /MediaBox [0 0 300 600] /Resources << /Font << /F1 3 0 R >> >> /Contents " & (pageObject + 1).ToString() & " 0 R /Annots [" & (pageObject + 2).ToString() & " 0 R] /Rotate " & If(index = 1, "90", "0") & " >>")
            Dim content = "BT /F1 16 Tf 20 500 Td (Reader sample page " & (index + 1).ToString() & ") Tj ET"
            objects.Add("<< /Length " & content.Length.ToString() & " >>" & vbLf & "stream" & vbLf & content & vbLf & "endstream")
            objects.Add("<< /Type /Annot /Subtype /Link /Rect [20 490 150 520] /A << /S /URI /URI (https://example.com/reader) >> >>")
        Next
        objects(1) = "<< /Type /Pages /Kids [" & pages.ToString() & "] /Count " & pageCount.ToString() & " >>"
        Dim pdf As New StringBuilder("%PDF-1.7" & vbLf)
        Dim offsets As New List(Of Integer)()
        For index = 0 To objects.Count - 1
            offsets.Add(pdf.Length)
            pdf.Append(index + 1).Append(" 0 obj").Append(vbLf).Append(objects(index)).Append(vbLf).Append("endobj").Append(vbLf)
        Next
        Dim xref = pdf.Length
        pdf.Append("xref").Append(vbLf).Append("0 ").Append(objects.Count + 1).Append(vbLf).Append("0000000000 65535 f ").Append(vbLf)
        For Each offset In offsets
            pdf.Append(offset.ToString("0000000000")).Append(" 00000 n ").Append(vbLf)
        Next
        pdf.Append("trailer << /Size ").Append(objects.Count + 1).Append(" /Root 1 0 R >>").Append(vbLf).Append("startxref").Append(vbLf).Append(xref).Append(vbLf).Append("%%EOF")
        Return Encoding.ASCII.GetBytes(pdf.ToString())
    End Function

    <Fact>
    Public Sub Native_text_geometry_outline_links_and_permissions_are_available()
        Using source As New PdfDocumentSource()
            source.Open(Fixture())
            Assert.True(source.IsOpen, source.ErrorMessage)
            Dim info = source.ReadReaderInfo()
            Assert.True(info.CanCopy)
            Assert.True(info.CanReadAloud)
            Assert.False(info.HasForms)
            Assert.Equal("Chapter one", Assert.Single(info.Outline).Title)
            Assert.Equal(1, info.Outline(0).Page)
            Dim page = source.ReadTextPage(1)
            Assert.Contains("Reader sample page 1", page.Text)
            Assert.Equal(page.Text.Length, page.Boxes.Count)
            Assert.Contains(page.Boxes, Function(box) box.Width > 0 AndAlso box.Height > 0)
            Assert.Equal("https://example.com/reader", Assert.Single(page.Links).Uri)
            Assert.Equal(1, source.ReadTextPage(2).Rotation)
            Assert.Throws(Of OperationCanceledException)(Sub() source.ReadTextPage(1, New CancellationToken(True)))
        End Using
    End Sub

    <Fact>
    Public Sub Reader_password_retry_reuses_the_owned_snapshot_and_distinguishes_wrong_password()
        Using source As New PdfDocumentSource()
            source.OpenReader(Path.Combine(AppContext.BaseDirectory, "TestData", "documents", "password.pdf"), Nothing, CancellationToken.None)
            Assert.Equal(PdfDocumentSource.OpenFailure.PasswordRequired, source.Failure)
            source.ReopenWithPassword("wrong")
            Assert.False(source.IsOpen)
            Assert.Equal(PdfDocumentSource.OpenFailure.WrongPassword, source.Failure)
            source.ReopenWithPassword("preview")
            Assert.True(source.IsOpen, source.ErrorMessage)
            Assert.Equal(PdfDocumentSource.OpenFailure.None, source.Failure)
            Assert.True(source.ReadReaderInfo().CanCopy)
        End Using
    End Sub

    <Fact>
    Public Sub Snapshot_releases_original_and_remains_readable_after_move()
        Dim original = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N") & ".pdf")
        Dim moved = original & ".moved"
        Try
            File.WriteAllBytes(original, Fixture())
            Using source As New PdfDocumentSource()
                source.OpenReader(original, Nothing, CancellationToken.None)
                Assert.True(source.IsOpen, source.ErrorMessage)
                File.Move(original, moved)
                Assert.Contains("Reader sample", source.ReadTextPage(1).Text)
                Using image = source.RenderPage(1, 600, 600)
                    Assert.NotNull(image)
                End Using
            End Using
        Finally
            File.Delete(original)
            File.Delete(moved)
        End Try
    End Sub

    <Fact>
    Public Sub Disposing_during_queued_native_work_keeps_the_snapshot_lease_until_completion()
        Dim original = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N") & ".pdf")
        Dim gate = DirectCast(GetType(PdfDocumentSource).GetField("NativeGate", BindingFlags.NonPublic Or BindingFlags.Static).GetValue(Nothing), SemaphoreSlim)
        Dim held As Boolean
        Try
            File.WriteAllBytes(original, Fixture())
            Using source As New PdfDocumentSource()
                source.OpenReader(original, Nothing, CancellationToken.None)
                Dim snapshot = CStr(GetType(PdfDocumentSource).GetField("_snapshot", BindingFlags.NonPublic Or BindingFlags.Instance).GetValue(source))
                gate.Wait()
                held = True
                Dim render = Task.Run(Function() source.RenderPage(1, 600, 600))
                Assert.True(SpinWait.SpinUntil(Function() CInt(GetType(PdfDocumentSource).GetField("activeReads", BindingFlags.NonPublic Or BindingFlags.Instance).GetValue(source)) > 0, 3000))
                source.Dispose()
                Assert.True(File.Exists(snapshot))
                File.Delete(original)
                gate.Release()
                held = False
                Using bitmap = render.GetAwaiter().GetResult()
                    Assert.NotNull(bitmap)
                End Using
                Assert.False(File.Exists(snapshot))
            End Using
        Finally
            If held Then gate.Release()
            File.Delete(original)
        End Try
    End Sub

    <Fact>
    Public Sub Thousand_page_fixture_opens_lazily_and_renders_within_existing_limits()
        Using source As New PdfDocumentSource()
            Dim timer = Stopwatch.StartNew()
            source.Open(Fixture(1000))
            Assert.True(source.IsOpen, source.ErrorMessage)
            Assert.Equal(1000, source.PageCount)
            output.WriteLine("1000-page open: {0} ms", timer.ElapsedMilliseconds)
            timer.Restart()
            Using page = source.RenderPage(1000, 800, 600)
                Assert.NotNull(page)
                Assert.InRange(page.Width, 1, PdfDocumentSource.MaxRenderDimension)
                Assert.InRange(page.Height, 1, PdfDocumentSource.MaxRenderDimension)
            End Using
            output.WriteLine("Last-page render: {0} ms", timer.ElapsedMilliseconds)
            Assert.True(timer.Elapsed < TimeSpan.FromSeconds(30))
        End Using
    End Sub
End Class
#End If
