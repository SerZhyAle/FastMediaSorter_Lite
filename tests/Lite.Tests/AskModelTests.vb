#If Not NETFRAMEWORK Then
Option Strict On

Imports System.Collections.Generic
Imports System.IO
Imports System.Linq
Imports Xunit

''' <summary>
''' Ticket 124 section 3.2 (UX-204, UX-205), the pure half: every question the viewer asks has well-formed named
''' buttons, a default that is never the answer that overwrites something, and (for a prompt) a validator that
''' keeps what was typed. Plus the two requirements section 5 names - "the dialog's default button is the safe one
''' for each call site".
''' </summary>
Public Class AskModelTests

    ' --- the call-site specs -------------------------------------------------------------------

    <Fact>
    Public Sub Every_call_site_spec_is_well_formed()
        For Each spec As AskSpec In AskSpecs.All()
            Assert.True(AskRules.Problems(spec).Count = 0, spec.Title & ": " & String.Join("; ", AskRules.Problems(spec)))
        Next
    End Sub

    ''' <summary>Sorting is a run of key presses; the Enter or Space typed for the next picture lands on this window.
    ''' A default that overwrites something is the audit 046 C-01 bug.</summary>
    <Fact>
    Public Sub The_default_button_of_every_call_site_is_never_the_one_that_overwrites()
        For Each spec As AskSpec In AskSpecs.All()
            Assert.True(AskRules.DefaultIsSafe(spec), spec.Title & " - " & spec.Body & ": the default is destructive")
        Next
    End Sub

    <Fact>
    Public Sub A_name_collision_defaults_to_skip_and_so_does_escape()
        Dim facts As New AskFileFacts(2048, New Date(2026, 3, 4, 5, 6, 7))
        Dim spec As AskSpec = AskSpecs.NameCollision("a.jpg", facts, facts)
        Assert.Equal(AskSpecs.CollisionSkip, spec.DefaultIndex)
        Assert.Equal(AskSpecs.CollisionSkip, spec.CancelIndex)
        Assert.True(spec.Buttons(AskSpecs.CollisionReplace).Destructive)
        Assert.False(spec.Buttons(AskSpecs.CollisionKeepBoth).Destructive)
        Assert.Equal(3, spec.Buttons.Count)
    End Sub

    ''' <summary>UX-333: the name collision shows both files, not just a name.</summary>
    <Fact>
    Public Sub A_name_collision_shows_the_size_and_date_of_both_files()
        Dim existing As New AskFileFacts(1536, New Date(2025, 12, 31, 23, 59, 58))
        Dim incoming As New AskFileFacts(3 * 1024 * 1024, New Date(2026, 1, 2, 3, 4, 5))
        Dim spec As AskSpec = AskSpecs.NameCollision("a.jpg", existing, incoming)
        Assert.Contains("1.50 KB", spec.Detail)
        Assert.Contains("2025-12-31 23:59:58", spec.Detail)
        Assert.Contains("3.00 MB", spec.Detail)
        Assert.Contains("2026-01-02 03:04:05", spec.Detail)
        Assert.Contains("a.jpg", spec.Body)
    End Sub

    <Fact>
    Public Sub A_file_that_cannot_be_read_is_shown_without_claiming_a_size()
        Dim gone As AskFileFacts = AskFileFacts.Read(Path.Combine(Path.GetTempPath(), "fms-124-" & Guid.NewGuid().ToString("N")))
        Assert.Equal(-1L, gone.Size)
        Assert.Contains("?", gone.Describe())
    End Sub

    <Fact>
    Public Sub Settings_import_defaults_to_cancel_and_replace_is_the_destructive_one()
        Dim spec As AskSpec = AskSpecs.SettingsImport("summary")
        Assert.Equal(AskSpecs.ImportCancel, spec.DefaultIndex)
        Assert.Equal(AskSpecs.ImportCancel, spec.CancelIndex)
        Assert.True(spec.Buttons(AskSpecs.ImportReplace).Destructive)
    End Sub

    ''' <summary>126 UX-605: the answers are NAMED - no Yes/No box where "No" means "keep
    ''' editing". Both real answers are destructive in their own way, so the default - and
    ''' Escape - is "Отмена": a stray Enter from the editing session lands on neither.</summary>
    <Fact>
    Public Sub The_editor_close_question_names_its_answers_and_defaults_to_cancel()
        Dim spec As AskSpec = AskSpecs.EditorUnsavedClose()
        Assert.Equal(3, spec.Buttons.Count)
        Assert.Equal(AskSpecs.EditorCloseCancel, spec.DefaultIndex)
        Assert.Equal(AskSpecs.EditorCloseCancel, spec.CancelIndex)
        Assert.True(spec.Buttons(AskSpecs.EditorCloseSave).Destructive)
        Assert.True(spec.Buttons(AskSpecs.EditorCloseDiscard).Destructive)
        Assert.False(spec.Buttons(AskSpecs.EditorCloseCancel).Destructive)
    End Sub

    ''' <summary>UX-331: "No" used to read as "cancel"; the three answers have three different words and Cancel is
    ''' its own.</summary>
    <Fact>
    Public Sub The_hotkey_conflict_names_its_three_answers()
        Dim spec As AskSpec = AskSpecs.HotkeyConflict("Ctrl+K", "Rotate")
        Assert.Equal(3, spec.Buttons.Count)
        Assert.Equal(spec.Buttons.Count, spec.Buttons.Select(Function(b) b.Caption).Distinct().Count())
        Assert.Equal(AskSpecs.ConflictCancel, spec.CancelIndex)
        Assert.Contains("Ctrl+K", spec.Body)
        Assert.Contains("Rotate", spec.Body)
    End Sub

    ' --- the model's rules ---------------------------------------------------------------------

    Private Shared Function Spec(defaultIndex As Integer, cancelIndex As Integer, ParamArray buttons As AskButton()) As AskSpec
        Return New AskSpec("t", "m", AskIcon.Question, buttons, defaultIndex, cancelIndex)
    End Function

    <Fact>
    Public Sub A_spec_with_a_default_out_of_range_or_no_cancel_is_a_problem()
        Assert.NotEmpty(AskRules.Problems(Spec(5, 1, New AskButton("a"), New AskButton("b"))))
        Assert.NotEmpty(AskRules.Problems(Spec(0, -1, New AskButton("a"), New AskButton("b"))))
        Assert.NotEmpty(AskRules.Problems(Spec(0, 0, New AskButton("a"), New AskButton(""))))
        Assert.NotEmpty(AskRules.Problems(Spec(0, 0)))
        Assert.Empty(AskRules.Problems(Spec(0, 1, New AskButton("a"), New AskButton("b"))))
    End Sub

    <Fact>
    Public Sub A_notice_with_one_button_needs_no_cancel_index_and_closes_with_that_button()
        Dim notice As AskSpec = Spec(0, -1, New AskButton("OK"))
        Assert.Empty(AskRules.Problems(notice))
        Assert.Equal(0, AskRules.ClosingAnswer(notice))
    End Sub

    <Fact>
    Public Sub A_destructive_default_is_reported_unsafe()
        Assert.False(AskRules.DefaultIsSafe(Spec(0, 1, New AskButton("Delete", destructive:=True), New AskButton("Cancel"))))
        Assert.True(AskRules.DefaultIsSafe(Spec(1, 1, New AskButton("Delete", destructive:=True), New AskButton("Cancel"))))
    End Sub

    <Fact>
    Public Sub Mnemonics_give_every_answer_its_own_letter()
        Dim marked As String() = AskRules.WithMnemonics(New List(Of String) From {"Replace", "Rename", "Reset"})
        Assert.Equal("&Replace", marked(0))
        Assert.Equal("R&ename", marked(1))
        Assert.Equal("Re&set", marked(2))
    End Sub

    <Fact>
    Public Sub Mnemonics_work_in_any_script_and_escape_an_ampersand()
        Dim marked As String() = AskRules.WithMnemonics(New List(Of String) From {"Заменить", "Оставить оба", "Пропустить", "A & B"})
        Assert.Equal("&Заменить", marked(0))
        Assert.Equal("&Оставить оба", marked(1))
        Assert.Equal("&Пропустить", marked(2))
        Assert.Equal("&A && B", marked(3))
    End Sub

    <Fact>
    Public Sub A_caption_with_nothing_usable_is_left_alone()
        Dim marked As String() = AskRules.WithMnemonics(New List(Of String) From {"..", "a"})
        Assert.Equal("..", marked(0))
        Assert.Equal("&a", marked(1))
    End Sub

    ' --- prompts ---------------------------------------------------------------------------------

    <Fact>
    Public Sub A_prompt_without_a_validator_accepts_anything()
        Dim spec As New PromptSpec("t", "m", "x", "OK", Nothing)
        Assert.True(AskRules.Evaluate(spec, "whatever").Accepted)
    End Sub

    <Fact>
    Public Sub A_refusal_carries_its_reason_and_an_acceptance_none()
        Dim spec As New PromptSpec("t", "m", "", "OK", Function(s) If(s.Length = 0, "empty", Nothing))
        Dim refused As PromptOutcome = AskRules.Evaluate(spec, "")
        Assert.False(refused.Accepted)
        Assert.Equal("empty", refused.Problem)
        Dim accepted As PromptOutcome = AskRules.Evaluate(spec, "x")
        Assert.True(accepted.Accepted)
        Assert.Equal(String.Empty, accepted.Problem)
    End Sub

    ''' <summary>A check that cannot judge a text must not pass it.</summary>
    <Fact>
    Public Sub A_validator_that_throws_refuses_the_text()
        Dim thrower As Func(Of String, String) =
            Function(s As String) As String
                Throw New InvalidOperationException("boom")
            End Function
        Dim spec As New PromptSpec("t", "m", "", "OK", thrower)
        Dim outcome As PromptOutcome = AskRules.Evaluate(spec, "x")
        Assert.False(outcome.Accepted)
        Assert.False(String.IsNullOrWhiteSpace(outcome.Problem))
    End Sub

    ' --- rename ----------------------------------------------------------------------------------

    Private Shared Function Rename(typed As String, Optional taken As Func(Of String, Boolean) = Nothing) As PromptOutcome
        Return AskRules.Evaluate(AskSpecs.RenamePrompt("photo", ".jpg", taken), typed)
    End Function

    <Theory>
    <InlineData("")>
    <InlineData("   ")>
    <InlineData("a\b")>
    <InlineData("a/b")>
    <InlineData("D:\other")>
    <InlineData("..")>
    <InlineData("what?")>
    Public Sub A_rename_to_a_bad_name_is_refused_with_a_reason_beside_the_field(typed As String)
        Dim outcome As PromptOutcome = Rename(typed)
        Assert.False(outcome.Accepted)
        Assert.False(String.IsNullOrWhiteSpace(outcome.Problem))
    End Sub

    <Fact>
    Public Sub A_rename_to_the_same_name_is_refused()
        Assert.False(Rename("photo").Accepted)
    End Sub

    <Fact>
    Public Sub A_rename_onto_an_existing_file_is_refused_but_a_free_name_passes()
        Dim exists As Func(Of String, Boolean) = Function(name) name = "taken.jpg"
        Assert.False(Rename("taken", exists).Accepted)
        Assert.True(Rename("free", exists).Accepted)
    End Sub

    ''' <summary>photo.jpg -> Photo.jpg is the same file answering File.Exists; the caller's callback says "not
    ''' taken", and the validator must let it through.</summary>
    Private Shared Function CaseOnlyRenameIsNotACollision() As Boolean
        Return Rename("Photo", Function(name) False).Accepted
    End Function

    <Fact>
    Public Sub A_case_only_rename_passes_when_the_caller_says_the_target_is_the_file_itself()
        Assert.True(CaseOnlyRenameIsNotACollision())
    End Sub

    <Fact>
    Public Sub The_rename_prompt_starts_from_the_current_name_and_names_the_extension_that_stays()
        Dim spec As PromptSpec = AskSpecs.RenamePrompt("photo", ".jpg", Nothing)
        Assert.Equal("photo", spec.InitialText)
        Assert.Contains(".jpg", spec.Hint)
    End Sub

    ' --- go to a number -------------------------------------------------------------------------

    <Theory>
    <InlineData("1", True)>
    <InlineData("42", True)>
    <InlineData(" 7 ", True)>
    <InlineData("0", False)>
    <InlineData("43", False)>
    <InlineData("-1", False)>
    <InlineData("abc", False)>
    <InlineData("", False)>
    <InlineData("1.5", False)>
    Public Sub A_file_number_is_checked_against_the_folder_while_the_prompt_is_up(typed As String, accepted As Boolean)
        Dim spec As PromptSpec = AskSpecs.JumpToNumberPrompt(3, 42)
        Dim outcome As PromptOutcome = AskRules.Evaluate(spec, typed)
        Assert.Equal(accepted, outcome.Accepted)
        If Not accepted Then Assert.False(String.IsNullOrWhiteSpace(outcome.Problem))
    End Sub

    <Fact>
    Public Sub The_jump_prompt_starts_from_the_current_number_and_shows_the_range()
        Dim spec As PromptSpec = AskSpecs.JumpToNumberPrompt(3, 42)
        Assert.Equal("3", spec.InitialText)
        Assert.Contains("42", spec.Hint)
    End Sub

    ' --- open a URL -----------------------------------------------------------------------------

    <Fact>
    Public Sub An_empty_address_is_refused_and_cancel_is_a_different_thing()
        Dim spec As PromptSpec = AskSpecs.OpenUrlPrompt("")
        Assert.False(AskRules.Evaluate(spec, "").Accepted)
        Assert.False(AskRules.Evaluate(spec, "   ").Accepted)
        Assert.True(AskRules.Evaluate(spec, "http://server/video.mp4").Accepted)
    End Sub

    <Fact>
    Public Sub The_url_prompt_has_the_instruction_and_the_examples_in_their_own_places()
        Dim spec As PromptSpec = AskSpecs.OpenUrlPrompt("http://last")
        Assert.Equal("http://last", spec.InitialText)
        Assert.False(String.IsNullOrWhiteSpace(spec.Body))
        Assert.Contains("smb://", spec.Hint)
        Assert.Contains("sftp://", spec.Hint)
        Assert.DoesNotContain("smb://", spec.Body)
    End Sub

End Class
#End If
