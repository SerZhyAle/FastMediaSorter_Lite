#If Not NETFRAMEWORK Then
Option Strict On

Imports System.IO
Imports System.Security.Cryptography
Imports System.Text

''' <summary>
''' What identifies one cached decode
''' (SPECIFICATION_DECODE_CACHE_AND_ANIMATION_TO_VIDEO_DOTNET10.md §4).
'''
''' Pure on purpose - no disk, no settings, no clock - because the single way this feature
''' can show WRONG pixels is a key that fails to change when the answer does. That is a
''' question for a test, not for whoever edits the string next.
'''
''' The shape mirrors <c>TranslationCache.BuildKey</c> deliberately: path, write time and
''' the settings that alter the CONTENT, hashed to a fixed-length, path-safe name.
''' </summary>
Friend Module DecodeCacheKey

    ''' <summary>Suffix of every cache entry, and the pattern DiskCacheTrim evicts by.
    ''' Distinctive so a stray file in that directory is recognisably ours.</summary>
    Friend Const File_Extension As String = ".fmsdec"

    Friend Const File_Pattern As String = "*" & File_Extension

    ''' <summary>
    ''' The key text. Write time AND size rather than a content hash: hashing a 200 MB file
    ''' to decide whether to skip decoding it is precisely the cost this feature removes.
    ''' A file edited in place keeps its path and gets a new pair, so the old entry is
    ''' orphaned and the trim collects it.
    '''
    ''' The path enters as given - two spellings of the same file cost two entries, which
    ''' is cheaper than normalising a UNC path wrong.
    ''' </summary>
    Friend Function Build(filePath As String,
                          lastWriteUtcTicks As Long,
                          lengthBytes As Long,
                          exifAutoRotate As Boolean,
                          formatVersion As Integer) As String
        Return String.Join("|",
                           If(filePath, String.Empty),
                           lastWriteUtcTicks.ToString(Globalization.CultureInfo.InvariantCulture),
                           lengthBytes.ToString(Globalization.CultureInfo.InvariantCulture),
                           If(exifAutoRotate, "1", "0"),
                           formatVersion.ToString(Globalization.CultureInfo.InvariantCulture))
    End Function

    ''' <summary>
    ''' <c>3f0a..d1-gif.fmsdec</c>. The kind is in the NAME so a hit knows what it holds
    ''' without reading a header and without a second file beside it.
    '''
    ''' SHA-1 over the key rather than the path itself: a fixed-length name that is always
    ''' legal on every file system, whatever the picture was called. A collision would
    ''' serve a wrong payload - SHA-1 over path + ticks + size makes that not-in-this-
    ''' universe, and even then the result is a decodable image, not a crash.
    ''' </summary>
    Friend Function FileNameFor(key As String, kind As DecodedPayloadKind) As String
        Return HashKey(key) & "-" & KindSuffix(kind) & File_Extension
    End Function

    Friend Function KindSuffix(kind As DecodedPayloadKind) As String
        Return If(kind = DecodedPayloadKind.Gif, "gif", "png")
    End Function

    Private Function HashKey(key As String) As String
        Using sha As SHA1 = SHA1.Create()
            Dim bytes As Byte() = sha.ComputeHash(Encoding.UTF8.GetBytes(If(key, String.Empty)))
            Dim builder As New StringBuilder(bytes.Length * 2)
            For Each b As Byte In bytes
                builder.Append(b.ToString("x2", Globalization.CultureInfo.InvariantCulture))
            Next
            Return builder.ToString()
        End Using
    End Function

End Module
#End If
