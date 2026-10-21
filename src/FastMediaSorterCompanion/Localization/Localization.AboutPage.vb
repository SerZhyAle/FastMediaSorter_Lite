Option Explicit On
Option Strict On
Option Infer On
Option Compare Binary

Partial Public NotInheritable Class Localization
    ''' <summary>
    ''' The About group of the Share Manager's "Справка" page (ticket 243 T-6, SETTINGS-NAVIGATION 2.0 section 3 rules 3 and 4):
    ''' the description, the update statement, the help, project and legal links and the related projects. The wording of the
    ''' shared items is the viewer's (src/Localization/Localization.SettingsHints.vb and .SettingsDesc.vb), copied column for
    ''' column, so the two About pages say the same thing in every language. Nothing here is fetched: the rows are labels and
    ''' links, and an address is opened on a click only.
    ''' </summary>
    Private Shared Sub AddAboutPageStrings()
        Add("Раздаёт папки этого компьютера Android-телефону, подключается к серверу обмена медиа и по расписанию выполняет операции с файлами.",
            "Shares this computer's folders with an Android phone, connects to the media exchange server and runs file operations on a schedule.",
            "Роздає папки цього комп'ютера Android-телефону, підключається до сервера обміну медіа та за розкладом виконує операції з файлами.",
            "Gibt Ordner dieses Computers für ein Android-Telefon frei, verbindet sich mit dem Medienaustausch-Server und führt Dateivorgänge nach Zeitplan aus.",
            "Condivide le cartelle di questo computer con un telefono Android, si collega al server di scambio multimediale ed esegue operazioni sui file in base a una pianificazione.",
            "Comparte las carpetas de este equipo con un teléfono Android, se conecta al servidor de intercambio de medios y ejecuta operaciones con archivos según un horario.",
            "Partage les dossiers de cet ordinateur avec un téléphone Android, se connecte au serveur d'échange de médias et exécute des opérations sur les fichiers selon un calendrier.",
            "Partilha as pastas deste computador com um telemóvel Android, liga-se ao servidor de troca de multimédia e executa operações com ficheiros de acordo com um horário.",
            "يشارك مجلدات هذا الكمبيوتر مع هاتف Android، ويتصل بخادم تبادل الوسائط، وينفّذ عمليات الملفات وفق جدول زمني.",
            "इस कंप्यूटर के फ़ोल्डर Android फ़ोन के साथ साझा करता है, मीडिया आदान-प्रदान सर्वर से जुड़ता है और शेड्यूल के अनुसार फ़ाइल कार्य चलाता है।",
            "এই কম্পিউটারের ফোল্ডার Android ফোনের সঙ্গে শেয়ার করে, মিডিয়া বিনিময় সার্ভারের সঙ্গে যুক্ত হয় এবং নির্ধারিত সময়সূচি অনুযায়ী ফাইলের কাজ চালায়।",
            "اس کمپیوٹر کے فولڈر Android فون کے ساتھ شیئر کرتا ہے، میڈیا تبادلہ سرور سے جڑتا ہے اور شیڈول کے مطابق فائل آپریشن چلاتا ہے۔",
            "与 Android 手机共享此电脑的文件夹，连接媒体交换服务器，并按计划执行文件操作。")
        Add("Новые версии", "Releases", "Нові версії", "Veröffentlichungen", "Versioni", "Versiones", "Versions", "Versões", "الإصدارات", "रिलीज़", "রিলিজ", "ریلیزز", "版本发布")
        Add("Установлена версия {0}. Программа сама не проверяет обновления и ничего не скачивает: новые версии выходят на странице релизов GitHub, в winget и в Microsoft Store.",
            "Version {0} is installed. The program does not check for updates by itself and downloads nothing: new versions are published on the GitHub releases page, in winget and in the Microsoft Store.",
            "Встановлено версію {0}. Програма сама не перевіряє оновлення й нічого не завантажує: нові версії виходять на сторінці релізів GitHub, у winget і в Microsoft Store.",
            "Installiert ist Version {0}. Das Programm prüft nicht selbst auf Updates und lädt nichts herunter: Neue Versionen erscheinen auf der GitHub-Releases-Seite, in winget und im Microsoft Store.",
            "È installata la versione {0}. Il programma non cerca aggiornamenti da solo e non scarica nulla: le nuove versioni escono nella pagina delle release di GitHub, in winget e nel Microsoft Store.",
            "Está instalada la versión {0}. El programa no busca actualizaciones por sí solo y no descarga nada: las versiones nuevas se publican en la página de versiones de GitHub, en winget y en Microsoft Store.",
            "La version {0} est installée. Le programme ne recherche pas de mises à jour de lui-même et ne télécharge rien : les nouvelles versions paraissent sur la page des versions de GitHub, dans winget et dans le Microsoft Store.",
            "A versão {0} está instalada. O programa não procura atualizações por conta própria e não baixa nada: as novas versões saem na página de versões do GitHub, no winget e na Microsoft Store.",
            "الإصدار المثبّت هو {0}. لا يتحقق البرنامج من التحديثات بنفسه ولا يُنزّل شيئًا: تصدر الإصدارات الجديدة في صفحة إصدارات GitHub وفي winget وفي Microsoft Store.",
            "संस्करण {0} स्थापित है। प्रोग्राम अपने आप अपडेट की जाँच नहीं करता और कुछ भी डाउनलोड नहीं करता: नए संस्करण GitHub के रिलीज़ पेज, winget और Microsoft Store में आते हैं।",
            "সংস্করণ {0} ইনস্টল করা আছে। প্রোগ্রাম নিজে থেকে আপডেট খোঁজে না এবং কিছুই ডাউনলোড করে না: নতুন সংস্করণ GitHub-এর রিলিজ পেজে, winget-এ এবং Microsoft Store-এ প্রকাশিত হয়।",
            "ورژن {0} نصب ہے۔ پروگرام خود اپ ڈیٹس نہیں جانچتا اور کچھ ڈاؤن لوڈ نہیں کرتا: نئے ورژن GitHub کے ریلیز صفحے، winget اور Microsoft Store میں آتے ہیں۔",
            "已安装版本 {0}。程序不会自行检查更新，也不会下载任何内容：新版本发布在 GitHub 发布页、winget 和 Microsoft Store。")
        Add("Справка по программе", "Program help", "Довідка до програми", "Hilfe zum Programm", "Guida del programma", "Ayuda del programa", "Aide du programme", "Ajuda do programa", "مساعدة البرنامج", "प्रोग्राम सहायता", "প্রোগ্রামের সহায়তা", "پروگرام کی مدد", "程序帮助")
        Add("Встроенная справка на языке интерфейса. Если её нет на этом компьютере, откроется страница с темами на сайте.",
            "Built-in help in the interface language. If it is not on this computer, the topics page on the website opens instead.",
            "Вбудована довідка мовою інтерфейсу. Якщо її немає на цьому комп'ютері, відкриється сторінка з темами на сайті.",
            "Integrierte Hilfe in der Sprache der Oberfläche. Ist sie auf diesem Computer nicht vorhanden, öffnet sich stattdessen die Themenseite der Website.",
            "Guida integrata nella lingua dell'interfaccia. Se non è presente su questo computer, si apre la pagina degli argomenti sul sito.",
            "Ayuda integrada en el idioma de la interfaz. Si no está en este equipo, se abre la página de temas del sitio web.",
            "Aide intégrée dans la langue de l'interface. Si elle n'est pas sur cet ordinateur, la page des thèmes du site s'ouvre à la place.",
            "Ajuda integrada no idioma da interface. Se não estiver neste computador, abre-se a página de tópicos do site.",
            "مساعدة مدمجة بلغة الواجهة. إذا لم تكن موجودة على هذا الكمبيوتر، تُفتح صفحة المواضيع على الموقع بدلًا منها.",
            "इंटरफ़ेस की भाषा में अंतर्निहित सहायता। यदि वह इस कंप्यूटर पर नहीं है, तो वेबसाइट का विषय पेज खुलेगा।",
            "ইন্টারফেসের ভাষায় অন্তর্নির্মিত সহায়তা। এটি এই কম্পিউটারে না থাকলে ওয়েবসাইটের বিষয়-পেজ খুলবে।",
            "انٹرفیس کی زبان میں شامل مدد۔ اگر یہ اس کمپیوٹر پر نہ ہو تو ویب سائٹ کا موضوعات کا صفحہ کھلے گا۔",
            "界面语言的内置帮助。如果此电脑上没有，将改为打开网站上的主题页面。")
        Add("Сайт проекта", "Project website", "Сайт проєкту", "Projekt-Website", "Sito del progetto", "Sitio del proyecto", "Site du projet", "Site do projeto", "موقع المشروع", "प्रोजेक्ट वेबसाइट", "প্রকল্পের ওয়েবসাইট", "پروجیکٹ ویب سائٹ", "项目网站")
        Add("Описание возможностей, инструкции и материалы проекта.", "Features, instructions and project resources.", "Опис можливостей, інструкції та матеріали проєкту.", "Funktionen, Anleitungen und Projektmaterialien.", "Funzionalità, istruzioni e materiali del progetto.", "Funciones, instrucciones y recursos del proyecto.", "Fonctionnalités, instructions et ressources du projet.", "Recursos, instruções e materiais do projeto.", "الميزات والتعليمات وموارد المشروع.", "विशेषताएँ, निर्देश और परियोजना सामग्री।", "বৈশিষ্ট্য, নির্দেশনা ও প্রকল্পের উপকরণ।", "خصوصیات، ہدایات اور پروجیکٹ کے وسائل۔", "功能介绍、使用说明与项目资料。")
        Add("Исходный код, задачи и техническая документация.", "Source code, issues and technical documentation.", "Початковий код, задачі та технічна документація.", "Quellcode, Issues und technische Dokumentation.", "Codice sorgente, issue e documentazione tecnica.", "Código fuente, incidencias y documentación técnica.", "Code source, tickets et documentation technique.", "Código-fonte, issues e documentação técnica.", "الشيفرة المصدرية والمشكلات والوثائق التقنية.", "स्रोत कोड, मुद्दे और तकनीकी दस्तावेज़।", "সোর্স কোড, ইস্যু ও কারিগরি নথি।", "سورس کوڈ، مسائل اور تکنیکی دستاویزات۔", "源代码、问题追踪与技术文档。")
        Add("Сторонние компоненты и лицензии", "Third-party components and licenses", "Сторонні компоненти та ліцензії", "Drittanbieter-Komponenten und Lizenzen", "Componenti di terze parti e licenze", "Componentes de terceros y licencias", "Composants tiers et licences", "Componentes de terceiros e licenças", "المكونات الخارجية والتراخيص", "तृतीय-पक्ष घटक और लाइसेंस", "তৃতীয় পক্ষের উপাদান ও লাইসেন্স", "تھرڈ پارٹی اجزا اور لائسنس", "第三方组件与许可证")
        Add("Используемые библиотеки и тексты их лицензий. Открывается файл рядом с программой; без него - страница на GitHub.",
            "The libraries in use and the texts of their licenses. The file next to the program opens; without it, the page on GitHub.",
            "Використані бібліотеки та тексти їхніх ліцензій. Відкривається файл поруч із програмою; без нього - сторінка на GitHub.",
            "Die verwendeten Bibliotheken und die Texte ihrer Lizenzen. Es öffnet sich die Datei neben dem Programm; fehlt sie, die Seite auf GitHub.",
            "Le librerie utilizzate e i testi delle loro licenze. Si apre il file accanto al programma; se manca, la pagina su GitHub.",
            "Las bibliotecas utilizadas y los textos de sus licencias. Se abre el archivo junto al programa; si falta, la página de GitHub.",
            "Les bibliothèques utilisées et les textes de leurs licences. Le fichier placé à côté du programme s'ouvre ; s'il manque, la page GitHub.",
            "As bibliotecas usadas e os textos das suas licenças. Abre-se o arquivo ao lado do programa; se faltar, a página no GitHub.",
            "المكتبات المستخدمة ونصوص تراخيصها. يُفتح الملف الموجود بجوار البرنامج، وإن لم يوجد فتُفتح صفحة GitHub.",
            "उपयोग की गई लाइब्रेरी और उनके लाइसेंस का पाठ। प्रोग्राम के पास की फ़ाइल खुलती है; वह न हो तो GitHub का पेज।",
            "ব্যবহৃত লাইব্রেরি ও সেগুলির লাইসেন্সের পাঠ। প্রোগ্রামের পাশের ফাইল খোলে; সেটি না থাকলে GitHub-এর পেজ।",
            "استعمال شدہ لائبریریاں اور ان کے لائسنس کا متن۔ پروگرام کے ساتھ والی فائل کھلتی ہے؛ وہ نہ ہو تو GitHub کا صفحہ۔",
            "所用的库及其许可证文本。打开程序旁边的文件；该文件不存在时，打开 GitHub 页面。")
        Add("Как приложение обрабатывает пользовательские данные.", "How the application handles user data.", "Як застосунок обробляє дані користувача.", "Wie die Anwendung mit Benutzerdaten umgeht.", "Come l'applicazione tratta i dati dell'utente.", "Cómo trata la aplicación los datos del usuario.", "Comment l'application traite les données utilisateur.", "Como o aplicativo trata os dados do usuário.", "كيف يتعامل التطبيق مع بيانات المستخدم.", "ऐप उपयोगकर्ता डेटा को कैसे संभालता है।", "অ্যাপ ব্যবহারকারীর ডেটা কীভাবে পরিচালনা করে।", "ایپ صارف کے ڈیٹا کو کیسے سنبھالتا ہے۔", "应用如何处理用户数据。")
        Add("Связаться с автором", "Contact the author", "Зв'язатися з автором", "Den Autor kontaktieren", "Contatta l'autore", "Contactar con el autor", "Contacter l'auteur", "Contatar o autor", "التواصل مع المؤلف", "लेखक से संपर्क करें", "লেখকের সাথে যোগাযোগ", "مصنف سے رابطہ کریں", "联系作者")
        Add("Написать автору проекта по электронной почте.", "Send an email to the project author.", "Написати автору проєкту електронною поштою.", "Dem Projektautor eine E-Mail schreiben.", "Invia un'e-mail all'autore del progetto.", "Enviar un correo al autor del proyecto.", "Envoyer un e-mail à l'auteur du projet.", "Enviar um e-mail ao autor do projeto.", "أرسل بريدًا إلكترونيًا لمؤلف المشروع.", "परियोजना के लेखक को ईमेल भेजें।", "প্রকল্পের লেখককে ইমেইল পাঠান।", "پروجیکٹ کے مصنف کو ای میل بھیجیں۔", "给项目作者发送电子邮件。")
        Add("Связанные проекты", "Related projects", "Пов'язані проєкти", "Verwandte Projekte", "Progetti correlati", "Proyectos relacionados", "Projets associés", "Projetos relacionados", "مشاريع ذات صلة", "संबंधित परियोजनाएँ", "সংশ্লিষ্ট প্রকল্প", "متعلقہ منصوبے", "相关项目")
        Add("Другие программы автора, которые работают вместе с этой. Страницы открываются только по щелчку.",
            "Other programs by the author that work together with this one. Pages open only when you click a link.",
            "Інші програми автора, які працюють разом із цією. Сторінки відкриваються лише після клацання.",
            "Weitere Programme des Autors, die mit diesem zusammenarbeiten. Seiten öffnen sich nur per Klick.",
            "Altri programmi dell'autore che funzionano insieme a questo. Le pagine si aprono solo con un clic.",
            "Otros programas del autor que funcionan junto con este. Las páginas se abren solo al hacer clic.",
            "D'autres programmes de l'auteur qui fonctionnent avec celui-ci. Les pages ne s'ouvrent qu'au clic.",
            "Outros programas do autor que funcionam em conjunto com este. As páginas só abrem com um clique.",
            "برامج أخرى للمؤلف تعمل مع هذا البرنامج. لا تُفتح الصفحات إلا بالنقر.",
            "लेखक के अन्य प्रोग्राम जो इसके साथ काम करते हैं। पेज केवल क्लिक करने पर खुलते हैं।",
            "লেখকের অন্য প্রোগ্রাম, যেগুলি এটির সঙ্গে কাজ করে। পেজ শুধু ক্লিক করলেই খোলে।",
            "مصنف کے دیگر پروگرام جو اس کے ساتھ کام کرتے ہیں۔ صفحات صرف کلک کرنے پر کھلتے ہیں۔",
            "作者的其他程序，可与本程序配合使用。仅在点击后才会打开页面。")
        Add("Страница приложения для Android.", "Page of the Android app.", "Сторінка застосунку для Android.", "Seite der Android-App.", "Pagina dell’app per Android.", "Página de la aplicación para Android.", "Page de l’application pour Android.", "Página do aplicativo para Android.", "صفحة تطبيق Android.", "Android ऐप का पृष्ठ।", "Android অ্যাপের পাতা।", "Android ایپ کا صفحہ۔", "Android 应用页面。")
        Add("Как открыть папки ПК и телефона на других устройствах: домашняя сеть, проброс порта, постоянный сервер.", "How to open the folders of a PC and a phone on other devices: home network, port forwarding, an always-on server.", "Як відкрити теки ПК і телефона на інших пристроях: домашня мережа, проброс порту, постійний сервер.", "So öffnen Sie die Ordner eines PCs und eines Telefons auf anderen Geräten: Heimnetz, Portweiterleitung, ständig laufender Server.", "Come aprire le cartelle del PC e del telefono su altri dispositivi: rete domestica, inoltro della porta, server sempre attivo.", "Cómo abrir las carpetas del PC y del teléfono en otros dispositivos: red doméstica, redirección de puertos, servidor siempre activo.", "Comment ouvrir les dossiers du PC et du téléphone sur d'autres appareils : réseau domestique, redirection de port, serveur toujours actif.", "Como abrir as pastas do PC e do telefone em outros dispositivos: rede doméstica, redirecionamento de porta, servidor sempre ligado.", "كيفية فتح مجلدات الحاسوب والهاتف على أجهزة أخرى: الشبكة المنزلية وإعادة توجيه المنفذ والخادم الدائم التشغيل.", "पीसी और फ़ोन के फ़ोल्डर दूसरे उपकरणों पर कैसे खोलें: घरेलू नेटवर्क, पोर्ट फ़ॉरवर्डिंग, हमेशा चालू सर्वर।", "পিসি ও ফোনের ফোল্ডার অন্য ডিভাইসে কীভাবে খুলবেন: হোম নেটওয়ার্ক, পোর্ট ফরোয়ার্ডিং, সর্বদা চালু সার্ভার।", "پی سی اور فون کے فولڈرز دوسرے آلات پر کیسے کھولیں: گھریلو نیٹ ورک، پورٹ فارورڈنگ، ہمیشہ چلنے والا سرور۔", "如何在其他设备上打开电脑和手机的文件夹：家庭网络、端口转发、常开服务器。")
        Add("Сайт Doc HTML Translate", "Doc HTML Translate website", "Сайт Doc HTML Translate", "Website von Doc HTML Translate", "Sito di Doc HTML Translate", "Sitio de Doc HTML Translate", "Site de Doc HTML Translate", "Site do Doc HTML Translate", "موقع Doc HTML Translate", "Doc HTML Translate वेबसाइट", "Doc HTML Translate ওয়েবসাইট", "Doc HTML Translate ویب سائٹ", "Doc HTML Translate 网站")
        Add("Описание возможностей, поддерживаемых форматов, установки и работы с документами и изображениями.", "Features, supported formats, installation, and workflows for documents and images.", "Опис можливостей, підтримуваних форматів, встановлення та роботи з документами й зображеннями.", "Funktionen, unterstützte Formate, Installation und Arbeitsabläufe für Dokumente und Bilder.", "Funzionalità, formati supportati, installazione e flussi di lavoro per documenti e immagini.", "Funciones, formatos admitidos, instalación y flujos de trabajo para documentos e imágenes.", "Fonctionnalités, formats pris en charge, installation et flux de travail pour documents et images.", "Recursos, formatos suportados, instalação e fluxos de trabalho para documentos e imagens.", "الميزات والصيغ المدعومة والتثبيت وسير العمل للمستندات والصور.", "विशेषताएँ, समर्थित प्रारूप, स्थापना और दस्तावेज़ों व छवियों के कार्यप्रवाह।", "বৈশিষ্ট্য, সমর্থিত ফরম্যাট, ইনস্টলেশন এবং নথি ও ছবির কর্মপ্রবাহ।", "خصوصیات، معاون فارمیٹس، انسٹالیشن اور دستاویزات و تصاویر کے ورک فلو۔", "功能、支持的格式、安装方法，以及文档与图片的处理流程。")
        Add("Не удалось открыть ссылку в браузере. Откройте её вручную: {0}",
            "Could not open the link in the browser. Open it by hand: {0}",
            "Не вдалося відкрити посилання в браузері. Відкрийте його вручну: {0}",
            "Der Link konnte nicht im Browser geöffnet werden. Öffnen Sie ihn manuell: {0}",
            "Impossibile aprire il link nel browser. Aprilo manualmente: {0}",
            "No se pudo abrir el enlace en el navegador. Ábralo manualmente: {0}",
            "Impossible d'ouvrir le lien dans le navigateur. Ouvrez-le manuellement : {0}",
            "Não foi possível abrir o link no navegador. Abra-o manualmente: {0}",
            "تعذر فتح الرابط في المتصفح. افتحه يدويًا: {0}",
            "ब्राउज़र में लिंक नहीं खुल सका। इसे मैन्युअल रूप से खोलें: {0}",
            "ব্রাউজারে লিঙ্কটি খোলা যায়নি। নিজে খুলুন: {0}",
            "براؤزر میں لنک نہیں کھل سکا۔ اسے خود کھولیں: {0}",
            "无法在浏览器中打开链接。请手动打开：{0}")
    End Sub
End Class
