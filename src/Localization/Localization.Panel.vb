Option Strict On

' <summary>
' Strings of the mainline F3 panel (005_SPECIFICATION_THUMBNAIL_GRID_TRIAGE_DOTNET10.md): the
' toolbar, the hover help, the details-list headers, the report window and the strip. The
' strings of the batch RUN itself (summary line, skip reasons, the collision question) are in
' Localization.Files.vb under "Batch file operations".
'
' Compiled by both builds for the same reason Localization.Secure.vb is: the x86 exe shares the
' registry, and a table missing from one project fails its shared constructor with BC30451.
' Nothing here is reachable on net48 - the original panel keeps its own, older strings.
'
' Columns after the Russian key: en, uk, de, it, es, fr, pt, ar, hi, bn, ur, zh.
' Никогда не ставьте «умные» кавычки в литерал - VB считает U+201C/U+201D разделителями
' строки; используются «…» и 「…」.
' </summary>
Partial Public NotInheritable Class Localization

    Private Shared Sub AddPanelStrings()
        Add("Книга", "Book", "Книга", "Buch", "Libro", "Libro", "Livre", "Livro",
            "كتاب", "पुस्तक", "বই", "کتاب", "书籍")
        Add("Поток", "Stream", "Потік", "Stream", "Flusso", "Transmisión", "Flux", "Fluxo",
            "بث", "स्ट्रीम", "স্ট্রিম", "اسٹریم", "流")

        ' --- toolbar ------------------------------------------------------------------
        Add("Размер миниатюр", "Thumbnail size", "Розмір мініатюр", "Größe der Miniaturansichten",
            "Dimensione delle miniature", "Tamaño de las miniaturas", "Taille des miniatures",
            "Tamanho das miniaturas", "حجم الصور المصغرة", "थंबनेल का आकार", "থাম্বনেইলের আকার",
            "تھمب نیل کا سائز", "缩略图大小")
        Add("Размер:", "Size:", "Розмір:", "Größe:", "Dimensione:", "Tamaño:", "Taille :",
            "Tamanho:", "الحجم:", "आकार:", "আকার:", "سائز:", "大小：")
        Add("Выбрать всё", "Select all", "Вибрати все", "Alle auswählen", "Seleziona tutto",
            "Seleccionar todo", "Tout sélectionner", "Selecionar tudo", "تحديد الكل", "सभी चुनें",
            "সব নির্বাচন করুন", "سب منتخب کریں", "全选")
        Add("Плитки", "Tiles", "Плитки", "Kacheln", "Riquadri", "Mosaicos", "Mosaïque",
            "Blocos", "بلاطات", "टाइलें", "টাইল", "ٹائلیں", "平铺")
        Add("Список", "List", "Список", "Liste", "Elenco", "Lista", "Liste", "Lista",
            "قائمة", "सूची", "তালিকা", "فہرست", "列表")
        Add("Переключить вид: плитки или список (Ctrl+L)",
            "Switch view: tiles or list (Ctrl+L)",
            "Перемкнути вигляд: плитки або список (Ctrl+L)",
            "Ansicht wechseln: Kacheln oder Liste (Ctrl+L)",
            "Cambia visualizzazione: riquadri o elenco (Ctrl+L)",
            "Cambiar vista: mosaicos o lista (Ctrl+L)",
            "Changer d'affichage : mosaïque ou liste (Ctrl+L)",
            "Alternar exibição: blocos ou lista (Ctrl+L)",
            "تبديل العرض: بلاطات أو قائمة (Ctrl+L)",
            "दृश्य बदलें: टाइलें या सूची (Ctrl+L)",
            "দৃশ্য পাল্টান: টাইল বা তালিকা (Ctrl+L)",
            "منظر بدلیں: ٹائلیں یا فہرست (Ctrl+L)",
            "切换视图：平铺或列表（Ctrl+L）")
        Add("Выбрано: {0} из {1}", "Selected: {0} of {1}", "Вибрано: {0} з {1}", "Ausgewählt: {0} von {1}",
            "Selezionati: {0} di {1}", "Seleccionados: {0} de {1}", "Sélectionnés : {0} sur {1}",
            "Selecionados: {0} de {1}", "المحدد: {0} من {1}", "चयनित: {0}/{1}",
            "নির্বাচিত: {1}-এর মধ্যে {0}", "منتخب: {0} از {1}", "已选择：{0} / {1}")

        ' --- the hover help (five lines, each a key of its own so each can be reworded) -----
        Add("ЛКМ: выбрать; Shift+ЛКМ: диапазон; Ctrl+ЛКМ: добавить или убрать; протяжка мышью - рамка выделения",
            "Left click: select; Shift+click: range; Ctrl+click: add or remove; drag with the mouse: selection box",
            "ЛКМ: вибрати; Shift+ЛКМ: діапазон; Ctrl+ЛКМ: додати або прибрати; перетягування мишею - рамка виділення",
            "Linksklick: auswählen; Shift+Klick: Bereich; Ctrl+Klick: hinzufügen oder entfernen; Ziehen mit der Maus: Auswahlrahmen",
            "Clic sinistro: seleziona; Shift+clic: intervallo; Ctrl+clic: aggiungi o rimuovi; trascinamento: riquadro di selezione",
            "Clic izquierdo: seleccionar; Shift+clic: rango; Ctrl+clic: añadir o quitar; arrastrar con el ratón: recuadro de selección",
            "Clic gauche : sélectionner ; Shift+clic : plage ; Ctrl+clic : ajouter ou retirer ; glisser avec la souris : cadre de sélection",
            "Clique esquerdo: selecionar; Shift+clique: intervalo; Ctrl+clique: adicionar ou remover; arrastar com o mouse: caixa de seleção",
            "النقر الأيسر: تحديد؛ Shift+نقر: نطاق؛ Ctrl+نقر: إضافة أو إزالة؛ السحب بالماوس: مربع تحديد",
            "बायाँ क्लिक: चुनें; Shift+क्लिक: श्रेणी; Ctrl+क्लिक: जोड़ें या हटाएँ; माउस से खींचना: चयन बॉक्स",
            "বাম ক্লিক: নির্বাচন; Shift+ক্লিক: পরিসর; Ctrl+ক্লিক: যোগ বা বাদ; মাউস দিয়ে টানা: নির্বাচন বাক্স",
            "بایاں کلک: منتخب کریں؛ Shift+کلک: حد؛ Ctrl+کلک: شامل یا خارج کریں؛ ماؤس سے گھسیٹنا: انتخابی خانہ",
            "左键：选择；Shift+左键：选择范围；Ctrl+左键：添加或移除；鼠标拖动：框选")
        Add("Ctrl+A: выделить всё; Ctrl+Shift+A: снять выделение; Ctrl+I: обратить; Пробел: отметить; стрелки, Home, End, PgUp, PgDn: перемещение",
            "Ctrl+A: select all; Ctrl+Shift+A: clear selection; Ctrl+I: invert; Space: toggle; arrows, Home, End, PgUp, PgDn: move",
            "Ctrl+A: виділити все; Ctrl+Shift+A: зняти виділення; Ctrl+I: обернути; Пробіл: відмітити; стрілки, Home, End, PgUp, PgDn: переміщення",
            "Ctrl+A: alles auswählen; Ctrl+Shift+A: Auswahl aufheben; Ctrl+I: umkehren; Leertaste: umschalten; Pfeiltasten, Home, End, PgUp, PgDn: bewegen",
            "Ctrl+A: seleziona tutto; Ctrl+Shift+A: deseleziona; Ctrl+I: inverti; Spazio: attiva/disattiva; frecce, Home, End, PgUp, PgDn: sposta",
            "Ctrl+A: seleccionar todo; Ctrl+Shift+A: quitar selección; Ctrl+I: invertir; Espacio: marcar; flechas, Home, End, PgUp, PgDn: mover",
            "Ctrl+A : tout sélectionner ; Ctrl+Shift+A : désélectionner ; Ctrl+I : inverser ; Espace : cocher ; flèches, Home, End, PgUp, PgDn : déplacer",
            "Ctrl+A: selecionar tudo; Ctrl+Shift+A: limpar seleção; Ctrl+I: inverter; Espaço: marcar; setas, Home, End, PgUp, PgDn: mover",
            "Ctrl+A: تحديد الكل؛ Ctrl+Shift+A: إلغاء التحديد؛ Ctrl+I: عكس التحديد؛ مسافة: تبديل؛ الأسهم وHome وEnd وPgUp وPgDn: تنقّل",
            "Ctrl+A: सब चुनें; Ctrl+Shift+A: चयन हटाएँ; Ctrl+I: उलटें; Space: टॉगल; तीर, Home, End, PgUp, PgDn: चलना",
            "Ctrl+A: সব নির্বাচন; Ctrl+Shift+A: নির্বাচন মুছুন; Ctrl+I: উল্টান; Space: টগল; তীর, Home, End, PgUp, PgDn: সরানো",
            "Ctrl+A: سب منتخب کریں؛ Ctrl+Shift+A: انتخاب ہٹائیں؛ Ctrl+I: الٹ دیں؛ Space: ٹوگل؛ تیر، Home، End، PgUp، PgDn: حرکت",
            "Ctrl+A：全选；Ctrl+Shift+A：取消选择；Ctrl+I：反选；空格：切换；方向键、Home、End、PgUp、PgDn：移动")
        Add("Двойной клик или Enter: открыть в главном окне; Esc: отменить операцию, затем закрыть панель",
            "Double-click or Enter: open in the main window; Esc: cancel the operation, then close the panel",
            "Подвійний клік або Enter: відкрити в головному вікні; Esc: скасувати операцію, потім закрити панель",
            "Doppelklick oder Enter: im Hauptfenster öffnen; Esc: Vorgang abbrechen, dann Panel schließen",
            "Doppio clic o Invio: apri nella finestra principale; Esc: annulla l'operazione, poi chiudi il pannello",
            "Doble clic o Intro: abrir en la ventana principal; Esc: cancelar la operación y luego cerrar el panel",
            "Double-clic ou Entrée : ouvrir dans la fenêtre principale ; Échap : annuler l'opération, puis fermer le panneau",
            "Duplo clique ou Enter: abrir na janela principal; Esc: cancelar a operação e depois fechar o painel",
            "نقر مزدوج أو Enter: فتح في النافذة الرئيسية؛ Esc: إلغاء العملية ثم إغلاق اللوحة",
            "डबल-क्लिक या Enter: मुख्य विंडो में खोलें; Esc: कार्रवाई रद्द करें, फिर पैनल बंद करें",
            "ডাবল-ক্লিক বা Enter: প্রধান উইন্ডোতে খুলুন; Esc: কাজ বাতিল করুন, তারপর প্যানেল বন্ধ করুন",
            "ڈبل کلک یا Enter: مرکزی ونڈو میں کھولیں؛ Esc: عمل منسوخ کریں، پھر پینل بند کریں",
            "双击或 Enter：在主窗口中打开；Esc：取消操作，然后关闭面板")
        Add("Del: удалить выделенное; цифры 0-9: переместить; Shift + цифра верхнего ряда: скопировать",
            "Del: delete the selection; digits 0-9: move; Shift + a top-row digit: copy",
            "Del: видалити виділене; цифри 0-9: перемістити; Shift + цифра верхнього ряду: копіювати",
            "Del: Auswahl löschen; Ziffern 0-9: verschieben; Shift + Ziffer der oberen Reihe: kopieren",
            "Del: elimina la selezione; cifre 0-9: sposta; Shift + cifra della riga superiore: copia",
            "Del: eliminar la selección; dígitos 0-9: mover; Shift + dígito de la fila superior: copiar",
            "Del : supprimer la sélection ; chiffres 0-9 : déplacer ; Shift + chiffre de la rangée supérieure : copier",
            "Del: excluir a seleção; dígitos 0-9: mover; Shift + dígito da fileira superior: copiar",
            "Del: حذف المحدد؛ الأرقام 0-9: نقل؛ Shift + رقم من الصف العلوي: نسخ",
            "Del: चयनित हटाएँ; अंक 0-9: स्थानांतरित करें; Shift + ऊपरी पंक्ति का अंक: कॉपी करें",
            "Del: নির্বাচিত মুছুন; সংখ্যা 0-9: স্থানান্তর; Shift + উপরের সারির সংখ্যা: কপি",
            "Del: منتخب حذف کریں؛ ہندسے 0-9: منتقل کریں؛ Shift + اوپر کی قطار کا ہندسہ: کاپی کریں",
            "Del：删除所选；数字 0-9：移动；Shift + 顶行数字：复制")
        Add("Ctrl+Z: отменить последнюю операцию; Ctrl+L: плитки или список",
            "Ctrl+Z: undo the last operation; Ctrl+L: tiles or list",
            "Ctrl+Z: скасувати останню операцію; Ctrl+L: плитки або список",
            "Ctrl+Z: letzten Vorgang rückgängig machen; Ctrl+L: Kacheln oder Liste",
            "Ctrl+Z: annulla l'ultima operazione; Ctrl+L: riquadri o elenco",
            "Ctrl+Z: deshacer la última operación; Ctrl+L: mosaicos o lista",
            "Ctrl+Z : annuler la dernière opération ; Ctrl+L : mosaïque ou liste",
            "Ctrl+Z: desfazer a última operação; Ctrl+L: blocos ou lista",
            "Ctrl+Z: التراجع عن آخر عملية؛ Ctrl+L: بلاطات أو قائمة",
            "Ctrl+Z: अंतिम कार्रवाई पूर्ववत करें; Ctrl+L: टाइलें या सूची",
            "Ctrl+Z: শেষ কাজ পূর্বাবস্থায়; Ctrl+L: টাইল বা তালিকা",
            "Ctrl+Z: آخری عمل واپس کریں؛ Ctrl+L: ٹائلیں یا فہرست",
            "Ctrl+Z：撤销上一次操作；Ctrl+L：平铺或列表")

        ' --- the strip at the bottom -------------------------------------------------------
        Add("Ничего не выбрано", "Nothing selected", "Нічого не вибрано", "Nichts ausgewählt",
            "Nessuna selezione", "Nada seleccionado", "Aucune sélection", "Nada selecionado",
            "لم يتم تحديد شيء", "कुछ भी चयनित नहीं", "কিছু নির্বাচিত নয়", "کچھ منتخب نہیں", "未选择任何内容")
        Add("нажмите, чтобы увидеть подробности", "click to see details", "натисніть, щоб побачити подробиці",
            "klicken für Details", "fai clic per i dettagli", "haga clic para ver los detalles",
            "cliquez pour voir les détails", "clique para ver os detalhes", "انقر لعرض التفاصيل",
            "विवरण देखने के लिए क्लिक करें", "বিস্তারিত দেখতে ক্লিক করুন", "تفصیل دیکھنے کے لیے کلک کریں", "单击查看详情")
        Add("Остановка.. текущий файл будет доделан", "Stopping.. the current file will be finished",
            "Зупинка.. поточний файл буде завершено", "Wird beendet.. die aktuelle Datei wird noch fertiggestellt",
            "Interruzione.. il file corrente verrà completato", "Deteniendo.. se terminará el archivo actual",
            "Arrêt.. le fichier en cours sera terminé", "Parando.. o arquivo atual será concluído",
            "جارٍ الإيقاف.. سيتم إنهاء الملف الحالي", "रोका जा रहा है.. वर्तमान फ़ाइल पूरी की जाएगी",
            "থামানো হচ্ছে.. বর্তমান ফাইলটি শেষ করা হবে", "روکا جا رہا ہے.. موجودہ فائل مکمل کی جائے گی",
            "正在停止.. 将完成当前文件")

        ' --- the details list -------------------------------------------------------------
        Add("Имя", "Name", "Ім'я", "Name", "Nome", "Nombre", "Nom", "Nome", "الاسم", "नाम", "নাম", "نام", "名称")
        Add("Тип", "Type", "Тип", "Typ", "Tipo", "Tipo", "Type", "Tipo", "النوع", "प्रकार", "ধরন", "قسم", "类型")
        Add("Размер", "Size", "Розмір", "Größe", "Dimensione", "Tamaño", "Taille", "Tamanho",
            "الحجم", "आकार", "আকার", "سائز", "大小")
        Add("Изменён", "Modified", "Змінено", "Geändert", "Modificato", "Modificado", "Modifié",
            "Modificado", "تاريخ التعديل", "संशोधित", "পরিবর্তিত", "ترمیم شدہ", "修改日期")
        Add("Документ", "Document", "Документ", "Dokument", "Documento", "Documento", "Document",
            "Documento", "مستند", "दस्तावेज़", "নথি", "دستاویز", "文档")
        Add("Контейнер", "Container", "Контейнер", "Container", "Contenitore", "Contenedor", "Conteneur",
            "Contêiner", "حاوية", "कंटेनर", "কন্টেইনার", "کنٹینر", "容器")
        Add("Файл", "File", "Файл", "Datei", "File", "Archivo", "Fichier", "Arquivo",
            "ملف", "फ़ाइल", "ফাইল", "فائل", "文件")

        ' --- the report window -------------------------------------------------------------
        Add("Отчёт об операции", "Operation report", "Звіт про операцію", "Bericht zum Vorgang",
            "Report dell'operazione", "Informe de la operación", "Rapport de l'opération",
            "Relatório da operação", "تقرير العملية", "कार्रवाई की रिपोर्ट", "কাজের প্রতিবেদন",
            "عمل کی رپورٹ", "操作报告")
        Add("Состояние", "Status", "Стан", "Status", "Stato", "Estado", "État", "Estado",
            "الحالة", "स्थिति", "অবস্থা", "حالت", "状态")
        Add("Подробности", "Details", "Подробиці", "Details", "Dettagli", "Detalles", "Détails",
            "Detalhes", "التفاصيل", "विवरण", "বিস্তারিত", "تفصیلات", "详细信息")

        ' --- settings row (Files and system > caches) ---------------------------------------
        Add("Кэш миниатюр панели, МБ", "Panel thumbnail cache, MB", "Кеш мініатюр панелі, МБ",
            "Miniaturen-Cache der Leiste, MB", "Cache miniature del pannello, MB",
            "Caché de miniaturas del panel, MB", "Cache des miniatures du panneau, Mo",
            "Cache de miniaturas do painel, MB", "ذاكرة التخزين المؤقت للصور المصغرة، ميغابايت",
            "पैनल थंबनेल कैश, MB", "প্যানেলের থাম্বনেইল ক্যাশ, MB", "پینل تھمب نیل کیش، MB",
            "面板缩略图缓存，MB")
        Add("Ускоряет повторное открытие панели F3. 0 отключает кэш.",
            "Speeds up reopening the F3 panel. 0 turns the cache off.",
            "Прискорює повторне відкриття панелі F3. 0 вимикає кеш.",
            "Beschleunigt das erneute Öffnen der F3-Leiste. 0 schaltet den Cache aus.",
            "Velocizza la riapertura del pannello F3. 0 disattiva la cache.",
            "Acelera la reapertura del panel F3. 0 desactiva la caché.",
            "Accélère la réouverture du panneau F3. 0 désactive le cache.",
            "Acelera a reabertura do painel F3. 0 desativa o cache.",
            "يسرّع إعادة فتح لوحة F3. القيمة 0 تعطّل ذاكرة التخزين المؤقت.",
            "F3 पैनल को दोबारा खोलना तेज़ करता है। 0 कैश बंद कर देता है।",
            "F3 প্যানেল আবার খোলা দ্রুত করে। 0 দিলে ক্যাশ বন্ধ হয়।",
            "F3 پینل کو دوبارہ کھولنا تیز کرتا ہے۔ 0 کیش بند کر دیتا ہے۔",
            "加快重新打开 F3 面板的速度。设为 0 将关闭缓存。")

        ' --- the busy band, the empty folder and the F1 title (126) -------------
        Add("Идёт операция - дождитесь или нажмите Esc",
            "An operation is running - wait for it, or press Esc",
            "Триває операція - дочекайтеся або натисніть Esc",
            "Ein Vorgang läuft - warten Sie, oder drücken Sie Esc",
            "Un'operazione è in corso - attendere o premere Esc",
            "Hay una operación en curso - espere o pulse Esc",
            "Une opération est en cours - attendez ou appuyez sur Échap",
            "Há uma operação em andamento - aguarde ou pressione Esc",
            "عملية قيد التنفيذ - انتظر أو اضغط Esc",
            "एक ऑपरेशन चल रहा है - प्रतीक्षा करें, या Esc दबाएँ",
            "একটি কাজ চলছে - অপেক্ষা করুন, বা Esc চাপুন",
            "ایک آپریشن جاری ہے - انتظار کریں، یا Esc دبائیں",
            "正在执行操作 - 请等待，或按 Esc")
        Add("Папка пуста",
            "The folder is empty", "Тека порожня", "Der Ordner ist leer", "La cartella è vuota",
            "La carpeta está vacía", "Le dossier est vide", "A pasta está vazia", "المجلد فارغ",
            "फ़ोल्डर खाली है", "ফোল্ডারটি খালি", "فولڈر خالی ہے", "文件夹为空")
        Add("Клавиши панели",
            "Panel keys", "Клавіші панелі", "Panel-Tasten", "Tasti del pannello",
            "Teclas del panel", "Touches du panneau", "Teclas do painel", "مفاتيح اللوحة",
            "पैनल की कुंजियाँ", "প্যানেলের কীগুলি", "پینل کی کلیدیں", "面板快捷键")
    End Sub

End Class
