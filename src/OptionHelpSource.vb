#If Not NETFRAMEWORK Then
Option Explicit On
Option Strict On
Option Infer On
Option Compare Binary

Imports System.Collections.Generic
Imports System.Drawing

''' <summary>
''' The ONE authored help text of a boolean option (BOOLEAN-OPTION 2.0 section 4 rule 2; ticket 243, slice T-11a).
''' The short summary shown on hover or keyboard focus and the full explanation opened by click, Enter or Space are
''' two fields of the same object, written side by side by the same author, so the summary cannot drift from the
''' full text the way two copies in two files do. The strings are the ones the user reads - the author resolves them
''' through <c>Localization.T</c> when the source is built, and a window that switches language builds the sources
''' again and hands them to <see cref="OptionHeading.SetHelp"/>.
'''
''' Pure data. The rules a source has to meet live in <see cref="OptionHelpRules"/>.
''' </summary>
Friend NotInheritable Class OptionHelpSource

    Public Sub New(key As String, summary As String, full As String)
        Me.Key = If(key, String.Empty)
        Me.Summary = If(summary, String.Empty)
        Me.Full = If(full, String.Empty)
    End Sub

    ''' <summary>A stable identifier of the option's help (lower case letters, digits, '.', '_' and '-'); it is what
    ''' the surface help anchor and the tests name, never shown and never translated.</summary>
    Public ReadOnly Property Key As String

    ''' <summary>The short non-modal text of hover and focus: what the option does and the one consequence the user
    ''' must not miss.</summary>
    Public ReadOnly Property Summary As String

    ''' <summary>The persistent explanation: both states, the prerequisites and the dependent behaviour.</summary>
    Public ReadOnly Property Full As String
End Class

''' <summary>What is wrong with an <see cref="OptionHelpSource"/>.</summary>
Friend Enum OptionHelpProblem
    ''' <summary>The key is empty.</summary>
    KeyMissing = 0
    ''' <summary>The key has a character outside a-z, 0-9, '.', '_' and '-'.</summary>
    KeyMalformed = 1
    ''' <summary>The summary is empty or blank.</summary>
    SummaryMissing = 2
    ''' <summary>The full explanation is empty or blank.</summary>
    FullMissing = 3
    ''' <summary>The summary is not shorter than the full explanation: then it is not a summary.</summary>
    SummaryNotShorter = 4
End Enum

''' <summary>
''' The rules of an option's help that need no control: the validator of an <see cref="OptionHelpSource"/>, the
''' accessible-name template, and the timings of the hover (BOOLEAN-OPTION 2.0 section 4). Pure apart from
''' <c>Localization</c>.
''' </summary>
Friend NotInheritable Class OptionHelpRules

    Private Sub New()
    End Sub

    ''' <summary>How long the pointer rests on the help button before the summary opens (milliseconds).</summary>
    Friend Const HoverShowDelayMs As Integer = 400

    ''' <summary>How long neither the button nor the summary has the pointer or the focus before the summary closes: the
    ''' time to travel from the button into the summary (section 4 rule 3).</summary>
    Friend Const HoverGraceMs As Integer = 350

    ''' <summary>The widest the summary popup grows, logical px.</summary>
    Friend Const SummaryMaxWidthLogical As Integer = 320

    ''' <summary>The width of the persistent explanation, logical px.</summary>
    Friend Const FullWidthLogical As Integer = 400

    ''' <summary>Every rule a source breaks, in the order of <see cref="OptionHelpProblem"/>; empty when it is sound.</summary>
    Friend Shared Function Problems(source As OptionHelpSource) As List(Of OptionHelpProblem)
        Dim found As New List(Of OptionHelpProblem)()
        If source Is Nothing Then
            found.Add(OptionHelpProblem.KeyMissing)
            found.Add(OptionHelpProblem.SummaryMissing)
            found.Add(OptionHelpProblem.FullMissing)
            Return found
        End If
        If source.Key.Length = 0 Then
            found.Add(OptionHelpProblem.KeyMissing)
        ElseIf Not IsKey(source.Key) Then
            found.Add(OptionHelpProblem.KeyMalformed)
        End If
        Dim summary As String = source.Summary.Trim()
        Dim full As String = source.Full.Trim()
        If summary.Length = 0 Then found.Add(OptionHelpProblem.SummaryMissing)
        If full.Length = 0 Then found.Add(OptionHelpProblem.FullMissing)
        If summary.Length > 0 AndAlso full.Length > 0 AndAlso summary.Length >= full.Length Then
            found.Add(OptionHelpProblem.SummaryNotShorter)
        End If
        Return found
    End Function

    ''' <summary>True when <see cref="Problems"/> finds nothing.</summary>
    Friend Shared Function IsValid(source As OptionHelpSource) As Boolean
        Return Problems(source).Count = 0
    End Function

    ''' <summary>The keys that more than one source of a collection carries (compared by ordinal text): two options must
    ''' not share a help anchor.</summary>
    Friend Shared Function DuplicateKeys(sources As IEnumerable(Of OptionHelpSource)) As List(Of String)
        Dim seen As New HashSet(Of String)(StringComparer.Ordinal)
        Dim duplicated As New List(Of String)()
        If sources Is Nothing Then Return duplicated
        For Each source As OptionHelpSource In sources
            If source Is Nothing Then Continue For
            If Not seen.Add(source.Key) AndAlso Not duplicated.Contains(source.Key) Then duplicated.Add(source.Key)
        Next
        Return duplicated
    End Function

    Private Shared Function IsKey(key As String) As Boolean
        For Each ch As Char In key
            Dim ok As Boolean = (ch >= "a"c AndAlso ch <= "z"c) OrElse (ch >= "0"c AndAlso ch <= "9"c) OrElse
                                ch = "."c OrElse ch = "_"c OrElse ch = "-"c
            If Not ok Then Return False
        Next
        Return True
    End Function

    ''' <summary>The localized accessible name of an option's help button: "Help: Network scan" (section 4 rule 4). The
    ''' option's name goes through a template, never a concatenation, so a language puts it where its grammar wants it.</summary>
    Friend Shared Function AccessibleName(optionCaption As String) As String
        If String.IsNullOrWhiteSpace(optionCaption) Then Return Localization.T("Справка")
        Return Localization.TF("Справка: {0}", optionCaption.Trim())
    End Function
End Class

''' <summary>What the hover model asks its host to do after a tick.</summary>
Friend Enum OptionHelpHoverAction
    ''' <summary>Nothing to do.</summary>
    None = 0
    ''' <summary>Open the summary popup.</summary>
    ShowSummary = 1
    ''' <summary>Close the summary popup.</summary>
    HideSummary = 2
End Enum

''' <summary>
''' The state machine of the non-modal summary (BOOLEAN-OPTION 2.0 section 4 rules 2 and 3), pure, with the clock
''' passed in so a test needs no timer. Three things keep the summary up: the pointer on the help button, the pointer
''' on the summary itself, and the keyboard focus on the help button. It opens after the pointer has rested for
''' <see cref="OptionHelpRules.HoverShowDelayMs"/> (at once for the focus), closes
''' <see cref="OptionHelpRules.HoverGraceMs"/> after the last of the three has gone - so the pointer can travel from the
''' button into the summary without it closing - and Escape closes it for good until every cause has ended, so a pointer
''' that rests where it is does not open it again.
''' </summary>
Friend NotInheritable Class OptionHelpHover

    Private onButton As Boolean
    Private onPopup As Boolean
    Private focused As Boolean
    Private shown As Boolean
    Private suppressed As Boolean
    Private pendingSince As Long = -1
    Private idleSince As Long = -1

    ''' <summary>True between the show and the hide this model has asked for.</summary>
    Public ReadOnly Property SummaryShown As Boolean
        Get
            Return shown
        End Get
    End Property

    ''' <summary>True after Escape (or a persistent open) until the pointer and the focus have both left.</summary>
    Public ReadOnly Property IsSuppressed As Boolean
        Get
            Return suppressed
        End Get
    End Property

    Private ReadOnly Property Active As Boolean
        Get
            Return onButton OrElse onPopup OrElse focused
        End Get
    End Property

    ''' <summary>True when nothing is pending: no pointer, no focus, no summary open - a host may stop its timer.</summary>
    Public ReadOnly Property IsIdle As Boolean
        Get
            Return Not Active AndAlso Not shown
        End Get
    End Property

    ''' <summary>True while the pointer or the focus is on the button or the pointer is on the summary.</summary>
    Public ReadOnly Property HasCause As Boolean
        Get
            Return Active
        End Get
    End Property

    Public Sub PointerEnteredButton(nowMs As Long)
        onButton = True
        CauseBegan(nowMs)
    End Sub

    Public Sub PointerLeftButton(nowMs As Long)
        onButton = False
        CauseEnded(nowMs)
    End Sub

    Public Sub PointerEnteredPopup(nowMs As Long)
        onPopup = True
        CauseBegan(nowMs)
    End Sub

    Public Sub PointerLeftPopup(nowMs As Long)
        onPopup = False
        CauseEnded(nowMs)
    End Sub

    Public Sub FocusEntered(nowMs As Long)
        focused = True
        CauseBegan(nowMs)
    End Sub

    Public Sub FocusLeft(nowMs As Long)
        focused = False
        CauseEnded(nowMs)
    End Sub

    Private Sub CauseBegan(nowMs As Long)
        idleSince = -1
        If Not shown AndAlso pendingSince < 0 Then pendingSince = nowMs
    End Sub

    Private Sub CauseEnded(nowMs As Long)
        If Active Then Return
        pendingSince = -1
        suppressed = False
        If shown AndAlso idleSince < 0 Then idleSince = nowMs
    End Sub

    ''' <summary>Escape: closes the summary and keeps it closed while the same pointer rest or focus lasts. Returns True
    ''' when there was a summary to close (the key was used).</summary>
    Public Function Escape() As Boolean
        If Not shown Then Return False
        shown = False
        suppressed = True
        pendingSince = -1
        idleSince = -1
        Return True
    End Function

    ''' <summary>The persistent explanation took over, or the surface asks for the summary to go: closed at once and
    ''' suppressed like after Escape.</summary>
    Public Sub Dismiss()
        shown = False
        suppressed = True
        pendingSince = -1
        idleSince = -1
    End Sub

    ''' <summary>Forgets everything (the button was hidden or its window closed).</summary>
    Public Sub Reset()
        onButton = False
        onPopup = False
        focused = False
        shown = False
        suppressed = False
        pendingSince = -1
        idleSince = -1
    End Sub

    ''' <summary>Advances the clock; returns what the host has to do now.</summary>
    Public Function Tick(nowMs As Long) As OptionHelpHoverAction
        If Not shown Then
            If suppressed OrElse Not Active OrElse pendingSince < 0 Then Return OptionHelpHoverAction.None
            If focused OrElse nowMs - pendingSince >= OptionHelpRules.HoverShowDelayMs Then
                shown = True
                pendingSince = -1
                idleSince = -1
                Return OptionHelpHoverAction.ShowSummary
            End If
            Return OptionHelpHoverAction.None
        End If
        If Active Then
            idleSince = -1
            Return OptionHelpHoverAction.None
        End If
        If idleSince >= 0 AndAlso nowMs - idleSince >= OptionHelpRules.HoverGraceMs Then
            shown = False
            idleSince = -1
            Return OptionHelpHoverAction.HideSummary
        End If
        Return OptionHelpHoverAction.None
    End Function
End Class

''' <summary>Where a help popup goes (pure rectangle arithmetic, screen coordinates).</summary>
Friend NotInheritable Class OptionHelpPlacement

    Private Sub New()
    End Sub

    ''' <summary>
    ''' The rectangle of a popup of <paramref name="popup"/> size next to <paramref name="anchor"/> (the help button),
    ''' inside <paramref name="workArea"/> and, whenever any candidate allows it, off every rectangle of
    ''' <paramref name="avoid"/> (the originating checkbox) - BOOLEAN-OPTION 2.0 section 4 rule 3: "not obscure the
    ''' originating control when avoidable". Candidates in order: below the button, above it, beside it at the reading
    ''' end, beside it at the reading start. The popup's reading-start edge lines up with the button's (right edge under
    ''' <paramref name="rtl"/>), then it is slid back into the work area.
    ''' </summary>
    Friend Shared Function Place(anchor As Rectangle, avoid As IEnumerable(Of Rectangle), popup As Size,
                                 workArea As Rectangle, rtl As Boolean, gap As Integer) As Rectangle
        Dim avoided As New List(Of Rectangle)()
        If avoid IsNot Nothing Then avoided.AddRange(avoid)
        Dim w As Integer = Math.Min(popup.Width, Math.Max(1, workArea.Width))
        Dim h As Integer = Math.Min(popup.Height, Math.Max(1, workArea.Height))

        Dim startX As Integer = If(rtl, anchor.Right - w, anchor.Left)
        Dim candidates As New List(Of Rectangle) From {
            New Rectangle(startX, anchor.Bottom + gap, w, h),
            New Rectangle(startX, anchor.Top - gap - h, w, h),
            New Rectangle(If(rtl, anchor.Left - gap - w, anchor.Right + gap), anchor.Top, w, h),
            New Rectangle(If(rtl, anchor.Right + gap, anchor.Left - gap - w), anchor.Top, w, h)
        }
        Dim best As Rectangle = Clamp(candidates(0), workArea)
        Dim bestOverlap As Long = Long.MaxValue
        For Each candidate As Rectangle In candidates
            Dim fitted As Rectangle = Clamp(candidate, workArea)
            Dim overlap As Long = OverlapArea(fitted, avoided) + OverlapArea(fitted, New List(Of Rectangle) From {anchor})
            ' A candidate that needed no sliding is preferred over one that did, among equally clean ones.
            Dim moved As Long = If(fitted = candidate, 0L, 1L)
            Dim score As Long = overlap * 4L + moved
            If score < bestOverlap Then
                bestOverlap = score
                best = fitted
            End If
        Next
        Return best
    End Function

    Private Shared Function Clamp(r As Rectangle, workArea As Rectangle) As Rectangle
        Dim x As Integer = Math.Max(workArea.Left, Math.Min(r.X, workArea.Right - r.Width))
        Dim y As Integer = Math.Max(workArea.Top, Math.Min(r.Y, workArea.Bottom - r.Height))
        Return New Rectangle(x, y, r.Width, r.Height)
    End Function

    Private Shared Function OverlapArea(r As Rectangle, others As List(Of Rectangle)) As Long
        Dim total As Long = 0
        For Each other As Rectangle In others
            Dim common As Rectangle = Rectangle.Intersect(r, other)
            If common.Width > 0 AndAlso common.Height > 0 Then total += CLng(common.Width) * common.Height
        Next
        Return total
    End Function
End Class

''' <summary>Where the parts of a heading row stand (all in the row's client coordinates).</summary>
Friend Structure OptionHeadingPlan
    ''' <summary>Left edge of the heading glyph; meaningless when there is none.</summary>
    Public GlyphLeft As Integer
    ''' <summary>Left edge of the caption's label.</summary>
    Public CaptionLeft As Integer
    ''' <summary>Width of the caption's label (it wraps inside it).</summary>
    Public CaptionWidth As Integer
    ''' <summary>Left edge of the help target; meaningless when there is none.</summary>
    Public HelpLeft As Integer
    ''' <summary>True when the caption did not fit on one line next to the glyph and the help target.</summary>
    Public Wrapped As Boolean
End Structure

''' <summary>
''' The heading row of a boolean option (BOOLEAN-OPTION 2.0 section 2 rules 2 and 6), pure: the optional glyph, the caption
''' and the optional help target in reading order with an 8 unit gap between neighbours, mirrored under right-to-left.
''' A part that is absent takes no room and leaves no gap; the help target follows the caption's own width, so it is not
''' pushed to the cell's far edge; when the caption cannot fit on one line it takes all that is left and wraps, with the
''' help target at the end of that box.
''' </summary>
Friend NotInheritable Class OptionHeadingGeometry

    Private Sub New()
    End Sub

    ''' <param name="cellLeft">Left edge of the text cell.</param>
    ''' <param name="cellWidth">Width of the text cell.</param>
    ''' <param name="glyphSide">Side of the glyph, 0 when there is none.</param>
    ''' <param name="helpSide">Side of the help target, 0 when there is none.</param>
    ''' <param name="gap">Gap between neighbours.</param>
    ''' <param name="captionNaturalWidth">The caption on one line.</param>
    ''' <param name="minCaptionWidth">The least width a wrapping caption keeps.</param>
    Friend Shared Function Plan(cellLeft As Integer, cellWidth As Integer, rtl As Boolean, glyphSide As Integer,
                                helpSide As Integer, gap As Integer, captionNaturalWidth As Integer,
                                minCaptionWidth As Integer) As OptionHeadingPlan
        Dim result As New OptionHeadingPlan()
        Dim before As Integer = If(glyphSide > 0, glyphSide + gap, 0)
        Dim after As Integer = If(helpSide > 0, gap + helpSide, 0)
        Dim available As Integer = Math.Max(Math.Max(1, minCaptionWidth), cellWidth - before - after)
        Dim natural As Integer = Math.Max(1, captionNaturalWidth)
        result.Wrapped = natural > available
        result.CaptionWidth = If(result.Wrapped, available, natural)
        If rtl Then
            Dim right As Integer = cellLeft + cellWidth
            If glyphSide > 0 Then result.GlyphLeft = right - glyphSide
            Dim captionRight As Integer = right - before
            result.CaptionLeft = captionRight - result.CaptionWidth
            If helpSide > 0 Then result.HelpLeft = result.CaptionLeft - gap - helpSide
        Else
            If glyphSide > 0 Then result.GlyphLeft = cellLeft
            result.CaptionLeft = cellLeft + before
            If helpSide > 0 Then result.HelpLeft = result.CaptionLeft + result.CaptionWidth + gap
        End If
        Return result
    End Function
End Class
#End If
