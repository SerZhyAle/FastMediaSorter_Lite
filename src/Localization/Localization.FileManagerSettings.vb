Option Strict On

' Specification 258: File manager content settings and safe open.
' Columns: ru, en, uk, de, it, es, fr, pt, ar, hi, bn, ur, zh.
Partial Public NotInheritable Class Localization

    Private Shared Sub AddFileManagerSettingsStrings()
        Add("Настройки файлового менеджера",
            "File manager settings", "Налаштування файлового менеджера", "Dateimanager-Einstellungen",
            "Impostazioni gestione file", "Configuración del administrador de archivos",
            "Paramètres du gestionnaire de fichiers", "Configurações do gerenciador de arquivos",
            "إعدادات مدير الملفات", "फ़ाइल प्रबंधक सेटिंग्स", "ফাইল ম্যানেজার সেটিংস",
            "فائل مینیجر کی ترتیبات", "文件管理器设置")

        Add("Отображение",
            "Display", "Відображення", "Anzeige", "Visualizzazione", "Visualización",
            "Affichage", "Exibição", "العرض", "प्रदर्शन", "প্রদর্শন", "ڈسپلے", "显示")

        Add("Открытие",
            "Opening", "Відкриття", "Öffnen", "Apertura", "Apertura",
            "Ouverture", "Abertura", "الفتح", "खोलना", "খোলা", "کھولنا", "打开")

        Add("Содержимое и видимость элементов в режиме файлового менеджера",
            "Content and item visibility in file manager mode",
            "Вміст та видимість елементів у режимі файлового менеджера",
            "Inhalt und Sichtbarkeit von Elementen im Dateimanager-Modus",
            "Contenuto e visibilità degli elementi nella modalità gestione file",
            "Contenido y visibilidad de elementos en el modo de administrador de archivos",
            "Contenu et visibilité des éléments en mode gestionnaire de fichiers",
            "Conteúdo e visibilidade dos itens no modo do gerenciador de arquivos",
            "المحتوى ومرئية العناصر في وضع مدير الملفات",
            "फ़ाइल प्रबंधक मोड में सामग्री और आइटम दृश्यता",
            "ফাইল ম্যানেজার মোডে বিষয়বস্তু এবং উপাদানের দৃশ্যমানতা",
            "فائل مینیجر موڈ میں مواد اور اشیاء کی مرئیت",
            "文件管理器模式下的内容与项目可见性")

        Add("Правила открытия и запуска файлов из файлового менеджера",
            "Rules for opening and launching files from the file manager",
            "Правила відкриття та запуску файлів із файлового менеджера",
            "Regeln zum Öffnen und Starten von Dateien aus dem Dateimanager",
            "Regole per l'apertura e l'avvio dei file da gestione file",
            "Reglas para abrir y ejecutar archivos desde el administrador de archivos",
            "Règles d'ouverture et de lancement des fichiers depuis le gestionnaire de fichiers",
            "Regras para abrir e iniciar arquivos do gerenciador de arquivos",
            "قواعد فتح وتشغيل الملفات من مدير الملفات",
            "फ़ाइल प्रबंधक से फ़ाइलें खोलने और लॉन्च करने के नियम",
            "ফাইল ম্যানেজার থেকে ফাইল খোলা এবং চালুর নিয়ম",
            "فائل مینیجر سے فائلیں کھولنے اور چلانے کے قواعد",
            "从文件管理器打开和启动文件的规则")

        Add("Источники и списки расположений в боковой панели файлового менеджера",
            "Sources and location lists in the file manager sidebar",
            "Джерела та списки розташувань у бічній панелі файлового менеджера",
            "Quellen und Ortslisten in der Seitenleiste des Dateimanagers",
            "Origini ed elenchi di posizioni nella barra laterale di gestione file",
            "Fuentes y listas de ubicaciones en la barra lateral del administrador de archivos",
            "Sources et listes d'emplacements dans le volet latéral du gestionnaire de fichiers",
            "Fontes e listas de locais na barra lateral do gerenciador de arquivos",
            "المصادر وقوائم المواقع في الشريط الجانبي لمدير الملفات",
            "फ़ाइल प्रबंधक साइडबार में स्रोत और स्थान सूचियाँ",
            "ফাইল ম্যানেজার সাইডবারে উৎস এবং অবস্থানের তালিকা",
            "فائل مینیجر سائیڈ بار میں ماخذ اور مقامات کی فہرستیں",
            "文件管理器侧边栏中的源与位置列表")

        Add("Фильтр содержимого",
            "Content filter", "Фільтр вмісту", "Inhaltsfilter", "Filtro contenuti",
            "Filtro de contenido", "Filtre de contenu", "Filtro de conteúdo",
            "تصفية المحتوى", "सामग्री फ़िल्टर", "বিষয়বস্তু ফিল্টার", "مواد کا فلٹر", "内容筛选器")

        Add("Какие типы файлов показывать в списке файлов",
            "Which file types to show in the file list",
            "Які типи файлів показувати у списку файлів",
            "Welche Dateitypen in der Dateiliste angezeigt werden sollen",
            "Quali tipi di file mostrare nell'elenco dei file",
            "Qué tipos de archivos mostrar en la lista de archivos",
            "Quels types de fichiers afficher dans la liste des fichiers",
            "Quais tipos de arquivo mostrar na lista de arquivos",
            "أنواع الملفات التي سيتم إظهارها في قائمة الملفات",
            "फ़ाइल सूची में किस प्रकार की फ़ाइलें दिखाएँ",
            "ফাইল তালিকায় কোন ধরণের ফাইল প্রদর্শিত হবে",
            "فائلوں کی فہرست میں کون سی اقسام کی فائلیں دکھائی جائیں",
            "在文件列表中显示哪些文件类型")

        Add("Медиафайлы",
            "Media files", "Медіафайли", "Mediendateien", "File multimediali",
            "Archivos multimedia", "Fichiers multimédias", "Arquivos de mídia",
            "ملفات الوسائط", "मीडिया फ़ाइलें", "মিডিয়া ফাইল", "میڈیا فائلیں", "媒体文件")

        Add("Медиа и документы",
            "Media and documents", "Медіа та документи", "Medien und Dokumente",
            "File multimediali e documenti", "Multimedia y documentos", "Médias et documents",
            "Mídia e documentos", "الوسائط والمستندات", "मीडिया और दस्तावेज़",
            "মিডিয়া এবং নথি", "میڈیا اور دستاویزات", "媒体与文档")

        Add("Все файлы",
            "All files", "Всі файли", "Alle Dateien", "Tutti i file", "Todos los archivos",
            "Tous les fichiers", "Todos os arquivos", "جميع الملفات", "सभी फ़ाइलें",
            "সমস্ত ফাইল", "تمام فائلیں", "所有文件")

        Add("Показывать скрытые файлы",
            "Show hidden files", "Показувати приховані файли", "Ausgeblendete Dateien anzeigen",
            "Mostra file nascosti", "Mostrar archivos ocultos", "Afficher les fichiers masqués",
            "Mostrar arquivos ocultos", "إظهار الملفات المخفية", "छिपी हुई फ़ाइलें दिखाएँ",
            "লুকানো ফাইল দেখান", "پوشیدہ فائلیں دکھائیں", "显示隐藏文件")

        Add("Отображать скрытые и системные файлы и папки",
            "Display hidden and system files and folders",
            "Відображати приховані та системні файли й папки",
            "Ausgeblendete und Systemdateien sowie -ordner anzeigen",
            "Visualizza file e cartelle nascosti e di sistema",
            "Mostrar archivos y carpetas ocultos y del sistema",
            "Afficher les fichiers et dossiers masqués et système",
            "Exibir arquivos e pastas ocultos e do sistema",
            "عرض الملفات والمجلدات المخفية وملفات النظام",
            "छिपी हुई और सिस्टम फ़ाइलें व फ़ोल्डर प्रदर्शित करें",
            "লুকানো এবং সিস্টেম ফাইল ও ফোল্ডার প্রদর্শন করুন",
            "پوشیدہ اور سسٹم فائلیں اور فولڈرز دکھائیں",
            "显示隐藏和系统文件及文件夹")

        Add("Отображать скрытые и системные файлы и папки.",
            "Display hidden and system files and folders.",
            "Відображати приховані та системні файли й папки.",
            "Ausgeblendete und Systemdateien sowie -ordner anzeigen.",
            "Visualizza file e cartelle nascosti e di sistema.",
            "Mostrar archivos y carpetas ocultos y del sistema.",
            "Afficher les fichiers et dossiers masqués et système.",
            "Exibir arquivos e pastas ocultos e do sistema.",
            "عرض الملفات والمجلدات المخفية وملفات النظام.",
            "छिपी हुई और सिस्टम फ़ाइलें व फ़ोल्डर प्रदर्शित करें।",
            "লুকানো এবং সিস্টেম ফাইল ও ফোল্ডার প্রদর্শন করুন।",
            "پوشیدہ اور سسٹم فائلیں اور فولڈرز دکھائیں۔",
            "显示隐藏和系统文件及文件夹。")

        Add("Вид по умолчанию",
            "Default view", "Вигляд за замовчуванням", "Standardansicht",
            "Visualizzazione predefinita", "Vista predeterminada", "Affichage par défaut",
            "Exibição padrão", "العرض الافتراضي", "डिफ़ॉल्ट दृश्य", "ডিফল্ট ভিউ", "ڈیفالٹ منظر", "默认视图")

        Add("Способ отображения файлов при входе в менеджер",
            "How files are displayed when entering the manager",
            "Спосіб відображення файлів під час входу до менеджера",
            "Art der Anzeige von Dateien beim Öffnen des Managers",
            "Modalità di visualizzazione dei file all'ingresso in gestione file",
            "Forma de mostrar archivos al entrar al administrador",
            "Mode d'affichage des fichiers à l'ouverture du gestionnaire",
            "Forma de exibição dos arquivos ao entrar no gerenciador",
            "طريقة عرض الملفات عند الدخول إلى مدير الملفات",
            "प्रबंधक में प्रवेश करते समय फ़ाइलों को प्रदर्शित करने का तरीका",
            "ম্যানেজারে প্রবেশের সময় ফাইল প্রদর্শনের পদ্ধতি",
            "مینیجر میں داخل ہوتے وقت فائلیں دکھانے کا طریقہ",
            "进入管理器时显示文件的方式")

        Add("Действие для не-медиа файлов",
            "Action for non-media files", "Дія для не-медіа файлів",
            "Aktion für Nicht-Mediendateien", "Azione per file non multimediali",
            "Acción para archivos no multimedia", "Action pour les fichiers non multimédias",
            "Ação para arquivos não multimídia", "الإجراء للملفات غير الإعلامية",
            "गैर-मीडिया फ़ाइलों के लिए कार्रवाई", "অ-মিডিয়া ফাইলের জন্য পদক্ষেপ",
            "غیر میڈیا فائلوں کے لیے کارروائی", "非媒体文件的操作")

        Add("Как открывать файлы, не поддерживаемые просмотрщиком",
            "How to open files not supported by the viewer",
            "Як відкривати файли, що не підтримуються переглядачем",
            "Wie vom Betrachter nicht unterstützte Dateien geöffnet werden",
            "Come aprire i file non supportati dal visualizzatore",
            "Cómo abrir archivos no admitidos por el visor",
            "Comment ouvrir les fichiers non pris en charge par la visionneuse",
            "Como abrir arquivos não suportados pelo visualizador",
            "كيفية فتح الملفات غير المدعومة من العارض",
            "व्यूअर द्वारा असमर्थित फ़ाइलों को कैसे खोलें",
            "ভিউয়ার দ্বারা অসমর্থিত ফাইলগুলি কীভাবে খুলবেন",
            "ویور کی غیر تعاون یافتہ فائلیں کیسے کھولیں",
            "如何打开查看器不支持的文件")

        Add("Программа по умолчанию",
            "Default program", "Програма за замовчуванням", "Standardprogramm",
            "Programma predefinito", "Programa predeterminado", "Programme par défaut",
            "Programa padrão", "البرنامج الافتراضي", "डिफ़ॉल्ट प्रोग्राम", "ডিফল্ট প্রোগ্রাম",
            "ڈیفالٹ پروگرام", "默认程序")

        Add("Диалог «Открыть с помощью»",
            "«Open with» dialog", "Діалог «Відкрити за допомогою»", "Dialog «Öffnen mit»",
            "Finestra «Apri con»", "Diálogo «Abrir con»", "Dialogue « Ouvrir avec »",
            "Diálogo «Abrir com»", "مربع حوار «فتح باستخدام»", "«इसके साथ खोलें» संवाद",
            "«এর সাহায্যে খুলুন» ডায়ালগ", "«اس کے ساتھ کھولیں» ڈائیلاگ", "「打开方式」对话框")

        Add("Запоминать сортировку мест",
            "Remember sort for places", "Запам'ятовувати сортування місць",
            "Sortierung für Orte merken", "Memorizza ordinamento posizioni",
            "Recordar ordenación de ubicaciones", "Mémoriser le tri des emplacements",
            "Lembrar ordenação de locais", "تذكر فرز الأماكن", "स्थानों की छँटाई याद रखें",
            "অবস্থানের বাছাই মনে রাখুন", "مقامات کی چھانٹی یاد رکھیں", "记住各位置的排序")

        Add("Сохранять выбранную сортировку отдельно для каждой папки и ресурса",
            "Save chosen sort separately for each folder and resource",
            "Зберігати вибране сортування окремо для кожної папки та ресурсу",
            "Gewählte Sortierung separat für jeden Ordner und jede Ressource speichern",
            "Salva l'ordinamento scelto separatamente per ciascuna cartella e risorsa",
            "Guardar la ordenación elegida por separado para cada carpeta y recurso",
            "Enregistrer le tri choisi séparément pour chaque dossier et ressource",
            "Salvar a ordenação escolhida separadamente para cada pasta e recurso",
            "حفظ الفرز المختار بشكل منفصل لكل مجلد ومورد",
            "प्रत्येक फ़ोल्डर और संसाधन के लिए चुनी गई छँटाई अलग से सहेजें",
            "প্রতিটি ফোল্ডার এবং সংস্থানের জন্য নির্বাচিত বাছাই আলাদাভাবে সংরক্ষণ করুন",
            "ہر فولڈر اور ماخذ کے لیے منتخب چھانٹی الگ سے محفوظ کریں",
            "为每个文件夹和资源分别保存所选排序")

        Add("Показывать локальные диски",
            "Show local drives", "Показувати локальні диски", "Lokale Laufwerke anzeigen",
            "Mostra unità locali", "Mostrar unidades locales", "Afficher les lecteurs locaux",
            "Mostrar unidades locais", "إظهار محركات الأقراص المحلية", "स्थानीय ड्राइव दिखाएँ",
            "স্থানীয় ড্রাইভ দেখান", "مقامی ڈرائیوز دکھائیں", "显示本地驱动器")

        Add("Отображать логические диски Windows в списке мест",
            "Display Windows logical drives in the places list",
            "Відображати логічні диски Windows у списку місць",
            "Logische Windows-Laufwerke in der Ortsliste anzeigen",
            "Visualizza le unità logiche di Windows nell'elenco delle posizioni",
            "Mostrar las unidades lógicas de Windows en la lista de ubicaciones",
            "Afficher les lecteurs logiques Windows dans la liste des emplacements",
            "Exibir unidades lógicas do Windows na lista de locais",
            "عرض محركات أقراص Windows المنطقية في قائمة الأماكن",
            "स्थान सूची में Windows लॉजिकल ड्राइव प्रदर्शित करें",
            "অবস্থানের তালিকায় Windows লজিক্যাল ড্রাইভ প্রদর্শন করুন",
            "مقامات کی فہرست میں Windows منطقی ڈرائیوز دکھائیں",
            "在位置列表中显示 Windows 逻辑驱动器")

        Add("Показывать подключённые ресурсы",
            "Show connected resources", "Показувати підключені ресурси",
            "Verbundene Ressourcen anzeigen", "Mostra risorse connesse",
            "Mostrar recursos conectados", "Afficher les ressources connectées",
            "Mostrar recursos conectados", "إظهار الموارد المتصلة", "कनेक्ट किए गए संसाधन दिखाएँ",
            "সংযুক্ত সংস্থানগুলি দেখান", "منسلک وسائل دکھائیں", "显示已连接的资源")

        Add("Отображать сетевые и облачные ресурсы в списке мест",
            "Display network and cloud resources in the places list",
            "Відображати мережеві та хмарні ресурси у списку місць",
            "Netzwerk- und Cloud-Ressourcen in der Ortsliste anzeigen",
            "Visualizza le risorse di rete e cloud nell'elenco delle posizioni",
            "Mostrar recursos de red y en la nube en la lista de ubicaciones",
            "Afficher les ressources réseau et cloud dans la liste des emplacements",
            "Exibir recursos de rede e nuvem na lista de locais",
            "عرض موارد الشبكة والسحابة في قائمة الأماكن",
            "स्थान सूची में नेटवर्क और क्लाउड संसाधन प्रदर्शित करें",
            "অবস্থানের তালিকায় নেটওয়ার্ক এবং ক্লাউড সংস্থান প্রদর্শন করুন",
            "مقامات کی فہرست میں نیٹ ورک اور کلاؤڈ وسائل دکھائیں",
            "在位置列表中显示网络和云资源")

        Add("Лимит внешнего кэша",
            "External cache limit", "Ліміт зовнішнього кешу", "Limit für externen Cache",
            "Limite cache esterna", "Límite de caché externa", "Limite du cache externe",
            "Limite de cache externo", "حد الذاكرة المؤقتة الخارجية", "बाहरी कैश सीमा",
            "বাহ্যিক ক্যাশ সীমা", "بیرونی کیش کی حد", "外部缓存限额")

        Add("Максимальный объем временных копий удаленных файлов (МБ)",
            "Maximum size of temporary copies of remote files (MB)",
            "Максимальний обсяг тимчасових копій віддалених файлів (МБ)",
            "Maximale Größe temporärer Kopien entfernter Dateien (MB)",
            "Dimensione massima delle copie temporanee di file remoti (MB)",
            "Tamaño máximo de copias temporales de archivos remotos (MB)",
            "Taille maximale des copies temporaires de fichiers distants (Mo)",
            "Tamanho máximo das cópias temporárias de arquivos remotos (MB)",
            "الحد الأقصى لحجم النسخ المؤقتة للملفات البعيدة (ميغابايت)",
            "रिमोट फ़ाइलों की अस्थायी प्रतियों का अधिकतम आकार (MB)",
            "দূরবর্তী ফাইলের অস্থায়ী অনুলিপির সর্বাধিক আকার (মেগাবাইট)",
            "ریموٹ فائلوں کی عارضی کاپیوں کا زیادہ سے زیادہ سائز (MB)",
            "远程文件临时副本的最大空间 (MB)")

        Add("Скрытые",
            "Hidden", "Приховані", "Ausgeblendet", "Nascosti", "Ocultos", "Masqués",
            "Ocultos", "مخفي", "छिपा हुआ", "লুকানো", "پوشیدہ", "隐藏")

        Add("Показывать скрытые и системные файлы",
            "Show hidden and system files", "Показувати приховані та системні файли",
            "Ausgeblendete und Systemdateien anzeigen", "Mostra file nascosti e di sistema",
            "Mostrar archivos ocultos y del sistema", "Afficher les fichiers masqués et système",
            "Mostrar arquivos ocultos e do sistema", "إظهار الملفات المخفية وملفات النظام",
            "छिपी हुई और सिस्टम फ़ाइलें दिखाएँ", "লুকানো এবং সিস্টেম ফাইল দেখান",
            "پوشیدہ اور سسٹم فائلیں دکھائیں", "显示隐藏和系统文件")

        Add("Запуск файлов с расширением {0} заблокирован из соображений безопасности.",
            "Launching files with extension {0} is blocked for security reasons.",
            "Запуск файлів із розширенням {0} заблоковано з міркувань безпеки.",
            "Das Starten von Dateien mit der Erweiterung {0} ist aus Sicherheitsgründen blockiert.",
            "L'avvio di file con estensione {0} è bloccato per motivi di sicurezza.",
            "La ejecución de archivos con extensión {0} está bloqueada por motivos de seguridad.",
            "Le lancement de fichiers avec l'extension {0} est bloqué pour des raisons de sécurité.",
            "A inicialização de arquivos com extensão {0} foi bloqueada por motivos de segurança.",
            "تم حظر تشغيل الملفات ذات الامتداد {0} لأسباب أمنية.",
            "{0} एक्सटेंशन वाली फ़ाइलों को लॉन्च करना सुरक्षा कारणों से अवरुद्ध है।",
            "নিরাপত্তাজনিত কারণে {0} এক্সটেনশনের ফাইল চালু করা ব্লক করা হয়েছে।",
            "سیکیورٹی وجوہات کی بنا پر {0} ایکسٹینشن والی فائلوں کا اجرا بلاک ہے۔",
            "出于安全考虑，已阻止启动扩展名为 {0} 的文件。")

        Add("Размер файла превышает лимит кэша.",
            "File size exceeds cache limit.", "Розмір файлу перевищує ліміт кешу.",
            "Dateigröße überschreitet das Cache-Limit.", "La dimensione del file supera il limite della cache.",
            "El tamaño del archivo supera el límite de caché.", "La taille du fichier dépasse la limite du cache.",
            "O tamanho do arquivo excede o limite do cache.", "حجم الملف يتجاوز حد الذاكرة المؤقتة.",
            "फ़ाइल का आकार कैश सीमा से अधिक है।", "ফাইルの আকার ক্যাশ সীমা অতিক্রম করেছে।",
            "فائل کا سائز کیش کی حد سے زیادہ ہے۔", "文件大小超出缓存限制。")

        Add("Загрузка: {0}...",
            "Downloading: {0}...", "Завантаження: {0}...", "Wird heruntergeladen: {0}...",
            "Download in corso: {0}...", "Descargando: {0}...", "Téléchargement : {0}...",
            "Baixando: {0}...", "جارٍ التنزيل: {0}...", "डाउनलोड हो रहा है: {0}...",
            "ডাউনলোড হচ্ছে: {0}...", "ڈاؤن لوڈ ہو رہا ہے: {0}...", "正在下载：{0}...")

        Add("Ошибка загрузки файла.",
            "File download error.", "Помилка завантаження файлу.", "Fehler beim Herunterladen der Datei.",
            "Errore durante il download del file.", "Error al descargar el archivo.",
            "Erreur de téléchargement du fichier.", "Erro ao baixar o arquivo.",
            "خطأ في تنزيل الملف.", "फ़ाइल डाउनलोड त्रुटि।", "ফাইল ডাউনলোড ত্রুটি।",
            "فائل ڈاؤن لوڈ میں خرابی۔", "文件下载出错。")

        Add("Изменения в локальной копии не сохраняются на сервере.",
            "Changes in the local copy are not saved to the server.",
            "Зміни в локальній копії не зберігаються на сервері.",
            "Änderungen an der lokalen Kopie werden nicht auf dem Server gespeichert.",
            "Le modifiche alla copia locale non vengono salvate sul server.",
            "Los cambios en la copia local no se guardan en el servidor.",
            "Les modifications apportées à la copie locale ne sont pas enregistrées sur le serveur.",
            "As alterações na cópia local não são salvas no servidor.",
            "لا يتم حفظ التغييرات في النسخة المحلية على الخادم.",
            "स्थानीय प्रति में किए गए परिवर्तन सर्वर पर सहेजे नहीं जाते हैं।",
            "স্থানীয় অনুলিপিতে করা পরিবর্তনগুলি সার্ভারে সংরক্ষিত হয় না।",
            "مقامی کاپی میں تبدیلیاں سرور پر محفوظ نہیں ہوتیں۔",
            "本地副本中的更改不会保存到服务器。")

        Add("Элемент не существует",
            "Item does not exist", "Елемент не існує", "Objekt existiert nicht",
            "L'elemento non esiste", "El elemento no existe", "L'élément n'existe pas",
            "O item não existe", "العنصر غير موجود", "आइटम मौजूद नहीं है",
            "উপাদানটির অস্তিত্ব নেই", "آئٹم موجود نہیں ہے", "项目不存在")

        Add("Переход по символическим ссылкам не поддерживается",
            "Following symbolic links is not supported",
            "Перехід за символічними посиланнями не підтримується",
            "Das Folgen symbolischer Verknüpfungen wird nicht unterstützt",
            "Il passaggio tramite collegamenti simbolici non è supportato",
            "No se admite seguir enlaces simbólicos",
            "Le suivi des liens symboliques n'est pas pris en charge",
            "O seguimento de links simbólicos não é suportado",
            "تتبع الروابط الرمزية غير مدعوم",
            "सिम्बोलिक लिंक का अनुसरण समर्थित नहीं है",
            "সিম্বলিক লিঙ্ক অনুসরণ করা সমর্থিত নয়",
            "علامتی لنکس کی پیروی تعاون یافتہ نہیں ہے",
            "不支持访问符号链接")

        Add("Запуск программ и сценариев заблокирован в целях безопасности",
            "Launching programs and scripts is blocked for security reasons",
            "Запуск програм і сценаріїв заблоковано з міркувань безпеки",
            "Das Ausführen von Programmen und Skripten ist aus Sicherheitsgründen blockiert",
            "L'avvio di programmi e script è bloccato per motivi di sicurezza",
            "La ejecución de programas y scripts está bloqueada por motivos de seguridad",
            "L'exécution de programmes et de scripts est bloquée pour des raisons de sécurité",
            "A execução de programas e scripts foi bloqueada por motivos de segurança",
            "تم حظر تشغيل البرامج والبرامج النصية لأسباب أمنية",
            "प्रोग्राम और स्क्रिप्ट लॉन्च करना सुरक्षा कारणों से अवरुद्ध है",
            "নিরাপত্তাজনিত কারণে প্রোগ্রাম এবং স্ক্রিপ্ট চালানো ব্লক করা হয়েছে",
            "پروگراموں اور اسکرپٹس کا اجرا سیکیورٹی وجوہات کی بنا پر بلاک ہے",
            "出于安全考虑，已阻止启动程序和脚本")

        Add("Не удалось подключиться к ресурсу.",
            "Could not connect to resource.", "Не вдалося підключитися до ресурсу.",
            "Verbindung zur Ressource fehlgeschlagen.", "Impossibile connettersi alla risorsa.",
            "No se pudo conectar al recurso.", "Impossible de se connecter à la ressource.",
            "Não foi possível conectar ao recurso.", "تعذر الاتصال بالمورد.",
            "संसाधन से कनेक्ट नहीं हो सका।", "সংস্থানে সংযোগ করা যায়নি।",
            "وسیلے سے رابطہ قائم نہیں ہو سکا۔", "无法连接到资源。")

        ' --- Table_Form modern layout titles & descriptions (258) ---
        Add("Вид, фильтры содержимого и открытие файлов.",
            "View, content filters and file opening.",
            "Вигляд, фільтри вмісту та відкриття файлів.",
            "Ansicht, Inhaltsfilter und Dateiöffnung.",
            "Visualizzazione, filtri contenuti e apertura file.",
            "Vista, filtros de contenido y apertura de archivos.",
            "Affichage, filtres de contenu et ouverture des fichiers.",
            "Exibição, filtros de conteúdo e abertura de arquivos.",
            "العرض وعوامل تصفية المحتوى وفتح الملفات.",
            "दृश्य, सामग्री फ़िल्टर और फ़ाइल खोलना।",
            "ভিউ, বিষয়বস্তু ফিল্টার এবং ফাইল খোলা।",
            "منظر، مواد کے فلٹرز اور فائل کھولنا۔",
            "视图、内容筛选器和文件打开。")

        Add("Режим отображения",
            "Display mode", "Режим відображення", "Anzeigemodus",
            "Modalità di visualizzazione", "Modo de visualización", "Mode d'affichage",
            "Modo de exibição", "وضع العرض", "प्रदर्शन मोड", "প্রদর্শন মোড", "ڈسپلے موڈ", "显示模式")

        Add("Скрытые и системные файлы",
            "Hidden and system files", "Приховані та системні файли", "Ausgeblendete und Systemdateien",
            "File nascosti e di sistema", "Archivos ocultos y del sistema", "Fichiers masqués et système",
            "Arquivos ocultos e do sistema", "الملفات المخفية وملفات النظام", "छिपी हुई और सिस्टम फ़ाइलें",
            "লুকানো এবং সিস্টেম ফাইল", "پوشیدہ اور سسٹم فائلیں", "隐藏和系统文件")

        Add("Локальные папки",
            "Local folders", "Локальні папки", "Lokale Ordner", "Cartelle locali",
            "Carpetas locales", "Dossiers locaux", "Pastas locais", "المجلدات المحلية",
            "स्थानीय फ़ोल्डर", "স্থানীয় ফোল্ডার", "مقامی فولڈرز", "本地文件夹")

        Add("Запоминать сортировку",
            "Remember sort", "Запам'ятовувати сортування", "Sortierung merken",
            "Memorizza ordinamento", "Recordar ordenación", "Mémoriser le tri",
            "Lembrar ordenação", "تذكر الفرز", "छँटाई याद रखें", "বাছাই মনে রাখুন",
            "چھانٹی یاد رکھیں", "记住排序")

        Add("Сетка карточек или таблица с подробными сведениями.",
            "Card grid or table with detailed information.",
            "Сітка карток або таблиця з докладними відомостями.",
            "Kartenraster oder Tabelle mit detaillierten Informationen.",
            "Griglia di schede o tabella con informazioni dettagliate.",
            "Cuadrícula de tarjetas o tabla con información detallada.",
            "Grille de cartes ou tableau avec informations détaillées.",
            "Grade de cartões ou tabela com informações detalhadas.",
            "شبكة بطاقات أو جدول يحتوي على معلومات تفصيلية.",
            "विस्तृत जानकारी वाली कार्ड ग्रिड या तालिका।",
            "বিস্তারিত তথ্য সহ কার্ড গ্রিড বা টেবিল।",
            "تفصیلی معلومات کے ساتھ کارڈ گرڈ یا جدول۔",
            "卡片网格或带有详细信息的表格。")

        Add("Какие файлы показывать в проводнике по умолчанию: медиафайлы, медиа и документы или все файлы.",
            "Which files to show in the browser by default: media files, media and documents, or all files.",
            "Які файли показувати у провіднику за замовчуванням: медіафайли, медіа та документи або всі файли.",
            "Welche Dateien standardmäßig im Explorer angezeigt werden sollen: Mediendateien, Medien und Dokumente oder alle Dateien.",
            "Quali file mostrare nel browser per impostazione predefinita: file multimediali, contenuti multimediali e documenti o tutti i file.",
            "Qué archivos mostrar en el explorador de forma predeterminada: archivos multimedia, multimedia y documentos o todos los archivos.",
            "Quels fichiers afficher par défaut dans l'explorateur : fichiers multimédias, médias et documents ou tous les fichiers.",
            "Quais arquivos mostrar no gerenciador por padrão: arquivos de mídia, mídia e documentos ou todos os arquivos.",
            "الملفات التي سيتم إظهارها في المستكشف افتراضيًا: ملفات الوسائط، أو الوسائط والمستندات، أو جميع الملفات.",
            "डिफ़ॉल्ट रूप से एक्सप्लोरर में कौन सी फ़ाइलें दिखाएँ: मीडिया फ़ाइलें, मीडिया और दस्तावेज़ या सभी फ़ाइलें।",
            "ডিফল্টভাবে এক্সপ্লোরারে কোন ফাইলগুলি দেখানো হবে: মিডিয়া ফাইল, মিডিয়া এবং নথি বা সমস্ত ফাইল।",
            "ڈیفالٹ کے طور پر ایکسپلورر میں کون سی فائلیں دکھائیں: میڈیا فائلیں، میڈیا اور دستاویزات یا تمام فائلیں؟",
            "默认在浏览器中显示哪些文件：媒体文件、媒体与文档或所有文件。")

        Add("Открывать документы и другие файлы в программе по умолчанию или запрашивать выбор программы.",
            "Open documents and other files in the default program or prompt for a program choice.",
            "Відкривати документи та інші файли у програмі за замовчуванням або запитувати вибір програми.",
            "Dokumente und andere Dateien im Standardprogramm öffnen oder zur Programmauswahl auffordern.",
            "Apri documenti e altri file nel programma predefinito o richiedi la scelta del programma.",
            "Abrir documentos y otros archivos en el programa predeterminado o solicitar la elección del programa.",
            "Ouvrir les documents et autres fichiers dans le programme par défaut ou demander le choix du programme.",
            "Abrir documentos e outros arquivos no programa padrão ou solicitar a escolha do programa.",
            "فتح المستندات والملفات الأخرى في البرنامج الافتراضي أو المطالبة باختيار البرنامج.",
            "दस्तावेज़ और अन्य फ़ाइलें डिफ़ॉल्ट प्रोग्राम में खोलें या प्रोग्राम चुनने का संकेत दें।",
            "ডিফল্ট প্রোগ্রামে নথি এবং অন্যান্য ফাইল খুলুন অথবা প্রোগ্রাম বেছে নেওয়ার অনুরোধ করুন।",
            "دستاویزات اور دیگر فائلیں ڈیفالٹ پروگرام میں کھولیں یا پروگرام کے انتخاب کا اشارہ دیں۔",
            "在默认程序中打开文档和其他文件，或提示选择程序。")

        Add("Показывать стандартные системные папки и диски в списке расположений.",
            "Show standard system folders and drives in the places list.",
            "Показувати стандартні системні папки та диски у списку розташувань.",
            "Standardsystemordner und -laufwerke in der Ortsliste anzeigen.",
            "Mostra le cartelle e le unità di sistema standard nell'elenco delle posizioni.",
            "Mostra las carpetas y unidades estándar del sistema en la lista de ubicaciones.",
            "Afficher les dossiers et lecteurs système standard dans la liste des emplacements.",
            "Mostrar pastas e unidades padrão do sistema na lista de locais.",
            "إظهار مجلدات ومحركات أقراص النظام القياسية في قائمة الأماكن.",
            "स्थान सूची में मानक सिस्टम फ़ोल्डर और ड्राइव दिखाएँ।",
            "অবস্থানের তালিকায় আদর্শ সিস্টেম ফোল্ডার এবং ড্রাইভ দেখান।",
            "مقامات کی فہرست میں معیاری سسٹم فولڈرز اور ڈرائیوز دکھائیں۔",
            "在位置列表中显示标准系统文件夹和驱动器。")

        Add("Показывать настроенные ресурсы и серверы в списке расположений.",
            "Show configured resources and servers in the places list.",
            "Показувати налаштовані ресурси та сервери у списку розташувань.",
            "Konfigurierte Ressourcen und Server in der Ortsliste anzeigen.",
            "Mostra le risorse e i server configurati nell'elenco delle posizioni.",
            "Mostrar recursos y servidores configurados en la lista de ubicaciones.",
            "Afficher les ressources et serveurs configurés dans la liste des emplacements.",
            "Mostrar recursos e servidores configurados na lista de locais.",
            "إظهار الموارد والخوادم المكوّنة في قائمة الأماكن.",
            "स्थान सूची में कॉन्फ़िगर किए गए संसाधन और सर्वर दिखाएँ।",
            "অবস্থানের তালিকায় কনফিগার করা সংস্থান এবং সার্ভার দেখান।",
            "مقامات کی فہرست میں ترتیب شدہ وسائل اور سرورز دکھائیں۔",
            "在位置列表中显示已配置的资源和服务器。")

        Add("Показывать устройства и мобильные подключения в списке расположений.",
            "Show devices and mobile connections in the places list.",
            "Показувати пристрої та мобільні підключення у списку розташувань.",
            "Geräte und mobile Verbindungen in der Ortsliste anzeigen.",
            "Mostra dispositivi e connessioni mobili nell'elenco delle posizioni.",
            "Mostrar dispositivos y conexiones móviles en la lista de ubicaciones.",
            "Afficher les appareils et les connexions mobiles dans la liste des emplacements.",
            "Mostrar dispositivos e conexões móveis na lista de locais.",
            "إظهار الأجهزة والاتصالات المحمولة في قائمة الأماكن.",
            "स्थान सूची में डिवाइस और मोबाइल कनेक्शन दिखाएँ।",
            "অবস্থানের তালিকায় ডিভাইস এবং মোবাইল সংযোগ দেখান।",
            "مقامات کی فہرست میں آلات اور موبائل کنکشنز دکھائیں۔",
            "在位置列表中显示设备和移动连接。")

        Add("Сохранять выбранную колонку и направление сортировки для каждого расположения.",
            "Save chosen column and sort direction for each location.",
            "Зберігати вибрану колонку та напрямок сортування для кожного розташування.",
            "Ausgewählte Spalte und Sortierrichtung für jeden Ort speichern.",
            "Salva la colonna scelta e la direzione di ordinamento per ciascuna posizione.",
            "Guardar la columna elegida y la dirección de ordenación para cada ubicación.",
            "Enregistrer la colonne et la direction de tri choisies pour chaque emplacement.",
            "Salvar a coluna escolhida e a direção de ordenação para cada local.",
            "حفظ العمود المختار واتجاه الفرز لكل موقع.",
            "प्रत्येक स्थान के लिए चुना गया कॉलम और छँटाई की दिशा सहेजें।",
            "প্রতিটি অবস্থানের জন্য নির্বাচিত কলাম এবং বাছাইয়ের দিক সংরক্ষণ করুন।",
            "ہر مقام کے لیے منتخب کالم اور چھانٹی کی سمت محفوظ کریں۔",
            "为每个位置保存所选列和排序方向。")
Add("Тип файла признан опасным системой и заблокирован",
            "The file type is considered dangerous by the system and has been blocked",
            "Тип файлу визнано небезпечним системою та заблоковано",
            "Der Dateityp wird vom System als gefährlich eingestuft und wurde blockiert",
            "Il tipo di file è considerato pericoloso dal sistema ed è stato bloccato",
            "El tipo de archivo es considerado peligroso por el sistema y ha sido bloqueado",
            "Le type de fichier est considéré comme dangereux par le système et a été bloqué",
            "O tipo de arquivo é considerado perigoso pelo sistema e foi bloqueado",
            "يعتبر النظام نوع الملف خطيرًا وتم حظره",
            "फ़ाइल प्रकार को सिस्टम द्वारा खतरनाक माना जाता है और इसे अवरुद्ध कर दिया गया है",
            "ফাইলের ধরণটি সিস্টেম দ্বারা বিপজ্জনক বলে বিবেচিত এবং ব্লক করা হয়েছে",
            "فائل کی قسم کو سسٹم کے ذریعہ خطرناک سمجھا جاتا ہے اور اسے بلاک کر دیا گیا ہے",
            "该文件类型被系统判定为危险并已被阻止")

        Add("Не удалось проверить безопасность типа файла",
            "Could not check file type safety",
            "Не вдалося перевірити безпеку типу файлу",
            "Sicherheit des Dateityps konnte nicht überprüft werden",
            "Impossibile verificare la sicurezza del tipo di file",
            "No se pudo comprobar la seguridad del tipo de archivo",
            "Impossible de vérifier la sécurité du type de fichier",
            "Não foi possível verificar a segurança do tipo de arquivo",
            "تعذر التحقق من أمان نوع الملف",
            "फ़ाइल प्रकार की सुरक्षा की जाँच नहीं की जा सकी",
            "ফাইলের ধরণের নিরাপত্তা পরীক্ষা করা যায়নি",
            "فائل کی قسم کی حفاظت کی جانچ نہیں ہو سکی",
            "无法检查文件类型的安全性")
    End Sub
End Class
