Option Explicit On
Option Strict On

' ResourceTransfer.vb (the viewer's, linked for the checked transfer of scheduled operations) names one constant of
' the viewer's FileManager - the extension of an in-flight copy. FileManager itself is the viewer's and is not linked
' here (it is the image loader and file layer of a 2 MB program); this module carries that one frozen constant, and
' ScheduledSurfaceTests holds it equal to the viewer's own so the two can never drift apart.
Friend Module FileManager

    ''' <summary>Extension given to the in-flight copy of a staged move. Frozen with the viewer's own.</summary>
    Friend Const Partial_Move_Suffix As String = ".fms-part"

End Module