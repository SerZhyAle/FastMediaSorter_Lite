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

        ' --- the file manager mode (257) -----------------------------------------------
        Add("Ссылка", "Link", "Посилання", "Verknüpfung", "Collegamento", "Enlace", "Lien", "Link",
            "رابط", "लिंक", "লিংক", "لنک", "链接")
        Add("Сортировка", "Sorting", "Сортування", "Sortieren", "Ordinamento", "Clasificación", "Tri", "Classificação",
            "الفرز", "छँटाई", "বাছাই", "چھانٹی", "整理")
        Add("Файловый менеджер", "File manager", "Файловий менеджер", "Dateimanager", "Gestione file",
            "Administrador de archivos", "Gestionnaire de fichiers", "Gerenciador de arquivos", "مدير الملفات",
            "फ़ाइल प्रबंधक", "ফাইল ম্যানেজার", "فائل مینیجر", "文件管理器")
        Add("Режим панели", "Panel mode", "Режим панелі", "Panelmodus", "Modalità pannello", "Modo del panel",
            "Mode du panneau", "Modo do painel", "وضع اللوحة", "पैनल मोड", "প্যানেল মোড", "پینل موڈ", "面板模式")
        Add("Места", "Places", "Місця", "Orte", "Posizioni", "Ubicaciones", "Emplacements", "Locais",
            "الأماكن", "स्थान", "অবস্থান", "مقامات", "位置")
        Add("Назад (Alt+Left)", "Back (Alt+Left)", "Назад (Alt+Left)", "Zurück (Alt+Left)", "Indietro (Alt+Left)",
            "Atrás (Alt+Left)", "Précédent (Alt+Left)", "Voltar (Alt+Left)", "رجوع (Alt+Left)", "वापस (Alt+Left)",
            "পিছনে (Alt+Left)", "پیچھے (Alt+Left)", "后退 (Alt+Left)")
        Add("Вперёд (Alt+Right)", "Forward (Alt+Right)", "Уперед (Alt+Right)", "Vorwärts (Alt+Right)", "Avanti (Alt+Right)",
            "Adelante (Alt+Right)", "Suivant (Alt+Right)", "Avançar (Alt+Right)", "تقدم (Alt+Right)", "आगे (Alt+Right)",
            "সামনে (Alt+Right)", "آگے (Alt+Right)", "前进 (Alt+Right)")
        Add("Вверх (Alt+Up)", "Up (Alt+Up)", "Угору (Alt+Up)", "Nach oben (Alt+Up)", "Su (Alt+Up)",
            "Subir (Alt+Up)", "Dossier parent (Alt+Up)", "Subir (Alt+Up)", "لأعلى (Alt+Up)", "ऊपर (Alt+Up)",
            "উপরে (Alt+Up)", "اوپر (Alt+Up)", "上一级 (Alt+Up)")
        Add("Обновить (F5)", "Refresh (F5)", "Оновити (F5)", "Aktualisieren (F5)", "Aggiorna (F5)",
            "Actualizar (F5)", "Actualiser (F5)", "Atualizar (F5)", "تحديث (F5)", "ताज़ा करें (F5)",
            "রিফ্রেশ (F5)", "ریفریش (F5)", "刷新 (F5)")
        Add("Адрес (Alt+D)", "Address (Alt+D)", "Адреса (Alt+D)", "Adresse (Alt+D)", "Indirizzo (Alt+D)",
            "Dirección (Alt+D)", "Adresse (Alt+D)", "Endereço (Alt+D)", "العنوان (Alt+D)", "पता (Alt+D)",
            "ঠিকানা (Alt+D)", "پتہ (Alt+D)", "地址 (Alt+D)")
        Add("Выберите место в списке слева",
            "Choose a place in the list on the left", "Виберіть місце у списку ліворуч",
            "Wählen Sie links in der Liste einen Ort", "Scegli una posizione nell'elenco a sinistra",
            "Elija una ubicación en la lista de la izquierda", "Choisissez un emplacement dans la liste de gauche",
            "Escolha um local na lista à esquerda", "اختر مكانًا من القائمة على اليسار",
            "बाईं ओर की सूची से कोई स्थान चुनें", "বাঁ দিকের তালিকা থেকে একটি অবস্থান বেছে নিন",
            "بائیں طرف کی فہرست میں سے کوئی مقام منتخب کریں", "请在左侧列表中选择一个位置")

        ' What a listing that did not happen came to: each reason has its own sentence (AC257-2).
        Add("Папка не найдена", "Folder not found", "Теку не знайдено", "Ordner nicht gefunden", "Cartella non trovata",
            "No se encontró la carpeta", "Dossier introuvable", "Pasta não encontrada", "لم يتم العثور على المجلد",
            "फ़ोल्डर नहीं मिला", "ফোল্ডার পাওয়া যায়নি", "فولڈر نہیں ملا", "未找到文件夹")
        Add("Это не папка", "This is not a folder", "Це не тека", "Das ist kein Ordner", "Questa non è una cartella",
            "Esto no es una carpeta", "Ce n'est pas un dossier", "Isto não é uma pasta", "هذا ليس مجلدًا",
            "यह फ़ोल्डर नहीं है", "এটি একটি ফোল্ডার নয়", "یہ فولڈر نہیں ہے", "这不是文件夹")
        Add("Нет доступа к папке", "No access to the folder", "Немає доступу до теки", "Kein Zugriff auf den Ordner",
            "Nessun accesso alla cartella", "Sin acceso a la carpeta", "Accès au dossier refusé", "Sem acesso à pasta",
            "لا يوجد وصول إلى المجلد", "फ़ोल्डर तक पहुँच नहीं है", "ফোল্ডারে প্রবেশাধিকার নেই",
            "فولڈر تک رسائی نہیں ہے", "无权访问该文件夹")
        Add("Нет связи с ресурсом", "The resource does not answer", "Ресурс не відповідає", "Die Ressource antwortet nicht",
            "La risorsa non risponde", "El recurso no responde", "La ressource ne répond pas", "O recurso não responde",
            "المورد لا يستجيب", "संसाधन उत्तर नहीं दे रहा", "রিসোর্স সাড়া দিচ্ছে না", "وسیلہ جواب نہیں دے رہا", "资源没有响应")
        Add("Чтение папки отменено", "Reading the folder was cancelled", "Читання теки скасовано",
            "Das Lesen des Ordners wurde abgebrochen", "La lettura della cartella è stata annullata",
            "Se canceló la lectura de la carpeta", "La lecture du dossier a été annulée",
            "A leitura da pasta foi cancelada", "تم إلغاء قراءة المجلد", "फ़ोल्डर पढ़ना रद्द किया गया",
            "ফোল্ডার পড়া বাতিল করা হয়েছে", "فولڈر کا پڑھنا منسوخ کر دیا گیا", "已取消读取文件夹")
        Add("Путь выходит за пределы этого места", "The path leaves this place", "Шлях виходить за межі цього місця",
            "Der Pfad verlässt diesen Ort", "Il percorso esce da questa posizione", "La ruta sale de esta ubicación",
            "Le chemin sort de cet emplacement", "O caminho sai deste local", "المسار يخرج عن حدود هذا المكان",
            "यह पथ इस स्थान से बाहर जाता है", "পথটি এই অবস্থানের বাইরে চলে যায়", "راستہ اس مقام سے باہر جاتا ہے",
            "路径超出了此位置的范围")
        Add("Для этого ресурса нужен пароль - подключитесь к нему в главном окне",
            "This resource needs a password - connect to it in the main window",
            "Для цього ресурсу потрібен пароль - підключіться до нього в головному вікні",
            "Diese Ressource braucht ein Passwort - verbinden Sie sich im Hauptfenster",
            "Questa risorsa richiede una password - connettiti dalla finestra principale",
            "Este recurso necesita una contraseña: conéctese a él en la ventana principal",
            "Cette ressource demande un mot de passe - connectez-vous-y dans la fenêtre principale",
            "Este recurso precisa de uma senha - conecte-se a ele na janela principal",
            "يحتاج هذا المورد إلى كلمة مرور - اتصل به من النافذة الرئيسية",
            "इस संसाधन के लिए पासवर्ड चाहिए - मुख्य विंडो में इससे जुड़ें",
            "এই রিসোর্সের জন্য পাসওয়ার্ড লাগবে - প্রধান উইন্ডোতে এতে সংযুক্ত হন",
            "اس وسیلے کے لیے پاس ورڈ درکار ہے - مرکزی ونڈو میں اس سے جڑیں",
            "此资源需要密码 - 请在主窗口中连接它")
        Add("Ключ сервера не подтверждён - подключитесь к нему в главном окне",
            "The server key is not confirmed - connect to it in the main window",
            "Ключ сервера не підтверджено - підключіться до нього в головному вікні",
            "Der Serverschlüssel ist nicht bestätigt - verbinden Sie sich im Hauptfenster",
            "La chiave del server non è confermata - connettiti dalla finestra principale",
            "La clave del servidor no está confirmada: conéctese al servidor en la ventana principal",
            "La clé du serveur n'est pas confirmée - connectez-vous au serveur dans la fenêtre principale",
            "A chave do servidor não está confirmada - conecte-se a ele na janela principal",
            "مفتاح الخادم غير مؤكد - اتصل به من النافذة الرئيسية",
            "सर्वर की कुंजी की पुष्टि नहीं हुई है - मुख्य विंडो में उससे जुड़ें",
            "সার্ভারের কী নিশ্চিত করা হয়নি - প্রধান উইন্ডোতে এতে সংযুক্ত হন",
            "سرور کی کلید کی تصدیق نہیں ہوئی - مرکزی ونڈو میں اس سے جڑیں",
            "服务器密钥尚未确认 - 请在主窗口中连接它")
        Add("Это место недоступно", "This place is unavailable", "Це місце недоступне", "Dieser Ort ist nicht verfügbar",
            "Questa posizione non è disponibile", "Esta ubicación no está disponible", "Cet emplacement est indisponible",
            "Este local está indisponível", "هذا المكان غير متاح", "यह स्थान उपलब्ध नहीं है",
            "এই অবস্থানটি পাওয়া যাচ্ছে না", "یہ مقام دستیاب نہیں ہے", "此位置不可用")
        Add("Показана только часть папки - список неполный",
            "Only part of the folder is shown - the list is incomplete",
            "Показано лише частину теки - список неповний",
            "Es wird nur ein Teil des Ordners angezeigt - die Liste ist unvollständig",
            "È mostrata solo una parte della cartella - l'elenco è incompleto",
            "Solo se muestra una parte de la carpeta: la lista está incompleta",
            "Seule une partie du dossier est affichée - la liste est incomplète",
            "Apenas parte da pasta é mostrada - a lista está incompleta",
            "يتم عرض جزء من المجلد فقط - القائمة غير مكتملة",
            "फ़ोल्डर का केवल एक हिस्सा दिखाया गया है - सूची अधूरी है",
            "ফোল্ডারের শুধু একটি অংশ দেখানো হচ্ছে - তালিকা অসম্পূর্ণ",
            "فولڈر کا صرف ایک حصہ دکھایا گیا ہے - فہرست نامکمل ہے",
            "仅显示了文件夹的一部分 - 列表不完整")
        Add("Чтение: {0}.. (Esc - отмена)", "Reading: {0}.. (Esc - cancel)", "Читання: {0}.. (Esc - скасувати)",
            "Lesen: {0}.. (Esc - Abbrechen)", "Lettura: {0}.. (Esc - annulla)", "Leyendo: {0}.. (Esc - cancelar)",
            "Lecture : {0}.. (Échap - annuler)", "Lendo: {0}.. (Esc - cancelar)", "قراءة: {0}.. (Esc - إلغاء)",
            "पढ़ा जा रहा है: {0}.. (Esc - रद्द करें)", "পড়া হচ্ছে: {0}.. (Esc - বাতিল)",
            "پڑھا جا رہا ہے: {0}.. (Esc - منسوخ)", "正在读取：{0}.. (Esc - 取消)")
        Add("Адрес не распознан", "The address is not recognised", "Адресу не розпізнано", "Die Adresse wird nicht erkannt",
            "Indirizzo non riconosciuto", "No se reconoce la dirección", "Adresse non reconnue", "Endereço não reconhecido",
            "العنوان غير معروف", "पता पहचाना नहीं गया", "ঠিকানাটি চেনা যায়নি", "پتہ پہچانا نہیں گیا", "无法识别该地址")
        Add("Такого ресурса нет в списке - добавьте его в настройках",
            "No such resource in the list - add it in the settings",
            "Такого ресурсу немає в списку - додайте його в налаштуваннях",
            "Diese Ressource ist nicht in der Liste - fügen Sie sie in den Einstellungen hinzu",
            "Questa risorsa non è nell'elenco - aggiungila nelle impostazioni",
            "Ese recurso no está en la lista: añádalo en los ajustes",
            "Cette ressource n'est pas dans la liste - ajoutez-la dans les paramètres",
            "Esse recurso não está na lista - adicione-o nas configurações",
            "هذا المورد غير موجود في القائمة - أضفه من الإعدادات",
            "यह संसाधन सूची में नहीं है - इसे सेटिंग में जोड़ें",
            "এই রিসোর্সটি তালিকায় নেই - সেটিংসে এটি যোগ করুন",
            "یہ وسیلہ فہرست میں نہیں ہے - اسے ترتیبات میں شامل کریں",
            "列表中没有此资源 - 请在设置中添加")
        Add("В режиме менеджера файловые операции пока недоступны",
            "File operations are not available in manager mode yet",
            "У режимі менеджера файлові операції поки недоступні",
            "Im Manager-Modus sind Dateioperationen noch nicht verfügbar",
            "Le operazioni sui file non sono ancora disponibili in modalità gestione",
            "Las operaciones con archivos aún no están disponibles en el modo administrador",
            "Les opérations sur les fichiers ne sont pas encore disponibles en mode gestionnaire",
            "As operações com arquivos ainda não estão disponíveis no modo gerenciador",
            "عمليات الملفات غير متاحة بعد في وضع المدير",
            "प्रबंधक मोड में फ़ाइल संचालन अभी उपलब्ध नहीं हैं",
            "ম্যানেজার মোডে ফাইল অপারেশন এখনও উপলব্ধ নয়",
            "مینیجر موڈ میں فائل آپریشنز ابھی دستیاب نہیں ہیں",
            "管理器模式下暂不支持文件操作")

        ' The key help of the mode (F1 and the hover on empty space).
        Add("Файловый менеджер: двойной клик или Enter по папке - войти в неё; просмотр, выбор и закрытие панели не меняют то, что показывает главное окно",
            "File manager: double-click or Enter on a folder opens it; browsing, selecting and closing the panel do not change what the main window shows",
            "Файловий менеджер: подвійний клік або Enter на теці - увійти в неї; перегляд, вибір і закриття панелі не змінюють те, що показує головне вікно",
            "Dateimanager: Doppelklick oder Enter auf einem Ordner öffnet ihn; Durchsuchen, Auswählen und Schließen des Panels ändern nichts an der Anzeige im Hauptfenster",
            "Gestione file: doppio clic o Invio su una cartella per aprirla; navigare, selezionare e chiudere il pannello non cambia ciò che mostra la finestra principale",
            "Administrador de archivos: doble clic o Intro en una carpeta para abrirla; explorar, seleccionar y cerrar el panel no cambia lo que muestra la ventana principal",
            "Gestionnaire de fichiers : double-clic ou Entrée sur un dossier pour l'ouvrir ; parcourir, sélectionner et fermer le panneau ne change pas ce que montre la fenêtre principale",
            "Gerenciador de arquivos: clique duplo ou Enter em uma pasta para abri-la; navegar, selecionar e fechar o painel não altera o que a janela principal mostra",
            "مدير الملفات: انقر نقرًا مزدوجًا أو اضغط Enter على مجلد لفتحه؛ التصفح والتحديد وإغلاق اللوحة لا تغيّر ما تعرضه النافذة الرئيسية",
            "फ़ाइल प्रबंधक: किसी फ़ोल्डर पर डबल-क्लिक या Enter से उसे खोलें; ब्राउज़ करने, चुनने और पैनल बंद करने से मुख्य विंडो में दिखने वाली चीज़ नहीं बदलती",
            "ফাইল ম্যানেজার: কোনো ফোল্ডারে ডাবল-ক্লিক বা Enter চাপলে সেটি খোলে; ব্রাউজ, নির্বাচন ও প্যানেল বন্ধ করলে প্রধান উইন্ডোর প্রদর্শন বদলায় না",
            "فائل مینیجر: کسی فولڈر پر ڈبل کلک یا Enter سے اسے کھولیں؛ براؤز کرنے، منتخب کرنے اور پینل بند کرنے سے مرکزی ونڈو میں نظر آنے والی چیز نہیں بدلتی",
            "文件管理器：双击文件夹或按 Enter 进入；浏览、选择和关闭面板都不会改变主窗口显示的内容")
        Add("Alt+Left и Alt+Right: назад и вперёд по истории; Alt+Up: на уровень выше; F5: обновить; Alt+D: адресная строка (Enter - перейти, Esc - вернуть адрес)",
            "Alt+Left and Alt+Right: back and forward in history; Alt+Up: one level up; F5: refresh; Alt+D: address bar (Enter - go, Esc - restore the address)",
            "Alt+Left і Alt+Right: назад і вперед за історією; Alt+Up: на рівень вище; F5: оновити; Alt+D: адресний рядок (Enter - перейти, Esc - повернути адресу)",
            "Alt+Left und Alt+Right: zurück und vor im Verlauf; Alt+Up: eine Ebene höher; F5: aktualisieren; Alt+D: Adressleiste (Enter - öffnen, Esc - Adresse zurücksetzen)",
            "Alt+Left e Alt+Right: indietro e avanti nella cronologia; Alt+Up: livello superiore; F5: aggiorna; Alt+D: barra degli indirizzi (Invio - vai, Esc - ripristina l'indirizzo)",
            "Alt+Left y Alt+Right: atrás y adelante en el historial; Alt+Up: subir un nivel; F5: actualizar; Alt+D: barra de direcciones (Intro - ir, Esc - restaurar la dirección)",
            "Alt+Left et Alt+Right : précédent et suivant dans l'historique ; Alt+Up : dossier parent ; F5 : actualiser ; Alt+D : barre d'adresse (Entrée - aller, Échap - rétablir l'adresse)",
            "Alt+Left e Alt+Right: voltar e avançar no histórico; Alt+Up: subir um nível; F5: atualizar; Alt+D: barra de endereço (Enter - ir, Esc - restaurar o endereço)",
            "Alt+Left وAlt+Right: رجوع وتقدم في السجل؛ Alt+Up: مستوى أعلى؛ F5: تحديث؛ Alt+D: شريط العنوان (Enter - انتقال، Esc - استعادة العنوان)",
            "Alt+Left और Alt+Right: इतिहास में पीछे और आगे; Alt+Up: एक स्तर ऊपर; F5: ताज़ा करें; Alt+D: पता पट्टी (Enter - जाएँ, Esc - पता वापस लाएँ)",
            "Alt+Left ও Alt+Right: ইতিহাসে পিছনে ও সামনে; Alt+Up: এক ধাপ উপরে; F5: রিফ্রেশ; Alt+D: ঠিকানা বার (Enter - যান, Esc - ঠিকানা ফিরিয়ে আনুন)",
            "Alt+Left اور Alt+Right: تاریخ میں پیچھے اور آگے؛ Alt+Up: ایک سطح اوپر؛ F5: ریفریش؛ Alt+D: ایڈریس بار (Enter - جائیں، Esc - پتہ بحال کریں)",
            "Alt+Left 和 Alt+Right：历史记录后退和前进；Alt+Up：上一级；F5：刷新；Alt+D：地址栏（Enter - 转到，Esc - 恢复地址）")
        Add("Ctrl+A: выделить всё; Ctrl+Shift+A: снять выделение; Ctrl+I: обратить; Пробел: отметить; Ctrl+L: плитки или список; Esc: отменить чтение папки, затем закрыть панель",
            "Ctrl+A: select all; Ctrl+Shift+A: clear selection; Ctrl+I: invert; Space: toggle; Ctrl+L: tiles or list; Esc: cancel reading the folder, then close the panel",
            "Ctrl+A: виділити все; Ctrl+Shift+A: зняти виділення; Ctrl+I: обернути; Пробіл: відмітити; Ctrl+L: плитки або список; Esc: скасувати читання теки, потім закрити панель",
            "Ctrl+A: alles auswählen; Ctrl+Shift+A: Auswahl aufheben; Ctrl+I: umkehren; Leertaste: umschalten; Ctrl+L: Kacheln oder Liste; Esc: Lesen des Ordners abbrechen, dann das Panel schließen",
            "Ctrl+A: seleziona tutto; Ctrl+Shift+A: deseleziona; Ctrl+I: inverti; Spazio: attiva/disattiva; Ctrl+L: riquadri o elenco; Esc: annulla la lettura della cartella, poi chiude il pannello",
            "Ctrl+A: seleccionar todo; Ctrl+Shift+A: quitar selección; Ctrl+I: invertir; Espacio: marcar; Ctrl+L: mosaico o lista; Esc: cancelar la lectura de la carpeta y luego cerrar el panel",
            "Ctrl+A : tout sélectionner ; Ctrl+Shift+A : désélectionner ; Ctrl+I : inverser ; Espace : cocher ; Ctrl+L : mosaïque ou liste ; Échap : annuler la lecture du dossier, puis fermer le panneau",
            "Ctrl+A: selecionar tudo; Ctrl+Shift+A: limpar seleção; Ctrl+I: inverter; Espaço: marcar; Ctrl+L: blocos ou lista; Esc: cancelar a leitura da pasta e depois fechar o painel",
            "Ctrl+A: تحديد الكل؛ Ctrl+Shift+A: إلغاء التحديد؛ Ctrl+I: عكس التحديد؛ مسافة: تبديل؛ Ctrl+L: مربعات أو قائمة؛ Esc: إلغاء قراءة المجلد ثم إغلاق اللوحة",
            "Ctrl+A: सब चुनें; Ctrl+Shift+A: चयन हटाएँ; Ctrl+I: उलटें; Space: टॉगल; Ctrl+L: टाइल या सूची; Esc: फ़ोल्डर पढ़ना रद्द करें, फिर पैनल बंद करें",
            "Ctrl+A: সব নির্বাচন; Ctrl+Shift+A: নির্বাচন মুছুন; Ctrl+I: উল্টান; Space: টগল; Ctrl+L: টাইল বা তালিকা; Esc: ফোল্ডার পড়া বাতিল করুন, তারপর প্যানেল বন্ধ করুন",
            "Ctrl+A: سب منتخب کریں؛ Ctrl+Shift+A: انتخاب ہٹائیں؛ Ctrl+I: الٹ دیں؛ Space: ٹوگل؛ Ctrl+L: ٹائلیں یا فہرست؛ Esc: فولڈر کا پڑھنا منسوخ کریں، پھر پینل بند کریں",
            "Ctrl+A：全选；Ctrl+Shift+A：取消选择；Ctrl+I：反选；空格：切换；Ctrl+L：平铺或列表；Esc：取消读取文件夹，然后关闭面板")
        Add("Файловые операции (цифры, Del, Ctrl+Z) в этом режиме пока не работают",
            "File operations (digits, Del, Ctrl+Z) do not work in this mode yet",
            "Файлові операції (цифри, Del, Ctrl+Z) у цьому режимі поки не працюють",
            "Dateioperationen (Ziffern, Del, Ctrl+Z) funktionieren in diesem Modus noch nicht",
            "Le operazioni sui file (cifre, Del, Ctrl+Z) non funzionano ancora in questa modalità",
            "Las operaciones con archivos (dígitos, Del, Ctrl+Z) aún no funcionan en este modo",
            "Les opérations sur les fichiers (chiffres, Del, Ctrl+Z) ne fonctionnent pas encore dans ce mode",
            "As operações com arquivos (dígitos, Del, Ctrl+Z) ainda não funcionam neste modo",
            "عمليات الملفات (الأرقام وDel وCtrl+Z) لا تعمل في هذا الوضع بعد",
            "इस मोड में फ़ाइल संचालन (अंक, Del, Ctrl+Z) अभी काम नहीं करते",
            "এই মোডে ফাইল অপারেশন (সংখ্যা, Del, Ctrl+Z) এখনও কাজ করে না",
            "اس موڈ میں فائل آپریشنز (ہندسے، Del، Ctrl+Z) ابھی کام نہیں کرتے",
            "此模式下暂不支持文件操作（数字键、Del、Ctrl+Z）")
    End Sub

End Class
