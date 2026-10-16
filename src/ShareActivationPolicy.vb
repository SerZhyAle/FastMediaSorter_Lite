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

    ''' <summary>
    ''' The first build that understands the add-resource intent (spec 215: <c>::fms-add-resource::</c> over the wake, or
    ''' <c>--add-resource</c> on a cold start). The same written-decision shape as <see cref="MinConsentVersion"/>: builds
    ''' are stamped YY.M.D.HHmm and the intent landed on 2026-10-13. An older receiver gets the plain wake - it would
    ''' refuse the unknown payload (a pre-gate one could take any string for a folder), so the intent never travels to it.
    ''' </summary>
    Friend ReadOnly MinAddResourceVersion As New Version(26, 10, 13, 0)

    ''' <summary>
    ''' The first build that understands the schedule intent (ticket 222: <c>::fms-schedule::</c> plus a folder over the
    ''' wake) - a written decision of the same shape as the two above, not a measurement. An older receiver would refuse
    ''' the unknown payload, so the viewer sends it the plain wake instead and the user opens the page by hand.
    ''' </summary>
    Friend ReadOnly MinScheduleVersion As New Version(26, 10, 13, 2000)

    ''' <summary>
    ''' The first build whose viewer understands the phone-code hand-back (spec 227 T-3:
    ''' <c>::fms-phone-code::</c> / <c>::fms-phone-code-file::</c> over the mirror-image wake, the Share Manager sending
    ''' the code its camera or intake door read). The same written-decision shape. An older receiver would take the
    ''' payload for a file path and put the code on the screen in a "file not found" box - exactly what must never
    ''' happen to a secret holder - so the code never travels to it; the clipboard fallback covers that case.
    ''' </summary>
    Friend ReadOnly MinPhoneCodeVersion As New Version(26, 10, 15, 0)

    ''' <summary>
    ''' The first build that understands the clipboard intents (spec 230: <c>::fms-clipboard-send::</c> and
    ''' <c>::fms-clipboard-get::</c> over the wake, or <c>--clipboard-send</c> / <c>--clipboard-get</c> on a cold start).
    ''' The same written-decision shape. An older receiver would refuse the unknown payload (a pre-gate one could take
    ''' any string for a folder), so the intent never travels to it: the plain wake raises its window instead.
    ''' </summary>
    Friend ReadOnly MinClipboardVersion As New Version(26, 10, 16, 0)

    ''' <summary>Spec 227 T-3: phone connection code command prefix.</summary>
    Friend Const PhoneCodeCommandPrefix As String = "::fms-phone-code::"
    ''' <summary>Spec 227 T-3: phone connection code file command prefix.</summary>
    Friend Const PhoneCodeFileCommandPrefix As String = "::fms-phone-code-file::"

    Friend Function IsConsentCapable(companionVersion As Version) As Boolean
        Return companionVersion IsNot Nothing AndAlso companionVersion >= MinConsentVersion
    End Function

    ''' <summary>The add-resource intent travels only to a Share Manager proven to know it; an unknown version is not proof
    ''' (the plain wake is the fallback, never an error).</summary>
    Friend Function IsAddResourceCapable(companionVersion As Version) As Boolean
        Return companionVersion IsNot Nothing AndAlso companionVersion >= MinAddResourceVersion
    End Function

    ''' <summary>The schedule intent travels only to a Share Manager proven to know it (ticket 222); an unknown
    ''' version is not proof - the plain wake is the fallback, never an error.</summary>
    Friend Function IsScheduleCapable(companionVersion As Version) As Boolean
        Return companionVersion IsNot Nothing AndAlso companionVersion >= MinScheduleVersion
    End Function

    ''' <summary>The clipboard intents travel only to a Share Manager proven to know them (spec 230); an unknown version
    ''' is not proof - the plain wake is the fallback, never an error.</summary>
    Friend Function IsClipboardCapable(companionVersion As Version) As Boolean
        Return companionVersion IsNot Nothing AndAlso companionVersion >= MinClipboardVersion
    End Function

    ''' <summary>The phone code travels only to a viewer proven to know it (spec 227); an unknown version is not
    ''' proof - the clipboard fallback answers instead, never a payload an old receiver would show as a path.</summary>
    Friend Function IsPhoneCodeCapable(viewerVersion As Version) As Boolean
        Return viewerVersion IsNot Nothing AndAlso viewerVersion >= MinPhoneCodeVersion
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
