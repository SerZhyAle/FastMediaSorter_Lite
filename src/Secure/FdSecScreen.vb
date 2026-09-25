#If Not NETFRAMEWORK Then
Option Strict On

''' <summary>
''' The one thing a reader may conclude about a file WITHOUT the credential
''' (026_SPECIFICATION_SECURE_CONTAINER_FD_SEC_DOTNET10.md §4.3, FDSEC-FORMAT §12).
'''
''' A container carries no marker at all, so nothing short of the credential can say
''' whether a file is one. Exactly one test exists, and it comes from the alignment
''' invariants: every container is a whole number of 512-byte clusters, and the smallest
''' possible one - A = 512 with an empty original - is 5632 bytes. A file that fails
''' either test is certainly not a container.
'''
''' This runs BEFORE any dialog, and it reports DAMAGED without ever asking for a
''' password: a refusal that arrives after the password was typed is a refusal that
''' wasted a secret (FDSEC-BEHAVIOUR §8.1).
'''
''' A file that PASSES both tests still says nothing whatsoever. There is no
''' "probably a container" anywhere in this feature.
''' </summary>
Friend Module FdSecScreen

    ''' <summary>The smallest length any container can have: 5632 bytes.</summary>
    Friend ReadOnly SmallestContainer As Long = SmallestPossible()

    Private Function SmallestPossible() As Long
        ' Parenthesised on purpose - VB binds * tighter than \, so "x \ a * a" divides by
        ' a squared and this constant would come out as zero.
        Dim align As Long = FdSecFormat.MinAlignment
        Dim pre As Long = ((CLng(FdSecFormat.HeadSize + FdSecFormat.MetadataSize) + align - 1) \ align) * align
        Return ((pre + FdSecFormat.TagSize + align - 1) \ align) * align
    End Function

    ''' <summary>
    ''' True when a file of this length could be a container at all. False means it
    ''' certainly is not, and the caller says "damaged or incomplete" - never "wrong
    ''' password", which would be a lie about a file nobody has a credential for yet.
    ''' </summary>
    Friend Function CouldBeContainer(length As Long) As Boolean
        If length < SmallestContainer Then Return False
        If length Mod FdSecFormat.MinAlignment <> 0 Then Return False
        Return True
    End Function

End Module
#End If
