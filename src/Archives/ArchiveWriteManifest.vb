#If Not NETFRAMEWORK Then
Option Explicit On
Option Strict On
Option Infer On
Option Compare Binary

Imports System.IO
Imports System.Threading

' Neutral streaming inputs. The adapter owns provider leases and version comparisons.
Friend NotInheritable Class ArchiveWriteInput
    Friend Property Name As String
    Friend Property IsDirectory As Boolean
    Friend Property Length As Long
    Friend Property ModifiedUtc As DateTime
    Friend Property OpenRead As Func(Of CancellationToken, Stream)
    Friend Property ValidateVersion As Action
End Class

Friend NotInheritable Class ArchiveWriteRecord
    Friend Property Name As String
    Friend Property IsDirectory As Boolean
    Friend Property Length As Long
    Friend Property Sha256 As Byte()
    Friend Property Crc32 As UInteger
End Class

Friend NotInheritable Class VerifiedZipArtifact
    Friend Property Path As String
    Friend Property Records As IReadOnlyList(Of ArchiveWriteRecord)
    Friend Property Bytes As Long
End Class

Friend Enum ArchiveFailure
    UnsafeName
    DuplicateName
    ChangedInput
    Verification
    Encrypted
    Unsupported
    Broken
    MultiVolume
    Link
    EntryLimit
    EntrySizeLimit
    TotalSizeLimit
    ExpansionLimit
    Space
End Enum

Friend NotInheritable Class ArchiveSafetyException
    Inherits IOException
    Friend Sub New(reason As ArchiveFailure)
        MyBase.New("Archive refused: " & reason.ToString())
        Me.Reason = reason
    End Sub
    Friend ReadOnly Property Reason As ArchiveFailure
End Class
#End If
