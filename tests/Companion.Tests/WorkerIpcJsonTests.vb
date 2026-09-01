Option Strict On

Imports System.Collections.Generic
Imports System.Text.Json
Imports System.Text.Json.Serialization
Imports FastMediaSorterCompanion
Imports Xunit

''' <summary>
''' Frozen named-pipe wire contract (SPECIFICATION_ANDROID_FOLDER_SHARE.md Appendix A).
''' These pin the exact JSON field names WorkerIpc emits/consumes; a DTO rename would
''' silently break the worker handshake, so it must break a test instead. Mirrors the
''' options WorkerIpc.Send uses (verbatim names on write, case-insensitive on read).
''' </summary>
Public Class WorkerIpcJsonTests

    Private Shared ReadOnly WriteOpts As New JsonSerializerOptions With {
        .DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    }
    Private Shared ReadOnly ReadOpts As New JsonSerializerOptions With {
        .PropertyNameCaseInsensitive = True
    }

    <Fact>
    Public Sub SetSharedFolders_Request_HasExactWireShape()
        Dim req As New WorkerRequest With {
            .type = "SetSharedFolders",
            .folders = New List(Of ShareFolder) From {
                New ShareFolder With {.name = "Photos", .hostPath = "C:\pics", .readOnly = False}
            }
        }
        Dim json As String = JsonSerializer.Serialize(req, WriteOpts)
        Assert.Contains("""schemaVersion"":1", json)
        Assert.Contains("""type"":""SetSharedFolders""", json)
        Assert.Contains("""name"":""Photos""", json)
        Assert.Contains("""hostPath"":""C:\\pics""", json)
        Assert.Contains("""readOnly"":false", json)
    End Sub

    <Fact>
    Public Sub GetStatus_Request_OmitsNullFolders()
        Dim json As String = JsonSerializer.Serialize(New WorkerRequest With {.type = "GetStatus"}, WriteOpts)
        Assert.DoesNotContain("folders", json)  ' WhenWritingNull - only SetSharedFolders carries folders
    End Sub

    <Fact>
    Public Sub Response_DeserializesCamelCase_IntoPascalDtos()
        Dim wire As String =
            "{""schemaVersion"":1,""ok"":true,""status"":{""running"":true,""listenPort"":2222," &
            """fingerprint"":""SHA256:abc"",""username"":""fms"",""password"":""p@ss""," &
            """roots"":[{""name"":""Photos"",""hostPath"":""C:\\pics"",""readOnly"":false}]," &
            """lastError"":"""",""reachability"":{""lanAddress"":""192.168.1.5"",""ipv6Address"":""2001:db8::1""," &
            """externalPortChecked"":true,""externalPortOpen"":true,""isCgnat"":false}}}"

        Dim resp As WorkerResponse = JsonSerializer.Deserialize(Of WorkerResponse)(wire, ReadOpts)
        Assert.NotNull(resp)
        Assert.True(resp.Ok)
        Assert.NotNull(resp.Status)
        Assert.True(resp.Status.Running)
        Assert.Equal(2222, resp.Status.ListenPort)
        Assert.Equal("fms", resp.Status.Username)
        Assert.Single(resp.Status.Roots)
        Assert.Equal("Photos", resp.Status.Roots(0).name)
        Assert.False(resp.Status.Roots(0).readOnly)
        Assert.NotNull(resp.Status.Reachability)
        Assert.Equal("192.168.1.5", resp.Status.Reachability.LanAddress)
        Assert.Equal("2001:db8::1", resp.Status.Reachability.Ipv6Address)
        Assert.True(resp.Status.Reachability.ExternalPortChecked)
        Assert.True(resp.Status.Reachability.ExternalPortOpen)
        Assert.False(resp.Status.Reachability.IsCgnat)
    End Sub

    ''' <summary>The port rides an EXISTING request type as a nullable field, so an unset
    ''' port must be OMITTED rather than sent as 0. The port is a setting, not a mode: both
    ''' "absent" and 0 mean "leave it alone" - there is nothing to switch back to.</summary>
    <Fact>
    Public Sub SetNetworkPolicy_Request_CarriesThePortOnlyWhenSet()
        Dim without As String = JsonSerializer.Serialize(
            New WorkerRequest With {.type = "SetNetworkPolicy", .maxConnections = 10}, WriteOpts)
        Assert.DoesNotContain("port", without)

        Dim withPort As String = JsonSerializer.Serialize(
            New WorkerRequest With {.type = "SetNetworkPolicy", .maxConnections = 10, .port = 2222}, WriteOpts)
        Assert.Contains("""port"":2222", withPort)

        Dim unchanged As String = JsonSerializer.Serialize(
            New WorkerRequest With {.type = "SetNetworkPolicy", .port = 0}, WriteOpts)
        Assert.Contains("""port"":0", unchanged)
    End Sub

    ''' <summary>The three additive status fields bind, and an older worker that omits them
    ''' degrades to 0/False/Nothing rather than failing the whole response.</summary>
    <Fact>
    Public Sub Status_BindsDesiredPortAndStartError()
        Dim wire As String =
            "{""schemaVersion"":1,""ok"":true,""status"":{""running"":false,""listenPort"":0," &
            """roots"":[],""lastError"":""x"",""desiredPort"":2222,""portSupported"":true," &
            """lastStartError"":""port 2222 unavailable [excluded-range]: bind""}}"
        Dim resp As WorkerResponse = JsonSerializer.Deserialize(Of WorkerResponse)(wire, ReadOpts)
        Assert.Equal(2222, resp.Status.DesiredPort)
        Assert.True(resp.Status.PortSupported)
        Assert.Contains(ShareController.ExcludedRangeMarker, resp.Status.LastStartError)

        Dim older As String = "{""schemaVersion"":1,""ok"":true,""status"":{""running"":true,""listenPort"":2222,""roots"":[]}}"
        Dim old As WorkerResponse = JsonSerializer.Deserialize(Of WorkerResponse)(older, ReadOpts)
        Assert.Equal(0, old.Status.DesiredPort)
        Assert.False(old.Status.PortSupported)
        Assert.Null(old.Status.LastStartError)
    End Sub

    <Fact>
    Public Sub Response_ErrorEnvelope_Binds()
        Dim wire As String = "{""schemaVersion"":1,""ok"":false,""error"":""schema mismatch""}"
        Dim resp As WorkerResponse = JsonSerializer.Deserialize(Of WorkerResponse)(wire, ReadOpts)
        Assert.False(resp.Ok)
        Assert.Equal("schema mismatch", resp.Error)
        Assert.Null(resp.Status)
    End Sub

End Class
