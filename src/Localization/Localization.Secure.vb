Option Strict On

' <summary>
' Strings of secure containers (026_SPECIFICATION_SECURE_CONTAINER_FD_SEC_DOTNET10.md §10):
' the password dialog, the three outcome messages, the locked placeholder, the refusals
' and the settings rows.
'
' Two of these carry more weight than the rest and must not be softened in any language:
'
'   - the three outcome classes are THREE and never merge. "Wrong password" for what might
'     be damage is a lie half the time, and so is the reverse. A translation that folds
'     them into one friendly sentence is a defect, not a style choice.
'   - the empty-credential statement must not reassure. An empty password produces a file
'     that is hidden from a casual look and from nothing else, and every surface that
'     accepts one says so.
'
' See Localization.vb for the key convention.
' </summary>
Partial Public NotInheritable Class Localization

    Private Shared Sub AddSecureStrings()

        ' --- the password dialog (§3.2) -------------------------------------------

        Add("Защищённый файл",
            "Protected file", "Захищений файл", "Geschützte Datei", "File protetto",
            "Archivo protegido", "Fichier protégé", "Arquivo protegido", "ملف محمي",
            "सुरक्षित फ़ाइल", "সুরক্ষিত ফাইল", "محفوظ فائل", "受保护的文件")

        Add("Введите пароль к файлу {0}",
            "Enter the password for {0}", "Введіть пароль до файлу {0}",
            "Geben Sie das Kennwort für {0} ein", "Inserisci la password per {0}",
            "Introduzca la contraseña de {0}", "Saisissez le mot de passe de {0}",
            "Digite a senha de {0}", "أدخل كلمة مرور الملف {0}",
            "{0} के लिए पासवर्ड दर्ज करें", "{0} এর পাসওয়ার্ড লিখুন",
            "{0} کا پاس ورڈ درج کریں", "请输入 {0} 的密码")

        Add("Показать пароль",
            "Show the password", "Показати пароль", "Kennwort anzeigen", "Mostra la password",
            "Mostrar la contraseña", "Afficher le mot de passe", "Mostrar a senha",
            "إظهار كلمة المرور", "पासवर्ड दिखाएँ", "পাসওয়ার্ড দেখান",
            "پاس ورڈ دکھائیں", "显示密码")

        Add("Помнить, пока программа открыта",
            "Remember while the program is running", "Пам'ятати, доки програма відкрита",
            "Merken, solange das Programm läuft", "Ricorda finché il programma è aperto",
            "Recordar mientras el programa esté abierto",
            "Mémoriser tant que le programme est ouvert",
            "Lembrar enquanto o programa estiver aberto",
            "التذكّر ما دام البرنامج مفتوحًا", "जब तक प्रोग्राम खुला है, याद रखें",
            "প্রোগ্রাম খোলা থাকা পর্যন্ত মনে রাখুন",
            "جب تک پروگرام کھلا ہے، یاد رکھیں", "在程序运行期间记住密码")

        ' The one sentence FDSEC-BEHAVIOUR §3.3 requires on every surface that accepts an
        ' empty credential. It must not sound like an option with a downside - the file is
        ' not protected at all.
        Add("Пустой пароль - это не защита: такой файл лишь скрыт от беглого взгляда. Нажмите ещё раз, чтобы открыть.",
            "An empty password is not protection: such a file is merely hidden from a casual look. Press again to open it.",
            "Порожній пароль - це не захист: такий файл лише прихований від побіжного погляду. Натисніть ще раз, щоб відкрити.",
            "Ein leeres Kennwort ist kein Schutz: eine solche Datei ist nur vor einem flüchtigen Blick verborgen. Drücken Sie erneut, um sie zu öffnen.",
            "Una password vuota non è una protezione: un file così è solo nascosto a uno sguardo distratto. Premi di nuovo per aprirlo.",
            "Una contraseña vacía no es protección: ese archivo solo queda oculto a una mirada superficial. Pulse de nuevo para abrirlo.",
            "Un mot de passe vide n'est pas une protection : un tel fichier est seulement caché à un regard distrait. Appuyez encore pour l'ouvrir.",
            "Uma senha vazia não é proteção: esse arquivo fica apenas escondido de um olhar casual. Pressione novamente para abri-lo.",
            "كلمة المرور الفارغة ليست حماية: هذا الملف مخفي عن النظرة العابرة فقط. اضغط مرة أخرى لفتحه.",
            "खाली पासवर्ड सुरक्षा नहीं है: ऐसी फ़ाइल केवल सरसरी नज़र से छिपी रहती है। खोलने के लिए फिर से दबाएँ।",
            "খালি পাসওয়ার্ড কোনো সুরক্ষা নয়: এমন ফাইল কেবল সাধারণ দৃষ্টি থেকে আড়াল থাকে। খুলতে আবার চাপুন।",
            "خالی پاس ورڈ تحفظ نہیں ہے: ایسی فائل صرف سرسری نظر سے چھپی رہتی ہے۔ کھولنے کے لیے دوبارہ دبائیں۔",
            "空密码不是保护：这样的文件只是躲过随意一瞥。再按一次即可打开。")

        ' --- the three outcome classes (§3.4) - three messages, never merged -------

        Add("Не открылось: пароль может быть неверным, файл может не быть контейнером, либо его изменили.",
            "It did not open: the password may be wrong, the file may never have been a container, or it may have been altered.",
            "Не відкрилося: пароль може бути невірним, файл може не бути контейнером, або його змінили.",
            "Es ließ sich nicht öffnen: das Kennwort kann falsch sein, die Datei war vielleicht nie ein Container, oder sie wurde verändert.",
            "Non si è aperto: la password può essere sbagliata, il file potrebbe non essere mai stato un contenitore, oppure è stato alterato.",
            "No se abrió: la contraseña puede ser incorrecta, el archivo puede no haber sido nunca un contenedor, o fue alterado.",
            "Il ne s'est pas ouvert : le mot de passe peut être faux, le fichier n'a peut-être jamais été un conteneur, ou il a été modifié.",
            "Não abriu: a senha pode estar errada, o arquivo pode nunca ter sido um contêiner, ou foi alterado.",
            "لم يُفتح: قد تكون كلمة المرور خاطئة، وقد لا يكون الملف حاوية أصلًا، وقد يكون جرى تعديله.",
            "नहीं खुला: पासवर्ड ग़लत हो सकता है, फ़ाइल कभी कंटेनर रही ही न हो, या उसे बदल दिया गया हो।",
            "খোলেনি: পাসওয়ার্ড ভুল হতে পারে, ফাইলটি কখনও কন্টেইনার না-ও হতে পারে, কিংবা এটি বদলে দেওয়া হয়েছে।",
            "نہیں کھلا: پاس ورڈ غلط ہو سکتا ہے، فائل کبھی کنٹینر رہی ہی نہ ہو، یا اسے بدل دیا گیا ہو۔",
            "未能打开：密码可能不对，这个文件也可能从来就不是容器，或者已被改动。")

        Add("Файл повреждён или неполон",
            "The file is damaged or incomplete", "Файл пошкоджено або він неповний",
            "Die Datei ist beschädigt oder unvollständig", "Il file è danneggiato o incompleto",
            "El archivo está dañado o incompleto", "Le fichier est endommagé ou incomplet",
            "O arquivo está danificado ou incompleto", "الملف تالف أو غير مكتمل",
            "फ़ाइल क्षतिग्रस्त या अधूरी है", "ফাইলটি ক্ষতিগ্রস্ত বা অসম্পূর্ণ",
            "فائل خراب یا نامکمل ہے", "文件已损坏或不完整")

        Add("Этот контейнер создан более новой версией формата",
            "This container was made by a newer version of the format",
            "Цей контейнер створено новішою версією формату",
            "Dieser Container wurde mit einer neueren Version des Formats erstellt",
            "Questo contenitore è stato creato da una versione più recente del formato",
            "Este contenedor se creó con una versión más nueva del formato",
            "Ce conteneur a été créé par une version plus récente du format",
            "Este contêiner foi criado por uma versão mais nova do formato",
            "أُنشئت هذه الحاوية بإصدار أحدث من الصيغة",
            "यह कंटेनर प्रारूप के किसी नए संस्करण ने बनाया है",
            "এই কন্টেইনারটি ফরম্যাটের নতুন কোনো সংস্করণ তৈরি করেছে",
            "یہ کنٹینر فارمیٹ کے کسی نئے ورژن نے بنایا ہے",
            "该容器由更新版本的格式创建")

        Add("Не удалось показать содержимое",
            "The content could not be shown", "Не вдалося показати вміст",
            "Der Inhalt konnte nicht angezeigt werden", "Impossibile mostrare il contenuto",
            "No se pudo mostrar el contenido", "Impossible d'afficher le contenu",
            "Não foi possível mostrar o conteúdo", "تعذّر عرض المحتوى",
            "सामग्री दिखाई नहीं जा सकी", "বিষয়বস্তু দেখানো যায়নি",
            "مواد دکھایا نہیں جا سکا", "无法显示内容")

        ' --- the placeholder, the status line, the actions (§3.3, §7) --------------

        Add("Нужен пароль - нажмите Enter или щёлкните по файлу",
            "A password is needed - press Enter or click the file",
            "Потрібен пароль - натисніть Enter або клацніть файл",
            "Ein Kennwort wird benötigt - drücken Sie die Eingabetaste oder klicken Sie die Datei an",
            "Serve una password - premi Invio o fai clic sul file",
            "Hace falta una contraseña - pulse Intro o haga clic en el archivo",
            "Un mot de passe est nécessaire - appuyez sur Entrée ou cliquez sur le fichier",
            "É preciso uma senha - pressione Enter ou clique no arquivo",
            "يلزم إدخال كلمة مرور - اضغط Enter أو انقر الملف",
            "पासवर्ड चाहिए - Enter दबाएँ या फ़ाइल पर क्लिक करें",
            "পাসওয়ার্ড প্রয়োজন - Enter চাপুন বা ফাইলে ক্লিক করুন",
            "پاس ورڈ درکار ہے - Enter دبائیں یا فائل پر کلک کریں",
            "需要密码 - 请按 Enter 或点击该文件")

        Add("Открыть защищённый файл.. (Enter)",
            "Open the protected file.. (Enter)", "Відкрити захищений файл.. (Enter)",
            "Geschützte Datei öffnen.. (Eingabetaste)", "Apri il file protetto.. (Invio)",
            "Abrir el archivo protegido.. (Intro)", "Ouvrir le fichier protégé.. (Entrée)",
            "Abrir o arquivo protegido.. (Enter)", "فتح الملف المحمي.. (Enter)",
            "सुरक्षित फ़ाइल खोलें.. (Enter)", "সুরক্ষিত ফাইল খুলুন.. (Enter)",
            "محفوظ فائل کھولیں.. (Enter)", "打开受保护的文件..（Enter）")

        Add("Открыт защищённый файл: {0}",
            "Opened a protected file: {0}", "Відкрито захищений файл: {0}",
            "Geschützte Datei geöffnet: {0}", "File protetto aperto: {0}",
            "Archivo protegido abierto: {0}", "Fichier protégé ouvert : {0}",
            "Arquivo protegido aberto: {0}", "تم فتح ملف محمي: {0}",
            "सुरक्षित फ़ाइल खुली: {0}", "সুরক্ষিত ফাইল খোলা হয়েছে: {0}",
            "محفوظ فائل کھل گئی: {0}", "已打开受保护的文件：{0}")

        Add("Защищённый файл открыт",
            "A protected file is open", "Захищений файл відкрито",
            "Eine geschützte Datei ist geöffnet", "Un file protetto è aperto",
            "Hay un archivo protegido abierto", "Un fichier protégé est ouvert",
            "Um arquivo protegido está aberto", "ملف محمي مفتوح",
            "एक सुरक्षित फ़ाइल खुली है", "একটি সুরক্ষিত ফাইল খোলা আছে",
            "ایک محفوظ فائل کھلی ہے", "已打开一个受保护的文件")

        Add("Контейнер открыт",
            "The container is open", "Контейнер відкрито", "Der Container ist geöffnet",
            "Il contenitore è aperto", "El contenedor está abierto", "Le conteneur est ouvert",
            "O contêiner está aberto", "الحاوية مفتوحة", "कंटेनर खुला है",
            "কন্টেইনার খোলা আছে", "کنٹینر کھلا ہے", "容器已打开")

        Add("Пропущено защищённых файлов: {0}",
            "Protected files skipped: {0}", "Пропущено захищених файлів: {0}",
            "Übersprungene geschützte Dateien: {0}", "File protetti saltati: {0}",
            "Archivos protegidos omitidos: {0}", "Fichiers protégés ignorés : {0}",
            "Arquivos protegidos ignorados: {0}", "الملفات المحمية التي جرى تخطّيها: {0}",
            "छोड़ी गई सुरक्षित फ़ाइलें: {0}", "এড়িয়ে যাওয়া সুরক্ষিত ফাইল: {0}",
            "چھوڑی گئی محفوظ فائلیں: {0}", "已跳过的受保护文件：{0}")

        Add("Забыть пароль",
            "Forget the password", "Забути пароль", "Kennwort vergessen",
            "Dimentica la password", "Olvidar la contraseña", "Oublier le mot de passe",
            "Esquecer a senha", "نسيان كلمة المرور", "पासवर्ड भूल जाएँ",
            "পাসওয়ার্ড ভুলে যান", "پاس ورڈ بھول جائیں", "忘记密码")

        Add("Пароль забыт",
            "The password has been forgotten", "Пароль забуто", "Das Kennwort wurde vergessen",
            "La password è stata dimenticata", "Se ha olvidado la contraseña",
            "Le mot de passe a été oublié", "A senha foi esquecida", "تم نسيان كلمة المرور",
            "पासवर्ड भुला दिया गया", "পাসওয়ার্ড ভুলে যাওয়া হয়েছে",
            "پاس ورڈ بھلا دیا گیا", "密码已被忘记")

        Add("Проверить контейнер целиком",
            "Verify the whole container", "Перевірити контейнер цілком",
            "Den ganzen Container prüfen", "Verifica l'intero contenitore",
            "Verificar todo el contenedor", "Vérifier tout le conteneur",
            "Verificar o contêiner inteiro", "التحقّق من الحاوية بأكملها",
            "पूरे कंटेनर की जाँच करें", "পুরো কন্টেইনার যাচাই করুন",
            "پورے کنٹینر کی جانچ کریں", "校验整个容器")

        Add("Проверка контейнера..",
            "Verifying the container..", "Перевірка контейнера..", "Container wird geprüft..",
            "Verifica del contenitore..", "Verificando el contenedor..",
            "Vérification du conteneur..", "Verificando o contêiner..",
            "جارٍ التحقّق من الحاوية..", "कंटेनर की जाँच हो रही है..",
            "কন্টেইনার যাচাই করা হচ্ছে..", "کنٹینر کی جانچ ہو رہی ہے..", "正在校验容器..")

        Add("Контейнер цел: все блоки прочитаны и подпись сошлась",
            "The container is whole: every block was read and the digest matched",
            "Контейнер цілий: усі блоки прочитано й підпис збігся",
            "Der Container ist unversehrt: jeder Block wurde gelesen und die Prüfsumme stimmt",
            "Il contenitore è integro: ogni blocco è stato letto e l'impronta corrisponde",
            "El contenedor está íntegro: se leyó cada bloque y la huella coincide",
            "Le conteneur est intact : chaque bloc a été lu et l'empreinte correspond",
            "O contêiner está íntegro: cada bloco foi lido e a impressão confere",
            "الحاوية سليمة: قُرئت كل الكتل وتطابقت البصمة",
            "कंटेनर सही है: हर खंड पढ़ा गया और डाइजेस्ट मिला",
            "কন্টেইনার অক্ষত: প্রতিটি ব্লক পড়া হয়েছে এবং ডাইজেস্ট মিলেছে",
            "کنٹینر درست ہے: ہر بلاک پڑھا گیا اور ڈائجسٹ مل گیا",
            "容器完好：每个数据块都已读取，摘要一致")

        Add("Для защищённого файла это недоступно: расшифрованные данные не попадают на диск",
            "Not available for a protected file: decrypted data never reaches the disk",
            "Для захищеного файла це недоступно: розшифровані дані не потрапляють на диск",
            "Für eine geschützte Datei nicht verfügbar: entschlüsselte Daten gelangen nie auf die Festplatte",
            "Non disponibile per un file protetto: i dati decifrati non arrivano mai sul disco",
            "No disponible para un archivo protegido: los datos descifrados nunca llegan al disco",
            "Indisponible pour un fichier protégé : les données déchiffrées n'atteignent jamais le disque",
            "Indisponível para um arquivo protegido: dados descriptografados nunca chegam ao disco",
            "غير متاح لملف محمي: البيانات المفكوكة لا تصل إلى القرص أبدًا",
            "सुरक्षित फ़ाइल के लिए उपलब्ध नहीं: डिक्रिप्ट किया गया डेटा कभी डिस्क पर नहीं जाता",
            "সুরক্ষিত ফাইলের জন্য নয়: ডিক্রিপ্ট করা তথ্য কখনও ডিস্কে যায় না",
            "محفوظ فائل کے لیے دستیاب نہیں: ڈکرپٹ شدہ ڈیٹا کبھی ڈسک پر نہیں جاتا",
            "受保护的文件不支持此操作：解密后的数据绝不写入磁盘")

        Add("Защищённый файл нельзя открыть во внешнем плеере",
            "A protected file cannot be opened in an external player",
            "Захищений файл не можна відкрити в зовнішньому плеєрі",
            "Eine geschützte Datei kann nicht in einem externen Player geöffnet werden",
            "Un file protetto non può essere aperto in un lettore esterno",
            "Un archivo protegido no puede abrirse en un reproductor externo",
            "Un fichier protégé ne peut pas être ouvert dans un lecteur externe",
            "Um arquivo protegido não pode ser aberto em um player externo",
            "لا يمكن فتح ملف محمي في مشغّل خارجي",
            "सुरक्षित फ़ाइल किसी बाहरी प्लेयर में नहीं खोली जा सकती",
            "সুরক্ষিত ফাইল বাইরের কোনো প্লেয়ারে খোলা যায় না",
            "محفوظ فائل کسی بیرونی پلیئر میں نہیں کھولی جا سکتی",
            "受保护的文件无法用外部播放器打开")

        Add("Не удалось изменить запись в реестре",
            "The registry entry could not be changed", "Не вдалося змінити запис у реєстрі",
            "Der Registrierungseintrag konnte nicht geändert werden",
            "Impossibile modificare la voce del registro",
            "No se pudo cambiar la entrada del registro",
            "Impossible de modifier l'entrée du registre",
            "Não foi possível alterar a entrada do registro",
            "تعذّر تغيير مُدخل السجل", "रजिस्ट्री प्रविष्टि बदली नहीं जा सकी",
            "রেজিস্ট্রি এন্ট্রি বদলানো যায়নি", "رجسٹری اندراج تبدیل نہیں ہو سکا",
            "无法修改注册表项")

        ' --- settings rows (§9) ---------------------------------------------------

        Add("Защищённые файлы",
            "Protected files", "Захищені файли", "Geschützte Dateien", "File protetti",
            "Archivos protegidos", "Fichiers protégés", "Arquivos protegidos",
            "الملفات المحمية", "सुरक्षित फ़ाइलें", "সুরক্ষিত ফাইল", "محفوظ فائلیں", "受保护的文件")

        Add("Показывать защищённые файлы в папке",
            "List protected files in the folder", "Показувати захищені файли в папці",
            "Geschützte Dateien im Ordner anzeigen", "Elenca i file protetti nella cartella",
            "Mostrar los archivos protegidos en la carpeta",
            "Afficher les fichiers protégés dans le dossier",
            "Listar arquivos protegidos na pasta", "إظهار الملفات المحمية في المجلد",
            "फ़ोल्डर में सुरक्षित फ़ाइलें दिखाएँ", "ফোল্ডারে সুরক্ষিত ফাইল দেখান",
            "فولڈر میں محفوظ فائلیں دکھائیں", "在文件夹中列出受保护的文件")

        Add("Файлы .fd-sec попадают в список папки и считаются в счётчике. Что внутри - неизвестно, пока файл не открыт паролем.",
            ".fd-sec files appear in the folder listing and are counted by the counter. What is inside is unknown until the file is opened with its password.",
            "Файли .fd-sec потрапляють до списку папки й рахуються лічильником. Що всередині - невідомо, доки файл не відкрито паролем.",
            ".fd-sec-Dateien erscheinen in der Ordnerliste und werden mitgezählt. Was darin ist, bleibt unbekannt, bis die Datei mit ihrem Kennwort geöffnet wird.",
            "I file .fd-sec compaiono nell'elenco della cartella e vengono contati. Cosa contengano resta ignoto finché il file non viene aperto con la sua password.",
            "Los archivos .fd-sec aparecen en la lista de la carpeta y se cuentan. Lo que hay dentro se desconoce hasta que el archivo se abre con su contraseña.",
            "Les fichiers .fd-sec apparaissent dans la liste du dossier et sont comptés. Ce qu'ils contiennent reste inconnu tant que le fichier n'est pas ouvert avec son mot de passe.",
            "Os arquivos .fd-sec aparecem na listagem da pasta e são contados. O que há dentro é desconhecido até o arquivo ser aberto com sua senha.",
            "تظهر ملفات ‎.fd-sec في قائمة المجلد وتُحتسب في العدّاد. وما بداخلها مجهول حتى يُفتح الملف بكلمة مروره.",
            ".fd-sec फ़ाइलें फ़ोल्डर सूची में दिखती हैं और गिनी जाती हैं। भीतर क्या है, यह तब तक अज्ञात है जब तक फ़ाइल अपने पासवर्ड से न खुले।",
            ".fd-sec ফাইল ফোল্ডারের তালিকায় আসে এবং গণনায় ধরা হয়। ভিতরে কী আছে তা ফাইলটি পাসওয়ার্ড দিয়ে না খোলা পর্যন্ত অজানা।",
            ".fd-sec فائلیں فولڈر کی فہرست میں آتی ہیں اور شمار ہوتی ہیں۔ اندر کیا ہے، یہ فائل کے پاس ورڈ سے کھلنے تک نامعلوم رہتا ہے۔",
            ".fd-sec 文件会出现在文件夹列表中并计入计数。在用密码打开之前，里面是什么无从得知。")

        Add("Помнить пароль, минут",
            "Remember the password, minutes", "Пам'ятати пароль, хвилин",
            "Kennwort merken, Minuten", "Ricorda la password, minuti",
            "Recordar la contraseña, minutos", "Mémoriser le mot de passe, minutes",
            "Lembrar a senha, minutos", "تذكّر كلمة المرور، بالدقائق",
            "पासवर्ड याद रखें, मिनट", "পাসওয়ার্ড মনে রাখুন, মিনিট",
            "پاس ورڈ یاد رکھیں، منٹ", "记住密码的分钟数")

        Add("Пароль хранится только в памяти программы и нигде не записывается. 0 - помнить до закрытия программы.",
            "The password is held in the program's memory only and is written nowhere. 0 means remember it until the program closes.",
            "Пароль зберігається лише в пам'яті програми й ніде не записується. 0 - пам'ятати до закриття програми.",
            "Das Kennwort bleibt nur im Speicher des Programms und wird nirgends gespeichert. 0 bedeutet: merken, bis das Programm geschlossen wird.",
            "La password resta solo nella memoria del programma e non viene scritta da nessuna parte. 0 significa ricordarla finché il programma non si chiude.",
            "La contraseña solo permanece en la memoria del programa y no se escribe en ningún sitio. 0 significa recordarla hasta que el programa se cierre.",
            "Le mot de passe ne reste qu'en mémoire du programme et n'est écrit nulle part. 0 signifie le mémoriser jusqu'à la fermeture du programme.",
            "A senha fica apenas na memória do programa e não é gravada em lugar nenhum. 0 significa lembrá-la até o programa fechar.",
            "تبقى كلمة المرور في ذاكرة البرنامج فقط ولا تُكتب في أي مكان. القيمة 0 تعني تذكّرها حتى إغلاق البرنامج.",
            "पासवर्ड केवल प्रोग्राम की मेमोरी में रहता है और कहीं नहीं लिखा जाता। 0 का अर्थ है प्रोग्राम बंद होने तक याद रखना।",
            "পাসওয়ার্ড শুধু প্রোগ্রামের মেমরিতে থাকে, কোথাও লেখা হয় না। 0 মানে প্রোগ্রাম বন্ধ হওয়া পর্যন্ত মনে রাখা।",
            "پاس ورڈ صرف پروگرام کی میموری میں رہتا ہے اور کہیں نہیں لکھا جاتا۔ 0 کا مطلب ہے پروگرام بند ہونے تک یاد رکھنا۔",
            "密码只保存在程序内存中，不会写到任何地方。0 表示记住到程序关闭为止。")

        Add("Показывать настоящее имя файла",
            "Show the real file name", "Показувати справжнє ім'я файла",
            "Den echten Dateinamen anzeigen", "Mostra il nome reale del file",
            "Mostrar el nombre real del archivo", "Afficher le vrai nom du fichier",
            "Mostrar o nome real do arquivo", "إظهار اسم الملف الحقيقي",
            "असली फ़ाइल नाम दिखाएँ", "আসল ফাইলের নাম দেখান",
            "اصل فائل کا نام دکھائیں", "显示真实文件名")

        Add("Открытый контейнер показывает имя файла, который в нём спрятан - в строке состояния и в подписи поверх картинки. Выключите, если на экран смотрит кто-то ещё.",
            "An open container shows the name of the file hidden inside it - in the status line and in the caption over the picture. Turn it off when somebody else is looking at the screen.",
            "Відкритий контейнер показує ім'я файла, схованого в ньому - у рядку стану та в підписі поверх зображення. Вимкніть, якщо на екран дивиться хтось іще.",
            "Ein geöffneter Container zeigt den Namen der darin verborgenen Datei - in der Statuszeile und in der Beschriftung über dem Bild. Schalten Sie es aus, wenn jemand anderes auf den Bildschirm schaut.",
            "Un contenitore aperto mostra il nome del file nascosto al suo interno - nella barra di stato e nella didascalia sopra l'immagine. Disattivalo se qualcun altro guarda lo schermo.",
            "Un contenedor abierto muestra el nombre del archivo oculto dentro - en la línea de estado y en el rótulo sobre la imagen. Desactívelo si alguien más mira la pantalla.",
            "Un conteneur ouvert affiche le nom du fichier caché à l'intérieur - dans la ligne d'état et dans la légende sur l'image. Désactivez-le si quelqu'un d'autre regarde l'écran.",
            "Um contêiner aberto mostra o nome do arquivo escondido dentro dele - na linha de status e na legenda sobre a imagem. Desligue se mais alguém estiver olhando a tela.",
            "تعرض الحاوية المفتوحة اسم الملف المخبّأ داخلها - في سطر الحالة وفي التسمية فوق الصورة. عطّله إذا كان هناك من ينظر إلى الشاشة.",
            "खुला कंटेनर उसमें छिपी फ़ाइल का नाम दिखाता है - स्थिति पंक्ति में और चित्र के ऊपर लिखे शीर्षक में। यदि स्क्रीन कोई और देख रहा हो तो इसे बंद कर दें।",
            "খোলা কন্টেইনার ভিতরে লুকানো ফাইলের নাম দেখায় - স্ট্যাটাস লাইনে এবং ছবির উপরের লেখায়। স্ক্রিনে অন্য কেউ তাকিয়ে থাকলে এটি বন্ধ করুন।",
            "کھلا کنٹینر اپنے اندر چھپی فائل کا نام دکھاتا ہے - اسٹیٹس لائن میں اور تصویر کے اوپر لکھے عنوان میں۔ اگر اسکرین کوئی اور دیکھ رہا ہو تو اسے بند کر دیں۔",
            "打开的容器会显示其中隐藏文件的名字 - 在状态栏和图片上方的说明里。若有他人正看着屏幕，请关掉它。")

        Add("Предлагать программу в «Открыть с помощью»",
            "Offer this program in Explorer's Open with",
            "Пропонувати програму в «Відкрити за допомогою»",
            "Dieses Programm unter «Öffnen mit» anbieten",
            "Proponi questo programma in «Apri con»",
            "Ofrecer este programa en «Abrir con»",
            "Proposer ce programme dans «Ouvrir avec»",
            "Oferecer este programa em «Abrir com»",
            "عرض هذا البرنامج في «فتح باستخدام»",
            "«इसके साथ खोलें» में यह प्रोग्राम दिखाएँ",
            "«এর সাহায্যে খুলুন» তালিকায় এই প্রোগ্রাম দেখান",
            "«اس کے ساتھ کھولیں» میں یہ پروگرام پیش کریں",
            "在「打开方式」中提供本程序")

        Add("Добавляет программу в меню «Открыть с помощью» для .fd-sec, ничего не отбирая у той программы, которой эти файлы принадлежат сейчас.",
            "Adds this program to the Open with menu for .fd-sec, taking nothing away from whatever owns those files today.",
            "Додає програму до меню «Відкрити за допомогою» для .fd-sec, нічого не відбираючи в тієї програми, якій ці файли належать зараз.",
            "Fügt dieses Programm dem Menü «Öffnen mit» für .fd-sec hinzu, ohne dem bisherigen Besitzer dieser Dateien etwas wegzunehmen.",
            "Aggiunge questo programma al menu «Apri con» per .fd-sec, senza togliere nulla a chi possiede oggi quei file.",
            "Añade este programa al menú «Abrir con» para .fd-sec, sin quitar nada al programa que hoy posee esos archivos.",
            "Ajoute ce programme au menu «Ouvrir avec» pour .fd-sec, sans rien retirer au programme auquel ces fichiers appartiennent aujourd'hui.",
            "Adiciona este programa ao menu «Abrir com» para .fd-sec, sem tirar nada de quem hoje é dono desses arquivos.",
            "يضيف هذا البرنامج إلى قائمة «فتح باستخدام» لملفات ‎.fd-sec دون أن ينتزع شيئًا من البرنامج الذي يملك هذه الملفات حاليًا.",
            "यह प्रोग्राम को .fd-sec के लिए «इसके साथ खोलें» मेन्यू में जोड़ता है, और जिस प्रोग्राम के पास अभी ये फ़ाइलें हैं उससे कुछ नहीं छीनता।",
            "এটি .fd-sec-এর জন্য «এর সাহায্যে খুলুন» মেনুতে এই প্রোগ্রাম যোগ করে, এখন যে প্রোগ্রামের অধীনে ফাইলগুলি আছে তার কিছু না কেড়ে।",
            "یہ .fd-sec کے لیے «اس کے ساتھ کھولیں» مینو میں یہ پروگرام شامل کرتا ہے، اور اس پروگرام سے کچھ نہیں چھینتا جس کے پاس یہ فائلیں اِس وقت ہیں۔",
            "把本程序加入 .fd-sec 的「打开方式」菜单，不会从当前拥有这些文件的程序手中拿走任何东西。")

        ' --- sealed text and a translator off this machine (audit 052 G-13) ------------

        Add("Текст защищённого файла будет отправлен переводчику на {0}. Продолжить?",
            "The text of this protected file will be sent to the translator at {0}. Continue?",
            "Текст захищеного файлу буде надіслано перекладачеві на {0}. Продовжити?",
            "Der Text dieser geschützten Datei wird an den Übersetzer auf {0} gesendet. Fortfahren?",
            "Il testo di questo file protetto verrà inviato al traduttore su {0}. Continuare?",
            "El texto de este archivo protegido se enviará al traductor en {0}. ¿Continuar?",
            "Le texte de ce fichier protégé sera envoyé au traducteur sur {0}. Continuer ?",
            "O texto deste arquivo protegido será enviado ao tradutor em {0}. Continuar?",
            "سيُرسل نص هذا الملف المحمي إلى المترجم على {0}. هل تريد المتابعة؟",
            "इस सुरक्षित फ़ाइल का पाठ {0} पर अनुवादक को भेजा जाएगा। जारी रखें?",
            "এই সুরক্ষিত ফাইলের লেখা {0}-এ অনুবাদকের কাছে পাঠানো হবে। চালিয়ে যাবেন?",
            "اس محفوظ فائل کا متن {0} پر مترجم کو بھیجا جائے گا۔ جاری رکھیں؟",
            "此受保护文件的文字将发送到 {0} 上的翻译服务。继续吗？")

        Add("Перевод отменён: текст защищённого файла не отправлен",
            "Translation cancelled: the text of the protected file was not sent",
            "Переклад скасовано: текст захищеного файлу не надіслано",
            "Übersetzung abgebrochen: Der Text der geschützten Datei wurde nicht gesendet",
            "Traduzione annullata: il testo del file protetto non è stato inviato",
            "Traducción cancelada: el texto del archivo protegido no se envió",
            "Traduction annulée : le texte du fichier protégé n'a pas été envoyé",
            "Tradução cancelada: o texto do arquivo protegido não foi enviado",
            "أُلغيت الترجمة: لم يُرسل نص الملف المحمي",
            "अनुवाद रद्द: सुरक्षित फ़ाइल का पाठ नहीं भेजा गया",
            "অনুবাদ বাতিল: সুরক্ষিত ফাইলের লেখা পাঠানো হয়নি",
            "ترجمہ منسوخ: محفوظ فائل کا متن نہیں بھیجا گیا",
            "已取消翻译：受保护文件的文字没有发送")

    End Sub

End Class
