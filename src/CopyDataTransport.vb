Option Strict On

Imports System.Runtime.InteropServices
Imports System.Text

''' <summary>Why a WM_COPYDATA payload was refused. Never a reason to touch anything.</summary>
Friend Enum PayloadRefusal
    None = 0
    ''' <summary>cbData is zero or negative, or lpData is null.</summary>
    BadBounds = 1
    ''' <summary>More than <see cref="CopyDataTransport.MaxPayloadBytes"/> bytes.</summary>
    TooLarge = 2
    ''' <summary>Not strictly valid UTF-8 (or text that has no UTF-8 form, a lone surrogate).</summary>
    MalformedUtf8 = 3
    ''' <summary>A control character - NUL, line breaks, tabs, DEL - which no path and no
    ''' command contains.</summary>
    ControlCharacter = 4
    Empty = 5
End Enum

Friend Enum ForwardAttempt
    ''' <summary>A window answered "handled" (ack 1).</summary>
    Acknowledged = 0
    ''' <summary>The instance exists but nothing accepted the payload yet (no window, a helper
    ''' window answering 0, a timeout) - worth another try inside the budget.</summary>
    NotAcknowledged = 1
    ''' <summary>There is nobody to deliver to at all - waiting is pointless.</summary>
    NoTarget = 2
End Enum

Friend Enum ForwardOutcome
    Delivered = 0
    ''' <summary>The budget ran out without an acknowledgement. The outcome of the LAST send is
    ''' uncertain - the receiver may still process it - so a caller must not replay a mutation.</summary>
    TimedOut = 1
    NoTarget = 2
End Enum

''' <summary>
''' The transport policy of the single-instance hand-off, one copy for the viewer and the
''' Share Manager (105 L04, 108 H01.2): the byte cap, strict UTF-8, no control characters, a
''' sender that refuses what it cannot send whole (a path is NEVER truncated - a shorter
''' string can name a different directory), and a bounded monotonic send loop.
'''
''' This validates a message; it does not authenticate its sender. WM_COPYDATA cannot do that.
''' </summary>
Friend Module CopyDataTransport

    ''' <summary>A byte cap, not a character count: 64 KiB.</summary>
    Friend Const MaxPayloadBytes As Integer = 64 * 1024

    ''' <summary>dwData of a message to the Share Manager: "CMPN". Shared by the viewer's wake and
    ''' the Share Manager's own second-instance forward, so there is exactly one value. Frozen.</summary>
    Friend Const CompanionTag As Integer = &H434D504E

    Friend Const Total_Budget_Ms As Integer = 6000
    Friend Const Per_Send_Timeout_Ms As Integer = 3000
    Friend Const Retry_Pause_Ms As Integer = 250

    Private ReadOnly StrictUtf8 As New UTF8Encoding(encoderShouldEmitUTF8Identifier:=False, throwOnInvalidBytes:=True)

    ''' <summary>Bounds only, before anything is allocated or copied.</summary>
    Friend Function CheckBounds(cbData As Integer, lpData As IntPtr) As PayloadRefusal
        If cbData <= 0 OrElse lpData = IntPtr.Zero Then Return PayloadRefusal.BadBounds
        If cbData > MaxPayloadBytes Then Return PayloadRefusal.TooLarge
        Return PayloadRefusal.None
    End Function

    ''' <summary>Strict decode of bytes already inside the bounds.</summary>
    Friend Function TryDecodeBytes(data As Byte(), ByRef text As String, ByRef refusal As PayloadRefusal) As Boolean
        text = String.Empty
        refusal = PayloadRefusal.None
        If data Is Nothing OrElse data.Length = 0 Then
            refusal = PayloadRefusal.BadBounds
            Return False
        End If
        If data.Length > MaxPayloadBytes Then
            refusal = PayloadRefusal.TooLarge
            Return False
        End If
        Dim decoded As String
        Try
            decoded = StrictUtf8.GetString(data)
        Catch ex As DecoderFallbackException
            refusal = PayloadRefusal.MalformedUtf8
            Return False
        End Try
        refusal = ValidateText(decoded)
        If refusal <> PayloadRefusal.None Then Return False
        text = decoded
        Return True
    End Function

    ''' <summary>The receiver's decode: bounds, then a copy of at most cbData bytes, then the strict text rules.</summary>
    Friend Function TryDecode(cbData As Integer, lpData As IntPtr, ByRef text As String, ByRef refusal As PayloadRefusal) As Boolean
        text = String.Empty
        refusal = CheckBounds(cbData, lpData)
        If refusal <> PayloadRefusal.None Then Return False
        Dim buffer(cbData - 1) As Byte
        Try
            Marshal.Copy(lpData, buffer, 0, cbData)
        Catch ex As AccessViolationException
            refusal = PayloadRefusal.BadBounds
            Return False
        End Try
        Return TryDecodeBytes(buffer, text, refusal)
    End Function

    ''' <summary>The sender's encode. Refuses - never truncates - what does not fit whole.</summary>
    Friend Function TryEncode(text As String, ByRef bytes As Byte(), ByRef refusal As PayloadRefusal) As Boolean
        bytes = Nothing
        If String.IsNullOrEmpty(text) Then
            refusal = PayloadRefusal.Empty
            Return False
        End If
        refusal = ValidateText(text)
        If refusal <> PayloadRefusal.None Then Return False
        Try
            ' Counted before anything is built, so an oversized path costs no allocation of its size.
            If StrictUtf8.GetByteCount(text) > MaxPayloadBytes Then
                refusal = PayloadRefusal.TooLarge
                Return False
            End If
            bytes = StrictUtf8.GetBytes(text)
            Return True
        Catch ex As EncoderFallbackException
            refusal = PayloadRefusal.MalformedUtf8
            Return False
        End Try
    End Function
    ''' <summary>No control characters anywhere; the text is not blank.</summary>
    Friend Function ValidateText(text As String) As PayloadRefusal
        If String.IsNullOrWhiteSpace(text) Then Return PayloadRefusal.Empty
        For Each ch As Char In text
            If AscW(ch) < &H20 OrElse AscW(ch) = &H7F Then Return PayloadRefusal.ControlCharacter
        Next
        Return PayloadRefusal.None
    End Function

    ''' <summary>
    ''' The bounded send loop: one deadline on a monotonic clock, each send's timeout the smaller
    ''' of the per-send cap and what remains, retry pauses clipped to the remainder. Stops at the
    ''' first acknowledgement and at once when there is nobody to send to.
    ''' </summary>
    Friend Function RunForward(clockMs As Func(Of Long), sleepMs As Action(Of Integer),
                               attempt As Func(Of Integer, ForwardAttempt),
                               Optional totalMs As Integer = Total_Budget_Ms,
                               Optional perSendMs As Integer = Per_Send_Timeout_Ms,
                               Optional pauseMs As Integer = Retry_Pause_Ms) As ForwardOutcome
        Dim deadline As Long = clockMs() + totalMs
        Do
            Dim remaining As Long = deadline - clockMs()
            If remaining <= 0 Then Return ForwardOutcome.TimedOut
            Select Case attempt(CInt(Math.Min(CLng(perSendMs), remaining)))
                Case ForwardAttempt.Acknowledged
                    Return ForwardOutcome.Delivered
                Case ForwardAttempt.NoTarget
                    Return ForwardOutcome.NoTarget
            End Select
            remaining = deadline - clockMs()
            If remaining <= 0 Then Return ForwardOutcome.TimedOut
            sleepMs(CInt(Math.Min(CLng(pauseMs), remaining)))
        Loop
    End Function

End Module
