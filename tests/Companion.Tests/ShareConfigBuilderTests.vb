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

End Class
