#If Not NETFRAMEWORK Then
Option Strict On

Imports System.Collections.Generic
Imports System.Drawing
Imports System.Globalization
Imports System.Linq
Imports System.Threading
Imports System.Windows.Forms
Imports Xunit

''' <summary>
''' BUTTONS 2.0 section 4 rule 4 as the question window holds it: the answers read Cancel, the intermediate ones,
''' then the final affirmative action, from the reading start - left in a left-to-right window, right in a
''' right-to-left one. The specs keep listing the acting answer first and the cancel answer last (indexes are
''' call-site contracts); the strip shows them the other way round, and Tab follows what the eye reads.
''' </summary>
<Collection("LocalizationState")>
Public Class AskAnswerOrderTests

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
        Assert.True(thread.Join(240000), "the UI check did not finish")
        If failure IsNot Nothing Then System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(failure).Throw()
    End Sub

    ''' <summary>The answer buttons in spec order, each with the x of its left edge on the screen.</summary>
    Private Shared Function PlacedAnswers(dialog As Form) As List(Of KeyValuePair(Of Button, Integer))
        Dim found As New List(Of Button)()
        Collect(dialog, found)
        Return found.Where(Function(b) b.Name.StartsWith("ask_Button", StringComparison.Ordinal)).
            OrderBy(Function(b) Integer.Parse(b.Name.Substring("ask_Button".Length), CultureInfo.InvariantCulture)).
            Select(Function(b) New KeyValuePair(Of Button, Integer)(b, b.PointToScreen(Point.Empty).X)).ToList()
    End Function

    Private Shared Sub Collect(parent As Control, into As List(Of Button))
        For Each c As Control In parent.Controls
            Dim b As Button = TryCast(c, Button)
            If b IsNot Nothing Then into.Add(b)
            Collect(c, into)
        Next
    End Sub

    Private Shared Sub ShowOffscreen(dialog As Form)
        dialog.StartPosition = FormStartPosition.Manual
        dialog.Location = New Point(-20000, -20000)
        dialog.ShowInTaskbar = False
        dialog.Show()
    End Sub

    ''' <summary>Asserts the spec-last answer reads first and the spec-first answer reads last, for every answer
    ''' between them in order too.</summary>
    Private Shared Sub AssertReadingOrder(name As String, placed As List(Of KeyValuePair(Of Button, Integer)), rightToLeft As Boolean)
        For i As Integer = 0 To placed.Count - 2
            Dim acting As Integer = placed(i).Value
            Dim earlier As Integer = placed(i + 1).Value
            If rightToLeft Then
                Assert.True(earlier > acting, name & ": answer " & (i + 1).ToString(CultureInfo.InvariantCulture) & " must read before answer " & i.ToString(CultureInfo.InvariantCulture) & " (right to left)")
            Else
                Assert.True(earlier < acting, name & ": answer " & (i + 1).ToString(CultureInfo.InvariantCulture) & " must read before answer " & i.ToString(CultureInfo.InvariantCulture) & " (left to right)")
            End If
        Next
    End Sub

    <Theory>
    <InlineData("en", False)>
    <InlineData("ar", True)>
    Public Sub Every_fixed_question_reads_cancel_first_and_the_acting_answer_last(code As String, rightToLeft As Boolean)
        Dim saved As String = Localization.CurrentCode
        Try
            Localization.CurrentCode = code
            Assert.Equal(rightToLeft, Localization.IsRightToLeft())
            OnSta(Sub()
                      For Each spec As AskSpec In AskSpecs.All()
                          If spec.Buttons.Count < 2 Then Continue For
                          Using dialog As New Ask_Dialog(spec)
                              ShowOffscreen(dialog)
                              Dim placed As List(Of KeyValuePair(Of Button, Integer)) = PlacedAnswers(dialog)
                              Assert.Equal(spec.Buttons.Count, placed.Count)
                              AssertReadingOrder(spec.Title, placed, rightToLeft)
                              ' the dismissing answer is the first one the eye meets
                              Assert.Equal(spec.Buttons.Count - 1, spec.CancelIndex)
                              dialog.Close()
                          End Using
                      Next
                  End Sub)
        Finally
            Localization.CurrentCode = saved
        End Try
    End Sub

    <Theory>
    <InlineData("en", False)>
    <InlineData("ar", True)>
    Public Sub A_text_prompt_reads_cancel_then_ok(code As String, rightToLeft As Boolean)
        Dim saved As String = Localization.CurrentCode
        Try
            Localization.CurrentCode = code
            OnSta(Sub()
                      Using dialog As New Ask_Dialog(AskSpecs.JumpToNumberPrompt(1, 10))
                          ShowOffscreen(dialog)
                          Dim placed As List(Of KeyValuePair(Of Button, Integer)) = PlacedAnswers(dialog)
                          Assert.Equal(2, placed.Count)
                          AssertReadingOrder("jump prompt", placed, rightToLeft)
                          dialog.Close()
                      End Using
                  End Sub)
        Finally
            Localization.CurrentCode = saved
        End Try
    End Sub

    <Fact>
    Public Sub Tab_follows_the_reading_order_and_the_default_answer_keeps_its_place()
        OnSta(Sub()
                  For Each spec As AskSpec In AskSpecs.All()
                      If spec.Buttons.Count < 2 Then Continue For
                      Using dialog As New Ask_Dialog(spec)
                          ShowOffscreen(dialog)
                          Dim placed As List(Of KeyValuePair(Of Button, Integer)) = PlacedAnswers(dialog)
                          For i As Integer = 0 To placed.Count - 1
                              Assert.Equal(placed.Count - 1 - i, placed(i).Key.TabIndex)
                          Next
                          Assert.Same(placed(spec.DefaultIndex).Key, dialog.AcceptButton)
                          Assert.Same(placed(spec.CancelIndex).Key, dialog.CancelButton)
                          dialog.Close()
                      End Using
                  Next
              End Sub)
    End Sub
End Class
#End If
