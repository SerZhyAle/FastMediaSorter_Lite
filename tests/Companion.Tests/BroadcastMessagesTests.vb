Option Strict On

Imports System.IO
Imports System.Linq
Imports FastMediaSorterCompanion
Imports Xunit

''' <summary>
''' Spec 159 section 3.3 (test T-3): one helper, one sentence per reason, "update" said only for the one
''' reason that is a real version mismatch, and the old wording gone from every table and every call site.
''' Texts are checked in Russian, the table's key language, so they do not depend on the machine's language.
''' </summary>
<Collection(UiLanguageCollection.Name)>
Public Class BroadcastMessagesTests

    Private Const OldKey As String = "Обновите Fast Media Sorter для поддержки трансляций."
    Private Const GenericRu As String = "Не удалось выполнить действие. Подробности в журнале."
    Private Const TooOldRu As String = "Эта версия службы трансляций устарела. Обновите Fast Media Sorter."

    Private Shared Function InRussian(Of T)(work As Func(Of T)) As T
        Dim saved As String = Localization.CurrentCode
        Try
            Localization.CurrentCode = "ru"
            Return work()
        Finally
            Localization.CurrentCode = saved
        End Try
    End Function

    ' --- the sentence for each reason --------------------------------------------

    <Fact>
    Public Sub A_real_mismatch_says_update_and_names_the_product()
        Assert.Equal(TooOldRu, InRussian(Function() BroadcastMessages.Text(BroadcastNoticeReason.WorkerTooOld)))
    End Sub

    <Fact>
    Public Sub A_gateway_refusal_is_the_workers_own_text_as_it_is()
        Dim text As String = InRussian(Function() BroadcastMessages.Text(BroadcastNoticeReason.GatewayRefused,
                                                                         "  port 55272 unavailable [firewall]  "))
        Assert.Equal("port 55272 unavailable [firewall]", text)
    End Sub

    <Theory>
    <InlineData("")>
    <InlineData("   ")>
    <InlineData(Nothing)>
    Public Sub A_gateway_refusal_with_no_text_falls_back_to_the_generic_line(workerText As String)
        Assert.Equal(GenericRu, InRussian(Function() BroadcastMessages.Text(BroadcastNoticeReason.GatewayRefused, workerText)))
    End Sub

    <Fact>
    Public Sub Anything_else_is_the_generic_line()
        Assert.Equal(GenericRu, InRussian(Function() BroadcastMessages.Text(BroadcastNoticeReason.Failed)))
    End Sub

    ''' <summary>"Update" is never said for any reason but the mismatch one - in particular not for a refusal
    ''' text the worker did not supply, nor for the generic line.</summary>
    <Fact>
    Public Sub Only_the_mismatch_reason_ever_says_update()
        For Each reason As BroadcastNoticeReason In [Enum].GetValues(GetType(BroadcastNoticeReason)).Cast(Of BroadcastNoticeReason)()
            Dim current As BroadcastNoticeReason = reason
            Dim shown As String = InRussian(Function() BroadcastMessages.Text(current))
            Assert.Equal(current = BroadcastNoticeReason.WorkerTooOld, shown.Contains("Обновите"))
        Next
    End Sub

    ' --- classifying an answer -----------------------------------------------------

    <Fact>
    Public Sub No_answer_is_a_plain_failure()
        Assert.Equal(BroadcastNoticeReason.Failed, BroadcastMessages.ReasonFor(Nothing, gatewayRequest:=True))
        Assert.Equal(BroadcastNoticeReason.Failed, BroadcastMessages.ReasonFor(Nothing, gatewayRequest:=False))
    End Sub

    <Theory>
    <InlineData(True)>
    <InlineData(False)>
    Public Sub The_workers_unknown_verb_error_is_a_mismatch_for_any_request(gatewayRequest As Boolean)
        Dim resp As New WorkerResponse With {.Ok = False, .Error = BroadcastController.UnsupportedRequestError}
        Assert.Equal(BroadcastNoticeReason.WorkerTooOld, BroadcastMessages.ReasonFor(resp, gatewayRequest))
    End Sub

    <Fact>
    Public Sub A_refusal_is_a_gateway_reason_only_for_the_request_that_starts_the_gateway()
        Dim resp As New WorkerResponse With {.Ok = False, .Error = "port 55272 unavailable [firewall]"}
        Assert.Equal(BroadcastNoticeReason.GatewayRefused, BroadcastMessages.ReasonFor(resp, gatewayRequest:=True))
        Assert.Equal(BroadcastNoticeReason.Failed, BroadcastMessages.ReasonFor(resp, gatewayRequest:=False))
    End Sub

    <Fact>
    Public Sub A_refusal_with_no_text_or_an_exchange_problem_or_an_ok_answer_is_a_plain_failure()
        Assert.Equal(BroadcastNoticeReason.Failed,
                     BroadcastMessages.ReasonFor(New WorkerResponse With {.Ok = False, .Error = ""}, gatewayRequest:=True))
        Assert.Equal(BroadcastNoticeReason.Failed,
                     BroadcastMessages.ReasonFor(WorkerResponse.WithProblem(WorkerExchangeProblem.Malformed), gatewayRequest:=True))
        Assert.Equal(BroadcastNoticeReason.Failed,
                     BroadcastMessages.ReasonFor(New WorkerResponse With {.Ok = True}, gatewayRequest:=True))
    End Sub

    ' --- a request the page does not serve ---------------------------------------------

    ''' <summary>A build that does not offer the page never sends the user to an update: there is none, and the
    ''' line must not advertise the feature either. Only a build that offers it, next to a worker that says it
    ''' lacks the verbs, is a mismatch.</summary>
    <Theory>
    <InlineData(False, False, False, BroadcastNoticeReason.Failed)>
    <InlineData(False, True, False, BroadcastNoticeReason.Failed)>
    <InlineData(False, True, True, BroadcastNoticeReason.Failed)>
    <InlineData(True, False, False, BroadcastNoticeReason.Failed)>
    <InlineData(True, True, False, BroadcastNoticeReason.WorkerTooOld)>
    Public Sub An_entry_the_page_does_not_serve_is_a_mismatch_only_under_a_build_that_offers_it(
            offeredByBuild As Boolean, haveStatus As Boolean, workerStatesSupport As Boolean, expected As BroadcastNoticeReason)
        Dim st As WorkerStatus = If(haveStatus, New WorkerStatus With {.BroadcastSupported = workerStatesSupport}, Nothing)
        Assert.Equal(expected, BroadcastMessages.EntryReason(st, offeredByBuild))
    End Sub

    ' --- the old wording is gone -------------------------------------------------------

    <Fact>
    Public Sub The_old_update_key_is_gone_from_every_table_and_the_new_keys_are_registered()
        Dim keys As String() = Localization.All.Select(Function(kv) kv.Key).ToArray()
        Assert.DoesNotContain(OldKey, keys)
        Assert.Contains("Эта версия службы трансляций устарела. Обновите {0}.", keys)
        Assert.Contains(GenericRu, keys)
    End Sub

    <Fact>
    Public Sub The_old_wording_is_in_no_source_and_the_page_asks_one_helper()
        For Each src As String In ExchangeTestSupport.CompanionSources()
            Assert.False(File.ReadAllText(src).Contains(OldKey), Path.GetFileName(src) & " still carries the old wording")
        Next
        Dim page As String = ExchangeTestSupport.CodeOnly(ExchangeTestSupport.ReadSource("Forms\Share_Broadcasts_Form.vb"))
        ' The classification lives in BroadcastMessages.ReasonFor alone; the page never reads an error's wording.
        Assert.DoesNotContain("IsUnsupportedWorker", page)
        Assert.DoesNotContain("Обновите", page)
    End Sub

End Class
