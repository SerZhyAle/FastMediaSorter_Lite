#If Not NETFRAMEWORK Then
Option Strict On

Imports System.Collections.Generic

''' <summary>One product-owned vocabulary map.  Controls consume this rather than
''' carrying a second, drifting name/glyph table in each layout file.</summary>
Public Module IconMap

    Public Enum IconRole
        Content
        State
    End Enum

    ''' <summary>Which program shows the control - each has its own string table, so a
    ''' name key is registered in the viewer's tables or in the Share Manager's.</summary>
    Public Enum IconSurface
        Viewer
        ShareManager
    End Enum

    ''' <summary>The catalog proposal the meanings without a usable vocabulary picture wait
    ''' for (spec 027 section 3.1). Accepted by the catalog owner on 2026-10-07 (ICON-SET
    ''' 0.28): <c>action.convert-to-video</c> (taken by btn_ToVideo) and <c>app.language</c> arrived
    ''' with glyphs; what remains here waits for a drawing the catalog still owes - the
    ''' <c>random</c> variant of <c>media.slideshow</c> and the <c>filled</c> variants of
    ''' <c>tool.rectangle</c> / <c>tool.oval</c> - or for the Share Manager to adopt
    ''' <c>action.reveal</c> (whose off form's file is also owed). Such a control keeps its
    ''' pre-contract picture under a dated exception.</summary>
    Public Const DesktopMeaningsProposal As String = "PROPOSAL-2026-09-24-fms-windows-meanings.md"

    Public NotInheritable Class IconMeaning
        Public Sub New(controlName As String, glyphId As String, localizationKey As String,
                       role As IconRole, Optional proposal As String = "", Optional artwork As Boolean = False,
                       Optional surface As IconSurface = IconSurface.Viewer)
            Me.ControlName = controlName
            Me.GlyphId = glyphId
            Me.LocalizationKey = localizationKey
            Me.Role = role
            Me.Proposal = proposal
            Me.IsArtwork = artwork
            Me.Surface = surface
        End Sub

        Public ReadOnly Property ControlName As String
        Public ReadOnly Property GlyphId As String
        Public ReadOnly Property LocalizationKey As String
        Public ReadOnly Property Role As IconRole
        Public ReadOnly Property Proposal As String
        Public ReadOnly Property IsArtwork As Boolean
        Public ReadOnly Property Surface As IconSurface

        ''' <summary>True when the meaning is in the vocabulary today; False while it
        ''' waits for <see cref="Proposal"/> or is declared artwork.</summary>
        Public ReadOnly Property IsVocabulary As Boolean
            Get
                Return Proposal.Length = 0 AndAlso Not IsArtwork
            End Get
        End Property
    End Class

    Private Const SM As IconSurface = IconSurface.ShareManager

    Private ReadOnly meanings As New Dictionary(Of String, IconMeaning)(StringComparer.Ordinal) From {
        {"btn_choose_file", New IconMeaning("btn_choose_file", "action.choose-file", "Выбрать файл..", IconRole.Content)},
        {"btn_Select_Folder", New IconMeaning("btn_Select_Folder", "action.choose-folder", "Выбрать папку..", IconRole.Content)},
        {"btn_Review", New IconMeaning("btn_Review", "action.refresh", "Обновить папку", IconRole.Content)},
        {"btn_Panel", New IconMeaning("btn_Panel", "view.grid", "Панель изображений", IconRole.Content)},
        {"btn_Full_Screen", New IconMeaning("btn_Full_Screen", "media.fullscreen", "Во весь экран", IconRole.Content)},
        {"btn_Slideshow", New IconMeaning("btn_Slideshow", "media.slideshow", "Слайд-шоу", IconRole.Content)},
        {"btn_Random_Slideshow", New IconMeaning("btn_Random_Slideshow", "", "Случайное слайд-шоу", IconRole.Content, DesktopMeaningsProposal)},
        {"btn_Next_Random", New IconMeaning("btn_Next_Random", "media.random", "Случайный файл", IconRole.Content)},
        {"btn_Rename", New IconMeaning("btn_Rename", "action.rename", "Переименовать", IconRole.Content)},
        {"btn_RecentFiles", New IconMeaning("btn_RecentFiles", "source.recent", "Недавние файлы", IconRole.Content)},
        {"btn_Prev_File", New IconMeaning("btn_Prev_File", "media.previous", "Предыдущий файл", IconRole.Content)},
        {"btn_Next_File", New IconMeaning("btn_Next_File", "media.next", "Следующий файл", IconRole.Content)},
        {"bt_Delete", New IconMeaning("bt_Delete", "action.delete", "Удалить", IconRole.Content)},
        {"btn_Overflow", New IconMeaning("btn_Overflow", "nav.more", "Ещё", IconRole.Content)},
        {"btn_Overflow_2", New IconMeaning("btn_Overflow_2", "nav.more", "Ещё", IconRole.Content)},
        {"btn_Image_Menu", New IconMeaning("btn_Image_Menu", "nav.more", "Ещё", IconRole.Content)},
        {"btn_Translate", New IconMeaning("btn_Translate", "action.translate", "Перевод", IconRole.Content)},
        {"btn_Edit", New IconMeaning("btn_Edit", "action.edit", "Редактировать", IconRole.Content)},
        {"btn_ToVideo", New IconMeaning("btn_ToVideo", "action.convert-to-video", "Преобразовать в видео", IconRole.Content)},
        {"btn_Video_Play", New IconMeaning("btn_Video_Play", "media.play", "Воспроизвести", IconRole.Content)},
        {"btn_Video_Mute", New IconMeaning("btn_Video_Mute", "media.volume", "Громкость", IconRole.Content)},
        {"editor_Tool_Crop", New IconMeaning("editor_Tool_Crop", "action.crop", "Обрезка", IconRole.Content)},
        {"editor_Tool_Brush", New IconMeaning("editor_Tool_Brush", "tool.brush", "Кисть", IconRole.Content)},
        {"editor_Tool_Rectangle", New IconMeaning("editor_Tool_Rectangle", "tool.rectangle", "Прямоугольник", IconRole.Content)},
        {"editor_Tool_Oval", New IconMeaning("editor_Tool_Oval", "tool.oval", "Овал", IconRole.Content)},
        {"editor_Tool_Rectangle_Filled", New IconMeaning("editor_Tool_Rectangle_Filled", "", "Залитый прямоугольник", IconRole.Content, DesktopMeaningsProposal)},
        {"editor_Tool_Oval_Filled", New IconMeaning("editor_Tool_Oval_Filled", "", "Залитый овал", IconRole.Content, DesktopMeaningsProposal)},
        {"thumbnail_Image_Missing", New IconMeaning("thumbnail_Image_Missing", "content.image", "Изображение", IconRole.Content)},
        {"thumbnail_Video_Missing", New IconMeaning("thumbnail_Video_Missing", "content.video", "Видео", IconRole.Content)},
        {"settingsNavGeneral", New IconMeaning("settingsNavGeneral", "app.settings", "Настройки менеджера..", IconRole.Content)},
        {"settingsNavRecipients", New IconMeaning("settingsNavRecipients", "action.send-to", "Получатели", IconRole.Content)},
        {"settingsNavViewing", New IconMeaning("settingsNavViewing", "view.screen", "Просмотр", IconRole.Content)},
        {"settingsNavVideo", New IconMeaning("settingsNavVideo", "content.video", "Видео", IconRole.Content)},
        {"settingsNavAudio", New IconMeaning("settingsNavAudio", "content.audio", "Аудио", IconRole.Content)},
        {"settingsNavFiles", New IconMeaning("settingsNavFiles", "content.folder", "Файлы", IconRole.Content)},
        {"settingsNavResources", New IconMeaning("settingsNavResources", "source.resource", "Ресурс", IconRole.Content)},
        {"btn_Resource", New IconMeaning("btn_Resource", "source.resource", "Ресурс", IconRole.Content)},
        {"res_DevicesAdd", New IconMeaning("res_DevicesAdd", "source.exchange-server", "Сервер обмена", IconRole.Content)},
        {"xc_Connect", New IconMeaning("xc_Connect", "action.connect-device", "Подключить устройство", IconRole.Content)},
        {"settingsNavOcr", New IconMeaning("settingsNavOcr", "action.extract-text", "OCR", IconRole.Content)},
        {"settingsNavTranslation", New IconMeaning("settingsNavTranslation", "action.translate", "Перевод", IconRole.Content)},
        {"settingsNavSftp", New IconMeaning("settingsNavSftp", "device.phone", "Android / SFTP", IconRole.Content)},
        {"settingsNavAbout", New IconMeaning("settingsNavAbout", "app.info", "О программе", IconRole.Content)},
        {"settingsNavStorage", New IconMeaning("settingsNavStorage", "content.disk-container", "Диски .fdd..", IconRole.Content)},
        {"settingsSearch", New IconMeaning("settingsSearch", "action.search", "Поиск настроек", IconRole.Content)},
        {"managerSettingsNav_startup", New IconMeaning("managerSettingsNav_startup", "app.settings", "Настройки менеджера..", IconRole.Content, surface:=SM)},
        {"managerSettingsNav_network", New IconMeaning("managerSettingsNav_network", "system.wifi", "Сеть", IconRole.Content, surface:=SM)},
        {"managerSettingsNav_hosting", New IconMeaning("managerSettingsNav_hosting", "system.internet", "Сеть", IconRole.Content, surface:=SM)},
        {"managerSettingsNav_exchange", New IconMeaning("managerSettingsNav_exchange", "source.exchange-server", "Сервер обмена", IconRole.Content, surface:=SM)},
        {"managerSettingsNav_exchange-account", New IconMeaning("managerSettingsNav_exchange-account", "action.connect-device", "Подключить устройство", IconRole.Content, surface:=SM)},
        {"managerSettingsNav_scheduled", New IconMeaning("managerSettingsNav_scheduled", "action.run", "Файловые операции по расписанию", IconRole.Content, surface:=SM)},
        {"managerSettingsNav_help", New IconMeaning("managerSettingsNav_help", "app.info", "О программе", IconRole.Content, surface:=SM)},
        {"managerSettingsSearch", New IconMeaning("managerSettingsSearch", "action.search", "Поиск настроек", IconRole.Content, surface:=SM)},
        {"btnClipboardSend", New IconMeaning("btnClipboardSend", "action.send-clipboard", "Отправить буфер на сервер", IconRole.Content, surface:=SM)},
        {"btnClipboardGet", New IconMeaning("btnClipboardGet", "action.receive-clipboard", "Получить буфер с сервера", IconRole.Content, surface:=SM)},
        {"btnClipboardMenu", New IconMeaning("btnClipboardMenu", "action.send-clipboard", "Отправить буфер на сервер", IconRole.Content, surface:=SM)},
        {"clipboardTraySend", New IconMeaning("clipboardTraySend", "action.send-clipboard", "Отправить буфер на сервер", IconRole.Content, surface:=SM)},
        {"clipboardTrayGet", New IconMeaning("clipboardTrayGet", "action.receive-clipboard", "Получить буфер с сервера", IconRole.Content, surface:=SM)},
        {"_btnAccAdd", New IconMeaning("_btnAccAdd", "action.connect-device", "Подключить устройство", IconRole.Content, surface:=SM)},
        {"_btnXCreateInvitation", New IconMeaning("_btnXCreateInvitation", "content.invitation", "Приглашение", IconRole.Content, surface:=SM)},
        {"_rbGuest", New IconMeaning("_rbGuest", "content.guest", "Гость", IconRole.Content, surface:=SM)},
        {"btnShare", New IconMeaning("btnShare", "action.share", "Поделиться", IconRole.Content, surface:=SM)},
        {"btnAdd", New IconMeaning("btnAdd", "action.add", "Добавить папку..", IconRole.Content, surface:=SM)},
        {"btnDisks", New IconMeaning("btnDisks", "content.disk-container", "Диски .fdd..", IconRole.Content, surface:=SM)},
        {"btnSettings", New IconMeaning("btnSettings", "app.settings", "Настройки менеджера..", IconRole.Content, surface:=SM)},
        {"btnRevealPassword", New IconMeaning("btnRevealPassword", "", "Показать пароль", IconRole.Content, DesktopMeaningsProposal, surface:=SM)},
        {"copy", New IconMeaning("copy", "action.copy", "Копировать в буфер", IconRole.Content, surface:=SM)},
        {"shareStatusOk", New IconMeaning("shareStatusOk", "status.ok", "Раздача с этого ПК работает", IconRole.State, surface:=SM)},
        {"shareStatusError", New IconMeaning("shareStatusError", "status.error", "Требует внимания", IconRole.State, surface:=SM)},
        {"shareStatus", New IconMeaning("shareStatus", "status.unknown", "Раздача выключена", IconRole.State, surface:=SM)},
        {"folderReadOnly", New IconMeaning("folderReadOnly", "status.locked", "Только чтение", IconRole.State, surface:=SM)},
        {"collapsibleSection", New IconMeaning("collapsibleSection", "nav.expand", "Развернуть", IconRole.Content, surface:=SM)},
        {"collapsibleSectionOpen", New IconMeaning("collapsibleSectionOpen", "nav.collapse", "Свернуть", IconRole.Content, surface:=SM)}
    }

    ''' <summary>Viewer toolbar controls that carry a vocabulary id but deliberately draw a
    ''' text caption instead of the catalog picture: the Translate button shows the target
    ''' language, which is the information the user needs from it.</summary>
    Public ReadOnly TextCaptionToolbarControls As New HashSet(Of String)(StringComparer.Ordinal) From {"btn_Translate"}

    ' One glyph, one name (ICON-SET rule 1): source.resource is the resource meaning, so
    ' settingsNavResources and btn_Resource carry the same canonical key; app.settings and
    ' content.disk-container are likewise shared with the Share Manager's btnSettings and
    ' btnDisks. The visible words come from each surface's own strings (the nav's «Ресурсы»,
    ' the button's «Ресурс»), never from a second map here; the canonical keys of the two
    ' glyphs the viewer nav newly carries are registered in the viewer's table too
    ' (Localization.Nav.vb), so the names test sees them.

    ''' <summary>
    ''' True when a viewer control's picture is rendered from the catalog - and therefore
    ''' must be re-rendered whenever the chrome colour changes, or it keeps the colour it
    ''' was first drawn in. The toolbar's recolour pass asks this of every button it holds
    ''' instead of walking a fixed list, which is how btn_Edit stayed black on the black
    ''' scheme (audit B-05): the next glyph button cannot be forgotten.
    ''' </summary>
    Public Function IsToolbarCatalogGlyph(controlName As String) As Boolean
        Dim meaning As IconMeaning = GetMeaning(controlName)
        If meaning Is Nothing OrElse meaning.Surface <> IconSurface.Viewer Then Return False
        If String.IsNullOrEmpty(meaning.GlyphId) Then Return False
        Return Not TextCaptionToolbarControls.Contains(controlName)
    End Function

    Public Function TryGet(controlName As String, ByRef meaning As IconMeaning) As Boolean
        If String.IsNullOrEmpty(controlName) Then
            meaning = Nothing
            Return False
        End If
        Return meanings.TryGetValue(controlName, meaning)
    End Function

    Public Function GetMeaning(controlName As String) As IconMeaning
        Dim meaning As IconMeaning = Nothing
        Return If(TryGet(controlName, meaning), meaning, Nothing)
    End Function

    Public ReadOnly Property All As IEnumerable(Of IconMeaning)
        Get
            Return meanings.Values
        End Get
    End Property
End Module
#End If
