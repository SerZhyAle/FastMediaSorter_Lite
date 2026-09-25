#If Not NETFRAMEWORK Then
Option Strict On

''' <summary>
''' The three outcome classes of the FDSEC-BEHAVIOUR contract, section 7, as a type
''' (026_SPECIFICATION_SECURE_CONTAINER_FD_SEC_DOTNET10.md §5.1).
'''
''' They are a type rather than a set of booleans or a message string for one reason:
''' the contract says they must never merge, and a call site that builds its own
''' sentence from an exception message is exactly how two of them become one friendly
''' "wrong password". A caller maps this enum and nothing else.
'''
''' Cryptographically a wrong credential and a flipped byte fail an AEAD tag
''' identically - that is a security property, not a gap in this code - so
''' <see cref="FdSecOutcome.CredentialOrTamper"/> carries all three readings at once
''' and the surface that shows it must offer all three and choose none.
''' </summary>
Public Enum FdSecOutcome
    ''' <summary>The container opened.</summary>
    Ok

    ''' <summary>
    ''' Key slot 0 did not authenticate, or an AEAD tag failed later on a structurally
    ''' intact file: the credential may be wrong, the file may never have been a
    ''' container, or it may have been altered. One outcome, three readings.
    ''' </summary>
    CredentialOrTamper

    ''' <summary>
    ''' A file too short to hold a head, a structural rule broken AFTER the credential
    ''' proved right, broken length arithmetic (truncation), or a final digest mismatch.
    ''' The credential is never blamed here.
    ''' </summary>
    Damaged

    ''' <summary>
    ''' Unknown version, suite, flag bit or key-slot type in a container that DID
    ''' authenticate. Refused by name, never called damage - a future field brought into
    ''' use in the reserved bits must be refused by an old reader, not mis-parsed by it.
    ''' </summary>
    Unsupported
End Enum

''' <summary>
''' Every refusal this module raises. It carries the outcome class and a short English
''' reason for the log; the user-visible wording is chosen by the viewer from the class
''' alone, so a new reason string can never quietly become a new outcome.
'''
''' The reason never contains the credential or the sealed true name
''' (FDSEC-BEHAVIOUR §6 rule 8) - that is the whole point of the format.
''' </summary>
Public NotInheritable Class FdSecException
    Inherits Exception

    Public ReadOnly Property Outcome As FdSecOutcome

    Public Sub New(outcome As FdSecOutcome, reason As String)
        MyBase.New(reason)
        Me.Outcome = outcome
    End Sub

    Friend Shared Function CredentialOrTamper(reason As String) As FdSecException
        Return New FdSecException(FdSecOutcome.CredentialOrTamper, reason)
    End Function

    Friend Shared Function Damaged(reason As String) As FdSecException
        Return New FdSecException(FdSecOutcome.Damaged, reason)
    End Function

    Friend Shared Function Unsupported(reason As String) As FdSecException
        Return New FdSecException(FdSecOutcome.Unsupported, reason)
    End Function
End Class
#End If
