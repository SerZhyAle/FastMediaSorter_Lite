#If Not NETFRAMEWORK Then
Option Strict On

' Ticket 199 sections 3 and 4: the producer's settings. The port is a GUARANTEED setting in the
' contract's words: default 8768, the user's number, no fallback - if it is taken the broadcast does
' not start. The audio numbers (44 100 Hz, 128 kbit/s, mono for the microphone) are settings too.
' Stereo is the deviation proposal ask 5 asks the owner to bless: system audio keeps it in this build,
' the settings page marks it "not in the written shape", and the gate is shut anyway.
'
' The store is injected (the Share Manager passes the VB SaveSetting/GetSetting hive, the tests pass a
' dictionary): the dormant-state rule of section 8 holds that nothing is read or written while the
' feature is off, which the controller enforces by never loading this class behind the shut gate.

Friend Class LiveProducerSettings

    ''' <summary>The audio listener port (2.1). No fallback: a taken port refuses the start.</summary>
    Public Property AudioPort As Integer = LiveProducerContract.DefaultAudioPort

    Public Property SampleRate As Integer = LiveProducerContract.DefaultSampleRate

    Public Property AudioBitrateKbps As Integer = LiveProducerContract.DefaultAudioBitrateKbps

    ''' <summary>Microphone channels: mono, the shape's own words.</summary>
    Public Property MicrophoneChannels As Integer = LiveProducerContract.MonoChannels

    ''' <summary>System audio channels: stereo, kept until the owner blesses ask 5. The settings page
    ''' shows the "not in the written shape" sentence next to it.</summary>
    Public Property SystemAudioChannels As Integer = LiveProducerContract.StereoChannels

    ''' <summary>The broadcast title, carried by the descriptor (optional member of 3.1).</summary>
    Public Property Title As String = ""

    ''' <summary>Section 3.4 (Q199-3): a LAN listener for video is OFF by default - the LAN shapes have
    ''' a fixed path and no credential, and for a screen that shows the picture to anyone on the
    ''' segment. Video starts exchange-only (173) unless the user ticks "Allow viewers on my network".</summary>
    Public Property AllowLanVideoViewers As Boolean = False
    ''' <summary>DEVICE-EXCHANGE item M: server-brokered relay/tunnel connections, zero for no cap. LAN viewing is independent.</summary>
    Public Property MaxListeners As Integer

    ''' <summary>Safety rule 2: a small topmost "on air" chip while anything is captured, a setting,
    ''' default on for the screen.</summary>
    Public Property OnAirChip As Boolean = True

    ''' <summary>Section 4: WHICH monitor the screen source captures - the picker's row, clamped to
    ''' the monitor count at start time (a remembered index outlived by a unplugged screen falls back
    ''' to the first one).</summary>
    Public Property ScreenMonitorIndex As Integer = 0

    ''' <summary>Ticket 263 T-2: the window the screen source captures INSTEAD of the monitor - its title, as
    ''' remembered when the person picked it. A window handle is not stable across runs, so the title and
    ''' the process name are what is stored; the broadcast start looks the window up again (a window that is
    ''' not open refuses the start, it never falls back to the monitor). Empty = the whole monitor.</summary>
    Public Property ScreenWindowTitle As String = ""

    ''' <summary>The process name (without ".exe") of the remembered window; see <see cref="ScreenWindowTitle"/>.</summary>
    Public Property ScreenWindowProcess As String = ""

    ''' <summary>True when a window, not the monitor, is the screen source's target.</summary>
    Public ReadOnly Property HasScreenWindow As Boolean
        Get
            Return Not String.IsNullOrEmpty(ScreenWindowTitle)
        End Get
    End Property

    ''' <summary>Section 4: the cursor toggle of the screen source, on by default.</summary>
    Public Property ScreenCursor As Boolean = True

    ''' <summary>Section 4 (B3): WHICH camera the camera source captures - the picker's row, clamped
    ''' to the device count at start time (a remembered index outlived by an unplugged camera falls
    ''' back to the first one that answers).</summary>
    Public Property CameraDeviceIndex As Integer = 0

    ''' <summary>Section 4 (B3): WHICH capture mode the camera uses - the picker's row, or -1 for
    ''' the automatic best (LiveCameraModes.PickBest, the default).</summary>
    Public Property CameraModeIndex As Integer = -1

    ''' <summary>Section 4: the microphone gain is applied by the producer (consumer rule 6: a quiet
    ''' microphone is a producer defect and the consumer adds none). 1.0 = the device's own level.</summary>
    Public Property MicrophoneGain As Double = 1.0

    Public Const KeyAudioPort As String = "Live_AudioPort"
    Public Const KeySampleRate As String = "Live_SampleRate"
    Public Const KeyAudioBitrateKbps As String = "Live_AudioBitrateKbps"
    Public Const KeyMicrophoneChannels As String = "Live_MicrophoneChannels"
    Public Const KeySystemAudioChannels As String = "Live_SystemAudioChannels"
    Public Const KeyTitle As String = "Live_Title"
    Public Const KeyAllowLanVideoViewers As String = "Live_AllowLanVideoViewers"
    Public Const KeyMaxListeners As String = "Live_MaxListeners"
    Public Const KeyOnAirChip As String = "Live_OnAirChip"
    Public Const KeyMicrophoneGain As String = "Live_MicrophoneGain"
    Public Const KeyScreenMonitorIndex As String = "Live_ScreenMonitorIndex"
    Public Const KeyScreenCursor As String = "Live_ScreenCursor"
    Public Const KeyScreenWindowTitle As String = "Live_ScreenWindowTitle"
    Public Const KeyScreenWindowProcess As String = "Live_ScreenWindowProcess"
    Public Const KeyCameraDeviceIndex As String = "Live_CameraDeviceIndex"
    Public Const KeyCameraModeIndex As String = "Live_CameraModeIndex"

    Public Const MinPort As Integer = 1024
    Public Const MaxPort As Integer = 65535
    Public Const MinGain As Double = 0.1
    Public Const MaxGain As Double = 4.0

    Public Shared Function ClampPort(port As Integer) As Integer
        Return Math.Max(MinPort, Math.Min(MaxPort, port))
    End Function

    Public Shared Function ClampGain(gain As Double) As Double
        Return Math.Max(MinGain, Math.Min(MaxGain, gain))
    End Function

    Public Shared Function SanitizeChannels(channels As Integer) As Integer
        If channels < 1 Then Return LiveProducerContract.MonoChannels
        If channels > 2 Then Return LiveProducerContract.StereoChannels
        Return channels
    End Function

    ''' <summary>Loads from the injected store. The store hands back the raw string of the value or
    ''' Nothing - exactly what GetSetting with a default of Nothing returns in the Share Manager.</summary>
    Public Shared Function Load(read As Func(Of String, String)) As LiveProducerSettings
        Dim s As New LiveProducerSettings()
        If read Is Nothing Then Return s
        s.AudioPort = ClampPort(ReadInt(read, KeyAudioPort, LiveProducerContract.DefaultAudioPort))
        s.SampleRate = ReadInt(read, KeySampleRate, LiveProducerContract.DefaultSampleRate)
        s.AudioBitrateKbps = ReadInt(read, KeyAudioBitrateKbps, LiveProducerContract.DefaultAudioBitrateKbps)
        s.MicrophoneChannels = SanitizeChannels(ReadInt(read, KeyMicrophoneChannels, LiveProducerContract.MonoChannels))
        s.SystemAudioChannels = SanitizeChannels(ReadInt(read, KeySystemAudioChannels, LiveProducerContract.StereoChannels))
        s.Title = If(read(KeyTitle), "")
        s.AllowLanVideoViewers = ReadBool(read, KeyAllowLanVideoViewers, False)
        s.MaxListeners = Math.Max(0, Math.Min(10000, ReadInt(read, KeyMaxListeners, 0)))
        s.OnAirChip = ReadBool(read, KeyOnAirChip, True)
        s.MicrophoneGain = ClampGain(ReadDouble(read, KeyMicrophoneGain, 1.0))
        s.ScreenMonitorIndex = Math.Max(0, ReadInt(read, KeyScreenMonitorIndex, 0))
        s.ScreenCursor = ReadBool(read, KeyScreenCursor, True)
        s.ScreenWindowTitle = If(read(KeyScreenWindowTitle), "")
        s.ScreenWindowProcess = If(read(KeyScreenWindowProcess), "")
        s.CameraDeviceIndex = Math.Max(0, ReadInt(read, KeyCameraDeviceIndex, 0))
        s.CameraModeIndex = Math.Max(-1, ReadInt(read, KeyCameraModeIndex, -1))
        Return s
    End Function

    ''' <summary>Persists to the injected store, one value at a time, the way the Share Manager's hive
    ''' works (SaveSetting). The caller owns the store; this class never touches the registry itself.</summary>
    Public Sub Save(write As Action(Of String, String))
        If write Is Nothing Then Return
        write(KeyAudioPort, AudioPort.ToString(Globalization.CultureInfo.InvariantCulture))
        write(KeySampleRate, SampleRate.ToString(Globalization.CultureInfo.InvariantCulture))
        write(KeyAudioBitrateKbps, AudioBitrateKbps.ToString(Globalization.CultureInfo.InvariantCulture))
        write(KeyMicrophoneChannels, MicrophoneChannels.ToString(Globalization.CultureInfo.InvariantCulture))
        write(KeySystemAudioChannels, SystemAudioChannels.ToString(Globalization.CultureInfo.InvariantCulture))
        write(KeyTitle, If(Title, ""))
        write(KeyAllowLanVideoViewers, If(AllowLanVideoViewers, "1", "0"))
        write(KeyMaxListeners, Math.Max(0, Math.Min(10000, MaxListeners)).ToString(Globalization.CultureInfo.InvariantCulture))
        write(KeyOnAirChip, If(OnAirChip, "1", "0"))
        write(KeyMicrophoneGain, ClampGain(MicrophoneGain).ToString("0.##", Globalization.CultureInfo.InvariantCulture))
        write(KeyScreenMonitorIndex, Math.Max(0, ScreenMonitorIndex).ToString(Globalization.CultureInfo.InvariantCulture))
        write(KeyScreenCursor, If(ScreenCursor, "1", "0"))
        write(KeyScreenWindowTitle, If(ScreenWindowTitle, ""))
        write(KeyScreenWindowProcess, If(ScreenWindowProcess, ""))
        write(KeyCameraDeviceIndex, Math.Max(0, CameraDeviceIndex).ToString(Globalization.CultureInfo.InvariantCulture))
        write(KeyCameraModeIndex, Math.Max(-1, CameraModeIndex).ToString(Globalization.CultureInfo.InvariantCulture))
    End Sub

    Private Shared Function ReadInt(read As Func(Of String, String), key As String, fallback As Integer) As Integer
        Dim raw As String = read(key)
        Dim v As Integer
        If Integer.TryParse(raw, v) Then Return v
        Return fallback
    End Function

    Private Shared Function ReadDouble(read As Func(Of String, String), key As String, fallback As Double) As Double
        Dim raw As String = read(key)
        Dim v As Double
        If Double.TryParse(raw, Globalization.CultureInfo.InvariantCulture, v) Then Return v
        Return fallback
    End Function

    Private Shared Function ReadBool(read As Func(Of String, String), key As String, fallback As Boolean) As Boolean
        Dim raw As String = read(key)
        If String.IsNullOrEmpty(raw) Then Return fallback
        Return raw.Trim() = "1"
    End Function

End Class
#End If
