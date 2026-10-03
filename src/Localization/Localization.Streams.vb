Option Strict On

Partial Public NotInheritable Class Localization

    ''' <summary>
    ''' Strings of the stream catalog producer and validator (spec 022).
    ''' </summary>
    Private Shared Sub AddStreamStrings()

        Add("Собрать каталог потоков (.zip)..",
            "Build stream catalog (.zip)..", "Зібрати каталог потоків (.zip)..",
            "Stream-Katalog erstellen (.zip)..", "Crea catalogo streaming (.zip)..",
            "Crear catálogo de transmisiones (.zip)..", "Créer le catalogue de flux (.zip)..",
            "Criar catálogo de streams (.zip)..", "..(.zip) إنشاء كتالوج البث",
            "स्ट्रीम कैटलॉग बनाएं (.zip)..", "স্ট্রিম ক্যাটালগ তৈরি করুন (.zip)..",
            "..(.zip) اسٹریم کیٹلاگ بنائیں", "构建流媒体目录 (.zip)..")

        Add("Создать stream-catalog.zip из плейлистов в этой папке",
            "Create stream-catalog.zip from playlists in this folder",
            "Створити stream-catalog.zip із плейлистів у цій папці",
            "stream-catalog.zip aus Playlists in diesem Ordner erstellen",
            "Crea stream-catalog.zip dalle playlist in questa cartella",
            "Crear stream-catalog.zip a partir de listas de reproducción en esta carpeta",
            "Créer stream-catalog.zip à partir des playlists de ce dossier",
            "Criar stream-catalog.zip a partir de playlists nesta pasta",
            "إنشاء stream-catalog.zip من قوائم التشغيل في هذا المجلد",
            "इस फ़ोल्डर में प्लेलिस्ट से stream-catalog.zip बनाएं",
            "এই ফোল্ডারের প্লেলিস্টগুলি থেকে stream-catalog.zip তৈরি করুন",
            "اس فولڈر میں موجود پلے لسٹس سے stream-catalog.zip بنائیں",
            "从此文件夹中的播放列表创建 stream-catalog.zip")

        Add("Выберите папку с плейлистами (.m3u, .m3u8)",
            "Select folder with playlists (.m3u, .m3u8)",
            "Виберіть папку з плейлистами (.m3u, .m3u8)",
            "Ordner mit Playlists auswählen (.m3u, .m3u8)",
            "Seleziona la cartella con le playlist (.m3u, .m3u8)",
            "Seleccionar carpeta con listas de reproducción (.m3u, .m3u8)",
            "Sélectionner le dossier contenant les playlists (.m3u, .m3u8)",
            "Selecionar pasta com playlists (.m3u, .m3u8)",
            "اختر مجلدًا يحتوي على قوائم التشغيل (.m3u, .m3u8)",
            "प्लेलिस्ट वाला फ़ोल्डर चुनें (.m3u, .m3u8)",
            "প্লেলিস্ট সহ ফোল্ডার নির্বাচন করুন (.m3u, .m3u8)",
            "پلے لسٹس والا فولڈر منتخب کریں (.m3u, .m3u8)",
            "选择包含播放列表的文件夹 (.m3u, .m3u8)")

        Add("Сохранить каталог потоков",
            "Save Stream Catalog", "Зберегти каталог потоків",
            "Stream-Katalog speichern", "Salva catalogo streaming",
            "Guardar catálogo de transmisiones", "Enregistrer le catalogue de flux",
            "Salvar catálogo de streams", "حفظ كتالوج البث",
            "स्ट्रीम कैटलॉग सहेजें", "স্ট্রিম ক্যাটালগ সংরক্ষণ করুন",
            "اسٹریم کیٹلاگ محفوظ کریں", "保存流媒体目录")

        Add("Каталог потоков (*.zip)|*.zip|Все файлы (*.*)|*.*",
            "Stream Catalog (*.zip)|*.zip|All Files (*.*)|*.*",
            "Каталог потоків (*.zip)|*.zip|Усі файли (*.*)|*.*",
            "Stream-Katalog (*.zip)|*.zip|Alle Dateien (*.*)|*.*",
            "Catalogo streaming (*.zip)|*.zip|Tutti i file (*.*)|*.*",
            "Catálogo de transmisiones (*.zip)|*.zip|Todos los archivos (*.*)|*.*",
            "Catalogue de flux (*.zip)|*.zip|Tous les fichiers (*.*)|*.*",
            "Catálogo de streams (*.zip)|*.zip|Todos os arquivos (*.*)|*.*",
            "كتالوج البث (*.zip)|*.zip|جميع الملفات (*.*)|*.*",
            "स्ट्रीम कैटलॉग (*.zip)|*.zip|सभी फ़ाइलें (*.*)|*.*",
            "স্ট্রিম ক্যাটালগ (*.zip)|*.zip|সমস্ত ফাইল (*.*)|*.*",
            "اسٹریم کیٹلاگ (*.zip)|*.zip|تمام فائلیں (*.*)|*.*",
            "流媒体目录 (*.zip)|*.zip|所有文件 (*.*)|*.*")

        Add("Замена каталога приведёт к потере {0} из {1} адресов ({2:F1}%). Продолжить запись?",
            "Replacing catalog will lose {0} of {1} addresses ({2:F1}%). Continue writing?",
            "Заміна каталогу призведе до втрати {0} із {1} адрес ({2:F1}%). Продовжити запис?",
            "Das Ersetzen des Katalogs führt zum Verlust von {0} von {1} Adressen ({2:F1}%). Schreiben fortsetzen?",
            "La sostituzione del catalogo comporterà la perdita di {0} su {1} indirizzi ({2:F1}%). Continuare a scrivere?",
            "Reemplazar el catálogo provocará la pérdida de {0} de {1} direcciones ({2:F1}%). ¿Continuar escribiendo?",
            "Le remplacement du catalogue entraînera la perte de {0} sur {1} adresses ({2:F1}%). Continuer l'écriture ?",
            "A substituição do catálogo resultará na perda de {0} de {1} endereços ({2:F1}%). Continuar a gravação?",
            "استبدال الكتالوج سيؤدي إلى فقدان {0} من أصل {1} عنوانًا ({2:F1}%). متابعة الكتابة؟",
            "कैटलॉग बदलने से {1} में से {0} पते ({2:F1}%) नष्ट हो जाएंगे। लिखना जारी रखें?",
            "ক্যাটালগ প্রতিস্থাপন করলে {1} টির মধ্যে {0} টি ঠিকানা ({2:F1}%) হারিয়ে যাবে। লেখা চালিয়ে যাবেন?",
            "کیٹلاگ تبدیل کرنے سے {1} میں سے {0} پتے ({2:F1}%) ضائع ہو جائیں گے۔ لکھنا جاری رکھیں؟",
            "替换目录将丢失 {1} 个地址中的 {0} 个 ({2:F1}%)。继续写入？")

        Add("Предупреждение о потере записей",
            "Warning: Loss of Records", "Попередження про втрату записів",
            "Warnung: Verlust von Einträgen", "Avviso: perdita di voci",
            "Advertencia: pérdida de registros", "Avertissement : perte d'enregistrements",
            "Aviso: perda de registros", "تحذير: فقدان السجلات",
            "चेतावनी: प्रविष्टियों का नुकसान", "সতর্কতা: রেকর্ড হারানো",
            "انتباہ: اندراجات کا نقصان", "警告：记录丢失")

        Add("Каталог потоков успешно создан: {0} записей.",
            "Stream catalog created successfully: {0} entries.",
            "Каталог потоків успішно створено: {0} записів.",
            "Stream-Katalog erfolgreich erstellt: {0} Einträge.",
            "Catalogo streaming creato con successo: {0} voci.",
            "Catálogo de transmisiones creado con éxito: {0} entradas.",
            "Catalogue de flux créé avec succès : {0} entrées.",
            "Catálogo de streams criado com sucesso: {0} entradas.",
            "تم إنشاء كتالوج البث بنجاح: {0} عنصر.",
            "स्ट्रीम कैटलॉग सफलतापूर्वक बनाया गया: {0} प्रविष्टियाँ।",
            "স্ট্রিম ক্যাটালগ সফলভাবে তৈরি হয়েছে: {0}টি এন্ট্রি।",
            "اسٹریم کیٹلاگ کامیابی سے بن گیا: {0} اندراجات۔",
            "流媒体目录创建成功：{0} 条记录。")

        Add("Отчёт сохранён: {0}",
            "Report saved: {0}", "Звіт збережено: {0}",
            "Bericht gespeichert: {0}", "Rapporto salvato: {0}",
            "Informe guardado: {0}", "Rapport enregistré : {0}",
            "Relatório salvo: {0}", "تم حفظ التقرير: {0}",
            "रिपोर्ट सहेजी गई: {0}", "রিপোর্ট সংরক্ষিত হয়েছে: {0}",
            "رپورٹ محفوظ ہو گئی: {0}", "报告已保存：{0}")

        Add("Открыть папку с каталогом?",
            "Open catalog folder?", "Відкрити папку з каталогом?",
            "Katalogordner öffnen?", "Aprire la cartella del catalogo?",
            "¿Abrir carpeta del catálogo?", "Ouvrir le dossier du catalogue ?",
            "Abrir pasta do catálogo?", "فتح مجلد الكتالوج؟",
            "कैटलॉग फ़ोल्डर खोलें?", "ক্যাটালগ ফোল্ডার খুলবেন?",
            "کیٹلاگ فولڈر کھولیں؟", "打开目录所在文件夹？")

        Add("Каталог потоков создан",
            "Stream Catalog Created", "Каталог потоків створено",
            "Stream-Katalog erstellt", "Catalogo streaming creato",
            "Catálogo de transmisiones creado", "Catalogue de flux créé",
            "Catálogo de streams criado", "تم إنشاء كتالوج البث",
            "स्ट्रीम कैटलॉग बन गया", "স্ট্রিম ক্যাটালগ তৈরি হয়েছে",
            "اسٹریم کیٹلاگ بن گیا", "流媒体目录已创建")

        Add("Не удалось создать каталог потоков: {0}",
            "Could not create stream catalog: {0}",
            "Не вдалося створити каталог потоків: {0}",
            "Stream-Katalog konnte nicht erstellt werden: {0}",
            "Impossibile creare il catalogo streaming: {0}",
            "No se pudo crear el catálogo de transmisiones: {0}",
            "Impossible de créer le catalogue de flux : {0}",
            "Não foi possível criar o catálogo de streams: {0}",
            "تعذّر إنشاء كتالوج البث: {0}",
            "स्ट्रीम कैटलॉग नहीं बनाया जा सका: {0}",
            "স্ট্রিম ক্যাটালগ তৈরি করা যায়নি: {0}",
            "اسٹریم کیٹلاگ نہیں بنایا جا سکا: {0}",
            "无法创建流媒体目录：{0}")

        Add("Ошибка создания каталога",
            "Catalog Creation Error", "Помилка створення каталогу",
            "Fehler beim Erstellen des Katalogs", "Errore durante la creazione del catalogo",
            "Error al crear catálogo", "Erreur lors de la création du catalogue",
            "Erro na criação do catálogo", "خطأ في إنشاء الكتالوج",
            "कैटलॉग निर्माण त्रुटि", "ক্যাটালগ তৈরিতে ত্রুটি",
            "کیٹلاگ بنانے میں خرابی", "目录创建错误")

    End Sub

End Class
