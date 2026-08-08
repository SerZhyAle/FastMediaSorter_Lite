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
            Me.IsSingleInstance = Not MultiWindowPolicy.AllowNewWindows()
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
