#If Not NETFRAMEWORK Then
Option Strict On

Imports System.Text

''' <summary>
''' The sealed metadata block's content (FDSEC-FORMAT §7): the facts the container
''' exists to hide - the true name, the real size, the original's timestamps - plus the
''' digest the read-back proof uses.
'''
''' Everything in here is display content, never state: the true name is shown while the
''' container is open and is written to no log, no registry value, no recent list and no
''' window title when the user has asked for it not to be (§5.4 rule 6, §9).
''' </summary>
Public NotInheritable Class FdSecMetadata

    ''' <summary>The original's own file name, with no path components.</summary>
    Public ReadOnly Property Name As String
    ''' <summary>The original's real size in bytes - what the decrypting stream reports
    ''' as its Length, never the container's own length.</summary>
    Public ReadOnly Property Size As Long
    ''' <summary>When the container was packed.</summary>
    Public ReadOnly Property EncodedAt As Date?
    Public ReadOnly Property CreatedAt As Date?
    Public ReadOnly Property AccessedAt As Date?
    Public ReadOnly Property ModifiedAt As Date?
    ''' <summary>BLAKE2b-256 of the original's bytes. A viewer can only check it when it
    ''' happens to have read the whole payload in order, which is why this reader never
    ''' claims a container is intact (§4.2).</summary>
    Public ReadOnly Property Digest As Byte()

    Private Sub New(name As String, size As Long, encodedAt As Date?, createdAt As Date?,
                    accessedAt As Date?, modifiedAt As Date?, digest As Byte())
        Me.Name = name
        Me.Size = size
        Me.EncodedAt = encodedAt
        Me.CreatedAt = createdAt
        Me.AccessedAt = accessedAt
        Me.ModifiedAt = modifiedAt
        Me.Digest = digest
    End Sub

    ''' <summary>
    ''' Parses the 4096-byte plaintext block. Everything it can refuse is DAMAGE, never a
    ''' credential problem: by the time this runs the container has authenticated twice
    ''' (key slot 0 and the metadata's own tag), so a name length outside 1..3000, an
    ''' unsafe true name or an impossible size can only mean the bytes are wrong
    ''' (FDSEC-FORMAT 1.1 §7, FDSEC-BEHAVIOUR 1.1 §4.3 and §5.2 step 6).
    ''' </summary>
    Friend Shared Function Parse(block As Byte()) As FdSecMetadata
        If block Is Nothing OrElse block.Length <> FdSecFormat.MetadataPlainSize Then
            Throw FdSecException.Damaged("metadata block is not " & FdSecFormat.MetadataPlainSize.ToString() & " bytes")
        End If

        Dim nameLength As Integer = FdSecPrimitives.ReadUInt16Le(block, 0)
        If nameLength < 1 OrElse nameLength > FdSecFormat.MaxNameBytes Then
            Throw FdSecException.Damaged("metadata name length " & nameLength.ToString())
        End If

        Dim offset As Integer = 2 + nameLength
        If offset + 40 + FdSecFormat.DigestSize > FdSecFormat.MetadataPlainSize Then
            Throw FdSecException.Damaged("metadata name overflows the block")
        End If

        Dim name As String = Encoding.UTF8.GetString(block, 2, nameLength)
        ' Refused, never sanitised - and the message never carries the name, which is the
        ' thing the format exists to hide.
        If Not IsAcceptableName(name) Then Throw FdSecException.Damaged("sealed true name is refused")

        ' Compared while still unsigned: CLng of anything above 2^63-1 throws
        ' OverflowException, which is no outcome class at all (§7).
        Dim rawSize As ULong = FdSecPrimitives.ReadUInt64Le(block, offset)
        If rawSize > CULng(Long.MaxValue) Then Throw FdSecException.Damaged("metadata size is not representable")
        Dim size As Long = CLng(rawSize)

        Dim digest(FdSecFormat.DigestSize - 1) As Byte
        Array.Copy(block, offset + 40, digest, 0, FdSecFormat.DigestSize)

        Return New FdSecMetadata(name, size,
                                 FromFileTime(FdSecPrimitives.ReadUInt64Le(block, offset + 8)),
                                 FromFileTime(FdSecPrimitives.ReadUInt64Le(block, offset + 16)),
                                 FromFileTime(FdSecPrimitives.ReadUInt64Le(block, offset + 24)),
                                 FromFileTime(FdSecPrimitives.ReadUInt64Le(block, offset + 32)),
                                 digest)
    End Function

    ''' <summary>
    ''' Windows FILETIME, and ZERO MEANS UNKNOWN - mistake 7 of FDSEC-BEHAVIOUR §13.1.
    ''' A reader that treats 0 as a date writes 1601 onto restored files; here it stays
    ''' Nothing and the surface simply does not show a date it does not have.
    ''' </summary>
    Friend Shared Function FromFileTime(value As ULong) As Date?
        If value = 0UL OrElse value > CULng(Long.MaxValue) Then Return Nothing
        Try
            Return Date.FromFileTimeUtc(CLng(value))
        Catch ex As ArgumentOutOfRangeException
            ' A timestamp outside the representable range is not worth failing a container
            ' for: the picture inside it is fine, and "unknown" is the honest answer.
            Return Nothing
        End Try
    End Function

    ''' <summary>
    ''' Stems Windows resolves to a device whatever extension follows them - "nul.txt" is
    ''' NUL. The stem reading is the owner's reference reader's, not an addition here.
    ''' </summary>
    Private Shared ReadOnly ReservedDeviceStems As New HashSet(Of String)(StringComparer.OrdinalIgnoreCase) From {
        "CON", "PRN", "AUX", "NUL",
        "COM1", "COM2", "COM3", "COM4", "COM5", "COM6", "COM7", "COM8", "COM9",
        "LPT1", "LPT2", "LPT3", "LPT4", "LPT5", "LPT6", "LPT7", "LPT8", "LPT9"
    }

    ''' <summary>
    ''' The 1.1 name rule (FDSEC-FORMAT §7, FDSEC-BEHAVIOUR §4.3). The true name is
    ''' attacker-controlled the moment somebody else made the container (§14.1), and a
    ''' name that could impersonate a path or a device is REFUSED as damage - never
    ''' rewritten into something displayable, because a rewritten name is a name the
    ''' packer never wrote.
    '''
    ''' Refused: empty, "." or "..", a path separator, a colon, a wildcard, a control
    ''' character (below 32, or 127), a reserved device name (with or without an
    ''' extension), and a trailing dot or space. Parsed by hand, never through
    ''' System.IO.Path - this string was never a path, and Path reads "a:b.jpg" as a
    ''' volume and a shorter name.
    ''' </summary>
    Friend Shared Function IsAcceptableName(name As String) As Boolean
        If String.IsNullOrEmpty(name) Then Return False
        If name = "." OrElse name = ".." Then Return False
        For Each ch As Char In name
            Select Case ch
                Case "/"c, "\"c, ":"c, "*"c, "?"c
                    Return False
            End Select
            Dim code As Integer = AscW(ch)
            If code < 32 OrElse code = 127 Then Return False
        Next
        Dim lastChar As Char = name(name.Length - 1)
        If lastChar = "."c OrElse lastChar = " "c Then Return False

        Dim dot As Integer = name.IndexOf("."c)
        Dim stem As String = If(dot < 0, name, name.Substring(0, dot))
        Return Not ReservedDeviceStems.Contains(stem)
    End Function

    ''' <summary>
    ''' The sealed name's extension, lowercased, as the classifier wants it - "" when
    ''' there is none.
    '''
    ''' Hand-parsed for the reason IsAcceptableName gives. Parse already refused every
    ''' name with a separator or a colon, so what is left is a plain file name.
    ''' </summary>
    Public Function ContentExtension() As String
        Dim dot As Integer = Name.LastIndexOf("."c)
        If dot < 0 OrElse dot = Name.Length - 1 Then Return ""
        Return Name.Substring(dot).ToLowerInvariant()
    End Function

End Class
#End If
