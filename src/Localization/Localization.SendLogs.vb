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
