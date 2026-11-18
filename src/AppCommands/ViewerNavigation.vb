Option Strict On
Option Explicit On

Imports System.Collections.Generic
Imports System.Globalization
Imports System.IO

' The pure halves of the viewer's drive-and-observe verbs (L0318): the listing arithmetic of the six
' "file" verbs, the bound of the slideshow interval, where a screenshot goes and what its name is
' (CAPTURE-OUTPUT rules 3 and 5, with the answer of the CyrFlip proposal item 5: a path a program gives
' is used exactly as given), and the PNG time chunk (CAPTURE-OUTPUT rule 16). No window, no disk read
' that the caller did not hand in as a delegate - linked into Lite.Tests and compiled by the generator.
Namespace AppCommands

    Friend Enum NavigationMove
        StepForward
        StepBack
        ToFirst
        ToLast
        ToNumber
    End Enum

    Friend Enum NavigationOutcome
        ''' <summary>The window is asked to go to <see cref="NavigationPlan.Target"/> (or, when it is -1, where its own key path decides).</summary>
        Move
        ''' <summary>Already there: nothing to ask the window, the answer is the place it holds.</summary>
        Stay
        ''' <summary>The window shows no file of a listing.</summary>
        NothingShown
        ''' <summary><c>file goto</c> named a place outside the listing.</summary>
        OutOfRange
    End Enum

    Friend NotInheritable Class NavigationPlan
        Friend Property Outcome As NavigationOutcome
        ''' <summary>The place to go to, counted from 0; -1 when the window's own key path works it out (a folder it has not listed yet).</summary>
        Friend Property Target As Integer = -1
    End Class

    Friend Module ViewerNavigation

        ''' <summary>The pause of the slideshow is kept between these bounds by the settings reader (SlideshowIntervalMs, 1 s to 2 min);
        ''' a command may ask for anything the person could have saved, no more.</summary>
        Friend Const MinSlideshowIntervalMs As Integer = 1000
        Friend Const MaxSlideshowIntervalMs As Integer = 120000

        Friend Function IsSlideshowIntervalInRange(milliseconds As Integer) As Boolean
            Return milliseconds >= MinSlideshowIntervalMs AndAlso milliseconds <= MaxSlideshowIntervalMs
        End Function

        ''' <summary>Which move a <c>file ..</c> verb is. False for <c>file current</c> (it moves nothing) and for any other verb.</summary>
        Friend Function TryMoveOfVerb(verb As String, ByRef move As NavigationMove) As Boolean
            Select Case verb
                Case "file next" : move = NavigationMove.StepForward
                Case "file previous" : move = NavigationMove.StepBack
                Case "file first" : move = NavigationMove.ToFirst
                Case "file last" : move = NavigationMove.ToLast
                Case "file goto" : move = NavigationMove.ToNumber
                Case Else : Return False
            End Select
            Return True
        End Function

        ''' <summary>
        ''' What a move does to a listing of <paramref name="count"/> files with the place <paramref name="current"/> (from 0)
        ''' shown. At either end <c>file next</c> and <c>file previous</c> stay (a script walking a folder can see the end; the
        ''' arrow key's wrap to the other end is not offered). <paramref name="number"/> is counted from 1 and read only by
        ''' <see cref="NavigationMove.ToNumber"/>. When <paramref name="listLoaded"/> is False the window holds only the one file
        ''' a launch opened and its first flip lists the folder: a step is then handed to the window's own key path, which
        ''' does that, and the end of a list that is not there yet is not judged.
        ''' </summary>
        Friend Function Plan(move As NavigationMove, current As Integer, count As Integer, number As Integer,
                             listLoaded As Boolean) As NavigationPlan
            If count <= 0 OrElse current < 0 OrElse current >= count Then
                Return New NavigationPlan With {.Outcome = NavigationOutcome.NothingShown}
            End If
            Dim target As Integer
            Select Case move
                Case NavigationMove.ToNumber
                    If number < 1 OrElse number > count Then Return New NavigationPlan With {.Outcome = NavigationOutcome.OutOfRange}
                    target = number - 1
                Case NavigationMove.ToFirst
                    target = 0
                Case NavigationMove.ToLast
                    target = count - 1
                Case NavigationMove.StepForward
                    If Not listLoaded Then Return New NavigationPlan With {.Outcome = NavigationOutcome.Move, .Target = -1}
                    target = If(current >= count - 1, current, current + 1)
                Case Else
                    If Not listLoaded Then Return New NavigationPlan With {.Outcome = NavigationOutcome.Move, .Target = -1}
                    target = If(current <= 0, current, current - 1)
            End Select
            Return New NavigationPlan With {.Outcome = If(target = current, NavigationOutcome.Stay, NavigationOutcome.Move), .Target = target}
        End Function

        ''' <summary>The one kind word <c>file current</c> adds to the ones <c>open</c> already names (the Kind constants of
        ''' OpenTargets): a file the text reader shows.</summary>
        Friend Const KindText As String = "text"

        ''' <summary>A disk-full failure of a file write: the Win32 codes ERROR_HANDLE_DISK_FULL (39) and ERROR_DISK_FULL (112) in the
        ''' low word of an <c>IOException</c> HRESULT - the code, never the message (exit 13).</summary>
        Friend Function IsDiskFullHResult(hresult As Integer) As Boolean
            Dim code As Integer = hresult And &HFFFF
            Return code = 39 OrElse code = 112
        End Function

        ' --- screenshot window -----------------------------------------------------------------------

        ''' <summary>CAPTURE-OUTPUT rule 3: <c>screenshot_&lt;yyMMdd&gt;_&lt;HHmmss&gt;.png</c> from the local wall-clock second the capture
        ''' started, ASCII digits, and rule 5's " (2)", " (3).." before the extension while the name is taken in the folder.</summary>
        Friend Function ScreenshotName(captured As DateTime, existsInFolder As Func(Of String, Boolean)) As String
            Dim stem As String = "screenshot_" & captured.ToString("yyMMdd_HHmmss", CultureInfo.InvariantCulture)
            Dim name As String = stem & ".png"
            Dim n As Integer = 2
            While existsInFolder IsNot Nothing AndAlso existsInFolder(name) AndAlso n < 10000
                name = stem & " (" & n.ToString(CultureInfo.InvariantCulture) & ").png"
                n += 1
            End While
            Return name
        End Function

        ''' <summary>
        ''' Where <c>--out</c> points, for a caller whose working folder is <paramref name="workingDir"/>. A path a program gives is
        ''' used exactly as given - no character replaced, no ordinal added; the caller refuses an existing file without
        ''' <c>--overwrite</c>. A folder (an existing directory, or a text that ends in a separator) is not a file name: the file inside
        ''' it is named by <see cref="ScreenshotName"/>. Nothing when the text is not a local path.
        ''' </summary>
        Friend Function ResolveScreenshotTarget(outArgument As String, workingDir As String, captured As DateTime,
                                                isDirectory As Func(Of String, Boolean), fileExists As Func(Of String, Boolean)) As String
            If String.IsNullOrEmpty(outArgument) OrElse outArgument.IndexOf("://", StringComparison.Ordinal) >= 0 Then Return Nothing
            Dim full As String
            Try
                full = If(String.IsNullOrEmpty(workingDir), Path.GetFullPath(outArgument), Path.GetFullPath(Path.Combine(workingDir, outArgument)))
            Catch ex As ArgumentException
                Return Nothing
            Catch ex As NotSupportedException
                Return Nothing
            Catch ex As PathTooLongException
                Return Nothing
            End Try
            Dim last As Char = outArgument(outArgument.Length - 1)
            Dim endsInSeparator As Boolean = last = "\"c OrElse last = "/"c
            If endsInSeparator OrElse (isDirectory IsNot Nothing AndAlso isDirectory(full)) Then
                Dim folder As String = full.TrimEnd("\"c, "/"c)
                If folder.Length = 2 AndAlso folder(1) = ":"c Then folder &= "\"
                Return Path.Combine(folder, ScreenshotName(captured, Function(name As String) fileExists IsNot Nothing AndAlso fileExists(Path.Combine(folder, name))))
            End If
            Return full
        End Function

    End Module

    ''' <summary>
    ''' The PNG <c>tIME</c> chunk (CAPTURE-OUTPUT rule 16, 0.5): 7 bytes of data inserted after <c>IHDR</c>, holding the local
    ''' wall-clock second the capture started - the one its automatic name is formed from, not a UTC conversion.
    ''' </summary>
    Friend Module PngTimeChunk

        Private ReadOnly Signature As Byte() = {&H89, &H50, &H4E, &H47, &HD, &HA, &H1A, &HA}
        ' 8 signature bytes, then IHDR: 4 length + 4 type + 13 data + 4 crc.
        Private Const AfterHeader As Integer = 8 + 4 + 4 + 13 + 4
        Private ReadOnly CrcTable As UInteger() = BuildCrcTable()

        Private Function BuildCrcTable() As UInteger()
            Dim table(255) As UInteger
            For n As Integer = 0 To 255
                Dim c As UInteger = CUInt(n)
                For k As Integer = 0 To 7
                    If (c And 1UI) <> 0UI Then
                        c = &HEDB88320UI Xor (c >> 1)
                    Else
                        c = c >> 1
                    End If
                Next
                table(n) = c
            Next
            Return table
        End Function

        Friend Function Crc32(data As Byte(), offset As Integer, length As Integer) As UInteger
            Dim c As UInteger = &HFFFFFFFFUI
            For i As Integer = offset To offset + length - 1
                c = CrcTable(CInt((c Xor data(i)) And &HFFUI)) Xor (c >> 8)
            Next
            Return c Xor &HFFFFFFFFUI
        End Function

        Private Function IsPng(png As Byte()) As Boolean
            If png Is Nothing OrElse png.Length < AfterHeader Then Return False
            For i As Integer = 0 To Signature.Length - 1
                If png(i) <> Signature(i) Then Return False
            Next
            Return png(12) = &H49 AndAlso png(13) = &H48 AndAlso png(14) = &H44 AndAlso png(15) = &H52
        End Function

        ''' <summary>The same PNG with a <c>tIME</c> chunk right after <c>IHDR</c>; the bytes unchanged when it is not a PNG
        ''' or already carries one.</summary>
        Friend Function Insert(png As Byte(), localTime As DateTime) As Byte()
            If Not IsPng(png) Then Return png
            Dim dummy As DateTime
            If TryRead(png, dummy) Then Return png
            Dim chunk(4 + 4 + 7 + 4 - 1) As Byte
            chunk(3) = 7
            chunk(4) = &H74 ' t
            chunk(5) = &H49 ' I
            chunk(6) = &H4D ' M
            chunk(7) = &H45 ' E
            chunk(8) = CByte((localTime.Year >> 8) And &HFF)
            chunk(9) = CByte(localTime.Year And &HFF)
            chunk(10) = CByte(localTime.Month)
            chunk(11) = CByte(localTime.Day)
            chunk(12) = CByte(localTime.Hour)
            chunk(13) = CByte(localTime.Minute)
            chunk(14) = CByte(localTime.Second)
            Dim crc As UInteger = Crc32(chunk, 4, 4 + 7)
            chunk(15) = CByte((crc >> 24) And &HFFUI)
            chunk(16) = CByte((crc >> 16) And &HFFUI)
            chunk(17) = CByte((crc >> 8) And &HFFUI)
            chunk(18) = CByte(crc And &HFFUI)
            Dim result(png.Length + chunk.Length - 1) As Byte
            Buffer.BlockCopy(png, 0, result, 0, AfterHeader)
            Buffer.BlockCopy(chunk, 0, result, AfterHeader, chunk.Length)
            Buffer.BlockCopy(png, AfterHeader, result, AfterHeader + chunk.Length, png.Length - AfterHeader)
            Return result
        End Function

        ''' <summary>The time the first <c>tIME</c> chunk holds; False when there is none or the file is not a PNG.</summary>
        Friend Function TryRead(png As Byte(), ByRef localTime As DateTime) As Boolean
            localTime = DateTime.MinValue
            If Not IsPng(png) Then Return False
            Dim p As Integer = 8
            While p + 12 <= png.Length
                Dim length As Long = (CLng(png(p)) << 24) Or (CLng(png(p + 1)) << 16) Or (CLng(png(p + 2)) << 8) Or CLng(png(p + 3))
                If length < 0 OrElse p + 12 + length > png.Length Then Return False
                If png(p + 4) = &H74 AndAlso png(p + 5) = &H49 AndAlso png(p + 6) = &H4D AndAlso png(p + 7) = &H45 Then
                    If length <> 7 Then Return False
                    Try
                        localTime = New DateTime((CInt(png(p + 8)) << 8) Or CInt(png(p + 9)), png(p + 10), png(p + 11), png(p + 12), png(p + 13), png(p + 14))
                        Return True
                    Catch ex As ArgumentOutOfRangeException
                        Return False
                    End Try
                End If
                p += CInt(12 + length)
            End While
            Return False
        End Function

    End Module

End Namespace
