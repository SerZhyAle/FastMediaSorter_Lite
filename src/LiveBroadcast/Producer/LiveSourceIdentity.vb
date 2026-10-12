#If Not NETFRAMEWORK Then
Option Strict On

Imports System.Security.Cryptography

' Ticket 199 section 5: sourceId, "stable per device: the agent's deviceId when enrolled, otherwise a
' random id kept in SZA\FastMediaSorter (LiveBroadcast_SourceId); one broadcast at a time, so one id"
' (proposal ask 1, answered by amendment item G: sourceId names a source; a device that runs one
' broadcast at a time writes its own stable id alone).
'
' Pure, with the device id, the store and the generator injected: the Share Manager passes the exchange
' agent's account device id and the VB settings store; the tests pass fakes and never touch the hive
' (the dormant-state rule of section 8: no registry value while off - a value exists only after the
' user started a broadcast on this machine).

Friend NotInheritable Class LiveSourceIdentity

    Private Sub New()
    End Sub

    ''' <summary>Resolves the stable source id of this device.
    ''' 1. The exchange agent's device id, when this PC is enrolled - the id the family already knows.
    ''' 2. Otherwise the stored random id (LiveBroadcast_SourceId), when one was drawn before.
    ''' 3. Otherwise a new random id, drawn from the injected generator, written through the injected
    '''    writer and returned. The store and the writer are separate seams so a test can prove the
    '''    "no registry value while off" rule: nothing is written while the gate is shut, because the
    '''    identity is only resolved on a start.</summary>
    Public Shared Function Resolve(enrolledDeviceId As String,
                                  readStored As Func(Of String, String),
                                  writeStored As Action(Of String, String),
                                  generateRandom As Func(Of String)) As String
        If Not String.IsNullOrWhiteSpace(enrolledDeviceId) Then Return enrolledDeviceId.Trim()

        Dim stored As String = ""
        If readStored IsNot Nothing Then
            Try
                stored = readStored(LiveProducerContract.SourceIdRegistryValue)
            Catch
                stored = ""
            End Try
        End If
        If Not String.IsNullOrWhiteSpace(stored) Then Return stored.Trim()

        Dim fresh As String = If(generateRandom, AddressOf NewRandomId)()
        If String.IsNullOrWhiteSpace(fresh) Then fresh = NewRandomId()
        writeStored?.Invoke(LiveProducerContract.SourceIdRegistryValue, fresh)
        Return fresh
    End Function

    ''' <summary>A random id of the shape the family reads as opaque: "fmsw-" + 16 hex chars from one
    ''' RNGCryptoServiceProvider batch. Nothing in it is parsed by a consumer (3.1: "a consumer that
    ''' stores broadcasts treats the whole string as opaque").</summary>
    Public Shared Function NewRandomId() As String
        Dim bytes(7) As Byte
        RandomNumberGenerator.Fill(bytes)
        Dim hex As New Text.StringBuilder("fmsw-")
        For Each b As Byte In bytes
            hex.Append(b.ToString("X2", Globalization.CultureInfo.InvariantCulture))
        Next
        Return hex.ToString()
    End Function

    ''' <summary>Item G: a device that offers more than one source writes "&lt;deviceSourceId&gt;:&lt;source&gt;".
    ''' This product runs one live broadcast per device at a time (section 2), so the descriptor carries
    ''' the device id alone - the suffix form exists for the day that changes and is one call away.</summary>
    Public Shared Function SourceIdFor(deviceSourceId As String, sourceSuffix As String) As String
        If String.IsNullOrWhiteSpace(sourceSuffix) Then Return deviceSourceId
        Return deviceSourceId & ":" & sourceSuffix
    End Function

End Class
#End If
