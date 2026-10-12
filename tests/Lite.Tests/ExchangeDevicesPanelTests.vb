#If Not NETFRAMEWORK Then
Option Strict On

Imports System.Collections
Imports System.Collections.Generic
Imports System.IO
Imports System.Reflection
Imports System.Threading
Imports Xunit

<Collection("LocalizationState")>
Public Class ExchangeDevicesPanelTests

    <Fact>
    Public Sub Signing_in_again_rebinds_the_directory_to_a_live_session()
        OnSta(Sub()
                  Dim previousProbe As Func(Of Boolean) = ExchangeFeatures.ComponentProbeOverride
                  ExchangeFeatures.ComponentProbeOverride = Function() True
                  Try
                      Dim store As New ExchangeEntryStore(New ExchangeStoresTests.MemorySettings(),
                          Path.Combine(Path.GetTempPath(), "fms-panel-" & Guid.NewGuid().ToString("N")))
                      Dim entry As New ExchangeEntry(ExchangeEntryStore.NewEntryId(), "127.0.0.1:22022", "alice", "",
                          ExchangeCertificateKind.CaSigned, New String("D"c, 21) & "A", "Test viewer", "Test server", False)
                      Assert.True(store.Upsert(entry))
                      Using hub As New ExchangeEntryHub(New ExchangeConsumerPorts With {.Entries = store})
                          Using panel As New Exchange_Devices_Panel(hub, store, Function() New List(Of ResourceDescriptor)(),
                                                                   Function(r) False, Sub(r) Return)
                              Dim old As ExchangeEntrySession = SourceSession(panel)
                              panel.EntryAdded(entry)
                              Dim fresh As ExchangeEntrySession = SourceSession(panel)
                              Assert.NotSame(old, fresh)
                              Assert.Same(hub.SessionFor(entry), fresh)
                              ' A disposed session cannot take a lease. A live one with no token reports that sign-in is needed.
                              ' This check opens no socket and touches no real credentials or resource settings.
                              Using lease As IDisposable = fresh.Lease()
                                  Assert.Equal(ExchangeAgentState.SignInAgain,
                                      fresh.GetStateAsync(CancellationToken.None).GetAwaiter().GetResult().Info.State)
                              End Using
                              panel.RebuildSources()
                              Assert.Same(fresh, SourceSession(panel))
                          End Using
                      End Using
                  Finally
                      ExchangeFeatures.ComponentProbeOverride = previousProbe
                  End Try
              End Sub)
    End Sub

    Private Shared Function SourceSession(panel As Exchange_Devices_Panel) As ExchangeEntrySession
        Dim sources As IList = DirectCast(GetType(Exchange_Devices_Panel).GetField("_sources",
            BindingFlags.Instance Or BindingFlags.NonPublic).GetValue(panel), IList)
        Assert.Equal(1, sources.Count)
        Dim source As Object = sources(0)
        Return DirectCast(source.GetType().GetField("Session", BindingFlags.Instance Or BindingFlags.NonPublic).GetValue(source), ExchangeEntrySession)
    End Function

    Private Shared Sub OnSta(action As Action)
        Dim failure As Exception = Nothing
        Dim thread As New Thread(Sub()
                                     Try
                                         action()
                                     Catch ex As Exception
                                         failure = ex
                                     End Try
                                 End Sub)
        thread.SetApartmentState(ApartmentState.STA)
        thread.Start()
        Assert.True(thread.Join(20000), "the UI check did not finish")
        If failure IsNot Nothing Then System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(failure).Throw()
    End Sub
End Class
#End If
