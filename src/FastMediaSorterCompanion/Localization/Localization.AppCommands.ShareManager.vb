Option Strict On

' <summary>
' Strings only the Share Manager's command line says (APP-COMMANDS, tickets L0316 and L0319): what became of a folder offered
' by "share add", and the sentences of the three observe verbs. Every other sentence of the command profile is the viewer's table (Localization.AppCommands.vb, linked)
' and the clipboard verbs' sentences are the clipboard table's.
'
' Columns after the Russian key: en, uk, de, it, es, fr, pt, ar, hi, bn, ur, zh. The pt column is pt-BR.
' Никогда не ставьте «умные» кавычки в литерал - VB считает U+201C/U+201D разделителями
' строки; используются «…» и 「…」.
' </summary>
Partial Public NotInheritable Class Localization

    Private Shared Sub AddShareManagerCommandStrings()

        Add("Папка предложена: ответ даст пользователь в окне менеджера.",
            "Folder proposed: the user will answer in the manager window.",
            "Папку запропоновано: відповідь дасть користувач у вікні менеджера.",
            "Ordner vorgeschlagen: Die Antwort gibt der Benutzer im Fenster des Managers.",
            "Cartella proposta: la risposta la darà l'utente nella finestra del gestore.",
            "Carpeta propuesta: la respuesta la dará el usuario en la ventana del administrador.",
            "Dossier proposé : l'utilisateur répondra dans la fenêtre du gestionnaire.",
            "Pasta sugerida: o usuário responderá na janela do gerenciador.",
            "تم اقتراح المجلد: سيجيب المستخدم في نافذة المدير.",
            "फ़ोल्डर प्रस्तावित किया गया: उत्तर उपयोगकर्ता प्रबंधक की विंडो में देगा।",
            "ফোল্ডার প্রস্তাব করা হয়েছে: ব্যবহারকারী ম্যানেজারের উইন্ডোতে উত্তর দেবেন।",
            "فولڈر تجویز کیا گیا: صارف مینیجر کی ونڈو میں جواب دے گا۔",
            "已提议该文件夹：用户将在管理器窗口中答复。")

        Add("Эта папка уже ждёт ответа.",
            "This folder is already waiting for an answer.",
            "Ця папка вже чекає на відповідь.",
            "Dieser Ordner wartet bereits auf eine Antwort.",
            "Questa cartella è già in attesa di risposta.",
            "Esta carpeta ya está esperando una respuesta.",
            "Ce dossier attend déjà une réponse.",
            "Esta pasta já está aguardando uma resposta.",
            "هذا المجلد ينتظر الإجابة بالفعل.",
            "यह फ़ोल्डर पहले से उत्तर की प्रतीक्षा में है।",
            "এই ফোল্ডারটি ইতিমধ্যেই উত্তরের অপেক্ষায় আছে।",
            "یہ فولڈر پہلے ہی جواب کا منتظر ہے۔",
            "此文件夹已在等待答复。")

        Add("Эта папка уже открыта для общего доступа.",
            "This folder is already shared.",
            "Ця папка вже відкрита для спільного доступу.",
            "Dieser Ordner ist bereits freigegeben.",
            "Questa cartella è già condivisa.",
            "Esta carpeta ya está compartida.",
            "Ce dossier est déjà partagé.",
            "Esta pasta já está compartilhada.",
            "هذا المجلد مشارك بالفعل.",
            "यह फ़ोल्डर पहले से साझा है।",
            "এই ফোল্ডারটি ইতিমধ্যেই শেয়ার করা আছে।",
            "یہ فولڈر پہلے ہی مشترک ہے۔",
            "此文件夹已被共享。")

        Add("Слишком много предложений ждёт ответа. Повторите позже.",
            "Too many proposals are waiting for an answer. Try again later.",
            "Забагато пропозицій чекає на відповідь. Повторіть пізніше.",
            "Zu viele Vorschläge warten auf eine Antwort. Versuchen Sie es später erneut.",
            "Troppe proposte sono in attesa di risposta. Riprova più tardi.",
            "Hay demasiadas propuestas esperando respuesta. Inténtelo de nuevo más tarde.",
            "Trop de propositions attendent une réponse. Réessayez plus tard.",
            "Há propostas demais aguardando resposta. Tente novamente mais tarde.",
            "هناك عدد كبير جدًا من الاقتراحات في انتظار الإجابة. أعد المحاولة لاحقًا.",
            "बहुत सारे प्रस्ताव उत्तर की प्रतीक्षा में हैं। कृपया बाद में पुनः प्रयास करें।",
            "অনেক বেশি প্রস্তাব উত্তরের অপেক্ষায় আছে। পরে আবার চেষ্টা করুন।",
            "بہت زیادہ تجاویز جواب کی منتظر ہیں۔ بعد میں دوبارہ کوشش کریں۔",
            "等待答复的提议过多，请稍后重试。")

        ' --- the observe verbs: share list, schedule list, exchange status (ticket L0319) ---

        Add("Общих папок: {0}.",
            "Shared folders: {0}.",
            "Спільних папок: {0}.",
            "Freigegebene Ordner: {0}.",
            "Cartelle condivise: {0}.",
            "Carpetas compartidas: {0}.",
            "Dossiers partagés : {0}.",
            "Pastas compartilhadas: {0}.",
            "المجلدات المشاركة: {0}.",
            "साझा किए गए फ़ोल्डर: {0}।",
            "শেয়ার করা ফোল্ডার: {0}।",
            "مشترک فولڈرز: {0}۔",
            "共享文件夹：{0}。")

        Add("Запланированных операций: {0}.",
            "Scheduled operations: {0}.",
            "Запланованих операцій: {0}.",
            "Geplante Vorgänge: {0}.",
            "Operazioni pianificate: {0}.",
            "Operaciones programadas: {0}.",
            "Opérations planifiées : {0}.",
            "Operações agendadas: {0}.",
            "العمليات المجدولة: {0}.",
            "निर्धारित संचालन: {0}।",
            "নির্ধারিত অপারেশন: {0}।",
            "طے شدہ کارروائیاں: {0}۔",
            "计划操作：{0}。")

        Add("Служба общего доступа не отвечает.",
            "The sharing service is not answering.",
            "Служба спільного доступу не відповідає.",
            "Der Freigabedienst antwortet nicht.",
            "Il servizio di condivisione non risponde.",
            "El servicio de uso compartido no responde.",
            "Le service de partage ne répond pas.",
            "O serviço de compartilhamento não está respondendo.",
            "خدمة المشاركة لا تستجيب.",
            "साझाकरण सेवा उत्तर नहीं दे रही है।",
            "শেয়ারিং পরিষেবা সাড়া দিচ্ছে না।",
            "شیئرنگ سروس جواب نہیں دے رہی۔",
            "共享服务没有响应。")

        Add("Учётная запись обмена не настроена.",
            "No exchange account is set up.",
            "Обліковий запис обміну не налаштовано.",
            "Es ist kein Austauschkonto eingerichtet.",
            "Nessun account di scambio è configurato.",
            "No hay ninguna cuenta de intercambio configurada.",
            "Aucun compte d'échange n'est configuré.",
            "Nenhuma conta de troca está configurada.",
            "لم يتم إعداد حساب تبادل.",
            "कोई एक्सचेंज खाता सेट नहीं है।",
            "কোনো এক্সচেঞ্জ অ্যাকাউন্ট সেট আপ করা নেই।",
            "کوئی ایکسچینج اکاؤنٹ سیٹ اپ نہیں ہے۔",
            "尚未设置交换账户。")

        Add("Учётная запись обмена настроена. Состояние: {0}. Запросов в ожидании: {1}.",
            "An exchange account is set up. State: {0}. Requests waiting: {1}.",
            "Обліковий запис обміну налаштовано. Стан: {0}. Запитів в очікуванні: {1}.",
            "Ein Austauschkonto ist eingerichtet. Zustand: {0}. Wartende Anfragen: {1}.",
            "Un account di scambio è configurato. Stato: {0}. Richieste in attesa: {1}.",
            "Hay una cuenta de intercambio configurada. Estado: {0}. Solicitudes en espera: {1}.",
            "Un compte d'échange est configuré. État : {0}. Demandes en attente : {1}.",
            "Uma conta de troca está configurada. Estado: {0}. Solicitações em espera: {1}.",
            "تم إعداد حساب تبادل. الحالة: {0}. الطلبات المنتظرة: {1}.",
            "एक एक्सचेंज खाता सेट है। स्थिति: {0}। प्रतीक्षारत अनुरोध: {1}।",
            "একটি এক্সচেঞ্জ অ্যাকাউন্ট সেট আপ করা আছে। অবস্থা: {0}। অপেক্ষমাণ অনুরোধ: {1}।",
            "ایک ایکسچینج اکاؤنٹ سیٹ اپ ہے۔ حالت: {0}۔ منتظر درخواستیں: {1}۔",
            "已设置交换账户。状态：{0}。等待中的请求：{1}。")

        ' --- scheduled operation CLI verbs (ticket L0345) ---

        Add("Операция по расписанию «{0}» создана.",
            "Scheduled operation «{0}» created.",
            "Заплановану операцію «{0}» створено.",
            "Geplanter Vorgang «{0}» erstellt.",
            "Operazione pianificata «{0}» creata.",
            "Operación programada «{0}» creada.",
            "Opération planifiée «{0}» créée.",
            "Operação agendada «{0}» criada.",
            "تم إنشاء العملية المجدولة «{0}».",
            "निर्धारित संचालन «{0}» बनाया गया।",
            "নির্ধারিত অপারেশন «{0}» তৈরি করা হয়েছে।",
            "طے شدہ کارروائی «{0}» بنائی گئی۔",
            "已创建计划操作「{0}」。")

        Add("Операция по расписанию «{0}» обновлена.",
            "Scheduled operation «{0}» updated.",
            "Заплановану операцію «{0}» оновлено.",
            "Geplanter Vorgang «{0}» aktualisiert.",
            "Operazione pianificata «{0}» aggiornata.",
            "Operación programada «{0}» actualizada.",
            "Opération planifiée «{0}» mise à jour.",
            "Operação agendada «{0}» atualizada.",
            "تم تحديث العملية المجدولة «{0}».",
            "निर्धारित संचालन «{0}» अपडेट किया गया।",
            "নির্ধারিত অপারেশন «{0}» আপডেট করা হয়েছে।",
            "طے شدہ کارروائی «{0}» اپ ڈیٹ کی گئی۔",
            "已更新计划操作「{0}」。")

        Add("Операция по расписанию «{0}» удалена.",
            "Scheduled operation «{0}» removed.",
            "Заплановану операцію «{0}» видалено.",
            "Geplanter Vorgang «{0}» entfernt.",
            "Operazione pianificata «{0}» rimossa.",
            "Operación programada «{0}» eliminada.",
            "Opération planifiée «{0}» supprimée.",
            "Operação agendada «{0}» removida.",
            "تمت إزالة العملية المجدولة «{0}».",
            "निर्धारित संचालन «{0}» हटाया गया।",
            "নির্ধারিত অপারেশন «{0}» সরানো হয়েছে।",
            "طے شدہ کارروائی «{0}» ہٹا دی گئی۔",
            "已移除计划操作「{0}」。")

        Add("Операция по расписанию «{0}» не найдена.",
            "Scheduled operation «{0}» not found.",
            "Заплановану операцію «{0}» не знайдено.",
            "Geplanter Vorgang «{0}» nicht gefunden.",
            "Operazione pianificata «{0}» non trovata.",
            "No se encontró la operación programada «{0}».",
            "Opération planifiée «{0}» introuvable.",
            "Operação agendada «{0}» não encontrada.",
            "لم يتم العثور على العملية المجدولة «{0}».",
            "निर्धारित संचालन «{0}» नहीं मिला।",
            "নির্ধারিত অপারেশন «{0}» পাওয়া যায়নি।",
            "طے شدہ کارروائی «{0}» نہیں ملی۔",
            "未找到计划操作「{0}」。")

        Add("Найдено несколько операций с именем «{0}». Укажите идентификатор.",
            "Multiple operations match «{0}». Specify the operation id.",
            "Знайдено кілька операцій з іменем «{0}». Укажіть ідентифікатор.",
            "Mehrere Vorgänge entsprechen «{0}». Geben Sie die Vorgangs-ID an.",
            "Più operazioni corrispondono a «{0}». Specifica l'ID dell'operazione.",
            "Varias operaciones coinciden con «{0}». Especifique el identificador.",
            "Plusieurs opérations correspondent à «{0}». Indiquez l'identifiant.",
            "Várias operações correspondem a «{0}». Informe o identificador.",
            "تطابق عدة عمليات الاسم «{0}». حدد معرف العملية.",
            "«{0}» से कई संचालन मेल खाते हैं। कृपया आईडी निर्दिष्ट करें।",
            "«{0}» নামের একাধিক অপারেশন পাওয়া গেছে। অনুগ্রহ করে আইডি উল্লেখ করুন।",
            "«{0}» سے متعدد کارروائیاں ملتی ہیں۔ براہ کرم آئی ڈی بتائیں۔",
            "多个操作匹配「{0}」，请指定操作 ID。")

        Add("Операция уже выполняется или ждёт очереди.",
            "An operation is already running or waiting in the queue.",
            "Операція вже виконується або чекає в черзі.",
            "Ein Vorgang wird bereits ausgeführt oder wartet in der Warteschlange.",
            "Un'operazione è già in esecuzione o in attesa in coda.",
            "Una operación ya está en ejecución o esperando en la cola.",
            "Une opération est déjà en cours ou en attente dans la file.",
            "Uma operação já está em execução ou aguardando na fila.",
            "هناك عملية قيد التشغيل بالفعل أو في انتظار دورها.",
            "एक संचालन पहले से चल रहा है या कतार में प्रतीक्षा कर रहा है।",
            "একটি অপারেশন ইতিমধ্যেই চলছে বা সারিতে অপেক্ষা করছে।",
            "ایک کارروائی پہلے ہی چل رہی ہے یا قطار میں منتظر ہے۔",
            "已有操作正在运行或在队列中等待。")

        Add("Операция «{0}» уже выполняется или ждёт очереди.",
            "Operation «{0}» is already running or waiting in the queue.",
            "Операція «{0}» вже виконується або чекає в черзі.",
            "Der Vorgang «{0}» wird bereits ausgeführt oder wartet in der Warteschlange.",
            "L'operazione «{0}» è già in esecuzione o in attesa in coda.",
            "La operación «{0}» ya está en ejecución o esperando en la cola.",
            "L'opération «{0}» est déjà en cours ou en attente dans la file.",
            "A operação «{0}» já está em execução ou aguardando na fila.",
            "العملية «{0}» قيد التشغيل بالفعل أو في انتظار دورها.",
            "संचालन «{0}» पहले से चल रहा है या कतार में प्रतीक्षा कर रहा है।",
            "অপারেশন «{0}» ইতিমধ্যেই চলছে বা সারিতে অপেক্ষা করছে।",
            "کارروائی «{0}» پہلے ہی چل رہی ہے یا قطار میں منتظر ہے۔",
            "操作「{0}」已在运行或在队列中等待。")

        Add("Поставлено в очередь операций: {0}.",
            "Queued operations: {0}.",
            "Поставлено в чергу операцій: {0}.",
            "In Warteschlange gestellte Vorgänge: {0}.",
            "Operazioni messe in coda: {0}.",
            "Operaciones puestas en cola: {0}.",
            "Opérations mises en file d'attente : {0}.",
            "Operações colocadas na fila: {0}.",
            "العمليات الموضوعة في قائمة الانتظار: {0}.",
            "कतार में रखे गए संचालन: {0}।",
            "সারিতে রাখা অপারেশন: {0}।",
            "قطار میں شامل کی گئی کارروائیاں: {0}۔",
            "已加入队列的操作：{0}。")

        Add("Выполнено операций: {0}; с ошибками: {1}.",
            "Operations completed: {0}; failed: {1}.",
            "Виконано операцій: {0}; з помилками: {1}.",
            "Abgeschlossene Vorgänge: {0}; fehlgeschlagen: {1}.",
            "Operazioni completate: {0}; con errori: {1}.",
            "Operaciones completadas: {0}; con errores: {1}.",
            "Opérations terminées : {0} ; en erreur : {1}.",
            "Operações concluídas: {0}; com falhas: {1}.",
            "العمليات المكتملة: {0}؛ الفاشلة: {1}.",
            "पूर्ण संचालन: {0}; विफल: {1}।",
            "সম্পন্ন অপারেশন: {0}; ব্যর্থ: {1}।",
            "مکمل کارروائیاں: {0}؛ ناکام: {1}۔",
            "已完成操作：{0}；失败：{1}。")

        Add("Журнал операций по расписанию пуст.",
            "Scheduled operations journal is empty.",
            "Журнал операцій за розкладом порожній.",
            "Das Protokoll der geplanten Vorgänge ist leer.",
            "Il registro delle operazioni pianificate è vuoto.",
            "El registro de operaciones programadas está vacío.",
            "Le journal des opérations planifiées est vide.",
            "O registro de operações agendadas está vazio.",
            "سجل العمليات المجدولة فارغ.",
            "निर्धारित संचालन लॉग खाली है।",
            "নির্ধারিত অপারেশন লগ খালি।",
            "طے شدہ کارروائیوں کا جریدہ خالی ہے۔",
            "计划操作日志为空。")

    End Sub

End Class
