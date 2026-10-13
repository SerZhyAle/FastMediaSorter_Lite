Option Strict On

' <summary>
' The strings of the welcome wizard (spec 184): the window's title, the step frame, the four named parts of
' every step - what it is, why it exists, how the viewer will behave, and where to change it later (spec 211) -
' the controls and the buttons.
'
' The table is shared with the net48 build like the others (a string table costs the x86 exe nothing, and splitting
' it per build is how two tables drift apart); only the .NET 10 viewer shows the wizard. Keys that already exist
' elsewhere are reused, not repeated: the six background names, "Язык интерфейса", "Цвет фона:", "Очистить" and
' "Выбрать папку..", and - through the where-paths of spec 211 - the pages, groups and rows the settings window
' itself renders. Never a smart double quote in a literal (CLAUDE.md): use the guillemets. House style: ".." not
' "...", a plain hyphen, and the Russian "ё" where it belongs.
' </summary>
Partial Public NotInheritable Class Localization

    Private Shared Sub AddWelcomeStrings()
        Add("{0}: {1} - {2}", "{0}: {1} - {2}", "{0}: {1} - {2}", "{0}: {1} - {2}",
            "{0}: {1} - {2}", "{0}: {1} - {2}", "{0}: {1} - {2}", "{0}: {1} - {2}",
            "{0}: {1} - {2}", "{0}: {1} - {2}", "{0}: {1} - {2}", "{0}: {1} - {2}", "{0}: {1} - {2}")
        Add("Первоначальную настройку можно открыть кнопкой на пустом экране.",
            "The welcome setup can be opened with the button on the empty screen.", "Початкове налаштування можна відкрити кнопкою на порожньому екрані.", "Die Ersteinrichtung lässt sich über die Schaltfläche auf dem leeren Bildschirm öffnen.",
            "La configurazione iniziale si apre con il pulsante sulla schermata vuota.", "La configuración inicial se abre con el botón de la pantalla vacía.", "La configuration initiale s'ouvre avec le bouton de l'écran vide.",
            "A configuração inicial abre-se com o botão no ecrã vazio.", "يمكن فتح الإعداد الأولي بزر على الشاشة الفارغة.", "प्रारंभिक सेटअप खाली स्क्रीन पर बटन से खोला जा सकता है।",
            "প্রাথমিক সেটআপ খালি স্ক্রিনের বোতাম দিয়ে খোলা যায়।", "ابتدائی سیٹ اپ خالی اسکرین پر موجود بٹن سے کھولا جا سکتا ہے۔", "初始设置可以通过空白屏幕上的按钮打开。")
        Add("Первоначальная настройка",
            "Welcome setup", "Початкове налаштування", "Ersteinrichtung",
            "Configurazione iniziale", "Configuración inicial", "Configuration initiale",
            "Configuração inicial", "الإعداد الأولي", "प्रारंभिक सेटअप",
            "প্রাথমিক সেটআপ", "ابتدائی سیٹ اپ", "初始设置")
        Add("Колесо меняет масштаб у курсора, файлы листают стрелки.",
            "The wheel zooms at the cursor; the arrow keys browse files.", "Колесо змінює масштаб біля курсора, файли гортають стрілки.", "Das Mausrad zoomt am Mauszeiger; die Pfeiltasten wechseln Dateien.",
            "La rotella ingrandisce al cursore; i tasti freccia scorrono i file.", "La rueda amplía en el cursor; las flechas recorren los archivos.", "La molette zoome au niveau du curseur ; les flèches parcourent les fichiers.",
            "A roda amplia no cursor; as setas percorrem os ficheiros.", "تغيّر العجلة التكبير عند المؤشر؛ وتتصفح مفاتيح الأسهم الملفات.", "पहिया कर्सर पर ज़ूम करता है; तीर कुंजियाँ फ़ाइलें बदलती हैं।",
            "চাকা কার্সরের কাছে জুম করে; তীরচিহ্নের কীগুলি ফাইল বদলায়।", "وہیل کرسر پر زوم کرتا ہے؛ تیر والی کلیدیں فائلیں بدلتی ہیں۔", "滚轮在光标处缩放，方向键切换文件。")
        Add("Колесо переходит к следующему или предыдущему файлу, а Ctrl с колесом меняет масштаб.",
            "The wheel moves to the next or previous file; Ctrl with the wheel zooms.", "Колесо переходить до наступного або попереднього файлу, а Ctrl з колесом змінює масштаб.", "Das Mausrad wechselt zur nächsten oder vorherigen Datei; Strg mit dem Mausrad zoomt.",
            "La rotella passa al file successivo o precedente; Ctrl con la rotella regola lo zoom.", "La rueda pasa al archivo siguiente o anterior; Ctrl con la rueda cambia el zoom.", "La molette passe au fichier suivant ou précédent ; Ctrl avec la molette règle le zoom.",
            "A roda passa ao ficheiro seguinte ou anterior; Ctrl com a roda ajusta o zoom.", "تنتقل العجلة إلى الملف التالي أو السابق؛ ويغيّر Ctrl مع العجلة التكبير.", "पहिया अगली या पिछली फ़ाइल पर जाता है; Ctrl के साथ पहिया ज़ूम बदलता है।",
            "চাকা পরের বা আগের ফাইলে যায়; Ctrl সহ চাকা জুম বদলায়।", "وہیل اگلی یا پچھلی فائل پر جاتا ہے؛ Ctrl کے ساتھ وہیل زوم بدلتا ہے۔", "滚轮切换到下一个或上一个文件，Ctrl 加滚轮调整缩放。")

        ' --- the window and the step frame ------------------------------------------------------
        Add("Добро пожаловать",
            "Welcome", "Ласкаво просимо", "Willkommen",
            "Benvenuto", "Bienvenido", "Bienvenue",
            "Bem-vindo", "مرحباً بك", "स्वागत है",
            "স্বাগতম", "خوش آمدید", "欢迎")
        ' "Шаг {0} из {1}" is registered once, by Localization.Editor.vb; the wizard reuses it.
        Add("Что это",
            "What it is", "Що це", "Was es ist",
            "Cos'è", "Qué es", "Ce que c'est",
            "O que é", "ما هو", "यह क्या है",
            "এটি কী", "یہ کیا ہے", "这是什么")
        Add("Зачем",
            "Why it exists", "Навіщо", "Wozu",
            "Perché", "Para qué", "Pourquoi",
            "Para que serve", "لماذا", "यह क्यों है",
            "কেন", "کیوں", "为什么")
        Add("Как будет работать",
            "How it will work", "Як це працюватиме", "So wird es funktionieren",
            "Come funzionerà", "Cómo funcionará", "Comment cela fonctionnera",
            "Como vai funcionar", "كيف سيعمل", "यह कैसे काम करेगा",
            "এটি কীভাবে কাজ করবে", "یہ کیسے کام کرے گا", "它将如何工作")
        ' Spec 211 §4.1: the fourth named part, "Где это менять" - the settings page and row, later in the recap too.
        Add("Где это менять",
            "Where to change it", "Де це змінити", "Wo man es ändert",
            "Dove si cambia", "Dónde se cambia", "Où le modifier",
            "Onde se muda", "أين تغيّره", "इसे कहाँ बदलें",
            "কোথায় বদলাবেন", "اسے کہاں بدلیں", "在哪里修改")
        Add("Внешний вид",
            "Appearance", "Вигляд", "Erscheinungsbild",
            "Aspetto", "Apariencia", "Apparence",
            "Aparência", "المظهر", "रूप-रंग",
            "চেহারা", "ظاہری شکل", "外观")
        Add("Как устроена сортировка",
            "How sorting works", "Як влаштовано сортування", "So funktioniert das Sortieren",
            "Come funziona lo smistamento", "Cómo funciona la clasificación", "Comment fonctionne le tri",
            "Como funciona a organização", "كيف يعمل الفرز", "छँटाई कैसे काम करती है",
            "বাছাই কীভাবে কাজ করে", "ترتیب کیسے کام کرتی ہے", "分拣的工作方式")
        Add("Безопасное удаление",
            "Safe deletion", "Безпечне видалення", "Sicheres Löschen",
            "Eliminazione sicura", "Eliminación segura", "Suppression sûre",
            "Exclusão segura", "حذف آمن", "सुरक्षित हटाना",
            "নিরাপদ মোছা", "محفوظ حذف", "安全删除")
        Add("Открывать картинки здесь",
            "Open pictures here", "Відкривати зображення тут", "Bilder hier öffnen",
            "Aprire qui le immagini", "Abrir las imágenes aquí", "Ouvrir les images ici",
            "Abrir imagens aqui", "افتح الصور هنا", "यहाँ तस्वीरें खोलें",
            "এখানে ছবি খুলুন", "تصاویر یہاں کھولیں", "在此打开图片")
        Add("Всё готово",
            "All set", "Усе готово", "Alles bereit",
            "Tutto pronto", "Todo listo", "Tout est prêt",
            "Tudo pronto", "كل شيء جاهز", "सब तैयार है",
            "সব প্রস্তুত", "سب تیار ہے", "一切就绪")

        ' --- step 1: language -------------------------------------------------------------------
        Add("Язык, на котором говорят окна, подсказки и сообщения программы.",
            "The language of the windows, tooltips and messages.", "Мова вікон, підказок і повідомлень програми.", "Die Sprache der Fenster, Tooltips und Meldungen.",
            "La lingua di finestre, suggerimenti e messaggi.", "El idioma de las ventanas, sugerencias y mensajes.", "La langue des fenêtres, info-bulles et messages.",
            "O idioma das janelas, dicas e mensagens.", "لغة النوافذ والتلميحات والرسائل.", "विंडो, टूलटिप और संदेशों की भाषा।",
            "উইন্ডো, টুলটিপ ও বার্তার ভাষা।", "ونڈوز، ٹول ٹپس اور پیغامات کی زبان۔", "窗口、提示和消息所使用的语言。")
        Add("Программа выбрала язык по настройкам Windows. Если вам нужен другой, выберите его здесь.",
            "The viewer chose a language from your Windows settings. If you want another one, pick it here.", "Програма обрала мову за налаштуваннями Windows. Якщо вам потрібна інша, оберіть її тут.", "Das Programm hat die Sprache anhand der Windows-Einstellungen gewählt. Wenn Sie eine andere möchten, wählen Sie sie hier.",
            "Il programma ha scelto la lingua dalle impostazioni di Windows. Se ne preferisci un'altra, scegline una qui.", "El programa eligió el idioma según la configuración de Windows. Si prefiere otro, elíjalo aquí.", "Le programme a choisi la langue d'après les paramètres de Windows. Si vous en voulez une autre, choisissez-la ici.",
            "O programa escolheu o idioma pelas definições do Windows. Se preferir outro, escolha-o aqui.", "اختار البرنامج اللغة من إعدادات Windows. إذا أردت لغة أخرى فاخترها هنا.", "प्रोग्राम ने Windows सेटिंग्स से भाषा चुनी है। कोई और चाहें तो यहाँ चुनें।",
            "প্রোগ্রাম Windows সেটিংস থেকে ভাষা বেছেছে। অন্য ভাষা চাইলে এখানে বেছে নিন।", "پروگرام نے Windows کی ترتیبات سے زبان منتخب کی ہے۔ کوئی اور چاہیں تو یہاں چنیں۔", "程序根据 Windows 设置选择了语言。如需其他语言，可在此选择。")
        Add("Все окна, подсказки и вопросы сразу станут на этом языке. Сменить его можно в любой момент в окне настроек.",
            "Every window, tooltip and question switches to this language at once. You can change it at any time in the settings window.", "Усі вікна, підказки та запитання одразу перейдуть цією мовою. Змінити її можна будь-коли у вікні налаштувань.", "Alle Fenster, Tooltips und Fragen wechseln sofort in diese Sprache. Sie können sie jederzeit im Einstellungsfenster ändern.",
            "Tutte le finestre, i suggerimenti e le domande passano subito a questa lingua. Puoi cambiarla in qualsiasi momento nelle impostazioni.", "Todas las ventanas, sugerencias y preguntas cambian de inmediato a este idioma. Puede cambiarlo en cualquier momento en la ventana de ajustes.", "Toutes les fenêtres, info-bulles et questions passent immédiatement dans cette langue. Vous pouvez la changer à tout moment dans la fenêtre des réglages.",
            "Todas as janelas, dicas e perguntas mudam já para este idioma. Pode alterá-lo a qualquer momento na janela de definições.", "ستتحول كل النوافذ والتلميحات والأسئلة إلى هذه اللغة فوراً. يمكنك تغييرها في أي وقت من نافذة الإعدادات.", "सभी विंडो, टूलटिप और प्रश्न तुरंत इसी भाषा में हो जाएँगे। सेटिंग्स विंडो में कभी भी बदल सकते हैं।",
            "সব উইন্ডো, টুলটিপ ও প্রশ্ন সঙ্গে সঙ্গে এই ভাষায় হবে। সেটিংস উইন্ডো থেকে যেকোনো সময় বদলাতে পারেন।", "تمام ونڈوز، ٹول ٹپس اور سوالات فوراً اسی زبان میں ہو جائیں گے۔ سیٹنگز ونڈو میں کسی بھی وقت بدل سکتے ہیں۔", "所有窗口、提示和问题会立即切换到此语言。可随时在设置窗口中更改。")

        ' --- step 2: appearance -----------------------------------------------------------------
        Add("Фон вокруг картинки и то, что делает колесо мыши.",
            "The background around a picture and what the mouse wheel does.", "Фон навколо зображення і те, що робить коліщатко миші.", "Der Hintergrund um ein Bild und was das Mausrad tut.",
            "Lo sfondo intorno all'immagine e cosa fa la rotellina del mouse.", "El fondo alrededor de la imagen y lo que hace la rueda del ratón.", "Le fond autour de l'image et ce que fait la molette de la souris.",
            "O fundo em volta da imagem e o que faz a roda do rato.", "الخلفية المحيطة بالصورة وما تفعله عجلة الفأرة.", "तस्वीर के चारों ओर की पृष्ठभूमि और माउस व्हील क्या करता है।",
            "ছবির চারপাশের পটভূমি ও মাউস হুইল কী করে।", "تصویر کے گرد پس منظر اور ماؤس وہیل کیا کرتا ہے۔", "图片周围的背景，以及鼠标滚轮的作用。")
        Add("Фон должен спокойно обрамлять снимок, а колесо не должно случайно менять масштаб, когда вы листаете.",
            "The background should frame the picture quietly, and the wheel should not zoom by accident while you flip through files.", "Фон має спокійно обрамляти знімок, а колесо не повинно випадково змінювати масштаб під час перегляду.", "Der Hintergrund soll das Bild ruhig einrahmen, und das Rad soll beim Durchblättern nicht versehentlich zoomen.",
            "Lo sfondo deve incorniciare la foto con discrezione, e la rotellina non deve ingrandire per sbaglio mentre sfogli i file.", "El fondo debe enmarcar la foto con discreción, y la rueda no debe ampliar por accidente al pasar archivos.", "Le fond doit encadrer la photo discrètement, et la molette ne doit pas zoomer par erreur en feuilletant les fichiers.",
            "O fundo deve enquadrar a foto com discrição, e a roda não deve ampliar por engano ao percorrer os ficheiros.", "يجب أن تحيط الخلفية بالصورة بهدوء، وألا تكبّر العجلة عن طريق الخطأ أثناء التنقل بين الملفات.", "पृष्ठभूमि तस्वीर को शांति से घेरे, और फ़ाइलें पलटते समय व्हील गलती से ज़ूम न करे।",
            "পটভূমি ছবিকে শান্তভাবে ঘিরে রাখবে, আর ফাইল ওল্টানোর সময় হুইল ভুল করে জুম না করে।", "پس منظر تصویر کو خاموشی سے گھیرے، اور فائلیں پلٹتے وقت وہیل غلطی سے زوم نہ کرے۔", "背景应安静地衬托照片，翻看文件时滚轮不应误触发缩放。")
        Add("Фон следует за картинкой. Колесо переходит к следующему или предыдущему файлу, а Ctrl с колесом меняет масштаб.",
            "The background follows the picture. The wheel moves to the next or previous file, and Ctrl with the wheel zooms.", "Фон слідує за зображенням. Колесо переходить до наступного або попереднього файлу, а Ctrl із колесом змінює масштаб.", "Der Hintergrund folgt dem Bild. Das Rad wechselt zur nächsten oder vorherigen Datei, und Strg mit dem Rad zoomt.",
            "Lo sfondo segue l'immagine. La rotellina passa al file successivo o precedente, e Ctrl con la rotellina ingrandisce.", "El fondo sigue a la imagen. La rueda pasa al archivo siguiente o anterior, y Ctrl con la rueda amplía.", "Le fond suit l'image. La molette passe au fichier suivant ou précédent, et Ctrl avec la molette zoome.",
            "O fundo segue a imagem. A roda passa para o ficheiro seguinte ou anterior, e Ctrl com a roda amplia.", "تتبع الخلفية الصورة. تنتقل العجلة إلى الملف التالي أو السابق، ويكبّر Ctrl مع العجلة.", "पृष्ठभूमि तस्वीर का अनुसरण करती है। व्हील अगली या पिछली फ़ाइल पर जाता है, और Ctrl के साथ व्हील ज़ूम करता है।",
            "পটভূমি ছবিকে অনুসরণ করে। হুইল পরের বা আগের ফাইলে যায়, আর Ctrl সহ হুইল জুম করে।", "پس منظر تصویر کی پیروی کرتا ہے۔ وہیل اگلی یا پچھلی فائل پر جاتا ہے، اور Ctrl کے ساتھ وہیل زوم کرتا ہے۔", "背景跟随图片。滚轮切换到下一个或上一个文件，按住 Ctrl 滚动则缩放。")
        Add("Фон следует за картинкой. Колесо меняет масштаб у курсора, файлы листают стрелки.",
            "The background follows the picture. The wheel zooms at the cursor, and the arrow keys move between files.", "Фон слідує за зображенням. Колесо змінює масштаб біля курсора, файли гортають стрілки.", "Der Hintergrund folgt dem Bild. Das Rad zoomt am Cursor, und die Pfeiltasten wechseln die Datei.",
            "Lo sfondo segue l'immagine. La rotellina ingrandisce sul cursore, e le frecce passano da un file all'altro.", "El fondo sigue a la imagen. La rueda amplía en el cursor, y las flechas cambian de archivo.", "Le fond suit l'image. La molette zoome à l'emplacement du curseur, et les flèches passent d'un fichier à l'autre.",
            "O fundo segue a imagem. A roda amplia no cursor, e as setas mudam de ficheiro.", "تتبع الخلفية الصورة. تكبّر العجلة عند المؤشر، وتتنقل مفاتيح الأسهم بين الملفات.", "पृष्ठभूमि तस्वीर का अनुसरण करती है। व्हील कर्सर पर ज़ूम करता है, और तीर कुंजियाँ फ़ाइलों के बीच ले जाती हैं।",
            "পটভূমি ছবিকে অনুসরণ করে। হুইল কার্সরের জায়গায় জুম করে, আর তীর কী ফাইলের মধ্যে সরায়।", "پس منظر تصویر کی پیروی کرتا ہے۔ وہیل کرسر پر زوم کرتا ہے، اور تیر کی کلیدیں فائلوں کے درمیان لے جاتی ہیں۔", "背景跟随图片。滚轮在光标处缩放，方向键在文件之间切换。")
        Add("Колесо мыши меняет масштаб, а не файлы",
            "The mouse wheel zooms instead of changing files", "Колесо миші змінює масштаб, а не файли", "Das Mausrad zoomt statt Dateien zu wechseln",
            "La rotellina ingrandisce invece di cambiare file", "La rueda amplía en lugar de cambiar de archivo", "La molette zoome au lieu de changer de fichier",
            "A roda amplia em vez de mudar de ficheiro", "تكبّر العجلة بدلاً من تغيير الملفات", "व्हील फ़ाइलें बदलने के बजाय ज़ूम करता है",
            "ফাইল বদলানোর বদলে হুইল জুম করে", "وہیل فائلیں بدلنے کے بجائے زوم کرے", "滚轮改为缩放而不是切换文件")

        ' --- step 3: sorting --------------------------------------------------------------------
        Add("Цифры 0-9 переносят открытый файл в папку, закреплённую за этой клавишей.",
            "Keys 0 to 9 move the open file to the folder bound to that key.", "Клавіші 0-9 переносять відкритий файл до папки, прив'язаної до цієї клавіші.", "Die Tasten 0 bis 9 verschieben die geöffnete Datei in den Ordner dieser Taste.",
            "I tasti da 0 a 9 spostano il file aperto nella cartella assegnata a quel tasto.", "Las teclas 0 a 9 mueven el archivo abierto a la carpeta asignada a esa tecla.", "Les touches 0 à 9 déplacent le fichier ouvert vers le dossier attribué à la touche.",
            "As teclas 0 a 9 movem o ficheiro aberto para a pasta atribuída a essa tecla.", "تنقل المفاتيح من 0 إلى 9 الملف المفتوح إلى المجلد المرتبط بذلك المفتاح.", "कुंजी 0 से 9 खुली फ़ाइल को उस कुंजी से जुड़े फ़ोल्डर में ले जाती हैं।",
            "0 থেকে 9 কী খোলা ফাইলটিকে সেই কীয়ের সঙ্গে যুক্ত ফোল্ডারে সরায়।", "کلیدیں 0 تا 9 کھلی فائل کو اس کلید سے جڑے فولڈر میں منتقل کرتی ہیں۔", "0 到 9 键会把当前文件移到该键对应的文件夹。")
        Add("Сортировка одним нажатием - главное, ради чего существует программа. Папки назначаются заранее, поэтому нажатие не требует раздумий.",
            "One-key sorting is the reason the viewer exists. Folders are assigned in advance, so a press needs no thought.", "Сортування одним натисканням - головне, заради чого існує програма. Папки призначаються наперед, тож натискання не потребує роздумів.", "Sortieren mit einer Taste ist der Grund, warum es dieses Programm gibt. Die Ordner werden vorher festgelegt, daher braucht ein Tastendruck keine Überlegung.",
            "Smistare con un solo tasto è il motivo per cui esiste il programma. Le cartelle si assegnano in anticipo, quindi premere non richiede riflessione.", "Clasificar con una sola tecla es la razón de ser del programa. Las carpetas se asignan de antemano, así que pulsar no requiere pensarlo.", "Trier d'une seule touche est la raison d'être du programme. Les dossiers sont attribués à l'avance, donc un appui ne demande pas de réflexion.",
            "Organizar com uma tecla é a razão de ser do programa. As pastas são atribuídas antecipadamente, por isso carregar numa tecla não exige pensar.", "الفرز بمفتاح واحد هو سبب وجود البرنامج. تُحدَّد المجلدات مسبقاً، لذا لا تحتاج الضغطة إلى تفكير.", "एक कुंजी से छँटाई ही इस प्रोग्राम का मकसद है। फ़ोल्डर पहले से तय होते हैं, इसलिए दबाने में सोचना नहीं पड़ता।",
            "এক কী দিয়ে বাছাই করাই এই প্রোগ্রামের উদ্দেশ্য। ফোল্ডার আগে থেকে ঠিক করা থাকে, তাই চাপতে ভাবতে হয় না।", "ایک کلید سے ترتیب ہی اس پروگرام کا مقصد ہے۔ فولڈر پہلے سے طے ہوتے ہیں، اس لیے دبانے میں سوچنا نہیں پڑتا۔", "一键分拣正是这个程序存在的理由。文件夹预先设定，按一下无需多想。")
        Add("Цифра переносит файл. Shift с цифрой верхнего ряда копирует его в ту же папку вместо переноса.",
            "A digit moves the file. Shift with a digit from the top row copies it into the same folder instead.", "Цифра переносить файл. Shift із цифрою верхнього ряду копіює його до тієї самої папки замість переносу.", "Eine Ziffer verschiebt die Datei. Shift mit einer Ziffer aus der oberen Reihe kopiert sie stattdessen in denselben Ordner.",
            "Una cifra sposta il file. Shift con una cifra della riga superiore lo copia invece nella stessa cartella.", "Un número mueve el archivo. Shift con un número de la fila superior lo copia en la misma carpeta.", "Un chiffre déplace le fichier. Maj avec un chiffre de la rangée supérieure le copie dans le même dossier.",
            "Um algarismo move o ficheiro. Shift com um algarismo da linha superior copia-o para a mesma pasta.", "يحرّك الرقم الملف. ويؤدي Shift مع رقم من الصف العلوي إلى نسخه في المجلد نفسه بدلاً من نقله.", "अंक फ़ाइल को ले जाता है। ऊपरी पंक्ति के अंक के साथ Shift उसे उसी फ़ोल्डर में कॉपी करता है।",
            "একটি সংখ্যা ফাইল সরায়। ওপরের সারির সংখ্যার সঙ্গে Shift চাপলে সরানোর বদলে একই ফোল্ডারে কপি হয়।", "ایک ہندسہ فائل کو منتقل کرتا ہے۔ اوپر کی قطار کے ہندسے کے ساتھ Shift اسے منتقل کرنے کے بجائے اسی فولڈر میں کاپی کرتا ہے۔", "数字键移动文件。按住 Shift 再按上排数字键则改为复制到同一文件夹。")
        Add("Папка для клавиши {0}",
            "Folder for key {0}", "Папка для клавіші {0}", "Ordner für Taste {0}",
            "Cartella per il tasto {0}", "Carpeta para la tecla {0}", "Dossier pour la touche {0}",
            "Pasta para a tecla {0}", "المجلد الخاص بالمفتاح {0}", "कुंजी {0} के लिए फ़ोल्डर",
            "কী {0}-এর ফোল্ডার", "کلید {0} کا فولڈر", "按键 {0} 对应的文件夹")
        Add("Не выбрана",
            "Not chosen", "Не вибрано", "Nicht gewählt",
            "Non scelta", "Sin elegir", "Non choisi",
            "Não escolhida", "غير محددة", "चुना नहीं गया",
            "বেছে নেওয়া হয়নি", "منتخب نہیں", "未选择")
        Add("Остальные девять папок задаются в меню папок.",
            "The other nine folders are set in the folder menu.", "Решту дев'ять папок задають у меню папок.", "Die übrigen neun Ordner werden im Ordnermenü festgelegt.",
            "Le altre nove cartelle si impostano dal menu delle cartelle.", "Las otras nueve carpetas se configuran en el menú de carpetas.", "Les neuf autres dossiers se définissent dans le menu des dossiers.",
            "As outras nove pastas definem-se no menu de pastas.", "تُضبط المجلدات التسعة الأخرى من قائمة المجلدات.", "बाकी नौ फ़ोल्डर फ़ोल्डर मेनू से तय होते हैं।",
            "বাকি নয়টি ফোল্ডার ফোল্ডার মেনু থেকে ঠিক করা হয়।", "باقی نو فولڈر فولڈر مینو سے طے ہوتے ہیں۔", "其余九个文件夹在文件夹菜单中设置。")
        Add("Выберите папку для клавиши {0}",
            "Choose a folder for key {0}", "Оберіть папку для клавіші {0}", "Wählen Sie einen Ordner für Taste {0}",
            "Scegli una cartella per il tasto {0}", "Elija una carpeta para la tecla {0}", "Choisissez un dossier pour la touche {0}",
            "Escolha uma pasta para a tecla {0}", "اختر مجلداً للمفتاح {0}", "कुंजी {0} के लिए एक फ़ोल्डर चुनें",
            "কী {0}-এর জন্য একটি ফোল্ডার বেছে নিন", "کلید {0} کے لیے ایک فولڈر چنیں", "为按键 {0} 选择一个文件夹")

        ' --- step 4: delete safety --------------------------------------------------------------
        Add("Del отправляет файл в Корзину, откуда его можно восстановить. Shift+Del удаляет окончательно.",
            "Del sends the file to the Recycle Bin, where you can restore it. Shift+Del deletes it for good.", "Del надсилає файл до Кошика, звідки його можна відновити. Shift+Del видаляє остаточно.", "Entf verschiebt die Datei in den Papierkorb, von wo Sie sie wiederherstellen können. Umschalt+Entf löscht endgültig.",
            "Canc sposta il file nel Cestino, da dove puoi ripristinarlo. Maiusc+Canc lo elimina definitivamente.", "Supr envía el archivo a la Papelera, desde donde puede restaurarlo. Mayús+Supr lo elimina para siempre.", "Suppr envoie le fichier à la Corbeille, d'où vous pouvez le restaurer. Maj+Suppr le supprime définitivement.",
            "Del envia o ficheiro para o Lixo, de onde pode restaurá-lo. Shift+Del elimina-o de vez.", "يرسل Del الملف إلى سلة المحذوفات حيث يمكن استعادته. ويحذفه Shift+Del نهائياً.", "Del फ़ाइल को रीसायकल बिन में भेजता है, जहाँ से उसे वापस लाया जा सकता है। Shift+Del उसे हमेशा के लिए मिटा देता है।",
            "Del ফাইলটিকে রিসাইকেল বিনে পাঠায়, সেখান থেকে ফেরত আনা যায়। Shift+Del একেবারে মুছে দেয়।", "Del فائل کو ری سائیکل بن میں بھیجتا ہے، جہاں سے اسے واپس لایا جا سکتا ہے۔ Shift+Del اسے مستقل طور پر مٹا دیتا ہے۔", "Del 会把文件移到回收站，之后可以还原。Shift+Del 则永久删除。")
        Add("Ошибка при сортировке не должна стоить файла. Корзина - страховка, которая есть почти всегда.",
            "A wrong press while sorting should not cost you a file. The Recycle Bin is a safety net that is almost always there.", "Помилка під час сортування не повинна коштувати файлу. Кошик - страховка, яка є майже завжди.", "Ein falscher Tastendruck beim Sortieren sollte Sie keine Datei kosten. Der Papierkorb ist ein Sicherheitsnetz, das fast immer vorhanden ist.",
            "Un tasto premuto per sbaglio durante lo smistamento non dovrebbe costarti un file. Il Cestino è una rete di sicurezza che quasi sempre c'è.", "Una pulsación equivocada al clasificar no debería costarle un archivo. La Papelera es una red de seguridad que casi siempre está.", "Un appui malheureux pendant le tri ne devrait pas vous coûter un fichier. La Corbeille est un filet de sécurité presque toujours présent.",
            "Um carregar errado ao organizar não deve custar-lhe um ficheiro. O Lixo é uma rede de segurança que quase sempre existe.", "لا ينبغي أن تكلّفك ضغطة خاطئة أثناء الفرز ملفاً. سلة المحذوفات شبكة أمان موجودة في الغالب دائماً.", "सॉर्ट करते समय गलत दबाने से कोई फ़ाइल नहीं जानी चाहिए। रीसायकल बिन एक सुरक्षा जाल है, जो लगभग हमेशा मौजूद रहता है।",
            "বাছাই করার সময় ভুল চাপে একটি ফাইল যাওয়া উচিত নয়। রিসাইকেল বিন এমন একটি নিরাপত্তা জাল যা প্রায় সব সময়ই থাকে।", "ترتیب دیتے وقت غلط دبانے سے کوئی فائل نہیں جانی چاہیے۔ ری سائیکل بن ایک حفاظتی جال ہے جو تقریباً ہمیشہ موجود ہوتا ہے۔", "分拣时误按不应让您丢失文件。回收站是几乎总是存在的安全网。")
        Add("Если на диске нет Корзины (сетевой диск, флешка), программа скажет об этом до удаления.",
            "If a drive has no Recycle Bin, such as a network share or a memory stick, the viewer says so before it deletes.", "Якщо на диску немає Кошика (мережева папка, флешка), програма скаже про це до видалення.", "Hat ein Laufwerk keinen Papierkorb, etwa eine Netzfreigabe oder ein USB-Stick, sagt das Programm es vor dem Löschen.",
            "Se un disco non ha il Cestino, come una condivisione di rete o una chiavetta, il programma lo dice prima di eliminare.", "Si una unidad no tiene Papelera, como un recurso de red o una memoria USB, el programa lo indica antes de borrar.", "Si un lecteur n'a pas de Corbeille, comme un partage réseau ou une clé USB, le programme le signale avant de supprimer.",
            "Se uma unidade não tiver Lixo, como uma partilha de rede ou uma pen, o programa diz-lho antes de eliminar.", "إذا لم يكن في القرص سلة محذوفات، كمشاركة شبكية أو ذاكرة USB، يخبرك البرنامج بذلك قبل الحذف.", "यदि किसी ड्राइव में रीसायकल बिन नहीं है, जैसे नेटवर्क शेयर या मेमोरी स्टिक, तो प्रोग्राम मिटाने से पहले बता देता है।",
            "কোনো ড্রাইভে রিসাইকেল বিন না থাকলে, যেমন নেটওয়ার্ক শেয়ার বা মেমোরি স্টিক, মোছার আগে প্রোগ্রাম সেটা জানিয়ে দেয়।", "اگر کسی ڈرائیو میں ری سائیکل بن نہ ہو، جیسے نیٹ ورک شیئر یا میموری اسٹک، تو پروگرام حذف کرنے سے پہلے بتا دیتا ہے۔", "如果某个驱动器没有回收站（例如网络共享或U盘），程序会在删除前提示。")
        ' Spec 211 §4.4: the recap's line for the delete choice, one sentence per state.
        Add("Файлы после Del уходят в Корзину.",
            "After Del, files go to the Recycle Bin.", "Після Del файли потрапляють до Кошика.", "Nach Entf wandern Dateien in den Papierkorb.",
            "Dopo Canc, i file finiscono nel Cestino.", "Tras Supr, los archivos van a la Papelera.", "Après Suppr, les fichiers vont à la Corbeille.",
            "Depois de Del, os ficheiros vão para o Lixo.", "بعد Del، تذهب الملفات إلى سلة المحذوفات.", "Del के बाद फ़ाइलें रीसायकल बिन में जाती हैं।",
            "Del-এর পরে ফাইল রিসাইকেল বিনে যায়।", "Del کے بعد فائلیں ری سائیکل بن میں جاتی ہیں۔", "按 Del 后，文件进入回收站。")
        Add("Файлы после Del удаляются окончательно.",
            "After Del, files are deleted for good.", "Після Del файли видаляються остаточно.", "Nach Entf werden Dateien endgültig gelöscht.",
            "Dopo Canc, i file vengono eliminati per sempre.", "Tras Supr, los archivos se eliminan para siempre.", "Après Suppr, les fichiers sont supprimés définitivement.",
            "Depois de Del, os ficheiros são eliminados de vez.", "بعد Del، تُحذف الملفات نهائياً.", "Del के बाद फ़ाइलें हमेशा के लिए मिट जाती हैं।",
            "Del-এর পরে ফাইল চিরতরে মুছে যায়।", "Del کے بعد فائلیں مستقل طور پر حذف ہو جاتی ہیں۔", "按 Del 后，文件被永久删除。")

        ' --- step 5: associations ---------------------------------------------------------------
        Add("Двойной щелчок по картинке в Проводнике откроет её в этой программе.",
            "A double-click on a picture in Explorer opens it in this viewer.", "Подвійний клік на зображенні у Провіднику відкриє його в цій програмі.", "Ein Doppelklick auf ein Bild im Explorer öffnet es in diesem Programm.",
            "Un doppio clic su un'immagine in Esplora risorse la apre in questo programma.", "Un doble clic en una imagen del Explorador la abre en este programa.", "Un double-clic sur une image dans l'Explorateur l'ouvre dans ce programme.",
            "Um duplo clique numa imagem no Explorador abre-a neste programa.", "النقر المزدوج على صورة في مستكشف الملفات يفتحها في هذا البرنامج.", "एक्सप्लोरर में किसी तस्वीर पर डबल-क्लिक करने से वह इसी प्रोग्राम में खुलती है।",
            "এক্সপ্লোরারে কোনো ছবিতে ডাবল-ক্লিক করলে সেটি এই প্রোগ্রামে খোলে।", "ایکسپلورر میں کسی تصویر پر ڈبل کلک کرنے سے وہ اسی پروگرام میں کھلتی ہے۔", "在资源管理器中双击图片，会在本程序中打开它。")
        Add("Файловые связи меняют то, что делает Проводник, поэтому без вашего согласия ничего не меняется.",
            "File associations change what Explorer does, so nothing changes without your yes.", "Прив'язки файлів змінюють дії Провідника, тож без вашої згоди нічого не зміниться.", "Dateizuordnungen ändern das Verhalten des Explorers, daher ändert sich ohne Ihre Zustimmung nichts.",
            "Le associazioni dei file cambiano il comportamento di Esplora risorse, quindi senza il tuo consenso non cambia nulla.", "Las asociaciones de archivos cambian lo que hace el Explorador, así que sin su permiso no cambia nada.", "Les associations de fichiers modifient le comportement de l'Explorateur, donc rien ne change sans votre accord.",
            "As associações de ficheiros mudam o que o Explorador faz, por isso nada muda sem o seu sim.", "تغيّر ارتباطات الملفات ما يفعله مستكشف الملفات، لذا لا يتغير شيء دون موافقتك.", "फ़ाइल संबंध बदलते हैं कि एक्सप्लोरर क्या करता है, इसलिए आपकी हामी के बिना कुछ नहीं बदलता।",
            "ফাইল সংযোগ এক্সপ্লোরারের আচরণ বদলায়, তাই আপনার সম্মতি ছাড়া কিছুই বদলায় না।", "فائل ایسوسی ایشن ایکسپلورر کے کام کرنے کا انداز بدلتی ہیں، اس لیے آپ کی ہاں کے بغیر کچھ نہیں بدلتا۔", "文件关联会改变资源管理器的行为，因此未经您同意不会有任何改变。")
        Add("Если включить, связи записываются только для вашей учётной записи и не требуют прав администратора. Изменить это можно в настройках.",
            "If you switch it on, the associations are written for your account only and need no administrator rights. You can change this in the settings.", "Якщо увімкнути, прив'язки записуються лише для вашого облікового запису і не потребують прав адміністратора. Змінити це можна в налаштуваннях.", "Wenn Sie es einschalten, werden die Zuordnungen nur für Ihr Konto geschrieben und benötigen keine Administratorrechte. Sie können dies in den Einstellungen ändern.",
            "Se lo attivi, le associazioni vengono scritte solo per il tuo account e non richiedono privilegi di amministratore. Puoi cambiarlo nelle impostazioni.", "Si lo activa, las asociaciones se escriben solo para su cuenta y no requieren derechos de administrador. Puede cambiarlo en los ajustes.", "Si vous l'activez, les associations ne sont écrites que pour votre compte et ne demandent aucun droit d'administrateur. Vous pouvez le modifier dans les réglages.",
            "Se o ativar, as associações são escritas apenas para a sua conta e não exigem direitos de administrador. Pode alterá-lo nas definições.", "إذا فعّلته، تُكتب الارتباطات لحسابك فقط ولا تحتاج إلى صلاحيات المسؤول. يمكنك تغيير ذلك في الإعدادات.", "अगर आप इसे चालू करते हैं, तो संबंध केवल आपके खाते के लिए लिखे जाते हैं और व्यवस्थापक अधिकार नहीं चाहिए। इसे सेटिंग्स में बदल सकते हैं।",
            "আপনি চালু করলে সংযোগগুলি শুধু আপনার অ্যাকাউন্টের জন্য লেখা হয় এবং প্রশাসক অধিকারের প্রয়োজন হয় না। সেটিংসে এটি বদলাতে পারেন।", "اگر آپ اسے آن کریں تو ایسوسی ایشنز صرف آپ کے اکاؤنٹ کے لیے لکھی جاتی ہیں اور ایڈمنسٹریٹر اجازت درکار نہیں ہوتی۔ سیٹنگز میں اسے بدل سکتے ہیں۔", "开启后，关联只写入当前用户账户，无需管理员权限。可在设置中更改。")
        Add("Открывать картинки в этой программе",
            "Open pictures in this viewer", "Відкривати зображення в цій програмі", "Bilder in diesem Programm öffnen",
            "Apri le immagini in questo programma", "Abrir las imágenes en este programa", "Ouvrir les images dans ce programme",
            "Abrir imagens neste programa", "افتح الصور في هذا البرنامج", "तस्वीरें इसी प्रोग्राम में खोलें",
            "এই প্রোগ্রামে ছবি খুলুন", "تصاویر اس پروگرام میں کھولیں", "用本程序打开图片")

        ' --- step 6: finish ---------------------------------------------------------------------
        Add("Выбранное сохранено. Всё остальное осталось по умолчанию.",
            "What you chose is saved. Everything else keeps its default.", "Вибране збережено. Решта лишилася за замовчуванням.", "Ihre Auswahl ist gespeichert. Alles andere bleibt auf dem Standardwert.",
            "Quello che hai scelto è salvato. Tutto il resto resta al valore predefinito.", "Lo que eligió está guardado. Lo demás conserva su valor predeterminado.", "Votre choix est enregistré. Le reste garde sa valeur par défaut.",
            "O que escolheu está guardado. Tudo o resto mantém o valor predefinido.", "تم حفظ ما اخترته. كل شيء آخر يبقى على قيمته الافتراضية.", "आपकी पसंद सहेजी गई है। बाकी सब डिफ़ॉल्ट पर ही रहता है।",
            "আপনার বেছে নেওয়া সংরক্ষিত হয়েছে। বাকি সব আগের ডিফল্টেই থাকে।", "آپ کا انتخاب محفوظ ہو گیا ہے۔ باقی سب اپنی ڈیفالٹ قدر پر رہتا ہے۔", "您的选择已保存，其余设置保持默认。")
        Add("Эти шаги - всё, что нужно понять новичку. Остальные параметры собраны в окне настроек, где работает поиск.",
            "These steps are all a newcomer needs to understand. The other settings are in the settings window, where search works.", "Ці кроки - усе, що має зрозуміти новачок. Решта параметрів зібрана у вікні налаштувань, де працює пошук.", "Diese Schritte sind alles, was ein Neuling verstehen muss. Die übrigen Einstellungen stehen im Einstellungsfenster, in dem die Suche funktioniert.",
            "Questi passaggi sono tutto ciò che serve capire a un principiante. Le altre impostazioni sono nella finestra delle impostazioni, dove funziona la ricerca.", "Estos pasos son todo lo que necesita entender un principiante. El resto de ajustes está en la ventana de ajustes, donde funciona la búsqueda.", "Ces étapes sont tout ce qu'un débutant doit comprendre. Les autres réglages sont dans la fenêtre des réglages, où la recherche fonctionne.",
            "Estes passos são tudo o que um principiante precisa de perceber. As outras definições estão na janela de definições, onde a pesquisa funciona.", "هذه الخطوات هي كل ما يحتاج المبتدئ إلى فهمه. بقية الإعدادات في نافذة الإعدادات حيث يعمل البحث.", "नए उपयोगकर्ता को बस ये चरण समझने होते हैं। बाकी सेटिंग्स सेटिंग्स विंडो में हैं, जहाँ खोज काम करती है।",
            "নতুন ব্যবহারকারীর এই ধাপগুলোই বোঝা দরকার। বাকি সেটিংস সেটিংস উইন্ডোতে আছে, যেখানে খোঁজ কাজ করে।", "نئے صارف کو بس یہی مراحل سمجھنے ہیں۔ باقی سیٹنگز سیٹنگز ونڈو میں ہیں، جہاں تلاش کام کرتی ہے۔", "这几步就是新用户需要了解的全部内容。其余设置都在设置窗口中，那里可以搜索。")
        Add("Откройте папку, чтобы начать. Окно настроек и F1 вернут эту справку в любой момент.",
            "Open a folder to start. The settings window and F1 bring this help back at any time.", "Відкрийте папку, щоб почати. Вікно налаштувань і F1 повернуть цю довідку будь-коли.", "Öffnen Sie einen Ordner, um zu beginnen. Das Einstellungsfenster und F1 holen diese Hilfe jederzeit zurück.",
            "Apri una cartella per iniziare. La finestra delle impostazioni e F1 riportano questo aiuto in qualsiasi momento.", "Abra una carpeta para empezar. La ventana de ajustes y F1 muestran esta ayuda en cualquier momento.", "Ouvrez un dossier pour commencer. La fenêtre des réglages et F1 rappellent cette aide à tout moment.",
            "Abra uma pasta para começar. A janela de definições e a tecla F1 trazem esta ajuda de volta a qualquer momento.", "افتح مجلداً للبدء. تعيد نافذة الإعدادات ومفتاح F1 هذه المساعدة في أي وقت.", "किसी फ़ोल्डर को खोलकर शुरू करें। सेटिंग्स विंडो और F1 यह मदद कभी भी वापस ला देते हैं।",
            "শুরু করতে একটি ফোল্ডার খুলুন। সেটিংস উইন্ডো ও F1 যেকোনো সময় এই সাহায্য ফিরিয়ে আনে।", "شروع کرنے کے لیے ایک فولڈر کھولیں۔ سیٹنگز ونڈو اور F1 یہ مدد کسی بھی وقت واپس لے آتے ہیں۔", "打开一个文件夹即可开始。设置窗口和 F1 随时可以再次显示此帮助。")

        ' --- the fourth part: where to change it later (spec 211 §4.1) ----------------------------
        ' One template per step; the {0}/{1} slots take the settings paths the form composes from the very keys the
        ' settings window renders. "в меню папок программы" names the one live surface that is not a settings row.
        Add("Позже меняется здесь: {0}.",
            "You can change it later here: {0}.", "Пізніше змінюється тут: {0}.", "Später können Sie es hier ändern: {0}.",
            "Puoi cambiarlo più avanti qui: {0}.", "Puede cambiarlo más adelante aquí: {0}.", "Vous pourrez le modifier ici : {0}.",
            "Pode mudá-lo mais tarde aqui: {0}.", "يمكنك تغييره لاحقاً من هنا: {0}.", "इसे बाद में यहाँ बदलें: {0}.",
            "পরে এখান থেকে বদলাতে পারবেন: {0}.", "بعد میں اسے یہاں سے بدلیں: {0}.", "以后可在这里修改：{0}。")
        Add("Позже меняется здесь: {0} и {1}.",
            "You can change them later here: {0} and {1}.", "Пізніше змінюється тут: {0} і {1}.", "Später können Sie beides hier ändern: {0} und {1}.",
            "Puoi cambiarli più avanti qui: {0} e {1}.", "Puede cambiarlos más adelante aquí: {0} y {1}.", "Vous pourrez les modifier ici : {0} et {1}.",
            "Pode mudá-los mais tarde aqui: {0} e {1}.", "يمكنك تغييرهما لاحقاً من هنا: {0} و{1}.", "दोनों को बाद में यहाँ बदलें: {0} और {1}.",
            "দুটোই পরে এখান থেকে বদলাতে পারবেন: {0} এবং {1}.", "دونوں بعد میں یہاں سے بدلیں: {0} اور {1}.", "以后可在这里修改：{0}和{1}。")
        Add("Фон - {0}; колесо - {1}.",
            "Background - {0}; wheel - {1}.", "Фон - {0}; колесо - {1}.", "Hintergrund - {0}; Mausrad - {1}.",
            "Sfondo - {0}; rotella - {1}.", "Fondo - {0}; rueda - {1}.", "Fond - {0} ; molette - {1}.",
            "Fundo - {0}; roda - {1}.", "الخلفية - {0}؛ والعجلة - {1}.", "पृष्ठभूमि - {0}; व्हील - {1}.",
            "পটভূমি - {0}; চাকা - {1}.", "پس منظر - {0}؛ وہیل - {1}.", "背景 - {0}；滚轮 - {1}。")
        Add("Папки назначаются {0}; переход после копирования - {1}.",
            "Folders are assigned {0}; moving on after copying - {1}.", "Папки призначаються {0}; перехід після копіювання - {1}.", "Ordner werden {0} festgelegt; der Wechsel nach dem Kopieren - {1}.",
            "Le cartelle si assegnano {0}; il passaggio dopo la copia - {1}.", "Las carpetas se asignan {0}; pasar a la siguiente tras copiar - {1}.", "Les dossiers s'attribuent {0} ; le passage au suivant après une copie - {1}.",
            "As pastas atribuem-se {0}; avançar após copiar - {1}.", "تُحدَّد المجلدات {0}؛ والانتقال بعد النسخ - {1}.", "फ़ोल्डर तय होते हैं {0}; कॉपी के बाद आगे बढ़ना - {1}.",
            "ফোল্ডার ঠিক হয় {0}; কপির পরে এগিয়ে যাওয়া - {1}.", "فولڈر طے ہوتے ہیں {0}؛ کاپی کے بعد آگے بڑھنا - {1}.", "文件夹{0}指定；复制后跳到下一个 - {1}。")
        Add("Вернуть эти шаги: {0} или клавиша F1.",
            "Bring these steps back: {0}, or the F1 key.", "Повернути ці кроки: {0} або клавіша F1.", "Diese Schritte zurückholen: {0}, oder die Taste F1.",
            "Riportare questi passaggi: {0}, oppure il tasto F1.", "Volver a mostrar estos pasos: {0}, o la tecla F1.", "Revenir à ces étapes : {0}, ou la touche F1.",
            "Retomar estes passos: {0}, ou a tecla F1.", "إعادة هذه الخطوات: {0}، أو مفتاح F1.", "ये चरण वापस लाएँ: {0}, या F1 कुंजी।",
            "এই ধাপগুলো ফিরিয়ে আনুন: {0}, বা F1 কী।", "یہ مراحل دوبارہ لائیں: {0}، یا F1 کلید۔", "再次显示这些步骤：{0}或 F1 键。")
        Add("в меню папок программы",
            "in the viewer's folder menu", "у меню папок програми", "im Ordnermenü des Programms",
            "nel menu delle cartelle del programma", "en el menú de carpetas del programa", "dans le menu des dossiers du programme",
            "no menu de pastas do programa", "في قائمة مجلدات البرنامج", "प्रोग्राम के फ़ोल्डर मेनू में",
            "প্রোগ্রামের ফোল্ডার মেনুতে", "پروگرام کے فولڈر مینو میں", "在程序的文件夹菜单中")

        ' --- the buttons ------------------------------------------------------------------------
        Add("Пропустить настройку",
            "Skip setup", "Пропустити налаштування", "Einrichtung überspringen",
            "Salta la configurazione", "Omitir configuración", "Passer la configuration",
            "Ignorar configuração", "تخطّي الإعداد", "सेटअप छोड़ें",
            "সেটআপ এড়িয়ে যান", "سیٹ اپ چھوڑیں", "跳过设置")
        Add("Назад",
            "Back", "Назад", "Zurück",
            "Indietro", "Atrás", "Précédent",
            "Voltar", "رجوع", "पीछे",
            "পিছনে", "پیچھے", "返回")
        Add("Далее",
            "Next", "Далі", "Weiter",
            "Avanti", "Siguiente", "Suivant",
            "Avançar", "التالي", "आगे",
            "পরবর্তী", "آگے", "下一步")
        Add("Готово",
            "Finish", "Готово", "Fertig",
            "Fine", "Finalizar", "Terminer",
            "Concluir", "إنهاء", "समाप्त",
            "শেষ", "ختم", "完成")

        ' --- the settings row and the folder that vanished before Next ----------------------------
        Add("Показать приветственные шаги снова",
            "Show the welcome steps again", "Показати привітальні кроки знову", "Die Willkommensschritte erneut anzeigen",
            "Mostra di nuovo i passaggi di benvenuto", "Mostrar otra vez los pasos de bienvenida", "Afficher à nouveau les étapes d'accueil",
            "Mostrar novamente os passos de boas-vindas", "إظهار خطوات الترحيب مجدداً", "स्वागत के चरण फिर से दिखाएँ",
            "স্বাগত ধাপগুলো আবার দেখান", "خوش آمدید کے مراحل دوبارہ دکھائیں", "再次显示欢迎步骤")
        Add("Открывает пошаговую настройку: язык, фон, сортировка, удаление и связи файлов.",
            "Opens the step-by-step setup: language, background, sorting, deletion and file associations.", "Відкриває покрокове налаштування: мова, фон, сортування, видалення та прив'язки файлів.", "Öffnet die schrittweise Einrichtung: Sprache, Hintergrund, Sortierung, Löschen und Dateizuordnungen.",
            "Apre la configurazione passo passo: lingua, sfondo, smistamento, eliminazione e associazioni dei file.", "Abre la configuración paso a paso: idioma, fondo, clasificación, borrado y asociaciones de archivos.", "Ouvre la configuration pas à pas : langue, fond, tri, suppression et associations de fichiers.",
            "Abre a configuração passo a passo: idioma, fundo, organização, eliminação e associações de ficheiros.", "يفتح الإعداد خطوة بخطوة: اللغة والخلفية والفرز والحذف وارتباطات الملفات.", "चरण-दर-चरण सेटअप खुलता है: भाषा, पृष्ठभूमि, छँटाई, मिटाना और फ़ाइल संबंध।", "ধাপে ধাপে সেটআপ খোলে: ভাষা, পটভূমি, বাছাই, মোছা ও ফাইল সংযোগ।",
            "مرحلہ وار سیٹ اپ کھولتا ہے: زبان، پس منظر، ترتیب، حذف اور فائل ایسوسی ایشنز۔", "打开分步设置：语言、背景、分拣、删除和文件关联。")
        Add("Папка больше не доступна. Выберите другую.",
            "That folder is no longer available. Choose another one.", "Тека більше недоступна. Оберіть іншу.", "Dieser Ordner ist nicht mehr verfügbar. Wählen Sie einen anderen.",
            "La cartella non è più disponibile. Sceglierne un'altra.", "La carpeta ya no está disponible. Elija otra.", "Ce dossier n'est plus disponible. Choisissez-en un autre.",
            "Esta pasta já não está disponível. Escolha outra.", "لم يعد هذا المجلد متاحاً. اختر مجلداً آخر.", "यह फ़ोल्डर अब उपलब्ध नहीं है। कोई और चुनें।",
            "এই ফোল্ডারটি আর পাওয়া যাচ্ছে না। অন্যটি বেছে নিন।", "یہ فولڈر اب دستیاب نہیں۔ کوئی اور چنیں۔", "该文件夹已不可用，请选择其他文件夹。")

        ' --- the finish screen's installed-servers block (spec 215) ---------------------------------
        ' The Media Exchange Server's own name is never a key: it is Latin in every language (ProductNames.ExchangeServer).
        Add("Серверы на этом ПК",
            "Servers on this PC", "Сервери на цьому ПК", "Server auf diesem PC",
            "Server su questo PC", "Servidores en este PC", "Serveurs sur ce PC",
            "Servidores neste PC", "الخوادم على هذا الكمبيوتر", "इस पीसी पर सर्वर",
            "এই পিসির সার্ভার", "اس پی سی پر سرورز", "此电脑上的服务器")
        Add("Сервер папок для Android",
            "Folder server for Android", "Сервер папок для Android", "Ordnerserver für Android",
            "Server delle cartelle per Android", "Servidor de carpetas para Android", "Serveur de dossiers pour Android",
            "Servidor de pastas para Android", "خادم المجلدات لجهاز Android", "Android के लिए फ़ोल्डर सर्वर",
            "Android-এর জন্য ফোল্ডার সার্ভার", "Android کے لیے فولڈر سرور", "面向 Android 的文件夹服务器")
        Add("Раздаёт папки этого ПК на телефон. Управляется в Share Manager.",
            "Shares this PC's folders to your phone. Managed in Share Manager.", "Роздає папки цього ПК на телефон. Керується в Share Manager.", "Gibt die Ordner dieses PCs für das Telefon frei. Wird im Share Manager verwaltet.",
            "Condivide le cartelle di questo PC con il telefono. Si gestisce in Share Manager.", "Comparte las carpetas de este PC con el teléfono. Se administra en Share Manager.", "Partage les dossiers de ce PC avec le téléphone. Se gère dans Share Manager.",
            "Partilha as pastas deste PC com o telemóvel. Gerido no Share Manager.", "يشارك مجلدات هذا الكمبيوتر مع الهاتف. تتم إدارته في Share Manager.", "इस पीसी के फ़ोल्डर फ़ोन को साझा करता है। Share Manager में प्रबंधित होता है।",
            "এই পিসির ফোল্ডার ফোনে শেয়ার করে। Share Manager-এ পরিচালিত হয়।", "اس پی سی کے فولڈر فون کے ساتھ شیئر کرتا ہے۔ Share Manager میں منظم ہوتا ہے۔", "将此电脑的文件夹共享给手机。在 Share Manager 中管理。")
        Add("Собственный сервер обмена этого ПК. Управляется в Share Manager.",
            "This PC's own exchange server. Managed in Share Manager.", "Власний сервер обміну цього ПК. Керується в Share Manager.", "Der eigene Austauschserver dieses PCs. Wird im Share Manager verwaltet.",
            "Il server di scambio di questo PC. Si gestisce in Share Manager.", "El servidor de intercambio de este PC. Se administra en Share Manager.", "Le serveur d'échange de ce PC. Se gère dans Share Manager.",
            "O servidor de troca deste PC. Gerido no Share Manager.", "خادم التبادل الخاص بهذا الكمبيوتر. تتم إدارته في Share Manager.", "इस पीसी का अपना एक्सचेंज सर्वर। Share Manager में प्रबंधित होता है।",
            "এই পিসির নিজস্ব এক্সচেঞ্জ সার্ভার। Share Manager-এ পরিচালিত হয়।", "اس پی سی کا اپنا ایکسچینج سرور۔ Share Manager میں منظم ہوتا ہے۔", "此电脑自己的交换服务器。在 Share Manager 中管理。")
        Add("Работает",
            "Running", "Працює", "Läuft",
            "In esecuzione", "En marcha", "En cours",
            "A funcionar", "يعمل", "चल रहा है",
            "চলছে", "چل رہا ہے", "正在运行")
        Add("Не запущен - менеджер включит его по вашему выбору",
            "Not running - the manager will start it if you choose", "Не запущено - менеджер увімкне його за вашим вибором", "Nicht gestartet - der Manager startet ihn auf Ihren Wunsch",
            "Non avviato - il gestore lo avvierà a vostra scelta", "No iniciado - el administrador lo iniciará si usted lo decide", "Non démarré - le gestionnaire le démarrera si vous le choisissez",
            "Não iniciado - o gestor inicia-o por sua escolha", "غير قيد التشغيل - سيشغّله المدير بحسب اختيارك", "चालू नहीं है - प्रबंधक आपकी पसंद पर इसे शुरू करेगा",
            "চালু নেই - আপনার পছন্দে ম্যানেজার এটি চালু করবে", "چل نہیں رہا - مینیجر آپ کے انتخاب پر اسے شروع کرے گا", "未运行 - 管理器会按您的选择启动它")
        Add("Открыть менеджер",
            "Open the manager", "Відкрити менеджер", "Manager öffnen",
            "Apri il gestore", "Abrir el administrador", "Ouvrir le gestionnaire",
            "Abrir o gestor", "فتح المدير", "प्रबंधक खोलें",
            "ম্যানেজার খুলুন", "مینیجر کھولیں", "打开管理器")
        Add("Добавить ресурс",
            "Add a resource", "Додати ресурс", "Ressource hinzufügen",
            "Aggiungi una risorsa", "Añadir un recurso", "Ajouter une ressource",
            "Adicionar um recurso", "إضافة مورد", "संसाधन जोड़ें",
            "রিসোর্স যোগ করুন", "وسیلہ شامل کریں", "添加资源")
    End Sub

End Class
