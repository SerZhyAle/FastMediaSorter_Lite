#If Not NETFRAMEWORK Then
Option Strict On

Imports System.Text

''' <summary>
''' The work factor of one format version (FDSEC-FORMAT §6, FDSEC-BEHAVIOUR §2.4).
'''
''' These four numbers are deliberately NOT stored in the container, and that is forced
''' rather than chosen: the head is masked with a credential-derived keystream, so a
''' reader must derive BEFORE it can read anything, and a work factor it could read
''' first would have to sit in the clear - which would name the format and hand a
''' scanner the marker this format refuses to carry. A different work factor is
''' therefore a new format version, never a per-file option.
'''
''' Nothing here is a preference and nothing in it is ours to tune: a build that quietly
''' lowered the memory or the iteration count would produce a reader that opens nothing
''' anyone else made.
''' </summary>
Public NotInheritable Class FdSecProfile

    ''' <summary>Credential length in UTF-8 bytes at which the fast branch takes over.</summary>
    Public ReadOnly Property Threshold As Integer
    ''' <summary>Argon2id memory, in KiB - the contract's unit, not bytes.</summary>
    Public ReadOnly Property MemoryKib As Integer
    Public ReadOnly Property Iterations As Integer
    Public ReadOnly Property Lanes As Integer

    Public Sub New(threshold As Integer, memoryKib As Integer, iterations As Integer, lanes As Integer)
        Me.Threshold = threshold
        Me.MemoryKib = memoryKib
        Me.Iterations = iterations
        Me.Lanes = lanes
    End Sub

    ''' <summary>The profile of format version 1 - the only one defined, and the only one
    ''' any shipped path may use.</summary>
    Public Shared ReadOnly Property V1 As FdSecProfile = New FdSecProfile(threshold:=64, memoryKib:=65536, iterations:=3, lanes:=4)

    ''' <summary>
    ''' The profile actually in force, with Nothing meaning "version 1".
    '''
    ''' A lowered profile is the test-only seam of FDSEC-BEHAVIOUR §13.3, and it is a
    ''' PARAMETER rather than a settable global precisely so no shipped path can touch
    ''' it by accident: a container written under a lowered profile is readable only
    ''' under that same profile, so a leak here would produce files nobody can open.
    ''' </summary>
    Friend Shared Function OrV1(profile As FdSecProfile) As FdSecProfile
        Return If(profile, V1)
    End Function

End Class

''' <summary>
''' The derived keys of containers already opened this session, so returning to a file
''' costs nothing (026_SPECIFICATION_SECURE_CONTAINER_FD_SEC_DOTNET10.md §5.5).
'''
''' A DERIVED KEY CANNOT BE REUSED ACROSS FILES - every container has its own salt - so
''' what is cached is one entry per container, and the entry is keyed by the container's
''' own salt rather than by its path and write time. That is tighter than keying on the
''' path: two copies of one container share the entry, and a container re-packed in place
''' gets a new salt and therefore a new entry, where a path-and-time key would have to be
''' trusted to notice.
'''
''' The credential is folded into the key as a keyed digest, so a cache carried across a
''' change of credential simply misses instead of handing back keys that authenticate
''' nothing. Everything in here is key material and lives only in memory: it is dropped
''' when the credential is dropped, and wiped as far as a garbage-collected runtime allows.
''' </summary>
Public NotInheritable Class FdSecKeyCache

    Private Const Tag As String = "FDSEC1/cachetag"

    Private ReadOnly gate As New Object()
    Private ReadOnly entries As New Dictionary(Of String, Byte()())(StringComparer.Ordinal)

    Friend Function TryGet(credentialUtf8 As Byte(), salt As Byte(),
                           <System.Runtime.InteropServices.Out> ByRef maskKey As Byte(),
                           <System.Runtime.InteropServices.Out> ByRef kek As Byte()) As Boolean
        maskKey = Nothing
        kek = Nothing
        Dim key As String = KeyFor(credentialUtf8, salt)
        SyncLock gate
            Dim pair As Byte()() = Nothing
            If Not entries.TryGetValue(key, pair) Then Return False
            maskKey = CType(pair(0).Clone(), Byte())
            kek = CType(pair(1).Clone(), Byte())
            Return True
        End SyncLock
    End Function

    Friend Sub Put(credentialUtf8 As Byte(), salt As Byte(), maskKey As Byte(), kek As Byte())
        Dim key As String = KeyFor(credentialUtf8, salt)
        SyncLock gate
            entries(key) = New Byte()() {CType(maskKey.Clone(), Byte()), CType(kek.Clone(), Byte())}
        End SyncLock
    End Sub

    ''' <summary>Drops everything, wiping as far as the runtime allows - best effort, never
    ''' a guarantee (FDSEC-BEHAVIOUR §8.2). Called when the credential is forgotten, when
    ''' it times out, and when the program closes.</summary>
    Public Sub Clear()
        SyncLock gate
            For Each pair As Byte()() In entries.Values
                FdSecPrimitives.Wipe(pair(0))
                FdSecPrimitives.Wipe(pair(1))
            Next
            entries.Clear()
        End SyncLock
    End Sub

    Private Shared Function KeyFor(credentialUtf8 As Byte(), salt As Byte()) As String
        ' Keyed rather than plain: the cache tag must not be something an attacker with a
        ' memory dump could match against a guess any more cheaply than the format allows.
        Dim tagged As Byte() = FdSecPrimitives.Blake2bKeyed(FdSecPrimitives.Blake2b(256, credentialUtf8),
                                                            FdSecPrimitives.Ascii(Tag), salt)
        Dim builder As New Text.StringBuilder(tagged.Length * 2)
        For Each b As Byte In tagged
            builder.Append(b.ToString("x2"))
        Next
        Return builder.ToString()
    End Function

End Class

''' <summary>
''' A credential held for the rest of a session, as the normalised UTF-8 bytes the format
''' derives from (026 §5.5) - never as a String, which is immutable and cannot be wiped.
''' <see cref="Dispose"/> overwrites the bytes, so "forget the password" really puts it
''' down as far as a garbage-collected runtime allows (FDSEC-BEHAVIOUR §8.2: best effort,
''' and described that way). Audit 052 G-19.
'''
''' Thread-safe: the prefetch reads it from the worker thread while the UI thread may be
''' forgetting it. A disposed credential refuses to hand out bytes rather than handing out
''' zeros, which would derive a key for the empty credential.
''' </summary>
Public NotInheritable Class FdSecCredential
    Implements IDisposable

    Private ReadOnly gate As New Object()
    Private utf8 As Byte()
    Private ReadOnly original As Byte()

    Private Sub New(bytes As Byte())
        utf8 = bytes
        original = bytes
    End Sub

    ''' <summary>Normalises <paramref name="text"/> once (NFC, UTF-8, never trimmed). The
    ''' caller should drop its own reference to the String straight after.</summary>
    Public Shared Function FromText(text As String) As FdSecCredential
        Return New FdSecCredential(FdSecKdf.NormalizeCredential(text))
    End Function

    ''' <summary>A fresh copy for one derivation; the caller wipes it. Throws
    ''' <see cref="ObjectDisposedException"/> once the credential was forgotten.</summary>
    Friend Function CopyBytes() As Byte()
        SyncLock gate
            If utf8 Is Nothing Then Throw New ObjectDisposedException(NameOf(FdSecCredential))
            Return CType(utf8.Clone(), Byte())
        End SyncLock
    End Function

    ''' <summary>The array the credential was held in, for the test that proves Dispose
    ''' wipes it. Nothing in the product reads it.</summary>
    Friend ReadOnly Property HeldBytesForTest As Byte()
        Get
            Return original
        End Get
    End Property

    Public ReadOnly Property IsDisposed As Boolean
        Get
            SyncLock gate
                Return utf8 Is Nothing
            End SyncLock
        End Get
    End Property

    ''' <summary>Same bytes, compared without an early exit. False once either side was
    ''' forgotten.</summary>
    Public Function SameAs(other As FdSecCredential) As Boolean
        If other Is Nothing Then Return False
        Dim mine As Byte() = Nothing
        Dim theirs As Byte() = Nothing
        Try
            mine = CopyBytes()
            theirs = other.CopyBytes()
            Return Security.Cryptography.CryptographicOperations.FixedTimeEquals(mine, theirs)
        Catch ex As ObjectDisposedException
            Return False
        Finally
            FdSecPrimitives.Wipe(mine)
            FdSecPrimitives.Wipe(theirs)
        End Try
    End Function

    Public Sub Dispose() Implements IDisposable.Dispose
        SyncLock gate
            If utf8 Is Nothing Then Return
            FdSecPrimitives.Wipe(utf8)
            utf8 = Nothing
        End SyncLock
    End Sub

End Class

''' <summary>
''' Credential handling and the two keys one container needs
''' (026_SPECIFICATION_SECURE_CONTAINER_FD_SEC_DOTNET10.md §5.1, FDSEC-BEHAVIOUR §3).
''' </summary>
Friend Module FdSecKdf

    Private Const MaskLabel As String = "FDSEC1/mask"
    Private Const KekLabel As String = "FDSEC1/kek"

    ''' <summary>
    ''' Normalise a credential to the bytes the format derives from: Unicode NFC, UTF-8,
    ''' and NO trimming - mistake 5 of FDSEC-BEHAVIOUR §13.1. A leading or trailing space
    ''' is part of the credential, and a credential that round-trips on one machine and
    ''' fails on another is almost always this.
    '''
    ''' A null credential is the empty one, which is accepted and takes the slow branch.
    ''' It is obfuscation with no secrecy, and the surface that accepts it says so - this
    ''' function deliberately does not refuse it.
    ''' </summary>
    Friend Function NormalizeCredential(text As String) As Byte()
        If text Is Nothing Then Return Array.Empty(Of Byte)()
        Return Encoding.UTF8.GetBytes(text.Normalize(NormalizationForm.FormC))
    End Function

    ''' <summary>
    ''' The root key, on the branch the credential's own length picks. Which branch ran
    ''' is stored nowhere: the reader recomputes it from the credential it holds, and the
    ''' threshold is at least 1, so an empty credential always takes the slow branch.
    '''
    ''' Length is not entropy: sixty-four repetitions of one character take the fast
    ''' branch, by design. That statement belongs on every surface that accepts a
    ''' credential, and it is why this function makes no judgement about the one it got.
    ''' </summary>
    Friend Function DeriveRoot(credentialUtf8 As Byte(), salt As Byte(), profile As FdSecProfile) As Byte()
        Dim active As FdSecProfile = FdSecProfile.OrV1(profile)
        If credentialUtf8.Length < active.Threshold Then
            Return FdSecPrimitives.Argon2id(credentialUtf8, salt,
                                            active.Iterations, active.MemoryKib, active.Lanes,
                                            FdSecFormat.FileKeySize)
        End If
        Dim wide As Byte() = FdSecPrimitives.Blake2b(512, salt, credentialUtf8)
        Dim root(FdSecFormat.FileKeySize - 1) As Byte
        Array.Copy(wide, root, root.Length)
        FdSecPrimitives.Wipe(wide)
        Return root
    End Function

    ''' <summary>
    ''' The two keys one container uses, domain-separated so neither use can leak the
    ''' other: the mask key never touches an AEAD, the KEK never touches the keystream.
    ''' </summary>
    Friend Sub DeriveKeys(credentialUtf8 As Byte(), salt As Byte(), profile As FdSecProfile,
                          <System.Runtime.InteropServices.Out> ByRef maskKey As Byte(),
                          <System.Runtime.InteropServices.Out> ByRef kek As Byte(),
                          Optional cache As FdSecKeyCache = Nothing)
        If cache IsNot Nothing AndAlso cache.TryGet(credentialUtf8, salt, maskKey, kek) Then Return

        ' One derivation at a time, process-wide (§5.5). Argon2id at 64 MiB with four lanes
        ' is designed to be expensive, and the prefetch worker derives on its own thread:
        ' two of them at once would take 128 MiB and make the visible one slower, which is
        ' the opposite of what a prefetch is for.
        SyncLock derivationGate
            If cache IsNot Nothing AndAlso cache.TryGet(credentialUtf8, salt, maskKey, kek) Then Return

            Dim root As Byte() = DeriveRoot(credentialUtf8, salt, profile)
            Try
                maskKey = FdSecPrimitives.Blake2bKeyed(root, FdSecPrimitives.Ascii(MaskLabel))
                kek = FdSecPrimitives.Blake2bKeyed(root, FdSecPrimitives.Ascii(KekLabel))
            Finally
                FdSecPrimitives.Wipe(root)
            End Try
        End SyncLock

        If cache IsNot Nothing Then cache.Put(credentialUtf8, salt, maskKey, kek)
    End Sub

    Private ReadOnly derivationGate As New Object()

End Module
#End If
