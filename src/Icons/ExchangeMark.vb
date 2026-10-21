#If Not NETFRAMEWORK Then
Option Explicit On
Option Strict On
Option Infer On
Option Compare Binary

Imports System.Drawing
Imports System.Drawing.Drawing2D
Imports System.IO
Imports System.Windows.Forms

''' <summary>Unmodified EXCHANGE-MARK 0.3 artwork, shared by both modern applications.</summary>
Public NotInheritable Class ExchangeMark
    Private Sub New()
    End Sub

    Private Shared ReadOnly Sizes As Integer() = {16, 20, 24, 28, 30, 32, 36, 40, 42, 48, 56, 64, 96, 128, 256, 512}

    Public Shared Function AccessibleName() As String
        Return Localization.T("Сервер обмена")
    End Function

    Public Shared Function Render(size As Integer, ink As Color, opener As Func(Of String, Stream)) As Bitmap
        If size < 16 Then Throw New ArgumentOutOfRangeException(NameOf(size))
        If SystemInformation.HighContrast Then Return GlyphRenderer.Render("exchange-mark", size, ink, opener)
        Dim rasterSize As Integer = Sizes(Sizes.Length - 1)
        For Each candidate As Integer In Sizes
            If candidate >= size Then
                rasterSize = candidate
                Exit For
            End If
        Next
        If size > rasterSize Then Throw New ArgumentOutOfRangeException(NameOf(size))
        Using stream As Stream = opener("exchange-mark/png/mark-" & rasterSize.ToString(Globalization.CultureInfo.InvariantCulture) & ".png")
            If stream Is Nothing Then Throw New FileNotFoundException("Exchange mark is not bundled.")
            Using source As Image = Image.FromStream(stream)
                Dim result As New Bitmap(size, size)
                Using g As Graphics = Graphics.FromImage(result)
                    g.DrawImage(source, New Rectangle(0, 0, size, size))
                End Using
                Return result
            End Using
        End Using
    End Function

    ''' <summary>Rule 7: a selected, saturated row gives the unchanged mark a neutral plate.</summary>
    Public Shared Sub Draw(graphics As Graphics, x As Integer, y As Integer, size As Integer, ink As Color,
                           background As Color, neutral As Color, opener As Func(Of String, Stream))
        Dim spread As Integer = Math.Max(CInt(background.R), Math.Max(CInt(background.G), CInt(background.B))) -
            Math.Min(CInt(background.R), Math.Min(CInt(background.G), CInt(background.B)))
        If Not SystemInformation.HighContrast AndAlso spread > 48 Then
            Dim inset As Integer = Math.Max(2, size \ 8)
            Dim box As New Rectangle(x - inset, y - inset, size + inset * 2, size + inset * 2)
            Dim corner As Integer = inset * 2
            Using path As New GraphicsPath(), brush As New SolidBrush(neutral)
                path.AddArc(box.Left, box.Top, corner, corner, 180, 90)
                path.AddArc(box.Right - corner, box.Top, corner, corner, 270, 90)
                path.AddArc(box.Right - corner, box.Bottom - corner, corner, corner, 0, 90)
                path.AddArc(box.Left, box.Bottom - corner, corner, corner, 90, 90)
                path.CloseFigure()
                graphics.FillPath(brush, path)
            End Using
        End If
        Using picture As Bitmap = Render(size, ink, opener)
            graphics.DrawImageUnscaled(picture, x, y)
        End Using
    End Sub

    ''' <summary>Reserves a reading-start column inside a heading. The picture is never mirrored.</summary>
    Public Shared Sub Apply(label As Label, logicalSize As Integer, opener As Func(Of String, Stream))
        Dim mark As New MarkImage(opener) With {.Name = "exchangeMark", .AccessibleName = AccessibleName(),
            .AccessibleRole = AccessibleRole.Graphic, .TabStop = False, .BackColor = Color.Transparent}
        label.Controls.Add(mark)
        Dim update As EventHandler =
            Sub(sender, args)
                Dim size As Integer = label.LogicalToDeviceUnits(logicalSize)
                Dim gap As Integer = label.LogicalToDeviceUnits(8)
                Dim inset As Integer = Math.Max(1, size \ 8)
                Dim rtl As Boolean = label.RightToLeft = RightToLeft.Yes
                label.Padding = If(rtl, New Padding(0, 0, size + gap + inset * 2, 0), New Padding(size + gap + inset * 2, 0, 0, 0))
                label.MinimumSize = New Size(label.MinimumSize.Width, size + inset * 2)
                mark.Bounds = New Rectangle(If(rtl, label.Width - size - inset, inset), Math.Max(inset, (label.Height - size) \ 2), size, size)
                mark.AccessibleName = AccessibleName()
                mark.Invalidate()
                label.Invalidate()
            End Sub
        AddHandler label.HandleCreated, update
        AddHandler label.DpiChangedAfterParent, update
        AddHandler label.RightToLeftChanged, update
        AddHandler label.ForeColorChanged, update
        AddHandler label.SystemColorsChanged, update
        AddHandler label.SizeChanged, update
        AddHandler label.TextChanged, update
        update(label, EventArgs.Empty)
    End Sub

    Private NotInheritable Class MarkImage
        Inherits Control
        Private ReadOnly opener As Func(Of String, Stream)
        Public Sub New(opener As Func(Of String, Stream))
            Me.opener = opener
            SetStyle(ControlStyles.UserPaint Or ControlStyles.AllPaintingInWmPaint Or ControlStyles.OptimizedDoubleBuffer Or
                     ControlStyles.SupportsTransparentBackColor, True)
            SetStyle(ControlStyles.Selectable, False)
        End Sub
        Protected Overrides Sub OnPaint(e As PaintEventArgs)
            MyBase.OnPaint(e)
            Using picture = Render(Width, ForeColor, opener)
                e.Graphics.DrawImageUnscaled(picture, 0, 0)
            End Using
        End Sub
    End Class

    Public Shared Function Heading(text As String, logicalSize As Integer, opener As Func(Of String, Stream)) As Label
        Dim label As New Label With {.AutoSize = True, .Text = text, .UseMnemonic = False,
            .MaximumSize = New Size(440, 0), .Margin = New Padding(0, 0, 0, 8)}
        Apply(label, logicalSize, opener)
        Return label
    End Function

    ''' <summary>Native account lists own and dispose their DPI-sized image list.</summary>
    Public Shared Sub ApplyAccounts(list As ListView, opener As Func(Of String, Stream))
        list.OwnerDraw = True
        AddHandler list.DrawColumnHeader, Sub(sender, args) args.DrawDefault = True
        AddHandler list.DrawItem, Sub(sender, args) args.DrawDefault = (list.View <> View.Details)
        AddHandler list.DrawSubItem,
            Sub(sender, args)
                If args.ColumnIndex <> 0 Then
                    args.DrawDefault = True
                    Return
                End If
                Dim selected As Boolean = args.Item.Selected
                Dim back As Color = If(selected, SystemColors.Highlight, list.BackColor)
                Dim ink As Color = If(selected, SystemColors.HighlightText, list.ForeColor)
                Using brush As New SolidBrush(back)
                    args.Graphics.FillRectangle(brush, args.Bounds)
                End Using
                Dim size As Integer = list.LogicalToDeviceUnits(16)
                Dim inset As Integer = list.LogicalToDeviceUnits(4)
                Dim rtl As Boolean = list.RightToLeft = RightToLeft.Yes
                Dim bounds As Rectangle = args.Bounds
                bounds.Inflate(-inset, 0)
                Draw(args.Graphics, If(rtl, bounds.Right - size, bounds.Left), bounds.Top + (bounds.Height - size) \ 2,
                     size, ink, back, list.BackColor, opener)
                Dim indent As Integer = size + inset * 2
                bounds.X += If(rtl, 0, indent)
                bounds.Width = Math.Max(0, bounds.Width - indent)
                Dim flags As TextFormatFlags = TextFormatFlags.SingleLine Or TextFormatFlags.VerticalCenter Or TextFormatFlags.EndEllipsis Or TextFormatFlags.NoPrefix
                If rtl Then flags = flags Or TextFormatFlags.RightToLeft Or TextFormatFlags.Right
                TextRenderer.DrawText(args.Graphics, args.SubItem.Text, list.Font, bounds, ink, flags)
            End Sub
        Dim update As EventHandler =
            Sub(sender, args)
                Dim size As Integer = list.LogicalToDeviceUnits(16)
                Dim rowSize As Integer = size + Math.Max(2, size \ 8) * 2
                Dim images As New ImageList With {.ImageSize = New Size(rowSize, rowSize), .ColorDepth = ColorDepth.Depth32Bit}
                Using picture As Bitmap = Render(rowSize, list.ForeColor, opener)
                    images.Images.Add("exchange", picture)
                    ' ImageList defers copying its originals until the native handle exists.
                    ' Realize it while our bitmap is alive, before a ListView can create its handle.
                    Dim imageHandle As IntPtr = images.Handle
                End Using
                Dim previous As ImageList = list.SmallImageList
                list.SmallImageList = images
                previous?.Dispose()
                For Each item As ListViewItem In list.Items
                    item.ImageKey = "exchange"
                Next
            End Sub
        AddHandler list.DpiChangedAfterParent, update
        AddHandler list.ForeColorChanged, update
        AddHandler list.SystemColorsChanged, update
        AddHandler list.Disposed, Sub() list.SmallImageList?.Dispose()
        update(list, EventArgs.Empty)
    End Sub
End Class
#End If
