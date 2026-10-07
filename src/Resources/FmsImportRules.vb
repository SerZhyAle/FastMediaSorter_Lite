#If Not NETFRAMEWORK Then
Option Strict On

Imports System.Collections.Generic
Imports System.Linq

''' <summary>What the importer does with one FMSCFG field (100 §14.1 "every v2 root parameter must be
''' mapped, or shown as unsupported", Q100-4).</summary>
Friend Enum FmsFieldHandling
    ''' <summary>Decides whether the document is read at all (version, protocol).</summary>
    EnvelopeGate = 0
    ''' <summary>Validated and carried into the import PLAN; a malformed value refuses the whole document.
    ''' Whether the value also reaches the SAVED resource is <see cref="FmsFieldRule.StoredInResource"/>.</summary>
    Mapped = 1
    ''' <summary>Kept in the plan so it round-trips, never applied; the preview shows a notice.</summary>
    PreservedNotApplied = 2
    ''' <summary>A non-empty value is a security semantic this build cannot honour: that root is refused.</summary>
    RefusesRootWhenSet = 3
End Enum

Friend NotInheritable Class FmsFieldRule
    ''' <summary>JSON path with <c>[]</c> for array members, e.g. <c>roots[].readOnly</c>.</summary>
    Friend ReadOnly Path As String
    ''' <summary>First schemaVersion that defines the field.</summary>
    Friend ReadOnly Since As Integer
    Friend ReadOnly Handling As FmsFieldHandling
    ''' <summary>What an ABSENT value means; the restrictive default where the contract names one.</summary>
    Friend ReadOnly WhenAbsent As String
    ''' <summary>One line for the guide: what the field does today, not what the contract allows.</summary>
    Friend ReadOnly Note As String
    ''' <summary>True when a value of this field changes what <c>ResourceImport.Preview</c> saves (the
    ''' resource's name, address, root, writability, subfolder scan, pin) or the credential. False for a gate
    ''' (version, protocol), a refused field and the presentation values that are read, validated and then NOT
    ''' kept (100 §15.5.3 wants them preserved with a notice; Q100-4 has not been decided). The rows are held
    ''' to this by a differential test: a False row must leave the saved descriptor untouched.</summary>
    Friend ReadOnly StoredInResource As Boolean

    Friend Sub New(path As String, since As Integer, handling As FmsFieldHandling, whenAbsent As String, storedInResource As Boolean, note As String)
        Me.Path = path
        Me.Since = since
        Me.Handling = handling
        Me.WhenAbsent = whenAbsent
        Me.StoredInResource = storedInResource
        Me.Note = note
    End Sub
End Class

''' <summary>
''' The per-field support matrix of the FMSCFG reader (100 T08, Q100-4): ONE declared table, held in both
''' directions by tests - a field the reader consumes without a row here fails, and a row the reader no
''' longer reads fails. A field that is neither listed nor read is an unknown one and is ignored whole,
''' which is the contract's forward-compatibility rule; it never reaches a decision.
''' Pure data: no logic, no I/O.
''' </summary>
Friend Module FmsFieldSupport

    Private ReadOnly RuleList As New List(Of FmsFieldRule) From {
        New FmsFieldRule("schemaVersion", 1, FmsFieldHandling.EnvelopeGate, "invalid", False, "Judged first; above 2 asks to update the app and nothing else is read."),
        New FmsFieldRule("protocol", 1, FmsFieldHandling.EnvelopeGate, "invalid", False, "Exactly ""sftp"", case-sensitive."),
        New FmsFieldRule("resourceName", 1, FmsFieldHandling.Mapped, "empty name", True, "Display name of the saved resource, with the root's label; never an identity."),
        New FmsFieldRule("accessPaths[].kind", 1, FmsFieldHandling.Mapped, "entry skipped", True, "lan, ipv6, portforward; an unknown kind (rendezvousTunnel included) is skipped without being dialled."),
        New FmsFieldRule("accessPaths[].host", 1, FmsFieldHandling.Mapped, "entry refused", True, "Plain host or literal; no blanks, slashes or user-info."),
        New FmsFieldRule("accessPaths[].port", 1, FmsFieldHandling.Mapped, "entry refused", True, "Integer 1..65535."),
        New FmsFieldRule("username", 1, FmsFieldHandling.Mapped, "invalid", True, "Required, not blank."),
        New FmsFieldRule("password", 1, FmsFieldHandling.Mapped, "ask before commit", True, "Empty or absent: the user is asked, an unusable resource is never saved."),
        New FmsFieldRule("accessNote", 1, FmsFieldHandling.Mapped, "no note", False, "Plain text for a connection failure; read and validated, not kept."),
        New FmsFieldRule("hostKeyFingerprintSha256", 1, FmsFieldHandling.Mapped, "unpinned (first-use consent)", True, "Canonical SHA256:+43 base64 only; present-but-malformed refuses the document."),
        New FmsFieldRule("roots[].virtualPath", 1, FmsFieldHandling.Mapped, "invalid", True, "POSIX namespace path; no backslash, control character or dot segment."),
        New FmsFieldRule("roots[].label", 1, FmsFieldHandling.Mapped, "empty label", True, "Part of the saved resource's display name."),
        New FmsFieldRule("roots[].readOnly", 1, FmsFieldHandling.Mapped, "true", True, "Writable iff readOnly == false OR isDestination == true."),
        New FmsFieldRule("roots[].isDestination", 2, FmsFieldHandling.Mapped, "false", True, "Makes the root writable (readOnly OR isDestination); no destination slot is offered yet."),
        New FmsFieldRule("roots[].profile", 2, FmsFieldHandling.Mapped, "none", False, "Read and validated, an unrecognised token noticed in the plan; the saved resource does not keep it."),
        New FmsFieldRule("roots[].mediaTypes", 2, FmsFieldHandling.Mapped, "profile default", False, "Read and validated, unrecognised tokens noticed in the plan; the saved resource does not keep them."),
        New FmsFieldRule("roots[].scanSubdirectories", 2, FmsFieldHandling.Mapped, "true", True, "Becomes the resource's subfolder scan."),
        New FmsFieldRule("roots[].showSubfoldersAsItems", 2, FmsFieldHandling.PreservedNotApplied, "false", False, "Kept in the plan, not applied and not saved, until Q100-4 decides subfolders-as-items."),
        New FmsFieldRule("roots[].showHiddenFiles", 2, FmsFieldHandling.Mapped, "false", False, "Read and validated; the saved resource does not keep it."),
        New FmsFieldRule("roots[].allFiles", 2, FmsFieldHandling.Mapped, "false", False, "Read and validated; the saved resource does not keep it."),
        New FmsFieldRule("roots[].destinationColor", 2, FmsFieldHandling.Mapped, "none", False, "Best-effort ARGB integer; read and validated, not kept."),
        New FmsFieldRule("roots[].slideshowInterval", 2, FmsFieldHandling.Mapped, "resource default", False, "Seconds; a non-positive value does not take effect; read and validated, not kept."),
        New FmsFieldRule("roots[].comment", 2, FmsFieldHandling.Mapped, "no comment", False, "Plain display text; read and validated, not kept."),
        New FmsFieldRule("roots[].accessPin", 2, FmsFieldHandling.RefusesRootWhenSet, "no lock", False, "Non-empty: that root is refused (no PIN is implemented); empty means no lock.")}

    Friend ReadOnly Property Rules As IReadOnlyList(Of FmsFieldRule)
        Get
            Return RuleList
        End Get
    End Property

    Friend Function Find(path As String) As FmsFieldRule
        Return Rules.FirstOrDefault(Function(r) String.Equals(r.Path, path, StringComparison.Ordinal))
    End Function

End Module

Friend Enum FmsPinOutcome
    ''' <summary>Neither side has a pin: the first connection asks for consent (explicit TOFU).</summary>
    NoPinEitherSide = 0
    ''' <summary>Same key. Nothing to confirm.</summary>
    Unchanged = 1
    ''' <summary>The resource had no pin and the file brings one: it is shown in the preview and adopted.</summary>
    AdoptedFromImport = 2
    ''' <summary>The file carries no pin. The stored one stays; absence never clears a pin.</summary>
    KeptBecauseImportHasNone = 3
    ''' <summary>The file's pin differs from the stored one: needs a separate Cancel-default confirmation.</summary>
    ChangedNeedsConfirmation = 4
    ''' <summary>The stored value is not a usable fingerprint: treated as a change, never as "no pin".</summary>
    StoredPinUnusableNeedsConfirmation = 5
End Enum

''' <summary>The pin verdict of a re-import, and the one value that may be persisted for it.</summary>
Friend NotInheritable Class FmsPinDecision
    Friend ReadOnly Outcome As FmsPinOutcome
    Friend ReadOnly StoredPin As String
    Friend ReadOnly ImportedPin As String

    Friend Sub New(outcome As FmsPinOutcome, storedPin As String, importedPin As String)
        Me.Outcome = outcome
        Me.StoredPin = If(storedPin, "")
        Me.ImportedPin = If(importedPin, "")
    End Sub

    Friend ReadOnly Property RequiresConfirmation As Boolean
        Get
            Return Outcome = FmsPinOutcome.ChangedNeedsConfirmation OrElse Outcome = FmsPinOutcome.StoredPinUnusableNeedsConfirmation
        End Get
    End Property

    ''' <summary>The confirmation's default button is Cancel, always (100 §14.1).</summary>
    Friend ReadOnly Property ConfirmationDefaultsToCancel As Boolean
        Get
            Return True
        End Get
    End Property

    ''' <summary>The pin to store once the user has answered. Without an explicit confirmation a changed pin
    ''' is NEVER taken, and the stored one is never cleared.</summary>
    Friend Function PinToPersist(confirmed As Boolean) As String
        Select Case Outcome
            Case FmsPinOutcome.AdoptedFromImport
                Return ImportedPin
            Case FmsPinOutcome.ChangedNeedsConfirmation, FmsPinOutcome.StoredPinUnusableNeedsConfirmation
                Return If(confirmed, ImportedPin, StoredPin)
            Case Else
                Return StoredPin
        End Select
    End Function
End Class

''' <summary>Re-import is a previewed update, never a silent replacement (100 §4.2, §14.1). Pure.</summary>
Friend Module FmsReimport

    ''' <param name="storedPin">What the resource holds now ("" = none).</param>
    ''' <param name="importedPin">What the file carries; already validated canonical by the reader, or "".</param>
    Friend Function EvaluatePin(storedPin As String, importedPin As String) As FmsPinDecision
        Dim stored As String = If(storedPin, "")
        Dim imported As String = If(importedPin, "")
        Dim digest As Byte() = Nothing

        If stored.Length > 0 AndAlso Not SshFingerprint.TryParse(stored, digest) Then
            ' Corrupt store: fail closed. It is neither "no pin" (which would let anything in) nor silently replaced.
            Return New FmsPinDecision(FmsPinOutcome.StoredPinUnusableNeedsConfirmation, stored, imported)
        End If
        If stored.Length = 0 AndAlso imported.Length = 0 Then Return New FmsPinDecision(FmsPinOutcome.NoPinEitherSide, stored, imported)
        If stored.Length = 0 Then Return New FmsPinDecision(FmsPinOutcome.AdoptedFromImport, stored, imported)
        If imported.Length = 0 Then Return New FmsPinDecision(FmsPinOutcome.KeptBecauseImportHasNone, stored, imported)
        If SshFingerprint.AreEqual(stored, imported) Then Return New FmsPinDecision(FmsPinOutcome.Unchanged, stored, imported)
        Return New FmsPinDecision(FmsPinOutcome.ChangedNeedsConfirmation, stored, imported)
    End Function

End Module
#End If
