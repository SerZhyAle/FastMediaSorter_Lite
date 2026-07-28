Option Strict On

' <summary>
' Context menus on the media surfaces, the toolbar overflow ("»") menu and the short
' status-bar hints.
'
' These were missed by the first migration pass: each file kept ONE
' "Dim rus As Boolean = Is_Russian_Language" that covered a whole Select Case, so the
' legacy-reads budget looked healthy while dozens of strings never reached the layer at
' all. LocalizationCoverageTests now counts strings, not reads.
'
' Columns after the Russian key: en, uk, de, it, es, fr, pt, ar, hi, bn, ur, zh.
' </summary>
Partial Public NotInheritable Class Localization

    Private Shared Sub AddMenuStrings()

        ' --- image context menu ----------------------------------------------------

        Add("Повернуть по часовой (R)",
            "Rotate clockwise (R)", "Повернути за годинниковою (R)", "Im Uhrzeigersinn drehen (R)",
            "Ruota in senso orario (R)", "Girar a la derecha (R)", "Pivoter dans le sens horaire (R)",
            "Rodar para a direita (R)", "تدوير باتجاه عقارب الساعة (R)",
            "दक्षिणावर्त घुमाएँ (R)", "ঘড়ির কাঁটার দিকে ঘোরান (R)", "گھڑی وار گھمائیں (R)", "顺时针旋转 (R)")

        Add("Повернуть против часовой (Shift+R)",
            "Rotate counter-clockwise (Shift+R)", "Повернути проти годинникової (Shift+R)",
            "Gegen den Uhrzeigersinn drehen (Shift+R)", "Ruota in senso antiorario (Shift+R)",
            "Girar a la izquierda (Shift+R)", "Pivoter dans le sens antihoraire (Shift+R)",
            "Rodar para a esquerda (Shift+R)", "تدوير عكس عقارب الساعة (Shift+R)",
            "वामावर्त घुमाएँ (Shift+R)", "ঘড়ির কাঁটার বিপরীতে ঘোরান (Shift+R)",
            "گھڑی کے مخالف گھمائیں (Shift+R)", "逆时针旋转 (Shift+R)")

        Add("Вписать в окно (серый /)",
            "Fit to window (grey /)", "Вписати у вікно (сірий /)", "In Fenster einpassen (graues /)",
            "Adatta alla finestra (/ grigio)", "Ajustar a la ventana (/ gris)",
            "Ajuster à la fenêtre (/ gris)", "Ajustar à janela (/ cinzento)",
            "ملاءمة النافذة (‎/‎ الرمادية)", "विंडो में फ़िट करें (धूसर /)",
            "উইন্ডোতে ফিট করুন (ধূসর /)", "ونڈو میں فٹ کریں (سرمئی /)", "适应窗口（灰色 /）")

        Add("Реальный размер, 100 % (серый *)",
            "Actual size, 100 % (grey *)", "Реальний розмір, 100 % (сірий *)",
            "Originalgröße, 100 % (graues *)", "Dimensioni reali, 100 % (* grigio)",
            "Tamaño real, 100 % (* gris)", "Taille réelle, 100 % (* gris)",
            "Tamanho real, 100 % (* cinzento)", "الحجم الفعلي، 100 % (‎*‎ الرمادية)",
            "वास्तविक आकार, 100 % (धूसर *)", "প্রকৃত আকার, ১০০ % (ধূসর *)",
            "اصل سائز، 100 % (سرمئی *)", "实际大小，100 %（灰色 *）")

        Add("Перевести текст на картинке (T)",
            "Translate text on the picture (T)", "Перекласти текст на зображенні (T)",
            "Text im Bild übersetzen (T)", "Traduci il testo nell'immagine (T)",
            "Traducir el texto de la imagen (T)", "Traduire le texte de l'image (T)",
            "Traduzir o texto da imagem (T)", "ترجمة النص في الصورة (T)",
            "चित्र पर लिखा पाठ अनुवाद करें (T)", "ছবির লেখা অনুবাদ করুন (T)",
            "تصویر پر موجود متن کا ترجمہ کریں (T)", "翻译图片上的文字 (T)")

        Add("Переводить автоматически (Shift+T)",
            "Translate automatically (Shift+T)", "Перекладати автоматично (Shift+T)",
            "Automatisch übersetzen (Shift+T)", "Traduci automaticamente (Shift+T)",
            "Traducir automáticamente (Shift+T)", "Traduire automatiquement (Shift+T)",
            "Traduzir automaticamente (Shift+T)", "الترجمة تلقائيًا (Shift+T)",
            "स्वतः अनुवाद करें (Shift+T)", "স্বয়ংক্রিয়ভাবে অনুবাদ করুন (Shift+T)",
            "خودکار ترجمہ کریں (Shift+T)", "自动翻译 (Shift+T)")

        Add("Копировать путь к файлу",
            "Copy the file path", "Копіювати шлях до файлу", "Dateipfad kopieren",
            "Copia il percorso del file", "Copiar la ruta del archivo",
            "Copier le chemin du fichier", "Copiar o caminho do ficheiro",
            "نسخ مسار الملف", "फ़ाइल पथ कॉपी करें", "ফাইলের পথ কপি করুন",
            "فائل کا راستہ کاپی کریں", "复制文件路径")

        ' --- media context menu ----------------------------------------------------

        Add("Копировать в",
            "Copy to", "Копіювати до", "Kopieren nach", "Copia in", "Copiar en", "Copier vers",
            "Copiar para", "نسخ إلى", "यहाँ कॉपी करें", "এখানে কপি করুন", "یہاں کاپی کریں", "复制到")

        Add("Переместить в",
            "Move to", "Перемістити до", "Verschieben nach", "Sposta in", "Mover a", "Déplacer vers",
            "Mover para", "نقل إلى", "यहाँ ले जाएँ", "এখানে সরান", "یہاں منتقل کریں", "移动到")

        Add("Переименовать.. (F6)",
            "Rename.. (F6)", "Перейменувати.. (F6)", "Umbenennen.. (F6)", "Rinomina.. (F6)",
            "Renombrar.. (F6)", "Renommer.. (F6)", "Mudar o nome.. (F6)",
            "إعادة التسمية.. (F6)", "नाम बदलें.. (F6)", "নাম পরিবর্তন.. (F6)",
            "نام تبدیل کریں.. (F6)", "重命名.. (F6)")

        Add("Удалить (Del)",
            "Delete (Del)", "Видалити (Del)", "Löschen (Entf)", "Elimina (Canc)", "Eliminar (Supr)",
            "Supprimer (Suppr)", "Eliminar (Del)", "حذف (Del)", "हटाएँ (Del)", "মুছুন (Del)",
            "حذف کریں (Del)", "删除 (Del)")

        Add("Полный экран (F7)",
            "Full screen (F7)", "Повний екран (F7)", "Vollbild (F7)", "Schermo intero (F7)",
            "Pantalla completa (F7)", "Plein écran (F7)", "Ecrã inteiro (F7)",
            "ملء الشاشة (F7)", "पूर्ण स्क्रीन (F7)", "পূর্ণ স্ক্রিন (F7)", "پوری اسکرین (F7)", "全屏 (F7)")

        Add("Полный экран без панелей (F11)",
            "Full screen, no panels (F11)", "Повний екран без панелей (F11)",
            "Vollbild ohne Leisten (F11)", "Schermo intero senza barre (F11)",
            "Pantalla completa sin barras (F11)", "Plein écran sans barres (F11)",
            "Ecrã inteiro sem barras (F11)", "ملء الشاشة بدون أشرطة (F11)",
            "बिना पट्टियों के पूर्ण स्क्रीन (F11)", "প্যানেল ছাড়া পূর্ণ স্ক্রিন (F11)",
            "پٹیوں کے بغیر پوری اسکرین (F11)", "全屏，无面板 (F11)")

        Add("Следующий файл (Space)",
            "Next file (Space)", "Наступний файл (Space)", "Nächste Datei (Leertaste)",
            "File successivo (Spazio)", "Archivo siguiente (Espacio)", "Fichier suivant (Espace)",
            "Ficheiro seguinte (Espaço)", "الملف التالي (Space)", "अगली फ़ाइल (Space)",
            "পরবর্তী ফাইল (Space)", "اگلی فائل (Space)", "下一个文件（空格）")

        Add("Предыдущий файл (B)",
            "Previous file (B)", "Попередній файл (B)", "Vorherige Datei (B)",
            "File precedente (B)", "Archivo anterior (B)", "Fichier précédent (B)",
            "Ficheiro anterior (B)", "الملف السابق (B)", "पिछली फ़ाइल (B)",
            "পূর্ববর্তী ফাইল (B)", "پچھلی فائل (B)", "上一个文件 (B)")

        ' --- video context menu ----------------------------------------------------

        Add("Пауза",
            "Pause", "Пауза", "Pause", "Pausa", "Pausa", "Pause", "Pausa",
            "إيقاف مؤقت", "रोकें", "বিরতি", "وقفہ", "暂停")

        Add("Продолжить",
            "Resume", "Продовжити", "Fortsetzen", "Riprendi", "Reanudar", "Reprendre",
            "Retomar", "متابعة", "जारी रखें", "চালিয়ে যান", "جاری رکھیں", "继续")

        Add("Без звука",
            "Mute", "Без звуку", "Stumm", "Muto", "Silenciar", "Muet", "Sem som",
            "كتم الصوت", "मौन", "নিঃশব্দ", "خاموش", "静音")

        Add("Повторять",
            "Repeat", "Повторювати", "Wiederholen", "Ripeti", "Repetir", "Répéter", "Repetir",
            "تكرار", "दोहराएँ", "পুনরাবৃত্তি", "دہرائیں", "循环")

        Add("Открыть во внешнем плеере",
            "Open in the default player", "Відкрити у зовнішньому плеєрі",
            "Im Standard-Player öffnen", "Apri nel lettore predefinito",
            "Abrir en el reproductor predeterminado", "Ouvrir dans le lecteur par défaut",
            "Abrir no leitor predefinido", "فتح في المشغّل الافتراضي",
            "डिफ़ॉल्ट प्लेयर में खोलें", "ডিফল্ট প্লেয়ারে খুলুন",
            "ڈیفالٹ پلیئر میں کھولیں", "在默认播放器中打开")

        Add("Открыть URL..",
            "Open URL..", "Відкрити URL..", "URL öffnen..", "Apri URL..", "Abrir URL..",
            "Ouvrir une URL..", "Abrir URL..", "فتح رابط..", "URL खोलें..",
            "URL খুলুন..", "URL کھولیں..", "打开 URL..")

        Add("Повтор включён",
            "Repeat on", "Повтор увімкнено", "Wiederholung ein", "Ripetizione attiva",
            "Repetición activada", "Répétition activée", "Repetição ligada",
            "التكرار مفعّل", "दोहराव चालू", "পুনরাবৃত্তি চালু", "دہرانا آن", "循环已开启")

        Add("Повтор выключен",
            "Repeat off", "Повтор вимкнено", "Wiederholung aus", "Ripetizione disattivata",
            "Repetición desactivada", "Répétition désactivée", "Repetição desligada",
            "التكرار متوقف", "दोहराव बंद", "পুনরাবৃত্তি বন্ধ", "دہرانا بند", "循环已关闭")

        ' --- toolbar overflow ("»") -------------------------------------------------

        Add("Выбрать файл..",
            "Choose file..", "Вибрати файл..", "Datei wählen..", "Scegli file..",
            "Elegir archivo..", "Choisir un fichier..", "Escolher ficheiro..",
            "اختيار ملف..", "फ़ाइल चुनें..", "ফাইল বাছুন..", "فائل منتخب کریں..", "选择文件..")

        Add("Выбрать папку..",
            "Select folder..", "Вибрати папку..", "Ordner wählen..", "Scegli cartella..",
            "Elegir carpeta..", "Choisir un dossier..", "Escolher pasta..",
            "اختيار مجلد..", "फ़ोल्डर चुनें..", "ফোল্ডার বাছুন..", "فولڈر منتخب کریں..", "选择文件夹..")

        Add("Обновить папку",
            "Reload folder", "Оновити папку", "Ordner neu laden", "Ricarica la cartella",
            "Recargar la carpeta", "Recharger le dossier", "Recarregar a pasta",
            "إعادة تحميل المجلد", "फ़ोल्डर फिर से लोड करें", "ফোল্ডার পুনরায় লোড করুন",
            "فولڈر دوبارہ لوڈ کریں", "重新载入文件夹")

        Add("Панель изображений",
            "Image panel", "Панель зображень", "Bildleiste", "Pannello immagini",
            "Panel de imágenes", "Panneau d'images", "Painel de imagens",
            "لوحة الصور", "छवि पैनल", "ছবির প্যানেল", "تصاویر کا پینل", "图片面板")

        Add("Полный экран",
            "Fullscreen", "Повний екран", "Vollbild", "Schermo intero", "Pantalla completa",
            "Plein écran", "Ecrã inteiro", "ملء الشاشة", "पूर्ण स्क्रीन", "পূর্ণ স্ক্রিন",
            "پوری اسکرین", "全屏")

        ' Was the bilingual "Язык / Language" while there were exactly two languages.
        ' With thirteen a bilingual label helps nobody - and the button beside it already
        ' shows the current code, so the word alone is enough. Same key as the Share
        ' Manager's tray entry, deliberately.
        Add("Язык интерфейса",
            "Interface language", "Мова інтерфейсу", "Sprache der Oberfläche",
            "Lingua dell'interfaccia", "Idioma de la interfaz", "Langue de l'interface",
            "Idioma da interface", "لغة الواجهة", "इंटरफ़ेस की भाषा",
            "ইন্টারফেসের ভাষা", "انٹرفیس کی زبان", "界面语言")

        Add("Недавние файлы",
            "Recent files", "Нещодавні файли", "Zuletzt verwendete Dateien", "File recenti",
            "Archivos recientes", "Fichiers récents", "Ficheiros recentes",
            "الملفات الأخيرة", "हाल की फ़ाइलें", "সাম্প্রতিক ফাইল", "حالیہ فائلیں", "最近的文件")

        Add("Предыдущий файл",
            "Previous file", "Попередній файл", "Vorherige Datei", "File precedente",
            "Archivo anterior", "Fichier précédent", "Ficheiro anterior",
            "الملف السابق", "पिछली फ़ाइल", "পূর্ববর্তী ফাইল", "پچھلی فائل", "上一个文件")

        Add("Следующий файл",
            "Next file", "Наступний файл", "Nächste Datei", "File successivo",
            "Archivo siguiente", "Fichier suivant", "Ficheiro seguinte",
            "الملف التالي", "अगली फ़ाइल", "পরবর্তী ফাইল", "اگلی فائل", "下一个文件")

        Add("Случайный файл",
            "Random file", "Випадковий файл", "Zufällige Datei", "File casuale",
            "Archivo aleatorio", "Fichier aléatoire", "Ficheiro aleatório",
            "ملف عشوائي", "यादृच्छिक फ़ाइल", "এলোমেলো ফাইল", "بے ترتیب فائل", "随机文件")

        Add("Случайное слайд-шоу",
            "Random slideshow", "Випадкове слайд-шоу", "Zufällige Diaschau",
            "Slideshow casuale", "Pase de diapositivas aleatorio", "Diaporama aléatoire",
            "Apresentação aleatória", "عرض شرائح عشوائي", "यादृच्छिक स्लाइडशो",
            "এলোমেলো স্লাইডশো", "بے ترتیب سلائیڈ شو", "随机幻灯片")

        Add("Слайд-шоу",
            "Slideshow", "Слайд-шоу", "Diaschau", "Slideshow", "Pase de diapositivas",
            "Diaporama", "Apresentação", "عرض الشرائح", "स्लाइडशो", "স্লাইডশো",
            "سلائیڈ شو", "幻灯片")

        Add("Настройки..",
            "Settings..", "Налаштування..", "Einstellungen..", "Impostazioni..", "Ajustes..",
            "Paramètres..", "Definições..", "الإعدادات..", "सेटिंग्स..", "সেটিংস..",
            "ترتیبات..", "设置..")

        Add("Переименовать",
            "Rename", "Перейменувати", "Umbenennen", "Rinomina", "Renombrar", "Renommer",
            "Mudar o nome", "إعادة التسمية", "नाम बदलें", "নাম পরিবর্তন", "نام تبدیل کریں", "重命名")

        Add("Удалить",
            "Delete", "Видалити", "Löschen", "Elimina", "Eliminar", "Supprimer", "Eliminar",
            "حذف", "हटाएँ", "মুছুন", "حذف کریں", "删除")

        Add("Перевод в браузере",
            "Translate in browser", "Переклад у браузері", "Im Browser übersetzen",
            "Traduci nel browser", "Traducir en el navegador", "Traduire dans le navigateur",
            "Traduzir no navegador", "الترجمة في المتصفح", "ब्राउज़र में अनुवाद",
            "ব্রাউজারে অনুবাদ", "براؤزر میں ترجمہ", "在浏览器中翻译")

        ' --- status-bar hints -------------------------------------------------------
        ' The jump helpers used to take the Russian and English text side by side; they
        ' now take one key each.

        Add("+10 файлов",
            "+10 files", "+10 файлів", "+10 Dateien", "+10 file", "+10 archivos",
            "+10 fichiers", "+10 ficheiros", "+10 ملفات", "+10 फ़ाइलें", "+১০ ফাইল",
            "+10 فائلیں", "+10 个文件")

        Add("-10 файлов",
            "-10 files", "-10 файлів", "-10 Dateien", "-10 file", "-10 archivos",
            "-10 fichiers", "-10 ficheiros", "-10 ملفات", "-10 फ़ाइलें", "-১০ ফাইল",
            "-10 فائلیں", "-10 个文件")

        Add("+100 файлов",
            "+100 files", "+100 файлів", "+100 Dateien", "+100 file", "+100 archivos",
            "+100 fichiers", "+100 ficheiros", "+100 ملف", "+100 फ़ाइलें", "+১০০ ফাইল",
            "+100 فائلیں", "+100 个文件")

        Add("-100 файлов",
            "-100 files", "-100 файлів", "-100 Dateien", "-100 file", "-100 archivos",
            "-100 fichiers", "-100 ficheiros", "-100 ملف", "-100 फ़ाइलें", "-১০০ ফাইল",
            "-100 فائلیں", "-100 个文件")

        Add("+1000 файлов",
            "+1000 files", "+1000 файлів", "+1000 Dateien", "+1000 file", "+1000 archivos",
            "+1000 fichiers", "+1000 ficheiros", "+1000 ملف", "+1000 फ़ाइलें", "+১০০০ ফাইল",
            "+1000 فائلیں", "+1000 个文件")

        Add("-1000 файлов",
            "-1000 files", "-1000 файлів", "-1000 Dateien", "-1000 file", "-1000 archivos",
            "-1000 fichiers", "-1000 ficheiros", "-1000 ملف", "-1000 फ़ाइलें", "-১০০০ ফাইল",
            "-1000 فائلیں", "-1000 个文件")

        Add("первый файл",
            "first file", "перший файл", "erste Datei", "primo file", "primer archivo",
            "premier fichier", "primeiro ficheiro", "الملف الأول", "पहली फ़ाइल",
            "প্রথম ফাইল", "پہلی فائل", "第一个文件")

        Add("последний файл",
            "last file", "останній файл", "letzte Datei", "ultimo file", "último archivo",
            "dernier fichier", "último ficheiro", "الملف الأخير", "अंतिम फ़ाइल",
            "শেষ ফাইল", "آخری فائل", "最后一个文件")

        Add("Авто-перевод включён",
            "Auto-translate on", "Автопереклад увімкнено", "Auto-Übersetzung ein",
            "Traduzione automatica attiva", "Traducción automática activada",
            "Traduction automatique activée", "Tradução automática ligada",
            "الترجمة التلقائية مفعّلة", "स्वतः अनुवाद चालू", "স্বয়ংক্রিয় অনুবাদ চালু",
            "خودکار ترجمہ آن", "自动翻译已开启")

        Add("Авто-перевод выключен",
            "Auto-translate off", "Автопереклад вимкнено", "Auto-Übersetzung aus",
            "Traduzione automatica disattivata", "Traducción automática desactivada",
            "Traduction automatique désactivée", "Tradução automática desligada",
            "الترجمة التلقائية متوقفة", "स्वतः अनुवाद बंद", "স্বয়ংক্রিয় অনুবাদ বন্ধ",
            "خودکار ترجمہ بند", "自动翻译已关闭")

        Add("Наложение показано",
            "Overlay shown", "Накладення показано", "Überlagerung eingeblendet",
            "Sovrapposizione mostrata", "Superposición mostrada", "Superposition affichée",
            "Sobreposição mostrada", "تم عرض التراكب", "ओवरले दिखाया गया",
            "ওভারলে দেখানো হয়েছে", "اوورلے دکھایا گیا", "已显示叠加层")

        Add("Наложение скрыто",
            "Overlay hidden", "Накладення приховано", "Überlagerung ausgeblendet",
            "Sovrapposizione nascosta", "Superposición oculta", "Superposition masquée",
            "Sobreposição oculta", "تم إخفاء التراكب", "ओवरले छिपाया गया",
            "ওভারলে লুকানো হয়েছে", "اوورلے چھپایا گیا", "已隐藏叠加层")

        ' --- confirmations that carry a value ---------------------------------------

        Add("Вы уверены, что хотите безвозвратно удалить файл '{0}'? Обратно его уже не уговорить.",
            "Are you sure you want to permanently delete the file '{0}'? There's no talking it back afterwards.",
            "Ви впевнені, що хочете безповоротно видалити файл '{0}'? Назад його вже не вмовити.",
            "Möchten Sie die Datei '{0}' wirklich unwiderruflich löschen? Zurückreden lässt sie sich danach nicht mehr.",
            "Vuoi davvero eliminare definitivamente il file '{0}'? Dopo non lo convinci a tornare.",
            "¿Seguro que quieres borrar definitivamente el archivo '{0}'? Después no hay forma de convencerlo de volver.",
            "Voulez-vous vraiment supprimer définitivement le fichier '{0}' ? Impossible de le faire revenir ensuite.",
            "Tem a certeza de que quer eliminar definitivamente o ficheiro '{0}'? Depois já não há como o convencer a voltar.",
            "هل تريد بالتأكيد حذف الملف '{0}' نهائيًا؟ لن يعود بعد ذلك مهما أقنعته.",
            "क्या आप वाकई फ़ाइल '{0}' को स्थायी रूप से हटाना चाहते हैं? इसके बाद उसे मनाकर वापस नहीं लाया जा सकता।",
            "আপনি কি নিশ্চিতভাবে '{0}' ফাইলটি স্থায়ীভাবে মুছতে চান? এরপর তাকে আর ফেরানো যাবে না।",
            "کیا آپ واقعی فائل '{0}' کو مستقل طور پر حذف کرنا چاہتے ہیں؟ اس کے بعد اسے واپس نہیں لایا جا سکتا۔",
            "确定要永久删除文件「{0}」吗？之后再怎么劝也回不来了。")

        Add("Вы уверены, что хотите безвозвратно удалить {0} файл(ов)?",
            "Are you sure you want to permanently delete {0} file(s)?",
            "Ви впевнені, що хочете безповоротно видалити {0} файл(ів)?",
            "Möchten Sie {0} Datei(en) wirklich unwiderruflich löschen?",
            "Vuoi davvero eliminare definitivamente {0} file?",
            "¿Seguro que quieres borrar definitivamente {0} archivo(s)?",
            "Voulez-vous vraiment supprimer définitivement {0} fichier(s) ?",
            "Tem a certeza de que quer eliminar definitivamente {0} ficheiro(s)?",
            "هل تريد بالتأكيد حذف {0} ملف نهائيًا؟",
            "क्या आप वाकई {0} फ़ाइल(ें) स्थायी रूप से हटाना चाहते हैं?",
            "আপনি কি নিশ্চিতভাবে {0}টি ফাইল স্থায়ীভাবে মুছতে চান?",
            "کیا آپ واقعی {0} فائل(یں) مستقل طور پر حذف کرنا چاہتے ہیں؟",
            "确定要永久删除 {0} 个文件吗？")

        Add("Вы уверены, что хотите {0} {1} файл(ов) в '{2}'?",
            "Are you sure you want to {0} {1} file(s) to '{2}'?",
            "Ви впевнені, що хочете {0} {1} файл(ів) до '{2}'?",
            "Möchten Sie wirklich {1} Datei(en) nach '{2}' {0}?",
            "Vuoi davvero {0} {1} file in '{2}'?",
            "¿Seguro que quieres {0} {1} archivo(s) en '{2}'?",
            "Voulez-vous vraiment {0} {1} fichier(s) vers '{2}' ?",
            "Tem a certeza de que quer {0} {1} ficheiro(s) para '{2}'?",
            "هل تريد بالتأكيد {0} {1} ملف إلى '{2}'؟",
            "क्या आप वाकई {1} फ़ाइल(ें) '{2}' में {0} चाहते हैं?",
            "আপনি কি নিশ্চিতভাবে {1}টি ফাইল '{2}'-এ {0} চান?",
            "کیا آپ واقعی {1} فائل(یں) '{2}' میں {0} چاہتے ہیں؟",
            "确定要将 {1} 个文件{0}到「{2}」吗？")

        Add("Не удалось обработать {0}: {1}",
            "Failed to process {0}: {1}", "Не вдалося обробити {0}: {1}",
            "{0} konnte nicht verarbeitet werden: {1}", "Impossibile elaborare {0}: {1}",
            "No se pudo procesar {0}: {1}", "Impossible de traiter {0} : {1}",
            "Não foi possível processar {0}: {1}", "تعذّرت معالجة {0}: {1}",
            "{0} संसाधित नहीं हो सका: {1}", "{0} প্রক্রিয়া করা যায়নি: {1}",
            "{0} پر عمل نہ ہو سکا: {1}", "无法处理 {0}：{1}")

        Add("{0} из {1} файлов обработано.",
            "{0} of {1} files processed.", "{0} з {1} файлів оброблено.",
            "{0} von {1} Dateien verarbeitet.", "{0} di {1} file elaborati.",
            "{0} de {1} archivos procesados.", "{0} fichier(s) sur {1} traités.",
            "{0} de {1} ficheiros processados.", "تمت معالجة {0} من {1} ملفًا.",
            "{1} में से {0} फ़ाइलें संसाधित हुईं।", "{1}টির মধ্যে {0}টি ফাইল প্রক্রিয়া হয়েছে।",
            "{1} میں سے {0} فائلیں پروسیس ہوئیں۔", "已处理 {1} 个文件中的 {0} 个。")

        Add("Сначала укажите каталог с медиафайлами.. Программа хороша, но не телепат.",
            "First point me at a folder with media files.. Great app, but not a mind reader.",
            "Спершу вкажіть каталог з медіафайлами.. Програма гарна, але не телепат.",
            "Zeigen Sie mir zuerst einen Ordner mit Mediendateien.. Gutes Programm, aber kein Gedankenleser.",
            "Prima indica una cartella con file multimediali.. Ottimo programma, ma non legge nel pensiero.",
            "Primero indícame una carpeta con archivos multimedia.. Buena aplicación, pero no adivina el pensamiento.",
            "Indiquez d'abord un dossier contenant des médias.. Excellente application, mais pas télépathe.",
            "Indique primeiro uma pasta com ficheiros multimédia.. Ótima aplicação, mas não lê pensamentos.",
            "أشِر أولاً إلى مجلد يحتوي على ملفات وسائط.. البرنامج جيد، لكنه لا يقرأ الأفكار.",
            "पहले मीडिया फ़ाइलों वाला फ़ोल्डर बताइए.. कार्यक्रम अच्छा है, पर मन नहीं पढ़ सकता।",
            "প্রথমে মিডিয়া ফাইল আছে এমন একটি ফোল্ডার দেখান.. প্রোগ্রামটি ভালো, তবে মন পড়তে পারে না।",
            "پہلے میڈیا فائلوں والا فولڈر بتائیں.. پروگرام اچھا ہے، مگر ذہن نہیں پڑھ سکتا۔",
            "请先指定一个含有媒体文件的文件夹.. 程序很好，但不会读心。")

        Add("Современный просмотрщик и сортировщик фото и видео." & vbCrLf & "Версия {0}",
            "A modern photo and video viewer and sorter." & vbCrLf & "Version {0}",
            "Сучасний переглядач і сортувальник фото та відео." & vbCrLf & "Версія {0}",
            "Ein moderner Foto- und Video-Betrachter mit Sortierung." & vbCrLf & "Version {0}",
            "Un moderno visualizzatore e ordinatore di foto e video." & vbCrLf & "Versione {0}",
            "Un visor y clasificador moderno de fotos y vídeos." & vbCrLf & "Versión {0}",
            "Une visionneuse et un trieur modernes de photos et vidéos." & vbCrLf & "Version {0}",
            "Um visualizador e organizador moderno de fotos e vídeos." & vbCrLf & "Versão {0}",
            "عارض ومنظّم حديث للصور والفيديو." & vbCrLf & "الإصدار {0}",
            "फ़ोटो और वीडियो का आधुनिक दर्शक और छँटाई उपकरण।" & vbCrLf & "संस्करण {0}",
            "ছবি ও ভিডিওর আধুনিক দর্শক এবং সাজানোর সরঞ্জাম।" & vbCrLf & "সংস্করণ {0}",
            "تصاویر اور ویڈیو کا جدید ویوَر اور ترتیب دینے والا۔" & vbCrLf & "ورژن {0}",
            "现代化的照片与视频查看和整理工具。" & vbCrLf & "版本 {0}")

        ' --- optional runtimes downloaded on demand ---------------------------------

        Add("OCR не установлен",
            "OCR not installed", "OCR не встановлено", "OCR nicht installiert", "OCR non installato",
            "OCR no instalado", "OCR non installé", "OCR não instalado",
            "لم يُثبَّت OCR", "OCR स्थापित नहीं है", "OCR ইনস্টল করা নেই", "OCR انسٹال نہیں", "未安装 OCR")

        Add("VLC не установлен, открываю внешний плеер",
            "VLC not installed, opening external player", "VLC не встановлено, відкриваю зовнішній плеєр",
            "VLC nicht installiert, externer Player wird geöffnet",
            "VLC non installato, apro il lettore esterno",
            "VLC no instalado, abriendo el reproductor externo",
            "VLC non installé, ouverture du lecteur externe",
            "VLC não instalado, a abrir o leitor externo",
            "لم يُثبَّت VLC، سيتم فتح مشغّل خارجي",
            "VLC स्थापित नहीं है, बाहरी प्लेयर खोला जा रहा है",
            "VLC ইনস্টল করা নেই, বাহ্যিক প্লেয়ার খোলা হচ্ছে",
            "VLC انسٹال نہیں، بیرونی پلیئر کھولا جا رہا ہے", "未安装 VLC，正在打开外部播放器")

        Add("OCR-движок ещё не установлен. Скачать и установить его сейчас?",
            "The OCR runtime is not installed yet. Download and install it now?",
            "Рушій OCR ще не встановлено. Завантажити й встановити його зараз?",
            "Die OCR-Laufzeit ist noch nicht installiert. Jetzt herunterladen und installieren?",
            "Il runtime OCR non è ancora installato. Scaricarlo e installarlo ora?",
            "El motor de OCR aún no está instalado. ¿Descargarlo e instalarlo ahora?",
            "Le moteur OCR n'est pas encore installé. Le télécharger et l'installer maintenant ?",
            "O motor de OCR ainda não está instalado. Transferir e instalar agora?",
            "لم يُثبَّت محرك OCR بعد. هل تريد تنزيله وتثبيته الآن؟",
            "OCR इंजन अभी स्थापित नहीं है। इसे अभी डाउनलोड करके स्थापित करें?",
            "OCR ইঞ্জিন এখনও ইনস্টল করা হয়নি। এখনই ডাউনলোড করে ইনস্টল করবেন?",
            "OCR انجن ابھی انسٹال نہیں۔ کیا اسے ابھی ڈاؤن لوڈ کر کے انسٹال کریں؟",
            "尚未安装 OCR 引擎。现在下载并安装吗？")

        Add("Поддержка VLC ещё не установлена. Скачать и установить её сейчас?",
            "VLC support is not installed yet. Download and install it now?",
            "Підтримку VLC ще не встановлено. Завантажити й встановити її зараз?",
            "Die VLC-Unterstützung ist noch nicht installiert. Jetzt herunterladen und installieren?",
            "Il supporto VLC non è ancora installato. Scaricarlo e installarlo ora?",
            "La compatibilidad con VLC aún no está instalada. ¿Descargarla e instalarla ahora?",
            "La prise en charge de VLC n'est pas encore installée. La télécharger et l'installer maintenant ?",
            "O suporte a VLC ainda não está instalado. Transferir e instalar agora?",
            "لم يُثبَّت دعم VLC بعد. هل تريد تنزيله وتثبيته الآن؟",
            "VLC समर्थन अभी स्थापित नहीं है। इसे अभी डाउनलोड करके स्थापित करें?",
            "VLC সমর্থন এখনও ইনস্টল করা হয়নি। এখনই ডাউনলোড করে ইনস্টল করবেন?",
            "VLC سپورٹ ابھی انسٹال نہیں۔ کیا اسے ابھی ڈاؤن لوڈ کر کے انسٹال کریں؟",
            "尚未安装 VLC 支持。现在下载并安装吗？")

        Add("{0} требует Microsoft Visual C++ Redistributable. Скачать и тихо установить его сейчас?",
            "{0} requires the Microsoft Visual C++ Redistributable. Download and silently install it now?",
            "{0} потребує Microsoft Visual C++ Redistributable. Завантажити й тихо встановити його зараз?",
            "{0} benötigt das Microsoft Visual C++ Redistributable. Jetzt herunterladen und still installieren?",
            "{0} richiede il Microsoft Visual C++ Redistributable. Scaricarlo e installarlo in modo silenzioso ora?",
            "{0} necesita el Microsoft Visual C++ Redistributable. ¿Descargarlo e instalarlo en silencio ahora?",
            "{0} nécessite le Microsoft Visual C++ Redistributable. Le télécharger et l'installer silencieusement maintenant ?",
            "{0} requer o Microsoft Visual C++ Redistributable. Transferir e instalar em silêncio agora?",
            "يتطلب {0} حزمة Microsoft Visual C++ Redistributable. هل تريد تنزيلها وتثبيتها بصمت الآن؟",
            "{0} को Microsoft Visual C++ Redistributable चाहिए। इसे अभी डाउनलोड करके चुपचाप स्थापित करें?",
            "{0}-এর জন্য Microsoft Visual C++ Redistributable প্রয়োজন। এখনই ডাউনলোড করে নীরবে ইনস্টল করবেন?",
            "{0} کو Microsoft Visual C++ Redistributable درکار ہے۔ کیا اسے ابھی ڈاؤن لوڈ کر کے خاموشی سے انسٹال کریں؟",
            "{0} 需要 Microsoft Visual C++ Redistributable。现在下载并静默安装吗？")

        Add("Не удалось подготовить {0}.",
            "Could not prepare {0}.", "Не вдалося підготувати {0}.", "{0} konnte nicht vorbereitet werden.",
            "Impossibile preparare {0}.", "No se pudo preparar {0}.", "Impossible de préparer {0}.",
            "Não foi possível preparar {0}.", "تعذّر تجهيز {0}.", "{0} तैयार नहीं किया जा सका।",
            "{0} প্রস্তুত করা যায়নি।", "{0} تیار نہ کیا جا سکا۔", "无法准备 {0}。")

        ' Result of "register as the default viewer/player". {0} count, {1} the list.
        Add("Успешно зарегистрировано {0} форматов:" & vbCrLf & "{1}" & vbCrLf & vbCrLf & "Изменения применены для текущего пользователя.",
            "{0} formats registered:" & vbCrLf & "{1}" & vbCrLf & vbCrLf & "Changes applied for the current user.",
            "Успішно зареєстровано {0} форматів:" & vbCrLf & "{1}" & vbCrLf & vbCrLf & "Зміни застосовано для поточного користувача.",
            "{0} Formate registriert:" & vbCrLf & "{1}" & vbCrLf & vbCrLf & "Die Änderungen gelten für den aktuellen Benutzer.",
            "{0} formati registrati:" & vbCrLf & "{1}" & vbCrLf & vbCrLf & "Le modifiche valgono per l'utente corrente.",
            "{0} formatos registrados:" & vbCrLf & "{1}" & vbCrLf & vbCrLf & "Los cambios se aplican al usuario actual.",
            "{0} formats enregistrés :" & vbCrLf & "{1}" & vbCrLf & vbCrLf & "Les modifications s'appliquent à l'utilisateur actuel.",
            "{0} formatos registados:" & vbCrLf & "{1}" & vbCrLf & vbCrLf & "As alterações aplicam-se ao utilizador atual.",
            "تم تسجيل {0} صيغة:" & vbCrLf & "{1}" & vbCrLf & vbCrLf & "طُبِّقت التغييرات على المستخدم الحالي.",
            "{0} प्रारूप पंजीकृत:" & vbCrLf & "{1}" & vbCrLf & vbCrLf & "परिवर्तन वर्तमान उपयोगकर्ता पर लागू हैं।",
            "{0}টি ফরম্যাট নিবন্ধিত:" & vbCrLf & "{1}" & vbCrLf & vbCrLf & "পরিবর্তনগুলি বর্তমান ব্যবহারকারীর জন্য প্রযোজ্য।",
            "{0} فارمیٹس رجسٹر ہو گئے:" & vbCrLf & "{1}" & vbCrLf & vbCrLf & "تبدیلیاں موجودہ صارف پر لاگو ہیں۔",
            "已注册 {0} 种格式：" & vbCrLf & "{1}" & vbCrLf & vbCrLf & "更改已应用于当前用户。")

    End Sub

End Class
