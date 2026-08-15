Option Strict On

' <summary>
' Strings of archive browsing (SPECIFICATION_ARCHIVE_BROWSING_DOTNET10.md §10): what the
' status line says on entering an archive, and every refusal the feature can hand back -
' invariant 9 there is that nothing fails silently. See Localization.vb for the key
' convention.
' </summary>
Partial Public NotInheritable Class Localization

    ''' <summary>
    ''' Archive browsing (SPECIFICATION_ARCHIVE_BROWSING_DOTNET10.md §10). Every one of
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

    End Sub

End Class
