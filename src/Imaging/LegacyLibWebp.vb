#If NETFRAMEWORK Then
Option Strict On

Imports System.Drawing
Imports System.Drawing.Imaging
Imports System.IO
Imports System.Runtime.InteropServices

''' <summary>
''' The x86 fallback's own WEBP decoder for the machines whose WIC has no WebP codec (Windows
''' 7/8.1, specification 270 R9): libwebp's decoder library (BSD-3-Clause, built from the pinned
''' upstream tag by tools/Build-LibWebpDecoder.ps1, which also proves it imports nothing beyond
''' Windows 7's KERNEL32), shipped as <c>x86\libwebpdecoder.dll</c> beside the exe -
''' <c>x64\</c> for a 64-bit host such as the net48 test leg - and loaded from that full path
''' only, never from the search path.
'''
''' What it decodes is exactly what the managed library it replaced decoded: a still WEBP (lossy,
''' lossless, with or without alpha). An animated WEBP is refused, as it always was on that
''' leg - the known gap the seam tests pin. The decode budget is asked first, from the bitstream
''' header. Whole-file <c>#If NETFRAMEWORK</c>: the mainline reads WEBP through Magick.NET.
''' </summary>
Friend Module LegacyLibWebp

    ''' <summary>The file name, beside the exe under the folder of the process bitness.</summary>
    Friend Const Library_File_Name As String = "libwebpdecoder.dll"

    ''' <summary>WEBP_DECODER_ABI_VERSION of libwebp 1.6.0 (src/webp/decode.h).</summary>
    Private Const Decoder_Abi_Version As Integer = &H210

    ''' <summary>VP8_STATUS_OK.</summary>
    Private Const Status_Ok As Integer = 0

    <StructLayout(LayoutKind.Sequential)>
    Private Structure WebPBitstreamFeatures
        Public Width As Integer
        Public Height As Integer
        Public HasAlpha As Integer
        Public HasAnimation As Integer
        Public Format As Integer
        <MarshalAs(UnmanagedType.ByValArray, SizeConst:=5)>
        Public Pad As UInteger()
    End Structure

    <DllImport("kernel32.dll", CharSet:=CharSet.Unicode, SetLastError:=True)>
    Private Function LoadLibraryW(path As String) As IntPtr
    End Function

    <DllImport(Library_File_Name, CallingConvention:=CallingConvention.Cdecl)>
    Private Function WebPGetFeaturesInternal(data As Byte(), dataSize As UIntPtr, ByRef features As WebPBitstreamFeatures, version As Integer) As Integer
    End Function

    <DllImport(Library_File_Name, CallingConvention:=CallingConvention.Cdecl)>
    Private Function WebPDecodeBGRAInto(data As Byte(), dataSize As UIntPtr, output As IntPtr, outputSize As UIntPtr, outputStride As Integer) As IntPtr
    End Function

    <DllImport(Library_File_Name, CallingConvention:=CallingConvention.Cdecl)>
    Private Function WebPGetDecoderVersion() As Integer
    End Function

    Private ReadOnly load_Gate As New Object()
    Private load_Attempted As Boolean
    Private load_Error As String

    ''' <summary>The full path the library is loaded from.</summary>
    Friend Function LibraryPath() As String
        Return Path.Combine(AppDomain.CurrentDomain.BaseDirectory, If(Environment.Is64BitProcess, "x64", "x86"), Library_File_Name)
    End Function

    ''' <summary>
    ''' Loads the library from <see cref="LibraryPath"/> once. The P/Invoke declarations name the
    ''' bare file, and Windows resolves a bare name to a module already loaded under it, so
    ''' nothing is ever looked up on the search path. Throws <see cref="DllNotFoundException"/>
    ''' (every time) when the file is missing or does not load.
    ''' </summary>
    Private Sub EnsureLoaded()
        SyncLock load_Gate
            If Not load_Attempted Then
                load_Attempted = True
                Dim fullPath As String = LibraryPath()
                If Not File.Exists(fullPath) Then
                    load_Error = "WEBP decoder library not found: " & fullPath
                ElseIf LoadLibraryW(fullPath) = IntPtr.Zero Then
                    load_Error = "WEBP decoder library did not load (error " & Marshal.GetLastWin32Error().ToString() & "): " & fullPath
                End If
            End If
            If load_Error IsNot Nothing Then Throw New DllNotFoundException(load_Error)
        End SyncLock
    End Sub

    ''' <summary>libwebp's version as "major.minor.revision" - for the test that pins the build.</summary>
    Friend Function DecoderVersion() As String
        EnsureLoaded()
        Dim packed As Integer = WebPGetDecoderVersion()
        Return ((packed >> 16) And &HFF).ToString() & "." & ((packed >> 8) And &HFF).ToString() & "." & (packed And &HFF).ToString()
    End Function

    ''' <summary>
    ''' A still WEBP to a detached 32-bit ARGB bitmap. Throws <see cref="InvalidDataException"/>
    ''' for data libwebp does not take as WEBP and for a picture over the decode budget,
    ''' <see cref="NotSupportedException"/> for an animation, <see cref="DllNotFoundException"/>
    ''' when the library is not there.
    ''' </summary>
    Friend Function DecodeStill(data As Byte()) As Bitmap
        If data Is Nothing OrElse data.Length = 0 Then Throw New InvalidDataException("No WEBP data.")
        EnsureLoaded()

        Dim features As New WebPBitstreamFeatures With {.Pad = New UInteger(4) {}}
        Dim status As Integer = WebPGetFeaturesInternal(data, New UIntPtr(CUInt(data.Length)), features, Decoder_Abi_Version)
        If status <> Status_Ok Then Throw New InvalidDataException("Not a decodable WEBP (status " & status.ToString() & ").")
        If features.HasAnimation <> 0 Then Throw New NotSupportedException("Animated WEBP is not supported by the legacy fallback.")
        If Not DecodeBudget.IsWithinBudget(features.Width, features.Height, 1) Then
            Throw New InvalidDataException("Decode refused, over budget (frame too large): " &
                                           features.Width.ToString() & "x" & features.Height.ToString() & " x 1 frames")
        End If

        Dim bitmap As New Bitmap(features.Width, features.Height, PixelFormat.Format32bppArgb)
        Try
            Dim locked As BitmapData = bitmap.LockBits(New Rectangle(0, 0, features.Width, features.Height),
                                                       ImageLockMode.WriteOnly, PixelFormat.Format32bppArgb)
            Try
                ' GDI+'s 32bppArgb is B, G, R, A in memory, not premultiplied - libwebp's BGRA.
                Dim outputSize As Long = CLng(locked.Stride) * features.Height
                Dim written As IntPtr = WebPDecodeBGRAInto(data, New UIntPtr(CUInt(data.Length)), locked.Scan0,
                                                           New UIntPtr(CULng(outputSize)), locked.Stride)
                If written = IntPtr.Zero Then Throw New InvalidDataException("The WEBP bitstream did not decode.")
            Finally
                bitmap.UnlockBits(locked)
            End Try
            Return bitmap
        Catch
            bitmap.Dispose()
            Throw
        End Try
    End Function

End Module
#End If
