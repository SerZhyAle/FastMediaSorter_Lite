Option Strict On

' <summary>
' Option texts of the drop-downs in the expanded (.NET 10) settings window -
' Table_Form.ExpandedSettings.vb.
'
' These were the worst of the strings the first migration pass missed: they were not
' even bilingual. Choice("topLeft", "Вверху слева") had no English text at all, so the
' shipped v26.7.23.1127 already showed Russian to every user, in eight drop-downs.
' Routing Choice() through T() fixes that and localizes them in the same move.
'
' Columns after the Russian key: en, uk, de, it, es, fr, pt, ar, hi, bn, ur, zh.
' </summary>
Partial Public NotInheritable Class Localization

    Private Shared Sub AddChoiceStrings()

        ' --- recipients overlay position ------------------------------------------

        Add("Вверху слева",
            "Top left", "Угорі ліворуч", "Oben links", "In alto a sinistra", "Arriba a la izquierda",
            "En haut à gauche", "Em cima à esquerda", "أعلى اليسار", "ऊपर बाएँ",
            "উপরে বাঁয়ে", "اوپر بائیں", "左上")

        Add("Вверху справа",
            "Top right", "Угорі праворуч", "Oben rechts", "In alto a destra", "Arriba a la derecha",
            "En haut à droite", "Em cima à direita", "أعلى اليمين", "ऊपर दाएँ",
            "উপরে ডানে", "اوپر دائیں", "右上")

        Add("Внизу слева",
            "Bottom left", "Внизу ліворуч", "Unten links", "In basso a sinistra", "Abajo a la izquierda",
            "En bas à gauche", "Em baixo à esquerda", "أسفل اليسار", "नीचे बाएँ",
            "নিচে বাঁয়ে", "نیچے بائیں", "左下")

        Add("Внизу справа",
            "Bottom right", "Внизу праворуч", "Unten rechts", "In basso a destra", "Abajo a la derecha",
            "En bas à droite", "Em baixo à direita", "أسفل اليمين", "नीचे दाएँ",
            "নিচে ডানে", "نیچے دائیں", "右下")

        ' --- scale of a newly opened image ----------------------------------------

        Add("Вписать",
            "Fit", "Вписати", "Einpassen", "Adatta", "Ajustar", "Ajuster", "Ajustar",
            "ملاءمة", "फ़िट करें", "ফিট করুন", "فٹ کریں", "适应窗口")

        Add("Запоминать для папки",
            "Remember per folder", "Запам'ятовувати для теки", "Pro Ordner merken",
            "Ricorda per cartella", "Recordar por carpeta", "Mémoriser par dossier",
            "Memorizar por pasta", "التذكّر لكل مجلد", "प्रति फ़ोल्डर याद रखें",
            "প্রতি ফোল্ডারে মনে রাখুন", "فی فولڈر یاد رکھیں", "按文件夹记住")

        ' --- slideshow order ------------------------------------------------------

        Add("Обычный порядок",
            "Natural order", "Звичайний порядок", "Normale Reihenfolge", "Ordine normale",
            "Orden normal", "Ordre normal", "Ordem normal", "الترتيب المعتاد",
            "सामान्य क्रम", "স্বাভাবিক ক্রম", "عام ترتیب", "正常顺序")

        Add("Случайный",
            "Random", "Випадковий", "Zufällig", "Casuale", "Aleatorio", "Aléatoire", "Aleatório",
            "عشوائي", "यादृच्छिक", "এলোমেলো", "بے ترتیب", "随机")

        Add("Без повторов до конца цикла",
            "No repeats until the cycle ends", "Без повторів до кінця циклу",
            "Keine Wiederholung bis zum Zyklusende", "Nessuna ripetizione fino a fine ciclo",
            "Sin repeticiones hasta terminar el ciclo", "Sans répétition jusqu'à la fin du cycle",
            "Sem repetições até ao fim do ciclo", "بدون تكرار حتى نهاية الدورة",
            "चक्र समाप्त होने तक कोई पुनरावृत्ति नहीं", "চক্র শেষ না হওয়া পর্যন্ত পুনরাবৃত্তি নেই",
            "سائیکل ختم ہونے تک کوئی تکرار نہیں", "一轮结束前不重复")

        ' --- what the slideshow hides ---------------------------------------------

        Add("Не скрывать",
            "Hide nothing", "Не приховувати", "Nichts ausblenden", "Non nascondere nulla",
            "No ocultar nada", "Ne rien masquer", "Não ocultar nada", "عدم الإخفاء",
            "कुछ न छिपाएँ", "কিছু লুকাবেন না", "کچھ نہ چھپائیں", "不隐藏")

        Add("Скрывать панель инструментов",
            "Hide the toolbar", "Приховувати панель інструментів", "Symbolleiste ausblenden",
            "Nascondi la barra degli strumenti", "Ocultar la barra de herramientas",
            "Masquer la barre d'outils", "Ocultar a barra de ferramentas",
            "إخفاء شريط الأدوات", "टूलबार छिपाएँ", "টুলবার লুকান", "ٹول بار چھپائیں", "隐藏工具栏")

        Add("Скрывать панель и статус",
            "Hide the toolbar and status bar", "Приховувати панель і статус",
            "Symbol- und Statusleiste ausblenden", "Nascondi barra strumenti e barra di stato",
            "Ocultar la barra de herramientas y la de estado",
            "Masquer la barre d'outils et la barre d'état",
            "Ocultar a barra de ferramentas e a de estado", "إخفاء شريط الأدوات وشريط الحالة",
            "टूलबार और स्टेटस बार छिपाएँ", "টুলবার ও স্ট্যাটাস বার লুকান",
            "ٹول بار اور اسٹیٹس بار چھپائیں", "隐藏工具栏和状态栏")

        ' --- what a click on a video does -----------------------------------------

        Add("Пауза / продолжить",
            "Pause / resume", "Пауза / продовжити", "Pause / fortsetzen", "Pausa / riprendi",
            "Pausar / reanudar", "Pause / reprendre", "Pausar / retomar",
            "إيقاف مؤقت / متابعة", "रोकें / जारी रखें", "বিরতি / চালিয়ে যান",
            "وقفہ / جاری رکھیں", "暂停 / 继续")

        ' --- what happens when a video ends ---------------------------------------

        Add("Оставить последний кадр",
            "Keep the last frame", "Залишити останній кадр", "Letztes Bild stehen lassen",
            "Lascia l'ultimo fotogramma", "Dejar el último fotograma",
            "Laisser la dernière image", "Manter o último fotograma",
            "إبقاء الإطار الأخير", "अंतिम फ़्रेम रखें", "শেষ ফ্রেম রাখুন",
            "آخری فریم رکھیں", "停在最后一帧")

        Add("Повторить",
            "Repeat", "Повторити", "Wiederholen", "Ripeti", "Repetir", "Répéter", "Repetir",
            "تكرار", "दोहराएँ", "পুনরাবৃত্তি", "دہرائیں", "重复")

        ' --- name collision on copy/move ------------------------------------------

        Add("Спрашивать",
            "Ask", "Запитувати", "Nachfragen", "Chiedi", "Preguntar", "Demander", "Perguntar",
            "السؤال", "पूछें", "জিজ্ঞাসা করুন", "پوچھیں", "询问")

        Add("Пропустить",
            "Skip", "Пропустити", "Überspringen", "Salta", "Omitir", "Ignorer", "Ignorar",
            "تخطّي", "छोड़ें", "এড়িয়ে যান", "چھوڑ دیں", "跳过")

        Add("Сохранить оба",
            "Keep both", "Зберегти обидва", "Beide behalten", "Mantieni entrambi",
            "Conservar ambos", "Conserver les deux", "Manter ambos", "الاحتفاظ بالاثنين",
            "दोनों रखें", "উভয়ই রাখুন", "دونوں رکھیں", "两个都保留")

        Add("Заменить",
            "Replace", "Замінити", "Ersetzen", "Sostituisci", "Reemplazar", "Remplacer",
            "Substituir", "استبدال", "बदलें", "প্রতিস্থাপন করুন", "تبدیل کریں", "替换")

        ' --- when to ask before deleting (R-1 Ф4) ---------------------------------
        '
        ' The middle option names the OUTCOME rather than the setting ("only when the file
        ' will not go to the Recycle Bin", not "only permanent ones"): what the user is
        ' choosing between is which deletions are worth stopping for, and the answer is the
        ' ones that cannot be taken back.

        Add("Всегда",
            "Always", "Завжди", "Immer", "Sempre", "Siempre", "Toujours", "Sempre",
            "دائمًا", "हमेशा", "সর্বদা", "ہمیشہ", "总是")

        Add("Только если файл не попадёт в Корзину",
            "Only when the file will not go to the Recycle Bin",
            "Тільки якщо файл не потрапить до Кошика",
            "Nur wenn die Datei nicht in den Papierkorb geht",
            "Solo se il file non finirà nel Cestino",
            "Solo si el archivo no irá a la Papelera",
            "Seulement si le fichier n'ira pas à la Corbeille",
            "Só quando o ficheiro não for para a Reciclagem",
            "فقط إذا لم ينتقل الملف إلى سلة المحذوفات",
            "केवल जब फ़ाइल रीसायकल बिन में नहीं जाएगी",
            "শুধু যখন ফাইলটি রিসাইকেল বিনে যাবে না",
            "صرف جب فائل ری سائیکل بن میں نہ جائے",
            "仅当文件不会进入回收站时")

        Add("Никогда",
            "Never", "Ніколи", "Nie", "Mai", "Nunca", "Jamais", "Nunca",
            "أبدًا", "कभी नहीं", "কখনও নয়", "کبھی نہیں", "从不")

        ' --- where to go after a file operation -----------------------------------

        Add("Остаться на текущем",
            "Stay on the current file", "Залишитися на поточному", "Bei der aktuellen Datei bleiben",
            "Resta sul file corrente", "Quedarse en el archivo actual",
            "Rester sur le fichier actuel", "Ficar no ficheiro atual",
            "البقاء على الملف الحالي", "वर्तमान फ़ाइल पर रहें", "বর্তমান ফাইলেই থাকুন",
            "موجودہ فائل پر رہیں", "停留在当前文件")

        Add("Закрыть, если файлов не осталось",
            "Close when no files are left", "Закрити, якщо файлів не лишилося",
            "Schließen, wenn keine Dateien mehr da sind", "Chiudi se non restano file",
            "Cerrar si no quedan archivos", "Fermer s'il ne reste aucun fichier",
            "Fechar se não restarem ficheiros", "الإغلاق إذا لم تبقَ ملفات",
            "यदि कोई फ़ाइल न बचे तो बंद करें", "কোনো ফাইল না থাকলে বন্ধ করুন",
            "اگر کوئی فائل باقی نہ رہے تو بند کریں", "没有文件时关闭")

        ' --- what to open at startup ----------------------------------------------

        Add("Стартовую страницу",
            "The start page", "Стартову сторінку", "Die Startseite", "La pagina iniziale",
            "La página de inicio", "La page de démarrage", "A página inicial",
            "صفحة البدء", "प्रारंभ पृष्ठ", "শুরুর পাতা", "ابتدائی صفحہ", "起始页")

        Add("Последнюю папку",
            "The last folder", "Останню теку", "Den letzten Ordner", "L'ultima cartella",
            "La última carpeta", "Le dernier dossier", "A última pasta",
            "آخر مجلد", "अंतिम फ़ोल्डर", "শেষ ফোল্ডার", "آخری فولڈر", "上次的文件夹")

        Add("Последний файл",
            "The last file", "Останній файл", "Die letzte Datei", "L'ultimo file",
            "El último archivo", "Le dernier fichier", "O último ficheiro",
            "آخر ملف", "अंतिम फ़ाइल", "শেষ ফাইল", "آخری فائل", "上次的文件")

    End Sub

End Class
