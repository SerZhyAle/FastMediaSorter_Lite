#If Not NETFRAMEWORK Then
Option Strict On

' Specification 260. Columns: ru, en, uk, de, it, es, fr, pt, ar, hi, bn, ur, zh.
Partial Public NotInheritable Class Localization
    Private Shared Sub AddManagerArchiveStrings()
        Add("Создать ZIP: архив включает все выбранные папки и файлы; удаление оригиналов выключено по умолчанию и разрешается только после проверки и публикации архива",
            "Create ZIP: the archive includes all selected folders and files; deleting originals is off by default and requires verified archive publication",
            "Створити ZIP: архів містить усі вибрані папки й файли; видалення оригіналів типово вимкнено й дозволено лише після перевірки та публікації архіву",
            "ZIP erstellen: Das Archiv enthält alle gewählten Ordner und Dateien; Originale werden standardmäßig behalten und erst nach geprüfter Archivveröffentlichung gelöscht",
            "Crea ZIP: l'archivio include tutte le cartelle e i file selezionati; l'eliminazione degli originali è disattivata e richiede la pubblicazione verificata dell'archivio",
            "Crear ZIP: incluye todas las carpetas y archivos seleccionados; eliminar originales está desactivado y requiere publicar el archivo verificado",
            "Créer un ZIP : inclut tous les dossiers et fichiers sélectionnés ; la suppression des originaux est désactivée et exige la publication vérifiée de l'archive",
            "Criar ZIP: inclui todas as pastas e arquivos selecionados; excluir originais fica desativado e exige a publicação verificada do arquivo",
            "إنشاء ZIP: يتضمن جميع المجلدات والملفات المحددة؛ حذف الملفات الأصلية معطل افتراضياً ويتطلب نشر الأرشيف بعد التحقق منه",
            "ZIP बनाएँ: सभी चुने फ़ोल्डर और फ़ाइलें शामिल हैं; मूल हटाना डिफ़ॉल्ट रूप से बंद है और सत्यापित संग्रह प्रकाशित होने पर ही संभव है",
            "ZIP তৈরি করুন: নির্বাচিত সব ফোল্ডার ও ফাইল অন্তর্ভুক্ত; মূল ফাইল মোছা ডিফল্টভাবে বন্ধ এবং যাচাইকৃত আর্কাইভ প্রকাশের পরে সম্ভব",
            "ZIP بنائیں: تمام منتخب فولڈر اور فائلیں شامل ہیں؛ اصل حذف کرنا پہلے سے بند ہے اور تصدیق شدہ آرکائیو کی اشاعت کے بعد ہی ممکن ہے",
            "创建 ZIP：包含所选文件夹及所有文件；默认保留原文件，仅在验证并发布压缩包后才允许删除")
        Add("Распаковать здесь: выберите один архив; папки и обычные файлы сохраняются, исходный архив остаётся, извлечённые программы не запускаются; Esc отменяет операцию",
            "Extract here: select one archive; folders and ordinary files are preserved, the archive is kept, extracted programs never run; Esc cancels the operation",
            "Розпакувати тут: виберіть один архів; папки та звичайні файли зберігаються, архів залишається, видобуті програми не запускаються; Esc скасовує операцію",
            "Hier entpacken: Ein Archiv wählen; Ordner und normale Dateien bleiben erhalten, das Archiv bleibt bestehen, Programme werden nie gestartet; Esc bricht ab",
            "Estrai qui: seleziona un archivio; conserva cartelle e file ordinari, mantiene l'archivio e non avvia programmi estratti; Esc annulla l'operazione",
            "Extraer aquí: selecciona un archivo; conserva carpetas, archivos normales y el original, sin ejecutar programas extraídos; Esc cancela la operación",
            "Extraire ici : sélectionnez une archive ; les dossiers, fichiers ordinaires et l'archive sont conservés, aucun programme extrait n'est lancé ; Échap annule",
            "Extrair aqui: selecione um arquivo; pastas, arquivos comuns e o original são preservados, programas extraídos não são executados; Esc cancela a operação",
            "استخراج هنا: حدد أرشيفاً واحداً؛ تُحفظ المجلدات والملفات العادية والأرشيف، ولا تُشغّل البرامج المستخرجة؛ Esc يلغي العملية",
            "यहाँ निकालें: एक संग्रह चुनें; फ़ोल्डर और सामान्य फ़ाइलें सुरक्षित रहती हैं, संग्रह रखा जाता है, निकाले प्रोग्राम नहीं चलते; Esc से रद्द करें",
            "এখানে নিষ্কাশন করুন: একটি আর্কাইভ বাছুন; ফোল্ডার, সাধারণ ফাইল ও মূল আর্কাইভ রাখা হয়, নিষ্কাশিত প্রোগ্রাম চলে না; Esc বাতিল করে",
            "یہاں نکالیں: ایک آرکائیو منتخب کریں؛ فولڈر، عام فائلیں اور اصل آرکائیو محفوظ رہتے ہیں، نکالے گئے پروگرام نہیں چلتے؛ Esc منسوخ کرتا ہے",
            "解压到此处：选择一个压缩包；保留文件夹结构、普通文件和原压缩包，不运行解压出的程序；Esc 取消操作")
        Add("Создать ZIP", "Create ZIP", "Створити ZIP", "ZIP erstellen", "Crea ZIP", "Crear ZIP", "Créer un ZIP", "Criar ZIP", "إنشاء ZIP", "ZIP बनाएँ", "ZIP তৈরি করুন", "ZIP بنائیں", "创建 ZIP")
        Add("Распаковать здесь", "Extract here", "Розпакувати тут", "Hier entpacken", "Estrai qui", "Extraer aquí", "Extraire ici", "Extrair aqui", "استخراج هنا", "यहाँ निकालें", "এখানে নিষ্কাশন করুন", "یہاں نکالیں", "解压到此处")
        Add("Удалить оригиналы после проверки архива", "Delete originals after verified archive", "Видалити оригінали після перевірки архіву", "Originale nach Archivprüfung löschen", "Elimina originali dopo la verifica dell'archivio", "Eliminar originales tras verificar el archivo", "Supprimer les originaux après vérification de l'archive", "Excluir originais após verificar o arquivo", "حذف الملفات الأصلية بعد التحقق من الأرشيف", "संग्रह सत्यापित होने के बाद मूल हटाएँ", "আর্কাইভ যাচাইয়ের পরে মূল ফাইল মুছুন", "آرکائیو کی تصدیق کے بعد اصل حذف کریں", "验证压缩包后删除原文件")
        Add("Подготовка архива", "Preparing archive", "Підготовка архіву", "Archiv wird vorbereitet", "Preparazione archivio", "Preparando archivo", "Préparation de l'archive", "Preparando arquivo", "جارٍ تحضير الأرشيف", "संग्रह तैयार हो रहा है", "আর্কাইভ প্রস্তুত হচ্ছে", "آرکائیو تیار ہو رہا ہے", "正在准备压缩包")
        Add("Архив: записей {0}; байт {1}", "Archive: {0} entries; {1} bytes", "Архів: записів {0}; байтів {1}", "Archiv: {0} Einträge; {1} Bytes", "Archivio: {0} voci; {1} byte", "Archivo: {0} entradas; {1} bytes", "Archive : {0} entrées ; {1} octets", "Arquivo: {0} entradas; {1} bytes", "الأرشيف: {0} إدخال؛ {1} بايت", "संग्रह: {0} प्रविष्टियाँ; {1} बाइट", "আর্কাইভ: {0} এন্ট্রি; {1} বাইট", "آرکائیو: {0} اندراجات؛ {1} بائٹس", "压缩包：{0} 项；{1} 字节")
        Add("Превышен лимит распаковки: {0}", "Extraction limit exceeded: {0}", "Перевищено ліміт розпакування: {0}", "Entpacklimit überschritten: {0}", "Limite di estrazione superato: {0}", "Límite de extracción superado: {0}", "Limite d'extraction dépassée : {0}", "Limite de extração excedido: {0}", "تم تجاوز حد الاستخراج: {0}", "निष्कर्षण सीमा पार हुई: {0}", "নিষ্কাশন সীমা অতিক্রম করেছে: {0}", "نکالنے کی حد سے تجاوز: {0}", "超出解压限制：{0}")
        Add("Одна запись: {0} байт", "One entry: {0} bytes", "Один запис: {0} байтів", "Ein Eintrag: {0} Bytes", "Una voce: {0} byte", "Una entrada: {0} bytes", "Une entrée : {0} octets", "Uma entrada: {0} bytes", "إدخال واحد: {0} بايت", "एक प्रविष्टि: {0} बाइट", "একটি এন্ট্রি: {0} বাইট", "ایک اندراج: {0} بائٹس", "单个条目：{0} 字节")
        Add("Все записи: {0} байт", "All entries: {0} bytes", "Усі записи: {0} байтів", "Alle Einträge: {0} Bytes", "Tutte le voci: {0} byte", "Todas las entradas: {0} bytes", "Toutes les entrées : {0} octets", "Todas as entradas: {0} bytes", "جميع الإدخالات: {0} بايت", "सभी प्रविष्टियाँ: {0} बाइट", "সব এন্ট্রি: {0} বাইট", "تمام اندراجات: {0} بائٹس", "全部条目：{0} 字节")
        Add("Многотомные архивы не поддерживаются", "Multi-volume archives are not supported", "Багатотомні архіви не підтримуються", "Mehrteilige Archive werden nicht unterstützt", "Gli archivi multivolume non sono supportati", "No se admiten archivos multivolumen", "Les archives multivolumes ne sont pas prises en charge", "Arquivos multivolume não são suportados", "الأرشيفات متعددة الأجزاء غير مدعومة", "बहु-खंड संग्रह समर्थित नहीं हैं", "বহু-খণ্ড আর্কাইভ সমর্থিত নয়", "کثیر حصوں والے آرکائیو معاونت یافتہ نہیں", "不支持分卷压缩包")
    End Sub
End Class
#End If
