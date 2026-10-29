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
    Public Const MaxReaderPages As Integer = 100000
    Public Property SourceWriteTicks As Long
    Public Property SourceLength As Long
    Private Shared ReadOnly NativeGate As New SemaphoreSlim(1, 1)
    Private _bytes As Byte()
    Private _password As String
    Private _snapshot As String = ""
    Public Enum OpenFailure
        None
        MissingFile
        PasswordRequired
        WrongPassword
        UnsupportedEncryption
        InvalidDocument
        RendererUnavailable
    End Enum
    Public Property Failure As OpenFailure
    Private ReadOnly lifetimeGate As New Object()
    Private activeReads As Integer
    Private ReadOnly rotations As New Collections.Concurrent.ConcurrentDictionary(Of Integer, Integer)()

    Friend Sub RotatePageClockwise(page As Integer)
        If page < 1 OrElse page > PageCount Then Return
        rotations.AddOrUpdate(page, 1, Function(key, value) (value + 1) Mod 4)
    End Sub

    Private Function PageQuarterTurns(page As Integer) As Integer
        Dim turns As Integer
        rotations.TryGetValue(page, turns)
        Return turns
    End Function
    Private retiredSnapshot As String = ""

    ' Full readers use an owned immutable snapshot. In-flight native reads never lock
    ' the original during sorting; the lease survives cancellation until PDFium exits.
    Public Sub OpenReader(filePath As String, password As String, token As CancellationToken)
        Dim folder = Path.Combine(Path.GetTempPath(), "fms-document-sessions")
        Directory.CreateDirectory(folder)
        Dim snapshot = Path.Combine(folder, Guid.NewGuid().ToString("N") & ".pdf")
        Try
            Dim before As New FileInfo(filePath)
            Dim ticks = before.LastWriteTimeUtc.Ticks
            Dim length = before.Length
            If length > 1024L * 1024 * 1024 Then Throw New InvalidDataException("PDF exceeds the 1 GiB session snapshot limit.")
            Using input As New FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite Or FileShare.Delete),
                  output As New FileStream(snapshot, FileMode.CreateNew, FileAccess.Write, FileShare.None)
                Dim buffer(81919) As Byte
                Dim total As Long
                Do
                    token.ThrowIfCancellationRequested()
                    Dim count = input.Read(buffer, 0, buffer.Length)
                    If count = 0 Then Exit Do
                    total += count
                    If total > 1024L * 1024 * 1024 Then Throw New InvalidDataException("PDF exceeds the session snapshot limit.")
                    output.Write(buffer, 0, count)
                Loop
            End Using
            Dim after As New FileInfo(filePath)
            If after.LastWriteTimeUtc.Ticks <> ticks OrElse after.Length <> length Then Throw New IOException("Document changed while opening.")
            Open(snapshot, password)
            SourceWriteTicks = ticks
            SourceLength = length
            If IsOpen AndAlso PageCount > MaxReaderPages Then
                _IsOpen = False
                Failure = OpenFailure.InvalidDocument
                _ErrorMessage = Localization.T("Не удалось открыть PDF")
            End If
            _FilePath = filePath
            _snapshot = snapshot
            snapshot = ""
        Finally
            If snapshot.Length > 0 Then TryDeleteSnapshot(snapshot)
        End Try
    End Sub

    Public Sub ReopenWithPassword(password As String)
        _password = password
        _ErrorMessage = ""
        _IsPasswordProtected = False
        Failure = OpenFailure.None
        OpenCore()
    End Sub

    Private Function BeginRead() As String
        SyncLock lifetimeGate
            If Not IsOpen Then Throw New ObjectDisposedException(NameOf(PdfDocumentSource))
            activeReads += 1
            Return If(_snapshot.Length > 0, _snapshot, FilePath)
        End SyncLock
    End Function

    Private Sub EndRead()
        SyncLock lifetimeGate
            activeReads -= 1
            If activeReads = 0 AndAlso retiredSnapshot.Length > 0 Then
                TryDeleteSnapshot(retiredSnapshot)
                retiredSnapshot = ""
            End If
        End SyncLock
    End Sub

    Private Shared Sub TryDeleteSnapshot(path As String)
        Try
            File.Delete(path)
        Catch ex As IOException
        Catch ex As UnauthorizedAccessException
        End Try
    End Sub

    Public Function ReadReaderInfo(Optional token As CancellationToken = Nothing) As PdfReaderInfo
        Dim path = BeginRead()
        Dim bytes = _bytes
        Dim password = _password
        Try
            NativeGate.Wait(token)
            Try
                Return PdfReaderNative.WithDocument(path, bytes, password, Function(doc) PdfReaderNative.ReadInfo(doc, token))
            Finally
                NativeGate.Release()
            End Try
        Finally
            EndRead()
        End Try
    End Function

    Public Function ReadTextPage(page As Integer, Optional token As CancellationToken = Nothing, Optional includeText As Boolean = True) As PdfTextPage
        If page < 1 OrElse page > PageCount Then Throw New ArgumentOutOfRangeException(NameOf(page))
        Dim path = BeginRead()
        Dim bytes = _bytes
        Dim password = _password
        Try
            NativeGate.Wait(token)
            Try
                Dim result = PdfReaderNative.WithDocument(path, bytes, password, Function(doc) PdfReaderNative.ReadPage(doc, page, token, includeText))
                For turn = 1 To PageQuarterTurns(page)
                    result.RotateClockwise()
                Next
                Return result
            Finally
                NativeGate.Release()
            End Try
        Finally
            EndRead()
        End Try
    End Function

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
        Failure = OpenFailure.None
        _IsPasswordProtected = False
        _FilePath = filePath
        _password = password
        If String.IsNullOrEmpty(filePath) OrElse Not File.Exists(filePath) Then
            Failure = OpenFailure.MissingFile
            _ErrorMessage = Localization.T("Файл не найден")
            Return
        End If
        OpenCore()
    End Sub

    Public Sub Open(bytes As Byte(), Optional password As String = Nothing)
        Dispose()
        _ErrorMessage = ""
        Failure = OpenFailure.None
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
                Using reader As IDocReader = CreateReader(If(_snapshot.Length > 0, _snapshot, FilePath), _bytes, _password, New PageDimensions(1.0))
                    _PageCount = reader.GetPageCount()
                    _IsOpen = PageCount > 0
                    If Not IsOpen Then _ErrorMessage = Localization.T("В документе нет страниц")
                End Using
            Catch ex As DocnetLoadDocumentException
                _IsPasswordProtected = ex.ErrorCode = 4UI
                Failure = If(IsPasswordProtected, If(_password Is Nothing, OpenFailure.PasswordRequired, OpenFailure.WrongPassword), If(ex.ErrorCode = 5UI, OpenFailure.UnsupportedEncryption, OpenFailure.InvalidDocument))
                Select Case Failure
                    Case OpenFailure.PasswordRequired : _ErrorMessage = Localization.T("Документ защищён паролем")
                    Case OpenFailure.WrongPassword : _ErrorMessage = Localization.T("Неверный пароль")
                    Case OpenFailure.UnsupportedEncryption : _ErrorMessage = Localization.T("Неподдерживаемое шифрование PDF")
                    Case Else : _ErrorMessage = Localization.T("Не удалось открыть PDF")
                End Select
            Catch ex As Exception
                Dim cause As Exception = ex
                While cause.InnerException IsNot Nothing
                    cause = cause.InnerException
                End While
                _ErrorMessage = If(TypeOf cause Is DllNotFoundException OrElse TypeOf cause Is BadImageFormatException OrElse TypeOf cause Is EntryPointNotFoundException,
                    Localization.T("Установите или восстановите компонент PDF в установщике приложения."), Localization.T("Не удалось открыть PDF"))
                Failure = If(TypeOf cause Is DllNotFoundException OrElse TypeOf cause Is BadImageFormatException OrElse TypeOf cause Is EntryPointNotFoundException, OpenFailure.RendererUnavailable, OpenFailure.InvalidDocument)
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
                               Optional token As CancellationToken = Nothing,
                               Optional annotations As Boolean = False) As Bitmap
        If Not IsOpen OrElse pageNumber < 1 OrElse pageNumber > PageCount Then Return Nothing
        ' Dispose never waits for a render on the UI thread. Capture inputs before work.
        Dim path As String = BeginRead()
        Dim bytes As Byte() = _bytes
        Dim password As String = _password
        targetWidth = Math.Max(1, Math.Min(MaxRenderDimension, targetWidth))
        targetHeight = Math.Max(1, Math.Min(MaxRenderDimension, targetHeight))
        Try
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
                        Dim raw As Byte() = If(annotations, page.GetImage(RenderFlags.RenderAnnotations Or RenderFlags.LimitImageCacheSize), page.GetImage())
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
                                Select Case PageQuarterTurns(pageNumber)
                                    Case 1 : result.RotateFlip(RotateFlipType.Rotate90FlipNone)
                                    Case 2 : result.RotateFlip(RotateFlipType.Rotate180FlipNone)
                                    Case 3 : result.RotateFlip(RotateFlipType.Rotate270FlipNone)
                                End Select
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
        Finally
            EndRead()
        End Try
    End Function

    Public Sub Dispose() Implements IDisposable.Dispose
        SyncLock lifetimeGate
            If _snapshot.Length > 0 Then
                If activeReads = 0 Then
                    TryDeleteSnapshot(_snapshot)
                Else
                    retiredSnapshot = _snapshot
                End If
                _snapshot = ""
            End If
        _IsOpen = False
        _PageCount = 0
        _bytes = Nothing
        _FilePath = ""
        _password = Nothing
        End SyncLock
    End Sub
End Class
#End If
