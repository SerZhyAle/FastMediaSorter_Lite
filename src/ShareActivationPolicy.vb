#If Not NETFRAMEWORK Then
Option Strict On

''' <summary>What the viewer's "share this folder" does about the Share Manager it found.</summary>
Friend Enum ActivationPlan
    ''' <summary>No folder was asked for (or none exists): just raise the manager window.</summary>
    WakeOnly = 0
    ''' <summary>The Share Manager can put the folder to the user first: send it.</summary>
    SendFolder = 1
    ''' <summary>The Share Manager may share a received folder without asking (or cannot be
    ''' shown to do otherwise): raise the window only and tell the user to add the folder there.</summary>
    WakeAndAdvise = 2
End Enum

''' <summary>
''' The version-pair decision of 108 Q108-1, as a function. A Share Manager from before the
''' consent gate shares a folder it receives over WM_COPYDATA at once; sending it one would be
''' exactly the unattended publication the gate exists to prevent. The reply "handled" (ack 1)
''' proves receipt, NOT consent support, and there is no safe probe command an old receiver
''' would ignore (it would take any string for a folder). So the decision rests on the Share
''' Manager's own file version.
'''
''' <see cref="MinConsentVersion"/> is the first build that stages a received folder and asks:
''' builds are stamped YY.M.D.HHmm, and the gate landed on 2026-10-02. It is a written decision
''' awaiting the owner's confirmation (Q108-1), not a measurement - changing it is one edit here.
''' </summary>
Friend Module ShareActivationPolicy

    Friend ReadOnly MinConsentVersion As New Version(26, 10, 2, 0)

    Friend Function IsConsentCapable(companionVersion As Version) As Boolean
        Return companionVersion IsNot Nothing AndAlso companionVersion >= MinConsentVersion
    End Function

    ''' <summary>A folder travels only to a Share Manager proven to ask first; an unknown version
    ''' is not proof.</summary>
    Friend Function PlanFor(folder As String, companionVersion As Version) As ActivationPlan
        If String.IsNullOrEmpty(folder) Then Return ActivationPlan.WakeOnly
        Return If(IsConsentCapable(companionVersion), ActivationPlan.SendFolder, ActivationPlan.WakeAndAdvise)
    End Function

    ''' <summary>
    ''' "Does a Share Manager already own the mutex?" from what the probe did. Opened = yes.
    ''' Access denied = yes (an elevated owner's DACL excludes this token) - never "no", and never
    ''' permission to start a second manager. Any other failure is UNKNOWN (Nothing).
    ''' </summary>
    Friend Function ExistingInstanceHint(opened As Boolean, failure As Exception) As Boolean?
        If failure Is Nothing Then Return opened
        If TypeOf failure Is UnauthorizedAccessException Then Return True
        Return Nothing
    End Function

    ''' <summary>The four-part version of a file, or Nothing when it has none.</summary>
    Friend Function VersionOf(fileMajor As Integer, fileMinor As Integer, fileBuild As Integer, filePrivate As Integer) As Version
        If fileMajor = 0 AndAlso fileMinor = 0 AndAlso fileBuild = 0 AndAlso filePrivate = 0 Then Return Nothing
        Return New Version(fileMajor, fileMinor, fileBuild, filePrivate)
    End Function

End Module
#End If
