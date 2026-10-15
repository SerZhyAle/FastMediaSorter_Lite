Option Strict On

Imports System.Diagnostics
Imports System.Runtime.InteropServices
Imports System.Threading

' Ticket 199 section 4, the audio sources: WASAPI capture of the microphone and WASAPI loopback of the
' default render device (system audio). Windows decides device access (safety rule 4): the
' microphone follows the privacy switch, and when Windows says no, Start returns False with a reason -
' the product asks for nothing the OS does not let it have.
'
' Feedback loop: a PC whose own viewer plays a broadcast would hear itself through loopback. Windows
' build 20348 and later offer process-loopback exclusion, and the system-audio source uses it where
' the OS offers it: the capture is ACTIVATED as "everything the render device plays EXCEPT one
' process tree", the tree being this product's viewer when one is running (the process that would
' feed back) and otherwise the Share Manager's own tree. The API excludes exactly one tree, so a
' SECOND viewer playing at the same time is still fed back - the window's warning sentence says the
' capture takes everything this PC plays, and it stays true. Where Windows does not offer the
' exclusion (older builds, activation failure) the loopback is the whole render device and the window
' carries the warning sentence (section 4's "otherwise warns in words").
'
' The gain is applied here, by the producer (consumer rule 6: a quiet microphone is a producer defect
' and the consumer adds none), with the live level meter showing the post-gain level. Mute keeps the
' capture (the meter keeps working) and pushes silence.
'
' One capture thread owns the COM apartment; Stop() stops the client and joins it. The chunks pushed
' are 16-bit little-endian interleaved PCM at the device's own mix format - the encoder is told the
' real numbers and resamples to the shape's own.

''' <summary>WASAPI microphone or system-audio loopback capture, one device, one thread.</summary>
Friend NotInheritable Class WasapiAudioSource
    Implements ILiveAudioSource

    ''' <summary>True for system audio: the default RENDER device captured in loopback mode.</summary>
    Private ReadOnly _loopback As Boolean
    Private _displayName As String
    Private _sampleRate As Integer
    Private _channels As Integer
    Private _bitsPerSample As Integer
    Private _isFloat As Boolean
    Private _sink As Action(Of LivePcmChunk)
    Private _thread As Thread
    Private _stopEvent As IntPtr
    Private _dataEvent As IntPtr
    ''' <summary>True when the client was initialized with the event flag (the data event drives the
    ''' pump); False for the polled whole-device loopback.</summary>
    Private _eventMode As Boolean
    Private _client As IAudioClient
    Private _capture As IAudioCaptureClient
    Private _mixFormat As IntPtr
    Private _muted As Boolean
    Private _gain As Double = 1.0
    Private _exclusionActive As Boolean

    ''' <summary>True when the system-audio capture is the process-loopback EXCLUSION (the feedback
    ''' loop is cut), False when it fell back to the whole render device.</summary>
    Friend ReadOnly Property ExclusionActive As Boolean
        Get
            Return _exclusionActive
        End Get
    End Property

    ''' <summary>The diagnostic reason the exclusion attempt fell back (empty when it worked).</summary>
    Friend ReadOnly Property ExclusionFailure As String
        Get
            Return _exclusionFailure
        End Get
    End Property

    Private _exclusionFailure As String = ""

    ''' <summary>True when Windows offers process-loopback exclusion (build 20348 and later).</summary>
    Friend Shared ReadOnly Property SupportsProcessLoopbackExclusion As Boolean
        Get
            Dim info As New OSVERSIONINFOW()
            info.dwOSVersionInfoSize = Marshal.SizeOf(GetType(OSVERSIONINFOW))
            If RtlGetVersion(info) <> 0 Then Return False
            Return info.dwBuildNumber >= 20348
        End Get
    End Property

    Public Sub New(loopback As Boolean)
        _loopback = loopback
        _displayName = If(loopback, "system audio", "microphone")
    End Sub

    Public ReadOnly Property DisplayName As String Implements ILiveAudioSource.DisplayName
        Get
            Return _displayName
        End Get
    End Property

    Public ReadOnly Property SampleRate As Integer Implements ILiveAudioSource.SampleRate
        Get
            Return _sampleRate
        End Get
    End Property

    Public ReadOnly Property Channels As Integer Implements ILiveAudioSource.Channels
        Get
            Return _channels
        End Get
    End Property

    Public Property Muted As Boolean Implements ILiveAudioSource.Muted
        Get
            Return _muted
        End Get
        Set(value As Boolean)
            _muted = value
        End Set
    End Property

    Public Property Gain As Double Implements ILiveAudioSource.Gain
        Get
            Return _gain
        End Get
        Set(value As Double)
            _gain = Math.Max(0.0, value)
        End Set
    End Property

    Public Function Start(sink As Action(Of LivePcmChunk), ByRef reason As String) As Boolean Implements ILiveAudioSource.Start
        If sink Is Nothing Then
            reason = "no sink"
            Return False
        End If
        _sink = sink
        _thread = New Thread(AddressOf CaptureLoop) With {.IsBackground = True, .Name = If(_loopback, "LiveSystemAudio", "LiveMicrophone")}
        _thread.SetApartmentState(ApartmentState.STA)
        _thread.Start()
        If Not _started.WaitOne(5000) Then
            reason = "audio capture did not start in time"
            Return False
        End If
        If _startFailed.Length > 0 Then
            reason = _startFailed
            Return False
        End If
        Return True
    End Function

    Private ReadOnly _started As New ManualResetEvent(False)
    Private _startFailed As String = ""

    Public Sub [Stop]() Implements ILiveAudioSource.Stop
        Try
            If _stopEvent <> IntPtr.Zero Then
                SetEvent(_stopEvent)
            End If
        Catch
        End Try
        Try
            _thread?.Join(2000)
        Catch
        End Try
        Try
            If _client IsNot Nothing Then
                _client.[Stop]()
            End If
        Catch
        End Try
        Try
            If _mixFormat <> IntPtr.Zero Then
                CoTaskMemFree(_mixFormat)
                _mixFormat = IntPtr.Zero
            End If
        Catch
        End Try
        If _stopEvent <> IntPtr.Zero Then
            CloseHandle(_stopEvent)
            _stopEvent = IntPtr.Zero
        End If
        If _dataEvent <> IntPtr.Zero Then
            CloseHandle(_dataEvent)
            _dataEvent = IntPtr.Zero
        End If
        _client = Nothing
        _capture = Nothing
    End Sub

    Private Sub CaptureLoop()
        Try
            CoInitializeEx(IntPtr.Zero, &H2)  ' COINIT_APARTMENTTHREADED on the capture thread

            ' The feedback-loop cut (section 4): where Windows offers it, the system-audio capture
            ' is activated as an EXCLUSION of this product's own process tree instead of the whole
            ' render device. Any failure here simply falls through to the whole-device path below.
            Dim hr As Integer
            Dim exclusionClient As IAudioClient = Nothing
            If _loopback AndAlso SupportsProcessLoopbackExclusion Then
                exclusionClient = TryActivateExclusionClient()
            End If
            If exclusionClient IsNot Nothing Then
                _client = exclusionClient
                _sampleRate = 48000
                _channels = 2
                _bitsPerSample = 16
                _isFloat = False
                _exclusionActive = True
                _displayName = "system audio (48000 Hz, this product's own sound excluded)"
            Else
                Dim enumObj As Object = Nothing
                hr = CoCreateInstance(GetClsidEnumerator(), IntPtr.Zero, &H17,
                                      GetIidEnumerator(), enumObj)
                If hr <> 0 Then
                    FailStart("device enumerator unavailable (0x" & hr.ToString("X8") & ")")
                    Return
                End If
                Dim enumerator As IMMDeviceEnumerator = DirectCast(enumObj, IMMDeviceEnumerator)

                ' EDataFlow: eRender = 0, eCapture = 1. The microphone captures from eCapture; the
                ' whole-device loopback opens the default RENDER endpoint with the loopback flag
                ' (the one flow combination Windows accepts it in). eConsole = 0.
                Dim device As IMMDevice = Nothing
                hr = enumerator.GetDefaultAudioEndpoint(If(_loopback, 0, 1), 0, device)
                If hr <> 0 OrElse device Is Nothing Then
                    FailStart("no default " & If(_loopback, "render", "capture") & " device (0x" & hr.ToString("X8") & ")")
                    Return
                End If
                Dim endpointId As String = ""
                Try
                    device.GetId(endpointId)
                Catch
                End Try

                Dim clientObj As Object = Nothing
                Dim iidAudioClient As New Guid("1CB9AD4C-DBFA-4C32-B178-C2F568A703B2")
                Dim iidDummy As Guid = iidAudioClient
                hr = device.Activate(iidDummy, &H17, IntPtr.Zero, clientObj)
                If hr <> 0 OrElse clientObj Is Nothing Then
                    ' 0x80070005 is the privacy switch: Windows said no, and that is the whole story.
                    FailStart(If(hr = &H80070005, "Windows denied device access (privacy switch)", "device activate failed (0x" & hr.ToString("X8") & ") [" & endpointId & "]"))
                    Return
                End If
                _client = DirectCast(clientObj, IAudioClient)

                hr = _client.GetMixFormat(_mixFormat)
                If hr <> 0 OrElse _mixFormat = IntPtr.Zero Then
                    FailStart("no mix format (0x" & hr.ToString("X8") & ")")
                    Return
                End If
                Dim format As WAVEFORMATEX = CType(Marshal.PtrToStructure(_mixFormat, GetType(WAVEFORMATEX)), WAVEFORMATEX)
                _sampleRate = format.nSamplesPerSec
                _channels = format.nChannels
                _bitsPerSample = format.wBitsPerSample
                _isFloat = IsFloatFormat(_mixFormat, format)
                If _sampleRate <= 0 OrElse _channels <= 0 OrElse (_bitsPerSample <> 16 AndAlso Not _isFloat) Then
                    FailStart("unsupported device format (rate=" & _sampleRate.ToString(Globalization.CultureInfo.InvariantCulture) &
                              " ch=" & _channels.ToString(Globalization.CultureInfo.InvariantCulture) &
                              " bits=" & _bitsPerSample.ToString(Globalization.CultureInfo.InvariantCulture) &
                              " tag=" & format.wFormatTag.ToString(Globalization.CultureInfo.InvariantCulture) &
                              " float=" & _isFloat.ToString() & ")")
                    Return
                End If
                _displayName = If(_loopback, "system audio", "microphone") & " (" & _sampleRate.ToString(Globalization.CultureInfo.InvariantCulture) & " Hz)"

                ' 100 ms of buffer: the contract's latency budget is tenths of a second, and a bigger
                ' buffer buys nothing a live broadcast can spend. The whole-device loopback is
                ' POLL mode - Windows refuses the event flag for a plain render-device loopback
                ' (AUDCLNT_E_INVALID_STREAM_FLAG), so the pump below polls it every 20 ms.
                Dim flags As Integer
                If _loopback Then
                    flags = StreamFlagLoopback
                    _eventMode = False
                Else
                    flags = StreamFlagEventCallback
                    _eventMode = True
                End If
                hr = _client.Initialize(0, flags, 100 * 10000, 0, _mixFormat, IntPtr.Zero)
                If hr <> 0 Then
                    FailStart(If(hr = &H80070005, "Windows denied device access (privacy switch)", "initialize failed (0x" & hr.ToString("X8") & ") [" & endpointId & " flags=0x" & flags.ToString("X8") & "]"))
                    Return
                End If
            End If

            _stopEvent = CreateEvent(IntPtr.Zero, True, False, Nothing)
            If _eventMode Then
                ' The data event is the stream's own auto-reset handle; the stop event is a separate
                ' manual-reset one, so "a buffer arrived" never reads as "stop".
                _dataEvent = CreateEvent(IntPtr.Zero, False, False, Nothing)
                hr = _client.SetEventHandle(_dataEvent)
                If hr <> 0 Then
                    FailStart("event handle refused (0x" & hr.ToString("X8") & ")")
                    Return
                End If
            End If

            Dim captureObj As Object = Nothing
            Dim iidCapture As New Guid("C8ADBD64-E71E-48A0-A4DE-185C395CD317")
            Dim iidCap As Guid = iidCapture
            hr = _client.GetService(iidCap, captureObj)
            If hr <> 0 OrElse captureObj Is Nothing Then
                FailStart("capture service unavailable (0x" & hr.ToString("X8") & ") loopback=" & _loopback & "]")
                Return
            End If
            _capture = DirectCast(captureObj, IAudioCaptureClient)

            hr = _client.Start()
            If hr <> 0 Then
                FailStart("start refused (0x" & hr.ToString("X8") & ")")
                Return
            End If

            _started.Set()
            PumpPackets()

        Catch
            ' One sentence per failure exists at the window; the exception's words never reach it
            ' (APP-BEHAVIOUR rule 6).
            FailStart("capture thread failed")
        Finally
            Try
                If _client IsNot Nothing Then
                    _client.[Stop]()
                End If
            Catch
            End Try
            _started.Set()
            Try
                CoUninitialize()
            Catch
            End Try
        End Try
    End Sub

    ''' <summary>Pumps capture packets until the stop event is set. Event mode waits on the stop and
    ''' data events together (a data signal or a timeout both drain); poll mode waits 20 ms on the
    ''' stop event and drains - the whole-device loopback has no event to wait on.</summary>
    Private Sub PumpPackets()
        While True
            If _eventMode Then
                Dim waitHandles(1) As IntPtr
                waitHandles(0) = _stopEvent
                waitHandles(1) = _dataEvent
                Dim signaled As Integer = WaitForMultipleObjects(2, waitHandles, 0, 200)
                If signaled = 0 Then Return                 ' the stop event (manual-reset)
            Else
                If WaitForSingleObject(_stopEvent, 20) = 0 Then Return
            End If

            Dim packetFrames As Integer = 0
            While _capture.GetNextPacketSize(packetFrames) = 0 AndAlso packetFrames > 0
                Dim data As IntPtr = IntPtr.Zero
                Dim frames As Integer = 0
                Dim flags As Integer = 0
                Dim devicePosition As Long = 0
                Dim qpcPosition As Long = 0
                Dim hr As Integer = _capture.GetBuffer(data, frames, flags, devicePosition, qpcPosition)
                If hr <> 0 Then Exit While

                Dim chunk As LivePcmChunk = ConvertToPcm(data, frames, flags)
                _capture.ReleaseBuffer()
                If chunk IsNot Nothing Then _sink(chunk)

                If WaitForSingleObject(_stopEvent, 0) = 0 Then Return
            End While
        End While
    End Sub

    ''' <summary>One packet to a 16-bit chunk: float or 16-bit source, the gain applied by the producer,
    ''' the peak of the result for the meter, mute pushing silence while the meter keeps the real level.</summary>
    Private Function ConvertToPcm(data As IntPtr, frames As Integer, flags As Integer) As LivePcmChunk
        If frames <= 0 Then Return Nothing
        Dim silentPacket As Boolean = (flags And &H2) <> 0        ' AUDCLNT_BUFFERFLAGS_SILENT
        Dim bytesPerSample As Integer = If(_isFloat, 4, 2)
        Dim sampleCount As Integer = frames * _channels
        Dim out(sampleCount * 2 - 1) As Byte

        Dim peak As Double = 0.0
        If Not silentPacket AndAlso data <> IntPtr.Zero Then
            For i As Integer = 0 To sampleCount - 1
                Dim value As Double
                If _isFloat Then
                    value = BitConverter.Int32BitsToSingle(Marshal.ReadInt32(data, i * bytesPerSample))
                Else
                    value = Marshal.ReadInt16(data, i * bytesPerSample) / Short.MaxValue
                End If
                Dim absValue As Double = Math.Abs(value)
                If absValue > peak Then peak = absValue
                If _muted Then value = 0.0
                Dim scaled As Integer = CInt(Math.Max(-1.0, Math.Min(1.0, value * _gain)) * Short.MaxValue)
                Dim clamped As Short = CShort(Math.Max(Short.MinValue, Math.Min(Short.MaxValue, scaled)))
                Dim outIndex As Integer = i * 2
                out(outIndex) = CByte(clamped And &HFF)
                out(outIndex + 1) = CByte((clamped >> 8) And &HFF)
            Next
        End If

        Return New LivePcmChunk With {.Data = out, .Frames = frames, .Peak = peak}
    End Function

    Private Sub FailStart(reason As String)
        _startFailed = reason
        _started.Set()
    End Sub

    Private Shared Function IsFloatFormat(ptr As IntPtr, format As WAVEFORMATEX) As Boolean
        If format.wFormatTag = 3 Then Return True                    ' WAVE_FORMAT_IEEE_FLOAT
        ' WAVE_FORMAT_EXTENSIBLE (0xFFFE) is -2 in a signed Short; the SubFormat GUID sits at
        ' offset 24 of WAVEFORMATEXTENSIBLE and only its first 4 bytes are needed.
        If format.wFormatTag <> -2S Then Return False
        Dim subFirst As Integer = Marshal.ReadInt32(ptr, 24)
        Return subFirst = 3
    End Function

    ' --- the process-loopback exclusion (section 4, Windows build 20348+) ------------------------

    ''' <summary>Activates the system-audio client as "everything the render device plays EXCEPT one
    ''' process tree" and initializes it. Returns Nothing on ANY failure - the caller falls back to
    ''' the whole-device loopback, never a half-started capture.</summary>
    Private Function TryActivateExclusionClient() As IAudioClient
        Dim paramsPtr As IntPtr = IntPtr.Zero
        Dim propPtr As IntPtr = IntPtr.Zero
        Dim operationPtr As IntPtr = IntPtr.Zero
        Dim handler As ExclusionActivationHandler = Nothing
        Try
            ' The one tree the API can exclude: a viewer of this product when one is running (that
            ' is the process that feeds back), else this program's own tree.
            Dim targetPid As Integer = FindExclusionTargetPid()

            ' AUDIOCLIENT_ACTIVATION_PARAMS { PROCESS_LOOPBACK(1), { pid, EXCLUDE(1) } } - 12 bytes,
            ' carried by a PROPVARIANT of VT_BLOB (offset 8: cbSize, offset 16: pBlobData on x64).
            paramsPtr = Marshal.AllocHGlobal(12)
            Marshal.WriteInt32(paramsPtr, 0, 1)
            Marshal.WriteInt32(paramsPtr, 4, targetPid)
            Marshal.WriteInt32(paramsPtr, 8, 1)
            propPtr = Marshal.AllocHGlobal(24)
            Marshal.WriteInt16(propPtr, 0, &H41)                 ' VT_BLOB
            Marshal.WriteInt32(propPtr, 8, 12)
            Marshal.WriteIntPtr(propPtr, 16, paramsPtr)

            handler = New ExclusionActivationHandler()
            Dim iidAudioClient As New Guid("1CB9AD4C-DBFA-4C32-B178-C2F568A703B2")
            Dim hr As Integer = ActivateAudioInterfaceAsync("VAD\Process_Loopback", iidAudioClient, propPtr,
                                                            handler, operationPtr)
            If hr <> 0 Then
                _exclusionFailure = "activate " & hr.ToString("X8", Globalization.CultureInfo.InvariantCulture)
                Return Nothing
            End If
            If Not handler.Done.WaitOne(3000) Then
                _exclusionFailure = "activate timeout"
                Return Nothing
            End If
            If handler.ActivateHr <> 0 OrElse handler.ClientPtr = IntPtr.Zero Then
                _exclusionFailure = "completion hr=" & handler.ActivateHr.ToString(Globalization.CultureInfo.InvariantCulture) &
                                    " ptrZero=" & (handler.ClientPtr = IntPtr.Zero).ToString() &
                                    " " & handler.Detail
                Return Nothing
            End If

            Dim client As IAudioClient = handler.Client
            If client Is Nothing Then Return Nothing

            ' The explicit format this activation path needs (there is no mix format to ask for):
            ' 48 000 Hz stereo 16-bit, the numbers the encoder is told as its input. Allocated from
            ' the COM task allocator because Stop() frees _mixFormat with CoTaskMemFree.
            _mixFormat = Marshal.AllocCoTaskMem(Marshal.SizeOf(GetType(WAVEFORMATEX)))
            Marshal.StructureToPtr(New WAVEFORMATEX With {
                                       .wFormatTag = 1,
                                       .nChannels = 2,
                                       .nSamplesPerSec = 48000,
                                       .nAvgBytesPerSec = 192000,
                                       .nBlockAlign = 4,
                                       .wBitsPerSample = 16,
                                       .cbSize = 0
                                   }, _mixFormat, False)

            ' The process activation rides the loopback flag and the event flag together (the OS
            ' sample does), with AUTOCONVERTPCM because the 16-bit explicit format is not the
            ' engine's own float; buffer 0 lets the engine pick its period.
            Dim initHr As Integer = client.Initialize(0,
                                                      StreamFlagLoopback Or StreamFlagEventCallback Or StreamFlagAutoConvertPcm,
                                                      0, 0, _mixFormat, IntPtr.Zero)
            If initHr <> 0 Then
                _exclusionFailure = "init " & initHr.ToString("X8", Globalization.CultureInfo.InvariantCulture)
                CoTaskMemFree(_mixFormat)
                _mixFormat = IntPtr.Zero
                Return Nothing
            End If
            _eventMode = True
            Return client
        Catch
            Return Nothing
        Finally
            If handler IsNot Nothing Then handler.Release()
            If operationPtr <> IntPtr.Zero Then Marshal.Release(operationPtr)
            If propPtr <> IntPtr.Zero Then Marshal.FreeHGlobal(propPtr)
            If paramsPtr <> IntPtr.Zero Then Marshal.FreeHGlobal(paramsPtr)
        End Try
    End Function

    Private Shared Function FindExclusionTargetPid() As Integer
        For Each baseName As String In New String() {"FastMediaSorter_LITE", "FastMediaSorter_x86"}
            Dim processes() As Process = Process.GetProcessesByName(baseName)
            If processes.Length > 0 Then
                Dim pid As Integer = processes(0).Id
                For Each p As Process In processes
                    p.Dispose()
                Next
                Return pid
            End If
        Next
        Return GetCurrentProcessId()
    End Function

    ''' <summary>The completion handler ActivateAudioInterfaceAsync calls back: a managed class behind
    ''' the interface's own GUID, so the runtime builds the CCW the OS calls.</summary>
    Private NotInheritable Class ExclusionActivationHandler
        Implements IActivateAudioInterfaceCompletedHandler

        Public ReadOnly Done As New ManualResetEvent(False)
        Public ActivateHr As Integer
        Public ClientPtr As IntPtr

        Public Sub ActivateCompleted(activateOperation As IActivateAudioInterfaceAsyncOperation) Implements IActivateAudioInterfaceCompletedHandler.ActivateCompleted
            Try
                Dim activated As IntPtr = IntPtr.Zero
                Dim activateHr As Integer
                Dim opHr As Integer = activateOperation.GetActivateResult(activateHr, activated)
                Detail = "op=" & opHr.ToString("X8", Globalization.CultureInfo.InvariantCulture) &
                         " ar=" & activateHr.ToString("X8", Globalization.CultureInfo.InvariantCulture) &
                         " ptr=" & activated.ToString()
                If opHr = 0 AndAlso activated <> IntPtr.Zero Then
                    ' QI for IAudioClient, the interface this file already declares. The local is
                    ' spelled differently from the field on purpose: VB ignores case, and a local
                    ' named like the field silently shadows it.
                    Dim iidAudioClient As New Guid("1CB9AD4C-DBFA-4C32-B178-C2F568A703B2")
                    Dim qiResult As IntPtr = IntPtr.Zero
                    Dim qiHr As Integer = Marshal.QueryInterface(activated, iidAudioClient, qiResult)
                    Detail &= " qi=" & qiHr.ToString("X8", Globalization.CultureInfo.InvariantCulture)
                    If qiHr = 0 Then
                        ClientPtr = qiResult
                        ActivateHr = 0
                    Else
                        ActivateHr = If(activateHr <> 0, activateHr, -2147467259)
                    End If
                    Marshal.Release(activated)
                Else
                    ActivateHr = If(activateHr <> 0, activateHr, -2147467259)
                End If
            Catch ex As Exception
                Detail &= " ex=" & ex.GetType().Name
                ActivateHr = -2147467259
            End Try
            Done.Set()
        End Sub

        Public Detail As String = ""

        Public ReadOnly Property Client As IAudioClient
            Get
                If ClientPtr = IntPtr.Zero Then Return Nothing
                Return TryCast(Marshal.GetObjectForIUnknown(ClientPtr), IAudioClient)
            End Get
        End Property

        Public Sub Release()
            If ClientPtr <> IntPtr.Zero Then
                Marshal.Release(ClientPtr)
                ClientPtr = IntPtr.Zero
            End If
        End Sub

    End Class

    Private Shared Function GetClsidEnumerator() As Guid
        Return New Guid("BCDE0395-E52F-467C-8E3D-C4579291692E")
    End Function

    Private Shared Function GetIidEnumerator() As Guid
        Return New Guid("A95664D2-9614-4F35-A746-DE8DB63617E6")
    End Function

    ' --- the COM shapes, in vtable order -------------------------------------------------

    ' The completion handler the OS calls back. NOT ComImport - it is implemented by the managed
    ' class above - but it carries the interface's own GUID so the CCW answers the OS's QueryInterface.
    <Guid("41D949AB-9862-444A-80F6-C261334DA5EB"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)>
    Private Interface IActivateAudioInterfaceCompletedHandler
        Sub ActivateCompleted(<MarshalAs(UnmanagedType.[Interface])> activateOperation As IActivateAudioInterfaceAsyncOperation)
    End Interface

    <ComImport, Guid("72A22D78-CDE4-431D-B8CC-843A71199B6D"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)>
    Private Interface IActivateAudioInterfaceAsyncOperation
        <PreserveSig> Function GetActivateResult(ByRef activateResult As Integer, ByRef activatedInterface As IntPtr) As Integer
    End Interface

    <ComImport, Guid("A95664D2-9614-4F35-A746-DE8DB63617E6"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)>
    Private Interface IMMDeviceEnumerator
        <PreserveSig> Function EnumAudioEndpoints(dataFlow As Integer, stateMask As Integer, <Out, MarshalAs(UnmanagedType.Interface)> ByRef devices As Object) As Integer
        <PreserveSig> Function GetDefaultAudioEndpoint(dataFlow As Integer, role As Integer, <Out, MarshalAs(UnmanagedType.Interface)> ByRef endpoint As IMMDevice) As Integer
        <PreserveSig> Function GetDevice(<MarshalAs(UnmanagedType.LPWStr)> id As String, <Out, MarshalAs(UnmanagedType.Interface)> ByRef endpoint As IMMDevice) As Integer
        <PreserveSig> Function RegisterEndpointNotificationCallback(client As Object) As Integer
        <PreserveSig> Function UnregisterEndpointNotificationCallback(client As Object) As Integer
    End Interface

    <ComImport, Guid("D666063F-1587-4E43-81F1-B948E807363F"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)>
    Private Interface IMMDevice
        <PreserveSig> Function Activate(ByRef iid As Guid, clsCtx As Integer, activationParams As IntPtr, <Out, MarshalAs(UnmanagedType.Interface)> ByRef instance As Object) As Integer
        <PreserveSig> Function OpenPropertyStore(access As Integer, <Out, MarshalAs(UnmanagedType.Interface)> ByRef store As Object) As Integer
        <PreserveSig> Function GetId(<Out, MarshalAs(UnmanagedType.LPWStr)> ByRef id As String) As Integer
        <PreserveSig> Function GetState(ByRef state As Integer) As Integer
    End Interface

    <ComImport, Guid("1CB9AD4C-DBFA-4C32-B178-C2F568A703B2"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)>
    Private Interface IAudioClient
        <PreserveSig> Function Initialize(shareMode As Integer, streamFlags As Integer, bufferDuration As Long, periodicity As Long, format As IntPtr, audioSessionGuid As IntPtr) As Integer
        <PreserveSig> Function GetBufferSize(ByRef frames As Integer) As Integer
        <PreserveSig> Function GetStreamLatency(ByRef latency As Long) As Integer
        <PreserveSig> Function GetCurrentPadding(ByRef padding As Integer) As Integer
        <PreserveSig> Function IsFormatSupported(shareMode As Integer, format As IntPtr, closestMatch As IntPtr) As Integer
        <PreserveSig> Function GetMixFormat(ByRef format As IntPtr) As Integer
        <PreserveSig> Function GetDevicePeriod(ByRef defaultPeriod As Long, ByRef minimumPeriod As Long) As Integer
        <PreserveSig> Function Start() As Integer
        <PreserveSig> Function [Stop]() As Integer
        <PreserveSig> Function Reset() As Integer
        <PreserveSig> Function SetEventHandle(handle As IntPtr) As Integer
        <PreserveSig> Function GetService(ByRef iid As Guid, <Out, MarshalAs(UnmanagedType.Interface)> ByRef instance As Object) As Integer
    End Interface

    <ComImport, Guid("C8ADBD64-E71E-48A0-A4DE-185C395CD317"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)>
    Private Interface IAudioCaptureClient
        <PreserveSig> Function GetBuffer(<Out> ByRef data As IntPtr, ByRef frames As Integer, ByRef flags As Integer, ByRef devicePosition As Long, ByRef qpcPosition As Long) As Integer
        <PreserveSig> Function ReleaseBuffer() As Integer
        <PreserveSig> Function GetNextPacketSize(ByRef frames As Integer) As Integer
    End Interface

    <StructLayout(LayoutKind.Sequential, Pack:=1)>
    Private Structure WAVEFORMATEX
        Public wFormatTag As Short
        Public nChannels As Short
        Public nSamplesPerSec As Integer
        Public nAvgBytesPerSec As Integer
        Public nBlockAlign As Short
        Public wBitsPerSample As Short
        Public cbSize As Short
    End Structure

    ' --- the plain Win32 calls -----------------------------------------------------------

    ' audioclient.h / AudioSessionTypes.h: the stream flags this source uses, spelled once.
    Private Const StreamFlagLoopback As Integer = &H20000           ' AUDCLNT_STREAMFLAGS_LOOPBACK
    Private Const StreamFlagEventCallback As Integer = &H40000      ' AUDCLNT_STREAMFLAGS_EVENTCALLBACK
    Private Const StreamFlagAutoConvertPcm As Integer = &H80000000  ' AUDCLNT_STREAMFLAGS_AUTOCONVERTPCM (as signed)

    <StructLayout(LayoutKind.Sequential, CharSet:=CharSet.Unicode)>
    Private Structure OSVERSIONINFOW
        Public dwOSVersionInfoSize As Integer
        Public dwMajorVersion As Integer
        Public dwMinorVersion As Integer
        Public dwBuildNumber As Integer
        Public dwPlatformId As Integer
        <MarshalAs(UnmanagedType.ByValTStr, SizeConst:=128)>
        Public szCSDVersion As String
    End Structure

    <DllImport("ntdll.dll")>
    Private Shared Function RtlGetVersion(ByRef versionInfo As OSVERSIONINFOW) As Integer
    End Function

    <DllImport("kernel32.dll")>
    Private Shared Function GetCurrentProcessId() As Integer
    End Function

    <DllImport("mmdevapi.dll", EntryPoint:="ActivateAudioInterfaceAsync")>
    Private Shared Function ActivateAudioInterfaceAsync(<MarshalAs(UnmanagedType.LPWStr)> deviceInterfacePath As String,
                                                        ByRef iid As Guid,
                                                        activationParams As IntPtr,
                                                        completionHandler As IActivateAudioInterfaceCompletedHandler,
                                                        ByRef asyncOperation As IntPtr) As Integer
    End Function

    <DllImport("ole32.dll")>
    Private Shared Function CoInitializeEx(reserved As IntPtr, coInit As Integer) As Integer
    End Function

    <DllImport("ole32.dll")>
    Private Shared Sub CoUninitialize()
    End Sub

    <DllImport("ole32.dll")>
    Private Shared Function CoCreateInstance(ByRef clsid As Guid, outer As IntPtr, clsContext As Integer, ByRef iid As Guid, <Out, MarshalAs(UnmanagedType.Interface)> ByRef instance As Object) As Integer
    End Function

    <DllImport("ole32.dll")>
    Private Shared Sub CoTaskMemFree(ptr As IntPtr)
    End Sub

    <DllImport("kernel32.dll", SetLastError:=True)>
    Private Shared Function CreateEvent(attributes As IntPtr, manualReset As Boolean, initialState As Boolean, name As String) As IntPtr
    End Function

    <DllImport("kernel32.dll", SetLastError:=True)>
    Private Shared Function CloseHandle(handle As IntPtr) As Boolean
    End Function

    <DllImport("kernel32.dll", SetLastError:=True)>
    Private Shared Function SetEvent(handle As IntPtr) As Boolean
    End Function

    <DllImport("kernel32.dll", SetLastError:=True)>
    Private Shared Function WaitForSingleObject(handle As IntPtr, milliseconds As Integer) As Integer
    End Function

    <DllImport("kernel32.dll", SetLastError:=True)>
    Private Shared Function WaitForMultipleObjects(count As Integer, <MarshalAs(UnmanagedType.LPArray)> waitHandles As IntPtr(),
                                                   waitAll As Integer, milliseconds As Integer) As Integer
    End Function

End Class
