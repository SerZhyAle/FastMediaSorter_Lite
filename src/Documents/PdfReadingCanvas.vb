#If Not NETFRAMEWORK Then
Option Explicit On
Option Strict On
Option Infer On
Option Compare Binary

Imports System
Imports System.Collections.Generic
Imports System.Drawing
Imports System.Drawing.Imaging
Imports System.Linq
Imports System.Threading
Imports System.Threading.Tasks
Imports System.Windows.Forms

' Virtual page layout: geometry is cheap; only visible pages acquire raster buffers.
' The ordinary single-page mode continues to use the viewer's picture box/zoom model.
Friend NotInheritable Class PdfReadingCanvas
    Inherits ScrollableControl

    Private ReadOnly source As PdfDocumentSource
    Private ReadOnly preferences As ModernViewerPreferences
    Private ReadOnly lifetime As New CancellationTokenSource()
    Private ReadOnly cache As New Dictionary(Of Integer, Bitmap)()
    Private ReadOnly failed As New HashSet(Of Integer)()
    Private ReadOnly pageSizes As New Dictionary(Of Integer, SizeF)()
    Private rects As Rectangle()
    Private busy As Boolean
    Private currentPage As Integer = 1
    Private cacheBytes As Long
    Private layoutWidth As Integer = -1
    Private lastReportedPage As Integer
    Public Event PageChanged(page As Integer)
    Public Event PageMouseDown(page As Integer, location As PointF)
    Public Event PageMouseMove(page As Integer, location As PointF)
    Public Event PageMouseUp(page As Integer, location As PointF)
    Public Event PageOverlay(page As Integer, graphics As Graphics, bounds As Rectangle)
    <ComponentModel.DesignerSerializationVisibility(ComponentModel.DesignerSerializationVisibility.Hidden)>
    Public Property DocumentRightToLeft As Boolean
    Private pressedPage As Integer

    Private Function HitPage(location As Point, ByRef point As PointF) As Integer
        For index = 0 To rects.Length - 1
            Dim rect = rects(index)
            rect.Offset(AutoScrollPosition)
            If rect.Width <= 0 OrElse rect.Height <= 0 OrElse Not rect.Contains(location) Then Continue For
            Dim size As SizeF
            If Not pageSizes.TryGetValue(index + 1, size) Then Return 0
            point = New PointF(CSng((location.X - rect.Left) * size.Width / rect.Width), CSng((location.Y - rect.Top) * size.Height / rect.Height))
            Return index + 1
        Next
        Return 0
    End Function

    Protected Overrides Sub OnMouseDown(e As MouseEventArgs)
        MyBase.OnMouseDown(e)
        If e.Button <> MouseButtons.Left Then Return
        Focus()
        Dim point As PointF
        pressedPage = HitPage(e.Location, point)
        If pressedPage = 0 Then Return
        Capture = True
        RaiseEvent PageMouseDown(pressedPage, point)
    End Sub

    Protected Overrides Sub OnMouseMove(e As MouseEventArgs)
        MyBase.OnMouseMove(e)
        Dim point As PointF
        Dim page = HitPage(e.Location, point)
        If page = pressedPage AndAlso page > 0 Then RaiseEvent PageMouseMove(page, point)
    End Sub

    Protected Overrides Sub OnMouseUp(e As MouseEventArgs)
        MyBase.OnMouseUp(e)
        If e.Button <> MouseButtons.Left OrElse pressedPage = 0 Then Return
        Dim point As PointF
        Dim page = HitPage(e.Location, point)
        Dim original = pressedPage
        pressedPage = 0
        Capture = False
        RaiseEvent PageMouseUp(original, If(page = original, point, New PointF(-1, -1)))
    End Sub

    Public ReadOnly Property ViewportFraction As Double
        Get
            Dim rect = rects(currentPage - 1)
            Return If(rect.Height <= 0, 0, Math.Max(0, Math.Min(1, (-AutoScrollPosition.Y - rect.Top) / CDbl(rect.Height))))
        End Get
    End Property

    Public Sub New(source As PdfDocumentSource, preferences As ModernViewerPreferences)
        Me.source = source
        Me.preferences = preferences
        Dock = DockStyle.Fill
        DoubleBuffered = True
        AutoScroll = True
        TabStop = True
        AccessibleName = Localization.T("Чтение PDF")
        BackColor = SystemColors.AppWorkspace
        ReDim rects(source.PageCount - 1)
    End Sub

    Public Sub GoToPage(page As Integer, Optional viewport As Double = 0)
        currentPage = Math.Max(1, Math.Min(source.PageCount, page))
        BuildLayout()
        If preferences.PdfLayout = "continuous" Then AutoScrollPosition = New Point(0, rects(currentPage - 1).Top + CInt(rects(currentPage - 1).Height * Math.Max(0, Math.Min(1, viewport))))
        Invalidate()
    End Sub

    Private Function ScaleFor(size As SizeF, available As Integer, spread As Boolean) As Double
        Dim width = If(spread, Math.Max(1, (available - 12) \ 2), available)
        Select Case preferences.PdfZoom
            Case "actual" : Return DeviceDpi / 72.0
            Case "manual" : Return DeviceDpi / 72.0 * preferences.PdfZoomPercent / 100.0
            Case "fitPage" : Return Math.Min(width / size.Width, Math.Max(1, ClientSize.Height - 24) / size.Height)
            Case Else : Return width / size.Width
        End Select
    End Function

    Private Sub BuildLayout()
        If IsDisposed OrElse ClientSize.Width < 1 Then Return
        Dim available = Math.Max(1, ClientSize.Width - SystemInformation.VerticalScrollBarWidth - 24)
        Dim spread = preferences.PdfLayout = "spread"
        Dim pages = If(spread, DocumentReadingState.SpreadPages(currentPage, source.PageCount, preferences.PdfFirstPageAlone,
            preferences.PdfSpreadDirection = "rtl" OrElse (preferences.PdfSpreadDirection = "document" AndAlso DocumentRightToLeft)), Enumerable.Range(1, source.PageCount).ToArray())
        Dim top As Integer = 12
        Dim left As Integer = 12
        Dim widest As Integer = available
        Dim tallest As Integer
        Array.Clear(rects)
        For Each page In pages
            Dim size As SizeF
            If Not pageSizes.TryGetValue(page, size) Then size = New SizeF(612, 792)
            Dim scale = ScaleFor(size, available, spread)
            Dim width = Math.Max(1, Math.Min(32000, CInt(size.Width * scale)))
            Dim height = Math.Max(1, Math.Min(32000, CInt(size.Height * scale)))
            rects(page - 1) = New Rectangle(If(spread, left, Math.Max(12, (available - width) \ 2)), top, width, height)
            widest = Math.Max(widest, rects(page - 1).Right + 12)
            tallest = Math.Max(tallest, height + 24)
            If spread Then
                left += width + 12
            Else
                top = CInt(Math.Min(1000000000L, CLng(top) + height + 12))
            End If
        Next
        AutoScrollMinSize = New Size(widest, If(spread, tallest, top))
        layoutWidth = ClientSize.Width
    End Sub

    Protected Overrides Sub OnResize(e As EventArgs)
        MyBase.OnResize(e)
        layoutWidth = -1
        Invalidate()
    End Sub

    Protected Overrides Sub OnScroll(se As ScrollEventArgs)
        MyBase.OnScroll(se)
        Invalidate()
    End Sub

    Protected Overrides Sub OnMouseWheel(e As MouseEventArgs)
        If (ModifierKeys And Keys.Control) = Keys.Control Then
            preferences.PdfZoom = "manual"
            preferences.PdfZoomPercent = Math.Max(25, Math.Min(800, CInt(preferences.PdfZoomPercent * If(e.Delta > 0, 1.25, 0.8))))
            BuildLayout()
            Invalidate()
            If TypeOf e Is HandledMouseEventArgs Then DirectCast(e, HandledMouseEventArgs).Handled = True
            Return
        End If
        If preferences.PdfLayout = "spread" AndAlso VerticalScroll.Maximum <= ClientSize.Height Then
            GoToPage(currentPage + If(e.Delta > 0, -2, 2))
            ReportPage(currentPage)
            Return
        End If
        MyBase.OnMouseWheel(e)
    End Sub

    Private Sub ReportPage(page As Integer)
        currentPage = page
        If lastReportedPage = page Then Return
        lastReportedPage = page
        RaiseEvent PageChanged(page)
    End Sub

    Protected Overrides Sub OnPaint(e As PaintEventArgs)
        MyBase.OnPaint(e)
        If layoutWidth <> ClientSize.Width Then BuildLayout()
        Dim firstVisible As Integer
        Dim missing As Integer
        For index = 0 To rects.Length - 1
            Dim rect = rects(index)
            If rect.IsEmpty Then Continue For
            rect.Offset(AutoScrollPosition)
            If Not rect.IntersectsWith(ClientRectangle) Then Continue For
            If firstVisible = 0 Then firstVisible = index + 1
            e.Graphics.FillRectangle(Brushes.White, rect)
            Dim bitmap As Bitmap = Nothing
            If cache.TryGetValue(index + 1, bitmap) Then
                DrawPage(e.Graphics, bitmap, rect, preferences.PdfColorMode)
                RaiseEvent PageOverlay(index + 1, e.Graphics, rect)
            ElseIf failed.Contains(index + 1) Then
                TextRenderer.DrawText(e.Graphics, Localization.T("Не удалось открыть PDF"), Font, rect, Color.Black, TextFormatFlags.HorizontalCenter Or TextFormatFlags.VerticalCenter)
            ElseIf missing = 0 Then
                missing = index + 1
            End If
        Next
        If firstVisible > 0 Then ReportPage(If(preferences.PdfLayout = "spread", currentPage, firstVisible))
        If missing > 0 AndAlso Not busy Then BeginRender(missing)
        If missing = 0 AndAlso Not busy AndAlso cacheBytes + CLng(preferences.DocumentMemoryCacheMb) * 1024 * 1024 \ 10 <= CacheBudget(preferences) Then
            For distance = 1 To preferences.PdfPrefetchPages
                Dim nextPage = New Integer() {currentPage + distance, currentPage - distance}.FirstOrDefault(
                    Function(page) page >= 1 AndAlso page <= source.PageCount AndAlso Not cache.ContainsKey(page) AndAlso Not failed.Contains(page))
                If nextPage > 0 Then
                    BeginRender(nextPage)
                    Exit For
                End If
            Next
        End If
    End Sub

    Private Async Sub BeginRender(page As Integer)
        busy = True
        Dim token = lifetime.Token
        Dim image As Bitmap = Nothing
        Try
            If Not pageSizes.ContainsKey(page) Then
                Dim geometry = Await Task.Run(Function() source.ReadTextPage(page, token, includeText:=False), token).WaitAsync(token)
                If token.IsCancellationRequested OrElse IsDisposed Then Return
                pageSizes(page) = geometry.Size
                Dim oldTop = rects(currentPage - 1).Top
                Dim scrollY = -AutoScrollPosition.Y
                BuildLayout()
                If preferences.PdfLayout = "continuous" Then AutoScrollPosition = New Point(0, Math.Max(0, rects(currentPage - 1).Top + scrollY - oldTop))
            End If
            Dim rect = rects(page - 1)
            Dim target = RasterTarget(rect.Size, preferences)
            Dim renderTask As Task(Of Bitmap) = Task.Run(Function() source.RenderPage(page, target.Width, target.Height, token, annotations:=True), token)
            Using deadline = CancellationTokenSource.CreateLinkedTokenSource(token)
                deadline.CancelAfter(TimeSpan.FromSeconds(30))
                Try
                    image = Await renderTask.WaitAsync(deadline.Token)
                Catch ex As OperationCanceledException
                    Dim cleanup = renderTask.ContinueWith(Sub(done)
                        If done.Status = TaskStatus.RanToCompletion Then done.Result?.Dispose()
                        If done.IsFaulted Then GC.KeepAlive(done.Exception)
                    End Sub, TaskScheduler.Default)
                    Throw
                End Try
            End Using
            If token.IsCancellationRequested OrElse IsDisposed Then Return
            If image Is Nothing Then
                failed.Add(page)
            Else
                Dim bytes = CLng(image.Width) * image.Height * 4
                Dim budget = CacheBudget(preferences)
                For Each key In cache.Keys.OrderByDescending(Function(value) Math.Abs(value - currentPage)).ToArray()
                    If cacheBytes + bytes <= budget Then Exit For
                    cacheBytes -= CLng(cache(key).Width) * cache(key).Height * 4
                    cache(key).Dispose()
                    cache.Remove(key)
                Next
                cache(page) = image
                cacheBytes += bytes
                image = Nothing
                ' Changing an estimate keeps the same visible page at its viewport offset.
                Dim oldTop = rects(currentPage - 1).Top
                Dim scrollY = -AutoScrollPosition.Y
                BuildLayout()
                If preferences.PdfLayout = "continuous" Then AutoScrollPosition = New Point(0, Math.Max(0, rects(currentPage - 1).Top + scrollY - oldTop))
            End If
        Catch ex As OperationCanceledException
            If Not token.IsCancellationRequested Then failed.Add(page)
        Catch ex As Exception
            If Not token.IsCancellationRequested Then failed.Add(page)
        Finally
            image?.Dispose()
            busy = False
            If Not IsDisposed AndAlso Not token.IsCancellationRequested Then Invalidate()
        End Try
    End Sub

    Friend Shared Function RasterTarget(size As Size, preferences As ModernViewerPreferences) As Size
        Dim quality = If(preferences.PdfRenderQuality = "high", 2.0, If(preferences.PdfRenderQuality = "fast", 0.75, 1.0))
        Dim width = Math.Max(1, CInt(Math.Min(PdfDocumentSource.MaxRenderDimension, size.Width * quality)))
        Dim height = Math.Max(1, CInt(Math.Min(PdfDocumentSource.MaxRenderDimension, size.Height * quality)))
        Dim pixels = Math.Max(1, CLng(preferences.DocumentMemoryCacheMb) * 1024 * 1024 \ 40)
        Dim ratio = Math.Min(1, Math.Sqrt(pixels / CDbl(CLng(width) * height)))
        Return New Size(Math.Max(1, CInt(Math.Floor(width * ratio))), Math.Max(1, CInt(Math.Floor(height * ratio))))
    End Function

    Friend Shared Function CacheBudget(preferences As ModernViewerPreferences) As Long
        Return Math.Min(CLng(preferences.PdfRenderCacheMb) * 1024 * 1024, CLng(preferences.DocumentMemoryCacheMb) * 1024 * 1024 * 2 \ 5)
    End Function

    Friend Shared Sub DrawPage(graphics As Graphics, image As Image, rect As Rectangle, mode As String)
        If mode = "original" Then
            graphics.DrawImage(image, rect)
            Return
        End If
        Dim matrix As ColorMatrix
        If mode = "night" Then
            matrix = New ColorMatrix(New Single()() {New Single() {-1, 0, 0, 0, 0}, New Single() {0, -1, 0, 0, 0}, New Single() {0, 0, -1, 0, 0}, New Single() {0, 0, 0, 1, 0}, New Single() {1, 1, 1, 0, 1}})
        Else
            matrix = New ColorMatrix(New Single()() {New Single() {0.393F, 0.349F, 0.272F, 0, 0}, New Single() {0.769F, 0.686F, 0.534F, 0, 0}, New Single() {0.189F, 0.168F, 0.131F, 0, 0}, New Single() {0, 0, 0, 1, 0}, New Single() {0, 0, 0, 0, 1}})
        End If
        Using attributes As New ImageAttributes()
            attributes.SetColorMatrix(matrix)
            graphics.DrawImage(image, rect, 0, 0, image.Width, image.Height, GraphicsUnit.Pixel, attributes)
        End Using
    End Sub

    Protected Overrides Sub Dispose(disposing As Boolean)
        If disposing Then
            lifetime.Cancel()
            For Each image In cache.Values
                image.Dispose()
            Next
            cache.Clear()
            lifetime.Dispose()
        End If
        MyBase.Dispose(disposing)
    End Sub
End Class
#End If
