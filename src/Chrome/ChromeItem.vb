#If Not NETFRAMEWORK Then
Option Strict On

Imports System.Drawing

Friend Enum ChromeItemKind
    ''' <summary>A push button.</summary>
    Button
    ''' <summary>A button with a checked state and an accent fill - a mode, not an action.</summary>
    Toggle
    ''' <summary>Opens a menu (phase 4).</summary>
    MenuButton
    ''' <summary>A live value that is read, not pressed (phase 5).</summary>
    Text
End Enum

''' <summary>
''' One thing a floating panel shows (002 section 2.2): a plain object, not a control - twenty child
''' controls would mean twenty window handles per panel, no rounded corners without regions and
''' no animation across items. The surface paints it, the layout places it.
'''
''' <see cref="Id"/> is the Name of the Designer control it replaces (<c>btn_Prev_File</c>): it is
''' what automation addresses, and it is what keeps the migration mechanical. The action is the
''' EXISTING handler's work - it clicks that control - so a rebuilt element keeps its Handles wiring
''' (invariant 1).
''' </summary>
Friend NotInheritable Class ChromeItem

    Public Sub New(id As String, kind As ChromeItemKind)
        Me.Id = id
        Me.Kind = kind
    End Sub

    Public ReadOnly Property Id As String
    Public Property Kind As ChromeItemKind

    ''' <summary>Catalog glyph id (<c>media.previous</c>); empty = the item draws <see cref="Caption"/>.</summary>
    Public Property GlyphId As String = ""

    ''' <summary>Text drawn when there is no glyph (and the stand-in for one that has no catalog entry).</summary>
    Public Property Caption As String = ""

    ''' <summary>The caption is a symbol, not a word: it is drawn in the symbol font, never the script font.</summary>
    Public Property CaptionIsSymbol As Boolean

    Public Property ShowCaptionWithGlyph As Boolean

    ''' <summary>The words a screen reader says and the menu line shows.</summary>
    Public Property AccessibleName As String = ""

    ''' <summary>The hover text; carries the hotkey.</summary>
    Public Property Tooltip As String = ""

    Public Property Enabled As Boolean = True
    Public Property Visible As Boolean = True
    Public Property Checked As Boolean

    ''' <summary>The red family: the only element that means "this destroys".</summary>
    Public Property Destructive As Boolean

    ''' <summary>The pin button of a panel's corner: painted as a pin (upright and filled while
    ''' <see cref="Checked"/>, tilted and outlined while not), never as a checkbox.</summary>
    Public Property IsPin As Boolean

    ''' <summary>True when caption text should align to the start/left edge rather than centered.</summary>
    Public Property AlignLeft As Boolean

    ''' <summary>A direction ("back"/"forward"): drawn mirrored when the UI language reads right to left.</summary>
    Public Property MirrorInRtl As Boolean

    ''' <summary>What a click does.</summary>
    Public Property Action As Action

    ''' <summary>What a right-click does (an item's secondary action); Nothing = none.</summary>
    Public Property SecondaryAction As Action

    ''' <summary>User-defined data associated with the item.</summary>
    Public Property Image As Image
    Public Property PreferredWidth As Integer
    Public Property MinimumWidth As Integer
    Public Property Tag As Object

    ' --- layout and paint state, owned by the surface ----------------------------------------

    ''' <summary>Where the layout put it, in the surface's client coordinates.</summary>
    Public Property Bounds As Rectangle

    ''' <summary>Hover amount 0..1, eased by the surface.</summary>
    Friend Property HoverLevel As Single

    ''' <summary>Press amount 0..1, eased by the surface.</summary>
    Friend Property PressLevel As Single

    ''' <summary>True while the pointer is held down on it.</summary>
    Friend Property IsPressed As Boolean

    ''' <summary>True while the pointer is over it.</summary>
    Friend Property IsHot As Boolean

End Class
#End If
