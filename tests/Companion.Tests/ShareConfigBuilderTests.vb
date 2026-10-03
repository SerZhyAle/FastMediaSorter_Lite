Option Strict On

Imports System.Collections.Generic
Imports System.Text.Json
Imports FastMediaSorterCompanion
Imports Xunit

''' <summary>
''' The .fmscfg export contract (frozen - SPECIFICATION_QR_IMPORT_ANDROID.md). The
''' Android client is released against exactly this shape, so these tests guard the
''' hand-built JSON: required keys, ordered accessPaths (lan/ipv6/portforward), the
''' password-exclusion safeguard, and per-root readOnly. Built from in-memory status
''' - no worker, fully deterministic.
''' </summary>
Public Class ShareConfigBuilderTests

    Private Shared Function RunningStatus(Optional reach As WorkerReachability = Nothing) As WorkerStatus
        Return New WorkerStatus With {
            .Running = True,
            .ListenPort = 2222,
            .Username = "fms",
            .Password = "secret",
            .Fingerprint = "SHA256:abc123",
            .Roots = New List(Of ShareFolder) From {
                New ShareFolder With {.name = "Photos", .hostPath = "C:\__fms_test__" & Guid.NewGuid().ToString("N"), .readOnly = False}
            },
            .Reachability = If(reach, New WorkerReachability With {.LanAddress = "192.168.1.50"})
        }
    End Function

    <Fact>
    Public Sub Build_ReturnsNothing_WhenNotServable()
        Assert.Null(ShareConfigBuilder.Build(Nothing, includeExternal:=True))
        Assert.Null(ShareConfigBuilder.Build(New WorkerStatus With {.Running = False}, includeExternal:=True))
        Assert.Null(ShareConfigBuilder.Build(New WorkerStatus With {.Running = True, .ListenPort = 0}, includeExternal:=True))
    End Sub

    <Fact>
    Public Sub Build_LanOnly_HasRequiredKeysAndQr()
        Dim res = ShareConfigBuilder.Build(RunningStatus(), includeExternal:=False, includePassword:=True)
        Assert.NotNull(res)
        Assert.Equal("192.168.1.50:2222", res.LanDisplay)
        Assert.False(res.HasExternal)
        Assert.False(res.HasIpv6)
        Assert.False(res.QrOverflow)
        Assert.NotNull(res.QrPng)
        Assert.True(res.QrPng.Length > 0)

        Using doc = JsonDocument.Parse(res.ConfigJson)
            Dim root = doc.RootElement
            Assert.Equal("sftp", root.GetProperty("protocol").GetString())
            Assert.Equal("fms", root.GetProperty("username").GetString())
            Assert.Equal("secret", root.GetProperty("password").GetString())
            Assert.Equal("SHA256:abc123", root.GetProperty("hostKeyFingerprintSha256").GetString())

            Dim paths = root.GetProperty("accessPaths")
            Assert.Equal(1, paths.GetArrayLength())
            Assert.Equal("lan", paths(0).GetProperty("kind").GetString())
            Assert.Equal("192.168.1.50", paths(0).GetProperty("host").GetString())
            Assert.Equal(2222, paths(0).GetProperty("port").GetInt32())

            Dim roots = root.GetProperty("roots")
            Assert.Equal(1, roots.GetArrayLength())
            Assert.Equal("/Photos", roots(0).GetProperty("virtualPath").GetString())
            Assert.Equal("Photos", roots(0).GetProperty("label").GetString())
            Assert.False(roots(0).GetProperty("readOnly").GetBoolean())  ' default folder is writable
        End Using
    End Sub

    <Fact>
    Public Sub Build_ExcludePassword_LeavesPasswordEmpty()
        Dim res = ShareConfigBuilder.Build(RunningStatus(), includeExternal:=False, includePassword:=False)
        Assert.NotNull(res)
        Using doc = JsonDocument.Parse(res.ConfigJson)
            Assert.Equal("", doc.RootElement.GetProperty("password").GetString())
            ' Username still present so the phone can prefill it.
            Assert.Equal("fms", doc.RootElement.GetProperty("username").GetString())
        End Using
    End Sub

    <Fact>
    Public Sub Build_Internet_OrdersLanThenIpv6ThenPortforward()
        Dim reach As New WorkerReachability With {
            .LanAddress = "192.168.1.50",
            .Ipv6Address = "2001:db8::1",
            .ExternalHost = "203.0.113.5",
            .ExternalPort = 2222,
            .IsCgnat = False
        }
        Dim res = ShareConfigBuilder.Build(RunningStatus(reach), includeExternal:=True)
        Assert.NotNull(res)
        Assert.True(res.HasExternal)
        Assert.True(res.HasIpv6)
        Using doc = JsonDocument.Parse(res.ConfigJson)
            Dim paths = doc.RootElement.GetProperty("accessPaths")
            Assert.Equal(3, paths.GetArrayLength())
            Assert.Equal("lan", paths(0).GetProperty("kind").GetString())
            Assert.Equal("ipv6", paths(1).GetProperty("kind").GetString())
            Assert.Equal("portforward", paths(2).GetProperty("kind").GetString())
        End Using
    End Sub

    <Fact>
    Public Sub Build_AppliesPerRecipientOverrides()
        ' The package wizard's per-recipient overrides (PIN / slideshow / soft-RO) must
        ' ride in THIS export's roots (§4.5.3), on top of the share's own defaults.
        Dim ov As New ShareExportOverrides With {.HasPin = True, .Pin = "4321", .HasSlideshow = True, .SlideshowInterval = 7, .ForceSoftReadOnly = True}
        Dim res = ShareConfigBuilder.Build(RunningStatus(), includeExternal:=False, includePassword:=True, exportOverrides:=ov)
        Assert.NotNull(res)
        Using doc = JsonDocument.Parse(res.ConfigJson)
            Dim r0 = doc.RootElement.GetProperty("roots")(0)
            Assert.True(r0.GetProperty("readOnly").GetBoolean())          ' soft RO -> advertised read-only
            Assert.Equal("4321", r0.GetProperty("accessPin").GetString()) ' PIN override
            Assert.Equal(7, r0.GetProperty("slideshowInterval").GetInt32()) ' slideshow override
            Assert.Equal(2, doc.RootElement.GetProperty("schemaVersion").GetInt32()) ' v2 fields present
        End Using
    End Sub

    <Fact>
    Public Sub Build_PerRootOverrides_AreIndependent()
        ' Each folder in ONE access code can carry its OWN PIN / read-only / slideshow
        ' (§4.5.3, the wizard's per-folder table), keyed by hostPath. A root with no map
        ' entry keeps its stored defaults - the two roots diverge in the same export.
        Dim hostA As String = "C:\__fms_test__" & Guid.NewGuid().ToString("N")
        Dim hostB As String = "C:\__fms_test__" & Guid.NewGuid().ToString("N")
        Dim st As New WorkerStatus With {
            .Running = True, .ListenPort = 2222, .Username = "fms", .Password = "secret", .Fingerprint = "SHA256:abc123",
            .Roots = New List(Of ShareFolder) From {
                New ShareFolder With {.name = "Alpha", .hostPath = hostA, .readOnly = False},
                New ShareFolder With {.name = "Bravo", .hostPath = hostB, .readOnly = False}
            },
            .Reachability = New WorkerReachability With {.LanAddress = "192.168.1.50"}
        }
        Dim perRoot As New Dictionary(Of String, ShareExportOverrides)(StringComparer.OrdinalIgnoreCase) From {
            {hostA, New ShareExportOverrides With {.HasPin = True, .Pin = "1111", .ForceSoftReadOnly = True}}
        }
        Dim res = ShareConfigBuilder.Build(st, includeExternal:=False, includePassword:=True, perRootOverrides:=perRoot)
        Assert.NotNull(res)
        Using doc = JsonDocument.Parse(res.ConfigJson)
            Dim roots = doc.RootElement.GetProperty("roots")
            Assert.Equal(2, roots.GetArrayLength())
            ' Root A (has override): PIN present + advertised read-only.
            Assert.Equal("1111", roots(0).GetProperty("accessPin").GetString())
            Assert.True(roots(0).GetProperty("readOnly").GetBoolean())
            ' Root B (no override): writable, no PIN emitted.
            Assert.False(roots(1).GetProperty("readOnly").GetBoolean())
            Dim pinB As JsonElement = Nothing
            Assert.False(roots(1).TryGetProperty("accessPin", pinB))
        End Using
    End Sub

    <Fact>
    Public Sub Build_PerRootParams_UsedAsIs()
        ' The wizard's editable grid hands the builder a FULL ShareRootParams per folder
        ' (perRootParams). That complete object is emitted as-is (highest precedence), so a
        ' recipient gets exactly the per-folder configuration the grid shows.
        Dim hostA As String = "C:\__fms_test__" & Guid.NewGuid().ToString("N")
        Dim hostB As String = "C:\__fms_test__" & Guid.NewGuid().ToString("N")
        Dim st As New WorkerStatus With {
            .Running = True, .ListenPort = 2222, .Username = "fms", .Password = "secret", .Fingerprint = "SHA256:abc123",
            .Roots = New List(Of ShareFolder) From {
                New ShareFolder With {.name = "Alpha", .hostPath = hostA, .readOnly = False},
                New ShareFolder With {.name = "Bravo", .hostPath = hostB, .readOnly = False}
            },
            .Reachability = New WorkerReachability With {.LanAddress = "192.168.1.50"}
        }
        Dim perRoot As New Dictionary(Of String, ShareRootParams)(StringComparer.OrdinalIgnoreCase) From {
            {hostA, New ShareRootParams With {.IsReadOnly = True, .AccessPin = "9999", .Profile = "photo_storage", .Label = "Отпуск"}}
        }
        Dim res = ShareConfigBuilder.Build(st, includeExternal:=False, includePassword:=True, perRootParams:=perRoot)
        Assert.NotNull(res)
        Using doc = JsonDocument.Parse(res.ConfigJson)
            Dim roots = doc.RootElement.GetProperty("roots")
            Assert.Equal(2, roots.GetArrayLength())
            ' Root A: full params applied verbatim.
            Assert.Equal("Отпуск", roots(0).GetProperty("label").GetString())
            Assert.True(roots(0).GetProperty("readOnly").GetBoolean())
            Assert.Equal("9999", roots(0).GetProperty("accessPin").GetString())
            Assert.Equal("photo_storage", roots(0).GetProperty("profile").GetString())
            ' Root B: no grid entry -> stored defaults (writable, no PIN, folder-name label).
            Assert.Equal("Bravo", roots(1).GetProperty("label").GetString())
            Assert.False(roots(1).GetProperty("readOnly").GetBoolean())
        End Using
    End Sub

    <Fact>
    Public Sub Build_LanOnlyToggle_OmitsExternalAddresses()
        ' includeExternal:=False must not leak any externally-routable address (privacy).
        Dim reach As New WorkerReachability With {
            .LanAddress = "192.168.1.50", .Ipv6Address = "2001:db8::1",
            .ExternalHost = "203.0.113.5", .ExternalPort = 2222, .IsCgnat = False
        }
        Dim res = ShareConfigBuilder.Build(RunningStatus(reach), includeExternal:=False)
        Using doc = JsonDocument.Parse(res.ConfigJson)
            Dim paths = doc.RootElement.GetProperty("accessPaths")
            Assert.Equal(1, paths.GetArrayLength())
            Assert.Equal("lan", paths(0).GetProperty("kind").GetString())
        End Using
    End Sub

    ' --- advertised writability may never be looser than the server (ticket 054 H-07) --

    ''' <summary>The wizard's per-code grid used to be exported AS-IS: an untouched grid on
    ''' a folder the server serves READ-ONLY advertised readOnly:false - exactly the
    ''' "Move deletes the original, then rm is denied" bug the FMSCFG producer rule exists
    ''' to prevent.</summary>
    <Fact>
    Public Sub Build_AWritablePromiseOnAServerReadOnlyRoot_IsClamped()
        Dim st As WorkerStatus = RunningStatus()
        st.Roots(0).readOnly = True
        Dim grid As New Dictionary(Of String, ShareRootParams) From {
            {st.Roots(0).hostPath, New ShareRootParams With {.IsReadOnly = False, .IsDestination = True}}
        }
        Dim res = ShareConfigBuilder.Build(st, includeExternal:=False, perRootParams:=grid)
        Assert.NotNull(res)
        Using doc = JsonDocument.Parse(res.ConfigJson)
            Dim root = doc.RootElement.GetProperty("roots")(0)
            Assert.True(root.GetProperty("readOnly").GetBoolean())
            ' A read-only root is never a destination either.
            Dim destination As JsonElement = Nothing
            Assert.False(root.TryGetProperty("isDestination", destination) AndAlso destination.GetBoolean())
        End Using
    End Sub

    ''' <summary>A stricter code is allowed: claiming read-only on a writable share is a
    ''' promise the phone keeps, not one the server breaks.</summary>
    <Fact>
    Public Sub Build_GridReadOnlyOnAWritableRoot_IsAdvertised()
        Dim st As WorkerStatus = RunningStatus()
        st.Roots(0).readOnly = False
        Dim grid As New Dictionary(Of String, ShareRootParams) From {
            {st.Roots(0).hostPath, New ShareRootParams With {.IsReadOnly = True}}
        }
        Dim res = ShareConfigBuilder.Build(st, includeExternal:=False, perRootParams:=grid)
        Using doc = JsonDocument.Parse(res.ConfigJson)
            Assert.True(doc.RootElement.GetProperty("roots")(0).GetProperty("readOnly").GetBoolean())
        End Using
    End Sub

    ' --- ticket 034: the FMSCFG producer against the contract ------------------------

    ''' <summary>Phase 2 - FMSCFG 2.1 amendment A resolves isDestination:true to writable, so a
    ''' soft read-only hint on a destination was exported as a state the phone reads as
    ''' writable while every surface here said read-only. A destination now ignores the hint.</summary>
    <Fact>
    Public Sub Build_SoftReadOnlyDestination_ExportsWritable()
        Dim st As WorkerStatus = RunningStatus()
        Dim grid As New Dictionary(Of String, ShareRootParams) From {
            {st.Roots(0).hostPath, New ShareRootParams With {.SoftReadOnly = True, .IsDestination = True}}
        }
        Dim res = ShareConfigBuilder.Build(st, includeExternal:=False, perRootParams:=grid)
        Assert.NotNull(res)
        Using doc = JsonDocument.Parse(res.ConfigJson)
            Dim root = doc.RootElement.GetProperty("roots")(0)
            Assert.False(root.GetProperty("readOnly").GetBoolean())
            Assert.True(root.GetProperty("isDestination").GetBoolean())
        End Using
    End Sub

    ''' <summary>Phase 1 - FMSCFG rule 6: the diagnostic log line may carry neither the
    ''' password nor any accessPin; that log is packed by "send logs to the author".</summary>
    <Fact>
    Public Sub RedactSecrets_RemovesPasswordAndEveryPin()
        Dim hostB As String = "C:\__fms_test__" & Guid.NewGuid().ToString("N")
        Dim st As WorkerStatus = RunningStatus()
        st.Password = "pa""ss\word-7731"
        st.Roots.Add(New ShareFolder With {.name = "Video", .hostPath = hostB, .readOnly = False})
        Dim grid As New Dictionary(Of String, ShareRootParams) From {
            {st.Roots(0).hostPath, New ShareRootParams With {.AccessPin = "4815"}},
            {hostB, New ShareRootParams With {.AccessPin = "1623"}}
        }
        Dim res = ShareConfigBuilder.Build(st, includeExternal:=False, perRootParams:=grid)
        Assert.NotNull(res)
        Assert.Contains("4815", res.ConfigJson)   ' the payload itself carries them by design

        Dim logged As String = ShareConfigBuilder.RedactSecrets(res.ConfigJson)
        Assert.DoesNotContain("7731", logged)
        Assert.DoesNotContain("4815", logged)
        Assert.DoesNotContain("1623", logged)
        ' Still valid JSON with the non-secret fields intact - a useful diagnostic.
        Using doc = JsonDocument.Parse(logged)
            Assert.Equal("***", doc.RootElement.GetProperty("password").GetString())
            Assert.Equal("***", doc.RootElement.GetProperty("roots")(1).GetProperty("accessPin").GetString())
            Assert.Equal("fms", doc.RootElement.GetProperty("username").GetString())
        End Using
    End Sub

    ''' <summary>Phase 1 - a saved .fmscfg is owner-only, like the reference exporter's.</summary>
    <Fact>
    Public Sub WriteConfigFile_IsOwnerOnly()
        Dim dir As String = PrivateTestFolders.Create("fms-034-")
        Try
            Dim path As String = IO.Path.Combine(dir, "t.fmscfg")
            ShareConfigBuilder.WriteConfigFile(path, "{""a"":1}")
            Assert.Equal("{""a"":1}", IO.File.ReadAllText(path))
            Dim acl = IO.FileSystemAclExtensions.GetAccessControl(New IO.FileInfo(path))
            Assert.True(acl.AreAccessRulesProtected)
            Dim meSid = Security.Principal.WindowsIdentity.GetCurrent().User
            For Each rule As Security.AccessControl.FileSystemAccessRule In acl.GetAccessRules(True, True, GetType(Security.Principal.SecurityIdentifier))
                Assert.Equal(meSid, rule.IdentityReference)
            Next
        Finally
            Try : IO.Directory.Delete(dir, recursive:=True) : Catch : End Try
        End Try
    End Sub

    ''' <summary>Phase 1 - the email hand-off's leftovers go; anything else in the folder stays.</summary>
    <Fact>
    Public Sub DeleteStaleConfigCopies_RemovesOnlyConfigCopies()
        Dim dir As String = IO.Path.Combine(IO.Path.GetTempPath(), "fms-034-" & Guid.NewGuid().ToString("N"))
        IO.Directory.CreateDirectory(IO.Path.Combine(dir, "mail-abc"))
        Try
            IO.File.WriteAllText(IO.Path.Combine(dir, "FastMediaSorter.fmscfg"), "x")
            IO.File.WriteAllText(IO.Path.Combine(dir, "mail-abc", "FastMediaSorter.fmscfg"), "x")
            IO.File.WriteAllText(IO.Path.Combine(dir, "other.zip"), "x")
            ShareConfigBuilder.DeleteStaleConfigCopies(dir)
            Assert.Empty(IO.Directory.GetFiles(dir, "*.fmscfg", IO.SearchOption.AllDirectories))
            Assert.True(IO.File.Exists(IO.Path.Combine(dir, "other.zip")))
        Finally
            Try : IO.Directory.Delete(dir, recursive:=True) : Catch : End Try
        End Try
    End Sub

    ''' <summary>Phase 4 - pins TODAY's behaviour while the owner decides (registry exception):
    ''' a port-forward the worker's connect-back found closed is still exported, and the
    ''' accessNote says so. If the owner keeps "a dead entry is a bug", this test flips.</summary>
    <Fact>
    Public Sub Build_CheckedAndClosedPortforward_IsStillExported_PendingOwner()
        Dim reach As New WorkerReachability With {
            .LanAddress = "192.168.1.50", .ExternalHost = "203.0.113.5", .ExternalPort = 2222,
            .IsCgnat = False, .ExternalPortChecked = True, .ExternalPortOpen = False
        }
        Dim res = ShareConfigBuilder.Build(RunningStatus(reach), includeExternal:=True)
        Assert.NotNull(res)
        Assert.True(res.HasExternal)
        Assert.NotEqual("", res.AccessNote)
        Using doc = JsonDocument.Parse(res.ConfigJson)
            Dim paths = doc.RootElement.GetProperty("accessPaths")
            Assert.Equal("portforward", paths(paths.GetArrayLength() - 1).GetProperty("kind").GetString())
        End Using
    End Sub

End Class
