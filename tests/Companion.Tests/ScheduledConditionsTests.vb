Option Strict On

Imports FastMediaSorterCompanion
Imports Xunit

''' <summary>Selection conditions of scheduled operations (ticket 222, sections 4.1 and 4.2): pure, no file system.</summary>
Public Class ScheduledConditionsTests

    Private Shared ReadOnly Now_ As New DateTime(2026, 10, 13, 12, 0, 0, DateTimeKind.Unspecified)

    Private Shared Function Entry(name As String, Optional length As Long = 100, Optional hoursOld As Double = 10, Optional hidden As Boolean = False) As SideEntry
        Dim item As SideEntry = SideEntry.WrittenAtLocal(name, length, Now_.AddHours(-hoursOld))
        item.IsHiddenOrSystem = hidden
        Return item
    End Function

    Private Shared Function Matcher(conditions As ScheduledConditions, Optional since As DateTime? = Nothing) As ConditionMatcher
        Return New ConditionMatcher(conditions, Now_, since, Function(n As String) MediaKindClassifier.KindOf(n))
    End Function

    ' --- masks ---------------------------------------------------------------------------------------

    <Fact>
    Public Sub Glob_separates_on_semicolon_drops_blank_parts_and_ignores_case()
        Dim mask As GlobMask = GlobMask.Parse(" *.JPG ; ; IMG_??.png;;")
        Assert.False(mask.IsEmpty)
        Assert.True(mask.IsMatch("holiday.jpg"))
        Assert.True(mask.IsMatch("img_01.PNG"))
        Assert.False(mask.IsMatch("img_001.png"))
        Assert.False(mask.IsMatch("holiday.jpeg"))
    End Sub

    <Fact>
    Public Sub Glob_empty_or_blank_is_no_condition()
        Assert.True(GlobMask.Parse("").IsEmpty)
        Assert.True(GlobMask.Parse("  ; ;").IsEmpty)
        Assert.True(GlobMask.Parse(Nothing).IsEmpty)
    End Sub

    <Fact>
    Public Sub Glob_star_matches_across_dots_and_does_not_backtrack_for_ever()
        Assert.True(GlobMask.Parse("*.tar.gz").IsMatch("a.b.tar.gz"))
        Assert.True(GlobMask.Parse("a*b*c").IsMatch("aXXbYYc"))
        Assert.False(GlobMask.Parse("a*b*c").IsMatch("aXXbYY"))
        ' A pattern that would melt a regular expression answers at once.
        Dim name As String = New String("a"c, 5000)
        Assert.False(GlobMask.Parse("*a*a*a*a*a*a*a*a*a*a*b").IsMatch(name))
    End Sub

    <Fact>
    Public Sub Name_mask_matches_the_file_name_only_never_the_path()
        Dim c As New ScheduledConditions With {.NameMask = "photos*"}
        Dim inFolder As SideEntry = Entry("report.txt")
        inFolder.RelativePath = "photos\report.txt"
        Assert.False(Matcher(c).Matches(inFolder))
        Assert.True(Matcher(c).Matches(Entry("photos 2026.jpg")))
    End Sub

    <Fact>
    Public Sub Exclude_mask_removes_what_the_include_mask_selected()
        Dim c As New ScheduledConditions With {.NameMask = "*.jpg", .ExcludeMask = "*_thumb.jpg;draft*"}
        Dim m As ConditionMatcher = Matcher(c)
        Assert.True(m.Matches(Entry("a.jpg")))
        Assert.False(m.Matches(Entry("a_thumb.jpg")))
        Assert.False(m.Matches(Entry("draft1.jpg")))
        ' "Everything except *.tmp" with no include mask at all.
        Dim onlyExclude As New ScheduledConditions With {.ExcludeMask = "*.tmp"}
        Assert.True(Matcher(onlyExclude).Matches(Entry("a.jpg")))
        Assert.False(Matcher(onlyExclude).Matches(Entry("a.TMP")))
    End Sub

    ' --- windows -------------------------------------------------------------------------------------

    <Fact>
    Public Sub An_empty_conditions_object_selects_everything_visible()
        Dim m As ConditionMatcher = Matcher(New ScheduledConditions())
        Assert.True(m.Matches(Entry("a.jpg")))
        Assert.True(m.Matches(Entry("noext", length:=0)))
        Assert.True(m.Matches(Entry("a.zip", hoursOld:=100000)))
    End Sub

    <Fact>
    Public Sub Size_window_is_strict_and_larger_than_zero_skips_empty_files()
        Dim c As New ScheduledConditions With {.MinSizeBytes = 0}
        Assert.False(Matcher(c).Matches(Entry("empty.txt", length:=0)))
        Assert.True(Matcher(c).Matches(Entry("one.txt", length:=1)))

        Dim window As New ScheduledConditions With {.MinSizeBytes = 100, .MaxSizeBytes = 200}
        Assert.False(Matcher(window).Matches(Entry("a", length:=100)))
        Assert.True(Matcher(window).Matches(Entry("a", length:=101)))
        Assert.True(Matcher(window).Matches(Entry("a", length:=199)))
        Assert.False(Matcher(window).Matches(Entry("a", length:=200)))

        ' "Smaller than 0" matches nothing.
        Dim none As New ScheduledConditions With {.MaxSizeBytes = 0}
        Assert.False(Matcher(none).Matches(Entry("a", length:=0)))
        Assert.False(Matcher(none).Matches(Entry("a", length:=5)))
    End Sub

    <Fact>
    Public Sub Age_window_is_measured_from_last_write_time_in_hours()
        Dim c As New ScheduledConditions With {.MinAgeHours = 24, .MaxAgeHours = 72}
        Assert.False(Matcher(c).Matches(Entry("a", hoursOld:=23)))
        Assert.True(Matcher(c).Matches(Entry("a", hoursOld:=25)))
        Assert.True(Matcher(c).Matches(Entry("a", hoursOld:=71)))
        Assert.False(Matcher(c).Matches(Entry("a", hoursOld:=73)))
    End Sub

    <Fact>
    Public Sub Time_filters_all_hour_day_and_since_last_run()
        Dim hour As New ScheduledConditions With {.TimeFilter = ScheduledTimeFilter.LastHour}
        Assert.True(Matcher(hour).Matches(Entry("a", hoursOld:=0.9)))
        Assert.False(Matcher(hour).Matches(Entry("a", hoursOld:=1.1)))

        Dim day As New ScheduledConditions With {.TimeFilter = ScheduledTimeFilter.LastDay}
        Assert.True(Matcher(day).Matches(Entry("a", hoursOld:=23)))
        Assert.False(Matcher(day).Matches(Entry("a", hoursOld:=25)))

        Dim since As New ScheduledConditions With {.TimeFilter = ScheduledTimeFilter.SinceLastRun}
        Dim reference As DateTime = Now_.AddHours(-5)
        Assert.True(Matcher(since, reference).Matches(Entry("a", hoursOld:=4)))
        Assert.False(Matcher(since, reference).Matches(Entry("a", hoursOld:=6)))
        ' Nothing has ever succeeded: everything is new.
        Assert.True(Matcher(since, Nothing).Matches(Entry("a", hoursOld:=100000)))
    End Sub

    ' --- scan layer ----------------------------------------------------------------------------------

    <Fact>
    Public Sub Hidden_and_system_files_are_skipped_unless_asked_for()
        Assert.False(Matcher(New ScheduledConditions()).Matches(Entry("a.jpg", hidden:=True)))
        Assert.True(Matcher(New ScheduledConditions With {.IncludeHidden = True}).Matches(Entry("a.jpg", hidden:=True)))
    End Sub

    <Fact>
    Public Sub Type_mask_uses_the_viewers_classifier_and_all_files_wins()
        Dim images As New ScheduledConditions With {.TypeMask = ScheduledTypeMask.Images}
        Assert.True(Matcher(images).Matches(Entry("a.JPG")))
        Assert.False(Matcher(images).Matches(Entry("a.mp4")))
        Assert.False(Matcher(images).Matches(Entry("a.txt")))

        Dim docs As New ScheduledConditions With {.TypeMask = ScheduledTypeMask.Documents}
        Assert.True(Matcher(docs).Matches(Entry("a.txt")))
        Assert.True(Matcher(docs).Matches(Entry("a.pdf")))

        Dim two As New ScheduledConditions With {.TypeMask = ScheduledTypeMask.Audio Or ScheduledTypeMask.Video}
        Assert.True(Matcher(two).Matches(Entry("a.mp3")))
        Assert.True(Matcher(two).Matches(Entry("a.mkv")))
        Assert.False(Matcher(two).Matches(Entry("a.png")))

        Dim all As New ScheduledConditions With {.TypeMask = ScheduledTypeMask.AllFiles Or ScheduledTypeMask.Images}
        Assert.True(Matcher(all).Matches(Entry("whatever.xyz")))
    End Sub

    ' --- validation ----------------------------------------------------------------------------------

    Private Shared Function Op(action As ScheduledAction, source As String, Optional destination As String = "") As ScheduledOperation
        Dim o As New ScheduledOperation With {.Id = "x", .Action = action}
        o.Source.Path = source
        o.Destination.Path = destination
        Return o
    End Function

    Private Shared Function Codes(o As ScheduledOperation, Optional protect As ProtectedFolders = Nothing) As List(Of ScheduledIssueCode)
        Return ScheduledValidation.Validate(o, protect).Select(Function(i) i.Code).ToList()
    End Function

    <Fact>
    Public Sub A_complete_definition_has_no_issues()
        Assert.Empty(Codes(Op(ScheduledAction.Copy, "D:\Photos", "E:\Backup")))
        Assert.Empty(Codes(Op(ScheduledAction.Delete, "D:\Temp")))
    End Sub

    <Fact>
    Public Sub Missing_and_relative_paths_are_refused()
        Assert.Contains(ScheduledIssueCode.SourceMissing, Codes(Op(ScheduledAction.Copy, "", "E:\B")))
        Assert.Contains(ScheduledIssueCode.DestinationMissing, Codes(Op(ScheduledAction.Copy, "D:\A", "")))
        Assert.Contains(ScheduledIssueCode.SourceNotAbsolute, Codes(Op(ScheduledAction.Copy, "photos\2026", "E:\B")))
        Assert.Contains(ScheduledIssueCode.DestinationNotAbsolute, Codes(Op(ScheduledAction.Copy, "D:\A", "..\B")))
        ' A delete has no destination, so none is asked for.
        Assert.DoesNotContain(ScheduledIssueCode.DestinationMissing, Codes(Op(ScheduledAction.Delete, "D:\A")))
    End Sub

    <Fact>
    Public Sub Destination_inside_the_source_or_the_source_itself_is_refused()
        Assert.Contains(ScheduledIssueCode.DestinationInsideSource, Codes(Op(ScheduledAction.Move, "D:\Photos", "D:\Photos\Sorted")))
        Assert.Contains(ScheduledIssueCode.DestinationInsideSource, Codes(Op(ScheduledAction.Copy, "D:\Photos", "d:\photos\")))
        ' A sibling with the same prefix is not inside.
        Assert.DoesNotContain(ScheduledIssueCode.DestinationInsideSource, Codes(Op(ScheduledAction.Copy, "D:\Photos", "D:\Photos-backup")))
        ' The other way round is fine: copying a subfolder out to its parent.
        Assert.DoesNotContain(ScheduledIssueCode.DestinationInsideSource, Codes(Op(ScheduledAction.Copy, "D:\Photos\2026", "D:\Photos")))
    End Sub

    <Fact>
    Public Sub System_and_program_folders_are_refused_as_source_or_destination()
        Dim protect As New ProtectedFolders({"C:\Windows", "C:\Program Files"}, {"C:\", "C:\Users"})
        Assert.Contains(ScheduledIssueCode.SourceProtected, Codes(Op(ScheduledAction.Delete, "C:\Windows\Temp"), protect))
        Assert.Contains(ScheduledIssueCode.DestinationProtected, Codes(Op(ScheduledAction.Copy, "D:\A", "C:\Program Files\App"), protect))
        Assert.Contains(ScheduledIssueCode.SourceProtected, Codes(Op(ScheduledAction.Delete, "C:\"), protect))
        Assert.Contains(ScheduledIssueCode.SourceProtected, Codes(Op(ScheduledAction.Delete, "c:\users\"), protect))
        ' Below an exact-only entry is the user's own: a folder under C:\Users, a whole removable drive.
        Assert.DoesNotContain(ScheduledIssueCode.SourceProtected, Codes(Op(ScheduledAction.Delete, "C:\Users\me\Downloads"), protect))
        Assert.DoesNotContain(ScheduledIssueCode.SourceProtected, Codes(Op(ScheduledAction.Copy, "E:\", "D:\Backup"), protect))
    End Sub

    <Fact>
    Public Sub Contradictory_bounds_are_refused_at_save()
        Dim o As ScheduledOperation = Op(ScheduledAction.Copy, "D:\A", "E:\B")
        o.Conditions.MinAgeHours = 48
        o.Conditions.MaxAgeHours = 48
        Assert.Contains(ScheduledIssueCode.AgeBoundsContradict, Codes(o))
        o.Conditions.MaxAgeHours = 24
        Assert.Contains(ScheduledIssueCode.AgeBoundsContradict, Codes(o))
        o.Conditions.MaxAgeHours = 49
        Assert.DoesNotContain(ScheduledIssueCode.AgeBoundsContradict, Codes(o))

        o.Conditions.MinSizeBytes = 1000
        o.Conditions.MaxSizeBytes = 1000
        Assert.Contains(ScheduledIssueCode.SizeBoundsContradict, Codes(o))
        o.Conditions.MaxSizeBytes = 1001
        Assert.DoesNotContain(ScheduledIssueCode.SizeBoundsContradict, Codes(o))
    End Sub

    <Fact>
    Public Sub An_empty_type_selection_is_refused_and_all_files_wins_the_interlock()
        Dim o As ScheduledOperation = Op(ScheduledAction.Copy, "D:\A", "E:\B")
        o.Conditions.TypeMask = ScheduledTypeMask.None
        Assert.Contains(ScheduledIssueCode.TypeMaskEmpty, Codes(o))

        o.Conditions.TypeMask = ScheduledTypeMask.AllFiles Or ScheduledTypeMask.Video
        ScheduledValidation.Normalize(o)
        Assert.Equal(ScheduledTypeMask.AllFiles, o.Conditions.TypeMask)
    End Sub

    <Fact>
    Public Sub Archive_takes_paths_only_in_the_first_version()
        Dim o As ScheduledOperation = Op(ScheduledAction.Archive, "D:\A", "E:\B")
        o.Destination.Kind = ScheduledLocationKind.Resource
        o.Destination.ResourceId = "r1"
        Assert.Contains(ScheduledIssueCode.ArchiveNeedsPaths, Codes(o))
    End Sub

    <Fact>
    Public Sub Normalize_corrects_interval_and_drops_options_that_do_not_apply()
        Dim o As ScheduledOperation = Op(ScheduledAction.Copy, "D:\A", "E:\B")
        o.IntervalMinutes = 0
        o.DeleteOriginalsAfterVerify = True
        ScheduledValidation.Normalize(o)
        Assert.Equal(60, o.IntervalMinutes)
        Assert.False(o.DeleteOriginalsAfterVerify)

        o.IntervalMinutes = 5
        ScheduledValidation.Normalize(o)
        Assert.Equal(ScheduleMath.MinIntervalMinutes, o.IntervalMinutes)

        Dim d As ScheduledOperation = Op(ScheduledAction.Delete, "D:\A", "E:\B")
        ScheduledValidation.Normalize(d)
        Assert.True(d.Destination.IsEmpty)
    End Sub

End Class
