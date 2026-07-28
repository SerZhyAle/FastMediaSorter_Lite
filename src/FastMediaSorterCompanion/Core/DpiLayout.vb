Option Strict On

Imports System.Drawing
Imports System.Windows.Forms

''' <summary>
''' Small DPI/layout helpers shared by the Companion windows. WinForms scales a form's
''' fixed <c>ClientSize</c>/<c>MinimumSize</c> by the font/DPI factor, so a window authored
''' at 96 DPI can, at 125-200% display scaling (typical on laptops), become taller or wider
''' than the screen - its edges (Start/Stop button, OK/Cancel) then fall off-screen and turn
''' unclickable. AutoScaleMode.Font gets the *proportions* right; these helpers make sure the
''' result still fits the monitor. Call <see cref="ClampToWorkingArea"/> from a window's
''' <c>OnLoad</c> (after MyBase.OnLoad, when scaling has been applied).
''' </summary>
Friend Module DpiLayout

    ''' <summary>
    ''' Turns on DPI scaling for a hand-built window. Call it as the LAST statement of the
    ''' form's UI builder, once every child control has been added.
    '''
    ''' Setting <c>AutoScaleMode = Font</c> alone - what the Companion windows used to do - is
    ''' a NO-OP: <c>AutoScaleFactor</c> returns (1,1) while <c>AutoScaleDimensions</c> is empty,
    ''' so nothing is ever scaled. The designer writes both lines for a .Designer.vb form; a
    ''' code-built one must do the same by hand. Without them, at 150-175% display scaling the
    ''' text renders 1.75x larger over a layout still measured in 96-DPI pixels: captions clip,
    ''' values ellipsize, and a button cuts its own caption in half (measured at 168 DPI: the
    ''' main window's "Поделиться" button gets 44 px where its content needs 58).
    '''
    ''' Two details are easy to get wrong:
    ''' - ORDER. Scaling runs on the first layout after the flag is set and only touches the
    '''   children present at that moment - setting it before the controls are added scales the
    '''   form and nothing inside it.
    ''' - MODE. Dpi(96,96), not Font(7,15): these layouts are authored in 96-DPI pixels and the
    '''   UI font is pinned app-wide (<c>Application.SetDefaultFont</c> in Program.Main), so the
    '''   honest factor is the DPI ratio. Font mode derives it from GDI font metrics, which round
    '''   to 2.0x vertically at 168 DPI against a true 1.75x - inflating an already tall window
    '''   by another 14%.
    '''
    ''' A ListView's column widths are NOT part of this (WinForms never scales them) - size those
    ''' with <see cref="Control.LogicalToDeviceUnits(Integer)"/> instead.
    ''' </summary>
    Friend Sub ApplyAutoScale(f As ContainerControl)
        If f Is Nothing Then Return
        f.AutoScaleDimensions = New SizeF(96.0F, 96.0F)
        f.AutoScaleMode = AutoScaleMode.Dpi
    End Sub

    ''' <summary>Working area of the monitor the form sits on (falls back to the primary
    ''' screen, then to a safe default if there is no screen at all).</summary>
    Friend Function WorkingAreaFor(f As Form) As Rectangle
        Try
            If f IsNot Nothing AndAlso f.IsHandleCreated Then Return Screen.FromControl(f).WorkingArea
        Catch
        End Try
        Dim s As Screen = Screen.PrimaryScreen
        Return If(s IsNot Nothing, s.WorkingArea, New Rectangle(0, 0, 1024, 768))
    End Function

    ''' <summary>Never let MinimumSize exceed the working area, or the window cannot fit the
    ''' screen at high DPI (and, being at its minimum, the user cannot shrink it either).</summary>
    Friend Sub RelaxMinimumSize(f As Form)
        If f Is Nothing Then Return
        Dim m As Size = f.MinimumSize
        If m.Width = 0 AndAlso m.Height = 0 Then Return
        Dim wa As Rectangle = WorkingAreaFor(f)
        Dim w As Integer = If(m.Width > 0, Math.Min(m.Width, wa.Width), 0)
        Dim h As Integer = If(m.Height > 0, Math.Min(m.Height, wa.Height), 0)
        If w <> m.Width OrElse h <> m.Height Then f.MinimumSize = New Size(w, h)
    End Sub

    ''' <summary>Moves the form (keeping its size) so it lies fully within <paramref name="wa"/>.</summary>
    Friend Sub NudgeOnScreen(f As Form, wa As Rectangle)
        If f Is Nothing Then Return
        Dim x As Integer = Math.Max(wa.Left, Math.Min(f.Left, wa.Right - f.Width))
        Dim y As Integer = Math.Max(wa.Top, Math.Min(f.Top, wa.Bottom - f.Height))
        If x <> f.Left OrElse y <> f.Top Then f.Location = New Point(x, y)
    End Sub

    ''' <summary>Caps the form to the monitor working area and nudges it fully on-screen. Relaxes
    ''' an over-large MinimumSize first, otherwise the size cap cannot take effect.</summary>
    Friend Sub ClampToWorkingArea(f As Form)
        If f Is Nothing Then Return
        RelaxMinimumSize(f)
        Dim wa As Rectangle = WorkingAreaFor(f)
        Dim w As Integer = Math.Min(f.Width, wa.Width)
        Dim h As Integer = Math.Min(f.Height, wa.Height)
        If w <> f.Width OrElse h <> f.Height Then f.Size = New Size(w, h)
        NudgeOnScreen(f, wa)
    End Sub

    ''' <summary>Centers the form over its owner (or the working area when it has none), then
    ''' clamps it fully on-screen. Use after changing a form's size post-construction, since
    ''' the StartPosition centering already ran against the old size.</summary>
    Friend Sub CenterOnOwner(f As Form)
        If f Is Nothing Then Return
        Dim wa As Rectangle = WorkingAreaFor(f)
        Dim anchor As Rectangle = If(f.Owner IsNot Nothing, f.Owner.Bounds, wa)
        f.Location = New Point(anchor.Left + (anchor.Width - f.Width) \ 2,
                               anchor.Top + (anchor.Height - f.Height) \ 2)
        NudgeOnScreen(f, wa)
    End Sub

End Module
