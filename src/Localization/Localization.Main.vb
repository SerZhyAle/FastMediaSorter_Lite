Option Strict On

' <summary>
' Strings of the main window: toolbar, tooltips, menus, status line, file
' operations, the image panel. Keys are the Russian source text - see
' Localization.vb.
'
' Argument order after the key: en, uk, de, it, es, fr, pt, ar, hi, bn, ur, zh.
'
' RU/UK/EN are proofread by the author; the other ten are machine translations
' and are not proofread - the same honesty note the app, the README and the
' site carry.
' </summary>
Partial Public NotInheritable Class Localization

    Private Shared Sub AddMainStrings()

        ' --- Main_Form.vb ------------------------------------------------------
        Add("Выберите папку с медиафайлами..",
            "Set folder of media files..", "Виберіть теку з медіафайлами..", "Ordner mit Mediendateien wählen..",
            "Scegli la cartella dei file multimediali..", "Elija la carpeta con archivos multimedia..",
            "Choisissez le dossier des fichiers multimédias..", "Escolha a pasta com arquivos de mídia..",
            "اختر مجلد ملفات الوسائط..", "मीडिया फ़ाइलों का फ़ोल्डर चुनें..", "মিডিয়া ফাইলের ফোল্ডার নির্বাচন করুন..",
            "میڈیا فائلوں کا فولڈر منتخب کریں..", "选择媒体文件夹..")
        Add("выбрана папка",
            "folder selected", "теку вибрано", "Ordner gewählt", "cartella selezionata", "carpeta seleccionada",
            "dossier sélectionné", "pasta selecionada", "تم اختيار المجلد", "फ़ोल्डर चुना गया",
            "ফোল্ডার নির্বাচিত", "فولڈر منتخب ہوا", "已选择文件夹")
        Add("Нет недавних файлов.",
            "No recent files.", "Немає нещодавніх файлів.", "Keine zuletzt geöffneten Dateien.",
            "Nessun file recente.", "No hay archivos recientes.", "Aucun fichier récent.",
            "Nenhum arquivo recente.", "لا توجد ملفات حديثة.", "कोई हाल की फ़ाइल नहीं।",
            "সাম্প্রতিক কোনো ফাইল নেই।", "کوئی حالیہ فائل نہیں۔", "没有最近的文件。")
        Add("Имя папки скопировано в буфер",
            "Folder sent to clipboard", "Шлях до теки скопійовано", "Ordnerpfad in die Zwischenablage kopiert",
            "Percorso cartella copiato negli appunti", "Ruta de la carpeta copiada al portapapeles",
            "Chemin du dossier copié dans le presse-papiers", "Caminho da pasta copiado",
            "تم نسخ مسار المجلد", "फ़ोल्डर पथ क्लिपबोर्ड पर कॉपी हुआ", "ফোল্ডার পাথ ক্লিপবোর্ডে কপি হয়েছে",
            "فولڈر کا راستہ کلپ بورڈ پر نقل ہوا", "文件夹路径已复制到剪贴板")
        Add("Выберите медиафайл",
            "Select a media file", "Виберіть медіафайл", "Mediendatei wählen", "Seleziona un file multimediale",
            "Seleccione un archivo multimedia", "Sélectionnez un fichier multimédia", "Selecione um arquivo de mídia",
            "اختر ملف وسائط", "मीडिया फ़ाइल चुनें", "একটি মিডিয়া ফাইল নির্বাচন করুন",
            "میڈیا فائل منتخب کریں", "选择媒体文件")
        Add("Введите номер файла:",
            "Enter file number:", "Введіть номер файлу:", "Dateinummer eingeben:", "Inserisci il numero del file:",
            "Introduzca el número de archivo:", "Saisissez le numéro du fichier :", "Digite o número do arquivo:",
            "أدخل رقم الملف:", "फ़ाइल संख्या दर्ज करें:", "ফাইল নম্বর লিখুন:", "فائل نمبر درج کریں:", "输入文件编号：")
        Add("Перейти к файлу",
            "Jump To File Number", "Перейти до файлу", "Zu Datei springen", "Vai al file",
            "Ir al archivo", "Aller au fichier", "Ir para o arquivo", "الانتقال إلى الملف",
            "फ़ाइल पर जाएँ", "ফাইলে যান", "فائل پر جائیں", "跳转到文件")
        Add("Номер файла вне диапазона.",
            "File number out of range.", "Номер файлу поза діапазоном.", "Dateinummer außerhalb des Bereichs.",
            "Numero di file fuori intervallo.", "Número de archivo fuera de rango.",
            "Numéro de fichier hors limites.", "Número de arquivo fora do intervalo.",
            "رقم الملف خارج النطاق.", "फ़ाइल संख्या सीमा से बाहर है।", "ফাইল নম্বর সীমার বাইরে।",
            "فائل نمبر حد سے باہر ہے۔", "文件编号超出范围。")
        Add("Неверный номер файла.",
            "Invalid file number.", "Хибний номер файлу.", "Ungültige Dateinummer.", "Numero di file non valido.",
            "Número de archivo no válido.", "Numéro de fichier invalide.", "Número de arquivo inválido.",
            "رقم ملف غير صالح.", "अमान्य फ़ाइल संख्या।", "অবৈধ ফাইল নম্বর।", "غلط فائل نمبر۔", "文件编号无效。")

        ' --- Main_Form.Lifecycle.vb: toolbar tooltips --------------------------
        Add("Выбрать папку с медиафайлами",
            "Select a folder with media files", "Вибрати теку з медіафайлами", "Ordner mit Mediendateien wählen",
            "Scegli una cartella con file multimediali", "Seleccionar una carpeta con archivos multimedia",
            "Choisir un dossier de fichiers multimédias", "Selecionar uma pasta com arquivos de mídia",
            "اختيار مجلد به ملفات وسائط", "मीडिया फ़ाइलों वाला फ़ोल्डर चुनें",
            "মিডিয়া ফাইলসহ ফোল্ডার নির্বাচন করুন", "میڈیا فائلوں والا فولڈر منتخب کریں", "选择包含媒体文件的文件夹")
        Add("Перечитать текущую папку - вдруг там что-то поменялось за вашей спиной.",
            "Reload the current folder - in case something changed behind your back.",
            "Перечитати поточну теку - раптом там щось змінилося за вашою спиною.",
            "Aktuellen Ordner neu einlesen - falls sich hinter Ihrem Rücken etwas geändert hat.",
            "Rileggi la cartella corrente - nel caso sia cambiato qualcosa alle tue spalle.",
            "Volver a leer la carpeta actual, por si algo cambió a sus espaldas.",
            "Relire le dossier courant - au cas où quelque chose aurait changé dans votre dos.",
            "Reler a pasta atual - caso algo tenha mudado sem você ver.",
            "إعادة قراءة المجلد الحالي - في حال تغيّر شيء دون علمك.",
            "मौजूदा फ़ोल्डर दोबारा पढ़ें - शायद पीछे से कुछ बदल गया हो।",
            "বর্তমান ফোল্ডার আবার পড়ুন - হয়তো আড়ালে কিছু বদলেছে।",
            "موجودہ فولڈر دوبارہ پڑھیں - شاید پیچھے سے کچھ بدل گیا ہو۔",
            "重新读取当前文件夹 - 以防有东西在你背后变了。")
        Add("Показать панель изображений (F3)",
            "Show the image panel (F3)", "Показати панель зображень (F3)", "Bildleiste anzeigen (F3)",
            "Mostra il pannello immagini (F3)", "Mostrar el panel de imágenes (F3)",
            "Afficher le panneau d'images (F3)", "Mostrar o painel de imagens (F3)",
            "إظهار لوحة الصور (F3)", "छवि पैनल दिखाएँ (F3)", "ছবির প্যানেল দেখান (F3)",
            "تصویری پینل دکھائیں (F3)", "显示图片面板 (F3)")
        Add("Полноэкранный режим - картинка во весь экран и ничего лишнего.",
            "Toggle fullscreen mode - the image, the whole image, and nothing but the image.",
            "Повноекранний режим - зображення на весь екран і нічого зайвого.",
            "Vollbildmodus - das Bild, das ganze Bild und nichts als das Bild.",
            "Modalità a schermo intero - l'immagine, tutta l'immagine e nient'altro.",
            "Modo de pantalla completa: la imagen, toda la imagen y nada más que la imagen.",
            "Mode plein écran - l'image, toute l'image, rien que l'image.",
            "Modo tela cheia - a imagem, toda a imagem e nada além da imagem.",
            "وضع ملء الشاشة - الصورة، كل الصورة، ولا شيء غيرها.",
            "पूर्ण-स्क्रीन मोड - बस छवि, पूरी छवि और कुछ नहीं।",
            "পূর্ণ-স্ক্রিন মোড - ছবি, পুরো ছবি, আর কিছু নয়।",
            "فل اسکرین موڈ - تصویر، پوری تصویر، اور کچھ نہیں۔",
            "全屏模式 - 只有图片，整张图片，别无其他。")
        Add("Предыдущий файл (Стрелка влево, PgUp)",
            "Previous file (Left Arrow, PgUp)", "Попередній файл (Стрілка вліво, PgUp)",
            "Vorherige Datei (Pfeil links, Bild auf)", "File precedente (freccia sinistra, PgSu)",
            "Archivo anterior (flecha izquierda, RePág)", "Fichier précédent (flèche gauche, Page préc.)",
            "Arquivo anterior (seta esquerda, PgUp)", "الملف السابق (سهم لليسار، PgUp)",
            "पिछली फ़ाइल (बायाँ तीर, PgUp)", "পূর্ববর্তী ফাইল (বাম তীর, PgUp)",
            "پچھلی فائل (بایاں تیر، PgUp)", "上一个文件（左箭头、PgUp）")
        Add("Следующий файл (Стрелка вправо, PgDn)",
            "Next file (Right Arrow, PgDn)", "Наступний файл (Стрілка вправо, PgDn)",
            "Nächste Datei (Pfeil rechts, Bild ab)", "File successivo (freccia destra, PgGiù)",
            "Archivo siguiente (flecha derecha, AvPág)", "Fichier suivant (flèche droite, Page suiv.)",
            "Próximo arquivo (seta direita, PgDn)", "الملف التالي (سهم لليمين، PgDn)",
            "अगली फ़ाइल (दायाँ तीर, PgDn)", "পরবর্তী ফাইল (ডান তীর, PgDn)",
            "اگلی فائل (دایاں تیر، PgDn)", "下一个文件（右箭头、PgDn）")
        Add("Случайный файл (Y) - пусть судьба выбирает за вас.",
            "Random file (Y) - let fate pick for you.", "Випадковий файл (Y) - хай доля вибирає за вас.",
            "Zufällige Datei (Y) - lassen Sie das Schicksal wählen.",
            "File casuale (Y) - lascia scegliere al destino.", "Archivo aleatorio (Y): deje que el destino elija.",
            "Fichier aléatoire (Y) - laissez le hasard choisir.", "Arquivo aleatório (Y) - deixe o destino escolher.",
            "ملف عشوائي (Y) - دع القدر يختار.", "यादृच्छिक फ़ाइल (Y) - चुनाव किस्मत पर छोड़ दें।",
            "এলোমেলো ফাইল (Y) - ভাগ্যকেই বাছতে দিন।", "بے ترتیب فائل (Y) - قسمت کو چننے دیں۔",
            "随机文件 (Y) - 交给运气来选。")
        Add("Случайное слайд-шоу (I, F5)",
            "Random slideshow (I, F5)", "Випадкове слайд-шоу (I, F5)", "Zufällige Diashow (I, F5)",
            "Presentazione casuale (I, F5)", "Pase de diapositivas aleatorio (I, F5)",
            "Diaporama aléatoire (I, F5)", "Apresentação aleatória (I, F5)", "عرض شرائح عشوائي (I, F5)",
            "यादृच्छिक स्लाइडशो (I, F5)", "এলোমেলো স্লাইডশো (I, F5)", "بے ترتیب سلائیڈ شو (I, F5)",
            "随机幻灯片 (I, F5)")
        Add("Слайд-шоу (S)",
            "Slideshow (S)", "Слайд-шоу (S)", "Diashow (S)", "Presentazione (S)", "Pase de diapositivas (S)",
            "Diaporama (S)", "Apresentação de slides (S)", "عرض شرائح (S)", "स्लाइडशो (S)",
            "স্লাইডশো (S)", "سلائیڈ شو (S)", "幻灯片 (S)")
        Add("Настройки: папки-получатели, OCR и перевод (F2)",
            "Settings: destination folders, OCR & translation (F2)",
            "Налаштування: теки-отримувачі, OCR і переклад (F2)",
            "Einstellungen: Zielordner, OCR und Übersetzung (F2)",
            "Impostazioni: cartelle di destinazione, OCR e traduzione (F2)",
            "Configuración: carpetas de destino, OCR y traducción (F2)",
            "Paramètres : dossiers de destination, OCR et traduction (F2)",
            "Configurações: pastas de destino, OCR e tradução (F2)",
            "الإعدادات: مجلدات الوجهة وOCR والترجمة (F2)",
            "सेटिंग्स: गंतव्य फ़ोल्डर, OCR और अनुवाद (F2)",
            "সেটিংস: গন্তব্য ফোল্ডার, OCR ও অনুবাদ (F2)",
            "ترتیبات: منزل فولڈرز، OCR اور ترجمہ (F2)",
            "设置：目标文件夹、OCR 与翻译 (F2)")
        Add("Переименовать файл (F6)",
            "Rename file (F6)", "Перейменувати файл (F6)", "Datei umbenennen (F6)", "Rinomina file (F6)",
            "Cambiar el nombre del archivo (F6)", "Renommer le fichier (F6)", "Renomear arquivo (F6)",
            "إعادة تسمية الملف (F6)", "फ़ाइल का नाम बदलें (F6)", "ফাইলের নাম বদলান (F6)",
            "فائل کا نام بدلیں (F6)", "重命名文件 (F6)")
        Add("Удалить файл (Del) - пути назад почти нет, так что прицеливайтесь.",
            "Delete the file (Del) - there's almost no going back, so aim carefully.",
            "Видалити файл (Del) - шляху назад майже немає, тож цільтеся уважно.",
            "Datei löschen (Entf) - ein Zurück gibt es fast nicht, also zielen Sie gut.",
            "Elimina il file (Canc) - tornare indietro è quasi impossibile, quindi mira bene.",
            "Eliminar el archivo (Supr): casi no hay vuelta atrás, así que apunte bien.",
            "Supprimer le fichier (Suppr) - il n'y a presque pas de retour en arrière, visez bien.",
            "Excluir o arquivo (Del) - quase não há volta, então mire bem.",
            "حذف الملف (Del) - لا رجعة تقريبًا، فصوّب جيدًا.",
            "फ़ाइल हटाएँ (Del) - वापसी लगभग असंभव है, इसलिए ध्यान से निशाना लगाएँ।",
            "ফাইল মুছুন (Del) - ফেরার পথ প্রায় নেই, তাই সাবধানে তাক করুন।",
            "فائل حذف کریں (Del) - واپسی تقریباً ممکن نہیں، احتیاط سے نشانہ لیں۔",
            "删除文件 (Del) - 几乎无法撤销，请瞄准了再按。")
        Add("Поверх всех окон - чтобы ничто не смело его заслонить.",
            "Always on top - so nothing dares cover it.", "Поверх усіх вікон - щоб ніщо не сміло його затулити.",
            "Immer im Vordergrund - damit nichts es zu verdecken wagt.",
            "Sempre in primo piano - così nulla osa coprirlo.",
            "Siempre visible: para que nada se atreva a taparlo.",
            "Toujours au premier plan - pour que rien n'ose le masquer.",
            "Sempre no topo - para que nada ouse encobri-lo.",
            "دائمًا في المقدمة - حتى لا يجرؤ شيء على حجبه.",
            "हमेशा सबसे ऊपर - ताकि कोई इसे ढक न सके।",
            "সবসময় উপরে - যেন কিছুই একে ঢাকতে না পারে।",
            "ہمیشہ سب سے اوپر - تاکہ کوئی اسے ڈھانپ نہ سکے۔",
            "总在最前 - 谁也别想挡住它。")
        Add("Выбрать файл..",
            "Choose file..", "Вибрати файл..", "Datei wählen..", "Scegli file..", "Elegir archivo..",
            "Choisir un fichier..", "Escolher arquivo..", "اختيار ملف..", "फ़ाइल चुनें..",
            "ফাইল নির্বাচন করুন..", "فائل منتخب کریں..", "选择文件..")
        Add("Порядок сортировки файлов",
            "File sort order", "Порядок сортування файлів", "Sortierreihenfolge der Dateien",
            "Ordinamento dei file", "Orden de clasificación de archivos", "Ordre de tri des fichiers",
            "Ordem de classificação dos arquivos", "ترتيب فرز الملفات", "फ़ाइल क्रमबद्ध करने का क्रम",
            "ফাইল সাজানোর ক্রম", "فائل ترتیب دینے کی ترتیب", "文件排序方式")
        Add("Текущая папка. Введите путь и нажмите Enter для перехода.",
            "Current folder. Type a path and press Enter to navigate.",
            "Поточна тека. Введіть шлях і натисніть Enter для переходу.",
            "Aktueller Ordner. Pfad eingeben und mit Eingabetaste wechseln.",
            "Cartella corrente. Digita un percorso e premi Invio per aprirlo.",
            "Carpeta actual. Escriba una ruta y pulse Intro para ir allí.",
            "Dossier courant. Saisissez un chemin et appuyez sur Entrée.",
            "Pasta atual. Digite um caminho e pressione Enter para navegar.",
            "المجلد الحالي. اكتب مسارًا واضغط Enter للانتقال.",
            "मौजूदा फ़ोल्डर। पथ लिखें और जाने के लिए Enter दबाएँ।",
            "বর্তমান ফোল্ডার। পাথ লিখে Enter চাপুন।",
            "موجودہ فولڈر۔ راستہ لکھیں اور Enter دبائیں۔",
            "当前文件夹。输入路径后按 Enter 跳转。")
        Add("Нажмите, чтобы скопировать путь к папке (вдруг пригодится).",
            "Click to copy the folder path (just in case).",
            "Натисніть, щоб скопіювати шлях до теки (раптом знадобиться).",
            "Klicken, um den Ordnerpfad zu kopieren (man weiß ja nie).",
            "Fai clic per copiare il percorso della cartella (non si sa mai).",
            "Haga clic para copiar la ruta de la carpeta (por si acaso).",
            "Cliquez pour copier le chemin du dossier (au cas où).",
            "Clique para copiar o caminho da pasta (vai que precisa).",
            "انقر لنسخ مسار المجلد (تحسبًا).", "फ़ोल्डर पथ कॉपी करने के लिए क्लिक करें (काम आ सकता है)।",
            "ফোল্ডার পাথ কপি করতে ক্লিক করুন (কাজে লাগতে পারে)।",
            "فولڈر کا راستہ نقل کرنے کے لیے کلک کریں (کام آ سکتا ہے)۔",
            "点击复制文件夹路径（说不定用得上）。")
        Add("Нажмите, чтобы скопировать путь к файлу",
            "Click to copy the file path", "Натисніть, щоб скопіювати шлях до файлу",
            "Klicken, um den Dateipfad zu kopieren", "Fai clic per copiare il percorso del file",
            "Haga clic para copiar la ruta del archivo", "Cliquez pour copier le chemin du fichier",
            "Clique para copiar o caminho do arquivo", "انقر لنسخ مسار الملف",
            "फ़ाइल पथ कॉपी करने के लिए क्लिक करें", "ফাইল পাথ কপি করতে ক্লিক করুন",
            "فائل کا راستہ نقل کرنے کے لیے کلک کریں", "点击复制文件路径")
        Add("Статус текущей операции",
            "Status of the current operation", "Стан поточної операції", "Status des aktuellen Vorgangs",
            "Stato dell'operazione corrente", "Estado de la operación actual",
            "État de l'opération en cours", "Status da operação atual", "حالة العملية الحالية",
            "मौजूदा कार्रवाई की स्थिति", "বর্তমান কাজের অবস্থা", "موجودہ عمل کی حالت", "当前操作的状态")
        Add("Номер текущего файла и сколько их всего - чтобы оценить масштаб предстоящего.",
            "Current file number and the total - so you can grasp the scale of what's ahead.",
            "Номер поточного файлу і скільки їх усього - щоб оцінити масштаб роботи.",
            "Nummer der aktuellen Datei und die Gesamtzahl - damit Sie das Ausmaß erkennen.",
            "Numero del file corrente e totale - per capire la portata dell'impresa.",
            "Número del archivo actual y el total, para hacerse una idea de lo que viene.",
            "Numéro du fichier courant et total - pour mesurer l'ampleur de la tâche.",
            "Número do arquivo atual e o total - para avaliar o tamanho da tarefa.",
            "رقم الملف الحالي والإجمالي - لتقدير حجم ما ينتظرك.",
            "मौजूदा फ़ाइल का नंबर और कुल संख्या - ताकि आगे का अंदाज़ा हो।",
            "বর্তমান ফাইলের নম্বর ও মোট সংখ্যা - কাজের মাপ বুঝতে।",
            "موجودہ فائل کا نمبر اور کل تعداد - تاکہ کام کا اندازہ ہو۔",
            "当前文件序号与总数 - 好掂量掂量工作量。")
        Add("Недавние файлы",
            "Recent files", "Нещодавні файли", "Zuletzt verwendete Dateien", "File recenti",
            "Archivos recientes", "Fichiers récents", "Arquivos recentes", "الملفات الحديثة",
            "हाल की फ़ाइलें", "সাম্প্রতিক ফাইল", "حالیہ فائلیں", "最近的文件")
        Add("Файл всё ещё занят: ",
            "File still locked: ", "Файл усе ще зайнятий: ", "Datei weiterhin gesperrt: ",
            "File ancora bloccato: ", "El archivo sigue bloqueado: ", "Fichier toujours verrouillé : ",
            "Arquivo ainda bloqueado: ", "الملف ما زال مقفلاً: ", "फ़ाइल अब भी लॉक है: ",
            "ফাইল এখনও লক করা: ", "فائل اب بھی مقفل ہے: ", "文件仍被占用：")
        Add("Файл удалён: ",
            "File deleted: ", "Файл видалено: ", "Datei gelöscht: ", "File eliminato: ",
            "Archivo eliminado: ", "Fichier supprimé : ", "Arquivo excluído: ", "تم حذف الملف: ",
            "फ़ाइल हटा दी गई: ", "ফাইল মুছে ফেলা হয়েছে: ", "فائل حذف ہو گئی: ", "文件已删除：")
        Add("Папка недоступна: ",
            "Folder is gone: ", "Тека недоступна: ", "Ordner nicht mehr vorhanden: ",
            "Cartella non più disponibile: ", "La carpeta ya no está: ", "Le dossier a disparu : ",
            "Pasta indisponível: ", "المجلد غير متاح: ", "फ़ोल्डर उपलब्ध नहीं: ",
            "ফোল্ডার আর নেই: ", "فولڈر دستیاب نہیں: ", "文件夹已不存在：")
        Add("Режим -NoBack активен. Ожидание файла/папки.",
            "NoBack mode active. Awaiting file/folder.", "Режим -NoBack активний. Очікування файлу/теки.",
            "NoBack-Modus aktiv. Warte auf Datei/Ordner.", "Modalità NoBack attiva. In attesa di file/cartella.",
            "Modo NoBack activo. Esperando archivo o carpeta.", "Mode NoBack actif. En attente d'un fichier/dossier.",
            "Modo NoBack ativo. Aguardando arquivo/pasta.", "وضع NoBack مفعّل. في انتظار ملف/مجلد.",
            "NoBack मोड सक्रिय। फ़ाइल/फ़ोल्डर की प्रतीक्षा।", "NoBack মোড সক্রিয়। ফাইল/ফোল্ডারের অপেক্ষা।",
            "NoBack موڈ فعال۔ فائل/فولڈر کا انتظار۔", "NoBack 模式已启用。等待文件/文件夹。")
        Add("завершаются файловые операции..",
            "finishing file operations..", "завершуються файлові операції..", "Dateivorgänge werden beendet..",
            "completamento delle operazioni sui file..", "finalizando las operaciones de archivo..",
            "fin des opérations sur les fichiers..", "concluindo operações de arquivo..",
            "جارٍ إنهاء عمليات الملفات..", "फ़ाइल कार्य पूरे किए जा रहे हैं..",
            "ফাইল অপারেশন শেষ হচ্ছে..", "فائل کے عمل مکمل ہو رہے ہیں..", "正在完成文件操作..")

        ' --- toolbar overflow, folder menu -------------------------------------
        Add("Ещё - кнопки, не поместившиеся в строку.",
            "More - buttons that didn't fit on the row.", "Ще - кнопки, які не вмістилися в рядок.",
            "Mehr - Schaltflächen, die nicht in die Zeile passten.",
            "Altro - pulsanti che non stanno nella riga.", "Más: botones que no cupieron en la fila.",
            "Plus - boutons qui ne tenaient pas sur la ligne.", "Mais - botões que não couberam na linha.",
            "المزيد - أزرار لم تتسع في الصف.", "और - वे बटन जो पंक्ति में नहीं समाए।",
            "আরও - যে বোতামগুলো সারিতে ধরেনি।", "مزید - وہ بٹن جو قطار میں نہ سما سکے۔",
            "更多 - 这一行放不下的按钮。")
        Add("Выбрать папку..",
            "Select folder..", "Вибрати теку..", "Ordner wählen..", "Scegli cartella..",
            "Seleccionar carpeta..", "Choisir un dossier..", "Selecionar pasta..", "اختيار مجلد..",
            "फ़ोल्डर चुनें..", "ফোল্ডার নির্বাচন করুন..", "فولڈر منتخب کریں..", "选择文件夹..")
        Add("Открыть другую папку с медиафайлами",
            "Open another folder of media files", "Відкрити іншу теку з медіафайлами",
            "Einen anderen Ordner mit Mediendateien öffnen", "Apri un'altra cartella di file multimediali",
            "Abrir otra carpeta con archivos multimedia", "Ouvrir un autre dossier de fichiers multimédias",
            "Abrir outra pasta com arquivos de mídia", "فتح مجلد آخر به ملفات وسائط",
            "मीडिया फ़ाइलों वाला दूसरा फ़ोल्डर खोलें", "মিডিয়া ফাইলসহ অন্য ফোল্ডার খুলুন",
            "میڈیا فائلوں والا دوسرا فولڈر کھولیں", "打开另一个媒体文件夹")
        Add("Выбрать мамку..",
            "Select file..", "Вибрати файл..", "Datei wählen..", "Scegli file..", "Seleccionar archivo..",
            "Sélectionner un fichier..", "Selecionar arquivo..", "اختيار ملف..", "फ़ाइल चुनें..",
            "ফাইল নির্বাচন করুন..", "فائل منتخب کریں..", "选择文件..")
        Add("Выбрать медиафайл (F, F4)",
            "Choose a media file (F, F4)", "Вибрати медіафайл (F, F4)", "Mediendatei wählen (F, F4)",
            "Scegli un file multimediale (F, F4)", "Elegir un archivo multimedia (F, F4)",
            "Choisir un fichier multimédia (F, F4)", "Escolher um arquivo de mídia (F, F4)",
            "اختيار ملف وسائط (F, F4)", "मीडिया फ़ाइल चुनें (F, F4)", "মিডিয়া ফাইল নির্বাচন করুন (F, F4)",
            "میڈیا فائل منتخب کریں (F, F4)", "选择媒体文件 (F, F4)")
        Add("Поделиться этой папкой с Android..",
            "Share this folder with Android..", "Поділитися цією текою з Android..",
            "Diesen Ordner mit Android teilen..", "Condividi questa cartella con Android..",
            "Compartir esta carpeta con Android..", "Partager ce dossier avec Android..",
            "Compartilhar esta pasta com o Android..", "مشاركة هذا المجلد مع أندرويد..",
            "इस फ़ोल्डर को Android के साथ साझा करें..", "এই ফোল্ডার Android-এর সাথে শেয়ার করুন..",
            "یہ فولڈر Android کے ساتھ شیئر کریں..", "将此文件夹分享到 Android..")
        Add("Открыть Share Manager и раздать эту папку на Android",
            "Open the Share Manager and serve this folder to Android",
            "Відкрити Share Manager і роздати цю теку на Android",
            "Share Manager öffnen und diesen Ordner an Android freigeben",
            "Apri Share Manager e pubblica questa cartella per Android",
            "Abrir Share Manager y publicar esta carpeta para Android",
            "Ouvrir Share Manager et diffuser ce dossier vers Android",
            "Abrir o Share Manager e publicar esta pasta para o Android",
            "افتح Share Manager وشارك هذا المجلد مع أندرويد",
            "Share Manager खोलें और यह फ़ोल्डर Android को दें",
            "Share Manager খুলে এই ফোল্ডার Android-এ দিন",
            "Share Manager کھولیں اور یہ فولڈر Android کو دیں",
            "打开 Share Manager 并把此文件夹共享给 Android")

        ' --- video controls / tracks -------------------------------------------
        Add("Перемотка", "Seek", "Перемотування", "Suchlauf", "Ricerca", "Búsqueda", "Recherche",
            "Busca", "التقديم", "सीक", "সিক", "سیک", "进度")
        Add("Громкость", "Volume", "Гучність", "Lautstärke", "Volume", "Volumen", "Volume",
            "Volume", "مستوى الصوت", "ध्वनि", "ভলিউম", "آواز", "音量")
        Add("Пауза / продолжить (клик по видео - то же самое; правая кнопка - меню)",
            "Pause / resume (a click on the video does the same; right-click for the menu)",
            "Пауза / продовжити (клік по відео - те саме; права кнопка - меню)",
            "Pause / Fortsetzen (ein Klick aufs Video tut dasselbe; Rechtsklick öffnet das Menü)",
            "Pausa / riprendi (un clic sul video fa lo stesso; tasto destro per il menu)",
            "Pausar / reanudar (un clic en el vídeo hace lo mismo; clic derecho para el menú)",
            "Pause / reprise (un clic sur la vidéo fait pareil ; clic droit pour le menu)",
            "Pausar / continuar (um clique no vídeo faz o mesmo; botão direito abre o menu)",
            "إيقاف مؤقت / متابعة (النقر على الفيديو يفعل الشيء ذاته؛ الزر الأيمن للقائمة)",
            "रोकें / जारी रखें (वीडियो पर क्लिक भी यही करता है; मेन्यू के लिए दायाँ क्लिक)",
            "থামান / চালান (ভিডিওতে ক্লিক করলেও একই; মেনুর জন্য ডান ক্লিক)",
            "روکیں / جاری رکھیں (ویڈیو پر کلک بھی یہی کرتا ہے؛ مینو کے لیے دایاں کلک)",
            "暂停 / 继续（点击视频效果相同；右键打开菜单）")
        Add("Звук вкл/выкл", "Mute / unmute", "Звук увімк/вимк", "Ton ein/aus", "Attiva/disattiva audio",
            "Silenciar / activar sonido", "Couper / rétablir le son", "Ativar / desativar som",
            "كتم / إلغاء كتم الصوت", "ध्वनि चालू/बंद", "শব্দ চালু/বন্ধ", "آواز آن/آف", "静音 / 取消静音")
        Add("Пауза / продолжить", "Pause / resume", "Пауза / продовжити", "Pause / Fortsetzen",
            "Pausa / riprendi", "Pausar / reanudar", "Pause / reprise", "Pausar / continuar",
            "إيقاف مؤقت / متابعة", "रोकें / जारी रखें", "থামান / চালান", "روکیں / جاری رکھیں", "暂停 / 继续")
        Add("Дорожки", "Tracks", "Доріжки", "Spuren", "Tracce", "Pistas", "Pistes", "Faixas",
            "المسارات", "ट्रैक", "ট্র্যাক", "ٹریکس", "轨道")
        Add("Звуковые дорожки и субтитры.  A - следующая звуковая, V - следующие субтитры.",
            "Audio tracks and subtitles.  A - next audio, V - next subtitles.",
            "Звукові доріжки й субтитри.  A - наступна звукова, V - наступні субтитри.",
            "Tonspuren und Untertitel.  A - nächste Tonspur, V - nächste Untertitel.",
            "Tracce audio e sottotitoli.  A - audio successivo, V - sottotitoli successivi.",
            "Pistas de audio y subtítulos.  A: siguiente audio, V: siguientes subtítulos.",
            "Pistes audio et sous-titres.  A - audio suivant, V - sous-titres suivants.",
            "Faixas de áudio e legendas.  A - próximo áudio, V - próximas legendas.",
            "المسارات الصوتية والترجمات.  A - الصوت التالي، V - الترجمة التالية.",
            "ऑडियो ट्रैक और उपशीर्षक।  A - अगला ऑडियो, V - अगले उपशीर्षक।",
            "অডিও ট্র্যাক ও সাবটাইটেল।  A - পরবর্তী অডিও, V - পরবর্তী সাবটাইটেল।",
            "آڈیو ٹریکس اور ذیلی عنوانات۔  A - اگلا آڈیو، V - اگلے ذیلی عنوانات۔",
            "音轨与字幕。  A - 下一条音轨，V - 下一条字幕。")
        Add("Звук", "Audio", "Звук", "Audio", "Audio", "Audio", "Audio", "Áudio",
            "الصوت", "ऑडियो", "অডিও", "آڈیو", "音频")
        Add("Субтитры", "Subtitles", "Субтитри", "Untertitel", "Sottotitoli", "Subtítulos",
            "Sous-titres", "Legendas", "الترجمات", "उपशीर्षक", "সাবটাইটেল", "ذیلی عنوانات", "字幕")
        Add("Звуковая дорожка (A)", "Audio track (A)", "Звукова доріжка (A)", "Tonspur (A)",
            "Traccia audio (A)", "Pista de audio (A)", "Piste audio (A)", "Faixa de áudio (A)",
            "المسار الصوتي (A)", "ऑडियो ट्रैक (A)", "অডিও ট্র্যাক (A)", "آڈیو ٹریک (A)", "音轨 (A)")
        Add("Субтитры (V)", "Subtitles (V)", "Субтитри (V)", "Untertitel (V)", "Sottotitoli (V)",
            "Subtítulos (V)", "Sous-titres (V)", "Legendas (V)", "الترجمات (V)", "उपशीर्षक (V)",
            "সাবটাইটেল (V)", "ذیلی عنوانات (V)", "字幕 (V)")
        Add("Отключить", "Off", "Вимкнути", "Aus", "Disattiva", "Desactivar", "Désactiver",
            "Desligar", "إيقاف", "बंद", "বন্ধ", "بند", "关闭")

        ' --- video player -------------------------------------------------------
        Add("Видео открыто во внешнем плеере",
            "Video opened in external player", "Відео відкрито у зовнішньому плеєрі",
            "Video im externen Player geöffnet", "Video aperto nel lettore esterno",
            "Vídeo abierto en el reproductor externo", "Vidéo ouverte dans le lecteur externe",
            "Vídeo aberto no player externo", "تم فتح الفيديو في مشغّل خارجي",
            "वीडियो बाहरी प्लेयर में खुला", "ভিডিও বাইরের প্লেয়ারে খোলা হয়েছে",
            "ویڈیو بیرونی پلیئر میں کھلا", "视频已在外部播放器中打开")
        Add("Нажмите стрелки для перехода к следующему файлу",
            "Use arrow keys to navigate to next file", "Натисніть стрілки для переходу до наступного файлу",
            "Mit den Pfeiltasten zur nächsten Datei", "Usa le frecce per passare al file successivo",
            "Use las flechas para ir al archivo siguiente", "Utilisez les flèches pour aller au fichier suivant",
            "Use as setas para ir ao próximo arquivo", "استخدم الأسهم للانتقال إلى الملف التالي",
            "अगली फ़ाइल के लिए तीर कुंजियाँ दबाएँ", "পরের ফাইলে যেতে তীর কী ব্যবহার করুন",
            "اگلی فائل کے لیے تیر کی کلیدیں دبائیں", "按方向键切换到下一个文件")
        Add("Установка поддержки VLC..",
            "Installing VLC support..", "Встановлення підтримки VLC..", "VLC-Unterstützung wird installiert..",
            "Installazione del supporto VLC..", "Instalando la compatibilidad con VLC..",
            "Installation de la prise en charge VLC..", "Instalando o suporte a VLC..",
            "جارٍ تثبيت دعم VLC..", "VLC सहायता स्थापित की जा रही है..",
            "VLC সাপোর্ট ইনস্টল হচ্ছে..", "VLC سپورٹ انسٹال ہو رہی ہے..", "正在安装 VLC 支持..")
        Add("Ваш браузер не поддерживает видео.",
            "Your browser does not support video.", "Ваш браузер не підтримує відео.",
            "Ihr Browser unterstützt kein Video.", "Il tuo browser non supporta i video.",
            "Su navegador no admite vídeo.", "Votre navigateur ne prend pas en charge la vidéo.",
            "Seu navegador não suporta vídeo.", "متصفحك لا يدعم الفيديو.",
            "आपका ब्राउज़र वीडियो का समर्थन नहीं करता।", "আপনার ব্রাউজার ভিডিও সমর্থন করে না।",
            "آپ کا براؤزر ویڈیو کی حمایت نہیں کرتا۔", "您的浏览器不支持视频。")

        AddMediaStrings()
        AddFileStrings()
    End Sub

End Class
