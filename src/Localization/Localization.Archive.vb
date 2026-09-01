Option Strict On

' <summary>
' Strings of archive browsing (010_SPECIFICATION_ARCHIVE_BROWSING_DOTNET10.md §10): what the
' status line says on entering an archive, and every refusal the feature can hand back -
' invariant 9 there is that nothing fails silently. See Localization.vb for the key
' convention.
' </summary>
Partial Public NotInheritable Class Localization

    ''' <summary>
    ''' Archive browsing (010_SPECIFICATION_ARCHIVE_BROWSING_DOTNET10.md §10). Every one of
    ''' these is a refusal or a state the user would otherwise have to guess at: invariant 9
    ''' of that specification is that nothing fails silently.
    ''' </summary>
    Private Shared Sub AddArchiveStrings()

        Add("Архив: {0} файлов",
            "Archive: {0} files", "Архів: {0} файлів", "Archiv: {0} Dateien", "Archivio: {0} file",
            "Archivo comprimido: {0} archivos", "Archive : {0} fichiers", "Arquivo: {0} arquivos",
            "الأرشيف: {0} ملفًا", "संग्रह: {0} फ़ाइलें", "আর্কাইভ: {0}টি ফাইল",
            "آرکائیو: {0} فائلیں", "压缩包：{0} 个文件")
        Add("Показаны первые {0} записей",
            "Showing the first {0} entries", "Показано перші {0} записів",
            "Es werden die ersten {0} Einträge gezeigt", "Vengono mostrate le prime {0} voci",
            "Se muestran las primeras {0} entradas", "Les {0} premières entrées sont affichées",
            "Mostrando as primeiras {0} entradas", "يتم عرض أول {0} عنصر",
            "पहली {0} प्रविष्टियाँ दिखाई जा रही हैं", "প্রথম {0}টি এন্ট্রি দেখানো হচ্ছে",
            "پہلی {0} اندراجات دکھائی جا رہی ہیں", "仅显示前 {0} 个条目")
        Add("В архиве нет поддерживаемых файлов",
            "No supported files in the archive", "В архіві немає підтримуваних файлів",
            "Keine unterstützten Dateien im Archiv", "Nessun file supportato nell'archivio",
            "No hay archivos compatibles en el archivo comprimido",
            "Aucun fichier pris en charge dans l'archive",
            "Nenhum arquivo compatível no arquivo compactado", "لا توجد ملفات مدعومة في الأرشيف",
            "संग्रह में कोई समर्थित फ़ाइल नहीं", "আর্কাইভে সমর্থিত কোনো ফাইল নেই",
            "آرکائیو میں کوئی قابلِ استعمال فائل نہیں", "压缩包中没有可打开的文件")
        Add("Не удалось прочитать архив",
            "Could not read the archive", "Не вдалося прочитати архів",
            "Das Archiv konnte nicht gelesen werden", "Impossibile leggere l'archivio",
            "No se pudo leer el archivo comprimido", "Impossible de lire l'archive",
            "Não foi possível ler o arquivo compactado", "تعذّرت قراءة الأرشيف",
            "संग्रह पढ़ा नहीं जा सका", "আর্কাইভটি পড়া যায়নি",
            "آرکائیو پڑھا نہیں جا سکا", "无法读取该压缩包")
        Add("Архив защищён паролем - откройте его архиватором",
            "The archive is password protected - open it in an archiver",
            "Архів захищено паролем - відкрийте його архіватором",
            "Das Archiv ist kennwortgeschützt - öffnen Sie es in einem Archivprogramm",
            "L'archivio è protetto da password - aprilo in un archiviatore",
            "El archivo comprimido está protegido con contraseña - ábralo en un archivador",
            "L'archive est protégée par mot de passe - ouvrez-la dans un archiveur",
            "O arquivo compactado está protegido por senha - abra-o em um compactador",
            "الأرشيف محمي بكلمة مرور - افتحه ببرنامج ضغط",
            "संग्रह पासवर्ड से सुरक्षित है - इसे किसी आर्काइवर में खोलें",
            "আর্কাইভটি পাসওয়ার্ড দিয়ে সুরক্ষিত - কোনো আর্কাইভারে খুলুন",
            "آرکائیو پاس ورڈ سے محفوظ ہے - اسے کسی آرکائیور میں کھولیں",
            "该压缩包有密码保护 - 请用压缩软件打开")
        Add("В архиве файловые операции недоступны",
            "File operations are not available inside an archive",
            "В архіві файлові операції недоступні",
            "Im Archiv sind Dateioperationen nicht verfügbar",
            "Nell'archivio le operazioni sui file non sono disponibili",
            "Dentro de un archivo comprimido no hay operaciones de archivo",
            "Les opérations sur les fichiers ne sont pas disponibles dans une archive",
            "Operações de arquivo não estão disponíveis dentro de um arquivo compactado",
            "عمليات الملفات غير متاحة داخل الأرشيف",
            "संग्रह के भीतर फ़ाइल संचालन उपलब्ध नहीं हैं",
            "আর্কাইভের ভেতরে ফাইল অপারেশন করা যায় না",
            "آرکائیو کے اندر فائل آپریشنز دستیاب نہیں",
            "压缩包内无法进行文件操作")
        Add("Запись слишком большая для просмотра ({0} МБ)",
            "This entry is too large to preview ({0} MB)",
            "Запис завеликий для перегляду ({0} МБ)",
            "Dieser Eintrag ist zu groß für die Vorschau ({0} MB)",
            "Questa voce è troppo grande per l'anteprima ({0} MB)",
            "Esta entrada es demasiado grande para verla ({0} MB)",
            "Cette entrée est trop volumineuse pour être affichée ({0} Mo)",
            "Esta entrada é grande demais para visualizar ({0} MB)",
            "هذا العنصر أكبر من أن يُعرض ({0} ميغابايت)",
            "यह प्रविष्टि देखने के लिए बहुत बड़ी है ({0} MB)",
            "এই এন্ট্রিটি দেখার জন্য অনেক বড় ({0} MB)",
            "یہ اندراج دیکھنے کے لیے بہت بڑا ہے ({0} MB)",
            "该条目太大，无法预览（{0} MB）")
        Add("Похоже на архив-бомбу",
            "This looks like an archive bomb", "Схоже на архів-бомбу",
            "Das sieht nach einer Archivbombe aus", "Sembra una bomba di decompressione",
            "Parece una bomba de descompresión", "Cela ressemble à une bombe de décompression",
            "Isso parece uma bomba de descompressão", "يبدو أن هذا أرشيف قنبلة",
            "यह आर्काइव बम जैसा लगता है", "এটি আর্কাইভ বোমার মতো মনে হচ্ছে",
            "یہ آرکائیو بم لگتا ہے", "这看起来像是压缩炸弹")
        Add("Не удалось распаковать запись",
            "Could not extract this entry", "Не вдалося розпакувати запис",
            "Der Eintrag konnte nicht entpackt werden", "Impossibile estrarre questa voce",
            "No se pudo extraer esta entrada", "Impossible d'extraire cette entrée",
            "Não foi possível extrair esta entrada", "تعذّر استخراج هذا العنصر",
            "यह प्रविष्टि निकाली नहीं जा सकी", "এই এন্ট্রিটি বের করা যায়নি",
            "یہ اندراج نکالا نہیں جا سکا", "无法解压该条目")

        ' --- entry points and settings (§2.1, §2.3, §9, §12 Ф4) --------------------

        Add("Архивы",
            "Archives", "Архіви", "Archive", "Archivi", "Archivos comprimidos", "Archives",
            "Arquivos compactados", "الأرشيفات", "संग्रह", "আর্কাইভ", "آرکائیوز", "压缩包")

        Add("Открыть архив..",
            "Open archive..", "Відкрити архів..", "Archiv öffnen..", "Apri archivio..",
            "Abrir archivo comprimido..", "Ouvrir une archive..", "Abrir arquivo compactado..",
            "فتح أرشيف..", "संग्रह खोलें..", "আর্কাইভ খুলুন..", "آرکائیو کھولیں..", "打开压缩包..")

        Add("Открыть ZIP или CBZ как папку",
            "Open a ZIP or CBZ as a folder", "Відкрити ZIP або CBZ як папку",
            "ZIP oder CBZ als Ordner öffnen", "Apri uno ZIP o CBZ come cartella",
            "Abrir un ZIP o CBZ como carpeta", "Ouvrir un ZIP ou CBZ comme un dossier",
            "Abrir um ZIP ou CBZ como pasta", "افتح ZIP أو CBZ كمجلد",
            "ZIP या CBZ को फ़ोल्डर की तरह खोलें", "ZIP বা CBZ ফোল্ডারের মতো খুলুন",
            "ZIP یا CBZ کو فولڈر کی طرح کھولیں", "将 ZIP 或 CBZ 作为文件夹打开")

        Add("Закрыть архив",
            "Close archive", "Закрити архів", "Archiv schließen", "Chiudi archivio",
            "Cerrar archivo comprimido", "Fermer l'archive", "Fechar arquivo compactado",
            "إغلاق الأرشيف", "संग्रह बंद करें", "আর্কাইভ বন্ধ করুন", "آرکائیو بند کریں", "关闭压缩包")

        Add("Вернуться в папку, где лежит архив",
            "Return to the folder the archive is in", "Повернутися до папки, де лежить архів",
            "Zurück zum Ordner, in dem das Archiv liegt", "Torna alla cartella in cui si trova l'archivio",
            "Volver a la carpeta donde está el archivo comprimido",
            "Revenir au dossier où se trouve l'archive",
            "Voltar à pasta onde está o arquivo compactado",
            "العودة إلى المجلد الذي يحتوي على الأرشيف",
            "उस फ़ोल्डर पर लौटें जहाँ संग्रह है", "আর্কাইভটি যে ফোল্ডারে আছে সেখানে ফিরে যান",
            "اس فولڈر پر واپس جائیں جہاں آرکائیو موجود ہے", "返回压缩包所在的文件夹")

        Add("Архив закрыт",
            "Archive closed", "Архів закрито", "Archiv geschlossen", "Archivio chiuso",
            "Archivo comprimido cerrado", "Archive fermée", "Arquivo compactado fechado",
            "تم إغلاق الأرشيف", "संग्रह बंद हुआ", "আর্কাইভ বন্ধ হয়েছে", "آرکائیو بند ہو گیا", "压缩包已关闭")

        Add("Лимит кэша архивов, МБ",
            "Archive cache limit, MB", "Ліміт кешу архівів, МБ", "Cache-Grenze für Archive, MB",
            "Limite cache archivi, MB", "Límite de caché de archivos comprimidos, MB",
            "Limite du cache des archives, Mo", "Limite de cache de arquivos compactados, MB",
            "حد ذاكرة تخزين الأرشيفات المؤقتة، ميغابايت", "संग्रह कैश सीमा, MB",
            "আর্কাইভ ক্যাশ সীমা, MB", "آرکائیو کیش کی حد، MB", "压缩包缓存上限（MB）")

        Add("Сколько места на диске может занимать временная распаковка одного открытого архива.",
            "How much disk space one open archive's temporary extraction may use.",
            "Скільки місця на диску може займати тимчасова розпаковка одного відкритого архіву.",
            "Wie viel Speicherplatz die temporäre Entpackung eines geöffneten Archivs belegen darf.",
            "Quanto spazio su disco può occupare l'estrazione temporanea di un archivio aperto.",
            "Cuánto espacio en disco puede ocupar la extracción temporal de un archivo comprimido abierto.",
            "Combien d'espace disque l'extraction temporaire d'une archive ouverte peut occuper.",
            "Quanto espaço em disco a extração temporária de um arquivo compactado aberto pode ocupar.",
            "مقدار مساحة القرص التي يمكن أن يشغلها الاستخراج المؤقت لأرشيف مفتوح واحد.",
            "एक खुले संग्रह की अस्थायी निकासी कितनी डिस्क जगह ले सकती है।",
            "একটি খোলা আর্কাইভের সাময়িক এক্সট্র্যাকশন কতটা ডিস্ক স্থান নিতে পারে।",
            "ایک کھلے آرکائیو کی عارضی نکاسی ڈسک کی کتنی جگہ لے سکتی ہے۔",
            "一个已打开压缩包的临时解压内容最多可占用的磁盘空间。")

        Add("Лимит одной записи архива, МБ",
            "Archive entry limit, MB", "Ліміт одного запису архіву, МБ",
            "Grenze für einen Archiveintrag, MB", "Limite per una singola voce dell'archivio, MB",
            "Límite de entrada del archivo, MB", "Limite d'une entrée d'archive, Mo",
            "Limite de entrada do arquivo, MB", "حد عنصر واحد في الأرشيف، ميغابايت",
            "संग्रह की एक प्रविष्टि की सीमा, MB", "আর্কাইভের একটি এন্ট্রির সীমা, MB",
            "آرکائیو کے ایک اندراج کی حد، MB", "压缩包单个条目上限（MB）")

        Add("Запись крупнее этого не распаковывается, а показывает честный отказ вместо картинки.",
            "An entry larger than this is not extracted - it shows a plain refusal instead of a picture.",
            "Запис, більший за це значення, не розпаковується - замість картинки показується чесна відмова.",
            "Ein Eintrag, der größer ist, wird nicht entpackt - statt eines Bildes erscheint eine klare Ablehnung.",
            "Una voce più grande di questo valore non viene estratta - al posto dell'immagine compare un rifiuto chiaro.",
            "Una entrada mayor que esto no se extrae; en lugar de la imagen se muestra un rechazo claro.",
            "Une entrée plus grande que cela n'est pas extraite - un refus clair s'affiche à la place de l'image.",
            "Uma entrada maior que isso não é extraída - em vez da imagem aparece uma recusa clara.",
            "لا يُستخرج عنصر أكبر من هذا الحد - يظهر رفض واضح بدلاً من الصورة.",
            "इससे बड़ी प्रविष्टि नहीं निकाली जाती - चित्र की जगह स्पष्ट अस्वीकृति दिखती है।",
            "এর চেয়ে বড় এন্ট্রি বের করা হয় না - ছবির বদলে স্পষ্ট প্রত্যাখ্যান দেখানো হয়।",
            "اس سے بڑا اندراج نہیں نکالا جاتا - تصویر کی بجائے واضح انکار دکھایا جاتا ہے۔",
            "超过此大小的条目不会被解压，会显示明确的拒绝提示而不是图片。")

        Add("Лимит записей в архиве",
            "Archive entries limit", "Ліміт записів в архіві", "Grenze für Archiveinträge",
            "Limite di voci nell'archivio", "Límite de entradas del archivo comprimido",
            "Limite d'entrées dans une archive", "Limite de entradas no arquivo compactado",
            "حد عدد عناصر الأرشيف", "संग्रह प्रविष्टियों की सीमा", "আর্কাইভ এন্ট্রির সীমা",
            "آرکائیو اندراجات کی حد", "压缩包条目数量上限")

        Add("Архив с большим числом записей показывается обрезанным до этого количества, и строка состояния предупреждает об этом.",
            "An archive with more entries than this is shown truncated to this count, and the status line says so.",
            "Архів із більшою кількістю записів показується обрізаним до цього числа, і рядок стану про це попереджає.",
            "Ein Archiv mit mehr Einträgen wird auf diese Anzahl gekürzt angezeigt, und die Statuszeile weist darauf hin.",
            "Un archivio con più voci di questo numero viene mostrato troncato a questo conteggio, e la barra di stato lo segnala.",
            "Un archivo comprimido con más entradas que esto se muestra recortado a esa cantidad, y la línea de estado lo indica.",
            "Une archive avec plus d'entrées que cela est affichée tronquée à ce nombre, et la ligne d'état le signale.",
            "Um arquivo compactado com mais entradas que isso é mostrado truncado nesse número, e a linha de status avisa.",
            "الأرشيف الذي يحتوي على عناصر أكثر من هذا العدد يُعرض مقتطعًا عند هذا العدد، ويشير سطر الحالة إلى ذلك.",
            "इससे अधिक प्रविष्टियों वाला संग्रह इस संख्या तक सीमित दिखाया जाता है, और स्थिति पंक्ति यह बताती है।",
            "এর চেয়ে বেশি এন্ট্রি থাকা আর্কাইভ এই সংখ্যা পর্যন্ত সীমিত করে দেখানো হয়, এবং স্ট্যাটাস লাইন তা জানায়।",
            "اس سے زیادہ اندراجات والا آرکائیو اس تعداد تک محدود دکھایا جاتا ہے، اور اسٹیٹس لائن یہ بتاتی ہے۔",
            "超过此数量的压缩包会被截断显示，状态栏会提示这一点。")

    End Sub

End Class
