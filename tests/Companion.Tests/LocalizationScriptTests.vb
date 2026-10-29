Option Strict On

Imports System.Collections.Generic
Imports System.Linq
Imports FastMediaSorterCompanion
Imports Xunit

''' <summary>
''' Holds the Share Manager's string table to the script and reading-order rules of
''' <see cref="LocaleScriptRules"/> (ticket L0290; the rules file is the viewer test project's,
''' linked, not copied), and keeps two different actions from sharing one word.
'''
''' The rules themselves are proved on sample cells in Lite.Tests.LocalizationScriptTests.
''' </summary>
<Collection(UiLanguageCollection.Name)>
Public Class LocalizationScriptTests

    <Fact>
    Public Sub Every_translation_keeps_to_the_script_and_reading_order_of_its_language()
        Dim inspected As Integer
        Dim findings = LocaleScriptRules.Audit(Localization.All, inspected)

        Assert.True(inspected > 10000, "Only " & inspected & " translations were inspected - the tables did not all load.")
        Assert.True(findings.Count = 0,
                    "Script or reading-order faults (" & findings.Count & "):" & vbLf & String.Join(vbLf, findings.Take(40)))
    End Sub

    ''' <summary>
    ''' Revoking a broadcast and cancelling a dialog are different acts: Arabic, Hindi and Urdu once
    ''' used one word for both, which on the broadcast list reads as "dismiss" instead of "withdraw".
    ''' </summary>
    <Fact>
    Public Sub Revoke_and_Cancel_read_differently_in_every_language()
        Dim revoke = Localization.All.Single(Function(kv) kv.Key = "Отозвать").Value
        Dim cancel = Localization.All.Single(Function(kv) kv.Key = "Отмена").Value
        Assert.Equal(LocaleScriptRules.SlotCodes.Length, revoke.Length)
        Assert.Equal(LocaleScriptRules.SlotCodes.Length, cancel.Length)

        Dim same As New List(Of String)()
        For i = 0 To revoke.Length - 1
            If String.Equals(revoke(i), cancel(i), StringComparison.Ordinal) Then
                same.Add(LocaleScriptRules.SlotCodes(i) & "  " & revoke(i))
            End If
        Next

        Assert.True(same.Count = 0, "One word for both Revoke and Cancel:" & vbLf & String.Join(vbLf, same))
    End Sub

End Class
