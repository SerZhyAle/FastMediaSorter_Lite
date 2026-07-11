Option Strict On

''' <summary>
''' Shared RU/EN copy for the internet-access half of the Share feature, used by
''' both the Settings Share tab and the quick-share wizard. House prose style:
''' plain hyphen (never em-dash), the letter "ё" where correct, ".." not "..".
''' </summary>
Public Module ShareText

    ''' <summary>Warning shown when the user opts into internet access - one SFTP
    ''' server, one credential, all folders exposed the moment a port is open.</summary>
    Public Function SecurityText(rus As Boolean) As String
        Return If(rus,
            "Внимание: вы открываете SFTP-сервер в интернет. Доступ ко всем общим папкам получит любой, кто узнает адрес, логин и пароль. Не публикуйте QR-код и файл .fmscfg. Выключайте доступ, когда закончили.",
            "Warning: you are exposing an SFTP server to the internet. Anyone who learns the address, username and password can reach every shared folder. Do not publish the QR code or the .fmscfg file. Switch it off when you are done.")
    End Function

    ''' <summary>Honest CGNAT explanation - forwarding cannot help.</summary>
    Public Function CgnatText(rus As Boolean) As String
        Return If(rus,
            "Ваш провайдер использует CGNAT (общий внешний адрес). Проброс портов не поможет - извне к этому ПК подключиться нельзя. По локальной сети всё работает как обычно.",
            "Your ISP uses CGNAT (a shared public address). Port forwarding will not help - this PC cannot be reached from outside. Local-network sharing works as usual.")
    End Function

    ''' <summary>Step-by-step router port-forward instructions with the concrete
    ''' values filled in. The external address is already embedded in the QR /
    ''' .fmscfg (ShareConfigBuilder), so the closing line just tells the user to
    ''' rescan/re-save after the forward takes effect.</summary>
    Public Function PortForwardText(rus As Boolean, router As String, extPort As Integer, lanIp As String, port As Integer) As String
        If rus Then
            Return "Внешний адрес уже добавлен в QR-код и файл .fmscfg. Чтобы он заработал, пробросьте порт на роутере:" & vbCrLf &
                   "1. Откройте роутер: " & router & " (кнопка «Открыть роутер»)." & vbCrLf &
                   "2. Войдите (логин и пароль обычно на наклейке снизу роутера)." & vbCrLf &
                   "3. Найдите раздел «Проброс портов» (Port Forwarding / Virtual Server)." & vbCrLf &
                   "4. Добавьте правило: внешний порт " & extPort.ToString() & " -> " & lanIp & ":" & port.ToString() & ", протокол TCP." & vbCrLf &
                   "5. Сохраните правило - и заново отсканируйте QR-код (или сохраните .fmscfg) на телефоне."
        Else
            Return "The external address is already in the QR code and .fmscfg file. To make it work, forward the port on your router:" & vbCrLf &
                   "1. Open the router: " & router & " (the ""Open router"" button)." & vbCrLf &
                   "2. Sign in (login and password are usually on a sticker under the router)." & vbCrLf &
                   "3. Find the ""Port Forwarding"" section (Virtual Server / NAT)." & vbCrLf &
                   "4. Add a rule: external port " & extPort.ToString() & " -> " & lanIp & ":" & port.ToString() & ", protocol TCP." & vbCrLf &
                   "5. Save the rule - then rescan the QR code (or save the .fmscfg) on the phone."
        End If
    End Function

End Module
