Option Strict On
' Columns: Russian key, en, uk, de, it, es, fr, pt, ar, hi, bn, ur, zh.
' Ticket 222: scheduled file operations - the page, the editor, the history, the tray items and the sentences of
' every outcome. Part of the Share Manager's own tables: the feature lives in this program and nowhere else.
Partial Public NotInheritable Class Localization
    Private Shared Sub AddScheduledStrings()
        Add("{0} мин",
            "{0} min", "{0} хв", "{0} Min.", "{0} min",
            "{0} min", "{0} min", "{0} min", "{0} د",
            "{0} मिनट", "{0} মিনিট", "{0} منٹ", "{0} 分钟")
        Add("{0} ч",
            "{0} h", "{0} год", "{0} h", "{0} h",
            "{0} h", "{0} h", "{0} h", "{0} س",
            "{0} घंटा", "{0} ঘণ্টা", "{0} گھنٹہ", "{0} 小时")
        Add("{0} ч {1} мин",
            "{0} h {1} min", "{0} год {1} хв", "{0} h {1} Min.", "{0} h {1} min",
            "{0} h {1} min", "{0} h {1} min", "{0} h {1} min", "{0} س {1} د",
            "{0} घंटा {1} मिनट", "{0} ঘণ্টা {1} মিনিট", "{0} گھنٹہ {1} منٹ", "{0} 小时 {1} 分钟")
        Add("{0}: последний запуск не удался",
            "{0}: the last run failed", "{0}: останній запуск не вдався", "{0}: Die letzte Ausführung ist fehlgeschlagen", "{0}: l'ultima esecuzione non è riuscita",
            "{0}: la última ejecución falló", "{0} : la dernière exécution a échoué", "{0}: a última execução falhou", "{0}: فشل آخر تشغيل",
            "{0}: पिछला रन विफल रहा", "{0}: শেষ রান ব্যর্থ হয়েছে", "{0}: پچھلا رن ناکام رہا", "{0}：上次运行失败")
        Add("архив не прошёл проверку, исходные файлы оставлены на месте",
            "the archive failed verification, the original files were left in place", "архів не пройшов перевірку, вихідні файли залишено на місці", "das Archiv hat die Prüfung nicht bestanden, die Originaldateien wurden an ihrem Platz belassen", "l'archivio non ha superato la verifica, i file originali sono stati lasciati al loro posto",
            "el archivo comprimido no superó la verificación, los archivos originales se dejaron en su sitio", "l'archive n'a pas passé la vérification, les fichiers d'origine ont été laissés en place", "o arquivo compactado não passou na verificação, os arquivos originais foram mantidos no lugar", "لم يجتز الأرشيف التحقق، وتُركت الملفات الأصلية في مكانها",
            "आर्काइव सत्यापन में विफल रहा; मूल फ़ाइलें अपनी जगह पर छोड़ दी गईं", "আর্কাইভ যাচাইয়ে ব্যর্থ হয়েছে; মূল ফাইলগুলো যথাস্থানে রাখা হয়েছে", "آرکائیو کی تصدیق ناکام رہی؛ اصل فائلیں اپنی جگہ پر چھوڑ دی گئیں", "归档文件未通过校验，原文件已保留在原处")
        Add("Архив принимает только обычные папки, не ресурсы.",
            "An archive accepts only ordinary folders, not resources.", "Архівування приймає лише звичайні папки, а не ресурси.", "Ein Archiv akzeptiert nur normale Ordner, keine Ressourcen.", "Un archivio accetta solo cartelle normali, non risorse.",
            "Un archivo comprimido solo acepta carpetas normales, no recursos.", "L'archivage n'accepte que des dossiers ordinaires, pas des ressources.", "O arquivamento aceita apenas pastas comuns, não recursos.", "تقبل الأرشفة المجلدات العادية فقط، وليس الموارد.",
            "आर्काइव केवल सामान्य फ़ोल्डर स्वीकार करता है, संसाधन नहीं.", "আর্কাইভ শুধু সাধারণ ফোল্ডার নেয়, রিসোর্স নয়।", "آرکائیو صرف عام فولڈر قبول کرتا ہے، وسائل نہیں۔", "归档只支持普通文件夹，不支持资源。")
        Add("Архивация",
            "Archive", "Архівування", "Archivieren", "Archivia",
            "Archivar", "Archiver", "Arquivar", "أرشفة",
            "आर्काइव करना", "আর্কাইভ করা", "آرکائیو کرنا", "归档")
        Add("безвозвратно - {0}",
            "permanently - {0}", "безповоротно - {0}", "endgültig - {0}", "in modo permanente - {0}",
            "de forma permanente - {0}", "définitivement - {0}", "permanentemente - {0}", "نهائيًا - {0}",
            "स्थायी रूप से - {0}", "স্থায়ীভাবে - {0}", "مستقل طور پر - {0}", "永久删除 - {0}")
        Add("Будет обработано файлов: {0}; будет пропущено: {1}; всего {2} МБ.",
            "Files to process: {0}; to skip: {1}; {2} MB in total.", "Буде оброблено файлів: {0}; буде пропущено: {1}; усього {2} МБ.", "Zu verarbeitende Dateien: {0}; zu überspringen: {1}; insgesamt {2} MB.", "File da elaborare: {0}; da saltare: {1}; {2} MB in totale.",
            "Archivos que se procesarán: {0}; que se omitirán: {1}; {2} MB en total.", "Fichiers à traiter : {0} ; à ignorer : {1} ; {2} Mo au total.", "Arquivos a processar: {0}; a ignorar: {1}; {2} MB no total.", "الملفات التي ستُعالج: {0}؛ التي ستُتخطى: {1}؛ الإجمالي {2} ميغابايت.",
            "संसाधित की जाने वाली फ़ाइलें: {0}; छोड़ी जाने वाली: {1}; कुल {2} MB.", "প্রক্রিয়া করা হবে এমন ফাইল: {0}; বাদ দেওয়া হবে: {1}; মোট {2} MB।", "پروسیس کی جانے والی فائلیں: {0}؛ چھوڑی جانے والی: {1}؛ کل {2} MB۔", "将处理的文件：{0}；将跳过：{1}；共 {2} MB。")
        Add("в Корзину",
            "to the Recycle Bin", "до Кошика", "in den Papierkorb", "nel Cestino",
            "a la Papelera de reciclaje", "dans la Corbeille", "para a Lixeira", "إلى سلة المحذوفات",
            "रीसायकल बिन में", "রিসাইকেল বিনে", "ری سائیکل بن میں", "放入回收站")
        Add("в настройках просмотрщика Корзина выключена",
            "the Recycle Bin is switched off in the viewer settings", "у налаштуваннях переглядача Кошик вимкнено", "der Papierkorb ist in den Einstellungen des Viewers deaktiviert", "il Cestino è disattivato nelle impostazioni del visualizzatore",
            "la Papelera de reciclaje está desactivada en la configuración del visor", "la Corbeille est désactivée dans les paramètres de la visionneuse", "a Lixeira está desativada nas configurações do visualizador", "سلة المحذوفات معطّلة في إعدادات العارض",
            "व्यूअर की सेटिंग्स में रीसायकल बिन बंद है", "ভিউয়ারের সেটিংয়ে রিসাইকেল বিন বন্ধ করা আছে", "ویور کی ترتیبات میں ری سائیکل بن بند ہے", "查看器设置中已关闭回收站")
        Add("В списке ресурсов нет подходящих ресурсов. Добавьте SFTP-сервер или сетевую папку в просмотрщике.",
            "There are no suitable resources in the resource list. Add an SFTP server or a network folder in the viewer.", "У списку ресурсів немає відповідних ресурсів. Додайте SFTP-сервер або мережеву папку в переглядачі.", "In der Ressourcenliste gibt es keine passenden Ressourcen. Fügen Sie im Viewer einen SFTP-Server oder einen Netzwerkordner hinzu.", "Nell'elenco delle risorse non ci sono risorse adatte. Aggiungi un server SFTP o una cartella di rete nel visualizzatore.",
            "En la lista de recursos no hay recursos adecuados. Añada un servidor SFTP o una carpeta de red en el visor.", "La liste des ressources ne contient aucune ressource adaptée. Ajoutez un serveur SFTP ou un dossier réseau dans la visionneuse.", "Não há recursos adequados na lista de recursos. Adicione um servidor SFTP ou uma pasta de rede no visualizador.", "لا توجد موارد مناسبة في قائمة الموارد. أضف خادم SFTP أو مجلد شبكة في العارض.",
            "संसाधन सूची में कोई उपयुक्त संसाधन नहीं है. व्यूअर में SFTP सर्वर या नेटवर्क फ़ोल्डर जोड़ें.", "রিসোর্স তালিকায় কোনো উপযুক্ত রিসোর্স নেই। ভিউয়ারে একটি SFTP সার্ভার বা নেটওয়ার্ক ফোল্ডার যোগ করুন।", "وسائل کی فہرست میں کوئی موزوں وسیلہ موجود نہیں۔ ویور میں ایک SFTP سرور یا نیٹ ورک فولڈر شامل کریں۔", "资源列表中没有合适的资源。请在查看器中添加 SFTP 服务器或网络文件夹。")
        Add("Включать скрытые и системные файлы",
            "Include hidden and system files", "Включати приховані та системні файли", "Versteckte Dateien und Systemdateien einschließen", "Includi file nascosti e di sistema",
            "Incluir archivos ocultos y del sistema", "Inclure les fichiers cachés et système", "Incluir arquivos ocultos e de sistema", "تضمين الملفات المخفية وملفات النظام",
            "छिपी हुई और सिस्टम फ़ाइलें शामिल करें", "লুকানো ও সিস্টেম ফাইল অন্তর্ভুক্ত করুন", "پوشیدہ اور سسٹم فائلیں شامل کریں", "包括隐藏文件和系统文件")
        Add("Включая вложенные папки",
            "Include subfolders", "Включно з вкладеними папками", "Unterordner einschließen", "Includi sottocartelle",
            "Incluir subcarpetas", "Inclure les sous-dossiers", "Incluir subpastas", "تضمين المجلدات الفرعية",
            "उप-फ़ोल्डर शामिल करें", "সাবফোল্ডার অন্তর্ভুক্ত করুন", "ذیلی فولڈر شامل کریں", "包括子文件夹")
        Add("Возобновить все",
            "Resume all", "Відновити всі", "Alle fortsetzen", "Riprendi tutte",
            "Reanudar todas", "Tout reprendre", "Retomar tudo", "استئناف الكل",
            "सभी को फिर शुरू करें", "সবগুলো পুনরায় শুরু করুন", "سب کو دوبارہ شروع کریں", "全部恢复")
        Add("Возобновить расписание",
            "Resume the schedule", "Відновити розклад", "Zeitplan fortsetzen", "Riprendi la pianificazione",
            "Reanudar la programación", "Reprendre la planification", "Retomar o agendamento", "استئناف الجدول الزمني",
            "शेड्यूल फिर शुरू करें", "সময়সূচি পুনরায় শুরু করুন", "شیڈول دوبارہ شروع کریں", "恢复计划")
        Add("Возобновлена: причина остановки устранена",
            "Resumed: the cause of the stop is gone", "Відновлено: причину зупинки усунуто", "Fortgesetzt: Die Ursache des Stopps ist behoben", "Ripresa: la causa dell'arresto è stata eliminata",
            "Reanudada: la causa de la detención se ha resuelto", "Reprise : la cause de l'arrêt a disparu", "Retomada: a causa da parada foi eliminada", "تم الاستئناف: زال سبب التوقف",
            "फिर शुरू हुआ: रुकने का कारण दूर हो गया", "পুনরায় শুরু হয়েছে: থামার কারণ দূর হয়েছে", "دوبارہ شروع ہو گیا: رکنے کی وجہ دور ہو گئی", "已恢复：停止原因已消除")
        Add("Возраст «младше» должен быть больше возраста «старше».",
            "The «younger than» age must be greater than the «older than» age.", "Вік «новіші за» має бути більшим за вік «старіші за».", "Das Alter «jünger als» muss größer sein als das Alter «älter als».", "L'età «più recente di» deve essere maggiore dell'età «più vecchio di».",
            "La edad «más reciente que» debe ser mayor que la edad «más antiguo que».", "L'âge « plus récent que » doit être supérieur à l'âge « plus ancien que ».", "A idade «mais recente que» deve ser maior que a idade «mais antigo que».", "يجب أن يكون عمر «أحدث من» أكبر من عمر «أقدم من».",
            "«इससे नई» की सीमा «इससे पुरानी» की सीमा से अधिक होनी चाहिए.", "«এর চেয়ে নতুন»-এর মান অবশ্যই «এর চেয়ে পুরনো»-র মানের চেয়ে বড় হতে হবে।", "«اس سے نئی» کی قدر «اس سے پرانی» کی قدر سے زیادہ ہونی چاہیے۔", "「不足」的值必须大于「超过」的值。")
        Add("Время",
            "Time", "Час", "Zeit", "Ora",
            "Hora", "Heure", "Hora", "الوقت",
            "समय", "সময়", "وقت", "时间")
        Add("Время изменения",
            "Modified", "Змінено", "Geändert", "Modificato",
            "Modificado", "Modifié le", "Modificado em", "تاريخ التعديل",
            "संशोधन का समय", "পরিবর্তনের সময়", "ترمیم کا وقت", "修改时间")
        Add("Вручную",
            "Manual", "Уручну", "Manuell", "Manuale",
            "Manual", "Manuel", "Manual", "يدويًا",
            "मैन्युअल", "ম্যানুয়াল", "دستی", "手动")
        Add("Выберите хотя бы один тип файлов.",
            "Select at least one file type.", "Виберіть хоча б один тип файлів.", "Wählen Sie mindestens einen Dateityp aus.", "Seleziona almeno un tipo di file.",
            "Seleccione al menos un tipo de archivo.", "Sélectionnez au moins un type de fichier.", "Selecione pelo menos um tipo de arquivo.", "اختر نوع ملف واحدًا على الأقل.",
            "कम से कम एक फ़ाइल प्रकार चुनें.", "অন্তত একটি ফাইলের ধরন বেছে নিন।", "کم از کم ایک فائل کی قسم منتخب کریں۔", "请至少选择一种文件类型。")
        Add("Выбор ресурса",
            "Choose a resource", "Вибір ресурсу", "Ressource auswählen", "Scegli una risorsa",
            "Elegir un recurso", "Choisir une ressource", "Escolher um recurso", "اختيار مورد",
            "संसाधन चुनें", "রিসোর্স বেছে নিন", "وسیلہ منتخب کریں", "选择资源")
        Add("Выбрать",
            "Choose", "Вибрати", "Auswählen", "Scegli",
            "Elegir", "Choisir", "Escolher", "اختيار",
            "चुनें", "বেছে নিন", "منتخب کریں", "选择")
        Add("Выбрать..",
            "Choose..", "Вибрати..", "Auswählen..", "Scegli..",
            "Elegir..", "Choisir..", "Escolher..", "اختيار..",
            "चुनें..", "বেছে নিন..", "منتخب کریں..", "选择..")
        Add("Выключите, чтобы остановить все расписания. Запуск вручную остаётся доступным.",
            "Switch off to stop every schedule. Running by hand stays available.", "Вимкніть, щоб зупинити всі розклади. Запуск уручну залишається доступним.", "Ausschalten, um alle Zeitpläne anzuhalten. Der manuelle Start bleibt möglich.", "Disattiva per fermare tutte le pianificazioni. L'esecuzione manuale resta disponibile.",
            "Desactive para detener todas las programaciones. La ejecución manual sigue disponible.", "Désactivez pour arrêter toutes les planifications. L'exécution manuelle reste disponible.", "Desative para parar todos os agendamentos. A execução manual continua disponível.", "عطّل هذا الخيار لإيقاف جميع الجداول الزمنية. يظل التشغيل اليدوي متاحًا.",
            "सभी शेड्यूल रोकने के लिए बंद करें. मैन्युअल रूप से चलाना उपलब्ध रहेगा.", "সব সময়সূচি থামাতে বন্ধ করুন। হাতে চালানো উপলব্ধ থাকবে।", "تمام شیڈول روکنے کے لیے بند کریں۔ دستی طور پر چلانا دستیاب رہے گا۔", "关闭后将停止所有计划。仍可手动运行。")
        Add("Выполнено: {0} файл(ов)",
            "Completed: {0} file(s)", "Виконано: {0} файл(ів)", "Abgeschlossen: {0} Datei(en)", "Completato: {0} file",
            "Completado: {0} archivo(s)", "Terminé : {0} fichier(s)", "Concluído: {0} arquivo(s)", "اكتمل: {0} من الملفات",
            "पूरा हुआ: {0} फ़ाइल(ें)", "সম্পন্ন: {0}টি ফাইল", "مکمل: {0} فائل(یں)", "已完成：{0} 个文件")
        Add("Выполнено: {0} файл(ов), пропущено: {1}",
            "Completed: {0} file(s), skipped: {1}", "Виконано: {0} файл(ів), пропущено: {1}", "Abgeschlossen: {0} Datei(en), übersprungen: {1}", "Completato: {0} file, saltati: {1}",
            "Completado: {0} archivo(s), omitidos: {1}", "Terminé : {0} fichier(s), ignoré(s) : {1}", "Concluído: {0} arquivo(s), ignorado(s): {1}", "اكتمل: {0} من الملفات، تم تخطي: {1}",
            "पूरा हुआ: {0} फ़ाइल(ें), छोड़ी गईं: {1}", "সম্পন্ন: {0}টি ফাইল, বাদ দেওয়া হয়েছে: {1}", "مکمل: {0} فائل(یں)، چھوڑی گئیں: {1}", "已完成：{0} 个文件，已跳过：{1}")
        Add("Выполнить один раз при запуске менеджера",
            "Run once when the manager starts", "Виконати один раз під час запуску менеджера", "Einmal beim Start des Managers ausführen", "Esegui una volta all'avvio del manager",
            "Ejecutar una vez al iniciar el manager", "Exécuter une fois au démarrage du gestionnaire", "Executar uma vez ao iniciar o gerenciador", "التنفيذ مرة واحدة عند بدء تشغيل المدير",
            "प्रबंधक शुरू होने पर एक बार चलाएँ", "ম্যানেজার চালু হলে একবার চালান", "مینیجر شروع ہونے پر ایک بار چلائیں", "管理器启动时运行一次")
        Add("Выполняется с опозданием: запуск был назначен на {0}",
            "Running late: the run was due at {0}", "Виконується із запізненням: запуск був призначений на {0}", "Läuft verspätet: Die Ausführung war für {0} vorgesehen", "In esecuzione in ritardo: l'esecuzione era prevista per {0}",
            "En ejecución con retraso: la ejecución estaba programada para {0}", "Exécution en retard : elle était prévue à {0}", "Em execução com atraso: a execução estava prevista para {0}", "قيد التشغيل متأخرًا: كان التشغيل مجدولًا في {0}",
            "देरी से चल रहा है: रन {0} के लिए निर्धारित था", "দেরিতে চলছে: রানটি {0}-এর জন্য নির্ধারিত ছিল", "تاخیر سے چل رہا ہے: رن {0} پر مقرر تھا", "延迟运行：原定于 {0} 运行")
        Add("Выполняется..",
            "Running..", "Виконується..", "Wird ausgeführt..", "In esecuzione..",
            "En ejecución..", "Exécution..", "Executando..", "قيد التنفيذ..",
            "चल रहा है..", "চলছে..", "چل رہا ہے..", "正在运行..")
        Add("Выполнять операции по расписанию на этом компьютере",
            "Run scheduled operations on this computer", "Виконувати операції за розкладом на цьому комп'ютері", "Geplante Vorgänge auf diesem Computer ausführen", "Esegui le operazioni pianificate su questo computer",
            "Ejecutar las operaciones programadas en este equipo", "Exécuter les opérations planifiées sur cet ordinateur", "Executar operações agendadas neste computador", "تنفيذ العمليات المجدولة على هذا الكمبيوتر",
            "इस कंप्यूटर पर शेड्यूल के अनुसार ऑपरेशन चलाएँ", "এই কম্পিউটারে সময়সূচি অনুযায়ী অপারেশন চালান", "اس کمپیوٹر پر شیڈول کے مطابق آپریشن چلائیں", "在此计算机上按计划运行操作")
        Add("Выполнять по расписанию",
            "Run on a schedule", "Виконувати за розкладом", "Nach Zeitplan ausführen", "Esegui in base a una pianificazione",
            "Ejecutar según una programación", "Exécuter selon une planification", "Executar conforme um agendamento", "التنفيذ حسب الجدول الزمني",
            "शेड्यूल के अनुसार चलाएँ", "সময়সূচি অনুযায়ী চালান", "شیڈول کے مطابق چلائیں", "按计划运行")
        Add("Действие",
            "Action", "Дія", "Aktion", "Azione",
            "Acción", "Action", "Ação", "الإجراء",
            "कार्रवाई", "অ্যাকশন", "عمل", "动作")
        Add("для ресурса не сохранён пароль: {0}",
            "no password is stored for the resource: {0}", "для ресурсу не збережено пароль: {0}", "für die Ressource ist kein Passwort gespeichert: {0}", "per la risorsa non è salvata alcuna password: {0}",
            "no hay ninguna contraseña guardada para el recurso: {0}", "aucun mot de passe n'est enregistré pour la ressource : {0}", "nenhuma senha está salva para o recurso: {0}", "لا توجد كلمة مرور محفوظة للمورد: {0}",
            "संसाधन के लिए पासवर्ड सहेजा नहीं गया है: {0}", "রিসোর্সের জন্য কোনো পাসওয়ার্ড সংরক্ষিত নেই: {0}", "وسیلے کے لیے پاس ورڈ محفوظ نہیں ہے: {0}", "未保存该资源的密码：{0}")
        Add("Для этого компьютера",
            "For this computer", "Для цього комп'ютера", "Für diesen Computer", "Per questo computer",
            "Para este equipo", "Pour cet ordinateur", "Para este computador", "لهذا الكمبيوتر",
            "इस कंप्यूटर के लिए", "এই কম্পিউটারের জন্য", "اس کمپیوٹر کے لیے", "适用于此计算机")
        Add("дн.",
            "d", "дн.", "Tg.", "gg",
            "d", "j", "d", "يوم",
            "दिन", "দিন", "دن", "天")
        Add("ежедневно в {0}",
            "daily at {0}", "щодня о {0}", "täglich um {0}", "ogni giorno alle {0}",
            "todos los días a las {0}", "tous les jours à {0}", "diariamente às {0}", "يوميًا في {0}",
            "प्रतिदिन {0} पर", "প্রতিদিন {0}-এ", "روزانہ {0} پر", "每天 {0}")
        Add("Если выключено, обрабатываются только файлы самой папки.",
            "When off, only the files of the folder itself are processed.", "Якщо вимкнено, обробляються лише файли самої папки.", "Wenn deaktiviert, werden nur die Dateien des Ordners selbst verarbeitet.", "Se disattivato, vengono elaborati solo i file della cartella stessa.",
            "Si está desactivado, solo se procesan los archivos de la propia carpeta.", "Si cette option est désactivée, seuls les fichiers du dossier lui-même sont traités.", "Quando desativado, apenas os arquivos da própria pasta são processados.", "عند التعطيل، تتم معالجة ملفات المجلد نفسه فقط.",
            "बंद होने पर केवल फ़ोल्डर की अपनी फ़ाइलें संसाधित होती हैं.", "বন্ধ থাকলে শুধু ফোল্ডারের নিজস্ব ফাইলগুলো প্রক্রিয়া করা হয়।", "بند ہونے پر صرف فولڈر کی اپنی فائلیں پروسیس کی جاتی ہیں۔", "关闭时，仅处理文件夹本身的文件。")
        Add("Если имя занято",
            "If the name is taken", "Якщо ім'я зайняте", "Wenn der Name bereits vergeben ist", "Se il nome è già in uso",
            "Si el nombre ya está en uso", "Si le nom existe déjà", "Se o nome já existir", "إذا كان الاسم مستخدمًا",
            "यदि नाम पहले से मौजूद हो", "নামটি আগে থেকে থাকলে", "اگر نام پہلے سے موجود ہو", "名称已存在时")
        Add("Ещё не запускалась",
            "Has not run yet", "Ще не запускалася", "Noch nicht ausgeführt", "Non ancora eseguita",
            "Aún no se ha ejecutado", "Pas encore exécutée", "Ainda não executada", "لم تُشغَّل بعد",
            "अभी तक नहीं चला", "এখনও চালানো হয়নি", "ابھی تک نہیں چلا", "尚未运行")
        Add("Ждёт очереди",
            "Waiting in the queue", "Очікує черги", "In der Warteschlange", "In coda",
            "En cola", "En attente de son tour", "Aguardando a vez", "في انتظار دورها",
            "कतार में प्रतीक्षा कर रहा है", "সারিতে অপেক্ষা করছে", "قطار میں انتظار کر رہا ہے", "排队等待中")
        Add("Журнал операций по расписанию",
            "Scheduled operations history", "Журнал запланованих операцій", "Verlauf der geplanten Vorgänge", "Cronologia delle operazioni pianificate",
            "Historial de operaciones programadas", "Historique des opérations planifiées", "Histórico das operações agendadas", "سجل العمليات المجدولة",
            "शेड्यूल किए गए ऑपरेशनों का इतिहास", "সময়সূচিবদ্ধ অপারেশনের ইতিহাস", "شیڈول شدہ آپریشنز کی ہسٹری", "计划操作历史记录")
        Add("Журнал пуст.",
            "The history is empty.", "Журнал порожній.", "Der Verlauf ist leer.", "La cronologia è vuota.",
            "El historial está vacío.", "L'historique est vide.", "O histórico está vazio.", "السجل فارغ.",
            "इतिहास खाली है.", "ইতিহাস খালি।", "ہسٹری خالی ہے۔", "历史记录为空。")
        Add("Журнал..",
            "History..", "Журнал..", "Verlauf..", "Cronologia..",
            "Historial..", "Historique..", "Histórico..", "السجل..",
            "इतिहास..", "ইতিহাস..", "ہسٹری..", "历史记录..")
        Add("За всё время",
            "All time", "За весь час", "Gesamter Zeitraum", "Tutto il periodo",
            "Todo el período", "Toute la période", "Todo o período", "كل الأوقات",
            "पूरी अवधि", "সম্পূর্ণ সময়কাল", "پوری مدت", "全部时间")
        Add("За последние сутки",
            "Last 24 hours", "За останню добу", "Letzte 24 Stunden", "Ultime 24 ore",
            "Últimas 24 horas", "Dernières 24 heures", "Últimas 24 horas", "آخر 24 ساعة",
            "पिछला एक दिन", "গত এক দিন", "پچھلا ایک دن", "最近一天")
        Add("За последний час",
            "Last hour", "За останню годину", "Letzte Stunde", "Ultima ora",
            "Última hora", "Dernière heure", "Última hora", "آخر ساعة",
            "पिछला एक घंटा", "গত এক ঘণ্টা", "پچھلا ایک گھنٹہ", "最近一小时")
        Add("Запустить все",
            "Run all", "Запустити всі", "Alle ausführen", "Esegui tutte",
            "Ejecutar todas", "Tout exécuter", "Executar tudo", "تشغيل الكل",
            "सभी चलाएँ", "সব চালান", "سب چلائیں", "全部运行")
        Add("Запустить все операции сейчас",
            "Run all operations now", "Запустити всі операції зараз", "Alle Vorgänge jetzt ausführen", "Esegui tutte le operazioni ora",
            "Ejecutar todas las operaciones ahora", "Exécuter toutes les opérations maintenant", "Executar todas as operações agora", "تشغيل جميع العمليات الآن",
            "सभी ऑपरेशन अभी चलाएँ", "সব অপারেশন এখনই চালান", "تمام آپریشن ابھی چلائیں", "立即运行所有操作")
        Add("Запустить все сейчас",
            "Run all now", "Запустити всі зараз", "Alle jetzt ausführen", "Esegui tutte ora",
            "Ejecutar todas ahora", "Tout exécuter maintenant", "Executar tudo agora", "تشغيل الكل الآن",
            "सभी अभी चलाएँ", "সব এখনই চালান", "سب ابھی چلائیں", "立即全部运行")
        Add("Запустить сейчас",
            "Run now", "Запустити зараз", "Jetzt ausführen", "Esegui ora",
            "Ejecutar ahora", "Exécuter maintenant", "Executar agora", "تشغيل الآن",
            "अभी चलाएँ", "এখনই চালান", "ابھی چلائیں", "立即运行")
        Add("Значения условий не могут быть отрицательными.",
            "Condition values cannot be negative.", "Значення умов не можуть бути від'ємними.", "Die Werte der Bedingungen dürfen nicht negativ sein.", "I valori delle condizioni non possono essere negativi.",
            "Los valores de las condiciones no pueden ser negativos.", "Les valeurs des conditions ne peuvent pas être négatives.", "Os valores das condições não podem ser negativos.", "لا يمكن أن تكون قيم الشروط سالبة.",
            "शर्तों के मान ऋणात्मक नहीं हो सकते.", "শর্তের মান ঋণাত্মক হতে পারবে না।", "شرائط کی قدریں منفی نہیں ہو سکتیں۔", "条件的值不能为负数。")
        Add("Изменить..",
            "Edit..", "Змінити..", "Bearbeiten..", "Modifica..",
            "Modificar..", "Modifier..", "Editar..", "تعديل..",
            "संपादित करें..", "সম্পাদনা করুন..", "ترمیم کریں..", "编辑..")
        Add("имя уже занято: {0}",
            "the name is already taken: {0}", "ім'я вже зайняте: {0}", "der Name ist bereits vergeben: {0}", "il nome è già in uso: {0}",
            "el nombre ya está en uso: {0}", "le nom existe déjà : {0}", "o nome já existe: {0}", "الاسم مستخدم بالفعل: {0}",
            "नाम पहले से उपयोग में है: {0}", "নামটি আগেই ব্যবহৃত হয়েছে: {0}", "نام پہلے سے استعمال میں ہے: {0}", "名称已被占用：{0}")
        Add("Исключить по маске",
            "Exclude by mask", "Виключити за маскою", "Nach Maske ausschließen", "Escludi per maschera",
            "Excluir por máscara", "Exclure par masque", "Excluir por máscara", "استبعاد حسب النمط",
            "मास्क से बाहर रखें", "মাস্ক অনুযায়ী বাদ দিন", "ماسک کے مطابق خارج کریں", "按通配符排除")
        Add("Источник",
            "Source", "Джерело", "Quelle", "Origine",
            "Origen", "Source", "Origem", "المصدر",
            "स्रोत", "উৎস", "ماخذ", "来源")
        Add("Источник должен быть полным путём: диск:\папка или \\сервер\ресурс\папка.",
            "The source must be a full path: drive:\folder or \\server\share\folder.", "Джерело має бути повним шляхом: диск:\папка або \\сервер\ресурс\папка.", "Die Quelle muss ein vollständiger Pfad sein: Laufwerk:\Ordner oder \\Server\Freigabe\Ordner.", "L'origine deve essere un percorso completo: unità:\cartella o \\server\condivisione\cartella.",
            "El origen debe ser una ruta completa: unidad:\carpeta o \\servidor\recurso compartido\carpeta.", "La source doit être un chemin complet : lecteur:\dossier ou \\serveur\partage\dossier.", "A origem deve ser um caminho completo: unidade:\pasta ou \\servidor\compartilhamento\pasta.", "يجب أن يكون المصدر مسارًا كاملًا: القرص:\المجلد أو \\الخادم\المشاركة\المجلد.",
            "स्रोत पूरा पथ होना चाहिए: ड्राइव:\फ़ोल्डर या \\सर्वर\शेयर\फ़ोल्डर.", "উৎস অবশ্যই পূর্ণ পাথ হতে হবে: ড্রাইভ:\ফোল্ডার বা \\সার্ভার\শেয়ার\ফোল্ডার।", "ماخذ مکمل راستہ ہونا چاہیے: ڈرائیو:\فولڈر یا \\سرور\شیئر\فولڈر۔", "来源必须是完整路径：驱动器:\文件夹 或 \\服务器\共享\文件夹。")
        Add("Источник и назначение",
            "Source and destination", "Джерело і призначення", "Quelle und Ziel", "Origine e destinazione",
            "Origen y destino", "Source et destination", "Origem e destino", "المصدر والوجهة",
            "स्रोत और गंतव्य", "উৎস ও গন্তব্য", "ماخذ اور منزل", "来源和目标")
        Add("Источник и назначение - одна и та же папка ресурса.",
            "The source and the destination are the same resource folder.", "Джерело і призначення - це одна й та сама папка ресурсу.", "Quelle und Ziel sind derselbe Ressourcenordner.", "L'origine e la destinazione sono la stessa cartella della risorsa.",
            "El origen y el destino son la misma carpeta del recurso.", "La source et la destination sont le même dossier de la ressource.", "A origem e o destino são a mesma pasta do recurso.", "المصدر والوجهة هما المجلد نفسه في المورد.",
            "स्रोत और गंतव्य एक ही संसाधन फ़ोल्डर हैं.", "উৎস ও গন্তব্য একই রিসোর্স ফোল্ডার।", "ماخذ اور منزل وسیلے کا ایک ہی فولڈر ہیں۔", "来源和目标是同一个资源文件夹。")
        Add("исходная папка недоступна: {0}",
            "the source folder is not available: {0}", "вихідна папка недоступна: {0}", "der Quellordner ist nicht verfügbar: {0}", "la cartella di origine non è disponibile: {0}",
            "la carpeta de origen no está disponible: {0}", "le dossier source n'est pas disponible : {0}", "a pasta de origem não está disponível: {0}", "المجلد المصدر غير متاح: {0}",
            "स्रोत फ़ोल्डर उपलब्ध नहीं है: {0}", "উৎস ফোল্ডার পাওয়া যাচ্ছে না: {0}", "ماخذ فولڈر دستیاب نہیں ہے: {0}", "来源文件夹不可用：{0}")
        Add("Исходные файлы удаляются только после того, как архив прочитан обратно и сверен.",
            "The original files are deleted only after the archive has been read back and checked.", "Вихідні файли видаляються лише після того, як архів прочитано назад і звірено.", "Die Originaldateien werden erst gelöscht, nachdem das Archiv zurückgelesen und geprüft wurde.", "I file originali vengono eliminati solo dopo che l'archivio è stato riletto e verificato.",
            "Los archivos originales se eliminan solo después de que el archivo comprimido se haya leído de nuevo y verificado.", "Les fichiers d'origine ne sont supprimés qu'après que l'archive a été relue et vérifiée.", "Os arquivos originais só são excluídos depois que o arquivo compactado é relido e conferido.", "لا تُحذف الملفات الأصلية إلا بعد قراءة الأرشيف مجددًا والتحقق منه.",
            "मूल फ़ाइलें तभी हटाई जाती हैं जब आर्काइव को वापस पढ़कर जाँच लिया जाए.", "আর্কাইভ আবার পড়ে যাচাই করার পরই মূল ফাইলগুলো মুছে ফেলা হয়।", "اصل فائلیں صرف اس وقت حذف کی جاتی ہیں جب آرکائیو کو واپس پڑھ کر جانچ لیا جائے۔", "只有在归档文件被重新读取并校验后，才会删除原文件。")
        Add("каждые {0} дн. в {1}",
            "every {0} d at {1}", "кожні {0} дн. о {1}", "alle {0} Tg. um {1}", "ogni {0} gg alle {1}",
            "cada {0} d a las {1}", "tous les {0} j à {1}", "a cada {0} d às {1}", "كل {0} يوم في {1}",
            "हर {0} दिन में {1} पर", "প্রতি {0} দিনে {1}-এ", "ہر {0} دن بعد {1} پر", "每 {0} 天 {1}")
        Add("каждые {0} с {1}",
            "every {0} from {1}", "кожні {0} з {1}", "alle {0} ab {1}", "ogni {0} dalle {1}",
            "cada {0} desde las {1}", "toutes les {0} à partir de {1}", "a cada {0} a partir de {1}", "كل {0} بدءًا من {1}",
            "हर {0}, {1} से", "প্রতি {0} পরপর, {1} থেকে", "ہر {0} بعد، {1} سے", "每 {0}，从 {1} 开始")
        Add("ключ сервера не подтверждён: {0}",
            "the server key is not confirmed: {0}", "ключ сервера не підтверджено: {0}", "der Serverschlüssel wurde nicht bestätigt: {0}", "la chiave del server non è confermata: {0}",
            "la clave del servidor no está confirmada: {0}", "la clé du serveur n'est pas confirmée : {0}", "a chave do servidor não foi confirmada: {0}", "لم يتم تأكيد مفتاح الخادم: {0}",
            "सर्वर की कुंजी की पुष्टि नहीं हुई है: {0}", "সার্ভারের কী নিশ্চিত করা হয়নি: {0}", "سرور کی کلید کی تصدیق نہیں ہوئی: {0}", "服务器密钥未确认：{0}")
        Add("Копирование",
            "Copy", "Копіювання", "Kopieren", "Copia",
            "Copiar", "Copier", "Copiar", "نسخ",
            "कॉपी करना", "কপি করা", "کاپی کرنا", "复制")
        Add("Копирование, перенос, удаление и архивация файлов по расписанию или вручную.",
            "Copy, move, delete and archive files on a schedule or by hand.", "Копіювання, переміщення, видалення та архівування файлів за розкладом або вручну.", "Dateien nach Zeitplan oder manuell kopieren, verschieben, löschen und archivieren.", "Copia, sposta, elimina e archivia file in base a una pianificazione o manualmente.",
            "Copiar, mover, eliminar y archivar archivos según una programación o manualmente.", "Copiez, déplacez, supprimez et archivez des fichiers selon une planification ou manuellement.", "Copie, mova, exclua e arquive arquivos de forma agendada ou manual.", "نسخ الملفات ونقلها وحذفها وأرشفتها حسب جدول زمني أو يدويًا.",
            "शेड्यूल के अनुसार या मैन्युअल रूप से फ़ाइलों की कॉपी, स्थानांतरण, हटाना और आर्काइव.", "সময়সূচি অনুযায়ী বা হাতে ফাইল কপি, সরানো, মুছে ফেলা ও আর্কাইভ করা।", "شیڈول کے مطابق یا دستی طور پر فائلوں کی کاپی، منتقلی، حذف اور آرکائیو۔", "按计划或手动复制、移动、删除和归档文件。")
        Add("копия не совпала с оригиналом: {0}",
            "the copy does not match the original: {0}", "копія не збігається з оригіналом: {0}", "die Kopie stimmt nicht mit dem Original überein: {0}", "la copia non corrisponde all'originale: {0}",
            "la copia no coincide con el original: {0}", "la copie ne correspond pas à l'original : {0}", "a cópia não corresponde ao original: {0}", "النسخة لا تطابق الأصل: {0}",
            "कॉपी मूल फ़ाइल से मेल नहीं खाती: {0}", "কপিটি মূল ফাইলের সঙ্গে মেলেনি: {0}", "کاپی اصل فائل سے مطابقت نہیں رکھتی: {0}", "副本与原文件不一致：{0}")
        Add("Корень ресурса защищён PIN-кодом.",
            "The root of the resource is PIN-locked.", "Корінь ресурсу захищено PIN-кодом.", "Das Stammverzeichnis der Ressource ist durch eine PIN geschützt.", "La radice della risorsa è protetta da un PIN.",
            "La raíz del recurso está protegida con un PIN.", "La racine de la ressource est protégée par un code PIN.", "A raiz do recurso está protegida por um PIN.", "جذر المورد محمي برمز PIN.",
            "संसाधन का रूट PIN से सुरक्षित है.", "রিসোর্সের রুট PIN দিয়ে সুরক্ষিত।", "وسیلے کا روٹ PIN سے محفوظ ہے۔", "该资源的根目录受 PIN 保护。")
        Add("Корзина не приняла файл",
            "the Recycle Bin did not take the file", "Кошик не прийняв файл", "der Papierkorb hat die Datei nicht angenommen", "il Cestino non ha accettato il file",
            "la Papelera de reciclaje no aceptó el archivo", "la Corbeille n'a pas accepté le fichier", "a Lixeira não aceitou o arquivo", "لم تقبل سلة المحذوفات الملف",
            "रीसायकल बिन ने फ़ाइल स्वीकार नहीं की", "রিসাইকেল বিন ফাইলটি নেয়নি", "ری سائیکل بن نے فائل قبول نہیں کی", "回收站未接收该文件")
        Add("Корзина отключена для этого диска",
            "the Recycle Bin is switched off for this drive", "Кошик вимкнено для цього диска", "der Papierkorb ist für dieses Laufwerk deaktiviert", "il Cestino è disattivato per questa unità",
            "la Papelera de reciclaje está desactivada para esta unidad", "la Corbeille est désactivée pour ce lecteur", "a Lixeira está desativada para esta unidade", "سلة المحذوفات معطّلة لهذا القرص",
            "इस ड्राइव के लिए रीसायकल बिन बंद है", "এই ড্রাইভের জন্য রিসাইকেল বিন বন্ধ আছে", "اس ڈرائیو کے لیے ری سائیکل بن بند ہے", "此驱动器已关闭回收站")
        Add("Маска имени",
            "Name mask", "Маска імені", "Namensmaske", "Maschera del nome",
            "Máscara de nombre", "Masque de nom", "Máscara de nome", "نمط الاسم",
            "नाम मास्क", "নামের মাস্ক", "نام کا ماسک", "名称通配符")
        Add("МБ",
            "MB", "МБ", "MB", "MB",
            "MB", "Mo", "MB", "ميغابايت",
            "MB", "MB", "MB", "MB")
        Add("Менеджер запускается при входе в Windows. Расписание работает, пока менеджер запущен.",
            "The manager starts when you sign in to Windows. The schedule works while the manager is running.", "Менеджер запускається під час входу в Windows. Розклад працює, поки менеджер запущено.", "Der Manager startet bei der Anmeldung an Windows. Der Zeitplan funktioniert, solange der Manager läuft.", "Il manager si avvia all'accesso a Windows. La pianificazione funziona finché il manager è in esecuzione.",
            "El manager se inicia al iniciar sesión en Windows. La programación funciona mientras el manager está en ejecución.", "Le gestionnaire démarre à l'ouverture de session Windows. La planification fonctionne tant que le gestionnaire est en cours d'exécution.", "O gerenciador é iniciado quando você entra no Windows. O agendamento funciona enquanto o gerenciador estiver em execução.", "يبدأ المدير عند تسجيل الدخول إلى Windows. يعمل الجدول الزمني ما دام المدير قيد التشغيل.",
            "Windows में साइन इन करने पर प्रबंधक शुरू हो जाता है. शेड्यूल तब तक काम करता है जब तक प्रबंधक चल रहा है.", "Windows-এ সাইন ইন করলে ম্যানেজার চালু হয়। ম্যানেজার চালু থাকা পর্যন্ত সময়সূচি কাজ করে।", "Windows میں سائن اِن کرنے پر مینیجر شروع ہو جاتا ہے۔ شیڈول اس وقت تک کام کرتا ہے جب تک مینیجر چل رہا ہو۔", "登录 Windows 时管理器会自动启动。只要管理器在运行，计划就会生效。")
        Add("Менеджер не запускается при входе в Windows, поэтому расписание работает только пока он открыт. Включить автозапуск можно в настройках менеджера.",
            "The manager does not start when you sign in to Windows, so the schedule works only while it is open. You can turn on autostart in the manager settings.", "Менеджер не запускається під час входу в Windows, тому розклад працює лише поки він відкритий. Увімкнути автозапуск можна в налаштуваннях менеджера.", "Der Manager startet nicht bei der Anmeldung an Windows, daher funktioniert der Zeitplan nur, solange er geöffnet ist. Den Autostart können Sie in den Einstellungen des Managers aktivieren.", "Il manager non si avvia all'accesso a Windows, quindi la pianificazione funziona solo finché è aperto. Puoi attivare l'avvio automatico nelle impostazioni del manager.",
            "El manager no se inicia al iniciar sesión en Windows, por lo que la programación solo funciona mientras está abierto. Puede activar el inicio automático en la configuración del manager.", "Le gestionnaire ne démarre pas à l'ouverture de session Windows, la planification ne fonctionne donc que lorsqu'il est ouvert. Vous pouvez activer le démarrage automatique dans les paramètres du gestionnaire.", "O gerenciador não é iniciado quando você entra no Windows, por isso o agendamento funciona apenas enquanto ele está aberto. Você pode ativar a inicialização automática nas configurações do gerenciador.", "لا يبدأ المدير عند تسجيل الدخول إلى Windows، لذلك يعمل الجدول الزمني فقط ما دام مفتوحًا. يمكنك تفعيل التشغيل التلقائي من إعدادات المدير.",
            "Windows में साइन इन करने पर प्रबंधक शुरू नहीं होता, इसलिए शेड्यूल केवल तभी काम करता है जब वह खुला हो. ऑटोस्टार्ट प्रबंधक की सेटिंग्स में चालू किया जा सकता है.", "Windows-এ সাইন ইন করলে ম্যানেজার চালু হয় না, তাই সময়সূচি শুধু ম্যানেজার খোলা থাকলেই কাজ করে। অটোস্টার্ট ম্যানেজারের সেটিংয়ে চালু করা যায়।", "Windows میں سائن اِن کرنے پر مینیجر شروع نہیں ہوتا، اس لیے شیڈول صرف اسی وقت کام کرتا ہے جب وہ کھلا ہو۔ آٹو اسٹارٹ مینیجر کی ترتیبات میں آن کیا جا سکتا ہے۔", "登录 Windows 时管理器不会自动启动，因此计划只在管理器打开时才会运行。可在管理器设置中启用自动启动。")
        Add("мин",
            "min", "хв", "Min.", "min",
            "min", "min", "min", "دقيقة",
            "मिनट", "মিনিট", "منٹ", "分钟")
        Add("Младше",
            "Younger than", "Новіші за", "Jünger als", "Più recente di",
            "Más reciente que", "Plus récent que", "Mais recente que", "أحدث من",
            "इससे नई", "এর চেয়ে নতুন", "اس سے نئی", "不足")
        Add("на диске не хватило места: {0}",
            "there was not enough space on the disk: {0}", "на диску не вистачило місця: {0}", "auf dem Datenträger war nicht genügend Speicherplatz vorhanden: {0}", "spazio insufficiente sul disco: {0}",
            "no había espacio suficiente en el disco: {0}", "espace insuffisant sur le disque : {0}", "não havia espaço suficiente no disco: {0}", "لم تتوفر مساحة كافية على القرص: {0}",
            "डिस्क पर पर्याप्त जगह नहीं थी: {0}", "ডিস্কে পর্যাপ্ত জায়গা ছিল না: {0}", "ڈسک پر کافی جگہ نہیں تھی: {0}", "磁盘空间不足：{0}")
        Add("на сетевом диске Корзины нет",
            "a network drive has no Recycle Bin", "на мережевому диску немає Кошика", "ein Netzlaufwerk hat keinen Papierkorb", "un'unità di rete non ha il Cestino",
            "una unidad de red no tiene Papelera de reciclaje", "un lecteur réseau n'a pas de Corbeille", "uma unidade de rede não tem Lixeira", "لا توجد سلة محذوفات على قرص الشبكة",
            "नेटवर्क ड्राइव पर रीसायकल बिन नहीं होता", "নেটওয়ার্ক ড্রাইভে রিসাইকেল বিন নেই", "نیٹ ورک ڈرائیو پر ری سائیکل بن نہیں ہوتا", "网络驱动器没有回收站")
        Add("на съёмном носителе Корзины нет",
            "a removable drive has no Recycle Bin", "на знімному носії немає Кошика", "ein Wechseldatenträger hat keinen Papierkorb", "un'unità rimovibile non ha il Cestino",
            "una unidad extraíble no tiene Papelera de reciclaje", "un support amovible n'a pas de Corbeille", "uma unidade removível não tem Lixeira", "لا توجد سلة محذوفات على الوسائط القابلة للإزالة",
            "रिमूवेबल ड्राइव पर रीसायकल बिन नहीं होता", "রিমুভেবল ড্রাইভে রিসাইকেল বিন নেই", "ریموویبل ڈرائیو پر ری سائیکل بن نہیں ہوتا", "可移动驱动器没有回收站")
        Add("Назначение",
            "Destination", "Призначення", "Ziel", "Destinazione",
            "Destino", "Destination", "Destino", "الوجهة",
            "गंतव्य", "গন্তব্য", "منزل", "目标")
        Add("Назначение должно быть полным путём: диск:\папка или \\сервер\ресурс\папка.",
            "The destination must be a full path: drive:\folder or \\server\share\folder.", "Призначення має бути повним шляхом: диск:\папка або \\сервер\ресурс\папка.", "Das Ziel muss ein vollständiger Pfad sein: Laufwerk:\Ordner oder \\Server\Freigabe\Ordner.", "La destinazione deve essere un percorso completo: unità:\cartella o \\server\condivisione\cartella.",
            "El destino debe ser una ruta completa: unidad:\carpeta o \\servidor\recurso compartido\carpeta.", "La destination doit être un chemin complet : lecteur:\dossier ou \\serveur\partage\dossier.", "O destino deve ser um caminho completo: unidade:\pasta ou \\servidor\compartilhamento\pasta.", "يجب أن تكون الوجهة مسارًا كاملًا: القرص:\المجلد أو \\الخادم\المشاركة\المجلد.",
            "गंतव्य पूरा पथ होना चाहिए: ड्राइव:\फ़ोल्डर या \\सर्वर\शेयर\फ़ोल्डर.", "গন্তব্য অবশ্যই পূর্ণ পাথ হতে হবে: ড্রাইভ:\ফোল্ডার বা \\সার্ভার\শেয়ার\ফোল্ডার।", "منزل مکمل راستہ ہونا چاہیے: ڈرائیو:\فولڈر یا \\سرور\شیئر\فولڈر۔", "目标必须是完整路径：驱动器:\文件夹 或 \\服务器\共享\文件夹。")
        Add("Начало в",
            "Start at", "Початок о", "Beginn um", "Inizio alle",
            "Inicio a las", "Début à", "Início às", "البدء في",
            "शुरू होने का समय", "শুরুর সময়", "آغاز کا وقت", "开始时间")
        Add("Не запущена: определение не прошло проверку",
            "Not started: the definition did not pass the check", "Не запущена: визначення не пройшло перевірку", "Nicht gestartet: Die Definition hat die Prüfung nicht bestanden", "Non avviata: la definizione non ha superato la verifica",
            "No iniciada: la definición no superó la verificación", "Non démarrée : la définition n'a pas passé la vérification", "Não iniciada: a definição não passou na verificação", "لم تبدأ: لم يجتز التعريف التحقق",
            "शुरू नहीं हुआ: परिभाषा जाँच में विफल रही", "চালু হয়নি: সংজ্ঞা যাচাইয়ে উত্তীর্ণ হয়নি", "شروع نہیں ہوا: تعریف جانچ میں کامیاب نہیں ہوئی", "未启动：定义未通过检查")
        Add("Не запущено: {0}",
            "Not started: {0}", "Не запущено: {0}", "Nicht gestartet: {0}", "Non avviato: {0}",
            "No iniciado: {0}", "Non démarré : {0}", "Não iniciado: {0}", "لم يبدأ التشغيل: {0}",
            "शुरू नहीं हुआ: {0}", "চালু হয়নি: {0}", "شروع نہیں ہوا: {0}", "未启动：{0}")
        Add("не удалось обработать: {0}",
            "could not process: {0}", "не вдалося обробити: {0}", "konnte nicht verarbeitet werden: {0}", "impossibile elaborare: {0}",
            "no se pudo procesar: {0}", "impossible de traiter : {0}", "não foi possível processar: {0}", "تعذرت المعالجة: {0}",
            "संसाधित नहीं किया जा सका: {0}", "প্রক্রিয়া করা যায়নি: {0}", "پروسیس نہیں کیا جا سکا: {0}", "无法处理：{0}")
        Add("не хватило памяти: {0}",
            "not enough memory: {0}", "не вистачило пам'яті: {0}", "nicht genügend Arbeitsspeicher: {0}", "memoria insufficiente: {0}",
            "memoria insuficiente: {0}", "mémoire insuffisante : {0}", "memória insuficiente: {0}", "الذاكرة غير كافية: {0}",
            "पर्याप्त मेमोरी नहीं थी: {0}", "পর্যাপ্ত মেমরি ছিল না: {0}", "کافی میموری نہیں تھی: {0}", "内存不足：{0}")
        Add("недопустимое имя или путь: {0}",
            "invalid name or path: {0}", "недопустиме ім'я або шлях: {0}", "ungültiger Name oder Pfad: {0}", "nome o percorso non validi: {0}",
            "nombre o ruta no válidos: {0}", "nom ou chemin non valide : {0}", "nome ou caminho inválido: {0}", "اسم أو مسار غير صالح: {0}",
            "अमान्य नाम या पथ: {0}", "অবৈধ নাম বা পাথ: {0}", "غلط نام یا راستہ: {0}", "名称或路径无效：{0}")
        Add("Нет включённых операций.",
            "No operations are enabled.", "Немає увімкнених операцій.", "Keine Vorgänge aktiviert.", "Nessuna operazione abilitata.",
            "No hay operaciones habilitadas.", "Aucune opération n'est activée.", "Nenhuma operação está ativada.", "لا توجد عمليات مفعّلة.",
            "कोई ऑपरेशन चालू नहीं है.", "কোনো অপারেশন চালু নেই।", "کوئی آپریشن فعال نہیں ہے۔", "没有已启用的操作。")
        Add("нет доступа к исходной папке: {0}",
            "no access to the source folder: {0}", "немає доступу до вихідної папки: {0}", "kein Zugriff auf den Quellordner: {0}", "nessun accesso alla cartella di origine: {0}",
            "sin acceso a la carpeta de origen: {0}", "pas d'accès au dossier source : {0}", "sem acesso à pasta de origem: {0}", "لا يوجد وصول إلى المجلد المصدر: {0}",
            "स्रोत फ़ोल्डर तक पहुँच नहीं है: {0}", "উৎস ফোল্ডারে অ্যাক্সেস নেই: {0}", "ماخذ فولڈر تک رسائی نہیں ہے: {0}", "无法访问来源文件夹：{0}")
        Add("нет доступа к папке назначения: {0}",
            "no access to the destination folder: {0}", "немає доступу до папки призначення: {0}", "kein Zugriff auf den Zielordner: {0}", "nessun accesso alla cartella di destinazione: {0}",
            "sin acceso a la carpeta de destino: {0}", "pas d'accès au dossier de destination : {0}", "sem acesso à pasta de destino: {0}", "لا يوجد وصول إلى مجلد الوجهة: {0}",
            "गंतव्य फ़ोल्डर तक पहुँच नहीं है: {0}", "গন্তব্য ফোল্ডারে অ্যাক্সেস নেই: {0}", "منزل فولڈر تک رسائی نہیں ہے: {0}", "无法访问目标文件夹：{0}")
        Add("нет доступа: {0}",
            "no access: {0}", "немає доступу: {0}", "kein Zugriff: {0}", "nessun accesso: {0}",
            "sin acceso: {0}", "pas d'accès : {0}", "sem acesso: {0}", "لا يوجد وصول: {0}",
            "पहुँच नहीं है: {0}", "অ্যাক্সেস নেই: {0}", "رسائی نہیں ہے: {0}", "无法访问：{0}")
        Add("Новые с прошлого запуска",
            "New since the last run", "Нові з минулого запуску", "Neu seit der letzten Ausführung", "Nuovi dall'ultima esecuzione",
            "Nuevos desde la última ejecución", "Nouveaux depuis la dernière exécution", "Novos desde a última execução", "الجديد منذ آخر تشغيل",
            "पिछले रन के बाद के नए", "শেষ রানের পর থেকে নতুন", "پچھلے رن کے بعد کی نئی", "自上次运行以来新增的")
        Add("Одна или несколько масок через «;», например *.jpg;IMG_??.png. Сравнивается имя файла.",
            "One or more masks separated by «;», for example *.jpg;IMG_??.png. The file name is compared.", "Одна або кілька масок через «;», наприклад *.jpg;IMG_??.png. Порівнюється ім'я файлу.", "Eine oder mehrere Masken, getrennt durch «;», zum Beispiel *.jpg;IMG_??.png. Verglichen wird der Dateiname.", "Una o più maschere separate da «;», ad esempio *.jpg;IMG_??.png. Viene confrontato il nome del file.",
            "Una o varias máscaras separadas por «;», por ejemplo *.jpg;IMG_??.png. Se compara el nombre del archivo.", "Un ou plusieurs masques séparés par « ; », par exemple *.jpg;IMG_??.png. Le nom du fichier est comparé.", "Uma ou mais máscaras separadas por «;», por exemplo *.jpg;IMG_??.png. O nome do arquivo é comparado.", "نمط واحد أو أكثر مفصولة بـ «;»، مثل *.jpg;IMG_??.png. تتم مقارنة اسم الملف.",
            "एक या अधिक मास्क, «;» से अलग किए हुए, उदाहरण के लिए *.jpg;IMG_??.png. फ़ाइल के नाम से मिलान किया जाता है.", "«;» দিয়ে আলাদা করা এক বা একাধিক মাস্ক, যেমন *.jpg;IMG_??.png। ফাইলের নামের সঙ্গে মেলানো হয়।", "«;» سے الگ کیے گئے ایک یا زیادہ ماسک، مثلاً *.jpg;IMG_??.png۔ فائل کے نام سے موازنہ کیا جاتا ہے۔", "一个或多个通配符，用「;」分隔，例如 *.jpg;IMG_??.png。比较的是文件名。")
        Add("Операции",
            "Operations", "Операції", "Vorgänge", "Operazioni",
            "Operaciones", "Opérations", "Operações", "العمليات",
            "ऑपरेशन", "অপারেশন", "آپریشنز", "操作")
        Add("Операций нет",
            "No operations", "Операцій немає", "Keine Vorgänge", "Nessuna operazione",
            "Sin operaciones", "Aucune opération", "Nenhuma operação", "لا توجد عمليات",
            "कोई ऑपरेशन नहीं", "কোনো অপারেশন নেই", "کوئی آپریشن نہیں", "无操作")
        Add("Операций: {0}",
            "Operations: {0}", "Операцій: {0}", "Vorgänge: {0}", "Operazioni: {0}",
            "Operaciones: {0}", "Opérations : {0}", "Operações: {0}", "العمليات: {0}",
            "ऑपरेशन: {0}", "অপারেশন: {0}", "آپریشنز: {0}", "操作数：{0}")
        Add("Операция",
            "Operation", "Операція", "Vorgang", "Operazione",
            "Operación", "Opération", "Operação", "العملية",
            "ऑपरेशन", "অপারেশন", "آپریشن", "操作")
        Add("Операция по расписанию",
            "Scheduled operation", "Запланована операція", "Geplanter Vorgang", "Operazione pianificata",
            "Operación programada", "Opération planifiée", "Operação agendada", "عملية مجدولة",
            "शेड्यूल किया गया ऑपरेशन", "সময়সূচিবদ্ধ অপারেশন", "شیڈول شدہ آپریشن", "计划操作")
        Add("определение не прошло проверку",
            "the definition did not pass the check", "визначення не пройшло перевірку", "die Definition hat die Prüfung nicht bestanden", "la definizione non ha superato la verifica",
            "la definición no superó la verificación", "la définition n'a pas passé la vérification", "a definição não passou na verificação", "لم يجتز التعريف التحقق",
            "परिभाषा जाँच में विफल रही", "সংজ্ঞা যাচাইয়ে উত্তীর্ণ হয়নি", "تعریف جانچ میں کامیاب نہیں ہوئی", "定义未通过检查")
        Add("Определение не прошло проверку.",
            "The definition did not pass the check.", "Визначення не пройшло перевірку.", "Die Definition hat die Prüfung nicht bestanden.", "La definizione non ha superato la verifica.",
            "La definición no superó la verificación.", "La définition n'a pas passé la vérification.", "A definição não passou na verificação.", "لم يجتز التعريف التحقق.",
            "परिभाषा जाँच में विफल रही.", "সংজ্ঞা যাচাইয়ে উত্তীর্ণ হয়নি।", "تعریف جانچ میں کامیاب نہیں ہوئی۔", "定义未通过检查。")
        Add("определение не читается этой версией менеджера",
            "the definition cannot be read by this version of the manager", "визначення не читається цією версією менеджера", "die Definition kann von dieser Version des Managers nicht gelesen werden", "la definizione non può essere letta da questa versione del manager",
            "esta versión del manager no puede leer la definición", "la définition n'est pas lisible par cette version du gestionnaire", "a definição não pode ser lida por esta versão do gerenciador", "لا يمكن لهذا الإصدار من المدير قراءة التعريف",
            "प्रबंधक का यह संस्करण परिभाषा को पढ़ नहीं सकता", "ম্যানেজারের এই সংস্করণ সংজ্ঞাটি পড়তে পারে না", "مینیجر کا یہ ورژن تعریف کو پڑھ نہیں سکتا", "此版本的管理器无法读取该定义")
        Add("Оставить обе копии",
            "Keep both copies", "Залишити обидві копії", "Beide Kopien behalten", "Mantieni entrambe le copie",
            "Conservar ambas copias", "Conserver les deux copies", "Manter ambas as cópias", "الاحتفاظ بالنسختين",
            "दोनों कॉपी रखें", "দুটি কপিই রাখুন", "دونوں کاپیاں رکھیں", "保留两个副本")
        Add("Остановлена: {0}",
            "Stopped: {0}", "Зупинена: {0}", "Angehalten: {0}", "Fermata: {0}",
            "Detenida: {0}", "Arrêtée : {0}", "Parada: {0}", "متوقفة: {0}",
            "रोका गया: {0}", "থামানো হয়েছে: {0}", "روک دیا گیا: {0}", "已停止：{0}")
        Add("Остановлено: {0}",
            "Stopped: {0}", "Зупинено: {0}", "Angehalten: {0}", "Fermato: {0}",
            "Detenido: {0}", "Arrêté : {0}", "Parado: {0}", "تم الإيقاف: {0}",
            "रोका गया: {0}", "থামানো হয়েছে: {0}", "روک دیا گیا: {0}", "已停止：{0}")
        Add("Отклонено: эта операция уже выполняется или ждёт очереди",
            "Refused: this operation is already running or waiting in the queue", "Відхилено: ця операція вже виконується або чекає черги", "Abgelehnt: Dieser Vorgang wird bereits ausgeführt oder wartet in der Warteschlange", "Rifiutato: questa operazione è già in esecuzione o in coda",
            "Rechazado: esta operación ya se está ejecutando o está en cola", "Refusé : cette opération est déjà en cours d'exécution ou en attente", "Recusado: esta operação já está em execução ou aguardando a vez", "تم الرفض: هذه العملية قيد التنفيذ بالفعل أو في انتظار دورها",
            "अस्वीकृत: यह ऑपरेशन पहले से चल रहा है या कतार में प्रतीक्षा कर रहा है", "প্রত্যাখ্যাত: এই অপারেশন ইতিমধ্যে চলছে বা সারিতে অপেক্ষা করছে", "مسترد: یہ آپریشن پہلے سے چل رہا ہے یا قطار میں انتظار کر رہا ہے", "已拒绝：此操作已在运行或正在排队等待")
        Add("Открыть в отдельном окне..",
            "Open in a separate window..", "Відкрити в окремому вікні..", "In einem eigenen Fenster öffnen..", "Apri in una finestra separata..",
            "Abrir en una ventana independiente..", "Ouvrir dans une fenêtre séparée..", "Abrir em uma janela separada..", "افتح في نافذة منفصلة..",
            "अलग विंडो में खोलें..", "আলাদা উইন্ডোতে খুলুন..", "الگ ونڈو میں کھولیں..", "在独立窗口中打开..")
        Add("Открыть настройку автозапуска",
            "Open the autostart setting", "Відкрити налаштування автозапуску", "Autostart-Einstellung öffnen", "Apri l'impostazione di avvio automatico",
            "Abrir la configuración de inicio automático", "Ouvrir le paramètre de démarrage automatique", "Abrir a configuração de inicialização automática", "فتح إعداد التشغيل التلقائي",
            "ऑटोस्टार्ट सेटिंग खोलें", "অটোস্টার্ট সেটিং খুলুন", "آٹو اسٹارٹ کی ترتیب کھولیں", "打开自动启动设置")
        Add("Очистить журнал",
            "Clear history", "Очистити журнал", "Verlauf löschen", "Cancella cronologia",
            "Borrar historial", "Effacer l'historique", "Limpar histórico", "مسح السجل",
            "इतिहास साफ़ करें", "ইতিহাস মুছে ফেলুন", "ہسٹری صاف کریں", "清除历史记录")
        Add("Очистить журнал операций по расписанию? Сами операции и их расписание не изменятся.",
            "Clear the history of scheduled operations? The operations and their schedules do not change.", "Очистити журнал запланованих операцій? Самі операції та їхній розклад не зміняться.", "Den Verlauf der geplanten Vorgänge löschen? Die Vorgänge selbst und ihr Zeitplan bleiben unverändert.", "Cancellare la cronologia delle operazioni pianificate? Le operazioni e la loro pianificazione non cambieranno.",
            "¿Borrar el historial de operaciones programadas? Las operaciones y su programación no cambiarán.", "Effacer l'historique des opérations planifiées ? Les opérations elles-mêmes et leur planification ne changent pas.", "Limpar o histórico das operações agendadas? As próprias operações e seus agendamentos não serão alterados.", "هل تريد مسح سجل العمليات المجدولة؟ لن تتغير العمليات نفسها ولا جدولها الزمني.",
            "शेड्यूल किए गए ऑपरेशनों का इतिहास साफ़ करें? ऑपरेशन और उनके शेड्यूल नहीं बदलेंगे.", "সময়সূচিবদ্ধ অপারেশনের ইতিহাস মুছে ফেলবেন? অপারেশন ও তাদের সময়সূচি অপরিবর্তিত থাকবে।", "شیڈول شدہ آپریشنز کی ہسٹری صاف کریں؟ آپریشنز اور ان کے شیڈول تبدیل نہیں ہوں گے۔", "要清除计划操作的历史记录吗？操作及其计划不会改变。")
        Add("Папка внутри ресурса",
            "Folder inside the resource", "Папка всередині ресурсу", "Ordner innerhalb der Ressource", "Cartella all'interno della risorsa",
            "Carpeta dentro del recurso", "Dossier dans la ressource", "Pasta dentro do recurso", "مجلد داخل المورد",
            "संसाधन के अंदर का फ़ोल्डर", "রিসোর্সের ভেতরের ফোল্ডার", "وسیلے کے اندر کا فولڈر", "资源内的文件夹")
        Add("Папка должна лежать внутри корня ресурса: {0}",
            "The folder must be inside the root of the resource: {0}", "Папка має розташовуватися всередині кореня ресурсу: {0}", "Der Ordner muss innerhalb des Stammverzeichnisses der Ressource liegen: {0}", "La cartella deve trovarsi all'interno della radice della risorsa: {0}",
            "La carpeta debe estar dentro de la raíz del recurso: {0}", "Le dossier doit se trouver à l'intérieur de la racine de la ressource : {0}", "A pasta deve estar dentro da raiz do recurso: {0}", "يجب أن يكون المجلد داخل جذر المورد: {0}",
            "फ़ोल्डर संसाधन के रूट के अंदर होना चाहिए: {0}", "ফোল্ডারটি রিসোর্সের রুটের ভেতরে থাকতে হবে: {0}", "فولڈر کو وسیلے کے روٹ کے اندر ہونا چاہیے: {0}", "文件夹必须位于该资源的根目录内：{0}")
        Add("папка назначения недоступна: {0}",
            "the destination folder is not available: {0}", "папка призначення недоступна: {0}", "der Zielordner ist nicht verfügbar: {0}", "la cartella di destinazione non è disponibile: {0}",
            "la carpeta de destino no está disponible: {0}", "le dossier de destination n'est pas disponible : {0}", "a pasta de destino não está disponível: {0}", "مجلد الوجهة غير متاح: {0}",
            "गंतव्य फ़ोल्डर उपलब्ध नहीं है: {0}", "গন্তব্য ফোল্ডার পাওয়া যাচ্ছে না: {0}", "منزل فولڈر دستیاب نہیں ہے: {0}", "目标文件夹不可用：{0}")
        Add("Папка назначения совпадает с источником или лежит внутри него.",
            "The destination is the source folder or lies inside it.", "Папка призначення збігається з джерелом або розташована всередині нього.", "Der Zielordner ist mit der Quelle identisch oder liegt darin.", "La cartella di destinazione coincide con l'origine o si trova al suo interno.",
            "La carpeta de destino coincide con el origen o está dentro de él.", "Le dossier de destination est le dossier source ou se trouve à l'intérieur.", "A pasta de destino é a pasta de origem ou está dentro dela.", "مجلد الوجهة هو المجلد المصدر نفسه أو يقع داخله.",
            "गंतव्य वही फ़ोल्डर है जो स्रोत है, या उसके अंदर स्थित है.", "গন্তব্য উৎস ফোল্ডারের সমান বা তার ভেতরে অবস্থিত।", "منزل وہی فولڈر ہے جو ماخذ ہے، یا اس کے اندر واقع ہے۔", "目标文件夹与来源文件夹相同，或位于其内部。")
        Add("Папка недоступна: {0}",
            "The folder is not available: {0}", "Папка недоступна: {0}", "Der Ordner ist nicht verfügbar: {0}", "La cartella non è disponibile: {0}",
            "La carpeta no está disponible: {0}", "Le dossier n'est pas disponible : {0}", "A pasta não está disponível: {0}", "المجلد غير متاح: {0}",
            "फ़ोल्डर उपलब्ध नहीं है: {0}", "ফোল্ডার পাওয়া যাচ্ছে না: {0}", "فولڈر دستیاب نہیں ہے: {0}", "文件夹不可用：{0}")
        Add("Пароль и ключ сервера берутся из списка ресурсов просмотрщика. Если пароль не сохранён или ключ не подтверждён, операция остановится - без вопросов.",
            "The password and the server key come from the viewer's resource list. If no password is stored or the key is not confirmed, the operation stops - without asking.", "Пароль і ключ сервера беруться зі списку ресурсів переглядача. Якщо пароль не збережено або ключ не підтверджено, операція зупиниться - без запитань.", "Passwort und Serverschlüssel stammen aus der Ressourcenliste des Viewers. Ist kein Passwort gespeichert oder der Schlüssel nicht bestätigt, wird der Vorgang angehalten - ohne Rückfrage.", "La password e la chiave del server provengono dall'elenco delle risorse del visualizzatore. Se la password non è salvata o la chiave non è confermata, l'operazione si ferma - senza fare domande.",
            "La contraseña y la clave del servidor se toman de la lista de recursos del visor. Si no hay una contraseña guardada o la clave no está confirmada, la operación se detiene - sin preguntar.", "Le mot de passe et la clé du serveur proviennent de la liste des ressources de la visionneuse. Si le mot de passe n'est pas enregistré ou si la clé n'est pas confirmée, l'opération s'arrête - sans poser de questions.", "A senha e a chave do servidor vêm da lista de recursos do visualizador. Se a senha não estiver salva ou a chave não estiver confirmada, a operação é interrompida - sem perguntas.", "تُؤخذ كلمة المرور ومفتاح الخادم من قائمة موارد العارض. إذا لم تكن كلمة المرور محفوظة أو لم يتم تأكيد المفتاح، تتوقف العملية - دون أي أسئلة.",
            "पासवर्ड और सर्वर की कुंजी व्यूअर की संसाधन सूची से ली जाती हैं. यदि पासवर्ड सहेजा नहीं गया है या कुंजी की पुष्टि नहीं हुई है, तो ऑपरेशन रुक जाएगा - बिना कोई प्रश्न पूछे.", "পাসওয়ার্ড ও সার্ভারের কী ভিউয়ারের রিসোর্স তালিকা থেকে নেওয়া হয়। পাসওয়ার্ড সংরক্ষিত না থাকলে বা কী নিশ্চিত না হলে অপারেশন থেমে যাবে - কোনো প্রশ্ন ছাড়াই।", "پاس ورڈ اور سرور کی کلید ویور کی وسائل کی فہرست سے لی جاتی ہے۔ اگر پاس ورڈ محفوظ نہ ہو یا کلید کی تصدیق نہ ہوئی ہو تو آپریشن رک جائے گا - بغیر کوئی سوال پوچھے۔", "密码和服务器密钥取自查看器的资源列表。如果未保存密码或密钥未确认，操作将停止 - 不会进行任何询问。")
        Add("Перезаписать",
            "Overwrite", "Перезаписати", "Überschreiben", "Sovrascrivi",
            "Sobrescribir", "Remplacer", "Substituir", "استبدال",
            "अधिलेखित करें", "ওভাররাইট করুন", "اوور رائٹ کریں", "覆盖")
        Add("Перенос",
            "Move", "Переміщення", "Verschieben", "Sposta",
            "Mover", "Déplacer", "Mover", "نقل",
            "स्थानांतरित करना", "সরানো", "منتقل کرنا", "移动")
        Add("По расписанию",
            "On schedule", "За розкладом", "Nach Zeitplan", "Su pianificazione",
            "Según programación", "Selon la planification", "Conforme o agendamento", "حسب الجدول الزمني",
            "शेड्यूल के अनुसार", "সময়সূচি অনুযায়ী", "شیڈول کے مطابق", "按计划")
        Add("Повторять каждые",
            "Repeat every", "Повторювати кожні", "Wiederholen alle", "Ripeti ogni",
            "Repetir cada", "Répéter toutes les", "Repetir a cada", "التكرار كل",
            "दोहराने का अंतराल", "পুনরাবৃত্তির বিরতি", "دہرانے کا وقفہ", "重复间隔")
        Add("Показаны первые {0}.",
            "The first {0} are shown.", "Показано перші {0}.", "Die ersten {0} werden angezeigt.", "Vengono mostrati i primi {0}.",
            "Se muestran los primeros {0}.", "Les {0} premiers sont affichés.", "Os primeiros {0} são exibidos.", "يتم عرض أول {0}.",
            "पहले {0} दिखाए गए हैं.", "প্রথম {0}টি দেখানো হয়েছে।", "پہلے {0} دکھائے گئے ہیں۔", "已显示前 {0} 项。")
        Add("Показывает список без каких-либо изменений. Запуск не требует просмотра.",
            "Shows a list without changing anything. A run does not need a preview.", "Показує список без жодних змін. Запуск не потребує попереднього перегляду.", "Zeigt eine Liste an, ohne etwas zu ändern. Eine Ausführung erfordert keine Vorschau.", "Mostra un elenco senza modificare nulla. Un'esecuzione non richiede l'anteprima.",
            "Muestra una lista sin cambiar nada. Una ejecución no requiere vista previa.", "Affiche une liste sans rien modifier. Une exécution n'a pas besoin d'un aperçu.", "Mostra uma lista sem alterar nada. Uma execução não precisa de uma visualização prévia.", "يعرض قائمة دون تغيير أي شيء. لا يحتاج التشغيل إلى معاينة.",
            "बिना कोई बदलाव किए सूची दिखाता है. रन के लिए पूर्वावलोकन आवश्यक नहीं है.", "কিছু পরিবর্তন না করে একটি তালিকা দেখায়। রান করার জন্য প্রিভিউ লাগে না।", "کوئی تبدیلی کیے بغیر فہرست دکھاتا ہے۔ رن کے لیے پیش نظارہ ضروری نہیں۔", "只显示列表，不做任何更改。运行前不需要预览。")
        Add("Последний запуск",
            "Last run", "Останній запуск", "Letzte Ausführung", "Ultima esecuzione",
            "Última ejecución", "Dernière exécution", "Última execução", "آخر تشغيل",
            "पिछला रन", "শেষ রান", "پچھلا رن", "上次运行")
        Add("Последний запуск не удался. Откройте журнал, чтобы увидеть подробности.",
            "The last run failed. Open the history for details.", "Останній запуск не вдався. Відкрийте журнал, щоб побачити подробиці.", "Die letzte Ausführung ist fehlgeschlagen. Öffnen Sie den Verlauf, um Details zu sehen.", "L'ultima esecuzione non è riuscita. Apri la cronologia per vedere i dettagli.",
            "La última ejecución falló. Abra el historial para ver los detalles.", "La dernière exécution a échoué. Ouvrez l'historique pour voir les détails.", "A última execução falhou. Abra o histórico para ver os detalhes.", "فشل آخر تشغيل. افتح السجل لعرض التفاصيل.",
            "पिछला रन विफल रहा. विवरण देखने के लिए इतिहास खोलें.", "শেষ রান ব্যর্থ হয়েছে। বিস্তারিত দেখতে ইতিহাস খুলুন।", "پچھلا رن ناکام رہا۔ تفصیلات دیکھنے کے لیے ہسٹری کھولیں۔", "上次运行失败。请打开历史记录查看详情。")
        Add("Прервана",
            "Interrupted", "Перервана", "Unterbrochen", "Interrotta",
            "Interrumpida", "Interrompue", "Interrompida", "تمت مقاطعتها",
            "बाधित", "বাধাপ্রাপ্ত", "منقطع", "已中断")
        Add("Прервано",
            "Interrupted", "Перервано", "Unterbrochen", "Interrotto",
            "Interrumpido", "Interrompu", "Interrompido", "تمت المقاطعة",
            "बाधित", "বাধাপ্রাপ্ত", "منقطع", "已中断")
        Add("Приостановить все",
            "Pause all", "Призупинити всі", "Alle pausieren", "Sospendi tutte",
            "Pausar todas", "Tout suspendre", "Pausar tudo", "إيقاف الكل مؤقتًا",
            "सभी को विराम दें", "সবগুলোতে বিরতি দিন", "سب کو موقوف کریں", "全部暂停")
        Add("Приостановить расписание",
            "Pause the schedule", "Призупинити розклад", "Zeitplan pausieren", "Sospendi la pianificazione",
            "Pausar la programación", "Suspendre la planification", "Pausar o agendamento", "إيقاف الجدول الزمني مؤقتًا",
            "शेड्यूल को विराम दें", "সময়সূচিতে বিরতি দিন", "شیڈول کو موقوف کریں", "暂停计划")
        Add("Пропустить",
            "Skip", "Пропустити", "Überspringen", "Salta",
            "Omitir", "Ignorer", "Ignorar", "تخطي",
            "छोड़ें", "বাদ দিন", "چھوڑ دیں", "跳过")
        Add("Пропущенные запуски",
            "Missed runs", "Пропущені запуски", "Verpasste Ausführungen", "Esecuzioni mancate",
            "Ejecuciones perdidas", "Exécutions manquées", "Execuções perdidas", "عمليات التشغيل الفائتة",
            "छूटे हुए रन", "বাদ পড়া রান", "چھوٹے ہوئے رن", "错过的运行")
        Add("Пропущено: время запуска ({0}) прошло, пока менеджер был закрыт",
            "Skipped: the run time ({0}) passed while the manager was closed", "Пропущено: час запуску ({0}) минув, поки менеджер був закритий", "Übersprungen: Die Ausführungszeit ({0}) ist verstrichen, während der Manager geschlossen war", "Saltato: l'ora di esecuzione ({0}) è trascorsa mentre il manager era chiuso",
            "Omitido: la hora de ejecución ({0}) pasó mientras el manager estaba cerrado", "Ignoré : l'heure d'exécution ({0}) est passée pendant que le gestionnaire était fermé", "Ignorado: o horário de execução ({0}) passou enquanto o gerenciador estava fechado", "تم التخطي: فات وقت التشغيل ({0}) أثناء إغلاق المدير",
            "छोड़ा गया: रन का समय ({0}) तब बीत गया जब प्रबंधक बंद था", "বাদ দেওয়া হয়েছে: ম্যানেজার বন্ধ থাকার সময় রানের সময় ({0}) পার হয়ে গেছে", "چھوڑ دیا گیا: رن کا وقت ({0}) اس دوران گزر گیا جب مینیجر بند تھا", "已跳过：运行时间（{0}）在管理器关闭期间已过")
        Add("Размер «меньше» должен быть больше размера «больше».",
            "The «smaller than» size must be greater than the «larger than» size.", "Розмір «менший за» має бути більшим за розмір «більший за».", "Die Größe «kleiner als» muss größer sein als die Größe «größer als».", "La dimensione «più piccolo di» deve essere maggiore della dimensione «più grande di».",
            "El tamaño «menor que» debe ser mayor que el tamaño «mayor que».", "La taille « inférieure à » doit être supérieure à la taille « supérieure à ».", "O tamanho «menor que» deve ser maior que o tamanho «maior que».", "يجب أن يكون الحجم «أصغر من» أكبر من الحجم «أكبر من».",
            "«इससे छोटा» की सीमा «इससे बड़ा» की सीमा से अधिक होनी चाहिए.", "«এর চেয়ে ছোট»-এর মান অবশ্যই «এর চেয়ে বড়»-র মানের চেয়ে বড় হতে হবে।", "«اس سے چھوٹا» کی قدر «اس سے بڑا» کی قدر سے زیادہ ہونی چاہیے۔", "「小于」的值必须大于「大于」的值。")
        Add("Размер больше",
            "Larger than", "Розмір більший за", "Größer als", "Più grande di",
            "Mayor que", "Taille supérieure à", "Tamanho maior que", "الحجم أكبر من",
            "इससे बड़ा", "এর চেয়ে বড়", "اس سے بڑا", "大于")
        Add("Размер меньше",
            "Smaller than", "Розмір менший за", "Kleiner als", "Più piccolo di",
            "Menor que", "Taille inférieure à", "Tamanho menor que", "الحجم أصغر من",
            "इससे छोटा", "এর চেয়ে ছোট", "اس سے چھوٹا", "小于")
        Add("Расписание",
            "Schedule", "Розклад", "Zeitplan", "Pianificazione",
            "Programación", "Planification", "Agendamento", "الجدول الزمني",
            "शेड्यूल", "সময়সূচি", "شیڈول", "计划")
        Add("Расписание выключено: операция выполняется только вручную.",
            "The schedule is off: the operation runs by hand only.", "Розклад вимкнено: операція виконується лише вручну.", "Der Zeitplan ist ausgeschaltet: Der Vorgang wird nur manuell ausgeführt.", "La pianificazione è disattivata: l'operazione viene eseguita solo manualmente.",
            "La programación está desactivada: la operación se ejecuta solo manualmente.", "La planification est désactivée : l'opération ne s'exécute que manuellement.", "O agendamento está desativado: a operação é executada apenas manualmente.", "الجدول الزمني معطّل: تُنفَّذ العملية يدويًا فقط.",
            "शेड्यूल बंद है: ऑपरेशन केवल मैन्युअल रूप से चलता है.", "সময়সূচি বন্ধ: অপারেশন শুধু হাতে চালানো যায়।", "شیڈول بند ہے: آپریشن صرف دستی طور پر چلتا ہے۔", "计划已关闭：该操作只能手动运行。")
        Add("Расписание приостановлено: сначала возобновите его.",
            "The schedule is paused: resume it first.", "Розклад призупинено: спершу відновіть його.", "Der Zeitplan ist pausiert: Setzen Sie ihn zuerst fort.", "La pianificazione è sospesa: riprendila prima.",
            "La programación está en pausa: reanúdela primero.", "La planification est suspendue : reprenez-la d'abord.", "O agendamento está pausado: retome-o primeiro.", "الجدول الزمني متوقف مؤقتًا: استأنفه أولًا.",
            "शेड्यूल को विराम दिया गया है: पहले उसे फिर शुरू करें.", "সময়সূচিতে বিরতি দেওয়া হয়েছে: আগে এটি পুনরায় শুরু করুন।", "شیڈول موقوف ہے: پہلے اسے دوبارہ شروع کریں۔", "计划已暂停：请先恢复。")
        Add("Расписание приостановлено.",
            "The schedule is paused.", "Розклад призупинено.", "Der Zeitplan ist pausiert.", "La pianificazione è sospesa.",
            "La programación está en pausa.", "La planification est suspendue.", "O agendamento está pausado.", "الجدول الزمني متوقف مؤقتًا.",
            "शेड्यूल को विराम दिया गया है.", "সময়সূচিতে বিরতি দেওয়া হয়েছে।", "شیڈول موقوف ہے۔", "计划已暂停。")
        Add("Результат",
            "Result", "Результат", "Ergebnis", "Risultato",
            "Resultado", "Résultat", "Resultado", "النتيجة",
            "परिणाम", "ফলাফল", "نتیجہ", "结果")
        Add("Ресурс",
            "Resource", "Ресурс", "Ressource", "Risorsa",
            "Recurso", "Ressource", "Recurso", "المورد",
            "संसाधन", "রিসোর্স", "وسیلہ", "资源")
        Add("Ресурс не найден в списке ресурсов.",
            "The resource is not in the resource list.", "Ресурс не знайдено в списку ресурсів.", "Die Ressource ist nicht in der Ressourcenliste vorhanden.", "La risorsa non è presente nell'elenco delle risorse.",
            "El recurso no está en la lista de recursos.", "La ressource est introuvable dans la liste des ressources.", "O recurso não foi encontrado na lista de recursos.", "لم يتم العثور على المورد في قائمة الموارد.",
            "संसाधन, संसाधन सूची में नहीं मिला.", "রিসোর্সটি রিসোর্স তালিকায় পাওয়া যায়নি।", "وسیلہ وسائل کی فہرست میں نہیں ملا۔", "在资源列表中找不到该资源。")
        Add("Ресурс только для чтения: перенос и удаление в нём недоступны.",
            "The resource is read-only: moving and deleting in it are not available.", "Ресурс лише для читання: переміщення та видалення в ньому недоступні.", "Die Ressource ist schreibgeschützt: Verschieben und Löschen sind darin nicht verfügbar.", "La risorsa è di sola lettura: spostamento ed eliminazione non sono disponibili.",
            "El recurso es de solo lectura: mover y eliminar no están disponibles en él.", "La ressource est en lecture seule : le déplacement et la suppression n'y sont pas disponibles.", "O recurso é somente leitura: mover e excluir nele não estão disponíveis.", "المورد للقراءة فقط: النقل والحذف غير متاحين فيه.",
            "संसाधन केवल पढ़ने के लिए है: इसमें स्थानांतरण और हटाना उपलब्ध नहीं है.", "রিসোর্সটি শুধু পড়ার জন্য: এতে সরানো ও মুছে ফেলা যাবে না।", "وسیلہ صرف پڑھنے کے لیے ہے: اس میں منتقلی اور حذف دستیاب نہیں۔", "该资源为只读：无法在其中移动和删除。")
        Add("ресурс удалён из списка ресурсов: {0}",
            "the resource was removed from the resource list: {0}", "ресурс видалено зі списку ресурсів: {0}", "die Ressource wurde aus der Ressourcenliste entfernt: {0}", "la risorsa è stata rimossa dall'elenco delle risorse: {0}",
            "el recurso se eliminó de la lista de recursos: {0}", "la ressource a été supprimée de la liste des ressources : {0}", "o recurso foi removido da lista de recursos: {0}", "تمت إزالة المورد من قائمة الموارد: {0}",
            "संसाधन को संसाधन सूची से हटा दिया गया: {0}", "রিসোর্সটি রিসোর্স তালিকা থেকে সরিয়ে ফেলা হয়েছে: {0}", "وسیلہ وسائل کی فہرست سے ہٹا دیا گیا: {0}", "资源已从资源列表中移除：{0}")
        Add("Ресурс..",
            "Resource..", "Ресурс..", "Ressource..", "Risorsa..",
            "Recurso..", "Ressource..", "Recurso..", "المورد..",
            "संसाधन..", "রিসোর্স..", "وسیلہ..", "资源..")
        Add("С опозданием",
            "Late", "Із запізненням", "Verspätet", "In ritardo",
            "Con retraso", "En retard", "Com atraso", "متأخرة",
            "देरी से", "দেরিতে", "تاخیر سے", "延迟")
        Add("С ошибками: {0}; выполнено: {1} файл(ов)",
            "With errors: {0}; completed: {1} file(s)", "З помилками: {0}; виконано: {1} файл(ів)", "Mit Fehlern: {0}; abgeschlossen: {1} Datei(en)", "Con errori: {0}; completati: {1} file",
            "Con errores: {0}; completado: {1} archivo(s)", "Avec des erreurs : {0} ; terminé : {1} fichier(s)", "Com erros: {0}; concluído: {1} arquivo(s)", "مع أخطاء: {0}؛ اكتمل: {1} من الملفات",
            "त्रुटियों के साथ: {0}; पूरा हुआ: {1} फ़ाइल(ें)", "ত্রুটিসহ: {0}; সম্পন্ন: {1}টি ফাইল", "غلطیوں کے ساتھ: {0}؛ مکمل: {1} فائل(یں)", "有错误：{0}；已完成：{1} 个文件")
        Add("связь с папкой прервалась: {0}",
            "the connection to the folder was lost: {0}", "зв'язок із папкою перервано: {0}", "die Verbindung zum Ordner wurde unterbrochen: {0}", "la connessione alla cartella è stata interrotta: {0}",
            "se perdió la conexión con la carpeta: {0}", "la connexion au dossier a été perdue : {0}", "a conexão com a pasta foi perdida: {0}", "انقطع الاتصال بالمجلد: {0}",
            "फ़ोल्डर से कनेक्शन टूट गया: {0}", "ফোল্ডারের সঙ্গে সংযোগ বিচ্ছিন্ন হয়েছে: {0}", "فولڈر سے کنکشن ٹوٹ گیا: {0}", "与文件夹的连接已中断：{0}")
        Add("Системные и программные папки нельзя использовать как источник: {0}",
            "System and program folders cannot be used as a source: {0}", "Системні папки та папки програм не можна використовувати як джерело: {0}", "Systemordner und Programmordner können nicht als Quelle verwendet werden: {0}", "Le cartelle di sistema e dei programmi non possono essere usate come origine: {0}",
            "Las carpetas del sistema y de programas no se pueden usar como origen: {0}", "Les dossiers système et de programmes ne peuvent pas servir de source : {0}", "Pastas do sistema e de programas não podem ser usadas como origem: {0}", "لا يمكن استخدام مجلدات النظام والبرامج كمصدر: {0}",
            "सिस्टम और प्रोग्राम फ़ोल्डर स्रोत के रूप में उपयोग नहीं किए जा सकते: {0}", "সিস্টেম ও প্রোগ্রাম ফোল্ডার উৎস হিসেবে ব্যবহার করা যায় না: {0}", "سسٹم اور پروگرام فولڈر ماخذ کے طور پر استعمال نہیں کیے جا سکتے: {0}", "系统文件夹和程序文件夹不能用作来源：{0}")
        Add("Системные и программные папки нельзя использовать как место назначения: {0}",
            "System and program folders cannot be used as a destination: {0}", "Системні папки та папки програм не можна використовувати як місце призначення: {0}", "Systemordner und Programmordner können nicht als Ziel verwendet werden: {0}", "Le cartelle di sistema e dei programmi non possono essere usate come destinazione: {0}",
            "Las carpetas del sistema y de programas no se pueden usar como destino: {0}", "Les dossiers système et de programmes ne peuvent pas servir de destination : {0}", "Pastas do sistema e de programas não podem ser usadas como destino: {0}", "لا يمكن استخدام مجلدات النظام والبرامج كوجهة: {0}",
            "सिस्टम और प्रोग्राम फ़ोल्डर गंतव्य के रूप में उपयोग नहीं किए जा सकते: {0}", "সিস্টেম ও প্রোগ্রাম ফোল্ডার গন্তব্য হিসেবে ব্যবহার করা যায় না: {0}", "سسٹم اور پروگرام فولڈر منزل کے طور پر استعمال نہیں کیے جا سکتے: {0}", "系统文件夹和程序文件夹不能用作目标：{0}")
        Add("скопирован, но исходный файл остался на месте: {0}",
            "copied, but the original file stayed in place: {0}", "скопійовано, але вихідний файл залишився на місці: {0}", "kopiert, aber die Originaldatei ist an ihrem Platz geblieben: {0}", "copiato, ma il file originale è rimasto al suo posto: {0}",
            "copiado, pero el archivo original se quedó en su sitio: {0}", "copié, mais le fichier d'origine est resté en place : {0}", "copiado, mas o arquivo original permaneceu no lugar: {0}", "تم النسخ لكن الملف الأصلي بقي في مكانه: {0}",
            "कॉपी हो गई, लेकिन मूल फ़ाइल अपनी जगह पर रही: {0}", "কপি হয়েছে, কিন্তু মূল ফাইলটি যথাস্থানে রয়ে গেছে: {0}", "کاپی ہو گئی، لیکن اصل فائل اپنی جگہ پر رہی: {0}", "已复制，但原文件仍保留在原处：{0}")
        Add("Следующий запуск",
            "Next run", "Наступний запуск", "Nächste Ausführung", "Prossima esecuzione",
            "Próxima ejecución", "Prochaine exécution", "Próxima execução", "التشغيل التالي",
            "अगला रन", "পরবর্তী রান", "اگلا رن", "下次运行")
        Add("Следующий запуск: {0} ({1})",
            "Next run: {0} ({1})", "Наступний запуск: {0} ({1})", "Nächste Ausführung: {0} ({1})", "Prossima esecuzione: {0} ({1})",
            "Próxima ejecución: {0} ({1})", "Prochaine exécution : {0} ({1})", "Próxima execução: {0} ({1})", "التشغيل التالي: {0} ({1})",
            "अगला रन: {0} ({1})", "পরবর্তী রান: {0} ({1})", "اگلا رن: {0} ({1})", "下次运行：{0}（{1}）")
        Add("Создать..",
            "New..", "Створити..", "Neu..", "Nuova..",
            "Nueva..", "Nouvelle..", "Nova..", "جديدة..",
            "नया..", "নতুন..", "نیا..", "新建..")
        Add("Сохранить",
            "Save", "Зберегти", "Speichern", "Salva",
            "Guardar", "Enregistrer", "Salvar", "حفظ",
            "सहेजें", "সংরক্ষণ করুন", "محفوظ کریں", "保存")
        Add("Старше",
            "Older than", "Старіші за", "Älter als", "Più vecchio di",
            "Más antiguo que", "Plus ancien que", "Mais antigo que", "أقدم من",
            "इससे पुरानी", "এর চেয়ে পুরনো", "اس سے پرانی", "超过")
        Add("Типы файлов",
            "File types", "Типи файлів", "Dateitypen", "Tipi di file",
            "Tipos de archivo", "Types de fichiers", "Tipos de arquivo", "أنواع الملفات",
            "फ़ाइल प्रकार", "ফাইলের ধরন", "فائل کی اقسام", "文件类型")
        Add("Удаление",
            "Delete", "Видалення", "Löschen", "Elimina",
            "Eliminar", "Supprimer", "Excluir", "حذف",
            "हटाना", "মুছে ফেলা", "حذف کرنا", "删除")
        Add("Удаление: {0}",
            "Delete: {0}", "Видалення: {0}", "Löschen: {0}", "Elimina: {0}",
            "Eliminar: {0}", "Supprimer : {0}", "Excluir: {0}", "حذف: {0}",
            "हटाना: {0}", "মুছে ফেলা: {0}", "حذف کرنا: {0}", "删除：{0}")
        Add("Удаление: куда пойдут файлы, станет известно, когда будет указан источник.",
            "Delete: where the files go will be known once a source is given.", "Видалення: куди підуть файли, стане відомо, коли буде вказано джерело.", "Löschen: Wohin die Dateien gehen, steht fest, sobald eine Quelle angegeben ist.", "Elimina: la destinazione dei file sarà nota quando verrà indicata un'origine.",
            "Eliminar: se sabrá adónde irán los archivos cuando se indique un origen.", "Supprimer : la destination des fichiers sera connue une fois la source indiquée.", "Excluir: para onde os arquivos vão será conhecido quando a origem for indicada.", "حذف: ستُعرف وجهة الملفات عند تحديد المصدر.",
            "हटाना: फ़ाइलें कहाँ जाएँगी, यह स्रोत बताए जाने पर पता चलेगा.", "মুছে ফেলা: ফাইলগুলো কোথায় যাবে, উৎস নির্দিষ্ট করার পর জানা যাবে।", "حذف کرنا: فائلیں کہاں جائیں گی، یہ ماخذ بتانے کے بعد معلوم ہو گا۔", "删除：指定来源后即可知道文件的去向。")
        Add("Удалить",
            "Delete", "Видалити", "Löschen", "Elimina",
            "Eliminar", "Supprimer", "Excluir", "حذف",
            "हटाएँ", "মুছুন", "حذف کریں", "删除")
        Add("Удалить исходные файлы после проверки архива",
            "Delete the original files after the archive is verified", "Видалити вихідні файли після перевірки архіву", "Originaldateien nach Prüfung des Archivs löschen", "Elimina i file originali dopo la verifica dell'archivio",
            "Eliminar los archivos originales tras verificar el archivo comprimido", "Supprimer les fichiers d'origine après vérification de l'archive", "Excluir os arquivos originais após a verificação do arquivo compactado", "حذف الملفات الأصلية بعد التحقق من الأرشيف",
            "आर्काइव की जाँच के बाद मूल फ़ाइलें हटाएँ", "আর্কাইভ যাচাইয়ের পর মূল ফাইলগুলো মুছুন", "آرکائیو کی جانچ کے بعد اصل فائلیں حذف کریں", "归档文件校验通过后删除原文件")
        Add("Удалить операцию «{0}»? Файлы она не затрагивает.",
            "Delete the operation «{0}»? It does not touch any files.", "Видалити операцію «{0}»? Файлів вона не зачіпає.", "Den Vorgang «{0}» löschen? Dateien sind davon nicht betroffen.", "Eliminare l'operazione «{0}»? I file non vengono toccati.",
            "¿Eliminar la operación «{0}»? No afecta a ningún archivo.", "Supprimer l'opération « {0} » ? Elle ne touche à aucun fichier.", "Excluir a operação «{0}»? Ela não afeta nenhum arquivo.", "هل تريد حذف العملية «{0}»؟ إنها لا تؤثر في أي ملفات.",
            "ऑपरेशन «{0}» हटाएँ? यह किसी फ़ाइल को नहीं छूता.", "«{0}» অপারেশন মুছে ফেলবেন? এটি কোনো ফাইলে হাত দেয় না।", "آپریشن «{0}» حذف کریں؟ یہ کسی فائل کو نہیں چھوتا۔", "要删除操作「{0}」吗？这不会影响任何文件。")
        Add("Укажите папку назначения.",
            "Specify the destination folder.", "Вкажіть папку призначення.", "Geben Sie den Zielordner an.", "Specifica la cartella di destinazione.",
            "Especifique la carpeta de destino.", "Indiquez le dossier de destination.", "Especifique a pasta de destino.", "حدد مجلد الوجهة.",
            "गंतव्य फ़ोल्डर बताएँ.", "গন্তব্য ফোল্ডার নির্দিষ্ট করুন।", "منزل فولڈر بتائیں۔", "请指定目标文件夹。")
        Add("Укажите папку-источник.",
            "Specify the source folder.", "Вкажіть папку-джерело.", "Geben Sie den Quellordner an.", "Specifica la cartella di origine.",
            "Especifique la carpeta de origen.", "Indiquez le dossier source.", "Especifique a pasta de origem.", "حدد المجلد المصدر.",
            "स्रोत फ़ोल्डर बताएँ.", "উৎস ফোল্ডার নির্দিষ্ট করুন।", "ماخذ فولڈر بتائیں۔", "请指定来源文件夹。")
        Add("Условия отбора файлов",
            "File selection conditions", "Умови відбору файлів", "Bedingungen für die Dateiauswahl", "Condizioni di selezione dei file",
            "Condiciones de selección de archivos", "Conditions de sélection des fichiers", "Condições de seleção de arquivos", "شروط اختيار الملفات",
            "फ़ाइल चयन की शर्तें", "ফাইল নির্বাচনের শর্ত", "فائل کے انتخاب کی شرائط", "文件筛选条件")
        Add("файл больше, чем вмещает Корзина",
            "the file is larger than the Recycle Bin can hold", "файл більший, ніж вміщує Кошик", "die Datei ist größer, als der Papierkorb aufnehmen kann", "il file è più grande di quanto il Cestino possa contenere",
            "el archivo es más grande de lo que cabe en la Papelera de reciclaje", "le fichier est plus grand que ce que la Corbeille peut contenir", "o arquivo é maior do que a Lixeira comporta", "الملف أكبر مما تتسع له سلة المحذوفات",
            "फ़ाइल रीसायकल बिन की क्षमता से बड़ी है", "ফাইলটি রিসাইকেল বিনের ধারণক্ষমতার চেয়ে বড়", "فائل ری سائیکل بن کی گنجائش سے بڑی ہے", "文件大于回收站的容量")
        Add("файл занят другой программой: {0}",
            "the file is in use by another program: {0}", "файл зайнятий іншою програмою: {0}", "die Datei wird von einem anderen Programm verwendet: {0}", "il file è in uso da parte di un altro programma: {0}",
            "el archivo está en uso por otro programa: {0}", "le fichier est utilisé par un autre programme : {0}", "o arquivo está em uso por outro programa: {0}", "الملف مستخدم من قبل برنامج آخر: {0}",
            "फ़ाइल किसी अन्य प्रोग्राम द्वारा उपयोग में है: {0}", "ফাইলটি অন্য একটি প্রোগ্রাম ব্যবহার করছে: {0}", "فائل کسی دوسرے پروگرام کے استعمال میں ہے: {0}", "文件正被其他程序占用：{0}")
        Add("файл изменился, пока шла работа: {0}",
            "the file changed while the work was in progress: {0}", "файл змінився, поки йшла робота: {0}", "die Datei hat sich geändert, während die Verarbeitung lief: {0}", "il file è cambiato mentre l'elaborazione era in corso: {0}",
            "el archivo cambió mientras el proceso estaba en curso: {0}", "le fichier a changé pendant le traitement : {0}", "o arquivo mudou durante o processamento: {0}", "تغيّر الملف أثناء العمل: {0}",
            "काम के दौरान फ़ाइल बदल गई: {0}", "কাজ চলাকালীন ফাইলটি পরিবর্তিত হয়েছে: {0}", "کام کے دوران فائل تبدیل ہو گئی: {0}", "处理过程中文件已更改：{0}")
        Add("файл не найден: {0}",
            "the file was not found: {0}", "файл не знайдено: {0}", "die Datei wurde nicht gefunden: {0}", "il file non è stato trovato: {0}",
            "no se encontró el archivo: {0}", "fichier introuvable : {0}", "arquivo não encontrado: {0}", "لم يتم العثور على الملف: {0}",
            "फ़ाइल नहीं मिली: {0}", "ফাইল পাওয়া যায়নি: {0}", "فائل نہیں ملی: {0}", "未找到文件：{0}")
        Add("Файловые операции по расписанию",
            "Scheduled file operations", "Файлові операції за розкладом", "Geplante Dateivorgänge", "Operazioni sui file pianificate",
            "Operaciones de archivos programadas", "Opérations sur fichiers planifiées", "Operações de arquivo agendadas", "عمليات الملفات المجدولة",
            "शेड्यूल किए गए फ़ाइल ऑपरेशन", "সময়সূচিবদ্ধ ফাইল অপারেশন", "شیڈول شدہ فائل آپریشنز", "计划文件操作")
        Add("Файловые операции по расписанию - часть менеджера общего доступа: устанавливаются вместе с ним, выполняются им и настраиваются в его настройках. Расписание работает только пока менеджер запущен.",
            "Scheduled file operations are part of the Share Manager: they are installed with it, run by it and managed from its settings. The schedule works only while the manager is running.", "Файлові операції за розкладом - частина менеджера спільного доступу: вони встановлюються разом із ним, виконуються ним і налаштовуються в його налаштуваннях. Розклад працює лише поки менеджер запущено.", "Geplante Dateivorgänge sind Teil von Share Manager: Sie werden zusammen mit ihm installiert, von ihm ausgeführt und in seinen Einstellungen verwaltet. Der Zeitplan funktioniert nur, solange der Manager läuft.", "Le operazioni sui file pianificate fanno parte di Share Manager: vengono installate insieme ad esso, eseguite da esso e gestite dalle sue impostazioni. La pianificazione funziona solo finché il manager è in esecuzione.",
            "Las operaciones de archivos programadas forman parte de Share Manager: se instalan junto con él, él las ejecuta y se gestionan desde su configuración. La programación solo funciona mientras el manager está en ejecución.", "Les opérations sur fichiers planifiées font partie du gestionnaire de partage : elles sont installées avec lui, exécutées par lui et configurées dans ses paramètres. La planification ne fonctionne que tant que le gestionnaire est en cours d'exécution.", "As operações de arquivo agendadas fazem parte do Share Manager: são instaladas junto com ele, executadas por ele e gerenciadas a partir de suas configurações. O agendamento funciona apenas enquanto o gerenciador estiver em execução.", "عمليات الملفات المجدولة جزء من مدير المشاركة: تُثبَّت معه وتُنفَّذ بواسطته وتُضبَط من إعداداته. يعمل الجدول الزمني فقط ما دام المدير قيد التشغيل.",
            "शेड्यूल किए गए फ़ाइल ऑपरेशन शेयर प्रबंधक का हिस्सा हैं - वे उसके साथ इंस्टॉल होते हैं, उसी के द्वारा चलाए जाते हैं और उसकी सेटिंग्स में प्रबंधित किए जाते हैं. शेड्यूल केवल तभी काम करता है जब प्रबंधक चल रहा हो.", "সময়সূচিবদ্ধ ফাইল অপারেশন শেয়ার ম্যানেজারের অংশ - এগুলো এর সঙ্গেই ইনস্টল হয়, এর মাধ্যমেই চলে এবং এর সেটিংয়ে পরিচালনা করা হয়। সময়সূচি শুধু ম্যানেজার চালু থাকলেই কাজ করে।", "شیڈول شدہ فائل آپریشنز شیئر مینیجر کا حصہ ہیں - یہ اس کے ساتھ انسٹال ہوتے ہیں، اسی کے ذریعے چلتے ہیں اور اسی کی ترتیبات میں منظم کیے جاتے ہیں۔ شیڈول صرف اسی وقت کام کرتا ہے جب مینیجر چل رہا ہو۔", "计划文件操作是共享管理器的一部分 - 随它一同安装，由它运行，并在它的设置中管理。只有在管理器运行时，计划才会生效。")
        Add("Файловые операции по расписанию..",
            "Scheduled file operations..", "Файлові операції за розкладом..", "Geplante Dateivorgänge..", "Operazioni sui file pianificate..",
            "Operaciones de archivos programadas..", "Opérations sur fichiers planifiées..", "Operações de arquivo agendadas..", "عمليات الملفات المجدولة..",
            "शेड्यूल किए गए फ़ाइल ऑपरेशन..", "সময়সূচিবদ্ধ ফাইল অপারেশন..", "شیڈول شدہ فائل آپریشنز..", "计划文件操作..")
        Add("Файлы, подходящие под эти маски, не обрабатываются.",
            "Files matching these masks are not processed.", "Файли, що відповідають цим маскам, не обробляються.", "Dateien, die diesen Masken entsprechen, werden nicht verarbeitet.", "I file che corrispondono a queste maschere non vengono elaborati.",
            "Los archivos que coinciden con estas máscaras no se procesan.", "Les fichiers correspondant à ces masques ne sont pas traités.", "Os arquivos que correspondem a estas máscaras não são processados.", "لا تتم معالجة الملفات المطابقة لهذه الأنماط.",
            "इन मास्क से मेल खाने वाली फ़ाइलें संसाधित नहीं की जातीं.", "এই মাস্কগুলোর সঙ্গে মেলে এমন ফাইল প্রক্রিয়া করা হয় না।", "ان ماسکس سے مطابقت رکھنے والی فائلیں پروسیس نہیں کی جاتیں۔", "匹配这些通配符的文件不会被处理。")
        Add("ч",
            "h", "год", "h", "h",
            "h", "h", "h", "ساعة",
            "घंटा", "ঘণ্টা", "گھنٹہ", "小时")
        Add("частично в Корзину, частично безвозвратно",
            "partly to the Recycle Bin, partly permanently", "частково до Кошика, частково безповоротно", "teilweise in den Papierkorb, teilweise endgültig", "in parte nel Cestino, in parte in modo permanente",
            "en parte a la Papelera de reciclaje, en parte de forma permanente", "en partie dans la Corbeille, en partie définitivement", "em parte para a Lixeira, em parte permanentemente", "جزئيًا إلى سلة المحذوفات وجزئيًا نهائيًا",
            "कुछ रीसायकल बिन में, कुछ स्थायी रूप से", "আংশিক রিসাইকেল বিনে, আংশিক স্থায়ীভাবে", "کچھ ری سائیکل بن میں، کچھ مستقل طور پر", "部分放入回收站，部分永久删除")
        Add("Что будет выполнено сейчас",
            "What would run now", "Що буде виконано зараз", "Was jetzt ausgeführt würde", "Cosa verrebbe eseguito ora",
            "Qué se ejecutaría ahora", "Ce qui s'exécuterait maintenant", "O que seria executado agora", "ما الذي سيُنفَّذ الآن",
            "अभी क्या चलेगा", "এখন কী চলবে", "ابھی کیا چلے گا", "现在将执行什么")
        Add("Что будет выполнено сейчас..",
            "What would run now..", "Що буде виконано зараз..", "Was jetzt ausgeführt würde..", "Cosa verrebbe eseguito ora..",
            "Qué se ejecutaría ahora..", "Ce qui s'exécuterait maintenant..", "O que seria executado agora..", "ما الذي سيُنفَّذ الآن..",
            "अभी क्या चलेगा..", "এখন কী চলবে..", "ابھی کیا چلے گا..", "现在将执行什么..")
        Add("Что делать с запуском, время которого прошло, пока менеджер был закрыт: выполнить его один раз при следующем запуске или пропустить.",
            "What to do with a run whose time passed while the manager was closed: run it once at the next start, or skip it.", "Що робити із запуском, час якого минув, поки менеджер був закритий: виконати його один раз під час наступного запуску чи пропустити.", "Was mit einer Ausführung geschehen soll, deren Zeitpunkt verstrichen ist, während der Manager geschlossen war: einmal beim nächsten Start ausführen oder überspringen.", "Cosa fare con un'esecuzione la cui ora è trascorsa mentre il manager era chiuso: eseguirla una volta al prossimo avvio oppure saltarla.",
            "Qué hacer con una ejecución cuya hora pasó mientras el manager estaba cerrado: ejecutarla una vez en el próximo inicio u omitirla.", "Que faire d'une exécution dont l'heure est passée pendant que le gestionnaire était fermé : l'exécuter une fois au prochain démarrage ou l'ignorer.", "O que fazer com uma execução cujo horário passou enquanto o gerenciador estava fechado: executá-la uma vez na próxima inicialização ou ignorá-la.", "ما يجب فعله بتشغيل فات وقته أثناء إغلاق المدير: تنفيذه مرة واحدة عند البدء التالي أو تخطيه.",
            "उस रन का क्या करना है जिसका समय प्रबंधक के बंद रहने के दौरान बीत गया: अगली बार शुरू होने पर उसे एक बार चलाएँ या छोड़ दें.", "ম্যানেজার বন্ধ থাকার সময় যে রানের সময় পার হয়ে গেছে, তার কী করা হবে: পরের বার চালু হলে একবার চালান, অথবা বাদ দিন।", "اس رن کا کیا کرنا ہے جس کا وقت مینیجر کے بند رہنے کے دوران گزر گیا: اگلی بار شروع ہونے پر اسے ایک بار چلائیں یا چھوڑ دیں۔", "对于运行时间在管理器关闭期间已过的那次运行如何处理：下次启动时执行一次，或跳过。")
        Add("Что делать, если в папке назначения уже есть файл с таким именем.",
            "What to do when the destination folder already has a file with that name.", "Що робити, якщо в папці призначення вже є файл з таким іменем.", "Was geschehen soll, wenn der Zielordner bereits eine Datei mit diesem Namen enthält.", "Cosa fare se la cartella di destinazione contiene già un file con lo stesso nome.",
            "Qué hacer si la carpeta de destino ya contiene un archivo con ese nombre.", "Que faire si le dossier de destination contient déjà un fichier portant ce nom.", "O que fazer quando a pasta de destino já tem um arquivo com esse nome.", "ما يجب فعله إذا كان مجلد الوجهة يحتوي بالفعل على ملف بهذا الاسم.",
            "गंतव्य फ़ोल्डर में उसी नाम की फ़ाइल पहले से हो तो क्या करना है.", "গন্তব্য ফোল্ডারে একই নামের ফাইল আগে থেকেই থাকলে কী করা হবে।", "اگر منزل فولڈر میں اسی نام کی فائل پہلے سے موجود ہو تو کیا کرنا ہے۔", "目标文件夹中已有同名文件时如何处理。")
        Add("Эта операция уже выполняется или ждёт очереди.",
            "This operation is already running or waiting in the queue.", "Ця операція вже виконується або чекає черги.", "Dieser Vorgang wird bereits ausgeführt oder wartet in der Warteschlange.", "Questa operazione è già in esecuzione o in coda.",
            "Esta operación ya se está ejecutando o está en cola.", "Cette opération est déjà en cours d'exécution ou en attente.", "Esta operação já está em execução ou aguardando a vez.", "هذه العملية قيد التنفيذ بالفعل أو في انتظار دورها.",
            "यह ऑपरेशन पहले से चल रहा है या कतार में प्रतीक्षा कर रहा है.", "এই অপারেশন ইতিমধ্যে চলছে বা সারিতে অপেক্ষা করছে।", "یہ آپریشن پہلے سے چل رہا ہے یا قطار میں انتظار کر رہا ہے۔", "此操作已在运行或正在排队等待。")
        Add("это расположение не поддерживает Корзину",
            "this location does not support the Recycle Bin", "це розташування не підтримує Кошик", "dieser Speicherort unterstützt den Papierkorb nicht", "questa posizione non supporta il Cestino",
            "esta ubicación no admite la Papelera de reciclaje", "cet emplacement ne prend pas en charge la Corbeille", "este local não oferece suporte à Lixeira", "هذا الموقع لا يدعم سلة المحذوفات",
            "यह स्थान रीसायकल बिन को सपोर्ट नहीं करता", "এই অবস্থান রিসাইকেল বিন সমর্থন করে না", "یہ مقام ری سائیکل بن کو سپورٹ نہیں کرتا", "此位置不支持回收站")
        Add("Этот вид ресурса (ссылка обмена) нельзя использовать в операциях по расписанию.",
            "This kind of resource (an exchange link) cannot be used in scheduled operations.", "Цей вид ресурсу (посилання обміну) не можна використовувати в операціях за розкладом.", "Diese Art von Ressource (ein Austauschlink) kann in geplanten Vorgängen nicht verwendet werden.", "Questo tipo di risorsa (un link di scambio) non può essere usato nelle operazioni pianificate.",
            "Este tipo de recurso (un enlace de intercambio) no se puede usar en operaciones programadas.", "Ce type de ressource (un lien d'échange) ne peut pas être utilisé dans les opérations planifiées.", "Este tipo de recurso (um link de troca) não pode ser usado em operações agendadas.", "لا يمكن استخدام هذا النوع من الموارد (رابط تبادل) في العمليات المجدولة.",
            "इस प्रकार के संसाधन (एक्सचेंज लिंक) का उपयोग शेड्यूल किए गए ऑपरेशन में नहीं किया जा सकता.", "এই ধরনের রিসোর্স (এক্সচেঞ্জ লিংক) সময়সূচিবদ্ধ অপারেশনে ব্যবহার করা যায় না।", "اس قسم کے وسیلے (ایکسچینج لنک) کو شیڈول شدہ آپریشنز میں استعمال نہیں کیا جا سکتا۔", "此类资源（交换链接）不能用于计划操作。")
        Add("устройство отозвано: {0}",
            "the device has been revoked: {0}", "пристрій відкликано: {0}", "das Gerät wurde widerrufen: {0}", "il dispositivo è stato revocato: {0}",
            "el dispositivo ha sido revocado: {0}", "l'appareil a été révoqué : {0}", "o dispositivo foi revogado: {0}", "تم إلغاء تفويض الجهاز: {0}",
            "डिवाइस निरस्त कर दिया गया है: {0}", "ডিভাইসটি প্রত্যাহার করা হয়েছে: {0}", "ڈیوائس منسوخ کر دی گئی ہے: {0}", "设备已被撤销：{0}")
        Add("сертификат сервера обмена изменился: {0}",
            "the exchange server certificate has changed: {0}", "сертифікат сервера обміну змінився: {0}", "das Zertifikat des Austauschservers hat sich geändert: {0}", "il certificato del server di scambio è cambiato: {0}",
            "el certificado del servidor de intercambio ha cambiado: {0}", "le certificat du serveur d'échange a changé : {0}", "o certificado do servidor de troca mudou: {0}", "تغيرت شهادة خادم التبادل: {0}",
            "एक्सचेंज सर्वर का प्रमाणपत्र बदल गया है: {0}", "এক্সচেঞ্জ সার্ভারের সার্টিফিকেট পরিবর্তিত হয়েছে: {0}", "ایکسچینج سرور کا سرٹیفکیٹ تبدیل ہو گیا ہے: {0}", "交换服务器证书已更改：{0}")
        Add("выполнен выход из учётной записи обмена: {0}",
            "signed out of the exchange account: {0}", "виконано вихід з облікового запису обміну: {0}", "vom Austauschkonto abgemeldet: {0}", "disconnesso dall'account di scambio: {0}",
            "se cerró la sesión de la cuenta de intercambio: {0}", "déconnecté du compte d'échange : {0}", "saiu da conta de troca: {0}", "تم تسجيل الخروج من حساب التبادل: {0}",
            "एक्सचेंज खाते से साइन आउट किया गया: {0}", "এক্সচেঞ্জ অ্যাকাউন্ট থেকে সাইন আউট করা হয়েছে: {0}", "ایکسچینج اکاؤنٹ سے سائن آؤٹ ہو گیا: {0}", "已退出交换账户：{0}")
        Add("требуется PIN-код или он изменился: {0}",
            "a PIN is required or has changed: {0}", "потрібен PIN-код або він змінився: {0}", "eine PIN ist erforderlich oder hat sich geändert: {0}", "è richiesto un PIN oppure è cambiato: {0}",
            "se requiere un PIN o ha cambiado: {0}", "un code PIN est requis ou a changé : {0}", "um PIN é necessário ou foi alterado: {0}", "رمز PIN مطلوب أو تم تغييره: {0}",
            "PIN आवश्यक है या बदल गया है: {0}", "PIN প্রয়োজন বা পরিবর্তিত হয়েছে: {0}", "PIN درکار ہے یا تبدیل ہو گیا ہے: {0}", "需要 PIN 码或 PIN 码已更改：{0}")
        Add("устройство сейчас не в сети: {0}",
            "the device is offline right now: {0}", "пристрій зараз не в мережі: {0}", "das Gerät ist derzeit offline: {0}", "il dispositivo è attualmente offline: {0}",
            "el dispositivo está desconectado ahora mismo: {0}", "l'appareil est hors ligne pour le moment : {0}", "o dispositivo está offline no momento: {0}", "الجهاز غير متصل حاليًا: {0}",
            "डिवाइस अभी ऑफ़लाइन है: {0}", "ডিভাইসটি এখন অফলাইনে আছে: {0}", "ڈیوائس اس وقت آف لائن ہے: {0}", "设备当前不在线：{0}")
        Add("Google Drive нельзя выбрать одновременно как источник и как назначение.",
            "Google Drive cannot be chosen as both the source and the destination.", "Google Drive не можна вибрати одночасно як джерело і як призначення.", "Google Drive kann nicht gleichzeitig als Quelle und als Ziel ausgewählt werden.", "Google Drive non può essere scelto contemporaneamente come origine e come destinazione.",
            "No se puede elegir Google Drive como origen y como destino al mismo tiempo.", "Google Drive ne peut pas être choisi à la fois comme source et comme destination.", "O Google Drive não pode ser escolhido como origem e destino ao mesmo tempo.", "لا يمكن اختيار Google Drive كمصدر ووجهة في الوقت نفسه.",
            "Google Drive को एक साथ स्रोत और गंतव्य दोनों के रूप में नहीं चुना जा सकता।", "Google Drive-কে একই সঙ্গে উৎস ও গন্তব্য হিসেবে বেছে নেওয়া যাবে না।", "Google Drive کو بیک وقت ماخذ اور منزل دونوں کے طور پر منتخب نہیں کیا جا سکتا۔", "不能同时将 Google Drive 选为来源和目标。")
        Add("Удаление по расписанию через Google Drive не поддерживается.",
            "Scheduled deletion via Google Drive is not supported.", "Видалення за розкладом через Google Drive не підтримується.", "Geplantes Löschen über Google Drive wird nicht unterstützt.", "L'eliminazione pianificata tramite Google Drive non è supportata.",
            "La eliminación programada a través de Google Drive no es compatible.", "La suppression planifiée via Google Drive n'est pas prise en charge.", "A exclusão agendada pelo Google Drive não é suportada.", "الحذف المجدول عبر Google Drive غير مدعوم.",
            "Google Drive के माध्यम से शेड्यूल किया गया हटाना समर्थित नहीं है।", "Google Drive-এর মাধ্যমে সময়সূচিবদ্ধ মুছে ফেলা সমর্থিত নয়।", "Google Drive کے ذریعے شیڈول شدہ حذف معاونت یافتہ نہیں ہے۔", "不支持通过 Google Drive 按计划删除。")
        Add("Возраст файла нельзя задавать для источника через Google Drive: время файла здесь - время его получения.",
            "A file age cannot be set for a source via Google Drive: a file's time here is the time it was received.", "Вік файлу не можна задавати для джерела через Google Drive: час файлу тут - час його отримання.", "Für eine Quelle über Google Drive lässt sich kein Dateialter festlegen: Die Zeit einer Datei ist hier die Zeit ihres Empfangs.", "Non è possibile impostare l'età del file per un'origine tramite Google Drive: l'ora del file qui è quella della ricezione.",
            "No se puede fijar la antigüedad del archivo para un origen a través de Google Drive: aquí la hora del archivo es la de su recepción.", "L'âge du fichier ne peut pas être défini pour une source via Google Drive : l'heure du fichier est ici celle de sa réception.", "A idade do arquivo não pode ser definida para uma origem pelo Google Drive: aqui, a hora do arquivo é a do seu recebimento.", "لا يمكن تحديد عمر الملف لمصدر عبر Google Drive: وقت الملف هنا هو وقت استلامه.",
            "Google Drive के माध्यम से स्रोत के लिए फ़ाइल की आयु तय नहीं की जा सकती: यहाँ फ़ाइल का समय उसके प्राप्त होने का समय है।", "Google Drive-এর মাধ্যমে উৎসের জন্য ফাইলের বয়স ঠিক করা যায় না: এখানে ফাইলের সময় হলো তা পাওয়ার সময়।", "Google Drive کے ذریعے ماخذ کے لیے فائل کی عمر مقرر نہیں کی جا سکتی: یہاں فائل کا وقت اس کے موصول ہونے کا وقت ہے۔", "无法为通过 Google Drive 的来源设置文件时长：此处文件的时间是其接收时间。")
        Add("файл больше предела пакета Google Drive: {0}",
            "the file is larger than the Google Drive packet limit: {0}", "файл більший за межу пакета Google Drive: {0}", "die Datei überschreitet die Paketgrenze von Google Drive: {0}", "il file supera il limite del pacchetto di Google Drive: {0}",
            "el archivo supera el límite de paquete de Google Drive: {0}", "le fichier dépasse la limite de paquet de Google Drive : {0}", "o arquivo excede o limite de pacote do Google Drive: {0}", "الملف أكبر من حد حزمة Google Drive: {0}",
            "फ़ाइल Google Drive पैकेट की सीमा से बड़ी है: {0}", "ফাইলটি Google Drive প্যাকেটের সীমার চেয়ে বড়: {0}", "فائل Google Drive پیکٹ کی حد سے بڑی ہے: {0}", "文件超过 Google Drive 数据包上限：{0}")
        Add("выполнен выход из Google Drive: {0}",
            "signed out of Google Drive: {0}", "виконано вихід із Google Drive: {0}", "von Google Drive abgemeldet: {0}", "disconnesso da Google Drive: {0}",
            "se cerró la sesión de Google Drive: {0}", "déconnecté de Google Drive : {0}", "a sessão do Google Drive foi encerrada: {0}", "تم تسجيل الخروج من Google Drive: {0}",
            "Google Drive से साइन आउट हो गया: {0}", "Google Drive থেকে সাইন আউট হয়েছে: {0}", "Google Drive سے سائن آؤٹ ہو گیا: {0}", "已退出 Google Drive：{0}")
        Add("на Google Drive не хватило места: {0}",
            "Google Drive ran out of space: {0}", "на Google Drive забракло місця: {0}", "auf Google Drive reichte der Speicherplatz nicht aus: {0}", "spazio insufficiente su Google Drive: {0}",
            "no hubo espacio suficiente en Google Drive: {0}", "espace insuffisant sur Google Drive : {0}", "não houve espaço suficiente no Google Drive: {0}", "لم تكن هناك مساحة كافية على Google Drive: {0}",
            "Google Drive पर पर्याप्त जगह नहीं थी: {0}", "Google Drive-এ যথেষ্ট জায়গা ছিল না: {0}", "Google Drive پر کافی جگہ نہیں تھی: {0}", "Google Drive 空间不足：{0}")
        Add("нет связи с Google Drive: {0}",
            "no connection to Google Drive: {0}", "немає зв'язку з Google Drive: {0}", "keine Verbindung zu Google Drive: {0}", "nessuna connessione a Google Drive: {0}",
            "sin conexión con Google Drive: {0}", "pas de connexion à Google Drive : {0}", "sem conexão com o Google Drive: {0}", "لا يوجد اتصال بـ Google Drive: {0}",
            "Google Drive से कनेक्शन नहीं है: {0}", "Google Drive-এর সঙ্গে সংযোগ নেই: {0}", "Google Drive سے کنکشن نہیں ہے: {0}", "无法连接到 Google Drive：{0}")
        Add("Что другие устройства присылают на этот ПК",
            "What other devices send to this PC", "Що інші пристрої надсилають на цей ПК", "Was andere Geräte an diesen PC senden", "Ciò che gli altri dispositivi inviano a questo PC",
            "Lo que otros dispositivos envían a este PC", "Ce que les autres appareils envoient à ce PC", "O que outros dispositivos enviam para este PC", "ما ترسله الأجهزة الأخرى إلى هذا الكمبيوتر",
            "अन्य डिवाइस इस पीसी पर जो भेजते हैं", "অন্য ডিভাইস এই পিসিতে যা পাঠায়", "دوسرے آلات اس پی سی پر جو بھیجتے ہیں", "其他设备发送到此电脑的内容")
        Add("Любое из моих устройств",
            "Any of my devices", "Будь-який із моїх пристроїв", "Eines meiner Geräte", "Uno qualsiasi dei miei dispositivi",
            "Cualquiera de mis dispositivos", "N'importe lequel de mes appareils", "Qualquer um dos meus dispositivos", "أي من أجهزتي",
            "मेरे डिवाइसों में से कोई भी", "আমার যেকোনো ডিভাইস", "میرے آلات میں سے کوئی بھی", "我的任意设备")
        Add("Файлы приходят во временную папку этого ПК и попадают в назначение с базовыми именами, без вложенных папок.",
            "Files arrive in a temporary folder of this PC and reach the destination under their base names, without subfolders.", "Файли надходять до тимчасової папки цього ПК і потрапляють у призначення з базовими іменами, без вкладених папок.", "Dateien kommen in einem temporären Ordner dieses PCs an und gelangen mit ihren Basisnamen, ohne Unterordner, ins Ziel.", "I file arrivano in una cartella temporanea di questo PC e raggiungono la destinazione con i nomi base, senza sottocartelle.",
            "Los archivos llegan a una carpeta temporal de este PC y pasan al destino con sus nombres base, sin subcarpetas.", "Les fichiers arrivent dans un dossier temporaire de ce PC et rejoignent la destination sous leur nom de base, sans sous-dossiers.", "Os arquivos chegam a uma pasta temporária deste PC e vão para o destino com os nomes base, sem subpastas.", "تصل الملفات إلى مجلد مؤقت على هذا الكمبيوتر وتنتقل إلى الوجهة بأسمائها الأساسية، دون مجلدات فرعية.",
            "फ़ाइलें इस पीसी के एक अस्थायी फ़ोल्डर में आती हैं और अपने मूल नामों के साथ, बिना सबफ़ोल्डर के, गंतव्य तक पहुँचती हैं।", "ফাইলগুলো এই পিসির একটি অস্থায়ী ফোল্ডারে আসে এবং মূল নাম নিয়ে, সাবফোল্ডার ছাড়াই, গন্তব্যে পৌঁছায়।", "فائلیں اس پی سی کے ایک عارضی فولڈر میں آتی ہیں اور اپنے بنیادی ناموں کے ساتھ، ذیلی فولڈروں کے بغیر، منزل تک پہنچتی ہیں۔", "文件先到达此电脑的临时文件夹，再以基本名称进入目标位置，不保留子文件夹。")
        Add("Файлы отправляются пакетами через ваш Google Drive и попадают на устройство, когда оно их заберёт. Вложенные папки не сохраняются.",
            "Files go in packets through your Google Drive and reach the device when it picks them up. Subfolders are not kept.", "Файли надсилаються пакетами через ваш Google Drive і потрапляють на пристрій, коли він їх забере. Вкладені папки не зберігаються.", "Dateien werden paketweise über Ihr Google Drive gesendet und erreichen das Gerät, sobald es sie abholt. Unterordner bleiben nicht erhalten.", "I file vengono inviati a pacchetti tramite il tuo Google Drive e raggiungono il dispositivo quando li preleva. Le sottocartelle non vengono mantenute.",
            "Los archivos se envían en paquetes a través de tu Google Drive y llegan al dispositivo cuando este los recoge. Las subcarpetas no se conservan.", "Les fichiers sont envoyés par paquets via votre Google Drive et parviennent à l'appareil quand il les récupère. Les sous-dossiers ne sont pas conservés.", "Os arquivos são enviados em pacotes pelo seu Google Drive e chegam ao dispositivo quando ele os busca. As subpastas não são mantidas.", "تُرسل الملفات على شكل حزم عبر Google Drive الخاص بك وتصل إلى الجهاز عندما يستلمها. لا يتم الاحتفاظ بالمجلدات الفرعية.",
            "फ़ाइलें पैकेटों में आपके Google Drive के माध्यम से भेजी जाती हैं और डिवाइस के उन्हें लेने पर उस तक पहुँचती हैं। सबफ़ोल्डर नहीं रखे जाते।", "ফাইলগুলো প্যাকেটে আপনার Google Drive-এর মাধ্যমে পাঠানো হয় এবং ডিভাইস সেগুলো নিলে তার কাছে পৌঁছায়। সাবফোল্ডার রাখা হয় না।", "فائلیں پیکٹوں میں آپ کے Google Drive کے ذریعے بھیجی جاتی ہیں اور آلہ انہیں لینے پر اس تک پہنچتی ہیں۔ ذیلی فولڈر محفوظ نہیں رہتے۔", "文件通过您的 Google Drive 分包发送，设备取走时才会到达。不保留子文件夹。")
        Add("Что придёт через Google Drive, станет известно при запуске.",
            "What comes via Google Drive becomes known when the operation runs.", "Що прийде через Google Drive, стане відомо під час запуску.", "Was über Google Drive ankommt, zeigt sich erst bei der Ausführung.", "Ciò che arriva tramite Google Drive si saprà all'avvio.",
            "Lo que llegue a través de Google Drive se sabrá al ejecutar la operación.", "Ce qui arrive via Google Drive ne sera connu qu'à l'exécution.", "O que vier pelo Google Drive só será conhecido na execução.", "ما سيصل عبر Google Drive سيُعرف عند التشغيل.",
            "Google Drive के माध्यम से क्या आएगा, यह चलाने पर पता चलेगा।", "Google Drive-এর মাধ্যমে কী আসবে, তা চালানোর সময় জানা যাবে।", "Google Drive کے ذریعے کیا آئے گا، یہ چلانے پر معلوم ہوگا۔", "通过 Google Drive 会收到什么，要到运行时才知道。")
        Add("Пропущена: устройство не в сети",
            "Skipped: device is offline", "Пропущена: пристрій не в мережі", "Übersprungen: Gerät ist offline", "Saltata: dispositivo offline",
            "Omitida: el dispositivo está desconectado", "Ignorée : l'appareil est hors ligne", "Ignorada: dispositivo offline", "تم التخطي: الجهاز غير متصل",
            "छोड़ा गया: डिवाइस ऑफ़लाइन है", "বাদ দেওয়া হয়েছে: ডিভাইস অফলাইনে আছে", "چھوڑ دیا گیا: ڈیوائس آف لائن ہے", "已跳过：设备不在线")
        Add("Пропущено: {0}",
            "Skipped: {0}", "Пропущено: {0}", "Übersprungen: {0}", "Saltato: {0}",
            "Omitido: {0}", "Ignoré : {0}", "Ignorado: {0}", "تم التخطي: {0}",
            "छोड़ा गया: {0}", "বাদ দেওয়া হয়েছে: {0}", "چھوڑ دیا گیا: {0}", "已跳过：{0}")
        Add("Через Google Drive",
            "Via Google Drive", "Через Google Drive", "Über Google Drive", "Tramite Google Drive",
            "A través de Google Drive", "Via Google Drive", "Pelo Google Drive", "عبر Google Drive",
            "Google Drive के माध्यम से", "Google Drive-এর মাধ্যমে", "Google Drive کے ذریعے", "通过 Google Drive")
        Add("Через Google Drive: {0}",
            "Via Google Drive: {0}", "Через Google Drive: {0}", "Über Google Drive: {0}", "Tramite Google Drive: {0}",
            "A través de Google Drive: {0}", "Via Google Drive : {0}", "Pelo Google Drive: {0}", "عبر Google Drive: {0}",
            "Google Drive के माध्यम से: {0}", "Google Drive-এর মাধ্যমে: {0}", "Google Drive کے ذریعے: {0}", "通过 Google Drive：{0}")
        Add("Выполнить пропущенный запуск при старте программы",
            "Run a missed schedule once when the program starts", "Виконати пропущений запуск під час старту програми", "Verpasste Ausführung beim Programmstart einmal nachholen", "Esegui una volta l'avvio mancato all'apertura del programma",
            "Ejecutar una vez el inicio omitido al abrir el programa", "Exécuter une fois le lancement manqué au démarrage du programme", "Executar uma vez a execução perdida ao iniciar o programa", "تنفيذ التشغيل الفائت مرة واحدة عند بدء البرنامج",
            "प्रोग्राम शुरू होने पर छूटा हुआ रन एक बार चलाएँ", "প্রোগ্রাম চালু হলে বাদ পড়া রান একবার চালান", "پروگرام شروع ہونے پر چھوٹا ہوا رن ایک بار چلائیں", "程序启动时补执行一次错过的计划")
        Add("Показывать уведомление при ошибке",
            "Show a notification on error", "Показувати сповіщення у разі помилки", "Benachrichtigung bei Fehler anzeigen", "Mostra una notifica in caso di errore",
            "Mostrar una notificación en caso de error", "Afficher une notification en cas d'erreur", "Mostrar uma notificação em caso de erro", "إظهار إشعار عند حدوث خطأ",
            "त्रुटि होने पर सूचना दिखाएँ", "ত্রুটি হলে বিজ্ঞপ্তি দেখান", "خرابی کی صورت میں اطلاع دکھائیں", "出错时显示通知")
        Add("Экспорт..",
            "Export..", "Експорт..", "Exportieren..", "Esporta..",
            "Exportar..", "Exporter..", "Exportar..", "تصدير..",
            "निर्यात..", "এক্সপোর্ট..", "ایکسپورٹ..", "导出..")
        Add("Импорт..",
            "Import..", "Імпорт..", "Importieren..", "Importa..",
            "Importar..", "Importer..", "Importar..", "استيراد..",
            "आयात..", "ইম্পোর্ট..", "امپورٹ..", "导入..")
        Add("Экспорт операций по расписанию",
            "Export scheduled operations", "Експорт операцій за розкладом", "Geplante Vorgänge exportieren", "Esporta operazioni pianificate",
            "Exportar operaciones programadas", "Exporter les opérations planifiées", "Exportar operações agendadas", "تصدير العمليات المجدولة",
            "शेड्यूल किए गए ऑपरेशन निर्यात करें", "সময়সূচিবদ্ধ অপারেশন এক্সপোর্ট করুন", "شیڈول شدہ آپریشنز ایکسپورٹ کریں", "导出计划操作")
        Add("Импорт операций по расписанию",
            "Import scheduled operations", "Імпорт операцій за розкладом", "Geplante Vorgänge importieren", "Importa operazioni pianificate",
            "Importar operaciones programadas", "Importer les opérations planifiées", "Importar operações agendadas", "استيراد العمليات المجدولة",
            "शेड्यूल किए गए ऑपरेशन आयात करें", "সময়সূচিবদ্ধ অপারেশন ইম্পোর্ট করুন", "شیڈول شدہ آپریشنز امپورٹ کریں", "导入计划操作")
        Add("Импорт не удался: файл не читается как экспорт операций по расписанию.",
            "Import failed: the file is not a readable export of scheduled operations.", "Імпорт не вдався: файл не читається як експорт операцій за розкладом.", "Import fehlgeschlagen: Die Datei ist kein lesbarer Export geplanter Vorgänge.", "Importazione non riuscita: il file non è un'esportazione leggibile delle operazioni pianificate.",
            "La importación falló: el archivo no es una exportación legible de operaciones programadas.", "Échec de l'importation : le fichier n'est pas une exportation lisible d'opérations planifiées.", "A importação falhou: o arquivo não é uma exportação legível de operações agendadas.", "فشل الاستيراد: الملف ليس تصديرًا قابلًا للقراءة للعمليات المجدولة.",
            "आयात विफल: फ़ाइल शेड्यूल किए गए ऑपरेशन का पढ़ने योग्य निर्यात नहीं है।", "ইম্পোর্ট ব্যর্থ: ফাইলটি সময়সূচিবদ্ধ অপারেশনের পড়ার যোগ্য এক্সপোর্ট নয়।", "امپورٹ ناکام: فائل شیڈول شدہ آپریشنز کی قابلِ مطالعہ ایکسپورٹ نہیں ہے۔", "导入失败：该文件不是可读取的计划操作导出文件。")
        Add("Импортировано операций: {0}. Не импортировано: {1}.",
            "Operations imported: {0}. Not imported: {1}.", "Імпортовано операцій: {0}. Не імпортовано: {1}.", "Importierte Vorgänge: {0}. Nicht importiert: {1}.", "Operazioni importate: {0}. Non importate: {1}.",
            "Operaciones importadas: {0}. No importadas: {1}.", "Opérations importées : {0}. Non importées : {1}.", "Operações importadas: {0}. Não importadas: {1}.", "العمليات المستوردة: {0}. غير المستوردة: {1}.",
            "आयात किए गए ऑपरेशन: {0}। आयात नहीं हुए: {1}।", "ইম্পোর্ট করা অপারেশন: {0}। ইম্পোর্ট হয়নি: {1}।", "امپورٹ شدہ آپریشنز: {0}۔ امپورٹ نہ ہوئے: {1}۔", "已导入的操作：{0}。未导入：{1}。")
        Add("Не удалось записать файл экспорта.",
            "The export file could not be written.", "Не вдалося записати файл експорту.", "Die Exportdatei konnte nicht geschrieben werden.", "Impossibile scrivere il file di esportazione.",
            "No se pudo escribir el archivo de exportación.", "Impossible d'écrire le fichier d'exportation.", "Não foi possível gravar o arquivo de exportação.", "تعذّرت كتابة ملف التصدير.",
            "निर्यात फ़ाइल लिखी नहीं जा सकी।", "এক্সপোর্ট ফাইলটি লেখা যায়নি।", "ایکسپورٹ فائل لکھی نہیں جا سکی۔", "无法写入导出文件。")
        ' Ticket L0299: the fifth action, fill free space with deletion (FileDO) - the editor's controls and note, and the
        ' sentences of the run's outcomes. The product name FileDO stays Latin in every language.
        Add("Заполнение свободного места с удалением (FileDO)",
            "Fill free space with deletion (FileDO)", "Заповнення вільного місця з видаленням (FileDO)", "Freien Speicherplatz füllen und löschen (FileDO)", "Riempi spazio libero con eliminazione (FileDO)",
            "Llenar espacio libre con eliminación (FileDO)", "Remplir l'espace libre avec suppression (FileDO)", "Preencher espaço livre com exclusão (FileDO)", "ملء المساحة الفارغة مع الحذف (FileDO)",
            "खाली जगह भरें और बाद में हटाएं (FileDO)", "খালি জায়গা পূরণ এবং পরে মুছে ফেলা (FileDO)", "خالی جگہ بھریں اور بعد میں حذف کریں (FileDO)", "填充剩余空间并随后删除 (FileDO)")
        Add("Размер временных файлов (МБ)",
            "Chunk file size (MB)", "Розмір тимчасових файлів (МБ)", "Größe der temporären Dateien (MB)", "Dimensione file temporanei (MB)",
            "Tamaño de archivos temporales (MB)", "Taille des fichiers temporaires (Mo)", "Tamanho dos arquivos temporários (MB)", "حجم الملفات المؤقتة (م.ب)",
            "अस्थायी फ़ाइल का आकार (MB)", "অস্থায়ী ফাইলের আকার (MB)", "عارضی فائل کا سائز (MB)", "临时文件块大小 (MB)")
        Add("Свободное место ({0}) будет заполнено временными файлами и затем очищено.",
            "Free space ({0}) will be overwritten with temporary files and then deleted.", "Вільне місце ({0}) буде заповнено тимчасовими файлами та потім очищено.", "Freier Speicherplatz ({0}) wird mit temporären Dateien überschrieben und gelöscht.", "Lo spazio libero ({0}) verrà sovrascritto con file temporanei e poi eliminato.",
            "El espacio libre ({0}) se sobrescribirá con archivos temporales y luego se eliminará.", "L'espace libre ({0}) sera écrasé par des fichiers temporaires puis supprimé.", "O espaço livre ({0}) será sobrescrito com arquivos temporários e depois excluído.", "سيتم ملء المساحة الفارغة ({0}) بملفات مؤقتة ثم حذفها.",
            "खाली स्थान ({0}) अस्थायी फ़ाइलों से भरा जाएगा और फिर हटा दिया जाएगा।", "খালি স্থান ({0}) অস্থায়ী ফাইল দিয়ে পূরণ করা হবে এবং পরে মুছে ফেলা হবে।", "خالی جگہ ({0}) عارضی فائلوں سے بھری جائے گی اور پھر حذف کر دی جائے گی۔", "剩余空间 ({0}) 将写入临时文件并随后删除。")
        Add("Перезапись свободного места на SSD увеличивает износ диска и не гарантирует полное удаление данных.",
            "Overwriting free space on SSDs increases drive wear and does not guarantee complete data erasure.", "Перезапис вільного місця на SSD збільшує зношування диска та не гарантує повне видалення даних.", "Das Überschreiben auf SSDs erhöht den Verschleiß und garantiert kein vollständiges Löschen.", "La sovrascrittura su SSD aumenta l'usura e non garantisce la cancellazione completa dei dati.",
            "Sobrescribir espacio libre en SSD aumenta el desgaste y no garantiza el borrado completo de datos.", "Écraser l'espace libre sur SSD augmente l'usure et ne garantit pas l'effacement complet des données.", "Sobrescrever espaço livre em SSDs aumenta o desgaste e não garante a exclusão completa dos dados.", "تؤدي الكتابة فوق المساحة الفارغة في أقراص SSD إلى زيادة التآكل ولا تضمن محو البيانات بالكامل.",
            "SSD पर खाली स्थान को अधिलेखित करने से डिस्क का घिसाव बढ़ता है और पूर्ण डेटा मिटाने की गारंटी नहीं मिलती है।", "SSD-তে খালি জায়গা ওভাররাইট করলে ডিস্কের ক্ষয় বৃদ্ধি পায় এবং সম্পূর্ণ ডেটা মুছে ফেলার নিশ্চয়তা দেয় না।", "SSD پر خالی جگہ کو دوبارہ لکھنے سے ڈسک کی خرابی میں اضافہ ہوتا ہے اور مکمل ڈیٹا حذف کرنے کی ضمانت نہیں ملتی ہے۔", "在固态硬盘 (SSD) 上覆盖剩余空间会增加磨损，且不能保证数据完全擦除。")
        Add("Для этой операции требуется FileDO. Установите компонент FileDO.",
            "FileDO is required for this operation. Please install the FileDO component.", "Для цієї операції потрібен FileDO. Встановіть компонент FileDO.", "FileDO ist für diese Operation erforderlich. Bitte FileDO-Komponente installieren.", "FileDO è richiesto per questa operazione. Installare il componente FileDO.",
            "Se requiere FileDO para esta operación. Instale el componente FileDO.", "FileDO est requis pour cette opération. Veuillez installer le composant FileDO.", "O FileDO é necessário para esta operação. Instale o componente FileDO.", "يلزم FileDO لهذه العملية. يرجى تثبيت مكون FileDO.",
            "इस ऑपरेशन के लिए FileDO आवश्यक है। कृपया FileDO घटक स्थापित करें।", "এই অপারেশনের জন্য FileDO প্রয়োজন। দয়া করে FileDO উপাদানটি ইনস্টল করুন।", "اس آپریشن کے لیے FileDO درکار ہے۔ براہ کرم FileDO جزو انسٹال کریں۔", "此操作需要 FileDO。请安装 FileDO 组件。")
        Add("Заполнение свободного места работает только с локальным диском или папкой на нём.",
            "Filling free space works only with a local disk or a folder on it.", "Заповнення вільного місця працює лише з локальним диском або папкою на ньому.", "Das Füllen von freiem Speicherplatz funktioniert nur mit einem lokalen Laufwerk oder einem Ordner darauf.", "Il riempimento dello spazio libero funziona solo con un disco locale o una cartella al suo interno.",
            "El llenado del espacio libre solo funciona con un disco local o una carpeta de este.", "Le remplissage de l'espace libre ne fonctionne qu'avec un disque local ou un dossier situé dessus.", "O preenchimento do espaço livre funciona apenas com um disco local ou uma pasta nele.", "يعمل ملء المساحة الفارغة مع قرص محلي أو مجلد عليه فقط.",
            "खाली स्थान भरने की सुविधा केवल स्थानीय डिस्क या उस पर मौजूद फ़ोल्डर के साथ काम करती है।", "খালি জায়গা পূরণ শুধু লোকাল ডিস্ক বা তার ওপরের ফোল্ডারের সঙ্গে কাজ করে।", "خالی جگہ بھرنے کی سہولت صرف مقامی ڈسک یا اس پر موجود فولڈر کے ساتھ کام کرتی ہے۔", "填充剩余空间仅适用于本地磁盘或其上的文件夹。")
        Add("FileDO не найден: установите компонент FileDO",
            "FileDO not found: install the FileDO component", "FileDO не знайдено: встановіть компонент FileDO", "FileDO nicht gefunden: FileDO-Komponente installieren", "FileDO non trovato: installare il componente FileDO",
            "FileDO no encontrado: instale el componente FileDO", "FileDO introuvable : installez le composant FileDO", "FileDO não encontrado: instale o componente FileDO", "لم يتم العثور على FileDO: ثبّت مكوّن FileDO",
            "FileDO नहीं मिला: FileDO घटक स्थापित करें", "FileDO পাওয়া যায়নি: FileDO উপাদানটি ইনস্টল করুন", "FileDO نہیں ملا: FileDO جزو انسٹال کریں", "未找到 FileDO：请安装 FileDO 组件")
        Add("не удалось запустить FileDO: {0}",
            "could not start FileDO: {0}", "не вдалося запустити FileDO: {0}", "FileDO konnte nicht gestartet werden: {0}", "impossibile avviare FileDO: {0}",
            "no se pudo iniciar FileDO: {0}", "impossible de démarrer FileDO : {0}", "não foi possível iniciar o FileDO: {0}", "تعذّر تشغيل FileDO: {0}",
            "FileDO शुरू नहीं हो सका: {0}", "FileDO চালু করা যায়নি: {0}", "FileDO شروع نہیں ہو سکا: {0}", "无法启动 FileDO：{0}")
        Add("FileDO не смог заполнить свободное место: {0}",
            "FileDO could not fill the free space: {0}", "FileDO не зміг заповнити вільне місце: {0}", "FileDO konnte den freien Speicherplatz nicht füllen: {0}", "FileDO non è riuscito a riempire lo spazio libero: {0}",
            "FileDO no pudo llenar el espacio libre: {0}", "FileDO n'a pas pu remplir l'espace libre : {0}", "O FileDO não conseguiu preencher o espaço livre: {0}", "تعذّر على FileDO ملء المساحة الفارغة: {0}",
            "FileDO खाली स्थान नहीं भर सका: {0}", "FileDO খালি জায়গা পূরণ করতে পারেনি: {0}", "FileDO خالی جگہ نہیں بھر سکا: {0}", "FileDO 无法填充剩余空间：{0}")
        Add("FileDO не подтвердил результат заполнения: {0}",
            "FileDO did not confirm the result of the fill: {0}", "FileDO не підтвердив результат заповнення: {0}", "FileDO hat das Ergebnis des Füllens nicht bestätigt: {0}", "FileDO non ha confermato il risultato del riempimento: {0}",
            "FileDO no confirmó el resultado del llenado: {0}", "FileDO n'a pas confirmé le résultat du remplissage : {0}", "O FileDO não confirmou o resultado do preenchimento: {0}", "لم يؤكد FileDO نتيجة الملء: {0}",
            "FileDO ने भरने के परिणाम की पुष्टि नहीं की: {0}", "FileDO পূরণের ফলাফল নিশ্চিত করেনি: {0}", "FileDO نے بھرنے کے نتیجے کی تصدیق نہیں کی: {0}", "FileDO 未确认填充结果：{0}")
        Add("в рабочей папке остались файлы, удалите её вручную: {0}",
            "files are left in the work folder, delete it manually: {0}", "у робочій папці залишилися файли, видаліть її вручну: {0}", "im Arbeitsordner sind Dateien übrig geblieben, löschen Sie ihn manuell: {0}", "nella cartella di lavoro sono rimasti dei file, eliminarla manualmente: {0}",
            "quedaron archivos en la carpeta de trabajo, elimínela manualmente: {0}", "des fichiers restent dans le dossier de travail, supprimez-le manuellement : {0}", "restaram arquivos na pasta de trabalho, exclua-a manualmente: {0}", "بقيت ملفات في مجلد العمل، احذفه يدويًا: {0}",
            "कार्य फ़ोल्डर में फ़ाइलें रह गई हैं, उसे मैन्युअल रूप से हटाएँ: {0}", "কাজের ফোল্ডারে ফাইল রয়ে গেছে, এটি নিজে মুছে ফেলুন: {0}", "ورکنگ فولڈر میں فائلیں رہ گئی ہیں، اسے خود حذف کریں: {0}", "工作文件夹中仍有文件，请手动删除该文件夹：{0}")
        Add("сетевой диск нельзя заполнять: {0}",
            "a network drive cannot be filled: {0}", "мережевий диск не можна заповнювати: {0}", "ein Netzlaufwerk kann nicht gefüllt werden: {0}", "un'unità di rete non può essere riempita: {0}",
            "no se puede llenar una unidad de red: {0}", "un lecteur réseau ne peut pas être rempli : {0}", "uma unidade de rede não pode ser preenchida: {0}", "لا يمكن ملء محرك أقراص الشبكة: {0}",
            "नेटवर्क ड्राइव को भरा नहीं जा सकता: {0}", "নেটওয়ার্ক ড্রাইভ পূরণ করা যায় না: {0}", "نیٹ ورک ڈرائیو کو بھرا نہیں جا سکتا: {0}", "无法填充网络驱动器：{0}")
        Add("свободно перед запуском: {0}, перезаписано: {1}",
            "free before the run: {0}, overwritten: {1}", "вільно перед запуском: {0}, перезаписано: {1}", "frei vor dem Start: {0}, überschrieben: {1}", "libero prima dell'esecuzione: {0}, sovrascritto: {1}",
            "libre antes de la ejecución: {0}, sobrescrito: {1}", "libre avant l'exécution : {0}, écrasé : {1}", "livre antes da execução: {0}, sobrescrito: {1}", "المساحة الفارغة قبل التشغيل: {0}، المكتوب فوقها: {1}",
            "चलाने से पहले खाली: {0}, अधिलेखित: {1}", "চালানোর আগে খালি: {0}, ওভাররাইট করা: {1}", "چلانے سے پہلے خالی: {0}، دوبارہ لکھا گیا: {1}", "运行前可用空间：{0}，已覆盖：{1}")
        Add("{0} ГБ",
            "{0} GB", "{0} ГБ", "{0} GB", "{0} GB",
            "{0} GB", "{0} Go", "{0} GB", "{0} غيغابايت",
            "{0} GB", "{0} GB", "{0} GB", "{0} GB")
        Add("{0} МБ",
            "{0} MB", "{0} МБ", "{0} MB", "{0} MB",
            "{0} MB", "{0} Mo", "{0} MB", "{0} ميغابايت",
            "{0} MB", "{0} MB", "{0} MB", "{0} MB")
        Add("Диск {0}: объём {1}, свободно {2}.",
            "Drive {0}: capacity {1}, free {2}.", "Диск {0}: обсяг {1}, вільно {2}.", "Laufwerk {0}: Kapazität {1}, frei {2}.", "Disco {0}: capacità {1}, libero {2}.",
            "Disco {0}: capacidad {1}, libre {2}.", "Disque {0} : capacité {1}, libre {2}.", "Disco {0}: capacidade {1}, livre {2}.", "القرص {0}: السعة {1}، المتاح {2}.",
            "डिस्क {0}: क्षमता {1}, खाली {2}।", "ডিস্ক {0}: ধারণক্ষমতা {1}, খালি {2}।", "ڈسک {0}: گنجائش {1}، خالی {2}۔", "磁盘 {0}：容量 {1}，可用 {2}。")
        Add("Это системный диск: FileDO оставит свободными не меньше 10 ГБ и не меньше 10 % его объёма.",
            "This is the system drive: FileDO will keep at least 10 GB and at least 10 % of its capacity free.", "Це системний диск: FileDO залишить вільними не менше 10 ГБ і не менше 10 % його обсягу.", "Dies ist das Systemlaufwerk: FileDO lässt mindestens 10 GB und mindestens 10 % seiner Kapazität frei.", "Questo è il disco di sistema: FileDO lascerà liberi almeno 10 GB e almeno il 10 % della sua capacità.",
            "Esta es la unidad del sistema: FileDO dejará libres al menos 10 GB y al menos el 10 % de su capacidad.", "Il s'agit du disque système : FileDO laissera libres au moins 10 Go et au moins 10 % de sa capacité.", "Este é o disco do sistema: o FileDO deixará livres pelo menos 10 GB e pelo menos 10 % da sua capacidade.", "هذا هو قرص النظام: سيُبقي FileDO ما لا يقل عن 10 غيغابايت وما لا يقل عن 10 % من سعته فارغًا.",
            "यह सिस्टम ड्राइव है: FileDO कम से कम 10 GB और उसकी क्षमता का कम से कम 10 % खाली छोड़ देगा।", "এটি সিস্টেম ড্রাইভ: FileDO অন্তত 10 GB এবং এর ধারণক্ষমতার অন্তত 10 % খালি রাখবে।", "یہ سسٹم ڈرائیو ہے: FileDO کم از کم 10 GB اور اس کی گنجائش کا کم از کم 10 % خالی چھوڑے گا۔", "这是系统盘：FileDO 将保留至少 10 GB 且不少于其容量 10 % 的可用空间。")

        ' --- backup of the folder (FileDO), ticket L0298 ---
        Add("Резервная копия папки (FileDO)",
            "Folder backup (FileDO)", "Резервна копія папки (FileDO)", "Ordnersicherung (FileDO)", "Backup della cartella (FileDO)",
            "Copia de seguridad de la carpeta (FileDO)", "Sauvegarde du dossier (FileDO)", "Backup da pasta (FileDO)", "نسخة احتياطية من المجلد (FileDO)",
            "फ़ोल्डर बैकअप (FileDO)", "ফোল্ডার ব্যাকআপ (FileDO)", "فولڈر کا بیک اپ (FileDO)", "文件夹备份 (FileDO)")
        Add("пароль резервной копии не сохранён на этом компьютере: введите его в операции",
            "the backup password is not stored on this PC: enter it in the operation", "пароль резервної копії не збережено на цьому комп'ютері: введіть його в операції", "das Sicherungskennwort ist auf diesem PC nicht gespeichert: geben Sie es in der Operation ein", "la password del backup non è salvata su questo PC: inseritela nell'operazione",
            "la contraseña de la copia de seguridad no está guardada en este equipo: introdúzcala en la operación", "le mot de passe de la sauvegarde n'est pas enregistré sur ce PC : saisissez-le dans l'opération", "a senha do backup não está salva neste computador: digite-a na operação", "كلمة مرور النسخة الاحتياطية غير محفوظة على هذا الكمبيوتر: أدخلها في العملية",
            "बैकअप का पासवर्ड इस कंप्यूटर पर सहेजा नहीं गया है: इसे ऑपरेशन में दर्ज करें", "ব্যাকআপের পাসওয়ার্ড এই কম্পিউটারে সংরক্ষিত নেই: অপারেশনে এটি লিখুন", "بیک اپ کا پاس ورڈ اس کمپیوٹر پر محفوظ نہیں ہے: اسے آپریشن میں درج کریں", "此电脑上未保存备份密码：请在操作中输入")
        Add("установленный FileDO не умеет нумеровать резервные копии: обновите компонент FileDO",
            "the installed FileDO cannot number backups: update the FileDO component", "встановлений FileDO не вміє нумерувати резервні копії: оновіть компонент FileDO", "das installierte FileDO kann Sicherungen nicht nummerieren: aktualisieren Sie die FileDO-Komponente", "il FileDO installato non sa numerare i backup: aggiornate il componente FileDO",
            "el FileDO instalado no puede numerar copias de seguridad: actualice el componente FileDO", "le FileDO installé ne sait pas numéroter les sauvegardes : mettez à jour le composant FileDO", "o FileDO instalado não sabe numerar backups: atualize o componente FileDO", "لا يستطيع FileDO المثبّت ترقيم النسخ الاحتياطية: حدّث مكون FileDO",
            "इंस्टॉल किया गया FileDO बैकअप को क्रमांकित नहीं कर सकता: FileDO घटक अपडेट करें", "ইনস্টল করা FileDO ব্যাকআপে নম্বর দিতে পারে না: FileDO উপাদানটি আপডেট করুন", "انسٹال شدہ FileDO بیک اپ کو نمبر نہیں دے سکتا: FileDO جزو اپ ڈیٹ کریں", "已安装的 FileDO 无法为备份编号：请更新 FileDO 组件")
        Add("Резервная копия принимает только обычные папки, не ресурсы и не Google Drive.",
            "A backup takes ordinary folders only, not resources and not Google Drive.", "Резервна копія приймає лише звичайні папки, не ресурси й не Google Drive.", "Eine Sicherung akzeptiert nur normale Ordner, keine Ressourcen und kein Google Drive.", "Un backup accetta solo cartelle normali, non risorse né Google Drive.",
            "Una copia de seguridad solo admite carpetas normales, no recursos ni Google Drive.", "Une sauvegarde n'accepte que des dossiers ordinaires, ni ressources ni Google Drive.", "Um backup aceita apenas pastas comuns, não recursos nem o Google Drive.", "تقبل النسخة الاحتياطية المجلدات العادية فقط، لا الموارد ولا Google Drive.",
            "बैकअप केवल सामान्य फ़ोल्डर लेता है, संसाधन या Google Drive नहीं।", "ব্যাকআপ শুধু সাধারণ ফোল্ডার নেয়, রিসোর্স বা Google Drive নয়।", "بیک اپ صرف عام فولڈر لیتا ہے، وسائل یا Google Drive نہیں۔", "备份只接受普通文件夹，不接受资源或 Google Drive。")
        Add("Введите пароль для защищённой резервной копии.",
            "Enter a password for the protected backup.", "Введіть пароль для захищеної резервної копії.", "Geben Sie ein Kennwort für die geschützte Sicherung ein.", "Inserite una password per il backup protetto.",
            "Introduzca una contraseña para la copia de seguridad protegida.", "Saisissez un mot de passe pour la sauvegarde protégée.", "Digite uma senha para o backup protegido.", "أدخل كلمة مرور للنسخة الاحتياطية المحمية.",
            "सुरक्षित बैकअप के लिए पासवर्ड दर्ज करें।", "সুরক্ষিত ব্যাকআপের জন্য একটি পাসওয়ার্ড লিখুন।", "محفوظ بیک اپ کے لیے پاس ورڈ درج کریں۔", "请为受保护的备份输入密码。")
        Add("Хранить копий",
            "Copies to keep", "Зберігати копій", "Aufzubewahrende Kopien", "Copie da conservare",
            "Copias que conservar", "Copies à conserver", "Cópias a manter", "عدد النسخ المحفوظة",
            "रखी जाने वाली प्रतियाँ", "যতগুলো কপি রাখা হবে", "محفوظ رکھی جانے والی کاپیاں", "保留的副本数")
        Add("Защитить копии паролем (контейнер FileDO)",
            "Protect the copies with a password (FileDO container)", "Захистити копії паролем (контейнер FileDO)", "Kopien mit Kennwort schützen (FileDO-Container)", "Proteggi le copie con una password (contenitore FileDO)",
            "Proteger las copias con contraseña (contenedor FileDO)", "Protéger les copies par mot de passe (conteneur FileDO)", "Proteger as cópias com senha (contêiner FileDO)", "حماية النسخ بكلمة مرور (حاوية FileDO)",
            "प्रतियों को पासवर्ड से सुरक्षित करें (FileDO कंटेनर)", "কপিগুলো পাসওয়ার্ড দিয়ে সুরক্ষিত করুন (FileDO কন্টেইনার)", "کاپیوں کو پاس ورڈ سے محفوظ کریں (FileDO کنٹینر)", "用密码保护副本 (FileDO 容器)")
        Add("Каждый запуск сохраняет папку целиком как следующую копию с номером от 001 до {0}.",
            "Each run saves the whole folder as the next copy, numbered 001 to {0}.", "Кожен запуск зберігає папку цілком як наступну копію з номером від 001 до {0}.", "Jeder Lauf speichert den ganzen Ordner als nächste Kopie, nummeriert von 001 bis {0}.", "Ogni esecuzione salva l'intera cartella come copia successiva, numerata da 001 a {0}.",
            "Cada ejecución guarda la carpeta entera como la copia siguiente, numerada de 001 a {0}.", "Chaque exécution enregistre le dossier entier comme copie suivante, numérotée de 001 à {0}.", "Cada execução salva a pasta inteira como a próxima cópia, numerada de 001 a {0}.", "يحفظ كل تشغيل المجلد كاملًا كنسخة تالية مرقّمة من 001 إلى {0}.",
            "हर रन पूरे फ़ोल्डर को अगली प्रति के रूप में सहेजता है, क्रमांक 001 से {0} तक।", "প্রতিটি রান পুরো ফোল্ডারটি পরবর্তী কপি হিসেবে সংরক্ষণ করে, নম্বর 001 থেকে {0}।", "ہر رن پورے فولڈر کو اگلی کاپی کے طور پر محفوظ کرتا ہے، نمبر 001 سے {0} تک۔", "每次运行将整个文件夹保存为下一个副本，编号 001 至 {0}。")
        Add("Когда набор заполнен, новая копия после проверки заменяет самую старую. Исходная папка не изменяется. Имена копий: {0}.",
            "Once the set is full, the new copy replaces the oldest one after it has been verified. The source folder is not changed. Copy names: {0}.", "Коли набір заповнено, нова копія після перевірки замінює найстаршу. Вихідна папка не змінюється. Імена копій: {0}.", "Ist der Satz voll, ersetzt die neue Kopie nach ihrer Prüfung die älteste. Der Quellordner wird nicht verändert. Namen der Kopien: {0}.", "Quando il set è pieno, la nuova copia, dopo la verifica, sostituisce la più vecchia. La cartella di origine non viene modificata. Nomi delle copie: {0}.",
            "Cuando el conjunto está lleno, la copia nueva sustituye a la más antigua tras verificarse. La carpeta de origen no cambia. Nombres de las copias: {0}.", "Quand le jeu est plein, la nouvelle copie remplace la plus ancienne après vérification. Le dossier source n'est pas modifié. Noms des copies : {0}.", "Quando o conjunto está cheio, a nova cópia substitui a mais antiga depois de verificada. A pasta de origem não é alterada. Nomes das cópias: {0}.", "عندما تمتلئ المجموعة، تحل النسخة الجديدة بعد التحقق منها محل الأقدم. لا يتغير المجلد المصدر. أسماء النسخ: {0}.",
            "सेट भर जाने पर, जाँच के बाद नई प्रति सबसे पुरानी की जगह लेती है। स्रोत फ़ोल्डर नहीं बदलता। प्रतियों के नाम: {0}।", "সেট পূর্ণ হলে, যাচাইয়ের পর নতুন কপি সবচেয়ে পুরোনোটির জায়গা নেয়। উৎস ফোল্ডার বদলায় না। কপির নাম: {0}।", "سیٹ بھر جانے پر، جانچ کے بعد نئی کاپی سب سے پرانی کی جگہ لے لیتی ہے۔ ماخذ فولڈر تبدیل نہیں ہوتا۔ کاپیوں کے نام: {0}۔", "副本集已满时，新副本经验证后替换最旧的副本。源文件夹不会更改。副本名称：{0}。")
        Add("Пароль сохранён на этом компьютере. Оставьте поле пустым, чтобы не менять его. Без пароля копию открыть нельзя.",
            "The password is stored on this PC. Leave the box empty to keep it. A copy cannot be opened without the password.", "Пароль збережено на цьому комп'ютері. Залиште поле порожнім, щоб не змінювати його. Без пароля копію відкрити неможливо.", "Das Kennwort ist auf diesem PC gespeichert. Lassen Sie das Feld leer, um es beizubehalten. Ohne Kennwort lässt sich eine Kopie nicht öffnen.", "La password è salvata su questo PC. Lasciate il campo vuoto per non cambiarla. Senza password la copia non si apre.",
            "La contraseña está guardada en este equipo. Deje el campo vacío para conservarla. Sin la contraseña la copia no se puede abrir.", "Le mot de passe est enregistré sur ce PC. Laissez le champ vide pour le conserver. Sans mot de passe, la copie ne s'ouvre pas.", "A senha está salva neste computador. Deixe o campo vazio para mantê-la. Sem a senha a cópia não pode ser aberta.", "كلمة المرور محفوظة على هذا الكمبيوتر. اترك الحقل فارغًا للإبقاء عليها. لا يمكن فتح النسخة بدون كلمة المرور.",
            "पासवर्ड इस कंप्यूटर पर सहेजा गया है। इसे न बदलने के लिए फ़ील्ड खाली छोड़ें। पासवर्ड के बिना प्रति नहीं खुल सकती।", "পাসওয়ার্ড এই কম্পিউটারে সংরক্ষিত আছে। এটি না বদলাতে ঘরটি খালি রাখুন। পাসওয়ার্ড ছাড়া কপি খোলা যায় না।", "پاس ورڈ اس کمپیوٹر پر محفوظ ہے۔ اسے برقرار رکھنے کے لیے خانہ خالی چھوڑیں۔ پاس ورڈ کے بغیر کاپی نہیں کھل سکتی۔", "密码已保存在此电脑上。保持此框为空即可不更改。没有密码无法打开副本。")
        Add("Пароль хранится только на этом компьютере и не попадает в экспорт настроек. Без пароля копию открыть нельзя.",
            "The password is stored only on this PC and is not part of the settings export. A copy cannot be opened without the password.", "Пароль зберігається лише на цьому комп'ютері й не потрапляє до експорту налаштувань. Без пароля копію відкрити неможливо.", "Das Kennwort wird nur auf diesem PC gespeichert und ist nicht Teil des Einstellungsexports. Ohne Kennwort lässt sich eine Kopie nicht öffnen.", "La password è salvata solo su questo PC e non entra nell'esportazione delle impostazioni. Senza password la copia non si apre.",
            "La contraseña solo se guarda en este equipo y no entra en la exportación de la configuración. Sin la contraseña la copia no se puede abrir.", "Le mot de passe n'est enregistré que sur ce PC et ne fait pas partie de l'export des réglages. Sans mot de passe, la copie ne s'ouvre pas.", "A senha fica salva somente neste computador e não entra na exportação das configurações. Sem a senha a cópia não pode ser aberta.", "تُحفظ كلمة المرور على هذا الكمبيوتر فقط ولا تدخل في تصدير الإعدادات. لا يمكن فتح النسخة بدون كلمة المرور.",
            "पासवर्ड केवल इस कंप्यूटर पर रहता है और सेटिंग्स निर्यात में शामिल नहीं होता। पासवर्ड के बिना प्रति नहीं खुल सकती।", "পাসওয়ার্ড শুধু এই কম্পিউটারে থাকে এবং সেটিংস রপ্তানিতে যায় না। পাসওয়ার্ড ছাড়া কপি খোলা যায় না।", "پاس ورڈ صرف اس کمپیوٹر پر محفوظ رہتا ہے اور ترتیبات کی برآمد میں شامل نہیں ہوتا۔ پاس ورڈ کے بغیر کاپی نہیں کھل سکتی۔", "密码只保存在此电脑上，不会包含在设置导出中。没有密码无法打开副本。")
        Add("Пароль слишком простой: нужно не меньше 8 символов, и не один и тот же символ.",
            "The password is too simple: it needs at least 8 characters, and not one character repeated.", "Пароль надто простий: потрібно щонайменше 8 символів, і не один і той самий символ.", "Das Kennwort ist zu einfach: mindestens 8 Zeichen, und nicht nur ein wiederholtes Zeichen.", "La password è troppo semplice: servono almeno 8 caratteri, e non un solo carattere ripetuto.",
            "La contraseña es demasiado simple: necesita al menos 8 caracteres, y no un solo carácter repetido.", "Le mot de passe est trop simple : il faut au moins 8 caractères, et pas un seul caractère répété.", "A senha é simples demais: são necessários pelo menos 8 caracteres, e não um único caractere repetido.", "كلمة المرور بسيطة جدًا: يلزم 8 أحرف على الأقل، وليس حرفًا واحدًا مكررًا.",
            "पासवर्ड बहुत सरल है: कम से कम 8 अक्षर चाहिए, और एक ही अक्षर दोहराया हुआ नहीं।", "পাসওয়ার্ডটি খুব সহজ: অন্তত 8টি অক্ষর দরকার, এবং একই অক্ষর বারবার নয়।", "پاس ورڈ بہت آسان ہے: کم از کم 8 حروف درکار ہیں، اور ایک ہی حرف بار بار نہیں۔", "密码过于简单：至少需要 8 个字符，且不能是同一字符重复。")
        Add("Не удалось сохранить пароль на этом компьютере. Операция сохранена, но запустится только после того, как пароль будет сохранён.",
            "The password could not be stored on this PC. The operation is saved, but it runs only once the password is stored.", "Не вдалося зберегти пароль на цьому комп'ютері. Операцію збережено, але вона запуститься лише після того, як пароль буде збережено.", "Das Kennwort konnte auf diesem PC nicht gespeichert werden. Die Operation ist gespeichert, läuft aber erst, wenn das Kennwort gespeichert ist.", "Impossibile salvare la password su questo PC. L'operazione è salvata, ma verrà eseguita solo dopo il salvataggio della password.",
            "No se pudo guardar la contraseña en este equipo. La operación está guardada, pero solo se ejecutará cuando la contraseña se haya guardado.", "Le mot de passe n'a pas pu être enregistré sur ce PC. L'opération est enregistrée, mais ne s'exécutera qu'une fois le mot de passe enregistré.", "Não foi possível salvar a senha neste computador. A operação está salva, mas só será executada depois que a senha for salva.", "تعذّر حفظ كلمة المرور على هذا الكمبيوتر. حُفظت العملية، لكنها لن تعمل إلا بعد حفظ كلمة المرور.",
            "इस कंप्यूटर पर पासवर्ड सहेजा नहीं जा सका। ऑपरेशन सहेज लिया गया है, लेकिन पासवर्ड सहेजे जाने के बाद ही चलेगा।", "এই কম্পিউটারে পাসওয়ার্ড সংরক্ষণ করা যায়নি। অপারেশনটি সংরক্ষিত হয়েছে, তবে পাসওয়ার্ড সংরক্ষিত হওয়ার পরেই চলবে।", "اس کمپیوٹر پر پاس ورڈ محفوظ نہیں کیا جا سکا۔ آپریشن محفوظ ہو گیا ہے، لیکن پاس ورڈ محفوظ ہونے کے بعد ہی چلے گا۔", "无法在此电脑上保存密码。操作已保存，但只有在密码保存后才会运行。")
    End Sub
End Class
