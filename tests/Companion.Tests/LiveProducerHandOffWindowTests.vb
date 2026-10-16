Option Strict On

Imports System.Collections.Generic
Imports System.Linq
Imports System.Reflection
Imports System.Windows.Forms
Imports FastMediaSorterCompanion
Imports Xunit

''' <summary>
''' Ticket 239 CR-15 (LIVE-BROADCAST item M), the window's half: the link, the code and the file are built from the ONE hand-out
''' descriptor the controller made (<c>LiveProducerState.DescriptorJson</c> / <c>BarcodeText</c>), never from the account's own record;
''' when that hand-out is empty (no password and no LAN endpoint) the window says so in one line and asks for a password instead of
''' copying a blank link; the password box carries a hint of what the password does. The descriptor side is held by
''' Lite.Tests.LiveProducerHandOutTests and the controller tests.
''' </summary>
<Collection(UiLanguageCollection.Name)>
Public Class LiveProducerHandOffWindowTests

    Private Const HintKey As String ="Без пароля ссылка, код и файл содержат только адрес в вашей сети. Чтобы передать трансляцию за пределы сети, задайте пароль."

    ' ---------------------------------------------------------------- the refusal line

    <Fact>
    Public Sub A_state_with_a_hand_out_has_no_refusal()
        Dim state As New LiveProducerState With {.Running = True, .DescriptorJson = "{""schemaVersion"":1}", .BarcodeText = "FMSBCAST1:AAAA"}
        Assert.Equal("", Live_Broadcast_Form.HandOffRefusal(state))
    End Sub

    <Fact>
    Public Sub An_empty_hand_out_is_refused_with_one_line_never_copied_as_a_blank_link()
        Dim refusal As String = Live_Broadcast_Form.HandOffRefusal(New LiveProducerState With {.Running = True, .HandOffWithheld = True})
        Assert.False(String.IsNullOrWhiteSpace(refusal))
        Assert.DoesNotContain(vbLf, refusal)
        Assert.DoesNotContain(vbCr, refusal)

        ' either half missing is as good as both missing; a missing state too
        Assert.Equal(refusal, Live_Broadcast_Form.HandOffRefusal(New LiveProducerState With {.DescriptorJson = "{}", .BarcodeText = ""}))
        Assert.Equal(refusal, Live_Broadcast_Form.HandOffRefusal(New LiveProducerState With {.DescriptorJson = "", .BarcodeText = "FMSBCAST1:AAAA"}))
        Assert.Equal(refusal, Live_Broadcast_Form.HandOffRefusal(Nothing))
    End Sub

    <Fact>
    Public Sub The_refusal_and_the_password_hint_are_translated_into_all_13_languages()
        Dim saved As String = Localization.CurrentCode
        Try
            Assert.Equal(13, Localization.Codes.Length)
            Dim refusals As New HashSet(Of String)(StringComparer.Ordinal)
            Dim hints As New HashSet(Of String)(StringComparer.Ordinal)
            For Each code As String In Localization.Codes
                Localization.CurrentCode = code
                Dim refusal As String = Live_Broadcast_Form.HandOffRefusal(Nothing)
                Dim hintText As String = Localization.T(HintKey)
                Assert.False(String.IsNullOrWhiteSpace(refusal), code)
                Assert.False(String.IsNullOrWhiteSpace(hintText), code)
                Assert.DoesNotContain("...", refusal)
                Assert.DoesNotContain("...", hintText)
                If code <> "ru" Then Assert.NotEqual(HintKey, hintText)
                Assert.True(refusals.Add(refusal), code & ": the refusal repeats another language")
                Assert.True(hints.Add(hintText), code & ": the hint repeats another language")
            Next
        Finally
            Localization.CurrentCode = saved
        End Try
    End Sub

    ' ---------------------------------------------------------------- the window

    Private Shared Function F(Of T)(target As Object, name As String) As T
        Dim info As FieldInfo = target.GetType().GetField(name, BindingFlags.Instance Or BindingFlags.NonPublic)
        Assert.True(info IsNot Nothing, name & " not found on " & target.GetType().Name)
        Return DirectCast(info.GetValue(target), T)
    End Function

    <Fact>
    Public Sub The_password_box_carries_the_hint_and_an_idle_hand_out_action_only_says_why()
        ExchangeTestSupport.OnUi(Sub()
                                     Using form As New Live_Broadcast_Form()
                                         Assert.Equal(Localization.T(HintKey), F(Of Label)(form, "_lblPasswordHint").Text)
                                         ' nothing is on air, so there is no hand-out: every action stops at the one line (no dialog, no clipboard)
                                         For Each handler As String In {"OnCopyLink", "OnCopyCode", "OnSaveFile", "OnShowQr"}
                                             F(Of Label)(form, "_lblState").Text = ""
                                             GetType(Live_Broadcast_Form).GetMethod(handler, BindingFlags.Instance Or BindingFlags.NonPublic).
                                                 Invoke(form, New Object() {form, EventArgs.Empty})
                                             Assert.Equal(Live_Broadcast_Form.HandOffRefusal(Nothing), F(Of Label)(form, "_lblState").Text)
                                         Next
                                     End Using
                                 End Sub)
    End Sub

    ' ---------------------------------------------------------------- the source

    <Fact>
    Public Sub The_three_channels_hand_out_the_hand_out_descriptor_and_never_the_account_record()
        Dim code As String = ExchangeTestSupport.CodeOnly(ExchangeTestSupport.ReadSource("Forms\Live_Broadcast_Form.vb"))
        For Each declaration As String In {"Private Sub OnCopyLink(", "Private Sub OnCopyCode(", "Private Sub OnSaveFile(", "Private Sub OnShowQr("}
            Dim body As String = ExchangeTestSupport.MethodBody(code, declaration)
            Assert.Contains("HandOffReady(state)", body)
            Assert.DoesNotContain("RecordDescriptorJson", body)
            Dim gate As Integer = body.IndexOf("HandOffReady(state)", StringComparison.Ordinal)
            Dim use As Integer = Math.Max(body.IndexOf("state.BarcodeText", StringComparison.Ordinal), body.IndexOf("state.DescriptorJson", StringComparison.Ordinal))
            Assert.True(use > gate, declaration & ": the hand-out is used before it is checked")
        Next
    End Sub

    <Fact>
    Public Sub Only_the_cast_to_the_accounts_own_devices_may_read_the_record_descriptor()
        ' Casting goes to another device of the SAME account through the exchange, like the directory record; it is not a hand-off.
        Dim code As String = ExchangeTestSupport.CodeOnly(ExchangeTestSupport.ReadSource("Forms\Live_Broadcast_Form.vb"))
        Dim offenders As List(Of String) = code.Split({vbLf}, StringSplitOptions.None).
            Where(Function(line) line.Contains("RecordDescriptorJson") AndAlso Not line.Contains("GetCastCandidates")).ToList()
        Assert.True(offenders.Count = 0, "the record descriptor is read outside the cast: " & String.Join(" | ", offenders))
    End Sub

End Class
