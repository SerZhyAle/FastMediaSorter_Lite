Option Strict On

Imports System.Collections.Generic
Imports System.Text
Imports FastMediaSorterCompanion
Imports Xunit

''' <summary>
''' Frozen named-pipe wire contract (WORKER-IPC sections 3-5; the pointer is
''' docs/contracts/WORKER-IPC.md). These pin the exact JSON field names WorkerIpc emits and
''' consumes; a DTO rename would silently break the worker handshake, so it must break a test
''' instead.
'''
''' Everything here goes through the PRODUCT's serializer (WorkerIpc.SerializeRequest /
''' ParseResponse). This file used to carry its own copy of the options, which proved only that
''' the copy agreed with itself - an option changed in WorkerIpc would have passed here. The
''' byte-for-byte pins of each request type live in WorkerIpcConformanceTests.
''' </summary>
Public Class WorkerIpcJsonTests

    Private Shared Function Wire(req As WorkerRequest) As String
        Return Encoding.UTF8.GetString(WorkerIpc.SerializeRequest(req))
    End Function

    <Fact>
    Public Sub SetSharedFolders_Request_HasExactWireShape()
        Dim req As New WorkerRequest With {
            .type = "SetSharedFolders",
            .folders = New List(Of ShareFolder) From {
                New ShareFolder With {.name = "Photos", .hostPath = "C:\pics", .readOnly = False}
            }
        }
        Dim json As String = Wire(req)
        Assert.Contains("""schemaVersion"":1", json)
        Assert.Contains("""type"":""SetSharedFolders""", json)
        Assert.Contains("""name"":""Photos""", json)
        Assert.Contains("""hostPath"":""C:\\pics""", json)
        Assert.Contains("""readOnly"":false", json)
    End Sub

    <Fact>
    Public Sub GetStatus_Request_OmitsNullFolders()
        Dim json As String = Wire(New WorkerRequest With {.type = "GetStatus"})
        Assert.DoesNotContain("folders", json)  ' WhenWritingNull - only SetSharedFolders carries folders
    End Sub

    ''' <summary>The serializer stamps the client's version itself - a caller that forgot, or
    ''' set something else, cannot put a different number on the wire.</summary>
    <Fact>
    Public Sub Request_AlwaysCarriesTheClientsSchemaVersion()
        Dim json As String = Wire(New WorkerRequest With {.type = "GetStatus", .schemaVersion = 0})
        Assert.StartsWith("{""schemaVersion"":" & WorkerIpc.SchemaVersion.ToString() & ",", json)
    End Sub

    <Fact>
    Public Sub Response_DeserializesCamelCase_IntoPascalDtos()
        Dim wireText As String =
            "{""schemaVersion"":1,""ok"":true,""status"":{""running"":true,""listenPort"":2222," &
            """fingerprint"":""SHA256:abc"",""username"":""fms"",""password"":""p@ss""," &
            """roots"":[{""name"":""Photos"",""hostPath"":""C:\\pics"",""readOnly"":false}]," &
            """lastError"":"""",""reachability"":{""lanAddress"":""192.168.1.5"",""ipv6Address"":""2001:db8::1""," &
            """externalPortChecked"":true,""externalPortOpen"":true,""isCgnat"":false}}}"

        Dim resp As WorkerResponse = WorkerIpc.ParseResponse(wireText)
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

    ''' <summary>WORKER-IPC rule 4: absent is unchanged, never 0. The port rides an EXISTING
    ''' request type as a nullable field, and a port nobody recorded here is OMITTED. The
    ''' worker happens to read 0 as unchanged as well, which is why sending it was harmless -
    ''' and still the one thing the rule says never to send.</summary>
    <Fact>
    Public Sub SetNetworkPolicy_Request_CarriesThePortOnlyWhenSet()
        Dim without As String = Wire(New WorkerRequest With {.type = "SetNetworkPolicy", .maxConnections = 10})
        Assert.DoesNotContain("port", without)

        Dim withPort As String = Wire(New WorkerRequest With {.type = "SetNetworkPolicy", .maxConnections = 10, .port = 2222})
        Assert.Contains("""port"":2222", withPort)
    End Sub

    ''' <summary>The product's own policy push, with no port recorded on this side (the
    ''' registry value is 0 = "not recorded"), emits no <c>port</c> key at all.</summary>
    <Fact>
    Public Sub PolicyPush_WithNoRecordedPort_OmitsThePort()
        Dim unset As String = Wire(ShareController.NetworkPolicyRequest(10, False, ShareSettings.UnsetPort))
        Assert.DoesNotContain("port", unset)
        Assert.Contains("""maxConnections"":10", unset)
        Assert.Contains("""lanOnly"":false", unset)

        Dim below As String = Wire(ShareController.NetworkPolicyRequest(10, True, -5))
        Assert.DoesNotContain("port", below)

        Dim recorded As String = Wire(ShareController.NetworkPolicyRequest(10, True, 2222))
        Assert.Contains("""port"":2222", recorded)
    End Sub

    ''' <summary>The three additive status fields bind, and an older worker that omits them
    ''' degrades to 0/False/Nothing rather than failing the whole response.</summary>
    <Fact>
    Public Sub Status_BindsDesiredPortAndStartError()
        Dim wireText As String =
            "{""schemaVersion"":1,""ok"":true,""status"":{""running"":false,""listenPort"":0," &
            """roots"":[],""lastError"":""x"",""desiredPort"":2222,""portSupported"":true," &
            """lastStartError"":""port 2222 unavailable [excluded-range]: bind""}}"
        Dim resp As WorkerResponse = WorkerIpc.ParseResponse(wireText)
        Assert.Equal(2222, resp.Status.DesiredPort)
        Assert.True(resp.Status.PortSupported)
        Assert.Contains(ShareController.ExcludedRangeMarker, resp.Status.LastStartError)

        Dim older As String = "{""schemaVersion"":1,""ok"":true,""status"":{""running"":true,""listenPort"":2222,""roots"":[]}}"
        Dim old As WorkerResponse = WorkerIpc.ParseResponse(older)
        Assert.Equal(0, old.Status.DesiredPort)
        Assert.False(old.Status.PortSupported)
        Assert.Null(old.Status.LastStartError)
    End Sub

    <Fact>
    Public Sub Response_ErrorEnvelope_Binds()
        Dim wireText As String = "{""schemaVersion"":1,""ok"":false,""error"":""schema mismatch""}"
        Dim resp As WorkerResponse = WorkerIpc.ParseResponse(wireText)
        Assert.False(resp.Ok)
        Assert.False(resp.IsRefused)
        Assert.Equal("schema mismatch", resp.Error)
        Assert.Null(resp.Status)
    End Sub

End Class
