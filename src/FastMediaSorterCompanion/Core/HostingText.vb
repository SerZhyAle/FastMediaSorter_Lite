Option Strict On

''' <summary>
''' Copy for the Hosting surface - the User/Server edition distinction
''' (SPECIFICATION_SHARE_SYSTEM_SERVICE.md §2, §5). House prose style: plain
''' hyphen (never em-dash), the letter "ё" where correct, ".." not "...".
'''
''' The wording carries one rule the UI must never blur: "service installed",
''' "service running", "SFTP serving" and "no folders configured" are four
''' different states, and a user debugging a phone that cannot connect needs to
''' know which one they are in.
''' </summary>
Public Module HostingText

    ''' <summary>Where the Server edition is obtained. The Share Manager only ever
    ''' OPENS this page - it never downloads or runs an installer itself (spec §1.4).
    ''' The path segment is the repository name, and GitHub Pages is case-sensitive:
    ''' "FastMediaSorter_Lite", not "_LITE" (which 404s and sent the button nowhere).</summary>
    Public Const ServerEditionUrl As String = "https://serzhyale.github.io/FastMediaSorter_Lite/server.html"

    ''' <summary>The one-line hosting state shown in the main window.</summary>
    Public Function HostModeLine(mode As ServerFeatures.ServerHostMode) As String
        Select Case mode
            Case ServerFeatures.ServerHostMode.SystemService
                Return Localization.T("Хостинг: служба Windows")
            Case ServerFeatures.ServerHostMode.UserSession
                Return Localization.T("Хостинг: раздаёт эта программа")
            Case Else
                Return ""
        End Select
    End Function

    Public Function ManageButton() As String
        Return Localization.T("Управление хостингом..")
    End Function

    Public Function Title() As String
        Return Localization.T("Хостинг общего доступа")
    End Function

    ''' <summary>What the current host means for availability - the answer to "will
    ''' my phone reach this PC tomorrow morning?".</summary>
    Public Function Intro(mode As ServerFeatures.ServerHostMode) As String
        If mode = ServerFeatures.ServerHostMode.SystemService Then
            Return Localization.T("Папки раздаёт служба Windows. Она стартует вместе с системой и работает без входа пользователя. Это окно - только пульт управления: его можно закрыть, раздача продолжится.")
        End If
        Return Localization.T("Папки раздаёт эта программа. Раздача работает, пока вы вошли в систему и менеджер запущен. Серверная редакция ставит службу Windows, которая раздаёт папки с загрузки - даже когда в систему никто не вошёл.")
    End Function

    ''' <summary>The live SCM verdict, spelled out. "Unknown" is deliberately its own
    ''' line: it is not the same as "not installed", and treating it as such would
    ''' offer an install over a service that already exists.</summary>
    Public Function ServiceStateLine(state As ServiceControl.ServiceState) As String
        Select Case state
            Case ServiceControl.ServiceState.Running
                Return Localization.T("Служба работает")
            Case ServiceControl.ServiceState.Stopped
                Return Localization.T("Служба установлена, но остановлена")
            Case ServiceControl.ServiceState.Starting
                Return Localization.T("Служба запускается..")
            Case ServiceControl.ServiceState.Stopping
                Return Localization.T("Служба останавливается..")
            Case ServiceControl.ServiceState.NotInstalled
                Return Localization.T("Служба не установлена")
            Case Else
                Return Localization.T("Состояние службы определить не удалось")
        End Select
    End Function

    ''' <summary>Whether the SFTP server itself is serving - a separate fact from the
    ''' service being up, and the one that decides whether a phone connects.</summary>
    Public Function ServingLine(running As Boolean, rootCount As Integer) As String
        If Not running Then Return Localization.T("Раздача SFTP выключена")
        If rootCount <= 0 Then Return Localization.T("Служба работает, но ни одна папка не выбрана - раздавать нечего")
        Return Localization.T("Раздача SFTP работает")
    End Function

    ''' <summary>
    ''' Who is answering the control pipe RIGHT NOW - the question the rest of the
    ''' console cannot answer on its own. "The service is installed" and "the service
    ''' is running" still do not say whether anything is serving, and on a PC that has
    ''' both editions' history the honest answer matters: exactly one host is live and
    ''' the user needs to know which.
    ''' </summary>
    Public Function LiveHostLine(serviceServing As Boolean, workerAnswering As Boolean) As String
        If Not workerAnswering Then
            Return Localization.T("Сейчас на канал управления никто не отвечает")
        End If
        If serviceServing Then
            Return Localization.T("Сейчас отвечает: служба Windows")
        End If
        Return Localization.T("Сейчас отвечает: фоновый процесс этой программы")
    End Function

    ''' <summary>The one state store both hosts use. Shown so "one folder list for both
    ''' editions" is a visible fact rather than a promise in the documentation.</summary>
    Public Function StateStoreLine(path As String) As String
        Return Localization.TF("Общее хранилище настроек и ключа: {0}", path)
    End Function

    Public Function RestartServiceButton() As String
        Return Localization.T("Перезапустить службу")
    End Function

    Public Function GrantRootsButton() As String
        Return Localization.T("Выдать службе доступ к общим папкам")
    End Function

    ''' <summary>Asked at the moment a folder is chosen, not later in a console: the
    ''' service account needs access the picker's own rights do not give it.</summary>
    Public Function GrantNeededPrompt(folderList As String) As String
        Return Localization.TF("Папки раздаёт служба Windows, и работает она под своей учётной записью - к этим папкам у неё пока нет доступа:" & vbCrLf & vbCrLf & "{0}" & vbCrLf & vbCrLf & "Выдать его сейчас? Windows спросит подтверждение.", folderList)
    End Function

    Public Function GrantDeclinedHint() As String
        Return Localization.T("Без доступа служба не откроет эти папки на телефоне. Выдать его можно позже - «Управление хостингом..».")
    End Function

    Public Function GrantWorkingHint() As String
        Return Localization.T("Выдаю службе доступ к папкам..")
    End Function

    ''' <summary>The state line when the listener is up but the account behind it cannot
    ''' read the folders the list promises. Green would be a lie: from the phone this
    ''' share is broken on every folder, and without this line the only symptom anywhere
    ''' is a directory that refuses to open - on the other side of the network.</summary>
    Public Function RunningNoAccessLine(blocked As Integer) As String
        Return Localization.TF("Раздача работает, но у службы нет доступа к папкам: {0}", blocked)
    End Function

    ''' <summary>Hover text on a folder row the service account cannot read.</summary>
    Public Function FolderNoAccessTip() As String
        Return Localization.T("У службы нет доступа к этой папке - на телефоне она не откроется.")
    End Function

    ''' <summary>
    ''' The share root is readable but folders INSIDE it are not - the shape of the
    ''' 2026-09-01 incident, where a phone got an empty listing for a folder of 758
    ''' files and every check on the PC said the share was fine, because every check
    ''' looked at the root alone.
    ''' </summary>
    Public Function SubtreeBlockedWarning(blocked As Integer) As String
        Return Localization.TF("Внутри выбранных папок есть подпапки без доступа: {0}. На телефоне они не откроются.", blocked.ToString())
    End Function

    ''' <summary>Nothing in the selection is readable, so the code would hand the phone
    ''' an address that opens an empty tree. Showing it anyway is the failure mode this
    ''' whole check exists to remove.</summary>
    Public Function SubtreeNothingReadable() As String
        Return Localization.T("Ни одна из выбранных папок не доступна службе. QR-код не показан: открывать по нему нечего.")
    End Function

    Public Function FixPermissionsButton() As String
        Return Localization.T("Исправить права")
    End Function

    ''' <summary>Asks before the one UAC prompt, naming the folders it is about to
    ''' change - a machine-affecting action must never be a surprise.</summary>
    Public Function SubtreeGrantPrompt(folderList As String) As String
        Return Localization.TF("К этим подпапкам у службы нет доступа - на телефоне они не откроются:" & vbCrLf & vbCrLf & "{0}" & vbCrLf & vbCrLf & "Выдать доступ сейчас? Windows спросит подтверждение.", folderList)
    End Function

    ''' <summary>A scan that ran out its budget covered only part of the tree. Saying
    ''' "nothing found" there would be a clean bill of health nobody checked for.</summary>
    Public Function SubtreeScanTruncated(scanned As Integer) As String
        Return Localization.TF("Проверено папок: {0} - проверка не завершена, могут быть и другие.", scanned.ToString())
    End Function

    ''' <summary>Tail of a folder list too long to show in full.</summary>
    Public Function AndMore(rest As Integer) As String
        Return Localization.TF("..и ещё {0}", rest.ToString())
    End Function

    Public Function CheckingAccessHint() As String
        Return Localization.T("Проверяю доступ к папкам..")
    End Function

    ''' <summary>The switch an ordinary installation can now make on its own: this is
    ''' NOT a second product, it is the same worker under a different host (spec §1 -
    ''' a packaging and host-mode distinction, never a fork).</summary>
    Public Function SwitchToServiceButton() As String
        Return Localization.T("Перевести раздачу в режим службы Windows..")
    End Function

    ''' <summary>Everything the one UAC prompt is about to do, before it appears. A
    ''' machine-wide role must never be taken on by a button whose consequences were
    ''' not spelled out first.</summary>
    Public Function SwitchToServicePrompt() As String
        Return Localization.T("Раздачу папок возьмёт на себя служба Windows: она стартует вместе с системой и работает, даже когда в систему никто не вошёл." & vbCrLf & vbCrLf &
                              "Windows запросит подтверждение администратора один раз. Будут: перенесены ваши настройки, папки и ключ узла в общее машинное хранилище, зарегистрирована служба, добавлено правило брандмауэра и выдан доступ к выбранным папкам." & vbCrLf & vbCrLf &
                              "Ключ узла сохраняется, поэтому уже подключённые телефоны подключать заново не придётся. Вернуться к обычному режиму можно здесь же, кнопкой «Вернуться к пользовательской редакции..»." & vbCrLf & vbCrLf &
                              "Перевести раздачу в режим службы?")
    End Function

    ''' <summary>Why the switch is not offered in the Store build - with the one thing
    ''' the user can actually do about it.</summary>
    Public Function SwitchUnavailablePackagedHint() As String
        Return Localization.T("Версия из Microsoft Store не может устанавливать службы Windows. Круглосуточная раздача есть в версии с сайта - её можно поставить рядом.")
    End Function

    Public Function InstallServerButton() As String
        Return Localization.T("Установить серверную редакцию..")
    End Function

    Public Function ReturnToUserButton() As String
        Return Localization.T("Вернуться к пользовательской редакции..")
    End Function

    Public Function StartServiceButton() As String
        Return Localization.T("Запустить службу")
    End Function

    Public Function StopServiceButton() As String
        Return Localization.T("Остановить службу")
    End Function

    Public Function RepairServiceButton() As String
        Return Localization.T("Восстановить регистрацию службы")
    End Function

    Public Function RemoveRoleButton() As String
        Return Localization.T("Удалить роль сервера")
    End Function

    ''' <summary>The download note. States the two promises that matter (the identity
    ''' survives; nothing is downloaded behind the user's back).</summary>
    Public Function DownloadNote() As String
        Return Localization.T("Серверная редакция скачивается отдельно - с сайта или через winget. Её установщик перенесёт ключ узла, пароль, список папок и порт, так что привязывать телефоны заново не придётся. Эта программа никогда не скачивает и не запускает установщик сама.")
    End Function

    ''' <summary>The LocalService consequences, surfaced BEFORE a root is added rather
    ''' than after a phone fails to open it.</summary>
    Public Function AccountNote() As String
        Return Localization.T("Служба работает от имени LOCAL SERVICE, а не от вашего. Каждой раздаваемой папке нужен доступ для этой учётной записи: чтение, а если папка раздаётся с записью - то и запись. Кнопка выдаёт их по текущему списку папок. Сетевые пути вида \\сервер\папка так работать не будут.")
    End Function

    ''' <summary>Shown next to "Remove server role": what is and is not destroyed.</summary>
    Public Function RemoveNote() As String
        Return Localization.T("Удаление роли сервера остановит и удалит службу. Ключ узла, пароль и список папок останутся на месте - после возврата в пользовательский режим телефоны подключатся как раньше.")
    End Function

    Public Function ManageUnavailable() As String
        Return Localization.T("Управление службой недоступно: это установка пользовательской редакции.")
    End Function

    Public Function ManageWorking() As String
        Return Localization.T("Выполняется.. подтвердите запрос прав администратора.")
    End Function

    Public Function ManageResultLine(result As ServiceControl.ManageResult) As String
        Select Case result
            Case ServiceControl.ManageResult.Succeeded
                Return Localization.T("Готово.")
            Case ServiceControl.ManageResult.Declined
                Return Localization.T("Не выполнено: не получены права администратора.")
            Case ServiceControl.ManageResult.Unavailable
                Return ManageUnavailable()
            Case Else
                Return Localization.T("Не удалось выполнить действие. Подробности - в журнале службы.")
        End Select
    End Function

End Module
