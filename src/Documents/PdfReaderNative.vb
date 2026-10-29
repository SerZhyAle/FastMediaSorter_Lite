#If Not NETFRAMEWORK Then
Option Explicit On
Option Strict On
Option Infer On
Option Compare Binary

Imports System
Imports System.Collections.Generic
Imports System.Drawing
Imports System.Runtime.InteropServices
Imports System.Text
Imports System.Threading

Public NotInheritable Class PdfTextPage
    Public Property Text As String = ""
    Public Property Boxes As New List(Of RectangleF)()
    Public Property Size As SizeF
    Public Property Rotation As Integer
    Public Property Links As New List(Of PdfReaderLink)()

    Friend Sub RotateClockwise()
        Dim height = Size.Height
        Dim turn As Func(Of RectangleF, RectangleF) = Function(rect) If(rect.IsEmpty, rect, New RectangleF(height - rect.Bottom, rect.Left, rect.Height, rect.Width))
        For index = 0 To Boxes.Count - 1
            Boxes(index) = turn(Boxes(index))
        Next
        ' Not "link": ResourceOrigin.Link is a module constant in scope, and For Each would bind to it.
        For Each pageLink In Links
            pageLink.Bounds = turn(pageLink.Bounds)
        Next
        Size = New SizeF(Size.Height, Size.Width)
        Rotation = (Rotation + 1) Mod 4
    End Sub
End Class

Public NotInheritable Class PdfReaderLink
    Public Property Bounds As RectangleF
    Public Property Page As Integer
    Public Property Uri As String = ""
End Class

Public NotInheritable Class PdfOutlineItem
    Public Property Title As String = ""
    Public Property Page As Integer
    Public Property Children As New List(Of PdfOutlineItem)()
End Class

Public NotInheritable Class PdfReaderInfo
    Public Property RightToLeft As Boolean
    Public Property CanCopy As Boolean
    Public Property CanReadAloud As Boolean
    Public Property Title As String = ""
    Public Property Author As String = ""
    Public Property HasForms As Boolean
    Public Property Outline As New List(Of PdfOutlineItem)()
End Class

' Only PdfDocumentSource calls this module, while holding its process-wide NativeGate.
' Docnet initializes PDFium first; no second initialization or library lifetime is introduced.
' The public PDFium C ABI uses 32-bit unsigned long on Windows, including x64.
Friend Module PdfReaderNative
    Private Const Library As String = "pdfium.dll"

    <DllImport(Library, CallingConvention:=CallingConvention.Cdecl)>
    Private Function FPDF_LoadDocument(<MarshalAs(UnmanagedType.LPUTF8Str)> path As String, <MarshalAs(UnmanagedType.LPUTF8Str)> password As String) As IntPtr
    End Function
    <DllImport(Library, CallingConvention:=CallingConvention.Cdecl)>
    Private Function FPDF_LoadMemDocument64(data As IntPtr, size As UIntPtr, <MarshalAs(UnmanagedType.LPUTF8Str)> password As String) As IntPtr
    End Function
    <DllImport(Library, CallingConvention:=CallingConvention.Cdecl)>
    Private Sub FPDF_CloseDocument(document As IntPtr)
    End Sub
    <DllImport(Library, CallingConvention:=CallingConvention.Cdecl)>
    Private Function FPDF_GetDocPermissions(document As IntPtr) As UInteger
    End Function
    <DllImport(Library, CallingConvention:=CallingConvention.Cdecl)>
    Private Function FPDF_GetFormType(document As IntPtr) As Integer
    End Function
    <DllImport(Library, CallingConvention:=CallingConvention.Cdecl)>
    Private Function FPDF_VIEWERREF_GetName(document As IntPtr, <MarshalAs(UnmanagedType.LPUTF8Str)> key As String, buffer As Byte(), length As UInteger) As UInteger
    End Function
    <DllImport(Library, CallingConvention:=CallingConvention.Cdecl)>
    Private Function FPDF_GetMetaText(document As IntPtr, <MarshalAs(UnmanagedType.LPStr)> tag As String, buffer As Byte(), size As UInteger) As UInteger
    End Function
    <DllImport(Library, CallingConvention:=CallingConvention.Cdecl)>
    Private Function FPDF_LoadPage(document As IntPtr, index As Integer) As IntPtr
    End Function
    <DllImport(Library, CallingConvention:=CallingConvention.Cdecl)>
    Private Sub FPDF_ClosePage(page As IntPtr)
    End Sub
    <DllImport(Library, CallingConvention:=CallingConvention.Cdecl)>
    Private Function FPDF_GetPageWidth(page As IntPtr) As Double
    End Function
    <DllImport(Library, CallingConvention:=CallingConvention.Cdecl)>
    Private Function FPDF_GetPageHeight(page As IntPtr) As Double
    End Function
    <DllImport(Library, CallingConvention:=CallingConvention.Cdecl)>
    Private Function FPDFPage_GetRotation(page As IntPtr) As Integer
    End Function
    <DllImport(Library, CallingConvention:=CallingConvention.Cdecl)>
    Private Function FPDF_PageToDevice(page As IntPtr, x As Integer, y As Integer, width As Integer, height As Integer, rotation As Integer,
                                       pageX As Double, pageY As Double, ByRef deviceX As Integer, ByRef deviceY As Integer) As Integer
    End Function
    <DllImport(Library, CallingConvention:=CallingConvention.Cdecl)>
    Private Function FPDFText_LoadPage(page As IntPtr) As IntPtr
    End Function
    <DllImport(Library, CallingConvention:=CallingConvention.Cdecl)>
    Private Sub FPDFText_ClosePage(text As IntPtr)
    End Sub
    <DllImport(Library, CallingConvention:=CallingConvention.Cdecl)>
    Private Function FPDFText_CountChars(text As IntPtr) As Integer
    End Function
    <DllImport(Library, CallingConvention:=CallingConvention.Cdecl)>
    Private Function FPDFText_GetUnicode(text As IntPtr, index As Integer) As UInteger
    End Function
    <DllImport(Library, CallingConvention:=CallingConvention.Cdecl)>
    Private Function FPDFText_GetCharBox(text As IntPtr, index As Integer, ByRef left As Double, ByRef right As Double, ByRef bottom As Double, ByRef top As Double) As Integer
    End Function
    <DllImport(Library, CallingConvention:=CallingConvention.Cdecl)>
    Private Function FPDFBookmark_GetFirstChild(document As IntPtr, bookmark As IntPtr) As IntPtr
    End Function
    <DllImport(Library, CallingConvention:=CallingConvention.Cdecl)>
    Private Function FPDFBookmark_GetNextSibling(document As IntPtr, bookmark As IntPtr) As IntPtr
    End Function
    <DllImport(Library, CallingConvention:=CallingConvention.Cdecl)>
    Private Function FPDFBookmark_GetTitle(bookmark As IntPtr, buffer As Byte(), length As UInteger) As UInteger
    End Function
    <DllImport(Library, CallingConvention:=CallingConvention.Cdecl)>
    Private Function FPDFBookmark_GetDest(document As IntPtr, bookmark As IntPtr) As IntPtr
    End Function
    <DllImport(Library, CallingConvention:=CallingConvention.Cdecl)>
    Private Function FPDFBookmark_GetAction(bookmark As IntPtr) As IntPtr
    End Function
    <DllImport(Library, CallingConvention:=CallingConvention.Cdecl)>
    Private Function FPDFDest_GetDestPageIndex(document As IntPtr, destination As IntPtr) As Integer
    End Function
    <DllImport(Library, CallingConvention:=CallingConvention.Cdecl)>
    Private Function FPDFAction_GetType(action As IntPtr) As UInteger
    End Function
    <DllImport(Library, CallingConvention:=CallingConvention.Cdecl)>
    Private Function FPDFAction_GetDest(document As IntPtr, action As IntPtr) As IntPtr
    End Function
    <DllImport(Library, CallingConvention:=CallingConvention.Cdecl)>
    Private Function FPDFAction_GetURIPath(document As IntPtr, action As IntPtr, buffer As Byte(), length As UInteger) As UInteger
    End Function
    <DllImport(Library, CallingConvention:=CallingConvention.Cdecl)>
    Private Function FPDFLink_Enumerate(page As IntPtr, ByRef position As Integer, ByRef link As IntPtr) As Integer
    End Function
    <StructLayout(LayoutKind.Sequential)>
    Private Structure PdfRect
        Public Left As Single
        Public Top As Single
        Public Right As Single
        Public Bottom As Single
    End Structure
    <DllImport(Library, CallingConvention:=CallingConvention.Cdecl)>
    Private Function FPDFLink_GetAnnotRect(link As IntPtr, ByRef rect As PdfRect) As Integer
    End Function
    <DllImport(Library, CallingConvention:=CallingConvention.Cdecl)>
    Private Function FPDFLink_GetDest(document As IntPtr, link As IntPtr) As IntPtr
    End Function
    <DllImport(Library, CallingConvention:=CallingConvention.Cdecl)>
    Private Function FPDFLink_GetAction(link As IntPtr) As IntPtr
    End Function

    Friend Function WithDocument(Of T)(path As String, bytes As Byte(), password As String, read As Func(Of IntPtr, T)) As T
        Dim pinned As GCHandle
        Dim document As IntPtr
        Try
            If bytes Is Nothing Then
                document = FPDF_LoadDocument(path, password)
            Else
                pinned = GCHandle.Alloc(bytes, GCHandleType.Pinned)
                document = FPDF_LoadMemDocument64(pinned.AddrOfPinnedObject(), New UIntPtr(CULng(bytes.LongLength)), password)
            End If
            If document = IntPtr.Zero Then Throw New IO.InvalidDataException("PDFium could not reopen the document.")
            Return read(document)
        Finally
            If document <> IntPtr.Zero Then FPDF_CloseDocument(document)
            If pinned.IsAllocated Then pinned.Free()
        End Try
    End Function

    Friend Function ReadInfo(document As IntPtr, token As CancellationToken) As PdfReaderInfo
        Dim permissions = FPDF_GetDocPermissions(document)
        Dim result As New PdfReaderInfo With {.CanCopy = (permissions And &H10UI) <> 0,
            .CanReadAloud = (permissions And &H210UI) <> 0, .HasForms = FPDF_GetFormType(document) <> 0}
        Dim buffer(8191) As Byte
        Dim direction(15) As Byte
        Dim directionLength = FPDF_VIEWERREF_GetName(document, "Direction", direction, CUInt(direction.Length))
        result.RightToLeft = directionLength > 0 AndAlso directionLength <= direction.Length AndAlso Encoding.ASCII.GetString(direction).TrimEnd(ChrW(0)) = "R2L"
        Dim count = FPDF_GetMetaText(document, "Title", buffer, CUInt(buffer.Length))
        If count > 0 AndAlso count <= buffer.Length Then result.Title = Encoding.Unicode.GetString(buffer, 0, CInt(count)).TrimEnd(ChrW(0))
        count = FPDF_GetMetaText(document, "Author", buffer, CUInt(buffer.Length))
        If count > 0 AndAlso count <= buffer.Length Then result.Author = Encoding.Unicode.GetString(buffer, 0, CInt(count)).TrimEnd(ChrW(0))
        Dim visited As New HashSet(Of IntPtr)()
        result.Outline = ReadOutline(document, IntPtr.Zero, visited, 0, token)
        Return result
    End Function

    Private Function ReadOutline(document As IntPtr, parent As IntPtr, visited As HashSet(Of IntPtr), depth As Integer, token As CancellationToken) As List(Of PdfOutlineItem)
        Dim result As New List(Of PdfOutlineItem)()
        If depth >= 32 Then Return result
        Dim item = FPDFBookmark_GetFirstChild(document, parent)
        While item <> IntPtr.Zero AndAlso visited.Count < 5000 AndAlso visited.Add(item)
            token.ThrowIfCancellationRequested()
            Dim buffer(8191) As Byte
            Dim count = FPDFBookmark_GetTitle(item, buffer, CUInt(buffer.Length))
            Dim destination = FPDFBookmark_GetDest(document, item)
            Dim action = FPDFBookmark_GetAction(item)
            If destination = IntPtr.Zero AndAlso action <> IntPtr.Zero AndAlso FPDFAction_GetType(action) = 1UI Then destination = FPDFAction_GetDest(document, action)
            result.Add(New PdfOutlineItem With {.Title = If(count > 0 AndAlso count <= buffer.Length, Encoding.Unicode.GetString(buffer, 0, CInt(Math.Min(count, CUInt(buffer.Length)))).TrimEnd(ChrW(0)), ""),
                .Page = If(destination = IntPtr.Zero, 0, FPDFDest_GetDestPageIndex(document, destination) + 1),
                .Children = ReadOutline(document, item, visited, depth + 1, token)})
            item = FPDFBookmark_GetNextSibling(document, item)
        End While
        Return result
    End Function

    Private Function DeviceRect(page As IntPtr, size As SizeF, left As Double, top As Double, right As Double, bottom As Double) As RectangleF
        Dim x1, x2, y1, y2 As Integer
        FPDF_PageToDevice(page, 0, 0, CInt(size.Width), CInt(size.Height), 0, left, top, x1, y1)
        FPDF_PageToDevice(page, 0, 0, CInt(size.Width), CInt(size.Height), 0, right, bottom, x2, y2)
        Return RectangleF.FromLTRB(Math.Min(x1, x2), Math.Min(y1, y2), Math.Max(x1, x2), Math.Max(y1, y2))
    End Function

    Friend Function ReadPage(document As IntPtr, number As Integer, token As CancellationToken, Optional includeText As Boolean = True) As PdfTextPage
        Dim page = FPDF_LoadPage(document, number - 1)
        If page = IntPtr.Zero Then Throw New IO.InvalidDataException("PDF page is damaged.")
        Dim text As IntPtr
        Try
            Dim result As New PdfTextPage With {.Size = New SizeF(CSng(FPDF_GetPageWidth(page)), CSng(FPDF_GetPageHeight(page))), .Rotation = FPDFPage_GetRotation(page)}
            If result.Size.Width <= 0 OrElse result.Size.Height <= 0 OrElse result.Size.Width > 1000000 OrElse result.Size.Height > 1000000 Then Throw New IO.InvalidDataException("Invalid PDF page geometry.")
            If includeText Then text = FPDFText_LoadPage(page)
            If text <> IntPtr.Zero Then
                Dim count = FPDFText_CountChars(text)
                If count > 250000 Then Throw New IO.InvalidDataException("PDF page text exceeds the reader limit.")
                Dim value As New StringBuilder()
                For index = 0 To count - 1
                    If index Mod 256 = 0 Then token.ThrowIfCancellationRequested()
                    Dim code = FPDFText_GetUnicode(text, index)
                    Dim character = If(code = 0 OrElse code > &H10FFFFUI OrElse (code >= &HD800UI AndAlso code <= &HDFFFUI), ChrW(&HFFFD).ToString(), Char.ConvertFromUtf32(CInt(code)))
                    Dim left, right, bottom, top As Double
                    Dim box = RectangleF.Empty
                    If FPDFText_GetCharBox(text, index, left, right, bottom, top) <> 0 Then box = DeviceRect(page, result.Size, left, top, right, bottom)
                    value.Append(character)
                    For Each unit As Char In character
                        result.Boxes.Add(box)
                    Next
                Next
                result.Text = value.ToString()
            End If
            Dim position As Integer
            Dim link As IntPtr
            Dim inspected As Integer
            While includeText AndAlso inspected < 5000 AndAlso FPDFLink_Enumerate(page, position, link) <> 0
                inspected += 1
                token.ThrowIfCancellationRequested()
                Dim rect As PdfRect
                If FPDFLink_GetAnnotRect(link, rect) = 0 Then Continue While
                Dim target As New PdfReaderLink With {.Bounds = DeviceRect(page, result.Size, rect.Left, rect.Top, rect.Right, rect.Bottom)}
                Dim destination = FPDFLink_GetDest(document, link)
                Dim action = FPDFLink_GetAction(link)
                If destination = IntPtr.Zero AndAlso action <> IntPtr.Zero AndAlso FPDFAction_GetType(action) = 1UI Then destination = FPDFAction_GetDest(document, action)
                If destination <> IntPtr.Zero Then target.Page = FPDFDest_GetDestPageIndex(document, destination) + 1
                If action <> IntPtr.Zero AndAlso FPDFAction_GetType(action) = 3UI Then
                    Dim buffer(8191) As Byte
                    Dim length = FPDFAction_GetURIPath(document, action, buffer, CUInt(buffer.Length))
                    If length > 0 AndAlso length <= buffer.Length Then target.Uri = Encoding.UTF8.GetString(buffer, 0, CInt(length)).TrimEnd(ChrW(0))
                End If
                If target.Page > 0 OrElse target.Uri.Length > 0 Then result.Links.Add(target)
            End While
            Return result
        Finally
            If text <> IntPtr.Zero Then FPDFText_ClosePage(text)
            FPDF_ClosePage(page)
        End Try
    End Function
End Module
#End If
