Option Strict On

Imports System.Runtime.InteropServices
Imports System.Threading
Imports Windows.Foundation
Imports Windows.Foundation.Metadata
Imports Windows.Graphics
Imports Windows.Graphics.Capture
Imports Windows.Graphics.DirectX
Imports Windows.Graphics.DirectX.Direct3D11
Imports Windows.Graphics.Imaging

' Ticket 199 section 4, the screen source: Windows Graphics Capture (Windows 10 1903 and later), a
' monitor picker, 15-30 fps (this build paces at 25), the shape's "at most 1280x720" left to the
' engine's scale filter (LiveFfmpegArgs), and a cursor toggle. Behind ILiveVideoSource so the suite
' drives fakes - no test needs a device (section 8).
'
' The projection route: the project targets windows10.0.19041 (TargetPlatformMinVersion stays
' 10.0.17763), so the Windows SDK projections give Direct3D11CaptureFramePool, GraphicsCaptureSession
' and SoftwareBitmap. The two pieces that are NOT projected are done by hand: the item is created from
' an HMONITOR through IGraphicsCaptureItemInterop (the activation factory is fetched with
' RoGetActivationFactory and queried for the interop interface), and the D3D11 device the pool needs
' is D3D11CreateDevice + IDXGIDevice + CreateDirect3D11DeviceFromDXGIDevice, wrapped with
' WinRT.MarshalInterface.
'
' Frame path: FrameArrived (free-threaded pool) -> SoftwareBitmap.CreateCopyFromSurfaceAsync (the
' GPU->CPU copy the runtime owns) -> LiveVideoCopy.FoldFrame into the ONE fixed buffer announced to
' the encoder; a pacing thread pushes that buffer at the fixed frame rate whether or not the screen
' changed, because a static screen delivers no WGC frames and the 1 s keyframe cadence (3.2) must
' not starve. If the capture dies (item closed), the last frame keeps pacing - a frozen picture
' instead of a dead stream.

''' <summary>Windows Graphics Capture of one monitor, one pacing thread, BGRA frames.</summary>
Friend NotInheritable Class ScreenCaptureSource
    Implements ILiveVideoSource

    Private ReadOnly _monitorIndex As Integer
    Private ReadOnly _includeCursor As Boolean
    Private _displayName As String = "screen"
    Private _width As Integer
    Private _height As Integer
    Private _sink As Action(Of LiveVideoFrame)
    Private _started As Boolean
    Private _stop As Boolean
    Private _pacing As Thread
    Private _copyBusy As Integer

    Private _device As IDirect3DDevice
    Private _item As GraphicsCaptureItem
    Private _pool As Direct3D11CaptureFramePool
    Private _session As GraphicsCaptureSession
    Private _devicePtr As IntPtr
    Private _d3dDevice As IntPtr

    ''' <summary>The one fixed buffer the encoder was told about at start (the rawvideo -s).</summary>
    Private _fixedBuffer As Byte()
    Private _sendBuffer As Byte()
    Private _scratch As Byte()
    Private ReadOnly _bufferLock As New Object()

    Public Sub New(monitorIndex As Integer, includeCursor As Boolean)
        _monitorIndex = Math.Max(0, monitorIndex)
        _includeCursor = includeCursor
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
            Return 25
        End Get
    End Property

    Public Function Start(sink As Action(Of LiveVideoFrame), ByRef reason As String) As Boolean Implements ILiveVideoSource.Start
        If sink Is Nothing Then
            reason = "no sink"
            Return False
        End If
        If Not ApiInformation.IsTypePresent("Windows.Graphics.Capture.GraphicsCaptureItem") Then
            reason = "Windows Graphics Capture is not available on this Windows"
            Return False
        End If

        Dim monitors As List(Of LiveScreenMonitor) = EnumerateMonitors()
        If monitors.Count = 0 Then
            reason = "no monitor"
            Return False
        End If
        Dim index As Integer = Math.Min(_monitorIndex, monitors.Count - 1)
        Dim monitor As LiveScreenMonitor = monitors(index)
        _displayName = "screen " & (index + 1).ToString(Globalization.CultureInfo.InvariantCulture) &
                       " (" & monitor.Width.ToString(Globalization.CultureInfo.InvariantCulture) & "x" &
                       monitor.Height.ToString(Globalization.CultureInfo.InvariantCulture) & ")"

        Try
            CreateDevice(reason)
        Catch
            ReleaseDevice()
            reason = "the capture device could not be created (" & reason & ")"
            Return False
        End Try

        Dim item As GraphicsCaptureItem = Nothing
        Dim itemStep As String = ""
        Try
            item = CreateItemForMonitor(monitor.HMonitor, itemStep)
        Catch ex As Exception
            itemStep &= " ex=" & ex.GetType().Name
            item = Nothing
        End Try
        If item Is Nothing Then
            ReleaseDevice()
            reason = "the monitor could not be opened for capture (" & itemStep & ")"
            Return False
        End If
        _item = item

        _width = Math.Max(1, item.Size.Width)
        _height = Math.Max(1, item.Size.Height)
        _fixedBuffer = New Byte(_width * _height * 4 - 1) {}
        _sendBuffer = New Byte(_width * _height * 4 - 1) {}
        _displayName = "screen " & (index + 1).ToString(Globalization.CultureInfo.InvariantCulture) &
                       " (" & _width.ToString(Globalization.CultureInfo.InvariantCulture) & "x" &
                       _height.ToString(Globalization.CultureInfo.InvariantCulture) & ")"

        Dim size As New SizeInt32 With {.Width = _width, .Height = _height}
        _pool = Direct3D11CaptureFramePool.CreateFreeThreaded(_device, DirectXPixelFormat.B8G8R8A8UIntNormalized, 2, size)
        _session = _pool.CreateCaptureSession(_item)
        If Not _includeCursor AndAlso
               ApiInformation.IsPropertyPresent("Windows.Graphics.Capture.GraphicsCaptureSession", "IsCursorCaptureEnabled") Then
            ' Windows 10 2004 and later; on 1903/1909 the toggle does not exist and the cursor is
            ' captured because the OS says so - the setting is honored where Windows offers it.
#Disable Warning CA1416 ' guarded by ApiInformation.IsPropertyPresent one line above
            _session.IsCursorCaptureEnabled = False
#Enable Warning CA1416
        End If
        AddHandler _pool.FrameArrived, AddressOf OnFrameArrived
        _sink = sink
        Try
            _session.StartCapture()
        Catch
            ReleaseCapture()
            reason = "the capture session did not start"
            Return False
        End Try

        _started = True
        _pacing = New Thread(AddressOf PaceFrames) With {.IsBackground = True, .Name = "LiveScreenCapture"}
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
        _started = False
    End Sub

    ' --- the frame path ----------------------------------------------------------------------------

    Private Sub OnFrameArrived(sender As Direct3D11CaptureFramePool, args As Object)
        If _stop Then Return
        Dim frame As Direct3D11CaptureFrame = Nothing
        Try
            frame = sender.TryGetNextFrame()
        Catch
            Return
        End Try
        If frame Is Nothing Then Return
        If Interlocked.CompareExchange(_copyBusy, 1, 0) <> 0 Then
            frame.Dispose()
            Return
        End If
        CopyFrameAsync(frame)
    End Sub

    ''' <summary>The GPU->CPU copy runs on the runtime's own async path; the frame is held until it
    ''' completes because the pool reuses its surfaces.</summary>
    Private Async Sub CopyFrameAsync(frame As Direct3D11CaptureFrame)
        Try
            Using held = frame
                Dim bitmap As SoftwareBitmap = Await SoftwareBitmap.CreateCopyFromSurfaceAsync(held.Surface)
                If bitmap Is Nothing Then Return
                Using b = bitmap
                    FoldBitmap(b)
                End Using
            End Using
        Catch
            ' A dropped frame is a dropped frame; the pacer keeps sending the previous one.
        Finally
            Interlocked.Exchange(_copyBusy, 0)
        End Try
    End Sub

    Private Sub FoldBitmap(bitmap As SoftwareBitmap)
        If _stop Then Return
        Dim width As Integer = bitmap.PixelWidth
        Dim height As Integer = bitmap.PixelHeight
        If width <= 0 OrElse height <= 0 Then Return
        Try
            Using buffer = bitmap.LockBuffer(BitmapBufferAccessMode.Read)
                If buffer Is Nothing Then Return
                Using reference = buffer.CreateReference()
                    If reference Is Nothing Then Return
                    Dim plane = buffer.GetPlaneDescription(0)

                    ' The byte access is the classic ComImport off the reference's raw pointer: the
                    ' runtime's own IID (5B0D3235-..), queried, wrapped, released.
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
    ''' screen delivered a new frame (a static screen delivers none, and the keyframe cadence of
    ''' section 3.2 must not starve).</summary>
    Private Sub PaceFrames()
        Dim frameMs As Long = 40
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
                ' Fell behind (a slow encoder pipe, a busy machine): resynchronize instead of
                ' bursting, and keep sending at the fixed rate from NOW.
                nextTick = now
                sleep = CInt(frameMs)
            End If
            Thread.Sleep(sleep)
        End While
    End Sub

    ' --- the device and the item -------------------------------------------------------------------

    Private Sub CreateDevice(ByRef reason As String)
        ' D3D11_CREATE_DEVICE_BGRA_SUPPORT (0x20); hardware first, WARP where there is no GPU.
        Dim device As IntPtr = IntPtr.Zero
        Dim featureLevel As Integer = 0
        Dim context As IntPtr = IntPtr.Zero
        Dim hr As Integer = D3D11CreateDevice(IntPtr.Zero, 1, IntPtr.Zero, &H20, IntPtr.Zero, 0, 7,
                                              device, featureLevel, context)
        If hr <> 0 Then
            ' WARP (software rasterizer) where there is no GPU to hand.
            hr = D3D11CreateDevice(IntPtr.Zero, 5, IntPtr.Zero, &H20, IntPtr.Zero, 0, 7,
                                   device, featureLevel, context)
        End If
        If hr <> 0 Then
            reason = "d3d11 " & hr.ToString("X8", Globalization.CultureInfo.InvariantCulture)
            Throw New ComponentModel.Win32Exception(hr)
        End If
        If context <> IntPtr.Zero Then Marshal.Release(context)
        _d3dDevice = device

        Dim dxgiDevice As IntPtr = IntPtr.Zero
        Dim iidDxgi As New Guid(&H54EC77FAUI, &H1377S, &H44E6S, &H8C, &H32, &H88, &HFD, &H5F, &H44, &HC8, &H4C)
        hr = Marshal.QueryInterface(device, iidDxgi, dxgiDevice)
        If hr <> 0 Then
            reason = "dxgi qi " & hr.ToString("X8", Globalization.CultureInfo.InvariantCulture)
            Throw New ComponentModel.Win32Exception(hr)
        End If

        Dim inspectable As IntPtr = IntPtr.Zero
        hr = CreateDirect3D11DeviceFromDXGIDevice(dxgiDevice, inspectable)
        Marshal.Release(dxgiDevice)
        If hr <> 0 Then
            reason = "wrap " & hr.ToString("X8", Globalization.CultureInfo.InvariantCulture)
            Throw New ComponentModel.Win32Exception(hr)
        End If

        ' The inspectable becomes the projected IDirect3DDevice the honest way: QI for the
        ' interface's own IID (taken from the projection's GuidAttribute, not from memory), then
        ' MarshalInterface.FromAbi - a TryCast of the raw RCW produces a wrapper that fails the
        ' moment the runtime marshals it back into WinRT (CreateFreeThreaded).
        Dim d3dPtr As IntPtr = IntPtr.Zero
        _devicePtr = inspectable
        Marshal.ThrowExceptionForHR(Marshal.QueryInterface(inspectable, GetType(IDirect3DDevice).GUID, d3dPtr))
        Try
            _device = WinRT.MarshalInterface(Of IDirect3DDevice).FromAbi(d3dPtr)
        Finally
            Marshal.Release(d3dPtr)
        End Try
    End Sub

    Private Function CreateItemForMonitor(hMonitor As IntPtr, ByRef detail As String) As GraphicsCaptureItem
        ' The class id is handed to RoGetActivationFactory as a raw HSTRING built by hand - the
        ' HString parameter marshaling this runtime refuses is not used at all.
        Dim classId As IntPtr = IntPtr.Zero
        Marshal.ThrowExceptionForHR(WindowsCreateString("Windows.Graphics.Capture.GraphicsCaptureItem",
                                                         "Windows.Graphics.Capture.GraphicsCaptureItem".Length, classId))
        Dim factory As IntPtr = IntPtr.Zero
        Dim iidInspectable As New Guid(&HAF86E2E0UI, &HB12DUI, &H4C6AUI, &H9C, &H5A, &HD7, &HAA, &H65, &H10, &H1E, &H90)
        Try
            Marshal.ThrowExceptionForHR(RoGetActivationFactory(classId, iidInspectable, factory))
        Finally
            WindowsDeleteString(classId)
        End Try

        Try
            ' IGraphicsCaptureItemInterop (3628E81B-..), the exact IID of
            ' Windows.Graphics.Capture.Interop.h - checked against the SDK header, because the
            ' almost-right GUIDs samples quote fail the QI with E_NOINTERFACE.
            Dim interopPtr As IntPtr = IntPtr.Zero
            Marshal.ThrowExceptionForHR(Marshal.QueryInterface(factory, New Guid(&H3628E81BUI, &H3CACUI, &H4C60UI, &HB7, &HF4, &H23, &HCE, &HE, &HC, &H33, &H56), interopPtr))
            Try
                ' The interop call goes through the vtable by hand: the RCW this runtime builds for
                ' IInspectable-derived pointers refuses casts to ComImport interfaces, so no wrapper
                ' is built at all. Slot 4 of the vtable (after QueryInterface/AddRef/Release and
                ' CreateForWindow) is CreateForMonitor.
                Dim vtable As IntPtr = Marshal.ReadIntPtr(interopPtr)
                Dim createForMonitorPtr As IntPtr = Marshal.ReadIntPtr(vtable, 4 * IntPtr.Size)
                Dim createForMonitor = Marshal.GetDelegateForFunctionPointer(Of CreateForMonitorDelegate)(createForMonitorPtr)
                Dim iidItem As New Guid(&H79C3F95BUI, &H31F7UI, &H4EC2UI, &HA4, &H64, &H63, &H2E, &HF5, &HD3, &H7, &H60)
                Dim itemPtr As IntPtr = IntPtr.Zero
                Dim hrCreate As Integer = createForMonitor(interopPtr, hMonitor, iidItem, itemPtr)
                If hrCreate < 0 Then
                    detail = "create 0x" & hrCreate.ToString("X8", Globalization.CultureInfo.InvariantCulture)
                    Throw New ComponentModel.Win32Exception(hrCreate)
                End If
                Try
                    Return GraphicsCaptureItem.FromAbi(itemPtr)
                Finally
                    Marshal.Release(itemPtr)
                End Try
            Finally
                Marshal.Release(interopPtr)
            End Try
        Finally
            Marshal.Release(factory)
        End Try
    End Function

    Private Sub ReleaseCapture()
        If _pool IsNot Nothing Then
            Try
                RemoveHandler _pool.FrameArrived, AddressOf OnFrameArrived
            Catch
            End Try
        End If
        Try
            _session?.Dispose()
        Catch
        End Try
        Try
            _pool?.Dispose()
        Catch
        End Try
        SyncLock _bufferLock
            _fixedBuffer = Nothing
            _sendBuffer = Nothing
            _scratch = Nothing
        End SyncLock
        _session = Nothing
        _pool = Nothing
        _item = Nothing
        ReleaseDevice()
    End Sub

    Private Sub ReleaseDevice()
        Try
            _device = Nothing
        Catch
        End Try
        If _devicePtr <> IntPtr.Zero Then
            Try
                Marshal.Release(_devicePtr)
            Catch
            End Try
            _devicePtr = IntPtr.Zero
        End If
        If _d3dDevice <> IntPtr.Zero Then
            Try
                Marshal.Release(_d3dDevice)
            Catch
            End Try
            _d3dDevice = IntPtr.Zero
        End If
    End Sub

    ' --- the monitor picker's table ------------------------------------------------------------------

    ''' <summary>Every monitor in EnumDisplayMonitors order, the same order the picker shows and the
    ''' stored index refers to.</summary>
    Friend Shared Function EnumerateMonitors() As List(Of LiveScreenMonitor)
        Dim state As New MonitorEnumeration()
        Dim callback As New EnumMonitorsCallback(AddressOf state.OneMonitor)
        Try
            EnumDisplayMonitors(IntPtr.Zero, IntPtr.Zero, callback, IntPtr.Zero)
        Catch
        End Try
        GC.KeepAlive(callback)
        Return state.Monitors
    End Function

    ''' <summary>The collector the enumeration callback fills - a plain method, not a closure, so the
    ''' delegate's lifetime is obvious.</summary>
    Private NotInheritable Class MonitorEnumeration
        Public ReadOnly Monitors As New List(Of LiveScreenMonitor)()

        Public Function OneMonitor(hMonitor As IntPtr, hdc As IntPtr, ByRef rect As RECT, lParam As IntPtr) As <MarshalAs(UnmanagedType.Bool)> Boolean
            Dim info As New MONITORINFOEX()
            info.cbSize = Marshal.SizeOf(GetType(MONITORINFOEX))
            If GetMonitorInfo(hMonitor, info) Then
                Monitors.Add(New LiveScreenMonitor With {
                                 .Index = Monitors.Count,
                                 .Width = info.rcMonitorRight - info.rcMonitorLeft,
                                 .Height = info.rcMonitorBottom - info.rcMonitorTop,
                                 .IsPrimary = (info.dwFlags And 1) <> 0,
                                 .DeviceName = info.szDevice,
                                 .HMonitor = hMonitor
                             })
            End If
            Return True
        End Function
    End Class

End Class

''' <summary>One row of the monitor picker: the index, the pixels, the primary flag, the device
''' name Windows itself reports. The HMONITOR is Friend - the window never touches it.</summary>
Friend Class LiveScreenMonitor
    Public Property Index As Integer
    Public Property Width As Integer
    Public Property Height As Integer
    Public Property IsPrimary As Boolean
    Public Property DeviceName As String = ""
    Friend Property HMonitor As IntPtr
End Class

' --- the plain Win32 and COM shapes, in vtable order -----------------------------------------------

' The byte access of an IMemoryBufferReference, the one interface the projections do not hand over
' as a cast target: declared ComImport with the runtime's own GUID and queried off the raw pointer.
<ComImport, Global.System.Runtime.InteropServices.Guid("5B0D3235-4DBA-4D44-865E-8F1D0E4FD04D"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)>
Friend Interface IMemoryBufferByteAccess
    Sub GetBuffer(ByRef buffer As IntPtr, ByRef capacity As UInteger)
End Interface

' The raw CreateForMonitor vtable entry, called by hand (see CreateItemForMonitor).
<UnmanagedFunctionPointer(CallingConvention.StdCall)>
Friend Delegate Function CreateForMonitorDelegate(thisPtr As IntPtr, monitor As IntPtr, ByRef iid As Guid, ByRef result As IntPtr) As Integer

Friend Delegate Function EnumMonitorsCallback(monitor As IntPtr, hdc As IntPtr, ByRef rect As RECT, lParam As IntPtr) As <MarshalAs(UnmanagedType.Bool)> Boolean

<StructLayout(LayoutKind.Sequential)>
Friend Structure RECT
    Public Left As Integer
    Public Top As Integer
    Public Right As Integer
    Public Bottom As Integer
End Structure

<StructLayout(LayoutKind.Sequential, CharSet:=CharSet.Unicode)>
Friend Structure MONITORINFOEX
    Public cbSize As Integer
    Public rcMonitorLeft As Integer
    Public rcMonitorTop As Integer
    Public rcMonitorRight As Integer
    Public rcMonitorBottom As Integer
    Public rcWorkLeft As Integer
    Public rcWorkTop As Integer
    Public rcWorkRight As Integer
    Public rcWorkBottom As Integer
    Public dwFlags As Integer
    <MarshalAs(UnmanagedType.ByValTStr, SizeConst:=32)>
    Public szDevice As String
End Structure

''' <summary>The flat Win32 calls the screen source owns. A module, because Shared lives in one.</summary>
Friend Module ScreenCaptureNative

    <DllImport("user32.dll", SetLastError:=True)>
    Friend Function EnumDisplayMonitors(hdc As IntPtr, clip As IntPtr, proc As EnumMonitorsCallback, lParam As IntPtr) As <MarshalAs(UnmanagedType.Bool)> Boolean
    End Function

    <DllImport("user32.dll", SetLastError:=True, CharSet:=CharSet.Unicode, EntryPoint:="GetMonitorInfoW")>
    Friend Function GetMonitorInfo(monitor As IntPtr, ByRef info As MONITORINFOEX) As <MarshalAs(UnmanagedType.Bool)> Boolean
    End Function

    <DllImport("d3d11.dll")>
    Friend Function D3D11CreateDevice(adapter As IntPtr, driverType As Integer, software As IntPtr, flags As Integer,
                                      featureLevels As IntPtr, featureLevelCount As Integer, sdkVersion As Integer,
                                      ByRef device As IntPtr, ByRef outFeatureLevel As Integer, ByRef context As IntPtr) As Integer
    End Function

    <DllImport("d3d11.dll")>
    Friend Function CreateDirect3D11DeviceFromDXGIDevice(dxgiDevice As IntPtr, ByRef graphicsDevice As IntPtr) As Integer
    End Function

    <DllImport("combase.dll", ExactSpelling:=True)>
    Friend Function RoGetActivationFactory(activatableClassId As IntPtr,
                                           <[In]> ByRef iid As Guid, ByRef factory As IntPtr) As Integer
    End Function

    <DllImport("combase.dll", ExactSpelling:=True, CharSet:=CharSet.Unicode)>
    Friend Function WindowsCreateString(<MarshalAs(UnmanagedType.LPWStr)> source As String, length As Integer, ByRef hstring As IntPtr) As Integer
    End Function

    <DllImport("combase.dll", ExactSpelling:=True)>
    Friend Function WindowsDeleteString(hstring As IntPtr) As Integer
    End Function

End Module
