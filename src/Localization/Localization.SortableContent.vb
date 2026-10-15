Option Explicit On
Option Strict On
Option Infer On
Option Compare Binary

Partial Public NotInheritable Class Localization
    Private Shared Sub AddSortableContentStrings()
        Add("Документы", "Documents", "Документи", "Dokumente", "Documenti", "Documentos", "Documents", "Documentos", "مستندات", "दस्तावेज़", "নথি", "دستاویزات", "文档")
        Add("Другие", "Other", "Інші", "Andere", "Altri", "Otros", "Autres", "Outros", "أخرى", "अन्य", "অন্যান্য", "دیگر", "其他")
        Add("Сортировать документы", "Sort documents", "Сортувати документи", "Dokumente sortieren", "Ordina documenti", "Ordenar documentos", "Trier les documents", "Ordenar documentos", "فرز المستندات", "दस्तावेज़ क्रमबद्ध करें", "নথি সাজান", "دستاویزات ترتیب دیں", "整理文档")
        Add("Сортировать другие файлы", "Sort other files", "Сортувати інші файли", "Andere Dateien sortieren", "Ordina altri file", "Ordenar otros archivos", "Trier les autres fichiers", "Ordenar outros arquivos", "فرز الملفات الأخرى", "अन्य फ़ाइलें क्रमबद्ध करें", "অন্যান্য ফাইল সাজান", "دیگر فائلیں ترتیب دیں", "整理其他文件")
        Add("Расширения документов", "Document extensions", "Розширення документів", "Dokumentendungen", "Estensioni dei documenti", "Extensiones de documentos", "Extensions des documents", "Extensões de documentos", "امتدادات المستندات", "दस्तावेज़ एक्सटेंशन", "নথির এক্সটেনশন", "دستاویزات کی توسیعات", "文档扩展名")
        Add("Расширения других файлов", "Other file extensions", "Розширення інших файлів", "Andere Dateiendungen", "Estensioni degli altri file", "Extensiones de otros archivos", "Extensions des autres fichiers", "Extensões de outros arquivos", "امتدادات الملفات الأخرى", "अन्य फ़ाइल एक्सटेंशन", "অন্যান্য ফাইলের এক্সটেনশন", "دیگر فائلوں کی توسیعات", "其他文件扩展名")
        Add("Вместо картинки - имя, размер и дата. Клавиши сортировки работают как обычно.",
            "Shows name, size and date instead of a picture. Sorting keys work as usual.",
            "Замість зображення - ім’я, розмір і дата. Клавіші сортування працюють як завжди.",
            "Zeigt Name, Größe und Datum statt eines Bildes. Sortiertasten funktionieren wie gewohnt.",
            "Mostra nome, dimensione e data al posto dell’immagine. I tasti di ordinamento funzionano come sempre.",
            "Muestra nombre, tamaño y fecha en lugar de una imagen. Las teclas de clasificación funcionan como siempre.",
            "Affiche le nom, la taille et la date à la place d’une image. Les touches de tri fonctionnent comme d’habitude.",
            "Mostra nome, tamanho e data em vez de uma imagem. As teclas de ordenação funcionam como sempre.",
            "يعرض الاسم والحجم والتاريخ بدلاً من الصورة. تعمل مفاتيح الفرز كالمعتاد.",
            "चित्र के बजाय नाम, आकार और तारीख दिखाता है। क्रमबद्ध करने की कुंजियाँ सामान्य रूप से काम करती हैं।",
            "ছবির বদলে নাম, আকার ও তারিখ দেখায়। সাজানোর কী স্বাভাবিকভাবে কাজ করে।",
            "تصویر کی جگہ نام، حجم اور تاریخ دکھاتا ہے۔ ترتیب دینے کی کلیدیں معمول کے مطابق کام کرتی ہیں۔",
            "显示名称、大小和日期，整理快捷键照常使用。")
        Add("Расширения через пробел или точку с запятой, например .pdf;.txt. Без масок.",
            "Separate extensions with spaces or semicolons, e.g. .pdf;.txt. No wildcards.",
            "Розширення через пробіл або крапку з комою, наприклад .pdf;.txt. Без масок.",
            "Endungen mit Leerzeichen oder Semikolon trennen, z. B. .pdf;.txt. Keine Platzhalter.",
            "Separa le estensioni con spazi o punti e virgola, es. .pdf;.txt. Senza caratteri jolly.",
            "Separe las extensiones con espacios o punto y coma, p. ej. .pdf;.txt. Sin comodines.",
            "Séparez les extensions par des espaces ou des points-virgules, p. ex. .pdf;.txt. Sans jokers.",
            "Separe as extensões com espaços ou ponto e vírgula, ex. .pdf;.txt. Sem curingas.",
            "افصل الامتدادات بمسافات أو فاصلة منقوطة، مثل .pdf;.txt. دون أحرف بدل.",
            "एक्सटेंशन को रिक्त स्थान या अर्धविराम से अलग करें, जैसे .pdf;.txt। वाइल्डकार्ड नहीं।",
            "স্পেস বা সেমিকোলন দিয়ে এক্সটেনশন আলাদা করুন, যেমন .pdf;.txt। ওয়াইল্ডকার্ড নয়।",
            "توسیعات کو خالی جگہ یا سیمی کولن سے الگ کریں، مثلاً .pdf;.txt۔ وائلڈ کارڈ نہیں۔",
            "用空格或分号分隔扩展名，例如 .pdf;.txt。不支持通配符。")
        ' The key is a placeholder since 123 (UX-112): "open" is a catalogue action now, and the placeholder
        ' names the key it answers to RIGHT NOW instead of a hard-coded Enter.
        Add("{0} - открыть в программе по умолчанию", "{0} - open in the default program", "{0} - відкрити у типовій програмі", "{0} - im Standardprogramm öffnen", "{0} - apri nel programma predefinito", "{0} - abrir en el programa predeterminado", "{0} - ouvrir dans le programme par défaut", "{0} - abrir no programa padrão", "{0} - فتح في البرنامج الافتراضي", "{0} - डिफ़ॉल्ट प्रोग्राम में खोलें", "{0} - ডিফল্ট প্রোগ্রামে খুলুন", "{0} - طے شدہ پروگرام میں کھولیں", "{0} - 使用默认程序打开")
        Add("Сначала извлеките файл из архива", "Extract the file from the archive first", "Спочатку витягніть файл з архіву", "Datei zuerst aus dem Archiv entpacken", "Estrai prima il file dall’archivio", "Extraiga primero el archivo", "Extrayez d’abord le fichier de l’archive", "Extraia primeiro o arquivo", "استخرج الملف من الأرشيف أولاً", "पहले संग्रह से फ़ाइल निकालें", "প্রথমে আর্কাইভ থেকে ফাইল বের করুন", "پہلے آرکائیو سے فائل نکالیں", "请先从压缩包中解压文件")
        Add("Не удалось открыть файл: {0}", "Could not open file: {0}", "Не вдалося відкрити файл: {0}", "Datei konnte nicht geöffnet werden: {0}", "Impossibile aprire il file: {0}", "No se pudo abrir el archivo: {0}", "Impossible d’ouvrir le fichier : {0}", "Não foi possível abrir o arquivo: {0}", "تعذر فتح الملف: {0}", "फ़ाइल नहीं खुल सकी: {0}", "ফাইল খোলা যায়নি: {0}", "فائل نہیں کھل سکی: {0}", "无法打开文件：{0}")
        Add("Папка перечитана", "Folder refreshed", "Папку перечитано", "Ordner aktualisiert", "Cartella aggiornata", "Carpeta actualizada", "Dossier actualisé", "Pasta atualizada", "تم تحديث المجلد", "फ़ोल्डर ताज़ा किया गया", "ফোল্ডার রিফ্রেশ হয়েছে", "فولڈر تازہ کیا گیا", "文件夹已刷新")
        Add("Слайд-шоу остановлено; папка перечитана", "Slideshow stopped; folder refreshed", "Слайд-шоу зупинено; папку перечитано", "Diashow angehalten; Ordner aktualisiert", "Presentazione interrotta; cartella aggiornata", "Presentación detenida; carpeta actualizada", "Diaporama arrêté ; dossier actualisé", "Apresentação parada; pasta atualizada", "تم إيقاف العرض وتحديث المجلد", "स्लाइड शो रोका गया; फ़ोल्डर ताज़ा किया गया", "স্লাইডশো থামানো হয়েছে; ফোল্ডার রিফ্রেশ হয়েছে", "سلائیڈ شو روکا گیا؛ فولڈر تازہ کیا گیا", "幻灯片已停止；文件夹已刷新")
        Add("{0} - {1}", "{0} - {1}", "{0} - {1}", "{0} - {1}", "{0} - {1}", "{0} - {1}", "{0} - {1}", "{0} - {1}", "{0} - {1}", "{0} - {1}", "{0} - {1}", "{0} - {1}", "{0} - {1}")
        Add("Документ защищён паролем", "Document is password protected", "Документ захищено паролем", "Dokument ist passwortgeschützt", "Il documento è protetto da password", "El documento está protegido por contraseña", "Le document est protégé par mot de passe", "O documento é protegido por senha", "المستند محمي بكلمة مرور", "दस्तावेज़ पासवर्ड से सुरक्षित है", "নথিটি পাসওয়ার্ড সুরক্ষিত", "دستاویز پاس ورڈ سے محفوظ ہے", "文档受密码保护")
        Add("Не удалось открыть PDF", "Could not open PDF", "Не вдалося відкрити PDF", "PDF konnte nicht geöffnet werden", "Impossibile aprire il PDF", "No se pudo abrir el PDF", "Impossible d’ouvrir le PDF", "Não foi possível abrir o PDF", "تعذر فتح PDF", "PDF नहीं खोला जा सका", "PDF খোলা যায়নি", "PDF نہیں کھولا جا سکا", "无法打开 PDF")
        Add("{0} - ошибка отображения страницы {1}", "{0} - error rendering page {1}", "{0} - помилка відображення сторінки {1}", "{0} - Fehler beim Rendern von Seite {1}", "{0} - errore durante il rendering della pagina {1}", "{0} - error al representar la página {1}", "{0} - erreur lors du rendu de la page {1}", "{0} - erro ao renderizar a página {1}", "{0} - خطأ في عرض الصفحة {1}", "{0} - पृष्ठ {1} प्रस्तुत करने में त्रुटि", "{0} - পৃষ্ঠা {1} রেন্ডার করতে ত্রুটি", "{0} - صفحہ {1} پیش کرنے میں خرابی", "{0} - 渲染第 {1} 页时出错")
        Add("{0} - страница {1} из {2}", "{0} - page {1} of {2}", "{0} - сторінка {1} з {2}", "{0} - Seite {1} von {2}", "{0} - pagina {1} di {2}", "{0} - página {1} de {2}", "{0} - page {1} sur {2}", "{0} - página {1} de {2}", "{0} - الصفحة {1} من {2}", "{0} - पृष्ठ {1} का {2}", "{0} - পৃষ্ঠা {1} এর {2}", "{0} - صفحہ {1} از {2}", "{0} - 第 {1} 页，共 {2} 页")
        Add("{0} - открыть в doc-html-translate", "{0} - open in doc-html-translate", "{0} - відкрити в doc-html-translate", "{0} - in doc-html-translate öffnen", "{0} - apri in doc-html-translate", "{0} - abrir en doc-html-translate", "{0} - ouvrir dans doc-html-translate", "{0} - abrir no doc-html-translate", "{0} - فتح في doc-html-translate", "{0} - doc-html-translate में खोलें", "{0} - doc-html-translate এ খুলুন", "{0} - doc-html-translate میں کھولیں", "{0} - 在 doc-html-translate 中打开")
        Add("Бинарный файл", "Binary file", "Бінарний файл", "Binärdatei", "File binario", "Archivo binario", "Fichier binaire", "Arquivo binário", "ملف ثنائي", "बाइनरी फ़ाइल", "বাইনারি ফাইল", "بائنری فائل", "二进制文件")
        Add("{0} - показаны первые {1} из {2}", "{0} - showing first {1} of {2}", "{0} - показано перші {1} з {2}", "{0} - erste {1} von {2} werden angezeigt", "{0} - visualizzati i primi {1} di {2}", "{0} - mostrando los primeros {1} de {2}", "{0} - affichage des premiers {1} sur {2}", "{0} - exibindo os primeiros {1} de {2}", "{0} - عرض أول {1} من {2}", "{0} - {2} में से पहले {1} दिखाए जा रहे हैं", "{0} - {2} এর মধ্যে প্রথম {1} দেখানো হচ্ছে", "{0} - {2} میں سے پہلے {1} دکھائے جا رہے ہیں", "{0} - 显示前 {1} / {2}")
        ' Two forms since 123 (UX-112): with the key the open action answers to ({2}), and without one when the
        ' user took that shortcut away - a footer must not name a key that does nothing.
        Add("Показаны первые {0} из {1}. {2} - открыть файл целиком.", "Showing first {0} of {1}. {2} - open full file.", "Показано перші {0} з {1}. {2} - відкрити файл повністю.", "Erste {0} von {1} werden angezeigt. {2} - gesamte Datei öffnen.", "Visualizzati i primi {0} di {1}. {2} - apri il file completo.", "Mostrando los primeros {0} de {1}. {2} - abrir el archivo completo.", "Affichage des premiers {0} sur {1}. {2} - ouvrir le fichier complet.", "Exibindo os primeiros {0} de {1}. {2} - abrir o arquivo completo.", "عرض أول {0} من {1}. {2} - فتح الملف بالكامل.", "{1} में से पहले {0} दिखाए जा रहे हैं। {2} - पूरी फ़ाइल खोलें।", "{1} এর মধ্যে প্রথম {0} দেখানো হচ্ছে। {2} - সম্পূর্ণ ফাইল খুলুন।", "{1} میں سے پہلے {0} دکھائے جا رہے ہیں۔ {2} - مکمل فائل کھولیں۔", "显示前 {0} / {1}。{2} - 打开完整文件。")
        Add("Показаны первые {0} из {1}.", "Showing first {0} of {1}.", "Показано перші {0} з {1}.", "Erste {0} von {1} werden angezeigt.", "Visualizzati i primi {0} di {1}.", "Mostrando los primeros {0} de {1}.", "Affichage des premiers {0} sur {1}.", "Exibindo os primeiros {0} de {1}.", "عرض أول {0} من {1}.", "{1} में से पहले {0} दिखाए जा रहे हैं।", "{1} এর মধ্যে প্রথম {0} দেখানো হচ্ছে।", "{1} میں سے پہلے {0} دکھائے جا رہے ہیں۔", "显示前 {0} / {1}。")
        Add("В документе нет страниц", "Document has no pages", "У документі немає сторінок", "Dokument enthält keine Seiten", "Il documento non contiene pagine", "El documento no tiene páginas", "Le document ne contient aucune page", "O documento não contém páginas", "لا توجد صفحات في المستند", "दस्तावेज़ में कोई पृष्ठ नहीं है", "নথিতে কোনো পৃষ্ঠা নেই", "دستاویز میں کوئی صفحہ نہیں ہے", "文档中没有页面")
        Add("Файл не найден", "File not found", "Файл не знайдено", "Datei nicht gefunden", "File non trovato", "Archivo no encontrado", "Fichier non trouvé", "Arquivo não encontrado", "الملف غير موجود", "फ़ाइल नहीं मिली", "ফাইল পাওয়া যায়নি", "فائل نہیں ملی", "找不到文件")
        Add("Размер просмотра текста, КБ", "Text preview size, KB", "Розмір перегляду тексту, КБ", "Textvorschaugröße, KB", "Dimensione anteprima testo, KB", "Tamaño de vista previa de texto, KB", "Taille d’aperçu du texte, Ko", "Tamanho da pré-visualização de texto, KB", "حجم معاينة النص، ك.ب", "पाठ पूर्वावलोकन आकार, KB", "লেখা প্রিভিউ সাইজ, KB", "متن پیش منظر کا سائز، KB", "文本预览大小，KB")
        Add("Читается только начало файла. Enter открывает файл целиком во внешнем редакторе.",
            "Only the beginning of the file is read. Enter opens the entire file in an external editor.",
            "Читається лише початок файлу. Enter відкриває файл повністю у зовнішньому редакторі.",
            "Es wird nur der Anfang der Datei gelesen. Enter öffnet die gesamte Datei in einem externen Editor.",
            "Viene letta solo la parte iniziale del file. Invio apre l'intero file in un editor esterno.",
            "Solo se lee el principio del archivo. Intro abre el archivo completo en un editor externo.",
            "Seul le début du fichier est lu. Entrée ouvre le fichier entier dans un éditeur externe.",
            "Apenas o início do arquivo é lido. Enter abre o arquivo completo em um editor externo.",
            "تتم قراءة بداية الملف فقط. Enter يفتح الملف بالكامل في محرر خارجي.",
            "केवल फ़ाइल की शुरुआत पढ़ी जाती है। Enter पूरी फ़ाइल को बाहरी संपादक में खोलता है।",
            "শুধুমাত্র ফাইলের শুরুটা পড়া হয়। Enter বহিরাগত এডিটরে সম্পূর্ণ ফাইলটি খোলে।",
            "صرف فائل کا آغاز پڑھا جاتا ہے۔ Enter مکمل فائل بیرونی ایڈیٹر میں کھولتا ہے۔",
            "仅读取文件开头。Enter 在外部编辑器中打开整个文件。")

        Add("Ошибка отображения страницы {0}",
            "Could not render page {0}",
            "Не вдалося відобразити сторінку {0}",
            "Seite {0} konnte nicht angezeigt werden",
            "Impossibile visualizzare la pagina {0}",
            "No se pudo mostrar la página {0}",
            "Impossible d’afficher la page {0}",
            "Não foi possível exibir a página {0}",
            "تعذر عرض الصفحة {0}",
            "पृष्ठ {0} नहीं दिखाया जा सका",
            "পৃষ্ঠা {0} দেখানো যায়নি",
            "صفحہ {0} نہیں دکھایا جا سکا",
            "无法显示第 {0} 页")
        Add("Документы: колесо и PgUp/PgDn - прокрутка или страницы; Ctrl+колесо - масштаб; Home/End - начало/конец документа, Ctrl+Home/End - первый/последний файл. Стрелки - файлы.",
            "Documents: wheel and PgUp/PgDn scroll or change pages; Ctrl+wheel zooms; Home/End go to document start/end, Ctrl+Home/End to first/last file. Arrows change files.",
            "Документи: колесо та PgUp/PgDn - прокрутка або сторінки; Ctrl+колесо - масштаб; Home/End - початок/кінець документа, Ctrl+Home/End - перший/останній файл. Стрілки - файли.",
            "Dokumente: Mausrad und PgUp/PgDn scrollen oder wechseln Seiten; Strg+Mausrad zoomt; Home/End: Dokumentanfang/-ende, Strg+Home/End: erste/letzte Datei. Pfeile wechseln Dateien.",
            "Documenti: rotella e PgUp/PgDn scorrono o cambiano pagina; Ctrl+rotella ingrandisce; Home/End: inizio/fine documento, Ctrl+Home/End: primo/ultimo file. Le frecce cambiano file.",
            "Documentos: rueda y PgUp/PgDn desplazan o cambian página; Ctrl+rueda amplía; Home/End: inicio/fin del documento, Ctrl+Home/End: primer/último archivo. Flechas: archivos.",
            "Documents : molette et PgUp/PgDn défilent ou changent de page ; Ctrl+molette zoome ; Home/End : début/fin du document, Ctrl+Home/End : premier/dernier fichier. Flèches : fichiers.",
            "Documentos: roda e PgUp/PgDn rolam ou mudam páginas; Ctrl+roda amplia; Home/End: início/fim do documento, Ctrl+Home/End: primeiro/último arquivo. Setas mudam arquivos.",
            "المستندات: العجلة وPgUp/PgDn للتمرير أو تغيير الصفحة؛ Ctrl+العجلة للتكبير؛ Home/End لبداية/نهاية المستند، Ctrl+Home/End لأول/آخر ملف. الأسهم تغيّر الملفات.",
            "दस्तावेज़: पहिया और PgUp/PgDn स्क्रॉल या पृष्ठ बदलते हैं; Ctrl+पहिया ज़ूम करता है; Home/End दस्तावेज़ के आरंभ/अंत, Ctrl+Home/End पहली/आखिरी फ़ाइल पर जाते हैं। तीर फ़ाइलें बदलते हैं।",
            "নথি: চাকা ও PgUp/PgDn স্ক্রল বা পৃষ্ঠা বদলায়; Ctrl+চাকা জুম করে; Home/End নথির শুরু/শেষ, Ctrl+Home/End প্রথম/শেষ ফাইলে যায়। তীর ফাইল বদলায়।",
            "دستاویزات: وہیل اور PgUp/PgDn اسکرول یا صفحات بدلتے ہیں؛ Ctrl+وہیل زوم کرتا ہے؛ Home/End دستاویز کے آغاز/اختتام، Ctrl+Home/End پہلی/آخری فائل پر جاتے ہیں۔ تیر فائلیں بدلتے ہیں۔",
            "文档：滚轮和 PgUp/PgDn 滚动或翻页；Ctrl+滚轮缩放；Home/End 到文档开头/末尾，Ctrl+Home/End 到第一个/最后一个文件。方向键切换文件。")
    End Sub
End Class
