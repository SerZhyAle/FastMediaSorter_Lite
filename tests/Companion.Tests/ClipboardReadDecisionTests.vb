Option Strict On

Imports System.Collections.Generic
Imports FastMediaSorterCompanion
Imports Xunit

''' <summary>
''' CLIPBOARD-GUARD 0.12 item S (L0327): the kind of a clipboard read is decided by the first format that is PRESENT, and the
''' decision is final. Proven on a fake clipboard through the pure seam <c>ClipboardWin32.ReadImageOrText</c>, no Win32 involved.
''' </summary>
Public Class ClipboardReadDecisionTests

    Private Const PngFormat As UInteger = &HC100UI

    Private NotInheritable Class FakeFormats
        Implements IClipboardFormatSource

        Private ReadOnly listed As New HashSet(Of UInteger)()
        Private ReadOnly blocks As New Dictionary(Of UInteger, Byte())()
        Public ReadOnly Reads As New List(Of UInteger)()

        ''' <summary>A listed format whose block can be read.</summary>
        Public Function Offer(format As UInteger, data As Byte()) As FakeFormats
            listed.Add(format)
            blocks(format) = data
            Return Me
        End Function

        ''' <summary>A listed format whose block cannot be read (GetClipboardData returns NULL).</summary>
        Public Function OfferUnreadable(format As UInteger) As FakeFormats
            listed.Add(format)
            Return Me
        End Function

        Public Function IsAvailable(format As UInteger) As Boolean Implements IClipboardFormatSource.IsAvailable
            Return listed.Contains(format)
        End Function

        Public Function Read(format As UInteger, ByRef tooLarge As Boolean) As Byte() Implements IClipboardFormatSource.Read
            Reads.Add(format)
            Dim data As Byte() = Nothing
            blocks.TryGetValue(format, data)
            Return data
        End Function
    End Class

    Private Shared ReadOnly PngBytes As Byte() = {&H89, &H50, &H4E, &H47, &HD, &HA, &H1A, &HA, 0, 0}
    Private Shared ReadOnly TextBytes As Byte() = {104, 0, 105, 0, 0, 0}

    ''' <summary>Case 1: a readable picture beside text is the picture, and the text is not even read.</summary>
    <Fact>
    Public Sub A_readable_picture_beside_text_is_the_picture_and_the_text_is_not_read()
        Dim fake As New FakeFormats()
        fake.Offer(PngFormat, PngBytes).Offer(ClipboardWin32.CF_UNICODETEXT, TextBytes)
        Dim png As Byte() = Nothing, dib As Byte() = Nothing, text As Byte() = Nothing
        Dim early = ClipboardWin32.ReadImageOrText(fake, PngFormat, png, dib, text)
        Assert.Null(early)
        Assert.NotNull(png)
        Assert.Null(text)
        Assert.DoesNotContain(ClipboardWin32.CF_UNICODETEXT, fake.Reads)
    End Sub

    ''' <summary>Case 2: a picture that is listed but cannot be read FAILS the read; the text beside it is never sent in its place.</summary>
    <Fact>
    Public Sub An_unreadable_picture_fails_and_never_falls_through_to_the_text()
        Dim fake As New FakeFormats()
        fake.OfferUnreadable(ClipboardWin32.CF_DIB).Offer(ClipboardWin32.CF_UNICODETEXT, TextBytes)
        Dim png As Byte() = Nothing, dib As Byte() = Nothing, text As Byte() = Nothing
        Dim early = ClipboardWin32.ReadImageOrText(fake, PngFormat, png, dib, text)
        Assert.NotNull(early)
        Assert.Equal(ClipboardReadKind.Failed, early.Kind)
        Assert.Null(text)
        Assert.DoesNotContain(ClipboardWin32.CF_UNICODETEXT, fake.Reads)
    End Sub

    ''' <summary>Case 3: no picture listed at all - the text is read.</summary>
    <Fact>
    Public Sub With_no_picture_listed_the_text_is_read()
        Dim fake As New FakeFormats()
        fake.Offer(ClipboardWin32.CF_UNICODETEXT, TextBytes)
        Dim png As Byte() = Nothing, dib As Byte() = Nothing, text As Byte() = Nothing
        Dim early = ClipboardWin32.ReadImageOrText(fake, PngFormat, png, dib, text)
        Assert.Null(early)
        Assert.Null(png)
        Assert.Null(dib)
        Assert.NotNull(text)
    End Sub

    ''' <summary>The picture kind keeps its own order inside: bytes that are not a PNG fall to the DIB behind them.</summary>
    <Fact>
    Public Sub Bytes_that_are_not_a_png_fall_to_the_dib_of_the_same_picture()
        Dim fake As New FakeFormats()
        fake.Offer(PngFormat, New Byte() {1, 2, 3}).Offer(ClipboardWin32.CF_DIB, New Byte() {9, 9, 9, 9})
        Dim png As Byte() = Nothing, dib As Byte() = Nothing, text As Byte() = Nothing
        Dim early = ClipboardWin32.ReadImageOrText(fake, PngFormat, png, dib, text)
        Assert.Null(early)
        Assert.Null(png)
        Assert.NotNull(dib)
    End Sub
End Class
