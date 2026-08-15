Option Strict On

' What one queued file operation IS. Shared by both viewers.
'
' It lived inside Main_Form as a Private Enum until the undo stack needed a rule - "which
' completed operations can be taken back, and by doing what" - that is worth proving with a
' test rather than re-reading a Select Case (SPECIFICATION_RECYCLE_BIN_AND_UNDO_DOTNET10.md
' §3.5). A Private nested enum cannot be named by anything outside the class, so the enum
' moved out and UndoPolicy could stay pure. Nothing else changed: every "FileOpKind.X" in
' the form partials resolves exactly as before, both projects compile it, and the members
' the x86 build never creates simply are never created there.
Friend Enum FileOpKind
    Copy
    Move
    ''' <summary>Permanent - what deleting always was, and what it still is on net48.</summary>
    Delete
    ''' <summary>To the Recycle Bin. Mainline only; created by ExecuteDelete when the policy
    ''' says the volume has a bin that will take the file.</summary>
    RecycleDelete
    ''' <summary>A same-folder rename (F6). Mainline only, and only so that it can be undone.</summary>
    Rename

    ' --- the inverses. None of these is ever recorded on the undo stack: that is what
    ' stops U from ping-ponging, and it is structural rather than a reentrancy flag.

    ''' <summary>Undo of a copy: delete the copy we made. Permanent on purpose - routing it
    ''' through the Recycle Bin would fill the bin with our own noise.</summary>
    DeleteUndo
    ''' <summary>Undo of a move: put the file back where it came from.</summary>
    MoveUndo
    ''' <summary>Undo of a rename: the old name back, in the same folder.</summary>
    RenameUndo
    ''' <summary>Undo of a recycled delete: the file comes back OUT of the Recycle Bin.
    ''' Mainline only, and the one inverse that can honestly fail without failing - the bin
    ''' may have been emptied in between, which is an answer rather than an error.</summary>
    RestoreFromBin
End Enum
