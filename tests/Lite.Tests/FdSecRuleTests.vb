#If Not NETFRAMEWORK Then
Option Strict On

Imports System.IO
Imports System.Linq
Imports System.Text
Imports Xunit

''' <summary>
''' The rules that outrank convenience
''' (026_SPECIFICATION_SECURE_CONTAINER_FD_SEC_DOTNET10.md §5.4, §12, §13 "Rule tests",
''' R-4).
'''
''' Each one is cheap, and each one guards something that is easy to lose in a later
''' refactor and expensive to notice: a decrypted byte reaching the disk looks like
''' nothing at all until somebody goes looking through free space, and a merged pair of
''' outcome messages looks like an improvement.
'''
''' Modern-only: on the net48 leg src/Secure and this file compile to nothing.
''' </summary>
Public Class FdSecRuleTests

    Private Const Credential As String = "a credential that must never be logged"
    Private Const TrueName As String = "top-secret-photograph.jpg"

    Private Shared Function PackedSecret() As Byte()
        Return FdSecTestContainer.Pack(Encoding.UTF8.GetBytes("the plaintext nobody may see"),
                                       TrueName, Credential, FdSecTestContainer.FastProfile)
    End Function

    Private Shared Function OpenPacked(packed As Byte(), credential As String) As FdSecContainer
        Return FdSecContainer.Open(New MemoryStream(packed, writable:=False), credential,
                                   ownsSource:=True, profile:=FdSecTestContainer.FastProfile)
    End Function

    ' ------------------------------------------------- no plaintext on the disk ----

    ''' <summary>
    ''' RULE 1: the decode cache never sees a container.
    '''
    ''' It writes decoded payloads to .fmsdec files for exactly the formats we decode
    ''' ourselves - WEBP, AVIF, HEIC - which are exactly the kinds of image a container is
    ''' likely to hold, and such a file IS the plaintext. The only thing that routes a path
    ''' into DecodeCache is FileManager.LoadImageWithStream asking whether its extension is
    ''' in this set, so keeping ".fd-sec" out of it is the whole mechanism. A future
    ''' maintainer adding it here to "support containers" is the failure this guards.
    ''' </summary>
    <Fact>
    Public Sub The_decode_cache_can_never_be_reached_for_a_container()
        Assert.DoesNotContain(FdSecFormat.Extension, ImageDecoderProvider.Decoder_Backed_Extensions)
    End Sub

    ''' <summary>
    ''' RULE 5: no failure message names the credential or the sealed true name.
    '''
    ''' The true name is the thing the format exists to hide, and a message is the shortest
    ''' path from it into a log file. Every class of refusal is exercised here, because the
    ''' one that leaks will be whichever one nobody thought about.
    ''' </summary>
    <Fact>
    Public Sub No_refusal_names_the_credential_or_the_true_name()
        Dim leaked As New List(Of String)()

        ' A wrong credential.
        Collect(leaked, Sub() OpenPacked(PackedSecret(), "the wrong password").Dispose())

        ' A flipped bit in the sealed metadata - past the head, so a different code path.
        Dim tampered As Byte() = PackedSecret()
        tampered(FdSecFormat.HeadSize + 3) = tampered(FdSecFormat.HeadSize + 3) Xor CByte(1)
        Collect(leaked, Sub() OpenPacked(tampered, Credential).Dispose())

        ' A truncation - the damaged class, which knows the metadata and therefore the name.
        Dim packed As Byte() = PackedSecret()
        Dim shortened(packed.Length - 512 - 1) As Byte
        Array.Copy(packed, shortened, shortened.Length)
        Collect(leaked, Sub() OpenPacked(shortened, Credential).Dispose())

        ' A chunk that does not authenticate, reported while reading rather than opening.
        Dim chunkBroken As Byte() = PackedSecret()
        Dim chunkAt As Integer = FdSecTestContainer.ChunkOffset(0, 512, 1024) + 2
        chunkBroken(chunkAt) = chunkBroken(chunkAt) Xor CByte(1)
        Collect(leaked, Sub()
                            Using container As FdSecContainer = OpenPacked(chunkBroken, Credential)
                                container.VerifyAll()
                            End Using
                        End Sub)

        Assert.Empty(leaked)
    End Sub

    ''' <summary>Runs an attempt that must fail, and records anything its message gave
    ''' away. A message that is missing entirely would be a different defect, so an
    ''' attempt that unexpectedly succeeds is recorded too.</summary>
    Private Shared Sub Collect(leaked As List(Of String), attempt As Action)
        Try
            attempt()
            leaked.Add("the attempt did not fail at all")
        Catch ex As FdSecException
            Dim text As String = ex.Message
            If text.IndexOf(Credential, StringComparison.OrdinalIgnoreCase) >= 0 Then
                leaked.Add("the credential appears in: " & text)
            End If
            If text.IndexOf("top-secret", StringComparison.OrdinalIgnoreCase) >= 0 OrElse
               text.IndexOf(TrueName, StringComparison.OrdinalIgnoreCase) >= 0 Then
                leaked.Add("the true name appears in: " & text)
            End If
        End Try
    End Sub

    ''' <summary>
    ''' The same rule, from the other side: the WORDS the user is shown name no file at
    ''' all. A message that helpfully said which file failed would put the sealed name on
    ''' screen for a container that has not even authenticated.
    ''' </summary>
    <Theory>
    <InlineData(FdSecOutcome.Ok)>
    <InlineData(FdSecOutcome.CredentialOrTamper)>
    <InlineData(FdSecOutcome.Damaged)>
    <InlineData(FdSecOutcome.Unsupported)>
    Public Sub No_outcome_message_carries_a_placeholder_for_a_name(outcome As FdSecOutcome)
        Dim message As String = SecureMessages.ForOutcome(outcome)
        Assert.DoesNotContain("{0}", message)
    End Sub

    ' ------------------------------------------- the outcome classes never merge ----

    ''' <summary>
    ''' EXHAUSTIVE BY TYPE (§14 Ф-S2): every value of the enum has a message, and no two
    ''' classes share one. The second half is the point - a refactor that "simplified" two
    ''' of these into one sentence would pass a test that only checked for non-empty.
    ''' </summary>
    <Fact>
    Public Sub Every_outcome_class_has_its_own_message()
        Dim values As FdSecOutcome() = CType([Enum].GetValues(GetType(FdSecOutcome)), FdSecOutcome())
        Assert.Equal(4, values.Length)

        Dim seen As New Dictionary(Of String, FdSecOutcome)(StringComparer.Ordinal)
        For Each outcome As FdSecOutcome In values
            Dim message As String = SecureMessages.ForOutcome(outcome)
            Assert.False(String.IsNullOrWhiteSpace(message), outcome.ToString() & " has no message")

            Dim other As FdSecOutcome
            If seen.TryGetValue(message, other) Then
                Assert.Fail(outcome.ToString() & " and " & other.ToString() &
                            " share one message - the three outcome classes must never merge")
            End If
            seen(message) = outcome
        Next
    End Sub

    ''' <summary>
    ''' The three classes stay three on the same file, whichever way it is broken. Written
    ''' as one table so that a change which quietly routed one class into another has to
    ''' fail here rather than in a code review nobody ran.
    ''' </summary>
    <Fact>
    Public Sub The_three_classes_stay_three()
        ' Wrong credential -> class one, never "damaged".
        Assert.Equal(FdSecOutcome.CredentialOrTamper, OutcomeOf(PackedSecret(), "wrong"))

        ' Truncated -> damaged, and the password is not blamed.
        Dim packed As Byte() = PackedSecret()
        Dim shortened(packed.Length - 512 - 1) As Byte
        Array.Copy(packed, shortened, shortened.Length)
        Assert.Equal(FdSecOutcome.Damaged, OutcomeOf(shortened, Credential))

        ' Too short to be a container at all -> damaged, and asked for no credential:
        ' the screen runs before any dialog can exist.
        Assert.False(FdSecScreen.CouldBeContainer(FdSecScreen.SmallestContainer - 512))
    End Sub

    Private Shared Function OutcomeOf(packed As Byte(), credential As String) As FdSecOutcome
        Try
            Using container As FdSecContainer = OpenPacked(packed, credential)
                container.VerifyAll()
                Return FdSecOutcome.Ok
            End Using
        Catch ex As FdSecException
            Return ex.Outcome
        End Try
    End Function

    ' ----------------------------------------------------------- one classifier ----

    ''' <summary>
    ''' A container is its own kind until it opens, and its sealed TRUE name then goes
    ''' through the very same classifier - so there is still one answer to "what kind of
    ''' file is this" and not two (§5.3).
    ''' </summary>
    <Fact>
    Public Sub A_container_is_its_own_kind_until_the_true_name_reclassifies_it()
        Assert.Equal(MediaKind.Container, MediaKindClassifier.KindOf(FdSecFormat.Extension))

        Using container As FdSecContainer = OpenPacked(PackedSecret(), Credential)
            Assert.Equal(".jpg", container.Metadata.ContentExtension())
            Assert.Equal(MediaKind.Image, MediaKindClassifier.KindOf(container.Metadata.ContentExtension()))
            Assert.Equal(TrueName, container.Metadata.Name)
        End Using
    End Sub

    ''' <summary>
    ''' One row per class the 1.1 name rule refuses (FDSEC-FORMAT §7, FDSEC-BEHAVIOUR §4.3
    ''' and §5.2 step 6). Built in code because a control character cannot be written into
    ''' an attribute argument.
    ''' </summary>
    Public Shared ReadOnly Property RefusedNames As TheoryData(Of String)
        Get
            Return New TheoryData(Of String) From {
                "",                                     ' empty
                ".",                                    ' relative directory reference
                "..",
                "..\..\windows\system32\evil.jpg",      ' backslash separator
                "a/b.png",                              ' forward-slash separator
                "a:b.jpg",                              ' colon - a volume or an alternate data stream
                "a*.jpg",                               ' wildcards
                "a?.jpg",
                "a" & ChrW(1) & ".jpg",                 ' control character below 32
                "a" & ChrW(10) & ".jpg",
                "a" & ChrW(127) & ".jpg",               ' DEL
                "CON",                                  ' reserved device names
                "nul.txt",                              ' ...with an extension, still the device
                "COM1.mp4",
                "lpt9",
                "photo.jpg.",                           ' trailing dot
                "photo.jpg "                            ' trailing space
            }
        End Get
    End Property

    ''' <summary>
    ''' The sealed true name is attacker-controlled the moment somebody else made the
    ''' container (FDSEC-BEHAVIOUR §14.1). Under 1.1 a name that could impersonate a path
    ''' or a device is REFUSED as damaged - never rewritten into something displayable, and
    ''' never reported as a wrong password: slot 0 and the metadata tag have both
    ''' authenticated by the time the name is read.
    ''' </summary>
    <Theory>
    <MemberData(NameOf(RefusedNames))>
    Public Sub An_unsafe_true_name_is_refused_as_damaged(name As String)
        Assert.False(FdSecMetadata.IsAcceptableName(name))

        Dim packed As Byte() = FdSecTestContainer.Pack(New Byte(31) {}, name, Credential,
                                                       FdSecTestContainer.FastProfile)
        Dim failure As FdSecException = Assert.Throws(Of FdSecException)(
            Sub() OpenPacked(packed, Credential).Dispose())
        Assert.Equal(FdSecOutcome.Damaged, failure.Outcome)
        ' Refused, never echoed: the name is the thing the format exists to hide.
        If name.Length > 2 Then Assert.DoesNotContain(name, failure.Message)
    End Sub

    ''' <summary>
    ''' The other side of the rule: names that merely LOOK close to a refused class still
    ''' open. COM10 is not a device, a leading dot is not a trailing one, and a name in
    ''' another script is just a name.
    '''
    ''' The four characters Windows refuses in a file name but the container format does not
    ''' (quote, less-than, greater-than, bar) are pinned open on purpose, FDSEC decision of
    ''' 2026-10-19: a sealed name that is close to a refused class still opens, and the reader
    ''' makes no platform-specific damage check, so this row set must not turn into a refusal.
    ''' </summary>
    <Theory>
    <InlineData("com10.log")>
    <InlineData("CONSOLE.jpg")>
    <InlineData(".hidden.jpg")>
    <InlineData("a b.jpg")>
    <InlineData("фото отпуск.jpg")>
    <InlineData("a""b.jpg")>
    <InlineData("a<b.jpg")>
    <InlineData("a>b.jpg")>
    <InlineData("a|b.jpg")>
    Public Sub A_name_close_to_a_refused_class_still_opens(name As String)
        Assert.True(FdSecMetadata.IsAcceptableName(name))
        Dim packed As Byte() = FdSecTestContainer.Pack(New Byte(31) {}, name, Credential,
                                                       FdSecTestContainer.FastProfile)
        Using container As FdSecContainer = OpenPacked(packed, Credential)
            Assert.Equal(name, container.Metadata.Name)
        End Using
    End Sub

    ''' <summary>
    ''' The sealed name is parsed BY HAND, never through System.IO.Path - it was never a
    ''' path. "a:b.jpg" is the shape that shows the difference: Path reads the colon as a
    ''' volume separator and hands back a shorter name than it was given, which could
    ''' have slipped past a rule written against Path's answer. Ours sees the colon.
    ''' </summary>
    <Fact>
    Public Sub The_sealed_name_is_judged_by_hand_and_not_through_Path()
        Assert.NotEqual("a:b.jpg", Path.GetFileName("a:b.jpg"))
        Assert.True(FdSecMetadata.IsAcceptableName(Path.GetFileName("a:b.jpg")))
        Assert.False(FdSecMetadata.IsAcceptableName("a:b.jpg"))
    End Sub

    ' ---------------------------------------------------------- impossible sizes ----

    ''' <summary>
    ''' 1.1 (FDSEC-FORMAT §7, §11 steps 6-7): a sealed size above 2^63-1, or one whose
    ''' L_min does not fit in a signed 64-bit integer, is DAMAGED - decided before the
    ''' arithmetic, so the refusal is an outcome class and never an OverflowException
    ''' surfacing as a generic I/O failure. OutcomeOf catches FdSecException only, so an
    ''' overflow escaping would fail these rows rather than pass them.
    ''' </summary>
    <Theory>
    <InlineData(9223372036854775808UL)>     ' 2^63, one above Long.MaxValue
    <InlineData(18446744073709551615UL)>    ' ULong.MaxValue
    <InlineData(9223372036854775807UL)>     ' Long.MaxValue: representable, but L_min is not
    <InlineData(9079256848778914896UL)>     ' L_min fits (S=1024, A=512), L_min + A does not
    <InlineData(1099511627776UL)>           ' 1 TiB: fits, and the file is simply far too short
    Public Sub An_impossible_sealed_size_is_damaged(sealedSize As ULong)
        Dim packed As Byte() = FdSecTestContainer.Pack(New Byte(31) {}, "a.bin", Credential,
                                                       FdSecTestContainer.FastProfile,
                                                       sealedSize:=sealedSize)
        Assert.Equal(FdSecOutcome.Damaged, OutcomeOf(packed, Credential))
    End Sub

    ' ------------------------------------------------------- credential hygiene ----

    ''' <summary>
    ''' The key cache is bound to the credential it was filled under, so a cache carried
    ''' across a change of credential MISSES instead of handing back keys that authenticate
    ''' nothing - which would be reported as "wrong password" for the right password, the
    ''' single most confusing failure this feature could produce.
    ''' </summary>
    <Fact>
    Public Sub The_key_cache_is_bound_to_its_credential()
        Dim packed As Byte() = PackedSecret()
        Dim cache As New FdSecKeyCache()

        Using first As FdSecContainer = FdSecContainer.Open(New MemoryStream(packed, writable:=False), Credential,
                                                             ownsSource:=True, profile:=FdSecTestContainer.FastProfile,
                                                             keyCache:=cache)
            Assert.Equal(TrueName, first.Metadata.Name)
        End Using

        ' The same cache, a different credential: it must still refuse, and it must refuse
        ' as the first class rather than opening with the cached keys.
        Dim failure As FdSecException = Assert.Throws(Of FdSecException)(
            Sub() FdSecContainer.Open(New MemoryStream(packed, writable:=False), "something else",
                                      ownsSource:=True, profile:=FdSecTestContainer.FastProfile,
                                      keyCache:=cache).Dispose())
        Assert.Equal(FdSecOutcome.CredentialOrTamper, failure.Outcome)

        ' And the right credential still opens it, out of the cache this time.
        Using again As FdSecContainer = FdSecContainer.Open(New MemoryStream(packed, writable:=False), Credential,
                                                             ownsSource:=True, profile:=FdSecTestContainer.FastProfile,
                                                             keyCache:=cache)
            Assert.Equal(TrueName, again.Metadata.Name)
        End Using

        cache.Clear()
    End Sub

    ''' <summary>
    ''' Clearing the cache really drops it: after Clear the next open derives again. Proven
    ''' by the only observable that does not require reading the process's memory - a
    ''' container the cache could not possibly have an entry for still opens correctly,
    ''' and one it did have an entry for opens correctly too.
    ''' </summary>
    <Fact>
    Public Sub Clearing_the_key_cache_leaves_everything_still_openable()
        Dim packed As Byte() = PackedSecret()
        Dim cache As New FdSecKeyCache()

        FdSecContainer.Open(New MemoryStream(packed, writable:=False), Credential,
                            ownsSource:=True, profile:=FdSecTestContainer.FastProfile, keyCache:=cache).Dispose()
        cache.Clear()

        Using after As FdSecContainer = FdSecContainer.Open(New MemoryStream(packed, writable:=False), Credential,
                                                             ownsSource:=True, profile:=FdSecTestContainer.FastProfile,
                                                             keyCache:=cache)
            Assert.Equal(TrueName, after.Metadata.Name)
        End Using
    End Sub

End Class
#End If
