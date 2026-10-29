Option Strict On

' <summary>
' What the "Send the log to the author" flow says when a log exists but cannot be included, and what it
' says when no mail program exists (ticket 182 L-3 and L-6). One table for both programs: the archive
' builder (Support\LogPackage.vb) and the manual route (Support\MailSender.vb) are linked into the viewer
' and the Share Manager alike, so the sentences are registered by both Localization.Registry.vb files -
' linked into the Share Manager by <Compile Include>, like Localization.Theme.vb.
'
' Columns after the Russian key: en, uk, de, it, es, fr, pt, ar, hi, bn, ur, zh.
' Никогда не ставьте «умные» кавычки в литерал - VB считает U+201C/U+201D разделителями
' строки (see the localization rules in CLAUDE.md); используются «…» и 「…」.
' </summary>
Partial Public NotInheritable Class Localization

    Private Shared Sub AddSendLogsStrings()

        Add("Экспорт журналов для анализа", "Export logs for analysis", "Експорт журналів для аналізу", "Protokolle zur Analyse exportieren", "Esporta registri per l'analisi", "Exportar registros para análisis", "Exporter les journaux pour analyse", "Exportar registos para análise", "تصدير السجلات للتحليل", "विश्लेषण के लिए लॉग निर्यात करें", "বিশ্লেষণের জন্য লগ রপ্তানি", "تجزیے کے لیے لاگز برآمد کریں", "导出日志以供分析")
        Add("Сбор журналов..", "Collecting logs..", "Збір журналів..", "Protokolle werden gesammelt..", "Raccolta registri..", "Recopilando registros..", "Collecte des journaux..", "A recolher registos..", "جارٍ جمع السجلات..", "लॉग एकत्र किए जा रहे हैं..", "লগ সংগ্রহ করা হচ্ছে..", "لاگز جمع کیے جا رہے ہیں..", "正在收集日志..")
        Add("Источники журналов", "Log sources", "Джерела журналів", "Protokollquellen", "Origini dei registri", "Fuentes de registros", "Sources des journaux", "Origens dos registos", "مصادر السجلات", "लॉग स्रोत", "লগের উৎস", "لاگز کے ذرائع", "日志来源")
        Add("сетевое хранилище пропущено: экспорт только локальный", "network store omitted: local export only", "мережеве сховище пропущено: експорт лише локальний", "Netzwerkspeicher ausgelassen: nur lokaler Export", "archivio di rete omesso: solo esportazione locale", "almacenamiento de red omitido: solo exportación local", "stockage réseau omis : export local uniquement", "armazenamento de rede omitido: apenas exportação local", "تم تخطي التخزين الشبكي: تصدير محلي فقط", "नेटवर्क संग्रह छोड़ा गया: केवल स्थानीय निर्यात", "নেটওয়ার্ক স্টোর বাদ: শুধু স্থানীয় রপ্তানি", "نیٹ ورک ذخیرہ چھوڑ دیا گیا: صرف مقامی برآمد", "已跳过网络存储：仅导出本地日志")
        Add("{0} - файл сократился во время сбора", "{0} - file shortened during collection", "{0} - файл скоротився під час збору", "{0} - Datei während der Sammlung verkürzt", "{0} - file ridotto durante la raccolta", "{0} - el archivo se redujo durante la recopilación", "{0} - fichier raccourci pendant la collecte", "{0} - ficheiro reduzido durante a recolha", "{0} - تقلص الملف أثناء الجمع", "{0} - संग्रह के दौरान फ़ाइल छोटी हुई", "{0} - সংগ্রহের সময় ফাইল ছোট হয়েছে", "{0} - جمع کرنے کے دوران فائل مختصر ہو گئی", "{0} - 收集期间文件已缩短")
        Add("Путь к архиву", "Archive path", "Шлях до архіву", "Archivpfad", "Percorso dell'archivio", "Ruta del archivo", "Chemin de l'archive", "Caminho do arquivo", "مسار الأرشيف", "संग्रह का पथ", "আর্কাইভের পথ", "آرکائیو کا راستہ", "压缩包路径")
        Add("Отправить автору", "Send to author", "Надіслати автору", "An den Autor senden", "Invia all'autore", "Enviar al autor", "Envoyer à l'auteur", "Enviar ao autor", "إرسال إلى المؤلف", "लेखक को भेजें", "লেখককে পাঠান", "مصنف کو بھیجیں", "发送给作者")
        Add("файл исчез во время сбора", "file disappeared during collection", "файл зник під час збору", "Datei während der Sammlung verschwunden", "file scomparso durante la raccolta", "el archivo desapareció durante la recopilación", "fichier disparu pendant la collecte", "o ficheiro desapareceu durante a recolha", "اختفى الملف أثناء الجمع", "संग्रह के दौरान फ़ाइल गायब हो गई", "সংগ্রহের সময় ফাইলটি হারিয়ে গেছে", "جمع کرنے کے دوران فائل غائب ہو گئی", "收集期间文件已消失")
        Add("ссылка пропущена для безопасности", "link omitted for safety", "посилання пропущено задля безпеки", "Verknüpfung aus Sicherheitsgründen ausgelassen", "collegamento omesso per sicurezza", "enlace omitido por seguridad", "lien omis par sécurité", "ligação omitida por segurança", "تم تخطي الرابط للأمان", "सुरक्षा के लिए लिंक छोड़ा गया", "নিরাপত্তার জন্য লিঙ্ক বাদ দেওয়া হয়েছে", "حفاظت کے لیے لنک چھوڑ دیا گیا", "为安全起见已跳过链接")
        Add("не удалось получить список файлов", "directory could not be listed", "не вдалося отримати список файлів", "Verzeichnis konnte nicht aufgelistet werden", "impossibile elencare la cartella", "no se pudo listar la carpeta", "impossible de lister le dossier", "não foi possível listar a pasta", "تعذّر سرد المجلد", "फ़ोल्डर की सूची नहीं मिल सकी", "ফোল্ডারের তালিকা পাওয়া যায়নি", "فولڈر کی فہرست حاصل نہیں ہو سکی", "无法列出文件夹")
        Add("{0} - сокращён: сохранены начало и конец", "{0} - truncated: head and tail retained", "{0} - скорочено: збережено початок і кінець", "{0} - gekürzt: Anfang und Ende erhalten", "{0} - troncato: conservati inizio e fine", "{0} - truncado: inicio y final conservados", "{0} - tronqué : début et fin conservés", "{0} - truncado: início e fim mantidos", "{0} - مقتطع: تم الاحتفاظ بالبداية والنهاية", "{0} - छोटा किया गया: शुरुआत और अंत रखे गए", "{0} - সংক্ষিপ্ত: শুরু ও শেষ রাখা হয়েছে", "{0} - مختصر: آغاز اور اختتام محفوظ", "{0} - 已截断：保留开头和结尾")
        Add("{0} - пропущен: превышен предел чтения", "{0} - omitted: source exceeds read limit", "{0} - пропущено: перевищено межу читання", "{0} - ausgelassen: Leselimit überschritten", "{0} - omesso: limite di lettura superato", "{0} - omitido: supera el límite de lectura", "{0} - omis : limite de lecture dépassée", "{0} - omitido: limite de leitura excedido", "{0} - محذوف: تجاوز حد القراءة", "{0} - छोड़ा गया: पढ़ने की सीमा से अधिक", "{0} - বাদ: পড়ার সীমা অতিক্রম করেছে", "{0} - چھوڑ دیا گیا: پڑھنے کی حد سے زیادہ", "{0} - 已跳过：超过读取上限")
        Add("Архив неполный: часть журналов сокращена, пропущена или недоступна.",
            "Partial archive: some logs were truncated, omitted or unreadable.", "Архів неповний: частину журналів скорочено, пропущено або неможливо прочитати.",
            "Unvollständiges Archiv: Einige Protokolle wurden gekürzt, ausgelassen oder waren nicht lesbar.", "Archivio parziale: alcuni registri sono troncati, omessi o illeggibili.",
            "Archivo parcial: algunos registros se truncaron, omitieron o no se pudieron leer.", "Archive partielle : certains journaux sont tronqués, omis ou illisibles.",
            "Arquivo parcial: alguns registos foram truncados, omitidos ou não puderam ser lidos.", "أرشيف جزئي: بعض السجلات مقتطعة أو محذوفة أو غير قابلة للقراءة.",
            "अपूर्ण संग्रह: कुछ लॉग छोटे किए गए, छोड़े गए या पढ़े नहीं जा सके।", "আংশিক আর্কাইভ: কিছু লগ সংক্ষিপ্ত, বাদ দেওয়া বা অপঠনযোগ্য।", "نامکمل آرکائیو: کچھ لاگز مختصر، خارج یا ناقابل مطالعہ ہیں۔", "压缩包不完整：部分日志已截断、跳过或无法读取。")
        Add("Сбор доступных журналов завершён.", "Available logs collected.", "Збір доступних журналів завершено.", "Verfügbare Protokolle gesammelt.", "Registri disponibili raccolti.", "Registros disponibles recopilados.", "Journaux disponibles collectés.", "Registos disponíveis recolhidos.", "تم جمع السجلات المتاحة.", "उपलब्ध लॉग एकत्र किए गए।", "উপলব্ধ লগ সংগ্রহ করা হয়েছে।", "دستیاب لاگز جمع کر لیے گئے۔", "已收集可用日志。")
        Add("Локальные серверные журналы не найдены. В архиве только доступная диагностика приложения; удалённые журналы не собираются.",
            "No local server logs were found. The archive contains available application diagnostics only; remote logs are not collected.",
            "Локальних серверних журналів не знайдено. Архів містить лише доступну діагностику програми; віддалені журнали не збираються.",
            "Keine lokalen Serverprotokolle gefunden. Das Archiv enthält nur verfügbare Anwendungsdiagnosen; entfernte Protokolle werden nicht gesammelt.",
            "Nessun registro del server locale trovato. L'archivio contiene solo la diagnostica disponibile dell'applicazione; i registri remoti non vengono raccolti.",
            "No se encontraron registros del servidor local. El archivo solo contiene diagnósticos disponibles de la aplicación; no se recopilan registros remotos.",
            "Aucun journal de serveur local trouvé. L'archive contient uniquement les diagnostics disponibles de l'application ; les journaux distants ne sont pas collectés.",
            "Não foram encontrados registos de servidores locais. O arquivo contém apenas diagnósticos disponíveis da aplicação; não recolhe registos remotos.",
            "لم يُعثر على سجلات خادم محلي. يحتوي الأرشيف على تشخيصات التطبيق المتاحة فقط؛ لا تُجمع السجلات البعيدة.",
            "स्थानीय सर्वर लॉग नहीं मिले। संग्रह में केवल उपलब्ध ऐप निदान हैं; दूरस्थ लॉग एकत्र नहीं किए जाते।",
            "স্থানীয় সার্ভারের লগ পাওয়া যায়নি। আর্কাইভে শুধু উপলব্ধ অ্যাপের ডায়াগনস্টিক আছে; দূরবর্তী লগ সংগ্রহ করা হয় না।",
            "مقامی سرور کے لاگز نہیں ملے۔ آرکائیو میں صرف دستیاب ایپ کی تشخیص ہے؛ دور دراز کے لاگز جمع نہیں کیے جاتے۔",
            "未找到本地服务器日志。压缩包仅包含可用的应用诊断信息；不会收集远程日志。")
        Add("Секреты удалены. Проверьте архив перед отправкой: в журналах могут остаться имена файлов. Программа ничего не отправляет сама.",
            "Secrets have been removed. Review the archive before sending: logs may still contain filenames. The application sends nothing itself.",
            "Секрети видалено. Перевірте архів перед надсиланням: журнали можуть містити імена файлів. Програма сама нічого не надсилає.",
            "Geheimnisse wurden entfernt. Prüfen Sie das Archiv vor dem Senden: Protokolle können Dateinamen enthalten. Die Anwendung sendet nichts selbst.",
            "I segreti sono stati rimossi. Controlla l'archivio prima dell'invio: i registri possono contenere nomi di file. L'applicazione non invia nulla da sola.",
            "Se eliminaron los secretos. Revise el archivo antes de enviarlo: los registros pueden contener nombres de archivo. La aplicación no envía nada por sí sola.",
            "Les secrets ont été supprimés. Vérifiez l'archive avant l'envoi : les journaux peuvent contenir des noms de fichiers. L'application n'envoie rien elle-même.",
            "Os segredos foram removidos. Reveja o arquivo antes de enviar: os registos podem conter nomes de ficheiros. A aplicação não envia nada por si.",
            "أُزيلت الأسرار. راجع الأرشيف قبل الإرسال: قد تبقى أسماء ملفات في السجلات. لا يرسل التطبيق شيئًا بنفسه.",
            "गोपनीय जानकारी हटा दी गई है। भेजने से पहले संग्रह देखें: लॉग में फ़ाइल नाम रह सकते हैं। ऐप अपने आप कुछ नहीं भेजता।",
            "গোপন তথ্য সরানো হয়েছে। পাঠানোর আগে আর্কাইভ দেখুন: লগে ফাইলের নাম থাকতে পারে। অ্যাপ নিজে কিছু পাঠায় না।",
            "خفیہ معلومات ہٹا دی گئی ہیں۔ بھیجنے سے پہلے آرکائیو دیکھیں: لاگز میں فائلوں کے نام رہ سکتے ہیں۔ ایپ خود کچھ نہیں بھیجتی۔",
            "已删除敏感信息。发送前请检查压缩包：日志中仍可能包含文件名。应用不会自行发送任何内容。")
        Add("Архив больше {0} МБ. Отправка через почтовую программу недоступна; скопируйте ZIP через Проводник.",
            "The archive exceeds {0} MiB. Mail handoff is unavailable; copy the ZIP through File Explorer.", "Архів перевищує {0} МБ. Передавання поштовій програмі недоступне; скопіюйте ZIP через Провідник.",
            "Das Archiv überschreitet {0} MiB. Die Mailübergabe ist nicht verfügbar; kopieren Sie die ZIP im Datei-Explorer.", "L'archivio supera {0} MiB. L'invio alla posta non è disponibile; copia lo ZIP con Esplora file.",
            "El archivo supera {0} MiB. No se puede abrir con el correo; copie el ZIP con el Explorador.", "L'archive dépasse {0} Mio. Le transfert au courrier est indisponible ; copiez le ZIP via l'Explorateur.",
            "O arquivo excede {0} MiB. A entrega ao correio está indisponível; copie o ZIP pelo Explorador.", "يتجاوز الأرشيف {0} ميبيبايت. تسليمه للبريد غير متاح؛ انسخ ZIP عبر مستكشف الملفات.",
            "संग्रह {0} MiB से अधिक है। मेल को सौंपना उपलब्ध नहीं है; फ़ाइल एक्सप्लोरर से ZIP कॉपी करें।", "আর্কাইভ {0} MiB-এর বেশি। মেইলে হস্তান্তর উপলব্ধ নয়; ফাইল এক্সপ্লোরার দিয়ে ZIP কপি করুন।", "آرکائیو {0} MiB سے زیادہ ہے۔ میل کے حوالے کرنا دستیاب نہیں؛ فائل ایکسپلورر سے ZIP کاپی کریں۔", "压缩包超过 {0} MiB。无法交给邮件程序；请通过文件资源管理器复制 ZIP。")
        Add("Не удалось открыть Проводник. Скопируйте путь к архиву из окна проверки и откройте папку вручную.",
            "File Explorer could not be opened. Copy the archive path from the review window and open the folder manually.", "Не вдалося відкрити Провідник. Скопіюйте шлях до архіву з вікна перевірки й відкрийте теку вручну.",
            "Der Datei-Explorer konnte nicht geöffnet werden. Kopieren Sie den Archivpfad aus dem Prüffenster und öffnen Sie den Ordner manuell.", "Impossibile aprire Esplora file. Copia il percorso dalla finestra di verifica e apri la cartella manualmente.",
            "No se pudo abrir el Explorador. Copie la ruta desde la ventana de revisión y abra la carpeta manualmente.", "Impossible d'ouvrir l'Explorateur. Copiez le chemin depuis la fenêtre de vérification et ouvrez le dossier manuellement.",
            "Não foi possível abrir o Explorador. Copie o caminho da janela de revisão e abra a pasta manualmente.", "تعذّر فتح مستكشف الملفات. انسخ مسار الأرشيف من نافذة المراجعة وافتح المجلد يدويًا.",
            "फ़ाइल एक्सप्लोरर नहीं खुल सका। समीक्षा विंडो से संग्रह का पथ कॉपी करें और फ़ोल्डर स्वयं खोलें।", "ফাইল এক্সপ্লোরার খোলা যায়নি। পর্যালোচনার উইন্ডো থেকে পথ কপি করে ফোল্ডারটি নিজে খুলুন।", "فائل ایکسپلورر نہیں کھل سکا۔ جائزہ ونڈو سے آرکائیو کا راستہ کاپی کریں اور فولڈر خود کھولیں۔", "无法打开文件资源管理器。请从检查窗口复制压缩包路径并手动打开文件夹。")
        Add("Почтовая программа не найдена. Архив сохранён. Полный путь к архиву: {0}",
            "No mail program was found. The archive is preserved. Full archive path: {0}", "Поштової програми не знайдено. Архів збережено. Повний шлях до архіву: {0}",
            "Kein Mailprogramm gefunden. Das Archiv bleibt erhalten. Vollständiger Archivpfad: {0}", "Nessun programma di posta trovato. L'archivio è conservato. Percorso completo: {0}",
            "No se encontró un programa de correo. El archivo se conserva. Ruta completa: {0}", "Aucun programme de courrier trouvé. L'archive est conservée. Chemin complet : {0}",
            "Não foi encontrado um programa de correio. O arquivo foi preservado. Caminho completo: {0}", "لم يُعثر على برنامج بريد. تم الاحتفاظ بالأرشيف. المسار الكامل: {0}",
            "कोई मेल प्रोग्राम नहीं मिला। संग्रह सुरक्षित है। पूरा पथ: {0}", "কোনো মেইল প্রোগ্রাম পাওয়া যায়নি। আর্কাইভ রাখা হয়েছে। সম্পূর্ণ পথ: {0}", "کوئی میل پروگرام نہیں ملا۔ آرکائیو محفوظ ہے۔ مکمل راستہ: {0}", "未找到邮件程序。压缩包已保留。完整路径：{0}")
        Add("Открыть письмо с вложением не удалось. Архив сохранён; добавьте ZIP в письмо вручную. Путь можно скопировать из окна проверки.",
            "Mail with an attachment could not be opened. The archive is preserved; attach the ZIP manually. You can copy its path from the review window.",
            "Не вдалося відкрити лист із вкладенням. Архів збережено; додайте ZIP вручну. Шлях можна скопіювати з вікна перевірки.",
            "Eine Mail mit Anhang konnte nicht geöffnet werden. Das Archiv bleibt erhalten; hängen Sie die ZIP manuell an. Den Pfad können Sie im Prüffenster kopieren.",
            "Impossibile aprire una mail con allegato. L'archivio è conservato; allega lo ZIP manualmente. Puoi copiare il percorso dalla finestra di verifica.",
            "No se pudo abrir un correo con adjunto. El archivo se conserva; adjunte el ZIP manualmente. Puede copiar la ruta desde la ventana de revisión.",
            "Impossible d'ouvrir un courrier avec pièce jointe. L'archive est conservée ; joignez le ZIP manuellement. Son chemin est copiable dans la fenêtre de vérification.",
            "Não foi possível abrir uma mensagem com anexo. O arquivo foi preservado; anexe o ZIP manualmente. Pode copiar o caminho da janela de revisão.",
            "تعذّر فتح رسالة بمرفق. تم الاحتفاظ بالأرشيف؛ أرفق ZIP يدويًا. يمكنك نسخ المسار من نافذة المراجعة.",
            "संलग्नक वाला मेल नहीं खुल सका। संग्रह सुरक्षित है; ZIP स्वयं संलग्न करें। समीक्षा विंडो से पथ कॉपी कर सकते हैं।",
            "সংযুক্তিসহ মেইল খোলা যায়নি। আর্কাইভ রাখা হয়েছে; ZIP নিজে সংযুক্ত করুন। পর্যালোচনার উইন্ডো থেকে পথ কপি করতে পারেন।",
            "منسلکہ کے ساتھ میل نہیں کھل سکا۔ آرکائیو محفوظ ہے؛ ZIP خود منسلک کریں۔ جائزہ ونڈو سے راستہ کاپی کر سکتے ہیں۔",
            "无法打开带附件的邮件。压缩包已保留；请手动添加 ZIP 附件。可以从检查窗口复制路径。")
        Add("Собрать локальные серверные журналы для анализа, отправить автору или скопировать ZIP через Проводник.",
            "Collect local server logs for analysis, send them to the author or copy the ZIP through File Explorer.", "Зібрати локальні серверні журнали для аналізу, надіслати автору або скопіювати ZIP через Провідник.",
            "Lokale Serverprotokolle zur Analyse sammeln, an den Autor senden oder die ZIP im Datei-Explorer kopieren.", "Raccogli i registri dei server locali per l'analisi, inviali all'autore o copia lo ZIP con Esplora file.",
            "Recopile registros de servidores locales para analizarlos, envíelos al autor o copie el ZIP con el Explorador.", "Collectez les journaux des serveurs locaux pour analyse, envoyez-les à l'auteur ou copiez le ZIP via l'Explorateur.",
            "Recolha registos dos servidores locais para análise, envie-os ao autor ou copie o ZIP pelo Explorador.", "اجمع سجلات الخوادم المحلية للتحليل، وأرسلها للمؤلف أو انسخ ZIP عبر مستكشف الملفات.",
            "विश्लेषण के लिए स्थानीय सर्वर लॉग एकत्र करें, लेखक को भेजें या फ़ाइल एक्सप्लोरर से ZIP कॉपी करें।", "বিশ্লেষণের জন্য স্থানীয় সার্ভারের লগ সংগ্রহ করুন, লেখককে পাঠান বা ফাইল এক্সপ্লোরার দিয়ে ZIP কপি করুন।", "تجزیے کے لیے مقامی سرور کے لاگز جمع کریں، مصنف کو بھیجیں یا فائل ایکسپلورر سے ZIP کاپی کریں۔", "收集本地服务器日志以供分析，发送给作者或通过文件资源管理器复制 ZIP。")

        ' The consent-list line of a log that exists but could not be put into the archive:
        ' {0} the entry name, {1} one of the three reasons below.
        Add("{0} - не включён в архив: {1}",
            "{0} - not included in the archive: {1}", "{0} - не включено до архіву: {1}",
            "{0} - nicht im Archiv enthalten: {1}", "{0} - non incluso nell'archivio: {1}",
            "{0} - no incluido en el archivo: {1}", "{0} - non inclus dans l'archive : {1}",
            "{0} - não incluído no arquivo: {1}", "{0} - غير مضمَّن في الأرشيف: {1}",
            "{0} - संग्रह में शामिल नहीं: {1}", "{0} - আর্কাইভে অন্তর্ভুক্ত নয়: {1}",
            "{0} - آرکائیو میں شامل نہیں: {1}", "{0} - 未包含在压缩包中：{1}")
        Add("доступ к файлу запрещён",
            "access to the file was denied", "доступ до файлу заборонено", "der Zugriff auf die Datei wurde verweigert",
            "accesso al file negato", "se denegó el acceso al archivo", "accès au fichier refusé",
            "o acesso ao arquivo foi negado", "تم رفض الوصول إلى الملف", "फ़ाइल तक पहुँच अस्वीकृत की गई",
            "ফাইলে প্রবেশাধিকার প্রত্যাখ্যাত হয়েছে", "فائل تک رسائی مسترد کر دی گئی", "拒绝访问该文件")
        Add("файл занят другим процессом",
            "the file is in use by another process", "файл зайнятий іншим процесом", "die Datei wird von einem anderen Prozess verwendet",
            "il file è in uso da un altro processo", "otro proceso está usando el archivo", "un autre processus utilise le fichier",
            "outro processo está usando o arquivo", "الملف مستخدم من قبل عملية أخرى", "फ़ाइल किसी अन्य प्रक्रिया द्वारा उपयोग में है",
            "ফাইলটি অন্য প্রক্রিয়ায় ব্যবহৃত হচ্ছে", "فائل کسی دوسرے عمل کے استعمال میں ہے", "文件正被其他进程占用")
        Add("файл не удалось прочитать",
            "the file could not be read", "файл не вдалося прочитати", "die Datei konnte nicht gelesen werden",
            "non è stato possibile leggere il file", "no se pudo leer el archivo", "impossible de lire le fichier",
            "não foi possível ler o arquivo", "تعذّرت قراءة الملف", "फ़ाइल पढ़ी नहीं जा सकी",
            "ফাইলটি পড়া যায়নি", "فائل پڑھی نہیں جا سکی", "无法读取该文件")

        ' No mail program at all (a Windows Server has none): nothing is opened, the archive is on the
        ' clipboard and shown in its folder, and its FULL path is in the text - {0} - so it can be read
        ' out over a phone or typed on another machine.
        Add("Почтовая программа не найдена. Архив скопирован в буфер обмена и показан в папке. Полный путь к архиву: {0}",
            "No mail program was found. The archive is on the clipboard and shown in its folder. Full path of the archive: {0}",
            "Поштову програму не знайдено. Архів скопійовано в буфер обміну та показано в теці. Повний шлях до архіву: {0}",
            "Es wurde kein Mailprogramm gefunden. Das Archiv liegt in der Zwischenablage und wird im Ordner angezeigt. Vollständiger Pfad des Archivs: {0}",
            "Nessun programma di posta trovato. L'archivio è negli appunti ed è mostrato nella sua cartella. Percorso completo dell'archivio: {0}",
            "No se encontró ningún programa de correo. El archivo está en el portapapeles y se muestra en su carpeta. Ruta completa del archivo: {0}",
            "Aucun programme de messagerie n'a été trouvé. L'archive est dans le presse-papiers et affichée dans son dossier. Chemin complet de l'archive : {0}",
            "Nenhum programa de e-mail foi encontrado. O arquivo está na área de transferência e é mostrado na pasta. Caminho completo do arquivo: {0}",
            "لم يُعثر على برنامج بريد. تم نسخ الأرشيف إلى الحافظة وعرضه في مجلده. المسار الكامل للأرشيف: {0}",
            "कोई मेल प्रोग्राम नहीं मिला। संग्रह क्लिपबोर्ड में है और उसके फ़ोल्डर में दिखाया गया है। संग्रह का पूरा पथ: {0}",
            "কোনো মেল প্রোগ্রাম পাওয়া যায়নি। আর্কাইভ ক্লিপবোর্ডে নেওয়া হয়েছে ও ফোল্ডারে দেখানো হয়েছে। আর্কাইভের সম্পূর্ণ পথ: {0}",
            "کوئی میل پروگرام نہیں ملا۔ آرکائیو کلپ بورڈ میں ہے اور فولڈر میں دکھایا گیا ہے۔ آرکائیو کا مکمل راستہ: {0}",
            "未找到邮件程序。压缩包已复制到剪贴板并在文件夹中显示。压缩包的完整路径：{0}")

    End Sub

End Class
