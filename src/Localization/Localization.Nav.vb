Option Strict On

' <summary>
' The .NET 10 settings shell's left-hand navigation and its page headers - the
' strings that used to live in If(ru, {..}, {..}) array literals.
' </summary>
Partial Public NotInheritable Class Localization

    Private Shared Sub AddNavStrings()

        ' nav buttons
        Add("Получатели", "Destinations", "Отримувачі", "Ziele", "Destinazioni", "Destinos", "Destinations", "Destinos", "الوجهات", "गंतव्य", "গন্তব্য", "منزلیں", "目标")
        Add("Файлы", "Files", "Файли", "Dateien", "File", "Archivos", "Fichiers", "Arquivos", "الملفات", "फ़ाइलें", "ফাইল", "فائلیں", "文件")
        Add("О программе", "About", "Про програму", "Über", "Informazioni", "Acerca de", "À propos", "Sobre", "حول", "बारे में", "সম্পর্কে", "بارے میں", "关于")

        ' The two SFTP labels differ only by the separator in Russian; both are proper
        ' nouns plus a conjunction, so most languages keep them as they are.
        Add("Android / SFTP", "Android / SFTP", "Android / SFTP", "Android / SFTP", "Android / SFTP", "Android / SFTP", "Android / SFTP", "Android / SFTP", "Android / SFTP", "Android / SFTP", "Android / SFTP", "Android / SFTP", "Android / SFTP")
        Add("Android и SFTP", "Android & SFTP", "Android і SFTP", "Android und SFTP", "Android e SFTP", "Android y SFTP", "Android et SFTP", "Android e SFTP", "أندرويد وSFTP", "Android और SFTP", "Android ও SFTP", "Android اور SFTP", "Android 与 SFTP")

        ' "OCR" is an acronym everywhere; registered so the parity test can see it.
        Add("OCR", "OCR", "OCR", "OCR", "OCR", "OCR", "OCR", "OCR", "OCR", "OCR", "OCR", "OCR", "OCR")

        ' page subtitles
        Add("Назначьте папки для быстрого перемещения и копирования.", "Assign folders for quick moving and copying.", "Призначте теки для швидкого переміщення та копіювання.", "Weisen Sie Ordner für schnelles Verschieben und Kopieren zu.", "Assegna le cartelle per spostare e copiare rapidamente.", "Asigne carpetas para mover y copiar rápidamente.", "Attribuez des dossiers pour déplacer et copier rapidement.", "Atribua pastas para mover e copiar rapidamente.", "خصّص المجلدات للنقل والنسخ السريع.", "तेज़ी से ले जाने और कॉपी करने के लिए फ़ोल्डर तय करें।", "দ্রুত সরানো ও কপি করার জন্য ফোল্ডার নির্ধারণ করুন।", "تیزی سے منتقل اور نقل کرنے کے لیے فولڈرز مقرر کریں۔", "指定用于快速移动和复制的文件夹。")
        Add("Настройте фон, информацию на экране и слайдшоу.", "Tune the background, on-screen information and slideshow.", "Налаштуйте фон, інформацію на екрані та слайд-шоу.", "Stellen Sie Hintergrund, Bildschirminfos und Diashow ein.", "Regola sfondo, informazioni a schermo e presentazione.", "Ajuste el fondo, la información en pantalla y el pase de diapositivas.", "Réglez le fond, les infos à l'écran et le diaporama.", "Ajuste o fundo, as informações na tela e a apresentação.", "اضبط الخلفية والمعلومات على الشاشة وعرض الشرائح.", "पृष्ठभूमि, स्क्रीन जानकारी और स्लाइडशो सेट करें।", "পটভূমি, পর্দার তথ্য ও স্লাইডশো সেট করুন।", "پس منظر، اسکرین معلومات اور سلائیڈ شو ترتیب دیں۔", "调整背景、屏幕信息与幻灯片。")
        Add("Качество изображения и привычное поведение видео.", "Image quality and familiar video behaviour.", "Якість зображення та звична поведінка відео.", "Bildqualität und gewohntes Videoverhalten.", "Qualità dell'immagine e comportamento video consueto.", "Calidad de imagen y comportamiento habitual del vídeo.", "Qualité d'image et comportement vidéo habituel.", "Qualidade da imagem e comportamento habitual do vídeo.", "جودة الصورة وسلوك الفيديو المعتاد.", "छवि गुणवत्ता और परिचित वीडियो व्यवहार।", "ছবির মান ও পরিচিত ভিডিও আচরণ।", "تصویر کا معیار اور مانوس ویڈیو رویہ۔", "图像质量与惯常的视频行为。")
        Add("Операции с файлами, интеграция и язык интерфейса.", "File operations, integration and interface language.", "Операції з файлами, інтеграція та мова інтерфейсу.", "Dateivorgänge, Integration und Oberflächensprache.", "Operazioni sui file, integrazione e lingua dell'interfaccia.", "Operaciones de archivo, integración e idioma de la interfaz.", "Opérations sur les fichiers, intégration et langue de l'interface.", "Operações de arquivo, integração e idioma da interface.", "عمليات الملفات والتكامل ولغة الواجهة.", "फ़ाइल कार्य, एकीकरण और इंटरफ़ेस भाषा।", "ফাইল অপারেশন, সমন্বয় ও ইন্টারফেস ভাষা।", "فائل کے عمل، انضمام اور انٹرفیس زبان۔", "文件操作、集成与界面语言。")
        Add("Распознавание текста на изображениях и параметры OCR.", "Text recognition on images and OCR options.", "Розпізнавання тексту на зображеннях і параметри OCR.", "Texterkennung auf Bildern und OCR-Optionen.", "Riconoscimento del testo nelle immagini e opzioni OCR.", "Reconocimiento de texto en imágenes y opciones de OCR.", "Reconnaissance de texte sur les images et options OCR.", "Reconhecimento de texto em imagens e opções de OCR.", "التعرف على النص في الصور وخيارات OCR.", "छवियों पर पाठ पहचान और OCR विकल्प।", "ছবিতে লেখা শনাক্তকরণ ও OCR বিকল্প।", "تصاویر پر متن کی شناخت اور OCR اختیارات۔", "图片文字识别与 OCR 选项。")
        Add("Сервис и параметры перевода распознанного текста.", "Service and options for translating recognized text.", "Сервіс і параметри перекладу розпізнаного тексту.", "Dienst und Optionen für die Übersetzung des erkannten Textes.", "Servizio e opzioni per tradurre il testo riconosciuto.", "Servicio y opciones para traducir el texto reconocido.", "Service et options de traduction du texte reconnu.", "Serviço e opções para traduzir o texto reconhecido.", "خدمة وخيارات ترجمة النص المتعرف عليه.", "पहचाने गए पाठ के अनुवाद की सेवा और विकल्प।", "শনাক্ত করা লেখার অনুবাদ সেবা ও বিকল্প।", "شناخت شدہ متن کے ترجمے کی سروس اور اختیارات۔", "识别文本的翻译服务与选项。")
        Add("SFTP-сервер и мобильное приложение Android.", "SFTP server and the Android mobile app.", "SFTP-сервер і мобільний застосунок Android.", "SFTP-Server und die Android-App.", "Server SFTP e app mobile Android.", "Servidor SFTP y la aplicación móvil Android.", "Serveur SFTP et l'application mobile Android.", "Servidor SFTP e o aplicativo móvel Android.", "خادم SFTP وتطبيق أندرويد للهاتف.", "SFTP सर्वर और Android मोबाइल ऐप।", "SFTP সার্ভার ও Android মোবাইল অ্যাপ।", "SFTP سرور اور Android موبائل ایپ۔", "SFTP 服务器与 Android 手机应用。")
        Add("Версия приложения, документация и ссылки проекта.", "Application version, documentation and project links.", "Версія застосунку, документація та посилання проєкту.", "Anwendungsversion, Dokumentation und Projektlinks.", "Versione dell'applicazione, documentazione e link del progetto.", "Versión de la aplicación, documentación y enlaces del proyecto.", "Version de l'application, documentation et liens du projet.", "Versão do aplicativo, documentação e links do projeto.", "إصدار التطبيق والوثائق وروابط المشروع.", "ऐप संस्करण, दस्तावेज़ और परियोजना लिंक।", "অ্যাপের সংস্করণ, নথি ও প্রকল্পের লিংক।", "ایپ کا ورژن، دستاویزات اور پروجیکٹ لنکس۔", "应用版本、文档与项目链接。")
    End Sub

End Class
