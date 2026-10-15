Option Strict On
Imports System.Drawing
Imports System.IO
Imports System.Reflection
Imports System.Text.Json
Imports System.Threading.Tasks
Imports System.Windows.Forms
Imports FastMediaSorterCompanion
Imports Xunit

<Collection("manager-welcome")>
Public Class ResourceAddTests
    Private Const Flags As BindingFlags = BindingFlags.Instance Or BindingFlags.NonPublic
    Private Shared Function Field(Of T)(target As Object, name As String) As T
        Return DirectCast(target.GetType().GetField(name, Flags).GetValue(target), T)
    End Function
    Private Shared Function Invoke(target As Object, name As String, ParamArray args As Object()) As Object
        Return target.GetType().GetMethod(name, Flags).Invoke(target, args)
    End Function
    Private Shared Sub Advance(form As Resource_Add_Form)
        DirectCast(Invoke(form, "AdvanceAsync"), Task).GetAwaiter().GetResult()
    End Sub
    Private Shared Sub Isolated(action As Action)
        ExchangeTestSupport.OnUi(Sub()
            Dim app = CompanionGlobals.SettingsAppNameOverride
            Dim gate = Environment.GetEnvironmentVariable(ExchangeFeatures.DeveloperSwitchName)
            Dim code = Localization.CurrentCode
            Dim mapField = GetType(ShareRootParamsStore).GetField("_map", BindingFlags.NonPublic Or BindingFlags.Static)
            Dim oldMap = mapField.GetValue(Nothing)
            Dim scratch = "FmsResourceAddTest_" & Guid.NewGuid().ToString("N")
            CompanionGlobals.SettingsAppNameOverride = scratch
            mapField.SetValue(Nothing, Nothing)
            Environment.SetEnvironmentVariable(ExchangeFeatures.DeveloperSwitchName, "1")
            Try
                Localization.CurrentCode = "en"
                ButtonGlyphs.AssetOpener = AddressOf CompanionAssets.OpenBundledAsset
                action()
            Finally
                CompanionGlobals.SettingsAppNameOverride = app
                mapField.SetValue(Nothing, oldMap)
                Localization.CurrentCode = code
                Environment.SetEnvironmentVariable(ExchangeFeatures.DeveloperSwitchName, gate)
                Try : Microsoft.VisualBasic.Interaction.DeleteSetting(scratch) : Catch : End Try
            End Try
        End Sub)
    End Sub
    Friend Class Actions
        Implements IResourceAddActions
        Public Commits As Integer
        Public Pairs As Integer
        Public Warnings As Integer
        Public IsDuplicate As Boolean
        Public Saved As ShareRootParams
        Public Result As New ResourceAddResult With {.Added = True, .Delivery = ResourceAddDelivery.NoPhone}
        Public ReadOnly Property CanPickDisks As Boolean Implements IResourceAddActions.CanPickDisks
            Get
                Return True
            End Get
        End Property
        Public Function Pick(owner As Form, disk As Boolean) As String Implements IResourceAddActions.Pick
            Return "C:\ResourceScene"
        End Function
        Public Function Duplicate(path As String) As Boolean Implements IResourceAddActions.Duplicate
            Return IsDuplicate
        End Function
        Public Sub WarnNetwork(owner As Form, path As String) Implements IResourceAddActions.WarnNetwork
            Warnings += 1
        End Sub
        Public Function Parameters(path As String) As ShareRootParams Implements IResourceAddActions.Parameters
            Return New ShareRootParams With {.Profile = "all_files"}
        End Function
        Public Function FolderName(path As String) As String Implements IResourceAddActions.FolderName
            Return "ResourceScene"
        End Function
        Public Function CommitAsync(owner As Form, path As String, parameters As ShareRootParams, exchange As Boolean, publish As Boolean) As Task(Of ResourceAddResult) Implements IResourceAddActions.CommitAsync
            Commits += 1
            Saved = parameters.Clone()
            ShareRootParamsStore.SetFor(path, parameters)
            Return Task.FromResult(Result)
        End Function
        Public Sub Pair(owner As Form) Implements IResourceAddActions.Pair
            Pairs += 1
        End Sub
        Public Sub Settings(owner As Form) Implements IResourceAddActions.Settings
        End Sub
    End Class

    <Fact>
    Public Sub Four_steps_are_rerunnable_and_never_offered_for_forwards_or_a_shut_exchange_gate()
        For Each entry As ResourceAddEntry In [Enum].GetValues(Of ResourceAddEntry)()
            Assert.Equal(If(entry = ResourceAddEntry.Forwarded, 0, 4), ResourceAddModel.Steps(entry, True).Length)
        Next
        Assert.Empty(ResourceAddModel.Steps(ResourceAddEntry.Exchange, False))
        Assert.Equal(ResourceAddStep.Pick, ResourceAddModel.Start(ResourceAddEntry.EmptyList))
        Assert.Equal(ResourceAddStep.Pick, ResourceAddModel.Start(ResourceAddEntry.Welcome))
        Assert.Equal(ResourceAddStep.Introduction, ResourceAddModel.Start(ResourceAddEntry.AddButton))
        Assert.True(ResourceAddModel.EmptyHint(0))
        Assert.False(ResourceAddModel.EmptyHint(1))
        Assert.False(ResourceAddModel.CanAdvance(ResourceAddStep.Pick, False, False, False))
    End Sub

    <Fact>
    Public Sub Cancel_including_after_question_Next_keeps_the_hive_and_store_exactly_unchanged()
        Isolated(Sub()
            ShareRootParamsStore.SetFor("C:\Existing", New ShareRootParams With {.AccessPin = "existing-pin"})
            Dim before = Microsoft.VisualBasic.Interaction.GetSetting(CompanionGlobals.App_name, CompanionGlobals.Second_App_Name, "Share_RootParams")
            For Each stepKind In {ResourceAddStep.Pick, ResourceAddStep.Questions, ResourceAddStep.Finish}
                Dim actions As New Actions()
                Using form As New Resource_Add_Form(actions, ResourceAddEntry.EmptyList)
                    Invoke(form, "AcceptPick", "C:\ResourceScene")
                    Invoke(form, "ShowStep", ResourceAddStep.Questions)
                    Field(Of TextBox)(Field(Of Share_Root_Params_Form)(form, "editor"), "txtPin").Text = "new-pin"
                    Advance(form)
                    Invoke(form, "ShowStep", stepKind)
                    Invoke(form, "OnFormClosing", New FormClosingEventArgs(CloseReason.UserClosing, False))
                    Assert.Equal(0, actions.Commits)
                End Using
                Assert.Equal(before, Microsoft.VisualBasic.Interaction.GetSetting(CompanionGlobals.App_name, CompanionGlobals.Second_App_Name, "Share_RootParams"))
                Assert.True(ShareRootParamsStore.GetFor("C:\ResourceScene").IsDefault())
            Next
        End Sub)
    End Sub

    <Fact>
    Public Sub Finish_commits_once_and_does_not_open_pairing_or_enable_anything()
        Isolated(Sub()
            Dim enabledBefore = ServerFeatures.IsEnabled()
            Dim actions As New Actions()
            Using form As New Resource_Add_Form(actions, ResourceAddEntry.AddButton)
                Invoke(form, "AcceptPick", "C:\ResourceScene")
                Invoke(form, "ShowStep", ResourceAddStep.Questions)
                Field(Of TextBox)(Field(Of Share_Root_Params_Form)(form, "editor"), "txtLabel").Text = "Scene label"
                Advance(form)
                Assert.Equal(0, actions.Commits)
                Advance(form)
                Assert.Equal(1, actions.Commits)
                Assert.Equal("Scene label", ShareRootParamsStore.GetFor("C:\ResourceScene").Label)
                Assert.Equal(0, actions.Pairs)
                Assert.True(Field(Of Boolean)(form, "committed"))
                Advance(form)
                Assert.Equal(1, actions.Commits)
            End Using
            Assert.Equal(enabledBefore, ServerFeatures.IsEnabled())
        End Sub)
    End Sub

    <Fact>
    Public Sub Duplicate_pick_and_duplicate_at_finish_never_make_a_second_row()
        Isolated(Sub()
            Dim actions As New Actions With {.IsDuplicate = True}
            Using form As New Resource_Add_Form(actions, ResourceAddEntry.EmptyList)
                Invoke(form, "AcceptPick", "C:\ResourceScene")
                Assert.Equal("", Field(Of String)(form, "picked"))
                Assert.Equal(0, actions.Warnings)
                actions.IsDuplicate = False
                Invoke(form, "AcceptPick", "C:\ResourceScene")
                Invoke(form, "ShowStep", ResourceAddStep.Questions)
                Advance(form)
                actions.IsDuplicate = True
                Advance(form)
                Assert.Equal(ResourceAddStep.Pick, Field(Of ResourceAddStep)(form, "position"))
                Assert.Equal(0, actions.Commits)
            End Using
        End Sub)
    End Sub

    <Fact>
    Public Sub Hosted_and_standalone_editors_have_the_same_tokens_writability_and_PIN()
        Isolated(Sub()
            Dim input As New ShareRootParams With {.Profile = "photo_storage", .MediaTypes = New List(Of String) From {"image", "pdf"},
                .IsDestination = True, .SoftReadOnly = True, .ShowHiddenFiles = True, .AccessPin = "scene-pin", .Comment = "scene", .SlideshowInterval = 17}
            Using standalone As New Share_Root_Params_Form("folder", input), hosted As New Share_Root_Params_Form("folder", input)
                Using control = hosted.CreateHostedEditor(600)
                    Assert.Equal(JsonSerializer.Serialize(standalone.ReadDraft()), JsonSerializer.Serialize(hosted.ReadDraft()))
                    Assert.True(hosted.ReadDraft().IsWritable())
                    Assert.False(hosted.ReadDraft().AdvertisedReadOnly())
                    Assert.True(Field(Of TextBox)(hosted, "txtPin").UseSystemPasswordChar)
                End Using
            End Using
            Assert.Equal("scene-pin", input.AccessPin)
        End Sub)
    End Sub

    <Fact>
    Public Sub Confirmed_default_profile_is_not_replaced_and_only_commit_changes_last_touched_root()
        Isolated(Sub()
            Using host As New MainWindow()
                Dim before = Field(Of String)(host, "_lastTouchedRoot")
                Assert.True(ShareRootParamsStore.TrySetFor("C:\DefaultScene", New ShareRootParams()))
                Assert.True(DirectCast(Invoke(host, "AddShareRow", "C:\DefaultScene", True), Boolean))
                Assert.True(ShareRootParamsStore.GetFor("C:\DefaultScene").IsDefault())
                Assert.NotEqual(before, Field(Of String)(host, "_lastTouchedRoot"))
                Assert.Single(Field(Of ListView)(host, "lvFolders").Items.Cast(Of ListViewItem)())
            End Using
        End Sub)
    End Sub

    <Fact>
    Public Sub Exchange_entry_is_absent_with_the_gate_shut_and_finish_uses_real_block_words()
        Isolated(Sub()
            Using form As New Resource_Add_Form(New Actions(), ResourceAddEntry.Exchange, currentPublish:=True)
                Assert.True(Field(Of CheckBox)(form, "publish").Checked)
            End Using
            Using ExchangeFeatures.ForFlow(offered:=False, switchOn:=False)
                Assert.Throws(Of InvalidOperationException)(Function() New Resource_Add_Form(New Actions(), ResourceAddEntry.Exchange))
            End Using
            For Each block In {ExchangePublishBlock.ServerDown, ExchangePublishBlock.PinLocked, ExchangePublishBlock.NoAddress, ExchangePublishBlock.NoShares}
                Dim result As New ResourceAddResult With {.Added = True, .Delivery = ResourceAddDelivery.NoPhone, .Publication = ExchangePublishText.StateBlocked(block)}
                Assert.Contains(ExchangePublishText.StateBlocked(block), ResourceAddText.Finish(result))
            Next
        End Sub)
    End Sub

    <Fact>
    Public Sub The_single_commit_uses_existing_engines_and_never_logs_values_or_creates_a_welcome_marker()
        Dim actions = ExchangeTestSupport.CodeOnly(ExchangeTestSupport.ReadSource("Forms\MainWindow.ResourceAdd.vb"))
        Assert.Equal(1, System.Text.RegularExpressions.Regex.Matches(actions, "host\.AddShareRow\(").Count)
        Assert.Equal(1, System.Text.RegularExpressions.Regex.Matches(actions, "host\.ApplySharedFoldersAsync\(").Count)
        Assert.Contains("publisher.ApplyNowAsync", actions)
        Assert.True(actions.IndexOf("SetPublishShares", StringComparison.Ordinal) < actions.IndexOf("host.ApplySharedFoldersAsync", StringComparison.Ordinal))
        For Each source In {"Forms\Resource_Add_Form.vb", "Forms\MainWindow.ResourceAdd.vb", "Core\ResourceAddActions.vb", "Core\ResourceAddText.vb"}
            Dim code = ExchangeTestSupport.CodeOnly(ExchangeTestSupport.ReadSource(source))
            Assert.DoesNotContain("AppFileLogger", code)
            Assert.DoesNotContain("SaveSetting", code)
            Assert.DoesNotContain("Welcome_", code)
            Assert.DoesNotContain("New Share_Enable_Form", code)
        Next
        Dim controller = ExchangeTestSupport.ReadSource("Core\ShareController.vb")
        Assert.True(controller.IndexOf("If Not startServing Then Return", StringComparison.Ordinal) < controller.IndexOf("Dim startResp", StringComparison.Ordinal))
    End Sub
End Class
