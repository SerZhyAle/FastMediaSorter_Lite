Option Strict On

Imports System.IO
Imports System.Reflection
Imports System.Security

''' <summary>
''' Why a path did not answer, in the only vocabulary the UI is allowed to speak.
'''
''' The categories are not invented here - ProbeArgument (Main_Form.Lifecycle.vb) has told
''' missing from denied from transport for years, with a retry policy tuned per category,
''' and then threw the answer away. This module gives that distinction a name and a second
''' caller.
'''
''' It exists because of one defect it is the fix for: every read failure funnelled into
''' SkipUnreadableFile, whose first statement removes the file from the list. That is right
''' for a JPEG that will not decode and catastrophic for an IOException from a dropped SMB
''' session - a ten-second blip walked the auto-skip chain through a healthy folder, one
''' file per keypress, and ended in "no readable files in this folder" about a folder in
''' which everything was readable. See
''' SPECIFICATION_SLOT_HEALTH_AND_HONEST_FAILURES_DOTNET10.md §0.4 and §3.1.
'''
''' Pure: no I/O, no form state, no clock. That is what makes the classification testable
''' without a NAS - and the derivation trap below is exactly the kind of thing a test has
''' to pin down rather than a person having to remember.
'''
''' NOTE ON THE BUILD SEAM. The specification's §5 called this file whole-file
''' "#If Not NETFRAMEWORK", but its own table also says SkipUnreadableFile takes the kind
''' "on both builds" - and a parameter cannot be typed by an enum the other build does not
''' compile. The enum is therefore shared and only the BEHAVIOUR is fenced: on net48 the
''' kind is computed and ignored, so the x86 viewer keeps today's behaviour to the byte
''' (invariant 10) while the classifier is proven on both test legs.
''' </summary>
Public Module PathFailure

    ''' <summary>
    ''' What went wrong. The split that carries the whole feature is <see cref="IsAboutTheFile"/>:
    ''' Missing/Invalid/OutOfMemory/Content accuse the file, Transport/Denied accuse everything
    ''' between us and it.
    ''' </summary>
    Public Enum PathFailureKind
        ''' <summary>Nothing went wrong.</summary>
        None
        ''' <summary>The file or folder is genuinely gone (FileNotFound, DirectoryNotFound).</summary>
        Missing
        ''' <summary>An ACL or a sharing violation said no (UnauthorizedAccess, SecurityException).</summary>
        Denied
        ''' <summary>Nothing between us and the bytes is answering: a dropped SMB session, a
        ''' sleeping NAS, a card pulled out mid-read. The file is innocent and so is the list.</summary>
        Transport
        ''' <summary>The path itself cannot be used (ArgumentException, NotSupported, PathTooLong).</summary>
        Invalid
        ''' <summary>The file is readable and we could not hold it - a genuinely huge image, or
        ''' the corrupt-bitmap case GDI+ also reports as OutOfMemory.</summary>
        OutOfMemory
        ''' <summary>The bytes arrived and they are not a picture: an empty file, a truncated
        ''' download, a decoder that returned nothing. Added to the specification's list because
        ''' three call sites know this and "Invalid" means an invalid PATH, which would put the
        ''' wrong sentence in front of the user (invariant 2).</summary>
        Content
        ''' <summary>Nobody has classified this one yet. Deliberately counts as being about the
        ''' file (§6.4): that preserves today's auto-skip for anything unseen, and the log line
        ''' names the exception type so the next kind can be added on purpose.</summary>
        Unknown
    End Enum

    ''' <summary>
    ''' Turns an exception into a category.
    '''
    ''' ORDER IS THE WHOLE FUNCTION, and it is not the order one would write by feel:
    ''' FileNotFoundException, DirectoryNotFoundException and PathTooLongException all derive
    ''' from IOException, so a classifier that asks "is it an IOException" first reports every
    ''' missing file as a transport failure - and then the list never self-cleans, which is the
    ''' mirror image of the bug this module fixes. UnauthorizedAccessException, by contrast,
    ''' does NOT derive from IOException. The tests pin all of it.
    ''' </summary>
    Public Function Classify(ex As Exception) As PathFailureKind
        If ex Is Nothing Then Return PathFailureKind.None

        Dim real As Exception = Unwrap(ex)

        ' The IOException subclasses that are NOT about the transport, first.
        If TypeOf real Is FileNotFoundException OrElse
           TypeOf real Is DirectoryNotFoundException Then Return PathFailureKind.Missing
        If TypeOf real Is PathTooLongException Then Return PathFailureKind.Invalid

        If TypeOf real Is UnauthorizedAccessException OrElse
           TypeOf real Is SecurityException Then Return PathFailureKind.Denied

        If TypeOf real Is OutOfMemoryException Then Return PathFailureKind.OutOfMemory

        If TypeOf real Is ArgumentException OrElse
           TypeOf real Is NotSupportedException OrElse
           TypeOf real Is FormatException Then Return PathFailureKind.Invalid

        ' Everything left under IOException is the transport: the session dropped, the device
        ' is not ready, the stream ended early.
        If TypeOf real Is IOException Then Return PathFailureKind.Transport

        Return PathFailureKind.Unknown
    End Function

    ''' <summary>
    ''' True when the file itself is the problem, so dropping it from the list is the right
    ''' answer. False for transport and denial, where the list is innocent - that single
    ''' inversion is what stops a network blip from shredding a healthy folder (§0.4, D7).
    ''' </summary>
    Public Function IsAboutTheFile(kind As PathFailureKind) As Boolean
        Select Case kind
            Case PathFailureKind.Transport, PathFailureKind.Denied
                Return False
            Case Else
                ' Unknown included, on purpose - see the enum. None too: "nothing went wrong"
                ' never reaches the skip path, and answering True keeps today's behaviour for
                ' a caller that gets there anyway.
                Return True
        End Select
    End Function

    ''' <summary>
    ''' Digs the real failure out of the wrappers the async decode path puts around it.
    ''' The display pipeline uses GetAwaiter().GetResult() precisely so its own catches still
    ''' match, but the prefetch worker and the abandoned-decode continuation both hand back
    ''' AggregateException, and reflection-driven calls hand back TargetInvocationException -
    ''' classifying either of those as Unknown would drop a file the transport lost.
    ''' </summary>
    Private Function Unwrap(ex As Exception) As Exception
        Dim current As Exception = ex
        ' Bounded: a corrupt chain must not spin the UI thread.
        For hop As Integer = 0 To 7
            Dim aggregate As AggregateException = TryCast(current, AggregateException)
            If aggregate IsNot Nothing AndAlso aggregate.InnerExceptions.Count > 0 Then
                current = aggregate.InnerExceptions(0)
                Continue For
            End If

            If TypeOf current Is TargetInvocationException AndAlso current.InnerException IsNot Nothing Then
                current = current.InnerException
                Continue For
            End If

            Exit For
        Next
        Return current
    End Function

End Module
