#If Not NETFRAMEWORK Then
Option Explicit On
Option Strict On
Option Infer On
Option Compare Binary

Imports System.Collections.Generic
Imports System.Globalization
Imports System.IO
Imports System.Linq
Imports System.Text

''' <summary>
''' Specification 169 §6.2: Writes and parses Windows Registry Editor Version 5.00 (.reg) files
''' for profile reset backup and restore.
''' </summary>
Public NotInheritable Class RegBackup

    Private Sub New()
    End Sub

    Public Const Header As String = "Windows Registry Editor Version 5.00"
    Public Const Section As String = "[HKEY_CURRENT_USER\Software\VB and VBA Program Settings\SZA\FastMediaSorter]"

    ''' <summary>Formats the given settings as .reg file text. Throws if any key is not in ProfileResetScope.ResetNames.</summary>
    Public Shared Function Format(values As IEnumerable(Of KeyValuePair(Of String, String))) As String
        If values Is Nothing Then Throw New ArgumentNullException(NameOf(values))

        Dim sb As New StringBuilder()
        sb.AppendLine(Header)
        sb.AppendLine()
        sb.AppendLine(Section)

        Dim sorted = values.OrderBy(Function(kvp) kvp.Key, StringComparer.OrdinalIgnoreCase).ToList()
        For Each kvp In sorted
            If Not ProfileResetScope.IsResetName(kvp.Key) Then
                Throw New ArgumentException("Disallowed key in reset backup: " & kvp.Key, NameOf(values))
            End If
            Dim val As String = If(kvp.Value, String.Empty)
            Dim keyEscaped As String = EscapeString(kvp.Key)

            If HasControlCharacters(val) Then
                Dim bytes As Byte() = Encoding.Unicode.GetBytes(val & ChrW(0))
                Dim hexString As String = String.Join(",", bytes.Select(Function(b) b.ToString("x2", CultureInfo.InvariantCulture)))
                sb.AppendLine("""" & keyEscaped & """=hex(1):" & hexString)
            Else
                Dim valEscaped As String = EscapeString(val)
                sb.AppendLine("""" & keyEscaped & """=""" & valEscaped & """")
            End If
        Next

        Return sb.ToString()
    End Function

    ''' <summary>Parses the key-value pairs from a .reg file string.</summary>
    Public Shared Function Parse(regText As String) As Dictionary(Of String, String)
        If regText Is Nothing Then Throw New ArgumentNullException(NameOf(regText))

        Dim result As New Dictionary(Of String, String)(StringComparer.OrdinalIgnoreCase)
        Using reader As New StringReader(regText)
            Dim line As String = reader.ReadLine()
            While line IsNot Nothing AndAlso String.IsNullOrWhiteSpace(line)
                line = reader.ReadLine()
            End While

            If line Is Nothing OrElse Not line.Trim().StartsWith(Header, StringComparison.Ordinal) Then
                Throw New FormatException("Invalid registry file: missing header.")
            End If

            Dim insideTargetSection As Boolean = False
            line = reader.ReadLine()
            While line IsNot Nothing
                Dim trimmed As String = line.Trim()
                If trimmed.StartsWith("[", StringComparison.Ordinal) AndAlso trimmed.EndsWith("]", StringComparison.Ordinal) Then
                    insideTargetSection = String.Equals(trimmed, Section, StringComparison.OrdinalIgnoreCase)
                    line = reader.ReadLine()
                    Continue While
                End If

                If Not insideTargetSection OrElse String.IsNullOrWhiteSpace(trimmed) OrElse trimmed.StartsWith(";", StringComparison.Ordinal) Then
                    line = reader.ReadLine()
                    Continue While
                End If

                ' Accumulate continuation lines if line ends with '\'
                Dim fullLine As New StringBuilder(trimmed)
                While fullLine.ToString().EndsWith("\", StringComparison.Ordinal)
                    fullLine.Length -= 1 ' remove '\'
                    Dim nextLine As String = reader.ReadLine()
                    If nextLine Is Nothing Then Exit While
                    fullLine.Append(nextLine.Trim())
                End While

                Dim entryText As String = fullLine.ToString().Trim()
                If entryText.StartsWith("""", StringComparison.Ordinal) Then
                    Dim eqIdx As Integer = FindAssignmentOperator(entryText)
                    If eqIdx > 0 Then
                        Dim keyPart As String = entryText.Substring(0, eqIdx).Trim()
                        Dim valPart As String = entryText.Substring(eqIdx + 1).Trim()
                        If keyPart.StartsWith("""", StringComparison.Ordinal) AndAlso keyPart.EndsWith("""", StringComparison.Ordinal) Then
                            Dim key As String = UnescapeString(keyPart.Substring(1, keyPart.Length - 2))
                            Dim val As String = ParseValue(valPart)
                            result(key) = val
                        End If
                    End If
                End If

                line = reader.ReadLine()
            End While
        End Using

        Return result
    End Function

    ''' <summary>Creates a .reg backup file under the specified directory with owner-only access, verified by read-back.</summary>
    Public Shared Function CreateBackup(values As IReadOnlyDictionary(Of String, String),
                                        Optional directory As String = Nothing,
                                        Optional openNew As Func(Of String, Stream) = Nothing,
                                        Optional readAll As Func(Of String, String) = Nothing,
                                        Optional nextName As Func(Of String) = Nothing,
                                        Optional protectDirectory As Action(Of String) = Nothing) As String
        If values Is Nothing Then Throw New ArgumentNullException(NameOf(values))
        Dim targetDir As String = If(directory, SettingsImportBackupStore.DefaultDirectory())
        If String.IsNullOrEmpty(targetDir) Then Throw New ArgumentException("No backup directory.", NameOf(directory))

        Dim protect As Action(Of String) = If(protectDirectory IsNot Nothing, protectDirectory, AddressOf SettingsImportBackupStore.EnsurePrivateDirectory)
        protect(targetDir)

        Dim open As Func(Of String, Stream) = If(openNew IsNot Nothing, openNew, AddressOf SettingsImportBackupStore.OpenExclusive)
        Dim read As Func(Of String, String) = If(readAll IsNot Nothing, readAll, Function(p As String) File.ReadAllText(p, Encoding.Unicode))

        Dim regText As String = Format(values)
        Dim timestamp As String = DateTime.UtcNow.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture)
        Dim name As Func(Of String) = If(nextName IsNot Nothing, nextName, Function() "profile-before-reset-" & timestamp & "-" & Guid.NewGuid().ToString("N") & ".reg")

        Dim createdPath As String = Nothing
        Dim stream As Stream = Nothing
        For attempt As Integer = 1 To SettingsImportBackupStore.MaxNameAttempts
            Dim leaf As String = name()
            If String.IsNullOrEmpty(leaf) OrElse Path.GetFileName(leaf) <> leaf Then Throw New IOException("Invalid backup name.")
            Dim candidate As String = Path.Combine(targetDir, leaf)
            Try
                stream = open(candidate)
                createdPath = candidate
                Exit For
            Catch ex As IOException When File.Exists(candidate)
                ' Candidate name taken; retry with new name
            End Try
        Next

        If createdPath Is Nothing Then Throw New IOException("A backup file name could not be reserved.")

        Try
            Using stream
                ' UTF-16 LE with BOM (standard Windows Unicode .reg file)
                Dim encoding As New UnicodeEncoding(bigEndian:=False, byteOrderMark:=True)
                Dim bytes As Byte() = encoding.GetBytes(regText)
                stream.Write(bytes, 0, bytes.Length)
                stream.Flush()
                If TypeOf stream Is FileStream Then DirectCast(stream, FileStream).Flush(True)
            End Using

            ' Read back and verify identical content
            Dim readBackContent As String = read(createdPath)
            Dim restored As Dictionary(Of String, String) = Parse(readBackContent)
            If restored.Count <> values.Count Then
                Throw New IOException("The backup read-back count did not match.")
            End If
            For Each kvp In values
                Dim restoredVal As String = Nothing
                If Not restored.TryGetValue(kvp.Key, restoredVal) OrElse Not String.Equals(restoredVal, kvp.Value, StringComparison.Ordinal) Then
                    Throw New IOException("The backup read-back value did not match for key: " & kvp.Key)
                End If
            Next

            Return createdPath
        Catch
            Try
                If File.Exists(createdPath) Then File.Delete(createdPath)
            Catch
            End Try
            Throw
        End Try
    End Function

    Private Shared Function HasControlCharacters(s As String) As Boolean
        If String.IsNullOrEmpty(s) Then Return False
        For Each c As Char In s
            If Char.IsControl(c) Then Return True
        Next
        Return False
    End Function

    Private Shared Function EscapeString(s As String) As String
        If String.IsNullOrEmpty(s) Then Return String.Empty
        Return s.Replace("\", "\\").Replace("""", "\""")
    End Function

    Private Shared Function UnescapeString(s As String) As String
        If String.IsNullOrEmpty(s) Then Return String.Empty
        Dim sb As New StringBuilder(s.Length)
        Dim i As Integer = 0
        While i < s.Length
            If s(i) = "\"c AndAlso i + 1 < s.Length Then
                Dim nxt As Char = s(i + 1)
                If nxt = "\"c OrElse nxt = """"c Then
                    sb.Append(nxt)
                    i += 2
                    Continue While
                End If
            End If
            sb.Append(s(i))
            i += 1
        End While
        Return sb.ToString()
    End Function

    Private Shared Function FindAssignmentOperator(line As String) As Integer
        Dim inQuotes As Boolean = False
        Dim escaped As Boolean = False
        For i As Integer = 0 To line.Length - 1
            Dim c As Char = line(i)
            If escaped Then
                escaped = False
            ElseIf c = "\"c Then
                escaped = True
            ElseIf c = """"c Then
                inQuotes = Not inQuotes
            ElseIf c = "="c AndAlso Not inQuotes Then
                Return i
            End If
        Next
        Return -1
    End Function

    Private Shared Function ParseValue(valPart As String) As String
        If valPart.StartsWith("""", StringComparison.Ordinal) Then
            Dim inner As String = valPart
            If inner.StartsWith("""", StringComparison.Ordinal) AndAlso inner.EndsWith("""", StringComparison.Ordinal) AndAlso inner.Length >= 2 Then
                inner = inner.Substring(1, inner.Length - 2)
            End If
            Return UnescapeString(inner)
        ElseIf valPart.StartsWith("hex(1):", StringComparison.OrdinalIgnoreCase) OrElse valPart.StartsWith("hex:", StringComparison.OrdinalIgnoreCase) Then
            Dim prefixLen As Integer = If(valPart.StartsWith("hex(1):", StringComparison.OrdinalIgnoreCase), 7, 4)
            Dim hexData As String = valPart.Substring(prefixLen).Trim()
            If String.IsNullOrEmpty(hexData) Then Return String.Empty
            Dim tokens As String() = hexData.Split(","c)
            Dim bytes As New List(Of Byte)()
            For Each tok In tokens
                Dim t As String = tok.Trim()
                If Not String.IsNullOrEmpty(t) Then
                    Dim b As Byte
                    If Byte.TryParse(t, NumberStyles.HexNumber, CultureInfo.InvariantCulture, b) Then
                        bytes.Add(b)
                    End If
                End If
            Next
            Dim decoded As String = Encoding.Unicode.GetString(bytes.ToArray())
            ' Remove trailing null terminator(s)
            Return decoded.TrimEnd(ChrW(0))
        ElseIf valPart.StartsWith("hex(2):", StringComparison.OrdinalIgnoreCase) Then
            Dim hexData As String = valPart.Substring(7).Trim()
            Dim tokens As String() = hexData.Split(","c)
            Dim bytes As New List(Of Byte)()
            For Each tok In tokens
                Dim t As String = tok.Trim()
                If Not String.IsNullOrEmpty(t) Then
                    Dim b As Byte
                    If Byte.TryParse(t, NumberStyles.HexNumber, CultureInfo.InvariantCulture, b) Then
                        bytes.Add(b)
                    End If
                End If
            Next
            Dim decoded As String = Encoding.Unicode.GetString(bytes.ToArray())
            Return decoded.TrimEnd(ChrW(0))
        ElseIf valPart.StartsWith("dword:", StringComparison.OrdinalIgnoreCase) Then
            Dim hexNum As String = valPart.Substring(6).Trim()
            Dim intVal As Integer
            If Integer.TryParse(hexNum, NumberStyles.HexNumber, CultureInfo.InvariantCulture, intVal) Then
                Return intVal.ToString(CultureInfo.InvariantCulture)
            End If
            Return hexNum
        End If
        Return valPart
    End Function

End Class
#End If
