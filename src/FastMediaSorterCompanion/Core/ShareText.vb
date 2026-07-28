Option Strict On

''' <summary>
''' Shared RU/EN copy for the internet-access half of the Share feature, used by
''' both the Settings Share tab and the quick-share wizard. House prose style:
''' plain hyphen (never em-dash), the letter "ё" where correct, ".." not "...".
''' </summary>
Public Module ShareText

    ''' <summary>Warning shown when the user opts into internet access - one SFTP
    ''' server, one credential, all folders exposed the moment a port is open.</summary>
    Public Function SecurityText() As String
        Return Localization.T("Внимание: вы открываете SFTP-сервер в интернет. Доступ ко всем общим папкам получит любой, кто узнает адрес, логин и пароль. Не публикуйте QR-код и файл .fmscfg. Выключайте доступ, когда закончили.")
    End Function

    ''' <summary>Default hint under the primary QR when it is LAN-only (either the
    ''' user ticked "LAN only" or no usable internet path exists). Carries the
    ''' bearer-token caution (decision A, 2026-07-15): the QR/file IS the key, so a
    ''' light "don't show it to others" rides even on the reassuring LAN copy.</summary>
    Public Function LanHintText() As String
        Return Localization.T("Работает на телефоне в той же сети Wi-Fi. Ничего настраивать не нужно. Любой, у кого есть этот код или файл, получит доступ к папкам - не показывайте его посторонним.")
    End Function

    ''' <summary>Factual note (decision F, 2026-07-15): while a share runs, other
    ''' devices on the CURRENT network - including a public Wi-Fi - can reach this PC
    ''' on the share port, protected by the password. Deliberately calm, not a scare:
    ''' same-network access (home, office Wi-Fi) is an intended capability. Surfaced
    ''' near the share toggle and mirrored in the docs.</summary>
    Public Function NetworkReachNote() As String
        Return Localization.T("Пока раздача включена, устройства в вашей текущей сети (в том числе в публичном Wi-Fi) могут подключиться к этому ПК по паролю. Выключайте раздачу, когда она не нужна.")
    End Function

    ''' <summary>Hint under the primary QR when it carries BOTH addresses
    ''' (LAN + internet) - the S1006 default. The phone picks whichever is
    ''' reachable, so one scan works home and away; brief security nudge +
    ''' pointer to the internet-setup tab.</summary>
    Public Function CombinedHintText() As String
        Return Localization.T("Один код на дом и на улицу - телефон сам выберет доступный адрес. В коде есть и интернет-адрес: не публикуйте его, выключайте доступ, когда закончили. Настройка и проверка - на вкладке «Доступ из интернета».")
    End Function

    ''' <summary>Label of the §6 "exclude password" export safeguard.</summary>
    Public Function NoPasswordText() As String
        Return Localization.T("Не включать пароль в файл/QR")
    End Function

    ''' <summary>Hint shown while the password is excluded: the recipient will have
    ''' to type it, so the sender needs to see and pass it on out-of-band.</summary>
    Public Function NoPasswordHint(password As String) As String
        Dim pw As String = If(String.IsNullOrEmpty(password), "-", password)
        Return Localization.TF("Пароль не попадёт в файл/QR. Сообщите его получателю отдельно: {0}", pw)
    End Function

    ''' <summary>Contract §7: the config no longer fits a QR code (too many
    ''' per-folder settings) - never truncate, point the user at the file.</summary>
    Public Function QrOverflowText() As String
        Return Localization.T("Слишком много настроек для QR-кода - сохраните файл .fmscfg и отправьте его на телефон.")
    End Function

    ''' <summary>Shown on the Internet tab/wizard when a portforward path was found
    ''' but this PC's own LAN address could not be - the QR still works, just not
    ''' the fast local hop, and detection only re-runs on a fresh start/stop.</summary>
    Public Function LanUnknownText() As String
        Return Localization.T("Не удалось определить адрес этого ПК в локальной сети (проверьте, не включён ли VPN). Если телефон рядом, в той же сети, остановите и снова начните общий доступ, чтобы попробовать ещё раз - сейчас в QR-коде есть только адрес из интернета.")
    End Function

    ''' <summary>Shown instead of the plain UPnP-success text when the worker
    ''' could not independently cross-check that the connection is not behind a
    ''' hidden ISP-side NAT (the IP-echo query failed) - stronger than the
    ''' always-shown UPnP caveat, since here we genuinely do not know either way.</summary>
    Public Function ExternalUnverifiedText() As String
        Return Localization.T("Порт открыт автоматически (UPnP), но проверить, что провайдер не использует скрытый NAT, не удалось (не ответил внешний сервис проверки IP). Адрес может не работать извне - обязательно проверьте с телефона по мобильной сети.")
    End Function

    ''' <summary>Honest CGNAT explanation - forwarding cannot help.</summary>
    Public Function CgnatText() As String
        Return Localization.T("Ваш провайдер использует CGNAT (общий внешний адрес). Проброс портов не поможет - извне к этому ПК подключиться нельзя. По локальной сети всё работает как обычно.")
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
    Public Function AccessNote(reach As WorkerReachability, port As Integer,
                               Optional includeExternal As Boolean = True) As String
        If reach Is Nothing Then Return ""
        Dim lan As String = If(reach.LanAddress, "")
        Dim ext As String = If(reach.ExternalHost, "")
        Dim ipv6 As String = If(reach.Ipv6Address, "")
        If Not includeExternal Then
            ' LAN-only export: no internet/IPv6 address was embedded, so describe
            ' only the local path (never claim internet reachability the config omits).
            If lan.Length > 0 Then
                Return Localization.T("Работает в той же сети Wi-Fi (интернет-адрес намеренно не включён).")
            End If
            Return ""
        End If
        If reach.ExternalPortChecked AndAlso reach.ExternalPortOpen Then
            Return Localization.T("Работает по Wi-Fi и из интернета - проброшенный порт ответил на внешнюю проверку.")
        ElseIf reach.ExternalPortChecked AndAlso Not reach.ExternalPortOpen Then
            Return Localization.TF("Работает только в той же сети Wi-Fi. Интернет-порт {0} не ответил на внешнюю проверку - проверьте проброс порта на роутере или включите UPnP.", port)
        ElseIf reach.IsCgnat Then
            Return Localization.T("Работает только в той же сети Wi-Fi. Провайдер использует CGNAT - проброс порта не сработает; используйте адрес IPv6 (если показан) или VPN.")
        ElseIf ipv6.Length > 0 AndAlso ext.Length = 0 Then
            Return Localization.T("Работает по Wi-Fi и по IPv6 из сетей, где он поддерживается. Обычный проброс порта на этом подключении недоступен.")
        ElseIf ext.Length > 0 AndAlso reach.ExternalPort > 0 Then
            Return Localization.T("Работает по Wi-Fi и, если проброс/UPnP держится, из интернета - проверьте с телефона по мобильной сети.")
        ElseIf lan.Length > 0 Then
            Return Localization.TF("Работает в той же сети Wi-Fi. Для доступа из других сетей пробросьте TCP-порт {0} на {1} в роутере или включите UPnP.", port, lan)
        End If
        Return ""
    End Function

    ' --- opt-in server-features enablement (SPECIFICATION_SHARE_SERVER_OPTIN_INSTALL) ---

    ''' <summary>Heading of the enable-sharing dialog / Settings-tab panel.</summary>
    Public Function ServerEnableTitle() As String
        Return Localization.T("Включить общий доступ к папкам")
    End Function

    ''' <summary>What/why body shown before the one UAC prompt. States the network
    ''' exposure honestly and that it needs administrator rights once.</summary>
    Public Function ServerEnableBody() As String
        Return Localization.T("Чтобы делиться папками с телефоном Android, нужен небольшой фоновый SFTP-сервер и разрешение в брандмауэре Windows. Это один раз потребует прав администратора. После включения выбранные вами папки станут доступны для чтения по сети. Продолжить?")
    End Function

    ''' <summary>Shorter intro for the Settings tab's enablement panel (the primary,
    ''' always-discoverable opt-in home).</summary>
    Public Function ServerEnablePanelIntro() As String
        Return Localization.T("Общий доступ к папкам для телефона Android пока не включён. Он добавляет небольшой фоновый SFTP-сервер и одно разрешение в брандмауэре Windows (нужны права администратора один раз). Пока он выключен, программа работает как просмотрщик и сортировщик медиафайлов.")
    End Function

    ''' <summary>The primary action button label (both dialog and panel).</summary>
    Public Function ServerEnableButton() As String
        Return Localization.T("Установить функции сервера..")
    End Function

    Public Function ServerEnableCancel() As String
        Return Localization.T("Отмена")
    End Function

    Public Function ServerEnableWorking() As String
        Return Localization.T("Настройка.. подтвердите запрос прав администратора.")
    End Function

    Public Function ServerEnableSuccess() As String
        Return Localization.T("Готово. Общий доступ включён.")
    End Function

    Public Function ServerEnableDeclined() As String
        Return Localization.T("Общий доступ не включён - не получены права администратора.")
    End Function

    Public Function ServerEnableFailed() As String
        Return Localization.T("Не удалось настроить брандмауэр. Попробуйте ещё раз.")
    End Function

    ''' <summary>Worker payload missing - nothing to enable (a fresh clone / a build
    ''' without the sidecar). Reinstall to get the Share feature.</summary>
    Public Function ServerEnableUnavailable() As String
        Return Localization.T("Компонент сервера не найден рядом с программой - переустановите приложение, чтобы включить общий доступ.")
    End Function

    ''' <summary>Tooltip / hint on the still-visible Share button while the feature
    ''' is disabled (clicking it opens the enable dialog rather than the wizard).</summary>
    Public Function ServerDisabledButtonHint() As String
        Return Localization.T("Общий доступ к папкам для телефона Android. Нажмите, чтобы включить (нужны права администратора один раз).")
    End Function

    ''' <summary>Step-by-step router port-forward instructions with the concrete
    ''' values filled in. The external address is already embedded in the QR /
    ''' .fmscfg (ShareConfigBuilder), so the closing line just tells the user to
    ''' rescan/re-save after the forward takes effect.</summary>
    Public Function PortForwardText(router As String, extPort As Integer, lanIp As String, port As Integer) As String
        Return Localization.TF(
            "Внешний адрес уже добавлен в QR-код и файл .fmscfg. Чтобы он заработал, пробросьте порт на роутере:" & vbCrLf &
            "1. Откройте роутер: {0} (кнопка «Открыть роутер»)." & vbCrLf &
            "2. Войдите (логин и пароль обычно на наклейке снизу роутера)." & vbCrLf &
            "3. Найдите раздел «Проброс портов» (Port Forwarding / Virtual Server)." & vbCrLf &
            "4. Добавьте правило: внешний порт {1} -> {2}:{3}, протокол TCP." & vbCrLf &
            "5. Сохраните правило - и заново отсканируйте QR-код (или сохраните .fmscfg) на телефоне.",
            router, extPort, lanIp, port)
    End Function

End Module
