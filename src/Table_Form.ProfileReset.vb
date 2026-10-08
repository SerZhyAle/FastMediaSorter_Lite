#If Not NETFRAMEWORK Then
Option Explicit On
Option Strict On
Option Infer On
Option Compare Binary

Imports System.Windows.Forms

Partial Public Class Table_Form

    Private btn_Reset_All_Settings As Button

    ''' <summary>Creates the reset settings button before the About page requests it.</summary>
    Private Sub EnsureResetAllButton()
        If btn_Reset_All_Settings IsNot Nothing Then Return
        btn_Reset_All_Settings = New Button With {.UseVisualStyleBackColor = True, .Height = 34}
        UpdateResetAllButtonState()
        AddHandler btn_Reset_All_Settings.Click, AddressOf OnResetAllClick
    End Sub

    ''' <summary>Updates enabled state, hover tooltip and accessible description according to instance role.</summary>
    Friend Sub UpdateResetAllButtonState()
        If btn_Reset_All_Settings Is Nothing Then Return
        If MultiWindowPolicy.IsSecondaryInstance() Then
            Dim reason As String = Localization.T("Сброс настроек выполняется из первого окна программы. Закройте это окно и откройте настройки там.")
            btn_Reset_All_Settings.Enabled = False
            btn_Reset_All_Settings.AccessibleDescription = reason
            If toolTip IsNot Nothing Then
                toolTip.SetToolTip(btn_Reset_All_Settings, reason)
            End If
        Else
            btn_Reset_All_Settings.Enabled = True
            btn_Reset_All_Settings.AccessibleDescription = String.Empty
            If toolTip IsNot Nothing Then
                toolTip.SetToolTip(btn_Reset_All_Settings, String.Empty)
            End If
        End If
    End Sub

    Private Sub OnResetAllClick(sender As Object, e As EventArgs)
        If MultiWindowPolicy.IsSecondaryInstance() Then Return

        Dim store As New VbSettingsProfileStore()
        If Not ProfileResetRunner.HasAnyResetSettings(store) Then
            ViewerBox.Show(Me,
                Localization.T("Настройки уже имеют значения по умолчанию - сбрасывать нечего."),
                Localization.T("Сброс настроек"),
                MessageBoxButtons.OK,
                MessageBoxIcon.Information)
            Return
        End If

        Dim answer As Integer = AskDialogs.Choose(Me, AskSpecs.ProfileResetConfirm())
        If answer <> AskSpecs.ProfileResetExecute Then Return ' Index 0 is "Сбросить и закрыть"; Index 1 is "Отмена"

        Try
            ProfileResetRunner.RecordResetRequest(store)
            Main_Form.RequestResetClose()
            Me.Close()
        Catch ex As Exception
            AppFileLogger.LogException("Profile reset request", ex)
            ViewerBox.Show(Me,
                Localization.TF("Не удалось запросить сброс настроек: {0}", FailureText.Describe(ex)),
                Localization.T("Ошибка"),
                MessageBoxButtons.OK,
                MessageBoxIcon.Error)
        End Try
    End Sub

End Class
#End If
