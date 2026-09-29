Option Strict On

' <summary>
' Strings of 060_SPECIFICATION_APP_THEME_SYSTEM_LIGHT_DARK.md - the one theme setting both
' programs offer. Linked into the Share Manager by <Compile Include>, and registered by both
' Localization.Registry.vb files, because the same three words appear in both settings windows.
'
' Columns after the Russian key: en, uk, de, it, es, fr, pt, ar, hi, bn, ur, zh.
' Никогда не ставьте «умные» кавычки в литерал - VB считает U+201C/U+201D разделителями
' строки (see the localization rules in CLAUDE.md); используются «…» и 「…」.
' </summary>
Partial Public NotInheritable Class Localization

    Private Shared Sub AddThemeStrings()
        Add("Оформление", "Appearance", "Оформлення", "Darstellung", "Aspetto", "Apariencia", "Apparence", "Aparência", "المظهر", "रूप-रंग", "চেহারা", "ظاہری شکل", "外观")
        Add("Тема оформления", "Theme", "Тема оформлення", "Design", "Tema", "Tema", "Thème", "Tema", "السمة", "थीम", "থিম", "تھیم", "主题")
        Add("Светлая, тёмная или как в Windows - для всех окон просмотрщика и менеджера общего доступа.",
            "Light, dark or as in Windows - for every window of the viewer and the Share Manager.",
            "Світла, темна або як у Windows - для всіх вікон переглядача й менеджера спільного доступу.",
            "Hell, dunkel oder wie in Windows - für alle Fenster des Viewers und des Freigabe-Managers.",
            "Chiaro, scuro o come in Windows - per tutte le finestre del visualizzatore e del gestore di condivisione.",
            "Claro, oscuro o como en Windows - para todas las ventanas del visor y del administrador de uso compartido.",
            "Clair, sombre ou comme Windows - pour toutes les fenêtres de la visionneuse et du gestionnaire de partage.",
            "Claro, escuro ou como no Windows - para todas as janelas do visualizador e do gerenciador de compartilhamento.",
            "فاتح أو داكن أو كما في Windows - لجميع نوافذ العارض ومدير المشاركة.",
            "हल्की, गहरी या Windows जैसी - व्यूअर और शेयर मैनेजर की सभी विंडो के लिए।",
            "হালকা, গাঢ় বা Windows-এর মতো - ভিউয়ার ও শেয়ার ম্যানেজারের সব উইন্ডোর জন্য।",
            "ہلکا، گہرا یا Windows کی طرح - ویور اور شیئر مینیجر کی تمام ونڈوز کے لیے۔",
            "浅色、深色或跟随 Windows - 适用于查看器和共享管理器的所有窗口。")
        Add("Как в системе", "Same as system", "Як у системі", "Wie im System", "Come nel sistema", "Como el sistema", "Comme le système", "Como no sistema", "كما في النظام", "सिस्टम जैसा", "সিস্টেমের মতো", "سسٹم کی طرح", "跟随系统")
        Add("Светлая", "Light", "Світла", "Hell", "Chiaro", "Claro", "Clair", "Claro", "فاتح", "हल्की", "হালকা", "ہلکا", "浅色")
        Add("Тёмная", "Dark", "Темна", "Dunkel", "Scuro", "Oscuro", "Sombre", "Escuro", "داكن", "गहरी", "গাঢ়", "گہرا", "深色")
    End Sub

End Class
