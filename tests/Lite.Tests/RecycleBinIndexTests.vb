#If Not NETFRAMEWORK Then
Option Strict On

Imports System.Text
Imports Xunit

' What the Recycle Bin's own $I record says, and which record belongs to the deletion U is
' taking back (SPECIFICATION_RECYCLE_BIN_AND_UNDO_DOTNET10.md §3.6). Modern-only, exactly
' like the feature: RecycleBinIndex.vb is whole-file "#If Not NETFRAMEWORK", so on the net48
' leg of this project both it and this file compile to nothing.
'
' The tests build the record BYTES themselves, which is the whole reason the parser is a
' pure function: "an unknown version is refused rather than guessed at" and "a truncated
' record is refused" are exactly the cases you cannot produce by deleting a file and looking.
' And the matcher is where a wrong answer costs the most - picking the newest thing in the
' bin instead of the file that was actually asked for would restore a stranger's photo over
' the user's.
Public Class RecycleBinIndexTests

    Private Const Bin_Dir As String = "C:\$Recycle.Bin\S-1-5-21-1-2-3-1001\"

    ''' <summary>A v2 record, the Windows 10+ layout: header, size, FILETIME, the path
    ''' length in characters (terminator included), then the path.</summary>
    Private Shared Function V2Bytes(originalPath As String, deletedUtc As DateTime, sizeBytes As Long) As Byte()
        Dim path_Chars As Integer = originalPath.Length + 1
        Dim bytes(28 + path_Chars * 2 - 1) As Byte

        Buffer.BlockCopy(BitConverter.GetBytes(2L), 0, bytes, 0, 8)
        Buffer.BlockCopy(BitConverter.GetBytes(sizeBytes), 0, bytes, 8, 8)
        Buffer.BlockCopy(BitConverter.GetBytes(deletedUtc.ToFileTimeUtc()), 0, bytes, 16, 8)
        Buffer.BlockCopy(BitConverter.GetBytes(path_Chars), 0, bytes, 24, 4)
        Buffer.BlockCopy(Encoding.Unicode.GetBytes(originalPath), 0, bytes, 28, originalPath.Length * 2)
        ' The last two bytes stay zero: that is the terminator the length counts.
        Return bytes
    End Function

    ''' <summary>A v1 record, the Vista..8.1 layout: the same header, then the path in a
    ''' fixed 260-character field padded with nulls.</summary>
    Private Shared Function V1Bytes(originalPath As String, deletedUtc As DateTime, sizeBytes As Long) As Byte()
        Dim bytes(24 + 520 - 1) As Byte

        Buffer.BlockCopy(BitConverter.GetBytes(1L), 0, bytes, 0, 8)
        Buffer.BlockCopy(BitConverter.GetBytes(sizeBytes), 0, bytes, 8, 8)
        Buffer.BlockCopy(BitConverter.GetBytes(deletedUtc.ToFileTimeUtc()), 0, bytes, 16, 8)
        Buffer.BlockCopy(Encoding.Unicode.GetBytes(originalPath), 0, bytes, 24, originalPath.Length * 2)
        Return bytes
    End Function

    Private Shared Function Record(originalPath As String, deletedUtc As DateTime,
                                   Optional token As String = "$IAB1234.jpg") As RecycleBinRecord
        Return RecycleBinIndex.TryParse(V2Bytes(originalPath, deletedUtc, 4096), Bin_Dir & token)
    End Function

    ' --- parsing ---------------------------------------------------------------

    <Fact>
    Public Sub V2Record_ParsesPathTimeAndSize()
        Dim deleted As New DateTime(2026, 8, 14, 10, 30, 0, DateTimeKind.Utc)
        Dim parsed = RecycleBinIndex.TryParse(V2Bytes("C:\photos\cover.jpg", deleted, 123456),
                                              Bin_Dir & "$IAB1234.jpg")

        Assert.NotNull(parsed)
        Assert.Equal("C:\photos\cover.jpg", parsed.OriginalPath)
        Assert.Equal(deleted, parsed.DeletedUtc)
        Assert.Equal(123456L, parsed.SizeBytes)
    End Sub

    <Fact>
    Public Sub V1Record_ParsesTheFixedWidthPath()
        Dim deleted As New DateTime(2026, 8, 14, 9, 0, 0, DateTimeKind.Utc)
        Dim parsed = RecycleBinIndex.TryParse(V1Bytes("D:\scans\page 12.png", deleted, 2048),
                                              Bin_Dir & "$IZZ9999.png")

        Assert.NotNull(parsed)
        ' The null padding of the 260-character field must not come along with the name.
        Assert.Equal("D:\scans\page 12.png", parsed.OriginalPath)
        Assert.Equal(deleted, parsed.DeletedUtc)
    End Sub

    <Fact>
    Public Sub UnicodePath_SurvivesTheRoundTrip()
        Dim parsed = Record("C:\фото\обложка №2.jpg", DateTime.UtcNow)

        Assert.NotNull(parsed)
        Assert.Equal("C:\фото\обложка №2.jpg", parsed.OriginalPath)
    End Sub

    ' --- everything this build does not recognise is REFUSED, never guessed -----

    <Fact>
    Public Sub UnknownVersion_IsRefused()
        Dim bytes = V2Bytes("C:\photos\cover.jpg", DateTime.UtcNow, 10)
        Buffer.BlockCopy(BitConverter.GetBytes(7L), 0, bytes, 0, 8)

        Assert.Null(RecycleBinIndex.TryParse(bytes, Bin_Dir & "$IAB1234.jpg"))
    End Sub

    <Fact>
    Public Sub TruncatedV2Record_IsRefused()
        Dim bytes = V2Bytes("C:\photos\cover.jpg", DateTime.UtcNow, 10)
        Dim cut(bytes.Length - 9) As Byte
        Buffer.BlockCopy(bytes, 0, cut, 0, cut.Length)

        ' The header still claims a longer path than the file holds - believing it would
        ' read past the end of the record.
        Assert.Null(RecycleBinIndex.TryParse(cut, Bin_Dir & "$IAB1234.jpg"))
    End Sub

    <Fact>
    Public Sub TruncatedV1Record_IsRefused()
        Dim bytes = V1Bytes("C:\photos\cover.jpg", DateTime.UtcNow, 10)
        Dim cut(200) As Byte
        Buffer.BlockCopy(bytes, 0, cut, 0, cut.Length)

        Assert.Null(RecycleBinIndex.TryParse(cut, Bin_Dir & "$IAB1234.jpg"))
    End Sub

    <Fact>
    Public Sub AbsurdPathLength_IsRefused()
        Dim bytes = V2Bytes("C:\photos\cover.jpg", DateTime.UtcNow, 10)
        Buffer.BlockCopy(BitConverter.GetBytes(Integer.MaxValue), 0, bytes, 24, 4)

        ' A length nobody could have written is a corrupt record, not a long name - and
        ' allocating from it is allocating from a number a stranger wrote.
        Assert.Null(RecycleBinIndex.TryParse(bytes, Bin_Dir & "$IAB1234.jpg"))
    End Sub

    <Fact>
    Public Sub EmptyAndShortBuffers_AreRefused()
        Assert.Null(RecycleBinIndex.TryParse(Nothing, Bin_Dir & "$IAB1234.jpg"))
        Assert.Null(RecycleBinIndex.TryParse(New Byte(9) {}, Bin_Dir & "$IAB1234.jpg"))
    End Sub

    ' --- the $I -> $R pairing --------------------------------------------------

    <Fact>
    Public Sub DataPath_IsTheSameNameOneLetterApart()
        Dim parsed = Record("C:\photos\cover.jpg", DateTime.UtcNow, "$IAB1234.jpg")

        Assert.Equal(Bin_Dir & "$RAB1234.jpg", parsed.DataPath)
        Assert.Equal(Bin_Dir & "$IAB1234.jpg", parsed.IndexPath)
    End Sub

    <Fact>
    Public Sub AnIndexNameTheShellDidNotWrite_HasNoDataFile()
        ' No pairing rule applies, so there is nothing to restore - which is a refusal the
        ' caller can report, not an unrelated file to move.
        Assert.Equal("", RecycleBinIndex.DataPathFor(Bin_Dir & "readme.txt"))
        Assert.Equal("", RecycleBinIndex.DataPathFor(""))
    End Sub

    ' --- the matcher: by path and by time, never "the newest thing in the bin" ---

    <Fact>
    Public Sub BestMatch_PicksOurPath_NotTheNewestRecord()
        Dim ours As New DateTime(2026, 8, 14, 12, 0, 0, DateTimeKind.Utc)
        Dim records = {Record("C:\photos\cover.jpg", ours, "$IAAA1.jpg"),
                       Record("C:\photos\other.jpg", ours.AddMinutes(5), "$IBBB2.jpg")}

        Dim hit = RecycleBinIndex.BestMatch(records, "C:\photos\cover.jpg", ours)

        Assert.NotNull(hit)
        Assert.Equal(Bin_Dir & "$RAAA1.jpg", hit.DataPath)
    End Sub

    <Fact>
    Public Sub BestMatch_IgnoresARecordDeletedBeforeOurOperation()
        Dim ours As New DateTime(2026, 8, 14, 12, 0, 0, DateTimeKind.Utc)
        Dim records = {Record("C:\photos\cover.jpg", ours.AddMinutes(-30), "$IOLD11.jpg")}

        ' The same file, deleted and restored an hour ago: that record is not ours, and
        ' restoring it would put a different version of the picture back.
        Assert.Null(RecycleBinIndex.BestMatch(records, "C:\photos\cover.jpg", ours))
    End Sub

    <Fact>
    Public Sub BestMatch_TakesTheNewestOfSeveralForTheSamePath()
        Dim ours As New DateTime(2026, 8, 14, 12, 0, 0, DateTimeKind.Utc)
        Dim records = {Record("C:\photos\cover.jpg", ours, "$IFIRST.jpg"),
                       Record("C:\photos\cover.jpg", ours.AddSeconds(30), "$ISECOND.jpg")}

        Dim hit = RecycleBinIndex.BestMatch(records, "C:\photos\cover.jpg", ours)

        Assert.Equal(Bin_Dir & "$RSECOND.jpg", hit.DataPath)
    End Sub

    <Fact>
    Public Sub BestMatch_TellsTwoSameNamedFilesInDifferentFoldersApart()
        ' Acceptance case 5 of Ф3: delete cover.jpg from two folders, press U twice, and
        ' each has to go home rather than both landing in the folder deleted from last.
        Dim first As New DateTime(2026, 8, 14, 12, 0, 0, DateTimeKind.Utc)
        Dim second As DateTime = first.AddSeconds(10)
        Dim records = {Record("C:\photos\a\cover.jpg", first, "$IFROMA.jpg"),
                       Record("C:\photos\b\cover.jpg", second, "$IFROMB.jpg")}

        Assert.Equal(Bin_Dir & "$RFROMB.jpg", RecycleBinIndex.BestMatch(records, "C:\photos\b\cover.jpg", second).DataPath)
        Assert.Equal(Bin_Dir & "$RFROMA.jpg", RecycleBinIndex.BestMatch(records, "C:\photos\a\cover.jpg", first).DataPath)
    End Sub

    <Fact>
    Public Sub BestMatch_ComparesPathsTheWayWindowsDoes()
        Dim ours As New DateTime(2026, 8, 14, 12, 0, 0, DateTimeKind.Utc)
        Dim records = {Record("C:\Photos\Cover.JPG", ours, "$IAAA1.jpg")}

        Assert.NotNull(RecycleBinIndex.BestMatch(records, "c:\photos\cover.jpg", ours))
    End Sub

    <Fact>
    Public Sub BestMatch_AllowsTheClockSlackButNotMore()
        Dim ours As New DateTime(2026, 8, 14, 12, 0, 0, DateTimeKind.Utc)
        Dim just_Inside = {Record("C:\photos\cover.jpg", ours.AddSeconds(-1), "$IAAA1.jpg")}
        Dim well_Outside = {Record("C:\photos\cover.jpg", ours.AddSeconds(-60), "$IAAA1.jpg")}

        Assert.NotNull(RecycleBinIndex.BestMatch(just_Inside, "C:\photos\cover.jpg", ours))
        Assert.Null(RecycleBinIndex.BestMatch(well_Outside, "C:\photos\cover.jpg", ours))
    End Sub

    <Fact>
    Public Sub BestMatch_SurvivesNothingInTheList()
        Dim ours As DateTime = DateTime.UtcNow
        Dim records = {CType(Nothing, RecycleBinRecord), Record("C:\photos\cover.jpg", ours, "$IAAA1.jpg")}

        Assert.NotNull(RecycleBinIndex.BestMatch(records, "C:\photos\cover.jpg", ours))
        Assert.Null(RecycleBinIndex.BestMatch(Nothing, "C:\photos\cover.jpg", ours))
        Assert.Null(RecycleBinIndex.BestMatch(records, "", ours))
    End Sub

End Class
#End If
