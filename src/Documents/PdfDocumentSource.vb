#If Not NETFRAMEWORK Then
Option Explicit On
Option Strict On
Option Infer On
Option Compare Binary

Imports System
Imports System.Drawing
Imports System.Drawing.Imaging
Imports System.IO
Imports System.Runtime.InteropServices
Imports System.Threading
Imports Docnet.Core
Imports Docnet.Core.Readers
Imports Docnet.Core.Models
Imports Docnet.Core.Exceptions

Public NotInheritable Class PdfDocumentSource
    Implements IDisposable

    Public Const MaxRenderDimension As Integer = 4096
    Private Shared ReadOnly NativeGate As New SemaphoreSlim(1, 1)
    Private _bytes As Byte()
    Private _password As String

    Private _PageCount As Integer = 0
    Public ReadOnly Property PageCount As Integer
        Get
            Return _PageCount
        End Get
    End Property
    Private _IsOpen As Boolean = False
    Public ReadOnly Property IsOpen As Boolean
        Get
            Return _IsOpen
        End Get
    End Property
    Private _IsPasswordProtected As Boolean = False
    Public ReadOnly Property IsPasswordProtected As Boolean
        Get
            Return _IsPasswordProtected
        End Get
    End Property
    Private _ErrorMessage As String = ""
    Public ReadOnly Property ErrorMessage As String
        Get
            Return _ErrorMessage
        End Get
    End Property
    Private _FilePath As String = ""
    Public ReadOnly Property FilePath As String
        Get
            Return _FilePath
        End Get
    End Property

    Public Sub Open(filePath As String, Optional password As String = Nothing)
        Dispose()
        _ErrorMessage = ""
        _IsPasswordProtected = False
        _FilePath = filePath
        _password = password
        If String.IsNullOrEmpty(filePath) OrElse Not File.Exists(filePath) Then
            _ErrorMessage = Localization.T("Файл не найден")
            Return
        End If
        OpenCore()
    End Sub

    Public Sub Open(bytes As Byte(), Optional password As String = Nothing)
        Dispose()
        _ErrorMessage = ""
        _IsPasswordProtected = False
        _bytes = bytes
        _password = password
        If bytes Is Nothing OrElse bytes.Length = 0 Then
            _ErrorMessage = Localization.T("Не удалось открыть PDF")
            Return
        End If
        OpenCore()
    End Sub

    Private Sub OpenCore()
        NativeGate.Wait()
        Try
            Try
                Using reader As IDocReader = CreateReader(FilePath, _bytes, _password, New PageDimensions(1.0))
                    _PageCount = reader.GetPageCount()
                    _IsOpen = PageCount > 0
                    If Not IsOpen Then _ErrorMessage = Localization.T("В документе нет страниц")
                End Using
            Catch ex As DocnetLoadDocumentException
                _IsPasswordProtected = ex.ErrorCode = 4UI
                _ErrorMessage = If(IsPasswordProtected, Localization.T("Документ защищён паролем"), Localization.T("Не удалось открыть PDF"))
            Catch ex As Exception
                _ErrorMessage = Localization.T("Не удалось открыть PDF")
            End Try
        Finally
            NativeGate.Release()
        End Try
    End Sub

    Private Shared Function CreateReader(path As String, bytes As Byte(), password As String, dimensions As PageDimensions) As IDocReader
        If bytes IsNot Nothing Then Return DocLib.Instance.GetDocReader(bytes, password, dimensions)
        Return DocLib.Instance.GetDocReader(path, password, dimensions)
    End Function

    ' Caller owns the returned bitmap. Cancellation surrounds native calls;
    ' PDFium cannot interrupt a call in progress.
    Public Function RenderPage(pageNumber As Integer, Optional targetWidth As Integer = 1080,
                               Optional targetHeight As Integer = 1920,
                               Optional token As CancellationToken = Nothing) As Bitmap
        If Not IsOpen OrElse pageNumber < 1 OrElse pageNumber > PageCount Then Return Nothing
        ' Dispose never waits for a render on the UI thread. Capture inputs before work.
        Dim path As String = FilePath
        Dim bytes As Byte() = _bytes
        Dim password As String = _password
        targetWidth = Math.Max(1, Math.Min(MaxRenderDimension, targetWidth))
        targetHeight = Math.Max(1, Math.Min(MaxRenderDimension, targetHeight))
        token.ThrowIfCancellationRequested()
        NativeGate.Wait(token)
        Try
            token.ThrowIfCancellationRequested()
            Try
                Dim scale As Double
                Using doc As IDocReader = CreateReader(path, bytes, password, New PageDimensions(1.0))
                    Using page As IPageReader = doc.GetPageReader(pageNumber - 1)
                        scale = Math.Min(targetWidth / CDbl(Math.Max(1, page.GetPageWidth())), targetHeight / CDbl(Math.Max(1, page.GetPageHeight())))
                    End Using
                End Using
                token.ThrowIfCancellationRequested()
                Using doc As IDocReader = CreateReader(path, bytes, password, New PageDimensions(scale))
                    Using page As IPageReader = doc.GetPageReader(pageNumber - 1)
                        Dim width As Integer = page.GetPageWidth()
                        Dim height As Integer = page.GetPageHeight()
                        If width <= 0 OrElse height <= 0 OrElse width > MaxRenderDimension OrElse height > MaxRenderDimension Then Return Nothing
                        Dim raw As Byte() = page.GetImage()
                        token.ThrowIfCancellationRequested()
                        If raw Is Nothing OrElse raw.Length <> width * height * 4 Then Return Nothing
                        Using transparent As New Bitmap(width, height, PixelFormat.Format32bppArgb)
                            Dim data As BitmapData = transparent.LockBits(New Rectangle(0, 0, width, height), ImageLockMode.WriteOnly, PixelFormat.Format32bppArgb)
                            Try
                                Marshal.Copy(raw, 0, data.Scan0, raw.Length)
                            Finally
                                transparent.UnlockBits(data)
                            End Try
                            Dim result As New Bitmap(width, height, PixelFormat.Format32bppArgb)
                            Try
                                Using g As Graphics = Graphics.FromImage(result)
                                    g.Clear(Color.White)
                                    g.DrawImageUnscaled(transparent, 0, 0)
                                End Using
                                Return result
                            Catch
                                result.Dispose()
                                Throw
                            End Try
                        End Using
                    End Using
                End Using
            Catch ex As OperationCanceledException
                Throw
            Catch ex As Exception
                _ErrorMessage = FailureText.Describe(ex)
                Return Nothing
            End Try
        Finally
            NativeGate.Release()
        End Try
    End Function

    Public Sub Dispose() Implements IDisposable.Dispose
        _IsOpen = False
        _PageCount = 0
        _bytes = Nothing
        _FilePath = ""
        _password = Nothing
    End Sub
End Class
#End If
