Option Strict On

Namespace My
    ''' <summary>
    ''' Hand-maintained replacement for My Project\Application.Designer.vb. The VB
    ''' project generator can only emit a constant IsSingleInstance value, whereas the
    ''' .NET 10 build needs the registry-backed policy before Startup runs.
    ''' </summary>
    Partial Friend Class MyApplication

        Public Sub New()
            MyBase.New(Global.Microsoft.VisualBasic.ApplicationServices.AuthenticationMode.Windows)
            ' Not for an elevated process: the framework's own single-instance pipe is
            ' created CurrentUserOnly, and an elevated and an ordinary process cannot
            ' connect to each other's pipe in either direction - Run() then throws
            ' CantStartSingleInstanceException before Startup, a crash with the file
            ' unopened (053 F-03). The mutex path in Startup covers that process instead.
            Me.IsSingleInstance = Not MultiWindowPolicy.AllowNewWindows() AndAlso
                                  Not SingleInstanceProtocol.IsProcessElevated()
            Me.EnableVisualStyles = False
            Me.SaveMySettingsOnExit = True
            Me.ShutDownStyle = Global.Microsoft.VisualBasic.ApplicationServices.ShutdownMode.AfterMainFormCloses
        End Sub

        Protected Overrides Sub OnCreateMainForm()
            Me.MainForm = Global.fmsl.Main_Form
        End Sub

        Protected Overrides Function OnInitialize(commandLineArgs As System.Collections.ObjectModel.ReadOnlyCollection(Of String)) As Boolean
            Me.MinimumSplashScreenDisplayTime = 0
            Return MyBase.OnInitialize(commandLineArgs)
        End Function
    End Class
End Namespace
