#If Not NETFRAMEWORK Then
Option Strict On

''' <summary>
''' Which file's video is prepared AHEAD of the one on screen (spec 242 rules 20 and 24): the
''' neighbour in the direction of the last navigation, and nothing when there is none. Pure.
'''
''' The direction rule is NOT restated here: it is <c>NavigationRules.PrefetchIndex</c>, the same
''' neighbour the decode prefetch uses, wrap-around at both ends included, so the two never point at
''' different files. This module only turns that answer into "an index, or no neighbour". Whether the
''' neighbour is worth preparing is a second question, asked of its header facts through
''' <see cref="AnimationVideoPolicy.Decide"/>; when its verdict is not Eligible it is simply not
''' prepared (rule 24).
''' </summary>
Friend Module AnimationVideoPrefetchPlan

    ''' <summary>The answer when there is nothing to prepare ahead.</summary>
    Friend Const No_Neighbour As Integer = -1

    ''' <summary>
    ''' The index to prepare ahead while <paramref name="current"/> is on screen, or
    ''' <see cref="No_Neighbour"/> for an empty list and for a list of one (its only neighbour is the
    ''' file itself, which is already the one being prepared). <paramref name="backwards"/> is the
    ''' viewer's <c>is_Nav_Backwards</c>; an out-of-range <paramref name="current"/> is clamped as
    ''' <c>PrefetchIndex</c> clamps it.
    ''' </summary>
    Friend Function NeighbourToPrepare(current As Integer, total As Integer, backwards As Boolean) As Integer
        If total <= 1 Then Return No_Neighbour
        Dim neighbour As Integer = NavigationRules.PrefetchIndex(current, total, backwards)
        Return If(neighbour < 0, No_Neighbour, neighbour)
    End Function

End Module
#End If
