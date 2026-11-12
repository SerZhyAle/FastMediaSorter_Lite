Option Strict On

''' <summary>What a row of the queue screen says about its packet, from this PC's point of view (item CS).</summary>
Friend Enum DriveQueueRole
    ''' <summary>This PC wrote it. It stays until its target takes it or it expires (items CR and CT).</summary>
    Sent
    ''' <summary>Offered to this PC, not taken yet.</summary>
    Waiting
    ''' <summary>This PC took it. An untargeted packet stays for the other own devices until it expires.</summary>
    Received
    ''' <summary>Between two other own devices: not this PC's to take, still the person's to delete.</summary>
    OtherDevices
End Enum

''' <summary>One packet the channel holds.</summary>
Friend NotInheritable Class DriveQueueRow

    Public ReadOnly Packet As ExchangeDrivePacket
    Public ReadOnly Role As DriveQueueRole

    Public Sub New(packet As ExchangeDrivePacket, role As DriveQueueRole)
        Me.Packet = packet
        Me.Role = role
    End Sub

    ''' <summary>What the screen compares to leave an unchanged list and its selection alone.</summary>
    Public ReadOnly Property Key As String
        Get
            Return Packet.FolderId & ":" & CInt(Role).ToString(Globalization.CultureInfo.InvariantCulture)
        End Get
    End Property
End Class

''' <summary>What the queue held as of one polling round: the rows, and the packets that have no row.</summary>
Friend NotInheritable Class DriveQueueView

    Public ReadOnly Rows As IReadOnlyList(Of DriveQueueRow)
    ''' <summary>Packet folders with no manifest yet: an upload in progress somewhere.</summary>
    Public ReadOnly InProgress As Integer
    ''' <summary>Packets whose manifest this program refuses: there is nothing to show about them.</summary>
    Public ReadOnly Unreadable As Integer

    Public Sub New(rows As IReadOnlyList(Of DriveQueueRow), inProgress As Integer, unreadable As Integer)
        Me.Rows = rows
        Me.InProgress = inProgress
        Me.Unreadable = unreadable
    End Sub
End Class

''' <summary>The model of the queue screen, item CS: every packet of the queue with its size, each of them deletable. Pure.</summary>
Friend NotInheritable Class DriveQueueScreen

    Private Sub New()
    End Sub

    ''' <summary>The role of one packet. A manifest this PC wrote is "sent" whatever its address says.</summary>
    Public Shared Function RoleOf(packet As ExchangeDrivePacket, wasTaken As Func(Of ExchangeDrivePacket, Boolean)) As DriveQueueRole
        If packet.IsOwn Then Return DriveQueueRole.Sent
        If packet.Address = ExchangeDriveAddress.OtherDevice Then Return DriveQueueRole.OtherDevices
        Return If(wasTaken(packet), DriveQueueRole.Received, DriveQueueRole.Waiting)
    End Function

    ''' <param name="packets">What the round found, oldest first: the order of the rows.</param>
    ''' <param name="wasTaken">The queue's handled set; it may read a file on its first call.</param>
    Public Shared Function Build(packets As IEnumerable(Of ExchangeDrivePacket), inProgress As Integer, unreadable As Integer,
                                 wasTaken As Func(Of ExchangeDrivePacket, Boolean)) As DriveQueueView
        Dim rows As New List(Of DriveQueueRow)
        For Each packet As ExchangeDrivePacket In packets
            rows.Add(New DriveQueueRow(packet, RoleOf(packet, wasTaken)))
        Next
        Return New DriveQueueView(rows, inProgress, unreadable)
    End Function

End Class
