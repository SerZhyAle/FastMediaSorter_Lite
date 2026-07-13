Option Strict On

Imports System.Collections.Generic
Imports System.Drawing
Imports System.Drawing.Drawing2D
Imports System.Runtime.InteropServices

''' <summary>
''' The recognizable Android-share glyph - a blue four-way arrow ("shared in all
''' directions") with a white halo for contrast on light/dark taskbars. Drawn in
''' code (no asset file) so it scales cleanly. Ported verbatim from LITE's
''' Main_Form.ShareTray.BuildShareTrayIcon, and used for BOTH the tray NotifyIcon
''' and the Share Manager window icon so the two match.
''' </summary>
Public Module ShareIcons

    <DllImport("user32.dll", SetLastError:=True)>
    Private Function DestroyIcon(handle As IntPtr) As Boolean
    End Function

    ''' <summary>Creates a fresh share Icon. Icon.FromHandle does NOT own the HICON,
    ''' so the caller must pair this with <see cref="FreeIcon"/> (dispose + DestroyIcon).</summary>
    Public Function CreateIcon(ByRef hIcon As IntPtr) As Icon
        Dim handle As IntPtr = IntPtr.Zero
        Using bmp As Bitmap = CreateGlyphBitmap(32)
            handle = bmp.GetHicon()
        End Using
        hIcon = handle
        Return Icon.FromHandle(handle)
    End Function

    ''' <summary>Draws the blue four-way-arrow glyph into a transparent Bitmap of the
    ''' given size - for a Button.Image (the tray/window use the Icon overload above).</summary>
    Public Function CreateGlyphBitmap(size As Integer) As Bitmap
        Dim s As Single = size / 32.0F
        Dim c As Single = 16.0F * s     ' center
        Dim tp As Single = 14.0F * s    ' center -> arrow tip
        Dim hb As Single = 8.0F * s     ' center -> arrowhead base
        Dim hw As Single = 5.5F * s     ' arrowhead half-width (barbs)
        Dim sw As Single = 2.5F * s     ' shaft half-width

        Dim arm As PointF() = {
            New PointF(-sw, -sw), New PointF(-sw, -hb), New PointF(-hw, -hb),
            New PointF(0, -tp), New PointF(hw, -hb), New PointF(sw, -hb), New PointF(sw, -sw)}

        Dim pts As New List(Of PointF)()
        Dim cur As PointF() = arm
        For a As Integer = 0 To 3
            For Each p As PointF In cur
                pts.Add(New PointF(c + p.X, c + p.Y))
            Next
            Dim rot(cur.Length - 1) As PointF
            For i As Integer = 0 To cur.Length - 1
                rot(i) = New PointF(-cur(i).Y, cur(i).X)  ' rotate 90°
            Next
            cur = rot
        Next
        Dim poly As PointF() = pts.ToArray()

        Dim bmp As New Bitmap(size, size)
        Using g As Graphics = Graphics.FromImage(bmp)
            g.SmoothingMode = SmoothingMode.AntiAlias
            g.PixelOffsetMode = PixelOffsetMode.HighQuality
            g.Clear(Color.Transparent)
            Using halo As New Pen(Color.FromArgb(235, 255, 255, 255), 2.4F * s)
                halo.LineJoin = LineJoin.Round
                g.DrawPolygon(halo, poly)
            End Using
            Using fill As New SolidBrush(Color.FromArgb(30, 120, 220))
                g.FillPolygon(fill, poly)
            End Using
            Using edge As New Pen(Color.FromArgb(18, 78, 150), 1.0F * s)
                edge.LineJoin = LineJoin.Round
                g.DrawPolygon(edge, poly)
            End Using
        End Using
        Return bmp
    End Function

    ''' <summary>Disposes the Icon and frees its GDI handle (FromHandle doesn't own it).</summary>
    Public Sub FreeIcon(icon As Icon, hIcon As IntPtr)
        Try
            If icon IsNot Nothing Then icon.Dispose()
        Catch
        End Try
        If hIcon <> IntPtr.Zero Then
            Try : DestroyIcon(hIcon) : Catch : End Try
        End If
    End Sub

End Module
