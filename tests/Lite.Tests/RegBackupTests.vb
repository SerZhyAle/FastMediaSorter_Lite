#If Not NETFRAMEWORK Then
Option Explicit On
Option Strict On
Option Infer On
Option Compare Binary

Imports System.Collections.Generic
Imports Xunit

''' <summary>
''' Specification 169 §8: Tests for RegBackup formatting, parsing, escaping and scope validation.
''' </summary>
Public Class RegBackupTests

    <Fact>
    Public Sub Header_and_section_name_are_exact()
        Assert.Equal("Windows Registry Editor Version 5.00", RegBackup.Header)
        Assert.Equal("[HKEY_CURRENT_USER\Software\VB and VBA Program Settings\SZA\FastMediaSorter]", RegBackup.Section)

        Dim sample As New Dictionary(Of String, String) From {
            {"chkTopMost", "1"}
        }
        Dim formatted As String = RegBackup.Format(sample)
        Assert.StartsWith("Windows Registry Editor Version 5.00", formatted)
        Assert.Contains("[HKEY_CURRENT_USER\Software\VB and VBA Program Settings\SZA\FastMediaSorter]", formatted)
    End Sub

    <Fact>
    Public Sub Round_trip_through_reader_preserves_special_characters()
        Dim original As New Dictionary(Of String, String) From {
            {"MoveOn1", "C:\My\Photos\Test\Folder"},
            {"MoveOn2", "Photo with ""quotes"" and \slashes\"},
            {"MoveOn3", "Multi" & vbCrLf & "Line" & vbLf & "Value"},
            {"MoveOn4", ""},
            {"MoveOn5", "P:\Музыка\Новая папка\中文\عربي"}
        }

        Dim formatted As String = RegBackup.Format(original)
        Dim parsed As Dictionary(Of String, String) = RegBackup.Parse(formatted)

        Assert.Equal(original.Count, parsed.Count)
        For Each kvp In original
            Assert.True(parsed.ContainsKey(kvp.Key), "Parsed missing key: " & kvp.Key)
            Assert.Equal(kvp.Value, parsed(kvp.Key))
        Next
    End Sub

    <Fact>
    Public Sub No_secret_value_can_be_passed_in()
        ' The scope table is the only input: any unapproved key (secret or non-reset) throws ArgumentException
        Dim secretAttempt As New Dictionary(Of String, String) From {
            {"TranslateApiKey", "secret-key-12345"}
        }
        Assert.Throws(Of ArgumentException)(Sub() RegBackup.Format(secretAttempt))

        Dim foreignAttempt As New Dictionary(Of String, String) From {
            {"Share_ListenPort", "8080"}
        }
        Assert.Throws(Of ArgumentException)(Sub() RegBackup.Format(foreignAttempt))

        Dim historyAttempt As New Dictionary(Of String, String) From {
            {"RecentFolders", "P:\Folder"}
        }
        Assert.Throws(Of ArgumentException)(Sub() RegBackup.Format(historyAttempt))
    End Sub

End Class
#End If
