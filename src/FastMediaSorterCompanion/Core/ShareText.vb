Option Strict On

''' <summary>
''' Shared RU/EN copy for the internet-access half of the Share feature, used by
''' both the Settings Share tab and the quick-share wizard. House prose style:
''' plain hyphen (never em-dash), the letter "ё" where correct, ".." not "...".
''' </summary>
Public Module ShareText

    ''' <summary>Warning shown when the user opts into internet access - one SFTP
    ''' server, one credential, all folders exposed the moment a port is open.</summary>
    Public Function SecurityText(rus As Boolean) As String
        Return If(rus,
            "Внимание: вы открываете SFTP-сервер в интернет. Доступ ко всем общим папкам получит любой, кто узнает адрес, логин и пароль. Не публикуйте QR-код и файл .fmscfg. Выключайте доступ, когда закончили.",
            "Warning: you are exposing an SFTP server to the internet. Anyone who learns the address, username and password can reach every shared folder. Do not publish the QR code or the .fmscfg file. Switch it off when you are done.")
    End Function

    ''' <summary>Default hint under the primary QR when it is LAN-only (either the
    ''' user ticked "LAN only" or no usable internet path exists). Carries the
    ''' bearer-token caution (decision A, 2026-07-15): the QR/file IS the key, so a
    ''' light "don't show it to others" rides even on the reassuring LAN copy.</summary>
    Public Function LanHintText(rus As Boolean) As String
        Return If(rus,
            "Работает на телефоне в той же сети Wi-Fi. Ничего настраивать не нужно. Любой, у кого есть этот код или файл, получит доступ к папкам - не показывайте его посторонним.",
            "Works on a phone on the same Wi-Fi. Nothing to configure. Anyone who has this code or file can open the folders - do not show it to others.")
    End Function

    ''' <summary>Factual note (decision F, 2026-07-15): while a share runs, other
    ''' devices on the CURRENT network - including a public Wi-Fi - can reach this PC
    ''' on the share port, protected by the password. Deliberately calm, not a scare:
    ''' same-network access (home, office Wi-Fi) is an intended capability. Surfaced
    ''' near the share toggle and mirrored in the docs.</summary>
    Public Function NetworkReachNote(rus As Boolean) As String
        Return If(rus,
            "Пока раздача включена, устройства в вашей текущей сети (в том числе в публичном Wi-Fi) могут подключиться к этому ПК по паролю. Выключайте раздачу, когда она не нужна.",
            "While sharing is on, devices on your current network (including public Wi-Fi) can reach this PC using the password. Switch sharing off when you don't need it.")
    End Function

    ''' <summary>Hint under the primary QR when it carries BOTH addresses
    ''' (LAN + internet) - the S1006 default. The phone picks whichever is
    ''' reachable, so one scan works home and away; brief security nudge +
    ''' pointer to the internet-setup tab.</summary>
    Public Function CombinedHintText(rus As Boolean) As String
        Return If(rus,
            "Один код на дом и на улицу - телефон сам выберет доступный адрес. В коде есть и интернет-адрес: не публикуйте его, выключайте доступ, когда закончили. Настройка и проверка - на вкладке «Доступ из интернета».",
            "One code for home and away - the phone picks whichever address is reachable. It also carries your internet address: do not publish it, and switch sharing off when done. Setup and testing are on the 'Internet access' tab.")
    End Function

    ''' <summary>Label of the §6 "exclude password" export safeguard.</summary>
    Public Function NoPasswordText(rus As Boolean) As String
        Return If(rus, "Не включать пароль в файл/QR", "Keep the password out of the file/QR")
    End Function

    ''' <summary>Hint shown while the password is excluded: the recipient will have
    ''' to type it, so the sender needs to see and pass it on out-of-band.</summary>
    Public Function NoPasswordHint(rus As Boolean, password As String) As String
        Dim pw As String = If(String.IsNullOrEmpty(password), "-", password)
        Return If(rus,
            "Пароль не попадёт в файл/QR. Сообщите его получателю отдельно: " & pw,
            "The password stays out of the file/QR. Pass it on separately: " & pw)
    End Function

    ''' <summary>Contract §7: the config no longer fits a QR code (too many
    ''' per-folder settings) - never truncate, point the user at the file.</summary>
    Public Function QrOverflowText(rus As Boolean) As String
        Return If(rus,
            "Слишком много настроек для QR-кода - сохраните файл .fmscfg и отправьте его на телефон.",
            "Too many settings for a QR code - save the .fmscfg file and send it to the phone instead.")
    End Function

    ''' <summary>Shown on the Internet tab/wizard when a portforward path was found
    ''' but this PC's own LAN address could not be - the QR still works, just not
    ''' the fast local hop, and detection only re-runs on a fresh start/stop.</summary>
    Public Function LanUnknownText(rus As Boolean) As String
        Return If(rus,
            "Не удалось определить адрес этого ПК в локальной сети (проверьте, не включён ли VPN). Если телефон рядом, в той же сети, остановите и снова начните общий доступ, чтобы попробовать ещё раз - сейчас в QR-коде есть только адрес из интернета.",
            "Could not determine this PC's local-network address (check whether a VPN is active). If the phone is nearby on the same network, stop and start sharing again to retry - for now, only the internet address is in the QR code.")
    End Function

    ''' <summary>Shown instead of the plain UPnP-success text when the worker
    ''' could not independently cross-check that the connection is not behind a
    ''' hidden ISP-side NAT (the IP-echo query failed) - stronger than the
    ''' always-shown UPnP caveat, since here we genuinely do not know either way.</summary>
    Public Function ExternalUnverifiedText(rus As Boolean) As String
        Return If(rus,
            "Порт открыт автоматически (UPnP), но проверить, что провайдер не использует скрытый NAT, не удалось (не ответил внешний сервис проверки IP). Адрес может не работать извне - обязательно проверьте с телефона по мобильной сети.",
            "The port was opened automatically (UPnP), but checking whether your provider uses a hidden NAT failed (the external IP-check service did not respond). This address may not work from outside - be sure to test it from the phone on mobile data.")
    End Function

    ''' <summary>Honest CGNAT explanation - forwarding cannot help.</summary>
    Public Function CgnatText(rus As Boolean) As String
        Return If(rus,
            "Ваш провайдер использует CGNAT (общий внешний адрес). Проброс портов не поможет - извне к этому ПК подключиться нельзя. По локальной сети всё работает как обычно.",
            "Your ISP uses CGNAT (a shared public address). Port forwarding will not help - this PC cannot be reached from outside. Local-network sharing works as usual.")
    End Function

    ''' <summary>Short (&lt;=~200 char) reachability line embedded in the .fmscfg as
    ''' the optional "accessNote" and shown on the phone (Android S1014) when no
    ''' access path connects, plus surfaced in the Share tab. Describes the current
    ''' state and the concrete next step, worst-actionable-case first: dead forward
    ''' -&gt; CGNAT -&gt; IPv6-only -&gt; unconfirmed forward -&gt; confirmed -&gt; LAN-only.
    ''' Emitted in the PC UI language (the sharer's audience). "" when nothing to
    ''' say (no address at all). <paramref name="includeExternal"/> off = the
    ''' LAN-only export: the note must describe ONLY the LAN path, never the
    ''' internet reachability the config deliberately left out.</summary>
    Public Function AccessNote(rus As Boolean, reach As WorkerReachability, port As Integer,
                               Optional includeExternal As Boolean = True) As String
        If reach Is Nothing Then Return ""
        Dim lan As String = If(reach.LanAddress, "")
        Dim ext As String = If(reach.ExternalHost, "")
        Dim ipv6 As String = If(reach.Ipv6Address, "")
        If Not includeExternal Then
            ' LAN-only export: no internet/IPv6 address was embedded, so describe
            ' only the local path (never claim internet reachability the config omits).
            If lan.Length > 0 Then
                Return If(rus,
                    "Работает в той же сети Wi-Fi (интернет-адрес намеренно не включён).",
                    "Reachable on the same Wi-Fi as this PC (internet address intentionally left out).")
            End If
            Return ""
        End If
        If reach.ExternalPortChecked AndAlso reach.ExternalPortOpen Then
            Return If(rus,
                "Работает по Wi-Fi и из интернета - проброшенный порт ответил на внешнюю проверку.",
                "Reachable on your Wi-Fi and over the internet - the forwarded port passed an external test.")
        ElseIf reach.ExternalPortChecked AndAlso Not reach.ExternalPortOpen Then
            Return If(rus,
                "Работает только в той же сети Wi-Fi. Интернет-порт " & port.ToString() & " не ответил на внешнюю проверку - проверьте проброс порта на роутере или включите UPnP.",
                "Reachable only on the same Wi-Fi. Internet port " & port.ToString() & " did not answer an external test - re-check the router forward or enable UPnP.")
        ElseIf reach.IsCgnat Then
            Return If(rus,
                "Работает только в той же сети Wi-Fi. Провайдер использует CGNAT - проброс порта не сработает; используйте адрес IPv6 (если показан) или VPN.",
                "Reachable only on the same Wi-Fi. Your ISP uses CGNAT, so a forwarded port cannot work - use the IPv6 address if shown, or a VPN/relay.")
        ElseIf ipv6.Length > 0 AndAlso ext.Length = 0 Then
            Return If(rus,
                "Работает по Wi-Fi и по IPv6 из сетей, где он поддерживается. Обычный проброс порта на этом подключении недоступен.",
                "Reachable on your Wi-Fi, and over IPv6 from networks that support it. A plain port-forward is not available on this connection.")
        ElseIf ext.Length > 0 AndAlso reach.ExternalPort > 0 Then
            Return If(rus,
                "Работает по Wi-Fi и, если проброс/UPnP держится, из интернета - проверьте с телефона по мобильной сети.",
                "Reachable on your Wi-Fi and, if the router forward/UPnP holds, over the internet - confirm from the phone on mobile data.")
        ElseIf lan.Length > 0 Then
            Return If(rus,
                "Работает в той же сети Wi-Fi. Для доступа из других сетей пробросьте TCP-порт " & port.ToString() & " на " & lan & " в роутере или включите UPnP.",
                "Reachable on the same Wi-Fi as this PC. For other networks, forward TCP port " & port.ToString() & " to " & lan & " on your router, or enable UPnP.")
        End If
        Return ""
    End Function

    ' --- opt-in server-features enablement (SPECIFICATION_SHARE_SERVER_OPTIN_INSTALL) ---

    ''' <summary>Heading of the enable-sharing dialog / Settings-tab panel.</summary>
    Public Function ServerEnableTitle(rus As Boolean) As String
        Return If(rus, "Включить общий доступ к папкам", "Enable folder sharing")
    End Function

    ''' <summary>What/why body shown before the one UAC prompt. States the network
    ''' exposure honestly and that it needs administrator rights once.</summary>
    Public Function ServerEnableBody(rus As Boolean) As String
        Return If(rus,
            "Чтобы делиться папками с телефоном Android, нужен небольшой фоновый SFTP-сервер и разрешение в брандмауэре Windows. Это один раз потребует прав администратора. После включения выбранные вами папки станут доступны для чтения по сети. Продолжить?",
            "To share folders with an Android phone, a small background SFTP server and a Windows Firewall exception are needed. This asks for administrator rights once. After it is on, the folders you pick become readable over the network. Continue?")
    End Function

    ''' <summary>Shorter intro for the Settings tab's enablement panel (the primary,
    ''' always-discoverable opt-in home).</summary>
    Public Function ServerEnablePanelIntro(rus As Boolean) As String
        Return If(rus,
            "Общий доступ к папкам для телефона Android пока не включён. Он добавляет небольшой фоновый SFTP-сервер и одно разрешение в брандмауэре Windows (нужны права администратора один раз). Пока он выключен, программа работает как просмотрщик и сортировщик медиафайлов.",
            "Folder sharing for an Android phone is not enabled yet. It adds a small background SFTP server and one Windows Firewall exception (administrator rights are needed once). While it is off, the app works as a plain image/video viewer and sorter.")
    End Function

    ''' <summary>The primary action button label (both dialog and panel).</summary>
    Public Function ServerEnableButton(rus As Boolean) As String
        Return If(rus, "Установить функции сервера..", "Install server features..")
    End Function

    Public Function ServerEnableCancel(rus As Boolean) As String
        Return If(rus, "Отмена", "Cancel")
    End Function

    Public Function ServerEnableWorking(rus As Boolean) As String
        Return If(rus, "Настройка.. подтвердите запрос прав администратора.", "Setting up.. confirm the administrator prompt.")
    End Function

    Public Function ServerEnableSuccess(rus As Boolean) As String
        Return If(rus, "Готово. Общий доступ включён.", "Done. Folder sharing is enabled.")
    End Function

    Public Function ServerEnableDeclined(rus As Boolean) As String
        Return If(rus, "Общий доступ не включён - не получены права администратора.", "Sharing not enabled - administrator rights were not granted.")
    End Function

    Public Function ServerEnableFailed(rus As Boolean) As String
        Return If(rus, "Не удалось настроить брандмауэр. Попробуйте ещё раз.", "Could not configure the firewall. Please try again.")
    End Function

    ''' <summary>Worker payload missing - nothing to enable (a fresh clone / a build
    ''' without the sidecar). Reinstall to get the Share feature.</summary>
    Public Function ServerEnableUnavailable(rus As Boolean) As String
        Return If(rus,
            "Компонент сервера не найден рядом с программой - переустановите приложение, чтобы включить общий доступ.",
            "The server component was not found next to the app - reinstall the application to enable sharing.")
    End Function

    ''' <summary>Tooltip / hint on the still-visible Share button while the feature
    ''' is disabled (clicking it opens the enable dialog rather than the wizard).</summary>
    Public Function ServerDisabledButtonHint(rus As Boolean) As String
        Return If(rus,
            "Общий доступ к папкам для телефона Android. Нажмите, чтобы включить (нужны права администратора один раз).",
            "Folder sharing for an Android phone. Click to enable it (administrator rights are needed once).")
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
