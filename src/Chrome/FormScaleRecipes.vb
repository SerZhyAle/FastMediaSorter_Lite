#If Not NETFRAMEWORK Then
Option Strict On

Imports System.Collections.Generic
Imports System.Drawing
Imports System.Runtime.CompilerServices
Imports System.Windows.Forms

''' <summary>What a product-owned font is meant to be (specification L0361 rule S2: the recipe is stored, a resolved
''' size never is): the unit it is assigned in, the family and style, and its em height in pixels at 96 dpi at the
''' window's design scale (<c>M</c> already inside). Immutable; one instance may be shared by many controls.</summary>
Friend NotInheritable Class FormScaleFontRecipe

    Friend ReadOnly Property Kind As FormScaleFontKind
    Friend ReadOnly Property Family As String
    Friend ReadOnly Property Style As FontStyle
    ''' <summary>The em height in pixels at 96 dpi, the design multiplier included (a 9 pt font at 100 % is 12).</summary>
    Friend ReadOnly Property DesignPixels As Double

    Friend Sub New(kind As FormScaleFontKind, family As String, style As FontStyle, designPixels As Double)
        Me.Kind = kind
        Me.Family = If(family, "")
        Me.Style = style
        Me.DesignPixels = designPixels
    End Sub

    ''' <summary>A point recipe from a point size at the design scale (<c>p x M</c>): 0.75 pt per logical pixel.</summary>
    Friend Shared Function FromDesignPoints(family As String, designPoints As Double, style As FontStyle) As FormScaleFontRecipe
        Return New FormScaleFontRecipe(FormScaleFontKind.Point, family, style, designPoints / 0.75)
    End Function

    ''' <summary>The size to assign (points or pixels, by <see cref="Kind"/>) for a control that reads its font at
    ''' <paramref name="basisDpi"/> (rule S4). The design multiplier is already inside the recipe.</summary>
    Friend Function AssignedSize(basisDpi As Integer, systemDpi As Integer) As Double
        Return FormScale.AssignedFontSize(Kind, DesignPixels, 1.0, basisDpi, systemDpi)
    End Function

    ''' <summary>Rule S9: the em height the control must show in a window at <paramref name="windowDpi"/>,
    ''' <c>l x M x max(1, D / 96)</c>, in device pixels.</summary>
    Friend Function ExpectedOnScreenPixels(windowDpi As Integer) As Double
        Return DesignPixels * FormScale.LayoutFactor(windowDpi)
    End Function

    Public Overrides Function ToString() As String
        Return String.Format(Globalization.CultureInfo.InvariantCulture, "{0} {1} {2} {3:0.##} lpx",
                             Kind, Family, Style, DesignPixels)
    End Function

End Class

''' <summary>One difference the font audit of rule S9 found: a control whose effective font is not its recipe at the
''' window's current dpi.</summary>
Friend NotInheritable Class FormScaleFontMismatch
    Friend ReadOnly Property Path As String
    Friend ReadOnly Property Recipe As FormScaleFontRecipe
    Friend ReadOnly Property ExpectedPixels As Double
    Friend ReadOnly Property ActualPixels As Double
    Friend ReadOnly Property Reason As String

    Friend Sub New(path As String, recipe As FormScaleFontRecipe, expectedPixels As Double, actualPixels As Double, reason As String)
        Me.Path = path
        Me.Recipe = recipe
        Me.ExpectedPixels = expectedPixels
        Me.ActualPixels = actualPixels
        Me.Reason = If(reason, "")
    End Sub

    Public Overrides Function ToString() As String
        Return String.Format(Globalization.CultureInfo.InvariantCulture,
                             "{0}: {1} expected {2:0.##} px, actual {3:0.##} px{4}",
                             Path, Recipe, ExpectedPixels, ActualPixels, If(Reason.Length = 0, "", " (" & Reason & ")"))
    End Function
End Class

''' <summary>
''' The per-window recipe table of specification L0361 (slice T-6, ticket L0365): a <see cref="ConditionalWeakTable(Of TKey, TValue)"/>
''' from control to the recipe of the font it was given, so a DPI change can re-resolve EVERY product-owned font of
''' the window - including the ones no style walk visits - and the audit of rule S9 can compare each control's
''' effective font with its recipe. Controls are held weakly: a disposed page leaves the table with its controls.
''' Fonts the window issues are remembered too (the last recipe a font object was issued for), so a control that
''' received an issued font in an object initializer is recorded by <see cref="Track"/> before it joins the window
''' (WinForms replaces the font object when it rescales a control that joins a window of another dpi).
''' </summary>
Friend NotInheritable Class FormScaleRecipeTable

    ''' <summary>Rule S9 tolerance in device pixels: a pixel font is assigned in whole pixels (one rounding at the
    ''' basis dpi, then the platform's exact rescale by D / c), so a correct font may differ from the recipe by less
    ''' than one basis pixel scaled to the window. Anything wider is a mismatch, never a rounding.</summary>
    Friend Const TolerancePixels As Double = 0.75

    Private ReadOnly byControl As New ConditionalWeakTable(Of Control, FormScaleFontRecipe)()
    Private ReadOnly byFont As New ConditionalWeakTable(Of Font, FormScaleFontRecipe)()

    ''' <summary>Remembers that <paramref name="font"/> was issued for <paramref name="recipe"/>.</summary>
    Friend Sub Issue(font As Font, recipe As FormScaleFontRecipe)
        If font Is Nothing OrElse recipe Is Nothing Then Return
        byFont.AddOrUpdate(font, recipe)
    End Sub

    ''' <summary>Records the recipe of the font <paramref name="control"/> was given.</summary>
    Friend Sub Record(control As Control, recipe As FormScaleFontRecipe)
        If control Is Nothing OrElse recipe Is Nothing Then Return
        byControl.AddOrUpdate(control, recipe)
    End Sub

    ''' <summary>Records <paramref name="control"/> under the recipe its current font object was issued for. Call it
    ''' right after an object initializer assigned an issued font, before the control joins the window. A font the
    ''' table did not issue records nothing.</summary>
    Friend Sub Track(control As Control)
        If control Is Nothing Then Return
        Dim recipe As FormScaleFontRecipe = Nothing
        If byFont.TryGetValue(control.Font, recipe) Then Record(control, recipe)
    End Sub

    Friend Function TryGetRecipe(control As Control, ByRef recipe As FormScaleFontRecipe) As Boolean
        recipe = Nothing
        If control Is Nothing Then Return False
        Return byControl.TryGetValue(control, recipe)
    End Function

    ''' <summary>The live controls with a recipe, in no particular order.</summary>
    Friend Function Entries() As List(Of KeyValuePair(Of Control, FormScaleFontRecipe))
        Dim result As New List(Of KeyValuePair(Of Control, FormScaleFontRecipe))()
        For Each pair As KeyValuePair(Of Control, FormScaleFontRecipe) In byControl
            If pair.Key IsNot Nothing AndAlso Not pair.Key.IsDisposed Then result.Add(pair)
        Next
        Return result
    End Function

    ''' <summary>Measured fact E4: the em height in device pixels a font shows at - a pixel font its size, a point
    ''' font its size through the SYSTEM dpi. Other units give NaN (the audit reports them).</summary>
    Friend Shared Function OnScreenPixels(font As Font, systemDpi As Integer) As Double
        If font Is Nothing Then Return Double.NaN
        Select Case font.Unit
            Case GraphicsUnit.Pixel
                Return font.Size
            Case GraphicsUnit.Point
                Return font.Size * Math.Max(FormScale.LogicalDpi, systemDpi) / 72.0
            Case Else
                Return Double.NaN
        End Select
    End Function

    ''' <summary>Rule S9 for one control: Nothing when its font is its recipe at <paramref name="windowDpi"/>.</summary>
    Friend Shared Function Compare(path As String, recipe As FormScaleFontRecipe, font As Font,
                                   systemDpi As Integer, windowDpi As Integer) As FormScaleFontMismatch
        Dim expected As Double = recipe.ExpectedOnScreenPixels(windowDpi)
        Dim actual As Double = OnScreenPixels(font, systemDpi)
        If Double.IsNaN(actual) Then Return New FormScaleFontMismatch(path, recipe, expected, actual, "unit " & font?.Unit.ToString())
        Dim reason As String = ""
        If Not String.Equals(font.FontFamily.Name, recipe.Family, StringComparison.OrdinalIgnoreCase) AndAlso
           Not String.Equals(font.OriginalFontName, recipe.Family, StringComparison.OrdinalIgnoreCase) Then
            reason = "family " & font.FontFamily.Name
        ElseIf font.Style <> recipe.Style Then
            reason = "style " & font.Style.ToString()
        End If
        If Math.Abs(actual - expected) <= TolerancePixels AndAlso reason.Length = 0 Then Return Nothing
        Return New FormScaleFontMismatch(path, recipe, expected, actual, reason)
    End Function

    ''' <summary>The audit of rule S9: every recorded control that is in <paramref name="window"/>, its effective font
    ''' compared with its recipe at the window's current dpi. An empty list is a pass.</summary>
    Friend Function Audit(window As Form, systemDpi As Integer) As List(Of FormScaleFontMismatch)
        Dim result As New List(Of FormScaleFontMismatch)()
        If window Is Nothing OrElse window.IsDisposed Then Return result
        Dim windowDpi As Integer = window.DeviceDpi
        For Each pair As KeyValuePair(Of Control, FormScaleFontRecipe) In Entries()
            Dim control As Control = pair.Key
            If control IsNot window AndAlso control.FindForm() IsNot window Then Continue For
            Dim mismatch As FormScaleFontMismatch = Compare(PathOf(control, window), pair.Value, control.Font, systemDpi, windowDpi)
            If mismatch IsNot Nothing Then result.Add(mismatch)
        Next
        result.Sort(Function(a, b) String.CompareOrdinal(a.Path, b.Path))
        Return result
    End Function

    ''' <summary>True when <paramref name="font"/> is still the product's own font for <paramref name="recipe"/>: a pixel or
    ''' point font of the recipe's family and style. A font some other code gave the control on purpose (a bold caption, a
    ''' symbol face, a unit the product does not issue) is not ours to correct; the audit of rule S9 reports it instead.</summary>
    Friend Shared Function IsOwnFont(recipe As FormScaleFontRecipe, font As Font) As Boolean
        If recipe Is Nothing OrElse font Is Nothing Then Return False
        If font.Unit <> GraphicsUnit.Pixel AndAlso font.Unit <> GraphicsUnit.Point Then Return False
        If font.Style <> recipe.Style Then Return False
        Return String.Equals(font.FontFamily.Name, recipe.Family, StringComparison.OrdinalIgnoreCase) OrElse
               String.Equals(font.OriginalFontName, recipe.Family, StringComparison.OrdinalIgnoreCase)
    End Function

    ''' <summary>Rule S10, the heal: for every recorded control in <paramref name="window"/> whose effective font is the
    ''' product's own but not the size its recipe asks for at the window's current dpi, assigns the recipe's font resolved
    ''' for that dpi. It looks only at the result (the font the control carries now), never at which of the platform's
    ''' rescales did or did not run, so it is right after any order of build, add, first show, monitor move and DPI
    ''' change, and it converges: a second call changes nothing.
    ''' <para>A control is in the window when <c>FindForm()</c> says so, but it is only SETTLED when its own
    ''' <c>DeviceDpi</c> is the window's: a control in a page that has no handle yet (a tab never selected) keeps the dpi
    ''' it started at until the page is first shown, and the platform then rescales its font by window dpi / that dpi
    ''' (measured, 2026-11-19). So the size judged is the PROJECTED one - the font it carries now, times D / c - and a
    ''' correction is resolved for <c>c</c> (rule S4), never for D: resolving it for D is the defect this pass exists to
    ''' undo (0.57 times too small at 96 on a 168 system, 1.29 times too large at 216).</para>
    ''' <paramref name="resolve"/> returns the window's shared font for a recipe and a basis dpi. Returns how many controls
    ''' changed (0 = nothing to lay out again). The window's own font is left to the platform, which changes it after the
    ''' DPI event (fact E3).</summary>
    Friend Function Heal(window As Form, systemDpi As Integer, resolve As Func(Of FormScaleFontRecipe, Integer, Font)) As Integer
        If window Is Nothing OrElse window.IsDisposed OrElse Not window.IsHandleCreated OrElse resolve Is Nothing Then Return 0
        Dim windowDpi As Integer = window.DeviceDpi
        Dim healed As Integer = 0
        For Each pair As KeyValuePair(Of Control, FormScaleFontRecipe) In Entries()
            Dim control As Control = pair.Key
            If control Is window OrElse control.IsDisposed OrElse control.FindForm() IsNot window Then Continue For
            Dim basisDpi As Integer = HealBasis(pair.Value, control.Font, control.DeviceDpi, windowDpi, systemDpi)
            If basisDpi = 0 Then Continue For
            Dim font As Font = resolve(pair.Value, basisDpi)
            If font Is Nothing OrElse ReferenceEquals(control.Font, font) Then Continue For
            control.Font = font
            healed += 1
        Next
        Return healed
    End Function

    ''' <summary>Rule S10 for one control, pure: 0 when the control needs nothing (not the product's own font, or the size
    ''' it will show once the platform has moved it to the window's dpi is already the recipe's), otherwise the basis dpi
    ''' the correction is resolved for - the control's own dpi (rule S4). The size judged is the one the control carries
    ''' now times <paramref name="windowDpi"/> / its own dpi: the platform rescales by exactly that when the control's
    ''' page is first shown. A pixel font is assigned in whole pixels at its basis, so the rounding of up to half a basis
    ''' pixel grows by the same ratio and the tolerance grows with it.</summary>
    Friend Shared Function HealBasis(recipe As FormScaleFontRecipe, font As Font, controlDpi As Integer,
                                     windowDpi As Integer, systemDpi As Integer) As Integer
        If Not IsOwnFont(recipe, font) Then Return 0
        Dim basis As Integer = Math.Max(FormScale.LogicalDpi, controlDpi)
        Dim window As Integer = Math.Max(FormScale.LogicalDpi, windowDpi)
        Dim projected As Double = OnScreenPixels(font, systemDpi) * window / basis
        Dim tolerance As Double = Math.Max(TolerancePixels, 0.5 * window / basis + 0.01)
        If Math.Abs(projected - recipe.ExpectedOnScreenPixels(window)) <= tolerance Then Return 0
        Return basis
    End Function

    ''' <summary>A readable path of a control inside its window: names where the Designer gave one, else the type and
    ''' the index among its siblings.</summary>
    Friend Shared Function PathOf(control As Control, root As Control) As String
        Dim parts As New List(Of String)()
        Dim current As Control = control
        While current IsNot Nothing AndAlso current IsNot root
            Dim name As String = current.Name
            If String.IsNullOrEmpty(name) Then
                Dim index As Integer = If(current.Parent Is Nothing, -1, current.Parent.Controls.GetChildIndex(current, False))
                name = current.GetType().Name & "[" & index.ToString(Globalization.CultureInfo.InvariantCulture) & "]"
            End If
            parts.Add(name)
            current = current.Parent
        End While
        If current Is root AndAlso root IsNot Nothing Then parts.Add(If(String.IsNullOrEmpty(root.Name), root.GetType().Name, root.Name))
        parts.Reverse()
        Return String.Join("/", parts)
    End Function

End Class
#End If
