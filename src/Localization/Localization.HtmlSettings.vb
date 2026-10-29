Option Explicit On
Option Strict On
Option Infer On
Option Compare Binary

Partial Public NotInheritable Class Localization
    ' Specification 275 section 12: the Documents page HTML groups, rows and descriptions.
    Private Shared Sub AddHtmlSettingsStrings()
        ' Clear-history confirmation (AskSpecs.vb).
        Add("Очистить историю HTML-страниц",
            "Clear HTML page history", "Очистити історію HTML-сторінок", "Verlauf der HTML-Seiten löschen",
            "Cancella cronologia pagine HTML", "Borrar historial de páginas HTML", "Effacer l'historique des pages HTML",
            "Limpar histórico de páginas HTML", "مسح سجل صفحات HTML", "HTML पेज का इतिहास साफ़ करें",
            "HTML পেজের ইতিহাস মুছুন", "HTML صفحات کی ہسٹری صاف کریں", "清除 HTML 页面历史记录")
        Add("Удалить позиции чтения и оформление отдельных HTML-страниц?",
            "Delete the reading positions and appearance of individual HTML pages?", "Видалити позиції читання та оформлення окремих HTML-сторінок?", "Leseposition und Darstellung einzelner HTML-Seiten löschen?",
            "Eliminare le posizioni di lettura e l'aspetto delle singole pagine HTML?", "¿Eliminar las posiciones de lectura y la apariencia de páginas HTML concretas?", "Supprimer les positions de lecture et l'apparence de chaque page HTML ?",
            "Excluir as posições de leitura e a aparência de páginas HTML individuais?", "هل تريد حذف مواضع القراءة ومظهر كل صفحة HTML على حدة؟", "क्या अलग-अलग HTML पेज की पढ़ने की स्थिति और रूप हटाएँ?",
            "আলাদা আলাদা HTML পেজের পড়ার অবস্থান ও রূপ কি মুছে ফেলবেন?", "کیا الگ الگ HTML صفحات کی مطالعے کی پوزیشنیں اور ظاہری شکل حذف کریں؟", "要删除各个 HTML 页面的阅读位置和样式吗？")
        Add("Удаляются только записи программы. Сами документы и общие настройки не изменятся.",
            "Only the program's records are deleted. The documents themselves and the general settings stay unchanged.", "Видаляються лише записи програми. Самі документи та загальні налаштування не зміняться.", "Es werden nur die Einträge des Programms gelöscht. Die Dokumente selbst und die allgemeinen Einstellungen bleiben unverändert.",
            "Vengono eliminate solo le registrazioni del programma. I documenti stessi e le impostazioni generali non cambiano.", "Solo se eliminan los registros del programa. Los documentos y la configuración general no cambian.", "Seuls les enregistrements du programme sont supprimés. Les documents eux-mêmes et les paramètres généraux ne changent pas.",
            "Somente os registros do programa são excluídos. Os documentos em si e as configurações gerais não são alterados.", "تُحذف سجلات البرنامج فقط. لا تتغير المستندات نفسها ولا الإعدادات العامة.", "केवल प्रोग्राम की प्रविष्टियाँ हटाई जाती हैं। दस्तावेज़ और सामान्य सेटिंग्स नहीं बदलेंगी।",
            "শুধু প্রোগ্রামের রেকর্ড মোছা হয়। নথিগুলো নিজে এবং সাধারণ সেটিংস বদলাবে না।", "صرف پروگرام کے ریکارڈ حذف ہوتے ہیں۔ دستاویزات خود اور عمومی ترتیبات تبدیل نہیں ہوں گی۔", "只会删除程序的记录。文档本身和常规设置不会改变。")

        ' Group headings of the Documents page.
        Add("HTML-страницы",
            "HTML pages", "HTML-сторінки", "HTML-Seiten",
            "Pagine HTML", "Páginas HTML", "Pages HTML",
            "Páginas HTML", "صفحات HTML", "HTML पेज",
            "HTML পেজ", "HTML صفحات", "HTML 页面")
        Add("Оформление HTML-страниц",
            "HTML page appearance", "Оформлення HTML-сторінок", "Darstellung von HTML-Seiten",
            "Aspetto delle pagine HTML", "Apariencia de las páginas HTML", "Apparence des pages HTML",
            "Aparência das páginas HTML", "مظهر صفحات HTML", "HTML पेज का रूप",
            "HTML পেজের রূপ", "HTML صفحات کی ظاہری شکل", "HTML 页面样式")
        Add("Ссылки, поиск и позиция в HTML-страницах",
            "Links, search and position in HTML pages", "Посилання, пошук і позиція в HTML-сторінках", "Links, Suche und Position in HTML-Seiten",
            "Collegamenti, ricerca e posizione nelle pagine HTML", "Enlaces, búsqueda y posición en las páginas HTML", "Liens, recherche et position dans les pages HTML",
            "Links, pesquisa e posição nas páginas HTML", "الروابط والبحث والموضع في صفحات HTML", "HTML पेज में लिंक, खोज और स्थिति",
            "HTML পেজে লিংক, খোঁজা ও অবস্থান", "HTML صفحات میں لنکس، تلاش اور پوزیشن", "HTML 页面中的链接、查找和位置")
        Add("HTML-страницы из интернета",
            "HTML pages from the internet", "HTML-сторінки з інтернету", "HTML-Seiten aus dem Internet",
            "Pagine HTML da internet", "Páginas HTML de internet", "Pages HTML depuis internet",
            "Páginas HTML da internet", "صفحات HTML من الإنترنت", "इंटरनेट से HTML पेज",
            "ইন্টারনেট থেকে HTML পেজ", "انٹرنیٹ سے HTML صفحات", "来自互联网的 HTML 页面")
        Add("Данные HTML-страниц",
            "HTML page data", "Дані HTML-сторінок", "Daten der HTML-Seiten",
            "Dati delle pagine HTML", "Datos de las páginas HTML", "Données des pages HTML",
            "Dados das páginas HTML", "بيانات صفحات HTML", "HTML पेज का डेटा",
            "HTML পেজের ডেটা", "HTML صفحات کا ڈیٹا", "HTML 页面数据")

        ' Choice labels.
        Add("Режим чтения",
            "Reading mode", "Режим читання", "Lesemodus",
            "Modalità lettura", "Modo de lectura", "Mode lecture",
            "Modo de leitura", "وضع القراءة", "पठन मोड",
            "পড়ার মোড", "مطالعہ موڈ", "阅读模式")
        Add("Блокировать",
            "Block", "Блокувати", "Blockieren",
            "Blocca", "Bloquear", "Bloquer",
            "Bloquear", "حظر", "ब्लॉक करें",
            "ব্লক করুন", "مسدود کریں", "阻止")
        Add("С засечками",
            "Serif", "З засічками", "Mit Serifen",
            "Con grazie", "Con serifa", "Avec empattements",
            "Com serifa", "بخطوط زخرفية (Serif)", "सेरिफ़ वाला",
            "সেরিফ সহ", "سیرف والا", "衬线")
        Add("Без засечек",
            "Sans-serif", "Без засічок", "Serifenlos",
            "Senza grazie", "Sin serifa", "Sans empattement",
            "Sem serifa", "بدون خطوط زخرفية (Sans-serif)", "बिना सेरिफ़ वाला",
            "সেরিফ ছাড়া", "بغیر سیرف کے", "无衬线")
        Add("Моноширинный",
            "Monospace", "Моноширинний", "Nichtproportional",
            "A spaziatura fissa", "Monoespaciada", "Chasse fixe",
            "Monoespaçada", "أحادي المسافة", "मोनोस्पेस",
            "মনোস্পেস", "مونو اسپیس", "等宽")
        Add("На всю ширину",
            "Full width", "На всю ширину", "Volle Breite",
            "Larghezza intera", "Ancho completo", "Pleine largeur",
            "Largura total", "العرض الكامل", "पूरी चौड़ाई",
            "পূর্ণ প্রস্থ", "پوری چوڑائی", "全宽")
        Add("{0} симв.",
            "{0} chars", "{0} симв.", "{0} Zeichen",
            "{0} car.", "{0} car.", "{0} car.",
            "{0} car.", "{0} حرف", "{0} अक्षर",
            "{0} অক্ষর", "{0} حروف", "{0} 个字符")

        ' Result messages of the clear actions.
        Add("Историю не удалось очистить: файл записей недоступен для записи.",
            "The history could not be cleared: the records file is not writable.", "Історію не вдалося очистити: файл записів недоступний для запису.", "Der Verlauf konnte nicht gelöscht werden: Die Datei mit den Einträgen ist nicht beschreibbar.",
            "Non è stato possibile cancellare la cronologia: il file delle registrazioni non è scrivibile.", "No se pudo borrar el historial: el archivo de registros no admite escritura.", "L'historique n'a pas pu être effacé : le fichier des enregistrements n'est pas accessible en écriture.",
            "Não foi possível limpar o histórico: o arquivo de registros não permite gravação.", "تعذّر مسح السجل: ملف السجلات غير قابل للكتابة.", "इतिहास साफ़ नहीं किया जा सका: प्रविष्टियों की फ़ाइल में लिखा नहीं जा सकता।",
            "ইতিহাস মোছা যায়নি: রেকর্ডের ফাইলে লেখা যায় না।", "ہسٹری صاف نہیں ہو سکی: ریکارڈ کی فائل میں لکھا نہیں جا سکتا۔", "无法清除历史记录：记录文件不可写入。")
        Add("История HTML-страниц очищена.",
            "HTML page history cleared.", "Історію HTML-сторінок очищено.", "Der Verlauf der HTML-Seiten wurde gelöscht.",
            "Cronologia delle pagine HTML cancellata.", "Historial de páginas HTML borrado.", "L'historique des pages HTML a été effacé.",
            "Histórico de páginas HTML limpo.", "تم مسح سجل صفحات HTML.", "HTML पेज का इतिहास साफ़ कर दिया गया।",
            "HTML পেজের ইতিহাস মুছে ফেলা হয়েছে।", "HTML صفحات کی ہسٹری صاف کر دی گئی۔", "已清除 HTML 页面历史记录。")
        Add("Очистить временные данные просмотра",
            "Clear temporary viewer data", "Очистити тимчасові дані перегляду", "Temporäre Ansichtsdaten löschen",
            "Cancella dati temporanei di visualizzazione", "Borrar datos temporales de la vista", "Effacer les données temporaires d'affichage",
            "Limpar dados temporários de visualização", "مسح بيانات العرض المؤقتة", "देखने का अस्थायी डेटा साफ़ करें",
            "দেখার অস্থায়ী ডেটা মুছুন", "دیکھنے کا عارضی ڈیٹا صاف کریں", "清除查看的临时数据")
        Add("Временные данные просмотра HTML-страниц удалены.",
            "The temporary HTML page viewer data was deleted.", "Тимчасові дані перегляду HTML-сторінок видалено.", "Die temporären Daten der HTML-Seitenansicht wurden gelöscht.",
            "I dati temporanei di visualizzazione delle pagine HTML sono stati eliminati.", "Se eliminaron los datos temporales de la vista de páginas HTML.", "Les données temporaires d'affichage des pages HTML ont été supprimées.",
            "Os dados temporários de visualização de páginas HTML foram excluídos.", "تم حذف البيانات المؤقتة لعرض صفحات HTML.", "HTML पेज देखने का अस्थायी डेटा हटा दिया गया।",
            "HTML পেজ দেখার অস্থায়ী ডেটা মুছে ফেলা হয়েছে।", "HTML صفحات دیکھنے کا عارضی ڈیٹا حذف کر دیا گیا۔", "已删除 HTML 页面查看的临时数据。")
        Add("Часть данных используется открытой страницей и останется. Закройте HTML-страницы и повторите очистку.",
            "Some of the data is in use by an open page and will remain. Close the HTML pages and clear again.", "Частину даних використовує відкрита сторінка, і вона залишиться. Закрийте HTML-сторінки та повторіть очищення.", "Ein Teil der Daten wird von einer geöffneten Seite verwendet und bleibt erhalten. Schließen Sie die HTML-Seiten und wiederholen Sie das Löschen.",
            "Una parte dei dati è usata da una pagina aperta e rimarrà. Chiudi le pagine HTML e ripeti la cancellazione.", "Una parte de los datos la usa una página abierta y se conservará. Cierre las páginas HTML y repita el borrado.", "Une partie des données est utilisée par une page ouverte et sera conservée. Fermez les pages HTML et recommencez l'effacement.",
            "Parte dos dados está em uso por uma página aberta e permanecerá. Feche as páginas HTML e repita a limpeza.", "جزء من البيانات مستخدم في صفحة مفتوحة وسيبقى. أغلق صفحات HTML وكرّر المسح.", "कुछ डेटा खुले पेज द्वारा उपयोग में है और बना रहेगा। HTML पेज बंद करें और फिर से साफ़ करें।",
            "কিছু ডেটা খোলা পেজ ব্যবহার করছে, তাই থেকে যাবে। HTML পেজগুলো বন্ধ করে আবার মুছুন।", "کچھ ڈیٹا کھلا ہوا صفحہ استعمال کر رہا ہے اور باقی رہے گا۔ HTML صفحات بند کریں اور دوبارہ صاف کریں۔", "部分数据正被打开的页面使用，将予保留。请关闭 HTML 页面后再次清除。")
        Add("Не удалось проверить компонент просмотра HTML-страниц.",
            "Could not check the HTML page viewer component.", "Не вдалося перевірити компонент перегляду HTML-сторінок.", "Die Komponente der HTML-Seitenansicht konnte nicht geprüft werden.",
            "Impossibile verificare il componente di visualizzazione delle pagine HTML.", "No se pudo comprobar el componente de la vista de páginas HTML.", "Impossible de vérifier le composant d'affichage des pages HTML.",
            "Não foi possível verificar o componente de visualização de páginas HTML.", "تعذّر فحص مكوّن عرض صفحات HTML.", "HTML पेज देखने वाले घटक की जाँच नहीं की जा सकी।",
            "HTML পেজ দেখার উপাদানটি যাচাই করা যায়নি।", "HTML صفحات دیکھنے والے جزو کی جانچ نہیں ہو سکی۔", "无法检查 HTML 页面查看组件。")

        ' Row titles.
        Add("Просмотр HTML-страниц",
            "HTML page viewing", "Перегляд HTML-сторінок", "Ansicht von HTML-Seiten",
            "Visualizzazione delle pagine HTML", "Vista de páginas HTML", "Affichage des pages HTML",
            "Visualização de páginas HTML", "عرض صفحات HTML", "HTML पेज देखना",
            "HTML পেজ দেখা", "HTML صفحات دیکھنا", "HTML 页面查看")
        Add("Вид страницы",
            "Page view", "Вигляд сторінки", "Seitenansicht",
            "Vista della pagina", "Vista de la página", "Vue de la page",
            "Visão da página", "عرض الصفحة", "पेज का रूप",
            "পেজের ভিউ", "صفحے کا منظر", "页面视图")
        Add("Компонент просмотра страниц",
            "Page viewer component", "Компонент перегляду сторінок", "Komponente der Seitenansicht",
            "Componente di visualizzazione pagine", "Componente de la vista de páginas", "Composant d'affichage des pages",
            "Componente de visualização de páginas", "مكوّن عرض الصفحات", "पेज देखने का घटक",
            "পেজ দেখার উপাদান", "صفحات دیکھنے کا جزو", "页面查看组件")
        Add("Страница загрузки компонента",
            "Component download page", "Сторінка завантаження компонента", "Download-Seite der Komponente",
            "Pagina di download del componente", "Página de descarga del componente", "Page de téléchargement du composant",
            "Página de download do componente", "صفحة تنزيل المكوّن", "घटक का डाउनलोड पेज",
            "উপাদান ডাউনলোডের পেজ", "جزو کے ڈاؤن لوڈ کا صفحہ", "组件下载页面")
        Add("Переносить широкое содержимое",
            "Wrap wide content", "Переносити широкий вміст", "Breite Inhalte umbrechen",
            "Mandare a capo i contenuti larghi", "Ajustar el contenido ancho", "Renvoyer à la ligne le contenu large",
            "Quebrar conteúdo largo", "التفاف المحتوى العريض", "चौड़ी सामग्री को लपेटें",
            "চওড়া কনটেন্ট র‍্যাপ করুন", "چوڑے مواد کو لپیٹیں", "自动换行显示过宽内容")
        Add("Запоминать оформление HTML-страниц",
            "Remember HTML page appearance", "Запам'ятовувати оформлення HTML-сторінок", "Darstellung von HTML-Seiten merken",
            "Ricorda l'aspetto delle pagine HTML", "Recordar la apariencia de las páginas HTML", "Mémoriser l'apparence des pages HTML",
            "Lembrar a aparência das páginas HTML", "تذكّر مظهر صفحات HTML", "HTML पेज का रूप याद रखें",
            "HTML পেজের রূপ মনে রাখুন", "HTML صفحات کی ظاہری شکل یاد رکھیں", "记住 HTML 页面样式")
        Add("Сбросить оформление HTML-страниц",
            "Reset HTML page appearance", "Скинути оформлення HTML-сторінок", "Darstellung von HTML-Seiten zurücksetzen",
            "Ripristina l'aspetto delle pagine HTML", "Restablecer la apariencia de las páginas HTML", "Réinitialiser l'apparence des pages HTML",
            "Redefinir a aparência das páginas HTML", "إعادة تعيين مظهر صفحات HTML", "HTML पेज का रूप रीसेट करें",
            "HTML পেজের রূপ রিসেট করুন", "HTML صفحات کی ظاہری شکل ری سیٹ کریں", "重置 HTML 页面样式")
        Add("Ссылки на другие сайты",
            "Links to other sites", "Посилання на інші сайти", "Links zu anderen Websites",
            "Collegamenti ad altri siti", "Enlaces a otros sitios", "Liens vers d'autres sites",
            "Links para outros sites", "الروابط إلى مواقع أخرى", "अन्य साइटों के लिंक",
            "অন্য সাইটের লিংক", "دوسری سائٹس کے لنکس", "指向其他网站的链接")
        Add("Только слова целиком",
            "Whole words only", "Лише слова цілком", "Nur ganze Wörter",
            "Solo parole intere", "Solo palabras completas", "Mots entiers uniquement",
            "Somente palavras inteiras", "كلمات كاملة فقط", "केवल पूरे शब्द",
            "শুধু পূর্ণ শব্দ", "صرف پورے الفاظ", "仅匹配整词")
        Add("Запоминать позицию в HTML-страницах",
            "Remember position in HTML pages", "Запам'ятовувати позицію в HTML-сторінках", "Position in HTML-Seiten merken",
            "Ricorda la posizione nelle pagine HTML", "Recordar la posición en las páginas HTML", "Mémoriser la position dans les pages HTML",
            "Lembrar a posição nas páginas HTML", "تذكّر الموضع في صفحات HTML", "HTML पेज में स्थिति याद रखें",
            "HTML পেজে অবস্থান মনে রাখুন", "HTML صفحات میں پوزیشن یاد رکھیں", "记住 HTML 页面中的位置")
        Add("Страницы из интернета",
            "Pages from the internet", "Сторінки з інтернету", "Seiten aus dem Internet",
            "Pagine da internet", "Páginas de internet", "Pages depuis internet",
            "Páginas da internet", "صفحات من الإنترنت", "इंटरनेट से पेज",
            "ইন্টারনেট থেকে পেজ", "انٹرنیٹ سے صفحات", "来自互联网的页面")
        Add("Внешние ресурсы локальных страниц",
            "External resources of local pages", "Зовнішні ресурси локальних сторінок", "Externe Ressourcen lokaler Seiten",
            "Risorse esterne delle pagine locali", "Recursos externos de las páginas locales", "Ressources externes des pages locales",
            "Recursos externos de páginas locais", "الموارد الخارجية للصفحات المحلية", "स्थानीय पेजों के बाहरी संसाधन",
            "লোকাল পেজের বাহ্যিক রিসোর্স", "مقامی صفحات کے بیرونی وسائل", "本地页面的外部资源")
        Add("Страниц в истории чтения",
            "Pages in reading history", "Сторінок в історії читання", "Seiten im Leseverlauf",
            "Pagine nella cronologia di lettura", "Páginas en el historial de lectura", "Pages dans l'historique de lecture",
            "Páginas no histórico de leitura", "الصفحات في سجل القراءة", "पढ़ने के इतिहास में पेज",
            "পড়ার ইতিহাসে পেজ", "مطالعے کی ہسٹری میں صفحات", "阅读历史中的页面数")
        Add("Кэш HTML-страниц",
            "HTML page cache", "Кеш HTML-сторінок", "Cache der HTML-Seiten",
            "Cache delle pagine HTML", "Caché de páginas HTML", "Cache des pages HTML",
            "Cache de páginas HTML", "ذاكرة التخزين المؤقت لصفحات HTML", "HTML पेज का कैश",
            "HTML পেজের ক্যাশ", "HTML صفحات کا کیش", "HTML 页面缓存")

        ' Row descriptions.
        Add("Показывает HTML-файлы как страницы в защищённом окне: скрипты, формы и загрузки не выполняются, файл не изменяется. Пока параметр выключен, HTML открывается как исходный текст.",
            "Shows HTML files as pages in a protected window: scripts, forms and downloads do not run, and the file is not modified. While this option is off, HTML opens as source text.",
            "Показує HTML-файли як сторінки в захищеному вікні: скрипти, форми та завантаження не виконуються, файл не змінюється. Поки параметр вимкнено, HTML відкривається як вихідний текст.",
            "Zeigt HTML-Dateien als Seiten in einem geschützten Fenster an: Skripte, Formulare und Downloads werden nicht ausgeführt, die Datei wird nicht verändert. Solange die Option ausgeschaltet ist, wird HTML als Quelltext geöffnet.",
            "Mostra i file HTML come pagine in una finestra protetta: script, moduli e download non vengono eseguiti e il file non viene modificato. Finché l'opzione è disattivata, l'HTML si apre come testo sorgente.",
            "Muestra los archivos HTML como páginas en una ventana protegida: los scripts, formularios y descargas no se ejecutan y el archivo no se modifica. Mientras la opción esté desactivada, el HTML se abre como texto fuente.",
            "Affiche les fichiers HTML sous forme de pages dans une fenêtre protégée : les scripts, formulaires et téléchargements ne sont pas exécutés et le fichier n'est pas modifié. Tant que l'option est désactivée, le HTML s'ouvre en texte source.",
            "Mostra os arquivos HTML como páginas em uma janela protegida: scripts, formulários e downloads não são executados e o arquivo não é alterado. Enquanto a opção estiver desativada, o HTML é aberto como texto-fonte.",
            "يعرض ملفات HTML كصفحات في نافذة محمية: لا تُنفَّذ النصوص البرمجية والنماذج والتنزيلات، ولا يتغيّر الملف. ما دام الخيار معطّلًا يُفتح HTML كنص مصدر.",
            "HTML फ़ाइलों को सुरक्षित विंडो में पेज के रूप में दिखाता है: स्क्रिप्ट, फ़ॉर्म और डाउनलोड नहीं चलते, फ़ाइल नहीं बदलती। विकल्प बंद रहने तक HTML स्रोत पाठ के रूप में खुलता है।",
            "HTML ফাইলকে সুরক্ষিত উইন্ডোতে পেজ হিসেবে দেখায়: স্ক্রিপ্ট, ফর্ম ও ডাউনলোড চলে না, ফাইল বদলায় না। বিকল্পটি বন্ধ থাকলে HTML উৎস পাঠ হিসেবে খোলে।",
            "HTML فائلوں کو محفوظ ونڈو میں صفحات کے طور پر دکھاتا ہے: اسکرپٹس، فارم اور ڈاؤن لوڈز نہیں چلتے، فائل تبدیل نہیں ہوتی۔ جب تک یہ آپشن بند ہے، HTML ماخذ متن کے طور پر کھلتا ہے۔",
            "在受保护的窗口中将 HTML 文件显示为页面：不运行脚本、表单和下载，也不修改文件。该选项关闭时，HTML 将作为源文本打开。")
        Add("«Оригинал» показывает страницу так, как её оформил автор. «Режим чтения» применяет тему, шрифт, ширину и поля из этой группы. Файл не изменяется.",
            "«Original» shows the page the way its author styled it. «Reading mode» applies the theme, font, width and margins from this group. The file is not modified.",
            "«Оригінал» показує сторінку так, як її оформив автор. «Режим читання» застосовує тему, шрифт, ширину та поля з цієї групи. Файл не змінюється.",
            "«Original» zeigt die Seite so, wie der Autor sie gestaltet hat. «Lesemodus» wendet Design, Schrift, Breite und Ränder aus dieser Gruppe an. Die Datei wird nicht verändert.",
            "«Originale» mostra la pagina come l'ha impostata l'autore. «Modalità lettura» applica tema, carattere, larghezza e margini di questo gruppo. Il file non viene modificato.",
            "«Original» muestra la página tal como la diseñó su autor. «Modo de lectura» aplica el tema, la fuente, el ancho y los márgenes de este grupo. El archivo no se modifica.",
            "«Original» affiche la page telle que son auteur l'a mise en forme. «Mode lecture» applique le thème, la police, la largeur et les marges de ce groupe. Le fichier n'est pas modifié.",
            "«Original» mostra a página como o autor a formatou. «Modo de leitura» aplica o tema, a fonte, a largura e as margens deste grupo. O arquivo não é alterado.",
            "«الأصل» يعرض الصفحة كما نسّقها مؤلفها. «وضع القراءة» يطبّق السمة والخط والعرض والهوامش من هذه المجموعة. لا يتغيّر الملف.",
            "«मूल» पेज को वैसे दिखाता है जैसा लेखक ने उसे सजाया है। «पठन मोड» इस समूह की थीम, फ़ॉन्ट, चौड़ाई और हाशिये लागू करता है। फ़ाइल नहीं बदलती।",
            "«মূল» পেজটি লেখকের সাজানো রূপেই দেখায়। «পড়ার মোড» এই গ্রুপের থিম, ফন্ট, প্রস্থ ও মার্জিন প্রয়োগ করে। ফাইল বদলায় না।",
            "«اصل» صفحہ کو اسی طرح دکھاتا ہے جیسے مصنف نے اسے ترتیب دیا ہے۔ «مطالعہ موڈ» اس گروپ کی تھیم، فونٹ، چوڑائی اور حاشیے لاگو کرتا ہے۔ فائل تبدیل نہیں ہوتی۔",
            "「原始」按作者设定的样式显示页面。「阅读模式」应用此组中的主题、字体、宽度和边距。文件不会被修改。")
        Add("Начальный масштаб страницы. Для отдельной страницы его можно изменить при чтении.",
            "The initial page zoom. For an individual page it can be changed while reading.", "Початковий масштаб сторінки. Для окремої сторінки його можна змінити під час читання.", "Der anfängliche Seitenzoom. Für eine einzelne Seite lässt er sich beim Lesen ändern.",
            "Zoom iniziale della pagina. Per una singola pagina può essere modificato durante la lettura.", "Zoom inicial de la página. En una página concreta se puede cambiar durante la lectura.", "Zoom initial de la page. Pour une page donnée, il peut être modifié pendant la lecture.",
            "Zoom inicial da página. Em uma página específica, ele pode ser alterado durante a leitura.", "التكبير الأولي للصفحة. يمكن تغييره لصفحة بعينها أثناء القراءة.", "पेज का प्रारंभिक ज़ूम। किसी एक पेज के लिए इसे पढ़ते समय बदला जा सकता है।",
            "পেজের প্রাথমিক জুম। আলাদা কোনো পেজের জন্য পড়ার সময় এটি বদলানো যায়।", "صفحے کا ابتدائی زوم۔ کسی ایک صفحے کے لیے اسے پڑھتے وقت بدلا جا سکتا ہے۔", "页面的初始缩放比例。对于单个页面，可在阅读时更改。")
        Add("Проверка выполняется только по кнопке: ничего не устанавливается и не скачивается.",
            "The check runs only when you press the button: nothing is installed or downloaded.", "Перевірка виконується лише за кнопкою: нічого не встановлюється і не завантажується.", "Die Prüfung läuft nur auf Knopfdruck: Es wird nichts installiert oder heruntergeladen.",
            "Il controllo viene eseguito solo con il pulsante: non viene installato né scaricato nulla.", "La comprobación se realiza solo con el botón: no se instala ni se descarga nada.", "La vérification n'a lieu que sur le bouton : rien n'est installé ni téléchargé.",
            "A verificação é feita apenas pelo botão: nada é instalado nem baixado.", "يتم الفحص بالضغط على الزر فقط: لا يُثبَّت ولا يُنزَّل أي شيء.", "जाँच केवल बटन दबाने पर होती है: कुछ भी इंस्टॉल या डाउनलोड नहीं होता।",
            "যাচাই শুধু বোতাম চাপলেই হয়: কিছু ইনস্টল বা ডাউনলোড হয় না।", "جانچ صرف بٹن دبانے پر ہوتی ہے: کچھ بھی انسٹال یا ڈاؤن لوڈ نہیں ہوتا۔", "仅在点击按钮时才会检查：不会安装或下载任何内容。")
        Add("Открывает в браузере страницу Microsoft, где компонент можно установить или обновить. Страница открывается только по щелчку.",
            "Opens in the browser a Microsoft page where the component can be installed or updated. The page opens only when you click.", "Відкриває в браузері сторінку Microsoft, де компонент можна встановити або оновити. Сторінка відкривається лише за клацанням.", "Öffnet im Browser eine Microsoft-Seite, auf der sich die Komponente installieren oder aktualisieren lässt. Die Seite wird nur per Klick geöffnet.",
            "Apre nel browser una pagina Microsoft da cui il componente può essere installato o aggiornato. La pagina si apre solo con un clic.", "Abre en el navegador una página de Microsoft donde se puede instalar o actualizar el componente. La página se abre solo al hacer clic.", "Ouvre dans le navigateur une page Microsoft où le composant peut être installé ou mis à jour. La page ne s'ouvre qu'au clic.",
            "Abre no navegador uma página da Microsoft onde o componente pode ser instalado ou atualizado. A página é aberta somente com um clique.", "يفتح في المتصفح صفحة من Microsoft يمكن منها تثبيت المكوّن أو تحديثه. لا تُفتح الصفحة إلا بالنقر.", "ब्राउज़र में Microsoft का वह पेज खोलता है जहाँ से घटक इंस्टॉल या अपडेट किया जा सकता है। पेज केवल क्लिक करने पर खुलता है।",
            "ব্রাউজারে Microsoft-এর সেই পেজ খোলে যেখান থেকে উপাদানটি ইনস্টল বা আপডেট করা যায়। পেজটি শুধু ক্লিক করলেই খোলে।", "براؤزر میں Microsoft کا وہ صفحہ کھولتا ہے جہاں سے جزو انسٹال یا اپ ڈیٹ کیا جا سکتا ہے۔ صفحہ صرف کلک کرنے پر کھلتا ہے۔", "在浏览器中打开 Microsoft 页面，可在其中安装或更新该组件。仅在点击时才会打开该页面。")
        Add("Цвета страницы в режиме чтения. «Как в системе» следует теме Windows.",
            "Page colors in reading mode. «Same as system» follows the Windows theme.", "Кольори сторінки в режимі читання. «Як у системі» наслідує тему Windows.", "Seitenfarben im Lesemodus. «Wie im System» folgt dem Windows-Design.",
            "Colori della pagina in modalità lettura. «Come nel sistema» segue il tema di Windows.", "Colores de la página en modo de lectura. «Como el sistema» sigue el tema de Windows.", "Couleurs de la page en mode lecture. «Comme le système» suit le thème de Windows.",
            "Cores da página no modo de leitura. «Como no sistema» segue o tema do Windows.", "ألوان الصفحة في وضع القراءة. «كما في النظام» يتبع سمة Windows.", "पठन मोड में पेज के रंग। «सिस्टम जैसा» Windows की थीम का पालन करता है।",
            "পড়ার মোডে পেজের রং। «সিস্টেমের মতো» Windows-এর থিম অনুসরণ করে।", "مطالعہ موڈ میں صفحے کے رنگ۔ «سسٹم کی طرح» Windows کی تھیم کی پیروی کرتا ہے۔", "阅读模式下的页面颜色。「跟随系统」遵循 Windows 主题。")
        Add("Шрифт страницы в режиме чтения. «Как в документе» оставляет шрифт автора.",
            "The page font in reading mode. «As in document» keeps the author's font.", "Шрифт сторінки в режимі читання. «Як у документі» залишає шрифт автора.", "Die Seitenschrift im Lesemodus. «Wie im Dokument» behält die Schrift des Autors bei.",
            "Il carattere della pagina in modalità lettura. «Come nel documento» mantiene il carattere dell'autore.", "La fuente de la página en modo de lectura. «Como en el documento» conserva la fuente del autor.", "La police de la page en mode lecture. «Comme dans le document» conserve la police de l'auteur.",
            "A fonte da página no modo de leitura. «Como no documento» mantém a fonte do autor.", "خط الصفحة في وضع القراءة. «كما في المستند» يُبقي خط المؤلف.", "पठन मोड में पेज का फ़ॉन्ट। «दस्तावेज़ के अनुसार» लेखक का फ़ॉन्ट बनाए रखता है।",
            "পড়ার মোডে পেজের ফন্ট। «নথি অনুযায়ী» লেখকের ফন্ট বজায় রাখে।", "مطالعہ موڈ میں صفحے کا فونٹ۔ «دستاویز کے مطابق» مصنف کا فونٹ برقرار رکھتا ہے۔", "阅读模式下的页面字体。「遵循文档」保留作者的字体。")
        Add("Размер текста в режиме чтения в пикселях страницы; не зависит от масштаба экрана.",
            "Text size in reading mode in page pixels; independent of the screen scale.", "Розмір тексту в режимі читання в пікселях сторінки; не залежить від масштабу екрана.", "Textgröße im Lesemodus in Seitenpixeln; unabhängig von der Bildschirmskalierung.",
            "Dimensione del testo in modalità lettura in pixel della pagina; non dipende dalla scala dello schermo.", "Tamaño del texto en modo de lectura, en píxeles de la página; no depende de la escala de la pantalla.", "Taille du texte en mode lecture, en pixels de la page ; indépendante de l'échelle de l'écran.",
            "Tamanho do texto no modo de leitura, em pixels da página; não depende da escala da tela.", "حجم النص في وضع القراءة بوحدات بكسل الصفحة؛ لا يتأثر بمقياس الشاشة.", "पठन मोड में टेक्स्ट का आकार पेज के पिक्सेल में; स्क्रीन के स्केल पर निर्भर नहीं।",
            "পড়ার মোডে লেখার আকার পেজের পিক্সেলে; স্ক্রিনের স্কেলের ওপর নির্ভর করে না।", "مطالعہ موڈ میں متن کا سائز صفحے کے پکسلز میں؛ اسکرین کے اسکیل پر منحصر نہیں۔", "阅读模式下的文字大小，以页面像素计；与屏幕缩放无关。")
        Add("Расстояние между строками в режиме чтения.",
            "The spacing between lines in reading mode.", "Відстань між рядками в режимі читання.", "Der Zeilenabstand im Lesemodus.",
            "La spaziatura tra le righe in modalità lettura.", "El espacio entre líneas en modo de lectura.", "L'interligne en mode lecture.",
            "O espaçamento entre linhas no modo de leitura.", "المسافة بين الأسطر في وضع القراءة.", "पठन मोड में पंक्तियों के बीच की दूरी।",
            "পড়ার মোডে লাইনগুলির মধ্যে দূরত্ব।", "مطالعہ موڈ میں سطروں کے درمیان فاصلہ۔", "阅读模式下的行间距。")
        Add("Ширина колонки текста в режиме чтения в символах. «На всю ширину» не ограничивает колонку.",
            "Text column width in reading mode, in characters. «Full width» does not limit the column.", "Ширина колонки тексту в режимі читання в символах. «На всю ширину» не обмежує колонку.", "Breite der Textspalte im Lesemodus in Zeichen. «Volle Breite» begrenzt die Spalte nicht.",
            "Larghezza della colonna di testo in modalità lettura, in caratteri. «Larghezza intera» non limita la colonna.", "Ancho de la columna de texto en modo de lectura, en caracteres. «Ancho completo» no limita la columna.", "Largeur de la colonne de texte en mode lecture, en caractères. «Pleine largeur» ne limite pas la colonne.",
            "Largura da coluna de texto no modo de leitura, em caracteres. «Largura total» não limita a coluna.", "عرض عمود النص في وضع القراءة بالأحرف. «العرض الكامل» لا يقيّد العمود.", "पठन मोड में टेक्स्ट कॉलम की चौड़ाई अक्षरों में। «पूरी चौड़ाई» कॉलम को सीमित नहीं करती।",
            "পড়ার মোডে লেখার কলামের প্রস্থ অক্ষরে। «পূর্ণ প্রস্থ» কলামকে সীমিত করে না।", "مطالعہ موڈ میں متن کے کالم کی چوڑائی حروف میں۔ «پوری چوڑائی» کالم کو محدود نہیں کرتی۔", "阅读模式下文本列的宽度，以字符计。「全宽」不限制列宽。")
        Add("Поля страницы в режиме чтения в пикселях страницы; размер окна не меняется.",
            "Page margins in reading mode in page pixels; the window size does not change.", "Поля сторінки в режимі читання в пікселях сторінки; розмір вікна не змінюється.", "Seitenränder im Lesemodus in Seitenpixeln; die Fenstergröße ändert sich nicht.",
            "Margini della pagina in modalità lettura in pixel della pagina; la dimensione della finestra non cambia.", "Márgenes de la página en modo de lectura, en píxeles de la página; el tamaño de la ventana no cambia.", "Marges de la page en mode lecture, en pixels de la page ; la taille de la fenêtre ne change pas.",
            "Margens da página no modo de leitura, em pixels da página; o tamanho da janela não muda.", "هوامش الصفحة في وضع القراءة بوحدات بكسل الصفحة؛ لا يتغيّر حجم النافذة.", "पठन मोड में पेज के हाशिये पेज के पिक्सेल में; विंडो का आकार नहीं बदलता।",
            "পড়ার মোডে পেজের মার্জিন পেজের পিক্সেলে; উইন্ডোর আকার বদলায় না।", "مطالعہ موڈ میں صفحے کے حاشیے صفحے کے پکسلز میں؛ ونڈو کا سائز تبدیل نہیں ہوتا۔", "阅读模式下的页面边距，以页面像素计；窗口大小不会改变。")
        Add("Длинные строки таблиц и блоков кода переносятся, а не прокручиваются в стороны.",
            "Long lines of tables and code blocks are wrapped instead of scrolling sideways.", "Довгі рядки таблиць і блоків коду переносяться, а не прокручуються вбік.", "Lange Zeilen von Tabellen und Codeblöcken werden umbrochen, statt zur Seite zu scrollen.",
            "Le righe lunghe di tabelle e blocchi di codice vanno a capo invece di scorrere di lato.", "Las líneas largas de tablas y bloques de código se ajustan en lugar de desplazarse hacia los lados.", "Les longues lignes des tableaux et des blocs de code sont renvoyées à la ligne au lieu de défiler sur le côté.",
            "As linhas longas de tabelas e blocos de código são quebradas em vez de rolar para os lados.", "تُلتَفّ الأسطر الطويلة في الجداول وكتل الشيفرة بدلًا من التمرير جانبيًا.", "तालिकाओं और कोड ब्लॉक की लंबी पंक्तियाँ बगल में स्क्रोल होने के बजाय लपेटी जाती हैं।",
            "টেবিল ও কোড ব্লকের লম্বা লাইন পাশে স্ক্রল না হয়ে র‍্যাপ হয়ে যায়।", "جدولوں اور کوڈ بلاکس کی لمبی سطریں پہلو میں اسکرول ہونے کے بجائے لپیٹ دی جاتی ہیں۔", "表格和代码块中的长行会自动换行，而不是横向滚动。")
        Add("Помнит тему, шрифт и масштаб, выбранные для отдельной страницы.",
            "Remembers the theme, font and zoom chosen for an individual page.", "Пам'ятає тему, шрифт і масштаб, обрані для окремої сторінки.", "Merkt sich Design, Schrift und Zoom, die für eine einzelne Seite gewählt wurden.",
            "Ricorda tema, carattere e zoom scelti per la singola pagina.", "Recuerda el tema, la fuente y el zoom elegidos para cada página.", "Mémorise le thème, la police et le zoom choisis pour chaque page.",
            "Lembra o tema, a fonte e o zoom escolhidos para cada página.", "يتذكّر السمة والخط والتكبير المختارة لصفحة بعينها.", "किसी एक पेज के लिए चुनी गई थीम, फ़ॉन्ट और ज़ूम याद रखता है।",
            "আলাদা কোনো পেজের জন্য বেছে নেওয়া থিম, ফন্ট ও জুম মনে রাখে।", "کسی ایک صفحے کے لیے منتخب کی گئی تھیم، فونٹ اور زوم یاد رکھتا ہے۔", "记住为单个页面选择的主题、字体和缩放比例。")
        Add("Возвращает оформление HTML к значениям по умолчанию и убирает оформление отдельных страниц. Позиции чтения остаются.",
            "Returns the HTML appearance to its defaults and removes the appearance of individual pages. Reading positions are kept.", "Повертає оформлення HTML до значень за замовчуванням і прибирає оформлення окремих сторінок. Позиції читання залишаються.", "Setzt die HTML-Darstellung auf die Standardwerte zurück und entfernt die Darstellung einzelner Seiten. Die Leseposition bleibt erhalten.",
            "Riporta l'aspetto HTML ai valori predefiniti e rimuove l'aspetto delle singole pagine. Le posizioni di lettura restano.", "Devuelve la apariencia HTML a los valores predeterminados y quita la apariencia de páginas concretas. Las posiciones de lectura se conservan.", "Rétablit l'apparence HTML aux valeurs par défaut et supprime l'apparence de chaque page. Les positions de lecture sont conservées.",
            "Devolve a aparência HTML aos valores padrão e remove a aparência de páginas individuais. As posições de leitura permanecem.", "يعيد مظهر HTML إلى القيم الافتراضية ويزيل مظهر الصفحات المفردة. تبقى مواضع القراءة.", "HTML का रूप डिफ़ॉल्ट मानों पर लौटाता है और अलग-अलग पेज का रूप हटा देता है। पढ़ने की स्थितियाँ बनी रहती हैं।",
            "HTML-এর রূপ ডিফল্ট মানে ফিরিয়ে আনে এবং আলাদা আলাদা পেজের রূপ সরিয়ে দেয়। পড়ার অবস্থান থেকে যায়।", "HTML کی ظاہری شکل کو طے شدہ قدروں پر لوٹاتا ہے اور الگ الگ صفحات کی ظاہری شکل ہٹا دیتا ہے۔ مطالعے کی پوزیشنیں باقی رہتی ہیں۔", "将 HTML 样式恢复为默认值，并移除各个页面的样式。阅读位置会保留。")
        Add("Что делать со ссылкой на другой сайт: «Спрашивать» открывает браузер после подтверждения, «Блокировать» не открывает ничего.",
            "What to do with a link to another site: «Ask» opens the browser after confirmation, «Block» opens nothing.", "Що робити з посиланням на інший сайт: «Запитувати» відкриває браузер після підтвердження, «Блокувати» не відкриває нічого.", "Was mit einem Link zu einer anderen Website geschehen soll: «Nachfragen» öffnet den Browser nach Bestätigung, «Blockieren» öffnet nichts.",
            "Cosa fare con un collegamento a un altro sito: «Chiedi» apre il browser dopo la conferma, «Blocca» non apre nulla.", "Qué hacer con un enlace a otro sitio: «Preguntar» abre el navegador tras la confirmación, «Bloquear» no abre nada.", "Que faire d'un lien vers un autre site : «Demander» ouvre le navigateur après confirmation, «Bloquer» n'ouvre rien.",
            "O que fazer com um link para outro site: «Perguntar» abre o navegador após a confirmação, «Bloquear» não abre nada.", "ما يُفعل بالرابط المؤدي إلى موقع آخر: «السؤال» يفتح المتصفح بعد التأكيد، و«حظر» لا يفتح شيئًا.", "दूसरी साइट के लिंक का क्या करें: «पूछें» पुष्टि के बाद ब्राउज़र खोलता है, «ब्लॉक करें» कुछ नहीं खोलता।",
            "অন্য সাইটের লিংকের ক্ষেত্রে কী করবেন: «জিজ্ঞাসা করুন» নিশ্চিত করার পর ব্রাউজার খোলে, «ব্লক করুন» কিছুই খোলে না।", "دوسری سائٹ کے لنک کا کیا کرنا ہے: «پوچھیں» تصدیق کے بعد براؤزر کھولتا ہے، «مسدود کریں» کچھ نہیں کھولتا۔", "如何处理指向其他网站的链接：「询问」会在确认后打开浏览器，「阻止」则什么都不打开。")
        Add("Поиск по странице различает заглавные и строчные буквы; применяется к новому поиску.",
            "Searching the page distinguishes uppercase and lowercase letters; applies to a new search.", "Пошук по сторінці розрізняє великі та малі літери; застосовується до нового пошуку.", "Die Suche auf der Seite unterscheidet Groß- und Kleinschreibung; gilt für die nächste Suche.",
            "La ricerca nella pagina distingue maiuscole e minuscole; vale per la nuova ricerca.", "La búsqueda en la página distingue mayúsculas de minúsculas; se aplica a la búsqueda nueva.", "La recherche dans la page distingue les majuscules des minuscules ; elle s'applique à la nouvelle recherche.",
            "A pesquisa na página diferencia maiúsculas de minúsculas; vale para a nova pesquisa.", "يميّز البحث في الصفحة بين الأحرف الكبيرة والصغيرة؛ ويُطبَّق على البحث الجديد.", "पेज में खोज बड़े और छोटे अक्षरों में अंतर करती है; यह नई खोज पर लागू होता है।",
            "পেজে খোঁজা বড় ও ছোট হাতের অক্ষরের পার্থক্য করে; এটি নতুন খোঁজে প্রযোজ্য।", "صفحے میں تلاش بڑے اور چھوٹے حروف میں فرق کرتی ہے؛ یہ نئی تلاش پر لاگو ہوتا ہے۔", "页面内查找区分大小写；适用于新的查找。")
        Add("Поиск находит только слова целиком; границы слов определяет компонент просмотра с учётом языка.",
            "Search finds whole words only; the viewer component determines word boundaries according to the language.", "Пошук знаходить лише слова цілком; межі слів визначає компонент перегляду з урахуванням мови.", "Die Suche findet nur ganze Wörter; die Wortgrenzen bestimmt die Anzeigekomponente je nach Sprache.",
            "La ricerca trova solo parole intere; i confini delle parole sono determinati dal componente di visualizzazione in base alla lingua.", "La búsqueda solo encuentra palabras completas; el componente de la vista determina los límites de las palabras según el idioma.", "La recherche ne trouve que des mots entiers ; le composant d'affichage détermine les limites des mots selon la langue.",
            "A pesquisa encontra apenas palavras inteiras; os limites das palavras são determinados pelo componente de visualização conforme o idioma.", "يعثر البحث على الكلمات الكاملة فقط؛ ويحدّد مكوّن العرض حدود الكلمات وفق اللغة.", "खोज केवल पूरे शब्द ढूँढती है; शब्दों की सीमाएँ भाषा के अनुसार देखने वाला घटक तय करता है।",
            "খোঁজা শুধু পূর্ণ শব্দ খুঁজে পায়; শব্দের সীমানা ভাষা অনুযায়ী দেখার উপাদান ঠিক করে।", "تلاش صرف پورے الفاظ تلاش کرتی ہے؛ الفاظ کی حدود زبان کے مطابق دیکھنے والا جزو طے کرتا ہے۔", "查找仅匹配整词；词的边界由查看组件根据语言确定。")
        Add("Помнит место, где вы остановились. Выключение прекращает новые записи; уже сохранённое удаляет очистка истории.",
            "Remembers where you stopped. Turning it off stops new records; what is already saved is removed by clearing the history.", "Пам'ятає місце, де ви зупинилися. Вимкнення припиняє нові записи; вже збережене видаляє очищення історії.", "Merkt sich, wo Sie aufgehört haben. Das Ausschalten beendet neue Einträge; bereits Gespeichertes entfernt das Löschen des Verlaufs.",
            "Ricorda dove ti sei fermato. Disattivandolo non si creano nuove registrazioni; ciò che è già salvato viene rimosso cancellando la cronologia.", "Recuerda dónde se quedó. Al desactivarlo se dejan de crear registros; lo ya guardado se elimina borrando el historial.", "Mémorise l'endroit où vous vous êtes arrêté. La désactivation arrête les nouveaux enregistrements ; ce qui est déjà enregistré est supprimé en effaçant l'historique.",
            "Lembra onde você parou. Desativar interrompe novos registros; o que já foi salvo é removido ao limpar o histórico.", "يتذكّر المكان الذي توقفت عنده. يؤدي التعطيل إلى إيقاف السجلات الجديدة؛ أما المحفوظ بالفعل فيُزال بمسح السجل.", "वह जगह याद रखता है जहाँ आप रुके थे। बंद करने पर नई प्रविष्टियाँ बनना रुक जाता है; पहले से सहेजा गया इतिहास साफ़ करने पर हटता है।",
            "আপনি যেখানে থেমেছিলেন সেই জায়গা মনে রাখে। বন্ধ করলে নতুন রেকর্ড তৈরি হওয়া থামে; আগে সংরক্ষিত তথ্য ইতিহাস মুছলে সরে যায়।", "وہ جگہ یاد رکھتا ہے جہاں آپ رکے تھے۔ بند کرنے سے نئے ریکارڈ بننا رک جاتا ہے؛ پہلے سے محفوظ شدہ کو ہسٹری صاف کرنے سے ہٹایا جاتا ہے۔", "记住您上次读到的位置。关闭后不再产生新记录；已保存的内容可通过清除历史记录来删除。")
        Add("Разрешает открывать HTML-страницу по адресу в интернете. Такие страницы открываются отдельно от локальных, без скриптов и входа в учётные записи.",
            "Allows opening an HTML page by its internet address. Such pages open separately from local ones, without scripts and without signing in to accounts.", "Дозволяє відкривати HTML-сторінку за адресою в інтернеті. Такі сторінки відкриваються окремо від локальних, без скриптів і входу в облікові записи.", "Erlaubt das Öffnen einer HTML-Seite über ihre Internetadresse. Solche Seiten werden getrennt von lokalen geöffnet, ohne Skripte und ohne Anmeldung bei Konten.",
            "Consente di aprire una pagina HTML tramite il suo indirizzo internet. Queste pagine si aprono separatamente da quelle locali, senza script e senza accesso agli account.", "Permite abrir una página HTML por su dirección de internet. Estas páginas se abren por separado de las locales, sin scripts y sin inicio de sesión en cuentas.", "Permet d'ouvrir une page HTML par son adresse internet. Ces pages s'ouvrent séparément des pages locales, sans scripts et sans connexion aux comptes.",
            "Permite abrir uma página HTML pelo endereço na internet. Essas páginas são abertas separadamente das locais, sem scripts e sem login em contas.", "يتيح فتح صفحة HTML من عنوانها على الإنترنت. تُفتح هذه الصفحات بمعزل عن المحلية، بلا نصوص برمجية وبلا تسجيل الدخول إلى الحسابات.", "इंटरनेट पते से HTML पेज खोलने की अनुमति देता है। ऐसे पेज स्थानीय पेजों से अलग खुलते हैं, बिना स्क्रिप्ट और खातों में लॉगिन के।",
            "ইন্টারনেট ঠিকানা থেকে HTML পেজ খোলার অনুমতি দেয়। এ ধরনের পেজ লোকাল পেজ থেকে আলাদাভাবে খোলে, স্ক্রিপ্ট ও অ্যাকাউন্টে লগইন ছাড়াই।", "انٹرنیٹ کے پتے سے HTML صفحہ کھولنے کی اجازت دیتا ہے۔ ایسے صفحات مقامی صفحات سے الگ کھلتے ہیں، اسکرپٹس اور اکاؤنٹس میں لاگ اِن کے بغیر۔", "允许通过互联网地址打开 HTML 页面。这类页面与本地页面分开打开，不运行脚本，也不登录账户。")
        Add("Загрузку картинок и стилей с других сайтов для локальной страницы вы разрешаете при её открытии, отдельно для каждого сайта и только на текущий сеанс. Здесь это не сохраняется.",
            "You allow loading images and styles from other sites for a local page when you open it, separately for each site and only for the current session. It is not saved here.", "Завантаження зображень і стилів з інших сайтів для локальної сторінки ви дозволяєте під час її відкриття, окремо для кожного сайту і лише на поточний сеанс. Тут це не зберігається.", "Das Laden von Bildern und Stilen von anderen Websites für eine lokale Seite erlauben Sie beim Öffnen der Seite, für jede Website einzeln und nur für die aktuelle Sitzung. Hier wird das nicht gespeichert.",
            "Il caricamento di immagini e stili da altri siti per una pagina locale lo consenti all'apertura della pagina, separatamente per ogni sito e solo per la sessione corrente. Qui non viene salvato.", "La carga de imágenes y estilos de otros sitios para una página local la permite al abrirla, por separado para cada sitio y solo durante la sesión actual. Aquí no se guarda.", "Le chargement d'images et de styles depuis d'autres sites pour une page locale s'autorise à l'ouverture de la page, séparément pour chaque site et seulement pour la session en cours. Il n'est pas enregistré ici.",
            "O carregamento de imagens e estilos de outros sites para uma página local é permitido por você ao abri-la, separadamente para cada site e somente na sessão atual. Aqui isso não é salvo.", "تسمح بتحميل الصور والأنماط من مواقع أخرى لصفحة محلية عند فتحها، لكل موقع على حدة وللجلسة الحالية فقط. لا يُحفظ ذلك هنا.", "स्थानीय पेज के लिए अन्य साइटों से चित्र और शैलियाँ लोड करने की अनुमति आप पेज खोलते समय देते हैं, हर साइट के लिए अलग और केवल मौजूदा सत्र के लिए। यहाँ यह सहेजा नहीं जाता।",
            "লোকাল পেজের জন্য অন্য সাইট থেকে ছবি ও স্টাইল লোড করার অনুমতি আপনি পেজ খোলার সময় দেন, প্রতিটি সাইটের জন্য আলাদাভাবে এবং শুধু বর্তমান সেশনের জন্য। এখানে তা সংরক্ষিত হয় না।", "مقامی صفحے کے لیے دوسری سائٹس سے تصاویر اور اسٹائل لوڈ کرنے کی اجازت آپ صفحہ کھولتے وقت دیتے ہیں، ہر سائٹ کے لیے الگ اور صرف موجودہ سیشن کے لیے۔ یہاں یہ محفوظ نہیں ہوتی۔", "为本地页面加载其他网站的图片和样式，需要您在打开页面时允许，按网站分别允许，且仅限当前会话。此处不会保存。")
        Add("Сколько страниц помнит программа. Ноль отключает запись позиций и оформления.",
            "How many pages the program remembers. Zero turns off recording of positions and appearance.", "Скільки сторінок пам'ятає програма. Нуль вимикає запис позицій і оформлення.", "Wie viele Seiten sich das Programm merkt. Null schaltet das Aufzeichnen von Positionen und Darstellung aus.",
            "Quante pagine ricorda il programma. Zero disattiva la registrazione di posizioni e aspetto.", "Cuántas páginas recuerda el programa. Cero desactiva el registro de posiciones y apariencia.", "Combien de pages le programme mémorise. Zéro désactive l'enregistrement des positions et de l'apparence.",
            "Quantas páginas o programa lembra. Zero desativa o registro de posições e aparência.", "عدد الصفحات التي يتذكرها البرنامج. الصفر يعطّل تسجيل المواضع والمظهر.", "प्रोग्राम कितने पेज याद रखता है। शून्य होने पर स्थितियों और रूप का रिकॉर्ड रखना बंद हो जाता है।",
            "প্রোগ্রাম কতগুলি পেজ মনে রাখে। শূন্য হলে অবস্থান ও রূপ রেকর্ড করা বন্ধ হয়।", "پروگرام کتنے صفحات یاد رکھتا ہے۔ صفر ہونے پر پوزیشنوں اور ظاہری شکل کی ریکارڈنگ بند ہو جاتی ہے۔", "程序记住的页面数量。设为零则不再记录位置和样式。")
        Add("Сколько памяти занимают копии открытых страниц и их ресурсов; входит в общий кэш документов.",
            "How much memory the copies of open pages and their resources take; counted in the overall document cache.", "Скільки пам'яті займають копії відкритих сторінок і їхніх ресурсів; входить до загального кешу документів.", "Wie viel Speicher die Kopien geöffneter Seiten und ihrer Ressourcen belegen; zählt zum gesamten Dokument-Cache.",
            "Quanta memoria occupano le copie delle pagine aperte e delle loro risorse; rientra nella cache complessiva dei documenti.", "Cuánta memoria ocupan las copias de las páginas abiertas y de sus recursos; forma parte de la caché general de documentos.", "Quelle quantité de mémoire occupent les copies des pages ouvertes et de leurs ressources ; elle fait partie du cache général des documents.",
            "Quanta memória as cópias das páginas abertas e de seus recursos ocupam; faz parte do cache geral de documentos.", "مقدار الذاكرة التي تشغلها نسخ الصفحات المفتوحة وموارِدها؛ ويدخل ضمن ذاكرة المستندات المؤقتة العامة.", "खुले पेजों और उनके संसाधनों की प्रतियाँ कितनी मेमोरी लेती हैं; यह दस्तावेज़ों के कुल कैश में शामिल है।",
            "খোলা পেজ ও তাদের রিসোর্সের কপি কতটা মেমরি নেয়; এটি নথির মোট ক্যাশের অন্তর্ভুক্ত।", "کھلے صفحات اور ان کے وسائل کی نقول کتنی میموری لیتی ہیں؛ یہ دستاویزات کے مجموعی کیش میں شامل ہے۔", "已打开页面及其资源的副本所占用的内存大小；计入文档总缓存。")
        Add("Удаляет позиции чтения и оформление отдельных HTML-страниц. Сами файлы и общие настройки не меняются.",
            "Deletes the reading positions and the appearance of individual HTML pages. The files themselves and the general settings are not changed.", "Видаляє позиції читання та оформлення окремих HTML-сторінок. Самі файли та загальні налаштування не змінюються.", "Löscht die Lesepositionen und die Darstellung einzelner HTML-Seiten. Die Dateien selbst und die allgemeinen Einstellungen bleiben unverändert.",
            "Elimina le posizioni di lettura e l'aspetto delle singole pagine HTML. I file stessi e le impostazioni generali non cambiano.", "Elimina las posiciones de lectura y la apariencia de páginas HTML concretas. Los archivos y la configuración general no cambian.", "Supprime les positions de lecture et l'apparence de chaque page HTML. Les fichiers eux-mêmes et les paramètres généraux ne changent pas.",
            "Exclui as posições de leitura e a aparência de páginas HTML individuais. Os próprios arquivos e as configurações gerais não são alterados.", "يحذف مواضع القراءة ومظهر صفحات HTML المفردة. لا تتغير الملفات نفسها ولا الإعدادات العامة.", "अलग-अलग HTML पेज की पढ़ने की स्थितियाँ और रूप हटाता है। फ़ाइलें और सामान्य सेटिंग्स नहीं बदलतीं।",
            "আলাদা আলাদা HTML পেজের পড়ার অবস্থান ও রূপ মুছে দেয়। ফাইলগুলো নিজে এবং সাধারণ সেটিংস বদলায় না।", "الگ الگ HTML صفحات کی مطالعے کی پوزیشنیں اور ظاہری شکل حذف کرتا ہے۔ فائلیں خود اور عمومی ترتیبات تبدیل نہیں ہوتیں۔", "删除各个 HTML 页面的阅读位置和样式。文件本身和常规设置不会改变。")
        Add("Удаляет временные данные, которые создаёт окно просмотра. Профили браузера не затрагиваются.",
            "Deletes the temporary data that the viewer window creates. Browser profiles are not affected.", "Видаляє тимчасові дані, які створює вікно перегляду. Профілі браузера не зачіпаються.", "Löscht die temporären Daten, die das Ansichtsfenster erzeugt. Browserprofile sind davon nicht betroffen.",
            "Elimina i dati temporanei creati dalla finestra di visualizzazione. I profili del browser non vengono toccati.", "Elimina los datos temporales que crea la ventana de vista. Los perfiles del navegador no se ven afectados.", "Supprime les données temporaires créées par la fenêtre d'affichage. Les profils du navigateur ne sont pas touchés.",
            "Exclui os dados temporários criados pela janela de visualização. Os perfis do navegador não são afetados.", "يحذف البيانات المؤقتة التي تنشئها نافذة العرض. لا تتأثر ملفات تعريف المتصفح.", "देखने वाली विंडो द्वारा बनाया गया अस्थायी डेटा हटाता है। ब्राउज़र प्रोफ़ाइल प्रभावित नहीं होतीं।",
            "দেখার উইন্ডো যে অস্থায়ী ডেটা তৈরি করে তা মুছে দেয়। ব্রাউজার প্রোফাইলে কোনো প্রভাব পড়ে না।", "دیکھنے والی ونڈو جو عارضی ڈیٹا بناتی ہے اسے حذف کرتا ہے۔ براؤزر پروفائلز متاثر نہیں ہوتیں۔", "删除查看窗口创建的临时数据。浏览器配置文件不受影响。")
    End Sub
End Class
