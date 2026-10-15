Option Strict On

Imports System.Runtime.InteropServices
Imports System.Threading
Imports Windows.Graphics.Imaging
Imports Windows.Media.Capture
Imports Windows.Media.Capture.Frames

' Ticket 199 section 4, phase B3: the camera source - a device and a mode picker (the mode picker's
' judgement itself is pure: LiveCameraModes, the Producer library). Behind ILiveVideoSource so the
' suite drives fakes - no test needs a device (section 8).
'
' The route: Media Foundation through the Windows SDK projections this project already targets for
' the screen source (net10.0-windows10.0.19041, TargetPlatformMinVersion unchanged). The WinRT
' camera stack - MediaFrameSourceGroup for the device list, MediaCapture + MediaFrameReader for
' the frames - IS Media Foundation underneath, chosen over hand-declared MF COM interfaces because
' the second pass's interop lessons (section 12: every GUID checked against the SDK on disk, the
' memory-priced ones cost a day) say: where a projection exists, use it. No IID in this file.
'
' Safety rule 4: Windows decides device access. InitializeAsync throws where the privacy switch is
' off or the camera is gone; the start is refused with that reason (the window's one sentence),
' never worked around.
'
' Frame path: FrameArrived (a threadpool callback) -> VideoMediaFrame.SoftwareBitmap (the capture is
' initialized with MemoryPreference.Cpu, so frames are CPU-resident) -> SoftwareBitmap.Convert to
' Bgra8 where the device offered NV12/YUY2 -> LockBuffer + IMemoryBufferByteAccess (the same
' ComImport the screen source declared) -> LiveVideoCopy.FoldFrame into the ONE fixed buffer
' announced to the encoder. A pacing thread pushes that buffer at the fixed 25 fps whether or not
' the camera delivered a new frame - the same contract the screen source gives the encoder, so the
' 1 s keyframe cadence (3.2) never starves and a camera that stops delivering keeps a frozen
' picture instead of a dead stream.

''' <summary>The camera capture: one device, one mode, one pacing thread, BGRA frames.</summary>
Friend NotInheritable Class CameraCaptureSource
    Implements ILiveVideoSource

    Private ReadOnly _deviceIndex As Integer
    Private ReadOnly _modeIndex As Integer
    Private _displayName As String = "camera"
    Private _width As Integer
    Private _height As Integer
    Private _framesPerSecond As Integer = 25
    Private _sink As Action(Of LiveVideoFrame)
    Private _stop As Boolean
    Private _pacing As Thread

    Private _capture As MediaCapture
    Private _reader As MediaFrameReader

    ''' <summary>The one fixed buffer the encoder was told about at start (the rawvideo -s).</summary>
    Private _fixedBuffer As Byte()
    Private _sendBuffer As Byte()
    Private _scratch As Byte()
    Private ReadOnly _bufferLock As New Object()

    Public Sub New(deviceIndex As Integer, modeIndex As Integer)
        _deviceIndex = Math.Max(0, deviceIndex)
        _modeIndex = modeIndex
    End Sub

    Public ReadOnly Property DisplayName As String Implements ILiveVideoSource.DisplayName
        Get
            Return _displayName
        End Get
    End Property

    Public ReadOnly Property Width As Integer Implements ILiveVideoSource.Width
        Get
            Return _width
        End Get
    End Property

    Public ReadOnly Property Height As Integer Implements ILiveVideoSource.Height
        Get
            Return _height
        End Get
    End Property

    Public ReadOnly Property FramesPerSecond As Integer Implements ILiveVideoSource.FramesPerSecond
        Get
            Return _framesPerSecond
        End Get
    End Property

    Public Function Start(sink As Action(Of LiveVideoFrame), ByRef reason As String) As Boolean Implements ILiveVideoSource.Start
        If sink Is Nothing Then
            reason = "no sink"
            Return False
        End If

        ' The WinRT camera stack is initialized on an MTA threadpool thread, never on the caller's
        ' STA: MediaCapture's initialization is documented to want a thread without a UI
        ' SynchronizationContext, and blocking the Share Manager's UI thread on it would deadlock.
        Dim holder As New StartHolder()
        Dim task As Threading.Tasks.Task =
            Threading.Tasks.Task.Run(Function() StartCoreAsync(holder))
        If Not task.Wait(20000) Then
            _stop = True
            reason = "the camera did not start in time"
            ReleaseCapture()
            Return False
        End If
        If Not holder.Ok Then
            reason = holder.Reason
            ReleaseCapture()
            Return False
        End If

        _sink = sink
        _pacing = New Thread(AddressOf PaceFrames) With {.IsBackground = True, .Name = "LiveCameraCapture"}
        _pacing.Start()
        Return True
    End Function

    Public Sub [Stop]() Implements ILiveVideoSource.Stop
        _stop = True
        Try
            _pacing?.Join(1000)
        Catch
        End Try
        _pacing = Nothing
        ReleaseCapture()
    End Sub

    ' --- the start, on the threadpool ----------------------------------------------------------------

    Private Class StartHolder
        Public Property Ok As Boolean
        Public Property Reason As String = ""
    End Class

    Private Async Function StartCoreAsync(holder As StartHolder) As Threading.Tasks.Task
        Try
            Dim devices As List(Of LiveCameraDevice) = EnumerateCamerasCore()
            If devices.Count = 0 Then
                holder.Reason = "no camera"
                Return
            End If
            Dim index As Integer = Math.Min(_deviceIndex, devices.Count - 1)
            Dim device As LiveCameraDevice = devices(index)

            Dim capture As New MediaCapture()
            Dim settings As New MediaCaptureInitializationSettings With {
                .SourceGroup = Await MediaFrameSourceGroup.FromIdAsync(device.SourceGroupId),
                .MemoryPreference = MediaCaptureMemoryPreference.Cpu,
                .StreamingCaptureMode = StreamingCaptureMode.Video,
                .SharingMode = MediaCaptureSharingMode.SharedReadOnly
            }
            Await capture.InitializeAsync(settings)
            _capture = capture

            Dim source As MediaFrameSource = Nothing
            If Not capture.FrameSources.TryGetValue(device.ColorSourceId, source) OrElse source Is Nothing Then
                holder.Reason = "the camera offers no color frame source"
                Return
            End If

            ' The mode picker: the device's own list, the stored row, else the automatic best.
            Dim modes As List(Of LiveCameraMode) = ModesOfSource(source)
            Dim picked As LiveCameraMode = LiveCameraModes.PickAt(modes, _modeIndex)
            If picked Is Nothing Then picked = LiveCameraModes.PickBest(modes)
            If picked Is Nothing Then
                holder.Reason = "the camera offered no capture mode"
                Return
            End If

            Dim format As MediaFrameFormat = FormatOfRow(source, picked)
            If format IsNot Nothing Then
                Await source.SetFormatAsync(format)
            End If

            _framesPerSecond = Math.Max(1, Math.Min(30, picked.FramesPerSecond))
            _width = picked.Width
            _height = picked.Height
            _fixedBuffer = New Byte(_width * _height * 4 - 1) {}
            _sendBuffer = New Byte(_width * _height * 4 - 1) {}
            _displayName = "camera " & device.Name &
                           " (" & _width.ToString(Globalization.CultureInfo.InvariantCulture) & "x" &
                           _height.ToString(Globalization.CultureInfo.InvariantCulture) & ")"

            Dim reader As MediaFrameReader = Await capture.CreateFrameReaderAsync(source)
            If reader Is Nothing Then
                holder.Reason = "the frame reader could not be created"
                Return
            End If
            _reader = reader
            AddHandler _reader.FrameArrived, AddressOf OnFrameArrived
            Dim status As MediaFrameReaderStartStatus = Await _reader.StartAsync()
            If status <> MediaFrameReaderStartStatus.Success Then
                holder.Reason = "the frame reader did not start (" & status.ToString() & ")"
                Return
            End If

            holder.Ok = True
        Catch ex As Exception
            holder.Reason = "the camera could not be opened (0x" & ex.HResult.ToString("X8", Globalization.CultureInfo.InvariantCulture) & ")"
            ReleaseCapture()
        End Try
    End Function

    ' --- the frame path ------------------------------------------------------------------------------

    Private Sub OnFrameArrived(sender As MediaFrameReader, args As MediaFrameArrivedEventArgs)
        If _stop Then Return
        Dim reference As MediaFrameReference = Nothing
        Try
            reference = sender.TryAcquireLatestFrame()
        Catch
            Return
        End Try
        If reference Is Nothing Then Return
        Try
            Dim frame As VideoMediaFrame = reference.VideoMediaFrame
            If frame Is Nothing OrElse frame.SoftwareBitmap Is Nothing Then Return
            Dim bitmap As SoftwareBitmap = frame.SoftwareBitmap
            Dim converted As SoftwareBitmap = Nothing
            If bitmap.BitmapPixelFormat <> BitmapPixelFormat.Bgra8 Then
                converted = SoftwareBitmap.Convert(bitmap, BitmapPixelFormat.Bgra8)
                If converted Is Nothing Then Return
                bitmap = converted
            End If
            FoldBitmap(bitmap)
            converted?.Dispose()
        Catch
            ' A dropped frame is a dropped frame; the pacer keeps sending the previous one.
        Finally
            Try
                reference.Dispose()
            Catch
            End Try
        End Try
    End Sub

    ''' <summary>Locks the bitmap and folds it into the fixed buffer - the same byte path the screen
    ''' source walks (LockBuffer, IMemoryBufferByteAccess off the raw pointer, stride-aware copy).</summary>
    Private Sub FoldBitmap(bitmap As SoftwareBitmap)
        Dim width As Integer = bitmap.PixelWidth
        Dim height As Integer = bitmap.PixelHeight
        If width <= 0 OrElse height <= 0 Then Return
        Try
            Using buffer = bitmap.LockBuffer(BitmapBufferAccessMode.Read)
                If buffer Is Nothing Then Return
                Using reference = buffer.CreateReference()
                    If reference Is Nothing Then Return
                    Dim plane = buffer.GetPlaneDescription(0)

                    Dim thisPtr As IntPtr = IntPtr.Zero
                    Dim winrtObject = TryCast(reference, WinRT.IWinRTObject)
                    If winrtObject Is Nothing OrElse winrtObject.NativeObject Is Nothing Then Return
                    thisPtr = winrtObject.NativeObject.ThisPtr

                    Dim accessPtr As IntPtr = IntPtr.Zero
                    Dim iidByteAccess As New Guid(&H5B0D3235UI, &H4DBAUI, &H4D44UI, &H86, &H5E, &H8F, &H1D, &HE, &H4F, &HD0, &H4D)
                    If Global.System.Runtime.InteropServices.Marshal.QueryInterface(thisPtr, iidByteAccess, accessPtr) <> 0 Then Return
                    Try
                        Dim byteAccess = DirectCast(Global.System.Runtime.InteropServices.Marshal.GetObjectForIUnknown(accessPtr), IMemoryBufferByteAccess)
                        Dim capacity As UInteger = 0UI
                        Dim data As IntPtr = IntPtr.Zero
                        byteAccess.GetBuffer(data, capacity)
                        If data = IntPtr.Zero Then Return

                        Dim needed As Integer = width * height * 4
                        If _scratch Is Nothing OrElse _scratch.Length < needed Then _scratch = New Byte(needed - 1) {}
                        If plane.Stride = width * 4 AndAlso plane.StartIndex = 0 Then
                            Global.System.Runtime.InteropServices.Marshal.Copy(data, _scratch, 0, needed)
                        Else
                            For y As Integer = 0 To height - 1
                                Global.System.Runtime.InteropServices.Marshal.Copy(New IntPtr(data.ToInt64() + plane.StartIndex + CLng(y) * plane.Stride),
                                                             _scratch, y * width * 4, width * 4)
                            Next
                        End If

                        SyncLock _bufferLock
                            If _fixedBuffer IsNot Nothing Then
                                LiveVideoCopy.FoldFrame(_scratch, width, height, _fixedBuffer, _width, _height)
                            End If
                        End SyncLock
                    Finally
                        Global.System.Runtime.InteropServices.Marshal.Release(accessPtr)
                    End Try
                End Using
            End Using
        Catch
        End Try
    End Sub

    ''' <summary>Pushes the fixed buffer at the frame rate the encoder was told, whether or not the
    ''' camera delivered a new frame (a camera that stops delivering keeps a frozen picture).</summary>
    Private Sub PaceFrames()
        Dim frameMs As Long = Math.Max(1, 1000L \ _framesPerSecond)
        Dim nextTick As Long = MonotonicClock.NowMs()
        While Not _stop
            Dim buffer As Byte() = Nothing
            SyncLock _bufferLock
                If _fixedBuffer IsNot Nothing Then
                    If _sendBuffer Is Nothing OrElse _sendBuffer.Length <> _fixedBuffer.Length Then
                        _sendBuffer = New Byte(_fixedBuffer.Length - 1) {}
                    End If
                    Array.Copy(_fixedBuffer, _sendBuffer, _fixedBuffer.Length)
                    buffer = _sendBuffer
                End If
            End SyncLock
            If buffer IsNot Nothing Then
                Try
                    _sink?.Invoke(New LiveVideoFrame With {.Pixels = buffer, .Width = _width, .Height = _height})
                Catch
                End Try
            End If

            nextTick += frameMs
            Dim now As Long = MonotonicClock.NowMs()
            Dim sleep As Integer = CInt(nextTick - now)
            If sleep < 0 Then
                nextTick = now
                sleep = CInt(frameMs)
            End If
            Thread.Sleep(sleep)
        End While
    End Sub

    ' --- the device and the mode pickers ----------------------------------------------------------------

    ''' <summary>Every camera the frame-source groups expose, deduplicated by the device id Windows
    ''' reports, in the system's own order (the picker's rows and the stored index refer to it).
    ''' Runs on a threadpool thread (the WinRT enumeration is not initialized on an STA either);
    ''' reads device METADATA only, no capture session.</summary>
    Friend Shared Function EnumerateCameras() As List(Of LiveCameraDevice)
        Try
            Return Threading.Tasks.Task.Run(Function() EnumerateCamerasCore()).GetAwaiter().GetResult()
        Catch
            Return New List(Of LiveCameraDevice)()
        End Try
    End Function

    Private Shared Function EnumerateCamerasCore() As List(Of LiveCameraDevice)
        Dim devices As New List(Of LiveCameraDevice)()
        Dim seen As New HashSet(Of String)(StringComparer.OrdinalIgnoreCase)
        For Each group As MediaFrameSourceGroup In MediaFrameSourceGroup.FindAllAsync().GetAwaiter().GetResult()
            For Each info As MediaFrameSourceInfo In group.SourceInfos
                If info Is Nothing OrElse info.SourceKind <> MediaFrameSourceKind.Color Then Continue For
                Dim deviceInfo As Windows.Devices.Enumeration.DeviceInformation = info.DeviceInformation
                If deviceInfo Is Nothing OrElse String.IsNullOrEmpty(deviceInfo.Id) Then Continue For
                If Not seen.Add(deviceInfo.Id) Then Continue For
                devices.Add(New LiveCameraDevice With {
                                 .Index = devices.Count,
                                 .Name = If(deviceInfo.Name, group.DisplayName),
                                 .SourceGroupId = group.Id,
                                 .ColorSourceId = info.Id
                             })
                Exit For
            Next
        Next
        Return devices
    End Function

    ''' <summary>The capture modes of one device, for the window's mode picker: a temporary capture
    ''' is initialized, the formats are read, and the camera is released again - a user act (the
    ''' picker row), behind the gate, and the broadcast start does it all over anyway.</summary>
    Friend Shared Function EnumerateModes(deviceIndex As Integer) As List(Of LiveCameraMode)
        Try
            Return Threading.Tasks.Task.Run(Function() EnumerateModesCore(deviceIndex)).GetAwaiter().GetResult()
        Catch
            Return New List(Of LiveCameraMode)()
        End Try
    End Function

    Private Shared Async Function EnumerateModesCore(deviceIndex As Integer) As Threading.Tasks.Task(Of List(Of LiveCameraMode))
        Dim devices As List(Of LiveCameraDevice) = EnumerateCamerasCore()
        If devices.Count = 0 Then Return New List(Of LiveCameraMode)()
        Dim index As Integer = Math.Min(Math.Max(0, deviceIndex), devices.Count - 1)
        Dim device As LiveCameraDevice = devices(index)

        Dim capture As New MediaCapture()
        Dim settings As New MediaCaptureInitializationSettings With {
            .SourceGroup = Await MediaFrameSourceGroup.FromIdAsync(device.SourceGroupId),
            .MemoryPreference = MediaCaptureMemoryPreference.Cpu,
            .StreamingCaptureMode = StreamingCaptureMode.Video,
            .SharingMode = MediaCaptureSharingMode.SharedReadOnly
        }
        Await capture.InitializeAsync(settings)
        Try
            Dim source As MediaFrameSource = Nothing
            If Not capture.FrameSources.TryGetValue(device.ColorSourceId, source) OrElse source Is Nothing Then
                Return New List(Of LiveCameraMode)()
            End If
            Return ModesOfSource(source)
        Finally
            capture.Dispose()
        End Try
    End Function

    ''' <summary>The device's capture modes as picker rows, deduplicated (a camera often lists the
    ''' same pixels and rate twice, once per subtype).</summary>
    Private Shared Function ModesOfSource(source As MediaFrameSource) As List(Of LiveCameraMode)
        Dim rows As New List(Of LiveCameraMode)()
        Dim seen As New HashSet(Of String)(StringComparer.OrdinalIgnoreCase)
        If source.SupportedFormats Is Nothing Then Return rows
        For Each format As MediaFrameFormat In source.SupportedFormats
            If format Is Nothing OrElse format.VideoFormat Is Nothing OrElse format.FrameRate Is Nothing Then Continue For
            Dim video As VideoMediaFrameFormat = format.VideoFormat
            Dim rate As Double = 0.0
            If format.FrameRate.Denominator > 0 Then
                rate = format.FrameRate.Numerator / CDbl(format.FrameRate.Denominator)
            End If
            Dim fps As Integer = CInt(Math.Round(rate))
            Dim width As Integer = CInt(video.Width)
            Dim height As Integer = CInt(video.Height)
            If width <= 0 OrElse height <= 0 OrElse fps <= 0 Then Continue For
            Dim key As String = width.ToString(Globalization.CultureInfo.InvariantCulture) & "x" &
                                height.ToString(Globalization.CultureInfo.InvariantCulture) & "@" &
                                fps.ToString(Globalization.CultureInfo.InvariantCulture)
            If Not seen.Add(key) Then Continue For
            rows.Add(New LiveCameraMode With {
                         .Index = rows.Count,
                         .Width = width,
                         .Height = height,
                         .FramesPerSecond = fps
                     })
        Next
        Return rows
    End Function

    ''' <summary>The format object of a picked row - matched by pixels and rate, because the row
    ''' list is deduplicated and the format list is not (a camera lists the same numbers once per
    ''' subtype; any of them produces the same picture).</summary>
    Private Shared Function FormatOfRow(source As MediaFrameSource, picked As LiveCameraMode) As MediaFrameFormat
        If source.SupportedFormats Is Nothing Then Return Nothing
        For Each format As MediaFrameFormat In source.SupportedFormats
            If format Is Nothing OrElse format.VideoFormat Is Nothing OrElse format.FrameRate Is Nothing Then Continue For
            Dim rate As Double = 0.0
            If format.FrameRate.Denominator > 0 Then
                rate = format.FrameRate.Numerator / CDbl(format.FrameRate.Denominator)
            End If
            Dim fps As Integer = CInt(Math.Round(rate))
            If CInt(format.VideoFormat.Width) = picked.Width AndAlso
               CInt(format.VideoFormat.Height) = picked.Height AndAlso fps = picked.FramesPerSecond Then
                Return format
            End If
        Next
        Return Nothing
    End Function

    Private Sub ReleaseCapture()
        If _reader IsNot Nothing Then
            Try
                RemoveHandler _reader.FrameArrived, AddressOf OnFrameArrived
            Catch
            End Try
            Try
                _reader.StopAsync().GetAwaiter().GetResult()
            Catch
            End Try
            Try
                _reader.Dispose()
            Catch
            End Try
        End If
        Try
            _capture?.Dispose()
        Catch
        End Try
        SyncLock _bufferLock
            _fixedBuffer = Nothing
            _sendBuffer = Nothing
            _scratch = Nothing
        End SyncLock
        _reader = Nothing
        _capture = Nothing
    End Sub

End Class

''' <summary>One row of the device picker: the name Windows itself reports. The source-group and
''' source ids are Friend - the window never touches them.</summary>
Friend Class LiveCameraDevice
    Public Property Index As Integer
    Public Property Name As String = ""
    Friend Property SourceGroupId As String = ""
    Friend Property ColorSourceId As String = ""
End Class
