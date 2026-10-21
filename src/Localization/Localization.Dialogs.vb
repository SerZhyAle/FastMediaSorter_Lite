Option Strict On

' <summary>
' The strings of the dialog layer (ticket 124): the named buttons and details of the questions that used to be
' system boxes and InputBox, the notification texts, the empty state and the first-run card's opening line.
'
' The table is shared with the net48 build like the others (a string table costs the x86 exe nothing, and
' splitting it per build is how two tables drift apart); only the .NET 10 viewer reads these. Never a smart
' double quote in a literal (CLAUDE.md): use the guillemets. House style: ".." not "...", a plain hyphen.
' </summary>
Partial Public NotInheritable Class Localization

    Private Shared Sub AddDialogStrings()

        ' --- a name collision, its two files and its three answers -------------------------------
        Add("В папке назначения уже есть файл {0}.",
            "The destination folder already has a file named {0}.", "У теці призначення вже є файл {0}.", "Im Zielordner gibt es bereits eine Datei {0}.",
            "Nella cartella di destinazione esiste già un file {0}.", "La carpeta de destino ya tiene un archivo {0}.", "Le dossier de destination contient déjà un fichier {0}.",
            "A pasta de destino já tem um ficheiro {0}.", "يوجد في المجلد الوجهة ملف باسم {0} بالفعل.", "गंतव्य फ़ोल्डर में {0} नाम की फ़ाइल पहले से है।",
            "গন্তব্য ফোল্ডারে {0} নামে একটি ফাইল আগে থেকেই আছে।", "منزل فولڈر میں {0} نام کی فائل پہلے سے موجود ہے۔", "目标文件夹中已有名为 {0} 的文件。")
        Add("{0}, изменён {1}",
            "{0}, modified {1}", "{0}, змінено {1}", "{0}, geändert {1}",
            "{0}, modificato {1}", "{0}, modificado {1}", "{0}, modifié {1}",
            "{0}, modificado {1}", "{0}، تاريخ التعديل {1}", "{0}, परिवर्तित {1}",
            "{0}, পরিবর্তিত {1}", "{0}، ترمیم {1}", "{0}，修改于 {1}")
        Add("Уже в папке: {0}",
            "Already in the folder: {0}", "Уже в теці: {0}", "Bereits im Ordner: {0}",
            "Già nella cartella: {0}", "Ya en la carpeta: {0}", "Déjà dans le dossier : {0}",
            "Já na pasta: {0}", "الموجود في المجلد: {0}", "फ़ोल्डर में पहले से: {0}",
            "ফোল্ডারে আগে থেকেই: {0}", "فولڈر میں پہلے سے: {0}", "文件夹中已有：{0}")
        Add("Этот файл: {0}",
            "This file: {0}", "Цей файл: {0}", "Diese Datei: {0}",
            "Questo file: {0}", "Este archivo: {0}", "Ce fichier : {0}",
            "Este ficheiro: {0}", "هذا الملف: {0}", "यह फ़ाइल: {0}",
            "এই ফাইল: {0}", "یہ فائل: {0}", "此文件：{0}")
        Add("Оставить оба",
            "Keep both", "Залишити обидва", "Beide behalten",
            "Mantieni entrambi", "Conservar ambos", "Garder les deux",
            "Manter ambos", "الإبقاء على الاثنين", "दोनों रखें",
            "দুটোই রাখুন", "دونوں رکھیں", "两者都保留")

        ' --- the settings import, the shortcut conflict and reset ----------------------------------
        Add("Объединить",
            "Merge", "Об'єднати", "Zusammenführen",
            "Unisci", "Combinar", "Fusionner",
            "Mesclar", "دمج", "मर्ज करें",
            "একত্র করুন", "ضم کریں", "合并")
        Add("Заменить - взять все настройки из файла. Объединить - взять только то, что отличается от значений по умолчанию.",
            "Replace - take every setting from the file. Merge - take only what differs from the defaults.", "Замінити - взяти всі налаштування з файлу. Об'єднати - взяти лише те, що відрізняється від типових значень.", "Ersetzen - alle Einstellungen aus der Datei übernehmen. Zusammenführen - nur übernehmen, was von den Standardwerten abweicht.",
            "Sostituisci - prende tutte le impostazioni dal file. Unisci - prende solo ciò che differisce dai valori predefiniti.", "Reemplazar - toma todos los ajustes del archivo. Combinar - toma solo lo que difiere de los valores predeterminados.", "Remplacer - reprend tous les réglages du fichier. Fusionner - ne reprend que ce qui diffère des valeurs par défaut.",
            "Substituir - usa todas as definições do ficheiro. Mesclar - usa apenas o que difere dos valores predefinidos.", "استبدال - أخذ كل الإعدادات من الملف. دمج - أخذ ما يختلف عن القيم الافتراضية فقط.", "बदलें - फ़ाइल की सभी सेटिंग्स लें। मर्ज करें - केवल वही लें जो डिफ़ॉल्ट से अलग है।",
            "প্রতিস্থাপন - ফাইলের সব সেটিংস নিন। একত্র করুন - শুধু যা ডিফল্ট থেকে আলাদা তা নিন।", "تبدیل کریں - فائل کی تمام سیٹنگز لیں۔ ضم کریں - صرف وہ لیں جو طے شدہ اقدار سے مختلف ہے۔", "替换 - 采用文件中的全部设置。合并 - 只采用与默认值不同的设置。")
        Add("Обменять сочетания",
            "Swap shortcuts", "Обміняти сполучення", "Tastenkürzel tauschen",
            "Scambia le scorciatoie", "Intercambiar atajos", "Échanger les raccourcis",
            "Trocar atalhos", "تبديل الاختصارات", "शॉर्टकट बदलें",
            "শর্টকাট অদলবদল করুন", "شارٹ کٹ آپس میں بدلیں", "交换快捷键")
        Add("Снять у того действия",
            "Remove it from that action", "Зняти в тієї дії", "Dort entfernen",
            "Rimuovi da quell'azione", "Quitarlo de esa acción", "Le retirer de cette action",
            "Remover dessa ação", "إزالته من ذلك الإجراء", "उस कार्य से हटाएँ",
            "ওই কাজ থেকে সরান", "اس عمل سے ہٹا دیں", "从那个操作中移除")
        Add("Вернуть к умолчаниям",
            "Reset to defaults", "Повернути до типових", "Auf Standard zurücksetzen",
            "Ripristina predefiniti", "Restablecer valores", "Rétablir par défaut",
            "Repor predefinições", "إعادة إلى الافتراضي", "डिफ़ॉल्ट पर लौटाएँ",
            "ডিফল্টে ফেরান", "طے شدہ پر لوٹائیں", "恢复默认")

        ' --- the prompts: rename, go to a number, open a URL ------------------------------------------
        Add("Введите имя файла",
            "Enter a file name", "Введіть ім'я файлу", "Geben Sie einen Dateinamen ein",
            "Inserisci un nome di file", "Escriba un nombre de archivo", "Saisissez un nom de fichier",
            "Introduza um nome de ficheiro", "أدخل اسم الملف", "फ़ाइल का नाम दर्ज करें",
            "ফাইলের নাম লিখুন", "فائل کا نام درج کریں", "请输入文件名")
        Add("Расширение {0} сохранится.",
            "The extension {0} is kept.", "Розширення {0} збережеться.", "Die Endung {0} bleibt erhalten.",
            "L'estensione {0} resta invariata.", "Se conserva la extensión {0}.", "L'extension {0} est conservée.",
            "A extensão {0} é mantida.", "سيبقى الامتداد {0} كما هو.", "एक्सटेंशन {0} बना रहेगा।",
            "এক্সটেনশন {0} অপরিবর্তিত থাকবে।", "ایکسٹینشن {0} برقرار رہے گی۔", "扩展名 {0} 将保留。")
        Add("Перейти",
            "Go", "Перейти", "Gehe zu",
            "Vai", "Ir", "Aller",
            "Ir", "انتقال", "जाएँ",
            "যান", "جائیں", "转到")
        Add("От 1 до {0}",
            "From 1 to {0}", "Від 1 до {0}", "Von 1 bis {0}",
            "Da 1 a {0}", "De 1 a {0}", "De 1 à {0}",
            "De 1 a {0}", "من 1 إلى {0}", "1 से {0} तक",
            "১ থেকে {0}", "1 سے {0} تک", "从 1 到 {0}")
        Add("Введите адрес",
            "Enter an address", "Введіть адресу", "Geben Sie eine Adresse ein",
            "Inserisci un indirizzo", "Escriba una dirección", "Saisissez une adresse",
            "Introduza um endereço", "أدخل عنواناً", "पता दर्ज करें",
            "ঠিকানা লিখুন", "پتہ درج کریں", "请输入地址")
        Add("Адрес видеопотока или файла на сервере:",
            "Address of a video stream or a file on a server:", "Адреса відеопотоку або файлу на сервері:", "Adresse eines Videostreams oder einer Datei auf einem Server:",
            "Indirizzo di un flusso video o di un file su un server:", "Dirección de una transmisión de vídeo o de un archivo en un servidor:", "Adresse d'un flux vidéo ou d'un fichier sur un serveur :",
            "Endereço de uma transmissão de vídeo ou de um ficheiro num servidor:", "عنوان بث فيديو أو ملف على خادم:", "सर्वर पर वीडियो स्ट्रीम या फ़ाइल का पता:",
            "সার্ভারে ভিডিও স্ট্রিম বা ফাইলের ঠিকানা:", "سرور پر ویڈیو سٹریم یا فائل کا پتہ:", "视频流或服务器上文件的地址：")
        Add("Обычные сетевые папки (\\сервер\шара\..) открываются кнопкой «Выбрать файл..» как есть.",
            "Ordinary network folders (\\server\share\..) open as they are with the «Choose file..» button.", "Звичайні мережеві теки (\\сервер\ресурс\..) відкриваються кнопкою «Вибрати файл..» як є.", "Normale Netzwerkordner (\\Server\Freigabe\..) öffnen Sie unverändert mit der Schaltfläche «Datei wählen..».",
            "Le normali cartelle di rete (\\server\condivisione\..) si aprono così come sono con il pulsante «Scegli file..».", "Las carpetas de red normales (\\servidor\compartido\..) se abren tal cual con el botón «Elegir archivo..».", "Les dossiers réseau ordinaires (\\serveur\partage\..) s'ouvrent tels quels avec le bouton «Choisir un fichier..».",
            "As pastas de rede normais (\\servidor\partilha\..) abrem-se tal como estão com o botão «Escolher ficheiro..».", "تُفتح مجلدات الشبكة العادية (\\الخادم\المشاركة\..) كما هي بزر «اختيار ملف..».", "सामान्य नेटवर्क फ़ोल्डर (\\सर्वर\शेयर\..) «फ़ाइल चुनें..» बटन से जैसे हैं वैसे ही खुलते हैं।",
            "সাধারণ নেটওয়ার্ক ফোল্ডার (\\সার্ভার\শেয়ার\..) «ফাইল বেছে নিন..» বোতামে যেমন আছে তেমনই খোলে।", "عام نیٹ ورک فولڈرز (\\سرور\شیئر\..) «فائل منتخب کریں..» بٹن سے جوں کے توں کھلتے ہیں۔", "普通网络文件夹 (\\服务器\共享\..) 可直接用「选择文件..」按钮打开。")
        ' --- notifications, the empty state, the details window --------------------------------------
        Add("{0}: {1}",
            "{0}: {1}", "{0}: {1}", "{0}: {1}",
            "{0}: {1}", "{0}: {1}", "{0} : {1}",
            "{0}: {1}", "{0}: {1}", "{0}: {1}",
            "{0}: {1}", "{0}: {1}", "{0}：{1}")
        Add("F4 - открыть файл из другой папки",
            "F4 - open a file from another folder", "F4 - відкрити файл з іншої теки", "F4 - eine Datei aus einem anderen Ordner öffnen",
            "F4 - apri un file da un'altra cartella", "F4 - abrir un archivo de otra carpeta", "F4 - ouvrir un fichier d'un autre dossier",
            "F4 - abrir um ficheiro de outra pasta", "F4 - فتح ملف من مجلد آخر", "F4 - किसी दूसरे फ़ोल्डर की फ़ाइल खोलें",
            "F4 - অন্য ফোল্ডারের ফাইল খুলুন", "F4 - کسی دوسرے فولڈر کی فائل کھولیں", "F4 - 打开其他文件夹中的文件")
        Add("Проверьте, что ресурс включён и присоединён",
            "Check that the resource is turned on and connected", "Перевірте, що ресурс увімкнено та приєднано", "Prüfen Sie, ob die Ressource eingeschaltet und verbunden ist",
            "Verificare che la risorsa sia accesa e collegata", "Compruebe que el recurso esté encendido y conectado", "Vérifiez que la ressource est allumée et connectée",
            "Verifique se o recurso está ligado e conectado", "تحقق من تشغيل المورد وتوصيله", "जाँचें कि संसाधन चालू और जुड़ा हुआ है",
            "রিসোর্সটি চালু এবং সংযুক্ত আছে কিনা তা পরীক্ষা করুন", "چیک کریں کہ وسیلہ آن اور منسلک ہے", "请检查资源是否已开启并连接")
        Add("Сначала откройте файл (F4) или папку с медиафайлами",
            "First open a file (F4) or a folder with media files", "Спершу відкрийте файл (F4) або теку з медіафайлами", "Öffnen Sie zuerst eine Datei (F4) oder einen Ordner mit Mediendateien",
            "Apri prima un file (F4) o una cartella con file multimediali", "Primero abra un archivo (F4) o una carpeta con archivos multimedia", "Ouvrez d'abord un fichier (F4) ou un dossier de fichiers multimédias",
            "Abra primeiro um ficheiro (F4) ou uma pasta com ficheiros multimédia", "افتح أولاً ملفاً (F4) أو مجلداً يحتوي على ملفات وسائط", "पहले कोई फ़ाइल (F4) या मीडिया फ़ाइलों वाला फ़ोल्डर खोलें",
            "প্রথমে একটি ফাইল (F4) বা মিডিয়া ফাইলসহ ফোল্ডার খুলুন", "پہلے کوئی فائل (F4) یا میڈیا فائلوں والا فولڈر کھولیں", "请先打开文件 (F4) 或包含媒体文件的文件夹")
        Add("Не удалось прочитать сведения о файле.",
            "The file's details could not be read.", "Не вдалося прочитати відомості про файл.", "Die Dateiinformationen konnten nicht gelesen werden.",
            "Impossibile leggere le informazioni sul file.", "No se pudieron leer los datos del archivo.", "Impossible de lire les informations du fichier.",
            "Não foi possível ler as informações do ficheiro.", "تعذّرت قراءة معلومات الملف.", "फ़ाइल की जानकारी पढ़ी नहीं जा सकी।",
            "ফাইলের তথ্য পড়া যায়নি।", "فائل کی معلومات پڑھی نہ جا سکیں۔", "无法读取文件信息。")
        ' --- faults and archive refusals: a cause and a next step (UX-208) ---------------------------
        Add("Внутренняя ошибка: {0}. Последнее действие могло выполниться не полностью.",
            "Internal error: {0}. The last action may not have completed fully.", "Внутрішня помилка: {0}. Остання дія могла виконатися не повністю.", "Interner Fehler: {0}. Die letzte Aktion wurde möglicherweise nicht vollständig ausgeführt.",
            "Errore interno: {0}. L'ultima azione potrebbe non essere stata completata del tutto.", "Error interno: {0}. Es posible que la última acción no se haya completado.", "Erreur interne : {0}. La dernière action n'a peut-être pas été menée à terme.",
            "Erro interno: {0}. A última ação pode não ter sido concluída por completo.", "خطأ داخلي: {0}. ربما لم يكتمل آخر إجراء بالكامل.", "आंतरिक त्रुटि: {0}। हो सकता है कि अंतिम क्रिया पूरी तरह न हुई हो।",
            "অভ্যন্তরীণ ত্রুটি: {0}। সম্ভবত শেষ কাজটি পুরোপুরি সম্পন্ন হয়নি।", "اندرونی خرابی: {0}۔ ہو سکتا ہے آخری عمل مکمل نہ ہوا ہو۔", "内部错误：{0}。上一个操作可能没有完整完成。")
        Add("Не удалось открыть архив: {0}",
            "The archive could not be opened: {0}", "Не вдалося відкрити архів: {0}", "Das Archiv konnte nicht geöffnet werden: {0}",
            "Impossibile aprire l'archivio: {0}", "No se pudo abrir el archivo comprimido: {0}", "Impossible d'ouvrir l'archive : {0}",
            "Não foi possível abrir o arquivo: {0}", "تعذّر فتح الأرشيف: {0}", "संग्रह नहीं खुल सका: {0}",
            "আর্কাইভ খোলা যায়নি: {0}", "آرکائیو کھولا نہ جا سکا: {0}", "无法打开压缩包：{0}")
        Add("Архив не распакован до конца: он повреждён или неполон. Попробуйте открыть его архиватором.",
            "The archive was not unpacked completely: it is damaged or incomplete. Try opening it with an archiver.", "Архів не розпаковано до кінця: він пошкоджений або неповний. Спробуйте відкрити його архіватором.", "Das Archiv wurde nicht vollständig entpackt: Es ist beschädigt oder unvollständig. Versuchen Sie, es mit einem Archivierungsprogramm zu öffnen.",
            "L'archivio non è stato estratto completamente: è danneggiato o incompleto. Prova ad aprirlo con un programma di archiviazione.", "El archivo no se extrajo por completo: está dañado o incompleto. Pruebe a abrirlo con un compresor.", "L'archive n'a pas été extraite entièrement : elle est endommagée ou incomplète. Essayez de l'ouvrir avec un logiciel d'archivage.",
            "O arquivo não foi extraído por completo: está danificado ou incompleto. Tente abri-lo com um compactador.", "لم يُفكّ الأرشيف بالكامل: فهو تالف أو ناقص. جرّب فتحه ببرنامج أرشفة.", "संग्रह पूरी तरह नहीं खुला: वह क्षतिग्रस्त या अधूरा है। उसे किसी आर्काइवर से खोलकर देखें।",
            "আর্কাইভ পুরোপুরি খোলা যায়নি: এটি ক্ষতিগ্রস্ত বা অসম্পূর্ণ। কোনো আর্কাইভার দিয়ে খুলে দেখুন।", "آرکائیو مکمل طور پر نہیں کھلا: وہ خراب یا ادھورا ہے۔ اسے کسی آرکائیور سے کھول کر دیکھیں۔", "压缩包未能完整解压：它已损坏或不完整。请尝试用压缩软件打开。")
        Add("Запись сжата слишком сильно, чтобы открыть её безопасно. Распакуйте архив архиватором и проверьте файл.",
            "The entry is compressed too heavily to open safely. Unpack the archive with an archiver and check the file.", "Запис стиснуто надто сильно, щоб відкрити її безпечно. Розпакуйте архів архіватором і перевірте файл.", "Der Eintrag ist zu stark komprimiert, um ihn sicher zu öffnen. Entpacken Sie das Archiv mit einem Archivierungsprogramm und prüfen Sie die Datei.",
            "La voce è compressa troppo per essere aperta in sicurezza. Estrai l'archivio con un programma di archiviazione e controlla il file.", "La entrada está comprimida en exceso para abrirla con seguridad. Extraiga el archivo con un compresor y revise el fichero.", "L'entrée est trop fortement compressée pour être ouverte en toute sécurité. Extrayez l'archive avec un logiciel d'archivage et vérifiez le fichier.",
            "A entrada está demasiado comprimida para ser aberta com segurança. Extraia o arquivo com um compactador e verifique o ficheiro.", "الإدخال مضغوط بشدة ولا يمكن فتحه بأمان. فُكّ الأرشيف ببرنامج أرشفة وافحص الملف.", "यह प्रविष्टि इतनी अधिक संपीड़ित है कि उसे सुरक्षित रूप से नहीं खोला जा सकता। आर्काइवर से संग्रह खोलें और फ़ाइल जाँचें।",
            "এন্ট্রিটি এত বেশি সংকুচিত যে নিরাপদে খোলা যায় না। আর্কাইভার দিয়ে আর্কাইভ খুলে ফাইলটি পরীক্ষা করুন।", "اندراج اتنا زیادہ کمپریس ہے کہ اسے محفوظ طریقے سے نہیں کھولا جا سکتا۔ آرکائیور سے آرکائیو کھولیں اور فائل جانچیں۔", "该条目压缩过度，无法安全打开。请用压缩软件解压后检查该文件。")
        Add("Архив защищён паролем, а программа такие не открывает. Распакуйте его архиватором и откройте папку.",
            "The archive is password-protected and this program cannot open those. Unpack it with an archiver and open the folder.", "Архів захищено паролем, а програма такі не відкриває. Розпакуйте його архіватором і відкрийте теку.", "Das Archiv ist passwortgeschützt, und dieses Programm kann solche nicht öffnen. Entpacken Sie es mit einem Archivierungsprogramm und öffnen Sie den Ordner.",
            "L'archivio è protetto da password e questo programma non può aprirli. Estrailo con un programma di archiviazione e apri la cartella.", "El archivo está protegido con contraseña y este programa no puede abrirlos. Extráigalo con un compresor y abra la carpeta.", "L'archive est protégée par un mot de passe et ce programme ne peut pas les ouvrir. Extrayez-la avec un logiciel d'archivage et ouvrez le dossier.",
            "O arquivo está protegido por palavra-passe e este programa não os consegue abrir. Extraia-o com um compactador e abra a pasta.", "الأرشيف محمي بكلمة مرور وهذا البرنامج لا يفتح مثله. فُكّه ببرنامج أرشفة ثم افتح المجلد.", "संग्रह पासवर्ड से सुरक्षित है और यह प्रोग्राम ऐसे संग्रह नहीं खोल सकता। उसे आर्काइवर से खोलें और फ़ोल्डर खोलें।",
            "আর্কাইভটি পাসওয়ার্ড দিয়ে সুরক্ষিত এবং এই প্রোগ্রাম তা খুলতে পারে না। আর্কাইভার দিয়ে খুলে ফোল্ডারটি খুলুন।", "آرکائیو پاس ورڈ سے محفوظ ہے اور یہ پروگرام ایسے آرکائیو نہیں کھول سکتا۔ اسے آرکائیور سے کھولیں اور فولڈر کھولیں۔", "压缩包有密码保护，本程序无法打开。请用压缩软件解压后打开该文件夹。")
        ' --- plain tooltips (Q124-1: the playful tone stays in the first-run card only) and the first-run card ----
        Add("Случайный файл (Y)",
            "Random file (Y)", "Випадковий файл (Y)", "Zufällige Datei (Y)",
            "File casuale (Y)", "Archivo aleatorio (Y)", "Fichier aléatoire (Y)",
            "Ficheiro aleatório (Y)", "ملف عشوائي (Y)", "यादृच्छिक फ़ाइल (Y)",
            "এলোমেলো ফাইল (Y)", "بے ترتیب فائل (Y)", "随机文件 (Y)")
        Add("OCR + перевод текущего изображения (T). ПКМ - настройки. Shift+T - авто-режим.",
            "OCR + translation of the current image (T). Right click - settings. Shift+T - auto mode.", "OCR + переклад поточного зображення (T). ПКМ - налаштування. Shift+T - авторежим.", "OCR + Übersetzung des aktuellen Bildes (T). Rechtsklick - Einstellungen. Umschalt+T - Automatikmodus.",
            "OCR + traduzione dell'immagine corrente (T). Clic destro - impostazioni. Maiusc+T - modalità automatica.", "OCR + traducción de la imagen actual (T). Clic derecho - ajustes. Mayús+T - modo automático.", "OCR + traduction de l'image actuelle (T). Clic droit - réglages. Maj+T - mode automatique.",
            "OCR + tradução da imagem atual (T). Botão direito - definições. Shift+T - modo automático.", "OCR + ترجمة الصورة الحالية (T). النقر بالزر الأيمن - الإعدادات. Shift+T - الوضع التلقائي.", "OCR + वर्तमान चित्र का अनुवाद (T)। राइट क्लिक - सेटिंग्स। Shift+T - स्वचालित मोड।",
            "OCR + বর্তমান ছবির অনুবাদ (T)। ডান ক্লিক - সেটিংস। Shift+T - স্বয়ংক্রিয় মোড।", "OCR + موجودہ تصویر کا ترجمہ (T)۔ دایاں کلک - سیٹنگز۔ Shift+T - خودکار موڈ۔", "OCR + 翻译当前图片 (T)。右键 - 设置。Shift+T - 自动模式。")
        Add("Включить перспективный фон: чёрные поля по краям превращаются в продолжение картинки.",
            "Enable the perspective background: the black bars become a continuation of the image.", "Увімкнути перспективний фон: чорні поля стають продовженням зображення.", "Perspektivischen Hintergrund einschalten: Die schwarzen Ränder werden zur Fortsetzung des Bildes.",
            "Attiva lo sfondo prospettico: le bande nere diventano la continuazione dell'immagine.", "Activar el fondo en perspectiva: las bandas negras se convierten en la continuación de la imagen.", "Activer l'arrière-plan en perspective : les bandes noires prolongent l'image.",
            "Ativar o fundo em perspetiva: as faixas pretas passam a ser a continuação da imagem.", "تفعيل الخلفية المنظورية: تتحول الحواف السوداء إلى امتداد للصورة.", "परिप्रेक्ष्य पृष्ठभूमि चालू करें: काली पट्टियाँ चित्र का विस्तार बन जाती हैं।",
            "পারস্পেক্টিভ ব্যাকগ্রাউন্ড চালু করুন: কালো পাশগুলো ছবির সম্প্রসারণ হয়ে যায়।", "پرسپیکٹو پس منظر آن کریں: کالی پٹیاں تصویر کا تسلسل بن جاتی ہیں۔", "启用透视背景：黑边会变成图片的延伸。")
        Add("Повторять видео по кругу.",
            "Repeat the video in a loop.", "Повторювати відео по колу.", "Das Video in einer Schleife wiederholen.",
            "Ripeti il video in ciclo.", "Repetir el vídeo en bucle.", "Répéter la vidéo en boucle.",
            "Repetir o vídeo em ciclo.", "تكرار الفيديو في حلقة.", "वीडियो को लूप में दोहराएँ।",
            "ভিডিও লুপে পুনরাবৃত্তি করুন।", "ویڈیو کو لوپ میں دہرائیں۔", "循环播放视频。")
        Add("Отключить подтверждения перед файловыми операциями: действия будут выполняться без вопросов.",
            "Turn off the confirmations before file operations: actions will run without asking.", "Вимкнути підтвердження перед файловими операціями: дії виконуватимуться без запитань.", "Bestätigungen vor Dateioperationen ausschalten: Aktionen werden ohne Nachfrage ausgeführt.",
            "Disattiva le conferme prima delle operazioni sui file: le azioni verranno eseguite senza chiedere.", "Desactivar las confirmaciones antes de las operaciones con archivos: las acciones se ejecutarán sin preguntar.", "Désactiver les confirmations avant les opérations sur les fichiers : les actions s'exécuteront sans demander.",
            "Desativar as confirmações antes das operações com ficheiros: as ações serão executadas sem perguntar.", "إيقاف التأكيدات قبل عمليات الملفات: ستُنفَّذ الإجراءات دون سؤال.", "फ़ाइल संचालन से पहले की पुष्टि बंद करें: क्रियाएँ बिना पूछे चलेंगी।",
            "ফাইল অপারেশনের আগে নিশ্চিতকরণ বন্ধ করুন: কাজগুলো জিজ্ঞাসা ছাড়াই চলবে।", "فائل آپریشنز سے پہلے کی تصدیق بند کریں: عمل بغیر پوچھے ہوں گے۔", "关闭文件操作前的确认：操作将不再询问直接执行。")
        Add("Начните с F4: выберите файл - откроется вся его папка.",
            "Start with F4: choose a file - its whole folder opens.", "Почніть з F4: виберіть файл - відкриється вся його тека.", "Beginnen Sie mit F4: Wählen Sie eine Datei - ihr ganzer Ordner wird geöffnet.",
            "Inizia con F4: scegli un file - si apre l'intera cartella.", "Empiece con F4: elija un archivo - se abrirá toda su carpeta.", "Commencez par F4 : choisissez un fichier - tout son dossier s'ouvre.",
            "Comece com F4: escolha um ficheiro - a pasta inteira abre-se.", "ابدأ بـ F4: اختر ملفاً - ويُفتح مجلده بالكامل.", "F4 से शुरू करें: कोई फ़ाइल चुनें - उसका पूरा फ़ोल्डर खुल जाएगा।",
            "F4 দিয়ে শুরু করুন: একটি ফাইল বেছে নিন - তার পুরো ফোল্ডার খুলে যাবে।", "F4 سے شروع کریں: کوئی فائل منتخب کریں - اس کا پورا فولڈر کھل جائے گا۔", "从 F4 开始：选择一个文件，其所在文件夹将整个打开。")
        Add("Щёлкните по этому тексту или нажмите F1 - он исчезнет.",
            "Click this text or press F1 - it goes away.", "Клацніть по цьому тексту або натисніть F1 - він зникне.", "Klicken Sie auf diesen Text oder drücken Sie F1 - er verschwindet.",
            "Fai clic su questo testo o premi F1 - scomparirà.", "Haga clic en este texto o pulse F1 - desaparecerá.", "Cliquez sur ce texte ou appuyez sur F1 - il disparaît.",
            "Clique neste texto ou prima F1 - desaparece.", "انقر على هذا النص أو اضغط F1 - سيختفي.", "इस पाठ पर क्लिक करें या F1 दबाएँ - यह हट जाएगा।",
            "এই লেখায় ক্লিক করুন বা F1 চাপুন - এটি সরে যাবে।", "اس متن پر کلک کریں یا F1 دبائیں - یہ غائب ہو جائے گا۔", "单击此文字或按 F1 - 它就会消失。")
        ' --- progress and hints on the status line (UX-211/UX-212/UX-213) -------------------------------
        Add("Подключение к «{0}».. Esc - отмена",
            "Connecting to «{0}».. Esc cancels", "Підключення до «{0}».. Esc - скасувати", "Verbindung zu «{0}».. Esc bricht ab",
            "Connessione a «{0}».. Esc annulla", "Conectando con «{0}».. Esc cancela", "Connexion à «{0}».. Échap annule",
            "A ligar a «{0}».. Esc cancela", "جارٍ الاتصال بـ «{0}».. Esc للإلغاء", "«{0}» से कनेक्ट हो रहा है.. रद्द करने के लिए Esc",
            "«{0}»-এ সংযোগ হচ্ছে.. বাতিল করতে Esc", "«{0}» سے جڑ رہا ہے.. منسوخ کرنے کے لیے Esc", "正在连接「{0}」.. 按 Esc 取消")
        Add("Открыт первый из {0} файлов - остальные не открыты.",
            "Opened the first of {0} files - the others were not opened.", "Відкрито перший із {0} файлів - решту не відкрито.", "Die erste von {0} Dateien wurde geöffnet - die übrigen nicht.",
            "Aperto il primo di {0} file - gli altri non sono stati aperti.", "Se abrió el primero de {0} archivos; los demás no.", "Le premier de {0} fichiers est ouvert - les autres ne le sont pas.",
            "Abriu-se o primeiro de {0} ficheiros - os outros não foram abertos.", "فُتح أول ملف من {0} - ولم تُفتح البقية.", "{0} फ़ाइलों में से पहली खुली - बाकी नहीं खुलीं।",
            "{0}টি ফাইলের প্রথমটি খোলা হয়েছে - বাকিগুলো খোলা হয়নি।", "{0} میں سے پہلی فائل کھولی گئی - باقی نہیں کھولی گئیں۔", "已打开 {0} 个文件中的第一个，其余未打开。")
        Add("Проверяю пароль..",
            "Checking the password..", "Перевіряю пароль..", "Passwort wird geprüft..",
            "Verifica della password..", "Comprobando la contraseña..", "Vérification du mot de passe..",
            "A verificar a palavra-passe..", "جارٍ التحقق من كلمة المرور..", "पासवर्ड जाँचा जा रहा है..",
            "পাসওয়ার্ড যাচাই করা হচ্ছে..", "پاس ورڈ جانچا جا رہا ہے..", "正在验证密码..")
        ' --- values with their words, in one sentence each (UX-217): word order and direction differ per language,
        ' so the old "word: " & value concatenations could not be translated correctly ------------------
        Add("Загрузка файла: {0}",
            "Loading: {0}", "Завантаження: {0}", "Wird geladen: {0}", "Caricamento: {0}",
            "Cargando: {0}", "Chargement : {0}", "Carregando: {0}", "جارٍ التحميل: {0}", "लोड हो रहा है: {0}",
            "লোড হচ্ছে: {0}", "لوڈ ہو رہا ہے: {0}", "正在加载：{0}")
        Add("Загрузка.. {0}",
            "Loading.. {0}", "Завантаження.. {0}", "Wird geladen.. {0}", "Caricamento.. {0}",
            "Cargando.. {0}", "Chargement.. {0}", "Carregando.. {0}", "جارٍ التحميل.. {0}", "लोड हो रहा है.. {0}",
            "লোড হচ্ছে.. {0}", "لوڈ ہو رہا ہے.. {0}", "正在加载.. {0}")
        Add("Текущий: {0}",
            "Current: {0}", "Поточний: {0}", "Aktuell: {0}", "Corrente: {0}", "Actual: {0}",
            "Actuel : {0}", "Atual: {0}", "الحالي: {0}", "मौजूदा: {0}", "বর্তমান: {0}", "موجودہ: {0}", "当前：{0}")
        Add("выбрана папка: {0}",
            "folder selected: {0}", "теку вибрано: {0}", "Ordner gewählt: {0}", "cartella selezionata: {0}", "carpeta seleccionada: {0}",
            "dossier sélectionné : {0}", "pasta selecionada: {0}", "تم اختيار المجلد: {0}", "फ़ोल्डर चुना गया: {0}",
            "ফোল্ডার নির্বাচিত: {0}", "فولڈر منتخب ہوا: {0}", "已选择文件夹：{0}")
        Add("удалён: {0}",
            "file deleted: {0}", "видалено: {0}", "gelöscht: {0}", "eliminato: {0}", "eliminado: {0}",
            "supprimé : {0}", "excluído: {0}", "تم الحذف: {0}", "हटाया गया: {0}", "মোছা হয়েছে: {0}", "حذف ہوا: {0}", "已删除：{0}")
        Add("{0} с",
            "{0} s", "{0} с", "{0} s", "{0} s", "{0} s", "{0} s", "{0} s", "{0} ث", "{0} से", "{0} সে", "{0} س", "{0} 秒")
        Add("{0} МиБ",
            "{0} MiB", "{0} МіБ", "{0} MiB", "{0} MiB", "{0} MiB", "{0} Mio", "{0} MiB", "{0} ميب", "{0} MiB", "{0} MiB", "{0} MiB", "{0} MiB")
        Add("{0} КиБ",
            "{0} KiB", "{0} КіБ", "{0} KiB", "{0} KiB", "{0} KiB", "{0} Kio", "{0} KiB", "{0} كيب", "{0} KiB", "{0} KiB", "{0} KiB", "{0} KiB")
        Add("{0} Б",
            "{0} B", "{0} Б", "{0} B", "{0} B", "{0} B", "{0} o", "{0} B", "{0} بايت", "{0} B", "{0} B", "{0} B", "{0} B")
        ' --- the settings file filter of the export and import dialogs (UX-220) ---------------------------
        Add("{0} - настройки|*.fms-settings.json",
            "{0} - settings|*.fms-settings.json", "{0} - налаштування|*.fms-settings.json", "{0} - Einstellungen|*.fms-settings.json",
            "{0} - impostazioni|*.fms-settings.json", "{0} - ajustes|*.fms-settings.json", "{0} - réglages|*.fms-settings.json",
            "{0} - definições|*.fms-settings.json", "{0} - الإعدادات|*.fms-settings.json", "{0} - सेटिंग्स|*.fms-settings.json",
            "{0} - সেটিংস|*.fms-settings.json", "{0} - سیٹنگز|*.fms-settings.json", "{0} - 设置|*.fms-settings.json")
        Add("Порядок сортировки файлов: {0}",
            "File sort order: {0}", "Порядок сортування файлів: {0}", "Sortierreihenfolge der Dateien: {0}",
            "Ordinamento dei file: {0}", "Orden de clasificación de archivos: {0}", "Ordre de tri des fichiers : {0}",
            "Ordem de classificação dos arquivos: {0}", "ترتيب فرز الملفات: {0}", "फ़ाइल क्रमबद्ध करने का क्रम: {0}",
            "ফাইল সাজানোর ক্রম: {0}", "فائل ترتیب دینے کی ترتیب: {0}", "文件排序方式：{0}")
        Add("{0} ГиБ",
            "{0} GiB", "{0} ГіБ", "{0} GiB", "{0} GiB", "{0} GiB", "{0} Gio", "{0} GiB", "{0} غيب", "{0} GiB", "{0} GiB", "{0} GiB", "{0} GiB")
        Add("Не удалось проверить введённый текст",
            "The entered text could not be checked", "Не вдалося перевірити введений текст", "Der eingegebene Text konnte nicht geprüft werden",
            "Impossibile verificare il testo inserito", "No se pudo comprobar el texto introducido", "Impossible de vérifier le texte saisi",
            "Não foi possível verificar o texto introduzido", "تعذّر التحقق من النص المُدخل", "दर्ज किए गए पाठ की जाँच नहीं हो सकी",
            "লেখা যাচাই করা যায়নি", "درج کیا گیا متن جانچا نہ جا سکا", "无法检查输入的文本")
        ' --- the image editor's close question (126 UX-605) ------------------------------------------
        ' "Сохранить" and "Отмена" are already registered (editor and Support tables).
        Add("Правки не сохранены",
            "The edits are not saved", "Правки не збережено", "Die Änderungen sind nicht gespeichert",
            "Le modifiche non sono salvate", "Los cambios no están guardados", "Les modifications ne sont pas enregistrées",
            "As alterações não foram salvas", "التعديلات غير محفوظة", "बदलाव सहेजे नहीं गए हैं",
            "পরিবর্তনগুলি সংরক্ষিত হয়নি", "تبدیلیاں محفوظ نہیں ہوئیں", "修改尚未保存")
        Add("Закрыть редактор и потерять несохранённые правки?",
            "Close the editor and lose the unsaved edits?", "Закрити редактор і втратити незбережені правки?",
            "Den Editor schließen und die nicht gespeicherten Änderungen verlieren?",
            "Chiudere l'editor e perdere le modifiche non salvate?",
            "¿Cerrar el editor y perder los cambios no guardados?",
            "Fermer l'éditeur et perdre les modifications non enregistrées ?",
            "Fechar o editor e perder as alterações não salvas?",
            "إغلاق المحرّر وفقدان التعديلات غير المحفوظة؟",
            "संपादक बंद करें और न सहेजे गए बदलाव खो दें?",
            "সম্পাদক বন্ধ করে অসংরক্ষিত পরিবর্তন হারাবেন?",
            "ایڈیٹر بند کریں اور غیر محفوظ تبدیلیاں کھو دیں؟",
            "关闭编辑器并丢失未保存的修改？")
        Add("Не сохранять",
            "Don't save", "Не зберігати", "Nicht speichern", "Non salvare", "No guardar",
            "Ne pas enregistrer", "Não salvar", "عدم الحفظ", "सहेजें नहीं",
            "সংরক্ষণ করবেন না", "محفوظ نہ کریں", "不保存")

        ' --- the plain answers of a Yes/No and OK/Cancel question (ticket 243 T-12a, ViewerBoxMapping) --------------
        ' "ОК" and "Отмена" are already registered (SettingsExpansion and Support tables); the same words as the
        ' Share Manager's own table for these two, which has its own copy of the layer.
        Add("Да", "Yes", "Так", "Ja", "Sì", "Sí", "Oui", "Sim", "نعم", "हाँ", "হ্যাঁ", "ہاں", "是")
        Add("Нет", "No", "Ні", "Nein", "No", "No", "Non", "Não", "لا", "नहीं", "না", "نہیں", "否")

        ' --- values that used to be glued to a translated fragment (124 UX-217, the sweep of 2026-10-13) ----------
        ' Each is ONE sentence with its value in a placeholder: where the value goes, and in which direction the
        ' words read around it, is the translator's to decide - a concatenation decides it for all 13 languages.
        Add("{0} · {1}",
            "{0} · {1}", "{0} · {1}", "{0} · {1}", "{0} · {1}", "{0} · {1}", "{0} · {1}",
            "{0} · {1}", "{0} · {1}", "{0} · {1}", "{0} · {1}", "{0} · {1}", "{0} · {1}")
        ' The "Save as.." default name: the stem, a translated marker, the extension.
        Add("{0} (ред.){1}",
            "{0} (edited){1}", "{0} (ред.){1}", "{0} (bearbeitet){1}", "{0} (modificata){1}", "{0} (editada){1}",
            "{0} (modifiée){1}", "{0} (editada){1}", "{0} (معدَّل){1}", "{0} (संपादित){1}", "{0} (সম্পাদিত){1}",
            "{0} (ترمیم شدہ){1}", "{0}（已编辑）{1}")
        ' A file dialog filter pair: the label, the bar, the pattern list - one string, so the label is not glued.
        Add("Архивы|{0}",
            "Archives|{0}", "Архіви|{0}", "Archive|{0}", "Archivi|{0}", "Archivos comprimidos|{0}", "Archives|{0}",
            "Arquivos compactados|{0}", "الأرشيفات|{0}", "संग्रह|{0}", "আর্কাইভ|{0}", "آرکائیوز|{0}", "压缩包|{0}")
        Add("Проверка контейнера.. {0}/{1}",
            "Verifying the container.. {0}/{1}", "Перевірка контейнера.. {0}/{1}", "Container wird geprüft.. {0}/{1}",
            "Verifica del contenitore.. {0}/{1}", "Verificando el contenedor.. {0}/{1}", "Vérification du conteneur.. {0}/{1}",
            "Verificando o contêiner.. {0}/{1}", "جارٍ التحقّق من الحاوية.. {0}/{1}", "कंटेनर की जाँच हो रही है.. {0}/{1}",
            "কন্টেইনার যাচাই করা হচ্ছে.. {0}/{1}", "کنٹینر کی جانچ ہو رہی ہے.. {0}/{1}", "正在校验容器.. {0}/{1}")
        Add("Модель установлена: {0}",
            "Model installed: {0}", "Модель встановлено: {0}", "Modell installiert: {0}",
            "Modello installato: {0}", "Modelo instalado: {0}", "Modèle installé : {0}",
            "Modelo instalado: {0}", "تم تثبيت النموذج: {0}", "मॉडल इंस्टॉल हुआ: {0}",
            "মডেল ইনস্টল হয়েছে: {0}", "ماڈل انسٹال ہوا: {0}", "模型已安装：{0}")
        Add("Готово: пакет {0}",
            "Ready: pack {0}", "Готово: пакет {0}", "Fertig: Paket {0}",
            "Pronto: pacchetto {0}", "Listo: paquete {0}", "Prêt : pack {0}",
            "Pronto: pacote {0}", "جاهز: الحزمة {0}", "तैयार: पैक {0}",
            "প্রস্তুত: প্যাক {0}", "تیار: پیک {0}", "完成：语言包 {0}")

        ' --- the text document: select, copy, and the F1 rows that say so (124 UX-216) ------------------------
        Add("Нечего копировать - выделите текст мышью или нажмите Ctrl+A",
            "Nothing to copy - select text with the mouse or press Ctrl+A", "Нічого копіювати - виділіть текст мишею або натисніть Ctrl+A",
            "Nichts zu kopieren - Text mit der Maus markieren oder Strg+A drücken",
            "Niente da copiare - seleziona il testo con il mouse o premi Ctrl+A", "Nada que copiar - seleccione texto con el ratón o pulse Ctrl+A",
            "Rien à copier - sélectionnez du texte à la souris ou appuyez sur Ctrl+A",
            "Nada para copiar - selecione o texto com o rato ou prima Ctrl+A", "لا شيء لنسخه - حدّد النص بالفأرة أو اضغط Ctrl+A",
            "कॉपी करने के लिए कुछ नहीं - माउस से पाठ चुनें या Ctrl+A दबाएँ",
            "কপি করার কিছু নেই - মাউস দিয়ে লেখা বেছে নিন বা Ctrl+A চাপুন", "کاپی کرنے کو کچھ نہیں - ماؤس سے متن منتخب کریں یا Ctrl+A دبائیں",
            "没有可复制的内容 - 请用鼠标选择文本或按 Ctrl+A")
        Add("Скопировано символов: {0}",
            "Characters copied: {0}", "Скопійовано символів: {0}", "Zeichen kopiert: {0}",
            "Caratteri copiati: {0}", "Caracteres copiados: {0}", "Caractères copiés : {0}",
            "Caracteres copiados: {0}", "عدد الأحرف المنسوخة: {0}", "कॉपी किए गए वर्ण: {0}",
            "কপি করা অক্ষর: {0}", "کاپی کیے گئے حروف: {0}", "已复制字符数：{0}")
        Add("Выделено символов: {0}",
            "Characters selected: {0}", "Виділено символів: {0}", "Zeichen markiert: {0}",
            "Caratteri selezionati: {0}", "Caracteres seleccionados: {0}", "Caractères sélectionnés : {0}",
            "Caracteres selecionados: {0}", "عدد الأحرف المحدّدة: {0}", "चुने गए वर्ण: {0}",
            "নির্বাচিত অক্ষর: {0}", "منتخب حروف: {0}", "已选择字符数：{0}")
        Add("Текстовый файл: выделить всё / копировать выделенное",
            "Text file: select all / copy the selection", "Текстовий файл: виділити все / копіювати виділене",
            "Textdatei: alles markieren / Markierung kopieren",
            "File di testo: seleziona tutto / copia la selezione", "Archivo de texto: seleccionar todo / copiar la selección",
            "Fichier texte : tout sélectionner / copier la sélection",
            "Ficheiro de texto: selecionar tudo / copiar a seleção", "ملف نصي: تحديد الكل / نسخ المحدّد",
            "पाठ फ़ाइल: सब चुनें / चुना हुआ कॉपी करें",
            "টেক্সট ফাইল: সব নির্বাচন / নির্বাচিত অংশ কপি", "متنی فائل: سب منتخب کریں / منتخب کاپی کریں",
            "文本文件：全选 / 复制所选内容")
        Add("Протяжка по тексту",
            "Drag over text", "Перетягування по тексту", "Über Text ziehen",
            "Trascina sul testo", "Arrastrar sobre el texto", "Glisser sur le texte",
            "Arrastar sobre o texto", "السحب فوق النص", "पाठ पर खींचना",
            "লেখার উপর টানা", "متن پر گھسیٹنا", "在文本上拖动")
        Add("Выделить текст в текстовом файле (Esc - снять)",
            "Select text in a text file (Esc clears)", "Виділити текст у текстовому файлі (Esc - зняти)",
            "Text in einer Textdatei markieren (Esc hebt auf)",
            "Seleziona il testo in un file di testo (Esc annulla)", "Seleccionar texto en un archivo de texto (Esc lo quita)",
            "Sélectionner du texte dans un fichier texte (Échap annule)",
            "Selecionar texto num ficheiro de texto (Esc remove)", "تحديد النص في ملف نصي (Esc للإلغاء)",
            "पाठ फ़ाइल में पाठ चुनें (Esc हटाता है)",
            "টেক্সট ফাইলে লেখা নির্বাচন করুন (Esc সরায়)", "متنی فائل میں متن منتخب کریں (Esc ہٹاتا ہے)",
            "在文本文件中选择文本（Esc 取消选择）")
    End Sub

End Class
