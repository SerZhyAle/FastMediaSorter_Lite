Option Explicit On
Option Strict On
Option Infer On
Option Compare Binary

Imports System.Drawing
Imports System.IO
Imports System.Linq
Imports System.Reflection
Imports System.Text.RegularExpressions
Imports System.Windows.Forms
Imports FastMediaSorterCompanion
Imports Xunit

''' <summary>
''' The About group of the Share Manager's "Справка" page (ticket 243 T-6, SETTINGS-NAVIGATION 2.0 section 3 rules 3 and 4):
''' every one of the twelve required items has its control under the name <see cref="AboutItems.ManagerControl"/> gives it,
''' building the page opens nothing (only a click may), a click goes through <see cref="AboutResources"/>, and a link row is a
''' pointer-target high hit area. The viewer's half is Lite.Tests/AboutPageTests.vb.
''' </summary>
<Collection(UiLanguageCollection.Name)>
Public Class AboutPageTests

    Private Const BuilderFile As String = "Forms\Share_Settings_Form.About.vb"
    Private Const NetworkTokens As String = "Process\.Start|HttpClient|WebClient|WebRequest|HttpWebRequest|TcpClient|\bSocket\b|\bDns\.|\bPing\b"

    ' --- source: the inventory and rule 3.4 ----------------------------------------------------------------

    <Fact>
    Public Sub TheManagerBuilderNamesAControlForEveryRequiredItem()
        Dim builder As String = ExchangeTestSupport.CodeOnly(ExchangeTestSupport.ReadSource(BuilderFile))
        Assert.Equal(12, AboutItems.Required.Length)
        For Each item As String In AboutItems.Required
            Dim name As String = AboutItems.ManagerControl(item)
            Assert.False(String.IsNullOrEmpty(name), item)
            Assert.Contains("""" & name & """", builder)
        Next
    End Sub

    <Fact>
    Public Sub TheManagerAboutBuilderStartsNoProcessAndTouchesNoNetwork()
        Dim builder As String = ExchangeTestSupport.CodeOnly(ExchangeTestSupport.ReadSource(BuilderFile))
        Assert.False(Regex.IsMatch(builder, NetworkTokens), "the About builder reaches for a process or the network")
        ' The click handler is the one place an address is opened, and it goes through AboutResources.
        Dim handler As String = ExchangeTestSupport.MethodBody(ExchangeTestSupport.ReadSource(BuilderFile), "Private Sub OnAboutLinkClicked(")
        Assert.Contains("AboutResources.Open(", handler)
        ' The Help page keeps its own links (the guide, the router search, the viewer): none of them is built here.
        Dim host As String = ExchangeTestSupport.MethodBody(ExchangeTestSupport.ReadSource("Forms\Share_Settings_Form.vb"), "Private Sub BuildHelpSection(")
        Assert.DoesNotContain("Process.Start", host)
    End Sub

    <Fact>
    Public Sub TheHelpPageStillStartsWithTheStartupPageAndEndsWithTheAboutGroup()
        Dim host As String = ExchangeTestSupport.CodeOnly(ExchangeTestSupport.ReadSource("Forms\Share_Settings_Form.vb"))
        Dim startup As Integer = host.IndexOf("CreateSettingsSection(""startup""", StringComparison.Ordinal)
        Dim help As Integer = host.IndexOf("CreateSettingsSection(""help""", StringComparison.Ordinal)
        Assert.True(startup >= 0 AndAlso help > startup, "the startup page must come first and the help page last")
        Dim about As Integer = host.IndexOf("BuildAboutRows(_secHelp)", help, StringComparison.Ordinal)
        Assert.True(about > help)
        ' The support-bundle command stays the only one in the settings (owner decision), inside the About group.
        Assert.Single(Regex.Matches(ExchangeTestSupport.CodeOnly(ExchangeTestSupport.ReadSource(BuilderFile)), """managerSupportBundle"""))
    End Sub

    ' --- the built window ---------------------------------------------------------------------------------

    Private Shared Function FindByName(parent As Control, name As String) As Control
        For Each child As Control In parent.Controls
            If String.Equals(child.Name, name, StringComparison.Ordinal) Then Return child
            Dim nested As Control = FindByName(child, name)
            If nested IsNot Nothing Then Return nested
        Next
        Return Nothing
    End Function

    Private Shared Iterator Function Below(parent As Control) As IEnumerable(Of Control)
        For Each child As Control In parent.Controls
            Yield child
            For Each nested As Control In Below(child)
                Yield nested
            Next
        Next
    End Function

    Private Shared Function HelpPage(window As Share_Settings_Form) As Panel
        Dim pages = DirectCast(GetType(Share_Settings_Form).GetField("_pages", BindingFlags.Instance Or BindingFlags.NonPublic).GetValue(window), Dictionary(Of String, Panel))
        Return pages("help")
    End Function

    Private Shared Sub ClickLink(window As Share_Settings_Form, link As Control)
        GetType(Share_Settings_Form).GetMethod("OnAboutLinkClicked", BindingFlags.Instance Or BindingFlags.NonPublic).
            Invoke(window, New Object() {link, New LinkLabelLinkClickedEventArgs(New LinkLabel.Link())})
    End Sub

    <Fact>
    Public Sub TheBuiltAboutGroupHoldsEveryRequiredItemAndOpensNothingOnBuilding()
        Dim savedCode As String = Localization.CurrentCode
        Dim oldLaunch As Action(Of String) = AboutResources.Launch
        Dim oldFile As Action(Of String) = AboutResources.LaunchFile
        Dim oldFrom As String = AboutResources.SearchFrom
        Dim empty As String = Path.Combine(Path.GetTempPath(), "fms-about-" & Guid.NewGuid().ToString("N"))
        Directory.CreateDirectory(empty)
        Try
            Localization.CurrentCode = "ru"
            Dim started As New List(Of String)()
            AboutResources.Launch = Sub(address) started.Add(address)
            AboutResources.LaunchFile = Sub(path) started.Add(path)
            AboutResources.SearchFrom = empty
            ExchangeTestSupport.OnUi(
                Sub()
                    Using window As New Share_Settings_Form()
                        Dim help As Panel = HelpPage(window)
                        For Each item As String In AboutItems.Required
                            Assert.True(FindByName(help, AboutItems.ManagerControl(item)) IsNot Nothing, "missing " & AboutItems.ManagerControl(item))
                        Next
                        ' Building the page started nothing: rule 3.4.
                        Assert.Empty(started)

                        Assert.IsType(Of Button)(FindByName(help, AboutItems.ManagerControl(AboutItems.Diagnostics)))
                        Assert.Equal(Localization.T("Отправить журнал автору.."), FindByName(help, "managerSupportBundle").Text)
                        Assert.Contains("MIT License", FindByName(help, AboutItems.ManagerControl(AboutItems.Licence)).Text)
                        Assert.Contains(Application.ProductVersion.Split("+"c)(0), FindByName(help, AboutItems.ManagerControl(AboutItems.Version)).Text)
                        Assert.False(String.IsNullOrWhiteSpace(FindByName(help, AboutItems.ManagerControl(AboutItems.Description)).Text))
                        Assert.Contains(Application.ProductVersion.Split("+"c)(0), FindByName(help, "managerUpdatesPurpose").Text)

                        ' Every link of the page is at least a pointer target high (UI-SIZING 1.1), the related ones included.
                        Dim links As LinkLabel() = Below(help).OfType(Of LinkLabel)().ToArray()
                        Assert.True(links.Length >= 10, "the page should hold the about links and the documentation links")
                        For Each link As LinkLabel In links
                            Assert.True(link.MinimumSize.Height >= UiSizing.PointerTarget, link.Name & " " & link.MinimumSize.ToString())
                        Next

                        ' The related projects are one compact group of exactly the table's three.
                        Dim related As Control = FindByName(help, AboutItems.ManagerControl(AboutItems.Related))
                        Assert.IsType(Of FlowLayoutPanel)(related)
                        Assert.Equal(ProductLinks.RelatedKeys.Length, Below(related).OfType(Of LinkLabel)().Count())

                        ' A click opens the table's address; a bundled file that is not beside the program opens its web page.
                        ClickLink(window, FindByName(help, AboutItems.ManagerControl(AboutItems.Site)))
                        ClickLink(window, FindByName(help, AboutItems.ManagerControl(AboutItems.Notices)))
                        ClickLink(window, FindByName(help, AboutItems.ManagerControl(AboutItems.Repository)))
                        ClickLink(window, FindByName(help, AboutItems.ManagerControl(AboutItems.Support)))
                        Assert.Equal({ProductLinks.Url(ProductLinks.Site, "ru"), ProductLinks.NoticesOnlineUrl, ProductLinks.RepositoryUrl,
                                      ProductLinks.Url(ProductLinks.Contact, "ru")}, started.ToArray())
                    End Using
                End Sub)
        Finally
            AboutResources.Launch = oldLaunch
            AboutResources.LaunchFile = oldFile
            AboutResources.SearchFrom = oldFrom
            Localization.CurrentCode = savedCode
            Directory.Delete(empty, True)
        End Try
    End Sub

    <Fact>
    Public Sub TheAboutTextsFollowALanguageSwitch()
        Dim savedCode As String = Localization.CurrentCode
        Try
            Localization.CurrentCode = "ru"
            ExchangeTestSupport.OnUi(
                Sub()
                    Using window As New Share_Settings_Form()
                        Dim site As Control = FindByName(HelpPage(window), AboutItems.ManagerControl(AboutItems.Site))
                        Dim russian As String = site.Text
                        Assert.Equal("Сайт проекта", russian)
                        Localization.CurrentCode = "de"
                        GetType(Share_Settings_Form).GetMethod("RefreshAboutTexts", BindingFlags.Instance Or BindingFlags.NonPublic).Invoke(window, Nothing)
                        Assert.NotEqual(russian, site.Text)
                        Assert.Equal(Localization.T("Сайт проекта"), site.Text)
                        Assert.Equal(site.Text, site.AccessibleName)
                    End Using
                End Sub)
        Finally
            Localization.CurrentCode = savedCode
        End Try
    End Sub

End Class
