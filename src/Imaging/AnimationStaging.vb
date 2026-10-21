#If Not NETFRAMEWORK Then
Option Strict On

Imports System.Drawing
Imports System.Runtime.CompilerServices

''' <summary>
''' What the first frame of a long animation carries to whoever shows it (spec 241 rules 9, 10
''' and 18): the figures the later stages need, none of which a plain Bitmap can hold.
''' Immutable.
''' </summary>
Friend NotInheritable Class StagedInfo

    Friend ReadOnly Width As Integer
    Friend ReadOnly Height As Integer
    Friend ReadOnly Frames As Integer
    Friend ReadOnly DurationMs As Long
    Friend ReadOnly HasAlpha As Boolean

    ''' <summary>True when the animation is over the decode budget: there is no stage 2, the first
    ''' frame is all the viewer will ever show of it (rule 18).</summary>
    Friend ReadOnly IsTooLong As Boolean

    Friend Sub New(payload As DecodedPayload)
        Width = payload.Width
        Height = payload.Height
        Frames = payload.Frames
        DurationMs = payload.DurationMs
        HasAlpha = payload.HasAlpha
        IsTooLong = payload.IsTooLong
    End Sub

End Class

''' <summary>
''' Which animations are opened in two stages, and the side table that lets a preview picture say
''' so (spec 241 section 3.4). Pure apart from the table.
'''
''' The display path already hands a decoded picture around as a <c>Tuple(Of Image, MemoryStream)</c>
''' through half a dozen layers; a preview has to survive all of them, and widening every
''' signature for a fact that exists on the mainline only would touch the net48 exe's code.
''' So the fact rides on the picture itself: a weak table keyed by the Image OBJECT. It dies with
''' the picture, and a copy of the picture (a rotation, a thumbnail) is a different object that
''' correctly knows nothing.
''' </summary>
Friend Module AnimationStaging

    ''' <summary>Animations whose total pixels (width x height x frames) are over this are opened in
    ''' two stages (spec 241 rule 9). About 0.6 s of work by the measured ns/pixel - the point where
    ''' the loading badge is already on screen - so a smaller animation keeps today's one step.</summary>
    Friend Const Still_First_Min_Total_Pixels As Long = 10L * 1000L * 1000L

    Private ReadOnly previews As New ConditionalWeakTable(Of Image, StagedInfo)()

    ''' <summary>True when an animation of this size is worth a first frame of its own. Without
    ''' overflow for a crafted header: the product is never formed.</summary>
    Friend Function ShouldStage(width As Long, height As Long, frames As Long) As Boolean
        If width <= 0 OrElse height <= 0 OrElse frames <= 1 Then Return False
        ' A frame the budget refuses is never shown, staged or not.
        If width > DecodeBudget.Max_Frame_Pixels OrElse height > DecodeBudget.Max_Frame_Pixels Then Return False
        Dim framePixels As Long = width * height
        If framePixels > DecodeBudget.Max_Frame_Pixels Then Return False
        Return frames > Still_First_Min_Total_Pixels \ framePixels
    End Function

    ''' <summary>Whether a first-frame payload stands for an animation that is to be opened in two
    ''' stages: over the threshold, or over the budget altogether.</summary>
    Friend Function IsStaged(payload As DecodedPayload) As Boolean
        If payload Is Nothing OrElse Not payload.IsPreview Then Return False
        Return payload.IsTooLong OrElse ShouldStage(payload.Width, payload.Height, payload.Frames)
    End Function

    ''' <summary>Marks a picture as the first frame of a long animation.</summary>
    Friend Sub Mark(picture As Image, info As StagedInfo)
        If picture Is Nothing OrElse info Is Nothing Then Return
        previews.AddOrUpdate(picture, info)
    End Sub

    ''' <summary>The animation a picture stands in for, or Nothing when it is an ordinary picture.</summary>
    Friend Function InfoOf(picture As Image) As StagedInfo
        If picture Is Nothing Then Return Nothing
        Dim info As StagedInfo = Nothing
        Return If(previews.TryGetValue(picture, info), info, Nothing)
    End Function

    ''' <summary>The picture is no longer a stand-in (it was replaced, or disposed).</summary>
    Friend Sub Unmark(picture As Image)
        If picture Is Nothing Then Return
        previews.Remove(picture)
    End Sub

End Module
#End If
