Option Strict On

' <summary>
' Strings of the "about file" window of the first panel's "i" item
' (File_Details_Form / FileDetails.vb / Main_Form.FileDetails.vb). See
' Localization.vb for the key convention. Label keys are translated here; the
' VALUES the window shows are language-neutral by construction (invariant numbers,
' units and dates), so they never appear in this table.
' </summary>
Partial Public NotInheritable Class Localization

    Private Shared Sub AddFileDetailsStrings()

        ' --- the window ----------------------------------------------------------------
        Add("О файле",
            "About file", "Про файл", "Über die Datei", "Informazioni sul file",
            "Acerca del archivo", "À propos du fichier", "Sobre o arquivo",
            "حول الملف", "फ़ाइल के बारे में", "ফাইল সম্পর্কে", "فائل کے بارے میں", "关于文件")
        Add("Копировать",
            "Copy", "Копіювати", "Kopieren", "Copia", "Copiar", "Copier", "Copiar",
            "نسخ", "कॉपी करें", "কপি করুন", "کاپی کریں", "复制")
        Add("Скопировано в буфер обмена",
            "Copied to the clipboard", "Скопійовано в буфер обміну", "In die Zwischenablage kopiert",
            "Copiato negli appunti", "Copiado al portapapeles", "Copié dans le presse-papiers",
            "Copiado para a área de transferência", "تم النسخ إلى الحافظة",
            "क्लिपबोर्ड पर कॉपी हो गया", "ক্লিপবোর্ডে কপি হয়েছে", "کلپ بورڈ پر کاپی ہو گیا", "已复制到剪贴板")
        Add("Чтение метаданных файла..",
            "Reading file metadata..", "Читання метаданих файлу..", "Datei-Metadaten werden gelesen..",
            "Lettura dei metadati del file..", "Leyendo los metadatos del archivo..",
            "Lecture des métadonnées du fichier..", "Lendo os metadados do arquivo..",
            "جارٍ قراءة بيانات الملف..", "फ़ाइल का मेटाडेटा पढ़ा जा रहा है..", "ফাইলের মেটাডেটা পড়া হচ্ছে..",
            "فائل کا میٹاڈیٹا پڑھا جا رہا ہے..", "正在读取文件元数据..")
        Add("Файл не выбран",
            "No file is selected", "Файл не обрано", "Keine Datei ausgewählt", "Nessun file selezionato",
            "No hay ningún archivo seleccionado", "Aucun fichier sélectionné", "Nenhum arquivo selecionado",
            "لم يتم تحديد ملف", "कोई फ़ाइल चयनित नहीं है", "কোনো ফাইল নির্বাচিত নেই", "کوئی فائل منتخب نہیں", "未选择文件")
        Add("Файл недоступен",
            "The file is not available", "Файл недоступний", "Die Datei ist nicht verfügbar",
            "Il file non è disponibile", "El archivo no está disponible", "Le fichier n'est pas disponible",
            "O arquivo não está disponível", "الملف غير متاح", "फ़ाइल उपलब्ध नहीं है", "ফাইলটি উপলব্ধ নয়",
            "فائل دستیاب نہیں", "文件不可用")
        Add("Файл прочитан не полностью",
            "Only part of the file was read", "Файл прочитано не повністю", "Die Datei wurde nur teilweise gelesen",
            "Il file è stato letto solo in parte", "El archivo se leyó solo parcialmente",
            "Le fichier n'a été lu que partiellement", "O arquivo foi lido apenas parcialmente",
            "تمت قراءة جزء من الملف فقط", "फ़ाइल का केवल एक हिस्सा पढ़ा गया", "ফাইলের কেবল অংশ পড়া হয়েছে",
            "فائل کا صرف کچھ حصہ پڑھا گیا", "仅读取了文件的一部分")

        ' --- the file itself ------------------------------------------------------------
        Add("Байт",
            "Bytes", "Байти", "Bytes", "Byte", "Bytes", "Octets", "Bytes",
            "بايت", "बाइट", "বাইট", "بائٹس", "字节")
        Add("Создан",
            "Created", "Створено", "Erstellt", "Creato", "Creado", "Créé", "Criado",
            "تاريخ الإنشاء", "बनाया गया", "তৈরি", "بنایا گیا", "创建日期")
        Add("Открыт",
            "Accessed", "Відкрито", "Letzter Zugriff", "Ultimo accesso", "Último acceso", "Dernier accès",
            "Último acesso", "آخر وصول", "अंतिम पहुँच", "সর্বশেষ ব্যবহার", "آخری رسائی", "访问日期")
        Add("Атрибуты",
            "Attributes", "Атрибути", "Attribute", "Attributi", "Atributos", "Attributs", "Atributos",
            "الخصائص", "विशेषताएँ", "বৈশিষ্ট্য", "خصوصیات", "属性")
        Add("скрытый",
            "hidden", "приховано", "versteckt", "nascosto", "oculto", "masqué", "oculto",
            "مخفي", "छिपा हुआ", "লুকানো", "چھپا ہوا", "隐藏")

        ' --- the image -------------------------------------------------------------------
        Add("Разрешение",
            "Resolution", "Роздільна здатність", "Auflösung", "Risoluzione", "Resolución",
            "Résolution", "Resolução", "الدقة", "रेज़ॉल्यूशन", "রেজোলিউশন", "ریزولوشن", "分辨率")
        Add("Глубина цвета",
            "Color depth", "Глибина кольору", "Farbtiefe", "Profondità colore", "Profundidad de color",
            "Profondeur de couleur", "Profundidade de cor", "عمق اللون", "रंग की गहराई",
            "রঙের গভীরতা", "رنگ کی گہرائی", "颜色深度")
        Add("Плотность пикселей",
            "Pixel density", "Щільність пікселів", "Pixeldichte", "Densità dei pixel", "Densidad de píxeles",
            "Densité de pixels", "Densidade de pixels", "كثافة البكسل", "पिक्सेल घनत्व", "পিক্সেল ঘনত্ব",
            "پکسل کثافت", "像素密度")

        ' --- the camera (EXIF) -------------------------------------------------------------
        Add("Камера",
            "Camera", "Камера", "Kamera", "Fotocamera", "Cámara", "Appareil photo", "Câmera",
            "الكاميرا", "कैमरा", "ক্যামেরা", "کیمرہ", "相机")
        Add("Программа",
            "Software", "Програма", "Software", "Software", "Software", "Logiciel", "Software",
            "البرنامج", "सॉफ़्टवेयर", "সফটওয়্যার", "سافٹ ویئر", "软件")
        Add("Дата съёмки",
            "Date taken", "Дата зйомки", "Aufnahmedatum", "Data di scatto", "Fecha de toma",
            "Date de prise de vue", "Data da captura", "تاريخ التصوير", "खींचने की तारीख", "তোলার তারিখ",
            "تصویر کھینچنے کی تاریخ", "拍摄日期")
        Add("Выдержка",
            "Exposure", "Витримка", "Belichtung", "Esposizione", "Exposición", "Exposition", "Exposição",
            "مدة التعريض", "एक्सपोज़र", "এক্সপোজার", "ایکسپوژر", "曝光时间")
        Add("Диафрагма",
            "Aperture", "Діафрагма", "Blende", "Diaframma", "Abertura", "Ouverture", "Abertura",
            "فتحة العدسة", "अपर्चर", "অ্যাপারচার", "اپرچر", "光圈")
        Add("Фокусное расстояние",
            "Focal length", "Фокусна відстань", "Brennweite", "Lunghezza focale", "Distancia focal",
            "Longueur focale", "Distância focal", "البعد البؤري", "फोकल लंबाई", "ফোকাল দূরত্ব",
            "فوکل فاصلہ", "焦距")
        Add("ISO",
            "ISO", "ISO", "ISO", "ISO", "ISO", "ISO", "ISO",
            "ISO", "ISO", "ISO", "ISO", "ISO")
        Add("Координаты",
            "Coordinates", "Координати", "Koordinaten", "Coordinate", "Coordenadas", "Coordonnées",
            "Coordenadas", "الإحداثيات", "निर्देशांक", "স্থানাঙ্ক", "کوآرڈینیٹس", "坐标")

        ' --- media tags and tracks (modern build) -------------------------------------------
        Add("Медиа",
            "Media", "Медіа", "Medien", "Media", "Medios", "Médias", "Mídia",
            "الوسائط", "मीडिया", "মিডিয়া", "میڈیا", "媒体")
        Add("Исполнитель",
            "Artist", "Виконавець", "Künstler", "Artista", "Intérprete", "Artiste", "Artista",
            "الفنان", "कलाकार", "শিল্পী", "فنکار", "艺术家")
        Add("Альбом",
            "Album", "Альбом", "Album", "Album", "Álbum", "Album", "Álbum",
            "الألبوم", "एल्बम", "অ্যালবাম", "البم", "专辑")
        Add("Год",
            "Year", "Рік", "Jahr", "Anno", "Año", "Année", "Ano",
            "السنة", "वर्ष", "বছর", "سال", "年份")
        Add("Трек",
            "Track", "Трек", "Titelnummer", "Traccia", "Pista", "Piste", "Faixa",
            "المقطوعة", "ट्रैक", "ট্র্যাক", "ٹریک", "曲目")
        Add("Жанр",
            "Genre", "Жанр", "Genre", "Genere", "Género", "Genre", "Gênero",
            "النوع", "शैली", "ধারা", "انواع", "流派")
        Add("Длительность",
            "Duration", "Тривалість", "Dauer", "Durata", "Duración", "Durée", "Duração",
            "المدة", "अवधि", "সময়কাল", "دورانیہ", "时长")
        Add("Кодек",
            "Codec", "Кодек", "Codec", "Codec", "Códec", "Codec", "Codec",
            "برنامج الترميز", "कोडेक", "কোডেক", "کوڈیک", "编解码器")
        Add("Битрейт",
            "Bitrate", "Бітрейт", "Bitrate", "Bitrate", "Tasa de bits", "Débit binaire", "Taxa de bits",
            "معدل البت", "बिटरेट", "বিটরেট", "بٹ ریٹ", "比特率")
        Add("Частота дискретизации",
            "Sample rate", "Частота дискретизації", "Abtastrate", "Frequenza di campionamento",
            "Frecuencia de muestreo", "Fréquence d'échantillonnage", "Taxa de amostragem",
            "معدل العينة", "सैंपल रेट", "স্যাম্পল রেট", "سیمپل ریٹ", "采样率")
        Add("Каналы",
            "Channels", "Канали", "Kanäle", "Canali", "Canales", "Canaux", "Canais",
            "القنوات", "चैनल", "চ্যানেল", "چینل", "声道")
        Add("Кадров в секунду",
            "Frames per second", "Кадрів на секунду", "Frames pro Sekunde", "Fotogrammi al secondo",
            "Fotogramas por segundo", "Images par seconde", "Quadros por segundo", "إطار في الثانية",
            "प्रति सेकंड फ़्रेम", "প্রতি সেকেন্ডে ফ্রেম", "فی سیکنڈ فریمز", "每秒帧数")
        Add("Режиссёр",
            "Director", "Режисер", "Regisseur", "Regista", "Director", "Réalisateur", "Diretor",
            "المخرج", "निर्देशक", "পরিচালক", "ہدایت کار", "导演")
        Add("Сериал",
            "Series", "Серіал", "Serie", "Serie", "Serie", "Série", "Série",
            "المسلسل", "शृंखला", "সিরিজ", "سیریز", "剧集")
        Add("Сезон",
            "Season", "Сезон", "Staffel", "Stagione", "Temporada", "Saison", "Temporada",
            "الموسم", "सीज़न", "সিজন", "سیزن", "季")
        Add("Серия",
            "Episode", "Серія", "Folge", "Episodio", "Episodio", "Épisode", "Episódio",
            "الحلقة", "एपिसोड", "পর্ব", "ایپیسوڈ", "集")

        ' --- documents ------------------------------------------------------------------------
        Add("Страниц",
            "Pages", "Сторінок", "Seiten", "Pagine", "Páginas", "Pages", "Páginas",
            "الصفحات", "पृष्ठ", "পৃষ্ঠা", "صفحات", "页数")
        Add("Автор",
            "Author", "Автор", "Autor", "Autore", "Autor", "Auteur", "Autor",
            "المؤلف", "लेखक", "লেখক", "مصنف", "作者")
        Add("Строк",
            "Lines", "Рядків", "Zeilen", "Righe", "Líneas", "Lignes", "Linhas",
            "الأسطر", "पंक्तियाँ", "লাইন", "سطریں", "行数")
        Add("Слов",
            "Words", "Слів", "Wörter", "Parole", "Palabras", "Mots", "Palavras",
            "الكلمات", "शब्द", "শব্দ", "الفاظ", "词数")
        Add("Символов",
            "Characters", "Символів", "Zeichen", "Caratteri", "Caracteres", "Caractères", "Caracteres",
            "الأحرف", "अक्षर", "অক্ষর", "حروف", "字符数")
        Add("Кодировка",
            "Encoding", "Кодування", "Kodierung", "Codifica", "Codificación", "Encodage", "Codificação",
            "الترميز", "एन्कोडिंग", "এনকোডিং", "انکوڈنگ", "编码")
    End Sub

End Class
