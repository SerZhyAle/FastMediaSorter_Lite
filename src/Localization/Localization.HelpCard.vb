Option Strict On

' <summary>
' The strings of the F1 reference card (UX-115 and UX-116 of ticket 123): its title and footer, the six
' group titles, the fixed (non-rebindable) rows and the mouse chords. The rows that name a shortcut-catalogue
' action carry that action's own caption, which already has its translations.
'
' The card is drawn only by the .NET 10 viewer, but the table is shared - a string table costs the x86
' build nothing, and splitting it per build is how two tables drift apart (the reasoning of
' Localization.Video.vb). Never a smart double quote in a literal (CLAUDE.md): use the guillemets.
' </summary>
Partial Public NotInheritable Class Localization

    Private Shared Sub AddHelpCardStrings()

        Add("Клавиши и мышь",
            "Keys and mouse", "Клавіші та миша", "Tasten und Maus",
            "Tasti e mouse", "Teclas y ratón", "Touches et souris",
            "Teclas e mouse", "المفاتيح والفأرة", "कुंजियाँ और माउस",
            "কী ও মাউস", "کلیدیں اور ماؤس", "按键与鼠标")
        Add("Esc или щелчок - закрыть. Клавиши можно изменить в настройках.",
            "Esc or a click closes this. Keys can be changed in the settings.", "Esc або клік - закрити. Клавіші можна змінити в налаштуваннях.", "Esc oder ein Klick schließt dies. Die Tasten lassen sich in den Einstellungen ändern.",
            "Esc o un clic per chiudere. I tasti si possono cambiare nelle impostazioni.", "Esc o un clic lo cierra. Las teclas se pueden cambiar en los ajustes.", "Échap ou un clic pour fermer. Les touches se modifient dans les réglages.",
            "Esc ou um clique fecha isto. As teclas podem ser alteradas nas definições.", "اضغط Esc أو انقر للإغلاق. يمكن تغيير المفاتيح من الإعدادات.", "बंद करने के लिए Esc या क्लिक। कुंजियाँ सेटिंग्स में बदली जा सकती हैं।",
            "বন্ধ করতে Esc বা ক্লিক। কী সেটিংসে বদলানো যায়।", "بند کرنے کے لیے Esc یا کلک۔ کلیدیں سیٹنگز میں بدلی جا سکتی ہیں۔", "按 Esc 或单击即可关闭。按键可在设置中更改。")

        ' --- the six groups ("Файлы" is an existing key) ----------------------------------------
        Add("Навигация",
            "Navigation", "Навігація", "Navigation",
            "Navigazione", "Navegación", "Navigation",
            "Navegação", "التنقل", "नेविगेशन",
            "নেভিগেশন", "نیویگیشن", "导航")
        Add("Вид",
            "View", "Вигляд", "Ansicht",
            "Visualizza", "Vista", "Affichage",
            "Exibição", "العرض", "दृश्य",
            "দৃশ্য", "منظر", "视图")
        Add("Видео и аудио",
            "Video and audio", "Відео та аудіо", "Video und Audio",
            "Video e audio", "Vídeo y audio", "Vidéo et audio",
            "Vídeo e áudio", "الفيديو والصوت", "वीडियो और ऑडियो",
            "ভিডিও ও অডিও", "ویڈیو اور آڈیو", "视频和音频")
        Add("Мышь",
            "Mouse", "Миша", "Maus",
            "Mouse", "Ratón", "Souris",
            "Mouse", "الفأرة", "माउस",
            "মাউস", "ماؤس", "鼠标")
        Add("Панели и окна",
            "Panels and windows", "Панелі та вікна", "Leisten und Fenster",
            "Pannelli e finestre", "Paneles y ventanas", "Panneaux et fenêtres",
            "Painéis e janelas", "اللوحات والنوافذ", "पैनल और विंडो",
            "প্যানেল ও উইন্ডো", "پینل اور ونڈوز", "面板和窗口")

        ' --- the rows that are not catalogue actions ----------------------------------------
        Add("Переместить в папку-получатель",
            "Move to the destination folder", "Перемістити до теки-одержувача", "In den Zielordner verschieben",
            "Sposta nella cartella di destinazione", "Mover a la carpeta de destino", "Déplacer vers le dossier de destination",
            "Mover para a pasta de destino", "النقل إلى المجلد الوجهة", "गंतव्य फ़ोल्डर में ले जाएँ",
            "গন্তব্য ফোল্ডারে সরান", "منزل فولڈر میں منتقل کریں", "移动到目标文件夹")
        Add("Копировать в папку-получатель",
            "Copy to the destination folder", "Копіювати до теки-одержувача", "In den Zielordner kopieren",
            "Copia nella cartella di destinazione", "Copiar a la carpeta de destino", "Copier vers le dossier de destination",
            "Copiar para a pasta de destino", "النسخ إلى المجلد الوجهة", "गंतव्य फ़ोल्डर में कॉपी करें",
            "গন্তব্য ফোল্ডারে কপি করুন", "منزل فولڈر میں کاپی کریں", "复制到目标文件夹")
        Add("Во весь экран без панелей",
            "Full screen without panels", "На весь екран без панелей", "Vollbild ohne Leisten",
            "Schermo intero senza pannelli", "Pantalla completa sin paneles", "Plein écran sans panneaux",
            "Ecrã inteiro sem painéis", "ملء الشاشة بدون لوحات", "पैनल के बिना पूर्ण स्क्रीन",
            "প্যানেল ছাড়া পূর্ণ পর্দা", "پینل کے بغیر پوری سکرین", "无面板全屏")
        Add("Клавиатурный режим панелей (Tab и стрелки - выбор, Enter - нажать)",
            "Panel keyboard mode (Tab and arrows choose, Enter presses)", "Клавіатурний режим панелей (Tab і стрілки - вибір, Enter - натиснути)", "Tastaturmodus der Leisten (Tab und Pfeile wählen, Enter drückt)",
            "Modalità tastiera dei pannelli (Tab e frecce scelgono, Invio preme)", "Modo de teclado de los paneles (Tab y flechas eligen, Intro pulsa)", "Mode clavier des panneaux (Tab et flèches choisissent, Entrée active)",
            "Modo de teclado dos painéis (Tab e setas escolhem, Enter prime)", "وضع لوحة المفاتيح للّوحات (Tab والأسهم للاختيار وEnter للضغط)", "पैनल कीबोर्ड मोड (Tab और तीर से चुनें, Enter से दबाएँ)",
            "প্যানেল কীবোর্ড মোড (Tab ও তীর দিয়ে বাছুন, Enter চাপুন)", "پینل کی بورڈ موڈ (Tab اور تیر سے منتخب کریں، Enter دبائیں)", "面板键盘模式（Tab 和方向键选择，Enter 按下）")
        Add("Меню текущего файла",
            "Menu of the current file", "Меню поточного файлу", "Menü der aktuellen Datei",
            "Menu del file corrente", "Menú del archivo actual", "Menu du fichier actuel",
            "Menu do ficheiro atual", "قائمة الملف الحالي", "वर्तमान फ़ाइल का मेनू",
            "বর্তমান ফাইলের মেনু", "موجودہ فائل کا مینو", "当前文件的菜单")
        Add("Шаг назад: меню, справка, перевод, масштаб, слайд-шоу, полный экран, затем закрытие окна",
            "One step back: menu, help, translation, zoom, slideshow, full screen, then closing the window", "Крок назад: меню, довідка, переклад, масштаб, слайд-шоу, повний екран, потім закриття вікна", "Ein Schritt zurück: Menü, Hilfe, Übersetzung, Zoom, Diashow, Vollbild, zuletzt Fenster schließen",
            "Un passo indietro: menu, guida, traduzione, zoom, presentazione, schermo intero, infine chiusura della finestra", "Un paso atrás: menú, ayuda, traducción, zoom, presentación, pantalla completa y, al final, cerrar la ventana", "Un pas en arrière : menu, aide, traduction, zoom, diaporama, plein écran, puis fermeture de la fenêtre",
            "Um passo atrás: menu, ajuda, tradução, zoom, apresentação, ecrã inteiro e, por fim, fechar a janela", "خطوة للخلف: القائمة، المساعدة، الترجمة، التكبير، عرض الشرائح، ملء الشاشة، ثم إغلاق النافذة", "एक कदम पीछे: मेनू, सहायता, अनुवाद, ज़ूम, स्लाइड शो, पूर्ण स्क्रीन, फिर विंडो बंद",
            "এক ধাপ পিছনে: মেনু, সহায়তা, অনুবাদ, জুম, স্লাইড শো, পূর্ণ পর্দা, তারপর উইন্ডো বন্ধ", "ایک قدم پیچھے: مینو، مدد، ترجمہ، زوم، سلائیڈ شو، پوری سکرین، پھر ونڈو بند", "后退一步：菜单、帮助、翻译、缩放、幻灯片、全屏，最后关闭窗口")

        ' --- the mouse chords (UX-116): the key column and the description column ---------------
        Add("Левый клик",
            "Left click", "Лівий клік", "Linksklick",
            "Clic sinistro", "Clic izquierdo", "Clic gauche",
            "Clique esquerdo", "نقرة يسرى", "बायाँ क्लिक",
            "বাঁ ক্লিক", "بایاں کلک", "左键单击")
        Add("Правый клик",
            "Right click", "Правий клік", "Rechtsklick",
            "Clic destro", "Clic derecho", "Clic droit",
            "Clique direito", "نقرة يمنى", "दायाँ क्लिक",
            "ডান ক্লিক", "دایاں کلک", "右键单击")
        Add("Средняя кнопка",
            "Middle button", "Середня кнопка", "Mittlere Taste",
            "Tasto centrale", "Botón central", "Bouton central",
            "Botão do meio", "الزر الأوسط", "बीच का बटन",
            "মাঝের বোতাম", "درمیانی بٹن", "中键")
        Add("Колесо",
            "Wheel", "Колесо", "Mausrad",
            "Rotella", "Rueda", "Molette",
            "Roda", "العجلة", "व्हील",
            "হুইল", "وہیل", "滚轮")
        Add("Shift / Ctrl / Alt + клик",
            "Shift / Ctrl / Alt + click", "Shift / Ctrl / Alt + клік", "Shift / Strg / Alt + Klick",
            "Maiusc / Ctrl / Alt + clic", "Mayús / Ctrl / Alt + clic", "Maj / Ctrl / Alt + clic",
            "Shift / Ctrl / Alt + clique", "Shift / Ctrl / Alt + نقرة", "Shift / Ctrl / Alt + क्लिक",
            "Shift / Ctrl / Alt + ক্লিক", "Shift / Ctrl / Alt + کلک", "Shift / Ctrl / Alt + 单击")
        Add("Ctrl / Shift / Alt + колесо",
            "Ctrl / Shift / Alt + wheel", "Ctrl / Shift / Alt + колесо", "Strg / Shift / Alt + Mausrad",
            "Ctrl / Maiusc / Alt + rotella", "Ctrl / Mayús / Alt + rueda", "Ctrl / Maj / Alt + molette",
            "Ctrl / Shift / Alt + roda", "Ctrl / Shift / Alt + العجلة", "Ctrl / Shift / Alt + व्हील",
            "Ctrl / Shift / Alt + হুইল", "Ctrl / Shift / Alt + وہیل", "Ctrl / Shift / Alt + 滚轮")
        Add("Двойной клик",
            "Double click", "Подвійний клік", "Doppelklick",
            "Doppio clic", "Doble clic", "Double clic",
            "Clique duplo", "نقر مزدوج", "डबल क्लिक",
            "ডাবল ক্লিক", "ڈبل کلک", "双击")
        Add("Клик по видео",
            "Click on a video", "Клік по відео", "Klick auf ein Video",
            "Clic su un video", "Clic en un vídeo", "Clic sur une vidéo",
            "Clique num vídeo", "النقر على فيديو", "वीडियो पर क्लिक",
            "ভিডিওতে ক্লিক", "ویڈیو پر کلک", "单击视频")
        Add("Правая кнопка по видео",
            "Right button on a video", "Права кнопка по відео", "Rechte Taste auf einem Video",
            "Tasto destro su un video", "Botón derecho en un vídeo", "Bouton droit sur une vidéo",
            "Botão direito num vídeo", "الزر الأيمن على فيديو", "वीडियो पर दायाँ बटन",
            "ভিডিওতে ডান বোতাম", "ویڈیو پر دایاں بٹن", "视频上的右键")
        Add("Боковые кнопки мыши",
            "Side mouse buttons", "Бічні кнопки миші", "Seitentasten der Maus",
            "Tasti laterali del mouse", "Botones laterales del ratón", "Boutons latéraux de la souris",
            "Botões laterais do mouse", "الأزرار الجانبية للفأرة", "माउस के साइड बटन",
            "মাউসের পাশের বোতাম", "ماؤس کے سائیڈ بٹن", "鼠标侧键")
        Add("Меню картинки со всеми действиями над ней",
            "Picture menu with every action on it", "Меню картинки з усіма діями над нею", "Bildmenü mit allen Aktionen",
            "Menu dell'immagine con tutte le azioni", "Menú de la imagen con todas las acciones", "Menu de l'image avec toutes les actions",
            "Menu da imagem com todas as ações", "قائمة الصورة بكل الإجراءات", "चित्र का मेनू, सभी क्रियाओं के साथ",
            "ছবির মেনু, সব ক্রিয়াসহ", "تصویر کا مینو، تمام کارروائیوں کے ساتھ", "包含所有操作的图片菜单")
        Add("Листать файлы (в настройках колесо можно переключить на масштаб)",
            "Flip through files (the wheel can be switched to zoom in the settings)", "Гортати файли (у налаштуваннях колесо можна перемкнути на масштаб)", "Dateien blättern (in den Einstellungen lässt sich das Rad auf Zoom umstellen)",
            "Scorrere i file (nelle impostazioni la rotella può passare allo zoom)", "Pasar archivos (en los ajustes la rueda puede cambiarse a zoom)", "Parcourir les fichiers (la molette peut passer au zoom dans les réglages)",
            "Folhear ficheiros (nas definições a roda pode passar para zoom)", "تصفّح الملفات (يمكن تحويل العجلة إلى التكبير في الإعدادات)", "फ़ाइलें पलटें (सेटिंग्स में व्हील को ज़ूम पर बदला जा सकता है)",
            "ফাইল ওল্টান (সেটিংসে হুইলকে জুমে বদলানো যায়)", "فائلیں پلٹیں (سیٹنگز میں وہیل کو زوم پر بدلا جا سکتا ہے)", "翻阅文件（可在设置中将滚轮改为缩放）")
        Add("На 10 / 100 / 1000 файлов вперёд; правой кнопкой - назад",
            "10 / 100 / 1000 files forward; with the right button - back", "На 10 / 100 / 1000 файлів уперед; правою кнопкою - назад", "10 / 100 / 1000 Dateien vor; mit der rechten Taste - zurück",
            "Avanti di 10 / 100 / 1000 file; con il tasto destro - indietro", "10 / 100 / 1000 archivos adelante; con el botón derecho, atrás", "10 / 100 / 1000 fichiers en avant ; avec le bouton droit - en arrière",
            "10 / 100 / 1000 ficheiros para a frente; com o botão direito, para trás", "10 / 100 / 1000 ملف للأمام؛ وبالزر الأيمن - للخلف", "10 / 100 / 1000 फ़ाइलें आगे; दाएँ बटन से - पीछे",
            "10 / 100 / 1000 ফাইল সামনে; ডান বোতামে - পিছনে", "10 / 100 / 1000 فائلیں آگے؛ دائیں بٹن سے - پیچھے", "向前 10 / 100 / 1000 个文件；右键则向后")
        Add("Масштаб / 100 % / вписать в окно",
            "Zoom / 100 % / fit to window", "Масштаб / 100 % / вписати у вікно", "Zoom / 100 % / an Fenster anpassen",
            "Zoom / 100 % / adatta alla finestra", "Zoom / 100 % / ajustar a la ventana", "Zoom / 100 % / ajuster à la fenêtre",
            "Zoom / 100 % / ajustar à janela", "تكبير / 100 % / ملاءمة النافذة", "ज़ूम / 100 % / विंडो में फ़िट",
            "জুম / 100 % / উইন্ডোতে ফিট", "زوم / 100 % / ونڈو میں فٹ", "缩放 / 100 % / 适应窗口")
        Add("Меню видео со всеми действиями над ним",
            "Video menu with every action on it", "Меню відео з усіма діями над ним", "Videomenü mit allen Aktionen",
            "Menu del video con tutte le azioni", "Menú del vídeo con todas las acciones", "Menu de la vidéo avec toutes les actions",
            "Menu do vídeo com todas as ações", "قائمة الفيديو بكل الإجراءات", "वीडियो का मेनू, सभी क्रियाओं के साथ",
            "ভিডিওর মেনু, সব ক্রিয়াসহ", "ویڈیو کا مینو، تمام کارروائیوں کے ساتھ", "包含所有操作的视频菜单")
        Add("Следующий / предыдущий файл",
            "Next / previous file", "Наступний / попередній файл", "Nächste / vorherige Datei",
            "File successivo / precedente", "Archivo siguiente / anterior", "Fichier suivant / précédent",
            "Ficheiro seguinte / anterior", "الملف التالي / السابق", "अगली / पिछली फ़ाइल",
            "পরের / আগের ফাইল", "اگلی / پچھلی فائل", "下一个 / 上一个文件")

    End Sub

End Class
