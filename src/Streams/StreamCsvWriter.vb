#If Not NETFRAMEWORK Then
Option Strict On
Option Explicit On

Imports System
Imports System.Collections.Generic
Imports System.IO
Imports System.Text

Namespace Streams

    ''' <summary>
    ''' Writes and reads canonical RFC-4180 CSV files under STREAM-BANK 2.1 specifications.
    ''' Output is UTF-8 without BOM, every field double-quoted, CRLF line endings.
    ''' </summary>
    Friend Module StreamCsvWriter

        Public ReadOnly Utf8NoBom As New UTF8Encoding(False)

        ''' <summary>
        ''' Formats a single CSV field with RFC-4180 double-quoting and quote-escaping.
        ''' </summary>
        Public Function EscapeField(value As String) As String
            If value Is Nothing Then value = String.Empty
            Dim escaped = value.Replace("""", """""")
            Return """" & escaped & """"
        End Function

        ''' <summary>
        ''' Writes the collection of stream rows to a CSV stream in STREAM-BANK 2.1 producer order.
        ''' </summary>
        Public Sub WriteCsv(rows As IEnumerable(Of StreamCatalogRow), destinationStream As Stream)
            Using writer As New StreamWriter(destinationStream, Utf8NoBom, 4096, leaveOpen:=True)
                writer.NewLine = vbCrLf

                ' Header row
                Dim headerFields As New List(Of String)()
                For Each col In StreamCatalogSchema.ProducerColumns
                    headerFields.Add(EscapeField(col))
                Next
                writer.WriteLine(String.Join(",", headerFields))

                ' Data rows
                For Each row In rows
                    Dim rowFields As New List(Of String)()
                    For Each col In StreamCatalogSchema.ProducerColumns
                        Dim val = row.GetFieldValue(col)
                        rowFields.Add(EscapeField(val))
                    Next
                    writer.WriteLine(String.Join(",", rowFields))
                Next

                writer.Flush()
            End Using
        End Sub

        ''' <summary>
        ''' Parses an RFC-4180 CSV stream into a list of field arrays.
        ''' Handles multiline quoted fields, escaped quotes, and empty fields.
        ''' </summary>
        Public Function ParseRfc4180(csvStream As Stream, ByRef hasBom As Boolean) As List(Of List(Of String))
            hasBom = False
            Dim result As New List(Of List(Of String))()

            ' Check BOM
            Dim pos = csvStream.Position
            Dim bomCheck(2) As Byte
            Dim bytesRead = csvStream.Read(bomCheck, 0, 3)
            If bytesRead = 3 AndAlso bomCheck(0) = &HEF AndAlso bomCheck(1) = &HBB AndAlso bomCheck(2) = &HBF Then
                hasBom = True
            Else
                csvStream.Position = pos
            End If

            Using reader As New StreamReader(csvStream, Encoding.UTF8, detectEncodingFromByteOrderMarks:=False, bufferSize:=4096, leaveOpen:=True)
                Dim currentRow As New List(Of String)()
                Dim currentField As New StringBuilder()
                Dim inQuotes As Boolean = False
                Dim prevChar As Integer = -1

                While True
                    Dim charInt As Integer = reader.Read()
                    If charInt = -1 Then
                        ' End of stream
                        If inQuotes Then
                            ' Unclosed quotes at EOF - still append field
                            currentRow.Add(currentField.ToString())
                            result.Add(currentRow)
                        ElseIf currentRow.Count > 0 OrElse currentField.Length > 0 Then
                            currentRow.Add(currentField.ToString())
                            result.Add(currentRow)
                        End If
                        Exit While
                    End If

                    Dim ch As Char = ChrW(charInt)

                    If inQuotes Then
                        If ch = """"c Then
                            Dim nextInt = reader.Peek()
                            If nextInt = AscW(""""c) Then
                                ' Escaped quote ("")
                                reader.Read()
                                currentField.Append(""""c)
                            Else
                                ' Closing quote
                                inQuotes = False
                            End If
                        Else
                            currentField.Append(ch)
                        End If
                    Else
                        If ch = """"c Then
                            inQuotes = True
                        ElseIf ch = ","c Then
                            currentRow.Add(currentField.ToString())
                            currentField.Clear()
                        ElseIf ch = ControlChars.Cr Then
                            ' Lookahead for Lf
                            Dim nextInt = reader.Peek()
                            If nextInt = AscW(ControlChars.Lf) Then
                                reader.Read()
                            End If
                            currentRow.Add(currentField.ToString())
                            currentField.Clear()
                            result.Add(currentRow)
                            currentRow = New List(Of String)()
                        ElseIf ch = ControlChars.Lf Then
                            currentRow.Add(currentField.ToString())
                            currentField.Clear()
                            result.Add(currentRow)
                            currentRow = New List(Of String)()
                        Else
                            currentField.Append(ch)
                        End If
                    End If

                    prevChar = charInt
                End While
            End Using

            Return result
        End Function

    End Module

End Namespace
#End If
