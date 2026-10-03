#If Not NETFRAMEWORK Then
Option Strict On

Imports System.IO
Imports System.Linq
Imports System.Reflection
Imports System.Text
Imports Xunit

Public Class FdSecAuditTests
    <Theory>
    <InlineData(0UL)>
    <InlineData(9223372036854775807UL)>
    <InlineData(9223372036854775808UL)>
    <InlineData(18446744073709551615UL)>
    Public Sub Unrepresentable_file_times_are_unknown(value As ULong)
        Assert.Null(FdSecMetadata.FromFileTime(value))
    End Sub

    <Fact>
    Public Sub A_representable_file_time_keeps_its_utc_value()
        Dim expected As New DateTime(2026, 10, 1, 12, 0, 0, DateTimeKind.Utc)
        Assert.Equal(expected, FdSecMetadata.FromFileTime(CULng(expected.ToFileTimeUtc())).Value)
    End Sub

    <Fact>
    Public Sub Replacing_cached_keys_wipes_the_old_pair_and_keeps_caller_arrays()
        Dim cache As New FdSecKeyCache()
        Dim credential As Byte() = Encoding.UTF8.GetBytes("audit credential")
        Dim salt(15) As Byte
        Dim mask As Byte() = Enumerable.Repeat(CByte(7), 32).ToArray()
        Dim kek As Byte() = Enumerable.Repeat(CByte(9), 32).ToArray()
        cache.Put(credential, salt, mask, kek)
        ' Retain the retired buffers to observe their contents after replacement.
        Dim field As FieldInfo = GetType(FdSecKeyCache).GetField("entries", BindingFlags.Instance Or BindingFlags.NonPublic)
        Dim entries = DirectCast(field.GetValue(cache), Dictionary(Of String, Byte()()))
        Dim retired As Byte()() = entries.Values.Single()
        cache.Put(credential, salt, mask, kek)
        Assert.All(retired(0), Sub(value) Assert.Equal(CByte(0), value))
        Assert.All(retired(1), Sub(value) Assert.Equal(CByte(0), value))
        Dim retrievedMask As Byte() = Nothing
        Dim retrievedKek As Byte() = Nothing
        Assert.True(cache.TryGet(credential, salt, retrievedMask, retrievedKek))
        Assert.Equal(mask, retrievedMask)
        Assert.Equal(kek, retrievedKek)
        Assert.All(mask, Sub(value) Assert.Equal(CByte(7), value))
        Assert.All(kek, Sub(value) Assert.Equal(CByte(9), value))
        cache.Clear()
    End Sub

    <Fact>
    Public Sub An_authenticated_unsupported_head_does_not_hand_out_a_file_key()
        Const credential As String = "audit credential"
        Dim packed As Byte() = FdSecTestContainer.Pack(New Byte(31) {}, "photo.jpg", credential,
            FdSecTestContainer.FastProfile, declaredChunkSize:=&H80000000UI)
        Dim fileKey As Byte() = Nothing
        Using source As New MemoryStream(packed, writable:=False)
            Dim failure = Assert.Throws(Of FdSecException)(
                Sub() FdSecHead.Open(source, Encoding.UTF8.GetBytes(credential), FdSecTestContainer.FastProfile, Nothing, fileKey))
            Assert.Equal(FdSecOutcome.Unsupported, failure.Outcome)
            Assert.Null(fileKey)
        End Using
    End Sub
End Class
#End If
