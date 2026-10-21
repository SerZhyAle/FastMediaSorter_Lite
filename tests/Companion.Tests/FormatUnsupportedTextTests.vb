Option Strict On

Imports FastMediaSorterCompanion
Imports Xunit

''' <summary>
''' Ticket 244, DEVICE-EXCHANGE 1.3 (amendment of 2026-10-17): a higher FMSEXCH prefix or descriptor schema is refused cleanly with the
''' canonical local text, row `format-unsupported`, and the Share Manager shows the SAME sentence the viewer shows - it demands no
''' update, and a wire `version-unsupported` keeps its code and its own row. The three sentences below are copied verbatim from the
''' catalog's amendment; the other 10 languages are product translations of them, pinned by the viewer's ConnectionDiagnosisTests.
''' </summary>
<Collection(UiLanguageCollection.Name)>
Public Class FormatUnsupportedTextTests

    Private Const CatalogEn As String = "This format is newer than this program supports. Nothing was imported. Use a compatible program."
    Private Const CatalogRu As String = "Этот формат новее, чем поддерживает программа. Ничего не импортировано. Используйте совместимую программу."
    Private Const CatalogUk As String = "Цей формат новіший, ніж підтримує програма. Нічого не імпортовано. Скористайтеся сумісною програмою."

    Private Shared Sub InLanguage(code As String, body As Action)
        Dim saved As String = Localization.CurrentCode
        Try
            Localization.CurrentCode = code
            body()
        Finally
            Localization.CurrentCode = saved
        End Try
    End Sub

    <Theory>
    <InlineData("en", CatalogEn)>
    <InlineData("ru", CatalogRu)>
    <InlineData("uk", CatalogUk)>
    Public Sub The_row_format_unsupported_is_the_catalog_sentence_verbatim(code As String, catalog As String)
        Assert.Equal(catalog, ConnectionDiagnosisLocalization.GetMessageText("format-unsupported", code))
        InLanguage(code, Sub() Assert.Equal(catalog, ConnectionDiagnosisLocalization.FormatUnsupportedText()))
    End Sub

    <Theory>
    <InlineData("en", CatalogEn)>
    <InlineData("ru", CatalogRu)>
    <InlineData("uk", CatalogUk)>
    Public Sub A_connection_string_of_a_higher_prefix_is_refused_with_the_catalog_sentence(code As String, catalog As String)
        InLanguage(code, Sub()
                             Assert.Equal(catalog, ExchangeJoinText.InputRefusal(ExchangeConnectionStringReason.NewerMajor, ""))
                             ' the looks hint of another refusal never changes this one
                             Assert.Equal(catalog, ExchangeJoinText.InputRefusal(ExchangeConnectionStringReason.NewerMajor, ExchangeConnectionStringReason.LooksUrl))
                         End Sub)
    End Sub

    <Fact>
    Public Sub In_every_language_the_refusal_is_the_one_row_of_the_table_and_demands_no_update()
        For Each code As String In Localization.Codes
            Dim languageCode As String = code
            InLanguage(languageCode, Sub()
                                         Dim text As String = ExchangeJoinText.InputRefusal(ExchangeConnectionStringReason.NewerMajor, "")
                                         Assert.Equal(ConnectionDiagnosisLocalization.GetMessageText("format-unsupported", languageCode), text)
                                         Assert.False(String.IsNullOrWhiteSpace(text), languageCode)
                                     End Sub)
        Next
        InLanguage("en", Sub()
                             Dim text As String = ExchangeJoinText.InputRefusal(ExchangeConnectionStringReason.NewerMajor, "")
                             Assert.DoesNotContain("pdate", text)
                             Assert.DoesNotContain("newer version", text)
                         End Sub)
        InLanguage("ru", Sub() Assert.DoesNotContain("Обнов", ExchangeJoinText.InputRefusal(ExchangeConnectionStringReason.NewerMajor, "")))
        InLanguage("uk", Sub() Assert.DoesNotContain("Онов", ExchangeJoinText.InputRefusal(ExchangeConnectionStringReason.NewerMajor, "")))
    End Sub

    <Fact>
    Public Sub A_wire_version_unsupported_keeps_its_own_row_and_carries_no_update_demand()
        InLanguage("en", Sub()
                             Dim wire As String = ConnectionDiagnosisLocalization.GetMessageText("version-unsupported", "en")
                             Assert.Equal("This program and the server speak different versions of the exchange.", wire)
                             Assert.NotEqual(CatalogEn, wire)
                             Assert.DoesNotContain("pdate", wire)
                             Assert.Equal(wire, ExchangeDeviceText.EnrollFailure(New ExchangeEnrollResult(ExchangeEnrollOutcome.Refused, ExchangeRefusal.VersionUnsupported, "")))
                         End Sub)
    End Sub

End Class
