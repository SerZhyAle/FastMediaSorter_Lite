Option Explicit On
Option Strict On
Option Infer On
Option Compare Binary

Imports System.Linq
Imports System.Reflection
Imports System.Windows.Forms
Imports FastMediaSorterCompanion
Imports Xunit

''' <summary>Spec 248 M2 and M3: the two exchange settings pages and the group headings that specs 245 and 246 added to
''' the exchange page wear the mark, named by the CW term. Real windows, nothing shown, no server started.</summary>
<Collection(UiLanguageCollection.Name)>
Public Class ExchangeMarkSettingsTests
    Private Const Flags As BindingFlags = BindingFlags.NonPublic Or BindingFlags.Instance

    <Fact>
    Public Sub Exchange_pages_and_the_groups_added_by_245_and_246_carry_the_named_mark()
        ExchangeTestSupport.Reset()
        Dim component = ExchangeTestSupport.MakeFakeComponent(True, False, False)
        Try
            ExchangeProcess.ExeDirectoryOverride = component
            ExchangeProcess.RunOverride = Function(args, input) New ExchangeProcess.RunResult With {.Started = False}
            ExchangeTestSupport.OnUi(
                Sub()
                    Using manager As New Share_Settings_Form(), exchange As New Share_Settings_Form(destination:=SettingsDestination.ExchangeServer)
                        AssertPageMark(manager, "exchange-account")
                        AssertPageMark(exchange, "exchange")
                        For Each field In {"_lblXResTitle", "_lblXBroTitle", "_lblXClipTitle"}
                            AssertHeadingMark(DirectCast(GetType(Share_Settings_Form).GetField(field, Flags).GetValue(exchange), Label), field)
                        Next
                        Dim remote = DirectCast(GetType(Share_Settings_Form).GetField("_pnlXRemoteAdmin", Flags).GetValue(exchange), Control)
                        AssertHeadingMark(DirectCast(remote.Controls(0), Label), "_pnlXRemoteAdmin heading")
                    End Using
                End Sub)
        Finally
            ExchangeTestSupport.Reset()
            ExchangeTestSupport.DeleteQuietly(component)
        End Try
    End Sub

    Private Shared Sub AssertPageMark(form As Share_Settings_Form, pageId As String)
        Dim pages = DirectCast(GetType(Share_Settings_Form).GetField("_pages", Flags).GetValue(form), Dictionary(Of String, Panel))
        Assert.True(pages.ContainsKey(pageId), "The window must own the page " & pageId)
        Dim marks As Control() = pages(pageId).Controls.Find("exchangeMark", True)
        Assert.NotEmpty(marks)
        Assert.All(marks, Sub(mark) Assert.Equal(ExchangeMark.AccessibleName(), mark.AccessibleName))
    End Sub

    Private Shared Sub AssertHeadingMark(heading As Label, what As String)
        Assert.True(heading IsNot Nothing, what & " must exist")
        Dim marks As Control() = heading.Controls.Find("exchangeMark", False)
        Assert.True(marks.Length = 1, what & " must carry exactly one mark")
        Assert.Equal(ExchangeMark.AccessibleName(), marks(0).AccessibleName)
    End Sub
End Class
